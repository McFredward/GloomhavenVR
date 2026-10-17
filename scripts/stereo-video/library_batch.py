#!/usr/bin/env python3
"""Resume serial library renders/gates or schedule hero depth after the ambient batch."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
from intro_depth import CODE_REVISION, MODEL_REVISION, WEIGHTS_SHA256, digest, save
from library_depth import source_for


def child(command: list[str], log: Path) -> None:
    with log.open('wb') as stream:
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            if process.wait():
                raise RuntimeError(f'Batch child failed; retained log: {log}')
        finally:
            if process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                    process.wait(timeout=15)
                except ProcessLookupError:
                    pass
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()


def living(pid: int) -> bool:
    try:
        status = Path(f'/proc/{pid}/stat').read_text()
        return status[status.rfind(')') + 2:].split()[0] != 'Z'
    except FileNotFoundError:
        return False


def publish(movie: dict, target: Path, proof: dict, render: dict, args) -> None:
    if proof['status'] != 'passed' or proof['stereo_sha256'] != digest(target):
        raise ValueError('Only actual independently verified output may enter Assets')
    asset = args.assets / movie['stereo_key']
    asset.parent.mkdir(parents=True, exist_ok=True)
    if asset.exists():
        if digest(asset) != proof['stereo_sha256']:
            raise ValueError('Refusing different existing delivered movie')
    else:
        os.link(target, asset)
    if movie['kind'] == 'embedded':
        source = source_for(movie, args.data_root, args.embedded)
        mono = args.assets / movie['key']
        if mono.exists() and digest(mono) != movie['source_sha256']:
            raise ValueError('Refusing different original embedded companion')
        if not mono.exists():
            os.link(source, mono)
    provenance = {'schema': 1, 'subject': movie['key'], 'source_sha256': movie['source_sha256'],
        'source_kind': movie['kind'], 'source_relative': movie.get('source_relative'),
        'stereo_file': Path(movie['stereo_key']).name, 'stereo_sha256': proof['stereo_sha256'],
        'stereo_bytes': proof['stereo_bytes'], 'packing': 'full-SBS-left-first',
        'per_eye_size': [1920, 1080], 'fps': movie['fps'], 'frames': movie['frames'],
        'duration_seconds': movie['frames'] / movie['fps'], 'body_rows': movie['body_rows'],
        'shots': movie['shots'], 'profile': {'cpu_dtype': 'float32', 'model_input': 392,
            'temporal_window': 32, 'temporal_overlap': 10, 'saved_depth_size': [640, 360],
            'shot_normalization_percentiles': [2, 98], 'depth_disparity_px': [-4, 12],
            'code_revision': CODE_REVISION, 'model_revision': MODEL_REVISION, 'checkpoint_sha256': WEIGHTS_SHA256},
        'left': 'Native decoded YUV directly hstacked before final H264 encoding',
        'right': render['right'], 'disocclusion_fill': 'Farther adjoining source background; no generative inpainting',
        'render_source_sha256': render['render_source_sha256'],
        'geometry_source_sha256': render['geometry_source_sha256'],
        'verification': proof, 'hardware_result': 'unverified'}
    save(asset.with_suffix('.provenance.json'), provenance)


def main() -> int:
    def interrupted(_signum, _frame):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=['render', 'hero-depth'], required=True)
    parser.add_argument('--manifest', type=Path, default=Path(__file__).with_name('library-manifest.json'))
    parser.add_argument('--data-root', type=Path, required=True)
    parser.add_argument('--embedded', type=Path, required=True)
    parser.add_argument('--model-root', type=Path, required=True)
    parser.add_argument('--depths', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--assets', type=Path, default=Path(__file__).resolve().parents[2] / 'src/GloomhavenVR/Assets/Movies')
    parser.add_argument('--after-pid', type=int)
    parser.add_argument('--skip', action='append', default=[])
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    if args.after_pid:
        while living(args.after_pid):
            save(args.out / 'batch-status.json', {'status': 'waiting-for-owned-process', 'pid': args.after_pid,
                'mode': args.mode, 'updated_unix': time.time()})
            time.sleep(5)
    manifest = json.loads(args.manifest.read_text())
    movies = [m for m in manifest['movies'] if not m.get('root_owned') and m['key'] not in args.skip]
    prefix = [sys.executable]
    script_root = Path(__file__).resolve().parent
    if args.mode == 'hero-depth':
        movies = [m for m in movies if m['key'].startswith('Heroes/')]
        command = prefix + [str(script_root / 'library_depth.py'), '--manifest', str(args.manifest),
            '--data-root', str(args.data_root), '--embedded', str(args.embedded),
            '--model-root', str(args.model_root), '--out', str(args.depths), '--threads', '6']
        for movie in movies:
            command += ['--movie', movie['key']]
        prior = args.depths / 'depth-manifest.json'
        if prior.exists():
            shutil.copy2(prior, args.out / 'ambient-depth-manifest.json')
        save(args.out / 'command.json', command)
        save(args.out / 'batch-status.json', {'status': 'running', 'mode': args.mode, 'subjects': len(movies)})
        child(command, args.out / 'hero-inference.log')
        save(args.out / 'batch-status.json', {'status': 'passed', 'mode': args.mode, 'subjects': len(movies)})
        return 0
    completed = []
    for movie in movies:
        label = movie['key'].removesuffix('.mov').replace('/', '_')
        render_dir = args.out / ('render-' + label)
        target = render_dir / Path(movie['stereo_key']).name
        if (render_dir / 'receipt.json').exists():
            render = json.loads((render_dir / 'receipt.json').read_text())
            if render['output_sha256'] != digest(target) or render['source_sha256'] != movie['source_sha256']:
                raise ValueError('Existing render differs from its pinned source/output receipt')
        else:
            save(args.out / 'batch-status.json', {'status': 'rendering-or-waiting-depth', 'movie': movie['key'], 'completed': completed})
            command = prefix + [str(script_root / 'library_stereo.py'), '--manifest', str(args.manifest),
                '--movie', movie['key'], '--data-root', str(args.data_root), '--embedded', str(args.embedded),
                '--depths', str(args.depths), '--out', str(render_dir), '--wait-depth']
            child(command, args.out / (label + '-render.log'))
            render = json.loads((render_dir / 'receipt.json').read_text())
        verify_dir = args.out / ('verify-' + label) / 'run-media001'
        if (verify_dir / 'result.json').exists():
            proof = json.loads((verify_dir / 'result.json').read_text())
            if proof['stereo_sha256'] != digest(target):
                raise ValueError('Existing verification does not refer to current output')
        else:
            save(args.out / 'batch-status.json', {'status': 'verifying', 'movie': movie['key'], 'completed': completed})
            command = prefix + [str(script_root / 'library_verify.py'), '--manifest', str(args.manifest),
                '--movie', movie['key'], '--data-root', str(args.data_root), '--embedded', str(args.embedded),
                '--depths', str(args.depths), '--model-root', str(args.model_root),
                '--stereo', str(target), '--out', str(verify_dir)]
            child(command, args.out / (label + '-verify.log'))
            proof = json.loads((verify_dir / 'result.json').read_text())
        publish(movie, target, proof, render, args)
        completed.append({'movie': movie['key'], 'sha256': proof['stereo_sha256'], 'bytes': proof['stereo_bytes']})
        save(args.out / 'batch-status.json', {'status': 'running', 'completed': completed})
        print(json.dumps(completed[-1]), flush=True)
    save(args.out / 'batch-status.json', {'status': 'passed', 'completed': completed})
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
