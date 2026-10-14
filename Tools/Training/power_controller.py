"""macOS launchd tick: resume one configured run on AC, gracefully save on battery.

Configuration and status live outside Git. No policy, recipe or battery settings
are changed. Unexpected/manual exits latch a fault instead of restarting forever.
"""
import argparse
import fcntl
import json
import os
from pathlib import Path
import plistlib
import re
import signal
import subprocess
import time

from host_resources import thermal_state


def read(path):
    return json.loads(path.read_text()) if path.exists() else {}


def write(path, value):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def parse_power(output):
    source = re.search(r"Now drawing from '([^']+)'", output)
    charge = re.search(r'(\d+)%;', output)
    if not source or not charge or source[1] not in ('AC Power', 'Battery Power'):
        raise ValueError('Cannot determine power source and battery percentage')
    percent = int(charge[1])
    if not 0 <= percent <= 100:
        raise ValueError('Invalid battery percentage')
    return source[1] == 'AC Power', percent


def read_power():
    """Retry pmset, then read the live battery registry; never use cached state."""
    for attempt in range(2):
        try:
            output = subprocess.check_output(['/usr/bin/pmset', '-g', 'batt'],
                                             text=True, timeout=5)
            return parse_power(output)
        except (subprocess.SubprocessError, ValueError):
            if attempt == 1:
                output = subprocess.check_output(['/usr/sbin/ioreg', '-a', '-r', '-c', 'AppleSmartBattery'], timeout=5)
                return parse_battery_registry(output)


def parse_battery_registry(output):
    batteries = plistlib.loads(output)
    if not isinstance(batteries, list) or len(batteries) != 1 or not isinstance(batteries[0], dict):
        raise ValueError('Cannot determine one live internal battery')
    battery = batteries[0]
    connected, current, maximum = (battery.get(k) for k in ('ExternalConnected', 'CurrentCapacity', 'MaxCapacity'))
    if (type(connected) is not bool or type(current) is not int or type(maximum) is not int or
            maximum <= 0 or not 0 <= current <= maximum):
        raise ValueError('Cannot determine live battery power and charge')
    return connected, int(100*current/maximum)


def action(running, ac, percent, start=80, stop=75):
    if running:
        return 'stop' if not ac or percent <= stop else 'keep'
    return 'start' if ac and percent >= start else 'wait'


def reconcile_boot(state, boot):
    if state.get('bootIdentity') and state['bootIdentity'] != boot:
        # A reboot invalidates old PIDs; resume the last checkpoint on login.
        # An already recorded fault still requires review.
        for key in ('lastPid', 'launchPid', 'stoppingPid'):
            state.pop(key, None)
    state['bootIdentity'] = boot
    return state


def command_for(pid):
    result = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'command='],
                            capture_output=True, text=True, timeout=5)
    # ps does not quote arguments containing spaces; do not shell-split its text.
    return result.stdout.strip() if result.returncode == 0 else ''


def identity(command, config):
    """Check executable, script and explicit run/root before signalling a PID."""
    wanted = config['command']
    if isinstance(command, str):
        native = [config.get('processExecutable', wanted[0]), *wanted[1:]]
        return command in (' '.join(wanted), ' '.join(native))
    if command[:2] != wanted[:2]:
        return False
    for flag in ('--root', '--run-id'):
        if flag not in command or flag not in wanted:
            return False
        try:
            if command[command.index(flag)+1] != wanted[wanted.index(flag)+1]:
                return False
        except IndexError:
            return False
    return True


def tick(config, state, ac, percent, now, inspect=command_for,
         send=lambda pid: os.kill(pid, signal.SIGTERM),
         launch=lambda cmd: subprocess.Popen(cmd, stdin=subprocess.DEVNULL,
                                            stdout=subprocess.DEVNULL,
                                            stderr=subprocess.DEVNULL,
                                            start_new_session=True).pid,
         thermal=None):
    root = Path(config['root'])
    command = config['command']
    if ('--resume' not in command or command[command.index('--root')+1] != str(root)
            or command[command.index('--run-id')+1] != config['runId']):
        raise ValueError('Only an explicitly configured resume command is supported')
    start, stop = config.get('startPercent', 80), config.get('stopPercent', 75)
    if not 0 <= stop < start <= 100:
        raise ValueError('Require 0 <= stop < start <= 100')
    state.update(updatedUnix=now, acPower=ac, batteryPercent=percent)
    if not config.get('enabled', False):
        state['state'] = 'disabled'
        return state
    if state.get('fault'):
        state['state'] = 'fault'
        return state
    review = read(root/'runs'/config['runId']/'training-review.json')
    review_required = (review.get('owner') == 'BlockNations.TrainingReview.v1' and
                       review.get('runId') == config['runId'] and review.get('state') == 'review_required')
    lock = root / 'supervisor.lock'
    pid = int(lock.read_text()) if lock.exists() else 0
    observed = inspect(pid) if pid else []
    # A newly launched supervisor may not have written its lock yet.
    if not observed and (state.get('launchPid') or state.get('lastPid')):
        pid = state.get('launchPid') or state['lastPid']
        observed = inspect(pid)
    if observed and not identity(observed, config):
        state['state'] = 'another_supervisor_active'
        return state
    running = bool(observed)
    if state.get('stoppingPid'):
        if inspect(state['stoppingPid']):
            state['state'] = 'saving'
            return state
        final = read(root / 'runs' / config['runId'] / 'supervisor-status.json')
        if final.get('state') != 'stopped' or final.get('exitCode') != 0:
            state.update(state='fault', fault='Power stop did not complete with a successful save')
            return state
        state.pop('stoppingPid')
        state.pop('lastPid', None)
        state.pop('launchPid', None)
    elif not running and (state.get('lastPid') or state.get('launchPid')):
        if review_required:
            state.pop('lastPid', None)
            state.pop('launchPid', None)
        else:
            state.update(state='fault', fault='Supervisor exited outside a power stop; review before rearming')
            return state
    if running:
        state['lastPid'] = pid
        state.pop('launchPid', None)
    decision = action(running, ac, percent, start, stop)
    resource_reason = ''
    if config.get('thermalGuard', False):
        if type(thermal) is not int or thermal not in (0, 1, 2, 3):
            raise ValueError('Require a fresh valid thermal reading before managed training.')
        state['thermalState'] = thermal
        if thermal >= 2 or not running and thermal > 0:
            decision = 'stop' if running else 'cool'
            resource_reason = 'thermal_pressure'
    controller_path = config.get('deferToStudyController')
    if controller_path:
        path = Path(controller_path)
        study = read(path)
        destination = Path(study.get('destination', ''))
        if (study.get('owner') != 'BlockNations.PowerStudy.v1' or not destination.is_relative_to(root/'benchmarks')
                or destination.resolve() != destination):
            raise ValueError('Study priority requires an owned controller below the shared benchmark root.')
        receipt = read(destination/'study.json')
        if study.get('enabled') is True and receipt.get('state') not in ('completed', 'failed'):
            decision = 'stop' if running else 'study'
            resource_reason = 'study_priority'
    if review_required:
        decision = 'stop' if running else 'review'
    if decision == 'stop':
        # Recheck identity immediately before signalling, never kill a process tree.
        if not identity(inspect(pid), config):
            raise RuntimeError('Supervisor identity changed before power stop')
        state.update(state='saving', stoppingPid=pid,
                     stopReason='benchmark_review' if review_required else resource_reason or ('unplugged' if not ac else 'battery_at_stop_threshold'))
        send(pid)
    elif decision == 'start':
        state.update(state='starting', launchPid=launch(command), startedUnix=now)
    else:
        state['state'] = ('benchmark_review_required' if decision == 'review' else
                          'waiting_for_cooling' if decision == 'cool' else
                          'waiting_for_study' if decision == 'study' else
                          'running' if running else 'waiting_for_power')
    return state


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', type=Path, required=True)
    args = parser.parse_args()
    config = read(args.config)
    directory = args.config.parent
    with (directory / 'power-controller.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return
        path = directory / 'power-controller-status.json'
        state = read(path)
        try:
            boot = subprocess.check_output(['/usr/sbin/sysctl', '-n', 'kern.bootsessionuuid'], text=True, timeout=5).strip()
            state = reconcile_boot(state, boot)
            try:
                ac, percent = read_power()
                thermal = thermal_state() if config.get('thermalGuard', False) else None
            except (subprocess.SubprocessError, OSError, RuntimeError, ValueError) as error:
                # Unknown power must not leave an owned learner draining battery.
                state = tick(dict(config, thermalGuard=False), state, False, 0, time.time())
                state.update(state='fault', fault='Power/thermal reading unavailable: ' + str(error))
                write(path, state)
                return
            state = tick(config, state, ac, percent, time.time(), thermal=thermal)
        except Exception as error:
            state.update(state='fault', fault=str(error), updatedUnix=time.time())
        write(path, state)


if __name__ == '__main__':
    main()
