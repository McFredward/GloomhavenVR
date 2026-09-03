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

    /// <summary>
    /// THE GRAB BAR — one per live conversion, for EVERY subclass of this family.
    ///
    /// <para><b>USER, 2026-09-03, verbatim:</b> <i>"Ich sehe nun das Fenster in dem ausgewählt wird,
    /// allerdings ohne Greifbalken. Ich will das auch das wie jedes andere Fenster auch behandelt
    /// wird und das Fenster normal verschiebbar ist wie alle anderen Fenster auch."</i> He met the
    /// map-side reward popup (<see cref="DistributeRewardSurface"/>), which ModBuild 370-373 fought
    /// onto the screen and which then could not be moved.</para>
    ///
    /// <para><b>THE BAR IS ON THE BASE CLASS, SO ALL THREE SUBCLASSES GET IT, AND THAT IS THE
    /// EVIDENCE-BACKED SCOPE RATHER THAN A CONVENIENCE.</b> Every member of this family is placed by
    /// the SAME <see cref="Place"/> — one metre in front of the HMD, once per conversion, at
    /// <see cref="FloatScaleFactor"/> — so none of them can collide with a bar that another one
    /// clears, and none of them has a second way to be moved today (<c>Place</c> is latched by
    /// <c>_placed</c> and nothing else writes their host pose). Per subclass:
    /// <list type="bullet">
    /// <item><see cref="DoomPickerSurface"/> — floats at the HMD mid-scenario; no other mover; gets
    /// the bar.</item>
    /// <item><see cref="DistributePointsSurface"/> — same pose, same absence of any other mover;
    /// gets the bar. Its select↔assign popup switch force-releases the conversion, and the handle
    /// follows that: <see cref="OnConverted"/> destroys the old holder before the new panel is
    /// placed, so a switch cannot leave a rod behind with no panel on it.</item>
    /// <item><see cref="DistributeRewardSurface"/> — the reported one; gets the bar.</item>
    /// </list>
    /// NONE was left out. There is no member of this family that is docked, tray-mounted or
    /// otherwise already movable, so there was nothing to exempt.</para>
    ///
    /// <para><b>AND IT CANNOT COST THE PANEL.</b> The handle writes mod-owned transforms and the
    /// host's pose/scale — which <see cref="Place"/> already wrote before it existed — and nothing
    /// else. It never releases a conversion, never touches a <c>WantConverted</c> term, and in
    /// particular never touches <see cref="DistributeRewardSurface"/>'s distribution hold. See
    /// <see cref="SurfaceGrabBar"/> for the full argument, including why it is not
    /// <c>GrabbableModal</c> and why it has no close X.</para>
    /// </summary>
    private SurfaceGrabBar? _grab;

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
        // A FRESH CONVERSION NEVER INHERITS THE OLD HANDLE. Two paths reach a new panel without the
        // release branch below ever running: the framework pruning a dead target and re-converting
        // in the same tick (WorldSurface.Tick's first branch), and a subclass calling
        // ReleaseCurrentPanel() and falling straight into base.Tick() (the select↔assign popup
        // switch). Both would otherwise leave the previous holder — a SCENE-ROOT tree that no host
        // teardown can reach — standing in the room as a rod with no window on it, which is the
        // artefact the user photographed three of for the modal windows
        // (.planning/debug/leeres_fenster2.jpg). Destroying here makes "one live conversion, one
        // handle" true by construction instead of by every caller remembering.
        DestroyGrab();
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
            // The handle goes with the panel. It is a scene-root tree, so nothing about releasing
            // the conversion could reach it — see OnConverted for the other half of this rule.
            DestroyGrab();
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
    /// an approaching fingertip.
    ///
    /// <para>THE ONCE-ONLY LATCH IS NOW ALSO WHAT MAKES THE PANEL MOVABLE. From the frame the
    /// handle exists, the host's pose is a COPY OF THE GRAB FRAME (<see cref="SurfaceGrabBar.Tick"/>)
    /// rather than of anything computed here, and <c>_placed</c> guarantees this method never
    /// re-derives an HMD pose over the top of it. So there is exactly one writer of the host pose at
    /// any moment: this method until the handle is built, the handle for ever after — no write war,
    /// and no path by which a moved panel can be yanked back ([[dont-win-a-write-war]]).</para></summary>
    protected override void Place()
    {
        if (Panel == null)
            return;
        if (!_placed)
        {
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
            // BUILT AFTER THE PLACE, NEVER BEFORE: SurfaceGrabBar.Build seeds its frame from the
            // host's CURRENT world pose, so seeding it before the host was placed would put the
            // handle at the origin and drag the panel there on the first follow tick.
            _grab = new SurfaceGrabBar();
            _grab.Build(Panel, FloatScaleFactor, scale, Name);
        }
        _grab?.Tick();
    }

    /// <summary>
    /// Drop the mod-owned handle. Idempotent, and the ONLY place this family destroys one — three
    /// callers (a fresh conversion, a release, a shutdown) so that no transition has to remember.
    /// </summary>
    private void DestroyGrab()
    {
        if (_grab == null)
            return;
        _grab.Destroy();
        _grab = null;
    }

    /// <summary>
    /// A teardown must take the handle with it. The holder is a scene-root tree with a
    /// MonoBehaviour on it, so a surface that shut down without this would leave a rod ticking in
    /// the room against a panel that no longer exists.
    /// </summary>
    public override void Shutdown()
    {
        DestroyGrab();
        _placed = false;
        base.Shutdown();
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
/// active. <c>Hide()</c> calls <c>controllerArea.Destroy()</c>, which WOULD have printed the game's
/// unregister line — but only under a condition nobody checked at the time, and that string appears
/// NOWHERE in the log. <b>ModBuild 375:</b> that second clause is NOT the evidence it was read as —
/// see <c>AreaLineCaveat</c>, which carries the falsification and the four ways
/// <c>ControllerInputAreaManager.UnregisterArea</c> runs silently. Nothing in this block's
/// conclusion rests on it: the mod never saw this popup, which the ABSENCE of every mod-side line
/// naming it proves on its own. The run then continues for another 800
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
    /// <para><b>ModBuild 375 — THE PARAGRAPH ABOVE CONTAINS ONE INFERENCE THAT IS NOT SAFE, AND IT
    /// IS RECORDED HERE RATHER THAN EDITED OUT.</b> "The game did NOT hide the popup" was read off
    /// the absence of the unregister line, and ModBuild 373's log proves that absence is not
    /// evidence: <c>ControllerInputAreaManager.UnregisterArea</c> logs only inside
    /// <c>if (m_AvailableAreas.Remove(area))</c>, so a <c>Destroy()</c> on an area the manager no
    /// longer holds — or with the manager gone, or with no <c>OnDisable</c> sent at all — runs
    /// silently. See <c>AreaLineCaveat</c> for the four routes and for the suspicion that was
    /// checked and rejected. The CONCLUSION of this block still stands on its own evidence, because
    /// it never needed that inference: the <c>DISTRIBUTE POPUP NOT FLOATED</c> line reports the
    /// other three gate terms as still true, so the release was the mod's, whether or not the game
    /// had also hidden the popup.</para>
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

    // ==========================================================================================
    // ModBuild 378 — THE HARD CAP IS GONE. THE HOLD ENDS ON STATE, NEVER ON ELAPSED TIME.
    //
    // USER RULING, 2026-09-03, verbatim and standing: "Für was hast du überhaupt ein Zeit-Limit in
    // den Fenstern eingebaut? Ist das Fenster da will ich nicht, dass ein User sich beeilen muss -
    // ich will gar keine Zeitlimits dieser Art." ("Why did you build a time limit into the windows
    // at all? If the window is there I do not want a user to have to hurry — I do not want any
    // time limits of this kind at all.")
    //
    // WHAT USED TO BE HERE: HoldMaxSeconds = 90f, an outer bound on one continuous hold, and with
    // it the DISTRIBUTE FLOAT HOLD LAPSED marker, the DISTRIBUTE HOLD CAP VETOED marker and the
    // CAP VETOES THIS HOLD clause. They are DELIBERATELY REMOVED, not reworded and not left in the
    // file as text that can never print — a marker kept alive with no reachable call site is its
    // own trap. The removal is recorded here and in the round's report so the surface baseline
    // carries a reason rather than a mystery.
    //
    // WHY REMOVING IT DOES NOT REOPEN THE DEADLOCK. The cap's stated job was "the player is never
    // left in front of nothing". That job is now done directly, and by a strictly better test:
    // TickBlindWatch escalates a floated panel that DRAWS NOTHING for RescueSeconds, and it does
    // so regardless of the hold. The cap was a proxy that could not tell a stuck flow from a slow
    // player; the darkness branch measures the thing the guarantee is actually about. ModBuild
    // 377's own hardware log is the demonstration: the cap never came due (0 vetoes, 0 lapses) and
    // the flow ended cleanly on state — "DISTRIBUTE FLOAT HOLD ENDED after 38.5 s — reason: the
    // game finished distributing (IsDistributing false)".
    //
    // AND THE HOLD STILL CANNOT STAND FOR EVER, which is this file's own rule. Every terminator is
    // a STATE term, and they are enumerated on HoldForDistribution below. The one case the clock
    // could catch and no state term can is named honestly in this round's report rather than
    // silently keeping a timer: a panel that is drawn and interactive-looking but whose promise is
    // functionally dead. The player is looking at a real window there — which is exactly the
    // situation the ruling says must not be interrupted — so the trade is deliberate.
    // ==========================================================================================

    /// <summary>Dwell before a hold is REPORTED (it engages instantly — see
    /// <see cref="TickHold"/>). Long enough that the legitimate one-or-two-frame gap between two
    /// reward processes of the same distribution stays out of the log, short enough that a real
    /// failure is named well before <see cref="BlindAlertSeconds"/>.</summary>
    private const float HoldLogSeconds = 0.5f;

    // ==========================================================================================
    // ModBuild 376 — THE LADDER NOW CONSULTS THE MOD'S OWN PANEL, BECAUSE ITS PREMISE WAS FALSE.
    //
    // USER, 2026-09-03, verbatim: "Ich bin einmal zum Desktop gewechselt, hab also das Spiel
    // minimiert während das Entscheidungsfenster offen ist, und bin dann wieder zurück, dann hing
    // das Entscheidungsfenster plötzlich an der Kopfbewegung und es war kein Fenster mehr wie
    // zuvor! ... Zuerst hatte es funktioniert - finde was das ausgelöst hat und fix das."
    //
    // WHAT ACTUALLY HAPPENED, read off .planning/debug/LogOutput.log (ModBuild 375):
    //   :2367-:2381  the popup converts, floats, and the fit measures DRAWN CONTENT 416x464 px
    //                from 30 visible graphic(s) — a real panel, really on the screen.
    //   :2385        the hold arms: the game is still distributing, no popup resolves any more.
    //   :2390-:2416  the PLAYER GRABS AND CARRIES THAT PANEL, twice, and re-faces it.
    //   :2689        90 s later the (now removed) hard cap lapses. He had alt-tabbed — the
    //                heartbeat at :2688 reads "devices=1 L=invalid R=invalid" — so the wall clock
    //                ran out while he was away, which is what "TRIGGERED it" means here. The
    //                ALT-TAB IS NOT THE MECHANISM; the wall clock is. The same flow would have
    //                lapsed identically if he had simply read the reward text for 90 seconds.
    //   :2690-:2695  the cap releases the float, the rescue screen is raised, and the mod's own
    //                deadlock alert fires. The head-locked 2D composite he described IS the
    //                rescue screen (:2712 onward, FlatScreen quad tracking the head camera).
    //
    // SO THE ESCALATION MADE A WORKING SITUATION BROKEN. Its own log text states the premise it
    // was acting on — "so the player is never left in front of nothing" and "the mod has had
    // NOTHING in front of the player for that whole time" — and in this log that premise is
    // measurably false, in the mod's own instruments, in the same run. The cap's own comment
    // even names the gap: it lapses "rather than an indefinitely-held float NOBODY CAN PROVE IS
    // DRAWING ANYTHING". Now somebody can, so the ladder asks before it escalates.
    //
    // THE TERM IS DRAWN CONTENT, NOT EXISTENCE, and it is the fit's own verdict rather than a
    // fifth liveness test — CanvasConversion.TryMeasureDrawnContent, the same per-graphic
    // visibility rule the HIT RECT and Host rect fit lines above are printed from, so the number
    // this veto acts on is the number the log already shows. It is documented non-writing ("no
    // entry is created, no cadence is disturbed, no rect is committed"), which is what lets a
    // gate call it: an instrument that writes cannot also be a gate term.
    //
    // WHY NOT ModalFallback's EMPTY WINDOW HIDDEN machinery, which answers the same question for
    // the modal family: it is keyed on ModalFallback's private WindowPanel records, and these
    // surfaces are not UIWindows and never enter that registry — IsDormantPanel(Panel) is
    // permanently false for every panel this file owns, so reusing it here would be a term that
    // reads clean on a dark panel. Its per-graphic verdict is not the fit's either. What IS
    // reused from it is the SHAPE: an arming grace, a strided re-measure, and a dwell.
    // ==========================================================================================

    /// <summary>How often the drawn-content walk runs while a panel is floated. One subtree walk
    /// per this interval (~85 us for a 251-transform window), not per frame — this surface ticks
    /// on the map, where the frame budget is the tight one.</summary>
    private const float DrawnCheckSeconds = 0.5f;

    /// <summary>How long a positive reading may still hold the veto open. A veto that outlived its
    /// measurement would be a latch, and a latch on this gate is the next deadlock: past this the
    /// verdict is stale and counts as NOT drawing, so a panel that goes dark loses the veto within
    /// one interval plus this.</summary>
    private const float DrawnStaleSeconds = 2f;

    /// <summary>Arming grace after a conversion, before an unmeasured panel counts as dark. A
    /// freshly converted panel has not been laid out or measured yet, and calling that "empty"
    /// would raise the rescue screen on every healthy float. Matches the modal family's
    /// LivenessGraceSeconds so the two rules cannot disagree about what "too early to judge"
    /// means.</summary>
    private const float DrawnGraceSeconds = 1.5f;

    private UIDistributeRewardManager? _manager;
    private UIDistributeReward[]? _rewardUis;
    private bool _sweepTried;

    /// <summary>
    /// EVERY reward UI this manager has ever handed us, minus the destroyed ones — the set
    /// <see cref="ResolveRewardUis"/> actually answers from.
    ///
    /// <para><b>ModBuild 378. WHY A UNION AND NOT JUST THE SCAN.</b> The ModBuild 377 hardware log
    /// proves the scan LOSES the popup the mod has on screen. At float time the census recorded
    /// <c>RESOLVED SET (2)</c> — <c>#0 'UI Distribute Gold Rewards Popup'</c> (window inactive) and
    /// <c>#1 'UI Distribute Items Rewards Popup'</c> (window active, the one that was floated). By
    /// the tick that armed the hold the term list read <c>PER REWARD UI (1)</c>, and
    /// <c>_termCount</c> is assigned from <c>uis.Length</c>, so the set had shrunk to one — and the
    /// survivor is <c>#0</c>, the GOLD popup, which is inactive. So the failing term
    /// "WINDOW INACTIVE" was computed over a popup the player was never shown, while the Items
    /// popup sat drawn and grabbable a few centimetres in front of his face.</para>
    ///
    /// <para>THAT IS THE WHOLE 2026-09-03 HOLD. The hold was not covering for a game that had
    /// hidden its popup; it was covering for this lookup losing track of it.</para>
    ///
    /// <para>WHAT IS PROVEN AND WHAT IS INFERRED, kept apart on purpose. PROVEN: the resolved set
    /// shrank from two to one within one episode, and the entry that survived is not the one that
    /// was floated. INFERRED (and NOT relied on by this fix): that it shrank because the mod's own
    /// conversion reparents the floated subtree out from under the manager, so a manager-scoped
    /// <c>GetComponentsInChildren</c> can no longer reach it — the census printed the reward UI's
    /// NAME but not its GameObject id, so "the reward UI rides on the reparented object" is a
    /// same-name inference, which is exactly the trap this round corrected on the FLOAT IDENTITY
    /// clause. The corrected clause now prints that GameObject id, so the next log settles it.</para>
    ///
    /// <para><b>THE REMEDY DOES NOT DEPEND ON THE INFERENCE.</b> Whatever removed the entry from
    /// the manager's subtree, a reward UI that is ALIVE must not stop being resolvable merely
    /// because a rescan no longer reaches it. The union drops entries on exactly one condition —
    /// Unity-null, i.e. actually destroyed — which is a fact about the object rather than about
    /// where it currently hangs.</para>
    /// </summary>
    private readonly System.Collections.Generic.List<UIDistributeReward> _knownRewardUis = new();

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
    private float _holdSince;
    private bool _holdLogged;

    // ---- the guaranteed escape (see TickBlindWatch) -------------------------------------------
    private float _blindSince = -1f;
    private bool _rescueAsked;

    // ---- is the mod's own panel actually drawing (see TickDrawnWatch) -------------------------
    // Measured ONCE per tick, at a cadence, and read by BOTH the cap in TickHold and the escape
    // in TickBlindWatch — one value per tick on purpose, so the two cannot disagree inside the
    // same frame and tear each other's decision down (which is exactly what ModBuild 375 did:
    // :2690 raised the rescue and :2691 released it again in the same tick).
    private float _drawnNextCheck;
    private float _drawnLastTrueAt = -1f;
    private float _drawnConvertedAt = -1f;
    private int _drawnGraphics;
    private int _drawnWalks;
    private bool _drawnVerdict;
    private string _drawnWhy = "(not measured yet)";
    private float _darkSince = -1f;
    private bool _darkLogged;

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
    //    controllerArea.Destroy().
    //  * WindowHasControllerArea — same argument via ControllerInputAreaLocal.OnDisable, which is
    //    the component the game's own "[AREA MANAGER] Register area Distribution" line named.
    //
    // ModBuild 375 — WHAT THESE TWO BOOLS ARE WORTH, CORRECTED. Through ModBuild 373 this block
    // ended: "With either of them true, the ABSENCE of an Unregister line in a future log is proof
    // the window was neither deactivated nor destroyed, and the remaining suspect is PopUp == null."
    // The ModBuild 373 hardware log falsified exactly that: both bools True, the term split naming
    // the window inactive, and the game's unregister line absent. The two bools decide only whether
    // controllerArea.Destroy() would RUN; whether that run LOGS anything is a separate condition
    // living in ControllerInputAreaManager.UnregisterArea, and it is often false. The whole reading
    // is on AreaLineCaveat, which is the text these two bools are now printed with — so the log can
    // never again carry the conclusion without the caveat that bounds it.
    private string _windowPath = "(not captured)";
    private string _popupGoPath = "(not captured)";
    private string _rewardGoPath = "(not captured)";
    private bool _windowIsPopupGo;
    private bool _windowHasControllerArea;
    private bool _identityCaptured;

    // ==========================================================================================
    // ModBuild 376 — WHY THE THREE PATHS ABOVE COULD NOT SETTLE FIX 2, AND WHAT REPLACES THEM.
    //
    // In the ModBuild 375 log all three read the SAME string:
    //   window / popup component on / reward UI on
    //     = 'Campaign Canvas/UI Distribute Rewards Window/UI Distribute Items Rewards Popup'
    // and it is tempting to conclude "so they are one GameObject, therefore activeSelf==false and
    // 'drawing 30 graphics' are a contradiction about one object". THAT CONCLUSION DOES NOT
    // FOLLOW. A HIERARCHY PATH IS NOT AN IDENTITY: two siblings may carry the same name, and
    // ScenePath() would print them identically. The instrument reports paths, so it cannot tell
    // one object from a same-named neighbour, and every reading built on it inherits that gap.
    //
    // AND THERE IS A CONCRETE REASON TO EXPECT MORE THAN ONE. UIDistributeRewardManager holds
    // `[SerializeField] private List<DistributeRewardProcess> processes` and chains
    // `process.Process(rewards)` over ALL of them (UIDistributeRewardManager.Distribute). The
    // decompile carries five subclasses — DistributeItemsProcess, DistributeGoldProcess,
    // DistributeGoldBagProcess, DistributeConditionsProcess, DistributeAttackModifierProcess —
    // and each of them dereferences its own `processUI.PopUp`, i.e. each has its own
    // UIDistributeReward. The scoped lookup here resolved exactly ONE ("reward UIs resolved=1,
    // scene sweep tried=False", LogOutput.log:2695). One of two things is therefore true, and
    // the log cannot currently say which:
    //   (a) the other process UIs live OUTSIDE the manager's subtree, so ResolveRewardUis is
    //       structurally blind to them and may be watching a reward UI the game is not using; or
    //   (b) all five share one UIDistributeReward, and the single resolved instance really is the
    //       one on screen — in which case the contradiction is about ONE object and the cause is
    //       downstream of the resolver entirely.
    // Those have different fixes, so this round ships the measurement that separates them and NO
    // remedy: three rounds have already been spent writing guesses into log lines on this defect.
    //
    // WHAT IS RECORDED, and why each field is load-bearing rather than decoration:
    //   * instance ids for the reward UI, the popup component, the popup's GameObject and the
    //     window — GetInstanceID() is the only identity Unity offers that survives a reparent and
    //     cannot be confused with a same-named neighbour;
    //   * the same for the mod's OWN live float target, plus a containment test in both
    //     directions. If the resolved window and the float target are the SAME id while one reads
    //     activeInHierarchy=False and the other is measured drawing, that is impossible and the
    //     resolver is not the fault; if they are DIFFERENT ids, the resolver is watching the
    //     wrong object and (a) is proven on the spot.
    // ==========================================================================================
    private int _rewardId;
    private int _popupId;
    private int _popupGoId;
    private int _windowId;
    private string _resolvedCensus = "(not captured)";

    // ==========================================================================================
    // ModBuild 375 — THE CLAUSE THAT WAS WRONG, AND THE INSTRUMENT THAT POISONED ITS OWN GREP.
    //
    // Through ModBuild 373 both verdict lines below ended with an argument of this shape: "either
    // identity bool True ⇒ deactivating or destroying that window MUST print '<the game's
    // unregister line>', so the ABSENCE of that game line proves the window is alive and active."
    // Two things were wrong with it, and the hardware log falsified both in one run.
    //
    // 1. THE "MUST" IS FALSE. In the ModBuild 373 log (Player.log :5504-:5527) both bools ARE True,
    //    the term split names the popup window inactive, and the game's unregister line occurs ZERO
    //    times. Read out of decompiled/GH.Runtime/ControllerInputAreaManager.cs, the reason is that
    //    the line is printed INSIDE `if (m_AvailableAreas.Remove(area))` — it is conditional on the
    //    area still being IN the manager's list at that moment, which is a fact about the manager
    //    and not about the window. Four silent routes, all read from source rather than guessed:
    //      (a) ControllerInputAreaLocal.Destroy() is reached from BOTH
    //          UIDistributePointsPopup.OnDisable and ControllerInputAreaLocal.OnDisable, and both
    //          go through that same Remove — so at most the FIRST of the pair can ever print, and
    //          every later Destroy on the same area is silent;
    //      (b) ControllerInputAreaManager.UnregisterAllAreas() empties the list with no line at
    //          all, after which every Destroy is silent;
    //      (c) both call sites are `Instance?.` — a destroyed manager unregisters nothing and
    //          prints nothing;
    //      (d) Unity sends no OnDisable to a component that is not already activeInHierarchy, so
    //          activeSelf can be written false with no message and therefore no line.
    //    CHECKED AND REJECTED — "a Destroy() on an already-DISABLED area unregisters nothing".
    //    ControllerInputAreaLocal.DisableGroup() only flips `isEnabled` and calls SetUnfocused();
    //    it never touches m_AvailableAreas. The ModBuild 373 log proves it directly: the game's
    //    disable line for this area is printed ONE LINE AFTER the register (:5504-:5505), and that
    //    branch of RegisterArea runs with the area already added to the list.
    //
    // 2. IT WAS A HIT FOR ITS OWN SEARCH. Both lines told the reader to grep the log for the game's
    //    unregister phrase and to read its ABSENCE as proof — while printing that phrase verbatim.
    //    `grep -c` for it on the ModBuild 373 log returns 2, and both hits are these two lines
    //    quoting themselves. An instrument that contaminates the search it prescribes is worse than
    //    no instrument: the first reader of that log took the two hits for the game's.
    //    So the phrase is DELIBERATELY NOT REPRODUCED in the emitted text below. The channel is
    //    named (the game brackets these lines with [AREA MANAGER]) and the event is named in words,
    //    which is greppable enough to find and cannot be a false positive for the phrase itself.
    //
    // One string, three consumers (both verdict lines and — through DescribeTerms — the release
    // detail), so the correction cannot be applied to two of them and forgotten on the third.
    // ==========================================================================================
    private const string AreaLineCaveat =
        "WHAT THE GAME'S AREA-MANAGER EVIDENCE PROVES: LESS THAN THIS LINE USED TO CLAIM. Through "
        + "ModBuild 373 it said that with either identity bool True, deactivating or destroying "
        + "that window MUST print the game's unregister line, so the absence of that line proved "
        + "the window was alive and active. The ModBuild 373 log falsified it: both bools True, the "
        + "term split naming the window inactive, and ZERO occurrences of that game line. THE "
        + "REASON, from ControllerInputAreaManager: the line is printed inside "
        + "'if (m_AvailableAreas.Remove(area))', i.e. only when a Destroy() finds the area STILL "
        + "REGISTERED — a fact about the manager, not about the window. It is silent when (a) "
        + "Destroy() already ran once (it is reached from UIDistributePointsPopup.OnDisable AND "
        + "from ControllerInputAreaLocal.OnDisable, so only the FIRST of the pair can print), (b) "
        + "UnregisterAllAreas() emptied the list, (c) the manager singleton is gone (both call "
        + "sites are null-conditional), or (d) no OnDisable ran at all, because Unity does not send "
        + "it to a component that was not already activeInHierarchy. NOT the cause, checked in the "
        + "decompile and rejected: 'the area was already disabled' — DisableGroup() only flips "
        + "isEnabled and unfocuses, and this same flow prints the game's DISABLE-AREA line one line "
        + "after the register, i.e. with the area registered AND disabled at once. HOW TO SEARCH: "
        + "the game brackets these events with [AREA MANAGER] and its area id here is Distribution "
        + "— read that channel's events in order rather than grepping for the unregister phrase. "
        + "THIS LINE DELIBERATELY DOES NOT SPELL THAT PHRASE OUT: through ModBuild 373 it did, and "
        + "a count of it on that log returned 2 hits that were both this instrument quoting itself.";

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
            // A new manager is a new scene's worth of reward UIs; the union must not carry the
            // old scene's objects across, and Unity-null alone would not catch them all promptly.
            _knownRewardUis.Clear();
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

        // ModBuild 376 — the identities the paths above cannot carry. All four are plain
        // GetInstanceID() reads on objects already dereferenced two lines up; nothing is searched
        // for and nothing is written to the game.
        _rewardId = reward.GetInstanceID();
        _popupId = reward.PopUp.GetInstanceID();
        _popupGoId = reward.PopUp.gameObject.GetInstanceID();
        _windowId = window.GetInstanceID();
        _resolvedCensus = DescribeResolvedSet();
    }

    /// <summary>
    /// EVERY reward UI the lookup resolved, by identity — not just the one that happened to be
    /// shown. This is the half of Fix 2 that decides whether <see cref="ResolveRewardUis"/> is
    /// watching the right object at all: the manager chains a LIST of processes and each of them
    /// owns a <c>processUI.PopUp</c>, so a census of one entry is itself the finding.
    ///
    /// <para>READ-ONLY AND ALLOCATION-BOUNDED. It walks the array this class already holds — no
    /// scene search, no <c>FindObjectsOfType</c>, no component lookup — and it is called only
    /// from <see cref="CaptureIdentity"/>, i.e. at most once per distribution episode. Its result
    /// is a string built once and printed by the verdict lines; nothing reads it as state.</para>
    /// </summary>
    private string DescribeResolvedSet()
    {
        UIDistributeReward[]? uis = _rewardUis;
        if (uis == null || uis.Length == 0)
            return "RESOLVED SET: none (the lookup held no reward UIs at capture time).";
        var sb = new System.Text.StringBuilder(160);
        sb.Append("RESOLVED SET (").Append(uis.Length).Append("): ");
        for (int i = 0; i < uis.Length; i++)
        {
            if (i > 0)
                sb.Append("; ");
            sb.Append('#').Append(i).Append(' ');
            UIDistributeReward ui = uis[i];
            if (ui == null)
            {
                sb.Append("<destroyed>");
                continue;
            }
            sb.Append("rewardUI id=").Append(ui.GetInstanceID())
              .Append(" on '").Append(ui.gameObject.name).Append('\'');
            UIDistributePointsPopup pop = ui.PopUp;
            if (pop == null)
            {
                sb.Append(", PopUp=<destroyed>");
                continue;
            }
            sb.Append(", PopUp id=").Append(pop.GetInstanceID())
              .Append(" on go id=").Append(pop.gameObject.GetInstanceID());
            GameObject w = pop.window;
            if (w == null)
            {
                sb.Append(", window=<destroyed>");
                continue;
            }
            sb.Append(", window go id=").Append(w.GetInstanceID())
              .Append(" activeSelf=").Append(w.activeSelf)
              .Append(" activeInHierarchy=").Append(w.activeInHierarchy);
        }
        return sb.ToString();
    }

    /// <summary>
    /// THE ONE COMPARISON THE PREVIOUS THREE ROUNDS COULD NOT MAKE: is the object the resolver is
    /// judging the same object the mod has floated?
    ///
    /// <para><b>ModBuild 378 — THE ModBuild 377 VERSION OF THIS CLAUSE MEASURED THE WRONG OBJECT,
    /// AND ITS OWN "WHAT EACH ANSWER MEANS" TEXT THEN POINTED THE READER AT A CONCLUSION THE
    /// MEASUREMENT COULD NOT SUPPORT.</b> It compared the float target against
    /// <c>_rootMemoWindow</c> — the window captured on the last SUCCESSFUL resolve — and called
    /// that "the window the resolver judges". It is not: the failing term is computed over
    /// <c>_rewardUis[i].PopUp.window</c> on THIS tick, and the memo is by construction a window
    /// that was active when it was captured. So the clause reported <c>SAME OBJECT=True</c> and
    /// <c>activeSelf=True</c> about an object whose activeness was never in doubt, while the term
    /// beside it in the same line reported <c>WINDOW INACTIVE</c> about a different object — two
    /// readings of two objects presented as one comparison. This is the repo's recurring failure
    /// class (an instrument modelling a DIFFERENT term from the one it is offered as evidence
    /// for), and it is what ModBuild 375 was spent correcting on the neighbouring line.</para>
    ///
    /// <para><b>THE TWO CONTAINMENT BOOLS WERE A TAUTOLOGY.</b> <see cref="IsDescendantOf"/>
    /// returns true for a node against itself, so "window is INSIDE the float target" and "float
    /// target is INSIDE the window" were both necessarily true whenever <c>SAME OBJECT</c> was
    /// true. They are replaced by ONE mutually-exclusive relation, which cannot print a fact the
    /// identity bool already carried.</para>
    ///
    /// <para>WHAT IT READS NOW: the LIVE resolved set, per entry, each entry's own
    /// <c>PopUp.window</c> — the exact object the term tested — with its activeness read at the
    /// same moment as the term's. The memo is still printed, because it is genuinely useful, but
    /// labelled as the memo and never as "the window the resolver judges"; and the bool that
    /// decides everything is whether the memo is STILL IN the live set.</para>
    ///
    /// <para>Read-only: reference comparisons, a bounded parent walk, and Unity-null tests on
    /// objects this class already holds. Nothing is searched for and nothing is written.</para>
    /// </summary>
    private string DescribeFloatIdentity()
    {
        ConvertedPanel? panel = Panel;
        if (panel == null || !panel.IsAlive || panel.Target == null)
            return "FLOAT IDENTITY: no live conversion, so there is nothing to compare the "
                   + "resolved window against.";
        GameObject targetGo = panel.Target.gameObject;
        var sb = new System.Text.StringBuilder(256);
        sb.Append("FLOAT IDENTITY: the mod's live float target is '").Append(targetGo.name)
          .Append("' go id=").Append(targetGo.GetInstanceID())
          .Append(" activeSelf=").Append(targetGo.activeSelf)
          .Append(" activeInHierarchy=").Append(targetGo.activeInHierarchy)
          .Append(", now parented under '")
          .Append(panel.Target.parent != null ? panel.Target.parent.name : "<no parent>")
          .Append("'. ");

        // THE LIVE SET — the objects the failing term was actually computed over, read now.
        UIDistributeReward[]? uis = _rewardUis;
        bool memoInLiveSet = false;
        GameObject? memo = _rootMemoWindow;
        if (uis == null || uis.Length == 0)
        {
            sb.Append("THE LIVE RESOLVED SET IS EMPTY, so the failing term had nothing to test. ");
        }
        else
        {
            sb.Append("THE LIVE RESOLVED SET (").Append(uis.Length)
              .Append("), each entry's own PopUp.window — the exact object the term tested, read "
                      + "at this same moment: ");
            for (int i = 0; i < uis.Length; i++)
            {
                if (i > 0)
                    sb.Append("; ");
                sb.Append('#').Append(i).Append(' ');
                UIDistributeReward ui = uis[i];
                if (ui == null)
                {
                    sb.Append("rewardUI <destroyed>");
                    continue;
                }
                sb.Append("rewardUI id=").Append(ui.GetInstanceID())
                  .Append(" on go '").Append(ui.gameObject.name)
                  .Append("' id=").Append(ui.gameObject.GetInstanceID());
                UIDistributePointsPopup pop = ui.PopUp;
                if (pop == null)
                {
                    sb.Append(", PopUp <destroyed>");
                    continue;
                }
                GameObject w = pop.window;
                if (w == null)
                {
                    sb.Append(", window <destroyed>");
                    continue;
                }
                if (ReferenceEquals(w, memo))
                    memoInLiveSet = true;
                sb.Append(", window go id=").Append(w.GetInstanceID())
                  .Append(" activeSelf=").Append(w.activeSelf)
                  .Append(" activeInHierarchy=").Append(w.activeInHierarchy)
                  .Append(", SAME OBJECT AS THE FLOAT TARGET=").Append(ReferenceEquals(w, targetGo))
                  .Append(", RELATION=").Append(DescribeRelation(w, targetGo, panel.Target));
            }
            sb.Append(". ");
        }

        // THE MEMO, labelled as what it is.
        if (memo == null)
        {
            sb.Append("MEMO: none — no successful resolve has been recorded for this conversion. ");
        }
        else
        {
            sb.Append("MEMO (the window recorded at the LAST SUCCESSFUL resolve, i.e. the one that "
                      + "was floated — NOT the object this tick's term tested): go id=")
              .Append(memo.GetInstanceID())
              .Append(" activeSelf=").Append(memo.activeSelf)
              .Append(" activeInHierarchy=").Append(memo.activeInHierarchy)
              .Append(", SAME OBJECT AS THE FLOAT TARGET=").Append(ReferenceEquals(memo, targetGo))
              .Append(", STILL IN THE LIVE RESOLVED SET=").Append(memoInLiveSet).Append(". ");
        }

        sb.Append("WHAT EACH ANSWER MEANS — and only what these inputs can support. "
                  + "(NAMING NOTE for anyone holding ModBuild 377 notes: the term this line used "
                  + "to call SAME OBJECT is unchanged in meaning and now reads SAME OBJECT AS THE "
                  + "FLOAT TARGET, because the old name never said same as WHAT — and in 377 it "
                  + "was in fact comparing the memo, not the object the term tested.) "
                  + "'STILL IN THE LIVE RESOLVED SET=False' is the finding: the reward UI the mod "
                  + "floated is no longer among the ones the lookup returns, so the term is being "
                  + "computed over a DIFFERENT popup and the manager-scoped ResolveRewardUis is "
                  + "the place to fix. 'STILL IN THE LIVE RESOLVED SET=True' with every live entry "
                  + "reading activeSelf=False means the resolver holds the right objects and they "
                  + "really are inactive, so the cause is downstream of the lookup. A live entry "
                  + "whose window is SAME OBJECT AS THE FLOAT TARGET=True while reading activeSelf "
                  + "False, in the same breath as this surface's drawn-content verdict reading "
                  + "positive, would be a genuine contradiction about one object — but note that "
                  + "THIS CLAUSE CANNOT PRODUCE THAT PAIRING BY ITSELF: the drawn-content verdict "
                  + "is measured over the float root, not over that entry's window, so read the "
                  + "two as separate measurements that happen to share a tick.");
        return sb.ToString();
    }

    /// <summary>
    /// The relation between a resolved window and the mod's float target, as ONE mutually
    /// exclusive answer. The ModBuild 377 clause printed two independent containment bools that
    /// were both necessarily true whenever the two were the same object, i.e. it spent two terms
    /// restating a third.
    /// </summary>
    private static string DescribeRelation(GameObject window, GameObject targetGo, Transform target)
    {
        if (ReferenceEquals(window, targetGo))
            return "IS THE FLOAT TARGET";
        if (IsDescendantOf(window.transform, target))
            return "IS INSIDE THE FLOAT TARGET";
        if (IsDescendantOf(target, window.transform))
            return "CONTAINS THE FLOAT TARGET";
        return "UNRELATED TO THE FLOAT TARGET (neither contains the other)";
    }

    /// <summary>Bounded ancestor walk — is <paramref name="node"/> at or below
    /// <paramref name="root"/>? Depth-capped for the same reason <see cref="ScenePath"/> is: this
    /// runs on a diagnostic path and must never become an unbounded climb.</summary>
    private static bool IsDescendantOf(Transform? node, Transform? root)
    {
        if (node == null || root == null)
            return false;
        const int MaxDepth = 24;
        Transform? t = node;
        for (int i = 0; t != null && i < MaxDepth; t = t.parent, i++)
        {
            if (ReferenceEquals(t, root))
                return true;
        }
        return false;
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
                    // ModBuild 375 — TWO CORRECTIONS, BOTH FORCED BY THE ModBuild 373 LOG.
                    //
                    // (1) IT ASSERTED A MECHANISM IT CANNOT OBSERVE. "somebody called
                    //     SetActive(false) on it" is not what !activeSelf means. A GameObject that
                    //     has been inactive since the scene loaded reads activeSelf == false with
                    //     no call ever made, and this test cannot distinguish the two. The reparent
                    //     clause was and stays correct — activeSelf really is local — but it was
                    //     doing duty for a conclusion it does not support.
                    // (2) IT PROMISED A GAME LOG LINE THAT DID NOT APPEAR. See AreaLineCaveat for
                    //     the falsification and for what the absence of that line actually proves
                    //     (nothing, on its own). The promise is gone from here rather than repeated
                    //     in three places: this term text is embedded in both verdict lines, and
                    //     both of them carry the caveat once, in full.
                    sb.Append("WINDOW INACTIVE — the window GameObject is ALIVE and its activeSelf "
                              + "is FALSE. WHAT THAT DOES AND DOES NOT SAY: activeSelf is LOCAL, so "
                              + "a reparent cannot have caused it — but it is equally NOT evidence "
                              + "that anything deactivated it, because an object left inactive when "
                              + "the scene loaded reads exactly the same and this test cannot tell "
                              + "the two apart. The game's own deactivating route is "
                              + "UIDistributePointsPopup.Hide(), which runs controllerArea.Destroy() "
                              + "beside window.SetActive(false)");
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

        // THE UNION (ModBuild 378). Drop what Unity has actually destroyed, then add anything the
        // scan found that we do not already hold. A reward UI never leaves this set for having
        // moved — only for having died — so a popup the mod has floated stays resolvable while it
        // is on the screen. See _knownRewardUis for the log evidence this repairs.
        int before = _knownRewardUis.Count;
        for (int i = _knownRewardUis.Count - 1; i >= 0; i--)
        {
            if (_knownRewardUis[i] == null)
                _knownRewardUis.RemoveAt(i);
        }
        int destroyed = before - _knownRewardUis.Count;
        for (int i = 0; i < found.Length; i++)
        {
            UIDistributeReward ui = found[i];
            if (ui == null)
                continue;
            bool have = false;
            for (int k = 0; k < _knownRewardUis.Count; k++)
            {
                // ReferenceEquals, not List.Contains: the comparison must be object identity and
                // must not route through Unity's overloaded ==, which answers a different question
                // for a destroyed object.
                if (ReferenceEquals(_knownRewardUis[k], ui))
                {
                    have = true;
                    break;
                }
            }
            if (!have)
                _knownRewardUis.Add(ui);
        }

        if (_knownRewardUis.Count == 0)
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
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    _knownRewardUis.Add(found[i]);
            }
            if (_knownRewardUis.Count == 0)
                return null;
        }

        if (_knownRewardUis.Count > found.Length)
        {
            // HW-VERIFY: THE ModBuild 378 RESOLVER FIX, FIRING. Its presence means a rescan came
            // back with FEWER reward UIs than the mod already knew about, and the union kept the
            // missing one(s) resolvable. In the ModBuild 377 log that loss is what armed the hold:
            // the set went from two to one and the survivor was the Gold popup, so the failing
            // term was computed over a popup the player had never been shown. Change-gated, so it
            // prints on the edge rather than every tick.
            VRLog.Note("WorldUI", "DISTRIBUTE RESOLVER UNION KEPT " + (_knownRewardUis.Count - found.Length)
                                  + " REWARD UI(s) THE RESCAN LOST: the manager-scoped "
                                  + "GetComponentsInChildren returned " + found.Length
                                  + " but this surface already knew " + _knownRewardUis.Count
                                  + " live one(s), so the union answered with all of them ("
                                  + destroyed + " dropped this pass for having been destroyed). "
                                  + "WHAT THIS DOES AND DOES NOT SAY: it says the scan no longer "
                                  + "REACHES a reward UI that is still alive — it does NOT say why, "
                                  + "and this line cannot tell a reparent from any other reason the "
                                  + "object left the manager's subtree. Read the FLOAT IDENTITY "
                                  + "clause's GameObject ids for that.");
        }

        _rewardUis = _knownRewardUis.ToArray();
        return _rewardUis;
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
    /// <item><b>The panel goes dark.</b> A floated panel drawing nothing for
    ///   <see cref="RescueSeconds"/> raises the rescue screen (see <see cref="TickBlindWatch"/>),
    ///   which sets <c>ManualScreenActive</c>, which is OUTSIDE the hold — so the float is
    ///   released and the hold ends. This is the term that replaced the ModBuild 377 hard cap,
    ///   and it is strictly better than the clock it replaced: it fires on the player having
    ///   nothing to look at, not on him being slow.</item>
    /// </list>
    ///
    /// <para><b>THERE IS NO LONGER ANY ELAPSED-TIME TERMINATOR, BY USER RULING (2026-09-03):</b>
    /// "Ist das Fenster da will ich nicht, dass ein User sich beeilen muss - ich will gar keine
    /// Zeitlimits dieser Art." Every entry above is a fact about the world, not a stopwatch. The
    /// dwells that remain in this file delay a LOG LINE or a RESCUE for a player who has nothing
    /// on screen; none of them takes a visible panel away from a player who does.</para>
    /// </summary>
    private bool HoldForDistribution =>
        Panel != null && _manager != null && _manager.IsDistributing;

    /// <summary>
    /// THE VETO TERM. True while the mod has a panel in front of the player that is measurably
    /// DRAWING — alive, not hidden by any of the three hides, and carrying at least one graphic
    /// the fit's own visibility rule counts as content. Read by the hard cap in
    /// <see cref="TickHold"/> and by the guaranteed escape in <see cref="TickBlindWatch"/>.
    ///
    /// <para><b>IT CANNOT LATCH, AND THAT IS THE PROPERTY THAT MAKES IT SAFE TO PUT IN FRONT OF A
    /// DEADLOCK GUARD.</b> It is not a flag anyone sets; it is the last measurement, and it stops
    /// being true <see cref="DrawnStaleSeconds"/> after that measurement was taken. So the worst
    /// case for a panel that goes dark is one check interval plus the staleness bound — under
    /// three seconds — after which every branch below behaves exactly as it did before this
    /// change.</para>
    ///
    /// <para><b>DURING THE ARMING GRACE IT IS TRUE ON PURPOSE.</b> A conversion that happened this
    /// frame has not been laid out, so "no graphics measured" is "too early to tell", not "dark".
    /// Judging it would raise the rescue screen on every healthy float. The grace is bounded by
    /// <see cref="DrawnGraceSeconds"/> and starts at the conversion, so it can delay a verdict by
    /// at most that and can never withhold one.</para>
    /// </summary>
    private bool PanelIsDrawing
    {
        get
        {
            if (Panel == null)
                return false;
            // The watch has not seen this conversion yet — it runs before base.Tick(), so the
            // tick that CONVERTS always lands here first. One tick, bounded, and it is the same
            // "too early to judge" the grace below covers.
            if (_drawnConvertedAt < 0f)
                return true;
            if (Time.unscaledTime - _drawnConvertedAt < DrawnGraceSeconds)
                return true;
            return _drawnVerdict && _drawnLastTrueAt >= 0f
                   && Time.unscaledTime - _drawnLastTrueAt <= DrawnStaleSeconds;
        }
    }

    /// <summary>
    /// Take the drawn-content measurement, at most once per <see cref="DrawnCheckSeconds"/>, and
    /// exactly once per tick. Called at the top of <see cref="Tick"/> so every consumer in the
    /// same frame reads ONE value: ModBuild 375's cap raised the rescue screen and the escape
    /// released it again in the same tick (LogOutput.log:2690 then :2691), which is what two
    /// consumers evaluating a moving term looks like.
    ///
    /// <para>WHY THIS IS A GATE TERM AND NOT AN INSTRUMENT, in the sense scripts/
    /// check-instrument-writes.py cares about: <c>CanvasConversion.TryMeasureDrawnContent</c>
    /// commits nothing — no hit-rect entry, no fit cadence, no dormancy counter — so switching the
    /// logging below off would not change what any other subsystem does. The fields written here
    /// are this rule's own state, read only by this rule.</para>
    /// </summary>
    private void TickDrawnWatch()
    {
        if (Panel == null)
        {
            _drawnConvertedAt = -1f;
            _drawnVerdict = false;
            _drawnLastTrueAt = -1f;
            _drawnNextCheck = 0f;
            _drawnGraphics = 0;
            _drawnWhy = "(no panel is floated)";
            // The darkness clock belongs to ONE conversion. Carrying it across a release would
            // let a previous float's darkness raise the rescue on the first frame of the next
            // one — a clock that measures two different panels is not measuring either.
            _darkSince = -1f;
            _darkLogged = false;
            return;
        }

        if (_drawnConvertedAt < 0f)
        {
            _drawnConvertedAt = Time.unscaledTime;
            _drawnNextCheck = 0f;
        }

        if (Time.unscaledTime < _drawnNextCheck)
            return;
        _drawnNextCheck = Time.unscaledTime + DrawnCheckSeconds;
        _drawnWalks++;

        ConvertedPanel panel = Panel;
        // The three hides are checked BEFORE the walk, and a hidden panel counts as NOT drawing.
        // That is the fail-safe direction twice over: a hidden panel genuinely shows the player
        // nothing, and the walk reads CanvasRenderer state that is frozen while a panel is
        // render-hidden, so its answer there would not be trustworthy anyway.
        if (!panel.IsAlive)
        {
            _drawnVerdict = false;
            _drawnGraphics = 0;
            _drawnWhy = "the conversion target is destroyed (ConvertedPanel.IsAlive false)";
            return;
        }
        if (panel.RevealPending || panel.RenderHidden || panel.OwnerRenderHidden)
        {
            _drawnVerdict = false;
            _drawnGraphics = 0;
            _drawnWhy = "the panel is HIDDEN, so it draws nothing whatever its content says "
                        + "(RevealPending=" + panel.RevealPending
                        + ", RenderHidden=" + panel.RenderHidden
                        + ", OwnerRenderHidden=" + panel.OwnerRenderHidden + ")";
            return;
        }

        if (CanvasConversion.TryMeasureDrawnContent(panel, out Rect content, out _, out int contributors)
            && contributors > 0)
        {
            _drawnVerdict = true;
            _drawnGraphics = contributors;
            _drawnLastTrueAt = Time.unscaledTime;
            _drawnWhy = "measured " + contributors + " visible graphic(s) over "
                        + content.width.ToString("0") + "x" + content.height.ToString("0") + " px";
            return;
        }

        _drawnVerdict = false;
        _drawnGraphics = 0;
        _drawnWhy = "the drawn-content walk found NO visible graphic under the float root — the "
                    + "panel is on the screen and empty";
    }

    /// <summary>The drawn-content verdict in words, for the lines that act on it. Formatted at log
    /// time only; the walk itself is the cadenced thing.</summary>
    private string DescribeDrawn()
    {
        return "DRAWN-CONTENT VERDICT: the mod's own floated panel is "
               + (PanelIsDrawing ? "DRAWING" : "NOT DRAWING")
               + " — " + _drawnWhy
               + " (visible graphic(s) at the last walk: " + _drawnGraphics
               + ", last positive reading "
               + (_drawnLastTrueAt < 0f
                   ? "never taken"
                   : (Time.unscaledTime - _drawnLastTrueAt).ToString("0.0") + " s ago")
               + ", " + _drawnWalks + " walk(s) this conversion, re-measured every "
               + DrawnCheckSeconds.ToString("0.0") + " s and treated as stale after "
               + DrawnStaleSeconds.ToString("0.0") + " s). THIS IS THE SAME PER-GRAPHIC "
               + "VISIBILITY RULE the HIT RECT and Host rect fit lines are printed from, so the "
               + "count here and the count there are the same quantity. WHAT IT DOES NOT SAY: "
               + "that the player can READ the panel, or that the right popup is on it — only "
               + "that something the fit counts as content is being drawn.";
    }

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

        // ONE measurement per tick, taken before anything consults it. Both the cap in TickHold
        // and the escape in TickBlindWatch read the value this leaves behind, so they cannot
        // disagree inside one frame.
        TickDrawnWatch();

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
                                          : _manager == null
                                              ? "the UIDistributeRewardManager is gone (scene "
                                                + "load or teardown) — the popup cannot exist "
                                                + "any more"
                                              : Panel == null
                                                  ? "there is no floated panel left to hold (the "
                                                    + "target was destroyed, the panel went dark "
                                                    + "and the rescue screen took over, or "
                                                    + "another gate term released it)"
                                                  : "the game finished distributing "
                                                    + "(IsDistributing false), which is also "
                                                    + "the moment MapChoreographer."
                                                    + "WaitDistributionEnds stops waiting")
                                      + ". " + DescribeTerms()
                                      // APPENDED, ModBuild 378. Every reason above is a STATE
                                      // term. There is no elapsed-time terminator any more (user
                                      // ruling: no Zeitlimits of this kind), so a hold that ran a
                                      // long time is not a fault and this line never implies one —
                                      // the duration is reported because it is useful, never
                                      // because it is judged.
                                      + " NO TIME LIMIT: this hold has no hard cap; it ended "
                                      + "because the state above changed, not because a clock ran "
                                      + "out. " + DescribeDrawn());
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
                                  + ". READ THE IDENTITY LIKE THIS: either bool True means a "
                                  + "deactivate or destroy of that window would have RUN "
                                  + "controllerArea.Destroy (via OnDisable); both False means it "
                                  + "would not, and the game's area lines then say nothing either "
                                  + "way about this window. Running it is not the same as logging "
                                  + "it — " + AreaLineCaveat
                                  + " The hold ends on IsDistributing, on the manager going away, "
                                  + "on the panel dying, on the manual screen, or on the panel "
                                  + "going dark (which raises the rescue screen and releases it). "
                                  + "IT HAS NO TIME LIMIT: the hold will stand for as long as the "
                                  + "player wants to look at the panel, by user ruling — so a long "
                                  + "hold beside a positive drawn-content verdict is the system "
                                  + "working, not a fault to chase."
                                  // APPENDED, ModBuild 376 — Fix 2's instrument, on the line that
                                  // fires at the moment the contradiction exists: the resolver
                                  // says no popup is shown WHILE the mod holds a float. These
                                  // clauses name both sides by instance id and say whether they
                                  // are one object. A path could not: two siblings may share a
                                  // name and ScenePath() prints them identically.
                                  + " IDENTITIES (instance ids, which a path cannot carry): "
                                  + "rewardUI=" + _rewardId + ", PopUp component=" + _popupId
                                  + ", PopUp GameObject=" + _popupGoId + ", window=" + _windowId
                                  + ". " + _resolvedCensus
                                  + " " + DescribeFloatIdentity()
                                  + " " + DescribeDrawn());
        }

        // NO CAP HERE ANY MORE. ModBuild 377 lapsed the hold at 90 s and raised the rescue
        // screen; the user ruled that out on 2026-09-03 ("ich will gar keine Zeitlimits dieser
        // Art"), and it had already cost him the panel he was carrying. The escalation this block
        // used to perform now lives entirely in TickBlindWatch, keyed on the panel DRAWING
        // NOTHING rather than on a stopwatch — see the block above HoldForDistribution for the
        // full argument and for the one case only a clock could have caught.
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
            _identityCaptured = false;
            // ModBuild 376 — the drawn-content watch's per-episode latches go with the episode,
            // for the same reason every latch above does: a marker that fired for the last
            // distribution must not stay silent through the next one.
            _darkSince = -1f;
            _darkLogged = false;
            _resolvedCensus = "(not captured)";
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
            // ==================================================================================
            // ModBuild 376 — "A PANEL IS UP" IS NOT "THE PLAYER CAN SEE SOMETHING".
            //
            // Through ModBuild 375 the mere EXISTENCE of a ConvertedPanel cleared the escape's
            // clock and released any standing rescue. That is wrong in both directions and both
            // of them are user rules:
            //   * a panel that is up and DARK suppressed the escape for ever — "Es darf niemals
            //     leere Fenster geben", and the escape is the thing that was supposed to catch
            //     exactly that;
            //   * and the cap, which had no such term at all, tore down a panel that WAS drawn.
            // One measurement fixes both ends, and it is the same one: PanelIsDrawing.
            // ==================================================================================
            bool drawing = PanelIsDrawing;

            if (!_floatLogged)
            {
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
                                      + "window would RUN controllerArea.Destroy "
                                      + "(UIDistributePointsPopup.OnDisable and ControllerInputAreaLocal."
                                      + "OnDisable both call it). That is a statement about which code "
                                      + "would execute, and nothing more: " + AreaLineCaveat
                                      // APPENDED, ModBuild 376 — Fix 2's instrument. The paths above
                                      // cannot tell one GameObject from a same-named neighbour; these
                                      // three clauses name the objects by instance id, list EVERY
                                      // reward UI the lookup resolved, and state whether the object
                                      // the resolver judges is the object the mod has floated.
                                      + " " + _resolvedCensus
                                      + " " + DescribeFloatIdentity()
                                      + " " + DescribeDrawn());
            }

            if (drawing)
            {
                // The player has something in front of him. Any standing rescue is over.
                // (It cannot normally be standing here — the rescue sets ManualScreenActive,
                // which releases this surface's float — but the tick order between FlatScreen and
                // the surfaces is not something this file gets to assume, so the release is
                // written level-triggered rather than as a claim about ordering.)
                _blindSince = -1f;
                _darkSince = -1f;
                _darkLogged = false;
                if (_rescueAsked)
                {
                    _rescueAsked = false;
                    FlatScreen.ReleaseRescueScreen("the distribute popup is floated in world space "
                                                   + "again, so the player has a usable panel without "
                                                   + "the 2D composite");
                }
                return;
            }

            // ==================================================================================
            // A PANEL IS FLOATED AND IT IS DRAWING NOTHING. This is a case ModBuild 375 could not
            // reach at all: the existence test above returned early, so the escape's clock was
            // cleared and the rescue could never be raised for it. It gets its own clock and its
            // own marker rather than borrowing the blind one, because the blind line's text says
            // "the mod has no panel up" and that would be a false statement here — an instrument
            // that misdescribes its own trigger costs a round.
            //
            // NOTE THE ONE THING THIS DELIBERATELY DOES NOT DO: it does not release or hide the
            // panel. Nothing here writes game state — no Show, no Hide, no SetActive, no
            // synthesised click. It raises the mod's own 2D composite, on which the game's own
            // widgets are drawn and clickable, and the player presses the game's own button.
            // ==================================================================================
            if (_darkSince < 0f)
                _darkSince = Time.unscaledTime;
            if (Time.unscaledTime - _darkSince < RescueSeconds)
                return;

            if (!_darkLogged)
            {
                _darkLogged = true;
                // HW-VERIFY: the empty-panel half of the escape, which did not exist before
                // ModBuild 376. Its presence means the mod had a decision panel floated that was
                // drawing NOTHING while the campaign was gated on it — the "leeres Fenster" case
                // — and that the escape treated it as nothing rather than as a panel.
                VRLog.Alert("WorldUI", "DISTRIBUTE FLOAT IS DARK: the mod has a decision panel "
                                      + "floated for this distribution and it has been drawing "
                                      + "NOTHING for " + RescueSeconds.ToString("0") + " s while "
                                      + "UIDistributeRewardManager.IsDistributing is still true. "
                                      + "An empty panel is not a panel: through ModBuild 375 the "
                                      + "mere existence of a conversion cleared this escape's "
                                      + "clock, so this state suppressed the rescue instead of "
                                      + "raising it. The 2D composite goes up on this same tick. "
                                      + DescribeDrawn() + " " + DescribeTerms()
                                      + " " + DescribeFloatIdentity());
            }

            if (_rescueAsked)
                return;
            _rescueAsked = true;
            FlatScreen.RequestRescueScreen(
                "DistributeRewardSurface",
                "The game has been distributing a reward for " + RescueSeconds.ToString("0")
                + " s and the panel the mod has floated for it has been drawing NOTHING for that "
                + "whole time — an empty window, which the standing user ruling forbids. "
                + DescribeDrawn() + " " + DescribeTerms());
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
                                  + _windowHasControllerArea + "."
                                  // APPENDED, ModBuild 376 — every reward UI the lookup resolved,
                                  // by instance id. "resolved=1" above is a COUNT; the manager
                                  // chains a LIST of DistributeRewardProcess and each of them
                                  // owns a processUI.PopUp, so WHICH one was resolved is the
                                  // question, and a count cannot answer it.
                                  + " " + _resolvedCensus);
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
        _knownRewardUis.Clear();
        _shownReward = null;
        _floatedReward = null;
        _sweepTried = false;
        _episodeOpen = false;
        _floatLogged = false;
        _blindLogged = false;
        _blindSince = -1f;
        _holdArmed = false;
        _holdLogged = false;
        _identityCaptured = false;
        _termCount = 0;
        _rootMemo = null;
        _rootMemoFor = null;
        _rootMemoWindow = null;
        _drawnNextCheck = 0f;
        _drawnLastTrueAt = -1f;
        _drawnConvertedAt = -1f;
        _drawnGraphics = 0;
        _drawnWalks = 0;
        _drawnVerdict = false;
        _drawnWhy = "(not measured yet)";
        _darkSince = -1f;
        _darkLogged = false;
        _rewardId = 0;
        _popupId = 0;
        _popupGoId = 0;
        _windowId = 0;
        _resolvedCensus = "(not captured)";
    }
}
