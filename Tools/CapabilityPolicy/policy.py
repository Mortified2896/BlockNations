"""Opt-in spatial/capability actor for the existing fair v4 observation contract.

Recruit entries use one encoder and one score function, without slot embeddings
or unit names. This architecture has its own weights; legacy MLP checkpoints
cannot initialize it by tensor padding. Game rules and observations are unchanged.
"""
from pathlib import Path

import torch
from torch import nn
from mlagents.trainers.torch_entities.action_model import ActionModel
from mlagents.trainers.torch_entities.networks import SimpleActor, Critic

ARCHITECTURE = 'shared-capability-v1'
OBSERVATIONS = 4712
ACTIONS = 259
POSITIONS = 121
RECRUITS = 16
RECRUIT_OFFSET = 2904
GLOBALS_OFFSET = 3112
UNIT_CAPABILITY_OFFSET = 3120
RECRUIT_CAPABILITY_OFFSET = 3241
ROLE_OFFSET = 3257
HISTORY_OFFSET = 3259


class CapabilityBody(nn.Module):
    memory_size = 0

    def __init__(self, width):
        super().__init__()
        # 24 current tile fields, AttackEndsMovement, 12 legitimate history
        # fields, and canonical coordinates. Padding contributes no embedding.
        self.spatial = nn.Sequential(nn.Conv2d(39, width, 3, padding=1), nn.ReLU(),
                                     nn.Conv2d(width, width, 3, padding=1), nn.ReLU())
        self.recruit = nn.Sequential(nn.Linear(14, width), nn.ReLU(),
                                     nn.Linear(width, width), nn.ReLU())
        self.context = nn.Sequential(nn.Linear(3*width+9, width), nn.ReLU(),
                                     nn.Linear(width, width), nn.ReLU())
        self.tile_score = nn.Sequential(nn.Linear(2*width, width), nn.ReLU(), nn.Linear(width, 2))
        self.recruit_score = nn.Sequential(nn.Linear(2*width, width), nn.ReLU(), nn.Linear(width, 1))
        self.end_score = nn.Linear(width, 1)
        y, x = torch.meshgrid(torch.linspace(-1, 1, 11), torch.linspace(-1, 1, 11), indexing='ij')
        self.register_buffer('coordinates', torch.stack((x, y), dim=-1).reshape(1, POSITIONS, 2))

    def update_normalization(self, buffer):
        # Public schema fields already have fixed documented scales.
        pass

    def board_context(self, squares, present):
        return squares.sum(dim=1)/present.sum(dim=1).clamp(min=1)

    def forward(self, inputs, actions=None, memories=None, sequence_length=1):
        if not torch.onnx.is_in_onnx_export():
            if len(inputs) != 1 or inputs[0].ndim != 2 or inputs[0].shape[1] != OBSERVATIONS:
                raise ValueError('Capability policy requires the exact fair observation-v4 vector.')
        if actions is not None:
            raise ValueError('Capability body does not accept extra action encodings.')
        values = inputs[0]
        batch = values.shape[0]
        tiles = values[:, :RECRUIT_OFFSET].reshape(batch, POSITIONS, 24)
        present = tiles[:, :, :1]
        history = values[:, HISTORY_OFFSET:-1].reshape(batch, POSITIONS, 12)
        unit_capability = values[:, UNIT_CAPABILITY_OFFSET:RECRUIT_CAPABILITY_OFFSET].reshape(batch, POSITIONS, 1)
        fields = torch.cat((tiles, unit_capability, history, self.coordinates.expand(batch, -1, -1)), dim=-1)
        board = (fields*present).transpose(1, 2).reshape(batch, 39, 11, 11)
        squares = self.spatial(board).flatten(2).transpose(1, 2)*present
        board_mean = self.board_context(squares, present)
        selected = tiles[:, :, 20:21]*present
        source = (squares*selected).sum(dim=1)/selected.sum(dim=1).clamp(min=1)

        recruits = values[:, RECRUIT_OFFSET:GLOBALS_OFFSET].reshape(batch, RECRUITS, 13)
        recruit_capability = values[:, RECRUIT_CAPABILITY_OFFSET:ROLE_OFFSET].reshape(batch, RECRUITS, 1)
        recruit_fields = torch.cat((recruits, recruit_capability), dim=-1)
        recruit_embeddings = self.recruit(recruit_fields)
        enabled = recruits[:, :, :1]
        roster_mean = (recruit_embeddings*enabled).sum(dim=1)/enabled.sum(dim=1).clamp(min=1)
        public_globals = torch.cat((values[:, GLOBALS_OFFSET:UNIT_CAPABILITY_OFFSET],
                                    values[:, ROLE_OFFSET:ROLE_OFFSET+1]), dim=-1)
        context = self.context(torch.cat((board_mean, source, roster_mean, public_globals), dim=-1))
        tile_logits = self.tile_score(torch.cat((squares, context.unsqueeze(1).expand(-1, POSITIONS, -1)), dim=-1))
        recruit_logits = self.recruit_score(torch.cat((recruit_embeddings, context.unsqueeze(1).expand(-1, RECRUITS, -1)), dim=-1)).reshape(batch, RECRUITS)
        # Keep the authoritative two-stage action indices: select/move squares,
        # attack squares, recruit slots, EndTurn. The external mask owns legality.
        logits = torch.cat((tile_logits[:, :, 0], tile_logits[:, :, 1], recruit_logits, self.end_score(context)), dim=1)
        return torch.cat((logits, context), dim=1), None


class CandidateLogits(nn.Module):
    def forward(self, encoding):
        return encoding[:, :ACTIONS]


class CapabilityActor(SimpleActor, Critic):
    """Use stock ML-Agents sampling/PPO with shared capability policy/value heads."""
    def __init__(self, observation_specs, network_settings, action_spec, stream_names=('extrinsic',),
                 conditional_sigma=False, tanh_squash=False):
        if (len(observation_specs) != 1 or tuple(observation_specs[0].shape) != (OBSERVATIONS,) or
                action_spec.continuous_size != 0 or list(action_spec.discrete_branches) != [ACTIONS] or
                network_settings.normalize or network_settings.memory is not None or network_settings.num_layers != 2 or
                type(network_settings.hidden_units) is not int or not 16 <= network_settings.hidden_units <= 128 or
                conditional_sigma or tanh_squash or not stream_names):
            raise ValueError('Use v4/259, width 16–128, fixed scales and a shared nonrecurrent capability actor.')
        super().__init__(observation_specs, network_settings, action_spec, False, False)
        self.network_body = CapabilityBody(network_settings.hidden_units)
        self.encoding_size = ACTIONS+network_settings.hidden_units
        self.action_model = ActionModel(self.encoding_size, action_spec, deterministic=network_settings.deterministic)
        self.action_model._discrete_distribution.branches[0] = CandidateLogits()
        self.stream_names = list(stream_names)
        self.value_heads = nn.ModuleDict({name: nn.Linear(network_settings.hidden_units, 1) for name in self.stream_names})

    def critic_pass(self, inputs, memories=None, sequence_length=1):
        encoding, _ = self.network_body(inputs, memories=memories, sequence_length=sequence_length)
        return {name: head(encoding[:, ACTIONS:]).reshape(-1) for name, head in self.value_heads.items()}, None


class ProbabilityPolicy(nn.Module):
    def __init__(self, actor):
        super().__init__()
        self.actor = actor

    def forward(self, observations, allowed):
        encoding, _ = self.actor.network_body([observations])
        return self.actor.action_model._get_dists(encoding, allowed).discrete[0].probs


def probabilities(actor, observations, allowed):
    """Checked Python boundary; exported inference must provide the same contract."""
    if (observations.ndim != 2 or observations.shape[1] != OBSERVATIONS or
            allowed.shape != (observations.shape[0], ACTIONS) or not torch.isfinite(observations).all() or
            not torch.isfinite(allowed).all() or not ((allowed == 0) | (allowed == 1)).all() or
            not (allowed.sum(dim=1) > 0).all() or not (observations[:, 3119] == 2).all() or
            not (observations[:, 3258] == 3).all() or not (observations[:, -1] == 4).all()):
        raise ValueError('Provide finite v4 observations and a binary mask with legal choices for each row.')
    return ProbabilityPolicy(actor)(observations, allowed)


def export(actor, observations, allowed, destination):
    """Probability-only prototype export; no release selection or Assets writes."""
    import onnx
    destination = Path(destination)
    if destination.exists():
        raise ValueError('Preserve the existing export and use a new destination.')
    probabilities(actor, observations, allowed)
    actor.eval()
    torch.onnx.export(ProbabilityPolicy(actor), (observations, allowed), str(destination),
                      input_names=['obs_0', 'action_masks'], output_names=['policy_probabilities'],
                      dynamic_axes={name: {0: 'batch'} for name in ['obs_0', 'action_masks', 'policy_probabilities']},
                      opset_version=14)
    model = onnx.load(str(destination))
    model.doc_string = ''; model.graph.doc_string = ''
    for node in model.graph.node:
        node.doc_string = ''
    onnx.checker.check_model(model)
    onnx.save(model, str(destination))
