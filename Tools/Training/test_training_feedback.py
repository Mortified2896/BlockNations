import json
from pathlib import Path
import unittest
import os
import subprocess
import sys
import uuid
from unittest.mock import Mock

from training_feedback import Feedback, OWNER, effective_recipe
from training_opponents import TrainingOpponents
from playtest_champion import select


class TrainingFeedbackTests(unittest.TestCase):
    def setUp(self):
        from test_playtest_champion import PlaytestChampionTests
        self.fixture = PlaytestChampionTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.run = self.fixture.run
        _, manifest, results = self.fixture.candidate(100, 2)
        select(self.run, manifest, results, initialize=True)
        self.rules = self.fixture.contract['rulesVersion']
        self.recipe = dict(version=2, rulesVersion=self.rules, boardSize=7, openingEconomyVersion=2,
            probability=.4, opponents=[dict(kind='fair-tactician', weight=1, workBudget=64+i) for i in range(8)])
        self.write('training-opponents.json', self.recipe)
        self.write('training-feedback.json', dict(owner=OWNER, version=1, runId=self.run.name, enabled=True, championSlot=2))

    def write(self, name, value):
        (self.run/name).write_text(json.dumps(value))

    def test_champion_and_capped_weakness_weights_use_training_matches_only(self):
        original = (self.run/'training-opponents.json').read_bytes()
        feedback = Feedback(self.run, self.rules, 'session')
        runtime = effective_recipe(self.run, self.rules)
        self.assertEqual(runtime['probability'], .5)
        self.assertEqual(runtime['opponents'][2]['kind'], 'frozen-policy')
        for i, identity in enumerate(feedback.identities):
            for starts in (True, False):
                for game in range(32):
                    feedback.observe(dict(kind='begin', worker=0, match=game, session='session', fullOpening=True,
                        firstSeat=0 if starts else 1, context=dict(learningSeat=0, opponent=identity)))
                    feedback.observe(dict(kind='end', worker=0, match=game, session='session', winner=1 if i == 0 else 0))
        feedback.publish(force=True)
        status = json.loads((self.run/'training-league.json').read_text())
        self.assertTrue(status['adapted'])
        values = [e['weight']/100 for e in status['recipe']['opponents']]
        self.assertLessEqual(max(values), .25)
        self.assertGreaterEqual(min(values), .5/8)
        self.assertGreater(values[0], values[1])
        self.assertEqual((self.run/'training-opponents.json').read_bytes(), original)
        self.assertEqual(status['partition'], 'training')

    def test_interrupted_changed_and_foreign_matches_do_not_bias_feedback(self):
        feedback = Feedback(self.run, self.rules, 'session')
        identity = feedback.identities[0]
        for fields in (dict(interrupted=True), dict(policyChanged=True), dict(session='acceptance')):
            feedback.observe(dict(kind='begin', worker=0, match=1, session='session', fullOpening=True,
                                  firstSeat=0, context=dict(learningSeat=0, opponent=identity)))
            feedback.observe(dict(dict(kind='end', worker=0, match=1, session='session', winner=1), **fields))
        self.assertEqual(feedback.rows[identity]['True'], [])

    def test_recipe_reload_does_not_change_an_opponent_mid_match(self):
        (self.run/'training-feedback.json').unlink()
        stats = [dict(worker=0, match=1, trainerResets=0)]
        policy = dict(learningSeat=0, opponent='rolling')
        pool = next(p for p in (TrainingOpponents(self.run, seed, self.rules) for seed in range(100)) if p.selected(stats, policy))
        chosen = pool.assignments[0][1]
        previous = pool.assignments[0][2]['workBudget']
        self.recipe['opponents'][chosen]['workBudget'] = 512
        self.write('training-opponents.json', self.recipe)
        pool.next_recipe_poll = 0
        pool.selected(stats, policy)
        self.assertEqual(pool.assignments[0][2]['workBudget'], previous)
        self.assertEqual(pool.entries[chosen]['workBudget'], 512)

    def test_runtime_recipe_cannot_read_heldout_or_changed_base_receipts(self):
        Feedback(self.run, self.rules, 'session')
        state = json.loads((self.run/'training-league.json').read_text())
        state['partition'] = 'acceptance'
        self.write('training-league.json', state)
        with self.assertRaises(ValueError): effective_recipe(self.run, self.rules)

    @unittest.skipUnless(os.environ.get('BLOCKNATIONS_TEST_LEAGUE_PPO'), 'Opt-in actual adaptive PPO/resume integration.')
    def test_real_ppo_saves_and_resumes_with_selected_champion_feedback(self):
        from supervisor import trainer_config
        from mlagents.torch_utils import torch
        worker = Path(os.environ['BLOCKNATIONS_TEST_WORKER'])
        plan = trainer_config(4096, 2048)
        plan['behaviors']['BlockNationsSeatV2']['network_settings'] = self.fixture.network
        self.write('trainer.yaml', plan)
        environment = dict(os.environ, BLOCKNATIONS_RATING_RUN=str(self.run), BLOCKNATIONS_RATING_BOARD='7',
            BLOCKNATIONS_RATING_SESSION=uuid.uuid4().hex, BLOCKNATIONS_SIMULATION_WORKER=str(worker),
            BLOCKNATIONS_PARALLEL_GAMES='4', BLOCKNATIONS_SIMULATION_SEED='42', BLOCKNATIONS_TORCH_THREADS='1',
            BLOCKNATIONS_SIMULATION_CURRICULUM='false', BLOCKNATIONS_SIMULATION_DISTANCE='2',
            BLOCKNATIONS_OPENING_ECONOMY_VERSION='2', BLOCKNATIONS_RULES_VERSION=self.rules)
        command = [sys.executable, str(Path(__file__).with_name('rated_training.py')), str(self.run/'trainer.yaml'),
                   '--run-id', self.run.name, '--results-dir', str(self.run/'ppo'), '--seed', '42', '--torch-device', 'cpu']
        def train(resume=False):
            with (self.run/'ppo.log').open('w') as output:
                result = subprocess.run(command+(['--resume'] if resume else []), env=environment,
                    stdout=output, stderr=subprocess.STDOUT, timeout=90)
            self.assertEqual(result.returncode, 0, (self.run/'ppo.log').read_text()[-4000:])
        train()
        checkpoint = self.run/'ppo'/self.run.name/'BlockNationsSeatV2/checkpoint.pt'
        old = torch.load(checkpoint, map_location='cpu')
        plan['behaviors']['BlockNationsSeatV2']['max_steps'] = 8192
        self.write('trainer.yaml', plan)
        environment['BLOCKNATIONS_RATING_SESSION'] = uuid.uuid4().hex
        train(resume=True)
        new = torch.load(checkpoint, map_location='cpu')
        before = [float(v['step']) for v in old['Optimizer:value_optimizer']['state'].values()]
        after = [float(v['step']) for v in new['Optimizer:value_optimizer']['state'].values()]
        self.assertTrue(all(b > a for a, b in zip(before, after)))
        runtime = json.loads((self.run/'training-league.json').read_text())
        self.assertEqual(runtime['recipe']['opponents'][2]['kind'], 'frozen-policy')
        self.assertEqual(json.loads((self.run/'arena-status.json').read_text())['rejections'], 0)
