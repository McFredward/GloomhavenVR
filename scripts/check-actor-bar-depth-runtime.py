#!/usr/bin/env python3
"""Execute exact bar depth methods against the shipped TMP runtime and actual Unity pixels.

The native widget's Image/TMP/submesh material APIs are real; test rectangles replace
artwork/font tessellation only. This tests whole-widget depth and lifecycle, not HMD imagery.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]

def method(source,signature):
    assert source.count(signature)==1, 'Production binding drift: '+signature
    start=source.index(signature); end=source.index('{',start)+1; depth=1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]

def native_shader_depth_contract(root,run):
    """Cross-check real shipped TMP pass depth, not the fixture shader's aliases."""
    python=Path.home()/'unitypy-venv/bin/python'
    code=r'''import UnityPy,hashlib,json,sys
from pathlib import Path
asset=Path(sys.argv[1]); env=UnityPy.load(str(asset)); wanted={"TextMeshPro/Distance Field","TextMeshPro/Mobile/Distance Field","TextMeshPro/Sprite"}; records=[]
for obj in env.objects:
 if obj.type.name!="Shader": continue
 data=obj.read_typetree(); pf=data.get("m_ParsedForm",{}); name=pf.get("m_Name","")
 if name not in wanted: continue
 passes=[p["m_State"] for s in pf["m_SubShaders"] for p in s["m_Passes"]]
 assert all(p["zTest"]["name"]=="unity_GUIZTestMode" and p["zWrite"]["val"]==0 for p in passes), name+" native depth binding differs from fixture contract"
 records.append({"shader":name,"path_id":obj.path_id,"depth":[{"zTest":p["zTest"],"zWrite":p["zWrite"]} for p in passes]})
assert {r["shader"] for r in records}==wanted, "Required original TMP font/sprite shaders missing"
with asset.open("rb") as stream: digest=hashlib.file_digest(stream,"sha256").hexdigest()
Path(sys.argv[2]).write_text(json.dumps({"asset":str(asset),"sha256":digest,"original_shader_passes":records,"limits":"Metadata cross-check only: real managed TMP/UI and fixture clip/depth pixels execute; original Windows TMP bytecode, font atlas and glyph tessellation do not."},indent=2)+"\n")
'''
    result=subprocess.run([str(python),'-c',code,str(root/'ressources/GH_Data/resources.assets'),str(run/'native-tmp-depth-provenance.json')],capture_output=True,text=True)
    if result.returncode:raise SystemExit(result.stdout+result.stderr)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/actor-bar-depth-runtime')
    parser.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--case',action='append')
    args=parser.parse_args();root=args.source_root
    bars=(root/'src/GloomhavenVR/WorldUI/ActorBars.cs').read_text()
    code='using System.Collections.Generic; using TMPro; using UnityEngine; using UnityEngine.UI; using UnityEngine.Rendering; using GloomhavenVR.Core; using Object=UnityEngine.Object;\nnamespace GloomhavenVR.WorldUI { internal static partial class ActorBars {\n'
    code+='\n'.join(method(bars,s) for s in ('private static void ScanBarGraphics(', 'private static void AssignBarMaterial(', 'private static void RestoreBarDepthTest('))+'\n}}\n'
    read=(root/'src/GloomhavenVR/WorldUI/Sharpness/PanelGraphicMaterial.cs').read_text()
    variants=[('production',code,'')]
    changes=[
      ('generic-tmp-draw-field','else if (graphic is TMP_Text text) text.fontSharedMaterial = material;','else if (graphic is TMP_Text) graphic.material = material;','through-wall mode renders native circle, actual TMP number and TMP inline symbol together'),
      ('inline-symbol-untreated','if (graphic is TMP_SubMeshUI submesh) submesh.sharedMaterial = material;','if (graphic is TMP_SubMeshUI) return;','through-wall mode renders native circle, actual TMP number and TMP inline symbol together'),
      ('native-replacement-stale','if (ReferenceEquals(src, adopted.DepthMats[existing].inst))','if (bool.Parse("true"))','native font replacement is immediately re-adopted on the next scan'),
      ('restore-stomps-native','if (ReferenceEquals(PanelGraphicMaterial.Read(g), inst)) AssignBarMaterial(g, orig);','AssignBarMaterial(g, orig);','restore uses original exact native font binding and preserves later foreign replacement'),
    ]
    for name,before,after,expected in changes:
        assert code.count(before)==1, 'Mutation binding drift: '+name
        variants.append((name,code.replace(before,after),expected))
    if args.case:
        unknown=set(args.case)-{v[0] for v in variants}
        if unknown:parser.error('Unknown cases: '+str(unknown))
        variants=[v for v in variants if v[0] in args.case]
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    native_shader_depth_contract(root,run)
    managed=root/'ressources/GH_Data/Managed'; fixture=ROOT/'scripts/actor-bar-depth-runtime';dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    manifest={'result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
    hashes={str(root/'src/GloomhavenVR/WorldUI/ActorBars.cs'):hashlib.sha256(bars.encode()).hexdigest(),str(root/'src/GloomhavenVR/WorldUI/Sharpness/PanelGraphicMaterial.cs'):hashlib.sha256(read.encode()).hexdigest()}
    for name in ('Unity.TextMeshPro.dll','UnityEngine.UI.dll'):
        hashes[str(managed/name)]=hashlib.file_digest((managed/name).open('rb'),'sha256').hexdigest()
    (run/'source-hashes.json').write_text(json.dumps({'sha256':hashes,'coverage':'partial' if args.case else 'production-and-negative-controls','limits':['Original game TMP/UI assemblies execute unchanged in Unity 2021.3.5.','Native font/glyph artwork and Windows TMP shader bytecode are replaced by test rectangles and an explicit depth/stencil fixture shader; materialForRendering, depth, cloning and world-space canvas pixels are real. Original TMP pass metadata cross-checks the exact unity_GUIZTestMode binding.','No game scene, headset, multiplayer picture or frame rate is asserted.']},indent=2)+'\n')
    for name,value,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        (production/'ActorBarDepth.cs').write_text(value);(production/'PanelGraphicMaterial.cs').write_text(read)
        project=build/'Depth.csproj';shutil.copyfile(fixture/'Depth.csproj',project);assembly='BarDepth_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed'),'-p:GameManaged='+str(managed.resolve())],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a successful defect control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    project=run/'unity'
    for name in ('Assets/Editor','Assets/Plugins','Packages','ProjectSettings'):(project/name).mkdir(parents=True)
    shutil.copyfile(fixture/'Editor/DepthRunner.cs',project/'Assets/Editor/DepthRunner.cs');shutil.copyfile(fixture/'NativeUi.shader',project/'Assets/NativeUi.shader')
    for name in ('Unity.TextMeshPro.dll','UnityEngine.UI.dll'):shutil.copyfile(managed/name,project/'Assets/Plugins'/name)
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.ui":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','DepthRunner.Start','-depthManifest',str(path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
    try:result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    finally:
        for cache in ('Library','Temp'):shutil.rmtree(project/cache,ignore_errors=True)
    report=Path(manifest['result'])
    if report.is_file():print(report.read_text(),end='')
    if result.returncode or not report.is_file():raise SystemExit('FAIL Unity: '+str(run/'unity.log'))
    print(('PARTIAL PASS' if args.case else 'PASS')+': '+str(len(variants))+' native TMP/UI runtime/pixel variants; '+str(run))
if __name__=='__main__':main()
