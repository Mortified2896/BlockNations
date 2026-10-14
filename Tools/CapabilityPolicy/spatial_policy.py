"""Version two preserves global square layout instead of averaging it away."""
from torch import nn
from mlagents.trainers.torch_entities.action_model import ActionModel

from policy import CapabilityActor, CapabilityBody, CandidateLogits, ACTIONS, POSITIONS

ARCHITECTURE = 'shared-capability-v2'


class SpatialCapabilityBody(CapabilityBody):
    def __init__(self, width):
        super().__init__(width)
        self.width = width
        self.context_width = 4*width
        self.global_projection = nn.Sequential(nn.Conv2d(width, 8, 1), nn.ReLU(), nn.Flatten(),
                                               nn.Linear(POSITIONS*8, self.context_width), nn.ReLU())
        self.context = nn.Sequential(nn.Linear(self.context_width+2*width+9, self.context_width), nn.ReLU(),
                                     nn.Linear(self.context_width, self.context_width), nn.ReLU())
        self.tile_score = nn.Sequential(nn.Linear(width+self.context_width, 2*width), nn.ReLU(), nn.Linear(2*width, 2))
        self.recruit_score = nn.Sequential(nn.Linear(width+self.context_width, 2*width), nn.ReLU(), nn.Linear(2*width, 1))
        self.end_score = nn.Linear(self.context_width, 1)

    def board_context(self, squares, present):
        board = (squares*present).transpose(1, 2).reshape(squares.shape[0], self.width, 11, 11)
        return self.global_projection(board)


class SpatialCapabilityActor(CapabilityActor):
    def __init__(self, observation_specs, network_settings, action_spec, stream_names=('extrinsic',),
                 conditional_sigma=False, tanh_squash=False):
        super().__init__(observation_specs, network_settings, action_spec, stream_names, conditional_sigma, tanh_squash)
        self.network_body = SpatialCapabilityBody(network_settings.hidden_units)
        self.encoding_size = ACTIONS+self.network_body.context_width
        self.action_model = ActionModel(self.encoding_size, action_spec, deterministic=network_settings.deterministic)
        self.action_model._discrete_distribution.branches[0] = CandidateLogits()
        self.value_heads = nn.ModuleDict({name: nn.Linear(self.network_body.context_width, 1) for name in self.stream_names})
