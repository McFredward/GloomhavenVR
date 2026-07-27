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
///   the REST zone (short/long-rest board buttons built by <see cref="RestControls"/>;
///   the REAL native short-rest widget docks over the short button when available),
///   with the element infusion board (<see cref="ElementMount"/>) docked directly
///   below it (test #20 — no longer a free-floating world panel),
/// - CENTER: two large card slots baked into the fixed asset as physical recesses
///   (slot 0 = initiative; drop to place, grab to take back, physical swap =
///   initiative swap — the redundant numbered badge was removed in test #24 item 3,
///   the initiative already reads on the docked track); the modal single-card pick
///   flows now home their candidate into the LEFT recess (Slot1) instead of a
///   centre field (test #28) — the old <see cref="BuildPickField"/> centre field is
///   retained but unused. A steady pulsing "wanted slot" hint marks the recess the
///   game is waiting for (<see cref="SetWantedSlots"/>),
/// - RIGHT: CONFIRM (drives the game's own Ready button path), UNDO and a settings
///   gear; the PIN follow-toggle sits on the bottom-right frame corner,
/// - RIGHT EDGE (off-board, mirror of the objectives dock): the discard/burnt pile
///   stacks on <see cref="PileMount"/> (test #21 A, built by <see cref="PileViewer"/>),
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
internal sealed class PlayTray : WorldUI.IPanelGrabOwner
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
    /// home scale back under the slot. (The slot captions that used to compensate the
    /// inherited scale are GONE — test #29 removed BuildSlotLabels outright; see the note
    /// above BuildButtons.) SlotCaptureRadius stays as-is —
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

    /// <summary>
    /// The shared ability-card backing prefab (bundle <c>CardBacking.prefab</c>) the
    /// <see cref="VRCardFactory"/> hands to <see cref="VRCard.Build"/> — captured in
    /// <see cref="EnsureBuilt"/> so the item chips (<see cref="ItemsPile.ItemChip"/>) can reuse the
    /// SAME card back the ability cards wear instead of a plain black slab. Null until the board is
    /// built, or when the bundle prefab is unavailable (the item chip then falls back to the
    /// procedural CardMesh backing, the same fallback VRCard uses).
    /// </summary>
    internal GameObject? CardBackingPrefab { get; private set; }

    private Transform? _root;
    private Transform? _anchorParent;
    private Transform? _initiativeMount;
    private Transform? _objectivesMount;
    private Transform? _elementMount;
    private Transform? _clusterMount;
    private Transform? _decisionMount;
    private Transform? _pileMount;
    private Transform? _activeMount;
    private Transform?[] _slots = new Transform?[2];
    private Transform? _shortRestAnchor;
    private Transform? _longRestAnchor;
    private readonly VRCard?[] _occupants = new VRCard?[2];

    private TextMeshPro? _roundLabel;
    private BoardButton? _confirm;
    private BoardButton? _undo;
    private Transform? _confirmAnchor;
    private Transform? _undoAnchor;

    // Items rework (requirement 3): the ITEM-USE clip-in slot — a card-sized recess UNDER the
    // board next to the Confirm/Undo decision buttons. Built once (hidden), shown live by
    // ItemsPile only while the local player holds a usable item card on their own turn; dropping
    // that card into the recess USES it. Its glow pulses like the wanted-slot hint.
    private Transform? _itemUseSlot;
    private Material? _itemUseSlotGlow;
    // Requirement 6 (clip-in decision): the CONFIRM (USE) button shown beside the item-use slot while a
    // held usable item card is CLIPPED into the slot awaiting a decision. Poking/laser-clicking it uses
    // the item (ItemsPile supplies the callback); the CANCEL is grabbing the card back out, so there is
    // no cancel button. Hidden by default; child of the slot so it inherits the slot's pose/visibility.
    private BoardButton? _itemUseConfirm;
    private System.Action? _itemUseConfirmAction;
    private bool _placed;
    private bool _wantVisible;
    private bool _placementDeferLogged;

    internal bool IsVisible => _root != null && _root.gameObject.activeSelf;

    internal Transform? ShortRestAnchor => _shortRestAnchor;
    internal Transform? LongRestAnchor => _longRestAnchor;
    internal Transform? Root => _root;

    /// <summary>
    /// Mount for the REAL Continue/Confirm (ReadyButton) native dock (test #23 item
    /// 4): the exact anchor the mod CONFIRM button occupied, so the docked native
    /// widget keeps the familiar right-column position. Pose-follow, centered origin
    /// (canvas faces the viewer, the DecisionMount convention). Null until built.
    /// </summary>
    internal Transform? ContinueMount => _confirmAnchor;

    /// <summary>Mount for the REAL Undo (UndoButton) native dock — the old UNDO anchor (test #23 item 4).</summary>
    internal Transform? UndoDockMount => _undoAnchor;

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
    /// Mount for the converted element infusion board (test #20), LEFT column below
    /// the objectives dock — right-center origin growing left, same convention as
    /// <see cref="ObjectivesMount"/>. Null until built.
    /// </summary>
    internal Transform? ElementMount => _elementMount;

    /// <summary>
    /// Mount for the WorldUI turn-flow ButtonCluster (Undo | Ready | Skip — the
    /// game's live mid-turn buttons incl. "skip movement"/"end turn" states), docked
    /// beside the right-hand pads (test #19). Since the lag fix the cluster is
    /// RIGIDLY parented under <see cref="Root"/> (it is MOD-owned geometry, so the
    /// mount-seam reversibility rule for game-owned canvases does not apply; the
    /// cluster detects its own destruction on a tray teardown and rebuilds) — this
    /// mount now only supplies the docked rotation/scale frame at attach time.
    /// Null until built.
    /// </summary>
    internal Transform? ButtonClusterMount => _clusterMount;

    /// <summary>
    /// SHARED DECISION DOCK (test #22): the reserved zone where the REAL interactive
    /// widgets of ANY in-scenario decision/confirmation prompt dock while it is open
    /// — the take-damage burn choice (TakeDamagePanel: two toggles + a button), the
    /// burn-confirm dialog (UIManager.dialogPopup: its option-button row), and any
    /// prompt added later to <see cref="WorldUI.ModalFallback.DecisionDock"/>. Test
    /// #21 docked the take-damage row CENTERED OVER THE SLOT ZONE, which flew the
    /// buttons over the two parked cards (the test-#22 complaint); this mount instead
    /// hangs BELOW the board, clear of the cards and all bottom furniture. CENTERED
    /// origin (the row centers on the mount). Null until built. Collision math in
    /// <see cref="BuildMounts"/>.
    /// </summary>
    internal Transform? DecisionMount => _decisionMount;

    /// <summary>
    /// Mount for the pile viewer stacks (test #21), RIGHT edge — left-center origin
    /// growing right, the mirror of the <see cref="ObjectivesMount"/> convention.
    /// Unlike the other mounts this one hosts mod-owned children directly
    /// (<see cref="PileViewer"/> parents its stacks under it — no live game UI, so
    /// the reversibility rule that forbids re-parenting does not apply here).
    /// Null until built.
    /// </summary>
    internal Transform? PileMount => _pileMount;

    /// <summary>
    /// Feature 6 (ACTIVE CARDS area): mount for the permanently-visible active-ability
    /// card column (<see cref="ActivePileViewer"/>), docked further off the RIGHT edge —
    /// just past the discard/burnt pile stacks (see the collision math in
    /// <see cref="BuildMounts"/>). Left-center origin growing right, the same convention
    /// as <see cref="PileMount"/>, and — like the pile mount — it hosts mod-owned
    /// factory cards directly (no live game UI re-parenting). Null until built.
    /// </summary>
    internal Transform? ActiveMount => _activeMount;

    /// <summary>
    /// Distance (tray-local meters) the active-card mount sits to the RIGHT of the pile mount.
    /// Raised from 0.10 to 0.17: at 0.10 the active column crowded the discard/burn pile stacks
    /// (worst-case pile right edge ≈ 0.408 tray-local), so this opens a clear gap between them.
    /// </summary>
    internal const float ActiveMountOffsetX = 0.17f;

    /// <summary>Stack center X in PileMount-local meters (see BuildMounts collision math).</summary>
    internal const float PileStackOffsetX = 0.05f;

    /// <summary>Vertical distance between the two stack centers, PileMount-local meters.</summary>
    internal const float PileStackSpacing = 0.116f;

    /// <summary>Target panel width at the initiative mount, tray-local meters (× mount lossyScale).</summary>
    internal const float InitiativeMountWidth = BoardW;

    /// <summary>Max panel height at the initiative mount, tray-local meters.</summary>
    internal const float InitiativeMountMaxHeight = 0.14f;

    /// <summary>Target panel width at the objectives mount, tray-local meters.</summary>
    internal const float ObjectivesMountWidth = 0.26f;

    /// <summary>Max panel height at the objectives mount, tray-local meters.</summary>
    internal const float ObjectivesMountMaxHeight = 0.32f;

    /// <summary>Target panel width at the element mount, tray-local meters (same left column as the objectives).</summary>
    internal const float ElementMountWidth = ObjectivesMountWidth;

    /// <summary>Max panel height at the element mount, tray-local meters.</summary>
    internal const float ElementMountMaxHeight = 0.12f;

    /// <summary>
    /// Target width of the decision dock, tray-local meters (× mount lossyScale). A
    /// decision is a short-and-wide BUTTON ROW, so the budget is wider and flatter
    /// than the old over-slot overlay (0.34 × 0.16). At 0.42 the row spans x ±0.21
    /// — the collision budget cleared in <see cref="BuildMounts"/>.
    /// </summary>
    internal const float DecisionMountWidth = 0.42f;

    /// <summary>Max height of the decision dock, tray-local meters.</summary>
    internal const float DecisionMountMaxHeight = 0.12f;

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

    /// <summary>
    /// Test #19 accident window: ANY CONFIRM activation (poke and laser alike) is
    /// suppressed this long after a card was dropped into / plucked from a slot —
    /// the exact gesture that brushed CONFIRM in the log. CardsDriver arms it via
    /// <see cref="NoteSlotActivity"/> on every real drop/pluck (never on the silent
    /// game-state sync placements).
    /// </summary>
    private const float ConfirmGuardSeconds = 0.7f;

    private float _lastSlotActivity = float.NegativeInfinity;

    /// <summary>True once the bundled board's real MeshCollider is registered as a laser
    /// target (DEFECT 2) — then <see cref="BuildBoardSurface"/> skips its synthetic plane.</summary>
    private bool _boardColliderRegistered;

    /// <summary>
    /// The bundled board's real MeshCollider(s), stored in <see cref="EnsureBuilt"/> (they are
    /// also pushed into <see cref="LaserTargets"/>). <see cref="SeatOnBoardFace"/> raycasts these
    /// from the player side at each widget's own XY so mod-built widgets seat PROUD of the TRUE
    /// local top surface (the raised rim/ornaments), not the flat slot-floor plane — the runtime
    /// equivalent of BuildBoard.cs's per-anchor projection. Empty for the procedural fallback
    /// board (no mesh), where <see cref="SeatOnBoardFace"/> falls back to the slot-plane projection.
    /// </summary>
    private readonly List<Collider> _boardColliders = new(2);

    /// <summary>The board's functional-face frame in <c>_root</c>-LOCAL space (a child's
    /// localRotation = this reproduces the same world facing the bundle slot anchors get:
    /// readable −Z toward the player). Derived from the bundle anchor axes in
    /// <see cref="EnsureBuilt"/>. Identity for the procedural fallback board (its
    /// localPositions already assume that convention).
    /// ITEM 2 FIX: previously this stored a WORLD LookRotation and was applied as a child
    /// localRotation — but <c>_root</c> is parented under the rig/hands root
    /// (CardsDriver.AnchorParent) and is NOT identity in world, so the readout/gear were
    /// double-rotated onto the board's BACK. It is now stored in <c>_root</c>-local space.</summary>
    private Quaternion _boardFaceFrame = Quaternion.identity;

    /// <summary>ITEM 2 diagnostic: the board functional-face outward (away-from-viewer, +Z)
    /// normal in WORLD space at build time (nF). −this points toward the player.</summary>
    private Vector3 _boardFaceNormalWorld = Vector3.forward;

    /// <summary>One-shot ground-truth log of each board element's facing after placement.</summary>
    private bool _boardDiagLogged;

    /// <summary>Arm the accidental-confirm guard (called by CardsDriver on real slot drops/plucks only).</summary>
    internal void NoteSlotActivity() => _lastSlotActivity = Time.unscaledTime;

    /// <summary>
    /// Seconds left of the confirm suppression window (≤0 = free). Wired as the mod
    /// CONFIRM ActivationGuard, and read by <see cref="WorldUI.Surfaces.TrayControlDockSurface"/>
    /// to gate the docked NATIVE Continue button the same way (test #19 / #23 item 4).
    /// </summary>
    internal float ConfirmGuardRemaining() => _lastSlotActivity + ConfirmGuardSeconds - Time.unscaledTime;

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

    /// <summary>
    /// Remove a laser target by collider (items rework): the item-browse chips register their
    /// colliders as laser targets while the fan is open and unregister on close/rebuild, so the
    /// transient chips never leak stale entries into <see cref="LaserTargets"/>. No-op if absent.
    /// </summary>
    internal void UnregisterLaserTarget(Collider collider)
    {
        if (collider == null)
            return;
        LaserTargets.RemoveAll(t => ReferenceEquals(t.Collider, collider));
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
        // Capture the shared card backing prefab so the item chips can wear the SAME card back the
        // ability cards do (ItemsPile.ItemChip reads PlayTray.Current.CardBackingPrefab). Null-safe:
        // the item chip falls back to the procedural CardMesh backing when the bundle prefab is absent.
        CardBackingPrefab = factory.GetBackingPrefab();
        _boardDiagLogged = false; // re-log the board-facing ground truth for this fresh board

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

            // The bundled PlayTray anchors carry a 90° twist from the FBX empty export
            // (their local -Z ends up along the board's in-plane axis instead of out of
            // the decorated face), which stood every attached element — cards, slot
            // frames/highlights, the confirm/undo buttons — UPRIGHT on the board. Re-align
            // all anchors to ONE board frame derived from their own positions: -Z points
            // out of the decorated face (the card/element facing), +Y up the short axis.
            // Children attach with worldPositionStays:false, so they inherit this frame.
            if (_slots[0] != null && _slots[1] != null
                && _shortRestAnchor != null && _longRestAnchor != null)
            {
                Vector3 uF = (_slots[1]!.position - _slots[0]!.position).normalized;              // long axis
                Vector3 sF = (_shortRestAnchor!.position - _longRestAnchor!.position).normalized; // short axis
                Vector3 nF = Vector3.Cross(uF, sF).normalized;                                    // board face normal
                Vector3 vF = Vector3.Cross(nF, uF).normalized;                                    // orthonormal short axis
                // Elements face the anchor's local -Z. VERIFIED BY OFFSCREEN RENDER (board
                // under the mod's real lectern tilt, player POV): LookRotation(nF, vF) lands
                // the card markers squarely in the two slot recesses on the player-facing
                // functional face. So forward = +nF => -Z = -nF = out of that face. (Do not
                // flip to -nF: that hides the cards on the decorative back.)
                // ITEM 2 FIX: the mod-BUILT elements (round readout, settings gear, follow
                // toggle, procedural button fallbacks) are children of _root and take this
                // frame as their localRotation; the bundle anchors below are aligned in
                // WORLD space. _root is NOT identity in world — it is parented under the
                // rig/hands root (CardsDriver.AnchorParent) which carries the diorama
                // rotation — so a world LookRotation applied as a child localRotation was
                // double-rotated by _root.rotation and put "Runde N"/the gear on the board
                // BACK (the prior fix "did not work"). Store the face frame in _root-LOCAL
                // space so a child localRotation reproduces exactly the world facing the
                // slots get, and stays consistent as PlaceAtHead later tilts _root.
                Quaternion faceWorld = Quaternion.LookRotation(nF, vF);
                _boardFaceNormalWorld = nF;
                _boardFaceFrame = Quaternion.Inverse(_root.rotation) * faceWorld;
                foreach (Transform? a in new[] { _slots[0], _slots[1], _shortRestAnchor, _longRestAnchor, confirmAnchor, undoAnchor })
                    if (a != null)
                        a.rotation = faceWorld;
            }

            // DEFECT 2: the bundled board now ships a MeshCollider (BuildBoard). Register
            // it as a laser target so the index-finger beam STOPS on the REAL board
            // surface instead of passing through it. Collider.Raycast is geometric and
            // layer-independent, so the non-convex mesh works directly. This supersedes
            // the synthetic flat BoardSurface plane (kept only for the procedural
            // fallback, which has no mesh) — see BuildBoardSurface.
            foreach (MeshCollider mc in visual.GetComponentsInChildren<MeshCollider>(true))
            {
                var t = mc.gameObject.GetComponent<BoardSurfaceTarget>();
                if (t == null) t = mc.gameObject.AddComponent<BoardSurfaceTarget>();
                RegisterLaserTarget(mc, t);
                _boardColliders.Add(mc); // CORE FIX: SeatOnBoardFace raycasts these to seat widgets proud
                _boardColliderRegistered = true;
            }

            // PART B: the old unreliable raycast reseat (ReseatProud) is GONE. Every attached
            // element (rest discs, Confirm/Undo, gear/toggle/readout) now seats at anchor +
            // PER-BOARD offset with a predictable proud Z — no more −50 mm surprises. The bundle
            // anchors stay at their authored positions; the per-board offset supplies the proud
            // depth toward the player (RestControls / BuildButtons / NewAnchor / BuildRoundReadout).
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

        BuildSlotHighlights();
        BuildWantedHighlights();
        BuildButtons(confirmAnchor, undoAnchor);
        BuildItemUseSlot();
        BuildHandle();
        BuildDashboardControls();
        BuildRoundReadout();
        BuildMounts();
        BuildPickField();
        BuildBoardSurface();
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
        // PART B: seat the readout at a FIXED small proud depth toward the player (predictable,
        // depth-correct) instead of the old unreliable raycast — it never floats off the board now.
        // Items 4/6: base + per-board ReadoutOffset (debug-menu tunable; seeded 0 → Oak unchanged).
        readoutGo.transform.localPosition = ReadoutBase + CardsConfig.ReadoutOffset(CardsConfig.CurrentBoard).Value;
        readoutGo.transform.localRotation = _boardFaceFrame; // face the player like the slots ("Runde N" was on the back)

        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        Object.Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(readoutGo.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(0.13f, 0.036f, 1f);
        plate.transform.localPosition = new Vector3(0f, 0f, 0.006f); // behind the text, toward the board body
        // CORE FIX: the readout now seats PROUD of the true surface (SeatOnBoardFace raycast), so it is
        // depth-correct — drop the RenderOnTop shine-through. Lit (Standard) plate so it shades like a
        // real object instead of the flat unlit Overlay.
        Tint(plate, new Color(0.12f, 0.11f, 0.10f));

        _roundLabel = readoutGo.AddComponent<TextMeshPro>();
        _roundLabel.text = "-";
        _roundShown = int.MinValue; // keep the change-detection key in sync after a rebuild
        _roundLabel.alignment = TextAlignmentOptions.Center;
        _roundLabel.color = new Color(1f, 0.9f, 0.6f);
        // Single line fitted to the plate ("Runde 12" and longer localizations shrink).
        Core.TmpFit.Fit(_roundLabel, 0.12f, 0.028f, maxFontSize: 0.32f, wrap: false);
        // No RenderOnTop: the readout is depth-correct now (seated proud). The TMP text draws
        // in the transparent queue after the opaque plate, and sits proud of it (text z 0 vs
        // plate z +0.006 toward the board), so the label reads over its own dark backing plate.
    }

    /// <summary>Cluster dock scale: the cluster's real-meter layout shrunk onto the button strip.</summary>
    private const float ButtonClusterMountScale = 0.7f;

    /// <summary>Cluster mount board-Y (collision math in <see cref="BuildMounts"/>).</summary>
    private const float ButtonClusterMountY = -0.115f;

    /// <summary>
    /// Initiative-track mount board-Y (tray-local meters). +Y = the board's BACK/far edge;
    /// the old +0.012 past the top edge (y ≈ 0.172) OVERHUNG the far edge. Pulled FORWARD
    /// (toward the player-front) to BoardH*0.5 − 0.06 ≈ 0.10 so the track sits just in front
    /// of the top edge, still above the board face.
    /// </summary>
    private const float InitiativeMountY = BoardH * 0.5f - 0.06f;

    private void BuildMounts()
    {
        _initiativeMount = new GameObject("InitiativeMount").transform;
        _initiativeMount.SetParent(_root, worldPositionStays: false);
        // PART B: the initiative-track mount position is PER-BOARD (debug-menu tunable);
        // seeded from the old fixed (0, InitiativeMountY, −0.004) so Oak is unchanged.
        _initiativeMount.localPosition = CardsConfig.InitiativeOffset(CardsConfig.CurrentBoard).Value;

        // Items 4/6: the objectives ('Aufgaben') dock mount position + size are PER-BOARD
        // (debug-menu tunable), base + ObjectivesOffset / × ObjectivesScale (seeded 0 / 1 → Oak
        // unchanged). The surface reads mount.position AND mount.lossyScale, so a mount scale
        // resizes the docked panel (TrayMountedPanelSurface.Place).
        _objectivesMount = new GameObject("ObjectivesMount").transform;
        _objectivesMount.SetParent(_root, worldPositionStays: false);
        _objectivesMount.localPosition = ObjectivesMountBase + CardsConfig.ObjectivesOffset(CardsConfig.CurrentBoard).Value;
        _objectivesMount.localScale = Vector3.one * CardsConfig.ObjectivesScale(CardsConfig.CurrentBoard).Value;

        // Test #20: the element infusion board docks in the LEFT column, below the
        // objectives dock — same right-center/grow-left convention and column width,
        // so both panels right-align off the board's left edge (x ≤ -0.332,
        // off-board: no board furniture to collide with there, only each other).
        // Vertically: the objectives are centered at y 0 with a 0.32 budget (worst
        // case bottom edge -0.16); the element mount sits at y -0.232 with a 0.12
        // budget (worst-case top edge -0.172) → 0.012 clearance, the same mount gap
        // used at the board edges.
        // Items 4/6: the element infusion ('Elemente') dock mount position + size are PER-BOARD
        // (debug-menu tunable), base + ElementsOffset / × ElementsScale (seeded 0 / 1 → Oak unchanged).
        _elementMount = new GameObject("ElementMount").transform;
        _elementMount.SetParent(_root, worldPositionStays: false);
        _elementMount.localPosition = ElementMountBase + CardsConfig.ElementsOffset(CardsConfig.CurrentBoard).Value;
        _elementMount.localScale = Vector3.one * CardsConfig.ElementsScale(CardsConfig.CurrentBoard).Value;

        // Test #19: the ButtonCluster docks under the card slots. Frame: +Z up the
        // board ("away from the player" — the PanelLayout pose contract the
        // cluster's own 180° yaw flip expects), +Y out of the board (caps rise
        // toward the viewer, presses travel into the board). At 0.7× the cluster
        // spans x ±0.109 (skip center 0.077 + base half 0.032) — clear of the rest
        // plate (right edge -0.1925) and the gear/undo column (left edge 0.186) —
        // and y -0.115 ± 0.042 (ready base half) → -0.157..-0.073: below the slot
        // captions (bottom edge ≈ -0.068), inside the board (bottom edge -0.16),
        // above the handle collider (top edge ≈ -0.165).
        // Items 4/6: the turn-flow ButtonCluster mount position + size are PER-BOARD (debug-menu
        // tunable), base + ClusterOffset / × ClusterScale (on top of the fixed 0.7× dock scale;
        // seeded 0 / 1 → Oak unchanged).
        _clusterMount = new GameObject("ButtonClusterMount").transform;
        _clusterMount.SetParent(_root, worldPositionStays: false);
        _clusterMount.localPosition = ClusterMountBase + CardsConfig.ClusterOffset(CardsConfig.CurrentBoard).Value;
        _clusterMount.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        _clusterMount.localScale = Vector3.one * (ButtonClusterMountScale * CardsConfig.ClusterScale(CardsConfig.CurrentBoard).Value);

        // Test #22: the SHARED DECISION DOCK hangs BELOW the board, off the bottom
        // edge — a drop-down "decision drawer", the mirror of the initiative track's
        // off-TOP-edge overhang (y 0.172, grows to 0.312 — the board routinely
        // extends content past its edges: objectives/elements off the left, pile off
        // the right). Test #21 docked the take-damage row CENTERED at y 0.06, OVER
        // the two parked cards (the test-#22 complaint that the buttons "fly over the
        // cards"); every board-FACE zone below the cards is already occupied — the
        // ButtonCluster owns the bottom-center (y -0.157..-0.073) whenever a scenario
        // runs (it shows for every non-Menu2D mode, so it CO-OCCURS with a decision
        // prompt), the CONFIRM/UNDO/gear column the bottom-right, the rest plate the
        // bottom-left. So the drawer goes clear of ALL of them, below the board.
        //
        // CENTERED origin, y -0.29 (the row centers on the mount). At the 0.12 max-
        // height budget the worst case spans y -0.35..-0.23; its TOP edge -0.23
        // clears the grab-handle box collider (y -0.215..-0.165, size 0.05 at
        // y -0.19) by 0.015 — a poke on a decision button can never grip the tray —
        // and sits 0.073 below the ButtonCluster's bottom edge (-0.157). At the 0.42
        // width budget it spans x ±0.21: clear of the FollowToggle (left edge 0.241)
        // by 0.031; the handle is above it in Y so its x ±0.198 never meets the
        // drawer. Everything else (cards y≥-0.068, cluster, CONFIRM column y 0.045
        // down to -0.125, the round readout y 0.107..0.143) is ≥0.073 above the
        // drawer top. z -0.020 lifts it 2 cm toward the viewer (proud of the tilted
        // board's bottom lip). The drawer only appears while a prompt is actually
        // open and everything is otherwise untouched — see DecisionDockSurface.
        // Item C: the decision dock's position + size are PER-BOARD (debug-menu tunable) — base +
        // DecisionOffset / × DecisionScale (seeded 0 / 1 → Oak unchanged). DecisionDockSurface
        // pose-follows the mount's position AND lossyScale, so a mount scale resizes the docked row.
        _decisionMount = new GameObject("DecisionMount").transform;
        _decisionMount.SetParent(_root, worldPositionStays: false);
        _decisionMount.localPosition = DecisionMountBase + CardsConfig.DecisionOffset(CardsConfig.CurrentBoard).Value;
        _decisionMount.localScale = Vector3.one * CardsConfig.DecisionScale(CardsConfig.CurrentBoard).Value;

        // Test #23 item 4: native-widget docks for the RIGHT-column controls. The
        // REAL Continue/Confirm (ReadyButton) and Undo (UndoButton) dock here — the
        // EXACT positions the mod CONFIRM/UNDO board buttons occupy (ButtonZoneX,
        // 0.045 / -0.06), so the familiar right-column layout is unchanged. Direct
        // _root children with identity localRotation (like DecisionMount): the
        // converted canvas faces the viewer via the board's own rotation. The
        // mod-drawn CONFIRM/UNDO buttons live at the same spots and hide while their
        // native counterpart is docked (TrayControlDockSurface / TickStatus).
        _confirmAnchor = new GameObject("ContinueMount").transform;
        _confirmAnchor.SetParent(_root, worldPositionStays: false);
        _confirmAnchor.localPosition = new Vector3(ButtonZoneX, 0.045f, -0.006f);

        _undoAnchor = new GameObject("UndoDockMount").transform;
        _undoAnchor.SetParent(_root, worldPositionStays: false);
        _undoAnchor.localPosition = new Vector3(ButtonZoneX, -0.06f, -0.006f);

        // Test #21: the pile viewer docks off the board's RIGHT edge — the only
        // free edge (initiative top, objectives + elements left, cluster bottom).
        // Left-center origin at x = +0.332 growing right, mirroring the
        // objectives' 0.012 mount gap. Stacks (PileViewer) sit at mount-local
        // x = PileStackOffsetX (0.05) → centers x ≈ 0.382, worst-case right edge
        // 0.382 + slab half 0.026 ≈ 0.408 — off-board, nothing docks there.
        // Vertically: two stacks at y = ±PileStackSpacing/2 (±0.058); each cell
        // spans slab half-height 0.031 + caption (bottom edge ≈ -0.055 in cell
        // space) → column extent y ≈ +0.089 .. -0.113, inside the board's ±0.16
        // half-height and clear of the FollowToggle (0.275, -0.19) and the handle
        // bar (y -0.19), which both sit below y -0.144. Inter-stack clearance:
        // upper cell bottom -0.003 vs lower cell top -0.027 → no overlap.
        _pileMount = new GameObject("PileMount").transform;
        _pileMount.SetParent(_root, worldPositionStays: false);
        // Round-2: the pile mount position is PER-BOARD (debug-menu tunable), base + PileOffset (seeded 0).
        _pileMount.localPosition = PileMountBase + CardsConfig.PileOffset(CardsConfig.CurrentBoard).Value;

        // Feature 6: the ACTIVE CARDS area docks to the RIGHT of the pile stacks — the
        // only spot on that edge still free (the pile stacks' worst-case right edge is
        // x ≈ 0.408; see the pile collision note above). ActiveMountOffsetX (0.17) pushes
        // this mount to x = 0.34 + 0.17 = 0.51, so the active cards — laid at mount-local
        // x = 0 and slightly SMALLER than hand/browse cards (ActivePileViewer.CardScale
        // ≈ 0.82 → half-width ≈ 0.026) — span x ≈ 0.484..0.536, a clear gap past the pile column.
        // Same left-center/grow-right convention, y and z as the pile mount so the two
        // areas sit side by side. Off-board: nothing else docks out here.
        _activeMount = new GameObject("ActiveMount").transform;
        _activeMount.SetParent(_root, worldPositionStays: false);
        // Round-2: the active mount position is PER-BOARD (debug-menu tunable), base + ActiveOffset (seeded 0).
        _activeMount.localPosition = ActiveMountBase + CardsConfig.ActiveOffset(CardsConfig.CurrentBoard).Value;
    }

    // ------------------------------------------------------------------ grab handle --

    private WorldUI.PanelGrabHandle? _handle;
    private BoxCollider? _handleZone;

    /// <summary>
    /// The tray handle bar's grab-zone collider (null before <see cref="BuildHandle"/> /
    /// after teardown). VRCard's dock-apron arbitration reads it: a slot-docked card's
    /// apron-extended collider yields to the bar whenever the palm is inside this zone
    /// (see <c>VRCard.PalmClearlyAtTrayBar</c>), so the bar stays grabbable under the board.
    /// </summary>
    internal Collider? HandleZone => _handleZone;

    private BoardButton? _followToggle;
    private BoardButton? _gear;
    private Transform? _gearAnchor;   // items 4/6: gear anchor, moved by the per-board VRSettingsOffset
    private Transform? _followAnchor; // items 4/6: follow/pin toggle anchor, moved by the per-board PinOffset

    // ---- IPanelGrabOwner (test #19: the grab mechanics moved into the shared
    // WorldUI.PanelGrabHandle core so panels can be grabbed exactly like the tray;
    // the tray's behavior is unchanged — same carry, resize, persistence, logs).
    Transform? WorldUI.IPanelGrabOwner.GrabRoot => Root;
    bool WorldUI.IPanelGrabOwner.GrabVisible => IsVisible;
    bool WorldUI.IPanelGrabOwner.GrabCarriesYaw => true; // the carry yaws the tray with the hand
    void WorldUI.IPanelGrabOwner.OnGrabFinished() => PersistPoseToConfig();

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
        _handleZone = box; // VRCard dock-apron arbitration reads this (bar beats apron)

        _handle = handleGo.AddComponent<WorldUI.PanelGrabHandle>();
        _handle.Init(this, bar.GetComponent<MeshRenderer>(), "Cards", "Tray");
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

        // CORE FIX: gear + follow-toggle now seat PROUD of the true surface (NewAnchor →
        // SeatOnBoardFace raycast) and are LIT (overlay:false → Standard base + cap), so their
        // side walls shade and they read as solid protruding buttons. Drop the RenderOnTop
        // shine-through — they are depth-correct and self-occlude like real buttons now.
        Transform pinAnchor = NewAnchor("FollowToggle",
            new Vector3(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -0.002f));
        // Items 4/6: nudge the follow/pin toggle by the per-board offset (base PinBase set by NewAnchor).
        pinAnchor.localPosition += CardsConfig.PinOffset(CardsConfig.CurrentBoard).Value;
        _followAnchor = pinAnchor;

        Transform gearAnchor = NewAnchor("SettingsGear",
            new Vector3(ButtonZoneX, -0.125f, -0.006f));
        // Items 4/6: nudge the VR-settings gear by the per-board offset (base GearBase set by NewAnchor).
        gearAnchor.localPosition += CardsConfig.VRSettingsOffset(CardsConfig.CurrentBoard).Value;
        _gearAnchor = gearAnchor;

        CreateDashboardButtons();

        // Baseline for the TickStatus language-change guard: the follow/gear labels self-heal
        // there on an actual language change (round/confirm/undo re-read every tick already).
        _labelLang = Core.Loc.CurrentLanguage;
    }

    /// <summary>
    /// Build the follow/pin toggle and settings gear keycaps on their (already placed)
    /// anchors. Split out of <see cref="BuildDashboardControls"/> so a ButtonTuning
    /// geometry change can rebuild just the buttons in place (anchors untouched).
    /// </summary>
    private void CreateDashboardButtons()
    {
        if (_followAnchor == null || _gearAnchor == null)
            return;
        // Item 5 (user): pin/gear were built NON-boxy (flat quad, no walls) while Confirm/Undo
        // pass boxy:true — that is why "Fixiert"/"Einstellungen" showed no walls but "Fortfahren"
        // did. Build them as the same beveled keycaps (thickness ≈ SquareCapThickness).
        // Category split (user: "every value applies ONLY to its own category"): the gear +
        // follow plates read the [BoardDashboard] set EXCLUSIVELY — per-button widths (they
        // are authored 0.062 vs 0.068), shared height/depth/travel. Numeric defaults ARE the
        // authored 0.062|0.068 × 0.030 × 0.030 / 4 mm geometry (no 0=Auto sentinel any more).
        WorldUI.ButtonTuning.Bind();
        float capDepth = WorldUI.ButtonTuning.DashboardDepth;
        float capTravel = WorldUI.ButtonTuning.DashboardTravel;
        _followToggle = BoardButton.Create(_followAnchor,
            new Vector2(WorldUI.ButtonTuning.DashboardPinWidth, WorldUI.ButtonTuning.DashboardHeight),
            new Color(0.58f, 0.46f, 0.26f), // T4: aged brass (desaturated from the loud gold)
            Core.Loc.Mod("follow"), ToggleFollow,
            thickness: capDepth, boxy: true, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Dashboard);
        _followToggle.SetState(true, accent: !CardsConfig.TrayFollow.Value);
        RegisterLaserTarget(_followToggle.Collider!, _followToggle);

        _gear = BoardButton.Create(_gearAnchor,
            new Vector2(WorldUI.ButtonTuning.DashboardGearWidth, WorldUI.ButtonTuning.DashboardHeight),
            new Color(0.37f, 0.36f, 0.38f), // T4: aged pewter (near-neutral, hint of cool)
            Core.Loc.Mod("set"),
            () => WorldUI.SettingsPanel.RequestToggle(),
            thickness: capDepth, boxy: true, travel: capTravel, // Item 5: beveled keycap walls like Confirm/Undo
            capCategory: WorldUI.ButtonTuning.CapCategory.Dashboard);
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
            // Item 4: toggling INTO follow must NOT zap the tray to the head-relative
            // config pose. The re-parent above already preserved the tray's current
            // world pose; leave it there and just mark it placed so TickPlacement
            // won't re-seat it. Only the very-first placement (the tray has never had a
            // valid pose yet) still seats it once from the head — a user-initiated
            // toggle always happens on an already-placed tray, so it stays put.
            if (!_placed)
                PlaceAtHead();
            else
                _placed = true; // keep the current pose (explicit: no re-seat on toggle)
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
            _followToggle.SetLabel(CardsConfig.TrayFollow.Value ? Core.Loc.Mod("follow") : Core.Loc.Mod("pinned"));
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
        _wantedHighlights[0] = _wantedHighlights[1] = null; // children of _root, destroyed with it
        _wantedMask = -1;
        _pickActive = false;
        _pickField = null; // child of _root, destroyed with it
        _pickFieldHighlight = null;
        _pickFieldVisible = false;
        _pickFieldHighlighted = false;
        _roundLabel = null; // child of _root, destroyed with it
        _roundShown = int.MinValue;
        _confirmedLabel = null;
        _confirm = null;
        _undo = null;
        _confirmAnchor = null; // child of _root, destroyed with it
        _undoAnchor = null;
        _itemUseSlot = null; // child of _root, destroyed with it
        _itemUseSlotGlow = null;
        _itemUseConfirm = null; // #9a: generic-cluster child (under _root via the Confirm anchor), destroyed with it
        _itemUseConfirmAction = null;
        _itemUseActive = false; // #9a: a fresh tray starts with the tuned 2-member cluster (no pending item)
        _handle = null; // child of _root, destroyed with it
        _followToggle = null;
        _gear = null;
        _gearAnchor = null; // child of _root, destroyed with it
        _followAnchor = null; // child of _root, destroyed with it
        _initiativeMount = null;
        _objectivesMount = null;
        _elementMount = null;
        _clusterMount = null; // child of _root, destroyed with it
        _decisionMount = null;
        _pileMount = null; // child of _root, destroyed with it (incl. the pile stacks)
        _activeMount = null; // child of _root, destroyed with it (incl. the active-card column)
        _placed = false;
        _wantVisible = false;
        _placementDeferLogged = false;
        _lastSlotActivity = float.NegativeInfinity;
        _boardColliderRegistered = false;
        _boardColliders.Clear(); // mesh colliders were children of _root, destroyed with it
        // Lost-board watchdog state: the PlayTray INSTANCE outlives its root (board switch /
        // rebuild), so a stale dwell timer or pin bookkeeping would otherwise be applied to the
        // next root and could recover a board that was never lost.
        _lostSince = 0f;
        _pinPoseVersion = -1;
        _rigLocalPinValid = false;
        _pinHousekeepingMove = null;
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
        // Untracked head (test #17 / item 1): in a session's first frames the rig camera
        // still sits at its local origin — the "in front of the player" math would
        // place (and a persisted PINNED mode then permanently pin) the tray at a
        // garbage pose (it spawned far below the map / outside the skybox). Defer until
        // the pose driver delivered a real pose — the same first-pose signal VRRigDriver
        // gates its pending recenter on; TickPlacement retries every frame. Item 1 also
        // treats a camera sitting EXACTLY at the world origin with identity rotation as
        // not-yet-posed (a fresh/stale Camera.main fallback), so a bogus head can never
        // seed the placement in the first place.
        bool untrackedHmd = head == VRRigDriver.HeadCamera
                            && head.transform.localPosition.sqrMagnitude < 1e-6f;
        bool atWorldOrigin = head.transform.position.sqrMagnitude < 1e-6f
                             && Quaternion.Angle(head.transform.rotation, Quaternion.identity) < 0.01f;
        if (untrackedHmd || atWorldOrigin)
        {
            if (!_placementDeferLogged)
            {
                _placementDeferLogged = true;
                VRLog.Info("Cards", "Control board placement deferred — head has no valid tracked pose yet.");
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

        ControlBoard board = CardsConfig.CurrentBoard;
        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        Vector3 offset = CardsConfig.TrayOffset; // (right, -down, forward), real meters
        // PART B: per-board BoardPosOffset is ADDED (in the head frame) on top of the tray offset.
        Vector3 boardPos = CardsConfig.BoardPosOffset(board).Value;
        Vector3 pos = headT.position
                      + flatForward * ((offset.z + boardPos.z) * scale)
                      + right * ((offset.x + boardPos.x) * scale)
                      + Vector3.up * ((offset.y + boardPos.y) * scale);

        // Item 1/3 safety net (never spawn/glitch outside the skybox): clamp the final pose to a
        // sane reach from the head. The deferral above catches the untracked-HMD / origin-camera
        // cases; this is the belt-and-braces guard for every OTHER cause (a wildly mis-tuned
        // BoardPosOffset, an odd rig scale, a frozen-then-restored head pose after an HMD doff/don)
        // — the board can never sit more than ~1.2 m from the head, at a plausible height, no
        // matter what the placement math produced.
        float outBefore = (pos - headT.position).magnitude;
        pos = ClampNearHead(pos, headT.position, scale, out bool wasClamped);
        if (wasClamped)
            VRLog.Info("Cards", $"Control board placement clamped to a sane reach from the head " +
                                $"(was {outBefore / Mathf.Max(scale, 1e-4f):F2} m out at scale 1, " +
                                $"now {(pos - headT.position).magnitude / Mathf.Max(scale, 1e-4f):F2} m).");

        _root.position = pos;
        _root.rotation = ComputeBoardRotation(flatForward, board);
        _root.localScale = Vector3.one * ComputeBoardScale(board);
        _placed = true;
        VRLog.Info("Cards", $"Control board placed ({board}: tilt {CardsConfig.BoardTilt(board).Value}°, " +
                            $"yaw {CardsConfig.TrayYaw.Value + CardsConfig.BoardYaw(board).Value:F0}°, " +
                            $"scale {ComputeBoardScale(board):F2}×).");
        // A persisted PINNED mode re-engages only NOW, at the just-placed
        // head-relative pose (test #17): the tray always spawns in front of the
        // player, pinned or not.
        if (!CardsConfig.TrayFollow.Value && _root.parent != _pinRoot)
            ApplyFollowMode();

        LogBoardFaceDiagnostics(); // ITEM 2 ground truth in the final placed pose (once per board)
    }

    /// <summary>
    /// PART B: the board's world rotation from the head's flat forward + the per-board
    /// tilt/yaw. BoardTilt REPLACES the old global TrayTilt (seeded 30 so Oak is unchanged);
    /// BoardYaw is ADDED on top of the grab-written TrayYaw (seeded 0). Shared by
    /// <see cref="PlaceAtHead"/> and <see cref="ReapplyOrientation"/>.
    /// </summary>
    private static Quaternion ComputeBoardRotation(Vector3 flatForward, ControlBoard board) =>
        Quaternion.Euler(0f, CardsConfig.TrayYaw.Value + CardsConfig.BoardYaw(board).Value, 0f)
        * Quaternion.LookRotation(flatForward, Vector3.up)
        * Quaternion.Euler(90f - CardsConfig.BoardTilt(board).Value, 0f, 0f);

    /// <summary>PART B: the board's local scale = grab-written TrayScale × per-board BoardScale (seeded 1).</summary>
    private static float ComputeBoardScale(ControlBoard board) =>
        CardsConfig.ClampedTrayScale * CardsConfig.BoardScale(board).Value;

    /// <summary>
    /// Item 3 safety net: clamp a candidate board position to a sane reach from the head — no
    /// more than ~1.2 m horizontally and a plausible height band. Applied on EVERY head-relative
    /// placement path (<see cref="PlaceAtHead"/> and the presence-regain re-assert) so a bogus or
    /// frozen-then-restored head pose can never fling the board outside the play space.
    /// </summary>
    private static Vector3 ClampNearHead(Vector3 pos, Vector3 headPos, float scale, out bool clamped)
    {
        Vector3 delta = pos - headPos;
        var horizontal = new Vector3(delta.x, 0f, delta.z);
        float maxReach = 1.2f * scale;
        if (horizontal.magnitude > maxReach)
            horizontal = horizontal.normalized * maxReach;
        float clampedY = Mathf.Clamp(delta.y, -1.0f * scale, 0.2f * scale);
        Vector3 result = headPos + horizontal + Vector3.up * clampedY;
        clamped = (result - pos).sqrMagnitude > 1e-6f;
        return result;
    }

    /// <summary>
    /// Horizontal distance (× scale) beyond which a PINNED (world-anchored) board is treated as
    /// LOST — a genuine glitch, not a legitimate walk-away — and snapped back near the head on a
    /// presence regain. Generous so ordinary world-anchored roaming is never disturbed.
    /// </summary>
    private const float LostReach = 6f;

    /// <summary>
    /// Item 3: re-assert the board's placement after the head regained tracking (HMD re-donned /
    /// runtime resume). The user is looking NOW, so the board must be PRESENT and sanely placed —
    /// it must never stay gone or stranded far away.
    /// - FOLLOW: re-seat at the head (through <see cref="PlaceAtHead"/>'s head-distance clamp).
    /// - PINNED/world-anchored: KEEP the pinned world pose (roaming is legitimate), UNLESS it is
    ///   non-finite or absurdly far/low (a real glitch) — then snap it back near the head.
    /// Either way the root is re-shown so a presence blip can never leave it hidden. Board-switch
    /// pose (<see cref="RestorePose"/>) is deliberately untouched.
    /// </summary>
    internal void ReassertPlacement(string reason)
    {
        if (_root == null)
            return;

        if (CardsConfig.TrayFollow.Value)
        {
            _placed = false;      // force a fresh, clamped head-relative seat
            PlaceAtHead();        // defers safely if the head still has no valid pose (TickPlacement retries)
            if (_wantVisible)
                SetVisible(true); // never leave the board hidden after a resume
            VRLog.Info("Cards", $"Control board placement re-asserted ({reason}, FOLLOW) — re-seated near the head.");
            return;
        }

        // PINNED: keep the world pose unless it is clearly lost.
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
            Vector3 pos = _root.position;
            Vector3 delta = pos - head.transform.position;
            var horizontal = new Vector3(delta.x, 0f, delta.z);
            bool finite = !(float.IsNaN(pos.x) || float.IsInfinity(pos.x)
                            || float.IsNaN(pos.y) || float.IsInfinity(pos.y)
                            || float.IsNaN(pos.z) || float.IsInfinity(pos.z));
            bool lost = !finite
                        || horizontal.magnitude > LostReach * scale
                        || delta.y < -2.0f * scale;
            if (lost)
            {
                PlaceAtHead(); // snaps back to the configured head-relative pose and re-pins (clamped)
                VRLog.Info("Cards", $"Control board was lost/far ({reason}, PINNED) — snapped back near the head.");
            }
        }
        if (_wantVisible)
            SetVisible(true); // re-show a pinned board that a presence blip hid
    }

    // ------------------------------------------------------------------ LOST-BOARD WATCHDOG --
    //
    // ROOT CAUSE THIS EXISTS FOR (incident: "walked around the room, briefly took the headset
    // off and put it back on — the control board was GONE").
    //
    // Every recovery path the board had before this watchdog hung off ONE signal: the
    // VRPresenceWatch doff/don edge (SessionResumed → CardsDriver.ReassertBoardKeepingPose →
    // ReassertPlacement above). The hardware log of the incident run proves that signal never
    // arrived:
    //   * LogOutput.log has exactly ONE "[Core] User presence lost" line, at boot in the menu,
    //     and NO "[Core] Session resumed" line anywhere — so no recovery ever ran;
    //   * Player.log shows the OpenXR session sitting in XR_SESSION_STATE_FOCUSED from
    //     11:03:22 straight through to the 11:10:51 shutdown — SteamVR/OpenXR (the runtime in
    //     use, not VDXR) neither idled the session nor reported the proximity sensor flipping,
    //     so neither VRPresenceWatch signal (userPresence feature / frozen head pose) fired.
    // A doff/don that the runtime hides from us is therefore INVISIBLE to every event-driven
    // recovery we have. Meanwhile the board CAN legitimately end up out of reach:
    //   * FOLLOW mode anchors the board in RIG space, so physically walking across the room
    //     leaves it standing where it was (and at the diorama scale ~20.7 world units per real
    //     metre, "a few steps" is tens of world units of separation);
    //   * PINNED mode anchors it in raw WORLD space, so a recentre — which TELEPORTS the rig to
    //     the table-edge/circle seat, VRRigDriver.Recenter — strands it at the old seat;
    //   * the pin holder's scale was baked ONCE at pin time from the rig's lossy scale, so a
    //     later world-grab zoom silently rescaled the pinned board's world OFFSET with it.
    // So the safety net must be UNCONDITIONAL and per-frame, not event-driven. It mirrors the
    // proven LOST-MENU RECALL in WorldUI.ModalFallback.TickMenuRecall: dwell timer, generous
    // envelope, never yank a board the user is holding, one loud log line stating WHY.

    /// <summary>Watchdog reach envelope, REAL metres (× diorama scale): within this the board is
    /// AT HAND — never recalled, no matter where the head is looking (a lectern below the chin
    /// legitimately leaves the frustum whenever the player looks up at the dungeon).</summary>
    private const float WatchReachMeters = 1.8f;

    /// <summary>Watchdog "findable" distance, REAL metres (× diorama scale): farther than this the
    /// board is unusable even if it is technically on screen.</summary>
    private const float WatchFindableMeters = 4f;

    /// <summary>Frustum slack for the watchdog visibility test (fraction of the viewport) — a board
    /// half off the view edge is findable by turning the head and must not be yanked back.</summary>
    private const float WatchViewMargin = 0.35f;

    /// <summary>How long the board must be continuously BOTH out of reach AND out of view before it
    /// is recovered. Long enough that leaning away / turning around never moves it; short enough
    /// that a doff/don or a stranding glitch is fixed before the player can call it "gone".</summary>
    private const float WatchLostDwellSeconds = 3f;

    /// <summary>Unscaled time the board first read LOST, 0 while it is fine. Reset on recovery,
    /// on a grab (the user is deliberately carrying it) and whenever it reads reachable/visible.</summary>
    private float _lostSince;

    /// <summary><see cref="VRRigDriver.RigPoseVersion"/> the PINNED world pose was authored under.
    /// The version bumps ONLY on a rig (re)build or a deliberate recentre — i.e. exactly the
    /// tracking-origin changes that move the player without moving the world — so a mismatch means
    /// the pinned pose was authored against a DIFFERENT origin and must be carried along.</summary>
    private int _pinPoseVersion = -1;

    /// <summary>Pending SANCTIONED-move label from <see cref="SyncPinHolder"/>, null when it wrote
    /// nothing. The pin housekeeping legitimately changes the board's PARENT-LOCAL pose (the holder
    /// rescale) or its world pose (the tracking-origin carry), and CardsDriver's issue-C pose watch
    /// Warns on any change it did not sanction — so the driver drains this right after the tick.</summary>
    private string? _pinHousekeepingMove;

    /// <summary>Take (and clear) the pending pin-housekeeping move label — see <see cref="_pinHousekeepingMove"/>.</summary>
    internal string? ConsumePinHousekeepingMove()
    {
        string? move = _pinHousekeepingMove;
        _pinHousekeepingMove = null;
        return move;
    }

    /// <summary>
    /// Per-frame safety net (CardsDriver.Update). Returns true when the board must be recovered
    /// THIS frame; the caller sanctions the resulting pose change (issue-C pose watchdog) and
    /// calls <see cref="RecoverLostBoard"/> with the returned reason. Never mutates the pose
    /// itself apart from the pin-holder housekeeping documented below, which is pose-PRESERVING.
    /// </summary>
    internal bool TickLostWatchdog(out string why)
    {
        why = string.Empty;
        // !_placed = the initial head-relative placement is still deferred (untracked head at
        // scenario start). The root is hidden and still sits at its spawn pose, so any verdict
        // here would be about a pose that does not exist yet; TickPlacement is already retrying
        // every frame and logs its own "placement deferred" line.
        if (_root == null || !_wantVisible || !_placed)
        {
            _lostSince = 0f;
            return false;
        }

        // Housekeeping first: keep the PINNED holder honest (scale drift + tracking-origin
        // changes). Both are pose-preserving in the user's frame of reference, so they run
        // before the lost test and can stop the board from ever reading lost.
        SyncPinHolder();

        // A gripped board is being deliberately placed — never recall mid-carry (the
        // ModalFallback recall rule; yanking a panel out of the user's hand is worse than
        // whatever it was doing).
        if (_handle != null && _handle.IsGrabbed)
        {
            _lostSince = 0f;
            return false;
        }

        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
        {
            _lostSince = 0f; // no head this tick — no verdict is possible, and no timer may run
            return false;
        }

        Vector3 pos = _root.position;
        // NON-FINITE: no dwell timer, no debate. A NaN/Inf transform is corrupt, everything
        // parented to it (cards, docked game canvases) renders undefined, and it can never heal
        // by itself.
        if (!IsFinite(pos) || !IsFinite(_root.localScale))
        {
            _lostSince = 0f;
            why = $"NON-FINITE transform (pos {pos}, localScale {_root.localScale})";
            return true;
        }

        float scale = Mathf.Max(_root.parent != null ? _root.parent.lossyScale.x : 1f, 1e-4f);
        Vector3 headPos = head.transform.position;
        Vector3 delta = pos - headPos;
        var horizontal = new Vector3(delta.x, 0f, delta.z);
        float reachM = horizontal.magnitude / scale;
        float dropM = delta.y / scale;

        // AT HAND: inside the reach envelope the board is usable whatever the head is doing.
        bool reachable = reachM <= WatchReachMeters && Mathf.Abs(dropM) <= WatchReachMeters;
        // FINDABLE: outside reach it must at least be ON SCREEN and close enough to walk to,
        // otherwise the player has no way of knowing where it went. Roaming away from a PINNED
        // board while still SEEING it stays legitimate — that is the whole point of pinning.
        bool findable = !reachable
                        && delta.magnitude / scale <= WatchFindableMeters
                        && IsInHeadView(head, pos);

        if (reachable || findable)
        {
            _lostSince = 0f;
            return false;
        }

        float now = Time.unscaledTime; // the pause menu may freeze timeScale
        if (_lostSince <= 0f)
        {
            _lostSince = now;
            return false;
        }
        float lostFor = now - _lostSince;
        if (lostFor < WatchLostDwellSeconds)
            return false;

        // Angle off the view axis is the number that tells the next reader whether the board was
        // BEHIND the player (walked away / recentre stranding) or merely too far ahead.
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        Vector3 headFlat = new Vector3(head.transform.forward.x, 0f, head.transform.forward.z);
        float angle = flat.sqrMagnitude > 1e-6f && headFlat.sqrMagnitude > 1e-6f
            ? Vector3.Angle(headFlat, flat)
            : 0f;
        why = $"out of reach AND out of view for {lostFor:F1}s " +
              $"({reachM:F2} m out, {dropM:+0.00;-0.00} m vertical, {angle:F0}° off the view axis, " +
              $"mode {(CardsConfig.TrayFollow.Value ? "FOLLOW" : "PINNED")}, rig scale {scale:F1})";
        return true;
    }

    /// <summary>
    /// Recover a lost board: re-seat it at the configured head-relative offset (through
    /// <see cref="PlaceAtHead"/>'s clamp, which also re-pins a PINNED board at the fresh pose)
    /// and force it visible. Logs LOUDLY with the previous pose and the reason — this is the line
    /// to grep for after an incident: "[Cards] CONTROL BOARD RECOVERED".
    /// </summary>
    internal void RecoverLostBoard(string why)
    {
        if (_root == null)
            return;
        Vector3 before = _root.position;
        _lostSince = 0f;
        _placed = false;   // force a fresh, clamped head-relative seat in BOTH modes
        PlaceAtHead();     // defers safely when the head has no pose yet (TickPlacement retries)
        if (_wantVisible)
            SetVisible(true); // a recovery must never leave the board hidden
        // Re-author the pinned world pose against the CURRENT tracking origin so the next
        // recentre carries it instead of stranding it again.
        _pinPoseVersion = VRRigDriver.RigPoseVersion;
        VRLog.Info("Cards", $"CONTROL BOARD RECOVERED — {why}. Was at {before}, re-homed to " +
                            $"{_root.position} in front of the player. The board must never be " +
                            "unreachable; report this line with the surrounding log.");
    }

    /// <summary>
    /// PINNED-holder housekeeping, pose-preserving. Two silent glitch sources live here:
    ///
    /// 1. SCALE DRIFT. <see cref="ApplyFollowMode"/> bakes the rig's lossy scale into the world
    ///    holder ONCE, at pin time, so the tray's own localScale keeps its 0.5×–2× semantics.
    ///    But WorldGrab rescales the rig live (0.1×–12× of base), and the tray sits at a
    ///    non-zero LOCAL offset under the holder — so a later zoom would multiply that offset
    ///    and shift the pinned board across the room without anyone touching it. Re-assert the
    ///    holder scale from the live rig scale every frame and restore the tray's WORLD pose
    ///    around the write, so the board stays bolted to its world spot at any zoom.
    ///
    /// 2. TRACKING-ORIGIN CHANGE. A pinned pose is raw WORLD space, but a recentre teleports the
    ///    RIG (VRRigDriver.Recenter — table-edge seat / multiplayer spawn circle) without moving
    ///    the world, which strands the board at the old seat. RigPoseVersion bumps on exactly
    ///    those events (rig (re)build + deliberate recentre) and on nothing else — snap turns and
    ///    world-grab deliberately do NOT bump it — so it is the stable signal to key on: carry the
    ///    pinned board along by its RIG-RELATIVE pose, i.e. it keeps the same position/orientation
    ///    relative to the player, which is what "my board stayed where I put it" means to the user.
    /// </summary>
    private void SyncPinHolder()
    {
        int version = VRRigDriver.RigPoseVersion;
        if (_root == null || CardsConfig.TrayFollow.Value || _pinRoot == null)
        {
            // FOLLOW mode is rig-parented: both problems are structurally impossible there.
            _pinPoseVersion = version;
            return;
        }

        Transform? rig = VRRigDriver.RigRoot;
        // (2) tracking-origin change — carry the pinned board with the rig.
        if (rig != null && _pinPoseVersion >= 0 && _pinPoseVersion != version && _rigLocalPinValid)
        {
            Vector3 pos = rig.TransformPoint(_rigLocalPinPos);
            Quaternion rot = rig.rotation * _rigLocalPinRot;
            if (IsFinite(pos))
            {
                Vector3 before = _root.position;
                _root.SetPositionAndRotation(pos, rot);
                _pinHousekeepingMove = "pin carried through a tracking-origin change";
                VRLog.Info("Cards", $"Control board (PINNED) carried through a tracking-origin change " +
                                    $"(rig pose version {_pinPoseVersion} → {version}: rig rebuild or " +
                                    $"recentre): {before} → {pos}. A world-space pin would have been " +
                                    "stranded at the old seat.");
            }
        }
        _pinPoseVersion = version;

        // (1) NO live holder rescale. An earlier cut of this housekeeping re-asserted
        // _pinRoot.localScale from the LIVE rig scale every frame, on the theory that a world-grab
        // zoom would otherwise drift the pinned board. That theory was wrong twice over:
        //   * it cannot drift. The holder sits at the world ORIGIN with identity rotation and a
        //     scale baked once at pin time (ApplyFollowMode); it is NOT parented under the rig, so
        //     rescaling the rig cannot move or resize anything underneath it.
        //   * the rescale itself was the bug the tester then reported ("world zoom zooms the
        //     pinned control board too — that must not happen, the board is scaled independently
        //     by the player"). With the holder tracking the rig, the board's WORLD size grows with
        //     the zoom while its world POSITION is held — so it swells on screen exactly as the
        //     rest of the world shrinks. With the holder FIXED, world size and world distance are
        //     both constant, so a pinned board is completely unaffected by zoom, which is the
        //     whole point of pinning it. Board size stays what the player dialled in (BoardScale /
        //     the two-hand resize), and nothing else.
        // The holder scale is therefore written ONCE, by ApplyFollowMode, and left alone.

        // Re-cache the rig-relative pin pose every frame the origin is stable, so the NEXT
        // origin change has a fresh, correct offset to carry the board by.
        if (rig != null)
        {
            _rigLocalPinPos = rig.InverseTransformPoint(_root.position);
            _rigLocalPinRot = Quaternion.Inverse(rig.rotation) * _root.rotation;
            _rigLocalPinValid = IsFinite(_rigLocalPinPos);
        }
        else
        {
            _rigLocalPinValid = false;
        }
    }

    private Vector3 _rigLocalPinPos;
    private Quaternion _rigLocalPinRot = Quaternion.identity;
    private bool _rigLocalPinValid;

    /// <summary>Board centre inside the head frustum with <see cref="WatchViewMargin"/> slack.</summary>
    private static bool IsInHeadView(Camera head, Vector3 worldPos)
    {
        Vector3 vp = head.WorldToViewportPoint(worldPos);
        return vp.z > 0f
               && vp.x >= -WatchViewMargin && vp.x <= 1f + WatchViewMargin
               && vp.y >= -WatchViewMargin && vp.y <= 1f + WatchViewMargin;
    }

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));

    // ------------------------------------------------------------------ debug-menu live apply --

    /// <summary>
    /// PART F live-apply: move the slot snap-glow / wanted-glow overlays to a new per-board
    /// offset in place (no rebuild). Base local-Z is preserved; the offset adds on top.
    /// Item 1: the two overlays are a PAIR, so a per-board SPACING spreads them apart along
    /// the slot-local X (the board's long/inter-slot axis) — slot 0 (left) −½, slot 1 (right) +½.
    /// </summary>
    internal void SetOverlayOffset(Vector3 offset, float spacing)
    {
        for (int i = 0; i < 2; i++)
        {
            float xSpread = (i == 0 ? -0.5f : 0.5f) * spacing; // spread the pair apart along the slot axis
            if (_slotHighlights[i] != null)
                _slotHighlights[i]!.transform.localPosition =
                    new Vector3(offset.x + xSpread, offset.y, SlotGlowBaseZ + offset.z);
            if (_wantedHighlights[i] != null)
                _wantedHighlights[i]!.transform.localPosition =
                    new Vector3(offset.x + xSpread, offset.y, WantedGlowBaseZ + offset.z);
            // Item B: the placed card's resting spot follows the SAME overlay offset/spread — the
            // glow AND the physical card move together. Re-home any resting (non-held) occupant now;
            // freshly placed cards read the coupled offset via SlotHomeOffsetFor.
            VRCard? occ = _occupants[i];
            if (occ != null && !occ.IsHeld)
                occ.SetHome(_slots[i]!, SlotHomeOffsetFor(i), Quaternion.identity, SlotCardScale);
        }
    }

    /// <summary>PART F live-apply: move the initiative-track mount to a new per-board local position.</summary>
    internal void SetInitiativeOffset(Vector3 offset)
    {
        if (_initiativeMount != null)
            _initiativeMount.localPosition = offset;
    }

    /// <summary>
    /// PART F live-apply: move the generic Confirm/Undo buttons to a new per-board offset +
    /// inter-button spacing (instant). Confirm (upper) takes +spacing/2 along the board's short
    /// axis, Undo (lower) −spacing/2.
    /// </summary>
    internal void SetConfirmUndoOffset(Vector3 offset, float spacing)
    {
        // Item D / requirement 9a: auto-fit the generic cluster's buttons in a vertical stack. The
        // member COUNT is dynamic — normally Confirm(0)/Undo(1); while a held usable item card is
        // clipped into the use slot the item "Use" confirm joins as member 1 (the slot directly ABOVE
        // Undo), pushing the pair to a 3-member stack. GenericClusterY packs any count into the column
        // (at 2 it reproduces the tuned ±spacing/2 pair; at 3 it spreads within GenericColumnHeight so
        // Use lands squarely above Undo). Undo is ALWAYS the bottom member (index count-1).
        int count = GenericCount;
        if (_confirm != null)
            _confirm.transform.localPosition = offset + new Vector3(0f, GenericClusterY(0, count, spacing), 0f);
        if (_itemUseConfirm != null && _itemUseActive)
            _itemUseConfirm.transform.localPosition = offset + new Vector3(0f, GenericClusterY(1, count, spacing), 0f);
        if (_undo != null)
            _undo.transform.localPosition = offset + new Vector3(0f, GenericClusterY(count - 1, count, spacing), 0f);
    }

    /// <summary>
    /// Item D: how many buttons the mod-owned GENERIC cluster currently lays out (Confirm + Undo).
    /// The layout/size math below is parametric so the cluster can hold &gt;2 — the game can activate
    /// up to four turn-flow buttons at once (readyButton/skip/undo/select, decompiled-confirmed).
    /// </summary>
    private const int GenericButtonCount = 2;

    /// <summary>
    /// Requirement 9a: true while the item "Use" confirm is a live member of the generic cluster (a held
    /// usable item card is clipped into the use slot). It bumps <see cref="GenericCount"/> to 3 so Confirm
    /// (top) / Use (middle, above Undo) / Undo (bottom) auto-fit the column; false restores the tuned pair.
    /// </summary>
    private bool _itemUseActive;

    /// <summary>Live generic-cluster member count: the tuned pair, plus the item "Use" confirm while pending.</summary>
    private int GenericCount => GenericButtonCount + (_itemUseActive ? 1 : 0);

    /// <summary>Vertical room (tray-local meters) the generic cluster packs its buttons into. Kept clear of
    /// the round readout (top edge) and the settings gear (bottom edge) so a 3-member stack (with the item
    /// "Use" confirm) fits between them without colliding.</summary>
    private const float GenericColumnHeight = 0.16f;

    /// <summary>Minimum inter-button gap (tray-local meters) when the generic cluster auto-fits &gt;2 buttons.</summary>
    private const float GenericButtonGap = 0.010f;

    /// <summary>
    /// SUPERSEDED (user: "der Use-Button soll genauso groß sein und sich nach den Werten richten,
    /// die die generischen Buttons vorgegeben haben"). Every generic-cluster member — Confirm, Undo
    /// and the item "Use" confirm alike — now keeps the tuned [BoardButtons] cap size at ANY count,
    /// and the stack makes room by SPACING instead (see <see cref="GenericClusterY"/>). The old
    /// auto-shrink meant a cluster changed size depending on how many members happened to be live,
    /// so the moment Use appeared all three caps snapped to a smaller auto-fit square and none of
    /// them matched the size the player had dialled in. Kept only for callers that still want the
    /// old fit answer; nothing in the cluster path uses it.
    ///
    /// Item D (original): per-button side length for a generic cluster of <paramref name="count"/>
    /// buttons. A pair (or single) keeps the full authored size; from 3 up each cap shrinks so the whole stack
    /// fits <see cref="GenericColumnHeight"/> (auto-scale from the count), floored so it stays pokeable.
    /// </summary>
    internal static float GenericClusterButtonSize(float baseSide, int count)
    {
        if (count <= 2)
            return baseSide;
        float avail = (GenericColumnHeight - (count - 1) * GenericButtonGap) / count;
        return Mathf.Clamp(Mathf.Min(baseSide, avail), 0.02f, baseSide);
    }

    /// <summary>
    /// Local-Y of button <paramref name="index"/> in a top-to-bottom generic stack of
    /// <paramref name="count"/>, centred on the cluster anchor with the TUNED
    /// <paramref name="spacing"/> as the step at every count. At count 2 this is exactly the old
    /// ±spacing/2 pair, so nothing about the tuned Confirm/Undo layout changes.
    ///
    /// WHY the step is the tuned spacing and no longer <see cref="GenericColumnHeight"/>/(count−1):
    /// the old form packed extra members into a FIXED column, which only works if the caps shrink
    /// to match — and shrinking the caps is exactly what the user rejected ("the Use button should
    /// be the same size and follow the values the generic buttons were given"). Sizes now come
    /// purely from the [BoardButtons] tuning, so the stack has to grow instead of the caps
    /// shrinking; growing it symmetrically keeps the cluster centred where the player placed it,
    /// and both directions stay under the player's control through the same spacing/offset dials.
    /// </summary>
    internal static float GenericClusterY(int index, int count, float spacing)
    {
        if (count <= 1)
            return 0f;
        return ((count - 1) * 0.5f - index) * spacing; // member 0 at the top, descending
    }

    /// <summary>PART F live-apply: move the discard/burn pile mount to a new per-board offset (instant).</summary>
    internal void SetPileOffset(Vector3 offset)
    {
        if (_pileMount != null)
            _pileMount.localPosition = PileMountBase + offset;
    }

    /// <summary>PART F live-apply: move the ACTIVE-cards mount to a new per-board offset (instant).</summary>
    internal void SetActiveOffset(Vector3 offset)
    {
        if (_activeMount != null)
            _activeMount.localPosition = ActiveMountBase + offset;
    }

    /// <summary>Items 4/6 live-apply: move + resize the OBJECTIVES ('Aufgaben') dock mount (instant).</summary>
    internal void SetObjectivesLayout(Vector3 offset, float scale)
    {
        if (_objectivesMount != null)
        {
            _objectivesMount.localPosition = ObjectivesMountBase + offset;
            _objectivesMount.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Items 4/6 live-apply: move + resize the ELEMENT infusion ('Elemente') dock mount (instant).</summary>
    internal void SetElementsLayout(Vector3 offset, float scale)
    {
        if (_elementMount != null)
        {
            _elementMount.localPosition = ElementMountBase + offset;
            _elementMount.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Items 4/6 live-apply: move + resize the turn-flow ButtonCluster mount (instant).</summary>
    internal void SetClusterLayout(Vector3 offset, float scale)
    {
        if (_clusterMount != null)
        {
            _clusterMount.localPosition = ClusterMountBase + offset;
            _clusterMount.localScale = Vector3.one * (ButtonClusterMountScale * scale);
        }
    }

    /// <summary>Item C live-apply: move + resize the shared DECISION DOCK mount (instant; the surface pose-follows it).</summary>
    internal void SetDecisionLayout(Vector3 offset, float scale)
    {
        if (_decisionMount != null)
        {
            _decisionMount.localPosition = DecisionMountBase + offset;
            _decisionMount.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Items 4/6 live-apply: move the round readout ('Runde N') to a new per-board offset (instant).</summary>
    internal void SetReadoutOffset(Vector3 offset)
    {
        if (_roundLabel != null)
            _roundLabel.transform.localPosition = ReadoutBase + offset;
    }

    /// <summary>Items 4/6 live-apply: move the VR-settings gear button to a new per-board offset (instant).</summary>
    internal void SetVRSettingsOffset(Vector3 offset)
    {
        if (_gearAnchor != null)
            _gearAnchor.localPosition = GearBase + offset;
    }

    /// <summary>Items 4/6 live-apply: move the FOLLOW/PIN toggle button to a new per-board offset (instant).</summary>
    internal void SetPinOffset(Vector3 offset)
    {
        if (_followAnchor != null)
            _followAnchor.localPosition = PinBase + offset;
    }

    /// <summary>Fixed base local position of the discard/burn pile mount (per-board PileOffset adds on top).</summary>
    private static Vector3 PileMountBase => new(BoardW * 0.5f + 0.012f, 0f, -0.004f);

    /// <summary>Fixed base local position of the ACTIVE-cards mount (per-board ActiveOffset adds on top).</summary>
    private static Vector3 ActiveMountBase => new(BoardW * 0.5f + 0.012f + ActiveMountOffsetX, 0f, -0.004f);

    // ---- Fixed base positions for the remaining board-attached elements (items 4/6). Each
    // per-board offset from the debug menu ADDS on top of these. ----

    /// <summary>Fixed base local position of the OBJECTIVES ('Aufgaben') dock mount.</summary>
    private static Vector3 ObjectivesMountBase => new(-BoardW * 0.5f - 0.012f, 0f, -0.004f);

    /// <summary>Fixed base local position of the ELEMENT infusion ('Elemente') dock mount (left column below objectives).</summary>
    private static Vector3 ElementMountBase =>
        new(-BoardW * 0.5f - 0.012f,
            -(ObjectivesMountMaxHeight * 0.5f + 0.012f + ElementMountMaxHeight * 0.5f),
            -0.004f);

    /// <summary>Fixed base local position of the turn-flow ButtonCluster mount (under the slots).</summary>
    private static Vector3 ClusterMountBase => new(0f, ButtonClusterMountY, -0.006f);

    /// <summary>Item C: fixed base local position of the shared DECISION DOCK mount (hangs below the board).</summary>
    private static Vector3 DecisionMountBase => new(0f, -0.29f, -0.020f);

    /// <summary>
    /// Fixed base local position of the ITEM-USE clip-in slot (items rework, requirement 3):
    /// UNDER the board's bottom edge in the right (Confirm/Undo) column, proud toward the player.
    /// The per-board <see cref="CardsConfig.ItemUseSlotOffset"/> adds on top (debug-menu tunable).
    /// </summary>
    private static Vector3 ItemUseSlotBase => new(ButtonZoneX, -BoardH * 0.5f - 0.095f, -0.020f);

    /// <summary>Gap between the item-use recess's bottom edge and the "USE" caption's centre line
    /// (tray-local metres). Big enough that the caption clears the glow rim (1.28× the card) as well
    /// as the card itself, so nothing ever overlaps the clipped-in card — see BuildItemUseSlot.</summary>
    private const float ItemUseLabelDrop = 0.026f;

    /// <summary>Height budget the "USE" caption is fitted into — the strip BELOW the recess, not the
    /// card's own height (see BuildItemUseSlot for why fitting it to the card height re-created the
    /// overlap the drop is there to prevent).</summary>
    private const float ItemUseLabelHeight = 0.030f;

    /// <summary>Fixed base local position of the round readout ('Runde N', top-right).</summary>
    private static Vector3 ReadoutBase => new(ButtonZoneX, 0.125f, -FixedProudZ);

    /// <summary>Fixed base local position of the VR-settings gear button (right column, under UNDO).</summary>
    private static Vector3 GearBase => new(ButtonZoneX, -0.125f, -FixedProudZ);

    /// <summary>Fixed base local position of the FOLLOW/PIN toggle button (bottom-right corner).</summary>
    private static Vector3 PinBase => new(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -FixedProudZ);

    /// <summary>
    /// PART F live-apply: recompute the board rotation + scale from the ACTIVE board's config
    /// (BoardTilt/Yaw/Scale/PosOffset). FOLLOW mode re-derives the full pose (incl. posOffset)
    /// from the head; PINNED KEEPS the world position and only re-orients + rescales in place.
    /// </summary>
    internal void ReapplyOrientation()
    {
        if (_root == null)
            return;
        if (CardsConfig.TrayFollow.Value)
        {
            PlaceAtHead(); // re-derives position (incl. BoardPosOffset), rotation and scale
            return;
        }
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 flatForward = head.transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();
        ControlBoard board = CardsConfig.CurrentBoard;
        _root.rotation = ComputeBoardRotation(flatForward, board); // KEEP position (pinned)
        _root.localScale = Vector3.one * ComputeBoardScale(board);
    }

    /// <summary>
    /// PART F live-apply: rebuild the square Confirm/Undo buttons in place (size changes need a
    /// rebuilt cap). Re-parents onto the SAME anchors, purges the dead laser targets and rebuilds
    /// them from current config. The rest discs are rebuilt separately by CardsDriver
    /// (<c>RestControls.Destroy(); EnsureBuilt(...)</c>).
    /// </summary>
    internal void RebuildAttachedControls()
    {
        if (_root == null)
            return;
        Transform? confirmAnchor = _confirm != null ? _confirm.transform.parent : _confirmAnchor;
        Transform? undoAnchor = _undo != null ? _undo.transform.parent : _undoAnchor;
        if (_confirm != null)
        {
            Object.DestroyImmediate(_confirm.gameObject);
            _confirm = null;
        }
        if (_undo != null)
        {
            Object.DestroyImmediate(_undo.gameObject);
            _undo = null;
        }
        // Requirement 9a: the item "Use" cluster button is (re)built by BuildButtons — tear the old one
        // down first so a tuning rebuild never leaks/doubles it.
        if (_itemUseConfirm != null)
        {
            Object.DestroyImmediate(_itemUseConfirm.gameObject);
            _itemUseConfirm = null;
        }
        LaserTargets.RemoveAll(static t => t.Collider == null); // drop the just-destroyed (and any other dead) targets
        BuildButtons(confirmAnchor, undoAnchor);
    }

    /// <summary>
    /// ButtonTuning live-apply, checked once per <see cref="TickStatus"/> tick: a geometry
    /// entry changed (width/height/depth/travel — anything the pull-based
    /// <see cref="WorldUI.ButtonTuning.Version"/> counter covers) → rebuild the affected
    /// keycaps in place on their existing anchors; the rebuild bakes each category's own
    /// travel per instance ([BoardButtons] Confirm/Undo, [BoardDashboard] gear/pin).
    /// </summary>
    private int _tuningVersion;

    private void ApplyButtonTuningIfChanged()
    {
        if (_root == null || _tuningVersion == WorldUI.ButtonTuning.Version)
            return;
        _tuningVersion = WorldUI.ButtonTuning.Version;
        RebuildAttachedControls();
        RebuildDashboardButtons();
        VRLog.Info("Cards", "Board: ButtonTuning changed → Confirm/Undo/gear/follow keycaps " +
                            $"rebuilt live — {WorldUI.ButtonTuning.Describe()}.");
    }

    /// <summary>Rebuild the gear + follow-toggle keycaps on their existing anchors (ButtonTuning live-apply).</summary>
    private void RebuildDashboardButtons()
    {
        if (_root == null)
            return;
        if (_followToggle != null)
        {
            Object.DestroyImmediate(_followToggle.gameObject);
            _followToggle = null;
        }
        if (_gear != null)
        {
            Object.DestroyImmediate(_gear.gameObject);
            _gear = null;
        }
        LaserTargets.RemoveAll(static t => t.Collider == null);
        CreateDashboardButtons();
    }

    /// <summary>Remove laser targets whose collider was destroyed (e.g. a rest-button rebuild).</summary>
    internal void PurgeDeadLaserTargets() => LaserTargets.RemoveAll(static t => t.Collider == null);

    // ------------------------------------------------------------------ board-switch pose --

    /// <summary>
    /// PART D: capture the live board's world pose so a board SWITCH can re-apply it to the
    /// new board (instead of re-anchoring to the head). Returns false when no board exists.
    /// </summary>
    internal bool TryCapturePose(out Vector3 position, out Quaternion rotation, out Vector3 localScale)
    {
        if (_root == null)
        {
            position = default;
            rotation = Quaternion.identity;
            localScale = Vector3.one;
            return false;
        }
        position = _root.position;
        rotation = _root.rotation;
        localScale = _root.localScale;
        return true;
    }

    /// <summary>
    /// PART D: re-apply a captured world pose to a freshly built board on a SWITCH — the new
    /// board spawns in the EXACT same place instead of re-placing at the head. Marks the tray
    /// placed and re-pins it (PINNED) at the preserved pose.
    /// </summary>
    internal void RestorePose(Vector3 position, Quaternion rotation, Vector3 localScale)
    {
        if (_root == null)
            return;
        _root.position = position;
        _root.rotation = rotation;
        _root.localScale = localScale;
        _placed = true;
        _placementDeferLogged = false;
        if (!CardsConfig.TrayFollow.Value && _root.parent != _pinRoot)
            ApplyFollowMode(); // re-pin at the preserved world pose (worldPositionStays)
        VRLog.Info("Cards", "Control board switch: preserved the previous board's world pose (no re-place at head).");
        LogBoardFaceDiagnostics();
    }

    /// <summary>
    /// Persist the CURRENT root pose back into the config (called by
    /// the shared <see cref="WorldUI.PanelGrabHandle"/> when the last gripping hand lets go): the inverse
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

        // Yaw: heading of the tray's flat forward relative to the head's. PART B: the pose now
        // uses the per-board tilt for the pitch and adds BoardYaw on top of TrayYaw, so undo both
        // here to recover the grab-written TrayYaw (BoardYaw/Tilt seeded so Oak is unchanged).
        ControlBoard board = CardsConfig.CurrentBoard;
        Vector3 trayFlat = _root.rotation * Quaternion.Euler(-(90f - CardsConfig.BoardTilt(board).Value), 0f, 0f)
                           * Vector3.forward;
        trayFlat.y = 0f;
        if (trayFlat.sqrMagnitude > 1e-4f)
        {
            float headHeading = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
            float trayHeading = Mathf.Atan2(trayFlat.x, trayFlat.z) * Mathf.Rad2Deg;
            CardsConfig.TrayYaw.Value = Mathf.DeltaAngle(headHeading, trayHeading) - CardsConfig.BoardYaw(board).Value;
        }
        // Divide out the per-board multiplier so TrayScale keeps its raw 0.5–2 grab semantics.
        float boardScale = Mathf.Max(0.01f, CardsConfig.BoardScale(board).Value);
        CardsConfig.TrayScale.Value = Mathf.Clamp(_root.localScale.x / boardScale, 0.5f, 2f);
        VRLog.Info("Cards", $"Tray layout persisted: fwd {CardsConfig.TrayForward.Value:F2} m, " +
                            $"right {CardsConfig.TrayRight.Value:F2} m, down {CardsConfig.TrayDown.Value:F2} m, " +
                            $"yaw {CardsConfig.TrayYaw.Value:F0}°, scale {CardsConfig.TrayScale.Value:F2}×.");
    }

    // ------------------------------------------------------------------ slots --

    internal VRCard? Occupant(int slot) => _occupants[slot];

    /// <summary>
    /// Home offset that seats a card ON the physical recess surface instead of at the
    /// bundle anchor's mid-plane centre (test #28): a small push toward the viewer
    /// (the board's -Z face), tuned by [Cards] SlotCardInset. Shared by every path
    /// that parks a card in a slot — <see cref="PlaceCard"/>, <see cref="PlacePickCard"/>
    /// and HalfSelection's docked action cards — so the seating is consistent and
    /// tunable in one place. The slot itself carries the tray tilt/scale; the card
    /// inherits both.
    /// </summary>
    internal static Vector3 SlotHomeOffset => SlotHomeOffsetFor(0, applySpread: false);

    /// <summary>
    /// Item B (couple the resting card to the Overlays element): the slot-local home offset a card
    /// takes, now including the per-board <see cref="CardsConfig.SlotOverlayOffset"/> (X/Y in plane,
    /// Z proud) and — when <paramref name="applySpread"/> — the <see cref="CardsConfig.SlotOverlaySpacing"/>
    /// pair spread (slot 0 −½, slot 1 +½). Tuning the debug-menu "Overlays" element therefore moves
    /// the actual SLOT where a placed card physically rests together with its snap/wanted glows (which
    /// take the identical offset in <see cref="BuildSlotHighlights"/>/<see cref="BuildWantedHighlights"/>).
    /// The base inset (−SlotCardInset toward the viewer) is unchanged.
    /// </summary>
    internal static Vector3 SlotHomeOffsetFor(int slot, bool applySpread = true)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        Vector3 ov = CardsConfig.SlotOverlayOffset(b).Value;
        float xSpread = applySpread
            ? (slot == 0 ? -0.5f : 0.5f) * CardsConfig.SlotOverlaySpacing(b).Value
            : 0f;
        return new Vector3(ov.x + xSpread, ov.y, -CardsConfig.SlotCardInset.Value + ov.z);
    }

    /// <summary>
    /// ITEM 3: the home scale a card takes when it seats in a slot — it grows to (nearly)
    /// fill the recess. Multiplies on top of the slot frame's inherited 1.3× SlotScale;
    /// tuned per board via <c>[Cards] SlotCardFill</c>. Shared by every slot-home path
    /// (<see cref="PlaceCard"/>, <see cref="PlacePickCard"/>, HalfSelection docked cards)
    /// so a slotted card is the same size regardless of how it got there.
    /// </summary>
    internal static float SlotCardScale => Mathf.Max(0.1f, CardsConfig.SlotCardFill.Value);

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

    // ------------------------------------------------------------------ pick field --

    private Transform? _pickField;
    private GameObject? _pickFieldHighlight;
    private bool _pickFieldVisible;
    private bool _pickFieldHighlighted;

    /// <summary>Card anchor of the pick drop field (null until built). Cards home at scale 1.</summary>
    internal Transform? PickFieldAnchor => _pickField;

    /// <summary>True while a modal pick mode shows the drop field (CardsDriver drives this).</summary>
    internal bool PickFieldVisible => _pickFieldVisible;

    /// <summary>
    /// The DROP FIELD for the modal pick flows (test #21 B): a single slot-style
    /// frame in the CENTER of the slot zone — lay a candidate card onto it to
    /// select it (same accept mechanics as the play slots: highlight-on-hover
    /// primary rule + capture-radius fallback, CardsDriver routes the release).
    /// While visible it REPLACES the two play-slot visuals (SetPickFieldVisible
    /// toggles the slot roots): the slots are guaranteed empty in pick modes
    /// (Rebuild calls ClearSlots outside CardsSelection), and two empty slot
    /// frames flanking a third frame read as three competing targets.
    /// Layout: anchored at the slot-zone center (0, 0.015) with the slots'
    /// 1.3× SlotScale — the field card reads exactly like a slotted card. Frame
    /// half-extents ≈ (0.046, 0.064)·1.3 → x ±0.060, y -0.068..+0.098: clear of
    /// the rest plate (right edge -0.1925), the CONFIRM column (left edge
    /// 0.1735) and the cluster mount (top edge -0.073 — the game's live confirm
    /// mirror docks DIRECTLY under the field, see BuildMounts). The mode's
    /// confirm affordance is therefore already adjacent on two sides: the
    /// ButtonCluster Ready below (mirrors ReadyButton in all 16 states incl.
    /// EREADYBUTTONRECOVERCARD "Confirm") and the tray CONFIRM to the right
    /// (accented while the field shows, see TickStatus).
    /// </summary>
    private void BuildPickField()
    {
        if (_root == null || _pickField != null)
            return;
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        _pickField = new GameObject("PickField").transform;
        _pickField.SetParent(_root, worldPositionStays: false);
        _pickField.localPosition = new Vector3(0f, 0.015f, 0f);
        _pickField.localScale = Vector3.one * SlotScale; // field card = slot card density (test #18)

        var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        frame.name = "Frame";
        Object.Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(_pickField, worldPositionStays: false);
        frame.transform.localScale = new Vector3(w * 1.12f, h * 1.12f, 1f);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.003f);
        Tint(frame, new Color(0.62f, 0.42f, 0.18f)); // warm accent — distinct from the play slots

        var inner = GameObject.CreatePrimitive(PrimitiveType.Quad);
        inner.name = "FrameInner";
        Object.Destroy(inner.GetComponent<Collider>());
        inner.transform.SetParent(_pickField, worldPositionStays: false);
        inner.transform.localScale = new Vector3(w * 1.04f, h * 1.04f, 1f);
        inner.transform.localPosition = new Vector3(0f, 0f, 0.0025f);
        Tint(inner, new Color(0.12f, 0.10f, 0.08f));

        // Snap-glow behind the frame — the same telegraph the play slots use.
        var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glow.name = "FieldHighlight";
        Object.Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(_pickField, worldPositionStays: false);
        glow.transform.localScale = new Vector3(w * 1.24f, h * 1.24f, 1f);
        glow.transform.localPosition = new Vector3(0f, 0f, -0.006f); // PROUD of the top (toward the player)
        // Emissive gold via Overlay (additive). CORE FIX: NEGATIVE local-Z (proud) and NO
        // RenderOnTop — the pick-field insert telegraph is depth-correct now, no shine-through.
        Material? glowMat = MakeGlowMaterial(new Color(1f, 0.85f, 0.3f, 0.95f));
        if (glowMat != null)
            glow.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
        glow.SetActive(false);
        _pickFieldHighlight = glow;

        // Caption under the field (box metrics divide by SlotScale — test #18 pattern).
        AddCaption(_pickField, new Vector3(0f, -h * 0.62f, -0.004f),
            Core.Loc.Game("GUI_SELECT", "SELECT"), new Color(1f, 0.85f, 0.55f),
            maxUpper: true, width: 0.14f / SlotScale, height: 0.024f / SlotScale,
            maxFontSize: 0.28f / SlotScale);

        _pickField.gameObject.SetActive(false);
    }

    /// <summary>
    /// Show/hide the pick drop field; while shown the two play-slot visuals hide
    /// (see <see cref="BuildPickField"/>) and come back on hide. No-ops unless the
    /// state changes.
    /// </summary>
    internal void SetPickFieldVisible(bool visible)
    {
        if (_pickField == null || _pickFieldVisible == visible)
            return;
        _pickFieldVisible = visible;
        _pickField.gameObject.SetActive(visible);
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot != null && slot.gameObject.activeSelf == visible)
                slot.gameObject.SetActive(!visible);
        }
        if (!visible)
            SetPickFieldHighlight(false);
        VRLog.Info("Cards", $"Board: pick drop field {(visible ? "shown (slots hidden)" : "hidden (slots restored)")}.");
    }

    /// <summary>
    /// Would the field capture a card released now? Same generous dual-sample rule
    /// as <see cref="SlotNear"/> (card center OR holding palm within the capture
    /// radius, test #13) with the distances exposed for the one-line drop log.
    /// </summary>
    internal bool PickFieldNear(Vector3 cardPos, Vector3 handPos, out float dist, out float radius)
    {
        dist = float.PositiveInfinity;
        radius = 0f;
        if (_pickField == null || !_pickFieldVisible || _root == null || !IsVisible)
            return false;
        radius = SlotCaptureRadius * _root.lossyScale.x;
        dist = Mathf.Min(
            Vector3.Distance(cardPos, _pickField.position),
            Vector3.Distance(handPos, _pickField.position));
        return dist <= radius;
    }

    /// <summary>Snap-preview glow on the field (change-gated, like SetHighlightedSlot).</summary>
    internal void SetPickFieldHighlight(bool highlighted)
    {
        if (_pickFieldHighlighted == highlighted)
            return;
        _pickFieldHighlighted = highlighted;
        if (_pickFieldHighlight != null && _pickFieldHighlight.activeSelf != highlighted)
            _pickFieldHighlight.SetActive(highlighted);
    }

    // ------------------------------------------------------------------ highlight --

    private readonly GameObject?[] _slotHighlights = new GameObject?[2];
    private int _highlightedSlot = -1;

    // ---- steady "wanted slot" hint (test #28) ------------------------------------------
    // A softly PULSING accent behind a slot marks where the game is currently waiting
    // for a card — the still-empty play slot(s) during selection, or the LEFT slot
    // during a single-card pick flow. Deliberately distinct from the transient gold
    // snap glow above (_slotHighlights): a different hue (teal), a larger rim, and it
    // pulses so a steady "drop here" hint never reads as the "card will land here on
    // release" preview. The driver toggles it via SetWantedSlots; PlayTray owns only
    // the visuals (pulse is self-animated by SlotPulse, no PlayTray Update needed).
    private readonly GameObject?[] _wantedHighlights = new GameObject?[2];
    private int _wantedMask = -1;

    /// <summary>True while a modal single-card pick flow is live (drives the CONFIRM accent, test #28).</summary>
    private bool _pickActive;

    /// <summary>The driver marks pick flows so CONFIRM accents as the mode's mirrored confirm affordance.</summary>
    internal void SetPickActive(bool active) => _pickActive = active;

    /// <summary>
    /// Glow frame behind each slot — shown while a HELD card is within snap range
    /// (test #13: telegraph exactly where the card will zap on release). Unlit
    /// bright gold so it reads emissive in the unlit void scenes.
    /// </summary>
    private void BuildSlotHighlights()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        Vector3 ov = CardsConfig.SlotOverlayOffset(CardsConfig.CurrentBoard).Value; // PART B: per-board overlay offset
        float ovSpacing = CardsConfig.SlotOverlaySpacing(CardsConfig.CurrentBoard).Value; // item 1: pair spacing
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null || _slotHighlights[i] != null)
                continue;
            float xSpread = (i == 0 ? -0.5f : 0.5f) * ovSpacing; // spread the overlay pair apart along the slot axis
            // Emissive gold via the shared glow-quad helper (Overlay additive): reads as light
            // ADDED over the board, NEGATIVE local-Z (proud toward the player), depth-correct (no
            // RenderOnTop, occludes naturally). The hand-fan insertion overlay uses this SAME
            // helper so the two telegraphs look identical.
            _slotHighlights[i] = CardGlow.CreateGlowQuad("SlotHighlight",
                slot!,
                new Vector3(w * 1.24f, h * 1.24f, 1f),
                new Vector3(ov.x + xSpread, ov.y, SlotGlowBaseZ + ov.z),
                new Color(1f, 0.85f, 0.3f, 0.95f));
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
    /// Steady "wanted slot" hint (test #28): a larger teal rim behind the slot that
    /// PULSES via <see cref="SlotPulse"/> — distinct from the gold snap glow. Built as
    /// a slot child so it inherits the slot's SlotScale and pose, hidden by default.
    /// </summary>
    private void BuildWantedHighlights()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        Vector3 ov = CardsConfig.SlotOverlayOffset(CardsConfig.CurrentBoard).Value; // PART B: per-board overlay offset
        float ovSpacing = CardsConfig.SlotOverlaySpacing(CardsConfig.CurrentBoard).Value; // item 1: pair spacing
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null || _wantedHighlights[i] != null)
                continue;
            float xSpread = (i == 0 ? -0.5f : 0.5f) * ovSpacing; // spread the overlay pair apart along the slot axis
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "WantedHighlight";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(slot, worldPositionStays: false);
            // Larger rim than the snap glow (1.24×) so the teal reads AROUND the gold when both
            // show; sits a hair less proud (base z -0.004) than the snap glow (-0.006) so the gold
            // snap draws in front of the teal, preserving the old ordering.
            quad.transform.localScale = new Vector3(w * 1.36f, h * 1.36f, 1f);
            quad.transform.localPosition = new Vector3(ov.x + xSpread, ov.y, WantedGlowBaseZ + ov.z); // PROUD toward the player + per-board offset + pair spacing
            var renderer = quad.GetComponent<MeshRenderer>();
            var baseColor = new Color(0.25f, 0.85f, 0.6f, 0.7f); // teal accent — the "drop here" hint
            // Emissive teal via Overlay (additive). CORE FIX: NEGATIVE local-Z (proud, toward the
            // player) and NO RenderOnTop — depth-correct, no shine-through.
            Material? mat = MakeGlowMaterial(baseColor);
            if (mat != null)
                renderer.sharedMaterial = mat;
            quad.AddComponent<SlotPulse>().Init(renderer, baseColor);
            quad.SetActive(false);
            _wantedHighlights[i] = quad;
        }
    }

    // ------------------------------------------------------------------ item-use slot --

    /// <summary>
    /// Build the ITEM-USE clip-in slot (items rework, requirement 3): a card-sized recess
    /// UNDER the board next to the Confirm/Undo buttons — a gold Frame + darker inner + a
    /// pulsing "drop here" glow + a localized "USE" caption. Positioned at
    /// <see cref="ItemUseSlotBase"/> + the per-board <see cref="CardsConfig.ItemUseSlotOffset"/>
    /// (debug-menu tunable, live-applied via <see cref="SetItemUseSlotOffset"/>). Built ONCE and
    /// starts HIDDEN — <see cref="ItemsPile"/> shows it live only while the local player holds a
    /// usable item card on their own turn, then reads <see cref="ItemUseSlotTransform"/> to
    /// detect a drop-in. Mirrors the procedural slot recess so it reads as a real card slot.
    /// </summary>
    private void BuildItemUseSlot()
    {
        if (_root == null)
            return;
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var go = new GameObject("GloomhavenVR.ItemUseSlot");
        go.transform.SetParent(_root, worldPositionStays: false);
        go.transform.localPosition = ItemUseSlotBase + CardsConfig.ItemUseSlotOffset(CardsConfig.CurrentBoard).Value;
        go.transform.localRotation = _boardFaceFrame; // face the player like the slots / decision buttons

        // Gold frame + darker inner (mirror of the procedural Slot1/Slot2 recess look).
        var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        frame.name = "Frame";
        Object.Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(go.transform, worldPositionStays: false);
        frame.transform.localScale = new Vector3(w * 1.12f, h * 1.12f, 1f);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        Tint(frame, new Color(0.55f, 0.45f, 0.22f));

        var inner = GameObject.CreatePrimitive(PrimitiveType.Quad);
        inner.name = "FrameInner";
        Object.Destroy(inner.GetComponent<Collider>());
        inner.transform.SetParent(go.transform, worldPositionStays: false);
        inner.transform.localScale = new Vector3(w * 1.04f, h * 1.04f, 1f);
        inner.transform.localPosition = new Vector3(0f, 0f, 0.003f);
        Tint(inner, new Color(0.12f, 0.10f, 0.08f));

        // Pulsing warm "drop here to use" glow rim (self-animated, no PlayTray Update).
        var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glow.name = "Glow";
        Object.Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(go.transform, worldPositionStays: false);
        glow.transform.localScale = new Vector3(w * 1.28f, h * 1.28f, 1f);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.0035f); // between frame and inner, proud
        var glowRenderer = glow.GetComponent<MeshRenderer>();
        var glowColor = new Color(1f, 0.82f, 0.35f, 0.8f); // warm gold — the inviting "use" highlight
        _itemUseSlotGlow = MakeGlowMaterial(glowColor);
        if (_itemUseSlotGlow != null)
            glowRenderer.sharedMaterial = _itemUseSlotGlow;
        glow.AddComponent<SlotPulse>().Init(glowRenderer, glowColor);

        // "USE" caption, BELOW the recess — never across it.
        //
        // ROOT CAUSE of "der 'Use'-Text glitcht immer mal wieder vor die Karte und darunter": the
        // caption used to sit at the slot's CENTRE (0, 0, −0.002), i.e. exactly where the clipped-in
        // card lands, and a couple of millimetres proud of it. Two co-planar surfaces two millimetres
        // apart, one of them a card whose face art and backing are pushed into a high render queue
        // (CardMesh.HeldCardRenderQueue / VRCard's render-on-top), decide their order per FRAME and
        // per VIEW ANGLE — so the text flickered in front of the card art and then behind it as the
        // head moved. Nudging the z would only move the flicker.
        //
        // The fix is geometric, not a depth tweak: the caption is parked entirely OUTSIDE the card
        // footprint, one half-card below the recess plus a margin, so there is no overlap left to
        // fight over at any viewing angle. It is a label for the slot, and a slot's label belongs
        // under it — which is also what the user asked for ("er soll fix unter der Karte bleiben").
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, worldPositionStays: false);
        labelGo.transform.localPosition = new Vector3(0f, -(h * 0.5f + ItemUseLabelDrop), -0.002f);
        var label = labelGo.AddComponent<TextMeshPro>();
        // "USE" — localized from the game's own use-item bar key, safe English fallback, uppercased.
        label.text = Core.Loc.Game("GUI_USE", "USE").ToUpperInvariant();
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.92f, 0.72f);
        WorldUI.NativeButtonSkin.ApplyFont(label);
        // Fit into the strip BELOW the card, not into the card's own height — a box as tall as the
        // card would let TMP grow the glyphs back up across the recess and undo the separation.
        Core.TmpFit.Fit(label, w * 1.1f, ItemUseLabelHeight, maxFontSize: 0.16f, wrap: false);

        // Requirement 9a: the item "Use" confirm is no longer a bespoke keycap beside the slot — it is a
        // GENERIC cluster board button in the right-hand Confirm/Undo column (built in BuildButtons,
        // positioned above the pair by SetConfirmUndoOffset), shown only while a card is clipped into the
        // slot awaiting the decision. See BuildButtons / SetItemUseConfirmVisible.

        Core.VRLayers.Apply(go);
        go.SetActive(false); // ItemsPile toggles it live via SetItemUseSlotVisible
        _itemUseSlot = go.transform;
    }

    /// <summary>
    /// Requirement 6 — show/hide the item-use CONFIRM button and set its click action. Called by
    /// <see cref="ItemsPile"/> when a card clips into the slot (visible, with the use callback) and
    /// when the decision resolves (hidden, null). The CANCEL is grabbing the card out, so no button.
    /// </summary>
    internal void SetItemUseConfirmVisible(bool visible, System.Action? onConfirm)
    {
        _itemUseConfirmAction = visible ? onConfirm : null;
        // Requirement 9a: the "Use" confirm is a dynamic member of the generic cluster. Toggling it
        // changes the member COUNT (2 ↔ 3), so the whole Confirm/Undo/Use stack must re-lay-out (and the
        // caps re-size to fit) — rebuild the cluster on an actual change, then show/hide the fresh button.
        if (visible != _itemUseActive)
        {
            _itemUseActive = visible;
            RebuildAttachedControls(); // rebuilds Confirm/Undo (+ Use when active) at the new count
        }
        _itemUseConfirm?.SetVisible(visible);
    }

    /// <summary>Requirement 6 / 4 — is <paramref name="target"/> the item-use CONFIRM button? The board
    /// laser dispatch uses this to EXEMPT a confirm click from the foreign-interaction fan-close (it is
    /// part of the item interaction, not a foreign one).</summary>
    internal bool IsItemUseConfirm(object? target) =>
        _itemUseConfirm != null && ReferenceEquals(_itemUseConfirm, target);

    /// <summary>
    /// The ITEM-USE clip-in slot transform (world pose read by <see cref="ItemsPile"/> for the
    /// drop-in proximity test). Null before the board is built / after teardown.
    /// </summary>
    internal Transform? ItemUseSlotTransform => _itemUseSlot;

    /// <summary>Show/hide the item-use slot (idempotent). Driven live by <see cref="ItemsPile"/>'s gate.</summary>
    internal void SetItemUseSlotVisible(bool visible)
    {
        if (_itemUseSlot != null && _itemUseSlot.gameObject.activeSelf != visible)
            _itemUseSlot.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Live-apply (debug menu / cfg edit): move the item-use slot to <see cref="ItemUseSlotBase"/>
    /// + the new per-board offset (instant). Mirrors <see cref="SetConfirmUndoOffset"/>.
    /// </summary>
    internal void SetItemUseSlotOffset(Vector3 offset)
    {
        if (_itemUseSlot != null)
            _itemUseSlot.localPosition = ItemUseSlotBase + offset;
    }

    /// <summary>
    /// Set which slots the game currently WANTS filled (bit 0 = Slot1, bit 1 = Slot2);
    /// 0 = none. The driver computes the mask from game state each frame; PlayTray
    /// dedupes the visual toggle so a rebuild never restarts the pulse.
    /// </summary>
    internal void SetWantedSlots(int mask)
    {
        if (mask == _wantedMask)
            return;
        _wantedMask = mask;
        for (int i = 0; i < 2; i++)
        {
            GameObject? go = _wantedHighlights[i];
            bool on = (mask & (1 << i)) != 0;
            if (go != null && go.activeSelf != on)
                go.SetActive(on);
        }
    }

    /// <summary>Self-animated soft pulse for a wanted-slot hint quad (no PlayTray Update).</summary>
    private sealed class SlotPulse : MonoBehaviour
    {
        private MeshRenderer? _renderer;
        private Color _base;

        internal void Init(MeshRenderer? renderer, Color baseColor)
        {
            _renderer = renderer;
            _base = baseColor;
        }

        private void Update()
        {
            if (_renderer == null || _renderer.sharedMaterial == null)
                return;
            // Breathe between ~0.30 and ~0.85 — a calm "waiting" pulse. Item 5: the glow
            // now uses the ADDITIVE Overlay shader (where rgb IS the emitted brightness and
            // alpha is unused), so scale the rgb by the breath; also breathe alpha so the
            // alpha-blended Sprites/Default fallback (Overlay absent) still pulses.
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
            float k = Mathf.Lerp(0.30f, 0.85f, t);
            Color c = _base;
            c.r *= k;
            c.g *= k;
            c.b *= k;
            c.a = k;
            _renderer.sharedMaterial.color = c;
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
            card.SetHome(_slots[slot]!, SlotHomeOffsetFor(slot), Quaternion.identity, SlotCardScale, instant); // ITEM 3: fill the recess; Item B: track the overlay offset
            card.SetDockGrabPad(true); // task #2 follow-up: under/around-grab apron while slot-docked
        }
        if (announce)
            VRLog.Info("Cards", $"Board: card placed in slot {slot + 1}.");
    }

    /// <summary>
    /// Home a single-card PICK candidate into a slot recess (test #28): pick 0 → the
    /// LEFT slot (Slot1), pick 1 → the RIGHT slot (Slot2, the burn-two-discarded
    /// flows). Deliberately does NOT touch <see cref="_occupants"/> — pick cards are
    /// tracked by the driver's <c>_fieldCards</c>, not the played-card slot occupancy
    /// (the slots stay logically empty so <see cref="SyncFromGameState"/> /
    /// <see cref="SlotOf"/> keep their CardsSelection meaning). A HELD card is never
    /// re-homed (the phantom-ACCEPT lesson, see <see cref="PlaceCard"/>). Returns the
    /// slot index used, or -1 when it fell back BESIDE Slot2 (index ≥ 2 — rare; the
    /// caller logs the fallback).
    /// </summary>
    internal int PlacePickCard(VRCard card, int index)
    {
        int slotIndex = index < 2 ? index : 1;
        Transform? slot = _slots[slotIndex];
        if (slot == null || card.IsHeld)
            return slotIndex < index ? -1 : slotIndex; // held: skip; caller re-runs on release
        card.gameObject.SetActive(true);
        if (index < 2)
        {
            card.SetHome(slot, SlotHomeOffsetFor(slotIndex), Quaternion.identity, SlotCardScale); // ITEM 3: fill the recess; Item B: track overlay
            card.SetDockGrabPad(true); // task #2 follow-up: pick cards docked in a recess get the same apron
            return slotIndex;
        }
        // Graceful fallback for a 3rd+ pick card (no silent cap): lay it beside Slot2.
        float w = CardsConfig.CardWidth.Value;
        Vector3 off = SlotHomeOffsetFor(1) + new Vector3((index - 1) * w * 1.15f, 0f, 0f);
        card.SetHome(slot, off, Quaternion.identity, SlotCardScale); // ITEM 3: fill the recess
        card.SetDockGrabPad(true);
        return -1;
    }

    internal void RemoveCard(VRCard card)
    {
        int slot = SlotOf(card);
        if (slot >= 0)
        {
            _occupants[slot] = null;
            card.SetDockGrabPad(false); // apron off with the dock (grab already clears it too)
            VRLog.Info("Cards", $"Board: card taken back from slot {slot + 1}.");
        }
    }

    internal void ClearSlots()
    {
        _occupants[0]?.SetDockGrabPad(false);
        _occupants[1]?.SetDockGrabPad(false);
        _occupants[0] = null;
        _occupants[1] = null;
    }

    /// <summary>
    /// Mirror the authoritative round pile into the slots while PRESERVING the player's
    /// FREE physical placement (item A). The game only tracks the SET of two
    /// <c>RoundAbilityCards</c> plus which one leads initiative
    /// (<c>CCharacterClass.InitiativeAbilityCard</c>, CCharacterClass.cs:220) — it does
    /// NOT care which VR slot a card sits in (<c>SwapInitiative</c> merely toggles the
    /// leader + reverses the pair, AbilityCardUI.cs:809). So this no longer FORCES the
    /// initiative card into slot 0; instead it keeps every already-seated round card in
    /// the exact slot the player dropped it, only (a) evicting occupants that left the
    /// round and (b) dropping a NEWLY-selected round card into an empty slot (default
    /// initiative→0 / other→1 purely as the seed when neither is placed yet, e.g. a
    /// mode re-entry). <see cref="CardsDriver.ReconcileInitiative"/> then drives the
    /// game's initiative to follow whatever card the player put in slot 0 — the inverse
    /// of the old game→slot mapping that fought the player's placement. Returns true
    /// when anything changed. Re-places ONLY what changed (test #14): this runs on every
    /// rebuild — unconditional re-placing re-parents held cards and spams the log.
    /// </summary>
    internal bool SyncFromGameState(CardsHandUI hand, VRCardFactory factory)
    {
        if (hand.PlayerActor == null)
            return false;
        var round = hand.PlayerActor.CharacterClass.RoundAbilityCards;
        ScenarioRuleLibrary.CAbilityCard? initiative = hand.PlayerActor.CharacterClass.InitiativeAbilityCard;

        // Resolve the round cards to their VRCards, tagging the initiative (leading) one
        // so a brand-new pair gets a sensible default seat (initiative → slot 0).
        VRCard? roundInit = null, roundOther = null;
        for (int i = 0; i < round.Count && i < 2; i++)
        {
            AbilityCardUI? widget = FindWidget(hand, round[i]);
            if (widget == null)
                continue;
            VRCard card = factory.GetOrCreate(widget);
            bool isInitiative = initiative != null ? round[i] == initiative : i == 0;
            if (isInitiative && roundInit == null)
                roundInit = card;
            else if (roundOther == null)
                roundOther = card;
            else
                roundInit ??= card;
        }

        bool changed = false;
        // (a) Evict any occupant that is no longer one of the two round cards (unselected
        // / swapped out) — its slot frees up for the surviving/new card.
        for (int s = 0; s < 2; s++)
        {
            VRCard? occ = _occupants[s];
            if (occ != null && occ != roundInit && occ != roundOther)
            {
                occ.SetDockGrabPad(false); // evicted from the slot → apron off
                _occupants[s] = null;
                changed = true;
            }
        }
        // (b) Seat any round card that is not already in a slot into an empty slot,
        // preferring its default seat (initiative → 0, other → 1) but taking whichever
        // slot is free — the player's own drops already placed most cards, so this only
        // fires for cards the GAME selected without a VR drop (mode re-entry / undo).
        changed |= PlaceRoundCardIfMissing(roundInit, preferSlot: 0);
        changed |= PlaceRoundCardIfMissing(roundOther, preferSlot: 1);
        return changed;
    }

    /// <summary>
    /// Item A helper: seat <paramref name="card"/> into a slot ONLY if it is not already
    /// an occupant (preserving the player's free placement). Prefers
    /// <paramref name="preferSlot"/>, falls back to the other empty slot. No-op when the
    /// card is null or already seated.
    /// </summary>
    private bool PlaceRoundCardIfMissing(VRCard? card, int preferSlot)
    {
        if (card == null || _occupants[0] == card || _occupants[1] == card)
            return false;
        int slot = _occupants[preferSlot] == null ? preferSlot
            : _occupants[1 - preferSlot] == null ? 1 - preferSlot : -1;
        if (slot < 0)
            return false;
        PlaceCard(card, slot);
        return true;
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

    // Last shown round number (change-gated; int.MinValue = never). Rebuilding the
    // string only on CHANGE avoids a per-frame ToString allocation AND a per-frame TMP
    // text assignment — every rewrite re-triggers TMP's auto-size layout (test #13).
    private int _roundShown = int.MinValue;

    // Confirmed-state label ("✓ <GUI_READY>"), built once — TickStatus runs per
    // frame and the concat would allocate every tick (badge/round lesson, test #13).
    private string? _confirmedLabel;

    // Language the follow/gear labels + cached round/confirmed strings were built in.
    // The TickStatus guard re-localizes them on an actual game-language change (live follow).
    private string _labelLang = string.Empty;

    /// <summary>Update round readout, badge, confirm/undo button states + labels (each frame while visible; cheap).</summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        // ButtonTuning live-apply (user #9): geometry entries rebuild the keycaps in place.
        ApplyButtonTuningIfChanged();

        // Task #2 follow-up: re-assert the slot-dock grab apron every tick (idempotent
        // flag check inside SetDockGrabPad) — a card that entered occupancy while HELD
        // (PlaceCard skips the held card, "the release path homes it") gets its apron
        // the moment it rests in the slot, regardless of which path homed it.
        for (int s = 0; s < 2; s++)
        {
            VRCard? occ = _occupants[s];
            if (occ != null && !occ.IsHeld)
                occ.SetDockGrabPad(true);
        }

        // Live language following: the follow/gear labels are set at events only and the
        // round/confirmed strings are cached, so on an ACTUAL language change re-label the
        // frame buttons and invalidate the caches (the per-tick logic below re-localizes the
        // round readout + confirm/undo labels via CardsGameApi/Loc.Game). Change-gated so the
        // TMP writes never happen per frame.
        string lang = Core.Loc.CurrentLanguage;
        if (lang != _labelLang)
        {
            _labelLang = lang;
            _confirmedLabel = null;     // rebuild "✓ READY" in the new language
            _roundShown = int.MinValue; // force the round readout to re-localize
            if (_followToggle != null)
                _followToggle.SetLabel(CardsConfig.TrayFollow.Value
                    ? Core.Loc.Mod("follow") : Core.Loc.Mod("pinned"));
            _gear?.SetLabel(Core.Loc.Mod("set"));
        }

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
                            Core.Loc.Game("GUI_START_ROUND_BANNER", "Round {0}"), round);
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

        // Test #24 item 3: the mod-drawn initiative badge over slot 0 is GONE — the
        // current initiative already reads on the docked initiative track (top edge),
        // so the redundant number circle was removed. Physical card swap still swaps
        // initiative (CardsDriver drives it on the slot gesture). We still read the
        // ready state here for the CONFIRM accent below.
        bool ready = hand != null && CardsGameApi.IsSelectionReady(hand);

        // Solo-host card-selection rescue (bug #5b): when hosting online with no other
        // players the game leaves BOTH commit affordances dead (SP ReadyButton
        // deactivated, MP ready toggle forced non-interactable until someone connects),
        // so the round can never advance. Re-run the game's own re-enable each tick while
        // stuck; a no-op in every other case (≥2 players, offline, other phases). Once it
        // flips the toggle interactable the CONFIRM visibility below surfaces the button.
        CardsGameApi.EnsureSoloHostSelectionCommittable();

        // Test #23 item 4: the REAL ReadyButton / UndoButton dock at these same
        // positions when the native-controls surface is active. While a native
        // widget holds, its mod-drawn twin hides (they overlap) and its state mirror
        // is skipped; when it undocks (feature off / widget hidden / no tray) the mod
        // button reappears with its full state logic — never a missing control.
        // Item 7: hide the mod Confirm ONLY when a REAL, VISIBLE native Continue replaces
        // it. In the "all cards of all characters placed" state the native ReadyButton
        // stays docked + interactable while the game drives its canvasGroup alpha to ~0
        // (VR hides the 2D stack) — docked but not rendering. Gating on ContinueDocked
        // alone hid the mod Confirm too, leaving nothing visible yet still pressable
        // (the docked host's raycaster). Gate on docked AND visible so the mod Confirm
        // shows whenever it is the only thing the player can actually see/press; the
        // docked-but-invisible host's raycaster is stood down in TrayControlDockSurface.
        if (_confirm != null
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueDocked
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueVisible)
        {
            _confirm.SetVisible(false);
        }
        else if (_confirm != null)
        {
            // Ready-state mirror (test #19): while THIS hand's player has confirmed
            // (online card selection — the only game state where a confirm persists
            // and is revocable, see CardsGameApi.ReadyToggle), the button flips to
            // a distinct gold "✓ …" state; pressing it then REVOKES through the
            // game's own un-ready path (CardsDriver.OnConfirmRequested). Switching
            // the active hand re-reads the state each tick, so the display always
            // tracks the shown character. Offline the game has no confirmed-waiting
            // state (END SELECTION starts the round at once) — normal affordance.
            bool confirmed = hand != null && CardsGameApi.IsConfirmed(hand);
            bool canConfirm = hand != null
                              && (CardsGameApi.CanConfirm() || CardsGameApi.ReadyToggleAvailable());
            // Item 7 ("wenn es nicht drückbar ist dann soll es dort auch nicht erscheinen"):
            // only SHOW confirm when its action is actually possible right now — a valid
            // confirmable selection exists (canConfirm) or the player has confirmed and can
            // revoke (confirmed). Otherwise HIDE it (not merely disable), so an unpressable
            // button never appears; it returns the instant the action becomes possible.
            bool show = canConfirm || confirmed;
            _confirm.SetVisible(show);
            if (show)
            {
                // Pick flows (test #28): during a single-card pick, CONFIRM is the mode's
                // mirrored confirm affordance (the ReadyButton path the recover flows arm) —
                // accent it as soon as the game reports it fireable.
                _confirm.SetState(true,
                    accent: (ready || _pickActive) && canConfirm && !confirmed, confirmed: confirmed);
                _confirm.SetLabel(confirmed
                    ? _confirmedLabel ??= "✓ " + Core.Loc.Game("GUI_READY", "READY")
                    : CardsGameApi.ReadyToggleAvailable() && !CardsGameApi.CanConfirm()
                        ? Core.Loc.Game("GUI_END_SELECTION", "END SELECTION")
                        : CardsGameApi.ConfirmLabel());
            }
        }
        if (_undo != null && WorldUI.Surfaces.TrayControlDockSurface.UndoDocked)
        {
            _undo.SetVisible(false);
        }
        else if (_undo != null)
        {
            // Item 7: only SHOW undo when there is actually something to undo — hide it
            // (not just disable) otherwise; it reappears the instant an undo is available.
            bool canUndo = hand != null && CardsGameApi.CanUndo();
            _undo.SetVisible(canUndo);
            if (canUndo)
            {
                _undo.SetState(true, accent: false);
                _undo.SetLabel(CardsGameApi.UndoLabel());
            }
        }
    }

    // ------------------------------------------------------------------ build --

    // Board layout constants (local meters; -Z = element/viewer side).
    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    /// <summary>
    /// The board's top (far) edge in board-LOCAL meters (+Y = the board's back/far edge). Board-
    /// anchored floaters (e.g. the pile browse fan) offset UP from here so they hover above the
    /// board face; a board-root child inherits the live board scale + pose automatically.
    /// </summary>
    internal const float BoardTopLocalY = BoardH * 0.5f;
    private const float SlotSpacing = 0.155f; // between slot centers
    private const float RestZoneX = -0.245f;
    private const float ButtonZoneX = 0.235f;

    /// <summary>
    /// The board's authored half width in board-LOCAL meters (fallback for
    /// <see cref="MeasureBoardLocalExtents"/> when the board has no renderers yet).
    /// </summary>
    internal const float BoardHalfWidthLocal = BoardW * 0.5f;

    /// <summary>
    /// Sanity band for the MEASURED top edge (board-local meters past the authored plate). The
    /// visible board (bundled frame + decorations) overhangs the authored plate by a few cm at
    /// most; anything beyond this is a transient outlier (e.g. a card mid-flight that is still
    /// parented under the tray while it animates home) and must never drag a board-anchored
    /// floater — or, worse, the enemy-info clearance — metres into the sky.
    /// </summary>
    private const float BoardExtentSanityMargin = 0.35f;

    /// <summary>Reused scan buffer for <see cref="MeasureBoardLocalExtents"/> (no steady-state allocation).</summary>
    private static readonly List<MeshRenderer> ExtentScratch = new(48);

    /// <summary>
    /// The control board's REAL rendered extents in the board's OWN LOCAL space: the top (far)
    /// edge <paramref name="topLocalY"/> and the half width <paramref name="halfLocalX"/>, both in
    /// board-local metres (multiply by the root's lossy scale for world metres).
    ///
    /// Derived from the tray's combined MESH-RENDERER bounds mapped into board-root-local coords —
    /// so a board TILT does not inflate the extent the way a world AABB would — because the VISIBLE
    /// board (bundled frame + decorations) is LARGER than the authored plate constants: anything
    /// that clears "the board" using <see cref="BoardTopLocalY"/> alone under-estimates the real
    /// edge and still ends up sitting inside the board (the WorldTooltips "still inside" report).
    /// Degrades to the authored constants when the board has no renderers yet (procedural build
    /// mid-frame), and clamps the measurement into a sane band (<see cref="BoardExtentSanityMargin"/>).
    ///
    /// Shared by every surface that must stay clear of the board (<c>WorldUI.WorldTooltips</c>'s
    /// above-the-edge hint anchor and <c>WorldUI.Surfaces.EnemyRevealSurface</c>'s spawn clearance),
    /// so both see the same board and neither carries its own copy of this math.
    /// </summary>
    internal static void MeasureBoardLocalExtents(Transform root, out float topLocalY, out float halfLocalX)
    {
        topLocalY = BoardTopLocalY;      // authored fallbacks
        halfLocalX = BoardHalfWidthLocal;
        if (root == null)
            return;

        ExtentScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, ExtentScratch);
        Matrix4x4 worldToLocal = root.worldToLocalMatrix;
        bool has = false;
        float maxLocalY = float.NegativeInfinity;
        float maxLocalAbsX = 0f;
        for (int i = 0; i < ExtentScratch.Count; i++)
        {
            MeshRenderer mr = ExtentScratch[i];
            if (mr == null || !mr.enabled)
                continue;

            // Prefer the renderer's own LOCAL mesh bounds mapped through (worldToLocal ×
            // rendererLocalToWorld) → board-root-local, which is tilt-tight; fall back to the
            // renderer's world AABB corners mapped into local space when there is no mesh.
            Bounds b;
            Matrix4x4 toBoardLocal;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            Mesh? mesh = mf != null ? mf.sharedMesh : null;
            if (mesh != null)
            {
                b = mesh.bounds;
                toBoardLocal = worldToLocal * mr.transform.localToWorldMatrix;
            }
            else
            {
                b = mr.bounds; // world AABB
                toBoardLocal = worldToLocal;
            }

            Vector3 c = b.center, e = b.extents;
            for (int s = 0; s < 8; s++)
            {
                Vector3 corner = c + new Vector3(
                    (s & 1) == 0 ? -e.x : e.x,
                    (s & 2) == 0 ? -e.y : e.y,
                    (s & 4) == 0 ? -e.z : e.z);
                Vector3 local = toBoardLocal.MultiplyPoint3x4(corner);
                if (local.y > maxLocalY)
                    maxLocalY = local.y;
                float absX = Mathf.Abs(local.x);
                if (absX > maxLocalAbsX)
                    maxLocalAbsX = absX;
                has = true;
            }
        }
        ExtentScratch.Clear();
        if (!has)
            return;

        topLocalY = Mathf.Clamp(maxLocalY, BoardTopLocalY, BoardTopLocalY + BoardExtentSanityMargin);
        halfLocalX = Mathf.Clamp(maxLocalAbsX, BoardHalfWidthLocal, BoardHalfWidthLocal + BoardExtentSanityMargin);
    }

    /// <summary>
    /// The CONTROL BOARD's REAL top edge in WORLD space, horizontally centred on the board (board-
    /// local X 0) and on the board face plane (local Z 0). <c>TransformPoint</c> carries the live
    /// pose, tilt and lossy scale, so callers re-read it every tick and stay aligned through grabs,
    /// resizes and board switches. See <see cref="MeasureBoardLocalExtents"/> for the derivation.
    /// </summary>
    internal static Vector3 BoardTopEdgeWorld(Transform root)
    {
        MeasureBoardLocalExtents(root, out float topLocalY, out _);
        return root.TransformPoint(new Vector3(0f, topLocalY, 0f));
    }

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

        // Rest zone (test #24 item 3): backdrop + two button anchors. RestControls
        // builds a comfortable BoardButton at each anchor (short rest on top, long
        // rest below — they sit together); the REAL native "Kurze Rast" widget docks
        // over the SHORT anchor (TrayControlDockSurface) and hides the mod short
        // button while it holds. NO mod-drawn rest header anymore: the redundant
        // "Kurze Rast" caption that used to sit above the button duplicated the
        // native widget's own label (each button already carries its localized
        // caption). Plate widened to 0.14 to seat the wider button footprints;
        // centered at RestZoneX -0.245 it spans x -0.315..-0.175 (left edge clears
        // the board edge -0.32; right edge clears slot 0's left content edge ≈ -0.129
        // by 0.046), y -0.10..0.08 (height 0.18).
        var restBack = GameObject.CreatePrimitive(PrimitiveType.Quad);
        restBack.name = "RestZone";
        Object.Destroy(restBack.GetComponent<Collider>());
        restBack.transform.SetParent(_root, worldPositionStays: false);
        restBack.transform.localScale = new Vector3(0.14f, 0.18f, 1f);
        restBack.transform.localPosition = new Vector3(RestZoneX, -0.01f, 0.003f);
        Tint(restBack, new Color(0.12f, 0.11f, 0.10f));

        // Button anchors: the RestControls BoardButton bases sit at z 0.004 in front
        // of the plate (z-order like CONFIRM/UNDO at z -0.006), viewer-side caps
        // proud. Short at y 0.04 (button 0.115×0.04 → y 0.02..0.06), long at y -0.05
        // (→ y -0.07..-0.03): a 0.05 gap between them, both inside the plate.
        _shortRestAnchor = new GameObject("ShortRestToken").transform;
        _shortRestAnchor.SetParent(_root, worldPositionStays: false);
        _shortRestAnchor.localPosition = new Vector3(RestZoneX, 0.04f, -0.006f);

        _longRestAnchor = new GameObject("LongRestToken").transform;
        _longRestAnchor.SetParent(_root, worldPositionStays: false);
        _longRestAnchor.localPosition = new Vector3(RestZoneX, -0.05f, -0.006f);
    }

    /// <summary>
    /// Test #24 item 7: a full-board LASER-RETICLE surface so the ray's hit dot
    /// renders ANYWHERE on the control board's face, not only on its discrete
    /// buttons/panels. An invisible thin plane collider matching the board face,
    /// registered as a laser target — <see cref="CardsDriver"/> ray-tests it
    /// geometrically (Collider.Raycast, layer-independent) with all the other
    /// LaserTargets and clamps the beam to the hit (UiHitOverride → reticle).
    ///
    /// PRECEDENCE (no stolen clicks): the plane sits at z 0.002 — BEHIND every
    /// viewer-side widget (board buttons at z ≤ -0.004, rest/CONFIRM/UNDO anchors
    /// -0.006, the docked native uGUI hosts, the DecisionDock row) and behind the
    /// slotted cards (z 0). CardsDriver keeps the NEAREST hit, so a real button /
    /// token / card always wins where the ray crosses it; the docked native widgets
    /// win through the game's own uGUI raycast (the RayUgui-closer guard stands the
    /// board laser down entirely). The surface only wins where the ray points at
    /// BARE board, where it shows the reticle and does NOTHING else — its
    /// <see cref="BoardSurfaceTarget"/> OnPoke is a no-op, so a trigger on bare board
    /// never activates anything and (as a bonus) the beam no longer passes THROUGH
    /// the floating board to click a hex behind it. Laser reticle only: it is NOT a
    /// PokeableBehaviour and is never registered for fingertip poke.
    /// </summary>
    private void BuildBoardSurface()
    {
        if (_root == null)
            return;
        // The bundled board already registered its real MeshCollider as the laser
        // surface (DEFECT 2, EnsureBuilt) — the synthetic flat plane below is only for
        // the procedural fallback board, which has no mesh collider of its own.
        if (_boardColliderRegistered)
            return;
        var go = new GameObject("BoardSurface");
        go.transform.SetParent(_root, worldPositionStays: false);
        // Between the slotted cards (z 0) and the opaque board face (z 0.004): the
        // reticle sits ~2 mm proud of the board, behind the cards and all widgets.
        go.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(BoardW, BoardH, 0.002f);
        box.isTrigger = true;
        var target = go.AddComponent<BoardSurfaceTarget>();
        RegisterLaserTarget(box, target);
    }

    // Item 6 (test #29): the card-slot captions ("INITIATIVE", "2") are GONE. They
    // sat in the recessed slot-floor plane and clipped THROUGH the seated card at the
    // player's oblique angle; the initiative already reads on the docked track and the
    // second slot needs no label. BuildSlotLabels + its call in EnsureBuilt were
    // removed outright (no no-op stub) — AddCaption stays live for the pick field's
    // "SELECT" caption, so nothing goes unused.

    private void BuildButtons(Transform? confirmAnchor, Transform? undoAnchor)
    {
        if (_root == null)
            return;

        Transform confirmParent = confirmAnchor != null ? confirmAnchor : NewAnchor("ConfirmButton", new Vector3(ButtonZoneX, 0.045f, -0.006f));
        Transform undoParent = undoAnchor != null ? undoAnchor : NewAnchor("UndoButton", new Vector3(ButtonZoneX, -0.06f, -0.006f));

        // PART B + C: Confirm/Undo are real 3D keycaps, sized and positioned from the ACTIVE
        // board's config. The full X/Y/Z offset (X/Y in plane, Z = proud toward the player)
        // REPLACES the old inset + raycast reseat, so the buttons seat at a predictable depth per
        // board (debug-menu tunable). Round-2: the cap SHAPE is per-board (Square boxy keycap by
        // default, or Round disc), and a per-board SPACING spreads Confirm/Undo apart.
        ControlBoard active = CardsConfig.CurrentBoard;
        // Item D: the generic-button area is now a COUNT-DRIVEN cluster. The game can show up to
        // FOUR turn-flow buttons at once (decompiled: Choreographer toggles readyButton + m_SkipButton
        // + m_UndoButton, and occasionally m_selectButton, as independent GameObjects — see
        // e.g. Choreographer.cs:6305-6309), so this consolidated area lays them out auto-fit
        // (SetConfirmUndoOffset). It does NOT auto-scale the caps from the live count — this
        // paragraph used to say it did, and the paragraph six lines below explains why that was
        // taken out. Believe the lower one.
        // Today the mod owns Confirm + Undo here (GenericButtonCount); the real Skip/Select turn-flow
        // buttons still dock via the WorldUI ButtonCluster mount (not one of these files).
        int count = GenericCount; // 2 (Confirm/Undo) or 3 while the item "Use" confirm is a cluster member
        // Cap size is the TUNED size at every count (user: "der Use-Button soll genauso groß sein und
        // sich nach den Werten richten, die die generischen Buttons vorgegeben haben"). It used to be
        // run through GenericClusterButtonSize, which shrank every cap once a third member joined — so
        // the instant an item clipped into the use slot, Confirm and Undo silently resized too and the
        // whole cluster stopped matching the dialled-in values. The stack now makes room by SPACING
        // (GenericClusterY), which is itself a tuned value, so every member is exactly the size the
        // player asked for and the geometry stays theirs.
        float side = CardsConfig.ConfirmUndoSize(active).Value;
        Vector3 off = CardsConfig.ConfirmUndoOffset(active).Value;
        float spacing = CardsConfig.GenericButtonSpacing(active).Value;
        bool round = CardsConfig.GenericButtonShape(active).Value == ButtonShape.Round;

        // Category split (user: "every value applies ONLY to its own category"): the
        // Confirm/Undo keycaps read the [BoardButtons] set EXCLUSIVELY — independent
        // WIDTH/HEIGHT (rectangular keycaps), DEPTH and TRAVEL, all with the authored
        // numeric defaults (0.073 × 0.073 × 0.036 / 4 mm — no 0=Auto sentinel any more).
        // Round caps keep the per-board authored diameter (the square set does not apply).
        WorldUI.ButtonTuning.Bind();
        // Square caps ALWAYS keep the tuned [BoardButtons] W×H, whatever the member count — the item
        // "Use" confirm is built from the very same rectSize/depth/travel below, so it is identical
        // to Confirm and Undo by construction and follows every tuning change with them.
        var rectSize = round
            ? new Vector2(side, side)
            : new Vector2(WorldUI.ButtonTuning.BoardCapWidth, WorldUI.ButtonTuning.BoardCapHeight);
        float capDepth = WorldUI.ButtonTuning.BoardCapDepth;
        float capTravel = WorldUI.ButtonTuning.BoardCapTravel;

        // Initial labels are overwritten by the live game-widget label each TickStatus
        // (ConfirmLabel()/UndoLabel()); route the fallback literals through the game keys.
        _confirm = BoardButton.Create(confirmParent, rectSize,
            new Color(0.35f, 0.46f, 0.28f), // T4: muted sage green — antique, still clearly "go"
            Core.Loc.Game("GUI_CONFIRM", "Confirm"),
            () => ConfirmRequested?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board);
        _confirm.DisabledReason = CardsGameApi.DescribeConfirmGate; // built only on rejection
        _confirm.ActivationGuard = ConfirmGuardRemaining; // accident window (test #19)
        RegisterLaserTarget(_confirm.Collider!, _confirm);

        _undo = BoardButton.Create(undoParent, rectSize,
            new Color(0.44f, 0.31f, 0.20f), // T4: worn leather brown (kept — already antique)
            Core.Loc.Game("GUI_UNDO", "Undo"),
            () => UndoRequested?.Invoke(),
            round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
            capCategory: WorldUI.ButtonTuning.CapCategory.Board);
        _undo.DisabledReason = CardsGameApi.DescribeUndoGate;
        RegisterLaserTarget(_undo.Collider!, _undo);

        // Requirement 9a: while the item "Use" confirm is a live cluster member, build it as a GENERIC
        // cluster board button in THIS column at index 1 (the slot directly ABOVE Undo) — same board
        // keycap look as Confirm/Undo (never the old bespoke keycap beside the slot). Built only while
        // active (SetItemUseConfirmVisible flips _itemUseActive + rebuilds); onClick routes to the
        // ItemsPile-supplied use action.
        if (_itemUseActive)
        {
            _itemUseConfirm = BoardButton.Create(confirmParent, rectSize,
                new Color(0.35f, 0.46f, 0.28f), // muted sage green — the "use / go" accent, like Confirm
                Core.Loc.Game("GUI_USE", "USE"),
                () => _itemUseConfirmAction?.Invoke(),
                round: round, diameter: side, thickness: capDepth, boxy: !round, travel: capTravel,
                capCategory: WorldUI.ButtonTuning.CapCategory.Board);
            _itemUseConfirm.SetState(enabled: true, accent: true); // always pressable while shown (no game gate)
            RegisterLaserTarget(_itemUseConfirm.Collider!, _itemUseConfirm);
        }

        SetConfirmUndoOffset(off, spacing); // count-aware: Confirm(top)/[Use]/Undo(bottom), Z proud
        VRLog.Info("Cards", $"Board: Confirm/Undo built as 3D {(round ? "round" : "square")} keycaps " +
                            $"{rectSize.x:F3}×{rectSize.y:F3} m for {active} (offset {off}, spacing {spacing:F3} m)" +
                            (round ? "." : " — square caps are beveled keycaps: state-colour top + BRIGHT lit bevel ring + dark warm walls (3-submesh, high contrast) for unmistakable 3D."));
        VRLog.Info("Cards", $"Board: button geometry config applied — {WorldUI.ButtonTuning.Describe()}.");
        _tuningVersion = WorldUI.ButtonTuning.Version; // fresh build reflects current config
    }

    private Transform NewAnchor(string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(_root, worldPositionStays: false);
        // PART B: seat the gear / follow-toggle at a FIXED small proud depth toward the player,
        // in place of the old raycast (which floated them up to 5 cm off the board). Predictable
        // and depth-correct; the debug menu does not expose these individually, so a fixed proud
        // keeps them consistent across every board.
        Vector3 seated = new(localPos.x, localPos.y, -FixedProudZ);
        t.localPosition = seated;
        t.localRotation = _boardFaceFrame; // face the functional board face like the bundle anchors
        VRLog.Info("Cards", $"Board: '{name}' seated at a fixed proud local-Z {seated.z * 1000f:F1} mm " +
                            "toward the player (predictable, no raycast).");
        return t;
    }

    /// <summary>
    /// PART B: fixed predictable proud depth (toward the player, −Z) at which the mod-built
    /// HUD widgets the debug menu does NOT expose individually — the settings gear, the
    /// follow-toggle and the round readout — seat, in place of the old unreliable raycast.
    /// Small and depth-correct: they rest just in front of the authored plane, always the
    /// same amount, so they never float 5 cm off the board again.
    /// </summary>
    private const float FixedProudZ = 0.005f;

    /// <summary>
    /// Item A/4: local-Z thickness (meters) of the SQUARE Confirm/Undo keycaps — the total
    /// protrusion toward the player. Raised over successive passes (0.014 → 0.03 → 0.036) so the
    /// side walls + bevel have real area at the board's oblique angle; taller = physically more
    /// side visible (item 4 lever c). Press travel (4 mm) is unchanged. Exact real-world
    /// protrusion is logged per cap by <see cref="BoardButton.LogCapDiagnostics"/>.
    /// </summary>
    private const float SquareCapThickness = 0.036f;

    /// <summary>
    /// Item 4: width (meters) of the lit 45° CHAMFER ring around the front edge of the square
    /// keycaps — the bright "catch-light" bevel that makes the cap read as raised even viewed
    /// near top-down. It consumes this much of both the front plateau inset AND the front depth
    /// (a true 45°). Clamped in <see cref="CardMesh.BuildBeveledKeycap"/> to ≤ 90 % of the cap's
    /// half-size and depth. One line to retune how chunky the lit edge reads (~7 mm ≈ a fat,
    /// clearly-visible chamfer on a ~120 mm cap).
    /// </summary>
    private const float SquareCapBevel = 0.007f;

    /// <summary>Base local-Z of the slot snap-glow (per-board SlotOverlayOffset.z adds on top).</summary>
    private const float SlotGlowBaseZ = -0.006f;

    /// <summary>Base local-Z of the wanted-slot glow (per-board SlotOverlayOffset.z adds on top).</summary>
    private const float WantedGlowBaseZ = -0.004f;

    /// <summary>Ray start standoff in front of the functional face, <c>_root</c>-local meters (mirrors BuildBoard.cs standoff 0.15).</summary>
    private const float SeatStandoff = 0.15f;

    /// <summary>How far PROUD (toward the player, −normal) of the TRUE surface a widget is seated, <c>_root</c>-local meters.</summary>
    private const float SeatProud = 0.003f;

    /// <summary>
    /// Re-seat a bundle anchor (a child of the bundled TrayVisual, NOT a direct <c>_root</c>
    /// child) proud of the TRUE surface: convert its world position into <c>_root</c>-local,
    /// run it through <see cref="SeatOnBoardFace"/>, and write the seated world position back.
    /// No-op when the anchor or the root is missing. Logged (next test-log ground truth).
    /// </summary>
    private void ReseatProud(Transform? anchor, string label)
    {
        if (anchor == null || _root == null)
            return;
        Vector3 before = _root.InverseTransformPoint(anchor.position);
        Vector3 after = SeatOnBoardFace(before);
        anchor.position = _root.TransformPoint(after);
        VRLog.Info("Cards", $"Board: '{label}' anchor re-seated {(after - before).magnitude * 1000f:F1} mm " +
                            $"proud of the true surface (was {before.z * 1000f:F1} mm, now {after.z * 1000f:F1} mm local-Z).");
    }

    /// <summary>
    /// CORE FIX (depth): map an authored <c>_root</c>-local position onto the board's TRUE
    /// local top surface at that point and lift it a few mm PROUD toward the player, so
    /// mod-built widgets (round readout, gear, follow-toggle, re-seated Confirm/Undo/rest
    /// anchors) rest ON the real surface — the raised rim/ornaments — instead of the flat
    /// slot-floor plane (where widgets over a rim were buried and only shone through via
    /// RenderOnTop). Raycasts the stored board MeshCollider(s) from the player side at the
    /// point's OWN XY (the runtime twin of BuildBoard.cs:243-296); only the along-normal
    /// depth is corrected, the in-plane (x/y) component is preserved.
    ///
    /// Fallback when the ray misses or the board has no mesh (procedural board): the previous
    /// slot-floor-plane projection lifted by <see cref="CardsConfig.SlotCardInset"/>. For the
    /// procedural board (identity frame, slots at z 0) that is simply z → −SlotCardInset.
    /// </summary>
    private Vector3 SeatOnBoardFace(Vector3 authoredLocal)
    {
        // nLocal = +Z away from the viewer (INTO the board), in _root-local space; −nLocal = toward the player.
        Vector3 nLocal = _boardFaceFrame * Vector3.forward;

        if (_root != null && _boardColliders.Count > 0)
        {
            // Shoot from SeatStandoff in FRONT of the point (toward the player, −nLocal) straight
            // INTO the board (+nLocal), and take the nearest hit — the true local surface at this XY.
            // Collider.Raycast is world-space + geometric (layer-independent, non-convex OK), so build
            // the ray in world space from _root and convert the hit back to local. Scale-correct: the
            // segment length carries the tray lossyScale through _root.TransformPoint.
            Vector3 startWorld = _root.TransformPoint(authoredLocal - nLocal * SeatStandoff);
            Vector3 endWorld = _root.TransformPoint(authoredLocal + nLocal * SeatStandoff);
            Vector3 segment = endWorld - startWorld;
            float maxDist = segment.magnitude;
            if (maxDist > 1e-5f)
            {
                var ray = new Ray(startWorld, segment / maxDist);
                float best = float.PositiveInfinity;
                Vector3 bestPt = default;
                foreach (Collider c in _boardColliders)
                {
                    if (c == null)
                        continue;
                    if (c.Raycast(ray, out RaycastHit hit, maxDist) && hit.distance < best)
                    {
                        best = hit.distance;
                        bestPt = hit.point;
                    }
                }
                if (!float.IsInfinity(best))
                {
                    Vector3 hitLocal = _root.InverseTransformPoint(bestPt);
                    // Keep the authored XY; move ONLY along the normal onto the hit surface, then proud.
                    float depthToSurface = Vector3.Dot(hitLocal - authoredLocal, nLocal);
                    return authoredLocal + nLocal * depthToSurface - nLocal * SeatProud;
                }
            }
        }

        // Fallback: slot-floor plane projection lifted by SlotCardInset (missed ray / procedural board).
        float inset = CardsConfig.SlotCardInset.Value;
        if (_root == null || _slots[0] == null || _slots[1] == null)
            return authoredLocal - nLocal * inset;
        Vector3 s0 = _root.InverseTransformPoint(_slots[0]!.position);
        Vector3 s1 = _root.InverseTransformPoint(_slots[1]!.position);
        Vector3 faceCenter = (s0 + s1) * 0.5f;
        float depthDiff = Vector3.Dot(faceCenter - authoredLocal, nLocal);
        return authoredLocal + nLocal * depthDiff - nLocal * inset;
    }

    /// <summary>
    /// ITEM 2 ground truth (logged once per board, after placement): for the round
    /// readout, gear, follow toggle, a reference slot and its card — world position, the
    /// readable-face direction (−forward), and how it compares to the board functional-
    /// face normal and the head direction — so an in-game log SHOWS which way each element
    /// faces and whether it sits proud, even without an HMD capture.
    /// </summary>
    private void LogBoardFaceDiagnostics()
    {
        if (_boardDiagLogged || _root == null)
            return;
        _boardDiagLogged = true;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        Vector3 headPos = head != null ? head.transform.position : Vector3.zero;
        Vector3 headDir = head != null ? head.transform.forward : Vector3.forward;
        // Functional face normal toward the viewer, in the CURRENT (placed) world pose.
        Vector3 faceTowardViewer = (_root.rotation * _boardFaceFrame) * Vector3.back;
        VRLog.Info("Cards", $"ITEM2 diag — board face: toward-viewer normal {faceTowardViewer}, " +
            $"head dir {headDir}, head→board {(_root.position - headPos).normalized}, " +
            $"root pos {_root.position}, buildNormal(nF world) {_boardFaceNormalWorld}.");
        LogElementFacing("RoundReadout", _roundLabel != null ? _roundLabel.transform : null, faceTowardViewer, headPos);
        LogElementFacing("SettingsGear", _gear != null ? _gear.transform : null, faceTowardViewer, headPos);
        LogElementFacing("FollowToggle", _followToggle != null ? _followToggle.transform : null, faceTowardViewer, headPos);
        LogElementFacing("Slot0", _slots[0], faceTowardViewer, headPos);
        LogElementFacing("Slot0Card", _occupants[0] != null ? _occupants[0]!.transform : null, faceTowardViewer, headPos);
        // Item A: conclusive square-cap wall diagnostic (real mm thickness + shader + queue).
        _confirm?.LogCapDiagnostics("Confirm");
        _undo?.LogCapDiagnostics("Undo");
        // Item 7b: prove the gear/pin caps are now SOLID OPAQUE beveled keycaps (BoardLit, opaque
        // queue, no alpha-blend, no Overlay/RenderOnTop) like the other keycaps — the diag reports
        // "OPAQUE: YES" for both, settling the old see-through look.
        _gear?.LogCapDiagnostics("Gear/EINST");
        _followToggle?.LogCapDiagnostics("Pin/FIXIERT");
    }

    private static void LogElementFacing(string label, Transform? t, Vector3 faceTowardViewer, Vector3 headPos)
    {
        if (t == null)
        {
            VRLog.Info("Cards", $"ITEM2 diag — {label}: not present.");
            return;
        }
        // TMP / quad / card all read on their LOCAL −Z. The readable face points at the
        // player when −forward aligns with the toward-viewer face normal (dot ~ +1) and
        // back toward the head (headDot > 0).
        Vector3 readable = -t.forward;
        float faceDot = Vector3.Dot(readable, faceTowardViewer);
        Vector3 toElem = (t.position - headPos).normalized;
        float headDot = Vector3.Dot(readable, -toElem);
        VRLog.Info("Cards", $"ITEM2 diag — {label}: pos {t.position}, readable(-Z) {readable}, " +
            $"vs faceNormal dot {faceDot:F2} (want ~+1), vs head dot {headDot:F2} (want >0) → " +
            $"{(headDot > 0.2f ? "FACES the player" : "faces AWAY from the player")}.");
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

    private static void Tint(GameObject go, Color color, bool overlay = false)
    {
        var renderer = go.GetComponent<MeshRenderer>();
        // Items 5/6: board-docked HUD widgets (round-readout plate, gear/follow-toggle
        // bodies) route to the bundled GloomhavenVR/Overlay shader, the ONLY one that
        // exposes _ZTest — so RenderOnTop's SetInt("_ZTest", Always) actually takes and
        // the widget draws over the now-OPAQUE board. Everything else keeps Standard so it
        // stays normally depth-tested. Overlay missing (bundle not updated) → fall back to
        // Standard (widget may be occluded until the new bundle ships).
        Shader? shader = overlay ? OverlayShader() : null;
        shader ??= Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");
        if (shader != null)
            renderer.sharedMaterial = new Material(shader) { color = color };
    }

    // ---- Overlay shader (items 5/6) --------------------------------------------------
    private static Shader? _overlayShader;
    private static bool _overlayFoundLogged;
    private static bool _overlayMissLogged;

    /// <summary>
    /// The bundled <c>GloomhavenVR/Overlay</c> shader (loaded from the asset bundle at
    /// runtime): an unlit shader that — unlike <c>Sprites/Default</c> and <c>Standard</c>
    /// — EXPOSES <c>_ZTest</c>, so <see cref="RenderOnTop"/> can force a board-HUD widget
    /// to draw over the opaque control board (the root cause of the invisible readout /
    /// gear / toggle / glows). Cached; re-found until present so a late bundle load still
    /// resolves. Null when the bundle lacks it — callers fall back to Standard/Sprites and
    /// the widget may be occluded until the new bundle ships. Logged once each way.
    /// </summary>
    internal static Shader? OverlayShader()
    {
        if (_overlayShader == null)
        {
            // A bundled shader is NOT discoverable via Shader.Find until something loads it
            // into memory. BoardLit resolves only because a bundle PREFAB's material
            // references it; GloomhavenVR/Overlay is referenced ONLY by runtime C#, so it is
            // never loaded and Shader.Find returns null (root cause of the STILL-invisible
            // gear/glows in build 0258fbb). Load it explicitly from whichever loaded bundle
            // holds it (the tray/hands bundle is already loaded by the time widgets build).
            _overlayShader = Shader.Find("GloomhavenVR/Overlay");
            if (_overlayShader == null)
            {
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b == null) continue;
                    var s = b.LoadAsset<Shader>("Assets/Bundle/Table/Overlay.shader");
                    if (s != null) { _overlayShader = s; break; }
                }
            }
        }
        if (_overlayShader != null && !_overlayFoundLogged)
        {
            _overlayFoundLogged = true;
            VRLog.Info("Cards", "Overlay shader 'GloomhavenVR/Overlay' loaded — board HUD widgets " +
                                "(round readout, gear/follow-toggle, slot glows) will draw over the opaque board.");
        }
        else if (_overlayShader == null && !_overlayMissLogged)
        {
            _overlayMissLogged = true;
            VRLog.Warn("Cards", "Overlay shader 'GloomhavenVR/Overlay' NOT found (bundle not updated yet) — " +
                                "board HUD widgets fall back to Standard/Sprites and may be occluded by the board.");
        }
        return _overlayShader;
    }

    /// <summary>An Overlay-shader material tinted <paramref name="color"/>, or null when the shader is absent.</summary>
    private static Material? OverlayMaterial(Color color)
    {
        Shader? s = OverlayShader();
        return s != null ? new Material(s) { color = color } : null;
    }

    // ---- BoardLit shader (item 5: solid, shaded button walls) -------------------------
    private static Shader? _boardLitShader;
    private static bool _boardLitFoundLogged;
    private static bool _boardLitMissLogged;

    /// <summary>
    /// Item 5: the bundled <c>GloomhavenVR/BoardLit</c> shader — a self-contained BAKED-lit
    /// shader (two fixed studio directions + an ambient floor, independent of the scene's own
    /// lights). The square keycaps (Confirm/Undo, and Rest when set to Square) used
    /// <c>Shader.Find("Standard")</c>, which strips to the UNLIT <c>Sprites/Default</c> fallback
    /// in the game build — every cube face then rendered the same flat colour, so the side WALLS
    /// never shaded and the button read as a floating flat square ("the walls aren't rendered").
    /// BoardLit shades by world normal, so the box's side walls visibly darken relative to its
    /// front face and it reads as a solid protruding 3D button even in the unlit void/menu scenes
    /// (the same reason the board mesh and the hands use it). Unlike <c>GloomhavenVR/Overlay</c>
    /// it is referenced by bundle PREFAB materials, so <c>Shader.Find</c> resolves it directly;
    /// still re-found until present and probed across loaded bundles, mirroring
    /// <see cref="OverlayShader"/>. Null only when the bundle lacks it — callers fall back to
    /// Standard/Legacy/Sprites (the pre-fix flat look). Logged once each way.
    /// </summary>
    internal static Shader? BoardLitShader()
    {
        if (_boardLitShader == null)
        {
            _boardLitShader = Shader.Find("GloomhavenVR/BoardLit");
            if (_boardLitShader == null)
            {
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b == null) continue;
                    var s = b.LoadAsset<Shader>("Assets/Bundle/Table/BoardLit.shader");
                    if (s != null) { _boardLitShader = s; break; }
                }
            }
        }
        if (_boardLitShader != null && !_boardLitFoundLogged)
        {
            _boardLitFoundLogged = true;
            VRLog.Info("Cards", "BoardLit shader 'GloomhavenVR/BoardLit' loaded — square board buttons " +
                                "get shaded, solid side walls (baked-lit, works in the unlit scenes).");
        }
        else if (_boardLitShader == null && !_boardLitMissLogged)
        {
            _boardLitMissLogged = true;
            VRLog.Warn("Cards", "BoardLit shader 'GloomhavenVR/BoardLit' NOT found — square board buttons " +
                                "fall back to Standard/Sprites and their side walls may read flat.");
        }
        return _boardLitShader;
    }

    // ---- keycap grain texture (task #5a: carved-wood/parchment surface) ----------------
    private const string GrainAlbedoPath = "Assets/Bundle/Table/KeycapGrain_albedo.png";
    private const string GrainNormalPath = "Assets/Bundle/Table/KeycapGrain_normal.png";
    private static Texture2D? _grainAlbedo;
    private static Texture2D? _grainNormal;
    private static bool _grainProbed; // includes the cached "not found" state (no per-material retry)

    /// <summary>
    /// Task #5a: load the shared tileable grain texture ONCE from whichever loaded bundle holds
    /// it (same probe pattern as <see cref="BoardLitShader"/>/<see cref="OverlayShader"/>), so
    /// every keycap material reuses one <see cref="Texture2D"/>. Caches the "not found" state
    /// too, so an older bundle without the texture never retries. Sets Repeat wrap so the planar
    /// keycap UVs (CardMesh.BuildBeveledKeycap) tile cleanly. Logged once each way.
    /// </summary>
    private static void EnsureGrainLoaded()
    {
        if (_grainProbed)
            return;
        _grainProbed = true;
        foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (b == null) continue;
            _grainAlbedo ??= b.LoadAsset<Texture2D>(GrainAlbedoPath);
            _grainNormal ??= b.LoadAsset<Texture2D>(GrainNormalPath);
            if (_grainAlbedo != null && _grainNormal != null) break;
        }
        if (_grainAlbedo != null)
        {
            _grainAlbedo.wrapMode = TextureWrapMode.Repeat;
            if (_grainNormal != null)
                _grainNormal.wrapMode = TextureWrapMode.Repeat;
            VRLog.Info("Cards", $"Keycap grain texture loaded ('{GrainAlbedoPath}'" +
                                $"{(_grainNormal != null ? " + normal map" : "")}) — 3D board keycaps get a " +
                                "carved wood/parchment surface (grayscale grain × per-submesh state tint).");
        }
        else
        {
            VRLog.Info("Cards", $"Keycap grain texture '{GrainAlbedoPath}' not in bundle (older bundle) — " +
                                "board keycaps keep the plain per-state tint (white _MainTex).");
        }
    }

    /// <summary>
    /// Task #5a: a BoardLit keycap material tinted <paramref name="color"/>, with the shared
    /// carved-grain texture assigned to <c>_MainTex</c> (and the normal map to <c>_BumpMap</c>
    /// when present) IF the bundle ships it. BoardLit does <c>alb = tex2D(_MainTex,uv) * _Color</c>,
    /// so a grayscale grain × the per-submesh state colour keeps the top-state / lit-bevel /
    /// dark-wall value signalling while adding surface texture. Graceful fallback: if the grain
    /// texture is absent the material is EXACTLY as before (white _MainTex, plain tint).
    /// </summary>
    internal static Material NewKeycapMaterial(Shader shader, Color color)
    {
        var m = new Material(shader) { color = color };
        EnsureGrainLoaded();
        if (_grainAlbedo != null)
        {
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _grainAlbedo);
            if (_grainNormal != null && m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", _grainNormal);
        }
        return m;
    }

    /// <summary>
    /// Item 5: the lit shader for the square keycap body, falling back to Standard/Legacy/Sprites
    /// when the bundle lacks BoardLit. BoardLit shades side walls even in an unlit scene; the
    /// built-in fallbacks only shade when the scene actually has lights (and go flat otherwise).
    /// </summary>
    private static Shader? BoxCapShader() =>
        BoardLitShader()
        ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");

    /// <summary>
    /// Item 5: an emissive glow material for the slot/pick insert telegraphs. Now delegates to
    /// the shared <see cref="CardGlow.MakeGlowMaterial"/> so the board slot glow and the hand-fan
    /// insertion glow are produced by the SAME recipe (identical look). Behaviour unchanged.
    /// </summary>
    private static Material? MakeGlowMaterial(Color color) => CardGlow.MakeGlowMaterial(color);

    /// <summary>
    /// Items 3/5/7: draw a board-mounted HUD widget ON TOP of the now-OPAQUE two-sided
    /// control board, immune to recess occlusion. The board was made opaque (_Cull=0)
    /// to fill MR passthrough holes; these readout widgets (round number, settings
    /// gear, follow toggle, slot insert-telegraph glows) sit in the recessed slot-floor
    /// plane, BELOW the board's raised outer rim, which now hides them at the player's
    /// oblique angle. Push them past every scene depth: a high render queue (drawn after
    /// the opaque scene) PLUS ZTest Always (never depth-culled by the board rim) and
    /// ZWrite off. The property names differ per shader — the quad/Tint (Standard) path
    /// uses <c>_ZTest</c>, TextMeshPro's distance-field material uses <c>_ZTestMode</c>
    /// — so both are set under HasProperty guards (a shader lacking one is left alone,
    /// never crashes). Uses <c>.materials</c> (per-renderer INSTANCES) so no shared
    /// bundle material is mutated globally. The user has explicitly accepted these as
    /// always-visible board readouts. NOT applied to the seated cards (they stay
    /// normally depth-tested).
    /// </summary>
    private static void RenderOnTop(GameObject go, int queueBase = 4000)
    {
        if (go == null)
            return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            foreach (Material m in r.materials) // instance materials (never the shared bundle asset)
            {
                if (m == null)
                    continue;
                m.renderQueue = queueBase;
                if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                if (m.HasProperty("_ZTestMode")) m.SetInt("_ZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always); // TMP distance-field
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            }
        }
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

    /// <summary>
    /// Full-board laser-reticle target (test #24 item 7, see <see cref="BuildBoardSurface"/>).
    /// A no-op <see cref="IPokeable"/> — the ray beam already clamps to it via
    /// UiHitOverride, so it needs no behavior; a trigger on bare board must do
    /// nothing (never steal a click from a docked widget). DELIBERATELY a plain
    /// MonoBehaviour, not a <see cref="PokeableBehaviour"/>: it is registered ONLY as
    /// a laser target, never with the fingertip-poke registry.
    /// </summary>
    private sealed class BoardSurfaceTarget : MonoBehaviour, IPokeable
    {
        public void OnPokeEnter(VRHand hand) { }
        public void OnPokeExit(VRHand hand) { }
        public void OnPoke(VRHand hand) { }
    }

    /// <summary>
    /// One physical board button: base plate + travelling cap + label. Poke (P2
    /// registry) and laser (tray LaserTargets) both land in <see cref="OnPoke"/>.
    /// Cap presses in ~4 mm on click and springs back (transform anim, no Animator).
    /// </summary>
    internal sealed class BoardButton : PokeableBehaviour
    {
        private System.Action? _onClick;
        private Material? _capMaterial;      // item 4: the TOP-plateau material (carries the state colour)
        private Material? _capBevelMaterial; // item 4: the BRIGHT parchment-lit bevel-ring material (boxy caps); null otherwise
        private Material? _capWallMaterial;  // item 4: the dark warm side-wall material (boxy caps); null otherwise
        private SpriteRenderer? _capFace; // native-skin face (test #25 item 3); null on the procedural fallback
        private Renderer? _capMeshRenderer; // item A diagnostic: the cap body renderer (cube/disc/sprite)

        // ---- Item 1b: SOLID, on-theme keycap palette (aged brass / dark wood / parchment) ----
        // The 3D square cap renders as three submeshes, all driven together from the button
        // STATE colour (disabled / accent / confirmed / dwell) so state signalling is preserved:
        //   • TOP   — the state colour (semantic; T4 antique palette: dark-wood disabled,
        //             warm-parchment available, muted accents, worn-brass readied).
        //   • BEVEL — an AGED-BRASS 45° chamfer ring framing the top (T4: softened from the
        //             old near-white parchment). Still the "catch-light" edge: lighter than
        //             top and wall, angled so it stays visible even near top-down — the
        //             primary "this button is RAISED" cue, now reading as a brass inlay.
        //   • WALL  — a solid, WARM dark-WOOD side band so the cap separates from the board by both
        //             value AND hue, yet still reads as a physical material (not a black void).
        //
        // WHY THE CAP LOOKED "TRANSPARENT / GLASSY" (item 1b root cause): the material is fully
        // OPAQUE (BoardLit, renderQueue 2000, alpha 1 — no alpha-blend anywhere on the cap), but
        // the TOP + WALLS were near-black dark grey (top 0.24, wall 0.13) sitting on a near-black
        // board. Only the bright bevel RING carried any luminance, so the eye saw floating lit
        // edges around dark faces that sank into the background — a wireframe / glass read. The
        // cure is NOT "darker" but SOLID, WARM, opaque material colours on every face so each one
        // reads as a real control-panel key. All colours below are fully opaque (alpha 1); state
        // only TINTS the solid base, it is never the whole washed-out colour. One line each to tune.

        /// <summary>How dark the side WALLS start relative to the top (before the warm lean).</summary>
        private const float WallTintFactor = 0.50f;

        /// <summary>Solid dark-WOOD/iron hue the walls lean toward so they read as material, distinct from the board.</summary>
        private static readonly Color WallWarm = new(0.17f, 0.11f, 0.06f);

        /// <summary>How far (0..1) the wall leans from "darker top" toward <see cref="WallWarm"/>.</summary>
        private const float WallWarmLerp = 0.42f;

        /// <summary>
        /// The AGED-BRASS tone the lit bevel ring is pulled toward (T4 restyle: the ring
        /// reads as a worn brass frame set into the carved dark-wood plaque, not the old
        /// near-white parchment catch-light that over-glowed against the board).
        /// </summary>
        private static readonly Color BevelHighlight = new(0.66f, 0.53f, 0.32f);

        /// <summary>How far (0..1) the bevel is pulled from the top toward <see cref="BevelHighlight"/>.
        /// T4: softened from 0.62 — the raised read survives, the wireframe-bright rim does not.</summary>
        private const float BevelLerp = 0.48f;

        /// <summary>Item 4: dark, warm side-wall colour for a given top/state colour.</summary>
        private static Color WallTint(Color top)
        {
            var dark = new Color(top.r * WallTintFactor, top.g * WallTintFactor, top.b * WallTintFactor, top.a);
            Color w = Color.Lerp(dark, WallWarm, WallWarmLerp);
            w.a = top.a;
            return w;
        }

        /// <summary>Item 4: bright parchment-lit bevel-ring colour for a given top/state colour.</summary>
        private static Color BevelTint(Color top)
        {
            Color b = Color.Lerp(top, BevelHighlight, BevelLerp);
            b.a = top.a;
            return b;
        }

        /// <summary>
        /// Item A conclusive diagnostic (logged once per board, in the placed pose so lossyScale is
        /// real): the cap body's LOCAL scale, the tray/world lossyScale, the resulting REAL cap
        /// thickness in mm (localScale.z × lossyScale.z), plus the material's SHADER NAME and
        /// renderQueue. This settles whether the square cap reads flat because its side walls are
        /// too thin (small mm thickness) or because the material is wrong (not GloomhavenVR/BoardLit,
        /// or an unlit/overlay path that kills wall shading).
        /// </summary>
        internal void LogCapDiagnostics(string label)
        {
            if (_capMeshRenderer == null)
            {
                VRLog.Info("Cards", $"ITEMA cap diag — {label}: no cap mesh renderer (unexpected).");
                return;
            }
            Transform ct = _capMeshRenderer.transform;
            Vector3 lossy = ct.lossyScale;
            // Item 4: the cap mesh is now authored at REAL size with a UNIT-scaled holder, so read
            // the physical extent from the mesh bounds (× world lossyScale), not localScale.
            Mesh? sm = _capMeshRenderer is MeshRenderer meshR && meshR.GetComponent<MeshFilter>() is { sharedMesh: { } fm }
                ? fm : null;
            Vector3 bounds = sm != null ? sm.bounds.size : Vector3.zero;
            float mmThick = Mathf.Abs(bounds.z * lossy.z) * 1000f;
            float mmW = Mathf.Abs(bounds.x * lossy.x) * 1000f;
            float mmH = Mathf.Abs(bounds.y * lossy.y) * 1000f;
            Material? m = _capMeshRenderer.sharedMaterial;
            string shaderName = m != null && m.shader != null ? m.shader.name : "<none>";
            int queue = m != null ? m.renderQueue : -1;
            // Item 4: report the THREE-submesh split (top / bright bevel / dark warm wall). A
            // beveled cap has subMeshCount 3 plus distinct bevel + wall material instances — the
            // big top→bevel→wall value+hue gradient is what makes the raised shape unmistakable.
            int subMeshes = sm != null ? sm.subMeshCount : -1;
            bool split = _capBevelMaterial != null && _capWallMaterial != null && subMeshes >= 3;
            // Item 7: prove the keycap is a CLOSED, correctly-wound SOLID (the see-through
            // "you can see the button's own underside through it" was an inside-out winding on a
            // Cull-Back material). A closed beveled keycap = 10 quads: top(1) + bevel ring(4) +
            // side walls(4) + BACK/bottom cap(1). The back cap lives in the WALL submesh (2) so it
            // shades dark like the walls. Expected watertight counts: 40 verts, 20 tris, submesh
            // index counts [6, 24, 30] (top 6 / bevel 24 / walls+back 30). Report the ACTUAL mesh
            // so the next hardware log confirms nothing is missing and the winding is now outward.
            int vtx = sm != null ? sm.vertexCount : -1;
            int triTotal = 0, s0 = -1, s1 = -1, s2 = -1;
            if (sm != null)
            {
                for (int si = 0; si < sm.subMeshCount; si++)
                    triTotal += (int)(sm.GetIndexCount(si) / 3);
                if (sm.subMeshCount > 0) s0 = (int)sm.GetIndexCount(0);
                if (sm.subMeshCount > 1) s1 = (int)sm.GetIndexCount(1);
                if (sm.subMeshCount > 2) s2 = (int)sm.GetIndexCount(2);
            }
            bool closedSolid = vtx == 40 && triTotal == 20 && s0 == 6 && s1 == 24 && s2 == 30;
            float bevelMm = SquareCapBevel * Mathf.Abs(lossy.z) * 1000f;
            string tintInfo = _capWallMaterial != null
                ? $"top {(m != null ? m.color.ToString() : "<none>")}, bevel {(_capBevelMaterial != null ? _capBevelMaterial.color.ToString() : "<none>")} (lerp {BevelLerp:F2} → parchment), " +
                  $"wall {_capWallMaterial.color} (factor {WallTintFactor:F2}, warm lerp {WallWarmLerp:F2})"
                : "single-material cap (no bevel/wall split)";
            // Item 1b: prove the cap is genuinely OPAQUE (the "glassy/see-through" complaint). Every
            // cap material must be alpha 1 AND draw in the opaque queue (< 2500 = Geometry/AlphaTest,
            // NOT the Transparent 3000 band). If all three pass, the look is a COLOUR issue, never blend.
            float topA = m != null ? m.color.a : -1f;
            float bevA = _capBevelMaterial != null ? _capBevelMaterial.color.a : 1f;
            float wallA = _capWallMaterial != null ? _capWallMaterial.color.a : 1f;
            bool allAlpha1 = topA >= 0.999f && bevA >= 0.999f && wallA >= 0.999f;
            bool opaqueQueue = queue >= 0 && queue < 2500;
            bool opaque = allAlpha1 && opaqueQueue;
            VRLog.Info("Cards", $"ITEMA cap diag — {label}: real cap size {mmW:F1}×{mmH:F1}×{mmThick:F1} mm, " +
                $"bevel ≈ {bevelMm:F1} mm (lossyScale {lossy}), shader '{shaderName}', renderQueue {queue}. " +
                $"OPAQUE: {(opaque ? "YES" : "NO")} (alpha top/bevel/wall {topA:F2}/{bevA:F2}/{wallA:F2} all=1 {allAlpha1}, " +
                $"queue {queue} < 2500 {opaqueQueue} — no alpha-blend/Transparent). " +
                $"Walls read {(mmThick >= 6f ? "SOLID (thickness OK)" : "FLAT (too thin)")}; material is " +
                $"{(shaderName.Contains("BoardLit") ? "BoardLit (shades by normal → lit bevel)" : "NOT BoardLit — bevel/wall shading may be wrong")}. " +
                $"Three-material bevel split: {(split ? "YES" : "NO")} (submeshes {subMeshes}); {tintInfo}. " +
                $"CLOSED SOLID: {(closedSolid ? "YES" : "NO")} (verts {vtx}, tris {triTotal}, submesh indices " +
                $"top/bevel/wall {s0}/{s1}/{s2}; back+bottom cap in wall submesh 2 — expect 40/20/6/24/30). " +
                "Winding is now OUTWARD (RH normal = +n), so no interior/underside shows through under Cull Back (item 7). " +
                "Antique palette (T4): dark-wood plaque / parchment-glow available / aged-brass bevel inlay / dark-wood walls.");
        }

        private TextMeshPro? _label;
        private Transform? _cap;
        private Color _accentColor;
        /// <summary>USER DEBUG OPTION: [ButtonColors] per-category cap-FACE tint (multiplier, default
        /// white = unchanged) — set once at <see cref="Create"/> from the button's category. Applied
        /// to every state colour AND the native sprite face so the user can darken this cap group.</summary>
        private Color _capTint = Color.white;
        private bool _enabledState;
        private bool _accent;
        private bool _confirmed;
        private float _press; // 0..1 press animation

        // Poke dwell state (test #19): the hand whose fingertip is charging the
        // press, hold start time, and the count of haptic ramp ticks already sent.
        private VRHand? _dwellHand;
        private float _dwellStart;
        private int _dwellTicks;

        // Finger-follow press (feature 6a): the hand currently hovering this button
        // in poke range. While set (and not dwelling) the cap continuously tracks the
        // fingertip's penetration depth so the puck physically sinks under the finger,
        // instead of a fire-then-spring flash. Cleared on OnPokeExit / SetVisible(false).
        private VRHand? _hoverHand;

        internal Collider? Collider { get; private set; }

        /// <summary>
        /// T4 antique restyle: DISABLED cap TOP — plain DARK WOOD, barely lighter than the
        /// board itself. A disabled key reads as an unlit carved plaque (the grain texture
        /// still shows), clearly "asleep" next to the parchment glow of an available one.
        /// </summary>
        private static readonly Color DisabledColor = new(0.21f, 0.16f, 0.11f);

        /// <summary>
        /// T4 antique restyle: IDLE/AVAILABLE cap TOP — a warm PARCHMENT glow over the
        /// wood grain. The solid, opaque base every enabled key rests at ("this one you
        /// can press"); state accents tint from here. Desaturated toward antique — no
        /// candy saturation on the board.
        /// </summary>
        private static readonly Color IdleColor = new(0.60f, 0.51f, 0.35f);

        /// <summary>
        /// Confirmed/readied state (test #19): ACTIVE = worn BRASS (T4: desaturated from
        /// the old saturated gold) — clearly distinct from both the muted CONFIRM accent
        /// and the parchment idle, matching the tray's brass "locked in" language. Shown
        /// while the game reports the player readied (revocable — pressing un-readies).
        /// </summary>
        private static readonly Color ConfirmedColor = new(0.68f, 0.52f, 0.24f);

        /// <summary>Charge tint the cap ramps toward while a poke dwell runs (test #19).
        /// T4: soft parchment, in key with the antique palette (was near-white).</summary>
        private static readonly Color DwellChargeColor = new(0.93f, 0.86f, 0.68f);

        /// <summary>
        /// How close the fingertip must stay to the cap for the dwell to keep
        /// charging — mirror of PokeInteractor.ReleaseRange (the interactor's
        /// re-arm hysteresis), meters at scale 1 × hand world scale.
        /// </summary>
        private const float DwellHoldRange = 0.02f;

        /// <summary>Cap rest position / authored full 4 mm press travel on the local Z (viewer side is -Z).</summary>
        private const float CapRestZ = -0.004f;
        private const float CapTravel = 0.004f;

        /// <summary>
        /// Per-instance press travel (user #9: configurable cap sink depth — also the
        /// distance the fingertip must push for the depth-fire press). Authored 4 mm;
        /// the CATEGORY-split ButtonTuning consumers pass their own section's Travel at
        /// <see cref="Create"/> (Confirm/Undo → [BoardButtons], gear/pin → [BoardDashboard])
        /// and rebuild on a config change, so a value never leaks across categories —
        /// uncategorized BoardButtons (rest discs) keep the authored constant.
        /// </summary>
        private float Travel { get; set; } = CapTravel;

        // Depth-fire press (user #6): fingertip contact no longer fires — the finger-follow
        // machinery fires exactly when the cap reaches PressFireFraction (~90%) of its full
        // travel, and re-arms only after the cap rose back past PressRearmFraction (~50%).
        // Releasing before the bottom = no fire, the cap springs back. Laser presses
        // (CardsDriver → Press(hand, "laser")) never pass through this and stay immediate.
        private bool _depthArmed = true;

        // Press DEBOUNCE (user: keycaps double-trigger like the pile stacks used to — port the
        // exact PileStack.OnPoke fix here). After a press commits, no second press fires until
        // the fingertip has both (a) retracted past PressRearmFraction so _depthArmed re-arms
        // AND (b) waited out this shared cooldown — killing the retract/re-entry and the
        // PokeInteractor hover-flicker (exit+enter inside one physical poke) re-fire the
        // hysteresis alone could not. The laser path (Press "laser") is cooldown-debounced too
        // (cross-path poke+laser double-fire), but never dwelled. Composes with the depth-fire:
        // the depth-fire at ~90% travel is still the press event; this only blocks the re-fire.
        private float _nextPressTime;

        // Dust-dissolve hide / materialize-from-dust show (user #7). The logical hide is INSTANT
        // (collider off, poke state dropped); only the visuals shrink out for
        // ButtonTuning.DissolveSeconds while the pooled dust burst plays. Appear reverses it:
        // converging dust + a surface fade-in, in place (no scale/grow pop).
        private bool _logicalVisible = true;
        private bool _ticked;      // false until the first Update — a hide before then is silent (initial state settling)
        private float _hideLeft;   // dissolve shrink countdown, seconds
        private float _showLeft;   // materialize-from-dust fade-in countdown, seconds
        private Color _appearTarget = Color.white; // the cap colour the materialize fade ramps UP to
        private Vector3 _shownScale = Vector3.one;

        /// <summary>
        /// Fingertip contact radius — mirror of <c>PokeInteractor.FingertipRadius</c>
        /// (private there), used by the finger-follow press to turn tip distance into
        /// a penetration depth. At tip-distance = this the finger just touches (depth 0).
        /// </summary>
        private const float FingertipRadius = 0.008f;

        /// <summary>
        /// Build a pressable board button. Rectangular by default (native 9-slice cap
        /// or procedural grey cube). Pass <paramref name="round"/> = true to build a
        /// ROUND disc sized to <paramref name="diameter"/> × <paramref name="thickness"/>
        /// that seats in the board's round rest-notches (feature 6a): the Base is a
        /// recessed dark "well" ring, the Cap a palette-tinted pressable puck, both SMOOTH
        /// generated 64-sided discs (<see cref="CardMesh.GetRoundCap"/> — user: Unity's
        /// primitive cylinder is only ~20-sided so a large round cap showed visible CORNERS;
        /// the disc is authored at real size in the button's local frame, front face toward
        /// the viewer along -Z, so no primitive rotation/scale is needed). Round buttons
        /// always use the procedural palette cap (the native skin's 9-slice is a rounded
        /// RECTANGLE, never a circle), so they read as turned-into-the-board discs. The
        /// existing cap-travel machinery, label and trigger collider are unchanged.
        /// </summary>
        internal static BoardButton Create(Transform anchor, Vector2 size, Color accent,
            string fallbackLabel, System.Action onClick,
            bool round = false, float diameter = 0f, float thickness = 0.01f,
            bool overlay = false, bool boxy = false, float travel = CapTravel,
            WorldUI.ButtonTuning.CapCategory capCategory = WorldUI.ButtonTuning.CapCategory.Rest)
        {
            var go = new GameObject($"BoardButton_{fallbackLabel}");
            go.transform.SetParent(anchor, worldPositionStays: false);

            // Round buttons take their footprint from the diameter (square bounds for the
            // label fit / trigger box); rectangular buttons keep their explicit size.
            if (round && diameter > 0f)
                size = new Vector2(diameter, diameter);

            // Round caps (Base well + Cap puck) are SMOOTH generated discs now (user: the
            // 'round' buttons showed visible CORNERS from Unity's ~20-sided primitive cylinder) —
            // CardMesh.GetRoundCap authors the disc directly in the button's local frame (front
            // face toward the viewer, -Z), so no primitive rotation/scale is needed.
            GameObject basePlate;
            if (round)
            {
                // Smooth generated disc (user: the 'round' buttons showed visible CORNERS — Unity's
                // primitive cylinder is only ~20-sided). Identity rotation / unit scale: the mesh is
                // authored at REAL size (diameter × 6 mm thickness, centred on the origin), matching
                // the old flattened-cylinder footprint. Slightly wider than the cap → a visible
                // recessed well ring around the puck. Cached/shared across identical caps.
                basePlate = new GameObject("Base");
                basePlate.transform.SetParent(go.transform, worldPositionStays: false);
                basePlate.AddComponent<MeshFilter>().sharedMesh =
                    CardMesh.GetRoundCap(size.x + 0.006f, 0.006f);
                basePlate.AddComponent<MeshRenderer>();
                basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            }
            else
            {
                basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                basePlate.name = "Base";
                Object.Destroy(basePlate.GetComponent<Collider>());
                basePlate.transform.SetParent(go.transform, worldPositionStays: false);
                basePlate.transform.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 0.006f);
                basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            }
            // Item 5/6: board-HUD buttons (gear / follow-toggle) route their body to the
            // Overlay shader so RenderOnTop can force it over the opaque board; CONFIRM /
            // UNDO / rest buttons keep Standard (they stay depth-correct, no RenderOnTop).
            // Item 1b: a solid warm dark-WOOD surround (not a near-black void) so the recessed
            // well around the cap reads as part of the physical panel, not a hole under a glassy key.
            Tint(basePlate, new Color(0.15f, 0.12f, 0.08f), overlay: overlay);

            // Native look (test #25 item 3): when a live game button has been sampled,
            // the travelling cap is an EMPTY holder carrying the game's own 9-sliced
            // button sprite (WorldUI.NativeButtonSkin) on a unit-scale face — so it is
            // indistinguishable from a native widget. Otherwise it falls back to the
            // procedural grey cube cap. Either way the holder sits at CapRestZ and
            // travels on press; the collider (on go) is independent of it.
            Material? capMaterial = null;
            Material? capBevelMaterial = null; // item 4: the bright bevel-ring material instance (boxy caps only)
            Material? capWallMaterial = null;  // item 4: the dark warm side-wall material instance (boxy caps only)
            SpriteRenderer? capFace = null;
            Renderer? capMeshRenderer = null; // item A diagnostic: the cap body renderer (cube/disc/sprite)
            var cap = new GameObject("Cap");
            cap.transform.SetParent(go.transform, worldPositionStays: false);
            cap.transform.localPosition = new Vector3(0f, 0f, CapRestZ);
            float labelZ = -0.007f; // default: proud of the flat cap face (viewer side, -Z)
            // Item 3 (laser): the frontmost (viewer-side, -Z) local-Z of the cap BODY, so the
            // trigger collider below can be sized to span the WHOLE protruding cap — the laser
            // reticle then lands on the visible cap FACE. Default covers the flat native/procedural
            // face (≈ -8 mm); the round disc and boxy keycap branches override it with their real
            // protrusion (a beveled keycap reaches CapRestZ - capThick ≈ -34 mm).
            float capFrontZ = CapRestZ - 0.004f;

            if (round)
            {
                // Round pressable puck: a smooth GENERATED disc (user: the 'round' buttons showed
                // visible CORNERS — Unity's primitive cylinder is only ~20-sided, so a large round
                // cap read as a faceted polygon). CardMesh.GetRoundCap builds a 64-sided disc at
                // REAL size (diameter × thickness), identity-rotated / unit-scaled, so its front
                // face still protrudes thickness/2 toward the viewer (-Z) exactly like the old
                // flattened cylinder. Planar XY UVs let the shared carved-grain keycap _MainTex map
                // across the round face (see below); one submesh = one keycap material. Sits proud
                // of the well toward the viewer and travels with the cap holder.
                float capThickR = Mathf.Max(0.002f, thickness);
                var capDisc = new GameObject("CapMesh");
                capDisc.transform.SetParent(cap.transform, worldPositionStays: false);
                capDisc.AddComponent<MeshFilter>().sharedMesh = CardMesh.GetRoundCap(size.x, capThickR);
                capDisc.AddComponent<MeshRenderer>();
                capFrontZ = CapRestZ - capThickR * 0.5f; // disc protrudes half its thickness toward the viewer
                // User (rest-cap alignment): the round rest puck now wears the SAME antique keycap
                // SURFACE as the square Confirm/Undo/gear/Fixiert caps. Previously this branch used a
                // bare Standard material with a flat state colour — a plain plastic puck — while the
                // boxy caps route through NewKeycapMaterial(BoxCapShader()), which shades via BoardLit
                // (lit even in unlit scenes) AND carries the shared carved wood/parchment grain on
                // _MainTex (grayscale grain × the per-state colour). Use that exact material path here
                // so the disc reads as one of the same keycap family. SHAPE STAYS ROUND (the user asked
                // for the same TEXTURE, not the same shape); the cylinder has one cap material (no
                // bevel/wall submeshes), and SetCapColor drives its top colour exactly as before, so the
                // per-rest accent tints (parchment-gold / slate-blue) and the [ButtonColors] RestCapTint
                // are untouched. The engraved parchment label (StyleEngravedLabel below) already matches.
                Shader? shader = BoxCapShader();
                if (shader != null)
                {
                    capMaterial = NewKeycapMaterial(shader, DisabledColor);
                    capDisc.GetComponent<MeshRenderer>().sharedMaterial = capMaterial;
                    Core.VRLog.Info("Cards", $"BoardButton '{fallbackLabel}': ROUND cap skinned with the shared " +
                                             "carved-grain keycap material (BoardLit + KeycapGrain _MainTex) — same antique " +
                                             "wood/parchment surface as the square board keycaps; shape stays round (64-seg " +
                                             "generated disc), accent tint kept.");
                }
                else
                {
                    // Shaderless environment: the generated disc has no default material (unlike the
                    // old CreatePrimitive cylinder), so seat a plain Standard-tinted material to match
                    // the pre-fix fallback look — never render an unmaterialed (magenta) disc.
                    Shader? fb = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                    if (fb != null)
                    {
                        capMaterial = new Material(fb) { color = DisabledColor };
                        capDisc.GetComponent<MeshRenderer>().sharedMaterial = capMaterial;
                    }
                }
                capMeshRenderer = capDisc.GetComponent<MeshRenderer>();
            }
            else if (boxy)
            {
                // PART C / item 4/5: a REAL 3D square keycap with a CHAMFERED front edge — its
                // side must read as a solid protruding button, not "a floating square with text".
                // Previous attempts split a plain cube into top + darker walls, but BOTH stayed
                // dark (top ~0.24, wall ~0.11) → viewed near top-down against a dark board the
                // walls had near-zero contrast and vanished. "Darker" was the wrong lever. The cap
                // now has THREE submeshes with a big value+hue gradient, and a lit 45° BEVEL RING
                // that catches light and frames the top so it reads RAISED from any angle:
                //   [0] top plateau  = the state colour (semantic)
                //   [1] bevel ring   = BRIGHT parchment/brass (BevelTint) — the catch-light edge
                //   [2] side walls   = dark WARM band (WallTint), distinct from the neutral board
                // Geometry is authored at REAL size (CardMesh.BuildBeveledKeycap) so the holder
                // stays UNIT-scaled — uniform scale keeps the bevel a true 45° in world space so
                // BoardLit (shades by world normal, baked-lit, works in the unlit scenes) lights it
                // brighter than top or wall. NO RenderOnTop; the cap protrudes toward the viewer
                // (front plateau at −capThick) and travels inward on press. The three material
                // instances all track button state (UpdateColor → SetCapColor).
                float capThick = Mathf.Max(0.012f, thickness);
                var capCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                capCube.name = "CapMesh";
                Object.Destroy(capCube.GetComponent<Collider>());
                capCube.transform.SetParent(cap.transform, worldPositionStays: false);
                capCube.transform.localScale = Vector3.one;       // mesh is authored at real size
                capCube.transform.localPosition = Vector3.zero;   // mesh already spans −capThick..0
                capFrontZ = CapRestZ - capThick; // the beveled plateau protrudes the full cap thickness toward the viewer
                Shader? shader = BoxCapShader();
                if (shader != null)
                {
                    var mf = capCube.GetComponent<MeshFilter>();
                    if (mf != null)
                        mf.sharedMesh = CardMesh.BuildBeveledKeycap(size.x, size.y, capThick, SquareCapBevel);
                    // Task #5a: each submesh material carries the shared carved-grain texture on
                    // _MainTex (grayscale grain × the state/bevel/wall tint) when the bundle ships
                    // it — a real wood/parchment surface — else EXACTLY the prior plain tint.
                    capMaterial = NewKeycapMaterial(shader, DisabledColor);                 // [0] top
                    capBevelMaterial = NewKeycapMaterial(shader, BevelTint(DisabledColor)); // [1] bright bevel
                    capWallMaterial = NewKeycapMaterial(shader, WallTint(DisabledColor));   // [2] dark warm wall
                    capCube.GetComponent<MeshRenderer>().sharedMaterials =
                        new[] { capMaterial, capBevelMaterial, capWallMaterial };
                }
                capMeshRenderer = capCube.GetComponent<MeshRenderer>();
                labelZ = -(capThick + 0.002f); // proud of the protruding plateau (front face sits at -capThick)
            }
            else
            {
                // Face proud of the base plate (viewer side, -Z), just behind the label.
                // Item 5/6: the native 9-slice face renders through the Overlay material
                // (a SpriteRenderer samples the sprite via _MainTex) so a board-HUD button
                // cap (gear/toggle, RenderOnTop'd) can be forced ZTest-Always over the
                // board. CONFIRM/UNDO get the same material but look identical (LEqual)
                // since they are not RenderOnTop'd. Null (Overlay absent) → default sprite
                // material, the pre-fix look.
                capFace = WorldUI.NativeButtonSkin.CreateFace(cap.transform, size, localZ: -0.004f,
                    sortingOrder: 1, overrideMaterial: OverlayMaterial(Color.white));
                capMeshRenderer = capFace;
                if (capFace == null)
                {
                    // Procedural fallback: the original squashed grey cube cap.
                    var capCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    capCube.name = "CapMesh";
                    Object.Destroy(capCube.GetComponent<Collider>());
                    capCube.transform.SetParent(cap.transform, worldPositionStays: false);
                    capCube.transform.localScale = new Vector3(size.x, size.y, 0.008f);
                    // Item 5/6: board-HUD button (gear/toggle) procedural cap routes to
                    // Overlay so RenderOnTop's _ZTest takes; others keep Standard.
                    Shader? shader = overlay ? OverlayShader() : null;
                    shader ??= Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        capMaterial = new Material(shader) { color = DisabledColor };
                        capCube.GetComponent<MeshRenderer>().sharedMaterial = capMaterial;
                    }
                    capMeshRenderer = capCube.GetComponent<MeshRenderer>();
                }
            }

            // Item 4: the label is parented to the CAP HOLDER (which is unit scale — only
            // its child mesh/face is non-uniformly scaled, so no distortion) so it TRAVELS
            // WITH the cap when the button is pressed (it used to hang off the static root
            // while only the cap sank, reading as detached). Held slightly proud of the cap
            // face on the viewer side (-Z), above it by sortingOrder so it never clips.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(cap.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, labelZ); // proud of the cap face (viewer side, -Z)
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = fallbackLabel;
            tmp.alignment = TextAlignmentOptions.Center;
            // Native label (test #25 item 3): the game's HUD font (MarcellusSC) + its
            // parchment-gold button-text colour when the skin has been sampled; plain
            // white otherwise (procedural fallback).
            tmp.color = WorldUI.NativeButtonSkin.HasFont ? WorldUI.NativeButtonSkin.LabelColor : Color.white;
            WorldUI.NativeButtonSkin.ApplyFont(tmp);
            WorldUI.NativeButtonSkin.StyleEngravedLabel(tmp); // T4: parchment glyphs carved into the cap
            // Draw the label ABOVE the native sprite face (test #26): both are transparent
            // renderers with ZWrite off, so sorting order — not the label's nearer z —
            // decides who wins. The face uses sortingOrder 1; without this the sprite drew
            // over the text and the button showed no readable label.
            var labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
                labelRenderer.sortingOrder = 3;
            // Fit inside the cap face: localized CONFIRM/UNDO strings (SetLabel
            // mirrors the game's texts) shrink/wrap inside the button instead of
            // spilling over its edges (TmpFit, test #12).
            Core.TmpFit.Fit(tmp, size.x * 0.92f, size.y * 0.85f, maxFontSize: 0.40f);

            // Item 3 (laser fix): the trigger collider must SPAN the full protruding cap so a
            // laser ray aimed at the visible cap FACE registers a hit. The old fixed box
            // (size.z 0.02, centre -0.004 → front face -0.014) fell 20 mm SHORT of a boxy
            // beveled keycap's front plateau (CapRestZ - capThick ≈ -0.034): the laser passed
            // over the box and never landed, so gear/pin (and any Square-shaped cap) could not
            // be laser-pressed even though poke worked (the fingertip reaches the box from its
            // 35 mm hover). Size the box from the cap's own frontmost local-Z back to the base
            // plate's rear face, so it hugs the whole visible cap for every shape.
            const float baseBackZ = 0.007f;                     // base plate rear face (localPos.z 0.004 + half depth 0.003)
            float boxFrontZ = Mathf.Min(capFrontZ, -0.006f);    // never shallower than the old front
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, baseBackZ - boxFrontZ);
            box.center = new Vector3(0f, 0f, (boxFrontZ + baseBackZ) * 0.5f);
            box.isTrigger = true;

            var button = go.AddComponent<BoardButton>();
            button._onClick = onClick;
            button._capMaterial = capMaterial;
            button._capBevelMaterial = capBevelMaterial; // item 4: bright bevel-ring instance (null on non-boxy caps)
            button._capWallMaterial = capWallMaterial;   // item 4: dark warm side-wall instance (null on non-boxy caps)
            button._capFace = capFace;
            button._capMeshRenderer = capMeshRenderer;
            button._label = tmp;
            button._cap = cap.transform;
            button._accentColor = accent;
            // USER DEBUG OPTION: this button's [ButtonColors] cap-face tint (default white = no
            // change). Applied to every state colour (StateColor) and the native sprite face
            // (UpdateColor). The category defaults to Rest so RestControls — which does not pass
            // one — picks up the rest tint; Confirm/Undo pass Board, gear/follow pass Dashboard.
            WorldUI.ButtonTuning.Bind();
            button._capTint = WorldUI.ButtonTuning.CapTint(capCategory);
            button.Collider = box;
            button.Travel = travel; // per-category press travel (category split; rest discs keep the authored default)
            button.UpdateColor(); // seat the initial (disabled) native/procedural face tint
            return button;
        }

        /// <summary>
        /// Built ONLY when a press is rejected — explains the disabled state
        /// (e.g. the CanConfirm gate inputs). Wired by <see cref="BuildButtons"/>.
        /// </summary>
        internal System.Func<string>? DisabledReason;

        /// <summary>
        /// Deliberate-poke dwell (test #19): &gt; 0 makes a fingertip CONTACT only
        /// start a hold — the tip must stay on the cap this many seconds before the
        /// press fires (cap sinks + brightens while charging; retracting cancels).
        /// 0 (default) keeps the fire-on-contact behavior. Laser presses are never
        /// dwelled — <see cref="Press"/> with source "laser" stays immediate.
        /// No button sets this anymore — every physical press fires on contact
        /// (user directive round 7); the accident protection that remains is
        /// <see cref="ActivationGuard"/>. Machinery kept for a possible config.
        /// </summary>
        internal float DwellSeconds = 0f;

        /// <summary>
        /// Optional activation suppressor: returns the seconds REMAINING of a
        /// suppression window (≤ 0 = free). Checked for EVERY source at fire time —
        /// the test #19 accident window after slot drops/plucks. Wired for CONFIRM
        /// by <see cref="BuildButtons"/>.
        /// </summary>
        internal System.Func<float>? ActivationGuard;

        internal void SetState(bool enabled, bool accent, bool confirmed = false)
        {
            if (_enabledState == enabled && _accent == accent && _confirmed == confirmed)
                return;
            _enabledState = enabled;
            _accent = accent;
            _confirmed = confirmed;
            UpdateColor();
        }

        internal void SetLabel(string text)
        {
            if (_label != null && _label.text != text)
                _label.text = text;
        }

        /// <summary>
        /// Show/hide the whole button (test #23 item 4): the mod CONFIRM/UNDO buttons
        /// hide while the REAL ReadyButton/UndoButton dock at the same spot. Inactive
        /// = its collider is off too, so it produces no poke/laser events.
        /// </summary>
        internal void SetVisible(bool visible)
        {
            if (_logicalVisible == visible)
                return;
            _logicalVisible = visible;
            if (visible)
            {
                // Cancel a running dissolve, restore the true scale, re-enable input.
                _hideLeft = 0f;
                transform.localScale = _shownScale;
                if (Collider != null)
                    Collider.enabled = true;
                if (!gameObject.activeSelf)
                    gameObject.SetActive(true);
                // APPEAR = MATERIALIZE FROM DUST (user: emerge from dust, matched to the crumble —
                // NOT a scale/grow pop): converging dust motes settle onto the cap while its surface
                // fades up from the dust to its full state colour, IN PLACE (no scaling). Only once
                // the button has ticked (suppresses the build-then-settle storm) and while the
                // animation is enabled ([ButtonAnim] Enable). Input/collider are already live above.
                _showLeft = _ticked && WorldUI.ButtonTuning.ButtonAnimEnabled ? WorldUI.ButtonTuning.AppearSeconds : 0f;
                if (_showLeft > 0f)
                {
                    _appearTarget = CurrentCapColor(); // the colour the surface fades UP to
                    WorldUI.ButtonTuning.LogAnim(name, "appear (materialize-from-dust)");
                    if (WorldUI.ButtonTuning.AppearParticlesEnabled)
                    {
                        Vector3 c = _cap != null ? _cap.position : transform.position;
                        float fp = Collider is BoxCollider b ? Mathf.Max(b.size.x, b.size.y) : 0.05f;
                        WorldUI.ButtonDissolveFx.PlayMaterialize(c, -transform.forward,
                            fp * Mathf.Abs(transform.lossyScale.x), _appearTarget);
                    }
                }
                return;
            }
            // LOGICAL hide is immediate (user #7 contract): input off now, visuals may linger.
            _hoverHand = null; // no poke events fire while hidden — drop stale follow
            _depthArmed = true;
            if (_dwellHand != null)
                CancelDwell();
            if (Collider != null)
                Collider.enabled = false;
            if (!_ticked || !gameObject.activeInHierarchy || !WorldUI.ButtonTuning.ButtonAnimEnabled)
            {
                // Initial state settling (built then hidden the same frame), already invisible
                // with the tray, or the animation is disabled — pop away silently, no dust.
                gameObject.SetActive(false);
                return;
            }
            // Dust dissolve (user #7): cap shrinks out over DissolveSeconds while the pooled
            // burst sweeps face-colored powder sideways; Update deactivates at the end.
            _shownScale = transform.localScale;
            _showLeft = 0f;
            _hideLeft = WorldUI.ButtonTuning.DissolveSeconds;
            WorldUI.ButtonTuning.LogAnim(name, "disappear (dust dissolve)");
            Vector3 center = _cap != null ? _cap.position : transform.position;
            float footprint = Collider is BoxCollider bc ? Mathf.Max(bc.size.x, bc.size.y) : 0.05f;
            WorldUI.ButtonDissolveFx.Play(center, -transform.forward,
                footprint * Mathf.Abs(transform.lossyScale.x), CurrentCapColor());
        }

        /// <summary>The cap's face color right now (native face, tinted material, or the palette fallback).</summary>
        private Color CurrentCapColor() =>
            _capFace != null ? _capFace.color
            : _capMaterial != null ? _capMaterial.color
            : StateColor();

        /// <summary>Resting cap color for the current state (procedural fallback; dwell ramps AWAY
        /// from this). USER DEBUG OPTION: the shared state palette is multiplied by this button's
        /// per-category <see cref="_capTint"/> (default white = unchanged).</summary>
        private Color StateColor() =>
            (!_enabledState ? DisabledColor : _confirmed ? ConfirmedColor : _accent ? _accentColor : IdleColor)
            * _capTint;

        /// <summary>Native-skin face state for the current button state (test #25 item 3).</summary>
        private WorldUI.NativeButtonSkin.FaceState FaceState() =>
            !_enabledState ? WorldUI.NativeButtonSkin.FaceState.Disabled
            : (_confirmed || _accent) ? WorldUI.NativeButtonSkin.FaceState.Accent
            : WorldUI.NativeButtonSkin.FaceState.Idle;

        private void UpdateColor()
        {
            if (_capFace != null)
            {
                WorldUI.NativeButtonSkin.Apply(_capFace, FaceState());
                // USER DEBUG OPTION: tint the native sprite face too (the beige/brass button art
                // the user reads white text on) — default white leaves the sampled sprite as-is.
                _capFace.color *= _capTint;
                return;
            }
            if (_capMaterial == null)
                return;
            SetCapColor(StateColor());
        }

        /// <summary>
        /// Item 4: drive the cap TOP colour and — when the boxy cap carries the split bevel
        /// (<see cref="_capBevelMaterial"/>) and wall (<see cref="_capWallMaterial"/>)
        /// submeshes — the BRIGHT bevel ring and the dark warm WALL band together, so all three
        /// always track the button state (disabled / accent / confirmed / dwell charge): the
        /// bevel a fixed <see cref="BevelLerp"/> brighter, the wall a fixed value+hue darker.
        /// No-op bevel/wall steps on round/native caps (null instances).
        /// </summary>
        private void SetCapColor(Color top)
        {
            if (_capMaterial != null && _capMaterial.color != top)
                _capMaterial.color = top;
            if (_capBevelMaterial != null)
            {
                Color bevel = BevelTint(top);
                if (_capBevelMaterial.color != bevel)
                    _capBevelMaterial.color = bevel;
            }
            if (_capWallMaterial != null)
            {
                Color wall = WallTint(top);
                if (_capWallMaterial.color != wall)
                    _capWallMaterial.color = wall;
            }
        }

        private void Update()
        {
            // Dust-dissolve shrink-out (user #7): the button is logically gone already
            // (collider off) — finish the visual shrink, then deactivate for real.
            if (_hideLeft > 0f)
            {
                _hideLeft -= Time.deltaTime;
                float k = Mathf.Max(0f, _hideLeft / WorldUI.ButtonTuning.DissolveSeconds);
                transform.localScale = _shownScale * k;
                if (_hideLeft <= 0f)
                {
                    transform.localScale = _shownScale; // restore for the next show
                    gameObject.SetActive(false);
                }
                return;
            }
            _ticked = true;

            // MATERIALIZE-FROM-DUST fade-in (user: emerge from dust, NOT a scale pop) — runs
            // alongside the normal press logic. The cap stays at full scale IN PLACE while its
            // OPAQUE surface brightens from the dust (0.15×) up to its true state colour (a real
            // fade with no transparency needed — safe on the BoardLit/Standard caps), under the
            // converging dust cloud. On completion UpdateColor() restores the exact state colours.
            if (_showLeft > 0f)
            {
                _showLeft -= Time.deltaTime;
                float k = 1f - Mathf.Max(0f, _showLeft / WorldUI.ButtonTuning.AppearSeconds);
                transform.localScale = _shownScale; // materialize in place — no grow/scale pop
                float b = Mathf.SmoothStep(0.15f, 1f, k);
                Color faded = _appearTarget * b; faded.a = _appearTarget.a;
                if (_capFace != null)
                    _capFace.color = faded;
                else
                    SetCapColor(faded);
                if (_showLeft <= 0f)
                {
                    transform.localScale = _shownScale;
                    UpdateColor(); // snap back to the exact state colour (top/bevel/wall or native face)
                }
            }

            if (_dwellHand != null)
            {
                TickDwell();
                return;
            }
            if (_cap == null)
                return;

            // Spring the click impulse back down (framerate-independent decay).
            if (_press > 0f)
                _press = Mathf.MoveTowards(_press, 0f, Time.deltaTime * 6f);

            // Finger-follow (feature 6a): while a fingertip hovers this button the cap
            // tracks how deep the tip has pushed past the face, so the puck sinks under
            // the finger 1:1 (up to the full travel) and rises as it retracts. When no
            // finger is present it falls back to the _press spring. The two combine as a
            // max so a quick laser/click still shows its dip even mid-hover.
            float follow = _hoverHand != null && _enabledState ? FollowDepth01(_hoverHand) : 0f;

            // DEPTH-FIRE (user #6): the press fires exactly when the cap reaches ~90% of
            // its full travel under the finger — not on contact. Re-arms only after the
            // finger retracted enough for the cap to rise back past ~50%, so resting at
            // the bottom cannot machine-gun and a shallow brush never fires at all.
            // Press() still runs the full gate chain (enabled/ActivationGuard/logs).
            if (_hoverHand != null && _enabledState)
            {
                if (follow >= WorldUI.ButtonTuning.PressFireFraction)
                {
                    // DEBOUNCE (user): fire only when re-armed (cap fully retracted past the
                    // hysteresis since the last press) AND the shared cooldown has elapsed —
                    // a retract-then-push or a hover flicker inside the same poke can no longer
                    // machine-gun a second press. Press() re-checks the cooldown and stamps it.
                    if (_depthArmed && Time.unscaledTime >= _nextPressTime)
                    {
                        _depthArmed = false;
                        Press(_hoverHand, "poke-depth");
                    }
                }
                else if (follow <= WorldUI.ButtonTuning.PressRearmFraction)
                {
                    _depthArmed = true;
                }
            }
            else
            {
                _depthArmed = true;
            }

            float depth01 = Mathf.Max(follow, _press);

            // Nothing to drive and already seated → leave it (avoids per-frame churn).
            if (depth01 <= 0f && Mathf.Approximately(_cap.localPosition.z, CapRestZ))
                return;

            Vector3 pos = _cap.localPosition;
            pos.z = CapRestZ + Travel * depth01;
            _cap.localPosition = pos;
        }

        /// <summary>
        /// Fingertip penetration for the finger-follow press, normalised to 0..1 of the
        /// cap travel. Same tip/collider probe as <see cref="TickDwell"/> and the
        /// PokeInteractor contact test: penetration = FingertipRadius·scale − distance
        /// (tip → nearest surface point). Converted through the button's WORLD depth
        /// scale so the puck follows the finger in real space (not local units), and
        /// clamped to one full travel. Framerate-independent — it is a pure function of
        /// where the fingertip is this frame, no accumulation.
        /// </summary>
        private float FollowDepth01(VRHand hand)
        {
            if (Collider == null || !hand.HasPose)
                return 0f;
            Vector3 tip = hand.Rig.IndexTip.position;
            float dist = Vector3.Distance(tip, Collider.ClosestPoint(tip));
            float penetration = FingertipRadius * hand.WorldScale - dist;
            if (penetration <= 0f)
                return 0f;
            float capTravelWorld = Travel * Mathf.Abs(transform.lossyScale.z);
            return capTravelWorld > 1e-6f ? Mathf.Clamp01(penetration / capTravelWorld) : 0f;
        }

        /// <summary>
        /// Poke path (P2 PokeInteractor — geometric fingertip test against this
        /// collider). DEPTH-FIRE (user #6): fingertip CONTACT no longer presses —
        /// the finger-follow machinery in <see cref="Update"/> fires when the cap
        /// reaches ~90% of its travel, so a brush against the button does nothing
        /// and the press feels like actually pushing the key in. The disabled case
        /// still routes to <see cref="Press"/> so every rejected attempt keeps its
        /// gate log (test #14); dwell buttons (none currently) keep their charge.
        /// Laser presses arrive via <see cref="Press"/> directly and stay immediate.
        /// </summary>
        public override void OnPoke(VRHand hand)
        {
            if (DwellSeconds > 0f && _enabledState)
            {
                BeginDwell(hand);
                return;
            }
            if (!_enabledState)
            {
                Press(hand, "poke"); // keeps the REJECTED + reason log
                return;
            }
            _hoverHand = hand; // safety: ensure the depth-fire tracker is armed on this hand
        }

        public override void OnPokeExit(VRHand hand)
        {
            if (ReferenceEquals(hand, _dwellHand))
                CancelDwell();
            if (ReferenceEquals(hand, _hoverHand))
                _hoverHand = null; // stop finger-follow; the cap springs back via Update
        }

        private void BeginDwell(VRHand hand)
        {
            if (_dwellHand != null)
                return; // already charging (second hand / re-arm jitter)
            _dwellHand = hand;
            _dwellStart = Time.unscaledTime;
            _dwellTicks = 0;
            VRLog.Debug("Cards", $"Board: {name} poke dwell started ({hand.Side}) — " +
                                 $"hold {DwellSeconds:F2} s to press.");
        }

        /// <summary>
        /// Per-frame dwell charge: self-tracks the fingertip against the button's
        /// own collider (same math as PokeInteractor, which has no per-frame stay
        /// callback — its OnPokeExit only fires when the tip leaves the 3.5 cm
        /// hover range, too late for a press-intent test). The cap sinks its full
        /// travel and brightens toward <see cref="DwellChargeColor"/> as the fill
        /// ramp; a rising haptic tick marks each quarter of the charge.
        /// </summary>
        private void TickDwell()
        {
            VRHand hand = _dwellHand!;
            bool holding = _enabledState && Collider != null && hand.HasPose;
            if (holding)
            {
                Vector3 tip = hand.Rig.IndexTip.position;
                holding = Vector3.Distance(tip, Collider!.ClosestPoint(tip))
                          <= DwellHoldRange * hand.WorldScale;
            }
            if (!holding)
            {
                CancelDwell();
                return;
            }

            float progress = Mathf.Clamp01((Time.unscaledTime - _dwellStart) / DwellSeconds);
            if (_cap != null)
            {
                Vector3 pos = _cap.localPosition;
                pos.z = CapRestZ + Travel * progress;
                _cap.localPosition = pos;
            }
            if (_capFace != null)
                // USER DEBUG OPTION: tint the native face (default white = unchanged); StateColor is
                // already tinted, so the procedural branch below needs no extra multiply.
                _capFace.color = Color.Lerp(WorldUI.NativeButtonSkin.ColorFor(FaceState()) * _capTint, DwellChargeColor, progress);
            else if (_capMaterial != null)
                SetCapColor(Color.Lerp(StateColor(), DwellChargeColor, progress));

            int tick = (int)(progress * 4f);
            if (tick > _dwellTicks)
            {
                _dwellTicks = tick;
                hand.SendHaptic(HapticPreset.HoverTick);
            }

            if (progress >= 1f)
            {
                _dwellHand = null;
                UpdateColor(); // drop the charge tint (Press animates the spring-back)
                Press(hand, "poke-dwell");
            }
        }

        private void CancelDwell()
        {
            if (_dwellHand == null)
                return;
            float held = Time.unscaledTime - _dwellStart;
            _dwellHand = null;
            _press = 0f;
            if (_cap != null)
            {
                Vector3 pos = _cap.localPosition;
                pos.z = CapRestZ;
                _cap.localPosition = pos;
            }
            UpdateColor(); // drop the charge tint
            VRLog.Info("Cards", $"Board: {name} poke dwell cancelled after {held:F2} s " +
                                $"(needs {DwellSeconds:F2} s — brushing the button no longer presses it).");
        }

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
            float guard = ActivationGuard != null ? ActivationGuard() : 0f;
            if (guard > 0f)
            {
                VRLog.Info("Cards", $"Board: {name} press SUPPRESSED (source={source}, {hand.Side}) — " +
                                    $"{guard:F2} s left of the slot-activity window " +
                                    "(accidental press right after handling cards, test #19).");
                return;
            }
            // DEBOUNCE (user): one press per physical poke/click. The depth-fire pre-checks this
            // window (so a valid poke-depth always passes here and re-stamps it); the laser path
            // arrives straight here and is cooldown-debounced too, so a retract/re-entry, a
            // hover flicker, or a poke+laser inside the same window cannot fire twice.
            if (Time.unscaledTime < _nextPressTime)
            {
                VRLog.Debug("Cards", $"Board: {name} press DEBOUNCED (source={source}, {hand.Side}) — " +
                                     $"within the {WorldUI.ButtonTuning.PokePressCooldownSeconds:F2}s press cooldown.");
                return;
            }
            _nextPressTime = Time.unscaledTime + WorldUI.ButtonTuning.PokePressCooldownSeconds;
            _press = 1f;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: {name} pressed (source={source}, {hand.Side}).");
            _onClick?.Invoke();
        }

        public override void OnPokeEnter(VRHand hand)
        {
            _hoverHand = hand; // arm the finger-follow press (feature 6a)
            if (_enabledState)
                hand.SendHaptic(HapticPreset.HoverTick);
        }
    }
}
