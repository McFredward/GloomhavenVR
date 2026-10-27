#!/usr/bin/env bash
set -euo pipefail
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 - "$repo_root" <<'PY'
from pathlib import Path
import hashlib,importlib.util,json,re,shutil,subprocess,sys,tempfile
root=Path(sys.argv[1]);out=root/'.planning/debug/city-ready';out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir()
fixture=root/'tests/GloomhavenVR.CityReadyTests'
for path in fixture.iterdir():
 if path.is_file():shutil.copy2(path,project/path.name)
original=root/'src/GloomhavenVR/WorldUI/MapRoom/MapCityEventReadyUp.cs';production=run/'MapCityEventReadyUp.cs';shutil.copy2(original,production)
binding={'source':str(original),'snapshot':str(production),'sha256':hashlib.sha256(production.read_bytes()).hexdigest()}
(run/'source-binding.json').write_text(json.dumps(binding,indent=2)+'\n')
command=['dotnet','run','--project',str(project/'GloomhavenVR.CityReadyTests.csproj'),'-c','Release','--property:FixtureDir='+str(project),'--property:ProductionSource='+str(production)]
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True);status=1
try:
 result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);log.write_text(log.read_text()+result.stdout);print(result.stdout,flush=True)
 if result.returncode:raise SystemExit(result.returncode)
 assertions=int(re.search(r'City readiness: (\d+) assertions',result.stdout).group(1))
 assembly=run/'executed-assembly';assembly.mkdir()
 for item in (project/'bin/Release/net8.0').iterdir():
  if item.is_file():shutil.copy2(item,assembly/item.name)
 text=production.read_text()
 mutations=[
 ('automatic-consent','_controller!.ToggleReadyUpUI(true, EReadyUpToggleStates.CityEvents);','_controller!.ToggleReadyUpUI(true, EReadyUpToggleStates.CityEvents);\n            _controller!.OnSelectedCityEvent();','presentation must never consent automatically'),
 ('disabled-input','return MatchesNativeProposal() && __instance.IsInteractable && __instance.ShouldBeVisible;','return true;','disabled observer cannot vote through gamepad or VR input'),
 ('native-blocker','&& __instance._visibilityRequests.Count == 0','&& true','native visibility blocker outranks shared observation'),
 ('local-withdrawal','if (FFSNetwork.IsHost)\n                    Reset();','if (FFSNetwork.IsOnline)\n                    Reset();','client unready must not withdraw host proposal'),
 ('action-identity','hash = SharedMapRunIdentity.Add(hash, actionId.ToString(CultureInfo.InvariantCulture));','hash = SharedMapRunIdentity.Add(hash, "constant");','new city occurrence cannot reuse old pose key'),
 ('singleton-replacement','|| !ReferenceEquals(_toggle, Singleton<UIReadyToggle>.Instance)','|| false','replacement native ready singleton cannot inherit old city lease'),
 ('all-ready','MatchesNativeRole() && !_toggle!._allPlayersReady','MatchesNativeRole()','native all-ready gate suppresses observation immediately'),
 ('native-cancel-seam','private static void BeforeHostCancellation() => Reset();','private static void BeforeHostCancellation() { }','exact native retirement seam clears city lease: BeforeHostCancellation'),
 ('native-target-registration','[HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Initialize))]','[HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Initialize))]\n        [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Reset))]','native retirement has one exact Harmony target: BeforeReadyInitialize'),
 ('unsafe-host-postfix','catch (Exception ex) { ReportFailure("host proposal postfix", ex); }','catch (Exception ex) { GC.KeepAlive(ex); throw; }','native host proposal postfix continues after failed native evidence: map-state'),
 ('unsafe-observer-getter','ReportFailure("observer visibility", ex);','ReportFailure("observer visibility", ex); throw;','native visibility getter survives failed city observation'),
 ('failed-input-open','ReportFailure("observer input permission", ex);\n                return false;','ReportFailure("observer input permission", ex);\n                return true;','failed exact-city observer permission must deny input'),
 ('unsafe-reset','catch (Exception ex) { ReportFailure("reset visibility", ex); }','catch (Exception ex) { GC.KeepAlive(ex); throw; }','native retirement continues after failed visibility recompute: BeforeReadyReset'),
 ('lost-permission-retry','Singleton<MapChoreographer>.Instance.DeterminePlayerToggleInteractability();\n                if (_contentKey != key || !ReferenceEquals(_toggle, current))\n                    return; // Native host permission loss can cancel the original city offer.\n                _refreshPermission = false;','_refreshPermission = false;\n                Singleton<MapChoreographer>.Instance.DeterminePlayerToggleInteractability();\n                if (_contentKey != key || !ReferenceEquals(_toggle, current))\n                    return; // Native host permission loss can cancel the original city offer.','failed city presentation retries without replaying native consent'),
 ('unsafe-diagnostic-logger','catch (Exception) { }\n    }\n\n    private static PropertyInfo? ReadNativeStateProperty()','catch (Exception) { throw; }\n    }\n\n    private static PropertyInfo? ReadNativeStateProperty()','failed city diagnostic logger cannot escape native getter'),
 ('focus-steals-city','return !MatchesNativeRole();','return true;','incidental quest focus refresh cannot steal a native city proposal'),
 ('mutable-state-withdrawal','private static bool MatchesNativeProposal() => MatchesNativeRole() && !_toggle!._allPlayersReady;','private static bool MatchesNativeProposal() => MatchesNativeRole() && _controller!.m_State == EReadyUpToggleStates.CityEvents && !_toggle!._allPlayersReady;','mutable native presentation state is not a city withdrawal'),
 ('missing-reentry-repair','if (!_toggle!._requestVisible || _controller!.m_State != EReadyUpToggleStates.CityEvents)','if (!_toggle!._requestVisible)','city reentry repairs controller role even when visibility request already true'),
 ('ack-wait-loses-role','current.UpdateVisiblity();\n                return;','Reset();\n                return;','native city role stays protected throughout ACK wait')]
 for name,needle,replacement,expected in mutations:
  if text.count(needle)!=1:raise RuntimeError('Mutation target is not exact: '+name)
  production.write_text(text.replace(needle,replacement));shutil.copy2(production,run/(name+'.cs'))
  result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);(run/(name+'.log')).write_text(result.stdout)
  if result.returncode==0 or expected not in result.stdout:raise RuntimeError('Negative control failed at wrong boundary: '+name+'\n'+result.stdout)
  message='City readiness negative control: '+name+' rejected.'
  with log.open('a') as stream:stream.write(message+'\n')
  print(message,flush=True)
 production.write_text(text)
 (run/'result.json').write_text(json.dumps({'assertions':assertions,'negative_controls':[row[0] for row in mutations],'source_binding':binding,'hardware_verified':False},indent=2)+'\n')
 status=0
finally:
 spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
 receipt=retention.cleanup_completed_run('city-ready',status,log,root,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
PY
python3 "$repo_root/tests/GloomhavenVR.CityDepartureTests/run.py" "$repo_root"
