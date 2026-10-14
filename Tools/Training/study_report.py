"""Recompute paired study results from frozen artifacts and complete match records.

Read-only with respect to experiments, learners and model selection. A report is
an evidence snapshot, not a process-health check or proof of human strength.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import time

from supervisor import ROOT_MARKER, atomic_json

DEFINITION = ('version', 'rulesVersion', 'boardSize', 'openingEconomyVersion',
              'cases', 'workerSha256', 'packages', 'evaluatorSources',
              'diagnosticOnly', 'diagnosticFullVision', 'diagnosticSampling')
ENVIRONMENT = ('rulesVersion', 'boardSize', 'openingEconomyVersion',
               'workerSha256', 'packages', 'evaluatorSources',
               'diagnosticOnly', 'diagnosticFullVision', 'diagnosticSampling')


def read(path, *, hashed=False):
    path = Path(path)
    if path.is_symlink() or path.stat().st_size > 16_000_000:
        raise ValueError('Choose a bounded, unlinked evidence file.')
    contents = path.read_bytes()
    value = json.loads(contents)
    return (value, hashlib.sha256(contents).hexdigest()) if hashed else value


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def owned(root, path):
    path = Path(path).absolute()
    if path.resolve() != path or not any(path.is_relative_to(root/name) for name in ('runs', 'benchmarks')):
        raise ValueError('Evidence must remain below the owned runs or benchmark root.')
    return path


def manifest(root, path):
    path = owned(root, path)
    value = read(path)
    checkpoint = owned(root, value['checkpoint'])
    config = owned(root, value['trainerConfig'])
    if (checkpoint.parent != path.parent or config.parent != path.parent or
            digest(checkpoint) != value['sha256'] or digest(config) != value['trainerConfigSha256']):
        raise ValueError('Frozen model weights/configuration changed.')
    return value


def checked_pack(root, path, frozen):
    path = owned(root, path); pack = read(path)
    canonical = {k: pack[k] for k in DEFINITION if k in pack}
    suite_id = hashlib.sha256(json.dumps(canonical, sort_keys=True).encode()).hexdigest()
    identity = pack.get('candidate', {})
    if (pack.get('state') != 'completed' or pack.get('suiteId') != suite_id or
            identity.get('sha256') != frozen['sha256'] or
            identity.get('configSha256') != frozen['trainerConfigSha256']):
        raise ValueError('Use a complete content-bound pack for the exact frozen actor.')
    cases, results = pack.get('cases', []), pack.get('results', [])
    if not cases or len(cases) != len(results) or len({c['name'] for c in cases}) != len(cases):
        raise ValueError('A complete pack must retain every unique opponent case.')
    cells = []
    for case, result in zip(cases, results):
        if type(case.get('gamesPerCell')) is not int or case['gamesPerCell'] <= 0:
            raise ValueError('Each case needs a positive balanced game count.')
        full = read(owned(root, path.parent/case['name']/'evaluation.json'))
        records = full.get('records', [])
        reference = case['reference']
        expected = dict(seed=case['seed'], difficulty=case['difficulty'],
                        referenceDifficulty=case['referenceDifficulty'], candidateSha256=frozen['sha256'])
        expected.update({k: pack[k] for k in ('rulesVersion', 'boardSize', 'openingEconomyVersion')})
        if reference['kind'] == 'frozen':
            expected['referenceSha256'] = reference['sha256']
        elif reference['kind'] == 'self':
            expected['referenceSha256'] = frozen['sha256']
        elif reference['kind'] == 'tactical':
            expected.update(reference=reference['implementation'], referenceWork=reference['workBudget'],
                            referenceRecruitType=reference['recruitType'])
        else:
            raise ValueError('Unknown benchmark opponent kind.')
        if (result.get('case') != case['name'] or result.get('games') != 4*case['gamesPerCell'] or
                len(records) != result['games'] or any(result.get(k) != v for k, v in expected.items()) or
                any(full.get(k) != v for k, v in result.items() if k != 'case')):
            raise ValueError('Aggregated results disagree with their complete match records.')
        identifiers = set()
        for record in records:
            if (type(record.get('candidateSeat')) is not int or record['candidateSeat'] not in (0, 1) or
                    type(record.get('firstSeat')) is not int or record['firstSeat'] not in (0, 1) or
                    type(record.get('interrupted')) is not bool or
                    type(record.get('winner')) is not int or record['winner'] not in (-1, 0, 1) or
                    not record['interrupted'] and record['winner'] == -1 or
                    record.get('diagnosticFullVision', 'fog') != pack.get('diagnosticFullVision', 'fog')):
                raise ValueError('Invalid or visibility-mismatched authoritative match outcome.')
            key = tuple(record[k] for k in ('candidateSeat', 'firstSeat', 'worker', 'game'))
            if key in identifiers:
                raise ValueError('Repeated raw match identifiers cannot count as independent games.')
            identifiers.add(key)
        colours = Counter((r['candidateSeat'], r['firstSeat']) for r in records)
        if any(colours[(seat, first)] != case['gamesPerCell'] for seat in (0, 1) for first in (0, 1)):
            raise ValueError('Raw games must balance colours and starting roles.')
        wins = sum(not r['interrupted'] and r['winner'] == r['candidateSeat'] for r in records)
        interrupted = sum(r['interrupted'] for r in records)
        if (result['wins'] != wins or result['interruptions'] != interrupted or
                result['losses'] != len(records)-wins-interrupted):
            raise ValueError('Wins, losses and interruptions disagree with raw outcomes.')
        roles = result.get('startingPositionResults', [])
        if (len(roles) != 2 or any(type(r['candidateStarts']) is not bool for r in roles) or
                {r['candidateStarts'] for r in roles} != {True, False}):
            raise ValueError('Retain both starting-role summaries.')
        for starts in (True, False):
            selected = [r for r in records if (r['candidateSeat'] == r['firstSeat']) == starts]
            role = next(r for r in roles if r['candidateStarts'] == starts)
            measured = dict(games=len(selected), wins=sum(not r['interrupted'] and r['winner'] == r['candidateSeat'] for r in selected),
                            interruptions=sum(r['interrupted'] for r in selected))
            measured['losses'] = measured['games']-measured['wins']-measured['interruptions']
            if any(role.get(k) != v for k, v in measured.items()):
                raise ValueError('Starting-role summary disagrees with raw outcomes.')
            if reference['kind'] != 'self':
                cells.append(dict(opponent=case['name'], candidateStarts=starts,
                                  sampling=case['difficulty'], referenceSampling=case['referenceDifficulty'],
                                  **measured, winRate=measured['wins']/measured['games']))
    if not cells:
        raise ValueError('A study needs external opponents, not only self-comparisons.')
    summary = dict(suiteId=pack['suiteId'], candidate=identity, cells=cells,
                   diagnosticOnly=pack.get('diagnosticOnly', False),
                   wins=sum(c['wins'] for c in cells), games=sum(c['games'] for c in cells),
                   interruptions=sum(c['interruptions'] for c in cells),
                   meanWinRate=sum(c['winRate'] for c in cells)/len(cells),
                   worstRoleWinRate=min(c['winRate'] for c in cells),
                   weighting='equal weight per opponent and starting role')
    return pack, summary


def comparable(before, after, *, confirmation=False):
    if any(before.get(k) != after.get(k) for k in ENVIRONMENT) or len(before['cases']) != len(after['cases']):
        raise ValueError('Do not compare different rules, workers, evaluator versions or opponent panels.')
    if not confirmation:
        if before['suiteId'] != after['suiteId'] or before['cases'] != after['cases']:
            raise ValueError('Paired comparisons require the identical frozen pack.')
    else:
        for first, second in zip(before['cases'], after['cases']):
            if (first['seed'] == second['seed'] or
                    {k: v for k, v in first.items() if k != 'seed'} != {k: v for k, v in second.items() if k != 'seed'}):
                raise ValueError('Confirmation needs new seeds and the same opponent/sampling panel.')


def change(before, after):
    key = lambda c: (c['opponent'], c['candidateStarts'])
    old = {key(c): c for c in before['cells']}; new = {key(c): c for c in after['cells']}
    if old.keys() != new.keys():
        raise ValueError('Starting-role/opponent coverage changed.')
    return dict(meanGain=after['meanWinRate']-before['meanWinRate'],
                worstRoleChange=after['worstRoleWinRate']-before['worstRoleWinRate'],
                cellChanges=[dict(opponent=k[0], candidateStarts=k[1], gain=new[k]['winRate']-old[k]['winRate']) for k in old])


def report(root, destination):
    root, destination = Path(root).absolute(), Path(destination).absolute()
    if root.resolve() != root or read(root/ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1':
        raise ValueError('Choose the marked shared training root.')
    destination = owned(root, destination); study, study_hash = read(destination/'study.json', hashed=True)
    if study.get('owner') != 'BlockNations.InformationStudy.v1':
        raise ValueError('Choose an owned matched information/capacity study.')
    seeds = study['seeds']
    if not seeds or len(seeds) != len(set(seeds)) or any(type(s) is not int for s in seeds):
        raise ValueError('Study seeds must be distinct.')
    parents = [owned(root, p) for p in study['pins'] if p.endswith('/manifest.json')]
    if len(parents) != 1 or digest(parents[0]) != study['pins'][str(parents[0])]:
        raise ValueError('The protected study parent changed or is ambiguous.')
    parent = manifest(root, parents[0]); labels = ('primary', 'confirmation') if study.get('confirmation') else ('primary',)
    baseline = {}; baseline_packs = {}
    for label in labels:
        path = destination/('baseline' if label == 'primary' else 'baseline-confirmation')/'suite.json'
        if not path.exists() or read(path).get('state') != 'completed':
            return dict(version=1, state=study['state'], name=study['name'], complete=False,
                        stage='baseline', completedArms=len(study['results']), comparisons=[],
                        interpretation='A baseline is unfinished; no treatment-strength conclusion is available.')
        baseline_packs[label], baseline[label] = checked_pack(root, path, parent)
    if len(labels) == 2:
        comparable(baseline_packs['primary'], baseline_packs['confirmation'], confirmation=True)
    entries = {}; paired = []
    for result in study['results']:
        key = (result['seed'], result['arm'])
        if key in entries or key[0] not in seeds or key[1] not in ('control', 'treatment'):
            raise ValueError('Unexpected or duplicated study arm.')
        frozen = manifest(root, result['manifest'])
        deltas = list(result['optimizerStepDeltas'].values())
        if (frozen['step'] != result['actualStep'] or frozen['step'] < parent['step']+study['stepsPerArm'] or
                type(result['rejections']) is not int or result['rejections'] != 0 or not deltas or
                any(type(v) is not int or v <= 0 for v in deltas) or
                not math.isfinite(result['elapsedSeconds']) or result['elapsedSeconds'] < 0):
            raise ValueError('Completed arm failed its learning-budget, legality or optimizer checks.')
        folder = destination/(str(key[0])+'-'+key[1]); packs = {}; summaries = {}; gains = {}
        for label in labels:
            packs[label], summaries[label] = checked_pack(root, folder/('fixed-results' if label == 'primary' else 'confirmation-results')/'suite.json', frozen)
            comparable(baseline_packs[label], packs[label])
            summary = summaries[label]; prefix = 'external' if label == 'primary' else 'confirmation'
            if result[prefix+'Wins'] != summary['wins'] or result[prefix+'Games'] != summary['games']:
                raise ValueError('Study arm totals disagree with its raw benchmark evidence.')
            gains[label] = change(baseline[label], summary)
        if len(labels) == 2:
            comparable(packs['primary'], packs['confirmation'], confirmation=True)
            if packs['primary']['candidate'] != packs['confirmation']['candidate']:
                raise ValueError('Confirmation must evaluate the same exact frozen actor.')
        entries[key] = dict(step=frozen['step'], packs=summaries, changesFromParent=gains,
                            elapsedSeconds=result['elapsedSeconds'])
    for seed in seeds:
        row = dict(seed=seed, state='pending')
        for arm in ('control', 'treatment'):
            if (seed, arm) in entries: row[arm] = entries[(seed, arm)]; row['state'] = 'partial'
        if all((seed, arm) in entries for arm in ('control', 'treatment')):
            row.update(state='completed', treatmentMinusControl={label:change(row['control']['packs'][label], row['treatment']['packs'][label]) for label in labels})
        paired.append(row)
    complete = study['state'] == 'completed' and all(p['state'] == 'completed' for p in paired)
    if study['state'] == 'completed' and not complete:
        raise ValueError('A completed receipt is missing expected paired arms.')
    clean = not any(pack['interruptions'] for entry in entries.values() for pack in entry['packs'].values())
    positive = complete and clean and len(seeds) >= 3 and len(labels) == 2 and all(
        p['treatmentMinusControl'][label]['meanGain'] > 0 for p in paired for label in labels)
    return dict(version=1, name=study['name'], treatment=study['treatment'], state=study['state'],
                complete=complete, completedArms=len(entries), expectedArms=2*len(seeds),
                baseline=baseline, comparisons=paired,
                replicatedPositivePairedMeans=positive if complete else None,
                generatedUnix=time.time(), studySha256=study_hash,
                interpretation='Report each continuation seed, opponent and role. Positive means alone do not establish retained worst-role strength, champion superiority, human strength or a promotion. Deterministic repeated openings are not IID samples.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True); parser.add_argument('--study', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(); value = report(args.root, args.study)
    output = owned(args.root.absolute(), args.output)
    if output.parent != args.study.absolute() or not output.name.endswith('-report.json'):
        raise ValueError('Write a separate report inside the study directory; preserve source receipts.')
    atomic_json(output, value)
    print(json.dumps({k:value[k] for k in ('state', 'complete', 'completedArms')}))
