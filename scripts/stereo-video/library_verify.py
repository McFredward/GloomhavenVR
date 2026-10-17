#!/usr/bin/env python3
"""Independent encoded-clock, native-YUV picture and original-AAC library gate."""
from __future__ import annotations
import argparse
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import cv2
import numpy as np
from intro_depth import validate_model
from library_common import (WIDTH, HEIGHT, audio_packets, digest, field_for,
    forward_warp, load_movie, read_depth, save, source_for)


def run(command: list[str], log: Path) -> bytes:
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    log.write_bytes(result.stderr)
    if result.returncode:
        raise RuntimeError(f'External verification failed: {log}')
    return result.stdout


def validate_probe(probe: dict, movie: dict) -> dict:
    video = [s for s in probe['streams'] if s['codec_type'] == 'video']
    if len(video) != 1:
        raise ValueError('Missing unique full-SBS video stream')
    video = video[0]
    if ([video['width'], video['height']] != [3840, 1080]
            or video.get('sample_aspect_ratio') != '1:1'
            or video['avg_frame_rate'] != f'{movie["fps"]}/1'
            or video['r_frame_rate'] != f'{movie["fps"]}/1'
            or video.get('codec_name') != 'h264' or video.get('pix_fmt') != 'yuv420p'
            or video.get('color_space') != 'bt709' or video.get('color_range') != 'tv'):
        raise ValueError('Native-clock full-SBS profile, pixel format or aspect changed')
    stamps = [float(f['best_effort_timestamp_time']) for f in probe['frames'] if f['media_type'] == 'video']
    if len(stamps) != movie['frames']:
        raise ValueError('Decoded original frames were duplicated or lost')
    if any(abs(t - n / movie['fps']) > 0.000001 for n, t in enumerate(stamps)):
        raise ValueError('Original per-frame timestamps changed')
    if abs(float(video['duration']) - movie['frames'] / movie['fps']) > 0.000001:
        raise ValueError('Original ending was retimed')
    return {'frames': len(stamps), 'fps': movie['fps'], 'dimensions': [3840, 1080],
            'sample_aspect_ratio': '1:1', 'every_original_timestamp': True}


def convert_reference(expected: np.ndarray, original: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    command = ['ffmpeg', '-v', 'error', '-threads', '1', '-filter_threads', '1',
        '-f', 'rawvideo', '-pixel_format', 'bgr24', '-video_size', '1920x1080', '-i', '-',
        '-vf', 'scale=flags=bicubic+accurate_rnd+full_chroma_int+full_chroma_inp:'
        'out_color_matrix=bt709,format=yuv420p', '-frames:v', '2', '-pix_fmt', 'yuv420p', '-f', 'rawvideo', '-']
    converted = subprocess.check_output(command, input=expected.tobytes() + original.tobytes())
    size = WIDTH * HEIGHT * 3 // 2
    if len(converted) != 2 * size:
        raise ValueError('Independent reference conversion lost an original witness')
    values = np.frombuffer(converted, np.uint8).astype(np.float32)
    return values[:size], values[size:]


def right_half_yuv(raw: bytes) -> np.ndarray:
    ysize, uvsize = WIDTH * 2 * HEIGHT, WIDTH * HEIGHT // 2
    planes = np.frombuffer(raw, np.uint8)
    return np.concatenate((planes[:ysize].reshape(HEIGHT, WIDTH * 2)[:, WIDTH:].ravel(),
        planes[ysize:ysize + uvsize].reshape(HEIGHT // 2, WIDTH)[:, WIDTH // 2:].ravel(),
        planes[ysize + uvsize:].reshape(HEIGHT // 2, WIDTH)[:, WIDTH // 2:].ravel())).astype(np.float32)


def verify_right(args, movie: dict, source: Path) -> dict:
    owned, errors, results = [], [], []
    endpoints = {}
    cv2.setNumThreads(1)
    try:
        for label, path in [('source', source), ('packed', args.stereo)]:
            command = ['ffmpeg', '-hide_banner', '-v', 'error', '-nostdin', '-threads', '2',
                '-i', str(path), '-map', '0:v:0', '-an']
            command += ['-vf', 'scale=in_color_matrix=bt709:in_range=tv', '-pix_fmt', 'bgr24'] if label == 'source' else ['-pix_fmt', 'yuv420p']
            command += ['-fps_mode', 'passthrough', '-f', 'rawvideo', '-']
            error = (args.out / ('right-' + label + '-decode.log')).open('wb')
            errors.append(error)
            owned.append(subprocess.Popen(command, stdout=subprocess.PIPE, stderr=error, start_new_session=True))
        original_decoder, packed_decoder = owned
        directory = args.depths / movie['key'].removesuffix('.mov')
        for shot in movie['shots']:
            data, info = read_depth(directory, movie, shot) if shot['mode'] == 'depth' else (None, None)
            witnesses = {shot['start'], (shot['start'] + shot['end']) // 2, shot['end'] - 1}
            for frame in range(shot['start'], shot['end']):
                original_bytes = original_decoder.stdout.read(WIDTH * HEIGHT * 3)
                packed_bytes = packed_decoder.stdout.read(WIDTH * 2 * HEIGHT * 3 // 2)
                if len(original_bytes) != WIDTH * HEIGHT * 3 or len(packed_bytes) != WIDTH * 2 * HEIGHT * 3 // 2:
                    raise ValueError('Original or packed right-eye decoder lost a frame')
                if frame not in witnesses:
                    continue
                original = np.frombuffer(original_bytes, np.uint8).reshape(HEIGHT, WIDTH, 3)
                field = field_for(data[frame - shot['start']] if data is not None else None, original, movie, shot, info)
                expected = original if shot['mode'] == 'screen' else forward_warp(original, field)[0]
                reference, duplicate = convert_reference(expected, original)
                actual = right_half_yuv(packed_bytes)
                mse = float(np.mean((actual - reference) ** 2))
                duplicate_mse = float(np.mean((actual - duplicate) ** 2))
                separation = float(np.mean((reference - duplicate) ** 2))
                psnr = 10 * math.log10(255 ** 2 / mse) if mse else 999.0
                if psnr < 34:
                    raise ValueError(f'Right encoding/frame correspondence below34dB at {frame}: {psnr:.3f}')
                if separation > 20 and duplicate_mse <= mse * 2:
                    raise ValueError(f'Right view is consistent with a duplicated left at {frame}')
                for x0, y0, x1, y1 in shot.get('screen_regions', []):
                    if np.any(field[y0:y1, x0:x1]):
                        raise ValueError('Native printed title region acquired stereo distortion')
                result = {'frame': frame, 'shot': shot['id'], 'metric_space': 'native-YUV420',
                    'right_reference_psnr_db': psnr, 'right_vs_duplicate_left_mse': duplicate_mse,
                    'expected_stereo_difference_mse': separation,
                    'disparity_bounds_px': [float(field.min()), float(field.max())]}
                results.append(result)
                if frame in (0, movie['frames'] - 1):
                    endpoints[str(frame)] = {'original_rgb_mean': float(original.mean()),
                        'disparity_bounds_px': result['disparity_bounds_px'], 'right_psnr_db': psnr}
        for process in owned:
            if process.stdout.read(1) or process.wait(timeout=60):
                raise ValueError('Extra decoded frame or verification decoder failure')
        if movie['key'].startswith('Ambient/') and any(max(abs(v) for v in e['disparity_bounds_px']) > 0.001 for e in endpoints.values()):
            raise ValueError('Native ambient black-loop endpoints acquired nonzero depth')
        return {'witnesses': results, 'native_loop_endpoints': endpoints}
    finally:
        for process in owned:
            if process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                finally:
                    process.wait()
        for error in errors:
            error.close()


def verify(args) -> dict:
    _, movie = load_movie(args.manifest, args.movie)
    source = source_for(movie, args.data_root, args.embedded)
    if digest(source) != movie['source_sha256']:
        raise ValueError('Source movie differs from native inventory')
    if args.stereo.stat().st_size > 100 * 1024 ** 2:
        raise ValueError('Movie exceeds repository single-file delivery limit')
    validate_model(args.model_root.resolve())
    depth_proof = []
    directory = args.depths / movie['key'].removesuffix('.mov')
    for shot in movie['shots']:
        if shot['mode'] == 'depth':
            data, info = read_depth(directory, movie, shot)
            depth_proof.append({'id': shot['id'], 'depth_sha256': info['depth_sha256'],
                'receipt_sha256': digest(directory / (shot['id'] + '.json'))})
            del data
    save(args.out / 'depth-validation.json', depth_proof)
    probe_command = ['ffprobe', '-v', 'error', '-count_frames', '-show_streams', '-show_frames',
        '-show_entries', 'frame=media_type,best_effort_timestamp_time', '-of', 'json', str(args.stereo)]
    probe = json.loads(run(probe_command, args.out / 'probe.log'))
    save(args.out / 'probe.json', probe)
    video_proof = validate_probe(probe, movie)
    original_audio, stereo_audio = audio_packets(source), audio_packets(args.stereo)
    if original_audio != stereo_audio:
        raise ValueError('Original AAC packet payloads, timestamps or durations changed')
    save(args.out / 'audio-packets.json', stereo_audio)
    stats = args.out.resolve() / 'left-psnr.log'
    command = ['ffmpeg', '-hide_banner', '-v', 'info', '-nostdin', '-threads', '2', '-i', str(args.stereo),
        '-threads', '2', '-i', str(source), '-filter_complex_threads', '1', '-filter_complex',
        f'[0:v]crop=1920:1080:0:0[left];[left][1:v]psnr=stats_file={stats}[check]',
        '-map', '[check]', '-an', '-f', 'null', '-']
    run(command, args.out / 'left-compare.log')
    values = [float(x) for x in re.findall(r'psnr_avg:([0-9.inf]+)', stats.read_text())]
    if len(values) != movie['frames'] or min(values) < 34:
        raise ValueError(f'Original left picture or ending below34dB: {min(values) if values else "no frames"}')
    right = verify_right(args, movie, source)
    save(args.out / 'right-frame-validation.json', right)
    proof = {'status': 'passed', 'movie': movie['key'], 'source_sha256': movie['source_sha256'],
        'stereo_sha256': digest(args.stereo), 'stereo_bytes': args.stereo.stat().st_size,
        'video': video_proof, 'identical_aac_packets': len(stereo_audio),
        'left_comparison': {'frames': len(values), 'minimum_psnr_db': min(values)},
        'depth_shots_validated': len(depth_proof), 'encoded_right_witness_frames': len(right['witnesses']),
        'minimum_right_witness_psnr_db': min(x['right_reference_psnr_db'] for x in right['witnesses']),
        'native_loop_endpoints': right['native_loop_endpoints'],
        'verification_source_sha256': digest(Path(__file__)), 'manifest_sha256': digest(args.manifest),
        'hardware_result': 'unverified'}
    save(args.out / 'result.json', proof)
    print(json.dumps(proof), flush=True)
    return proof


def main() -> int:
    def interrupted(_signum, _frame):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=Path(__file__).with_name('library-manifest.json'))
    parser.add_argument('--movie', required=True)
    parser.add_argument('--data-root', type=Path, required=True)
    parser.add_argument('--embedded', type=Path, required=True)
    parser.add_argument('--depths', type=Path, required=True)
    parser.add_argument('--model-root', type=Path, required=True)
    parser.add_argument('--stereo', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    code = 1
    try:
        verify(args)
        code = 0
        return code
    finally:
        root = Path(__file__).resolve().parents[2]
        sys.path.insert(0, str(root / 'scripts'))
        from town_run_retention import cleanup_completed_run
        log = args.out / 'completion.log'
        log.write_text(f'All owned media decoders stopped and reaped; exit={code}.\nevidence: {args.out.resolve()}\n')
        save(args.out / 'cleanup.json', cleanup_completed_run('stereo-video-library', code, log.resolve(), root, process_reaped=True))

if __name__ == '__main__':
    raise SystemExit(main())
