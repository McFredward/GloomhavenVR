#!/usr/bin/env python3
"""Resolve native art GUIDs from Unity's actual compact Addressables catalogue."""
from __future__ import annotations
import argparse
import base64
import json
from pathlib import Path
import re
import struct

from catalogue import digest, write_json


def read_key(data: bytes, index: int):
    if not 0 <= index < len(data):
        raise ValueError('Catalogue key offset out of bounds')
    kind = data[index]
    index += 1
    if kind in (0, 1):
        size = struct.unpack_from('<i', data, index)[0]
        if not 0 <= size <= len(data) - index - 4:
            raise ValueError('Invalid catalogue string length')
        return data[index + 4:index + 4 + size].decode('ascii' if kind == 0 else 'utf-16-le')
    if kind in (2, 3, 4):
        return struct.unpack_from({2: '<H', 3: '<I', 4: '<i'}[kind], data, index)[0]
    if kind == 5:
        size = data[index]
        return data[index + 1:index + 1 + size].decode('ascii')
    raise ValueError('Unsupported native catalogue key type: ' + str(kind))


def normalize(value: str) -> str:
    return value.replace('\\', '/').lower()


def locations(catalogue: dict) -> tuple[list, list[dict]]:
    keys = base64.b64decode(catalogue['m_KeyDataString'], validate=True)
    buckets = base64.b64decode(catalogue['m_BucketDataString'], validate=True)
    entries = base64.b64decode(catalogue['m_EntryDataString'], validate=True)
    count = struct.unpack_from('<i', buckets)[0]
    if count != struct.unpack_from('<i', keys)[0]:
        raise ValueError('Catalogue key/bucket counts differ')
    offset = 4
    key_values, memberships = [], []
    for _ in range(count):
        key_offset, n = struct.unpack_from('<ii', buckets, offset)
        offset += 8
        if n < 0 or offset + n * 4 > len(buckets):
            raise ValueError('Invalid catalogue bucket membership')
        memberships.append(struct.unpack_from('<' + 'i' * n, buckets, offset))
        offset += n * 4
        key_values.append(read_key(keys, key_offset))
    if offset != len(buckets):
        raise ValueError('Trailing catalogue bucket bytes')
    entry_count = struct.unpack_from('<i', entries)[0]
    if len(entries) != 4 + entry_count * 28:
        raise ValueError('Truncated native catalogue locations')
    records = []
    for index in range(entry_count):
        internal, provider, dependency, dep_hash, extra, primary, resource = struct.unpack_from('<7i', entries, 4 + index * 28)
        path = catalogue['m_InternalIds'][internal]
        if catalogue['m_InternalIdPrefixes'] and '#' in path:
            prefix, suffix = path.rsplit('#', 1)
            if prefix.isdigit():
                path = catalogue['m_InternalIdPrefixes'][int(prefix)] + suffix
        records.append({'internal_id': path, 'primary_key': key_values[primary],
                        'resource': catalogue['m_resourceTypes'][resource], 'keys': []})
    for key, membership in zip(key_values, memberships):
        for entry in membership:
            if not 0 <= entry < entry_count:
                raise ValueError('Catalogue entry index out of bounds')
            records[entry]['keys'].append(key)
    return key_values, records


def bind(catalogue_file: Path, source_file: Path, output: Path) -> dict:
    native = json.loads(catalogue_file.read_text())
    _, records = locations(native)
    by_path = {}
    for record in records:
        if not str(record['resource'].get('m_ClassName', '')).endswith('Sprite'):
            continue
        paths = {normalize(value) for value in [record['internal_id'], record['primary_key'], *record['keys']]
                 if isinstance(value, str) and value.lower().startswith('assets/')}
        guids = {key.lower() for key in record['keys']
                 if isinstance(key, str) and re.fullmatch('[0-9a-fA-F]{32}', key)}
        for path in paths:
            if path in by_path and by_path[path] != guids:
                raise ValueError('Conflicting exact sprite GUIDs: ' + path)
            by_path[path] = guids
    source = json.loads(source_file.read_text())
    mappings = []
    for art in source['artwork']:
        if art['kind'] == 'event-fallback':
            guids = set()
        else:
            guids = by_path.get(normalize(art['asset_path']), set())
            if len(guids) != 1:
                raise ValueError('Native art does not resolve to exactly one Sprite GUID: ' + art['asset_path'] + ' / ' + str(guids))
        mappings.append({'source_id': art['source_id'], 'asset_path': art['asset_path'],
                         'addressable_guid': next(iter(guids)) if guids else None,
                         'native_field': art.get('native_field')})
    result = {'schema': 1, 'catalogue_sha256': digest(catalogue_file),
              'source_catalogue_sha256': digest(source_file), 'artwork_bindings': mappings}
    write_json(output, result)
    return result


def augment(manifest_file: Path, mapping_file: Path) -> None:
    manifest, mappings = json.loads(manifest_file.read_text()), json.loads(mapping_file.read_text())
    if manifest['source_catalogue_sha256'] != mappings['source_catalogue_sha256']:
        raise ValueError('GUID map belongs to a different native art inventory')
    if manifest.get('addressable_map_sha256'):
        raise ValueError('Refusing to overwrite an already augmented shipping manifest')
    by_path = {row['asset_path']: row for row in mappings['artwork_bindings']}
    manifest['neural_manifest_sha256'] = digest(manifest_file)
    manifest['addressable_map_sha256'] = digest(mapping_file)
    manifest['addressable_catalogue_sha256'] = mappings['catalogue_sha256']
    for row in manifest['artwork']:
        binding = by_path[row['asset_path']]
        if row['source_id'] != binding['source_id']:
            raise ValueError('GUID binding selected another native source')
        row['addressable_guid'] = binding['addressable_guid']
        row['native_field'] = binding['native_field']
    write_json(manifest_file, manifest)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest='command', required=True)
    scan = subparsers.add_parser('bind')
    scan.add_argument('--catalogue', type=Path, required=True)
    scan.add_argument('--sources', type=Path, required=True)
    scan.add_argument('--out', type=Path, required=True)
    update = subparsers.add_parser('augment')
    update.add_argument('--manifest', type=Path, required=True)
    update.add_argument('--mapping', type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'bind':
        result = bind(args.catalogue, args.sources, args.out)
        print(json.dumps({'status': 'bound', 'artwork': len(result['artwork_bindings'])}))
    else:
        augment(args.manifest, args.mapping)
        print(json.dumps({'status': 'shipping-manifest-augmented'}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
