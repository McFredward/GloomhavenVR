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
/// THE CAPS ARE ANIMATED, not just drawn (2026-08-08 ruling: "alle Interaktionen, ANIMATIONEN und
/// Anzeigen des Controllboards … so wie der Spieler sie sieht"). A local keycap crumbles into a dust
/// burst when it is taken away, assembles back out of that dust when it returns, dips its full
/// authored travel on a press and springs back, and switches between four state colours. Every one
/// of those used to be a POP or a fixed colour on a peer's board. They are reproduced here by
/// <see cref="RemoteCapFx"/> — and three of the four cost NOTHING on the wire, because the
/// transitions they animate (show/hide, state) were already synced by the board-UI record; only the
/// PRESS is an event with no state behind it, and it rides five reserved bits of a record that
/// already exists. An ANIMATION IS NOT INTERACTIVITY: the animator writes a transform, a scale and
/// material colours, and the inertness guarantee below is unchanged and unweakened.
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
/// host-replicated <c>CPlayerActor</c> model and the already-synced VR extras).
///
/// THE DECISION DISPLAY IS SYNCED WHOLE (user ruling 2026-08-08): its button wordings (record 12),
/// each option's OFFERED / GREYED / CHOSEN state (record 23) and the prompt LINE above them are all
/// mirrored — the line composed on this machine from the record's variant id, because the owner's
/// own sentence can embed active-bonus card names (see <see cref="RemoteDecisionPrompt"/>). And it
/// disappears whole: while the owner has focused another character their board shows nothing at
/// that seat, so the records stop riding and this copy empties with it. The "LOCAL-ONLY neutral
/// look" list this paragraph used to carry — button enabled-states, live Confirm wordings, the
/// follow/pin preference — is empty now; every entry on it became a synced field.
///
/// The modal PICK FIELD the flat board used to draw is
/// GONE — the local board removed its pick field outright, so a copy of it had become a picture
/// of a widget that no longer exists.
///
/// ANTI-CHEAT is unchanged: nothing here reads a card identity, and the pieces that DO depend on the
/// peer's card state (the wanted-slot pulse and the snap-glow hover rim) are slot POSITIONS the
/// remote board already draws — see their notes. The HALF-CARD DIVIDER that used to be the third
/// such piece is gone: it drew a hairline across every face-up round card, standing in for poke
/// zones the owner's own board deliberately never draws, so under the 1:1 rule it was a widget peers
/// saw and the owner did not. Deleted, not gated — there is no owner-side state to gate it by.
///
/// COST. Built once, torn down with the board root, and refreshed on the shared
/// <see cref="RemoteBoardContent.RefreshSeconds"/> (4 Hz) cadence with change-gated writes. The
/// per-frame work is the single <see cref="RemoteGlowPulse"/> component (only while a pulse is
/// visible) plus one <see cref="RemoteCapFx"/> per cap, which early-returns on the first line
/// unless that cap is mid-press or mid-dissolve — an idle board does no per-frame work at all, and
/// nothing here logs per frame.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (PER-ACTOR MODEL + VR-ONLY-derived + one small record of its
/// own). The old "NEUTRAL LOOKS" / "LOCAL-ONLY STATE" reading of this file — button
/// interactability, the Confirm wording, the drawer's contents being
/// "knowable-but-not-worth-a-field" — is GONE, one member at a time and finally as a rule: the
/// FOLLOW/PIN toggle left it through the board-UI record's byte 1 bit 2, the cap wordings through
/// record 13, the decision buttons through record 12, and their OFFERED / GREYED / CHOSEN states
/// plus the prompt line through record 23 and this round's ruling ("alle Interaktionen,
/// Animationen und Anzeigen des Controllboards … so wie der Spieler sie sieht" — see the class
/// definition in <see cref="RemoteBoardContent"/>). What is left of DELIBERATELY-NOT here is the
/// safety half alone: no card identity, ever. Personal TUNING offsets are not that class either —
/// every client renders a given board style at its shipped layout, which is a rendering
/// convention, not a hidden display. The cap STATES and the snap-glow HOVER left the list this
/// round, through the board-UI record's byte 2 and its byte 1 bits 6..7; the cap PRESS left it
/// through record 14's reserved byte-0 bits; and the UNDO / item-USE wordings through record 13's
/// mask bits 2..3. Slot occupancy and the pile stacks are PER-ACTOR MODEL; the wanted-slot pulse is
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

    /// <summary>The clearance between the grab-bar bottom and the prompt reference the owner's
    /// dock anchors under — read STRAIGHT from the local dock
    /// (<see cref="WorldUI.Surfaces.DecisionDockSurface.BarClearanceMeters"/>) since the 1:1
    /// mirror round, so it can no longer drift from the original it copies.</summary>
    private const float BarClearanceMeters = WorldUI.Surfaces.DecisionDockSurface.BarClearanceMeters;

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

    // ---- authored PRESS TRAVEL, per cap category ----------------------------------------------
    // How far the local cap of each category sinks under a press (the [*] Travel entries). Like
    // every other number on this board these are the SHIPPED defaults, never the peer's private
    // tuning: a mirrored press must look the same on every client that renders that board style.
    private const float BoardCapTravel = Defaults.BoardButtons_Travel;
    private const float DashCapTravel = Defaults.BoardDashboard_Travel;
    private const float RestCapTravel = Defaults.RestButtons_Travel;
    private const float TransientCapTravel = Defaults.RoundButtons_Travel;

    /// <summary>Authored seconds a vanishing cap's dust dissolve runs
    /// (<c>[ButtonAnim] DisappearSeconds</c> — the duration <c>PlayTray.BoardButton.SetVisible</c>
    /// shrinks the local cap out over). Authored, not the viewer's tuned value, for the same reason
    /// every geometry constant here is authored.</summary>
    internal const float DissolveSeconds = Defaults.DisappearSeconds;

    /// <summary>Authored seconds an appearing cap's materialize-from-dust fade runs
    /// (<c>[ButtonAnim] AppearSeconds</c>).</summary>
    internal const float AppearSeconds = Defaults.AppearSeconds;

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

    // The owner's live CARD width (record 11) is no longer read here: its only consumer was the
    // half-poke divider, which the 1:1 round deleted. The ctor still ACCEPTS it — the parameter is
    // part of the board's construction contract and the frame metric beside it is very much in use
    // — it simply has nothing to size any more.

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

    // ---- the STATE palette every mirrored keycap now switches through (gap: "every cap looks
    //      enabled and un-accented"). The local caps have four looks and a peer saw ONE: the
    //      colour the cap was constructed with. These three are the shared statics
    //      PlayTray.BoardButton.StateColor picks between; they are private there and are Color
    //      values, which scripts/check-mirrors.sh (a float/string extractor) cannot lint — so, like
    //      the PIN pair above, they are called out as MIRRORS in both doc comments instead.

    /// <summary>Verbatim <c>PlayTray.BoardButton.IdleColor</c> — the warm parchment every ENABLED,
    /// un-accented keycap rests at. Identical to <see cref="PinIdleColor"/> by construction (that
    /// constant is this one, discovered first, for the FOLLOW/PIN cap alone).</summary>
    private static readonly Color CapIdleColor = PinIdleColor;

    /// <summary>Verbatim <c>PlayTray.BoardButton.DisabledColor</c> — plain dark wood, "an unlit
    /// carved plaque". What a rest disc that is up but dead looks like on the owner's board.</summary>
    private static readonly Color CapDisabledColor = new(0.21f, 0.16f, 0.11f);

    /// <summary>Verbatim <c>PlayTray.BoardButton.ConfirmedColor</c> — worn brass, the "you ARE
    /// ready, pressing this REVOKES" look of the CONFIRM cap.</summary>
    private static readonly Color CapConfirmedColor = new(0.68f, 0.52f, 0.24f);

    /// <summary>Verbatim the dark wood <c>ButtonCluster.PhysicalButton</c> lerps a DISABLED cluster
    /// cap toward — a different recipe from the board keycaps' flat
    /// <see cref="CapDisabledColor"/> (it keeps a trace of the cap's own accent), which is why the
    /// mirrored SKIP cap needs its own branch rather than the shared palette.</summary>
    private static readonly Color ClusterDisabledWood = new(0.17f, 0.13f, 0.09f);

    /// <summary>Mirror of the lerp factor <c>ButtonCluster.PhysicalButton</c> disables a cap
    /// with.</summary>
    private const float ClusterDisabledLerp = 0.75f;

    /// <summary>Mirror of the alpha <c>ButtonCluster.PhysicalButton</c> fades a DISABLED cap's
    /// label to.</summary>
    private const float ClusterDisabledLabelAlpha = 0.35f;

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

    /// <summary>True when <see cref="_decisionRow"/> was built with the SAMPLED GAME BUTTON SPRITE
    /// (the 1:1 look); false while it wears the pre-sample procedural fallback, which
    /// <see cref="SetDecisionLines"/> upgrades as soon as a live button has been harvested.</summary>
    private bool _decisionRowNative;

    /// <summary>One mirrored option plate's repaintable parts — the pieces
    /// <see cref="ApplyDecisionOptionStates"/> writes when the owner's option states move (a toggle
    /// flips, the game re-asserts a gate) WITHOUT rebuilding the row. Face is the 9-sliced game
    /// button sprite when one has been sampled, otherwise the procedural rim/body pair.</summary>
    private readonly struct DecisionPlate
    {
        public DecisionPlate(SpriteRenderer? face, Material? rim, Material? body,
            TextMeshPro label, GameObject chosenRim)
        {
            Face = face;
            RimMat = rim;
            BodyMat = body;
            Label = label;
            ChosenRim = chosenRim;
        }

        public readonly SpriteRenderer? Face;
        public readonly Material? RimMat;
        public readonly Material? BodyMat;
        public readonly TextMeshPro Label;
        public readonly GameObject ChosenRim;
    }

    /// <summary>The mirrored option plates of the current row, in wire order (index i is line i of
    /// record 12 and option i of record 23 — the sender walked the widgets once for both).</summary>
    private readonly System.Collections.Generic.List<DecisionPlate> _decisionPlates = new(4);

    /// <summary>The option states last APPLIED to <see cref="_decisionPlates"/> (wire record 23);
    /// null = nothing applied yet, so the first refresh after a rebuild always paints.</summary>
    private byte[]? _shownOptionStates;

    /// <summary>The composed prompt line last shown above the mirrored row (null = none). Change
    /// gate: a TMP write re-triggers auto-size layout, the badge-flicker lesson.</summary>
    private string? _shownPromptText;

    /// <summary>The mirrored PROMPT TEXT above the decision row — the owner's HelpBox line,
    /// composed on THIS machine by <see cref="RemoteDecisionPrompt"/>; hidden while there is
    /// none.</summary>
    private readonly TextMeshPro _decisionPrompt;

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

    /// <summary>Last synced UNDO wording applied to the cap (record 13 mask bit 2) — same contract
    /// as <see cref="_appliedConfirmWire"/>.</summary>
    private string? _appliedUndoWire;

    /// <summary>Last synced item-USE wording applied to the cap (record 13 mask bit 3) — same
    /// contract as <see cref="_appliedConfirmWire"/>.</summary>
    private string? _appliedUseWire;
    private readonly GameObject?[] _wanted = new GameObject?[2];
    private readonly GameObject?[] _snap = new GameObject?[2];

    // ---- change gates (a TMP/material write per tick is exactly the churn the 4 Hz cadence is
    //      there to avoid; every setter below no-ops until the value really moves) --------------
    private bool _shownArmed;
    private int _shownWantedMask = -1;
    private int _shownSnapMask = -1;
    /// <summary>Last applied synced buttons mask (-2 = nothing applied yet, -1 = legacy sender).</summary>
    private int _shownButtonsMask = -2;
    private string _langShown = string.Empty;

    /// <summary>Last applied cap-STATE byte (record 4 byte 2); -2 = nothing applied yet, -1 = a
    /// sender without the byte (every cap keeps its built colour).</summary>
    private int _shownCapStates = -2;

    /// <summary>The last cap-press key (cap | seq &lt;&lt; 8) this board ANIMATED. The wire field is
    /// a latch that rides several packets, so the dip plays on the value CHANGING; -1 = none seen.
    /// Seeded from the owner's current key on the first refresh so a board built mid-press does not
    /// replay a press that already happened.</summary>
    private int _playedPressKey = -1;

    /// <summary>False until the first <see cref="Refresh"/> has seeded every cap's visibility. The
    /// mirror of the local button's own <c>_ticked</c> guard: a board is BUILT with its caps shown
    /// and the first refresh applies the owner's real mask, which without this gate would crumble
    /// four caps into dust the instant a peer's board appears.</summary>
    private bool _settled;

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
    /// positions the two round CARDS render at — the slot overlays (wanted pulse / snap glow)
    /// centre on them so glow and card agree on every board style.
    /// </summary>
    public RemoteBoardFurniture(Transform boardRoot, Cards.ControlBoard style, RemoteTrayVisual? tray,
        Vector3 slot0CardLocal, Vector3 slot1CardLocal,
        float slotFrameWidth = 0f, float slotCardWidth = 0f)
    {
        // The owner's synced slot metrics (extension record 11); 0 = not on the wire, keep the
        // legacy constant — the exact size every build before the record drew. Only the FRAME
        // metric is consumed now (the glow rims); see the field block above for where the CARD
        // metric's consumer went.
        _ = slotCardWidth;
        _slotFrameW = slotFrameWidth > 0f ? slotFrameWidth : CardW;
        _slotFrameH = _slotFrameW * (88f / 63.5f);

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
        // BUILT AT THE IDLE COLOUR, NOT THE ACCENT — half of the "every cap looks accented" gap,
        // and it costs nothing. The colour a local keycap is CREATED with is its _accentColor, the
        // look it wears only while SetState(accent: true); its resting look is the shared parchment
        // IdleColor. These two mirrors were built in the accent and left there, so a peer's CONFIRM
        // sat permanently in the sage "go" accent and their UNDO in worn leather — a colour the
        // owner's undo cap never wears at all, since every SetState on it is (enabled, !accent).
        // The accent is passed alongside so the state pass can switch back to it.
        _confirm = InertCap.Square(confirmParent, "Confirm", confirmPos,
            new Vector2(BoardCapW, BoardCapH), BoardCapD, CapIdleColor,
            travel: BoardCapTravel, accent: ConfirmColor);
        _undo = InertCap.Square(undoParent, "Undo", undoPos,
            new Vector2(BoardCapW, BoardCapH), BoardCapD, CapIdleColor,
            travel: BoardCapTravel, accent: UndoColor);

        // The item "USE" confirm is a DYNAMIC member of that same generic cluster (PlayTray
        // requirement 9a): while a usable item card is clipped into the use recess it joins as
        // member 1, i.e. the slot between CONFIRM (top) and UNDO (bottom). Drawn at the geometric
        // midpoint of the two caps (computed through world space so it is right on the real
        // anchors too), which is where a sanely tuned three-stack puts it.
        Vector3 useMidLocal = confirmParent.InverseTransformPoint(
            (_confirm.WorldPosition + _undo.WorldPosition) * 0.5f);
        // …and this one IS accented, permanently: every SetState on the local item-USE cap is
        // (enabled: true, accent: true) — "always pressable while shown (no game gate)" — so its
        // look is a BUILD fact, not a state fact, and it costs no wire bit (see
        // NetProtocol.BoardUiCapConfirmAccentBit's "what is not here" note).
        _use = InertCap.Square(confirmParent, "ItemUse", useMidLocal,
            new Vector2(BoardCapW, BoardCapH), BoardCapD, ConfirmColor,
            travel: BoardCapTravel, accent: ConfirmColor);
        // Starts hidden and in step with the _shownArmed seed below: the local cluster only holds
        // this member while an item decision is pending, and Refresh() early-outs while nothing
        // changed — so a board that never sees an item fan must not be left showing a USE cap.
        _use.SetShown(false);

        // ---- rest discs (real-tray board only — they seat in the prefab's rest notches) --------
        // The local board's short/long rest BoardButtons: round discs at the ShortRestToken /
        // LongRestToken anchors + the authored per-style offset ± spacing/2 (RestControls
        // EnsureBuilt/SetOffset).
        //
        // THE "NEUTRAL LOOK" NOTE THAT USED TO STAND HERE IS GONE. It said the discs are "drawn at
        // their authored accent colours; whether the peer has actually selected a rest is a
        // separate readout, not a cap state". That was the defect: RestControls.TickStatus drives
        // these two caps with SetState(canShort, accent: shortSelected) — three visibly different
        // looks (dark-wood dead, parchment available, accented selected) — and a peer saw the
        // ACCENT one always, i.e. every visible rest disc read as "selected". The states ride the
        // board-UI record's cap-state byte now and are applied in ApplyCapStates; the discs are
        // built IDLE and switch to their authored accent when the owner's do.
        if (tray?.ShortRestAnchor != null && tray.LongRestAnchor != null)
        {
            Vector3 restOff = RestOffsetFor(style);
            float restSpacing = RestSpacingFor(style);
            float restD = RestDiameterFor(style);
            _shortRest = InertCap.Round(tray.ShortRestAnchor, "ShortRest",
                restOff + new Vector3(0f, restSpacing * 0.5f, 0f), restD, RestCapD, CapIdleColor,
                travel: RestCapTravel, accent: ShortRestColor);
            _longRest = InertCap.Round(tray.LongRestAnchor, "LongRest",
                restOff + new Vector3(0f, -restSpacing * 0.5f, 0f), restD, RestCapD, CapIdleColor,
                travel: RestCapTravel, accent: LongRestColor);
        }

        // FOLLOW/PIN toggle: built in the FOLLOW (idle) look, then driven from the owner's synced
        // state every refresh (SetPinned) — label AND cap colour, exactly like their own cap.
        _pin = InertCap.Square(_root, "FollowToggle", PinMount + PinOffsetFor(style),
            new Vector2(PinCapW, DashCapH), DashCapD, PinIdleColor,
            travel: DashCapTravel, accent: PinAccentColor);

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
                TransientCapR * 2f * clusterScale, TransientCapD * clusterScale, SkipColor,
                travel: TransientCapTravel * clusterScale, accent: SkipColor, clusterStyle: true)
            : InertCap.Square(_root, "TurnFlowSkip", skipSeat,
                new Vector2(Defaults.RoundButtons_Width, Defaults.RoundButtons_Height) * clusterScale,
                Defaults.RoundButtons_Depth * clusterScale, SkipColor,
                travel: TransientCapTravel * clusterScale, accent: SkipColor, clusterStyle: true);

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
        // …and the PROMPT TEXT above it, at the seat the owner's own tip takes: their
        // DamageTooltipSurface parks the converted HelpBox AboveRowMetres over the decision MOUNT
        // (mount position + up × 0.11), so unlike the row — whose Y solve cancels the mount out —
        // this one DOES follow the mount's authored Y offset. Built empty and hidden; filled from
        // the wire-driven variant on every refresh (SetDecisionPrompt).
        _decisionPrompt = BuildDecisionPrompt(new Vector3(
            DecisionMount.x + decisionOff.x,
            DecisionMount.y + decisionOff.y + PromptAboveMountY,
            DecisionMount.z + decisionOff.z), style);

        // ---- slot overlays: wanted pulse + snap glow ------------------------------------------
        // Centred on the CARD positions handed in by the board (the real recess anchors when the
        // 3D asset is up) PLUS the authored per-board SLOT-OVERLAY seat — see SlotOverlayLocal for
        // why dropping that term is what pushed every overlay off-centre inside the recess.
        //
        // THE HALF-POKE DIVIDER IS GONE (this round, deliberately, zero wire). A third quad used to
        // be drawn across the middle of every face-up round card here — a hairline standing in for
        // the HalfSelection poke zones, on the argument that "a literally faithful copy would draw
        // nothing at all and the element would be missing from the peer's board". Under the 1:1
        // ruling that argument inverts: the local zones are INVISIBLE BY DESIGN ("the game's own
        // on-card highlight is the only hover/selection feedback"), so drawing nothing is not a
        // missing element, it IS the element — and the divider was a widget every peer could see
        // and the owner could not. The half states themselves are not lost: hover and click both
        // ride record 14 and are drawn as the game's own two-state on-card highlight, which is
        // exactly what the owner sees. Deleted rather than gated, because there is no owner-side
        // state that could gate it: the zones are never visible.
        for (int i = 0; i < 2; i++)
        {
            Vector3 card = (i == 0 ? slot0CardLocal : slot1CardLocal) + SlotOverlayLocal(style, i);
            _wanted[i] = BuildSlotGlow($"WantedGlow{i}", card, 1.36f, -0.003f,
                new Color(0.25f, 0.85f, 0.60f, 0.70f), pulse: true);
            _snap[i] = BuildSlotGlow($"SnapGlow{i}", card, 1.24f, -0.005f,
                new Color(1f, 0.85f, 0.30f, 0.95f), pulse: false);
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
    /// <paramref name="slotMask"/> says which of that peer's card slots currently hold a card — the
    /// mask <c>RemoteControlBoard.SeatSlots</c> already resolved for the slots themselves, handed
    /// down rather than re-derived, so nothing here can disagree with the cards or leak anything
    /// the board does not already show. It feeds ONE thing now: the LEGACY snap-glow fallback for a
    /// sender that carries no board-UI record (a synced sender's gold rim follows their actual
    /// hover — see the snap block below).
    ///
    /// <para>The <c>showFronts</c> / <c>faceMask</c> pair this method used to take is GONE with the
    /// half-card divider that was their only consumer (see the ctor's slot-overlay block for why
    /// that widget was deleted). The reveal gate itself is untouched — it still governs the CARDS,
    /// upstream in <see cref="RemoteControlBoard"/>, exactly as before.</para>
    /// </summary>
    public void Refresh(CPlayerActor? actor, RemoteAvatar owner, int slotMask)
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
            // THE SHOW/HIDE ANIMATION IS ZERO-WIRE (this round's first gap). Locally a keycap does
            // not pop: it crumbles into a sideways dust burst on the way out and assembles back out
            // of that dust on the way in (PlayTray.BoardButton.SetVisible). Every peer saw a pop —
            // yet the transition itself was ALREADY synced, right here, by the visibility bits.
            // So the same two animations are simply played on this copy off the same edge. Nothing
            // new goes on the wire, and nothing on this board becomes interactive: the animator is
            // a rendering component (see RemoteCapFx).
            //
            // Suppressed on the FIRST refresh (_settled): the caps are built shown, so the first
            // application of the owner's real mask is state SEEDING, not a transition the owner
            // made — the same reason the local button silences its own animation until it has
            // ticked once.
            bool animate = _settled;
            if (synced)
            {
                _confirm.SetShown((buttons & NetProtocol.BoardUiConfirmBit) != 0, animate);
                _undo.SetShown((buttons & NetProtocol.BoardUiUndoBit) != 0, animate);
                _shortRest?.SetShown((buttons & NetProtocol.BoardUiShortRestBit) != 0, animate);
                _longRest?.SetShown((buttons & NetProtocol.BoardUiLongRestBit) != 0, animate);
                _skip.SetShown((buttons & NetProtocol.BoardUiSkipBit) != 0, animate);
                SetShown(_itemUse, (buttons & NetProtocol.BoardUiItemRecessBit) != 0);
                // The decision drawer: drawn only while a prompt is actually docked on the
                // owner's board — an idle local board shows nothing at that mount.
                SetShown(_decision, (buttons & NetProtocol.BoardUiDecisionBit) != 0);
            }
            else
            {
                // Legacy sender: the pre-record look (everything drawn, drawer always out).
                _confirm.SetShown(true, animate);
                _undo.SetShown(true, animate);
                _shortRest?.SetShown(true, animate);
                _longRest?.SetShown(true, animate);
                _skip.SetShown(true, animate);
                SetShown(_itemUse, true);
                SetShown(_decision, true);
            }
        }

        // ---- SYNCED CAP STATES (record 4 byte 2 — "every cap looks enabled and un-accented") --
        ApplyCapStates(owner);

        // ---- SYNCED CAP PRESS (record 14 byte 0 bits 3..7) ------------------------------------
        ApplyCapPress(owner);

        // ---- SYNCED CAP LABELS (wire record 13 — task 4 "der Button-Text muss immer korrekt
        //      synchronisiert sein"). The owner's actually-displayed CONFIRM/SKIP wording, shown
        //      verbatim in their language; absent record = the neutral ApplyLabels fallback.
        SetCapLabels(owner);

        // ---- SYNCED DECISION DISPLAY (wire records 12 + 23 — task 2 "die Entscheidungsbuttons
        //      1:1", extended by the 2026-08-08 ruling to the WHOLE decision display: "alle
        //      Interaktionen, Animationen und Anzeigen des Controllboards … so wie der Spieler sie
        //      sieht"). Three passes, in the order the owner's own dock builds them:
        //        • the LABELS of the row their dock really shows, as inert plates at the same
        //          bar-anchored seat (record 12; without labels the captioned idle drawer stands in);
        //        • their per-option STATES — greyed / dimmed / chosen (record 23), repainted without
        //          rebuilding the row, because those move on every click while the wordings do not;
        //        • the PROMPT TEXT above the plates, composed HERE from the record's variant id and
        //          this client's own localization (the words never ride the wire — see
        //          RemoteDecisionPrompt).
        //      All three vanish together the moment the owner's row does — including when they
        //      focus another character and their own board goes blank at this seat.
        SetDecisionLines(owner.DecisionLines);
        ApplyDecisionOptionStates(owner.DecisionOptionStates);
        SetDecisionPrompt(actor, owner);

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
            // The USE cap is a real keycap that appears and disappears with the item decision, so
            // it takes the same mirrored dust transition as the rest of the column.
            _use.SetShown(armed, _settled);
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

        // ---- snap glow: the HOVER TELEGRAPH, synced ------------------------------------------
        // THE COMMENT THAT USED TO STAND HERE SAID "a remote hover is not reproduced (and cannot
        // be)". Both halves of that were wrong, and the sentence is what kept the defect alive.
        //
        // It is not the same event. Locally the gold rim is a HOVER telegraph — "the held card
        // lands HERE on release" (PlayTray.SetHighlightedSlot ← CardsDriver.UpdateSlotHighlight):
        // the owner sees it while they are still holding the card, it FOLLOWS their hand across the
        // two recesses, and it goes out again if they pull away without dropping. This mirror lit
        // its rim on the model's empty→occupied edge instead and faded it after 0.6 s, so a peer
        // saw the telegraph AFTER the drop it was telegraphing, and never at all for a hover that
        // ended without one.
        //
        // And it was never impossible: the hovered recess is one small integer the owner's own
        // board already renders, and the record it belongs in had two RESERVED bits sitting in the
        // very byte the wanted-glow mask rides. It costs zero extra bytes (board-UI byte 1 bits
        // 6..7). Anti-cheat is unchanged and strictly weaker than the occupancy nibble two bits
        // below it: a recess POSITION for a card the peer is already watching the owner carry.
        //
        // LEGACY senders (no board-UI record at all) keep the old occupancy-edge flash, so an
        // old peer's board renders exactly as it always did.
        int snapMask;
        if (synced)
        {
            int hovered = owner.SnapGlowSlot;
            snapMask = hovered >= 0 && hovered < 2 ? 1 << hovered : 0;
            // Keep the occupancy edge detector fed so a mid-session fallback (a sender that stops
            // carrying the record) resumes from a truthful state rather than re-flashing history.
            _wasFilled[0] = slot0;
            _wasFilled[1] = slot1;
        }
        else
        {
            snapMask = 0;
            for (int i = 0; i < 2; i++)
            {
                bool filled = i == 0 ? slot0 : slot1;
                if (filled && !_wasFilled[i])
                    _snapLitAt[i] = Time.unscaledTime;
                _wasFilled[i] = filled;
                if (Time.unscaledTime - _snapLitAt[i] < SnapGlowSeconds)
                    snapMask |= 1 << i;
            }
        }
        SetSnap(snapMask);

        _settled = true;

        StateLine = $"use={(armed ? "armed" : "idle")}, " +
                    $"buttons={(synced ? "0x" + owner.BoardButtonsMask.ToString("X2") : "legacy")}, " +
                    $"capStates={(owner.HasCapStates ? "0x" + owner.CapStateMask.ToString("X2") : "legacy")}, " +
                    $"wanted={wantedMask}{(synced ? "(synced)" : string.Empty)}, " +
                    $"snap={snapMask}{(synced ? "(hover)" : "(occupancy edge)")}, " +
                    $"tray={(owner.TrayPinned ? "PINNED" : "FOLLOW")}{(synced ? "(synced)" : "(default)")}, " +
                    $"capLabels[confirm={(owner.ConfirmCapLabel != null ? "'" + owner.ConfirmCapLabel + "'" : "neutral")}, " +
                    $"skip={(owner.SkipCapLabel != null ? "'" + owner.SkipCapLabel + "'" : "neutral")}, " +
                    $"undo={(owner.UndoCapLabel != null ? "'" + owner.UndoCapLabel + "'" : "neutral")}, " +
                    $"use={(owner.ItemUseCapLabel != null ? "'" + owner.ItemUseCapLabel + "'" : "neutral")}], " +
                    $"decision={(_shownDecisionLines != null ? _shownDecisionLines.Split('\n').Length + " synced button(s)" : "drawer")}" +
                    $"[{DescribeStates(_shownOptionStates, _decisionPlates.Count)}]" +
                    // Single quotes around the line, like the cap labels above: a nested \" inside
                    // an interpolation hole trips the patch-inventory source scanner.
                    $", prompt={(_shownPromptText != null ? "'" + StripRichText(_shownPromptText) + "'" : "none")}";
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
        // THE TWO WORDINGS THAT NEVER TRAVELLED (record 13 mask bits 2/3, new this round).
        //   • UNDO carries the pick flow's dialog-CANCEL override while the event-discard confirm
        //     dialog is open ("Wähle eine andere Karte") — in that flow this cap IS the popup's
        //     second button, and every peer read a flat "Rückgängig" instead.
        //   • The item-USE cap carries an item-SURRENDER demand's own wording, precisely so that
        //     "the user must never read a surrender as an ordinary use" — and the mirror wrote a
        //     hardcoded GUI_USE, so peers watching a player hand an item over saw them USE it.
        string? undo = owner.UndoCapLabel;
        if (undo != _appliedUndoWire)
        {
            _appliedUndoWire = undo;
            _undo.SetLabel(undo ?? Loc.Game("GUI_UNDO", "Undo"));
        }
        string? use = owner.ItemUseCapLabel;
        if (use != _appliedUseWire)
        {
            _appliedUseWire = use;
            _use.SetLabel(use ?? Loc.Game("GUI_USE", "USE").ToUpperInvariant());
        }
    }

    /// <summary>
    /// Apply the owner's live cap STATES (board-UI record byte 2): the CONFIRM cap's accent /
    /// confirmed look, both rest discs' enabled + accent pair, and the SKIP cap's interactability.
    ///
    /// <para>WHAT A PEER USED TO SEE. Every mirrored cap wore the ONE colour it was constructed
    /// with, for the whole session. A greyed-out rest disc, a rest disc the owner had SELECTED and
    /// an available one were the same picture; so were a brass-accented pick-flow CONFIRM and a
    /// gold "you are ready, press to revoke" CONFIRM; and a dead SKIP looked pressable. The local
    /// caps have four looks (<c>BoardButton.SetState</c> → <c>StateColor</c>) and the cluster cap
    /// two, all of which this now resolves out of the same palette in the same precedence.</para>
    ///
    /// <para>WHAT IS DELIBERATELY NOT HERE: UNDO and the item-USE cap. Every <c>SetState</c> call
    /// on them in the whole mod is a constant — <c>(enabled, !accent)</c> for UNDO,
    /// <c>(enabled, accent)</c> for USE — so their look is a BUILD fact and is applied by the
    /// constructor for zero bits. The FOLLOW/PIN cap's accent is byte 1 bit 2 and is applied by
    /// <see cref="SetPinned"/>, where it has ridden since the pinned bit shipped.</para>
    ///
    /// <para>A sender without the byte (<c>HasCapStates</c> false) leaves every cap exactly where
    /// the constructor put it.</para>
    /// </summary>
    private void ApplyCapStates(RemoteAvatar owner)
    {
        int states = owner.HasCapStates ? owner.CapStateMask : -1;
        if (states == _shownCapStates)
            return;
        _shownCapStates = states;
        if (states < 0)
            return;
        _confirm.SetCapState(
            enabled: true, // the board HIDES an unpressable confirm rather than greying it
            accent: (states & NetProtocol.BoardUiCapConfirmAccentBit) != 0,
            confirmed: (states & NetProtocol.BoardUiCapConfirmReadyBit) != 0);
        _shortRest?.SetCapState(
            enabled: (states & NetProtocol.BoardUiCapShortRestEnabledBit) != 0,
            accent: (states & NetProtocol.BoardUiCapShortRestAccentBit) != 0,
            confirmed: false);
        _longRest?.SetCapState(
            enabled: (states & NetProtocol.BoardUiCapLongRestEnabledBit) != 0,
            accent: (states & NetProtocol.BoardUiCapLongRestAccentBit) != 0,
            confirmed: false);
        _skip.SetCapState(
            enabled: (states & NetProtocol.BoardUiCapSkipEnabledBit) != 0,
            accent: true, // a cluster cap has no idle look; its accent IS its enabled colour
            confirmed: false);
        VRLog.Info("Net", $"Remote cap states applied: 0x{states:X2} — confirm=" +
                          ((states & NetProtocol.BoardUiCapConfirmReadyBit) != 0 ? "CONFIRMED"
                              : (states & NetProtocol.BoardUiCapConfirmAccentBit) != 0 ? "accent"
                              : "idle") +
                          ", shortRest=" +
                          ((states & NetProtocol.BoardUiCapShortRestEnabledBit) != 0 ? "enabled" : "DIMMED") +
                          ((states & NetProtocol.BoardUiCapShortRestAccentBit) != 0 ? "+accent" : string.Empty) +
                          ", longRest=" +
                          ((states & NetProtocol.BoardUiCapLongRestEnabledBit) != 0 ? "enabled" : "DIMMED") +
                          ((states & NetProtocol.BoardUiCapLongRestAccentBit) != 0 ? "+accent" : string.Empty) +
                          ", skip=" +
                          ((states & NetProtocol.BoardUiCapSkipEnabledBit) != 0 ? "enabled" : "DIMMED") +
                          " (board-UI record byte 2). Colours only — the caps stay colliderless.");
    }

    /// <summary>
    /// Replay the owner's keycap PRESS on this copy (record 14 byte 0 bits 3..7). The wire field is
    /// a LATCH that rides several packets per press, so the dip plays when the (cap, sequence) pair
    /// CHANGES, never merely when it is set — otherwise the same press would replay on every packet
    /// of its hold window.
    ///
    /// <para>The first refresh SEEDS the key without animating: a board built while a press is
    /// still latched must not open with a dip for something that already happened.</para>
    ///
    /// <para>An ANIMATION, not an interaction: the mapped cap sinks and springs back and nothing is
    /// invoked. No collider, no registration, nothing added to the copy at all — the dip is a
    /// transform write inside <see cref="RemoteCapFx"/>.</para>
    /// </summary>
    private void ApplyCapPress(RemoteAvatar owner)
    {
        int key = owner.CapPressKey;
        if (key < 0 || key == _playedPressKey)
            return;
        bool seed = !_settled;
        _playedPressKey = key;
        if (seed)
            return;
        byte cap = (byte)(key & 0xFF);
        InertCap? target = cap switch
        {
            NetProtocol.CapPressConfirm => _confirm,
            NetProtocol.CapPressUndo => _undo,
            NetProtocol.CapPressItemUse => _use,
            NetProtocol.CapPressShortRest => _shortRest,
            NetProtocol.CapPressLongRest => _longRest,
            NetProtocol.CapPressSkip => _skip,
            NetProtocol.CapPressFollowPin => _pin,
            _ => null,
        };
        if (target == null)
            return;
        target.Press();
        VRLog.Info("Net", $"Remote cap press animated: wire cap {cap} (sequence {(key >> 8) & 0x03}) " +
                          "— the mirrored cap sinks its full authored travel and springs back at the " +
                          "owner's own decay rate. Nothing was invoked and nothing became pressable: " +
                          "the copy is still a picture of a button.");
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
    /// THE "NEUTRAL LOOKS" LIST THIS METHOD USED TO CARRY IS EMPTY. It declared that "CONFIRM /
    /// UNDO / SKIP / REST enabled-vs-disabled … are drawn ENABLED (the authored base colour), never
    /// dimmed", on the argument that the states are recomputed per frame on the owner's client
    /// only. That is true of the COMPUTATION and irrelevant to the RESULT: the result is four
    /// distinct colours on a labelled control, and a peer seeing one of them while the owner sees
    /// another is exactly the disagreement the 1:1 rule forbids. Seven bits of the board-UI record's
    /// cap-state byte carry every one of them now (see <see cref="ApplyCapStates"/>), read off the
    /// flags the owner's own renderer obeys.
    ///
    /// The FOLLOW/PIN toggle left that list earlier: its label and accent are SYNCED (board-UI
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
        _appliedUndoWire = null;
        _appliedUseWire = null;
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

    /// <summary>Mirror of <c>WorldUI.Surfaces.DamageTooltipSurface.AboveRowMetres</c> — how far
    /// above the decision MOUNT the owner's own prompt text is parked. Board-local metres at the
    /// authored mount, so the mirrored line sits over the mirrored plates exactly as the original
    /// sits over the real ones.</summary>
    private const float PromptAboveMountY = 0.11f;

    /// <summary>
    /// The mirrored PROMPT TEXT of the decision dock — the line the owner reads above their docked
    /// buttons ("Schadensphase: Erleide entweder Schaden, verbrenne …"), composed on THIS machine
    /// from the wire-carried variant id (see <see cref="RemoteDecisionPrompt"/> for why the text
    /// itself may never ride the wire). Built once, empty and hidden; a game-HUD-font label with
    /// the help box's own gold/grey rich-text colouring, MR-backed like every other line that hangs
    /// below the board in open air. Display-only: one TMP, no collider, nothing to press.
    /// </summary>
    private TextMeshPro BuildDecisionPrompt(Vector3 local, Cards.ControlBoard style)
    {
        float scale = DecisionScaleFor(style);
        TextMeshPro label = RemoteBoardContent.Label(_root, "DecisionPrompt", local,
            new Vector2(Cards.PlayTray.DecisionMountWidth * scale, 0.075f * scale),
            0.17f * scale, new Color(0.82f, 0.80f, 0.76f),
            TextAlignmentOptions.Center, wrap: true);
        WorldUI.NativeButtonSkin.ApplyFont(label); // the game's HUD font, depth-honest material
        label.richText = true;                     // the help box's own gold title / grey body
        WorldUI.MrBacking.Label(label);
        label.gameObject.SetActive(false);
        return label;
    }

    /// <summary>
    /// Show (or hide) the mirrored prompt line for the owner's docked decision. The text is
    /// COMPOSED here from the wire's prompt-kind + text-variant pair and this client's own
    /// localization — never received — so the mandatory-use variant's active-bonus card names
    /// cannot travel; see <see cref="RemoteDecisionPrompt"/>. Change-gated on the composed string
    /// (a per-tick TMP write re-triggers auto-size layout).
    /// </summary>
    private void SetDecisionPrompt(CPlayerActor? actor, RemoteAvatar owner)
    {
        // No visible row on the owner's board ⇒ no line, whatever the last state record said.
        string? text = _shownDecisionLines == null
            ? null
            : RemoteDecisionPrompt.Compose(owner.DecisionPromptKind, owner.DecisionTextVariant, actor);
        if (text == _shownPromptText)
            return;
        _shownPromptText = text;
        if (_decisionPrompt == null)
            return;
        bool show = !string.IsNullOrEmpty(text);
        if (show)
            _decisionPrompt.text = text;
        if (_decisionPrompt.gameObject.activeSelf != show)
            _decisionPrompt.gameObject.SetActive(show);
        VRLog.Info("Net", show
            ? $"Remote decision prompt: line composed LOCALLY for prompt kind " +
              $"{owner.DecisionPromptKind} / text variant {owner.DecisionTextVariant} — " +
              $"\"{StripRichText(text!)}\" — shown {PromptAboveMountY * 1000f:F0} mm above the " +
              "decision mount, the same seat the owner's own HelpBox takes over their buttons. The " +
              "wire carried the VARIANT, never the words (no card identity, ever)."
            : "Remote decision prompt: no line (no visible decision row, a prompt that has none, or " +
              "a sender predating record 23).");
    }

    /// <summary>Strip TMP colour tags for a log line (the composed prompt is rich text).</summary>
    private static string StripRichText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inTag = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
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

    /// <summary>Per-plate width FLOOR at scale 1 — a two-letter option ("Ja"/"Nein") must still
    /// read as a button, not as a sliver, when it shares the row with a long burn wording.</summary>
    private const float DecisionButtonMinW = 0.055f;

    /// <summary>Padding added to a label's content share when the row is sized content-true (the
    /// game's option buttons are a ContentSizeFitter around the wording plus a fixed inset, so a
    /// short option is a SHORT button — the equal-slot split the first mirror used was the most
    /// visible size difference against the owner's real row).</summary>
    private const float DecisionButtonPadW = 0.030f;

    /// <summary>
    /// (Re)build the mirrored decision-button row from the owner's synced labels (wire record
    /// 12; null = none). Change-gated on the joined string — a rebuild is a handful of quads and
    /// labels, and it only happens when the owner's dock content really changed. The idle
    /// captioned drawer shows exactly while the decision bit is set WITHOUT labels, so a legacy
    /// sender keeps its familiar look.
    ///
    /// 1:1 WITH WHAT THE DECIDING PLAYER SEES (user 2026-08-07, verbatim: "Die
    /// Entscheidungsbuttons sollen 1:1 genau so aussehen (Position und Größe und Erscheinungsbild)
    /// und genau das beinhalten was der Spieler sieht"). The owner's dock does NOT build buttons —
    /// <c>WorldUI.Surfaces.DecisionDockSurface</c> re-hosts the GAME's own prompt widgets on the
    /// board and restyles them in <c>AdjustDockedRow</c>: the shared 9-sliced game button sprite
    /// multiplied by <c>DecisionDockSurface.AntiqueTint</c>, labels recoloured to
    /// <c>NativeButtonSkin.LabelColor</c> in the game's HUD font. The mirror therefore reproduces
    /// that same look from the same sources instead of approximating it:
    /// <list type="bullet">
    ///   <item>FACE — <c>NativeButtonSkin.CreateFace</c>, i.e. a 9-sliced <c>SpriteRenderer</c>
    ///     wearing the very sprite <c>NativeButtonSkin</c> harvested off the live game UI (every
    ///     client owns the same assets, so nothing about the button art needs to ride the wire),
    ///     tinted with the local dock's own <c>AntiqueTint</c> constant. The flat gold-rim/dark-body
    ///     quads it replaces are kept only as the fallback for the window before a live button has
    ///     been sampled.</item>
    ///   <item>LABEL — the game HUD font (<c>NativeButtonSkin.ApplyFont</c>), the dock's
    ///     <c>LabelColor</c>, and the shared un-renderable-glyph strip, so the wording reads
    ///     identically to the owner's caption.</item>
    ///   <item>SIZE — plates are CONTENT-TRUE (the game fits each option button to its wording),
    ///     laid out inside the very envelope the owner's dock fits its row into
    ///     (<c>PlayTray.DecisionMountWidth</c> × the authored dock scale), so a short "Schaden
    ///     erhalten" is a short button next to a long burn wording.</item>
    ///   <item>POSITION — unchanged: the row already hangs from the widget-block top edge the
    ///     owner's own dock anchors at (bar bottom − <c>BarClearanceMeters</c> − the authored
    ///     <c>DecisionGap</c>), centred on the dock axis.</item>
    /// </list>
    /// Display-only by construction: no collider, no <c>IPokeable</c>, registered with no laser or
    /// poke router, and <see cref="StripColliders"/> sweeps the finished row.
    /// </summary>
    private void SetDecisionLines(string? lines)
    {
        // Change gate, with ONE exception: a row that had to fall back to the procedural plate
        // (no live game button sampled yet when it was built) is rebuilt as soon as
        // NativeButtonSkin has one, so the first prompt of a session cannot get stuck on the
        // approximate look the 1:1 rule replaced.
        bool upgrade = _decisionRow != null && !_decisionRowNative && WorldUI.NativeButtonSkin.HasSprite;
        if (lines == _shownDecisionLines && !upgrade)
            return;
        _shownDecisionLines = lines;

        if (_decisionRow != null)
        {
            Object.Destroy(_decisionRow.gameObject);
            _decisionRow = null;
        }
        _decisionPlates.Clear();
        _shownOptionStates = null; // a new row repaints its states from scratch
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

        // One horizontal row, centred on the dock axis like the game's own option rows, inside the
        // owner's own width budget — with CONTENT-TRUE plate widths (see the member doc).
        float budget = Cards.PlayTray.DecisionMountWidth * scale;
        float gap = DecisionButtonGap * scale;
        float h = DecisionButtonH * scale;
        float[] widths = DecisionPlateWidths(labels, budget, gap, scale);
        float rowW = (n - 1) * gap;
        for (int i = 0; i < n; i++)
            rowW += widths[i];

        bool native = WorldUI.NativeButtonSkin.HasSprite;
        _decisionRowNative = native;
        Color gold = WorldUI.NativeButtonSkin.HasFont
            ? WorldUI.NativeButtonSkin.LabelColor
            : new Color(0.91f, 0.82f, 0.62f);
        float x = -rowW * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float w = widths[i];
            var plate = new GameObject($"Button{i}").transform;
            plate.SetParent(_decisionRow, worldPositionStays: false);
            plate.localPosition = new Vector3(x + w * 0.5f, -h * 0.5f, 0f);
            x += w + gap;

            // The owner's docked widget IS the game's 9-sliced button sprite multiplied by
            // AntiqueTint — so wear the same sprite and the same constant here.
            SpriteRenderer? face = native
                ? WorldUI.NativeButtonSkin.CreateFace(plate, new Vector2(w, h), localZ: 0.001f,
                    sortingOrder: 0)
                : null;
            Material? rimMat = null;
            Material? bodyMat = null;
            if (face != null)
            {
                face.color = WorldUI.Surfaces.DecisionDockSurface.AntiqueTint;
            }
            else
            {
                // Pre-sample fallback (no live button harvested yet): the flat gold-rim/dark-body
                // plate of the first mirror, kept so an early prompt is never an empty hole.
                rimMat = BoardVisual.Unlit(new Color(0.55f, 0.45f, 0.22f, 1f));
                BoardVisual.Quad(plate, "Rim", new Vector2(w, h), rimMat)
                    .transform.localPosition = new Vector3(0f, 0f, 0.0015f);
                bodyMat = BoardVisual.Unlit(new Color(0.23f, 0.18f, 0.12f, 1f));
                BoardVisual.Quad(plate, "Face", new Vector2(w - 0.006f * scale, h - 0.006f * scale),
                    bodyMat)
                    .transform.localPosition = new Vector3(0f, 0f, 0.001f);
            }

            // The CHOSEN telegraph: a thin accent frame behind the plate, shown only while the
            // owner has that option toggled on (wire record 23). Inert like everything here, and
            // built once per plate so the state repaint never allocates.
            GameObject chosenRim = BoardVisual.Quad(plate, "ChosenRim",
                new Vector2(w + 0.008f * scale, h + 0.008f * scale),
                BoardVisual.Unlit(new Color(1f, 0.85f, 0.35f, 0.95f))).gameObject;
            chosenRim.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            chosenRim.SetActive(false);

            TextMeshPro label = RemoteBoardContent.Label(plate, "Label", new Vector3(0f, 0f, -0.001f),
                new Vector2(w * 0.86f, h * 0.72f), 0.23f * scale, gold,
                TextAlignmentOptions.Center, wrap: true);
            WorldUI.NativeButtonSkin.ApplyFont(label); // game HUD font, depth-honest material
            label.color = gold;                        // ApplyFont must not undo the dock colour
            label.text = WorldUI.NativeButtonSkin.SanitizeLabel(label, labels[i]);
            _decisionPlates.Add(new DecisionPlate(face, rimMat, bodyMat, label, chosenRim));
        }
        StripColliders(_decisionRow.gameObject, "RemoteBoardFurniture.SyncedRow");

        VRLog.Info("Net", $"Remote decision dock: {n} mirrored button(s) " +
                          $"(\"{lines.Replace('\n', '|')}\") — row {rowW:F3} m wide, content-true " +
                          $"plate widths [{string.Join(", ", System.Array.ConvertAll(widths, v => v.ToString("F3")))}] " +
                          $"× {h:F3} m at the authored ×{scale:F2} dock scale, face = " +
                          $"{(native ? "the sampled GAME button sprite (9-sliced) × DecisionDockSurface.AntiqueTint" : "procedural fallback plate (no live button sampled yet)")}, " +
                          $"top edge at board-local y {_decision.localPosition.y:F3} (bar bottom − " +
                          $"clearance − authored DecisionGap_{_decisionStyle}) — the seat, look and " +
                          "wording the OWNER's own docked row shows. Display-only: colliderless.");
    }

    /// <summary>Procedural-fallback plate colours (only used before a live game button has been
    /// sampled) — kept as constants so the state repaint can restore them exactly.</summary>
    private static readonly Color FallbackRimColor = new(0.55f, 0.45f, 0.22f, 1f);
    private static readonly Color FallbackBodyColor = new(0.23f, 0.18f, 0.12f, 1f);

    /// <summary>How far a GREYED option is pushed toward the board's shadow — the mirror of what a
    /// non-interactable uGUI Selectable looks like on the owner's dock (its ColorTint transition
    /// multiplies the disabled colour onto the CanvasRenderer).</summary>
    private const float GreyedFactor = 0.55f;

    /// <summary>Alpha a DIMMED option renders at — the game's own
    /// <c>TakeDamagePanel.UnactiveButtonTransparency</c> (0.7), the "your character does not have
    /// the cards for this" look. Deliberately a SEPARATE axis from greyed: the game shows the two
    /// independently and collapsing them would lose the distinction the owner can see.</summary>
    private const float DimmedAlpha = 0.7f;

    /// <summary>The label gold the mirrored plates letter in — the dock's own
    /// <c>NativeButtonSkin.LabelColor</c> when a live button has been sampled.</summary>
    private static Color BaseLabelGold() => WorldUI.NativeButtonSkin.HasFont
        ? WorldUI.NativeButtonSkin.LabelColor
        : new Color(0.91f, 0.82f, 0.62f);

    /// <summary>
    /// Paint the owner's OPTION STATES (wire record 23) onto the mirrored plates: greyed where the
    /// owner cannot press, dimmed where the game dims, and a lit accent frame on the option they
    /// have already chosen.
    ///
    /// <para>WHY IT IS A SEPARATE PASS from <see cref="SetDecisionLines"/>: the wordings are
    /// constant for a whole prompt while the states move on every click — a toggle flips, the game
    /// re-asserts a gate (<c>CardsDriver.TickTakeDamageOptions</c>). Rebuilding the row for that
    /// would rebuild a handful of quads several times per decision; this repaints four material
    /// colours instead, and only when the bytes actually change.</para>
    ///
    /// <para>A sender that predates record 23 delivers no states: every plate then keeps the plain
    /// look every build before this one drew — never a guess at which option is live, which would
    /// be a lie about somebody else's decision.</para>
    /// </summary>
    private void ApplyDecisionOptionStates(byte[]? states)
    {
        if (_decisionPlates.Count == 0)
        {
            _shownOptionStates = states;
            return;
        }
        if (SameStates(states, _shownOptionStates))
            return;
        _shownOptionStates = states;

        Color gold = BaseLabelGold();
        for (int i = 0; i < _decisionPlates.Count; i++)
        {
            DecisionPlate plate = _decisionPlates[i];
            byte flags = states != null && i < states.Length ? states[i] : (byte)0;
            bool known = states != null && i < states.Length;
            // Unknown (pre-record sender) ⇒ the plain look: offered, undimmed, unchosen.
            bool offered = !known || (flags & NetProtocol.DecisionOptionOfferedBit) != 0;
            bool dimmed = known && (flags & NetProtocol.DecisionOptionDimmedBit) != 0;
            bool chosen = known && (flags & NetProtocol.DecisionOptionChosenBit) != 0;

            float tint = offered ? 1f : GreyedFactor;
            float alpha = dimmed ? DimmedAlpha : 1f;
            if (plate.Face != null)
            {
                Color c = WorldUI.Surfaces.DecisionDockSurface.AntiqueTint;
                plate.Face.color = new Color(c.r * tint, c.g * tint, c.b * tint, c.a * alpha);
            }
            if (plate.RimMat != null)
                plate.RimMat.color = new Color(FallbackRimColor.r * tint, FallbackRimColor.g * tint,
                    FallbackRimColor.b * tint, FallbackRimColor.a * alpha);
            if (plate.BodyMat != null)
                plate.BodyMat.color = new Color(FallbackBodyColor.r * tint,
                    FallbackBodyColor.g * tint, FallbackBodyColor.b * tint,
                    FallbackBodyColor.a * alpha);
            if (plate.Label != null)
                plate.Label.color = new Color(gold.r * tint, gold.g * tint, gold.b * tint,
                    gold.a * alpha);
            if (plate.ChosenRim != null && plate.ChosenRim.activeSelf != chosen)
                plate.ChosenRim.SetActive(chosen);
        }
        VRLog.Info("Net", $"Remote decision states applied: {_decisionPlates.Count} plate(s) — " +
                          $"{DescribeStates(states, _decisionPlates.Count)} (wire record 23). " +
                          "Greyed/dim/chosen read exactly as on the owner's own dock; still inert — " +
                          "no collider, no raycast target, nothing to press.");
    }

    /// <summary>Value equality for the applied option-state bytes (the repaint's change gate).</summary>
    private static bool SameStates(byte[]? a, byte[]? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    /// <summary>Human-readable option states for the diagnostic line.</summary>
    private static string DescribeStates(byte[]? states, int plates)
    {
        if (states == null)
            return "no state record (pre-record sender ⇒ every plate keeps the plain look)";
        var sb = new System.Text.StringBuilder(48);
        for (int i = 0; i < plates; i++)
        {
            if (i > 0)
                sb.Append(", ");
            if (i >= states.Length)
            {
                sb.Append('#').Append(i).Append("=unstated");
                continue;
            }
            byte f = states[i];
            sb.Append('#').Append(i).Append('=')
              .Append((f & NetProtocol.DecisionOptionOfferedBit) != 0 ? "OFFERED" : "greyed");
            if ((f & NetProtocol.DecisionOptionDimmedBit) != 0)
                sb.Append("+dim");
            if ((f & NetProtocol.DecisionOptionChosenBit) != 0)
                sb.Append("+CHOSEN");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Content-true plate widths for one mirrored decision row. The game fits each option button to
    /// its wording (ContentSizeFitter + inset), so an equal split is visibly wrong beside the
    /// owner's real row; widths are shared out in proportion to the label lengths, each clamped to
    /// [<see cref="DecisionButtonMinW"/>, <see cref="DecisionButtonMaxW"/>] and the whole row then
    /// scaled to fit the owner's width budget. Never returns a non-positive width.
    /// </summary>
    private static float[] DecisionPlateWidths(string[] labels, float budget, float gap, float scale)
    {
        int n = labels.Length;
        var widths = new float[n];
        float minW = DecisionButtonMinW * scale;
        float maxW = DecisionButtonMaxW * scale;
        float padW = DecisionButtonPadW * scale;
        float free = Mathf.Max(minW * n, budget - (n - 1) * gap);

        // Share the content budget (what is left after each plate's fixed inset) by label length.
        float contentBudget = Mathf.Max(0f, free - n * padW);
        int totalChars = 0;
        for (int i = 0; i < n; i++)
            totalChars += Mathf.Max(1, labels[i].Length);

        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            float share = totalChars > 0 ? contentBudget * Mathf.Max(1, labels[i].Length) / totalChars : 0f;
            widths[i] = Mathf.Clamp(padW + share, minW, maxW);
            sum += widths[i];
        }
        // The clamps can push the row past the budget — shrink uniformly rather than overflow the
        // owner's dock envelope (a mirror that is WIDER than the original is the one thing the 1:1
        // rule cannot tolerate).
        if (sum > free && sum > 0f)
        {
            float k = free / sum;
            for (int i = 0; i < n; i++)
                widths[i] = Mathf.Max(0.001f, widths[i] * k);
        }
        return widths;
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

    // BuildHalfDivider / SetHalves are GONE (see the ctor's slot-overlay block for the argument):
    // they drew a hairline across every face-up round card on a peer's board, standing in for poke
    // zones the OWNER's board deliberately never draws. Under the 1:1 rule that is a widget peers
    // see and the owner does not, and there is no owner-side state that could gate it, so it was
    // deleted rather than gated. Zero wire either way.

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

        /// <summary>The travelling CAP holder — the transform the local
        /// <c>BoardButton.Update</c> sinks along local +Z on a press, here driven by
        /// <see cref="RemoteCapFx"/> from the synced press edge.</summary>
        private Transform? _capMesh;

        /// <summary>This cap's per-frame animator (press dip, dust dissolve, materialize fade). A
        /// pure RENDERING component: it writes a transform, a scale and material colours, adds no
        /// collider and registers nowhere. Null only in a shader-less environment where nothing
        /// could animate anyway.</summary>
        private RemoteCapFx? _fx;

        /// <summary>The colour this cap was BUILT with, i.e. the <c>_accentColor</c> the local
        /// <c>BoardButton.Create</c> was handed — the ACCENT entry of the state palette, and the
        /// only one of the four looks a peer's board used to be able to show.</summary>
        private Color _accentColor;

        /// <summary>True for the turn-flow SKIP cap, which mirrors a <c>ButtonCluster</c>
        /// PhysicalButton rather than a <c>BoardButton</c> and therefore has its OWN disabled
        /// recipe (an accent-preserving lerp toward dark wood plus a faded label) instead of the
        /// board keycaps' flat disabled plaque.</summary>
        private bool _clusterStyle;

        /// <summary>Last applied (enabled, accent, confirmed) triple, packed — the change gate for
        /// <see cref="SetCapState"/>. -1 = nothing applied yet, so the first refresh always paints.</summary>
        private int _shownState = -1;

        /// <summary>The engraved label's authored colour, so the cluster-style disabled fade can be
        /// applied and undone without drifting.</summary>
        private Color _labelBase = Color.white;

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
            float depth, Color color, float travel = 0f, Color? accent = null,
            bool clusterStyle = false)
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

            // The LABEL hangs off the CAP holder on the local board precisely so it travels with
            // the cap on a press ("it used to hang off the static root while only the cap sank,
            // reading as detached"). Same parenting here, so the mirrored dip moves the same parts.
            TextMeshPro label = BuildLabel(capMesh.transform, size,
                new Vector3(0f, 0f, -capThick - 0.001f));
            var cap = new InertCap(go, label)
            {
                _topMat = top,
                _bevelMat = bevel,
                _wallMat = wall,
                _tint = color,
                _capMesh = capMesh.transform,
                _accentColor = accent ?? color,
                _clusterStyle = clusterStyle,
                _labelBase = label.color,
            };
            cap.AttachFx(travel, Mathf.Max(size.x, size.y));
            return cap;
        }

        /// <summary>The round disc cap (rest discs, turn-flow Skip): recessed well ring + smooth
        /// generated disc, in the same carved-grain keycap material family.</summary>
        public static InertCap Round(Transform parent, string name, Vector3 localPos, float diameter,
            float thickness, Color color, float travel = 0f, Color? accent = null,
            bool clusterStyle = false)
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
            Material? disc = null;
            if (shader != null)
            {
                baseMr.sharedMaterial = new Material(shader) { color = new Color(0.15f, 0.12f, 0.08f) };
                disc = Cards.PlayTray.NewKeycapMaterial(shader, color);
                capMr.sharedMaterial = disc;
            }

            TextMeshPro label = BuildLabel(capDisc.transform, new Vector2(diameter, diameter),
                new Vector3(0f, 0f, -capThick * 0.5f - 0.001f));
            var cap = new InertCap(go, label)
            {
                // A disc has ONE cap material (no bevel/wall submeshes) — exactly like the local
                // round BoardButton, whose SetCapColor drives its top colour alone.
                _topMat = disc,
                _tint = color,
                _capMesh = capDisc.transform,
                _accentColor = accent ?? color,
                _clusterStyle = clusterStyle,
                _labelBase = label.color,
            };
            cap.AttachFx(travel, diameter);
            return cap;
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

        /// <summary>
        /// Give this cap its per-frame animator. <paramref name="travel"/> is the AUTHORED press
        /// travel of the cap's category (0 = a cap whose presses are not mirrored — it still gets
        /// the show/hide dust, which needs no travel); <paramref name="footprint"/> sizes the dust
        /// burst exactly as the local button sizes its own from its trigger box.
        /// </summary>
        private void AttachFx(float travel, float footprint)
        {
            _fx = _go.AddComponent<RemoteCapFx>();
            _fx.Init(_capMesh, CapRestZ, travel, footprint, CurrentStateColor, PaintCap);
        }

        /// <summary>Replay the owner's press dip on this copy (synced press edge). A no-op on a cap
        /// with no animator or no travel. NOTHING is invoked — this is the animation, not the
        /// button.</summary>
        public void Press() => _fx?.Press();

        /// <summary>
        /// Show or hide the cap. <paramref name="animate"/> false pops it (the build-time seeding
        /// and the first refresh, mirroring the local button's own <c>_ticked</c> suppression of
        /// the build-then-settle storm); true plays the owner's own transition — the dust dissolve
        /// on the way out, the materialize-from-dust on the way in.
        /// </summary>
        public void SetShown(bool shown, bool animate = false)
        {
            if (_go.activeSelf == shown && !(shown && animate && _fx != null && _fx.Hiding))
                return;
            if (!animate || _fx == null || !WorldUI.ButtonTuning.ButtonAnimEnabled)
            {
                _fx?.CancelAnimations();
                if (_go.activeSelf != shown)
                    _go.SetActive(shown);
                return;
            }
            if (shown)
            {
                if (!_go.activeSelf)
                    _go.SetActive(true);
                _fx.PlayAppear();
            }
            else
            {
                // The local hide is LOGICAL first and visual after; there is no logical half here
                // (nothing was ever interactive), so this is the visual half alone — the cap stays
                // active for the shrink and the animator deactivates it at the end.
                if (!_go.activeInHierarchy)
                {
                    _go.SetActive(false);
                    return;
                }
                _fx.PlayDissolve();
            }
        }

        /// <summary>
        /// Apply the owner's live cap STATE — the inert counterpart of
        /// <c>PlayTray.BoardButton.SetState</c> + <c>UpdateColor</c>, resolving the same four looks
        /// in the same precedence (disabled beats confirmed beats accent beats idle) out of the
        /// same palette. The SKIP cap mirrors a cluster button instead and takes its accent-
        /// preserving disabled lerp plus the faded label. Change-gated on the packed triple.
        /// </summary>
        public void SetCapState(bool enabled, bool accent, bool confirmed)
        {
            int key = (enabled ? 1 : 0) | (accent ? 2 : 0) | (confirmed ? 4 : 0);
            if (key == _shownState)
                return;
            _shownState = key;
            SetTint(StateColor(enabled, accent, confirmed));
            if (_clusterStyle && _label != null)
            {
                Color c = _labelBase;
                c.a = enabled ? _labelBase.a : ClusterDisabledLabelAlpha;
                _label.color = c;
            }
        }

        /// <summary>The colour this cap should rest at for a state triple — see
        /// <see cref="SetCapState"/>.</summary>
        private Color StateColor(bool enabled, bool accent, bool confirmed)
        {
            // A cluster cap has TWO looks, not four: its authored accent, or that accent lerped
            // toward dark wood. ButtonCluster.PhysicalButton has no idle/confirmed states at all —
            // MirrorSkip only ever hands it visible + interactable.
            if (_clusterStyle)
                return enabled
                    ? _accentColor
                    : Color.Lerp(_accentColor, ClusterDisabledWood, ClusterDisabledLerp);
            return !enabled ? CapDisabledColor
                : confirmed ? CapConfirmedColor
                : accent ? _accentColor
                : CapIdleColor;
        }

        /// <summary>The cap's resting STATE colour right now — what the materialize fade ramps up
        /// to and what the dust burst is coloured with, exactly like the local button's
        /// <c>CurrentCapColor</c>.</summary>
        private Color CurrentStateColor() => _tint;

        /// <summary>Write a colour onto the cap's live materials WITHOUT touching the change gate —
        /// the animator's fade ramp uses this, so completing a fade restores the true state colour
        /// through <see cref="SetTint"/>'s gate rather than fighting it.</summary>
        private void PaintCap(Color color)
        {
            if (_topMat != null) _topMat.color = color;
            if (_bevelMat != null) _bevelMat.color = BevelTint(color);
            if (_wallMat != null) _wallMat.color = WallTint(color);
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
/// THE MIRRORED KEYCAP ANIMATIONS — the inert twin of what <c>PlayTray.BoardButton.Update</c> and
/// <c>SetVisible</c> do to a real board button, run on a peer's colliderless copy.
///
/// <para>Locally a cap sinks under a fingertip, springs back on a click, crumbles into dust when it
/// is taken away and assembles out of dust when it returns. On a peer's board every one of those
/// was a POP. Under the 1:1 ruling ("alle Interaktionen, ANIMATIONEN und Anzeigen des
/// Controllboards … so wie der Spieler sie sieht") the transitions have to look the same, and three
/// of the four need no wire at all: the show/hide edge is already synced by the board-UI record, so
/// the receiver plays the owner's own dissolve/materialize off a transition it can already see.
/// Only the PRESS is an event with no state behind it, and that is the one field that was added
/// (record 14 byte 0 bits 3..7).</para>
///
/// <para>THE PRESS SHAPE is copied term for term from the original: the local <c>_press</c> impulse
/// is set to 1 at the moment of the press and decays linearly at <see cref="PressDecayPerSecond"/>,
/// while the cap sits at <c>CapRestZ + Travel × depth</c>. So the cap drops to the bottom of its
/// travel instantly and rises back over about 170 ms. The FINGER-FOLLOW half of the local motion
/// (the cap tracking penetration depth continuously while a fingertip hovers) is deliberately NOT
/// reproduced: it is a per-frame function of the owner's fingertip position, it would cost a
/// per-frame stream to sync, and what it exists to telegraph — the commit — is exactly what the
/// press edge already delivers.</para>
///
/// <para>IT IS A RENDERING COMPONENT AND NOTHING ELSE. It writes a transform, a local scale and
/// material colours, and it emits into the SHARED, pooled, purely-visual
/// <c>WorldUI.ButtonDissolveFx</c> particle system the local buttons use. It creates no collider,
/// no rigidbody and no registration of any kind, so <c>RemoteBoardFurniture.StripColliders</c> has
/// nothing to find — an ANIMATION is not interactivity.</para>
///
/// <para>The viewer's <c>[ButtonAnim] Enable</c> switch gates it, exactly as it gates their own
/// board's caps: a player who has turned keycap animation off has turned it off, and a remote board
/// is not the place to re-impose it. The DURATIONS, by contrast, are the AUTHORED ones and not the
/// viewer's tuning — the same rule every geometry constant on this board follows.</para>
/// </summary>
internal sealed class RemoteCapFx : MonoBehaviour
{
    /// <summary>Mirror of the local press spring's decay rate (<c>BoardButton.Update</c>:
    /// <c>Mathf.MoveTowards(_press, 0f, Time.deltaTime * 6f)</c>) — linted against drift by
    /// scripts/check-mirrors.sh.</summary>
    private const float PressDecayPerSecond = 6f;

    /// <summary>Mirror of the brightness the local materialize fade starts its surface ramp from
    /// (<c>BoardButton.Update</c>: <c>Mathf.SmoothStep(0.15f, 1f, k)</c>).</summary>
    private const float AppearFadeFloor = 0.15f;

    private Transform? _capMesh;
    private float _restZ;
    private float _travel;
    private float _footprint;
    private System.Func<Color>? _stateColor;
    private System.Action<Color>? _paint;

    private float _press;
    private float _hideLeft;
    private float _showLeft;
    private Vector3 _shownScale = Vector3.one;
    private Color _appearTarget = Color.white;

    /// <summary>True while the dust dissolve is still shrinking the cap out — the window in which a
    /// re-show has to CANCEL the shrink rather than no-op on "already active".</summary>
    internal bool Hiding => _hideLeft > 0f;

    internal void Init(Transform? capMesh, float restZ, float travel, float footprint,
        System.Func<Color> stateColor, System.Action<Color> paint)
    {
        _capMesh = capMesh;
        _restZ = restZ;
        _travel = travel;
        _footprint = footprint;
        _stateColor = stateColor;
        _paint = paint;
        _shownScale = transform.localScale;
    }

    /// <summary>Replay the owner's press dip. Caps with no authored travel (nothing to sink) still
    /// accept the call and simply have nothing to show.</summary>
    internal void Press()
    {
        if (_travel <= 0f || _capMesh == null)
            return;
        _press = 1f;
    }

    /// <summary>Crumble the cap away, then deactivate it. Sized and coloured exactly like the local
    /// burst: the cap's footprint through its own world scale, in its current state colour.</summary>
    internal void PlayDissolve()
    {
        _showLeft = 0f;
        _shownScale = transform.localScale;
        _hideLeft = RemoteBoardFurniture.DissolveSeconds;
        WorldUI.ButtonTuning.LogAnim(name, "disappear (dust dissolve) — MIRRORED");
        WorldUI.ButtonDissolveFx.Play(CapWorldCenter(), -transform.forward,
            _footprint * Mathf.Abs(transform.lossyScale.x), Current());
    }

    /// <summary>Assemble the cap out of dust in place — no scale pop, matching the local appear.</summary>
    internal void PlayAppear()
    {
        _hideLeft = 0f;
        transform.localScale = _shownScale;
        _showLeft = RemoteBoardFurniture.AppearSeconds;
        _appearTarget = Current();
        WorldUI.ButtonTuning.LogAnim(name, "appear (materialize-from-dust) — MIRRORED");
        if (WorldUI.ButtonTuning.AppearParticlesEnabled)
            WorldUI.ButtonDissolveFx.PlayMaterialize(CapWorldCenter(), -transform.forward,
                _footprint * Mathf.Abs(transform.lossyScale.x), _appearTarget);
    }

    /// <summary>Abandon any running transition and restore the cap's true scale and colour — used
    /// when the animation is switched off, and on the silent build-time seeding.</summary>
    internal void CancelAnimations()
    {
        _hideLeft = 0f;
        _showLeft = 0f;
        _press = 0f;
        transform.localScale = _shownScale;
        _paint?.Invoke(Current());
        SeatCap(0f);
    }

    private Color Current() => _stateColor != null ? _stateColor() : Color.white;

    private Vector3 CapWorldCenter() => _capMesh != null ? _capMesh.position : transform.position;

    private void SeatCap(float depth01)
    {
        if (_capMesh == null || _travel <= 0f)
            return;
        Vector3 p = _capMesh.localPosition;
        float z = _restZ + _travel * depth01;
        if (Mathf.Approximately(p.z, z))
            return;
        p.z = z;
        _capMesh.localPosition = p;
    }

    private void Update()
    {
        // An IDLE cap does no per-frame work — this is one branch on three floats, and it is the
        // common case by a wide margin (a board's caps are mid-animation for a fraction of a second
        // at a time). Nothing in here logs, per frame or otherwise.
        if (_hideLeft <= 0f && _showLeft <= 0f && _press <= 0f)
            return;

        // Dust dissolve: the cap shrinks out under the burst, then really goes away.
        if (_hideLeft > 0f)
        {
            _hideLeft -= Time.deltaTime;
            float k = Mathf.Max(0f, _hideLeft / RemoteBoardFurniture.DissolveSeconds);
            transform.localScale = _shownScale * k;
            if (_hideLeft <= 0f)
            {
                transform.localScale = _shownScale; // restore for the next show
                gameObject.SetActive(false);
            }
            return;
        }

        // Materialize: full scale IN PLACE while the opaque surface brightens from the dust up to
        // the true state colour, which is then re-asserted exactly.
        if (_showLeft > 0f)
        {
            _showLeft -= Time.deltaTime;
            float k = 1f - Mathf.Max(0f, _showLeft / RemoteBoardFurniture.AppearSeconds);
            transform.localScale = _shownScale;
            float b = Mathf.SmoothStep(AppearFadeFloor, 1f, k);
            Color faded = _appearTarget * b;
            faded.a = _appearTarget.a;
            _paint?.Invoke(faded);
            if (_showLeft <= 0f)
                _paint?.Invoke(Current());
        }

        // Press spring-back — the local impulse, decay rate and seat formula, unchanged.
        if (_press > 0f)
        {
            _press = Mathf.MoveTowards(_press, 0f, Time.deltaTime * PressDecayPerSecond);
            SeatCap(_press);
        }
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
