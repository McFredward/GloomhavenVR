#!/usr/bin/env python3
"""Render/decode the assembler's real graph against tiny, distinct color plates.

Checks all 14810 frames of the gameplay delivery clock, including all 20 selected
clip windows, adjacent frame4088, unchanged gaps and a three-frame still graphic.
No game assets, network, third-party Python packages or production render needed.
Only fixture dimensions and final branding are excluded; placement, normalization,
overlays, encoding and output timestamps use the imported build_command unchanged.

Usage: python3 scripts/trailer/check_assembly_boundaries.py --out-dir NEW_DIRECTORY
       ... --assembler /other/checkout/scripts/trailer/assemble_reference_remake.py
"""
from __future__ import annotations

import argparse
from fractions import Fraction
import hashlib
import importlib.util
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
FPS, FRAMES, WIDTH, HEIGHT = 60, 14810, 32, 18
# Delivery fixture addresses, not an implementation-derived enable expression.
WINDOWS = (
    (3129, 3272), (3272, 3485), (3485, 3583), (3776, 4088),
    (4088, 4250), (4250, 4359), (4534, 4779), (4779, 4965),
    (4965, 5112), (5549, 5690), (5690, 5749), (5749, 5910),
    (5910, 6067), (6213, 6413), (7090, 7234), (7234, 7554),
    (11062, 11359), (11861, 11952), (11952, 12189), (12522, 13032),
)


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def run(command: list[str], directory: Path, name: str) -> bytes:
    process = subprocess.Popen(command, cwd=directory, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, start_new_session=True)
    try:
        stdout, stderr = process.communicate(timeout=120)
    except BaseException:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait()
        raise
    (directory / (name + '.log')).write_bytes(stderr)
    if process.returncode:
        raise RuntimeError(f'{name} failed ({process.returncode}): {stderr[-2000:].decode(errors="replace")}')
    return stdout


def check(args: argparse.Namespace, directory: Path) -> dict:
    assembler_path = args.assembler.resolve(strict=True)
    spec = importlib.util.spec_from_file_location('assembly_under_test', assembler_path)
    assembler = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(assembler)
    assembler.WIDTH, assembler.HEIGHT = WIDTH, HEIGHT
    if assembler.FPS != FPS:
        raise RuntimeError('This delivery-clock fixture requires the production 60fps clock')
    ffmpeg, ffprobe = shutil.which('ffmpeg'), shutil.which('ffprobe')
    if not ffmpeg or not ffprobe:
        raise RuntimeError('Local ffmpeg and ffprobe are required')
    prefix = [ffmpeg, '-nostdin', '-v', 'error', '-threads', '1', '-filter_threads', '1', '-n']
    palette = ['#080808'] + ['#%02x%02x%02x' % rgb for rgb in
                             itertools.islice(itertools.product((32, 128, 224), repeat=3), 20)]
    inputs, expected_colors = [], []
    counts = [FRAMES] + [last - first for first, last in WINDOWS]
    for i, (color, count) in enumerate(zip(palette, counts)):
        path = directory / f'input-{i:02}.mkv'
        command = prefix + ['-f', 'lavfi', '-i', f'color=c={color}:s={WIDTH}x{HEIGHT}:r={FPS}']
        if i == 0:
            command += ['-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=mono',
                        '-t', f'{FRAMES / FPS:.9f}', '-c:a', 'aac', '-b:a', '32k']
        command += ['-frames:v', str(count), '-c:v', 'ffv1', '-threads', '1', str(path)]
        run(command, directory, f'input-{i:02}')
        decoded = run([ffmpeg, '-v', 'error', '-threads', '1', '-i', str(path),
                       '-frames:v', '1', '-pix_fmt', 'rgb24', '-f', 'rawvideo', '-'],
                      directory, f'input-color-{i:02}')
        expected_colors.append(tuple(decoded[:3]))
        inputs.append(path)
    graphic = directory / 'graphic.ppm'
    graphic.write_bytes(f'P6\n{WIDTH} {HEIGHT}\n255\n'.encode() + bytes((255, 0, 255)) * WIDTH * HEIGHT)
    expected_colors.append((255, 0, 255))
    owner = [0] * FRAMES
    clips = []
    for i, (first, last) in enumerate(WINDOWS, 1):
        owner[first:last] = [i] * (last - first)
        clips.append({'id': f'clip-{i:02}', 'path': str(inputs[i]),
                      'start_frame': first, 'end_frame_exclusive': last,
                      'frame_count': last - first, 'duration_seconds': (last - first) / FPS,
                      'input_span_seconds': (last - first) / FPS, 'in_seconds': 0,
                      'pts_multiplier': 1, 'timing': 'trim'})
    owner[14000:14003] = [21] * 3
    plan = {'ffmpeg': ffmpeg, 'source': str(inputs[0]), 'threads': 1,
            'expected_video_frames': FRAMES, 'clips': clips,
            'graphics': [{'path': str(graphic), 'start_frame': 14000,
                          'end_frame_exclusive': 14003, 'frame_count': 3,
                          'duration_seconds': 3 / FPS}],
            'footer_height': 0, 'audio_mode': 'copy', 'crf': 0, 'preset': 'ultrafast'}
    graph, command = assembler.build_command(plan)
    # Exclude ONLY the final branding operation, which would obscure tiny plates.
    graph, substitutions = re.subn(r'(\[[^\]]+\])drawtext=[^;]+,format=yuv420p\[outv\]\s*$',
                                   r'\1format=yuv420p[outv]\n', graph)
    if substitutions != 1:
        raise RuntimeError('Cannot isolate final branding; refusing to alter the placement graph')
    (directory / 'filter.ffgraph').write_text(graph)
    (directory / 'fixture.json').write_text(json.dumps({'assembler': str(assembler_path),
        'assembler_sha256': digest(assembler_path), 'plan': plan, 'render_command': command,
        'palette_rgb_decoded_from_inputs': expected_colors,
        'checker_sha256': digest(Path(__file__)),
        'synthetic_input_sha256': {str(path): digest(path) for path in inputs + [graphic]},
        'scope': '32x18, final branding omitted, production 60fps and actual build_command placement'}, indent=2) + '\n')
    run(command, directory, 'render')
    output = directory / 'render.partial.mp4'
    metadata = json.loads(run([ffprobe, '-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'stream=width,height,avg_frame_rate,time_base,nb_frames:frame=best_effort_timestamp',
        '-of', 'json', str(output)], directory, 'timestamps'))
    stream = metadata['streams'][0]
    times = [int(frame['best_effort_timestamp']) * Fraction(stream['time_base'])
             for frame in metadata['frames']]
    time_errors = [i for i, stamp in enumerate(times) if stamp != Fraction(i, FPS)]
    decoded = run([ffmpeg, '-v', 'error', '-threads', '1', '-i', str(output),
                   '-map', '0:v:0', '-vsync', '0', '-pix_fmt', 'rgb24', '-f', 'rawvideo', '-'],
                  directory, 'decode')
    frame_bytes = WIDTH * HEIGHT * 3
    decoded_count, partial = divmod(len(decoded), frame_bytes)
    mismatches, mismatch_count = [], 0
    # Every actual decoded pixel must belong to its prescribed color plate.
    # Tolerance covers ordinary YUV/RGB roundtrip rounding, not another plate.
    for index in range(min(decoded_count, FRAMES)):
        image = decoded[index * frame_bytes:(index + 1) * frame_bytes]
        expected = expected_colors[owner[index]]
        extrema = [(min(image[channel::3]), max(image[channel::3])) for channel in range(3)]
        error = max(abs(bound - value) for value, bounds in zip(expected, extrema) for bound in bounds)
        if error > 6:
            mismatch_count += 1
            if len(mismatches) < 40:
                mismatches.append({'frame': index, 'expected_input': owner[index],
                                   'expected_rgb': expected, 'actual_rgb_extrema': extrema})
    proof_frames = sorted({4087, 4088, 4089, 13999, 14000, 14002, 14003} |
                          {item['frame'] for item in mismatches})
    for index in proof_frames:
        if index < decoded_count:
            header = f'P6\n{WIDTH} {HEIGHT}\n255\n'.encode()
            actual = decoded[index * frame_bytes:(index + 1) * frame_bytes]
            (directory / f'actual-{index:05}.ppm').write_bytes(header + actual)
    checks = {'decoded_frame_count': decoded_count == FRAMES and not partial,
              'metadata_frame_count': int(stream['nb_frames']) == FRAMES,
              'size': [stream['width'], stream['height']] == [WIDTH, HEIGHT],
              'fps': Fraction(stream['avg_frame_rate']) == FPS,
              'all_frame_timestamps': len(times) == FRAMES and not time_errors,
              'all_decoded_pixels': mismatch_count == 0}
    return {'status': 'passed' if all(checks.values()) else 'failed',
            'assembler': str(assembler_path), 'assembler_sha256': digest(assembler_path),
            'checks': checks, 'decoded_frames': decoded_count, 'mismatch_frames': mismatch_count,
            'first_mismatches': mismatches, 'first_timestamp_errors': time_errors[:40],
            'clip_windows': WINDOWS, 'graphic_window': [14000, 14003],
            'actual_pixel_proof_frames': proof_frames,
            'unchanged_outside_frames': owner.count(0), 'output_sha256': digest(output),
            'scope': 'Actual tiny fixture graph/render/decode. Branding excluded; no production media/visual-quality/audio-parity claim.',
            'processes_reaped': True}


def interrupted(signum: int, frame: object) -> None:
    raise KeyboardInterrupt(f'Interrupted by signal {signum}')


def main() -> int:
    signal.signal(signal.SIGTERM, interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assembler', type=Path, default=Path(__file__).with_name('assemble_reference_remake.py'))
    parser.add_argument('--out-dir', type=Path, required=True, help='New private evidence directory; never overwritten')
    args = parser.parse_args()
    directory = args.out_dir.resolve()
    directory.mkdir(parents=True, exist_ok=False)
    code = 1
    try:
        result = check(args, directory)
        code = 0 if result['status'] == 'passed' else 1
    except (Exception, KeyboardInterrupt) as error:
        code = 130 if isinstance(error, KeyboardInterrupt) else 1
        result = {'status': 'failed', 'error': str(error), 'processes_reaped': True}
    finally:
        # These are only this invocation's regenerable synthetic media. Preserve
        # manifests, logs, timestamps and actual pixel witnesses, even on failure.
        removed = []
        for path in list(directory.glob('input-[0-9][0-9].mkv')) + [
                directory / 'graphic.ppm', directory / 'render.partial.mp4']:
            if path.is_file() and not path.is_symlink():
                info = path.stat()
                removed.append({'path': str(path), 'allocated_bytes': info.st_blocks * 512})
                path.unlink()
        result['synthetic_media_cleanup'] = {'removed': removed,
            'allocated_bytes': sum(item['allocated_bytes'] for item in removed)}
        (directory / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
        log = directory / 'completion.log'
        log.write_text('processes_reaped=true\nevidence: ' + str(directory / 'result.json') + '\n')
        retention = ROOT / 'scripts/town_run_retention.py'
        spec = importlib.util.spec_from_file_location('boundary_run_retention', retention)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        cleanup = module.cleanup_completed_run('trailer-assembly-boundaries', code, log, ROOT, process_reaped=True)
        (directory / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    print(json.dumps(result, indent=2))
    return code


if __name__ == '__main__':
    raise SystemExit(main())
