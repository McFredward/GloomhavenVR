#!/usr/bin/env python3
"""Exercise serialized mage originals through production offered-card playback.

The actual level4 highlighter includes the spinning native sprite and pooled area
prototype. Card-model construction and the native animation clock are explicit
fixture boundaries; capture, wire, fitting and observer playback are production.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=root)
    parser.add_argument('--output-dir', type=Path, default=root / '.planning/debug/town-native-overlays635')
    parser.add_argument('--mask-source', type=Path, help='Explicit pending worker source; its hash is recorded.')
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    python = os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python'))
    native = run / 'native'
    subprocess.run([python, str(root / 'scripts/town-native-overlays635-runtime/export-native.py'),
                    str(root), str(native)], check=True)
    spec = importlib.util.spec_from_file_location('original_mirror_sources', root / 'scripts/check-town-service-mirror.py')
    binder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(binder)
    bound, hashes = binder.sources(root)
    if args.mask_source:
        bound['TownServiceNativeEnhancementCardMask.cs'] = args.mask_source.read_text()
        hashes['TownServiceNativeEnhancementCardMask.cs'] = hashlib.sha256(bound['TownServiceNativeEnhancementCardMask.cs'].encode()).hexdigest()
    fixture = run / 'fixture'
    shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    program = (fixture / 'Program.cs').read_text()
    anchor = '            DelayedCensusRace();'
    assert program.count(anchor) == 1
    program = program.replace(anchor, '''            if (suite == "native-overlays635")
            {
                _camera = Go("Native overlay proof camera").AddComponent<Camera>();
                _camera.enabled = false; _camera.orthographic = true;
                _camera.nearClipPlane = .01f; _camera.farClipPlane = 100;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(.025f, .03f, .04f, 1);
                IEnumerator proof = NativeOverlays635(); while (proof.MoveNext()) yield return proof.Current;
                File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\\n");
                yield break;
            }
''' + anchor)
    (fixture / 'Program.cs').write_text(program)
    # Reuse the source-bound portable reader; no second serialized grammar or
    # hand-painted replacement of the native layout/sprites is introduced.
    reader_source = root / 'scripts/town-first-picture632-runtime/FirstPicture632.cs'
    reader = reader_source.read_text().split('    private static IEnumerator FirstPicture632()')[0] + '}\n'
    reader = reader.replace('NativeFirstPicture632', 'NativeOverlays635')
    (fixture / 'OriginalOverlayReader635.cs').write_text(reader)
    shutil.copyfile(root / 'scripts/town-native-overlays635-runtime/NativeOverlays635.cs', fixture / 'NativeOverlays635.cs')
    hashes['serialized-reader (full source)'] = hashlib.sha256(reader_source.read_bytes()).hexdigest()
    hashes['native-overlays635 fixture'] = hashlib.sha256((fixture / 'NativeOverlays635.cs').read_bytes()).hexdigest()
    hashes['UIEnchantressEffect native clock'] = json.loads((native / 'provenance.json').read_text())['native_clock_sha256']
    (run / 'source-hashes.json').write_text(json.dumps({'source': str(root), 'mask_override': str(args.mask_source or ''), 'sha256': hashes}, indent=2) + '\n')
    cases = [('production', None, None, None, '')]
    if not args.no_negative_controls:
        cases += [
            ('missing-print-affinity', 'TownServiceMirror.Motion.cs', '        ApplyOfferedFrames(now);',
             '        /* causal probe: omit exact original print relation */',
             'native original area stays on the same physical print throughout an observer turn'),
            ('wrong-offered-binding', 'TownServiceMirror.Offerings.cs',
             'int printIndex = Array.IndexOf(physical.Binding.Bindings, relation.OfferedBinding);',
             'int printIndex = -1;',
             'native original area stays on the same physical print throughout an observer turn'),
            ('retained-native-pool-basis', 'TownServiceNativeEnhancementCardMask.cs',
             '        AlignNativeAreas();', '        /* causal probe: preserve stale pooled world basis */',
             'real pooled native area loses its retained flat basis before offered-card capture'),
            ('double-card-size-mapping', 'TownServiceNativeEnhancementCardMask.cs',
             'correction = cardHeight / rootHeight * _lastRootHeight / _lastPhysicalCardHeight;',
             'correction = cardHeight / _lastPhysicalCardHeight;',
             'actual native ring is round and surrounds the entire physical card at every owner yaw'),
            ('wrong-sibling-plane', 'TownServiceNativeEnhancementCardMask.cs',
             'Quaternion plane = PhysicalPlane(parent);',
             'Quaternion plane = Quaternion.identity;',
             'actual native ring uses the pooled physical print plane rather than its differently rotated sibling holder')]
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    managed = root / 'ressources/GH_Data/Managed'
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'suite': 'native-overlays635', 'cases': []}
    for name, filename, before, after, expected in cases:
        build = run / name
        production = build / 'production'
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                assert text.count(before) == 1, (name, 'mutation binding drift')
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        csproj = build / 'Mirror.csproj'
        shutil.copyfile(fixture / 'Mirror.csproj', csproj)
        assembly = 'NativeOverlays635_' + name.replace('-', '_')
        command = [str(Path.home() / '.dotnet/dotnet'), 'build', str(csproj), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                   '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                   '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(managed / 'UnityEngine.UI.dll'),
                   '-p:UnityTmp=' + str(managed / 'Unity.TextMeshPro.dll')]
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / 'build.log').write_text(result.stdout)
        if result.returncode:
            print(result.stdout)
            raise SystemExit('FAIL compilation ' + name)
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    shutil.copytree(native, project / 'Assets/NativeOverlays635')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'
    path.write_text(json.dumps(manifest, indent=2) + '\n')
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                             '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')],
                            stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    report = Path(manifest['result'])
    if report.exists():
        print(report.read_text(), flush=True)
    if result.returncode or not report.exists():
        raise SystemExit('FAIL native overlay runtime; evidence: ' + str(run))
    print('PASS serialized native mage overlay source/playback; evidence: ' + str(run), flush=True)


if __name__ == '__main__':
    main()
