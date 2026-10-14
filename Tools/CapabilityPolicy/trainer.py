"""Explicit process-local custom PPO registration, pinned to audited SDK 1.1.0."""
import mlagents.trainers
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.ppo.trainer import PPOTrainer
from mlagents.trainers.ppo.optimizer_torch import PPOSettings

from policy import CapabilityActor

TRAINER = 'capability_ppo_v1'


class CapabilityPPOTrainer(PPOTrainer):
    @staticmethod
    def get_trainer_name():
        return TRAINER

    def create_policy(self, parsed_behavior_id, behavior_spec):
        if not self.shared_critic:
            raise ValueError('Capability PPO v1 requires its shared invariant critic.')
        return TorchPolicy(self.seed, behavior_spec, self.trainer_settings.network_settings,
                           CapabilityActor, dict(stream_names=[key.value for key in self.trainer_settings.reward_signals]))


def register():
    if mlagents.trainers.__version__ != '1.1.0':
        raise RuntimeError('Capability PPO adapter requires audited ML-Agents 1.1.0.')
    from mlagents import plugins
    plugins.all_trainer_types[TRAINER] = CapabilityPPOTrainer
    plugins.all_trainer_settings[TRAINER] = PPOSettings
