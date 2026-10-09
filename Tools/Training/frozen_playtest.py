"""Stock ML-Agents inference with strict checkpoint loading (never random fallback)."""
from mlagents.trainers import learn
from mlagents.trainers.model_saver.torch_model_saver import TorchModelSaver
from mlagents.torch_utils import torch
from interactive_environment import install as install_interactive_environment


def strict_load(self, load_path, policy=None, reset_global_steps=False):
    modules = self.modules if policy is None else policy.get_modules()
    saved = torch.load(load_path, map_location="cpu")
    for name, module in modules.items():
        if isinstance(module, torch.nn.Module):
            module.load_state_dict(saved[name], strict=True)
        else:
            module.load_state_dict(saved[name])
    active_policy = self.policy if policy is None else policy
    if reset_global_steps:
        active_policy.set_step(0)


if __name__ == "__main__":
    import os
    import mlagents.trainers
    from playtest_sampling import install as install_difficulty
    if mlagents.trainers.__version__ != "1.1.0":
        raise RuntimeError("Frozen playtests require the audited ML-Agents 1.1.0 runtime.")
    TorchModelSaver._load_model = strict_load
    install_difficulty(os.environ.get('BLOCKNATIONS_PLAYTEST_DIFFICULTY', 'Medium'))
    install_interactive_environment(learn)
    learn.main()
