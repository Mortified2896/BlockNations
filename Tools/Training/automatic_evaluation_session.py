"""Start/reuse the optional evaluator without importing the learning runtime."""
import os
from pathlib import Path
import subprocess
import sys
import time

from automatic_evaluation import settings, read_json, STATUS, OWNER, alive, stop_process


class AutomaticEvaluationSession:
    def __init__(self, run, worker):
        self.run, self.worker = Path(run), Path(worker)
        self.process = None
        self.next_launch = 0

    def poll(self):
        if time.monotonic() < self.next_launch:
            return
        self.next_launch = time.monotonic() + 60
        try:
            config = settings(self.run)
            state = read_json(self.run / STATUS)
            attached = state.get('owner') == OWNER and state.get('runId') == self.run.name and alive(state.get('pid'))
            if not config or attached or self.process and self.process.poll() is None:
                return
            self.process = subprocess.Popen([sys.executable, str(Path(__file__).with_name('automatic_evaluation.py')),
                                            '--run', str(self.run), '--worker', str(self.worker)],
                                           stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                           stderr=subprocess.DEVNULL, start_new_session=True)
        except (OSError, ValueError):
            pass  # Optional evaluation can never stop or restart the learner.

    def close(self):
        if self.process and self.process.poll() is None:
            stop_process(self.process)
