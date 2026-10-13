"""launchd tick for one owned, resumable, power-controlled information study."""
import argparse
import fcntl
import json
from pathlib import Path
import subprocess
import time

from power_controller import command_for, parse_power
from supervisor import atomic_json, read_json


def tick(config_path):
    config_path=Path(config_path);config=read_json(config_path)
    command=config['command'];destination=Path(config['destination'])
    if (config.get('owner')!='BlockNations.PowerStudy.v1' or '--power-controlled' not in command or
            command[command.index('--destination')+1]!=str(destination) or destination.is_symlink()):
        raise ValueError('Only the explicitly owned power-controlled study can be launched.')
    status_path=config_path.with_name('controller-status.json')
    status=read_json(status_path)
    with config_path.with_name('controller.lock').open('a') as lock:
        try:fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError:return
        receipt=read_json(destination/'study.json')
        state=receipt.get('state')
        if state in ('completed','failed') or config.get('enabled') is not True:
            status.update(state=state or 'disabled',updatedUnix=time.time());atomic_json(status_path,status);return
        pid=status.get('studyPid') or receipt.get('pid')
        if pid:
            native=[config['processExecutable'],*command[1:]]
            expected=[' '.join(args+suffix) for args in (command,native) for suffix in ([],['--resume'])]
            observed=command_for(pid)
            if observed:
                if observed not in expected:raise ValueError('Study PID identity changed; unrelated process was preserved.')
                status.update(state=state or 'starting',studyPid=pid,updatedUnix=time.time());atomic_json(status_path,status);return
        if receipt and state not in ('paused_power','running'):
            raise ValueError('Study is not in an explicitly resumable state.')
        ac,percent=parse_power(subprocess.check_output(['/usr/bin/pmset','-g','batt'],text=True,timeout=5))
        status.update(acPower=ac,batteryPercent=percent,updatedUnix=time.time())
        if not ac or percent<80:
            status['state']='waiting_for_power';atomic_json(status_path,status);return
        launch=command+(['--resume'] if receipt else [])
        process=subprocess.Popen(launch,stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,
                                 stderr=subprocess.DEVNULL,start_new_session=True)
        status.update(state='starting',studyPid=process.pid);atomic_json(status_path,status)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--config',type=Path,required=True)
    args=parser.parse_args();tick(args.config)
