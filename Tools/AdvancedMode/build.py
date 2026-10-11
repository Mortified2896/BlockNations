#!/usr/bin/env python3
"""Build only the advanced human/scripted-opponent playtest, using the shared C# rules."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

SOURCE = Path(__file__).resolve().parents[2]
MARKER = '.advanced-mode-web-snapshot'

def prepare(snapshot):
    if snapshot == SOURCE or SOURCE in snapshot.parents or snapshot in SOURCE.parents:
        raise ValueError('Snapshot must be outside the source checkout')
    if snapshot.exists() and any(snapshot.iterdir()) and not (snapshot / MARKER).is_file():
        raise ValueError('Refusing to replace an unowned project')
    snapshot.mkdir(parents=True, exist_ok=True)
    (snapshot / MARKER).write_text('Owned advanced playtest build project; never upload this directory.\n')
    # Whitelist excludes credentials, saved matches, learned actors, training and unrelated Resources.
    selected = ['Assets/Scripts/Simulation', 'Assets/Scripts/AdvancedMode', 'Assets/Resources/AdvancedPlaytest',
                'Assets/WebGLTemplates/Advanced', 'Assets/Editor/AdvancedModeWebBuild.cs',
                'Assets/Editor/AdvancedModeWebBuild.cs.meta']
    for name in ('Warrior', 'Rider', 'Archer'):
        selected += [f'Assets/Sprites/{name}.png', f'Assets/Sprites/{name}.png.meta']
    for relative in selected:
        source = SOURCE / relative; target = snapshot / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if source.is_dir():
            if target.exists(): shutil.rmtree(target)
            shutil.copytree(source, target)
        else: shutil.copy2(source, target)
    if (snapshot / 'ProjectSettings').exists(): shutil.rmtree(snapshot / 'ProjectSettings')
    shutil.copytree(SOURCE / 'ProjectSettings', snapshot / 'ProjectSettings')
    generated = snapshot / 'Assets/GeneratedAdvanced'
    if generated.exists(): shutil.rmtree(generated)
    packages = snapshot / 'Packages'; packages.mkdir(exist_ok=True)
    (packages / 'manifest.json').write_text(json.dumps({'dependencies': {
        'com.unity.inputsystem': '1.19.0', 'com.unity.ugui': '2.0.0',
        'com.unity.modules.ui': '1.0.0', 'com.unity.modules.uielements': '1.0.0',
        'com.unity.modules.imgui': '1.0.0', 'com.unity.modules.jsonserialize': '1.0.0'
    }}, indent=2) + '\n')
    return selected

def source_hash(selected):
    digest = hashlib.sha256()
    for relative in selected + ['ProjectSettings']:
        path = SOURCE / relative
        files = sorted(path.rglob('*')) if path.is_dir() else [path]
        for item in files:
            if item.is_file():
                digest.update(str(item.relative_to(SOURCE)).encode()); digest.update(item.read_bytes())
    return digest.hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--snapshot', type=Path, default=Path.home() / '.local/share/blocknations-advanced/project')
    parser.add_argument('--output', type=Path, default=SOURCE / 'Build/AdvancedWeb')
    parser.add_argument('--prepare-only', action='store_true')
    args = parser.parse_args(); snapshot = args.snapshot.resolve(); output = args.output.resolve()
    if (SOURCE / 'Build').resolve() not in output.parents: raise ValueError('Output must be inside Build/')
    selected = prepare(snapshot)
    if args.prepare_only: return
    expected_hash = source_hash(selected)
    if output.exists(): shutil.rmtree(output)
    output.mkdir(parents=True)
    version = (SOURCE / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(':', 1)[1].strip()
    unity = f'/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity'
    log = SOURCE / 'Logs/Validation/AdvancedMode/web-build.log'; log.parent.mkdir(parents=True, exist_ok=True)
    env = {k: v for k, v in os.environ.items() if not any(word in k.upper() for word in ('API_KEY', 'TOKEN', 'SECRET'))}
    env['BLOCKNATIONS_ADVANCED_OUTPUT'] = str(output); env['EMCC_CORES'] = '2'
    print(f'Building advanced single player with Unity {version}; log: {log}', flush=True)
    subprocess.run(['nice', '-n', '10', unity, '-batchmode', '-quit', '-job-worker-count', '2', '-projectPath', str(snapshot),
                    '-buildTarget', 'WebGL', '-executeMethod', 'AdvancedModeWebBuild.Build', '-logFile', str(log)], env=env, check=True)
    if not (output / 'index.html').is_file(): raise RuntimeError('Missing advanced HTML')
    shutil.copy2(SOURCE / 'Web/_headers', output / '_headers')
    if source_hash(selected) != expected_hash: raise RuntimeError('Source changed during build; rebuild before release')
    (output / 'release.json').write_text(json.dumps({'game': 'Block Nations', 'mode': 'advanced-single-player', 'boardSize': 7,
        'ai': 'scripted-v1', 'rules': 'advanced-economy-prototype-v1', 'sourceHash': expected_hash, 'unity': version,
        'persistence': 'in-memory'}, indent=2) + '\n')
    subprocess.run([sys.executable, str(SOURCE / 'Tools/WebRelease/audit.py'), str(output)], check=True)

if __name__ == '__main__': main()
