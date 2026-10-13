import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

from information_study_runtime import PowerGuard, PowerPause, save_environment, resume_environment, evaluate_or_resume
from power_study_controller import tick


class StudyRuntimeTests(unittest.TestCase):
    def test_power_hysteresis_and_unplug_pause(self):
        guard=PowerGuard(True)
        def reading(source,percent):return f"Now drawing from '{source}'\n battery {percent}%; present: true"
        with patch('information_study_runtime.subprocess.check_output',return_value=reading('AC Power',79)):
            with self.assertRaises(PowerPause):guard.check()
        for source,percent,allowed in [('AC Power',80,True),('AC Power',76,True),('AC Power',75,False),('Battery Power',90,False)]:
            guard.next_check=0
            with patch('information_study_runtime.subprocess.check_output',return_value=reading(source,percent)):
                if allowed:guard.check()
                else:
                    with self.assertRaises(PowerPause):guard.check()

    def test_resume_keeps_public_settings_and_creates_a_new_rating_session(self):
        with tempfile.TemporaryDirectory() as tmp:
            run=Path(tmp)
            environment=dict(BLOCKNATIONS_RATING_RUN=str(run),BLOCKNATIONS_HUMAN_IMITATION='disabled',
                BLOCKNATIONS_RATING_SESSION='old',BLOCKNATIONS_OBSERVATION_VERSION='4',UNRELATED_SECRET='test-only-value')
            save_environment(run,environment)
            saved=json.loads((run/'study-environment.json').read_text())
            self.assertNotIn('UNRELATED_SECRET',saved)
            resumed=resume_environment(run)
            self.assertEqual(resumed['BLOCKNATIONS_OBSERVATION_VERSION'],'4')
            self.assertNotEqual(resumed['BLOCKNATIONS_RATING_SESSION'],'old')

    def test_partial_pack_is_preserved_and_complete_pack_is_reused(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory=Path(tmp)/'pack';directory.mkdir()
            (directory/'suite.json').write_text(json.dumps(dict(state='failed')))
            with patch('evaluate_suite.prepare',return_value=(None,{},None,dict(suiteId='id',cases=[{}]))), \
                    patch('evaluate_suite.run',return_value=dict(state='completed')) as launch:
                evaluate_or_resume('manifest','suite','worker',directory,lambda:None)
                launch.assert_called_once()
            self.assertEqual(len(list(Path(tmp).glob('pack-interrupted-*'))),1)
            directory.mkdir();(directory/'case').mkdir()
            result=dict(case='case',games=4,wins=2)
            (directory/'case/evaluation.json').write_text(json.dumps(dict(games=4,wins=2,records=[{}]*4)))
            receipt=dict(state='completed',suiteId='id',candidate={},cases=[dict(name='case',gamesPerCell=1)],results=[result])
            (directory/'suite.json').write_text(json.dumps(receipt))
            with patch('evaluate_suite.prepare',return_value=(None,{},None,dict(suiteId='id',cases=[{}]))),patch('evaluate_suite.run') as launch:
                self.assertEqual(evaluate_or_resume('manifest','suite','worker',directory,lambda:None),receipt)
                launch.assert_not_called()

    def test_controller_resumes_only_after_power_returns_and_never_duplicates(self):
        with tempfile.TemporaryDirectory() as tmp:
            destination=Path(tmp)/'study';destination.mkdir()
            config=Path(tmp)/'controller.json'
            command=['python','study.py','--destination',str(destination),'--power-controlled']
            config.write_text(json.dumps(dict(owner='BlockNations.PowerStudy.v1',command=command,destination=str(destination),processExecutable='native-python',enabled=True)))
            (destination/'study.json').write_text(json.dumps(dict(state='paused_power',pid=123)))
            with patch('power_study_controller.command_for',return_value=''), \
                    patch('power_study_controller.subprocess.check_output',return_value="Now drawing from 'AC Power'\n battery 79%;"), \
                    patch('power_study_controller.subprocess.Popen') as launch:
                tick(config);launch.assert_not_called()
            with patch('power_study_controller.command_for',return_value=''), \
                    patch('power_study_controller.subprocess.check_output',return_value="Now drawing from 'AC Power'\n battery 80%;"), \
                    patch('power_study_controller.subprocess.Popen',return_value=Mock(pid=456)) as launch:
                tick(config);self.assertEqual(launch.call_args.args[0],command+['--resume'])
            with patch('power_study_controller.command_for',return_value=' '.join(command+['--resume'])),patch('power_study_controller.subprocess.Popen') as launch:
                tick(config);launch.assert_not_called()
