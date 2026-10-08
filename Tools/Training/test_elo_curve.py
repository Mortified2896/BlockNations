import copy
import json
import unittest

from elo_curve import append_point, restore_curve


class EloCurveTests(unittest.TestCase):
    def curve(self):
        return dict(wins=0, losses=0, curveVersion=1, curveStart=0, curveBucketWidth=1,
                    points=[dict(step=0, elo=1200)])

    def record(self, data, step, elo):
        data['wins'] = step
        append_point(data, step, elo)

    def test_long_run_retains_early_peaks_troughs_and_latest_result_with_fixed_storage(self):
        data = self.curve()
        for step in range(1, 50001):
            elo = 1500 if step == 123 else 900 if step == 124 else 1200 + step % 31
            self.record(data, step, elo)
        points = data['points']
        self.assertLessEqual(len(points), 200)
        self.assertEqual(points[0], dict(step=0, elo=1200))
        self.assertEqual(points[-1]['step'], 50000)
        self.assertIn(dict(step=123, elo=1500), points)
        self.assertIn(dict(step=124, elo=900), points)
        for beginning in range(0, 50000, 10000):
            self.assertTrue(any(beginning <= point['step'] < beginning + 10000 for point in points))
        self.assertEqual(data['curveStart'], 0)

    def test_resume_retains_the_same_whole_run_summary(self):
        uninterrupted, resumed = self.curve(), self.curve()
        for step in range(1, 6001):
            elo = 1100 + (step // 20) % 200
            self.record(uninterrupted, step, elo)
            self.record(resumed, step, elo)
            if step == 3000:
                resumed = json.loads(json.dumps(resumed))
                restore_curve(resumed)
        self.assertEqual(uninterrupted, resumed)

    def test_legacy_tail_is_marked_unavailable_and_never_invented_on_resume(self):
        data = dict(wins=4337, losses=0, points=[dict(step=0, elo=1200)] +
                    [dict(step=step, elo=1119) for step in range(4139, 4338)])
        restore_curve(data)
        self.assertEqual(data['curveStart'], 4139)
        for step in range(4338, 20001):
            self.record(data, step, 1119 + step % 20)
        self.assertEqual(data['points'][1]['step'], 4139)
        self.assertFalse(any(0 < point['step'] < 4139 for point in data['points']))
        self.assertEqual(data['points'][-1]['step'], 20000)
        self.assertLessEqual(len(data['points']), 200)

    def test_invalid_samples_or_coverage_are_rejected(self):
        data = self.curve()
        self.record(data, 1, 1200)
        for field, value in [('curveVersion', 2), ('curveStart', 2), ('curveBucketWidth', 3)]:
            broken = copy.deepcopy(data)
            broken[field] = value
            with self.assertRaises(ValueError):
                restore_curve(broken)
        broken = copy.deepcopy(data)
        broken['points'][1]['elo'] = float('nan')
        with self.assertRaises(ValueError):
            restore_curve(broken)
