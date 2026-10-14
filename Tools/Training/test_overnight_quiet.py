import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock

from overnight_quiet import AudioOutput, tick
from supervisor import ROOT_MARKER


class QuietHostTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name).resolve()
        (self.root/ROOT_MARKER).write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1')))
        self.config = self.root/'overnight-quiet.json'
        self.value = dict(owner='BlockNations.QuietHost.v1', root=str(self.root), enabled=True, expiresUnix=10000)
        self.save()

    def tearDown(self): self.temp.cleanup()

    def save(self): self.config.write_text(json.dumps(self.value))

    def test_route_change_mutes_the_new_output_and_never_unmutes(self):
        audio = object.__new__(AudioOutput)
        audio.default = Mock(side_effect=[7,8,8,8]); audio.muted = Mock(side_effect=[False,False,True]); audio.mute = Mock()
        result = audio.quiet()
        self.assertTrue(result['muted']); self.assertEqual(result['deviceId'],8)
        self.assertEqual([call.args for call in audio.mute.call_args_list],[(7,),(8,)])

    def test_unacknowledged_mute_is_a_fault_not_a_silence_claim(self):
        audio = object.__new__(AudioOutput)
        audio.default = Mock(return_value=7); audio.muted = Mock(return_value=False); audio.mute = Mock()
        with self.assertRaises(RuntimeError): audio.quiet()

    def test_deadline_and_disable_never_apply_or_restore_audio(self):
        audio=Mock(); keyboard=Mock()
        state=tick(self.config,now=10000,audio_factory=audio,keyboard_factory=keyboard)
        self.assertEqual(state['state'],'expired');audio.assert_not_called();keyboard.assert_not_called()
        self.value['enabled']=False;self.save()
        self.assertEqual(tick(self.config,now=9000,audio_factory=audio,keyboard_factory=keyboard)['state'],'disabled')
        audio.assert_not_called();keyboard.assert_not_called()

    def test_repairs_are_verified_and_keyboard_checks_survive_audio_failure(self):
        audio=Mock();audio.return_value.quiet.return_value=dict(deviceId=7,muted=True,repaired=True)
        keyboard=Mock();keyboard.return_value.quiet.return_value=[dict(brightness=0,actualBacklight=0,idleDimSeconds=30)]
        state=tick(self.config,now=9000,audio_factory=audio,keyboard_factory=keyboard)
        self.assertEqual(state['state'],'quiet');self.assertEqual(state['audioRepairs'],1)
        audio.return_value.quiet.side_effect=RuntimeError('route unavailable')
        state=tick(self.config,now=9015,audio_factory=audio,keyboard_factory=keyboard)
        self.assertEqual(state['state'],'fault');self.assertEqual(keyboard.return_value.quiet.call_count,2)
        self.assertEqual(state['errors'],['route unavailable'])

    def test_unbounded_or_unowned_configuration_has_no_native_effects(self):
        for change in [dict(expiresUnix=100000),dict(expiresUnix=float('nan')),dict(owner='other')]:
            self.value.update(change);self.save();audio=Mock();keyboard=Mock()
            with self.assertRaises(ValueError):tick(self.config,now=9000,audio_factory=audio,keyboard_factory=keyboard)
            audio.assert_not_called();keyboard.assert_not_called()


if __name__=='__main__':unittest.main()
