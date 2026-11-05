#!/usr/bin/env python3
"""Execute progression recovery and attention cues with actual Unity geometry/pixels.

Native window/model readiness and transport claims are explicit boundary ports.
The complete new production drivers/policies execute; existing shared-window pose
suite owns actual publication/receipt/election. No headset image is asserted.
"""
import argparse, hashlib, importlib.util, json, os, re, shutil, signal, subprocess, sys, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run

def execute(command,log,timeout=180):
 env=os.environ.copy();env.update(DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1',MSBUILDDISABLENODEREUSE='1',UseSharedCompilation='false')
 with log.open('w') as output:
  p=subprocess.Popen(command,stdout=output,stderr=subprocess.STDOUT,start_new_session=True,env=env)
  try:return p.wait(timeout=timeout)
  finally:
   try:os.killpg(p.pid,signal.SIGKILL)
   except ProcessLookupError:pass
   p.wait()

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT);p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/multiplayer-progression-runtime');p.add_argument('--case',action='append');a=p.parse_args()
 a.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir)).resolve();completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n');status=1
 def interrupted(signum, frame): raise KeyboardInterrupt
 previous={kind:signal.signal(kind,interrupted) for kind in (signal.SIGTERM,signal.SIGINT)}
 try:
  base=a.source_root/'src/GloomhavenVR';fixture=ROOT/'scripts/multiplayer-progression-runtime';sources={}
  paths=['Rig/ScenarioArrivalScale.cs','WorldUI/Modal/StoryRecoveryDwell.cs','WorldUI/Modal/StoryRecoveryFlight.cs','WorldUI/Modal/ModalFallback.StoryRecovery.cs','Cards/Caps/RestOfferCue.cs','Net/DecisionWaitClock.cs','Net/Avatar/NetAvatarDriver.DecisionWait.cs','Net/Avatar/DecisionWaitArrow.cs','Core/VRLayers.cs','Core/ModVisualOwnership.cs','Net/SharedWindowMoveRegister.cs','Net/SharedWindowPoseTrack.cs']
  for rel in paths:sources[Path(rel).name]=(base/rel).read_text()
  focus=(base/'Board/FocusCue.cs').read_text();start=focus.index('internal sealed class WorldFrame');sources['WorldFrame.cs']='using UnityEngine; using GloomhavenVR.Net; using GloomhavenVR.Core; namespace GloomhavenVR.Board {\n'+focus[start:]+'\n}'
  grab=(base/'Rig/WorldGrab.cs').read_text()
  def method(text, signature):
   start=text.index(signature); end=text.index('{',start)+1; depth=1
   while depth:
    depth+=(text[end]=='{')-(text[end]=='}');end+=1
   return text[start:end]
  moves=(base/'Net/SharedWindowMoveOrder.cs').read_text();sources['MoveEntry.cs']=moves[:moves.index('/// <summary>Additive123.')]
  poses=(base/'Net/Avatar/AvatarState.cs').read_text();sources['RigPose.cs']='using UnityEngine; namespace GloomhavenVR.Net {\n'+method(poses,'internal struct RigPose')+'\n}'
  modal_grab=(base/'WorldUI/Grab/GrabbableModal.cs').read_text();sources['ModalFrames.cs']='using UnityEngine; namespace GloomhavenVR.WorldUI { internal partial class GrabbableModal {\n'+method(modal_grab,'internal void SnapFrameTo(')+'\n'+method(modal_grab,'internal void ReadRoomMakingStart(')+'\n}}'
  sources['WorldGrab.cs']='using UnityEngine; namespace GloomhavenVR.Rig { internal sealed partial class WorldGrab {\n'+method(grab,'private void TrackRig(')+'\n'+method(grab,'private void Disengage(')+'\n}}'
  pick=(base/'Board/Patches/PickPhaseInitiativeTrack.cs').read_text();start=pick.index('internal static bool IsDecisionWait(');end=pick.index(';',start)+1
  sources['PickDecisionWait.cs']='namespace GloomhavenVR.Board.Patches { internal static class PickPhaseInitiativeTrack {\n'+pick[start:end]+'\n}}'
  # Source contracts bind real callers to the executed components, not just detached helpers.
  assert 'TickStoryRecovery(Time.unscaledTime);' in (base/'WorldUI/Modal/ModalFallback.4.Tick.cs').read_text()
  assert 'public StoryRecoveryFlight? StoryRecoveryFlight;' in (base/'WorldUI/Modal/ModalFallback.3.WindowPanel.cs').read_text()
  recovery=sources['ModalFallback.StoryRecovery.cs'];assert recovery.index('flight.Sample(')<recovery.index('if (now < _nextStoryRecoveryPoll) return;')
  assert 'if (claimed && !_localPoseOwned)' in (base/'Net/Remote/RemoteStorySync.cs').read_text()
  assert 'internal void ReadRoomMakingStart(' in (base/'WorldUI/Grab/GrabbableModal.cs').read_text()
  assert 'internal void SnapFrameTo(' in (base/'WorldUI/Grab/GrabbableModal.cs').read_text()
  assert 'ScenarioArrivalScale.Resolve(savedZoom, Defaults.SavedScaleMultiplier, arrival)' in (base/'Rig/VRRigDriver.cs').read_text()
  assert 'TrackRig(rig);' in (base/'Rig/WorldGrab.cs').read_text()
  rest_local=(base/'Cards/Caps/RestControls.cs').read_text()
  rest_remote=(base/'Net/Remote/RemoteBoardFurniture.cs').read_text()
  assert 'bool requiresRest = hand != null && CardsGameApi.MustRestInsteadOfPlay(hand);' in rest_local
  assert 'RestOfferCue.Wanted(requiresRest, shortVisible' in rest_local
  assert 'bool requiresRest = displayedActor != null && Cards.CardsGameApi.MustRestInsteadOfPlay(displayedActor);' in rest_remote
  assert '_shortRest?.TickRestCue(Cards.RestOfferCue.Wanted(requiresRest,' in rest_remote
  assert 'NetAvatarDriver.DelayedDecisionPlayer() == _owner.PlayerId' in (base/'Net/Remote/RemoteNameTag.cs').read_text()
  controls=[('early-story','StoryRecoveryDwell.cs','now - _since >= 3f','now - _since >= 0f','no premature story recovery'),('observer-recovery','ModalFallback.StoryRecovery.cs','private static bool StoryRecoveryEligible(WindowPanel wp, bool author) => author','private static bool StoryRecoveryEligible(WindowPanel wp, bool author) => true','observer cannot recover'),('ignore-remote-grip','ModalFallback.StoryRecovery.cs','if (online && !SharedWindowMoves.BeginAutomatic(kind)) continue;','if (online && bool.Parse("false")) continue;','held remote claim prevents recovery'),('no-arrival-guard','ScenarioArrivalScale.cs','? fallback : saved','? saved : saved','extreme arrival restores'),('early-player-arrow','DecisionWaitClock.cs','now - _since >= 20f','now - _since >= 0f','single pending decision waits'),('unnecessary-rest-cue','RestOfferCue.cs','requiresRest && offered','true && offered','normal or unresolved hand'),('arrow-default-layer','DecisionWaitArrow.cs','Core.VRLayers.Apply(_root);','','arrow uses the actual dedicated mod layer'),('stale-world-gesture','WorldGrab.cs','Disengage(null);','Disengage(rig);','cross-scene handoff cannot persist old zoom')]
  controls += [
   ('instant-story-flight','StoryRecoveryFlight.cs','float t = Mathf.Clamp01((now - _startedAt) / Seconds);','float t = 1f;','story recovery has a visible finite intermediate pose'),
   ('four-hz-story-flight','ModalFallback.StoryRecovery.cs','StoryRecoveryFlight? flight = wp.StoryRecoveryFlight;','if (now < _nextStoryRecoveryPoll) return; StoryRecoveryFlight? flight = wp.StoryRecoveryFlight;','story flight samples before the four-Hz admission poll'),
   ('flight-local-grip','ModalFallback.StoryRecovery.cs','&& wp.Grab != null && !wp.Grab.IsGrabbed','&& wp.Grab != null && true','local grip interrupts flight at its last visible sample'),
   ('flight-remote-claim','ModalFallback.StoryRecovery.cs','flight.SharedClaim && (!SharedWindowMoves.MayWrite(flight.Kind)\n                    || !SharedWindowMoves.HasLocalClaim(flight.Kind))','false','new remote grip interrupts the actual automatic claim'),
   ('flight-reflow-priority','ModalFallback.StoryRecovery.cs','&& !SharedReflowActive(SharedWindows.KindOf(wp.Window))','&& true','opening room-making wins over an old recovery flight'),
   ('flight-native-close','ModalFallback.StoryRecovery.cs','&& wp.Window != null && wp.Window.IsOpen && ReflowVisible(wp)','&& wp.Window != null && true && ReflowVisible(wp)','native close stops flight without reopening the window'),
   ('flight-panel-replaced','ModalFallback.StoryRecovery.cs','!ReferenceEquals(flight.Panel, wp.Panel)','false','replaced presentation cancels the previous flight'),
   ('flight-transport-transition','ModalFallback.StoryRecovery.cs','flight.SharedClaim != (online && SharedWindowMoves.Active)','false','transport activation cancels an unclaimed old flight'),
   ('flight-no-cadence','ModalFallback.StoryRecovery.cs','flight.SendUntil = now + 2f / NetProtocol.SendRateHz;','flight.SendUntil = now;','active story flight requests the existing fast send gate'),
   ('flight-no-endpoint-tail','ModalFallback.StoryRecovery.cs','flight.SendUntil = now + 2f / NetProtocol.SendRateHz;','flight.SendUntil = now + .001f;','final story endpoint retains two rig intervals to publish'),
   ('flight-permanent-cadence','ModalFallback.StoryRecovery.cs','now < flight.SendUntil','true','finite publication tail returns to idle cadence'),
   ('flight-hidden-cadence','ModalFallback.StoryRecovery.cs','&& StoryRecoveryMayWrite(wp, flight, online, author)','&& true','manual/native/authority interruption revokes fast publication'),
  ]
  variants=[('production','','','','')]+controls
  if a.case:variants=[v for v in variants if v[0] in a.case];assert len(variants)==len(a.case)
  manifest={'result':str(run/'results.txt'),'evidence':str(run),'cases':[]};dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet');unity=Path('/home/claw/unity-2021.3.5/Editor/Unity')
  for name,file,before,after,expected in variants:
   case=run/name;prod=case/'production';prod.mkdir(parents=True)
   for filename,source in sources.items():
    if file==filename:assert source.count(before)==1;source=source.replace(before,after)
    (prod/filename).write_text(source)
   project=case/'Progression.csproj';shutil.copyfile(fixture/'Progression.csproj',project);assembly='MpProgression_'+name.replace('-','_')
   result=execute([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:UseSharedCompilation=false','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(prod),'-p:UnityManaged='+str(unity.parent/'Data/Managed')],case/'build.log')
   if result:raise RuntimeError((case/'build.log').read_text()+'\nCompilation is never an accepted causal control')
   manifest['cases'].append({'name':name,'dll':str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
  project=run/'unity'
  for d in ['Assets/Editor','Packages','ProjectSettings']:(project/d).mkdir(parents=True)
  shutil.copyfile(fixture/'Editor/ProgressionRunner.cs',project/'Assets/Editor/ProgressionRunner.cs')
  shader=a.source_root/'unity/Assets/Shaders/Overlay.shader'
  if not shader.exists():shader=next(a.source_root.glob('unity/**/Overlay.shader'))
  shutil.copyfile(shader,project/'Assets/Overlay.shader')
  (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.modules.imageconversion':'1.0.0','com.unity.modules.physics':'1.0.0'}}))
  (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');mp=run/'manifest.json';mp.write_text(json.dumps(manifest,indent=2))
  bound=[Path(__file__),*fixture.rglob('*')];bound+=[base/rel for rel in paths]+[base/rel for rel in ['Rig/WorldGrab.cs','Rig/VRRigDriver.cs','WorldUI/Modal/ModalFallback.4.Tick.cs','WorldUI/Modal/ModalFallback.3.WindowPanel.cs','WorldUI/Grab/GrabbableModal.cs','Net/Remote/RemoteStorySync.cs','Net/SharedWindowMoveOrder.cs','Net/Avatar/AvatarState.cs','Cards/Caps/RestControls.cs','Net/Remote/RemoteBoardFurniture.cs','Net/Remote/RemoteNameTag.cs','Board/Patches/PickPhaseInitiativeTrack.cs','Board/FocusCue.cs']]+[shader]
  (run/'source-hashes.json').write_text(json.dumps({str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in bound if path.is_file()},indent=2))
  command=[str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','ProgressionRunner.Start','-progressionManifest',str(mp),'-logFile',str(run/'unity.log')]
  if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
  code=execute(command,run/'unity-launch.log',240)
  result=run/'results.txt'
  if result.exists():print(result.read_text(),end='')
  if code or not result.exists():raise RuntimeError('Unity failure: '+str(run/'unity.log'))
  status=0;print('PASS; Evidence: '+str(run))
 finally:
  (run/'result.json').write_text(json.dumps({'exit_code':status,'coverage':'partial' if a.case else 'production-and-'+str(len(controls))+'-controls','limits':__doc__},indent=2))
  receipt=cleanup_completed_run('multiplayer-progression-runtime',status,completion,a.source_root,process_reaped=True);(run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2))
  for kind,old in previous.items():signal.signal(kind,old)
if __name__=='__main__':main()
