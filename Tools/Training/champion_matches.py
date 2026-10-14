"""Direct incumbent challenges; fixed-reference scores remain separate diagnostics."""
import copy
from pathlib import Path

from playtest import atomic_json, read_json


def suites(fixed_suite, incumbent_manifest):
    manifest = read_json(Path(incumbent_manifest))
    from evaluate_suite import digest
    reference = dict(kind='frozen', manifest=str(incumbent_manifest),
                     checkpointSha256=manifest['sha256'],
                     configSha256=digest(Path(manifest['trainerConfig'])))
    primary = {k: copy.deepcopy(v) for k, v in fixed_suite.items() if k != 'cases'}
    primary['cases'] = [dict(name='champion-'+mode.lower(), seed=610014, gamesPerCell=32,
                            difficulty=mode, referenceDifficulty=mode, reference=reference)
                        for mode in ('Hard', 'Policy')]
    from tournament_selection import confirmation_suite
    return primary, confirmation_suite(primary)


def challenge(run, candidate_manifest, incumbent_manifest, fixed_suite, worker, destination):
    from evaluate_suite import run as evaluate
    destination = Path(destination)
    destination.mkdir(exist_ok=False)
    evidence = {}
    for label, suite in zip(('primary', 'confirmation'), suites(read_json(Path(fixed_suite)), incumbent_manifest)):
        path = destination/(label+'-suite.json')
        atomic_json(path, suite)
        evaluate(candidate_manifest, path, worker, destination/label)
        evidence[label] = destination/label/'suite.json'
    return evidence


def assess(primary, confirmation, incumbent, candidate):
    """Balanced complete matches, both deployed and raw sampling, two seed packs.

    A five point win margin and non-losing role results avoid promoting on a tie.
    Deterministic openings can repeat: these are empirical gates, not IID claims.
    """
    reasons, scores = [], []
    for key in ('rulesVersion', 'boardSize', 'openingEconomyVersion', 'workerSha256', 'packages', 'evaluatorSources'):
        if primary.get(key) != confirmation.get(key):
            raise ValueError('Direct confirmation requires the same evaluator and rules.')
    for pack in (primary, confirmation):
        if pack['candidate'] != candidate or len(pack['cases']) != 2 or len(pack['results']) != 2:
            raise ValueError('Direct challenge must evaluate the exact candidate in both sampling modes.')
        if {c['difficulty'] for c in pack['cases']} != {'Hard', 'Policy'}:
            raise ValueError('Direct challenge requires Hard and Policy sampling.')
        for case, result in zip(pack['cases'], pack['results']):
            ref = case['reference']
            if (ref.get('kind') != 'frozen' or any(ref.get(k) != incumbent[k] for k in ('sha256', 'configSha256', 'actorId')) or
                    case['referenceDifficulty'] != case['difficulty'] or case['gamesPerCell'] < 32):
                raise ValueError('Direct challenge must face the exact incumbent with matched sampling and sufficient games.')
            roles = result['startingPositionResults']
            if (result['games'] != 4*case['gamesPerCell'] or
                    len(roles) != 2 or {r['candidateStarts'] for r in roles} != {True, False} or
                    any(r['games'] != result['games']//2 for r in roles)):
                raise ValueError('Direct challenge requires complete balanced starting roles.')
            win_rate = result['wins']/result['games']
            scores.append(dict(mode=case['difficulty'], seed=case['seed'], games=result['games'],
                               wins=result['wins'], winRate=win_rate, roles=roles))
            if result['interruptions']:
                reasons.append('interrupted direct matches')
            if win_rate < .55:
                reasons.append('direct win rate below 55%: '+case['difficulty'])
            if any(r['wins']/r['games'] < .5 for r in roles):
                reasons.append('losing starting role: '+case['difficulty'])
    for a, b in zip(primary['cases'], confirmation['cases']):
        if a['seed'] == b['seed'] or {k: v for k, v in a.items() if k != 'seed'} != {k: v for k, v in b.items() if k != 'seed'}:
            raise ValueError('Direct confirmation requires distinct seeds and unchanged opponents.')
    return dict(accepted=not reasons, reasons=sorted(set(reasons)), scores=scores,
                criterion='direct-incumbent-v1',
                evidence='two seed packs, balanced colours and roles; empirical margins, not IID confidence')
