using System.Collections.Generic;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using MapRuleLibrary.Party;
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
/// <para>3 — AND THE MODE OUTLIVES THE PRESS, WHICH IS WHY 184's REMEDY WAS NOT ENOUGH
/// (ModBuild 195). 184 fixed the CLOSE and assumed the player would use it. In the map room he
/// does not have to: <see cref="ModalFallback.MapRoomParallel"/> keeps every window open in
/// parallel by explicit ruling — <i>"Anders als in Flat soll es hier möglich sein mehrere Fenster
/// parallel offen zu haben zB Kirche zum Spenden UND Händler"</i> — and ModBuild 185 made the
/// character screen PERMANENT and X-less — <i>"Das CharacterUI Fenster soll gar kein 'x' haben, das
/// soll hier in der Phase nicht schließbar sein."</i> So the room's normal state is: merchant open
/// on the left, character screen open on the right, both permanently visible, and the game's
/// single-mode machine quietly owning the second one on behalf of the first. The ModBuild 194
/// hardware log is exactly that: the shop window is floated from line 3992 to the end of the
/// session, all fifteen <c>uGUI click: 'Adventure Character Slot'</c> lines fall inside it, and
/// there is no <c>UIWindow SHOWN: 'Campaign Adventure Party Assembly Variant'</c> anywhere. His
/// report: <i>"Ich kann nun in der Character-UI die Character gar nicht mehr öffnen."</i></para>
///
/// <para>THE PRECISE CHAIN, READ FROM SOURCE, because the old instrument said the opposite.
/// <c>EnterShop</c> calls <c>EnableSelectionMode(cb, disableButtons: <b>false</b>)</c>
/// (UIShopItemWindow.cs:79), and that overload's per-slot body is
/// <c>EnableAutoOpenDefaultPanel(false)</c> + <c>EnableSelectCharacter(false)</c> with
/// <c>DisableButtons</c> SKIPPED for assigned slots (NewPartyDisplayUI.cs:1415-1422).
/// <c>NewPartyCharacterUI.OnClick</c> then reads
/// <c>if (autoOpenDefaultPanel &amp;&amp; AreButtonsInteractable &amp;&amp; !GamePadInUse)</c>
/// (:817) and falls to <c>classToggle.group.SetAllTogglesOff()</c> — and it is the class toggle
/// that runs <c>OnCharacterPickerSelected</c> → <c>CharacterSelector.Show</c>, i.e. the assembly
/// window with the character display in it. Both switches are OFF, so the sheet cannot open.
/// Meanwhile <c>IsInteractable</c> is <c>button.IsInteractable() &amp;&amp;
/// slotInteraction.interactable &amp;&amp; buttonsCanvasGroup.interactable</c> (:292) — three
/// fields the <c>disableButtons: false</c> path never touches. THAT is why
/// <c>PARTY SLOTS … 4/4 interactable</c> was true in the same log in which the feature was dead:
/// the line measured the only part of the mechanism the shop leaves alone.</para>
///
/// <para>THE FIX IS A LEVEL-TRIGGERED RE-ARM, NOT A CLOSE. <see cref="ReArmCharacterScreen"/>
/// puts <c>autoOpenDefaultPanel</c> back on, and <c>disabledCharacterSelection</c> back off for
/// ASSIGNED slots, for as long as the map room stands. It is safe against a write war by
/// construction and not merely by hope: the ONLY two callers of either setter in the whole
/// decompile are <c>EnableSelectionMode</c> and <c>DisableSelectionMode</c>, both one-shot mode
/// transitions, so there is no per-frame writer to fight ([[dont-win-a-write-war]]). A counter
/// says so out loud if that ever stops being true.</para>
///
/// <para>WHAT IS DELIBERATELY LEFT ALONE. The shop's own <c>onCharacterSelectedCallback</c> still
/// runs on every slot click, so picking a character still re-points the merchant's inventory at
/// him — the two windows now BOTH answer the click, which is the whole point of a room where they
/// are both open. <c>DISABLE_TOGGLE_OFF_CHARACTER</c> is left set. <c>DisableButtons</c> on EMPTY
/// and AVAILABLE slots is left set, and <c>EnableSelectCharacter</c> is re-armed only for
/// <c>PartySlotState.Assigned</c>: recruiting a NEW mercenary into an empty slot in the middle of a
/// purchase is the one edit the vanilla lock is actually protecting, and nothing in the report asks
/// for it. MULTIPLAYER is unaffected in both directions — nothing here touches the wire, and the
/// two toggles this re-arms keep their own online guards because <c>DetermineInteractability</c>
/// ANDs them (<c>classToggle.interactable = !disabledCharacterSelection &amp;&amp; (!IsOnline ||
/// characterData.IsUnderMyControl)</c>, :1148), so a client still cannot open or reassign a
/// character it does not control.</para>
///
/// <para>4 — AND THE OUTCOME IS MEASURED FROM NOW ON. This is the third time a character-slot
/// click has died quietly here (ModBuild 182, 184, 194) and the second time a green instrument
/// shipped beside the broken feature. <see cref="TickSheetOutcome"/> measures the OUTCOME instead
/// of a precondition: a slot with a character in it became selected, and within
/// <see cref="OutcomeWatchTicks"/> ticks the assembly window either opened or it did not — and if
/// it did not, the line prints every switch <c>OnClick</c> actually branches on plus the name of
/// whatever destination window was floated at that moment. One grep,
/// <c>CHARACTER SHEET OUTCOME</c>, decides the next report.</para>
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

        // ModBuild 195 — the character screen is re-armed BEFORE anything else, and the outcome of
        // the last slot click is judged, on every tick. Both are cheap (a singleton read plus four
        // slots) and both are deliberately independent of whether a destination is floated right
        // now: the mode outlives the window that set it, which is the entire defect.
        ReArmCharacterScreen();
        TickSheetOutcome(floated);

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

    /// <summary>
    /// CONVERT THE PANEL THE FLAT GAME DRAWS AS ONE, NOT THE WINDOW COMPONENT (ModBuild 186).
    /// User: <i>"Alles was den Händler betrifft soll sich in diesem einen Fenster abspielen. Das
    /// betrifft auch die anderen Knöpfe neben dem Händler."</i>
    ///
    /// <para>THE PREMISE THIS WAS BUILT ON WAS WRONG, and the correction matters more than the
    /// mechanism. ModBuild 186 read "the mod-layer sweep moved 56 transforms for 'UI Shop Item
    /// Window' and 1998 for 'Scroll View'" as proof that the inventory is a SIBLING of the shop
    /// window. It is not: 56 is only the FIRST sweep, before the shop's rows pool in, so the
    /// comparison proves nothing at all. 187's <c>WINDOW IDENTITY</c> census measured it instead of
    /// inferring it, and the inventory is four levels INSIDE the shop window
    /// (<c>…/UI Shop Item Window/UI Shop Inventory Variant/Content/Scroll View</c>). The real cause
    /// of the second merchant window was that it floated while the shop window was CLOSED — see
    /// <c>ModalFallback.AncestorWillBeFloated</c>, where it is fixed.</para>
    ///
    /// <para>WHAT IS KEPT, AND WHY. The common-ancestor rule below is not the merchant fix and no
    /// longer claims to be — for the shop it correctly finds nothing outside the subtree and returns
    /// the window unchanged, which the hardware log confirms. It stays because the QUESTION it
    /// answers is real for this family: a destination whose panel genuinely spans more than its own
    /// <c>UIWindow</c> would otherwise be floated in pieces, and the rule costs one reflected field
    /// walk per destination, once.</para>
    ///
    /// <para>So the conversion root moves UP to the nearest common ancestor of the destination
    /// window and everything its own component references. That single change does all of it: the
    /// host now contains both, the flat layout between them is preserved verbatim (nothing is
    /// re-parented, no anchors are touched), and the inventory stops floating on its own by itself
    /// — <c>IsAdoptedByConversion</c> sees a live conversion whose target is now its ancestor.</para>
    ///
    /// <para>BOUNDED, because "walk up until it fits" would eventually reach the full-screen canvas
    /// and float the entire UI. The ancestor is accepted only while its rect stays within
    /// <see cref="MaxAncestorAreaFactor"/> of the window's own, and never when it is the root
    /// canvas. Both outcomes are logged with the numbers, so the next hardware log says which
    /// branch ran and why.</para>
    /// </summary>
    private const float MaxAncestorAreaFactor = 6f;

    internal static RectTransform PreferredConvertRoot(UIWindow window, RectTransform fallback)
    {
        if (!MapRoomDriver.Active || !IsDestination(window))
            return fallback;
        if (_rootChoice.TryGetValue(window, out RectTransform? cached))
            return cached != null ? cached : fallback;

        RectTransform chosen = fallback;
        string why;
        MonoBehaviour? owner = DestinationComponent(window);
        List<Transform> outside = CollectOutsideRoots(owner, window.transform);
        if (owner == null || outside.Count == 0)
        {
            why = "nothing this destination owns lives outside its own subtree — the window itself "
                  + "is already the whole panel";
        }
        else
        {
            Transform? common = window.transform;
            for (int i = 0; i < outside.Count && common != null; i++)
                common = NearestCommonAncestor(common, outside[i]);
            var rect = common as RectTransform;
            var self = window.transform as RectTransform;
            if (rect == null || self == null)
            {
                why = "the common ancestor is not a RectTransform — keeping the window itself";
            }
            else if (IsRootCanvas(rect))
            {
                why = $"the common ancestor '{rect.name}' IS the root canvas — refusing to float the "
                      + "entire screen; the inventory keeps its own float for now";
            }
            else
            {
                float selfArea = Mathf.Abs(self.rect.width * self.rect.height);
                float upArea = Mathf.Abs(rect.rect.width * rect.rect.height);
                if (selfArea > 1f && upArea > selfArea * MaxAncestorAreaFactor)
                {
                    why = $"the common ancestor '{rect.name}' is {upArea / selfArea:F1}× the window's "
                          + $"own area (limit {MaxAncestorAreaFactor:F0}×) — too big to be 'the same "
                          + "panel', so the window itself is kept";
                }
                else
                {
                    chosen = rect;
                    why = $"'{rect.name}' is the nearest ancestor containing the window AND the "
                          + $"{outside.Count} thing(s) it owns outside its own subtree "
                          + $"({upArea / Mathf.Max(1f, selfArea):F1}× its area) — the flat game draws "
                          + "them as one panel and now so does VR, with no re-parenting and no "
                          + "anchor changes";
                }
            }
        }
        _rootChoice[window] = ReferenceEquals(chosen, fallback) ? null : chosen;
        VRLog.Info(Scope, $"GUILDMASTER WINDOW: conversion root for '{window.name}' = "
                          + $"'{chosen.name}' — {why}.");
        return chosen;
    }

    private static readonly Dictionary<UIWindow, RectTransform?> _rootChoice = new();
    private static readonly List<Transform> _outside = new(8);

    /// <summary>The destination MonoBehaviour on this window's own GameObject.</summary>
    private static MonoBehaviour? DestinationComponent(UIWindow window)
    {
        if (window.GetComponent<UIShopItemWindow>() is { } shop) return shop;
        if (window.GetComponent<UITempleWindow>() is { } temple) return temple;
        if (window.GetComponent<UITrainerWindow>() is { } trainer) return trainer;
        if (window.GetComponent<UINewEnhancementWindow>() is { } enh) return enh;
        if (window.GetComponent<UITownRecordsWindow>() is { } rec) return rec;
        return null;
    }

    /// <summary>
    /// Everything this destination REFERENCES that lives outside its own subtree. Read off the
    /// component's own fields rather than guessed by name: a serialized reference is the game's own
    /// statement of "this belongs to me", and it survives a version that renames the objects.
    /// </summary>
    private static List<Transform> CollectOutsideRoots(MonoBehaviour? owner, Transform self)
    {
        _outside.Clear();
        if (owner == null)
            return _outside;
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo[] fields = owner.GetType().GetFields(Flags);
        for (int i = 0; i < fields.Length; i++)
        {
            object? value;
            try
            {
                value = fields[i].GetValue(owner);
            }
            catch
            {
                continue;
            }
            Transform? t = value switch
            {
                Component c when c != null => c.transform,
                GameObject go when go != null => go.transform,
                _ => null,
            };
            if (t == null || t.IsChildOf(self) || self.IsChildOf(t))
                continue;
            if (!_outside.Contains(t))
                _outside.Add(t);
        }
        return _outside;
    }

    private static Transform? NearestCommonAncestor(Transform a, Transform b)
    {
        for (Transform? x = a; x != null; x = x.parent)
        {
            if (b.IsChildOf(x))
                return x;
        }
        return null;
    }

    private static bool IsRootCanvas(Transform t)
    {
        var canvas = t.GetComponent<Canvas>();
        return canvas != null && canvas.isRootCanvas;
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
    /// One line naming how many party slots are interactable.
    ///
    /// <para>THIS LINE IS NOT THE EVIDENCE FOR THE CHARACTER-CLICK REPORT, AND ModBuild 184's
    /// claim that it was cost ModBuild 194 an entire test round. <c>IsInteractable</c> is
    /// <c>button.IsInteractable() &amp;&amp; slotInteraction.interactable &amp;&amp;
    /// buttonsCanvasGroup.interactable</c> (NewPartyCharacterUI.cs:292) — and the merchant and the
    /// temple both call <c>EnableSelectionMode(…, disableButtons: <b>false</b>)</c>, which touches
    /// NONE of those three. So this line read <c>4/4</c> in the very log in which no character could
    /// be opened at all. What it still catches is the OTHER half of the family: the enchantress
    /// (<c>EnableEnhancementMode</c> → <c>disableButtons: true</c>) does set
    /// <c>buttonsCanvasGroup.interactable = false</c>, and there a <c>0/4</c> here is real.</para>
    ///
    /// <para>THE LINE THAT DECIDES THE REPORT IS <c>CHARACTER SHEET OUTCOME</c>
    /// (<see cref="TickSheetOutcome"/>), which measures whether the sheet actually opened rather
    /// than whether one precondition of opening it holds. Transitions only — never per frame.</para>
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
                              + "READ IT AS A PARTIAL: IsInteractable covers only button/slotInteraction/"
                              + "buttonsCanvasGroup, and the merchant and temple enter selection mode with "
                              + "disableButtons=FALSE — so 4/4 here does NOT mean a character can be opened "
                              + "(ModBuild 194's log said 4/4 while nothing opened). 0 here does mean the "
                              + "enchantress's harder lock is on. The line that decides the report is "
                              + "CHARACTER SHEET OUTCOME.");
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

    // -------------------------------------------------------- the permanent character screen --

    /// <summary>Consecutive ticks on which the re-arm had to write something. A guildmaster mode
    /// transition is ONE event, so a healthy re-arm writes on exactly one tick and then reports 0
    /// forever; a number that keeps climbing means somebody is writing these flags per frame and
    /// the reconciler has become a write war it cannot win ([[dont-win-a-write-war]]).</summary>
    private static int _reArmFightTicks;

    /// <summary>Ticks of consecutive fighting after which the war is declared. Three, for the same
    /// reason <c>ModalFallback.StickyFightWarnFrames</c> uses three: a one-shot mode transition is a
    /// single tick, a per-frame writer is unmistakable by the third.</summary>
    private const int ReArmWarTicks = 3;

    private static bool _reArmProbeWarned;

    /// <summary>
    /// PUT THE CHARACTER SCREEN BACK IN CHARGE OF ITS OWN SLOTS (ModBuild 195). See section 3 of
    /// the class doc for the whole chain and for what is deliberately NOT re-armed.
    ///
    /// <para>Level-triggered and idempotent: in the steady state this reads two bools per slot and
    /// writes nothing. Runs only while the map room stands — on the flat screen, in a scenario and
    /// with VR off the guildmaster mode machine keeps byte-identical vanilla behaviour, because
    /// there the merchant and the character screen genuinely are the same screen and the flat
    /// player left one to see the other.</para>
    /// </summary>
    private static void ReArmCharacterScreen()
    {
        if (!MapRoomDriver.Active)
        {
            _reArmFightTicks = 0;
            return;
        }

        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            List<NewPartyCharacterUI>? slots = display != null ? display.CharacterSlots : null;
            if (slots == null || slots.Count == 0)
            {
                _reArmFightTicks = 0;
                return;
            }

            int autoRearmed = 0;
            int selectRearmed = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                NewPartyCharacterUI? slot = slots[i];
                if (slot == null)
                    continue;
                // (a) THE SWITCH OnClick BRANCHES ON. Without this, a slot click runs
                //     classToggle.group.SetAllTogglesOff() and nothing opens.
                if (!slot.autoOpenDefaultPanel)
                {
                    slot.EnableAutoOpenDefaultPanel(isEnabled: true);
                    autoRearmed++;
                }
                // (b) THE SWITCH THE TOGGLE ITSELF BRANCHES ON. Re-arming (a) alone is not enough:
                //     DetermineInteractability keeps classToggle.interactable false while
                //     disabledCharacterSelection stands, so OnClick still falls through to
                //     SetAllTogglesOff. ASSIGNED slots only — see the class doc for why the empty
                //     slot's recruit lock is left exactly as the shop set it.
                if (slot.disabledCharacterSelection && slot.State == PartySlotState.Assigned)
                {
                    slot.EnableSelectCharacter(enable: true);
                    selectRearmed++;
                }
            }

            if (autoRearmed == 0 && selectRearmed == 0)
            {
                _reArmFightTicks = 0;
                return;
            }

            if (_reArmFightTicks == 0)
            {
                VRLog.Info(Scope, $"CHARACTER SCREEN RE-ARMED: {autoRearmed} slot(s) had "
                                  + $"autoOpenDefaultPanel off and {selectRearmed} assigned slot(s) had "
                                  + "character selection disabled — a guildmaster destination "
                                  + "(merchant/temple/trainer/enchantress/records) had put the party display "
                                  + "into SELECTION MODE, in which a slot click means 'pick this character for "
                                  + "that window' and the character sheet is not allowed to open. In the map "
                                  + "room both windows are open at once by user ruling, so the click must do "
                                  + "BOTH: the destination's own callback still fires, and the sheet opens "
                                  + "again. READ IT AS: this line at most once per destination window opened; "
                                  + "if it repeats every tick, see the STUCK line below.");
            }

            if (++_reArmFightTicks == ReArmWarTicks)
            {
                VRLog.Warn(Scope, $"CHARACTER SCREEN RE-ARM STUCK: the party display's selection flags have "
                                  + $"been re-armed {_reArmFightTicks} ticks running. EnableSelectionMode and "
                                  + "DisableSelectionMode are the ONLY writers of these flags in the decompile "
                                  + "and both are one-shot mode transitions, so this can only mean a mode is "
                                  + "being (re-)entered every frame. That is a write war and neither side wins "
                                  + "it: expect the character toggles to flicker rather than settle. If this "
                                  + "line appears, the remedy is to find and stop that per-frame entry, NOT to "
                                  + "re-arm harder.");
            }
        }
        catch (System.Exception ex)
        {
            _reArmFightTicks = 0;
            if (!_reArmProbeWarned)
            {
                _reArmProbeWarned = true;
                VRLog.Warn(Scope, $"CHARACTER SCREEN RE-ARM threw once and is now silent for this session "
                                  + $"({ex.GetType().Name}: {ex.Message}) — if clicking a character in the map "
                                  + "room opens nothing, a guildmaster destination has left the party display "
                                  + "in selection mode and this is the reason the mod did not undo it.");
            }
        }
    }

    // ------------------------------------------------------------------- the outcome instrument --

    /// <summary>How many ticks a click is watched before the outcome is declared. The window opens
    /// synchronously inside <c>classToggle.isOn = true</c> (OnClickAssign → OnCharacterPickerSelected
    /// → CharacterSelector.Show → UIWindow.Show), so one tick would do; thirty is roughly a third of
    /// a second at rig frame rate and costs nothing, so a tween or a deferred frame cannot produce a
    /// false negative.</summary>
    private const int OutcomeWatchTicks = 30;

    private static NewPartyCharacterUI? _lastSelectedSlot;
    private static int _outcomeTicksLeft;
    private static string _outcomeSlotName = string.Empty;
    private static bool _outcomeProbeWarned;

    /// <summary>
    /// DID THE CLICK DO ANYTHING? (ModBuild 195 — the guard.) The old
    /// <see cref="ReportPartySlots"/> line reported a PRECONDITION and reported it green in the very
    /// log in which the feature was dead, because <c>IsInteractable</c> reads the three fields the
    /// shop's <c>disableButtons: false</c> path never touches. An instrument that models a SUBSET of
    /// what the feature needs agrees with every broken build ([[instrument-measures-one-term]]), so
    /// this one measures the END STATE instead.
    ///
    /// <para>ARMED BY: <c>NewPartyDisplayUI.SelectedUISlot</c> changing to a slot that HAS a
    /// character. That is the click the report is about. It is a poll rather than the game's
    /// <c>NewCharacterSelected</c> event on purpose — this class already ticks once per frame and a
    /// subscription would have to survive the display being destroyed and rebuilt between rooms.
    /// The one case it cannot see is a re-click on the ALREADY selected slot (the selection does not
    /// change, and in selection mode <c>DISABLE_TOGGLE_OFF_CHARACTER</c> keeps it from even
    /// deselecting); that case is uninteresting once the sheet is open and it is stated here so the
    /// absence of a line is never read as a pass.</para>
    ///
    /// <para>RESOLVED BY: <c>UIAdventurePartyAssemblyWindow.window.IsOpen</c> — the window the log
    /// knows as <c>Campaign Adventure Party Assembly Variant</c> (ID <c>PartyAssemblyWindow</c>),
    /// reached through the party display's own serialized <c>CharacterSelector</c> reference rather
    /// than by name or by a scene sweep.</para>
    ///
    /// <para>HOW TO READ THE FAILURE LINE: every field it prints is a term <c>OnClick</c> or
    /// <c>DetermineInteractability</c> actually branches on, in the order they are tested, plus the
    /// destination window floated at that moment. <c>autoOpen=False</c> or
    /// <c>selectionDisabled=True</c> means a guildmaster mode owns the display and
    /// <see cref="ReArmCharacterScreen"/> did not undo it. All of those true with
    /// <c>classToggle active=False</c> means the slot is showing the battle-goal toggle instead.
    /// Everything green and still no window means the cause is downstream of the toggle and this
    /// class is no longer the place to look.</para>
    /// </summary>
    private static void TickSheetOutcome(UIWindow? floated)
    {
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            if (display == null)
            {
                _lastSelectedSlot = null;
                _outcomeTicksLeft = 0;
                return;
            }

            NewPartyCharacterUI? selected = display.SelectedUISlot;
            if (!ReferenceEquals(selected, _lastSelectedSlot))
            {
                _lastSelectedSlot = selected;
                // A slot with a character in it just became the selection: that is the click whose
                // outcome the report is about. An empty/available slot opens the recruit picker
                // instead, which is a different question and is deliberately not watched here.
                bool watchable = selected != null && selected.Data != null;
                _outcomeTicksLeft = watchable ? OutcomeWatchTicks : 0;
                _outcomeSlotName = watchable ? SlotLabel(selected!) : string.Empty;
                // NEVER JUDGE ON THE ARMING TICK. The sheet may still be standing open for the
                // PREVIOUS character at this instant: in selection mode a slot click runs
                // SetAllTogglesOff → OnCharacterPickerSelected(false) → TryHideCurrentDisplay, and
                // that close lands in the same frame as the selection change. Reading IsOpen here
                // would report the outgoing window as this click's success — the exact shape of
                // false pass this instrument exists to stop being possible.
                return;
            }

            if (_outcomeTicksLeft <= 0)
                return;

            if (SheetWindowOpen(display))
            {
                _outcomeTicksLeft = 0;
                VRLog.Info(Scope, $"CHARACTER SHEET OUTCOME for {_outcomeSlotName}: OPENED. The party "
                                  + "assembly window (the character display) is open, so the slot click did "
                                  + "what the player asked. This is the OUTCOME, not a precondition — the "
                                  + "'PARTY SLOTS n/m interactable' line above measures something the shop's "
                                  + "selection mode never touches and was green through the whole build in "
                                  + "which this was broken.");
                return;
            }

            if (--_outcomeTicksLeft > 0)
                return;

            NewPartyCharacterUI? slot = _lastSelectedSlot;
            VRLog.Warn(Scope, $"CHARACTER SHEET OUTCOME for {_outcomeSlotName}: DID NOT OPEN within "
                              + $"{OutcomeWatchTicks} ticks. {DescribeSlotGates(slot)} Floated guildmaster "
                              + $"destination right now: {(floated != null ? "'" + floated.name + "'" : "none")}. "
                              + "READ IT AS: the character became the party display's selection and the party "
                              + "assembly window ('Campaign Adventure Party Assembly Variant', ID "
                              + "PartyAssemblyWindow) is still closed. autoOpen=False or selectionDisabled=True "
                              + "means a guildmaster mode still owns the display and the ModBuild 195 re-arm "
                              + "did not undo it; everything green here means the cause is past the toggle and "
                              + "not in GuildmasterDestinations.");
        }
        catch (System.Exception ex)
        {
            _outcomeTicksLeft = 0;
            if (!_outcomeProbeWarned)
            {
                _outcomeProbeWarned = true;
                VRLog.Warn(Scope, $"CHARACTER SHEET OUTCOME probe threw once and is now silent for this "
                                  + $"session ({ex.GetType().Name}: {ex.Message}) — the absence of "
                                  + "CHARACTER SHEET OUTCOME lines from here on proves nothing.");
            }
        }
    }

    /// <summary>Is the party assembly window (the character sheet the report is about) open? Reached
    /// through the display's own serialized reference, never by name — the window is localised and
    /// Unity renames clones.</summary>
    private static bool SheetWindowOpen(NewPartyDisplayUI display)
    {
        UIAdventurePartyAssemblyWindow? selector = display.CharacterSelector;
        UIWindow? window = selector != null ? selector.window : null;
        return window != null && window.IsOpen;
    }

    private static string SlotLabel(NewPartyCharacterUI slot)
    {
        CMapCharacter? data = slot.Data;
        string who = data != null ? data.CharacterID : "<no character>";
        return $"slot {slot.SlotIndex} ('{slot.name}', {who})";
    }

    /// <summary>
    /// Every switch <c>NewPartyCharacterUI.OnClick</c> and <c>DetermineInteractability</c> branch on,
    /// in the order they are tested, so a failure line is decidable without a second round. Written
    /// defensively: a missing sub-object prints as a question mark rather than throwing.
    /// </summary>
    private static string DescribeSlotGates(NewPartyCharacterUI? slot)
    {
        if (slot == null)
            return "The slot itself is gone, so no gate can be read.";
        string classGate = slot.classToggle != null
            ? $"active={slot.classToggle.gameObject.activeSelf} interactable={slot.classToggle.interactable} on={slot.classToggle.isOn}"
            : "<missing>";
        string goalGate = slot.battleGoalToggle != null
            ? $"active={slot.battleGoalToggle.gameObject.activeSelf} interactable={slot.battleGoalToggle.IsInteractable()}"
            : "<missing>";
        return "GATES: "
               + $"autoOpen={slot.autoOpenDefaultPanel}, "
               + $"selectionDisabled={slot.disabledCharacterSelection}, "
               + $"button.interactable={(slot.button != null ? slot.button.interactable.ToString() : "<missing>")}, "
               + $"slotInteraction={(slot.slotInteraction != null ? slot.slotInteraction.interactable.ToString() : "<missing>")}, "
               + $"buttonsCanvasGroup={(slot.buttonsCanvasGroup != null ? slot.buttonsCanvasGroup.interactable.ToString() : "<missing>")}, "
               + $"classToggle[{classGate}], battleGoalToggle[{goalGate}], "
               + $"state={slot.State}, gamepad={InputManager.GamePadInUse}, "
               + $"switchingCharacter={(FFSNetwork.IsOnline && PlayerRegistry.IsSwitchingCharacter)}.";
    }

    /// <summary>Module teardown — forget everything, restoring the banner if we still hold it.</summary>
    internal static void Reset()
    {
        ReleaseBanner("module teardown");
        _rootChoice.Clear();
        _homeMode = EGuildmasterMode.WorldMap;
        _reArmFightTicks = 0;
        _reArmProbeWarned = false;
        _lastSelectedSlot = null;
        _outcomeTicksLeft = 0;
        _outcomeSlotName = string.Empty;
        _outcomeProbeWarned = false;
    }
}
