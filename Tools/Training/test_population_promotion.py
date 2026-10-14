import copy
import unittest

from population_promotion import assess, scores


def pack(wins, *, repeat=False, actor='old'):
    cases, results = [], []
    for i, (first, second) in enumerate(wins):
        cases.append(dict(name=str(i), seed=200+i+(1000003 if repeat else 0), gamesPerCell=32,
            difficulty='Hard', referenceDifficulty='Hard', reference=dict(kind='frozen', sha256=str(i))))
        results.append(dict(games=128, wins=first+second, interruptions=0, startingPositionResults=[
            dict(candidateStarts=True, games=64, wins=first, interruptions=0),
            dict(candidateStarts=False, games=64, wins=second, interruptions=0)]))
    return dict(suiteId='confirmation' if repeat else 'primary', cases=cases, results=results,
                candidate=dict(sha256=actor), rulesVersion='rules', workerSha256='worker')


class PopulationPromotionTests(unittest.TestCase):
    def compare(self, old, new):
        return assess(pack(old), pack(new, actor='new'), pack(old, repeat=True), pack(new, repeat=True, actor='new'))

    def test_broad_gain_accepts_an_opening_tradeoff_when_the_worst_role_improves(self):
        old=[(60,60),(20,20),(64,64)];new=[(48,48),(50,50),(64,64)]
        result=self.compare(old,new)
        self.assertTrue(result['accepted'])
        self.assertGreater(min(result['meanGains']), .01)
        self.assertGreater(min(result['worstRoleChanges']), 0)

    def test_mean_gain_cannot_hide_a_new_major_worst_role_failure(self):
        result=self.compare([(30,30),(40,40),(50,50)],[(20,20),(64,64),(64,64)])
        self.assertFalse(result['accepted']);self.assertGreater(result['meanGains'][0],0)

    def test_gain_must_repeat_and_ties_do_not_promote(self):
        a=[(40,40)]*3;b=[(50,50)]*3
        self.assertFalse(assess(pack(a),pack(b,actor='new'),pack(a,repeat=True),pack(a,repeat=True,actor='new'))['accepted'])
        self.assertFalse(self.compare(a,a)['accepted'])

    def test_mixed_modes_unbalanced_roles_and_interruptions_fail_closed(self):
        original=pack([(40,40)]*3)
        for change in (lambda p:p['cases'][0].update(difficulty='Policy'),
                       lambda p:p['cases'][0].update(referenceDifficulty='Policy'),
                       lambda p:p['results'][0]['startingPositionResults'][0].update(games=63),
                       lambda p:p['results'][0].update(interruptions=1),
                       lambda p:p['results'][0].update(wins=79)):
            value=copy.deepcopy(original);change(value)
            with self.assertRaises(ValueError):scores(value)

    def test_distinct_seeds_unchanged_sources_and_exact_actor_identity_required(self):
        a=pack([(40,40)]*3);b=pack([(50,50)]*3,actor='new')
        for change in (lambda p:p.update(candidate=dict(sha256='other')),
                       lambda p:p.update(workerSha256='different'),
                       lambda p:p['cases'][0].update(seed=a['cases'][0]['seed'])):
            repeat=pack([(50,50)]*3,repeat=True,actor='new');change(repeat)
            with self.assertRaises(ValueError):assess(a,b,pack([(40,40)]*3,repeat=True),repeat)

    def test_margins_and_minimum_opponent_coverage_are_bounded(self):
        for value in (True,0,-.1,.2,float('nan')):
            with self.assertRaises(ValueError):assess({}, {}, {}, {}, minimum_gain=value)
        with self.assertRaises(ValueError):scores(pack([(40,40)]*2))


if __name__=='__main__':unittest.main()
