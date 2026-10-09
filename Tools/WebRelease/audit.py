#!/usr/bin/env python3
"""Inspect exported bytes, including gzip payloads, without ever printing credentials."""
import argparse
import gzip
import json
from pathlib import Path
import re
import struct

SOURCE = Path(__file__).resolve().parents[2]
MAX_ASSET = 25 * 1024 * 1024
KEY_PATTERNS = [re.compile(rb"sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}"),
                re.compile(rb"(?:ghp_|github_pat_)[A-Za-z0-9_]{30,}"),
                re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")]


def signature_buffers(data):
    """Respect IL2CPP literal boundaries; unrelated adjacent strings aren't keys.

    Unity 6000.4 uses metadata version 39 and offset/size/count sections. Unknown
    or malformed layouts retain the original raw-byte scan. Known credentials
    are always compared against the entire raw payload independently.
    """
    if not data.startswith(b"UnityWebData1.0\0"):
        return [data]
    try:
        end = struct.unpack_from("<I", data, 16)[0]
        if end < 20 or end > len(data): return [data]
        cursor = 20
        while cursor < end:
            offset, size, path_length = struct.unpack_from("<III", data, cursor)
            cursor += 12
            if cursor + path_length > end or offset < end or offset + size > len(data): return [data]
            name = data[cursor:cursor + path_length]
            cursor += path_length
            if not name.endswith(b"global-metadata.dat"): continue
            metadata = data[offset:offset + size]
            magic, version, records, record_bytes, count, literals, literal_bytes, literal_count = struct.unpack_from("<8I", metadata)
            if magic != 0xFAB11BAF or version != 39: return [data]
            if count < 1 or record_bytes != count * 4 or literal_count != count - 1 or records < 32 or \
                    records + record_bytes > literals or literals + literal_bytes > size: return [data]
            indices = struct.unpack_from("<" + str(count) + "I", metadata, records)
            if indices[0] != 0 or indices[-1] != literal_bytes or any(a > b for a, b in zip(indices, indices[1:])):
                return [data]
            start = offset + literals
            return [data[:start], *(data[start + a:start + b] for a, b in zip(indices, indices[1:])),
                    data[start + literal_bytes:]]
    except (ValueError, struct.error):
        pass
    return [data]


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
        if any(pattern.search(part) for part in signature_buffers(data) for pattern in KEY_PATTERNS):
            failures.append(f"Credential signature: {relative}")
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
