#!/usr/bin/env python3
"""Exercise actual native rules widgets and production DLL in real Unity play mode.

GH_Data remains read-only. Serialized native row/container/font/atlas are freshly recovered;
no other feature's exported project or prior test output is a dependency.
"""
import argparse, hashlib, json, os, shutil, subprocess, sys, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def validate_loaded_dll(expected,loaded):
 if hashlib.sha256(expected.read_bytes()).digest()!=hashlib.sha256(loaded.read_bytes()).digest():
  raise ValueError('Loaded production DLL differs from the private current-source build')
def production_hashes(root):
 return {str(f.relative_to(root)):hashlib.sha256(f.read_bytes()).hexdigest()
  for f in sorted((root/'src').rglob('*.cs')) if 'obj' not in f.parts and 'bin' not in f.parts}
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--reuse-project',type=Path);p.add_argument('--source-root',type=Path,default=ROOT);p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/board-rules-runtime');p.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')));p.add_argument('--unitypy-python',type=Path,default=Path('/home/claw/unitypy-venv/bin/python'));a=p.parse_args()
 a.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()));project=a.reuse_project.resolve() if a.reuse_project else run/'unity';assets=project/'Assets';(assets/'Editor').mkdir(parents=True,exist_ok=True);(assets/'Plugins').mkdir(exist_ok=True);(project/'Packages').mkdir(exist_ok=True);(project/'ProjectSettings').mkdir(exist_ok=True)
 # A stale normal bin/Debug DLL is never evidence: build requested sources in this run's
 # private output/intermediate tree before loading Unity, even when reusing its import cache.
 dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
 source_before=production_hashes(a.source_root)
 build=run/'production';build.mkdir();build_log=run/'build.log'
 items=build/'PrivateItems.targets'
 items.write_text('<Project><Target Name="RemoveExistingOutput" BeforeTargets="CoreCompile"><ItemGroup><Compile Remove="obj/**/*.cs;bin/**/*.cs" /><Compile Remove="'+str(a.source_root/'src/GloomhavenVR/obj')+'/**/*.cs" /><Compile Remove="'+str(a.source_root/'src/GloomhavenVR/bin')+'/**/*.cs" /></ItemGroup></Target></Project>')
 with build_log.open('w') as log:
  result=subprocess.run([dotnet,'build',str(a.source_root/'src/GloomhavenVR/GloomhavenVR.csproj'),'-c','Debug','-v','quiet','-p:BaseIntermediateOutputPath='+str(build/'obj')+'/', '-p:OutputPath='+str(build/'bin')+'/', '-p:UseSharedCompilation=false','-p:CustomAfterMicrosoftCommonTargets='+str(items)],cwd=a.source_root,stdout=log,stderr=subprocess.STDOUT)
 if result.returncode:raise SystemExit('FAIL: current private production build; see '+str(build_log))
 if production_hashes(a.source_root)!=source_before:raise SystemExit('FAIL: production sources changed during private compilation')
 plugin=build/'bin/GloomhavenVR.dll'
 if not plugin.is_file():raise SystemExit('FAIL: private production DLL missing')
 subprocess.run([str(a.unitypy_python),str(ROOT/'scripts/board-rules-runtime/export-native.py'),str(a.source_root/'ressources/GH_Data'),str(assets/'NativeSource')],check=True)
 # Exact built production plus read-only native dependency assemblies; Unity engine/uGUI/TMP
 # come from the actual editor/packages, never shims. Original native game controllers remain dormant.
 shutil.copyfile(plugin,assets/'Plugins/GloomhavenVR.dll')
 validate_loaded_dll(plugin,assets/'Plugins/GloomhavenVR.dll')
 stale=run/'stale-control.dll';stale.write_bytes(plugin.read_bytes()+b'stale artifact control')
 try:validate_loaded_dll(plugin,stale)
 except ValueError:stale_rejected=True
 else:raise SystemExit('FAIL: stale artifact provenance control was accepted')
 stale.unlink()
 (run/'provenance-control.json').write_text(json.dumps({'loaded_private_current_build':True,'stale_artifact_rejected':stale_rejected})+'\n')
 managed=a.source_root/'ressources/GH_Data/Managed';public=a.source_root/'src/GloomhavenVR/obj/Debug/net472/publicized'
 for dll in managed.glob('*.dll'):
  if dll.name.startswith(('UnityEngine','Unity.','System','Microsoft','Mono.','mscorlib','netstandard')):continue
  shutil.copyfile(dll,assets/'Plugins'/dll.name)
 # Use the editor-compatible package for InputSystem. The shipped player assembly cannot
 # process Editor updates and would flood the fixture log without affecting game behavior.
 (assets/'Plugins/Unity.InputSystem.dll').unlink(missing_ok=True)
 for name in ['Unity.Addressables.dll','Unity.ResourceManager.dll','Unity.Timeline.dll','Unity.Postprocessing.Runtime.dll','UnityEngine.SpatialTracking.dll','System.Runtime.CompilerServices.Unsafe.dll']:
  shutil.copyfile(managed/name,assets/'Plugins'/name)
 for dll in (a.source_root/'libs/RuntimeDeps').glob('*.dll'):shutil.copyfile(dll,assets/'Plugins'/dll.name)
 packages=Path.home()/'.nuget/packages'
 shutil.copyfile(packages/'bepinex.baselib/5.4.20/lib/net35/BepInEx.dll',assets/'Plugins/BepInEx.dll')
 shutil.copyfile(packages/'harmonyx/2.7.0/lib/net45/0Harmony.dll',assets/'Plugins/0Harmony.dll')
 for package,version,framework,name in [('mono.cecil','0.11.4','net40','Mono.Cecil'),('monomod.utils','21.12.13.1','net452','MonoMod.Utils'),('monomod.runtimedetour','21.12.13.1','net452','MonoMod.RuntimeDetour')]:
  shutil.copyfile(packages/package/version/'lib'/framework/(name+'.dll'),assets/'Plugins'/(name+'.dll'))
 for src in (ROOT/'scripts/board-rules-runtime').glob('*.cs'):shutil.copyfile(src,assets/src.name)
 shutil.copyfile(ROOT/'scripts/board-rules-runtime/Editor/RulesRunner.cs',assets/'Editor/RulesRunner.cs')
 (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.textmeshpro':'3.0.6','com.unity.inputsystem':'1.4.4','com.unity.ugui':'1.0.0',**{'com.unity.modules.'+n:'1.0.0' for n in ['ui','physics','animation','imageconversion','jsonserialize','assetbundle','audio','director','particlesystem','terrain','video','cloth','physics2d','unitywebrequest','unitywebrequestassetbundle','unitywebrequestaudio','unitywebrequesttexture','unitywebrequestwww','ai','imgui','xr']}}}))
 (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
 files=[*sorted((a.source_root/'src/GloomhavenVR/WorldUI/Surfaces').glob('BoardRules*.cs')),*sorted((a.source_root/'src/GloomhavenVR/Net').glob('NativeBoard*.cs')),*[a.source_root/f for f in ['src/GloomhavenVR/WorldUI/Surfaces/TablePanelSurfaces.cs','src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs','src/GloomhavenVR/WorldUI/Conversion/ConvertedPanel.cs','src/GloomhavenVR/WorldUI/MrBacking.cs','src/GloomhavenVR/Net/Remote/RemoteWidgetMirror.cs','src/GloomhavenVR/Net/Remote/RemoteBoardRulesPlayer.cs','src/GloomhavenVR/Net/Remote/RemoteObjectivesPanel.cs','src/GloomhavenVR/Net/Remote/NativeBoardPresentationClock.cs','src/GloomhavenVR/Net/Remote/RemoteNativeElements.cs']]]
 hashes={str(f.relative_to(a.source_root)):hashlib.sha256(f.read_bytes()).hexdigest() for f in files}
 hashes['private-current-production-dll']=hashlib.sha256(plugin.read_bytes()).hexdigest()
 (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
 result=subprocess.run(['xvfb-run','-a',str(a.unity),'-batchmode','-projectPath',str(project),'-executeMethod','RulesRunner.Start','-evidenceRoot',str(run),'-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=420)
 report=run/'results.json'
 if not report.is_file() or result.returncode:raise SystemExit('FAIL: actual Unity run; see '+str(run/'unity.log'))
 if production_hashes(a.source_root)!=source_before:raise SystemExit('FAIL: production sources changed during native execution')
 (run/'frozen-production-inputs.json').write_text(json.dumps(source_before,indent=2)+'\n')
 data=json.loads(report.read_text());print(json.dumps(data,indent=2));print('Evidence: '+str(run))
 if not data.get('passed'):raise SystemExit(1)
if __name__=='__main__':main()
