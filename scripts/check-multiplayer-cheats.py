#!/usr/bin/env python3
"""Execute production multiplayer cheats and exact native queue method excerpts.

The original QueueUpAction/TryProcessNextAction/FinishProcessingAction,
AutoExecuteServerAuthGameAction/ForwardGameActionToClients and Execute bodies run
in CLR. Native model setters, socket/event, coroutine scheduling, configuration
and native singleton fields are explicit fixture boundaries. No Photon/headset
or native rule-engine acceptance is claimed. All production sources stay linked
unchanged; negative controls must compile and fail for their causal assertion.
"""
from pathlib import Path
import argparse,hashlib,json,os,shutil,subprocess,sys,tempfile,time
from town_run_retention import cleanup_completed_run
ROOT=Path(__file__).resolve().parents[1]
PRODUCTION=['MultiplayerCheats.cs','MultiplayerCheatCommand.cs','NativeCheatQueue.cs',
            'MultiplayerCheatActionPatch.cs','MultiplayerCheatQueueGuard.cs']
MUTATIONS={
 'host':('MultiplayerCheats.cs','if (!FFSNetwork.IsHost) return "cheat_mp_host";','', 'enqueue refusal: guest'),
 'roster':('MultiplayerCheats.cs','if (player != local && !NativeCheatQueue.HasCurrentVersion(player)) return false;','if (false) return false;', 'enqueue refusal: unmodded'),
 'late-join':('NativeCheatQueue.cs','Joining.GetValue(null) is ICollection joins && joins.Count > 0','false', 'enqueue refusal: join'),
 'private-replica-gate':('MultiplayerCheats.cs','else if (command!.Sequence <= _appliedSequence) return true;','else if (!VROptionsTab.CheatsEnabledNow) return false;\n        else if (command!.Sequence <= _appliedSequence) return true;', 'accepted replica retains original validity'),
 'exact-character':('MultiplayerCheats.cs','c.CharacterID == command.Character && c.CharacterName == command.CharacterName','c.CharacterID == command.Character', 'host gold accepted for queue'),
 'invalid-forward':('MultiplayerCheatActionPatch.cs','if (!MultiplayerCheats.Execute(__instance)) ___isValid = false;','MultiplayerCheats.Execute(__instance);', 'changed command rejected before apply: disabled'),
 'client-save':('MultiplayerCheats.cs','if (FFSNetwork.IsHost && command.Kind is MultiplayerCheatKind.Gold or MultiplayerCheatKind.Unlock)','if (command.Kind is MultiplayerCheatKind.Gold or MultiplayerCheatKind.Unlock)', 'replica never writes a private save'),
 'requirements-shared':('GateMethods.cs','Online(out bool certain) && !MultiplayerCheats.SharedRequirementsAllowed','Online(out bool certain)', 'shared native requirements override reaches the real result'),
 'storage-forward':('MultiplayerCheats.cs','catch (Exception ex)\n            {\n                // Do not stop native forwarding','catch (Exception ex)\n            {\n                throw;\n                // Do not stop native forwarding', 'save exception cannot split the admitted native commit'),
 'native-bound':('MultiplayerCheats.cs','internal const int NativeAction = 127;','internal const int NativeAction = 60124;', 'host gold accepted for queue'),
 'issued-ownership':('MultiplayerCheats.cs','_pending = null; _pendingCohort = null; _appliedSequence = 0;','_pending = null; _issuedBytes = null; _pendingCohort = null; _appliedSequence = 0;', 'context reset preserves issued ownership until exact stale head withdrawal'),
 'native-failure':('MultiplayerCheatQueueGuard.cs','FFSNetwork.HandleDesync(ex);','_ = ex;', 'accepted receiver model failure uses original native desync and retires its exact head'),
 'pre-dequeue':('MultiplayerCheatQueueGuard.cs','if (MultiplayerCheats.WaitForReveal(head))','if (false)', 'busy reveal stays at original head before dequeue'),
 'receiver-permit':('MultiplayerCheatQueueGuard.cs','if (FFSNetwork.IsHost) return true;','return true;', 'accepted receiver command applies in native one-action mode'),
 'host-permit':('MultiplayerCheatQueueGuard.cs','if (MultiplayerCheats.IsOwnedPendingHostAction(head)','if (false', 'only following original choice consumes native one-action permit'),
 'receiver-reentrant':('MultiplayerCheatQueueGuard.cs','if (_applyingReceiver) { __result = false; return false; }','', 'receiver native model callback cannot process a successor reentrantly'),
}
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/multiplayer-cheats-runtime');ap.add_argument('--production-only',action='store_true');args=ap.parse_args()
 args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()));print('evidence: '+str(run),flush=True)
 completion=run/'completion.log';completion.write_text('evidence: '+str(run)+'\n');code=1;results=[];start=time.monotonic()
 native_manifest=ROOT/'scripts/multiplayer-cheats-runtime/native-excerpt-manifest.json'
 native=json.loads(native_manifest.read_text())
 for name,h in native['native_binaries'].items():assert sha(ROOT/'ressources/GH_Data/Managed'/name)==h,name
 for name,h in native['fixture_sha256'].items():assert sha(ROOT/'scripts/multiplayer-cheats-runtime'/name)==h,name
 inputs=[ROOT/'src/GloomhavenVR/WorldUI/Options'/n for n in PRODUCTION]+list((ROOT/'scripts/multiplayer-cheats-runtime').glob('*.cs'))+[Path(__file__).resolve(),native_manifest]
 gate=ROOT/'src/GloomhavenVR/WorldUI/Patches/ScenarioGateCheat.cs'
 inputs.append(gate)
 text=gate.read_text();signature='    internal static void OverrideRequirements(';begin=text.index(signature);end=text.index('\n    }',begin)+6
 shared_signature='    internal static void SetFromSharedHost(';shared_begin=text.index(shared_signature);shared_end=text.index('\n    }',shared_begin)+6
 gate_method='using System; using MapRuleLibrary.MapState; using GloomhavenVR.Core; namespace GloomhavenVR.WorldUI.Patches; internal static partial class ScenarioGateCheat {\n'+text[shared_begin:shared_end]+'\n'+text[begin:end]+'\n}\n'
 pins={str(p.relative_to(ROOT)):sha(p) for p in inputs};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
 try:
  variants=['production']+([] if args.production_only else list(MUTATIONS))
  for variant in variants:
   case=run/variant;case.mkdir();source=case/'Sources';source.mkdir()
   for p in inputs:
    if p.suffix=='.cs' and p!=gate:shutil.copyfile(p,source/p.name)
   (source/'GateMethods.cs').write_text(gate_method)
   if variant!='production':
    name,old,new,expected=MUTATIONS[variant];p=source/name;text=p.read_text();assert old in text,variant;p.write_text(text.replace(old,new))
   project=case/'Cheats.csproj';project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649;CS0414;CS0162</NoWarn></PropertyGroup></Project>')
   env=dict(os.environ);env['PATH']=str(Path(env.get('DOTNET_ROOT',Path.home()/'.dotnet')))+os.pathsep+env.get('PATH','');env['UseSharedCompilation']='false';env['MSBUILDDISABLENODEREUSE']='1'
   build=subprocess.run(['dotnet','build',str(project),'--configuration','Release','-v:q','--nologo'],cwd=ROOT,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT);(case/'build.log').write_text(build.stdout)
   if build.returncode:raise RuntimeError('Variant failed to compile: '+variant+'\n'+build.stdout)
   execute=subprocess.run(['dotnet',str(case/'bin/Release/net8.0/Cheats.dll')],cwd=ROOT,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT);(case/'runtime.log').write_text(execute.stdout)
   passed=execute.returncode==0 if variant=='production' else execute.returncode!=0 and expected in execute.stdout
   row={'name':variant,'passed':passed,'exit_code':execute.returncode,'output':execute.stdout};results.append(row)
   print(('PASS ' if passed else 'FAIL ')+variant+': '+execute.stdout.strip(),flush=True)
   if not passed:raise RuntimeError('Variant did not match its exact expectation: '+variant)
  assert all(sha(ROOT/p)==h for p,h in pins.items());code=0
 finally:
  (run/'results.json').write_text(json.dumps({'passed':code==0,'cases':results,'elapsed':time.monotonic()-start,'source_hashes_unchanged':all(sha(ROOT/p)==h for p,h in pins.items()),'limits':__doc__},indent=2)+'\n')
  (run/'cache-cleanup.json').write_text(json.dumps(cleanup_completed_run('multiplayer-cheats-runtime',code,completion,ROOT,process_reaped=True),indent=2)+'\n')
 return code
if __name__=='__main__':sys.exit(main())
