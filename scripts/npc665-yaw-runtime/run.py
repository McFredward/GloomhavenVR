#!/usr/bin/env python3
"""Offered native facing through the production sampler and sparse receiver render clocks."""
import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc665-yaw')
    parser.add_argument('--controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = run / 'fixture'
    shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    (run / 'town-purse-runtime').symlink_to(root / 'scripts/town-purse-runtime', target_is_directory=True)
    test = Path(__file__).with_name('Yaw665.cs')
    shutil.copyfile(test, fixture / test.name)
    program = fixture / 'Program.cs'
    source = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    assert source.count(anchor) == 1
    source = source.replace(anchor, '            if (suite == "lifecycle") { IEnumerator yaw = Yaw665(); '
        'while (yaw.MoveNext()) yield return yaw.Current; '
        'File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\\n"); yield break; }\n' + anchor)
    program.write_text(source)
    checker = root / 'scripts/check-town-service-mirror.py'
    source = checker.read_text()
    opening = '    bound, hashes = sources(args.source_root)'
    assert source.count(opening) == 1
    source = source.replace(opening, opening + '\n'
        '    path = args.source_root / "src/GloomhavenVR/Net/TownServices/TownServiceMirror.OfferedRoot.cs"\n'
        '    if path.exists():\n'
        '        bound[path.name] = path.read_text(); hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()\n'
        '    for name in list(bound):\n'
        '        if name.startswith("TownServiceMirror") or name == "TownServiceMotion.cs":\n'
        '            bound[name] = bound[name].replace("UnityEngine.Time.unscaledTime", "global::YawClock665.Read").replace("Time.unscaledTime", "global::YawClock665.Read")\n'
        '            hashes[name + " (explicit common source/render time port)"] = hashlib.sha256(bound[name].encode()).hexdigest()\n')
    anchor = '    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)'
    assert source.count(anchor) == 1
    variants = [('production', None, None, None, '')]
    if args.controls:
        variants += [
            ('old-header-clock', 'TownServiceMirror.Motion.cs',
             '&& !ContinuousOfferedRoot(pair.Key, module, slot, now) && slot.SampleTime < module.LastFrame.SampleTime)',
             '&& slot.SampleTime < module.LastFrame.SampleTime)',
             'arriving owner root and native UI targets cannot snap the currently rendered offered yaw service=3'),
            ('old-merchant-clock', 'TownServiceMirror.Motion.cs',
             'if (module.LastFrame == null || sample == null || !ContinuousOfferedRoot(motion.Owner, module, sample, now))',
             'if (module.LastFrame == null || sample == null || sample.Entry.Service == 1 || !ContinuousOfferedRoot(motion.Owner, module, sample, now))',
             'native UI headers preserve a continuous offered yaw clock service=1'),
        ]
    source = source.replace(anchor, '    variants = ' + repr(variants) + '\n' + anchor)
    bound_checker = run / 'bound-checker.py'
    bound_checker.write_text(source)
    (run / 'yaw665-manifest.json').write_text(json.dumps({
        'boundary': 'Complete production OfferingPose/native procedural backing/native enhancement mask, '
            'capture/packing/codec/binding/motion. Pooled artwork and game-model callbacks are fixture inputs; '
            'source and receiver share the declared deterministic clock. No headset claim.',
        'source_sha256': {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in [test, Path(__file__), checker]},
        'variants': [v[0] for v in variants],
    }, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['python3', str(bound_checker), '--source-root', str(root), '--fixture-dir', str(fixture),
        '--suite', 'lifecycle', '--no-negative-controls', '--output-dir', str(run / 'proof')],
        text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (run / 'command.log').write_text(result.stdout)
    print(result.stdout, end='')
    if result.returncode:
        raise SystemExit(result.returncode)


if __name__ == '__main__':
    main()
