#!/usr/bin/env bash
set -euo pipefail
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 - "$repo_root" <<'PY'
from pathlib import Path
import hashlib,importlib.util,json,re,shutil,subprocess,sys,tempfile
root=Path(sys.argv[1]);out=root/'.planning/debug/ready-phase-owner';out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir();source=run/'sources';source.mkdir()
fixture=root/'tests/GloomhavenVR.ReadyPhaseOwnerTests'
for path in fixture.iterdir():
 if path.is_file():shutil.copy2(path,project/path.name)
bindings=[]
for name,folder in [('LoadoutConfirmPark.cs','Composites'),('ModalFallback.7.Close.cs','Modal'),('ModalFallback.CompositeTransfer.cs','Modal')]:
 original=root/'src/GloomhavenVR/WorldUI'/folder/name;target=source/name;shutil.copy2(original,target)
 bindings.append({'source':str(original),'snapshot':str(target),'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
unity_original=root/'tests/GloomhavenVR.MapReadyHandoffTests/UnityBoundary.cs';unity=project/'UnityBoundary.cs';shutil.copy2(unity_original,unity)
bindings.append({'source':str(unity_original),'snapshot':str(unity),'sha256':hashlib.sha256(unity.read_bytes()).hexdigest()})
(run/'source-bindings.json').write_text(json.dumps(bindings,indent=2)+'\n')
command=['dotnet','run','--project',str(project/'GloomhavenVR.ReadyPhaseOwnerTests.csproj'),'-c','Release','--property:FixtureDir='+str(project),'--property:ProductionSource='+str(project/'Production.cs'),'--property:UnityBoundary='+str(unity)]
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True);status=1
try:
 subprocess.run([sys.executable,str(fixture/'extract.py'),str(source),str(project/'Production.cs')],check=True)
 result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);log.write_text(log.read_text()+result.stdout);print(result.stdout,flush=True)
 if result.returncode:raise SystemExit(result.returncode)
 assertions=int(re.search(r'Ready phase owner: (\d+) assertions',result.stdout).group(1))
 shutil.copy2(project/'Production.cs',run/'executed-production.cs')
 assembly=run/'executed-assembly';assembly.mkdir()
 for item in (project/'bin/Release/net8.0').iterdir():
  if item.is_file():shutil.copy2(item,assembly/item.name)
 originals={path:path.read_text() for path in source.iterdir()}
 mutations=[
 ('cross-phase-group','ModalFallback.7.Close.cs','ready.readyUpToggleState != EReadyUpToggleStates.Quests','false','only Quests groups with a visible quest card'),
 ('cross-phase-loadout','LoadoutConfirmPark.cs','toggle.readyUpToggleState != EReadyUpToggleStates.NotSet','toggle.readyUpToggleState == EReadyUpToggleStates.Quests','loadout cannot steal another native ready phase'),
 ('missing-loadout-handoff','LoadoutConfirmPark.cs','if (nativeWindow == null || !ModalFallback.ReleaseForComposite(nativeWindow))','if (nativeWindow == null)','old standalone ready capture retires before loadout adoption'),
 ('user-close-handoff','ModalFallback.CompositeTransfer.cs','if (wp.UserClosing)','if (ReferenceEquals(wp, null))','user close retains native ownership until it finishes')]
 for name,file,needle,replacement,expected in mutations:
  for path,text in originals.items():path.write_text(text)
  path=source/file;text=path.read_text();assert text.count(needle)==1,(name,'mutation target');path.write_text(text.replace(needle,replacement))
  subprocess.run([sys.executable,str(fixture/'extract.py'),str(source),str(project/'Production.cs')],check=True)
  shutil.copy2(project/'Production.cs',run/(name+'.cs'))
  result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);(run/(name+'.log')).write_text(result.stdout)
  if result.returncode==0 or expected not in result.stdout:raise RuntimeError('Negative control failed at wrong boundary: '+name+'\n'+result.stdout)
  message='Ready phase owner negative control: '+name+' rejected.'
  with log.open('a') as stream:stream.write(message+'\n')
  print(message,flush=True)
 for path,text in originals.items():path.write_text(text)
 (run/'result.json').write_text(json.dumps({'assertions':assertions,'negative_controls':[row[0] for row in mutations],'source_bindings':bindings,'hardware_verified':False},indent=2)+'\n')
 status=0
finally:
 spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
 receipt=retention.cleanup_completed_run('ready-phase-owner',status,log,root,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
PY
