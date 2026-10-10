import json
from pathlib import Path
import tempfile
import unittest

from training_contract import RULES_VERSION
from training_opponents import TrainingOpponents, load_recipe, entry_id


class TrainingOpponentTests(unittest.TestCase):
    def test_roster_references_have_distinct_stable_identities(self):
        base = dict(kind='fair-tactician', workBudget=128)
        self.assertEqual(entry_id(dict(base, workBudget=512)),
                         'tactician-74d246ff24983fb04b1cf7b4')
        self.assertEqual(entry_id(base), entry_id(dict(base, recruitType='')))
        self.assertNotEqual(entry_id(base), entry_id(dict(base, recruitType='warrior')))
        self.assertNotEqual(entry_id(dict(base, recruitType='warrior')),
                            entry_id(dict(base, recruitType='rider')))

    def test_restricted_reference_does_not_restrict_learner(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            recipe = dict(version=1, rulesVersion=RULES_VERSION, kind='fair-tactician',
                          probability=.5, workBudget=128, recruitType='custom_unit')
            for invalid in (None, True, '../warrior', 'x'*65, 'warrior rider'):
                (root/'training-opponents.json').write_text(json.dumps(dict(recipe, recruitType=invalid)))
                with self.assertRaises(ValueError): load_recipe(root)
            (root/'training-opponents.json').write_text(json.dumps(recipe))
            policy = dict(learningSeat=1, learner='learner', opponent='neural')
            stats = [dict(worker=0, match=1, trainerResets=0)]
            seed = next(s for s in range(100) if TrainingOpponents(root,s).selected(stats,policy))
            pool = TrainingOpponents(root,seed)
            class Advice:
                workers = 1
                roster = ('custom_unit', 'other')
                def reference_actions(self, seat, *, work_budget, agents, recruit_type):
                    assert seat == 0 and recruit_type == 'custom_unit'
                    assert work_budget == 128 and agents == [0]
                    return {0:242}
            env = Advice(); env.stats = stats
            original = {0:258, 1:243}
            replaced, assignments = pool.prepare(env, original, policy)
            self.assertEqual(original, {0:258, 1:243})
            self.assertEqual(replaced, {0:242, 1:243})
            self.assertEqual(assignments[0]['opponent'],entry_id(recipe))
            env.roster = ('other',)
            with self.assertRaises(ValueError): pool.prepare(env,original,policy)

    def test_recipes_are_optional_bounded_and_rules_versioned(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.assertIsNone(load_recipe(root))
            recipe = dict(version=1, rulesVersion=RULES_VERSION, kind='fair-tactician', probability=.2, workBudget=128)
            for field, value in [('rulesVersion','old'),('kind','omniscient'),('probability',.6),('probability',True),('workBudget',0)]:
                (root/'training-opponents.json').write_text(json.dumps(dict(recipe, **{field:value})))
                with self.assertRaises(ValueError):
                    load_recipe(root)

    def test_match_assignments_are_stable_and_advice_never_replaces_learner_actions(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root/'training-opponents.json').write_text(json.dumps(dict(version=1, rulesVersion=RULES_VERSION,
                kind='fair-tactician', probability=.5, workBudget=128)))
            policy = dict(learningSeat=0, learner='learner', opponent='neural-opponent')
            stats = [dict(worker=i,match=1,trainerResets=0) for i in range(4)]
            seed = next(seed for seed in range(100) if 0 < len(TrainingOpponents(root,seed).selected(stats,policy)) < 4)
            pool = TrainingOpponents(root, seed)
            selected = pool.selected(stats,policy)
            self.assertTrue(selected)
            self.assertLess(len(selected),4)
            self.assertEqual(pool.selected(stats,policy),selected)
            self.assertEqual(TrainingOpponents(root,seed).selected(stats,policy),selected)

            class Advice:
                workers = 4
                def reference_actions(self, seat, *, work_budget, agents):
                    assert seat == 1 and work_budget == 128
                    assert all(agent%2 == 1 for agent in agents)
                    return {agent:48 for agent in agents}
            env = Advice(); env.stats = stats
            original = {i:258 for i in range(8)}
            replaced, assignments = pool.prepare(env,original,policy)
            self.assertEqual(original,{i:258 for i in range(8)})
            self.assertEqual([replaced[i] for i in range(0,8,2)],[258]*4)
            for worker in range(4):
                self.assertEqual(replaced[worker*2+1],48 if worker in selected else 258)
                self.assertEqual(assignments[worker]['opponent'],pool.identifier if worker in selected else 'neural-opponent')
            self.assertEqual(pool.decisions,len(selected))


if __name__ == '__main__':
    unittest.main()
