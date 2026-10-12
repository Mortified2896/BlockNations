"""Freeze only current owned training replays into a verified reachable corpus."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
from supervisor import ROOT_MARKER, size


def prepare(run,worker,destination):
    run,worker,destination=map(lambda p:Path(p).absolute(),(run,worker,destination))
    contract=json.loads((run/'run.json').read_text()); root=run.parent.parent
    if (run.is_symlink() or run.resolve()!=run or run.parent.name!='runs' or
            contract.get('owner')!='BlockNations.LocalTraining.v1' or
            json.loads((root/ROOT_MARKER).read_text()).get('owner')!='BlockNations.LocalTraining.v1' or
            not destination.is_relative_to(root/'benchmarks') or destination.resolve()!=destination or size(root)>19_472_000_000 or contract.get('rulesVersion')!='blocknations-simulation-v5-core3' or destination.exists()):
        raise ValueError('Choose a real owned core-three training run and a new corpus destination.')
    snapshots=[];provenance=[]
    for path in sorted((run/'workers').glob('*/replays/recent-*.json'))[:32]:
        if path.is_symlink() or not path.resolve().is_relative_to(run/'workers') or path.stat().st_size>2_000_000:raise ValueError('Linked or oversized training replay.')
        contents=path.read_bytes();trace=json.loads(contents)
        if trace.get('truncated') or not trace.get('frames') or not trace['frames'][-1].get('gameOver'):continue
        snapshots.append(trace);provenance.append(dict(file=str(path.relative_to(run)),sha256=hashlib.sha256(contents).hexdigest()))
    if not snapshots:raise ValueError('No complete current training replays are available.')
    destination.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.NamedTemporaryFile(mode='w',suffix='.json',dir=destination.parent,delete=False) as source:
        json.dump(snapshots,source);source_path=Path(source.name)
    try:
        command=[str(worker)] if worker.suffix!='.dll' else [os.environ.get('BLOCKNATIONS_DOTNET','dotnet'),str(worker)]
        result=subprocess.run(command+['--episode-corpus-input',str(source_path),'--episode-corpus-output',str(destination)],capture_output=True,text=True,timeout=120)
        if result.returncode:raise RuntimeError('Actual-rules corpus validation failed; no acceptance data was used. '+result.stderr[-1500:])
        receipt=dict(version=1,sourceRun=run.name,replays=provenance,corpusSha256=hashlib.sha256(destination.read_bytes()).hexdigest(),**json.loads(result.stdout))
        with destination.with_suffix('.provenance.json').open('x') as output:json.dump(receipt,output,indent=2)
        return receipt
    finally:source_path.unlink(missing_ok=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for field in ('run','worker','destination'):parser.add_argument('--'+field,type=Path,required=True)
    args=parser.parse_args();result=prepare(args.run,args.worker,args.destination)
    print(json.dumps(dict(cases=result['cases'],verifiedGames=result['verifiedGames'],partition='training')))
