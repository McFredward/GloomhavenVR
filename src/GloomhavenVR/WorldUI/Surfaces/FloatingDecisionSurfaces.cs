using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// FLOATING PANEL DECISIONS (flows 2-4) — the shared float-in-front-of-the-HMD base for
/// the two mid-scenario decision panels the generic modal machinery can never see:
/// <c>UIAbilityCardPicker</c> (doom flows) and the <c>UIScenarioDistributePointsManager</c>
/// popups (prevent-damage hero select / redistribute damage). Both are plain serialized
/// <c>window</c> GameObjects — NOT <c>UIWindow</c>s — so <see cref="ModalFallback"/> (which
/// tracks UIWindow visibility) and the <see cref="ModalFallback.DecisionDock"/> registry
/// (typed on UIWindow) never float them: in VR they sat invisible on the hidden 2D stack
/// while the rule engine waited forever (a hard SRL gate / worker-thread spin-wait — see
/// each subclass). The pattern here is <see cref="DialogSurface"/>'s: poll the shown panel,
/// convert its REAL widget tree pokeable via <see cref="CanvasConversion"/> (labels,
/// localization, interactable states and the game's own click handlers all ride along),
/// place it ONCE in front of the HMD (<see cref="DecisionDockSurface"/>'s "a decision must
/// never be invisible" fallback pose), and release/restore the instant the game hides it.
///
/// COMMIT PATH: deliberately NOT part of these surfaces — every one of these flows commits
/// through the Choreographer's own Ready/Undo/Skip buttons (readyButton.AlternativeAction /
/// SetOnClickOverrider / skip actions), which the mod already mirrors live: the board tray's
/// CONFIRM/UNDO keycaps (PlayTray.TickStatus → CardsGameApi.ClickReady/ClickUndo — the exact
/// 2D dispatch incl. queued alternative actions) and the ButtonCluster's Undo|Ready|Skip row.
/// The board banner (CardsDriver.UpdatePanelDecisionStatus) does the wayfinding.
///
/// MULTIPLAYER: mirrors the flat game exactly — the panel shows on every client (as in 2D),
/// and ALL gating lives in the game's own widgets/services: doom slots self-gate Select/
/// Deselect on <c>m_CurrentActor.IsUnderMyControl</c> (UIAbilityCardPickerSlot.cs:82/104),
/// the select popup host-gates +/− via <c>DistributeSelectPlayerActorService.CanAddPointsTo</c>
/// (FFSNetwork.IsHost), the assign popup via <c>DistributeDamageService</c>'s
/// <c>caster.IsUnderMyControl</c>. A non-deciding client can poke — and, exactly like a 2D
/// click, nothing happens; proxies (<c>ProxySelectAbilityCard</c>,
/// <c>ProxyRedistributeHealth</c>) replay the decider's actions. Zero wire changes.
///
/// LIFECYCLE/SAFETY: level-triggered on the game's own shown state; releasing restores the
/// panel to its exact 2D home (CanvasConversion restore records). The host raycaster is
/// re-enabled every tick (the game's UI-lock raycaster mirror must not dead-lock the one
/// panel that IS the decision — the ModalFallback floating-modal exemption). The manual A/X
/// screen chord remains the universal rescue (full 2D composite).
/// </summary>
internal abstract class FloatingDecisionSurface : WorldSurface
{
    /// <summary>Float distance in front of the HMD (real metres at diorama scale 1).</summary>
    private const float FloatDistanceMeters = 1.0f;

    /// <summary>Extra shrink on the floated panel (the ModalFallback window factor).</summary>
    private const float FloatScaleFactor = 0.7f;

    private bool _placed;

    /// <summary>The shown panel root to convert (null while hidden) — also the level trigger.</summary>
    protected abstract RectTransform? ShownPanel();

    protected sealed override RectTransform? FindTarget() => ShownPanel();

    protected override bool WantConverted =>
        base.WantConverted && !FlatScreen.ManualScreenActive && ShownPanel() != null;

    protected override void OnConverted()
    {
        _placed = false;
        VRLog.Info("WorldUI", $"{Name}: decision panel floated pokeable in front of the HMD " +
                              "(plain-GameObject window — invisible to the UIWindow modal machinery; " +
                              "restored to its 2D home when the game hides it). Commit stays on the " +
                              "board CONFIRM/UNDO + ButtonCluster Skip (the game's own Ready/Undo/Skip).");
    }

    public override void Tick()
    {
        bool hadPanel = Panel != null;
        base.Tick(); // convert / release / Place
        if (Panel == null)
        {
            _placed = false;
            if (hadPanel)
                VRLog.Info("WorldUI", $"{Name}: decision panel released — restored to its 2D home " +
                                      "(game hid it / conversion gate closed).");
            return;
        }
        // The one surface family that must accept input even under the game's UI-lock
        // raycaster mirror — these panels ARE the decision (ModalFallback modal exemption).
        if (Panel.HostRaycaster != null && !Panel.HostRaycaster.enabled)
            Panel.HostRaycaster.enabled = true;
    }

    /// <summary>Place ONCE per conversion in front of the HMD (retried until a camera exists);
    /// a spawn-only pose, never a per-frame billboard — a decision panel must hold still under
    /// an approaching fingertip.</summary>
    protected override void Place()
    {
        if (Panel == null || _placed)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Vector3 pos = h.position + fwd * (FloatDistanceMeters * scale);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        CanvasConversion.PlaceHost(Panel, pos, rot, scale * FloatScaleFactor);
        _placed = true;
    }
}

/// <summary>
/// FLOW 2 — the DOOM pickers (<c>UIAbilityCardPicker</c>). (a) Doom slots full:
/// <c>CActor.AddDoom</c> (CActor.cs:928-936) → <c>CActiveBonusDoomSlotChoice_MessageData</c>
/// + <c>GameState.WaitingForMercenarySpecialMechanicSlotChoice = true</c> — EVERY ability's
/// Perform() early-returns while that flag is set, so an invisible picker is a hard rule-
/// engine gate — → Choreographer.cs:10898 shows the picker with <c>DoomPickerChoice</c>
/// options; commit = readyButton.AlternativeAction (→ <c>ReplaceDoom</c> + Hide +
/// StepComplete), Undo = ClearSelection overrider, Skip = Hide. (b) Transfer dooms:
/// CAbilityTransferDooms.cs:42 → Choreographer.cs:10969, same picker; Skip auto-transfers
/// the first N. Selection networks through the game's own SelectAbilityCard/
/// DeselectAbilityCard actions (phase DoomTransfering), fired by the slot's own button
/// click — which is exactly what a poke/laser on the floated panel drives. The picker shows
/// REAL ability cards; per the user rule the fan treatment targets card-DISCARD flows, and
/// this is a doom-slot choice — the pokeable converted panel is the ruled-acceptable surface.
/// </summary>
internal sealed class DoomPickerSurface : FloatingDecisionSurface
{
    public override string Name => "DoomPicker";
    // Always on — user ruling 2026-08-11: essential (off = hard rule-engine deadlock; every
    // ability Perform() early-returns while the invisible picker waits).
    protected override bool ConfigEnabled => true;

    protected override RectTransform? ShownPanel()
    {
        UIAbilityCardPicker p = Singleton<UIAbilityCardPicker>.Instance;
        if (p == null || p.window == null || !p.window.activeSelf)
            return null;
        return p.window.transform as RectTransform;
    }
}

/// <summary>
/// FLOWS 3+4 — the <c>UIScenarioDistributePointsManager</c> popups. Flow 3 (ShowSelect,
/// Choreographer.cs:12078): "which hero burns a card to prevent this damage" — the SRL
/// worker thread SPIN-WAITS (GameState.cs:1191) until <c>GameState.SelectedPlayerToAvoidDamage</c>
/// runs from the readyButton's queued alternative action; HOST-only decision online. Flow 4
/// (ShowAssign, :11761): redistribute damage/health — commit = readyButton (→
/// <c>StoreHealthChanges</c> + StepComplete), Undo = Reset overrider; caster-controlled.
///
/// WHAT THE PLAYER CLICKS (verified in UIDistributePointsPopup/UIDistributePointsSlot): a
/// row of actor-portrait slots, each with plain uGUI <c>addPointButton</c>/<c>removePointButton</c>
/// ExtendedButtons (+/−; the slot body's own OnClick is gamepad-mode only) — all fully
/// driveable by the converted panel's poke/laser pointer events. MINIMUM VIABLE SURFACE:
/// this panel is the ONLY input path for BOTH flows — the flow-4 <c>WorldspaceStarHexDisplay</c>
/// (VR-native 3D star hexes) is DISPLAY-only (it merely reads
/// <c>m_SavedDistributeDamageService.GetAssignedPoints</c>, WorldspaceStarHexDisplay.cs:2870),
/// and flow 3 drives no 3D display at all; neither service has any TileBehaviour/actor-click
/// seam. So the converted popup is not partially redundant — it is the whole input surface,
/// and the readyButton commit is reachable via the board CONFIRM either way.
/// </summary>
internal sealed class DistributePointsSurface : FloatingDecisionSurface
{
    public override string Name => "DistributePoints";
    // Always on — user ruling 2026-08-11: essential (off = the rule engine spin-waits on an
    // invisible popup; this converted panel is the ONLY input path for both flows).
    protected override bool ConfigEnabled => true;

    /// <summary>The popup currently converted — the select and assign popups are two distinct
    /// scene instances; a switch releases the old conversion so the base re-converts.</summary>
    private UIDistributePointsPopup? _shown;

    protected override RectTransform? ShownPanel()
    {
        UIScenarioDistributePointsManager m = Singleton<UIScenarioDistributePointsManager>.Instance;
        if (m == null)
            return null;
        UIDistributePointsPopup? popup =
            m.distributePointsAssignPopup != null && m.distributePointsAssignPopup.IsShown
                ? m.distributePointsAssignPopup
            : m.distributePointsSelectPopup != null && m.distributePointsSelectPopup.IsShown
                ? m.distributePointsSelectPopup
            : null;
        _shown = popup;
        return popup != null && popup.window != null ? popup.window.transform as RectTransform : null;
    }

    public override void Tick()
    {
        // Popup switch under a live conversion (select popup closes, assign opens — or vice
        // versa — without a hidden frame in between): WorldSurface only converts while
        // Panel == null, so force-release the stale popup's conversion first (the decision-
        // dock prompt-switch lesson, WorldSurface.ReleaseCurrentPanel doc).
        UIDistributePointsPopup? before = _shown;
        RectTransform? now = ShownPanel();
        if (Panel != null && now != null && !ReferenceEquals(before, _shown) && before != null)
        {
            if (ReleaseCurrentPanel())
                VRLog.Info("WorldUI", "DistributePoints: popup switched (select ↔ assign) — previous " +
                                      "conversion released so the shown popup can float in its place.");
        }
        base.Tick();
    }
}

/// <summary>
/// FLOW 5 — the MAP-SIDE <c>UIDistributeRewardManager</c> popups. The third deadlock of the
/// 2026-09 family, and the first one that is not a scenario flow at all.
///
/// <para><b>THE REPORT (2026-09-03, ModBuild 367, verbatim):</b> "NEUER DEADLOCK entdeckt: Wegen
/// einem Reiseevent muss ich einen Gegenstand ablegen, allerdings passiert nichts weiter. Ich habe
/// kurz gesehen wie ein Fenster aufging das sich aber sofort wieder geschlossen hat bzw.
/// verschwunden ist. Somit ist ein Deadlock entstanden es ging nicht weiter."</para>
///
/// <para><b>WHAT THE LOG PROVES — the last four game-side lines of a 14,565-line run.</b> The road
/// event resolved and handed its rewards straight to the distribute manager, whose popup then went
/// up and never came down (Player.log :13741-13761, in order):</para>
/// <code>
///   :13741  [GUI] Event hide animation finished True                 &lt;- the window he SAW vanish
///   :13747  [WorldUI] UIWindow hidden: 'UI Event Window' (ID EventsPanel …)
///   :13753  [GUI] Hide party panel already closed, requested by UI Distribute Rewards Window
///                 (UIDistributeRewardManager) (True)
///   :13755  [GUI] Locked options by UI Distribute Rewards Window 2   &lt;- the map goes UNCLICKABLE
///   :13758  [AREA MANAGER] Register area Distribution (object UI Distribute Items Rewards Popup
///                 (ControllerInputAreaLocal))
///   :13761  [AREA MANAGER] Set Focused Area Distribution             &lt;- THE LAST GAME LINE, EVER
/// </code>
/// <para>Line :13758 is written from inside <c>UIDistributePointsPopup.Show</c>, on the statement
/// AFTER <c>window.SetActive(true)</c> (<c>controllerArea.Enable()</c>), so the popup provably went
/// active. <c>Hide()</c> calls <c>controllerArea.Destroy()</c>, which would print "Unregister area
/// Distribution" — that string appears NOWHERE in the log. The run then continues for another 800
/// lines of pure mod telemetry (Heartbeat #40 → #43, ~3,600 frames ≈ 40 s of the player standing
/// in front of nothing) before he quit. And the word "Distribute" appears in the WHOLE log only in
/// those game lines: not one MODAL WINDOW, FLOAT, CATCH-ALL, refusal or conversion line names it.
/// The mod did not float this popup, did not refuse it, and never saw it.</para>
///
/// <para><b>WHY IT IS INVISIBLE.</b> <c>UIDistributePointsPopup</c> is a
/// <c>Singleton&lt;UIDistributePointsPopup&gt;</c> whose visibility is a plain
/// <c>[SerializeField] GameObject window</c> toggled with <c>SetActive</c> — it is NOT a
/// <c>UIWindow</c>. <see cref="ModalFallback"/> tracks UIWindow visibility, the catch-all's
/// <c>CatchAllObserve(UIWindow, bool)</c> is typed on UIWindow, and the decision-dock registry is
/// typed on UIWindow, so all three are structurally blind to it. Its canvas is the map's
/// screen-space UI, which in VR is drawn by nobody (the mod retargets the game's cameras into the
/// flat-screen RT and that screen is down — the exact argument recorded in
/// ModalFallback.12.ScreenBind's header for the ESC menu). Result: active, focused, accepting
/// input, and not on any pixel the player can see.</para>
///
/// <para><b>WHY IT IS A HARD DEADLOCK AND NOT MERELY A MISSING PANEL.</b> This is the game's own
/// gate, read out of the decompile: <c>UIDistributeReward.Distribute</c> returns a
/// <c>CallbackPromise</c> that ONLY <c>OnConfirmClick</c> / <c>ProxyConfirmClick</c> resolves;
/// <c>UIDistributeRewardManager.Distribute</c> chains one promise per process and only then clears
/// <c>IsDistributing</c>; and <c>MapChoreographer.WaitDistributionEnds</c> (MapChoreographer.cs:2467-2470,
/// entered from <c>WaitToShowAchievements</c> at :1180) is literally
/// <c>yield return new WaitUntil(() =&gt; !Singleton&lt;UIDistributeRewardManager&gt;.Instance.IsDistributing)</c>.
/// On top of that the manager calls
/// <c>AdventureMapUIManager.LockOptionsInteraction(locked: true, blur: true)</c> — line :13755
/// above — so while the invisible popup waits, nothing ELSE on the map is clickable either. There
/// is no second input path: the map has no board CONFIRM keycap to fall back on, unlike flows 2-4.</para>
///
/// <para><b>SCOPE — THE FAMILY, NOT THE ROAD EVENT.</b> Fixing only the road event would be fixing
/// one call site of a gate that has eight. Every one of these funnels into the same
/// <c>UIDistributeReward.Distribute</c> → <c>popup.Show</c> → invisible-popup wait:
/// <c>UIEventPanel.cs:788</c> (road/city events — the one he hit),
/// <c>MapChoreographer.cs:419, :440, :850, :889</c> (scenario-completion and queued completion
/// rewards), <c>MapChoreographer.cs:938</c> (personal-quest rewards),
/// <c>UITownRecordsWindow.cs:138</c> (town records) and <c>DebugMenu.cs:1434</c>. And the manager
/// runs FIVE process types over the same popup + confirm button — DistributeGold,
/// DistributeItems (item / LoseItem / ConsumeItem — his case), DistributeModifiers,
/// DistributeConditions, DistributeGoldBag — each a separate <c>UIDistributeReward</c> instance
/// with its own popup. This surface is keyed on none of them individually: it asks the manager for
/// its reward UIs and floats whichever one's popup is showing, so all five processes and all eight
/// entry points are covered by construction.
/// THIS IS NOT ModBuild 361 (a float host created in the wrong scene) AND NOT ModBuild 364 (two
/// honesty clauses in a deadly embrace). Both of those were the mod mishandling a window it had
/// ALREADY taken; this is a window the mod could never see in the first place.</para>
///
/// <para><b>WHAT IS FLOATED, AND WHY NOT JUST THE POPUP.</b> The popup carries the actor slots and
/// their +/- buttons; the COMMIT lives on a different component, <c>UIDistributeReward.confirmButton</c>
/// (<c>onClick</c> → <c>OnConfirmClick</c> → <c>promise.Resolve()</c>). Floating
/// <c>popup.window</c> alone — which is what <see cref="DistributePointsSurface"/> does for the
/// scenario flows, correctly, because THERE the commit is the board's Ready button — would hand the
/// player a panel he can set and never submit: a second deadlock wearing a fixed bug's clothes. So
/// the float target is computed at runtime as the nearest common ancestor of the popup window and
/// the confirm button, which is the smallest subtree provably containing both. The chosen root is
/// NAMED in the log line below rather than assumed, because what contains what in a scene is not
/// something this file can read.</para>
///
/// <para><b>MULTIPLAYER.</b> Local presentation only — one CanvasConversion of the local client's
/// own popup, restored on release. Every gate stays the game's: the +/- buttons run
/// <c>DistributeItemService.AddPoint/RemovePoint</c>, which self-gate on <c>FFSNetwork.IsHost</c>
/// before replicating, and a non-host's poke does exactly what a non-host's 2D click does. The
/// confirm button likewise only sends <c>GameActionType.DistributeUIConfirm</c> when
/// <c>FFSNetwork.IsHost</c>; the proxy path (<c>ProxyConfirmClick</c>) is untouched. Nothing new on
/// the wire, no NetProtocol surface, no game state written from here.</para>
/// </summary>
internal sealed class DistributeRewardSurface : FloatingDecisionSurface
{
    public override string Name => "DistributeReward";

    /// <summary>Always on, for the same reason the sibling surfaces are: OFF is a hard deadlock
    /// with no escape on the map, not a degraded look. A config key here would be a dial whose
    /// only off-position is "the campaign cannot continue".</summary>
    protected override bool ConfigEnabled => true;

    /// <summary>
    /// MAP-CAPABLE. <see cref="WorldSurface.WantConverted"/> requires
    /// <c>Choreographer.s_Choreographer != null</c> — i.e. a scenario — which is exactly right for
    /// flows 2-4 and exactly wrong here: every one of the eight entry points listed above fires on
    /// the MAP, where there is no Choreographer at all. That single inherited term is why the
    /// existing decision surfaces could not have caught this even if one of them had known the
    /// type. Restated here without it, the other three terms verbatim.
    /// </summary>
    protected override bool WantConverted =>
        ConfigEnabled && WorldUIConfig.ConversionActive
        && !FlatScreen.ManualScreenActive && ShownPanel() != null;

    /// <summary>Seconds a distribution may run with nothing floated before the blind-spot alert
    /// fires. Long enough that a normal convert (next tick) never trips it, short enough that the
    /// line is in the log well before the player gives up.</summary>
    private const float BlindAlertSeconds = 3f;

    private UIDistributeRewardManager? _manager;
    private UIDistributeReward[]? _rewardUis;
    private bool _sweepTried;

    /// <summary>The reward UI whose popup we last resolved — a change under a live conversion
    /// forces a release (the prompt-switch lesson, <see cref="WorldSurface.ReleaseCurrentPanel"/>).</summary>
    private UIDistributeReward? _shownReward;

    // Per-distribution-episode latches, cleared when IsDistributing goes false.
    private bool _episodeOpen;
    private bool _floatLogged;
    private bool _blindLogged;
    private float _episodeStart;

    // What the verdict lines report. Written by FloatRoot, read by TickBlindWatch.
    private string _rootHow = "(not resolved)";
    private string _rootPath = "(none)";
    private string _popupName = "(none)";

    // FloatRoot memo. ShownPanel runs three times per tick (WantConverted, FindTarget, and the
    // switch check in Tick), and both Transform.name and GetComponent<Canvas> cost a marshalled
    // call each time — the memo keeps a shown popup at zero per-frame work and zero allocation,
    // which matters because this surface ticks on the map where the frame budget is already the
    // tight one. Keyed on the two objects the answer depends on; a change to either recomputes.
    private RectTransform? _rootMemo;
    private UIDistributeReward? _rootMemoFor;
    private GameObject? _rootMemoWindow;

    protected override RectTransform? ShownPanel()
    {
        UIDistributeRewardManager? mgr = Singleton<UIDistributeRewardManager>.IsInitialized
            ? Singleton<UIDistributeRewardManager>.Instance
            : null;
        if (mgr == null)
        {
            _manager = null;
            _rewardUis = null;
            _shownReward = null;
            return null;
        }

        // A scene load builds a new manager; drop the cached component list with it.
        if (!ReferenceEquals(mgr, _manager))
        {
            _manager = mgr;
            _rewardUis = null;
            _sweepTried = false;
        }

        UIDistributeReward[]? uis = ResolveRewardUis(mgr);
        if (uis == null)
        {
            _shownReward = null;
            return null;
        }

        UIDistributeReward? shown = null;
        for (int i = 0; i < uis.Length; i++)
        {
            UIDistributeReward ui = uis[i];
            if (ui == null)
            {
                // Something in the cache was destroyed without the manager changing — rebuild
                // next tick rather than dereferencing a tombstone.
                _rewardUis = null;
                _shownReward = null;
                return null;
            }
            UIDistributePointsPopup pop = ui.PopUp;
            // Read the GameObject directly rather than UIDistributePointsPopup.IsShown, whose body
            // is `window.activeSelf` with no null guard of its own.
            if (pop != null && pop.window != null && pop.window.activeSelf)
            {
                shown = ui;
                break;
            }
        }

        _shownReward = shown;
        if (shown == null)
            return null;

        GameObject window = shown.PopUp.window;
        if (_rootMemo != null && ReferenceEquals(shown, _rootMemoFor) && ReferenceEquals(window, _rootMemoWindow))
            return _rootMemo;

        _popupName = window.name;
        _rootMemoFor = shown;
        _rootMemoWindow = window;
        _rootMemo = FloatRoot(shown);
        return _rootMemo;
    }

    /// <summary>
    /// The manager's reward UIs. Primary path is a scoped <c>GetComponentsInChildren</c> on the
    /// manager's own subtree — the manager sits on the object the game calls "UI Distribute Rewards
    /// Window" and the popups are named after it ("UI Distribute Items Rewards Popup"), so the
    /// components are expected there. That is an expectation about a scene, not a proof, so a
    /// single fallback sweep runs ONCE per manager if the subtree comes back empty, and says so in
    /// the log: an unproven hierarchy assumption that silently returns nothing is precisely how
    /// this class of bug stays invisible. The sweep is latched — <c>FindObjectsOfType</c> is this
    /// project's default performance suspect and must never become a per-tick call.
    /// </summary>
    private UIDistributeReward[]? ResolveRewardUis(UIDistributeRewardManager mgr)
    {
        if (_rewardUis != null)
            return _rewardUis;

        UIDistributeReward[] found = mgr.GetComponentsInChildren<UIDistributeReward>(includeInactive: true);
        if (found.Length == 0)
        {
            if (_sweepTried)
                return null;
            _sweepTried = true;
            found = Object.FindObjectsOfType<UIDistributeReward>(includeInactive: true);
            VRLog.Info("WorldUI", "DistributeReward: no UIDistributeReward under the manager's own "
                                  + "subtree — fell back to a one-shot scene sweep and found "
                                  + found.Length + ". The scoped lookup is the fast path; if this "
                                  + "line appears the reward UIs live outside the manager and the "
                                  + "scoped path can be retired.");
            if (found.Length == 0)
                return null;
        }
        _rewardUis = found;
        return found;
    }

    /// <summary>
    /// The smallest subtree provably containing BOTH the popup window (the slots and their +/-)
    /// and the confirm button (the only thing that resolves the promise). Candidates, in order:
    /// the nearest common RectTransform ancestor; the reward UI's own rect; the popup window
    /// alone. A root Canvas is refused at every step — converting the map's whole screen canvas
    /// would rip every other UI element into the same world panel.
    /// </summary>
    private RectTransform? FloatRoot(UIDistributeReward reward)
    {
        Transform windowT = reward.PopUp.window.transform;
        Transform? confirmT = reward.confirmButton != null ? reward.confirmButton.transform : null;

        if (confirmT != null)
        {
            Transform? nca = NearestCommonAncestor(windowT, confirmT);
            if (nca is RectTransform ncaRect && !IsRootCanvas(ncaRect))
            {
                _rootHow = "nearest common ancestor of the popup window and the confirm button "
                           + "(both are inside it, so the player can assign AND submit)";
                _rootPath = ncaRect.name;
                return ncaRect;
            }
        }

        if (reward.transform is RectTransform rewardRect && !IsRootCanvas(rewardRect))
        {
            _rootHow = confirmT == null
                ? "the UIDistributeReward rect (no confirm button serialized on it)"
                : "the UIDistributeReward rect — the popup window and the confirm button have no "
                  + "usable common ancestor below the root canvas";
            _rootPath = rewardRect.name;
            return rewardRect;
        }

        _rootHow = "THE POPUP WINDOW ALONE — the confirm button is NOT inside the floated subtree, "
                   + "so the player may be able to assign and unable to submit. If this string is "
                   + "in the log, the fix is incomplete for that scene layout";
        _rootPath = windowT.name;
        return windowT as RectTransform;
    }

    private static bool IsRootCanvas(Component t)
    {
        Canvas c = t.GetComponent<Canvas>();
        return c != null && c.isRootCanvas;
    }

    /// <summary>Nearest common ancestor of two transforms, or null if they share none within a
    /// sane depth. Both walks are depth-capped so a malformed hierarchy cannot spin.</summary>
    private static Transform? NearestCommonAncestor(Transform a, Transform b)
    {
        const int MaxDepth = 64;
        int outer = 0;
        for (Transform? pa = a; pa != null && outer < MaxDepth; pa = pa.parent, outer++)
        {
            int inner = 0;
            for (Transform? pb = b; pb != null && inner < MaxDepth; pb = pb.parent, inner++)
            {
                if (ReferenceEquals(pa, pb))
                    return pa;
            }
        }
        return null;
    }

    public override void Tick()
    {
        // A popup switch under a live conversion (one reward's popup hides and the next process's
        // opens inside the same promise chain, with no hidden frame between): the base only
        // converts while Panel == null, so force the release here or the FIRST popup stays floated
        // over a second, invisible one — the DistributePointsSurface lesson, one flow over.
        UIDistributeReward? before = _shownReward;
        RectTransform? now = ShownPanel();
        if (Panel != null && now != null && !ReferenceEquals(before, _shownReward) && before != null)
        {
            if (ReleaseCurrentPanel())
                VRLog.Info("WorldUI", "DistributeReward: reward process switched under a live "
                                      + "conversion — previous popup released so the shown one can "
                                      + "float in its place.");
        }

        base.Tick();

        TickBlindWatch();
    }

    /// <summary>
    /// The instrument. It answers, per distribution episode and in ONE line, the question a
    /// hardware round actually has to settle: the game raised a distribute popup — did the mod put
    /// it somewhere the player can see, and if not, WHAT was in the way? A count would not have
    /// found this bug; the whole reason it survived a 14,565-line log is that nothing named the
    /// object. Both branches are latched per episode, so the pair is at most two lines per reward
    /// distribution, and the negative one only fires on the reported failure.
    /// </summary>
    private void TickBlindWatch()
    {
        UIDistributeRewardManager? mgr = _manager;
        bool distributing = mgr != null && mgr.IsDistributing;

        if (!distributing)
        {
            _episodeOpen = false;
            _floatLogged = false;
            _blindLogged = false;
            return;
        }

        if (!_episodeOpen)
        {
            _episodeOpen = true;
            _episodeStart = Time.unscaledTime;
            // Re-resolve the reward UIs once per distribution rather than once per manager: a
            // process UI that the game instantiates or re-parents late would otherwise be missing
            // from a cache built at the first distribution of the session and never rebuilt — and
            // "the lookup silently found nothing" is exactly the shape of the bug this class
            // exists to prevent. The rebuild is one scoped GetComponentsInChildren per reward
            // distribution, i.e. a handful per campaign hour, not a per-frame sweep.
            _rewardUis = null;
            _sweepTried = false;
        }

        if (Panel != null)
        {
            if (_floatLogged)
                return;
            _floatLogged = true;
            // HW-VERIFY: the positive half of the 2026-09-03 travel-event deadlock verdict. It is
            // the only line that says the map-side distribute popup reached the player's eyes, and
            // it names the popup and the exact subtree that was floated — because "did the confirm
            // button come with it" is the term that decides whether this fix is a fix or a second
            // deadlock. Once per distribution episode.
            VRLog.Note("WorldUI", "DISTRIBUTE POPUP FLOATED: '" + _popupName + "' is in world space "
                                  + "in front of the HMD. FLOAT ROOT: '" + _rootPath + "', chosen as "
                                  + _rootHow + ". The game's own widgets ride along — the slot +/- "
                                  + "buttons and the confirm button are the game's, clicked by poke "
                                  + "or laser, and the promise this popup gates "
                                  + "(UIDistributeReward.Distribute → OnConfirmClick → promise."
                                  + "Resolve, waited on by MapChoreographer.WaitDistributionEnds) "
                                  + "resolves exactly as it does in 2D. WHAT THIS DOES NOT CLAIM: "
                                  + "that the player pressed anything. If the campaign still does "
                                  + "not advance after this line, the panel is on screen and the "
                                  + "fault is downstream of the float.");
            return;
        }

        if (_blindLogged || Time.unscaledTime - _episodeStart < BlindAlertSeconds)
            return;
        _blindLogged = true;
        // HW-VERIFY: the negative half, and the line that would have ended the 2026-09-03 round in
        // one hardware run instead of a 14,565-line log with no mod line naming the popup at all.
        // It fires only when the game says it is distributing and the mod has floated nothing for
        // BlindAlertSeconds — i.e. exactly the reported deadlock — and it names WHICH term failed.
        VRLog.Alert("WorldUI", "DISTRIBUTE POPUP NOT FLOATED — the game is distributing a reward "
                              + "and the mod has no panel up after " + BlindAlertSeconds.ToString("0.0")
                              + " s. THIS IS THE 2026-09-03 TRAVEL-EVENT DEADLOCK: the map is locked "
                              + "(AdventureMapUIManager.LockOptionsInteraction) and MapChoreographer."
                              + "WaitDistributionEnds is waiting on a promise only the popup's own "
                              + "confirm button can resolve. MEASURED THIS TICK: manager="
                              + (mgr == null ? "null" : mgr.name)
                              + ", reward UIs resolved=" + (_rewardUis == null ? "NONE" : _rewardUis.Length.ToString())
                              + " (scene sweep tried=" + _sweepTried + ")"
                              + ", a popup reports shown=" + (_shownReward != null)
                              + ", popup='" + _popupName + "'"
                              + ", float root='" + _rootPath + "' via " + _rootHow
                              + ", conversion active=" + WorldUIConfig.ConversionActive
                              + ", manual flat screen up=" + FlatScreen.ManualScreenActive
                              + ". READ IT LIKE THIS: 'reward UIs resolved=NONE' means the lookup "
                              + "missed and no popup can ever be found — start there. 'a popup "
                              + "reports shown=False' with UIs resolved means the game has not "
                              + "activated a window yet and this line is early, not a fault. "
                              + "'manual flat screen up=True' means the player already has the 2D "
                              + "composite in front of him and can click the popup there, which is "
                              + "the standing escape.");
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _manager = null;
        _rewardUis = null;
        _shownReward = null;
        _sweepTried = false;
        _episodeOpen = false;
        _floatLogged = false;
        _blindLogged = false;
        _rootMemo = null;
        _rootMemoFor = null;
        _rootMemoWindow = null;
    }
}
