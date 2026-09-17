internal static class Program
{
    private static int _assertions;
    private static void Check(bool result, string message) { _assertions++; if (!result) throw new Exception(message); }
    private static void Mask(CardsDriver driver, CardsHandUI hand, int expected, string message)
    {
        driver.Tick(hand); Check(driver._tray.Mask == expected, message);
    }
    private static void Main()
    {
        // Walk actual page turns from one-card burns through multiple odd/even discard pages.
        for (int total = 1; total <= 8; total++)
        {
            CardsGameApi.Total = total;
            var driver = new CardsDriver(); var hand = new CardsHandUI();
            int locked = 0;
            while (locked < total)
            {
                int batch = Math.Min(2,total-locked);
                Mask(driver,hand,batch == 1 ? 1 : 3,"New page must advertise only its remaining capacity");
                for (int i=0;i<batch;i++)
                {
                    Check(driver.Target() == i,"New pick must target its current page seat");
                    driver._fieldCards.Add(new VRCard());
                    driver.Prune();
                    Mask(driver,hand,i+1 == batch ? 0 : 2,"Filled positions must stop advertising placement");
                }
                Check(driver.Target() == -1,"Full page must not offer another seat");
                if (locked+batch == total) break;
                foreach (var card in driver._fieldCards) { driver._pickExitFlown.Add(card); card.Parked=true; }
                locked += batch; driver._pickLockedCount=locked;
                driver.Prune();
                Check(driver._pickLockedCount == locked && driver._fieldCards.Count == locked,
                    "Completed page flight must retain selected locked bookkeeping");
                Check(driver._pickExitFlown.Count == locked,"Parked page must retain its undo return-flight claims");
            }
            hand.Popup=true;
            Mask(driver,hand,0,"Native confirmation must advertise no extra placement");
            driver._fieldCards[^1].IsHeld=true;
            Mask(driver,hand,0,"Native confirmation must stay dark before queued grab-to-reopen completes");
            foreach(var card in driver._fieldCards) card.GameCard!.IsSelected=false;
            int queued=driver.Restart();
            Check(queued == locked,"Undo must return every completed page");
            driver.Prune(); hand.Popup=false;
            Check(driver._fieldCards.Count == 0 && driver._pickLockedCount == 0,"Undo must retire all old page selections");
            Mask(driver,hand,total == 1 ? 1 : 3,"Undo must restart the first page capacity");
            hand.Mode=CardHandMode.CardsSelection; hand.PickLive=false;
            Mask(driver,hand,3,"Completed decision must allow normal two-card selection");
            hand.SelectionReady=true;
            Mask(driver,hand,0,"Confirmed normal selection must stay dark");
        }
        foreach (var mode in new[] { CardHandMode.LoseCard, CardHandMode.RecoverDiscardedCard })
        {
            CardsGameApi.Total=2; var driver=new CardsDriver(); var hand=new CardsHandUI { Mode=mode };
            Mask(driver,hand,3,"Two-card sacrifice/recovery must offer both slots");
            driver._fieldCards.Add(new VRCard()); Mask(driver,hand,2,"Two-card sacrifice/recovery must keep the second seat");
            driver._fieldCards.Add(new VRCard()); Mask(driver,hand,0,"Completed sacrifice/recovery must clear both slots");
        }
        {
            CardsGameApi.Total=5; var driver=new CardsDriver(); var hand=new CardsHandUI();
            for(int i=0;i<4;i++) { var card=new VRCard { Parked=true }; driver._fieldCards.Add(card); driver._pickExitFlown.Add(card); }
            driver._pickLockedCount=4; driver._fieldCards[1].GameCard!.IsSelected=false; driver.Prune();
            Check(driver._pickLockedCount==3 && driver._fieldCards.Count==3 && driver._pickExitFlown.Count==3,
                "Removing a deselected locked entry must shift the prefix and keep other pages intact");
            Mask(driver,hand,3,"Revised earlier choice must expose the newly required two-card page");
        }
        // The native per-widget recycle path must maintain the same prefix as rebuild pruning.
        for (int removed = 0; removed < 4; removed++)
        {
            var driver=new CardsDriver();
            for(int i=0;i<4;i++) { var card=new VRCard { Parked=true }; driver._fieldCards.Add(card); driver._pickExitFlown.Add(card); }
            driver._pickLockedCount=4; var visible=new VRCard(); driver._fieldCards.Add(visible);
            var recycled=driver._fieldCards[removed]; driver.Retire(recycled);
            Check(driver._pickLockedCount==3 && driver._fieldCards.Count==4,
                "Recycling a locked page entry must decrease the locked prefix");
            Check(driver.Seat(visible)==0,"Recycling first or middle locked entry must keep final visible card in left recess");
            Check(driver._pickExitFlown.Count==3 && !driver._pickExitFlown.Contains(recycled),
                "Recycling a page entry must retire its exit claim");
            Check(driver.LayoutCalls==1,"Recycled page entry must request one fresh layout");
            driver.Retire(recycled);
            Check(driver._pickLockedCount==3 && driver.LayoutCalls==1,"Repeated recycle must not consume another locked entry");
            driver.Retire(visible);
            Check(driver._pickLockedCount==3 && driver._fieldCards.Count==3,
                "Recycling a live final-page card must preserve all earlier locked pages");
            Check(driver._pickExitFlown.Count==3 && driver.LayoutCalls==2,
                "Recycling an unclaimed live card must leave prior flight claims untouched");
        }
        // Historic burn latches and ordinary unselected seats must still retire.
        var d=new CardsDriver(); var h=new CardsHandUI();
        var oldBurn=new VRCard { Parked=true }; d._fieldCards.Add(oldBurn); d.Prune();
        Check(d._fieldCards.Count == 0,"Historical parked burn without page claim must retire");
        var unselected=new VRCard { Parked=true }; unselected.GameCard!.IsSelected=false;
        d._fieldCards.Add(unselected); d._pickExitFlown.Add(unselected); d._pickLockedCount=1; d.Prune();
        Check(d._fieldCards.Count == 0 && d._pickLockedCount == 0,"Unselected locked page must retire");
        var dead=new VRCard { GameCard=null, Parked=true }; d._fieldCards.Add(dead); d._pickExitFlown.Add(dead); d._pickLockedCount=1; d.Prune();
        Check(d._fieldCards.Count == 0,"Destroyed native card must retire despite its old page claim");
        CardsGameApi.Total=1; var held=new VRCard { IsHeld=true }; d._fieldCards.Add(held);
        Mask(d,h,1,"A lifted card must rearm its own seat when no confirmation blocks input");
        d._fieldCards.Clear(); d._pickReopenBusy=true; d._fieldCards.Add(unselected); d._pickLockedCount=1; d._pickExitFlown.Add(unselected); d.Prune();
        Check(d._fieldCards.Count == 1,"Queued native reopen must retain its transient selection bookkeeping");
        d._pickReopenBusy=false; d.Prune(); Check(d._fieldCards.Count == 0,"Reopen completion must retire deselected seats");
        CardsGameApi.Total=3;
        d.Exiting=true; Mask(d,h,0,"Page flights must finish before new hints appear"); d.Exiting=false;
        d._overlayGateBlocked=true; Mask(d,h,0,"Native modal gate must suppress placement hints"); d._overlayGateBlocked=false;
        h.Placement=false; Mask(d,h,0,"Foreign/nonplacing hand must never advertise placement"); h.Placement=true;
        h.PickLive=false; Mask(d,h,0,"A latched ended pick must not advertise placement"); h.PickLive=true;
        h.ShortChoosing=true; Mask(d,h,0,"Short-rest random choice has no placement seats"); h.ShortChoosing=false;
        d._tray.IsVisible=false; Mask(d,h,0,"Hidden tray must not retain hint mask"); d._tray.IsVisible=true;
        d.Tick(null); Check(d._tray.Mask==0,"Unbound hand must clear hints");
        h.Mode=CardHandMode.CardsSelection; h.LongRest=true; Mask(d,h,0,"Long-rest selection must clear normal hints"); h.LongRest=false;
        h.ShortRest=true; Mask(d,h,0,"Short-rest selection must clear normal hints"); h.ShortRest=false;
        h.SelectionPhase=false; Mask(d,h,0,"Normal hints must not outlive selection phase"); h.SelectionPhase=true;
        CardsConfig.WantedSlotHint.Value=false; Mask(d,h,0,"Disabled hints must clear the published mask");
        Console.WriteLine($"Pick tray production tests: {_assertions} assertions passed.");
    }
}
