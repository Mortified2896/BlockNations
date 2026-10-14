"""Compare frozen decisions on the same reachable prefix with fog/full vision.

This replays a completed manual diagnostic, never an active game. A read-only
projection changes vision on the current fog board without applying an action.
Saved positions are evaluation-only; do not ingest them as an episode-start curriculum.
"""
import argparse
import base64
import json
from pathlib import Path
import shutil
import time
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents_envs.base_env import ActionTuple
from dotnet_environment import DotNetEnvironment
from evaluate_suite import frozen_actor, digest, worker_digest
from frozen_policy import evaluation_weights
from manual_policy_play import describe
from model_contract import SIZES, checkpoint_schema, prefix, validate_schema
from host_resources import thermal_state
from power_controller import read_power
from supervisor import ROOT_MARKER, atomic_json, size
from training_contract import CORE_THREE_RULES, validate_rules_version


def evidence_file(root, path, area, limit):
    path = Path(path).absolute()
    if (path.resolve() != path or not path.is_relative_to(root/area) or
            not path.is_file() or path.stat().st_size > limit):
        raise ValueError('Use bounded, unlinked evidence inside its owned root.')
    return path


def journal(root, session, rules_version=CORE_THREE_RULES):
    result_path = evidence_file(root, session/'result.json', 'benchmarks', 128_000)
    result = json.loads(result_path.read_text())
    if (result.get('owner') != 'BlockNations.ManualPolicyDiagnostic.v1' or
            result.get('state') != 'completed' or result.get('interrupted') is not False or
            type(result.get('rejections')) is not int or result['rejections'] != 0 or result.get('boardSize') != 7 or
            result.get('vision') != 'normal-fog' or
            any(type(result.get(k)) is not int or result[k] not in (0, 1)
                for k in ('seat', 'firstSeat', 'winner')) or
            type(result.get('rounds')) is not int or not 1 <= result['rounds'] <= 100 or
            type(result.get('allChoices')) is not int or not 1 <= result['allChoices'] <= 4096 or
            type(result.get('seed')) is not int or not 0 <= result['seed'] < 2**31):
        raise ValueError('Replay needs a complete legal normal-fog diagnostic match.')
    source = evidence_file(root, result['model'], 'runs', 128_000)
    if digest(source) != result['manifestSha256']:
        raise ValueError('Recorded source-model manifest changed.')
    manifest = json.loads(source.read_text())
    if (manifest.get('rulesVersion') != rules_version or manifest.get('boardSize') != 7 or
            manifest.get('openingEconomyVersion') != 2):
        raise ValueError('Recorded source rules/opening differ from the requested replay profile.')
    schema = checkpoint_schema(manifest['checkpoint'])
    if manifest.get('schema', schema) != schema or manifest.get('observationVersion', schema) != schema:
        raise ValueError('Recorded model and journal observation contracts differ.')
    path = evidence_file(root, session/'decisions.jsonl', 'benchmarks', 160_000_000)
    records = []
    with path.open() as stream:
        while line := stream.readline(100_001):
            if len(line) > 100_000 or len(records) >= result['allChoices']:
                raise ValueError('Oversized or extra journal decisions.')
            record = json.loads(line)
            if (type(record.get('seat')) is not int or record['seat'] not in (0, 1) or
                    record.get('controller') != ('manual' if record['seat'] == result['seat'] else 'frozen') or
                    type(record.get('choice')) is not int or not 0 <= record['choice'] < 259):
                raise ValueError('Journal choices must retain explicit seat/controller identity.')
            vector = np.frombuffer(base64.b64decode(record['vector'], validate=True), dtype='<f4')
            if vector.shape != (SIZES[schema],) or not np.isfinite(vector).all():
                raise ValueError('Invalid recorded observation contract or values.')
            record['_vector'] = vector
            records.append(record)
    if len(records) != result['allChoices']:
        raise ValueError('Incomplete decision journal.')
    return result, records, schema, dict(sourceResultSha256=digest(result_path),
                                       sourceManifestSha256=digest(source), sourceJournalSha256=digest(path))


def sightings(vector):
    """Last observations from the real seat-local V4 projection, never live labels."""
    visible = vector[:121*24].reshape(121, 24)[:, 2]
    history = vector[SIZES[3]:-1].reshape(121, 12)
    return [dict(position=int(position), ageRounds=round(float(row[11])*100),
                 currentlyVisible=bool(visible[position]), health=float(row[1])*10,
                 attack=float(row[3])*10, defense=float(row[4])*10, range=float(row[5])*11,
                 vision=float(row[6])*11, maxMoves=float(row[7])*11)
            for position, row in enumerate(history) if row[0]]


def run(root, session, manifests, worker, destination, *, power_controlled=False, rules_version=CORE_THREE_RULES):
    if type(power_controlled) is not bool:
        raise ValueError('Enable the power guard explicitly.')
    root, session, worker, destination = (Path(p).absolute() for p in (root, session, worker, destination))
    if (root.resolve() != root or json.loads((root/ROOT_MARKER).read_text()).get('owner') != 'BlockNations.LocalTraining.v1'
            or session.resolve() != session or not session.is_relative_to(root/'benchmarks')
            or destination.resolve() != destination or not destination.is_relative_to(root/'benchmarks') or destination.exists()):
        raise ValueError('Use owned completed diagnostics and a new destination.')
    rules_version = validate_rules_version(rules_version)
    result, records, journal_schema, sources = journal(root, session, rules_version)
    scored_round = max((round(float(r['_vector'][3114])*100) for r in records if r['controller'] == 'frozen'), default=None)
    if not 1 <= len(manifests) <= 8:
        raise ValueError('Choose one to eight compatible frozen models.')
    actors = []
    for manifest in manifests:
        manifest = evidence_file(root, manifest, 'runs', 128_000)
        actor, identity = frozen_actor(manifest, rules_version)
        validate_schema(actor.observation_version)
        actors.append((actor, identity))
    next_guard = 0
    def guard():
        nonlocal next_guard
        now = time.monotonic()
        if now < next_guard:
            return
        if size(root) > 19_488_000_000 or shutil.disk_usage(root).free < 20_512_000_000:
            raise RuntimeError('Preserve the shared final-save reserve.')
        if power_controlled:
            ac, battery = read_power(); thermal = thermal_state()
            if not ac or battery < (80 if next_guard == 0 else 76) or thermal >= (1 if next_guard == 0 else 2):
                raise RuntimeError('Replay power/thermal guard stopped the diagnostic; preserve its receipt.')
        next_guard = now+30
    guard()
    destination.mkdir(parents=True)
    receipt = dict(owner='BlockNations.ReplayPolicyDiagnostic.v1', state='running', partition='evaluation-only',
        **sources, rulesVersion=rules_version, workerSha256=worker_digest(worker), journalObservationVersion=journal_schema,
        projectionObservationVersion=4, powerControlled=power_controlled,
        scoredRound=scored_round, scoreScope='last recorded frozen-controlled round',
        models=[dict(identity, observationVersion=actor.observation_version) for actor, identity in actors],
        positions=[], replays=[])
    def score(decisions, record, index, vision):
        vector = decisions.obs[0][0]; scores = []
        for actor, identity in actors:
            raw = actor.probabilities(prefix(decisions.obs[0], 4, actor.observation_version), decisions.action_mask[0])
            scores.append(dict(actorId=identity['actorId'], modes={mode: dict(
                replayedActionProbability=float(weights[record['choice']]),
                top=[dict(action=int(i), description=describe(int(i)), probability=float(weights[i]))
                     for i in np.argsort(weights)[-5:][::-1] if weights[i] > 0])
                for mode in ('Policy', 'Hard') for weights in (evaluation_weights(raw, mode)[0],)}))
        receipt['positions'].append(dict(journalIndex=index, vision=vision,
            round=round(float(vector[3114])*100), replayedAction=record['choice'],
            observation=base64.b64encode(prefix(decisions.obs[0], 4, journal_schema)[0].astype('<f4').tobytes()).decode(),
            available=np.flatnonzero(~decisions.action_mask[0][0]).tolist(), scores=scores, sightings=sightings(vector)))

    try:
        directory = destination/'fog'; directory.mkdir()
        env = DotNetEnvironment(worker, directory, 1,
            result['seed'], 7, False, 2, uuid.uuid4().hex, first_seat=result['firstSeat'],
            rules_version=rules_version, observation_version=4, diagnostic_projections=True)
        try:
            env.reset()
            for index, record in enumerate(records):
                guard()
                decisions, _ = env.get_steps(env.names[record['seat']])
                if len(decisions) != 1 or decisions.action_mask[0][0][record['choice']]:
                    raise ValueError('Recorded action prefix no longer reaches the same legal state.')
                vector = decisions.obs[0][0]
                np.testing.assert_array_equal(prefix(decisions.obs[0], 4, journal_schema)[0], record['_vector'])
                if np.flatnonzero(~decisions.action_mask[0][0]).tolist() != record['available']:
                    raise ValueError('Recorded normal-fog legal choices changed.')
                if record['controller'] == 'frozen' and round(float(vector[3114])*100) == scored_round:
                    score(decisions, record, index, 'fog')
                    projected, _ = env.diagnostic_projection()[env.names[record['seat']]]
                    if len(projected) != 1:
                        raise ValueError('Projection must retain the current decision seat.')
                    score(projected, record, index, 'full-view')
                    np.testing.assert_array_equal(env.get_steps(env.names[record['seat']])[0].obs[0], decisions.obs[0])
                env.set_actions(env.names[record['seat']], ActionTuple(discrete=np.array([[record['choice']]], dtype=np.int32)))
                env.step()
            _, terminal = env.get_steps(env.names[result['seat']])
            if (len(terminal) != 1 or terminal.interrupted[0] or
                    (result['seat'] if terminal.reward[0] > 0 else 1-result['seat']) != result['winner'] or
                    sum(s['rejections'] for s in env.stats) != 0):
                raise ValueError('Replayed prefix did not reproduce the complete legal outcome.')
            receipt['replays'].append(dict(vision='fog', choices=len(records), winner=result['winner'], interruptions=0, rejections=0))
            receipt['projectionMethod'] = 'read-only-current-state'
            receipt['positions'].sort(key=lambda p: (p['vision'] == 'full-view', p['journalIndex']))
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
    parser.add_argument('--power-controlled', action='store_true')
    parser.add_argument('--rules-version', default=CORE_THREE_RULES)
    args = parser.parse_args()
    torch.set_num_threads(1)
    result = run(args.root, args.session, args.manifest, args.worker, args.destination,
                 power_controlled=args.power_controlled, rules_version=args.rules_version)
    print(json.dumps(dict(state=result['state'], positions=len(result['positions']), models=len(result['models']))))
