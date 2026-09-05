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

    /// <summary>Feature switch: may a card be taken INTO THE HAND (grip held while the trigger
    /// grabs it) instead of always floating readable? See <see cref="HeldCardGrip"/>.</summary>
    internal static ConfigEntry<bool> InHandHold = null!;

    /// <summary>In-hand grip: rest tilt of the card out of the palm plane, degrees.</summary>
    internal static ConfigEntry<float> InHandPitch = null!;

    /// <summary>In-hand grip: fine-tune offset added to the modelled pinch point, GrabAnchor-local meters.</summary>
    internal static ConfigEntry<Vector3> InHandPinchOffset = null!;

    /// <summary>In-hand grip: how long the hand takes to close on the card (and to let go), seconds.</summary>
    internal static ConfigEntry<float> InHandGraspSeconds = null!;

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

    /// <summary>[Cards] PokePadPixels — how far past a default-action button's own rect the
    /// FINGERTIP hitbox reaches, in the card's authored uGUI pixels (ModBuild 403). 0 = off. The
    /// laser is never enlarged by it. See <see cref="PokePads"/>.</summary>
    internal static ConfigEntry<float> PokePadPixels = null!;

    internal const float PokePadPixelsMin = 0f;
    internal const float PokePadPixelsMax = 60f;

    /// <summary>The pad in force right now, clamped to the storage band; the pre-bind fallback is
    /// the shipped default so a face built before the config is bound gets the same pad.</summary>
    internal static float PokePadPixelsLive() =>
        Mathf.Clamp(PokePadPixels != null ? PokePadPixels.Value : Defaults.PokePadPixels,
                    PokePadPixelsMin, PokePadPixelsMax);

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

    /// <summary>Per-pile fan spread: degrees between two neighbouring cards of THAT pile's fan.</summary>
    private static readonly ConfigEntry<float>[] _fanStepDegrees = new ConfigEntry<float>[3];

    /// <summary>Per-pile fan radius multiplier on the shared hand-fan radius.</summary>
    private static readonly ConfigEntry<float>[] _fanRadiusFactor = new ConfigEntry<float>[3];

    /// <summary>Hard floor/ceiling on the board's APPARENT width in real metres (see the binds).</summary>
    internal static ConfigEntry<float> BoardMinWidthMeters = null!;
    internal static ConfigEntry<float> BoardMaxWidthMeters = null!;

    /// <summary>First placement of a scenario seats the board beside the head on the LEFT.</summary>
    internal static ConfigEntry<bool> SpawnLeftOfHead = null!;
    internal static ConfigEntry<float> SpawnSideMeters = null!;
    internal static ConfigEntry<float> SpawnForwardMeters = null!;
    internal static ConfigEntry<float> SpawnDownMeters = null!;

    /// <summary>The ARRIVAL SEAT GUARD's two bounds — how far from the head, and how far off the
    /// player's forward, the control board may be when a scenario puts the player down. See
    /// <c>PlayTray.TickArrivalSeatGuard</c>.</summary>
    internal static ConfigEntry<float> SpawnMaxReachMeters = null!;
    internal static ConfigEntry<float> SpawnMaxBearingDegrees = null!;

    /// <summary>How wide the control board LOOKS at the first seat, as the angle it subtends from
    /// the head, degrees. The comfort number behind <c>PlayTray.TrySolveArrivalBoardScale</c> — see
    /// <see cref="SeatBoardWidthMeters"/> for the arithmetic that turns it into a width.</summary>
    internal static ConfigEntry<float> SpawnBoardWidthDegrees = null!;

    /// <summary>Per-board: the APPARENT width in real metres the two-hand resize last authored,
    /// or 0 when it never has. The rig-neutral twin of the grab-written <see cref="TrayScale"/> —
    /// see the bind for why a localScale alone could not answer "how big did he make it".</summary>
    private static readonly ConfigEntry<float>[] _boardApparentWidth = new ConfigEntry<float>[3];

    // [Cards] SlotCardFill is GONE (retired 2026-08-11). It scaled a slotted card up to fill the
    // physical recess, but the blinking slot overlays took their size from a code literal, so the
    // card that landed was 6.6 % wider than the overlay that had just marked the spot and there was
    // no dial that could close the gap. Both are the per-board SlotOverlayScale_{board} now — see
    // PlayTray.SlotCardScale. Deliberately NOT bound any more (tombstone, ConfirmUndoSize pattern).

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

    // [Cards] PileViewer and [Cards] ActivePile are GONE (user ruling 2026-08-11): the pile
    // stacks and the active-cards column are the ONLY way to see those piles in VR, so they are
    // no longer optional — the features are unconditionally on (CardsDriver builds them whenever
    // an active local hand exists). The keys are not re-bound on purpose: an unbound key is
    // simply dropped from the user's cfg on the next save.

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
    // NOT PER BOARD ANY MORE (2026-08-25) — one shared entry each. The per-board component of a
    // keycap seat is carried by the BOARD, as the ButtonSeat1/2/3 and ShortRest/LongRestToken anchor
    // empties every re-authored plate exports; what is left for the player to dial is a DELTA on
    // top of that, and a delta is the same delta on all three. See Defaults.Cards' seat-family note
    // and BoardAnchors.StackDelta.
    private static ConfigEntry<Vector3> _restButtonOffset = null!;
    private static ConfigEntry<float> _restButtonDiameter = null!;
    private static ConfigEntry<Vector3> _confirmUndoOffset = null!;
    // [Cards] ConfirmUndoSize_{board} is GONE (retired 2026-08, user report "hat keinen Effekt").
    // It was the square Confirm/Undo side length until the button-family split (e0432fe) moved the
    // square caps onto [BoardButtons] Width/Height EXCLUSIVELY; after that only the non-default
    // ROUND shape still read it, so with the shipped Square shape the dial did nothing. The round
    // diameter now comes from [BoardButtons] Width too (PlayTray.BuildButtons), so ONE family sizes
    // the caps in both shapes. Not re-bound on purpose: an unbound key is dropped from the user's
    // cfg on the next save, and rebase-defaults.py reports a tuned cfg still carrying it as
    // UNMAPPED — exactly right for a retired key.
    // Item-use clip-in slot (items rework): a card-sized recess UNDER the board next to the
    // Confirm/Undo decision buttons — dropping a held, usable item card into it USES the item.
    // Per-board offset, mirrors ConfirmUndoOffset's binding/accessor/debug-menu wiring exactly.
    private static readonly ConfigEntry<Vector3>[] _itemUseSlotOffset = new ConfigEntry<Vector3>[3];
    // Item-card fan offset (items rework, req #2): nudges the ITEM pile fan + held item card pose
    // INDEPENDENTLY of the ability-card fan (item cards are a different, near-square shape). Per-board
    // Vector3, mirrors ConfirmUndoOffset's binding/accessor/debug-menu wiring. Read live by ItemsPile.
    private static readonly ConfigEntry<Vector3>[] _itemCardOffset = new ConfigEntry<Vector3>[3];
    // The two ENGRAVED rest captions, one dial each ("jeweils"): the short caption rides up into the
    // board's top margin and the long one down into its bottom margin, so they cannot share a nudge.
    private static readonly ConfigEntry<Vector3>[] _shortRestCaptionOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _longRestCaptionOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _slotOverlayOffset = new ConfigEntry<Vector3>[3];
    // Item 1: the two slot overlays move together as a PAIR (Overlays element); this spacing
    // spreads them apart along the inter-slot (board long) axis — slot 0 by −½, slot 1 by +½.
    private static readonly ConfigEntry<float>[] _slotOverlaySpacing = new ConfigEntry<float>[3];
    // THE SLOT-OVERLAY SIZE, and — this is the point of it — the size of the card that lands there.
    // User report 2026-08-10/11: "die zwei Bereiche die Blinken … Overlay-Größe und -Position sollen
    // auch die Karten regieren, die auf dem Board liegen", answered with "exakt ausfüllen". The
    // position half of that coupling already existed (_slotOverlayOffset + spacing, read by both the
    // glows and SlotHomeOffsetFor); this is the missing size half, and it retires [Cards]
    // SlotCardFill, whose 1.45 it inherits so the card's fit in the recess is unchanged.
    private static readonly ConfigEntry<float>[] _slotOverlayScale = new ConfigEntry<float>[3];
    private static readonly ConfigEntry<Vector3>[] _initiativeOffset = new ConfigEntry<Vector3>[3];
    private static readonly ConfigEntry<Vector3>[] _pickBannerOffset = new ConfigEntry<Vector3>[3];
    // Hover-hint (game tooltip) anchor above the board, user report 2026-08-03: a hint that BELONGS
    // to the board -- an ability card lying in a slot, a docked decision button -- is parked above
    // the board's top edge, and the exact spot has to be dial-able like every other board element.
    // Read LIVE by WorldUI.WorldTooltips every LateUpdate, so a nudge moves an OPEN hint.
    private static readonly ConfigEntry<Vector3>[] _hoverHintOffset = new ConfigEntry<Vector3>[3];
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
    // The two STACK SPACINGS. Dimensionless multipliers on the board's own anchor pitch now, not
    // per-board metre gaps — see BoardAnchors.StackDelta for why that is the only form that can be
    // one shared setting across three boards whose recess pitches differ by 8 %.
    private static ConfigEntry<float> _restStackSpacing = null!;
    private static ConfigEntry<float> _buttonStackSpacing = null!;
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

    // ---- THE HAND FAN's CHARACTER-SWAP EXCHANGE (2026-08-09) ---------------------------------
    // Deliberately NOT the FanOpen*/FanClose* dials above. Those animate the fan APPEARING and
    // DISAPPEARING as a whole — a centre-out ripple, because a fan that unfolds from its middle is
    // what a hand of cards being raised looks like. These animate one hand being EXCHANGED for
    // another while the fan stays up, which is a different gesture and must not be mistaken for a
    // close followed by an open: it is sequenced along the arc instead of out from its centre, and
    // both halves run at once. See Cards/CardFan.cs's exchange region.

    /// <summary>Swap: seconds ONE card takes to fly out of (or into) the fan during a character exchange.</summary>
    internal static ConfigEntry<float> FanSwapDuration = null!;

    /// <summary>Swap: extra start delay per card ALONG THE ARC — the wipe's moving front.</summary>
    internal static ConfigEntry<float> FanSwapStagger = null!;

    /// <summary>Swap: how much of each slot's departure the matching arrival overlaps (0..1).</summary>
    internal static ConfigEntry<float> FanSwapOverlap = null!;

    /// <summary>Swap: how far past the arc's end the gather/deal point sits, real meters.</summary>
    internal static ConfigEntry<float> FanSwapTravel = null!;

    /// <summary>Swap: mid-flight depth amplitude — the leaver ducks back by it, the arriver bows toward the viewer by it.</summary>
    internal static ConfigEntry<float> FanSwapArc = null!;

    /// <summary>Swap: the roll (degrees) the two halves counter-rotate through.</summary>
    internal static ConfigEntry<float> FanSwapSpinDegrees = null!;

    /// <summary>Swap: the size a card has at the gather/deal point, as a fraction of its seated size.</summary>
    internal static ConfigEntry<float> FanSwapSeedScale = null!;

    /// <summary>Swap: back-ease strength — the arriving card's overshoot-and-settle and the leaver's wind-up.</summary>
    internal static ConfigEntry<float> FanSwapSettleOvershoot = null!;

    // ---- THE ITEM FAN's OWN open/close animation (item-pile presence pass) --------------------
    // Deliberately NOT the FanOpen*/FanClose* dials above. Those animate the HAND fan, which opens
    // an arm's length in front of a face against whatever the player is looking at; these animate
    // the ITEM fan, which blooms out of a stack on a board that in mixed reality sits on a real
    // table in a real room. The two want different amounts of motion for exactly that reason —
    // see Defaults.Cards.cs for the report these were authored against.

    /// <summary>Item fan: seconds ONE chip takes to fly out of the items stack into its arc slot.</summary>
    internal static ConfigEntry<float> ItemFanOpenDuration = null!;

    /// <summary>Item fan: extra start delay per slot of distance from the fan centre — the deal-out ripple.</summary>
    internal static ConfigEntry<float> ItemFanOpenStagger = null!;

    /// <summary>Item fan: how far the flying chip bows TOWARD the viewer at mid-flight, real meters.</summary>
    internal static ConfigEntry<float> ItemFanOpenArc = null!;

    /// <summary>Item fan: the roll (degrees) a chip unwinds from as it flies out — outward-signed per side.</summary>
    internal static ConfigEntry<float> ItemFanOpenSpinDegrees = null!;

    /// <summary>Item fan: the size a chip starts at on the stack, as a fraction of its seated size.</summary>
    internal static ConfigEntry<float> ItemFanSeedScale = null!;

    /// <summary>Item fan: back-ease strength — the overshoot-and-settle at the end of the fly-out
    /// and the wind-up before the collapse. 0 = a plain ease with no reversal.</summary>
    internal static ConfigEntry<float> ItemFanSettleOvershoot = null!;

    /// <summary>Item fan: seconds ONE chip takes to fall back into the items stack on close.</summary>
    internal static ConfigEntry<float> ItemFanCloseDuration = null!;

    /// <summary>Item fan: per-slot close delay — the fold-in runs outermost chip first.</summary>
    internal static ConfigEntry<float> ItemFanCloseStagger = null!;

    // ---- THE "an item can be USED" cue, and the berth a card is used IN --------------------------
    // One rhythm, two sentences: the closed items pile says LOOK HERE (rings thrown outward), the
    // item-use recess says PUT IT HERE (a ring closing inward). See Defaults.Cards.cs for the user
    // report each of these was authored against and the reasoning behind every number.

    /// <summary>Item cue: seconds per heartbeat — the shared clock of the pile's rings, the ember
    /// bursts, the item cards' frames and the use berth's ping.</summary>
    internal static ConfigEntry<float> ItemCueBeatSeconds = null!;

    /// <summary>Item cue: how far a ring travels out from the closed pile, as a factor of the pile's
    /// own footprint. 1 = the ring never leaves the stack (no travel).</summary>
    internal static ConfigEntry<float> ItemCueRingReach = null!;

    /// <summary>Item cue: peak opacity of the pile's travelling rings. 0 = no rings, embers only.</summary>
    internal static ConfigEntry<float> ItemCueRingAlpha = null!;

    /// <summary>Item cue: embers per second drifting off the closed items pile (0 = none).</summary>
    internal static ConfigEntry<float> ItemCueEmberRate = null!;

    /// <summary>Item cue: ember size multiplier over the original (subtle-by-construction) size.</summary>
    internal static ConfigEntry<float> ItemCueEmberSize = null!;

    /// <summary>Item berth: thickness of the card-shaped outline around the use recess, real meters.</summary>
    internal static ConfigEntry<float> ItemBerthRingThickness = null!;

    /// <summary>Item berth: peak brightness of the warm ADDITIVE field inside the recess. 0 = a fully
    /// open berth (outline only), which is the extreme the old black plate was the opposite of.</summary>
    internal static ConfigEntry<float> ItemBerthGlow = null!;

    /// <summary>Item berth: seconds per inward "put it here" ping (0 = no ping).</summary>
    internal static ConfigEntry<float> ItemBerthPingSeconds = null!;

    /// <summary>Item berth: how far outside the card rect that ping starts, as a factor. 1 = no travel.</summary>
    internal static ConfigEntry<float> ItemBerthPingReach = null!;

    /// <summary>Item berth: seconds the recess takes to grow in / collapse out.</summary>
    internal static ConfigEntry<float> ItemBerthRevealSeconds = null!;

    /// <summary>
    /// Master switch over ALL five card/fan sounds below (menu overhaul ruling 6, 2026-08-11 —
    /// the user kept the five sound STRINGS power-user-only and ordered one everyday on/off
    /// switch instead: "Setze erstmal alle Vorschläge zu den Settings deinerseits so um").
    /// A pure AND on top of the strings: it never modifies them, so a player's hand-picked
    /// audio items survive toggling. "" in a string entry still means that one sound is silent
    /// on its own.
    /// </summary>
    internal static ConfigEntry<bool> CardSoundsEnabled = null!;

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
        // RestButtonOffset / RestButtonDiameter / ConfirmUndoOffset / RestButtonSpacing /
        // GenericButtonSpacing STOOD HERE as five three-entry rows and are GONE (2026-08-25). They
        // are single shared Defaults constants now — Defaults.RestButtonOffset,
        // Defaults.RestButtonDiameter, Defaults.ConfirmUndoOffset, Defaults.RestStackSpacing,
        // Defaults.ButtonStackSpacing — because the per-board part of a keycap seat comes off the
        // board's own anchors now. A remote board reads the shared constants directly, so the
        // reason this table is `internal` still holds for everything left in it.
        // ConfirmUndoSize is GONE (retired 2026-08) — the Confirm/Undo cap size is [BoardButtons]
        // Width/Height for both shapes now; see the tombstone at the _confirmUndoOffset field.
        internal static readonly Vector3[] SlotOverlayOffset = { Defaults.SlotOverlayOffset_Oak, Defaults.SlotOverlayOffset_Steel, Defaults.SlotOverlayOffset_Bronze };
        internal static readonly float[] SlotOverlaySpacing = { Defaults.SlotOverlaySpacing_Oak, Defaults.SlotOverlaySpacing_Steel, Defaults.SlotOverlaySpacing_Bronze };
        internal static readonly float[] SlotOverlayScale = { Defaults.SlotOverlayScale_Oak, Defaults.SlotOverlayScale_Steel, Defaults.SlotOverlayScale_Bronze };
        internal static readonly Vector3[] InitiativeOffset = { Defaults.InitiativeOffset_Oak, Defaults.InitiativeOffset_Steel, Defaults.InitiativeOffset_Bronze };
        internal static readonly float[] DecisionGap = { Defaults.DecisionGap_Oak, Defaults.DecisionGap_Steel, Defaults.DecisionGap_Bronze };
        internal static readonly Vector3[] PickBannerOffset = { Defaults.PickBannerOffset_Oak, Defaults.PickBannerOffset_Steel, Defaults.PickBannerOffset_Bronze };
        internal static readonly Vector3[] HoverHintOffset = { Defaults.HoverHintOffset_Oak, Defaults.HoverHintOffset_Steel, Defaults.HoverHintOffset_Bronze };
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

    /// <summary>
    /// The sentence every size dial bounded by the 2026-08-13 settings audit carries, so the
    /// reason is in the config file next to the value and nobody re-opens the range by accident.
    /// The user's ruling, verbatim: "Entferne weiterhin die Optionen die den Spielfluss in VR
    /// beschädigen können. Die Einstellungen sollen nur Optionale Inhalte einstellbar machen."
    /// A size is taste; a size of ZERO deletes the thing, and the things below are not optional.
    /// </summary>
    private const string BoundedNote =
        "BOUNDED by the 2026-08-13 settings audit: how big this is may be taste, but 0 would "
        + "remove it outright, and it is not optional content. ";

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
                "opens the fan). The shipped default, printed under this text, is a comfortable " +
                "supination well past vertical (Demeo's own threshold is ~37°). NOTE: the scale CHANGED from the old v2 " +
                "measure (whose default was 95 on a 0-180 scale) — old out-of-range values " +
                "are auto-reset once. Live-tunable from the in-VR debug menu (Fan category).",
                new AcceptableValueRange<float>(15f, 85f)));
        RevealExitDegrees = _file.Bind("Cards", "RevealExitDegrees", Defaults.RevealExitDegrees,
            new ConfigDescription(
                "RevealMode=tilt: hand roll in DEGREES below which the fan CLOSES (same " +
                "roll scale as RevealEnterDegrees: 0 = flat, 90 = palm fully toward " +
                "the face). Sitting BELOW the enter angle is what keeps the gate from chattering " +
                "at the boundary; the gate always clamps this below RevealEnterDegrees, and " +
                "both shipped values are printed under their own rows. Live-tunable from the in-VR debug menu (Fan category).",
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
        FanRadius = _file.Bind("Cards", "FanRadius", Defaults.FanRadius, new ConfigDescription(
            "Palm fan arc radius in real-world meters (diorama scale is applied automatically). " +
            "BOUNDED (user ruling 2026-08-13): at 0 the whole hand collapses onto one point and " +
            "no card can be picked out of it any more. Range 0.05-0.5.",
            new AcceptableValueRange<float>(0.05f, 0.5f)));
        FanArcDegrees = _file.Bind("Cards", "FanArcDegrees", Defaults.FanArcDegrees,
            "LEGACY — no effect, superseded by FanArcSweepDegrees. Nothing reads this value. It was " +
            "the maximum total fan arc in degrees; FanArcSweepDegrees replaced it and was seeded to " +
            "this default × 1.3 (= 91°). Kept bound so existing cfg files load unchanged — an " +
            "unbound key is silently dropped from your file on the next save.");
        FanPalmOffset = _file.Bind("Cards", "FanPalmOffset", Defaults.FanPalmOffset,
            "Height of the fan pivot above the palm center, real-world meters.");
        CardWidth = _file.Bind("Cards", "CardWidth", Defaults.CardWidth, new ConfigDescription(
            "Physical card width in meters (real poker card = 0.0635). Height keeps the 63.5:88 " +
            "aspect. BOUNDED (user ruling 2026-08-13): a card is the thing you read and play, so " +
            "the size may be taste but 0 may not — at the low end it stops being readable and at " +
            "0 it stops existing. Range 0.03-0.15.",
            new AcceptableValueRange<float>(0.03f, 0.15f)));
        InspectScale = _file.Bind("Cards", "InspectScale", Defaults.InspectScale, new ConfigDescription(
            "Scale multiplier applied to a card while it is held (natural-size inspection). " +
            "BOUNDED (user ruling 2026-08-13): holding a card up to read it is the gesture this " +
            "multiplies, and 0 would make the held card vanish out of your hand. Range 0.5-4.",
            new AcceptableValueRange<float>(0.5f, 4f)));
        HeldTiltDegrees = _file.Bind("Cards", "HeldTiltDegrees", Defaults.Cards_HeldTiltDegrees,
            "LEGACY — no effect, superseded by HeldFaceBias. Nothing reads this value (hardware " +
            "test #13). The old palm-aligned held pose required a hard supination to read the card; " +
            "the pose is now controlled by HeldFaceBias instead. Kept bound only so existing config " +
            "files load cleanly. NOTE: [FigureGrab] HeldTiltDegrees is a DIFFERENT, live entry.");
        HeldFaceBias = _file.Bind("Cards", "HeldFaceBias", Defaults.HeldFaceBias,
            "Held card readability (test #13): degrees the card FACE leans from 'flat on the " +
            "palm' (0 = old pose, face along the palm normal — readable only by twisting the " +
            "wrist) back toward the wrist/forearm. In a relaxed controller grip (grip pose " +
            "pitched, see the per-style [Hands] seat pitch) the fingers point forward " +
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
            "sideways. Authored for the RIGHT hand; the X term is automatically " +
            "sign-flipped on the left hand so the card sits at the same anatomical " +
            "spot in both. Example {x:0, y:0.01, z:0.02} lifts the card 1 cm off " +
            "the palm and shifts it 2 cm toward the fingertips.");
        InHandHold = _file.Bind("Cards", "InHandHold", Defaults.InHandHold,
            "Take a card INTO YOUR HAND (2026-08-29). Normally a grabbed card floats so its face " +
            "always turns to you, however you move your wrist - that stays exactly as it was. " +
            "With this on you get a SECOND way to hold one: while you are holding a card, press " +
            "and HOLD THE GRIP button. The card then sits rigidly in your fist - your fingers " +
            "close on its bottom edge and turning your wrist turns the card, so you can hold it " +
            "up and show its face to another player. Let the grip go and it floats readable " +
            "again; press it again to show it again. It works in EITHER hand, at any point during " +
            "the hold, and everywhere a card can be taken at all - the campaign map room included, " +
            "and when it is not your turn. The ghost hand belongs to the reading mode only: a hand " +
            "that is really holding a card stays solid. Off: the grip does nothing while a card is " +
            "held, exactly as before.");
        InHandPitch = _file.Bind("Cards", "InHandPitch", Defaults.InHandPitch, new ConfigDescription(
            "In-hand grip: how far the card leans back, degrees. The card is held the way you " +
            "really hold one - thumb flat on its face near a bottom corner, the other four " +
            "fingers curled behind it - so which WAY it faces is settled by your thumb and is not " +
            "a setting. This is the one thing left to choose: 0 points the card straight out past " +
            "your fingertips, 90 stands it straight up out of your fist, and the default sits " +
            "between them, where a hand really carries a card. Only affects the in-hand mode - " +
            "the floating reading pose is HeldFaceBias.",
            new AcceptableValueRange<float>(0f, 120f)));
        InHandPinchOffset = _file.Bind("Cards", "InHandPinchOffset", Defaults.InHandPinchOffset,
            "In-hand grip fine-tune: offset (meters) ADDED to the modelled pinch point, in " +
            "GrabAnchor-local axes: +Y out of the palm, +Z along the fingers, +X sideways. " +
            "Authored for the RIGHT hand; the X term is automatically sign-flipped on the left " +
            "hand so the card sits at the same anatomical spot in both - same convention as " +
            "HeldPinchOffset. Ships at zero: the fingers are modelled FOR this pose, so there " +
            "should be nothing to correct - this is here for taste, not for a known error.");
        InHandGraspSeconds = _file.Bind("Cards", "InHandGraspSeconds", Defaults.InHandGraspSeconds,
            new ConfigDescription(
                "In-hand grip: how long your hand takes to CLOSE on the card, in seconds - and to " +
                "open again when you let the grip go. Your fingers travel into the grip and the " +
                "card travels with them, on one shared, eased motion: it starts from rest, speeds " +
                "up, and arrives at rest, rather than snapping or springing. Short on purpose. 0 " +
                "is not allowed - that IS the snap this exists to remove.",
                new AcceptableValueRange<float>(0.05f, 1f)));
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
        // THE RANGE IS THE CLAMP THE CODE ALREADY APPLIES (2026-08-22 settings audit). The
        // description PROMISED "0.5-2" and nothing enforced it: ClampedTrayScale below clamps at
        // read, so every arrow press past 2 moved the number and not the board. It also matters
        // that the gesture writes this key — the two-handed tray grab persists what it reached —
        // and the same clamp bounds that gesture, so declaring it changes no reachable pose.
        // The live cfg ships 2, exactly the upper end, which the range includes.
        PokePadPixels = _file.Bind("Cards", "PokePadPixels", Defaults.PokePadPixels,
            new ConfigDescription(
                "How far past a card half's small DEFAULT-ACTION button (Standard 2 Move / Attack) " +
                "the FINGERTIP hitbox reaches, in the card's own pixels — the physical press with " +
                "the extended finger lands on the small button from further away. 0 = no pad. The " +
                "LASER's target is never enlarged by this. The vertical pad is clamped so the two " +
                "actions of one card can never share a finger. (User 2026-09-03: 'man trifft es " +
                "nicht weil es zu klein ist'.)",
                new AcceptableValueRange<float>(PokePadPixelsMin, PokePadPixelsMax)));
        TrayScale = _file.Bind("Cards", "TrayScale", Defaults.TrayScale,
            new ConfigDescription(
                "Control board size multiplier (0.5–2). Written automatically by the two-handed " +
                "tray grab (grip the handle bar with both hands and spread/pinch); edit only to reset.",
                new AcceptableValueRange<float>(TrayScaleMin, TrayScaleMax)));
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
        // TrayPitch DELIBERATELY KEEPS ITS OPEN STEPPER. The 2026-08-22 settings audit listed it
        // among the fifteen dials that "keep moving past a clamp the code applies at read time",
        // and it is the one entry of the fifteen where that is not true: EffectiveTrayPitch clamps
        // it ONLY in the LimitedPitch mode, against the LIVE per-board window; in the Free mode it
        // returns the raw value, deliberately ("Free never clamps" — the board can end up upside
        // down and that is the mode's whole point). A declared AcceptableValueRange would be a
        // NARROWING, and worse, it would clamp what the GESTURE writes: PlayTray.3.Pose persists
        // whatever pitch you released the handle bar at. The live cfg carries 45.13076, a value
        // his own grab authored. So this row stays honest as it is, and the fifteenth range is
        // not declared — see the report for the audit correction.
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
        // SlotCardFill is deliberately NOT bound any more — see the tombstone at its former field.
        // Its successor is the per-board SlotOverlayScale_{board}, bound with the other per-board
        // dials below, which sizes the blinking overlay and the card that lands in it together.
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
        // PER-PILE FAN SPREAD (user request 2026-08-03: "Der Fächer der Items muss breiter sein,
        // also mehr Abstand zwischen den Karten damit man es physisch gut greifen kann — ich möchte
        // das wie den Handkarten pro pile in den Debug-Einstellungen verändern können"). The three
        // board fans (items, discard, burnt) shared two hardcoded constants; each gets its own pair
        // now, read LIVE so a stepper widens an OPEN fan immediately.
        string[] pileNames = { "Items", "Discard", "Burnt" };
        float[] stepSeeds = { Defaults.FanStepDegrees_Items, Defaults.FanStepDegrees_Discard, Defaults.FanStepDegrees_Burnt };
        float[] radiusSeeds = { Defaults.FanRadiusFactor_Items, Defaults.FanRadiusFactor_Discard, Defaults.FanRadiusFactor_Burnt };
        for (int p = 0; p < 3; p++)
        {
            _fanStepDegrees[p] = _file.Bind("Cards", $"FanStepDegrees_{pileNames[p]}", stepSeeds[p],
                new ConfigDescription(
                    $"[{pileNames[p]} fan] angle between two neighbouring cards, degrees — the SPREAD. " +
                    "Bigger = the cards sit further apart, which is what makes a single card easy to " +
                    "grab physically. The fan still never exceeds its total arc, so on a very full " +
                    "pile the spread is capped to keep every card reachable.",
                    new AcceptableValueRange<float>(2f, 30f)));
            _fanRadiusFactor[p] = _file.Bind("Cards", $"FanRadiusFactor_{pileNames[p]}", radiusSeeds[p],
                new ConfigDescription(
                    $"[{pileNames[p]} fan] radius as a multiple of the hand fan's radius ([Cards] " +
                    "FanRadius). Bigger = a wider, flatter arc, which also spaces the cards out.",
                    new AcceptableValueRange<float>(0.5f, 4f)));
        }
        // THE ITEM FAN's OPEN/CLOSE ANIMATION (user report 2026-08-08: "Ich mag die Animation im
        // Item-Pile sehr aber sie ist (insbesondere in mixed Reality) etwas zu dezent."). Read LIVE
        // by Cards/ItemsPile.cs's emerge/collapse region and mirrored to peers through extension
        // record 28 — the standing 1:1 ruling names ANIMATIONS explicitly, so a player who dials
        // these further must be seen dialling them further on every other screen.
        //
        // WHY THE RANGES START AT 0: every one of these is an amplitude, and 0 on all of them is
        // exactly the previous build's animation (straight chord, one shared timing, no roll, no
        // reversal). That is the honest "turn it back down" position, and it is reachable from the
        // debug steppers without editing a file.
        ItemFanOpenDuration = _file.Bind("Cards", "ItemFanOpenDuration", Defaults.ItemFanOpenDuration,
            new ConfigDescription(
                "Item fan opening: seconds ONE item card takes to fly out of the items stack into " +
                "its place in the arc (unscaled time — it runs even while the game pauses). The " +
                "whole fan takes this PLUS the last card's stagger delay. Longer = the flight is " +
                "readable rather than a flick; too long and opening the fan feels slow.",
                new AcceptableValueRange<float>(0.05f, 0.9f)));
        ItemFanOpenStagger = _file.Bind("Cards", "ItemFanOpenStagger", Defaults.ItemFanOpenStagger,
            new ConfigDescription(
                "Item fan opening: extra start delay in seconds PER PLACE of distance from the " +
                "middle of the fan — the cards deal outwards one after another instead of all " +
                "leaving the stack at once. This is the single biggest reason the animation reads " +
                "on a mixed-reality background: a moving FRONT that crosses the fan is something " +
                "the eye follows, where one simultaneous blob-move is a flicker a busy room " +
                "swallows. 0 = all cards start together (the old look).",
                new AcceptableValueRange<float>(0f, 0.2f)));
        ItemFanOpenArc = _file.Bind("Cards", "ItemFanOpenArc", Defaults.ItemFanOpenArc,
            new ConfigDescription(
                "Item fan opening: how far the flying card bows TOWARD YOU at the middle of its " +
                "flight, real meters, before settling back into the arc plane. It turns a straight " +
                "slide into a thrown curve, and — the mixed-reality half — it moves the card in " +
                "DEPTH, so both eyes see it separate from the room behind it. Stereo separation is " +
                "a cue passthrough cannot mask. 0 = the old straight line.",
                new AcceptableValueRange<float>(0f, 0.2f)));
        ItemFanOpenSpinDegrees = _file.Bind("Cards", "ItemFanOpenSpinDegrees", Defaults.ItemFanOpenSpinDegrees,
            new ConfigDescription(
                "Item fan opening: the roll (degrees) a card unwinds from while it flies — signed " +
                "outward, so the fan visibly UNFOLDS instead of sliding open. A rotation changes " +
                "the card's outline, and an outline change is legible against a background that " +
                "already has movement and contrast of its own. 0 = no roll (the old look).",
                new AcceptableValueRange<float>(0f, 180f)));
        ItemFanSeedScale = _file.Bind("Cards", "ItemFanSeedScale", Defaults.ItemFanSeedScale,
            new ConfigDescription(
                "Item fan: the size a card starts at on the stack (and shrinks to on close), as a " +
                "fraction of its seated size in the arc. Smaller = more growth over the flight, " +
                "which is the second depth cue after the bow — a card that doubles in size is " +
                "coming at you, not sliding across a picture.",
                new AcceptableValueRange<float>(0.02f, 1f)));
        ItemFanSettleOvershoot = _file.Bind("Cards", "ItemFanSettleOvershoot", Defaults.ItemFanSettleOvershoot,
            new ConfigDescription(
                "Item fan: the strength of the overshoot at the END of the fly-out (the card " +
                "passes its slot and swings back into it) and of the matching wind-up before the " +
                "collapse. A REVERSAL OF DIRECTION is the most noticeable event motion has, and " +
                "unlike speed it costs no extra travel — which is why it is here rather than a " +
                "simply faster animation. About 1.4 is a ~5 % overshoot; 0 = a plain ease that " +
                "just decelerates to a stop (the old look).",
                new AcceptableValueRange<float>(0f, 3f)));
        ItemFanCloseDuration = _file.Bind("Cards", "ItemFanCloseDuration", Defaults.ItemFanCloseDuration,
            new ConfigDescription(
                "Item fan closing: seconds ONE item card takes to fall back into the items stack " +
                "before it disappears (unscaled time). The cards are detached from the fan first, " +
                "so this plays out in full however the fan was closed.",
                new AcceptableValueRange<float>(0.05f, 0.9f)));
        ItemFanCloseStagger = _file.Bind("Cards", "ItemFanCloseStagger", Defaults.ItemFanCloseStagger,
            new ConfigDescription(
                "Item fan closing: per-place delay, run OUTERMOST CARD FIRST so the fold-in is the " +
                "opening exactly reversed. 0 = every card leaves at the same moment.",
                new AcceptableValueRange<float>(0f, 0.2f)));

        // THE "an item can be USED" CUE and the BERTH it is used in (user reports 2026-08-09:
        // "Die Animation über dem Pile … ist immer noch zu dezent und kann man schnell übersehen"
        // and "Überarbeite das Aussehen des Item-Overlays … einfach so ein schwarzes Rechteck").
        // Defaults.Cards.cs carries the derivation; the short version is that neither cue was too
        // SMALL — one had the wrong rhythm for peripheral vision and neither had a luminance edge
        // that survives an unknown passthrough background.
        ItemCueBeatSeconds = _file.Bind("Cards", "ItemCueBeatSeconds", Defaults.ItemCueBeatSeconds,
            new ConfigDescription(
                "Item cue: seconds per HEARTBEAT — the one clock the whole item cue runs on (the " +
                "rings thrown off the closed items pile, the ember bursts, the frames around usable " +
                "cards in the open fan, and the ping inside the use recess). It is a beat with a " +
                "REST in it rather than a steady breath on purpose: the corner of your eye reports " +
                "sudden change and ignores slow ramps, so the pause between beats is what makes the " +
                "next one visible. Shorter = more urgent; much longer and the rest reads as 'off'.",
                new AcceptableValueRange<float>(0.4f, 4f)));
        ItemCueRingReach = _file.Bind("Cards", "ItemCueRingReach", Defaults.ItemCueRingReach,
            new ConfigDescription(
                "Item cue: how far each ring of light travels out from the closed items pile, as a " +
                "multiple of the pile's own size. A ring that GROWS is a change of shape, and a " +
                "change of shape is the one thing a bright, busy mixed-reality room cannot swallow " +
                "— brightness it competes with and wins. 1 = the ring never leaves the stack.",
                new AcceptableValueRange<float>(1f, 5f)));
        ItemCueRingAlpha = _file.Bind("Cards", "ItemCueRingAlpha", Defaults.ItemCueRingAlpha,
            new ConfigDescription(
                "Item cue: peak opacity of those rings. They are drawn as a bright core with a DARK " +
                "edge on both sides, so they keep a visible outline over a white wall and over a " +
                "dark room alike. 0 = no rings at all (drifting embers only, the old cue).",
                new AcceptableValueRange<float>(0f, 1f)));
        ItemCueEmberRate = _file.Bind("Cards", "ItemCueEmberRate", Defaults.ItemCueEmberRate,
            new ConfigDescription(
                "Item cue: soft gold embers per second lifting off the closed items pile. They now " +
                "arrive in a PUFF on each heartbeat instead of as a steady trickle. 0 = no embers.",
                new AcceptableValueRange<float>(0f, 60f)));
        ItemCueEmberSize = _file.Bind("Cards", "ItemCueEmberSize", Defaults.ItemCueEmberSize,
            new ConfigDescription(
                "Item cue: how much bigger each ember is than the original 'subtle by construction' " +
                "size. A few millimetres across is under a degree of visual angle at reading " +
                "distance, which is where a mote stops being an object and becomes shimmer.",
                new AcceptableValueRange<float>(0.5f, 5f)));
        ItemBerthRingThickness = _file.Bind("Cards", "ItemBerthRingThickness", Defaults.ItemBerthRingThickness,
            new ConfigDescription(
                "Item berth: how thick the card-shaped outline around the use recess is drawn, in " +
                "real meters. The recess is a hollow outline with an OPEN middle now, so this line " +
                "is the whole shape — thick enough to read from across the table, thin enough that " +
                "it never becomes a plate again.",
                new AcceptableValueRange<float>(0.001f, 0.02f)));
        ItemBerthGlow = _file.Bind("Cards", "ItemBerthGlow", Defaults.ItemBerthGlow,
            new ConfigDescription(
                "Item berth: peak brightness of the warm light FILLING the recess. It is added " +
                "light, not a dark plate — so on a dark scene the berth glows and in mixed reality " +
                "your own room shows through it instead of a black rectangle. 0 = a completely " +
                "open berth (just the outline and the ping).",
                new AcceptableValueRange<float>(0f, 1f)));
        ItemBerthPingSeconds = _file.Bind("Cards", "ItemBerthPingSeconds", Defaults.ItemBerthPingSeconds,
            new ConfigDescription(
                "Item berth: seconds per INWARD ping — a ring that closes onto the card outline, " +
                "the mirror image of the outward rings the items pile throws. Outward says 'look " +
                "here'; inward says 'put it in here'. 0 = no ping.",
                new AcceptableValueRange<float>(0f, 4f)));
        ItemBerthPingReach = _file.Bind("Cards", "ItemBerthPingReach", Defaults.ItemBerthPingReach,
            new ConfigDescription(
                "Item berth: how far outside the card outline that inward ping starts, as a " +
                "multiple of the card. Bigger = it sweeps in from further away and is easier to " +
                "catch out of the corner of your eye. 1 = no travel.",
                new AcceptableValueRange<float>(1f, 3f)));
        ItemBerthRevealSeconds = _file.Bind("Cards", "ItemBerthRevealSeconds", Defaults.ItemBerthRevealSeconds,
            new ConfigDescription(
                "Item berth: seconds the recess takes to GROW IN when an item becomes placeable and " +
                "to collapse out again when it stops. It used to blink in and out instantly — the " +
                "one transition in the item flow that was never animated.",
                new AcceptableValueRange<float>(0.05f, 1f)));
        BoardMinWidthMeters = _file.Bind("Cards", "BoardMinWidthMeters", Defaults.BoardMinWidthMeters,
            new ConfigDescription(
                "Smallest the control board may ever get, measured as its APPARENT WIDTH in real " +
                "meters — i.e. how wide it actually looks to you, not an internal factor. This is " +
                "what stops the board from collapsing to a speck: in FOLLOW mode the board hangs " +
                "off the rig, so shrinking YOURSELF with the world grab shrinks it too, and " +
                "alternating pin/grow/follow/shrink multiplies those two shrinks together. The " +
                "limit is enforced every frame on the final size, so no combination of gestures " +
                "can get past it.",
                new AcceptableValueRange<float>(0.05f, 1f)));
        BoardMaxWidthMeters = _file.Bind("Cards", "BoardMaxWidthMeters", Defaults.BoardMaxWidthMeters,
            new ConfigDescription(
                "Largest the control board may ever get, as its APPARENT WIDTH in real meters " +
                "(see BoardMinWidthMeters). Kept at least a little above the minimum whatever the " +
                "two values say.",
                new AcceptableValueRange<float>(0.2f, 4f)));
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
        SpawnMaxReachMeters = _file.Bind("Cards", "SpawnMaxReachMeters", Defaults.SpawnMaxReachMeters,
            new ConfigDescription(
                "ARRIVAL GUARD: how far from your head the control board may be when a scenario " +
                "puts you down, real meters. When a scenario seats you - and ONLY then, plus at " +
                "each further re-seat the mod itself performs during that arrival - the board's " +
                "distance from your head is measured; further out than this and it is put back at " +
                "the starting spot above. It is deliberately a little wider than that spot (about " +
                "0.53 m out) and a little tighter than the 1.2 m the placement maths will ever " +
                "produce, so a board that has drifted to the very edge of reach is re-seated " +
                "instead of being left at full stretch. Once the arrival is over the board is " +
                "never moved again for being far away - walking away from a fixed board is not a " +
                "fault.",
                new AcceptableValueRange<float>(0.3f, 3f)));
        SpawnMaxBearingDegrees = _file.Bind("Cards", "SpawnMaxBearingDegrees", Defaults.SpawnMaxBearingDegrees,
            new ConfigDescription(
                "ARRIVAL GUARD: how far to the SIDE of your view the control board may be when a " +
                "scenario puts you down, degrees off straight ahead (0 = dead ahead, 180 = " +
                "directly behind you). Same moment and same rule as the reach above. The default " +
                "100 deg sits just outside a headset's own field of view, so anything the guard " +
                "moves really was something you would have had to turn around and look for; the " +
                "starting spot itself is about 58 deg off forward and is never touched by it.",
                new AcceptableValueRange<float>(30f, 180f)));
        SpawnBoardWidthDegrees = _file.Bind("Cards", "SpawnBoardWidthDegrees", Defaults.SpawnBoardWidthDegrees,
            new ConfigDescription(
                "How BIG the control board is when a scenario seats it beside you, given as the " +
                "angle it covers from where you are standing - so it is a size in the only unit " +
                "that means the same thing at every zoom level. The board is placed at the " +
                "starting spot above, and this angle plus that distance decide its width: at the " +
                "default 0.53 m out and 0.32 m down, 44 deg is a board that looks 50 cm wide. " +
                "Move the starting spot further away and the board grows with it, so it keeps " +
                "looking the same size. It is only ever applied when the board is SEATED for a " +
                "scenario and only when you have not sized it yourself with the two-handed grab - " +
                "once you have, your own size is kept and re-created at whatever zoom you spawn at.",
                new AcceptableValueRange<float>(15f, 90f)));
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
        // Pile stacks + active-cards column: always on — user ruling 2026-08-11: essential
        // (the only VR surface for the discard/burnt/active piles; see the tombstone above).
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
        // ---- THE KEYCAP SEAT FAMILY — SHARED, because the per-board part is the BOARD ------------
        //
        // These five were fifteen entries (_Oak/_Steel/_Bronze x five dials) until 2026-08-25. The
        // user's question that retired them: are the three boards proportionally identical now, and
        // if so can the per-board offsets go. The measurement says the boards are the same
        // 0.640 x 0.320 m plate but NOT a uniform scale of one another — the button recesses are
        // pitched 76.5 / 80.1 / 70.1 mm and the rest pads 114.8 / 120.2 / 105.0, a spread of up to
        // 8 % that no single factor reproduces. So the answer is better than "yes": he does not need
        // per-board dials, because the MESH carries the per-board layout as named anchor empties and
        // the code reads them (BoardAnchors.StackPitch / StackDelta / FitCapSize). What is left for
        // a dial is the taste on top of the board's own layout, and that is one taste.
        _confirmUndoOffset = _file.Bind("Cards", "ConfirmUndoOffset", Defaults.ConfirmUndoOffset,
            "NUDGE added to every GENERIC keycap's seat — Confirm, Undo, the turn-flow Skip and the " +
            "item 'Use' cap — on top of the button recess the board itself cut for it, board-local " +
            "meters. X/Y lie in the board plane, Z is proud depth toward the player (NEGATIVE = " +
            "prouder). 0 = each cap dead centre in its own well, which is where the board says it " +
            "goes and is what ships; a few millimetres shifts the whole column together. It cannot " +
            "push a cap out of its recess: on a board that carries a measured seat the in-plane part " +
            "is bounded by the slack between this cap and the recess wall (Z is never bounded). ONE " +
            "entry for all three boards on purpose — the per-board difference is the anchor, not " +
            "this number.");
        _buttonStackSpacing = _file.Bind("Cards", "ButtonStackSpacing",
            Defaults.ButtonStackSpacing,
            new ConfigDescription(
                "The Y GAP between the generic keycaps stacked in the board's three button recesses " +
                "(Confirm/Use on top, Undo in the middle, the turn-flow Skip at the bottom), as a " +
                "MULTIPLE of the board's own recess pitch. 1 = exactly the pitch the board was cut " +
                "with, so every cap sits centred in its own well — this is what ships, and it is " +
                "correct on all three boards without a per-board number because each board supplies " +
                "its own pitch (76.5 mm Oak, 80.1 Steel, 70.1 Bronze). Below 1 the three caps close " +
                "up toward the middle one until they overlap and start leaving their wells; at 0.25 " +
                "they are nearly on top of each other. Above 1 they spread apart, and past roughly " +
                "1.2 the outer two already stand on the flat board beside their recesses; at 3 they " +
                "are a third of the board apart. The middle cap never moves — the stack spreads " +
                "about it — and on a board with measured recesses no cap may travel further than " +
                "the slack it has in its own well, so an extreme value is bounded rather than thrown " +
                "off the board.",
                new AcceptableValueRange<float>(0.25f, 3f)));
        _restButtonOffset = _file.Bind("Cards", "RestButtonOffset", Defaults.RestButtonOffset,
            "NUDGE added to the short/long REST discs' seats on top of the rest pads the board " +
            "itself cut for them, board-local meters. X/Y in the board plane, Z = proud depth toward " +
            "the player (NEGATIVE = prouder). 0 = each disc dead centre in its own pad, which is what " +
            "ships. Bounded by the pad's own slack on a board that carries a measurement, exactly " +
            "like the keycap seats. ONE entry for all three boards — the pads' positions are the " +
            "per-board part and they come from the mesh.");
        _restStackSpacing = _file.Bind("Cards", "RestStackSpacing",
            Defaults.RestStackSpacing,
            new ConfigDescription(
                "The Y gap between the SHORT and LONG rest discs, as a MULTIPLE of the board's own " +
                "rest-pad pitch. 1 = exactly the pitch the board was cut with (114.8 mm Oak, 120.2 " +
                "Steel, 105.0 Bronze), so both discs sit centred in their pads — what ships. Below " +
                "1 they close up toward the midpoint between the pads and off their notches; at 0.25 " +
                "they nearly coincide. Above 1 they spread apart onto the flat board; at 3 they are " +
                "most of the board's height apart. Bounded by each disc's slack in its own pad on a " +
                "measured board, so the extremes are refused rather than rendered.",
                new AcceptableValueRange<float>(0.25f, 3f)));
        _restButtonDiameter = _file.Bind("Cards", "RestButtonDiameter",
            Defaults.RestButtonDiameter,
            new ConfigDescription(
                "Diameter (meters) of the round short/long-rest discs. A CEILING, not the final " +
                "size: each board's own measured rest pad shrinks the disc further so it never " +
                "overhangs the notch it sits in, which is why one shared number is right for three " +
                "boards with three different pads. At the shipped 0.091 every disc is as large as " +
                "its own pad allows (73.6 mm Oak and Steel, 60.1 Bronze). BOUNDED (user ruling " +
                "2026-08-13): at 0 the rest discs are a point nobody can press, and resting is a " +
                "move the game requires. Range 0.02-0.25.",
                new AcceptableValueRange<float>(0.02f, 0.25f)));

        foreach (ControlBoard board in System.Enum.GetValues(typeof(ControlBoard)))
        {
            int i = (int)board;
            // ConfirmUndoSize_{board} is deliberately NOT bound any more — see the tombstone at
            // the _confirmUndoOffset field. The cap size is [BoardButtons] Width/Height.
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
            _shortRestCaptionOffset[i] = _file.Bind("Cards", $"ShortRestCaptionOffset_{board}",
                Defaults.ShortRestCaptionOffset_ByBoard[i],
                $"[{board}] offset ADDED to the ENGRAVED \"KURZE RAST\" caption cut into the board " +
                "ABOVE the short-rest pad, board-local meters. X/Y in plane, Z = proud depth toward " +
                "the player (NEGATIVE = prouder). The caption already follows its own disc — every " +
                "dial that moves the pad moves the word with it — so this is the nudge on top, for " +
                "the board margin the word has to share with that board's own carving. Seeded 0.");
            _longRestCaptionOffset[i] = _file.Bind("Cards", $"LongRestCaptionOffset_{board}",
                Defaults.LongRestCaptionOffset_ByBoard[i],
                $"[{board}] offset ADDED to the ENGRAVED \"LANGE RAST\" caption cut into the board " +
                "BELOW the long-rest pad, board-local meters. X/Y in plane, Z = proud depth toward " +
                "the player (NEGATIVE = prouder). Its own dial and not the short caption's, because " +
                "the two go into opposite margins of the board and those margins are neither the " +
                "same size nor at the same depth. Seeded 0.");
            _slotOverlayOffset[i] = _file.Bind("Cards", $"SlotOverlayOffset_{board}", BoardDefaults.SlotOverlayOffset[i],
                $"[{board}] offset ADDED to the slot snap-glow / wanted-glow local position, board-local " +
                "meters. X/Y in plane, Z = proud depth toward the player (NEGATIVE = prouder). Seeded 0 (Oak).");
            _slotOverlaySpacing[i] = _file.Bind("Cards", $"SlotOverlaySpacing_{board}", BoardDefaults.SlotOverlaySpacing[i],
                $"[{board}] EXTRA gap (board-local meters) ADDED between the TWO slot overlays along the " +
                "board's long (inter-slot) axis — slot 0 (left) moves −½, slot 1 (right) +½. Seeded 0 " +
                "(the slots already space the overlays; positive spreads them apart). Item 1.");
            _slotOverlayScale[i] = _file.Bind("Cards", $"SlotOverlayScale_{board}", BoardDefaults.SlotOverlayScale[i],
                new ConfigDescription(
                $"[{board}] SIZE of the two blinking slot overlays AND of the card that comes to rest " +
                "in them — one dial for both, so what the overlay marks is exactly what the card " +
                "covers. Multiplies the card's own width/height (on top of the slot frame's 1.3x " +
                "SlotScale), so 1.0 = a card at its authored size. The teal 'wanted' pulse takes this " +
                "value exactly; the gold snap glow keeps its shipped 0.912 ratio to it so it still " +
                "reads INSIDE the teal when both show. Raise until the card nearly fills the physical " +
                "recess without overflowing the rim. Replaces the retired global SlotCardFill and " +
                "ships its 1.45 unchanged. Does NOT change the seating depth (that is " +
                "SlotCardInset). " + BoundedNote + "It sizes the PLAYED CARDS as well as their " +
                "markers, so 0 would empty the board's two card slots. Range 0.25-3.",
                new AcceptableValueRange<float>(0.25f, 3f)));
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
            _hoverHintOffset[i] = _file.Bind("Cards", $"HoverHintOffset_{board}",
                BoardDefaults.HoverHintOffset[i],
                $"[{board}] offset ADDED to the board's TOOLTIP AREA — the ONE fixed spot at the " +
                "board's top-LEFT corner where EVERY board-owned tooltip appears (hover hints, the " +
                "docked damage tip) — board-local meters. X/Y lie in the board plane, Z = proud " +
                "depth toward the player (NEGATIVE = prouder). Seeded 0 = the computed top-left: " +
                "the shown box's bottom-left corner sits just above the board's MEASURED top-left " +
                "corner, so 'top left' tracks the real board at any size. A tooltip that belongs to " +
                "a floated WINDOW or MENU is laid on that window instead and this offset does not " +
                "apply to it. Read live every LateUpdate, so a nudge moves a tooltip that is " +
                "already open. (Key name kept from the old per-hint offset for cfg compatibility.)");
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
            _boardApparentWidth[i] = _file.Bind("Cards", $"BoardApparentWidth_{board}",
                Defaults.BoardApparentWidth_ByBoard[i],
                new ConfigDescription(
                    $"[{board}] how wide this board LOOKED, in real metres, the last time you " +
                    "resized it with the two-handed grab. 0 = you never have. Written by the " +
                    "gesture, never by hand - it is what lets a scenario put the board back at " +
                    "the size you gave it instead of at a number that meant that size only at " +
                    $"the zoom you were standing at. TrayScale/BoardScale_{board} are untouched " +
                    "and still do everything they did.",
                    new AcceptableValueRange<float>(0f, 4f)));
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
                new ConfigDescription(
                    $"[{board}] scale of the ACTIVE-cards column (× card size). Seeded 0.82 — " +
                    "slightly smaller than the hand/browse fan. " + BoundedNote + "Range 0.25-3.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            _activeGridSpacing[i] = _file.Bind("Cards", $"ActiveGridSpacing_{board}", Defaults.ActiveGridSpacing_ByBoard[i],
                $"[{board}] ACTIVE-cards grid step FACTORS: X = column step (× scaled card width), Y = row " +
                "step (× scaled card height). Seeded (1.06, 0.70).");
            _pileOffset[i] = _file.Bind("Cards", $"PileOffset_{board}", BoardDefaults.PileOffset[i],
                $"[{board}] offset ADDED to the discard/burn PILE mount local position (on top of the fixed " +
                "right-edge base), board-local meters. Seeded 0 (Oak).");
            _pileScale[i] = _file.Bind("Cards", $"PileScale_{board}", Defaults.PileScale_ByBoard[i],
                new ConfigDescription(
                    $"[{board}] size MULTIPLIER of the two discard/burn pile stacks. Seeded 1 " +
                    "(Oak). " + BoundedNote + "Range 0.25-3.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
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
                new ConfigDescription(
                    $"[{board}] SIZE multiplier of the OBJECTIVES ('Aufgaben') dock — a true ZOOM: " +
                    "text, progress bars, icons and the panel itself all scale together, because " +
                    "this is the objectives MOUNT's localScale and the docked panel rides " +
                    "mount.lossyScale. This is the ONLY dial that changes how big the task text " +
                    "renders; ObjectivesWidth changes the shape of the block, never its type size. " +
                    "Seeded 1 (Oak). " + BoundedNote + "Range 0.25-3.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
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
                new ConfigDescription(
                    $"[{board}] size MULTIPLIER of the ELEMENT infusion ('Elemente') dock. " +
                    "Seeded 1 (Oak). " + BoundedNote + "Range 0.25-3.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            _pinOffset[i] = _file.Bind("Cards", $"PinOffset_{board}", BoardDefaults.PinOffset[i],
                $"[{board}] offset ADDED to the FOLLOW/PIN toggle button local position (on top of its fixed " +
                "bottom-right base), board-local meters (Z = proud toward the player). Seeded 0 (Oak).");
            _readoutOffset[i] = _file.Bind("Cards", $"ReadoutOffset_{board}", BoardDefaults.ReadoutOffset[i],
                $"[{board}] offset ADDED to the ROUND readout ('Runde N') local position (on top of its fixed " +
                "top-right base), board-local meters (Z = proud toward the player). Seeded 0 (Oak).");

            // Item C: the shared DECISION DOCK (text + buttons UNDER the board — the decision/confirm
            // prompt row) offset + size, per board.
            _decisionOffset[i] = _file.Bind("Cards", $"DecisionOffset_{board}", BoardDefaults.DecisionOffset[i],
                $"[{board}] offset ADDED to the shared DECISION DOCK mount local position, board-local " +
                "meters — it moves the WHOLE decision area as one body: the buttons, the prompt text " +
                "above them and the use-slot bars under them, all by the same amount. Y = up/down " +
                "(NEGATIVE = further below the board), X = sideways, Z = proud toward the player. It " +
                "never changes the distance WITHIN the area — that is DecisionGap alone. Seeded 0 on " +
                "every board (the 157 mm this used to ship with lives in the mount's own base since " +
                "the Y went live; see DecisionOffsetYRebased).");
            _decisionScale[i] = _file.Bind("Cards", $"DecisionScale_{board}", Defaults.DecisionScale_ByBoard[i],
                new ConfigDescription(
                    $"[{board}] size MULTIPLIER of the shared DECISION DOCK (its docked prompt " +
                    "row pose-follows the mount's lossyScale). Seeded 1 (Oak). " + BoundedNote +
                    "The dock carries the buttons that ANSWER a prompt the rule engine is waiting " +
                    "on, so this bound is a flow guard, not a taste one. Range 0.25-3.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
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

        // ONE-TIME migration, ModBuild 90 — THE DECISION OFFSET'S Y BECAME LIVE.
        //
        // [Cards] DecisionOffset_<board>.y shipped at −0.157 and could not move the decision area:
        // it moved the MOUNT, but the dock solves its seat against the grab bar in an up-axis
        // computation the mount's own Y cancels out of (WorldUI.Surfaces.DecisionDockSurface.Place).
        // That Y now displaces the whole area — buttons, prompt text, use-slot bars — so the shipped
        // 157 mm moved into PlayTray.DecisionMountBase and the shipped default became 0. A FRESH
        // install is therefore already correct; an EXISTING config file still holds −0.157, and
        // honouring it there would drop the area 157 board-local mm on first launch.
        //
        // Only the untouched value is migrated (saved y == the old default, the BoardScale
        // precedent above): a y the player deliberately dialled somewhere else is their number, and
        // this build is the first one on which it does anything, so it is left to do it. The marker
        // makes this run at most once per file, and the test is a no-op on a fresh file (y == 0).
        const float oldShippedDecisionOffsetY = -0.157f;
        ConfigEntry<bool> decisionYRebased = _file.Bind("Cards", "DecisionOffsetYRebased",
            Defaults.DecisionOffsetYRebased,
            "Internal one-time migration marker: this config file's DecisionOffset_*.y has been " +
            "re-based onto the build where that dial started moving the whole decision area (the " +
            "shipped -0.157 moved into the decision mount's own base). Do not edit.");
        if (!decisionYRebased.Value)
        {
            foreach (ControlBoard board in System.Enum.GetValues(typeof(ControlBoard)))
            {
                ConfigEntry<Vector3> entry = _decisionOffset[(int)board];
                Vector3 saved = entry.Value;
                if (Mathf.Abs(saved.y - oldShippedDecisionOffsetY) > 1e-4f)
                    continue;   // never tuned away from the old ship value, or already re-based
                entry.Value = new Vector3(saved.x, 0f, saved.z);
                Core.VRLog.Info("Cards", $"One-time migration: DecisionOffset_{board}.y was at the old " +
                                    "shipped -0.157, which moved the decision mount but not the decision " +
                                    "AREA (the dock's up-axis solve cancelled it). That displacement now " +
                                    "lives in the mount's fixed base, so the entry adopted 0 — the area " +
                                    "stays exactly where it was, and from here on this dial moves the " +
                                    "buttons, the prompt text and the use bars together.");
            }
            decisionYRebased.Value = true;
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
        // THE CHARACTER-SWAP EXCHANGE (user 2026-08-09: "mach auch hier eine neue coolere
        // Tauschanimation rein die den Fächer austauscht"). One wipe across the palm: the old hand
        // is gathered off one end of the arc while the new one is dealt out of the other, the two
        // halves separated in depth and counter-rolled so the arriving hand passes in FRONT of the
        // leaving one. Every range starts at 0, and 0 on all of them is exactly the instant content
        // swap this replaced — the honest "turn it back down" position, reachable from the steppers.
        FanSwapDuration = _file.Bind("Cards", "FanSwapDuration", Defaults.FanSwapDuration,
            new ConfigDescription(
                "Character swap: seconds ONE card takes to fly out of the fan (or into it) when " +
                "you switch which character's hand you are looking at while the fan is open " +
                "(unscaled time — it runs even while the game pauses). The whole exchange takes " +
                "this plus the overlap delay plus the last card's stagger. Longer = the exchange " +
                "is readable rather than a flick; too long and switching characters feels slow.",
                new AcceptableValueRange<float>(0.05f, 0.8f)));
        FanSwapStagger = _file.Bind("Cards", "FanSwapStagger", Defaults.FanSwapStagger,
            new ConfigDescription(
                "Character swap: extra start delay in seconds PER CARD along the arc, so the " +
                "exchange sweeps across the hand instead of every card moving at once. BOTH " +
                "halves use it and both run in the same direction, which is what makes the " +
                "leaving and the arriving hand read as ONE wipe rather than two animations. It is " +
                "also the reason the motion survives a mixed-reality background: the eye follows " +
                "a moving front, where a simultaneous blob-move is a flicker a busy room " +
                "swallows. 0 = every card starts together.",
                new AcceptableValueRange<float>(0f, 0.12f)));
        FanSwapOverlap = _file.Bind("Cards", "FanSwapOverlap", Defaults.FanSwapOverlap,
            new ConfigDescription(
                "Character swap: how much of each slot's DEPARTURE its matching ARRIVAL overlaps. " +
                "1 = the new card sets off the instant the old one does (the two hands cross " +
                "mid-air); 0 = the new card only sets off once the old one has fully gone. This " +
                "is the single dial that decides whether the swap reads as an EXCHANGE or as an " +
                "empty hand being refilled, which is why the shipped value is high.",
                new AcceptableValueRange<float>(0f, 1f)));
        FanSwapTravel = _file.Bind("Cards", "FanSwapTravel", Defaults.FanSwapTravel,
            new ConfigDescription(
                "Character swap: how far PAST the end of the arc the gather/deal point sits, real " +
                "meters. The outgoing hand converges on that point off one end (swept up like a " +
                "deck), the incoming hand fans out of the mirror-image point off the other — so " +
                "the cards visibly leave and arrive instead of fading in place. Bigger = they " +
                "travel further clear of the hand.",
                new AcceptableValueRange<float>(0f, 0.3f)));
        FanSwapArc = _file.Bind("Cards", "FanSwapArc", Defaults.FanSwapArc,
            new ConfigDescription(
                "Character swap: how far the cards move in DEPTH at the middle of their flight, " +
                "real meters. The leaving card ducks AWAY from you by this; the arriving card " +
                "bows TOWARD you by the same amount — so the new hand passes in front of the old " +
                "one and the two can never appear to slide through each other. Depth is also the " +
                "cue passthrough cannot mask: both eyes see the separation. 0 = both halves stay " +
                "flat in the fan plane.",
                new AcceptableValueRange<float>(0f, 0.25f)));
        FanSwapSpinDegrees = _file.Bind("Cards", "FanSwapSpinDegrees", Defaults.FanSwapSpinDegrees,
            new ConfigDescription(
                "Character swap: the roll (degrees) the two halves turn through — the leaving " +
                "hand winds one way as it gathers, the arriving hand unwinds the other way as it " +
                "deals out. A rotation changes a card's outline, and a cluttered background never " +
                "produces a coherent outline rotation by accident; the opposite signs are what " +
                "make the two halves read as an exchange rather than a shove. 0 = no roll.",
                new AcceptableValueRange<float>(0f, 180f)));
        FanSwapSeedScale = _file.Bind("Cards", "FanSwapSeedScale", Defaults.FanSwapSeedScale,
            new ConfigDescription(
                "Character swap: the size a card has at the gather/deal point, as a fraction of " +
                "its size in the fan. Smaller = more shrink on the way out and more growth on the " +
                "way in, which is the monocular half of the depth cue the bow gives stereoscopically.",
                new AcceptableValueRange<float>(0.02f, 1f)));
        FanSwapSettleOvershoot = _file.Bind("Cards", "FanSwapSettleOvershoot",
            Defaults.FanSwapSettleOvershoot,
            new ConfigDescription(
                "Character swap: the strength of the overshoot at the END of an arriving card's " +
                "flight (it passes its slot and swings back into it) and of the matching wind-up " +
                "a leaving card takes before it goes. A REVERSAL OF DIRECTION is the loudest event " +
                "motion has, and it costs no extra travel. About 1.5 is a ~6 % overshoot; 0 = a " +
                "plain ease that only decelerates to a stop.",
                new AcceptableValueRange<float>(0f, 3f)));
        CardSoundsEnabled = _file.Bind("Cards", "CardSoundsEnabled", Defaults.CardSoundsEnabled,
            "Master switch for the mod's own card and fan sounds (fan open/close, card grab, " +
            "place, take-back). Off = the mod plays none of them; the game's own card sounds are " +
            "untouched. The five *Sound entries below keep their audio-item strings either way — " +
            "this is an AND on top of them, not a rewrite.");
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

    /// <summary>
    /// THE BAND <c>[Cards] TrayScale</c> MAY HOLD — A STORAGE RANGE, AND SINCE ModBuild 351 THAT IS
    /// ALL IT IS. The declared config range, the read clamp below and the round trip in
    /// <c>PlayTray.PersistPoseToConfig</c> are this one pair, named once. It is not a taste value:
    /// it is the set of sizes the config can reproduce bit-exactly as
    /// <c>TrayScale × BoardScale_{board}</c>. The SIZE LIMITS the player actually bumps into are
    /// <c>[Cards] BoardMin/BoardMaxWidthMeters</c>, which are apparent metres and ride the rig scale
    /// (<c>PlayTray.TryGetApparentWidthPerScaleUnit</c>).
    ///
    /// <para><b>THE TWO-HAND GESTURE WINDOW IS NO LONGER INTERSECTED WITH THIS BAND, and the
    /// ModBuild 269 paragraph below is CORRECTED, not deleted, because its claim is the thing that
    /// went wrong.</b> That paragraph says 0.25–4.0 "makes the storable band a SUPERSET of the whole
    /// 18-140 cm apparent window on all three boards, so the intersection never bites". THAT IS
    /// TRUE IN FOLGEN AND FALSE IN FIXIERT. In FOLGEN the tray hangs under the rig anchor, so the
    /// parent chain IS the rig scale, the division in <c>TryGetApparentWidthPerScaleUnit</c>
    /// cancels, and the apparent window is the fixed localScale <c>[0.281, 2.188]</c> the claim was
    /// checked against. In FIXIERT the parent chain is the pin holder, frozen at pin time, so
    /// nothing cancels and the window is PROPORTIONAL TO THE LIVE RIG SCALE. The ModBuild 350
    /// hardware log walked it clean off this band: rig ×8.90 window <c>[0.622, 4.841]</c>, rig
    /// ×29.93 window <c>[2.091, 16.260]</c> (a 3.8 % overlap left), rig ×73.93 window
    /// <c>[5.172, 40.230]</c> against a band of <c>[0.136, 2.171]</c> — no overlap at all, the
    /// window collapsed onto a point and the pinch went silently inert. That is the user's
    /// "Das konnte ich im Test nicht".</para>
    ///
    /// <para><b>SO DO NOT TRY TO FIX THAT BY WIDENING THESE TWO NUMBERS.</b> In FIXIERT the
    /// required localScale is the FOLGEN window times the ZOOM RATIO SINCE PINNING, and the rig's
    /// own span is <c>base × [0.1, 12]</c> — a factor of up to 120. The ModBuild 350 session alone
    /// covered ×19.8 (rig ×4.02 → ×79.68) and would have needed <c>TrayScale</c> to reach 80. A
    /// fixed band cannot contain a sliding window at any width, so the intersection was removed
    /// instead (<c>PlayTray.IPanelGrabOwner.GrabScaleLimits</c>) and the ratchet it was guarding
    /// against was closed at its own end: <c>PersistPoseToConfig</c> no longer re-seats
    /// <c>BoardScale_{board}</c> under any circumstances.</para>
    ///
    /// <para>THE ORIGINAL ModBuild 269 NOTE, kept because the reach argument in it is still right
    /// and still the reason these are 0.25/4 and not 0.5/2: "the narrower pair would have COST the
    /// player reach he has today … at 0.5-2 the intersection BITES: on Oak/Bronze
    /// (<c>BoardScale 0.54265</c>) the two-hand grow would have stopped at <c>localScale 1.085</c>
    /// ~ 69 cm apparent instead of ~128 cm." With the intersection gone that argument now applies
    /// to what the CONFIG can hold rather than to what the gesture can reach, which is a smaller
    /// claim — but it is the same direction, and narrowing the pair would only make the lossy case
    /// in <c>PersistPoseToConfig</c> more common. Changing these two numbers moves the settings
    /// window's own slider range; that is a deliberate, stable change, not the drifting range of
    /// the 2026-08-15 report.</para>
    /// </summary>
    internal const float TrayScaleMin = 0.25f;

    /// <inheritdoc cref="TrayScaleMin"/>
    internal const float TrayScaleMax = 4f;

    internal static float ClampedTrayScale => Mathf.Clamp(TrayScale.Value, TrayScaleMin, TrayScaleMax);

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

    /// <summary>
    /// How far the head is from the FIRST SEAT, real metres — the straight-line distance to
    /// <see cref="SpawnSeatOffset"/>, so it moves with the three Spawn…Meters dials and with
    /// nothing else. Floored well above zero: a seat dialled onto the head itself would otherwise
    /// divide the arrival size by nothing.
    /// </summary>
    internal static float SeatDistanceMeters => Mathf.Max(0.05f, SpawnSeatOffset.magnitude);

    /// <summary>
    /// THE ARRIVAL SIZE, DERIVED. The board is seated a known distance from the head, so the only
    /// size statement that survives a zoom is an ANGULAR one: a board of apparent width w at
    /// distance d covers 2·atan(w/2d), so the width that covers
    /// <see cref="SpawnBoardWidthDegrees"/> is w = 2·d·tan(θ/2). Both terms are already the
    /// player's: d is his own Spawn{Side,Forward,Down}Meters seat, read verbatim and never moved,
    /// and θ is his own comfort dial. In REAL (apparent) metres, which is the unit
    /// BoardMin/BoardMaxWidthMeters are written in, so the caller can clamp one against the other
    /// without a conversion. At the shipped seat and angle this is 50.0 cm.
    /// </summary>
    internal static float SeatBoardWidthMeters
    {
        get
        {
            float degrees = SpawnBoardWidthDegrees != null
                ? SpawnBoardWidthDegrees.Value
                : Defaults.SpawnBoardWidthDegrees;
            return 2f * SeatDistanceMeters * Mathf.Tan(0.5f * degrees * Mathf.Deg2Rad);
        }
    }

    // ---- Per-board resolver accessors (Part A) ----
    // Return the ConfigEntry so callers can both READ (.Value) and WRITE (.Value = …, which
    // BepInEx persists and fires SettingChanged on — the debug menu's live-apply hook).

    /// <summary>The board the tray currently uses (drives every per-board resolver below).</summary>
    internal static ControlBoard CurrentBoard => Board.Value;

    /// <summary>[Cards] RestButtonOffset — the shared nudge on top of the board's own rest-pad
    /// anchors. No <c>ControlBoard</c> parameter any more: the per-board part is the anchor.</summary>
    internal static ConfigEntry<Vector3> RestButtonOffset => _restButtonOffset;
    /// <summary>[Cards] RestButtonDiameter — the shared rest-disc size CEILING; each board's own
    /// measured pad shrinks it further through <c>BoardAnchors.FitCapSize</c>.</summary>
    internal static ConfigEntry<float> RestButtonDiameter => _restButtonDiameter;
    /// <summary>[Cards] ConfirmUndoOffset — the shared nudge on top of the board's own button-seat
    /// anchors, for every member of the generic cluster.</summary>
    internal static ConfigEntry<Vector3> ConfirmUndoOffset => _confirmUndoOffset;
    // ConfirmUndoSize(b) accessor is GONE with its entry (retired 2026-08) — the cap size is
    // WorldUI.ButtonTuning.BoardCapWidth/Height ([BoardButtons]) for both shapes.
    internal static ConfigEntry<Vector3> ItemUseSlotOffset(ControlBoard b) => _itemUseSlotOffset[(int)b];
    internal static ConfigEntry<Vector3> ItemCardOffset(ControlBoard b) => _itemCardOffset[(int)b];
    /// <summary>[Cards] ShortRestCaptionOffset_{board} — where the engraved "KURZE RAST" sits,
    /// board-local metres, on top of the seat the board itself dictates.</summary>
    internal static ConfigEntry<Vector3> ShortRestCaptionOffset(ControlBoard b) => _shortRestCaptionOffset[(int)b];
    /// <summary>[Cards] LongRestCaptionOffset_{board} — the same for "LANGE RAST".</summary>
    internal static ConfigEntry<Vector3> LongRestCaptionOffset(ControlBoard b) => _longRestCaptionOffset[(int)b];
    internal static ConfigEntry<Vector3> SlotOverlayOffset(ControlBoard b) => _slotOverlayOffset[(int)b];
    internal static ConfigEntry<float> SlotOverlaySpacing(ControlBoard b) => _slotOverlaySpacing[(int)b];
    internal static ConfigEntry<float> SlotOverlayScale(ControlBoard b) => _slotOverlayScale[(int)b];
    internal static ConfigEntry<Vector3> InitiativeOffset(ControlBoard b) => _initiativeOffset[(int)b];

    /// <summary>Per-board offset of the pick-status placard (board-local metres) — user request
    /// 2026-08-03 ("Ich möchte auch in der Lage sein die Position von Text wie 'Barbar: Wähle 1
    /// Karte(n) zum Verlieren' zu ändern"). Mirrored onto a peer's remote board.</summary>
    internal static ConfigEntry<Vector3> PickBannerOffset(ControlBoard b) => _pickBannerOffset[(int)b];

    /// <summary>
    /// Per-board offset of the HOVER HINT (the game's shared tooltip box) while it is parked AT THE
    /// CONTROL BOARD -- board-local metres, user report 2026-08-03 ("Sind die overlay-hints von
    /// einer Karte die auf dem Controllboard liegen, sollen sie auch am Controllboard angezeigt
    /// werden (Position einstellbar im Debug-Menue)"). Read live by
    /// <c>WorldUI.WorldTooltips.LateTick</c>; a hint that belongs to a floated window is laid on
    /// that window and never consults this entry.
    /// </summary>
    internal static ConfigEntry<Vector3> HoverHintOffset(ControlBoard b) => _hoverHintOffset[(int)b];

    /// <summary>Degrees between neighbouring cards of a board fan (per pile — see the binds).</summary>
    internal static ConfigEntry<float> FanStepDegrees(PileKind k) => _fanStepDegrees[PileIndex(k)];

    /// <summary>Radius multiplier of a board fan (per pile).</summary>
    internal static ConfigEntry<float> FanRadiusFactor(PileKind k) => _fanRadiusFactor[PileIndex(k)];

    /// <summary>Config slot for a pile kind, clamped so an unknown kind reads as Items.</summary>
    private static int PileIndex(PileKind k) => k switch
    {
        PileKind.Discard => 1,
        PileKind.Burnt => 2,
        _ => 0,
    };

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

    /// <inheritdoc cref="_boardApparentWidth"/>
    internal static ConfigEntry<float> BoardApparentWidthMeters(ControlBoard b) => _boardApparentWidth[(int)b];
    internal static ConfigEntry<Vector3> BoardPosOffset(ControlBoard b) => _boardPosOffset[(int)b];

    // ---- Round-2 group spacing / shape / Active / Piles resolvers ----
    /// <summary>[Cards] RestStackSpacing — multiplier on the board's own rest-pad anchor pitch.</summary>
    internal static ConfigEntry<float> RestStackSpacing => _restStackSpacing;
    /// <summary>[Cards] ButtonStackSpacing — multiplier on the board's own button-recess pitch: the
    /// Y gap between the stacked generic keycaps.</summary>
    internal static ConfigEntry<float> ButtonStackSpacing => _buttonStackSpacing;
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
    internal static ConfigEntry<Vector3> DecisionOffset(ControlBoard b) => _decisionOffset[(int)b];
    internal static ConfigEntry<float> DecisionScale(ControlBoard b) => _decisionScale[(int)b];
}
