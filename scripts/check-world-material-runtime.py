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
    combined='\n'.join(sources.values())
    assert 'StaticBatchingUtility' not in combined and 'SetStaticBatchInfo' not in combined
    assert 'new Mesh' not in combined and 'SetPropertyBlock(' not in combined and 'forceRenderingOff =' not in combined
    assert 'Camera.onPreCull +=' not in combined,'commit must use the final native boundary'
    changes=[
        ('mode-ignored',[('WorldMaterialBudget.Materials.cs','variant.SetFloat("_GHVRWorldMaterialMode", _mode);','variant.SetFloat("_GHVRWorldMaterialMode", 1);',1)],'actual private material binds independently requested mode'),
        ('native-material-shader-mutated',[('WorldMaterialBudget.Materials.cs','variant.shader = _shader;','original.shader = _shader;',2)],'private world shader never mutates original native material'),
        ('native-copy-omitted',[('WorldMaterialBudget.Materials.cs','variant.CopyPropertiesFromMaterial(original);','/* injected stale material properties */',1)],'between-eye native in-place material edits'),
        ('material-pass-cross-eye-stale',[('WorldMaterialBudget.cs','_prepared.Clear();','/* injected cross-eye reuse */',3)],'between-eye native in-place material edits'),
        ('scope-cross-eye-stale',[('WorldMaterialBudget.cs','_scopes.Clear();','/* injected cross-eye scope verdict */',3)],'current added native interaction between eyes'),
        ('foreign-slot-overwritten',[('WorldMaterialBudget.cs','original = Canonical(current);','original = Source(_slots[0]);',1)],'conditional restoration preserves same-count foreign slot replacement'),
        ('native-clone-boundary-missing',[('WorldMaterialBudget.cs','internal static void BeforeNativeContentChange() => _driver?.RestoreBindings();','internal static void BeforeNativeContentChange() { }',1)],'native content boundary restores original slot composition'),
        ('proxy-only-refusal-not-notified',[('WorldMaterialBudget.cs','if (restored || geometryChanged || !surface.Refused) _changedSources.Add(renderer);','if (restored) _changedSources.Add(renderer);',1)],'late native scope refusal revokes an earlier proxy-only variant'),
        ('final-boundary-lost',[
            ('WorldMaterialBudget.cs','ScenarioCameraCullBoundary.Subscribe(_driver.PreCull);','Camera.onPreCull += _driver.PreCull;',1),
            ('WorldMaterialBudget.cs','ScenarioCameraCullBoundary.Unsubscribe(_driver.PreCull);','Camera.onPreCull -= _driver.PreCull;',1),
            ('WorldMaterialBudget.cs','ScenarioCameraCullBoundary.Unsubscribe(PreCull);','Camera.onPreCull -= PreCull;',1)],'final native cull boundary sees late material edits'),
        ('vertex-effect-veto-missing',[('WorldMaterialBudget.Materials.cs','{ "_AddVertexAnim",','{ "_UnusedVertexFlag",',1)],'live original effect retains native material family: _AddVertexAnim'),
        ('consumer-disposal-not-called',[('WorldMaterialBudget.cs','_beforeVariantDisposal?.Invoke();','/* injected dangling consumer */',1)],'stage zero releases factory-only render consumers'),
        ('asset-preparation-not-called',[('WorldMaterialBudget.Materials.cs','if (_ensureAssets?.Invoke() == false)','if (bool.Parse("false"))',1)],'cold asset preparation refusal keeps native source materials'),
        ('native-render-tag-dropped',[('WorldMaterialBudget.Materials.cs','variant.SetOverrideTag("RenderType", original.GetTag("RenderType", false, ""));','/* injected replacement render route loss */',1)],'native replacement-camera render type and queue'),
        ('native-tile-required-generator-vetoed',[('WorldMaterialBudget.Scope.cs','&& component is not ProceduralStyle','&& component is not UnknownNativeAnimation',1)],'requested world stage produces a private variant'),
        ('animated-style-veto-lost',[('WorldMaterialBudget.Scope.cs','if (component is ProceduralStyle style && style.AnimateStyle) allowed = false;','/* injected animated native style admission */',1)],'live native animated style retains original rendering'),
        ('held-source-veto-lost',[('WorldMaterialBudget.Scope.cs','if (HeldProps.OwnsRendererOf(renderer.transform) || PropGrab.OwnsRendererOf(renderer.transform)) return false;','/* injected held source admission */',1)],'current held native world prop retains original material ownership'),
        ('current-meshfilter-not-read',[('WorldMaterialBudget.cs','MeshFilter filter = renderer.GetComponent<MeshFilter>();\n                    Mesh mesh','MeshFilter filter = surface.Filter!;\n                    Mesh mesh',1)],'replacement native mesh filter is read again and can regain safe world shading'),
        ('native-mesh-reference-ignored',[('WorldMaterialBudget.cs',' || surface.Mesh != mesh','',1)],'live native mesh-reference swap notifies earlier consumers'),
        ('empty-slots-not-refused',[('WorldMaterialBudget.cs','refused = _slots.Count == 0;','refused = false;',1)],'empty native material slots revoke an earlier factory-only draw'),
        ('joint-program-contract-unrestricted',[('WorldMaterialBudget.Materials.cs','return ProvenProgram(original, route) ? route : -1;','return route;',1)],'unproven native worldspace alpha program combination retains original shader'),
        ('standard-mpb-render-mode-unchecked',[('WorldMaterialBudget.Materials.cs','if (route == 9)\n                foreach','if (route == 10)\n                foreach',1)],'native MPB blend mode override remains original'),
        ('native-map-provenance-missing',[('WorldMaterialBudget.cs','if (map.worldMap != null) _worldRoots.Add(map.worldMap.transform);','/* injected missing native map scope */',1)],'actual native MapChoreographer worldMap field establishes positive map decoration scope'),
        ('disabled-native-pass-admitted',[('WorldMaterialBudget.Materials.cs',' || !NativePassesEnabled(original)','',1)],'live disabled native material pass remains original: FORWARD'),
        ('disabled-native-fallback-caster-admitted',[('WorldMaterialBudget.Materials.cs','return original.GetShaderPassEnabled("ShadowCaster") && original.GetShaderPassEnabled("CUSTOM_SHADOW_PASS");','return original.GetShaderPassEnabled("CUSTOM_SHADOW_PASS");',1)],'live disabled native material pass remains original: ShadowCaster'),
        ('disabled-native-low-caster-admitted',[('WorldMaterialBudget.Materials.cs','return original.GetShaderPassEnabled("ShadowCaster") && original.GetShaderPassEnabled("CUSTOM_SHADOW_PASS");','return original.GetShaderPassEnabled("ShadowCaster");',1)],'live disabled native material pass remains original: CUSTOM_SHADOW_PASS'),
        ('video-mpb-admitted',[('WorldMaterialBudget.Materials.cs','if (_block.HasTexture("_MainTex") && _block.GetTexture("_MainTex") is RenderTexture\n                || _slotBlock.HasTexture("_MainTex") && _slotBlock.GetTexture("_MainTex") is RenderTexture) return true;','/* injected animated texture admission */',1)],'current per-slot native video/render texture remains original'),
    ]
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
    from integration_bindings import verify
    bridge_paths,bridge_report=verify((args.integration_root or root).resolve())
    (run/'integration-bindings.json').write_text(json.dumps(bridge_report,indent=2)+'\n')
    deps=[Path.home()/'.nuget/packages'/relative for relative in (
        'harmonyx/2.7.0/lib/net45/0Harmony.dll','monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll',
        'monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll','mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll')]
    assert all(p.is_file() for p in deps),'pinned production HarmonyX dependencies must exist'
    inputs=paths+bridge_paths+[Path(__file__).resolve()]+sorted(p for p in fixture.rglob('*') if p.is_file() and '__pycache__' not in p.parts)+deps
    hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    manifest={'result':str(run/'results.txt'),'cases':[]}
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name,texts,expected in variants:
        case=run/name;production=case/'production';production.mkdir(parents=True)
        for filename,text in texts.items():
            if filename=='WorldMaterialBudget.Materials.cs':
                text=text.replace('variant.CopyPropertiesFromMaterial(original);','NativeWriteObserver.Copy(variant, original);')
            if filename=='WorldMaterialBudget.cs':
                text=text.replace('renderer.sharedMaterials = _slots.ToArray();','NativeWriteObserver.Slots(renderer, _slots.ToArray());')
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
    for dependency in deps:shutil.copyfile(dependency,unity/'Assets'/dependency.name)
    (unity/'Packages/manifest.json').write_text('{"dependencies":{}}\n');(unity/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (run/'source-hashes.json').write_text(json.dumps({'sha256':hashes,'coverage':'partial' if args.production_only or args.case else 'production-and-negative-controls',
        'cases':[v[0] for v in variants],'limits':['Native scene/controllers/config are explicit surrogate boundaries.',
        'Fixture bridge shader proves ownership/delivery pixels, not production world shading; separate shader lane proves native fragment routes.',
        'Actual Unity material/MPB/cloning/renderer camera calls and production final Harmony postfix execute.',
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
