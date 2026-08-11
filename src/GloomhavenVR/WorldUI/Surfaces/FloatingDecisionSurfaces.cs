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
