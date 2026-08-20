using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE GUILDMASTER DESTINATIONS IN THE 3D MAP ROOM — merchant, temple, trainer, enchantress,
/// town records. User, ModBuild 183 round: <i>"Der Händler und co sollte ein separates Fenster
/// sein das spawned inklusive des jeweiligen Hintergrunds (zB vom Händler)."</i>
///
/// <para>WHAT WENT WRONG. Each destination is a <c>UIWindow</c> of its own (all five carry
/// <c>[RequireComponent(typeof(UIWindow))]</c>) and each is a serialized CHILD of
/// <c>UIGuildmasterHUD</c> (decompiled UIGuildmasterHUD.cs:76-92). The HUD's own window is open
/// for as long as the bar is on screen and is permanently refused by the catch-all
/// (<c>IsKnownHudWindow</c> — its VR surface is the table caps). ModBuild 181's "parent wins"
/// rule then refused every destination window because an ANCESTOR was open, and 182's
/// root-canvas exemption let the destination's INNER scroll view through instead. The hardware
/// log shows exactly that: 'Scroll View' floated, the shop window never did, and the merchant
/// appeared as bare rows with no frame and no background. <c>ModalFallback.AncestorWillBeFloated</c>
/// fixes the cause; this class supplies the two things that are specific to these five windows.
/// </para>
///
/// <para>1 — THE BACKGROUND TRAVELS WITH THE WINDOW. The destination's art is a single shared
/// <c>UIGuildmasterBanner</c> that lives in the HUD's own banner container, NOT inside the
/// destination window — so floating the window alone still leaves the merchant's backdrop
/// drawing in a flat HUD nothing renders in VR. The game itself already demonstrates the move:
/// entering the temple runs <c>banner.transform.SetParent(TempleWindow.transform)</c> +
/// <c>SetSiblingIndex(1)</c> (UIGuildmasterHUD.cs:198-200). This class makes the same move for
/// whichever destination is floated, and undoes it on release — never fighting the game, which
/// takes the banner back itself on <c>OnReturnToMap</c> (UIGuildmasterHUD.cs:492).</para>
///
/// <para>2 — CLOSING ONE MUST RUN THE GAME'S OWN EXIT, AND THAT IS WHY THE CHARACTERS WERE DEAD.
/// <c>UIShopItemWindow.EnterShop</c> puts the party display into SELECTION MODE
/// (UIShopItemWindow.cs:79 → <c>NewPartyDisplayUI.PartyDisplay.EnableSelectionMode</c>), which
/// sets <c>buttonsCanvasGroup.interactable = false</c> and <c>EnableSelectCharacter(false)</c> on
/// every slot; <c>UITempleWindow</c> does the same (:111). The ONLY thing that undoes it is the
/// mode's <c>Exit</c> — <c>shopWindow.Exit()</c> / <c>TempleWindow.Exit</c> — which the game runs
/// from <c>UIGuildmasterHUD.UpdateCurrentMode</c> when another mode is selected. In the room the
/// merchant was entered from a table cap and NEVER left, so the party display stayed disabled for
/// the rest of the session. His report: <i>"Ich kann gar nicht mehr auf einen Character in dem
/// Character-UI Fenster klicken … Zuvor hat es korrekt geöffnet."</i> Hiding the window with
/// <c>UIWindow.Hide()</c> would not have helped — it does not touch the mode. So the X on a
/// destination window presses the bar's MAP button instead, exactly as a flat player leaves a
/// shop, and the game's own Exit chain runs.</para>
///
/// <para>NOT PARALLELISM. The guildmaster modes are a state machine with one active mode
/// (<c>UpdateCurrentMode</c> exits the current before entering the next, and
/// <c>toggleGroup.allowSwitchOff</c> is false while a mode is active). Merchant AND temple at the
/// same time is not something the mod can grant without driving that machine into a state the
/// game never produces — the sticky/parallel rule (<c>MapRoomParallel</c>) continues to hold for
/// every window that is NOT a guildmaster mode.</para>
/// </summary>
internal static class GuildmasterDestinations
{
    private const string Scope = "MapRoom";

    /// <summary>The banner's home parent, recorded the first time it is borrowed.</summary>
    private static Transform? _bannerHome;
    private static int _bannerHomeIndex;

    /// <summary>The window the banner is currently parked under, or null.</summary>
    private static UIWindow? _bannerHost;

    /// <summary>Last guildmaster mode that was NOT a destination — where an X returns to.</summary>
    private static EGuildmasterMode _homeMode = EGuildmasterMode.WorldMap;

    private static bool _reflectionTried;
    private static FieldInfo? _bannerField;
    private static FieldInfo? _currentModeField;

    /// <summary>
    /// IS THIS WINDOW ONE OF THE FIVE DESTINATIONS? Matched by COMPONENT on the window's OWN
    /// GameObject — never by containment, and never by name. All five classes carry
    /// <c>[RequireComponent(typeof(UIWindow))]</c> and fetch it with <c>GetComponent</c>, so the
    /// component and the window are provably the same object. (ModBuild 179 and 181 both shipped
    /// the containment version of this test and both had to be corrected: "related to an X" is a
    /// different question from "IS an X".)
    /// </summary>
    internal static bool IsDestination(UIWindow? window) =>
        window != null
        && (window.GetComponent<UIShopItemWindow>() != null
            || window.GetComponent<UITempleWindow>() != null
            || window.GetComponent<UITrainerWindow>() != null
            || window.GetComponent<UINewEnhancementWindow>() != null
            || window.GetComponent<UITownRecordsWindow>() != null);

    /// <summary>
    /// Level-triggered reconciler, one call per tick from <c>ModalFallback.Tick</c>. Takes the
    /// destination window that is floated right now (or null) and makes the banner agree with it.
    /// Idempotent: a steady state costs two reference compares and no writes.
    /// </summary>
    internal static void Reconcile(UIWindow? floated)
    {
        TrackHomeMode();

        if (!MapRoomDriver.Active)
        {
            ReleaseBanner("map room stood down");
            return;
        }

        if (floated == null)
        {
            ReleaseBanner("no destination window is floated");
            return;
        }

        if (ReferenceEquals(_bannerHost, floated))
            return; // already parked where it belongs

        ReleaseBanner("a different destination took over");

        Transform? banner = Banner();
        if (banner == null || floated.transform == null)
            return;

        // The game's own move for the temple (UIGuildmasterHUD.cs:198-200), applied to whichever
        // destination is floated. Sibling index 1 keeps it behind the window's content, which is
        // what makes it read as a BACKGROUND rather than as an overlay.
        _bannerHome = banner.parent;
        _bannerHomeIndex = banner.GetSiblingIndex();
        _bannerHost = floated;
        banner.SetParent(floated.transform, worldPositionStays: false);
        banner.SetSiblingIndex(1);
        VRLog.Info(Scope, $"GUILDMASTER WINDOW: '{floated.name}' floated WITH its background — the shared "
                          + $"UIGuildmasterBanner was moved from '{(_bannerHome != null ? _bannerHome.name : "<none>")}' "
                          + "into the window (sibling 1), the same move the game itself makes when it enters the "
                          + "temple. It goes back the moment this window releases; the game takes it back by "
                          + "itself on OnReturnToMap, and this only restores it if it is still ours.");
        ReportPartySlots($"'{floated.name}' opened");
    }

    /// <summary>Put the banner back if — and only if — it is still parked under our host.</summary>
    private static void ReleaseBanner(string why)
    {
        if (_bannerHost == null)
            return;
        Transform? banner = Banner();
        UIWindow? host = _bannerHost;
        Transform? home = _bannerHome;
        _bannerHost = null;
        _bannerHome = null;
        // ONLY IF IT IS STILL OURS. The game moves the banner itself the moment the mode exits
        // (OnReturnToMap → SetParent(QuestManager)), and that hand-off is the normal case — taking
        // it back from wherever the game has since put it would be winning a write war we have no
        // business being in.
        bool stillOurs = banner != null && host != null && host.transform != null
                         && banner.parent != null && banner.parent.IsChildOf(host.transform);
        if (stillOurs && home != null)
        {
            banner!.SetParent(home, worldPositionStays: false);
            banner.SetSiblingIndex(Mathf.Clamp(_bannerHomeIndex, 0, Mathf.Max(0, home.childCount - 1)));
        }
        VRLog.Info(Scope, $"GUILDMASTER WINDOW: background handed back ({why}).");
        ReportPartySlots(why);
    }

    /// <summary>
    /// LEAVE THE MODE, DO NOT MERELY HIDE THE WINDOW. Called from
    /// <c>ModalFallback.CloseFloatedWindow</c> before the normal Escape/Hide path. Presses the
    /// bar's map button through the SAME dispatch the table caps use, so
    /// <c>UIGuildmasterHUD.UpdateCurrentMode</c> runs the current mode's Exit — which is the only
    /// thing that takes the party display back out of selection mode (see the class doc).
    /// Returns true when the press went out.
    /// </summary>
    internal static bool LeaveMode(UIWindow window, string source)
    {
        if (!MapRoomDriver.Active || !IsDestination(window))
            return false;
        EGuildmasterMode home = _homeMode == EGuildmasterMode.City
            ? EGuildmasterMode.City
            : EGuildmasterMode.WorldMap;
        bool pressed = MapRoomDriver.PressGuildmasterMode(home, $"{source} on '{window.name}'");
        VRLog.Info(Scope, $"GUILDMASTER WINDOW: closing '{window.name}' ({source}) by RETURNING TO "
                          + $"{home} on the game's own bar{(pressed ? "" : " — but no such button is on the bar")}. "
                          + "Hiding the window alone would leave the guildmaster mode active, and with it the "
                          + "party display stuck in selection mode (EnableSelectionMode sets every character "
                          + "slot non-interactable; only the mode's Exit undoes it).");
        return pressed;
    }

    /// <summary>Remember the last non-destination mode, so an X returns where the player came from.</summary>
    private static void TrackHomeMode()
    {
        EnsureReflection();
        if (_currentModeField == null)
            return;
        UIGuildmasterHUD? hud = Hud();
        if (hud == null)
            return;
        object? raw = _currentModeField.GetValue(hud);
        if (raw is not EGuildmasterMode mode)
            return;
        if (mode is EGuildmasterMode.WorldMap or EGuildmasterMode.City)
            _homeMode = mode;
    }

    /// <summary>
    /// One line naming how many party slots are interactable. This is the evidence for the
    /// character-click report: <c>NewPartyCharacterUI.IsInteractable</c> is the exact predicate
    /// <c>OnClick</c> dies on, so a line reading 0/4 while no destination is open proves a stuck
    /// selection mode, and 4/4 proves the fix. Transitions only — never per frame.
    /// </summary>
    private static void ReportPartySlots(string when)
    {
        try
        {
            NewPartyCharacterUI[] slots = Object.FindObjectsOfType<NewPartyCharacterUI>(true);
            int live = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].IsInteractable)
                    live++;
            }
            VRLog.Info(Scope, $"PARTY SLOTS after {when}: {live}/{slots.Length} interactable. "
                              + "IsInteractable is exactly what NewPartyCharacterUI.OnClick tests before it "
                              + "does anything, so 0 here IS the 'clicking a character does nothing' report.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"PARTY SLOTS probe threw: {ex.Message}");
        }
    }

    private static Transform? Banner()
    {
        EnsureReflection();
        UIGuildmasterHUD? hud = Hud();
        if (hud != null && _bannerField?.GetValue(hud) is Component banner && banner != null)
            return banner.transform;
        var sweep = Object.FindObjectOfType<UIGuildmasterBanner>(true);
        return sweep != null ? sweep.transform : null;
    }

    private static UIGuildmasterHUD? Hud() => Object.FindObjectOfType<UIGuildmasterHUD>(true);

    private static void EnsureReflection()
    {
        if (_reflectionTried)
            return;
        _reflectionTried = true;
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        System.Type t = typeof(UIGuildmasterHUD);
        _bannerField = t.GetField("banner", Flags);
        _currentModeField = t.GetField("currentMode", Flags);
        if (_bannerField == null || _currentModeField == null)
            VRLog.Warn(Scope, "GUILDMASTER WINDOW: UIGuildmasterHUD fields not found by name "
                              + $"(banner={_bannerField != null}, currentMode={_currentModeField != null}). "
                              + "The banner falls back to a scene sweep and the X returns to the world map; "
                              + "if a destination window opens without its background, this line is the reason.");
    }

    /// <summary>Module teardown — forget everything, restoring the banner if we still hold it.</summary>
    internal static void Reset()
    {
        ReleaseBanner("module teardown");
        _homeMode = EGuildmasterMode.WorldMap;
    }
}
