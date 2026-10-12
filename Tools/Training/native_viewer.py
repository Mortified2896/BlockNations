"""Find and focus a separately opened Mac training inspector without owning it."""
import os
from pathlib import Path
import re
import subprocess
import sys


def matches_viewer(arguments, run):
    viewers = re.findall(r'(?:^|\s)--training-viewer\s+(true|false)(?=\s|$)', arguments, re.IGNORECASE)
    directories = re.findall(r'(?:^|\s)--viewer-run\s+(.+?)(?=\s--?[A-Za-z][\w-]*(?:\s|$)|$)', arguments)
    if not viewers or viewers[-1].lower() != 'true' or not directories:
        return False
    value = directories[-1].strip().strip('"\'')
    return Path(value).is_absolute() and Path(value).resolve() == Path(run).resolve()


def find_viewer(run):
    if sys.platform != 'darwin':
        return 0
    # Read command lines only for Unity game players, not other applications or
    # the learner. Never log their arguments. -ww avoids truncating spaced paths.
    listing = subprocess.run(['/bin/ps', '-axo', 'pid=,comm='], capture_output=True, text=True,
                             check=True, timeout=5)
    for row in listing.stdout.splitlines():
        fields = row.strip().split(None, 1)
        if len(fields) != 2 or not fields[0].isdigit() or Path(fields[1]).name != 'BlockNations':
            continue
        pid = int(fields[0])
        result = subprocess.run(['/bin/ps', '-ww', '-p', str(pid), '-o', 'args='],
                                capture_output=True, text=True, timeout=5)
        if result.returncode:
            try:
                os.kill(pid, 0)
            except ProcessLookupError:
                continue  # The window closed between enumeration and inspection.
            raise OSError('Could not inspect an existing BlockNations window; viewer launch was skipped.')
        if matches_viewer(result.stdout, run):
            return pid
    return 0


def alive(pid):
    if not pid:
        return False
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False


def focus(pid):
    if sys.platform != 'darwin':
        return True
    if type(pid) is not int or pid <= 0:
        raise ValueError('Focus requires a positive process identifier.')
    # AppKit activation targets the verified process and cannot open another
    # app/window. It does not require System Events or accessibility automation.
    script = ('ObjC.import("AppKit"); '
              f'$.NSRunningApplication.runningApplicationWithProcessIdentifier({pid}).activateWithOptions(2);')
    try:
        result = subprocess.run(['/usr/bin/osascript', '-l', 'JavaScript', '-e', script],
                                capture_output=True, text=True, timeout=5)
        return result.returncode == 0 and result.stdout.strip() == 'true'
    except (OSError, subprocess.SubprocessError):
        return False
