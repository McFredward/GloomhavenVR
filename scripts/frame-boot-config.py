#!/usr/bin/env python3
"""Prepare Unity graphics jobs before the first Steam Frame game launch.

Unity reads GH_Data/boot.config before the BepInEx preloader runs. The preloader
can update the file, but its change cannot affect that first process. This
installer helper applies the same two settings ahead of time, while keeping an
unaltered, one-time backup for recovery.
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import stat
import sys
import tempfile


GRAPHICS_KEYS = (b"gfx-enable-gfx-jobs", b"gfx-enable-native-gfx-jobs")


def graphics_jobs_enabled(config_path: Path) -> bool:
    """Match the preloader's [Core] EnableGraphicsJobs default and false check."""
    if not config_path.is_file():
        return True

    try:
        lines = config_path.read_text(encoding="utf-8-sig").splitlines()
    except (OSError, UnicodeError):
        # The preloader also retains its enabled default when it cannot parse the file.
        return True

    section = ""
    for raw in lines:
        line = raw.strip()
        if not line or line.startswith(("#", ";")):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip().lower()
            continue
        if "=" not in line or section != "core":
            continue
        key, value = line.split("=", 1)
        if key.strip().lower() == "enablegraphicsjobs":
            return value.strip().lower() != "false"
    return True


def preferred_newline(content: bytes) -> bytes:
    crlf = content.count(b"\r\n")
    lf = content.count(b"\n") - crlf
    return b"\r\n" if crlf > lf else b"\n"


def update_graphics_keys(content: bytes, value: bytes) -> bytes:
    """Change only the two graphics-job keys and preserve other lines verbatim."""
    lines = content.splitlines(keepends=True)
    output: list[bytes] = []
    seen: set[bytes] = set()

    for line in lines:
        body = line.rstrip(b"\r\n")
        newline = line[len(body) :]
        before, separator, current = body.partition(b"=")
        key = before.strip().lower().removeprefix(b"\xef\xbb\xbf")
        if separator and key in GRAPHICS_KEYS:
            seen.add(key)
            if current.strip() != value:
                body = before + separator + value
            line = body + newline
        output.append(line)

    missing = [key for key in GRAPHICS_KEYS if key not in seen]
    if missing:
        newline = preferred_newline(content)
        if output and not output[-1].endswith((b"\n", b"\r")):
            output.append(newline)
        output.extend(key + b"=" + value + newline for key in missing)

    return b"".join(output)


def write_atomic(path: Path, content: bytes, mode: int) -> None:
    fd, temp_name = tempfile.mkstemp(prefix=path.name + ".gloomhavenvr-", dir=path.parent)
    temp = Path(temp_name)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temp, stat.S_IMODE(mode))
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def backup_once(path: Path, content: bytes, mode: int) -> Path:
    backup = path.with_name(path.name + ".gloomhavenvr-backup")
    if os.path.lexists(backup):
        return backup

    fd, temp_name = tempfile.mkstemp(prefix=backup.name + "-", dir=path.parent)
    temp = Path(temp_name)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temp, stat.S_IMODE(mode))
        try:
            os.link(temp, backup)
        except FileExistsError:
            pass
    finally:
        temp.unlink(missing_ok=True)
    return backup


def prepare(game_path: Path, dry_run: bool = False) -> bool:
    boot = game_path / "GH_Data" / "boot.config"
    if not boot.is_file():
        raise FileNotFoundError(f"Unity boot config is missing: {boot}")

    config = game_path / "BepInEx" / "config" / "dev.gloomhavenvr.cfg"
    wanted = b"1" if graphics_jobs_enabled(config) else b"0"
    original = boot.read_bytes()
    updated = update_graphics_keys(original, wanted)
    if updated == original:
        print(f"Unity graphics jobs already {'enabled' if wanted == b'1' else 'disabled'}; boot.config unchanged.")
        return False

    if dry_run:
        print(f"Would set Unity graphics jobs to {wanted.decode()} in {boot} (dry run).")
        return True

    mode = boot.stat().st_mode
    backup = backup_once(boot, original, mode)
    write_atomic(boot, updated, mode)
    print(f"Unity graphics jobs set to {wanted.decode()} before first launch; original saved at {backup}.")
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-path", required=True, type=Path, help="Gloomhaven installation directory")
    parser.add_argument("--dry-run", action="store_true", help="Report the change without writing files")
    args = parser.parse_args()
    try:
        prepare(args.game_path, args.dry_run)
    except (OSError, UnicodeError) as error:
        print(f"Steam Frame setup: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
