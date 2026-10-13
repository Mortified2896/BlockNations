"""Verified fixed-reference results, separate from changing training-pool Elo."""
from pathlib import Path
import time
from playtest import read_json, atomic_json
from playtest_champion import checked_evaluation, resolve_champion


def score(receipt, checkpoint):
    results = [r for case, r in zip(receipt['cases'], receipt['results']) if case['reference']['kind'] != 'self']
    if not results:
        raise ValueError('A strength score requires fixed opponents.')
    roles = [next(p for p in r['startingPositionResults'] if p['candidateStarts'] == starts)
             for starts in (True, False) for r in results]
    split = len(results)
    return dict(checkpoint=checkpoint.stem, games=sum(r['games'] for r in results),
                wins=sum(r['wins'] for r in results), losses=sum(r['losses'] for r in results),
                interruptions=sum(r['interruptions'] for r in results),
                firstWins=sum(r['wins'] for r in roles[:split]), firstGames=sum(r['games'] for r in roles[:split]),
                secondWins=sum(r['wins'] for r in roles[split:]), secondGames=sum(r['games'] for r in roles[split:]))


def publish(run, candidate_manifest, candidate_results):
    run = Path(run)
    selected = read_json(run / 'playtest-champion.json')
    resolve_champion(run)
    champion, baseline = checked_evaluation(run, run / selected['candidateManifest'], run / selected['evaluation'])
    candidate, recent = checked_evaluation(run, candidate_manifest, candidate_results)
    if baseline['suiteId'] != recent['suiteId'] or baseline['cases'] != recent['cases']:
        raise ValueError('Strength scores require the same fixed benchmark.')
    status = dict(version=1, source='verified-fixed-reference-v1', runId=run.name,
                  boardSize=baseline['boardSize'], rulesVersion=baseline['rulesVersion'], suiteId=baseline['suiteId'],
                  updatedUnix=time.time(), champion=score(baseline, champion), latest=score(recent, candidate))
    selection = read_json(run/'playtest-champion.json')
    status['promotionEvidence'] = selection.get('promotionEvidence')
    atomic_json(run / 'fixed-reference-status.json', status)
    return status
