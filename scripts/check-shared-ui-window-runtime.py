#!/usr/bin/env python3
"""Run complete production veil/shared reads and actual LateTick against original UIWindow in Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/shared-ui-window-runtime')
    parser.add_argument('--case', action='append', help='Partial development run; repeat to choose cases')
    args = parser.parse_args()
    root = args.source_root.resolve()
    conversion = root / 'src/GloomhavenVR/WorldUI/Conversion'
    fixture = root / 'scripts/shared-ui-window-runtime'
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    managed = unity.parent / 'Data/Managed'
    game = root / 'ressources/GH_Data/Managed'
    cache = Path.home() / '.nuget/packages'
    dependencies = [cache / 'harmonyx/2.7.0/lib/net45/0Harmony.dll',
                    cache / 'monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll',
                    cache / 'monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll',
                    cache / 'mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll']
    sources = {name: (conversion / name).read_text() for name in (
        'UiHierarchyInventory.cs', 'CanvasConversion.9e.HiddenWindowVeil.cs', 'CanvasConversion.9e.SharedWindowReads.cs')}
    lifecycle = (conversion / 'CanvasConversion.4.Lifecycle.cs').read_text()
    late = method(lifecycle, 'internal static void LateTick()')
    assert 'using var sharedWindowReads = BeginSharedVeilWindowReads();' in late, 'Shared scope must bracket the real production panel loop'
    assert late.index('BeginSharedVeilWindowReads()') < late.index('for (int i = 0; i < Active.Count; i++)')
    sources['LateTick.cs'] = 'using UnityEngine; using GloomhavenVR.Core; namespace GloomhavenVR.WorldUI { internal static partial class CanvasConversion {\n' + late + '\n}}'
    original_hashes = {name: hashlib.sha256(code.encode()).hexdigest() for name, code in sources.items()}
    original_hashes['CanvasConversion.4.Lifecycle.cs'] = hashlib.sha256(lifecycle.encode()).hexdigest()
    # Test-only read counters: complete production methods execute with no substituted decision.
    for filename in ('CanvasConversion.9e.HiddenWindowVeil.cs', 'CanvasConversion.9e.SharedWindowReads.cs'):
        code = sources[filename]
        anchor = '        foreach (UIWindow window in registered)\n        {'
        assert code.count(anchor) == 1, 'Registry iteration binding drift: ' + filename
        code = code.replace(anchor, '        Proof.RegistryScans++;\n' + anchor + '\n            Proof.RegistryMembers++;')
        if filename.endswith('HiddenWindowVeil.cs'):
            code = code.replace('            target.GetComponentsInChildren(includeInactive: true, state.Windows);', '            Proof.ComponentCaptures++;\n            target.GetComponentsInChildren(includeInactive: true, state.Windows);')
            code = code.replace('            var group = t.GetComponent<CanvasGroup>();', '            Proof.GroupReads++;\n            var group = t.GetComponent<CanvasGroup>();')
        else:
            code = code.replace('                entry.Root.GetComponentsInChildren(includeInactive: true, owner.Windows);', '                Proof.ComponentCaptures++;\n                entry.Root.GetComponentsInChildren(includeInactive: true, owner.Windows);')
            code = code.replace('                CanvasGroup? group = node.GetComponent<CanvasGroup>();', '                Proof.GroupReads++;\n                CanvasGroup? group = node.GetComponent<CanvasGroup>();')
        sources[filename] = code
    variants = [
        ('production', '', '', '', ''),
        ('unsupported-version-fallback', 'CanvasConversion.9e.SharedWindowReads.cs',
         'if (added != empty && read(probe) != added) return read;',
         'if (added != empty && read(probe) != added) return null;', ''),
        ('repeated-registry-scan', 'CanvasConversion.9e.SharedWindowReads.cs',
         'if (!registryChanged && !SharedVeilRootsDirty', 'if (false && !registryChanged && !SharedVeilRootsDirty',
         'one exact shared registry distribution replaces 20 independent scans'),
        ('recreated-route-storage', 'CanvasConversion.9e.SharedWindowReads.cs',
         '        SharedVeilRootIndices.Clear();\n        SharedVeilRootHeads.Clear();',
         '        SharedVeilRootIndices.Clear();\n        SharedVeilRootHeads = new(16);',
         'warmed routing storage is reused'),
        ('count-only-registry', 'CanvasConversion.9e.SharedWindowReads.cs',
         'int version = NativeWindowRegistryVersion(registered);', 'int version = registered.Count;',
         'same-count native replacement invalidates the exact shared read'),
        ('missing-hierarchy-invalidation', 'CanvasConversion.9e.SharedWindowReads.cs',
         '&& SharedVeilHierarchyRevision == UiHierarchyInventory.HierarchyRevision', '&& true',
         'same-frame native reparenting reroutes original window identities'),
        ('missing-disabled-component-recapture', 'CanvasConversion.9e.SharedWindowReads.cs',
         '|| owner.SharedRegistryVersion != version || topologyChanged', '|| topologyChanged',
         'registry edit recaptures newly disabled native component on an existing transform'),
        ('repeated-group-reads', 'CanvasConversion.9e.HiddenWindowVeil.cs',
         '? SharedGroupChainAlphaBelow(g.transform, v.Window.transform)', '? GroupChainAlphaBelow(g.transform, v.Window.transform)',
         'shared pure group reads remove repeated native ancestor component reads'),
        ('missing-existing-graphic-recapture', 'CanvasConversion.9e.HiddenWindowVeil.cs',
         'if (veil.GraphicRevision == revision || veil.Window == null) continue;',
         'if (veil.GraphicRevision != int.MinValue || veil.Window == null) continue;',
         'new graphic under an existing hidden window is veiled in the same frame'),
        ('foreign-alpha-lost', 'CanvasConversion.9e.HiddenWindowVeil.cs',
         '                    h.Alpha = a;', '                    h.Alpha = 1f;',
         'every intermediate foreign materialise alpha is learned and reasserted'),
        ('stale-window-hold', 'CanvasConversion.9e.HiddenWindowVeil.cs',
         '\n                    || !v.Window.transform.IsChildOf(target)', '',
         'reparented window releases its former panel hold before taking the current one'),
        ('option-off-ignored', 'CanvasConversion.9e.HiddenWindowVeil.cs',
         'if (PerfConfig.SharedUiWindowReadsOn && TryRefreshSharedVeilWindowInventory(state, target))',
         'if (TryRefreshSharedVeilWindowInventory(state, target))',
         'option-off between native callbacks immediately resumes independent window reads'),
        ('scope-not-disposed', 'LateTick.cs',
         'using var sharedWindowReads = BeginSharedVeilWindowReads();', 'var sharedWindowReads = BeginSharedVeilWindowReads();',
         'shared pass releases all route and registry references'),
    ]
    if args.case:
        known = {entry[0] for entry in variants}
        if set(args.case) - known:
            parser.error('Unknown case: ' + ', '.join(set(args.case) - known))
        variants = [entry for entry in variants if entry[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    proof = dict(original_hashes)
    for path in [Path(__file__).resolve(), *fixture.glob('*.cs'), fixture / 'SharedUi.csproj', fixture / 'Editor/SharedUiRunner.cs']:
        proof[str(path.relative_to(root))] = hashlib.sha256(path.read_bytes()).hexdigest()
    for path in [game / 'GH.Runtime.dll', *dependencies]:
        proof[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps(proof, indent=2) + '\n')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    dotnet = os.environ.get('DOTNET_EXE', str(Path.home() / '.dotnet/dotnet'))
    for name, filename, before, after, expected in variants:
        case = run / name
        generated = case / 'production'
        generated.mkdir(parents=True)
        for target, code in sources.items():
            if target == filename:
                if code.count(before) != 1:
                    raise RuntimeError('Mutation binding drift: ' + name)
                code = code.replace(before, after, 1)
                if name == 'recreated-route-storage':
                    declaration = 'private static readonly Dictionary<Transform, int> SharedVeilRootHeads'
                    assert code.count(declaration) == 1
                    code = code.replace(declaration, 'private static Dictionary<Transform, int> SharedVeilRootHeads', 1)
            (generated / target).write_text(code)
        shutil.copyfile(fixture / 'SharedUi.csproj', case / 'SharedUi.csproj')
        assembly = 'SharedUi_' + name.replace('-', '_')
        command = [dotnet, 'build', str(case / 'SharedUi.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                   '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(generated),
                   '-p:UnityManaged=' + str(managed), '-p:GameManaged=' + str(game), '-p:HarmonyPath=' + str(dependencies[0])]
        result = subprocess.run(command, text=True, capture_output=True)
        (case / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr + '\nCompilation is not a passing causal control')
        manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    project = run / 'unity'
    for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'):
        (project / directory).mkdir(parents=True)
    for entry in manifest['cases']:
        target = project / 'Assets/Plugins' / Path(entry['dll']).name
        shutil.copyfile(entry['dll'], target)
        entry['dll'] = str(target)
    for dll in game.glob('*.dll'):
        if (dll.name.startswith(('UnityEngine.', 'System.')) and dll.name not in ('UnityEngine.UI.dll', 'System.Runtime.CompilerServices.Unsafe.dll')) or dll.name in ('UnityEngine.dll', 'mscorlib.dll', 'netstandard.dll'):
            continue
        shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
    for dll in dependencies:
        shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
    shutil.copyfile(fixture / 'Editor/SharedUiRunner.cs', project / 'Assets/Editor/SharedUiRunner.cs')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0'}}))
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'
    path.write_text(json.dumps(manifest, indent=2) + '\n')
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                             '-executeMethod', 'SharedUiRunner.Start', '-interactionManifest', str(path),
                             '-logFile', str(run / 'unity.log')], timeout=240)
    if (run / 'results.txt').exists():
        print((run / 'results.txt').read_text())
    print('Evidence: ' + str(run))
    raise SystemExit(result.returncode)


if __name__ == '__main__':
    main()
