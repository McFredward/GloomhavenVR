#!/usr/bin/env python3
"""Link every selected Unity bank on the actual local GLES device.

This independent driver gate consumes compiler receipts. A mono-only run states
its scope explicitly; it does not establish multiview support or Quest pixels.
Byte-identical banks share one driver compilation while every alias is retained.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import gles


def digest(data):
    return hashlib.sha256(data).hexdigest()


def link(receipt_path, manifest_path, output_path, stereo='mono'):
    receipt_path, manifest_path, output_path = map(Path, (receipt_path, manifest_path, output_path))
    receipt = json.loads(receipt_path.read_text())
    manifest = json.loads(manifest_path.read_text())
    if receipt['sourceManifestSha256'] != digest(manifest_path.read_bytes()):
        raise gles.GlesError('Driver input receipt does not identify the exact current manifest.')
    expected = {(s['guid'], v['subshader'], v['pass'], v['hardwareTier'], tuple(v['keywords']))
                for s in manifest['shaders'] for v in s['variants'] if v['stereo'] == stereo}
    selected = [p for p in receipt['programs'] if p['stereo'] == stereo]
    actual = {(p['guid'], p['subshader'], p['pass'], p['hardwareTier'], tuple(p['keywords'])) for p in selected}
    if not expected or expected != actual or len(selected) != len(actual):
        raise gles.GlesError('Driver input omits or duplicates selected original compiler aliases.')
    device = gles.Device()
    if stereo == 'multiview' and 'GL_OVR_multiview2' not in device.extensions:
        raise gles.GlesError('Actual local GLES device lacks GL_OVR_multiview2; multiview cannot be validated here.')
    result = {'schema': 1, 'scope': 'actual-native-gles-link', 'stereo': stereo,
              'sourceManifestSha256': digest(manifest_path.read_bytes()), 'compilerReceiptSha256': digest(receipt_path.read_bytes()),
              'renderer': device.renderer, 'vendor': device.vendor, 'version': device.version,
              'requiredAliasCount': len(selected), 'aliases': [], 'banks': [],
              'passed': False, 'headsetPictureVerified': False, 'originalPixelParityVerified': False}
    compiled = {}
    first = None
    try:
        for row in selected:
            raw = (receipt_path.parent / row['file']).read_bytes()
            if digest(raw) != row['glesSha256']:
                raise gles.GlesError('Actual generated bank bytes differ from the compiler receipt.')
            key = row['glesSha256']
            if key not in compiled:
                program, adaptation = device.program(raw.decode('utf-8'))
                device.call['glDeleteProgram'](program)
                compiled[key] = adaptation
                result['banks'].append({'glesSha256': key, **adaptation})
                first = first or raw.decode('utf-8')
            result['aliases'].append({k: row[k] for k in ('guid', 'subshader', 'pass', 'hardwareTier', 'keywords', 'glesSha256')})
        # Prove that the driver/compiler errors are observed, using a corrupted
        # copy of an actual generated native bank, never the production source.
        invalid = first.replace('void main()', 'void main() { questUndefinedNativePosition(); }\nvoid questDiscardedMain()', 1)
        try:
            program, _ = device.program(invalid)
        except gles.GlesError:
            result['negativeControlRejected'] = True
        else:
            device.call['glDeleteProgram'](program)
            raise gles.GlesError('Actual driver accepted the planted undefined native instruction.')
        result['passed'] = True
    except Exception as error:
        result['failure'] = str(error)
        result['failedAlias'] = {k: row[k] for k in ('guid', 'subshader', 'pass', 'hardwareTier', 'keywords', 'glesSha256')}
        raise
    finally:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(json.dumps(result, indent=2, sort_keys=True) + '\n')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--receipt', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--stereo', choices=('mono', 'instancing', 'multiview'), default='mono')
    args = parser.parse_args()
    result = link(args.receipt, args.manifest, args.output, args.stereo)
    print(f"PASS actual GLES {args.stereo} link: {len(result['aliases'])} aliases / {len(result['banks'])} unique banks")


if __name__ == '__main__':
    main()
