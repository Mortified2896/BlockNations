"""Audited Python boundary for the shared C# worker and learned schema v2."""
import re
from pathlib import Path

RULES_VERSION = 'blocknations-simulation-v4'
OBSERVATIONS = 3120
ACTIONS = 259
OPENING_ECONOMY_VERSION = 2
LEGACY_OPENING_ECONOMY_VERSION = 1


def validate_opening_economy(version):
    if type(version) is not int or version not in (LEGACY_OPENING_ECONOMY_VERSION, OPENING_ECONOMY_VERSION):
        raise ValueError('Unsupported opening economy version.')
    return version


def checkpoint_step(path):
    match = re.fullmatch(r'BlockNationsSeatV2-(\d+)\.pt', Path(path).name)
    if not match:
        raise ValueError('Use an immutable numbered checkpoint.')
    return int(match[1])


def latest_saved_step(run):
    directory = Path(run)/'checkpoints'/Path(run).name/'BlockNationsSeatV2'
    return max((checkpoint_step(path) for path in directory.glob('BlockNationsSeatV2-*.pt')), default=0)
