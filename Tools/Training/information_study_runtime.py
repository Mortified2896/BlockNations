"""Power-safe restart and immutable input handling for controlled studies."""
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

from power_controller import read_power
from host_resources import thermal_state
from supervisor import atomic_json


class PowerPause(RuntimeError):
    pass


class PowerGuard:
    def __init__(self, enabled):
        self.enabled, self.running, self.next_check = enabled, False, 0

    def check(self):
        if not self.enabled or time.monotonic() < self.next_check:
            return
        self.next_check = time.monotonic()+2
        try:
            ac, percent = read_power()
        except (subprocess.SubprocessError, ValueError) as error:
            raise PowerPause('Power status unavailable; saved study waits for valid power readings.') from error
        if not ac or percent <= 75 or not self.running and percent < 80:
            raise PowerPause('Study saved and paused until AC power and at least 80% battery.')
        try:
            thermal = thermal_state()
        except (OSError, RuntimeError, ValueError) as error:
            raise PowerPause('Thermal pressure unavailable; saved study waits for a valid reading.') from error
        if thermal >= 2 or not self.running and thermal > 0:
            raise PowerPause('Study saved and paused until macOS reports nominal thermal pressure.')
        self.running = True


def save_environment(run, environment):
    # Preserve only known public execution settings, never inherited credentials.
    keys = ('BLOCKNATIONS_RATING_RUN', 'BLOCKNATIONS_RATING_BOARD', 'BLOCKNATIONS_RATING_SESSION',
            'BLOCKNATIONS_RULES_VERSION', 'BLOCKNATIONS_SIMULATION_WORKER', 'BLOCKNATIONS_PARALLEL_GAMES',
            'BLOCKNATIONS_SIMULATION_SEED', 'BLOCKNATIONS_SIMULATION_CURRICULUM', 'BLOCKNATIONS_SIMULATION_DISTANCE',
            'BLOCKNATIONS_OPENING_ECONOMY_VERSION', 'BLOCKNATIONS_OBSERVATION_VERSION', 'BLOCKNATIONS_HUMAN_IMITATION',
            'BLOCKNATIONS_TORCH_THREADS', 'BLOCKNATIONS_EPISODE_STARTS', 'BLOCKNATIONS_DIAGNOSTIC_FULL_VISION_SEATS', 'OMP_NUM_THREADS', 'MKL_NUM_THREADS',
            'OPENBLAS_NUM_THREADS', 'PYTHONUNBUFFERED')
    atomic_json(Path(run)/'study-environment.json', {key: environment[key] for key in keys if key in environment})


def resume_environment(run):
    saved = json.loads((Path(run)/'study-environment.json').read_text())
    if saved.get('BLOCKNATIONS_RATING_RUN') != str(run) or saved.get('BLOCKNATIONS_HUMAN_IMITATION') != 'disabled':
        raise ValueError('Resume environment belongs to another controlled study.')
    saved['BLOCKNATIONS_RATING_SESSION'] = uuid.uuid4().hex
    return dict(os.environ, **saved)


def evaluate_or_resume(manifest, suite, worker, destination, guard):
    from evaluate_suite import prepare, run as evaluate
    destination = Path(destination)
    guard()
    if destination.exists():
        _, identity, _, definition = prepare(manifest, suite)
        path = destination/'suite.json'
        receipt = json.loads(path.read_text()) if path.exists() else {}
        if (receipt.get('state') == 'completed' and receipt.get('suiteId') == definition['suiteId'] and
                receipt.get('candidate') == identity and len(receipt.get('results', [])) == len(definition['cases'])):
            for case, result in zip(receipt['cases'], receipt['results']):
                full = json.loads((destination/case['name']/'evaluation.json').read_text())
                if len(full.get('records', [])) != case['gamesPerCell']*4 or any(full.get(k) != v for k, v in result.items() if k != 'case'):
                    raise ValueError('Saved completed evaluation evidence changed.')
            return receipt
        destination.rename(destination.with_name(destination.name+'-interrupted-'+str(time.time_ns())))
    return evaluate(manifest, suite, worker, destination, guard=guard)
