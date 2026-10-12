#!/usr/bin/env python3
"""Execute exact bar depth methods against the shipped TMP runtime and actual Unity pixels.

Legacy Image/TMP/submesh binding controls use rectangles. Native font controls use
original game glyph metrics, atlas and sprite submeshes with exact upstream native
shader source and its derivative. Actual pixels and lifecycle are checked, not HMD imagery.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import sys
import atexit
import signal
import re

from town_run_retention import cleanup_completed_run

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
Path(sys.argv[2]).write_text(json.dumps({"asset":str(asset),"sha256":digest,"original_shader_passes":records,"limits":"This file checks original game Shader metadata only. Separate native-font controls execute original atlases/glyph/sprite data, shipped managed TMP/UI and canonical source shader pixels; original game Windows shader bytecode is not the reference executable."},indent=2)+"\n")
'''
    result=subprocess.run([str(python),'-c',code,str(root/'ressources/GH_Data/resources.assets'),str(run/'native-tmp-depth-provenance.json')],capture_output=True,text=True)
    if result.returncode:raise SystemExit(result.stdout+result.stderr)

def native_font_inputs(root,run):
    """Extract serialized glyph metrics, original atlas and sprite data from the game."""
    code=r'''import UnityPy,json,sys,hashlib
from pathlib import Path
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
root,out=map(Path,sys.argv[1:]);asset=root/'ressources/GH_Data/resources.assets'
env=UnityPy.load(str(asset));generator=TypeTreeGenerator('2021.3.5f1');generator.load_local_game(str(root/'ressources'));env.typetree_generator=generator
objects={o.path_id:o for o in env.objects}
records=[]
for path_id,prefix,atlas_key in [(10356,'native-font','m_AtlasTextures'),(10387,'native-sprite','spriteSheet')]:
 data=objects[path_id].read_typetree();atlas_ref=data[atlas_key][0] if isinstance(data[atlas_key],list) else data[atlas_key]
 atlas=objects[atlas_ref['m_PathID']].read();atlas.image.save(out/(prefix+'-atlas.png'))
 links={'m_Script','m_GameObject','material','spriteSheet','m_SourceFontFile','m_AtlasTextures','m_SourceFontFile_EditorRef','fallbackFontAssetTable','m_FallbackFontAssetTable'}
 clean={k:v for k,v in data.items() if k not in links}
 (out/(prefix+'.json')).write_text(json.dumps(clean,indent=2)+'\n')
 records.append({'name':data['m_Name'],'path_id':path_id,'atlas_path_id':atlas_ref['m_PathID'],'glyph_count':len(data.get('m_GlyphTable',data.get('m_SpriteGlyphTable',[]))),'atlas_png_sha256':hashlib.sha256((out/(prefix+'-atlas.png')).read_bytes()).hexdigest(),'serialized_json_sha256':hashlib.sha256((out/(prefix+'.json')).read_bytes()).hexdigest()})
material=objects[202].read_typetree();saved=material['m_SavedProperties'];native_material={'name':material['m_Name'],'shader_path_id':material['m_Shader']['m_PathID'],'keywords':material['m_ValidKeywords'],'renderQueue':material['m_CustomRenderQueue'],'floats':[{'name':n,'value':v} for n,v in saved['m_Floats']],'colors':[{'name':n,'value':v} for n,v in saved['m_Colors']],'textures':[{'name':n,'path_id':v['m_Texture']['m_PathID'],'scale':v['m_Scale'],'offset':v['m_Offset']} for n,v in saved['m_TexEnvs']]}
(out/'native-font-material.json').write_text(json.dumps(native_material,indent=2)+'\n')
records.append({'font_material_path_id':202,'shader_path_id':native_material['shader_path_id'],'material_json_sha256':hashlib.sha256((out/'native-font-material.json').read_bytes()).hexdigest()})
(out/'native-font-provenance.json').write_text(json.dumps({'game_asset':str(asset),'game_asset_sha256':hashlib.file_digest(asset.open('rb'),'sha256').hexdigest(),'originals':records,'limits':'Original game serialized glyph metrics/characters and atlas execute with shipped managed TMP/UI. The native Windows shader program is compared to its matching upstream1.3.0 program properties; derivative and original canonical source programs are rasterized, not native game Shader objects.'},indent=2)+'\n')
'''
    result=subprocess.run([str(Path.home()/'unitypy-venv/bin/python'),'-c',code,str(root),str(run)],capture_output=True,text=True)
    (run/'native-font-export.log').write_text(result.stdout+result.stderr)
    if result.returncode:raise SystemExit(result.stdout+result.stderr)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/actor-bar-depth-runtime')
    parser.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--case',action='append')
    parser.add_argument('--depth-debug',action='store_true')
    parser.add_argument('--native-font-only',action='store_true')
    parser.add_argument('--skip-viewport',action='store_true')
    parser.add_argument('--graphics-api',choices=('glcore','vulkan'),default='glcore')
    args=parser.parse_args();root=args.source_root
    bars=(root/'src/GloomhavenVR/WorldUI/ActorBars.cs').read_text()
    code='using System.Collections.Generic; using TMPro; using UnityEngine; using UnityEngine.UI; using UnityEngine.Rendering; using GloomhavenVR.Core; using Object=UnityEngine.Object;\nnamespace GloomhavenVR.WorldUI { internal static partial class ActorBars {\n'
    code+='\n'.join(method(bars,s) for s in ('private static void ScanBarGraphics(', 'private static void AssignBarMaterial(', 'private static void RestoreBarDepthTest('))+'\n}}\n'
    read=(root/'src/GloomhavenVR/WorldUI/Sharpness/PanelGraphicMaterial.cs').read_text()
    shaders=(root/'src/GloomhavenVR/WorldUI/ActorBarDepthShaders.cs').read_text()
    foreground=(root/'src/GloomhavenVR/WorldUI/HealthbarForegroundDepth.cs').read_text()
    variants=[('production',code,foreground,'')]
    changes=[
      ('text-native-refresh-ignored','text.OnPreRenderText += callback;','/* native refresh omitted */','native inline sprite receives its own complete foreground-aware material'),
      ('generic-tmp-draw-field','else if (graphic is TMP_Text text) text.fontSharedMaterial = material;','else if (graphic is TMP_Text) graphic.material = material;','through-wall mode renders native circle, actual TMP number and TMP inline symbol together'),
      ('inline-symbol-untreated','if (graphic is TMP_SubMeshUI submesh) submesh.sharedMaterial = material;','if (graphic is TMP_SubMeshUI) return;','through-wall mode renders native circle, actual TMP number and TMP inline symbol together'),
      ('native-replacement-stale','if (ReferenceEquals(src, adopted.DepthMats[existing].inst))','if (bool.Parse("true"))','native font replacement is immediately re-adopted on the next scan'),
      ('restore-stomps-native','if (ReferenceEquals(PanelGraphicMaterial.Read(g), inst)) AssignBarMaterial(g, orig);','AssignBarMaterial(g, orig);','restore uses original exact native font binding and preserves later foreign replacement'),
    ]
    for name,before,after,expected in changes:
        assert code.count(before)==1, 'Mutation binding drift: '+name
        variants.append((name,code.replace(before,after),foreground,expected))
    more=[
        ('capture-fallback-unsafe','ActorBarDepthShaders.SetCaptureAvailable(false);','/* unsafe Always retained */','capture unavailable keeps own derivative on safe ordinary depth rather than piercing foreground'),
        ('viewport-native-domain-ignored','new Vector4(viewport.x, viewport.y, viewport.width, viewport.height)','new Vector4(0, 0, camera.pixelWidth, camera.pixelHeight)','target-texture viewport matches native full-target card occlusion despite ignored camera rect'),
        ('ignore-solid-foreground','capture.SetGlobalFloat(ReadyId, 1f);','capture.SetGlobalFloat(ReadyId, 0f);','actual nearer solid card geometry hides every covered native glyph pixel'),
        ('world-roots-foreground','name == "GloomhavenVR.Hands" || name == "GloomhavenVR.PlayTray"','name == "GloomhavenVR.Room" || name == "GloomhavenVR.Hands" || name == "GloomhavenVR.PlayTray"','original game atlas and real TMP glyph tessellation render through a wall'),
        ('wrong-duplicate-forward-pass','ForwardPass(material.shader)','0','duplicate FORWARD names select actual depth-writing ForwardBase by LightMode'),
        ('native-hold-ignored','DrawHeld(capture, camera, FigureGrabbable.HeldBy(HandSide.Left)?.HeldRenderers());','DrawHeld(capture, camera, null);','native figure held in the left hand occludes without changing its game layer'),
    ]
    for name,before,after,expected in more:
        assert foreground.count(before)==1,'Mutation binding drift: '+name
        variants.append((name,code,foreground.replace(before,after),expected))
    if args.case:
        unknown=set(args.case)-{v[0] for v in variants}
        if unknown:parser.error('Unknown cases: '+str(unknown))
        variants=[v for v in variants if v[0] in args.case]
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    completion=run/'completion.log';completion.write_text('evidence: '+str(run)+'\n');outcome={'exit_code':1,'reaped':True}
    def completed():
        receipt=cleanup_completed_run('actor-bar-depth-runtime',outcome['exit_code'],completion,root,process_reaped=outcome['reaped'])
        (run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
    atexit.register(completed)
    native_shader_depth_contract(root,run)
    native_font_inputs(root,run)
    if args.native_font_only:(run/'native-font-only.marker').write_text('Focused native font binding only; legacy rectangle controls omitted.\n')
    if args.skip_viewport:(run/'skip-viewport.marker').write_text('Partial font/sprite proof; independently failing viewport mapping check remains pending.\n')
    managed=root/'ressources/GH_Data/Managed'; fixture=ROOT/'scripts/actor-bar-depth-runtime';dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    manifest={'result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
    hashes={str(root/'src/GloomhavenVR/WorldUI/ActorBars.cs'):hashlib.sha256(bars.encode()).hexdigest(),str(root/'src/GloomhavenVR/WorldUI/Sharpness/PanelGraphicMaterial.cs'):hashlib.sha256(read.encode()).hexdigest()}
    for source in (root/'src/GloomhavenVR/WorldUI/ActorBarDepthShaders.cs',root/'src/GloomhavenVR/WorldUI/HealthbarForegroundDepth.cs',root/'scripts/check-actor-bar-depth-runtime.py',root/'scripts/actor-bar-depth-runtime/Depth.csproj',*(root/'scripts/actor-bar-depth-runtime').rglob('*.cs'),*(root/'scripts/actor-bar-depth-runtime').rglob('*.shader'),*(root/'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars').glob('*.shader'),*(root/'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars').glob('*.cginc')):
        hashes[str(source)]=hashlib.file_digest(source.open('rb'),'sha256').hexdigest()
    for name in ('Unity.TextMeshPro.dll','UnityEngine.UI.dll','Newtonsoft.Json.dll'):
        hashes[str(managed/name)]=hashlib.file_digest((managed/name).open('rb'),'sha256').hexdigest()
    (run/'source-hashes.json').write_text(json.dumps({'sha256':hashes,'coverage':'partial' if args.case or args.skip_viewport or args.native_font_only else 'production-and-negative-controls','limits':['Original game TMP/UI assemblies execute unchanged in Unity 2021.3.5.','Legacy binding cases use rectangle tessellation. Added native-font cases execute original game glyph/character metrics, atlas, actual TMP glyph tessellation, full/Mobile original source programs and original Sprite data. Canonical source hashes independently bind the derivative undo to official upstream original source; original Windows game shader bytecode is metadata checked, not used as the font reference shader.','Actual camera pixels/depth/stencil, clone bindings and per-render held lifetime are exercised; there is no game scenario controller, headset, multiplayer picture or hardware performance claim.','Skipped viewport cases remain explicitly partial and are never promoted to a complete pass.']},indent=2)+'\n')
    for name,value,depth,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        (production/'ActorBarDepth.cs').write_text(value);(production/'PanelGraphicMaterial.cs').write_text(read)
        (production/'ActorBarDepthShaders.cs').write_text(shaders);(production/'HealthbarForegroundDepth.cs').write_text(depth)
        project=build/'Depth.csproj';shutil.copyfile(fixture/'Depth.csproj',project);assembly='BarDepth_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed'),'-p:GameManaged='+str(managed.resolve())],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a successful defect control')
        binaries=run/'assemblies';binaries.mkdir(exist_ok=True);dll=binaries/(assembly+'.dll');shutil.copyfile(build/'bin/Release/netstandard2.1'/(assembly+'.dll'),dll)
        manifest['cases'].append({'name':name,'dll':str(dll),'dll_sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),'expected':expected})
    project=run/'unity'
    for name in ('Assets/Editor','Assets/Plugins','Packages','ProjectSettings'):(project/name).mkdir(parents=True)
    shutil.copyfile(fixture/'Editor/DepthRunner.cs',project/'Assets/Editor/DepthRunner.cs')
    shader_dir=project/'Assets/Resources/HealthBars';shader_dir.mkdir(parents=True)
    actual_shader_dir=root/'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars'
    for source in actual_shader_dir.iterdir():
        if source.suffix in ('.shader','.cginc'):shutil.copyfile(source,shader_dir/source.name)
    if args.depth_debug:
        debug=shader_dir/'ActorBarUI.shader';value=debug.read_text();value=value.replace('GHVRActorBarForegroundClip(IN.vertex);','float depth = SAMPLE_DEPTH_TEXTURE(_GHVRActorForegroundDepth,IN.vertex.xy * _GHVRActorForegroundSize.xy); return fixed4(depth, IN.vertex.z * 100, _GHVRActorForegroundReady,1);');debug.write_text(value)
    shutil.copyfile(root/'unity/GloomhavenVR.Assets/Assets/Bundle/Table/BoardLit.shader',project/'Assets/Resources/BoardLit.shader')
    shutil.copyfile(fixture/'DuplicateForward.shader',project/'Assets/Resources/DuplicateForward.shader')
    # Canonical original programs are recovered by undoing only our explicit changes.
    original_dir=project/'Assets/Resources/OriginalShaders';original_dir.mkdir()
    names={'ActorBarUI':'UI/Default','ActorBarSDF':'TextMeshPro/Distance Field','ActorBarMobileSDF':'TextMeshPro/Mobile/Distance Field','ActorBarSprite':'TextMeshPro/Sprite'}
    provenance=json.loads((actual_shader_dir/'source-provenance.json').read_text());canonical=[]
    for file,name in names.items():
        source=(actual_shader_dir/(file+'.shader')).read_text()
        source=source.replace('Shader "GloomhavenVR/'+file+'"','Shader "'+name+'"',1)
        source=source.replace('\n\t\t\t#include "ActorBarForeground.cginc"','',1)
        source=re.sub(r'\n\t\t\tGHVRActorBarForegroundClip\([^;]*\);','',source)
        source=re.sub(r'\n\t\t\tfloat4 ghvrScreenPosition : TEXCOORD7;','',source)
        source=re.sub(r'\n\t\t\tOUT.ghvrScreenPosition = ComputeScreenPos\(OUT.vertex\);','',source)
        source=source.replace('\n\t\t\t\tComputeScreenPos(vPosition),','')
        source=source.replace('Fallback "GloomhavenVR/ActorBarMobileSDF"','Fallback "TextMeshPro/Mobile/Distance Field"')
        record=next(r for r in provenance['sources'] if r['file']==file+'.shader');original=source
        for pragma in record['retained_feature_pragmas']:
            before=pragma.strip().replace('shader_feature','multi_compile');assert original.count(before)==1,'Canonical feature binding drift: '+before
            original=original.replace(before,pragma.strip())
        digest=hashlib.sha256(original.encode()).hexdigest();assert digest==record['normalized_original_sha256'],'Canonical upstream hash differs: '+file
        canonical.append({'shader':file,'verified_official_normalized_source_sha256':digest,'reference_runtime_pragmas':'same original keyword branches retained with multi_compile'})
        (original_dir/(file+'.shader')).write_text(source)
    # Deliberately wrong historical mapping: allocation _ScreenParams does not
    # identify the active GPU eye viewport. Actual subset controls must reject it.
    bad_dir=project/'Assets/Resources/DiagnosticBad';bad_dir.mkdir()
    bad_shader=(actual_shader_dir/'ActorBarMobileSDF.shader').read_text().replace('Shader "GloomhavenVR/ActorBarMobileSDF"','Shader "GloomhavenVR/ActorBarMobileSDF_BadViewport"')
    (bad_dir/'ActorBarMobileSDF.shader').write_text(bad_shader)
    bad_include=(actual_shader_dir/'ActorBarForeground.cginc').read_text().replace('(screenPosition.xy / screenPosition.w) * _GHVRActorForegroundViewport.zw','pixelPosition.xy * (_GHVRActorForegroundViewport.zw / _ScreenParams.xy)')
    (bad_dir/'ActorBarForeground.cginc').write_text(bad_include)
    for file in ('TMPro.cginc','TMPro_Properties.cginc'):shutil.copyfile(actual_shader_dir/file,bad_dir/file)
    (run/'canonical-source-provenance.json').write_text(json.dumps(canonical,indent=2)+'\n')
    for file in ('TMPro.cginc','TMPro_Properties.cginc'):shutil.copyfile(actual_shader_dir/file,original_dir/file)
    for name in ('Unity.TextMeshPro.dll','UnityEngine.UI.dll','Newtonsoft.Json.dll'):shutil.copyfile(managed/name,project/'Assets/Plugins'/name)
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.ui":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
    command=[str(args.unity),'-batchmode','-force-'+args.graphics_api,'-projectPath',str(project),'-executeMethod','DepthRunner.Start','-depthManifest',str(path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
    outcome['reaped']=False
    process=subprocess.Popen(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,start_new_session=True)
    try:
        returncode=process.wait(timeout=240)
    finally:
        # xvfb-run is a wrapper: terminate the entire owned process group, not
        # merely its parent, before permitting cache cleanup on any outcome.
        try:os.killpg(process.pid,signal.SIGKILL)
        except ProcessLookupError:pass
        process.wait()
        outcome['reaped']=True
    report=Path(manifest['result'])
    if report.is_file():print(report.read_text(),end='')
    if returncode or not report.is_file():raise SystemExit('FAIL Unity: '+str(run/'unity.log'))
    outcome['exit_code']=0
    print(('PARTIAL PASS' if args.case or args.skip_viewport or args.native_font_only else 'PASS')+': '+str(len(variants))+' native TMP/UI runtime/pixel variants; '+str(run))
if __name__=='__main__':main()
