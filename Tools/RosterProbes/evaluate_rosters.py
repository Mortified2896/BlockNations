"""Measure frozen-policy recruitment ablations, never restrict training.

The same fair observations and shared game rules are used in every match.
Only the evaluated candidate's recruitment mask changes; the reference is fixed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import signal
import shutil
import sys

import numpy as np
from mlagents_envs.base_env import DecisionSteps

TRAINING = Path(__file__).resolve().parents[1] / 'Training'
sys.path.insert(0, str(TRAINING))
from evaluate_policy import evaluate
from evaluate_suite import prepare, worker_digest
from mlagents.torch_utils import torch
from supervisor import ROOT_MARKER, atomic_json, read_json, size
from training_contract import ACTIONS, OBSERVATIONS

MEASUREMENT = 'frozen-policy-recruitment-ablation-v1'
RECRUIT_START, RECRUIT_END = 242, 258


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def allowed_roster(value):
    if value is None:
        return None
    if (not isinstance(value, list) or not 1 <= len(value) <= 16 or
            any(not isinstance(name, str) or not re.fullmatch(r'[a-z][a-z0-9_-]{0,63}', name) for name in value) or
            len(set(value)) != len(value)):
        raise ValueError('Choose a nonempty, unique list of public recruit type names, or null for unrestricted.')
    return tuple(sorted(value))


class RosterProbeActor:
    """Read-only frozen actor with an explicitly labelled evaluation-only mask."""
    def __init__(self, actor, allowed):
        self.actor, self.allowed = actor, allowed_roster(allowed)
        if self.allowed is None:
            raise ValueError('Use the original actor for an unrestricted control.')
        self.sha256 = actor.sha256
        self.name = actor.name + '-roster-probe'

    def choose(self, decisions, random, difficulty, *, environment, seat):
        roster, enabled = tuple(environment.roster), tuple(environment.enabled_roster)
        if (len(roster) > RECRUIT_END - RECRUIT_START or len(set(roster)) != len(roster) or
                not set(self.allowed).issubset(enabled) or not set(enabled).issubset(roster)):
            raise ValueError('A restricted recruit is absent or disabled in the authoritative worker roster.')
        if (len(decisions.obs) != 1 or decisions.obs[0].shape != (len(decisions), OBSERVATIONS) or
                len(decisions.action_mask) != 1 or decisions.action_mask[0].shape != (len(decisions), ACTIONS) or
                decisions.action_mask[0].dtype != np.bool_):
            raise ValueError('Recruitment probes require the existing fair observation/action contract.')
        mask = decisions.action_mask[0].copy()
        forbidden = [RECRUIT_START + index for index, name in enumerate(roster) if name not in self.allowed]
        mask[:, forbidden] = True
        if mask.all(axis=1).any():
            raise ValueError('Recruit restriction removed every legal choice; do not invent an action.')
        projected = DecisionSteps(decisions.obs, decisions.reward, decisions.agent_id, [mask],
                                  decisions.group_id, decisions.group_reward)
        # Delegate the ordinary sampling path. No world state or named-unit
        # preferences are added to numeric observations or frozen weights.
        actions = self.actor.choose(projected, random, difficulty, environment=environment, seat=seat)
        if (actions.shape != (len(decisions), 1) or
                any(not 0 <= int(choice) < ACTIONS or mask[index, int(choice)]
                    for index, choice in enumerate(actions[:, 0]))):
            raise ValueError('Frozen actor returned a forbidden recruitment or illegal action.')
        return actions


def budget_guard(root):
    if size(root) >= 20_000_000_000 - 512_000_000 or shutil.disk_usage(root).free < 20_512_000_000:
        raise ValueError('Preserve the shared 20 GB allowance and final-checkpoint/free-disk reserves.')


def run(candidate_manifest, suite_path, worker, destination, root):
    root = root.absolute()
    destination = destination.absolute()
    if (root.is_symlink() or root.resolve() != root or
            read_json(root / ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1' or
            destination.resolve() != destination or not destination.is_relative_to(root / 'benchmarks') or
            destination.exists()):
        raise ValueError('Use a new canonical benchmark directory in the marked shared training root.')
    budget_guard(root)
    suite = read_json(suite_path)
    if (suite.get('measurement') != MEASUREMENT or not isinstance(suite.get('cases'), list) or
            any(not isinstance(case, dict) or 'allowedRecruits' not in case for case in suite['cases'])):
        raise ValueError('Recruitment ablations require their own explicit measurement contract.')
    restrictions = [allowed_roster(case.get('allowedRecruits')) for case in suite.get('cases', [])]
    candidate, identity, prepared, definition = prepare(candidate_manifest, suite_path)
    actual_worker = worker_digest(worker)
    if actual_worker != definition['workerSha256']:
        raise ValueError('Worker differs from the fixed ablation definition.')
    pinned = {str(Path(path).absolute()): digest(path) for path in
              (Path(__file__), candidate_manifest, suite_path)}
    for case in suite['cases']:
        reference = case.get('reference', {})
        if reference.get('kind') == 'frozen':
            manifest = Path(reference['manifest'])
            pinned[str(manifest.absolute())] = digest(manifest)
    for module in definition['evaluatorSources']:
        path = TRAINING / module
        pinned[str(path)] = digest(path)
        if pinned[str(path)] != definition['evaluatorSources'][module]:
            raise ValueError('Evaluator source changed while preparing the ablation.')
    manifest = read_json(candidate_manifest)
    for path in (manifest['checkpoint'], manifest['trainerConfig']):
        pinned[str(Path(path).absolute())] = digest(path)
    if (pinned[str(Path(manifest['checkpoint']).absolute())] != identity['sha256'] or
            pinned[str(Path(manifest['trainerConfig']).absolute())] != identity['configSha256']):
        raise ValueError('Candidate changed while preparing the ablation.')
    for case, canonical in zip(suite['cases'], definition['cases']):
        if case.get('reference', {}).get('kind') == 'frozen':
            reference = read_json(Path(case['reference']['manifest']))
            for path in (reference['checkpoint'], reference['trainerConfig']):
                pinned[str(Path(path).absolute())] = digest(path)
            if (pinned[str(Path(reference['checkpoint']).absolute())] != canonical['reference']['sha256'] or
                    pinned[str(Path(reference['trainerConfig']).absolute())] != canonical['reference']['configSha256']):
                raise ValueError('Reference changed while preparing the ablation.')
    for case, restriction in zip(definition['cases'], restrictions):
        case['allowedRecruits'] = list(restriction) if restriction is not None else None
    definition.update(measurement=MEASUREMENT, adapterSha256=digest(Path(__file__)))
    definition.pop('suiteId')
    definition['suiteId'] = hashlib.sha256(json.dumps(definition, sort_keys=True).encode()).hexdigest()
    def verify():
        budget_guard(root)
        if (any(digest(path) != value for path, value in pinned.items()) or
                worker_digest(worker) != actual_worker):
            raise ValueError('Frozen inputs, evaluator sources or worker changed during ablation.')
    verify()
    destination.mkdir(parents=True, exist_ok=False)
    receipt = dict(definition, candidate=identity, pinnedSources=pinned, state='running', results=[],
                   caveat='Restrictions test latent responses in a frozen policy, not unit balance or trained competence. '
                          'No quotas, rewards, restrictions or recorded evaluation positions enter training.')
    atomic_json(destination / 'suite.json', receipt)
    try:
        for (case, reference), restriction in zip(prepared, restrictions):
            verify()
            actor = candidate if restriction is None else RosterProbeActor(candidate, list(restriction))
            result = evaluate(worker, destination / case['name'], actor, reference,
                              games_per_cell=case['gamesPerCell'], seed=case['seed'],
                              difficulty=case['difficulty'], reference_difficulty=case['referenceDifficulty'],
                              rules_version=definition['rulesVersion'])
            verify()
            result.pop('records')  # Complete records remain in each case's evaluation.json.
            receipt['results'].append(dict(case=case['name'], allowedRecruits=case['allowedRecruits'], **result))
            atomic_json(destination / 'suite.json', receipt)
        receipt['state'] = 'completed'
    except BaseException as error:
        receipt.update(state='interrupted' if isinstance(error, KeyboardInterrupt) else 'failed',
                       error=str(error) or type(error).__name__)
        raise
    finally:
        atomic_json(destination / 'suite.json', receipt)
    return receipt


if __name__ == '__main__':
    def stop_requested(signum, frame):
        raise KeyboardInterrupt('Ablation stop requested.')
    signal.signal(signal.SIGTERM, stop_requested)
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('candidate-manifest', 'suite', 'worker', 'destination', 'root'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    torch.set_num_threads(1)
    receipt = run(args.candidate_manifest, args.suite, args.worker, args.destination, args.root)
    print(json.dumps(dict(state=receipt['state'], cases=len(receipt['results']), suiteId=receipt['suiteId'])))
