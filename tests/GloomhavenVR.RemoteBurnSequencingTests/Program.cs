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
        var burn=new Burn { ActorId=4, Widget=new AbilityCardUI { AbilityCard=card } };
        var hold=new BurnFixture(); hold._burns.Add(burn);
        Check(ReferenceEquals(hold.PresentationActor,actor),"Native burn keeps its presentation actor");
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
        Console.WriteLine($"Remote burn sequencing: {assertions} assertions passed.");
    }
}
