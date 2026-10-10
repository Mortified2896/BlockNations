from pathlib import Path
import json
import tempfile
import unittest
from unittest.mock import patch
import build


class BuildPathTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.source = self.root / "repo"
        self.source.mkdir()
        self.source_patch = patch.object(build, "SOURCE", self.source)
        self.source_patch.start()
        self.addCleanup(self.source_patch.stop)

    def test_accepts_separate_snapshot_and_generated_output(self):
        build.validate_paths(self.root/"snapshot", self.source/"Build/WebRelease")

    def test_rejects_snapshot_in_or_above_repository(self):
        for snapshot in (self.source, self.source/"Assets", self.root):
            with self.subTest(snapshot=snapshot), self.assertRaises(ValueError):
                build.validate_paths(snapshot, self.source/"Build/WebRelease")

    def test_cannot_delete_source_or_build_root_as_output(self):
        for output in (self.source, self.source/"Assets", self.source/"Build", self.root/"unrelated"):
            with self.subTest(output=output), self.assertRaises(ValueError):
                build.validate_paths(self.root/"snapshot", output)

    def test_cannot_overwrite_an_unowned_snapshot_directory(self):
        snapshot = self.root/"existing-project"
        snapshot.mkdir()
        original = snapshot/"keep.txt"
        original.write_text("unrelated project")
        with self.assertRaisesRegex(ValueError, "not an owned"):
            build.prepare(snapshot)
        self.assertEqual(original.read_text(), "unrelated project")

    def release_fixture(self):
        for name in ("Assets/Resources/LearnedAI", "Assets/Scripts/Training", "Packages", "ProjectSettings"):
            (self.source/name).mkdir(parents=True, exist_ok=True)
        (self.source/"Assets/Resources/LearnedAI/model.onnx").write_bytes(b"synthetic actor")
        (self.source/"Assets/Resources/PbpTransportSettings.asset").write_text("private fixture")
        (self.source/"Assets/Scripts/Training/Private.cs").write_text("training fixture")
        (self.source/"Packages/manifest.json").write_text(json.dumps({"dependencies": {
            "com.unity.ml-agents": "fixture", "com.unity.ai.assistant": "fixture", "com.unity.inputsystem": "fixture",
        }}))

    def test_account_snapshot_keeps_native_credentials_and_unapproved_actor_out_of_release(self):
        self.release_fixture()
        snapshot = self.root/"account-snapshot"
        build.prepare(snapshot, multiplayer=True)
        self.assertFalse((snapshot/"Assets/Resources/LearnedAI").exists())
        self.assertFalse((snapshot/"Assets/Resources/PbpTransportSettings.asset").exists())
        self.assertFalse((snapshot/"Assets/Scripts/Training").exists())
        self.assertEqual(json.loads((snapshot/"Packages/manifest.json").read_text())["dependencies"], {"com.unity.inputsystem": "fixture"})
        self.assertTrue((self.source/"Assets/Resources/LearnedAI/model.onnx").exists())
        self.assertTrue((self.source/"Assets/Resources/PbpTransportSettings.asset").exists())

    def test_separate_learned_candidate_still_preserves_its_frozen_actor(self):
        self.release_fixture()
        snapshot = self.root/"candidate-snapshot"
        build.prepare(snapshot)
        self.assertTrue((snapshot/"Assets/Resources/LearnedAI/model.onnx").exists())
        self.assertFalse((snapshot/"Assets/Resources/PbpTransportSettings.asset").exists())


if __name__ == "__main__": unittest.main()
