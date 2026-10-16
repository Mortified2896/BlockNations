"""Opt-in host headroom and cooperative pacing for background training.

Only public resource counters are sampled. Never inspect agent conversations,
raise another process's priority, or suspend a worker during an RPC.
"""
import ctypes
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import time

from host_resources import thermal_state

OWNER = 'BlockNations.HostResources.v1'
ROOT_ENV = 'BLOCKNATIONS_RESOURCE_ROOT'
DEFAULTS = dict(owner=OWNER, enabled=True, resumeCpuPercent=40, pauseCpuPercent=65,
                resumeLoadPerCore=.65, pauseLoadPerCore=.9, idleSeconds=120,
                maxSwapMBps=1, activeFraction=.25, sampleSeconds=10)


def policy(root):
    if root is None:
        return None
    path = Path(root) / 'resource-policy.json'
    if not path.exists():
        return None
    if path.is_symlink():
        raise ValueError('Resource policy must be an owned local file, not a link.')
    value = json.loads(path.read_text())
    if not isinstance(value, dict) or value.get('owner') != OWNER or type(value.get('enabled')) is not bool:
        raise ValueError('Invalid BlockNations resource policy owner/enabled setting.')
    if not value['enabled']:
        return None
    result = dict(DEFAULTS, **value)
    for key in DEFAULTS.keys() - {'owner', 'enabled'}:
        if type(result[key]) not in (int, float) or not math.isfinite(result[key]):
            raise ValueError('Invalid numeric resource policy: ' + key)
    if not (0 < result['resumeCpuPercent'] < result['pauseCpuPercent'] <= 100 and
            0 < result['resumeLoadPerCore'] < result['pauseLoadPerCore'] <= 2 and
            0 <= result['idleSeconds'] <= 3600 and 0 < result['maxSwapMBps'] <= 100 and
            .05 <= result['activeFraction'] <= 1 and 1 <= result['sampleSeconds'] <= 30):
        raise ValueError('Resource policy thresholds are out of range.')
    return result


def cpu_ticks():
    """Mach CPU ticks: user, system, idle, nice, without polling process arguments."""
    libc = ctypes.CDLL(None)
    libc.mach_host_self.restype = ctypes.c_uint
    host = libc.mach_host_self()
    ticks = (ctypes.c_uint * 4)()
    count = ctypes.c_uint(4)
    try:
        if libc.host_statistics(host, 3, ticks, ctypes.byref(count)) or count.value != 4:
            raise RuntimeError('Live CPU counters unavailable.')
        return list(ticks)
    finally:
        libc.mach_port_deallocate(ctypes.c_uint.in_dll(libc, 'mach_task_self_').value, host)


def parse_swap(output):
    page = re.search(r'page size of (\d+) bytes', output)
    counts = [re.search(r'^' + key + r':\s+(\d+)\.', output, re.MULTILINE)
              for key in ('Swapins', 'Swapouts')]
    if not page or not all(counts):
        raise ValueError('Live swap counters unavailable.')
    return sum(int(count[1]) for count in counts) * int(page[1])


def sample_host():
    if sys.platform != 'darwin':
        raise RuntimeError('This host resource policy currently requires macOS.')
    pressure = int(subprocess.check_output(
        ['/usr/sbin/sysctl', '-n', 'kern.memorystatus_vm_pressure_level'], text=True, timeout=3))
    if pressure not in (1, 2, 4):
        raise ValueError('Unknown live memory pressure level.')
    swap = parse_swap(subprocess.check_output(['/usr/bin/vm_stat'], text=True, timeout=3))
    return dict(cpuTicks=cpu_ticks(), swapBytes=swap, memoryPressure=pressure,
                thermalState=thermal_state(), load1=os.getloadavg()[0], cores=os.cpu_count() or 1)


def assess(root, state, running, now=None, sample=None):
    """Update the caller's persisted gate; require sustained headroom to resume.

    Unknown/malformed readings defer work and clear the healthy interval, rather
    than converting a temporary busy machine into a permanent learner fault.
    """
    now = time.time() if now is None else now
    try:
        config = policy(root)
        if config is None:
            state.clear()
            state.update(enabled=False, allowed=True, reason='')
            return state
        if (sample is None and state.get('running') == running and
                0 <= now - state.get('sampledUnix', -1e10) < config['sampleSeconds']):
            return state
        current = sample_host() if sample is None else sample
        ticks = current['cpuTicks']
        if (not isinstance(ticks, list) or len(ticks) != 4 or
                any(type(v) is not int or v < 0 for v in ticks) or
                current['memoryPressure'] not in (1, 2, 4) or
                current['thermalState'] not in (0, 1, 2, 3) or
                type(current['swapBytes']) is not int or current['swapBytes'] < 0 or
                not math.isfinite(current['load1']) or current['load1'] < 0 or current['cores'] < 1):
            raise ValueError('Invalid live host resource sample.')
        previous = state.get('sample', {})
        seconds = now - state.get('sampledUnix', now)
        fresh = 0 < seconds <= 60 and len(previous.get('cpuTicks', [])) == 4
        busy = swap_rate = None
        if fresh:
            # Mach ticks are unsigned 32-bit counters; handle ordinary wrap.
            delta = [(new - old) % 2**32 for new, old in zip(ticks, previous['cpuTicks'])]
            if sum(delta):
                busy = 100 * (1 - delta[2] / sum(delta))
            swap_rate = max(0, current['swapBytes'] - previous['swapBytes']) / seconds / 1_000_000
        reasons = []
        if current['memoryPressure'] != 1:
            reasons.append('memory_pressure')
        if current['thermalState'] > 0:
            reasons.append('thermal_pressure')
        limit = config['pauseCpuPercent' if running else 'resumeCpuPercent']
        load_limit = config['pauseLoadPerCore' if running else 'resumeLoadPerCore']
        if (busy is not None and busy >= limit) or current['load1'] >= current['cores'] * load_limit:
            reasons.append('cpu_busy')
        if swap_rate is not None and swap_rate >= config['maxSwapMBps']:
            reasons.append('active_swapping')
        if not fresh:
            state.pop('healthySinceUnix', None)
        if reasons:
            state.pop('healthySinceUnix', None)
        else:
            state.setdefault('healthySinceUnix', now)
        allowed = not reasons and (running or fresh and now - state['healthySinceUnix'] >= config['idleSeconds'])
        state.update(enabled=True, running=running, sampledUnix=now, sample=current,
                     cpuBusyPercent=busy, swapMBps=swap_rate, allowed=bool(allowed),
                     reason=', '.join(reasons) if reasons else '' if allowed else 'waiting_for_idle_capacity')
    except (OSError, ValueError, TypeError, KeyError, RuntimeError, subprocess.SubprocessError) as error:
        state.pop('healthySinceUnix', None)
        state.update(enabled=True, running=running, sampledUnix=now, allowed=False,
                     reason='resource_reading_unavailable', error=str(error))
    else:
        state.pop('error', None)
    return state


def compute_environment(root, environment):
    """Opt background compute children in; leave supervisor/UI processes alone."""
    config = policy(root)
    if config:
        environment[ROOT_ENV] = str(root)
        environment['BLOCKNATIONS_TORCH_THREADS'] = '1'
        environment.update(OMP_NUM_THREADS='1', MKL_NUM_THREADS='1', OPENBLAS_NUM_THREADS='1',
                           VECLIB_MAXIMUM_THREADS='1', NUMEXPR_NUM_THREADS='1')
    return environment


def lower_compute_priority(root):
    """Apply nice 19 and passive disk I/O in this compute child, inherited by workers.

    Keep the normal macOS scheduling class: Darwin-background throttling has
    previously starved a bounded simulator RPC. Pacing runs outside those RPCs.
    """
    if not policy(root):
        return
    compute_environment(root, os.environ)
    os.setpriority(os.PRIO_PROCESS, 0, 19)
    if sys.platform == 'darwin':
        libc = ctypes.CDLL(None, use_errno=True)
        if libc.setiopolicy_np(0, 0, 2) != 0:  # DISK, PROCESS, PASSIVE (sys/resource.h)
            raise OSError(ctypes.get_errno(), 'Cannot lower training disk I/O priority.')


class WorkBudget:
    """Pace complete synchronous simulator cycles, including intervening PPO work."""
    def __init__(self, root=None, clock=time.monotonic, sleep=time.sleep):
        config = policy(root)
        self.fraction = config['activeFraction'] if config else 1
        self.clock, self.sleep, self.resumed = clock, sleep, clock()

    def pace(self):
        # Sleeping before sending a request never consumes its response deadline.
        # Bound each sleep so saves/control signals stay responsive after slow RPCs.
        active = max(0, self.clock() - self.resumed)
        if self.fraction < 1:
            self.sleep(min(2, active * (1 / self.fraction - 1)))
        self.resumed = self.clock()
