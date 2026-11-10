#!/usr/bin/env python3
"""Execute production multiplayer cheats and exact native queue method excerpts.

The original QueueUpAction/TryProcessNextAction/FinishProcessingAction,
AutoExecuteServerAuthGameAction/ForwardGameActionToClients and Execute bodies run
in CLR. Native model setters, socket/event, coroutine scheduling, configuration
and native singleton fields are explicit fixture boundaries. Capability ingress
executes the complete production RaiseReceived body, exact driver MsgExtras case
and original VersionGuard registry methods; decode/fragment/socket ports are
explicit and do not claim binary codec or Photon acceptance. All production
sources stay linked unchanged; negative controls must compile and fail for their
causal assertion. No headset or native rule-engine acceptance is claimed.
"""
from pathlib import Path
import argparse,hashlib,json,os,re,shutil,subprocess,sys,tempfile,time
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
 'ingress-version-only':('CapabilityIngress.cs','WorldUI.MultiplayerCheats.NoteVersionReceipt(senderId, version.ModBuild);','', 'actual standalone version ingress grants current native identity receipt'),
 'ingress-sparse-version':('CapabilityIngress.cs','if (extras.HasModVersion)\n                        WorldUI.MultiplayerCheats.NoteVersionReceipt(senderId, extras.ModBuild);','WorldUI.MultiplayerCheats.NoteVersionReceipt(senderId, extras.HasModVersion ? extras.ModBuild : (ushort)0);', 'actual sparse extras retain the prior explicit native identity receipt'),
}
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def method(text,signature):
 start=text.index(signature);brace=text.index('{',start);depth=1;end=brace+1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[start:end]
def capability_ingress():
 net=ROOT/'src/GloomhavenVR/Net';paths=[net/'FfsNetTransport.cs',net/'Avatar/NetAvatarDriver.cs',net/'VersionGuard.cs',net/'NetProtocol.cs']
 transport,avatar,version,protocol=[p.read_text() for p in paths]
 receive=method(transport,'    private void RaiseReceived(')
 start=avatar.index('            case NetProtocol.MsgExtras:');end=avatar.index('\n                break;',start)+len('\n                break;');case=avatar[start:end]
 registry='\n'.join(method(version,sig) for sig in ['    internal static void NotePacket(','    internal static void NoteExtras('])
 registry+='\n'+re.search(r'    internal static int PeerBuild\(int playerId\) =>[^;]+;',version).group(0)
 names=sorted(set(re.findall(r'NetProtocol\.(\w+)',receive+case)))
 constants='\n'.join(re.search(r'^    (?:public|internal) const [^;\n]+\b'+name+r'\s*=[^;]+;',protocol,re.M).group(0) for name in names)
 generated='using System;using GloomhavenVR.Core;namespace GloomhavenVR.Net {\ninternal static partial class NetProtocol {\n'+constants+'\n}\ninternal static partial class VersionGuard {\n'+registry+'\n}\ninternal sealed partial class FfsNetTransport {\n'+receive+'\n}\ninternal sealed partial class NetAvatarDriver {\ninternal void ReceiveExtrasFixture(int senderId,byte[] buffer,int length) { bool parsed=false;switch(NetPacket.PeekType(buffer,length)) {\n'+case+'\n}LastParsed=parsed;}\n}\n}\n'
 bindings={'schema':'multiplayer-cheat-capability-ingress-projection-v1','source_files':{str(p.relative_to(ROOT)):sha(p) for p in paths},
  'exact_projections':{'FfsNetTransport.RaiseReceived':hashlib.sha256(receive.encode()).hexdigest(),'NetAvatarDriver.MsgExtrasCase':hashlib.sha256(case.encode()).hexdigest(),'VersionGuard.registry':hashlib.sha256(registry.encode()).hexdigest(),'NetProtocol.used_constants':hashlib.sha256(constants.encode()).hexdigest()},
  'generated_sha256':hashlib.sha256(generated.encode()).hexdigest(),'ports':['Decoded PresenceState and version-only/batch recognition supplied by explicit decode ports; real codec is unchanged and not proved here','Fragment assembly, sockets and native presentation drawing are unexercised ports','Only driver MsgExtras case is projected; surrounding switch/test receiver wrapper is explicit, not a replacement claimed as full OnPacketReceived proof','Complete production RaiseReceived, including its native guard, batch recursion, early return and fallback dispatch, is compiled without edits']}
 return generated,paths,bindings
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
 ingress,projected,projection=capability_ingress();inputs.extend(projected)
 text=gate.read_text();signature='    internal static void OverrideRequirements(';begin=text.index(signature);end=text.index('\n    }',begin)+6
 shared_signature='    internal static void SetFromSharedHost(';shared_begin=text.index(shared_signature);shared_end=text.index('\n    }',shared_begin)+6
 gate_method='using System; using MapRuleLibrary.MapState; using GloomhavenVR.Core; namespace GloomhavenVR.WorldUI.Patches; internal static partial class ScenarioGateCheat {\n'+text[shared_begin:shared_end]+'\n'+text[begin:end]+'\n}\n'
 pins={str(p.relative_to(ROOT)):sha(p) for p in inputs};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
 (run/'ingress-projection-bindings.json').write_text(json.dumps(projection,indent=2)+'\n')
 originals=run/'originals';originals.mkdir()
 for p in projected:shutil.copyfile(p,originals/p.name)
 try:
  variants=['production']+([] if args.production_only else list(MUTATIONS))
  for variant in variants:
   case=run/variant;case.mkdir();source=case/'Sources';source.mkdir()
   for p in inputs:
    if p.suffix=='.cs' and p!=gate and p not in projected:shutil.copyfile(p,source/p.name)
   (source/'GateMethods.cs').write_text(gate_method)
   (source/'CapabilityIngress.cs').write_text(ingress)
   if variant!='production':
    name,old,new,expected=MUTATIONS[variant];p=source/name;text=p.read_text();assert old in text,variant;p.write_text(text.replace(old,new))
   project=case/'Cheats.csproj';project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649;CS0414;CS0162</NoWarn></PropertyGroup></Project>')
   env=dict(os.environ);env['PATH']=str(Path(env.get('DOTNET_ROOT',Path.home()/'.dotnet')))+os.pathsep+env.get('PATH','');env['UseSharedCompilation']='false';env['MSBUILDDISABLENODEREUSE']='1'
   build=subprocess.run(['dotnet','build',str(project),'--configuration','Release','-v:q','--nologo'],cwd=ROOT,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT);(case/'build.log').write_text(build.stdout)
   if build.returncode:raise RuntimeError('Variant failed to compile: '+variant+'\n'+build.stdout)
   execute=subprocess.run(['dotnet',str(case/'bin/Release/net8.0/Cheats.dll')],cwd=ROOT,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT);(case/'runtime.log').write_text(execute.stdout)
   retained=case/'executed-binaries';shutil.copytree(case/'bin/Release/net8.0',retained)
   (case/'artifact-manifest.json').write_text(json.dumps({str(p.relative_to(case)):sha(p) for p in sorted([*source.glob('*.cs'),*retained.iterdir()]) if p.is_file()},indent=2)+'\n')
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
