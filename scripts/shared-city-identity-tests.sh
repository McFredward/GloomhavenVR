#!/usr/bin/env bash
set -euo pipefail
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="$repo_root"
if [[ $# != 0 ]]; then
    if [[ $# != 2 || "$1" != --source-root ]]; then
        echo "Usage: bash scripts/shared-city-identity-tests.sh [--source-root CHECKOUT]" >&2
        exit 2
    fi
    source_root="$2"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 - "$repo_root" "$source_root" <<'PY'
from pathlib import Path
import hashlib,importlib.util,json,re,shutil,subprocess,sys,tempfile
root=Path(sys.argv[1]);production=Path(sys.argv[2]).resolve();out=root/'.planning/debug/shared-city-identity';out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir();source=run/'sources';source.mkdir()
fixture=root/'tests/GloomhavenVR.SharedCityIdentityTests'
for path in fixture.iterdir():
 if path.is_file():shutil.copy2(path,project/path.name)
bindings=[]
for name,path in [('SharedWindows.cs','WorldUI/Modal'),('RemoteMapStory.cs','Net/Remote'),('NetProtocol.cs','Net')]:
 original=production/'src/GloomhavenVR'/path/name;target=source/name;shutil.copy2(original,target)
 bindings.append({'source':str(original),'snapshot':str(target),'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
unity_original=root/'tests/GloomhavenVR.MapReadyHandoffTests/UnityBoundary.cs';unity=project/'UnityBoundary.cs';shutil.copy2(unity_original,unity)
bindings.append({'source':str(unity_original),'snapshot':str(unity),'sha256':hashlib.sha256(unity.read_bytes()).hexdigest()})
(run/'source-bindings.json').write_text(json.dumps(bindings,indent=2)+'\n')
command=['dotnet','run','--project',str(project/'GloomhavenVR.SharedCityIdentityTests.csproj'),'-c','Release','--property:FixtureDir='+str(project),'--property:ProductionSource='+str(project/'Production.cs')]
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True);status=1
try:
 subprocess.run([sys.executable,str(fixture/'extract.py'),str(source),str(project/'Production.cs')],check=True)
 result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);log.write_text(log.read_text()+result.stdout);print(result.stdout,flush=True)
 if result.returncode:raise SystemExit(result.returncode)
 assertions=int(re.search(r'Shared city identity: (\d+) assertions',result.stdout).group(1))
 shutil.copy2(project/'Production.cs',run/'executed-production.cs')
 assembly=run/'executed-assembly';assembly.mkdir()
 for item in (project/'bin/Release/net8.0').iterdir():
  if item.is_file():shutil.copy2(item,assembly/item.name)
 originals={path:path.read_text() for path in source.iterdir()}
 mutations=[
 ('missing-city-subject','SharedWindows.cs','if (cityReady != null)','if (object.ReferenceEquals(null,new object()))','city preflight is the encounter pose subject'),
 ('broad-ready-alias','SharedWindows.cs','if (ReferenceEquals(WindowOf(SharedWindowKind.Encounter), window))','if (window.GetComponent<UIReadyToggle>() != null)','another pooled-ready component cannot inherit city identity'),
 ('missing-proposal-key','RemoteMapStory.cs','if (ReferenceEquals(MapCityEventReadyUp.Window, window))','if (object.ReferenceEquals(null,new object()))','city preflight retains exact native proposal key'),
 ('hidden-pose','RemoteMapStory.cs','if (w == null || !w.IsOpen)','if (w == null)','hidden native preflight has no transport pose'),
 ('missing-float-gate','RemoteMapStory.cs','if (!ModalFallback.TryGetGrabFor(w, out GrabbableModal? grab) || grab == null)','if (object.ReferenceEquals(null,new object()))','unfloated native preflight has no transport pose'),
 ('translated-event-key','RemoteMapStory.cs','NetProtocol.HashMapKey(id!)','NetProtocol.HashMapKey(panel.LocalisedTitle)','actual event uses original content identity rather than old proposal')]
 for name,file,needle,replacement,expected in mutations:
  for path,text in originals.items():path.write_text(text)
  path=source/file;text=path.read_text();assert text.count(needle)==1,(name,'mutation target');path.write_text(text.replace(needle,replacement))
  subprocess.run([sys.executable,str(fixture/'extract.py'),str(source),str(project/'Production.cs')],check=True)
  shutil.copy2(project/'Production.cs',run/(name+'.cs'))
  result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);(run/(name+'.log')).write_text(result.stdout)
  if result.returncode==0 or expected not in result.stdout:raise RuntimeError('Negative control failed at wrong boundary: '+name+'\n'+result.stdout)
  message='Shared city identity negative control: '+name+' rejected.'
  with log.open('a') as stream:stream.write(message+'\n')
  print(message,flush=True)
 for path,text in originals.items():path.write_text(text)
 (run/'result.json').write_text(json.dumps({'assertions':assertions,'negative_controls':[row[0] for row in mutations],'source_bindings':bindings,'helper_key_generation_covered_here':False,'hardware_verified':False},indent=2)+'\n')
 status=0
finally:
 spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
 receipt=retention.cleanup_completed_run('shared-city-identity',status,log,root,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
PY
