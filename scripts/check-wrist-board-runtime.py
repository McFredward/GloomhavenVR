#!/usr/bin/env python3
"""Execute the complete current production board/wrist paths in real Unity.

The tracked device samples are an explicit external boundary. Original authored
board assets are rebuilt for this editor platform; no pose/clone/menu code is
reimplemented by the fixture. This is not hardware comfort or latency evidence.
"""
import argparse, hashlib, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def hashes(root):
 return {str(f.relative_to(root)):hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted((root/'src').rglob('*.cs')) if 'obj' not in f.parts and 'bin' not in f.parts}
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT);p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/wrist-board-runtime');p.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')));p.add_argument('--reuse-project',type=Path);a=p.parse_args()
 a.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()));before=hashes(a.source_root)
 project=a.reuse_project.resolve() if a.reuse_project else run/'unity'
 for name in ('Assets/Editor','Assets/Plugins','Packages','ProjectSettings'):(project/name).mkdir(parents=True,exist_ok=True)
 build=run/'production';build.mkdir();items=build/'PrivateItems.targets';items.write_text('<Project><Target Name="RemoveExistingOutput" BeforeTargets="CoreCompile"><ItemGroup><Compile Remove="obj/**/*.cs;bin/**/*.cs;'+str(a.source_root/'src/GloomhavenVR/obj')+'/**/*.cs;'+str(a.source_root/'src/GloomhavenVR/bin')+'/**/*.cs" /></ItemGroup></Target></Project>')
 dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
 with (run/'build.log').open('w') as log:
  r=subprocess.run([dotnet,'build',str(a.source_root/'src/GloomhavenVR/GloomhavenVR.csproj'),'-c','Debug','-v','quiet','-p:BaseIntermediateOutputPath='+str(build/'obj')+'/', '-p:OutputPath='+str(build/'bin')+'/', '-p:UseSharedCompilation=false','-p:CustomAfterMicrosoftCommonTargets='+str(items)],cwd=a.source_root,stdout=log,stderr=subprocess.STDOUT)
 if r.returncode:raise SystemExit('Private current-source build failed: '+str(run/'build.log'))
 plugin=build/'bin/GloomhavenVR.dll';shutil.copyfile(plugin,project/'Assets/Plugins/GloomhavenVR.dll')
 managed=a.source_root/'ressources/GH_Data/Managed'
 for dll in managed.glob('*.dll'):
  if not dll.name.startswith(('UnityEngine','Unity.','System','Microsoft','Mono.','mscorlib','netstandard')):shutil.copyfile(dll,project/'Assets/Plugins'/dll.name)
 (project/'Assets/Plugins/Unity.InputSystem.dll').unlink(missing_ok=True)
 for name in ('Unity.Addressables.dll','Unity.ResourceManager.dll','Unity.Timeline.dll','Unity.Postprocessing.Runtime.dll','UnityEngine.SpatialTracking.dll','System.Runtime.CompilerServices.Unsafe.dll'):shutil.copyfile(managed/name,project/'Assets/Plugins'/name)
 for dll in (a.source_root/'libs/RuntimeDeps').glob('*.dll'):shutil.copyfile(dll,project/'Assets/Plugins'/dll.name)
 packages=Path.home()/'.nuget/packages'
 for package,version,framework,name in [('bepinex.baselib','5.4.20','net35','BepInEx'),('harmonyx','2.7.0','net45','0Harmony'),('mono.cecil','0.11.4','net40','Mono.Cecil'),('monomod.utils','21.12.13.1','net452','MonoMod.Utils'),('monomod.runtimedetour','21.12.13.1','net452','MonoMod.RuntimeDetour')]:shutil.copyfile(packages/package/version/'lib'/framework/(name+'.dll'),project/'Assets/Plugins'/(name+'.dll'))
 table=a.source_root/'unity/GloomhavenVR.Assets/Assets/Bundle/Table';shutil.copytree(table,project/'Assets/Bundle/Table',dirs_exist_ok=True)
 shutil.copytree(a.source_root/'unity/GloomhavenVR.Assets/Assets/Bundle/Hands',project/'Assets/Bundle/Hands',dirs_exist_ok=True)
 fixture=ROOT/'scripts/wrist-board-runtime'
 (project/'Assets/Editor/WristHarness.cs').unlink(missing_ok=True);(project/'Assets/Editor/WristHarness.cs.meta').unlink(missing_ok=True);shutil.copyfile(fixture/'WristHarness.cs',project/'Assets/WristHarness.cs');shutil.copyfile(fixture/'Editor/WristRunner.cs',project/'Assets/Editor/WristRunner.cs')
 (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.textmeshpro':'3.0.6','com.unity.inputsystem':'1.4.4','com.unity.ugui':'1.0.0',**{'com.unity.modules.'+n:'1.0.0' for n in ['ui','physics','animation','imageconversion','jsonserialize','assetbundle','audio','director','particlesystem','terrain','video','cloth','physics2d','unitywebrequest','unitywebrequestassetbundle','unitywebrequestaudio','unitywebrequesttexture','unitywebrequestwww','ai','imgui','xr']}}}))
 (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
 (run/'source-hashes.json').write_text(json.dumps({'production':before,'production_dll':hashlib.sha256(plugin.read_bytes()).hexdigest(),'assets':{str(f.relative_to(a.source_root/'unity/GloomhavenVR.Assets/Assets')):hashlib.sha256(f.read_bytes()).hexdigest() for folder in ('Table','Hands') for f in sorted((a.source_root/'unity/GloomhavenVR.Assets/Assets/Bundle'/folder).rglob('*')) if f.is_file()},'limits':['Tracked XR samples are supplied explicitly to real VRHand/HandRig instances; no headset latency/comfort is asserted.','Original board prefab/model/textures are imported unchanged and rebundled for the matching Unity editor.','Complete private current-source production DLL executes original EnsureBuilt, wrist lifecycle, original serializer, remote furniture and config row/step logic.']},indent=2)+'\n')
 r=subprocess.run(['xvfb-run','-a',str(a.unity),'-batchmode','-projectPath',str(project),'-executeMethod','WristRunner.Start','-evidenceRoot',str(run),'-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=480)
 report=run/'results.json'
 if not report.is_file() or r.returncode:raise SystemExit('FAIL real Unity wrist runtime: '+str(run/'unity.log'))
 if hashes(a.source_root)!=before:raise SystemExit('FAIL production changed during native execution')
 data=json.loads(report.read_text());print(json.dumps(data,indent=2));print('Evidence: '+str(run))
 if not data.get('passed'):raise SystemExit(1)
if __name__=='__main__':main()
