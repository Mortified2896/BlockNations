"""Bounded telemetry from ML-Agents' reported self-play rating, not a benchmark."""
import json
import math
import re
import threading


class TrainerEloHistory:
    maximum_points = 200
    behavior = "BlockNationsSeatV2"
    pattern = re.compile(r'^\[INFO\] BlockNationsSeatV2\. Step: (\d+)\..* ELO: (-?\d+(?:\.\d+)?)\.$')

    def __init__(self, run_id, board_size, initial_elo=1200):
        self.lock = threading.Lock()
        self.pending = b""
        self.data = dict(version=1, schema=2, runId=run_id, boardSize=board_size,
                         behavior=self.behavior, points=[dict(step=0, elo=float(initial_elo))])

    def restore(self, path):
        try:
            saved = json.loads(path.read_text())
            if any(saved.get(key) != self.data[key] for key in ('version', 'schema', 'runId', 'boardSize', 'behavior')):
                return
            points = saved['points']
            if not 1 <= len(points) <= self.maximum_points or points[0]['step'] != 0:
                return
            previous = -1
            for point in points:
                if not isinstance(point['step'], int) or point['step'] <= previous or not math.isfinite(point['elo']):
                    return
                previous = point['step']
            with self.lock:
                self.data['points'] = points
        except (OSError, ValueError, KeyError, TypeError):
            pass

    def backfill(self, run):
        for name in ['trainer.log.4', 'trainer.log.3', 'trainer.log.2', 'trainer.log.1', 'trainer.log']:
            try:
                with (run / name).open('rb') as stream:
                    while chunk := stream.read(4096):
                        self.feed(chunk)
            except FileNotFoundError:
                pass
            self.pending = b""  # A rotated file may end partway through a line.

    def feed(self, chunk):
        with self.lock:
            lines = (self.pending + chunk).split(b'\n')
            self.pending = lines.pop()[-8192:]
            for line in lines:
                match = self.pattern.match(line.decode('utf-8', errors='replace').strip())
                if not match:
                    continue
                step, elo = int(match[1]), float(match[2])
                if step <= 0 or not math.isfinite(elo):
                    continue
                points = self.data['points']
                if step < points[-1]['step']:
                    continue
                if step == points[-1]['step']:
                    points[-1] = dict(step=step, elo=elo)
                else:
                    points.append(dict(step=step, elo=elo))
                if len(points) > self.maximum_points:
                    del points[1]  # Preserve the starting reference and latest reports.

    def snapshot(self):
        with self.lock:
            return dict(self.data, points=[dict(point) for point in self.data['points']])
