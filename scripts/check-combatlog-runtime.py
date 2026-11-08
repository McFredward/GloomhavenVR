#!/usr/bin/env python3
"""Original compact combat-log conversion, real config/placement and final Unity pixels.

Native gameplay/population, gesture sampling, anchor housekeeping, chrome and
non-font Windows fragment programs are explicit ports. Original serialized
layout/font data, affected production consumers and actual engine pixels run.
"""
import argparse,hashlib,importlib.util,json,os,re,shutil,signal,subprocess,sys,tempfile,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run

def method(source,signature):
 assert source.count(signature)==1,signature
 start=source.index(signature);opening=source.index('{',start);depth=1;end=opening+1
 while depth:depth+=(source[end]=='{')-(source[end]=='}');end+=1
 return source[start:end]
def execute(command,log,timeout=180):
 with log.open('w') as output:
  child=subprocess.Popen(command,cwd=ROOT,stdout=output,stderr=subprocess.STDOUT,start_new_session=True)
  try:return child.wait(timeout=timeout)
  finally:
   if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
   child.wait()
def load_module(name,path):
 spec=importlib.util.spec_from_file_location(name,path);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--case',action='append');args=parser.parse_args()
 assert os.environ.get('ADB_SERVER_SOCKET'),'Use the task-private ADB server'
 signal.signal(signal.SIGTERM,lambda n,f:sys.exit(128+n))
 base=ROOT/'src/GloomhavenVR/WorldUI';fixture=ROOT/'scripts/combatlog-runtime';output=ROOT/'.planning/debug/combatlog-runtime';output.mkdir(parents=True,exist_ok=True)
 run=Path(tempfile.mkdtemp(prefix='run-',dir=output));print('Evidence: '+str(run),flush=True);completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n')
 status=1;started=time.monotonic();pins=[]
 try:
  source=(base/'Surfaces/CombatLogSurface.cs').read_text();pins.append(base/'Surfaces/CombatLogSurface.cs')
  methods=[]
  for sig in ['protected override void Place()','private void SyncHostPose(','private void PlaceInView(','private void PersistLayout()']:
   body=method(source,sig).replace('protected override void','private static void',1).replace('private void','private static void',1);methods.append(body)
  center=re.search(r'private Vector3 HostCenter\([\s\S]*?;',source)[0].replace('private Vector3','private static Vector3',1)
  sources={'CombatLogPlacement.cs':'using UnityEngine;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI { internal static partial class CombatLogProjection {\n'+'\n'.join(methods)+'\n'+center+'\n}}',
           'NativeWindowFrame.cs':(base/'Conversion/NativeWindowFrame.cs').read_text()}
  conversion=(base/'Conversion/CanvasConversion.1.Core.cs').read_text();pins.extend([base/'Conversion/NativeWindowFrame.cs',base/'Conversion/CanvasConversion.1.Core.cs'])
  size=conversion[conversion.index('        Vector2 size = target.rect.size;'):conversion.index('        // Bug #7 (pause-menu height):')]
  seat=conversion[conversion.index('        target.SetParent(hostRect, worldPositionStays: false);'):conversion.index('        AdoptNestedCanvases(panel);')]
  sources['Conversion764.cs']='using UnityEngine;namespace GloomhavenVR.WorldUI { internal static class NativeFrameConversion764 {internal static Vector2 Convert(RectTransform target,RectTransform hostRect,ConvertedPanel panel){panel.NativeDesignFrame=NativeWindowFrame.Resolve(target);\n'+size+'\nhostRect.sizeDelta=size;\n'+seat+'return size;}internal static void Maintain(RectTransform target,Vector2 frame){NativeWindowFrame.Repin(target,frame);}}\ninternal static partial class CanvasConversion {'+method(conversion,'internal static void PlaceHost(')+'}}'
  config=(base/'WorldUIConfig.cs').read_text();bind=config[config.index('        CombatLogScale = _file.Bind('):config.index('        PanelsFollowView = _file.Bind(')]
  grab=(base/'Grab/PanelGrab.cs').read_text();limits='\n'.join(re.search(r'internal const float '+n+r' = [^;]+;',grab)[0] for n in ['MinScale','MaxScale'])
  default=(ROOT/'src/GloomhavenVR/Defaults/Defaults.WorldUI.cs').read_text();constant=re.search(r'internal const float CombatLogScale = [^;]+;',default)[0]
  sources['Config764.cs']='using BepInEx.Configuration;namespace GloomhavenVR.WorldUI {internal static class PanelGrabHandle {'+limits+'}\ninternal static class Defaults {'+constant+'}\ninternal static partial class WorldUIConfig {private static void BindScale(){'+bind+'}}}'
  pins.extend([base/'WorldUIConfig.cs',base/'Grab/PanelGrab.cs',ROOT/'src/GloomhavenVR/Defaults/Defaults.WorldUI.cs'])
  cases=[('production',None,None,None,None),
   ('full-screen-log','NativeWindowFrame.cs','        if (target.GetComponent<CombatLogHandler>() != null) return Vector2.zero;','        // original compact-log exclusion removed','conversion retains original compact combat-log frame'),
   ('config-old-floor','Config764.cs','new AcceptableValueRange<float>(PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale)','new AcceptableValueRange<float>(0.5f, 2f)','actual config range admits the same small two-hand gesture'),
   ('persist-old-floor','CombatLogPlacement.cs','Mathf.Clamp(_frame.localScale.x,\n            PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale)','Mathf.Clamp(_frame.localScale.x, 0.5f, 2f)','release retains the complete small or large manual resize'),
   ('free-tick-old-floor','CombatLogPlacement.cs','float factor = Mathf.Clamp(WorldUIConfig.CombatLogScale.Value,\n                PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);','float factor = Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f);','free placement tick preserves the manual resize'),
   ('respawn-old-floor','CombatLogPlacement.cs','_frame.localScale = Vector3.one * Mathf.Clamp(WorldUIConfig.CombatLogScale.Value,\n            PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);','_frame.localScale = Vector3.one * Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f);','explicit log respawn preserves the full supported small scale')]
  if args.case:assert set(args.case)<={x[0] for x in cases};cases=[x for x in cases if x[0] in args.case]
  inputs=run/'inputs';inputs.mkdir();assert execute([str(Path.home()/'unitypy-venv/bin/python'),str(fixture/'export-native.py'),str(ROOT),str(inputs)],run/'native-export.log')==0,'Original log export failed'
  project=run/'unity'
  for d in ['Assets/Plugins','Assets/Editor','ProjectSettings','Packages']:(project/d).mkdir(parents=True)
  native_tmp=load_module('native_tmp',ROOT/'scripts/menu-depth-desktop/native_tmp.py');native_tmp.prepare(ROOT,run,project)
  # prepare() exports a menu font as its existing independent shader-source witness;
  # the actual glyphs rendered here come from our original combat-log row fonts.
  managed=ROOT/'ressources/GH_Data/Managed';unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');bep=Path.home()/'.nuget/packages/bepinex.baselib/5.4.20/lib/netstandard2.0/BepInEx.dll'
  manifest={'result':str(run/'results.txt'),'evidence':str(run/'captures'),'cases':[]}
  for name,file,before,after,expected in cases:
   case=run/name;prod=case/'production';prod.mkdir(parents=True)
   for filename,text in sources.items():
    if filename==file:assert text.count(before)==1,(name,before);text=text.replace(before,after)
    (prod/filename).write_text(text)
   csproj=case/'CombatLog.csproj';shutil.copy2(fixture/'CombatLog.csproj',csproj);assembly='CombatLog_'+name.replace('-','_')
   command=[str(Path.home()/'.dotnet/dotnet'),'build',str(csproj),'-c','Release','--nologo','-v','quiet','-p:UseSharedCompilation=false','-nodeReuse:false','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(prod),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(managed),'-p:BepInExAssembly='+str(bep)]
   code=execute(command,case/'build.log');assert code==0,(case/'build.log').read_text()
   dll=case/'bin/Release/netstandard2.1'/(assembly+'.dll');target=project/'Assets/Plugins'/dll.name;shutil.copy2(dll,target);manifest['cases'].append({'name':name,'dll':str(target),'expected':expected or ''})
  runner=ROOT/'scripts/multiplayer-progression-runtime/Editor/ProgressionRunner.cs';text=runner.read_text();cleanup='foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj);';assert text.count(cleanup)==1
  (project/'Assets/Editor/ProgressionRunner.cs').write_text(text.replace(cleanup,cleanup+'\n                    Assembly.LoadFile(entry.dll).GetType("ProgressionTests").GetMethod("Cleanup").Invoke(null,null);'))
  for p in [managed/'UnityEngine.UI.dll',managed/'Unity.TextMeshPro.dll',bep]:shutil.copy2(p,project/'Assets/Plugins'/p.name);pins.append(p)
  # BepInEx's optional plugin/patcher types reference Harmony/Cecil/InputSystem.
  # No such type is used here: execute its real ConfigFile/ConfigEntry/range,
  # and let Mono resolve actually used types rather than Unity eagerly rejecting
  # the entire assembly for unrelated optional plugin references.
  (project/'Assets/Plugins/BepInEx.dll.meta').write_text('fileFormatVersion: 2\nguid: 71a927a3c09f4b11a6475fbca52f2864\nPluginImporter:\n  serializedVersion: 2\n  validateReferences: 0\n  platformData:\n  - first:\n      Any: \n    second:\n      enabled: 1\n      settings: {}\n')
  (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');(project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{k:'1.0.0' for k in ['com.unity.modules.ui','com.unity.modules.imgui','com.unity.modules.imageconversion','com.unity.modules.physics','com.unity.modules.jsonserialize']}}))
  mf=run/'manifest.json';mf.write_text(json.dumps(manifest,indent=2));pins.extend([Path(__file__),runner,ROOT/'scripts/levelup-native-runtime/export_inputs.py',ROOT/'scripts/menu726-native-profile.py',ROOT/'scripts/menu-depth-desktop/native_tmp.py',*fixture.glob('*')])
  (run/'source-pins.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in pins if p.is_file()},indent=2)+'\n');(run/'source-content-hashes.json').write_text(json.dumps({n:hashlib.sha256(s.encode()).hexdigest() for n,s in sources.items()},indent=2)+'\n')
  command=[str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','ProgressionRunner.Start','-progressionManifest',str(mf),'-logFile',str(run/'unity.log')]
  if not os.environ.get('DISPLAY'):command=['xvfb-run','-a',*command]
  code=execute(command,run/'launcher.log');result=run/'results.txt'
  if result.exists():print(result.read_text(),end='',flush=True)
  assert code==0 and result.exists(),'Unity failed: '+str(run)
  status=0
 finally:
  (run/'result.json').write_text(json.dumps({'exit_code':status,'partial':bool(args.case),'seconds':time.monotonic()-started,'limits':__doc__},indent=2)+'\n')
  (run/'cleanup.json').write_text(json.dumps(cleanup_completed_run('combatlog-runtime',status,completion,ROOT,process_reaped=True),indent=2)+'\n')
if __name__=='__main__':main()
