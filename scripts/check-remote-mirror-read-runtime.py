#!/usr/bin/env python3
"""Run complete production Pair.Apply and pinned original against real Unity UI callbacks."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def block(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def instrument(method):
    # Count actual evaluated source getters; wrappers do not substitute any returned value.
    reads = re.compile(r'\b(?:_src(?:Rect|Graphic|Tmp|Text|Image|Raw|Group)|Src)\.(?:canvasRenderer\.GetColor\(\)|gameObject\.activeSelf|anchorMin|anchorMax|pivot|sizeDelta|anchoredPosition3D|localPosition|localRotation|localScale|enabled|color|text|fontSize|fontStyle|fontSharedMaterial|font|overrideSprite|sprite|type|fillAmount|texture|uvRect|alpha)(?![A-Za-z0-9_])')
    method = reads.sub(lambda m: 'Proof.Read("' + m[0] + '", ' + m[0] + ')', method)
    writes = re.compile(r'((?:_dst\w+|Dst)\.\w+)\s*=\s*([^;\n]+);')
    method = writes.sub(lambda m: m[1] + ' = Proof.Write("' + m[1] + '", ' + m[2] + ');', method)
    method = method.replace('Dst.gameObject.SetActive(on);', 'Dst.gameObject.SetActive(Proof.Write("Dst.active", on));')
    method = method.replace('Dst.gameObject.SetActive(false);', 'Dst.gameObject.SetActive(Proof.Write("Dst.active", false));')
    method = method.replace('_dstGraphic.canvasRenderer.SetColor(rendered);', '_dstGraphic.canvasRenderer.SetColor(Proof.Write("_dstGraphic.rendered", rendered));')
    return method



def prove_dial_census(root, run):
    """Use the actual registry scanner on the actual new partial/wrapper/call site."""
    scanner = root / 'scripts/check-mirror-dials.py'
    config = root / 'src/GloomhavenVR/Core/Perf/PerfConfig.FrameRendering.cs'
    allow = root / '.planning/refactor/MIRROR-DIALS.allow'
    sample = run / 'dial-census-source'
    (sample / 'Core/Perf').mkdir(parents=True)
    (sample / 'Net/Remote').mkdir(parents=True)
    shutil.copyfile(config, sample / 'Core/Perf/PerfConfig.FrameRendering.cs')
    shutil.copyfile(root / 'src/GloomhavenVR/Net/Remote/RemoteWidgetMirror.cs', sample / 'Net/Remote/RemoteWidgetMirror.cs')
    code = scanner.read_text()
    namespace = {'__file__': str(scanner), '__name__': 'mirror_dial_proof'}
    exec(compile(code, str(scanner), 'exec'), namespace)
    namespace['SRC'] = sample; namespace['MIRROR'] = sample / 'Net/Remote'
    dials, wrappers = namespace['census']()
    assert 'SharedUiWindowReads' in dials
    key = 'PerfConfig.SharedUiWindowReadsOn'
    assert key in wrappers, 'Nullable partial config wrapper must be censused'
    found = namespace['reads'](dials, wrappers)
    assert any(read[0] == 'Net/Remote/RemoteWidgetMirror.cs' and read[2] == key for read in found)
    allowed, malformed = namespace['load_allow']()
    assert not malformed and allowed[('Net/Remote/RemoteWidgetMirror.cs', key)][0] == 'not-1to1'
    before = r'r"\s*(?:\?\.|\.)\s*Value\b"'
    assert code.count(before) == 1, 'Nullable wrapper causal seam changed'
    legacy = {'__file__': str(scanner), '__name__': 'old_mirror_dial_proof'}
    exec(compile(code.replace(before, r'r"\.Value\b"', 1), str(scanner), 'exec'), legacy)
    legacy['SRC'] = sample; legacy['MIRROR'] = sample / 'Net/Remote'
    assert key not in legacy['census']()[1], 'Legacy scanner control must miss the nullable wrapper'
    (run / 'dial-census-proof.json').write_text(json.dumps({
        'actual_nullable_partial_wrapper_found': True, 'actual_remote_reader_found': True,
        'recorded_verdict': 'not-1to1', 'old_nullable_matcher_rejected': True,
        'scanner_sha256': hashlib.sha256(scanner.read_bytes()).hexdigest(),
        'allow_sha256': hashlib.sha256(allow.read_bytes()).hexdigest()}, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/remote-mirror-read-runtime')
    parser.add_argument('--case', action='append', help='Partial development run only')
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = root / 'scripts/remote-mirror-read-runtime'
    mirror = root / 'src/GloomhavenVR/Net/Remote/RemoteWidgetMirror.cs'
    code = mirror.read_text()
    sync = block(code, 'private void Sync()')
    assert 'using var nativeReadWork = NativeMirrorReadWork.Begin();' in sync
    work_path = root / 'src/GloomhavenVR/Net/Remote/NativeMirrorReadWork.cs'
    monitor_path = root / 'src/GloomhavenVR/Core/Perf/PerfMonitor.cs'
    tally_path = root / 'src/GloomhavenVR/Core/Perf/PerfMonitor.Counters.cs'
    monitor = monitor_path.read_text()
    counter_output = block(monitor, 'internal static void RegisterDebug(string name)') + '\n' + block(monitor, 'private static void LogCounters(float windowSeconds)')
    counter_output = counter_output.replace('Scope0,', '"Perf",')
    pair = block(code, 'private struct Pair').replace('private struct Pair', 'internal struct Pair', 1)
    # Outer-class documentation references are outside this narrowly compiled fixture.
    pair = re.sub(r'^\s*///[^\n]*\n', '', pair, flags=re.MULTILINE)
    apply = block(pair, 'public bool Apply(bool isRoot, bool driveRects)')
    baseline = (fixture / 'Apply.pre-optimization.fixture').read_text().strip()
    assert hashlib.sha256(baseline.encode()).hexdigest() == '1074b531ac389a5c3c2f3af8b5600eea3cc5581e209fe84c97df79db4919e625', 'Pinned original Apply changed'
    # Preserve the deliberately unoptimized virtual-source branches byte for byte.
    for signature in ('if (_srcTmp != null && _dstTmp != null)', 'else if (_srcText != null && _dstText != null)'):
        assert block(apply, signature) == block(baseline, signature), 'Virtual getter path changed'
    material_path = root / 'src/GloomhavenVR/Net/Remote/NativePlaybackWrites.cs'
    material = material_path.read_text()
    owner = block(material, 'internal struct NativePlaybackMaterialOwner')
    material_write = block(material, 'internal static void Material(Graphic target, Material material)')
    variants = [
        ('production', '', '', ''),
        ('debug-read-undercount', '', '', 'debug counters equal evaluated nonvirtual source getters'),
        ('debug-nested-flush', '', '', 'nested reads wait for outer mirror scope before reporting'),
        ('debug-normal-log-leak', '', '', 'normal log level emits no diagnostic-only counter line'),
        ('repeated-native-getter', 'reuseNativeReads ? sourceAnchorMin : _srcRect.anchorMin',
         'false ? sourceAnchorMin : _srcRect.anchorMin', 'changed native getter read once'),
        ('off-ignored', 'PerfConfig.SharedUiWindowReadsOn', 'true', 'Off read/write/callback trace equals original Apply'),
        ('early-anchor-read', 'Vector2 sourceAnchorMax = _srcRect.anchorMax;', 'Vector2 sourceAnchorMax = reuseNativeReads ? earlyAnchorMax : _srcRect.anchorMax;',
         'native callback writes retain live later source fields'),
        ('early-sprite-read', 'Sprite? sourceSprite = _srcImage.sprite;', 'Sprite? sourceSprite = reuseNativeReads ? earlySprite : _srcImage.sprite;',
         'native callback writes retain live later source fields'),
        ('early-fill-read', 'float sourceFill = _srcImage.fillAmount;', 'float sourceFill = reuseNativeReads ? earlyFill : _srcImage.fillAmount;',
         'native callback writes retain live later source fields'),
        ('virtual-text-read-reused', 'if (_dstText.text != _srcText.text) _dstText.text = _srcText.text;',
         'if (reuseNativeReads) { string virtualText = _srcText.text; if (_dstText.text != virtualText) _dstText.text = virtualText; } else { if (_dstText.text != _srcText.text) _dstText.text = _srcText.text; }',
         'virtual getter keeps original second observation'),
        ('unchanged-fill-written', 'if (targetFill != sourceFill)', 'if (targetFill != sourceFill || reuseNativeReads)', 'unchanged destination writes nothing'),
        ('group-animation-lost', 'if (targetAlpha != sourceAlpha)', 'if (targetAlpha != sourceAlpha && !reuseNativeReads)',
         'native callback writes retain live later source fields'),
    ]
    if args.case:
        known = {v[0] for v in variants}
        if set(args.case) - known:
            parser.error('Unknown cases: ' + ', '.join(set(args.case) - known))
        variants = [v for v in variants if v[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    prove_dial_census(root, run)
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    managed = unity.parent / 'Data/Managed'
    game = root / 'ressources/GH_Data/Managed'
    proof_files = [mirror, work_path, monitor_path, tally_path, material_path, root / 'scripts/check-mirror-dials.py', root / '.planning/refactor/MIRROR-DIALS.allow', root / 'src/GloomhavenVR/Core/Perf/PerfConfig.FrameRendering.cs', Path(__file__).resolve(), *fixture.glob('*.*'), fixture / 'Editor/MirrorReadsRunner.cs',
                   game / 'UnityEngine.UI.dll', game / 'Unity.TextMeshPro.dll',
                   game / 'UnityEngine.CoreModule.dll', game / 'UnityEngine.UIModule.dll']
    hashes = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in proof_files}
    (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    dotnet = os.environ.get('DOTNET_EXE', str(Path.home() / '.dotnet/dotnet'))
    for name, before, after, expected in variants:
        changed = apply
        if before:
            if name == 'off-ignored':
                assert changed.count(before) == 1, 'Option control binding drift'
                changed = changed.replace(before, after)
            else:
                assert changed.count(before) == 1, 'Causal binding drift: ' + name
                changed = changed.replace(before, after, 1)
            if name == 'early-anchor-read':
                changed = changed.replace('            if (driveRects)',
                    '            Vector2 earlyAnchorMax = reuseNativeReads && _srcRect != null ? _srcRect.anchorMax : default;\n            if (driveRects)', 1)
            if name == 'early-sprite-read':
                changed = changed.replace('            if (_srcGraphic != null && _dstGraphic != null)',
                    '            Sprite? earlySprite = reuseNativeReads && _srcImage != null ? _srcImage.sprite : null;\n            if (_srcGraphic != null && _dstGraphic != null)', 1)
            if name == 'early-fill-read':
                changed = changed.replace('                Image.Type targetImageType = _dstImage.type;',
                    '                float earlyFill = reuseNativeReads ? _srcImage.fillAmount : 0f;\n                Image.Type targetImageType = _dstImage.type;', 1)
        current = pair.replace(apply, instrument(changed), 1)
        baseline_method = baseline.replace('public bool Apply(', 'public bool ApplyOriginal(', 1)
        current = current[:-1] + '\n' + instrument(baseline_method) + '\n}'
        generated_code = 'using System; using GloomhavenVR.Core; using TMPro; using UnityEngine; using UnityEngine.UI; namespace GloomhavenVR.Net { internal static class RemoteWidgetMirror {\n' + current + '\n}\n' + owner + '\ninternal static class NativePlaybackWrites {\n' + material_write + '\n}\n}'
        case = run / name; generated = case / 'production'; generated.mkdir(parents=True)
        (generated / 'Pair.cs').write_text(generated_code)
        work_code = work_path.read_text()
        if name == 'debug-read-undercount':
            assert work_code.count('_reads++;') == 2
            work_code = work_code.replace('_reads++;', '_reads += 0;', 1)
        if name == 'debug-nested-flush':
            assert work_code.count('if (!_active || --_depth != 0) return;') == 1
            work_code = work_code.replace('if (!_active || --_depth != 0) return;', 'if (!_active) return; --_depth;', 1)
        (generated / 'NativeMirrorReadWork.cs').write_text(work_code)
        counter_code = counter_output
        if name == 'debug-normal-log-leak':
            counter_code = counter_code.replace('tally.DebugOnly = true;', 'tally.DebugOnly = false;', 1)
        (generated / 'CounterOutput.cs').write_text('using System.Text; using UnityEngine; namespace GloomhavenVR.Core { internal static partial class PerfMonitor {' + counter_code + '}}')
        (generated / 'Counters.cs').write_text(re.sub(r'^\s*///[^\n]*\n', '', tally_path.read_text(), flags=re.MULTILINE))
        shutil.copyfile(fixture / 'MirrorReads.csproj', case / 'MirrorReads.csproj')
        assembly = 'MirrorReads_' + name.replace('-', '_')
        command = [dotnet, 'build', str(case / 'MirrorReads.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                   '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(generated),
                   '-p:UnityManaged=' + str(managed), '-p:GameManaged=' + str(game)]
        built = subprocess.run(command, capture_output=True, text=True)
        (case / 'build.log').write_text(built.stdout + built.stderr)
        if built.returncode:
            raise RuntimeError(built.stdout + built.stderr + '\nCompilation cannot pass a causal control')
        manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    project = run / 'unity'
    for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'):
        (project / directory).mkdir(parents=True)
    for entry in manifest['cases']:
        target = project / 'Assets/Plugins' / Path(entry['dll']).name
        shutil.copyfile(entry['dll'], target); entry['dll'] = str(target)
    # Only the native UI dependencies are loaded, avoiding unrelated player-only game bootstraps.
    for name in ('UnityEngine.UI.dll', 'Unity.TextMeshPro.dll'):
        shutil.copyfile(game / name, project / 'Assets/Plugins' / name)
    shutil.copyfile(fixture / 'Editor/MirrorReadsRunner.cs', project / 'Assets/Editor/MirrorReadsRunner.cs')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0'}}))
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
    ran = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                          '-executeMethod', 'MirrorReadsRunner.Start', '-interactionManifest', str(path),
                          '-logFile', str(run / 'unity.log')], timeout=240)
    if (run / 'results.txt').exists(): print((run / 'results.txt').read_text())
    print('Evidence: ' + str(run))
    raise SystemExit(ran.returncode)


if __name__ == '__main__':
    main()
