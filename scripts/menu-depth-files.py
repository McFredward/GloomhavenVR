#!/usr/bin/env python3
"""Verify the finite external menu-depth delivery set before staging or installation."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
import zipfile

FIELD_COUNT = 515
MANIFEST = 'Story/manifest.json'
PREFIX = 'BepInEx/plugins/GloomhavenVR/MenuDepth/'
FIELD_PATH = re.compile(r'Story/[0-9a-f]{32}\.gmd\Z')
MAX_MANIFEST = 4 * 1024 * 1024
MAX_FIELD = 32 + 512 * 512 * 2


def roster(path: Path) -> list[str]:
    names = [line.strip() for line in path.read_text(encoding='utf-8-sig').splitlines()
             if line.strip() and not line.lstrip().startswith('#')]
    if len(names) != FIELD_COUNT + 1 or len(set(names)) != len(names):
        raise ValueError('Menu depth roster must contain exactly 516 distinct companions')
    if names.count(MANIFEST) != 1 or any(name != MANIFEST and not FIELD_PATH.fullmatch(name) for name in names):
        raise ValueError('Invalid menu depth roster path')
    return names


def dimensions(value, maximum: int) -> tuple[int, int]:
    if (not isinstance(value, list) or len(value) != 2
            or any(type(axis) is not int or not 1 <= axis <= maximum for axis in value)):
        raise ValueError('Invalid menu depth dimensions')
    return tuple(value)


def validate(names: list[str], present: list[str], read) -> None:
    # Reject duplicate archive entries and orphan fields before reading any payload.
    missing = sorted(set(names) - set(present))
    if missing:
        raise ValueError('Missing required menu depth companion: ' + missing[0])
    selected = [name for name in present if name.endswith('.gmd') or name == MANIFEST]
    if len(selected) != len(set(selected)) or set(selected) != set(names):
        raise ValueError('Menu depth files do not match finite roster')
    document = json.loads(read(MANIFEST, MAX_MANIFEST).decode('utf-8'))
    if not isinstance(document, dict) or (document.get('schema'), document.get('status'), document.get('format')) != (1, 'complete', 'GMD1'):
        raise ValueError('Invalid menu depth manifest schema/status/format')
    images = document.get('images')
    if not isinstance(images, list) or len(images) != FIELD_COUNT:
        raise ValueError('Menu depth manifest must contain exactly 515 fields')
    seen = set()
    for image in images:
        if not isinstance(image, dict):
            raise ValueError('Invalid menu depth manifest image')
        source = image.get('source_id')
        if not isinstance(source, str) or not re.fullmatch('[0-9a-f]{32}', source) or source in seen:
            raise ValueError('Invalid or duplicate menu depth source identity')
        seen.add(source)
        filename = source + '.gmd'
        relative = 'Story/' + filename
        if image.get('depth_file') != filename or relative not in names:
            raise ValueError('Menu depth manifest and roster disagree: ' + relative)
        width, height = dimensions(image.get('depth_dimensions'), 512)
        native_width, native_height = dimensions(image.get('source_dimensions'), 16384)
        expected_size = 32 + width * height * 2
        if type(image.get('depth_bytes')) is not int or image['depth_bytes'] != expected_size:
            raise ValueError('Invalid menu depth manifest byte count: ' + relative)
        expected_hash = image.get('depth_sha256')
        if not isinstance(expected_hash, str) or not re.fullmatch('[0-9a-f]{64}', expected_hash):
            raise ValueError('Invalid menu depth manifest checksum: ' + relative)
        data = read(relative, MAX_FIELD)
        if len(data) != expected_size or hashlib.sha256(data).hexdigest() != expected_hash:
            raise ValueError('Menu depth companion size/checksum mismatch: ' + relative)
        magic, version, header, fw, fh, flags, size, _, sw, sh = struct.unpack_from('<4sHHHHIIIII', data)
        if ((magic, version, header, fw, fh, flags, size, sw, sh)
                != (b'GMD1', 1, 32, width, height, 3, expected_size - 32, native_width, native_height)):
            raise ValueError('Menu depth companion header mismatch: ' + relative)
    if {'Story/' + value + '.gmd' for value in seen} | {MANIFEST} != set(names):
        raise ValueError('Menu depth manifest and roster disagree')


def directory(path: Path, names: list[str]) -> None:
    def read(relative: str, bound: int) -> bytes:
        candidate = path / relative
        if candidate.is_symlink() or not candidate.is_file() or candidate.stat().st_size > bound:
            raise ValueError('Invalid menu depth companion: ' + relative)
        # Neither the Story folder nor a file may redirect an explicit roster path.
        if candidate.resolve().parent != (path.resolve() / 'Story'):
            raise ValueError('Menu depth companion escapes source folder: ' + relative)
        return candidate.read_bytes()
    if (path / 'Story').is_symlink():
        raise ValueError('Menu depth Story folder must not be a symbolic link')
    present = [entry.relative_to(path).as_posix() for entry in path.rglob('*') if entry.is_file()]
    validate(names, present, read)


def archive(path: Path, names: list[str]) -> None:
    with zipfile.ZipFile(path) as package:
        entries = [item for item in package.infolist() if item.filename.startswith(PREFIX) and not item.is_dir()]
        present = [item.filename[len(PREFIX):] for item in entries]
        by_name = {item.filename: item for item in entries}
        def read(relative: str, bound: int) -> bytes:
            entry = by_name[PREFIX + relative]
            if entry.file_size > bound:
                raise ValueError('Oversized menu depth archive companion: ' + relative)
            return package.read(entry)
        validate(names, present, read)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--roster', type=Path, required=True)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument('--directory', type=Path)
    target.add_argument('--archive', type=Path)
    parser.add_argument('--list', action='store_true', help='Print only verified relative paths')
    args = parser.parse_args()
    try:
        names = roster(args.roster)
        if args.directory is not None:
            directory(args.directory, names)
        else:
            archive(args.archive, names)
        print('\n'.join(names) if args.list else 'Menu depth companions: 515 fields and manifest verified.')
        return 0
    except (OSError, ValueError, KeyError, TypeError, struct.error, zipfile.BadZipFile) as error:
        print('error: ' + str(error), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
