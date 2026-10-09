"""Opt-in integration: real Unity-recorded human game -> shared worker -> PPO -> checkpoint.

Set BLOCKNATIONS_TEST_HUMAN_GAME to the fixture emitted by the graphical Unity
input test, and BLOCKNATIONS_TEST_WORKER to a newly built worker. Never uses a
real training run or real human records.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import uuid

from supervisor import trainer_config

FIXTURE = os.environ.get('BLOCKNATIONS_TEST_HUMAN_GAME')
WORKER = os.environ.get('BLOCKNATIONS_TEST_WORKER')


@unittest.skipUnless(FIXTURE and WORKER, 'Requires a real Unity input fixture and shared worker.')
class HumanTrainingIntegrationTests(unittest.TestCase):
    def test_recorded_human_win_updates_real_ppo_and_resumes_checkpointed_optimizer(self):
        from mlagents.torch_utils import torch
        with tempfile.TemporaryDirectory(prefix='bn-human-pipeline-') as directory:
            run = Path(directory) / 'pipeline'
            (run / 'human-games').mkdir(parents=True)
            game = json.loads(Path(FIXTURE).read_text())
            shutil.copyfile(FIXTURE, run / 'human-games' / (game['id']+'.json'))
            config = run / 'trainer.json'
            config.write_text(json.dumps(trainer_config(4096, 2048)))
            env = dict(os.environ, BLOCKNATIONS_RATING_RUN=str(run), BLOCKNATIONS_RATING_BOARD=str(game['boardSize']),
                       BLOCKNATIONS_RATING_SESSION=uuid.uuid4().hex,
                       BLOCKNATIONS_SIMULATION_WORKER=WORKER, BLOCKNATIONS_PARALLEL_GAMES='2',
                       BLOCKNATIONS_SIMULATION_SEED='42', BLOCKNATIONS_SIMULATION_CURRICULUM='false',
                       BLOCKNATIONS_SIMULATION_DISTANCE='2', PYTHONWARNINGS='ignore')
            command = [sys.executable, '-W', 'ignore', str(Path(__file__).with_name('rated_training.py')), str(config),
                       '--run-id', run.name, '--results-dir', str(run/'checkpoints'), '--seed', '42', '--torch-device', 'cpu']
            def train(resume=False):
                log = run / ('resume.log' if resume else 'initial.log')
                with log.open('w') as output:
                    process = subprocess.run(command + (['--resume'] if resume else []), env=env,
                                             stdout=output, stderr=subprocess.STDOUT, timeout=90)
                self.assertEqual(process.returncode, 0, log.read_text()[-6000:])
            train()
            ledger = json.loads((run/'human-learning-status.json').read_text())
            self.assertEqual(ledger['winningGames'], 1)
            self.assertGreater(ledger['updates'], 0)
            self.assertGreater(ledger['usedExamples'], 0)
            checkpoint = run/'checkpoints'/run.name/'BlockNationsSeatV2'/'checkpoint.pt'
            initial = torch.load(checkpoint)
            self.assertGreaterEqual(len(initial['Optimizer:value_optimizer']['state']), 12)
            plan = json.loads(config.read_text())
            plan['behaviors']['BlockNationsSeatV2']['max_steps'] = 8192
            config.write_text(json.dumps(plan))
            env['BLOCKNATIONS_RATING_SESSION'] = uuid.uuid4().hex
            train(resume=True)
            resumed = torch.load(checkpoint)
            continued = json.loads((run/'human-learning-status.json').read_text())
            self.assertEqual(continued['usedExamples'], ledger['usedExamples'], 'Spent human example budgets survive a clean resume.')
            self.assertGreaterEqual(len(resumed['Optimizer:value_optimizer']['state']), 12)
            old_steps = max(float(s['step']) for s in initial['Optimizer:value_optimizer']['state'].values())
            new_steps = max(float(s['step']) for s in resumed['Optimizer:value_optimizer']['state'].values())
            self.assertGreater(new_steps, old_steps, 'Optimizer momentum/steps continue instead of reinitializing.')
            print(f'Real Unity/PPO human pipeline: {ledger["updates"]} imitation updates; {ledger["usedExamples"]} examples; '
                  f'Adam steps {old_steps:.0f} -> {new_steps:.0f}')


if __name__ == '__main__': unittest.main()
