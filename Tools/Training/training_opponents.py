"""Optional fixed challengers for self-play; never replace a learner's action.

Recipes are per-run development data. The default is stock learned self-play.
The challenger uses the same seat projection and action schema as the learner.
"""
import hashlib
import json
import math
import re
from pathlib import Path

from training_contract import (RULES_VERSION, OPENING_ECONOMY_VERSION, checkpoint_step,
                               CORE_THREE_RULES, validate_rules_version, validate_transfer, run_rules_version)
from playtest_sampling import PRESETS


def entries(recipe):
    return recipe['opponents'] if recipe['version'] == 2 else [recipe]


def owned_frozen_path(run, relative):
    if not isinstance(relative, str) or Path(relative).is_absolute():
        raise ValueError('Use a retained opponent file relative to its owned run.')
    path = (Path(run)/relative).resolve()
    base = Path(run).resolve()/'frozen-evaluations'
    if base.is_symlink() or not path.is_relative_to(base) or not path.is_file():
        raise ValueError('Retained opponents must remain inside the owned frozen-evaluations directory.')
    return path


def checked_contents(path, expected, maximum):
    if (not isinstance(expected,str) or len(expected) != 64 or
            any(c not in '0123456789abcdef' for c in expected) or path.stat().st_size > maximum):
        raise ValueError('Invalid retained opponent digest or file size.')
    contents = path.read_bytes()
    if hashlib.sha256(contents).hexdigest() != expected:
        raise ValueError('Retained opponent content changed; preserve its original files.')
    return contents


def load_recipe(run, rules_version=RULES_VERSION):
    path = Path(run)/'training-opponents.json'
    if not path.exists():
        return None
    return validate_recipe(run,json.loads(path.read_text()), rules_version)


def validate_recipe(run, recipe, rules_version=RULES_VERSION):
    rules_version = validate_rules_version(rules_version)
    if not isinstance(recipe,dict):
        raise ValueError('Opponent recipe needs a versioned definition.')
    if (type(recipe.get('version')) is not int or recipe['version'] not in (1,2) or
            recipe.get('rulesVersion') != rules_version or
            type(recipe.get('probability')) not in (int, float) or
            not 0 <= recipe['probability'] <= .5):
        raise ValueError('Incompatible training opponent recipe; keep at least half the games as learned self-play.')
    if recipe['version'] == 2:
        manifest = json.loads((Path(run)/'run.json').read_text())
        if (manifest.get('owner') != 'BlockNations.LocalTraining.v1' or manifest.get('schema') != 2 or
                recipe.get('boardSize') != manifest.get('boardSize') or run_rules_version(run) != rules_version or
                recipe.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION or
                manifest.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION or
                not isinstance(recipe.get('opponents'),list) or not 1 <= len(recipe['opponents']) <= 8):
            raise ValueError('Retained opponent league belongs to another board, opening or run contract.')
    for entry in entries(recipe):
        if not isinstance(entry,dict):
            raise ValueError('Each league opponent needs a versioned definition.')
        weight = entry.get('weight',1)
        if type(weight) not in (int,float) or not math.isfinite(weight) or not 0 < weight <= 100:
            raise ValueError('Opponent weights must be positive and bounded.')
        if entry.get('kind') == 'fair-tactician':
            if type(entry.get('workBudget')) is not int or not 1 <= entry['workBudget'] <= 2048:
                raise ValueError('Invalid fair tactical work budget.')
            recruit = entry.get('recruitType', '')
            if not isinstance(recruit, str) or (recruit and not re.fullmatch(r'[A-Za-z0-9_-]{1,64}', recruit)):
                raise ValueError('Use an optional roster type identifier for a tactical reference.')
            if rules_version == CORE_THREE_RULES and recruit == 'scout':
                raise ValueError('A Scout-only reference cannot recruit under the three-unit rules.')
        elif recipe['version'] == 2 and entry.get('kind') == 'frozen-policy':
            checkpoint = owned_frozen_path(run,entry.get('checkpoint'))
            checkpoint_step(checkpoint)
            checked_contents(checkpoint,entry.get('checkpointSha256'),100_000_000)
            config = owned_frozen_path(run,entry.get('trainerConfig'))
            plan = json.loads(checked_contents(config,entry.get('configSha256'),256_000))
            frozen = json.loads((checkpoint.parent/'manifest.json').read_text())
            validate_transfer(frozen.get('rulesVersion'), rules_version, entry.get('transferRules'))
            if (frozen.get('sha256') != entry['checkpointSha256'] or
                    frozen.get('boardSize') != recipe['boardSize'] or
                    frozen.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION or
                    entry.get('sampling') not in ('Policy','Easy','Medium','Hard') or
                    entry.get('samplingSettings') != (list(PRESETS[entry['sampling']]) if entry['sampling'] in PRESETS else None) or
                    not isinstance(entry.get('actorId'),str) or len(entry['actorId']) != 24 or
                    any(c not in '0123456789abcdef' for c in entry['actorId']) or
                    plan['behaviors']['BlockNationsSeatV2']['network_settings'].get('memory')):
                raise ValueError('Retained model provenance or sampling is incompatible.')
        else:
            raise ValueError('Only fair tactical and retained learned opponents are supported.')
    if len(set(entry_id(entry, rules_version) for entry in entries(recipe))) != len(entries(recipe)):
        raise ValueError('Duplicate league policies should use one weighted entry.')
    return recipe


def recipe_id(recipe):
    if recipe['version'] == 2:
        return 'league-' + hashlib.sha256(json.dumps(recipe,sort_keys=True).encode()).hexdigest()[:24]
    return entry_id(recipe, recipe['rulesVersion'])


def entry_id(entry, rules_version=RULES_VERSION):
    rules_version = validate_rules_version(rules_version)
    # Include the actual policy implementation revision and rules. This is a
    # distinct rated entity, not a hash pretending to be neural weights.
    if entry['kind'] == 'frozen-policy':
        # Unmodified identical actors are the same rated entity, regardless of
        # checkpoint packaging or trainer settings. Sampling presets are distinct.
        if entry['sampling'] == 'Policy' and rules_version == RULES_VERSION:return entry['actorId']
        return 'sampled-' + hashlib.sha256(json.dumps(dict(actor=entry['actorId'],
            sampling=entry['sampling'], settings=entry['samplingSettings'], rules=rules_version),
            sort_keys=True).encode()).hexdigest()[:24]
    identity = dict(kind=entry['kind'], implementation='hard-tactician-v3',
                    rules=rules_version, work=entry['workBudget'])
    # Preserve existing unrestricted identities; a restricted reference is a
    # distinct opponent, never a restriction on the learning seat's roster.
    if entry.get('recruitType'):
        identity['recruitType'] = entry['recruitType']
    return 'tactician-' + hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()[:24]


def challenger_ids(recipe):
    return [entry_id(entry, recipe['rulesVersion']) for entry in entries(recipe)] if recipe else []


class TrainingOpponents:
    def __init__(self, run, seed, rules_version=RULES_VERSION):
        self.run = Path(run)
        self.rules_version = validate_rules_version(rules_version)
        self.recipe = load_recipe(run, self.rules_version)
        self.entries = entries(self.recipe) if self.recipe else []
        self.seed = seed
        self.assignments = {}
        self.randoms = {}
        self.actors = {}
        self.decisions = 0
        self.decisions_by_policy = {}

    @property
    def identifier(self):
        return recipe_id(self.recipe) if self.recipe else None

    def selected(self, stats, policy):
        if not self.recipe or not policy:
            return []
        selected = []
        for stat in stats:
            key = (stat['worker'], stat['match'], stat['trainerResets'], policy['learningSeat'])
            previous = self.assignments.get(stat['worker'])
            if previous is None or previous[0] != key:
                sample = int.from_bytes(hashlib.sha256((str(self.seed) + ':' + repr(key)).encode()).digest()[:8], 'big')/2**64
                index = -1
                if sample < self.recipe['probability']:
                    total = sum(entry.get('weight',1) for entry in self.entries)
                    draw = int.from_bytes(hashlib.sha256(('entry:'+str(self.seed)+':'+repr(key)).encode()).digest()[:8],'big')/2**64*total
                    for index,entry in enumerate(self.entries):
                        draw -= entry.get('weight',1)
                        if draw < 0:break
                previous = (key, index)
                self.assignments[stat['worker']] = previous
                if index >= 0 and self.entries[index]['kind'] == 'frozen-policy':
                    import numpy as np
                    rng_seed = int.from_bytes(hashlib.sha256(('actions:'+str(self.seed)+':'+repr(key)).encode()).digest()[:8],'big')
                    self.randoms[stat['worker']] = np.random.default_rng(rng_seed)
            if previous[1] >= 0:
                selected.append(stat['worker'])
        return selected

    def prepare(self, env, actions, policy):
        selected = self.selected(env.stats, policy)
        if not selected:
            return actions, None
        opponent_seat = 1-policy['learningSeat']
        agents = [agent for agent in actions if agent//2 in selected and agent%2 == opponent_seat]
        replaced = dict(actions)
        for index, entry in enumerate(self.entries):
            group = [agent for agent in agents if self.assignments[agent//2][1] == index]
            if not group:continue
            if entry['kind'] == 'fair-tactician':
                options = dict(work_budget=entry['workBudget'], agents=group)
                if entry.get('recruitType'):
                    if entry['recruitType'] not in getattr(env, 'enabled_roster', env.roster):
                        raise ValueError('Tactical reference recruit type is absent from the current roster.')
                    options['recruit_type'] = entry['recruitType']
                advice = env.reference_actions(opponent_seat, **options)
            else:
                from frozen_policy import FrozenPolicy, evaluation_weights
                name = 'BlockNationsSeatV2?team='+str(opponent_seat)
                if index not in self.actors:
                    config_path = owned_frozen_path(self.run,entry['trainerConfig'])
                    plan = json.loads(checked_contents(config_path,entry['configSha256'],256_000))
                    self.actors[index] = FrozenPolicy(owned_frozen_path(self.run,entry['checkpoint']),
                        plan['behaviors']['BlockNationsSeatV2']['network_settings'], env.behavior_specs[name],
                        entry['checkpointSha256'],entry['actorId'])
                decisions,_ = env.get_steps(name)
                rows = [decisions.agent_id_to_index[agent] for agent in group]
                mask = decisions.action_mask[0][rows]
                weights = evaluation_weights(self.actors[index].probabilities(decisions.obs[0][rows],mask),entry['sampling'])
                advice = {agent:int(self.randoms[agent//2].choice(weights.shape[1],p=weights[row])) for row,agent in enumerate(group)}
                if any(mask[row,advice[agent]] for row,agent in enumerate(group)):
                    raise ValueError('Retained opponent produced an illegal choice.')
            if set(advice) != set(group):
                raise ValueError('Training challenger advice did not cover the selected opponent agents.')
            replaced.update(advice)
            self.decisions += len(advice)
            identifier = entry_id(entry, self.rules_version)
            self.decisions_by_policy[identifier] = self.decisions_by_policy.get(identifier,0)+len(advice)
        assignments = [dict(policy, opponent=entry_id(self.entries[self.assignments[worker][1]], self.rules_version))
                       if worker in selected else policy for worker in range(env.workers)]
        return replaced, assignments
