"""Extract diagnostic positions from verified, completed automated game replays.

Uses the shared C# rules and fair observation history. It does not train, alter
games, read human-match folders, or label these positions as independent games.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

TRAINING = Path(__file__).resolve().parents[1] / 'Training'
sys.path.insert(0, str(TRAINING))
from supervisor import ROOT_MARKER, read_json, size

MEASUREMENT = 'verified-replay-position-joint-action-probabilities'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def extract(replays, probe, destination, provenance, root, maximum=32):
    root = root.absolute()
    if (root.is_symlink() or root.resolve() != root or read_json(root / ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1' or
            type(maximum) is not int or not 1 <= maximum <= 32 or not 1 <= len(replays) <= 8 or
            len(set(replays)) != len(replays) or destination.resolve() == provenance.resolve() or
            any(p.exists() or not p.resolve().is_relative_to(root / 'benchmarks') for p in (destination, provenance))):
        raise ValueError('Use distinct new artifacts in the marked benchmark folder and 1-8 automated replays.')
    if size(root) >= 20_000_000_000 - 514_097_152 or shutil.disk_usage(root).free < 20_512_000_000:
        raise ValueError('Preserve the shared storage reserve before extracting replay positions.')
    sources, eligible = [], []
    assembly = probe.parent / (probe.name + '.dll')
    fingerprints = {str(p): sha(p) for p in (Path(__file__), probe, assembly)}
    for number, path in enumerate(replays):
        resolved = path.resolve()
        if path.absolute() != resolved or not resolved.is_relative_to(root):
            raise ValueError('Replay sources must be canonical files inside the shared training root.')
        relative = resolved.relative_to(root)
        if (relative.parts[0] != 'benchmarks' and not (relative.parts[0] == 'runs' and 'evaluations' in relative.parts) or
                'replays' not in relative.parts or resolved.name not in {f'recent-{i}.json' for i in range(4)} or
                resolved.stat().st_size > 2_000_000):
            raise ValueError('Use bounded completed automated evaluation replays; human-match folders are excluded.')
        fingerprint = sha(resolved)
        result = subprocess.run([str(probe), '--replay-cases', str(resolved)], capture_output=True, check=True, timeout=60)
        payload = json.loads(result.stdout)
        if (payload.get('version') != 1 or payload.get('measurement') != MEASUREMENT or
                payload.get('rulesVersion') != 'blocknations-simulation-v5-core3' or payload.get('replaySha256') != fingerprint or
                not 2 <= payload.get('frames', 0) <= 256 or payload.get('verifiedTransitions') != payload['frames'] - 1 or
                not isinstance(payload.get('cases'), list) or len(payload['cases']) > 255):
            raise ValueError('Compiled replay verification differs from its supported contract.')
        source = dict(path=str(relative), sha256=fingerprint, frames=payload['frames'],
                      verifiedTransitions=payload['verifiedTransitions'], eligiblePositions=len(payload['cases']))
        assignments = [re.fullmatch(r'first-[01]-candidate-([01])', part) for part in relative.parts]
        candidate_seats = [int(match.group(1)) for match in assignments if match]
        source['recordedCandidateSeat'] = candidate_seats[0] if len(candidate_seats) == 1 else None
        sources.append(source)
        fingerprints[str(resolved)] = fingerprint
        for case in payload['cases']:
            original = case['name']
            case['name'] = f'replay_{number}_{original}'
            eligible.append((case, dict(name=case['name'], source=number, frame=int(original.split('_')[1]),
                                       recordedCandidateSeat=source['recordedCandidateSeat'])))
    if not eligible:
        raise ValueError('No recorded attack or visible city-capture opportunities were found.')
    count = min(maximum, len(eligible))
    indexes = [len(eligible) // 2] if count == 1 else [i * (len(eligible) - 1) // (count - 1) for i in range(count)]
    selected = [eligible[i] for i in indexes]
    contents = json.dumps([case for case, _ in selected], separators=(',', ':')) + '\n'
    receipt = dict(version=1, measurement=MEASUREMENT, rulesVersion='blocknations-simulation-v5-core3',
                   boardSize=7, casesSha256=hashlib.sha256(contents.encode()).hexdigest(),
                   history='same-seat observations at verified macro-action boundaries',
                   selection='evenly spaced recorded attack or visible city-capture opportunities',
                   eligiblePositions=len(eligible), selectedPositions=count, recordedGames=len(sources),
                   dependence='Frames within a game are dependent; separate replays may repeat deterministic lines.',
                   sources=sources, caseRecords=[record for _, record in selected],
                   extractorSha256=sha(Path(__file__)), probeAssemblySha256=sha(assembly))
    metadata = json.dumps(receipt, indent=2) + '\n'
    if any(len(s.encode()) > 1_048_576 for s in (contents, metadata)) or any(sha(p) != value for p, value in fingerprints.items()):
        raise ValueError('Replay inputs changed or extracted artifacts exceeded their 1 MiB bound.')
    for path in (destination, provenance):
        path.parent.mkdir(parents=True, exist_ok=True)
    with destination.open('x') as output:
        output.write(contents)
    try:
        with provenance.open('x') as output:
            output.write(metadata)
    except Exception:
        destination.unlink()
        raise
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--replay', type=Path, action='append', required=True)
    for name in ('probe', 'destination', 'provenance', 'root'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--maximum', type=int, default=32)
    args = parser.parse_args()
    result = extract(args.replay, args.probe, args.destination, args.provenance, args.root, args.maximum)
    print(json.dumps({key: result[key] for key in ('selectedPositions', 'eligiblePositions', 'recordedGames')}))
