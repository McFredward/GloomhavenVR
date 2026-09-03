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

    /// <summary>
    /// Extra clause appended to this surface's RELEASE line — empty by default.
    ///
    /// <para><b>WHY IT EXISTS (2026-09-03, round two of the travel-event deadlock).</b> The release
    /// marker below reads "(game hid it / conversion gate closed)", which is ONE string for two
    /// entirely different events — and for a third it does not even mention: the framework pruning
    /// a panel whose TARGET WAS DESTROYED (<see cref="WorldSurface.Tick"/>'s first branch nulls
    /// <c>Panel</c> before the gate is ever evaluated, and lands on this same line with
    /// <c>hadPanel</c> true). ModBuild 371's log shows exactly this line one frame after a
    /// successful float of the map-side distribute popup, and nothing in it says which of the three
    /// happened. That ambiguity is the whole reason the bug survived into a second hardware round.
    /// The marker string itself must NOT be reworded — scripts/check-surface.py reads a vanished
    /// marker as a removal, which costs a round — so the naming is APPENDED here instead, by the
    /// subclass that is the only thing that knows what its own gate terms were.</para>
    /// </summary>
    protected virtual string ReleaseDetail() => string.Empty;

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
                                      "(game hid it / conversion gate closed)." + ReleaseDetail());
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
    /// <summary>
    /// <para><b>ROUND TWO, 2026-09-03. THE THIRD TERM IS NOW HELD WHILE THE GAME IS DISTRIBUTING.</b>
    /// User, second report of the same deadlock, verbatim: <i>"Deadlock besteht nach wie vor.
    /// Nachdem Reiseevent sehe ich weiterhin kein Fenster bei dem ich irgendwas auswählen kann.
    /// Kurz sehe ich ein Fenster das aufgeht und dann sofort wieder verschwindet."</i></para>
    ///
    /// <para><b>WHAT THE ModBuild 371 LOG PROVES.</b> The fix WORKED and then the mod LET GO, about
    /// one frame later (.planning/debug/Player.log, consecutive lines :7361-:7368):
    /// <c>Converted 'DistributeReward'</c> → <c>DISTRIBUTE POPUP FLOATED</c> (float root = the
    /// popup window, confirm button inside it) → <c>HIT RECT … 30 visible graphic(s)</c> → one
    /// <c>PANEL DRAW ORDER</c> frame → <c>decision panel released</c>. Three seconds later
    /// <c>DISTRIBUTE POPUP NOT FLOATED</c> reports <c>conversion active=True, manual flat screen
    /// up=False</c>, so of the four terms below only <c>ShownPanel() != null</c> can have flipped.
    /// The game did NOT hide the popup: <c>UIDistributePointsPopup.Hide()</c> runs
    /// <c>controllerArea.Destroy()</c> → <c>ControllerInputAreaManager.UnregisterArea</c> → the
    /// string "Unregister area Distribution", and that string occurs ZERO times in the whole log
    /// (the LAST <c>[AREA MANAGER]</c> line of the entire 8071-line run is the
    /// <c>Set Focused Area Distribution</c> at :7358, before the float).</para>
    ///
    /// <para><b>SO THE RELEASE WAS THE MOD'S OWN DECISION, AND IT IS THE THING THAT KILLS THE RUN.</b>
    /// While <c>UIDistributeRewardManager.IsDistributing</c> is true there is NO legitimate reason
    /// for this surface to hand the panel back: <c>MapChoreographer.WaitDistributionEnds</c>
    /// (MapChoreographer.cs:2467-2470) is literally
    /// <c>yield return new WaitUntil(() =&gt; !Singleton&lt;UIDistributeRewardManager&gt;.Instance.IsDistributing)</c>,
    /// the map is locked by <c>LockOptionsInteraction</c>, and the ONLY thing that resolves the
    /// promise is this popup's own confirm button. So the third term is held: while the game says
    /// it is distributing AND a panel is already floated, a momentarily-unresolvable popup does not
    /// end the float. The hold is correct whichever of the four sub-terms of
    /// <see cref="ShownPanel"/> flipped, because the release is the harm.</para>
    ///
    /// <para><b>THE HOLD OVERRIDES EXACTLY ONE TERM — deliberately, and this is what stops it from
    /// becoming the next deadlock.</b> <c>ConfigEnabled</c>, <c>ConversionActive</c> and
    /// <c>!ManualScreenActive</c> are all still hard gates: turning conversion off releases, and
    /// raising the 2D composite (by the player's A/X chord OR by this surface's own rescue, see
    /// <see cref="TickBlindWatch"/>) releases and restores the popup to the 2D UI where the
    /// composite draws it. See <see cref="HoldForDistribution"/> for the full list of the ways the
    /// hold ends.</para>
    /// </summary>
    protected override bool WantConverted =>
        ConfigEnabled && WorldUIConfig.ConversionActive
        && !FlatScreen.ManualScreenActive
        && (ShownPanel() != null || _holdArmed);

    /// <summary>Seconds a distribution may run with nothing floated before the blind-spot alert
    /// fires. Long enough that a normal convert (next tick) never trips it, short enough that the
    /// line is in the log well before the player gives up.</summary>
    private const float BlindAlertSeconds = 3f;

    /// <summary>
    /// Seconds the game may be distributing with NOTHING floated before the guaranteed escape
    /// fires and the mod forces the 2D composite up. Comfortably past
    /// <see cref="BlindAlertSeconds"/> so the diagnostic verdict always lands in the log FIRST
    /// (a rescue that pre-empts its own diagnosis costs the next round), and past the multiplayer
    /// ready-up wait in <c>UIDistributeRewardManager.Process</c>, which legitimately holds
    /// <c>IsDistributing</c> true with no popup while the other players confirm.
    /// </summary>
    private const float RescueSeconds = 8f;

    /// <summary>
    /// Hard cap on ONE continuous hold. A hold that never lets go is itself a deadlock generator,
    /// so this is the outer bound on the one gate term this surface overrides. It is generous
    /// because the hold only engages while the game reports NO shown popup — in the healthy flow
    /// (player reading the reward, assigning points) the popup IS shown and the hold never arms at
    /// all, so a hold standing this long means something is genuinely wrong. Lapsing does not put
    /// the player in front of nothing: the same tick that drops the hold raises the rescue screen.
    /// </summary>
    private const float HoldMaxSeconds = 90f;

    /// <summary>Dwell before a hold is REPORTED (it engages instantly — see
    /// <see cref="TickHold"/>). Long enough that the legitimate one-or-two-frame gap between two
    /// reward processes of the same distribution stays out of the log, short enough that a real
    /// failure is named well before <see cref="BlindAlertSeconds"/>.</summary>
    private const float HoldLogSeconds = 0.5f;

    private UIDistributeRewardManager? _manager;
    private UIDistributeReward[]? _rewardUis;
    private bool _sweepTried;

    /// <summary>The reward UI whose popup we last resolved — a change under a live conversion
    /// forces a release (the prompt-switch lesson, <see cref="WorldSurface.ReleaseCurrentPanel"/>).</summary>
    private UIDistributeReward? _shownReward;

    /// <summary>The reward UI whose popup the LIVE conversion was built from. Recorded at convert
    /// time, not read back from <see cref="_shownReward"/>: with the hold in place
    /// <c>_shownReward</c> may be null for a stretch while the float stands, so the old
    /// "did _shownReward change since last tick" test would have missed a process switch across
    /// that gap and left the first popup floated over the second.</summary>
    private UIDistributeReward? _floatedReward;

    // ---- the hold (see WantConverted) --------------------------------------------------------
    private bool _holdArmed;
    private bool _holdLapsed;
    private float _holdSince;
    private bool _holdLogged;

    // ---- the guaranteed escape (see TickBlindWatch) -------------------------------------------
    private float _blindSince = -1f;
    private bool _rescueAsked;

    // Per-distribution-episode latches, cleared when IsDistributing goes false.
    private bool _episodeOpen;
    private bool _floatLogged;
    private bool _blindLogged;
    private float _episodeStart;

    // What the verdict lines report. Written by FloatRoot, read by TickBlindWatch.
    private string _rootHow = "(not resolved)";
    private string _rootPath = "(none)";
    private string _popupName = "(none)";

    // ==========================================================================================
    // WHICH TERM FAILED — the instrument round two exists for.
    //
    // ModBuild 370's verdict line said "a popup reports shown=False", which is a SUMMARY of four
    // completely different facts and cannot tell them apart: the reward UI itself was destroyed,
    // its serialized UIDistributePointsPopup was destroyed, the popup's `window` GameObject was
    // destroyed, or the window is merely SetActive(false). Those have four different causes and
    // three different fixes, and the ambiguity is exactly why this bug is on its second hardware
    // round. From here the loop records one code per reward UI and the verdict names them.
    //
    // Recorded as codes and formatted only at log time, on purpose: ShownPanel runs three times
    // per tick on the map, where the frame budget is the tight one, and a per-tick string build
    // for a line that prints at most twice per distribution is exactly the "instrument that costs
    // more than it measures" this project keeps paying for.
    // ==========================================================================================
    private enum PopupTerm : byte
    {
        /// <summary>Never evaluated this tick (loop stopped earlier).</summary>
        NotReached = 0,
        /// <summary>All four terms passed — this one's popup is up.</summary>
        Shown = 1,
        /// <summary>`ui == null`: the UIDistributeReward component/GameObject was DESTROYED.</summary>
        RewardUiDestroyed = 2,
        /// <summary>`ui.PopUp == null`: the serialized UIDistributePointsPopup was DESTROYED.</summary>
        PopupComponentDestroyed = 3,
        /// <summary>`PopUp.window == null`: the popup's window GameObject was DESTROYED.</summary>
        WindowDestroyed = 4,
        /// <summary>`!window.activeSelf`: alive, and somebody called SetActive(false) on it.</summary>
        WindowInactive = 5,
    }

    private PopupTerm[] _terms = System.Array.Empty<PopupTerm>();
    private int _termCount;

    /// <summary>Why the LAST evaluation of <see cref="ShownPanel"/> produced null before it ever
    /// reached the per-UI loop. Kept separate from the per-UI codes because "there was no manager"
    /// and "the manager has no reward UIs" are failures of the LOOKUP, not of the popup.</summary>
    private string _lookupStop = "(not evaluated)";

    // Scene identity, captured once per episode at the moment a popup IS shown. Every one of these
    // is a fact this file could not read off the ModBuild 371 log and had to reason about instead,
    // and each of them changes the diagnosis:
    //  * IsWindowThePopupGo — if `window` IS the UIDistributePointsPopup's own GameObject then
    //    deactivating or destroying it fires the popup's OnDisable, which calls
    //    controllerArea.Destroy() and MUST print "Unregister area Distribution".
    //  * WindowHasControllerArea — same argument via ControllerInputAreaLocal.OnDisable, which is
    //    the component the game's own "[AREA MANAGER] Register area Distribution" line named.
    // With either of them true, the ABSENCE of an Unregister line in a future log is proof the
    // window was neither deactivated nor destroyed, and the remaining suspect is PopUp == null.
    // With both false, that argument does not hold and the absence proves nothing.
    private string _windowPath = "(not captured)";
    private string _popupGoPath = "(not captured)";
    private string _rewardGoPath = "(not captured)";
    private bool _windowIsPopupGo;
    private bool _windowHasControllerArea;
    private bool _identityCaptured;

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
            _termCount = 0;
            _lookupStop = "no UIDistributeRewardManager singleton (Singleton<>.IsInitialized false, "
                          + "or the instance was destroyed) — there is no distribution to watch";
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
            _termCount = 0;
            _lookupStop = "the manager resolved but NO UIDistributeReward could be found under it, "
                          + "and the one-shot scene sweep found none either — no popup can ever be "
                          + "resolved from here, so the lookup itself is the fault";
            return null;
        }

        // One slot per reward UI, reused. Grown only, never shrunk: the array is at most a handful
        // of entries and a per-tick allocation on the map's frame budget is not worth the bytes.
        if (_terms.Length < uis.Length)
            _terms = new PopupTerm[uis.Length];
        _termCount = uis.Length;
        for (int i = 0; i < uis.Length; i++)
            _terms[i] = PopupTerm.NotReached;
        _lookupStop = "(the lookup succeeded — read the per-reward-UI terms instead)";

        UIDistributeReward? shown = null;
        for (int i = 0; i < uis.Length; i++)
        {
            UIDistributeReward ui = uis[i];
            if (ui == null)
            {
                // Something in the cache was destroyed without the manager changing — rebuild
                // next tick rather than dereferencing a tombstone.
                _terms[i] = PopupTerm.RewardUiDestroyed;
                _termCount = i + 1;
                _rewardUis = null;
                _shownReward = null;
                return null;
            }
            UIDistributePointsPopup pop = ui.PopUp;
            // Read the GameObject directly rather than UIDistributePointsPopup.IsShown, whose body
            // is `window.activeSelf` with no null guard of its own. The four terms are separated
            // here rather than &&-chained because WHICH of them failed is the answer round two is
            // waiting on — the ModBuild 370 verdict collapsed all four into one "shown=False".
            if (pop == null)
            {
                _terms[i] = PopupTerm.PopupComponentDestroyed;
                continue;
            }
            if (pop.window == null)
            {
                _terms[i] = PopupTerm.WindowDestroyed;
                continue;
            }
            if (!pop.window.activeSelf)
            {
                _terms[i] = PopupTerm.WindowInactive;
                continue;
            }
            _terms[i] = PopupTerm.Shown;
            shown = ui;
            break;
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
        CaptureIdentity(shown, window);
        return _rootMemo;
    }

    /// <summary>
    /// WHO IS WHO IN THE SCENE — captured once per episode, on the memo miss (i.e. at most once
    /// per popup, not per frame), and reported in the FLOATED line.
    ///
    /// <para>Every field here is a fact the ModBuild 371 post-mortem needed and could not read.
    /// The log's only handle on this subtree is the game's own
    /// <c>[AREA MANAGER] Register area Distribution (object UI Distribute Items Rewards Popup
    /// (ControllerInputAreaLocal))</c>, and the mod printed <c>window.name</c> as the SAME string —
    /// so it was impossible to tell whether the ControllerInputAreaLocal sits on the window itself
    /// or on a same-named relative. That distinction decides a whole argument: if it IS on the
    /// window, then deactivating or destroying the window fires
    /// <c>ControllerInputAreaLocal.OnDisable</c> → <c>Destroy()</c> →
    /// <c>ControllerInputAreaManager.UnregisterArea</c> → the log line "Unregister area
    /// Distribution", whose ABSENCE then proves the window was neither deactivated nor destroyed
    /// and leaves <c>PopUp == null</c> as the only surviving suspect. If it is NOT on the window,
    /// the absence proves nothing at all. One bool ends that.</para>
    /// </summary>
    private void CaptureIdentity(UIDistributeReward reward, GameObject window)
    {
        if (_identityCaptured)
            return;
        _identityCaptured = true;
        _windowPath = ScenePath(window.transform);
        _popupGoPath = ScenePath(reward.PopUp.transform);
        _rewardGoPath = ScenePath(reward.transform);
        _windowIsPopupGo = ReferenceEquals(window, reward.PopUp.gameObject);
        _windowHasControllerArea = window.GetComponent<ControllerInputAreaLocal>() != null;
    }

    /// <summary>Hierarchy path, root-first, depth-capped. Only ever called from a once-per-episode
    /// capture, so the string build is not on any per-frame path.</summary>
    private static string ScenePath(Transform t)
    {
        const int MaxDepth = 12;
        string path = t.name;
        Transform? p = t.parent;
        for (int i = 0; p != null && i < MaxDepth; p = p.parent, i++)
            path = p.name + "/" + path;
        return p != null ? ".../" + path : path;
    }

    /// <summary>
    /// The per-reward-UI verdict, formatted. Called only from the two once-per-episode verdict
    /// lines and from <see cref="ReleaseDetail"/>, never per frame.
    /// </summary>
    private string DescribeTerms()
    {
        if (_termCount <= 0)
            return "LOOKUP STOPPED BEFORE ANY POPUP WAS TESTED: " + _lookupStop + ".";
        var sb = new System.Text.StringBuilder(160);
        sb.Append("PER REWARD UI (").Append(_termCount).Append("): ");
        for (int i = 0; i < _termCount && i < _terms.Length; i++)
        {
            if (i > 0)
                sb.Append("; ");
            sb.Append('#').Append(i).Append(' ');
            switch (_terms[i])
            {
                case PopupTerm.Shown:
                    sb.Append("SHOWN — all four terms passed (ui alive, PopUp alive, window alive, "
                              + "window.activeSelf true)");
                    break;
                case PopupTerm.RewardUiDestroyed:
                    sb.Append("UI DESTROYED — the UIDistributeReward itself is a Unity tombstone. "
                              + "Something deleted the reward UI; the component cache is rebuilt "
                              + "next tick");
                    break;
                case PopupTerm.PopupComponentDestroyed:
                    sb.Append("POPUP COMPONENT DESTROYED — ui.PopUp is a Unity tombstone, i.e. the "
                              + "UIDistributePointsPopup component or its GameObject has been "
                              + "DESTROYED. It is a [SerializeField], not a Singleton lookup, so it "
                              + "cannot go stale any other way. Look for who deleted it: the mod's "
                              + "own float host cannot (CanvasConversion.DestroyHostSafely refuses "
                              + "to destroy a host still holding game content, ModBuild 361)");
                    break;
                case PopupTerm.WindowDestroyed:
                    sb.Append("WINDOW DESTROYED — PopUp.window is a Unity tombstone: the popup's "
                              + "window GameObject HAS BEEN DELETED, not merely hidden. Unity's "
                              + "fake null cannot say by whom; the candidates are a cascade from a "
                              + "destroyed ancestor and an explicit Destroy. The mod's own host "
                              + "destroy is guarded (HOST DESTROY DEFERRED would be in this log if "
                              + "it had tried)");
                    break;
                case PopupTerm.WindowInactive:
                    sb.Append("WINDOW INACTIVE — alive, and somebody called SetActive(false) on it. "
                              + "activeSelf is LOCAL, so a reparent cannot have done this; the "
                              + "game's own route is UIDistributePointsPopup.Hide(), which also "
                              + "runs controllerArea.Destroy() and therefore MUST have printed "
                              + "'[AREA MANAGER] Unregister area Distribution' just above");
                    break;
                default:
                    sb.Append("NOT REACHED — the loop stopped before this entry");
                    break;
            }
        }
        return sb.ToString();
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

    /// <summary>
    /// THE HOLD (see <see cref="WantConverted"/>). True while the game is provably still waiting on
    /// this distribution AND a panel is already up. Computed once per tick, at the top of
    /// <see cref="Tick"/>, so <see cref="WantConverted"/> — which the base evaluates part-way
    /// through <c>base.Tick()</c> — reads a value taken from the same frame's state.
    ///
    /// <para><b>EVERY WAY THE HOLD ENDS, because a hold that never lets go is the next deadlock:</b></para>
    /// <list type="bullet">
    /// <item><b>The popup comes back.</b> The first term of <see cref="WantConverted"/> passes on
    ///   its own and the hold is irrelevant.</item>
    /// <item><b>The game finishes distributing.</b> <c>IsDistributing</c> goes false the moment the
    ///   last promise resolves (<c>UIDistributeRewardManager.ProcessSP</c>), which is the same
    ///   instant <c>MapChoreographer.WaitDistributionEnds</c> stops waiting. The hold drops, the
    ///   surface releases, the popup is restored to its exact 2D home.</item>
    /// <item><b>The manager goes away</b> — scene load, teardown, a new manager instance. The
    ///   <c>Singleton&lt;&gt;.IsInitialized</c> / Unity-null test at the top of
    ///   <see cref="ShownPanel"/> clears <c>_manager</c>, so the hold cannot survive into a scene
    ///   where the popup no longer exists.</item>
    /// <item><b>The target dies.</b> <see cref="WorldSurface.Tick"/>'s first branch prunes a panel
    ///   whose target is destroyed BEFORE the gate is read; the hold requires <c>Panel != null</c>
    ///   and therefore cannot resurrect anything.</item>
    /// <item><b>The player raises the 2D composite</b> (A/X chord) or turns conversion off. Those
    ///   two terms are outside the hold, deliberately — the hold overrides exactly one term.</item>
    /// <item><b>Module shutdown.</b> <see cref="Shutdown"/> releases unconditionally.</item>
    /// <item><b>The hard cap.</b> <see cref="HoldMaxSeconds"/> of one continuous hold lapses it —
    ///   and the same tick raises the rescue screen, so the lapse never produces "nothing".</item>
    /// </list>
    /// </summary>
    private bool HoldForDistribution =>
        Panel != null && !_holdLapsed && _manager != null && _manager.IsDistributing;

    public override void Tick()
    {
        // A popup switch under a live conversion (one reward's popup hides and the next process's
        // opens inside the same promise chain, with no hidden frame between): the base only
        // converts while Panel == null, so force the release here or the FIRST popup stays floated
        // over a second, invisible one — the DistributePointsSurface lesson, one flow over.
        //
        // ROUND TWO: the test is against the reward the LIVE CONVERSION was built from
        // (_floatedReward), not against the previous tick's _shownReward. With the hold in place a
        // popup may be unresolvable for a stretch while the float stands, and the old
        // "did it change since last tick" comparison steps straight over that gap: A shown → null
        // (held) → B shown reads as "before was null", the `before != null` guard fails, and the
        // first popup would stay floated over the second. Comparing against what is ACTUALLY on
        // screen has no gap to step over.
        RectTransform? now = ShownPanel();
        if (Panel != null && now != null && _floatedReward != null
            && !ReferenceEquals(_shownReward, _floatedReward))
        {
            if (ReleaseCurrentPanel())
            {
                _floatedReward = null;
                VRLog.Info("WorldUI", "DistributeReward: reward process switched under a live "
                                      + "conversion — previous popup released so the shown one can "
                                      + "float in its place.");
            }
        }

        TickHold(now != null);

        bool hadPanel = Panel != null;
        base.Tick();

        // The conversion the base may just have made is keyed to the reward that was shown when it
        // ran; record it here rather than in OnConverted so there is exactly one writer.
        if (Panel == null)
            _floatedReward = null;
        else if (_floatedReward == null && _shownReward != null)
            _floatedReward = _shownReward;

        // THE RELEASE VERDICT, AT A TIER THE HARDWARE LOG ACTUALLY PRINTS. The base class's
        // release marker is VRLog.Info, i.e. the DEBUG tier, i.e. absent from every shipped log —
        // which is precisely how ModBuild 371's release reached the user with no explanation
        // attached. ReleaseDetail() names the failing term on that line for a dev run; this names
        // it for the hardware round, and ONLY on the release that matters: one where the campaign
        // is still gated on this popup.
        if (hadPanel && Panel == null && _manager != null && _manager.IsDistributing)
        {
            bool deliberate = !WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive;
            if (deliberate)
            {
                // HW-VERIFY: the benign half — a release into 2D while the game is still
                // distributing, because the player (or this surface's own escape) asked for the
                // flat composite. Reading this as the deadlock would send the next round chasing
                // the wrong thing, so it is named apart from the alert below.
                VRLog.Note("WorldUI", "DISTRIBUTE FLOAT RELEASED INTO 2D while the game is still "
                                      + "distributing — this is DELIBERATE, not the deadlock: the "
                                      + "2D composite owns the popup now and the player clicks it "
                                      + "there." + ReleaseDetail());
            }
            else
            {
                // HW-VERIFY: THE DEADLOCK ITSELF, IF IT EVER HAPPENS AGAIN. With the hold in place
                // this release is supposed to be unreachable, so every appearance is either the
                // target having been destroyed under us (no hold can help — read the term) or a
                // gate term nobody predicted. It is the single line the next round should grep for.
                VRLog.Alert("WorldUI", "DISTRIBUTE FLOAT RELEASED WHILE STILL DISTRIBUTING — THIS "
                                      + "IS THE 2026-09-03 DEADLOCK REPEATING. The panel the "
                                      + "campaign is gated on has just been handed back with "
                                      + "UIDistributeRewardManager.IsDistributing still true, the "
                                      + "map still locked, and no 2D composite up to catch it. The "
                                      + "hold in WantConverted was supposed to make this "
                                      + "unreachable, so read the failing term and believe it over "
                                      + "any earlier diagnosis." + ReleaseDetail()
                                      + " The guaranteed escape will raise the 2D composite in "
                                      + RescueSeconds.ToString("0") + " s if nothing floats before "
                                      + "then.");
            }
        }

        TickBlindWatch();
    }

    /// <summary>
    /// Arm / disarm the hold and enforce its hard cap. Runs before <c>base.Tick()</c> so the value
    /// <see cref="WantConverted"/> reads inside it is this frame's.
    /// </summary>
    private void TickHold(bool popupShown)
    {
        bool want = !popupShown && HoldForDistribution;
        if (!want)
        {
            if (_holdArmed)
            {
                _holdArmed = false;
                bool wasLogged = _holdLogged;
                _holdLogged = false;
                // Only report an end for a hold that reported its start. The pair must stay a
                // pair: an ENDED line with no HELD line above it reads as a hold nobody armed.
                if (!wasLogged)
                    return;
                // HW-VERIFY: proves the hold LET GO. A hold with no matching end line is the one
                // way this fix could itself become the next deadlock report, so the pair is what
                // the next hardware round reads — never the raise on its own.
                VRLog.Note("WorldUI", "DISTRIBUTE FLOAT HOLD ENDED after "
                                      + (Time.unscaledTime - _holdSince).ToString("0.0")
                                      + " s — reason: "
                                      + (popupShown
                                          ? "the popup reports shown again, so the ordinary gate "
                                            + "carries the float from here"
                                          : _holdLapsed
                                              ? "the " + HoldMaxSeconds.ToString("0")
                                                + " s hard cap lapsed it"
                                              : _manager == null
                                                  ? "the UIDistributeRewardManager is gone (scene "
                                                    + "load or teardown) — the popup cannot exist "
                                                    + "any more"
                                                  : Panel == null
                                                      ? "there is no floated panel left to hold "
                                                        + "(the target was destroyed, or another "
                                                        + "gate term released it)"
                                                      : "the game finished distributing "
                                                        + "(IsDistributing false), which is also "
                                                        + "the moment MapChoreographer."
                                                        + "WaitDistributionEnds stops waiting")
                                      + ". " + DescribeTerms());
            }
            return;
        }

        if (!_holdArmed)
        {
            _holdArmed = true;
            _holdSince = Time.unscaledTime;
        }

        // THE HOLD ENGAGES IMMEDIATELY; ONLY THE LINE WAITS. Between two processes of the same
        // distribution the game legitimately runs popup.Hide() and the next popup.Show() — one
        // resolved promise apart — and if those land on different frames the hold arms for a frame
        // or two, correctly and invisibly. Logging that would put an Alert pair in the log per
        // reward process for a state that is not a fault, and a marker that fires when nothing is
        // wrong is a marker nobody reads. The dwell is well under BlindAlertSeconds, so a genuine
        // failure is still named long before the player notices anything.
        if (!_holdLogged && Time.unscaledTime - _holdSince >= HoldLogSeconds)
        {
            _holdLogged = true;
            // HW-VERIFY: THE ROUND-TWO FIX, FIRING. Its presence means the mod refused to hand the
            // distribute panel back while the campaign was still gated on it — i.e. the exact
            // release that produced "kurz sehe ich ein Fenster das aufgeht und dann sofort wieder
            // verschwindet" did not happen this time. The term text names WHY the popup stopped
            // resolving, which is the diagnosis ModBuild 371 could not deliver.
            VRLog.Alert("WorldUI", "DISTRIBUTE FLOAT HELD: the game says it is still distributing a "
                                  + "reward (UIDistributeRewardManager.IsDistributing) but no popup "
                                  + "resolves any more, so the float is HELD rather than released. "
                                  + "Releasing here is what killed the 2026-09-03 travel event: the "
                                  + "map is locked by AdventureMapUIManager.LockOptionsInteraction "
                                  + "and MapChoreographer.WaitDistributionEnds waits on a promise "
                                  + "only this popup's own confirm button resolves, so handing the "
                                  + "panel back leaves the player with no way forward at all. "
                                  + "WHICH TERM FAILED: " + DescribeTerms()
                                  + " SCENE IDENTITY: window='" + _windowPath
                                  + "', popup component on='" + _popupGoPath
                                  + "', reward UI on='" + _rewardGoPath
                                  + "', window IS the popup's own GameObject=" + _windowIsPopupGo
                                  + ", window carries the ControllerInputAreaLocal="
                                  + _windowHasControllerArea
                                  + ". READ THE IDENTITY LIKE THIS: if either of those two bools is "
                                  + "True, then deactivating or destroying that window MUST have "
                                  + "printed '[AREA MANAGER] Unregister area Distribution' (via "
                                  + "OnDisable -> controllerArea.Destroy), so the ABSENCE of that "
                                  + "game line proves the window is alive and active and the failed "
                                  + "term is PopUp itself. If both are False that argument does not "
                                  + "hold and the game line says nothing either way. The hold ends "
                                  + "on IsDistributing, on the manager going away, on the panel "
                                  + "dying, on the manual screen, or at the "
                                  + HoldMaxSeconds.ToString("0") + " s cap.");
        }

        if (!_holdLapsed && Time.unscaledTime - _holdSince >= HoldMaxSeconds)
        {
            _holdLapsed = true;
            // HW-VERIFY: the hold's own outer bound. If this ever appears, the hold was standing
            // for a minute and a half on a panel the game no longer described as shown, and the
            // escape below takes over in the same tick — the player is handed the 2D composite
            // rather than an indefinitely-held float nobody can prove is drawing anything.
            VRLog.Alert("WorldUI", "DISTRIBUTE FLOAT HOLD LAPSED at the "
                                  + HoldMaxSeconds.ToString("0") + " s hard cap — the float is "
                                  + "released and the guaranteed escape takes over on this same "
                                  + "tick, so the player is never left in front of nothing. A hold "
                                  + "standing this long means the popup never came back: "
                                  + DescribeTerms());
            FlatScreen.RequestRescueScreen(
                "DistributeRewardSurface",
                "The game has been distributing a reward for " + HoldMaxSeconds.ToString("0")
                + " s with no resolvable popup, and the world-space float held for it has now hit "
                + "its hard cap.");
            _rescueAsked = true;
        }
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
            _blindSince = -1f;
            _holdLapsed = false;
            _identityCaptured = false;
            // THE ESCAPE ENDS WITH THE FLOW IT WAS RAISED FOR — nothing else ever clears it, which
            // is what makes it impossible for this surface to strand the player behind a screen it
            // forgot about. `distributing` false covers both endings: the promise chain resolved
            // (IsDistributing goes false in UIDistributeRewardManager.ProcessSP) and the manager
            // went away entirely (ShownPanel cleared _manager on a scene load or teardown).
            if (_rescueAsked)
            {
                _rescueAsked = false;
                FlatScreen.ReleaseRescueScreen(
                    mgr == null
                        ? "the UIDistributeRewardManager is gone — scene load or teardown"
                        : "the game finished distributing (IsDistributing false), so "
                          + "MapChoreographer.WaitDistributionEnds has stopped waiting");
            }
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
            // A panel is up: the player is not staring at nothing, so any standing rescue is over.
            // (It cannot normally be standing here — the rescue sets ManualScreenActive, which
            // releases this surface's float — but the tick order between FlatScreen and the
            // surfaces is not something this file gets to assume, so the release is written
            // level-triggered rather than as a claim about ordering.)
            _blindSince = -1f;
            if (_rescueAsked)
            {
                _rescueAsked = false;
                FlatScreen.ReleaseRescueScreen("the distribute popup is floated in world space "
                                               + "again, so the player has a usable panel without "
                                               + "the 2D composite");
            }
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
                                  + "fault is downstream of the float."
                                  + " SCENE IDENTITY (captured once, and the thing the ModBuild 371 "
                                  + "post-mortem had to guess at): window='" + _windowPath
                                  + "', popup component on='" + _popupGoPath
                                  + "', reward UI on='" + _rewardGoPath
                                  + "', window IS the popup's own GameObject=" + _windowIsPopupGo
                                  + ", window carries the ControllerInputAreaLocal="
                                  + _windowHasControllerArea
                                  + ". EITHER BOOL TRUE means a later deactivate or destroy of that "
                                  + "window MUST print '[AREA MANAGER] Unregister area Distribution' "
                                  + "(UIDistributePointsPopup.OnDisable / ControllerInputAreaLocal."
                                  + "OnDisable both run controllerArea.Destroy), so the absence of "
                                  + "that game line later in this log is PROOF the window is still "
                                  + "alive and active.");
            return;
        }

        // The escape's dwell clock. Started on the first tick of an episode with nothing floated,
        // cleared the moment anything is (above). Separate from the blind alert's clock, which
        // measures from the START of the episode: the alert asks "did the mod ever get it up",
        // the escape asks "is the player looking at nothing RIGHT NOW, and for how long".
        if (_blindSince < 0f)
            _blindSince = Time.unscaledTime;

        if (!_blindLogged && Time.unscaledTime - _episodeStart >= BlindAlertSeconds)
        {
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
                                  + "the standing escape."
                                  // APPENDED, ROUND TWO. The clause above collapsed four different
                                  // facts into one "shown=False" and that is why this bug got a
                                  // second hardware round. These name them apart.
                                  + " WHICH TERM FAILED: " + DescribeTerms()
                                  + " SCENE IDENTITY: window='" + _windowPath
                                  + "', popup component on='" + _popupGoPath
                                  + "', reward UI on='" + _rewardGoPath
                                  + "', window IS the popup's own GameObject=" + _windowIsPopupGo
                                  + ", window carries the ControllerInputAreaLocal="
                                  + _windowHasControllerArea + ".");
        }

        // ==================================================================================
        // THE GUARANTEED ESCAPE. Standing user ruling: a player must ALWAYS end in one of two
        // observable states — a usable panel on screen, or the action having happened. Never
        // "nothing". If the game is still distributing and the mod has had NOTHING in front of
        // him for RescueSeconds, this puts the full 2D composite up through the same latch the
        // manual A/X chord uses, which restores every floated window to the 2D UI first and then
        // draws the game's own screen-space widgets on the screen quad, clickable by laser.
        //
        // WHY IT HAS TO EXIST EVEN THOUGH THE HOLD ABOVE FIXES THE OBSERVED CASE: the hold can
        // only keep a panel that EXISTS. If the popup's GameObject is destroyed, or the float
        // never happened at all (the lookup missed, the conversion refused), holding is a no-op
        // and the player is back in front of a locked map. And on the MAP he cannot rescue
        // himself: FlatScreen.TickManualChord returns immediately unless a scenario board exists,
        // so the chord — the mod's standing universal escape — is unreachable in precisely the
        // situation this class exists for. ModBuild 370's lane declined to build the entry point;
        // that call was overruled on 2026-09-03 after the second identical report.
        //
        // WHAT IT COSTS: while it stands, this surface (and every other) releases its float and
        // the player is looking at the flat composite instead of the 3D map room — including
        // past the map-room gate in FlatScreen.WantVisible, which is a deliberate override
        // documented there. That is a worse VR experience and a strictly better game: he can
        // finish the distribution and the campaign continues.
        //
        // WHAT IT IS NOT: it does not touch game state. No Show, no Hide, no SetActive, no
        // synthesised click, nothing on the wire. The mod raises its own presentation surface;
        // the player presses the game's own button, host-gated by the game's own code.
        // ==================================================================================
        if (_rescueAsked || Time.unscaledTime - _blindSince < RescueSeconds)
            return;
        _rescueAsked = true;
        FlatScreen.RequestRescueScreen(
            "DistributeRewardSurface",
            "The game has been distributing a reward for " + RescueSeconds.ToString("0")
            + " s (UIDistributeRewardManager.IsDistributing, with the map locked by "
            + "AdventureMapUIManager.LockOptionsInteraction and MapChoreographer."
            + "WaitDistributionEnds waiting on a promise only this popup's confirm button "
            + "resolves) and the mod has had NOTHING in front of the player for that whole time. "
            + DescribeTerms());
    }

    /// <summary>
    /// Names the failing gate term on the base class's release marker (see
    /// <see cref="FloatingDecisionSurface.ReleaseDetail"/>). ModBuild 371's log carried that
    /// marker one frame after a successful float and said nothing about WHY, which is the reason
    /// this bug reached a second hardware round.
    /// </summary>
    protected override string ReleaseDetail()
    {
        bool distributing = _manager != null && _manager.IsDistributing;
        return " FAILING TERM: "
               + (!WorldUIConfig.ConversionActive
                   ? "WorldUIConfig.ConversionActive went FALSE (the player turned world conversion "
                     + "off) — this is a setting, not a fault"
                   : FlatScreen.ManualScreenActive
                       ? "the 2D composite is up (manual A/X chord or the guaranteed escape), so "
                         + "the popup was deliberately restored to the flat UI where the composite "
                         + "draws it — this is the standing escape working, not a fault"
                       : _shownReward == null
                           ? "ShownPanel() returned null — " + DescribeTerms()
                           : "none of the gate terms is false, so this release came from the "
                             + "framework pruning a panel whose TARGET WAS DESTROYED "
                             + "(WorldSurface.Tick's first branch, ConvertedPanel.IsAlive)")
               + " GAME STILL DISTRIBUTING=" + distributing
               + (distributing
                   ? " <- IF THIS IS TRUE THE HOLD FAILED. WantConverted keeps the float while "
                     + "IsDistributing is true, so a release here means one of the OTHER gate "
                     + "terms went false, or the target died and no hold can help."
                   : " (the campaign is no longer gated on this popup, so releasing is correct).");
    }

    public override void Shutdown()
    {
        base.Shutdown();
        // The rescue latch is static and outlives this object; a teardown must never leave the
        // screen forced up on nobody's behalf.
        if (_rescueAsked)
        {
            _rescueAsked = false;
            FlatScreen.ReleaseRescueScreen("the WorldUI module is shutting down");
        }
        _manager = null;
        _rewardUis = null;
        _shownReward = null;
        _floatedReward = null;
        _sweepTried = false;
        _episodeOpen = false;
        _floatLogged = false;
        _blindLogged = false;
        _blindSince = -1f;
        _holdArmed = false;
        _holdLapsed = false;
        _holdLogged = false;
        _identityCaptured = false;
        _termCount = 0;
        _rootMemo = null;
        _rootMemoFor = null;
        _rootMemoWindow = null;
    }
}
