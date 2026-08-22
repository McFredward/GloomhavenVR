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
/// <para>5 — AND IT WAS THE MOD'S MOST EXPENSIVE LINE OF CODE (ModBuild 196). The ModBuild 195
/// hardware log ranks this class as 99 % of the whole ModalFallback step —
/// <c>ModalFallback.Destinations 12.619ms (99%), worst 39.64ms</c> against an 11.11 ms budget,
/// on <c>over-budget 1497/1497 (100.0%)</c> frames, in every one of the 33 breakdown lines of
/// the session and rising from 2.6 ms to 13.2 ms as the room filled up. avg ≈ steady, so by the
/// breakdown line's own reading key it was an unbounded sweep or a per-frame engine call, and it
/// was both: <c>Hud()</c> was <c>Object.FindObjectOfType&lt;UIGuildmasterHUD&gt;(true)</c> — a
/// walk of every loaded object of that type INCLUDING inactive ones — called unconditionally
/// from <see cref="TrackHomeMode"/> on every single tick, plus a reflected
/// <c>FieldInfo.GetValue</c> that boxed an enum 90 times a second.</para>
///
/// <para>THE ANSWER IS NOT A CADENCE, IT IS THE RIGHT QUESTION. <c>UIGuildmasterHUD</c> is
/// declared <c>Singleton&lt;UIGuildmasterHUD&gt;</c> (decompiled UIGuildmasterHUD.cs:18) whose
/// <c>Instance</c> is a plain static field written in <c>Awake</c> and nulled in
/// <c>OnDestroy</c> (Singleton.cs) — the same accessor <c>MapButtonRail</c> has always used. And
/// <c>currentMode</c> has a public accessor, <c>CurrentMode</c> (:122), while the assembly is
/// publicized at build time anyway (GloomhavenVR.csproj:31), so the reflection bought nothing at
/// all. The sweep survives ONLY as a bounded fallback for the window in which the singleton is
/// cold (an inactive HUD never runs <c>Awake</c>): at most one sweep per
/// <see cref="HudSweepCadenceTicks"/> ticks, cached, and counted out loud in the breakdown line
/// so a regression to per-frame is one grep away. Nothing became edge-triggered — every
/// reconciler in this class stays LEVEL-triggered exactly as it was, including the ModBuild 195
/// re-arm, so there is no new edge set that could be incomplete.</para>
///
/// <para>AND THE CLAIM IS MEASURED, NOT ASSERTED. <see cref="MeasureDiscoveryBaselineOnce"/>
/// times ONE such sweep against the live scene the first tick the room stands, beside the
/// singleton read it replaced, and prints both — <c>DESTINATIONS DISCOVERY BASELINE</c>.
/// <see cref="LogSubBreakdown"/> then attributes every millisecond this class spends to a named
/// sub-step with avg, worst and run count — <c>DESTINATIONS SUB-STEP BREAKDOWN</c>, in the same
/// 30 s window and the same shape as <c>MODAL TICK BREAKDOWN</c>, on the same
/// <c>PerfMonitor</c> step machinery, so the two read side by side and the sub-steps also appear
/// by name on the per-frame <c>[Perf] SPIKE</c> line.</para>
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

    // (ModBuild 195 kept two cached FieldInfos here — `banner` and `currentMode` — read with
    //  FieldInfo.GetValue on every tick. Both are gone: the assembly is publicized at build time
    //  (GloomhavenVR.csproj:31), `CurrentMode` is a public accessor in the game's own source, and a
    //  reflected read of an enum BOXES, which is 90 allocations a second for a value that is one
    //  field load away. No MemberInfo of any kind is looked up in this class's per-tick path.)

    /// <summary>
    /// IS THIS WINDOW ONE OF THE FIVE DESTINATIONS? Matched by COMPONENT on the window's OWN
    /// GameObject — never by containment, and never by name. All five classes carry
    /// <c>[RequireComponent(typeof(UIWindow))]</c> and fetch it with <c>GetComponent</c>, so the
    /// component and the window are provably the same object. (ModBuild 179 and 181 both shipped
    /// the containment version of this test and both had to be corrected: "related to an X" is a
    /// different question from "IS an X".)
    /// </summary>
    /// <para>MEASURED SEPARATELY (ModBuild 196), because it is the one part of this class that runs
    /// OUTSIDE <see cref="Reconcile"/>: <c>ModalFallback.FirstFloatedDestination</c> calls it once
    /// per floated window per tick to produce Reconcile's own argument, and that call is billed to
    /// the parent's <c>ModalFallback.Destinations</c> phase. Without its own entry the sub-step
    /// breakdown would not add up to the parent's number and the difference would look like a
    /// mystery. Its run count is therefore normally a MULTIPLE of the tick count. It is deliberately
    /// NOT memoised yet: five <c>GetComponent</c> calls is a cost worth knowing before it is worth
    /// caching, and a stale entry for a rebuilt window is a real risk to take for a number nobody has
    /// looked at.</para>
    internal static bool IsDestination(UIWindow? window)
    {
        long begin = System.Diagnostics.Stopwatch.GetTimestamp();
        bool hit = window != null
                   && (window.GetComponent<UIShopItemWindow>() != null
                       || window.GetComponent<UITempleWindow>() != null
                       || window.GetComponent<UITrainerWindow>() != null
                       || window.GetComponent<UINewEnhancementWindow>() != null
                       || window.GetComponent<UITownRecordsWindow>() != null);
        Bill(SubIsDestination, System.Diagnostics.Stopwatch.GetTimestamp() - begin);
        return hit;
    }

    /// <summary>
    /// Level-triggered reconciler, one call per tick from <c>ModalFallback.Tick</c>. Takes the
    /// destination window that is floated right now (or null) and makes the banner agree with it.
    /// Idempotent: a steady state costs two reference compares and no writes.
    ///
    /// <para>EVERY SUB-STEP BELOW IS MEASURED (ModBuild 196 — see section 5 of the class doc). The
    /// boundaries are the same cursor pattern <c>ModalFallback.Tick</c> uses and they feed the same
    /// <c>PerfMonitor</c> step table, so <c>ModalFallback.Destinations.*</c> shows up both on the
    /// per-frame <c>[Perf] SPIKE</c> line and, ranked with its own averages, worsts and run counts,
    /// on <c>DESTINATIONS SUB-STEP BREAKDOWN</c>.</para>
    /// </summary>
    internal static void Reconcile(UIWindow? floated)
    {
        // One-shot and deliberately OUTSIDE the tick timer: it pays the old per-frame sweep exactly
        // once, so this tick is not representative of any other and must not pollute the averages.
        MeasureDiscoveryBaselineOnce();

        long tickBegin = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            EnterSub(SubHomeMode);
            TrackHomeMode();

            // ModBuild 195 — the character screen is re-armed BEFORE anything else, and the outcome
            // of the last slot click is judged, on every tick. Both are cheap (a singleton read plus
            // four slots) and both are deliberately independent of whether a destination is floated
            // right now: the mode outlives the window that set it, which is the entire defect. They
            // stay LEVEL-triggered through the ModBuild 196 perf round for exactly that reason: no
            // edge can be missed if there is no edge.
            EnterSub(SubReArm);
            ReArmCharacterScreen();

            EnterSub(SubOutcome);
            TickSheetOutcome(floated);

            EnterSub(SubBanner);
            ReconcileBanner(floated);
        }
        finally
        {
            EndSub();
            CloseTick(tickBegin);
        }
    }

    /// <summary>The banner half of <see cref="Reconcile"/> — split out only so the sub-step cursor
    /// can close cleanly around it; the body is unchanged from ModBuild 195.</summary>
    private static void ReconcileBanner(UIWindow? floated)
    {
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
        return ReturnHome($"{source} on '{window.name}'", $"closing '{window.name}'");
    }

    /// <summary>
    /// THE SAME CLOSE, ASKED FOR BY MODE INSTEAD OF BY WINDOW (ModBuild 200 — the second half of
    /// <i>"Weiterhin soll ein erneuter Druck auf den button zB vom Händler obwohl das Fenster offen
    /// ist, das offene Fenster wieder schließen."</i>).
    ///
    /// <para>WHY IT IS THE SAME ROUTE AND NOT A SECOND ONE. <see cref="LeaveMode"/> already had the
    /// only correct answer for this family — the game's mode machine has no "close", only "switch to
    /// another mode" (<c>UIGuildmasterHUD.UpdateCurrentMode</c>, :435-460, which runs
    /// <c>modes[current].Exit()</c> before entering the next), so RETURNING TO THE MAP <b>is</b> the
    /// close, and it is the only thing that takes the party display back out of selection mode. A
    /// table cap pressed a second time therefore does exactly what the window's own X does, through
    /// exactly the same dispatch. Both now call this method; there is one close path in the room and
    /// it is the flat player's.</para>
    ///
    /// <para>WHICH MODES. The caller decides — see <c>MapButtonRail.IsClosableMode</c>, which admits
    /// the five destination windows plus <c>MercenaryLog</c> (the sixth mode, which shares
    /// <c>UITownRecordsWindow</c>) and refuses <c>WorldMap</c> and <c>City</c>. Those two are not
    /// windows at all: they are the map surface this room is BUILT ON, and "closing" one of them
    /// would mean pressing the other, i.e. silently teleporting the player between the world map and
    /// the city map on a second press. This method additionally refuses to return home to the mode it
    /// was asked to close, which is the same guard stated as an invariant rather than as a caller's
    /// promise.</para>
    /// </summary>
    internal static bool CloseMode(EGuildmasterMode mode, string source)
    {
        if (!MapRoomDriver.Active)
            return false;
        EGuildmasterMode home = HomeMode();
        if (home == mode)
        {
            VRLog.Warn(Scope, $"GUILDMASTER WINDOW: asked to close mode {mode} by returning to "
                              + $"{home} — but that is the SAME mode, so the press would be a no-op. "
                              + "Refused. This can only happen if a WorldMap/City cap reached the "
                              + "toggle-close path, which MapButtonRail.IsClosableMode exists to "
                              + "prevent; nothing was dispatched.");
            return false;
        }
        return ReturnHome(source, $"closing the {mode} window from its own table cap");
    }

    /// <summary>The one close: press the bar's map button, exactly as a flat player leaves a shop.
    /// <paramref name="source"/> travels into the cap press's own log line; <paramref name="what"/>
    /// is the human sentence for this line.</summary>
    private static bool ReturnHome(string source, string what)
    {
        EGuildmasterMode home = HomeMode();
        bool pressed = MapRoomDriver.PressGuildmasterMode(home, source);
        VRLog.Info(Scope, $"GUILDMASTER WINDOW: {what} ({source}) by RETURNING TO "
                          + $"{home} on the game's own bar{(pressed ? "" : " — but no such button is on the bar")}. "
                          + "Hiding the window alone would leave the guildmaster mode active, and with it the "
                          + "party display stuck in selection mode (EnableSelectionMode sets every character "
                          + "slot non-interactable; only the mode's Exit undoes it). NOTHING NEW GOES ON THE "
                          + "WIRE: this is one pointerClick on the bar's own map Toggle, the same dispatch the "
                          + "window's X has sent since ModBuild 184, and UpdateCurrentMode -> Exit is local "
                          + "presentation — the purchase, blessing or enhancement the destination may have "
                          + "committed was committed by ITS own button, not by leaving it.");
        return pressed;
    }

    private static EGuildmasterMode HomeMode() =>
        _homeMode == EGuildmasterMode.City ? EGuildmasterMode.City : EGuildmasterMode.WorldMap;

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

    /// <summary>Remember the last non-destination mode, so an X returns where the player came from.
    ///
    /// <para>ModBuild 196: this method WAS the 12.6 ms. It ran a full
    /// <c>FindObjectOfType&lt;UIGuildmasterHUD&gt;(true)</c> and a boxing reflected field read on
    /// every tick to learn a value the game publishes as a public property on a singleton. It is now
    /// two static field loads and an enum compare, and it is still level-triggered every tick — the
    /// mode can change from the game's own bar, from a table cap or from an Escape, and there is no
    /// edge this class can see for all three.</para></summary>
    private static void TrackHomeMode()
    {
        UIGuildmasterHUD? hud = Hud();
        if (hud == null)
            return;
        EGuildmasterMode mode = hud.CurrentMode;
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
            // ModBuild 196: ask the party display for ITS OWN slots first. The list is the subject
            // of the sentence ("PARTY SLOTS"), it is a serialized field read, and it is the same set
            // ReArmCharacterScreen writes to — so the line now measures exactly what the re-arm
            // touched instead of every NewPartyCharacterUI loaded in the process. The old
            // includeInactive sweep is kept for the case where there is no display yet, and the line
            // says which of the two produced the numbers so the change is never silent.
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            IReadOnlyList<NewPartyCharacterUI>? slots =
                display != null ? display.CharacterSlots : null;
            string source = "the party display's own CharacterSlots";
            if (slots == null || slots.Count == 0)
            {
                _slotSweeps++;
                slots = Object.FindObjectsOfType<NewPartyCharacterUI>(true);
                source = "a scene sweep (no party display was reachable)";
            }
            int live = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].IsInteractable)
                    live++;
            }
            VRLog.Info(Scope, $"PARTY SLOTS after {when}: {live}/{slots.Count} interactable, from {source}. "
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

    /// <summary>The shared guildmaster banner, off the HUD's own serialized reference. Called on
    /// TRANSITIONS only (acquire and release), never in the steady state — the sweep fallback below
    /// is therefore a rare cost, and it is cached and counted all the same.</summary>
    private static Transform? Banner()
    {
        UIGuildmasterHUD? hud = Hud();
        if (hud != null && hud.banner != null)
            return hud.banner.transform;
        if (_bannerFallback != null)
            return _bannerFallback.transform;
        _bannerSweeps++;
        _bannerFallback = Object.FindObjectOfType<UIGuildmasterBanner>(true);
        return _bannerFallback != null ? _bannerFallback.transform : null;
    }

    // ------------------------------------------------------------------------- HUD discovery --

    /// <summary>Ticks between sweeps while the singleton is cold. 60 ≈ 0.7 s at rig rate: fast
    /// enough that a HUD which only becomes reachable late is picked up long before the player can
    /// press a table cap, slow enough that even a permanently cold singleton costs ~1/60th of what
    /// ModBuild 195 paid. The number is printed in the breakdown line rather than left to a reader
    /// of this file.</summary>
    private const int HudSweepCadenceTicks = 60;

    private static UIGuildmasterHUD? _hudFallback;
    private static int _hudSweepDue;
    private static int _hudSweeps;
    private static int _hudSingletonHits;
    private static UIGuildmasterBanner? _bannerFallback;
    private static int _bannerSweeps;
    private static int _slotSweeps;

    /// <summary>
    /// THE HUD, IN CONSTANT TIME. <c>UIGuildmasterHUD : Singleton&lt;UIGuildmasterHUD&gt;</c>, and
    /// that base class's <c>Instance</c> is a plain static field set in <c>Awake</c> and cleared in
    /// <c>OnDestroy</c> — so the singleton is not a cache that can go stale, it is the game's own
    /// registration. <c>MapButtonRail</c> has asked the same way since ModBuild 183.
    ///
    /// <para>THE FALLBACK, AND WHY IT IS BOUNDED RATHER THAN DELETED. A GameObject that is inactive
    /// at load never runs <c>Awake</c>, so a HUD can exist that the singleton does not know about —
    /// which is the case the old <c>includeInactive: true</c> sweep covered, and dropping it
    /// silently would be trading correctness for speed. It is kept, but at most once every
    /// <see cref="HudSweepCadenceTicks"/> ticks, and its result is cached until the object dies. The
    /// moment the singleton warms up the cache is dropped and the sweep never runs again.</para>
    /// </summary>
    private static UIGuildmasterHUD? Hud()
    {
        if (Singleton<UIGuildmasterHUD>.IsInitialized)
        {
            _hudSingletonHits++;
            _hudFallback = null;
            return Singleton<UIGuildmasterHUD>.Instance;
        }
        if (_hudFallback != null)
            return _hudFallback;
        if (--_hudSweepDue > 0)
            return null;
        _hudSweepDue = HudSweepCadenceTicks;
        _hudSweeps++;
        _hudFallback = Object.FindObjectOfType<UIGuildmasterHUD>(true);
        return _hudFallback;
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
    /// Last seen <c>classToggle.isOn</c> of the selected slot — the RISING EDGE of this is what now
    /// arms the outcome watch. See <see cref="TickSheetOutcome"/>'s ARMED BY paragraph for why the
    /// arming moved off the slot click.
    ///
    /// <para><c>null</c> means "no selected slot to read", which is deliberately NOT the same as
    /// <c>false</c>: a selection appearing with the toggle already on must not be read as an edge.
    /// </para>
    /// </summary>
    private static bool? _lastClassOn;

    /// <summary>
    /// DID THE CLICK DO ANYTHING? (ModBuild 195 — the guard.) The old
    /// <see cref="ReportPartySlots"/> line reported a PRECONDITION and reported it green in the very
    /// log in which the feature was dead, because <c>IsInteractable</c> reads the three fields the
    /// shop's <c>disableButtons: false</c> path never touches. An instrument that models a SUBSET of
    /// what the feature needs agrees with every broken build ([[instrument-measures-one-term]]), so
    /// this one measures the END STATE instead.
    ///
    /// <para><b>ARMED BY — RE-KEYED IN ModBuild 220, AND THE OLD KEY WOULD NOW BE WRONG ON EVERY
    /// CLICK.</b> Until 220 this armed on <c>NewPartyDisplayUI.SelectedUISlot</c> changing to a slot
    /// that HAS a character, because a slot click was what opened the sheet. USER REQUEST
    /// (2026-08-22, verbatim): <i>"Ich möchte das ein Klick auf den Character nur den aktuell
    /// ausgewählten Character für die Handkarten ändert nicht direct das Characterinfo-Sub-Menu
    /// öffnet, das soll wirklich nur dann passieren, wenn man auf das entsprechende Symbol (das 1.
    /// mit der abgebildeten 'Person') in der Leiste klickt."</i>
    /// <c>WorldUI/Patches/CharacterClickSelectsOnly</c> implements that by lowering
    /// <c>autoOpenDefaultPanel</c> around <c>NewPartyCharacterUI.OnClick</c>, so a slot click now
    /// takes the game's own <c>SetAllTogglesOff</c> branch and opens NOTHING — by design. An
    /// instrument still keyed on the slot click would therefore have warned <c>DID NOT OPEN</c> on
    /// every single character click and would have read as a regression report for a feature working
    /// exactly as asked.</para>
    ///
    /// <para>It is now armed by the RISING EDGE of the selected slot's <c>classToggle.isOn</c> — the
    /// person icon, which is the one gesture that is still supposed to open the sheet
    /// (<c>OnClickAssign</c> → <c>NewPartyDisplayUI.OnCharacterPickerSelected</c> →
    /// <c>CharacterSelector.Show</c>). Rising edge and not level: the toggle stays on for as long as
    /// the sheet is open, so a level test would re-arm every tick and judge the same open window
    /// dozens of times.</para>
    ///
    /// <para>It is a poll rather than the game's <c>NewCharacterSelected</c> event on purpose — this
    /// class already ticks once per frame and a subscription would have to survive the display being
    /// destroyed and rebuilt between rooms. Two cases it deliberately cannot see, stated here so the
    /// absence of a line is never read as a pass: the toggle being turned on by the game rather than
    /// by the player (an edge is an edge, and it is judged the same way), and a slot click, which no
    /// longer promises anything to measure.</para>
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
                _lastClassOn = null;
                _outcomeTicksLeft = 0;
                return;
            }

            NewPartyCharacterUI? selected = display.SelectedUISlot;
            if (!ReferenceEquals(selected, _lastSelectedSlot))
            {
                // The SELECTION changed. Since ModBuild 220 that promises nothing on its own (see
                // the ARMED BY paragraph), so it arms nothing — it only re-baselines the toggle
                // latch, and it re-baselines it to the toggle's CURRENT value so that a slot which
                // arrives with the person icon already lit is not mistaken for a fresh press.
                _lastSelectedSlot = selected;
                _lastClassOn = ReadClassOn(selected);
                _outcomeTicksLeft = 0;
                _outcomeSlotName = string.Empty;
                return;
            }

            bool? classOn = ReadClassOn(selected);
            bool rising = classOn == true && _lastClassOn == false;
            _lastClassOn = classOn;

            if (rising && selected != null && selected.Data != null)
            {
                _outcomeTicksLeft = OutcomeWatchTicks;
                _outcomeSlotName = SlotLabel(selected);
                // NEVER JUDGE ON THE ARMING TICK. The sheet may still be standing open for the
                // PREVIOUS character at this instant: a slot click runs SetAllTogglesOff →
                // OnCharacterPickerSelected(false) → TryHideCurrentDisplay, and that close can land
                // in the same frame. Reading IsOpen here would report the outgoing window as this
                // press's success — the exact shape of false pass this instrument exists to stop
                // being possible.
                return;
            }

            if (_outcomeTicksLeft <= 0)
                return;

            if (SheetWindowOpen(display))
            {
                _outcomeTicksLeft = 0;
                VRLog.Info(Scope, $"CHARACTER SHEET OUTCOME for {_outcomeSlotName}: OPENED. The party "
                                  + "assembly window (the character display) is open, so the PERSON ICON did "
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
                              + "READ IT AS: the PERSON ICON on that slot was switched on and the party "
                              + "assembly window ('Campaign Adventure Party Assembly Variant', ID "
                              + "PartyAssemblyWindow) is still closed. A plain click on the character opens "
                              + "nothing BY DESIGN since ModBuild 220 and is not watched here at all; this "
                              + "line is only ever about the icon. autoOpen=False or selectionDisabled=True "
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

    /// <summary>The selected slot's person-icon state, or <c>null</c> when there is no slot or no
    /// toggle to read. Null rather than false on purpose — see <see cref="_lastClassOn"/>: an absent
    /// reading must never combine with a later <c>true</c> into a rising edge that never happened.
    /// </summary>
    private static bool? ReadClassOn(NewPartyCharacterUI? slot)
    {
        if (slot == null)
            return null;
        UnityEngine.UI.Toggle? toggle = slot.classToggle;
        return toggle != null ? toggle.isOn : (bool?)null;
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

    // ==========================================================================================
    //  SUB-STEP ATTRIBUTION FOR THIS CLASS (ModBuild 196)
    //
    //  WHY IT EXISTS. ModBuild 195's MODAL TICK BREAKDOWN did its job perfectly and then stopped
    //  one level short: it named ModalFallback.Destinations as 99 % of the step and 12.6 ms of an
    //  11.11 ms budget, and "Destinations" is this whole class. That is four reconcilers behind
    //  one number, and a perf claim without an attribution is a guess. This is the next level,
    //  built on the SAME machinery rather than a parallel one:
    //
    //    * PerfMonitor.BeginStep/EndStep, so each sub-step is a nested named step under
    //      "ModalFallback.Destinations." — one grep, 'ModalFallback\.', still finds all of them,
    //      and the per-frame [Perf] SPIKE line names the guilty sub-step on the frame it blew.
    //      Nested steps are attributed individually but counted once in the mod total
    //      (PerfMonitor's depth counter), so the mod share cannot inflate because of this.
    //    * Its own accumulators as well, printed as ONE ranked line every SubBreakdownSeconds —
    //      the same 30 s window MODAL TICK BREAKDOWN, [Perf] FRAME and [Perf] STEPS use, so the
    //      lines read side by side with no correction for different averaging periods.
    //
    //  WHAT IT ADDS OVER THE PARENT. A RUN COUNT per sub-step. The parent's phases all run once
    //  per tick so it never needed one; here it is the whole point — it is what distinguishes
    //  "cheap" from "did not run", which is the failure mode of every edge-triggered fix, and it
    //  is what a future round that DOES move one of these onto an edge has to be judged against.
    //  It also counts the three scene sweeps left in this file by name, so a regression to
    //  per-frame discovery shows up as a four-digit sweep count instead of as a mystery.
    //
    //  COST: two Stopwatch.GetTimestamp() reads per boundary, four boundaries — well under 1 µs
    //  against 11.11 ms — plus one Info line every 30 s. Allocation-free per tick; the line's
    //  StringBuilder is static and reused. Unconditional on purpose: a diagnostic that has to be
    //  switched on is a diagnostic that is off in the log you actually receive.
    // ==========================================================================================

    private const int SubHomeMode = 0;
    private const int SubReArm = 1;
    private const int SubOutcome = 2;
    private const int SubBanner = 3;
    private const int SubIsDestination = 4;
    private const int SubCount = 5;

    /// <summary>What each sub-step covers, so a number can be acted on without reading the method.
    /// <c>HomeMode</c> the HUD lookup plus the current-mode read that feeds an X's return target;
    /// <c>ReArm</c> the ModBuild 195 character-screen re-arm over the party display's slots;
    /// <c>SheetOutcome</c> the CHARACTER SHEET OUTCOME watcher; <c>Banner</c> the banner
    /// acquire/release reconcile — including, on transitions only, the PARTY SLOTS line;
    /// <c>IsDestination</c> the five-component identity test the PARENT runs per floated window, the
    /// only entry here that is not called from <see cref="Reconcile"/>.</summary>
    private static readonly string[] SubNames =
    {
        "ModalFallback.Destinations.HomeMode",
        "ModalFallback.Destinations.ReArm",
        "ModalFallback.Destinations.SheetOutcome",
        "ModalFallback.Destinations.Banner",
        "ModalFallback.Destinations.IsDestination",
    };

    private static readonly long[] SubTicks = new long[SubCount];
    private static readonly long[] SubWorst = new long[SubCount];
    private static readonly int[] SubRuns = new int[SubCount];
    private static readonly int[] SubRank = new int[SubCount];

    /// <summary>Open sub-step, or −1. A cursor rather than a using-block for the same reason
    /// <c>ModalFallback.EnterPhase</c> is one: the body of <see cref="Reconcile"/> keeps its
    /// original shape, one added line per boundary.</summary>
    private static int _sub = -1;
    private static long _subBegin;
    private static long _subPerfBegin;

    private static long _reconcileTotal;
    private static long _reconcileWorst;
    private static int _reconcileTicks;
    private static float _nextSubBreakdown;

    private const float SubBreakdownSeconds = 30f;

    private static readonly System.Text.StringBuilder SubSb = new(1024);

    /// <summary>Close the open sub-step (if any) and open <paramref name="slot"/>.</summary>
    private static void EnterSub(int slot)
    {
        EndSub();
        _sub = slot;
        _subBegin = System.Diagnostics.Stopwatch.GetTimestamp();
        _subPerfBegin = PerfMonitor.BeginStep();
    }

    /// <summary>Close the open sub-step and fold its duration into both consumers.</summary>
    private static void EndSub()
    {
        int slot = _sub;
        if (slot < 0)
            return;
        _sub = -1;
        Bill(slot, System.Diagnostics.Stopwatch.GetTimestamp() - _subBegin);
        PerfMonitor.EndStep(SubNames[slot], _subPerfBegin);
    }

    /// <summary>Fold one run of <paramref name="slot"/> into this class's own accumulators. Split
    /// out of <see cref="EndSub"/> so <see cref="IsDestination"/> — which is called from the parent,
    /// with no cursor open — can be billed by the same rules.</summary>
    private static void Bill(int slot, long dt)
    {
        if (dt < 0L)
            dt = 0L;
        SubTicks[slot] += dt;
        SubRuns[slot]++;
        if (dt > SubWorst[slot])
            SubWorst[slot] = dt;
    }

    /// <summary>Fold this tick into the totals and emit the breakdown when the window is up.</summary>
    private static void CloseTick(long tickBegin)
    {
        long dt = System.Diagnostics.Stopwatch.GetTimestamp() - tickBegin;
        if (dt > 0L)
        {
            _reconcileTotal += dt;
            if (dt > _reconcileWorst)
                _reconcileWorst = dt;
        }
        _reconcileTicks++;

        float nowT = Time.unscaledTime;
        if (_nextSubBreakdown <= 0f)
        {
            // First tick of a session: start the clock rather than printing a one-tick window.
            _nextSubBreakdown = nowT + SubBreakdownSeconds;
        }
        else if (nowT >= _nextSubBreakdown)
        {
            _nextSubBreakdown = nowT + SubBreakdownSeconds;
            LogSubBreakdown();
            ResetSubBreakdown();
        }
    }

    private static void ResetSubBreakdown()
    {
        for (int i = 0; i < SubTicks.Length; i++)
        {
            SubTicks[i] = 0L;
            SubWorst[i] = 0L;
            SubRuns[i] = 0;
        }
        _reconcileTotal = 0L;
        _reconcileWorst = 0L;
        _reconcileTicks = 0;
        _hudSweeps = 0;
        _hudSingletonHits = 0;
        _bannerSweeps = 0;
        _slotSweeps = 0;
    }

    /// <summary>
    /// ONE LINE THAT ANSWERS "WHICH PART OF Destinations COSTS THE MILLISECONDS" — every sub-step,
    /// ranked, with its average per TICK, its average per RUN, its share, its worst single run and
    /// how many of the window's ticks it ran on; then the discovery counters.
    /// </summary>
    private static void LogSubBreakdown()
    {
        try
        {
            int ticks = _reconcileTicks;
            if (ticks <= 0)
                return;
            double freq = System.Diagnostics.Stopwatch.Frequency;
            double totalMs = _reconcileTotal * 1000d / freq / ticks;

            int n = SubNames.Length;
            for (int i = 0; i < n; i++)
                SubRank[i] = i;
            for (int i = 1; i < n; i++)
            {
                int key = SubRank[i];
                int j = i - 1;
                while (j >= 0 && SubTicks[SubRank[j]] < SubTicks[key])
                {
                    SubRank[j + 1] = SubRank[j];
                    j--;
                }
                SubRank[j + 1] = key;
            }

            System.Text.StringBuilder sb = SubSb;
            sb.Length = 0;
            sb.Append("DESTINATIONS SUB-STEP BREAKDOWN over ").Append(ticks)
              .Append(" tick(s): GuildmasterDestinations.Reconcile cost ")
              .Append(totalMs.ToString("F4"))
              .Append("ms/tick avg, worst ")
              .Append((_reconcileWorst * 1000d / freq).ToString("F3"))
              .Append("ms. Ranked by total time:");
            for (int r = 0; r < n; r++)
            {
                int slot = SubRank[r];
                int runs = SubRuns[slot];
                double avgMs = SubTicks[slot] * 1000d / freq / ticks;
                double perRunMs = runs > 0 ? SubTicks[slot] * 1000d / freq / runs : 0d;
                double share = totalMs > 1e-9d ? avgMs / totalMs * 100d : 0d;
                sb.Append(r == 0 ? " " : " | ")
                  .Append(SubNames[slot]).Append(' ')
                  .Append(avgMs.ToString("F4")).Append("ms/tick (")
                  .Append(share.ToString("F0")).Append("%), ran ").Append(runs).Append('/')
                  .Append(ticks).Append(" tick(s) at ").Append(perRunMs.ToString("F4"))
                  .Append("ms/run, worst run ")
                  .Append((SubWorst[slot] * 1000d / freq).ToString("F3")).Append("ms");
            }
            sb.Append(" | discovery: the HUD answered from Singleton<UIGuildmasterHUD>.Instance ")
              .Append(_hudSingletonHits).Append(" time(s) and from a scene sweep ")
              .Append(_hudSweeps).Append(" time(s) (bounded to one per ")
              .Append(HudSweepCadenceTicks)
              .Append(" ticks while the singleton is cold); banner sweeps ").Append(_bannerSweeps)
              .Append(", party-slot sweeps ").Append(_slotSweeps)
              .Append(". HOW TO READ THIS LINE. It is the next level down from MODAL TICK "
                      + "BREAKDOWN's 'ModalFallback.Destinations' entry and uses the same 30 s "
                      + "window, so the two are directly comparable; the same sub-step names also "
                      + "appear on the per-frame [Perf] SPIKE line. In ModBuild 195 this whole "
                      + "class cost 12.619ms/tick avg (worst 39.64ms) — 99% of ModalFallback and "
                      + "more than the entire 11.11ms frame budget, on 100% of frames — and "
                      + "ALL of it was HomeMode running "
                      + "FindObjectOfType<UIGuildmasterHUD>(true) once per tick. So: if HomeMode "
                      + "is not now the CHEAPEST entry here, the singleton has gone cold and the "
                      + "sweep count above is non-zero and climbing — that is the regression to "
                      + "look for, and it is a fact on this line rather than an inference. 'ran "
                      + "n/m tick(s)' is what separates 'cheap' from 'never ran': every sub-step "
                      + "in this class is LEVEL-triggered by design, so anything below m here "
                      + "means a reconciler is being skipped, and for ReArm that means the "
                      + "ModBuild 195 character-sheet fix is not running. Banner's worst run is "
                      + "expected to be far above its average — it does real work only when a "
                      + "destination window opens or closes, and that transition also emits the "
                      + "PARTY SLOTS line. IsDestination is the ONE entry that is not called from "
                      + "Reconcile — the parent runs it once per floated window per tick to build "
                      + "Reconcile's argument — so its run count is normally a multiple of the "
                      + "tick count, its share above 100% is arithmetic rather than a fault, and "
                      + "it is the whole of the gap between the ms/tick total on this line and "
                      + "MODAL TICK BREAKDOWN's ModalFallback.Destinations entry. DESTINATIONS "
                      + "DISCOVERY BASELINE, printed once per map room, is where the cost of the "
                      + "call this round replaced is measured directly.");
            VRLog.Info(Scope, sb.ToString());
        }
        catch (System.Exception ex)
        {
            // House rule: an instrument may never be the thing that starves VR input.
            VRLog.Warn(Scope, $"DESTINATIONS SUB-STEP BREAKDOWN could not be composed "
                              + $"({ex.GetType().Name}: {ex.Message}) — the per-sub-step numbers are "
                              + "still on the [Perf] STEPS and [Perf] SPIKE lines under their "
                              + "'ModalFallback.Destinations.' names; only this summary is missing.");
        }
    }

    // ------------------------------------------------------------------- the discovery baseline --

    /// <summary>How many singleton reads the baseline averages over. Enough that the timer's own
    /// resolution cannot dominate, few enough that the whole probe is invisible.</summary>
    private const int BaselineSingletonReads = 1000;

    private static bool _baselineDone;

    /// <summary>
    /// MEASURE THE CALL THAT WAS REMOVED, ONCE, AGAINST THE REAL SCENE. A fix whose evidence is
    /// "FindObjectOfType is known to be slow" is a fix nobody can check. This runs the exact call
    /// ModBuild 195 made on every tick — <c>Object.FindObjectOfType&lt;UIGuildmasterHUD&gt;(true)</c>,
    /// includeInactive and all — exactly ONCE, the first tick the map room stands, beside the
    /// singleton read that replaced it, and prints both numbers and their ratio.
    ///
    /// <para>It costs one frame per map room, deliberately, and it is taken OUTSIDE
    /// <see cref="Reconcile"/>'s own timer so it cannot pollute a single average. It re-arms on
    /// <see cref="Reset"/>, so a second visit to the room measures the scene as it is then — which
    /// matters, because the ModBuild 195 log shows this cost RISING from 2.6 ms to 13.2 ms across
    /// one session as the room filled up.</para>
    /// </summary>
    private static void MeasureDiscoveryBaselineOnce()
    {
        if (_baselineDone || !MapRoomDriver.Active)
            return;
        _baselineDone = true;
        try
        {
            double freq = System.Diagnostics.Stopwatch.Frequency;

            long a = System.Diagnostics.Stopwatch.GetTimestamp();
            UIGuildmasterHUD? swept = Object.FindObjectOfType<UIGuildmasterHUD>(true);
            double sweepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - a) * 1000d / freq;

            // The replacement, averaged. `seen` is used in the line below so the loop cannot be
            // optimised away into a measurement of nothing.
            UIGuildmasterHUD? seen = null;
            long b = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < BaselineSingletonReads; i++)
            {
                if (Singleton<UIGuildmasterHUD>.IsInitialized)
                    seen = Singleton<UIGuildmasterHUD>.Instance;
            }
            double readUs = (System.Diagnostics.Stopwatch.GetTimestamp() - b) * 1e6d / freq
                            / BaselineSingletonReads;

            bool agree = ReferenceEquals(swept, seen);
            VRLog.Info(Scope, $"DESTINATIONS DISCOVERY BASELINE: one "
                              + $"Object.FindObjectOfType<UIGuildmasterHUD>(true) over this scene took "
                              + $"{sweepMs:F3}ms and found {(swept != null ? "'" + swept.name + "'" : "nothing")}. "
                              + $"Singleton<UIGuildmasterHUD>.Instance answered the SAME question in "
                              + $"{readUs:F4}µs ({(readUs > 1e-6d ? (sweepMs * 1000d / readUs).ToString("F0") : "∞")}× "
                              + $"cheaper) and returned {(agree ? "the same object" : "a DIFFERENT object — read the warning below")}"
                              + ". READ IT AS THE EVIDENCE FOR ModBuild 196: up to and including "
                              + "ModBuild 195, GuildmasterDestinations.Reconcile ran that sweep once "
                              + "per tick from TrackHomeMode, unconditionally and before any map-room "
                              + "gate, which is why MODAL TICK BREAKDOWN reported "
                              + "'ModalFallback.Destinations 12.619ms (99%), worst 39.64ms' with "
                              + "over-budget 1497/1497 (100.0%) — a steady floor above the entire "
                              + "11.11ms frame budget, not a spike. Multiply the first number above by "
                              + "the frame rate to see what the room was paying. This probe runs ONCE "
                              + "per map room and is measured outside the per-tick timer, so it "
                              + "appears as a single wide frame in ModalFallback.Destinations' WORST "
                              + "and in no average anywhere. From here on, see DESTINATIONS SUB-STEP "
                              + "BREAKDOWN every 30s.");
            if (!agree)
                VRLog.Warn(Scope, "DESTINATIONS DISCOVERY BASELINE DISAGREES: the scene sweep and the "
                                  + "singleton named different UIGuildmasterHUD objects. The singleton is "
                                  + "written in Awake and cleared in OnDestroy, so this can only mean a "
                                  + "second HUD exists (one of them inactive, hence never awoken). "
                                  + "TrackHomeMode and the banner now follow the SINGLETON, i.e. the one "
                                  + "the game itself considers current — which is also the one "
                                  + "MapButtonRail drives. If the X on a destination window starts "
                                  + "returning to the wrong mode, or a window opens without its "
                                  + "background, this line is where to start.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"DESTINATIONS DISCOVERY BASELINE threw ({ex.GetType().Name}: "
                              + $"{ex.Message}) — the sub-step breakdown is unaffected, only the "
                              + "before/after comparison for the ModBuild 196 fix is missing.");
        }
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
        _hudFallback = null;
        _bannerFallback = null;
        _hudSweepDue = 0;
        _sub = -1;
        _nextSubBreakdown = 0f;
        _baselineDone = false;
        ResetSubBreakdown();
    }
}
