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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT/'.planning/debug/terrain-budget-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--production-only', action='store_true')
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    root=args.source_root.resolve(); fixture=ROOT/'tests/terrain-budget-runtime'
    paths=[root/'src/GloomhavenVR/Core/Perf/ScenarioTerrainBudget.cs',root/'src/GloomhavenVR/Core/Perf/ScenarioTerrainBudget.Geometry.cs',
        root/'unity/GloomhavenVR.Assets/Assets/Bundle/Environments/ScenarioCheapTerrain.shader',
        root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshStream.cs']
    source, geometry, shader=[p.read_text() for p in paths[:3]]
    assert 'StaticBatchingUtility' not in source+geometry and 'SetStaticBatchInfo' not in source+geometry
    assert not re.search(r'(?<![\w])(?:Filter\.sharedMesh|Renderer\.sharedMaterials)\s*=(?!=)', source+geometry), 'native cloning sources must stay unchanged'
    controls=[
        ('floorhex-veto-missing','AuthoredName(mesh.name).IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0','false','complete floor identity veto recognizes FloorHex',source,1),
        ('bank-identity-veto-missing','!StructuralIdentity(filter.sharedMesh) ? 2','false ? 2','eligible non-floor terrain has private proxies while floors remain native',source,1),
        ('wall-owner-veto-missing','state.Structural |= component is ProceduralWall;','state.Structural |= component is ProceduralWall or ProceduralMapTile;','eligible non-floor terrain has private proxies while floors remain native',source,1),
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
        ('actor-scope-bypass',' or ActorBehaviour',' /* injected actor veto bypass */','actor-owned source with genuine bank mesh is never scenery',source,1),
        ('late-interaction-bypass','|| !CurrentScope(surface.Renderer)','/* injected live native scope veto */','late native interaction veto',source,1),
        ('native-command-buffer-bypass','|| (_nativeCameraConsumers?.Invoke(camera) ?? camera.commandBufferCount > 0)','/* injected command-buffer veto */','native command-buffer camera',source,1),
        ('inactive-host-masks-original','|| !isActiveAndEnabled','|| !enabled','deactivated terrain host retains original wall pixels',source,1),
        ('hand-proximity-bypass','NearHand(VRHands.Left, bounds)','false','tracked hand proximity restores',source,1),
        ('distant-detail-ignored','Mathf.Min(near, PerfConfig.DistantTerrainDetailPercent)','near','independent distant terrain detail cap',source,1),
        ('foreign-mesh-bypass','|| Filter.sharedMesh != Original','/* injected foreign mesh ownership */','foreign native mesh replacement',geometry,1),
        ('native-rendering-layer-not-copied','_proxyRenderer.renderingLayerMask = next.RenderingLayer;','/* injected native layer loss */','actual terrain camera proxy preserves current native rendering layers',geometry,1),
        ('material-slot-block-dropped','Renderer.GetPropertyBlock(SlotBlock, slot);','SlotBlock.Clear();','native material-slot MPB precedence',geometry,1),
        ('live-renderer-effect-ignored','if (LiveSpecialEffect(Block)) return false;','/* injected live renderer effect bypass */','live renderer-wide vertex effect retains',geometry,1),
        ('live-slot-effect-ignored','if (LiveSpecialEffect(SlotBlock)) return false;','/* injected live slot effect bypass */','live material-slot emissive effect retains',geometry,1),
        ('canonical-original-ignored','_canonicalMaterial?.Invoke(material) ?? material','material','existing environment material variant resolves',source,1),
        ('native-visibility-bypass','|| !surface.Renderer.enabled','/* injected source enabled */','native disabled visibility',source,1),
        ('shader-cutoff-ramp-ignored','clip(m - _Cutoff);','clip(m - .5);','production native wall map has multiple visible intermediate frames',shader,1),
        ('shader-floor-fades','_GHVRTerrainNeverFade > .5 || ','','never-fade floor ignores native wall channel',shader,1),
        ('native-simplex-scrambled','return dot(m*m, float4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));','return 0.;','production simplex matches original native DXBC instruction samples',shader,1),
        ('native-high-foundation-dropped','float foundation = min(max(1. - i.world.y, 0.), 5.) / 3.;','float foundation = 0.;','production HIGH and toggle-native map foundation',shader,1),
        ('native-high-enable-dropped','float M = m * _EnableOcclusionMap;','float M = 0.;','production HIGH and toggle-native map foundation',shader,1),
        ('native-high-cutoff-fixed','clip(value - _Cutoff);','clip(value - .5);','production HIGH and toggle-native map foundation',shader,1),
    ]
    controls += [
        ('captured-structural-coverage-missing','or "CR_INT_Stone_Int_Wall_01"','or "Unknown_CapturedWall"','captured structural definition is prepared and leased',source,1),
        ('native-foundation-boundary-ignored','|| StructuralBoundary(nodeName)','/* injected source-template boundary bypass */','same audited structural mesh under a foundation cap or doorway template stays native',source,1),
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
    variants=[('production',source,geometry,shader,'')]
    if not args.production_only:
        for name,before,after,expected,text,count in controls:
            assert text.count(before)==count, 'mutation binding drift: '+name
            values=[source,geometry,shader]
            values[values.index(text)]=text.replace(before,after)
            variants.append((name,*values,expected))
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
    inputs=paths+[Path(__file__).resolve(),Path(native['nativeBundle']),
        root/'tools/environment-mesh/export-native.py',
        root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/index.json']
    inputs+=sorted(p for p in fixture.rglob('*') if p.is_file())
    inputs+=[Path(entry[k]) for entry in native['entries'] for k in ('exactPath','coarsePath')]
    input_hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    manifest={'result':str(run/'results.txt'),'cases':[]}
    for name,src,geo,shade,expected in variants:
        case=run/name; production=case/'production'; production.mkdir(parents=True)
        pose_call='_proxyTransform.SetPositionAndRotation(_sourceTransform.position, _sourceTransform.rotation);'
        assert geo.count(pose_call)==1, 'actual production private pose observer binding drift'
        observed_geometry=geo.replace(pose_call,
            'TerrainWriteObserver.SetPositionAndRotation(_proxyTransform, _sourceTransform.position, _sourceTransform.rotation);')
        (production/'Terrain.cs').write_text(src); (production/'Geometry.cs').write_text(observed_geometry)
        (production/'MeshStream.cs').write_text(paths[3].read_text())
        shutil.copyfile(fixture/'NativeCoverage.cs',production/'NativeCoverage.cs')
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
