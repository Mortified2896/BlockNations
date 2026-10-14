import unittest
from champion_matches import assess

INCUMBENT = dict(sha256='old', configSha256='config', actorId='old-actor')
CANDIDATE = dict(sha256='new', configSha256='config', actorId='new-actor')


def pack(repeat=False, wins=100):
    return dict(candidate=CANDIDATE, rulesVersion='rules', cases=[
        dict(name='champion-'+mode.lower(), seed=42+repeat*1000003, gamesPerCell=32,
             difficulty=mode, referenceDifficulty=mode, reference=dict(kind='frozen', **INCUMBENT))
        for mode in ('Hard', 'Policy')], results=[
            dict(games=128, wins=wins, interruptions=0, startingPositionResults=[
                dict(candidateStarts=starts, games=64, wins=wins//2) for starts in (True, False)])
            for _ in range(2)])


class ChampionMatchTests(unittest.TestCase):
    def test_hard_selection_reports_raw_results_without_requiring_them_to_win(self):
        primary,repeat=pack(),pack(True)
        for value in (primary,repeat):
            value['results'][1]['wins']=64
            for role in value['results'][1]['startingPositionResults']:role['wins']=32
        self.assertFalse(assess(primary,repeat,INCUMBENT,CANDIDATE)['accepted'])
        result=assess(primary,repeat,INCUMBENT,CANDIDATE,required_modes=('Hard',))
        self.assertTrue(result['accepted']);self.assertEqual(result['requiredModes'],['Hard'])
        self.assertEqual(len(result['scores']),4)
        primary['results'][0]['startingPositionResults'][0]['wins']=31
        self.assertFalse(assess(primary,repeat,INCUMBENT,CANDIDATE,required_modes=('Hard',))['accepted'])

    def test_unrecognized_or_raw_only_selection_mode_is_rejected(self):
        for modes in (('Policy',),('Hard','Easy'),[],None,'Hard'):
            with self.assertRaises(ValueError):assess(pack(),pack(True),INCUMBENT,CANDIDATE,required_modes=modes)

    def test_direct_winner_passes_without_reference_score_input(self):
        self.assertTrue(assess(pack(), pack(True), INCUMBENT, CANDIDATE)['accepted'])

    def test_tie_one_pack_gain_one_mode_gain_and_role_failure_reject(self):
        for primary, repeat in ((pack(wins=64), pack(True)), (pack(), pack(True, wins=64))):
            self.assertFalse(assess(primary, repeat, INCUMBENT, CANDIDATE)['accepted'])
        bad = pack(); bad['results'][1]['wins'] = 64
        self.assertFalse(assess(bad, pack(True), INCUMBENT, CANDIDATE)['accepted'])
        bad = pack(); bad['results'][0]['startingPositionResults'][1]['wins'] = 31
        self.assertFalse(assess(bad, pack(True), INCUMBENT, CANDIDATE)['accepted'])

    def test_interruptions_block_and_wrong_actor_or_same_seeds_are_errors(self):
        bad = pack(); bad['results'][0]['interruptions'] = 1
        self.assertFalse(assess(bad, pack(True), INCUMBENT, CANDIDATE)['accepted'])
        with self.assertRaises(ValueError): assess(pack(), pack(), INCUMBENT, CANDIDATE)
        bad = pack(); bad['cases'][0]['reference']['sha256'] = 'unrelated'
        with self.assertRaises(ValueError): assess(bad, pack(True), INCUMBENT, CANDIDATE)
        bad = pack(); bad['candidate'] = INCUMBENT
        with self.assertRaises(ValueError): assess(bad, pack(True), INCUMBENT, CANDIDATE)
        bad = pack(); bad['cases'][0]['referenceDifficulty'] = 'Easy'
        with self.assertRaises(ValueError): assess(bad, pack(True), INCUMBENT, CANDIDATE)
