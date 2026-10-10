import json
import os
from pathlib import Path
import tempfile
import threading
import time
import unittest
import uuid

import numpy as np
from mlagents_envs.base_env import ActionTuple
from dotnet_environment import DotNetEnvironment, NAMES, ACTIONS, OBSERVATIONS

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER', Path(__file__).resolve().parents[2] / 'Build/TrainingWorker/BlockNations.TrainingWorker.dll'))


@unittest.skipUnless(WORKER.is_file(), 'Build the actual shared C# worker first.')
class DotNetEnvironmentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.run = Path(self.temporary.name)
        self.env = DotNetEnvironment(WORKER, self.run, 4, 42, 5, False, 2, uuid.uuid4().hex,
                                    lambda: dict(learningSeat=0, learner='a', opponent='b'))
        self.env.reset()

    def tearDown(self):
        self.env.close()
        self.temporary.cleanup()

    def test_worker_obeys_the_recorded_opening_economy_for_both_starting_seats(self):
        for legacy in (False, True):
            with tempfile.TemporaryDirectory() as directory:
                env = DotNetEnvironment(WORKER, directory, 4, 42, 7, False, 2, uuid.uuid4().hex,
                                        opening_economy_version=1 if legacy else 2)
                try:
                    starts = set()
                    for _ in range(4):
                        env.reset()
                        for stat in env.stats:
                            first=stat['seat']; starts.add(first)
                            self.assertEqual(stat['gold'+str(first)], 3 if legacy else 2)
                            self.assertEqual(stat['gold'+str(1-first)], 2)
                    self.assertEqual(starts, {0, 1})
                    for name in NAMES:
                        decisions, _ = env.get_steps(name)
                        env.set_actions(name, ActionTuple(discrete=np.full((len(decisions), 1), ACTIONS-1, dtype=np.int32)))
                    env.step()
                    for stat in env.stats:
                        self.assertEqual(stat['gold'+str(stat['seat'])], 3)
                finally:
                    env.close()

    def act(self, choices):
        for name in NAMES:
            decisions, _ = self.env.get_steps(name)
            values = np.array([[choices.get(int(agent), ACTIONS - 1)] for agent in decisions.agent_id], dtype=np.int32).reshape((-1, 1))
            self.env.set_actions(name, ActionTuple(discrete=values))
        self.env.step()

    def test_reference_advice_is_read_only_legal_and_evaluation_starters_are_balanced(self):
        for first in (0, 1):
            with tempfile.TemporaryDirectory() as directory:
                env = DotNetEnvironment(WORKER, directory, 4, 42, 7, False, 2, uuid.uuid4().hex, first_seat=first)
                try:
                    env.reset()
                    before = env.get_steps(NAMES[first])[0].obs[0].copy()
                    advice = env.reference_actions(first, 'warrior', 32)
                    self.assertEqual(len(advice), 4)
                    self.assertTrue(all(s['seat'] == first for s in env.stats))
                    np.testing.assert_array_equal(env.get_steps(NAMES[first])[0].obs[0], before)
                    self.assertTrue(all(s['decisions'] == 0 for s in env.stats))
                    for agent, choice in advice.items():
                        env.set_action_for_agent(NAMES[first], agent, ActionTuple(discrete=np.array([[choice]],dtype=np.int32)))
                    env.step()
                    self.assertTrue(all(s['decisions'] == 1 for s in env.stats))
                finally:
                    env.close()

    def test_relative_run_path_keeps_worker_artifacts_in_the_requested_directory(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as directory:
            relative = Path(directory).relative_to(Path.cwd())
            env = DotNetEnvironment(WORKER, relative, 1, 42, 7, False, 2, uuid.uuid4().hex)
            try:
                env.reset()
                for name in NAMES:
                    decisions, _ = env.get_steps(name)
                    env.set_actions(name, ActionTuple(discrete=np.full((len(decisions), 1), ACTIONS-1, dtype=np.int32)))
                env.step()
                self.assertTrue((relative/'match-events.jsonl').is_file())
                self.assertFalse((relative/relative).exists())
            finally:
                env.close()

    def test_fixed_training_challenger_only_drives_the_nonlearning_seat(self):
        from training_contract import RULES_VERSION
        with tempfile.TemporaryDirectory() as directory:
            run=Path(directory)
            (run/'training-opponents.json').write_text(json.dumps(dict(version=1,rulesVersion=RULES_VERSION,
                kind='fair-tactician',probability=.5,workBudget=32)))
            policy=dict(learningSeat=1,learner='learner',opponent='neural-opponent')
            env=DotNetEnvironment(WORKER,run,4,42,7,False,2,uuid.uuid4().hex,lambda:policy,first_seat=0)
            try:
                env.reset()
                selected=env.opponents.selected(env.stats,policy)
                self.assertTrue(selected)
                env.set_actions(NAMES[0],ActionTuple(discrete=np.full((4,1),258,dtype=np.int32)))
                env.step()
                self.assertEqual(env.opponents.decisions,len(selected))
                for stat in env.stats:
                    self.assertEqual(stat['seat'],0 if stat['worker'] in selected else 1)
                # Blue now becomes the learner. Its supplied EndTurn must be
                # applied unchanged, regardless of the previous assignment.
                policy['learningSeat']=0
                before=env.opponents.decisions
                decisions,_=env.get_steps(NAMES[0])
                env.set_actions(NAMES[0],ActionTuple(discrete=np.full((len(decisions),1),258,dtype=np.int32)))
                red,_=env.get_steps(NAMES[1])
                env.set_actions(NAMES[1],ActionTuple(discrete=np.full((len(red),1),258,dtype=np.int32)))
                env.step()
                self.assertTrue(all(env.stats[worker]['seat']==1 for worker in selected))
                self.assertEqual(env.opponents.decisions-before,sum(int(agent)//2 not in selected and
                    int(agent)//2 in env.opponents.selected(env.stats,policy) for agent in red.agent_id))
                events=[json.loads(line) for line in (run/'match-events.jsonl').read_text().splitlines()]
                self.assertTrue(any(e['context']['opponent'].startswith('tactician-') for e in events))
            finally:
                env.close()

    def await_json(self, path):
        deadline=time.monotonic()+3
        while time.monotonic()<deadline:
            if path.exists(): return json.loads(path.read_text())
            time.sleep(.02)
        self.fail('Optional snapshot was not published: '+str(path))

    def test_one_turn_capture_reports_both_rewards_including_zero_decision_loser(self):
        # 5x5 canonical source (city inset 1), Rider recruit slot 1, enemy city.
        for choice in (48, 243, 48, 72):
            active = {int(agent): choice for name in NAMES for agent in self.env.get_steps(name)[0].agent_id}
            self.act(active)
        rewards = {}
        for name in NAMES:
            decisions, terminal = self.env.get_steps(name)
            self.assertEqual(len(decisions), 0)
            for i, agent in enumerate(terminal.agent_id):
                rewards[int(agent)] = terminal.reward[i]
                self.assertFalse(terminal.interrupted[i])
                self.assertEqual(terminal.obs[0].shape[1], OBSERVATIONS)
        self.assertEqual(len(rewards), 8)
        for worker in range(4):
            self.assertEqual(rewards[worker*2] + rewards[worker*2+1], 0)
            self.assertEqual(abs(rewards[worker*2]), 1)
        events = [json.loads(line) for line in (self.run/'match-events.jsonl').read_text().splitlines()]
        ends = [event for event in events if event['kind']=='end']
        self.assertEqual(len(ends), 4)
        self.assertTrue(all(min(e['blueDecisions'],e['redDecisions']) == 0 for e in ends))
        self.assertEqual([event['sequence'] for event in events], list(range(1,9)))
        self.act({})  # New episodes start after the terminal exchange.
        self.assertEqual(sum(len(self.env.get_steps(name)[0]) for name in NAMES), 4)
        for folder in (self.run/'workers').iterdir():
            trace=self.await_json(folder/'replays/recent-1.json')
            self.assertEqual(trace['policy']['learner'], 'a')
            self.assertTrue(trace['frames'][-1]['gameOver'])

    def test_round_limits_bootstrap_instead_of_awarding_draw_rewards(self):
        for _ in range(200):
            self.act({})  # EndTurn is the only action, no implicit frame ticking.
        for name in NAMES:
            decisions, terminal = self.env.get_steps(name)
            self.assertEqual(len(decisions), 0)
            self.assertEqual(len(terminal), 4)
            self.assertTrue(terminal.interrupted.all())
            self.assertFalse(terminal.reward.any())
            self.assertTrue(np.isfinite(terminal.obs[0]).all())

    def test_masks_and_agent_ownership_fail_before_mutating_a_game(self):
        name=next(name for name in NAMES if len(self.env.get_steps(name)[0]))
        decisions,_=self.env.get_steps(name)
        agent=int(decisions.agent_id[0])
        invalid=int(np.flatnonzero(decisions.action_mask[0][0])[0])
        with self.assertRaises(ValueError):
            self.env.set_action_for_agent(name,agent,ActionTuple(discrete=np.array([[invalid]],dtype=np.int32)))
        with self.assertRaises(ValueError):
            self.env.set_action_for_agent(name,agent ^ 1,ActionTuple(discrete=np.array([[258]],dtype=np.int32)))
        self.assertTrue(all(worker['decisions']==0 for worker in self.env.stats))

    def test_pause_holds_all_games_and_resume_does_not_require_a_viewer(self):
        (self.run/'training-control.json').write_text('{"paused":true}')
        errors=[]
        def step():
            try:self.act({})
            except Exception as error:errors.append(error)
        thread=threading.Thread(target=step); thread.start()
        deadline=time.monotonic()+3
        while time.monotonic()<deadline:
            if (self.run/'arena-status.json').exists() and json.loads((self.run/'arena-status.json').read_text()).get('paused'):break
            time.sleep(.05)
        self.assertTrue(json.loads((self.run/'arena-status.json').read_text())['paused'])
        self.assertTrue(thread.is_alive())
        self.assertTrue(all(worker['decisions']==0 for worker in self.env.stats))
        trace=self.await_json(self.run/'workers/3/live.json')
        self.assertEqual(trace['worker'],3)
        self.assertTrue(all(worker['decisions']==0 for worker in self.env.stats), 'Inspecting paused state must not create decisions.')
        self.assertEqual(sum(len(self.env.get_steps(name)[0]) for name in NAMES),4)
        (self.run/'training-control.json').write_text('{"paused":false}')
        thread.join(3)
        self.assertFalse(thread.is_alive()); self.assertEqual(errors,[])
        self.assertTrue(all(worker['decisions']==1 for worker in self.env.stats))
        pid=self.env.process.pid
        self.env.close()
        self.assertIsNotNone(self.env.process.poll())
        self.assertFalse(json.loads((self.run/'arena-status.json').read_text())['trainerConnected'])

    def test_all_workers_publish_without_a_viewer_and_display_requests_are_ignored(self):
        self.assertFalse((self.run/'viewer-watch.json').exists())
        for worker in range(4):
            trace=self.await_json(self.run/'workers'/str(worker)/'live.json')
            self.assertEqual(trace['worker'],worker)
            self.assertEqual(trace['match'],1)
            self.assertEqual(len(trace['frames']),2)
        # Even corrupt obsolete viewer configuration cannot affect the environment.
        (self.run/'viewer-watch.json').write_text('not JSON; no consumer acknowledgement')
        self.act({})
        self.assertTrue(all(worker['decisions']==1 for worker in self.env.stats))
        self.assertTrue(all(worker['rejections']==0 for worker in self.env.stats))
