using System;
internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    internal static void Main()
    {
        var actor=new CPlayerActor(4); var other=new CPlayerActor(5); var card=new CAbilityCard(31);
        RemoteBoardFocus.Actors[4]=actor; RemoteBoardFocus.Actors[5]=other;
        actor.CharacterClass.LostAbilityCards.Add(card);
        var burn=new Burn { ActorId=4, OriginalCard=card, Widget=new AbilityCardUI { AbilityCard=card } };
        var hold=new BurnFixture(); hold._burns.Add(burn);
        Check(ReferenceEquals(hold.PresentationActor,actor),"Native burn keeps its presentation actor");
        var nativeWidget=burn.Widget; burn.Widget=null;
        Check(ReferenceEquals(hold.PresentationActor,actor),"Temporary native widget loss cannot cancel the exact original's burn hold");
        burn.Widget=nativeWidget;
        var active=new ActiveFixture(); var board=new BoardFixture {_latchedActor=actor};
        foreach (int simulatedFrame in new[]{1,2,60,300,36000})
        {
            active._owner.HoldsBurnCardLayout=hold.PresentationActor!=null;
            board._owner.HoldsBurnCardLayout=hold.PresentationActor!=null;
            active.Refresh(actor); board.SeatSlots(actor,true,false);
            Check(active.Reflows==0,"Pending burn must not compact the active grid");
            Check(board.Replacements==0,"Pending burn must not replace the first recess");
            Check(active.Pulses>0,"Layout hold must not freeze native active highlights");
            Check(active._cards[1].Holds>0&&board._cards[1].Holds>0,"Sibling keeps original presentation while native effects tick");
        }
        burn.HandoverLogged=true;
        active._owner.HoldsBurnCardLayout=hold.PresentationActor!=null; board._owner.HoldsBurnCardLayout=hold.PresentationActor!=null;
        active.Refresh(actor); board.SeatSlots(actor,true,false);
        Check(active.Reflows==1&&board.Replacements==1,"Completed burn releases both layouts");
        burn.HandoverLogged=false; actor.CharacterClass.LostAbilityCards.Clear();
        Check(hold.PresentationActor==null,"Recovery cancels an obsolete burn hold");
        actor.CharacterClass.PermanentlyLostAbilityCards.Add(card);
        Check(ReferenceEquals(hold.PresentationActor,actor),"Permanent loss also holds layout");
        RemoteBoardFocus.Actors.Remove(4);
        Check(hold.PresentationActor==null,"Scenario teardown cannot retain an old actor");
        RemoteBoardFocus.Actors[4]=actor;
        burn.Active=false; Check(hold.PresentationActor==null,"Completed presentation leaves no layout lock");
        var from=new CardAppearanceState {SourceActorId=4,Original=card};
        var to=new CardAppearanceState {SourceActorId=4,Original=card};
        MirrorFixture.From=from; MirrorFixture.To=to;
        MirrorFixture.Frames[7]=new MirrorFixture.Frame {Previous=new CardAppearanceSnapshot(9.9f),Current=new CardAppearanceSnapshot(10.1f)};
        foreach(float progress in new[]{0f,.1f,.5f,.99f})
        {
            MirrorFixture.Progress=progress;
            Check(!MirrorFixture.HasPresentedThrough(7,actor,card,10f),"Release must wait while native interpolation still contains pre-completion output");
        }
        MirrorFixture.Progress=1f;
        Check(MirrorFixture.HasPresentedThrough(7,actor,card,10f),"Drained owner completion permits motion");
        Check(!MirrorFixture.HasPresentedThrough(7,actor,card,11f),"Later release cannot consume an older completion");
        Check(!MirrorFixture.HasPresentedThrough(8,actor,card,10f),"Another board stream cannot authorize this burn");
        to.Original=new CAbilityCard(31);
        Check(!MirrorFixture.HasPresentedThrough(7,actor,card,10f),"Same seat or id cannot substitute another original card");
        to.Original=card;to.SourceActorId=0;
        Check(!MirrorFixture.HasPresentedThrough(7,actor,card,10f),"Legacy output cannot acknowledge a causal release");
        to.SourceActorId=4;MirrorFixture.Available=false;
        Check(!MirrorFixture.HasPresentedThrough(7,actor,card,10f),"Unresolved original output never means animation completed");
        MirrorFixture.Available=true;MirrorFixture.Progress=0f;MirrorFixture.From=to;
        Check(MirrorFixture.HasPresentedThrough(7,actor,card,10f),"First terminal sample paints directly without invented old output");
        MirrorFixture.From=from;MirrorFixture.Frames[7].Previous=new CardAppearanceSnapshot(10.01f);
        Check(MirrorFixture.HasPresentedThrough(7,actor,card,10f),"Continued samples entirely after completion cannot hold forever");
        Check(MirrorFixture.HasPresentedThrough(7,actor,card,-1f),"Legacy release path remains compatible");
        MirrorFixture.Frames[7].Presented[card]=(4,10.1f,to);
        MirrorFixture.Available=false;
        Check(MirrorFixture.HasPresentedThrough(7,actor,card,10f),"A previously presented terminal frame remains acknowledged after page rotation");
        actor.CharacterClass.HandAbilityCards.Add(card);
        Check(!MirrorFixture.HasPresentedThrough(7,actor,card,10f),"Recovery invalidates the old terminal acknowledgement before original reuse");
        actor.CharacterClass.HandAbilityCards.Clear();
        MirrorFixture.Frames[7].Presented.Clear(); MirrorFixture.Available=true;
        var pending=new BurnFixture.PendingRelease {CompletionTime=10f, ActorId=4, OriginalCard=card, PresentationPlayer=7};
        hold._watchActor=4;hold._pendingReleases.Add(pending);
        actor.CharacterClass.PermanentlyLostAbilityCards.Clear();actor.CharacterClass.RoundAbilityCards.Add(card);
        to.ActorId=4;to.FaceCode=9;MirrorFixture.Frames[7].Current!.States=new[]{to};
        Check(ReferenceEquals(hold.PresentationActor,actor),"Event before model holds the prior actor without treating old Round membership as recovery");
        to.FaceCode=NetProtocol.HeldFaceListHand;
        Check(hold.PresentationActor==null,"Causal owner recovery cancels an obsolete unmatched release");
        pending.OriginalCard=null;
        Check(ReferenceEquals(hold.PresentationActor,actor),"Unknown original cannot fabricate recovery");
        pending.FallbackPlayed=true;
        Check(hold.PresentationActor==null,"Already consumed release leaves no pending hold");
        pending.FallbackPlayed=false;hold._watchActor=5;
        Check(hold.PresentationActor==null,"Unrelated focus must never be retargeted by a pending release");
        hold._watchActor=4;RemoteBoardFocus.Actors.Remove(4);
        Check(hold.PresentationActor==null,"Destroyed actor releases unresolved causal hold");
        RemoteBoardFocus.Actors[4]=actor;
        CardAppearanceProvenance.Originals[(4,0,32)]=card;
        var completed=new CardBurnCompletionHistory { Entries=new[]{new CardBurnCompletion(1,4,4,0,32,10f)} };
        var receiver=new RemoteAvatar(); NetAvatarDriver.Controller=receiver;
        receiver.ApplyBurnCompletions(completed);
        Check(receiver.Dispatched==0,"Joining after historical losses must not create an orphan burn claim");
        receiver.ApplyBurnCompletions(completed);
        Check(receiver.Dispatched==0,"Adopted historical terminal state stays inert on repetition");
        receiver=new RemoteAvatar(); NetAvatarDriver.Controller=receiver;
        receiver._burnFx._burns.Add(new Burn {ActorId=4,OriginalCard=card,Widget=new AbilityCardUI {AbilityCard=card}});
        receiver.ApplyBurnCompletions(completed);
        Check(receiver.Dispatched==1,"First packet during an observed burn must release its native hold");
        receiver.ApplyBurnCompletions(completed);
        Check(receiver.Dispatched==1,"Repeated durable terminal receipt must not replay a burn");
        receiver=new RemoteAvatar(); NetAvatarDriver.Controller=receiver;
        receiver.ApplyBurnCompletions(new CardBurnCompletionHistory());
        receiver.ApplyBurnCompletions(completed);
        Check(receiver.Dispatched==1,"Empty initial history does not suppress a later durable burn");
        completed.Entries[0]=new CardBurnCompletion(1,4,4,0,32,20f);
        receiver.ApplyBurnCompletions(completed);
        Check(receiver.Dispatched==2,"New generation of the exact original survives sequence wrap without legacy62");
        receiver=new RemoteAvatar(); NetAvatarDriver.Controller=receiver;
        CardAppearanceProvenance.Originals.Clear(); NetAvatarDriver.OwnershipReady=false;
        receiver.ApplyBurnCompletions(completed);
        CardAppearanceProvenance.Originals[(4,0,32)]=card; NetAvatarDriver.OwnershipReady=true;
        receiver.ApplyBurnCompletions(completed);
        Check(receiver.Dispatched==0,"Late roster arrival cannot turn initial historical loss into a live burn");
        receiver=new RemoteAvatar(); NetAvatarDriver.Controller=receiver;
        receiver.ApplyBurnCompletions(new CardBurnCompletionHistory());
        var incomingProgress=new CardBurnCompletionHistory {Entries=new[]{new CardBurnCompletion(0,5,4,0,32,21f,true)}};
        receiver.ApplyBurnCompletions(incomingProgress);
        Check(receiver.HasBurnInProgress(5)&&receiver.Dispatched==0,"Owner progress must never dispatch a completed burn or flight");
        var incomingHold=new BurnFixture {_watchActor=4};
        RemoteBoardFocus.Requested=other;
        Check(ReferenceEquals(incomingHold.PresentationActor,actor),"Explicit owner progress retains the previous card actor during an incoming switch");
        receiver.ApplyBurnCompletions(new CardBurnCompletionHistory());
        Check(incomingHold.PresentationActor==null&&!receiver.HasBurnInProgress(5),"Unadopted native completion releases incoming focus without requiring a nonexistent flight");
        receiver.ApplyBurnCompletions(incomingProgress);
        receiver.ApplyBurnCompletions(new CardBurnCompletionHistory {Entries=new[]{new CardBurnCompletion(0,5,4,0,32,22f)}});
        Check(receiver.Dispatched==0,"A deferred incoming burn ending with a terminal receipt must not replay its historical animation");
        RemoteBoardFocus.Requested=null;
        Check(BurnReleasePolicy.RetireWithoutFlight(true,false,false,false),"Observed offscreen native completion can retire without inventing a flight");
        Check(!BurnReleasePolicy.RetireWithoutFlight(true,true,false,false),"Running owner progress retains the burn");
        Check(!BurnReleasePolicy.RetireWithoutFlight(true,false,true,false),"An actual terminal release must retain its native flight path");
        Check(!BurnReleasePolicy.RetireWithoutFlight(true,false,false,true),"No-flight retirement still waits for native presentation completion");
        Check(!BurnReleasePolicy.RetireWithoutFlight(false,false,false,false),"Unobserved absence cannot authorize no-flight retirement");
        var runningFlight=new RunningFlightFixture();
        runningFlight._owner.HoldsBurnCardLayout=true; Time.unscaledTime=10f;
        runningFlight.Tick(.1f);
        Check(runningFlight.ActiveTicks==1,"Already launched remote flights keep moving during a later burn");
        Check(runningFlight.Admissions==0 && runningFlight._pending.Count==1,"Later pending flights stay queued throughout a burn");
        runningFlight._owner.HoldsBurnCardLayout=false; runningFlight.Tick(.1f);
        Check(runningFlight.ActiveTicks==2 && runningFlight.Admissions==1,"Queued flight resolves after burn without expiring in the wait");
        receiver=new RemoteAvatar();NetAvatarDriver.Controller=receiver;
        receiver.ApplyBurnCompletions(new CardBurnCompletionHistory());
        receiver.ApplyBurnCompletions(new CardBurnCompletionHistory {Entries=new[]{new CardBurnCompletion(0,4,4,0,32,31f,false,true)}});
        Check(receiver.Dispatched==0 && receiver.TryBurnNoFlightCompletion(4,card,out float noFlightClock)&&noFlightClock==31f,
            "An explicit no-flight terminal survives coalesced progress and never dispatches a flight");
        Check(!receiver.TryBurnNoFlightCompletion(4,new CAbilityCard(31),out _),"No-flight completion cannot attach to another original card with the same id");
        receiver.ApplyBurnCompletions(new CardBurnCompletionHistory {Entries=new[]{new CardBurnCompletion(0,4,4,0,32,32f,true)}});
        Check(!receiver.TryBurnNoFlightCompletion(4,card,out _),"A new owner burn invalidates the old no-flight completion before playback");
        // The actual production discovery method must survive pooled widget replacement and
        // absent pile UI without admitting another burn for the same original model card.
        var original=new CAbilityCard(101);
        var burnActor=new CPlayerActor(51);
        var discovery=new BurnDiscoveryFixture();
        discovery.DiscoverBurns(burnActor);
        burnActor.CharacterClass.LostAbilityCards.Add(original);
        discovery._burntBuf.Add(new AbilityCardUI {AbilityCard=original});
        discovery.DiscoverBurns(burnActor);
        Check(discovery.Presented.Count==1,"The initial native loss starts one remote presentation");
        for(int refresh=0;refresh<5;refresh++)
        {
            discovery._burntBuf.Clear();discovery.DiscoverBurns(burnActor);
            discovery._burntBuf.Add(new AbilityCardUI {AbilityCard=original});
            discovery.DiscoverBurns(burnActor);
            Check(discovery.Presented.Count==1,"Pile rebuild must not replay an original burn");
        }
        burnActor.CharacterClass.LostAbilityCards.Clear();
        burnActor.CharacterClass.HandAbilityCards.Add(original);discovery.DiscoverBurns(burnActor);
        Check(discovery.Presented.Count==1,"Stale lost UI cannot burn a recovered original");
        burnActor.CharacterClass.HandAbilityCards.Clear();burnActor.CharacterClass.LostAbilityCards.Add(original);
        discovery.DiscoverBurns(burnActor);
        Check(discovery.Presented.Count==2,"Genuine recovery permits a later independent burn");
        burnActor.CharacterClass.LostAbilityCards.Clear();burnActor.CharacterClass.PermanentlyLostAbilityCards.Add(original);
        discovery._burntBuf.Clear();discovery.DiscoverBurns(burnActor);
        discovery._burntBuf.Add(new AbilityCardUI {AbilityCard=original});discovery.DiscoverBurns(burnActor);
        Check(discovery.Presented.Count==2,"Moving between lost populations does not restart a burn");
        var historicalDiscovery=new BurnDiscoveryFixture();historicalDiscovery.DiscoverBurns(burnActor);
        historicalDiscovery._burntBuf.Add(new AbilityCardUI {AbilityCard=original});historicalDiscovery.DiscoverBurns(burnActor);
        Check(historicalDiscovery.Presented.Count==0,"Historical originals seed even before their UI exists");
        var delayed=new CAbilityCard(102);burnActor.CharacterClass.LostAbilityCards.Add(delayed);
        historicalDiscovery._burntBuf.Add(new AbilityCardUI {AbilityCard=delayed,ArcMember=false});historicalDiscovery.DiscoverBurns(burnActor);
        Check(historicalDiscovery.Presented.Count==0,"Placeholder widgets do not consume the burn claim");
        historicalDiscovery._burntBuf[1].ArcMember=true;historicalDiscovery.DiscoverBurns(burnActor);
        Check(historicalDiscovery.Presented.Count==1,"A newly usable original widget starts its pending burn exactly once");
        historicalDiscovery._burntBuf.Add(new AbilityCardUI {AbilityCard=delayed});historicalDiscovery.DiscoverBurns(burnActor);
        Check(historicalDiscovery.Presented.Count==1,"Duplicate simultaneous wrappers still identify only one native card");
        var sameId=new CAbilityCard(102);burnActor.CharacterClass.LostAbilityCards.Add(sameId);
        historicalDiscovery._burntBuf.Add(new AbilityCardUI {AbilityCard=sameId});historicalDiscovery.DiscoverBurns(burnActor);
        Check(historicalDiscovery.Presented.Count==2,"Different originals never share a claim merely because an id matches");

        // Execute ApplyNativeAppearance itself: a temporary Lost address miss is neither a
        // recovery nor a licence to erase the owner's already painted burn material state.
        foreach(NativeOutputFixture.FxSurface surface in Enum.GetValues<NativeOutputFixture.FxSurface>())
        {
            MirrorFixture.Available=true;MirrorFixture.From=MirrorFixture.To=new CardAppearanceState();
            var output=new NativeOutputFixture {Surface=surface};
            output.SetNativeAppearance(7,burnActor,original);
            int before=output.Clears;
            MirrorFixture.Available=false;
            for(int frame=0;frame<4;frame++) output.ApplyNativeAppearance();
            Check(output._nativeOutputApplied&&output.Clears==before,"Missing lost-list samples must not clear a painted burn to blue");
            MirrorFixture.Available=true;output.ApplyNativeAppearance();
            Check(output.Paints==2,"The next native frame resumes the existing burn presentation");
            MirrorFixture.Available=false;burnActor.CharacterClass.HandAbilityCards.Add(original);
            output.ApplyNativeAppearance();
            Check(!output._nativeOutputApplied,"Actual recovery releases the previous burn output");
            burnActor.CharacterClass.HandAbilityCards.Clear();
            MirrorFixture.Available=true;output.SetNativeAppearance(7,burnActor,original);
            MirrorFixture.Available=false;output.SetNativeAppearance(7,burnActor,sameId);
            Check(!output._nativeOutputApplied,"Retargeting never transfers the previous original's burn picture");
        }
        Console.WriteLine($"Remote burn sequencing: {assertions} assertions passed.");
    }
}
