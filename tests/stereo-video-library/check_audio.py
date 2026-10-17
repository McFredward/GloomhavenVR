#!/usr/bin/env python3
"""Real native AAC ending and lossless video-copy regression/refusal fixture."""
from __future__ import annotations
import argparse
import copy
import json
from pathlib import Path
import shutil
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/stereo-video'))
from intro_depth import digest, save
from library_audio import finalize_audio, packets, require_packets, validate_mux
from library_common import load_movie


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=ROOT / 'scripts/stereo-video/library-manifest.json')
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--encoded', type=Path, required=True)
    parser.add_argument('--movie', default='Heroes/BeastTyrant.mov')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    code = 1
    try:
        _, movie = load_movie(args.manifest, args.movie)
        if digest(args.source) != movie['source_sha256']:
            raise ValueError('Real AAC fixture source differs from the native catalogue')
        proof = finalize_audio(args.encoded, args.source, args.out / Path(movie['stereo_key']).name,
                               movie, args.out / 'audio-finalization')
        refused = []
        def rejects(call, label):
            try:
                call()
            except ValueError:
                refused.append(label)
            else:
                raise AssertionError('Audio/video causal control escaped: ' + label)
        rejects(lambda: validate_mux(args.encoded, args.source, args.encoded),
                'Original combined encoder loses the final native AAC packet')
        rejects(lambda: validate_mux(args.encoded, args.source,
                    args.out / 'audio-finalization/copy-mux-before-native-window.mov'),
                'Plain copy mux extends the original presented AAC ending')
        actual_video = packets(args.encoded, 'v:0')
        wrong_clock = copy.deepcopy(actual_video)
        wrong_clock[0]['pts'] += 1
        rejects(lambda: require_packets(actual_video, wrong_clock, 'Video'),
                'Changed encoded video presentation timestamp')
        rejects(lambda: require_packets(actual_video, actual_video[:-1], 'Video'),
                'Lost final encoded video packet')
        proof['negative_controls'] = refused
        proof['source_fixture_sha256'] = digest(Path(__file__))
        proof['manifest_sha256'] = digest(args.manifest)
        save(args.out / 'result.json', proof)
        snapshot = args.out / 'sources'; snapshot.mkdir()
        for path in (Path(__file__), ROOT / 'scripts/stereo-video/library_audio.py', args.manifest):
            shutil.copy2(path, snapshot / path.name)
        print(json.dumps(proof), flush=True)
        code = 0
        return code
    finally:
        sys.path.insert(0, str(ROOT / 'scripts'))
        from town_run_retention import cleanup_completed_run
        log = args.out / 'completion.log'
        log.write_text(f'All owned copy-mux/ffprobe processes stopped and reaped; exit={code}.\nevidence: {args.out.resolve()}\n')
        save(args.out / 'cleanup.json', cleanup_completed_run('stereo-video-library', code,
             log.resolve(), ROOT, process_reaped=True))


if __name__ == '__main__':
    raise SystemExit(main())
