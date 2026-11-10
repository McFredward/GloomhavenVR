#!/usr/bin/env python3
"""Replay canceled card work and native UI cancellation through the actual owners."""
import argparse,hashlib,json,os,re,shutil,subprocess,tempfile,time
from pathlib import Path
from town_run_retention import cleanup_completed_run
ROOT=Path(__file__).resolve().parents[1]
def method(text,signature):
 start=text.index(signature);brace=text.index('{',start);depth=1;end=brace+1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[start:end]
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source-root',type=Path,default=ROOT);parser.add_argument('--output-dir',type=Path)
 args=parser.parse_args();root=args.source_root.resolve();out=args.output_dir or root/'.planning/debug/card-progress-lifetime';out.mkdir(parents=True,exist_ok=True)
 run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir();sources=run/'sources';sources.mkdir();log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True)
 env=os.environ.copy();env['DOTNET_ROOT']=str(Path.home()/'.dotnet');env['PATH']=env['DOTNET_ROOT']+os.pathsep+env.get('PATH','');env.update(UseSharedCompilation='false',MSBUILDDISABLENODEREUSE='1',DOTNET_CLI_USE_MSBUILD_SERVER='0')
 status=1;bindings=[];started=time.monotonic()
 def bind(path,dest):
  shutil.copy2(path,dest);bindings.append({'source':str(path),'snapshot':str(dest),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
 try:
  for path in (root/'tests/GloomhavenVR.CardProgressLifetimeTests').iterdir():
   if path.is_file():bind(path,project/path.name)
  queue=sources/'CardActionQueue.cs';lifetime=sources/'NativeCardAnimationLifetime.cs';progress=sources/'CardsDriver.ProgressHelp.cs'
  bind(root/'src/GloomhavenVR/Cards/CardActionQueue.cs',queue);bind(root/'src/GloomhavenVR/Cards/Patches/NativeCardAnimationLifetime.cs',lifetime);bind(root/'src/GloomhavenVR/Cards/Driver/CardsDriver.ProgressHelp.cs',progress)
  clock=sources/'DecisionHelpClock.cs';bind(root/'src/GloomhavenVR/Cards/DecisionHelpClock.cs',clock)
  helploc=sources/'Loc.ProgressHelp.cs';bind(root/'src/GloomhavenVR/Core/Loc/Loc.ProgressHelp.cs',helploc)
  defaults=sources/'Defaults.Cards.cs';bind(root/'src/GloomhavenVR/Defaults/Defaults.Cards.cs',defaults)
  constants='\n'.join(line for line in defaults.read_text().splitlines() if re.search(r'const .* (ProgressHelpMode|ProgressHelpDelaySeconds) =',line))
  assert constants.count('internal const ')==2
  (project/'HelpDefaults.cs').write_text('namespace GloomhavenVR {internal static class Defaults {\n'+constants+'\n}}')
  remote=sources/'RemotePickBanner.cs';bind(root/'src/GloomhavenVR/Net/Remote/RemotePickBanner.cs',remote)
  sampler=sources/'NetAvatarDriver.cs';bind(root/'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs',sampler)
  sample=sampler.read_text();start=sample.index('string? bannerNow = trayNow != null ? trayNow.PickBannerText : null;');end=sample.index('if (bannerNow != _lastSentPickBanner)',start)
  mirror=project/'MirrorMethods.cs';mirror.write_text('using UnityEngine;using GloomhavenVR.Core;\nnamespace GloomhavenVR.Net {internal sealed partial class RemotePickBanner {\n'+method(remote.read_text(),'public void Apply(string? line)')+'\n} internal static class OwnerBannerSampler {internal static BannerSnapshot Publish(Cards.PortTray? trayNow) {var extras=new BannerSnapshot();\n'+sample[start:end]+'\nreturn extras;}}}\n')
  parts=[]
  flow=(root/'src/GloomhavenVR/Cards/Driver/CardsDriver.6.Flows.cs').read_text()
  assert flow.index('if (UpdateItemDemandStatus(hand))')<flow.index('if (UpdatePanelDecisionStatus())')<flow.index('UpdateExhaustedStatus(hand);')<flow.index('UpdateProgressStatus(hand);')
  assert 'if (!_exhaustedStatusKey.HasValue) UpdateProgressStatus(hand);' in flow
  for rel,signatures in [
   ('Driver/CardsDriver.6.Flows.cs',['private void PumpLongRestTurn()','private void PumpSelectionHandSwitch()']),
   ('Driver/CardsDriver.5.Interactions.cs',['private void EnqueuePickReopen(','private void TryCommitPick(']),
   ('Driver/CardsDriver.2.Update.cs',['internal static void ReleaseCardsBeforeSceneLoad()'])]:
   src=root/'src/GloomhavenVR/Cards'/rel;bind(src,sources/src.name);text=src.read_text()
   for signature in signatures:parts.append(method(text,signature))
  driver=project/'DriverMethods.cs';driver.write_text('using System;using System.Collections.Generic;using GloomhavenVR.Core;\nnamespace GloomhavenVR.Cards { internal sealed partial class CardsDriver {\n'+'\n'.join(parts)+'\n}}\n')
  api=root/'src/GloomhavenVR/Cards/CardsGameApi.cs';bind(api,sources/api.name)
  policy='\n'.join(method(api.read_text(),sig) for sig in ['internal static bool MustRestInsteadOfPlay(CardsHandUI hand)','internal static bool MustRestInsteadOfPlay(CPlayerActor? actor)'])
  cue=root/'src/GloomhavenVR/Cards/Caps/RestOfferCue.cs';bind(cue,sources/cue.name)
  wanted=re.search(r'internal static bool Wanted\([^;]+;',cue.read_text()).group(0)
  driver.write_text(driver.read_text()+'namespace GloomhavenVR.Cards { internal static partial class CardsGameApi {\n'+policy+'\n} internal static class RestOfferCue {\n'+wanted+'\n}}')
  rest=root/'src/GloomhavenVR/Cards/Caps/RestControls.cs';bind(rest,sources/rest.name)
  assert 'bool requiresRest = hand != null && CardsGameApi.MustRestInsteadOfPlay(hand);' in rest.read_text()
  assert rest.read_text().count('RestOfferCue.Wanted(requiresRest,')==2
  runtime=root/'ressources/GH_Data/Managed/GH.Runtime.dll'
  native=subprocess.run([str(Path.home()/'.dotnet/tools/ilspycmd'),'-t','CardsHandUI',str(runtime)],capture_output=True,text=True,check=True,env=env).stdout
  (sources/'CardsHandUI.native.cs').write_text(native)
  nativeparts=[method(native,'private void CancelAnimateCardLost()').replace('private void CancelAnimateCardLost()','private void CancelAnimateCardLostOriginal()',1),method(native,'private void OnClearLeanTweens()'),method(native,'private void OnDisable()')]
  (project/'NativeMethods.cs').write_text('using System;\npartial class CardsHandUI {\n'+'\n'.join(nativeparts)+'\n}\n')
  choreographer=subprocess.run([str(Path.home()/'.dotnet/tools/ilspycmd'),'-t','Choreographer',str(runtime)],capture_output=True,text=True,check=True,env=env).stdout
  wait=method(choreographer,'private IEnumerator WaitTriggerAnyOnLongRestAddActiveBonuses(');assert 'yield return new WaitWhile(() => hand.AnimatingLostCards);' in wait;(sources/'LongRestWait.native.cs').write_text(wait)
  module=root/'src/GloomhavenVR/Cards/CardsModule.cs';bind(module,sources/module.name)
  assert 'PatchAll(typeof(CardsHandUI_AnimateCardsLost_Lifetime))' in module.read_text()
  assert 'PatchAll(typeof(CardsHandUI_CancelAnimateCardLost_Lifetime))' in module.read_text()
  manifest={'sources':bindings,'native_runtime_sha256':hashlib.sha256(runtime.read_bytes()).hexdigest(),'ports':['Unity coroutine scheduling and animation/layout drawing endpoints','Native card/rule/network methods in queue caller ports counted, never executed or synthesized','Original native cancellation and tween clearing compiled unchanged except method name; actual production Harmony prefix/postfix invoked by reflection adapter','Native long-rest predicate source-bound; no rule continuation executed'],'hardware_verified':False,'live_network_verified':False}
  (run/'input-bindings.json').write_text(json.dumps(manifest,indent=2)+'\n')
  command=['dotnet','run','--project',str(project/'CardProgressLifetime.csproj'),'-c','Release','--property:QueueSource='+str(queue),'--property:LifetimeSource='+str(lifetime),'--property:ProgressSource='+str(progress),'--property:HelpClockSource='+str(clock),'--property:HelpLocSource='+str(helploc)]
  def execute(name):
   result=subprocess.run(command,capture_output=True,text=True,env=env);output=result.stdout+result.stderr;(run/(name+'.log')).write_text(output)
   with log.open('a') as f:f.write(output)
   return result.returncode,output
  code,output=execute('production');print(output,flush=True)
  if code:raise RuntimeError('Production failed')
  assertions=int(re.search(r'Card progress lifetime: (\d+) assertions',output).group(1));shutil.copytree(project/'bin/Release/net8.0',run/'executed-assembly')
  variants=[
   ('queue-old-clear',queue,'Entry[] cancelled = Queue.ToArray();','Entry[] cancelled = Array.Empty<Entry>();','Cancellation notifies owner without native action or ordinary completion'),
   ('queue-success-on-cancel',queue,'cancelled[i].OnCancelled?.Invoke();','cancelled[i].OnDone?.Invoke();','Cancellation notifies owner without native action or ordinary completion'),
   ('queue-drop-reentrant',queue,'Entry[] cancelled = Queue.ToArray();\n        Queue.Clear();','Entry[] cancelled = Queue.ToArray();\n        Queue.Clear();',None),
   ('lost-rest-cancellation',driver,'onCancelled: () => { _longRestPumpQueued = false; _dirty = true; }','onCancelled: () => { _dirty = true; }','Aborted scene load must release caller latch 0'),
   ('lost-switch-cancellation',driver,'onCancelled: () => { _selSwitchQueued = false; _dirty = true; }','onCancelled: () => { _dirty = true; }','Aborted scene load must release caller latch 1'),
   ('lost-reopen-cancellation',driver,'onCancelled: () => { _pickReopenBusy = false; _dirty = true; }','onCancelled: () => { _dirty = true; }','Aborted scene load must release caller latch 2'),
   ('lost-pick-cancellation',driver,'onCancelled: () => { _pickCommitBusy = false; _dirty = true; }','onCancelled: () => { _dirty = true; }','Aborted scene load must release caller latch 3'),
   ('native-old-cancellation',lifetime,'hand.AnimatingLostCards = false;','// original cancellation omitted its final flag write','Successful native cancellation must release its animation-liveness flag'),
   ('native-missing-generation',lifetime,'|| cancellation.Generation.Value != cancellation.Value','', 'replacement generation protects even unstarted iterator'),
   ('native-missing-owned-animation',lifetime,'!cancellation.WasRunning || cancellation.Generation == null','cancellation.Generation == null','unrelated stale/no-owned-animation state is not guessed away'),
   ('rest-counts-fan-only',driver,'+ actor.CharacterClass.RoundAbilityCards.Count < 2;','< 2;','Local and mirrored rest cue retain native hand+round/phase/extra-turn semantics'),
   ('rest-extra-turn',driver,'if (actor.SelectingCardsForExtraTurnOfType != CAbilityExtraTurn.EExtraTurnType.None)','if (actor == null)','Local and mirrored rest cue retain native hand+round/phase/extra-turn semantics'),
   ('hint-foreign-target',progress,'&& Choreographer.s_Choreographer.ThisPlayerHasTurnControl','',"Another player's targeting never asks this player to click"),
   ('hint-automatic-wait',progress,'hint = ProgressWaitNotice.ImmediateHintKey;','hint = null;','Actual long-running automatic wait is never described as rest or card input'),
   ('hint-empty-action-dock',progress,'&& _halfBuffer.Count > 0','', 'Empty long-rest dock does not advertise a card action'),
   ('hint-finish-action',progress,'&& !CardsGameApi.CanConfirm()','', 'Live End Turn or Continue does not compete with a choose-action instruction'),
   ('help-no-delay',clock,'now - _since >= Math.Max(0f, delaySeconds)','true','Delayed text is not shown on first native choice'),
   ('help-short-delay',clock,'now - _since >= Math.Max(0f, delaySeconds)','now - _since >= 20f','Configured delay must not be shortened'),
   ('help-ignore-response',clock,'|| _response != response','', 'One accepted card response restarts the delay even when prompt wording stays the same'),
   ('help-ignore-card-swap',progress,'changed = !ReferenceEquals(cards[i], _progressHelpRound[i]);','changed = false;','Same-count card swap is still a meaningful response'),
   ('help-reset-on-hide',progress,'bool shown = _progressHelpClock.Observe','if (!_tray.IsVisible) _progressHelpClock.Reset();\n        bool shown = _progressHelpClock.Observe','Board visibility and modal/head presentation do not restart the decision clock'),
   ('help-steal-native-banner',progress,'if (resident != null && (!_progressStatusKey.HasValue || resident != _progressStatusKey.Value.line))','if (resident != null && resident.Length < 0)','Never cannot erase an existing mandatory native banner'),
   ('help-viewer-config',mirror,'bool show = !string.IsNullOrEmpty(line);','bool show = !string.IsNullOrEmpty(line) && Cards.CardsConfig.ProgressHelpMode.Value != Cards.ProgressHelpMode.Never;','Remote banner uses owner visibility even when viewer help is Never'),
  ]
  # A queue tail enqueued during cancellation must survive; clearing again after callbacks loses it.
  variants[2]=('queue-drop-reentrant',queue,'catch (Exception ex) { VRLog.Error("Cards", $"Card action cancellation callback threw: {ex}"); }\n        }','catch (Exception ex) { VRLog.Error("Cards", $"Card action cancellation callback threw: {ex}"); }\n        }\n        Queue.Clear();','Reentrant cancellation enqueue belongs to next pump')
  for name,path,old,new,expected in variants:
   original=path.read_text();assert original.count(old)==1,(name,'control binding');path.write_text(original.replace(old,new));(sources/(name+'.cs')).write_text(path.read_text())
   try:
    code,output=execute(name)
    if not code or expected not in output or 'error CS' in output:raise RuntimeError('Control failed at wrong boundary '+name+'\n'+output)
    print('Card progress control: '+name+' rejected.',flush=True)
   finally:path.write_text(original)
  (run/'result.json').write_text(json.dumps({'assertions':assertions,'negative_controls':[v[0] for v in variants],'elapsed_seconds':time.monotonic()-started,'hardware_verified':False,'live_network_verified':False},indent=2)+'\n');status=0
 finally:
  receipt=cleanup_completed_run('card-progress-lifetime',status,log,root,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
 return status
if __name__=='__main__':raise SystemExit(main())
