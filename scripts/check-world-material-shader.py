#!/usr/bin/env python3
"""Render source-bound world-material stages with pinned Unity GL.

Native DXBC identities/program hashes are verified separately before rendering.
The reference is an explicit reconstructed parameter branch, not native Windows
shader execution, native-game lighting, HMD visual parity or a speed benchmark.
"""
import argparse, hashlib, json, os, re, shutil, subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
SHADER=Path('unity/GloomhavenVR.Assets/Assets/Bundle/Environments/WorldSimpleMaterial.shader')
FIXTURE=Path('tests/world-material-shader')

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def replace(source,before,after,count=1):
    assert source.count(before)==count,'Source binding drift: '+before
    return source.replace(before,after)

def noise_probe(source,name):
    include=source.split('    CGINCLUDE\n',1)[1].split('    ENDCG',1)[0]
    return 'Shader "'+name+'" { Properties { _SamplePosition ("Bounded sample",Vector)=(0,0,0,0) } SubShader { Pass { Cull Back CGPROGRAM\n#pragma target 3.0\n#pragma vertex probeVert\n#pragma fragment probeFrag\n'+include+'''\nfloat4 _SamplePosition;
float4 probeVert(float4 vertex:POSITION):SV_POSITION { return UnityObjectToClipPos(vertex); }
float4 probeFrag():SV_Target { return float4(.5+.5*42.*NativeSimplex(_SamplePosition.xyz*float3(6.,7.,10.)),0,0,1); }
ENDCG\n} } Fallback Off }\n'''

def caster_probe(source,name):
    properties=source.split('    Properties\n',1)[1].split('    CGINCLUDE',1)[0]
    include=source.split('    CGINCLUDE\n',1)[1].split('    ENDCG',1)[0]
    program=source.split('            #pragma vertex shadowVert\n',1)[1].split('            ENDCG',1)[0]
    program=replace(program,'SHADOW_CASTER_FRAGMENT(i)','return float4(0,1,0,1);')
    return 'Shader "'+name+'" { Properties\n'+properties+'CGINCLUDE\n'+include+'ENDCG\nSubShader { Pass { Cull Back ZWrite On ZTest LEqual CGPROGRAM\n#pragma target 3.0\n#pragma vertex shadowVert\n'+program+'\nENDCG\n} } Fallback Off }\n'

def high_probe(source,name):
    source=replace(source,'Shader "GloomhavenVR/WorldSimpleMaterial"','Shader "'+name+'"')
    source=replace(source,'        _MainTex (','        _NativeNoise ("Native DXBC sample boundary", Float) = .003\n        _MainTex (')
    source=replace(source,'    int ToggleWallFade;','    int ToggleWallFade; float _NativeNoise;')
    return replace(source,'NativeSimplex((i.world + _Time.y * float3(.02, -.04, .006)) * float3(6., 7., 10.))','_NativeNoise')

def variants(source):
    yield 'production',source,''
    mutations=[
        ('high-maintex-st','v.uv * _texcoord_ST.xy + _texcoord_ST.zw','v.uv * _MainTex_ST.xy + _MainTex_ST.zw','native albedo/UV/tint/desaturation/dim',2),
        ('high-uv-order','highUv * _UVTiling + _UV_Offset','(v.uv * _UVTiling + _UV_Offset) * _texcoord_ST.xy + _texcoord_ST.zw','native albedo/UV/tint/desaturation/dim',2),
        ('world-axis','float2(p.z * direction.x, p.y)','float2(p.y * direction.x, p.z)','native albedo/UV/tint/desaturation/dim',1),
        ('world-z-sign','float2(-p.x * direction.z, p.y)','float2(p.x * direction.z, p.y)','native albedo/UV/tint/desaturation/dim',1),
        ('world-weight-exponent','pow(abs(n), _WorldSpace_FallOff)','pow(abs(n), _WorldSpace_FallOff * 4.)','native albedo/UV/tint/desaturation/dim',1),
        ('low-world-scale','LowRoute() ? _MainTex_ST.x : _WorldSpace_tiling','LowRoute() ? _MainTex_ST.y : _WorldSpace_tiling','native albedo/UV/tint/desaturation/dim',1),
        ('high-dim-multiplier','dot(color,float3(.299,.587,.115)) * _DimmFactor','dot(color,float3(.299,.587,.115)) * (1. - _DimmFactor)','native albedo/UV/tint/desaturation/dim',1),
        ('low-desaturation-always','#if defined(_DESATURATION_ON)','#if 1','native albedo/UV/tint/desaturation/dim',1),
        ('low-alpha-cutoff','clip(albedo.a - _Cutout);','clip(albedo.a - _Cutoff);','native alpha threshold',1),
        ('low-basic-regular-alpha','clip(_Cutout - albedo.a);','clip(albedo.a - _Cutout);','native albedo/UV/tint/desaturation/dim',1),
        ('tint-alpha-opacity','float3 color = albedo.rgb * _Tint.rgb;','float3 color = albedo.rgb * _Tint.rgb * _Tint.a;','native albedo/UV/tint/desaturation/dim',1),
        ('slot-tint-lost','albedo.rgb * _Tint.rgb','albedo.rgb * _Color.rgb','actual material-slot MPB pixels',1),
        ('slot-cutoff-lost','clip(saturate(wall * alpha) - _Cutoff);','clip(saturate(wall * alpha) - .5);','actual material-slot MPB authored cutoffs',1),
        ('rear-culling-removed','Cull Back ZWrite On ZTest LEqual','Cull Off ZWrite On ZTest LEqual','native Back culling',2),
        ('depth-write-removed','Cull Back ZWrite On ZTest LEqual','Cull Back ZWrite Off ZTest LEqual','native depth-writing geometry',2),
        ('fog-removed','UNITY_APPLY_FOG(i.fogCoord,result);','/* injected: fog omitted */','native Unity fog boundary',1),
        ('low-map-enable-lost','occlusion = 1. + _EnableOcclusionMap * (occlusion - 1.);','occlusion = occlusion;','live native global toggle/map-scale',1),
        ('low-wall-threshold','_GHVRWorldNativeRoute == 2. ? _Cutout : _Cutoff','_Cutoff','native continuous wall/alpha coverage',1),
        ('high-independent-alpha','clip(saturate(wall * alpha) - _Cutoff);','clip(saturate(wall) - _Cutoff); clip(alpha - _Cutoff);','native continuous wall/alpha coverage',1),
        ('low-object-height','if (low && i.world.y < .4)','if (low && 0. < .4)','native continuous wall/alpha coverage',1),
        ('map-depth-ignored','occlusion.a >= depth ? 1. : 1. - occlusion.r','1.','native continuous wall/alpha coverage',1),
        ('simplex-zero','return dot(m*m, float4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));','return 0.;','production simplex',1),
        ('radical-still-lit','if (_GHVRWorldMaterialMode < 1.5)','if (_GHVRWorldMaterialMode < 2.5)','actual material-slot MPB pixels',1),
        ('caster-main-alpha','if (_GHVRWorldNativeRoute == 5.) clip(1. - _Cutoff);','if (_GHVRWorldNativeRoute < 9.) clip(tex2D(_MainTex,i.uv).a - _Cutoff);\n                if (_GHVRWorldNativeRoute == 5.) clip(1. - _Cutoff);','audited native AMP/fallback caster',1),
    ]
    for name,before,after,expected,count in mutations:
        yield name,replace(source,before,after,count),expected
    frozen=replace(source,'|| ToggleWallFade == 0','|| false')
    frozen=replace(frozen,'1. + ToggleWallFade * (A * B - 1.)','A * B')
    yield 'toggle-frozen',frozen,'native albedo/UV/tint/desaturation/dim'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/world-material-shader/runtime')
    parser.add_argument('--unity',type=Path,default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--production-only',action='store_true')
    parser.add_argument('--case',action='append')
    parser.add_argument('--skip-native',action='store_true',help='Partial development only; omit addressed DXBC digest verification')
    args=parser.parse_args();root=args.source_root.resolve();run=args.output_dir.resolve();run.mkdir(parents=True,exist_ok=True)
    inputs=[root/SHADER,root/FIXTURE/'NativeReference.shader',root/FIXTURE/'Editor/WorldMaterialRunner.cs',root/FIXTURE/'extract-native.py',root/FIXTURE/'native-contract.json',root/'tests/terrain-budget-runtime/native-noise-vectors.json',Path(__file__).resolve()]
    hashes={str(p.relative_to(root)):sha(p) for p in inputs if p.exists()}
    if not args.skip_native:
        result=subprocess.run(['/home/claw/unitypy-venv/bin/python',str(root/FIXTURE/'extract-native.py'),'--source-root',str(root),'--output-dir',str(run/'native')],capture_output=True,text=True)
        (run/'native-extract.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit('Addressed native extraction failed; see '+str(run/'native-extract.log'))
        actual=json.loads((run/'native/native-programs.json').read_text()); contract=json.loads((root/FIXTURE/'native-contract.json').read_text())
        keyed={row['identity']:row for row in actual}
        for pinned in contract['objects']:
            current=keyed[pinned['identity']]
            assert all(current[k]==pinned[k] for k in ('route','rawSha256','sourceSha256')),'Native object/source changed: '+pinned['identity']
            programs={p['file']:p for p in current['programs']}
            for bounded in pinned['programs']:
                p=programs[bounded['file']]
                assert all(p[k]==bounded[k] for k in ('dxbcSha256','tailSha256','offset','segment','length','tailOffset','keywords','pass','stage')),'Addressed native program changed: '+bounded['file']
        for pair in contract['crossVariantProgramEquivalences']:
            nativeRoot=next(p for p in keyed[pair['rootIdentity']]['programs']if p['file']==pair['rootProgram'])
            nativeBundle=next(p for p in keyed[pair['bundleIdentity']]['programs']if p['file']==pair['bundleProgram'])
            assert nativeRoot['dxbcSha256']==nativeBundle['dxbcSha256']==pair['dxbcSha256'],'Same-name native core bytecode differs: '+pair['rootProgram']
        for route,effects in contract['effectKeywordIntersections'].items():
            sets=[{tuple(sorted(k for k in p['keywords']if k not in ('DIRECTIONAL','LIGHTPROBE_SH')))for p in o['programs']if p['stage']=='progFragment'and p['pass']==0}for o in actual if o['route']==int(route)]
            assert [list(k)for k in sorted(set.intersection(*sets))]==effects,'Native compiled effect intersection changed: '+route
        (run/'native-verified.json').write_text(json.dumps({'objects':len(contract['objects']),'programs':sum(len(o['programs'])for o in contract['objects']),'contractSha256':sha(root/FIXTURE/'native-contract.json'),'limits':contract['limits']},indent=2)+'\n')
    project=run/'unity-project'; assets=project/'Assets'; assets.mkdir(parents=True,exist_ok=True); (assets/'Editor').mkdir(exist_ok=True)
    (project/'ProjectSettings').mkdir(exist_ok=True);(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (project/'Packages').mkdir(exist_ok=True);(project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.imgui":"1.0.0"}}\n')
    shutil.copy2(root/FIXTURE/'NativeReference.shader',assets/'NativeReference.shader');shutil.copy2(root/FIXTURE/'Editor/WorldMaterialRunner.cs',assets/'Editor/WorldMaterialRunner.cs')
    cases=[];source=(root/SHADER).read_text()
    for index,(name,variant,expected) in enumerate(variants(source)):
        if args.production_only and name!='production':continue
        if args.case and name not in args.case:continue
        shader='GloomhavenVR/WorldSimpleMaterial' if name=='production' else 'Fixture/World-'+name
        if name!='production':variant=replace(variant,'Shader "GloomhavenVR/WorldSimpleMaterial"','Shader "'+shader+'"')
        high='Fixture/High-'+name;noise='Fixture/Noise-'+name;caster='Fixture/Caster-'+name
        (assets/(name+'.shader')).write_text(variant)
        # Bind probes to the same complete production/mutated text; do not paste a
        # separate simplified implementation into the actual rendered subject.
        canonical=replace(variant,'Shader "'+shader+'"','Shader "GloomhavenVR/WorldSimpleMaterial"')
        (assets/(name+'-high.shader')).write_text(high_probe(canonical,high));(assets/(name+'-noise.shader')).write_text(noise_probe(canonical,noise));(assets/(name+'-caster.shader')).write_text(caster_probe(canonical,caster))
        cases.append(dict(name=name,shader=shader,high=high,noise=noise,caster=caster,expected=expected))
    assert cases,'No selected cases'
    manifest=run/'manifest.json';manifest.write_text(json.dumps({'result':str(run/'result.txt'),'evidence':str(run),'cases':cases},indent=2)+'\n')
    (run/'source-binding.json').write_text(json.dumps({'inputs':hashes,'nativeVerified':not args.skip_native,'partial':bool(args.case or args.production_only or args.skip_native),'cases':cases,'generated':{str(p.relative_to(project)):sha(p)for p in assets.rglob('*')if p.is_file()}},indent=2)+'\n')
    env=os.environ.copy();env['GHVR_WORLD_NOISE_VECTORS']=str(root/'tests/terrain-budget-runtime/native-noise-vectors.json')
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','WorldMaterialRunner.Run','-worldMaterialManifest',str(manifest),'-logFile',str(run/'Unity.log')]
    if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
    try: completed=subprocess.run(command,capture_output=True,text=True,env=env,timeout=1200)
    except subprocess.TimeoutExpired as error:
        (run/'launcher.log').write_text(str(error));raise SystemExit('Unity fixture timed out; full output retained')
    (run/'launcher.log').write_text(completed.stdout+completed.stderr)
    if (run/'result.txt').exists():print((run/'result.txt').read_text(),end='')
    else:print('Unity produced no results; see '+str(run/'Unity.log'))
    log=(run/'Unity.log').read_text() if(run/'Unity.log').exists()else''
    assert not re.search(r'error CS\d+|Shader error in',log),'Shader/C# compile failure cannot pass a causal control'
    assert all(sha(root/p)==value for p,value in hashes.items()),'Fixture inputs changed while rendering'
    if completed.returncode:raise SystemExit(completed.returncode)
    print('Evidence: '+str(run))
if __name__=='__main__':main()
