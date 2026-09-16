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
            burn.FullCard.Playing=false;d.NativeLossActive=true;d.Tick();Check(d.Pending&&d.Renders==0,"Owning-hand loss must finish before layout replacement");
            d.NativeLossActive=false;d.Tick();Check(!d.Pending&&d.Flights==1&&d.Renders==1&&sibling.Seat==0,"Native completion must resume pending layout without a new event");
            d.Tick();Check(d.Flights==1,"Completed loss must not replay");
        }
        {var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);d.Add(c);d.Requested.Add(Card(new CPlayerActor()));c.GameCard!.Playing=true;d.Tick();Check(d.Pending&&d.Renders==0,"Native burn before model commit must retain the previous character");c.GameCard.Playing=false;d.Tick();Check(!d.Pending&&d.Renders==1,"No model loss must not invent a flight or strand the layout");}
        {var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);d.Add(c);actor.CharacterClass.LostAbilityCards.Add(c.GameCard!.AbilityCard!);d.Known(c.GameCard);d.Requested.Add(c);d.Tick();Check(!d.Pending&&d.Flights==0,"Already-burnt browse cards must not restart a hold");}
        {var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var c=Card(actor);c.Parked=true;c.FullCard.Playing=true;d.Add(c);d.Tick();Check(!d.Pending,"Hidden pooled artwork must not strand another layout");c.Parked=false;c.IsFlying=true;d.Tick();Check(!d.Pending,"A departed flight must not block the next layout");c.IsFlying=false;c.IsVanishing=true;d.Tick();Check(!d.Pending,"An unrelated vanish must not become a burn barrier");c.IsVanishing=false;c.IsHeld=true;c.FullCard.Playing=false;actor.CharacterClass.LostAbilityCards.Add(c.GameCard!.AbilityCard!);d.Tick();Check(!d.Pending,"Held card must not invent an unlaunchable pending flight");}
        {Time.unscaledTime=0;var actor=new CPlayerActor();var d=new CardsDriver();d.Bind(actor);var first=Card(actor);var second=Card(actor,1);d.Add(first);d.Add(second);actor.CharacterClass.LostAbilityCards.Add(first.GameCard!.AbilityCard!);actor.CharacterClass.LostAbilityCards.Add(second.GameCard!.AbilityCard!);first.FullCard.Playing=true;d.Tick();Time.unscaledTime=5;d.Tick();Check(d.Pending&&d.Flights==0&&d.Renders==0,"Every burning card must contribute to the shared layout barrier");first.FullCard.Playing=false;second.FullCard.Playing=true;d.Tick();Check(d.Pending&&d.Flights==0,"A second burn must finish before either source departs");second.FullCard.Playing=false;d.Tick();Check(!d.Pending&&d.Flights==2&&d.Renders==1,"A completed burn batch must release every source once");}
        Console.WriteLine($"Burn layout: {checks} assertions passed.");
    }
}
