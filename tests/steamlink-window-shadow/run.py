#!/usr/bin/env python3
"""Focused actual Unity stock image-effect geometry and raster proof."""
import argparse,hashlib,importlib,json,os,shutil,signal,subprocess,sys,tempfile
from pathlib import Path
sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--old-control',action='store_true');p.add_argument('--aspect-only',action='store_true');a=p.parse_args()
 a.output.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output.resolve()));log=run/'completion.log';log.write_text(str(run)+'\n');children=[];code=1
 sys.path.insert(0,str(ROOT/'scripts'));cleanup=importlib.import_module('town_run_retention').cleanup_completed_run
 project=run/'owned-unity-project';assets=project/'Assets';(assets/'Editor').mkdir(parents=True);(assets/'Plugins').mkdir()
 source=ROOT/'src/GloomhavenVR/WorldUI/Conversion/MrBackingPaintedBounds.cs';capture=ROOT/'src/GloomhavenVR/WorldUI/Conversion/MrBackingCaptureBounds.cs'
 files=[Path(__file__),HERE/'Fixture.cs',HERE/'Ports.cs',source,capture];pins={str(f):sha(f)for f in files};(run/'sources').mkdir()
 for f in files:shutil.copyfile(f,run/'sources'/f.name)
 (run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
 frozen=run/'sources'/source.name
 if a.old_control:frozen.write_text(frozen.read_text().replace('            IncludeImageEffects(graphic, toHost, ref bounds);','            _ = graphic;'))
 unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');data=unity.parent/'Data';framework=Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
 friend=run/'friend.cs';friend.write_text('[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]\n');binary=run/'actual-painted-bounds.dll'
 refs=[framework/'mscorlib.dll',framework/'System.dll',data/'Managed/UnityEngine/UnityEngine.CoreModule.dll',data/'Managed/UnityEngine/UnityEngine.UIModule.dll',ROOT/'ressources/GH_Data/Managed/UnityEngine.UI.dll',data/'Managed/UnityEngine/UnityEngine.TextRenderingModule.dll']
 cmd=[str(Path.home()/'.dotnet/dotnet'),str(Path.home()/'.dotnet/sdk/8.0.423/Roslyn/bincore/csc.dll'),'-nologo','-noconfig','-nostdlib+','-target:library','-langversion:latest','-nullable:enable','-out:'+str(binary)]+['-r:'+str(r)for r in refs]+[str(friend),str(frozen),str(capture),str(HERE/'Ports.cs')]
 def execute(command,path,timeout,env=None):
  with path.open('w')as stream:
   child=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True,env=env);children.append(child)
   try:return child.wait(timeout=timeout)
   except BaseException:
    if child.poll()is None:os.killpg(child.pid,signal.SIGKILL)
    child.wait();raise
 try:
  (run/'compile-command.json').write_text(json.dumps(cmd,indent=2)+'\n');assert execute(cmd,run/'compile.log',30)==0,'Actual production helper compile failed'
  shutil.copyfile(binary,assets/'Plugins'/binary.name);shutil.copyfile(HERE/'Fixture.cs',assets/'Editor/Fixture.cs')
  (project/'Packages').mkdir();(project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}\n');(project/'ProjectSettings').mkdir();(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
  result=run/'gpu.json';env=os.environ.copy();env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
  status=execute(['xvfb-run','-a',str(unity),'-batchmode','-force-vulkan','-projectPath',str(project),'-executeMethod','WindowShadowFixture.Start','-shadowResult',str(result),'-logFile',str(run/'unity.log')]+(['-shadowAspectOnly']if a.aspect_only else []),run/'launch.log',240,env)
  assert result.is_file(),'No actual Unity result';r=json.loads(result.read_text())
  if a.old_control:assert status!=0 and not r['passed'] and 'active-shadow xMax' in r['failure'],'Old geometry did not fail causally'
  else:assert status==0 and r['passed'],r['failure']
  assert all(sha(Path(path))==pin for path,pin in pins.items()),'Frozen inputs changed through child reap';code=0
 finally:
  receipt=cleanup('steamlink-window-shadow',code,log,ROOT,process_reaped=all(c.poll()is not None for c in children))
  if project.is_dir()and not project.is_symlink():shutil.rmtree(project)
  (run/'cleanup.json').write_text(json.dumps({'retention_api':receipt,'exact_owned_project_removed':str(project)},indent=2)+'\n');(run/'result.json').write_text(json.dumps({'exit_code':code,'old_control':a.old_control,'xr_executed':False,'native_story_cause_proven':False},indent=2)+'\n');print(str(run))
 return code
if __name__=='__main__':raise SystemExit(main())
