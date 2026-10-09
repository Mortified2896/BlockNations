"""Bounded 1/2/4-game learning benchmark; not an opponent-strength tournament."""
import argparse
import copy
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import uuid
from supervisor import trainer_config, process_tree_rss


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--worker',type=Path,required=True)
    parser.add_argument('--checkpoint',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--steps',type=int,default=12000)
    args=parser.parse_args()
    if not args.worker.is_file() or not 2048<=args.steps<=50000:
        raise ValueError('Build the worker and choose a bounded learning benchmark.')
    if args.checkpoint and not args.checkpoint.is_file():raise ValueError('Checkpoint is missing.')
    receipts=[]
    for workers in (1,2,4):
        with tempfile.TemporaryDirectory(prefix='bn-throughput-') as directory:
            run=Path(directory)/'benchmark'; run.mkdir()
            config=trainer_config(args.steps,4000)
            config['behaviors']['BlockNationsSeatV2']['self_play'].update(team_change=5000,save_steps=4000)
            if args.checkpoint:
                snapshot=run/'input.pt'
                import shutil
                shutil.copyfile(args.checkpoint,snapshot)
                config['behaviors']['BlockNationsSeatV2']['init_path']=str(snapshot)
            (run/'trainer.yaml').write_text(json.dumps(config))
            session=uuid.uuid4().hex
            (run/'rating-session.json').write_text(json.dumps({'session':session}))
            env=os.environ.copy()
            env.update(BLOCKNATIONS_RATING_RUN=str(run),BLOCKNATIONS_RATING_BOARD='7',BLOCKNATIONS_RATING_SESSION=session,
                       BLOCKNATIONS_SIMULATION_WORKER=str(args.worker.absolute()),BLOCKNATIONS_PARALLEL_GAMES=str(workers),
                       BLOCKNATIONS_SIMULATION_SEED='42',BLOCKNATIONS_SIMULATION_CURRICULUM='false',BLOCKNATIONS_SIMULATION_DISTANCE='2',
                       OMP_NUM_THREADS='2',MKL_NUM_THREADS='2',PYTHONUNBUFFERED='1',PYTHONWARNINGS='ignore')
            started=time.monotonic(); peak=0
            with (run/'trainer.log').open('wb') as log:
                process=subprocess.Popen([sys.executable,str(Path(__file__).with_name('rated_training.py')),str(run/'trainer.yaml'),
                                         '--run-id','benchmark','--results-dir',str(run/'checkpoints'),'--seed','42','--torch-device','cpu'],
                                         stdout=log,stderr=subprocess.STDOUT,env=env,cwd=run)
                try:
                    while process.poll() is None and time.monotonic()-started<180:
                        peak=max(peak,process_tree_rss(process.pid) or 0)
                        time.sleep(.5)
                    if process.poll() is None:
                        process.send_signal(signal.SIGINT); process.wait(timeout=30)
                        raise TimeoutError('Benchmark exceeded its three-minute case limit.')
                finally:
                    if process.poll() is None:process.terminate(); process.wait(timeout=10)
            if process.returncode:
                raise RuntimeError((run/'trainer.log').read_text()[-8000:])
            status=json.loads((run/'arena-status.json').read_text())
            rating=json.loads((run/'match-elo.json').read_text())
            receipt=dict(workers=workers,backend=status['simulationBackend'],rulesVersion=status['simulationVersion'],
                         decisions=status['decisions'],actions=status['actions'],matches=status['games'],
                         captures=status['captures'],interruptions=status['interruptions'],rejections=status['rejections'],
                         trainerResets=status['trainerResets'],seconds=status['elapsedSeconds'],
                         decisionsPerSecond=status['decisionsPerSecond'],peakResidentBytes=peak,
                         ratingMatches=rating['matches'],checkpointCount=len(list(run.rglob('*.pt'))),
                         weights='same supplied checkpoint' if args.checkpoint else 'fresh seed 42',
                         note='Actual inference, PPO updates, self-play swaps, transport and replay recording; viewer absent. Concurrent native baseline may compete for CPU.')
            if receipt['rejections'] or receipt['matches']!=receipt['ratingMatches']:raise RuntimeError('Benchmark lost actions or match results.')
            receipts.append(receipt)
            args.output.parent.mkdir(parents=True,exist_ok=True)
            args.output.write_text(json.dumps(receipts,indent=2)+'\n')
            print(json.dumps(receipt),flush=True)
    return 0


if __name__=='__main__':raise SystemExit(main())
