"""Cumulative active trainer time, independent of each launch's stop budget."""
import json
import math
import re


class TrainingTime:
    pattern = re.compile(r'^\[INFO\] BlockNationsSeatV2\. Step: \d+\. Time Elapsed: ([\d.]+) s\.')

    def __init__(self, run_id, board_size, seconds=0, estimated=False):
        self.data = dict(version=1, runId=run_id, boardSize=board_size,
                         totalSeconds=seconds, estimated=estimated)
        self.last_time = None
        self.was_active = False

    @classmethod
    def load(cls, run, run_id, board_size):
        try:
            data = json.loads((run / 'training-time.json').read_text())
            seconds = data['totalSeconds']
            if (data['version'] == 1 and data['runId'] == run_id and data['boardSize'] == board_size
                    and math.isfinite(seconds) and seconds >= 0):
                return cls(run_id, board_size, seconds, bool(data['estimated']))
        except (OSError, ValueError, TypeError, KeyError):
            pass
        # Legacy sessions have no cumulative clock. Recover a lower-bound estimate
        # from retained trainer summaries; never present it as exact active time.
        total = previous = 0.0
        found = False
        for name in ['trainer.log.4', 'trainer.log.3', 'trainer.log.2', 'trainer.log.1', 'trainer.log']:
            try:
                with (run / name).open() as stream:
                    for line in stream:
                        match = cls.pattern.match(line.strip())
                        if not match:
                            continue
                        value = float(match[1])
                        if not math.isfinite(value):
                            continue
                        if value < previous:
                            total += previous
                        previous = value
                        found = True
            except FileNotFoundError:
                pass
        return cls(run_id, board_size, total + previous, found)

    def tick(self, now, active):
        if self.last_time is not None and active and self.was_active:
            self.data['totalSeconds'] += max(0, now - self.last_time)
        self.last_time, self.was_active = now, active

    def snapshot(self):
        return dict(self.data)
