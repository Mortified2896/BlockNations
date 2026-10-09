"""Owned execution configuration and optional viewer lifecycle, separate from learning."""
import json
import os
from pathlib import Path
import plistlib
import subprocess
import threading
import time
from bounded_log import BoundedLog


def read_json(path):
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return {}


def configure(args, run, environment):
    if args.backend != 'dotnet':
        return
    if not args.worker.is_file():
        raise ValueError('Build the C# simulation worker before starting decoupled training.')
    environment.update(BLOCKNATIONS_SIMULATION_WORKER=str(args.worker.absolute()),
                       BLOCKNATIONS_PARALLEL_GAMES=str(args.parallel_games),
                       BLOCKNATIONS_SIMULATION_SEED=str(args.seed),
                       BLOCKNATIONS_SIMULATION_CURRICULUM=str(args.curriculum).lower(),
                       BLOCKNATIONS_SIMULATION_DISTANCE=str(args.curriculum_distance))
    (run / 'training-control.json').unlink(missing_ok=True)
    record = dict(version=1, backend='standalone-dotnet', workerCount=args.parallel_games,
                  rulesVersion='blocknations-simulation-v2', modelSchema=2,
                  seedConvention='seed + arena * 7919', seed=args.seed,
                  updatedUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()))
    (run / 'execution-settings.json.tmp').write_text(json.dumps(record) + '\n')
    (run / 'execution-settings.json.tmp').replace(run / 'execution-settings.json')


class ViewerSession:
    def __init__(self, run, app, enabled):
        self.run, self.app, self.enabled = run, app, enabled
        self.process, self.output_thread = None, None
        self.initial_launch = enabled
        self.error = ''

    def launch(self):
        if self.process is not None and self.process.poll() is None:
            return  # One supervisor-owned viewer at a time.
        try:
            if self.app is None:
                raise ValueError('Configure a built Mac viewer/player to watch this run.')
            with (self.app / 'Contents/Info.plist').open('rb') as stream:
                info = plistlib.load(stream)
            executable = self.app / 'Contents/MacOS' / info['CFBundleExecutable']
            if not executable.is_file():
                raise ValueError('Viewer executable is missing.')
            if self.output_thread:
                self.output_thread.join(timeout=2)
            log = BoundedLog(self.run / 'viewer.log', limit=2 * 1024 * 1024)
            command = [str(executable), '--training-viewer', 'true', '--viewer-run', str(self.run),
                       '-screen-width', '1400', '-screen-height', '900', '-screen-fullscreen', '0',
                       '-logFile', '-']
            environment = {key: value for key, value in os.environ.items() if not key.startswith('BLOCKNATIONS_')}
            try:
                self.process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                                cwd=self.run, env=environment, start_new_session=True)
            except OSError:
                log.close()
                raise
            process = self.process
            def capture():
                try:
                    while chunk := process.stdout.read1(4096):
                        log.append(chunk)
                finally:
                    process.stdout.close()
                    log.close()
            self.output_thread = threading.Thread(target=capture, daemon=True)
            self.output_thread.start()
            self.error = ''
        except (OSError, ValueError, KeyError, plistlib.InvalidFileException) as error:
            self.error = str(error)  # Optional viewer failure never stops a learner.

    def poll(self, arena, state):
        request = self.run / 'viewer.request'
        requested = request.exists()
        if requested:
            request.unlink()
        if self.enabled and (requested or (self.initial_launch and arena.get('trainerConnected'))):
            self.initial_launch = False
            self.launch()
        state.update(viewerAvailable=self.enabled and self.app is not None,
                     viewerPid=self.process.pid if self.process and self.process.poll() is None else 0,
                     viewerError=self.error)

    def close(self):
        # Called only when the training supervisor ends, never on viewer close.
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill(); self.process.wait()
        if self.output_thread:
            self.output_thread.join(timeout=2)
