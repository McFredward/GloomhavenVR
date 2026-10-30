#!/usr/bin/env bash
set -euo pipefail
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 - "$repo_root" <<'PY'
from pathlib import Path
import hashlib,importlib.util,json,re,shutil,subprocess,sys,tempfile
root=Path(sys.argv[1]);out=root/'.planning/debug/city-ready-roster';out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir();source=run/'sources';source.mkdir()
fixture=root/'tests/GloomhavenVR.CityReadyRosterTests'
for path in fixture.iterdir():
 if path.is_file():shutil.copy2(path,project/path.name)
bindings=[]
for name in ('MapQuestReadyRoster.cs','MapTravelConfirm.cs'):
 original=root/'src/GloomhavenVR/WorldUI/MapRoom'/name;target=source/name;shutil.copy2(original,target)
 bindings.append({'source':str(original),'snapshot':str(target),'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
original=root/'tests/GloomhavenVR.MapReadyHandoffTests/UnityBoundary.cs';unity=project/'UnityBoundary.cs';text=original.read_text()
# Expand the existing bounded Unity API only for this coordinator's missing APIs.
# These remain fixture boundaries, not substitutions in any production method.
changes=[
 ('public sealed class CanvasRenderer : Component { public bool cull; }','public sealed class CanvasRenderer : Component { public bool cull;public float GetInheritedAlpha(){float a=1;for(Transform? t=transform;t!=null;t=t.parent){CanvasGroup? g=t.GetComponent<CanvasGroup>();if(g!=null)a*=g.alpha;}return a;} }'),
 ('public class Graphic : UnityEngine.Behaviour { public UnityEngine.RectTransform rectTransform','public class Graphic : UnityEngine.Behaviour { public UnityEngine.CanvasRenderer canvasRenderer=>GetComponent<UnityEngine.CanvasRenderer>()??gameObject.AddComponent<UnityEngine.CanvasRenderer>();public UnityEngine.RectTransform rectTransform'),
 ('public override string ToString()=>$"({x},{y})";', 'public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y;public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b);public override bool Equals(object? o)=>o is Vector2 b&&this==b;public override int GetHashCode()=>HashCode.Combine(x,y);public override string ToString()=>$"({x},{y})";')]
for needle,replacement in changes:
 assert text.count(needle)==1,needle;text=text.replace(needle,replacement)
unity.write_text(text);shutil.copy2(original,run/'unity-boundary-original.cs')
bindings.append({'source':str(original),'snapshot':str(unity),'original_sha256':hashlib.sha256(original.read_bytes()).hexdigest(),'executed_sha256':hashlib.sha256(unity.read_bytes()).hexdigest(),'fixture_api_expansions':len(changes)})
(run/'source-bindings.json').write_text(json.dumps(bindings,indent=2)+'\n')
command=['dotnet','run','--project',str(project/'GloomhavenVR.CityReadyRosterTests.csproj'),'-c','Release','--property:FixtureDir='+str(project),'--property:ProductionSource='+str(project/'Production.cs')]
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True);status=1
try:
 subprocess.run([sys.executable,str(fixture/'extract.py'),str(source),str(project/'Production.cs')],check=True)
 result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);log.write_text(log.read_text()+result.stdout);print(result.stdout,flush=True)
 if result.returncode:raise SystemExit(result.returncode)
 assertions=int(re.search(r'City ready roster: (\d+) assertions',result.stdout).group(1))
 shutil.copy2(project/'Production.cs',run/'executed-production.cs')
 assembly=run/'executed-assembly';assembly.mkdir()
 for item in (project/'bin/Release/net8.0').iterdir():
  if item.is_file():shutil.copy2(item,assembly/item.name)
 originals={path:path.read_text() for path in source.iterdir()}
 mutations=[
 ('stale-phase-offer','MapQuestReadyRoster.cs','&& IsCurrentSite((ReadyRosterSite)i, _offerConfirm[i])','&& true','stale previous-phase offer cannot steal city row'),
 ('self-measured-city-row','MapQuestReadyRoster.cs','TryInkBounds(win, confirmRect, rowRect, out confirmInk, out confirmCount)','TryInkBounds(win, confirmRect, out confirmInk, out confirmCount)','city same-root measurement cannot chase its own row upward'),
 ('city-reservation','MapQuestReadyRoster.cs','_row != null && _site == ReadyRosterSite.Quest ? _reserved : 0f','_row != null ? _reserved : 0f','city row never offsets quest confirmation'),
 ('counterfeit-ready','MapQuestReadyRoster.cs','(confirm != null && !ReferenceEquals(toggle.gameObject, confirm))','(confirm != null && ReferenceEquals(null,new object()))','same component type cannot counterfeit native ready identity'),
 ('missing-third-site','MapQuestReadyRoster.cs','private const int SiteCount = 3;','private const int SiteCount = 2;','city ready-up adopts native readiness row into same shared window')]
 for name,file,needle,replacement,expected in mutations:
  for path,text in originals.items():path.write_text(text)
  path=source/file;text=path.read_text();assert text.count(needle)==1,(name,'mutation target');path.write_text(text.replace(needle,replacement))
  subprocess.run([sys.executable,str(fixture/'extract.py'),str(source),str(project/'Production.cs')],check=True)
  shutil.copy2(project/'Production.cs',run/(name+'.cs'))
  result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);(run/(name+'.log')).write_text(result.stdout)
  if result.returncode==0 or expected not in result.stdout:raise RuntimeError('Negative control failed at wrong boundary: '+name+'\n'+result.stdout)
  message='City ready roster negative control: '+name+' rejected.'
  with log.open('a') as stream:stream.write(message+'\n')
  print(message,flush=True)
 for path,text in originals.items():path.write_text(text)
 (run/'result.json').write_text(json.dumps({'assertions':assertions,'negative_controls':[row[0] for row in mutations],'source_bindings':bindings,'prefloat_native_bar_tested':True,'capture_or_hardware_verified':False},indent=2)+'\n')
 status=0
finally:
 spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
 receipt=retention.cleanup_completed_run('city-ready-roster',status,log,root,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
PY
