import base64
import json
from pathlib import Path
import tempfile
import unittest
import uuid

import numpy as np

from human_imitation import (HumanImitation, decode_game, RULES_VERSION, OBSERVATIONS, ACTIONS,
                            BATCHES_PER_UPDATE, BATCH_SIZE, PASSES_PER_GAME)


class HumanImitationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.run = Path(self.temporary.name) / 'test-run'
        (self.run / 'human-games').mkdir(parents=True)

    def tearDown(self):
        self.temporary.cleanup()

    def write_game(self, **overrides):
        values = np.zeros(OBSERVATIONS, dtype='<f4'); values[-1] = 2
        game = dict(version=1, schema=2, rulesVersion=RULES_VERSION, id=uuid.uuid4().hex,
                    boardSize=7, humanSeat=0, winnerSeat=0, completed=True, truncated=False,
                    samples=[dict(action=1, legal=[0, 1, ACTIONS-1], observation=base64.b64encode(values).decode())])
        game.update(overrides)
        path = self.run / 'human-games' / (game['id']+'.json')
        path.write_text(json.dumps(game))
        return path

    def test_only_compatible_completed_human_wins_are_training_examples(self):
        self.write_game(winnerSeat=1)
        self.write_game(completed=False, winnerSeat=-1)
        self.write_game(boardSize=6)
        self.write_game(rulesVersion='blocknations-simulation-v2')
        accepted = self.write_game()
        learning = HumanImitation(self.run, 7, 42)
        self.assertEqual(learning.saved_games, 5)
        self.assertEqual(list(learning.cache), [accepted])
        self.assertEqual(learning.updates, 0)

    def test_invalid_masks_and_nonfinite_tensors_never_reach_optimizer(self):
        path = self.write_game()
        original = json.loads(path.read_text())
        invalid = dict(original)
        invalid['samples'] = [dict(original['samples'][0], legal=[0, ACTIONS-1])]
        path.write_text(json.dumps(invalid))
        with self.assertRaises(ValueError): decode_game(path, 7)
        values = np.full(OBSERVATIONS, np.nan, dtype='<f4')
        invalid['samples'] = [dict(original['samples'][0], observation=base64.b64encode(values).decode())]
        path.write_text(json.dumps(invalid))
        with self.assertRaises(ValueError): decode_game(path, 7)
        learning = HumanImitation(self.run, 7, 42)
        self.assertFalse(learning.cache)
        self.assertIn('skipped', learning.error)

    def test_linked_recordings_are_rejected(self):
        path = self.write_game()
        contents = path.read_text(); path.unlink()
        foreign = self.run / 'outside.json'; foreign.write_text(contents)
        path.symlink_to(foreign)
        with self.assertRaises(ValueError): decode_game(path, 7)

    def test_new_games_are_ingested_without_restarting_the_learner(self):
        learning = HumanImitation(self.run, 7, 42)
        self.assertFalse(learning.cache)
        self.write_game()
        learning.poll(force=True)
        self.assertEqual(len(learning.cache), 1)
        batch = learning.sample_batch()
        self.assertEqual(batch[1].shape, (BATCH_SIZE, OBSERVATIONS))
        self.assertEqual(batch[2].shape, (BATCH_SIZE, ACTIONS))
        self.assertTrue(batch[2][:, 1].all())

    def test_actual_torch_actor_learns_demo_and_checkpoint_keeps_optimizer_state(self):
        from mlagents.torch_utils import torch
        from mlagents.trainers.policy.torch_policy import TorchPolicy
        from mlagents.trainers.settings import NetworkSettings
        from mlagents.trainers.torch_entities.networks import SimpleActor
        from mlagents.trainers.torch_entities.agent_action import AgentAction
        from dotnet_environment import SPEC
        self.write_game()
        learning = HumanImitation(self.run, 7, 42)
        policy = TorchPolicy(42, SPEC, NetworkSettings(hidden_units=128, num_layers=2), SimpleActor,
                             dict(conditional_sigma=False, tanh_squash=False))
        frozen = {name: value.clone() for name, value in policy.actor.state_dict().items()}
        optimizer = torch.optim.Adam(policy.actor.parameters(), lr=.001)
        batch = learning.sample_batch()
        _, obs, masks, actions = batch
        def loss():
            with torch.no_grad():
                stats = policy.actor.get_stats([torch.from_numpy(obs)],
                    AgentAction(None, [torch.from_numpy(actions[:, None])]), masks=torch.from_numpy(masks))
                return -stats['log_probs'].flatten().mean().item()
        before = loss()
        result = learning.update(policy, optimizer, 1234)
        after = loss()
        self.assertIsNotNone(result)
        self.assertLess(after, before)
        self.assertGreater(learning.used_examples, 0)
        self.assertLessEqual(learning.updates, BATCHES_PER_UPDATE)
        self.assertTrue(any(not torch.equal(v, frozen[k]) for k,v in policy.actor.state_dict().items()))
        self.assertEqual(optimizer.param_groups[0]['lr'], .001)
        # Standard actor and existing PPO Adam state remain ordinary checkpoint modules.
        checkpoint = self.run / 'checkpoint.pt'
        torch.save({'actor': policy.actor.state_dict(), 'optimizer': optimizer.state_dict()}, checkpoint)
        restored = torch.load(checkpoint)
        optimizer.load_state_dict(restored['optimizer'])
        self.assertGreater(len(optimizer.state), 0)
        resumed = HumanImitation(self.run, 7, 42)
        self.assertEqual(resumed.used_examples, learning.used_examples)
        self.assertEqual(resumed.consumed, learning.consumed)
        # A single tiny demonstration cannot produce unbounded imitation forever.
        self.assertIsNone(resumed.sample_batch())
        print(f'Actual masked human imitation loss: {before:.4f} -> {after:.4f}; {learning.used_examples} examples')

    def test_storage_and_sampling_are_bounded(self):
        for _ in range(36): self.write_game()
        learning = HumanImitation(self.run, 7, 42)
        self.assertEqual(len(learning.cache), 32)
        for value in learning.cache.values():
            learning.consumed[value[0]] = max(128, len(value[3])*PASSES_PER_GAME)
        self.assertIsNone(learning.sample_batch())


if __name__ == '__main__': unittest.main()
