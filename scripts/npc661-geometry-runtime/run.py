#!/usr/bin/env python3
"""Render actual offered VRCard body and print through production capture/playback."""
import argparse, hashlib, json, shutil, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
BASE = '12df1353f'
INVARIANT = 'offered body and printed front preserve exact original geometry on every render'
ORIGIN_BOUNDARY = 'Historical660 geometry control cannot enter current116 original-return detach.'
HOVER_BOUNDARY = 'Historical660 geometry control cannot enter current117 active physical-hover classification.'


def old_offerings_adapter(old):
    namespace = 'namespace GloomhavenVR.Net.TownServices;'
    if old.count(namespace) != 1 or 'DetachOfferedOriginal(' in old or 'IsOfferedPhysical(' in old:
        raise RuntimeError('Historical660 offerings boundary source drift')
    # These existing publisher/opening ports cannot create physical affinity.
    # Current116 original migration and active117 physical hover are outside
    # this historical control: fail
    # explicitly if reached instead of silently dropping its gameplay lifecycle,
    # or importing current mounts/depth paint into the old geometry implementation.
    compatibility = '\nnamespace GloomhavenVR.Net.TownServices { internal static partial class TownServiceMirror { '
    compatibility += 'internal static void RegisterOfferedPhysical(UnityEngine.Transform? body,UnityEngine.Transform? print) {} '
    compatibility += 'private static void RestoreOfferedPhysicalMounts() {} '
    compatibility += 'private static void DetachOfferedOriginal(RemoteModule module,UnityEngine.Transform shared) '
    compatibility += '{ throw new System.NotSupportedException(' + json.dumps(ORIGIN_BOUNDARY) + '); } '
    compatibility += 'private static bool IsOfferedPhysical(RemoteModule module) '
    compatibility += '{ throw new System.NotSupportedException(' + json.dumps(HOVER_BOUNDARY) + '); } } }\n'
    return old.replace(namespace, 'namespace GloomhavenVR.Net.TownServices {', 1) + '\n}\n' + compatibility

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc661-geometry')
    parser.add_argument('--controls', action='store_true')
    parser.add_argument('--old-code', action='store_true', help='Run exact Build660 Offerings as a causal control')
    parser.add_argument('--with-old-code', action='store_true', help='Run production and only the exact Build660 causal control')
    args = parser.parse_args(); root = args.source_root.resolve(); args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    (run / 'town-purse-runtime').symlink_to(root / 'scripts/town-purse-runtime', target_is_directory=True)
    geometry = Path(__file__).with_name('Geometry661.cs'); shutil.copyfile(geometry, fixture / geometry.name)
    transfer = fixture / 'Transfer.cs'; text = transfer.read_text()
    anchor = '{ revision=629; values=ReturnNumbers==null?System.Array.Empty<float>():(float[])ReturnNumbers.Clone();'
    assert text.count(anchor) == 1
    text = text.replace(anchor, '{ if(GeometryClock661.Controlled)return CaptureNativeTownReturn661(source,shared,hand,out revision,out values); '
        'revision=629; values=ReturnNumbers==null?System.Array.Empty<float>():(float[])ReturnNumbers.Clone();')
    transfer.write_text(text)
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
    raw_old_sha256 = hashlib.sha256(old.encode()).hexdigest()
    old = old_offerings_adapter(old)
    variants = [('production', None, None, None, '')]
    if args.controls or args.old_code or args.with_old_code:
        variants.append(('old660-physical-affinity', filename, actual, old, INVARIANT))
        if args.controls:
            variants.append(('no-return-supersession', filename,
                'if (PhysicalOfferingBlocked(module, composed, physical, slot)) continue;',
                'if (PhysicalOfferingBlocked(module, composed, physical, slot) && slot.Entry.Kind == 255) continue;',
                'native return supersedes lost old offered-body affinity on every render'))
            variants.append(('no-terminal-return-floor', filename,
                'return blocked || OfferedPhysicalReturns.TryGetValue(module, out float returnedAt) && affinity.SampleTime <= returnedAt;',
                'return blocked;',
                'native return supersedes lost old offered-body affinity on every render'))
            variants.append(('no-hand-supersession', filename,
                'else if (slot.Entry.Kind == 1 && slot.Entry.Hand != 0) blocked = true;',
                'else if (slot.Entry.Kind == 1 && slot.Entry.Hand == 255) blocked = true;',
                'direct hand reclaim supersedes lost old offered-body affinity on every render'))
            variants.append(('no-census-child-preservation', 'TownServiceMirror.cs',
                'child.Motion.Reparent(mount);',
                'if (child.Host.transform.parent == null) child.Motion.Reparent(mount);',
                'native census preserves the visible mounted backing before retiring its old printed parent'))
            native_apply = '        module.Motion.AdoptExternalRootPose(applyTarget: true);\n        TownCardReturnMotion.Apply('
            variants.append(('old-native-host-adoption', 'TownServiceMirror.Motion.cs', native_apply,
                '        TownCardReturnMotion.Apply(',
                'native return supersedes lost old offered-body affinity on every render'))
            handoff = 'ordinaryRoot.Dirty = true;'
            if handoff in (root / 'src/GloomhavenVR/Net/TownServices/TownServiceMirror.Motion.cs').read_text():
                variants.append(('no-native-root-handoff', 'TownServiceMirror.Motion.cs', handoff,
                    'ordinaryRoot.Dirty |= ordinaryRoot.Entry.Kind == 255;',
                    'real native cohort starts, completes and hands its original roots back to ordinary pose ownership'))
    if args.old_code: variants = variants[1:]
    anchor = '    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)'
    assert source.count(anchor) == 1
    source = source.replace(anchor, '    variants = ' + repr(variants) + '\n' + anchor)
    binder = Path(__file__).with_name('bind-native.py')
    opening = '    bound, hashes = sources(args.source_root)'
    assert source.count(opening) == 1
    source = source.replace(opening, opening + '\n'
        '    spec661 = importlib.util.spec_from_file_location("geometry661_native", ' + repr(str(binder)) + ')\n'
        '    native661 = importlib.util.module_from_spec(spec661); spec661.loader.exec_module(native661)\n'
        '    bound, hashes = native661.bind(args.source_root, bound, hashes, method)')
    bound_checker = run / 'bound-checker.py'; bound_checker.write_text(source)
    (run / 'geometry661-manifest.json').write_text(json.dumps({
        'base': BASE, 'boundary': 'Actual VRCard backing factory/CardMesh, native enhancement mask, capture/codec/Receive/motion and Camera readbacks; '
        'card artwork/native pooled UI constructor and transport scheduling are declared fixtures, not headset proof.',
        'old_offerings_raw_sha256': raw_old_sha256,
        'old_offerings_boundary_adapter_sha256': hashlib.sha256(old.encode()).hexdigest(),
        'old_offerings_unused_ports': ['RegisterOfferedPhysical: no physical affinity in660',
            'RestoreOfferedPhysicalMounts: no temporary physical mounts in660'],
        'old_offerings_unsupported_lifecycle': {'method': 'DetachOfferedOriginal', 'policy': 'fail-fast',
            'message': ORIGIN_BOUNDARY, 'boundary': 'Current116 retained/adopted original return; '
                'control must fail at the earlier original-geometry assertion. Reaching this seam is an unexpected failure.'},
        'old_offerings_unsupported_hover': {'method': 'IsOfferedPhysical', 'policy': 'fail-fast',
            'message': HOVER_BOUNDARY, 'boundary': 'Current117 active physical-hover classification; '
                'historical geometry uses direct source poses without registered native Place. '
                'An active117 header reaching this seam is an unexpected failure, not a successful control.'},
        'variants': [variant[0] for variant in variants],
        'source_sha256': {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in [root/path, geometry, Path(__file__), checker, binder]}
    }, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    command = ['python3', str(bound_checker), '--source-root', str(root), '--fixture-dir', str(fixture),
               '--suite', 'lifecycle', '--no-negative-controls', '--output-dir', str(run / 'proof')]
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (run/'command.log').write_text(result.stdout); print(result.stdout, end='')
    if result.returncode: raise SystemExit(result.returncode)

if __name__ == '__main__': main()
