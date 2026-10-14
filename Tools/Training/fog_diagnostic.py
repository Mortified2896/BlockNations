"""Frozen matched fog/full-vision evaluation; never trains or promotes an actor."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import time

from evaluate_suite import worker_digest
from information_study_runtime import PowerGuard, evaluate_or_resume
from supervisor import ROOT_MARKER, atomic_json, size


def paired_outcomes(control, treatment):
    def index(pack):
        rows = {}
        for case, result in zip(pack['cases'], pack['results']):
            for game in result['records']:
                key = (case['name'], result['seed'], game['firstSeat'], game['candidateSeat'], game['worker'], game['game'])
                if key in rows:
                    raise ValueError('Duplicate paired match identity.')
                rows[key] = 'limit' if game['interrupted'] else 'win' if game['winner'] == game['candidateSeat'] else 'loss'
        return rows
    before, after = index(control), index(treatment)
    if before.keys() != after.keys():
        raise ValueError('Vision treatments did not cover the same matched openings and sampling seeds.')
    transitions = {a+'-to-'+b: 0 for a in ('win','loss','limit') for b in ('win','loss','limit')}
    for key in before:
        transitions[before[key]+'-to-'+after[key]] += 1
    return transitions


def full_pack(folder):
    pack = json.loads((folder/'suite.json').read_text())
    pack['results'] = [json.loads((folder/case['name']/'evaluation.json').read_text()) for case in pack['cases']]
    return pack


def run(root, parent, suite, worker, destination, *, power_controlled=False, resume=False):
    root, parent, suite, worker, destination = (Path(p).absolute() for p in (root,parent,suite,worker,destination))
    if (json.loads((root/ROOT_MARKER).read_text()).get('owner') != 'BlockNations.LocalTraining.v1' or
            any(p.is_symlink() or p.resolve()!=p for p in (root,parent,suite,worker,destination)) or
            not parent.is_relative_to(root/'runs') or not destination.is_relative_to(root/'benchmarks')):
        raise ValueError('Use owned immutable inputs and a new diagnostic destination under the marked root.')
    destination.mkdir(parents=True, exist_ok=resume)
    definition = json.loads(suite.read_text())
    definition.update(workerSha256=worker_digest(worker), diagnosticOnly=True)
    pins = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in
            [parent,suite,*Path(__file__).parent.glob('*.py')]}
    power, next_check = PowerGuard(power_controlled), 0
    receipt = dict(owner='BlockNations.FogDiagnostic.v1', state='running', diagnosticOnly=True,
                   parent=str(parent), parentSha256=json.loads(parent.read_text())['sha256'],
                   workerDigest=worker_digest(worker), pins=pins, startedUnix=time.time(), results={})
    if resume:
        old=json.loads((destination/'diagnostic.json').read_text())
        if any(old[key]!=receipt[key] for key in ('parent','parentSha256','workerDigest','pins')):
            raise ValueError('Resume requires the exact original diagnostic inputs.')
    def guard():
        nonlocal next_check
        power.check()
        if (destination/'stop.request').exists(): raise RuntimeError('Diagnostic stopped by request.')
        if time.monotonic()<next_check: return
        next_check=time.monotonic()+30
        if size(root)>19_488_000_000 or shutil.disk_usage(root).free<20_512_000_000:
            raise RuntimeError('Shared storage reserve reached.')
        if worker_digest(worker)!=receipt['workerDigest'] or any(hashlib.sha256(Path(p).read_bytes()).hexdigest()!=h for p,h in pins.items()):
            raise RuntimeError('Diagnostic inputs changed.')
    try:
        for vision in ('fog','candidate','both'):
            path=destination/(vision+'-suite.json')
            if not path.exists(): atomic_json(path,dict(definition,diagnosticFullVision=vision))
            pack=evaluate_or_resume(parent,path,worker,destination/vision,guard)
            rows=[r for c,r in zip(pack['cases'],pack['results']) if c['reference']['kind']!='self']
            receipt['results'][vision]=dict(wins=sum(r['wins'] for r in rows),games=sum(r['games'] for r in rows),
                interruptions=sum(r['interruptions'] for r in rows),suiteId=pack['suiteId'])
            atomic_json(destination/'diagnostic.json',receipt)
        control=full_pack(destination/'fog')
        receipt['paired']={vision:paired_outcomes(control,full_pack(destination/vision)) for vision in ('candidate','both')}
        receipt.update(state='completed', interpretation='Frozen input ablation; distribution shift and symmetric opponent gains require matched continued training. Fixed geometry and repeated deterministic games are not independent statistical samples.')
    except BaseException as error:
        receipt.update(state='failed',error=str(error));raise
    finally:
        receipt['finishedUnix']=time.time();atomic_json(destination/'diagnostic.json',receipt)
    return receipt


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for field in ('root','parent','suite','worker','destination'): parser.add_argument('--'+field,type=Path,required=True)
    parser.add_argument('--power-controlled',action='store_true');parser.add_argument('--resume',action='store_true')
    args=parser.parse_args()
    from mlagents.torch_utils import torch
    torch.set_num_threads(1)
    print(json.dumps(run(args.root,args.parent,args.suite,args.worker,args.destination,power_controlled=args.power_controlled,resume=args.resume),indent=2))
