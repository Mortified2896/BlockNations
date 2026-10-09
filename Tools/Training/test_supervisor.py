import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

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

    def test_trainer_deletes_checkpoint_between_enumeration_and_stat(self):
        active = self.run_directory("active")
        obsolete, current = active / "Policy-1.pt", active / "Policy-2.pt"
        obsolete.write_bytes(b"expired by ML-Agents")
        current.write_bytes(b"latest weights")
        original = supervisor.files

        def retire_before_stat(root):
            for path in original(root):
                if path == obsolete:
                    obsolete.unlink(missing_ok=True)
                yield path

        with patch.object(supervisor, "files", retire_before_stat):
            self.assertIn(current, supervisor.protected_files(active))
            supervisor.prune(self.root, active, target_bytes=0)
        self.assertTrue(current.exists())
        self.assertFalse(obsolete.exists())

    def test_checkpoint_inventory_does_not_stat_paths_again_while_sorting(self):
        old = self.run_directory("old")
        victim, latest = old / "Policy-1.pt", old / "Policy-2.pt"
        victim.write_bytes(b"expired")
        latest.write_bytes(b"latest")
        original = supervisor.file_snapshot

        def retire_after_snapshot(root):
            snapshot = original(root)
            if root == old:
                victim.unlink(missing_ok=True)
            return snapshot

        with patch.object(supervisor, "file_snapshot", retire_after_snapshot):
            supervisor.prune(self.root, self.run_directory("active"), target_bytes=0)
        self.assertTrue(latest.exists())

    def test_concurrent_log_rotation_and_removal_are_benign(self):
        active = self.run_directory("active")
        log = active / "trainer.log.1"
        log.write_bytes(b"rotated log")
        original = Path.unlink

        def remove_before_unlink(path, *args, **kwargs):
            if path == log and path.exists():
                original(path)
            return original(path, *args, **kwargs)

        with patch.object(Path, "unlink", remove_before_unlink):
            supervisor.prune(self.root, active, target_bytes=0, log_limit=0)
        self.assertFalse(log.exists())

    def test_storage_permission_errors_are_not_hidden_as_missing_files(self):
        run = self.run_directory("active")
        victim = run / "Policy-1.pt"
        victim.write_bytes(b"weights")
        original = Path.stat

        def denied(path, *args, **kwargs):
            if path == victim and kwargs.get("follow_symlinks") is False:
                raise PermissionError("denied storage inventory")
            return original(path, *args, **kwargs)

        with patch.object(Path, "stat", denied), self.assertRaises(PermissionError):
            supervisor.prune(self.root, run, target_bytes=0)

    def test_unexpected_supervisor_failure_saves_child_and_records_failed_status(self):
        run = self.root / "runs" / "failure"

        class Trainer:
            pid = 123456
            returncode = None
            stdout = io.BytesIO()

            def poll(self):
                return self.returncode

            def send_signal(self, signal):
                (run / "final.pt").write_bytes(b"saved on interrupt")
                self.returncode = 0

            def wait(self, timeout=None):
                return self.returncode

        child = Trainer()
        argv = ["supervisor.py", "--root", str(self.root), "--run-id", "failure", "--board-size", "7"]
        with patch.object(supervisor.sys, "argv", argv), patch.object(supervisor.sys, "platform", "linux"), \
                patch.object(supervisor.signal, "signal"), patch.object(supervisor.subprocess, "Popen", return_value=child), \
                patch.object(supervisor, "prune", side_effect=[[], PermissionError("denied storage inventory")]):
            with self.assertRaises(PermissionError):
                supervisor.main()
        status = supervisor.read_json(run / "supervisor-status.json")
        self.assertEqual(status["state"], "failed")
        self.assertEqual(status["stopReason"], "supervisor_failure")
        self.assertIn("PermissionError", status["error"])
        self.assertEqual(status["exitCode"], 1)
        self.assertEqual(status["trainerExitCode"], 0)
        self.assertEqual(status["checkpointCount"], 1)
        self.assertEqual((run / "final.pt").read_bytes(), b"saved on interrupt")
        self.assertFalse((self.root / "supervisor.lock").exists())

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
