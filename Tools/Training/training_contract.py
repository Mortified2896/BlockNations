"""Audited Python boundary for the shared C# worker and learned schema v2."""
import re
from pathlib import Path

RULES_VERSION = 'blocknations-simulation-v4'
ARCHER_VISION_RULES = 'blocknations-simulation-v5-vision2'
CORE_THREE_RULES = 'blocknations-simulation-v5-core3'
SUPPORTED_RULES = (RULES_VERSION, ARCHER_VISION_RULES, CORE_THREE_RULES)
POLICY_CATALOG = ('archer', 'rider', 'scout', 'warrior')
OBSERVATIONS = 3120
ACTIONS = 259
OPENING_ECONOMY_VERSION = 2
LEGACY_OPENING_ECONOMY_VERSION = 1


def validate_rules_version(version):
    if not isinstance(version, str) or version not in SUPPORTED_RULES:
        raise ValueError('Unsupported simulation rules version.')
    return version


def run_rules_version(run):
    import json
    path = Path(run)/'run.json'
    # Existing owned runs predate this field and retain the v4 contract.
    return validate_rules_version(json.loads(path.read_text()).get('rulesVersion', RULES_VERSION)) if path.exists() else RULES_VERSION


def transfer_contract(source, target):
    """Explicit permission to apply preserved weights to another audited profile.

    This does not claim the actor was trained under the target rules. These
    profiles share slot meanings and shapes; visibility/availability may change.
    """
    source, target = validate_rules_version(source), validate_rules_version(target)
    return dict(sourceRulesVersion=source, targetRulesVersion=target, modelSchema=2,
                policyCatalog=list(POLICY_CATALOG), observationSize=OBSERVATIONS, actionCount=ACTIONS)


def validate_transfer(source, target, record=None):
    source, target = validate_rules_version(source), validate_rules_version(target)
    if source == target and record is None:
        return
    if source == target or record != transfer_contract(source, target):
        raise ValueError('Rules transfer requires an explicit compatible source and target contract.')


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
