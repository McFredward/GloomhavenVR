#!/usr/bin/env python3
"""Execute Results binding/ready ownership and draw-only leases in actual Unity.

Native registry, animation clocks, vote acknowledgements and conversion allocation
are explicit ports. The original native results-ready decision body executes;
this is not a Photon or full native results/bootstrap/HMD replay.
"""
import argparse,hashlib,json,os,shutil,signal,subprocess,tempfile
from pathlib import Path
from town_run_retention import cleanup_completed_run
ROOT=Path(__file__).resolve().parents[1]
def method(s,k):
 a=s.index(k);p=s.index('{',a)+1;d=1
 while d:d+=(s[p]=='{')-(s[p]=='}');p+=1
 return s[a:p]
def run(cmd,log,timeout=180):
 with log.open('w') as output:
  p=subprocess.Popen(cmd,stdout=output,stderr=subprocess.STDOUT,start_new_session=True)
  try:return p.wait(timeout=timeout)
  finally:
   try:os.killpg(p.pid,signal.SIGKILL)
   except ProcessLookupError:pass
   p.wait()
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--case',action='append');args=parser.parse_args()
 out=ROOT/'.planning/debug/results-window-runtime';out.mkdir(parents=True,exist_ok=True);folder=Path(tempfile.mkdtemp(prefix='run-',dir=out));log=folder/'completion.log';log.write_text('Evidence: '+str(folder)+'\n');status=1
 print('Evidence: '+str(folder),flush=True)
 fixture=ROOT/'scripts/results-window-runtime';base=ROOT/'src/GloomhavenVR/WorldUI';unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');managed=ROOT/'ressources/GH_Data/Managed'
 paths=[base/'Modal/ScenarioResultsNative.cs',base/'Composites/ScenarioResultsReadyPark.cs',base/'Modal/ScenarioResultsVisualLease.cs',base/'Composites/MovedSubtreeLayers.cs']
 sources={p.name:p.read_text() for p in paths}
 transfer=base/'Modal/ModalFallback.CompositeTransfer.cs';shared=base/'Modal/SharedWindows.cs';native=ROOT/'decompiled/GH.Runtime/UINewAdventureResultsManager.cs'
 sources['Transfer.cs']='using UnityEngine.UI; namespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {\n'+method(transfer.read_text(),'internal static bool ReleaseForComposite(')+'\n}}'
 sources['Shared.cs']='using UnityEngine.UI;using GloomhavenVR.WorldUI.MapRoom; namespace GloomhavenVR.WorldUI {\n'+method(shared.read_text(),'internal enum SharedWindowKind')+'\ninternal static partial class SharedWindows {\n'+'\n'.join(method(shared.read_text(),s) for s in ['internal static SharedWindowKind KindOf(','internal static bool ParticipatesHere(','internal static UIWindow? WindowOf('])+'\n}}'
 sources['NativeReady.cs']='using System;using FFSNet;using GLOOM;using UnityEngine;using UnityEngine.Events; public partial class UINewAdventureResultsManager {\n'+method(native.read_text(),'private void ShowReadyUpToggles(string buttonText, Action callback, bool autoReadyUp')+'\n}'
 cases=[('production',None,None,None,None),
 ('missing-native-home-retirement','Transfer.cs','        ScenarioResultsReadyPark.BeforeExternalOwner(window);','','external owner receives the actual native ready home before recording its receipt'),
 ('steal-map-owner','ScenarioResultsReadyPark.cs','if (MapRoom.ReadyToggleParkClaim.Claimed','if (bool.Parse("false") && MapRoom.ReadyToggleParkClaim.Claimed','an existing map/loadout owner retains the pooled ready control'),
 ('blank-native-after-mirror','ScenarioResultsVisualLease.cs','foreach (var pair in _colours)\n            if (pair.Key != null && pair.Key.GetColor().a == 0f) pair.Key.SetColor(pair.Value);','foreach (var pair in _colours)\n            if (bool.Parse("false")) pair.Key.SetColor(pair.Value);','native draw tween changes survive visual-lease retirement'),
 ('suppress-unrelated-help','ScenarioResultsNative.cs','return help.m_HelpInfo.keybaordLines == LocalizationManager.GetTranslation("Consoles/GUI_WAIT_FOR_HOST_TIP")','return bool.Parse("true") || help.m_HelpInfo.keybaordLines == LocalizationManager.GetTranslation("Consoles/GUI_WAIT_FOR_HOST_TIP")','an unrelated replacement HelpBox is restored immediately')]
 if args.case:assert set(args.case)<={x[0] for x in cases};cases=[x for x in cases if x[0] in args.case]
 try:
  assert run([str(Path.home()/'unitypy-venv/bin/python'),str(fixture/'export-native.py'),str(managed.parent),str(folder/'original-results-audit.json')],folder/'native-export.log')==0,'Original results audit failed'
  project=folder/'unity';
  for d in ['Assets/Editor','Assets/Plugins','Packages','ProjectSettings']:(project/d).mkdir(parents=True,exist_ok=True)
  manifest={'result':str(folder/'results.txt'),'evidence':str(folder/'captures'),'cases':[]}
  for name,file,before,after,expected in cases:
   case=folder/name;prod=case/'production';prod.mkdir(parents=True)
   for filename,text in sources.items():
    if filename==file:assert text.count(before)>=1;text=text.replace(before,after)
    (prod/filename).write_text(text)
   csproj=case/'Results.csproj';shutil.copy2(fixture/'Results.csproj',csproj);assembly='Results_'+name.replace('-','_')
   result=run([str(Path.home()/'.dotnet/dotnet'),'build',str(csproj),'-c','Release','--nologo','--verbosity','quiet','-p:UseSharedCompilation=false','-nodeReuse:false','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(prod),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(managed)],case/'build.log')
   if result:raise RuntimeError((case/'build.log').read_text()+'\nCompilation is not an accepted control')
   dll=case/'bin/Release/netstandard2.1'/(assembly+'.dll');target=project/'Assets/Plugins'/dll.name;shutil.copy2(dll,target)
   manifest['cases'].append({'name':name,'dll':str(target),'expected':expected or ''})
  shutil.copy2(managed/'UnityEngine.UI.dll',project/'Assets/Plugins/UnityEngine.UI.dll')
  runner=ROOT/'scripts/multiplayer-progression-runtime/Editor/ProgressionRunner.cs';shutil.copy2(runner,project/'Assets/Editor/ProgressionRunner.cs')
  (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{k:'1.0.0' for k in ['com.unity.modules.ui','com.unity.modules.imgui','com.unity.modules.imageconversion','com.unity.modules.physics','com.unity.modules.jsonserialize']}}))
  (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');mf=folder/'manifest.json';mf.write_text(json.dumps(manifest,indent=2))
  pins=[folder/'original-results-audit.json',fixture/'export-native.py',*paths,transfer,shared,native,ROOT/'decompiled/GH.Runtime/UIReadyToggle.cs',ROOT/'decompiled/GH.Runtime/Singleton.cs',Path(__file__),*fixture.glob('*'),runner,managed/'UnityEngine.UI.dll']
  (folder/'source-pins.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in pins if p.is_file()},indent=2))
  command=[str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','ProgressionRunner.Start','-progressionManifest',str(mf),'-logFile',str(folder/'unity.log')]
  if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
  code=run(command,folder/'launcher.log',240);result=folder/'results.txt'
  if result.exists():print(result.read_text(),end='')
  assert code==0 and result.exists(),'Unity failure: '+str(folder)
  status=0
 finally:
  (folder/'result.json').write_text(json.dumps({'exit_code':status,'limits':__doc__,'partial':bool(args.case)},indent=2));(folder/'cleanup.json').write_text(json.dumps(cleanup_completed_run('results-window-runtime',status,log,ROOT,process_reaped=True),indent=2))
if __name__=='__main__':main()
