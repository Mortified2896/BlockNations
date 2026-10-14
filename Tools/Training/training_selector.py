"""Private native chooser service: bounded catalog, saved training switches, frozen matches."""
import argparse
import fcntl
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

from model_catalog import OWNER, discover, read
from playtest import PlaytestSession, atomic_json
from power_controller import command_for, read_power


class Selector:
    def __init__(self, root, config):
        self.root, self.config = Path(root).absolute(), config
        if config.get('owner') != OWNER or not isinstance(config.get('runs'),list):
            raise ValueError('Use the owned selector launch configuration.')
        self.commands = {entry['id']:entry['command'] for entry in config['runs']}
        for name, command in self.commands.items():
            if (not re.fullmatch('[A-Za-z0-9_-]{1,81}',name) or '--resume' not in command or
                    command[command.index('--run-id')+1] != name or command[command.index('--root')+1] != str(self.root)):
                raise ValueError('Only exact configured saved training runs can be launched.')
        self.state = read(self.root/'training-selector-state.json')
        if self.state.get('owner') != OWNER: self.state=dict(owner=OWNER,desiredRun='',message='Choose training or a frozen model.',requestId='')
        if not self.state.get('desiredRun') and not self.state.get('error'):self.state['message']='Choose training or a frozen model.'
        self.process = None; self.training_log = None; self.power_stopping = False
        self.session = PlaytestSession(Path(config['sessionRun']),Path(config['player']),training_active=False)
        from decoupled_runtime import ViewerSession
        self.viewer = ViewerSession(self.root/'runs'/config['viewerRun'],Path(config['player']),enabled=True)
        self.viewer.initial_launch=False
        self.next_catalog=0;self.catalog={};self.last_power=None

    def active(self):
        lock=self.root/'supervisor.lock'
        try: pid=int(lock.read_text())
        except (OSError,ValueError):return None
        command=command_for(pid)
        if not command:return None
        return next((name for name in self.commands if '--run-id '+name+' ' in command+' ' and '--root '+str(self.root) in command), 'unmanaged')

    def take_training_control(self):
        legacy=self.root/'power-controller.json';value=read(legacy)
        if value and value.get('enabled'):
            backup=self.root/'training-selector-previous-power.json'
            if not backup.exists():atomic_json(backup,value)
            atomic_json(legacy,dict(value,enabled=False,managedBy=OWNER))
        self.state['managesTraining']=True

    def request(self, request):
        identifier=request.get('requestId')
        if not isinstance(identifier,str) or not re.fullmatch('[a-f0-9]{32}',identifier):raise ValueError('Invalid selector request.')
        op=request.get('op');name=request.get('runId','')
        if op in ('start','stop','pause','resume'):
            if name not in self.commands:raise ValueError('This historical run is inspectable but has no configured training launcher.')
            active=self.active()
            if active=='unmanaged':raise ValueError('An unrelated supervisor is active; its process was preserved.')
            run=self.root/'runs'/name
            if op=='start':
                self.take_training_control();self.state.update(desiredRun=name,observedRun='',message='Preparing '+name)
                if active and active!=name:
                    self.state['stoppingRun']=active
                    (self.root/'runs'/active/'stop.request').touch()
            elif op=='stop':
                if active==name:(run/'stop.request').touch()
                if self.state.get('desiredRun')==name:self.state['desiredRun']=''
                self.state['message']='Stop and save requested for '+name
            else:
                if active!=name:raise ValueError('That run is not currently running.')
                atomic_json(run/'training-control.json',dict(paused=op=='pause'))
                self.state['message']=('Paused ' if op=='pause' else 'Resumed ')+name
        elif op=='play':
            if self.session.process is not None:raise ValueError('Close the current human match before opening another.')
            self.session.start(request,{})
            if self.session.state.get('state')=='error':raise ValueError(self.session.state['message'])
            self.state['message']='Loading the selected frozen opponent.'
        else:raise ValueError('Unknown selector action.')
        self.state['requestId']=identifier
        self.state['lastOp']=op

    def tick(self):
        request_path=self.root/'training-selector.request.json'
        if request_path.exists():
            request=read(request_path);request_path.unlink()
            try:self.request(request)
            except (OSError,ValueError,KeyError) as error:
                self.state.update(message=str(error),requestId=request.get('requestId',''),error=str(error))
            else:self.state.pop('error',None)
        self.session.training_active=bool(self.active())
        self.session.poll({})
        if self.state.get('lastOp')=='play' and self.session.state.get('state') in ('playing','finished','error'):
            self.state['message']=self.session.state.get('message','')
        viewer_request=Path(self.config['sessionRun'])/'viewer.request'
        if viewer_request.exists():
            viewer_request.unlink();self.viewer.launch()
        active=self.active()
        if active in self.commands:self.state['observedRun']=active
        try:
            ac,percent=read_power();self.last_power=(ac,percent)
        except (OSError,subprocess.SubprocessError,ValueError):ac,percent=False,-1
        self.state.update(acPower=ac,batteryPercent=percent,activeRun=active or '')
        desired=self.state.get('desiredRun','')
        if self.state.get('managesTraining') and active in self.commands and (not ac or percent<=75):
            (self.root/'runs'/active/'stop.request').touch();self.power_stopping=True;self.state['stoppingRun']=active
            self.state['message']='Saving training; waiting for AC power and 80% battery.'
        if self.process is not None and self.process.poll() is not None:
            final=read(self.root/'runs'/self.state.get('launchedRun','')/'supervisor-status.json')
            if self.training_log:self.training_log.close();self.training_log=None
            self.process=None
            if final.get('state')!='stopped' or final.get('exitCode')!=0:
                self.state.update(desiredRun='',error='Trainer exited without a verified saved shutdown. Review its run logs.',message='Training stopped with an error.')
            elif not self.power_stopping and desired==self.state.get('launchedRun'):
                self.state.update(desiredRun='',message='Training session finished and saved.')
            self.power_stopping=False
            desired=self.state.get('desiredRun','')
        previous=self.state.get('stoppingRun')
        if previous and not active and self.process is None:
            final=read(self.root/'runs'/previous/'supervisor-status.json')
            if final.get('state')!='stopped' or final.get('exitCode')!=0:
                self.state.update(desiredRun='',error='The previous trainer did not report a successful save. Its files are preserved.',message='Training switch stopped after an incomplete save.')
                desired=''
            self.state.pop('stoppingRun',None)
            self.state['observedRun']=''
        if desired and not active and self.process is None and self.state.get('observedRun')==desired:
            final=read(self.root/'runs'/desired/'supervisor-status.json')
            self.state.update(desiredRun='',message='Training session finished and saved.' if final.get('state')=='stopped' and final.get('exitCode')==0 else 'Training ended unexpectedly; review its run logs.')
            desired=''
        if desired and not active and self.process is None:
            if ac and percent>=80:
                run=self.root/'runs'/desired
                control=read(run/'training-control.json');atomic_json(run/'training-control.json',dict(control,paused=False))
                # The existing supervisor owns the root lock, storage reserve and final save.
                log=run/'selector-training.log'
                if log.exists() and log.stat().st_size>2000000:log.replace(run/'selector-training.previous.log')
                self.training_log=log.open('ab')
                self.process=subprocess.Popen(self.commands[desired],stdin=subprocess.DEVNULL,stdout=self.training_log,
                    stderr=subprocess.STDOUT,start_new_session=True)
                self.state.update(launchedRun=desired,message='Starting saved training: '+desired)
            else:self.state['message']='Selected '+desired+'; waiting for AC power and at least 80% battery.'
        elif desired and active and active!=desired:self.state['message']='Saving '+active+' before starting '+desired
        if time.monotonic()>=self.next_catalog:
            self.next_catalog=time.monotonic()+5;self.catalog=discover(self.root)
            for run in self.catalog['runs']:run['managed']=run['id'] in self.commands
        atomic_json(self.root/'training-selector-state.json',dict(self.state,owner=OWNER))
        atomic_json(self.root/'training-selector.json',dict(self.catalog,pid=os.getpid(),updatedUnix=time.time(),
            desiredRun=self.state.get('desiredRun',''),activeRun=self.state.get('activeRun',''),message=self.state.get('message',''),
            requestId=self.state.get('requestId',''),acPower=ac,batteryPercent=percent,playtest=self.session.state))


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);parser.add_argument('--config',type=Path,required=True)
    args=parser.parse_args()
    with (args.root/'training-selector.lock').open('a') as lock:
        try:fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError:return
        selector=Selector(args.root,read(args.config))
        try:
            while True:selector.tick();time.sleep(2)
        finally:selector.session.close()


if __name__=='__main__':main()
