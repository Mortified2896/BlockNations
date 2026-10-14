"""Explicit experimental architectures; never guess a network from its tensors."""
from policy import CapabilityActor, ARCHITECTURE as DEFAULT
from spatial_policy import SpatialCapabilityActor, ARCHITECTURE as SPATIAL

ARCHITECTURES = {DEFAULT: CapabilityActor, SPATIAL: SpatialCapabilityActor}


def actor_class(architecture):
    if not isinstance(architecture, str) or architecture not in ARCHITECTURES:
        raise ValueError('Choose an explicit supported capability architecture.')
    return ARCHITECTURES[architecture]
