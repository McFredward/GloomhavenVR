#!/usr/bin/env python3
"""Prove exact publisher-type admission and native idle transform culling in Unity2021."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/native-actor-audit-runtime')
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    root = args.source_root.resolve()
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    game = root / 'ressources/GH_Data/Managed'
    harmony = Path.home() / '.nuget/packages/harmonyx/2.7.0/lib/net45/0Harmony.dll'
    bundle = root / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/npc_spittingdrake_assets_all.bundle'
    for required in (unity, game / 'GH.Runtime.dll', game / 'GH.Runtime.FirstPass.dll', harmony, bundle):
        if not required.is_file(): parser.error('Original game/Unity dependencies required: ' + str(required))
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    files = ['WorldUI/ActorBarPose.cs', 'Core/Perf/NativeActorPoseAudit.cs',
             'Core/Perf/ScenarioIdleAnimationBudget.cs', 'Core/Perf/ScenarioVisibleIdleSnapshot.cs',
             'Board/FigureGrab/FigureVisualMirror.cs']
    source = {Path(name).name: (root / 'src/GloomhavenVR' / name).read_text() for name in files}
    variants = [
        ('production', '', '', '', ''),
        ('mandatory-root-rejected', 'NativeActorPoseAudit.cs', 'type == typeof(CharacterManager) || ', '',
         'actual mandatory CharacterManager and original prefab components admit sparse checks'),
        ('unknown-subclass-admitted', 'NativeActorPoseAudit.cs', 'type == typeof(CharacterManager)',
         'component is CharacterManager', 'unknown native subclass cannot bypass exact audit'),
        ('unsafe-provider-admitted', 'NativeActorPoseAudit.cs', 'if (provider is IDetailDisablerProvider',
         'if (false && provider is IDetailDisablerProvider', 'unknown detail provider prevents sparse admission'),
        ('unknown-state-callback-admitted', 'ActorBarPose.cs',
         'if (behaviour == null || !NativeActorPoseAudit.AllowsStateBehaviour(behaviour))',
         'if (behaviour == null)', 'unknown current native state callback cancels transform shortcut'),
        ('active-dissolve-admitted', 'NativeActorPoseAudit.cs',
         '&& !DeathDissolve.s_DeathDissolvesInProgress.Contains((DeathDissolve)component)', '',
         'actual active native dissolve immediately cancels sparse admission'),
        ('cull-completely', 'ScenarioIdleAnimationBudget.cs',
         'Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; Applied = true;',
         'Animator.cullingMode = AnimatorCullingMode.CullCompletely; Applied = true;',
         'only transform culling is applied; original state clock stays native'),
        ('hold-not-restored', 'ScenarioIdleAnimationBudget.cs',
         '&& !HeldFigures.Owns(Actor) && !NetHeldFigures.Owns(Actor)', '&& true',
         'local held native actor restores exact original animator mode'),
        ('native-action-not-restored', 'ScenarioIdleAnimationBudget.cs',
         'private static void Prefix(Animator animator) => ScenarioIdleAnimationBudget.NativeAction(animator);',
         'private static void Prefix(Animator animator) { }',
         'real MF.AnimatorPlay restores before native nonloop dispatch'),
        ('native-action-fault-escapes', 'ScenarioIdleAnimationBudget.cs',
         'try { if (animator != null && _driver != null && _driver.NativeActionsReady) _driver.NativeAction(animator); }\n        catch (Exception error) { FailOpen(error); }',
         'try { if (animator != null && _driver != null && _driver.NativeActionsReady) _driver.NativeAction(animator); }\n        catch (Exception error) { throw new Exception("optional owner fault never escapes or blocks native action dispatch", error); }',
         'optional owner fault never escapes or blocks native action dispatch'),
        ('foreign-mode-overwritten', 'ScenarioIdleAnimationBudget.cs',
         'if (Applied && Animator != null && Animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms)',
         'if (Applied && Animator != null)',
         'foreign animator culling edit survives owner restoration'),
        ('visible-idle-mask-not-restored', 'ScenarioVisibleIdleSnapshot.cs',
         'if (Masked && Source != null && Source.forceRenderingOff) Source.forceRenderingOff = false;',
         '/* injected: camera mask survives */', 'visible idle masks restore after each real camera'),
        ('visible-idle-proxy-not-drawn', 'ScenarioVisibleIdleSnapshot.cs',
         'surface.Proxy.enabled = true;', 'surface.Proxy.enabled = false;',
         'visible idle proxy retains visible original skin pixels'),
        ('visible-idle-native-mesh-written', 'ScenarioVisibleIdleSnapshot.cs',
         'surface.Source.BakeMesh(surface.Baked, false);',
         'surface.Source.BakeMesh(surface.Baked, false); surface.Source.sharedMesh = surface.Baked;',
         'visible idle retains native enabled state and mesh identity during culling'),
        ('visible-idle-action-mask-deferred', 'ScenarioIdleAnimationBudget.cs',
         'Visible?.Tick(false, 0f);', '/* injected: visible mask release deferred */',
         'native action immediately releases visible idle masks before continuation'),
        ('eventful-idle-admitted', 'ActorBarPose.cs', 'if (clip.events.Length == 0) _eventFreeIdleLoops.Add(clip);',
         'if (true) _eventFreeIdleLoops.Add(clip);', 'native eventful idle remains fully evaluated'),
    ]
    if args.case:
        unknown = set(args.case) - {v[0] for v in variants}
        if unknown: parser.error('Unknown cases: ' + ', '.join(sorted(unknown)))
        variants = [v for v in variants if v[0] in args.case]
    proof = {name: hashlib.sha256(code.encode()).hexdigest() for name, code in source.items()}
    for path in (game / 'GH.Runtime.dll', game / 'GH.Runtime.FirstPass.dll', game / 'ThirdParty.dll', bundle):
        proof[str(path.name)] = hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps(proof, indent=2) + '\n')
    fixture = root / 'scripts/native-actor-audit-runtime'
    proof.update({str(path.relative_to(root)): hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()
                  for path in (*fixture.glob('*.cs'), fixture / 'Editor/PoseRunner.cs',
                               fixture / 'export-components.py', Path(__file__).resolve())})
    (run / 'source-hashes.json').write_text(json.dumps(proof, indent=2) + '\n')
    deps = run / 'native-dependencies'; deps.mkdir()
    for file in (harmony, Path.home() / '.nuget/packages/monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll',
                 Path.home() / '.nuget/packages/monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll',
                 Path.home() / '.nuget/packages/mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll'):
        shutil.copyfile(file, deps / file.name)
    harmony = deps / '0Harmony.dll'
    metadata = run / 'original-native-components.json'
    subprocess.run([str(Path.home() / 'unitypy-venv/bin/python'), str(fixture / 'export-components.py'),
                    str(bundle), str(metadata)], check=True)
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / 'production'; production.mkdir(parents=True)
        for target, code in source.items():
            if target == filename:
                if code.count(before) != 1: raise SystemExit('Mutation seam drift: ' + name)
                code = code.replace(before, after, 1)
            (production / target).write_text(code)
        shutil.copyfile(fixture / 'Audit.csproj', build / 'Audit.csproj')
        assembly = 'NativeActorAudit_' + name.replace('-', '_')
        command = [str(Path.home() / '.dotnet/dotnet'), 'build', str(build / 'Audit.csproj'), '-c', 'Release',
                   '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
                   '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
                   '-p:GameManaged=' + str(game), '-p:HarmonyPath=' + str(harmony)]
        result = subprocess.run(command, text=True, capture_output=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation is never a passing control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2))
    project = run / 'unity'
    for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'): (project / directory).mkdir(parents=True)
    # Import exact publisher assemblies and case scripts as real Unity MonoScripts.
    # Assembly.Load alone is insufficient for native Animator state behaviours/events.
    for entry in manifest['cases']:
        shutil.copyfile(entry['dll'], project / 'Assets/Plugins' / Path(entry['dll']).name)
    for dll in game.glob('*.dll'):
        if (dll.name.startswith(('UnityEngine.', 'System.')) and dll.name not in ('UnityEngine.UI.dll', 'System.Runtime.CompilerServices.Unsafe.dll')) or dll.name in ('UnityEngine.dll', 'mscorlib.dll', 'netstandard.dll'):
            continue
        shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
    for dll in deps.glob('*.dll'):
        shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
    shutil.copyfile(fixture / 'Editor/PoseRunner.cs', project / 'Assets/Editor/PoseRunner.cs')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.' + name: '1.0.0'
        for name in ('physics', 'animation', 'assetbundle', 'imageconversion', 'particlesystem')}}))
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = ['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
               '-executeMethod', 'PoseRunner.Start', '-interactionManifest', str(manifest_path),
               '-nativeDrakeBundle', str(bundle), '-nativeComponentMetadata', str(metadata), '-nativeGameManaged', str(game), '-nativeHarmony', str(harmony),
               '-evidenceRoot', str(run), '-logFile', str(run / 'unity.log')]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=600)
    receipt = Path(manifest['result'])
    if receipt.is_file(): print(receipt.read_text(), end='')
    if result.returncode or not receipt.is_file(): raise SystemExit('FAIL native actor audit; see ' + str(run / 'unity.log'))
    print('PASS ' + str(len(variants)) + ' native production/causal variants; evidence: ' + str(run))


if __name__ == '__main__': main()
