using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Phase-3b config. Plugin.cs is frozen shared surface, so the Cards module binds its
/// own ConfigFile (<c>BepInEx/config/dev.gloomhavenvr.cards.cfg</c>) instead of adding
/// entries to the main plugin config.
/// </summary>
internal static class CardsConfig
{
    private static ConfigFile? _file;

    /// <summary>Spawn N dummy VR cards (procedural face) without a scenario — exercises fan/tray/grab under [Dev] SimulateHands.</summary>
    internal static ConfigEntry<int> DevFakeHand = null!;

    /// <summary>How the palm fan reveals: "tilt" (Demeo palm-flip gate) or "always" (open whenever cards exist).</summary>
    internal static ConfigEntry<string> RevealMode = null!;

    /// <summary>
    /// RevealMode=tilt: hand ROLL (degrees, Demeo-measure scale — asin of
    /// dot(side·handRight, worldUp)) above which the fan OPENS. 0 = knuckles-up flat
    /// hand, 90 = palm fully toward the face; pitch/yaw of the arm are irrelevant BY
    /// CONSTRUCTION (roll gate v3 — the v2 projected-angle measure degenerated when
    /// the fingers pitched toward vertical and false-fired on pure pitch).
    /// </summary>
    internal static ConfigEntry<float> RevealEnterDegrees = null!;

    /// <summary>
    /// RevealMode=tilt: hand roll (degrees) below which the fan CLOSES — the hysteresis
    /// dead band under RevealEnterDegrees so the gate cannot chatter at the boundary.
    /// Always clamped below RevealEnterDegrees by the gate. Replaces the old RevealExitDot.
    /// </summary>
    internal static ConfigEntry<float> RevealExitDegrees = null!;

    /// <summary>Fan arc radius in real meters (diorama scale applied automatically).</summary>
    internal static ConfigEntry<float> FanRadius = null!;

    /// <summary>LEGACY — no effect, superseded by <see cref="FanArcSweepDegrees"/> (seeded to this × 1.3).
    /// Bound for cfg back-compat only; nothing reads it.</summary>
    internal static ConfigEntry<float> FanArcDegrees = null!;

    /// <summary>Height of the fan pivot above the palm, real meters.</summary>
    internal static ConfigEntry<float> FanPalmOffset = null!;

    /// <summary>Card width in real meters (poker card = 0.0635); height follows 63.5:88 aspect.</summary>
    internal static ConfigEntry<float> CardWidth = null!;

    /// <summary>Scale factor applied to a card while grabbed/inspected.</summary>
    internal static ConfigEntry<float> InspectScale = null!;

    /// <summary>LEGACY — no effect, superseded by <see cref="HeldFaceBias"/>: the old palm-aligned face
    /// tilt. Bound for cfg back-compat only; nothing reads it. Not to be confused with the LIVE
    /// <c>FigureGrabConfig.HeldTiltDegrees</c>, which a bare-name grep returns alongside this one.</summary>
    internal static ConfigEntry<float> HeldTiltDegrees = null!;

    /// <summary>Held card: face-normal lean from "out of the palm" back toward the wrist/eyes, degrees.</summary>
    internal static ConfigEntry<float> HeldFaceBias = null!;

    /// <summary>Held card: FALLBACK pinch-point offset along the fingers, real meters (no finger joints).</summary>
    internal static ConfigEntry<float> HeldForward = null!;

    /// <summary>Held card: FALLBACK pinch-point offset off the palm surface, real meters (no finger joints).</summary>
    internal static ConfigEntry<float> HeldOffPalm = null!;

    /// <summary>Held card: fine-tune offset added to the thumb/index pinch point, GrabAnchor-local meters.</summary>
    internal static ConfigEntry<Vector3> HeldPinchOffset = null!;

    /// <summary>Tray placement offset from the head, real meters: forward distance.</summary>
    internal static ConfigEntry<float> TrayForward = null!;

    /// <summary>Tray placement offset from the head, real meters: drop below eye height.</summary>
    internal static ConfigEntry<float> TrayDown = null!;

    /// <summary>Tray placement offset, real meters: sideways (+right).</summary>
    internal static ConfigEntry<float> TrayRight = null!;

    /// <summary>Item 12: how the handle-bar grab may move the board (Frei / Begrenzt / Begrenzt mit Neigung).</summary>
    internal static ConfigEntry<BoardMoveMode> BoardMoveMode = null!;

    /// <summary>Item 12: grab-pitch persisted across grabs/sessions, degrees ADDED to the per-board BoardTilt
    /// (positive = more upright). Written by the grab release like TrayYaw; applied only in the
    /// LimitedPitch (clamped) and Free modes.</summary>
    internal static ConfigEntry<float> TrayPitch = null!;

    /// <summary>LEGACY — no effect, superseded by the per-board <c>BoardPitchMin_{board}</c> (seeded to −45,
    /// this entry's default). Bound for cfg back-compat only; nothing reads it.</summary>
    internal static ConfigEntry<float> BoardPitchMinDegrees = null!;

    /// <summary>LEGACY — no effect, superseded by the per-board <c>BoardPitchMax_{board}</c> (seeded to 45,
    /// this entry's default). Bound for cfg back-compat only; nothing reads it.</summary>
    internal static ConfigEntry<float> BoardPitchMaxDegrees = null!;

    /// <summary>LEGACY — no effect, superseded by the per-board <c>BoardTilt_{board}</c> (seeded to 30,
    /// this entry's default). Bound for cfg back-compat only; nothing reads it.</summary>
    internal static ConfigEntry<float> TrayTilt = null!;

    /// <summary>Tray yaw relative to the head's flat forward at placement, degrees (written by the tray grab).</summary>
    internal static ConfigEntry<float> TrayYaw = null!;

    /// <summary>Tray size multiplier (two-handed tray grab), clamped 0.5–2.</summary>
    internal static ConfigEntry<float> TrayScale = null!;

    /// <summary>Tray anchor mode (test #15): true = follows the player (rig-anchored), false = static in world.</summary>
    internal static ConfigEntry<bool> TrayFollow = null!;

    /// <summary>Card seating depth in the physical slot recesses, real meters toward the viewer (−z). Test #28.</summary>
    internal static ConfigEntry<float> SlotCardInset = null!;

    /// <summary>Steady "wanted slot" hint glow on the slot(s) the game is waiting to be filled (test #28).</summary>
    internal static ConfigEntry<bool> WantedSlotHint = null!;

    /// <summary>Dust/spark burst when a card appears or crumbles away. OFF since 2026-08-03.</summary>
    internal static ConfigEntry<bool> CardDust = null!;

    /// <summary>Let the GAME's own card particles play (CardSmoke). OFF since 2026-08-03.</summary>
    internal static ConfigEntry<bool> GameCardParticles = null!;

    /// <summary>First placement of a scenario seats the board beside the head on the LEFT.</summary>
    internal static ConfigEntry<bool> SpawnLeftOfHead = null!;
    internal static ConfigEntry<float> SpawnSideMeters = null!;
    internal static ConfigEntry<float> SpawnForwardMeters = null!;
    internal static ConfigEntry<float> SpawnDownMeters = null!;

    /// <summary>Item 3: multiplier that scales a slotted card UP to (nearly) fill the physical slot recess.</summary>
    internal static ConfigEntry<float> SlotCardFill = null!;

    /// <summary>LEGACY — no effect, superseded by the per-board <c>RestButtonDiameter_{board}</c>, which
    /// <c>RestControls.EnsureBuilt</c> reads. Bound for cfg back-compat only. ("Round" here means the
    /// circular disc SHAPE; everywhere else <c>[RoundButtons]</c> means the round-PHASE button group.)</summary>
    internal static ConfigEntry<float> RoundButtonDiameter = null!;

    /// <summary>LEGACY — no effect, superseded by <c>[RestButtons] Depth</c> (WorldUI
    /// <c>ButtonTuning.RestCapDepth</c>, same 0.012 default). Bound for cfg back-compat only.</summary>
    internal static ConfigEntry<float> RoundButtonThickness = null!;

    /// <summary>LEGACY — no effect, superseded by the per-board <c>RestButtonOffset_{board}</c> (its X
    /// carries this nudge, its Z the proud depth), which <c>RestControls.EnsureBuilt</c> reads. Bound for
    /// cfg back-compat only. Its description used to advertise itself as a LIVE FIT KNOB.</summary>
    internal static ConfigEntry<float> RestButtonInsetX = null!;

    /// <summary>LEGACY — no effect, superseded by the per-board <c>ConfirmUndoOffset_{board}</c>, which
    /// <c>PlayTray.BuildButtons</c> reads. Bound for cfg back-compat only; it is a single global despite
    /// its old description ending "PER-BOARD".</summary>
    internal static ConfigEntry<float> ConfirmUndoInsetX = null!;

    /// <summary>Animation speed for cards flying between fan/tray/half layout (1/s, exponential smoothing).</summary>
    internal static ConfigEntry<float> CardLerpSpeed = null!;

    /// <summary>Discard/burnt pile stacks on the control board + the browse fan (hardware test #21 wish).</summary>
    internal static ConfigEntry<bool> PileViewer = null!;

    /// <summary>ACTIVE CARDS area on the control board's right edge (feature 6): the currently-active ability cards, permanently shown + grabbable.</summary>
    internal static ConfigEntry<bool> ActivePile = null!;

    /// <summary>Aliasing round 3 (T3): runtime MIP BAKE of the game's mipless card-face atlases — adopted card faces sample trilinear/aniso mipmapped copies instead (texture-space shimmer fix).</summary>
    internal static ConfigEntry<bool> FaceMipBake = null!;

    /// <summary>Which control-board prefab is loaded (Oak = the original bundled board; Steel/Bronze are new). Switchable live.</summary>
    internal static ConfigEntry<ControlBoard> Board = null!;

    // [Cards] DebugMenu is GONE (settings audit 2026-07). It used to gate the "Debug — Board tuning"
    // section of the VR settings panel; the 2026-07 redesign moved that tuning into its own top-level
    // "Debug" sidebar category and stopped consulting the flag, after which NOTHING in the mod read
    // it — a bound entry whose only remaining effect was one dead line in the config file. It is not
    // re-bound here on purpose: an unbound key is simply dropped from the user's cfg on the next save,
    // and there is nothing for it to switch any more.

    // ---- Per-board board-element tuning (in-VR debug menu, indexed by (int)ControlBoard) ----
    // These REPLACE the old unreliable raycast auto-seating (PlayTray.SeatOnBoardFace /
    // ReseatProud, both since deleted — they had no callers left):
    // every board-attached element seats at anchor + PER-BOARD offset, so the depth is
    // PREDICTABLE (no more −50 mm surprises) and dial-able PER BOARD from the debug menu.
    // Offset convention: X/Y lie in the board plane, Z is the "proud" depth toward the
    // player (the board's −Z face) — NEGATIVE Z = toward the player (prouder). Seeded from
    // the current global Oak values so Oak keeps today's look minus the raycast wobble.
    private static readonly ConfigEntry<Vector3>[] _restButtonOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _restButtonDiameter = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector3>[] _confirmUndoOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _confirmUndoSize = new ConfigEntry<float>[3];
    // Item-use clip-in slot (items rework): a card-sized recess UNDER the board next to the
    // Confirm/Undo decision buttons — dropping a held, usable item card into it USES the item.
    // Per-board offset, mirrors ConfirmUndoOffset's binding/accessor/debug-menu wiring exactly.
    private static readonly ConfigEntry<Vector3>[] _itemUseSlotOffset = new ConfigEntry<Vector3>[3];
    // Item-card fan offset (items rework, req #2): nudges the ITEM pile fan + held item card pose
    // INDEPENDENTLY of the ability-card fan (item cards are a different, near-square shape). Per-board
    // Vector3, mirrors ConfirmUndoOffset's binding/accessor/debug-menu wiring. Read live by ItemsPile.
    private static readonly ConfigEntry<Vector3>[] _itemCardOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _slotOverlayOffset = new ConfigEntry<Vector3>[3];
    // Item 1: the two slot overlays move together as a PAIR (Overlays element); this spacing
    // spreads them apart along the inter-slot (board long) axis — slot 0 by −½, slot 1 by +½.
    private static readonly ConfigEntry<float>[] _slotOverlaySpacing = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector3>[] _initiativeOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _pickBannerOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _decisionGap = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _boardTilt = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _boardPitchMin = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _boardPitchMax = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector3>[] _assetOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _assetRotation = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _assetPitch = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _assetYaw = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _assetRoll = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _boardYaw = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _boardScale = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector3>[] _boardPosOffset = new ConfigEntry<Vector3>[3];

    // ---- Per-board GROUP spacing + SHAPE + Active/Piles tuning (in-VR debug menu, round 2) ----
    // Every group's inter-element GAP is now dial-able (rest disc gap, confirm/undo gap, the two
    // pile stacks' gap, the active grid col/row step), each button GROUP carries a per-board SHAPE
    // (Round/Square), and the Active area + the Discard/Burn piles have their own per-board
    // offset/scale. All seeded so the current look is unchanged until tuned.
    private static readonly ConfigEntry<float>[] _restButtonSpacing = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _genericButtonSpacing = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<ButtonShape>[] _restButtonShape = new ConfigEntry<ButtonShape>[3];
    private static readonly ConfigEntry<ButtonShape>[] _genericButtonShape = new ConfigEntry<ButtonShape>[3];
    private static readonly ConfigEntry<Vector3>[] _activeOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _activeCardScale = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector2>[] _activeGridSpacing = new ConfigEntry<Vector2>[3];
    private static readonly ConfigEntry<Vector3>[] _pileOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _pileScale = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<float>[] _pileSpacing = new ConfigEntry<float>[3];

    // ---- Per-board tuning for the remaining board-attached elements (items 4/6: "Aufgaben"
    // objectives dock, "Elemente" infusion dock, VR-settings gear, follow/pin toggle, round
    // readout, turn-flow ButtonCluster). Each carries a per-board offset ADDED on top of its
    // fixed base position, and the docked panels/cluster carry a size MULTIPLIER too, so every
    // element hanging on the control board is dial-able per board from the debug menu. All
    // seeded so today's look is unchanged until tuned. ----
    private static readonly ConfigEntry<Vector3>[] _objectivesOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _objectivesScale = new ConfigEntry<float>[3];
    // Task-panel WIDTH (user request): a multiplier on the objectives dock's WRAP COLUMN
    // (PlayTray.ObjectivesMountWidth) so the scenario task + its progress bar render longer/wider.
    // Strictly orthogonal to _objectivesScale above: width re-wraps the text, scale zooms it — see
    // ObjectivesSurface.FitWidthToMount for why the two used to drag each other.
    private static readonly ConfigEntry<float>[] _objectivesWidth = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector3>[] _elementsOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _elementsScale = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector3>[] _pinOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _readoutOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _clusterOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _clusterScale = new ConfigEntry<float>[3];
    // Item C: the shared DECISION DOCK (the interactive widget row of any in-scenario decision
    // prompt — take-damage burn choice, burn-confirm dialog — that hangs below the board). Offset
    // ADDED on top of its fixed base + a size MULTIPLIER, so the text+buttons under the board dial
    // per board like every other element.
    private static readonly ConfigEntry<Vector3>[] _decisionOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<float>[] _decisionScale = new ConfigEntry<float>[3];

    // ---- Demeo-parity fan/grab tuning (test #22 blueprint DEMEO-HANDS-CARDS.md) ----

    /// <summary>G1: scale fan arch + per-card tilt by how full the hand is (Demeo CardHandView.cs:814).</summary>
    internal static ConfigEntry<bool> FanCurveByFill = null!;

    /// <summary>G1: hand size at which the fan reaches its full curvature (fill = n / this, clamped 0..1).</summary>
    internal static ConfigEntry<int> FanMaxHandForCurve = null!;

    /// <summary>G1: full-curvature vertical-arch factor (the pre-Demeo constant curvature, now the fill=1 target).</summary>
    internal static ConfigEntry<float> FanFlatCurvatureFactor = null!;

    /// <summary>G1: full-curvature per-card Z-tilt factor (fill=1 target).</summary>
    internal static ConfigEntry<float> FanTiltFactor = null!;

    /// <summary>G2: sideways slide (real meters) applied to the fan's cards to split apart around the hovered one.</summary>
    internal static ConfigEntry<float> FanSplitMultiplier = null!;

    /// <summary>G2: how fast the neighbor split decays with index distance from the hovered card (higher = only nearest neighbors move). Coded-curve substitute for Demeo's serialized AnimationCurve.</summary>
    internal static ConfigEntry<float> FanSplitFalloff = null!;

    /// <summary>G2: forward pop (real meters, along -face-normal toward the viewer) of the hovered/selected card.</summary>
    internal static ConfigEntry<float> FanSelectedPopForward = null!;

    /// <summary>G3: which controller button grabs a card. Demeo = Trigger (index); our legacy = Grip.</summary>
    internal static ConfigEntry<CardGrabButton> GrabButton = null!;

    /// <summary>G4: eased dead-zoned fan follow rate (1/s exponential). 0 = rigidly parented to the palm (pre-Demeo). >0 = Demeo-style eased follow.</summary>
    internal static ConfigEntry<float> FanFollowSmoothing = null!;

    /// <summary>G4: fan-follow dead zone in real meters — the fan only chases the palm once it drifts past this (Demeo ViewHelper minDistanceToMove).</summary>
    internal static ConfigEntry<float> FanFollowDeadzone = null!;

    /// <summary>G5: suppress the reveal gate on the hand that is currently grabbing something (Demeo CardHandController.cs:475).</summary>
    internal static ConfigEntry<bool> RevealIgnoreWhenGrabbing = null!;

    // ---- Demeo-style fan-out reveal animation + sound (fan-reveal parity pass) ----

    /// <summary>Fan-out reveal animation length in seconds (unscaled time). 0 = instant (pre-animation behavior).</summary>
    internal static ConfigEntry<float> FanOpenDuration = null!;

    /// <summary>Extra per-card delay (seconds) by distance from the fan center — a tiny outward ripple on reveal.</summary>
    internal static ConfigEntry<float> FanOpenStagger = null!;

    /// <summary>Quick-collapse animation length in seconds on hide (unscaled time). 0 = the fan vanishes instantly.</summary>
    internal static ConfigEntry<float> FanCloseDuration = null!;

    /// <summary>Game audio item played once when the fan reveals ("" = silent).</summary>
    internal static ConfigEntry<string> FanRevealSound = null!;

    /// <summary>Game audio item played once when the fan hides ("" = silent).</summary>
    internal static ConfigEntry<string> FanHideSound = null!;

    // ---- Card interaction sounds (more-card-sounds pass) ----

    /// <summary>Game audio item played once when a card is GRABBED from the fan/tray/piles ("" = silent).</summary>
    internal static ConfigEntry<string> CardGrabSound = null!;

    /// <summary>Game audio item played once when a card is PLACED into a board slot / pick field ("" = silent).</summary>
    internal static ConfigEntry<string> CardPlaceSound = null!;

    /// <summary>Game audio item played once when a slotted/picked card is TAKEN BACK to the fan ("" = silent).</summary>
    internal static ConfigEntry<string> CardTakeBackSound = null!;

    // ---- GLOBAL hand-fan geometry (in-VR debug menu, "Fan" category) ----------------------
    // Item 8's four LOCAL consts in CardFan (per-card step cap 14°, arc scale ×1.3, radius scale
    // ×1.12, hover-split scale ×1.45) are now LIVE-TUNABLE global ConfigEntries so the hand fan's
    // width/roundness/spacing can be dialed in-VR. GLOBAL (not per-board): the palm fan is the
    // same for every control board. Seeded to the CURRENT effective values so the fan is unchanged
    // until tuned. CardFan.Relayout/SplitOffset read these instead of the removed consts, keeping
    // the count-scaling: small hands stay narrow (step cap), big hands round out (arc cap).

    /// <summary>Fan (global): per-card angular step cap in degrees — the dominant knob for small/medium hands (each added card fans out this far until the total sweep hits the arc cap).</summary>
    internal static ConfigEntry<float> FanPerCardStepDegrees = null!;

    /// <summary>Fan (global): total fan arc sweep in degrees — governs big hands (how far a full hand wraps once the step cap is reached). Effective total arc; seeded to the old FanArcDegrees × 1.3.</summary>
    internal static ConfigEntry<float> FanArcSweepDegrees = null!;

    /// <summary>Fan (global): effective hand-fan arc radius in real meters (seeded to the old FanRadius × 1.12). Opens real space between card centers as it grows.</summary>
    internal static ConfigEntry<float> FanEffectiveRadius = null!;

    /// <summary>Fan (global): hover-split scale — multiplies FanSplitMultiplier so the hover gap stays proportional to the (wider) card spacing. Seeded to the old local 1.45.</summary>
    internal static ConfigEntry<float> FanHoverSplitScale = null!;

    /// <summary>
    /// Browse fan (global): offset ADDED to the poke-toggle pile BROWSE fan's fixed above-board
    /// anchor, board-local meters (in-VR debug menu, Piles element). Z NEGATIVE = toward the player.
    /// </summary>
    internal static ConfigEntry<Vector3> BrowseFanOffset = null!;

    /// <summary>Fan depth curvature (global): signed max bow in real meters of the OUTERMOST card along
    /// the fan-local forward axis so a full hand bows into depth like a real held fan. POSITIVE = edge
    /// cards recede AWAY from the viewer (fan-local +Z, center card nearest); NEGATIVE = edge cards bow
    /// TOWARD the viewer (the other direction). 0 = flat (the old billboarded sheet).</summary>
    internal static ConfigEntry<float> FanSideDepthCurve = null!;

    /// <summary>Fan depth curvature (global): exponent of the fraction-from-center curve. 2 = quadratic
    /// (gentle near center, steep at the edges); 1 = linear wedge; higher = flatter middle, sharper edges.</summary>
    internal static ConfigEntry<float> FanCurvePower = null!;

    /// <summary>Fan depth curvature (global): card count below which the fan stays FLAT (no depth bow).
    /// The bow ramps in from here up to FanMaxHandForCurve so a small hand is nearly flat.</summary>
    internal static ConfigEntry<int> FanCurveMinCards = null!;

    /// <summary>Fan facing (global): enable the gaze-responsive yaw bias (opt-in). Default OFF — the fan
    /// billboards steadily and the depth curvature is the primary shape. ON adds a hysteresis-gated yaw
    /// toward the head's gaze so an edge card tips forward when you look at it (no center dither).
    /// SUPERSEDED by FanFaceViewer + FanGazeApexFollow (see CardFan's "card presentation" region); kept
    /// for config compatibility and as an extra flourish for anyone who liked the whole-fan lean.</summary>
    internal static ConfigEntry<bool> FanGazeBias = null!;

    /// <summary>Fan facing (global): per-card TOE-IN toward the head, 0..1. 0 = every card keeps the
    /// fan's single billboard normal (the old flat sheet, so an outer card is seen obliquely); 1 = each
    /// card is aimed at the head individually, like cupping a real hand of cards so every card faces
    /// your eyes. Purely orientation — positions, hit rects and draw order are untouched.</summary>
    internal static ConfigEntry<float> FanFaceViewer = null!;

    /// <summary>Fan facing (global): how far the gaze RELIEVES the depth bow, 0..1. 0 = the plain
    /// symmetric bow (the card you turn to look at is the one that has receded most); 1 = the card under
    /// your gaze (and, tapering off, its neighbours) comes fully out of the recession. Every other card
    /// keeps at most the recession it has at 0, so nothing can ever get worse than the plain bow.</summary>
    internal static ConfigEntry<float> FanGazeApexFollow = null!;

    /// <summary>Fan facing (global): exponential ease rate (1/s) of the gaze apex toward the looked-at
    /// card. Low = lazy/heavy, high = snappy (and more head-jitter sensitive).</summary>
    internal static ConfigEntry<float> FanGazeSmoothing = null!;

    /// <summary>
    /// The shipped per-board element layout, indexed by <c>(int)ControlBoard</c>: Oak, Steel, Bronze.
    ///
    /// <para>These are MEASURED, not derived. Every board used to share one default seeded from Oak,
    /// which meant Steel and Bronze arrived with Oak's element placement on a board of a different
    /// size and shape — rest discs off their notches, the initiative track floating at the wrong
    /// height — until someone dialled each one in from the debug menu. The values here are those
    /// dial-ins, so a fresh install gets a board that is already seated.</para>
    ///
    /// <para>Only the properties that genuinely differ per board are tabled; the rest keep a single
    /// literal at their bind, because a table whose three entries are equal only invites them to
    /// drift apart.</para>
    ///
    /// <para>WHY `internal` AND NOT `private`: a REMOTE player's board (<c>Net/RemoteBoardLayout</c>)
    /// has to seat the very same elements on the very same board, keyed by the style that peer
    /// SYNCED — and it cannot read their <c>ConfigEntry</c>s, because a player's own dial-ins never
    /// ride the wire. It therefore needs exactly this table: the SHIPPED per-board layout, which
    /// every client compiles in identically. Reading it directly is what keeps the two boards from
    /// drifting; the alternative was a second copy of these numbers in <c>Net/</c>, i.e. the very
    /// failure mode <c>scripts/check-mirrors.sh</c> exists to catch.</para>
    /// </summary>
    internal static class BoardDefaults
    {
        internal static readonly Vector3[] RestButtonOffset = { Defaults.RestButtonOffset_Oak, Defaults.RestButtonOffset_Steel, Defaults.RestButtonOffset_Bronze };
        internal static readonly float[] RestButtonDiameter = { Defaults.RestButtonDiameter_Oak, Defaults.RestButtonDiameter_Steel, Defaults.RestButtonDiameter_Bronze };
        internal static readonly Vector3[] ConfirmUndoOffset = { Defaults.ConfirmUndoOffset_Oak, Defaults.ConfirmUndoOffset_Steel, Defaults.ConfirmUndoOffset_Bronze };
        internal static readonly float[] ConfirmUndoSize = { Defaults.ConfirmUndoSize_Oak, Defaults.ConfirmUndoSize_Steel, Defaults.ConfirmUndoSize_Bronze };
        internal static readonly Vector3[] SlotOverlayOffset = { Defaults.SlotOverlayOffset_Oak, Defaults.SlotOverlayOffset_Steel, Defaults.SlotOverlayOffset_Bronze };
        internal static readonly float[] SlotOverlaySpacing = { Defaults.SlotOverlaySpacing_Oak, Defaults.SlotOverlaySpacing_Steel, Defaults.SlotOverlaySpacing_Bronze };
        internal static readonly Vector3[] InitiativeOffset = { Defaults.InitiativeOffset_Oak, Defaults.InitiativeOffset_Steel, Defaults.InitiativeOffset_Bronze };
        internal static readonly float[] DecisionGap = { Defaults.DecisionGap_Oak, Defaults.DecisionGap_Steel, Defaults.DecisionGap_Bronze };
        internal static readonly Vector3[] PickBannerOffset = { Defaults.PickBannerOffset_Oak, Defaults.PickBannerOffset_Steel, Defaults.PickBannerOffset_Bronze };
        internal static readonly float[] RestButtonSpacing = { Defaults.RestButtonSpacing_Oak, Defaults.RestButtonSpacing_Steel, Defaults.RestButtonSpacing_Bronze };
        internal static readonly float[] GenericButtonSpacing = { Defaults.GenericButtonSpacing_Oak, Defaults.GenericButtonSpacing_Steel, Defaults.GenericButtonSpacing_Bronze };
        internal static readonly Vector3[] ActiveOffset = { Defaults.ActiveOffset_Oak, Defaults.ActiveOffset_Steel, Defaults.ActiveOffset_Bronze };
        internal static readonly float[] ActiveCardScale = { Defaults.ActiveCardScale_Oak, Defaults.ActiveCardScale_Steel, Defaults.ActiveCardScale_Bronze };
        internal static readonly Vector3[] PileOffset = { Defaults.PileOffset_Oak, Defaults.PileOffset_Steel, Defaults.PileOffset_Bronze };
        internal static readonly Vector3[] ObjectivesOffset = { Defaults.ObjectivesOffset_Oak, Defaults.ObjectivesOffset_Steel, Defaults.ObjectivesOffset_Bronze };
        internal static readonly float[] ObjectivesScale = { Defaults.ObjectivesScale_Oak, Defaults.ObjectivesScale_Steel, Defaults.ObjectivesScale_Bronze };
        internal static readonly float[] ObjectivesWidth = { Defaults.ObjectivesWidth_Oak, Defaults.ObjectivesWidth_Steel, Defaults.ObjectivesWidth_Bronze };
        internal static readonly Vector3[] ElementsOffset = { Defaults.ElementsOffset_Oak, Defaults.ElementsOffset_Steel, Defaults.ElementsOffset_Bronze };
        internal static readonly Vector3[] PinOffset = { Defaults.PinOffset_Oak, Defaults.PinOffset_Steel, Defaults.PinOffset_Bronze };
        internal static readonly Vector3[] DecisionOffset = { Defaults.DecisionOffset_Oak, Defaults.DecisionOffset_Steel, Defaults.DecisionOffset_Bronze };
        internal static readonly Vector3[] ReadoutOffset = { Defaults.ReadoutOffset_Oak, Defaults.ReadoutOffset_Steel, Defaults.ReadoutOffset_Bronze };
        internal static readonly Vector3[] AssetOffset = { Defaults.AssetOffset_Oak, Defaults.AssetOffset_Steel, Defaults.AssetOffset_Bronze };
        internal static readonly float[] AssetPitchDegrees = { Defaults.AssetPitchDegrees_Oak, Defaults.AssetPitchDegrees_Steel, Defaults.AssetPitchDegrees_Bronze };
    }

    internal static void Bind()
    {
        if (_file != null)
            return;

        // THROUGH ModuleConfig, not a raw ConfigFile. This file was the one module that created its
        // own and never registered it, so every [Cards] setting — the tray, the fan, the reveal
        // gesture, the board choice — was invisible to ConfigCatalog: absent from the in-VR config
        // browser all along, and the reason the new options tab's "Karten & Brett" section came up
        // empty. Same path, same contents; only the registration is new.
        _file = ModuleConfig.Create("cards");

        DevFakeHand = _file.Bind("Cards", "DevFakeHand", Defaults.DevFakeHand,
            "Spawn this many dummy VR cards (procedural placeholder faces) so the fan/tray/grab " +
            "mechanics are exercisable without a scenario. Requires [Dev] Enabled (+ SimulateHands " +
            "or a real HMD). 0 = off.");
        RevealMode = _file.Bind("Cards", "RevealMode", Defaults.RevealMode,
            "How the palm fan reveals. 'tilt' = Demeo-style wrist SUPINATION on the non-dominant " +
            "hand: turning the palm up / toward you, measured on the ROLL axis alone at ANY arm " +
            "pitch — even fingers straight up, a wrist twist reveals (roll gate v4, hardware " +
            "test #10 + round 4). 'always' = the fan is out whenever a card phase has cards, " +
            "no gesture at all.");
        RevealEnterDegrees = _file.Bind("Cards", "RevealEnterDegrees", Defaults.RevealEnterDegrees,
            new ConfigDescription(
                "RevealMode=tilt: hand ROLL in DEGREES above which the fan OPENS. Roll gate " +
                "v4 measures TRUE wrist roll via a parallel-transported reference: the " +
                "signed twist of the back-of-hand around the finger axis, drift-anchored to " +
                "world up whenever the fingers are off vertical. Pitching or pointing the " +
                "arm — including straight UP — cannot move the measure; only rolling the " +
                "wrist does. Measured on the VISIBLE hand (after the debug-menu hand seat " +
                "offsets/per-style trims). Scale: 0 = knuckles-up flat hand, 90 = palm " +
                "fully rolled toward the face (negative = rolled the other way, which never " +
                "opens the fan). Default 60 = a comfortable supination well past vertical " +
                "(Demeo's own threshold is ~37°). NOTE: the scale CHANGED from the old v2 " +
                "measure (whose default was 95 on a 0-180 scale) — old out-of-range values " +
                "are auto-reset once. Live-tunable from the in-VR debug menu (Fan category).",
                new AcceptableValueRange<float>(15f, 85f)));
        RevealExitDegrees = _file.Bind("Cards", "RevealExitDegrees", Defaults.RevealExitDegrees,
            new ConfigDescription(
                "RevealMode=tilt: hand roll in DEGREES below which the fan CLOSES (same " +
                "roll scale as RevealEnterDegrees: 0 = flat, 90 = palm fully toward " +
                "the face). The 15° default dead band under the 60° enter keeps the gate from " +
                "chattering at the boundary; the gate always clamps this below " +
                "RevealEnterDegrees. Live-tunable from the in-VR debug menu (Fan category).",
                new AcceptableValueRange<float>(5f, 80f)));
        // One-time migration (roll gate v3): the v2 measure ran on a 0-180° scale with 95/80
        // defaults; the Demeo measure caps at 90° and defaults 60/45. Old cfg values above the
        // new range maxima are clamped by BepInEx on load (95 → 85, exits ≥ 80 → 80), so the
        // old-scale SIGNATURE is enter pinned at the 85° max AND exit ≥ 75° — a pair that is
        // physically absurd on the new scale (near-full crank enter with a barely-lower exit)
        // but exactly what any old-scale config lands on. Requiring BOTH avoids eating a
        // deliberately step-maxed new-scale enter from the in-VR panel. Reset both to the new
        // defaults (persisted immediately by BepInEx).
        if (RevealEnterDegrees.Value >= 84.9f && RevealExitDegrees.Value >= 75f)
        {
            RevealEnterDegrees.Value = 60f;
            RevealExitDegrees.Value = 45f;
        }
        FanRadius = _file.Bind("Cards", "FanRadius", Defaults.FanRadius,
            "Palm fan arc radius in real-world meters (diorama scale is applied automatically).");
        FanArcDegrees = _file.Bind("Cards", "FanArcDegrees", Defaults.FanArcDegrees,
            "LEGACY — no effect, superseded by FanArcSweepDegrees. Nothing reads this value. It was " +
            "the maximum total fan arc in degrees; FanArcSweepDegrees replaced it and was seeded to " +
            "this default × 1.3 (= 91°). Kept bound so existing cfg files load unchanged — an " +
            "unbound key is silently dropped from your file on the next save.");
        FanPalmOffset = _file.Bind("Cards", "FanPalmOffset", Defaults.FanPalmOffset,
            "Height of the fan pivot above the palm center, real-world meters.");
        CardWidth = _file.Bind("Cards", "CardWidth", Defaults.CardWidth,
            "Physical card width in meters (real poker card = 0.0635). Height keeps the 63.5:88 aspect.");
        InspectScale = _file.Bind("Cards", "InspectScale", Defaults.InspectScale,
            "Scale multiplier applied to a card while it is held (natural-size inspection).");
        HeldTiltDegrees = _file.Bind("Cards", "HeldTiltDegrees", Defaults.Cards_HeldTiltDegrees,
            "LEGACY — no effect, superseded by HeldFaceBias. Nothing reads this value (hardware " +
            "test #13). The old palm-aligned held pose required a hard supination to read the card; " +
            "the pose is now controlled by HeldFaceBias instead. Kept bound only so existing config " +
            "files load cleanly. NOTE: [FigureGrab] HeldTiltDegrees is a DIFFERENT, live entry.");
        HeldFaceBias = _file.Bind("Cards", "HeldFaceBias", Defaults.HeldFaceBias,
            "Held card readability (test #13): degrees the card FACE leans from 'flat on the " +
            "palm' (0 = old pose, face along the palm normal — readable only by twisting the " +
            "wrist) back toward the wrist/forearm. In a relaxed controller grip (grip pose " +
            "pitched, see [Hands] GripPitchOffsetDegrees) the fingers point forward " +
            "and slightly down, so at ~65 the face points up/back at your eyes — like really " +
            "holding a playing card. The card top points to the thumb side (which is world-up " +
            "in a relaxed grip; mirrored automatically for the left hand). The card still " +
            "follows the wrist 1:1 — this is a fixed bias, NOT per-frame auto-facing.");
        HeldForward = _file.Bind("Cards", "HeldForward", Defaults.HeldForward,
            "Held card FALLBACK (only used when the hand rig has no finger joints): " +
            "pinch-point offset from the grab anchor along the fingers, meters.");
        HeldOffPalm = _file.Bind("Cards", "HeldOffPalm", Defaults.HeldOffPalm,
            "Held card FALLBACK (only used when the hand rig has no finger joints): " +
            "pinch-point offset off the palm surface, meters.");
        HeldPinchOffset = _file.Bind("Cards", "HeldPinchOffset", Defaults.HeldPinchOffset,
            "Held card fine-tune: offset (meters) ADDED to the computed pinch point — " +
            "the midpoint between the thumb tip and index tip at grab time — in " +
            "GrabAnchor-local axes: +Y out of the palm, +Z along the fingers, +X " +
            "sideways (anatomically mirrored between hands). Example {x:0, y:0.01, " +
            "z:0.02} lifts the card 1 cm off the palm and shifts it 2 cm toward the " +
            "fingertips.");
        TrayForward = _file.Bind("Cards", "TrayForward", Defaults.TrayForward,
            "Control board placement: forward distance from the head at placement time, meters.");
        TrayDown = _file.Bind("Cards", "TrayDown", Defaults.TrayDown,
            "Control board placement: drop below eye height, meters (0.35 ~ chest height).");
        TrayRight = _file.Bind("Cards", "TrayRight", Defaults.TrayRight,
            "Control board placement: sideways offset (+right), meters.");
        TrayTilt = _file.Bind("Cards", "TrayTilt", Defaults.TrayTilt,
            "LEGACY — no effect, superseded by the per-board BoardTilt_<board>. Nothing reads this " +
            "value. It was the control board tilt in degrees FROM HORIZONTAL toward the player (0 = " +
            "flat like a desk, 90 = upright panel); BoardTilt_<board> replaced it in the pose math " +
            "and was seeded to 30 so Oak is unchanged. Tune BoardTilt_<board> instead. Kept bound so " +
            "existing cfg files load unchanged.");
        TrayYaw = _file.Bind("Cards", "TrayYaw", Defaults.TrayYaw,
            "Control board yaw relative to the head's flat forward at placement time, degrees. " +
            "Written automatically when you grip-move the tray by its handle bar; edit only to reset.");
        TrayScale = _file.Bind("Cards", "TrayScale", Defaults.TrayScale,
            "Control board size multiplier (0.5–2). Written automatically by the two-handed " +
            "tray grab (grip the handle bar with both hands and spread/pinch); edit only to reset.");
        TrayFollow = _file.Bind("Cards", "TrayFollow", Defaults.TrayFollow,
            "Tray anchor mode (test #15, toggled by the pin button on the tray frame). " +
            "true = the tray is rig-anchored: it moves with you (world grab, snap turn, " +
            "recenter) and re-places itself at the TrayForward/Down/Right offsets on mode " +
            "entry. false = the tray is PINNED where you left it, world-anchored — it " +
            "stays put while you move around and never re-places itself. Switching back " +
            "to follow re-anchors it at the configured offsets.");
        BoardMoveMode = _file.Bind("Cards", "BoardMoveMode", Defaults.BoardMoveMode,
            "Item 12: how the handle-bar grab may MOVE the control board. Limited (default) = " +
            "today's behavior: position + yaw only, the board is kept level for you (under the " +
            "world tilt 'level' means level in YOUR view, not the world's). LimitedPitch = like " +
            "Limited, plus the grab may PITCH the board toward/away from you, clamped to the " +
            "per-board BoardPitchMin_<board>..BoardPitchMax_<board> window. Free = the board follows the " +
            "grabbing hand in ALL axes 1:1 — no leveling, no clamps (it CAN end up upside down; " +
            "switching back to a Limited mode re-levels it). Selectable in the VR settings " +
            "(Tafeln → Karten & Brett) with localized labels; local cosmetics only — peers just " +
            "see the resulting board pose, exactly as before.");
        TrayPitch = _file.Bind("Cards", "TrayPitch", Defaults.TrayPitch,
            "Item 12: the grab-authored board pitch, degrees ADDED to the per-board BoardTilt_<board> " +
            "(positive = more upright toward you). Written automatically when you release the handle " +
            "bar in the LimitedPitch or Free movement mode, so the pitch survives re-placements and " +
            "sessions; ignored in the Limited mode (which always uses BoardTilt alone). Edit only to " +
            "reset. Clamped to the per-board BoardPitchMin_<board>..BoardPitchMax_<board> window when " +
            "applied in LimitedPitch.");
        BoardPitchMinDegrees = _file.Bind("Cards", "BoardPitchMinDegrees", Defaults.BoardPitchMinDegrees,
            "LEGACY — no effect, superseded by the per-board BoardPitchMin_<board>. Nothing reads " +
            "this value. It was the GLOBAL lower edge of the LimitedPitch grab-pitch window, " +
            "degrees relative to BoardTilt; BoardPitchMin_<board> replaced it in the clamp math " +
            "and was seeded to −45 so behavior is unchanged. Tune BoardPitchMin_<board> instead. " +
            "Kept bound so existing cfg files load unchanged.");
        BoardPitchMaxDegrees = _file.Bind("Cards", "BoardPitchMaxDegrees", Defaults.BoardPitchMaxDegrees,
            "LEGACY — no effect, superseded by the per-board BoardPitchMax_<board>. Nothing reads " +
            "this value. It was the GLOBAL upper edge of the LimitedPitch grab-pitch window, " +
            "degrees relative to BoardTilt; BoardPitchMax_<board> replaced it in the clamp math " +
            "and was seeded to 45 so behavior is unchanged. Tune BoardPitchMax_<board> instead. " +
            "Kept bound so existing cfg files load unchanged.");
        CardLerpSpeed = _file.Bind("Cards", "CardLerpSpeed", Defaults.CardLerpSpeed,
            "Card fly animation speed (exponential smoothing constant, 1/s).");
        SlotCardInset = _file.Bind("Cards", "SlotCardInset", Defaults.SlotCardInset,
            "How far a card is lifted OUT of a physical slot recess toward the viewer, real meters " +
            "(the board's -Z face). BuildBoard now projects the slot anchors onto the recess FLOOR, so " +
            "a card at 0 sits deep inside the recess and reads as 'poking through' — barely visible from " +
            "the top. This lifts it up to (roughly) the recess rim so it rests visibly ON the board's top " +
            "surface facing the player. Raise it if cards still look sunken, lower it if they float. Applies to played " +
            "cards, docked action cards and the single-card pick/short-rest layouts alike. Does NOT " +
            "change the card width/height (a separate pass aligns the recess to the card).");
        SlotCardFill = _file.Bind("Cards", "SlotCardFill", Defaults.SlotCardFill,
            "Item 3: how much a card laid in a board slot scales UP to fill the physical slot " +
            "recess. Multiplies the card's in-slot size (on top of the slot frame's own 1.3x " +
            "SlotScale). 1.0 = the pre-fix size (visibly smaller than the recess). PER-BOARD: " +
            "raise toward the recess/card ratio of the ACTIVE control-board asset until the card " +
            "nearly fills the recess without overflowing the rim; the default is tuned for the " +
            "current bundled PlayTray. Applies to played cards, single-card pick candidates and " +
            "docked action cards alike. Does NOT change the recess or the card's slot seating depth " +
            "(that is SlotCardInset).");
        RoundButtonDiameter = _file.Bind("Cards", "RoundButtonDiameter", Defaults.RoundButtonDiameter,
            "LEGACY — no effect, superseded by the per-board RestButtonDiameter_<board>. Nothing " +
            "reads this value: the per-board descriptor this entry's old text called 'a future' one " +
            "already exists and already wins (RestControls.EnsureBuilt reads " +
            "RestButtonDiameter_<board>). It was the diameter (real meters) of the ROUND short-rest / " +
            "long-rest discs that seat in the board's two round rest-notches. Tune " +
            "RestButtonDiameter_<board> instead — same meaning, same 0.105 default. Kept bound so " +
            "existing cfg files load unchanged.");
        RoundButtonThickness = _file.Bind("Cards", "RoundButtonThickness", Defaults.RoundButtonThickness,
            "LEGACY — no effect, superseded by [RestButtons] Depth. Nothing reads this value; the " +
            "live one is ButtonTuning.RestCapDepth, which defaults to this exact 0.012 so the look " +
            "is unchanged. It was the thickness (real meters) of the round rest-button puck along " +
            "the press axis. Tune [RestButtons] Depth instead. Kept bound so existing cfg files " +
            "load unchanged.");
        RestButtonInsetX = _file.Bind("Cards", "RestButtonInsetX", Defaults.RestButtonInsetX,
            "LEGACY — no effect, superseded by the per-board RestButtonOffset_<board>. Nothing reads " +
            "this value: RestControls.EnsureBuilt reads RestButtonOffset_<board>, whose X carries the " +
            "same nudge (and whose Z adds the proud depth the old raycast seat used to guess). Dialling " +
            "this key changes NOTHING, whatever this description used to promise. It was the sideways " +
            "nudge of the round short/long-rest discs along the rest anchor's LOCAL X, real meters: " +
            "POSITIVE = toward the board CENTER, NEGATIVE = toward the board EDGE, and the correct sign " +
            "depends on the board frame. That direction convention still applies — to " +
            "RestButtonOffset_<board>.X, which is seeded from this 0.024 Oak value. Kept bound so " +
            "existing cfg files load unchanged.");
        ConfirmUndoInsetX = _file.Bind("Cards", "ConfirmUndoInsetX", Defaults.ConfirmUndoInsetX,
            "LEGACY — no effect, superseded by the per-board ConfirmUndoOffset_<board>. Nothing reads " +
            "this value; despite the 'PER-BOARD' this description used to end on, it is a single " +
            "global. ConfirmUndoOffset_<board> is the per-board one PlayTray.BuildButtons actually " +
            "reads, seeded from this Oak value as X = −0.014. It was the inward nudge in local X (real " +
            "meters, toward board center) applied to Confirm/Undo so they center on the Oak metal pads. " +
            "Kept bound so existing cfg files load unchanged.");
        SpawnLeftOfHead = _file.Bind("Cards", "SpawnLeftOfHead", Defaults.SpawnLeftOfHead,
            "Seat the control board BESIDE YOUR HEAD ON THE LEFT the first time it is placed in a " +
            "scenario, instead of wherever you last dragged it. The saved layout is what you get " +
            "for the rest of the session (moving the board still persists as before) — this only " +
            "makes the STARTING spot the same every time, so you always know where to reach for " +
            "it. The three Spawn… distances below define that spot.");
        SpawnSideMeters = _file.Bind("Cards", "SpawnSideMeters", Defaults.SpawnSideMeters,
            new ConfigDescription(
                "First-placement seat: how far to your LEFT the board sits, real meters.",
                new AcceptableValueRange<float>(0f, 1.2f)));
        SpawnForwardMeters = _file.Bind("Cards", "SpawnForwardMeters", Defaults.SpawnForwardMeters,
            new ConfigDescription(
                "First-placement seat: how far IN FRONT of you the board sits, real meters. Small " +
                "on purpose — 'beside you', not 'in front of you'.",
                new AcceptableValueRange<float>(-0.5f, 1.5f)));
        SpawnDownMeters = _file.Bind("Cards", "SpawnDownMeters", Defaults.SpawnDownMeters,
            new ConfigDescription(
                "First-placement seat: how far BELOW eye level the board sits, real meters.",
                new AcceptableValueRange<float>(-0.5f, 1.5f)));
        GameCardParticles = _file.Bind("Cards", "GameCardParticles", Defaults.GameCardParticles,
            "Let the GAME's own card particle effect (the CardSmoke spark/smoke plume) play. OFF by " +
            "default: it is authored for the full-size 2D card, so on the table-sized board it " +
            "sprays sparks across the WHOLE play field — most visibly when the played cards are " +
            "swept into their piles at the end of a turn. Suppressed through the game's own " +
            "low-spec switch (LowParticlesUse.NoCardsParticles), so no particle is spawned at all " +
            "and nothing can be mis-scaled; the vanilla value is restored the moment this is " +
            "turned back on. The card's own burn/dissolve artwork is unaffected.");
        CardDust = _file.Bind("Cards", "CardDust", Defaults.CardDust,
            "Burst of dust/spark motes when a card appears or crumbles away. OFF by default: on hardware the burst that goes with a card flying to a discard pile read as a huge spark animation sweeping across the WHOLE board (the motes are emitted in world units and the board is a scaled-up diorama), which is what the player sees rather than the intended small puff at the card. The card still fades and settle-shrinks either way — the dust was only ever a secondary flourish. User ruling 2026-08-03.");
        WantedSlotHint = _file.Bind("Cards", "WantedSlotHint", Defaults.WantedSlotHint,
            "Steady, softly pulsing accent glow on the slot(s) the game is currently waiting to be " +
            "filled (test #28) — distinct from the transient gold snap glow that previews where a " +
            "HELD card will drop. During normal card selection it marks the still-empty play slot(s) " +
            "the round expects a card in; during single-card pick flows (long rest lose-a-card, " +
            "avoid-damage, recover/discard) it marks the left slot. Clears once the requirement is " +
            "met or the flow ends. false = no wanted-slot hint.");
        PileViewer = _file.Bind("Cards", "PileViewer", Defaults.PileViewer,
            "Discard/burnt pile stacks on the control board's right edge (hardware test #21 " +
            "wish): each pile shows as a small physical card stack with a count; poking or " +
            "pinch-grabbing a stack raises a readable browse fan of that pile's cards " +
            "(informational — release/poke again to dismiss). false = no pile furniture at all.");
        ActivePile = _file.Bind("Cards", "ActivePile", Defaults.ActivePile,
            "ACTIVE CARDS area (feature 6): the character's currently-active ability cards " +
            "(round-long or persistent) shown PERMANENTLY as a small column just to the RIGHT " +
            "of the discard/burnt pile stacks. The cards read slightly smaller than the hand " +
            "fan and each stays grabbable so you can pluck one out to read it (it returns to " +
            "the column on release); the active HALF of each card is highlighted. Purely " +
            "informational — grabbing an active card never selects or commits it. Empty when " +
            "no card is active. false = no active-cards area at all.");
        FaceMipBake = _file.Bind("Cards", "FaceMipBake", Defaults.FaceMipBake,
            "Aliasing round 3 (T3): the game ships its card-face sprite atlases WITHOUT mipmaps " +
            "(FACE TEXTURE DIAG: mips=1), so the adopted card faces shimmer under minification " +
            "no matter the MSAA/supersampling level. When true, each unique card-face texture is " +
            "baked ONCE at runtime into a mipmapped trilinear/aniso-8 copy (GPU blit -> readback " +
            "-> mip chain) and the face Images' sprites are swapped to equivalent sprites on the " +
            "baked copy (rect/pivot/border/PPU preserved; originals restored when a face is " +
            "returned to the game). false = leave the game's mipless atlases untouched.");
        Board = _file.Bind("Cards", "Board", Defaults.Board,
            "Which control-board (PlayTray) model to load from the asset bundle — switchable " +
            "live from the VR settings panel. Oak = the original bundled board (default); Steel " +
            "and Bronze are the two new boards. The enum→bundle-path map lives in " +
            "VRCardFactory.GetTrayPrefab. If the selected prefab is not in the bundle yet the " +
            "board falls back to Oak, and if Oak is also missing the procedural board is used, " +
            "so any selection is safe. Changing this tears down and rebuilds the tray live " +
            "(CardsDriver), re-seating the cards on the newly loaded board.");

        // ---- Per-board element tuning (Part A) ----
        // Bound with one entry per board (loop over the ControlBoard enum). Offsets: X/Y in the
        // board plane, Z = proud depth toward the player (NEGATIVE = prouder / closer to you).
        //
        // EVERY BOARD SHIPS ITS OWN LAYOUT NOW (see BoardDefaults). They used to share one default
        // seeded from Oak, so Steel and Bronze started with Oak's element placement on a board of a
        // different size and shape — every element sat wrong until someone dialled it in by hand
        // from the debug menu. The tables below are those dial-ins.
        foreach (ControlBoard board in System.Enum.GetValues(typeof(ControlBoard)))
        {
            int i = (int)board;
            _restButtonOffset[i] = _file.Bind("Cards", $"RestButtonOffset_{board}",
                BoardDefaults.RestButtonOffset[i],
                $"[{board}] ROUND rest-disc offset from the rest anchor, board-local meters. " +
                "X/Y lie in the board plane (+X toward board center), Z = proud depth toward the " +
                "player (NEGATIVE = prouder). Replaces the raycast seat — dial Z until the discs " +
                "rest cleanly in the notches. Ships this board's own measured seat.");
            _restButtonDiameter[i] = _file.Bind("Cards", $"RestButtonDiameter_{board}", BoardDefaults.RestButtonDiameter[i],
                $"[{board}] diameter (meters) of the round short/long-rest discs. Per-board measured.");
            _confirmUndoOffset[i] = _file.Bind("Cards", $"ConfirmUndoOffset_{board}",
                BoardDefaults.ConfirmUndoOffset[i],
                $"[{board}] SQUARE Confirm/Undo offset from their button anchors, board-local meters. " +
                "X/Y in plane (−X toward board center from the right column), Z = proud depth toward " +
                "the player (NEGATIVE = prouder). Ships this board's own measured seat.");
            _confirmUndoSize[i] = _file.Bind("Cards", $"ConfirmUndoSize_{board}", BoardDefaults.ConfirmUndoSize[i],
                $"[{board}] side length (meters) of the square Confirm/Undo buttons. Per-board measured.");
            _itemUseSlotOffset[i] = _file.Bind("Cards", $"ItemUseSlotOffset_{board}", Defaults.ItemUseSlotOffset_ByBoard[i],
                $"[{board}] offset ADDED to the ITEM-USE clip-in slot local position (on top of its fixed " +
                "base UNDER the board next to the Confirm/Undo buttons), board-local meters. X/Y in plane, " +
                "Z = proud depth toward the player (NEGATIVE = prouder). Drop a held usable item card into " +
                "this slot to USE it. Seeded 0 (Oak).");
            _itemCardOffset[i] = _file.Bind("Cards", $"ItemCardOffset_{board}", Defaults.ItemCardOffset_ByBoard[i],
                $"[{board}] offset ADDED to the ITEM pile fan + the held item-card pose, board-local " +
                "meters — moves the item cards INDEPENDENTLY of the ability-card fan (they are a " +
                "different, near-square shape). X/Y in plane, Z = proud depth toward the player " +
                "(NEGATIVE = prouder). Seeded 0 (Oak).");
            _slotOverlayOffset[i] = _file.Bind("Cards", $"SlotOverlayOffset_{board}", BoardDefaults.SlotOverlayOffset[i],
                $"[{board}] offset ADDED to the slot snap-glow / wanted-glow local position, board-local " +
                "meters. X/Y in plane, Z = proud depth toward the player (NEGATIVE = prouder). Seeded 0 (Oak).");
            _slotOverlaySpacing[i] = _file.Bind("Cards", $"SlotOverlaySpacing_{board}", BoardDefaults.SlotOverlaySpacing[i],
                $"[{board}] EXTRA gap (board-local meters) ADDED between the TWO slot overlays along the " +
                "board's long (inter-slot) axis — slot 0 (left) moves −½, slot 1 (right) +½. Seeded 0 " +
                "(the slots already space the overlays; positive spreads them apart). Item 1.");
            _initiativeOffset[i] = _file.Bind("Cards", $"InitiativeOffset_{board}",
                BoardDefaults.InitiativeOffset[i],
                $"[{board}] initiative-track mount local position (replaces the fixed mount pos), " +
                "board-local meters. Ships this board's own measured offset.");
            _decisionGap[i] = _file.Bind("Cards", $"DecisionGap_{board}",
                BoardDefaults.DecisionGap[i],
                new ConfigDescription(
                    $"[{board}] vertical distance between the decision PROMPT TEXT (which the game "
                    + "draws along the board's lower edge) and the TOP of the decision buttons, "
                    + "board-local meters. This is the ONLY thing that sets that distance: it is "
                    + "deliberately independent of DecisionScale and DecisionOffset, so resizing or "
                    + "moving the dock never changes how far the buttons sit from the text.",
                    new AcceptableValueRange<float>(0f, 0.12f)));
            _pickBannerOffset[i] = _file.Bind("Cards", $"PickBannerOffset_{board}",
                BoardDefaults.PickBannerOffset[i],
                $"[{board}] offset ADDED to the PICK STATUS placard — the hovering line that reads " +
                "e.g. \"Barbar: Wähle 1 Karte(n) zum Verlieren\" — board-local meters. X/Y in plane, " +
                "Z = proud depth toward the player (NEGATIVE = prouder). Seeded 0 = today's spot just " +
                "above the board's top edge, right under the initiative track. The MULTIPLAYER mirror " +
                "places a peer's placard at the same offset on their remote board.");
            _boardTilt[i] = _file.Bind("Cards", $"BoardTilt_{board}", Defaults.BoardTilt_ByBoard[i],
                $"[{board}] board tilt from horizontal toward the player, degrees (0 = flat desk, " +
                "90 = upright). Replaces TrayTilt in the pose math for this board. Seeded from Oak (30).");
            _boardPitchMin[i] = _file.Bind("Cards", $"BoardPitchMin_{board}", Defaults.BoardPitchMin_ByBoard[i],
                new ConfigDescription(
                    $"[{board}] item 12, BoardMoveMode=LimitedPitch only: how far the grab may pitch " +
                    $"this board DOWN/away from its configured BoardTilt_{board}, degrees (the lower " +
                    "edge of the pitch window; 0 = no downward pitch at all). Live-tunable from the " +
                    $"debug menu; always kept ≤ BoardPitchMax_{board} at read time. Replaces the " +
                    "global BoardPitchMinDegrees, seeded from its default (−45).",
                    new AcceptableValueRange<float>(-85f, 85f)));
            _boardPitchMax[i] = _file.Bind("Cards", $"BoardPitchMax_{board}", Defaults.BoardPitchMax_ByBoard[i],
                new ConfigDescription(
                    $"[{board}] item 12, BoardMoveMode=LimitedPitch only: how far the grab may pitch " +
                    $"this board UP/toward you from its configured BoardTilt_{board}, degrees (the " +
                    "upper edge of the pitch window; 0 = no upward pitch at all). Live-tunable from " +
                    $"the debug menu; always kept ≥ BoardPitchMin_{board} at read time. Replaces the " +
                    "global BoardPitchMaxDegrees, seeded from its default (45).",
                    new AcceptableValueRange<float>(-85f, 85f)));
            _boardYaw[i] = _file.Bind("Cards", $"BoardYaw_{board}", Defaults.BoardYaw_ByBoard[i],
                $"[{board}] extra board yaw ADDED on top of the grab-written TrayYaw, degrees. Seeded 0 (Oak).");
            _boardScale[i] = _file.Bind("Cards", $"BoardScale_{board}", Defaults.BoardScale_ByBoard[i],
                $"[{board}] board size MULTIPLIER applied on top of the grab-written TrayScale. " +
                "Default 0.5: the board is rig-anchored, so its apparent size does not shrink with " +
                "the table — the ~0.4 table-ratio default felt a touch small on first spawn, so the " +
                "board opens slightly larger (0.5). Resize any time with the two-handed grab gesture " +
                "(writes TrayScale); this is the per-board seed on top of that.");
            _assetOffset[i] = _file.Bind("Cards", $"AssetOffset_{board}", BoardDefaults.AssetOffset[i],
                $"[{board}] position offset of the BOARD MESH ALONE, board-local meters. The six " +
                "anchors (card slots, rest tokens, Confirm/Undo) and everything docked to them " +
                "STAY PUT — BoardPosOffset moves the whole board WITH its elements, this slides " +
                "only the asset underneath them. Seeded 0 (today's look).");
            // One session old and NOT seeded from: its only recorded values are the 0.01-degree
            // stepper accidents that exposed the step bug. The three named entries below replace it.
            _assetRotation[i] = _file.Bind("Cards", $"AssetRotation_{board}", Defaults.AssetRotation_ByBoard[i],
                $"LEGACY — no effect, superseded by [{board}] AssetPitch/Yaw/RollDegrees_{board}. " +
                "A Vector3 whose key carried no unit word, so the menu stepped it in hundredths " +
                "of a degree.");
            _assetPitch[i] = _file.Bind("Cards", $"AssetPitchDegrees_{board}", BoardDefaults.AssetPitchDegrees[i],
                $"[{board}] PITCH of the BOARD MESH ALONE, degrees — tips the asset toward/away " +
                "from the player about the board root. Anchors and docked elements stay put " +
                "(BoardTilt tilts the WHOLE board; this tilts only the asset). Seeded 0.");
            _assetYaw[i] = _file.Bind("Cards", $"AssetYawDegrees_{board}", Defaults.AssetYawDegrees_ByBoard[i],
                $"[{board}] YAW of the BOARD MESH ALONE, degrees — turns the asset flat about the " +
                "board root. Anchors and docked elements stay put. Seeded 0.");
            _assetRoll[i] = _file.Bind("Cards", $"AssetRollDegrees_{board}", Defaults.AssetRollDegrees_ByBoard[i],
                $"[{board}] ROLL of the BOARD MESH ALONE, degrees — rolls the asset about the " +
                "board root. Anchors and docked elements stay put. The mesh collider rides the " +
                "mesh, so the laser lands on what you see. Seeded 0.");
            _boardPosOffset[i] = _file.Bind("Cards", $"BoardPosOffset_{board}", Defaults.BoardPosOffset_ByBoard[i],
                $"[{board}] board position offset ADDED on top of the tray head-relative offset, real " +
                "meters in the head frame (X = right, Y = up, Z = forward). Seeded 0 (Oak).");

            // Round-2 group spacing + shape + Active/Piles (all seeded so today's look is unchanged).
            _restButtonSpacing[i] = _file.Bind("Cards", $"RestButtonSpacing_{board}", BoardDefaults.RestButtonSpacing[i],
                $"[{board}] EXTRA gap (board-local meters) ADDED between the short/long REST buttons " +
                "along the board's short axis — the short (upper) disc moves +½, the long (lower) −½. " +
                "Seeded 0 (the bundle anchors already space them; positive spreads them apart).");
            _genericButtonSpacing[i] = _file.Bind("Cards", $"GenericButtonSpacing_{board}", BoardDefaults.GenericButtonSpacing[i],
                $"[{board}] EXTRA gap (board-local meters) ADDED between the GENERIC Confirm/Undo buttons " +
                "along the board's short axis — Confirm (upper) +½, Undo (lower) −½. Seeded 0.");
            _restButtonShape[i] = _file.Bind("Cards", $"RestButtonShape_{board}", Defaults.RestButtonShape_ByBoard[i],
                $"[{board}] cap SHAPE of the REST button group (short/long rest). Round = notch discs " +
                "(today's look); Square = boxy keycaps. Seeded Round.");
            _genericButtonShape[i] = _file.Bind("Cards", $"GenericButtonShape_{board}", Defaults.GenericButtonShape_ByBoard[i],
                $"[{board}] cap SHAPE of the GENERIC button group (Confirm/Undo/etc). Square = boxy keycaps " +
                "(today's look); Round = notch discs. Seeded Square.");
            _activeOffset[i] = _file.Bind("Cards", $"ActiveOffset_{board}", BoardDefaults.ActiveOffset[i],
                $"[{board}] offset ADDED to the ACTIVE-cards mount local position (on top of the fixed base " +
                "just past the pile stacks), board-local meters. Seeded 0 (Oak).");
            _activeCardScale[i] = _file.Bind("Cards", $"ActiveCardScale_{board}", BoardDefaults.ActiveCardScale[i],
                $"[{board}] scale of the ACTIVE-cards column (× card size). Seeded 0.82 — slightly smaller " +
                "than the hand/browse fan.");
            _activeGridSpacing[i] = _file.Bind("Cards", $"ActiveGridSpacing_{board}", Defaults.ActiveGridSpacing_ByBoard[i],
                $"[{board}] ACTIVE-cards grid step FACTORS: X = column step (× scaled card width), Y = row " +
                "step (× scaled card height). Seeded (1.06, 0.70).");
            _pileOffset[i] = _file.Bind("Cards", $"PileOffset_{board}", BoardDefaults.PileOffset[i],
                $"[{board}] offset ADDED to the discard/burn PILE mount local position (on top of the fixed " +
                "right-edge base), board-local meters. Seeded 0 (Oak).");
            _pileScale[i] = _file.Bind("Cards", $"PileScale_{board}", Defaults.PileScale_ByBoard[i],
                $"[{board}] size MULTIPLIER of the two discard/burn pile stacks. Seeded 1 (Oak).");
            _pileSpacing[i] = _file.Bind("Cards", $"PileSpacing_{board}", Defaults.PileSpacing_ByBoard[i],
                $"[{board}] vertical gap (board-local meters) between the discard (upper) and burn (lower) " +
                "pile stack centers. Seeded 0.116 (Oak).");

            // Items 4/6: the remaining board-attached elements (offsets ADDED on top of the fixed
            // base; the docked panels + cluster also carry a size multiplier). All seeded so today's
            // look is unchanged.
            _objectivesOffset[i] = _file.Bind("Cards", $"ObjectivesOffset_{board}", BoardDefaults.ObjectivesOffset[i],
                $"[{board}] offset ADDED to the OBJECTIVES ('Aufgaben') dock mount local position (on top of " +
                "the fixed left-column base), board-local meters. Seeded 0 (Oak).");
            _objectivesScale[i] = _file.Bind("Cards", $"ObjectivesScale_{board}", BoardDefaults.ObjectivesScale[i],
                $"[{board}] SIZE multiplier of the OBJECTIVES ('Aufgaben') dock — a true ZOOM: text, " +
                "progress bars, icons and the panel itself all scale together, because this is the " +
                "objectives MOUNT's localScale and the docked panel rides mount.lossyScale. This is the " +
                "ONLY dial that changes how big the task text renders; ObjectivesWidth changes the shape " +
                "of the block, never its type size. Seeded 1 (Oak).");
            _objectivesWidth[i] = _file.Bind("Cards", $"ObjectivesWidth_{board}", BoardDefaults.ObjectivesWidth[i],
                new ConfigDescription(
                    $"[{board}] WIDTH multiplier of the OBJECTIVES ('Aufgaben') dock — SHAPE ONLY, NOT " +
                    "size. The wrap column is PlayTray.ObjectivesMountWidth (0.26 m) x this factor, e.g. " +
                    "1.6 = 416 mm, FORCED onto the game's objective container as a pixel width (column x " +
                    "the panel density, 2400 px/m x 0.6), so the objective TEXT RE-WRAPS at that measure " +
                    "and each row's fillAmount PROGRESS BAR really is that long. The rendered GLYPH SIZE " +
                    "is untouched: the dock fit no longer looks at the width axis for this panel (it " +
                    "cannot — the content IS the budget), so metres-per-pixel is the panel density alone " +
                    "and only ObjectivesScale ('Größe') moves it. Higher = the same text on fewer, longer " +
                    "lines with the panel reaching further LEFT from the board edge into open space (no " +
                    "board overlap); lower = more, shorter lines in a narrower block, same letter height. " +
                    "Below ~0.5 the rows wrap tighter than the game authored them. Default 1.6. " +
                    "Live-applied: ObjectivesSurface re-forces the column each tick; every rect change is " +
                    "reverted when the panel is released.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            _elementsOffset[i] = _file.Bind("Cards", $"ElementsOffset_{board}", BoardDefaults.ElementsOffset[i],
                $"[{board}] offset ADDED to the ELEMENT infusion ('Elemente') dock mount local position (on top " +
                "of the fixed left-column base below the objectives), board-local meters. Seeded 0 (Oak).");
            _elementsScale[i] = _file.Bind("Cards", $"ElementsScale_{board}", Defaults.ElementsScale_ByBoard[i],
                $"[{board}] size MULTIPLIER of the ELEMENT infusion ('Elemente') dock. Seeded 1 (Oak).");
            _pinOffset[i] = _file.Bind("Cards", $"PinOffset_{board}", BoardDefaults.PinOffset[i],
                $"[{board}] offset ADDED to the FOLLOW/PIN toggle button local position (on top of its fixed " +
                "bottom-right base), board-local meters (Z = proud toward the player). Seeded 0 (Oak).");
            _readoutOffset[i] = _file.Bind("Cards", $"ReadoutOffset_{board}", BoardDefaults.ReadoutOffset[i],
                $"[{board}] offset ADDED to the ROUND readout ('Runde N') local position (on top of its fixed " +
                "top-right base), board-local meters (Z = proud toward the player). Seeded 0 (Oak).");
            _clusterOffset[i] = _file.Bind("Cards", $"ClusterOffset_{board}", Defaults.ClusterOffset_ByBoard[i],
                $"[{board}] offset ADDED to the turn-flow BUTTON CLUSTER (Undo|Ready|Skip) mount local position " +
                "(on top of its fixed under-slots base), board-local meters. Seeded 0 (Oak).");
            _clusterScale[i] = _file.Bind("Cards", $"ClusterScale_{board}", Defaults.ClusterScale_ByBoard[i],
                $"[{board}] size MULTIPLIER of the turn-flow BUTTON CLUSTER (on top of its fixed 0.7x dock scale). " +
                "Seeded 1 (Oak).");

            // Item C: the shared DECISION DOCK (text + buttons UNDER the board — the decision/confirm
            // prompt row) offset + size, per board.
            _decisionOffset[i] = _file.Bind("Cards", $"DecisionOffset_{board}", BoardDefaults.DecisionOffset[i],
                $"[{board}] offset ADDED to the shared DECISION DOCK mount local position (the decision/confirm " +
                "prompt row that hangs below the board), board-local meters. Seeded 0 (Oak).");
            _decisionScale[i] = _file.Bind("Cards", $"DecisionScale_{board}", Defaults.DecisionScale_ByBoard[i],
                $"[{board}] size MULTIPLIER of the shared DECISION DOCK (its docked prompt row pose-follows the " +
                "mount's lossyScale). Seeded 1 (Oak).");
        }

        // ONE-TIME defaults migration (paired with the 2.5x default table scale in
        // [Comfort] SavedScaleMultiplier): BepInEx keeps values saved in an existing config
        // file, so a lowered BoardScale default only reaches FRESH installs by itself.
        // NOTE, and this is deliberate-or-a-bug, not something to "tidy": the bind above
        // defaults to 0.5, NOT the 0.4 this migration writes. The bind was later raised to 0.5
        // ("the ~0.4 table-ratio default felt a touch small on first spawn" — its own
        // description); the migration value and the marker key BoardScaleDefault04Applied were
        // left at 0.4. So a fresh install gets 0.5 and a migrated one gets 0.4. Do not
        // "harmonise" the two numbers without asking — changing either moves board size on
        // somebody's existing rig. This comment previously claimed 0.4 was the default above.
        // For existing files, adopt 0.4 ONLY where the user never touched the old default
        // (saved value == old default 1.0 — BoardScale is hand-edit/debug-menu only, so an
        // exact 1.0 means untouched). The marker makes this run at most once per config file;
        // a grab-written TrayScale is the user's own tuning and is NEVER migrated.
        ConfigEntry<bool> boardScaleMigrated = _file.Bind("Cards", "BoardScaleDefault04Applied",
            Defaults.BoardScaleDefault04Applied,
            "Internal one-time migration marker: the 0.4x BoardScale default (paired with the " +
            "2.5x table-scale default) has been offered to this config file. Do not edit.");
        if (!boardScaleMigrated.Value)
        {
            foreach (ControlBoard board in System.Enum.GetValues(typeof(ControlBoard)))
            {
                ConfigEntry<float> entry = _boardScale[(int)board];
                if (Mathf.Approximately(entry.Value, 1f))
                {
                    entry.Value = 0.4f;
                    Core.VRLog.Info("Cards", $"One-time migration: BoardScale_{board} was at the old " +
                                        "default 1.0 (never tuned) — adopted the new default 0.4 " +
                                        "to fit the 2.5x default table scale.");
                }
            }
            boardScaleMigrated.Value = true;
        }

        // ---- Demeo-parity fan/grab tuning (test #22 blueprint) ----
        FanCurveByFill = _file.Bind("Cards", "FanCurveByFill", Defaults.FanCurveByFill,
            "Demeo parity (G1): scale the fan's vertical arch and per-card tilt by how full the " +
            "hand is — nearly flat with a few cards, arched/tilted when the hand is full (Demeo " +
            "CardHandView). false = the old constant curvature at every hand size.");
        FanMaxHandForCurve = _file.Bind("Cards", "FanMaxHandForCurve", Defaults.FanMaxHandForCurve,
            "Demeo parity (G1): hand size at which the fan reaches full curvature. fill = " +
            "cardCount / this (clamped 0..1) scales the arch + tilt.");
        FanFlatCurvatureFactor = _file.Bind("Cards", "FanFlatCurvatureFactor", Defaults.FanFlatCurvatureFactor,
            "Demeo parity (G1): the vertical-arch factor at a FULL hand (fill = 1). This is the " +
            "pre-Demeo constant value; with FanCurveByFill it is now the fill=1 target and the " +
            "arch scales down toward flat as the hand shrinks.");
        FanTiltFactor = _file.Bind("Cards", "FanTiltFactor", Defaults.FanTiltFactor,
            "Demeo parity (G1): the per-card Z-tilt factor at a FULL hand (fill = 1), scaled by fill.");
        FanSplitMultiplier = _file.Bind("Cards", "FanSplitMultiplier", Defaults.FanSplitMultiplier,
            "Demeo parity (G2): how far (real meters) the fan's cards slide sideways to open a gap " +
            "around the hovered card (Demeo splits the whole fan apart, not just the hovered card). " +
            "0 = no split.");
        FanSplitFalloff = _file.Bind("Cards", "FanSplitFalloff", Defaults.FanSplitFalloff,
            "Demeo parity (G2): how quickly the neighbor split decays with distance (in card slots) " +
            "from the hovered card. Higher = only the nearest neighbors move; lower = the whole fan " +
            "spreads. Coded-curve substitute for Demeo's serialized falloff curve.");
        FanSelectedPopForward = _file.Bind("Cards", "FanSelectedPopForward", Defaults.FanSelectedPopForward,
            "Demeo parity (G2): how far (real meters) the hovered/selected card pops toward the " +
            "viewer (along -face normal). Demeo uses ~0.25 scene units; 0.035 m matches our scale.");
        GrabButton = _file.Bind("Cards", "GrabButton", Defaults.GrabButton,
            "Demeo parity (G3): which controller button grabs a card by proximity. Trigger = " +
            "Demeo (index-finger pinch, matches our laser pluck so a card grabbed either way " +
            "releases on trigger-up). Grip = the pre-Demeo behavior.");
        FanFollowSmoothing = _file.Bind("Cards", "FanFollowSmoothing", Defaults.FanFollowSmoothing,
            "Demeo parity (G4): eased fan-follow rate (1/s exponential smoothing). The fan chases " +
            "the palm with a soft ease instead of being rigidly welded to it (Demeo ViewHelper). " +
            "0 = rigidly parented (the pre-Demeo behavior). Higher = snappier.");
        FanFollowDeadzone = _file.Bind("Cards", "FanFollowDeadzone", Defaults.FanFollowDeadzone,
            "Demeo parity (G4): fan-follow dead zone (real meters). The fan holds still until the " +
            "palm drifts past this, then eases to it — kills micro-jitter (Demeo minDistanceToMove). " +
            "Only used when FanFollowSmoothing > 0.");
        RevealIgnoreWhenGrabbing = _file.Bind("Cards", "RevealIgnoreWhenGrabbing", Defaults.RevealIgnoreWhenGrabbing,
            "Demeo parity (G5): don't open the fan on the hand that is currently grabbing " +
            "something (Demeo suppresses the reveal on the busy hand). false = the old behavior.");

        // ---- Demeo-style fan-out reveal animation + sound (fan-reveal parity pass) ----
        // Demeo seeds every card at the MIDDLE slot and lerps it out to its fan slot
        // (CardHandView.ChangeCards doFanAnimation, decompiled CardHandView.cs:577-580; the
        // lerp runs at lerpTime += dt * cardMoveSpeed, :430-431). cardMoveSpeed itself is a
        // serialized prefab value (not in code); Demeo's flip-transition wait of
        // 1/cardMoveSpeed + 0.1 s (:540) implies the fan-in completes in ~1/cardMoveSpeed s.
        // 0.18 s ease-out + a tiny stagger reproduces the short/snappy feel.
        FanOpenDuration = _file.Bind("Cards", "FanOpenDuration", Defaults.FanOpenDuration,
            new ConfigDescription(
                "Fan-out reveal animation (Demeo CardHandView fan-in): seconds each card takes " +
                "to fly from the collapsed center stack to its fan slot (ease-out, UNSCALED " +
                "time — runs even while the game pauses simulation time). 0 = instant.",
                new AcceptableValueRange<float>(0f, 0.6f)));
        FanOpenStagger = _file.Bind("Cards", "FanOpenStagger", Defaults.FanOpenStagger,
            new ConfigDescription(
                "Fan-out reveal: extra start delay in seconds PER SLOT of distance from the fan " +
                "center — the fan ripples outward instead of all cards moving at once. Demeo " +
                "moves all cards simultaneously (0); a tiny stagger reads livelier. 0 = none.",
                new AcceptableValueRange<float>(0f, 0.08f)));
        FanCloseDuration = _file.Bind("Cards", "FanCloseDuration", Defaults.FanCloseDuration,
            new ConfigDescription(
                "Fan hide: seconds the cards take to collapse back into the center stack before " +
                "the fan disappears (unscaled time). 0 = vanish instantly (pre-animation behavior).",
                new AcceptableValueRange<float>(0f, 0.4f)));
        FanRevealSound = _file.Bind("Cards", "FanRevealSound", Defaults.FanRevealSound,
            "Game audio item played once when the palm fan reveals (Demeo plays " +
            "MotherbrainAudio.OnCardHandShow, CardHandView.cs:682). PlaySound_EnemyCardDraw is " +
            "the game's card-draw whoosh (InitiativeTrack.cs:59); alternatives found in " +
            "GH.Runtime: PlaySound_CardUI_SelectCard, PlaySound_UICardTabSelect, " +
            "PlaySound_CardUI_DiscardedCard, PlaySound_CardUI_BurnedCard. Empty = silent.");
        FanHideSound = _file.Bind("Cards", "FanHideSound", Defaults.FanHideSound,
            "Game audio item played once when the palm fan hides (Demeo plays " +
            "MotherbrainAudio.OnCardHandHide, CardHandView.cs:691). PlaySound_UICardTabSelect " +
            "is the soft card-tab tick. Empty = silent.");

        // ---- Card interaction sounds (more-card-sounds pass; task #5 double-sound fix) ----
        // VERIFIED against the decompiled game (AbilityCardUI.cs:1182-1185): EVERY queued
        // SelectCard/UnselectCard resolves through AbilityCardUI.ToggleSelect, which plays the
        // card's serialized profile click (interactionAudioProfile.mouseDownAudioItem) for a
        // locally-controlled hand — so any select/unselect-backed drop already sounds on its own.
        // The mod therefore plays its own sound ONLY at moments the game is silent: the grab
        // itself, the tray→tray reorder, the pick-card re-drop, and take-backs that happen while
        // the game already deselected the card (pick reopen). Playing CardPlaceSound on the
        // select-backed placement paths stacked with the game's click = the reported "two sounds
        // at once" per slot placement.
        CardGrabSound = _file.Bind("Cards", "CardGrabSound", Defaults.CardGrabSound,
            "Game audio item played once when a card is grabbed (plucked from the fan, a board " +
            "slot, the pick field, or a pile/active column — proximity grab and laser pluck " +
            "alike). The game plays nothing of its own on a physical grab. " +
            "PlaySound_UICardTabSelect is the game's soft card-tab tick — a quiet pick. " +
            "Alternatives: PlaySound_UIButtonSelect, PlaySound_CardUI_SelectCard. Empty = silent.");
        CardPlaceSound = _file.Bind("Cards", "CardPlaceSound", Defaults.CardPlaceSound,
            "Game audio item played once when a held card lands somewhere the GAME plays no " +
            "sound of its own: a tray→tray reorder or a pick-card re-drop. Placements that " +
            "select a card (fan→slot, swap, pick commit) intentionally play NO mod sound — the " +
            "game's own AbilityCardUI profile click (mouseDownAudioItem) fires there when the " +
            "queued SelectCard resolves, and doubling it was the two-sounds-per-placement bug. " +
            "Alternatives: PlaySound_ScenarioUI_TileConfirm, PlaySound_UIButtonSelect. Empty = silent.");
        CardTakeBackSound = _file.Bind("Cards", "CardTakeBackSound", Defaults.CardTakeBackSound,
            "Game audio item played once when a card is taken back while the game stays silent " +
            "(pick reopen — the card was already deselected by the game's own \"choose another " +
            "card\" path). Normal take-backs play the game's own profile click via the queued " +
            "UnselectCard and add no mod sound. PlaySound_UIUndoHex is the game's soft " +
            "hex-cancel click — reads as an undo. Alternatives: PlaySound_ScenarioUIUndo, " +
            "PlaySound_UICardTabSelect. Empty = silent.");

        // ---- GLOBAL hand-fan geometry (in-VR "Fan" debug category) ----
        FanPerCardStepDegrees = _file.Bind("Cards", "FanPerCardStepDegrees", Defaults.FanPerCardStepDegrees,
            new ConfigDescription(
                "Hand fan (global, in-VR debug 'Fan' category): per-card angular step cap in " +
                "degrees. Dominant knob for small/medium hands — each added card fans out this far " +
                "until the total sweep hits FanArcSweepDegrees. Larger = adjacent cards sit farther " +
                "apart (easier to aim at one). Seeded 14° (the old item-8 local const).",
                new AcceptableValueRange<float>(2f, 40f)));
        FanArcSweepDegrees = _file.Bind("Cards", "FanArcSweepDegrees", Defaults.FanArcSweepDegrees,
            new ConfigDescription(
                "Hand fan (global): total fan arc sweep in degrees — governs BIG hands (once there " +
                "are enough cards to reach the step cap, this sets how far the full hand wraps: " +
                "higher = a rounder, more circular fan). Seeded 91° (the old FanArcDegrees 70 × the " +
                "1.3 arc scale).",
                new AcceptableValueRange<float>(20f, 180f)));
        FanEffectiveRadius = _file.Bind("Cards", "FanEffectiveRadius", Defaults.FanEffectiveRadius,
            new ConfigDescription(
                "Hand fan (global): effective arc radius in real meters. Opens real space between " +
                "card centers (chord ∝ radius·sin(step/2)) and enlarges the exposed grab strip in " +
                "step. Seeded 0.1792 m (the old FanRadius 0.16 × the 1.12 radius scale). Supersedes " +
                "FanRadius for the hand fan only (the pile browse-fan keeps its own).",
                new AcceptableValueRange<float>(0.05f, 0.4f)));
        FanHoverSplitScale = _file.Bind("Cards", "FanHoverSplitScale", Defaults.FanHoverSplitScale,
            new ConfigDescription(
                "Hand fan (global): hover-split scale — multiplies FanSplitMultiplier so the gap the " +
                "fan opens around a hovered card stays proportional to the (wider) card spacing. " +
                "Seeded 1.45 (the old item-8 local const).",
                new AcceptableValueRange<float>(0.5f, 3f)));
        BrowseFanOffset = _file.Bind("Cards", "BrowseFanOffset", Defaults.BrowseFanOffset,
            "Pile BROWSE fan anchor offset, board-local meters ADDED to the fixed above-board base " +
            "pose (x 0, y board-top + 0.26, z -0.05). X = along the board's long axis (+right), " +
            "Y = up above the board face, Z = out of the board face (NEGATIVE = toward the player). " +
            "GLOBAL (not per-board): the anchor is board-LOCAL, so it already rides every board's " +
            "own pose and scale. Live: PileBrowser re-reads it every frame while a browse fan is " +
            "open, so the in-VR debug menu's Piles-element 'Browse X/Y/Z' steppers move the open " +
            "fan immediately. Seeded 0 (today's placement).");

        // ---- Fan DEPTH curvature + gaze-bias toggle (in-VR debug 'Fan' category) ----
        FanSideDepthCurve = _file.Bind("Cards", "FanSideDepthCurve", Defaults.FanSideDepthCurve,
            new ConfigDescription(
                "Hand fan (global): DEPTH CURVATURE — signed bow (real meters) of the OUTERMOST card " +
                "along the fan's forward axis, so a full hand bows into depth like a real held fan. " +
                "POSITIVE = edge cards recede AWAY from the viewer (center card nearest, edges fall " +
                "back); NEGATIVE = edge cards bow the OTHER way, TOWARD the viewer (center furthest). " +
                "The bow is quadratic (FanCurvePower) in each card's distance from center and ramps in " +
                "with hand size (flat below FanCurveMinCards, full at FanMaxHandForCurve). Grab/hover " +
                "raycasting tracks the moved cards automatically either sign. 0 = flat (the old " +
                "billboarded sheet).",
                new AcceptableValueRange<float>(-0.12f, 0.12f)));
        FanCurvePower = _file.Bind("Cards", "FanCurvePower", Defaults.FanCurvePower,
            new ConfigDescription(
                "Hand fan (global): depth-curvature exponent applied to each card's fraction-from-center " +
                "(0 at the middle card, 1 at the outermost). 2 = quadratic (gentle near the center, " +
                "steepening toward the edges — reads like a real fan); 1 = a straight wedge; higher = a " +
                "flatter middle with sharper edge recession.",
                new AcceptableValueRange<float>(0.5f, 4f)));
        FanCurveMinCards = _file.Bind("Cards", "FanCurveMinCards", Defaults.FanCurveMinCards,
            new ConfigDescription(
                "Hand fan (global): card count at/below which the fan stays FLAT (no depth bow). The " +
                "curvature ramps in linearly from here up to FanMaxHandForCurve, so 1-3 cards read flat " +
                "and a full hand curves noticeably.",
                new AcceptableValueRange<int>(1, 12)));
        FanGazeBias = _file.Bind("Cards", "FanGazeBias", Defaults.FanGazeBias,
            "Hand fan (global): enable the gaze-responsive facing YAW (opt-in, default OFF). OFF = the " +
            "fan billboards steadily toward the head and the DEPTH curvature (FanSideDepthCurve) is the " +
            "sole shape response — the steady, predictable follow. ON re-adds an eased extra yaw that " +
            "turns the fan partway toward the head's gaze so the looked-at edge tips forward; a WIDE " +
            "deadzone plus side-hysteresis means a left-right head shake no longer dithers about which " +
            "way to lean near the fan center (it holds center until the gaze clearly commits to a side). " +
            "SUPERSEDED by FanFaceViewer + FanGazeApexFollow — leave OFF unless you want the extra lean.");

        // ---- Fan CARD PRESENTATION (per-card toe-in + gaze-following bow apex) ----
        // The edge-read fix the whole-fan yaw bias above could not deliver: instead of swinging the
        // WHOLE hand (a see-saw that improves one end by ruining the other), each card is aimed at
        // the head individually and the depth bow's apex slides under whichever card you are looking
        // at. See CardFan's "card presentation" region for the full derivation.
        FanFaceViewer = _file.Bind("Cards", "FanFaceViewer", Defaults.FanFaceViewer,
            new ConfigDescription(
                "Hand fan (global): per-card TOE-IN toward the head. The fan as a whole billboards at " +
                "the head, but a card sitting 13 cm out along the arc is still seen at ~15-20° off its " +
                "own normal — it is turned away from your eye exactly when you turn to read it. This " +
                "aims EACH card at the head instead (like cupping a real hand of cards). 0 = off (one " +
                "flat billboarded sheet, the old look), 1 = exact per-card facing. Orientation only: " +
                "card positions, the draw order, the hover split and the laser hit rects are unchanged " +
                "(the pick reads the same home rotation that is drawn).",
                new AcceptableValueRange<float>(0f, 1f)));
        FanGazeApexFollow = _file.Bind("Cards", "FanGazeApexFollow", Defaults.FanGazeApexFollow,
            new ConfigDescription(
                "Hand fan (global): how far your GAZE RELIEVES the depth bow. The bow " +
                "(FanSideDepthCurve) recedes cards away from the viewer with distance from the middle " +
                "of the hand, so the outermost card — the one you turn your head to read — is the " +
                "FURTHEST away, i.e. looking at a card made it harder to see. At 1 the card under your " +
                "gaze is lifted fully out of that recession (its neighbours partly, tapering off), at " +
                "0.5 halfway, at 0 not at all (= the plain symmetric bow, a bit-exact revert). The " +
                "relief only ever REMOVES recession: no card can end up further back than it is at 0, " +
                "at any gaze angle. The response is a smooth function of the gaze angle with no " +
                "threshold, no latch and no per-card quantisation — turning your head further toward a " +
                "card can only ever bring that card further forward — and is eased at FanGazeSmoothing. " +
                "Silhouette-safe: the bow runs along the VIEW axis, so relieving it changes what is " +
                "nearest without visibly moving the fan.",
                new AcceptableValueRange<float>(0f, 1f)));
        FanGazeSmoothing = _file.Bind("Cards", "FanGazeSmoothing", Defaults.FanGazeSmoothing,
            new ConfigDescription(
                "Hand fan (global): exponential ease rate (1/s, unscaled time) of the gaze apex toward " +
                "the card you are looking at. Lower = heavier/lazier and completely immune to head " +
                "jitter; higher = the fan presents the looked-at card faster. 8 reaches ~90% of a head " +
                "turn in ~0.3 s.",
                new AcceptableValueRange<float>(1f, 30f)));
    }

    /// <summary>Tray scale multiplier clamp (matches the two-handed grab clamp).</summary>
    internal static float ClampedTrayScale => Mathf.Clamp(TrayScale.Value, 0.5f, 2f);

    /// <summary>
    /// Item 12: the CURRENT board's normalized grab-pitch window (min ≤ max guaranteed, whatever
    /// the two per-board debug entries say — a crossed pair collapses onto its midpoint rather
    /// than throwing or flipping).
    /// Degrees relative to the per-board BoardTilt; positive = more upright toward the player.
    /// </summary>
    internal static (float Min, float Max) BoardPitchWindow
    {
        get
        {
            ControlBoard board = CurrentBoard;
            float min = BoardPitchMin(board).Value;
            float max = BoardPitchMax(board).Value;
            if (min > max)
                min = max = (min + max) * 0.5f;
            return (min, max);
        }
    }

    /// <summary>
    /// Item 12: the pitch (degrees added to BoardTilt) the CURRENT movement mode actually applies:
    /// 0 in Limited (today's pose, bit-identical), the window-clamped TrayPitch in LimitedPitch,
    /// the raw TrayPitch in Free (best-effort re-placement pose — Free never clamps).
    /// </summary>
    internal static float EffectiveTrayPitch
    {
        get
        {
            switch (BoardMoveMode.Value)
            {
                case Cards.BoardMoveMode.LimitedPitch:
                    (float min, float max) = BoardPitchWindow;
                    return Mathf.Clamp(TrayPitch.Value, min, max);
                case Cards.BoardMoveMode.Free:
                    return TrayPitch.Value;
                default:
                    return 0f;
            }
        }
    }

    /// <summary>Card height derived from width (63.5 x 88 mm poker aspect).</summary>
    internal static float CardHeight => CardWidth.Value * (88f / 63.5f);

    /// <summary>True when the fan should be out without a gesture ([Cards] RevealMode = always).</summary>
    internal static bool RevealAlways =>
        string.Equals(RevealMode.Value, "always", System.StringComparison.OrdinalIgnoreCase);

    internal static Vector3 TrayOffset => new(TrayRight.Value, -TrayDown.Value, TrayForward.Value);

    /// <summary>
    /// The FIRST-PLACEMENT seat (user ruling 2026-08-03: "Beim ersten Spawnen sollte das
    /// Controllboard immer links neben dem Kopf spawnen"): head-relative, LEFT of the head
    /// (negative x), slightly forward and below eye level. Deliberately NOT the persisted
    /// <see cref="TrayOffset"/> — that one is whatever the last drag happened to leave behind, so
    /// a scenario used to start with the board wherever the previous session ended, which is
    /// exactly the unpredictability the ruling removes.
    /// </summary>
    internal static Vector3 SpawnSeatOffset => new(
        -Mathf.Abs(SpawnSideMeters.Value), -SpawnDownMeters.Value, SpawnForwardMeters.Value);

    // ---- Per-board resolver accessors (Part A) ----
    // Return the ConfigEntry so callers can both READ (.Value) and WRITE (.Value = …, which
    // BepInEx persists and fires SettingChanged on — the debug menu's live-apply hook).

    /// <summary>The board the tray currently uses (drives every per-board resolver below).</summary>
    internal static ControlBoard CurrentBoard => Board.Value;

    internal static ConfigEntry<Vector3> RestButtonOffset(ControlBoard b) => _restButtonOffset[(int)b];
    internal static ConfigEntry<float> RestButtonDiameter(ControlBoard b) => _restButtonDiameter[(int)b];
    internal static ConfigEntry<Vector3> ConfirmUndoOffset(ControlBoard b) => _confirmUndoOffset[(int)b];
    internal static ConfigEntry<float> ConfirmUndoSize(ControlBoard b) => _confirmUndoSize[(int)b];
    internal static ConfigEntry<Vector3> ItemUseSlotOffset(ControlBoard b) => _itemUseSlotOffset[(int)b];
    internal static ConfigEntry<Vector3> ItemCardOffset(ControlBoard b) => _itemCardOffset[(int)b];
    internal static ConfigEntry<Vector3> SlotOverlayOffset(ControlBoard b) => _slotOverlayOffset[(int)b];
    internal static ConfigEntry<float> SlotOverlaySpacing(ControlBoard b) => _slotOverlaySpacing[(int)b];
    internal static ConfigEntry<Vector3> InitiativeOffset(ControlBoard b) => _initiativeOffset[(int)b];

    /// <summary>Per-board offset of the pick-status placard (board-local metres) — user request
    /// 2026-08-03 ("Ich möchte auch in der Lage sein die Position von Text wie 'Barbar: Wähle 1
    /// Karte(n) zum Verlieren' zu ändern"). Mirrored onto a peer's remote board.</summary>
    internal static ConfigEntry<Vector3> PickBannerOffset(ControlBoard b) => _pickBannerOffset[(int)b];

    /// <summary>Per-board gap between the decision prompt text and the decision buttons, board-local
    /// metres — see the bind description; scale- and offset-independent by construction.</summary>
    internal static ConfigEntry<float> DecisionGap(ControlBoard b) => _decisionGap[(int)b];
    internal static ConfigEntry<float> BoardTilt(ControlBoard b) => _boardTilt[(int)b];
    internal static ConfigEntry<float> BoardPitchMin(ControlBoard b) => _boardPitchMin[(int)b];
    internal static ConfigEntry<float> BoardPitchMax(ControlBoard b) => _boardPitchMax[(int)b];
    internal static ConfigEntry<Vector3> AssetOffset(ControlBoard b) => _assetOffset[(int)b];
    internal static ConfigEntry<float> AssetPitch(ControlBoard b) => _assetPitch[(int)b];
    internal static ConfigEntry<float> AssetYaw(ControlBoard b) => _assetYaw[(int)b];
    internal static ConfigEntry<float> AssetRoll(ControlBoard b) => _assetRoll[(int)b];
    internal static ConfigEntry<float> BoardYaw(ControlBoard b) => _boardYaw[(int)b];
    internal static ConfigEntry<float> BoardScale(ControlBoard b) => _boardScale[(int)b];
    internal static ConfigEntry<Vector3> BoardPosOffset(ControlBoard b) => _boardPosOffset[(int)b];

    // ---- Round-2 group spacing / shape / Active / Piles resolvers ----
    internal static ConfigEntry<float> RestButtonSpacing(ControlBoard b) => _restButtonSpacing[(int)b];
    internal static ConfigEntry<float> GenericButtonSpacing(ControlBoard b) => _genericButtonSpacing[(int)b];
    internal static ConfigEntry<ButtonShape> RestButtonShape(ControlBoard b) => _restButtonShape[(int)b];
    internal static ConfigEntry<ButtonShape> GenericButtonShape(ControlBoard b) => _genericButtonShape[(int)b];
    internal static ConfigEntry<Vector3> ActiveOffset(ControlBoard b) => _activeOffset[(int)b];
    internal static ConfigEntry<float> ActiveCardScale(ControlBoard b) => _activeCardScale[(int)b];
    internal static ConfigEntry<Vector2> ActiveGridSpacing(ControlBoard b) => _activeGridSpacing[(int)b];
    internal static ConfigEntry<Vector3> PileOffset(ControlBoard b) => _pileOffset[(int)b];
    internal static ConfigEntry<float> PileScale(ControlBoard b) => _pileScale[(int)b];
    internal static ConfigEntry<float> PileSpacing(ControlBoard b) => _pileSpacing[(int)b];

    // ---- Remaining board-attached element resolvers (items 4/6) ----
    internal static ConfigEntry<Vector3> ObjectivesOffset(ControlBoard b) => _objectivesOffset[(int)b];
    internal static ConfigEntry<float> ObjectivesScale(ControlBoard b) => _objectivesScale[(int)b];
    internal static ConfigEntry<float> ObjectivesWidth(ControlBoard b) => _objectivesWidth[(int)b];
    internal static ConfigEntry<Vector3> ElementsOffset(ControlBoard b) => _elementsOffset[(int)b];
    internal static ConfigEntry<float> ElementsScale(ControlBoard b) => _elementsScale[(int)b];
    internal static ConfigEntry<Vector3> PinOffset(ControlBoard b) => _pinOffset[(int)b];
    internal static ConfigEntry<Vector3> ReadoutOffset(ControlBoard b) => _readoutOffset[(int)b];
    internal static ConfigEntry<Vector3> ClusterOffset(ControlBoard b) => _clusterOffset[(int)b];
    internal static ConfigEntry<float> ClusterScale(ControlBoard b) => _clusterScale[(int)b];
    internal static ConfigEntry<Vector3> DecisionOffset(ControlBoard b) => _decisionOffset[(int)b];
    internal static ConfigEntry<float> DecisionScale(ControlBoard b) => _decisionScale[(int)b];
}
