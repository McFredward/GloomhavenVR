#!/usr/bin/env bash
set -euo pipefail
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 - "$repo_root" "${1:-$repo_root/src/GloomhavenVR/WorldUI/Surfaces/DialogSurface.cs}" <<'PY'
from pathlib import Path
import hashlib,importlib.util,json,os,shutil,subprocess,sys,tempfile
root,dialog=map(Path,sys.argv[1:]);out=root/'.planning/debug/dialog-surface';out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));print('Evidence: '+str(run),flush=True)
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');status=1
world=root/'src/GloomhavenVR/WorldUI';fixture=root/'tests/GloomhavenVR.DialogSurfaceTests'
files={'DialogSource':dialog,'OwnerSource':world/'Composites/ConfirmationOnOwner.cs','OriginSource':world/'Composites/ConfirmationOnOwner.Origins.cs','LayerSource':world/'Composites/MovedSubtreeLayers.cs'}
originals={key:path.read_text() for key,path in files.items()}
bindings={str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in (*files.values(),*fixture.glob('*.cs'),*fixture.glob('*.csproj'))}
(run/'source-bindings.json').write_text(json.dumps(bindings,indent=2)+'\n')
cases=[('production',{},None)]
for name,before,after,expected in [
 ('scenario-only','bool inRoom = VRModeStateMachine.TableInFrontOfPlayer','bool inRoom = Choreographer.s_Choreographer != null && VRModeStateMachine.TableInFrontOfPlayer','already-open map confirmation converts without scenario choreographer'),
 ('edge-only','if (_pendingShow || _panel == null)','if (_pendingShow)','already-open map confirmation converts without scenario choreographer'),
 ('no-fallback','FallbackWindow = box.GetComponent<UIWindow>();','FallbackWindow = null;','failed dedicated conversion publishes exact original fallback window'),
 ('no-release','CanvasConversion.Release(_panel);','{}','desktop rescue releases only presentation'),
 ('native-hide','private void ReleasePanel()\n    {','private void ReleasePanel()\n    {\n        if (_attached != null) _attached.CurrentBox.IsOpen = false;','desktop rescue releases only presentation'),
 ('no-retry','_conversionFailure = null;','{}','failed opening transfers ownership once without per-frame retry or log flood'),
 ('standalone-backdrop-retained','transparentBackground: true','transparentBackground: false','standalone confirmation removes only full-frame backdrops before reveal and allocation'),
 ('standalone-capture-bypassed','useModLayer: true','useModLayer: false','standalone confirmation admits isolated filtered capture and scoped blur treatment'),
]:
 assert before in originals['DialogSource'],'Mutation binding drift: '+name
 cases.append((name,{'DialogSource':originals['DialogSource'].replace(before,after)},expected))
for name,key,before,after,expected in [
 ('double-capture','OwnerSource','if (window == null || !ModalFallback.ReleaseForComposite(window)) return false;','if (window == null) return false;','late semantic adoption must release the old standalone capture before recording native home'),
 ('stale-producer','OriginSource','Requests.Remove(box);','if (CurrentScope != null) Requests.Remove(box);','unowned reuse of a pooled dialog must retain standalone fallback instead of stale level-up ownership'),
 ('lost-home','OwnerSource','rect.SetParent(seat.Home != null ? seat.Home : null, worldPositionStays: false);','// original parent restore omitted','native hide must restore the original confirmation parent without a lingering claim'),
 ('collapsed-frame','OwnerSource','rect.sizeDelta = seat.Frame;','rect.sizeDelta = Vector2.zero;','centering must preserve measured native frame, not collapse stretched anchors'),
 ('persistent-owner-tier-lost','OwnerSource','sortingOrder: panel.BaseSortingOrder + 1','sortingOrder: 0','persistent confirmation starts above its owner inside the native depth tie'),
 ('persistent-backdrop-retained','OwnerSource','transparentBackground: true','transparentBackground: false','persistent confirmation removes only full-frame backdrops before reveal and allocation'),
 ('persistent-capture-bypassed','OwnerSource','useModLayer: true','useModLayer: false','persistent confirmation admits isolated filtered capture and scoped blur treatment'),
 ('persistent-lease-not-retired','OwnerSource','if (PanelAlive(seat.PersistentMember)) return false;','if (seat.PersistentMember.IsAlive) return false;','lost level-up owner restores persistent presentation without native cancellation'),
]:
 assert before in originals[key],'Mutation binding drift: '+name
 cases.append((name,{key:originals[key].replace(before,after)},expected))
try:
 source=(world/'Modal/ModalFallback.10.CatchAll.cs').read_text();start=source.index('private static void TickCatchAll(')
 assert source.index('AddPollWindow(Surfaces.DialogSurface.FallbackWindow);',start)<source.index('if (UnknownShown.Count == 0)',start),'Failed dedicated confirmation must reach fallback before empty-census return'
 for name,changes,expected in cases:
  case=run/name;project=case/'project';project.mkdir(parents=True)
  for original in fixture.iterdir():
   if original.suffix in ('.cs','.csproj'):shutil.copy2(original,project/original.name)
  paths={}
  for key,text in originals.items():
   path=case/(key+'.fixture');path.write_text(changes.get(key,text));paths[key]=path
  command=['dotnet','run','--project',str(project/'GloomhavenVR.DialogSurfaceTests.csproj'),'--configuration','Release',*['--property:'+key+'='+str(value) for key,value in paths.items()]]
  result=subprocess.run(command,capture_output=True,text=True);(case/'result.log').write_text(result.stdout+result.stderr)
  executed=case/'executed-assemblies';executed.mkdir()
  for artifact in (project/'bin/Release/net8.0').glob('*'):
   if artifact.is_file():shutil.copy2(artifact,executed/artifact.name)
  (case/'executed-pins.json').write_text(json.dumps({p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in executed.iterdir()},indent=2)+'\n')
  if expected is None:
   print(result.stdout,end='',flush=True)
   if result.returncode:raise RuntimeError('Production dialog fixture failed: '+str(case/'result.log'))
  elif result.returncode==0 or expected not in result.stdout+result.stderr:
   raise RuntimeError('Defect control escaped or failed at an unrelated boundary: '+str(case/'result.log'))
  else:print('Dialog/confirmation negative control: '+name+' rejected.',flush=True)
  with log.open('a') as stream:stream.write(result.stdout+result.stderr+'\n')
 status=0
finally:
 spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
 receipt=retention.cleanup_completed_run('dialog-surface',status,log,root,process_reaped=True)
 (run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
PY
