using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;
static class Program {
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception(message);}
    static VRCard Card(CPlayerActor owner,int seat=0) => new(){GameCard=new(){PlayerActor=owner,AbilityCard=new()},Seat=seat};
    static void Main() {
        foreach(string flow in new[]{"played lost half", "short rest", "long rest", "one-card damage", "two-card damage", "active expiry"}) {
            Time.unscaledTime=0;var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);
            var burn=Card(actor);var sibling=Card(actor,1);d.Add(burn);d.Add(sibling);d.Requested.Add(sibling);
            if(flow=="active expiry") d.ActiveCard(burn);
            actor.CharacterClass.LostAbilityCards.Add(burn.GameCard!.AbilityCard!);
            d.Tick();Check(d.Pending&&d.Renders==0&&sibling.Seat==1,"Model-first loss must retain the outgoing layout");
            Check(d.Dirty&&d.GrabBlocks>0&&d.HalfBlocked,"Retained card input must wait and rebuild must remain scheduled");
            if(flow=="active expiry") Check(d.HasSource(burn.GameCard),"Active source must be captured before layout changes");
            Time.unscaledTime=.2f;burn.FullCard.Playing=true;
            for(int i=0;i<20;i++){Time.unscaledTime+=1;d.Tick();Check(d.Pending&&d.Renders==0&&d.Flights==0&&sibling.Seat==1,"Live burn must never admit sibling movement or replacement");}
            Check(d.ArtworkObserved(burn.GameCard!),"The layout barrier must retain actual artwork observations before releasing its hold");
            Check(d.ForeignProgressObservations >= 40,"Foreign progress must be observed during the native barrier before Flush can run");
            burn.FullCard.Playing=false;d.NativeLossActive=true;d.Tick();Check(d.Pending&&d.Renders==0,"Owning-hand loss must finish before layout replacement");
            d.NativeLossActive=false;d.Tick();Check(!d.Pending&&d.Flights==1&&d.Renders==1&&sibling.Seat==0,"Native completion must resume pending layout without a new event");
            d.Tick();Check(d.Flights==1,"Completed loss must not replay");
        }
        {
            Time.unscaledTime=0;var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);d.Add(c);
            actor.CharacterClass.PermanentlyLostAbilityCards.Add(c.GameCard!.AbilityCard!);c.FullCard.Playing=true;d.Tick();
            Check(d.Holds==1&&d.ArtworkObserved(c.GameCard),"A permanent loss already animating at discovery must be recorded as observed");
            c.FullCard.Playing=false;Time.unscaledTime=.4f;d.Tick();
            Check(d.Holds==1&&d.ArtworkObserved(c.GameCard)&&d.Flights==0,"A quiet frame must retain the observation and original startup grace");
            Time.unscaledTime=.51f;d.Tick();Check(d.Flights==1,"Observed completion must release exactly one permanent-loss flight");
        }
        {var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);d.Add(c);d.Requested.Add(Card(new CPlayerActor()));c.GameCard!.Playing=true;d.Tick();Check(d.Pending&&d.Renders==0,"Native burn before model commit must retain the previous character");c.GameCard.Playing=false;d.Tick();Check(!d.Pending&&d.Renders==1,"No model loss must not invent a flight or strand the layout");}
        {var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);d.Add(c);actor.CharacterClass.LostAbilityCards.Add(c.GameCard!.AbilityCard!);d.Known(c.GameCard);d.Requested.Add(c);d.Tick();Check(!d.Pending&&d.Flights==0,"Already-burnt browse cards must not restart a hold");}
        {var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);c.Parked=true;c.FullCard.Playing=true;d.Add(c);d.Tick();Check(!d.Pending,"Hidden pooled artwork must not strand another layout");c.Parked=false;c.IsFlying=true;d.Tick();Check(!d.Pending,"A departed flight must not block the next layout");c.IsFlying=false;c.IsVanishing=true;d.Tick();Check(!d.Pending,"An unrelated vanish must not become a burn barrier");c.IsVanishing=false;c.IsHeld=true;c.FullCard.Playing=false;actor.CharacterClass.LostAbilityCards.Add(c.GameCard!.AbilityCard!);d.Tick();Check(!d.Pending,"Held card must not invent an unlaunchable pending flight");}
        {Time.unscaledTime=0;var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var first=Card(actor);var second=Card(actor,1);d.Add(first);d.Add(second);actor.CharacterClass.LostAbilityCards.Add(first.GameCard!.AbilityCard!);actor.CharacterClass.LostAbilityCards.Add(second.GameCard!.AbilityCard!);first.FullCard.Playing=true;d.Tick();Time.unscaledTime=5;d.Tick();Check(d.Pending&&d.Flights==0&&d.Renders==0,"Every burning card must contribute to the shared layout barrier");first.FullCard.Playing=false;second.FullCard.Playing=true;d.Tick();Check(d.Pending&&d.Flights==0,"A second burn must finish before either source departs");second.FullCard.Playing=false;d.Tick();Check(!d.Pending&&d.Flights==2&&d.Renders==1,"A completed burn batch must release every source once");}
        {
            var outgoing=new CPlayerActor();var incoming=new CPlayerActor();var d=new CardsDriver();d.Bind(outgoing);
            var old=Card(outgoing);d.Add(old);var burning=Card(incoming);var replacement=Card(incoming,1);
            var hand=new CardsHandUI{PlayerActor=incoming};hand.cardsUI.Add(burning.GameCard!);hand.cardsUI.Add(replacement.GameCard!);
            incoming.CharacterClass.LostAbilityCards.Add(burning.GameCard!.AbilityCard!);
            burning.GameCard.Playing=true;d.Incoming=hand;d.Requested.Add(replacement);
            int observed=GloomhavenVR.Net.CardAppearanceSampler.Observed;
            for(int i=0;i<20;i++){Time.unscaledTime+=1;d.Tick();Check(d.Pending&&d.Renders==0&&d.Flights==0&&d.Drawn.Contains(old)&&ReferenceEquals(d.LayoutActor,outgoing),"Incoming native burn must wait before replacing the outgoing character");}
            Check(GloomhavenVR.Net.CardAppearanceSampler.Observed-observed==40,"Every incoming original widget must publish its owner progress before adoption");
            burning.GameCard.Playing=false;hand.AnimatingLostCards=true;hand.animatedLosingCard=true;d.Tick();Check(d.Pending&&d.Renders==0,"Incoming owning-hand loss sequence must finish before focus admission");
            hand.animatedLosingCard=false;d.Tick();Check(!d.Pending&&d.Renders==1&&d.Flights==0,"Completed incoming burns must admit focus without replaying a historical loss");
            hand.animatedLosingCard=true;hand.gameObject.activeInHierarchy=false;d.Tick();Check(!d.Pending,"An inactive cancelled incoming hand must not freeze focus on stale loss flags");
            hand.gameObject.activeInHierarchy=true;hand.AnimatingLostCards=false;d.Tick();Check(!d.Pending,"One incoming native loss flag alone must not strand focus");
            hand.cardsUI.Clear();d.Tick();Check(!d.Pending,"An incoming hand with no native artwork must not invent a burn wait");
            hand.cardsUI.Add(burning.GameCard);burning.GameCard.Playing=true;d.Tick();Check(d.Pending,"A new incoming burn must re-arm admission from actual native playback");
            d.Incoming=null;d.Tick();Check(!d.Pending,"Cancelling incoming focus must release its presentation-only wait");
        }
        {
            var outgoing=new CPlayerActor();var foreign=new CPlayerActor();var d=new CardsDriver();d.Bind(outgoing);
            var old=Card(outgoing);d.Add(old);d.Incoming=new CardsHandUI{PlayerActor=foreign};d.Requested.Add(Card(foreign));
            GloomhavenVR.Net.CardAppearanceMirror.Pending.Add(foreign);
            d.Tick();Check(d.Pending&&d.Renders==0&&ReferenceEquals(d.LayoutActor,outgoing),"Foreign incoming owner progress must retain the admitted local view when its native proxy is inactive");
            GloomhavenVR.Net.CardAppearanceMirror.Pending.Remove(foreign);
            d.Tick();Check(!d.Pending&&d.Renders==1&&d.Flights==0,"Owner progress removal must admit the foreign view without a nonexistent flight release");
            d.Bind(foreign);GloomhavenVR.Net.CardAppearanceMirror.Pending.Add(foreign);d.Tick();
            Check(d.Pending&&d.Renders==1,"Owner progress arriving after foreign focus admission must still stop card replacement");
            GloomhavenVR.Net.CardAppearanceMirror.Pending.Remove(foreign);d.Tick();Check(!d.Pending&&d.Renders==2,"A read-only adopted hand must resume when its owner clears native progress");
        }
        {
            Time.unscaledTime=0;var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var card=Card(actor);d.Add(card);
            actor.CharacterClass.LostAbilityCards.Add(card.GameCard!.AbilityCard!);
            Check(CardsDriver.ExpectsBurnFlight(card.GameCard.AbilityCard!),"A fresh controlled adopted loss must retain progress until its real flight is reported");
            card.IsHeld=true;Check(!CardsDriver.ExpectsBurnFlight(card.GameCard.AbilityCard!),"A held card cannot promise an unlaunchable flight");card.IsHeld=false;
            card.GameCard.Playing=true;d.Tick();card.Parked=true;
            Check(CardsDriver.ExpectsBurnFlight(card.GameCard.AbilityCard!),"An existing original burn hold must retain progress independently of its current wrapper pose");
            actor.Controlled=false;Check(!CardsDriver.ExpectsBurnFlight(card.GameCard.AbilityCard!),"Foreign presentation holds must never retain the sender's own progress");
            actor.Controlled=true;var next=new CardsDriver();next.Bind(actor);next.Add(card);card.Parked=false;next.Known(card.GameCard);
            Check(!CardsDriver.ExpectsBurnFlight(card.GameCard.AbilityCard!),"Historical lost-pile browsing must not retain progress for a nonexistent flight");
        }
        {
            Time.unscaledTime=0;var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);
            var original=Card(actor);var model=original.GameCard!.AbilityCard!;
            actor.CharacterClass.LostAbilityCards.Add(model);d.Known(original.GameCard);
            var replacement=Card(actor);replacement.GameCard!.AbilityCard=model;d.Add(replacement);
            d.ObserveRecovery();d.Tick();
            Check(d.KnownModel(replacement.GameCard)&&!d.Pending&&d.Flights==0,"Replacing a completed lost widget must not replay its original burn");
            d.ObserveRecovery();d.ObserveRecovery();
            Check(d.KnownModel(replacement.GameCard),"Empty UI snapshots must not retire a still-lost original model");
            actor.CharacterClass.LostAbilityCards.Clear();actor.CharacterClass.PermanentlyLostAbilityCards.Add(model);d.ObserveRecovery();
            Check(d.KnownModel(replacement.GameCard),"Permanent loss must preserve an existing completed claim");
            actor.CharacterClass.PermanentlyLostAbilityCards.Clear();actor.CharacterClass.HandAbilityCards.Add(model);d.ObserveRecovery();
            Check(!d.KnownModel(replacement.GameCard),"Authoritative recovery must re-arm a real later burn");
            actor.CharacterClass.HandAbilityCards.Clear();actor.CharacterClass.LostAbilityCards.Add(model);d.Tick();
            Check(d.Pending&&d.Holds==1,"Recovered originals must start a new burn hold when actually lost again");
            Time.unscaledTime=1;d.Tick();Check(d.Flights==1,"A recovered and burned original must complete its new flight exactly once");
        }
        {
            Time.unscaledTime=0;var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);
            var old=Card(actor);var replacement=Card(actor);replacement.GameCard!.AbilityCard=old.GameCard!.AbilityCard;
            actor.CharacterClass.LostAbilityCards.Add(old.GameCard.AbilityCard!);d.Add(old);d.Add(replacement);old.FullCard.Playing=true;
            d.Tick();Check(d.Holds==1,"Two widgets for one original must share one pending burn hold");
            old.FullCard.Playing=false;Time.unscaledTime=1;d.Tick();
            Check(d.Flights==1&&!d.Pending,"A replacement native widget must not release a second burn flight");
            replacement.IsFlying=false;d.Tick();Check(d.Flights==1,"A replacement arriving after completion must remain historical");
        }
        {
            var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);
            actor.CharacterClass.LostAbilityCards.Add(c.GameCard!.AbilityCard!);d.SeedClaims();
            d.Add(c);d.Tick();Check(!d.Pending&&d.Flights==0,"Historical native loss with no initial widget must not become a fresh burn on later UI creation");
            var permanent=Card(actor);actor.CharacterClass.PermanentlyLostAbilityCards.Add(permanent.GameCard!.AbilityCard!);d.SeedClaims();
            Check(d.KnownModel(permanent.GameCard),"Initial baselines must include permanently lost originals");
            var other=new CPlayerActor();var foreign=Card(other);other.CharacterClass.LostAbilityCards.Add(foreign.GameCard!.AbilityCard!);d.Add(foreign);d.Tick();
            Check(!d.Pending,"A different character's historical original must never create a local flight claim");
        }
        {
            var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var original=Card(actor);
            var replacement=Card(actor);replacement.GameCard!.AbilityCard=original.GameCard!.AbilityCard;
            actor.CharacterClass.LostAbilityCards.Add(original.GameCard.AbilityCard!);d.SeedClaims();
            Check(d.KnownModel(original.GameCard)&&!d.CompletedModel(replacement.GameCard),"Initial historical seeding must not suppress a first round or active burn handover");
            d.Complete(original.GameCard);
            Check(d.CompletedModel(replacement.GameCard),"A second dock or active wrapper must recognize an actually completed original burn");
            actor.CharacterClass.LostAbilityCards.Clear();actor.CharacterClass.HandAbilityCards.Add(original.GameCard.AbilityCard!);d.ObserveRecovery();
            Check(!d.CompletedModel(replacement.GameCard),"Real recovery must also clear completed round or active flight claims");
        }
        Console.WriteLine($"Burn layout: {checks} assertions passed.");
    }
}
