using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using UnityEngine;
using ScenarioRuleLibrary;

static class Program
{
    static int assertions;
    static void Check(bool condition,string message) { assertions++;if(!condition)throw new Exception(message); }
    static void ConfirmedDemandLifetime()
    {
        foreach(var open in new[]{false,true})
        foreach(var mode in new[]{"consume","refresh","reward"})
        foreach(var callback in new[]{"none","end","replace"})
        {
            var pile=ItemsPile.Create("demand",out var card,out _);
            pile.ConfigureDemand(mode=="refresh",mode=="reward");
            // A refresh of an authored consumable must tap, while a forfeited non-consumable
            // reward must burn. This separates the decision kind from the live item state.
            card.Item!.YMLData!.Usage=mode=="reward"?CItem.EUsageType.None:CItem.EUsageType.Consumed;
            pile.OnChipReleased(card,new(),new());
            if(open)pile.ReopenForTest(card);
            int recessIndex=pile.ClippedChipIndex;
            Check(recessIndex>=0,"Selected original must have a remote recess address before confirmation");
            pile.TickDemandPick(pile.Hand);
            Check(PlayTray.Current!.Confirm,"Native demand readiness must expose a callable confirmation");
            if(callback!="none")CardsGameApi.DuringConfirm=()=>
            {
                pile.TickDemandPick(null);
                if(callback=="replace")
                {
                    CardsGameApi.Refreshing=mode!="refresh";
                    CardsGameApi.LoseReward=false;
                    pile.NewDemand();
                }
            };
            PlayTray.Current.OnConfirm!.Invoke();
            Check(CardsGameApi.Confirms==(mode=="reward"?0:1)&&CardsGameApi.RewardConfirms==(mode=="reward"?1:0),"Demand must invoke exactly its original native callback");
            Check(card.UsePresentations==1&&pile.Finishing,"Accepted demand must retain exactly one original result presentation");
            Check(card.Burn==(mode!="refresh")&&card.Spent==(mode=="refresh"),"Native callback reentry must preserve the accepted demand's burn/tap classification: "+mode);
            // This is the actual PileViewer.TickStatus off-turn call, before the demand pump.
            // No native animation tick is invented between the commit and this lifecycle edge.
            pile.RetirePlacedCardIfAny("no locally-controlled hand is active");
            Check(card.Collapses==0&&pile.Finishing,"Off-turn retirement must not preempt an accepted demand's native result");
            pile.TickDemandPick(null);
            Check(PlayTray.Current.ItemUseSlotTransform!.gameObject.activeSelf,"Closing demand must keep the native result's parent active");
            Check(!PlayTray.Current.Confirm&&PlayTray.Current.OnConfirm==null,"Completed demand must revoke its confirmation without accepting twice");
            Check(pile.OwnsUseBerth,"Native result must retain the shared berth until its own completion");
            Check(pile.ClippedChipIndex==recessIndex,"Remote original recess address must remain available throughout the result");
            pile.Tick(null);
            Check(PlayTray.Current.ItemUseSlotTransform.gameObject.activeSelf,"Result service must remain live with no current action hand");
            card.FinishPresentation();
            Check(card.Collapses==1&&!pile.Finishing&&!pile.Kept(card),"Native result completion must release original ownership and start exactly one return");
            Check(!pile.OwnsUseBerth&&pile.ClippedChipIndex==-1,"Finished demand must clear the mirrored berth and original recess address");
            Check(!PlayTray.Current.ItemUseSlotTransform.gameObject.activeSelf,"Finished demand must remove the stale Use overlay");
            pile.Tick(null);pile.TickDemandPick(null);pile.Tick(pile.Hand);
            Check(!PlayTray.Current.ItemUseSlotTransform.gameObject.activeSelf&&!pile.Finishing,"Later local-hand ticks must not resurrect the completed Use overlay");
            pile.CompleteAgain(card);
            Check(card.Collapses==1&&!PlayTray.Current.ItemUseSlotTransform.gameObject.activeSelf,"Repeated completion must not create a second return or reopen the overlay");
        }

        foreach(var open in new[]{false,true})
        {
            var pile=ItemsPile.Create("demand",out var card,out _);
            pile.OnChipReleased(card,new(),new());
            if(open)pile.ReopenForTest(card);
            CardsGameApi.ConfirmAccepted=false;
            pile.ConfirmDemand();
            Check(card.UsePresentations==0&&!pile.Finishing,"Refused native confirmation must never start an accepted result");
            Check(open?card.Returns==1:card.Collapses==1,"Refused native confirmation must return the original through its existing home path");
            CardsGameApi.PickerOpen=false;pile.TickDemandPick(pile.Hand);pile.Tick(pile.Hand);
            Check(!pile.OwnsUseBerth&&!PlayTray.Current!.ItemUseSlotTransform!.gameObject.activeSelf,"Refused demand teardown must not leave the overlay or its owner behind");
        }

        foreach(var successor in new[]{"demand","active-dismiss"})
        {
            var pile=ItemsPile.Create("demand",out var card,out _);
            pile.OnChipReleased(card,new(),new());pile.ConfirmDemand();pile.TickDemandPick(null);
            if(successor=="demand")pile.NewDemand();
            else PlayTray.Current!.ReserveActiveDismiss();
            card.FinishPresentation();
            Check(PlayTray.Current!.ItemUseSlotTransform!.gameObject.activeSelf,"Old completion must retain a successor's live berth: "+successor);
            if(successor=="demand")
            {
                Check(pile.OwnsUseBerth,"A new native demand must own its guide after the old result finishes");
                CardsGameApi.PickerOpen=false;pile.TickDemandPick(null);
                Check(!PlayTray.Current.ItemUseSlotTransform.gameObject.activeSelf&&!pile.OwnsUseBerth,"Successor demand must still close normally");
            }
            else
                Check(PlayTray.Current.ActiveDismissOwner&&PlayTray.Current.Confirm&&PlayTray.Current.ActiveDismissCancels==0,"Old result completion must preserve a successor's active-dismiss reservation");
        }
    }
    static void Main()
    {
        foreach(var ownsActive in new[]{false,true})
        foreach(var flow in new[]{"use","choice","bonus","demand","damage"})
        {
            var pile=ItemsPile.Create(flow,out var card,out var sibling);var hand=new VRHand();
            if(ownsActive)PlayTray.Current!.ReserveActiveDismiss();
            Check(pile.IsOpen&&card.Collapses==0,"Opening state must not close or consume an untouched card");
            pile.OnChipReleased(card,new(),hand);
            Check(!PlayTray.Current!.ActiveDismissOwner,"Accepted item must release active dismissal before installing its cap");
            if(ownsActive)Check(CardsDriver.BerthClaims==1,"Every accepted drop must claim the berth exactly once: "+flow);
            Check(PlayTray.Current.ActiveDismissCancels==(ownsActive?1:0)&&PlayTray.Current.ActiveDismissCommits==0,"Replacing active dismissal must cancel its reservation without committing it");
            Check(!pile.IsOpen,"Accepted item placement must close the fan: "+flow);
            Check(card.PendingUse&&card.Collapses==0&&card.Clips==1,"Placed card must remain in its slot: "+flow);
            Check(pile.Kept(card),flow=="demand"?"Demand card must survive close":flow=="damage"?"Take-damage card must survive close":"Used card must survive close: "+flow);
            Check(pile.OwnsUseBerth,"Shared berth must remain owned after accepted item drop: "+flow);
            Check(pile.Owns(card),"Original decision must retain its card: "+flow);
            Check(pile.ClippedChipIndex==1,"Remote recess address must retain the original fan position: "+flow);
            Check(sibling.Collapses==1&&!pile.RootVisible,"Only the remaining fan must fold away: "+flow);
            Check(hand.Pulses==1&&CardsDriver.Sounds==1,"Placement feedback must remain exactly one pulse and sound: "+flow);
            Check(PlayTray.Current!.ItemUseSlotTransform!.gameObject.activeSelf,"Placed item overlay must stay visible: "+flow);
            if(flow is "use" or "choice" or "bonus")
            {
                Check(PlayTray.Current.Confirm,"Original use/choose confirmation must remain visible: "+flow);
                PlayTray.Current.OnConfirm?.Invoke();
                Check(pile.Decisions==1&&PlayTray.Current.ActiveDismissCommits==0,"Item confirm must remain callable after the active dismissal reservation releases: "+flow);
            }
            else
                Check(!PlayTray.Current.Confirm&&PlayTray.Current.OnConfirm==null,"Demand and damage must not retain the former active dismissal cap");
            if(flow=="choice")Check(pile.ChoiceArmed,"Closing must retain the deferred element choice");
            if(flow=="demand")Check(CardsGameApi.Picker.Selected.Contains(card.Item!),"Closing must retain native demand selection");
            if(flow=="damage")Check(card.Item!.SlotState==CItem.EItemSlotState.Selected,"Closing must retain native shield toggle");
            pile.TickClosed();
            Check(pile.Kept(card)&&pile.Owns(card),flow=="demand"?"Demand decision must remain owned while closed":flow=="damage"?"Take-damage decision must remain owned while closed":"Ordinary decision must remain owned while closed: "+flow);
            Check(!pile.IsOpen&&card.Collapses==0,"Closed decision service must never reopen or retire an active placement: "+flow);
            pile.Close();Check(pile.Kept(card)&&card.Collapses==0,"A second close must be harmless: "+flow);
            if(flow is "demand" or "damage")
            {
                card.Holder=hand;
                Check(pile.HeldCandidate(flow,card),"Held survivor must remain a drop/ghost candidate: "+flow);
                Check(pile.ClippedChipIndex==-1,"A held survivor must leave the remote recess");
                card.Holder=null;pile.OnChipReleased(card,new(2),hand);
                Check(card.Collapses==1&&!pile.Kept(card),"Refused closed-fan return must retire the card");
                if(flow=="demand")Check(!CardsGameApi.Picker.Selected.Contains(card.Item!),"Rejected return must release native demand selection");
                else Check(card.Item!.SlotState==CItem.EItemSlotState.Useable,"Rejected return must release native shield toggle");
                pile=ItemsPile.Create(flow,out card,out sibling);pile.OnChipReleased(card,new(),hand);pile.End(flow);
                Check(card.Collapses==1&&!pile.Kept(card)&&!pile.Owns(card),"Finished native decision must retire its closed-fan card: "+flow);
                pile=ItemsPile.Create(flow,out card,out sibling);pile.OnChipReleased(card,new(),hand);
                card.Holder=hand;card.PendingUse=false;pile.ClearPicked(flow);pile.TickClosed();
                Check(pile.Kept(card),"A retrieved card must remain alive while held: "+flow);
                card.Holder=null;pile.OnChipReleased(card,new(),hand);
                Check(card.PendingUse&&pile.Kept(card)&&pile.Owns(card)&&card.Collapses==0,"Releasing over the closed recess must reselect the same card: "+flow);
            }
        }
        foreach(var ownsActive in new[]{false,true})
        foreach(var flow in new[]{"use","choice","bonus","demand","damage"})
        {
            foreach(var refusal in new[]{"distance","disabled","choice-missing","native-rejected"})
            {
                var pile=ItemsPile.Create(flow,out var card,out var sibling);
                var item=card.Item!;
                if(ownsActive)PlayTray.Current!.ReserveActiveDismiss();
                if(refusal=="disabled") { item.Activatable=false;item.Candidate=false;item.Bonus=null; }
                if(refusal=="choice-missing") { if(flow!="choice")continue;item.ChoiceSlot=false; }
                if(refusal=="native-rejected") { if(flow is not("demand" or "damage"))continue;item.Accept=false; }
                pile.OnChipReleased(card,new(refusal=="distance"?2:0),new());
                Check(CardsDriver.BerthClaims==0&&PlayTray.Current!.ActiveDismissOwner==ownsActive,"Refused drop must preserve any active dismissal reservation");
                Check(PlayTray.Current!.ActiveDismissCancels==0&&PlayTray.Current.ActiveDismissCommits==0,"Refused drop must neither cancel nor commit active dismissal");
                Check(pile.IsOpen,"Refused drop must keep the fan open: "+flow+"/"+refusal);
                Check(!pile.Kept(card)&&sibling.Collapses==0,"Refused drop must never create a survivor or fold away siblings");
            }
        }
        ConfirmedDemandLifetime();
        Console.WriteLine($"Item placement production fixture: {assertions} assertions passed.");
    }
}
