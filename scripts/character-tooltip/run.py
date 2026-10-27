#!/usr/bin/env python3
"""Execute original native tooltip placement against complete private production DLLs.

The game controllers are dormant except UILocalTooltip.Show/Hide/RefreshPosition;
converted-window membership, XR-running state and original UI camera are explicit
inputs. Production tooltip patches, raise/restore, exact identity policy and side
layout execute verbatim. Native data is read only; no headset result is claimed.
"""
from pathlib import Path
import argparse,hashlib,json,os,shutil,signal,subprocess,sys,tempfile
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0,str(ROOT/'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers,cleanup_project_editors
sys.dont_write_bytecode=True

def execute(command,log,timeout=300):
 with log.open('w')as stream:
  child=subprocess.Popen(command,cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
  try:return child.wait(timeout=timeout)
  finally:
   if child.poll()is None:os.killpg(child.pid,signal.SIGKILL)
   child.wait()

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def retain_package_sources(project,run):
 # Retention protects directories named Remote as hardware evidence. This is
 # instead the exact pinned InputSystem package's source namespace; retain its
 # byte identities outside Library before asking the unchanged API to clean it.
 records=[]
 for package in (project/'Library/PackageCache').glob('com.unity.inputsystem@*'):
  source=package/'InputSystem/Devices/Remote'
  if not source.is_dir()or source.is_symlink():continue
  files=[p for p in source.rglob('*')if p.is_file()]
  assert all(not p.is_symlink()and p.name!='.keep' for p in source.rglob('*'))
  pins={str(p.relative_to(source)):sha(p)for p in files}
  target=run/'retained-package-sources'/package.name/'InputSystem/Devices/Remote';target.parent.mkdir(parents=True,exist_ok=True)
  shutil.move(source,target)
  assert all(sha(target/p)==h for p,h in pins.items())
  records.append({'source':str(source),'retained':str(target),'sha256':pins})
 (run/'retained-package-sources.json').write_text(json.dumps(records,indent=2)+'\n')
 return records
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--case',action='append');a=p.parse_args()
 if not os.environ.get('ADB_SERVER_SOCKET'):p.error('Use the task-private ADB server.')
 signal.signal(signal.SIGTERM,lambda signum,frame:(_ for _ in()).throw(SystemExit(128+signum)))
 out=ROOT/'.planning/debug/character-tooltips';out.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=out));completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True)
 files={name:ROOT/'src/GloomhavenVR/WorldUI/Tooltips'/name for name in ['CharacterTooltipPlacement.cs','WorldTooltips.cs','TooltipOnWindow.cs']};sources={n:f.read_text()for n,f in files.items()}
 variants=[('production','','','',''),
  ('lost-right-lane','CharacterTooltipPlacement.cs','halfHostWidth + gap + halfWidth','-halfHostWidth - gap - halfWidth','native skin hint clears the fixed right edge'),
  ('lost-quest-placement','TooltipOnWindow.cs','bool characterHint = CharacterTooltipPlacement.TryLocalHint(widget, out RectTransform? characterAnchor);','bool characterHint = false; RectTransform? characterAnchor = null;','the original personal quest explanation clears the fixed right edge'),
  ('lost-late-layout','TooltipOnWindow.cs','if (rec.CharacterHintOnRight && rec.Owner.HostRect != null)','if (rec.CharacterHintOnRight && rec.Owner.HostRect != null && Time.frameCount < 0)','late quest layout retains the fixed right edge'),
  ('lost-creator-identity','CharacterTooltipPlacement.cs','UICharacterCreatorClassRoster? roster = creator != null','UICharacterCreatorClassRoster? roster = creator != null && Time.frameCount < 0','native creation perks hint uses right lane'),
  ('lost-exact-scope','CharacterTooltipPlacement.cs','if (anchor == null) return false;','if (anchor == null) return false;\n        if (anchor.GetComponent<UITooltipTarget>() != null) return true;','unrelated same-name tooltip retains ordinary inside-window placement'),
  ('lost-warm-cache','WorldTooltips.cs','if (!ReferenceEquals(_hoveredRect, hovered) || !ReferenceEquals(_hostWindow, owner)','if (Time.frameCount >= 0 || !ReferenceEquals(_hoveredRect, hovered) || !ReferenceEquals(_hostWindow, owner)','warm owner ticks never repeat character hierarchy classification')]
 if a.case:
  wanted=set(a.case);assert wanted<={v[0]for v in variants};variants=[v for v in variants if v[0]in wanted]
 paths=[p for p in (ROOT/'src').rglob('*.cs')if not {'obj','bin'}.intersection(p.parts)]+list(files.values())+[Path(__file__),ROOT/'scripts/character-tooltip/Editor/Runner.cs',ROOT/'scripts/character-tooltip/audit-native.py']
 pins={str(p):sha(p)for p in paths};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
 project=run/'unity';status=1
 try:
  assemblies=run/'assemblies';assemblies.mkdir();manifest={'result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
  for name,changed,before,after,expected in variants:
   case=run/name;case.mkdir();(case/'CacheOwnership.csproj').write_text('<Project/>\n');production=case/'Sources';production.mkdir()
   for filename,original in sources.items():
    code=original
    if filename==changed:
     assert code.count(before)==1,(name,before);code=code.replace(before,after)
    (production/filename).write_text(code)
   target=case/'PrivateSources.targets'
   remove=';'.join(str(f)for f in files.values())+';'+';'.join(str(f.relative_to(ROOT/'src/GloomhavenVR'))for f in files.values())
   includes=''.join('<Compile Include="'+str(production/n)+'" />'for n in files)
   target.write_text('<Project><Target Name="TooltipSourceInputs" BeforeTargets="CoreCompile"><ItemGroup><Compile Remove="'+remove+';obj/**/*.cs;bin/**/*.cs;'+str(ROOT/'src/GloomhavenVR/obj')+'/**/*.cs;'+str(ROOT/'src/GloomhavenVR/bin')+'/**/*.cs" />'+includes+'</ItemGroup></Target><Target Name="NoUnusedLooseMedia" BeforeTargets="AssignTargetPaths"><ItemGroup><Content Remove="@(Content)" /></ItemGroup></Target></Project>')
   assembly='GloomhavenVRTooltip_'+name.replace('-','_')
   command=['/home/claw/.dotnet/dotnet','build',str(ROOT/'src/GloomhavenVR/GloomhavenVR.csproj'),'-c','Debug','-v','quiet','--nologo','--disable-build-servers','-nr:false','-p:UseSharedCompilation=false','-p:AssemblyName='+assembly,'-p:BaseIntermediateOutputPath='+str(case/'obj')+'/','-p:OutputPath='+str(case/'bin')+'/','-p:CustomAfterMicrosoftCommonTargets='+str(target)]
   (case/'build-command.json').write_text(json.dumps(command,indent=2)+'\n')
   if execute(command,case/'build.log'):raise RuntimeError('Private production compilation failed: '+name)
   dll=assemblies/(assembly+'.dll');shutil.copy2(case/'bin'/dll.name,dll);manifest['cases'].append({'name':name,'dll':str(dll),'expected':expected})
  for name in ['Assets/Plugins','Assets/Editor','Packages','ProjectSettings']:(project/name).mkdir(parents=True)
  (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
  (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.textmeshpro':'3.0.6','com.unity.inputsystem':'1.4.4','com.unity.ugui':'1.0.0',**{'com.unity.modules.'+n:'1.0.0'for n in ['ui','physics','animation','imageconversion','jsonserialize','assetbundle','audio','director','particlesystem','terrain','video','cloth','physics2d','unitywebrequest','unitywebrequestassetbundle','unitywebrequestaudio','unitywebrequesttexture','unitywebrequestwww','ai','imgui','xr']}}}))
  managed=ROOT/'ressources/GH_Data/Managed'
  for dll in managed.glob('*.dll'):
   if not dll.name.startswith(('UnityEngine','Unity.','System','Microsoft','Mono.','mscorlib','netstandard')):shutil.copy2(dll,project/'Assets/Plugins'/dll.name)
  for name in ['Unity.Addressables.dll','Unity.ResourceManager.dll','Unity.Timeline.dll','Unity.Postprocessing.Runtime.dll','UnityEngine.SpatialTracking.dll','System.Runtime.CompilerServices.Unsafe.dll']:shutil.copy2(managed/name,project/'Assets/Plugins'/name)
  (project/'Assets/Plugins/Unity.InputSystem.dll').unlink(missing_ok=True)
  for dll in (ROOT/'libs/RuntimeDeps').glob('*.dll'):shutil.copy2(dll,project/'Assets/Plugins'/dll.name)
  packages=Path.home()/'.nuget/packages'
  for package,version,framework,name in [('bepinex.baselib','5.4.20','net35','BepInEx'),('harmonyx','2.7.0','net45','0Harmony'),('mono.cecil','0.11.4','net40','Mono.Cecil'),('monomod.utils','21.12.13.1','net452','MonoMod.Utils'),('monomod.runtimedetour','21.12.13.1','net452','MonoMod.RuntimeDetour')]:shutil.copy2(packages/package/version/'lib'/framework/(name+'.dll'),project/'Assets/Plugins'/(name+'.dll'))
  shutil.copy2(ROOT/'scripts/character-tooltip/Editor/Runner.cs',project/'Assets/Editor/Runner.cs')
  (run/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
  (run/'executed-sha256.json').write_text(json.dumps({p.name:sha(p)for p in assemblies.iterdir()},indent=2)+'\n')
  status=execute(['xvfb-run','-a','/home/claw/unity-2021.3.5/Editor/Unity','-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','CharacterTooltipRunner.Start','-characterTooltipManifest',str(run/'manifest.json'),'-logFile',str(run/'unity.log')],run/'launch.log',420)
  stable=all(Path(p).is_file()and sha(Path(p))==h for p,h in pins.items());(run/'source-stability.json').write_text(json.dumps({'unchanged':stable,'inputs':len(pins)})+'\n')
  if not stable:status=1
  if (run/'results.txt').exists():print((run/'results.txt').read_text(),end='')
  if status:raise RuntimeError('Original character tooltip proof failed: '+str(run))
 finally:
  driver=project/'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'
  if driver.is_file():
   target=run/'assemblies/Assembly-CSharp-Editor.dll';target.parent.mkdir(exist_ok=True);shutil.copy2(driver,target)
   (run/'executed-driver-sha256.json').write_text(json.dumps({'Assembly-CSharp-Editor.dll':sha(target)},indent=2)+'\n')
  editors=cleanup_project_editors(project,Path('/home/claw/unity-2021.3.5/Editor/Unity'),launcher_reaped=True);(run/'editor-cleanup.json').write_text(json.dumps(editors,indent=2)+'\n')
  if editors['remaining']:raise RuntimeError('Owned editor remains; preserve all caches.')
  compilers=cleanup_project_compilers(project,editor_reaped=True);(run/'compiler-cleanup.json').write_text(json.dumps(compilers,indent=2)+'\n')
  if compilers['remaining']:raise RuntimeError('Owned compiler remains; preserve all caches.')
  retain_package_sources(project,run)
  cleanup=cleanup_completed_run('character-tooltips',status,completion,ROOT,process_reaped=True);(run/'cache-cleanup.json').write_text(json.dumps(cleanup,indent=2)+'\n');print('cache cleanup',json.dumps({'removed_paths':len(cleanup['removed_paths']),'removed_bytes':cleanup['removed_bytes'],'skipped_paths':cleanup['skipped_paths']}),flush=True)
 return status
if __name__=='__main__':raise SystemExit(main())
