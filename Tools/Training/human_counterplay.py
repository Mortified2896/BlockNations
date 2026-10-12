"""Prepare a fair learned challenger from completed human wins for isolated trials.

Only the opposing policy changes. Never replace the defender's legal actions,
rewards or observations with a demonstration, and never change the owner's run.
"""
import copy
import hashlib
import re
from pathlib import Path
import numpy as np

from human_imitation import decode_game
from playtest import atomic_json, read_json


def fit(policy, examples, *, batches=256, seed=42):
    from mlagents.torch_utils import torch
    from mlagents.trainers.torch_entities.agent_action import AgentAction
    if not examples or type(batches) is not int or not 1 <= batches <= 1024:
        raise ValueError('Fit a bounded challenger only from completed compatible human wins.')
    rng = np.random.default_rng(seed)
    optimizer = torch.optim.Adam(policy.actor.parameters(), lr=.0001)
    def stats(obs, masks, actions):
        return policy.actor.get_stats([torch.as_tensor(obs)],
            AgentAction(None, [torch.as_tensor(actions[:, None])]), masks=torch.as_tensor(masks))
    def loss():
        with torch.no_grad():
            return float(np.mean([float(-stats(obs, masks, actions)['log_probs'].flatten().mean())
                                  for _, obs, masks, actions in examples]))
    before = loss()
    for _ in range(batches):
        _, obs, masks, actions = examples[int(rng.integers(len(examples)))]
        indices = rng.integers(len(actions), size=64)
        objective = -stats(obs[indices], masks[indices], actions[indices])['log_probs'].flatten().mean()
        if not torch.isfinite(objective):
            raise ValueError('Human challenger learning became non-finite.')
        optimizer.zero_grad(set_to_none=True)
        objective.backward()
        torch.nn.utils.clip_grad_norm_(policy.actor.parameters(), .5)
        optimizer.step()
    after = loss()
    if after >= before:
        raise ValueError('Human challenger did not learn its recorded moves.')
    return dict(winningGames=len(examples), uniqueSamples=sum(len(x[3]) for x in examples),
                batches=batches, examples=batches*64, initialLoss=before, finalLoss=after,
                validity='training-set fit only; defensive utility requires separate evaluation')


def prepare(root, source_run, parent_manifest, worker, name, *, batches=256, seed=42):
    from mlagents.torch_utils import torch
    from mlagents.trainers.policy.torch_policy import TorchPolicy
    from mlagents.trainers.settings import NetworkSettings
    from mlagents.trainers.torch_entities.networks import SimpleActor
    from dotnet_environment import SPEC
    from evaluate_suite import frozen_actor
    from match_rating import policy_id
    from rule_profile_study import arm_files
    from training_opponents import validate_recipe
    from training_contract import run_rules_version

    torch.set_num_threads(1)
    root, source_run, parent_manifest = Path(root).absolute(), Path(source_run).absolute(), Path(parent_manifest).absolute()
    if (root.is_symlink() or root.resolve() != root or
            read_json(root/'.blocknations-training-root.json').get('owner') != 'BlockNations.LocalTraining.v1' or
            source_run.parent != root/'runs' or source_run.is_symlink() or
            read_json(source_run/'run.json').get('owner') != 'BlockNations.LocalTraining.v1' or
            not parent_manifest.is_relative_to(root/'runs') or parent_manifest.is_symlink() or
            not re.fullmatch(r'[a-z0-9][a-z0-9-]{0,60}', name)):
        raise ValueError('Prepare only a new named challenger inside its owned training root.')
    rules = run_rules_version(source_run)
    if (source_run/'human-games').is_symlink():
        raise ValueError('Human recordings must stay inside the owned run.')
    parent = read_json(parent_manifest)
    frozen_actor(parent_manifest, rules)
    examples = []
    for path in sorted((source_run/'human-games').glob('*.json'), key=lambda p:p.stat().st_mtime, reverse=True)[:32]:
        value = decode_game(path, 7, rules)
        if value is not None:
            examples.append(value)
    if not examples:
        raise ValueError('No compatible completed human wins are available.')
    plan = read_json(Path(parent['trainerConfig']))
    policy = TorchPolicy(seed, SPEC, NetworkSettings(**plan['behaviors']['BlockNationsSeatV2']['network_settings']),
                         SimpleActor, dict(conditional_sigma=False, tanh_squash=False))
    saved = torch.load(Path(parent['checkpoint']), map_location='cpu')
    policy.actor.load_state_dict(saved['Policy'], strict=True)
    evidence = fit(policy, examples, batches=batches, seed=seed)
    run, _, _ = arm_files(root, name, parent_manifest, source_run, rules, worker, 2048, seed)
    folder = run/'frozen-evaluations/human-challenger'
    folder.mkdir()
    checkpoint = folder/Path(parent['checkpoint']).name
    # Challenger inference uses only the actor. Preserve the source's other
    # modules without pretending their optimizer was used for this fit.
    snapshot = copy.deepcopy(saved)
    snapshot['Policy'] = policy.actor.state_dict()
    torch.save(snapshot, checkpoint)
    config = folder/'trainer.yaml'
    config.write_bytes(Path(parent['trainerConfig']).read_bytes())
    sha = lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
    manifest = dict(parent, checkpoint=str(checkpoint), trainerConfig=str(config), sha256=sha(checkpoint),
                    actorId=policy_id(policy.actor.state_dict()), trainedFrom='human-winning-demonstrations',
                    counterplay=evidence)
    atomic_json(folder/'manifest.json', manifest)
    recipe = read_json(run/'training-opponents.json')
    # A single treatment entry replaces the matching tactical Rider probe in
    # the isolated recipe. Probability/other opponents and all rules stay fixed.
    index = next(i for i, e in enumerate(recipe['opponents'])
                 if e['kind'] == 'fair-tactician' and e.get('recruitType') == 'rider')
    old = recipe['opponents'][index]
    recipe['opponents'][index] = dict(kind='frozen-policy', weight=old.get('weight', 1),
        checkpoint=str(checkpoint.relative_to(run)), trainerConfig=str(config.relative_to(run)),
        checkpointSha256=sha(checkpoint), configSha256=sha(config), actorId=manifest['actorId'],
        sampling='Policy', samplingSettings=None)
    validate_recipe(run, recipe, rules)
    atomic_json(run/'training-opponents.json', recipe)
    atomic_json(run/'human-counterplay-preparation.json', evidence)
    return run, evidence


if __name__ == '__main__':
    import argparse
    import json
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--source-run', type=Path, required=True)
    parser.add_argument('--parent-manifest', type=Path, required=True)
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--name', required=True)
    parser.add_argument('--batches', type=int, default=256)
    parser.add_argument('--seed', type=int, default=42)
    args = parser.parse_args()
    run, evidence = prepare(args.root, args.source_run, args.parent_manifest, args.worker, args.name,
                            batches=args.batches, seed=args.seed)
    print(json.dumps(dict(run=str(run), **evidence)))
