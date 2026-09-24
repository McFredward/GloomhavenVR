using System;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine.UI;

static class Program
{
    private static int _assertions;
    private static void Check(bool value, string reason)
    {
        _assertions++;
        if (!value) throw new Exception("Modal close assertion: " + reason);
    }
    private static void Reset()
    {
        ModalFallback.ResetForTest();
        FlatScreen.RescueScreenActive = false;
        FlatScreen.Requests = 0;
        FlatScreen.LastReason = "";
        GuildmasterDestinations.Exits = GuildmasterDestinations.LeaveCalls = 0;
        MapRoomDriver.Active = true;
        VRLog.Lines.Clear();
    }
    private static void Refused(UIWindow window, string reason)
    {
        ModalFallback.CloseFloatedWindow(window);
        Check(window.IsOpen && window.Hides == 0 && window.Escapes == 0, reason);
        Check(!ModalFallback.Panel.UserClosing && ModalFallback.Panel.WindowCanvasGroup.alpha == 1f,
            "mandatory close cannot release or hide its native content");
        Check(GuildmasterDestinations.LeaveCalls == 0 && ModalFallback.MenuResets == 0,
            "mandatory guard runs before native mode exit and menu mutation");
        Check(FlatScreen.RescueScreenActive && FlatScreen.Requests == 1,
            "refused close presents the original native options on the rescue screen");
        Check(FlatScreen.LastReason.Contains("a mod close request") && !FlatScreen.LastReason.Contains("long-hold"),
            "direct close rescue records its actual input origin");
        Check(!VRLog.Lines.Any(line => line.Contains("MODAL ESCAPE CHORD REDIRECTED")),
            "a stale X is not falsely logged as an escape chord");
        ModalFallback.TickRescueForTest();
        Check(FlatScreen.RescueScreenActive, "open native waiter retains its rescue presentation");
        // Only native continuation closes this window. The mod never invents an answer.
        window.Hide();
        ModalFallback.TickRescueForTest();
        Check(!FlatScreen.RescueScreenActive, "native completion releases the owned rescue screen");
    }
    private static void Main()
    {
        Reset();
        var pooled = new UIWindow();
        Check(!ModalFallback.IsMandatoryDecision(pooled, out _), "initial pooled dialog permits an X");
        Action staleClose = () => ModalFallback.CloseFloatedWindow(pooled);
        // DialogPopup.Show updates escapeKeyAction for each use of the SAME UIWindow;
        // a Hide/Show within one frame need not retire its old converted close plate.
        pooled.escapeKeyAction = UIWindow.EscapeKeyAction.None;
        staleClose();
        Check(pooled.IsOpen && pooled.Hides == 0 && pooled.Escapes == 0,
            "stale close plate cannot hide a newly mandatory pooled dialog");
        Check(!ModalFallback.Panel.UserClosing && FlatScreen.RescueScreenActive,
            "pooled policy change is rechecked before conversion release");

        foreach (object identity in new object[] { new UIEventPanel(), new UIRewardsManager(),
            new UICampaignRewardWindow(), new ItemCardPicker(), new TakeDamagePanel(),
            new UILevelUpWindow(), new UIUnlockLocationFlowManager(), new UICharacterCreatorWindow(),
            new ConfirmationBox() })
        {
            Reset();
            Refused(new UIWindow { Identity = identity }, "known mandatory identity cannot be force-hidden");
        }
        Reset();
        Refused(new UIWindow { Introduction = true }, "introduction callback cannot be bypassed by direct close");
        Reset();
        Refused(new UIWindow { escapeKeyAction = UIWindow.EscapeKeyAction.None }, "native escape refusal cannot be bypassed");

        foreach (var policy in new[] { UIWindow.EscapeKeyAction.Hide, UIWindow.EscapeKeyAction.Skip })
        {
            Reset();
            var menu = new UIWindow { escapeKeyAction = policy };
            ModalFallback.CloseFloatedWindow(menu);
            Check(!menu.IsOpen && menu.Hides == 1 && menu.Escapes == 1,
                "ordinary menu still closes through existing native escape/fallback");
            Check(ModalFallback.Panel.UserClosing && !FlatScreen.RescueScreenActive,
                "ordinary menu uses normal float release without rescue");
        }
        Reset();
        var destination = new UIWindow { Destination = true, escapeKeyAction = UIWindow.EscapeKeyAction.None };
        ModalFallback.CloseFloatedWindow(destination);
        Check(GuildmasterDestinations.Exits == 1 && !destination.IsOpen && !FlatScreen.RescueScreenActive,
            "known map destination retains native mode exit despite its escape policy");
        Reset();
        MapRoomDriver.Active = false;
        Refused(new UIWindow { Destination = true, escapeKeyAction = UIWindow.EscapeKeyAction.None },
            "destination exemption cannot strand native mode outside the map room");
        Reset();
        var permanent = new UIWindow { Permanent = true };
        ModalFallback.CloseFloatedWindow(permanent);
        Check(permanent.IsOpen && permanent.Hides == 0 && FlatScreen.Requests == 0,
            "permanent map furniture retains existing refusal");
        Reset();
        var chord = new UIWindow { escapeKeyAction = UIWindow.EscapeKeyAction.None };
        ModalFallback.RescueForMandatoryDecision(chord, "native wait", 1.2f);
        Check(FlatScreen.LastReason.Contains("long-hold escape chord (1.2s)"), "existing chord reason remains recognizable");
        Check(VRLog.Lines.Any(line => line.Contains("MODAL ESCAPE CHORD REDIRECTED")), "existing chord log token is preserved");
        Reset();
        FlatScreen.RescueScreenActive = true;
        var other = new UIWindow { escapeKeyAction = UIWindow.EscapeKeyAction.None };
        ModalFallback.CloseFloatedWindow(other);
        other.Hide();
        ModalFallback.TickRescueForTest();
        Check(FlatScreen.RescueScreenActive, "direct close rescue never releases another requester's latch");
        Console.WriteLine($"Modal close: {_assertions} assertions passed.");
    }
}
