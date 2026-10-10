"""Optional fixed challengers for self-play; never replace a learner's action.

Recipes are per-run development data. The default is stock learned self-play.
The challenger uses the same seat projection and action schema as the learner.
"""
import hashlib
import json
from pathlib import Path

from training_contract import RULES_VERSION


def load_recipe(run):
    path = Path(run)/'training-opponents.json'
    if not path.exists():
        return None
    recipe = json.loads(path.read_text())
    if (recipe.get('version') != 1 or recipe.get('rulesVersion') != RULES_VERSION or
            recipe.get('kind') != 'fair-tactician' or
            type(recipe.get('probability')) not in (int, float) or
            not 0 <= recipe['probability'] <= .5 or
            type(recipe.get('workBudget')) is not int or not 1 <= recipe['workBudget'] <= 2048):
        raise ValueError('Incompatible training opponent recipe; keep at least half the games as learned self-play.')
    return recipe


def recipe_id(recipe):
    # Include the actual policy implementation revision and rules. This is a
    # distinct rated entity, not a hash pretending to be neural weights.
    return 'tactician-' + hashlib.sha256(json.dumps(dict(kind=recipe['kind'],
        implementation='hard-tactician-v3', rules=RULES_VERSION, work=recipe['workBudget']),
        sort_keys=True).encode()).hexdigest()[:24]


class TrainingOpponents:
    def __init__(self, run, seed):
        self.recipe = load_recipe(run)
        self.seed = seed
        self.assignments = {}
        self.decisions = 0

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
                previous = (key, sample < self.recipe['probability'])
                self.assignments[stat['worker']] = previous
            if previous[1]:
                selected.append(stat['worker'])
        return selected

    def prepare(self, env, actions, policy):
        selected = self.selected(env.stats, policy)
        if not selected:
            return actions, None
        opponent_seat = 1-policy['learningSeat']
        agents = [agent for agent in actions if agent//2 in selected and agent%2 == opponent_seat]
        replaced = dict(actions)
        if agents:
            advice = env.reference_actions(opponent_seat, work_budget=self.recipe['workBudget'], agents=agents)
            if set(advice) != set(agents):
                raise ValueError('Training challenger advice did not cover the selected opponent agents.')
            replaced.update(advice)
            self.decisions += len(advice)
        assignments = [dict(policy, opponent=self.identifier) if worker in selected else policy for worker in range(env.workers)]
        return replaced, assignments
