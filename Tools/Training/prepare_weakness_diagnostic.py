"""Separate training-only challenger diagnostic for a frozen sampling treatment."""
import argparse
import hashlib
import json
from pathlib import Path

from evaluate_suite import frozen_actor, worker_digest
from evaluate_policy import FrozenActor, TacticalActor, evaluate
from model_contract import behavior, checkpoint_schema
from training_contract import run_rules_version
from training_opponents import load_recipe, entries, owned_frozen_path, entry_id
from supervisor import ROOT_MARKER, size


def prepare(run,parent,worker,destination,seed=92001):
    run,parent,worker,destination=map(lambda p:Path(p).absolute(),(run,parent,worker,destination))
    root=run.parent.parent
    if type(seed) is not int or not 0 <= seed < 2**31:raise ValueError('Choose a bounded recorded diagnostic seed.')
    if (run.is_symlink() or run.resolve()!=run or run.parent.name!='runs' or destination.exists() or
            not destination.is_relative_to(root/'benchmarks') or
            json.loads((root/ROOT_MARKER).read_text()).get('owner')!='BlockNations.LocalTraining.v1' or size(root)>19_400_000_000):
        raise ValueError('Use an owned run and new training-only diagnostic under the guarded shared root.')
    recipe=load_recipe(run,run_rules_version(run));candidate,_=frozen_actor(parent,run_rules_version(run))
    destination.mkdir(parents=True)
    result=dict(version=1,partition='training',seed=seed,interruptions=0,opponents=[],
                candidateSha256=candidate.sha256,recipeSha256=hashlib.sha256((run/'training-opponents.json').read_bytes()).hexdigest(),workerDigest=worker_digest(worker))
    for index,entry in enumerate(entries(recipe)):
        if entry['kind']=='fair-tactician':reference=TacticalActor(entry['workBudget'],entry.get('recruitType',''));mode='Hard'
        else:
            checkpoint=owned_frozen_path(run,entry['checkpoint']);config=owned_frozen_path(run,entry['trainerConfig'])
            network=json.loads(config.read_text())['behaviors'][behavior(checkpoint_schema(checkpoint))]['network_settings']
            reference=FrozenActor(checkpoint,network);mode=entry['sampling']
            if reference.sha256!=entry['checkpointSha256']:raise ValueError('Recorded challenger changed.')
        pack=evaluate(worker,destination/('challenger-'+str(index)),candidate,reference,games_per_cell=8,seed=seed,
                      difficulty='Hard',reference_difficulty=mode,rules_version=run_rules_version(run))
        result['interruptions']+=pack['interruptions']
        result['opponents'].append(dict(id=entry_id(entry,run_rules_version(run)),roles=pack['startingPositionResults']))
    with (destination/'diagnostic.json').open('x') as output:json.dump(result,output,indent=2)
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for field in ('run','parent','worker','destination'):parser.add_argument('--'+field,type=Path,required=True)
    parser.add_argument('--seed',type=int,default=92001);args=parser.parse_args()
    result=prepare(args.run,args.parent,args.worker,args.destination,args.seed)
    print(json.dumps(dict(partition=result['partition'],challengers=len(result['opponents']),interruptions=result['interruptions'])))
