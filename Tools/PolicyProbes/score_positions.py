"""Score fair diagnostic positions without training or changing a policy.

Outputs joint probabilities over the two decision stages, not match win rates.
Outcome labels are analysis only and are never supplied to the frozen actor.
"""
import argparse
import hashlib
from importlib.metadata import version
import json
from pathlib import Path
import shutil
import subprocess
import sys

import numpy as np

TRAINING = Path(__file__).resolve().parents[1] / 'Training'
sys.path.insert(0, str(TRAINING))
from evaluate_suite import frozen_actor
from frozen_policy import evaluation_weights, SAMPLING_MODES
from mlagents.torch_utils import torch
from supervisor import ROOT_MARKER, read_json, size
from training_contract import OBSERVATIONS, ACTIONS


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def score(manifest, probe, cases, destination, root, transfer=None, modes=('Policy', 'Hard'), provenance=None):
    root = root.absolute()
    if (root.is_symlink() or root.resolve() != root or read_json(root / ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1' or
            not destination.resolve().is_relative_to(root) or destination.exists()):
        raise ValueError('Use a new artifact in the marked shared training root.')
    if not modes or len(set(modes)) != len(modes) or any(mode not in SAMPLING_MODES for mode in modes):
        raise ValueError('Choose distinct supported inference modes.')
    # Small diagnostic artifacts share the main allowance and final-save reserve.
    if size(root) >= 20_000_000_000 - 513_048_576 or shutil.disk_usage(root).free < 20_512_000_000:
        raise ValueError('Preserve the shared storage reserve before evaluating positions.')
    assembly = probe.parent / (probe.name+'.dll')
    sources = {str(path): sha(path) for path in (Path(__file__), manifest, cases, probe,
        assembly, TRAINING / 'frozen_policy.py', TRAINING / 'playtest_sampling.py',
        TRAINING / 'evaluate_suite.py', TRAINING / 'training_contract.py')}
    if transfer is not None:
        sources[str(transfer)] = sha(transfer)
    case_provenance = None
    if provenance is not None:
        if not provenance.resolve().is_relative_to(root / 'benchmarks') or provenance.stat().st_size > 1_048_576:
            raise ValueError('Use bounded replay provenance inside the shared benchmark folder.')
        case_provenance = read_json(provenance)
        from extract_replay_positions import MEASUREMENT
        if (case_provenance.get('version') != 1 or case_provenance.get('measurement') != MEASUREMENT or
                case_provenance.get('casesSha256') != sha(cases) or case_provenance.get('probeAssemblySha256') != sha(assembly) or
                case_provenance.get('boardSize') != 7 or not 1 <= case_provenance.get('selectedPositions', 0) <= 32 or
                not 1 <= case_provenance.get('recordedGames', 0) <= 8 or
                len(case_provenance.get('sources', [])) != case_provenance['recordedGames']):
            raise ValueError('Replay provenance does not match the diagnostic inputs.')
        sources[str(provenance)] = sha(provenance)
        for replay in case_provenance['sources']:
            path = (root / replay['path']).resolve()
            if not path.is_relative_to(root / 'benchmarks') and not (path.is_relative_to(root / 'runs') and 'evaluations' in path.relative_to(root).parts):
                raise ValueError('Replay provenance cannot reference human-match or external files.')
            if replay.get('verifiedTransitions') != replay.get('frames', 0) - 1 or sha(path) != replay.get('sha256'):
                raise ValueError('A verified replay source changed after extraction.')
            sources[str(path)] = replay['sha256']
    result = subprocess.run([str(probe), '--cases', str(cases)], capture_output=True, check=True, timeout=60)
    payload = json.loads(result.stdout)
    if (payload.get('version') != 1 or payload.get('schema') != 2 or payload.get('encoding') != 'float32-le' or
            payload.get('observationSize') != OBSERVATIONS or payload.get('actionCount') != ACTIONS or
            payload.get('inputSha256') != sha(cases) or payload.get('assemblySha256') != sha(assembly) or
            not 1 <= len(payload.get('cases', [])) <= 32):
        raise ValueError('Probe output differs from the compatible frozen policy contract.')
    rules = {case['rulesVersion'] for case in payload['cases']}
    if len(rules) != 1:
        raise ValueError('Keep position scores from different rulesets in separate receipts.')
    rules_version = rules.pop()
    if case_provenance is not None and (rules_version != case_provenance.get('rulesVersion') or
            len(payload['cases']) != case_provenance['selectedPositions'] or
            [c['name'] for c in payload['cases']] != [c['name'] for c in case_provenance.get('caseRecords', [])]):
        raise ValueError('Replay case identities or target rules changed after extraction.')
    if case_provenance is None and any(case.get('recordedChoices') is not None for case in payload['cases']):
        raise ValueError('Recorded action labels require verified replay provenance.')
    actor, identity = frozen_actor(manifest, rules_version, read_json(transfer) if transfer else None)
    import base64
    summaries = []
    for case in payload['cases']:
        decisions = case['decisions']
        if not decisions or decisions[0]['source'] != -1:
            raise ValueError('A position needs source-selection followed by its legal action rows.')
        recorded = case.get('recordedChoices')
        if recorded is not None:
            if (not isinstance(recorded, list) or not 1 <= len(recorded) <= 122 or
                    any(not isinstance(choice, dict) or type(choice.get('source')) is not int or
                        type(choice.get('encoded')) is not int for choice in recorded)):
                raise ValueError('Invalid recorded macro-action labels.')
            recorded = {(choice['source'], choice['encoded']) for choice in recorded}
            if len(recorded) != len(case['recordedChoices']):
                raise ValueError('Recorded choices must not repeat equivalent two-stage paths.')
        vectors, masks = [], []
        for row in decisions:
            vector = np.frombuffer(base64.b64decode(row['model'], validate=True), dtype='<f4')
            available = row['available']
            if (vector.shape != (OBSERVATIONS,) or not np.isfinite(vector).all() or not available or
                    any(type(action) is not int or not 0 <= action < ACTIONS for action in available) or
                    len(set(available)) != len(available)):
                raise ValueError('A position contains invalid model data or action masks.')
            mask = np.ones(ACTIONS, dtype=bool)
            mask[available] = False
            vectors.append(vector); masks.append(mask)
        # Only fair model vectors and masks reach inference. Names/outcome labels
        # stay in the scoring code below, outside the observation boundary.
        probabilities = actor.probabilities(np.stack(vectors), np.stack(masks))
        for mode in modes:
            weights = evaluation_weights(probabilities, mode)
            actions = [dict(kind='EndTurn', probability=float(weights[0, ACTIONS-1]), source=-1, encoded=ACTIONS-1)]
            for index, row in enumerate(decisions[1:], 1):
                source = row['source']
                if source not in decisions[0]['available'] or source == ACTIONS-1:
                    raise ValueError('Conditional row is outside the root action mask.')
                for label in row['analysis']:
                    if label['encoded'] not in row['available']:
                        raise ValueError('Analysis label is outside its legal action mask.')
                    actions.append(dict(label, source=source, probability=float(weights[0, source] * weights[index, label['encoded']])))
            if abs(sum(a['probability'] for a in actions)-1) > 1e-6:
                raise ValueError('The joint action probabilities do not sum to one.')
            def ranged(action):
                return (action['kind'] == 'Attack' and max(abs(action['actorPosition'] % 7 - action['destination'] % 7),
                    abs(action['actorPosition'] // 7 - action['destination'] // 7)) > 1)
            actual = [a for a in actions if recorded is not None and (a['source'], a['encoded']) in recorded]
            if recorded is not None and len(actual) != len(recorded):
                raise ValueError('Recorded choices must match the fair macro-action labels.')
            summary = dict(name=case['name'], seat=case['seat'], firstSeat=case['firstSeat'], mode=mode,
                visibleWinProbability=sum(a['probability'] for a in actions if a.get('visibleWin')),
                visibleKillProbability=sum(a['probability'] for a in actions if a.get('killedVisibleUnit')),
                endTurnProbability=sum(a['probability'] for a in actions if a['kind'] == 'EndTurn'),
                moveProbability=sum(a['probability'] for a in actions if a['kind'] == 'Move'),
                attackProbability=sum(a['probability'] for a in actions if a['kind'] == 'Attack'),
                availableRangedAttacks=sum(ranged(a) for a in actions),
                rangedAttackProbability=sum(a['probability'] for a in actions if ranged(a)),
                visibleRangedKillProbability=sum(a['probability'] for a in actions if ranged(a) and a.get('killedVisibleUnit')),
                actions=sorted(actions, key=lambda a: -a['probability']))
            if recorded is not None:
                summary.update(recordedActionProbability=sum(a['probability'] for a in actual),
                    recordedActionKinds=sorted({a['kind'] for a in actual}),
                    recordedActionRanged=any(ranged(a) for a in actual),
                    recordedChoiceCount=len(recorded), recordedChoices=case['recordedChoices'])
            summaries.append(summary)
    if any(sha(Path(path)) != fingerprint for path, fingerprint in sources.items()):
        raise ValueError('Probe sources or frozen inputs changed during evaluation.')
    receipt = dict(version=3, state='completed', rulesVersion=rules_version, boardSize=7, observationSchema=payload['schema'],
        measurement=case_provenance['measurement'] if case_provenance else 'synthetic-position-joint-action-probabilities',
        candidate=identity, sources=sources, probeAssemblySha256=payload['assemblySha256'],
        packages={name: version(name) for name in ('torch', 'mlagents', 'numpy')}, results=summaries)
    if case_provenance is not None:
        receipt['caseProvenance'] = case_provenance
    contents = json.dumps(receipt, indent=2)+'\n'
    if len(contents.encode()) > 1_048_576:
        raise ValueError('Keep position-score receipts within 1 MiB.')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open('x') as output:
        output.write(contents)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('candidate-manifest', 'probe', 'cases', 'destination', 'root'):
        parser.add_argument('--'+name, type=Path, required=True)
    parser.add_argument('--candidate-transfer', type=Path)
    parser.add_argument('--case-provenance', type=Path)
    parser.add_argument('--difficulty', choices=SAMPLING_MODES, action='append')
    args = parser.parse_args()
    torch.set_num_threads(1)
    receipt = score(args.candidate_manifest, args.probe, args.cases, args.destination, args.root,
                    args.candidate_transfer, tuple(args.difficulty or ('Policy', 'Hard')), args.case_provenance)
    print(json.dumps(dict(state=receipt['state'], rows=len(receipt['results']), candidate=receipt['candidate']['actorId'])))
