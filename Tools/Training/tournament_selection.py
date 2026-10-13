"""Comparable champion evidence and replicated, bounded promotion decisions."""
import copy
import hashlib
import json
from pathlib import Path
import time
import uuid

from playtest import atomic_json, read_json

DEFAULT_POLICY = dict(minimumGain=0.01, maximumRoleDrop=0.02, maximumOpponentDrop=0.05,
                      regressionDrop=0.05, regressionPatience=3)


def policy(value=None):
    result = dict(DEFAULT_POLICY, **(value or {}))
    if (set(result) != set(DEFAULT_POLICY) or
            any(type(result[k]) not in (int, float) or not 0 <= result[k] <= .1
                for k in ('minimumGain', 'maximumRoleDrop', 'maximumOpponentDrop', 'regressionDrop')) or
            result['minimumGain'] <= 0 or result['regressionDrop'] <= 0 or
            type(result['regressionPatience']) is not int or not 3 <= result['regressionPatience'] <= 10):
        raise ValueError('Use bounded positive promotion margins and at least three regression evaluations.')
    return result


def confirmation_suite(suite):
    result = copy.deepcopy(suite)
    for case in result['cases']:
        case['seed'] = (case['seed'] + 1000003) % (2**31)
    return result


def external(receipt):
    return [(c, r) for c, r in zip(receipt['cases'], receipt['results']) if c['reference']['kind'] != 'self']


def rate(receipt):
    rows = external(receipt)
    return sum(r['wins'] for _, r in rows) / sum(r['games'] for _, r in rows)


def assess(old, new, old_repeat, new_repeat, margins=None):
    """Require independent seed packs; do not pretend repeated geometry is IID."""
    limits = policy(margins)
    for a, b in ((old, new), (old_repeat, new_repeat)):
        if a['suiteId'] != b['suiteId'] or a['cases'] != b['cases']:
            raise ValueError('Promotion requires identical benchmark identity within each paired pack.')
    for key in ('rulesVersion', 'boardSize', 'openingEconomyVersion', 'workerSha256', 'packages', 'evaluatorSources'):
        if old.get(key) != old_repeat.get(key):
            raise ValueError('Confirmation must use the same rules, worker and evaluator.')
    if len(old['cases']) != len(old_repeat['cases']):
        raise ValueError('Confirmation must retain every case.')
    for a, b in zip(old['cases'], old_repeat['cases']):
        if a['seed'] == b['seed'] or {k: v for k, v in a.items() if k != 'seed'} != {k: v for k, v in b.items() if k != 'seed'}:
            raise ValueError('Confirmation requires distinct seeds with unchanged references and sampling.')
    deltas = [rate(new)-rate(old), rate(new_repeat)-rate(old_repeat)]
    failures = []
    if min(deltas) + 1e-12 < limits['minimumGain']:
        failures.append('gain did not reach the margin on both seed packs')
    for before, after in ((old, new), (old_repeat, new_repeat)):
        rows = list(zip(external(before), external(after)))
        if any(n['interruptions'] > o['interruptions'] for (_, o), (_, n) in rows):
            failures.append('increased interruptions')
        for (_, o), (case, n) in rows:
            if n['wins']/n['games'] - o['wins']/o['games'] < -limits['maximumOpponentDrop']-1e-12:
                failures.append('opponent regression: ' + case['name'])
        for starts in (True, False):
            o = [next(s for s in r['startingPositionResults'] if s['candidateStarts'] == starts) for _, r in external(before)]
            n = [next(s for s in r['startingPositionResults'] if s['candidateStarts'] == starts) for _, r in external(after)]
            if sum(s['wins'] for s in n)/sum(s['games'] for s in n) - sum(s['wins'] for s in o)/sum(s['games'] for s in o) < -limits['maximumRoleDrop']-1e-12:
                failures.append('starting-role regression')
    return dict(accepted=not failures, deltas=deltas, policy=limits,
                evidence='two separate seed packs; empirical margins, not an IID confidence claim',
                reasons=sorted(set(failures)))


def baseline(run, manifest, suite, worker):
    from evaluate_suite import prepare, run as evaluate
    from playtest_champion import checked_evaluation
    _, _, _, definition = prepare(manifest, suite)
    cache = Path(run)/'evaluations/baselines'/definition['suiteId']/hashlib.sha256(Path(manifest).read_bytes()).hexdigest()[:16]
    receipt = cache/'suite.json'
    if receipt.exists():
        _, result = checked_evaluation(Path(run), manifest, receipt)
        if result['suiteId'] != definition['suiteId']:
            raise ValueError('Cached baseline identity changed.')
        return receipt
    # A cancelled partial baseline is never reused or overwritten.
    target = cache if not cache.exists() else cache.with_name(cache.name+'-'+uuid.uuid4().hex[:8])
    target.parent.mkdir(parents=True, exist_ok=True)
    evaluate(manifest, suite, worker, target)
    checked_evaluation(Path(run), manifest, target/'suite.json')
    return target/'suite.json'


def refresh(run, evaluation):
    """Refresh evidence for the exact same actor; archive the original pointer."""
    from playtest_champion import checked_evaluation, resolve_champion, digest
    run = Path(run)
    previous = read_json(run/'playtest-champion.json')
    checkpoint = resolve_champion(run)
    manifest = run/previous['candidateManifest']
    refreshed, receipt = checked_evaluation(run, manifest, evaluation)
    if refreshed != checkpoint or digest(refreshed) != previous['checkpointSha256']:
        raise ValueError('Baseline refresh cannot select different weights.')
    if previous['evaluation'] == str(Path(evaluation).relative_to(run)):
        return previous
    history = run/'champion-history'
    history.mkdir(exist_ok=True)
    archive = history/(str(time.time_ns())+'.json')
    atomic_json(archive, previous)
    selection = dict(previous, evaluation=str(Path(evaluation).relative_to(run)),
                     evaluationSha256=digest(evaluation), suiteId=receipt['suiteId'],
                     evidenceRefreshedUnix=time.time())
    atomic_json(run/'playtest-champion.json', selection)
    return selection


def review(run, old, new, old_repeat, new_repeat, candidate_step, margins=None):
    """Repeated measured regressions request a saved stop, never replace weights."""
    run = Path(run)
    limits = policy(margins)
    path = run/'training-review.json'
    previous = read_json(path)
    if previous and (previous.get('owner') != 'BlockNations.TrainingReview.v1' or previous.get('runId') != run.name):
        raise ValueError('Unrelated review state was preserved.')
    if previous.get('state') == 'review_required':
        return previous
    if previous.get('candidateStep') == candidate_step:
        return previous
    identity = (old['suiteId'], old_repeat['suiteId'], old['candidate']['sha256'])
    count = previous.get('consecutiveRegressions', 0) if previous.get('baselineIdentity') == list(identity) else 0
    deltas = [rate(new)-rate(old), rate(new_repeat)-rate(old_repeat)]
    count = count+1 if max(deltas) <= -limits['regressionDrop'] else 0
    value = dict(owner='BlockNations.TrainingReview.v1', version=1, runId=run.name,
                 state='review_required' if count >= limits['regressionPatience'] else 'monitoring',
                 consecutiveRegressions=count, baselineIdentity=list(identity), candidateStep=candidate_step,
                 deltas=deltas, updatedUnix=time.time(),
                 reason='Repeated benchmark regressions; review recipe before resuming.' if count >= limits['regressionPatience'] else '')
    atomic_json(path, value)
    return value
