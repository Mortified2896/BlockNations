"""Compare frozen decisions on the same reachable prefix with fog/full vision.

This replays a completed manual diagnostic, never an active game. Counterfactual
full vision changes observations, not the replayed board or past actions. Saved
positions are evaluation-only; do not ingest them as an episode-start curriculum.
"""
import argparse
import base64
import json
from pathlib import Path
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents_envs.base_env import ActionTuple
from dotnet_environment import DotNetEnvironment
from evaluate_suite import frozen_actor, digest, worker_digest
from frozen_policy import evaluation_weights
from manual_policy_play import describe
from supervisor import ROOT_MARKER, atomic_json
from training_contract import CORE_THREE_RULES


def run(root, session, manifests, worker, destination):
    root, session, worker, destination = (Path(p).absolute() for p in (root, session, worker, destination))
    if (root.resolve() != root or json.loads((root/ROOT_MARKER).read_text()).get('owner') != 'BlockNations.LocalTraining.v1'
            or session.resolve() != session or not session.is_relative_to(root/'benchmarks')
            or destination.resolve() != destination or not destination.is_relative_to(root/'benchmarks') or destination.exists()):
        raise ValueError('Use owned completed diagnostics and a new destination.')
    result = json.loads((session/'result.json').read_text())
    if result.get('state') != 'completed' or result.get('interrupted') or result.get('rejections') != 0:
        raise ValueError('Replay needs a complete legal diagnostic match.')
    records = [json.loads(line) for line in (session/'decisions.jsonl').open()]
    if len(records) != result['allChoices'] or not 1 <= len(records) <= 4096:
        raise ValueError('Incomplete or unbounded decision journal.')
    actors = []
    for manifest in manifests:
        manifest = Path(manifest).absolute()
        if manifest.resolve() != manifest or not manifest.is_relative_to(root/'runs'):
            raise ValueError('Use immutable owned actors.')
        actor, identity = frozen_actor(manifest, CORE_THREE_RULES)
        if actor.observation_version != 2:
            raise ValueError('This recorded console protocol is observation v2.')
        actors.append((actor, identity))
    destination.mkdir(parents=True)
    receipt = dict(owner='BlockNations.ReplayPolicyDiagnostic.v1', state='running', partition='evaluation-only',
        sourceJournalSha256=digest(session/'decisions.jsonl'), workerSha256=worker_digest(worker),
        models=[identity for _, identity in actors], positions=[])
    try:
        for vision in (0, 3):
            directory = destination/('fog' if vision == 0 else 'full-view')
            directory.mkdir()
            env = DotNetEnvironment(worker, directory, 1,
                result['seed'], 7, False, 2, uuid.uuid4().hex, first_seat=result['firstSeat'],
                rules_version=CORE_THREE_RULES, observation_version=2, diagnostic_full_vision_seats=vision)
            try:
                env.reset()
                for index, record in enumerate(records):
                    decisions, _ = env.get_steps(env.names[record['seat']])
                    if len(decisions) != 1 or decisions.action_mask[0][0][record['choice']]:
                        raise ValueError('Recorded action prefix no longer reaches the same legal state.')
                    vector = decisions.obs[0][0]
                    # Normal fog must exactly reproduce the original fair observation.
                    if vision == 0:
                        original = np.frombuffer(base64.b64decode(record['vector']), dtype='<f4')
                        np.testing.assert_array_equal(vector, original)
                    if record['controller'] == 'frozen' and round(float(vector[3114])*100) >= result['rounds']:
                        scores = []
                        for actor, identity in actors:
                            raw = actor.probabilities(decisions.obs[0], decisions.action_mask[0])
                            scores.append(dict(actorId=identity['actorId'], modes={mode: dict(
                                replayedActionProbability=float(evaluation_weights(raw, mode)[0, record['choice']]),
                                top=[dict(action=int(i), description=describe(int(i)), probability=float(weights[i]))
                                     for i in np.argsort(weights)[-5:][::-1] if weights[i] > 0])
                                for mode in ('Policy', 'Hard')
                                for weights in (evaluation_weights(raw, mode)[0],)}))
                        receipt['positions'].append(dict(journalIndex=index, vision='fog' if vision == 0 else 'full-view',
                            round=round(float(vector[3114])*100), replayedAction=record['choice'],
                            observation=base64.b64encode(vector.astype('<f4').tobytes()).decode(),
                            available=np.flatnonzero(~decisions.action_mask[0][0]).tolist(), scores=scores))
                    env.set_actions(env.names[record['seat']], ActionTuple(discrete=np.array([[record['choice']]], dtype=np.int32)))
                    env.step()
            finally:
                env.close()
        receipt['state'] = 'completed'
    except BaseException as error:
        receipt.update(state='failed', error=str(error))
        raise
    finally:
        atomic_json(destination/'diagnostic.json', receipt)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ('root', 'session', 'worker', 'destination'):
        parser.add_argument('--'+field, type=Path, required=True)
    parser.add_argument('--manifest', type=Path, action='append', required=True)
    args = parser.parse_args()
    torch.set_num_threads(1)
    result = run(args.root, args.session, args.manifest, args.worker, args.destination)
    print(json.dumps(dict(state=result['state'], positions=len(result['positions']), models=len(result['models']))))
