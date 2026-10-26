#!/usr/bin/env python3
"""Independently compare every shipped field with retained native pixels and neural outputs."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import time
import zlib

import cv2
import numpy as np
from PIL import Image

from catalogue import digest, write_json


def verify(catalogue: Path, evidence: Path, assets: Path, data_root: Path, output: Path) -> dict:
    source = json.loads(catalogue.read_text())
    manifest = json.loads((assets / 'manifest.json').read_text())
    if digest(catalogue) != manifest['source_catalogue_sha256']:
        raise ValueError('Shipping corpus belongs to another source inventory')
    if digest(data_root / 'StreamingAssets/aa/catalog.json') != manifest['addressable_catalogue_sha256']:
        raise ValueError('Original Addressables catalogue changed')
    for pin in [*source['source_bundles'], *source['embedded_asset_pins']]:
        if digest(data_root / pin['path']) != pin['sha256']:
            raise ValueError('Read-only native asset changed: ' + pin['path'])
    unique = {row['source_id']: row for row in source['artwork']}
    rows = {row['source_id']: row for row in manifest['images']}
    if set(rows) != set(unique) or len(rows) != 515:
        raise ValueError('Shipped source coverage is incomplete')
    began = time.monotonic()
    cv2.setNumThreads(1)
    proofs = []
    for key, art in sorted(unique.items()):
        snapshot = catalogue.parent / art['source_snapshot']
        if digest(snapshot) != art['source_png_sha256']:
            raise ValueError('Native source snapshot bytes changed: ' + key)
        with Image.open(snapshot) as image:
            rgba = np.asarray(image.convert('RGBA'))
        if hashlib.sha256(rgba.tobytes()).hexdigest() != art['rgba_sha256']:
            raise ValueError('Original decoded color pixels changed: ' + key)
        row = rows[key]
        raw_path = evidence / (key + '.npz')
        if digest(raw_path) != row['raw_inference_sha256']:
            raise ValueError('Original model output changed: ' + key)
        with np.load(raw_path, allow_pickle=False) as stored:
            raw = stored['raw']
        if raw.dtype != np.float32 or raw.shape != rgba.shape[:2] or not np.isfinite(raw).all():
            raise ValueError('Invalid retained original inference: ' + key)
        low, high = np.quantile(raw[rgba[:, :, 3] >= 128], [.02, .98], method='linear')
        if not np.allclose([low, high], row['metrics']['normalization_p02_p98'], rtol=0, atol=1e-10):
            raise ValueError('Inferred source range changed: ' + key)
        normalized = np.clip((raw - float(low)) / float(high - low), 0, 1).astype(np.float32)
        width, height = row['depth_dimensions']
        resized = cv2.resize(normalized, (width, height), interpolation=cv2.INTER_AREA)
        smoothed = cv2.bilateralFilter(resized, 5, .08, 1.2)
        expected = np.rint(smoothed.astype(np.float64) * 65535).astype('<u2')
        binary = (assets / row['depth_file']).read_bytes()
        identity, version, header_bytes, w, h, flags, payload_bytes, crc, sw, sh = struct.unpack_from('<4sHHHHIIIII', binary)
        if (identity, version, header_bytes, flags) != (b'GMD1', 1, 32, 3):
            raise ValueError('Invalid shipped format identity')
        payload = binary[32:]
        if (w, h) != (width, height) or (sw, sh) != tuple(art['logical_dimensions']) or payload_bytes != len(payload) or zlib.crc32(payload) != crc:
            raise ValueError('Shipped coordinate/checksum metadata changed')
        actual = np.frombuffer(payload, '<u2').reshape(height, width)
        # This is an independent literal format/profile check, not infer.normalized.
        if not np.array_equal(actual[::-1], expected):
            raise ValueError('Shipped field lost raw model values or native bottom-left orientation: ' + key)
        if art['logical_dimensions'] != art['texture_dimensions'] or art['raster_reconstruction'] != 'native-full-texture':
            raise ValueError('Unproven atlas/native UV coordinate system: ' + key)
        # Audit actual trimmed image corners, not just the whole-texture corners.
        x, y, rw, rh = art['texture_rect']
        samples = []
        for u, v in ((x / sw, y / sh), ((x + rw) / sw, (y + rh) / sh), (.5, .5)):
            fx, fy = min(w - 1, max(0, round(u * (w - 1)))), min(h - 1, max(0, round(v * (h - 1))))
            sx, sy = min(sw - 1, max(0, round(u * (sw - 1)))), min(sh - 1, max(0, round((1 - v) * (sh - 1))))
            samples.append({'native_uv': [u, v], 'source_pixel_top_origin': [sx, sy],
                            'native_rgba': rgba[sy, sx].tolist(), 'payload_pixel_bottom_origin': [fx, fy],
                            'payload_u16': int(actual[fy, fx]), 'expected_from_neural_u16': int(expected[h - 1 - fy, fx])})
        proofs.append({'source_id': key, 'source_rgba_sha256': art['rgba_sha256'],
                       'actual_depth_sha256': digest(assets / row['depth_file']),
                       'raw_inference_sha256': row['raw_inference_sha256'], 'trimmed_uv_alignment': samples})
        if len(proofs) % 100 == 0:
            print(json.dumps({'verified': len(proofs), 'elapsed_seconds': time.monotonic() - began}), flush=True)
    result = {'status': 'all-native-neural-and-shipped-coordinates-match', 'images': len(proofs),
              'native_pixel_depth_uv_samples': len(proofs) * 3,
              'source_catalogue_sha256': digest(catalogue), 'shipping_manifest_sha256': digest(assets / 'manifest.json'),
              'elapsed_seconds': time.monotonic() - began, 'proofs': proofs,
              'scope': 'Actual original RGBA and retained model outputs, every shipped scalar sample/CRC/orientation and native trimmed-UV coordinates. No headset or production GPU claim.'}
    write_json(output, result)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalogue', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--data-root', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    result = verify(args.catalogue, args.evidence, args.assets, args.data_root, args.out)
    print(json.dumps({key: value for key, value in result.items() if key != 'proofs'}))
