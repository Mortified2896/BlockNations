"""Owned execution configuration and optional viewer lifecycle, separate from learning."""
import json
import os
from pathlib import Path
import plistlib
import subprocess
import sys
import threading
import time
from bounded_log import BoundedLog
from training_opponents import load_recipe, recipe_id
from training_contract import (RULES_VERSION, OPENING_ECONOMY_VERSION, LEGACY_OPENING_ECONOMY_VERSION,
                               validate_opening_economy, latest_saved_step)


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
    economy = validate_opening_economy(getattr(args, 'opening_economy_version', OPENING_ECONOMY_VERSION))
    environment.update(BLOCKNATIONS_SIMULATION_WORKER=str(args.worker.absolute()),
                       BLOCKNATIONS_PARALLEL_GAMES=str(args.parallel_games),
                       BLOCKNATIONS_SIMULATION_SEED=str(args.seed),
                       BLOCKNATIONS_SIMULATION_CURRICULUM=str(args.curriculum).lower(),
                       BLOCKNATIONS_SIMULATION_DISTANCE=str(args.curriculum_distance),
                       BLOCKNATIONS_OPENING_ECONOMY_VERSION=str(economy))
    (run / 'training-control.json').unlink(missing_ok=True)
    previous = read_json(run / 'execution-settings.json')
    changed = previous and (previous.get('rulesVersion') != RULES_VERSION or
                            previous.get('openingEconomyVersion', LEGACY_OPENING_ECONOMY_VERSION) != economy)
    if changed:
        revision = time.strftime('%Y%m%dT%H%M%SZ', time.gmtime())
        # Ratings measure a particular ruleset. Keep the old curve for reference;
        # continuing its weights doesn't make old/new results comparable.
        for name in ('match-elo.json', 'human-learning-status.json',
                     'board-' + str(args.board_size) + '-progress.json'):
            path = run / name
            if path.exists():
                path.rename(run / (path.stem + '-before-' + revision + '.json'))
        (run / ('execution-settings-before-' + revision + '.json')).write_text(json.dumps(previous) + '\n')
    contract_path = run / 'checkpoint-contract.json'
    if changed or not contract_path.exists():
        # A rules transition must train a new checkpoint before an export can
        # claim compatibility. A live arena running new rules is not evidence
        # that an old saved actor has been trained under them.
        if contract_path.exists():
            contract_path.rename(run / ('checkpoint-contract-before-' + revision + '.json'))
        contract = dict(version=1, rulesVersion=RULES_VERSION, boardSize=args.board_size,
                        openingEconomyVersion=economy, minimumCheckpointStep=latest_saved_step(run))
        (run / 'checkpoint-contract.json.tmp').write_text(json.dumps(contract) + '\n')
        (run / 'checkpoint-contract.json.tmp').replace(contract_path)
    record = dict(version=1, backend='standalone-dotnet', workerCount=args.parallel_games,
                  rulesVersion=RULES_VERSION, modelSchema=2, openingEconomyVersion=economy,
                  seedConvention='seed + arena * 7919', seed=args.seed,
                  updatedUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()))
    recipe = load_recipe(run)
    if recipe:
        record['trainingOpponentRecipe'] = recipe
        record['trainingChallengerId'] = recipe_id(recipe)
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
            if sys.platform == 'darwin':
                command = ['/usr/bin/nice', '-n', '10'] + command
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
