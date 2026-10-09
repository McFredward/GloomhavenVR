#!/usr/bin/env python3
"""Render actual offered VRCard body and print through production capture/playback."""
import argparse, hashlib, json, shutil, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
BASE = '12df1353f'
INVARIANT = 'offered body and printed front preserve exact original geometry on every render'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc661-geometry')
    parser.add_argument('--controls', action='store_true')
    parser.add_argument('--old-code', action='store_true', help='Run exact Build660 Offerings as a causal control')
    args = parser.parse_args(); root = args.source_root.resolve(); args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    (run / 'town-purse-runtime').symlink_to(root / 'scripts/town-purse-runtime', target_is_directory=True)
    geometry = Path(__file__).with_name('Geometry661.cs'); shutil.copyfile(geometry, fixture / geometry.name)
    program = fixture / 'Program.cs'; text = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    assert text.count(anchor) == 1
    text = text.replace(anchor, '            if (suite == "lifecycle") { IEnumerator geometry = Geometry661(); '
        'while (geometry.MoveNext()) yield return geometry.Current; yield break; }\n' + anchor)
    program.write_text(text)
    checker = root / 'scripts/check-town-service-mirror.py'; source = checker.read_text()
    filename = 'TownServiceMirror.Offerings.cs'
    path = 'src/GloomhavenVR/Net/TownServices/' + filename
    actual = (root / path).read_text()
    old = subprocess.check_output(['git', '-C', str(root), 'show', BASE + ':' + path], text=True)
    variants = [('production', None, None, None, '')]
    if args.controls or args.old_code:
        variants.append(('old660-physical-affinity', filename, actual, old, INVARIANT))
    if args.old_code: variants = variants[1:]
    anchor = '    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)'
    assert source.count(anchor) == 1
    source = source.replace(anchor, '    variants = ' + repr(variants) + '\n' + anchor)
    bound_checker = run / 'bound-checker.py'; bound_checker.write_text(source)
    (run / 'geometry661-manifest.json').write_text(json.dumps({
        'base': BASE, 'boundary': 'Actual VRCard backing factory/CardMesh, native enhancement mask, capture/codec/Receive/motion and Camera readbacks; '
        'card artwork/native pooled UI constructor and transport scheduling are declared fixtures, not headset proof.',
        'old_offerings_sha256': hashlib.sha256(old.encode()).hexdigest(),
        'source_sha256': {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in [root/path, geometry, Path(__file__), checker]}
    }, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    command = ['python3', str(bound_checker), '--source-root', str(root), '--fixture-dir', str(fixture),
               '--suite', 'lifecycle', '--no-negative-controls', '--output-dir', str(run / 'proof')]
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (run/'command.log').write_text(result.stdout); print(result.stdout, end='')
    if result.returncode: raise SystemExit(result.returncode)

if __name__ == '__main__': main()
