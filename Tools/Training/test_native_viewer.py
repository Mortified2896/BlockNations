from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, patch

from native_viewer import matches_viewer, find_viewer, focus
from decoupled_runtime import ViewerSession


class NativeViewerTests(unittest.TestCase):
    def test_only_the_exact_run_and_viewer_mode_can_be_reused(self):
        run = Path('/Users/Jo/Library/Application Support/BlockNations/Training/runs/test')
        base = f'/Applications/Inspector.app/Contents/MacOS/BlockNations --training-viewer true --viewer-run {run}'
        self.assertTrue(matches_viewer(base, run))
        self.assertTrue(matches_viewer(base+' --training-rules-version example -screen-width 1400', run))
        self.assertTrue(matches_viewer(base.replace(str(run), '"'+str(run)+'"'), run))
        self.assertFalse(matches_viewer(base.replace('true', 'false'), run))
        self.assertFalse(matches_viewer(base.replace(str(run), str(run)+'-other'), run))
        self.assertFalse(matches_viewer(base+' --training-viewer false', run))
        self.assertFalse(matches_viewer('--training-human-seat 0 --training-status /playtest/arena-status.json', run))

    @patch('native_viewer.sys.platform', 'darwin')
    @patch('native_viewer.subprocess.run')
    def test_scan_reads_arguments_only_for_game_players_and_rejects_other_runs(self, command):
        run = Path('/owned run')
        command.side_effect = [Mock(stdout='11 /usr/bin/private-service\n22 /Game.app/Contents/MacOS/BlockNations\n33 /Inspect.app/Contents/MacOS/BlockNations\n'),
                               Mock(returncode=0, stdout='game --training-viewer true --viewer-run /different run'),
                               Mock(returncode=0, stdout='game --training-viewer true --viewer-run /owned run')]
        self.assertEqual(find_viewer(run), 33)
        commands = [call.args[0] for call in command.call_args_list]
        self.assertFalse(any('11' in args for args in commands))
        self.assertEqual(commands[-1], ['/bin/ps', '-ww', '-p', '33', '-o', 'args='])

    @patch('decoupled_runtime.find_viewer', return_value=123)
    @patch('decoupled_runtime.focus', return_value=True)
    @patch('decoupled_runtime.alive', return_value=True)
    @patch('decoupled_runtime.subprocess.Popen')
    def test_return_reuses_external_inspector_and_never_takes_shutdown_ownership(self, launch, alive, focus, find):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            (run/'viewer.request').write_text('Return from human playtest.\n')
            (run/'viewer-live.request').touch()
            session = ViewerSession(run, Path('/Configured.app'), True)
            session.initial_launch = False
            state = dict(state='running', trainerPid=456)
            session.poll(dict(trainerConnected=True), state)
            launch.assert_not_called()
            focus.assert_called_once_with(123)
            self.assertEqual(state['viewerPid'], 123)
            self.assertFalse(state['viewerOwned'])
            self.assertTrue((run/'viewer-live.request').exists())
            self.assertFalse((run/'viewer.request').exists())
            self.assertEqual(state['trainerPid'], 456)
            self.assertFalse((run/'stop.request').exists())
            self.assertFalse((run/'training-control.json').exists())
            session.close()
            self.assertIsNone(session.process)
            launch.assert_not_called()

    @patch('decoupled_runtime.find_viewer', return_value=123)
    @patch('decoupled_runtime.focus', return_value=False)
    @patch('decoupled_runtime.subprocess.Popen')
    def test_focus_failure_cannot_launch_a_duplicate_or_stop_training(self, launch, focus, find):
        session = ViewerSession(Path('/owned'), Path('/Configured.app'), True)
        session.launch(activate=True)
        self.assertEqual(session.external_pid, 123)
        self.assertIn('existing inspector', session.error)
        launch.assert_not_called()

    @patch('decoupled_runtime.find_viewer', side_effect=subprocess.TimeoutExpired('ps', 5))
    @patch('decoupled_runtime.subprocess.Popen')
    def test_discovery_failure_is_reported_without_a_speculative_duplicate(self, launch, find):
        session = ViewerSession(Path('/owned'), Path('/Configured.app'), True)
        session.launch()
        self.assertTrue(session.error)
        launch.assert_not_called()

    @patch('native_viewer.sys.platform', 'darwin')
    @patch('native_viewer.subprocess.run', return_value=Mock(returncode=0, stdout='true\n'))
    def test_focus_targets_one_numeric_process_without_opening_an_application(self, command):
        self.assertTrue(focus(123))
        self.assertEqual(command.call_args.args[0][:3], ['/usr/bin/osascript', '-l', 'JavaScript'])
        self.assertIn('ProcessIdentifier(123)', command.call_args.args[0][-1])
        with self.assertRaises(ValueError):
            focus('123; bad')


if __name__ == '__main__':
    unittest.main()
