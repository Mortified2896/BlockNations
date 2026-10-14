"""Launch one frozen human playtest alongside the supervised training arena.

The stock SDK runs in inference mode in an isolated scratch directory. Neither
optimizer updates, self-play swaps nor whole-match ratings run in this process.
"""
from __future__ import annotations

import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import sys
import time
from training_contract import OPENING_ECONOMY_VERSION, RULES_VERSION, run_rules_version

BEHAVIOR = "BlockNationsSeatV2"
OWNER = "BlockNations.HumanPlaytest.v1"


def read_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return {}


def atomic_json(path: Path, value: dict) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.replace(path)


def latest_checkpoint(run: Path, *, allow_pending=False) -> Path | None:
    manifest = read_json(run / "run.json")
    if manifest.get('diagnosticOnly'):
        raise ValueError('Diagnostic training runs are excluded from ordinary human playtests.')
    if (manifest.get("owner") != "BlockNations.LocalTraining.v1" or manifest.get("schema") != 2 or
            manifest.get("observationSize") != 3120 or manifest.get("actionCount") != 259 or
            manifest.get("boardSize") not in (5, 6, 7, 9, 11)):
        raise ValueError("The saved run is incompatible with this playtest player.")
    directory = run / "checkpoints" / run.name / BEHAVIOR
    if directory.resolve() != run.resolve() / "checkpoints" / run.name / BEHAVIOR:
        raise ValueError("Checkpoint directories must not be symbolic links.")
    completed = []
    for path in directory.glob(BEHAVIOR + "-*.pt"):
        match = re.fullmatch(BEHAVIOR + r"-(\d+)\.pt", path.name)
        export = path.with_suffix(".onnx")
        # Numbered checkpoints are immutable. checkpoint.pt is overwritten by
        # the trainer, and an unfinished ONNX export is not a completed pair.
        if (match and not path.is_symlink() and not export.is_symlink() and
                path.is_file() and export.is_file() and path.stat().st_size > 0 and
                export.stat().st_size > 0 and time.time() - export.stat().st_mtime >= 2):
            completed.append((int(match[1]), path))
    if not completed:
        if allow_pending:
            return None
        raise ValueError("No completed checkpoint yet. Wait for the first model save and try again.")
    return max(completed, key=lambda item: item[0])[1]


def frozen_config(config: dict, checkpoint: Path, schema=2) -> dict:
    result = copy.deepcopy(config)
    from model_contract import behavior
    settings = result["behaviors"][behavior(schema)]
    settings.pop("self_play", None)
    settings["init_path"] = str(checkpoint)
    # No graphics/learning acceleration: this arena is for a human's inputs.
    result["engine_settings"] = {"time_scale": 1, "target_frame_rate": 60}
    return result


class PlaytestSession:
    def __init__(self, run: Path, environment: Path | None, frozen_checkpoint: Path | None = None, training_active=True,
                 frozen_selection='fixed comparison checkpoint'):
        self.run, self.environment = run, environment
        self.frozen_checkpoint, self.training_active = frozen_checkpoint, training_active
        self.frozen_selection = frozen_selection
        self.process = None
        self.log = None
        self.deadline = None
        self.state = {"state": "idle", "requestId": "", "message": ""}
        self.publish()

    def publish(self):
        atomic_json(self.run / "playtest-status.json", self.state)

    def start(self, request: dict, arena: dict):
        request_id = request.get("requestId", "")
        if not re.fullmatch(r"[a-f0-9]{32}", request_id):
            return
        self.state = {"state": "starting", "requestId": request_id, "message": "Loading a frozen checkpoint…"}
        try:
            if self.environment is None:
                raise ValueError("A standalone training player is required to open a human match.")
            difficulty = request.get('difficulty', 'Medium')
            if difficulty not in ('Easy', 'Medium', 'Hard'):
                raise ValueError('Choose Easy, Medium or Hard.')
            first_seat = request.get('firstSeat', 0)
            if type(first_seat) is not int or first_seat not in (0, 1):
                raise ValueError('Choose whether you or the AI moves first.')
            from playtest_champion import preferred_checkpoint
            chosen = None
            if request.get('modelId'):
                from model_catalog import resolve
                chosen = resolve(self.run.parent.parent, request['modelId'])
                source, selection = Path(chosen['checkpoint']), chosen['selection']
            elif self.frozen_checkpoint is not None:
                source, selection = self.frozen_checkpoint, self.frozen_selection
            else:
                source, selection = preferred_checkpoint(self.run, request.get('checkpointSource', 'Champion'))
            scratch = self.run / "playtest"
            if scratch.is_symlink():
                raise ValueError("The playtest scratch directory cannot be a symbolic link.")
            if scratch.exists():
                if read_json(scratch / "session.json").get("owner") != OWNER:
                    raise ValueError("An unrelated playtest directory already exists; it was preserved.")
                shutil.rmtree(scratch)
            scratch.mkdir()
            snapshot = scratch / "frozen.pt"
            shutil.copyfile(source, snapshot)
            digest = hashlib.sha256(snapshot.read_bytes()).hexdigest()
            if chosen and digest != chosen['checkpointSha256']:
                raise ValueError('Selected model changed while the frozen copy was being made.')
            manifest = read_json(self.run / "run.json")
            schema = chosen['schema'] if chosen else 2
            trained_vision = chosen['trainingVision'] if chosen else 'Fog'
            match_vision = request.get('matchVision', trained_vision)
            if match_vision not in ('Fog', 'FullVision'): raise ValueError('Choose normal fog or full vision for both players.')
            rules = chosen['rulesVersion'] if chosen else run_rules_version(self.run)
            self.state.update({"checkpoint": source.stem, "sha256": digest, "boardSize": chosen['boardSize'] if chosen else manifest["boardSize"],
                               "difficulty": difficulty, "firstSeat": first_seat, "rulesVersion": rules,
                               "checkpointSelection": selection, "schema": schema, "trainingVision": trained_vision,
                               "matchVision": match_vision, "modelRunId": chosen['runId'] if chosen else self.run.name,
                               "modelId": request.get('modelId', '')})
            atomic_json(scratch / "session.json", dict(self.state, owner=OWNER, source=str(source)))
            config = scratch / "inference.yaml"
            source_config = Path(chosen['trainerConfig']) if chosen else self.run/'trainer.yaml'
            if chosen and hashlib.sha256(source_config.read_bytes()).hexdigest() != chosen['configSha256']:
                raise ValueError('Selected model configuration changed before loading.')
            atomic_json(config, frozen_config(read_json(source_config), snapshot, schema))
            with socket.socket() as reserved:
                reserved.bind(("127.0.0.1", 0))
                port = reserved.getsockname()[1]
            policy_label = source.stem + ' / ' + difficulty + ' / ' + selection
            if chosen or match_vision == 'FullVision': policy_label += ' / trained '+trained_vision+' / match '+match_vision
            command = [sys.executable, str(Path(__file__).with_name("frozen_playtest.py")), str(config),
                       "--inference", "--run-id", "human", "--results-dir", str(scratch / "results"),
                       "--seed", str(manifest["seed"]), "--base-port", str(port), "--timeout-wait", "300",
                       "--torch-device", "cpu", "--env", str(self.environment), "--width", "1400", "--height", "900",
                       "--env-args", "--training-status", str(scratch / "arena-status.json"),
                       "--training-board-size", str(manifest["boardSize"]), "--training-curriculum", "false",
                       "--training-opening-economy-version", str(OPENING_ECONOMY_VERSION),
                       "--training-rules-version", rules,
                       "--training-seed", str(manifest["seed"]), "--training-human-seat", "0",
                       "--training-first-seat", str(first_seat),
                       "--training-policy-version", policy_label, "--training-playtest-return", "true"]
            if chosen or match_vision == 'FullVision':
                command += ['--training-observation-version', str(schema), '--training-match-vision', match_vision,
                            '--training-training-vision', trained_vision, '--training-model-run', self.state['modelRunId']]
            environment = {key: value for key, value in os.environ.items() if not key.startswith("BLOCKNATIONS_RATING_")}
            environment.update({"PYTHONUNBUFFERED": "1", "OMP_NUM_THREADS": "2", "MKL_NUM_THREADS": "2"})
            environment['BLOCKNATIONS_PLAYTEST_DIFFICULTY'] = difficulty
            self.log = (scratch / "inference.log").open("wb")
            self.process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=self.log,
                                            stderr=subprocess.STDOUT, cwd=scratch, env=environment, start_new_session=True)
            self.deadline = time.monotonic() + 300
        except (OSError, ValueError, KeyError) as error:
            self.state.update(state="error", message=str(error))
            if self.log is not None:
                self.log.close()
                self.log = None
            self.cleanup_outputs()
        self.publish()

    def poll(self, arena: dict):
        request_path = self.run / "playtest.request.json"
        if request_path.exists():
            request = read_json(request_path)
            request_path.unlink(missing_ok=True)
            if self.process is None:
                self.start(request, arena)
        if self.process is None:
            return
        status = read_json(self.run / "playtest" / "arena-status.json")
        expected = self.state.get('rulesVersion', RULES_VERSION)
        if status.get('trainerConnected') and (status.get('matchVision','Fog') != self.state.get('matchVision','Fog') or
                status.get('observationVersion',2) != self.state.get('schema',2)):
            self.state.update(state='error', message='This player does not support the selected visibility/model contract. Rebuild the matching training player.')
            self.close()
        elif status.get('trainerConnected') and status.get('simulationVersion', RULES_VERSION) != expected:
            self.state.update(state='error', message='Playtest player uses different rules; rebuild the matching native player.')
            self.close()
        elif status.get("failure"):
            self.state.update(state="error", message=status["failure"])
            self.close()
        elif self.process.poll() is not None:
            # Closing the human window ends the SDK environment as well.
            ready = self.state["state"] == "playing" and (self.process.returncode == 0 or status.get("returnRequested"))
            self.state.update(state="finished" if ready else "error",
                              message="Snapshot match closed." if ready else "Model playtest closed unexpectedly; see playtest/inference.log.")
            self.process = None
            self.log.close()
            self.log = None
            self.deadline = None
            self.cleanup_outputs()
        elif status.get("trainerConnected") and status.get("round", 0) > 0:
            self.state.update(state="playing", message="Human match open; training is " +
                              ("stopped." if not self.training_active else "manually paused." if arena.get("paused") else "continuing."))
            self.deadline = None
        elif self.deadline is not None and time.monotonic() > self.deadline:
            self.state.update(state="error", message="Model playtest did not connect in time.")
            self.close()
        self.publish()

    def cleanup_outputs(self):
        scratch = self.run / "playtest"
        # Keep the small receipt/log, retain only one session, and remove duplicate
        # weights/export/event outputs when finished. Original checkpoints survive.
        if not scratch.is_symlink() and read_json(scratch / "session.json").get("owner") == OWNER:
            (scratch / "frozen.pt").unlink(missing_ok=True)
            if (scratch / "results").exists():
                shutil.rmtree(scratch / "results")

    def close(self):
        if self.process is not None:
            if self.process.poll() is None:
                scratch = self.run / "playtest"
                if not scratch.is_symlink() and read_json(scratch / "session.json").get("owner") == OWNER:
                    # Unity's SDK launches the native player in its own process group.
                    # Ask its live Update loop to quit too, including on a human turn.
                    (scratch / "close.request").touch()
                self.process.send_signal(signal.SIGINT)
                try:
                    self.process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    # This group was created exclusively for this inference session.
                    os.killpg(self.process.pid, signal.SIGTERM)
                    self.process.wait(timeout=5)
            self.process = None
        if self.log is not None:
            self.log.close()
            self.log = None
        self.cleanup_outputs()
