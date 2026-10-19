#!/usr/bin/env python3
"""Extract three addressed, decoded stereo cue pairs for honest manual review."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys
from PIL import Image, ImageDraw
from intro_depth import digest, save
from library_common import load_movie


def cue_frames(movie: dict) -> list[int]:
    shots = [shot for shot in movie['shots'] if shot['mode'] == 'depth']
    if movie['key'].startswith('Ambient/') and len(shots) == 3:
        frames = [(shot['start'] + shot['end']) // 2 for shot in shots]
    elif shots:
        first, middle, last = shots[0], shots[len(shots) // 2], shots[-1]
        frames = [(first['start'] + first['end']) // 2,
            (middle['start'] + middle['end']) // 2,
            max(last['start'], last['end'] - movie['fps'])]
    else:
        frames = [movie['frames'] // 12, movie['frames'] // 2, movie['frames'] - 1]
    frames = sorted(set(frames))
    if len(frames) != 3 or any(not 0 <= frame < movie['frames'] for frame in frames):
        raise ValueError('Three distinct original cue addresses are required')
    return frames


def extract_cues(stereo: Path, movie: dict, out: Path) -> dict:
    frames = cue_frames(movie)
    out.mkdir(parents=True, exist_ok=False)
    select = '+'.join(f'eq(n\\,{frame})' for frame in frames)
    command = ['ffmpeg', '-hide_banner', '-v', 'info', '-nostdin', '-threads', '2',
        '-i', str(stereo), '-map', '0:v:0', '-an', '-filter_threads', '1',
        '-vf', 'select=' + select + ',showinfo,scale=in_color_matrix=bt709:in_range=tv',
        '-frames:v', '3', '-fps_mode', 'passthrough', '-pix_fmt', 'rgb24',
        '-threads', '1', str(out / 'decoded-%02d.png')]
    save(out / 'command.json', command)
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    (out / 'decode.log').write_bytes(result.stderr)
    if result.returncode:
        raise RuntimeError('Decoded cue extraction failed; see decode.log')
    images = sorted(out.glob('decoded-*.png'))
    # select + passthrough must produce the actual selected clock, never duplicate
    # the first selected picture to fill the omitted source intervals.
    stamps = [float(t) for t in re.findall(rb'pts_time:([0-9.]+)', result.stderr)]
    if len(images) != 3 or len(stamps) != 3 or any(abs(t - f / movie['fps']) > 0.00001 for t, f in zip(stamps, frames)):
        raise ValueError('Cue extraction duplicated/lost or retimed a selected frame')
    contact = Image.new('RGB', (1536, 3 * (432 + 28)), 'black')
    draw = ImageDraw.Draw(contact)
    records = []
    for index, (frame, path) in enumerate(zip(frames, images)):
        with Image.open(path) as picture:
            if picture.size != (3840, 1080):
                raise ValueError('Decoded stereo cue has squashed or missing eye dimensions')
            contact.paste(picture.resize((1536, 432), Image.Resampling.LANCZOS), (0, index * 460 + 28))
        draw.text((8, index * 460 + 8), f'{movie["key"]} | native frame {frame} | {frame / movie["fps"]:.3f}s | L / R', fill='white')
        records.append({'frame': frame, 'time_seconds': stamps[index], 'file': path.name, 'sha256': digest(path)})
    contact_path = out / 'decoded-contact.jpg'
    contact.save(contact_path, quality=96, subsampling=0)
    proof = {'status': 'decoded-cues-ready-for-manual-inspection', 'movie': movie['key'],
        'stereo_sha256': digest(stereo), 'frames': records, 'contact_file': contact_path.name,
        'contact_sha256': digest(contact_path), 'extraction_source_sha256': digest(Path(__file__)),
        'manual_visual_review': 'pending', 'hardware_result': 'unverified'}
    save(out / 'receipt.json', proof)
    return proof


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=Path(__file__).with_name('library-manifest.json'))
    parser.add_argument('--movie', required=True)
    parser.add_argument('--stereo', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    code = 1
    try:
        _, movie = load_movie(args.manifest, args.movie)
        print(json.dumps(extract_cues(args.stereo, movie, args.out)))
        code = 0
        return code
    finally:
        if args.out.exists():
            root = Path(__file__).resolve().parents[2]
            sys.path.insert(0, str(root / 'scripts'))
            from town_run_retention import cleanup_completed_run
            log = args.out / 'completion.log'
            log.write_text(f'Owned cue decoder stopped and reaped; exit={code}.\nevidence: {args.out.resolve()}\n')
            save(args.out / 'cleanup.json', cleanup_completed_run('stereo-video-library', code, log.resolve(), root, process_reaped=True))

if __name__ == '__main__':
    raise SystemExit(main())
