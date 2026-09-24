using System;
using GloomhavenVR.WorldUI;
using UnityEngine;

static class Program
{
    private static int _assertions;
    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new Exception(message);
    }
    private static UILevelUpWindow Fresh()
    {
        AnnouncementContinue.Tick(false);
        Time.frameCount++;
        WorldUIConfig.ConversionActive = true;
        FlatScreen.ManualScreenActive = false;
        FFSNetwork.IsOnline = false;
        Singleton<ESCMenu>.Instance = new();
        Singleton<UINavigation>.Instance = new();
        var owner = Singleton<UILevelUpWindow>.Instance = new();
        owner.Begin();
        return owner;
    }
    private static void Ready(UILevelUpWindow owner)
    {
        owner.cardHolder.FinishHighlight();
        AnnouncementContinue.Tick(true);
    }
    private static void Main()
    {
        var owner = Fresh();
        AnnouncementContinue.Tick(true);
        Check(!AnnouncementContinue.TryConfirm(), "reveal animation gates Continue");
        Ready(owner);
        Check(AnnouncementContinue.CanConfirm && AnnouncementContinueView.Interactable,
            "native reveal completion exposes usable Continue");
        Check(AnnouncementContinue.TryConfirm(), "VR Continue reaches native click tracker");
        Check(owner.nextCardTracker.Clicks == 1 && !owner.enableTracker && !owner.inventory.Interactive,
            "native handoff locks input until unhighlight ends");
        Time.frameCount++;
        Check(!AnnouncementContinue.TryConfirm() && owner.currentCard == 0,
            "no advancement during native unhighlight animation");
        owner.cardHolder.FinishUnhighlight();
        Check(owner.currentCard == 1 && owner.inventory.Added == 1 && !owner.enableTracker,
            "first native card moves to inventory and next reveal starts");
        Ready(owner);
        Time.frameCount++;
        Check(AnnouncementContinue.TryConfirm(), "second reveal has independent Continue");
        owner.cardHolder.FinishUnhighlight();
        Check(owner.inventory.Interactive && !owner.IsShowing && owner.myWindow.IsOpen,
            "last reveal enables original card choice without hiding window");
        AnnouncementContinue.Tick(true);
        Check(!AnnouncementContinue.TryConfirm() && !AnnouncementContinueView.Interactable,
            "selection phase has no generic dismissal");

        owner = Fresh(); Ready(owner);
        owner.nextCardTracker.SkipNextClick = true;
        Check(AnnouncementContinue.TryConfirm() && owner.nextCardTracker.Clicks == 0 && owner.enableTracker,
            "native skip-next-click is consumed without bypass");
        Check(!AnnouncementContinue.TryConfirm(), "same frame cannot advance twice");
        Time.frameCount++;
        Check(AnnouncementContinue.TryConfirm() && owner.nextCardTracker.Clicks == 1,
            "next deliberate press advances after native skip");

        owner = Fresh(); Ready(owner);
        owner.nextCardTracker.enabled = false;
        Check(AnnouncementContinue.TryConfirm(), "controller focus alone does not strand VR announcement");

        owner = Fresh(); Ready(owner);
        owner.isPlayingOpenAnimation = true;
        Check(!AnnouncementContinue.TryConfirm(), "opening animation gates Continue");
        owner.isPlayingOpenAnimation = false; owner.isOpenConfirmationBox = true;
        Check(!AnnouncementContinue.TryConfirm(), "real confirmation owns interaction");
        owner.isOpenConfirmationBox = false; owner.enableTracker = false;
        Check(!AnnouncementContinue.TryConfirm(), "native reveal latch remains authoritative");
        owner.enableTracker = true; owner.myWindow.IsOpen = false;
        Check(!AnnouncementContinue.TryConfirm(), "hidden pooled window rejects stale click");
        owner.myWindow.IsOpen = true; owner.IsShowing = false;
        Check(!AnnouncementContinue.TryConfirm(), "card choice is not an announcement");
        owner.IsShowing = true; owner.nextCardTracker.gameObject.activeInHierarchy = false;
        Check(!AnnouncementContinue.TryConfirm(), "inactive original tracker rejects click");
        owner.nextCardTracker.gameObject.activeInHierarchy = true; owner.character.IsUnderMyControl = false;
        FFSNetwork.IsOnline = true;
        Check(!AnnouncementContinue.TryConfirm(), "observer cannot advance another player's level-up");
        owner.character.IsUnderMyControl = true; Singleton<ESCMenu>.Instance.IsOpen = true;
        Check(!AnnouncementContinue.TryConfirm(), "pause menu gates Continue");
        Singleton<ESCMenu>.Instance.IsOpen = false; FlatScreen.ManualScreenActive = true;
        Check(!AnnouncementContinue.TryConfirm(), "desktop view does not receive duplicate VR input");
        FlatScreen.ManualScreenActive = false; WorldUIConfig.ConversionActive = false;
        Check(!AnnouncementContinue.TryConfirm(), "disabled conversion rejects VR input");
        WorldUIConfig.ConversionActive = true;
        Singleton<UILevelUpWindow>.Instance = new();
        Check(!AnnouncementContinue.TryConfirm(), "old scene source cannot advance new level-up");
        AnnouncementContinue.Tick(false);
        Check(!AnnouncementContinue.TryConfirm(), "teardown removes continuation authority");

        for (int cycle = 0; cycle < 64; cycle++)
        {
            owner = Fresh(); Ready(owner);
            Check(AnnouncementContinue.TryConfirm(), "reopening retains native callback");
            owner.cardHolder.FinishUnhighlight(); Ready(owner); Time.frameCount++;
            Check(AnnouncementContinue.TryConfirm(), "successive native reveals remain usable");
            owner.cardHolder.FinishUnhighlight();
            Check(owner.inventory.Added == 2 && owner.inventory.Interactive,
                "repeated openings never invent dismissal or double-add revealed cards");
        }
        // The game's level-up window is pooled: cycle the SAME source and original listener.
        for (int cycle = 0; cycle < 32; cycle++)
        {
            owner.myWindow.IsOpen = false;
            AnnouncementContinue.Tick(true);
            Check(!AnnouncementContinue.TryConfirm(), "pooled hidden source cannot consume clicks");
            owner.myWindow.IsOpen = true; owner.IsShowing = true; owner.currentCard = 0;
            owner.inventory.Added = 0; owner.inventory.Interactive = false;
            owner.Begin(); Ready(owner); Time.frameCount++;
            Check(AnnouncementContinue.TryConfirm(), "same pooled source reopens with native callback");
            owner.cardHolder.FinishUnhighlight(); Ready(owner); Time.frameCount++;
            Check(AnnouncementContinue.TryConfirm(), "same pooled source reveals each next card");
            owner.cardHolder.FinishUnhighlight();
            Check(owner.inventory.Added == 2 && owner.inventory.Interactive,
                "pooled reopen reaches card selection exactly once");
        }
        Console.WriteLine($"Announcement continuation: {_assertions} assertions passed.");
    }
}
