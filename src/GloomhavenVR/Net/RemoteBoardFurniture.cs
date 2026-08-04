using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The INTERACTIVE FURNITURE of a peer's control board, reproduced on their remote board as
/// PURELY INERT VISUALS.
///
/// WHY THIS EXISTS AT ALL. An earlier pass rendered only the informational surfaces (objectives,
/// elements, round, initiative badge, rest badge, active cards, pile counters) and DELIBERATELY
/// left the furniture out, on the argument that "a button you cannot press is not information".
/// The user rejected that outright ("Das will ich NICHT, ALLES von den aufgeführten Elementen soll
/// dargestellt werden. Aber wichtig: Nichts davon soll man interagieren können auf dem fremden
/// Board, es ist eine reine Darstellung."). A peer's board must LOOK like a control board — the
/// keycaps, the rest discs, the pin toggle, the handle bar, the turn-flow cap, the item-use recess
/// and the decision drawer are what makes it read as one — while remaining completely untouchable.
///
/// SINCE THE 3D-PARITY PASS ("komisch 2D" rejection) the caps are REAL 3D GEOMETRY, built from the
/// very meshes the local board's <c>PlayTray.BoardButton</c> uses — the beveled keycap
/// (<c>CardMesh.BuildBeveledKeycap</c>: state-coloured plateau, bright chamfer ring, dark warm
/// walls) and the smooth round disc (<c>CardMesh.GetRoundCap</c>) — skinned through the SAME
/// <c>PlayTray.NewKeycapMaterial</c> (BoardLit + carved-grain texture) and labelled with the same
/// engraved parchment type (<c>NativeButtonSkin.StyleEngravedLabel</c>). What is NOT reproduced is
/// everything that made the local widgets buttons: no collider, no press travel, no
/// <c>PokeableBehaviour</c>, no registration anywhere.
///
/// SEATING. When the REAL tray asset is up (<see cref="RemoteTrayVisual"/>), the caps sit on the
/// prefab's own anchors (<c>ConfirmButton/UndoButton/ShortRestToken/LongRestToken</c>) plus the
/// AUTHORED per-board offsets (<c>Defaults.ConfirmUndoOffset_*</c>, <c>RestButtonOffset_*</c>, …,
/// keyed by the PEER's synced style) — the same anchor + authored-offset seat the owner's own
/// board uses. The peer's private debug-menu RE-tuning of those offsets stays off the wire and is
/// NOT applied (DELIBERATELY-NOT, as ever): every client renders a given board style at its
/// shipped layout. On the flat fallback board (bundle absent) the caps keep the legacy Oak
/// board-local constants.
///
/// NON-INTERACTIVE IS A HARD REQUIREMENT, and it is enforced three ways, not one:
///   1. CONSTRUCTION — nothing here ever creates a <c>Collider</c>, a <c>Rigidbody</c> or a
///      <c>GrabbableBehaviour</c>; primitives have their colliders destroyed at creation.
///   2. REGISTRATION — this file never calls <c>PlayTray.RegisterLaserTarget</c>, never implements
///      <c>IPokeable</c>/<c>IGrabbable</c>, never touches <c>VRInteractables</c>,
///      <c>UguiPokeSurfaces</c> or <c>LaserTargets</c>. It has no click callbacks at all.
///   3. BELT AND BRACES — <see cref="StripColliders"/> walks the finished hierarchy and destroys
///      anything that still carries a collider, logging a warning if it ever finds one.
///
/// STATE FIDELITY. Since the 1:1-parity round (mod build 2) the owner broadcasts their live
/// BOARD-UI STATE (extras extension record 4, ~4 B at 5 Hz + on-change): which controls their
/// board currently shows (confirm/undo/use recess + cap/rest discs/skip/decision drawer) and the
/// wanted-slot glow mask. When that record is present it is AUTHORITATIVE — this board shows
/// exactly the controls the owner sees, in the same frames. A sender that predates the record
/// falls back to the previous behaviour (everything drawn, states derived from the
/// host-replicated <c>CPlayerActor</c> model and the already-synced VR extras). What remains
/// LOCAL-ONLY on the peer's client (button enabled-vs-disabled accents, live Confirm label
/// variants, their follow/pin preference) keeps the NEUTRAL / default look; each such case is
/// called out on the member that draws it. The modal PICK FIELD the flat board used to draw is
/// GONE — the local board removed its pick field outright, so a copy of it had become a picture
/// of a widget that no longer exists.
///
/// ANTI-CHEAT is unchanged: nothing here reads a card identity, and the two pieces that DO depend
/// on the peer's card state (the wanted-slot pulse and the half-card divider) derive strictly from
/// information the remote board already draws — see their notes.
///
/// COST. Built once, torn down with the board root, and refreshed on the shared
/// <see cref="RemoteBoardContent.RefreshSeconds"/> (4 Hz) cadence with change-gated writes. The
/// only per-frame work is the single <see cref="RemoteGlowPulse"/> component, and only while a
/// pulse is actually visible.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (DELIBERATELY-NOT + PER-ACTOR MODEL + VR-ONLY-derived) — and it
/// adds NO wire field of its own. The "NEUTRAL LOOKS" and "LOCAL-ONLY STATE" blocks called out on
/// individual members ARE the DELIBERATELY-NOT class: the peer's own button interactability, their
/// Confirm label, their drawer state and their personal tuning offsets are
/// knowable-but-not-worth-a-field, so peers are drawn at the AUTHORED defaults. The FOLLOW/PIN
/// toggle LEFT that class this round — its two-state label/accent is synced through the board-UI
/// record's byte 1 bit 2, at zero extra bytes. Slot occupancy
/// and the pile stacks are PER-ACTOR MODEL; the wanted-slot pulse, snap glow and half divider are
/// DERIVED from state the board already draws. The wire inputs are the already-synced
/// <see cref="RemoteAvatar"/> passed to <c>Refresh</c> and the board STYLE the ctor keys the
/// authored layout from — neither costs a new byte. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class RemoteBoardFurniture
{
    // ---------------------------------------------------------------- layout (board-local) --
    // FALLBACK-board constants: the LOCAL Oak board's authored BASE offsets, copied from PlayTray
    // so a peer's furniture sits where that player's own furniture sits when no real tray asset
    // (and therefore no prefab anchor) is available. With the real asset up, the caps seat on the
    // prefab anchors + the AUTHORED per-style offsets instead (see StyleOffsets below).

    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    /// <summary>PlayTray.ButtonZoneX — the right-hand control column.</summary>
    private const float ButtonZoneX = 0.235f;

    /// <summary>PlayTray "ContinueMount" (0.235, 0.045, −0.006) — the CONFIRM keycap / native
    /// Continue dock (fallback board).</summary>
    private static readonly Vector3 ConfirmMount = new(ButtonZoneX, 0.045f, -0.006f);

    /// <summary>PlayTray "UndoDockMount" (0.235, −0.06, −0.006) — the UNDO keycap / native Undo
    /// dock (fallback board).</summary>
    private static readonly Vector3 UndoMount = new(ButtonZoneX, -0.06f, -0.006f);

    // The VR-settings gear cap is gone from both boards: the mod's settings live in the game's own
    // options window now, so there is no local button for a remote board to mirror.

    /// <summary>PlayTray.PinBase (BoardW/2 − 0.045, −BoardH/2 − 0.030, −FixedProudZ) — FOLLOW/PIN.
    /// The local board uses this same board-local base for EVERY board style (plus the per-style
    /// PinOffset default, applied below).</summary>
    private static readonly Vector3 PinMount = new(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -0.005f);

    /// <summary>PlayTray.BuildHandle's bar (0, −BoardH/2 − 0.030, +0.004) — board-local on every
    /// style, like the local board's own handle. NOTE the local handle also carries a 62 %-wide
    /// trigger BoxCollider — the remote copy is the BAR ONLY, no zone, no <c>PanelGrabHandle</c>.</summary>
    private static readonly Vector3 HandleMount = new(0f, -BoardH * 0.5f - 0.030f, 0.004f);

    /// <summary>ButtonCluster's docked right-column anchor (ColumnCenterX/Y/RootZ).</summary>
    private static readonly Vector3 ClusterMount = new(0.148f, -0.124f, -0.006f);

    /// <summary>Mirror of <c>PlayTray.ButtonClusterMountScale</c> — the fixed 0.7× dock shrink
    /// every docked cluster button renders under (on TOP of the per-style
    /// <see cref="ClusterScaleFor"/>). Dropping it is half of why the remote skip cap rendered
    /// 43 % too big (task 3(b)).</summary>
    private const float ClusterDockScale = 0.7f;

    /// <summary>Mirror of <c>ButtonCluster.ClusterProudOffset</c> — the depth-correct proud seat
    /// the docked cluster is lifted toward the viewer, scaled by the dock factor.</summary>
    private const float ClusterProudLift = 0.010f;

    /// <summary>The SHIPPED [RoundButtons] group offset (<c>Defaults.RoundButtons_OffsetX/Y</c> +
    /// <c>Defaults.OffsetZ</c>) — the authored seat term <c>ButtonCluster.AttachDocked</c> adds to
    /// the column anchor in tray-root-local metres (X/Y straight, Z along the outward normal).
    /// The shipped values are NOT zero (build 34 rebased the tuned cfg into the defaults:
    /// x −0.045, y +0.26, z 0.025 — the skip disc lives UP-BOARD beside the card slots, not in
    /// the authored bottom-right column), and dropping them is the other half of task 3(b)'s
    /// "wrongly positioned round button". As everywhere on this board these are the AUTHORED
    /// defaults, never the peer's live [RoundButtons] tuning (DELIBERATELY-NOT).</summary>
    private static readonly Vector3 SkipSeatOffset = new(
        Defaults.RoundButtons_OffsetX, Defaults.RoundButtons_OffsetY, 0f);

    /// <summary>PlayTray.ItemUseSlotBase (ButtonZoneX, −BoardH/2 − 0.095, −0.020).</summary>
    private static readonly Vector3 ItemUseMount = new(ButtonZoneX, -BoardH * 0.5f - 0.095f, -0.020f);

    /// <summary>PlayTray.DecisionMountBase (0, −0.29, −0.020) — the shared decision drawer.</summary>
    private static readonly Vector3 DecisionMount = new(0f, -0.29f, -0.020f);

    /// <summary>Half height of the local grab-bar's trigger zone (<c>PlayTray.BuildHandle</c>
    /// box.size.y 0.05 / 2) — the bar-bottom reference the local decision dock hangs its widget
    /// block from.</summary>
    private const float HandleZoneHalfY = 0.025f;

    /// <summary>Mirror of <c>DecisionDockSurface.BarClearanceMeters</c> — the clearance between
    /// the grab-bar bottom and the prompt reference the owner's dock anchors under (linted
    /// against drift by <c>scripts/check-mirrors.sh</c>).</summary>
    private const float BarClearanceMeters = 0.008f;

    // ---- authored widget sizes (Defaults — the shipped [ButtonTuning] values) ------------------
    // The live ButtonTuning entries are the LOCAL player's own config; a peer's caps are drawn at
    // the AUTHORED defaults so every remote board looks the same regardless of local tuning.
    private const float BoardCapW = Defaults.BoardButtons_Width;
    private const float BoardCapH = Defaults.BoardButtons_Height;
    private const float BoardCapD = Defaults.BoardButtons_Depth;
    private const float PinCapW = Defaults.PinWidth;
    private const float DashCapH = Defaults.BoardDashboard_Height;
    private const float DashCapD = Defaults.BoardDashboard_Depth;
    private const float RestCapD = Defaults.RestButtons_Depth;
    private const float TransientCapR = Defaults.RoundButtons_CapSize; // cap RADIUS
    private const float TransientCapD = Defaults.RoundButtons_Depth;

    /// <summary>LEGACY slot metric (authored card width × the local board's 1.3 SlotScale) — the
    /// fallback the live fields below take when the owner's real sizes are not on the wire.
    /// Identical to <see cref="NetProtocol.SlotCardWidthLegacy"/> by construction.</summary>
    private const float CardW = 0.0635f * 1.3f;
    private const float CardH = CardW * (88f / 63.5f);

    /// <summary>The owner's live slot FRAME metric (their <c>CardWidth × SlotScale</c>, extension
    /// record 11) — what the wanted/snap glow rims are sized from, exactly like the local board's
    /// slot frames. Falls back to <see cref="CardW"/> for pre-record peers.</summary>
    private readonly float _slotFrameW;
    private readonly float _slotFrameH;

    /// <summary>The width a CARD parked in the owner's recess actually renders at (their
    /// <c>… × SlotCardFill</c>, extension record 11) — the half-poke divider spans THIS, because
    /// it marks the split on the rendered card, not on the recess.</summary>
    private readonly float _slotCardW;

    /// <summary>Authored card size for the item-use RECESS (the recess is card-sized at the
    /// UNSCALED card metric on the local board).</summary>
    private const float ItemCardW = 0.0635f;
    private const float ItemCardH = ItemCardW * (88f / 63.5f);

    // ---- palette (verbatim from the local widgets so the boards match) -------------------------
    private static readonly Color ConfirmColor = new(0.35f, 0.46f, 0.28f); // muted sage "go"
    private static readonly Color UndoColor = new(0.44f, 0.31f, 0.20f);    // worn leather
    /// <summary>PINNED (accented) FOLLOW/PIN cap — the <c>_accentColor</c> the local
    /// <c>PlayTray.BuildDashboardControls</c> hands its pin button (aged brass).</summary>
    private static readonly Color PinAccentColor = new(0.58f, 0.46f, 0.26f);

    /// <summary>FOLLOW (idle) FOLLOW/PIN cap — verbatim <c>PlayTray.BoardButton.IdleColor</c>, the
    /// warm parchment every ENABLED, un-accented keycap rests at. The remote cap used to be built
    /// in the ACCENT colour and left there, so a peer's board showed the PINNED look with the
    /// FOLLOW label permanently — half of defect (a).</summary>
    private static readonly Color PinIdleColor = new(0.60f, 0.51f, 0.35f);
    private static readonly Color SkipColor = new(0.37f, 0.44f, 0.56f);    // slate
    private static readonly Color HandleColor = new(0.62f, 0.50f, 0.28f);  // brass bar
    private static readonly Color ShortRestColor = new(0.62f, 0.52f, 0.30f); // parchment-gold
    private static readonly Color LongRestColor = new(0.37f, 0.44f, 0.56f);  // antique slate-blue

    // ---------------------------------------------------------------- authored per-style seats --
    // The SHIPPED per-board layout (Defaults.*_Oak/Steel/Bronze) keyed by the PEER's synced style.
    // These are the numeric defaults every client ships with — the per-board DESIGN, not anyone's
    // tuning — so applying them by wire style renders a Steel peer's confirm column where a Steel
    // board actually wears it (x +0.462 off the anchor!) instead of at the Oak spot.

    private static Vector3 ConfirmUndoOffsetFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.ConfirmUndoOffset_Steel,
        Cards.ControlBoard.Bronze => Defaults.ConfirmUndoOffset_Bronze,
        _ => Defaults.ConfirmUndoOffset_Oak,
    };

    private static float GenericSpacingFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.GenericButtonSpacing_Steel,
        Cards.ControlBoard.Bronze => Defaults.GenericButtonSpacing_Bronze,
        _ => Defaults.GenericButtonSpacing_Oak,
    };

    private static Vector3 RestOffsetFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.RestButtonOffset_Steel,
        Cards.ControlBoard.Bronze => Defaults.RestButtonOffset_Bronze,
        _ => Defaults.RestButtonOffset_Oak,
    };

    private static float RestSpacingFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.RestButtonSpacing_Steel,
        Cards.ControlBoard.Bronze => Defaults.RestButtonSpacing_Bronze,
        _ => Defaults.RestButtonSpacing_Oak,
    };

    private static float RestDiameterFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.RestButtonDiameter_Steel,
        Cards.ControlBoard.Bronze => Defaults.RestButtonDiameter_Bronze,
        _ => Defaults.RestButtonDiameter_Oak,
    };

    private static Vector3 PinOffsetFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.PinOffset_Steel,
        Cards.ControlBoard.Bronze => Defaults.PinOffset_Bronze,
        _ => Defaults.PinOffset_Oak,
    };

    private static Vector3 ItemUseOffsetFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.ItemUseSlotOffset_Steel,
        Cards.ControlBoard.Bronze => Defaults.ItemUseSlotOffset_Bronze,
        _ => Defaults.ItemUseSlotOffset_Oak,
    };

    /// <summary>Per-style ButtonCluster mount SCALE (<c>Defaults.ClusterScale_*</c>). Note the
    /// POSITION half of that pair (<c>ClusterOffset_*</c>) is deliberately NOT applied here: the
    /// local cluster's rigid dock (<c>ButtonCluster.AttachDocked</c>) reads the mount's ROTATION
    /// and SCALE only and seats the buttons at the fixed column constants — the mount's position
    /// never moves the rendered cluster, so mirroring it would move the copy where the original
    /// never goes (the previous revision's misplacement, task 3(b)).</summary>
    private static float ClusterScaleFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.ClusterScale_Steel,
        Cards.ControlBoard.Bronze => Defaults.ClusterScale_Bronze,
        _ => Defaults.ClusterScale_Oak,
    };

    private static Vector3 DecisionOffsetFor(Cards.ControlBoard s) => s switch
    {
        Cards.ControlBoard.Steel => Defaults.DecisionOffset_Steel,
        Cards.ControlBoard.Bronze => Defaults.DecisionOffset_Bronze,
        _ => Defaults.DecisionOffset_Oak,
    };

    /// <summary>
    /// THE SLOT-OVERLAY SEAT — defect (d) of this round ("die Kartenoverlays haben einen Versatz
    /// auf der X-Achse, sie werden zu weit links dargestellt").
    ///
    /// The LOCAL glows are built as CHILDREN of the slot transform at slot-local
    /// <c>(SlotOverlayOffset.x ± SlotOverlaySpacing/2, SlotOverlayOffset.y, base + …z)</c>
    /// (<c>PlayTray.BuildSlotHighlights</c> / <c>BuildWantedHighlights</c>, and a card parked in
    /// the recess takes the SAME offset through <c>SlotHomeOffsetFor</c> — that is the whole point
    /// of the debug menu's "Overlays" element: glow and resting card move together). The remote
    /// copies were seated on the bare recess ANCHOR and dropped that term entirely — the exact
    /// same class of omission <see cref="RemoteBoardLayout"/> was created to end for the docks.
    ///
    /// On Oak the shipped values are <c>(0.002, −0.002, 0.004)</c> with a −0.008 pair spacing, so
    /// slot 0's overlay belongs 6 mm to the RIGHT of the anchor and slot 1's 2 mm to the LEFT —
    /// before the slot's own 1.3× <c>SlotScale</c>, which multiplies both because the local quads
    /// hang under the scaled slot. That is the reported leftward shift, and it is asymmetric,
    /// which is why it reads as "off" rather than as a uniform nudge.
    ///
    /// Returned in BOARD-local metres (the frame the remote overlays live in): the authored
    /// slot-local values × <c>PlayTray.SlotScale</c>. The Z term is deliberately EXCLUDED — the
    /// remote glows already carry their own proud offsets relative to the card plane, and the
    /// authored z is the local build's equivalent of exactly that.
    ///
    /// As everywhere on this board, these are the SHIPPED per-board defaults keyed by the peer's
    /// SYNCED style, never that peer's private re-tuning (DELIBERATELY-NOT).
    /// </summary>
    private static Vector3 SlotOverlayLocal(Cards.ControlBoard s, int slot)
    {
        int i = (int)Cards.ControlBoards.Clamp((int)s);
        Vector3 ov = Cards.CardsConfig.BoardDefaults.SlotOverlayOffset[i];
        float spread = (slot == 0 ? -0.5f : 0.5f) * Cards.CardsConfig.BoardDefaults.SlotOverlaySpacing[i];
        return new Vector3(ov.x + spread, ov.y, 0f) * Cards.PlayTray.SlotScale;
    }

    // ---------------------------------------------------------------- built pieces --

    private readonly Transform _root;

    private readonly InertCap _confirm;
    private readonly InertCap _undo;
    private readonly InertCap _use;
    private readonly InertCap _pin;
    private readonly InertCap _skip;
    private readonly InertCap? _shortRest; // real-tray board only (needs the prefab rest anchors)
    private readonly InertCap? _longRest;

    private readonly Transform _itemUse;
    private readonly Material _itemUseGlowMat;
    private readonly Transform _decision;

    /// <summary>The board style this furniture was built for — keys the decision row's authored
    /// scale/gap when the synced content is rebuilt after construction.</summary>
    private Cards.ControlBoard _decisionStyle;

    /// <summary>The captioned IDLE drawer under <see cref="_decision"/> — shown while the owner
    /// has a docked prompt whose labels this client does not hold (legacy sender).</summary>
    private Transform? _drawerIdle;

    /// <summary>The SYNCED button row under <see cref="_decision"/> (wire record 12), rebuilt by
    /// <see cref="SetDecisionLines"/>; null while no labels are synced.</summary>
    private Transform? _decisionRow;

    /// <summary>Change gate for <see cref="SetDecisionLines"/> (the '\n'-joined labels last
    /// built; null = idle drawer).</summary>
    private string? _shownDecisionLines;

    /// <summary>Last synced CONFIRM wording applied to the cap (wire record 13; null = the
    /// neutral GUI_CONFIRM fallback is applied). Reset by <see cref="ApplyLabels"/> so a language
    /// switch re-derives the fallback without losing a live synced label.</summary>
    private string? _appliedConfirmWire;

    /// <summary>Last synced SKIP wording applied to the cap — same contract as
    /// <see cref="_appliedConfirmWire"/>.</summary>
    private string? _appliedSkipWire;
    private readonly GameObject?[] _wanted = new GameObject?[2];
    private readonly GameObject?[] _snap = new GameObject?[2];
    private readonly GameObject?[] _halves = new GameObject?[2];

    // ---- change gates (a TMP/material write per tick is exactly the churn the 4 Hz cadence is
    //      there to avoid; every setter below no-ops until the value really moves) --------------
    private bool _shownArmed;
    private int _shownWantedMask = -1;
    private int _shownSnapMask = -1;
    /// <summary>Last applied synced buttons mask (-2 = nothing applied yet, -1 = legacy sender).</summary>
    private int _shownButtonsMask = -2;
    /// <summary>Per-slot half-divider visibility as a bit mask (−1 = nothing written yet). A plain
    /// bool would miss the case where the dividers stay shown but the OCCUPANCY moves from one slot
    /// to the other.</summary>
    private int _shownHalfMask = -1;
    private string _langShown = string.Empty;

    /// <summary>Per-slot unscaled time the snap glow was lit (a card just landed there). Negative
    /// infinity = never.</summary>
    private readonly float[] _snapLitAt = { float.NegativeInfinity, float.NegativeInfinity };

    /// <summary>How long a snap glow stays lit after a card lands in a slot. Matches the "the card
    /// will zap here" telegraph duration the local board's gold glow is visible for around a drop.</summary>
    private const float SnapGlowSeconds = 0.6f;

    /// <summary>Round-card occupancy at the previous refresh — the edge detector that lights the
    /// snap glow (see <see cref="Refresh"/>).</summary>
    private readonly bool[] _wasFilled = new bool[2];

    /// <summary>One-line state summary for the change-gated diagnostic in
    /// <see cref="RemoteControlBoard.LogContent"/> (grep: "Remote board furniture").</summary>
    public string StateLine { get; private set; } = string.Empty;

    // ---------------------------------------------------------------- construction --

    /// <summary>
    /// Build the inert furniture for a board of <paramref name="style"/>. With a real
    /// <paramref name="tray"/>, caps seat on the prefab anchors + the authored per-style offsets
    /// (see the class note); without one, on the legacy flat-board constants.
    /// <paramref name="slot0CardLocal"/>/<paramref name="slot1CardLocal"/> are the board-local
    /// positions the two round CARDS render at — the slot overlays (wanted pulse / snap glow /
    /// half divider) centre on them so glow and card agree on every board style.
    /// </summary>
    public RemoteBoardFurniture(Transform boardRoot, Cards.ControlBoard style, RemoteTrayVisual? tray,
        Vector3 slot0CardLocal, Vector3 slot1CardLocal,
        float slotFrameWidth = 0f, float slotCardWidth = 0f)
    {
        // The owner's synced slot metrics (extension record 11); 0 = not on the wire, keep the
        // legacy constant — the exact size every build before the record drew.
        _slotFrameW = slotFrameWidth > 0f ? slotFrameWidth : CardW;
        _slotFrameH = _slotFrameW * (88f / 63.5f);
        _slotCardW = slotCardWidth > 0f ? slotCardWidth : CardW;

        _root = new GameObject("Furniture").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);

        // ---- right-hand control column: CONFIRM / [USE] / UNDO -------------------------------
        // Real tray: on the prefab's own ConfirmButton/UndoButton anchors + the authored per-style
        // offset + the authored generic spacing (± spacing/2 — GenericClusterY's 2-member layout),
        // exactly the seat PlayTray.BuildButtons gives the live keycaps. Fallback: the Oak mounts.
        Vector3 cuOff = ConfirmUndoOffsetFor(style);
        float cuSpacing = GenericSpacingFor(style);
        Transform confirmParent = tray?.ConfirmAnchor ?? _root;
        Transform undoParent = tray?.UndoAnchor ?? _root;
        Vector3 confirmPos = tray?.ConfirmAnchor != null
            ? cuOff + new Vector3(0f, cuSpacing * 0.5f, 0f)
            : ConfirmMount;
        Vector3 undoPos = tray?.UndoAnchor != null
            ? cuOff + new Vector3(0f, -cuSpacing * 0.5f, 0f)
            : UndoMount;
        _confirm = InertCap.Square(confirmParent, "Confirm", confirmPos,
            new Vector2(BoardCapW, BoardCapH), BoardCapD, ConfirmColor);
        _undo = InertCap.Square(undoParent, "Undo", undoPos,
            new Vector2(BoardCapW, BoardCapH), BoardCapD, UndoColor);

        // The item "USE" confirm is a DYNAMIC member of that same generic cluster (PlayTray
        // requirement 9a): while a usable item card is clipped into the use recess it joins as
        // member 1, i.e. the slot between CONFIRM (top) and UNDO (bottom). Drawn at the geometric
        // midpoint of the two caps (computed through world space so it is right on the real
        // anchors too), which is where a sanely tuned three-stack puts it.
        Vector3 useMidLocal = confirmParent.InverseTransformPoint(
            (_confirm.WorldPosition + _undo.WorldPosition) * 0.5f);
        _use = InertCap.Square(confirmParent, "ItemUse", useMidLocal,
            new Vector2(BoardCapW, BoardCapH), BoardCapD, ConfirmColor);
        // Starts hidden and in step with the _shownArmed seed below: the local cluster only holds
        // this member while an item decision is pending, and Refresh() early-outs while nothing
        // changed — so a board that never sees an item fan must not be left showing a USE cap.
        _use.SetShown(false);

        // ---- rest discs (real-tray board only — they seat in the prefab's rest notches) --------
        // The local board's short/long rest BoardButtons: round discs at the ShortRestToken /
        // LongRestToken anchors + the authored per-style offset ± spacing/2 (RestControls
        // EnsureBuilt/SetOffset). NEUTRAL LOOK: drawn at their authored accent colours; whether
        // the peer has actually selected a rest is a separate readout
        // (RemoteStatusReadouts.RestText), not a cap state.
        if (tray?.ShortRestAnchor != null && tray.LongRestAnchor != null)
        {
            Vector3 restOff = RestOffsetFor(style);
            float restSpacing = RestSpacingFor(style);
            float restD = RestDiameterFor(style);
            _shortRest = InertCap.Round(tray.ShortRestAnchor, "ShortRest",
                restOff + new Vector3(0f, restSpacing * 0.5f, 0f), restD, RestCapD, ShortRestColor);
            _longRest = InertCap.Round(tray.LongRestAnchor, "LongRest",
                restOff + new Vector3(0f, -restSpacing * 0.5f, 0f), restD, RestCapD, LongRestColor);
        }

        // FOLLOW/PIN toggle: built in the FOLLOW (idle) look, then driven from the owner's synced
        // state every refresh (SetPinned) — label AND cap colour, exactly like their own cap.
        _pin = InertCap.Square(_root, "FollowToggle", PinMount + PinOffsetFor(style),
            new Vector2(PinCapW, DashCapH), DashCapD, PinIdleColor);

        // ---- grab-handle bar -------------------------------------------------------------------
        // The local handle is a brass Cube PLUS a 62 %-wide trigger BoxCollider and a
        // WorldUI.PanelGrabHandle that carries/rotates/resizes the board. The remote copy is the
        // 3D BAR (the same cube the local board renders): no collider, no grab zone, no handle
        // component. A peer's board can never be picked up — it follows the pose THEY broadcast
        // and nothing else.
        var handle = new GameObject("HandleBar").transform;
        handle.SetParent(_root, worldPositionStays: false);
        handle.localPosition = HandleMount;
        LitCube(handle, "Bar", new Vector3(BoardW * 0.55f, 0.024f, 0.024f), HandleColor);

        // ---- turn-flow ButtonCluster ----------------------------------------------------------
        // FIDELITY NOTE (this is why only ONE cap is drawn, not three): on a DOCKED board the
        // cluster's Ready and Undo twins are forced permanently OFF — ButtonCluster.Tick calls
        // MirrorReady(null, …) / MirrorUndo(null, …) precisely so the board never shows a duplicate
        // "Fortfahren"/Undo next to the right-hand pads. ONLY Skip is mirrored there.
        //
        // SEAT + SHAPE + SIZE are the local rigid dock's, reproduced term for term (task 3(b) —
        // "der runde Button ist falsch positioniert und ignoriert die Offsets des Boards"): the
        // column anchor + the SHIPPED [RoundButtons] group offset (x −0.045, y +0.26 — up-board
        // beside the card slots, where the owner actually sees it), lifted along −Z by the proud
        // seat + the authored OffsetZ, at the 0.7× dock shrink × the per-style cluster scale, in
        // the AUTHORED shape (the shipped default is a SQUARE keycap of the [RoundButtons] W/H/D,
        // not a round disc). The previous revision drew an unscaled round disc at the bare column
        // anchor — wrong spot, wrong shape, 1.43× too big.
        float clusterScale = ClusterDockScale * ClusterScaleFor(style);
        Vector3 skipSeat = ClusterMount + SkipSeatOffset
                           + new Vector3(0f, 0f, -(ClusterProudLift * clusterScale + Defaults.OffsetZ));
        _skip = Defaults.RoundButtons_Shape == Cards.ButtonShape.Round
            ? InertCap.Round(_root, "TurnFlowSkip", skipSeat,
                TransientCapR * 2f * clusterScale, TransientCapD * clusterScale, SkipColor)
            : InertCap.Square(_root, "TurnFlowSkip", skipSeat,
                new Vector2(Defaults.RoundButtons_Width, Defaults.RoundButtons_Height) * clusterScale,
                Defaults.RoundButtons_Depth * clusterScale, SkipColor);

        // ---- item-USE clip-in recess ----------------------------------------------------------
        _itemUse = BuildItemUseRecess(ItemUseMount + ItemUseOffsetFor(style), out _itemUseGlowMat);

        // ---- shared decision drawer -----------------------------------------------------------
        // ANCHORED WHERE THE OWNER'S DOCK REALLY HANGS (task 2 — the detached "ENTSCHEIDUNGEN"
        // plate): the local DecisionDockSurface does NOT place its widget block at the decision
        // MOUNT's y — it anchors the block TOP a configured gap below the grab-bar BOTTOM
        // (Place(): promptRef = bar bottom − BarClearance; block top = promptRef − DecisionGap;
        // the mount's own Y cancels out of the solve). The old drawer sat at the RAW mount seat
        // (y −0.29 − 0.157 = −0.447 on Steel) — 0.18 m below where the owner's buttons actually
        // are. The mirror now derives the same top edge from the same references: the handle
        // bar's authored seat, its zone half-height, the shared clearance and the AUTHORED
        // per-board DecisionGap. The mount contributes only its authored X/Z (sideways + proud),
        // exactly as it does locally.
        _decisionStyle = style;
        Vector3 decisionOff = DecisionOffsetFor(style);
        float barBottomY = HandleMount.y - HandleZoneHalfY;
        float decisionTopY = barBottomY - BarClearanceMeters - DecisionGapFor(style);
        _decision = BuildDecisionDrawer(new Vector3(
            DecisionMount.x + decisionOff.x, decisionTopY, DecisionMount.z + decisionOff.z));

        // ---- slot overlays: wanted pulse, snap glow, half-poke divider ------------------------
        // Centred on the CARD positions handed in by the board (the real recess anchors when the
        // 3D asset is up) PLUS the authored per-board SLOT-OVERLAY seat — see SlotOverlayLocal for
        // why dropping that term is what pushed every overlay off-centre inside the recess.
        for (int i = 0; i < 2; i++)
        {
            Vector3 card = (i == 0 ? slot0CardLocal : slot1CardLocal) + SlotOverlayLocal(style, i);
            _wanted[i] = BuildSlotGlow($"WantedGlow{i}", card, 1.36f, -0.003f,
                new Color(0.25f, 0.85f, 0.60f, 0.70f), pulse: true);
            _snap[i] = BuildSlotGlow($"SnapGlow{i}", card, 1.24f, -0.005f,
                new Color(1f, 0.85f, 0.30f, 0.95f), pulse: false);
            _halves[i] = BuildHalfDivider($"HalfDivider{i}", card);
        }

        ApplyLabels();
        StripColliders(_root.gameObject, "RemoteBoardFurniture");
    }

    // ---------------------------------------------------------------- refresh --

    /// <summary>
    /// Re-read everything knowable and repaint what changed. Called on the shared 4 Hz content
    /// cadence from <see cref="RemoteControlBoard"/> — with or without an actor (the synced
    /// board-UI state below is wire-fed, so an actorless peer's board still mirrors its owner's
    /// controls; only the slot-occupancy-derived pieces need the actor-fed slot flags).
    ///
    /// <paramref name="showFronts"/> is the shared <see cref="RevealGate"/> answer for this actor;
    /// <paramref name="slotMask"/> says which of that peer's card slots currently hold a card and
    /// <paramref name="faceMask"/> which of those are drawn FACE-UP (a strict subset). Both are the
    /// masks <c>RemoteControlBoard.SeatSlots</c> already resolved for the slots themselves —
    /// handed down rather than re-derived, so the glows can never disagree with the cards, and
    /// nothing derived from them can leak anything the board does not already show.
    ///
    /// WHY THE FACE MASK IS SEPARATE. Since the owner's recess occupancy rides the wire, a slot can
    /// legitimately show a card whose IDENTITY this client does not have (an anonymous back — see
    /// <see cref="RemoteBoardCard.SetAnonymousBack"/>). The half-card divider marks where a face-up
    /// card's two action halves split, so it must follow the faces, not the occupancy; one flag for
    /// both would draw a divider across a card back.
    /// </summary>
    public void Refresh(CPlayerActor? actor, RemoteAvatar owner, bool showFronts,
        int slotMask, int faceMask)
    {
        bool slot0 = (slotMask & 1) != 0;
        bool slot1 = (slotMask & 2) != 0;
        // A language switch invalidates every cached label (the local board self-heals the same way).
        string lang = Loc.CurrentLanguage;
        if (lang != _langShown)
        {
            _langShown = lang;
            ApplyLabels();
        }

        // ---- SYNCED BOARD-UI (extension record 4 — defect 5 "genau die Buttons, die der
        //      Besitzer sieht"). When the owner broadcasts their live control visibility, the
        //      caps mirror it EXACTLY: confirm/undo/use/rest/skip appear and disappear on this
        //      board in the same frames they do on the owner's (5 Hz + on-change). A sender that
        //      predates the record (HasBoardUi false) gets the legacy always-drawn furniture, so
        //      nothing regresses cross-version.
        bool synced = owner.HasBoardUi;
        int buttons = synced ? owner.BoardButtonsMask : -1;
        if (buttons != _shownButtonsMask)
        {
            _shownButtonsMask = buttons;
            if (synced)
            {
                _confirm.SetShown((buttons & NetProtocol.BoardUiConfirmBit) != 0);
                _undo.SetShown((buttons & NetProtocol.BoardUiUndoBit) != 0);
                _shortRest?.SetShown((buttons & NetProtocol.BoardUiShortRestBit) != 0);
                _longRest?.SetShown((buttons & NetProtocol.BoardUiLongRestBit) != 0);
                _skip.SetShown((buttons & NetProtocol.BoardUiSkipBit) != 0);
                SetShown(_itemUse, (buttons & NetProtocol.BoardUiItemRecessBit) != 0);
                // The decision drawer: drawn only while a prompt is actually docked on the
                // owner's board — an idle local board shows nothing at that mount.
                SetShown(_decision, (buttons & NetProtocol.BoardUiDecisionBit) != 0);
            }
            else
            {
                // Legacy sender: the pre-record look (everything drawn, drawer always out).
                _confirm.SetShown(true);
                _undo.SetShown(true);
                _shortRest?.SetShown(true);
                _longRest?.SetShown(true);
                _skip.SetShown(true);
                SetShown(_itemUse, true);
                SetShown(_decision, true);
            }
        }

        // ---- SYNCED CAP LABELS (wire record 13 — task 4 "der Button-Text muss immer korrekt
        //      synchronisiert sein"). The owner's actually-displayed CONFIRM/SKIP wording, shown
        //      verbatim in their language; absent record = the neutral ApplyLabels fallback.
        SetCapLabels(owner);

        // ---- SYNCED DECISION ROW (wire record 12 — task 2 "die Entscheidungsbuttons 1:1").
        //      The labels of the row the owner's dock really shows, rendered as inert plates at
        //      the same bar-anchored seat; without labels the captioned idle drawer stands in.
        SetDecisionLines(owner.DecisionLines);

        // ---- FOLLOW / PIN toggle (defect (a)) -------------------------------------------------
        // The owner's tray anchor mode now rides the board-UI record (byte 1 bit 2), so this cap
        // shows their ACTUAL state instead of one fixed look: "FIXIERT"/"PINNED" on the accented
        // brass cap while their board is world-anchored, "FOLGEN"/"FOLLOW" on the parchment idle
        // cap while it follows their rig — the same label/colour pair their own BoardButton wears
        // (PlayTray: SetLabel(follow/pinned) + SetState(accent: !TrayFollow)). A sender that
        // predates the bit reads as FOLLOW, which is the look every previous build already drew.
        SetPinned(owner.TrayPinned);

        // ---- item-use USE cap + recess ARMED look ---------------------------------------------
        // SYNCED: the USE cap is its own wire bit (it exists on the owner's board only while a
        // card is clipped into the recess), and the recess glows armed exactly then. LEGACY
        // (pre-record sender): the old knowable proxy — armed while their item fan is open.
        bool armed = synced
            ? (buttons & NetProtocol.BoardUiItemUseCapBit) != 0
            : owner.ItemCardCount > 0;
        if (armed != _shownArmed)
        {
            _shownArmed = armed;
            // Additive/alpha glow rim: bright while armed, a dim outline while idle.
            _itemUseGlowMat.color = armed
                ? new Color(1f, 0.82f, 0.35f, 0.80f)
                : new Color(0.30f, 0.25f, 0.12f, 0.35f);
            _use.SetShown(armed);
        }

        // ---- wanted-slot pulse ----------------------------------------------------------------
        // SYNCED (defect 4 "das Blinken soll synchron sein"): the owner's live wanted-glow mask
        // rides the board-UI record, so the teal rim pulses on exactly the slots the owner's own
        // board pulses — including every local gate (pick flows, short-rest choice, overlay gate)
        // this board could never re-derive. The blink ANIMATION stays on the local clock at the
        // shared 3.2 rad/s period (RemoteGlowPulse == PlayTray.SlotPulse): synced state, locally
        // animated, zero per-frame traffic.
        //
        // LEGACY senders keep the old derivation: "slot empty during the secret selection phase".
        //
        // ANTI-CHEAT: this reveals nothing, on two independent grounds. (a) It is the strict
        // COMPLEMENT of what this very board already draws — an occupied slot already shows a card
        // BACK during selection, so "empty" was already visible. (b) VANILLA BROADCASTS THE SAME
        // FACT ANYWAY, twice over: the multiplayer ready tracker shows a per-character ready marker
        // for the whole selection phase (UIScenarioMultiplayerController.ShowReadyTracker →
        // UIReadyTrackerBar.RefreshReady → UIReadyTracker.ShowReady), and the hand tabs print every
        // player's live "selected/2" count with no IsUnderMyControl gate
        // (CardsHandManager.OnSelectedCardsNumberChanged ← the bolt StartRoundCards replication).
        // No card IDENTITY is involved here and the synced mask carries none either.
        int wantedMask;
        if (synced)
        {
            wantedMask = owner.WantedGlowMask;
        }
        else
        {
            bool selecting = RevealGate.InScenario && RevealGate.IsSecretSelectionPhase;
            wantedMask = 0;
            if (selecting)
            {
                if (!slot0) wantedMask |= 1;
                if (!slot1) wantedMask |= 2;
            }
        }
        SetWanted(wantedMask);

        // ---- snap glow ------------------------------------------------------------------------
        // The local gold snap glow is a HOVER telegraph ("the held card lands here on release") and
        // the hovering hand is local-only. The moment it is actually FOR, though — a card arriving
        // in a slot — is perfectly knowable: the model transition empty→occupied. So the remote
        // glow lights on that edge and fades after SnapGlowSeconds, which reproduces the flash the
        // peer saw at the instant of their own drop. A remote hover is not reproduced (and cannot be).
        int snapMask = 0;
        for (int i = 0; i < 2; i++)
        {
            bool filled = i == 0 ? slot0 : slot1;
            if (filled && !_wasFilled[i])
                _snapLitAt[i] = Time.unscaledTime;
            _wasFilled[i] = filled;
            if (Time.unscaledTime - _snapLitAt[i] < SnapGlowSeconds)
                snapMask |= 1 << i;
        }
        SetSnap(snapMask);

        // ---- HalfSelection half-poke zones ----------------------------------------------------
        // The LOCAL half zones are deliberately INVISIBLE (HalfSelection: "the mod zones ... stay
        // INVISIBLE — the game's own on-card highlight is the only hover/selection feedback"), so a
        // literally faithful copy would draw nothing at all and the element would be missing from
        // the peer's board. The compromise: a hairline divider across the middle of each face-up
        // round card, marking where the two action halves split. Inert, one quad per slot, shown
        // exactly when the local zones are armed — i.e. while the cards are face-up in the action
        // phase, never during the secret selection phase.
        // The face mask is already the intersection of "a card is drawn here" with "it is drawn
        // face-up", so the reveal gate is applied once, upstream, instead of twice with two
        // different occupancy notions. Re-ANDed with showFronts purely as a belt-and-braces read of
        // the same gate this method is handed.
        int halfMask = showFronts ? faceMask & 0x3 : 0;
        SetHalves(halfMask);

        StateLine = $"use={(armed ? "armed" : "idle")}, " +
                    $"buttons={(synced ? "0x" + owner.BoardButtonsMask.ToString("X2") : "legacy")}, " +
                    $"wanted={wantedMask}{(synced ? "(synced)" : string.Empty)}, snap={snapMask}, " +
                    $"halves={halfMask}, " +
                    $"tray={(owner.TrayPinned ? "PINNED" : "FOLLOW")}{(synced ? "(synced)" : "(default)")}, " +
                    $"capLabels[confirm={(owner.ConfirmCapLabel != null ? "'" + owner.ConfirmCapLabel + "'" : "neutral")}, " +
                    $"skip={(owner.SkipCapLabel != null ? "'" + owner.SkipCapLabel + "'" : "neutral")}], " +
                    $"decision={(_shownDecisionLines != null ? _shownDecisionLines.Split('\n').Length + " synced button(s)" : "drawer")}";
        _ = actor; // reserved: no per-actor furniture state is knowable beyond the slots (see notes)
    }

    /// <summary>Last applied FOLLOW/PIN state (null = nothing written yet, so the first refresh
    /// always states it). Change-gated because both writes it drives — a TMP label and three
    /// material colours — are exactly the per-tick churn the 4 Hz cadence exists to avoid.</summary>
    private bool? _shownPinned;

    /// <summary>Apply the owner's tray anchor mode to the inert FOLLOW/PIN cap: the local board's
    /// own label pair (<c>Loc.Mod("follow")</c> / <c>Loc.Mod("pinned")</c>) and its own colour pair
    /// (idle parchment / accent brass). See the call site in <see cref="Refresh"/>.</summary>
    private void SetPinned(bool pinned)
    {
        if (_shownPinned == pinned)
            return;
        _shownPinned = pinned;
        _pin.SetLabel(pinned ? Loc.Mod("pinned") : Loc.Mod("follow"));
        _pin.SetTint(pinned ? PinAccentColor : PinIdleColor);
    }

    /// <summary>
    /// Apply the owner's SYNCED cap wordings (wire record 13): the confirm cap and the skip cap
    /// read EXACTLY what the owner's do, verbatim in their language; a null (record absent —
    /// control hidden, or a pre-record sender) falls back to the neutral localized seed. Both
    /// writes are change-gated against the WIRE value, and <see cref="ApplyLabels"/> re-arms the
    /// gates on a language switch so the fallback re-localizes without clobbering a live label.
    /// </summary>
    private void SetCapLabels(RemoteAvatar owner)
    {
        string? confirm = owner.ConfirmCapLabel;
        if (confirm != _appliedConfirmWire)
        {
            _appliedConfirmWire = confirm;
            _confirm.SetLabel(confirm ?? Loc.Game("GUI_CONFIRM", "Confirm"));
        }
        string? skip = owner.SkipCapLabel;
        if (skip != _appliedSkipWire)
        {
            _appliedSkipWire = skip;
            _skip.SetLabel(skip ?? Loc.Game("GUI_SKIP_MOVEMENT", "Skip"));
        }
    }

    /// <summary>Change-safe activeSelf flip for a plain furniture root.</summary>
    private static void SetShown(Transform root, bool shown)
    {
        if (root != null && root.gameObject.activeSelf != shown)
            root.gameObject.SetActive(shown);
    }

    // ---------------------------------------------------------------- labels --

    /// <summary>
    /// (Re)write every cap label in the current language. All literals go through the game's own
    /// loc keys where one exists, so a peer's board reads in the local player's language exactly
    /// like their own board does.
    ///
    /// NEUTRAL LOOKS declared here, once, because none of these states cross the wire and none is
    /// worth a wire field:
    ///   • CONFIRM / UNDO / SKIP / REST enabled-vs-disabled — the local caps mirror the peer's OWN
    ///     uGUI widget interactability, which is recomputed per frame on THEIR client only.
    ///     Drawn ENABLED (the authored base colour), never dimmed.
    ///
    /// The FOLLOW/PIN toggle is NO LONGER on that list: its label and accent are SYNCED (board-UI
    /// record byte 1 bit 2) and applied in <see cref="SetPinned"/>, so this method only seeds the
    /// wording. Calling <c>[Cards] TrayFollow</c> "a private VR preference" was the mistake — it is
    /// a labelled two-state control on a board the user requires to read 1:1 like its owner's.
    /// The CONFIRM and SKIP wordings left the list the same way (user report 2026-08-04: "mein
    /// Mitspieler las 'Fortfahren', ich sehe 'Bestätigen'"): the owner's actually-displayed text
    /// rides wire record 13 and is applied in <see cref="SetCapLabels"/> — this method only seeds
    /// the no-record fallback.
    /// </summary>
    private void ApplyLabels()
    {
        _confirm.SetLabel(Loc.Game("GUI_CONFIRM", "Confirm"));
        _undo.SetLabel(Loc.Game("GUI_UNDO", "Undo"));
        // The CONFIRM/SKIP wordings are SYNCED state now (wire record 13, see SetCapLabels):
        // this method only seeds the neutral fallback, and re-arming the gates here makes the
        // next refresh re-assert whichever synced label is live in place of the reseed.
        _appliedConfirmWire = null;
        _appliedSkipWire = null;
        _use.SetLabel(Loc.Game("GUI_USE", "USE").ToUpperInvariant());
        // FOLLOW/PIN is SYNCED state now (see SetPinned), so a language switch must re-state the
        // CURRENT mode's word, not the FOLLOW one — and must re-arm the change gate so the next
        // refresh re-applies it in the new language.
        _pin.SetLabel(_shownPinned == true ? Loc.Mod("pinned") : Loc.Mod("follow"));
        _shownPinned = null;
        // GUI_SKIP_MOVEMENT is the key SkipButton.Start() seeds its own label from; the live
        // button swaps in GUI_SKIP_ABILITY / GUI_SKIP_ATTACK per situation — that live wording
        // rides wire record 13 now (SetCapLabels overrides this seed whenever it is present).
        _skip.SetLabel(Loc.Game("GUI_SKIP_MOVEMENT", "Skip"));
        // Same strings the local RestControls caps wear (no game key exists for the short rest).
        _shortRest?.SetLabel(Loc.Mod("short_rest"));
        _longRest?.SetLabel(Loc.Game("GUI_LONG_REST", "Long rest"));
    }

    // ---------------------------------------------------------------- sub-builders --

    /// <summary>
    /// The item-USE clip-in recess: gold frame + dark inner + glow rim + the localized "USE"
    /// caption BELOW the recess — the exact composition (and the exact 1.12 / 1.04 / 1.28 card-size
    /// multipliers, and the caption's one-half-card drop) of <c>PlayTray.BuildItemUseSlot</c>, minus
    /// the pulse driver and minus anything droppable.
    /// </summary>
    private Transform BuildItemUseRecess(Vector3 mount, out Material glowMat)
    {
        var root = new GameObject("ItemUseRecess").transform;
        root.SetParent(_root, worldPositionStays: false);
        root.localPosition = mount;

        glowMat = BoardVisual.Unlit(new Color(0.30f, 0.25f, 0.12f, 0.35f));
        BoardVisual.Quad(root, "Glow", new Vector2(ItemCardW * 1.28f, ItemCardH * 1.28f), glowMat)
            .transform.localPosition = new Vector3(0f, 0f, 0.0005f);
        BoardVisual.Quad(root, "Frame", new Vector2(ItemCardW * 1.12f, ItemCardH * 1.12f),
            BoardVisual.Unlit(new Color(0.55f, 0.45f, 0.22f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        BoardVisual.Quad(root, "FrameInner", new Vector2(ItemCardW * 1.04f, ItemCardH * 1.04f),
            BoardVisual.Unlit(new Color(0.12f, 0.10f, 0.08f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.0005f);

        TextMeshPro caption = RemoteBoardContent.Label(root, "Label",
            new Vector3(0f, -(ItemCardH * 0.5f + 0.026f), -0.001f),
            new Vector2(ItemCardW * 1.1f, 0.030f), 0.05f,
            new Color(1f, 0.92f, 0.72f), TextAlignmentOptions.Center, FontStyles.Bold);
        caption.text = Loc.Game("GUI_USE", "USE").ToUpperInvariant();
        // MR readability parity with the owner's board: the local item-use caption below the
        // recess is MrBacking.Label'd (PlayTray.4.Slots), because it hangs below the recess in
        // open air — over the passthrough room in MR. Same treatment for its mirror; the fitted
        // plate renders only while MR is on, so normal mode stays bit-identical.
        WorldUI.MrBacking.Label(caption);
        return root;
    }

    /// <summary>
    /// The SHARED DECISION DOCK mirror: the strip below the board where the take-damage burn
    /// choice, the burn-confirm dialog and every other in-scenario prompt dock their REAL widgets
    /// on the local board (<c>DecisionDockSurface</c> on <c>PlayTray.DecisionMount</c>).
    ///
    /// The root's local origin is the WIDGET-BLOCK TOP EDGE the owner's dock anchors at (bar
    /// bottom − clearance − authored DecisionGap; computed at the ctor call site), horizontally
    /// centred like the local dock; content grows DOWN from it. Two mutually exclusive children:
    ///   • <see cref="_drawerIdle"/> — the slim captioned drawer, shown while the owner HAS a
    ///     docked prompt (board-UI decision bit) but this client holds no labels for it (legacy
    ///     sender, or a row whose labels could not be read);
    ///   • <see cref="_decisionRow"/> — the MIRRORED BUTTON ROW (wire record 12): one inert
    ///     antique plate per label the owner's dock really shows, rebuilt in
    ///     <see cref="SetDecisionLines"/>.
    /// </summary>
    private Transform BuildDecisionDrawer(Vector3 topEdgeLocal)
    {
        var root = new GameObject("DecisionDrawer").transform;
        root.SetParent(_root, worldPositionStays: false);
        root.localPosition = topEdgeLocal;

        // Idle drawer, hung with its TOP edge at the root origin (the same anchor the real
        // buttons use, so legacy and synced looks sit at the same spot).
        _drawerIdle = new GameObject("Idle").transform;
        _drawerIdle.SetParent(root, worldPositionStays: false);
        _drawerIdle.localPosition = new Vector3(0f, -0.0275f, 0f);

        MeshRenderer drawerPlate = BoardVisual.Quad(_drawerIdle, "Plate", new Vector2(0.42f, 0.055f),
            BoardVisual.Unlit(new Color(0.10f, 0.09f, 0.08f, 0.80f)));
        drawerPlate.transform.localPosition = new Vector3(0f, 0f, 0.001f);
        WorldUI.MrBacking.Opacify(drawerPlate.sharedMaterial); // 0.80 → 1 while MR is on
        // A thin lip along the top edge so the empty drawer reads as a drawer and not as a shadow.
        BoardVisual.Quad(_drawerIdle, "Lip", new Vector2(0.42f, 0.004f),
            BoardVisual.Unlit(new Color(0.36f, 0.31f, 0.20f, 1f)))
            .transform.localPosition = new Vector3(0f, 0.0275f, 0f);

        RemoteBoardContent.Label(_drawerIdle, "Caption", new Vector3(0f, 0f, -0.001f),
            new Vector2(0.36f, 0.026f), 0.05f,
            new Color(0.72f, 0.68f, 0.58f), TextAlignmentOptions.Center)
            .text = Loc.Mod("decision_dock").ToUpperInvariant();
        return root;
    }

    /// <summary>Authored per-board decision text↔button gap (<c>Defaults.DecisionGap_*</c> — the
    /// board-local metres the owner's widget-block top hangs below the prompt reference).</summary>
    private static float DecisionGapFor(Cards.ControlBoard s) =>
        Cards.CardsConfig.BoardDefaults.DecisionGap[(int)Cards.ControlBoards.Clamp((int)s)];

    /// <summary>Authored per-board decision dock SCALE (<c>Defaults.DecisionScale_*</c>, 1.6 on
    /// every shipped board — the "readable under pressure" enlargement the local mount carries).</summary>
    private static float DecisionScaleFor(Cards.ControlBoard s) =>
        Defaults.DecisionScale_ByBoard[(int)Cards.ControlBoards.Clamp((int)s)];

    // ---- decision-row geometry (all board-local metres, scaled by DecisionScaleFor) ----------
    // The local row is the game's own widget row fitted into PlayTray.DecisionMountWidth ×
    // DecisionMountMaxHeight at the mount's 1.6× scale; the mirror reproduces that envelope with
    // one plate per synced label — content-true widths inside the same budget.

    /// <summary>Plate height at scale 1 (a game option button's ~90 px at the dock density).</summary>
    private const float DecisionButtonH = 0.048f;

    /// <summary>Gap between two plates, board-local metres (at scale 1).</summary>
    private const float DecisionButtonGap = 0.008f;

    /// <summary>Per-plate width ceiling at scale 1 — a lone confirm button must not stretch
    /// across the whole 0.42 budget the way an equal split would.</summary>
    private const float DecisionButtonMaxW = 0.20f;

    /// <summary>
    /// (Re)build the mirrored decision-button row from the owner's synced labels (wire record
    /// 12; null = none). Change-gated on the joined string — a rebuild is a handful of quads and
    /// labels, and it only happens when the owner's dock content really changed. The idle
    /// captioned drawer shows exactly while the decision bit is set WITHOUT labels, so a legacy
    /// sender keeps its familiar look.
    /// </summary>
    private void SetDecisionLines(string? lines)
    {
        if (lines == _shownDecisionLines)
            return;
        _shownDecisionLines = lines;

        if (_decisionRow != null)
        {
            Object.Destroy(_decisionRow.gameObject);
            _decisionRow = null;
        }
        if (_drawerIdle != null && _drawerIdle.gameObject.activeSelf != (lines == null))
            _drawerIdle.gameObject.SetActive(lines == null);
        if (lines == null)
            return;

        float scale = DecisionScaleFor(_decisionStyle);
        string[] labels = lines.Split('\n');
        int n = labels.Length;

        _decisionRow = new GameObject("SyncedRow").transform;
        _decisionRow.SetParent(_decision, worldPositionStays: false);
        _decisionRow.localPosition = Vector3.zero;

        // One horizontal row, centred on the dock axis like the game's own option rows: equal
        // slots inside the owner's width budget, each plate capped at the content ceiling.
        float budget = Cards.PlayTray.DecisionMountWidth * scale;
        float gap = DecisionButtonGap * scale;
        float h = DecisionButtonH * scale;
        float w = Mathf.Min(DecisionButtonMaxW * scale, (budget - (n - 1) * gap) / n);
        float rowW = n * w + (n - 1) * gap;
        for (int i = 0; i < n; i++)
        {
            var plate = new GameObject($"Button{i}").transform;
            plate.SetParent(_decisionRow, worldPositionStays: false);
            plate.localPosition = new Vector3(-rowW * 0.5f + w * 0.5f + i * (w + gap),
                                              -h * 0.5f, 0f);
            // The local docked row's antique restyle, mirrored: the game's light-stone button
            // sprite multiplied toward dark wood/aged brass (DecisionDockSurface.AntiqueTint)
            // with parchment-gold labels — a gold rim + dark body reads as exactly that family.
            BoardVisual.Quad(plate, "Rim", new Vector2(w, h),
                BoardVisual.Unlit(new Color(0.55f, 0.45f, 0.22f, 1f)))
                .transform.localPosition = new Vector3(0f, 0f, 0.0015f);
            BoardVisual.Quad(plate, "Face", new Vector2(w - 0.006f * scale, h - 0.006f * scale),
                BoardVisual.Unlit(new Color(0.23f, 0.18f, 0.12f, 1f)))
                .transform.localPosition = new Vector3(0f, 0f, 0.001f);
            Color gold = WorldUI.NativeButtonSkin.HasFont
                ? WorldUI.NativeButtonSkin.LabelColor
                : new Color(0.91f, 0.82f, 0.62f);
            RemoteBoardContent.Label(plate, "Label", new Vector3(0f, 0f, -0.001f),
                new Vector2(w * 0.9f, h * 0.72f), 0.23f * scale, gold,
                TextAlignmentOptions.Center, wrap: true)
                .text = labels[i];
        }
        StripColliders(_decisionRow.gameObject, "RemoteBoardFurniture.SyncedRow");

        VRLog.Info("Net", $"Remote decision dock: {n} mirrored button(s) " +
                          $"(\"{lines.Replace('\n', '|')}\") — row {rowW:F3} m wide, plates " +
                          $"{w:F3}x{h:F3} m at the authored ×{scale:F2} dock scale, top edge at " +
                          $"board-local y {_decision.localPosition.y:F3} (bar bottom − clearance − " +
                          $"authored DecisionGap_{_decisionStyle}) — the seat the OWNER's own dock " +
                          "hangs its widget block from.");
    }

    /// <summary>A collider-free glow rim behind a round-card slot (the teal "wanted" pulse and the
    /// gold snap flash share this shape, exactly as on the local board — different hue, different
    /// rim size, the teal a hair less proud so the gold always draws in front of it). Sized to the
    /// remote card metric so the rim frames the rendered card.</summary>
    private GameObject BuildSlotGlow(string name, Vector3 cardLocal, float scale, float proud,
        Color color, bool pulse)
    {
        var mat = BoardVisual.Unlit(color);
        MeshRenderer mr = BoardVisual.Quad(_root, name,
            new Vector2(_slotFrameW * scale, _slotFrameH * scale), mat);
        mr.transform.localPosition = new Vector3(cardLocal.x, cardLocal.y, cardLocal.z + proud);
        if (pulse)
            mr.gameObject.AddComponent<RemoteGlowPulse>().Init(mat, color);
        mr.gameObject.SetActive(false);
        return mr.gameObject;
    }

    /// <summary>The hairline that marks the top/bottom action-half split on a face-up round card —
    /// the only way to depict the deliberately invisible <c>HalfSelection</c> poke zones at all
    /// (see <see cref="Refresh"/>). A single inert quad; nothing to poke.</summary>
    private GameObject BuildHalfDivider(string name, Vector3 cardLocal)
    {
        MeshRenderer mr = BoardVisual.Quad(_root, name, new Vector2(_slotCardW * 0.90f, 0.0012f),
            BoardVisual.Unlit(new Color(0.20f, 0.16f, 0.12f, 0.75f)));
        mr.transform.localPosition = new Vector3(cardLocal.x, cardLocal.y, cardLocal.z - 0.002f);
        mr.gameObject.SetActive(false);
        return mr.gameObject;
    }

    /// <summary>A collider-free LIT cube (BoardLit → Standard fallback) — the 3D handle bar and
    /// any future solid furniture piece, shaded like the local board's own primitives instead of
    /// the flat unlit quads of the 2D era.</summary>
    private static void LitCube(Transform parent, string name, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localScale = size;
        Shader? shader = Cards.PlayTray.BoardLitShader()
                         ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse")
                         ?? Shader.Find("Sprites/Default");
        if (shader != null)
            go.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = color };
    }

    // ---------------------------------------------------------------- change-gated setters --

    private void SetWanted(int mask)
    {
        if (mask == _shownWantedMask)
            return;
        _shownWantedMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_wanted[i] != null && _wanted[i]!.activeSelf != on)
                _wanted[i]!.SetActive(on);
        }
    }

    private void SetSnap(int mask)
    {
        if (mask == _shownSnapMask)
            return;
        _shownSnapMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_snap[i] != null && _snap[i]!.activeSelf != on)
                _snap[i]!.SetActive(on);
        }
    }

    private void SetHalves(int mask)
    {
        if (mask == _shownHalfMask)
            return;
        _shownHalfMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_halves[i] != null && _halves[i]!.activeSelf != on)
                _halves[i]!.SetActive(on);
        }
    }

    // ---------------------------------------------------------------- inert cap --

    /// <summary>
    /// One INERT keycap in REAL 3D: the same base plate + beveled/round cap MESH the local board's
    /// <c>PlayTray.BoardButton</c> renders — dark-wood base, cap body through
    /// <c>PlayTray.NewKeycapMaterial</c> (BoardLit + carved-grain <c>_MainTex</c>, so the walls
    /// shade even in the unlit scenes), engraved parchment label — with everything button-like
    /// omitted: NO collider, NO press travel, NO <c>IPokeable</c>, registered NOWHERE. It is a
    /// solid picture of a button.
    ///
    /// Geometry constants below are copies of <c>PlayTray.BoardButton</c>'s authored privates
    /// (promoting them would widen PlayTray's internal surface — the same trade the layout consts
    /// at the top of this file already make); the three float lerps are linted against drift by
    /// <c>scripts/check-mirrors.sh</c>.
    /// </summary>
    private sealed class InertCap
    {
        /// <summary>Mirror of PlayTray.BoardButton.CapRestZ — the cap's seat toward the viewer.</summary>
        private const float CapRestZ = -0.004f;

        /// <summary>Mirror of PlayTray.SquareCapBevel — the lit 45° chamfer width.</summary>
        private const float CapBevel = 0.007f;

        // Mirrors of PlayTray.BoardButton's wall/bevel tint recipe (checked by check-mirrors.sh).
        private const float WallTintFactor = 0.50f;
        private const float WallWarmLerp = 0.42f;
        private const float BevelLerp = 0.48f;
        private static readonly Color WallWarm = new(0.17f, 0.11f, 0.06f);
        private static readonly Color BevelHighlight = new(0.66f, 0.53f, 0.32f);

        private readonly GameObject _go;
        private readonly TextMeshPro _label;
        private string _shown = string.Empty;

        /// <summary>The cap's three live material instances (top plateau / bright bevel / dark
        /// warm wall) so a STATE colour change can be re-applied to all three at once — the local
        /// <c>BoardButton.SetCapColor</c> drives exactly the same trio, which is what makes an
        /// accented remote cap read identically to an accented local one. Null entries on the
        /// round discs (one material) and whenever no cap shader resolved.</summary>
        private Material? _topMat, _bevelMat, _wallMat;

        /// <summary>Change gate for <see cref="SetTint"/> — a material write per 4 Hz refresh is
        /// exactly the churn the cadence exists to avoid.</summary>
        private Color _tint = new(-1f, -1f, -1f, -1f);

        /// <summary>World position of the cap centre (build-time layout math — the USE cap centres
        /// between Confirm and Undo across two different parent anchors).</summary>
        public Vector3 WorldPosition => _go.transform.position;

        private InertCap(GameObject go, TextMeshPro label)
        {
            _go = go;
            _label = label;
        }

        /// <summary>The square beveled keycap (Confirm/Undo/Use/Pin): dark base plate + the
        /// 3-submesh chamfered cap mesh (state top / bright bevel / dark warm walls).</summary>
        public static InertCap Square(Transform parent, string name, Vector3 localPos, Vector2 size,
            float depth, Color color)
        {
            GameObject go = NewRoot(parent, name, localPos);

            // Base plate: the recessed well the cap sits in (BoardButton's non-round branch).
            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            Object.Destroy(basePlate.GetComponent<Collider>());
            basePlate.transform.SetParent(go.transform, worldPositionStays: false);
            basePlate.transform.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 0.006f);
            basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            TintLit(basePlate, new Color(0.15f, 0.12f, 0.08f));

            float capThick = Mathf.Max(0.012f, depth);
            var capMesh = new GameObject("CapMesh");
            capMesh.transform.SetParent(go.transform, worldPositionStays: false);
            capMesh.transform.localPosition = new Vector3(0f, 0f, CapRestZ);
            capMesh.AddComponent<MeshFilter>().sharedMesh =
                Cards.CardMesh.BuildBeveledKeycap(size.x, size.y, capThick, CapBevel);
            var mr = capMesh.AddComponent<MeshRenderer>();
            Shader? shader = CapShader();
            Material? top = null, bevel = null, wall = null;
            if (shader != null)
            {
                top = Cards.PlayTray.NewKeycapMaterial(shader, color);            // [0] top plateau
                bevel = Cards.PlayTray.NewKeycapMaterial(shader, BevelTint(color)); // [1] bright bevel
                wall = Cards.PlayTray.NewKeycapMaterial(shader, WallTint(color));   // [2] dark warm wall
                mr.sharedMaterials = new[] { top, bevel, wall };
            }

            TextMeshPro label = BuildLabel(go.transform, size,
                new Vector3(0f, 0f, CapRestZ - capThick - 0.001f));
            return new InertCap(go, label)
            {
                _topMat = top,
                _bevelMat = bevel,
                _wallMat = wall,
                _tint = color,
            };
        }

        /// <summary>The round disc cap (rest discs, turn-flow Skip): recessed well ring + smooth
        /// generated disc, in the same carved-grain keycap material family.</summary>
        public static InertCap Round(Transform parent, string name, Vector3 localPos, float diameter,
            float thickness, Color color)
        {
            GameObject go = NewRoot(parent, name, localPos);

            var basePlate = new GameObject("Base");
            basePlate.transform.SetParent(go.transform, worldPositionStays: false);
            basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            basePlate.AddComponent<MeshFilter>().sharedMesh =
                Cards.CardMesh.GetRoundCap(diameter + 0.006f, 0.006f);
            var baseMr = basePlate.AddComponent<MeshRenderer>();

            float capThick = Mathf.Max(0.002f, thickness);
            var capDisc = new GameObject("CapMesh");
            capDisc.transform.SetParent(go.transform, worldPositionStays: false);
            capDisc.transform.localPosition = new Vector3(0f, 0f, CapRestZ);
            capDisc.AddComponent<MeshFilter>().sharedMesh =
                Cards.CardMesh.GetRoundCap(diameter, capThick);
            var capMr = capDisc.AddComponent<MeshRenderer>();

            Shader? shader = CapShader();
            if (shader != null)
            {
                baseMr.sharedMaterial = new Material(shader) { color = new Color(0.15f, 0.12f, 0.08f) };
                capMr.sharedMaterial = Cards.PlayTray.NewKeycapMaterial(shader, color);
            }

            TextMeshPro label = BuildLabel(go.transform, new Vector2(diameter, diameter),
                new Vector3(0f, 0f, CapRestZ - capThick * 0.5f - 0.001f));
            return new InertCap(go, label);
        }

        private static GameObject NewRoot(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            return go;
        }

        /// <summary>The engraved parchment label the local caps wear
        /// (<c>NativeButtonSkin.StyleEngravedLabel</c> + TmpFit inside the cap face).</summary>
        private static TextMeshPro BuildLabel(Transform parent, Vector2 size, Vector3 localPos)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, worldPositionStays: false);
            labelGo.transform.localPosition = localPos;
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            WorldUI.NativeButtonSkin.StyleEngravedLabel(tmp);
            TmpFit.Fit(tmp, size.x * 0.92f, size.y * 0.85f, maxFontSize: 0.40f);
            return tmp;
        }

        /// <summary>BoardLit (shades walls even in the unlit scenes) with the same fallback ladder
        /// as <c>PlayTray.BoxCapShader</c>.</summary>
        private static Shader? CapShader() =>
            Cards.PlayTray.BoardLitShader()
            ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse")
            ?? Shader.Find("Sprites/Default");

        private static void TintLit(GameObject go, Color color)
        {
            Shader? shader = CapShader();
            if (shader != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = color };
        }

        private static Color WallTint(Color top)
        {
            var dark = new Color(top.r * WallTintFactor, top.g * WallTintFactor, top.b * WallTintFactor, top.a);
            Color w = Color.Lerp(dark, WallWarm, WallWarmLerp);
            w.a = top.a;
            return w;
        }

        private static Color BevelTint(Color top)
        {
            Color b = Color.Lerp(top, BevelHighlight, BevelLerp);
            b.a = top.a;
            return b;
        }

        /// <summary>Change-gated label write (a per-tick TMP assignment re-triggers auto-size).
        /// Runs the shared tofu strip first (WorldUI.NativeButtonSkin.SanitizeLabel): the chip
        /// labels are the same translated strings the LOCAL keycaps show, so a glyph the mod
        /// font cannot render would box identically on the peer's mirror — both sides must
        /// show exactly the same thing (MP rule), including the same clean fallback.</summary>
        public void SetLabel(string text)
        {
            text = WorldUI.NativeButtonSkin.SanitizeLabel(_label, text);
            if (text == _shown)
                return;
            _shown = text;
            _label.text = text;
        }

        public void SetShown(bool shown)
        {
            if (_go.activeSelf != shown)
                _go.SetActive(shown);
        }

        /// <summary>
        /// Re-tint the cap to a STATE colour — the inert counterpart of the local
        /// <c>BoardButton.SetCapColor</c>, driving the same three submesh materials with the same
        /// two derived tints, so an accented cap on a peer's board is the same colour as the
        /// accented cap on its owner's. Change-gated; a no-op on a cap whose shader never resolved.
        /// </summary>
        public void SetTint(Color color)
        {
            if (color == _tint)
                return;
            _tint = color;
            if (_topMat != null) _topMat.color = color;
            if (_bevelMat != null) _bevelMat.color = BevelTint(color);
            if (_wallMat != null) _wallMat.color = WallTint(color);
        }
    }

    // ---------------------------------------------------------------- inertness guarantee --

    /// <summary>
    /// BELT AND BRACES for the "nothing on a peer's board may be interactable" rule: walk the
    /// finished hierarchy and destroy any <c>Collider</c> that made it in. Today none can — every
    /// builder above strips primitive colliders at creation (and <see cref="RemoteTrayVisual"/>
    /// silently strips the prefab's expected ones before this guard ever runs) — but a future edit
    /// that adds a plain <c>CreatePrimitive</c> would silently make a peer's board pokeable, and
    /// that is exactly the regression this catches. The warning it logs is deliberately loud
    /// (grep: "remote board furniture carried").
    /// </summary>
    internal static void StripColliders(GameObject root, string who)
    {
        Collider[] found = root.GetComponentsInChildren<Collider>(includeInactive: true);
        if (found == null || found.Length == 0)
            return;
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                Object.Destroy(found[i]);
        }
        VRLog.Warn("Net", $"INERT-GUARD: {who} carried {found.Length} collider(s) — destroyed. " +
                          "A remote player's control board is a pure display: nothing on it may be " +
                          "pokeable, laser-targetable or grabbable.");
    }
}

/// <summary>
/// Self-animated soft pulse for the remote board's "wanted slot" hint — the mirror of
/// <c>PlayTray.SlotPulse</c> (same 3.2 rad/s breath between 0.30 and 0.85), kept local to the Net
/// module so the remote board never reaches into the local board's private visuals. Runs only while
/// its quad is active, i.e. only while a peer really has an unfilled slot during selection.
///
/// It is a RENDERING component: it writes a material colour and nothing else. No input, no
/// collider, no registry.
/// </summary>
internal sealed class RemoteGlowPulse : MonoBehaviour
{
    private Material? _material;
    private Color _base;

    internal void Init(Material material, Color baseColor)
    {
        _material = material;
        _base = baseColor;
    }

    private void Update()
    {
        if (_material == null)
            return;
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
        float k = Mathf.Lerp(0.30f, 0.85f, t);
        _material.color = new Color(_base.r * k, _base.g * k, _base.b * k, k);
    }
}
