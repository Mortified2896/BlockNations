#!/usr/bin/env python3
"""Build a frozen local Mac opponent without a trainer or training-folder writes."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

SOURCE = Path(__file__).resolve().parents[2]
MARKER = '.private-policy-playtest-snapshot'
MODEL = 'Assets/Resources/LearnedAI/Compact7.onnx'
MANIFEST = 'Assets/Resources/LearnedAI/manifest.json'
MACHO_MAGIC = {bytes.fromhex(value) for value in
               ('cafebabe', 'bebafeca', 'cafebabf', 'bfbafeca',
                'cffaedfe', 'cefaedfe', 'feedfacf', 'feedface')}


def module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def validate_paths(snapshot, output):
    for path in (snapshot, output):
        if not path.is_absolute() or path.resolve() != path:
            raise ValueError('Use absolute canonical paths without symbolic links.')
    if snapshot == SOURCE or SOURCE in snapshot.parents or snapshot in SOURCE.parents:
        raise ValueError('The private snapshot must be outside the working repository.')
    if (SOURCE / 'Build').resolve() not in output.parents or output.suffix != '.app':
        raise ValueError('Choose a new .app under this checkout\'s Build directory.')
    if snapshot == output or snapshot in output.parents or output in snapshot.parents:
        raise ValueError('Keep the snapshot and output separate.')
    if output.exists():
        raise ValueError('Preserve existing apps; choose a new output name.')
    if snapshot.exists() and any(snapshot.iterdir()) and not (snapshot / MARKER).is_file():
        raise ValueError('Preserve unrelated snapshots; choose a new private snapshot directory.')


def frozen_policy(snapshot):
    manifest = json.loads((snapshot / MANIFEST).read_text())
    required = dict(schema=2, boardSize=7, observationSize=3120, actionCount=259,
                    rulesVersion='blocknations-simulation-v5-core3', openingEconomyVersion=2,
                    probabilityOutput='policy_probabilities')
    if any(manifest.get(key) != value for key, value in required.items()):
        raise ValueError('Choose a compatible frozen 7x7 core-three release.')
    data = (snapshot / MODEL).read_bytes()
    if (hashlib.sha256(data).hexdigest() != manifest.get('modelSha256') or
            len(data) != manifest.get('modelBytes') or
            not re.fullmatch(r'[a-f0-9]{64}', manifest.get('checkpointSha256', ''))):
        raise ValueError('Frozen model bytes or checkpoint provenance differ from their manifest.')
    return manifest


def audit_bundle(app, secrets):
    """Scan owned native bytes, allowing ordinary internal framework links."""
    signatures = [re.compile(rb'sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}'),
                  re.compile(rb'(?:ghp_|github_pat_)[A-Za-z0-9_]{30,}'),
                  re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----\s+[A-Za-z0-9+/=]{64,}')]
    count, total = 0, 0
    for path in app.rglob('*'):
        if path.is_symlink() and not path.resolve().is_relative_to(app):
            raise ValueError('The native bundle contains a link outside its app.')
        if not path.is_file():
            continue
        relative = path.relative_to(app)
        if (path.name in ('.env', 'auth.json') or
                set(relative.parts) & {'.git', 'UserSettings', 'human-games', 'checkpoints', 'TrainingModels'}):
            raise ValueError('Private material path in native bundle: ' + str(relative))
        data = path.read_bytes()
        if (any(secret in data or secret.decode(errors='ignore').encode('utf-16-le') in data for secret in secrets) or
                any(pattern.search(data) for pattern in signatures)):
            raise ValueError('Credential match in native bundle: ' + str(relative))
        count += 1
        total += len(data)
    if not count:
        raise ValueError('The native bundle is empty.')
    return dict(files=count, totalBytes=total, knownCredentialChecks=len(secrets), credentialMatches=0)


def validate_native_app(app):
    with (app / 'Contents/Info.plist').open('rb') as stream:
        info = plistlib.load(stream)
    name = info.get('CFBundleExecutable', '')
    if (info.get('CFBundleIdentifier') != 'com.blocknations.privatepolicyplaytest' or
            not name or Path(name).name != name):
        raise ValueError('The private application has the wrong identity or executable.')
    required = {f'MacOS/{name}', 'Frameworks/UnityPlayer.dylib',
                'Frameworks/libmonobdwgc-2.0.dylib'}
    architectures = {}
    for folder in ('MacOS', 'Frameworks', 'PlugIns', 'MonoBleedingEdge'):
        for path in sorted((app / 'Contents' / folder).rglob('*')):
            if not path.is_file() or path.is_symlink():
                continue
            with path.open('rb') as stream:
                if stream.read(4) not in MACHO_MAGIC:
                    continue
            archs = subprocess.check_output(['/usr/bin/lipo', '-archs', str(path)], text=True).split()
            if 'arm64' not in archs:
                raise ValueError('A native component cannot run on Apple Silicon: ' +
                                 str(path.relative_to(app)))
            architectures[str(path.relative_to(app / 'Contents'))] = archs
    if not required.issubset(architectures):
        raise ValueError('The private application is missing its executable or Mono runtime.')
    # Unity's launcher and frameworks may be universal even for an arm64 build.
    # Validate every native component, rather than rejecting that valid launcher.
    return architectures


def write_receipt(path, value):
    with path.open('x') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')


def validate_flow_results(path):
    result = ET.parse(path).getroot()
    if (result.get('result') != 'Passed' or result.get('failed') != '0' or
            result.get('passed') != '4' or result.get('skipped', '0') != '0'):
        raise RuntimeError('The four fresh private gameplay flow tests did not all pass.')
    return dict(testsPassed=4, failed=0, skipped=0,
                resultsSha256=hashlib.sha256(path.read_bytes()).hexdigest())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--snapshot', type=Path, default=Path.home() / '.local/share/blocknations-private-policy/project')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--cpu-fixtures', type=Path)
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--validate-flow', action='store_true',
                        help='Test the owned scene, all three local presets, role labels, rematches and menu return before building.')
    args = parser.parse_args()
    if args.validate_flow and not args.cpu_fixtures:
        parser.error('--validate-flow requires --cpu-fixtures to validate and compile Editor inference jobs before gameplay.')
    snapshot, output = args.snapshot.absolute(), args.output.absolute()
    validate_paths(snapshot, output)
    web = module(SOURCE / 'Tools/WebRelease/build.py', 'private_snapshot')
    audit = module(SOURCE / 'Tools/WebRelease/audit.py', 'private_audit')
    before = web.source_digest()
    # Share the reviewed exclusions: credentials, ML-Agents, trainer assets and code.
    web.prepare(snapshot)
    (snapshot / MARKER).write_text('Owned frozen local Mac playtest snapshot.\n')
    policy = frozen_policy(snapshot)
    if web.source_digest() != before:
        raise RuntimeError('Game source changed during preparation; prepare again.')
    if args.prepare_only:
        print(json.dumps(dict(state='prepared', sourceHash=before, policy=policy), indent=2))
        return
    version = (SOURCE / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(':', 1)[1].strip()
    unity = Path(f'/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity')
    if not unity.is_file():
        raise ValueError('Install the matching Unity Editor through Hub.')
    output.parent.mkdir(parents=True, exist_ok=True)
    log = output.parent / (output.stem + '-build.log')
    if log.exists():
        raise ValueError('Preserve earlier build logs; choose a new output name.')
    intent = dict(version=1, state='building', startedUtc=datetime.now(timezone.utc).isoformat(),
                  sourceHash=before, policy=policy, unity=version, snapshot=str(snapshot),
                  output=str(output), log=str(log), builderSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
    if args.cpu_fixtures:
        intent['cpuFixturesSha256'] = hashlib.sha256(args.cpu_fixtures.read_bytes()).hexdigest()
    # Preserve provenance even if Unity or a later check fails. Never overwrite
    # an earlier build attempt, application, log or successful release receipt.
    write_receipt(output.parent / (output.stem + '-intent.json'), intent)
    environment = {key: value for key, value in os.environ.items()
                   if not key.startswith('BLOCKNATIONS_') and not any(word in key.upper() for word in ('API_KEY', 'TOKEN', 'SECRET'))}
    environment['BLOCKNATIONS_PRIVATE_PLAYTEST_OUTPUT'] = str(output)
    if args.cpu_fixtures:
        environment['BLOCKNATIONS_POLICY_VALIDATION_DATA'] = str(args.cpu_fixtures.absolute())
    base_command = ['/usr/bin/nice', '-n', '10', str(unity), '-batchmode', '-job-worker-count', '2',
                    '-projectPath', str(snapshot)]
    flow = None
    if args.validate_flow:
        fixture = SOURCE / 'Assets/Editor/Tests/PrivatePolicyPlaytestFlowTests.cs'
        test_directory = snapshot / 'Assets/Editor/Tests'
        test_directory.mkdir(parents=True, exist_ok=True)
        for path in (fixture, fixture.with_suffix('.cs.meta')):
            shutil.copyfile(path, test_directory / path.name)
        configuration_log = output.parent / (output.stem + '-configure.log')
        flow_log = output.parent / (output.stem + '-flow.log')
        flow_xml = output.parent / (output.stem + '-flow.xml')
        if any(path.exists() for path in (configuration_log, flow_log, flow_xml)):
            raise ValueError('Preserve earlier validation records; choose a new output name.')
        subprocess.run(base_command + ['-quit', '-executeMethod', 'PrivatePolicyPlaytestBuild.ConfigureValidation',
                       '-logFile', str(configuration_log)], env=environment, check=True)
        # UITK needs graphics. The test runner exits on completion; -quit could
        # exit too early and must not be used for this phase.
        subprocess.run(base_command + ['-runTests', '-testPlatform', 'EditMode',
                       '-testFilter', 'PrivatePolicyPlaytestFlowTests', '-testResults', str(flow_xml),
                       '-logFile', str(flow_log)], env=environment, check=True)
        flow = validate_flow_results(flow_xml)
        flow['fixtureSha256'] = hashlib.sha256(fixture.read_bytes()).hexdigest()
        # Restore the reviewed snapshot exclusion before producing the app.
        shutil.rmtree(test_directory)
        meta = test_directory.with_suffix('.meta')
        if meta.exists(): meta.unlink()
    subprocess.run(base_command + ['-quit',
                    '-executeMethod', 'PrivatePolicyPlaytestBuild.BuildMac', '-logFile', str(log)],
                   env=environment, check=True)
    architectures = validate_native_app(output)
    inspection = audit_bundle(output, audit.known_secrets())
    if web.source_digest() != before or frozen_policy(snapshot) != policy:
        raise RuntimeError('Source or frozen model changed during build; preserve this artifact and rebuild.')
    receipt = dict(version=1, state='built', game='Block Nations', mode='private-native-policy-playtest',
                   unity=version, sourceHash=before, policy=policy, difficulties=['Easy', 'Medium', 'Hard'],
                   architecture='arm64', nativeComponents=architectures,
                   trainerRequired=False, externalInferenceRequired=False,
                   publicDeployment=False, audit=inspection, log=str(log))
    if flow:
        receipt['gameplayFlowValidation'] = flow
    receipt['playAgain'] = dict(humanSeat=0, randomFirstSeat=True, preservesDifficulty=True)
    receipt_path = output.parent / (output.stem + '-release.json')
    write_receipt(receipt_path, receipt)
    print(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    main()
