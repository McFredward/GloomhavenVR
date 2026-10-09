#!/usr/bin/env python3
"""Execute owned world slots and final native culling in Unity2021/HarmonyX.

Native scene/controller, shader-resolver and config APIs are explicit boundaries.
Actual Unity renderers, material copying, MPBs, cloning, Camera.Render and the
unchanged production FireOnPreCull Harmony postfix execute. The shader lane
separately proves its production fragment against original native bytecode.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]


def pure_method(source,signature):
    start=source.index(signature);body=source.index('{',start);depth=1;end=body+1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]


def wall_attachment_source(root):
    """Read complete current production attachment primitives; no runtime dependency."""
    wall_dir=root/'src/GloomhavenVR/Core/WallFade'
    wall_paths=[wall_dir/name for name in ('WallSegmentFade.cs','WallSegmentFade.Mounted.cs',
        'WallSegmentFade.Dissolve.cs','WallSegmentFade.ReadFacts.cs','WallSegmentFade.PropUnit.cs',
        'WallSegmentFade.Floor.cs','WallSegmentFade.Standing.cs','WallSegmentFade.FreeStanding.cs',
        'WallSegmentFade.SelectionFacts.cs','WallSegmentFade.HoldQuery.cs')]
    wall_text={p.name:p.read_text() for p in wall_paths}
    attachment_methods=[]
    for filename,signatures in (
        ('WallSegmentFade.cs',('private bool EnsureTextures()', 'private static bool HasLiveWallFadeToggle(Material m)',
            'private bool RendererIsWallFadeCapable(MeshRenderer r)', 'private bool RendererUsesWallFade(MeshRenderer r)')),
        ('WallSegmentFade.Mounted.cs',('private static MountedProp ClassifyProp(Renderer r)',
            'private static ParticleSystem.MinMaxGradient ScaledStartColor(MountedProp p, float alpha)',
            'private void DriveProp(MountedProp p, float fade)')),
        ('WallSegmentFade.Dissolve.cs',('private void CaptureMasonryTemplate(Material m)',
            'private void EnsureDissolveChannel(MountedProp p)', 'private Material BuildSwapMaterial(Material source)',
            'private void DriveNativeProp(MountedProp p, float fade)',
            'private static void RestorePropSwap(MountedProp p, Renderer? r)',
            'private static bool MaterialOffersOwnChannel(Material? mat)',
            'private int PredictClassOfRenderer(Renderer? r)')),
        ('WallSegmentFade.ReadFacts.cs',('private static void ReadFadeMaterials(Renderer renderer, List<Material> destination)',)),
        ('WallSegmentFade.HoldQuery.cs',('internal void HideByEnable(Renderer r)', 'internal bool ShowIfWeHid(Renderer r)')),
    ):
        attachment_methods.extend(pure_method(wall_text[filename],sig) for sig in signatures)
    read_source=wall_text['WallSegmentFade.ReadFacts.cs']
    source_signature='private static Material FadeSourceMaterial(Material material) =>'
    start=read_source.index(source_signature);end=read_source.index(';',start)+1
    attachment_methods.append(read_source[start:end])
    for filename,signature in (('WallSegmentFade.cs','private const float FoliageCutoffEnd'),
                               ('WallSegmentFade.Mounted.cs','private const float MountedParticleShrink')):
        text=wall_text[filename];start=text.index(signature);end=text.index(';',start)+1
        attachment_methods.append(text[start:end])
    return wall_paths, 'using System; using System.Collections.Generic; using UnityEngine; namespace GloomhavenVR.Core { internal static partial class WallSegmentFade { private sealed partial class FadeDriver {\n'+'\n'.join(attachment_methods)+'\n} } }\n'


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/world-material-runtime')
    parser.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--production-only',action='store_true')
    parser.add_argument('--integration-root',type=Path,help='Read-only integrated source bindings; defaults to source-root')
    parser.add_argument('--case',action='append')
    args=parser.parse_args();root=args.source_root.resolve();fixture=ROOT/'tests/world-material-runtime'
    paths=sorted((root/'src/GloomhavenVR/Core/Perf').glob('WorldMaterialBudget*.cs'))
    paths.append(root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentBudget.CameraBoundary.cs')
    sources={p.name:p.read_text() for p in paths}
    prop_path=root/'src/GloomhavenVR/Board/FigureGrab/PropGrab.cs'
    prop_source=prop_path.read_text()
    prop_methods=[pure_method(prop_source,signature) for signature in (
        'internal static bool OwnsRendererOf(Transform? t)',
        'internal static void CopyVisualRoots(List<GameObject> destination)')]
    sources['PropGrab.VisualRoots.cs']='using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nnamespace GloomhavenVR.Board.FigureGrab { internal static partial class PropGrab {\n'+'\n'.join(prop_methods)+'\n} }\n'
    wall_paths,attachment_source=wall_attachment_source(root)
    sources['WallAttachmentDelivery.cs']=attachment_source
    combined='\n'.join(sources.values())
    assert 'StaticBatchingUtility' not in combined and 'SetStaticBatchInfo' not in combined
    world_combined='\n'.join(value for name,value in sources.items() if name!='WallAttachmentDelivery.cs')
    assert 'new Mesh' not in world_combined and 'SetPropertyBlock(' not in world_combined and 'forceRenderingOff =' not in world_combined
    assert 'Camera.onPreCull +=' not in combined,'commit must use the final native boundary'
    # Exact component owners may be stored once; their mutable layer stays live.
    current_layer='surface.Object.layer' if 'surface.Object.layer' in sources['WorldMaterialBudget.cs'] else 'renderer.gameObject.layer'
    video_guard=('if (!rendererBlockEmpty && _block.HasTexture(MainTextureId) && _block.GetTexture(MainTextureId) is RenderTexture\n                || !slotBlockEmpty && _slotBlock.HasTexture(MainTextureId) && _slotBlock.GetTexture(MainTextureId) is RenderTexture) return true;'
        if 'bool rendererBlockEmpty' in sources['WorldMaterialBudget.Materials.cs'] else
        'if (_block.HasTexture(MainTextureId) && _block.GetTexture(MainTextureId) is RenderTexture\n                || _slotBlock.HasTexture(MainTextureId) && _slotBlock.GetTexture(MainTextureId) is RenderTexture) return true;')
    changes=[
        ('camera-ui-read-pass-bypassed',[('WorldMaterialBudget.cs','using IDisposable timing = PerfMonitor.Scope("WorldMaterial.PreCull");','if (camera != null && camera.name == "UI capture" && camera.commandBufferCount == 0) return;\n                using IDisposable timing = PerfMonitor.Scope("WorldMaterial.PreCull");',1)],'every UI capture retains its actual current native final callback and read-pass lifecycle'),
        ('camera-exclusion-omitted',[('WorldMaterialBudget.cs','if (camera != null && camera.commandBufferCount == 0','if (false && camera != null && camera.commandBufferCount == 0',1)],'two unreachable UI captures perform zero world material mesh scope or prop traversal'),
        ('camera-current-mask-ignored',[('WorldMaterialBudget.cs','(camera.cullingMask & (1 << '+current_layer+'))','((camera.name == "UI capture" ? (1 << 31) : camera.cullingMask) & (1 << '+current_layer+'))',1)],'current native camera mask change immediately validates newly reachable world sources'),
        ('camera-current-source-layer-ignored',[('WorldMaterialBudget.cs','(camera.cullingMask & (1 << '+current_layer+'))','(camera.cullingMask & (1 << (camera.name == "UI capture" ? 0 : '+current_layer+')))',1)],'late native source re-layer into current UI mask immediately retains its native effect'),
        ('camera-native-commands-ignored',[('WorldMaterialBudget.cs','camera.commandBufferCount == 0','true',1)],'late native DrawRenderer beyond UI mask retains complete current world effect validation and pixels'),
        ('camera-current-consumer-ignored',[('WorldMaterialBudget.cs','&& !NeedsSubstituteRevocation())','&& true)',1)],'unreachable original with current differently layered consumer retains same-camera native revocation'),
        ('foreign-hide-ownership-adopted',[('WallAttachmentDelivery.cs','r.enabled = false;\n                _hidByEnable.Add(r);\n            }','r.enabled = false;\n            }\n            _hidByEnable.Add(r);',1)],'foreign disabled renderer without owned hide still drives its native endpoint'),
        ('hidden-attachment-skip-omitted',[('WallAttachmentDelivery.cs','if (fade == 1f','if (false && fade == 1f',1)],'fully hidden owned endpoint avoids all repeated native effect writes'),
        ('hidden-attachment-ledger-ignored',[('WallAttachmentDelivery.cs','&& _hidByEnable.Contains(r) && !r.enabled','&& !r.enabled',1)],'foreign disabled renderer without owned hide still drives its native endpoint'),
        ('hidden-attachment-ramp-skipped',[('WallAttachmentDelivery.cs','if (fade == 1f','if (fade > 0f',1)],'intermediate hidden return continues native opacity delivery'),
        ('hidden-attachment-native-reenable-ignored',[('WallAttachmentDelivery.cs','&& _hidByEnable.Contains(r) && !r.enabled','&& _hidByEnable.Contains(r)',1)],'actual native reenable immediately restores current full native fade drive'),
        ('hidden-enable-release-unconditional',[('WallAttachmentDelivery.cs','if (r.enabled || ScenarioTerrainBudget.HasCurrentRenderLease(r)','if (true || ScenarioTerrainBudget.HasCurrentRenderLease(r)',1)],'repeated enabled-hide endpoint has no native write release'),
        ('attachment-canonical-source-missing',[('WallAttachmentDelivery.cs','ScenarioEnvironmentBudget.CanonicalMaterial(material);','material;',1)],'optimized native wall admission resolves original shader name and live gate'),
        ('attachment-native-priority-missing',[('WallAttachmentDelivery.cs','if (p.System == null && r is MeshRenderer)','if (p.System == null && r is MeshRenderer && bool.Parse("false"))',1)],'optimized attachment retains original native dissolve classification'),
        ('attachment-swap-original-not-canonical',[('WallAttachmentDelivery.cs','src[i] = m = FadeSourceMaterial(m);','m = FadeSourceMaterial(m);',1)],'mixed attachment restore snapshot retains real native slots'),
        ('attachment-native-write-release-missing',[('WallAttachmentDelivery.cs','ScenarioEnvironmentBudget.BeforeNativeRendererWrite(p.Renderer);','/* injected surviving native-write lease */',1)],'attachment native write releases original renderer binding before its effect'),
        ('mode-ignored',[('WorldMaterialBudget.Materials.cs','variant.SetFloat("_GHVRWorldMaterialMode", _mode);','variant.SetFloat("_GHVRWorldMaterialMode", 1);',1)],'actual private material binds independently requested mode'),
        ('native-material-shader-mutated',[('WorldMaterialBudget.Materials.cs','variant.shader = _shader;','original.shader = _shader;',2)],'private world shader never mutates original native material'),
        ('native-copy-omitted',[('WorldMaterialBudget.Materials.cs','variant.CopyPropertiesFromMaterial(original);','/* injected stale material properties */',1)],'between-eye native in-place material edits'),
        ('material-pass-cross-eye-stale',[('WorldMaterialBudget.cs','_prepared.Clear();','/* injected cross-eye reuse */',6)],'between-eye native in-place material edits'),
        ('scope-cross-eye-stale',[('WorldMaterialBudget.cs','_scopes.Clear();','/* injected cross-eye scope verdict */',3),('WorldMaterialBudget.Scope.cs','_scopes.Clear();','/* injected cross-eye scope verdict */',1)],'current added native interaction between eyes'),
        ('foreign-slot-overwritten',[('WorldMaterialBudget.cs','original = Canonical(current);','original = Source(_slots[0]);',1)],'conditional restoration preserves same-count foreign slot replacement'),
        ('native-clone-boundary-missing',[('WorldMaterialBudget.cs','internal static void BeforeNativeContentChange() => _driver?.RestoreBindings();','internal static void BeforeNativeContentChange() { }',1)],'native content boundary restores original slot composition'),
        ('proxy-only-refusal-not-notified',[('WorldMaterialBudget.cs','if (restored || geometryChanged || !surface.Refused || NeedsSubstituteRevocation()) _changedSources.Add(renderer);','if (restored) _changedSources.Add(renderer);',1)],'late native scope refusal revokes an earlier proxy-only variant'),
        ('final-boundary-lost',[
            ('WorldMaterialBudget.cs','ScenarioCameraCullBoundary.Subscribe(_driver.PreCull);','Camera.onPreCull += _driver.PreCull;',1),
            ('WorldMaterialBudget.cs','ScenarioCameraCullBoundary.Unsubscribe(_driver.PreCull);','Camera.onPreCull -= _driver.PreCull;',1),
            ('WorldMaterialBudget.cs','ScenarioCameraCullBoundary.Unsubscribe(PreCull);','Camera.onPreCull -= PreCull;',1)],'final native boundary observes same-camera late prop registration'),
        ('vertex-effect-veto-missing',[('WorldMaterialBudget.Materials.cs','{ "_AddVertexAnim",','{ "_UnusedVertexFlag",',1)],'live original effect retains native material family: _AddVertexAnim'),
        ('consumer-disposal-not-called',[('WorldMaterialBudget.cs','_beforeVariantDisposal?.Invoke();','/* injected dangling consumer */',1)],'stage zero releases factory-only render consumers'),
        ('asset-preparation-not-called',[('WorldMaterialBudget.Materials.cs','if (_ensureAssets?.Invoke() == false)','if (bool.Parse("false"))',1)],'cold asset preparation refusal keeps native source materials'),
        ('native-render-tag-dropped',[('WorldMaterialBudget.Materials.cs','variant.SetOverrideTag("RenderType", original.GetTag("RenderType", false, ""));','/* injected replacement render route loss */',1)],'native replacement-camera render type and queue'),
        ('native-tile-required-generator-vetoed',[('WorldMaterialBudget.Scope.cs','&& component is not ProceduralStyle','&& component is not UnknownNativeAnimation',1)],'requested world stage produces a private variant'),
        ('animated-style-veto-lost',[('WorldMaterialBudget.Scope.cs','if (component is ProceduralStyle style && style.AnimateStyle) allowed = false;','/* injected animated native style admission */',1)],'live native animated style retains original rendering'),
        ('held-source-veto-lost',[('WorldMaterialBudget.Scope.cs','scope.Prop |= _shareReads && _propRoots.Contains(node);','scope.Prop |= false;',1)],'current held native world prop retains original material ownership'),
        ('current-meshfilter-not-read',[('WorldMaterialBudget.cs','MeshFilter filter = renderer.GetComponent<MeshFilter>();\n                    Mesh mesh','MeshFilter filter = surface.Filter!;\n                    Mesh mesh',1)],'replacement native mesh filter is read again and can regain safe world shading'),
        ('native-mesh-reference-ignored',[('WorldMaterialBudget.cs',' || surface.Mesh != mesh','',1)],'live native mesh-reference swap notifies earlier consumers'),
        ('empty-slots-not-refused',[('WorldMaterialBudget.cs','refused = _slots.Count == 0;','refused = false;',1)],'empty native material slots revoke an earlier factory-only draw'),
        ('joint-program-contract-unrestricted',[('WorldMaterialBudget.Materials.cs','return ProvenProgram(metadata.Keywords(original), route) ? route : -1;','return route;',1)],'unproven native worldspace alpha program combination retains original shader'),
        ('standard-mpb-render-mode-unchecked',[('WorldMaterialBudget.Materials.cs','if (route == 9)\n                foreach','if (route == 10)\n                foreach',1)],'native MPB blend mode override remains original'),
        ('native-map-provenance-missing',[('WorldMaterialBudget.cs','if (map.worldMap != null) _worldRoots.Add(map.worldMap.transform);','/* injected missing native map scope */',1)],'actual native MapChoreographer worldMap field establishes positive map decoration scope'),
        ('disabled-native-pass-admitted',[('WorldMaterialBudget.Materials.cs',' || !NativePassesEnabled(original)','',1)],'live disabled native material pass remains original: FORWARD'),
        ('disabled-native-fallback-caster-admitted',[('WorldMaterialBudget.Materials.cs','return original.GetShaderPassEnabled("ShadowCaster") && original.GetShaderPassEnabled("CUSTOM_SHADOW_PASS");','return original.GetShaderPassEnabled("CUSTOM_SHADOW_PASS");',1)],'live disabled native material pass remains original: ShadowCaster'),
        ('disabled-native-low-caster-admitted',[('WorldMaterialBudget.Materials.cs','return original.GetShaderPassEnabled("ShadowCaster") && original.GetShaderPassEnabled("CUSTOM_SHADOW_PASS");','return original.GetShaderPassEnabled("ShadowCaster");',1)],'live disabled native material pass remains original: CUSTOM_SHADOW_PASS'),
        ('video-mpb-admitted',[('WorldMaterialBudget.Materials.cs',video_guard,'/* injected animated texture admission */',1)],'current per-slot native video/render texture remains original'),
        ('off-read-pass-allocates',[('WorldMaterialBudget.cs','if (!Requested && _originalByVariant.Count == 0) return EmptyPass.Instance;','/* injected allocating Off pass */',1)],'settled Off reuses a no-op read pass'),
        ('off-renderer-material-read',[('WorldMaterialBudget.cs','if (_originalByVariant.Count == 0) return false;','/* injected unused native Off slot read */',1)],'settled Off performs no native renderer slot reads'),
        ('off-scene-inventory',[('WorldMaterialBudget.cs','private void SceneLoaded(Scene scene, LoadSceneMode mode) { if (Requested) Seed(); }','private void SceneLoaded(Scene scene, LoadSceneMode mode) { if (VRSession.IsRunning) Seed(); }',1)],'settled Off additive scene loading performs no scene material inventory'),
        ('retained-scene-not-reseeded',[('WorldMaterialBudget.cs','if (Requested) Seed();\n        }\n        internal IDisposable BeginPass()', '/* injected missing retained scene rediscovery */\n        }\n        internal IDisposable BeginPass()',1)],'unrelated additive unload reseeds retained scenery'),
        ('native-map-coordinator-vetoed',[('WorldMaterialBudget.Scope.cs','type == typeof(ApparanceMap)','false',1)],'exact original ProcGen root and native map coordinator tuple'),
        ('native-map-config-vetoed',[('WorldMaterialBudget.Scope.cs','type == typeof(ProceduralMapConfig)','false',1)],'exact original ProcGen root and native map coordinator tuple'),
        ('native-placement-coordinator-vetoed',[('WorldMaterialBudget.Scope.cs','type == typeof(ProceduralPlacementNotifierHandler)','false',1)],'exact original ProcGen root and native map coordinator tuple'),
        ('native-shadow-coordinator-vetoed',[('WorldMaterialBudget.Scope.cs','type == typeof(LightShadowsModifierController)','false',1)],'exact original ProcGen root and native map coordinator tuple'),
        ('native-coordinator-subclass-admitted',[('WorldMaterialBudget.Scope.cs','type == typeof(ApparanceMap)','component is ApparanceMap',1)],'unreviewed subclass never inherits permission'),
        ('ambient-weight-ignored',[('WorldMaterialBudget.cs','_passAmbient = _shareReads ? _ambientWeight?.Invoke() ?? 1f : 1f;','_passAmbient = 1f;',2)],'actual private material binds independently requested ambient weight'),
        ('renderer-mpb-read-per-slot',[('WorldMaterialBudget.Materials.cs','renderer.GetPropertyBlock(_slotBlock, slot);','renderer.GetPropertyBlock(_block);\n            renderer.GetPropertyBlock(_slotBlock, slot);',1)],'settled 64-source two-slot MPBs read renderer-wide blocks once per source'),
        ('registered-root-enumeration-lost',[('WorldMaterialBudget.Scope.cs','PropGrab.CopyVisualRoots(_propVisuals);','/* injected omitted registered props */',1)],'registered unheld prop subtree remains native'),
        ('remote-root-enumeration-lost',[('WorldMaterialBudget.Scope.cs','NetHeldProps.CopyVisualRoots(_propVisuals);','/* injected omitted remote held props */',1)],'remote held original visual remains native'),
        ('prop-roots-cross-eye-stale',[('WorldMaterialBudget.Scope.cs','_propRoots.Clear(); _propVisuals.Clear(); _propRootsReady = false;','/* injected stale exact prop roots */',1)],'current held native world prop retains original material ownership'),
        ('registry-work-repeated',[('WorldMaterialBudget.Scope.cs','if (_propRootsReady) return;','if (_propRootsReady && bool.Parse("false")) return;',1)],'64-source pass enumerates 128 exact grabbable roots once'),
        ('ambient-read-repeated',[('WorldMaterialBudget.Materials.cs','_shareReads ? _passAmbient : _ambientWeight?.Invoke() ?? 1f','_ambientWeight?.Invoke() ?? 1f',1)],'one synchronous pass reads current ambient config once'),
        ('native-writer-read-invalidation-lost',[('WorldMaterialBudget.cs','InvalidateScopeReads(); _prepared.Clear(); _metadata.Clear(); RestoreRenderer(renderer);','InvalidateScopeReads(); RestoreRenderer(renderer);',1)],'native writer boundary invalidates prepared material reads within an outer pass'),
        ('nested-prop-reads-stale',[('WorldMaterialBudget.cs','InvalidateScopeReads();\n            return new ReadPass(this);','if (_passDepth == 1) InvalidateScopeReads();\n            return new ReadPass(this);',1)],'nested actual camera boundary refreshes current local held roots'),
        ('nested-native-material-reads-stale',[('WorldMaterialBudget.cs','if (_passDepth > 1)','if (_passDepth > 1 && bool.Parse("false"))',1)],'nested actual final camera boundary reads current native shader keywords tint and ambient'),
        ('renewed-refusal-not-notified',[
            ('WorldMaterialBudget.cs',' || NeedsSubstituteRevocation()) _changedSources.Add(renderer);',' || NeedsSubstituteRevocation() && bool.Parse("false")) _changedSources.Add(renderer);',2),
            ('WorldMaterialBudget.cs','refused && (!surface.Refused || NeedsSubstituteRevocation())','refused && (!surface.Refused || NeedsSubstituteRevocation() && bool.Parse("false"))',1)],'successive actual camera culls revoke each renewed scope substitute, eye=1'),
        ('prepared-membership-used-for-revocation',[('WorldMaterialBudget.cs','needsRevocation = _substituteRevocation(renderer);','needsRevocation = HasSubstitute();',1)],'unchanged prepared-only refused source does not revoke or re-adopt'),
    ]
    if '_performanceWallHidden?.Invoke(renderer)' in sources['WorldMaterialBudget.cs']:
        changes.extend([
            ('performance-hidden-read-guard-lost',[('WorldMaterialBudget.cs','if (_performanceWallHidden?.Invoke(renderer) == true)','if (false && _performanceWallHidden?.Invoke(renderer) == true)',1)],
                'settled performance-hidden world sources make zero per-eye mesh material and MPB reads'),
            ('performance-hidden-renotified',[('WorldMaterialBudget.cs','surface.PerformanceHidden = true;','surface.PerformanceHidden = false;',1)],
                'settled hidden source notifies geometry consumers once rather than every eye'),
        ])
    if 'bool rendererBlockEmpty' in sources['WorldMaterialBudget.Materials.cs']:
        changes.extend([
            ('nonempty-renderer-block-skipped',[('WorldMaterialBudget.Materials.cs','bool rendererBlockEmpty = _block.isEmpty;','bool rendererBlockEmpty = true;',1)],
                'native MPB blend mode override remains original rather than fixed opaque private pass'),
            ('nonempty-slot-block-skipped',[('WorldMaterialBudget.Materials.cs','bool slotBlockEmpty = _slotBlock.isEmpty;','bool slotBlockEmpty = true;',1)],
                'live native effect veto is per material subslot'),
            ('either-empty-block-skips-both',[('WorldMaterialBudget.Materials.cs','if (rendererBlockEmpty && slotBlockEmpty) return false;','if (rendererBlockEmpty || slotBlockEmpty) return false;',1)],
                'live native effect veto is per material subslot'),
        ])
    if 'string name = node.name;' in sources['WorldMaterialBudget.Scope.cs']:
        changes.append(('scope-name-cross-eye-stale',[
            ('WorldMaterialBudget.Scope.cs','private readonly List<Transform> _ancestry = new();',
                'private readonly List<Transform> _ancestry = new();\n        private readonly Dictionary<Transform,string> _cachedNames = new();',1),
            ('WorldMaterialBudget.Scope.cs','string name = node.name;',
                'string name = _cachedNames.TryGetValue(node, out string cached) ? cached : _cachedNames[node] = node.name;',1)],
            'same native Transform renamed to Preview immediately restores original material ownership'))
    variants=[('production',sources,'')]
    if not args.production_only:
        for name,edits,expected in changes:
            copied=dict(sources)
            for path,before,after,count in edits:
                assert copied[path].count(before)==count,'source mutation binding drift: '+name
                copied[path]=copied[path].replace(before,after)
            variants.append((name,copied,expected))
    if args.case:
        unknown=set(args.case)-{v[0] for v in variants}
        if unknown:raise SystemExit('Unknown selected variants: '+','.join(sorted(unknown)))
        variants=[v for v in variants if v[0]=='production' or v[0] in args.case]
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    sys.path.insert(0,str(fixture))
    from native_scope_proof import verify as verify_native_scope
    (run/'native-scope-bindings.json').write_text(json.dumps(verify_native_scope(),indent=2)+'\n')
    from integration_bindings import verify
    bridge_paths,bridge_report=verify((args.integration_root or root).resolve())
    (run/'integration-bindings.json').write_text(json.dumps(bridge_report,indent=2)+'\n')
    deps=[Path.home()/'.nuget/packages'/relative for relative in (
        'harmonyx/2.7.0/lib/net45/0Harmony.dll','monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll',
        'monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll','mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll')]
    assert all(p.is_file() for p in deps),'pinned production HarmonyX dependencies must exist'
    world_shader=root/'unity/GloomhavenVR.Assets/Assets/Bundle/Environments/WorldSimpleMaterial.shader'
    inputs=paths+wall_paths+[world_shader,prop_path]+bridge_paths+[Path(__file__).resolve()]+sorted(p for p in fixture.rglob('*') if p.is_file() and '__pycache__' not in p.parts)+deps
    hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    manifest={'result':str(run/'results.txt'),'cases':[]}
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name,texts,expected in variants:
        case=run/name;production=case/'production';production.mkdir(parents=True)
        for filename,text in texts.items():
            if filename=='WorldMaterialBudget.Materials.cs':
                text=text.replace('variant.CopyPropertiesFromMaterial(original);','NativeWriteObserver.Copy(variant, original);')
            text=text.replace('renderer.GetPropertyBlock(_block);','NativeWriteObserver.RendererBlock(renderer, _block);')
            text=text.replace('renderer.GetPropertyBlock(_slotBlock, slot);','NativeWriteObserver.SlotBlock(renderer, _slotBlock, slot);')
            if filename=='WorldMaterialBudget.cs':
                text=text.replace('renderer.sharedMaterials = _slots.ToArray();','NativeWriteObserver.Slots(renderer, _slots.ToArray());')
                text=text.replace('renderer.GetSharedMaterials(_slots);','NativeWriteObserver.Read(renderer, _slots);')
                text=text.replace('UnityEngine.Object.FindObjectsOfType<MapChoreographer>(true)','NativeWriteObserver.FindMaps()')
                text=text.replace('MeshFilter filter = renderer.GetComponent<MeshFilter>();\n                    Mesh mesh',
                                  'MeshFilter filter = NativeWriteObserver.ReadMesh(renderer);\n                    Mesh mesh')
            if filename=='PropGrab.VisualRoots.cs':
                text=text.replace('foreach (GrabbableProp g in Registry.Values)\n            {','foreach (GrabbableProp g in Registry.Values)\n            {\n                GloomhavenVR.Core.NativeWriteObserver.RegistryVisits++;')
                text=text.replace('foreach (GrabbableProp prop in Registry.Values)\n        {','GloomhavenVR.Core.NativeWriteObserver.PropCopies++;\n        foreach (GrabbableProp prop in Registry.Values)\n        {\n            GloomhavenVR.Core.NativeWriteObserver.RegistryVisits++;')
            (production/filename).write_text(text)
        project=case/'World.csproj';shutil.copyfile(fixture/'World.csproj',project)
        assembly='WorldMaterial_'+name.replace('-','_')
        command=[dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,
            '-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed'),'-p:HarmonyPath='+str(deps[0])]
        result=subprocess.run(command,capture_output=True,text=True);(case/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a passing control')
        manifest['cases'].append({'name':name,'dll':str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    (run/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    unity=run/'unity';(unity/'Assets/Editor').mkdir(parents=True);(unity/'Packages').mkdir();(unity/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/WorldRunner.cs',unity/'Assets/Editor/WorldRunner.cs')
    shutil.copyfile(fixture/'Bridge.shader',unity/'Assets/Bridge.shader');native=(fixture/'Native.shader').read_text()
    for shader in ('Amp_Basic_N_MRAO','Amp_Low/Amp_Basic_N_MRAO_Low','Amp_Basic_WallFade',
            'Amp_Low/Amp_Basic_WallFade_Low','Amp_Basic','Amp_Low/Amp_Basic_Low','Fixture/UnreviewedWorld'):
        (unity/'Assets'/(shader.replace('/','_')+'.shader')).write_text(native.replace('Amp_Basic_N_MRAO',shader,1))
    shutil.copyfile(world_shader,unity/'Assets/WorldSimpleMaterial.shader')
    shutil.copyfile(fixture/'AttachmentNative.shader',unity/'Assets/Amp_Basic_WallFade.shader')
    for dependency in deps:shutil.copyfile(dependency,unity/'Assets'/dependency.name)
    (unity/'Packages/manifest.json').write_text('{"dependencies":{}}\n');(unity/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (run/'source-hashes.json').write_text(json.dumps({'sha256':hashes,'coverage':'partial' if args.production_only or args.case else 'production-and-negative-controls',
        'cases':[v[0] for v in variants],'limits':['Native scene/controllers/config are explicit surrogate boundaries.',
        'Fixture bridge shader proves ownership/delivery pixels, not production world shading; separate shader lane proves native fragment routes.',
        'Actual Unity material/MPB/cloning/renderer camera calls and production final Harmony postfix execute.',
        'Complete production attachment classification, channel establishment, native drive, textures and swap restoration execute; scene membership/held/floor decisions are explicit boundaries.',
        'Attachment pixels use the actual production world shader and a GL native HIGH clip surrogate; native simplex sample/art/lighting and Windows bytecode remain outside this lane.',
        'No headset FPS, full original scene, Windows shader execution or multiplayer acceptance.']},indent=2)+'\n')
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(unity),'-executeMethod','WorldRunner.Start',
        '-worldManifest',str(run/'manifest.json'),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
    try:result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    finally:
        for cache in ('Library','Temp'):shutil.rmtree(unity/cache,ignore_errors=True)
    changed={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs if hashlib.sha256(p.read_bytes()).hexdigest()!=hashes[str(p)]}
    (run/'source-stability.json').write_text(json.dumps({'unchanged':not changed,'changed':changed},indent=2)+'\n')
    if changed:raise SystemExit('Source changed during runtime: '+str(run))
    (run/'unity-exit-code.txt').write_text(str(result.returncode)+'\n');report=run/'results.txt'
    if report.is_file():print(report.read_text(),end='')
    if 'Shader error in ' in (run/'unity.log').read_text():raise SystemExit('Shader compile failure cannot pass a control')
    if result.returncode or not report.is_file():raise SystemExit('Unity runtime FAIL: '+str(run/'unity.log'))
    print(('PARTIAL PASS' if args.production_only or args.case else 'PASS')+': '+str(len(variants))+' runtime variants; '+str(run))


if __name__=='__main__':main()
