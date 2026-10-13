import copy
import json
from pathlib import Path
import tempfile
import unittest

from tournament_selection import assess, confirmation_suite, review


def pack(wins, repeat=False):
    cases, results = [], []
    for i, count in enumerate(wins):
        cases.append(dict(name=str(i), seed=100+i+(1000003 if repeat else 0), reference=dict(kind='frozen')))
        results.append(dict(games=100, wins=count, interruptions=0, startingPositionResults=[
            dict(candidateStarts=True, games=50, wins=count//2),
            dict(candidateStarts=False, games=50, wins=count-count//2)]))
    return dict(suiteId='repeat' if repeat else 'primary', cases=cases, results=results,
                candidate=dict(sha256='champion'), rulesVersion='rules', workerSha256='worker')


class TournamentSelectionTests(unittest.TestCase):
    def test_broad_replicated_gain_allows_small_cell_regression(self):
        result = assess(pack([70, 70, 70, 70]), pack([69, 75, 75, 75]),
                        pack([70, 70, 70, 70], True), pack([69, 75, 75, 75], True))
        self.assertTrue(result['accepted'])

    def test_one_pack_gain_or_large_opponent_regression_cannot_promote(self):
        old, new = pack([70]*4), pack([75]*4)
        self.assertFalse(assess(old, new, pack([70]*4, True), pack([70]*4, True))['accepted'])
        self.assertFalse(assess(old, pack([60, 90, 90, 90]), pack([70]*4, True), pack([60, 90, 90, 90], True))['accepted'])

    def test_distinct_seeds_and_same_identity_are_required(self):
        with self.assertRaises(ValueError):
            assess(pack([70]*4), pack([75]*4), pack([70]*4), pack([75]*4))
        different = pack([75]*4, True);different['suiteId'] = 'different'
        with self.assertRaises(ValueError):
            assess(pack([70]*4), pack([75]*4), pack([70]*4, True), different)
        source = dict(cases=[dict(seed=2**31-1, name='x')])
        other = confirmation_suite(source)
        self.assertNotEqual(source['cases'][0]['seed'], other['cases'][0]['seed'])
        self.assertEqual(source['cases'][0]['seed'], 2**31-1)

    def test_interruptions_and_role_regressions_block_promotion(self):
        old, new = pack([70]*4), pack([75]*4)
        new['results'][0]['interruptions'] = 1
        self.assertFalse(assess(old, new, pack([70]*4, True), pack([75]*4, True))['accepted'])
        new = pack([75]*4)
        for r in new['results']:
            r['startingPositionResults'][0]['wins'] = 25
            r['startingPositionResults'][1]['wins'] = 50
        self.assertFalse(assess(old, new, pack([70]*4, True), pack([75]*4, True))['accepted'])

    def test_review_requires_three_distinct_regressions_and_latches_for_review(self):
        with tempfile.TemporaryDirectory() as tmp:
            run = Path(tmp)
            old, bad = pack([80]*4), pack([60]*4)
            a, b = pack([80]*4, True), pack([60]*4, True)
            for step in (1, 2):
                self.assertEqual(review(run, old, bad, a, b, step)['state'], 'monitoring')
            self.assertEqual(review(run, old, bad, a, b, 2)['consecutiveRegressions'], 2)
            self.assertEqual(review(run, old, bad, a, b, 3)['state'], 'review_required')
            self.assertEqual(review(run, old, old, a, a, 4)['state'], 'review_required')
            self.assertFalse((run/'stop.request').exists())
