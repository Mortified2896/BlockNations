import hashlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

import torch
from mlagents.trainers.settings import NetworkSettings, RunOptions
from mlagents.trainers.torch_entities.networks import SimpleActor

sys.path.insert(0, str(Path(__file__).parents[1]/'Training'))
from match_rating import policy_id
from model_contract import spec
from training_contract import CORE_THREE_RULES
from playtest_sampling import PRESETS
from train import recipe
import league

torch.set_num_threads(1)
CONTRACT = dict(width=32, boardSize=7, rulesVersion=CORE_THREE_RULES, architecture='shared-capability-v2')


def source(root):
    (root/'.blocknations-training-root.json').write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1')))
    run=root/'runs/teacher-league';run.mkdir(parents=True)
    folder=run/'frozen-evaluations/milestone-123';folder.mkdir(parents=True)
    network=dict(hidden_units=16,num_layers=2)
    contract=spec(2);actor=SimpleActor(contract.observation_specs,NetworkSettings(**network),contract.action_spec)
    saved=folder/'BlockNationsSeatV2-123.pt';torch.save(dict(Policy=actor.state_dict()),saved)
    config=folder/'trainer.json';config.write_text(json.dumps(dict(behaviors={'BlockNationsSeatV2':dict(network_settings=network)})))
    sha=hashlib.sha256(saved.read_bytes()).hexdigest()
    metadata=dict(owner='BlockNations.LocalTraining.v1',schema=2,rulesVersion=CORE_THREE_RULES,boardSize=7,openingEconomyVersion=2)
    (run/'run.json').write_text(json.dumps(metadata))
    (folder/'manifest.json').write_text(json.dumps(dict(rulesVersion=CORE_THREE_RULES,boardSize=7,openingEconomyVersion=2,sha256=sha,checkpoint=str(saved),trainerConfig=str(config))))
    entry=dict(kind='frozen-policy',checkpoint=str(saved.relative_to(run)),trainerConfig=str(config.relative_to(run)),
               checkpointSha256=sha,configSha256=hashlib.sha256(config.read_bytes()).hexdigest(),
               actorId=policy_id(actor.state_dict()),sampling='Hard',samplingSettings=list(PRESETS['Hard']))
    recipe=dict(version=2,rulesVersion=CORE_THREE_RULES,boardSize=7,openingEconomyVersion=2,probability=.5,
                opponents=[entry,dict(kind='fair-tactician',workBudget=8)])
    (run/'training-opponents.json').write_text(json.dumps(recipe))
    (run/'private-journal.json').write_text('private local game data')
    return run


class RetainedLeagueTests(unittest.TestCase):
    def test_copied_league_survives_source_removal_without_importing_private_journals(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();original=source(root);run=root/'benchmarks/trial';run.mkdir(parents=True)
            identity=league.install(root,original,run,CONTRACT)
            self.assertFalse((run/'private-journal.json').exists())
            manifest=json.loads((run/'frozen-evaluations/milestone-123/manifest.json').read_text())
            self.assertTrue(Path(manifest['checkpoint']).is_relative_to(run))
            shutil.rmtree(original)
            self.assertEqual(league.identity(run,CONTRACT),identity)
            (run/'training-opponents.json').write_text('{}')
            with self.assertRaises(ValueError):league.identity(run,CONTRACT)

    def test_another_board_or_unreceipted_pool_cannot_be_silently_used(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();original=source(root);run=root/'benchmarks/trial';run.mkdir(parents=True)
            with self.assertRaises(ValueError):league.install(root,original,run,dict(CONTRACT,boardSize=9))
            (run/'training-opponents.json').write_text('{}')
            with self.assertRaises(ValueError):league.identity(run,CONTRACT)

    def test_frozen_policy_bytes_are_checked_before_copying_or_training(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();original=source(root);run=root/'benchmarks/trial';run.mkdir(parents=True)
            saved=original/'frozen-evaluations/milestone-123/BlockNationsSeatV2-123.pt'
            saved.write_bytes(saved.read_bytes()+b'changed')
            with self.assertRaises(ValueError):league.install(root,original,run,CONTRACT)
            self.assertFalse((run/'retained-opponents.json').exists())

    def test_gentle_recipe_loads_as_stock_ppo_with_explicit_constant_schedules(self):
        gentle=RunOptions.from_dict(recipe(250000,32,'gentle')).behaviors['BlockNationsSeatV4']
        self.assertEqual(gentle.hyperparameters.learning_rate,.000075)
        self.assertEqual(gentle.hyperparameters.beta_schedule.value,'constant')
        self.assertEqual(gentle.hyperparameters.epsilon_schedule.value,'constant')
        self.assertEqual(gentle.network_settings.hidden_units,32)
        self.assertTrue(gentle.hyperparameters.shared_critic)
        with self.assertRaises(ValueError):recipe(250000,32,'unknown')


if __name__=='__main__':unittest.main()
