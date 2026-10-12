import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch
from reference_score import score, publish


class ReferenceScoreTests(unittest.TestCase):
    def fixture(self):
        return dict(boardSize=7, rulesVersion='rules', suiteId='fixed', cases=[
            dict(reference=dict(kind='frozen')), dict(reference=dict(kind='self'))], results=[
                dict(games=8, wins=5, losses=2, interruptions=1, startingPositionResults=[
                    dict(candidateStarts=True, games=4, wins=2), dict(candidateStarts=False, games=4, wins=3)]),
                dict(games=8, wins=4, losses=4, interruptions=0, startingPositionResults=[
                    dict(candidateStarts=True, games=4, wins=0), dict(candidateStarts=False, games=4, wins=4)])])

    def test_self_matches_do_not_inflate_strength_and_limits_count_as_no_win(self):
        value = score(self.fixture(), Path('checkpoint.pt'))
        self.assertEqual((value['wins'], value['games'], value['interruptions']), (5, 8, 1))
        self.assertEqual((value['firstWins'], value['secondWins']), (2, 3))

    def test_different_reference_pack_cannot_publish_comparable_scores(self):
        with tempfile.TemporaryDirectory() as tmp:
            run = Path(tmp)
            old = self.fixture();new = dict(old, suiteId='different')
            with patch('reference_score.read_json', return_value=dict(candidateManifest='m', evaluation='e')), \
                    patch('reference_score.resolve_champion'), \
                    patch('reference_score.checked_evaluation', side_effect=[(Path('old.pt'), old), (Path('new.pt'), new)]):
                with self.assertRaises(ValueError):publish(run, Path('new'), Path('results'))
            self.assertFalse((run/'fixed-reference-status.json').exists())
