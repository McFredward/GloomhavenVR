#!/usr/bin/env bash
set -euo pipefail
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 - "$repo_root" <<'PY'
from pathlib import Path
import hashlib,importlib.util,json,re,shutil,subprocess,sys,tempfile
root=Path(sys.argv[1]);out=root/'.planning/debug/city-preconvert';out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir();source=run/'sources';source.mkdir()
fixture=root/'tests/GloomhavenVR.CityPreconvertTests'
for path in fixture.iterdir():
 if path.is_file():shutil.copy2(path,project/path.name)
original=root/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.8.Convert.cs';target=source/original.name;shutil.copy2(original,target)
bindings=[{'source':str(original),'snapshot':str(target),'sha256':hashlib.sha256(target.read_bytes()).hexdigest()}]
original=root/'tests/GloomhavenVR.MapReadyHandoffTests/UnityBoundary.cs';unity=project/'UnityBoundary.cs';shutil.copy2(original,unity)
bindings.append({'source':str(original),'snapshot':str(unity),'sha256':hashlib.sha256(unity.read_bytes()).hexdigest()})
(run/'source-bindings.json').write_text(json.dumps(bindings,indent=2)+'\n')
extract=[sys.executable,str(fixture/'extract.py'),str(target),str(project/'Production.cs')]
command=['dotnet','run','--project',str(project/'GloomhavenVR.CityPreconvertTests.csproj'),'-c','Release','--property:FixtureDir='+str(project),'--property:ProductionSource='+str(project/'Production.cs')]
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True);status=1
try:
 subprocess.run(extract,check=True)
 result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);log.write_text(log.read_text()+result.stdout);print(result.stdout,flush=True)
 if result.returncode:raise SystemExit(result.returncode)
 assertions=int(re.search(r'City preconvert: (\d+) assertions',result.stdout).group(1))
 shutil.copy2(project/'Production.cs',run/'executed-production.cs')
 assembly=run/'executed-assembly';assembly.mkdir()
 for item in (project/'bin/Release/net8.0').iterdir():
  if item.is_file():shutil.copy2(item,assembly/item.name)
 original=target.read_text()
 mutations=[
 ('missing-entry-hook','            PrepareCityReadinessForConversion(window);\n','', 'city preparation must run exactly once in conversion entry'),
 ('late-entry-hook','            PrepareCityReadinessForConversion(window);\n            var rect = window.transform as RectTransform;','            var rect = window.transform as RectTransform;\n            PrepareCityReadinessForConversion(window);','city preparation must precede first rect snapshot'),
 ('closed-native-window','!MapRoom.MapRoomDriver.Active || !window.IsOpen','!MapRoom.MapRoomDriver.Active || ReferenceEquals(null,new object())','only exact open native city proposal in map room may prepare'),
 ('outside-map-room','!MapRoom.MapRoomDriver.Active || !window.IsOpen','ReferenceEquals(null,new object()) || !window.IsOpen','only exact open native city proposal in map room may prepare'),
 ('wrong-native-subject','!ReferenceEquals(MapRoom.MapCityEventReadyUp.Window, window)','ReferenceEquals(null,new object())','city row must use exact native proposal and control'),
 ('row-before-old-loadout','        LoadoutConfirmPark.Tick();\n        MapRoom.MapQuestReadyRoster.Tick(MapRoom.ReadyRosterSite.CityEvent,\n            window, window.gameObject, confirmPosed: true);','        MapRoom.MapQuestReadyRoster.Tick(MapRoom.ReadyRosterSite.CityEvent,\n            window, window.gameObject, confirmPosed: true);\n        LoadoutConfirmPark.Tick();','old parkers must release before native row adoption')]
 for name,needle,replacement,expected in mutations:
  assert original.count(needle)==1,(name,'mutation target');target.write_text(original.replace(needle,replacement))
  extraction=subprocess.run(extract,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
  if extraction.returncode:result=extraction
  else:
   shutil.copy2(project/'Production.cs',run/(name+'.cs'))
   result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
  (run/(name+'.log')).write_text(result.stdout)
  if result.returncode==0 or expected not in result.stdout:raise RuntimeError('Negative control failed at wrong boundary: '+name+'\n'+result.stdout)
  message='City preconvert negative control: '+name+' rejected.'
  with log.open('a') as stream:stream.write(message+'\n')
  print(message,flush=True)
 target.write_text(original)
 (run/'result.json').write_text(json.dumps({'assertions':assertions,'negative_controls':[row[0] for row in mutations],'source_bindings':bindings,'actual_entry_prefix_executed':True,'final_capture_or_hardware_verified':False},indent=2)+'\n')
 status=0
finally:
 spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
 receipt=retention.cleanup_completed_run('city-preconvert',status,log,root,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
PY
