import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("training_supervisor", Path(__file__).with_name("supervisor.py"))
supervisor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(supervisor)


class RetentionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name) / "training"
        supervisor.prepare_root(self.root)

    def tearDown(self):
        self.temporary.cleanup()

    def run_directory(self, name):
        run = self.root / "runs" / name
        run.mkdir()
        supervisor.atomic_json(run / "run.json", {"owner": "BlockNations.LocalTraining.v1"})
        return run

    def test_unmarked_nonempty_directory_is_never_adopted(self):
        root = Path(self.temporary.name) / "unrelated"
        root.mkdir()
        (root / "important.txt").write_text("keep")
        with self.assertRaises(ValueError):
            supervisor.prepare_root(root)
        self.assertEqual((root / "important.txt").read_text(), "keep")

    def test_active_latest_pinned_and_foreign_files_survive_budget_retention(self):
        active, old = self.run_directory("active"), self.run_directory("old")
        active_file = active / "Policy-1.pt"
        active_file.write_bytes(b"active")
        removable = old / "Policy-1.pt"
        removable.write_bytes(b"obsolete")
        pinned = old / "Policy-2.onnx"
        pinned.write_bytes(b"pinned")
        pinned.with_suffix(".pt").write_bytes(b"pinned resume")
        latest = old / "Policy-3.pt"
        latest.write_bytes(b"latest")
        latest_export = old / "Policy.onnx"
        latest_export.write_bytes(b"reference")
        supervisor.atomic_json(old / "pins.json", {"files": [pinned.name]})
        unrelated = self.root / "runs" / "foreign"
        unrelated.mkdir()
        foreign = unrelated / "Policy-1.pt"
        foreign.write_bytes(b"not owned")
        supervisor.prune(self.root, active, target_bytes=0)
        self.assertFalse(removable.exists())
        for path in (active_file, pinned, pinned.with_suffix(".pt"), latest, latest_export, foreign):
            self.assertTrue(path.exists(), path)

    def test_symbolic_links_are_not_followed_or_pruned(self):
        old = self.run_directory("old")
        outside = Path(self.temporary.name) / "outside"
        outside.mkdir()
        important = outside / "Policy-1.pt"
        important.write_bytes(b"keep")
        (old / "linked").symlink_to(outside, target_is_directory=True)
        supervisor.prune(self.root, self.run_directory("active"), target_bytes=0)
        self.assertEqual(important.read_bytes(), b"keep")
        self.assertNotIn(important, list(supervisor.files(self.root)))

    def test_checkpoint_metadata_reference_remains_protected(self):
        old = self.run_directory("old")
        keep = old / "Policy-1.pt"
        keep.write_bytes(b"referenced")
        newer = old / "Policy-2.pt"
        newer.write_bytes(b"new")
        supervisor.atomic_json(old / "checkpoint.json", {"last_checkpoint": {"file_path": str(keep)}})
        supervisor.prune(self.root, self.run_directory("active"), target_bytes=0)
        self.assertTrue(keep.exists())
        self.assertTrue(newer.exists())

    def test_log_rotation_has_a_fixed_upper_bound(self):
        path = self.run_directory("active") / "trainer.log"
        log = supervisor.BoundedLog(path, limit=8)
        for _ in range(40):
            log.append(b"1234")
        log.close()
        self.assertLessEqual(sum(p.stat().st_size for p in path.parent.glob("trainer.log*")), 40)

    def test_configuration_is_real_two_team_self_play_with_retained_checkpoints(self):
        config = supervisor.trainer_config(4096, 2048)
        behavior = config["behaviors"]["BlockNationsSeatV2"]
        self.assertEqual(behavior["trainer_type"], "ppo")
        self.assertEqual(behavior["keep_checkpoints"], 5)
        self.assertGreater(behavior["self_play"]["window"], 0)
        self.assertEqual(config["torch_settings"]["device"], "cpu")

    def test_resume_keeps_environment_contract_and_curriculum_stage(self):
        run = self.run_directory("resume")
        supervisor.atomic_json(run / "run.json", {"owner": "BlockNations.LocalTraining.v1", "seed": 12, "curriculum": False})
        supervisor.atomic_json(run / "arena-status.json", {"curriculumDistance": 6, "failure": "old diagnostic"})
        self.assertEqual(supervisor.arena_options(run, 42, True, True), (12, False, 6))
        self.assertEqual(supervisor.arena_options(run, 42, True, False), (42, True, 2))

    def test_board_resume_preserves_geometry_and_rejects_invalid_size(self):
        run = self.run_directory("small")
        supervisor.atomic_json(run / "run.json", {"boardSize": 5})
        self.assertEqual(supervisor.board_options(run, 11, True), 5)
        self.assertEqual(supervisor.board_options(run, 7, False), 7)
        self.assertEqual(supervisor.board_options(run, 6, False), 6)
        supervisor.atomic_json(run / "run.json", {"boardSize": 6})
        self.assertEqual(supervisor.board_options(run, 5, True), 6)
        supervisor.atomic_json(run / "run.json", {"boardSize": 3})
        with self.assertRaises(ValueError):
            supervisor.board_options(run, 11, True)

    def test_invalid_saved_curriculum_stage_falls_back_to_first_stage(self):
        run = self.run_directory("resume")
        supervisor.atomic_json(run / "arena-status.json", {"curriculumDistance": 999})
        self.assertEqual(supervisor.arena_options(run, 42, True, True), (42, True, 2))


if __name__ == "__main__":
    unittest.main()
