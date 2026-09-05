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

            // ModBuild 231 — did the window the last press asked for actually arrive? Two reference
            // compares while nothing is armed, which is every tick except the ~120 frames after an
            // open. See the block above TickOpenWatch for why this exists at all.
            EnterSub(SubOpenWatch);
            TickOpenWatch();
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

    // =========================================================================================
    //  6 — THE RAIL'S ORDER IS A PROPERTY OF THE DATA, NOT OF A SCAN (ModBuild 226)
    //
    //  USER REPORT, verbatim: "Die Button-Reihenfolge am Tisch vor der Map ist nicht bei jedem
    //  Spieler die gleiche - das soll nicht sein - jeder soll die gleiche Reihenfolge sehen."
    //
    //  WHAT THE ORDER WAS DERIVED FROM UNTIL NOW, and why two clients could disagree.
    //  MapButtonRail.Rescan builds one cap per UIGuildmasterButton in the order the SCAN handed
    //  them over, and lays them out at x0 + pitch*i in that same order. There are two scans and
    //  NEITHER of them is a statement the game's data makes about how the bar should read:
    //
    //    (a) Singleton<UIGuildmasterHUD>.Instance.GetComponentsInChildren<UIGuildmasterButton>(true)
    //        — depth-first TRANSFORM HIERARCHY order of whichever HUD instance won the singleton
    //        race. The .planning/debug/Player.log of the reporting player proves that "whichever"
    //        is a real question and not a pedantic one: DESTINATIONS DISCOVERY BASELINE at line
    //        3087 says the sweep and the singleton "returned a DIFFERENT object", i.e. there are
    //        TWO UIGuildmasterHUD objects in that scene and Singleton.Instance is simply the one
    //        whose Awake ran last. The second player's log (remote/Player.log:17656) reports the
    //        two AGREEING. So the two clients did not even resolve the same object graph.
    //
    //    (b) Object.FindObjectsOfType<UIGuildmasterButton>(true) — the fallback the rail takes
    //        whenever the singleton is cold, which is not hypothetical either: the same breakdown
    //        line reports "the HUD answered from Singleton<UIGuildmasterHUD>.Instance 0 time(s)
    //        and from a scene sweep 39 time(s)" for the reporting player's first 30 s window, and
    //        FOUR consecutive windows of that for the second player. FindObjectsOfType's order is
    //        an engine registry walk: Unity documents it as undefined, and it is a function of
    //        load order and instance ids, i.e. of the PROCESS, not of the campaign.
    //
    //  Either way the answer is "same set, different order", which is exactly what he described,
    //  and MapButtonRail.SameSet compares MEMBERSHIP only — so whatever order the first successful
    //  scan produced is frozen for the whole session and never re-examined.
    //
    //  THE FIX IS THIS TABLE. One ordered list that every client reads, so the rail's geometry
    //  stops being a function of anything the process happens to do. It also fixes the SUBSET
    //  case for free: if two players ever end up with different buttons (RefreshUnlocked does
    //  SetActive(IsUnlocked) per mode, and Hide() drops a button out of the flat bar's layout),
    //  the ones they share still keep the same relative places, because a rank is a property of
    //  the MODE and not of how many neighbours it has.
    //
    //  WHY THIS ORDER. It is UIGuildmasterHUD's own serialized declaration order for the option
    //  bar — enhanceButton, shopButton, trainerButton, mapButton, templeButton, cityButton,
    //  townRecordsButton, mercenaryLogButton (decompiled GH.Runtime/UIGuildmasterHUD.cs:52-73),
    //  the same list MapButtonRail's class doc has cited since ModBuild 183 as "the option bar:
    //  enhance, shop, trainer, map, temple, city, town records, mercenary log".
    //
    //  REJECTED: the EGuildmasterMode enum's own numeric order (None, WorldMap, Trainer,
    //  Enchantress, Merchant, Temple, City, TownRecords, MercenaryLog). It is stable across
    //  processes too, but it is an internal numbering that also carries None, QuestAccept,
    //  CityEncounter and MultiplayerQuest — none of which is a bar button — and it puts the map
    //  first and the trainer second, which is not how the flat bar reads. A rank table that has
    //  to be EDITED to change the rail is the point; deriving it from an enum would hide the
    //  decision inside the game's numbering again.
    // =========================================================================================

    /// <summary>
    /// THE ONE ORDERED TABLE. Index = rank; position 0 stands at the far end of the rail from
    /// <c>MapButtonRail</c>'s local +X. Edit THIS to change the rail's order for everybody — no
    /// other file has an opinion about it, and no scan does either.
    /// </summary>
    private static readonly EGuildmasterMode[] DeclaredOrder =
    {
        EGuildmasterMode.Enchantress,   // enhanceButton
        EGuildmasterMode.Merchant,      // shopButton
        EGuildmasterMode.Trainer,       // trainerButton
        EGuildmasterMode.WorldMap,      // mapButton
        EGuildmasterMode.Temple,        // templeButton
        EGuildmasterMode.City,          // cityButton
        EGuildmasterMode.TownRecords,   // townRecordsButton
        EGuildmasterMode.MercenaryLog,  // mercenaryLogButton
    };

    /// <summary>
    /// This mode's declared place in the rail, low first. A mode the table does not name — a DLC
    /// or a version that adds a bar button, or one of the enum members that is not a bar button at
    /// all — sorts AFTER every named one, in enum order, so it is appended deterministically
    /// rather than dropped or inserted somewhere a second client might not agree with.
    /// <see cref="IsRanked"/> is what tells a caller which of the two happened.
    /// </summary>
    internal static int Rank(EGuildmasterMode mode)
    {
        for (int i = 0; i < DeclaredOrder.Length; i++)
        {
            if (DeclaredOrder[i] == mode)
                return i;
        }
        return DeclaredOrder.Length + (int)mode;
    }

    /// <summary>How many rows the table rail stands in. Row 0 is the far one, nearest the map.</summary>
    internal const int RailRowCount = 2;

    /// <summary>
    /// WHICH ROW OF THE TABLE RAIL THIS MODE'S CAP STANDS IN — user request, 2026-09-05: <i>the
    /// "Gloomhaven" and "Worldmap" buttons should sit in a SECOND ROW below the others, for visual
    /// separation.</i>
    ///
    /// <para>DECLARED, LIKE THE ORDER, AND FOR THE SAME REASON. It is a partition of the same table
    /// and it lives beside it, so "which buttons stand where" is answered by one file that every
    /// client compiles in. Nothing scans, nothing infers it from a name or from how many neighbours
    /// a cap has, and two players' rails are identical by construction.</para>
    ///
    /// <para>THE PARTITION IS THE ONE THE ROOM ALREADY HAS. Row 1 is exactly
    /// <see cref="IsMapSurfaceMode"/>: <c>WorldMap</c> and <c>City</c>, the two modes that are not
    /// windows but SURFACES this whole room is built on — the same two the press path already treats
    /// apart (they cannot be "closed", only switched between). So the visual separation he asked for
    /// is a separation that already existed in the behaviour; it is only now visible on the table.
    /// Row 0 keeps everything else, including any mode a later game version adds.</para>
    ///
    /// <para>Row 1 stands NEARER THE PLAYER, which is what "below" means on a table of caps lying
    /// flat — see <c>MapButtonRail.Build</c>, where the sign is worked out from the rail's own frame
    /// and printed on the order line.</para>
    /// </summary>
    internal static int RailRow(EGuildmasterMode mode) => IsMapSurfaceMode(mode) ? 1 : 0;

    /// <summary>Is this mode named by <see cref="DeclaredOrder"/>? False means it was appended by
    /// the fallback rule in <see cref="Rank"/> and the rail says so on its order line.</summary>
    internal static bool IsRanked(EGuildmasterMode mode)
    {
        for (int i = 0; i < DeclaredOrder.Length; i++)
        {
            if (DeclaredOrder[i] == mode)
                return true;
        }
        return false;
    }

    // =========================================================================================
    //  7 — "IS THAT WINDOW ALREADY STANDING?" IS A QUESTION FOR THE WINDOW (ModBuild 226)
    //
    //  USER REPORT, verbatim: "Ein erneuter Druck auf einen Button z.B. Händler obwohl das Fenster
    //  schon da ist soll das jeweilige Fenster wieder schließen." — reported AGAIN after ModBuild
    //  222 shipped a close path for it.
    //
    //  THE 222 PATH EXISTS AND IT FIRES. This is not a "the fix never ran" round: the reporting
    //  player's log carries five MAP TABLE BUTTON '…' pressed (…) while its window is OPEN lines
    //  (Player.log:64665, 64917, 65840 for Merchant and 163131, 163275 for MercenaryLog), each
    //  followed within ten lines by the WorldMap press and by UIWindow hidden: 'UI Shop Item
    //  Window'. The second player's log carries twelve more. The close works.
    //
    //  WHAT IT ASKS IS THE WRONG QUESTION. <see cref="CloseMode"/> was reached only from
    //  MapButtonRail.Press's `toggle.isOn` test, and `toggle.isOn` is the game's CURRENT MODE, not
    //  "is that window standing in this room". In the flat game those are the same sentence,
    //  because the mode machine owns the screen. In the map room they are not, by explicit user
    //  ruling: ModalFallback.MapRoomParallel keeps a destination window floated after the game's
    //  single-window toggle has already hidden it ("Anders als in Flat soll es hier möglich sein
    //  mehrere Fenster parallel offen zu haben"). That state is in the log by name —
    //  Player.log:66110 "MODAL CLOSE (X button): 'UI Shop Item Window' (ID Shop) — game had
    //  already hidden it (single-window toggle); releasing the parallel VR float only" — and the
    //  line above it shows what the mode-keyed close does there: "MAP TABLE BUTTON 'City' pressed
    //  (X button on 'UI Shop Item Window') while it is ALREADY the current mode … nothing was
    //  dispatched." The mode had moved on; the merchant was still on the table; a press on the
    //  Händler cap in that state took the OPEN branch and re-entered the mode instead of closing
    //  the window he was looking at. That is his sentence, word for word: "obwohl das Fenster
    //  SCHON DA IST".
    //
    //  SO THE CAP ASKS THE WINDOW. <see cref="IsLeftoverWindowStanding"/> resolves the mode's own
    //  UIWindow off UIGuildmasterHUD's serialized references and asks IT — UIWindow.IsOpen, or the
    //  mod's own float set via ModalFallback.FloatedWindowWithId. Nothing is remembered, so a
    //  window the player closed with its own X reads as gone in the very next frame, which a
    //  remembered "I opened this" flag could not.
    //
    //  AND IT IS CLOSED THE WAY THE GAME CLOSES IT. Not SetActive(false) behind the game's back,
    //  and not UIWindow.Hide() either (ModBuild 184 proved that leaves the mode active and the
    //  party display dead). The leftover case goes through ModalFallback.CloseFloatedWindow, i.e.
    //  the window's OWN X button — the one path that already handles both halves: an open game
    //  window gets Escape()/Hide(), and a window the game has already hidden gets its parallel VR
    //  float released. The mode-is-current case is untouched and still returns to the map.
    //
    //  (ModBuild 230 CORRECTED THAT LAST SENTENCE. "The mode-is-current case is untouched" is
    //  precisely what made the close take two presses — see section 8, immediately below.)
    // =========================================================================================

    // =========================================================================================
    //  8 — THE TWO BRANCHES WERE TWO PRESSES (ModBuild 230)
    //
    //  USER REPORT, verbatim: "Die Fenster die man durch Drücken der Tasten in der Map-Umgebung
    //  öffnen kann, zB der Händler gehen erst durch zwei-maliges erneutes Drücken auf den Tasten
    //  wieder zu. Die Tasten soll Tiggles sein, also einmal drücken öffnet das Fenster sofern es
    //  geschlossen ist, nochmal drücken schließt das Fenster sofern es noch offen sein sollte."
    //
    //  WHAT THE TWO PRESSES ACTUALLY WERE, from his ModBuild 229 log, and it is not an inference —
    //  the two branches ModBuild 226 wrote into CloseMode each printed their own line, and they are
    //  TWENTY LOG LINES APART with a whole frame of panel maintenance between them:
    //
    //    Player.log:27898  'UI Shop Item Window' floated WITH its background       <- press 1, OPEN
    //    Player.log:27922  MAP TABLE BUTTON 'Merchant' pressed … its window is OPEN <- press 2
    //    Player.log:27933  GUILDMASTER WINDOW: … by RETURNING TO WorldMap           <- branch (B)
    //      …and immediately after it, 27934-27941: MIP BAKE ARRIVAL on 'UI Shop Item Window',
    //      PANEL SUPERSAMPLE re-allocated 'UI Shop Item Window' to 3968x2288, HIT RECT … commit #2,
    //      Ray-uGUI canvas 'GloomhavenVR.Panel_Modal_UI Shop Item Window' world rect 237x133 — i.e.
    //      the mod was still building, supersampling and RAY-TESTING that panel. It stood.
    //    Player.log:27942  MAP TABLE BUTTON 'Merchant' pressed … its window is OPEN <- press 3
    //    Player.log:27943  GUILDMASTER WINDOW: … through the WINDOW'S OWN X PATH    <- branch (A)
    //    Player.log:27946  MODAL CLOSE (X button): … releasing the parallel VR float only
    //    Player.log:27949  MODAL WINDOW: 'UI Shop Item Window' released
    //
    //  The same shape repeats four times in that one session (27762/27769 and 27836/27852 on the
    //  MercenaryLog cap, 27933/27943 and 28009/28023 on the Merchant cap) and not once does a single
    //  press produce both lines — CloseMode returns after whichever branch it took.
    //
    //  WHY BRANCH (B) COULD NOT CLOSE ANYTHING HE COULD SEE. ReturnHome presses the bar's map
    //  button, UpdateCurrentMode runs the destination's Exit, and the game hides its window. In the
    //  flat game that IS the close, because the game owns the screen. In this room it is not, by the
    //  mod's own explicit ruling: every window floated while the room stands is STICKY
    //  (ModalFallback.MapRoomParallel, ModBuild 180 — "Anders als in Flat soll es hier möglich sein
    //  mehrere Fenster parallel offen zu haben"), and the release loop reads
    //
    //      stillOpen = alive && !wp.UserClosing && (ContainsWindow(OpenWindows, wp.Window) ||
    //                                               wp.Sticky || ScriptedLevelMessageActive(...))
    //      (ModalFallback.4.Tick.cs:2218-2220)
    //
    //  — a sticky float does NOT release when the game hides its window; only WindowPanel.UserClosing
    //  releases it, and the only writer of that flag is ModalFallback.CloseFloatedWindow
    //  (ModalFallback.7.Close.cs:113-115). Worse, step 4b of the same tick walks the converted set
    //  and re-asserts alpha 1 / raycasts on for exactly this state ("if (!wp.Window.IsOpen ||
    //  !wp.Window.IsVisible) ReassertStickyVisible(wp)", ModalFallback.4.Tick.cs:2306-2313). So
    //  press 2 exited the mode and the room immediately re-showed the panel. The merchant stayed on
    //  the table, unchanged, and the only thing that had happened was invisible.
    //
    //  THE PREDICATE WAS THE OTHER HALF. IsLeftoverWindowStanding refused to answer at all while the
    //  mode machine still had a window mode current (its clause 2, "if (IsWindowMode(current)) return
    //  false"), so on press 2 — Merchant current, merchant panel standing — the cap could only take
    //  the mode-keyed branch. The panel-keyed branch became reachable only AFTER press 2 had moved
    //  the mode machine home. Two questions, asked in two presses, about one window.
    //
    //  THE FIX IS ONE QUESTION AND ONE ACTION. <see cref="Decide"/> asks what is OBSERVABLY standing
    //  — the live VR float first, the game's own IsOpen second, the mode enum only third and only as
    //  a tiebreaker — and every Close goes through ModalFallback.CloseFloatedWindow, which is the ONE
    //  routine that already performs BOTH halves in the right order and has since ModBuild 184:
    //
    //      1. wp.UserClosing = true                      -> the parallel float releases next tick
    //      2. GuildmasterDestinations.LeaveMode(window)   -> the game-side mode Exit, but ONLY when
    //                                                       this window IS the current mode's (the
    //                                                       ModBuild 226 guard, kept verbatim)
    //      3. window.IsOpen ? Escape()/Hide() : reset the forced CanvasGroup back to hidden
    //
    //  There is no ordering left to get wrong and no orphan either way: a mode that is active is
    //  exited (so the party display comes back out of selection mode — section 2, still the reason
    //  Hide() alone is never enough), and a float that stands is released, whether one or both of
    //  those is true. It is idempotent by construction: after it runs nothing is standing, so the
    //  next press falls to Open.
    //
    //  THE ONE CARVE-OUT, AND IT IS NOT A THIRD STATE. TownRecords and MercenaryLog are two MODES
    //  over ONE UIWindow (UIGuildmasterHUD.cs:248-254; see ModeWindow). While one of them is current
    //  and that shared window stands, a press on the OTHER cap is a TAB SWITCH — "show me the
    //  mercenary log" — and it stays one, because there is no second panel to close and the press
    //  still changes what he sees. Pressing the cap whose tab is already showing closes it, exactly
    //  like every other destination. So each cap is still a strict toggle of its own content and no
    //  press is ever a no-op. This is the ONLY case in which a standing window does not close, it is
    //  decided by window IDENTITY (ReferenceEquals on ModeWindow) rather than by name or by mode, and
    //  Decide names it on the press line so the next log shows it happening.
    //
    //  MULTIPLAYER: unchanged in both directions and by construction. Every fact read here —
    //  UIWindow.IsOpen, UIGuildmasterHUD.CurrentMode, the mod's own converted-window list — is local
    //  presentation the game already maintains per client, nothing is broadcast, nothing is read from
    //  a host, and the close is still the same single pointerClick on the local bar plus a local
    //  float release. A second player pressing his own table cap runs this identical code against his
    //  own HUD singleton and his own float set.
    //
    //  COST: zero per-frame work is added. Decide() and Observe() run ONLY inside
    //  MapButtonRail.Press — that is, on a trigger pull — and Reconcile's per-tick path is untouched,
    //  so the ModBuild 196 budget work and its DESTINATIONS SUB-STEP BREAKDOWN line are unaffected.
    //  Pressable(), which DOES run per cap per frame, deliberately still consults none of this (see
    //  its own doc for why that was already the right call in ModBuild 226).
    // =========================================================================================

    // =========================================================================================
    //  9 — TWO REPORTS, TWO CAUSES, AND NEITHER WAS THE ONE THE ROUND WAS OPENED ON (ModBuild 231)
    //
    //  REPORT A, verbatim: "Wird der Händler gedrückt, reagiert der Button der World-Map, er wird
    //  ausgegraut und wieder nicht. Da sollte es gar keine Wechselwirkung mehr geben. Prüfe auch
    //  andere Wechselwirkungen."
    //
    //  It is TWO separate visible events on that cap, with two separate causes, and only one of
    //  them involves a dispatch:
    //    (1) THE CAP TRAVELS. Closing a destination runs CloseFloatedWindow -> LeaveMode ->
    //        ReturnHome -> MapRoomDriver.PressGuildmasterMode -> MapButtonRail.PressMode ->
    //        Press(), which set PressedUntil and pushed the World-Map cap 0.007 m into the table
    //        with nobody near it. The ModBuild 230 log has that dispatch on its own line every
    //        time (Player.log:7872, 9515, 9641, 9759, 10461 …: "MAP TABLE BUTTON 'WorldMap'
    //        pressed (X button on 'UI Shop Item Window')"). FIXED in MapButtonRail.Press: the
    //        travel is gated on a PHYSICAL press; the dispatch is untouched, because it is the only
    //        thing that runs the closing mode's Exit and without Exit the party display stays in
    //        selection mode (ModBuild 184/195).
    //    (2) THE CAP GREYS, and this one needs no dispatch at all — which is why reading the
    //        dispatch as the cause would have fixed half the report. The game's bar is a uGUI
    //        ToggleGroup and each button greys ITSELF when it becomes the selected one
    //        (UIGuildmasterButton.RefreshSelected, decompiled :206-219: `toggle.interactable =
    //        !toggle.isOn`, plus `icon.material = disabledGrayscaleMaterial`). Opening the merchant
    //        DEselects the map button, its interactable flips true, and MapButtonRail.Pressable's
    //        clause 2 lit the cap; closing it selects the map again and clause 3 refuses, because
    //        IsClosableMode(WorldMap) is false by design. Grey, lit, grey, once per merchant press.
    //        FIXED in MapButtonRail.Pressable: a map-SURFACE cap answers to HomeSurface — which
    //        surface this room is standing on — and that is sticky across every destination mode.
    //
    //  REPORT B, verbatim: "Wenn ich die buttons wiederholt hintereinander drücke tauchen die
    //  Fenster irgendwann gar nicht mehr auf. Das habe ich nun mit dem Händler und der Liste der in
    //  Ruhestand gegangenen Charactere hinbekommen. Das darf niemals passieren."
    //
    //  THE LOG NAMES IT TWICE AND NAMES HIS TWO WINDOWS:
    //    Player.log:10230  CATCH-ALL FUSE: window 'UI Shop Item Window'   re-floated 4× in 60s …
    //                      suppressed for this session
    //    Player.log:11217  CATCH-ALL FUSE: window 'UI Town Records Window' re-floated 4× in 60s …
    //  Every destination reaches VR through the catch-all, so every open is one tick of that
    //  count, and four open/close cycles inside a minute blow it FOREVER. After 10230 every
    //  merchant press reads "IsOpen=True float=none": the game's window opens and nothing is shown.
    //  FIXED in ModalFallback.LiftChurnFuseForDestinations (see its header for why the count is not
    //  simply disabled, and for the lift's own fuse against a genuine re-float loop).
    //
    //  THE RECONSTRUCTED SEQUENCE, from the 84 press lines, is one shape repeated:
    //    open  Merchant (7894) → …61 s… → open (9449) → close (9518) → open (9579) → close (9644)
    //    → open (9679) → close (9762) → open (10220) ⇒ the 4th append inside the 60 s window ⇒
    //    FUSE (10230). Every later merchant press: IsOpen=True, float=none, no slot claim.
    //    The records window repeats it exactly: opens at 11017 / 11082 / 11143 / 11207 ⇒ FUSE
    //    (11217), and from 11228 on it never appears again either.
    //
    //  TWO PREMISES THIS ROUND WAS OPENED ON WERE FALSE, and saying so is cheaper than a build:
    //    * "the arc-slot registry leaks 2:1 — 42 claims, 21 releases". It does not. Of the 41
    //      "claimed reservation" lines, 20 are the SECOND, "replayed against the final fit" line the
    //      same claim prints once its content fit is known; the 42nd match of that grep is the
    //      once-per-session MAP ROOM WINDOW SLOTS geometry header. 21 first-claims, 21 releases,
    //      exactly balanced — and at the moment the merchant first failed to appear the registry
    //      held TWO of eight reservations. TryClaimArcSlot never returned −1 in that session.
    //    * "the WorldMap cap animates because the close's dispatch arrives at it as a press". Half
    //      right — see (1) and (2) above.
    //
    //  MULTIPLAYER: nothing here goes on the wire and nothing new is read from one. The fuse lift
    //  is local presentation state in this client's own ModalFallback; HomeSurface is read from
    //  this client's own UIGuildmasterHUD; the travel gate is a local animation. A second player
    //  pressing his own cap runs the identical code against his own singletons, and the remote
    //  surface mirror (RemoteMapRoom's record-20 edge) reaches PressMode, so his switch dispatches
    //  here exactly as before and now also stops moving a cap this player is not touching.
    // =========================================================================================

    /// <summary>
    /// Is this mode one of the SIX that are windows, as opposed to a map surface? THE ONE TABLE —
    /// <c>MapButtonRail.IsClosableMode</c> delegates here so the rail's press path and this class's
    /// close path can never disagree about which modes have a window at all. See that method's doc
    /// for the reasoning about WorldMap/City, which has not changed.
    /// </summary>
    internal static bool IsWindowMode(EGuildmasterMode mode) => mode switch
    {
        EGuildmasterMode.Merchant => true,
        EGuildmasterMode.Temple => true,
        EGuildmasterMode.Trainer => true,
        EGuildmasterMode.Enchantress => true,
        EGuildmasterMode.TownRecords => true,
        EGuildmasterMode.MercenaryLog => true,
        _ => false,
    };

    /// <summary>
    /// The <c>UIWindow</c> a guildmaster mode owns, off <c>UIGuildmasterHUD</c>'s OWN serialized
    /// references (decompiled UIGuildmasterHUD.cs:73-86) — never by name and never by a scene
    /// sweep. All five destination classes carry <c>[RequireComponent(typeof(UIWindow))]</c>, so
    /// the window is provably the same GameObject as the component, exactly as
    /// <see cref="IsDestination"/> relies on.
    ///
    /// <para><c>MercenaryLog</c> deliberately resolves to the SAME window as <c>TownRecords</c>:
    /// it is a sixth MODE sharing <c>UITownRecordsWindow</c> (UIGuildmasterHUD.cs:248-254) and has
    /// no window class of its own. Both caps therefore ask about, and close, the one window that
    /// is actually standing — which is the honest answer, because there is only one.</para>
    /// </summary>
    internal static UIWindow? ModeWindow(EGuildmasterMode mode)
    {
        UIGuildmasterHUD? hud = Hud();
        if (hud == null)
            return null;
        Component? owner = null;
        switch (mode)
        {
            case EGuildmasterMode.Merchant: owner = hud.shopWindow; break;
            case EGuildmasterMode.Temple: owner = hud.templeWindow; break;
            case EGuildmasterMode.Trainer: owner = hud.trainerWindow; break;
            case EGuildmasterMode.Enchantress: owner = hud.enhancementWindow; break;
            case EGuildmasterMode.TownRecords:
            case EGuildmasterMode.MercenaryLog: owner = hud.townRecordsWindow; break;
        }
        return owner != null ? owner.GetComponent<UIWindow>() : null;
    }

    /// <summary>The guildmaster mode the game says is current, or <c>None</c> when no HUD is
    /// reachable. One static field load through <see cref="Hud"/>'s singleton path.</summary>
    private static EGuildmasterMode CurrentMode()
    {
        UIGuildmasterHUD? hud = Hud();
        return hud != null ? hud.CurrentMode : EGuildmasterMode.None;
    }

    /// <summary>
    /// WHAT ONE PRESS OF A TABLE CAP CAN DO. Exactly one of these, decided once, in
    /// <see cref="Decide"/> — see section 8 of the class doc.
    /// </summary>
    internal enum CapPress
    {
        /// <summary>Nothing of this destination is standing — dispatch the game's own press and let
        /// the window open exactly as it always has.</summary>
        Open,

        /// <summary>This destination's panel is standing in the room — close it, the whole way, in
        /// THIS press: the game-side mode Exit and the parallel float release both.</summary>
        Close,

        /// <summary>The SHARED town-records window is standing under the OTHER of its two modes.
        /// This press switches its tab, which is a visible change and not a close; there is no
        /// second panel to close. The only case in which a standing window does not close.</summary>
        TabSwitch,

        /// <summary>A map SURFACE (WorldMap/City) that is already the current mode. A click there
        /// commits nothing at all (<c>allowSwitchOff</c> is false while a mode is active), and
        /// "closing" a map would mean switching the player to the other map. Nothing dispatched.
        /// </summary>
        Refused,
    }

    /// <summary>
    /// THE OBSERVABLE STATE OF ONE DESTINATION, sampled from the three places that can disagree —
    /// and the whole ModBuild 230 defect was that they DID disagree and only one of them was asked.
    ///
    /// <para><paramref name="floatLive"/> IS THE STRONG ANSWER (ModBuild 231).
    /// <c>ModalFallback.FloatIsLive</c> walks the converted set newest-first with exactly
    /// <c>FloatedWindowWithId</c>'s exclusions — dead panel, host-less, already flagged
    /// <c>UserClosing</c> (ModalFallback.4.Tick.cs:361-372) — but keyed on the window OBJECT, so a
    /// true here means a live world-space panel the player can look at and point at RIGHT NOW and no
    /// id can shadow it.</para>
    ///
    /// <para><paramref name="floatWithId"/> WAS that answer until ModBuild 231 and could never have
    /// been it for this family. It is ID-keyed, and <c>UITownRecordsWindow</c> and
    /// <c>UITempleWindow</c> both carry <c>UIWindowID.None</c> (his log: "'UI Town Records Window'
    /// (ID None)") — as does the map room's PERMANENT 'Quest Log Manager', which was floated from
    /// Player.log:3599 to :17019, i.e. for the entire ModBuild 230 session. So
    /// <c>FloatedWindowWithId(None)</c> returned the quest log every single time those two asked and
    /// this value was NEVER this window. Five perfectly good closes printed "float=STANDS …
    /// id-shadowed" — the string <see cref="Describe"/>'s own text calls the FAILURE reading
    /// (Player.log:7213, 7588, 11074, 11135, 11199, each followed four lines later by its own
    /// MAP ROOM WINDOW SLOT RELEASED, i.e. each a success). It is still sampled and still printed
    /// when it disagrees, because naming the shadowing window is what separates "id-shadowed" from
    /// "gone" — but it decides nothing.</para>
    ///
    /// <para><paramref name="floatByGrab"/> closes that gap from the other side:
    /// <c>TryGetGrabFor</c> is keyed on the window OBJECT (ModalFallback.3.WindowPanel.cs:221-235),
    /// so no id can shadow it. It does not exclude <c>UserClosing</c>, so it stays true for the one
    /// tick between a close and the release pass — which is honest, because the panel is still drawn
    /// during that tick. Pressing again inside that ~11 ms therefore re-runs an idempotent close
    /// rather than opening a second window on top of one that is leaving. The pair is reported
    /// separately in the log so a reader can always tell which of them answered, and their
    /// combination is what <see cref="Describe"/> turns into the words STANDS / RELEASING / none.</para>
    ///
    /// <para><paramref name="gameOpen"/> is the game's own <c>UIWindow.IsOpen</c>, which covers the
    /// frames after the window opens but before <c>ModalFallback.Tick</c> has converted it, and the
    /// case where conversion failed or was refused outright and there is no float at all.</para>
    ///
    /// <para>ASKED, NEVER REMEMBERED — unchanged from ModBuild 226 and the reason it survives. There
    /// is no "this cap opened it" flag anywhere in this class, because such a flag is wrong the
    /// instant the player uses the window's own X, which in this room he can, on any window, at any
    /// time.</para>
    /// </summary>
    private static void Sample(EGuildmasterMode mode, out UIWindow? window, out bool gameOpen,
                               out UIWindow? floatWithId, out bool floatByGrab, out bool floatLive,
                               out EGuildmasterMode current)
    {
        current = CurrentMode();
        window = IsWindowMode(mode) ? ModeWindow(mode) : null;
        gameOpen = window != null && window.IsOpen;
        floatWithId = window != null ? ModalFallback.FloatedWindowWithId(window.ID) : null;
        floatByGrab = window != null && ModalFallback.TryGetGrabFor(window, out _);
        floatLive = ModalFallback.FloatIsLive(window);
    }

    /// <summary>
    /// THE SAMPLE IN WORDS, and the words are chosen so the AFTER line of a close is unambiguous —
    /// which is the whole reason the ModBuild 229 log could not settle this report by itself.
    ///
    /// <para>THE THREE STATES ARE BOTH DECIDED OBJECT-KEYED SINCE ModBuild 231, and the id is a
    /// footnote it prints rather than a fact it reasons from:</para>
    /// <list type="bullet">
    ///   <item><b>STANDS</b> — <c>floatLive</c>: THIS window is a live float. A world-space panel is
    ///   up, not flagged for release, with a living host. He can see it.</item>
    ///   <item><b>RELEASING</b> — <c>floatByGrab</c> without <c>floatLive</c>: this window is in the
    ///   converted set (object-keyed grab, ModalFallback.3.WindowPanel.cs:286) but is not a live
    ///   float — it is flagged <c>UserClosing</c>, or its panel/host is already gone. So this is the
    ///   one tick between a close and the release pass, and on the AFTER half of a close line it is
    ///   the SUCCESS reading, not a failure.</item>
    ///   <item><b>none</b> — not converted at all. Combined with <c>IsOpen=False</c> this is the
    ///   closed state, full stop. Combined with <c>IsOpen=True</c> it is the ModBuild 231 report:
    ///   the game has the window open and the mod is showing nothing (see
    ///   <see cref="JudgeOpenWatch"/>, which now says so out loud and names the gate).</item>
    /// </list>
    ///
    /// <para>WHAT WAS REMOVED, AND WHY IT HAD TO BE. A fourth state, "STANDS (id-shadowed …)", stood
    /// here and was declared the FAILURE reading. It fired five times in the ModBuild 230 session and
    /// was wrong every time: every one of those closes had already succeeded (its
    /// MAP ROOM WINDOW SLOT RELEASED is four lines further down). It could not have fired any other
    /// way — the RELEASING branch it competed with required the id-keyed lookup to return NULL, and
    /// for a <c>UIWindowID.None</c> window it returned the permanently-floated quest log instead. An
    /// instrument that reports a success as its own named failure mode costs a build; see
    /// <see cref="Sample"/> for the line numbers.</para>
    /// </summary>
    private static string Describe(EGuildmasterMode mode, UIWindow? window, bool gameOpen,
                                   UIWindow? floatWithId, bool floatByGrab, bool floatLive,
                                   EGuildmasterMode current)
    {
        string floatState;
        if (floatLive)
            floatState = "STANDS (live world-space panel, object-keyed)";
        else if (floatByGrab)
            floatState = "RELEASING (converted, but it is not a LIVE float — it is flagged "
                         + "UserClosing, or its panel/host is already gone, and the record drops on "
                         + "the next ModalFallback tick)";
        else
            floatState = "none (not in the converted set)";
        if (window != null && floatWithId != null && !ReferenceEquals(floatWithId, window))
            floatState += $" [id {window.ID} is ALSO carried by the floated '{floatWithId.name}' — "
                          + "only UIWindowID.None can do that, and it decides NOTHING here since "
                          + "ModBuild 231]";
        return $"mode={mode} window={(window != null ? $"'{window.name}' (ID {window.ID})" : "<unresolved>")} "
               + $"IsOpen={gameOpen} float={floatState} currentMode={current} home={HomeMode()} "
               + $"roomActive={MapRoomDriver.Active}";
    }

    /// <summary>
    /// ONE LINE OF STATE, in the words the next log has to be readable in. Called twice per press —
    /// once BEFORE the decision and once AFTER the action — so a single grep answers "did one press
    /// reach the closed state?" without another hardware round.
    /// </summary>
    internal static string Observe(EGuildmasterMode mode)
    {
        Sample(mode, out UIWindow? window, out bool gameOpen, out UIWindow? floatWithId,
               out bool floatByGrab, out bool floatLive, out EGuildmasterMode current);
        return Describe(mode, window, gameOpen, floatWithId, floatByGrab, floatLive, current);
    }

    /// <summary>
    /// THE WHOLE DECISION, ONCE, AGAINST WHAT IS STANDING. Section 8 of the class doc is the
    /// evidence; this is the rule:
    ///
    /// <list type="number">
    ///   <item>a mode that is not a window mode is a map SURFACE — it opens (or, if it is already
    ///   current, a click would commit nothing, so it is <see cref="CapPress.Refused"/> and the cap
    ///   says so instead of dispatching a silent no-op);</item>
    ///   <item>if the SHARED town-records window is standing under the OTHER of its two modes, this
    ///   press is a <see cref="CapPress.TabSwitch"/> — decided by window IDENTITY, never by name;</item>
    ///   <item>otherwise the press CLOSES if anything of this destination is observably standing —
    ///   the live float, or the game's own IsOpen, or (as a tiebreaker only) the mode enum saying
    ///   this mode is current, which in practice implies the window is shown and is kept purely so a
    ///   momentarily unresolvable window can never turn a press into a no-op;</item>
    ///   <item>and it OPENS otherwise.</item>
    /// </list>
    ///
    /// <para>THE ORDER OF THE THREE STANDING SIGNALS IS THE FIX. ModBuild 226 asked the mode enum
    /// first and only fell through to the panel when the enum had already left; that is what took two
    /// presses. The panel is asked first now because the panel is what he is pressing the button to
    /// get rid of, and a mode that has exited while the panel still stands is not "closed".</para>
    /// </summary>
    internal static CapPress Decide(EGuildmasterMode mode, out UIWindow? window, out string observed)
    {
        Sample(mode, out window, out bool gameOpen, out UIWindow? floatWithId, out bool floatByGrab,
               out bool floatLive, out EGuildmasterMode current);
        observed = Describe(mode, window, gameOpen, floatWithId, floatByGrab, floatLive, current);

        bool modeIsCurrent = current == mode && current != EGuildmasterMode.None;

        if (!IsWindowMode(mode))
            return modeIsCurrent ? CapPress.Refused : CapPress.Open;

        // Outside the room the flat rules apply verbatim. The caps only exist while the room stands,
        // so this is a guard against a torn-down rail's last frame, not a reachable behaviour.
        if (!MapRoomDriver.Active || window == null)
            return CapPress.Open;

        // ModBuild 231: the first term was `ReferenceEquals(floatWithId, window)` and it was
        // structurally FALSE for every ID-less destination (see Sample) — the close only ever worked
        // because floatByGrab and gameOpen carried it. The object-keyed term is the honest one and
        // it can only ever ADD a standing signal, never remove one, so no press changes its answer.
        //
        // 2026-09-05: the three signals moved into IsStanding so the RAIL'S CAP LIGHTING can read
        // the same definition instead of a lookalike. The out-flags above are still produced, but
        // only for Describe — the log must keep naming each signal separately, which is the whole
        // reason a close's BEFORE/AFTER pair is readable.
        bool standing = IsStanding(window);

        // TownRecords / MercenaryLog: two modes, ONE UIWindow (UIGuildmasterHUD.cs:248-254). While
        // the other mode owns it and it is standing, this press switches the tab — it is not a
        // close, because there is no panel of this mode's own to close, and it is not a no-op either.
        if (standing && !modeIsCurrent && IsWindowMode(current)
            && ReferenceEquals(ModeWindow(current), window))
            return CapPress.TabSwitch;

        return standing || modeIsCurrent ? CapPress.Close : CapPress.Open;
    }

    /// <summary>
    /// IS ANYTHING OF THIS DESTINATION OBSERVABLY STANDING? The three signals <see cref="Decide"/>
    /// closes on, as ONE definition that the rail's cap lighting reads too.
    ///
    /// <para><b>WHY THIS BECAME A METHOD (2026-09-05, user item 4).</b> The rail decided which caps
    /// look lit and carry a collider from <c>c.Toggle.isOn</c> — the game's own bar
    /// <c>ToggleGroup</c>, which is SINGLE-MODE by construction (<c>UIGuildmasterButton.RefreshSelected</c>,
    /// decompiled :209: <c>toggle.interactable = !toggle.isOn</c>). With two destinations genuinely
    /// standing open in parallel — which is what the map room is FOR, and what the modal fix of the
    /// same day gave back — the game's mode enum can only ever name one of them, so <c>isOn</c>
    /// cannot answer "is this destination standing" for the other. It is not a question about the
    /// game's mode at all; it is a question about what is on the table, and the mod is the only thing
    /// that knows. Reading it here means the rail and the press path cannot drift apart, which is the
    /// invariant <c>MapButtonRail.Pressable</c> exists to keep.</para>
    ///
    /// <para><b>WHAT IT COSTS, because this runs per cap per frame and the rail's own doc has twice
    /// refused to put a converted-window walk there.</b> It is one walk of <c>Converted</c> (which
    /// holds at most a handful of floats and is scanned back-to-front with an early exit), one
    /// dictionary probe and one bool field — a few dozen reference compares for the whole rail, per
    /// frame. What was refused, and is still refused, is <see cref="Sample"/>: that resolves the
    /// window through the HUD singleton and a <c>GetComponent</c> every time. The caller passes a
    /// window it has already resolved and CACHED, so nothing is looked up here.</para>
    ///
    /// <para>All three signals are kept, and in <see cref="Decide"/>'s order. <c>floatByGrab</c>
    /// without <c>floatLive</c> is the single tick between a close and its release — counting it as
    /// standing is what stops the cap's LOOK and the press's DECISION from disagreeing for that
    /// tick, which would be a cap going dark under a panel that is still on the table.</para>
    /// </summary>
    internal static bool IsStanding(UIWindow? window) =>
        window != null
        && (ModalFallback.FloatIsLive(window)
            || ModalFallback.TryGetGrabFor(window, out _)
            || window.IsOpen);

    /// <summary>
    /// THE CAP'S ONE ENTRY POINT, and the one line per press the next log is read by. Returns TRUE
    /// when the press was fully handled here (a close, or a refusal) and the rail must NOT dispatch
    /// the game's own click; FALSE when the press is an open or a tab switch and the rail dispatches
    /// exactly as it always has.
    ///
    /// <para><paramref name="toggleIsOn"/> is the rail's OWN reading of the game's bar toggle, passed
    /// in for the log alone. It is the fact ModBuild 222 keyed the close on, and printing it beside
    /// the panel state is what makes a future disagreement between the two visible in one line
    /// instead of costing another test round.</para>
    /// </summary>
    internal static bool HandleCapPress(EGuildmasterMode mode, string source, bool toggleIsOn)
    {
        CapPress decision = Decide(mode, out UIWindow? window, out string before);
        // ModBuild 231 — THE TWO FACTS THE LAST LOG COULD NOT ANSWER, ON THE PRESS LINE ITSELF.
        //   * the ARC REGISTRY's occupancy, because the ModBuild 230 log's 42 "MAP ROOM WINDOW SLOT"
        //     lines against 21 "… RELEASED" read as a 2:1 leak and were not one: 21 of those 42 are
        //     the SECOND, "replayed against the final fit" line the same claim prints, so the true
        //     count is 21 claims / 21 releases and the registry held TWO of eight reservations at
        //     the moment the merchant first failed to appear. Printing held/free/by-whom on the
        //     press line means that question is answered in the line that raises it;
        //   * the CHURN FUSE's state for this window, which is what actually wedged him — see
        //     ModalFallback.LiftChurnFuseForDestinations.
        string arc = ModalFallback.ArcOccupancyLine();
        string fuse = ModalFallback.ChurnStateFor(window);
        string head = $"GUILDMASTER WINDOW PRESS: {mode} ({source}) -> {decision}. "
                      + $"OBSERVED BEFORE: {before} toggle.isOn={toggleIsOn} {arc} {fuse}.";

        switch (decision)
        {
            case CapPress.Close:
                // ONE ACTION, BOTH HALVES. See section 8: this is the only routine that flags the
                // parallel float for release AND runs the game-side mode Exit (through LeaveMode,
                // whose ModBuild 226 guard keeps it from exiting somebody else's mode) AND hides a
                // window the game still has open. Nothing here decides an order; the order is that
                // method's, and it has been right since ModBuild 184.
                CloseMode(mode, window, source);
                VRLog.Info(Scope, head + " LEFT BEHIND: " + Observe(mode)
                                  + " — READ THE LEFT BEHIND HALF AS THE VERDICT, it is the whole point "
                                  + "of this line. float=RELEASING or float=none, together with "
                                  + "IsOpen=False and a currentMode that is no longer this one, means ONE "
                                  + "PRESS REACHED THE CLOSED STATE, which is the entirety of the ModBuild "
                                  + "230 report ('nochmal drücken schließt das Fenster'). RELEASING is the "
                                  + "normal reading here and is a success: the float is flagged and its "
                                  + "host drops on the next ModalFallback tick — look for 'MODAL WINDOW: "
                                  + "… released' within a few lines to see it land. float=STANDS on this "
                                  + "line is the FAILURE reading: the release did not take, and the next "
                                  + "press would be the second one all over again. ModBuild 231: that "
                                  + "verdict is OBJECT-KEYED now, so the five 'STANDS … id-shadowed' "
                                  + "readings the ModBuild 230 session produced for the temple and "
                                  + "the records window cannot recur — they were successful releases "
                                  + "reported as this exact failure, because both windows carry "
                                  + "UIWindowID.None and the id-keyed lookup kept answering with the "
                                  + "permanently-floated quest log. That is exactly what "
                                  + "ModBuild 229 did on every mode-is-current press, because returning to "
                                  + "the map exits the mode while the sticky float survives the release "
                                  + "loop and is re-shown by ModalFallback's own ReassertStickyVisible in "
                                  + "the same tick (see section 8). IsOpen=True with float=none means the "
                                  + "opposite failure — the game window outlived the panel. READING ORDER: "
                                  + "this line is printed AFTER the act, so the MODAL CLOSE / GUILDMASTER "
                                  + "WINDOW / MAP TABLE BUTTON lines directly ABOVE it belong to this one "
                                  + "press. They are labelled '(X button)' because the close IS the "
                                  + "window's own X route — ModalFallback.CloseFloatedWindow — not because "
                                  + "an X was touched; the physical action is named in this line's own "
                                  + "source field.");
                return true;

            case CapPress.Refused:
                VRLog.Info(Scope, head + " WorldMap and City are map SURFACES, not windows: this room is "
                                  + "built on one of them, and the only thing 'close' could mean for a map "
                                  + "is switching to the other one, i.e. silently moving the player between "
                                  + "the world map and the city on a second press. A re-click would commit "
                                  + "nothing anyway (toggleGroup.allowSwitchOff is false while a mode is "
                                  + "active, UIGuildmasterHUD.cs:441), so nothing was dispatched. This cap "
                                  + "should already have been inert — MapButtonRail.Pressable refuses it — "
                                  + "so this line appearing means the mirror was one frame behind the game.");
                return true;

            case CapPress.TabSwitch:
                VRLog.Info(Scope, head + " TownRecords and MercenaryLog are two MODES over ONE UIWindow "
                                  + "(UIGuildmasterHUD.cs:248-254), and the OTHER one currently owns it. "
                                  + "So this press switches the shared window's TAB — a visible change, "
                                  + "not a close, and not a no-op: there is no panel of this mode's own to "
                                  + "close. Pressing the cap whose tab is already showing closes it like "
                                  + "every other destination, so each cap remains a strict toggle of its "
                                  + "own content. Dispatching the game's own press.");
                return false;

            default:
                // ModBuild 231: an open is now WATCHED. The whole of report 3 is a press that
                // decided Open, dispatched correctly, opened the game's window — and produced no
                // panel, silently, for the rest of the session. See TickOpenWatch.
                ArmOpenWatch(mode, window, source);
                VRLog.Info(Scope, head + " Nothing of this destination is standing, so this is the OPEN "
                                  + "half of the toggle and the game's own press goes out unchanged. The "
                                  + "next press on this cap will read the panel this one creates and close "
                                  + "it — the state that decides that is on this line's LEFT BEHIND "
                                  + "counterpart after the close. THE OPEN IS NOW WATCHED: if no live "
                                  + $"float for this window exists within {OpenWatchFrames} frames, a "
                                  + "'GUILDMASTER WINDOW DID NOT APPEAR' line names whichever gate "
                                  + "refused it. Silence after this line means the window arrived.");
                return false;
        }
    }

    // ---- ModBuild 231: DID THE WINDOW HE ASKED FOR ACTUALLY APPEAR? --------------------------
    //
    // User, verbatim: "Wenn ich die buttons wiederholt hintereinander drücke tauchen die Fenster
    // irgendwann gar nicht mehr auf. … Das darf niemals passieren."
    //
    // The ModBuild 230 log answered that question only by inference, and only to a reader who
    // already knew what to look for: the late merchant presses read "IsOpen=True float=none", i.e.
    // the game had the window open and the mod had no panel — but nothing said so out loud and
    // nothing named the cause. It took a grep for CATCH-ALL FUSE across 26 MB to find the two lines
    // that did (Player.log:10230 and :11217). A wedge that is invisible until the player notices it
    // is the thing being removed, so an OPEN now carries a deadline: one press, one watch, and at
    // most one extra line — the failure line — per press that fails.
    //
    // WHY A FRAME BUDGET AND NOT A TICK COUNT. The path from the dispatch to a live float is
    // several ModalFallback ticks (CatchAllGraceTicks alone is 2, then the append, then the convert
    // loop, then the pre-reveal re-place), and ModalFallback.Tick does not run every frame. 120
    // frames is ~2 s at 60 Hz — long enough that a slow conversion never trips it, short enough
    // that the line lands in the same screenful of log as the press it belongs to.
    //
    // THE WATCH IS SINGLE-SLOT ON PURPOSE. Two destinations cannot be opening at once: the game's
    // mode machine has exactly one current mode, and a second press supersedes the first. A new arm
    // therefore judges and closes the standing watch immediately rather than dropping it — an open
    // that was overtaken before it could appear is still an open that did not appear.

    private const int OpenWatchFrames = 120;

    private static UIWindow? _openWatchWindow;
    private static EGuildmasterMode _openWatchMode;
    private static string _openWatchSource = string.Empty;
    private static int _openWatchDeadline;
    private static int _openWatchArmedFrame;

    /// <summary>Arm the watch for one OPEN press. Judges any watch already standing first.</summary>
    private static void ArmOpenWatch(EGuildmasterMode mode, UIWindow? window, string source)
    {
        if (_openWatchWindow != null)
            JudgeOpenWatch("a later press superseded it before it could appear");
        if (window == null || !MapRoomDriver.Active || !IsWindowMode(mode))
            return;
        _openWatchWindow = window;
        _openWatchMode = mode;
        _openWatchSource = source;
        _openWatchArmedFrame = Time.frameCount;
        _openWatchDeadline = Time.frameCount + OpenWatchFrames;
    }

    /// <summary>
    /// Per-tick step of the open watch (from <see cref="Reconcile"/>). Two reference compares and an
    /// int compare while nothing is armed, which is every tick but the ~120 frames after an open.
    /// </summary>
    private static void TickOpenWatch()
    {
        UIWindow? window = _openWatchWindow;
        if (window == null)
            return;
        if (ModalFallback.FloatIsLive(window))
        {
            // It arrived. Silence is the success reading — the press line already said so.
            DisarmOpenWatch();
            return;
        }
        if (Time.frameCount < _openWatchDeadline)
            return;
        JudgeOpenWatch($"no live float appeared within {OpenWatchFrames} frames of the press");
    }

    /// <summary>Print the verdict for a watch that ended without a float, and disarm.</summary>
    private static void JudgeOpenWatch(string why)
    {
        UIWindow? window = _openWatchWindow;
        EGuildmasterMode mode = _openWatchMode;
        string source = _openWatchSource;
        int frames = Time.frameCount - _openWatchArmedFrame;
        DisarmOpenWatch();
        if (window == null)
            return;
        if (ModalFallback.FloatIsLive(window))
            return; // raced with the arrival — no failure to report
        VRLog.Warn(Scope, $"GUILDMASTER WINDOW DID NOT APPEAR: {mode} ('{window.name}', ID "
                          + $"{window.ID}) was pressed ({source}) {frames} frame(s) ago, the press "
                          + $"decided OPEN and dispatched — and {why}. "
                          + $"NOW: IsOpen={window.IsOpen} {Observe(mode)} "
                          + $"{ModalFallback.ArcOccupancyLine()} {ModalFallback.ChurnStateFor(window)}. "
                          + "WHAT REFUSED IT: " + ModalFallback.ExplainNotFloated(window)
                          + ". READ IT AS: this is the report 'tauchen die Fenster irgendwann gar "
                          + "nicht mehr auf' happening, caught at the moment it happens instead of "
                          + "being reconstructed from a 26 MB log afterwards. The clause above names "
                          + "the gate that said no, and it is the FIRST gate that said no — the ones "
                          + "after it were never reached and are not evidence.");
    }

    private static void DisarmOpenWatch()
    {
        _openWatchWindow = null;
        _openWatchMode = EGuildmasterMode.None;
        _openWatchSource = string.Empty;
        _openWatchDeadline = 0;
        _openWatchArmedFrame = 0;
    }

    /// <summary>
    /// LEAVE THE MODE, DO NOT MERELY HIDE THE WINDOW. Called from
    /// <c>ModalFallback.CloseFloatedWindow</c> before the normal Escape/Hide path. Presses the
    /// bar's map button through the SAME dispatch the table caps use, so
    /// <c>UIGuildmasterHUD.UpdateCurrentMode</c> runs the current mode's Exit — which is the only
    /// thing that takes the party display back out of selection mode (see the class doc).
    /// Returns true when the press went out.
    ///
    /// <para>ModBuild 226 — IT NOW REFUSES WHEN THIS WINDOW IS NOT THE CURRENT MODE'S. Until now
    /// the X on ANY destination window returned home, whatever the mode machine was doing. In the
    /// map room that is wrong in both directions and the log shows both:
    /// <list type="bullet">
    ///   <item>harmlessly, when the machine is already at home — Player.log:65175 and 163415, "MAP
    ///   TABLE BUTTON 'WorldMap' pressed (X button on '…') while it is ALREADY the current mode …
    ///   nothing was dispatched". A measured no-op, but one that reads like a failure;</item>
    ///   <item>harmfully, when a DIFFERENT destination is current. Closing a merchant window the
    ///   game hid ten minutes ago would then press the map button and exit the TEMPLE the player
    ///   is standing in. Nothing in the report asks for that, and parallel windows
    ///   (<c>ModalFallback.MapRoomParallel</c>) make it reachable by construction.</item>
    /// </list>
    /// It is also what makes <see cref="CloseMode"/>'s leftover branch safe: that branch closes
    /// through <c>ModalFallback.CloseFloatedWindow</c>, which calls straight back into this
    /// method, and the guard is what stops the re-entry from exiting somebody else's mode.</para>
    ///
    /// <para>WHEN THE MODE CANNOT BE READ AT ALL (no HUD reachable, <c>CurrentMode</c> is
    /// <c>None</c>) the pre-226 behaviour is kept verbatim and the press goes out. A guard whose
    /// evidence is missing must not silently disable the ModBuild 184 fix.</para>
    /// </summary>
    internal static bool LeaveMode(UIWindow window, string source)
    {
        if (!MapRoomDriver.Active || !IsDestination(window))
            return false;

        EGuildmasterMode current = CurrentMode();
        if (current != EGuildmasterMode.None)
        {
            UIWindow? currentWindow = IsWindowMode(current) ? ModeWindow(current) : null;
            if (!ReferenceEquals(currentWindow, window))
            {
                VRLog.Info(Scope, $"GUILDMASTER WINDOW: NOT returning home for '{window.name}' "
                                  + $"({source}) — the current guildmaster mode is {current}, whose "
                                  + $"window is {(currentWindow != null ? "'" + currentWindow.name + "'" : "not a window at all")}, "
                                  + "so this window's mode has ALREADY exited and pressing the bar's map "
                                  + "button would not close it — it would only leave whatever mode IS "
                                  + "current. What is standing here is the map room's parallel VR float "
                                  + "(ModalFallback.MapRoomParallel), and the caller's own release is the "
                                  + "close. Before ModBuild 226 this press went out anyway: harmless when "
                                  + "the machine was already at home (it logged 'ALREADY the current mode "
                                  + "… nothing was dispatched'), and a silent exit of an unrelated "
                                  + "destination when it was not.");
                return false;
            }
        }
        bool pressed = ReturnHome($"{source} on '{window.name}'", $"closing '{window.name}'");

        // ITEM 6, 2026-09-05 — VERIFY THE OUTCOME, NOT THE PATH.
        //
        // `pressed` is ReturnHome's answer to "did the bar carry a button for the home surface",
        // and it has never been an answer to "did the mode actually leave". The ModBuild 447 host
        // log is why that distinction now has an instrument: at the point of no return the sweep
        // closed the shop window through exactly this method and the dispatch it triggered printed
        // "MAP TABLE BUTTON 'WorldMap' pressed (X button on 'UI Shop Item Window') but its game
        // object 'Map Button' is INACTIVE … nothing was dispatched" (:84182), four lines before
        // "MODAL CLOSE (X button): 'UI Shop Item Window' … closed via UIWindow.Hide()" (:84186).
        // The WINDOW went and the MODE stayed — and only the mode's Exit takes the party display
        // back out of selection mode (ModBuild 184/195). `pressed` was true throughout.
        //
        // So the mode is READ BACK off the game instead of being inferred from the dispatch. The
        // press is delivered off-bar since this round (MapButtonRail.SelectThroughTheGamesOwnApi),
        // which is what makes this line's happy branch reachable at the point of no return at all.
        EGuildmasterMode after = CurrentMode();
        bool left = after != current || current == EGuildmasterMode.None;
        // HW-VERIFY
        // ITEM 6 — THE ANSWER LINE, and it is at NOTE tier on purpose: the co-player always runs a
        // shipped build, where VRLog.Info is not printed, so the ReturnHome line beside it is
        // invisible on exactly the client where "a window left open on the peer is the same defect"
        // is decided. One line per destination close (five at most in one point-of-no-return
        // sweep), never per frame.
        //
        // THE FALSIFIER: this line reading LEFT=False. That is the defect, not the absence of the
        // line — absence means no destination was closed at all, which for a point-of-no-return
        // report means the sweep did not run and 'POINT OF NO RETURN OPENED at edge' is the line to
        // read instead. LEFT=False together with 'MAP TABLE BUTTON … is INACTIVE' means the off-bar
        // delivery did not fire; LEFT=False WITHOUT it means the dispatch landed and the game
        // declined the switch, which would be a different cause entirely.
        VRLog.Note(Scope, $"GUILDMASTER MODE EXIT: closing '{window.name}' ({source}) — LEFT={left}. "
                          + $"The guildmaster mode was {current} before and is {after} after, and the "
                          + $"home surface press {(pressed ? "went out" : "found no button on the bar")}. "
                          + "THIS IS THE OUTCOME, NOT THE PATH: hiding the window is not the close — "
                          + "only UIGuildmasterHUD.UpdateCurrentMode -> modes[current].Exit() takes "
                          + "the party display back out of selection mode (EnableSelectionMode makes "
                          + "every character slot non-interactable, ModBuild 184/195), so a false "
                          + "here is a window that closed and a room that did not. IT IS READ BACK "
                          + "FROM THE GAME because the dispatch's own return value lied at ModBuild "
                          + "447: the point-of-no-return sweep's press landed on an INACTIVE bar "
                          + "button and reported success (host Player.log:84182 against :84186). "
                          + "NOTHING GOES ON THE WIRE — this is one pointerClick's worth of local "
                          + "presentation, run identically and independently on each client.");
        return pressed;
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
    ///
    /// <para>ModBuild 226 — TWO CLOSES, BECAUSE THERE ARE TWO WAYS A WINDOW CAN BE STANDING. See
    /// section 7 of the class doc for the evidence. If the mode machine has already moved on and
    /// what is left is the map room's PARALLEL FLOAT, returning to the map closes nothing at all
    /// (the game has already run Exit; the press is the measured no-op the log calls "ALREADY the
    /// current mode"). That case is closed through <c>ModalFallback.CloseFloatedWindow</c> — the
    /// window's own X button, the one path that releases a float whose game window is already
    /// hidden — and nothing else about this method changed: while the mode IS current, the close is
    /// still the return home, still one pointerClick on the bar's own map Toggle.</para>
    ///
    /// <para>ModBuild 230 — AND THAT "NOTHING ELSE CHANGED" IS WHAT COST HIM THE SECOND PRESS. There
    /// is now ONE close and it is <c>ModalFallback.CloseFloatedWindow</c> for every case, because
    /// that routine already does BOTH halves and in the only order that leaves no orphan: it flags
    /// the parallel float for release (<c>wp.UserClosing</c>, ModalFallback.7.Close.cs:113-115),
    /// then calls straight back into <see cref="LeaveMode"/> for the game-side mode Exit — which
    /// still runs whenever this window IS the current mode's, so the ModBuild 184/195 party-display
    /// fix is untouched and the ModBuild 226 guard still stops it exiting somebody ELSE'S mode —
    /// and then hides the game window if the game still has it open. The old branch (B), a bare
    /// <see cref="ReturnHome"/>, exited the mode and left the sticky float standing; see section 8
    /// of the class doc for the log lines that show it happening four times in one session.
    /// <see cref="ReturnHome"/> survives as <see cref="LeaveMode"/>'s body and has no other
    /// caller.</para>
    ///
    /// <para>THE HOME==MODE REFUSAL IS GONE WITH THE BRANCH IT GUARDED. It only ever protected
    /// <see cref="ReturnHome"/> from being asked to return to the mode it was closing, i.e. a
    /// WorldMap/City cap reaching this path; that is now impossible one level up —
    /// <see cref="Decide"/> answers <see cref="CapPress.Refused"/> for a non-window mode and never
    /// <see cref="CapPress.Close"/> — and <see cref="LeaveMode"/> carries the same invariant anyway,
    /// since a map surface's <see cref="ModeWindow"/> is null and can never be
    /// <c>ReferenceEquals</c> to the window being closed.</para>
    /// </summary>
    internal static bool CloseMode(EGuildmasterMode mode, UIWindow? window, string source)
    {
        if (!MapRoomDriver.Active)
            return false;
        if (window == null)
        {
            VRLog.Warn(Scope, $"GUILDMASTER WINDOW: asked to close mode {mode} ({source}) but its "
                              + "UIWindow could not be resolved off UIGuildmasterHUD's own serialized "
                              + "references (no HUD reachable, or this mode owns no window). Nothing was "
                              + "dispatched — there is nothing on the table to close, and pressing the "
                              + "bar's map button on a guess would exit whatever mode IS current. "
                              + "Decide() only asks for a close when it has a window, so this line means "
                              + "the HUD went away between the sample and the act.");
            return false;
        }

        // THE ONE CLOSE. Both halves, in the order ModalFallback.CloseFloatedWindow already fixed:
        // flag the parallel float for release, run the mode's own Exit through LeaveMode when this
        // window IS the current mode's, hide the game window if the game still has it open. Nothing
        // new goes on the wire — this is the same local UI close the window's own X has performed
        // since ModBuild 184, and the purchase, blessing or enhancement a destination may have
        // committed was committed by ITS own button, not by leaving it.
        ModalFallback.CloseFloatedWindow(window);
        return true;
    }

    /// <summary>THE GAME-SIDE HALF of a close: press the bar's map button, exactly as a flat player
    /// leaves a shop, so <c>UpdateCurrentMode -> Exit</c> runs. <paramref name="source"/> travels
    /// into the cap press's own log line; <paramref name="what"/> is the human sentence for this one.
    ///
    /// <para>ModBuild 230: this is no longer a close ON ITS OWN and has exactly one caller,
    /// <see cref="LeaveMode"/>, which <c>ModalFallback.CloseFloatedWindow</c> reaches after it has
    /// already flagged the parallel float for release. Called alone it exits the mode and leaves the
    /// map room's sticky float standing — which is precisely the state the user reported as needing a
    /// second press (section 8 of the class doc).</para></summary>
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
    /// WHICH MAP SURFACE THE ROOM IS STANDING ON — <c>WorldMap</c> or <c>City</c>, and never
    /// anything else. This is <see cref="HomeMode"/> under the name that says what it MEANS to a
    /// reader outside this class, exposed for <c>MapButtonRail.Pressable</c> (ModBuild 231).
    ///
    /// <para>IT IS STICKY AND THAT IS THE POINT. <see cref="TrackHomeMode"/> writes it only when the
    /// game's current mode IS one of the two surfaces, so opening the merchant does not change it
    /// and closing the merchant does not change it back. That is exactly the property the map-surface
    /// caps needed and did not have: they were reading the game's per-button
    /// <c>toggle.interactable</c>, which the game flips on EVERY mode change (<c>RefreshSelected</c>,
    /// decompiled UIGuildmasterButton.cs:206-219, <c>toggle.interactable = !toggle.isOn</c>), so the
    /// World-Map cap greyed and un-greyed every time a destination opened or closed — his report.</para>
    /// </summary>
    internal static EGuildmasterMode HomeSurface => HomeMode();

    /// <summary>Is this mode one of the two MAP SURFACES the room can be built on? Deliberately not
    /// <c>!IsWindowMode</c>: the enum also carries <c>None</c>, <c>QuestAccept</c>,
    /// <c>CityEncounter</c> and <c>MultiplayerQuest</c>, none of which is a surface and none of which
    /// has a cap on the table.</summary>
    internal static bool IsMapSurfaceMode(EGuildmasterMode mode) =>
        mode is EGuildmasterMode.WorldMap or EGuildmasterMode.City;

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
            _hudSweepBudget = HudColdSweepBudget;   // this scene HAS a HUD: re-arm the fallback
            return Singleton<UIGuildmasterHUD>.Instance;
        }
        if (_hudFallback != null)
            return _hudFallback;

        // A NEW SCENE IS A NEW QUESTION. These statics outlive a scene load, so a budget spent in a
        // scenario must not follow the player onto the campaign map — where an inactive-at-load HUD
        // is exactly the case the fallback exists for, and where a cold singleton would then never
        // be answered. Compared rather than subscribed: the handle read is one native call on a
        // path that already only runs while the singleton is cold, and a missed event is a defect
        // that hides, while a compared value cannot go out of date.
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (scene != _hudSweepScene)
        {
            _hudSweepScene = scene;
            _hudSweepBudget = HudColdSweepBudget;
            _hudSweepDue = 0;
        }

        if (_hudSweepBudget <= 0)
            return null;                            // the budget is spent — see the field's doc
        if (--_hudSweepDue > 0)
            return null;
        _hudSweepDue = HudSweepCadenceTicks;
        _hudSweeps++;
        _hudSweepBudget--;
        _hudFallback = Object.FindObjectOfType<UIGuildmasterHUD>(true);
        if (_hudFallback == null && _hudSweepBudget == 0)
        {
            VRLog.Info(Scope,
                $"GUILDMASTER HUD DISCOVERY: {HudColdSweepBudget} bounded sweep(s) found no "
                + "UIGuildmasterHUD and the singleton has never warmed up on this scene, so the "
                + "fallback STANDS DOWN and this class costs nothing until one appears. THIS IS "
                + "THE NORMAL STATE INSIDE A SCENARIO — the guildmaster HUD is a campaign-map "
                + "object and there is nothing here to find. The budget re-arms the instant the "
                + "singleton reports a HUD (so a scene that really has one keeps the "
                + "inactive-at-load safety net), and it is reset on every scene change. Before "
                + "ModBuild 227 this sweep ran once every "
                + $"{HudSweepCadenceTicks} ticks FOR EVER: the ModBuild 226 hardware log measured "
                + "it at a 28.5 ms hitch every 60 frames and 0.296 ms on every single tick, with "
                + "0 floated windows and the map room down.");
        }
        return _hudFallback;
    }

    /// <summary>
    /// How many bounded sweeps the cold-singleton fallback may spend before it stands down.
    ///
    /// <para><b>THE COLD SINGLETON IS NOT A TRANSIENT — IN A SCENARIO IT IS FOREVER (ModBuild 227).</b>
    /// <see cref="Hud"/>'s fallback exists for a real case: a GameObject inactive at load never runs
    /// <c>Awake</c>, so a HUD can exist that <c>Singleton</c> does not know about. ModBuild 195
    /// bounded that sweep to one per <see cref="HudSweepCadenceTicks"/> ticks and stopped there,
    /// on the unstated assumption that a cold singleton is a startup condition that resolves.
    ///
    /// <para>It does not. In a SCENARIO there is no guildmaster HUD at all and there never will be,
    /// so the cadence ran unbounded for the whole session. The ModBuild 226 log priced it exactly —
    /// <c>ModalFallback.Destinations.HomeMode 0.2963ms/tick (100%) … worst run 28.530ms |
    /// discovery: the HUD answered from Singleton 0 time(s) and from a scene sweep 24 time(s)</c>,
    /// on a tick with <b>0 floated windows and the map room down</b>. That 28.5 ms is the whole of
    /// <c>ModalFallback</c>'s <c>worst 30.03ms</c>.</para>
    ///
    /// <para>THE REPAIR IS THE ONE THE MAP-ROOM PREDICATE JUST TOOK, and it is the same defect: a
    /// scene sweep asking a question whose answer is a static field, on a scene where the answer is
    /// permanently no. Three sweeps is enough for the case the fallback was written for — an
    /// inactive-at-load HUD is present from the first frame, so if three sweeps ~0.7 s apart cannot
    /// see it, no number of further sweeps will. And the budget RE-ARMS on any tick the singleton
    /// answers, so a scene that genuinely has a HUD never loses the safety net.</para>
    /// </para></summary>
    private const int HudColdSweepBudget = 3;

    /// <inheritdoc cref="HudColdSweepBudget"/>
    private static int _hudSweepBudget = HudColdSweepBudget;

    /// <summary>The active scene's handle when <see cref="_hudSweepBudget"/> was last re-armed, so a
    /// budget spent on one scene cannot silence the fallback on the next. 0 is not a valid handle,
    /// so the first cold tick of a session always re-arms.</summary>
    private static int _hudSweepScene;

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
    private const int SubOpenWatch = 5;
    private const int SubCount = 6;

    /// <summary>What each sub-step covers, so a number can be acted on without reading the method.
    /// <c>HomeMode</c> the HUD lookup plus the current-mode read that feeds an X's return target;
    /// <c>ReArm</c> the ModBuild 195 character-screen re-arm over the party display's slots;
    /// <c>SheetOutcome</c> the CHARACTER SHEET OUTCOME watcher; <c>Banner</c> the banner
    /// acquire/release reconcile — including, on transitions only, the PARTY SLOTS line;
    /// <c>IsDestination</c> the five-component identity test the PARENT runs per floated window, the
    /// only entry here that is not called from <see cref="Reconcile"/>; <c>OpenWatch</c> the ModBuild
    /// 231 deadline on a press that decided OPEN — two reference compares unless one is armed, and
    /// one gate walk (<c>ModalFallback.ExplainNotFloated</c>) on the tick a watch actually fails.
    /// </summary>
    private static readonly string[] SubNames =
    {
        "ModalFallback.Destinations.HomeMode",
        "ModalFallback.Destinations.ReArm",
        "ModalFallback.Destinations.SheetOutcome",
        "ModalFallback.Destinations.Banner",
        "ModalFallback.Destinations.IsDestination",
        "ModalFallback.Destinations.OpenWatch",
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
        // ModBuild 231: an armed open watch belongs to the session that pressed the cap. Dropped
        // SILENTLY rather than judged — the module going away is not a window failing to appear.
        DisarmOpenWatch();
        _sub = -1;
        _nextSubBreakdown = 0f;
        _baselineDone = false;
        ResetSubBreakdown();
    }
}
