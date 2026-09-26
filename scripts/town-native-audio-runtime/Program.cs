using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static UIWindow Window<T>() where T : class, new()
    {
        var window = new UIWindow { name = typeof(T).Name, AudioItemShow = "native-open", AudioItemHide = "native-close" };
        window.Add<T>();
        return window;
    }

    private static void CheckService(UIWindow window)
    {
        Check(TownServiceNativeAudioSilence.ShouldSilence(window), "immersive service identified by exact controller");
        string? state = null;
        TownServiceNativeAudioSilence.BeforeWindowShow(window, ref state);
        Check(state == "native-open" && window.AudioItemShow == string.Empty, "show item muted only during native call");
        var failure = new Exception("fixture");
        Check(ReferenceEquals(TownServiceNativeAudioSilence.AfterWindowShow(window, state, failure), failure)
            && window.AudioItemShow == "native-open", "show item restored through finalizer without swallowing exception");
        state = null;
        TownServiceNativeAudioSilence.BeforeWindowHide(window, ref state);
        Check(state == "native-close" && window.AudioItemHide == string.Empty, "hide item muted only during native call");
        Check(TownServiceNativeAudioSilence.AfterWindowHide(window, state, null) == null
            && window.AudioItemHide == "native-close", "hide item restored through finalizer");
    }

    private static int Main()
    {
        MapRoomDriver.Active = true;
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceEnhancementHandoff.Enabled = true;
        TownServiceNativeAudioSilence.EnsureInstalled();
        Check(VRSession.Harmony!.Targets.Count == 4, "window, cards-display and direct native audio seams installed before map entry");
        CheckService(Window<UIShopItemWindow>());
        CheckService(Window<UITempleWindow>());
        UIWindow enchantress = Window<UINewEnhancementWindow>();
        CheckService(enchantress);

        var cards = new UIPartyCharacterEnhancementAbilityCardsDisplay { Window = enchantress };
        string? cardState = null;
        TownServiceNativeAudioSilence.BeforeDisplay(cards, ref cardState);
        Check(cardState == "card-tab" && cards.audioItemShow == string.Empty,
            "hidden enchantment card list remains silent");
        TownServiceNativeAudioSilence.AfterDisplay(cards, cardState, null);
        Check(cards.audioItemShow == "card-tab", "hidden list item restored after display");

        var ordinary = new UIWindow();
        Check(!TownServiceNativeAudioSilence.ShouldSilence(ordinary), "unrelated window remains audible");
        string? state = null;
        TownServiceNativeAudioSilence.BeforeWindowShow(ordinary, ref state);
        Check(state == null && ordinary.AudioItemShow == "open", "unrelated show remains unchanged");

        WorldUIConfig.ImmersiveTownServices.Value = false;
        UIWindow flat = Window<UIShopItemWindow>();
        Check(!TownServiceNativeAudioSilence.ShouldSilence(flat), "flat preference retains 1.0.6 sound behavior");
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceEnhancementHandoff.Enabled = false;
        Check(!TownServiceNativeAudioSilence.ShouldSilence(flat), "flat hand fallback retains sound behavior");
        TownServiceEnhancementHandoff.Enabled = true;
        MapRoomDriver.Active = false;
        Check(!TownServiceNativeAudioSilence.ShouldSilence(flat), "service window outside 3D map remains audible");
        Check(VRLog.InfoLines.Count == 7, "debug evidence is one bounded line per native show/hide/display edge");

        MapRoomDriver.Active = true;
        Check(TownServiceNativeAudioSilence.BeginAutomaticTransition(EGuildmasterMode.Merchant, "approached merchant"),
            "immersive automatic resident transition arms native audio scope");
        Check(!TownServiceNativeAudioSilence.BeforeNativePlay("native-direct"),
            "direct native audio inside automatic transition is suppressed");
        TownServiceNativeAudioSilence.EndAutomaticTransition(armed: true);
        Check(TownServiceNativeAudioSilence.BeforeNativePlay("physical-map-press"),
            "physical map audio outside automatic scope remains audible");

        MapRoomDriver.Active = true;
        UIWindow leavingMap = Window<UIShopItemWindow>();
        state = null;
        TownServiceNativeAudioSilence.BeforeWindowShow(leavingMap, ref state);
        TownServiceNativeAudioSilence.AfterWindowShow(leavingMap, state, null);
        MapRoomDriver.Active = false;
        state = null;
        TownServiceNativeAudioSilence.BeforeWindowHide(leavingMap, ref state);
        Check(state == "native-close" && leavingMap.AudioItemHide == string.Empty,
            "matching close cue remains silent when map teardown clears Active before Hide");
        TownServiceNativeAudioSilence.AfterWindowHide(leavingMap, state, null);

        MapRoomDriver.Active = true;
        UIWindow disabledBeforeClose = Window<UITempleWindow>();
        state = null;
        TownServiceNativeAudioSilence.BeforeWindowShow(disabledBeforeClose, ref state);
        TownServiceNativeAudioSilence.AfterWindowShow(disabledBeforeClose, state, null);
        WorldUIConfig.ImmersiveTownServices.Value = false;
        MapRoomDriver.Active = false;
        state = null;
        TownServiceNativeAudioSilence.BeforeWindowHide(disabledBeforeClose, ref state);
        Check(state == null && disabledBeforeClose.AudioItemHide == "native-close",
            "disabling immersive presentation restores flat close sound immediately");
        Check(VRLog.InfoLines.Count == 11, "diagnostics remain bounded to suppressed native calls");

        MapRoomDriver.Active = true;
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceEnhancementHandoff.Enabled = true;
        Check(TownServiceNativeAudioSilence.BeginAutomaticTransition(EGuildmasterMode.WorldMap, "left merchant"),
            "automatic departure to map is scoped even though the target is not a resident");
        TownServiceNativeAudioSilence.EndAutomaticTransition(armed: true);
        WorldUIConfig.ImmersiveTownServices.Value = false;
        Check(!TownServiceNativeAudioSilence.BeginAutomaticTransition(EGuildmasterMode.Merchant, "flat merchant"),
            "classic flat presentation never arms audio suppression");
        Console.WriteLine("PASS: " + _checks + " immersive native-audio assertions");
        return 0;
    }
}
