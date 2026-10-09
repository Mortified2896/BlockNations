import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock

from playtest import BEHAVIOR, OWNER, PlaytestSession, frozen_config, latest_checkpoint
from supervisor import trainer_config
from interactive_environment import waiting_for_human


class PlaytestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.run = Path(self.temporary.name) / "saved-run"
        self.run.mkdir()
        self.write("run.json", {"owner": "BlockNations.LocalTraining.v1", "schema": 2,
                               "boardSize": 7, "seed": 42, "observationSize": 3120, "actionCount": 259})
        self.write("trainer.yaml", trainer_config(100000, 5000))
        self.models = self.run / "checkpoints" / self.run.name / BEHAVIOR
        self.models.mkdir(parents=True)

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, path, value):
        (self.run / path).write_text(json.dumps(value))

    def checkpoint(self, step, completed=True):
        path = self.models / f"{BEHAVIOR}-{step}.pt"
        path.write_bytes(b"immutable weights " + str(step).encode())
        if completed:
            export = path.with_suffix(".onnx")
            export.write_bytes(b"completed model")
            os.utime(export, (1, 1))
        return path

    def test_selects_latest_completed_numbered_checkpoint_not_mutable_or_partial(self):
        old, latest = self.checkpoint(5), self.checkpoint(10)
        self.checkpoint(15, completed=False)
        (self.models / "checkpoint.pt").write_bytes(b"mutable")
        self.assertEqual(latest_checkpoint(self.run), latest)
        latest.with_suffix(".onnx").write_bytes(b"unfinished export")
        self.assertEqual(latest_checkpoint(self.run), old)

    def test_rejects_incompatible_runs_and_linked_checkpoint_directories(self):
        self.checkpoint(5)
        manifest = json.loads((self.run / "run.json").read_text())
        manifest["schema"] = 1
        self.write("run.json", manifest)
        with self.assertRaises(ValueError):
            latest_checkpoint(self.run)
        manifest["schema"] = 2
        self.write("run.json", manifest)
        target = self.models.with_name("foreign")
        self.models.rename(target)
        self.models.symlink_to(target, target_is_directory=True)
        with self.assertRaises(ValueError):
            latest_checkpoint(self.run)

    def test_inference_config_has_no_self_play_and_preserves_training_config(self):
        original = trainer_config(100000, 5000)
        frozen = frozen_config(original, Path("/frozen.pt"))
        self.assertNotIn("self_play", frozen["behaviors"][BEHAVIOR])
        self.assertEqual(frozen["behaviors"][BEHAVIOR]["init_path"], "/frozen.pt")
        self.assertIn("self_play", original["behaviors"][BEHAVIOR])
        self.assertNotIn("init_path", original["behaviors"][BEHAVIOR])

    @patch("playtest.subprocess.Popen")
    def test_one_frozen_inference_session_with_separate_outputs_and_return(self, launch):
        source = self.checkpoint(10)
        original = source.read_bytes()
        process = Mock()
        process.poll.return_value = None
        process.returncode = None
        launch.return_value = process
        session = PlaytestSession(self.run, Path("/Training.app"))
        request = {"requestId": "a" * 32}
        self.write("playtest.request.json", request)
        session.poll({"paused": False})
        scratch = self.run / "playtest"
        self.assertEqual((scratch / "frozen.pt").read_bytes(), original)
        receipt = json.loads((scratch / "session.json").read_text())
        self.assertEqual(receipt["sha256"], hashlib.sha256(original).hexdigest())
        command = launch.call_args.args[0]
        self.assertIn("--inference", command)
        self.assertNotIn("--resume", command)
        self.assertEqual(command[command.index("--training-board-size") + 1], "7")
        self.assertEqual(command[command.index("--training-human-seat") + 1], "0")
        self.assertFalse(any("BLOCKNATIONS_RATING_" in key for key in launch.call_args.kwargs["env"]))
        (scratch / "arena-status.json").write_text(json.dumps({"trainerConnected": True, "round": 1}))
        session.poll({"paused": False})
        self.assertEqual(session.state["state"], "playing")
        self.assertIn("training is continuing", session.state["message"])
        self.write("playtest.request.json", {"requestId": "b" * 32})
        session.poll({"paused": True})
        launch.assert_called_once()
        process.poll.return_value = 0
        process.returncode = 0
        session.poll({"paused": True})
        self.assertEqual(session.state["state"], "finished")
        self.assertEqual(source.read_bytes(), original)
        self.assertFalse((scratch / "frozen.pt").exists())
        self.assertFalse((self.run / "match-elo.json").exists())

    def test_missing_player_and_foreign_scratch_are_preserved(self):
        self.checkpoint(5)
        session = PlaytestSession(self.run, None)
        session.start({"requestId": "a" * 32}, {"paused": False})
        self.assertEqual(session.state["state"], "error")
        session.environment = Path("/Training.app")
        scratch = self.run / "playtest"
        scratch.mkdir()
        keep = scratch / "important.txt"
        keep.write_text("keep")
        session.start({"requestId": "b" * 32}, {"paused": True})
        self.assertEqual(session.state["state"], "error")
        self.assertEqual(keep.read_text(), "keep")

    @patch("playtest.subprocess.Popen")
    def test_offline_difficulty_uses_one_fixed_model_and_does_not_resume_learning(self, launch):
        source = self.checkpoint(10)
        process = Mock()
        process.poll.return_value = None
        launch.return_value = process
        session = PlaytestSession(self.run, Path('/Training.app'), frozen_checkpoint=source, training_active=False)
        session.start({'requestId': 'd'*32, 'difficulty': 'Hard'}, {})
        arguments = launch.call_args.args[0]
        self.assertIn('--inference', arguments)
        self.assertNotIn('--resume', arguments)
        self.assertEqual(launch.call_args.kwargs['env']['BLOCKNATIONS_PLAYTEST_DIFFICULTY'], 'Hard')
        self.assertIn(source.stem + ' / Hard', arguments)
        self.write('playtest/arena-status.json', {'trainerConnected':True, 'round':1})
        session.poll({})
        self.assertEqual(session.state['difficulty'], 'Hard')
        self.assertIn('training is stopped', session.state['message'])
        self.assertTrue(source.exists())
        self.assertNotIn('self_play', json.loads((self.run/'playtest/inference.yaml').read_text())['behaviors'][BEHAVIOR])

    @patch("playtest.subprocess.Popen")
    def test_invalid_difficulty_never_launches_a_process(self, launch):
        self.checkpoint(10)
        session = PlaytestSession(self.run, Path('/Training.app'))
        session.start({'requestId':'e'*32, 'difficulty':'Maximum'}, {})
        launch.assert_not_called()
        self.assertEqual(session.state['state'], 'error')

    @patch("playtest.subprocess.Popen")
    def test_supervisor_shutdown_asks_its_native_human_window_to_close(self, launch):
        source = self.checkpoint(10)
        process = Mock()
        process.poll.return_value = None
        launch.return_value = process
        session = PlaytestSession(self.run, Path("/Training.app"))
        session.start({"requestId": "c" * 32}, {"paused": False})
        session.close()
        self.assertTrue((self.run / "playtest/close.request").exists())
        self.assertFalse((self.run / "playtest/frozen.pt").exists())
        self.assertTrue(source.exists())
        process.send_signal.assert_called_once()

    def test_interactive_wait_requires_a_fresh_connected_player_not_a_stale_or_failed_one(self):
        path = self.run / "arena-status.json"
        for state, expected in [({"paused": True}, False),
                                ({"trainerConnected": True, "paused": True}, True),
                                ({"trainerConnected": True, "humanPlaytest": True}, True),
                                ({"trainerConnected": True, "humanPlaytest": True, "failure": "bad input"}, False),
                                ({"trainerConnected": True, "paused": False}, False)]:
            path.write_text(json.dumps(state))
            self.assertEqual(waiting_for_human(path), expected)
        path.write_text(json.dumps({"trainerConnected": True, "paused": True}))
        os.utime(path, (1, 1))
        self.assertFalse(waiting_for_human(path))


if __name__ == "__main__":
    unittest.main()
