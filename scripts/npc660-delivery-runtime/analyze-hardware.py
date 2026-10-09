#!/usr/bin/env python3
"""Retain paired build banners and exact native rejection/admission evidence."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import re


def analyze(path):
    raw = path.read_bytes()
    lines = raw.decode('utf-8', errors='replace').splitlines()
    tokens = ['GloomhavenVR ModBuild', 'v1.1.0 build', 'Native enhancement picture waiting:',
              'Native enhancement picture admitted:', 'Native enhancement picture displayed:',
              'Original widget publisher unavailable:', 'Original service presentation unavailable (native template metadata playback:']
    evidence = {token: [{'line': index + 1, 'text': line} for index, line in enumerate(lines) if token in line] for token in tokens}
    assemblies = []
    for index, line in enumerate(lines):
        match = re.search(r'Native original bundle assembled: peer=(\d+) stream=(\d+) sequence=(\d+) members=(\d+) bytes=(\d+) assembly=([0-9.]+)s', line)
        if not match:
            continue
        peer, stream, sequence, members, length = map(int, match.groups()[:5])
        assemblies.append({'line': index + 1, 'peer': peer, 'stream': stream, 'sequence': sequence,
                           'visitor_stock': bool(sequence & (1 << 63)), 'members': members,
                           'decoded_bytes': length, 'assembly_seconds': float(match[6])})
    unavailable = evidence[tokens[-1]]
    addresses = collections.Counter(re.search(r'template: (.*?), structure=', row['text'])[1].split('|')[0] for row in unavailable)
    return {'input': str(path), 'sha256': hashlib.sha256(raw).hexdigest(), 'lines': len(lines),
            'evidence': evidence, 'native_basis_rejections_by_template': dict(addresses),
            'longest_assemblies': sorted(assemblies, key=lambda row: row['assembly_seconds'], reverse=True)[:20],
            'limits': 'Bundle assembly measures first fragment to complete atomic bytes, not owner handoff or first visible headset pixels. Stock marker is bit63; stream65534 alone is not private mage latency.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inputs', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = {'host': analyze(args.inputs / 'LogOutput.log'), 'remote': analyze(args.inputs / 'remote/LogOutput.log')}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    for side, data in result.items():
        print(side + ': ' + ', '.join(str(len(rows)) + ' ' + name for name, rows in data['evidence'].items()))


if __name__ == '__main__':
    main()
