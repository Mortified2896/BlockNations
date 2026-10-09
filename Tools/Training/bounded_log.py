"""Bounded diagnostics shared by learner and optional viewer processes."""
from pathlib import Path


class BoundedLog:
    def __init__(self, path: Path, limit: int = 20 * 1024 * 1024):
        self.path, self.limit = path, limit
        self.stream = path.open("ab")

    def append(self, chunk: bytes) -> None:
        if self.stream.tell() + len(chunk) > self.limit:
            self.stream.close()
            for index in range(3, 0, -1):
                previous = self.path.with_name(self.path.name + f".{index}")
                following = self.path.with_name(self.path.name + f".{index + 1}")
                if previous.exists():
                    previous.replace(following)
            self.path.replace(self.path.with_name(self.path.name + ".1"))
            self.stream = self.path.open("wb")
        self.stream.write(chunk)
        self.stream.flush()

    def close(self) -> None:
        self.stream.close()
