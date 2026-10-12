"""Frozen training-diagnostic weighting, never acceptance-test-driven adaptation."""
import copy
import math


def weights(losses, games, *, uniform_share=.5, cap=.25):
    if (not isinstance(losses, list) or not isinstance(games, list) or len(losses) != len(games) or
            not 4 <= len(games) <= 8 or not .5 <= uniform_share <= 1 or not 1/len(games) <= cap <= .5):
        raise ValueError('Use bounded broad-coverage challenger weighting.')
    scores = []
    for lost, played in zip(losses, games):
        if type(lost) is not int or type(played) is not int or not 0 <= lost <= played or played < 4:
            raise ValueError('Training diagnostics need completed counts for every challenger.')
        scores.append((lost+1)/(played+2))
    total = sum(scores)
    values = [uniform_share/len(scores) + (1-uniform_share)*s/total for s in scores]
    # Water filling redistributes capped mass without abandoning coverage.
    for _ in range(len(values)+1):
        excess = sum(max(0, v-cap) for v in values)
        values = [min(cap, v) for v in values]
        eligible = [i for i, v in enumerate(values) if v < cap-1e-12]
        if excess < 1e-12: break
        if not eligible: raise ValueError('Infeasible sampling cap.')
        for i in eligible: values[i] += excess/len(eligible)
    if abs(sum(values)-1) > 1e-9 or not all(math.isfinite(v) for v in values):
        raise ValueError('Invalid bounded sampling weights.')
    return values


def apply(recipe, diagnostic):
    from training_opponents import entry_id
    if diagnostic.get('version') != 1 or diagnostic.get('partition') != 'training' or diagnostic.get('interruptions') != 0:
        raise ValueError('Weakness weighting uses explicit training diagnostics, never held-out acceptance or interrupted results.')
    original = copy.deepcopy(recipe)
    ids = [entry_id(e, recipe['rulesVersion']) for e in recipe['opponents']]
    cells = diagnostic.get('opponents', [])
    if len(cells) != len(ids) or {c.get('id') for c in cells} != set(ids):
        raise ValueError('Training diagnostic must cover each exact recorded challenger identity.')
    by_id = {c['id']:c for c in cells}
    # Average role-conditioned failure rates; require evidence from both roles.
    rates, totals = [], []
    for identity in ids:
        roles = by_id[identity]['roles']
        if len(roles) != 2 or {r.get('candidateStarts') for r in roles} != {True, False}:
            raise ValueError('Training diagnostics require both starting roles.')
        for role in roles:
            if type(role.get('losses')) is not int or type(role.get('games')) is not int or not 0 <= role['losses'] <= role['games'] or role['games'] < 4:
                raise ValueError('Invalid role-conditioned diagnostic counts.')
        common = min(r['games'] for r in roles)
        rates.append(round(sum(r['losses']/r['games'] for r in roles)*common))
        totals.append(2*common)
    for entry, weight in zip(original['opponents'], weights(rates, totals)):
        entry['weight'] = weight*100
    return original
