#!/usr/bin/env python3
"""Run the live production environment driver and shader in actual Unity graphics.

Native scene type/config boundaries are explicit surrogates; meshes/materials are
small generated fixtures. This proves engine ownership, runtime callbacks, native
cloning and pixels for the fixture, not native-game artwork, HMD parity or FPS.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def material_repair_binding(source):
    signature = 'private static bool TryFinishDirect(MaterialLoaderData data, Renderer r)'
    assert source.count(signature) == 1, 'Native material repair method binding drift'
    start = source.index(signature)
    body = source[start:source.index('\n        }\n',start)]
    steps = ('r.sharedMaterials = final;', 'r.enabled = true;', 'ScenarioEnvironmentBudget.MaterialReady(r);', 'return true;')
    assert all(body.count(step) == 1 for step in steps), 'Successful native repair must publish environment readiness once'
    positions = [body.index(step) for step in steps]
    assert positions == sorted(positions), 'Environment readiness must follow restored native material/enabled state and precede successful continuation'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT/'.planning/debug/environment-budget-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--production-only', action='store_true', help='Partial development run, omits negative controls')
    parser.add_argument('--case', action='append', help='Partial development run; repeat to select named variants')
    args = parser.parse_args()
    source_path = args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentBudget.cs'
    shader_path = args.source_root/'unity/GloomhavenVR.Assets/Assets/Bundle/Environments/ScenarioSimpleEnvironment.shader'
    repair_path = args.source_root/'src/GloomhavenVR/Core/MaterialLoaderHeal.cs'
    floor_path = args.source_root/'src/GloomhavenVR/Core/WallFade/WallFloorTile.cs'
    source, shader, repair, floor = source_path.read_text(), shader_path.read_text(), repair_path.read_text(), floor_path.read_text()
    material_repair_binding(repair)
    try:
        material_repair_binding(repair.replace('ScenarioEnvironmentBudget.MaterialReady(r);','/* injected: native repair edge removed */'))
    except AssertionError as error:
        assert 'publish environment readiness' in str(error), 'Removed-edge control failed for an unrelated reason'
    else: raise SystemExit('Native material repair removed-edge negative control escaped')
    assert 'StaticBatchingUtility' not in source and 'SetStaticBatchInfo' not in source, 'Native sources must not acquire Unity internal static-batch state'
    assert 'Camera.onPreCull += OnPreCull;' in source and 'Camera.onPostRender -= OnPostRender;' in source, 'Draw leases require paired real rendering hooks'
    variants = [('production',source,'')]
    if not args.production_only:
        changes = [
            ('foreign-ui-admitted','node.GetComponent<Canvas>() != null','false','foreign UI material stays untouched',2),
            ('foreign-actor-admitted','node.GetComponent<ActorBehaviour>() != null','false','foreign actor/UI/held/water/foliage/dissolve/native scope exclusions retain original rendering: CV_Floor_Base_Actor',2),
            ('ambient-loop-is-enough','AmbientIdentity(node, tile.transform)','true','combat effects never become ambience merely because they loop',1),
            ('ambient-never-paused','System.Pause(false);','/* injected: solver kept playing */','zero ambient budget pauses exact native families',1),
            ('native-material-not-restored','if (IsApplied()) Renderer.sharedMaterials = Original;','if (IsApplied()) Renderer.sharedMaterials = Applied!;','owned native material restore takes effect immediately',1),
            ('unknown-native-clone-not-restored','RestoreClonedMaterials();','/* injected: unknown native clones skipped */','unannounced native clone restores its original material',2),
            ('new-multi-material-not-validated','_materialScratch.Count == 1','true','real camera pre-cull rejects newly multi-material originals',1),
            ('source-property-block-not-validated','r.gameObject.activeInHierarchy && !r.HasPropertyBlock()','r.gameObject.activeInHierarchy','real camera pre-cull rejects native per-renderer effect properties',1),
            ('source-transform-not-validated','inverse * r.transform.localToWorldMatrix == Matrices[i]','true','real camera pre-cull rejects changed source transforms',1),
            ('common-render-flags-not-validated','SameRenderFlags(r, Renderer)','SameRenderFlags(r, Sources[0].Renderer)','common source render-flag changes cannot retain old combined rendering flags',1),
            ('post-render-does-not-release','foreach (Batch batch in _batches) batch.Unmask();','/* injected: batch lease survives post */','real camera callback pair owns masks only during rendering',2),
            ('normal-chunks-not-bounded','DrainBatches(loading ? int.MaxValue : 2);','DrainBatches(int.MaxValue);','normal update publishes at most two small chunks',1),
            ('oversized-native-member-portions','private const int MaxBatchMembers = 24;','private const int MaxBatchMembers = 64;','55 same-cell native floors split into bounded 24-member portions',1),
            ('loading-drain-missing','DrainBatches(int.MaxValue);','/* injected: loading ended before mesh prep */','compatible static floor geometry creates one render substitute',1),
            ('ancestor-floor-union','bool identity = FloorIdentity(mesh.name) || FloorIdentity(renderer.name);','bool identity = FloorIdentity(mesh.name) || FloorIdentity(renderer.name) || (renderer.transform.parent != null && FloorIdentity(renderer.transform.parent.name));','native scope exclusions retain original rendering: MountedDecoration',1),
            ('elevated-pillar-name-bypass','return WallFloorTile.Judge(new WallFloorTile.Plate(min.y, max.y, max.x - min.x, max.z - min.z), 0f)\n            == WallFloorTile.Verdict.FloorTile;','return true;','native scope exclusions retain original rendering: CV_Floor_Base_Raised',1),
            ('native-continuation-fault-guard-removed','try { _driver?.MaterialReady(renderer); }\n        catch (Exception error) { StopAfterFailure(error); }','_driver?.MaterialReady(renderer);','generated shader resolver fault',1),
        ]
        for name, before, after, expected, occurrences in changes:
            assert source.count(before) == occurrences, 'negative control binding drift: '+name
            variants.append((name,source.replace(before,after),expected))
        before = 'if (!surface.Mesh.isReadable) { _unreadable++; continue; }'
        guard = '!s.Mesh.isReadable || '
        assert source.count(before) == 1 and source.count(guard) == 1, 'negative control binding drift: unreadable geometry'
        variants.append(('unreadable-mesh-not-excluded',source.replace(before,'/* injected: unreadable source admitted */').replace(guard,''),'unreadable'))
        before = 'if (!identity) return false;'
        assert source.count(before) == 1, 'negative control binding drift: floor provenance'
        variants.append(('non-floor-substitute-admitted',source.replace(before,'/* injected: direct floor identity omitted */'),'non-floor opaque trim preserves native material'))
    if args.case:
        unknown = set(args.case)-{name for name,_,_ in variants}
        if unknown: raise SystemExit('Unknown selected case: '+', '.join(sorted(unknown)))
        variants = [entry for entry in variants if entry[0] in args.case]
    partial = args.production_only or bool(args.case)
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    fixture = ROOT/'tests/environment-budget-runtime'
    manifest = {'result':str(run/'results.txt'),'cases':[]}
    (run/'source-hashes.json').write_text(json.dumps({'root':str(args.source_root.resolve()),'sha256':{str(source_path):hashlib.sha256(source.encode()).hexdigest(),str(shader_path):hashlib.sha256(shader.encode()).hexdigest(),str(repair_path):hashlib.sha256(repair.encode()).hexdigest(),str(floor_path):hashlib.sha256(floor.encode()).hexdigest()},'coverage':'partial' if partial else 'production-and-negative-controls','cases':[name for name,_,_ in variants],'limits':['Native scene classes/config are boundary surrogates.','Actual Unity meshes, renderer masks, shaders, pixels, cloning and camera callbacks are executed.','Native material healer integration is source-bound; its native callbacks are not executed.','Native game scenes, OpenXR HMD images and FPS remain hardware-open.']},indent=2)+'\n')
    (run/'repair-binding.json').write_text(json.dumps({'native-success-order':'sharedMaterials / enabled / MaterialReady / return true','removed-edge-negative-control':'rejected'},indent=2)+'\n')
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name, value, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        (production/'Environment.cs').write_text(value)
        (production/'WallFloorTile.cs').write_text(floor)
        project = build/'Environment.csproj'; shutil.copyfile(fixture/'Environment.csproj',project)
        assembly = 'EnvironmentBudget_'+name.replace('-','_')
        command = [dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')]
        result = subprocess.run(command,capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+'\nA compilation failure cannot pass as a negative control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path = run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    unity_project = run/'unity'; (unity_project/'Assets/Editor').mkdir(parents=True); (unity_project/'Packages').mkdir(); (unity_project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/EnvironmentRunner.cs',unity_project/'Assets/Editor/EnvironmentRunner.cs')
    shutil.copyfile(fixture/'NativeMaterials.shader',unity_project/'Assets/NativeMaterials.shader')
    (unity_project/'Assets/ScenarioSimpleEnvironment.shader').write_text(shader)
    (unity_project/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (unity_project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = [str(args.unity),'-batchmode','-force-glcore','-projectPath',str(unity_project),'-executeMethod','EnvironmentRunner.Start','-environmentManifest',str(manifest_path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run','-a']+command
    try:
        result = subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    finally:
        # Keep complete source, compiled test assemblies and logs; native API copies
        # are disabled and disposable import caches never accumulate across runs.
        for cache in ('Library','Temp'): shutil.rmtree(unity_project/cache,ignore_errors=True)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(),end='')
    print('PASS native material-repair readiness binding and removed-edge negative control')
    (run/'unity-exit-code.txt').write_text(str(result.returncode)+'\n')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity runtime; see '+str(run/'unity.log'))
    print(('PARTIAL PASS' if partial else 'PASS')+': '+str(len(variants))+' actual production/negative variants; evidence: '+str(run))


if __name__ == '__main__': main()
