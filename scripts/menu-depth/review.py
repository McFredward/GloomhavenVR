#!/usr/bin/env python3
"""Diagnostic source/depth/oblique point-raster witnesses; never replacement game art."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

from catalogue import digest, write_json
from heightfield import decode


def field(path: Path) -> np.ndarray:
    item = decode(path.read_bytes())
    return np.frombuffer(item['payload'], dtype='<u2').reshape(item['height'], item['width'])[::-1].astype(np.float32) / 65535


def oblique(image: np.ndarray, depth: np.ndarray, eye: float) -> tuple[np.ndarray, int]:
    """Project original RGB samples on a 2%-width relief through the fixed image plane.

    Uncovered pixels are intentionally black. This point witness neither invents
    disoccluded content nor claims to execute the production Unity triangle path.
    """
    h, w = image.shape[:2]
    y, x = np.mgrid[:h, :w].astype(np.float32)
    u, v = x / (w - 1), y / (h - 1)
    edge = np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v))
    feather = np.clip(edge / .05, 0, 1)
    feather = feather * feather * (3 - 2 * feather)
    z = .02 * (1 - depth) * feather
    distance = 1.2
    factor = distance / (distance + z)
    px = eye + ((u - .5) - eye) * factor
    py = (v - .5) * factor
    ix = np.rint((px + .5) * (w - 1)).astype(np.int32)
    iy = np.rint((py + .5) * (h - 1)).astype(np.int32)
    valid = (ix >= 0) & (ix < w) & (iy >= 0) & (iy < h)
    target = (iy[valid] * w + ix[valid]).ravel()
    values, distances = image[valid], z[valid]
    ordered = np.lexsort((distances, target))
    unique = np.r_[True, target[ordered][1:] != target[ordered][:-1]]
    chosen = ordered[unique]
    output = np.zeros((h * w, 3), np.uint8)
    output[target[chosen]] = values[chosen]
    return output.reshape(h, w, 3), int(h * w - chosen.size)


def run(catalogue_file: Path, fields: Path, output: Path, count: int) -> dict:
    data = json.loads(catalogue_file.read_text())
    candidates = [art for art in data['artwork'] if (fields / (art['source_id'] + '.gmd')).exists()]
    groups = [lambda a: 'narrativeimages_cp/' in a['asset_path'].lower(),
              lambda a: 'forest' in a['asset_path'].lower(),
              lambda a: 'cave' in a['asset_path'].lower(),
              lambda a: a['kind'] == 'event-fallback',
              lambda a: a['kind'] == 'compendium']
    selected, used = [], set()
    for predicate in groups:
        for art in candidates:
            if predicate(art) and art['source_id'] not in used:
                selected.append(art); used.add(art['source_id']); break
    for art in candidates:
        if len(selected) >= count:
            break
        if art['source_id'] not in used:
            selected.append(art); used.add(art['source_id'])
    output.mkdir(parents=True, exist_ok=True)
    witnesses = []
    for art in selected:
        source = catalogue_file.parent / art['source_snapshot']
        with Image.open(source) as native:
            native = native.convert('RGB')
            target_width = min(640, native.width)
            thumbnail = native.resize((target_width, round(target_width * native.height / native.width)))
        image = np.asarray(thumbnail)
        depth = cv2.resize(field(fields / (art['source_id'] + '.gmd')), thumbnail.size, interpolation=cv2.INTER_LINEAR)
        colored = cv2.cvtColor(cv2.applyColorMap(np.rint(depth * 255).astype(np.uint8), cv2.COLORMAP_TURBO), cv2.COLOR_BGR2RGB)
        left, left_holes = oblique(image, depth, -.35)
        right, right_holes = oblique(image, depth, .35)
        w, h = thumbnail.size
        sheet = Image.new('RGB', (w * 2, (h + 24) * 2 + 24), '#171717')
        draw = ImageDraw.Draw(sheet)
        draw.text((5, 4), art['sprite_name'] + ' | 2%-width relief; supplied oblique eyes +/-0.35width', fill='white')
        for n, (pixels, caption) in enumerate(((image, 'Original native RGB'), (colored, 'Inferred inverse depth: blue=far, red=near'),
                                              (left, 'Left observation; uncovered points black'), (right, 'Right observation; uncovered points black'))):
            x, y = (n % 2) * w, 24 + (n // 2) * (h + 24)
            draw.text((x + 5, y + 3), caption, fill='white')
            sheet.paste(Image.fromarray(pixels), (x, y + 24))
        target = output / (art['source_id'] + '.png'); sheet.save(target)
        witnesses.append({'source_id': art['source_id'], 'asset_path': art['asset_path'],
            'source_rgba_sha256': art['rgba_sha256'], 'field_sha256': digest(fields / (art['source_id'] + '.gmd')),
            'witness': target.name, 'sha256': digest(target), 'oblique_uncovered_points': [left_holes, right_holes],
            'scope': 'Diagnostic CPU point projection of actual native RGB/depth, not production triangle raster or HMD evidence.'})
    result = {'schema': 1, 'source_catalogue_sha256': digest(catalogue_file), 'witnesses': witnesses}
    write_json(output / 'witnesses.json', result)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalogue', type=Path, required=True)
    parser.add_argument('--fields', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--count', type=int, default=6)
    args = parser.parse_args()
    result = run(args.catalogue, args.fields, args.out, args.count)
    print(json.dumps({'witnesses': len(result['witnesses'])}))
