using System.Collections;
using GloomhavenVR.Cards;
using static ScenarioRuleLibrary.CBaseCard;
static class Program {
 static int count;
 static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
 static (CardEffects,ScenarioRuleLibrary.CAbilityCard) Make(ECardPile pile){var card=new ScenarioRuleLibrary.CAbilityCard{CurrentCardPile=pile};var fx=new CardEffects();fx.Full.AbilityCard=card;return(fx,card);}
 static (CardEffects,ScenarioRuleLibrary.CAbilityCard) MakeObserved(ECardPile pile){var(fx,card)=Make(pile);fx.Full.playerActor=new();MoveObserved(fx,card,pile);return(fx,card);}
 static void MoveObserved(CardEffects fx,ScenarioRuleLibrary.CAbilityCard card,ECardPile pile){var c=fx.Full.playerActor!.CharacterClass;c.HandAbilityCards.Clear();c.RoundAbilityCards.Clear();c.LostAbilityCards.Clear();(pile==ECardPile.Hand?c.HandAbilityCards:pile==ECardPile.Round?c.RoundAbilityCards:c.LostAbilityCards).Add(card);card.CurrentCardPile=pile;}
 sealed class NativeProbe:IEnumerator {internal bool Throw;internal readonly InvalidOperationException Error=new();public object Current=>this;public bool MoveNext(){if(Throw)throw Error;return true;}public void Reset(){}}
 static void Main(){
  foreach(var origin in new[]{ECardPile.Discarded,ECardPile.Round,ECardPile.Activated,ECardPile.Hand}){
   var(fx,card)=Make(origin);fx.ToggleEffect(true,CardEffects.FXTask.BurnCard);var live=fx.Live;var paint=fx.Paint;card.CurrentCardPile=ECardPile.Lost;
   for(int i=0;i<100;i++){
    fx.ToggleEffect(true,CardEffects.FXTask.LostMode);fx.ToggleAdditiveEffect(true,CardEffects.FXTask.BurnCard);fx.ToggleEffect(false,CardEffects.FXTask.LostMode);fx.RestoreCard();
    Check(fx.Starts==1&&fx.Resets==1&&ReferenceEquals(live,fx.Live)&&fx.Paint==paint,"Repeated native refresh must retain the first burn iterator and paint");
   }
   Check(fx.toggledEffects.Contains(CardEffects.FXTask.BurnCard)&&fx.toggledEffects.Contains(CardEffects.FXTask.LostMode),"Native burn task aliases must remain latched");
   var unused=fx.BurnCardTimeline(true);Check(ReferenceEquals(BurnArtwork.BurnTimelines.GetValue(fx,_=>throw new Exception()),live),"Unstarted iterators must not replace live playback tracking");
   while(live!.MoveNext()){Check(fx.Paint>=paint,"Native progress cannot reverse");paint=fx.Paint;}
   fx.ToggleEffect(true,CardEffects.FXTask.LostMode);fx.RestoreCard();Check(fx.Starts==1&&fx.Paint>=1,"Completed lost burns must never replay or turn blue");
   card.CurrentCardPile=ECardPile.Hand;fx.RestoreCard();Check(fx.Paint==0&&fx.Resets==2,"Actual model recovery must restore normal artwork");
   fx.ToggleEffect(true,CardEffects.FXTask.BurnCard);Check(fx.Starts==2,"Recovered cards may start a genuinely new burn");
   BurnArtwork.RetireBurnPlayback(fx);fx.RestoreCard();Check(fx.Paint==0,"Explicit pool retirement must allow reset even for old lost widgets");
  }
  var(bail,bc)=Make(ECardPile.Lost);bail.Disabled=true;bail.ToggleEffect(true,CardEffects.FXTask.BurnCard);bail.Disabled=false;bail.ToggleEffect(true,CardEffects.FXTask.BurnCard);Check(bail.Starts==1,"Synchronous native bail must allow a real first playback later");
  // Reproduce native FinalizeShortRest -> inactive full face -> reactivation -> SetPile(Lost).
  // The actual native entry gate runs inside the stub iterator, after Harmony argument binding.
  foreach(bool adoptedRest in new[]{false,true}) {
   var(rest,restCard)=Make(ECardPile.Discarded);rest.Full.playerActor=new();
   var restOwner=rest.Full.playerActor;restOwner.CharacterClass.DiscardedAbilityCards.Add(restCard);
   var restWidget=new AbilityCardUI{fullAbilityCard=rest.Full,AbilityCard=restCard,PlayerActor=restOwner};
   if(adoptedRest)CardFace.Owner=restWidget;else rest.Full.Parent=restWidget;
   rest.imgComp=new[]{new UnityEngine.UI.Image()};rest.RawSteps=new[]{.01f,.2f,.5f,.8f,1f};
   rest.WriteRaw(1);rest.gameObject.activeInHierarchy=false;
   rest.ToggleEffect(true,CardEffects.FXTask.BurnCard);var first=rest.Live!;
   Check(first.Started&&!first.Finished&&rest.Starts==1&&rest.Paint==.01f,
    "An inactive original must execute the first native burn instead of bailing and replaying later");
   Check(!rest.gameObject.activeInHierarchy,"Burn permission must never activate hidden native UI");
   Check(rest.imgComp[0].material.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==1,
    "An inactive short-rest original must retain its spent wash on the first native step");
   rest.gameObject.activeInHierarchy=true;
   restOwner.CharacterClass.DiscardedAbilityCards.Clear();restOwner.CharacterClass.LostAbilityCards.Add(restCard);
   restCard.CurrentCardPile=ECardPile.Lost;
   while(first.MoveNext()) {
    rest.ToggleEffect(true,CardEffects.FXTask.LostMode);rest.ToggleEffect(false,CardEffects.FXTask.LostMode);rest.RestoreCard();
    BurnArtwork.PreserveSpentBurnStart(rest,restWidget);
    Check(ReferenceEquals(first,rest.Live)&&rest.Starts==1&&rest.Resets==1,
     "Reactivation and LostMode refresh must retain the one original short-rest burn");
    Check(rest.imgComp[0].material.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==1,
     "Local drawing and remote sampling must not expose a clean short-rest card");
   }
   rest.ToggleEffect(true,CardEffects.FXTask.LostMode);rest.RestoreCard();
   Check(rest.Starts==1&&rest.Resets==1&&rest.Paint==1,
    "The completed inactive-origin burn must remain authoritative after loss refresh");
   restOwner.CharacterClass.LostAbilityCards.Clear();restOwner.CharacterClass.HandAbilityCards.Add(restCard);
   BurnArtwork.ReconcileRecoveredAppearance(rest);
   Check(rest.Paint==0&&rest.Resets==2,"Recovery without a native SetPile edge must restore the original before local draw and remote capture");
   CardFace.Owner=null;
  }
  var(unbound,unboundCard)=Make(ECardPile.Discarded);unbound.gameObject.activeInHierarchy=false;
  unbound.ToggleEffect(true,CardEffects.FXTask.BurnCard);
  Check(unbound.Starts==0&&unbound.Live!.Finished,"Unbound clones must retain the native inactive playback gate");
  var(mismatch,mismatchCard)=Make(ECardPile.Discarded);mismatch.gameObject.activeInHierarchy=false;
  mismatch.Full.Parent=new AbilityCardUI{fullAbilityCard=new FullAbilityCard(),AbilityCard=mismatchCard};
  mismatch.ToggleEffect(true,CardEffects.FXTask.BurnCard);
  Check(mismatch.Starts==0&&mismatch.Live!.Finished,"A mismatched native parent must not authorize inactive clone playback");
  var(changed,cc)=Make(ECardPile.Lost);changed.ToggleEffect(true,CardEffects.FXTask.BurnCard);changed.Full.AbilityCard=new(){CurrentCardPile=ECardPile.Lost};changed.ToggleEffect(true,CardEffects.FXTask.LostMode);Check(changed.Starts==2,"Recycled widget identity must not inherit another card's burn");
  var(adopted,ac)=Make(ECardPile.Discarded);CardFace.Owner=new(){fullAbilityCard=adopted.Full,AbilityCard=ac};adopted.Full.AbilityCard=null;adopted.ToggleEffect(true,CardEffects.FXTask.BurnCard);ac.CurrentCardPile=ECardPile.Lost;adopted.ToggleEffect(true,CardEffects.FXTask.LostMode);Check(adopted.Starts==1,"Adopted widget identity must win over missing or stale full-card metadata");CardFace.Owner=null;
  var(authoritative,stale)=Make(ECardPile.Hand);authoritative.Full.playerActor=new();authoritative.Full.playerActor.CharacterClass.LostAbilityCards.Add(stale);authoritative.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(authoritative.Live!.MoveNext()){}authoritative.RestoreCard();Check(authoritative.Paint>=1,"Authoritative lost membership must override a stale hand pile stamp");
  authoritative.Full.playerActor.CharacterClass.LostAbilityCards.Clear();authoritative.Full.playerActor.CharacterClass.HandAbilityCards.Add(stale);stale.CurrentCardPile=ECardPile.Lost;authoritative.RestoreCard();Check(authoritative.Paint==0,"Authoritative hand recovery must override a stale lost pile stamp");
  var(recover,rc)=Make(ECardPile.Lost);recover.ToggleEffect(true,CardEffects.FXTask.BurnCard);rc.CurrentCardPile=ECardPile.Hand;recover.RestoreCard();Check(recover.Paint==0,"Actual model recovery must restore normal artwork");
  var(retired,rtc)=Make(ECardPile.Lost);retired.ToggleEffect(true,CardEffects.FXTask.BurnCard);var obsolete=retired.Live!;BurnArtwork.RetireBurnPlayback(retired);obsolete.MoveNext();retired.RestoreCard();Check(retired.Paint==0,"A retired iterator cannot recreate an episode on pooled artwork");
  var(cancelled,cnc)=Make(ECardPile.Lost);cancelled.ToggleEffect(true,CardEffects.FXTask.BurnCard);cancelled.Live!.Dispose();cancelled.ToggleEffect(true,CardEffects.FXTask.BurnCard);Check(cancelled.Starts==2,"Disposed native playback cannot leave a permanent replay guard");
  var(activated,act)=Make(ECardPile.Round);activated.ToggleEffect(true,CardEffects.FXTask.BurnCard);act.CurrentCardPile=ECardPile.Activated;activated.RestoreCard();Check(activated.Paint>0,"Activation must not interrupt an in-progress original burn");var activatedPlayback=activated.Live!;while(activatedPlayback.MoveNext()){}Check(activated.Paint==0,"A completed activation must permit the native clean active-card look");
  var firstSeen=new ScenarioRuleLibrary.CAbilityCard{CurrentCardPile=ECardPile.Hand};var historicalOwner=new ScenarioRuleLibrary.CPlayerActor();historicalOwner.CharacterClass.LostAbilityCards.Add(firstSeen);BurnArtwork.AbilityCardUI_Init_HistoricalBurn_Patch.Prefix(firstSeen,historicalOwner);var historical=new CardEffects();historical.Full.AbilityCard=firstSeen;historical.Full.playerActor=historicalOwner;historical.ToggleEffect(true,CardEffects.FXTask.LostMode);Check(historical.Starts==0&&historical.Paint==1,"First-seen already-lost widgets must settle native artwork without a historical replay");
  var(original,model)=Make(ECardPile.Lost);original.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(original.Live!.MoveNext()){}BurnArtwork.RetireBurnPlayback(original);var recreated=new CardEffects();recreated.Full.AbilityCard=model;recreated.ToggleEffect(true,CardEffects.FXTask.LostMode);Check(recreated.Starts==0&&recreated.Paint==1,"Completed original burns must survive widget recreation without replay");
  recreated.RestoreCard();Check(recreated.Paint==1,"Historical native settle must survive a standalone reset without blue flash");recreated.ToggleEffect(true,CardEffects.FXTask.LostMode);Check(recreated.Starts==0&&recreated.Paint==1,"Historical native settle must survive later resets and alias requests");
  model.CurrentCardPile=ECardPile.Hand;recreated.RestoreCard();model.CurrentCardPile=ECardPile.Lost;recreated.ToggleEffect(true,CardEffects.FXTask.BurnCard);Check(recreated.Starts==1,"Model history must retire on genuine recovery before another loss");
  var(existing,ec)=Make(ECardPile.Lost);existing.Full.playerActor=new();existing.Full.playerActor.CharacterClass.LostAbilityCards.Add(ec);existing.ToggleEffect(true,CardEffects.FXTask.BurnCard);BurnArtwork.AbilityCardUI_Init_HistoricalBurn_Patch.Prefix(ec,existing.Full.playerActor);Check(!BurnArtwork.ModelBurns.GetValue(ec,_=>throw new Exception()).Completed,"Initializing another widget must not finish an original running burn");
  var(construction,con)=Make(ECardPile.Hand);construction.ThrowOwner=true;IEnumerator raw=new NativeProbe();var preserved=raw;BurnArtwork.BurnCardTimeline_PreserveSpentStart_Patch.Postfix(construction,true,ref preserved);Check(ReferenceEquals(raw,preserved),"Mod-only interception failure must preserve the original native iterator");
  var(nativeError,ne)=Make(ECardPile.Hand);var probe=new NativeProbe{Throw=true};IEnumerator forwarded=probe;BurnArtwork.BurnCardTimeline_PreserveSpentStart_Patch.Postfix(nativeError,true,ref forwarded);try{forwarded.MoveNext();throw new Exception("Native failure was swallowed");}catch(InvalidOperationException ex){Check(ReferenceEquals(ex,probe.Error),"Native execution exception identity must remain unchanged");}
  var(primary,pc)=Make(ECardPile.Lost);primary.ToggleEffect(true,CardEffects.FXTask.BurnCard);var primaryIterator=primary.Live!;var follower=new CardEffects();follower.Full.AbilityCard=pc;follower.ToggleEffect(true,CardEffects.FXTask.LostMode);var followerIterator=follower.Live!;for(int i=0;i<10;i++){follower.ToggleEffect(true,CardEffects.FXTask.BurnCard);Check(followerIterator.MoveNext()&&follower.Starts==0&&primary.Starts==1,"A replacement widget must wait on the original burn without starting another ramp");}while(primaryIterator.MoveNext()){}Check(!followerIterator.MoveNext()&&follower.Paint==1&&follower.Starts==0,"A replacement widget must receive settled native output after original completion");Check(BurnArtwork.ModelBurns.GetValue(pc,_=>throw new Exception()).Running==null,"Completed model history must release its original widget reference");
  var(retiring,rt)=Make(ECardPile.Lost);retiring.ToggleEffect(true,CardEffects.FXTask.BurnCard);var waiting=new CardEffects();waiting.Full.AbilityCard=rt;waiting.ToggleEffect(true,CardEffects.FXTask.LostMode);var waiter=waiting.Live!;BurnArtwork.RetireBurnPlayback(retiring);Check(!waiter.MoveNext()&&waiting.Paint==1,"Retiring the primary must release replacement waiters without another ramp");
  var(recoverPrimary,rp)=Make(ECardPile.Lost);recoverPrimary.ToggleEffect(true,CardEffects.FXTask.BurnCard);var recoveringFollower=new CardEffects();recoveringFollower.Full.AbilityCard=rp;recoveringFollower.ToggleEffect(true,CardEffects.FXTask.LostMode);var recoveringWaiter=recoveringFollower.Live!;rp.CurrentCardPile=ECardPile.Hand;recoverPrimary.RestoreCard();Check(!recoveringWaiter.MoveNext()&&recoveringFollower.Paint==0,"Original recovery must release waiters without painting a new lost state");
  var(steady,sc)=Make(ECardPile.Lost);steady.ToggleEffect(true,CardEffects.FXTask.BurnCard);var obsoleteFollower=new CardEffects();obsoleteFollower.Full.AbilityCard=sc;obsoleteFollower.ToggleEffect(true,CardEffects.FXTask.LostMode);var oldFollower=obsoleteFollower.Live!;BurnArtwork.RetireBurnPlayback(obsoleteFollower);Check(!oldFollower.MoveNext()&&obsoleteFollower.Paint==0,"Retired follower iterators must stop without painting recycled artwork");Check(steady.Live!.MoveNext(),"Retiring a follower must not cancel the primary burn");
  var retargeted=new CardEffects();retargeted.Full.AbilityCard=sc;retargeted.ToggleEffect(true,CardEffects.FXTask.LostMode);var staleFollower=retargeted.Live!;retargeted.Full.AbilityCard=new(){CurrentCardPile=ECardPile.Hand};Check(!staleFollower.MoveNext()&&retargeted.Paint==0,"Retargeted follower iterators must stop without altering the new card");
  var(normal,nc)=Make(ECardPile.Hand);normal.ToggleEffect(true,CardEffects.FXTask.DiscardMode);Check(normal.Discards==1,"Unrelated normal discard presentation must remain native");
  var(preview,previewCard)=Make(ECardPile.Discarded); preview.Full.playerActor=new(); preview.Full.playerActor.CharacterClass.DiscardedAbilityCards.Add(previewCard);
  CardsHandManager.Instance=new(); CardsHandManager.Instance.Hand.ShortRestedCard=previewCard;preview.Paint=.7f;
  for(int hover=0;hover<30;hover++){preview.ToggleEffect(false,CardEffects.FXTask.BurnCard);Check(preview.Paint==.7f && preview.Resets==0 && preview.Live==null,"Uncommitted short-rest hover must retain spent artwork without a premature flame preview");}
  preview.ToggleEffect(true,CardEffects.FXTask.BurnCard); Check(preview.Starts==1,"Confirming the same hovered rest offer must start its native burn exactly once");
  preview.Full.playerActor.CharacterClass.DiscardedAbilityCards.Clear();preview.Full.playerActor.CharacterClass.LostAbilityCards.Add(previewCard);previewCard.CurrentCardPile=ECardPile.Lost;
  while(preview.Live!.MoveNext()){} preview.ToggleEffect(false,CardEffects.FXTask.BurnCard);Check(preview.Starts==1 && preview.Paint>=1,"Completed rest artwork must remain burnt after hover exits");
  var(otherPreview,otherCard)=Make(ECardPile.Discarded);otherPreview.Full.playerActor=preview.Full.playerActor;otherPreview.Full.playerActor.CharacterClass.DiscardedAbilityCards.Add(otherCard);otherPreview.ToggleEffect(false,CardEffects.FXTask.BurnCard);Check(otherPreview.Paint==1 && otherPreview.Resets==1,"Unrelated native burn previews must retain their original behavior");
  CardsHandManager.Instance.Hand.ShortRestedCard=otherCard;otherPreview.Paint=.7f;int redrawResets=otherPreview.Resets;
  otherPreview.ToggleEffect(false,CardEffects.FXTask.BurnCard);Check(otherPreview.Paint==.7f && otherPreview.Resets==redrawResets,"Redrawn short-rest offer must retain its own spent artwork");
  otherPreview.ToggleEffect(false,CardEffects.FXTask.DiscardMode);Check(otherPreview.Discards==1,"Leaving an unaccepted hover must allow the native discarded-pile refresh");
  CardsHandManager.Instance.Hand.ShortRestedCard=null;otherPreview.ToggleEffect(false,CardEffects.FXTask.BurnCard);Check(otherPreview.Resets==redrawResets+2,"Cancelling the rest must retire preview suppression");
  preview.Full.playerActor.CharacterClass.LostAbilityCards.Clear();preview.Full.playerActor.CharacterClass.HandAbilityCards.Add(previewCard);previewCard.CurrentCardPile=ECardPile.Hand;preview.RestoreCard();
  preview.Full.playerActor.CharacterClass.HandAbilityCards.Clear();preview.Full.playerActor.CharacterClass.DiscardedAbilityCards.Add(previewCard);previewCard.CurrentCardPile=ECardPile.Discarded;CardsHandManager.Instance.Hand.ShortRestedCard=previewCard;preview.Paint=.7f;
  preview.ToggleEffect(false,CardEffects.FXTask.BurnCard);Check(preview.Paint==.7f,"Recovered card offered in another rest must retain its spent preview");preview.ToggleEffect(true,CardEffects.FXTask.BurnCard);Check(preview.Starts==2,"A genuinely recovered card must burn again on later confirmation");
  CardsHandManager.Instance=null;
  // Model recovery without another native SetPile edge: the native widget has already cached
  // Hand while a previous refresh/reset was protected. Test the production reconciliation itself.
  foreach(var target in new[]{ECardPile.Hand,ECardPile.Round}){
   var(recoveredFx,recoveredCard)=MakeObserved(ECardPile.Hand);var owner=new ScenarioRuleLibrary.CPlayerActor();recoveredFx.Full.playerActor=owner;
   owner.CharacterClass.LostAbilityCards.Add(recoveredCard);recoveredFx.ToggleEffect(true,CardEffects.FXTask.BurnCard);
   while(recoveredFx.Live!.MoveNext()){}int before=recoveredFx.Resets;
   for(int repeat=0;repeat<10;repeat++)BurnArtwork.ReconcileRecoveredAppearance(recoveredFx);
   Check(recoveredFx.Paint>=1&&recoveredFx.Resets==before,"Lost cards with stale Hand stamps must retain their finished burn");
   owner.CharacterClass.LostAbilityCards.Clear();
   (target==ECardPile.Hand?owner.CharacterClass.HandAbilityCards:owner.CharacterClass.RoundAbilityCards).Add(recoveredCard);
   recoveredCard.CurrentCardPile=ECardPile.Lost; // membership, not the stale serialized stamp
   BurnArtwork.ReconcileRecoveredAppearance(recoveredFx);
   Check(recoveredFx.Paint==0&&recoveredFx.toggledEffects.Count==0&&recoveredFx.Resets==before+1,
    "Recovery without a native SetPile edge must restore the original before local draw and remote capture");
   Check(!BurnArtwork.ModelBurns.TryGetValue(recoveredCard,out _)&&!BurnArtwork.BurnEpisodes.TryGetValue(recoveredFx,out _)&&!BurnArtwork.BurnTimelines.TryGetValue(recoveredFx,out _),
    "Recovery must retire completed episode and callback ownership before another burn");
   for(int sample=0;sample<20;sample++)BurnArtwork.ReconcileRecoveredAppearance(recoveredFx);
   Check(recoveredFx.Resets==before+1,"Repeated local and network sampling must not reset recovered originals again");
   recoveredFx.ToggleEffect(true,CardEffects.FXTask.BurnCard);var newBurn=recoveredFx.Live;
   BurnArtwork.ReconcileRecoveredAppearance(recoveredFx);
   Check(recoveredFx.Starts==2&&ReferenceEquals(recoveredFx.Live,newBurn)&&recoveredFx.Paint>0,
    "Recovered cards must allow a new early Hand or Round burn without interruption");
  }
  var(early,earlyCard)=MakeObserved(ECardPile.Hand);early.ToggleEffect(true,CardEffects.FXTask.BurnCard);var earlyBurn=early.Live;
  for(int tick=0;tick<20;tick++)BurnArtwork.ReconcileRecoveredAppearance(early);
  Check(ReferenceEquals(earlyBurn,early.Live)&&early.Paint>0&&early.Resets==1,"An ordinary precommit Hand burn must never be mistaken for recovery");
  var(hidden,hc)=MakeObserved(ECardPile.Lost);hidden.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(hidden.Live!.MoveNext()){}
  var(hiddenPeer,_)=MakeObserved(ECardPile.Lost);hiddenPeer.Full.AbilityCard=hc;hiddenPeer.Full.playerActor=hidden.Full.playerActor;hiddenPeer.ToggleEffect(true,CardEffects.FXTask.LostMode);
  MoveObserved(hidden,hc,ECardPile.Hand);BurnArtwork.ReconcileRecoveredAppearance(hidden);BurnArtwork.ReconcileRecoveredAppearance(hiddenPeer);
  Check(hidden.Paint==0&&hiddenPeer.Paint==0,"Returning to a previously hidden original must clear its own stale burn after another widget consumed model recovery");
  var(retry,retryCard)=MakeObserved(ECardPile.Lost);retry.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(retry.Live!.MoveNext()){}
  MoveObserved(retry,retryCard,ECardPile.Hand);retry.ThrowReset=true;BurnArtwork.ReconcileRecoveredAppearance(retry);Check(retry.Paint>0,"A failed original reset must leave its recovery retryable");
  retry.ThrowReset=false;BurnArtwork.ReconcileRecoveredAppearance(retry);Check(retry.Paint==0,"A later intact original must retry recovery after an earlier native reset failure");
  var(retryNew,nextCard)=MakeObserved(ECardPile.Lost);retryNew.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(retryNew.Live!.MoveNext()){}
  MoveObserved(retryNew,nextCard,ECardPile.Hand);retryNew.ThrowReset=true;BurnArtwork.ReconcileRecoveredAppearance(retryNew);retryNew.ThrowReset=false;
  retryNew.ToggleEffect(true,CardEffects.FXTask.BurnCard);var restarted=retryNew.Live;BurnArtwork.ReconcileRecoveredAppearance(retryNew);
  Check(ReferenceEquals(restarted,retryNew.Live)&&retryNew.Paint>0&&retryNew.Starts==2,"A failed recovery retry must never cancel a new genuine burn of the same card");
  retry.ThrowOwner=true;BurnArtwork.ReconcileRecoveredAppearance(retry);Check(true,"A teardown metadata failure cannot interrupt the next local or network card");
  var(rebound,oldCard)=MakeObserved(ECardPile.Lost);rebound.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(rebound.Live!.MoveNext()){}
  MoveObserved(rebound,oldCard,ECardPile.Hand);rebound.ThrowReset=true;BurnArtwork.ReconcileRecoveredAppearance(rebound);rebound.ThrowReset=false;
  rebound.Full.AbilityCard=new(){CurrentCardPile=ECardPile.Hand};MoveObserved(rebound,rebound.Full.AbilityCard,ECardPile.Hand);rebound.Paint=.25f;int reusedResets=rebound.Resets;
  BurnArtwork.ReconcileRecoveredAppearance(rebound);Check(rebound.Paint==.25f&&rebound.Resets==reusedResets,"A pooled rebind must never apply another card's failed recovery to its new presentation");
  var(unresolved,unknown)=Make(ECardPile.Lost);unresolved.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(unresolved.Live!.MoveNext()){}
  unknown.CurrentCardPile=ECardPile.Hand;BurnArtwork.ReconcileRecoveredAppearance(unresolved);
  Check(unresolved.Paint>=1,"Unresolved owner metadata must not clear a real lost card from a stale Hand stamp");
  // Execute the production spent-channel implementation, not a boolean-only timeline stub.
  // Native BurnCardTimeline terminates from elapsed clock time without writing its endpoint;
  // its accumulated delta-time paint may therefore lag that clock (including a coarse frame).
  foreach(float lastNative in new[]{0f,.1f,.5f,.97f,.999f}){
   var(floor,floorCard)=Make(ECardPile.Discarded);floor.Full.playerActor=new();
   floor.Full.playerActor.CharacterClass.DiscardedAbilityCards.Add(floorCard);
   floor.imgComp=new[]{new UnityEngine.UI.Image(),new UnityEngine.UI.Image()};floor.RawSteps=new[]{0f,lastNative};
   floor.WriteRaw(1);floor.ToggleEffect(true,CardEffects.FXTask.BurnCard);var floorIterator=floor.Live!;
   floor.Full.playerActor.CharacterClass.DiscardedAbilityCards.Clear();floor.Full.playerActor.CharacterClass.LostAbilityCards.Add(floorCard);
   foreach(var image in floor.imgComp)Check(image.material.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==1,"Spent artwork must stay spent from the first native burn step");
   Check(floorIterator.MoveNext(),"The running original must advance without a replacement iterator");
   Check(!floorIterator.MoveNext()&&floor.coroutine==null,"The terminal native step must still complete normally");
   foreach(var image in floor.imgComp)foreach(var channel in new[]{("_GreyOut",1f),("_Flow",1f),("_Dissolve",.646f)})
    Check(image.material.GetFloat(UnityEngine.Shader.PropertyToID(channel.Item1))==channel.Item2,"The terminal native step must not expose raw unspent artwork");
   Check(floor.Starts==1&&floor.Resets==1,"Retaining terminal paint must not restart native burn or reset its card");
   Check(BurnArtwork.BurnStarts.TryGetValue(floor,out var floorRecord)&&floorRecord.RawGrey==lastNative,"Spent display floors must not manufacture native completion progress");
   BurnArtwork.PreserveSpentBurnStart(floor,floorCard,floor.Full.playerActor,beforeReset:false);
   foreach(var image in floor.imgComp)Check(image.material.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==1,"A later local or remote presentation sample must retain the finished spent picture");
   floor.Full.playerActor.CharacterClass.LostAbilityCards.Clear();floor.Full.playerActor.CharacterClass.HandAbilityCards.Add(floorCard);
   BurnArtwork.ReconcileRecoveredAppearance(floor);
   foreach(var image in floor.imgComp)Check(image.material.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==0,"A terminal spent floor must not survive genuine card recovery");
  }
  var(activeFloor,activeFloorCard)=Make(ECardPile.Activated);activeFloor.Full.playerActor=new();
  activeFloor.Full.playerActor.CharacterClass.ActivatedCards.Add(activeFloorCard);
  activeFloor.imgComp=new[]{new UnityEngine.UI.Image()};activeFloor.RawSteps=new[]{.1f,.8f};activeFloor.WriteRaw(.6f);
  activeFloor.ToggleEffect(true,CardEffects.FXTask.BurnCard);var activeFloorIterator=activeFloor.Live!;activeFloor.RestoreCard();
  while(activeFloorIterator.MoveNext()){}
  Check(activeFloor.imgComp[0].material.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==0,
   "A legitimate deferred activation reset must not regain its old spent floor");
  var(traceFx,traceCard)=Make(ECardPile.Discarded);
  var source=new UnityEngine.Material();var drawn=new UnityEngine.Material();
  traceFx._headerImage=new(){material=source};traceFx._headerImage.canvasRenderer.Bound=drawn;
  traceFx._uiFxOverlay=new();
  BurnPlaybackTrace.Begin(traceFx,false);BurnPlaybackTrace.Event(traceFx,"disabled");BurnPlaybackTrace.Sample(traceFx);
  Check(GloomhavenVR.Core.VRLog.Lines.Count==0,"Normal logging must never record native burn tracing");
  GloomhavenVR.Core.VRLog.WantsDebug=true;
  BurnPlaybackTrace.Begin(traceFx,false);BurnPlaybackTrace.Sample(traceFx);
  drawn.SetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"),1);BurnPlaybackTrace.Sample(traceFx);
  drawn.SetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"),0);BurnPlaybackTrace.Sample(traceFx);
  Check(GloomhavenVR.Core.VRLog.Lines.Any(l=>l.Contains("renderer-paint-rewind")),"Debug tracing must observe the renderer's own rewind independently of the source material");
  traceFx._headerImage.canvasRenderer.Bound=new();BurnPlaybackTrace.Sample(traceFx);
  Check(GloomhavenVR.Core.VRLog.Lines.Any(l=>l.Contains("renderer-binding-change")),"Debug tracing must distinguish binding replacement from native paint");
  traceFx._uiFxOverlay!.material.SetFloat(UnityEngine.Shader.PropertyToID("_FXAnim"),.5f);BurnPlaybackTrace.Sample(traceFx);
  int beforePause=GloomhavenVR.Core.VRLog.Lines.Count;
  UnityEngine.Time.unscaledTime=5;traceFx.coroutine=new();BurnPlaybackTrace.Event(traceFx,"paused-live-timeline");
  Check(GloomhavenVR.Core.VRLog.Lines.Count==beforePause+1,"A paused live timeline must keep Debug observation beyond the completed-tail deadline");
  UnityEngine.Time.unscaledTime=10;traceFx.coroutine=null;BurnPlaybackTrace.Event(traceFx,"native-terminal-step");
  Check(GloomhavenVR.Core.VRLog.Lines.Count==beforePause+2,"A late native completion must remain observable after a pause");
  int beforeFlame=GloomhavenVR.Core.VRLog.Lines.Count;
  traceFx._uiFxOverlay.material.SetFloat(UnityEngine.Shader.PropertyToID("_FXAnim"),0);BurnPlaybackTrace.Sample(traceFx);
  Check(GloomhavenVR.Core.VRLog.Lines.Count==beforeFlame+1,"Debug tracing must detect a flame-only rewind");
  for(int i=0;i<1000;i++)BurnPlaybackTrace.Event(traceFx,"bounded");
  Check(GloomhavenVR.Core.VRLog.Lines.Count==beforeFlame+2,"Repeated native reset requests must be deduplicated");
  for(int i=0;i<1000;i++)BurnPlaybackTrace.Event(traceFx,"ordinary-"+i);
  int beforeTerminal=GloomhavenVR.Core.VRLog.Lines.Count;
  BurnPlaybackTrace.Event(traceFx,"native-terminal-step");
  Check(GloomhavenVR.Core.VRLog.Lines.Count==beforeTerminal+1,"Ordinary events must reserve budget for native completion");
  for(int i=0;i<1000;i++)BurnPlaybackTrace.Event(traceFx,"renderer-paint-rewind");
  Check(GloomhavenVR.Core.VRLog.Lines.Count==18,"A burn diagnostic episode must stay bounded");
  for(int i=0;i<1000;i++){BurnPlaybackTrace.Begin(traceFx,false);BurnPlaybackTrace.Event(traceFx,"bounded-session");}
  Check(GloomhavenVR.Core.VRLog.Lines.Count==128,"A long Debug session must cap native burn trace output");
  Check(source.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==0&&drawn.GetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"))==0,"Tracing must not write the original or renderer material");
  GloomhavenVR.Core.VRLog.WantsDebug=false;
  Console.WriteLine($"Burn replay: {count} runtime assertions passed.");
 }
}
