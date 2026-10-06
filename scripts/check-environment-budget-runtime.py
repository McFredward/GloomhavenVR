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
    boundary_path = args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentBudget.CameraBoundary.cs'
    boundary = boundary_path.read_text()
    ambient_path = args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentAmbientEffects.cs'
    ambient = ambient_path.read_text()
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
    clock_variants, delivery_variants, trace_variants, boundary_variants, ambient_variants = {}, {}, {}, {}, {}
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
            ('world-canonical-map-missing', 'return _worldCanonical?.Invoke(original) ?? original;', 'return original;', 'composed canonical material maps private world references to exact native original', 1),
            ('world-notification-recurses-owner', '_terrainBeforeWrite?.Invoke(renderer);\n            if (_failed) return;', '_worldBeforeWrite?.Invoke(renderer); _terrainBeforeWrite?.Invoke(renderer);\n            if (_failed) return;', 'world reference notification retains new bindings without recursively restoring its owner', 1),
            ('world-disposal-consumers-retained', '_driver?.WorldMaterialsDisposing();', '/* injected retained world material consumers */', 'world variant disposal synchronously releases current and queued material consumers', 1),

            ('inactive-host-acquires-render-lease', '!isActiveAndEnabled || !_active', '!_active', 'inactive environment host uses original camera pixels without private chunk leases', 1),
            ('material-read-toggle-stuck', 'bool share = PerfConfig.SharedEnvironmentMaterialReadsOn;', 'bool share = true;', 'shared material read option Off repeats every original per-surface validation', 1),
            ('private-bank-toggle-ignored', 'surface.ReadableMesh == null && _meshBankOn && ScenarioEnvironmentMeshBank.TryGetExact', 'surface.ReadableMesh == null && ScenarioEnvironmentMeshBank.TryGetExact', 'private mesh bank option Off restores unreadable original rendering immediately', 1),
            ('private-bank-never-used', 'surface.ReadableMesh == null && _meshBankOn && ScenarioEnvironmentMeshBank.TryGetExact', 'surface.ReadableMesh == null && bool.Parse("false") && ScenarioEnvironmentMeshBank.TryGetExact', 'verified unreadable native floor originals create a bounded private exact chunk', 1),
            ('instance-native-write-survives', 'instances.Dispose(); _instances.Remove(instances); _buildPending = true;', '/* injected: queued draw survives native write */ _buildPending = true;', 'late native pre-cull write revokes queued instance geometry before restoring originals', 1),
            ('supplementary-native-geometry-admitted', '!r.isPartOfStaticBatch && r.additionalVertexStreams == null', 'true', 'initial native supplementary geometry keeps exact original camera draws', 1),
            ('late-chunk-supplementary-geometry-ignored', '&& NativeGeometryCompatible(r) && r.forceRenderingOff', '&& r.forceRenderingOff', 'late native supplementary geometry revokes private submission before actual culling', 1),
            ('original-object-probe-lighting-combined', 'if (probes != null && probes.count != 0) return false;', '/* injected: actual populated light probes ignored */', 'native populated baked light probes refuse absence-aware chunks', 1),
            ('simplified-probe-contract-bypassed', 'if (probes != null && probes.count != 0) return false;', 'if (probes != null && probes.count != 0 && r.sharedMaterial.shader.name != SimpleShader) return false;', 'simplified shader keeps its material compromise without combining per-object probe draws', 1),
            ('absent-probe-flags-refused', 'if (r.lightProbeUsage != LightProbeUsage.BlendProbes || r.lightProbeProxyVolumeOverride != null) return false;', 'if (bool.Parse("true")) return false;', 'authored probe defaults with actual absent light and reflection probes create exact chunks', 1),
            ('native-reflection-registry-ignored', 'if (_driver == null || _driver.HasLocalReflectionProbes) return false;', '/* injected: moving live reflection volumes ignored */', 'active off-volume reflection consumers stay native before later movement or rebake', 1),
            ('late-probe-native-event-not-recovered', 'if (_renderDepth > 0) PerfMonitor.Count("Environment.LightingFallback");\n                RecoverRenderLeases();', '/* injected: native reflection add retains old camera source masks */', 'late native reflection appearance revokes the chunk before render and retains actual original lighting pixels', 1),
            ('late-baked-probe-final-boundary-missing', 'if (!_failed) _driver?.FinishCameraPreCull(camera);', '/* injected: raw native baked-probe installation survives all pre-cull callbacks */', 'late native baked-probe installation restores original source masks before culling', 1),
            ('late-native-consumer-final-check-missing', 'bool foreign = camera.commandBufferCount > 0 && HasNativeCommandBufferConsumers(camera);', 'bool foreign = false;', 'late foreign native DrawRenderer consumer restores original identity before actual camera culling', 1),
            ('late-native-lighting-flags-final-check-missing', 'foreach (Batch batch in _batches) if (batch.HasLateLightingWrite()) { changedLighting = true; break; }', '/* injected: late per-original lighting flags ignored */', 'late native custom or proxy lighting flags restore masked original sources before actual culling', 1),
            ('native-chunk-lightmap-early-check-missing', 'if (valid && HasNativeLightmap(r!)) { valid = false; LightingRefused = true; }', '/* injected: changed native lightmap index ignored at initial validation */', 'changed native chunk lightmap index restores originals at the initial camera validation', 1),
            ('native-chunk-lightmap-final-check-missing', 'r == null || HasNativeLightmap(r) || r.lightProbeUsage', 'r == null || r.lightProbeUsage', 'late native chunk lightmap index restores original sources before actual camera culling', 1),
            ('live-preparation-summary-missing', 'if (_reportPending && !_buildPending && !IsPreparingPresentation)', 'if (_reportPending && bool.Parse("false"))', 'live post-load settings rebuild publishes its bounded actual candidate/refusal summary', 1),
            ('preparation-report-unbounded', '_reports >= 32', 'bool.Parse("false")', 'preparation diagnostics are bounded for each scene despite repeated native placement', 1),
            ('option-off-resets-scene-report-bound', '_reportPending = false;\n            _meshVisits', '_reportPending = false; _reports = 0;\n            _meshVisits', 'complete option off/on toggles cannot reset the current scene preparation diagnostic bound', 1),
            ('live-native-refusal-sampling-missing', 'if (!VRLog.Wants(VRLogLevel.Debug) || renderer == null) return;', 'if (bool.Parse("true")) return;', 'live preparation exposes positive native refusal field terrain', 1),
            ('native-load-start-not-restored', 'ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);', '/* injected late native load-start hide */', 'native material-load start revokes queued geometry before its original hide', 1),
            ('native-load-start-not-installed', 'VRSession.Harmony?.PatchAll(typeof(MaterialLoaderData_Load_EnvironmentBudgetPatch));', '/* injected missing load-start hook */', 'production install registers native material-load start interruption', 1),
            ('native-load-start-terrain-lease-not-recovered', 'ScenarioEnvironmentBudget.BeforeNativeContentChange();\n        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);', '/* injected terrain lease survives */\n        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);', 'native material-load start dispatches the terrain lease recovery before hiding', 1),
            ('rejected-probes-decode-private-geometry', 'if (!ChunkLightingCompatible(renderer)) { _probeRefusals++; continue; }', 'if (!surface.Mesh.isReadable && _meshBankOn) ScenarioEnvironmentMeshBank.TryGetExact(surface.Mesh, out _);\n                if (!ChunkLightingCompatible(renderer)) { _probeRefusals++; continue; }', 'probe-rejected unreadable sources never enter bank hashing or decoding', 1),
            ('instance-post-command-not-detached', 'submission.Camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, submission.Buffer);', '/* injected: queued instance commands remain attached */', 'instance camera completion restores source masks and removes private commands', 2),
            ('instance-native-clone-prefix-missing', 'private static void Prefix() => ScenarioEnvironmentBudget.BeforeNativeContentChange();', 'private static void Prefix() { }', 'room content changes revoke interrupted queued commands before cloning', 2),
            ('foreign-ui-admitted','node.GetComponent<Canvas>() != null','false','foreign UI material stays untouched',2),
            ('foreign-actor-admitted','node.GetComponent<ActorBehaviour>() != null','false','foreign actor/UI/held/water/foliage/dissolve/native scope exclusions retain original rendering: CV_Floor_Base_Actor',2),
            ('ambient-loop-is-enough','AmbientIdentity(node, tile.transform)','true','combat effects never become ambience merely because they loop',1),
            ('ambient-never-paused','System.Pause(false);','/* injected: solver kept playing */','zero ambient budget pauses exact native families',1),
            ('ambient-static-mesh-scope-reused','ProceduralMapTile? tile = ScenarioEnvironmentAmbientEffects.Scope(node);','ProceduralMapTile? tile = TileScope(node, out _);','decorative doorway and prop smoke uses emitter scope independent of static mesh vetoes',1),
            ('ambient-static-walk-pruned','if (_effects >= 100 && (node.GetComponent<ProceduralProp>() != null','if ((node.GetComponent<ProceduralProp>() != null','decorative doorway and prop smoke uses emitter scope independent of static mesh vetoes',1),
            ('ambient-late-doorway-ready-missing','{ AdoptAmbient(renderer.transform); return; }','{ return; }','late material readiness adopts decorative smoke below native doorway mesh exclusions',1),
            ('ambient-native-stop-restarted','if (_paused && System != null && System.isPaused) System.Play(false);','if (_paused && System != null) System.Play(false);','ambient restoration respects a native stop while suppressed',1),
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
            ('structural-command-renderer-masked','if (camera != null && camera.commandBufferCount > 0 && foreignCommands)','if (bool.Parse("false"))','native command-buffer DrawRenderer keeps the original structural renderer identity and geometry',1),
            ('structural-effect-release-missing','InvalidateBatch(id);\n            if (_surfaces.TryGetValue(id, out Surface surface))','/* injected: substitute survives write */\n            if (_surfaces.TryGetValue(id, out Surface surface))','wall effect write restores structural sources and material synchronously',1),
            ('live-floor-dissolve-not-preserved','if (NativeWallFadeEnabled(material)) return false;','/* injected: native floor shader channel lost */','live native floor wall channels retain original shaders',1),
            ('late-native-channel-not-retired','RetireChangedNativeMaterials();','/* injected: late original wall channel ignored */','late native material gate retires the simpler variant before actual camera culling',1),
            ('shared-original-verdict-recomputed','if (!share || !_preCullMaterialVerdicts.TryGetValue(original, out materialCompatible))','_preCullMaterialVerdicts.Clear();\n                    if (!share || !_preCullMaterialVerdicts.TryGetValue(original, out materialCompatible))','one camera validates each shared original material once',1),
            ('shared-original-cross-camera-cache','_preCullMaterialVerdicts.Clear();','/* injected: stale material verdict survives cameras */','native keyword edit between camera invocations restores every sharing surface before culling',3),
            ('shared-original-renderer-veto-missing','!surface.Renderer.HasPropertyBlock() && (surface.Floor || surface.Structural)','surface.Floor || surface.Structural','shared original verdict never bypasses an individual native property-block veto',1),
            ('native-continuation-fault-guard-removed','try { _worldReady?.Invoke(renderer); _terrainReady?.Invoke(renderer); if (!_failed) _driver?.MaterialReady(renderer); }\n        catch (Exception error) { StopAfterFailure(error); }','_driver?.MaterialReady(renderer);','generated shader resolver fault',1),
            ('failed-environment-stops-terrain', '_terrainBeforeWrite?.Invoke(renderer); if (!_failed) _driver?.BeforeNativeRendererWrite(renderer);',
             'if (!_failed) { _terrainBeforeWrite?.Invoke(renderer); _driver?.BeforeNativeRendererWrite(renderer); }',
             'terrain native write and placement bridges survive an independent environment failure', 1),
        ]
        for name, before, after, expected, occurrences in changes:
            assert source.count(before) == occurrences, 'negative control binding drift: '+name
            variants.append((name,source.replace(before,after),expected))
        storage = 'private readonly Dictionary<Material, bool> _preCullMaterialVerdicts = new();'
        entry_clear = '_preCullMaterialVerdicts.Clear();\n            _dead.Clear();'
        assert source.count(storage) == 1 and source.count(entry_clear) == 1, 'material storage control binding drift'
        variants.append(('shared-original-storage-reallocated',
            source.replace(storage, storage.replace('readonly ', ''))
                .replace(entry_clear, '_preCullMaterialVerdicts = new Dictionary<Material, bool>();\n            _dead.Clear();'),
            'warmed material validation reuses its bounded dictionary storage'))
        before = 'if (surface.ReadableMesh == null) { _unreadable++; continue; }'
        guard = 's.ReadableMesh == null || !s.ReadableMesh.isReadable || '
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
    if not args.production_only:
        before = 'if (ReferenceEquals(_installedOwner, owner)) return;'
        assert boundary.count(before) == 1, 'Cold native camera install lifecycle binding drift'
        name = 'same-domain-camera-owner-never-reinstalled'
        variants.append((name,source,'new plugin owner installs the shared native camera boundary exactly once'))
        boundary_variants[name] = boundary.replace(before,'if (_installedOwner != null) return;')
    bank_variants = {}
    if not args.production_only:
        bank_path = args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshBank.cs'
        bank_source = bank_path.read_text()
        bank_changes = [
            ('bank-source-hash-not-checked', 'Hex(sha.ComputeHash(input)) == source.sha256', 'true', 'wrong actual source bundle hash keeps native rendering'),
            ('bank-asset-hash-not-checked', ' || Digest(bytes) != variant.sha256', '', 'corrupt prepared geometry hash keeps native rendering'),
            ('bank-native-bounds-not-checked', 'if (bounds.center[i] != sig.bounds[i] || bounds.extents[i] != sig.bounds[i + 3]) return false;', 'if (bool.Parse("false")) return false;', 'stale original geometry bounds reject a substitute immediately'),
        ]
        for name,before,after,expected in bank_changes:
            assert bank_source.count(before) == 1, 'Bank causal binding drift: '+name
            variants.append((name,source,expected)); bank_variants[name] = bank_source.replace(before,after)
    if not args.production_only:
        for name, before, after, expected in (
            ('ambient-numbered-family-missed','name = name.Substring(0, open);','return name;','authored numbered torch family is paused at zero environment budget'),
            ('ambient-pooled-parent-veto-missing','if (Family(name)) family = true;','if (Family(name)) return true;','pooled combat parent vetoes even an exact decorative torch leaf'),
            ('ambient-collision-paused','!system.collision.enabled','true','decorative collision callbacks keep native particle simulation under zero FX'),
            ('ambient-trigger-paused','!system.trigger.enabled','true','decorative trigger callbacks keep native particle simulation under zero FX'),
            ('ambient-stop-callback-paused','system.main.stopAction == ParticleSystemStopAction.None','true','decorative stop callbacks keep native particle simulation under zero FX'),
        ):
            assert ambient.count(before) == 1, 'Ambient causal binding drift: '+name
            variants.append((name,source,expected)); ambient_variants[name] = ambient.replace(before,after)
    if not args.production_only:
        variants.append(('native-camera-callback-failure-swallowed',source,
            'actual native camera callback assertions propagate after Render into the runner process status'))
    if args.case:
        unknown = set(args.case)-{name for name,_,_ in variants}
        if unknown: raise SystemExit('Unknown selected case: '+', '.join(sorted(unknown)))
        variants = [entry for entry in variants if entry[0] in args.case]
    partial = args.production_only or bool(args.case)
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    native_mesh = native_masonry(args.source_root, run)
    (run/'wall-write-bindings.json').write_text(json.dumps({'mpb_writes':wall_writes,'count':len(wall_writes),'contract':'synchronous native source restoration immediately before each actual wall MPB setter; enable primitives and material swaps additionally hooked'},indent=2)+'\n')
    # Snapshot the fixture too: a parallel edit must not bind later variants to
    # different test bodies after their production source has already been frozen.
    fixture = run/'fixture'
    shutil.copytree(ROOT/'tests/environment-budget-runtime', fixture)
    fixture_hashes = {str(path.relative_to(fixture)): hashlib.sha256(path.read_bytes()).hexdigest()
                      for path in fixture.rglob('*') if path.is_file()}
    swallowed_fixture = None
    if any(name == 'native-camera-callback-failure-swallowed' for name,_,_ in variants):
        guard = 'if (callbackFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackFailure).Throw();'
        program = (fixture/'Program.cs').read_text()
        assert program.count(guard) == 1, 'Native camera callback process-status binding drift'
        # This fixture-only control tests the evidence bridge itself. The complete
        # production source stays unchanged; Unity must not hide failed assertions.
        swallowed_fixture = run/'fixture-callback-swallowed'
        shutil.copytree(fixture,swallowed_fixture)
        (swallowed_fixture/'Program.cs').write_text(program.replace(guard,
            'if (callbackFailure != null && bool.Parse("false")) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackFailure).Throw();'))
    manifest = {'result':str(run/'results.txt'),'cases':[]}
    bound_sources = {source_path:source,boundary_path:boundary,ambient_path:ambient,shader_path:shader,repair_path:repair,floor_path:floor,wall_path:wall,clock_path:clock,trace_path:trace,occlusion_path:occlusion,dissolve_path:dissolve}
    for bank_file in ('ScenarioEnvironmentMeshBank.cs','ScenarioEnvironmentMeshStream.cs'):
        bank_path = args.source_root/'src/GloomhavenVR/Core/Perf'/bank_file
        bound_sources[bank_path] = bank_path.read_text()
    (run/'source-hashes.json').write_text(json.dumps({'root':str(args.source_root.resolve()),'sha256':{str(path):hashlib.sha256(value.encode()).hexdigest() for path,value in bound_sources.items()},'fixture_sha256':fixture_hashes,'coverage':'partial' if partial else 'production-and-negative-controls','cases':[name for name,_,_ in variants],'limits':['Native scene classes/config and empty wall attachment lists are explicit boundary surrogates.','Complete original wall Apply/EnsureTextures, shared ramp and wall-specific visual clock execute.', 'Original enable/disable subscription bodies and complete draw sampler execute against actual Camera.Render events; Time.frameCount alone is aliased to a deterministic fixture clock. Native scene-loaded bookkeeping is an explicit boundary.','Actual Unity meshes, renderer masks, pixels, cloning and camera callbacks are executed.','Native wall pixels execute GL surrogates of the LOW branch and exact HIGH/toggle-native clip equation derived from original DXBC; HIGH noise is a valid parameterized sample. Windows bytecode, artwork and lighting are not executed.','Native material healer integration is source-bound; its native callbacks are not executed.','Native game scenes, OpenXR HMD images and FPS remain hardware-open.']},indent=2)+'\n')
    (run/'repair-binding.json').write_text(json.dumps({'native-success-order':'sharedMaterials / enabled / MaterialReady / return true','removed-edge-negative-control':'rejected'},indent=2)+'\n')
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name, value, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        read_entry = 'private static bool CompatibleMaterial(Material material, bool floor)\n    {'
        assert value.count(read_entry) == 1, 'complete original material-read counter binding drift'
        for bank_file in ('ScenarioEnvironmentMeshBank.cs','ScenarioEnvironmentMeshStream.cs'):
            bank_text = bank_variants.get(name, bound_sources[args.source_root/'src/GloomhavenVR/Core/Perf'/bank_file]) if bank_file == 'ScenarioEnvironmentMeshBank.cs' else bound_sources[args.source_root/'src/GloomhavenVR/Core/Perf'/bank_file]
            if bank_file == 'ScenarioEnvironmentMeshBank.cs':
                lookup = '''        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            if (bundle != null && bundle.Contains(path)) return bundle.LoadAsset<TextAsset>(path);
        return null;'''
                assert bank_text.count(lookup) == 1, 'Only actual platform asset lookup is the explicit editor fixture boundary'
                bank_text = 'using Application = GloomhavenVR.Core.BankFixturePaths;\n'+bank_text.replace(lookup, '        return BankFixtureAssets.Resolve(path);')
                bank_entry = 'internal static bool TryGetExact(Mesh native, out Mesh mesh) => TryGet(native, 100, out mesh);'
                assert bank_text.count(bank_entry) == 1, 'Complete original bank read-request counter binding drift'
                bank_text = bank_text.replace(bank_entry, 'internal static bool TryGetExact(Mesh native, out Mesh mesh) { global::EnvironmentProgram.RecordBankRead(); return TryGet(native, 100, out mesh); }')
            (production/bank_file).write_text(bank_text)
        (production/'Environment.cs').write_text(value.replace(read_entry,
            read_entry + '\n        global::EnvironmentProgram.RecordMaterialRead();'))
        (production/'CameraBoundary.cs').write_text(boundary_variants.get(name,boundary))
        (production/'AmbientEffects.cs').write_text(ambient_variants.get(name,ambient))
        (production/'WallFloorTile.cs').write_text(floor)
        (production/'WallDelivery.cs').write_text(delivery_variants.get(name,delivery))
        (production/'WallClock.cs').write_text(clock_variants.get(name,clock))
        (production/'WallDrawTrace.cs').write_text('using Time = GloomhavenVR.Core.WallFixtureClock;\n'+trace_variants.get(name,trace))
        (production/'OcclusionFade.cs').write_text(occlusion)
        project = build/'Environment.csproj'; shutil.copyfile(fixture/'Environment.csproj',project)
        assembly = 'EnvironmentBudget_'+name.replace('-','_')
        case_fixture = swallowed_fixture if name == 'native-camera-callback-failure-swallowed' else fixture
        command = [dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(case_fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')]
        result = subprocess.run(command,capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+'\nA compilation failure cannot pass as a negative control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path = run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    unity_project = run/'unity'; (unity_project/'Assets/Editor').mkdir(parents=True); (unity_project/'Packages').mkdir(); (unity_project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/EnvironmentRunner.cs',unity_project/'Assets/Editor/EnvironmentRunner.cs')
    shutil.copyfile(fixture/'NativeMaterials.shader',unity_project/'Assets/NativeMaterials.shader')
    shutil.copyfile(fixture/'NativeHighBranch.shader',unity_project/'Assets/NativeHighBranch.shader')
    # Exact versions match the production BepInEx.Core -> HarmonyX dependency
    # graph. Private test copies never overwrite references or the editor install.
    native_dependencies = (
        'harmonyx/2.7.0/lib/net45/0Harmony.dll',
        'monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll',
        'monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll',
        'mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll',
    )
    plugins = unity_project/'Assets/Plugins'; plugins.mkdir()
    native_provenance = []
    for relative in native_dependencies:
        dependency = Path.home()/'.nuget/packages'/relative
        if not dependency.is_file(): raise SystemExit('Pinned production HarmonyX dependency absent: '+str(dependency))
        shutil.copyfile(dependency,plugins/dependency.name)
        native_provenance.append({'path':str(dependency),'sha256':hashlib.sha256(dependency.read_bytes()).hexdigest()})
    (run/'native-harmony-provenance.json').write_text(json.dumps(native_provenance,indent=2)+'\n')
    # Independent imported shader metadata routes. Unity Shader.name writes do not
    # change the compiled shader name, so a renamed Object is not route coverage.
    native_shader = (fixture/'NativeMaterials.shader').read_text()
    for route in ('High','Low'):
        (unity_project/'Assets'/('NativeTrace'+route+'.shader')).write_text(
            native_shader.replace('Shader "Amp_Basic_N_MRAO"', 'Shader "WallTrace.WallFade.'+route+'"'))
    (unity_project/'Assets/ScenarioSimpleEnvironment.shader').write_text(shader)
    bank_root = args.source_root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    if not (bank_root/'index.json').is_file(): raise SystemExit('Prepared environment bank assets missing')
    bank_index = json.loads((bank_root/'index.json').read_text())
    chosen = [entry for entry in bank_index['entries'] if entry['signature']['name'] in ('EN_CR_Pillar_Thin','CV_Floor_Basic_01','CV_Wall_Generic_01')]
    assert any(entry['signature']['name'] == 'EN_CR_Pillar_Thin' for entry in chosen), 'Actual native masonry fixture bank absent'
    bank_fixture = unity_project/'Assets/Bundle/EnvironmentMeshes'; bank_fixture.mkdir(parents=True)
    (bank_fixture/'index.json').write_text(json.dumps({'format':1,'entries':chosen})+'\n')
    for entry in chosen:
        for variant in entry['variants']: shutil.copyfile(bank_root/variant['file'],bank_fixture/variant['file'])
    (run/'bank-fixture-provenance.json').write_text(json.dumps({'entries':chosen,'application_path_boundary':'original read-only game StreamingAssets; actual SHA256 source file proof runs','geometry_asset_lookup':'real imported Unity TextAssets; standalone loader is an explicit boundary. Fresh compressed fixture-bank workflow failed; the integrator independently proved the actual production Windows bank loads in the exact Linux editor.'},indent=2)+'\n')
    (unity_project/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (unity_project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = [str(args.unity),'-batchmode','-force-glcore','-projectPath',str(unity_project),'-executeMethod','EnvironmentRunner.Start','-environmentManifest',str(manifest_path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run','-a']+command
    try:
        environment = os.environ.copy(); environment["GHVR_ENVIRONMENT_NATIVE_MESH"] = str(native_mesh); environment["GHVR_ENVIRONMENT_EVIDENCE"] = str(run); environment["GHVR_ENVIRONMENT_STREAMING_ASSETS"] = str(args.source_root/"ressources/GH_Data/StreamingAssets"); environment["GHVR_ENVIRONMENT_BANK_FIXTURE"] = str(bank_fixture)
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
