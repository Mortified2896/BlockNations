from pathlib import Path
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


if __name__ == "__main__": unittest.main()
