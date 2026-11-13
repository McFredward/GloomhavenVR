"""Relocate the pinned Linux Wine socket directory into its private prefix.

Wine 9's client setup_config_dir and server create_server_dir both change to
the absolute WINEPREFIX before constructing this path. Android lacks /tmp.
Replacing only these equal-length, NUL-padded literals preserves ELF offsets,
format arguments, lock ownership checks and the original server identity.
"""
from __future__ import annotations
import hashlib
from pathlib import Path

FILES = {
    "bin/wineserver": ("bb73dbb5a736f2ca1523bb4fc75750bb2f4ae59dac771002983b9f9f89b1e62f", b"/tmp/.wine-%u\0"),
    "lib/wine/x86_64-unix/ntdll.so": ("fc6dd282c922dfd4bc812821e297313ab96af3e38439b9809aab23403214bfc9", b"/tmp/.wine-%u/server-%s\0"),
}


def relocate(data: bytes, expected: str, original: bytes) -> bytes:
    if hashlib.sha256(data).hexdigest() != expected or data.count(original) != 1:
        raise RuntimeError("Pinned Wine socket path input differs; refusing an unknown binary edit.")
    replacement = original.replace(b"/tmp/", b"./", 1).ljust(len(original), b"\0")
    result = data.replace(original, replacement, 1)
    if len(result) != len(data):
        raise RuntimeError("Wine socket path relocation changed an ELF offset.")
    return result


def apply(wine: Path) -> list[dict]:
    records = []
    for relative, (expected, original) in FILES.items():
        path = wine / relative
        data = relocate(path.read_bytes(), expected, original)
        path.write_bytes(data)
        records.append(dict(path=relative, originalSha256=expected,
                            sha256=hashlib.sha256(data).hexdigest(),
                            change="Only /tmp/.wine socket directory relocated under current absolute WINEPREFIX"))
    return records
