#!/usr/bin/env python3
"""Inspect exported bytes, including gzip payloads, without ever printing credentials."""
import argparse
import gzip
import json
from pathlib import Path
import re

SOURCE = Path(__file__).resolve().parents[2]
MAX_ASSET = 25 * 1024 * 1024
KEY_PATTERNS = [re.compile(rb"sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}"),
                re.compile(rb"(?:ghp_|github_pat_)[A-Za-z0-9_]{30,}"),
                re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")]


def known_secrets():
    values = []
    settings = SOURCE/"Assets/Resources/PbpTransportSettings.asset"
    if settings.exists():
        match = re.search(r"^\s*releaseMobileApiKey:\s*(.+)$", settings.read_text(), re.M)
        if match:
            value = match.group(1).strip().strip('"\'')
            if value: values.append(value.encode())
    for name in ("pbp-api-key.staging", "pbp-api-key.default"):
        path = SOURCE/"UserSettings"/name
        if path.exists() and path.read_bytes().strip(): values.append(path.read_bytes().strip())
    return values


def inspect(directory):
    files = sorted(path for path in directory.rglob("*") if path.is_file())
    if not files or not (directory/"index.html").is_file(): raise ValueError("Missing complete web export")
    failures, total = [], 0
    secrets = known_secrets()
    for path in files:
        relative = str(path.relative_to(directory))
        if path.is_symlink(): failures.append(f"Symbolic link: {relative}")
        size = path.stat().st_size
        total += size
        if size > MAX_ASSET: failures.append(f"Cloudflare per-file limit: {relative}")
        if path.name in (".env", "auth.json") or any(x in path.parts for x in ("UserSettings", ".git", "human-games", "checkpoints")):
            failures.append(f"Private material path: {relative}")
        data = path.read_bytes()
        if data.startswith(b"\x1f\x8b"): data = gzip.decompress(data)
        if any(value in data or value.decode(errors="ignore").encode("utf-16-le") in data for value in secrets):
            failures.append(f"Known credential in exported bytes: {relative}")
        if any(pattern.search(data) for pattern in KEY_PATTERNS): failures.append(f"Credential signature: {relative}")
    if len(files) > 20000: failures.append("Cloudflare asset-count limit")
    if failures: raise ValueError("Release audit failed: " + "; ".join(failures))
    return {"files":len(files), "totalMiB":round(total/1024/1024,2),
            "largestMiB":round(max(path.stat().st_size for path in files)/1024/1024,2),
            "knownCredentialChecks":len(secrets), "credentialMatches":0}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    print(json.dumps(inspect(args.directory.resolve()), indent=2))
