using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The control board (P7 redesign, test #10; test #15: central DASHBOARD): a
/// desk-like tray in front of the player, tilted toward them like a card-table edge
/// (~30° from horizontal, [Cards] TrayTilt), chest height, anchored in rig space by
/// default ([Cards] TrayFollow; the PIN button on the frame switches to
/// world-anchored). Visible for the WHOLE scenario since test #15 (dashboard), not
/// only during card selection. Layout:
/// - TOP edge: the game's REAL initiative track (converted canvas, posed by WorldUI
///   onto <see cref="InitiativeMount"/> — portraits incl. the vanilla '?' for
///   players who have not locked in),
/// - LEFT: the converted Objectives panel (<see cref="ObjectivesMount"/>) next to
///   the REST zone (short/long-rest tokens built by <see cref="RestControls"/>),
/// - CENTER: two large card slots (slot 0 = initiative, marked by the numbered
///   badge; drop to place, grab to take back, physical swap = initiative swap),
/// - RIGHT: CONFIRM (drives the game's own Ready button path), UNDO and a settings
///   gear; the PIN follow-toggle sits on the bottom-right frame corner,
/// - TOP-RIGHT corner: the round readout (test #18 — replaces the floating
///   PhaseBanner box; same "Runde N" text, fed from the same game state),
/// - BOTTOM-CENTER, under the slots: the WorldUI turn-flow ButtonCluster docks on
///   <see cref="ButtonClusterMount"/> (test #19 — Undo | Ready | Skip with the
///   game's live labels/states, no longer floating at the table edge).
/// Poke AND laser work on every element: pokes via the P2 registry, laser via
/// <see cref="LaserTargets"/> which CardsDriver ray-tests geometrically each frame.
/// Every interaction is logged. Slot order == initiative order:
/// <see cref="SyncFromGameState"/> mirrors
/// <c>CCharacterClass.RoundAbilityCards/InitiativeAbilityCard</c> into the slots.
/// Bundle asset <c>PlayTray.prefab</c> (children <c>Slot1/Slot2/ShortRestToken/
/// LongRestToken/ConfirmButton/UndoButton</c>, see unity/.../Table/README.md) with a
/// full procedural fallback.
/// </summary>
internal sealed class PlayTray
{
    // Meters at scale 1, scaled by tray lossyScale. GENEROUS on purpose (test #13),
    // widened again in test #15: hardware logs showed releases consistently landing
    // 14–16 cm real from the slot center (the release gesture moves the hand) while
    // the hover glow HAD triggered — the primary accept rule is now the glow itself
    // (CardsDriver highlight-at-release), this radius is only the fallback.
    private const float SlotCaptureRadius = 0.25f;

    /// <summary>
    /// Test #18: the card slots read a bit small — the slot ROOTS are scaled 1.3×,
    /// which uniformly enlarges the frames, the snap-glow highlights, the initiative
    /// badge and the PARKED cards (cards park at localScale 1 under the slot, so the
    /// parent scale IS the slot size). Layout stays collision-free at SlotSpacing
    /// 0.155: enlarged highlight half-width 0.0635·1.24·1.3/2 ≈ 0.051 m → outer
    /// edges ±0.129, clear of the rest plate (right edge -0.19) and the CONFIRM
    /// column (base-plate left edge ≈ 0.177); the inter-slot highlight gap stays
    /// ≈ 0.053 m. Grab/release is unaffected: VRCard.OnGrab re-parents into the
    /// hand (held scale is hand-defined) and OnRelease restores localScale =
    /// home scale back under the slot. Slot captions compensate the inherited
    /// scale (see <see cref="BuildSlotLabels"/>); SlotCaptureRadius stays as-is —
    /// at 0.25 m it already spans both slots and the glow is the primary accept
    /// rule anyway.
    /// </summary>
    private const float SlotScale = 1.3f;

    /// <summary>
    /// The live tray instance (test #15 dashboard mount seam): WorldUI surfaces read
    /// <see cref="InitiativeMount"/>/<see cref="ObjectivesMount"/> through this to
    /// pose their converted hosts on the tray. Null while no tray exists.
    /// </summary>
    internal static PlayTray? Current { get; private set; }

    private Transform? _root;
    private Transform? _anchorParent;
    private Transform? _initiativeMount;
    private Transform? _objectivesMount;
    private Transform? _clusterMount;
    private Transform?[] _slots = new Transform?[2];
    private Transform? _shortRestAnchor;
    private Transform? _longRestAnchor;
    private readonly VRCard?[] _occupants = new VRCard?[2];

    private TextMeshPro? _badge;
    private TextMeshPro? _roundLabel;
    private InitiativeBadgeZone? _badgeZone;
    private BoardButton? _confirm;
    private BoardButton? _undo;
    private bool _placed;
    private bool _wantVisible;
    private bool _placementDeferLogged;

    internal bool IsVisible => _root != null && _root.gameObject.activeSelf;

    internal Transform? ShortRestAnchor => _shortRestAnchor;
    internal Transform? LongRestAnchor => _longRestAnchor;
    internal Transform? Root => _root;

    // ---- dashboard mount seam (test #15) -----------------------------------------------
    // WorldUI surfaces POSE-FOLLOW these anchors (they never re-parent their hosts
    // under the tray: the hosts carry live GAME UI, and a tray/rig teardown must
    // never cascade into destroying game-owned canvases — reversibility rule).
    // Anchor origin conventions: InitiativeMount = bottom-center of the initiative
    // panel (grows up, above the tray's top edge); ObjectivesMount = right-center
    // (grows left, off the tray's left edge). Both inherit tray pose AND scale.

    /// <summary>Mount for the converted InitiativeTrack canvas (top edge). Null until built.</summary>
    internal Transform? InitiativeMount => _initiativeMount;

    /// <summary>Mount for the converted Objectives panel (left side). Null until built.</summary>
    internal Transform? ObjectivesMount => _objectivesMount;

    /// <summary>
    /// Mount for the WorldUI turn-flow ButtonCluster (Undo | Ready | Skip — the
    /// game's live mid-turn buttons incl. "skip movement"/"end turn" states), docked
    /// under the card slots (test #19). Pose-follow like the other mounts (the
    /// cluster is never re-parented under the tray). Null until built.
    /// </summary>
    internal Transform? ButtonClusterMount => _clusterMount;

    /// <summary>Target panel width at the initiative mount, tray-local meters (× mount lossyScale).</summary>
    internal const float InitiativeMountWidth = BoardW;

    /// <summary>Max panel height at the initiative mount, tray-local meters.</summary>
    internal const float InitiativeMountMaxHeight = 0.14f;

    /// <summary>Target panel width at the objectives mount, tray-local meters.</summary>
    internal const float ObjectivesMountWidth = 0.26f;

    /// <summary>Max panel height at the objectives mount, tray-local meters.</summary>
    internal const float ObjectivesMountMaxHeight = 0.32f;

    /// <summary>
    /// ONE shared pixel density for every tray-docked panel, uGUI pixels per
    /// tray-local meter (test #16). Before, each panel was scaled to FIT its dock
    /// area from its host-rect size — similar dock sizes over wildly different
    /// host rects (1714 px initiative track vs 100 px objectives) made the
    /// initiative portraits minuscule and the objectives text giant. With a shared
    /// density the world size follows the CONTENT pixel size instead, so text and
    /// portraits read consistently across panels. 2400 px/m maps the game's
    /// ~24–30 px HUD body text to ~10–13 mm — the same order as the tray's own
    /// TmpFit caption/button labels (0.024–0.030 m boxes) at arm's length.
    /// Panels that need larger type multiply this via their DensityScale override
    /// (WorldUI TrayMountedPanelSurface, test #17: objectives at 0.6×).
    /// </summary>
    internal const float TrayPixelsPerMeter = 2400f;

    /// <summary>Raised when the initiative badge is poked (CardsDriver queues the swap).</summary>
    internal System.Action? SwapRequested;

    /// <summary>Raised by the CONFIRM button (CardsDriver queues the game's Ready click).</summary>
    internal System.Action? ConfirmRequested;

    /// <summary>Raised by the UNDO button (CardsDriver queues the game's Undo click).</summary>
    internal System.Action? UndoRequested;

    // ------------------------------------------------------------------ laser targets --

    internal readonly struct LaserTarget
    {
        public readonly Collider Collider;
        public readonly IPokeable Target;

        public LaserTarget(Collider collider, IPokeable target)
        {
            Collider = collider;
            Target = target;
        }
    }

    /// <summary>
    /// Every pokeable board element, for the dominant hand's laser (CardsDriver
    /// ray-tests these each frame — poke AND laser work on all elements, test #10).
    /// </summary>
    internal readonly List<LaserTarget> LaserTargets = new(8);

    internal void RegisterLaserTarget(Collider collider, IPokeable target)
    {
        if (collider != null && target != null)
            LaserTargets.Add(new LaserTarget(collider, target));
    }

    // ------------------------------------------------------------------ lifecycle --

    internal void EnsureBuilt(VRCardFactory factory, Transform anchorParent)
    {
        _anchorParent = anchorParent;
        if (_root != null)
        {
            // Re-home only in FOLLOW mode — a pinned tray lives under its world
            // "TrayPin" holder (test #15) and must not be dragged back under the rig.
            if (CardsConfig.TrayFollow.Value && _root.parent != anchorParent)
            {
                _root.SetParent(anchorParent, worldPositionStays: false);
                _placed = false;
            }
            return;
        }

        // Externally destroyed tray (rig teardown in follow mode): stale laser
        // targets would otherwise pile up across rebuilds.
        LaserTargets.RemoveAll(static t => t.Collider == null);

        _root = new GameObject("GloomhavenVR.PlayTray").transform;
        _root.SetParent(anchorParent, worldPositionStays: false);
        Current = this;

        Transform? confirmAnchor = null;
        Transform? undoAnchor = null;
        GameObject? prefab = factory.GetTrayPrefab();
        if (prefab != null)
        {
            GameObject visual = Object.Instantiate(prefab, _root, false);
            visual.name = "TrayVisual";
            _slots[0] = FindDeep(visual.transform, "Slot1");
            _slots[1] = FindDeep(visual.transform, "Slot2");
            _shortRestAnchor = FindDeep(visual.transform, "ShortRestToken");
            _longRestAnchor = FindDeep(visual.transform, "LongRestToken");
            confirmAnchor = FindDeep(visual.transform, "ConfirmButton");
            undoAnchor = FindDeep(visual.transform, "UndoButton");
        }

        if (_slots[0] == null || _slots[1] == null)
            BuildProceduralBoard();

        // Bigger card slots (test #18): scale the slot roots BEFORE the dependent
        // visuals build — frames (already childed), highlights, badge, labels and
        // the parked cards all inherit the slot scale.
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot != null)
                slot.localScale *= SlotScale;
        }

        BuildSlotLabels();
        BuildSlotHighlights();
        BuildBadge();
        BuildButtons(confirmAnchor, undoAnchor);
        BuildHandle();
        BuildDashboardControls();
        BuildRoundReadout();
        BuildMounts();
        // Mod layer (render-only — zones & tokens poke via registries).
        Core.VRLayers.Apply(_root.gameObject);
        _placed = false;
        // A persisted PINNED mode is deliberately NOT applied here (test #17): the
        // pinned WORLD pose does not survive sessions, so pinning the fresh root now
        // would anchor the tray at a stale/default pose (it spawned far below the
        // map). PlaceAtHead re-pins right after the initial head-relative placement.
    }

    /// <summary>
    /// Test #15 dashboard: pose anchors for the game's REAL converted UI. The
    /// initiative track docks above the top edge (replaces the old text strip AND
    /// the free-floating world panel); the objectives panel docks off the left edge.
    /// WorldUI pose-follows these — see the mount seam doc at <see cref="InitiativeMount"/>.
    /// </summary>
    /// <summary>
    /// Test #18: the round number ON the board (top-right corner, above the CONFIRM
    /// column) instead of the floating PhaseBanner box. A small dark plate + gold
    /// TMP label; the text updates change-gated in <see cref="TickStatus"/>.
    /// Collision check: plate top edge y≈0.143 &lt; board edge 0.16; bottom edge
    /// y≈0.107 clears the CONFIRM base plate (top edge y≈0.079).
    /// </summary>
    private void BuildRoundReadout()
    {
        if (_root == null)
            return;

        var readoutGo = new GameObject("RoundReadout");
        readoutGo.transform.SetParent(_root, worldPositionStays: false);
        readoutGo.transform.localPosition = new Vector3(ButtonZoneX, 0.125f, -0.004f);

        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        Object.Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(readoutGo.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(0.13f, 0.036f, 1f);
        plate.transform.localPosition = new Vector3(0f, 0f, 0.006f); // behind the text, in front of the board
        Tint(plate, new Color(0.12f, 0.11f, 0.10f));

        _roundLabel = readoutGo.AddComponent<TextMeshPro>();
        _roundLabel.text = "-";
        _roundShown = int.MinValue; // keep the change-detection key in sync after a rebuild
        _roundLabel.alignment = TextAlignmentOptions.Center;
        _roundLabel.color = new Color(1f, 0.9f, 0.6f);
        // Single line fitted to the plate ("Runde 12" and longer localizations shrink).
        Core.TmpFit.Fit(_roundLabel, 0.12f, 0.028f, maxFontSize: 0.32f, wrap: false);
    }

    /// <summary>Cluster dock scale: the cluster's real-meter layout shrunk onto the button strip.</summary>
    private const float ButtonClusterMountScale = 0.7f;

    /// <summary>Cluster mount board-Y (collision math in <see cref="BuildMounts"/>).</summary>
    private const float ButtonClusterMountY = -0.115f;

    private void BuildMounts()
    {
        _initiativeMount = new GameObject("InitiativeMount").transform;
        _initiativeMount.SetParent(_root, worldPositionStays: false);
        _initiativeMount.localPosition = new Vector3(0f, BoardH * 0.5f + 0.012f, -0.004f);

        _objectivesMount = new GameObject("ObjectivesMount").transform;
        _objectivesMount.SetParent(_root, worldPositionStays: false);
        _objectivesMount.localPosition = new Vector3(-BoardW * 0.5f - 0.012f, 0f, -0.004f);

        // Test #19: the ButtonCluster docks under the card slots. Frame: +Z up the
        // board ("away from the player" — the PanelLayout pose contract the
        // cluster's own 180° yaw flip expects), +Y out of the board (caps rise
        // toward the viewer, presses travel into the board). At 0.7× the cluster
        // spans x ±0.109 (skip center 0.077 + base half 0.032) — clear of the rest
        // plate (right edge -0.1925) and the gear/undo column (left edge 0.186) —
        // and y -0.115 ± 0.042 (ready base half) → -0.157..-0.073: below the slot
        // captions (bottom edge ≈ -0.068), inside the board (bottom edge -0.16),
        // above the handle collider (top edge ≈ -0.165).
        _clusterMount = new GameObject("ButtonClusterMount").transform;
        _clusterMount.SetParent(_root, worldPositionStays: false);
        _clusterMount.localPosition = new Vector3(0f, ButtonClusterMountY, -0.006f);
        _clusterMount.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        _clusterMount.localScale = Vector3.one * ButtonClusterMountScale;
    }

    // ------------------------------------------------------------------ grab handle --

    private TrayGrabHandle? _handle;
    private BoardButton? _followToggle;
    private BoardButton? _gear;

    /// <summary>
    /// Test #14 ("Controllboard"): a clearly visible handle bar along the tray's
    /// bottom edge. Grip it to move/rotate the tray; grip with BOTH hands to resize
    /// (0.5×–2×). Registered as a normal <see cref="Hands.Interact.IGrabbable"/>, so
    /// the P2 ProximityGrabber arbitration applies — WorldGrab yields whenever the
    /// grip starts on (or highlights) the handle, and a grip anywhere else never
    /// touches the tray.
    /// </summary>
    private void BuildHandle()
    {
        if (_root == null)
            return;
        var handleGo = new GameObject("TrayHandle");
        handleGo.transform.SetParent(_root, worldPositionStays: false);
        handleGo.transform.localPosition = new Vector3(0f, -BoardH * 0.5f - 0.030f, 0.004f);

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        Object.Destroy(bar.GetComponent<Collider>());
        bar.transform.SetParent(handleGo.transform, worldPositionStays: false);
        bar.transform.localScale = new Vector3(BoardW * 0.55f, 0.024f, 0.024f);
        Tint(bar, new Color(0.62f, 0.5f, 0.28f)); // brass bar — reads as "grab me"

        var box = handleGo.AddComponent<BoxCollider>();
        box.size = new Vector3(BoardW * 0.62f, 0.05f, 0.05f);
        box.isTrigger = true;

        _handle = handleGo.AddComponent<TrayGrabHandle>();
        _handle.Init(this, bar.GetComponent<MeshRenderer>());
    }

    // ------------------------------------------------------------------ dashboard controls --

    /// <summary>
    /// Test #15: the tray-frame utility buttons.
    /// - PIN toggle (bottom-right corner, next to the handle bar): switches between
    ///   "follows the player" (rig-anchored, default) and "pinned in the world".
    ///   Label mirrors the state (FOLLOW / PINNED, accent while pinned); persisted
    ///   as [Cards] TrayFollow; poke AND laser (registered laser target).
    /// - Settings gear (right column, under UNDO): opens the in-VR settings panel
    ///   (same panel as the table-edge gear / A-X chord).
    /// </summary>
    private void BuildDashboardControls()
    {
        if (_root == null)
            return;

        Transform pinAnchor = NewAnchor("FollowToggle",
            new Vector3(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -0.002f));
        _followToggle = BoardButton.Create(pinAnchor, new Vector2(0.068f, 0.030f),
            new Color(0.75f, 0.55f, 0.2f), "FOLLOW", ToggleFollow);
        _followToggle.SetState(true, accent: !CardsConfig.TrayFollow.Value);
        RegisterLaserTarget(_followToggle.Collider!, _followToggle);

        Transform gearAnchor = NewAnchor("SettingsGear",
            new Vector3(ButtonZoneX, -0.125f, -0.006f));
        _gear = BoardButton.Create(gearAnchor, new Vector2(0.062f, 0.030f),
            new Color(0.4f, 0.42f, 0.5f), "SET",
            () => WorldUI.SettingsPanel.RequestToggle());
        _gear.SetState(true, accent: false);
        RegisterLaserTarget(_gear.Collider!, _gear);
    }

    private void ToggleFollow()
    {
        bool follow = !CardsConfig.TrayFollow.Value;
        CardsConfig.TrayFollow.Value = follow; // BepInEx persists on set
        ApplyFollowMode();
        VRLog.Info("Cards", $"Tray anchor mode → {(follow ? "FOLLOW (rig-anchored)" : "PINNED (world-anchored)")}.");
    }

    /// <summary>World-anchor holder while pinned (carries the rig scale — see ApplyFollowMode).</summary>
    private Transform? _pinRoot;

    /// <summary>
    /// Apply [Cards] TrayFollow to the live tray (test #15).
    /// Follow: re-home under the rig-space anchor and re-anchor at the configured
    /// head offsets. Pinned: the tray keeps its EXACT current world pose. It is
    /// re-parented under a world-static holder ("TrayPin", DontDestroyOnLoad) whose
    /// scale mirrors the rig anchor's lossy scale — so the tray's own localScale
    /// keeps its 0.5×–2× semantics (PlaceAtHead, PersistPoseToConfig and the
    /// two-hand resize clamp all assume that; a bare world detach would bake the
    /// ~diorama-scale factor into localScale and break all three).
    /// </summary>
    private void ApplyFollowMode()
    {
        if (_root == null)
            return;
        if (CardsConfig.TrayFollow.Value)
        {
            if (_anchorParent != null && _root.parent != _anchorParent)
                _root.SetParent(_anchorParent, worldPositionStays: true);
            PlaceAtHead();
            if (_pinRoot != null)
            {
                Object.Destroy(_pinRoot.gameObject);
                _pinRoot = null;
            }
        }
        else
        {
            if (_pinRoot == null)
            {
                _pinRoot = new GameObject("GloomhavenVR.TrayPin").transform;
                _pinRoot.gameObject.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(_pinRoot.gameObject);
            }
            Transform? scaleRef = _root.parent != null ? _root.parent : _anchorParent;
            _pinRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _pinRoot.localScale = Vector3.one * (scaleRef != null ? scaleRef.lossyScale.x : 1f);
            if (_root.parent != _pinRoot)
                _root.SetParent(_pinRoot, worldPositionStays: true);
        }
        if (_followToggle != null)
        {
            _followToggle.SetState(true, accent: !CardsConfig.TrayFollow.Value);
            _followToggle.SetLabel(CardsConfig.TrayFollow.Value ? "FOLLOW" : "PINNED");
        }
    }

    internal void Destroy()
    {
        if (ReferenceEquals(Current, this))
            Current = null; // WorldUI mount consumers fall back to the floating layout
        _occupants[0] = _occupants[1] = null;
        LaserTargets.Clear();
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
        }
        if (_pinRoot != null)
        {
            Object.DestroyImmediate(_pinRoot.gameObject);
            _pinRoot = null;
        }
        _slots = new Transform?[2];
        _slotHighlights[0] = _slotHighlights[1] = null; // children of _root, destroyed with it
        _highlightedSlot = -1;
        _badge = null;
        _roundLabel = null; // child of _root, destroyed with it
        _roundShown = int.MinValue;
        _badgeZone = null;
        _confirm = null;
        _undo = null;
        _handle = null; // child of _root, destroyed with it
        _followToggle = null;
        _gear = null;
        _initiativeMount = null;
        _objectivesMount = null;
        _clusterMount = null; // child of _root, destroyed with it
        _placed = false;
        _wantVisible = false;
        _placementDeferLogged = false;
    }

    /// <summary>
    /// Tray visibility. While the initial placement is still deferred (untracked
    /// head at scenario start, test #17) the root STAYS HIDDEN — showing it would
    /// flash the tray at a stale/default pose; <see cref="TickPlacement"/> retries
    /// until the head delivers a real pose.
    /// </summary>
    internal void SetVisible(bool visible)
    {
        if (_root == null)
            return;
        _wantVisible = visible;
        if (visible && !_placed)
            PlaceAtHead();
        bool show = visible && _placed;
        if (_root.gameObject.activeSelf != show)
            _root.gameObject.SetActive(show);
    }

    /// <summary>Per-frame retry for a deferred initial placement (CardsDriver.Update).</summary>
    internal void TickPlacement()
    {
        if (_wantVisible && !_placed)
            SetVisible(true);
    }

    /// <summary>
    /// Position the board in rig space from the current head pose + config offsets.
    /// [Cards] TrayTilt is degrees FROM HORIZONTAL: 0 = flat desk, 90 = upright
    /// panel; default 30 reads like a lectern / card-table edge (test #10). The
    /// board's -Z (element side) faces up toward the player.
    /// </summary>
    internal void PlaceAtHead()
    {
        if (_root == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        // Untracked head (test #17): in a session's first frames the rig camera
        // still sits at its local origin — the "in front of the player" math would
        // place (and a persisted PINNED mode then permanently pin) the tray at a
        // garbage pose (it spawned far below the map). Defer until the pose driver
        // delivered a real pose — the same first-pose signal VRRigDriver gates its
        // pending recenter on; TickPlacement retries every frame.
        if (head == VRRigDriver.HeadCamera && head.transform.localPosition.sqrMagnitude < 1e-6f)
        {
            if (!_placementDeferLogged)
            {
                _placementDeferLogged = true;
                VRLog.Info("Cards", "Control board placement deferred — head has no tracked pose yet.");
            }
            return;
        }
        _placementDeferLogged = false;

        Transform headT = head.transform;
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flatForward);

        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        Vector3 offset = CardsConfig.TrayOffset; // (right, -down, forward), real meters
        Vector3 pos = headT.position
                      + flatForward * (offset.z * scale)
                      + right * (offset.x * scale)
                      + Vector3.up * (offset.y * scale);

        _root.position = pos;
        // TrayYaw/TrayScale are the persisted tray-grab layout (test #14 wish:
        // "Controllboard" the player can grip-move/resize; survives sessions).
        _root.rotation = Quaternion.Euler(0f, CardsConfig.TrayYaw.Value, 0f)
                         * Quaternion.LookRotation(flatForward, Vector3.up)
                         * Quaternion.Euler(90f - CardsConfig.TrayTilt.Value, 0f, 0f);
        _root.localScale = Vector3.one * CardsConfig.ClampedTrayScale;
        _placed = true;
        VRLog.Info("Cards", $"Control board placed (tilt {CardsConfig.TrayTilt.Value}°, " +
                            $"yaw {CardsConfig.TrayYaw.Value:F0}°, scale {CardsConfig.ClampedTrayScale:F2}×).");
        // A persisted PINNED mode re-engages only NOW, at the just-placed
        // head-relative pose (test #17): the tray always spawns in front of the
        // player, pinned or not.
        if (!CardsConfig.TrayFollow.Value && _root.parent != _pinRoot)
            ApplyFollowMode();
    }

    /// <summary>
    /// Persist the CURRENT root pose back into the config (called by
    /// <see cref="TrayGrabHandle"/> when the last gripping hand lets go): the inverse
    /// of <see cref="PlaceAtHead"/> — head-relative offsets in real meters, yaw
    /// relative to the head's flat forward, and the size multiplier. BepInEx writes
    /// the ConfigFile on set, so the layout survives sessions.
    /// </summary>
    internal void PersistPoseToConfig()
    {
        if (_root == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;

        Transform headT = head.transform;
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flatForward);

        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        if (scale < 1e-5f)
            return;
        Vector3 delta = _root.position - headT.position;
        CardsConfig.TrayForward.Value = Vector3.Dot(delta, flatForward) / scale;
        CardsConfig.TrayRight.Value = Vector3.Dot(delta, right) / scale;
        CardsConfig.TrayDown.Value = -delta.y / scale;

        // Yaw: heading of the tray's flat forward relative to the head's.
        Vector3 trayFlat = _root.rotation * Quaternion.Euler(-(90f - CardsConfig.TrayTilt.Value), 0f, 0f)
                           * Vector3.forward;
        trayFlat.y = 0f;
        if (trayFlat.sqrMagnitude > 1e-4f)
        {
            float headHeading = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
            float trayHeading = Mathf.Atan2(trayFlat.x, trayFlat.z) * Mathf.Rad2Deg;
            CardsConfig.TrayYaw.Value = Mathf.DeltaAngle(headHeading, trayHeading);
        }
        CardsConfig.TrayScale.Value = Mathf.Clamp(_root.localScale.x, 0.5f, 2f);
        VRLog.Info("Cards", $"Tray layout persisted: fwd {CardsConfig.TrayForward.Value:F2} m, " +
                            $"right {CardsConfig.TrayRight.Value:F2} m, down {CardsConfig.TrayDown.Value:F2} m, " +
                            $"yaw {CardsConfig.TrayYaw.Value:F0}°, scale {CardsConfig.TrayScale.Value:F2}×.");
    }

    /// <summary>
    /// Force re-placement next time the tray shows (mode re-entry) — FOLLOW mode
    /// only: a pinned tray stays exactly where the player left it (test #15).
    /// </summary>
    internal void InvalidatePlacement()
    {
        if (CardsConfig.TrayFollow.Value)
            _placed = false;
    }

    // ------------------------------------------------------------------ slots --

    internal VRCard? Occupant(int slot) => _occupants[slot];

    /// <summary>
    /// Slot anchor transform (test #19: HalfSelection docks the round cards into
    /// the SAME slots during action selection). Null until built or out of range —
    /// callers treat null as "no dock" instead of crashing the driver.
    /// </summary>
    internal Transform? SlotTransform(int slot) =>
        slot >= 0 && slot < _slots.Length ? _slots[slot] : null;

    internal int SlotOf(VRCard card)
    {
        if (_occupants[0] == card) return 0;
        if (_occupants[1] == card) return 1;
        return -1;
    }

    internal bool ContainsCard(VRCard card) => SlotOf(card) >= 0;

    /// <summary>
    /// Which slot would capture a card with the card center at <paramref name="cardPos"/>
    /// and the holding hand at <paramref name="handPos"/>? EITHER sample within the
    /// capture radius accepts — the pinch-grip held pose (P8) offsets the card center
    /// from the palm, so "hand over the slot" and "card over the slot" must both work
    /// (test #13). Returns -1 when outside both radii. With <paramref name="log"/> the
    /// full distance table and the verdict go to the log (drop-time diagnostics).
    /// </summary>
    internal int SlotNear(Vector3 cardPos, Vector3 handPos) =>
        SlotNear(cardPos, handPos, out _, out _, out _);

    /// <summary>
    /// Same test with the sampled distances exposed so the RELEASE path can log one
    /// concise line per real drop (test #14) — no logging in here.
    /// </summary>
    internal int SlotNear(Vector3 cardPos, Vector3 handPos, out float d0, out float d1, out float radius)
    {
        d0 = d1 = float.PositiveInfinity;
        radius = 0f;
        if (_root == null || !IsVisible)
            return -1;
        float scale = _root.lossyScale.x;
        radius = SlotCaptureRadius * scale;
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null)
                continue;
            float dist = Mathf.Min(
                Vector3.Distance(cardPos, slot.position),
                Vector3.Distance(handPos, slot.position));
            if (i == 0) d0 = dist; else d1 = dist;
            if (dist <= radius && dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ highlight --

    private readonly GameObject?[] _slotHighlights = new GameObject?[2];
    private int _highlightedSlot = -1;

    /// <summary>
    /// Glow frame behind each slot — shown while a HELD card is within snap range
    /// (test #13: telegraph exactly where the card will zap on release). Unlit
    /// bright gold so it reads emissive in the unlit void scenes.
    /// </summary>
    private void BuildSlotHighlights()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null || _slotHighlights[i] != null)
                continue;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SlotHighlight";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(slot, worldPositionStays: false);
            quad.transform.localScale = new Vector3(w * 1.24f, h * 1.24f, 1f);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.0035f); // behind card, rim past the frame
            Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null)
            {
                quad.GetComponent<MeshRenderer>().sharedMaterial =
                    new Material(shader) { color = new Color(1f, 0.85f, 0.3f, 0.95f) };
            }
            quad.SetActive(false);
            _slotHighlights[i] = quad;
        }
    }

    /// <summary>Show the snap-preview glow on one slot (-1 = none). No-ops unless it changes.</summary>
    internal void SetHighlightedSlot(int slot)
    {
        if (slot == _highlightedSlot)
            return;
        _highlightedSlot = slot;
        for (int i = 0; i < 2; i++)
        {
            GameObject? go = _slotHighlights[i];
            if (go != null && go.activeSelf != (i == slot))
                go.SetActive(i == slot);
        }
    }

    /// <summary>
    /// Visually park a card in a slot (game-state sync happens separately).
    /// <paramref name="announce"/> is true only on the REAL drop path — the
    /// game-state sync re-runs on every rebuild and must stay silent (test #14: the
    /// unconditional log here produced the "card placed" spam without user drops).
    /// A HELD card is never re-homed: SetHome re-parents, which used to yank the
    /// card out of the hand mid-grab and pull it onto the slot (the source of the
    /// phantom ACCEPTs — the card then sat within capture radius at the next
    /// unrelated grip release). Occupancy still updates; the release path homes it.
    /// </summary>
    internal void PlaceCard(VRCard card, int slot, bool instant = false, bool announce = false)
    {
        if (_slots[slot] == null)
            return;
        // A card can only occupy one slot.
        if (_occupants[0] == card) _occupants[0] = null;
        if (_occupants[1] == card) _occupants[1] = null;
        _occupants[slot] = card;
        if (!card.IsHeld)
        {
            card.gameObject.SetActive(true);
            card.SetHome(_slots[slot]!, Vector3.zero, Quaternion.identity, 1f, instant);
        }
        if (announce)
            VRLog.Info("Cards", $"Board: card placed in slot {slot + 1}.");
    }

    internal void RemoveCard(VRCard card)
    {
        int slot = SlotOf(card);
        if (slot >= 0)
        {
            _occupants[slot] = null;
            VRLog.Info("Cards", $"Board: card taken back from slot {slot + 1}.");
        }
    }

    internal void ClearSlots()
    {
        _occupants[0] = null;
        _occupants[1] = null;
    }

    /// <summary>
    /// Mirror the authoritative game state into the slots: slot 0 always shows the
    /// initiative card (<c>CCharacterClass.InitiativeAbilityCard</c>), slot 1 the
    /// other round card. Returns true when anything changed.
    /// </summary>
    internal bool SyncFromGameState(CardsHandUI hand, VRCardFactory factory)
    {
        if (hand.PlayerActor == null)
            return false;
        var round = hand.PlayerActor.CharacterClass.RoundAbilityCards;
        ScenarioRuleLibrary.CAbilityCard? initiative = hand.PlayerActor.CharacterClass.InitiativeAbilityCard;

        VRCard? want0 = null, want1 = null;
        for (int i = 0; i < round.Count && i < 2; i++)
        {
            AbilityCardUI? widget = FindWidget(hand, round[i]);
            if (widget == null)
                continue;
            VRCard card = factory.GetOrCreate(widget);
            bool isInitiative = initiative != null ? round[i] == initiative : i == 0;
            if (isInitiative && want0 == null)
                want0 = card;
            else if (want1 == null)
                want1 = card;
            else
                want0 ??= card;
        }

        // Re-place ONLY what changed (test #14): this sync runs on every rebuild —
        // dozens of times per selection phase — and unconditional re-placing both
        // re-parented held cards and spammed the log with phantom placements.
        bool changed = _occupants[0] != want0 || _occupants[1] != want1;
        if (_occupants[0] != want0)
        {
            _occupants[0] = null;
            if (want0 != null)
                PlaceCard(want0, 0);
        }
        if (_occupants[1] != want1)
        {
            _occupants[1] = null;
            if (want1 != null)
                PlaceCard(want1, 1);
        }
        return changed;
    }

    private static AbilityCardUI? FindWidget(CardsHandUI hand, ScenarioRuleLibrary.CAbilityCard card)
    {
        var cards = hand.cardsUI; // publicized private list
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].AbilityCard == card)
                return cards[i];
        }
        return null;
    }

    /// <summary>
    /// Laser pluck for slotted cards (P7): geometric rect test against the two
    /// occupants — same math as CardFan.TryRaycast. No allocations.
    /// </summary>
    internal bool TryRaycastCards(Vector3 origin, Vector3 direction, out VRCard? card,
        out Vector3 point, out float distance)
    {
        card = null;
        point = default;
        distance = float.PositiveInfinity;
        if (!IsVisible)
            return false;

        float halfW = CardsConfig.CardWidth.Value * 0.5f;
        float halfH = CardsConfig.CardHeight * 0.5f;
        for (int i = 0; i < 2; i++)
        {
            VRCard? c = _occupants[i];
            if (c == null || c.IsHeld || !c.gameObject.activeInHierarchy)
                continue;
            Transform t = c.transform;
            float denom = Vector3.Dot(direction, t.forward);
            if (denom < 1e-5f)
                continue;
            float dist = Vector3.Dot(t.position - origin, t.forward) / denom;
            if (dist <= 0f || dist >= distance)
                continue;
            Vector3 hit = origin + direction * dist;
            Vector3 local = t.InverseTransformPoint(hit);
            if (Mathf.Abs(local.x) > halfW || Mathf.Abs(local.y) > halfH)
                continue;
            card = c;
            point = hit;
            distance = dist;
        }
        return card != null;
    }

    // ------------------------------------------------------------------ status --

    // Last shown badge state (int key, not string): -2 = none ("-"), -1 = long rest
    // ("99"), else the initiative value. Rebuilding the string only on CHANGE avoids
    // a per-frame ToString allocation AND a per-frame TMP text assignment — every
    // rewrite re-triggers TMP's auto-size layout, which made the badge flicker
    // against its plate (test #13).
    private int _badgeState = int.MinValue;

    // Last shown round number (change-gated like the badge; int.MinValue = never).
    private int _roundShown = int.MinValue;

    /// <summary>Update round readout, badge, confirm/undo button states + labels (each frame while visible; cheap).</summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        // Round readout (test #18): the PhaseBanner world conversion is GONE — the
        // round number lives on the dashboard instead, read from the same state the
        // banner showed (CardsGameApi.RoundNumber). Change-gated: TMP rewrites
        // re-trigger auto-size layout (the badge flicker lesson, test #13).
        if (_roundLabel != null)
        {
            int round = CardsGameApi.RoundNumber();
            if (round != _roundShown)
            {
                _roundShown = round;
                string text;
                if (round <= 0)
                {
                    text = "-";
                }
                else
                {
                    // The banner's own text: GUI_START_ROUND_BANNER is "Runde {0}"
                    // (PhaseBannerHandler.ShowStartRound). Guard the Format — a
                    // malformed localization must not kill the status tick.
                    try
                    {
                        text = string.Format(
                            CardsGameApi.Localize("GUI_START_ROUND_BANNER", "Round {0}"), round);
                    }
                    catch (System.FormatException)
                    {
                        text = $"Round {round}";
                    }
                }
                _roundLabel.text = text;
                VRLog.Info("Cards", $"Board: round readout → '{text}'.");
            }
        }

        if (_badge == null)
            return;

        int state = -2;
        bool canSwap = false;
        bool ready = false;
        if (hand != null && hand.PlayerActor != null)
        {
            if (CardsGameApi.IsLongRestSelected(hand))
            {
                state = -1; // long rest initiative (99) by game rule
            }
            else
            {
                ScenarioRuleLibrary.CAbilityCard? initiative = CardsGameApi.InitiativeCard(hand);
                if (initiative != null)
                    state = CardsGameApi.InitiativeValue(initiative);
                canSwap = hand.PlayerActor.CharacterClass.RoundAbilityCards.Count == 2;
            }
            ready = CardsGameApi.IsSelectionReady(hand);
        }

        if (state != _badgeState)
        {
            _badgeState = state;
            _badge.text = state switch { -2 => "-", -1 => "99", _ => state.ToString() };
        }
        if (_badgeZone != null)
            _badgeZone.SwapEnabled = canSwap;

        if (_confirm != null)
        {
            bool canConfirm = hand != null && CardsGameApi.CanConfirm();
            _confirm.SetState(canConfirm, accent: ready && canConfirm);
            _confirm.SetLabel(hand != null ? CardsGameApi.ConfirmLabel() : "-");
        }
        if (_undo != null)
        {
            bool canUndo = hand != null && CardsGameApi.CanUndo();
            _undo.SetState(canUndo, accent: false);
            _undo.SetLabel(hand != null ? CardsGameApi.UndoLabel() : "-");
        }
    }

    // ------------------------------------------------------------------ build --

    // Board layout constants (local meters; -Z = element/viewer side).
    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;
    private const float SlotSpacing = 0.155f; // between slot centers
    private const float RestZoneX = -0.245f;
    private const float ButtonZoneX = 0.235f;

    private void BuildProceduralBoard()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "TrayBoard";
        Object.Destroy(board.GetComponent<Collider>());
        board.transform.SetParent(_root, worldPositionStays: false);
        board.transform.localScale = new Vector3(BoardW, BoardH, 0.012f);
        board.transform.localPosition = new Vector3(0f, 0f, 0.010f);
        Tint(board, new Color(0.16f, 0.13f, 0.10f));

        // Subtle raised edge so the board reads as a desk/tray, not a floating slab.
        var lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lip.name = "TrayLip";
        Object.Destroy(lip.GetComponent<Collider>());
        lip.transform.SetParent(_root, worldPositionStays: false);
        lip.transform.localScale = new Vector3(BoardW + 0.015f, 0.02f, 0.018f);
        lip.transform.localPosition = new Vector3(0f, -BoardH * 0.5f - 0.002f, 0.008f);
        Tint(lip, new Color(0.11f, 0.09f, 0.07f));

        for (int i = 0; i < 2; i++)
        {
            var slot = new GameObject($"Slot{i + 1}").transform;
            slot.SetParent(_root, worldPositionStays: false);
            slot.localPosition = new Vector3((i == 0 ? -0.5f : 0.5f) * SlotSpacing, 0.015f, 0f);
            _slots[i] = slot;

            var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frame.name = "Frame";
            Object.Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(slot, worldPositionStays: false);
            frame.transform.localScale = new Vector3(w * 1.12f, h * 1.12f, 1f);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.003f); // behind the card, in front of the board
            Tint(frame, i == 0 ? new Color(0.55f, 0.45f, 0.22f) : new Color(0.30f, 0.29f, 0.27f));

            var inner = GameObject.CreatePrimitive(PrimitiveType.Quad);
            inner.name = "FrameInner";
            Object.Destroy(inner.GetComponent<Collider>());
            inner.transform.SetParent(slot, worldPositionStays: false);
            inner.transform.localScale = new Vector3(w * 1.04f, h * 1.04f, 1f);
            inner.transform.localPosition = new Vector3(0f, 0f, 0.0025f);
            Tint(inner, new Color(0.12f, 0.10f, 0.08f));
        }

        // Rest zone: backdrop + anchors (RestControls builds the tokens).
        var restBack = GameObject.CreatePrimitive(PrimitiveType.Quad);
        restBack.name = "RestZone";
        Object.Destroy(restBack.GetComponent<Collider>());
        restBack.transform.SetParent(_root, worldPositionStays: false);
        restBack.transform.localScale = new Vector3(0.105f, 0.24f, 1f);
        restBack.transform.localPosition = new Vector3(RestZoneX, -0.01f, 0.003f);
        Tint(restBack, new Color(0.12f, 0.11f, 0.10f));

        // Caption box = the rest plate's width (0.105) — the localized header shrinks
        // to fit instead of spilling over the slots (TmpFit, test #12).
        AddCaption(_root!, new Vector3(RestZoneX, 0.105f, -0.004f),
            CardsGameApi.Localize("GUI_SHORT_REST", "REST"), new Color(0.85f, 0.8f, 0.7f),
            maxUpper: true, width: 0.10f, height: 0.026f, maxFontSize: 0.30f);

        _shortRestAnchor = new GameObject("ShortRestToken").transform;
        _shortRestAnchor.SetParent(_root, worldPositionStays: false);
        _shortRestAnchor.localPosition = new Vector3(RestZoneX, 0.035f, -0.010f);

        _longRestAnchor = new GameObject("LongRestToken").transform;
        _longRestAnchor.SetParent(_root, worldPositionStays: false);
        _longRestAnchor.localPosition = new Vector3(RestZoneX, -0.055f, -0.010f);
    }

    private void BuildSlotLabels()
    {
        if (_root == null || _slots[0] == null || _slots[1] == null)
            return;
        float h = CardsConfig.CardHeight;
        // Slot 0 is ALWAYS the initiative slot (CardsDriver reconciles the game state
        // to the physical order) — label it so the marking is unambiguous.
        // Caption box stays inside one slot pitch (SlotSpacing 0.155) so neighboring
        // captions can never collide; "INITIATIVE" (and longer localizations) shrink
        // to a single line inside it (TmpFit, test #12). The captions are children
        // of the SCALED slots (test #18): the box metrics divide by SlotScale so the
        // EFFECTIVE caption size stays as designed — an inherited 1.3× would push
        // the 0.14 box past the 0.155 pitch and collide the neighboring caption.
        AddCaption(_slots[0]!, new Vector3(0f, -h * 0.62f, -0.004f),
            CardsGameApi.Localize("GUI_INITIATIVE", "INITIATIVE"), new Color(1f, 0.9f, 0.6f),
            maxUpper: true, width: 0.14f / SlotScale, height: 0.024f / SlotScale,
            maxFontSize: 0.28f / SlotScale);
        AddCaption(_slots[1]!, new Vector3(0f, -h * 0.62f, -0.004f),
            "2", new Color(0.75f, 0.73f, 0.7f),
            maxUpper: true, width: 0.14f / SlotScale, height: 0.024f / SlotScale,
            maxFontSize: 0.28f / SlotScale);
    }

    private void BuildBadge()
    {
        if (_root == null || _slots[0] == null)
            return;
        float h = CardsConfig.CardHeight;

        var badgeGo = new GameObject("InitiativeBadge");
        badgeGo.transform.SetParent(_slots[0], worldPositionStays: false);
        badgeGo.transform.localPosition = new Vector3(0f, h * 0.68f, -0.004f); // TMP reads from -Z (viewer side)

        // Gold disc so the number reads as a marker, not floating text.
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "BadgeDisc";
        Object.Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(badgeGo.transform, worldPositionStays: false);
        disc.transform.localScale = new Vector3(0.042f, 0.003f, 0.042f);
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // Disc front face 1.5 mm BEHIND the number (cylinder half-height 0.003):
        // at z=0.003 the face sat exactly on the text plane — z-fighting made the
        // badge number flicker/"clip" (test #13).
        disc.transform.localPosition = new Vector3(0f, 0f, 0.0045f);
        Tint(disc, new Color(0.5f, 0.42f, 0.2f));

        _badge = badgeGo.AddComponent<TextMeshPro>();
        _badge.text = "-";
        _badgeState = -2; // keep the change-detection key in sync after a rebuild
        _badge.alignment = TextAlignmentOptions.Center;
        _badge.color = new Color(1f, 0.95f, 0.8f);
        // The number must sit ON the 0.042 m disc (was fontSize 1.2 = a 0.12 m line
        // dwarfing the marker, test #12) — single line, fitted to the disc.
        Core.TmpFit.Fit(_badge, 0.05f, 0.036f, maxFontSize: 0.30f, wrap: false);

        // Poke/laser the badge to swap initiative (same as the 2D badge click).
        var zoneGo = new GameObject("SwapZone");
        zoneGo.transform.SetParent(badgeGo.transform, worldPositionStays: false);
        var box = zoneGo.AddComponent<BoxCollider>();
        box.size = new Vector3(0.08f, 0.045f, 0.02f);
        box.isTrigger = true;
        _badgeZone = zoneGo.AddComponent<InitiativeBadgeZone>();
        _badgeZone.Owner = this;
        RegisterLaserTarget(box, _badgeZone);
    }

    private void BuildButtons(Transform? confirmAnchor, Transform? undoAnchor)
    {
        if (_root == null)
            return;

        Transform confirmParent = confirmAnchor != null ? confirmAnchor : NewAnchor("ConfirmButton", new Vector3(ButtonZoneX, 0.045f, -0.006f));
        Transform undoParent = undoAnchor != null ? undoAnchor : NewAnchor("UndoButton", new Vector3(ButtonZoneX, -0.06f, -0.006f));

        _confirm = BoardButton.Create(confirmParent, new Vector2(0.115f, 0.06f),
            new Color(0.22f, 0.52f, 0.25f), "CONFIRM",
            () => ConfirmRequested?.Invoke());
        _confirm.DisabledReason = CardsGameApi.DescribeConfirmGate; // built only on rejection
        RegisterLaserTarget(_confirm.Collider!, _confirm);

        _undo = BoardButton.Create(undoParent, new Vector2(0.09f, 0.042f),
            new Color(0.45f, 0.32f, 0.2f), "UNDO",
            () => UndoRequested?.Invoke());
        _undo.DisabledReason = CardsGameApi.DescribeUndoGate;
        RegisterLaserTarget(_undo.Collider!, _undo);
    }

    private Transform NewAnchor(string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(_root, worldPositionStays: false);
        t.localPosition = localPos;
        return t;
    }

    private static void AddCaption(Transform parent, Vector3 localPos, string text,
        Color color, bool maxUpper, float width, float height, float maxFontSize)
    {
        var go = new GameObject("Caption");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = maxUpper ? text.ToUpperInvariant() : text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        // Single-line captions: long localizations shrink to fit the given box
        // instead of overflowing across the board (TmpFit, test #12).
        Core.TmpFit.Fit(tmp, width, height, maxFontSize, wrap: false);
    }

    private static void Tint(GameObject go, Color color)
    {
        var renderer = go.GetComponent<MeshRenderer>();
        Shader? shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");
        if (shader != null)
            renderer.sharedMaterial = new Material(shader) { color = color };
    }

    private static Transform? FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>Poke/laser target on the initiative badge → initiative swap.</summary>
    private sealed class InitiativeBadgeZone : PokeableBehaviour
    {
        internal PlayTray? Owner;
        internal bool SwapEnabled;

        public override void OnPoke(VRHand hand)
        {
            if (!SwapEnabled || Owner == null)
                return;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: initiative badge pressed ({hand.Side}).");
            Owner.SwapRequested?.Invoke();
        }

        public override void OnPokeEnter(VRHand hand)
        {
            if (SwapEnabled)
                hand.SendHaptic(HapticPreset.HoverTick);
        }
    }

    /// <summary>
    /// One physical board button: base plate + travelling cap + label. Poke (P2
    /// registry) and laser (tray LaserTargets) both land in <see cref="OnPoke"/>.
    /// Cap presses in ~4 mm on click and springs back (transform anim, no Animator).
    /// </summary>
    internal sealed class BoardButton : PokeableBehaviour
    {
        private System.Action? _onClick;
        private Material? _capMaterial;
        private TextMeshPro? _label;
        private Transform? _cap;
        private Color _accentColor;
        private bool _enabledState;
        private bool _accent;
        private float _press; // 0..1 press animation

        internal Collider? Collider { get; private set; }

        private static readonly Color DisabledColor = new(0.24f, 0.23f, 0.22f);
        private static readonly Color IdleColor = new(0.35f, 0.34f, 0.32f);

        internal static BoardButton Create(Transform anchor, Vector2 size, Color accent,
            string fallbackLabel, System.Action onClick)
        {
            var go = new GameObject($"BoardButton_{fallbackLabel}");
            go.transform.SetParent(anchor, worldPositionStays: false);

            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            Object.Destroy(basePlate.GetComponent<Collider>());
            basePlate.transform.SetParent(go.transform, worldPositionStays: false);
            basePlate.transform.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 0.006f);
            basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            Tint(basePlate, new Color(0.10f, 0.09f, 0.08f));

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "Cap";
            Object.Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(go.transform, worldPositionStays: false);
            cap.transform.localScale = new Vector3(size.x, size.y, 0.008f);
            cap.transform.localPosition = new Vector3(0f, 0f, -0.004f);

            Material? capMaterial = null;
            Shader? shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                capMaterial = new Material(shader) { color = DisabledColor };
                cap.GetComponent<MeshRenderer>().sharedMaterial = capMaterial;
            }

            // Label parented to the (unit-scale) button root, floating just in front
            // of the cap — a child of the non-uniformly scaled cap would distort.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.010f); // viewer side (-Z)
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = fallbackLabel;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            // Fit inside the cap face: localized CONFIRM/UNDO strings (SetLabel
            // mirrors the game's texts) shrink/wrap inside the button instead of
            // spilling over its edges (TmpFit, test #12).
            Core.TmpFit.Fit(tmp, size.x * 0.92f, size.y * 0.85f, maxFontSize: 0.40f);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 0.02f);
            box.center = new Vector3(0f, 0f, -0.004f);
            box.isTrigger = true;

            var button = go.AddComponent<BoardButton>();
            button._onClick = onClick;
            button._capMaterial = capMaterial;
            button._label = tmp;
            button._cap = cap.transform;
            button._accentColor = accent;
            button.Collider = box;
            return button;
        }

        /// <summary>
        /// Built ONLY when a press is rejected — explains the disabled state
        /// (e.g. the CanConfirm gate inputs). Wired by <see cref="BuildButtons"/>.
        /// </summary>
        internal System.Func<string>? DisabledReason;

        internal void SetState(bool enabled, bool accent)
        {
            if (_enabledState == enabled && _accent == accent)
                return;
            _enabledState = enabled;
            _accent = accent;
            UpdateColor();
        }

        internal void SetLabel(string text)
        {
            if (_label != null && _label.text != text)
                _label.text = text;
        }

        private void UpdateColor()
        {
            if (_capMaterial == null)
                return;
            Color color = !_enabledState ? DisabledColor : _accent ? _accentColor : IdleColor;
            if (_capMaterial.color != color)
                _capMaterial.color = color;
        }

        private void Update()
        {
            if (_cap == null || _press <= 0f)
                return;
            _press = Mathf.MoveTowards(_press, 0f, Time.deltaTime * 6f);
            // Cap travel: 4 mm into the board at full press.
            Vector3 pos = _cap.localPosition;
            pos.z = -0.004f + 0.004f * _press;
            _cap.localPosition = pos;
        }

        /// <summary>Poke path (P2 PokeInteractor — geometric fingertip test against this collider).</summary>
        public override void OnPoke(VRHand hand) => Press(hand, "poke");

        /// <summary>
        /// Single press entry for BOTH input paths (test #14): every attempt is
        /// logged with its source and, when rejected, the exact gate state — a
        /// silent dead button can no longer happen.
        /// </summary>
        internal void Press(VRHand hand, string source)
        {
            if (!_enabledState)
            {
                VRLog.Info("Cards", $"Board: {name} press REJECTED (source={source}, {hand.Side}) — " +
                                    $"disabled: {(DisabledReason != null ? DisabledReason() : "no reason hook")}.");
                return;
            }
            _press = 1f;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: {name} pressed (source={source}, {hand.Side}).");
            _onClick?.Invoke();
        }

        public override void OnPokeEnter(VRHand hand)
        {
            if (_enabledState)
                hand.SendHaptic(HapticPreset.HoverTick);
        }
    }
}
