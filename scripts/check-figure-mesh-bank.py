#!/usr/bin/env python3
"""Validate all indexed, split figure mesh banks without importing the game or Unity.

The runtime resolves the index beside the plugin. A missing part silently restores
native detail, so every release and committed-bank gate checks the complete set.
"""
import json
from pathlib import Path
import re
import sys


def validate(folder):
    path = folder / 'ghvr-figure-meshes-index.json'
    if path.stat().st_size > 1024 * 1024:
        raise ValueError('figure index exceeds runtime bound')
    entries = json.loads(path.read_text(encoding='utf-8'))['entries']
    if not entries or len(entries) > 4096:
        raise ValueError('invalid figure entry count')
    names, banks = set(), set()
    for entry in entries:
        mesh, bank = entry['mesh'], entry['bank']
        match = re.fullmatch(r'figure-[0-9a-f]{16}-(5|20|45|75)', mesh)
        if not match or not re.fullmatch(r'ghvr-figure-meshes-(5|20|45|75|distance)-[0-9]{2}\.bundle', bank):
            raise ValueError('invalid figure identity or part filename')
        if (not bank.startswith('ghvr-figure-meshes-distance-') and f'-{match[1]}-' not in bank) or mesh in names:
            raise ValueError('duplicate identity or mismatched part tier')
        names.add(mesh)
        banks.add(bank)
    present = {p.name for p in folder.glob('ghvr-figure-meshes-*.bundle')}
    if banks != present:
        raise ValueError(f'figure part mismatch: missing {sorted(banks-present)}, orphan {sorted(present-banks)}')
    for filename in sorted(banks):
        with (folder / filename).open('rb') as stream:
            header = stream.read(64)
        if header[:8] != b'UnityFS\0' or int.from_bytes(header[8:12], 'big') != 7:
            raise ValueError(f'{filename}: incompatible UnityFS wrapper')
        if header[12:].split(b'\0', 2)[:2] != [b'5.x.x', b'2021.3.5f1']:
            raise ValueError(f'{filename}: editor differs from game 2021.3.5f1')
    return len(names), len(banks)


if __name__ == '__main__':
    try:
        folder = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / 'prebuilt'
        meshes, parts = validate(folder)
        print(f'Figure mesh bank OK: {meshes} unique derivatives in {parts} game-compatible parts.')
    except (OSError, ValueError, KeyError, TypeError) as error:
        print('Figure mesh bank rejected: ' + str(error), file=sys.stderr)
        raise SystemExit(1)
