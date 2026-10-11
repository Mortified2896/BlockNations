"""Private build ownership, model compatibility and credential boundaries."""
import hashlib
import json
from pathlib import Path
import plistlib
import tempfile
import unittest
from unittest.mock import patch

import build


class PrivateBuildTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.base = Path(self.temporary.name).resolve()
        self.source = self.base / 'source'
        self.source.mkdir()
        self.patch = patch.object(build, 'SOURCE', self.source)
        self.patch.start()
        self.snapshot = self.base / 'snapshot'
        self.output = self.source / 'Build/Test.app'

    def tearDown(self):
        self.patch.stop()
        self.temporary.cleanup()

    def test_new_canonical_paths_accepted(self):
        build.validate_paths(self.snapshot, self.output)

    def test_source_overlap_and_nonbuild_outputs_rejected(self):
        for snapshot, output in [(self.source, self.output), (self.source/'nested', self.output),
                                 (self.base, self.output), (self.snapshot, self.base/'Outside.app'),
                                 (self.snapshot, self.source/'Build/wrong.zip')]:
            with self.subTest(snapshot=snapshot, output=output), self.assertRaises(ValueError):
                build.validate_paths(snapshot, output)

    def test_existing_application_preserved(self):
        self.output.mkdir(parents=True)
        p = self.output/'unrelated.txt'; p.write_text('keep')
        with self.assertRaises(ValueError):build.validate_paths(self.snapshot, self.output)
        self.assertEqual(p.read_text(), 'keep')

    def test_unowned_snapshot_preserved(self):
        self.snapshot.mkdir();p=self.snapshot/'original.txt';p.write_text('keep')
        with self.assertRaises(ValueError):build.validate_paths(self.snapshot, self.output)
        self.assertEqual(p.read_text(),'keep')
        (self.snapshot/build.MARKER).write_text('owned')
        build.validate_paths(self.snapshot,self.output)

    def test_linked_snapshot_and_output_rejected(self):
        target=self.base/'target';target.mkdir()
        self.snapshot.symlink_to(target,target_is_directory=True)
        with self.assertRaises(ValueError):build.validate_paths(self.snapshot,self.output)
        self.snapshot.unlink()
        self.output.parent.mkdir(parents=True);self.output.symlink_to(target,target_is_directory=True)
        with self.assertRaises(ValueError):build.validate_paths(self.snapshot,self.output)

    def policy(self):
        p=self.snapshot/build.MODEL;p.parent.mkdir(parents=True)
        p.write_bytes(b'synthetic frozen graph')
        m=dict(schema=2,boardSize=7,observationSize=3120,actionCount=259,
               rulesVersion='blocknations-simulation-v5-core3',openingEconomyVersion=2,
               probabilityOutput='policy_probabilities',modelBytes=p.stat().st_size,
               checkpointSha256='a'*64,modelSha256=hashlib.sha256(p.read_bytes()).hexdigest())
        (self.snapshot/build.MANIFEST).write_text(json.dumps(m));return m

    def test_model_hash_and_contract_mismatch_rejected(self):
        manifest=self.policy();self.assertEqual(build.frozen_policy(self.snapshot),manifest)
        for key,value in [('boardSize',11),('rulesVersion','blocknations-simulation-v4'),
                          ('modelBytes',1),('modelSha256','0'*64),('checkpointSha256','missing')]:
            with self.subTest(key=key):
                (self.snapshot/build.MANIFEST).write_text(json.dumps(dict(manifest,**{key:value})))
                with self.assertRaises(ValueError):build.frozen_policy(self.snapshot)

    def test_bundle_known_secret_and_signatures_rejected_without_disclosure(self):
        self.output.mkdir(parents=True);p=self.output/'game.bin'
        secret=b'synthetic-local-credential-value'
        for value,secrets in [(b'prefix'+secret,[secret]),(secret.decode().encode('utf-16-le'),[secret]),
                              (b'sk-proj-'+b'x'*40,[]),
                              (b'-----BEGIN PRIVATE KEY-----\n'+b'A'*64,[])]:
            p.write_bytes(value)
            with self.assertRaises(ValueError) as error:build.audit_bundle(self.output,secrets)
            self.assertNotIn(secret.decode(),str(error.exception))

    def test_private_paths_and_external_links_rejected(self):
        self.output.mkdir(parents=True);private=self.output/'checkpoints';private.mkdir()
        p=private/'model.pt';p.write_bytes(b'private')
        with self.assertRaises(ValueError):build.audit_bundle(self.output,[])
        p.unlink();private.rmdir()
        outside=self.base/'outside';outside.write_text('preserve');(self.output/'linked').symlink_to(outside)
        with self.assertRaises(ValueError):build.audit_bundle(self.output,[])
        self.assertEqual(outside.read_text(),'preserve')

    def test_internal_framework_link_and_parser_marker_allowed(self):
        self.output.mkdir(parents=True);p=self.output/'framework.bin'
        p.write_bytes(b'parser literal: -----BEGIN PRIVATE KEY-----')
        (self.output/'framework-link').symlink_to(p)
        receipt=build.audit_bundle(self.output,[])
        self.assertEqual(receipt['credentialMatches'],0)
        self.assertEqual(receipt['files'],2)

    def native_app(self):
        contents = self.output/'Contents'; contents.mkdir(parents=True)
        with (contents/'Info.plist').open('wb') as stream:
            plistlib.dump(dict(CFBundleIdentifier='com.blocknations.privatepolicyplaytest',
                              CFBundleExecutable='Game'),stream)
        for relative in ('MacOS/Game','Frameworks/UnityPlayer.dylib',
                         'Frameworks/libmonobdwgc-2.0.dylib','PlugIns/test.dylib'):
            p=contents/relative;p.parent.mkdir(exist_ok=True);p.write_bytes(bytes.fromhex('cafebabe'))
        return contents

    def test_universal_launcher_and_plugins_accepted_when_arm64_present(self):
        self.native_app()
        with patch.object(build.subprocess,'check_output',return_value='x86_64 arm64\n'):
            self.assertEqual(len(build.validate_native_app(self.output)),4)

    def test_missing_arm64_plugin_or_runtime_rejected(self):
        contents=self.native_app()
        def architecture(command,**kwargs):
            return 'x86_64' if command[-1].endswith('test.dylib') else 'arm64'
        with patch.object(build.subprocess,'check_output',side_effect=architecture), self.assertRaises(ValueError):
            build.validate_native_app(self.output)
        (contents/'Frameworks/UnityPlayer.dylib').unlink()
        with patch.object(build.subprocess,'check_output',return_value='arm64'),self.assertRaises(ValueError):
            build.validate_native_app(self.output)

    def test_build_receipts_never_overwrite_previous_provenance(self):
        receipt=self.base/'intent.json';build.write_receipt(receipt,dict(state='building',sourceHash='original'))
        with self.assertRaises(FileExistsError):build.write_receipt(receipt,dict(state='replacement'))
        self.assertEqual(json.loads(receipt.read_text())['sourceHash'],'original')


if __name__=='__main__':unittest.main()
