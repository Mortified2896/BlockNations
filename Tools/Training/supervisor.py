"""Own one local trainer, its sleep assertion, and a shared artifact budget.

Only files below a marked training root are eligible for retention. Never launch
with a shell, modify user power settings, or prune imported/pinned models.
"""
from __future__ import annotations

import argparse
from elo_history import TrainerEloHistory
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import threading
import time
from typing import Iterable

ROOT_MARKER = ".blocknations-training-root.json"
RUN_MARKER = "run.json"
GB = 1_000_000_000
MB = 1_000_000
stop_requested = False


def atomic_json(path: Path, data: dict) -> None:
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(data, indent=2) + "\n")
    temporary.replace(path)


def read_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return {}


def prepare_root(root: Path) -> None:
    if root.is_symlink():
        raise ValueError("Training root must not be a symbolic link.")
    root.mkdir(parents=True, exist_ok=True)
    marker = root / ROOT_MARKER
    if marker.exists():
        if read_json(marker).get("owner") != "BlockNations.LocalTraining.v1":
            raise ValueError("Training root has an incompatible ownership marker.")
    elif any(root.iterdir()):
        raise ValueError("Refusing to manage a nonempty directory without a training ownership marker.")
    else:
        atomic_json(marker, {"owner": "BlockNations.LocalTraining.v1", "schema": 1})
    (root / "runs").mkdir(exist_ok=True)


def files(root: Path) -> Iterable[Path]:
    # Never follow links into another directory or count a link target as owned.
    for directory, folders, names in os.walk(root, followlinks=False):
        folders[:] = [name for name in folders if not (Path(directory) / name).is_symlink()]
        for name in names:
            path = Path(directory) / name
            if not path.is_symlink():
                yield path


def size(root: Path) -> int:
    total = 0
    for path in files(root):
        try:
            total += path.stat().st_size
        except FileNotFoundError:
            pass
    return total


def process_tree_rss(pid: int) -> int | None:
    """Sample resident memory of trainer and descendants without reading arguments."""
    if os.name != "posix":
        return None
    try:
        result = subprocess.run(["ps", "-axo", "pid=,ppid=,rss="], capture_output=True, text=True, check=True)
        entries = [tuple(map(int, row.split())) for row in result.stdout.splitlines() if len(row.split()) == 3]
        members = {pid}
        while True:
            expanded = members | {child for child, parent, _ in entries if parent in members}
            if expanded == members:
                break
            members = expanded
        return sum(rss * 1024 for child, _, rss in entries if child in members)
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


def owned_runs(root: Path) -> list[Path]:
    return [run for run in (root / "runs").iterdir()
            if run.is_dir() and not run.is_symlink() and read_json(run / RUN_MARKER).get("owner") == "BlockNations.LocalTraining.v1"]


def protected_files(run: Path) -> set[Path]:
    protected = set()
    pins = read_json(run / "pins.json").get("files", [])
    for relative in pins:
        path = run / relative
        if ".." not in Path(relative).parts and not Path(relative).is_absolute():
            protected.add(path)
            # Preserve a resumable network/optimizer alongside a pinned export.
            protected.add(path.with_suffix(".pt"))
    for checkpoint in run.rglob("checkpoint.json"):
        metadata = read_json(checkpoint)
        last = metadata.get("last_checkpoint", {})
        filename = last.get("file_path")
        if filename:
            candidate = Path(filename)
            if candidate.is_absolute() and candidate.is_relative_to(run):
                protected.add(candidate)
                protected.add(candidate.with_suffix(".onnx"))
            elif not candidate.is_absolute() and ".." not in candidate.parts:
                protected.add(checkpoint.parent / candidate.name)
    # Trainer releases differ in checkpoint metadata location. Always retain the
    # newest .pt per behavior directory even if metadata cannot be interpreted.
    checkpoints: dict[Path, list[Path]] = {}
    for path in files(run):
        if path.suffix == ".pt":
            checkpoints.setdefault(path.parent, []).append(path)
        if path.suffix == ".onnx" and not re.search(r"-\d+\.onnx$", path.name):
            protected.add(path)  # final/latest reference export
    for entries in checkpoints.values():
        latest = max(entries, key=lambda path: path.stat().st_mtime_ns)
        protected.update((latest, latest.with_suffix(".onnx")))
    return protected


def prune(root: Path, active: Path, target_bytes: int, log_limit: int = 500 * MB) -> list[str]:
    removed = []
    candidates = []
    logs = []
    for run in owned_runs(root):
        protected = protected_files(run)
        for path in files(run):
            if path in protected:
                continue
            if path.name.endswith(".log") or ".log." in path.name or path.name.startswith("events.out.tfevents"):
                # The current writer is never truncated/deleted beneath its fd.
                if run != active or path.name.startswith("trainer.log."):
                    logs.append(path)
            elif run != active and path.suffix in (".pt", ".onnx") and re.search(r"-\d+\.", path.name):
                candidates.append(path)
    logs.sort(key=lambda path: path.stat().st_mtime_ns)
    all_log_size = sum(path.stat().st_size for run in owned_runs(root) for path in files(run)
                       if path.name.endswith(".log") or ".log." in path.name or path.name.startswith("events.out.tfevents"))
    for path in logs:
        if all_log_size <= log_limit:
            break
        amount = path.stat().st_size
        path.unlink()
        all_log_size -= amount
        removed.append(str(path.relative_to(root)))
    candidates.extend(path for path in logs if path.exists())
    candidates.sort(key=lambda path: path.stat().st_mtime_ns)
    used = size(root)
    for path in candidates:
        if used <= target_bytes:
            break
        amount = path.stat().st_size
        path.unlink()
        used -= amount
        removed.append(str(path.relative_to(root)))
    return removed


class BoundedLog:
    def __init__(self, path: Path, limit: int = 20 * MB):
        self.path, self.limit = path, limit
        self.stream = path.open("ab")

    def append(self, chunk: bytes) -> None:
        if self.stream.tell() + len(chunk) > self.limit:
            self.stream.close()
            for index in range(3, 0, -1):
                previous = self.path.with_name(self.path.name + f".{index}")
                following = self.path.with_name(self.path.name + f".{index + 1}")
                if previous.exists():
                    previous.replace(following)
            self.path.replace(self.path.with_name(self.path.name + ".1"))
            self.stream = self.path.open("wb")
        self.stream.write(chunk)
        self.stream.flush()

    def close(self) -> None:
        self.stream.close()


def trainer_config(max_steps: int, checkpoint_interval: int, self_play: bool = True) -> dict:
    behavior = {
        "trainer_type": "ppo",
        "hyperparameters": {"batch_size": 128, "buffer_size": 2048, "learning_rate": 0.0003,
                            "beta": 0.01, "epsilon": 0.2, "lambd": 0.95, "num_epoch": 3,
                            "learning_rate_schedule": "linear"},
        "network_settings": {"normalize": False, "hidden_units": 128, "num_layers": 2},
        "reward_signals": {"extrinsic": {"gamma": 0.995, "strength": 1.0}},
        "max_steps": max_steps, "time_horizon": 64, "summary_freq": 1000,
        "keep_checkpoints": 5, "checkpoint_interval": checkpoint_interval,
    }
    if self_play:
        behavior["self_play"] = {"save_steps": 5000, "team_change": 10000, "swap_steps": 1000,
                                 "window": 10, "play_against_latest_model_ratio": 0.5,
                                 "initial_elo": 1200}
    return {"behaviors": {"BlockNationsSeatV2": behavior},
            "engine_settings": {"time_scale": 1, "target_frame_rate": 60},
            "torch_settings": {"device": "cpu"}}


def arena_options(run: Path, seed: int, curriculum: bool, resume: bool) -> tuple[int, bool, int]:
    """Keep an owned resumed run's environment contract and curriculum stage."""
    manifest = read_json(run / RUN_MARKER) if resume else {}
    previous = read_json(run / "arena-status.json") if resume else {}
    run_seed = int(manifest.get("seed", seed))
    use_curriculum = bool(manifest.get("curriculum", curriculum))
    distance = int(previous.get("curriculumDistance", 2))
    return run_seed, use_curriculum, distance if distance in (2, 4, 6, 8) else 2


def board_options(run: Path, board_size: int, resume: bool) -> int:
    size = int(read_json(run / RUN_MARKER).get("boardSize", board_size)) if resume else board_size
    if size not in (5, 7, 9, 11):
        raise ValueError("Saved run has an unsupported board size.")
    return size


def interrupt_trainer(process: subprocess.Popen, timeout: float = 90) -> None:
    if process.poll() is not None:
        return
    process.send_signal(signal.SIGINT)
    try:
        process.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        process.terminate()
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def main() -> int:
    global stop_requested
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--hours", type=float, default=8)
    parser.add_argument("--budget-gb", type=float, default=20)
    parser.add_argument("--free-gb", type=float, default=20)
    parser.add_argument("--reserve-mb", type=int, default=512)
    parser.add_argument("--max-steps", type=int, default=1_000_000)
    parser.add_argument("--checkpoint-interval", type=int, default=5000)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--board-size", type=int, choices=(5, 7, 9, 11), default=11)
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--full-openings", action="store_true", help="Disable the tactical opening curriculum for a new run.")
    parser.add_argument("--env", type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]{0,80}", args.run_id):
        raise ValueError("Invalid run id.")
    if args.hours <= 0 or args.budget_gb <= 0 or args.free_gb < 0 or args.reserve_mb < 1 or args.max_steps < 2048:
        raise ValueError("Invalid duration, storage, or step budget.")
    root = args.root.absolute()
    prepare_root(root)
    lock = root / "supervisor.lock"
    # Exclusively owned process lock; a crash leaves a stale pid which may be reclaimed.
    if lock.exists():
        try:
            os.kill(int(lock.read_text()), 0)
        except (ProcessLookupError, ValueError):
            lock.unlink()
        else:
            raise ValueError("A training supervisor is already active.")
    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    os.write(descriptor, str(os.getpid()).encode())
    os.close(descriptor)
    run = root / "runs" / args.run_id
    if run.is_symlink():
        lock.unlink()
        raise ValueError("Run must not be a symbolic link.")
    run.mkdir(exist_ok=True)
    if args.resume:
        if read_json(run / RUN_MARKER).get("owner") != "BlockNations.LocalTraining.v1" or read_json(run / RUN_MARKER).get("schema") != 2 or not list(run.rglob("*.pt")):
            lock.unlink()
            raise ValueError("Resume requires an owned schema v2 run with a saved checkpoint; v1 runs remain archived separately.")
    elif (run / RUN_MARKER).exists():
        lock.unlink()
        raise ValueError("Run already exists; choose Resume or a new run id.")
    budget, reserve = int(args.budget_gb * GB), args.reserve_mb * MB
    if reserve >= budget:
        lock.unlink()
        raise ValueError("Storage budget must exceed checkpoint reserve.")
    removed = prune(root, run, budget - reserve)
    if size(root) > budget - reserve or shutil.disk_usage(root).free < args.free_gb * GB + reserve:
        lock.unlink()
        raise ValueError("Insufficient artifact-budget headroom or free-disk reserve.")
    config = run / "trainer.yaml"
    args.seed, curriculum, curriculum_distance = arena_options(run, args.seed, not args.full_openings, args.resume)
    args.board_size = board_options(run, args.board_size, args.resume)
    if args.board_size != 11:
        curriculum = False
    if not args.resume:
        atomic_json(config, trainer_config(args.max_steps, args.checkpoint_interval))
        atomic_json(run / RUN_MARKER, {"owner": "BlockNations.LocalTraining.v1", "runId": args.run_id,
                                      "createdUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                                      "seed": args.seed, "curriculum": curriculum,
                                      "schema": 2, "boardSize": args.board_size, "behavior": "BlockNationsSeatV2",
                                      "opening": "tactical-curriculum" if curriculum else "standard",
                                      "initialWeights": "random", "observationSize": 3120, "actionCount": 259})
    stop_path = run / "stop.request"
    stop_path.unlink(missing_ok=True)
    # A previous failure or pause is diagnostic history, not this launch's state.
    (run / "arena-status.json").unlink(missing_ok=True)
    for signal_type in (signal.SIGINT, signal.SIGTERM):
        signal.signal(signal_type, lambda *_: globals().__setitem__("stop_requested", True))
    environment = os.environ.copy()
    environment.update({"PYTHONUNBUFFERED": "1", "OMP_NUM_THREADS": "2", "MKL_NUM_THREADS": "2",
                        "OPENBLAS_NUM_THREADS": "2"})
    command = [sys.executable, "-m", "mlagents.trainers.learn", str(config), "--run-id", args.run_id,
               "--results-dir", str(run / "checkpoints"), "--seed", str(args.seed), "--timeout-wait", "300",
               "--torch-device", "cpu"]
    if args.resume:
        command.append("--resume")
    if args.env:
        if not args.env.exists():
            lock.unlink()
            raise ValueError("Standalone training player does not exist.")
        command.extend(["--env", str(args.env), "--width", "1400", "--height", "900", "--time-scale", "1",
                        "--env-args", "--training-status", str(run / "arena-status.json"),
                        "--training-seed", str(args.seed), "--training-curriculum", str(curriculum).lower(),
                        "--training-curriculum-distance", str(curriculum_distance),
                        "--training-board-size", str(args.board_size)])
    process = None
    assertion = None
    started = time.monotonic()
    state = {"runId": args.run_id, "supervisorPid": os.getpid(), "state": "starting", "trainerReady": False, "budgetBytes": budget,
             "reserveBytes": reserve, "durationSeconds": args.hours * 3600, "removedArtifacts": removed[-20:]}
    try:
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   stdin=subprocess.DEVNULL, env=environment, cwd=run)
        state["trainerPid"] = process.pid
        if sys.platform == "darwin":
            # No -d: locking and display sleep remain available. Bound to this supervisor.
            assertion = subprocess.Popen(["/usr/bin/caffeinate", "-i", "-s", "-w", str(os.getpid())],
                                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        elo = TrainerEloHistory(args.run_id, args.board_size,
            read_json(config)["behaviors"]["BlockNationsSeatV2"]["self_play"]["initial_elo"])
        elo.restore(run / "training-elo.json")
        elo.backfill(run)
        log = BoundedLog(run / "trainer.log")
        def capture_output():
            try:
                while chunk := process.stdout.read1(4096):
                    log.append(chunk)
                    elo.feed(chunk)
                    if b"Listening on port 5004" in chunk:
                        state["trainerReady"] = True
            finally:
                log.close()
        output_thread = threading.Thread(target=capture_output, daemon=True)
        output_thread.start()
        reason = "trainer_finished"
        while process.poll() is None:
            elapsed = time.monotonic() - started
            removed.extend(prune(root, run, budget - reserve))
            used, free = size(root), shutil.disk_usage(root).free
            state.update({"state": "running", "elapsedSeconds": elapsed, "usedBytes": used, "freeBytes": free,
                          "removedArtifacts": removed[-20:], "updatedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})
            resident = process_tree_rss(process.pid)
            if resident is not None:
                state["peakResidentBytes"] = max(state.get("peakResidentBytes", 0), resident)
            atomic_json(run / "supervisor-status.json", state)
            atomic_json(run / "training-elo.json", elo.snapshot())
            atomic_json(root / "active.json", {"runId": args.run_id, "supervisorPid": os.getpid()})
            arena = read_json(run / "arena-status.json")
            if arena.get("trainerConnected"):
                state["trainerReady"] = True
            state["checkpointCount"] = len(list(run.rglob("*.pt")))
            state["exportCount"] = len(list(run.rglob("*.onnx")))
            if stop_requested or stop_path.exists():
                reason = "user_stop"
            elif elapsed >= args.hours * 3600:
                reason = "duration_limit"
            elif used >= budget - reserve:
                reason = "artifact_budget"
            elif free < args.free_gb * GB + reserve:
                reason = "free_disk_guard"
            elif arena.get("failure"):
                reason = "arena_failure"
            else:
                time.sleep(2)
                continue
            state.update({"state": "saving", "stopReason": reason})
            atomic_json(run / "supervisor-status.json", state)
            interrupt_trainer(process)
            break
        output_thread.join(timeout=5)
        atomic_json(run / "training-elo.json", elo.snapshot())
        checkpoints = list(run.rglob("*.pt"))
        exports = list(run.rglob("*.onnx"))
        failed = process.returncode != 0 or reason == "arena_failure"
        state.update({"state": "failed" if failed else "stopped", "stopReason": reason,
                      "exitCode": process.returncode, "elapsedSeconds": time.monotonic() - started,
                      "usedBytes": size(root), "freeBytes": shutil.disk_usage(root).free,
                      "checkpointCount": len(checkpoints), "exportCount": len(exports)})
        atomic_json(run / "supervisor-status.json", state)
        return process.returncode or (1 if failed else 0)
    finally:
        if process is not None and process.poll() is None:
            interrupt_trainer(process)
        if assertion is not None:
            assertion.terminate()
            assertion.wait(timeout=5)
        if lock.exists() and lock.read_text() == str(os.getpid()):
            lock.unlink()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Training supervisor: {error}", file=sys.stderr)
        raise SystemExit(1)
