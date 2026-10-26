#!/usr/bin/env python3
"""Static narrative-art depth from the existing pinned Video Depth Anything Small model."""
from __future__ import annotations

import argparse
import gc
import hashlib
import json
from pathlib import Path
import resource
import sys
import time

import cv2
import numpy as np
from PIL import Image

from catalogue import digest, write_json
from heightfield import decode, encode

VIDEO_SCRIPTS = Path(__file__).resolve().parents[1] / 'stereo-video'
sys.path.insert(0, str(VIDEO_SCRIPTS))
from intro_depth import CODE_REVISION, MODEL_REVISION, WEIGHTS_SHA256, validate_model

PROFILE = {
    'name': 'static-native-art-v1', 'device': 'cpu', 'dtype': 'float32',
    'input_size': 392, 'frame_count': 1, 'temporal_history': False,
    'maximum_field_axis': 512, 'normalization_percentiles': [2, 98],
    'filter': {'method': 'bilateral', 'diameter': 5, 'sigmaColor': 0.08, 'sigmaSpace': 1.2},
    'orientation': 'bottom-left', 'range': 'normalized-inverse-depth-near1-far0',
    'edge_feather': 'Applied to geometry at runtime; not baked into the scalar field',
}


def image_size(width: int, height: int) -> tuple[int, int]:
    scale = PROFILE['maximum_field_axis'] / max(width, height)
    return max(1, round(width * scale)), max(1, round(height * scale))


def normalized(raw: np.ndarray, alpha: np.ndarray, output_size: tuple[int, int]) -> tuple[np.ndarray, dict]:
    if raw.ndim != 2 or raw.shape != alpha.shape or not np.isfinite(raw).all():
        raise ValueError('Invalid native depth inference dimensions or values')
    population = raw[alpha >= 128]
    if population.size < 16:
        raise ValueError('Native picture has insufficient visible pixels')
    low, high = (float(value) for value in np.percentile(population, PROFILE['normalization_percentiles']))
    if high - low <= 1e-6:
        raise ValueError('Degenerate inferred depth requires explicit source review')
    depth = np.clip((raw - low) / (high - low), 0, 1).astype(np.float32)
    depth = cv2.resize(depth, output_size, interpolation=cv2.INTER_AREA)
    depth = cv2.bilateralFilter(depth, 5, 0.08, 1.2)
    if not np.isfinite(depth).all() or depth.min() < 0 or depth.max() > 1.000001:
        raise ValueError('Depth filtering produced invalid data')
    # Quantize in float64 so float32 multiply rounding cannot cross a half-step.
    quantized = np.rint(np.clip(depth.astype(np.float64), 0, 1) * 65535).astype('<u2')
    # Native pictures are top-down; Unity samples heightfields from bottom-left.
    quantized = np.ascontiguousarray(quantized[::-1])
    reconstructed = quantized[::-1].astype(np.float64) / 65535
    error = float(np.max(np.abs(reconstructed - depth)))
    if error > 0.5001 / 65535:
        raise ValueError('Heightfield quantization exceeded half an integer step')
    return quantized, {'normalization_p02_p98': [low, high], 'minimum': int(quantized.min()),
        'maximum': int(quantized.max()), 'standard_deviation': float(np.std(depth)),
        'maximum_quantization_error': error}


def load_model(model_root: Path, threads: int):
    validate_model(model_root)
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(model_root))
    import torch
    from video_depth_anything.video_depth import VideoDepthAnything
    try:
        import xformers  # noqa: F401
    except ImportError:
        pass
    else:
        raise ValueError('Pinned CPU profile must not select xformers')
    torch.set_num_threads(threads)
    cv2.setNumThreads(1)
    model = VideoDepthAnything(encoder='vits', features=64, out_channels=[48, 96, 192, 384])
    model.load_state_dict(torch.load(model_root / 'checkpoints/video_depth_anything_vits.pth', map_location='cpu'), strict=True)
    model.eval()
    return model, torch


def infer_image(model, torch, rgba: np.ndarray) -> np.ndarray:
    """One original still is one frame; no unrelated illustrations share temporal attention."""
    from torchvision.transforms import Compose
    from video_depth_anything.util.transform import NormalizeImage, PrepareForNet, Resize
    height, width = rgba.shape[:2]
    input_size = PROFILE['input_size']
    ratio = max(height, width) / min(height, width)
    if ratio > 1.78:
        input_size = round(int(input_size * 1.777 / ratio) / 14) * 14
    transform = Compose([
        Resize(width=input_size, height=input_size, resize_target=False, keep_aspect_ratio=True,
               ensure_multiple_of=14, resize_method='lower_bound', image_interpolation_method=cv2.INTER_CUBIC),
        NormalizeImage(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
        PrepareForNet(),
    ])
    # Alpha padding is black for model context, while original color remains untouched.
    rgb = rgba[:, :, :3].astype(np.float32) * (rgba[:, :, 3:4].astype(np.float32) / 255)
    tensor = torch.from_numpy(transform({'image': rgb / 255})['image']).unsqueeze(0).unsqueeze(0)
    with torch.no_grad():
        result = model(tensor)[0, 0].cpu().numpy()
    result = cv2.resize(result, (width, height), interpolation=cv2.INTER_LINEAR)
    if result.shape != (height, width) or not np.isfinite(result).all():
        raise ValueError('Single-image model forward did not preserve native aspect')
    return result


def run(args) -> dict:
    catalogue = json.loads(args.catalogue.read_text())
    unique = {}
    for art in catalogue['artwork']:
        unique.setdefault(art['source_id'], art)
    model, torch = load_model(args.model_root, args.threads)
    args.out.mkdir(parents=True, exist_ok=True)
    args.evidence.mkdir(parents=True, exist_ok=True)
    began = time.monotonic()
    rows = []
    for number, (key, art) in enumerate(sorted(unique.items())):
        if args.limit and number >= args.limit:
            break
        source = args.catalogue.parent / art['source_snapshot']
        if digest(source) != art['source_png_sha256']:
            raise ValueError('Source snapshot changed: ' + str(source))
        with Image.open(source) as image:
            rgba = np.asarray(image.convert('RGBA'))
        if hashlib.sha256(rgba.tobytes()).hexdigest() != art['rgba_sha256']:
            raise ValueError('Decoded native picture pixels changed')
        width, height = art['logical_dimensions']
        if rgba.shape != (height, width, 4):
            raise ValueError('Original logical sprite coordinates changed')
        target = args.out / (key + '.gmd')
        receipt = args.evidence / (key + '.json')
        if target.exists() or receipt.exists():
            if not target.exists() or not receipt.exists():
                raise ValueError('Orphan output needs explicit review: ' + key)
            previous = json.loads(receipt.read_text())
            if previous['source_rgba_sha256'] != art['rgba_sha256'] or previous['depth_sha256'] != digest(target) or previous['profile'] != PROFILE:
                raise ValueError('Invalid resumable depth receipt: ' + key)
            decode(target.read_bytes())
            rows.append(previous)
            print(json.dumps({'resumed': key}), flush=True)
            continue
        started = time.monotonic()
        raw = infer_image(model, torch, rgba)
        field, metrics = normalized(raw, rgba[:, :, 3], image_size(width, height))
        encoded = encode(field.shape[1], field.shape[0], width, height, field.tobytes())
        decoded = decode(encoded)
        if decoded['payload'] != field.tobytes():
            raise ValueError('Actual GMD1 payload lost source orientation/data')
        raw_path = args.evidence / (key + '.npz')
        np.savez_compressed(raw_path, raw=raw.astype(np.float32))
        pending = target.with_suffix('.pending')
        pending.write_bytes(encoded)
        pending.replace(target)
        info = {'source_id': key, 'source_rgba_sha256': art['rgba_sha256'],
            'source_png_sha256': art['source_png_sha256'], 'source_dimensions': [width, height],
            'depth_file': target.name, 'depth_dimensions': [field.shape[1], field.shape[0]],
            'depth_bytes': len(encoded), 'depth_sha256': digest(target),
            'raw_inference_sha256': digest(raw_path), 'profile': PROFILE,
            'code_revision': CODE_REVISION, 'model_revision': MODEL_REVISION,
            'checkpoint_sha256': WEIGHTS_SHA256, 'torch_version': torch.__version__,
            'threads': args.threads, 'metrics': metrics,
            'elapsed_seconds': round(time.monotonic() - started, 3),
            'process_peak_rss_kib': resource.getrusage(resource.RUSAGE_SELF).ru_maxrss,
            'generator_sha256': digest(Path(__file__))}
        write_json(receipt, info)
        rows.append(info)
        write_json(args.evidence / 'progress.json', {'status': 'running', 'completed': len(rows),
            'expected': len(unique), 'last': key, 'elapsed_seconds': time.monotonic() - began})
        print(json.dumps({'completed': len(rows), 'source': key, 'seconds': info['elapsed_seconds']}), flush=True)
        del rgba, raw, field
    complete = len(rows) == len(unique)
    result = {'schema': 1, 'status': 'complete' if complete else 'limited-proof', 'format': 'GMD1',
        'source_catalogue_sha256': digest(args.catalogue), 'profile': PROFILE,
        'images': rows, 'elapsed_seconds': time.monotonic() - began,
        'narrative_configs': catalogue['narrative_configs'], 'artwork': catalogue['artwork'],
        'source_bundles': catalogue['source_bundles']}
    if complete:
        write_json(args.out / 'manifest.json', result)
    write_json(args.evidence / 'inference-result.json', result)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalogue', type=Path, required=True)
    parser.add_argument('--model-root', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--threads', type=int, default=4)
    parser.add_argument('--limit', type=int, default=0)
    args = parser.parse_args()
    args.catalogue, args.model_root = args.catalogue.resolve(), args.model_root.resolve()
    result = run(args)
    print(json.dumps({'status': result['status'], 'images': len(result['images']),
                      'elapsed_seconds': result['elapsed_seconds']}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
