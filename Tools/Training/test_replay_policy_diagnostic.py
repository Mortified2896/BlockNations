import base64
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents_envs.base_env import ActionTuple

import replay_policy_diagnostic as replay
from dotnet_environment import DotNetEnvironment
from manual_policy_play import describe
from model_contract import SIZES
from supervisor import ROOT_MARKER
from training_contract import CORE_THREE_RULES

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER', '/not-built'))
torch.set_num_threads(1)


class CapturingActor:
    def __init__(self, schema):
        self.observation_version = schema
        self.inputs = []

    def probabilities(self, values, masks):
        if values.shape != (1, SIZES[self.observation_version]):
            raise AssertionError('Actor received the wrong contract.')
        self.inputs.append(values.copy())
        weights = (~masks).astype(np.float64)
        return weights/weights.sum(axis=1, keepdims=True)


def models(root):
    paths = {}
    for schema in SIZES:
        folder = root/'runs'/'fixture'/f'frozen-{schema}'
        folder.mkdir(parents=True)
        path = folder/'manifest.json'
        path.write_text(json.dumps(dict(schema=schema, observationVersion=schema, rulesVersion=CORE_THREE_RULES,
                                       boardSize=7, openingEconomyVersion=2,
                                       checkpoint=str(folder/f'BlockNationsSeatV{schema}-17.pt'))))
        paths[schema] = path
    return paths


def session(root, paths, schema, records, winner=0, rounds=1):
    folder = root/'benchmarks'/uuid.uuid4().hex
    folder.mkdir(parents=True)
    rows = []
    for record in records:
        row = {k: v for k, v in record.items() if k != '_vector'}
        row['vector'] = base64.b64encode(record['_vector'][:SIZES[schema]].astype('<f4').tobytes()).decode()
        rows.append(row)
    (folder/'decisions.jsonl').write_text(''.join(json.dumps(row)+'\n' for row in rows))
    (folder/'result.json').write_text(json.dumps(dict(owner='BlockNations.ManualPolicyDiagnostic.v1',
        state='completed', interrupted=False, rejections=0, boardSize=7, vision='normal-fog',
        seat=0, firstSeat=0, winner=winner, rounds=rounds, seed=7331, allChoices=len(rows),
        model=str(paths[schema]), manifestSha256=replay.digest(paths[schema]))))
    return folder


class ReplayBoundaries(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        (self.root/ROOT_MARKER).write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1')))
        self.models = models(self.root)
        self.session = session(self.root, self.models, 2, [dict(seat=0, controller='manual',
            choice=258, available=[258], _vector=np.zeros(SIZES[4], dtype=np.float32))])

    def test_future_recruit_slots_have_safe_descriptions(self):
        self.assertEqual(describe(242), 'recruit archer')
        self.assertEqual(describe(245), 'recruit warrior')
        self.assertEqual(describe(246), 'recruit slot 4')
        self.assertEqual(describe(257), 'recruit slot 15')
        for action in (-1, 259, True, 242.0):
            with self.assertRaises(ValueError): describe(action)

    def test_source_model_changes_invalidate_a_journal(self):
        self.models[2].write_text('{}')
        with self.assertRaisesRegex(ValueError, 'manifest changed'):
            replay.journal(self.root, self.session)

    def test_other_rules_cannot_enter_a_same_shape_journal(self):
        model = self.models[2]; value = json.loads(model.read_text()); value['rulesVersion'] = 'blocknations-simulation-v4'
        model.write_text(json.dumps(value))
        path = self.session/'result.json'; result = json.loads(path.read_text()); result['manifestSha256'] = replay.digest(model)
        path.write_text(json.dumps(result))
        with self.assertRaisesRegex(ValueError, 'source rules/opening'): replay.journal(self.root, self.session)

    def test_extra_and_truncated_records_cannot_count_as_a_complete_match(self):
        path = self.session/'decisions.jsonl'; original = path.read_text()
        for contents in ('', original+original):
            path.write_text(contents)
            with self.assertRaises(ValueError): replay.journal(self.root, self.session)

    def test_nonfinite_vectors_and_wrong_observation_versions_are_rejected(self):
        path = self.session/'decisions.jsonl'; original = json.loads(path.read_text())
        for values in (np.zeros(SIZES[3], dtype=np.float32), np.full(SIZES[2], np.nan, dtype=np.float32)):
            row = dict(original, vector=base64.b64encode(values.astype('<f4').tobytes()).decode())
            path.write_text(json.dumps(row)+'\n')
            with self.assertRaisesRegex(ValueError, 'observation contract'): replay.journal(self.root, self.session)

    def test_linked_source_evidence_is_rejected(self):
        path = self.session/'decisions.jsonl'; target = self.root/'retained-journal.jsonl'
        path.rename(target); path.symlink_to(target)
        with self.assertRaisesRegex(ValueError, 'unlinked evidence'): replay.journal(self.root, self.session)

    def test_seat_and_source_visibility_remain_explicit(self):
        path = self.session/'result.json'; original = json.loads(path.read_text())
        for field, value in [('seat', True), ('firstSeat', 2), ('vision', 'full-view'), ('owner', 'other')]:
            path.write_text(json.dumps(dict(original, **{field: value})))
            with self.assertRaises(ValueError): replay.journal(self.root, self.session)

    def test_history_summary_uses_public_capabilities_and_sighting_age(self):
        values = np.zeros(SIZES[4], dtype=np.float32)
        offset = SIZES[3]+17*12
        values[offset:offset+12] = [1, .6, 1, .2, .3, 2/11, 2/11, 1/11, 0, 0, 1, .07]
        original = values.copy(); facts = replay.sightings(values)
        self.assertEqual(len(facts), 1)
        self.assertEqual((facts[0]['position'], facts[0]['ageRounds'], facts[0]['currentlyVisible']), (17, 7, False))
        for key, expected in [('health', 6), ('attack', 2), ('defense', 3), ('range', 2)]:
            self.assertAlmostEqual(facts[0][key], expected, places=5)
        self.assertNotIn('unitId', facts[0]); self.assertNotIn('gold', facts[0])
        np.testing.assert_array_equal(values, original)

    def test_overnight_guards_stop_before_starting_a_worker(self):
        actor = CapturingActor(2)
        identity = dict(actorId='fixture', sha256='0'*64, configSha256='1'*64)
        for ac, battery, thermal in [(False, 100, 0), (True, 79, 0), (True, 100, 1), (True, 100, 2)]:
            with patch.object(replay, 'frozen_actor', return_value=(actor, identity)), \
                    patch.object(replay, 'read_power', return_value=(ac, battery)), \
                    patch.object(replay, 'thermal_state', return_value=thermal), \
                    patch.object(replay, 'DotNetEnvironment') as environment:
                destination = self.root/'benchmarks'/uuid.uuid4().hex
                with self.assertRaisesRegex(RuntimeError, 'power/thermal'):
                    replay.run(self.root, self.session, [self.models[2]], WORKER, destination, power_controlled=True)
                environment.assert_not_called(); self.assertFalse(destination.exists())


@unittest.skipUnless(WORKER.is_file(), 'Build the actual shared worker first.')
class ActualReplayContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.root = Path(cls.temp.name).resolve()
        (cls.root/ROOT_MARKER).write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1')))
        cls.models = models(cls.root)
        cls.records = []; cls.rounds = 1
        directory = cls.root/'benchmarks'/'fixture-worker'; directory.mkdir(parents=True)
        env = DotNetEnvironment(WORKER, directory, 1, 7331, 7, False, 2, uuid.uuid4().hex,
                               first_seat=0, rules_version=CORE_THREE_RULES, observation_version=4)
        try:
            env.reset()
            for _ in range(512):
                _, terminal = env.get_steps(env.names[0])
                if len(terminal):
                    if terminal.interrupted[0]: raise AssertionError('Fixture must finish decisively.')
                    cls.winner = 0 if terminal.reward[0] > 0 else 1
                    break
                for seat, name in enumerate(env.names):
                    decisions, _ = env.get_steps(name)
                    if not len(decisions): continue
                    vector = decisions.obs[0][0].copy()
                    cls.rounds = max(cls.rounds, round(float(vector[3114])*100))
                    choice = env.reference_actions(seat, 'rider', 64)[int(decisions.agent_id[0])]
                    cls.records.append(dict(seat=seat, controller='manual' if seat == 0 else 'frozen',
                        choice=int(choice), available=np.flatnonzero(~decisions.action_mask[0][0]).tolist(), _vector=vector))
                    env.set_actions(name, ActionTuple(discrete=np.array([[choice]], dtype=np.int32)))
                env.step()
            else: raise AssertionError('Complete a real fixture within the bounded action budget.')
        finally: env.close()

    @classmethod
    def tearDownClass(cls): cls.temp.cleanup()

    def run_replay(self, schema, alter=None):
        source = session(self.root, self.models, schema, self.records, self.winner, self.rounds)
        if alter: alter(source)
        destination = self.root/'benchmarks'/uuid.uuid4().hex
        actors = {s: CapturingActor(s) for s in SIZES}; environments = []
        def load(path, _):
            version = json.loads(Path(path).read_text())['schema']
            return actors[version], dict(actorId=f'fixture-{version}', sha256=str(version)*64, configSha256='1'*64)
        def start(*args, **kwargs):
            env = DotNetEnvironment(*args, **kwargs); environments.append(env); return env
        with patch.object(replay, 'frozen_actor', side_effect=load), patch.object(replay, 'DotNetEnvironment', side_effect=start):
            try:
                result = replay.run(self.root, source, list(self.models.values()), WORKER, destination)
            finally:
                self.assertTrue(all(env.process.poll() is not None for env in environments))
        return result, actors

    def test_real_completed_journals_replay_all_three_contracts_and_keep_old_prefixes(self):
        for schema in SIZES:
            result, actors = self.run_replay(schema)
            self.assertEqual(result['state'], 'completed')
            self.assertEqual(result['journalObservationVersion'], schema)
            self.assertEqual(result['projectionObservationVersion'], 4)
            self.assertEqual([(r['winner'], r['choices']) for r in result['replays']], [(self.winner, len(self.records))])
            self.assertEqual(result['projectionMethod'], 'read-only-current-state')
            self.assertTrue(result['positions'])
            for position in result['positions']:
                self.assertEqual(len(base64.b64decode(position['observation'])), SIZES[schema]*4)
            for old, newer in zip(actors[2].inputs, actors[4].inputs):
                np.testing.assert_array_equal(old, newer[:, :SIZES[2]])
            self.assertTrue(actors[3].inputs)

    def test_altered_legacy_observation_is_not_reproduced(self):
        def alter(source):
            path = source/'decisions.jsonl'; rows = [json.loads(line) for line in path.read_text().splitlines()]
            values = np.frombuffer(base64.b64decode(rows[0]['vector']), dtype='<f4').copy(); values[0] += 1
            rows[0]['vector'] = base64.b64encode(values.tobytes()).decode()
            path.write_text(''.join(json.dumps(row)+'\n' for row in rows))
        with self.assertRaises(AssertionError): self.run_replay(2, alter)

    def test_altered_history_suffix_cannot_pass_only_a_legacy_prefix_check(self):
        def alter(source):
            path = source/'decisions.jsonl'; rows = [json.loads(line) for line in path.read_text().splitlines()]
            values = np.frombuffer(base64.b64decode(rows[0]['vector']), dtype='<f4').copy(); values[SIZES[3]+11] = .1
            rows[0]['vector'] = base64.b64encode(values.tobytes()).decode()
            path.write_text(''.join(json.dumps(row)+'\n' for row in rows))
        with self.assertRaises(AssertionError): self.run_replay(4, alter)

    def test_wrong_completed_outcome_is_rejected_after_real_replay(self):
        def alter(source):
            path = source/'result.json'; value = json.loads(path.read_text()); value['winner'] = 1-self.winner
            path.write_text(json.dumps(value))
        with self.assertRaisesRegex(ValueError, 'complete legal outcome'): self.run_replay(3, alter)

    def test_projection_cannot_replace_fog_decisions_history_counters_or_rng(self):
        environments = []
        try:
            for enabled in (False, True):
                directory = self.root/'benchmarks'/uuid.uuid4().hex; directory.mkdir()
                env = DotNetEnvironment(WORKER, directory, 2, 7331, 7, False, 2, uuid.uuid4().hex,
                    first_seat=-1, rules_version=CORE_THREE_RULES, observation_version=4, diagnostic_projections=enabled)
                environments.append(env); env.reset()
            baseline, projected = environments
            with self.assertRaisesRegex(ValueError, 'ordinary sessions'): baseline.diagnostic_projection()
            for _ in range(24):
                before = [(d.obs[0].copy(), d.action_mask[0].copy()) for d, _ in projected.results.values()]
                counters = [(s['decisions'], s['actions'], s['games']) for s in projected.stats]
                diagnostic = projected.diagnostic_projection()
                self.assertEqual(sum(len(d) for d, _ in diagnostic.values()), 2)
                for decisions, _ in diagnostic.values():
                    for values in decisions.obs[0]:
                        self.assertEqual(np.count_nonzero(values[:121*24].reshape(121, 24)[:, 2]), 49)
                self.assertEqual(counters, [(s['decisions'], s['actions'], s['games']) for s in projected.stats])
                for (values, masks), (decisions, _) in zip(before, projected.results.values()):
                    np.testing.assert_array_equal(values, decisions.obs[0]); np.testing.assert_array_equal(masks, decisions.action_mask[0])
                for seat in (0, 1):
                    normal, _ = baseline.get_steps(baseline.names[seat]); actual, _ = projected.get_steps(projected.names[seat])
                    np.testing.assert_array_equal(normal.obs[0], actual.obs[0]); np.testing.assert_array_equal(normal.action_mask[0], actual.action_mask[0])
                    if not len(normal): continue
                    choices = np.array([[np.flatnonzero(~row)[0]] for row in normal.action_mask[0]], dtype=np.int32)
                    baseline.set_actions(baseline.names[seat], ActionTuple(discrete=choices))
                    projected.set_actions(projected.names[seat], ActionTuple(discrete=choices))
                baseline.step(); projected.step()
            for _ in range(12):
                baseline.reset(); projected.reset()
                for seat in (0, 1):
                    np.testing.assert_array_equal(baseline.get_steps(baseline.names[seat])[0].obs[0], projected.get_steps(projected.names[seat])[0].obs[0])
        finally:
            for env in environments: env.close()
