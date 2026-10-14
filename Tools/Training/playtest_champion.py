"""Select and retain an evaluated local playtest champion independently of training.

Selection never edits learner weights or publishes a game release. Automatic
direct challenges establish a winner over the incumbent while keeping the
fixed-reference benchmark as a separate diagnostic. Legacy gates remain available.
"""
import argparse
import hashlib
import json
from pathlib import Path
import time

from training_contract import run_rules_version, OPENING_ECONOMY_VERSION

OWNER = 'BlockNations.PlaytestChampion.v1'
POINTER = 'playtest-champion.json'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def owned_path(run, relative, directory=None):
    if not isinstance(relative, str) or Path(relative).is_absolute() or '..' in Path(relative).parts:
        raise ValueError('Champion evidence must stay inside its owned run.')
    path = run/relative
    if any(parent.is_symlink() for parent in (path, *path.parents) if parent.is_relative_to(run)) or not path.is_file():
        raise ValueError('Champion evidence is missing or linked; it was not replaced with a newer model.')
    if directory and not path.is_relative_to(run/directory):
        raise ValueError('Champion models must be retained outside rolling checkpoints.')
    return path


def checked_evaluation(run, manifest_path, evaluation_path):
    manifest_path = owned_path(run, str(Path(manifest_path).absolute().relative_to(run)), 'frozen-evaluations')
    evaluation_path = owned_path(run, str(Path(evaluation_path).absolute().relative_to(run)))
    manifest = json.loads(manifest_path.read_text())
    evaluation = json.loads(evaluation_path.read_text())
    if manifest.get('diagnosticOnly') or evaluation.get('diagnosticOnly'):
        raise ValueError('Diagnostic actors and visibility suites cannot promote a playable champion.')
    contract = dict(rulesVersion=run_rules_version(run), boardSize=json.loads((run/'run.json').read_text())['boardSize'],
                    openingEconomyVersion=OPENING_ECONOMY_VERSION)
    if any(manifest.get(key) != value or evaluation.get(key) != value for key, value in contract.items()):
        raise ValueError('Champion and completed evaluation must use this run\'s rules, board and opening.')
    checkpoint = owned_path(run, str(Path(manifest['checkpoint']).relative_to(run)), 'frozen-evaluations')
    config = owned_path(run, str(Path(manifest['trainerConfig']).relative_to(run)), 'frozen-evaluations')
    candidate = evaluation.get('candidate', {})
    if (checkpoint.parent != manifest_path.parent or config.parent != checkpoint.parent or
            digest(checkpoint) != manifest['sha256'] or candidate.get('sha256') != manifest['sha256'] or
            candidate.get('configSha256') != digest(config) or candidate.get('actorId') != manifest.get('actorId') or
            evaluation.get('state') != 'completed'):
        raise ValueError('Champion weights/configuration do not match a completed evaluation.')
    current_network = json.loads((run/'trainer.yaml').read_text())['behaviors']['BlockNationsSeatV2']['network_settings']
    network = json.loads(config.read_text())['behaviors']['BlockNationsSeatV2']['network_settings']
    if current_network != network:
        raise ValueError('Champion network differs from the native playtest contract.')
    cases, results = evaluation.get('cases', []), evaluation.get('results', [])
    if not cases or len(cases) != len(results) or len({case['name'] for case in cases}) != len(cases):
        raise ValueError('Champion benchmark is incomplete or has duplicate cases.')
    for case, result in zip(cases, results):
        expected = dict(seed=case['seed'], difficulty=case['difficulty'], referenceDifficulty=case['referenceDifficulty'])
        reference = case['reference']
        if reference['kind'] == 'frozen':
            expected['referenceSha256'] = reference['sha256']
        elif reference['kind'] == 'self':
            expected['referenceSha256'] = manifest['sha256']
        elif reference['kind'] == 'tactical':
            expected.update(reference=reference['implementation'], referenceWork=reference['workBudget'],
                            referenceRecruitType=reference['recruitType'])
        else:
            raise ValueError('Champion benchmark contains an unknown reference.')
        records_path = owned_path(run, str((evaluation_path.parent/case['name']/'evaluation.json').relative_to(run)))
        full = json.loads(records_path.read_text())
        records = full.get('records', [])
        if (result.get('case') != case['name'] or
                any(result.get(key) != value for key, value in contract.items()) or
                result.get('candidateSha256') != manifest['sha256'] or
                any(result.get(key) != value for key, value in expected.items()) or
                result.get('games') != case['gamesPerCell']*4 or len(records) != result['games'] or
                any(full.get(key) != value for key, value in result.items() if key != 'case') or
                result['wins']+result['losses']+result['interruptions'] != result['games']):
            raise ValueError('Champion benchmark has missing or inconsistent match records.')
        for first in (0, 1):
            for seat in (0, 1):
                if sum(record['firstSeat'] == first and record['candidateSeat'] == seat for record in records) != case['gamesPerCell']:
                    raise ValueError('Champion benchmark must balance colours and starting roles.')
        wins = sum(not r['interrupted'] and r['winner'] == r['candidateSeat'] for r in records)
        interruptions = sum(r['interrupted'] for r in records)
        if wins != result['wins'] or interruptions != result['interruptions']:
            raise ValueError('Champion totals disagree with complete match records.')
        for starts in (True, False):
            selected = [r for r in records if (r['candidateSeat'] == r['firstSeat']) == starts]
            role = next((r for r in result['startingPositionResults'] if r['candidateStarts'] == starts), None)
            if (role is None or role['games'] != len(selected) or
                    role['wins'] != sum(not r['interrupted'] and r['winner'] == r['candidateSeat'] for r in selected) or
                    role['interruptions'] != sum(r['interrupted'] for r in selected) or
                    role['wins']+role['losses']+role['interruptions'] != role['games']):
                raise ValueError('Champion starting-role results disagree with match records.')
    return checkpoint, evaluation


def resolve_champion(run):
    run = Path(run).absolute()
    owned = json.loads((run/'run.json').read_text())
    if owned.get('owner') != 'BlockNations.LocalTraining.v1' or owned.get('schema') != 2:
        raise ValueError('Choose an owned compatible training run.')
    pointer = run/POINTER
    if not pointer.exists() and not pointer.is_symlink():
        return None
    pointer = owned_path(run, POINTER)
    selection = json.loads(pointer.read_text())
    if selection.get('owner') != OWNER or selection.get('version') != 1 or selection.get('runId') != run.name:
        raise ValueError('Champion selection belongs to another run.')
    manifest = owned_path(run, selection['candidateManifest'], 'frozen-evaluations')
    evaluation = owned_path(run, selection['evaluation'])
    if digest(manifest) != selection['manifestSha256'] or digest(evaluation) != selection['evaluationSha256']:
        raise ValueError('Selected champion evidence changed; preserve and inspect it.')
    checkpoint, receipt = checked_evaluation(run, manifest, evaluation)
    if (receipt['suiteId'] != selection['suiteId'] or checkpoint.name != selection['checkpoint'] or
            digest(checkpoint) != selection['checkpointSha256']):
        raise ValueError('Selected champion benchmark identity changed.')
    if selection.get('headToHead'):
        for key, relative in selection['headToHead'].items():
            evidence = owned_path(run, relative)
            if digest(evidence) != selection['headToHeadSha256'][key]:
                raise ValueError('Selected champion direct-match evidence changed.')
            checked_evaluation(run, manifest, evidence)
    return checkpoint


def preferred_checkpoint(run, source='Champion'):
    if source not in ('Champion', 'Latest'):
        raise ValueError('Choose Champion or Latest checkpoint selection.')
    if source == 'Champion':
        champion = resolve_champion(run)
        if champion is not None:
            return champion, 'evaluated champion'
    from playtest import latest_checkpoint
    return latest_checkpoint(run), 'latest completed checkpoint'


def select(run, manifest, evaluation, *, initialize=False, confirmation=None, margins=None, head_to_head=None, required_modes=None):
    if required_modes is not None and head_to_head is None:
        raise ValueError('Explicit playing modes require replicated direct evidence.')
    run = Path(run).absolute()
    owned = json.loads((run/'run.json').read_text())
    if owned.get('owner') != 'BlockNations.LocalTraining.v1' or owned.get('schema') != 2:
        raise ValueError('Choose an owned compatible training run.')
    checkpoint, candidate = checked_evaluation(run, manifest, evaluation)
    # A finite strictly loaded actor is required before changing the pointer.
    from evaluate_suite import frozen_actor
    frozen_actor(manifest, run_rules_version(run))
    previous = resolve_champion(run)
    if previous is None:
        if confirmation is not None or head_to_head is not None:
            raise ValueError('Initialize an evaluated champion before replicated promotion.')
        if not initialize:
            raise ValueError('Initialize an explicitly selected evaluated champion first.')
    else:
        if initialize:
            raise ValueError('A champion already exists; use the regression gate.')
        selection = json.loads((run/POINTER).read_text())
        incumbent = json.loads(owned_path(run, selection['evaluation']).read_text())
        if candidate['suiteId'] != incumbent['suiteId'] or candidate['cases'] != incumbent['cases']:
            raise ValueError('Promotion requires identical benchmark opponents, seeds, sampling and implementation.')
        if head_to_head is not None:
            if confirmation is not None or set(head_to_head) != {'primary', 'confirmation'}:
                raise ValueError('Choose one complete promotion criterion.')
            from champion_matches import assess as assess_direct
            packs = [checked_evaluation(run, manifest, head_to_head[key])[1]
                     for key in ('primary', 'confirmation')]
            evidence = assess_direct(*packs, incumbent['candidate'], candidate['candidate'],
                                     required_modes=('Hard', 'Policy') if required_modes is None else required_modes)
            if not evidence['accepted']:
                raise ValueError('Candidate did not improve direct matches: ' + '; '.join(evidence['reasons']))
        elif confirmation is not None:
            from tournament_selection import assess
            old_checkpoint, old_repeat = checked_evaluation(run, run/selection['candidateManifest'], confirmation['incumbent'])
            new_checkpoint, new_repeat = checked_evaluation(run, manifest, confirmation['candidate'])
            if old_checkpoint != previous or new_checkpoint != checkpoint:
                raise ValueError('Confirmation must evaluate the exact compared actors.')
            evidence = assess(incumbent, candidate, old_repeat, new_repeat, margins)
            if not evidence['accepted']:
                raise ValueError('Candidate did not improve replicated benchmarks: ' + '; '.join(evidence['reasons']))
        else:
            evidence = None
        improved = confirmation is not None or head_to_head is not None
        for case, new, old in zip(candidate['cases'], candidate['results'], incumbent['results']):
            if confirmation is not None or head_to_head is not None:
                break  # The replicated gate includes outcome/role/interruption checks.
            if case['reference']['kind'] == 'self':
                continue  # Self-play is a balance diagnostic, not strength against a fixed opponent.
            if new['interruptions'] > old['interruptions']:
                raise ValueError('Candidate increased interrupted games; the champion was preserved.')
            for starts in (True, False):
                new_role = next(r for r in new['startingPositionResults'] if r['candidateStarts'] == starts)
                old_role = next(r for r in old['startingPositionResults'] if r['candidateStarts'] == starts)
                if new_role['wins'] < old_role['wins']:
                    raise ValueError('Candidate regressed against a fixed opponent or starting role; champion preserved.')
                improved |= new_role['wins'] > old_role['wins']
        if not improved:
            raise ValueError('Candidate did not improve fixed-reference results; the champion was preserved.')
    selection = dict(owner=OWNER, version=1, runId=run.name,
                     candidateManifest=str(Path(manifest).absolute().relative_to(run)),
                     manifestSha256=digest(Path(manifest)), evaluation=str(Path(evaluation).absolute().relative_to(run)),
                     evaluationSha256=digest(Path(evaluation)), suiteId=candidate['suiteId'],
                     checkpoint=checkpoint.name, checkpointSha256=digest(checkpoint), selectedUnix=time.time(),
                     strengthStatus='best selected evaluated candidate; human strength and balance remain unproven')
    if confirmation is not None:
        selection['confirmation'] = {key: str(Path(value).relative_to(run)) for key, value in confirmation.items()}
        selection['promotionEvidence'] = evidence
    if head_to_head is not None:
        selection['headToHead'] = {key: str(Path(value).relative_to(run)) for key, value in head_to_head.items()}
        selection['headToHeadSha256'] = {key: digest(Path(value)) for key, value in head_to_head.items()}
        selection['promotionEvidence'] = evidence
        selection['strengthStatus'] = 'replicated direct winner over previous champion; fixed-reference results are diagnostics'
        if evidence.get('requiredModes') == ['Hard']:
            selection['strengthStatus'] = 'replicated Hard winner; raw-policy sampling is a separate diagnostic'
    from playtest import atomic_json
    if previous is not None:
        history = run/'champion-history'
        history.mkdir(exist_ok=True)
        atomic_json(history/(str(time.time_ns())+'.json'), json.loads((run/POINTER).read_text()))
    atomic_json(run/POINTER, selection)
    return selection


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--evaluation', type=Path, required=True)
    parser.add_argument('--initialize', action='store_true')
    args = parser.parse_args()
    print(json.dumps(select(args.run, args.manifest, args.evaluation, initialize=args.initialize)))
