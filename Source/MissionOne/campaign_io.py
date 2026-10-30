"""Read plain JSON or gzip campaign files; write compressed files atomically."""
import gzip
from pathlib import Path


def read_bytes(path):
    data = Path(path).read_bytes()
    return gzip.decompress(data) if data[:2] == b"\x1f\x8b" else data


def read_text(path, encoding="utf-8", errors=None):
    return read_bytes(path).decode(encoding, errors or "strict")


def write_text(path, text, encoding="utf-8", errors=None):
    path = Path(path)
    data = text.encode(encoding, errors or "strict")
    if path.name.endswith(".json.gz") or ".json.gz." in path.name:
        data = gzip.compress(data, compresslevel=6, mtime=0)
    temporary = path.with_name(path.name + ".writing")
    try:
        temporary.write_bytes(data)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)
    return len(text)
