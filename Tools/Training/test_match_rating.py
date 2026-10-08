import json
from pathlib import Path
import tempfile
import unittest
from match_rating import MatchRating, MatchJournalReader


class MatchRatingTests(unittest.TestCase):
    def event(self, sequence, kind, match=1, winner=0, interrupted=False):
        return dict(version=1, boardSize=5, session='session', sequence=sequence,
                    kind=kind, match=match, winner=winner, interrupted=interrupted)

    def test_loss_without_losing_agent_decisions_is_rated_and_balanced_pair_plateaus(self):
        rating = MatchRating('run', 5)
        context = dict(learningSeat=0, learner='current', opponent='frozen')
        rating.accept(self.event(1, 'begin'), context)
        rating.accept(self.event(2, 'end', winner=1), current_ids=('current', 'frozen'))
        self.assertEqual(rating.data['losses'], 1)
        self.assertLess(rating.data['elo'], 1200)
        for match in range(2, 1002):
            rating.accept(self.event(2 * match - 1, 'begin', match), context)
            rating.accept(self.event(2 * match, 'end', match, winner=match % 2), current_ids=('current', 'frozen'))
        self.assertEqual(rating.data['matches'], 1001)
        self.assertEqual(rating.data['wins'] + rating.data['losses'], 1001)
        self.assertLess(abs(rating.data['elo'] - 1200), 10)
        self.assertLessEqual(len(rating.data['points']), 200)
        self.assertEqual(rating.data['points'][0]['step'], 0)
        self.assertEqual(rating.data['points'][-1]['step'], 1001)

    def test_identical_frozen_weights_stay_exactly_at_baseline(self):
        rating = MatchRating('run', 5)
        context = dict(learningSeat=1, learner='same', opponent='same')
        for match in range(1, 101):
            rating.accept(self.event(2 * match - 1, 'begin', match), context)
            rating.accept(self.event(2 * match, 'end', match, winner=match % 2), current_ids=('same', 'same'))
        self.assertEqual((rating.data['wins'], rating.data['losses']), (50, 50))
        self.assertEqual(rating.data['elo'], 1200)
        self.assertEqual(rating.data['selfMatches'], 100)

    def test_restore_and_replayed_journal_do_not_duplicate_results(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'match-elo.json'
            rating = MatchRating('run', 5)
            context = dict(learningSeat=0, learner='a', opponent='b')
            begin, end = self.event(1, 'begin'), self.event(2, 'end')
            rating.accept(begin, context); rating.save(path)
            resumed = MatchRating('run', 5); resumed.restore(path)
            self.assertFalse(resumed.accept(begin, context))
            resumed.accept(end, current_ids=('a', 'b')); resumed.save(path)
            self.assertFalse(resumed.accept(end, current_ids=('a', 'b')))
            self.assertEqual(resumed.data['matches'], 1)
            with self.assertRaises(ValueError): MatchRating('run', 6).restore(path)

    def test_limits_and_changed_policies_are_visible_without_fabricating_draws(self):
        rating = MatchRating('run', 5)
        context = dict(learningSeat=0, learner='a', opponent='b')
        rating.accept(self.event(1, 'begin'), context)
        rating.accept(self.event(2, 'end', interrupted=True))
        rating.accept(self.event(3, 'begin', 2), context)
        rating.accept(self.event(4, 'end', 2), current_ids=('new', 'b'))
        self.assertEqual(rating.data['matches'], 2)
        self.assertEqual(rating.data['interruptions'], 1)
        self.assertEqual(rating.data['unrated'], 1)
        self.assertEqual(rating.data['wins'] + rating.data['losses'], 0)
        self.assertEqual(rating.data['elo'], 1200)
        with self.assertRaises(ValueError): rating.accept(self.event(6, 'begin', 3), context)

    def test_journal_handles_partial_writes_and_rotation(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory); path = run / 'match-events.jsonl'
            first = json.dumps(self.event(1, 'begin')).encode()
            path.write_bytes(first[:30]); reader = MatchJournalReader(run)
            self.assertEqual(list(reader.events()), [])
            path.write_bytes(first + b'\n')
            self.assertEqual(len(list(reader.events())), 1)
            path.rename(run / 'match-events.jsonl.1')
            path.write_text(json.dumps(self.event(2, 'end')) + '\n')
            self.assertEqual([event['sequence'] for event in reader.events()], [1, 2])
