using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using UnityEngine;

static class Program
{
    static int assertions;
    static void Check(bool value,string reason) { assertions++;if(!value)throw new InvalidOperationException(reason); }
    static void Main()
    {
        var d=new CardsDriver();var hand=new VRHand();var card=d.Card(true);
        card.Holder=hand;d.Tick();
        Check(d.Tray.Shown&&!d.Tray.ConfirmShown,"Holding only offers the berth, never confirms");
        Check(d.Tray.Caption=="summon_dismiss_confirm","Summons receive the dismiss caption");
        Check(CardsDriver.ActiveDismissSeat==255&&CardsDriver.ActiveDismissListCount==1,"Held guide publishes no staged seat");
        Check(d.Ghost!=null&&d.Ghost.activeSelf,"Approaching displays the landing preview");
        Check(d.Ghost!.transform.localScale.y>d.Ghost.transform.localScale.x,"Ability preview must be rectangular");
        Check(Math.Abs(d.Ghost.transform.localScale.x-CardsConfig.CardWidth.Value*CardsDriver.ActiveDismissFitScale)<1e-6,"Ghost must match actual placed-card width");
        card.Holder=null;
        Check(d.Drop(card,hand),"Eligible active card must enter the shared berth");
        Check(CardsGameApi.Accepted==0&&d.Presented!.PlayerActor.Cards.Count==1,"Placement must leave the original effect active");
        Check(ReferenceEquals(d.Active.StagedCard,card)&&ReferenceEquals(card.transform.parent,d.Tray.ItemUseSlotTransform),"Stage the same card, not a copy");
        Check(hand.Haptics==1&&CardsDriver.Sounds==1,"One accepted placement gives identical item feedback once");
        Check(!d.Ghost.activeSelf&&d.Tray.ConfirmShown&&d.Tray.ConfirmLabel=="summon_dismiss_confirm","Staging replaces preview with explicit dismiss cap");
        Check(d.Tray.Caption==d.Tray.ConfirmLabel,"One transmitted wording must match destination and cap");
        Check(CardsDriver.ActiveDismissSeat==0,"Staged native seat must be published");
        Check(CardsDriver.ActiveDismissActorId==d.Presented!.PlayerActor.Id,"Staged source publishes its exact original actor");
        d.Tray.SetItemUseSlotVisible(false);d.Tray.SetItemUseConfirmVisible(false,null);d.Tray.SetItemUseAreaLabel(null);
        Check(d.Tray.Shown&&d.Tray.ConfirmShown&&d.Tray.Caption=="summon_dismiss_confirm","Item pumps must not hide or relabel a reserved berth");
        var staleClick=d.Tray.CapClick!;
        d.Grab(card,hand);d.Tick();
        Check(CardsGameApi.Cancelled==1&&CardsGameApi.Accepted==0&&d.Active.StagedCard==null,"Taking back cancels without ending the effect");
        staleClick();Check(CardsGameApi.Accepted==0,"A stale confirm after retrieval must never commit");
        card.Holder=null;Check(d.Drop(card,hand),"Retrieved card may be placed again");
        d.Tray.CapClick!();
        Check(CardsGameApi.Accepted==1&&d.Presented!.PlayerActor.Cards.Count==0&&d.Rebuilds==1,"Explicit confirm commits once and requests model refresh");
        staleClick();Check(CardsGameApi.Accepted==1,"Repeated stale clicks must not repeat native confirmation");

        d=new();card=d.Card();var other=d.Card();
        Check(d.Drop(card,hand),"First active effect stages");
        Check(d.Drop(other,hand)&&CardsGameApi.Cancelled==2,"Replacing a staged effect cancels the previous reservation");
        Check(d.Active.StagedCard==other&&CardsDriver.ActiveDismissSeat==1&&CardsDriver.ActiveDismissListCount==2,"Replacement retains complete native addressing");
        CardsGameApi.Allowed=false;d.Tick();
        Check(!d.Tray.Shown&&d.Active.StagedCard==null&&CardsGameApi.Accepted==1,"Permission loss retires pending presentation without a commit");
        CardsGameApi.Allowed=true;

        d=new();card=d.Card();int requests=CardsGameApi.Requests;
        hand.TriggerUp=false;Check(!d.Drop(card,hand),"Synthetic release cannot stage dismissal");
        hand.GripUp=true;Check(d.Drop(card,hand),"Genuine grip release is also supported");d.Reset();hand.GripUp=false;hand.TriggerUp=true;
        card.transform.position=new(1,0,0);Check(!d.Drop(card,hand),"A release outside the berth must return normally");
        card.transform.position=new(.10f,0,0);d.Tray.ItemUseSlotTransform!.lossyScale=new(.5f,.5f,.5f);
        Check(!d.Drop(card,hand),"Drop capture must scale with the board");
        card.transform.position=Vector3.zero;
        ItemsPile.RecessOwner!.OwnsUseBerth=true;Check(!d.Drop(card,hand),"A pending item owns the shared berth");
        ItemsPile.RecessOwner.OwnsUseBerth=false;
        d.Block(true);Check(!d.Drop(card,hand),"Another modal decision forbids a new dismissal");d.Block(false);
        Check(CardsGameApi.Requests==requests+1,"Rejected releases must not open native confirmation");
        card.Holder=hand;VRHands.Left=new();VRHands.Left.Grabber.Held=new ItemsPile.ItemChip();d.Tick();
        Check(!d.Tray.Shown,"Holding an item must not offer conflicting ability placement");VRHands.Left=null;
        d.Tick();Check(d.Tray.Shown,"Releasing the competing item restores the guide");
        card.Holder=null;Check(d.Drop(card,hand),"Stage before character switch");d.Presented=new();d.Tick();
        Check(d.Active.StagedCard==null&&!d.Tray.Shown,"Character switches must cancel the old actor reservation");

        d=new();card=d.Card();d.Presented!.PlayerActor.Cards.Insert(0,new ScenarioRuleLibrary.CAbilityCard());
        Check(d.Drop(card,hand)&&CardsDriver.ActiveDismissSeat==1&&CardsDriver.ActiveDismissListCount==2,"Missing native widgets must not renumber public model seats");
        d.Tray.IsVisible=false;d.Tick();Check(!CardsDriver.ActiveDismissBerthActive&&CardsDriver.ActiveDismissListCount==0,"Hidden board must clear the complete wire offer");
        d.Tray.IsVisible=true;d.Fake(true);card.Holder=hand;d.Tick();Check(!d.Tray.Shown,"Fake/test hands must not offer gameplay dismissal");

        d=new();card=d.Card(true);other=d.Card(true);
        int accepted=CardsGameApi.Accepted;
        Check(d.Drop(card,hand),"Stage first same-label summon");var oldCap=d.Tray.CapClick!;
        Check(d.Drop(other,hand),"Replace with another same-label summon");
        oldCap();Check(CardsGameApi.Accepted==accepted&&d.Active.StagedCard==other,"Old actual cap callback must not confirm a replacement card");
        d.Tray.CapClick!();Check(CardsGameApi.Accepted==accepted+1,"Replacement cap must bind its own new confirmation even with identical text");
        d.Tick();Check(!d.Tray.Shown,"Native model retirement clears accepted berth");

        d=new();card=d.Card();CardsGameApi.Deferred=true;accepted=CardsGameApi.Accepted;
        Check(d.Drop(card,hand),"Stage a callback whose native model update is delayed");
        d.Tray.CapClick!();d.Tick();
        Check(CardsGameApi.Accepted==accepted+1&&d.Active.StagedCard==card&&d.Tray.Shown&&!d.Tray.ConfirmShown,
            "Accepted card stays in its berth without a second confirm until native model retirement");
        d.Grab(card,hand);card.Holder=null;Check(d.Drop(card,hand)&&CardsGameApi.Accepted==accepted+1,
            "Reading an accepted card returns it to berth without another native confirmation");
        CardsGameApi.Commit(d.Presented!.PlayerActor,card.GameCard.AbilityCard);d.Tick();
        Check(!d.Tray.Shown&&d.Active.StagedCard==null,"Delayed native model removal retires the same staged card");CardsGameApi.Deferred=false;

        d=new();d.Presented!.PlayerActor.Id=17;card=d.Card();Check(d.Drop(card,hand),"Stage before native scene load");oldCap=d.Tray.CapClick!;
        d.Presented=new(){PlayerActor=new(){Id=29}};
        Check(CardsDriver.ActiveDismissActorId==17,"Pending source never changes actor merely because focus changed before the next tick");
        d.Tick();Check(CardsDriver.ActiveDismissActorId==0,"Retired actor source clears its provenance");
        d=new();card=d.Card();Check(d.Drop(card,hand),"Stage a fresh decision before native scene load");oldCap=d.Tray.CapClick!;
        CardsDriver.NativeSceneLoadInProgress=true;oldCap();d.Tick();
        Check(!d.Tray.Shown&&d.Active.StagedCard==null,"Scene loading revokes both physical reservation and stale cap");
        CardsDriver.NativeSceneLoadInProgress=false;
        // The actual launcher/classifier now runs too. Native confirmation remains the sole
        // gameplay authority; neither accepting a callback nor a temporary active gap is a flight.
        d=new();card=d.Card();other=d.Card();d.Presented!.PlayerActor.Cards.Insert(0,new ScenarioRuleLibrary.CAbilityCard());
        d.Tray.ItemUseSlotTransform!.position=new(.2f,.4f,.1f);
        card.transform.position=d.Tray.ItemUseSlotTransform.position;
        Check(d.Drop(card,hand),"Stage the same original before synchronous native removal");
        card.transform.position=new(.23f,.42f,.1f); // cap pressed while the drop is still settling
        var expectedStart=card.transform.position;
        d.Tray.CapClick!();
        Check(card.Flights==0,"Native callback forwarding alone must not launch a flight");
        d.Tick();
        Check(card.Flights==1&&card.FlightFrom==expectedStart&&card.FlightTo==new Vector3(2,0,0),"Confirmed discard flies the same staged original from its actual mid-glide pose");
        Check(ReferenceEquals(card.transform.parent,d.Tray.Root)&&card.gameObject.activeInHierarchy,"Hiding the berth cannot hide or shrink its departing card");
        Check(!d.Tray.Shown&&!d.Tray.ConfirmShown&&d.Active.StagedCard==null,"The flight owns the original before its berth retires");
        Check(GloomhavenVR.Net.NetCardFx.Events.Count==1,"Only the actual launch publishes one flight");
        var sent=GloomhavenVR.Net.NetCardFx.Events.Single();
        Check(sent.From==GloomhavenVR.Net.CardFxAnchor.Active&&sent.To==GloomhavenVR.Net.CardFxAnchor.Discard,"Semantic active endpoint retains its meaning");
        Check(sent.Source is {Seat:1,Count:3}&&sent.Dismiss is {PoolSeat:7,PoolCount:20},"Flight retains complete counted active seat and immutable original pool provenance before removal");
        Check(sent.Dismiss!.BerthPose.Position==expectedStart-d.Tray.ItemUseSlotTransform.position,"Remote origin records the actual launch pose relative to the berth");
        d.RebuildActiveRemoval();d.Tick();d.ContinueFlight(card);
        Check(card.Flights==1&&GloomhavenVR.Net.NetCardFx.Events.Count==1,"A later rebuild or repeated accepted cap cannot duplicate the departure");
        card.Land!();d.ContinueFlight(card);
        Check(d.Parks==1&&!card.gameObject.activeInHierarchy&&card.Flights==1,"The original parks once on arrival and cannot re-fly from the pool");

        foreach(int destination in new[]{1,2})
        {
            CardsGameApi.Destination=destination;CardsGameApi.Deferred=true;
            d=new();card=d.Card(true);Check(d.Drop(card,hand),"Stage an asynchronous native burn dismissal");
            d.Tray.CapClick!();d.Tick();
            Check(card.Flights==0&&d.Active.StagedCard==card,"Accepted asynchronous callback keeps the original staged until native pile commit");
            d.Presented!.PlayerActor.Cards.Remove(card.GameCard.AbilityCard);d.RebuildActiveRemoval();d.Tick();
            Check(card.Flights==0&&d.Tray.Shown,"An active-list gap alone must not invent a pile destination");
            Check(card.gameObject.activeInHierarchy&&d.Active.StagedCard==card,"Actual rebuild zone sweep keeps the accepted original visible across a native list handoff gap");
            d.BurnReleased=false;CardsGameApi.Commit(d.Presented.PlayerActor,card.GameCard.AbilityCard);d.RebuildActiveRemoval();d.Tick();
            Check(card.Flights==0&&card.gameObject.activeInHierarchy&&ReferenceEquals(card.transform.parent,d.Tray.Root),"Native burn artwork holds the same detached original after berth and active-grid removal");
            d.BurnReleased=true;d.ContinueFlight(card);d.Tick();
            Check(card.Flights==1&&card.FlightTo==new Vector3(3,0,0),"Lost and permanently lost dismissals fly only after original burn completion");
            Check(GloomhavenVR.Net.NetCardFx.Events.Single() is {Completion:5f,Dismiss:not null},"Delayed burn preserves berth metadata and native completion on its actual event");
            d.ContinueFlight(card);Check(card.Flights==1,"The completed burn claim prevents a duplicate flight");
        }
        CardsGameApi.Destination=0;CardsGameApi.Deferred=false;

        d=new();card=d.Card();CardsGameApi.Reject=true;Check(d.Drop(card,hand),"Stage before a rejected native callback");
        d.Tray.CapClick!();d.Tick();
        Check(card.Flights==0&&GloomhavenVR.Net.NetCardFx.Events.Count==0&&d.Presented!.PlayerActor.Cards.Contains(card.GameCard.AbilityCard),"Rejected native callback must not move or announce the staged card");
        CardsGameApi.Reject=false;
        d=new();card=d.Card();Check(d.Drop(card,hand),"Stage before cancelling the offer");d.Reset();
        CardsGameApi.Commit(d.Presented!.PlayerActor,card.GameCard.AbilityCard);d.Tick();
        Check(card.Flights==0&&GloomhavenVR.Net.NetCardFx.Events.Count==0,"An unrelated native departure after cancellation must not inherit a berth flight");
        Console.WriteLine($"Active dismissal physical flow: {assertions} assertions passed.");
    }
}
