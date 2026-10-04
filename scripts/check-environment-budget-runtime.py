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
import re

ROOT = Path(__file__).resolve().parents[1]


def native_masonry(root, run):
    """Extract original channels verbatim; native artwork is never checked in."""
    python = Path.home()/"unitypy-venv/bin/python"
    bundle = root/"ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/pcg_databases_assets_assets/pcg/pcg_crypt.asset.bundle"
    code = r'''
import UnityPy, hashlib, json, sys
from pathlib import Path
from UnityPy.helpers.MeshHelper import MeshHandler
p=Path(sys.argv[1]); env=UnityPy.load(str(p))
obj=next(x for x in env.objects if x.type.name=="Mesh" and x.read().m_Name=="EN_CR_Pillar_Thin")
mesh=obj.read(); assert mesh.m_IsReadable and len(mesh.m_SubMeshes)==1
h=MeshHandler(mesh); h.process()
vector=lambda values,keys:[dict(zip(keys,value)) for value in values]
data={"name":mesh.m_Name,"vertices":vector(h.m_Vertices,"xyz"),"normals":vector(h.m_Normals,"xyz"),"uv":vector(h.m_UV0,"xy"),"triangles":[i for triangle in h.get_triangles()[0] for i in triangle]}
Path(sys.argv[2]).write_text(json.dumps(data)+"\n")
large_obj=next(x for x in env.objects if x.type.name=="Mesh" and x.read().m_Name=="EN_CR_Pillar_Large")
large=large_obj.read(); lh=MeshHandler(large); lh.process()
Path(sys.argv[2]).with_name("native-pillar-large.json").write_text(json.dumps({"name":large.m_Name,"vertices":vector(lh.m_Vertices,"xyz"),"normals":vector(lh.m_Normals,"xyz"),"uv":vector(lh.m_UV0,"xy"),"triangles":[i for triangle in lh.get_triangles()[0] for i in triangle]})+"\n")
Path(sys.argv[3]).write_text(json.dumps({"bundle":str(p),"sha256":hashlib.sha256(p.read_bytes()).hexdigest(),"mesh_path_id":obj.path_id,"name":mesh.m_Name,"native_readable":mesh.m_IsReadable,"submeshes":len(mesh.m_SubMeshes),"vertices":len(h.m_Vertices),"triangles":len(data["triangles"])/3,"second_mesh":{"name":large.m_Name,"path_id":large_obj.path_id,"vertices":len(lh.m_Vertices)},"transfer":"original vertex, normal, UV and index channels; no OBJ coordinate/winding conversion","limits":"native Windows shader, material/art textures and procedural controllers are NOT executed by this GL fixture"},indent=2)+"\n")
floor_path=p.parent.parent.parent/"pcg_materials_assets_cv_floor_basic_m.bundle"
floor_env=UnityPy.load(str(floor_path)); floor_obj=next(x for x in floor_env.objects if x.type.name=="Material" and x.read().m_Name=="CV_Floor_Basic_M")
floor=floor_obj.read(); floats=dict(floor.m_SavedProperties.m_Floats); assert floats["_WallFade_On"]==1
Path(sys.argv[2]).with_name("native-floor-material.json").write_text(json.dumps({"name":floor.m_Name,"wallFade":floats["_WallFade_On"],"cutoff":floats["_Cutoff"]})+"\n")
Path(sys.argv[2]).with_name("native-floor-material-provenance.json").write_text(json.dumps({"bundle":str(floor_path),"sha256":hashlib.sha256(floor_path.read_bytes()).hexdigest(),"material_path_id":floor_obj.path_id,"name":floor.m_Name,"original_saved_floats":floats,"shader_keywords":floor.m_ShaderKeywords,"limits":"original authored floats imported into explicit GL native API surrogate; original Windows shader bytecode is not executed"},indent=2)+"\n")
'''
    result = subprocess.run([str(python),"-c",code,str(bundle),str(run/"native-pillar.json"),str(run/"native-masonry-provenance.json")],capture_output=True,text=True)
    if result.returncode: raise SystemExit(result.stdout+result.stderr)
    return run/"native-pillar.json"


def material_repair_binding(source):
    signature = 'private static bool TryFinishDirect(MaterialLoaderData data, Renderer r)'
    assert source.count(signature) == 1, 'Native material repair method binding drift'
    start = source.index(signature)
    body = source[start:source.index('\n        }\n',start)]
    steps = ('r.sharedMaterials = final;', 'r.enabled = true;', 'ScenarioEnvironmentBudget.MaterialReady(r);', 'return true;')
    assert all(body.count(step) == 1 for step in steps), 'Successful native repair must publish environment readiness once'
    positions = [body.index(step) for step in steps]
    assert positions == sorted(positions), 'Environment readiness must follow restored native material/enabled state and precede successful continuation'


def wall_delivery(source):
    """Bind complete original delivery/texture methods, including every native MPB write."""
    assert source.count('OcclusionFade.StepFactor(AnimationDelta(frameDelta), FadeTauSeconds)') == 1, 'Actual wall tick must use the bounded presentation step'
    methods = []
    for signature in ('private void Apply(Segment seg)', 'private bool EnsureTextures()', 'private void OnEnable()', 'private void OnDisable()'):
        assert source.count(signature) == 1, 'Wall delivery extraction drift: '+signature
        start = source.index(signature)
        end = source.index('\n        }', start) + len('\n        }')
        methods.append(source[start:end])
    return ('using System; using UnityEngine; using UnityEngine.SceneManagement; namespace GloomhavenVR.Core; '
        'internal static partial class WallSegmentFade { private sealed partial class FadeDriver { '
        + '\n'.join(methods) + '\n} }\n')


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
    wall_path = args.source_root/'src/GloomhavenVR/Core/WallFade/WallSegmentFade.cs'
    clock_path = args.source_root/'src/GloomhavenVR/Core/WallFade/WallSegmentFade.AnimationClock.cs'
    trace_path = args.source_root/'src/GloomhavenVR/Core/WallFade/WallSegmentFade.DrawTrace.cs'
    occlusion_path = args.source_root/'src/GloomhavenVR/Core/OcclusionFade.cs'
    dissolve_path = args.source_root/'src/GloomhavenVR/Core/WallFade/WallSegmentFade.Dissolve.cs'
    source, shader, repair, floor = source_path.read_text(), shader_path.read_text(), repair_path.read_text(), floor_path.read_text()
    wall, clock, occlusion = wall_path.read_text(), clock_path.read_text(), occlusion_path.read_text()
    trace = trace_path.read_text()
    delivery = wall_delivery(wall)
    dissolve = dissolve_path.read_text()
    signature = 'private void DriveNativeProp(MountedProp p, float fade)'
    start = dissolve.index(signature)
    native_prop = dissolve[start:dissolve.index('\n        }',start)+len('\n        }')]
    delivery = delivery.replace('\n} }\n',native_prop+'\n} }\n')
    assert '_nativeTransitionMaps' not in wall + dissolve, 'Retired square-mask bank must not return'
    assert 'Shader.PropertyToID("_EnableOcclusionMap")' in wall, 'Original native enable binding must remain explicit'
    clock_variants, delivery_variants, trace_variants = {}, {}, {}
    wall_writes = []
    for path in (args.source_root/'src/GloomhavenVR/Core/WallFade').glob('WallSegmentFade*.cs'):
        text = path.read_text()
        for match in re.finditer(r'(?m)^\s*([\w.]+)\.SetPropertyBlock\([^\n]+\);', text):
            renderer = match.group(1)
            previous = text[:match.start()].rstrip().splitlines()[-1].strip()
            assert previous == 'ScenarioEnvironmentBudget.BeforeNativeRendererWrite('+renderer+');', 'Wall renderer effect has no synchronous release: '+str(path)
            wall_writes.append(path.name+':'+str(text.count('\n',0,match.start())+1))
    assert len(wall_writes) == 22, 'Wall setter census drift; review new primitive safety before batching'
    material_repair_binding(repair)
    try:
        material_repair_binding(repair.replace('ScenarioEnvironmentBudget.MaterialReady(r);','/* injected: native repair edge removed */'))
    except AssertionError as error:
        assert 'publish environment readiness' in str(error), 'Removed-edge control failed for an unrelated reason'
    else: raise SystemExit('Native material repair removed-edge negative control escaped')
    assert 'StaticBatchingUtility' not in source and 'SetStaticBatchInfo' not in source, 'Native sources must not acquire Unity internal static-batch state'
    assert 'Camera.onPreCull += HandlePreCull;' in source and 'Camera.onPostRender -= HandlePostRender;' in source, 'Draw leases require paired real rendering hooks'
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
            ('loading-drain-missing','DrainBatches(int.MaxValue);','/* injected: loading ended before mesh prep */','finished discovery and substitute construction release preparation immediately',1),
            ('ancestor-floor-union','bool identity = FloorIdentity(mesh.name) || FloorIdentity(renderer.name);','bool identity = FloorIdentity(mesh.name) || FloorIdentity(renderer.name) || (renderer.transform.parent != null && FloorIdentity(renderer.transform.parent.name));','native scope exclusions retain original rendering: MountedDecoration',1),
            ('elevated-pillar-name-bypass','return WallFloorTile.Judge(new WallFloorTile.Plate(min.y, max.y, max.x - min.x, max.z - min.z), 0f)\n            == WallFloorTile.Verdict.FloorTile;','return true;','native scope exclusions retain original rendering: CV_Floor_Base_Raised',1),
            ('structural-never-admitted','bool structural = !floor && StructuralIdentity(filter.sharedMesh) && !renderer.HasPropertyBlock();','bool structural = false;','audited native masonry creates a bounded structural render substitute',1),
            ('structural-command-renderer-masked','if (camera != null && camera.commandBufferCount > 0)','if (bool.Parse("false"))','native command-buffer DrawRenderer keeps the original structural renderer identity and geometry',1),
            ('structural-effect-release-missing','InvalidateBatch(id);\n            if (_surfaces.TryGetValue(id, out Surface surface))','/* injected: substitute survives write */\n            if (_surfaces.TryGetValue(id, out Surface surface))','wall effect write restores structural sources and material synchronously',1),
            ('live-floor-dissolve-not-preserved','if (NativeWallFadeEnabled(material)) return false;','/* injected: native floor shader channel lost */','live native floor wall channels retain original shaders',1),
            ('late-native-channel-not-retired','RetireChangedNativeMaterials();','/* injected: late original wall channel ignored */','late native material gate retires the simpler variant before actual camera culling',1),
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
        before = 'Mathf.Clamp(frameDelta, 0f, 1f / 30f)'
        assert clock.count(before) == 1, 'Wall visual-clock mutation binding drift'
        variants.append(('wall-hitch-finishes-animation',source,'stalled wall frame preserves rendered intermediate pixels'))
        clock_variants['wall-hitch-finishes-animation'] = clock.replace(before,'frameDelta')
        before = '_mpb.SetTexture(TilesOcclusionMapId, _noiseTex!);'
        assert delivery.count(before) == 1, 'Original wall noise-map mutation binding drift'
        variants.append(('wall-ramp-uses-binary-map',source,'stalled wall frame preserves rendered intermediate pixels'))
        delivery_variants['wall-ramp-uses-binary-map'] = delivery.replace(before,'_mpb.SetTexture(TilesOcclusionMapId, _occludedTex!);')
        variants.append(('invalid-unity-camera-messages',source.replace('HandlePreCull','OnPreCull').replace('HandlePostRender','OnPostRender'),
            'actual Unity AddComponent produces no invalid engine callback messages'))
        trace_changes = [
            ('wall-trace-normal-level-sampled', '!VRLog.Wants(VRLogLevel.Debug)', 'false', 'normal logging performs no wall draw capture', 2),
            ('wall-trace-foreign-camera-sampled', 'camera != Rig.VRRigDriver.HeadCamera', 'false', 'only the actual head camera captures wall delivery', 1),
            ('wall-trace-actual-block-not-read', 'r.GetPropertyBlock(_drawBlock);', '_drawBlock.Clear();', 'actual head-camera callback reads the delivered native renderer, shader and keyword', 1),
            ('wall-trace-slot-overrides-not-read', 'r.GetPropertyBlock(_drawSlotBlock, i);', '_drawSlotBlock.Clear();', 'draw capture exposes material-index overrides', 1),
            ('wall-trace-episode-bound-missing', '_drawEpisodeCount >= DrawTraceEpisodes && !existingSegment', 'false', 'wall draw episodes are bounded for a whole scene', 1),
            ('wall-trace-terminal-bucket-dropped', 'if (!terminal && (bucket == episode.LastBucket || episode.Samples >= DrawTraceSamples - 1)) continue;', 'if (bucket == episode.LastBucket || episode.Samples >= DrawTraceSamples - 1) continue;', 'restored zero endpoint is sampled despite sharing the last returning bucket', 1),
            ('wall-trace-endpoint-reservation-lost', 'episode.Samples >= DrawTraceSamples - 1', 'episode.Samples >= DrawTraceSamples', 'intermediate samples reserve one bounded endpoint slot', 1),
            ('wall-trace-route-reservation-lost', 'routeEpisodes >= DrawTraceRouteEpisodes', 'bool.Parse("false")', 'first native HIGH route reserves LOW and toggle-native coverage', 1),
        ]
        for name, before, after, expected, occurrences in trace_changes:
            assert trace.count(before) == occurrences, 'Wall trace negative control binding drift: '+name
            variants.append((name,source,expected))
            trace_variants[name] = trace.replace(before,after)
        native_changes = [
            ('native-high-enable-not-supplied', '_mpb.SetFloat(NativeMapEnableId, 1f);', '/* injected: native camera enable omitted */', 'original wall supplies its actual native map-enable binding', 1),
            ('native-prop-enable-not-supplied', '_mountedMpb.SetFloat(NativeMapEnableId, 1f);', '/* injected: native camera enable omitted */', 'original mounted prop supplies its actual native map-enable binding', 1),
            ('historical-cutoff-sweep-replaced', '_mpb.SetFloat(CutoffId, Mathf.Lerp(-0.15f, 1f, seg.Fade));', '_mpb.SetFloat(CutoffId, seg.HeldCutoff);', 'visible native wall delivery has multiple decreasing frames and a complete held endpoint', 1),
            ('historical-noise-uses-square-texels', 'filterMode = FilterMode.Bilinear,', 'filterMode = FilterMode.Point,', 'historical transition keeps its bilinear continuous noise texture', 1),
        ]
        for name, before, after, expected, occurrences in native_changes:
            assert delivery.count(before) == occurrences, 'Native historical delivery mutation binding drift: '+name
            variants.append((name,source,expected))
            delivery_variants[name] = delivery.replace(before,after)
    if args.case:
        unknown = set(args.case)-{name for name,_,_ in variants}
        if unknown: raise SystemExit('Unknown selected case: '+', '.join(sorted(unknown)))
        variants = [entry for entry in variants if entry[0] in args.case]
    partial = args.production_only or bool(args.case)
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    native_mesh = native_masonry(args.source_root, run)
    (run/'wall-write-bindings.json').write_text(json.dumps({'mpb_writes':wall_writes,'count':len(wall_writes),'contract':'synchronous native source restoration immediately before each actual wall MPB setter; enable primitives and material swaps additionally hooked'},indent=2)+'\n')
    fixture = ROOT/'tests/environment-budget-runtime'
    manifest = {'result':str(run/'results.txt'),'cases':[]}
    bound_sources = {source_path:source,shader_path:shader,repair_path:repair,floor_path:floor,wall_path:wall,clock_path:clock,trace_path:trace,occlusion_path:occlusion,dissolve_path:dissolve}
    (run/'source-hashes.json').write_text(json.dumps({'root':str(args.source_root.resolve()),'sha256':{str(path):hashlib.sha256(value.encode()).hexdigest() for path,value in bound_sources.items()},'coverage':'partial' if partial else 'production-and-negative-controls','cases':[name for name,_,_ in variants],'limits':['Native scene classes/config and empty wall attachment lists are explicit boundary surrogates.','Complete original wall Apply/EnsureTextures, shared ramp and wall-specific visual clock execute.', 'Original enable/disable subscription bodies and complete draw sampler execute against actual Camera.Render events; Time.frameCount alone is aliased to a deterministic fixture clock. Native scene-loaded bookkeeping is an explicit boundary.','Actual Unity meshes, renderer masks, pixels, cloning and camera callbacks are executed.','Native wall pixels execute GL surrogates of the LOW branch and exact HIGH/toggle-native clip equation derived from original DXBC; HIGH noise is a valid parameterized sample. Windows bytecode, artwork and lighting are not executed.','Native material healer integration is source-bound; its native callbacks are not executed.','Native game scenes, OpenXR HMD images and FPS remain hardware-open.']},indent=2)+'\n')
    (run/'repair-binding.json').write_text(json.dumps({'native-success-order':'sharedMaterials / enabled / MaterialReady / return true','removed-edge-negative-control':'rejected'},indent=2)+'\n')
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name, value, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        (production/'Environment.cs').write_text(value)
        (production/'WallFloorTile.cs').write_text(floor)
        (production/'WallDelivery.cs').write_text(delivery_variants.get(name,delivery))
        (production/'WallClock.cs').write_text(clock_variants.get(name,clock))
        (production/'WallDrawTrace.cs').write_text('using Time = GloomhavenVR.Core.WallFixtureClock;\n'+trace_variants.get(name,trace))
        (production/'OcclusionFade.cs').write_text(occlusion)
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
    shutil.copyfile(fixture/'NativeHighBranch.shader',unity_project/'Assets/NativeHighBranch.shader')
    # Independent imported shader metadata routes. Unity Shader.name writes do not
    # change the compiled shader name, so a renamed Object is not route coverage.
    native_shader = (fixture/'NativeMaterials.shader').read_text()
    for route in ('High','Low'):
        (unity_project/'Assets'/('NativeTrace'+route+'.shader')).write_text(
            native_shader.replace('Shader "Amp_Basic_N_MRAO"', 'Shader "WallTrace.WallFade.'+route+'"'))
    (unity_project/'Assets/ScenarioSimpleEnvironment.shader').write_text(shader)
    (unity_project/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (unity_project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = [str(args.unity),'-batchmode','-force-glcore','-projectPath',str(unity_project),'-executeMethod','EnvironmentRunner.Start','-environmentManifest',str(manifest_path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run','-a']+command
    try:
        environment = os.environ.copy(); environment["GHVR_ENVIRONMENT_NATIVE_MESH"] = str(native_mesh); environment["GHVR_ENVIRONMENT_EVIDENCE"] = str(run)
        result = subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240,env=environment)
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
