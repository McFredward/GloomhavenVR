using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// ModalFallback part 10 (see the split rules in ModalFallback.1.Core.cs:13-22): the
// UNKNOWN-WINDOW CATCH-ALL + the two deadlock-insurance enrollments (reward showcase,
// GlobalErrorMessage). NOTE on compile order: the ordinal filename sort lands "…10…"
// BETWEEN parts 1 and 2 ('.' < '0'), not after part 9 — which is fine, and the reason
// this part may exist at all: it contributes ONLY NEW members and no nested types, so
// the relative member order of the original parts 1-9 (the load-bearing property the
// numbering protects) is unchanged wherever this file sorts.

internal static partial class ModalFallback
{
    // =====================================================================================
    // CATCH-ALL for unknown scenario windows (deadlock INSURANCE).
    //
    // THE HOLE THIS CLOSES: FallbackIds tracks the 24 enum-known modal IDs and three polls
    // cover the known ID-less deadlockers — but any UIWindow whose UIWindowID is
    // scene-serialized to a value this class does not know (usually the enum default,
    // None) opens on the HIDDEN 2D stack while the game waits for its click: a SILENT
    // deadlock with no VR-side symptom at all. This exact class of bug shipped once
    // already (ItemCardPicker, the item-surrender flow — see CardsGameApi.OpenItemPicker).
    // Enumerating windows can never be complete against future game patches, so the
    // choke point (UIWindow_Transition_Patch sees EVERY window transition) now feeds a
    // catch-all: an unknown window that stays open in a scenario is floated generically
    // after a short grace. The DurabilityPanel rule is the design intent: a wrongly-
    // floated window is recoverable (grab bar, X, escape chord, attributable Warn log);
    // a dropped one is a silent deadlock.
    //
    // RACE HANDLING (the grace): explicit handlers must always win. A freshly shown
    // window may be claimed by the decision dock, adopted by a surface conversion or
    // picked up by the Cards item flow one or more ticks AFTER its Show transition
    // (DecisionDockSurface converts level-triggered with its own ClaimGraceSeconds;
    // the Cards driver reads the pickers in its own update). So an unknown window only
    // joins the generic open set once it has been continuously open for
    // CatchAllGraceTicks ticks AND passes every exclusion LEVEL-TRIGGERED on the tick it
    // would join — a claim/adoption that arrives later still wins, because the
    // exclusions are re-checked every tick and windows already floated release the
    // moment they leave OpenWindows (the part-4 release loop).
    // =====================================================================================

    /// <summary>Ticks an unknown window must stay open before the catch-all floats it —
    /// explicit handlers (decision dock, surfaces, Cards flows) claim within this window.</summary>
    private const int CatchAllGraceTicks = 2;

    /// <summary>Unknown shown windows → the Time.frameCount of their Show transition.</summary>
    private static readonly Dictionary<UIWindow, int> UnknownShown = new();

    /// <summary>
    /// HOTFIX (hardware round 2026-08-01, "zwei leere Fenster flattern"): per-instance verdict
    /// cache for <see cref="IsKnownHudWindow"/> — the component walk over e.g. the whole flat
    /// hand subtree is far too heavy to repeat per tick (the un-cached catch-all measured
    /// ~1000 ms/tick converting 'Cards Hands Manager'). Pruned with <see cref="UnknownShown"/>.
    /// </summary>
    private static readonly Dictionary<UIWindow, bool> HudVerdict = new();

    /// <summary>Churn fuse: floats per window NAME within the rolling window.</summary>
    private static readonly Dictionary<string, (int Count, float WindowStart)> FloatChurn = new();

    /// <summary>Window names the churn fuse suppressed for the rest of the session.</summary>
    private static readonly HashSet<string> ChurnSuppressed = new();

    /// <summary>A window name floating more than this often within <see cref="ChurnWindowSeconds"/>
    /// is NOT a stuck decision — decisions open once and wait. It is a self-cycling HUD banner
    /// (the observed Notification/PhaseBanner pattern: show→hide every few seconds), and every
    /// re-float pays a full conversion. Suppress it for the session (one Warn) — the manual A/X
    /// screen chord still reaches it, and the Warn drives explicit enrollment/exclusion.</summary>
    private const int ChurnMaxFloats = 3;
    private const float ChurnWindowSeconds = 60f;

    /// <summary>Per-window-name Warn latch: ONE "enroll it explicitly" line per window type.</summary>
    private static readonly HashSet<string> CatchAllWarned = new();

    /// <summary>Scratch for pruning <see cref="UnknownShown"/> (allocation-free steady state).</summary>
    private static readonly List<UIWindow> UnknownScratch = new(4);

    /// <summary>
    /// IDs the catch-all must NEVER float because the mod already handles them
    /// deliberately elsewhere (the converted/passive window sets, class doc of
    /// ModalFallback.1.Core.cs). The four hover-driven passives carry post-mortems:
    /// HelpBox (test #16) and TextInfoPanel (test #18) each became a SELF-SUSTAINING
    /// ModalUI lock when treated as modal — asserting ModalUI stops the board hover
    /// that is their ONLY hide path — and TrapInfoPanel is the UIPropInfoPanel
    /// trap/hazard/terrain/quest-item hover card (all four Show* methods fire
    /// exclusively from hover, see the FallbackIds audit). DoorInfoPanel /
    /// MapNodeInfoPanel are the same hover-popup family. The remainder are windows
    /// other VR conversions own: ConfirmationBox (DialogSurface; the Dialogs=off case
    /// is already routed by IsFallbackWindow), the stat panels (StatPanelSurface),
    /// CardHolder (Cards module), QuestTracker/MapObjectiveManager (HUD conversions).
    /// Town windows (Village/Shop/EnhancementShop/…) are deliberately NOT excluded:
    /// they cannot legitimately open mid-scenario, and if one ever does, floating it
    /// is the recoverable outcome (the DurabilityPanel rule).
    /// </summary>
    private static readonly HashSet<UIWindowID> CatchAllKnownHandled = new()
    {
        UIWindowID.HelpBox,
        UIWindowID.TextInfoPanel,
        UIWindowID.TrapInfoPanel,
        UIWindowID.DoorInfoPanel,
        UIWindowID.MapNodeInfoPanel,
        UIWindowID.ConfirmationBox,
        UIWindowID.ActorStatPanel,
        UIWindowID.EnemyCurrentTurnStatPanel,
        UIWindowID.CardHolder,
        UIWindowID.QuestTracker,
        UIWindowID.MapObjectiveManager,
    };

    /// <summary>
    /// Observe EVERY window transition (called from <see cref="OnWindow"/>, one line
    /// there): track unknown shown windows so the per-tick catch-all can grace-float
    /// them. Tracking is UNCONDITIONAL like the Open set (test #10 lesson — windows
    /// opened during loading must survive into the scenario); every judgement call
    /// (scenario gate, exclusions, config) is level-triggered in
    /// <see cref="TickCatchAll"/> so nothing is lost to an edge-time race.
    /// </summary>
    private static void CatchAllObserve(UIWindow window, bool shown)
    {
        if (!shown)
        {
            UnknownShown.Remove(window);
            return;
        }
        if (IsFallbackWindow(window.ID))
            return; // the explicit path owns it
        if (!UnknownShown.ContainsKey(window))
            UnknownShown[window] = Time.frameCount;
    }

    /// <summary>
    /// Per-tick catch-all step (called from Tick after the three explicit polls, BEFORE
    /// the sticky/convert logic reads <see cref="OpenWindows"/>): prune dead/closed
    /// unknown windows, then append every eligible one to <see cref="OpenWindows"/> so
    /// the ENTIRE existing machinery treats it like any tracked modal — float via
    /// TryConvertWindow (grab bar + X), Failed → flat screen, ModalUI (unknown IDs are
    /// never in <see cref="NonBlockingMenus"/>, so they count as blocking), escape
    /// chord, release-on-close. Also runs the two explicit enrollment polls (reward
    /// showcase, GlobalErrorMessage) so their comments live next to the mechanism.
    /// </summary>
    private static void TickCatchAll(bool inScenario)
    {
        // Part 2 enrollment #2 — mid-scenario reward showcase (see AddRewardShowcaseWindow).
        AddRewardShowcaseWindow(inScenario);

        if (UnknownShown.Count == 0)
            return;

        // Prune: destroyed or game-closed windows leave the tracker (level-triggered —
        // a Hide transition already removed most in CatchAllObserve; this catches
        // scene unloads / ForceHideWindows that destroy without a clean transition).
        UnknownScratch.Clear();
        foreach (KeyValuePair<UIWindow, int> kv in UnknownShown)
        {
            if (kv.Key == null || !kv.Key.IsOpen)
                UnknownScratch.Add(kv.Key!);
        }
        for (int i = 0; i < UnknownScratch.Count; i++)
        {
            UnknownShown.Remove(UnknownScratch[i]);
            HudVerdict.Remove(UnknownScratch[i]); // verdict cache lives exactly as long as tracking
        }
        UnknownScratch.Clear();

        // ROOM gate (ModBuild 178 — the caller's local is TableInFrontOfPlayer now): the flat
        // menu/town keeps current behaviour, because Menu2D auto-shows the full flat screen there
        // and a floated window would duplicate it. THE 3D MAP ROOM IS THE OPPOSITE CASE and is why
        // this is no longer a scenario test: the flat screen is off there by construction, so a
        // window that is not floated is shown NOWHERE. Tracking above stays live so a window that
        // opened during loading floats the moment the scenario settles. The catch-all itself is
        // always on — user ruling 2026-08-11: essential deadlock insurance (its off state
        // restored the ItemCardPicker silent-deadlock class; misbehaving window types are
        // handled by the churn fuse below, not by a global switch).
        if (!inScenario || !WorldUIConfig.ConversionActive)
            return;

        int now = Time.frameCount;
        foreach (KeyValuePair<UIWindow, int> kv in UnknownShown)
        {
            UIWindow window = kv.Key;
            if (window == null || now < kv.Value + CatchAllGraceTicks)
                continue; // grace: explicit handlers win same-open races
            // ModBuild 184 — THE FUSE MAY NOT COUNT A HOVER CARD. A hover card floats once per
            // hover; that IS its life cycle, not churn. The 183 hardware log is unambiguous: the
            // map's quest-preview popup floated four times while he swept the laser across the
            // icons, and the fuse then suppressed it FOR THE WHOLE SESSION — "Es kommen nun gar
            // keine Mouseovers mehr", and one build later "Mouseover funktioniert nach wie vor
            // nicht". The fuse's own premise (see ChurnMaxFloats) is "decisions open once and
            // wait"; a hover card is the one floated thing that is not a decision at all, so it
            // is exempt from both the count and the verdict. Nothing else about the fuse changes:
            // a genuinely cycling HUD banner is still capped after ChurnMaxFloats.
            bool hoverCard = IsMapRoomHoverCard(window);
            if (!hoverCard && ChurnSuppressed.Contains(window.name))
                continue; // fuse blew for this window type — session-suppressed (see ChurnMaxFloats)
            if (!CatchAllEligible(window))
                continue;
            if (ContainsWindow(OpenWindows, window))
                continue; // already carried (e.g. its serialized ID IS a tracked one)

            // CHURN FUSE (hotfix): every append below costs a full conversion when the part-4
            // loop floats it. A window type re-floating in a tight loop (show→hide HUD banners)
            // burned ~1000 ms/frame on hardware — cap it and move on.
            if (!hoverCard)
            {
                float nowT = Time.unscaledTime;
                if (!FloatChurn.TryGetValue(window.name, out (int Count, float WindowStart) churn)
                    || nowT - churn.WindowStart > ChurnWindowSeconds)
                    churn = (0, nowT);
                churn.Count++;
                FloatChurn[window.name] = churn;
                if (churn.Count > ChurnMaxFloats)
                {
                    ChurnSuppressed.Add(window.name);
                    VRLog.Warn("WorldUI", $"CATCH-ALL FUSE: window '{window.name}' re-floated " +
                                          $"{churn.Count}× in {ChurnWindowSeconds:0}s — a cycling HUD " +
                                          "banner, not a waiting decision; suppressed for this session " +
                                          "(manual A/X screen chord still reaches it). Exclude it explicitly.");
                    continue;
                }
            }

            OpenWindows.Add(window);
            // ONE Warn per window type — the hardware log drives future EXPLICIT
            // enrollment (add the ID/poll, then this line disappears for that window).
            if (CatchAllWarned.Add(window.name))
                VRLog.Warn("WorldUI", $"CATCH-ALL: unknown scenario window '{window.name}' " +
                                      $"(ID {window.ID}) floated — enroll it explicitly.");
        }
    }

    /// <summary>
    /// Level-triggered exclusion check, re-evaluated EVERY tick the window would join
    /// the generic set — so a claim/adoption that starts later than the grace still
    /// stands the catch-all down (and its float releases through the normal part-4
    /// release loop the moment the window leaves OpenWindows).
    /// </summary>
    private static bool CatchAllEligible(UIWindow window)
    {
        // Known-handled IDs (passives with post-mortems + surface-owned windows).
        if (CatchAllKnownHandled.Contains(window.ID))
            return false;
        // HOTFIX (hardware round 2026-08-01): known HUD OWNERS, matched by COMPONENT (their
        // window IDs are all scene-serialized None, so the ID set above cannot carry them).
        // These are permanent flat-HUD subsystems the mod already owns elsewhere — floating
        // them produced exactly the reported bug: empty bar+X windows flapping over the view
        // (Notification/PhaseBanner cycle show→hide constantly) and ~1000 ms conversions of
        // the whole hidden hand subtree ('Cards Hands Manager').
        if (IsKnownHudWindow(window))
            return false;
        // The three explicit polls resolve their own window instances — those join
        // OpenWindows through their polls (with their special handling: story-box X
        // exclusion, level-message groups), never through the catch-all.
        if (IsPollWindow(window))
            return false;
        // Decision-dock claims stand the generic path down completely (test #21/#22).
        if (DecisionDock.ClaimsWindow(window))
            return false;
        // Cards flows own the item picker window while a surrender/refresh/lose pick is
        // live (the ItemCardPicker deadlock got its OWN VR flow — item fan + banner +
        // item-use slot; floating the hidden 2D picker over it would double-handle).
        if (IsCardsOwnedItemPickerWindow(window))
            return false;
        // A window whose subtree is already adopted by ANY live conversion (a surface
        // docked its row, a host carries its root) is owned elsewhere this instant.
        if (IsAdoptedByConversion(window))
            return false;
        // ModBuild 181 — THE PARENT WINS IN THE MAP ROOM. User, on the character screen: "das Menu
        // [ist] jetzt in mehrere Elemente unterteilt die jeweils ein eigenes Fenster bekommen. Das
        // soll nicht sein — es soll … alles direkt in dem Fenster das für die Character UI
        // zuständig ist angezeigt werden." The party/character screen is a nest of UIWindows
        // ('Campaign Adventure Party Assembly Variant' carrying 'Campaign Party Assembly Character
        // Display' carrying 'Party Display UI'), and floating each one separately scatters a single
        // screen across the room. An open ANCESTOR window already renders this subtree inside its
        // own host, so a second float of the child is a duplicate, not a window.
        //
        // Level-triggered, like every other rule here: OpenWindows is rebuilt from scratch each
        // tick, so when the ancestor closes the child becomes eligible again by itself — and a
        // child that was floated FIRST releases by itself the moment the ancestor opens, because
        // it stops being re-added. Map-room scoped: in a scenario the flat screen composites
        // whatever the mod does not float, so nesting there is not a visual problem and the rule
        // would be a behaviour change with no report behind it.
        if (MapRoom.MapRoomDriver.Active && HasOpenAncestorWindow(window))
            return false;
        // Never float world-space UI: the generic float is a screen-space→world
        // conversion; a genuinely world-space window is already visible in VR.
        var rect = window.transform as RectTransform;
        if (rect == null)
            return false;
        Canvas? canvas = window.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
            return false;
        return true;
    }

    /// <summary>
    /// Known flat-HUD owners the catch-all must never float, matched by component because
    /// their window IDs are scene-serialized <c>None</c>:
    /// <c>CardsHandManager</c> (the hidden flat hand — the VR card fans ARE its surface),
    /// <c>CombatLogHandler</c> (adopted by <see cref="Surfaces.CombatLogSurface"/> — the
    /// instant-level conversion check misses it whenever that panel is currently released),
    /// <c>UINotificationManager</c> and <c>PhaseBannerHandler</c> (transient banners cycling
    /// show→hide — a float would flap and convert forever; they are ambient info, not
    /// decisions). Verdict cached per instance (<see cref="HudVerdict"/>): the component
    /// walk over the hand subtree is far too heavy to repeat per tick.
    /// </summary>
    private static bool IsKnownHudWindow(UIWindow window)
    {
        if (HudVerdict.TryGetValue(window, out bool hud))
            return hud;
        hud = window.GetComponentInParent<CardsHandManager>() != null
              || window.GetComponentInParent<CombatLogHandler>() != null
              || window.GetComponentInParent<UINotificationManager>() != null
              || window.GetComponentInParent<PhaseBannerHandler>() != null
              // ModBuild 179: UIGuildmasterHUD is the campaign map's PERMANENT bar, not a decision.
              // Floating it produced exactly the failure mode this list exists for — the log shows
              // it converted, released ("open=True, convertWanted=True") and re-converted until the
              // churn fuse blew and named it itself: "a cycling HUD banner, not a waiting decision".
              // Its VR surface is now WorldUI/MapRoom/MapButtonRail, which stands its buttons on the
              // table as physical caps — the same relationship the card fans have to CardsHandManager
              // one line above. Unconditional, not map-room-gated: this verdict is CACHED per window
              // instance below, so a gate that changed with the room would be sticky and wrong half
              // the time — and on the flat 2D map the screen shows the bar anyway.
              //
              // ModBuild 180 — AND THE TEST IS ON THE WINDOW'S OWN GAMEOBJECT, NOT ITS ANCESTRY.
              // 179 wrote GetComponentInParent here, which is true for every window the HUD OWNS:
              // shopWindow, templeWindow, trainerWindow, enhancementWindow are all serialized
              // children of it (decompiled UIGuildmasterHUD.cs:76-92). So pressing Merchant or
              // Temple opened the game's window and the catch-all then refused to float it — the
              // hardware log shows "MAP TABLE BUTTON 'Merchant' pressed" with NOTHING after it.
              // The BAR is the HUD; its windows are not, and only the bar belongs on this list.
              || window.GetComponent<UIGuildmasterHUD>() != null
              || window.GetComponentInChildren<CardsHandManager>(true) != null
              || window.GetComponentInChildren<CombatLogHandler>(true) != null
              || window.GetComponentInChildren<UINotificationManager>(true) != null
              || window.GetComponentInChildren<PhaseBannerHandler>(true) != null;
        HudVerdict[window] = hud;
        return hud;
    }

    /// <summary>Is this instance one of the three explicit polls' windows (story box,
    /// level-message groups, dialogPopup)? Instance compare — their IDs are scene-serialized.</summary>
    private static bool IsPollWindow(UIWindow window)
    {
        if (Singleton<StoryController>.IsInitialized)
        {
            StoryController sc = Singleton<StoryController>.Instance;
            if (sc != null && ReferenceEquals(sc.window, window))
                return true;
        }
        LevelMessagesUIHandler? lm = LevelMessagesUIHandler.s_Instance;
        if (lm != null)
        {
            if (lm.LevelMessageBoxLayoutGroup != null
                && ReferenceEquals(lm.LevelMessageBoxLayoutGroup.window, window))
                return true;
            if (lm.LevelMessageHelpTextLayoutGroup != null
                && ReferenceEquals(lm.LevelMessageHelpTextLayoutGroup.window, window))
                return true;
        }
        UIManager? manager = UIManager.Instance;
        if (manager != null && manager.dialogPopup != null
            && ReferenceEquals(manager.dialogPopup.Window, window))
            return true;
        return false;
    }

    /// <summary>
    /// The ItemCardPicker window while the Cards module's item-surrender/refresh flow
    /// (or the reward lose-item flow) presents it — identified exactly like
    /// <c>CardsGameApi.OpenItemPicker</c>: the pickers' publicized <c>picker.window</c>
    /// (ItemCardRefreshPicker.cs:14 / ItemRewardLosePicker.cs:15, ItemCardPicker.cs:27).
    /// Both pickers are checked because either may drive the (possibly shared) picker
    /// component; while open, the Cards item fan + banner + item-use slot are the VR
    /// affordance for it.
    /// </summary>
    private static bool IsCardsOwnedItemPickerWindow(UIWindow window)
    {
        if (Singleton<ItemCardRefreshPicker>.IsInitialized)
        {
            ItemCardRefreshPicker rp = Singleton<ItemCardRefreshPicker>.Instance;
            if (rp != null && rp.picker != null && ReferenceEquals(rp.picker.window, window))
                return true;
        }
        if (Singleton<ItemRewardLosePicker>.IsInitialized)
        {
            ItemRewardLosePicker lp = Singleton<ItemRewardLosePicker>.Instance;
            if (lp != null && lp.picker != null && ReferenceEquals(lp.picker.window, window))
                return true;
        }
        return false;
    }

    /// <summary>Is the window root inside (or above) any live CanvasConversion target —
    /// i.e. some surface already physicalizes part of it this instant?</summary>
    /// <summary>
    /// Does an OPEN <c>UIWindow</c> sit above this one in the hierarchy? Walks parents only — the
    /// child is inside the ancestor's subtree by construction, so the ancestor's float already
    /// renders it. See the call site for why this is map-room scoped.
    /// </summary>
    private static bool HasOpenAncestorWindow(UIWindow window)
    {
        Transform? t = window.transform.parent;
        while (t != null)
        {
            var above = t.GetComponent<UIWindow>();
            if (above != null && !ReferenceEquals(above, window) && AncestorWillBeFloated(above))
            {
                if (AncestorRefusalWarned.Add(window.name))
                    VRLog.Info("WorldUI", $"MAP ROOM: window '{window.name}' (ID {window.ID}) is NOT " +
                                          $"floated on its own — its ancestor '{above.name}' " +
                                          $"(ID {above.ID}) is floated and renders this subtree inside " +
                                          "its own host. The parent wins (ModBuild 181/184).");
                return true;
            }
            t = t.parent;
        }
        return false;
    }

    /// <summary>Per-child-name latch for the "parent wins" Info line — one per window type.</summary>
    private static readonly HashSet<string> AncestorRefusalWarned = new();

    /// <summary>
    /// IS THIS ANCESTOR ACTUALLY GOING TO BE A FLOATED WINDOW? (ModBuild 184.)
    ///
    /// <para>181's "parent wins" rule asked only whether an ancestor was OPEN, and 182 patched a
    /// symptom of that by exempting children with a root Canvas of their own. Both were wrong at
    /// the same spot: the rule's premise is <i>"the parent's float already renders this subtree"</i>,
    /// so the question is not whether an ancestor is open but whether it is <b>floated</b>. An
    /// ancestor that is open and permanently un-floatable suppresses its child and shows it
    /// NOWHERE — which is exactly what happened to the merchant. <c>UIShopItemWindow</c>,
    /// <c>UITempleWindow</c>, <c>UITrainerWindow</c>, <c>UINewEnhancementWindow</c> and
    /// <c>UITownRecordsWindow</c> are all serialized children of <c>UIGuildmasterHUD</c>
    /// (decompiled UIGuildmasterHUD.cs:76-92), and the HUD's own window is open forever while it
    /// is on the bar AND permanently refused by <see cref="IsKnownHudWindow"/> — so the shop
    /// window was refused, and 182's root-canvas exemption then let its INNER scroll view through
    /// instead. The 183 log shows the result exactly: 'Scroll View' floated, the shop window
    /// never did, and the merchant appeared as bare content with no frame and no background. His
    /// report: <i>"Der Händler und co sollte ein separates Fenster sein das spawned inklusive des
    /// jeweiligen Hintergrunds."</i></para>
    ///
    /// <para>THE ROOT-CANVAS EXEMPTION IS GONE WITH IT, and that is a simplification rather than a
    /// loss: a child canvas inside a floated host is ADOPTED by the conversion
    /// (CanvasConversion.2.Adopt — the MODAL DIAG lines list the adopted children and their pinned
    /// sorting order), so once the ancestor genuinely floats, the child renders inside it. The
    /// exemption only ever mattered while the ancestor did not float, and that case is now
    /// answered at its cause.</para>
    ///
    /// <para>Two ways an ancestor counts: it is already carried this tick (the explicit polls run
    /// BEFORE the catch-all, so an enrolled ancestor is in <see cref="OpenWindows"/> by now), or
    /// it is open and the catch-all itself would take it. The second test recurses up the chain —
    /// bounded by hierarchy depth, and correct by construction: "will anything above me float"
    /// is exactly the question.</para>
    /// </summary>
    private static bool AncestorWillBeFloated(UIWindow above)
    {
        if (ContainsWindow(OpenWindows, above))
            return true;
        return above.IsOpen && CatchAllEligible(above);
    }

    private static bool IsAdoptedByConversion(UIWindow window)
    {
        Transform root = window.transform;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            RectTransform target = panels[i].Target;
            if (target == null)
                continue;
            if (ReferenceEquals(target, root) || target.IsChildOf(root) || root.IsChildOf(target))
                return true;
        }
        return false;
    }

    /// <summary>Catch-all state teardown (module detach — mirrors the part-4 Detach resets).</summary>
    private static void CatchAllReset()
    {
        UnknownShown.Clear();
        CatchAllWarned.Clear();
        AncestorRefusalWarned.Clear();
        HudVerdict.Clear();
        FloatChurn.Clear();
        ChurnSuppressed.Clear();
        _rewardShowcaseOpen = false;
        ReleaseErrorFloat("module shutdown");
        ErrorModalOpen = false;
        ErrorScreenWanted = false;
        _errorOpenLogged = false;
        _errorConvertFailedLogged = false;
    }

    // =====================================================================================
    // Part 2 enrollment #1 — the MID-SCENARIO REWARD SHOWCASE (treasure chests).
    //
    // VERIFICATION RESULT (decompiled, 2026-08-01): the chest flow is Choreographer state
    // WaitingForRewardsProcess (Choreographer.cs:9600-9606 sets m_BlockClientMessage-
    // Processing=true, :2411-2432 waits) → ScenarioRewardManager.Show. ScenarioReward-
    // Manager is ABSTRACT with two mode-dependent subclasses:
    //  - CampaignScenarioRewardManager (campaign — the main mode) → CampaignRewards-
    //    Manager.ShowRewards (CampaignScenarioRewardManager.cs:21-47) → UICampaignReward-
    //    Window.Show → its own UIWindow.Show() (UICampaignRewardWindow.cs:46/62/132-139).
    //    That window's UIWindowID is SCENE-SERIALIZED and NOWHERE assigned in code —
    //    UIWindowID.RewardsPanel appears exactly once in the whole decompile (the enum
    //    member), so "this window's ID == RewardsPanel" is NOT provable from code. The
    //    FallbackIds entry (annotated UIRewardsManager) may therefore MISS this window.
    //  - GuildmasterScenarioRewardManager → UIRewardsManager.StartRewardsShowcase
    //    (GuildmasterScenarioRewardManager.cs:8-13) — the class the RewardsPanel
    //    annotation plausibly maps to (UIRewardsManager.cs:17/69/141).
    // Both windows DO pass through UIWindow.Show() → EvaluateAndTransitionToVisual-
    // State, so the transition patch sees them — but with an unknown serialized ID the
    // event path may drop them. ENROLLMENT: a poll on the game's own
    // ScenarioRewardManager.IsShown, resolving the concrete window per subclass —
    // provable from code where the ID is not. If the ID happens to BE RewardsPanel the
    // event path already carries it and AddPollWindow's dedupe makes this a no-op.
    // (The DistributeItemsRewards/DistributeGoldRewards popups never open mid-scenario:
    // CampaignScenarioRewardManager passes showAppliedEffects:false, and they are plain
    // GameObject toggles, not UIWindows — no enrollment needed.)
    // =====================================================================================

    private static bool _rewardShowcaseOpen;

    private static void AddRewardShowcaseWindow(bool inScenario)
    {
        UIWindow? win = null;
        if (inScenario && WorldUIConfig.ConversionActive
            && Singleton<ScenarioRewardManager>.IsInitialized)
        {
            ScenarioRewardManager mgr = Singleton<ScenarioRewardManager>.Instance;
            if (mgr != null && mgr.IsShown)
            {
                // Campaign chest showcase: manager (CampaignScenarioRewardManager.cs:15)
                // → rewardsWindow (CampaignRewardsManager.cs:90) → window
                // (UICampaignRewardWindow.cs:46) — all publicized.
                if (mgr is CampaignScenarioRewardManager campaign
                    && campaign.manager != null && campaign.manager.rewardsWindow != null)
                    win = campaign.manager.rewardsWindow.window;
                // Guildmaster showcase: UIRewardsManager.myWindow (UIRewardsManager.cs:69).
                else if (Singleton<UIRewardsManager>.IsInitialized)
                {
                    UIRewardsManager rm = Singleton<UIRewardsManager>.Instance;
                    if (rm != null)
                        win = rm.myWindow;
                }
            }
        }
        bool open = win != null && (win.IsOpen || win.IsVisible);
        LogPollTransition(ref _rewardShowcaseOpen, open,
            "reward showcase (ScenarioRewardManager.IsShown)");
        if (open)
            AddPollWindow(win);
    }

    // =====================================================================================
    // Part 2 enrollment #2 — GlobalErrorMessage (the Choreographer's ~230 catch blocks).
    //
    // VERIFICATION RESULT (decompiled, 2026-08-01): SceneController.GlobalErrorMessage
    // (SceneController.cs:126/136) is an ErrorMessage — a plain MonoBehaviour + IEscapable
    // (ErrorMessage.cs:22), NOT a UIWindow. It shows via gameObject.SetActive(true)
    // (ErrorMessage.cs:353 and siblings) and NEVER passes through UIWindow.EvaluateAnd-
    // TransitionToVisualState, so the Part-1 catch-all (fed by UIWindow_Transition_Patch)
    // can NOT catch it — verified, hence this dedicated poll. While it shows, the whole
    // game halts logic-level: Choreographer.Update early-outs (Choreographer.cs:2348),
    // SRL message processing stops (:1647/:3406), buttons/camera gate on ShowingMessage —
    // an error box hidden in VR = frozen game with no explanation. Dismissal is ONLY its
    // own buttons (ErrorMessage.cs:614/604 → the passed ErrorDelegate, usually
    // ErrorHandlingUnloadSceneAndLoadMainMenu), so the float carries NO mod X: Hide()
    // without the button's delegate would swallow the error and strand whatever recovery
    // the game intended. Poll reads the publicized backing FIELD _errorMessage — never
    // the GlobalErrorMessage getter, which lazily Addressables-INSTANTIATES the prefab
    // (SceneController.cs:620-631) and must not be triggered from a per-frame poll.
    //
    // MENU EXTENSION (2026-08-01, "Spielstand ist für eine Mehrspielerpartie" deadlock):
    // the same box also opens in MENU context (save-load MP/DLC prompts, load failures,
    // boot errors) where it is equally invisible — it lives on the persistent GlobalCanvas
    // that no captured camera carries, so the Menu2D flat screen cannot show it either.
    // TickErrorMessage therefore floats it in Menu2D too (kill-switch [WorldUI]
    // MenuPopupFloat; the ModalUI lock and the screen fallback stay scenario-only).
    // =====================================================================================

    /// <summary>True while the error box shows in a scenario → ModalUI (a genuine blocker).</summary>
    internal static bool ErrorModalOpen { get; private set; }

    /// <summary>True when the error box is open but could not be floated → raise the screen.</summary>
    internal static bool ErrorScreenWanted { get; private set; }

    private static ConvertedPanel? _errorPanel;
    private static GrabbableModal? _errorGrab;
    private static bool _errorOpenLogged;
    private static bool _errorConvertFailedLogged;

    /// <summary>
    /// The error box is NOT a UIWindow, so it has no <c>WindowPanel</c> record — these three
    /// fields are its private copy of the state a floated window keeps there, purely so the ONE
    /// pose re-place (<see cref="TickPoseRePlace"/>) covers this float too: it is content-fitted
    /// like any other modal, so its spawn clamps are computed from the same PRE-fit rect.
    /// </summary>
    private static float _errorExtraScale = WindowScaleFactor;
    private static SpawnAnchor _errorSpawnAnchor;
    private static bool _errorPoseRePlaceDone;

    /// <summary>
    /// Per-tick error-box service (called from Tick before the lock/screen policy):
    /// level-triggered like the other polls. Floats the ErrorMessage root directly via
    /// CanvasConversion (the window-float pattern minus UIWindow specifics); on
    /// conversion failure ErrorScreenWanted raises the full flat screen instead. Not
    /// gated by the catch-all kill-switch — this is an explicit enrollment, same tier
    /// as the story/level-message/dialog polls.
    /// </summary>
    private static void TickErrorMessage(bool inScenario)
    {
        ErrorMessage? err = null;
        SceneController controller = SceneController.Instance;
        if (controller != null)
            err = controller._errorMessage; // backing field — the getter would instantiate
        bool showing = err != null && err.ShowingMessage;

        if (!_errorOpenLogged && showing)
            VRLog.Warn("WorldUI", "MODAL FALLBACK poll: GlobalErrorMessage SHOWING — the game is " +
                                  "halted until its button is pressed; floating the error box in VR.");
        else if (_errorOpenLogged && !showing)
            VRLog.Info("WorldUI", "Modal fallback poll: GlobalErrorMessage closed.");
        _errorOpenLogged = showing;

        // The error blocks the game wherever it appears. The LOCK (ModalUI) stays scenario-only
        // like the polls — in Menu2D the mode composition ignores aux-modal anyway.
        ErrorModalOpen = showing && inScenario;

        // MENU-context float (user deadlock: loading a multiplayer save from the MAIN MENU —
        // SaveData.cs:232/244 `ShowGenericMessage("Consoles/CREATE_NEW_LOCAL_SAVE" /
        // "Consoles/OVERWRITE_EXISTING_LOCAL_SAVE")`, German "Dieser Spielstand ist für eine
        // Mehrspielerpartie…"; same box: DLC_REQUIRED, load-failure ERROR_SCENE_*): the old
        // scenario gate here rested on "the Menu2D flat screen already shows the 2D stack" —
        // which is FALSE for exactly this box. It is SetActive-shown on the persistent
        // boot-scene GlobalCanvas (SceneController.cs:92/620-631), a canvas no captured CAMERA
        // carries, so the flat screen never composited it: invisible in VR while the game
        // raycast-blocked the whole menu behind it (ErrorMessage's full-screen panel +
        // MainMenuUIManager.RequestDisableInteraction) — nothing clickable, waiting forever.
        // Every OTHER menu popup (confirmation-box family, EULA, sign-out, lobby prompts —
        // menu-popup inventory 2026-08-01) lives in the menu canvas hierarchy, IS captured and
        // stays operable via laser→virtual mouse, so the scenario gate for the WINDOW float
        // machinery stands untouched; only this capture-invisible error family floats in menu.
        // Deliberately independent of [WorldUI] ModalStyle: that setting picks between two ways
        // of SHOWING a scenario fallback window, and neither screen path can show this canvas —
        // floating is the only visibility there is. Always on — user ruling 2026-08-11:
        // essential (the former [WorldUI] MenuPopupFloat kill switch left a menu-context
        // hard deadlock when off).
        bool menuFloat = showing && !inScenario
                         && VRModeStateMachine.CurrentMode == VRMode.Menu2D;

        bool wantFloat = ((ErrorModalOpen && WorldUIConfig.ModalWindowStyle) || menuFloat)
                         && !FlatScreen.ManualScreenActive && WorldUIConfig.ConversionActive;

        if (!wantFloat || _errorPanel is { IsAlive: false })
        {
            ReleaseErrorFloat(showing ? "float no longer wanted" : "error box closed");
            // style=screen (or float impossible): the screen is the fallback visibility.
            ErrorScreenWanted = ErrorModalOpen && !wantFloat && !WorldUIConfig.ModalWindowStyle;
            if (!showing)
            {
                ErrorScreenWanted = false;
                _errorConvertFailedLogged = false;
            }
            return;
        }
        if (_errorPanel != null)
        {
            // Same one-surface-must-stay-clickable exemption as the floated windows.
            GraphicRaycaster raycaster = _errorPanel.HostRaycaster;
            if (raycaster != null && !raycaster.enabled)
                raycaster.enabled = true;
            _errorGrab?.Tick();
            return;
        }
        if (ErrorScreenWanted)
            return; // conversion already failed this open — screen path holds (retry next open)

        try
        {
            var rect = err!.transform as RectTransform;
            ConvertedPanel? panel = rect == null
                ? null
                : CanvasConversion.Convert(rect, "Modal_GlobalErrorMessage", pokeable: true,
                    sortingOrder: ModalHostSortingOrder, useModLayer: true);
            if (panel == null)
            {
                if (!_errorConvertFailedLogged)
                {
                    _errorConvertFailedLogged = true;
                    VRLog.Warn("WorldUI", "MODAL WINDOW: GlobalErrorMessage could not be floated " +
                                          "(no rect / conversion returned null) — raising the flat " +
                                          "screen for it instead.");
                }
                ErrorScreenWanted = true;
                return;
            }
            float extraScale = DeriveWindowScale(panel);
            // Keep the placement inputs so the one-shot re-place can replay them once this float's
            // content fit lands (see TickPoseRePlace) — invalid anchor if no head pose existed.
            _errorSpawnAnchor = PlaceAtHmd(panel, extraScale, Converted.Count)
                ? s_lastSpawnAnchor
                : default;
            _errorExtraScale = extraScale;
            _errorPoseRePlaceDone = false;
            var grab = new GrabbableModal();
            grab.Build(panel, extraScale, "GlobalErrorMessage");
            _errorPanel = panel;
            _errorGrab = grab;
            VRLog.Info("WorldUI", "MODAL WINDOW: GlobalErrorMessage floated in front of the HMD " +
                                  "(grabbable, NO mod X — its own buttons are the only exit; " +
                                  "Hide() would swallow the error and skip the game's recovery).");
        }
        catch (Exception ex)
        {
            if (!_errorConvertFailedLogged)
            {
                _errorConvertFailedLogged = true;
                VRLog.Error("WorldUI", "MODAL WINDOW: floating GlobalErrorMessage FAILED " +
                                       $"({ex.GetType().Name}: {ex.Message}) — raising the flat " +
                                       "screen for it instead.");
            }
            ErrorScreenWanted = true;
        }
    }

    /// <summary>Release the error-box float (restores its exact 2D home; reversible mutation).</summary>
    private static void ReleaseErrorFloat(string reason)
    {
        if (_errorPanel == null)
            return;
        _errorGrab?.Destroy();
        _errorGrab = null;
        CanvasConversion.Release(_errorPanel);
        _errorPanel = null;
        // Drop the re-place state with the float: the next open is a fresh spawn (its own anchor).
        _errorSpawnAnchor = default;
        _errorPoseRePlaceDone = false;
        VRLog.Info("WorldUI", $"MODAL WINDOW: GlobalErrorMessage float released ({reason}) — " +
                              "restored to its 2D home.");
    }
}
