#!/usr/bin/env python3
"""Portable checks for the actual shipped narrative depth corpus and strict format."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
import zlib

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/menu-depth'))
from heightfield import decode, encode
from addressables import read_key

checks = 0
refusals = 0


def check(value: bool, label: str) -> None:
    global checks
    checks += 1
    if not value:
        raise AssertionError(label)


def refuse(data: bytes, label: str) -> None:
    global refusals
    refusals += 1
    try:
        decode(data)
    except ValueError:
        check(True, label)
    else:
        check(False, 'Malformed native field accepted: ' + label)


def format_vectors() -> None:
    # Byte-exact vector is independent of the encoder/decoder's field construction.
    golden = bytes.fromhex('474d44310100200003000200030000000c00000090f3f8230c000000080000000000ffff0080002000c03930')
    payload = struct.pack('<6H', 0, 65535, 32768, 8192, 49152, 12345)
    check(encode(3, 2, 12, 8, payload) == golden, 'Portable golden header and ushort order')
    parsed = decode(golden)
    check((parsed['width'], parsed['height'], parsed['source_width'], parsed['source_height']) == (3, 2, 12, 8), 'Golden dimensions')
    check(parsed['payload'] == payload and parsed['flags'] == 3, 'Bottom-left inverse-depth payload retained')
    check(struct.unpack_from('<3H', parsed['payload']) == (0, 65535, 32768), 'Golden bottom row far/near/mid')
    check(struct.unpack_from('<3H', parsed['payload'], 6) == (8192, 49152, 12345), 'Golden top row retained')
    for offset, replacement, label in ((0, b'BAD!', 'magic'), (4, b'\x02\0', 'version'),
            (6, b'\x1f\0', 'header size'), (8, b'\0\0', 'zero width'),
            (8, b'\x01\x02', 'oversized width'), (10, b'\0\0', 'zero height'),
            (12, b'\x01\0\0\0', 'lost inverse-depth flag'), (12, b'\x07\0\0\0', 'unknown flag'),
            (16, b'\x0b\0\0\0', 'payload length'), (20, b'\0\0\0\0', 'checksum'),
            (24, b'\0\0\0\0', 'zero native source width'), (28, b'\x01\x40\0\0', 'oversized native height')):
        changed = bytearray(golden); changed[offset:offset + len(replacement)] = replacement
        refuse(bytes(changed), label)
    refuse(golden[:31], 'truncated header')
    refuse(golden[:-1], 'truncated payload')
    refuse(golden + b'\0', 'trailing payload')
    changed = bytearray(golden); changed[-1] ^= 1
    refuse(bytes(changed), 'actual ushort corruption')
    check(read_key(b'\0\x04\0\0\0test', 0) == 'test', 'Native ASCII addressable key')
    check(read_key(b'\x01\x04\0\0\0A\0B\0', 0) == 'AB', 'Native Unicode addressable key')
    check(read_key(b'\x05\x04abcd', 0) == 'abcd', 'Native hash addressable key')


def corpus() -> None:
    assets = ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Story'
    manifest = json.loads((assets / 'manifest.json').read_text())
    check((manifest['schema'], manifest['status'], manifest['format']) == (1, 'complete', 'GMD1'), 'Complete shipping manifest')
    check(len(manifest['images']) == 515, 'All unique native images included')
    check(len(manifest['artwork']) == 523, 'All original asset references included')
    check(len(manifest['narrative_configs']) == 448, 'All native narrative IDs included')
    fields = {row['source_id']: row for row in manifest['images']}
    check(len(fields) == 515, 'Unique field identities')
    check({path.name for path in assets.glob('*.gmd')} == {row['depth_file'] for row in fields.values()}, 'No missing or orphan shipped heightfield')
    check(not any(path.suffix.lower() in ('.png', '.jpg', '.jpeg', '.pth', '.npz') for path in assets.iterdir()), 'No original color/model/runtime inference shipped')
    check(manifest['profile']['device'] == 'cpu' and manifest['profile']['dtype'] == 'float32', 'Pinned offline CPU profile')
    check(manifest['profile']['frame_count'] == 1 and manifest['profile']['temporal_history'] is False, 'Unrelated illustrations have independent static inference')
    total = 0
    for source_id, row in fields.items():
        check(re.fullmatch('[0-9a-f]{32}', source_id) is not None, 'Finite source identity')
        check(row['source_rgba_sha256'].startswith(source_id), 'Depth identity bound to decoded original pixels')
        check(row['depth_file'] == source_id + '.gmd', 'Source-bound bounded filename')
        data = (assets / row['depth_file']).read_bytes()
        check(len(data) == row['depth_bytes'] and hashlib.sha256(data).hexdigest() == row['depth_sha256'], 'Actual delivered depth bytes/hash')
        item = decode(data)
        # Independent header access catches a format implementation that changes both helpers.
        check(data[:8] == b'GMD1\x01\0\x20\0' and struct.unpack_from('<I', data, 12)[0] == 3, 'Actual fixed header identity and flags')
        check(item['width'] * item['height'] * 2 == len(data) - 32, 'Exact bounded payload')
        check(zlib.crc32(data[32:]) == struct.unpack_from('<I', data, 20)[0], 'Actual independent CRC')
        check([item['width'], item['height']] == row['depth_dimensions'], 'Manifest/data field dimensions')
        check([item['source_width'], item['source_height']] == row['source_dimensions'], 'Original logical coordinate dimensions')
        sw, sh = row['source_dimensions']; factor = 512 / max(sw, sh)
        check((item['width'], item['height']) == (max(1, round(sw * factor)), max(1, round(sh * factor))), 'Native aspect preserved at declared field rate')
        check(max(item['width'], item['height']) == 512, 'Full declared field resolution')
        values = struct.unpack('<' + 'H' * (item['width'] * item['height']), item['payload'])
        check(min(values) == row['metrics']['minimum'] and max(values) == row['metrics']['maximum'], 'Actual inferred near/far extrema')
        check(min(values) < max(values) and row['metrics']['standard_deviation'] > .001, 'Native depth not collapsed to one plane')
        check(row['metrics']['maximum_quantization_error'] <= .5001 / 65535, 'Bounded field quantization')
        check(row['code_revision'] == '4f5ae23172ba60fd7bc11ef671cca678842c7072', 'Original reviewed neural code revision')
        check(row['checkpoint_sha256'] == '13379300b739e659f076a59d52e9801bd8d38c541a7e71f73bbca4dcfb013609', 'Original reviewed model checkpoint')
        check(row['profile'] == manifest['profile'], 'One deterministic profile for every source')
        total += len(data)
    ids = set()
    for row in manifest['narrative_configs']:
        check(row['id'].lower() not in ids, 'No ambiguous case-insensitive narrative ID')
        ids.add(row['id'].lower())
        check(row['source_id'] in fields, 'Native narrative source has a delivered field')
        check(row['canonical_asset'] == row['path_picture'].replace('\\', '/').lower() + '.png', 'Exact native addressable path preserved')
    check('loadoutart' in ids, 'Native first-config fallback retained')
    guids = {}
    kinds = {}
    names = {}
    for art in manifest['artwork']:
        check(art['source_id'] in fields, 'Native art resolves to exact field')
        row = fields[art['source_id']]
        check(art['rgba_sha256'] == row['source_rgba_sha256'], 'Native art/field source hash agrees')
        check(art['logical_dimensions'] == art['texture_dimensions'] == row['source_dimensions'], 'Full source texture/logical/depth alignment')
        check(art['raster_reconstruction'] == 'native-full-texture', 'No hidden atlas/reconstructed coordinate assumption')
        check(art['sprite_rect'][:2] == [0, 0]
              and all(round(raw) == logical and abs(raw - logical) < .0001
                      for raw, logical in zip(art['sprite_rect'][2:], art['logical_dimensions'])),
              'Native original rect matches source field domain within serialized float precision')
        kinds[art['kind']] = kinds.get(art['kind'], 0) + 1
        key = (art['sprite_name'].lower(), tuple(art['logical_dimensions']))
        names.setdefault(key, set()).add(art['source_id'])
        guid = art['addressable_guid']
        if art['kind'] == 'event-fallback':
            check(guid is None and art['native_field'] in ('defaultEventImage', 'defaultCityEventImage'), 'Embedded event fallback uses exact typed slot')
        else:
            check(re.fullmatch('[0-9a-f]{32}', guid) is not None, 'Exact native addressable GUID supplied')
            check(guid not in guids or guids[guid] == art['source_id'], 'No ambiguous GUID source')
            guids[guid] = art['source_id']
    check(kinds == {'narrative': 448, 'compendium': 73, 'event-fallback': 2}, 'Every intended native art family covered')
    check(len(guids) == 521, 'All521 addressable Sprite identities resolved')
    check(any(len(values) > 1 for values in names.values()), 'Real name collisions retained rather than guessed away')
    print(f'Actual corpus: 448 narrative IDs, 523 asset bindings, 515 fields, {total} field bytes; 521 exact GUIDs.')


if __name__ == '__main__':
    format_vectors()
    corpus()
    print(f'Menu depth media: {checks} assertions passed; {refusals} malformed-format controls rejected.')
