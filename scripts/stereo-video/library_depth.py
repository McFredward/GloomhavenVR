#!/usr/bin/env python3
"""Pinned, resumable native-clock CPU depth for the inventoried movie library."""
from __future__ import annotations
import argparse
import json
import resource
import sys
import time
from pathlib import Path

import cv2
import numpy as np
from intro_depth import CODE_REVISION, MODEL_REVISION, WEIGHTS_SHA256, digest, save, validate_model


def validate_movie(movie: dict) -> None:
    if movie['fps'] not in (30, 60) or movie['dimensions'] != [1920, 1080]:
        raise ValueError('Only inventoried native 1080p/30 or 60 fps sources are supported')
    cursor = 0
    names = set()
    for shot in movie['shots']:
        if shot['id'] in names or shot['start'] != cursor or not cursor < shot['end'] <= movie['frames']:
            raise ValueError('Shots must uniquely partition every original frame')
        if shot['mode'] not in ('depth', 'screen', 'logo'):
            raise ValueError('Unknown shot mode')
        names.add(shot['id'])
        cursor = shot['end']
    if cursor != movie['frames']:
        raise ValueError('Shot partition lost the native ending')
    top, bottom = movie['body_rows']
    if not 0 <= top < bottom <= 1080:
        raise ValueError('Invalid original active-picture rows')


def source_for(movie: dict, data_root: Path, embedded: Path) -> Path:
    return data_root / movie['source_relative'] if movie['kind'] == 'external' else embedded / Path(movie['key']).name


def read_frames(source: Path, movie: dict, shot: dict) -> np.ndarray:
    cap = cv2.VideoCapture(str(source))
    try:
        if not cap.isOpened() or int(cap.get(cv2.CAP_PROP_FRAME_COUNT)) != movie['frames']:
            raise ValueError('Original frame inventory changed')
        cap.set(cv2.CAP_PROP_POS_FRAMES, shot['start'])
        result = []
        for frame in range(shot['start'], shot['end']):
            okay, image = cap.read()
            if not okay:
                raise ValueError(f'Missing original frame {frame}')
            image = cv2.resize(image, (640, 360), interpolation=cv2.INTER_AREA)
            result.append(cv2.cvtColor(image, cv2.COLOR_BGR2RGB))
        return np.stack(result)
    finally:
        cap.release()


def receipt_matches(info: dict, movie: dict, shot: dict) -> bool:
    return (info.get('source_sha256') == movie['source_sha256']
        and info.get('code_revision') == CODE_REVISION and info.get('model_revision') == MODEL_REVISION
        and info.get('checkpoint_sha256') == WEIGHTS_SHA256 and info.get('device') == 'cpu'
        and info.get('dtype') == 'float32' and info.get('input_size') == 392
        and info.get('temporal_window') == 32 and info.get('temporal_overlap') == 10
        and info.get('fps') == movie['fps'] and info.get('body_rows') == movie['body_rows']
        and (info.get('start'), info.get('end')) == (shot['start'], shot['end'])
        and info.get('depth_shape') == [shot['end'] - shot['start'], 360, 640])


def run(args) -> int:
    manifest = json.loads(args.manifest.read_text())
    movies = [m for m in manifest['movies'] if not m.get('root_owned') and (not args.movie or m['key'] in args.movie)]
    if args.movie and {m['key'] for m in movies} != set(args.movie):
        raise ValueError('Unknown or root-owned movie requested')
    validate_model(args.model_root)
    sys.path.insert(0, str(args.model_root))
    sys.dont_write_bytecode = True
    import torch
    from video_depth_anything.video_depth import VideoDepthAnything
    try:
        import xformers  # noqa: F401
    except ImportError:
        pass
    else:
        raise ValueError('CPU inference must not select xformers')
    torch.set_num_threads(args.threads)
    cv2.setNumThreads(1)
    model = VideoDepthAnything(encoder='vits', features=64, out_channels=[48, 96, 192, 384])
    model.load_state_dict(torch.load(args.model_root / 'checkpoints/video_depth_anything_vits.pth', map_location='cpu'), strict=True)
    model.eval()
    began = time.monotonic()
    receipts = []
    for movie in movies:
        validate_movie(movie)
        source = source_for(movie, args.data_root, args.embedded)
        if digest(source) != movie['source_sha256']:
            raise ValueError(f'Native source bytes changed: {movie["key"]}')
        directory = args.out / movie['key'].removesuffix('.mov')
        directory.mkdir(parents=True, exist_ok=True)
        for shot in movie['shots']:
            if shot['mode'] != 'depth':
                continue
            target, receipt = directory / (shot['id'] + '.npz'), directory / (shot['id'] + '.json')
            if receipt.exists():
                info = json.loads(receipt.read_text())
                if not receipt_matches(info, movie, shot) or digest(target) != info['depth_sha256']:
                    raise ValueError(f'Invalid resumable depth evidence: {receipt}')
                receipts.append(info)
                print(json.dumps({'resumed': str(receipt)}), flush=True)
                continue
            if target.exists():
                raise ValueError(f'Orphan depth bytes need explicit review before retry: {target}')
            started = time.monotonic()
            images = read_frames(source, movie, shot)
            depths, fps = model.infer_video_depth(images, movie['fps'], input_size=392, device='cpu', fp32=True)
            if depths.shape != (shot['end'] - shot['start'], 360, 640) or fps != movie['fps'] or not np.isfinite(depths).all():
                raise ValueError('Temporal inference changed original frame addresses or produced invalid values')
            top, bottom = movie['body_rows']
            body = depths[:, top // 3:(bottom + 2) // 3, :]
            low, high = np.percentile(body[:, ::4, ::4], (2, 98)).tolist()
            if high <= low:
                raise ValueError('Degenerate shot-wide depth normalization')
            temp = target.with_suffix('.pending.npz')
            np.savez_compressed(temp, depths=depths.astype(np.float16))
            temp.replace(target)
            info = {'movie': movie['key'], 'id': shot['id'], 'start': shot['start'], 'end': shot['end'],
                'frames': shot['end'] - shot['start'], 'fps': movie['fps'], 'body_rows': movie['body_rows'],
                'source_sha256': movie['source_sha256'], 'code_revision': CODE_REVISION,
                'model_revision': MODEL_REVISION, 'checkpoint_sha256': WEIGHTS_SHA256,
                'device': 'cpu', 'dtype': 'float32', 'input_size': 392,
                'temporal_window': 32, 'temporal_overlap': 10, 'threads': args.threads,
                'depth_shape': list(depths.shape), 'normalization_p02_p98': [low, high],
                'elapsed_seconds': round(time.monotonic() - started, 3),
                'process_peak_rss_kib': resource.getrusage(resource.RUSAGE_SELF).ru_maxrss,
                'depth_sha256': digest(target), 'torch': torch.__version__,
                'inference_source_sha256': digest(Path(__file__))}
            save(receipt, info)
            receipts.append(info)
            print(json.dumps(info), flush=True)
            save(args.out / 'progress.json', {'status': 'running', 'last_completed': info,
                'completed_depth_shots': len(receipts), 'elapsed_seconds': time.monotonic() - began,
                'manifest_sha256': digest(args.manifest)})
            del images, depths, body
    save(args.out / 'depth-manifest.json', {'shots': receipts, 'manifest_sha256': digest(args.manifest),
        'elapsed_seconds': time.monotonic() - began})
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=Path(__file__).with_name('library-manifest.json'))
    parser.add_argument('--data-root', type=Path, required=True)
    parser.add_argument('--embedded', type=Path, required=True)
    parser.add_argument('--model-root', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--threads', type=int, default=4)
    parser.add_argument('--movie', action='append')
    args = parser.parse_args()
    args.model_root = args.model_root.resolve()
    args.out.mkdir(parents=True, exist_ok=True)
    return run(args)

if __name__ == '__main__':
    raise SystemExit(main())
