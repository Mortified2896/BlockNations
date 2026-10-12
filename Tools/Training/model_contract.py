"""Versioned observation contracts; actions retain their original v2 meanings."""
import re
from pathlib import Path

SIZES = {2: 3120, 3: 3259, 4: 4712}


def validate_schema(schema):
    if type(schema) is not int or schema not in SIZES:
        raise ValueError('Choose an explicit supported observation contract: 2, 3 or 4.')
    return schema


def behavior(schema):
    return 'BlockNationsSeatV' + str(validate_schema(schema))


def checkpoint_schema(path):
    match = re.fullmatch(r'BlockNationsSeatV([234])-\d+\.pt', Path(path).name)
    if not match:
        raise ValueError('Use an immutable numbered checkpoint with its observation version.')
    return int(match[1])


def spec(schema):
    from mlagents_envs.base_env import BehaviorSpec, ObservationSpec, DimensionProperty, ObservationType, ActionSpec
    return BehaviorSpec([ObservationSpec((SIZES[validate_schema(schema)],), (DimensionProperty.NONE,), ObservationType.DEFAULT, 'VectorSensor')],
                        ActionSpec.create_discrete((259,)))


def names(schema):
    return [behavior(schema) + '?team=' + str(seat) for seat in (0, 1)]


def prefix(observations, source_schema, target_schema):
    validate_schema(source_schema); validate_schema(target_schema)
    if target_schema > source_schema or observations.ndim != 2 or observations.shape[1] != SIZES[source_schema]:
        raise ValueError('Only a recorded unchanged observation prefix may feed an older actor.')
    return observations[:, :SIZES[target_schema]]
