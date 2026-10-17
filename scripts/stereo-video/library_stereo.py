#!/usr/bin/env python3
"""Render a pinned library subject into native-clock full SBS, left first."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import signal
import shutil
import subprocess
import time
import cv2
import numpy as np
from library_common import (WIDTH, HEIGHT, audio_packets, digest, field_for,
    forward_warp, load_movie, read_depth, save, source_for)


def render(args) -> Path:
    _, movie = load_movie(args.manifest, args.movie)
    source = source_for(movie, args.data_root, args.embedded)
    if digest(source) != movie['source_sha256']:
        raise ValueError('Native source bytes changed')
    args.out.mkdir(parents=True, exist_ok=False)
    target = args.out / Path(movie['stereo_key']).name
    frames, fps = movie['frames'], movie['fps']
    command = ['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-n',
        '-f', 'rawvideo', '-pixel_format', 'bgr24', '-video_size', '1920x1080',
        '-framerate', str(fps), '-i', 'pipe:0', '-threads', '2', '-i', str(source),
        '-filter_complex_threads', '1', '-filter_complex',
        '[0:v]scale=flags=bicubic+accurate_rnd+full_chroma_int+full_chroma_inp:'
        'out_color_matrix=bt709,format=yuv420p,setsar=1[right];'
        '[1:v][right]hstack=inputs=2,setsar=1[packed]',
        '-map', '[packed]', '-map', '1:a:0?', '-frames:v', str(frames),
        '-c:v', 'libx264', '-threads', '3', '-preset', 'fast', '-crf', str(args.crf),
        '-maxrate', '17M', '-bufsize', '34M', '-profile:v', 'high', '-level:v', '5.1',
        '-g', str(fps), '-keyint_min', '1', '-video_track_timescale', str(fps * 256),
        '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', '-color_range', 'tv',
        '-c:a', 'copy', '-map_metadata', '-1', '-metadata',
        'title=GloomhavenVR - ' + movie['key'] + ' (Full SBS left first)',
        '-movflags', '+faststart', str(target)]
    decode = ['ffmpeg', '-hide_banner', '-v', 'error', '-nostdin', '-threads', '2',
        '-i', str(source), '-map', '0:v:0', '-an', '-vf',
        'scale=in_color_matrix=bt709:in_range=tv', '-pix_fmt', 'bgr24',
        '-fps_mode', 'passthrough', '-f', 'rawvideo', '-']
    save(args.out / 'command.json', command)
    save(args.out / 'decode-command.json', decode)
    save(args.out / 'movie-snapshot.json', movie)
    sources = args.out / 'sources'
    sources.mkdir()
    for filename in ('library_stereo.py', 'library_common.py', 'library_depth.py', 'intro_stereo.py', 'intro_depth.py'):
        shutil.copy2(Path(__file__).with_name(filename), sources / filename)
    records, owned, errors = [], [], []
    began = time.monotonic()
    cv2.setNumThreads(1)
    try:
        decoder_error = (args.out / 'decode.log').open('wb')
        errors.append(decoder_error)
        decoder = subprocess.Popen(decode, stdout=subprocess.PIPE, stderr=decoder_error, start_new_session=True)
        owned.append(decoder)
        encode_error = (args.out / 'encode.log').open('wb')
        errors.append(encode_error)
        encoder = subprocess.Popen(command, stdin=subprocess.PIPE, stderr=encode_error, stdout=subprocess.DEVNULL, start_new_session=True)
        owned.append(encoder)
        directory = args.depths / movie['key'].removesuffix('.mov')
        for shot in movie['shots']:
            data, info = read_depth(directory, movie, shot, args.wait_depth) if shot['mode'] == 'depth' else (None, None)
            record = {'id': shot['id'], 'start': shot['start'], 'end': shot['end'], 'mode': shot['mode'],
                'holes_filled': 0, 'maximum_hole_fraction': 0.0, 'minimum_disparity_px': 0.0,
                'maximum_disparity_px': 0.0, 'screen_regions': shot.get('screen_regions', [])}
            witnesses = {shot['start'], (shot['start'] + shot['end']) // 2, shot['end'] - 1}
            for index in range(shot['start'], shot['end']):
                raw = decoder.stdout.read(WIDTH * HEIGHT * 3)
                if len(raw) != WIDTH * HEIGHT * 3:
                    raise ValueError(f'Original decoder lost frame {index}')
                original = np.frombuffer(raw, np.uint8).reshape(HEIGHT, WIDTH, 3)
                field = field_for(data[index - shot['start']] if data is not None else None,
                                  original, movie, shot, info)
                right, holes = (original, 0) if shot['mode'] == 'screen' else forward_warp(original, field)
                encoder.stdin.write(right.tobytes())
                record['holes_filled'] += holes
                record['maximum_hole_fraction'] = max(record['maximum_hole_fraction'], holes / (WIDTH * HEIGHT))
                record['minimum_disparity_px'] = min(record['minimum_disparity_px'], float(field.min()))
                record['maximum_disparity_px'] = max(record['maximum_disparity_px'], float(field.max()))
                if index in (0, frames - 1):
                    record['endpoint_disparity_px_' + str(index)] = [float(field.min()), float(field.max())]
                if index in witnesses:
                    cv2.imwrite(str(args.out / f'source-{index:05d}.jpg'), np.hstack((original, right)))
            records.append(record)
            save(args.out / 'geometry.json', records)
            print(json.dumps(record), flush=True)
            del data
        if decoder.stdout.read(1) or decoder.wait(timeout=60):
            raise ValueError('Original decoder produced extra frames or failed')
        encoder.stdin.close()
        if encoder.wait(timeout=600):
            raise RuntimeError('Full SBS encoder failed; see encode.log')
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
    if target.stat().st_size > 100 * 1024 ** 2:
        raise ValueError('Delivered movie exceeds repository single-file limit; do not weaken quality to fit')
    original_audio, stereo_audio = audio_packets(source), audio_packets(target)
    if original_audio != stereo_audio:
        raise ValueError('Native AAC packet payloads, timestamps or durations changed')
    proof = {'status': 'rendered-not-yet-independent-verification', 'movie': movie['key'],
        'source_sha256': movie['source_sha256'], 'output_sha256': digest(target), 'output_bytes': target.stat().st_size,
        'packing': 'full-SBS-left-first', 'per_eye_size': [WIDTH, HEIGHT], 'frames': frames, 'fps': fps,
        'duration_seconds': frames / fps, 'identical_aac_packets': len(stereo_audio),
        'left': 'original native YUV directly hstacked before H264 encoding',
        'right': 'shot-wide temporal inverse-depth horizontal forward reprojection for depth scenes; uniform+8px original studio plane; screen-plane protected titles',
        'shot_modes': sorted({shot['mode'] for shot in movie['shots']}),
        'disocclusion_fill': 'farther adjoining original background, no generative inpainting',
        'depth_bounds_px': [-4, 12], 'manifest_sha256': digest(args.manifest),
        'render_source_sha256': digest(Path(__file__)), 'geometry_source_sha256': digest(Path(__file__).with_name('library_common.py')),
        'elapsed_seconds': time.monotonic() - began, 'hardware_result': 'unverified'}
    save(args.out / 'receipt.json', proof)
    print(json.dumps(proof), flush=True)
    return target


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
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--wait-depth', action='store_true')
    parser.add_argument('--crf', type=int, default=17)
    args = parser.parse_args()
    print(render(args))
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
