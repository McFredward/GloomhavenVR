using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string why)
    { _checks++;if(!value)throw new InvalidOperationException(why); }
    private static void Main()
    {
        QueueCases(); CallerCases(); NativeCancellationCases(); RestCueCases(); ProgressHelpCases(); DecisionDelayCases(); HelpLocaleCases();
        Console.WriteLine($"Card progress lifetime: {_checks} assertions passed.");
    }
    private static void QueueCases()
    {
        CardActionQueue.Clear();
        int actions=0,done=0,cancelled=0;
        CardActionQueue.Enqueue(()=>actions++,()=>done++,()=>cancelled++);
        Check(CardActionQueue.Pending==1,"Queue admission retains one pending action");
        CardActionQueue.Clear();
        Check(actions==0 && done==0 && cancelled==1,"Cancellation notifies owner without native action or ordinary completion");
        CardActionQueue.Clear();Check(cancelled==1,"Repeated clearing must not notify a canceled owner twice");
        CardActionQueue.Enqueue(()=>actions++,()=>done++,()=>cancelled++);CardActionQueue.Pump();
        Check(actions==1&&done==1&&cancelled==1,"Normal pump invokes action and completion without cancellation");
        int errors=VRLog.Errors;
        CardActionQueue.Enqueue(()=>throw new InvalidOperationException("native action"),()=>done++,()=>cancelled++);
        CardActionQueue.Pump();Check(done==2&&cancelled==1&&VRLog.Errors==errors+1,"Throwing action preserves ordinary completion cleanup");
        CardActionQueue.Enqueue(()=>{},()=>throw new InvalidOperationException("completion"));CardActionQueue.Pump();
        Check(VRLog.Errors==errors+2,"Throwing completion is isolated");
        CardActionQueue.Enqueue(()=>{},null,()=>throw new InvalidOperationException("cancellation"));
        CardActionQueue.Enqueue(()=>actions++,()=>done++,()=>cancelled++);CardActionQueue.Clear();
        Check(cancelled==2&&actions==1&&done==2,"Throwing cancellation cannot strand later owners");
        Check(VRLog.Errors==errors+3,"Cancellation failure is reported");
        CardActionQueue.Enqueue(()=>{},null,()=>CardActionQueue.Enqueue(()=>actions++,()=>done++));
        CardActionQueue.Clear();Check(CardActionQueue.Pending==1,"Reentrant cancellation enqueue belongs to next pump");
        CardActionQueue.Pump();Check(actions==2&&done==3,"Reentrant replacement work remains executable");
        int cancels=0;
        CardActionQueue.Enqueue(()=>{CardActionQueue.Clear();},()=>done++,()=>cancels++);
        CardActionQueue.Enqueue(()=>actions++,()=>done++,()=>cancels++);CardActionQueue.Pump();
        Check(actions==2&&done==4&&cancels==1,"Clear during action cancels queued tail, not currently executing action");
        var order=new List<int>();
        CardActionQueue.Enqueue(()=>order.Add(1));CardActionQueue.Enqueue(()=>order.Add(2));
        CardActionQueue.Pump();Check(order.Count==1&&order[0]==1,"Pump retains one native action per frame");
        CardActionQueue.Pump();Check(order.Count==2&&order[1]==2,"Pump preserves FIFO after cancellation paths");
    }
    private static void CallerCases()
    {
        // Execute the actual four queue-owning driver methods, then the actual scene release.
        // Both context labels intentionally use the same queue: no network authority is changed.
        foreach(string context in new[]{"singleplayer","multiplayer-owner"})
        for(int which=0;which<4;which++)
        {
            CardActionQueue.Clear();var hand=new CardsHandUI();
            CardsGameApi.LongRest=hand;CardsGameApi.Drift=new CPlayerActor();
            CardsGameApi.NativeCalls=0;Time.unscaledTime+=10;Time.frameCount+=10;
            var driver=new CardsDriver();CardsDriver.Instance=driver;
            driver.Request(which,hand);
            Check(driver.Busy(which),$"{context} actual caller {which} arms its in-flight latch");
            Check(CardActionQueue.Pending>0,$"{context} actual caller {which} enqueues work");
            CardsDriver.ReleaseCardsBeforeSceneLoad();
            Check(driver.Released,$"{context} native scene release reached surviving driver");
            Check(!driver.Busy(which),$"Aborted scene load must release caller latch {which}");
            Check(CardActionQueue.Pending==0&&CardsGameApi.NativeCalls==0,$"{context} cancellation must not advance native action {which}");
            Check(driver.Dirty,$"{context} canceled owner requests a fresh authoritative rebuild");
            Time.unscaledTime+=10;Time.frameCount+=10;driver.Request(which,hand);
            Check(driver.Busy(which)&&CardActionQueue.Pending>0,$"{context} same driver can offer caller {which} after aborted load");
            while(CardActionQueue.Pending>0)CardActionQueue.Pump();
            Check(!driver.Busy(which)&&CardsGameApi.NativeCalls>0,$"{context} replacement caller {which} completes normally");
        }
        CardsDriver.Instance=null;CardsGameApi.LongRest=null;CardsGameApi.Drift=null;
    }
    private static CardsHandUI Hand()
    { var hand=new CardsHandUI();hand.cardsUI.Add(new AbilityCardUI());hand.BeginAnimation();return hand; }
    private static void NativeCancellationCases()
    {
        foreach(string context in new[]{"singleplayer","multiplayer-owner","multiplayer-observer"})
        {
            var hand=Hand();int answers=0;hand.onAnimateCardLostCallback=()=>answers++;
            hand.Disable();
            Check(!hand.AnimatingLostCards,$"Successful native cancellation must release its animation-liveness flag ({context})");
            Check(!hand.animatedLosingCard&&answers==1,$"{context} cancellation retains exactly one original completion callback");
            Check(hand.animations.Count==0&&hand.Stops==1,$"{context} original native tween/coroutine cleanup runs");
            Check(hand.verticalLayoutGroup.enabled&&hand.layoutElement.minHeight==0&&hand.givenCardsGroup.Resets==1,$"{context} native layout restored");
            Check(!UIManager.Instance.Locked&&!Singleton<UINavigation>.Instance.StateMachine.Locked,$"{context} native input lock released");
            Check(hand.cardsUI[0].Cancels==1&&hand.cardsUI[0].Updates==1,$"{context} native card cleanup unchanged");
            Check(!LongRestWait(hand),$"{context} original long-rest WaitWhile predicate no longer blocks");
            hand.Cancel();Check(answers==1&&hand.Stops==1,$"{context} repeat cancel cannot repeat native answer");
            hand=Hand();hand.CompleteAnimation();hand.Cancel();
            Check(!hand.AnimatingLostCards&&hand.Stops==0,$"{context} normal completion is untouched");
            hand=new CardsHandUI{AnimatingLostCards=true};hand.Cancel();
            Check(hand.AnimatingLostCards&&hand.Stops==0,$"{context} unrelated stale/no-owned-animation state is not guessed away");
            hand=Hand();var error=new InvalidOperationException("original callback failed");hand.onAnimateCardLostCallback=()=>throw error;
            Exception? caught=null;try{hand.Cancel();}catch(Exception ex){caught=ex;}
            Check(ReferenceEquals(caught,error)&&hand.AnimatingLostCards,$"{context} native failure must propagate without successful-cancellation repair");
            hand=Hand();hand.onAnimateCardLostCallback=()=>hand.BeginAnimation();hand.Cancel();
            Check(hand.AnimatingLostCards&&hand.animatedLosingCard,$"{context} reentrant running replacement must retain animation flag");
            hand=Hand();hand.onAnimateCardLostCallback=()=>{ProductionHooks.Start(hand);hand.AnimatingLostCards=true;};hand.Cancel();
            Check(hand.AnimatingLostCards&&!hand.animatedLosingCard,$"{context} replacement generation protects even unstarted iterator");
            hand=Hand();hand.onAnimateCardLostCallback=()=>{ProductionHooks.Start(hand);hand.AnimatingLostCards=false;};hand.Cancel();
            Check(!hand.AnimatingLostCards,$"{context} replacement completed during callback stays completed");
            hand=Hand();var other=Hand();hand.onAnimateCardLostCallback=()=>other.BeginAnimation();hand.Cancel();
            Check(!hand.AnimatingLostCards&&other.AnimatingLostCards,$"{context} another hand's generation cannot poison cancellation ownership");
        }
    }
    // Native Choreographer.WaitTriggerAnyOnLongRestAddActiveBonuses uses this exact predicate;
    // generated source binds its fresh decompilation. No rule continuation is called here.
    private static bool LongRestWait(CardsHandUI hand)=>hand.AnimatingLostCards;
    private static void RestCueCases()
    {
        Check(!CardsGameApi.MustRestInsteadOfPlay((CPlayerActor?)null),"Missing actor never advertises forced rest");
        foreach(CPhase.PhaseType phase in Enum.GetValues<CPhase.PhaseType>())
        foreach(bool extra in new[]{false,true})
        for(int held=0;held<4;held++)for(int laid=0;laid<3;laid++)
        {
            var actor=new CPlayerActor();var hand=new CardsHandUI{PlayerActor=actor};
            for(int i=0;i<held;i++)actor.CharacterClass.HandAbilityCards.Add(i);
            for(int i=0;i<laid;i++)actor.CharacterClass.RoundAbilityCards.Add(new CAbilityCard());
            actor.SelectingCardsForExtraTurnOfType=extra?CAbilityExtraTurn.EExtraTurnType.ExtraTurn:CAbilityExtraTurn.EExtraTurnType.None;
            PhaseManager.PhaseType=phase;
            bool expected=phase==CPhase.PhaseType.SelectAbilityCardsOrLongRest&&!extra&&held+laid<2;
            bool local=CardsGameApi.MustRestInsteadOfPlay(hand);
            bool remote=CardsGameApi.MustRestInsteadOfPlay(actor);
            Check(local==expected&&remote==local,"Local and mirrored rest cue retain native hand+round/phase/extra-turn semantics");
            Check(RestOfferCue.Wanted(local,true,false)==expected,"Final valid pair must not advertise forced rest after leaving fan");
            Check(!RestOfferCue.Wanted(local,false,false)&&!RestOfferCue.Wanted(local,true,true),"Unavailable or selected rest never pulses");
        }
    }

    private static void ProgressHelpCases()
    {
        CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Always;
        var actor=new CPlayerActor();actor.CharacterClass.HandAbilityCards.AddRange(new[]{1,2});
        var hand=new CardsHandUI{PlayerActor=actor,Mode=CardHandMode.CardsSelection};
        var driver=new CardsDriver();PhaseManager.PhaseType=CPhase.PhaseType.SelectAbilityCardsOrLongRest;
        Choreographer.s_Choreographer=new Choreographer();
        GloomhavenVR.Core.Events.ProgressWaitNotice.CurrentHint=null;
        driver.Help(hand);Check(driver.Banner=="progress_choose_cards","Ordinary choice explains two cards or rest");
        int writes=driver.Writes;driver.Help(hand);Check(driver.Writes==writes,"Unchanged player help does not rewrite the banner each frame");
        actor.CharacterClass.HandAbilityCards.Clear();driver.Help(hand);
        Check(driver.Banner=="progress_rest_required","Genuinely insufficient card pool names legal rest alternative");
        actor.CharacterClass.RoundAbilityCards.AddRange(new[]{new CAbilityCard(),new CAbilityCard()});hand.SelectionReady=true;
        driver.Help(hand);Check(driver.Banner=="progress_selection_other","Other owned characters can still need cards before shared confirm");
        CardsGameApi.MayConfirm=true;driver.Help(hand);
        Check(driver.Banner=="progress_selection_confirm","Live original confirm affordance has actionable guidance");
        CardsGameApi.MayConfirm=false;CardsGameApi.MayReady=true;driver.Help(hand);
        Check(driver.Banner=="progress_selection_confirm","Multiplayer ready toggle has the same explicit confirmation hint");
        hand.Confirmed=true;driver.Help(hand);
        Check(driver.Banner=="progress_ready_wait","Already ready players are told to wait, not answer again");
        hand.Owned=false;driver.Help(hand);
        Check(driver.Banner=="progress_other_owner","Foreign focus explains ownership without exposing private card data");
        hand.Owned=true;hand.Confirmed=false;hand.ShortRestChoosing=true;driver.Help(hand);
        Check(driver.Banner==null,"Short-rest sacrifice choice never receives ordinary selection instructions");
        hand.ShortRestChoosing=false;hand.Mode=CardHandMode.ActionSelection;PhaseManager.PhaseType=CPhase.PhaseType.ActionSelection;
        CardsGameApi.MayReady=false;
        driver.Help(hand);Check(driver.Banner=="progress_choose_action","Own action phase explains card or basic action affordance");
        driver.Help(hand,actions:false);Check(driver.Banner==null,"Empty long-rest dock does not advertise a card action");
        CardsGameApi.MayConfirm=true;driver.Help(hand);
        Check(driver.Banner==null,"Live End Turn or Continue does not compete with a choose-action instruction");
        CardsGameApi.MayConfirm=false;
        GloomhavenVR.Core.Events.VRModeStateMachine.TargetingActive=true;driver.Help(hand);
        Check(driver.Banner=="progress_choose_target","Mandatory targeting explains laser use instead of another card action");
        hand.IsTurn=false;Choreographer.s_Choreographer.ThisPlayerHasTurnControl=false;driver.Help(hand);
        Check(driver.Banner==null,"Another player's targeting never asks this player to click");
        GloomhavenVR.Core.Events.VRModeStateMachine.TargetingActive=false;
        GloomhavenVR.Core.Events.ProgressWaitNotice.CurrentHint="native automatic wait";driver.Help(hand);
        Check(driver.Banner=="native automatic wait","Actual long-running automatic wait is never described as rest or card input");
        driver.Help(hand,burning:true);
        Check(driver.Banner=="native automatic wait","Native automatic wait notice survives an animation-only input barrier");
        driver.Help(hand,modal:true);Check(driver.Banner==null,"Genuine blocking modal keeps its own instructions");
        driver.Help(hand,blocked:true);Check(driver.Banner==null,"Blocking overlay retains its own decision and instructions");
        driver.Help(hand,visible:false);Check(driver.Banner==null,"Hidden board does not publish a progress banner");
        driver.Help(null);Check(driver.Banner=="native automatic wait","Empty board still explains actual automatic synchronization wait");
        GloomhavenVR.Core.Events.ProgressWaitNotice.CurrentHint=null;
        driver.Help(null);Check(driver.Banner==null,"Missing hand is not guessed into a required action");
        PhaseManager.PhaseType=CPhase.PhaseType.SelectAbilityCardsOrLongRest;hand.Mode=CardHandMode.CardsSelection;
        driver.Help(hand,burning:true);Check(driver.Banner==null,"An incomplete native burn must not advertise new card input");
    }

    private static void DecisionDelayCases()
    {
        var actor=new CPlayerActor();actor.CharacterClass.HandAbilityCards.AddRange(new[]{1,2});
        var hand=new CardsHandUI{PlayerActor=actor,Mode=CardHandMode.CardsSelection};var driver=new CardsDriver();
        var owner=new Choreographer();Choreographer.s_Choreographer=owner;
        PhaseManager.PhaseType=CPhase.PhaseType.SelectAbilityCardsOrLongRest;
        GloomhavenVR.Core.Events.ProgressWaitNotice.CurrentHint=null;
        CardsConfig.ProgressHelpMode.Value=GloomhavenVR.Defaults.ProgressHelpMode;
        CardsConfig.ProgressHelpDelaySeconds.Value=GloomhavenVR.Defaults.ProgressHelpDelaySeconds;
        Check(CardsConfig.ProgressHelpMode.Value==ProgressHelpMode.AfterDelay&&CardsConfig.ProgressHelpDelaySeconds.Value==150,"New profiles default to delayed 150-second explanations");
        Time.unscaledTime=0;driver.Help(hand);Check(driver.Banner==null,"Delayed text is not shown on first native choice");
        Time.unscaledTime=149;driver.Help(hand);Check(driver.Banner==null,"Configured delay must not be shortened");
        Time.unscaledTime=150;driver.Help(hand);Check(driver.Banner=="progress_choose_cards","Explanation appears at configured delay");
        var remote=new GloomhavenVR.Net.RemotePickBanner();
        var packet=driver.Publish();CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Never;
        remote.Apply(packet.HasPickBanner?packet.PickBannerText:null);
        Check(remote.Visible&&remote.Text=="progress_choose_cards","Remote banner uses owner visibility even when viewer help is Never");
        driver.Help(hand);packet=driver.Publish();remote.Apply(packet.HasPickBanner?packet.PickBannerText:null);
        Check(driver.Banner==null&&!packet.HasPickBanner&&!remote.Visible,"Owner Never hides both local and existing remote banner immediately");
        CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Always;var nativeDriver=new CardsDriver();nativeDriver.Help(hand);
        nativeDriver.ForeignBanner("mandatory element choice");CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Never;nativeDriver.Help(hand);
        Check(nativeDriver.Banner=="mandatory element choice","Never cannot erase an existing mandatory native banner");
        CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Always;nativeDriver.Help(hand);
        Check(nativeDriver.Banner=="mandatory element choice","Always cannot replace an existing mandatory native banner");
        CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Always;driver.Help(hand);Check(driver.Banner!=null,"Always applies immediately during an unchanged choice");
        CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.AfterDelay;CardsConfig.ProgressHelpDelaySeconds.Value=600;driver.Help(hand);
        Check(driver.Banner==null,"Increasing configured delay immediately hides premature guidance");
        Time.unscaledTime=600;driver.Help(hand);Check(driver.Banner!=null,"Updated delay retains actual native choice age");
        actor.CharacterClass.RoundAbilityCards.Add(new CAbilityCard());driver.Help(hand);
        Check(driver.Banner==null,"One accepted card response restarts the delay even when prompt wording stays the same");
        Time.unscaledTime=1200;driver.Help(hand);Check(driver.Banner!=null,"Unchanged native choice eventually regains help");
        actor.CharacterClass.RoundAbilityCards[0]=new CAbilityCard();driver.Help(hand);Check(driver.Banner==null,"Same-count card swap is still a meaningful response");
        Time.unscaledTime=1500;driver.Help(hand,visible:false);driver.Help(hand,modal:true);driver.Help(hand,blocked:true);
        Time.unscaledTime=1800;driver.Help(hand);Check(driver.Banner!=null,"Board visibility and modal/head presentation do not restart the decision clock");
        owner.m_WaitState=new Choreographer.CWaitState();driver.Help(hand);Check(driver.Banner==null,"A new actual native decision context restarts the delay");
        Time.unscaledTime=2400;driver.Help(hand);Check(driver.Banner!=null,"New decision owns its own complete delay");
        driver.Help(null);Time.unscaledTime=5000;driver.Help(null);Check(driver.Banner==null,"An idle missing choice never creates explanations");
        CardsConfig.ProgressHelpDelaySeconds.Value=300;
        GloomhavenVR.Core.Events.ProgressWaitNotice.CurrentHint="progress_finishing_turn";Time.unscaledTime=6000;driver.Help(null);
        Time.unscaledTime=6299;driver.Help(null);Check(driver.Banner==null,"Actual automatic wait shares configured five-minute delay");
        Time.unscaledTime=6300;driver.Help(null);Check(driver.Banner=="progress_finishing_turn","Automatic wait is explained after the same configurable interval");
        Time.unscaledTime=1;driver.Help(null);Check(driver.Banner==null,"Clock rewind cannot inherit stale guidance");
        CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Never;driver.Help(null);Check(driver.Banner==null,"Never also suppresses mod-authored automatic-wait text");
        GloomhavenVR.Core.Events.ProgressWaitNotice.CurrentHint=null;CardsConfig.ProgressHelpMode.Value=ProgressHelpMode.Always;
        PhaseManager.PhaseType=CPhase.PhaseType.EndTurn;driver.Help(hand);Check(driver.Banner==null,"Always still requires a real native choice or wait");
    }

    private static void HelpLocaleCases()
    {
        GloomhavenVR.Core.Loc.CurrentLanguage="English";
        Check(GloomhavenVR.Core.Loc.ProgressHelpModeName(0)=="Always"&&GloomhavenVR.Core.Loc.ProgressHelpModeName(1)=="Never"&&GloomhavenVR.Core.Loc.ProgressHelpModeName(2)=="After a delay","All help choices have English player labels");
        GloomhavenVR.Core.Loc.CurrentLanguage="German";
        Check(GloomhavenVR.Core.Loc.ProgressHelpModeName(0)=="Immer"&&GloomhavenVR.Core.Loc.ProgressHelpModeName(1)=="Nie"&&GloomhavenVR.Core.Loc.ProgressHelpModeName(2)=="Nach Wartezeit","All help choices have German player labels");
        GloomhavenVR.Core.Loc.CurrentLanguage="French";Check(GloomhavenVR.Core.Loc.ProgressHelpModeName(2)=="After a delay","Other UI languages keep readable English fallback");
        var clock=new DecisionHelpClock();var owner=new object();var wait=new object();var actor=new object();
        Check(!clock.Observe("choice",owner,wait,actor,1,0,0,ProgressHelpMode.AfterDelay,float.NaN),"Nonfinite configured delay uses the real shipped default");
        Check(!clock.Observe("choice",owner,wait,actor,1,0,149,ProgressHelpMode.AfterDelay,float.PositiveInfinity),"Invalid delay does not shorten the default");
        Check(clock.Observe("choice",owner,wait,actor,1,0,150,(ProgressHelpMode)99,float.NaN),"Unknown future mode falls back to delayed help like its label");
    }
}
