"""Authoritative whole-match Elo. No agent trajectory is required for a loss."""
import hashlib
import json
import math
from pathlib import Path
import time
from elo_curve import MAXIMUM_POINTS, append_point, restore_curve


def policy_id(weights):
    digest = hashlib.sha256()
    values = weights.items() if hasattr(weights, "items") else enumerate(weights)
    for key, array in values:
        digest.update(str(key).encode())
        if hasattr(array, "detach"):
            array = array.detach().cpu().numpy()
        digest.update(str(array.shape).encode())
        digest.update(str(array.dtype).encode())
        digest.update(array.tobytes())
    return digest.hexdigest()[:24]


class MatchRating:
    maximum_points = MAXIMUM_POINTS
    initial = 1200.0
    k = 16.0

    def __init__(self, run_id, board_size):
        self.data = dict(version=2, source='authoritative-match-results-v1', schema=2,
                         runId=run_id, boardSize=board_size, behavior='BlockNationsSeatV2',
                         startedUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
                         matches=0, wins=0, losses=0, interruptions=0, unrated=0,
                         selfMatches=0, abandoned=0, elo=self.initial,
                         curveVersion=1, curveStart=0, curveBucketWidth=1,
                         points=[dict(step=0, elo=self.initial)], ratings={}, sequences={}, pending=None)

    def restore(self, path):
        try:
            data = json.loads(path.read_text())
        except FileNotFoundError:
            return
        for key in ('version', 'source', 'schema', 'runId', 'boardSize', 'behavior'):
            if data.get(key) != self.data[key]:
                raise ValueError('Corrected rating belongs to another run, board, or protocol.')
        if (not math.isfinite(data['elo']) or not 1 <= len(data['points']) <= self.maximum_points or
                data['wins'] + data['losses'] + data['interruptions'] + data['unrated'] != data['matches']):
            raise ValueError('Invalid corrected rating history; preserve it before starting a new history.')
        self.data = data
        restore_curve(self.data)

    def register(self, identifier, inherited=None):
        ratings = self.data['ratings']
        if identifier not in ratings:
            ratings[identifier] = self.data['elo'] if inherited is None else inherited
        return ratings[identifier]

    def accept(self, event, context=None, current_ids=None):
        if event.get('version') != 1 or event.get('boardSize') != self.data['boardSize']:
            raise ValueError('Match result protocol/board mismatch.')
        session, sequence = event['session'], event['sequence']
        if sequence <= self.data['sequences'].get(session, 0):
            return False
        if sequence != self.data['sequences'].get(session, 0) + 1:
            raise ValueError('Match journal sequence gap; rating must not silently omit results.')
        self.data['sequences'][session] = sequence
        if len(self.data['sequences']) > 128:
            del self.data['sequences'][next(iter(self.data['sequences']))]
        kind = event['kind']
        if kind == 'begin':
            if self.data['pending'] is not None:
                self.data['abandoned'] += 1  # SDK reset discarded an unfinished episode.
            self.data['pending'] = dict(session=session, match=event['match'], context=context)
            if context:
                self.register(context['learner']); self.register(context['opponent'])
            return True
        pending = self.data['pending']
        if pending is None or pending['session'] != session or pending['match'] != event['match']:
            raise ValueError('Terminal result has no matching opening event.')
        self.data['pending'] = None
        self.data['matches'] += 1
        if event['interrupted']:
            self.data['interruptions'] += 1
            return True
        winner = event['winner']
        if winner not in (0, 1):
            raise ValueError('Capture result has no valid winning seat.')
        saved = pending['context']
        if saved is None or (current_ids is not None and
                (saved['learner'], saved['opponent']) != current_ids):
            self.data['unrated'] += 1
            return True
        result = 1.0 if winner == saved['learningSeat'] else 0.0
        self.data['wins' if result else 'losses'] += 1
        learner, opponent = saved['learner'], saved['opponent']
        ratings = self.data['ratings']
        if learner == opponent:
            self.data['selfMatches'] += 1  # Identical weights are one rated entity.
        else:
            expectation = 1.0 / (1.0 + 10 ** ((ratings[opponent] - ratings[learner]) / 400))
            change = self.k * (result - expectation)
            ratings[learner] += change; ratings[opponent] -= change
        self.data['elo'] = ratings[learner]
        rated = self.data['wins'] + self.data['losses']
        append_point(self.data, rated, self.data['elo'])
        return True

    def retain(self, identifiers):
        keep = set(identifiers)
        if self.data['pending'] and self.data['pending']['context']:
            context = self.data['pending']['context']
            keep.update((context['learner'], context['opponent']))
        self.data['ratings'] = {key: value for key, value in self.data['ratings'].items() if key in keep}

    def save(self, path):
        temporary = path.with_suffix('.tmp')
        temporary.write_text(json.dumps(self.data, indent=2) + '\n')
        temporary.replace(path)


class MatchJournalReader:
    def __init__(self, run):
        self.run = run
        self.cursors = {}

    def events(self):
        for name in ('match-events.jsonl.1', 'match-events.jsonl'):
            path = self.run / name
            try:
                stat = path.stat()
                inode, offset = self.cursors.get(name, (None, 0))
                if inode != stat.st_ino or offset > stat.st_size:
                    offset = 0
                with path.open('rb') as stream:
                    stream.seek(offset)
                    while True:
                        line = stream.readline(8193)
                        if not line or not line.endswith(b'\n'):
                            break
                        if len(line) > 8192:
                            raise ValueError('Oversized match journal event.')
                        yield json.loads(line)
                        offset = stream.tell()
                self.cursors[name] = (stat.st_ino, offset)
            except FileNotFoundError:
                continue
