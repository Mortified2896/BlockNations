"""Bounded training-only weakness feedback; acceptance results never set weights."""
import copy
import hashlib
from pathlib import Path
import time

from playtest import read_json, atomic_json
from weakness_sampling import weights

OWNER = 'BlockNations.TrainingFeedback.v1'


def enabled(run):
    config = read_json(Path(run)/'training-feedback.json')
    if not config or config.get('enabled') is False:
        return None
    if (config.get('owner') != OWNER or config.get('version') != 1 or config.get('runId') != Path(run).name or
            config.get('enabled') is not True or type(config.get('championSlot')) is not int or
            not 0 <= config['championSlot'] < 8):
        raise ValueError('Training feedback requires explicit owned configuration and a bounded champion slot.')
    return config


def effective_recipe(run, rules):
    from training_opponents import load_recipe, validate_recipe
    run = Path(run)
    recipe = load_recipe(run, rules)
    if not enabled(run):
        return recipe
    runtime = read_json(run/'training-league.json')
    if not runtime:
        return recipe
    if (runtime.get('owner') != OWNER or runtime.get('runId') != run.name or
            runtime.get('baseRecipeSha256') != hashlib.sha256((run/'training-opponents.json').read_bytes()).hexdigest() or
            runtime.get('partition') != 'training'):
        raise ValueError('Adaptive league is unrelated to this training recipe.')
    return validate_recipe(run, runtime['recipe'], rules)


class Feedback:
    def __init__(self, run, rules, session):
        self.run, self.rules, self.session = Path(run), rules, session
        self.config = enabled(run)
        self.pending = {}
        self.next_publish = 0
        self.selected_digest = None
        self.champion_entry = None
        self.rows = {}
        if self.config:
            old = read_json(self.run/'training-league.json')
            if (old.get('owner') == OWNER and old.get('partition') == 'training' and old.get('runId') == self.run.name and
                    old.get('baseRecipeSha256') == hashlib.sha256((self.run/'training-opponents.json').read_bytes()).hexdigest()):
                self.rows = old.get('recentResults', {})
                if not isinstance(self.rows, dict) or any(not isinstance(value, dict) or set(value) != {'True', 'False'} or
                    any(not isinstance(items, list) or len(items) > 256 or any(type(x) is not int or x not in (0, 1) for x in items)
                        for items in value.values()) for value in self.rows.values()):
                    raise ValueError('Invalid bounded training feedback history.')
            self.publish(force=True)

    def observe(self, event):
        if not self.config or event.get('session') != self.session:
            return
        worker = event.get('worker', 0)
        if event.get('kind') == 'begin':
            self.pending[worker] = event
            return
        opening = self.pending.pop(worker, None)
        if (not opening or opening['match'] != event['match'] or not opening.get('fullOpening') or
                event.get('interrupted') or event.get('policyChanged') or event.get('winner') not in (0, 1)):
            return
        context = opening.get('context') or {}
        opponent = context.get('opponent')
        if opponent not in self.identities or context.get('learningSeat') not in (0, 1):
            return
        role = str(context['learningSeat'] == opening['firstSeat'])
        rows = self.rows.setdefault(opponent, {'True': [], 'False': []})[role]
        rows.append(int(event['winner'] != context['learningSeat']))
        del rows[:-256]

    def publish(self, force=False):
        if not self.config or not force and time.monotonic() < self.next_publish:
            return
        self.next_publish = time.monotonic()+5
        from training_opponents import load_recipe, validate_recipe, entry_id
        from playtest_champion import resolve_champion, digest
        from playtest_sampling import PRESETS
        recipe = copy.deepcopy(load_recipe(self.run, self.rules))
        if recipe is None or recipe['version'] != 2 or len(recipe['opponents']) < 4:
            raise ValueError('Adaptive feedback requires four to eight retained challengers.')
        selected = read_json(self.run/'playtest-champion.json')
        selected_digest = selected.get('checkpointSha256')
        if selected_digest != self.selected_digest:
            checkpoint = resolve_champion(self.run)
            if checkpoint is None:
                raise ValueError('Adaptive feedback requires a verified selected champion.')
            manifest = read_json(self.run/selected['candidateManifest'])
            config = Path(manifest['trainerConfig'])
            self.champion_entry = dict(kind='frozen-policy', weight=1, sampling='Hard',
                checkpoint=str(checkpoint.relative_to(self.run)), trainerConfig=str(config.relative_to(self.run)),
                checkpointSha256=digest(checkpoint), configSha256=digest(config), actorId=manifest['actorId'],
                samplingSettings=list(PRESETS['Hard']))
            self.selected_digest = selected_digest
        identities = [entry_id(e, self.rules) for e in recipe['opponents']]
        champion_id = entry_id(self.champion_entry, self.rules)
        if champion_id not in identities:
            if self.config['championSlot'] >= len(recipe['opponents']):
                raise ValueError('Champion slot exceeds the retained recipe.')
            recipe['opponents'][self.config['championSlot']] = copy.deepcopy(self.champion_entry)
        self.identities = [entry_id(e, self.rules) for e in recipe['opponents']]
        self.rows = {k: self.rows.get(k, {'True': [], 'False': []}) for k in self.identities}
        # Equalize first/second contributions; retain broad coverage until all
        # challengers have enough real training evidence from both roles.
        enough = all(min(len(self.rows[k][r]) for r in ('True', 'False')) >= 32 for k in self.identities)
        losses, games = [], []
        for k in self.identities:
            count = min(len(self.rows[k][r]) for r in ('True', 'False'))
            losses.append(round(sum(sum(self.rows[k][r])/max(1, len(self.rows[k][r])) for r in ('True', 'False'))*count))
            games.append(count*2)
        values = weights(losses, games) if enough else [1/len(self.identities)]*len(self.identities)
        for entry, value in zip(recipe['opponents'], values):
            entry['weight'] = value*100
        recipe['probability'] = .5
        validate_recipe(self.run, recipe, self.rules)
        atomic_json(self.run/'training-league.json', dict(owner=OWNER, version=1, runId=self.run.name,
            partition='training', source='completed-live-training-matches', recipe=recipe, recentResults=self.rows,
            adapted=enough, championSha256=self.selected_digest, updatedUnix=time.time(),
            baseRecipeSha256=hashlib.sha256((self.run/'training-opponents.json').read_bytes()).hexdigest()))
