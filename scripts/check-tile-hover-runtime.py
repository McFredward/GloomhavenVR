#!/usr/bin/env python3
"""Exercise production laser hover admission against the shipped native hex footprint."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def method(source, signature):
    start = source.index(signature); end = source.index('{', start) + 1; depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end]


def retain_generated_package_source(run):
    # InputSystem's SDK source directory happens to be named Remote. It is freshly generated
    # package content, not hardware evidence or a borrowed input, but the shared cleanup guard
    # correctly refuses to infer that. Preserve it verbatim outside Library before cleanup.
    source = run / 'unity/Library/PackageCache/com.unity.inputsystem@1.4.4/InputSystem/Devices/Remote'
    if not source.exists(): return
    files = [path for path in source.rglob('*') if path.is_file()]
    assert source.is_dir() and not source.is_symlink() and not any(path.is_symlink() for path in source.rglob('*'))
    assert not any(path.name == '.keep' for path in source.rglob('*'))
    hashes = {str(path.relative_to(source)): hashlib.sha256(path.read_bytes()).hexdigest() for path in files}
    retained = run / 'Sources/InputSystemDeviceRemote'; retained.parent.mkdir(exist_ok=True)
    source.rename(retained)
    assert all(hashlib.sha256((retained / name).read_bytes()).hexdigest() == digest for name, digest in hashes.items())
    (run / 'generated-package-source-retention.json').write_text(json.dumps({'original': str(source), 'retained': str(retained), 'sha256': hashes}, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/tile-hover-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--case', action='append')
    args = parser.parse_args(); root = args.source_root.resolve()
    fixture = ROOT / 'scripts/tile-hover-runtime'
    patch_path = root / 'src/GloomhavenVR/Board/Patches/HoverPickPatch.cs'
    admission_path = root / 'src/GloomhavenVR/Board/TileHoverAdmission.cs'
    pick_path = root / 'src/GloomhavenVR/Board/BoardPick.cs'
    patch = patch_path.read_text()
    assert patch.count('if (UIManager.IsPointerOverUI') == 1
    # The UI gate is the only modified production API seam. Controller identity, native
    # lists/IHoverable, native physics and the complete hover dispatch execute unchanged.
    patch = patch.replace('if (UIManager.IsPointerOverUI', 'if (PointerBoundary.IsPointerOverUI')
    native_pick = method(pick_path.read_text(), 'public static bool TryGetHoveredTile(')
    pick = 'using UnityEngine; namespace GloomhavenVR.Board { internal static partial class BoardPick {\n' + native_pick + '\n}}\n'
    admission = admission_path.read_text()
    sources = {'HoverPickPatch.cs': patch, 'TileHoverAdmission.cs': admission, 'NativePickResolution.cs': pick}
    variants = [('production', sources, '')]
    replacements = [
        ('collider-only-old-path', 'HoverPickPatch.cs', 'if (BoardPick.Source == BoardPick.PickSource.Far)', 'if (BoardPick.Source == BoardPick.PickSource.Near)', 'off-tile laser drains original hover exactly once'),
        ('off-tile-exit-lost', 'HoverPickPatch.cs', 'ExitAll(cursorTargets, "the laser does not point at a native hex containing this prop");', '/* exit lost */', 'off-tile laser drains original hover exactly once'),
        ('same-prefab-false-membership', 'TileHoverAdmission.cs', 'string.Equals(member.PropGuid, original.PropGuid, StringComparison.Ordinal)', 'string.Equals(member.PrefabName, original.PrefabName, StringComparison.Ordinal)', 'off-tile laser drains original hover exactly once'),
        ('active-selection-mask-leak', 'HoverPickPatch.cs', 'controller.m_HexSelectionRaycastLayer, hitObject)', 'controller.m_ActiveSelectionRaycastLayer, hitObject)', 'passive tooltip keeps the native hex mask across active selection changes'),
        ('spawner-footprint-lost', 'TileHoverAdmission.cs', 'if (spawner != null && Same(spawner.Prop, original, name))', 'if (spawner == null && Same(null, original, name))', 'native spawner tile membership remains hoverable'),
    ]
    for name, file, before, after, expected in replacements:
        assert sources[file].count(before) == 1, 'Mutation binding drift: ' + name
        changed = dict(sources); changed[file] = changed[file].replace(before, after, 1)
        variants.append((name, changed, expected))
    if args.case:
        selected = set(args.case); variants = [variant for variant in variants if variant[0] in selected]
        assert len(variants) == len(selected), 'Unknown selected case'
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('evidence: ' + str(run) + '\n')
    process = None; code = 1
    inputs = [patch_path, admission_path, pick_path, Path(__file__).resolve(), *[path for path in fixture.rglob('*') if path.is_file()]]
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}
    (run / 'source-hashes.json').write_text(json.dumps({'sha256': hashes, 'cases': [case[0] for case in variants], 'boundaries': [
        'Complete production HoverPickPatch and TileHoverAdmission execute with the original game HoverRegisterer lists, IHoverable interface, Controller identity and rules objects.',
        'The native UI gate, hardware hand sampling and logging are explicit seams; native physics resolves both the controlled ray and production passive hex-mask query.',
        'Original game Hex mesh and BearTrap box are freshly recovered read-only. No native gameplay controller callbacks, multiplayer session or headset image is claimed.',
    ]}, indent=2) + '\n')
    try:
        managed = root / 'ressources/GH_Data/Managed'
        dependencies = [Path.home() / '.nuget/packages' / name for name in (
            'harmonyx/2.7.0/lib/net45/0Harmony.dll', 'monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll',
            'monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll', 'mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll')]
        subprocess.run([str(Path.home() / 'unitypy-venv/bin/python'), str(fixture / 'export-native.py'), str(managed.parent / 'resources.assets'), str(run / 'native-hex.json')], check=True)
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')

        def compile_case(variant):
            name, texts, expected = variant; case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for filename, text in texts.items(): (production / filename).write_text(text)
            project = case / 'TileHover.csproj'; shutil.copyfile(fixture / 'TileHover.csproj', project)
            assembly = 'TileHover_' + name.replace('-', '_')
            command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
                '-p:GameManaged=' + str(managed), '-p:HarmonyPath=' + str(dependencies[0])]
            result = subprocess.run(command, capture_output=True, text=True, timeout=180)
            (case / 'build.log').write_text(result.stdout + result.stderr)
            if result.returncode: raise RuntimeError(result.stdout + result.stderr + '\nCompilation failure is not a passing negative control')
            return {'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected}

        with ThreadPoolExecutor(max_workers=3) as pool: cases = list(pool.map(compile_case, variants))
        manifest = {'result': str(run / 'results.txt'), 'native': str(run / 'native-hex.json'), 'cases': cases}
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Assets/Plugins').mkdir(); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
        shutil.copyfile(fixture / 'Editor/TileHoverRunner.cs', project / 'Assets/Editor/TileHoverRunner.cs')
        # Copy read-only managed native dependencies; engine modules come from the real editor.
        for dll in managed.glob('*.dll'):
            if dll.name.startswith(('UnityEngine', 'Unity.', 'System', 'Microsoft', 'Mono.', 'mscorlib', 'netstandard')): continue
            shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
        for name in ('UnityEngine.UI.dll', 'Unity.TextMeshPro.dll'): shutil.copyfile(managed / name, project / 'Assets/Plugins' / name)
        for name in ('Unity.Addressables.dll', 'Unity.ResourceManager.dll', 'Unity.Timeline.dll', 'Unity.Postprocessing.Runtime.dll', 'UnityEngine.SpatialTracking.dll', 'System.Runtime.CompilerServices.Unsafe.dll'):
            shutil.copyfile(managed / name, project / 'Assets/Plugins' / name)
        for dependency in dependencies: shutil.copyfile(dependency, project / 'Assets/Plugins' / dependency.name)
        modules = ('ui', 'physics', 'animation', 'imageconversion', 'jsonserialize', 'assetbundle', 'audio', 'director', 'particlesystem', 'terrain', 'video', 'cloth', 'physics2d', 'unitywebrequest', 'unitywebrequestassetbundle', 'unitywebrequestaudio', 'unitywebrequesttexture', 'unitywebrequestwww', 'ai', 'imgui', 'xr')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.inputsystem': '1.4.4', **{'com.unity.modules.' + name: '1.0.0' for name in modules}}}) + '\n')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        command = [str(args.unity), '-batchmode', '-nographics', '-disable-assembly-updater', '-projectPath', str(project), '-executeMethod', 'TileHoverRunner.Start', '-tileHoverManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
        process = subprocess.Popen(command, start_new_session=True, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        unity_code = process.wait(timeout=240)
        (run / 'unity-exit-code.txt').write_text(str(unity_code) + '\n')
        report = run / 'results.txt'
        if report.is_file(): print(report.read_text(), end='')
        changed = [str(path) for path in inputs if hashlib.sha256(path.read_bytes()).hexdigest() != hashes[str(path)]]
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': not changed, 'changed': changed}, indent=2) + '\n')
        if unity_code or changed or not report.is_file(): raise RuntimeError('Native tile hover failed; inspect ' + str(run))
        code = 0; print('PASS: ' + str(len(variants)) + ' native tile-hover variants; evidence: ' + str(run))
    finally:
        if process is not None:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait()
        retain_generated_package_source(run)
        receipt = cleanup_completed_run('tile-hover-runtime', code, completion, root, process_reaped=True)
        (run / 'completion-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__': main()
