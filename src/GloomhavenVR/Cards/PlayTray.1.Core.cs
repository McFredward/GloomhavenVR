using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray is ONE class split across SEVEN files. This is part 1 — read it first: it holds the
// class doc, every field and const, the mount accessors and the build/placement lifecycle.
//
//   PlayTray.1.Core.cs      class doc, fields/consts, mount accessors, laser targets, lifecycle
//                           (EnsureBuilt + Build*), grab handle, dashboard controls, follow mode,
//                           Destroy, SetVisible / TickPlacement / PlaceAtHead / ReassertPlacement,
//                           solid occluder + furniture draw order
//   PlayTray.2.Watchdog.cs  the LOST-BOARD WATCHDOG, whole and alone
//   PlayTray.3.Pose.cs      debug-menu live apply, fixed base positions, ReapplyOrientation,
//                           rebuild helpers, board-switch pose, PersistPoseToConfig
//   PlayTray.4.Slots.cs     slots, pick field (REMOVED), highlight, recess seat liner (RETIRED),
//                           item-use slot, the multiplayer board-UI read seam
//   PlayTray.5.Status.cs    TickStatus, pick banner + keycap overrides
//   PlayTray.6.Build.cs     build helpers, shaders, materials, MeasureBoardLocalExtents,
//                           board diagnostics
//   PlayTray.7.Nested.cs    the four nested types (LaserTarget, SlotPulse, BoardSurfaceTarget,
//                           BoardButton) — see that file's own header for why they stay nested
//
// THE FILENAMES ARE NOT DECORATION — the digits keep the seven parts concatenating back into the
// ORIGINAL member order. Renaming a part so it sorts differently silently reorders field
// initializers. Canonical statement of the rule and why: CardsDriver.1.Core.cs's header.
//
// Part 2 is the one to notice. Those ~288 lines are the subsystem's most expensive lesson, and
// several of them are COMMENT BLOCKS WITH NO CODE UNDER THEM — that is not leftover cruft, it is
// the invariant (INVARIANTS-Cards §6). Its own file makes it impossible to mistake for one.

/// <summary>
/// The control board (P7 redesign, test #10; test #15: central DASHBOARD): a
/// desk-like tray in front of the player, tilted toward them like a card-table edge
/// (~30° from horizontal, [Cards] BoardTilt_{board}), chest height, anchored in rig space by
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
///   centre field (test #28); the old centre pick field has since been removed as dead
///   code (see the "pick field (REMOVED)" note). A steady pulsing "wanted slot" hint marks the recess the
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
/// LongRestToken</c> plus the button seats <c>ButtonSeat1/2/3</c> — legacy spelling
/// <c>ConfirmButton/UndoButton</c>, both resolve, see <see cref="BoardAnchors"/> and
/// unity/.../Table/README.md) with a full procedural fallback.
/// </summary>
internal sealed partial class PlayTray : WorldUI.IPanelGrabOwner, WorldUI.IFurnitureOrderAnchor
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
    /// <remarks>INTERNAL, not private: a peer's remote board reproduces the slot overlays in
    /// BOARD-local space and therefore has to multiply the authored SLOT-local overlay offsets by
    /// this factor (<c>Net.RemoteBoardFurniture.SlotOverlayLocal</c>). Copying the literal over
    /// there is what the remote board's whole layout class exists to stop.</remarks>
    internal const float SlotScale = 1.3f;

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

    // Asset-only pose (SetAssetPose): the tray mesh + its capture at build.
    private Transform? _visual;
    private Vector3 _visualBasePos;
    private Quaternion _visualBaseRot;
    private readonly System.Collections.Generic.List<(Transform t, Vector3 lp, Quaternion lr)>
        _assetPinnedAnchors = new(6);
    private Transform? _longRestAnchor;
    private readonly VRCard?[] _occupants = new VRCard?[2];

    private TextMeshPro? _roundLabel;
    private BoardButton? _confirm;
    private BoardButton? _undo;

    // Event-discard pick flow (pre-scenario "Begegnungen" mali + every modal card pick):
    // the hovering progress banner ("<Charakter>: Wähle 2 von 3 Karten zum Abwerfen —
    // Schritt 1/2") and the CONFIRM/UNDO keycap label overrides the driver pushes via
    // SetPickStatus. Null override = the keycap follows its normal game-state logic.
    private GameObject? _pickBannerRoot;
    private TextMeshPro? _pickBannerLabel;
    private string? _pickBannerText;
    private string? _pickConfirmLabel;
    private string? _pickUndoLabel;
    private Transform? _confirmAnchor;
    private Transform? _undoAnchor;

    /// <summary>
    /// THE BUNDLED BOARD'S BUTTON RECESSES, by seat index — the anchors <c>BuildButtons</c> parents
    /// the generic cluster's keycaps to. Resolved once per board build from
    /// <see cref="BoardAnchors.ResolveSeats"/>, which accepts the new <c>ButtonSeat1/2/3</c> spelling
    /// AND the legacy <c>ConfirmButton</c>/<c>UndoButton</c> one.
    ///
    /// <para>A NULL ENTRY IS NORMAL, NOT A FAULT. Every board shipped up to and including the
    /// current bundle has exactly TWO recesses, so seat 2 is null there and the cluster runs as the
    /// two-seat one it always was. Seats 0/1 are null only on the procedural fallback board (no
    /// bundle at all), where <c>BuildButtons</c> synthesises them at the authored
    /// <c>ButtonZoneX</c> offsets exactly as before.</para>
    ///
    /// <para>These are DESCENDANTS OF THE VISUAL, so they die with <c>_root</c> and are cleared in
    /// the teardown next to <see cref="_confirmAnchor"/>.</para>
    /// </summary>
    private readonly Transform?[] _buttonSeats = new Transform?[BoardAnchors.ButtonSeatCount];

    /// <summary>Reused list for <see cref="LiveBoardAnchors"/> — the four frame anchors plus
    /// whichever button seats the live board supplies. Build-time only (EnsureBuilt), never per
    /// frame.</summary>
    private readonly List<Transform> _anchorScratch = new(4 + BoardAnchors.ButtonSeatCount);

    /// <summary>
    /// EVERY anchor of the live bundled board as one list: the four frame anchors plus the button
    /// seats that resolved. Both build-time passes that walk "all the anchors" — the face-frame
    /// re-alignment and the asset-pose pin capture — read THIS, so they can never disagree about
    /// the set. They used to carry two hand-written six-element arrays, which is precisely the shape
    /// of bug a third recess introduces: one list updated, the other not, and a seat that rotates
    /// with the board but does not stay pinned when the mesh pose moves underneath it.
    /// </summary>
    private List<Transform> LiveBoardAnchors()
    {
        _anchorScratch.Clear();
        if (_slots[0] != null) _anchorScratch.Add(_slots[0]!);
        if (_slots[1] != null) _anchorScratch.Add(_slots[1]!);
        if (_shortRestAnchor != null) _anchorScratch.Add(_shortRestAnchor!);
        if (_longRestAnchor != null) _anchorScratch.Add(_longRestAnchor!);
        for (int i = 0; i < _buttonSeats.Length; i++)
            if (_buttonSeats[i] != null)
                _anchorScratch.Add(_buttonSeats[i]!);
        return _anchorScratch;
    }

    /// <summary>
    /// One line per board build naming WHICH seat resolved under WHICH spelling — the field the
    /// "name the blocker, not the number" lesson asks for. "2 of 3" alone cannot tell a board that
    /// legitimately has two recesses from one whose third anchor was misspelled by the asset lane;
    /// the per-seat name does.
    /// </summary>
    private void LogSeatResolution(int seatsFound)
    {
        for (int i = 0; i < _buttonSeats.Length; i++)
        {
            Transform? t = _buttonSeats[i];
            VRLog.Info("Cards", t != null
                ? $"Board: button seat {i} resolved to anchor '{t.name}' (accepted: {BoardAnchors.SeatNamesJoined(i)})."
                : $"Board: button seat {i} ABSENT — this board carries none of {BoardAnchors.SeatNamesJoined(i)}" +
                  (i < 2 ? "; BuildButtons will synthesise a procedural anchor."
                         : "; the cluster runs as a two-seat one (every board shipped so far)."));
        }
        VRLog.Info("Cards", $"Board: {seatsFound} of {BoardAnchors.ButtonSeatCount} button seats " +
                            $"supplied by the '{CardsConfig.CurrentBoard}' asset.");
    }

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
    // Item-surrender pick: demand-specific label override for the item-use confirm button
    // ("ITEM ABGEBEN" instead of "USE"); null = the default GUI_USE label.
    private string? _itemUseConfirmLabel;
    private System.Action? _itemUseConfirmAction;
    private bool _placed;
    private bool _wantVisible;
    private bool _placementDeferLogged;
    /// <summary>True once this tray instance has completed a head-relative placement. The
    /// FIRST one uses the fixed left-of-head spawn seat (user ruling 2026-08-03); every later
    /// one uses the saved layout. Instance state on purpose: a board SWITCH rebuilds the tray
    /// but keeps the instance and restores the captured pose, so it must not re-seat.</summary>
    private bool _everPlaced;


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
    /// mount supplies the docked rotation/scale frame at attach time, and (via
    /// <see cref="ButtonClusterOffset"/>) the per-board seat the player tuned.
    /// Null until built.
    /// </summary>
    internal Transform? ButtonClusterMount => _clusterMount;

    /// <summary>
    /// The per-board <c>[Cards] ClusterOffset_{board}</c> ("Tastengruppe: Position") the mount is
    /// currently carrying, in tray-ROOT-local meters — the same frame and the same unit as the
    /// cluster's own column anchor and as the <c>[RoundButtons]</c> group offset that adds beside
    /// it, so ±0.01 on either stepper is the same centimetre on the board.
    ///
    /// <para>USER REPORT 2026-08-09: "Die Offsets bei den Überspringen-Tasten haben keinen
    /// Einfluss. Alles andere scheint zu funktionieren, aber die Offsets verändern nichts." The
    /// fresh hardware log is unambiguous about WHICH offset: it carries ~25 lines of
    /// "[Cards] Debug live-apply [Oak]: cluster offset (-0.01, 0.00, 0.00)" — the user walking all
    /// three axes of this dial out and back to zero — while the neighbouring ClusterScale (1.00 →
    /// 1.15 → 1.00) and the [RoundButtons] Height/Depth steppers in the same debug-menu block moved
    /// the cap every time. The dial did nothing because the rigid-dock lag fix reparented the
    /// cluster from THIS MOUNT to the tray root and kept only the mount's rotation and scale: the
    /// translation the player was tuning was written to a transform that had stopped having
    /// children. This property is the seam that puts it back into the pose solve.</para>
    ///
    /// <para>IT IS A DELTA OFF THE MOUNT, NOT A CONFIG READ, and that is what keeps it from being
    /// applied twice. <c>ButtonCluster</c> is still forbidden to read
    /// <c>CardsConfig.ClusterOffset</c> — the warning its <c>ClusterProudOffset</c> comment has
    /// carried since the dock was written — because <see cref="SetClusterLayout"/> already turns
    /// that config into meters here. Asking the mount what it ended up at keeps exactly one
    /// conversion in the mod, so the live-apply path and the build path can never disagree.</para>
    /// </summary>
    internal Vector3 ButtonClusterOffset =>
        _clusterMount != null ? _clusterMount.localPosition - ClusterMountBase : Vector3.zero;

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

    // No unregister-by-collider counterpart: transient targets (item chips, destroyed caps) are
    // dropped by the null-collider sweep instead — see PurgeDeadLaserTargets and the three
    // RemoveAll(t => t.Collider == null) sites in PlayTray.3.Pose. Destroying the object IS the
    // unregister, so a by-collider variant only ever duplicated it.

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

        // THE BUTTON SEATS the bundled board supplies, by index (0 = the commit seat). Resolved
        // through BoardAnchors, which accepts BOTH the new ButtonSeat1/2/3 spelling and the legacy
        // ConfirmButton/UndoButton one — see that class's note. A null entry is a seat this board
        // does not have; BuildButtons synthesises a procedural anchor for the ones it needs.
        for (int i = 0; i < _buttonSeats.Length; i++)
            _buttonSeats[i] = null;
        GameObject? prefab = factory.GetTrayPrefab();
        if (prefab != null)
        {
            GameObject visual = Object.Instantiate(prefab, _root, false);
            visual.name = "TrayVisual";
            _slots[0] = FindDeep(visual.transform, "Slot1");
            _slots[1] = FindDeep(visual.transform, "Slot2");
            _shortRestAnchor = FindDeep(visual.transform, "ShortRestToken");
            _longRestAnchor = FindDeep(visual.transform, "LongRestToken");
            int seatsFound = BoardAnchors.ResolveSeats(visual.transform, _buttonSeats);
            LogSeatResolution(seatsFound);

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
                List<Transform> live = LiveBoardAnchors();
                for (int i = 0; i < live.Count; i++)
                    live[i].rotation = faceWorld;
            }

            // Asset-only pose (user request): the visual mesh can be offset/tilted PER BOARD
            // without moving anything that docks to it. The anchors (four frame + the board's
            // button seats, LiveBoardAnchors) are DESCENDANTS of the
            // visual, so posing the mesh would drag every element along — capture their
            // root-local poses NOW, while the visual is untouched, so SetAssetPose can move the
            // mesh underneath them and pin them back exactly where they were.
            _visual = visual.transform;
            _visualBasePos = _visual.localPosition;
            _visualBaseRot = _visual.localRotation;
            _assetPinnedAnchors.Clear();
            List<Transform> pinned = LiveBoardAnchors();
            for (int i = 0; i < pinned.Count; i++)
            {
                Transform a = pinned[i];
                _assetPinnedAnchors.Add((a, _root.InverseTransformPoint(a.position),
                                         Quaternion.Inverse(_root.rotation) * a.rotation));
            }
            SetAssetPose(CardsConfig.AssetOffset(CardsConfig.CurrentBoard).Value,
                         new Vector3(CardsConfig.AssetPitch(CardsConfig.CurrentBoard).Value,
                                     CardsConfig.AssetYaw(CardsConfig.CurrentBoard).Value,
                                     CardsConfig.AssetRoll(CardsConfig.CurrentBoard).Value));

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
                _boardColliderRegistered = true;
            }

            // PART B: the old unreliable raycast reseat (ReseatProud, since deleted) is GONE. Every attached
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

        // (The round-13 SLOT SEAT LINER used to be built here, ahead of the glows. It is GONE —
        // see the retirement note above BuildSlotHighlights in PlayTray.4.Slots.cs.)
        BuildSlotHighlights();
        BuildWantedHighlights();
        BuildButtons(_buttonSeats);
        BuildItemUseSlot();
        BuildHandle();
        BuildDashboardControls();
        BuildRoundReadout();
        BuildMounts();
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
        // CORE FIX: the readout now seats PROUD (NewAnchor at the fixed FixedProudZ, plus the
        // per-board ReadoutOffset), so it is depth-correct — drop the forced draw-over-the-board.
        // Lit (Standard) plate so it shades like a real object instead of the flat unlit Overlay.
        // (This used to credit a "SeatOnBoardFace raycast"; that seating path never ran for the
        // readout and has since been removed — see the "raycast seating (REMOVED)" note.)
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

        // 2026-08-04 (same defect family as the status placard): the TMP label is a depth-less
        // transparent renderer at order 0 — join the board's furniture order group so a panel
        // BEHIND the board can no longer paint over it (the opaque plate needs nothing).
        AdoptFurniture(readoutGo);
    }

    /// <summary>Cluster dock scale: the cluster's real-meter layout shrunk onto the button strip.</summary>
    private const float ButtonClusterMountScale = 0.7f;

    /// <summary>Cluster mount board-Y (collision math in <see cref="BuildMounts"/>).</summary>
    private const float ButtonClusterMountY = -0.115f;

    private void BuildMounts()
    {
        _initiativeMount = new GameObject("InitiativeMount").transform;
        _initiativeMount.SetParent(_root, worldPositionStays: false);
        // PART B: the initiative-track mount position is PER-BOARD (debug-menu tunable) and comes
        // WHOLLY from [Cards] InitiativeOffset_{board} — there is no fixed fallback Y any more.
        // Board-Y convention: +Y = the board's BACK/far edge. Watch that edge when tuning: an
        // early fixed y ≈ 0.172 (BoardH*0.5 = 0.16) OVERHUNG it, which is what made the mount
        // per-board in the first place.
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
        // (THE AUTHORED y BELOW IS -0.29; the SHIPPED seat has been -0.447 ever since the
        // per-board offset shipped at -0.157, and since ModBuild 90 that displacement lives in
        // DecisionMountBase itself — see its doc. The clearance reasoning that follows is the
        // original authored analysis and still bounds the mount's own zone; where the widget ROW
        // actually lands is solved against the grab bar, not against this y.)
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
        // Vertically: two stacks at y = ±spacing/2 — the step is the per-board dial
        // [Cards] PileSpacing_{board} (PileViewer reads it), Oak default 0.116 → ±0.058; each cell
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
    private Transform? _followAnchor; // items 4/6: follow/pin toggle anchor, moved by the per-board PinOffset

    // ---- IPanelGrabOwner (test #19: the grab mechanics moved into the shared
    // WorldUI.PanelGrabHandle core so panels can be grabbed exactly like the tray;
    // the tray's behavior is unchanged — same carry, resize, persistence, logs).
    Transform? WorldUI.IPanelGrabOwner.GrabRoot => Root;
    bool WorldUI.IPanelGrabOwner.GrabVisible => IsVisible;

    /// <summary>Item 12: the carry follows [Cards] BoardMoveMode (read per-frame — a settings
    /// change applies to the very next carry frame, even mid-grip).</summary>
    WorldUI.PanelCarryMode WorldUI.IPanelGrabOwner.CarryMode => CardsConfig.BoardMoveMode.Value switch
    {
        BoardMoveMode.Free => WorldUI.PanelCarryMode.Free,
        BoardMoveMode.LimitedPitch => WorldUI.PanelCarryMode.LevelPitch,
        _ => WorldUI.PanelCarryMode.Level, // Begrenzt (default) = today's yaw-only carry
    };

    /// <summary>"Level" for the board means PLAIN WORLD level (user decision 2026-08,
    /// supersedes item 11): the board is deliberately world-frame and fully decoupled from the
    /// world tilt — under an active tilt it simply looks tilted like the rest of the world and
    /// the user lays it out to taste in Free mode. Identity like every other owner (the
    /// interface + LevelPose machinery stay — item 12's movement modes build on them).</summary>
    Quaternion WorldUI.IPanelGrabOwner.GrabLevelFrame => Quaternion.identity;

    /// <summary>Item 12: the ABSOLUTE level-frame pitch window for the LimitedPitch carry.
    /// The config window is degrees around the per-board BoardTilt (TrayPitch semantics:
    /// positive = more upright), the carry works in extracted x-pitch (90° − tilt − TrayPitch),
    /// hence the mirrored mapping.</summary>
    Vector2 WorldUI.IPanelGrabOwner.GrabPitchLimits
    {
        get
        {
            float baseX = 90f - CardsConfig.BoardTilt(CardsConfig.CurrentBoard).Value;
            (float min, float max) = CardsConfig.BoardPitchWindow;
            return new Vector2(baseX - max, baseX - min);
        }
    }

    /// <summary>
    /// APPARENT-SIZE LIMITS, enforced INSIDE the two-hand gesture (user report 2026-08-04: in
    /// FOLGEN mode the resize pushed past the board's min/max). The frame-by-frame push
    /// (<see cref="ClampApparentSize"/>) deliberately never runs while a hand grips the bar, and
    /// the handle's generic factor range bounds the wrong quantity: the apparent width per
    /// localScale unit moves with the world zoom (rig scale vs. the tray's parent-chain scale —
    /// in FOLLOW mode the hands-root parent does NOT track the live rig zoom, hardware log:
    /// constant "parent chain ×25.18" against rig scales 4–70), so a factor the range allows can
    /// be metres of perceived width. This window converts the perceived-cm limits into localScale
    /// bounds with the SAME measure the push uses (one source of truth,
    /// <see cref="TryGetApparentWidthPerScaleUnit"/>), read live each resize frame — the pinch
    /// simply stops at the limit, in EVERY anchor mode, and nothing resizes on its own.
    ///
    /// <para><b>AND IT IS INTERSECTED WITH WHAT THE CONFIG CAN STORE, which is new on 2026-08-25
    /// and is the guard that keeps this feature away from the user's hand-tuned numbers.</b> Since
    /// the limits are divided by the LIVE rig again, this window is proportional to the rig scale:
    /// at four times the zoom the board was pinned at, the perceived window [18, 140] cm becomes a
    /// localScale window of [1.125, 8.75] — while <c>TrayScale × BoardScale_Oak</c> can only express
    /// [0.271, 1.085]. Every single release would then land outside what the config can hold, and
    /// <see cref="PersistPoseToConfig"/> would absorb the overflow into <c>BoardScale_{board}</c> —
    /// the 2026-08-15 ratchet, which is the very thing whose repeat he reported ("weiterhin hat sich
    /// damit auch das maximum und minimum wieder verschoben", measured as Steel 0.54 → 1.00 → 1.13
    /// in one session). So the gesture may only author sizes the config can reproduce bit-exactly.
    /// When the two ranges do not meet — or when the push has already parked the board outside their
    /// intersection — the window collapses onto the size the board ALREADY has, so the pinch is
    /// INERT at that zoom instead of authoring something the config cannot hold. Inert and not
    /// widened: a window that reaches past what can be stored would let a release land there, and a
    /// window that excludes the live size would make the board snap on the grab's first frame.
    /// The honest trade is that his configured 18/140 cm window is enforced by the push at every
    /// zoom, and the two-hand gesture gets whatever part of it TrayScale's own 0.5–2 band can also
    /// persist. If the collapsed case turns out to be common on hardware, the fix is to widen
    /// <see cref="CardsConfig.TrayScaleMin"/>/<see cref="CardsConfig.TrayScaleMax"/> — a settings
    /// range, which is a user decision — and NOT to let the ratchet back in.</para>
    /// </summary>
    Vector2 WorldUI.IPanelGrabOwner.GrabScaleLimits
    {
        get
        {
            if (_root == null
                || !TryGetApparentWidthPerScaleUnit(out float perUnit, out _, out _))
                return new Vector2(WorldUI.PanelGrabHandle.MinScale, WorldUI.PanelGrabHandle.MaxScale);
            float live = _root.localScale.x;
            if (!(live > 1e-4f) || float.IsInfinity(live))
                return new Vector2(WorldUI.PanelGrabHandle.MinScale, WorldUI.PanelGrabHandle.MaxScale);

            // The user's window, in units of the tray's own localScale.
            float lo = MinWidthMeters / perUnit;
            float hi = MaxWidthMeters / perUnit;

            // Intersected with what TrayScale × BoardScale_{board} can express — the SAME arithmetic
            // PersistPoseToConfig round-trips through, so the two must be read from one pair of
            // constants or they will drift apart.
            float boardScale = Mathf.Max(0.01f, CardsConfig.BoardScale(CardsConfig.CurrentBoard).Value);
            lo = Mathf.Max(lo, CardsConfig.TrayScaleMin * boardScale);
            hi = Mathf.Min(hi, CardsConfig.TrayScaleMax * boardScale);

            // THE PINCH IS INERT RATHER THAN UNSTORABLE. If the two windows do not meet, or the size
            // the board already has lies outside their intersection (which is where the push parks
            // it once a bound has walked past the expressible band), the only value this gesture may
            // write is the one already there: widening the window to reach the live size would let
            // the player release at a size the config cannot reproduce, and that release is the
            // BoardScale_{board} ratchet. Returning the live size on both ends also means the grab
            // cannot SNAP the board on its first frame, which widening was there to prevent —
            // PanelGrabHandle clamps its target into this window before it lerps.
            if (lo > hi || live < lo || live > hi)
                return new Vector2(live, live);
            return new Vector2(lo, hi);
        }
    }

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

        // CORE FIX: gear + follow-toggle now seat PROUD via NewAnchor's fixed FixedProudZ (this
        // used to say "→ SeatOnBoardFace raycast"; NewAnchor seats at a fixed Z instead, and that
        // raycast path has since been removed) and are LIT (overlay:false → Standard base + cap),
        // so their side walls shade and they read as solid protruding buttons. Drop the forced
        // draw-over-the-board — they are depth-correct and self-occlude like real buttons now.
        Transform pinAnchor = NewAnchor("FollowToggle",
            new Vector3(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -0.002f));
        // Items 4/6: nudge the follow/pin toggle by the per-board offset (base PinBase set by NewAnchor).
        pinAnchor.localPosition += CardsConfig.PinOffset(CardsConfig.CurrentBoard).Value;
        _followAnchor = pinAnchor;

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
        if (_followAnchor == null)
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
        _followToggle.WireCap = Net.NetProtocol.CapPressFollowPin;
        _followToggle.SetState(true, accent: !CardsConfig.TrayFollow.Value);
        RegisterLaserTarget(_followToggle.Collider!, _followToggle);

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
            // Freeze-sentinel announcement: engaging the pin is world-pose-preserving
            // (worldPositionStays), but the re-parent under the scaled holder can leave
            // float-noise-sized deltas — name it so it never reads as an unknown writer.
            NotePinnedWrite("pin engaged (ApplyFollowMode — world-pose-preserving re-parent)");
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
        _roundLabel = null; // child of _root, destroyed with it
        _roundShown = int.MinValue;
        _confirmedLabel = null;
        _confirm = null;
        _undo = null;
        _pickBannerRoot = null;  // child of _root, destroyed with it
        _pickBannerLabel = null;
        _pickBannerText = null;
        _pickConfirmLabel = null;
        _pickUndoLabel = null;
        _confirmAnchor = null; // child of _root, destroyed with it
        for (int i = 0; i < _buttonSeats.Length; i++)
            _buttonSeats[i] = null; // descendants of the visual, destroyed with _root
        _undoAnchor = null;
        _itemUseSlot = null; // child of _root, destroyed with it
        _itemUseSlotGlow = null;
        _itemUseConfirm = null; // #9a: generic-cluster child (under _root via the Confirm anchor), destroyed with it
        _itemUseConfirmAction = null;
        _itemUseActive = false; // #9a: a fresh tray starts with the tuned 2-member cluster (no pending item)
        _handle = null; // child of _root, destroyed with it
        _followToggle = null;
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
        // A full Destroy is leaving the scenario (a board SWITCH does not come through here — it
        // captures and restores the pose around EnsureBuilt and never calls PlaceAtHead), so the
        // next scenario gets the fixed left-of-head first seat again, which is what "beim ersten
        // Spawnen" means.
        _everPlaced = false;
        _placementDeferLogged = false;
        _lastSlotActivity = float.NegativeInfinity;
        _boardColliderRegistered = false;
        // Pin bookkeeping: the PlayTray INSTANCE outlives its root (board switch / rebuild), so
        // stale pin state would otherwise be applied to the next root. (The lost-board dwell
        // timer that used to be reset here is gone with the automatic recall.)
        _pinPoseVersion = -1;
        _rigLocalPinValid = false;
        _pinHousekeepingMove = null;
        _pinFreezeValid = false;   // freeze sentinel: never diff a new root against the old one's pose
        _pinFreezeSource = null;
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
    /// [Cards] BoardTilt_{board} is degrees FROM HORIZONTAL: 0 = flat desk, 90 = upright
    /// panel; default 30 reads like a lectern / card-table edge (test #10). (The global
    /// [Cards] TrayTilt it replaced is still bound so old cfg files load, but nothing reads
    /// it — see ComputeBoardRotation.) The board's -Z (element side) faces up toward the player.
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
        // PLAIN WORLD-FRAME math (user decision 2026-08, supersedes item 11): this briefly ran
        // in the rig's perceived-level tilt frame so the board would author "level for the
        // player" under [Rig] WorldTiltDegrees; the user rejected that coupling — the board is
        // deliberately world-frame (under a tilt it looks tilted like the rest of the world and
        // is laid out manually in Free mode). At tilt 0 both versions were bit-identical, so
        // this is exactly the pre-item-11 behavior.
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flatForward);

        ControlBoard board = CardsConfig.CurrentBoard;
        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        // FIRST PLACEMENT OF A SCENARIO GETS A FIXED SEAT (user ruling 2026-08-03: "Beim ersten
        // Spawnen sollte das Controllboard immer links neben dem Kopf spawnen"). The persisted
        // TrayOffset is wherever the last drag left the board, so the board used to start each
        // scenario somewhere different — and with a big forward component it starts IN FRONT of
        // the player rather than beside them. Only the very first placement is overridden; every
        // later path through here (board switch, explicit recall, follow-mode re-seat) keeps using
        // the saved layout, so nothing the player arranges during the session is thrown away.
        bool firstSeat = !_everPlaced && CardsConfig.SpawnLeftOfHead != null
                         && CardsConfig.SpawnLeftOfHead.Value;
        Vector3 offset = firstSeat
            ? CardsConfig.SpawnSeatOffset  // (left, -down, forward), real meters
            : CardsConfig.TrayOffset;      // (right, -down, forward), real meters
        // PART B: per-board BoardPosOffset is ADDED (in the head frame) on top of the tray offset.
        Vector3 boardPos = CardsConfig.BoardPosOffset(board).Value;
        Vector3 levelDelta = flatForward * ((offset.z + boardPos.z) * scale)
                             + right * ((offset.x + boardPos.x) * scale)
                             + Vector3.up * ((offset.y + boardPos.y) * scale);

        // Item 1/3 safety net (never spawn/glitch outside the skybox): clamp the final pose to a
        // sane reach from the head. The deferral above catches the untracked-HMD / origin-camera
        // cases; this is the belt-and-braces guard for every OTHER cause (a wildly mis-tuned
        // BoardPosOffset, an odd rig scale, a frozen-then-restored head pose after an HMD doff/don)
        // — the board can never sit more than ~1.2 m from the head, at a plausible height, no
        // matter what the placement math produced.
        float outBefore = levelDelta.magnitude;
        levelDelta = ClampNearHead(levelDelta, scale, out bool wasClamped);
        Vector3 pos = headT.position + levelDelta;
        if (wasClamped)
            VRLog.Info("Cards", $"Control board placement clamped to a sane reach from the head " +
                                $"(was {outBefore / Mathf.Max(scale, 1e-4f):F2} m out at scale 1, " +
                                $"now {levelDelta.magnitude / Mathf.Max(scale, 1e-4f):F2} m).");

        _root.position = pos;
        _root.rotation = ComputeBoardRotation(flatForward, board);
        _root.localScale = Vector3.one * ComputeBoardScale(board);
        NotePinnedWrite("head-relative placement (PlaceAtHead — first seat/recall/recovery)");
        _placed = true;
        _everPlaced = true;
        VRLog.Info("Cards", $"Control board placed ({board}: tilt {CardsConfig.BoardTilt(board).Value}°, " +
                            $"yaw {CardsConfig.TrayYaw.Value + CardsConfig.BoardYaw(board).Value:F0}°, " +
                            $"scale {ComputeBoardScale(board):F2}×)" +
                            (firstSeat
                                ? $" — FIRST SEAT: fixed spot beside the head on the LEFT ({offset.x:F2} m " +
                                  $"side, {offset.z:F2} m forward, {-offset.y:F2} m down), not the saved " +
                                  "layout, so every scenario starts with the board in the same place " +
                                  "([Cards] SpawnLeftOfHead)."
                                : $" — saved layout ({offset.x:F2}, {offset.y:F2}, {offset.z:F2} m)."));
        // A persisted PINNED mode re-engages only NOW, at the just-placed
        // head-relative pose (test #17): the tray always spawns in front of the
        // player, pinned or not.
        if (!CardsConfig.TrayFollow.Value && _root.parent != _pinRoot)
            ApplyFollowMode();

        LogBoardFaceDiagnostics(); // ITEM 2 ground truth in the final placed pose (once per board)
    }

    /// <summary>
    /// PART B: the board's WORLD rotation from the (world-frame) flat forward + the
    /// per-board tilt/yaw. BoardTilt REPLACES the old global TrayTilt (seeded 30 so Oak is
    /// unchanged); BoardYaw is ADDED on top of the grab-written TrayYaw (seeded 0). Shared by
    /// <see cref="PlaceAtHead"/> and <see cref="ReapplyOrientation"/> — both run in the plain
    /// world frame (user decision 2026-08: the board is deliberately decoupled from the world
    /// tilt; the perceived-level compositions of item 11 are gone). Item 12:
    /// the grab-authored TrayPitch adds on top of BoardTilt per the movement mode
    /// (<see cref="CardsConfig.EffectiveTrayPitch"/> — 0 in Begrenzt, so the default pose is
    /// bit-identical to before).
    /// </summary>
    private static Quaternion ComputeBoardRotation(Vector3 flatForward, ControlBoard board) =>
        Quaternion.Euler(0f, CardsConfig.TrayYaw.Value + CardsConfig.BoardYaw(board).Value, 0f)
        * Quaternion.LookRotation(flatForward, Vector3.up)
        * Quaternion.Euler(90f - CardsConfig.BoardTilt(board).Value - CardsConfig.EffectiveTrayPitch, 0f, 0f);

    /// <summary>PART B: the board's local scale = grab-written TrayScale × per-board BoardScale
    /// (seeded 1). The per-board factor is floored at 0.05 (user ruling 2026-08-13): TrayScale
    /// is already read-clamped to 0.5-2 by ClampedTrayScale, but BoardScale_{board} carries no
    /// range — it is grab-WRITTEN (PlayTray.3.Pose absorbs whatever TrayScale cannot express),
    /// so a config range would fight the gesture. A hand-edited 0 would leave the control board
    /// at zero size, i.e. no board at all, and the board is not optional content. Floored at the
    /// READ instead, which changes nothing for any value the gesture can produce.</summary>
    private static float ComputeBoardScale(ControlBoard board) =>
        CardsConfig.ClampedTrayScale * Mathf.Max(0.05f, CardsConfig.BoardScale(board).Value);

    /// <summary>
    /// Item 3 safety net: clamp a candidate board offset from the head to a sane reach — no
    /// more than ~1.2 m horizontally and a plausible height band. Applied on EVERY head-relative
    /// placement path (<see cref="PlaceAtHead"/> and the presence-regain re-assert) so a bogus or
    /// frozen-then-restored head pose can never fling the board outside the play space.
    /// Operates on the plain WORLD-frame delta (head → board) since the board's decoupling
    /// from the world tilt (user decision 2026-08 — "horizontal"/"height" are world axes).
    /// </summary>
    private static Vector3 ClampNearHead(Vector3 levelDelta, float scale, out bool clamped)
    {
        var horizontal = new Vector3(levelDelta.x, 0f, levelDelta.z);
        float maxReach = 1.2f * scale;
        if (horizontal.magnitude > maxReach)
            horizontal = horizontal.normalized * maxReach;
        float clampedY = Mathf.Clamp(levelDelta.y, -1.0f * scale, 0.2f * scale);
        Vector3 result = horizontal + Vector3.up * clampedY;
        clamped = (result - levelDelta).sqrMagnitude > 1e-6f;
        return result;
    }

    // The LostReach distance threshold (6 m) that used to live here is GONE with the automatic
    // recall (user ruling 2026-08-03 — see the block at the top of PlayTray.2.Watchdog.cs). DO NOT
    // RE-ADD A DISTANCE THRESHOLD: how far a PINNED board is from the head is a consequence of
    // pinning it and walking off, never evidence that it needs moving.

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

        // PINNED: keep the world pose. DISTANCE IS NOT A REASON TO MOVE IT (user ruling
        // 2026-08-03 — see the block at the top of PlayTray.2.Watchdog.cs): a pinned board being
        // far away or below the head is the normal consequence of pinning it and then walking
        // off, not a glitch. Only a NON-FINITE pose is recovered, because that is not a position
        // at all and nothing parented to it renders.
        Vector3 pos = _root.position;
        bool finite = !(float.IsNaN(pos.x) || float.IsInfinity(pos.x)
                        || float.IsNaN(pos.y) || float.IsInfinity(pos.y)
                        || float.IsNaN(pos.z) || float.IsInfinity(pos.z));
        if (!finite)
        {
            PlaceAtHead(); // snaps back to the configured head-relative pose and re-pins (clamped)
            VRLog.Info("Cards", $"Control board pose was NON-FINITE ({reason}, PINNED) — re-seated near the head.");
        }
        if (_wantVisible)
            SetVisible(true); // re-show a pinned board that a presence blip hid
    }

    // ------------------------------------------ solid occluder + furniture draw order --

    /// <summary>
    /// Nearest SOLID board hit along the aim ray, or +inf (board hidden / ray misses it).
    ///
    /// ROOT CAUSE (user report 2026-08-04: "collidet der Laser mit etwas davor, soll er nicht
    /// unsichtbar weitergehen und trotzdem im Optionsmenu etwas auswaehlen"): every board
    /// surface - the board mesh, the keycaps, the pile stacks, the rest discs - lives on the
    /// mod layer as TRIGGER colliders, tested geometrically by the board laser
    /// (CardsDriver.UpdateBoardLaser) and INVISIBLE to <c>RayInteractor</c>'s physics pick
    /// Mask. RayUguiDriver's occlusion rule ("a nearer physics hit blocks the UI hit")
    /// therefore never saw the board: with the board pushed in front of the options menu the
    /// beam visibly landed ON the board while the uGUI raycast sailed through it and delivered
    /// hover + click to the menu tabs behind. This method is the board's answer to the ray's
    /// per-frame "what solid mod-owned surface do I cross first?" question
    /// (<c>RayInteractor.SolidOccluderDistance</c>) - the exact scan the board laser runs
    /// (registered laser-target colliders + the two slotted cards), reduced to a distance.
    /// Runs BEFORE the interactor drivers each frame, so the uGUI/grab arbitration sees the
    /// board the same frame it sees everything else. No allocations (Collider.Raycast over the
    /// small registry; the Ray struct is a stack value).
    /// </summary>
    internal float RaycastSolidDistance(Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (!IsVisible)
            return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        var ray = new Ray(origin, direction);
        for (int i = 0; i < LaserTargets.Count; i++)
        {
            Collider col = LaserTargets[i].Collider;
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                continue;
            if (col.Raycast(ray, out RaycastHit hit, maxDistance) && hit.distance < best)
                best = hit.distance;
        }
        if (TryRaycastCards(origin, direction, out _, out _, out float cardDist)
            && cardDist <= maxDistance && cardDist < best)
            best = cardDist;
        return best;
    }

    /// <summary>
    /// Adopt every TRANSPARENT renderer under <paramref name="subtree"/> into the current
    /// board's furniture draw-order group (<see cref="WorldUI.CanvasConversion.RegisterFurniture"/>
    /// - see part 9's root-cause header: the status placard, labels and glow quads write no
    /// depth and sat at sortingOrder 0..3, so any converted panel BEHIND the board painted
    /// over them). Static + <see cref="Current"/>-based so the nested builders (BoardButton,
    /// PileStack) and RestControls can call it without threading a tray reference. Opaque and
    /// AlphaTest materials (queue &lt;= 2500) are skipped - they write depth and already
    /// resolve against panels per pixel. Call AFTER all relative sortingOrder writes on the
    /// subtree (the current order is captured as the in-band offset).
    /// </summary>
    internal static void AdoptFurniture(GameObject? subtree)
    {
        PlayTray? tray = Current;
        if (tray == null || subtree == null)
            return;
        foreach (Renderer r in subtree.GetComponentsInChildren<Renderer>(true))
        {
            Material? m = r.sharedMaterial;
            if (m == null || m.renderQueue <= 2500)
                continue; // depth-writing opaque/cutout: already correct against depthless panels
            WorldUI.CanvasConversion.RegisterFurniture(tray, r);
        }
    }

    string WorldUI.IFurnitureOrderAnchor.FurnitureOrderName => "control board";

    /// <summary>Alive while the tray root exists - NOT gated on visibility, so a hidden board
    /// keeps its registrations and re-shows with correct orders (a rebuild re-adopts anyway).</summary>
    bool WorldUI.IFurnitureOrderAnchor.FurnitureOrderAlive => _root != null;

    /// <summary>Extra rect margin (board-local meters) around the slab when measuring the
    /// furniture group's eye distance: the placard hovers ~0.16 m above the top edge, the pile
    /// captions and the FIXIERT toggle hang below/beside it - the measure must cover the whole
    /// furnished apron or a menu tucked right behind an overhanging piece could out-measure it.</summary>
    private const float FurnitureApronMeters = 0.18f;

    /// <summary>
    /// The furniture group's eye distance: nearest point of the board's furnished face rect
    /// (slab plus <see cref="FurnitureApronMeters"/> apron, on the z=0 face plane) - the same
    /// clamp-into-rect measure <c>CanvasConversion.PanelEyeDistance</c> uses for panels, so the
    /// group and the panels are ranked by directly comparable numbers.
    /// </summary>
    float WorldUI.IFurnitureOrderAnchor.FurnitureEyeDistance(Vector3 eye)
    {
        if (_root == null)
            return float.PositiveInfinity;
        Vector3 local = _root.InverseTransformPoint(eye);
        float halfW = BoardHalfWidthLocal + FurnitureApronMeters;
        float halfH = BoardH * 0.5f + FurnitureApronMeters;
        var onFace = new Vector3(
            Mathf.Clamp(local.x, -halfW, halfW),
            Mathf.Clamp(local.y, -halfH, halfH),
            0f);
        return Vector3.Distance(eye, _root.TransformPoint(onFace));
    }
}
