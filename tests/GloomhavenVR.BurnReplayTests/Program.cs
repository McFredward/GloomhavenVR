using System.Collections;
using GloomhavenVR.Cards;
using static ScenarioRuleLibrary.CBaseCard;
static class Program {
 static int count;
 static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
 static (CardEffects,ScenarioRuleLibrary.CAbilityCard) Make(ECardPile pile){var card=new ScenarioRuleLibrary.CAbilityCard{CurrentCardPile=pile};var fx=new CardEffects();fx.Full.AbilityCard=card;return(fx,card);}
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
  var(changed,cc)=Make(ECardPile.Lost);changed.ToggleEffect(true,CardEffects.FXTask.BurnCard);changed.Full.AbilityCard=new(){CurrentCardPile=ECardPile.Lost};changed.ToggleEffect(true,CardEffects.FXTask.LostMode);Check(changed.Starts==2,"Recycled widget identity must not inherit another card's burn");
  var(adopted,ac)=Make(ECardPile.Discarded);CardFace.Owner=new(){fullAbilityCard=adopted.Full,AbilityCard=ac};adopted.Full.AbilityCard=null;adopted.ToggleEffect(true,CardEffects.FXTask.BurnCard);ac.CurrentCardPile=ECardPile.Lost;adopted.ToggleEffect(true,CardEffects.FXTask.LostMode);Check(adopted.Starts==1,"Adopted widget identity must win over missing or stale full-card metadata");CardFace.Owner=null;
  var(authoritative,stale)=Make(ECardPile.Hand);authoritative.Full.playerActor=new();authoritative.Full.playerActor.CharacterClass.LostAbilityCards.Add(stale);authoritative.ToggleEffect(true,CardEffects.FXTask.BurnCard);while(authoritative.Live!.MoveNext()){}authoritative.RestoreCard();Check(authoritative.Paint>=1,"Authoritative lost membership must override a stale hand pile stamp");
  authoritative.Full.playerActor.CharacterClass.LostAbilityCards.Clear();authoritative.Full.playerActor.CharacterClass.HandAbilityCards.Add(stale);stale.CurrentCardPile=ECardPile.Lost;authoritative.RestoreCard();Check(authoritative.Paint==0,"Authoritative hand recovery must override a stale lost pile stamp");
  var(recover,rc)=Make(ECardPile.Lost);recover.ToggleEffect(true,CardEffects.FXTask.BurnCard);rc.CurrentCardPile=ECardPile.Hand;recover.RestoreCard();Check(recover.Paint==0,"Actual model recovery must restore normal artwork");
  var(retired,rtc)=Make(ECardPile.Lost);retired.ToggleEffect(true,CardEffects.FXTask.BurnCard);var obsolete=retired.Live!;BurnArtwork.RetireBurnPlayback(retired);obsolete.MoveNext();retired.RestoreCard();Check(retired.Paint==0,"A retired iterator cannot recreate an episode on pooled artwork");
  var(cancelled,cnc)=Make(ECardPile.Lost);cancelled.ToggleEffect(true,CardEffects.FXTask.BurnCard);cancelled.Live!.Dispose();cancelled.ToggleEffect(true,CardEffects.FXTask.BurnCard);Check(cancelled.Starts==2,"Disposed native playback cannot leave a permanent replay guard");
  var(activated,act)=Make(ECardPile.Round);activated.ToggleEffect(true,CardEffects.FXTask.BurnCard);act.CurrentCardPile=ECardPile.Activated;activated.RestoreCard();Check(activated.Paint>0,"Activation must not interrupt an in-progress original burn");while(activated.Live!.MoveNext()){}activated.RestoreCard();Check(activated.Paint==0,"A completed activation must permit the native clean active-card look");
  var(normal,nc)=Make(ECardPile.Hand);normal.ToggleEffect(true,CardEffects.FXTask.DiscardMode);Check(normal.Discards==1,"Unrelated normal discard presentation must remain native");
  Console.WriteLine($"Burn replay: {count} runtime assertions passed.");
 }
}
