using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using UnityEngine;
using ScenarioRuleLibrary;

static class Program
{
    static int assertions;
    static void Check(bool condition,string message) { assertions++;if(!condition)throw new Exception(message); }
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
        Console.WriteLine($"Item placement production fixture: {assertions} assertions passed.");
    }
}
