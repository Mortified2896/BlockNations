#!/usr/bin/env python3
"""Prepare and build a separate Unity project with no multiplayer or training secrets."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

SOURCE = Path(__file__).resolve().parents[2]
SNAPSHOT_MARKER = ".single-player-web-snapshot"


def validate_paths(snapshot: Path, output: Path):
    if snapshot == SOURCE or SOURCE in snapshot.parents or snapshot in SOURCE.parents:
        raise ValueError("The build snapshot must be outside and separate from the working repository")
    build_root = (SOURCE / "Build").resolve()
    if build_root not in output.parents:
        raise ValueError("The output must be a subdirectory of the repository's Build directory")
    if snapshot == output or snapshot in output.parents or output in snapshot.parents:
        raise ValueError("The output and snapshot must not overlap")


def prepare(snapshot: Path):
    if snapshot == SOURCE or SOURCE in snapshot.parents or snapshot in SOURCE.parents:
        raise ValueError("The build snapshot must be outside and separate from the working repository")
    marker = snapshot / SNAPSHOT_MARKER
    if snapshot.exists() and any(snapshot.iterdir()) and not marker.is_file():
        raise ValueError("Refusing to replace an existing directory that is not an owned web build snapshot")
    snapshot.mkdir(parents=True, exist_ok=True)
    marker.write_text("Dedicated single-player web build. Never deploy this project directory.\n")
    for folder in ("Assets", "Packages", "ProjectSettings"):
        subprocess.run(["rsync", "-a", "--delete", str(SOURCE / folder) + "/", str(snapshot / folder) + "/"], check=True)
    # Resources are always bundled, even when their menus/scenes are unused.
    for relative in (
        "Assets/Resources/PbpTransportSettings.asset", "Assets/Resources/PbpTransportSettings.asset.meta",
        "Assets/TrainingModels", "Assets/TrainingModels.meta", "Assets/ML-Agents", "Assets/ML-Agents.meta",
        "Assets/Scripts/Training", "Assets/Scripts/Training.meta",
        "Assets/Editor/Training", "Assets/Editor/Training.meta",
        "Assets/Editor/Tests", "Assets/Editor/Tests.meta", "Assets/Tests", "Assets/Tests.meta",
        "Assets/Scenes/LocalTraining.unity", "Assets/Scenes/LocalTraining.unity.meta",
    ):
        path = snapshot / relative
        if path.is_dir(): shutil.rmtree(path)
        elif path.exists(): path.unlink()
    manifest = snapshot / "Packages/manifest.json"
    packages = json.loads(manifest.read_text())
    for name in ("com.unity.ai.assistant", "com.unity.ml-agents"):
        packages["dependencies"].pop(name, None)
    manifest.write_text(json.dumps(packages, indent=2) + "\n")
    services = snapshot/"ProjectSettings/UnityConnectSettings.asset"
    if services.exists():
        services.write_text(re.sub(r"(m_(?:Enabled|InitializeOnStartup):) 1", r"\1 0", services.read_text()))


def source_digest():
    source_hash = hashlib.sha256()
    for folder in ("Assets", "Packages", "ProjectSettings"):
        for path in sorted((SOURCE/folder).rglob("*")):
            if path.is_file() and not any(x in path.parts for x in ("TrainingModels", "ML-Agents")):
                source_hash.update(str(path.relative_to(SOURCE)).encode())
                source_hash.update(path.read_bytes())
    return source_hash.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot", type=Path, default=Path.home()/".local/share/blocknations-web/project")
    parser.add_argument("--output", type=Path, default=SOURCE/"Build/WebRelease")
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args()
    snapshot, output = args.snapshot.resolve(), args.output.resolve()
    validate_paths(snapshot, output)
    prepare(snapshot)
    if args.prepare_only: return
    version = (SOURCE/"ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(":", 1)[1].strip()
    unity = Path(f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity")
    if not unity.exists(): raise RuntimeError(f"Unity {version} is not installed at the expected Mac Hub path")
    log = SOURCE/"Logs/WebRelease/build.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    # Start with a clean output so older symbols, resources, or files cannot survive a release.
    if output.exists(): shutil.rmtree(output)
    output.mkdir(parents=True)
    env = os.environ.copy()
    for key in tuple(env):
        if any(word in key.upper() for word in ("API_KEY", "TOKEN", "SECRET")): env.pop(key)
    env["BLOCKNATIONS_WEB_OUTPUT"] = str(output)
    env["EMCC_CORES"] = "2"
    command = ["nice", "-n", "10", str(unity), "-batchmode", "-quit", "-job-worker-count", "2",
               "-projectPath", str(snapshot), "-buildTarget", "WebGL", "-executeMethod", "SinglePlayerWebBuild.Build", "-logFile", str(log)]
    print(f"Building single-player Web release with Unity {version}; log: {log}", flush=True)
    subprocess.run(command, env=env, check=True)
    if not (output/"index.html").exists(): raise RuntimeError("Unity did not produce the game page")
    shutil.copyfile(SOURCE/"Web/_headers", output/"_headers")
    (output/"release.json").write_text(json.dumps({"game":"Block Nations", "mode":"single-player",
        "unity":version, "sourceHash":source_digest(), "ai":["Normal", "Rider Focus", "Hard"]}, indent=2)+"\n")
    subprocess.run([sys.executable, str(SOURCE/"Tools/WebRelease/audit.py"), str(output)], check=True)


if __name__ == "__main__": main()
