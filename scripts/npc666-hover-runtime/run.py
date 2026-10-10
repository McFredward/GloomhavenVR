#!/usr/bin/env python3
"""Native offered sine through actual Place, canonical capture and per-render observer playback."""
import argparse
import hashlib
import importlib.util
import io
import tarfile
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc666-hover')
    parser.add_argument('--controls', action='store_true')
    parser.add_argument('--baseline665', action='store_true', help='Bind untouched dev ce637a1dc source as the causal legacy control')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    baseline_sha = None
    if args.baseline665:
        if args.controls: parser.error('--baseline665 is a separate untouched-source causal control')
        baseline_sha = subprocess.check_output(['git', 'rev-parse', 'ce637a1dc'], cwd=ROOT, text=True).strip()
        archive = subprocess.check_output(['git', 'archive', baseline_sha, 'src', 'scripts'], cwd=ROOT)
        root = run / 'source-build665'; root.mkdir()
        with tarfile.open(fileobj=io.BytesIO(archive)) as tar: tar.extractall(root)
        for name in ('ressources', 'unity'):
            (root / name).symlink_to(ROOT / name, target_is_directory=True)
    fixture = run / 'fixture'
    shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    (run / 'town-purse-runtime').symlink_to(root / 'scripts/town-purse-runtime', target_is_directory=True)
    shutil.copyfile(ROOT / 'scripts/npc665-yaw-runtime/Yaw665.cs', fixture / 'Yaw665.cs')
    test = Path(__file__).with_name('Hover666.cs')
    shutil.copyfile(test, fixture / test.name)
    boundaries = fixture / 'Boundaries.cs'
    boundaries.write_text(boundaries.read_text().replace('bool WantsDebug => false', 'bool WantsDebug => true').replace('Debug(string channel, string message) { }', 'Debug(string channel, string message) => Messages.Add(channel + ": " + message);'))
    transfer = fixture / 'Transfer.cs'
    text = transfer.read_text()
    begin = text.index('        internal bool TryTownReturnMotion(Transform source,Transform shared,Hands.VRHand? hand,out uint revision,out float[] values)')
    end = text.index('\n    }', begin)
    text = text[:begin] + '''        internal bool TryTownReturnMotion(Transform source,Transform shared,Hands.VRHand? hand,out uint revision,out float[] values)
        => CaptureNative666(source,shared,hand,out revision,out values);''' + text[end:]
    transfer.write_text(text)
    program = fixture / 'Program.cs'
    source = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    assert source.count(anchor) == 1
    source = source.replace(anchor, '            if (suite == "lifecycle") { IEnumerator yaw = Hover666(); '
        'while (yaw.MoveNext()) yield return yaw.Current; '
        'File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\\n"); yield break; }\n' + anchor)
    program.write_text(source)
    checker = root / 'scripts/check-town-service-mirror.py'
    source = checker.read_text()
    opening = '    bound, hashes = sources(args.source_root)'
    assert source.count(opening) == 1
    spec = importlib.util.spec_from_file_location('hover666_binding', checker)
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    native = (root / 'src/GloomhavenVR/Cards/VRCard.cs').read_text()
    sampler = loader.method(native, 'internal bool TryTownReturnMotion(')
    begin = native.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);')
    end = native.index('            if (ft >= 1f)', begin)
    native_port = '''using System; using UnityEngine; using GloomhavenVR.Hands;
namespace GloomhavenVR.Cards; internal sealed partial class VRCard {
private bool _flying, _flyIntro;
private float _flyElapsed, _flyDuration, _flyArcHeight;
private uint _townReturnRevision;
private Vector3 _flyFromPos,_flyToPos,_flyFromScale,_flyToScale,_flyArcUp;
private Quaternion _flyRot;
internal void PrimeInactive666(uint revision) { _townReturnRevision=revision; _flying=false; _flyIntro=false; Holder=null; }
internal void BeginNative666(Vector3 target,float duration) {
_flying=true; _flyIntro=true; _flyElapsed=0; _flyDuration=duration;
if(++_townReturnRevision==0)_townReturnRevision=1;
_flyFromPos=transform.position; _flyToPos=target; _flyRot=transform.rotation;
_flyFromScale=transform.localScale; _flyToScale=transform.localScale*.7f;
_flyArcUp=Vector3.up; _flyArcHeight=.08f;
}
''' + sampler.replace('TryTownReturnMotion(', 'CaptureNative666(', 1) + '''
internal void StepNative666() {
''' + native[begin:end].replace('Time.unscaledDeltaTime','global::HoverClock666.Delta') + '''
if(ft>=1f) {_flying=false; _flyIntro=false;}
// Native motion and false/terminal sampler semantics are exact. Home/callback
// gameplay is the boundary; this probe retains the actual rendered endpoint.
}}
'''
    port = run / 'NativeReturn666.cs'; port.write_text(native_port)
    source = source.replace(opening, opening + '\n'
        '    bound["NativeReturn666.cs"] = Path(' + repr(str(port)) + ').read_text()\n'
        '    hashes["VRCard.cs (complete native sampler and flight source)"] = ' + repr(hashlib.sha256(native.encode()).hexdigest()) + '\n'
        '    hashes["NativeReturn666.cs (declared native gameplay boundary)"] = ' + repr(hashlib.sha256(native_port.encode()).hexdigest()) + '\n'
        '    path = args.source_root / "src/GloomhavenVR/Net/TownServices/TownServiceMirror.OfferedRoot.cs"\n'
        '    if path.exists():\n'
        '        bound[path.name] = path.read_text(); hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()\n'
        '    for name in list(bound):\n'
        '        if name.startswith("TownServiceMirror") or name in ("TownServiceMotion.cs", "TownServiceOfferingPose.cs", "TownServiceNativeEnhancementCardMask.cs"):\n'
        '            bound[name] = bound[name].replace("UnityEngine.Time.unscaledTime", "global::HoverClock666.Read").replace("Time.unscaledTime", "global::HoverClock666.Read")\n'
        '            hashes[name + " (explicit common source/render time port)"] = hashlib.sha256(bound[name].encode()).hexdigest()\n')
    anchor = '    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)'
    assert source.count(anchor) == 1
    variants = [('production', None, None, None, '')]
    if args.baseline665:
        variants = [('untouched-build665', None, None, None, 'cold native original evaluates one intrinsic wave without any numeric root or accumulated displacement')]
    if args.controls:
        variants += [
            ('no-local-wave', 'TownServiceMirror.Motion.cs', '        ApplyOfferedHover(now);', '',
             'cold native original evaluates one intrinsic wave without any numeric root or accumulated displacement'),
            ('no-source-strip', 'TownServiceMirror.OfferedHover.cs', '        StripHover(frame.Pose, delta);', '',
             'native intrinsic sine is evaluated per render from its complete canonical base'),
            ('no-base-restore', 'TownServiceMirror.cs', '        RestoreOfferedHoverBase();', '',
             'cold native original evaluates one intrinsic wave without any numeric root or accumulated displacement'),
            ('restart-ui-heartbeat', 'TownServiceMirror.OfferedHover.cs',
             '{ clock = new HoverClock { Started = now }; HoverClocks.Add(key, clock); }',
             '{ clock = new HoverClock { Started = now }; HoverClocks.Add(key, clock); }\n                clock.Started = now;',
             'cold native original evaluates one intrinsic wave without any numeric root or accumulated displacement'),
            ('wrong-root-canvas', 'TownServiceMirror.OfferedHover.cs',
             'source.GetComponentsInParent(true, ParentCanvases);\n            foreach (Canvas candidate in ParentCanvases)\n                if (candidate.isActiveAndEnabled) { authoredCanvas = candidate.rootCanvas; break; }',
             'authoredCanvas = source.GetComponentInParent<Canvas>(true);',
             'hover recipe identifies the exact transmitted active root canvas'),
            ('old-off-floor', 'TownServiceMirror.OfferedHover.cs',
             'if (module.LastFrame?.OfferedHover == null && !HoverPictures.ContainsKey(module)) return true;',
             'if (module.LastFrame?.OfferedHover == null) return true;',
             'fresh native OFF header rejects the actual delayed active canonical root'),
            ('no-native-reoffer-epoch', 'TownServiceOfferingPose.cs',
             '        TownServiceOfferingPose.BeginHover(card, seat);', '',
             'actual native acceptance creates a new epoch after a no-Place no-flight detached interval'),
        ]
    source = source.replace(anchor, '    variants = ' + repr(variants) + '\n' + anchor)
    bound_checker = run / 'bound-checker.py'
    bound_checker.write_text(source)
    (run / 'hover666-manifest.json').write_text(json.dumps({
        'boundary': 'Complete production OfferingPose registry/native procedural backing/native enhancement mask, '
            'capture/packing/codec/binding/motion. Pooled artwork and game-model callbacks are fixture inputs; '
            'source and receiver share the declared deterministic time port; local phase starts at first complete original. '
            'The finite native card settle remains unchanged. No headset claim.',
        'source_sha256': {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in [test, Path(__file__), checker]},
        'variants': [v[0] for v in variants],
        'untouched_baseline_sha': baseline_sha,
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
