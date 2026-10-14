"""Broad Hard-strength promotion without requiring every matchup to improve."""
import math
from pathlib import Path

from playtest import atomic_json, read_json


def scores(pack):
    roles = []
    opponents = 0
    if len(pack['cases']) != len(pack['results']):
        raise ValueError('Population panel is incomplete.')
    for case, result in zip(pack['cases'], pack['results']):
        if case['reference']['kind'] == 'self':
            continue
        if case['difficulty'] != 'Hard' or case['referenceDifficulty'] != 'Hard':
            raise ValueError('Population promotion requires uniform Hard sampling for both sides.')
        cells = result['startingPositionResults']
        if (result['games'] != 4*case['gamesPerCell'] or len(cells) != 2
                or {r['candidateStarts'] for r in cells} != {True, False}
                or any(r['games'] != result['games']//2 or not 0 <= r['wins'] <= r['games'] for r in cells)
                or sum(r['wins'] for r in cells) != result['wins']):
            raise ValueError('Population promotion needs complete balanced starting roles.')
        if result['interruptions'] or any(r['interruptions'] for r in cells):
            raise ValueError('Interrupted population matches cannot establish promotion.')
        roles.extend(r['wins']/r['games'] for r in cells)
        opponents += 1
    if opponents < 3:
        raise ValueError('Use at least three fixed population opponents.')
    return dict(mean=sum(roles)/len(roles), worstRole=min(roles), opponents=opponents)


def assess(old, new, old_repeat, new_repeat, *, minimum_gain=.01, maximum_worst_role_drop=.05):
    if (any(type(v) not in (int, float) or not math.isfinite(v) for v in (minimum_gain, maximum_worst_role_drop))
            or not 0 < minimum_gain <= .1 or not 0 <= maximum_worst_role_drop <= .1):
        raise ValueError('Use bounded population promotion margins.')
    for before, after in ((old, new), (old_repeat, new_repeat)):
        if before['suiteId'] != after['suiteId'] or before['cases'] != after['cases']:
            raise ValueError('Population comparisons require identical fixed panels within each pack.')
    for key in ('rulesVersion', 'boardSize', 'openingEconomyVersion', 'workerSha256', 'packages', 'evaluatorSources'):
        if any(p.get(key) != old.get(key) for p in (new, old_repeat, new_repeat)):
            raise ValueError('Population confirmation requires the same rules and evaluator.')
    if old['candidate'] != old_repeat['candidate'] or new['candidate'] != new_repeat['candidate']:
        raise ValueError('Population confirmation must evaluate the exact same actors.')
    if len(old['cases']) != len(old_repeat['cases']):
        raise ValueError('Population confirmation must retain every opponent.')
    for first, repeat in zip(old['cases'], old_repeat['cases']):
        if first['seed'] == repeat['seed'] or {k:v for k,v in first.items() if k != 'seed'} != {k:v for k,v in repeat.items() if k != 'seed'}:
            raise ValueError('Population confirmation needs new seeds and unchanged opponents.')
    pairs = [(scores(before), scores(after)) for before, after in ((old, new), (old_repeat, new_repeat))]
    gains = [after['mean']-before['mean'] for before, after in pairs]
    worst = [after['worstRole']-before['worstRole'] for before, after in pairs]
    reasons = []
    if min(gains)+1e-12 < minimum_gain:
        reasons.append('mean gain below the required margin on a seed pack')
    if min(worst) < -maximum_worst_role_drop-1e-12:
        reasons.append('worst opponent/starting-role result fell beyond the margin')
    return dict(accepted=not reasons, reasons=reasons, criterion='replicated-hard-population-v1',
                meanGains=gains, worstRoleChanges=worst, scores=pairs,
                minimumGain=minimum_gain, maximumWorstRoleDrop=maximum_worst_role_drop,
                weighting='equal weight per fixed opponent and starting role',
                evidence='two seed packs; empirical gates; deterministic openings are not IID samples')


def review(run, manifest, suite, worker, folder):
    from evaluate_suite import run as evaluate, digest
    from playtest_champion import checked_evaluation
    from tournament_selection import baseline, confirmation_suite
    run, folder = Path(run), Path(folder)
    selected = read_json(run/'playtest-champion.json')
    incumbent = run/selected['candidateManifest']
    _, old = checked_evaluation(run, incumbent, run/selected['evaluation'])
    _, new = checked_evaluation(run, manifest, folder/'results/suite.json')
    repeat_suite = folder/'population-confirmation-suite.json'
    atomic_json(repeat_suite, confirmation_suite(read_json(Path(suite))))
    old_path = baseline(run, incumbent, repeat_suite, worker)
    new_path = folder/'population-confirmation/suite.json'
    evaluate(manifest, repeat_suite, worker, new_path.parent)
    _, old_repeat = checked_evaluation(run, incumbent, old_path)
    _, new_repeat = checked_evaluation(run, manifest, new_path)
    evidence = assess(old, new, old_repeat, new_repeat)
    evidence.update(primarySuiteId=old['suiteId'], confirmationSuiteId=old_repeat['suiteId'],
        confirmation={label:dict(path=str(path.relative_to(run)),sha256=digest(path))
                      for label,path in (('incumbent',old_path),('candidate',new_path))},
        candidateConfirmationGames=sum(r['games'] for r in new_repeat['results']))
    return evidence
