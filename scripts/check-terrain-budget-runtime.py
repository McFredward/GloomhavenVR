#!/usr/bin/env python3
"""Execute private terrain geometry/leases and the production shader in Unity 2021.3.5.

This focused suite uses explicit native scene/config and verified-bank boundaries.
Actual Unity meshes, material properties, clone behavior and Camera.Render pixels run.
It does not establish native game/headset appearance, network behavior or FPS.
"""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    assert source.count(signature) == 1, 'source API binding drift: ' + signature
    match = source.index(signature)
    start = source.rfind('\n', 0, match) + 1
    line = source[start:source.index('\n', start)]
    indent = len(line) - len(line.lstrip())
    closing = '\n' + ' ' * indent + '}\n'
    return source[start:source.index(closing, start) + len(closing)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT/'.planning/debug/terrain-budget-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--production-only', action='store_true')
    parser.add_argument('--case', action='append')
    parser.add_argument('--wall-source-root', type=Path, help='Explicit frozen wall worker source for the cross-owner native-write fixture; defaults to source-root')
    parser.add_argument('--integration-root', type=Path, help='Frozen CoreModule source for exact wall-release callback; defaults to source-root')
    args = parser.parse_args()
    root=args.source_root.resolve(); fixture=ROOT/'tests/terrain-budget-runtime'
    paths=[root/'src/GloomhavenVR/Core/Perf/ScenarioTerrainBudget.cs',root/'src/GloomhavenVR/Core/Perf/ScenarioTerrainBudget.Geometry.cs',
        root/'src/GloomhavenVR/Core/Perf/ScenarioTerrainBudget.Admission.cs',
        root/'unity/GloomhavenVR.Assets/Assets/Bundle/Environments/ScenarioCheapTerrain.shader',
        root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshStream.cs']
    source, geometry, admission, shader=[p.read_text() for p in paths[:4]]
    core_path=(args.integration_root.resolve() if args.integration_root else root)/'src/GloomhavenVR/Core/CoreModule.cs'
    core=core_path.read_text()
    assert 'ScenarioTerrainBudget.ConfigurePerformanceWallVisibility(WallSegmentFade.IsPerformanceHidden);' in core, 'terrain exact wall-policy wiring missing'
    callbacks=re.findall(r'WallSegmentFade.ConfigurePerformanceMaskRestored\((renderer\s*=>\s*\{.*?\})\);',core,re.S)
    assert len(callbacks)==1, 'exact wall-release callback binding drift'
    callback=callbacks[0]
    assert callback.count('ScenarioTerrainBudget.MaterialReady(renderer);')==1, 'exact final wall release must queue the source once'
    ownership_paths=[root/'src/GloomhavenVR/Board/FigureGrab'/name for name in ('PropGrab.cs','HeldProps.cs','NetHeldProps.cs')]
    prop, held, remote=[p.read_text() for p in ownership_paths]
    assert 'internal static int Count => Held.Count;' in held, 'held source count binding drift'
    assert 'internal static bool OwnsRendererOf(Transform? t) =>\n        LocalOwnsRendererOf(t) || NetHeldProps.OwnsRendererOf(t);' in held, 'held source ownership forwarding drift'
    ownership='using System; using System.Collections.Generic; using UnityEngine; using GloomhavenVR.Hands; using ScenarioRuleLibrary;\nnamespace GloomhavenVR.Board.FigureGrab {\n'
    ownership+='internal static partial class PropGrab {\n'+method(prop,'    internal static bool OwnsRendererOf(')+method(prop,'    internal static void CopyVisualRoots(')+'}\n'
    ownership+='internal static partial class HeldProps { internal static int Count => Held.Count;\ninternal static bool OwnsRendererOf(Transform? t) => LocalOwnsRendererOf(t) || NetHeldProps.OwnsRendererOf(t);\n'
    for signature in ('    internal static bool LocalOwnsRendererOf(', '    internal static bool TryGetSlot(int slot, out CObjectProp prop, out HandSide side)',
        '    internal static bool TryGetSlot(int slot, out CObjectProp prop, out GameObject visual,'):
        ownership+=method(held,signature)
    ownership+='}\ninternal static partial class NetHeldProps {\n'+method(remote,'    internal static bool OwnsRendererOf(')+method(remote,'    internal static void CopyVisualRoots(')+'}\n}\n'
    # Count exact primitive visual-root accesses, then execute the same Unity
    # getter. The registry/read APIs above are source-extracted, not a toy verdict.
    ownership=ownership.replace('v.transform','GloomhavenVR.Core.TerrainOwnershipObserver.VisualTransform(v)')
    ownership=ownership.replace('    internal static void CopyVisualRoots(List<GameObject> destination)\n    {',
        '    internal static void CopyVisualRoots(List<GameObject> destination)\n    {\n        GloomhavenVR.Core.TerrainOwnershipObserver.RootCopies++;')
    wall_path=(args.wall_source_root.resolve() if args.wall_source_root else root)/'src/GloomhavenVR/Core/WallFade/WallSegmentFade.HoldQuery.cs'
    wall_source=wall_path.read_text()
    # Bind extraction to the same source bytes later recorded in the final receipt;
    # this also catches a parallel worker editing the optional cross-lane source
    # between extraction and the native coverage export.
    extracted_hashes={str(p):hashlib.sha256(value.encode()).hexdigest()
        for p,value in zip(paths[:4]+ownership_paths+[wall_path], [source,geometry,admission,shader,prop,held,remote,wall_source])}
    extracted_hashes[str(core_path)]=hashlib.sha256(core.encode()).hexdigest()
    hide='using System.Collections.Generic; using UnityEngine;\nnamespace GloomhavenVR.Core { internal sealed class TerrainWallHide {\nprivate readonly HashSet<Renderer> _hidByEnable = new();\ninternal bool Owns(Renderer r) => _hidByEnable.Contains(r);\nprivate static bool FloorNeverFades(Renderer r) => false;\nprivate static bool HeldNeverFades(Renderer r) => false;\n'+method(wall_source,'    internal void HideByEnable(Renderer r)')+'}\n}\n'
    assert 'StaticBatchingUtility' not in source+geometry and 'SetStaticBatchInfo' not in source+geometry
    assert not re.search(r'(?<![\w])(?:Filter\.sharedMesh|Renderer\.sharedMaterials)\s*=(?!=)', source+geometry), 'native cloning sources must stay unchanged'
    controls=[
        ('world-refusal-falls-through', 'supported &= !world || _worldOwns?.Invoke(next) == true;', 'supported &= true;', 'changed world variant refusal between actual eyes keeps every repeated terrain source native', source, 1),
        ('world-variant-owner-bypassed', 'world ? CurrentWorldVariant(original, shared)', 'world ? CheapMaterial(original)', 'shared world terrain variants preserve every admitted native source', source, 1),
        ('floorhex-veto-missing','AuthoredName(mesh.name).IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0','false','complete floor identity veto recognizes FloorHex',source,1),
        ('bank-identity-veto-missing','!StructuralIdentity(filter.sharedMesh) ? 2','false ? 2','eligible non-floor terrain has private proxies while floors remain native',source,1),
        ('wall-owner-veto-missing','if (component is ProceduralWall) role |= ComponentRole.Structural;','if (component is ProceduralWall or ProceduralMapTile) role |= ComponentRole.Structural;','eligible non-floor terrain has private proxies while floors remain native',admission,1),
        ('temporary-readiness-bypass','while (active && ready && nodes-- > 0','while (active && nodes-- > 0','temporary bank readiness retains discovery',source,1),
        ('terminal-pending-retained','if (unavailable)\n                {\n                    _pending.Clear(); _queued.Clear();','if (unavailable)\n                {\n                    /* injected terminal pending leak */','terminal bank failure clears pending discovery',source,1),
        ('later-readiness-never-reseeds','_assetsWereReady = ready;','/* injected readiness transition loss */','eligible non-floor terrain has private proxies while floors remain native',source,1),
        ('hitch-skip','Mathf.Clamp(delta, 0f, 1f / 30f)','delta','stalled frame retains visible intermediate geometry',geometry,1),
        ('original-endpoint','_current = _percent >= 100 ? null : _target;','_current = _target;','near terrain uses selected full geometry',geometry,1),
        ('native-mask-stuck','Renderer.forceRenderingOff = false;','Renderer.forceRenderingOff = true;','post-render restores every native source',geometry,1),
        ('native-write-proxy-stuck','if (_proxyRenderer != null) _proxyRenderer.enabled = false;','/* injected stale proxy */','post-render restores every native source',geometry,1),
        ('detail-has-no-effect','_progress >= 1f) _current = _percent >= 100 ? null : _target;','_progress >= 1f) _current = null;','coarse 3D endpoint materially reduces',geometry,1),
        ('original-triangles-through-endpoint','_current = _percent >= 100 ? null : _target;','_current = _percent >= 100 ? null : _morph;','coarse 3D endpoint materially reduces',geometry,1),
        ('identical-tier-substitute','if (percent < 100 && TriangleCount(target) >= OriginalTriangles) { percent = 100; target = _exact; }','/* injected private exact-tier overhead */','coarse bank tier without actual triangle saving retains',geometry,1),
        ('actor-scope-bypass',' or ActorBehaviour',' /* injected actor veto bypass */','actor-owned source with genuine bank mesh is never scenery',admission,1),
        ('late-interaction-bypass','|| !CurrentScope(surface.Renderer)','/* injected live native scope veto */','late native interaction veto',source,1),
        ('native-command-buffer-bypass','|| (_nativeCameraConsumers?.Invoke(camera) ?? camera.commandBufferCount > 0)','/* injected command-buffer veto */','native command-buffer camera',source,1),
        ('inactive-host-masks-original','|| !isActiveAndEnabled','|| !enabled','deactivated terrain host retains original wall pixels',source,1),
        ('hand-proximity-bypass','NearHand(detail.Left, bounds)','false','tracked hand proximity restores',source,1),
        ('distant-detail-ignored','Mathf.Min(near, detail.Distant)','near','independent distant terrain detail cap',source,1),
        ('foreign-mesh-bypass','|| Filter.sharedMesh != Original','/* injected foreign mesh ownership */','foreign native mesh replacement',geometry,1),
        ('native-rendering-layer-not-copied','_proxyRenderer.renderingLayerMask = next.RenderingLayer;','/* injected native layer loss */','actual terrain camera proxy preserves current native rendering layers',geometry,1),
        ('material-slot-block-dropped','Renderer.GetPropertyBlock(SlotBlock, slot);','SlotBlock.Clear();','native material-slot MPB precedence',geometry,1),
        ('live-renderer-effect-ignored','if (LiveSpecialEffect(Block)) return false;','/* injected live renderer effect bypass */','live renderer-wide vertex effect retains',geometry,1),
        ('live-slot-effect-ignored','if (LiveSpecialEffect(SlotBlock)) return false;','/* injected live slot effect bypass */','live material-slot emissive effect retains',geometry,1),
        ('canonical-original-ignored','_canonicalMaterial?.Invoke(material) ?? material','material','existing environment material variant resolves',source,1),
        ('native-visibility-bypass','|| !surface.Renderer.enabled','/* injected source enabled */','native disabled terrain stays original after shared ownership reuse',source,1),
        ('shader-cutoff-ramp-ignored','clip(m - _Cutoff);','clip(m - .5);','production native wall map has multiple visible intermediate frames',shader,1),
        ('shader-floor-fades','_GHVRTerrainNeverFade > .5 || ','','never-fade floor ignores native wall channel',shader,1),
        ('native-simplex-scrambled','return dot(m*m, float4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));','return 0.;','production simplex matches original native DXBC instruction samples',shader,1),
        ('native-high-foundation-dropped','float foundation = min(max(1. - i.world.y, 0.), 5.) / 3.;','float foundation = 0.;','production HIGH and toggle-native map foundation',shader,1),
        ('native-high-enable-dropped','float M = m * _EnableOcclusionMap;','float M = 0.;','production HIGH and toggle-native map foundation',shader,1),
        ('native-high-cutoff-fixed','clip(value - _Cutoff);','clip(value - .5);','production HIGH and toggle-native map foundation',shader,1),
    ]
    controls += [
        ('terrain-exact-hidden-update-skip-lost','\n                    if (PerformanceWallHidden(surface.Renderer)) continue;',
         '\n                    /* injected hidden Update work */',
         'exact performance-hidden terrain has zero per-source validation detail or geometry work',source,1),
        ('terrain-exact-hidden-camera-skip-lost','\n                        if (PerformanceWallHidden(surface.Renderer)) continue;',
         '\n                        /* injected hidden camera native reads */',
         'exact performance-hidden terrain camera skips native visibility reads',source,1),
        ('terrain-hidden-native-mask-overreach','\n                    if (PerformanceWallHidden(surface.Renderer)) continue;',
         '\n                    if (PerformanceWallHidden(surface.Renderer) || surface.Renderer != null && surface.Renderer.forceRenderingOff) continue;',
         'foreign native renderer masks do not suspend unrelated terrain maintenance',source,1),
        ('terrain-exact-hidden-preparation-skip-lost','if (renderer != null && PerformanceWallHidden(renderer)) continue;',
         '/* injected hidden discovery preparation */',
         'exact already-hidden discovery performs no native filter reads or private preparation',source,1),
        ('terrain-hidden-substitute-ownership-retained','            && !PerformanceWallHidden(renderer)',
         '            /* injected hidden substitute ownership */',
         'exact performance-hidden terrain camera skips native visibility reads',source,1),
        ('terrain-hidden-current-mesh-replacement-lost','if (_surfaces.TryGetValue(id, out Surface previous) && previous.Original != filter.sharedMesh)',
         'if (_surfaces.TryGetValue(id, out Surface previous) && _surfaces.Count < 0)',
         'Regular restores current native mesh material and reparented terrain after exact release',source,1),
        ('terrain-hidden-detail-pose-read-retained','bool detailReady = false;',
         'bool detailReady = active && camera != null; detail = detailReady ? new DetailState(camera!) : default;',
         'all exact hidden terrain skips even shared head and tracked detail reads',source,1),
        ('terrain-hidden-discovery-identity-mismatch','_queued.Remove(node.gameObject.GetInstanceID());',
         '_queued.Remove(node.GetInstanceID());',
         'exact final wall release requeues previously hidden native discovery once',source,1),
    ]
    controls += [
        ('repeated-detail-pose-reads','int percent = active && camera != null ? DetailFor(surface, detail) : 100;',
         'detail = active && camera != null ? new DetailState(camera) : default; int percent = active && camera != null ? DetailFor(surface, detail) : 100;',
         'ninety-six prepared terrain sources read current head and both tracked positions only once',source,1),
        ('tracking-loss-ignored','Tracked = hand != null && hand.HasPose;','Tracked = hand != null;',
         'tracking loss is read on the next terrain Update',source,1),
        ('hand-scale-ignored','Mathf.Max(hand!.WorldScale, .0001f)','1f',
         'current tracked hand scale restores full geometry',source,1),
        ('empty-property-fast-path-removed','if (!Renderer.HasPropertyBlock())','if (false && !Renderer.HasPropertyBlock())',
         'settled empty native MPB checks one fresh guard and skips all block reads',geometry,1),
        ('property-absence-cross-eye-stale','if (!Renderer.HasPropertyBlock())','if (_hasPropertyDefaults || !Renderer.HasPropertyBlock())',
         'native material-slot MPB precedence',geometry,1),
        ('removed-property-blocks-not-cleared','if (_copiedNativeProperties || !_hasPropertyDefaults)','if (!_hasPropertyDefaults)',
         'removed native slot property block is cleared',geometry,1),
        ('empty-property-private-rewrites','if (_copiedNativeProperties || !_hasPropertyDefaults)',
         'if (_copiedNativeProperties || !_hasPropertyDefaults || _hasPropertyDefaults)',
         'settled empty native MPB checks one fresh guard and skips all block reads',geometry,1),
        ('empty-slot-effect-reads','!block.isEmpty && (block.GetFloat','(block.GetFloat',
         'empty native material-slot block skips effect reads',geometry,1),
        ('world-floor-safety-channel-lost','block.SetFloat("_GHVRWorldNeverFade", Floor ? 1f : 0f);',
         'block.SetFloat("_GHVRWorldNeverFade", 0f);',
         'private floor renderer block sets legacy and world never-fade channels',geometry,1),
    ]
    controls += [
        ('captured-structural-coverage-missing','or "CR_INT_Stone_Int_Wall_01"','or "Unknown_CapturedWall"','captured structural definition is prepared and leased',source,1),
        ('native-foundation-boundary-ignored','|| StructuralBoundary(nodeName)','/* injected source-template boundary bypass */','same audited structural mesh under a foundation cap or doorway template stays native',admission,1),
        ('settled-proxy-pose-rewritten','if (!_hasPose || !SameMatrix(native, _sourcePose) || !SameMatrix(owner, _ownerPose))','if (!_hasPose || _hasPose)','settled terrain camera retains private proxy transform without repeated native writes',geometry,1),
        ('material-route-cross-camera-stale','_routesThisCamera.Clear();','/* injected cross-camera route reuse */','native keyword edits remain live between actual camera invocations',source,1),
        ('revoked-admission-counted','if (!surface.IsMasked) continue;','if (surface == null) continue;','revoked native-write camera leases are absent from terrain completion counters',source,1),
    ]
    controls += [
        ('repeated-mesh-admission-read', 'if (!meshes.TryGetValue(Original, out bool eligible))',
         'bool eligible; if (true)', 'repeated native mesh admission is read exactly once', geometry, 1),
        ('mesh-admission-cross-camera-stale', '_meshThisInvocation.Clear();',
         '/* injected stale mesh admission */', 'repeated native mesh admission is read exactly once', source, 1),
    ]
    # Source-bound operation ordering: the inactive dissolve branch precedes all
    # map, pow and simplex work. Pixel/native-route controls below prove its result.
    assert shader.index('if (ToggleWallFade == 0)') < shader.index('float4 occlusion = tex2D(_TilesOcclusionMap, uv);'), 'inactive native dissolve early-out occurs before texture work'
    controls += [
        ('terrain-cap-ignored','limit > 0 && candidates >= limit','limit < 0 && candidates >= limit',
         'terrain CPU cap admits only bounded private substitutes',source,2),
        ('terrain-frustum-ignored','if (OutsideFrustum(surface.Renderer))','if (OutsideFrustum(surface.Renderer) && limit < 0)',
         'actual current camera frustum rejects offscreen substitute work',source,1),
        ('terrain-classification-cross-type-stale','if (shared && Roles.TryGetValue(type, out ComponentRole cached)) return cached;',
         'if (shared && Roles.Count > 0 && component is CInteractable) return ComponentRole.None;',
         'new native component between eyes revokes capped terrain admission',admission,1),
        ('inactive-shader-toggle-inverted','if (ToggleWallFade == 0)','if (ToggleWallFade != 0)',
         'production native wall map has multiple visible intermediate frames',shader,1),
        ('inactive-shader-unconditional','if (ToggleWallFade == 0)','if (true)',
         'production native wall map has multiple visible intermediate frames',shader,1),
        ('terrain-stereo-second-eye-dropped','&& (!stereo || !GeometryUtility.TestPlanesAABB(left, bounds)\n                && !GeometryUtility.TestPlanesAABB(right, bounds))',
         '&& (!stereo || !GeometryUtility.TestPlanesAABB(left, bounds))',
         'terrain union retains a source visible exclusively to the second eye',admission,1),
        ('inactive-shader-clip-lost','if (_GHVRTerrainNativeRoute >= 1.5) clip(1. - _Cutoff);',
         '/* injected high authored-cutoff bypass */',
         'inactive HIGH and toggle-native clip retain original authored cutoff',shader,1),
    ]
    controls += [
        ('terrain-prop-memo-bypassed','if (_propRootsReady) return;','if (_propRootsReady && _propRoots.Count < 0) return;',
         'terrain shared scope reads exact prop visuals once per camera instead of every ancestor',admission,1),
        ('terrain-prop-membership-ignored','state.Prop |= propRoots?.Contains(node) == true;','state.Prop |= propRoots != null && propRoots.Count < 0;',
         'unregistered local held terrain root remains native',source,1),
        ('terrain-local-root-skipped','if (HeldProps.TryGetSlot(slot, out _, out GameObject visual, out _, out _)) _propVisuals.Add(visual);',
         'if (slot < 0) _propVisuals.Add(null!);','unregistered local held terrain root remains native',admission,1),
        ('terrain-remote-root-skipped','NetHeldProps.CopyVisualRoots(_propVisuals);','/* injected remote hold missing */',
         'unregistered remote held terrain root remains native',admission,1),
        ('terrain-prop-roots-cross-camera-stale','_propRoots.Clear(); _propVisuals.Clear(); _propRootsReady = false;',
         'if (_propRoots.Count != 16) { _propRoots.Clear(); _propVisuals.Clear(); _propRootsReady = false; }',
         'unregistered local held terrain root remains native',source,1),
        ('terrain-native-writer-scope-stale','            ClearValidation();\n        }\n        internal void RecoverLeases()',
         '            /* injected stale native writer scope */\n        }\n        internal void RecoverLeases()',
         'native writer registration and reparent invalidate same-pass terrain roots',source,1),
        ('terrain-current-lease-native-mask-dependent','internal bool HasCurrentRenderLease => _masked && Renderer != null;',
         'internal bool HasCurrentRenderLease => IsMasked;',
         'terrain current lease survives late native mask edits until explicit owner release',geometry,1),
        ('terrain-exhausted-cap-still-reads','if (shared && limit > 0 && candidates >= limit)',
         'if (false && shared && limit > 0 && candidates >= limit)',
         'exhausted shared terrain cap skips all native visibility reads',source,1),
        ('terrain-pre-cull-lease-recovery-lost','RecoverLeases();\n                if (camera == null',
         '/* injected prior camera lease leak */\n                if (camera == null',
         'narrowed next-eye terrain budget releases wider previous leases',source,1),
        ('terrain-canonical-memo-bypassed','if (_canonicalThisCamera.TryGetValue(material, out Material original)) return original;',
         'Material original; /* injected repeated native original lookup */',
         'ninety-six repeated terrain sources resolve canonical world material and variant exactly once',source,1),
        ('terrain-world-variant-memo-bypassed','if (shared && _worldVariantsThisCamera.TryGetValue(original, out Material cached)) return cached;',
         'if (shared) _worldVariantsThisCamera.Remove(original); /* injected repeated world variant lookup */',
         'ninety-six repeated terrain sources resolve canonical world material and variant exactly once',source,1),
        ('terrain-world-variant-cross-eye-stale','_worldVariantsThisCamera.Clear();',
         '/* injected stale next-eye world variant */',
         'shared terrain material maps expire before the next actual eye',source,1),
        ('terrain-canonical-cross-eye-stale','_canonicalThisCamera.Clear();',
         '/* injected stale next-eye native original */',
         'shared terrain material maps expire before the next actual eye',source,1),
        ('terrain-nested-camera-owner-lost','                        _leaseCamera = camera;\n                        surface.Mask();',
         '                        /* injected resumed outer lease owner loss */\n                        surface.Mask();',
         'nested terrain material lookup ends with every native source unmasked',source,1),
        ('terrain-master-membership-gate-lost','internal bool OwnsRenderSubstitute(Renderer renderer) => PerfConfig.TerrainSubstitutionOn',
         'internal bool OwnsRenderSubstitute(Renderer renderer) => true',
         'terrain master off drops prepared ownership immediately before Update',source,1),
        ('terrain-master-camera-gate-lost','|| !isActiveAndEnabled || !PerfConfig.TerrainSubstitutionOn',
         '|| !isActiveAndEnabled',
         'terrain master off next camera releases interrupted leases without native material preparation',source,1),
        ('terrain-master-update-release-lost','if (_active || _surfaces.Count != 0 || _pending.Count != 0) RestoreAll();',
         '/* injected disabled terrain private preparation retained */',
         'terrain master off Update clears prepared proxies and queued native discovery',source,1),
        ('terrain-master-discovery-gate-lost','private bool Enabled => PerfConfig.TerrainSubstitutionOn && VRSession.IsRunning',
         'private bool Enabled => VRSession.IsRunning',
         'terrain master off rejects new native discovery without queued preparation',source,1),
    ]
    variants=[('production',source,geometry,admission,shader,'')]
    if not args.production_only:
        for name,before,after,expected,text,count in controls:
            assert text.count(before)==count, 'mutation binding drift: '+name
            values=[source,geometry,admission,shader]
            values[values.index(text)]=text.replace(before,after)
            variants.append((name,*values,expected))
    wall_controls=[
        ('wall-native-hide-terrain-release-lost','                ScenarioEnvironmentBudget.BeforeNativeRendererWrite(r);',
         '                /* injected native disable without current terrain release */',
         'actual wall native disable synchronously releases current terrain proxy before its draw'),
        ('wall-already-disabled-terrain-consumer-ignored',
         'r.enabled || ScenarioTerrainBudget.HasCurrentRenderLease(r)\n                || ScenarioEnvironmentBudget.OwnsRenderSubstitute(r)',
         'r.enabled',
         'already native-disabled wall releases its actual terrain consumer without stealing visibility ownership'),
        ('wall-foreign-disabled-ledger-adopted','                _hidByEnable.Add(r);\n            }',
         '            }\n            _hidByEnable.Add(r);',
         'prepared disabled wall without current terrain lease is never adopted or rewritten'),
    ]
    observed_wall={'production':hide}
    observed_callback={'production':callback}
    if not args.production_only:
        for name,before,after,expected in wall_controls:
            assert hide.count(before)==1, 'actual wall mutation binding drift: '+name
            observed_wall[name]=hide.replace(before,after)
            variants.append((name,source,geometry,admission,shader,expected))
        name='terrain-hidden-final-release-requeue-lost'
        observed_callback[name]=callback.replace('ScenarioTerrainBudget.MaterialReady(renderer);','/* injected final source rediscovery lost */')
        variants.append((name,source,geometry,admission,shader,'exact final wall release requeues previously hidden native discovery once'))
    if args.case:
        unknown=set(args.case)-{v[0] for v in variants}
        if unknown: raise SystemExit('Unknown selected variants: '+', '.join(sorted(unknown)))
        variants=[v for v in variants if v[0]=='production' or v[0] in args.case]
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    native_command=[str(Path.home()/'unitypy-venv/bin/python'),str(fixture/'verify-native-coverage.py'),
        '--source-root',str(root),'--output-dir',str(run)]
    native_result=subprocess.run(native_command,capture_output=True,text=True,check=True)
    (run/'native-verification.log').write_text(native_result.stdout+native_result.stderr)
    print(native_result.stdout,end='')
    native=json.loads((run/'native-coverage.json').read_text())
    inputs=paths+ownership_paths+[wall_path,core_path,Path(__file__).resolve(),Path(native['nativeBundle']),
        root/'tools/environment-mesh/export-native.py',
        root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/index.json']
    inputs+=sorted(p for p in fixture.rglob('*') if p.is_file())
    inputs+=[Path(entry[k]) for entry in native['entries'] for k in ('exactPath','coarsePath')]
    input_hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    assert all(input_hashes[path]==value for path,value in extracted_hashes.items()), 'source changed between extraction and receipt binding'
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    manifest={'result':str(run/'results.txt'),'cases':[]}
    for name,src,geo,admit,shade,expected in variants:
        case=run/name; production=case/'production'; production.mkdir(parents=True)
        pose_call='_proxyTransform.SetPositionAndRotation(_sourceTransform.position, _sourceTransform.rotation);'
        assert geo.count(pose_call)==1, 'actual production private pose observer binding drift'
        observed_geometry=geo.replace(pose_call,
            'TerrainWriteObserver.SetPositionAndRotation(_proxyTransform, _sourceTransform.position, _sourceTransform.rotation);')
        for signature,observation in (
            ('internal bool Validate(Dictionary<Mesh, bool> meshes)','TerrainWorkObserver.Validate(Renderer);'),
            ('internal void StepGeometry(int percent, float delta)','TerrainWorkObserver.Geometry(Renderer);'),
            ('internal Surface(MeshRenderer renderer, MeshFilter filter, Transform owner)','TerrainWorkObserver.Prepare(renderer);'),
        ):
            before=signature+'\n        {'
            assert observed_geometry.count(before)==1, 'hidden work observer binding drift: '+signature
            observed_geometry=observed_geometry.replace(before,before+'\n            '+observation)
        for before,after in (
            ('Renderer.HasPropertyBlock()', 'TerrainReadObserver.HasPropertyBlock(Renderer)'),
            ('Renderer.GetPropertyBlock(Block);', 'TerrainReadObserver.GetPropertyBlock(Renderer, Block);'),
            ('Renderer.GetPropertyBlock(SlotBlock, slot);', 'TerrainReadObserver.GetPropertyBlock(Renderer, SlotBlock, slot);'),
            ('_proxyRenderer.SetPropertyBlock(Block);', 'TerrainReadObserver.SetPropertyBlock(_proxyRenderer, Block);'),
            ('_proxyRenderer.SetPropertyBlock(null, slot);', 'TerrainReadObserver.SetPropertyBlock(_proxyRenderer, null, slot);'),
            ('_proxyRenderer.SetPropertyBlock(SlotBlock, slot);', 'TerrainReadObserver.SetPropertyBlock(_proxyRenderer, SlotBlock, slot);'),
            ('block.GetFloat(', 'TerrainReadObserver.Effect(block, '),
        ):
            observed_geometry=observed_geometry.replace(before,after)
        for before,after in (
            ('Position = head.position;', 'Position = TerrainReadObserver.HeadPosition(head);'),
            ('head.lossyScale.x', 'TerrainReadObserver.HeadScale(head)'),
            ('hand!.transform.position', 'TerrainReadObserver.HandPosition(hand!)'),
        ):
            assert before in src, 'actual production primitive observer binding drift: '+before
            src=src.replace(before,after)
        src=src.replace('surface.Renderer.GetSharedMaterials(_materialScratch);', 'TerrainWriteObserver.MaterialReads++; surface.Renderer.GetSharedMaterials(_materialScratch);')
        before='private static int DetailFor(Surface surface, DetailState detail)\n        {'
        assert src.count(before)==1, 'hidden detail observer binding drift'
        src=src.replace(before,before+'\n            TerrainWorkObserver.Detail(surface.Renderer);')
        before='MeshFilter filter = node.GetComponent<MeshFilter>();'
        assert src.count(before)==1, 'hidden preparation filter observer binding drift'
        src=src.replace(before,'MeshFilter filter = TerrainWorkObserver.Filter(node, renderer);')
        for before,after in (
            ('surface.Renderer.enabled', 'TerrainReadObserver.Enabled(surface.Renderer)'),
            ('surface.Renderer.gameObject.activeInHierarchy', 'TerrainReadObserver.Active(surface.Renderer)'),
            ('surface.Renderer.forceRenderingOff', 'TerrainReadObserver.Mask(surface.Renderer)'),
        ):
            expected_count=0 if name=='native-visibility-bypass' and before=='surface.Renderer.enabled' else (2 if name=='terrain-hidden-native-mask-overreach' and before=='surface.Renderer.forceRenderingOff' else 1)
            assert src.count(before)==expected_count, 'actual production visibility observer binding drift: '+before
            src=src.replace(before,after)
        (production/'Terrain.cs').write_text(src); (production/'Geometry.cs').write_text(observed_geometry)
        observed_admission=admit.replace('_propRoots.Add(visual.transform);',
            '_propRoots.Add(TerrainOwnershipObserver.VisualTransform(visual));')
        (production/'Admission.cs').write_text(observed_admission)
        (production/'PropOwnership.cs').write_text(ownership)
        (production/'WallHide.cs').write_text(observed_wall.get(name,hide))
        (production/'WallRelease.cs').write_text('using System; using UnityEngine; namespace GloomhavenVR.Core { internal static class TerrainFinalWallRelease { private static readonly Action<Renderer> Release = '+observed_callback.get(name,callback)+'; internal static void Invoke(Renderer renderer) => Release(renderer); } }')
        (production/'MeshStream.cs').write_text(paths[4].read_text())
        shutil.copyfile(fixture/'NativeCoverage.cs',production/'NativeCoverage.cs')
        for extra in fixture.glob('*.cs'):
            if extra.name not in ('Boundaries.cs','Program.cs','NativeCoverage.cs'):
                shutil.copyfile(extra,production/extra.name)
        literals=['new Entry('+','.join([json.dumps(entry['name']),json.dumps(entry['exactPath']),
            json.dumps(entry['coarsePath']),str(entry['sourceTriangles'])])+')' for entry in native['entries']]
        (production/'TerrainCoverageData.cs').write_text(
            'internal static class TerrainCoverageData { internal readonly struct Entry { '+
            'internal readonly string Name,Exact,Coarse; internal readonly int Triangles; '+
            'internal Entry(string n,string e,string c,int t) {Name=n;Exact=e;Coarse=c;Triangles=t;} } '+
            'internal static readonly Entry[] Entries={'+','.join(literals)+'}; }')
        project=case/'Terrain.csproj'; shutil.copyfile(fixture/'Terrain.csproj',project)
        assembly='TerrainBudget_'+name.replace('-','_')
        command=[dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,
            '-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')]
        shader_name='GloomhavenVR/ScenarioCheapTerrain'+('_'+name.replace('-','_') if name!='production' else '')
        (case/'shader.shader').write_text(shade.replace('GloomhavenVR/ScenarioCheapTerrain',shader_name))
        common=shade[shade.index('CGINCLUDE')+len('CGINCLUDE'):shade.index('ENDCG')]
        noise_name='Fixture/TerrainNativeNoise_'+name.replace('-','_')
        (case/'noise.shader').write_text('Shader "'+noise_name+'" { Properties { _SamplePosition("Position",Vector)=(0,0,0,0) } '+
            'CGINCLUDE\n'+common+'\nfloat4 _SamplePosition;\nENDCG\nSubShader { Pass { CGPROGRAM\n#pragma target 3.0\n#pragma vertex vert\n#pragma fragment probe\n'+
            'float4 probe(v2f i):SV_Target { return float4(.5+.5*42*NativeSimplex(_SamplePosition.xyz*float3(6,7,10)),0,0,1); }\nENDCG\n} } Fallback Off }')
        high_name='Fixture/TerrainCheapHighBoundary_'+name.replace('-','_')
        high=shade.replace('GloomhavenVR/ScenarioCheapTerrain',high_name).replace('CGINCLUDE','CGINCLUDE\nfloat _NativeNoise;',1).replace(
            'Properties\n    {','Properties\n    {\n        _NativeNoise("Native noise boundary",Float)=.03',1)
        before='NativeSimplex((i.world + _Time.y * float3(.02, -.04, .006)) * float3(6., 7., 10.))'
        assert high.count(before)==1,'native high noise boundary binding drift'
        (case/'high.shader').write_text(high.replace(before,'_NativeNoise'))
        # Per-assembly boundary resolves the actual shader variant. Both native route
        # and direct production fragment probes use this injected lookup string only.
        boundary=(fixture/'Boundaries.cs').read_text().replace('Shader.Find(name)','Shader.Find(name=="GloomhavenVR/ScenarioCheapTerrain"?"'+shader_name+'":name)')
        program=(fixture/'Program.cs').read_text().replace('Shader.Find("GloomhavenVR/ScenarioCheapTerrain")','Shader.Find("'+shader_name+'")')
        program=program.replace('"Fixture/TerrainNativeNoise"','"'+noise_name+'"').replace('"Fixture/TerrainCheapHighBoundary"','"'+high_name+'"')
        # Recompile after the two binding-only files enter the owned production copy.
        (production/'Boundaries.cs').write_text(boundary); (production/'Program.cs').write_text(program)
        samples=json.loads((fixture/'native-noise-vectors.json').read_text())['samples']
        assert len(samples)==20, 'native noise vector inventory drift'
        literals=['new Sample(new Vector3('+','.join(format(x,'.9g')+'f' for x in s['position'])+'),'+format(s['nativeNoise42'],'.9g')+'f)' for s in samples]
        (production/'NoiseSamples.cs').write_text('using UnityEngine; internal static class NoiseSamples { internal readonly struct Sample { internal readonly Vector3 Position; internal readonly float Value; '+
            'internal Sample(Vector3 p,float v) {Position=p;Value=v;} } internal static readonly Sample[] Data={'+','.join(literals)+'}; }')
        # Fixture glob is disabled here because the exact same two files are now bound.
        p=project.read_text().replace('<Compile Include="$(FixtureDir)/*.cs"/>','')
        project.write_text(p)
        result=subprocess.run(command,capture_output=True,text=True)
        (case/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a passing control')
        manifest['cases'].append({'name':name,'dll':str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path=run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    unity=run/'unity'; (unity/'Assets/Editor').mkdir(parents=True); (unity/'Packages').mkdir(); (unity/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/TerrainRunner.cs',unity/'Assets/Editor/TerrainRunner.cs')
    shutil.copyfile(fixture/'NativeMaterials.shader',unity/'Assets/NativeMaterials.shader')
    shutil.copyfile(fixture/'NativeHighBranch.shader',unity/'Assets/NativeHighBranch.shader')
    for name,*_ in variants:
        for kind in ('shader','noise','high'):shutil.copyfile(run/name/(kind+'.shader'),unity/'Assets'/(name+'-'+kind+'.shader'))
    (unity/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (unity/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    partial=args.production_only or bool(args.case)
    (run/'source-hashes.json').write_text(json.dumps({'root':str(root),'sha256':input_hashes,
        'coverage':'partial' if partial else 'production-and-negative-controls','cases':[v[0] for v in variants],
        'limits':['Native scene, verified mesh bank and config boundaries are explicit surrogates.','Actual Unity geometry, cloning, shaders and Camera.Render callbacks execute.','Native controllers, Windows bytecode, original artwork, headset appearance, network behavior and FPS are not executed.']},indent=2)+'\n')
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(unity),'-executeMethod','TerrainRunner.Start',
        '-terrainManifest',str(manifest_path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
    environment=os.environ.copy(); environment['GHVR_TERRAIN_NOISE_VECTORS']=str(fixture/'native-noise-vectors.json');environment['GHVR_TERRAIN_EVIDENCE']=str(run)
    try: result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=300,env=environment)
    finally:
        for cache in ('Library','Temp'):shutil.rmtree(unity/cache,ignore_errors=True)
    changed={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs
        if hashlib.sha256(p.read_bytes()).hexdigest()!=input_hashes[str(p)]}
    (run/'source-stability.json').write_text(json.dumps({'unchanged':not changed,'changed':changed},indent=2)+'\n')
    if changed: raise SystemExit('FAIL source inputs changed during terrain compilation/runtime: '+str(run/'source-stability.json'))
    report=Path(manifest['result'])
    if report.is_file(): print(report.read_text(),end='')
    (run/'unity-exit-code.txt').write_text(str(result.returncode)+'\n')
    if 'Shader error in ' in (run/'unity.log').read_text(): raise SystemExit('Shader compile failure cannot pass as a negative control: '+str(run/'unity.log'))
    if result.returncode or not report.is_file():raise SystemExit('FAIL Unity runtime: '+str(run/'unity.log'))
    print(('PARTIAL PASS' if partial else 'PASS')+': '+str(len(variants))+' production/negative variants; evidence: '+str(run))


if __name__=='__main__':main()
