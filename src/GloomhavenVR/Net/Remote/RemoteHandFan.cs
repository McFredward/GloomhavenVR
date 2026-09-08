// FILLED BY WORKER A — the ghost card-hand fan.
//
// Cosmetic fan of card slabs on a remote player's non-dominant hand. By DEFAULT every card is a
// back-on-both-faces slab built from the mod's own procedural card-back material
// (CardMesh.CreateBackMaterial → CardMesh.GetBackTexture) — no game card data is read at all, so a
// hand shows only backs (PLAN2 anti-cheat: you never see another player's cards during selection).
//
// FRONT ART (multiplayer "see teammates' cards" feature): when — and ONLY when — the game's OWN
// reveal rule permits (RevealGate.ShowRoundCardFronts(remoteActor), which mirrors vanilla
// AbilityCardUI: hidden iff online && scenario && !IsUnderMyControl && phase == SelectAbilityCards),
// each slab additionally shows the REAL card face (art + enhancement stickers) by CLONING the remote
// actor's own AbilityCardUI.fullAbilityCard widget onto the slab's owner-facing (−Z) side (see
// RemoteCardArt). We NEVER transmit or synthesize fronts over the wire and NEVER adopt the live
// widget — the fronts are read locally from the already-host-replicated CPlayerActor hand and only
// rendered when the gate is open; during the secret selection phase every card is a BACK. Every game
// deref is null-guarded and fails safe to BACKS (no leak) on any error.
//
// MAP-PHASE FRONTS (report 4, 2026-08-22 — "Handkarten sind nicht sichtbar im Multiplayer im
// Map-Bereich"): outside a scenario the peer's fan is the mod map room's LOADOUT hand, which has no
// AbilityCardUI and no CPlayerActor to clone from. That case has its own capability path — the map
// room names the character from the broadcast COUNT (MapRoomHand.TryResolvePeerLoadout) and the
// faces are borrowed from this client's own ObjectPool by card id — behind its own predicate,
// RevealGate.ShowMapPhaseHandFronts. It is DISJOINT from the scenario gate above (that one requires
// a running scenario, this one requires its absence), so nothing about the secret selection window
// changes. See the block beside _mapBuffer for the root cause and the evidence.
//
// Geometry mirrors Cards/CardFan.Relayout (arc radius, per-card step, curvature-by-fill, tilt,
// z-stagger) so a remote hand reads exactly like the local one, but with LOCAL constants seeded to
// the CardsConfig defaults — this stays self-contained and does not depend on the game's live Fan
// config being initialised.

using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A cosmetic fan of card slabs on a remote player's non-dominant hand, sized to
/// <c>owner.HandCardCount</c>. Attached under <see cref="RemoteAvatar.NonDominantHandHolder"/>
/// (falling back to <see cref="RemoteAvatar.Root"/>), floated a palm standoff up the hand normal
/// and arced to face <see cref="RemoteAvatar.HeadHolder"/> — mirroring <see cref="CardFan"/>.
/// Shows card BACKS by default; when <see cref="RevealGate.ShowRoundCardFronts"/> permits for the
/// remote actor it additionally overlays each slab with the REAL cloned card face
/// (<see cref="RemoteCardArt"/>), and in the MAP PHASE — where
/// <see cref="RevealGate.ShowMapPhaseHandFronts"/> is the rule and there is no actor at all — with
/// the peer's scenario LOADOUT card. The broadcast COUNT drives the fan size; both sets of fronts
/// are read locally from data this client already holds and gated strictly on the reveal rules.
///
/// ORIENTATION CONTRACT (audited against <see cref="CardFan"/>; user report: "prüf nochmal, ob der
/// Handfächer … richtig synchronisiert und … die Orientierung, die der jeweilige Spieler sieht, auch
/// genauso (inklusive aller Drehungen etc.)"). The wire carries a COUNT and nothing else about the
/// fan. Everything else in CardFan's pose is a function of the count and of the owner's HAND and
/// HEAD — both of which already ride the rig packet — so it is all DERIVED here rather than
/// transmitted, and no extras flag or reserved bit is spent:
///   * ANCHOR — one palm standoff up the PALM normal of the peer's own <c>Rig.PalmCenter</c>
///     (<see cref="RemoteAvatar.PalmAnchorFor"/>), the transform the owner's fan is parented to.
///   * FACING / rotation about the palm — <c>LookRotation(fanPos − ownerHeadPos, Vector3.up)</c>,
///     the same rule and the same world-up reference CardFan.Tick uses. The fan does NOT roll with
///     the palm on either side, so nothing about wrist twist needs syncing.
///   * PER-CARD LAYOUT — arc angle, roll, curvature-by-fill, z-stagger, the per-card TOE-IN at the
///     owner's head, and the depth BOW with its gaze relief and stacking clamp (see
///     <see cref="LayoutCards"/>), plus the fan-out reveal timing.
///
/// KNOWN, DELIBERATE GAPS (nothing here is derivable from synced data, and all are transient or
/// opt-in cosmetics — none is worth a wire field):
///   * REORDER INSERTION GAP: while the owner DRAGS a card sideways to a new seat their own fan
///     opens a gap at the drop index (CardFan._insertGap, pushed by the driver each frame). Nothing
///     on the wire says where that gap is, so a peer's fan does not open one and the reorder reads
///     as a card jumping rather than sliding into a space. That is a genuine wire item and is held
///     as one.
///
///     (THE HOVER SPLIT USED TO BE LISTED HERE WITH IT, AND BOTH ITS CLAUSES WERE FALSE — retired
///     2026-09-07. The bullet read "Nothing on the wire says which card, so a peer's fan never
///     splits". Extension record 6 carries the hovered INDEX (RemoteAvatar.HandHighlightIndex), and
///     LayoutCards has been drawing the split off it for builds — the `pos += rot * new
///     Vector3(SplitOffset(i - hovered), 0f, 0f)` term, on the shared Cards.FanSweep.SplitOffset
///     curve, with the card itself lifting on VRCard's own pop. The sentence welded a SHIPPED
///     feature to an unshipped one, and for as long as it stood, anybody grepping this list for
///     what was missing found the split already crossed off and the gap not named at all. A
///     class-doc claim is a hypothesis, exactly like a log string.)
/// (GAZE-BIAS YAW left this list. It said the receiver "could compute the whole eased/hysteretic
/// yaw from the peer's synced head gaze, but not whether the SENDER has the toggle on — that one bit
/// is the only missing input", and that was true and then simply stood there: an owner who switched
/// [Cards] FanGazeBias on yawed their fan on their own screen and on nobody else's. The yaw is
/// DERIVED here now — see the gaze-facing-bias region — and the one bit is the only thing still
/// outstanding, which is the renderer-first order this project requires: a wire field whose receiver
/// ignores it reads green in scripts/check-wire-coverage.py while the picture stays wrong.)
/// CLOSED SINCE EXTENSION RECORD 28 — the TUNED Fan* CONFIG. The geometry fields below used to be
/// consts seeded to the CardsConfig DEFAULTS, on the argument that "a peer's fan must not depend on
/// the LOCAL player's Cards config being bound or tuned". That argument is still right and still
/// holds: nothing here reads the local config. What was wrong was the conclusion — the fan was
/// drawn at the SHIPPED numbers rather than at the OWNER's, so a sender who retuned their own fan
/// read differently to others than to themselves. Their dials now ride the wire, sparsely and only
/// when moved (<see cref="NetProtocol.ExtIdBoardTuning"/>), and <see cref="SyncTuning"/> pulls them
/// off <see cref="RemoteAvatar.BoardTuning"/>; every dial they have NOT moved resolves to this
/// client's own shipped constant, which is the same number this file always used.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY — costs wire bytes: one card-COUNT byte in the extras packet
/// (<c>HandCardCount</c>, always written) plus the sender's hand pose and dominant-hand flag, which
/// the rig packet already carries. Card IDENTITY is DELIBERATELY-NOT transmitted — backs only. The
/// fan's whole geometry is DERIVED on the receiver from the synced hand + head, so curvature,
/// toe-in, bow and fan-out timing cost nothing; the KNOWN GAPS above are the fields deliberately
/// not bought. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteHandFan
{
    // ---- fan geometry (real meters / degrees, seeded to CardsConfig Fan* defaults) -----------

    /// <summary>
    /// The NOMINAL card metric every remote card MESH is authored at — the shipped
    /// <c>[Cards] CardWidth</c>. Slabs then carry <c>ownerWidth / DefaultCardWidth</c> as their
    /// uniform scale, which is why this number has to be one shared constant rather than a per-file
    /// literal.
    ///
    /// <para>ITS OLD DOC SAID it was "shared with RemoteAvatar's held-card slab so all remote card
    /// slabs match", and that claim was FALSE the moment an owner touched their card size: the fan
    /// slabs are scaled off the owner's own width (see <see cref="SyncTuning"/>) while the held slab
    /// was drawn at this constant flat, so the two DIVERGED for exactly the player who had tuned
    /// them. A nominal is a mesh-authoring unit, never a licence to skip a size. The held slab
    /// composes its own size now — <c>RemoteAvatar.HeldSlabScale</c>.</para>
    /// </summary>
    internal const float DefaultCardWidth = Defaults.CardWidth;

    /// <summary>Card slab height default (CardsConfig.CardHeight ratio over the width).</summary>
    internal const float DefaultCardHeight = DefaultCardWidth * (88f / 63.5f);

    /// <summary>
    /// THE OWNER'S CARD SIZE AS THE SLAB ROOT'S UNIFORM SCALE — the single place a peer's
    /// <c>[Cards] CardWidth</c> enters this fan's geometry, and the value three other things in
    /// this file already assume is on that transform.
    ///
    /// <para>WHOSE DIAL: the OWNER'S, off extension record 28 (<c>NetProtocol.TuneCardWidth</c>,
    /// sampled by the sender from their own <c>CardsConfig.CardWidth</c> and read back as
    /// <c>BoardTuning.CardWidth</c>). This client's own <c>CardsConfig</c> is never consulted on
    /// this path, which is the standing rule: a mirror reads the owner's dial and never the
    /// viewer's, and ANDing the two is a defect this project has already shipped once.</para>
    ///
    /// <para>WHY THE WHOLE SUBTREE IS AUTHORED NOMINAL AND THIS CARRIES THE SIZE. The body mesh
    /// comes out of <c>CardMesh.AttachBody(mf, kind, DefaultCardWidth, DefaultCardHeight)</c>, the
    /// body's own scale is <c>_visibleFace / Default*</c> where <see cref="SyncFaceRect"/>
    /// letterboxes into the NOMINAL card, and <see cref="RemoteCardArt"/> is handed
    /// <c>DefaultCardWidth/Height</c> on purpose ("handing the tuned width here fitted the face a
    /// second time and squared the ratio"). One uniform scale on the root is therefore the only
    /// term that may carry the owner's size. It had a fourth consumer until 2026-09-07 —
    /// <see cref="FanSweep.StripWidth"/>'s <c>cardLocalScale</c>, which divided the arc chord by
    /// exactly this number to size the borrow collider's strip in the card's own frame. That
    /// collider is gone with the borrow (report 2); the three above are the whole list.</para>
    ///
    /// <para>1 AT THE SHIPPED DEFAULT, by construction: the numerator IS
    /// <see cref="DefaultCardWidth"/> for a peer who has not moved the dial. The bind's range is
    /// 0.03-0.15 m against a 0.0635 default, so this spans 0.47x to 2.36x for one who has.</para>
    /// </summary>
    private float SlabScale => _cardWidth / DefaultCardWidth;

    // ---- THE OWNER'S OWN FAN GEOMETRY (extension record 28) ---------------------------------
    // These were `const`, seeded to the CardsConfig defaults, with a KNOWN-GAP note saying that a
    // sender who retunes their fan "reads slightly differently to others than to themselves". Under
    // the 1:1 ruling that gap is a defect, so every one of them is now an instance field refreshed
    // from RemoteBoardTuning: the owner's value where they have moved the dial, this client's
    // shipped constant where they have not — which is the same number, so an untuned peer renders
    // byte-for-byte the fan this file has always drawn.
    //
    // WHY FIELDS AND NOT PROPERTY READS: the layout loop touches a dozen of these per card per
    // frame, and RemoteBoardTuning is a wide struct. They are refreshed by SyncTuning() when the
    // owner's tuning REVISION changes (a config edit on their side — rare), and read as plain
    // floats in between.
    private float _cardWidth = DefaultCardWidth;
    private float _cardHeight = DefaultCardHeight;

    /// <summary>
    /// Whether THIS OWNER has the card dust on (wire id <see cref="NetProtocol.TuneCardDustOn"/>).
    ///
    /// <para>Cached beside the other tuning mirrors and for the same reason: the dust is asked
    /// about per card and <c>RemoteBoardTuning</c> is a wide struct. It is a PERMISSION and not a
    /// picture — the puff itself is drawn by this client's own <c>Cards.CardDustFx</c>, which is a
    /// mod-side static pool and not a member of <c>VRCard</c>, so a peer has always owned the
    /// emitter and only ever lacked the owner's say-so and a pose.</para>
    /// </summary>
    private bool _cardDustOn = Defaults.CardDust;

    /// <summary>
    /// Whether THIS OWNER lets the GAME's own card plume play (wire id
    /// <see cref="NetProtocol.TuneGameCardParticlesOn"/>, <c>[Cards] GameCardParticles</c>) — the
    /// twin of <see cref="_cardDustOn"/> and under the identical rule.
    ///
    /// <para>The old wire debt claimed the receiver COULD not draw this one ("a peer's mirrored
    /// cards are mod slabs with no game particle system to switch on"). That reason was false in
    /// every clause: the plume is a PREFAB on a Resources-loaded singleton, not a component on a
    /// card, so every client already has it and needs no card of its own to reach it. See
    /// <see cref="RemoteCardPlume"/>, which hosts a tamed copy on the slab.</para>
    ///
    /// <para>The VIEWER's own copy of this dial is NOT consulted here. Theirs is answered by
    /// <c>Compat.CardParticlesOff</c>, which pins the game's low-spec switch so THEIR OWN cards
    /// spawn no plume; that suppression cannot touch this path, because this path instantiates the
    /// prefab itself rather than going through <c>CardEffects.SpawnParticle</c>. That separation is
    /// the whole point — a peer's board is a picture of ITS OWNER's board.</para>
    /// </summary>
    private bool _gameCardParticlesOn = Defaults.GameCardParticles;

    private float _palmOffset = Defaults.FanPalmOffset;

    // ---- THE PRINTED FACE RECT (report 12, 2026-08-15) ---------------------------------------
    //
    // "Die remote Handkarten Vorderseiten werden etwas zu klein angezeigt, so dass sie nicht
    // perfekt auf dem mesh liegen und der Hintergrund am rand durchscheint." (screenshot
    // .planning/debug/remote_faecher.jpg — a rim of the slab's own gold card-back braid visible
    // all the way around every printed face.)
    //
    // MEASURED, not eyeballed. The face is the game's FullAbilityCard, 294 x 450 px (host log:
    // "CARD SILHOUETTE (Ability): first face offered ('Full', rect 294x450 px)"), aspect 0.6533.
    // A nominal card slab is 63.5 x 88.0 mm, aspect 0.7216. RemoteCardArt.FitClone letterboxes the
    // clone with Mathf.Min and insets it by CardFace's 6 %:
    //     fit = min(63.5/294, 88/450) = min(0.2160, 0.19556) = 0.19556 mm/px   (HEIGHT-limited)
    //     printed = 294 x 0.19556 x 0.94  =  54.04 mm
    //               450 x 0.19556 x 0.94  =  82.72 mm
    // against a BODY of 63.5 x 88.0 mm. Margin = (63.5-54.04)/2 = 4.73 mm per side left and right,
    // (88.0-82.72)/2 = 2.64 mm per side top and bottom. That is the rim, and its measured shape
    // matches the screenshot: WIDER at the sides than at the ends.
    //
    // THE LOCAL CARD NEVER HAD IT, because it solves the same letterbox the other way round —
    // VRCard.SetCanvasSize scales the BACKING MESH to the printed rect (facePixels x fit x
    // VisibleFaceFraction) instead of leaving it at the nominal card. So the fix is not a fudge
    // factor on the face; it is the step this fan was missing. The rect is computed by the one
    // shared definition, CardFace.VisibleFaceRect, from the face size this client OBSERVES on its
    // own hosted card widget — a local read of the shared card prefab, so no wire field and no
    // question asked about the peer.
    //
    // Consequence worth stating: a peer's card also STOPS being 63.5 mm wide while the owner's own
    // is 54.04 mm. The rim and a 17.5 % width mismatch were the same defect.
    private Vector2 _visibleFace = CardFace.VisibleFaceRect(DefaultCardWidth, DefaultCardHeight);
    private int _faceRevision = -1;
    private bool _loggedFaceRect;

    /// <summary>
    /// Where a peer's hand fan actually floats: one palm standoff up the PALM normal of
    /// <paramref name="holder"/>'s hand (falling back to the holder's own +Y before the rig exists).
    /// Shared with <see cref="RemoteCardFx"/> so a replicated card flight lands in the fan rather
    /// than out of the back of the peer's hand — the same frame bug PoseFan documents.
    ///
    /// This is the ONLY anchor surface, deliberately. The standoff used to be exposed on its own as
    /// an <c>internal const PalmStandoff</c> and <see cref="RemoteCardFx"/> aimed with it along the
    /// hand ROOT's +Y — which points out of the BACK of the hand, so flights landed a palm-thickness
    /// on the wrong side (fixed in 9a7f911, which is when this helper appeared and the constant lost
    /// its last caller). Do not re-expose the bare offset: a distance without the frame it is
    /// measured in is what produced the bug.
    /// </summary>
    internal static Vector3 FanAnchorPoint(RemoteAvatar owner, Transform holder)
    {
        Transform anchor = owner.PalmAnchorFor(holder) ?? holder;
        // The OWNER's own FanPalmOffset (extension record 28) — read straight off their resolved
        // tuning because this is a static helper shared with RemoteCardFx, and it runs once per
        // frame per fan rather than per card.
        return anchor.position
               + anchor.up * (owner.BoardTuning.FanPalmOffset * owner.AppliedScale);
    }
    private float _radius = Defaults.FanEffectiveRadius;
    private float _arcSweepDegrees = Defaults.FanArcSweepDegrees;
    private float _perCardStepDegrees = Defaults.FanPerCardStepDegrees;
    private float _archFactor = Defaults.FanFlatCurvatureFactor;
    private float _tiltFactor = Defaults.FanTiltFactor;
    private int _maxHandForCurve = Defaults.FanMaxHandForCurve;

    /// <summary>
    /// Whether THIS OWNER has curvature-by-fill on — their <c>[Cards] FanCurveByFill</c>, wire id
    /// <see cref="NetProtocol.TuneFanCurveByFillOn"/>, defaulting to the shipped <c>true</c> so an
    /// older or untuned peer renders exactly what they rendered before the field existed.
    ///
    /// <para>IT IS A GATE THIS FILE NEVER HAD. Both of the two fill multiplies below ran
    /// UNCONDITIONALLY, and the wire-coverage exemption that stood over the dial — "a local hand-fill
    /// heuristic feeding dials that ARE on the wire" — was the mirror image of the data flow: the
    /// dial does not feed <c>FanCurvePower</c> (135) and <c>FanCurveMinCards</c> (224); those cross
    /// RAW and the RECEIVER does the multiply. So an owner who switched it off flattened their own
    /// fan and nobody else's. Cached here beside the other dozen mirrored dials rather than read per
    /// frame, for the reason given above.</para>
    /// </summary>
    private bool _curveByFillOn = Defaults.FanCurveByFill;

    private const float ZStagger = 0.004f;                         // CardFan.ZStagger (draw order)
    private const int MaxCards = 12;                               // hard clamp on the broadcast count

    // ---- card PRESENTATION (CardFan's "card presentation" region, seeded to its defaults) ------
    // These four were simply MISSING from the ghost fan: a peer's hand rendered as a bare arc with
    // only the z-stagger, while the owner sees a cupped hand whose cards each aim at their own eyes
    // and whose gazed end lifts out of the cup. Every input they need (the owner's head POSITION and
    // GAZE, and the card COUNT) is already on the wire — the head pose rides the rig packet — so the
    // whole presentation is DERIVED on the receiver and costs no wire bits.

    /// <summary>Per-card toe-in gain (CardsConfig.FanFaceViewer default 1 = each card's own normal
    /// aims fully at the owner's head, not the fan root's single billboard normal).</summary>
    private float _faceViewer = Defaults.FanFaceViewer;

    /// <summary>Depth bow at the ends of a full hand, metres (CardsConfig.FanSideDepthCurve).</summary>
    private float _sideDepthCurve = Defaults.FanSideDepthCurve;

    /// <summary>Bow exponent in the card's fraction-from-centre (CardsConfig.FanCurvePower).</summary>
    private float _curvePower = Defaults.FanCurvePower;

    /// <summary>Hands at or below this many cards stay flat (CardsConfig.FanCurveMinCards).</summary>
    private int _curveMinCards = Defaults.FanCurveMinCards;

    /// <summary>Gaze relief amplitude (CardsConfig.FanGazeApexFollow): how much of the resting bow
    /// the card the owner is LOOKING at is lifted out of.</summary>
    private float _gazeApexFollow = Defaults.FanGazeApexFollow;

    /// <summary>
    /// Ease rate (1/s) of the tracked gaze apex — the OWNER's <c>[Cards] FanGazeSmoothing</c>
    /// (record 28 id <see cref="NetProtocol.TuneFanGazeSmoothing"/>), falling back to the shipped
    /// default for an untuned or pre-field peer.
    ///
    /// <para>IT WAS A <c>const 8f</c> AND THE DEFAULT IS 6, so every mirrored fan relieved its bow
    /// 33 % faster than its owner's, for every player, before anybody had tuned anything — and the
    /// wire-coverage exemption that stood over the dial ("driven by THEIR head; the mirror
    /// re-derives from the synced head pose") described the gaze TARGET, which is true, and said
    /// nothing about the RATE, which is an independent coefficient this file held on its own. A
    /// synced input does not make an unsynced coefficient synced. Seeded from <c>Defaults</c> so
    /// scripts/check-remote-defaults.py can pin the untuned case, overwritten from the wire by
    /// <see cref="SyncTuning"/> for the tuned one.</para>
    /// </summary>
    private float _gazeSmoothing = Defaults.FanGazeSmoothing;

    /// <summary>Relief half-width as a fraction of the hand's half-span, and its floor in cards —
    /// CardFan.GazeReliefWidthFactor / GazeReliefMinWidth.</summary>
    private const float GazeReliefWidthFactor = 0.55f;
    private const float GazeReliefMinWidth = 1.2f;

    /// <summary>Grazing-gaze fade window (CardFan.GazeGrazeMinZ / GazeGrazeFullZ): the fan-local +Z
    /// component of the gaze below which the plane crossing is faded out to "centred".</summary>
    private const float GazeGrazeMinZ = 0.05f;
    private const float GazeGrazeFullZ = 0.35f;

    /// <summary>Exponential follow/face sharpness (higher = snappier) — the OWNER's
    /// <c>[Cards] FanFollowSmoothing</c>, record 28 id
    /// <see cref="NetProtocol.TuneFanFollowSmoothing"/>. The INITIALISER is the shipped default,
    /// which is what an untuned peer is still drawn with.
    ///
    /// <para>IT WAS A <c>const</c>, and its comment said it "mirrors CardFan's eased follow
    /// (CardsConfig.FanFollowSmoothing default 16)". It mirrored the DEFAULT, which is a parity
    /// that holds only until somebody moves the dial — and this dial has a QUALITATIVE end. At
    /// <c>0</c> <c>CardFan.Tick</c> does not ease slowly: it takes the other branch entirely and
    /// WELDS the fan to the palm ("Rigid (pre-Demeo, FanFollowSmoothing == 0): welded to
    /// PalmCenter"), so the owner's fan has no follow lag at all while every peer watched a fan
    /// that still eased. That is not a magnitude a peer could squint past; it is a different
    /// animation. Neither is it a "sync sub-feature" needing a key of its own — it is the owner's
    /// existing tuning arriving on the record that already carries the rest of their fan.</para>
    ///
    /// <para>The rate itself matters in between, which is why the number and not just the zero is
    /// read: 60 ms after the palm moves, a fan at the shipped 16 has closed 62 % of the remaining
    /// distance and one at the wire clamp's ceiling of 60 has closed 97 %.</para>
    ///
    /// <para>CardFan's 4 mm dead zone is still deliberately NOT reproduced: it exists to hold the
    /// fan still through raw hand-tracking jitter, and the peer's hand pose is already an
    /// interpolated, packet-rate signal.</para></summary>
    private float _followSmoothing = Defaults.FanFollowSmoothing;

    // ---- HIGHLIGHT (extension record 6) --------------------------------------------------------
    // Verbatim copies of the LOCAL split + pop constants, so a peer's lifted card looks like the
    // owner's lifted card. Local copies for the same reason as every constant above: the sender's
    // live [Cards] tuning is theirs and never rides the wire (DELIBERATELY-NOT), so every client
    // renders a given fan at the SHIPPED numbers.

    /// <summary>CardsConfig.FanSplitMultiplier — the base sideways slide of a split neighbour.</summary>
    private float _splitMultiplier = Defaults.FanSplitMultiplier;

    /// <summary>CardsConfig.FanSplitFalloff — how fast that slide decays with card distance.</summary>
    private float _splitFalloff = Defaults.FanSplitFalloff;

    /// <summary>CardsConfig.FanHoverSplitScale — global gain keeping the gap proportional to the
    /// card spacing.</summary>
    private float _splitScale = Defaults.FanHoverSplitScale;

    /// <summary>CardsConfig.FanSelectedPopForward — how far a lifted card comes toward the viewer
    /// (VRCard's pop, along the card's own −Z).</summary>
    private float _popForward = Defaults.FanSelectedPopForward;

    /// <summary>VRCard's pop: the small upward component that rides with the forward lift. READ
    /// from the owner's own constant, not re-typed beside a comment naming it.</summary>
    private const float PopUp = Cards.VRCard.PopUp;

    /// <summary>VRCard's pop: the extra size a lifted card takes (+18 %).</summary>
    private const float PopScale = Cards.VRCard.PopScale;

    /// <summary>VRCard's pop RAMP rate (units per second, MoveTowards) — the lift grows and relaxes
    /// at the local speed, so the wire never carries an animation, only the index.</summary>
    private const float PopRate = Cards.VRCard.PopRate;

    private readonly RemoteAvatar _owner;

    private GameObject? _root;              // fan pivot; child of the current hand holder
    private Transform? _holder;            // the holder we are currently parented under
    private readonly List<GameObject> _cards = new(MaxCards);

    /// <summary>
    /// THE ARC SEATS THE OWNER IS HOLDING IN THEIR FIST — the seats of <see cref="_cards"/> that
    /// must not be drawn, or -1. Re-resolved every frame by <see cref="ResolveArcHeldSeats"/>;
    /// never latched, because the owner's fist changes with no edge this fan is told about.
    /// </summary>
    private int _arcHeldSeatA = -1;

    /// <inheritdoc cref="_arcHeldSeatA"/>
    private int _arcHeldSeatB = -1;

    /// <summary>The record-36 MODEL seats the two above were translated FROM, kept only so
    /// <see cref="ReportArcMembershipIfChanged"/> can print the translation rather than its result.
    /// A count of suppressed slabs cannot tell a right hide from a wrong one; a
    /// <c>modelSeat-&gt;arcSeat</c> pair can, and that pair is the whole reading for the 2026-09-07
    /// HELD-order regression.</summary>
    private int _arcHeldListSeatA = -1;
    private int _arcHeldListSeatB = -1;

    /// <summary>Record 36's list length beside those seats, for the verdict line's membership
    /// clause. 0 when no seat was named.</summary>
    private int _arcHeldListLength;
    private readonly List<RemoteCardArt> _faces = new(MaxCards); // per-slab cloned-front overlays (parallel to _cards)
    private int _builtCount = -1;          // how many card slabs currently exist (-1 = never built)
    private bool _poseInit;                // snap (no ease) on the first pose after (re)activation

    /// <summary>
    /// The eased BASE billboard — the fan's rotation WITHOUT the gaze-bias yaw. Held apart from
    /// <c>_root.rotation</c> because the yaw is composed on top of it: easing from a rotation that
    /// already carries the yaw toward a target that does not would feed the lean back into its own
    /// source each frame and settle it short of the owner's. Seeded from the live root rotation on
    /// every (re)appearance (see <see cref="PoseFan"/>), so with the bias off it is bit-for-bit the
    /// rotation this file produced before it existed.
    /// </summary>
    private Quaternion _facing = Quaternion.identity;

    // FAN-OUT REVEAL (report 6, "Fächer ist sichtbar" / the fan being RAISED). The local fan does a
    // Demeo fan-in on Open (CardFan._openElapsed: every card seeds at the middle slot and flies out
    // to its own slot), but the remote ghost simply appeared fully spread the instant the count
    // arrived — a pop, not a raise. Seconds since this fan became visible; -1 = settled.
    private float _openElapsed = -1f;

    // WIRE-OVERRIDABLE SINCE 2026-08-09 (extension record 28, ids 154..155), and they were `const`
    // before that for one reason only: the record was FULL at exactly 255 bytes and there was
    // nowhere to put them. Paging removed that ceiling (see NetProtocol.BoardTunePages), so the
    // reveal now runs on the OWNER's timing on every screen — which is what the 1:1 ruling has
    // always demanded of it, since the ruling names ANIMATIONS outright and the item fan's and the
    // swap's dials were wired for exactly that. The INITIALISER stays the shipped default, so an
    // untuned peer's fan opens exactly as it did before (scripts/check-remote-defaults.py pins it).
    private float _openSeconds = Defaults.FanOpenDuration;    // CardsConfig.FanOpenDuration

    /// <summary>
    /// Seconds into the owner's fan COLLAPSE, or -1 when none is running — the mirror of
    /// <c>CardFan._closeElapsed</c>.
    ///
    /// <para>WHY IT EXISTS ONLY SINCE ModBuild 306, and why the field it consumes was declared for
    /// months without being sampled: this class used to hide the fan OUTRIGHT. When the owner's
    /// count reached zero it ran <c>Rebuild(0)</c>, which destroys every slab in one frame, and the
    /// peer saw the fan blink out while the owner watched theirs fold into the centre stack over
    /// 0.12 s. A wire field for the duration would have had no consumer, which is exactly what
    /// <c>check-wire-coverage.py</c>'s note said and exactly why it refused to sample id 156.</para>
    ///
    /// <para>THE MIRROR CAN SEE THE CLOSE, which is the fact that makes this possible at all. The
    /// owner's <c>HandCardCount</c> is the count of cards in the OPEN fan, so closing a five-card
    /// hand sends 0 — the same signal an emptied hand sends. That ambiguity is stated at
    /// <c>NetProtocol.ExtIdHalfHover</c>'s empty-fan flag and it does not matter here: both mean
    /// "the fan is coming down", and both should collapse.</para></summary>
    private float _closeElapsed = -1f;

    /// <summary>The OWNER's <c>[Cards] FanCloseDuration</c> (record 28 id 156). 0 = they vanish
    /// their fan instantly, and so does every mirror of it.</summary>
    private float _closeSeconds = Defaults.FanCloseDuration;
    private float _openStagger = Defaults.FanOpenStagger;     // CardsConfig.FanOpenStagger (ripples outward)

    // ---- the owner's CHARACTER-SWAP EXCHANGE dials (extension record 28, ids 77..78 / 150..153 /
    // 226..227). Wire-overridable fields whose INITIALISER is what an untuned peer's exchange is
    // drawn with — the same shape and the same guarantee as the geometry fields above, and on the
    // wire at all for the reason the standing 1:1 ruling gives: it names ANIMATIONS outright.
    private float _swapDuration = Defaults.FanSwapDuration;
    private float _swapStagger = Defaults.FanSwapStagger;
    private float _swapOverlap = Defaults.FanSwapOverlap;
    private float _swapTravel = Defaults.FanSwapTravel;
    private float _swapArc = Defaults.FanSwapArc;
    private float _swapSpinDegrees = Defaults.FanSwapSpinDegrees;
    private float _swapSeedScale = Defaults.FanSwapSeedScale;
    private float _swapSettleOvershoot = Defaults.FanSwapSettleOvershoot;

    /// <summary>Ease-out cubic progress (0..1) of card <paramref name="i"/> in the fan-out reveal —
    /// CardFan.OpenProgress verbatim, so a peer's fan opens on the owner's timing curve.</summary>
    private float OpenProgress(int i, int mid)
    {
        float p = Mathf.Clamp01((_openElapsed - Mathf.Abs(i - mid) * _openStagger) / _openSeconds);
        float inv = 1f - p;
        return 1f - inv * inv * inv;
    }

    // ------------------------------------------- the owner's CHARACTER-SWAP EXCHANGE --
    //
    // The 1:1 ruling ("alle Interaktionen, ANIMATIONEN und Anzeigen des Controllboards") applied to
    // the exchange CardFan gained on 2026-08-09: when the owner switches which character's hand
    // they are looking at while their fan is up, their hand is not edited, it is WIPED — the old
    // one gathers off one end of the arc while the new one deals out of the other, the two halves
    // crossing in depth and counter-rolling. A peer whose ghost fan simply re-sized itself would be
    // watching the very content edit that report was about, one screen over.
    //
    // WHAT RIDES THE WIRE FOR THIS: NOTHING NEW, AND IN PARTICULAR NO CARD IDENTITY. The trigger is
    // a change in the owner's raw scenario focus (record 22) or map-room character key (record 20).
    // Front-art resolution remains separate and grants its own permission. The animation uses
    // that identity edge, the card COUNT that was already broadcast, and the owner's synced hand and head —
    // exactly like the fan-out reveal and the depth bow above. A MOTION CARRIES NO IDENTITY: what
    // is mirrored here is where slabs move, never which cards they are. The slabs on their way out
    // keep whatever face state the reveal gate had already granted them and are re-gated every
    // frame by UpdateFaces, so a phase that turns secret mid-wipe turns the leavers to BACKS in the
    // same frame it turns the arrivers — the gate is never outrun by an animation.
    //
    // Motion follows the owner's existing identity records, independently of the face predicate:
    // raw scenario focus in record 22, map-room loadout character in record 20. The face resolver
    // still decides what each slab may wear; an identity edge grants no permission to show fronts.

    /// <summary>Seconds since the owner's exchange began (-1 = none). Advanced on the caller's
    /// unscaled dt, like the reveal.</summary>
    private float _swapElapsed = -1f;

    /// <summary>How many slabs the outgoing wave started with — it fixes the wipe's rhythm and the
    /// gather point's place on the arc (CardFan._swapOutCount).</summary>
    private int _swapOutCount;

    /// <summary>The character this fan is currently drawn for (0 = not resolved yet) — the FACE
    /// question, and the only one <see cref="RemoteBoardFocus.DisplayedActor"/> answers.</summary>
    private int _shownActorId;

    /// <summary>The last visible fan's motion identity: raw scenario focus (record 22) or
    /// map-room loadout character key (record 20). These are separate identity domains; neither
    /// asks which card faces the viewer is allowed to see.</summary>
    private FanExchangeIdentity _swapIdentity;

    /// <summary>…and the actor object behind it, carried forward one frame so that when the edge
    /// fires the wave can be handed the character it is WEARING rather than the one replacing it.</summary>
    private CPlayerActor? _shownActor;

    /// <summary>One-shot guard for the displayed-actor resolve failure line.</summary>
    private bool _loggedShownActorError;

    /// <summary>The slabs of the hand being replaced, and the pose each of them started from.
    /// Parented under the same root as the live ones, so they ride the owner's hand while they fly.</summary>
    private readonly List<GameObject> _leaving = new(MaxCards);
    private readonly List<RemoteCardArt?> _leavingFaces = new(MaxCards);
    private readonly List<Vector3> _leavePos = new(MaxCards);
    private readonly List<Quaternion> _leaveRot = new(MaxCards);
    private readonly List<float> _leaveScale = new(MaxCards);

    /// <summary>Each leaver's place IN THE WAVE — not its place in the list. Slabs are destroyed as
    /// they land and the earliest land first, so a list position would shorten every remaining
    /// slab's stagger delay each time one went and the tail of the wipe would snap to the gather
    /// point in one frame (CardFan._leaveIndex documents the same trap).</summary>
    private readonly List<int> _leaveIndex = new(MaxCards);

    /// <summary>Progress (0..1) of outgoing slab <paramref name="i"/> — CardFan.OutProgress.</summary>
    private float OutProgress(int i) =>
        Mathf.Clamp01((_swapElapsed - i * _swapStagger) / Mathf.Max(0.02f, _swapDuration));

    /// <summary>Progress (0..1) of incoming slab <paramref name="j"/> — CardFan.InProgress. The
    /// arrival's head start is measured against the per-card duration, not the whole wave, so the
    /// two halves stay in lockstep at every slot (see CardFan's exchange region).</summary>
    private float InProgress(int j)
    {
        float dur = Mathf.Max(0.02f, _swapDuration);
        float delay = (1f - Mathf.Clamp01(_swapOverlap)) * dur;
        return Mathf.Clamp01((_swapElapsed - delay - j * _swapStagger) / dur);
    }

    /// <summary>The card count the exchange's two end points are derived from: the LARGER of the two
    /// hands, so the gather and the deal point are true mirror images and both sit a full
    /// FanSwapTravel clear of BOTH arcs — CardFan.SwapArcSpan.</summary>
    private int SwapArcSpan() => Mathf.Max(1, Mathf.Max(_swapOutCount, _cards.Count));

    /// <summary>Whole-exchange length for the two hand sizes — CardFan.SwapTotalSeconds.</summary>
    private float SwapTotalSeconds(int outCount, int inCount)
    {
        float dur = Mathf.Max(0.02f, _swapDuration);
        return (1f - Mathf.Clamp01(_swapOverlap)) * dur + dur
               + Mathf.Max(0, Mathf.Max(outCount, inCount) - 1) * _swapStagger;
    }

    /// <summary>CardFan.EaseOutBack.</summary>
    private static float EaseOutBack(float t, float s)
    {
        float u = t - 1f;
        return 1f + u * u * ((s + 1f) * u + s);
    }

    /// <summary>CardFan.EaseInBack.</summary>
    private static float EaseInBack(float t, float s) => t * t * ((s + 1f) * t - s);

    /// <summary>The point a hand is gathered into (<paramref name="side"/> = +1) or dealt out of
    /// (−1) — CardFan.SwapGatherPoint against the OWNER's own resolved dials.</summary>
    private void SwapGatherPoint(int n, float side, out Vector3 pos, out Quaternion rot)
    {
        // Gated on the owner's [Cards] FanCurveByFill exactly as the steady layout is — the gather
        // point sits ON the arc the fan describes, so a flattened fan must throw from a flattened end.
        float fill = _curveByFillOn
            ? Mathf.Clamp01((float)Mathf.Max(n, 1) / Mathf.Max(1, _maxHandForCurve))
            : 1f;
        float arch = _archFactor * fill;
        float tilt = _tiltFactor * fill;
        float step = n > 1 ? Mathf.Min(_perCardStepDegrees, _arcSweepDegrees / (n - 1)) : 0f;
        float endAngle = step * (n - 1) * 0.5f;
        float rad = endAngle * Mathf.Deg2Rad;
        pos = new Vector3(side * (Mathf.Sin(rad) * _radius + Mathf.Max(0f, _swapTravel)),
                          (Mathf.Cos(rad) - 1f) * _radius * arch,
                          0f);
        rot = Quaternion.Euler(0f, 0f, -side * endAngle * tilt + side * _swapSpinDegrees);
    }

    /// <summary>
    /// Arm the exchange: the slabs the fan is showing become the outgoing wave, and
    /// <see cref="_builtCount"/> is invalidated so the caller builds a fresh set for the incoming
    /// hand — necessary even when the two hands happen to be the same SIZE, which is exactly the
    /// case where the old code would have silently reused the slabs and shown no exchange at all.
    ///
    /// <para>SLABS ALREADY LEAVING FROM A PREVIOUS SWITCH ARE RE-CAPTURED WHERE THEY ARE, and then
    /// the whole list is renumbered — <c>CardFan.BeginSwapOut</c> + <c>ReindexLeaving</c>, term for
    /// term, and for the same reason. The clock restarts at 0, so an old entry left holding the
    /// pose it had at the FIRST switch would be yanked back to it; an old entry left holding its
    /// old wave place would push the new slabs a whole generation's worth of stagger later; and
    /// <see cref="_swapOutCount"/> summed over both generations would move the gather point. All
    /// three are the common path — scrubbing the initiative row is the reported gesture.</para>
    /// </summary>
    private void BeginSwap(CPlayerActor? leavingActor)
    {
        _openElapsed = -1f; // the exchange supersedes a reveal still in the air (CardFan.BeginSwapOut)

        // (1) the previous generation keeps going from where it IS.
        for (int i = 0; i < _leaving.Count; i++)
        {
            GameObject slab = _leaving[i];
            if (slab == null)
                continue;
            Transform t = slab.transform;
            _leavePos[i] = t.localPosition;
            _leaveRot[i] = t.localRotation;
            _leaveScale[i] = t.localScale.x;
        }

        // (2) the hand on screen joins it, behind the stragglers.
        for (int i = 0; i < _cards.Count; i++)
        {
            GameObject slab = _cards[i];
            RemoteCardArt? face = i < _faces.Count ? _faces[i] : null;
            if (slab == null)
            {
                // The slab died under us (external destruction — see EnsureRoot). Its face's clone
                // went with it, but release the bookkeeping rather than drop it on the Clear below.
                face?.Destroy();
                continue;
            }
            Transform t = slab.transform;
            // The owner's card crumbles the instant it leaves (VRCard.Vanish emits before the
            // fade), so the mirror emits when the slab JOINS the leaving set, not when it is
            // finally destroyed several frames later.
            EmitMirroredCardDust(slab, appear: false);
            _leaving.Add(slab);
            _leavingFaces.Add(face);
            _leavePos.Add(t.localPosition);
            _leaveRot.Add(t.localRotation);
            _leaveScale.Add(t.localScale.x);
            _leaveIndex.Add(0); // renumbered across the whole wave below
        }

        // (3) one wave, numbered 0..n-1 however many generations built it.
        for (int i = 0; i < _leaving.Count; i++)
            _leaveIndex[i] = i;
        _swapOutCount = _leaving.Count;

        // Hand the slabs over WITHOUT destroying them: Rebuild only ever destroys what these lists
        // still name, so emptying them here is what keeps the outgoing wave alive.
        _cards.Clear();
        _seeded.Clear();
        _bodyWearsBack.Clear();   // index-aligned with _cards; a stale FALSE would skip a body write
        _faces.Clear();
        _builtCount = -1;
        _frontsShown = false;
        ClearPops();
        ClearReturnGlide(); // the slabs are handed to the outgoing wave; the seat index is gone
        // ANTI-CHEAT: the wave wears the OUTGOING character's faces, so it stays under the OUTGOING
        // character's reveal gate — not the arriving one's. See UpdateFaces.
        _leavingActor = leavingActor;
        _swapElapsed = 0f;
    }

    /// <summary>The character whose faces the outgoing wave is wearing (null = none / unresolved).
    /// Kept for exactly one reason: <see cref="RevealGate.ShowRoundCardFronts"/> is PER ACTOR — it
    /// folds in that actor's own <c>IsUnderMyControl</c> — so gating the leaving slabs on the
    /// ARRIVING character's verdict would be gating them on the wrong rule. Latched here rather than
    /// re-derived because by the time the wave is flying the fan is already displaying somebody
    /// else.</summary>
    private CPlayerActor? _leavingActor;

    /// <summary>Advance the exchange and drive the outgoing wave; destroy each slab (and release its
    /// cloned front) the moment its own flight ends, so nothing lingers shrunk at the gather point.
    /// Allocation-free.</summary>
    private void TickSwap(float dt)
    {
        if (_swapElapsed < 0f)
            return;
        _swapElapsed += Mathf.Max(dt, 0f);

        if (_leaving.Count > 0 && _root != null)
        {
            SwapGatherPoint(SwapArcSpan(), 1f, out Vector3 gather, out Quaternion gatherRot);
            float seed = Mathf.Clamp(_swapSeedScale, 0.02f, 1f);
            float arc = Mathf.Max(0f, _swapArc);
            float s = Mathf.Clamp(_swapSettleOvershoot, 0f, 3f);
            for (int i = _leaving.Count - 1; i >= 0; i--)
            {
                GameObject slab = _leaving[i];
                float t = OutProgress(_leaveIndex[i]); // wave place, not list place — see _leaveIndex
                if (slab == null || t >= 1f)
                {
                    if (i < _leavingFaces.Count)
                        _leavingFaces[i]?.Destroy();
                    if (slab != null)
                        Object.Destroy(slab);
                    _leaving.RemoveAt(i);
                    _leavingFaces.RemoveAt(i);
                    _leavePos.RemoveAt(i);
                    _leaveRot.RemoveAt(i);
                    _leaveScale.RemoveAt(i);
                    _leaveIndex.RemoveAt(i);
                    continue;
                }
                float e = EaseInBack(t, s);
                Vector3 p = Vector3.LerpUnclamped(_leavePos[i], gather, e);
                p.z += arc * Mathf.Sin(t * Mathf.PI); // ducks AWAY; the arriving half bows the other way
                Transform tr = slab.transform;
                tr.localPosition = p;
                tr.localRotation = Quaternion.Slerp(_leaveRot[i], gatherRot, Mathf.Clamp01(e));
                // …to SEED x the owner's card size, not to the bare seed: _swapSeedScale is a
                // FRACTION of a card (0.02-1), and the arriving half multiplies it by SlabScale one
                // loop over. A leaving slab that gathered to the bare fraction would shrink to a
                // nominal card's 2 % while its own size is the owner's — the same term this fan
                // dropped everywhere else.
                tr.localScale = Vector3.one * Mathf.LerpUnclamped(_leaveScale[i], seed * SlabScale, e);
            }
        }

        if (_swapElapsed >= SwapTotalSeconds(_swapOutCount, _cards.Count) && _leaving.Count == 0)
            _swapElapsed = -1f;
    }

    /// <summary>End the exchange NOW, destroying anything still on its way out — the fan is going
    /// away (hidden, rebuilt under us, or destroyed) and a slab that stopped being ticked would sit
    /// frozen half-way off the hand forever.</summary>
    private void EndSwap()
    {
        for (int i = 0; i < _leaving.Count; i++)
        {
            if (i < _leavingFaces.Count)
                _leavingFaces[i]?.Destroy();
            if (_leaving[i] != null)
                Object.Destroy(_leaving[i]);
        }
        _leaving.Clear();
        _leavingFaces.Clear();
        _leavePos.Clear();
        _leaveRot.Clear();
        _leaveScale.Clear();
        _leaveIndex.Clear();
        _leavingActor = null; // no wave, no outgoing character to gate
        _swapElapsed = -1f;
    }

    /// <summary>Diagnostics dedup: whether the fan is CURRENTLY showing cloned fronts (vs backs), so we
    /// log exactly once on each backs↔fronts transition (never per frame, never card identities).</summary>
    private bool _frontsShown;

    /// <summary>The held-seat drop has been reported once for this fan instance — see the note at
    /// the length belt for why it is latched rather than change-gated.</summary>
    private bool _loggedHeldSeatDrop;

    // ---- NOTHING IS HANDED OUT BY THIS FAN (report 2, 2026-09-07) -----------------------------
    //
    // "Es geht mir explizit darum, dass mein Mitspieler ein Fächer öffnet und ich hineingreife und
    // eine Karte von ihm in der Hand hab. Das darf nicht sein." — and, naming the very copy this
    // section used to hand out: "Immerhin ist es ein Klon aktuell, also es verschwindet dann
    // wenigstens keine Karte aus seinem Fächer die ich genommen habe."
    //
    // THE LINE IS A SURFACE LINE, NOT AN OWNERSHIP ONE, and both halves must be read together:
    //   * HIS OWN board's fan, showing whatever character he switched to — including a teammate's —
    //     stays exactly as ModBuild 474 shipped it: shown, and a card may be LIFTED to be read
    //     (CardFan.FanMode.Inspect, gated on RevealGate.ShowRoundCardFronts). Board/CharacterFocus
    //     and Cards/CardsGameApi own that rule and it did not move.
    //   * THIS fan — the one hanging at the TEAMMATE'S AVATAR — hands out nothing. Not a real card,
    //     and not a clone either.
    //
    // THIS OVERTURNS THE 2026-08-15 RULING that built Cards/CardBorrow.cs ("Ich will auch in der
    // Lage sein, dass man die fremden Handkarten jederzeit auch in der Hand nehmen kann"), and it is
    // NOT a contradiction on his part — recorded here because a future reader who finds the deleted
    // file in the history would otherwise read it as an accident. In August, switching to a
    // teammate's character on one's OWN board did not produce a handleable fan, so reaching into the
    // peer's avatar fan was the ONLY way to read a teammate's card. ModBuild 474 made the own-board
    // route work (HandInspectable moved from the OWNER term to the FRONT term). The borrow is
    // therefore now both REDUNDANT and UNWANTED, and the whole of Cards/CardBorrow.cs — the
    // IBorrowedCardSource interface, the BorrowTarget slab component, the sweep, the copy and
    // BorrowedCardWatch — went with it. This fan was its only implementer and its only caller.
    //
    // THE REFUSAL IS STRUCTURAL, NOT A DECLINE, and that is the whole of it: the slabs carry NO
    // collider of any kind any more (the borrow trigger box was the only one they ever had), no
    // IGrabbable, no IFanSweepTarget and no registration in any sweep. ProximityGrabber elects over
    // colliders, so there is nothing here for a hand to win; the routing does not exist rather than
    // being blocked. The layer-2 re-stamp that used to follow VRLayers.Apply went with the collider
    // — it existed ONLY to keep that trigger box out of RayInteractor's Physics.Raycast mask, and a
    // slab root with no collider and no renderer is invisible to both the world ray and the camera.
    //
    // THE PICTURE IS UNTOUCHED, and that is the standing 1:1 ruling: a watcher still sees the
    // owner's fan, its card count, its arc, its order, its highlight pop, its open/close animation
    // and its cloned FRONTS under RevealGate. This removes an INTERACTION, never a picture.

    /// <summary>
    /// THE INSTRUMENT FOR THE REFUSAL — it grants nothing and costs nothing.
    ///
    /// <para>A structural removal is invisible in a log: "he reached in and got nothing" and "he
    /// never reached in" produce the same silence, and that is exactly the reading this round needs
    /// to distinguish. So the fan answers ONE question, on the TRIGGER-DOWN EDGE ONLY: was an empty
    /// hand inside this fan's slab cloud when the player pulled? No hover state, no haptic, no
    /// trigger claim, no collider and no per-frame sweep — a bounded loop over at most
    /// <see cref="MaxCards"/> slab centres, on the frames a human presses a trigger.</para>
    ///
    /// <para>The envelope is deliberately GENEROUS: palm-to-slab-CENTRE against
    /// <c>FanSweep.ResolveReach(...).Palm</c> plus the slab's own centre-to-corner half-diagonal,
    /// which is the upper bound of the surface distance the deleted sweep measured with
    /// <c>Collider.ClosestPoint</c>. Over-approximating can only make the probe report MORE reaches
    /// than the old affordance would have taken, which is the safe direction for an instrument whose
    /// only output is a log line.</para>
    /// </summary>
    private void ReportRefusedReach()
    {
        if (_cards.Count == 0 || _root == null || !_root.activeInHierarchy)
            return;

        for (int h = 0; h < 2; h++)
        {
            VRHand? hand = h == 0 ? VRHands.Left : VRHands.Right;
            if (hand == null || !hand.HasPose || !hand.TriggerDown || hand.Grabber.Held != null)
                continue;

            Vector3 palm = hand.Rig.PalmCenter.position;
            int nearest = -1;
            float nearestDistance = float.MaxValue;
            float envelope = 0f;
            for (int i = 0; i < _cards.Count; i++)
            {
                GameObject slab = _cards[i];
                if (slab == null)
                    continue;
                float faceWidthWorld = _visibleFace.x * slab.transform.lossyScale.x;
                float faceHeightWorld = _visibleFace.y * slab.transform.lossyScale.y;
                float halfDiagonal =
                    0.5f * Mathf.Sqrt(faceWidthWorld * faceWidthWorld + faceHeightWorld * faceHeightWorld);
                float reach = Cards.FanSweep.ResolveReach(hand.WorldScale, faceWidthWorld).Palm + halfDiagonal;
                float distance = Vector3.Distance(palm, slab.transform.position);
                if (distance <= reach && distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                    envelope = reach;
                }
            }
            if (nearest < 0)
                continue;

            _refusedReaches++;
            // HW-VERIFY
            VRLog.Note("Net", $"PEER FAN REACH REFUSED [player {_owner.PlayerId}]: the {hand.Side} "
                + $"hand pulled the trigger {nearestDistance * 100f / Mathf.Max(hand.WorldScale, 1e-4f):F1} cm "
                + $"from slab {nearest} of {_cards.Count} (envelope "
                + $"{envelope * 100f / Mathf.Max(hand.WorldScale, 1e-4f):F1} cm, real metres) and got "
                + $"NOTHING — refusal #{_refusedReaches} on this fan. This surface is the fan hanging "
                + "at ANOTHER PLAYER'S AVATAR (Net.Remote.RemoteHandFan) and it hands out nothing BY "
                + "CONSTRUCTION: its slabs carry no collider, no IGrabbable and no sweep target, so "
                + "there is no card here to take and nothing to decline. Report 2 of 2026-09-07. "
                + "To read a teammate's card, switch to that character on YOUR OWN board — that fan "
                + "is CardFan.FanMode.Inspect and a card may be lifted there.");
        }
    }

    /// <summary>How many trigger pulls this fan has refused since it was built — the count that
    /// makes the removal falsifiable (see <see cref="ReportRefusedReach"/>).</summary>
    private int _refusedReaches;

    /// <summary>Reused scratch buffer for the remote actor's HAND-pile card widgets (no per-frame alloc).</summary>
    private readonly List<AbilityCardUI> _handBuffer = new(MaxCards);

    /// <summary>Set on the frames the length belt above refuses the fronts, to (this client's model
    /// count, the owner's wire count). Null whenever the belt did not fire. It exists ONLY so the
    /// BACKS line can name the blocker rather than list the candidates — see the log block.</summary>
    private (int Model, int Wire)? _countBelt;

    /// <summary>Change gate for the FAN SOURCE receiver line. 0xFF rather than 0 so the FIRST
    /// sample prints, including the ordinary "their fan is their hand" reading: a line that states
    /// the resting value once is what makes a later silence readable as "nothing changed" rather
    /// than as "this surface never ran" — the failure mode that made the held card produce exactly
    /// zero lines in two 100 MB logs.</summary>
    private byte _loggedFanList = 0xFF;

    /// <summary>Which list this frame's fan was resolved from (record 43), for the census line.
    /// <see cref="NetProtocol.HeldFaceListNone"/> is the HAND, which is the resting value and the
    /// one every sender predating ModBuild 459 means.</summary>
    private byte _censusList;

    /// <summary>
    /// TRUE only when the reveal gate itself is what refused this frame's fronts — i.e. a scenario
    /// is running, a character resolved, and <see cref="RevealGate.ShowRoundCardFronts"/> said no.
    ///
    /// <para>IT EXISTS BECAUSE ONE CENSUS STRING WAS CARRYING TWO CAUSES, and that ambiguity is what
    /// report item 7 was read through for a round. "RevealGate.CardFaces(Selectable) named no
    /// source, or no widget resolved" is true of a SHUT GATE (the game's own secret window — correct
    /// and expected) and of an OPEN GATE with nothing to resolve (a defect, every time), and a
    /// reader counting BACKs against it is counting two populations with one number. The remedy is
    /// not a better sentence, it is a second term.</para>
    /// </summary>
    private bool _censusGateShut;

    // ---- THE MAP PHASE (report 4, 2026-08-22) -------------------------------------------------
    //
    // "Handkarten sind nicht sichtbar im Multiplayer im Map-Bereich. Das soll nicht sein, die
    //  Handkarten sollen wie in der Aktionsphase im Szenario voll sichtbar sein, wenn man den Fächer
    //  eines anderen Spielers betrachtet. Aktuell sieht man nur die Rückseiten (wie es zur
    //  Auswahlphase der Fall ist)."
    //
    // ROOT CAUSE, and it was NOT the reveal gate. RevealGate.ShowRoundCardFronts has always been
    // OPEN on the map (its conjunction folds in RevealGate.InScenario, which is false there, and its
    // own doc names the case). What closed the fronts was the SECOND term this very file added
    // beside it — `RevealGate.InScenario &&` at UpdateFaces — whose comment says exactly what it is
    // for: "require an actual running scenario before touching the game's hand UI (the clone's
    // widget lifecycle depends on scenario singletons)". That is TRUE and it is RIGHT for
    // ResolveHandFronts, which reads CardsHandManager.Instance.GetHand(actor).cardsUI — neither the
    // manager nor the CPlayerActor exists in the map phase. The defect is that a CAPABILITY test's
    // safe default ("we cannot resolve fronts here") was left standing as the answer to a SECRECY
    // question ("these cards are secret"). RevealGate's map-phase block carries the whole argument.
    //
    // THE FIX IS A SECOND CAPABILITY, NOT A RELAXED RULE. In the map phase the mod's own 3D map room
    // grew a card hand in ModBuild 192 (WorldUI/MapRoom/MapRoomHand.*), and a peer's fan there is
    // their SCENARIO LOADOUT. The receiver asks the map room which character that is
    // (MapRoomHand.TryResolvePeerLoadout — the count off the wire plus this client's own replicated
    // party data; nothing about a peer's objects is inspected, and no card identity rides the wire)
    // and prints the faces from CAbilityCard models through the same
    // RemoteAbilityCardSource borrow the local map fan already uses.
    //
    // ANTI-CHEAT IS UNCHANGED: RevealGate.ShowMapPhaseHandFronts requires the ABSENCE of a running
    // scenario, so it is disjoint from the secret selection window by construction — it cannot be
    // true in any frame ShowRoundCardFronts would close, and the scenario branch below is still the
    // only thing that can draw a scenario hand.

    /// <summary>The peer's map-phase loadout IN FULL, in the initiative order both machines build it
    /// in (empty = unresolved, i.e. backs). Models rather than widgets: the map phase has no
    /// <c>AbilityCardUI</c> anywhere.
    ///
    /// <para>THE WHOLE LOADOUT AND NOT THE ARC, and the distinction is load-bearing since
    /// 2026-09-07: record 36's held-card seat is named against the sender's full <c>_loadout</c>
    /// (<c>MapRoomHand.TryNameLocalLoadoutSeat</c>), so <see cref="MapLoadoutSeat"/> must index
    /// THIS list. What the fan's slabs are drawn from is <see cref="_mapArc"/>.</para></summary>
    private readonly List<CAbilityCard> _mapBuffer = new(MaxCards);

    /// <summary>
    /// The peer's map arc — <see cref="_mapBuffer"/> with the seat in their fist removed — and the
    /// list the slabs are index-aligned with. Rebuilt every tick of the map branch rather than
    /// cached with the buffer, because a card is picked up and put down far faster than
    /// <see cref="MapResolveInterval"/>.
    ///
    /// <para>WHY REMOVING A SEAT IS NOT A GUESS. It is exactly what the OWNER's own fan did to
    /// produce the arc: <c>CardFan.Remove</c> takes the plucked card out of the layout list and
    /// leaves every other card at its own index, so an ordered list minus seat k IS their arc. The
    /// seat is the one record 36 already carries to draw that card's own front, in the same index
    /// space, built by the same expression on both machines — and the length belt in
    /// <c>UpdateFaces</c> is what proves the two agree before a single face is printed.</para>
    /// </summary>
    private readonly List<CAbilityCard> _mapArc = new(MaxCards);

    /// <summary>
    /// WHICH SEAT of this peer's map LOADOUT is in their fist right now, or -1.
    ///
    /// <para>Record 36 through <c>RemoteAvatar.SingleHeldHandSeat</c>, narrowed to
    /// <c>NetProtocol.HeldFaceListMapLoadout</c> — the list id
    /// <c>LocalRigSampler.NameHeldMapCard</c> writes for a card lifted out of the map fan. It is
    /// NOT <c>RemoteAvatar.HeldHandSeats</c>, and that is the whole of the defect this method
    /// exists to close: that helper filters record 36 to the HAND list (or a record-43 pile), so
    /// a map-loadout seat was thrown away before any caller could see it.</para>
    ///
    /// <para>TWO FISTS ANSWER -1, by construction: <c>SingleHeldHandSeat</c> refuses when both pose
    /// slots name an arc card, because it cannot say which is which. The arc is then one card
    /// longer than the wire, the length belt refuses, and the fan draws BACKS — the safe direction,
    /// and the one this whole path had never reached. Reading BOTH seats needs an accessor
    /// <c>RemoteAvatar</c> does not expose (its <c>HeldSeatsIn</c> is private and its public
    /// map-aware entry point answers a single seat); that is filed rather than forced, because a
    /// second reading of record 36 in this file is how two surfaces come to disagree about what
    /// "in the fist" means.</para>
    /// </summary>
    private int HeldMapLoadoutSeat()
    {
        if (!_owner.SingleHeldHandSeat(out int seat, out int listLength, out _, out byte listId))
            return -1;
        if (listId != NetProtocol.HeldFaceListMapLoadout || seat < 0 || listLength <= 0)
            return -1;
        return seat;
    }

    /// <summary>
    /// May slab <paramref name="widget"/> show its front? TRUE outright unless the arc was opened
    /// by the per-card burn exception alone (<paramref name="publicCardsOnly"/>), in which case the
    /// card has to earn it through the CARD-AWARE <c>RevealGate.CardFaces</c> overload — the one
    /// that can reach <c>RevealGate.IsPubliclyRevealedCard</c>.
    ///
    /// <para>ONE PREDICATE, OWNED BY <c>RevealGate</c>, asked with the same population the whole-fan
    /// gate was asked with, so this cannot become a second opinion about secrecy. Any failure to
    /// name the card at all answers FALSE, which is the back — this file's standing direction.</para>
    /// </summary>
    private static bool PrintsFront(bool publicCardsOnly, CPlayerActor? actor, AbilityCardUI? widget)
    {
        if (!publicCardsOnly)
            return true;
        if (actor == null || widget == null)
            return false;
        try
        {
            CAbilityCard? card = widget.AbilityCard;
            if (card == null)
                return false;
            return RevealGate.CardFaces(RevealGate.PeerCardPopulation.PickFan, actor,
                                        card.CardInstanceID) != RevealGate.CardFaceSource.None;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>Project <see cref="_mapBuffer"/> into <see cref="_mapArc"/>, dropping
    /// <paramref name="heldSeat"/> when it names one. Allocation-free; a seat out of range drops
    /// nothing, which leaves the length belt to refuse rather than this method to guess.</summary>
    private void BuildMapArc(int heldSeat)
    {
        _mapArc.Clear();
        for (int i = 0; i < _mapBuffer.Count; i++)
        {
            if (i == heldSeat)
                continue;
            _mapArc.Add(_mapBuffer[i]);
        }
    }

    /// <summary>
    /// The card at map-loadout seat <paramref name="seat"/> of THIS peer's loadout, or null when the
    /// seat cannot be trusted. For the held-card record's map branch
    /// (<c>NetProtocol.HeldFaceListMapLoadout</c>) — see <see cref="RemoteHeldCardFace"/>.
    ///
    /// <para>WHY THE HELD CARD ASKS THE FAN RATHER THAN RESOLVING ITS OWN. Naming the loadout means
    /// naming a CHARACTER first, and that resolution is a tiered deduction with a cache
    /// (<see cref="ResolveMapFronts"/> over <c>MapRoomHand.TryResolvePeerLoadout</c>, keyed on the
    /// peer's record-20 character key and its card count). Two independent runs of it can answer
    /// with two different characters on the frame a peer switches — and the failure that produces is
    /// not a missing face, it is the RIGHT card index into the WRONG character's loadout, i.e. a
    /// confident front showing a card the peer is not holding. One resolve, one answer, and the seat
    /// is a seat in the very list the fan beside the hand is drawing from.</para>
    ///
    /// <para><paramref name="senderLength"/> is the record's length byte and is checked here for the
    /// same reason every other branch checks it: an index is a name only while both copies of the
    /// list agree. A disagreement draws a BACK, never a shifted face.</para>
    /// </summary>
    internal CAbilityCard? MapLoadoutSeat(int seat, int senderLength)
    {
        if (!_mapFronts)
        {
            // THE HELD CARD OUTLIVES THE FAN (2026-09-05, report item 2a): "Wird der Faecher
            // geschlossen aber eine Karte ist noch in der Hand bekommt diese keine Vorderseite mehr,
            // das soll nicht sein. Die Vorderseite soll dauerhaft sichtbar sein."
            //
            // _mapFronts says "this fan is drawing map faces RIGHT NOW", and it is false the moment
            // the fan hides — which is the moment the owner lowers it, because the wire count is
            // CardFan.Current.Count and a closed fan publishes no Current at all. Tick then takes
            // its `count == 0` bail, never reaches UpdateFaces, and this answered null for a card
            // that is still physically in the peer's hand. It was a CAPABILITY answer ("no buffer
            // has been resolved this frame") standing in for a secrecy one ("this card may not be
            // shown") — the exact substitution RevealGate's own map-phase block is written about,
            // for the third time and one method over.
            //
            // The secrecy question is asked HERE, of the gate that owns it, and the buffer is then
            // resolved on demand. Cost: one walk of the party, on a card being picked up, throttled
            // by ResolveMapFronts' own cadence.
            if (!RevealGate.ShowMapPhaseHandFronts)
                return null;
            ResolveMapFronts(senderLength);
        }
        if (seat < 0 || seat >= _mapBuffer.Count || _mapBuffer.Count != senderLength)
            return null;
        return _mapBuffer[seat];
    }

    /// <summary>Which card id each slab has ALREADY printed on the map path (-1 = none), parallel to
    /// <see cref="_faces"/>. The scenario path gets its dedup for free — <c>RemoteCardArt.ShowFront</c>
    /// keys on the live widget's instance id — but a POOLED borrow manufactures a fresh widget every
    /// call, so calling it per frame would spawn, clone and recycle a card widget per slab per frame.
    /// This is the same latch <c>MapRoomHand.PrintPendingFaces</c> uses (there it is "does this slot
    /// have a face object yet"), for the same reason.</summary>
    private readonly List<int> _mapPrinted = new(MaxCards);

    /// <summary>What each slab's card BODY is wearing on its FRONT fan (true = the card BACK) —
    /// INDEX-ALIGNED with <see cref="_cards"/> and cleared everywhere that list is, on exactly the
    /// convention <see cref="_mapPrinted"/> and <c>_seeded</c> already follow.
    ///
    /// <para>USER ITEM 10 (2026-09-06): every remote slab gave BOTH submeshes the card back, while a
    /// local card's backing wears <c>CardMesh.CreateEdgeMaterial</c> on submesh 0 — the FRONT fan
    /// plus the rim, i.e. the band that frames the print. So a peer's readable card front was framed
    /// by the burgundy field and gold diamond lattice of its own BACK, on a board that was not
    /// fading at all, and the user named it literally: he is seeing the Rückseite.</para>
    ///
    /// <para>IT IS AN EDGE GATE AND NOTHING ELSE. <c>CardMesh.SetBodyFrontFace</c> is idempotent, so
    /// this list buys no correctness — it buys not walking that registry once per slab per frame on
    /// a fan of up to <see cref="MaxCards"/>. The safe direction of a stale entry is therefore
    /// <c>true</c>: a stale true costs one redundant write, a stale false would silently skip one,
    /// which is why every entry is seeded true and the list dies with the slabs it names.</para>
    /// </summary>
    private readonly List<bool> _bodyWearsBack = new(MaxCards);

    /// <summary>Tell slab <paramref name="index"/>'s card BODY what its FRONT fan wears — see
    /// <c>CardMesh.SetBodyFrontFace</c>, which owns the rule and the (fade-aware) write. Idempotent
    /// all the way down; the edge gate here is what keeps a steady fan off CardMesh's body registry.
    ///
    /// <para>MID-FADE: this fan is a <c>PeerBoardFade</c> follower under the <c>WhileOverBoard</c>
    /// rule, so a ramp can be running while it is parked over its owner's board. That is why the
    /// write goes through <c>PeerBoardFade.SetSubmeshMaterial</c> — it edits the remembered authored
    /// array and the installed clone for slot 0 and leaves the ramp alone — and not through
    /// <c>sharedMaterials</c>, which would destroy those clones. That seam is argued from
    /// <c>Swap</c>/<c>Restore</c>/<c>SettleDepthState</c> and is NOT hardware-verified.</para>
    /// </summary>
    private void SetFrontFace(int index, bool showsBack)
    {
        if (index < 0 || index >= _cards.Count)
            return;
        // Grown here and nowhere else, always in the SAFE direction: every slab is BUILT wearing the
        // card back, so an entry this list has never seen is true.
        while (_bodyWearsBack.Count <= index)
            _bodyWearsBack.Add(true);
        if (_bodyWearsBack[index] == showsBack)
            return; // an EDGE, so a steady fan never walks CardMesh's body registry at all
        GameObject slab = _cards[index];
        if (slab == null)
            return;
        CardMesh.SetBodyFrontFace(slab.transform, showsBack);
        _bodyWearsBack[index] = showsBack;
    }


    /// <summary>Card count the map loadout was last resolved for (-1 = never), and the next unscaled
    /// time the resolve may run again. The resolve walks the party and is NOT a per-frame cost: it
    /// re-runs on a count or character-key change, or on this slow cadence for a loadout edit made on
    /// the peer's side while their fan is up.</summary>
    private int _mapResolvedForCount = -1;
    private uint _mapResolvedForCharacterKey;

    private float _nextMapResolveAt;

    /// <summary>The map room's own sentence about WHICH tier identified the hand (or why none did),
    /// written verbatim into the faces diagnostic.</summary>
    private string _mapVerdict = "not resolved yet";

    /// <summary>The verdict already reported, so the diagnostic fires on a CHANGE of answer rather
    /// than per resolve.</summary>
    private string _loggedMapVerdict = string.Empty;

    /// <summary>True while the faces currently up came from <see cref="_mapArc"/> (the map phase)
    /// rather than from <see cref="_handBuffer"/> (a scenario). It used to drive the borrow as well
    /// — a borrow had to read a card out of the same buffer the slab's face was drawn from — and
    /// since report 2 of 2026-09-07 removed the borrow it drives the printing and the diagnostics
    /// only.</summary>
    private bool _mapFronts;

    /// <summary>Seconds between map-loadout resolves at a steady card count.</summary>
    private const float MapResolveInterval = 0.5f;

    /// <summary>Eased fan-local X (metres) where the OWNER's gaze pierces their fan plane — the
    /// depth-bow apex, tracked here exactly as <c>CardFan.UpdateCardPresentation</c> tracks it
    /// locally, from the head pose that already rides the rig packet.</summary>
    private float _gazeX;

    /// <summary>Scratch per-card depths (stagger + bow + stacking clamp) — a field so the per-frame
    /// layout stays allocation-free, exactly like <c>CardFan._depths</c>.</summary>
    private readonly float[] _depths = new float[MaxCards];

    /// <summary>True while the fan is anchored on the peer's real PALM anchor rather than the
    /// holder fallback; latched so the diagnostic fires once per change, never per frame.</summary>
    private bool _loggedPalmAnchor;
    private bool _anchorLogged;
    private int _loggedCount = -1;      // one geometry line per card-count change
    private float _geometryLogTime;     // throttle clock for the steady-state geometry line

    // ---- THE LIVE PEER FANS (the Enchantress edge, ModBuild 240) -----------------------------
    //
    // ModBuild 239 fixed the LOCAL half of the staleness this list exists for: a card enhanced at
    // the Enchantress kept its old face in the buyer's own VR hand fan, because that face is an
    // Object.Instantiate SNAPSHOT of a pooled widget and the game's own post-commit redraw
    // (SaveDataShared.ApplyEnhancementIcons → ObjectPool.GetAllCachedAbilityCards) cannot reach a
    // mod-owned clone. A PEER's fan is the same snapshot behind a stricter latch — PrintMapFace
    // refuses to re-print a slab whose _mapPrinted entry already equals the card's ID, and an
    // enhancement moves no card ID — so it went stale for the whole map visit too.
    //
    // WHY A REGISTRY AND NOT A SCENE SWEEP. The remedy (Net.RemoteFanEnhancementRefresh) runs on
    // ONE edge: the game's own enhancement commit. It needs the peers' fans, and this project has
    // already lost a whole frame budget to a FindObjectOfType sweep — so the fans put themselves in
    // a list instead. Membership is exactly the RemoteAvatar lifetime: RemoteAvatar.Destroy is the
    // only caller of Destroy() below, and it runs when the peer's avatar is gone for good.
    //
    // THE LOCAL FAN IS NOT IN HERE and cannot be: a RemoteHandFan only ever exists under a
    // RemoteAvatar, and NetAvatarDriver drops this client's own echo before an avatar is ever
    // created for it (NetAvatarDriver.cs:2760). The local fan is CardsDriver.OffScenarioFanCards
    // and stays ModBuild 239's business.
    private static readonly List<RemoteHandFan> s_live = new(4);

    /// <summary>Every peer hand fan that currently exists, newest last. Read-only to callers; the
    /// list is mutated only by the constructor and <see cref="Destroy"/>.</summary>
    internal static IReadOnlyList<RemoteHandFan> Live => s_live;

    /// <summary>The peer this fan belongs to — for diagnostics only, never a decision.</summary>
    internal int OwnerPlayerId => _owner.PlayerId;

    /// <summary>The fan's card slabs, index-aligned with <see cref="PrintedMapCardIds"/>. Exposed so
    /// the enhancement refresh can reach the printed FACE (a child of the slab) without this class
    /// having to know anything about enhancement stickers.</summary>
    internal IReadOnlyList<GameObject> Slabs => _cards;

    /// <summary>The per-slab map-print latch (<c>-1</c> = nothing printed), i.e. WHICH card id each
    /// slab is currently drawing on the map path. This is the state the refresh reports as "the
    /// print latch before and after": an in-place sticker rewrite deliberately leaves it alone,
    /// because the slab is still drawing the same CARD — only its face was corrected.</summary>
    internal IReadOnlyList<int> PrintedMapCardIds => _mapPrinted;

    public RemoteHandFan(RemoteAvatar owner)
    {
        _owner = owner;
        s_live.Add(this);
    }

    // ------------------------------------------------------------------ per frame --

    public void Tick(float dt)
    {
        // THE REFUSED REACH (report 2, 2026-09-07). This used to drive CardBorrow.Tick — the sweep
        // that handed a hand a read-only copy of a teammate's card. The gesture is gone; what is
        // left is the INSTRUMENT that says so, on the trigger-down edge only. FIRST, before every
        // early return below, so a pull at a fan this particular frame bails out of is still seen.
        ReportRefusedReach();

        SyncTuning();
        SyncFaceRect(); // AFTER SyncTuning: the printed rect is derived from the card size it resolves

        // Which hand does the fan hang off? owner.NonDominantHandHolder already resolves to the
        // LEFT holder when DominantRight is true (the sensible default), else RIGHT; fall back to
        // the avatar root when the holder is missing. (Handedness audit: this receiver mapping is
        // correct; the fan-on-the-wrong-hand report was the SENDER predicate,
        // LocalRigSampler.LocalDominantRight — fixed there.)
        Transform? holder = _owner.NonDominantHandHolder != null ? _owner.NonDominantHandHolder : _owner.Root;
        if (holder == null)
        {
            Hide();
            return;
        }

        // Strict no-op while the hand holder is inactive (hand not tracked this frame): hide the
        // fan and bail without posing or allocating. (The Root fallback is always active.)
        if (!holder.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        int count = Mathf.Clamp(_owner.HandCardCount, 0, MaxCards);
        // NOTHING TO SHOW AND NOTHING IN FLIGHT — the only state that may hide outright.
        //
        // `_cards.Count == 0` joined this condition on 2026-08-09, and it is the second half of the
        // missing-exchange report. The block below has always CLAIMED that "an empty incoming hand
        // still gets its wipe", but it could not deliver it: the wave that makes `_leaving` non-empty
        // is created by BeginSwap, which lives BELOW this line, so on the one frame that matters
        // `_leaving` is still empty and this bail fired first — Hide() → EndSwap(), the whole fan
        // gone in a blink while the owner watched a full gather. Switching to a character with an
        // empty hand (every card burnt, a long rest) is a real switch target and the owner's own fan
        // animates it (CardsDriver's edge fires on `_fan.Count > 0 || incoming > 0`).
        if (count == 0 && _leaving.Count == 0 && _cards.Count == 0)
        {
            Hide();
            return;
        }

        EnsureRoot(holder);
        if (_root == null)
            return;

        // Resolve the scenario face owner once for the arriving hand and the next outgoing wave.
        // This is a reveal-gated answer, not the motion identity: the map has no scenario actor,
        // and secret selection may pin the displayed actor while raw focus still changes.
        CPlayerActor? shownActor = null;
        int shownId = 0;
        try
        {
            shownActor = RemoteBoardFocus.DisplayedActor(_owner, out _);
            if (shownActor != null)
                shownId = NetFigures.StableActorId(shownActor);
        }
        catch (System.Exception ex)
        {
            shownActor = null;
            shownId = 0;
            // Report a failed face resolve once. The owner's wire identity can still drive motion.
            if (!_loggedShownActorError)
            {
                _loggedShownActorError = true;
                VRLog.Warn("Net", $"Remote hand fan [player {_owner.PlayerId}]: could not resolve which " +
                                  $"character the owner is displaying ({ex.Message}). The fan falls back to " +
                                  "BACKS; character-swap motion still follows the owner's wire identity.");
            }
        }

        // CHARACTER EXCHANGE HAS TWO OWNER SOURCES (2026-09-08 follow-up). The scenario path
        // already worked: MB482 host Fan EXCHANGE at 11005 matches remote 7213. The other four
        // local exchanges (host 5224/5502/5607, remote 2350) are followed by "map-room hand".
        // That owner path uses OffScenarioFanSwap, while this receiver only watched record 22,
        // whose scenario actor is absent on the map. Equal-size map hands therefore silently
        // reused every slab. Record 20 already names the loadout character for the face resolver;
        // its key now arms the SAME outgoing/incoming animation, without a new wire field.
        // Keep the domains separate: a map key is not a scenario actor id, even if bits coincide.
        int focusId = Board.CharacterFocus.FocusIdForPeer(_owner.PlayerId);
        bool mapPhase = RevealGate.InMapPhase;
        RemoteMapRoom.TryGetPeerFanCharacterKey(_owner.PlayerId, out uint mapCharacterKey);
        FanExchangeIdentity nextIdentity = mapPhase
            ? FanExchangeIdentity.Map(mapCharacterKey)
            : FanExchangeIdentity.Scenario(focusId != 0 ? focusId : shownId);
        if (_swapIdentity.ShouldExchangeTo(nextIdentity, _root.activeSelf, _cards.Count, count))
        {
            // The outgoing face clones retain their own actor and existing per-frame reveal gate.
            // Map fronts use ShowMapPhaseHandFronts; no incoming character's art is put on leavers.
            BeginSwap(_shownActor);
            VRLog.Info("Net", $"Remote hand fan EXCHANGE [player {_owner.PlayerId}]: {_swapOutCount} slab(s) " +
                              $"gather off the arc while {count} deal in — the owner switched which character " +
                              $"they are looking at ({(mapPhase ? "map-room character key, record 20" : "raw scenario focus, record 22")}). " +
                              "The MOTION is mirrored; no card identity is transmitted for it. Incoming " +
                              "and outgoing fronts remain under their respective reveal gates.");
        }
        _swapIdentity = nextIdentity;
        _shownActorId = shownId;
        _shownActor = shownActor;

        // An incoming count of zero can mean a DIFFERENT character has an empty hand. Detect the
        // identity edge before the ordinary fold; otherwise its early return spends the outgoing
        // slabs on a close animation and the exchange is never visible. No identity edge still
        // means the same normal close, with the owner's existing duration.
        // THE OWNER'S FAN IS COMING DOWN AND THERE ARE STILL SLABS TO FOLD (ModBuild 306). Their
        // CardFan.Close keeps the root visible and blends every card back into the centre stack
        // over FanCloseDuration before hiding; this is that, on the mirror.
        //
        // Deliberately NOT run while a swap or a leaving wave is in flight: those already animate
        // the very cards this would fold, and the owner's own Close does not run during an exchange
        // either. Nor with the duration at 0 — that is the owner's "vanish instantly", and honouring
        // it is the same 1:1 rule as honouring the animation.
        if (count == 0 && _cards.Count > 0 && _leaving.Count == 0 && _swapElapsed < 0f
            && _closeSeconds > 0f && _root.activeSelf)
        {
            if (_closeElapsed < 0f)
            {
                _closeElapsed = 0f;
                _openElapsed = -1f; // a reveal still in the air is superseded by the collapse
                VRLog.Info("Net", $"Remote hand fan [player {_owner.PlayerId}] COLLAPSING — " +
                                  $"{_cards.Count} card(s) folding into the centre stack over " +
                                  $"{_closeSeconds:F2} s, the owner's own [Cards] FanCloseDuration " +
                                  "off record 28 (id 156). Before ModBuild 306 this was one frame.");
            }
            _closeElapsed += Mathf.Max(dt, 0f);
            if (_closeElapsed < _closeSeconds)
            {
                // Keep posing and keep the FACES: a collapse whose slabs went blank half-way would
                // be a different animation from the owner's, not a cheaper one.
                PoseFan(holder, dt);
                LayoutCards(_cards.Count, dt);
                UpdateFaces(_cards.Count, _shownActor);
                return;
            }
            _closeElapsed = -1f; // finished — fall through and let the ordinary path tear it down
        }
        else if (_closeElapsed >= 0f)
        {
            // Re-opened, swapped or emptied mid-collapse: the collapse is abandoned exactly as
            // CardFan.Open abandons it, and the open animation takes over from here.
            _closeElapsed = -1f;
        }

        // AN EMPTY INCOMING HAND, now genuinely reachable: the edge above has run, so a switch INTO
        // an empty hand has already armed its wave and the gather plays out here on this client's own
        // clock. Once it has drained, the bail at the top of Tick sees count 0, no wave and no slabs,
        // and the fan hides silently.
        //
        // The Rebuild(0) is what the old `count == 0` early-return got for free from the Hide() it
        // has now replaced: when NO exchange was armed (the hand simply ran out — the last card was
        // played, no character switch), nothing downstream would ever clear the slabs still on
        // screen, because every teardown path below is gated on `count != _builtCount` and this
        // branch returns before it. Emptying the fan here and hiding on the NEXT tick is visually
        // identical to hiding now — the slabs are destroyed either way, in the same frame.
        if (count == 0)
        {
            TickSwap(dt);
            if (_cards.Count > 0)
                Rebuild(0);
            PoseFan(holder, dt);
            // THE DISPLAYED CHARACTER, NOT `null` (2026-09-07 report items 5a/5b). An empty arc is a
            // statement about the fan's LENGTH; it says nothing about whose fan it is. Handing null
            // down here made TrackFist's character-change reset fire — `!ReferenceEquals(actor,
            // _fistActor)` is true for every non-null previous actor — and that reset calls
            // ClearHandoff(). So the RECESS HAND-OFF, the only local fact that can name a card a
            // peer has just laid in one of their recesses, was destroyed by the owner's fan running
            // out of cards: exactly the frame after they lay the last picked card down. The face
            // that was about to be handed to RemoteControlBoard.SeatSlots was thrown away one tick
            // before it was asked for, and the recess fell to an anonymous BACK in the ACTION phase.
            // Nothing else about this call changes: count is 0, so the face loop below draws backs
            // either way, and the gate is asked per frame as before.
            UpdateFaces(0, _shownActor);
            return;
        }

        TickSwap(dt);

        // Rebuild the card slabs only when the count actually changes (cheap; the sizes/poses of
        // existing slabs are refreshed every frame below and auto-inherit AppliedScale via the
        // scaled holder, so a scale change needs no rebuild). EnsureRoot may additionally have
        // invalidated _builtCount because the built fan died under us (external destruction —
        // see the self-heal note there), which funnels through this same rebuild.
        if (count != _builtCount)
            Rebuild(count);

        if (!_root.activeSelf)
        {
            _root.SetActive(true);
            _poseInit = true; // snap on the frame we (re)appear so we don't ease in from a stale pose
            _openElapsed = 0f; // …but the CARDS fan out from the centre stack, like the local Open
        }

        PoseFan(holder, dt);
        LayoutCards(count, dt);
        UpdateFaces(count, shownActor);
    }

    // ------------------------------------------------------------------ front art (gated) --

    /// <summary>
    /// Per-frame anti-cheat gate + front rendering, with two DISJOINT sources for the face.
    /// IN A SCENARIO: when <see cref="RevealGate.ShowRoundCardFronts"/> is true for the remote actor
    /// (never during the secret selection phase), overlay each slab with a CLONE of that actor's real
    /// hand-card face (<see cref="RemoteCardArt"/>). IN THE MAP PHASE: when
    /// <see cref="RevealGate.ShowMapPhaseHandFronts"/> is true — which requires the ABSENCE of a
    /// scenario, so the two can never both be open — overlay each slab with the peer's LOADOUT card,
    /// identified by the map room (<see cref="ResolveMapFronts"/>). Otherwise show BACKS.
    /// Every game deref is guarded and fails safe to BACKS on any error — no front can leak.
    /// </summary>
    private void UpdateFaces(int count, CPlayerActor? actor)
    {
        bool showFronts = false;
        bool mapFronts = false;
        int frontCount = 0;
        // Which seat of the peer's map LOADOUT is in their fist this frame (-1 = none). Read on the
        // map branch below and used again for the census, so the number the log prints is the
        // number the arc was actually built with.
        int heldMapSeat = -1;
        // TRUE while the arc is open ONLY through the per-card burn exception — the population gate
        // said None and each slab must earn its own front. See the default branch of the switch
        // below, and PrintsFront, which is the only place it is honoured.
        bool publicCardsOnly = false;
        try
        {
            // The DISPLAYED character is resolved once per tick by the caller and handed in — see
            // the exchange edge in Tick for why the two consumers must be looking at the same
            // answer. The game's own reveal rule is applied here; both are null-safe and degrade to
            // no-front off-scenario.
            //
            // THE FAN FOLLOWS THE OWNER'S FOCUS (ModBuild 84). The card COUNT has always come off
            // the wire — it is the size of the fan that peer is physically holding up, which on
            // their machine is the hand of the character they are LOOKING at. Resolving the fronts
            // from their OWNED character therefore used to draw a mismatched pair: n slabs from
            // one character wearing faces from another. RemoteBoardFocus makes both halves name
            // the same character; when it cannot (no record, unresolvable, secret phase) it hands
            // back the owned character exactly as before.
            //
            // ONE CALL DECIDES BOTH "MAY WE?" AND "FROM WHERE?" — RevealGate.CardFaces. This
            // used to be two hand-written branches, `InScenario && ShowRoundCardFronts(actor)` and
            // `ShowMapPhaseHandFronts`, whose disjointness was an argument in a comment rather than
            // a property of the code. The argument was correct here and the SAME pair of branches
            // was wrong one surface over: RemoteHeldCardFace kept only the first of them, so in the
            // map room a peer's fan showed its fronts while the card in his hand showed only its
            // back (report item 5a). Branches that "happen to agree today" is how that happens, so
            // both surfaces now switch on this one call and the disjointness is the enum's rather
            // than a reader's.
            //
            // The scenario branch's InScenario term was never an anti-cheat term — it is a
            // CAPABILITY test for ResolveHandFronts, whose clone widget lifecycle depends on
            // scenario singletons — and that distinction now lives in RevealGate, stated once.
            //
            // ...AND THE FAN IS NOT ALWAYS THE HAND (2026-09-06, report item 7). While the game has
            // the owner stepping through a modal card pick it re-Shows their hand over another
            // PILE — the long rest's burn step fans the DISCARD pile, a recover-lost fans the LOST
            // one — and the arc the owner is physically holding up is then that pile. Extension
            // record 43 is the only way this client can know which: the pile is chosen by
            // CardsHandUI.Show's selectableCardType and read back off AbilityCardUI.IsSelectable,
            // both of them LOCAL UI state on the picking player's machine that no copy of the
            // replicated model reflects. Absent record == the HAND, which is what every sender
            // before ModBuild 459 means and what this surface has always resolved.
            byte fanList = _owner.FanSourceList;
            bool pickFan = NetProtocol.IsFanSourcePile(fanList);
            _censusList = fanList;
            // WHY there is no source, when there is none. CardFaces answers None for three different
            // states and the census has to tell them apart: this term is TRUE for exactly one of
            // them — a running scenario, a resolved character, and the secrecy predicate itself
            // saying no. The other two (no character to resolve against; neither a scenario nor a
            // map) leave it false and the line then says the gate was OPEN, which for item 7 was the
            // whole of the answer and the string could not say it.
            _censusGateShut = RevealGate.InScenario && actor != null
                              && !RevealGate.ShowRoundCardFronts(actor);
            if (fanList != _loggedFanList)
            {
                _loggedFanList = fanList;
                // HW-VERIFY: report item 7, the RECEIVER edge. Grep token: FAN SOURCE — the SAME
                // token the owner's "FAN SOURCE SENT" line carries, so one grep across both logs
                // settles the 1:1 question with no arithmetic. Change-gated on the list, so it is
                // one line per pick opening and one per pick closing, not a flood.
                //
                // WORKING = this line naming the DISCARD pile within a packet or two of the owner's,
                // followed by a PEER CARD FACE CENSUS whose hand-fan row reads N FRONT / 0 BACK.
                //
                // INERT = the owner's line names DISCARD and this one still says the HAND: the
                // record did not arrive (a sender predating ModBuild 459, or a dropped packet) and
                // nothing on this client can fix it.
                //
                // STILL BEYOND THE INSTRUMENT = both lines name DISCARD and the census still reads
                // BACKs with LENGTH BELT beside them. The two clients then disagree about the
                // CONTENTS of that pile, not about which pile it is; the belt's two counts are the
                // next reading and this record cannot move them.
                VRLog.Note("Net", $"FAN SOURCE [player {_owner.PlayerId}]: this peer's fan is "
                    + $"{FanListName(fanList)} (record 43). Their faces are resolved out of THIS "
                    + "client's own copy of that host-replicated list and refused unless it is "
                    + "exactly as long as the arc on the wire, so nothing here can print a face "
                    + "from one pile onto a card from another. NOT a secrecy term: RevealGate is "
                    + "asked the SAME phase question for a pick fan as for a hand fan — see "
                    + "RevealGate.PeerCardPopulation.PickFan, which also enumerates every flow that "
                    + "picks a card outside the selection phase and the face each one owes. Before "
                    + "this record a long rest's discard arc was resolved as the HAND, the lengths "
                    + "disagreed, and the length belt drew the whole fan as BACKS (report item 7).");
            }
            // THE POPULATION IS NAMED, AND IT CHANGES NO ANSWER. PickFan is NOT exempt from the
            // selection-phase carve-out (RevealGate.IsPublicPopulation answers false for it, the
            // same as for Selectable), so this asks the identical secrecy question either way; what
            // the member buys is that the census line below, and the next hardware round's grep,
            // can say WHICH arc was drawn. See RevealGate.PeerCardPopulation.PickFan for the whole
            // enumeration of choosing flows and the face each one owes.
            switch (RevealGate.CardFaces(pickFan
                                             ? RevealGate.PeerCardPopulation.PickFan
                                             : RevealGate.PeerCardPopulation.Selectable, actor))
            {
                case RevealGate.CardFaceSource.Scenario:
                    ResolveHandFronts(actor!, fanList);  // fills _handBuffer with the fanned list
                    showFronts = _handBuffer.Count > 0;
                    break;
                case RevealGate.CardFaceSource.MapLoadout:
                    // ─── THE MAP ARC IS THE LOADOUT MINUS THE FIST (2026-09-07) ─────────────────
                    // This was the ONE arc of the four with no length belt and no held-seat
                    // removal, and it is the only surface in this round that failed UNSAFELY. The
                    // chain, every link measurable at source: plucking a card out of the owner's
                    // map fan runs CardFan.Remove (CardsDriver.5.Interactions.cs, the unconditional
                    // `if (_fan.Contains(card)) _fan.Remove(card)`), the wire count is
                    // CardFan.Current.Count (NetAvatarDriver.cs), so `count` drops to n-1 — while
                    // MapRoomHand.TryResolvePeerLoadout hands back the peer's FULL n-card loadout
                    // and its Tier 0 (the only tier a current build takes) compared it to nothing
                    // at all. n-1 slabs were then filled positionally from an n-entry buffer:
                    // slab i showed loadout[i] while the owner's seat i held loadout[i+1], i.e.
                    // every face from the plucked seat rightwards was A DIFFERENT CARD, drawn with
                    // full confidence. A back reads as "not loaded yet"; a shifted front does not
                    // read as wrong at all, and the player acts on it.
                    //
                    // MEASURED, ModBuild 476, the CO-PLAYER's log — it is in the open and nobody
                    // had read it. One PEER CARD FACE CENSUS tick carries both halves in one line:
                    //   held card[p1/slot1] 1 FRONT ... MapLoadout — resolved map-room loadout
                    //                                   SEAT 7 OF 10 ... over 701 tick(s)
                    //   hand fan[p1]        9 FRONT / 0 BACK — RevealGate map-phase fronts
                    // A ten-card loadout, seat 7 in the owner's fist, NINE slabs on the arc, and
                    // every one of them showing a front. Slabs 0-6 were right; slabs 7 and 8 wore
                    // loadout[7] and loadout[8] while the owner's own arc held loadout[8] and
                    // loadout[9]. TWO CONFIDENTLY WRONG CARDS, standing for most of a ten-second
                    // census interval, with the row's own rule reading "map-phase fronts" — i.e.
                    // the instrument said the surface was working.
                    //
                    // AND EVERY EXISTING COMPENSATION WAS GUARDED OUT. The held-seat drop, the
                    // recess hand-off removal and the length belt below are all `!mapFronts` — and
                    // even unguarded the seat translation would have found nothing, because
                    // RemoteAvatar.HeldHandSeats narrows record 36 to HeldFaceListHand (or a
                    // record-43 pile) while a map card in a fist is sent as HeldFaceListMapLoadout
                    // (LocalRigSampler.NameHeldMapCard). The seat was NARROWED AWAY, not missing.
                    //
                    // NO NEW WIRE FIELD, and the fix is the same shape the hand, browse, item and
                    // active arcs already share: record 20 names the character, record 36 names
                    // which seat of THAT character's loadout is in the fist, both in the index
                    // space MapRoomHand.TryNameLocalLoadoutSeat writes and
                    // MapRoomHand.ResolveLoadout reads. So resolve the whole loadout, take the
                    // named seat out for the ARC, and belt what is left against the wire.
                    //
                    // A SEAT-AWARE FILL RATHER THAN A REFUSAL, deliberately: a refusal is safe and
                    // a correct arc is better, and the information to draw the correct one is
                    // already here. When it is NOT — an unnameable fist, two fists, a loadout this
                    // client resolves at a different length — the belt below still refuses and the
                    // fan falls back to backs, which is where this path started.
                    heldMapSeat = HeldMapLoadoutSeat();
                    ResolveMapFronts(count + (heldMapSeat >= 0 ? 1 : 0));
                    BuildMapArc(heldMapSeat);
                    mapFronts = _mapArc.Count > 0;
                    showFronts = mapFronts;
                    break;
                default:
                    ClearMapFronts();
                    // ─── THE BURN EXCEPTION IS A PROPERTY OF THE CARD, AND THIS GATE COULD NOT
                    //     SEE ONE (2026-09-07) ────────────────────────────────────────────────
                    // The call above is the TWO-ARGUMENT CardFaces — population and actor — which
                    // by construction cannot reach RevealGate.IsPubliclyRevealedCard, because it
                    // is handed no card. So this surface asked only the PHASE question, and during
                    // the game's secret selection window it answered None for every card in the
                    // arc without exception. Every card in a BURNT pile is by construction in the
                    // exact two lists that exception walks (CCharacterClass.LostAbilityCards /
                    // PermanentlyLostAbilityCards — a burn is committed into them before the burn
                    // artwork even starts), so a peer picking a card out of their burnt pile drew
                    // a fan of backs against the user's strongest ruling on any face:
                    //
                    //   "Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der
                    //    Vorderseite sichtbar sein. Es gibt keinen Grund warum sie nicht sichtbar
                    //    sein sollte."
                    //
                    // ROUTED THROUGH THE CARD-AWARE OVERLOAD, PER SLAB, AND IT ONLY WIDENS. The
                    // resolve below fills the same buffer the open path fills, from the same
                    // expression; what is different is that every slab must then pass
                    // CardFaces(population, actor, cardInstanceId) on its own — see PrintsFront.
                    // A slab whose card is not already public keeps the back the shut gate gave
                    // it, so the secret window is exactly as closed as it was for every card the
                    // ruling does not name.
                    //
                    // ONLY A PILE FAN PAYS FOR IT. IsFanSourcePile is a capability test and not a
                    // secrecy one: a HAND-pile widget cannot be in ActivatedCards or either burnt
                    // list — the game moves a card's widget out of CardPileType.Hand in the same
                    // step it commits the model — so resolving the hand here would walk three
                    // lists per slab per frame for the whole selection phase to answer false every
                    // time. The one state where a hand widget and a burnt model coexist is this
                    // client's copy of the peer's model lagging a choreographer turn, and that is
                    // the length belt's case, not this one.
                    if (RevealGate.InScenario && actor != null && pickFan)
                    {
                        ResolveHandFronts(actor, fanList);
                        publicCardsOnly = _handBuffer.Count > 0;
                        showFronts = publicCardsOnly;
                    }
                    break;
            }
        }
        catch (System.Exception ex)
        {
            // ANY failure → no fronts, backs only (fail-safe = no cheat).
            showFronts = false;
            mapFronts = false;
            _censusGateShut = false;   // a THROWN gate is not a shut gate, and the line must not say it was
            _handBuffer.Clear();
            ClearMapFronts();
            VRLog.Warn("Net", $"RemoteHandFan front gate errored ({ex.Message}) — showing backs.");
        }

        // ─── THE OWNER'S OWN LEFT-TO-RIGHT ORDER (record 44, report item 2 of 2026-09-06) ──────
        // "Ich habe ganz rechts eine andere Karte gesehen als der Spieler selber. Das darf niemals
        // passieren." _handBuffer is this client's DERIVED order — cardsUI walked under
        // HandFanMember — and until this record that was simply asserted to be the owner's arc
        // order too. It is not: their arc is CardsDriver._fanOrder, the player's own drag-reorder,
        // which is session-local and which no rule here can reproduce. Measured in the 2026-09-06
        // session, 38 of the co-player's 54 non-empty 'Fan order [scenario hand]' readings and 21
        // of the host's 33 say NOT SORTED, i.e. for most of that session each fan was drawn to
        // everyone else in an order its owner was not looking at.
        //
        // IT FAILS CLOSED, and that is the whole reason this is safe to apply to a face list. A
        // missing order costs a cosmetic divergence; a WRONG order applied is a lie about which
        // card is where and would shift every face after the first mistake — the exact failure the
        // length belt below exists to prevent. So it is applied only when it is an EXACT
        // permutation of the seats this client actually holds (NetProtocol.ValidateFanArcOrder:
        // right count, every index in range, no index twice) AND the two lists are the same length.
        // A FLAT player, a peer predating ModBuild 462, a peer whose arc already matches, a model a
        // beat behind — all of them land in the else and keep the order this build's predecessor
        // drew.
        ApplyFanArcOrder(count);

        // A LENGTH DISAGREEMENT MEANS THE TWO SIDES ARE NOT LOOKING AT THE SAME HAND — SHOW BACKS
        // (ModBuild 351, hardware MP report item 10: "Die gerade verbrannte Karte ist beim Test auf
        // dem Handfaecher zu sehen direkt nachdem der Mitspieler sie verbrannt hat").
        //
        // The slab COUNT below is the owner's, off the wire and timely. The FACES are this client's
        // OWN model, and on an observer that model lags a whole choreographer turn. The loop then
        // zips the two POSITIONALLY with nothing but a bounds check, so once the owner burns a card
        // mid-hand every face after it is drawn one place out AND the burned card keeps being drawn.
        // A back is wrong in a way the player can read as "not loaded yet"; a SHIFTED FRONT is wrong
        // in a way he cannot read at all, and he would act on it.
        //
        // WHAT CHANGED, AND WHY THIS LINE IS NOW A BELT RATHER THAN THE ONLY THING HOLDING IT UP.
        // The note that stood here said "this is a stopgap, not the fix: the fix is for the hand
        // fan's MEMBERSHIP to travel the way the pile counts already do, after which the zip can be
        // by identity and this can go." That framing was wrong about the cost and wrong about the
        // defect. The membership does not have to TRAVEL — every term of it is host-replicated model
        // state this client already holds in full. What was actually wrong is that the two sides
        // computed it with two different expressions, and the pair had drifted: the owner's arc drops
        // the LONG REST placeholder, this buffer did not, and that placeholder sits in cardsUI with
        // CardPileType.Hand (CardsHandUI.cs:1309). So the counts differed by one PERMANENTLY, this
        // line refused every front for the entire session, and report item 5b reads as "no fronts in
        // the fan" with the gate wide open. Two 100 MB logs contain no "FRONTS - content=HAND" line
        // at all while every sibling surface on the same RevealGate opened normally — which is the
        // signature of shut arithmetic, not a shut gate.
        //
        // Both sides now call ONE expression (CardsGameApi.HandFanMember), so this compares two
        // computations of the same definition instead of two definitions. It stays, because what it
        // is really testing is MODEL LAG — this client's copy of the peer's hand being a
        // choreographer turn behind theirs — and no shared filter can remove that. It should now be
        // a transient around a burn or a play, and if it is not, the log line below says so.
        // Latched for the BACKS log line below, which otherwise cannot tell the player WHICH of the
        // three reasons shut the fan — the very ambiguity that let item 5b read as a reveal-gate
        // problem for a whole round of hardware testing.
        //
        // ...AND THE CARD IN THEIR FIST IS NOT A DISAGREEMENT (2026-09-05, report item 2c): "Wurde
        // eine Karte aus dem Faecher wo ich alles sehen kann in die Hand genommen wurde nicht nur
        // die Karte in der remote Hand mit der Rueckseite angezeigt sondern ploetzlich auch der
        // ganze Faecher wieder nur Rueckseiten." That co-occurrence names its own cause: ONE term
        // flipped for the fan and the held card together, and the term is this belt.
        //
        // Plucking a card out of the owner's fan runs CardFan.Remove (Cards/Driver/
        // CardsDriver.5.Interactions.cs:274), so the wire count — CardFan.Current.Count, sampled at
        // NetAvatarDriver.cs:1035 — drops by one the instant the card leaves the arc. The MODEL this
        // client resolves faces from does NOT: the card has not been played, so it is still a hand
        // card by CardsGameApi.HandFanMember and still in _handBuffer. N against N-1, every frame the
        // peer holds a card up to read it, for as long as they hold it. The belt then did exactly
        // what it is for and refused the whole fan — correctly, on a premise that was wrong.
        //
        // THE MISSING SEAT IS ALREADY ON THE WIRE. Record 36 names WHICH seat of that same list is in
        // the peer's fist (it exists to draw that card's own front), in the same index space, built
        // by the same shared membership expression on both machines. So take those seats out: what
        // is left is the arc the owner is actually holding, in the owner's own order, at the owner's
        // own length. No new wire field, and the belt below still has to agree afterwards — if it
        // does not, nothing is dropped and the fan falls back to backs exactly as before.
        // ─── THE FIST, REMEMBERED ONE FRAME LONGER THAN THE WIRE REMEMBERS IT ──────────────────
        // Read BEFORE the removals below, while _handBuffer is still this client's whole model
        // list and record 36's seat still indexes it. TrackFist is what arms the RECESS HAND-OFF
        // the moment the card leaves their fist; see its own note for why that is the only free
        // identity a freshly-seated round card has.
        TrackFist(showFronts && !mapFronts, actor, count);

        // …AND RE-AIM A FLIGHT THAT IS ALREADY IN THE AIR. Same staged window, same one source of
        // truth: a glide armed three packets ago is still keyed on its hand-list seat, and the arc
        // seat that seat maps to is re-derived here. See RetargetReturnGlide for why it re-aims
        // instead of abandoning.
        RetargetReturnGlide();

        // ─── THE OWNER'S OWN STATEMENT OF WHAT IS IN THEIR ARC WINS OVER EVERY COUNT BELOW ──────
        // Record 44 names, per arc seat, the derived index it holds — so a derived card the order
        // does not name is one the arc does not carry, and dropping it is a NAME and not a guess.
        // That subsumes both count-based removals below: the held seat (record 36) and the recess
        // hand-off are two ways of guessing WHICH card left the arc, and this is the owner saying
        // so. They stay for the peers that state no order — a FLAT player, a peer predating
        // ModBuild 462, or an arc whose order the sender legitimately omits.
        CommitFanArcOrder();
        bool arcNamed = _orderArcValid;

        // The map arc does its own held-seat removal on the branch above (record 36's map-loadout
        // seat, which HeldHandSeats below cannot see because it narrows to the HAND list). Seeded
        // here so the census line's fist column is the number the arc was really built with rather
        // than a zero that would make the belt equation look unbalanced.
        int heldSeatCount = mapFronts && heldMapSeat >= 0 ? 1 : 0;
        if (!arcNamed && showFronts && !mapFronts && _handBuffer.Count != count)
        {
            heldSeatCount = _owner.HeldHandSeats(out int heldSeatA, out int heldSeatB);
            // TWO POSE SLOTS CANNOT NAME ONE SEAT, but this is a value off the wire and a receiver
            // never assumes a sender is well-formed: a duplicate would remove two entries for one
            // held card and shift every face after it, which is exactly the failure the belt below
            // exists to prevent. Collapse it to one and let the length check judge the result.
            if (heldSeatCount == 2 && heldSeatA == heldSeatB)
            {
                heldSeatB = -1;
                heldSeatCount = 1;
            }
            if (heldSeatCount > 0 && _handBuffer.Count - heldSeatCount == count)
            {
                // Descending, so removing the first index cannot move the second.
                if (heldSeatB > heldSeatA)
                    (heldSeatA, heldSeatB) = (heldSeatB, heldSeatA);
                if (heldSeatA >= 0 && heldSeatA < _handBuffer.Count)
                    _handBuffer.RemoveAt(heldSeatA);
                if (heldSeatB >= 0 && heldSeatB < _handBuffer.Count)
                    _handBuffer.RemoveAt(heldSeatB);
                if (!_loggedHeldSeatDrop)
                {
                    _loggedHeldSeatDrop = true;
                    // HW-VERIFY: report item 2c. This is the line that says the fan did NOT fall to
                    // backs because a card was plucked out of it. Latched once per fan instance --
                    // it is a per-frame condition for as long as the card is up, so a per-event line
                    // would be a flood; the PEER CARD FACE CENSUS carries the standing picture.
                    VRLog.Note("Net", $"Remote hand fan [player {_owner.PlayerId}]: HELD SEAT "
                        + $"DROPPED — the owner has {heldSeatCount} card(s) of this hand in their "
                        + "fist, so their fan reports one slab fewer than this client resolves hand "
                        + "cards. The seats record 36 already names are removed from the resolved "
                        + "list, which makes the two lengths agree and keeps slab i a name for card "
                        + "i. Before this build that difference tripped the LENGTH BELT and every "
                        + "face in the fan went to a BACK for as long as the peer held a card up — "
                        + "which is exactly report item 2c, and why the held card and the whole fan "
                        + "lost their fronts in the same instant. No new wire field: the seat is the "
                        + "one the held-card front is already drawn from.");
                }
            }
        }

        // ─── …AND NEITHER IS THE CARD THEY JUST LAID IN A RECESS ───────────────────────────────
        // The same argument as the held seat above, one instant later. When the owner drops the
        // card into a round recess their fan reports one slab fewer AT ONCE (CardFan.Remove), and
        // their own model moves it out of the hand into RoundAbilityCards AT ONCE — but this
        // client's copy of that model does not, because the move travels as a ScenarioRuleClient
        // EMOVEABILITYCARDMESSAGE and arrives a beat later. N against N-1 again, for the length of
        // the placement animation, and the belt correctly refused the whole fan for it.
        //
        // The card is REMOVED BY REFERENCE, never by index: HandoffCard is the very widget's card
        // this fan resolved while it was still in their fist, so this is an exact removal of a
        // known member — not a positional guess, which is the thing the belt exists to forbid. If
        // the card is not in the buffer the model has already caught up and nothing is dropped, so
        // the belt still has the last word exactly as before.
        if (!arcNamed && showFronts && !mapFronts && _handBuffer.Count == count + 1
            && HandoffCard != null)
        {
            for (int i = 0; i < _handBuffer.Count; i++)
            {
                AbilityCardUI w = _handBuffer[i];
                if (w != null && ReferenceEquals(w.AbilityCard, HandoffCard))
                {
                    _handBuffer.RemoveAt(i);
                    break;
                }
            }
        }

        // ─── AND THE MAP ARC IS BELTED BY THE SAME LINE, WHICH IT NEVER WAS BEFORE ──────────────
        // `!mapFronts` used to stand here, so the one arc that could not compensate for a plucked
        // card was also the one arc that was never checked. It is the same test on the same two
        // numbers — this client's model list against the owner's slab count — and it fails the same
        // way: to BACKS. The map buffer stays at its full loadout length for MapLoadoutSeat (record
        // 36's seat is named against the whole loadout), so the list compared here is the ARC
        // projection built above, not the buffer.
        int modelCount = mapFronts ? _mapArc.Count : _handBuffer.Count;
        _countBelt = showFronts && modelCount != count
            ? (Model: modelCount, Wire: count)
            : ((int Model, int Wire)?)null;
        if (_countBelt != null)
            showFronts = false;

        // Which buffer this frame's faces come from. Latched rather than re-derived by each reader,
        // because two readers disagreeing about which buffer is live is the ModBuild 84 defect one
        // surface over (n slabs from one character wearing another's faces).
        _mapFronts = mapFronts;

        // A CLOSED GATE EMPTIES THE WIDGET BUFFER, so no stale entry from the last open frame can
        // be printed after the phase turned secret. (This used to also latch the borrow permission
        // — report 2 of 2026-09-07 removed the borrow, so the buffer clear is all that is left.)
        if (!showFronts)
            _handBuffer.Clear();

        while (_mapPrinted.Count < _faces.Count)
            _mapPrinted.Add(-1);

        for (int i = 0; i < _faces.Count; i++)
        {
            RemoteCardArt face = _faces[i];
            // Only slab indices that both (a) are within the built fan and (b) map to a resolved hand
            // widget / loadout model with a real card get a front; everything else stays a back.
            if (showFronts && i < count)
            {
                if (mapFronts)
                {
                    // _mapArc, NOT _mapBuffer: the buffer is the peer's whole loadout (record 36's
                    // seat is an index into that) and the ARC is what the owner is holding up — the
                    // same list with the card in their fist taken out. Zipping the buffer against
                    // the slabs is precisely the shift this build fixed.
                    if (i < _mapArc.Count && PrintMapFace(i, face, _mapArc[i]))
                    {
                        frontCount++;
                        // USER ITEM 10: a front is up, so this body stops wearing the card BACK on
                        // its FRONT fan — the banner and outer frame the print does not paint now
                        // read as the owner's own card edge instead of the back's gold lattice.
                        SetFrontFace(i, showsBack: false);
                        continue;
                    }
                }
                else if (i < _handBuffer.Count)
                {
                    AbilityCardUI widget = _handBuffer[i];
                    FullAbilityCard? full = widget != null ? widget.fullAbilityCard : null;
                    if (full != null && PrintsFront(publicCardsOnly, actor, widget)
                        && face.ShowFront(full))
                    {
                        frontCount++;
                        SetFrontFace(i, showsBack: false);   // …and the same on the hand-widget arm
                        continue;
                    }
                }
            }
            face.HideFront();
            // …AND THE BACK ARM, WHICH IS THE HALF THAT MUST NOT BE GOT BACKWARDS. A slab drawing a
            // card back keeps the back on BOTH submeshes; an edge ring around a card back is a new
            // defect nobody has reported. This is the fall-through of the one front/back decision
            // above, so there is no second rule.
            SetFrontFace(i, showsBack: true);
            if (i < _mapPrinted.Count)
                _mapPrinted[i] = -1;
        }

        // …and, on the SAME resolved widgets, the game's own card plume (wire id 236). It rides
        // this pass rather than a pass of its own because the source widget the predicate needs is
        // exactly the one just used for the face: a second resolve would be a second answer, which
        // is the ModBuild 84 mismatch one surface over.
        TickMirroredPlumes(count, showFronts, mapFronts);

        // …AND THE LOOK THE CARD ITSELF WEARS, on the SAME resolved widgets and for the same reason
        // the plume rides this pass. It is a separate call and not a branch of the plume because
        // the two answer to different permissions: the plume is the owner's [Cards]
        // GameCardParticles bit, the wash is the card. See TickUsedCardFx.
        TickUsedCardFx(count, showFronts, mapFronts);

        // THE LEAVING HALF IS RE-GATED EVERY FRAME TOO — ON ITS OWN CHARACTER'S VERDICT, NOT THIS
        // ONE'S. Slabs on their way out of a character exchange keep the faces the gate had already
        // granted them (that is the whole point of the wipe being legible), but they are wearing the
        // OUTGOING character's cards, and RevealGate.ShowRoundCardFronts is PER ACTOR: it folds in
        // that actor's own IsUnderMyControl. Reusing `showFronts` — computed for the ARRIVING
        // character — would therefore have left a teammate's fronts on screen for the ~0.4 s of the
        // wipe in the one combination that matters (they are not under my control, the character I
        // just switched to is, and the phase turns secret mid-wipe). An animation must never be a
        // window in which a rule is briefly not enforced, and it must be THE rule.
        if (_leavingFaces.Count > 0)
        {
            bool leavingFronts = false;
            try
            {
                // IN THE MAP PHASE THERE IS NO PER-ACTOR TERM TO GET WRONG, which is the whole reason
                // this clause is allowed to be so much simpler than the one below it. The map rule
                // (RevealGate.ShowMapPhaseHandFronts) is a statement about the PHASE — no actor, no
                // IsUnderMyControl, nothing that can differ between the outgoing character and the
                // arriving one — so the leaving wave is gated on exactly the same boolean the
                // arriving half was, and a wipe cannot become a window in which a rule is not
                // enforced. Without this the outgoing slabs would flip to BACKS the instant a
                // character swap started, which is the divergence-from-the-scenario the map room's
                // 1:1 ruling exists to prevent.
                leavingFronts = RevealGate.ShowMapPhaseHandFronts
                                || (_leavingActor != null && RevealGate.InScenario
                                    && RevealGate.ShowRoundCardFronts(_leavingActor));
            }
            catch
            {
                leavingFronts = false; // any failure -> backs, like every other gate here
            }
            if (!leavingFronts)
            {
                for (int i = 0; i < _leavingFaces.Count; i++)
                {
                    _leavingFaces[i]?.HideFront();
                    // …AND THE BODY, WHICH THE INDEX HELPER CANNOT REACH: a leaving slab left
                    // _cards carrying whatever front face it had, so a gate that shuts mid-wipe
                    // would otherwise leave an edge ring around a bare card back for the ~0.4 s of
                    // the exchange. Called directly rather than through SetFrontFace because these
                    // slabs are in no index space of _cards; the wave is bounded by the wipe and
                    // SetBodyFrontFace is idempotent against the authored material.
                    if (i < _leaving.Count && _leaving[i] != null)
                        CardMesh.SetBodyFrontFace(_leaving[i].transform, showsBack: true);
                }
            }
        }

        // THE STANDING PICTURE, every frame, for the per-population census — beside (never instead
        // of) the change-gated line below. See PeerCardFaceCensus: a fan that is UNIFORMLY on backs
        // never crosses the transition that line is gated on and therefore says nothing at all,
        // which is how two 100 MB logs came back holding 31 lines about a defect the user says was
        // constant.
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.HandFan, _owner.PlayerId,
            frontCount, Mathf.Max(count - frontCount, 0),
            _countBelt != null
                // THE THIRD NUMBER IS THE DENOMINATOR THIS ROW WAS MISSING (report item 6, second
                // half). Model-vs-wire alone cannot say WHY they differ, and for a whole session it
                // read "8 vs 7" with no way to tell "the peer is holding one up" from "the peer has
                // laid one on their board" from "one of the two lists is simply wrong". The seated
                // count and the fist count are exactly the two legitimate reasons the arc is
                // shorter, so model == wire + fist + seated is the equation that should hold, and
                // a row where it does NOT is the defect. READ IT LIKE THIS: model - wire - fist -
                // seated == 0 with backs still showing means the belt is not the blocker and the
                // rule beside it is lying; a non-zero remainder names how many cards left that fan
                // for a reason this client cannot see at all.
                ? $"LENGTH BELT: {_countBelt.Value.Model} model card(s) vs {_countBelt.Value.Wire} "
                  + $"slab(s) on the wire, with {heldSeatCount} in their fist and "
                  + $"{_owner.SeatedHandCardExcess} hand card(s) lying in their recesses — "
                  + $"remainder {_countBelt.Value.Model - _countBelt.Value.Wire - heldSeatCount
                                 - _owner.SeatedHandCardExcess} "
                  + (mapFronts
                        ? "(map-room loadout arc — the model list is the peer's replicated loadout "
                          + "minus the seat record 36 names, so a standing remainder means the two "
                          + "clients disagree about that character's loadout, not about the fist)"
                        : $"({FanListName(_censusList)})")
                // ONE STRING USED TO CARRY TWO CAUSES, and it is the string this whole item was read
                // through: "RevealGate.CardFaces(Selectable) named no source, OR no widget resolved"
                // cannot tell a SHUT GATE (the game's secret window, which is correct and expected)
                // from an OPEN GATE with nothing to resolve (a defect, every time). A reader
                // counting BACKs against that rule is measuring two populations with one number —
                // and for report item 7 the honest answer was "the gate was open", which the line
                // was incapable of saying. They are two branches now, and the census line quotes
                // whichever one actually decided.
                : !showFronts
                    ? (_censusGateShut
                        ? "RevealGate SHUT — the game's own secret SelectAbilityCardsOrLongRest "
                          + $"window for a remote character ({FanListName(_censusList)})"
                        : "RevealGate OPEN but NO widget resolved on this client — the gate is not "
                          + $"the blocker here ({FanListName(_censusList)})")
                    : publicCardsOnly
                        ? "RevealGate SHUT by phase, arc opened by the PER-CARD burn exception "
                          + $"(RevealGate.IsPubliclyRevealedCard) — {FanListName(_censusList)}. "
                          + "Every front on this row is a card already public to everybody; a slab "
                          + "that did not earn one kept its back."
                    : mapFronts
                        ? "RevealGate map-phase fronts (peer's replicated map loadout)"
                        // WHICH FACT MADE THE TWO LISTS AGREE, named rather than left to be
                        // inferred. "arc order (record 44)" is the owner's own statement of the
                        // arc's membership AND order — the term added for report item 4 — and it is
                        // what keeps a fan open while a card is in their fist or lying in one of
                        // their recesses. "held seat(s) dropped" is the older, count-based fallback
                        // for a peer that states no order. Seeing the second one where the first is
                        // expected says record 44 was refused, and ARC ORDER NOT APPLIED says why.
                        : arcNamed
                            ? $"RevealGate scenario fronts ({FanListName(_censusList)}), arc order "
                              + "(record 44) named this arc's membership"
                            : heldSeatCount > 0
                                ? $"RevealGate scenario fronts ({FanListName(_censusList)}), "
                                  + $"{heldSeatCount} held seat(s) dropped"
                                : $"RevealGate scenario fronts ({FanListName(_censusList)})");

        ReportSeatStackIfChanged(count, frontCount, heldSeatCount);
        ReportArcMembershipIfChanged(count);

        // Log exactly once per backs↔fronts transition — counts + gate state only, never identities.
        // The line NAMES the predicate on purpose: the same sentence appears on every other remote
        // card surface ("Remote pile browse fan faces", "Remote item fan faces", the board's
        // "Remote board content … fronts="), so one hardware log proves the phase rule across all of
        // them at once and a surface that disagrees is visible without a screenshot.
        bool nowFronts = frontCount > 0;
        // …and ALSO on a change of the map room's VERDICT, whether or not it resolved. A fan that
        // stays BACKS never crosses the transition above, so without this term the one case the next
        // hardware round has to be able to read — "the peer is holding a hand we could not name, and
        // here is why" — would produce no line at all. That is exactly how report 4 arrived with two
        // 70 MB logs containing nothing about it. Gated on count > 0 so raising and lowering an empty
        // hand cannot chatter.
        bool mapNews = RevealGate.InMapPhase && count > 0 && _mapVerdict != _loggedMapVerdict;
        if (nowFronts != _frontsShown || mapNews)
        {
            _frontsShown = nowFronts;
            _loggedMapVerdict = RevealGate.InMapPhase ? _mapVerdict : string.Empty;
            if (nowFronts && mapFronts)
            {
                VRLog.Info("Net", $"Remote hand fan faces [player {_owner.PlayerId}]: FRONTS — "
                    + $"content=MAP LOADOUT, {frontCount} card(s), gate: "
                    + "RevealGate.ShowMapPhaseHandFronts=true (no scenario is running, so nothing "
                    + "secret is being decided — see RevealGate's map-phase block). Hand identified "
                    + $"by the map room: {_mapVerdict}. No card identity crossed the wire: the only "
                    + "wire input is the peer's HandCardCount byte, and the faces are borrowed from "
                    + "this client's OWN ObjectPool by card id.");
            }
            else if (nowFronts)
            {
                VRLog.Info("Net", $"Remote hand fan faces [player {_owner.PlayerId}]: FRONTS — " +
                    $"content={FanListName(_censusList)}, {frontCount} card(s), gate: " +
                    "RevealGate.ShowRoundCardFronts(actor)=true.");
            }
            else
            {
                if (_countBelt != null)
                {
                    // HW-VERIFY: this line decides report item 5b. The reveal gate was OPEN and the
                    // fronts were refused by ARITHMETIC — so it must name the two numbers, at a tier
                    // a shipped log prints, or the next round measures the gate again and finds
                    // nothing. A burn or a play may flash it for a second while this client's copy
                    // of the peer's hand catches up. A line that STANDS is the real defect: the two
                    // sides have drifted apart on what counts as a hand card again.
                    VRLog.Note("Net", $"Remote hand fan faces [player {_owner.PlayerId}]: BACKS — "
                        + $"content={FanListName(_censusList)}, and the reveal gate was OPEN. The "
                        + $"fronts were refused by the LENGTH BELT: this client resolves "
                        + $"{_countBelt.Value.Model} card(s) in that list for that character while "
                        + $"the owner's own fan reports {_countBelt.Value.Wire} slab(s) on the wire. "
                        + "Slab i is only a name for card i while those two agree, so a front here "
                        + "would be the wrong card's face — a back is the safe direction. Both sides "
                        + "compute membership from one shared expression per list "
                        + "(CardsGameApi.HandFanMember for the hand, CardsGameApi.GetPileArcWidgets "
                        + "for a pick fan's pile), so a BRIEF disagreement around a burn or a played "
                        + "card is this client's copy of the peer's model lagging a choreographer "
                        + "turn and will clear itself. A disagreement that STANDS while the content "
                        + "reads THE HAND means the two sides have drifted apart on what counts as a "
                        + "hand card; one that stands while it reads a PICK FAN means they disagree "
                        + "about that pile's contents, and record 43 has already done all it can — "
                        + "it names the list, not what is in it.");
                }
                else
                {
                    // TWO CAUSES, TWO SENTENCES. This line used to say "gate=false ... OR no
                    // widget resolved", which is the same ambiguity the census row carried and the
                    // one report item 7 was read through: a SHUT gate is the game's own secret
                    // window and is correct, an OPEN gate with nothing resolved is a defect every
                    // time, and one string cannot be evidence for either.
                    VRLog.Info("Net", $"Remote hand fan faces [player {_owner.PlayerId}]: BACKS — "
                        + $"content={FanListName(_censusList)}, "
                        + (_censusGateShut
                            ? "gate SHUT: RevealGate.ShowRoundCardFronts(actor)=false — the game's "
                              + "own secret SelectAbilityCardsOrLongRest window for a remote "
                              + "character, which is the one phase in which this is correct."
                            : "the gate was OPEN and NO widget resolved on this client, so the gate "
                              + "is NOT the blocker here.")
                        + (RevealGate.InScenario
                            ? string.Empty
                            : " OFF-SCENARIO, so the MAP-PHASE path is the one that answered: "
                              + _mapVerdict + "."));
                }
            }
        }
    }

    // ------------------------------------------------------------------ map-phase fronts --

    /// <summary>
    /// Resolve (and cache) the map-phase loadout this peer's fan is holding into
    /// <see cref="_mapBuffer"/>. The identification itself belongs to the map room — see
    /// <c>MapRoomHand.TryResolvePeerLoadout</c>, which carries the tiers, their certainty and the
    /// belt each one applies. Cached because that walk is O(party) and this is called every frame:
    /// it re-runs when the peer's LOADOUT SIZE or CHARACTER KEY changes, including equal-sized
    /// hands, and otherwise on <see cref="MapResolveInterval"/> to pick up same-character edits.
    ///
    /// <para><paramref name="loadoutSize"/> IS THE WHOLE LOADOUT AND NOT THE ARC (2026-09-07).
    /// <see cref="_mapBuffer"/> is the peer's full loadout in initiative order — it has to be,
    /// because <see cref="MapLoadoutSeat"/> indexes record 36's seat straight into it and that seat
    /// is named on the sender against the full <c>_loadout</c>
    /// (<c>MapRoomHand.TryNameLocalLoadoutSeat</c>). The wire's slab count is that number MINUS the
    /// cards in the owner's fist, so both callers hand the full length: the fan adds its held seats
    /// back on, the held card passes record 36's own length byte. Feeding the raw arc count instead
    /// would make the map room refuse for as long as a card was up.</para>
    /// </summary>
    private void ResolveMapFronts(int loadoutSize)
    {
        RemoteMapRoom.TryGetPeerFanCharacterKey(_owner.PlayerId, out uint characterKey);
        if (loadoutSize == _mapResolvedForCount && characterKey == _mapResolvedForCharacterKey
            && Time.unscaledTime < _nextMapResolveAt)
            return;
        bool characterChanged = characterKey != _mapResolvedForCharacterKey;
        _mapResolvedForCount = loadoutSize;
        _mapResolvedForCharacterKey = characterKey;
        _nextMapResolveAt = Time.unscaledTime + MapResolveInterval;

        int before = _mapBuffer.Count;
        int firstBefore = before > 0 && _mapBuffer[0] != null ? _mapBuffer[0].ID : 0;
        // The peer's own statement of WHICH character their fan is showing, when their build sends
        // one (extension record 20, ModBuild 226). 0 from an older peer, and then the resolver falls
        // back to deducing the owner from the hand size exactly as it did before the field existed.
        WorldUI.MapRoom.MapRoomHand.TryResolvePeerLoadout(
            _owner.PlayerId, loadoutSize, characterKey, _mapBuffer, out _mapVerdict);
        int firstAfter = _mapBuffer.Count > 0 && _mapBuffer[0] != null ? _mapBuffer[0].ID : 0;

        // A DIFFERENT HAND MUST NOT INHERIT THE OLD HAND'S PRINTS. The per-slab latch below is what
        // keeps the pooled borrow off the per-frame path, so it has to be invalidated whenever the
        // resolved set can have moved under it — the cheap, always-safe test is "the size or the
        // leading card changed", and a false positive costs one re-print pass.
        if (characterChanged || _mapBuffer.Count != before || firstAfter != firstBefore)
        {
            for (int i = 0; i < _mapPrinted.Count; i++)
                _mapPrinted[i] = -1;
        }
    }

    /// <summary>Drop the map-phase resolution (leaving the slabs to fall back to backs) — used on
    /// every frame that is not a map phase and on any error, so a resolution can never survive into
    /// a scenario where the scenario gate is the only one allowed to speak.</summary>
    private void ClearMapFronts()
    {
        // Idempotent AND cheap: this is called on every frame of every scenario, for every peer, so
        // the steady state must be one integer compare and nothing else.
        if (_mapResolvedForCount < 0 && _mapBuffer.Count == 0 && _mapArc.Count == 0)
            return;
        _mapBuffer.Clear();
        _mapArc.Clear();
        _mapResolvedForCount = -1;
        _mapResolvedForCharacterKey = 0;
        _mapVerdict = "not in the map phase";
        for (int i = 0; i < _mapPrinted.Count; i++)
            _mapPrinted[i] = -1;
    }

    /// <summary>
    /// Print <paramref name="card"/>'s real face on slab <paramref name="index"/> from the game's own
    /// pool, at most once per (slab, card). Returns true iff a front is up on that slab.
    ///
    /// <para>THE LATCH IS LOAD-BEARING, not an optimisation. <c>RemoteCardArt</c> dedups on the
    /// SOURCE widget's instance id, and the map path's source is a widget borrowed from and returned
    /// to the pool inside a single call — a fresh instance every time — so the dedup can never hit
    /// and an unlatched call would spawn, configure, clone and recycle a card widget per slab per
    /// frame, for every peer. Exactly the cost <c>RemoteAbilityCardSource</c>'s own COST NOTE warns
    /// about and the reason <c>MapRoomHand.PrintPendingFaces</c> carries the same latch locally. A
    /// slot the pool refuses stays at -1 and is retried on the next frame, which is the fail-safe:
    /// a card BACK, never a blank quad.</para>
    /// </summary>
    private bool PrintMapFace(int index, RemoteCardArt face, CAbilityCard? card)
    {
        if (card == null)
            return false;
        if (index < _mapPrinted.Count && _mapPrinted[index] == card.ID)
        {
            // The steady-state upkeep the scenario path gets from ShowFront's dedup arm: the clone's
            // header art arrives async, so the mip bake has to keep rescanning.
            face.MaintainMipBake();
            return true;
        }
        if (RemoteAbilityCardSource.ShowFullFace(face, null, card) == RemoteAbilityCardSource.FacePath.None)
            return false;
        if (index < _mapPrinted.Count)
            _mapPrinted[index] = card.ID;
        return true;
    }

    // ═══ THE FIST'S HAND-OFF: WHERE THE CARD WENT, AND WHAT IT DID ON THE WAY ═══════════════════
    //
    // ONE EDGE, TWO 1:1 DEFECTS (hardware round 2026-09-05, user items 2 and 5). Both begin at the
    // same instant — the frame the owner opens their fingers — and both are the receiver watching
    // a card it can no longer name:
    //
    //   ITEM 5, "Bei der Animation bei denen die Karten in die Faecher hinein sliden, sehe ich beim
    //   remote board Karten waehrend der Animation mit der Rueckseite statt der Vorderseite wie es
    //   der jeweilige Spieler sieht." The card goes into a ROUND RECESS. The owner's occupancy
    //   nibble says so at once (LocalBoardSlots reads the recess's live child), and their own model
    //   moves the card out of the hand into RoundAbilityCards at once — but THIS client's copy of
    //   that model does not, because the move travels as a ScenarioRuleClient
    //   EMOVEABILITYCARDMESSAGE and lands a beat later. RemoteControlBoard.SeatSlots then has 0
    //   model cards against 1 occupied recess, its compaction belt correctly refuses to zip two
    //   lists of different lengths, and the recess draws an ANONYMOUS BACK for the length of the
    //   arrival animation. THE LOG SAYS EXACTLY THIS, AND IT IS WHY THE FIX IS HERE RATHER THAN IN
    //   THE BELT: of the 21 'ROUND SLOT COMPACTION REFUSED' lines in the co-player's ModBuild 448
    //   log, 13 fire 2-5 lines after a 'Board slot occupancy RECEIVED' line, every one of them an
    //   occupancy GAIN with the model exactly one card short (peer 106426/106428, 202820/202825,
    //   226517, 250152, 252561, 253623 and the rest). The other 8 fire nowhere near an occupancy
    //   change — those are the model DRAINING mid-action-phase, which _latchedFaces already covers.
    //
    //   ITEM 2, "Ich sehe bei den remote Karten nicht die Animation wie die Karte in die Hand
    //   zurueckkehrt, wenn man die Karte in die Hand nimmt und irgendwo loslaesst." The card goes
    //   back to the FAN. Locally that is not a tween at all — VRCard.OnRelease keeps the card's
    //   world pose across the re-parent, CardFan.Add re-inserts it at its OWN authored index and
    //   Relayout(instant: false) leaves VRCard's standing home-lerp to carry it there
    //   (1 - exp(-CardLerpSpeed * dt), with _releaseGlide holding it on unscaled time for
    //   ReleaseGlideSeconds). On a peer the held slab was simply deactivated where it hung and a
    //   fan slab appeared at its arc slot: the destination matched and the MOTION did not, which
    //   the 1:1 ruling counts as not done.
    //
    //   ...AND THE FIRST FIX FOR IT WAS ONLY 4/11 OF ONE (item 3, 2026-09-06): "Animation, dass die
    //   Karte zurueck in den Faecher geht fehlt beim remote spieler wenn der Faecher beim remote
    //   Spieler auch offen ist." The machinery below was shipped and correct and simply could not
    //   RUN: TrackFist wrote _fistSeat only where it could also NAME the card, so a fist this
    //   client could not name was a fist it did not track, and the release edge never fired. THE
    //   HOST LOG SAYS IT IN TWO GREPS. The peer's arc dipped by one and came back 11 times
    //   (Player.log 'geometry: n=' at 100373/100414, 100966/101014, 104266/104291, 115418/115432,
    //   117165/117185, 163674/163698, 230283/230306 and the four in 267814-269327); only 4 'FAN
    //   RETURN FLIGHT' lines exist. Every dip that flew has a 'Remote hand fan faces ... FRONTS'
    //   line beside it; not one of the six in 100373-101014 and 267814-269327 does, and that census
    //   line is change-gated against a _frontsShown that Rebuild zeroes on every count change — so
    //   its ABSENCE across a count change is positive evidence the fan was drawing BACKS. Backs
    //   means RevealGate.ShowRoundCardFronts was false, which is the `armed` argument, which was
    //   the term. The seat needed no gate to be safe: record 36's own list length and the sender's
    //   own arc count belt it (listLength == count + 1) with two numbers off one packet.
    //
    // WHY THE BELT IS NOT LOOSENED, for item 5. The belt exists because the alternative was a
    // CONFIDENTLY WRONG FACE — the user reported that twice — and nothing here weakens it: the
    // walk is still refused on a length disagreement, and what this adds is a SEPARATE, non-
    // positional fact that fills the hole the refusal leaves. Nor is the answer to hold the
    // previous face: _latchedFaces has nothing to hold for a card that is ARRIVING, which is the
    // half of the brief the evidence above corrects.
    //
    // NO NEW WIRE FIELD, AND NONE IS OWED. Record 36 already names WHICH seat of this client's own
    // hand list is in the peer's fist, in the same index space this fan resolves in (both machines
    // run CardsGameApi.HandFanMember), with the sender's LIST LENGTH beside it as the safety. So
    // the identity is resolved HERE, from a widget this client already holds, one frame before the
    // wire stops naming it — and the wire never carries a card id or a card name. That is the same
    // "the seat is already here" argument the HELD SEAT DROP above is built on, and the item chip's
    // take-back glide (RemoteItemFan._returnGlide) is the same zero-byte replay for item 2.
    //
    // THE PAIRING IS AN OBSERVATION, NOT A GUESS, and that is what separates it from the walk the
    // belt refuses. It arms only when EVERY one of these holds:
    //   * exactly ONE pose slot named a HAND seat (two fists against one recess is unpairable);
    //   * the sender's list length equalled this client's, so the seat named the card it meant;
    //   * the fist emptied and, measured against the occupancy mask AS IT WAS WHILE THE CARD WAS
    //     STILL IN IT, exactly one recess GAINED a card and none lost one;
    //   * it all happened inside HandoffGraceSeconds, so the two facts are one event.
    // Anything else and nothing arms, and the recess draws the same anonymous back it drew before.

    /// <summary>Unscaled seconds the mirrored return-to-fan glide runs — MIRROR of
    /// <c>Cards.VRCard.ReleaseGlideSeconds</c>, the window the owner's own released card is carried
    /// home on unscaled time in. Held equal by scripts/check-mirrors.sh: a peer whose glide is a
    /// different LENGTH is watching a different animation, which is what the 1:1 ruling forbids.
    /// </summary>
    private const float ReleaseGlideSeconds = 0.35f;

    /// <summary>How long after the fist empties the occupancy gain may still arrive and still count
    /// as the same event. The two facts ride ONE packet (record 36 and the board-UI byte are both
    /// in the extras stream) so this is normally zero frames; the window exists only so a dropped
    /// or re-ordered apply does not cost the hand-off, and it is deliberately short — every frame
    /// of it is a frame in which an unrelated recess could fill and be mispaired.</summary>
    private const float HandoffGraceSeconds = 0.25f;

    /// <summary>
    /// WHICH ARRIVAL the standing hand-off belongs to: the value of <see cref="_handoffGains"/> at
    /// the moment it was armed. A LATER arrival into the same recess retires it.
    ///
    /// <para>IT REPLACES A SIX-SECOND WALL CLOCK, AND THE CLOCK WAS THE DEFECT. That constant's own
    /// doc called itself "a BACKSTOP, not the normal exit" and named the two real exits — the model
    /// catching up (<c>RemoteControlBoard.SeatSlots</c> calls <see cref="ClearHandoff"/>) and the
    /// recess emptying (<see cref="ExpireHandoff"/> already tests the occupancy nibble). Both are
    /// EDGES and both are precise. The clock was neither, and it fired first: the ModBuild 461
    /// session's avoid-damage pick held one hand card in a recess for the whole window between the
    /// peer's own `Pick fan source (LoseCard): real hand` at 157886 and its close at 179525 — far
    /// longer than six seconds — so even an armed hand-off would have gone stale with the card
    /// still lying there and the fan still needing to drop it.</para>
    ///
    /// <para>WHAT THE CLOCK WAS GUARDING IS KEPT, EXACTLY. Its stated fear was a hand-off "whose
    /// model never arrives" standing forever. The precise version of that fear is not elapsed time,
    /// it is a DIFFERENT card arriving in the same recess without the mask ever reading empty — a
    /// turn-clear can do that inside one board pass. That is what this counter refuses, and a
    /// recess that simply keeps the card it was handed keeps the hand-off, which is the whole point.
    /// A timer whose normal exit is a timeout is a latch that outlives its edge; this one has no
    /// exit that is not an edge.</para>
    /// </summary>
    private int _handoffGainEpoch = -1;

    /// <summary>Arrivals into each round recess since this fan was built — the per-recess half of
    /// <see cref="_handoffGains"/>, which is the total. <see cref="_handoffGainEpoch"/> is compared
    /// against the entry for the hand-off's OWN recess.</summary>
    private readonly int[] _recessGains = new int[2];

    /// <summary>The card in this peer's fist right now, resolved from THIS client's own hand list
    /// through record 36's seat — null whenever the fist is empty, names two cards, the sender's
    /// list length disagrees with this client's, OR the reveal gate is shut.
    ///
    /// <para>IDENTITY ONLY, AND THAT IS THE CORRECTION OF 2026-09-06 (report item 3). This field
    /// used to be the whole fist: <see cref="_fistSeat"/> was written beside it and cleared with
    /// it, so a fist this client could not NAME was a fist it did not TRACK. The return-to-fan
    /// glide needs no name — it moves an anonymous back slab from a pose to a seat — but it was
    /// standing behind this null and could therefore never arm in the secret card-selection phase,
    /// which is the one phase in which a player fans their hand out and puts cards back. See the
    /// evidence block above <see cref="TrackFist"/>. The two facts are now tracked apart: this one
    /// gates the RECESS HAND-OFF, which does need a name, and nothing else.</para></summary>
    private CAbilityCard? _fistCard;

    /// <summary>The pose slot the fist's card is held in (1 or 2), so the return glide can
    /// read the slab it was released at.</summary>
    private int _fistPoseSlot;

    /// <summary>WHICH arc list this fist's seat came out of — <c>HeldFaceListHand</c> in a scenario,
    /// <c>HeldFaceListMapLoadout</c> in the map room. Carried for ONE reason: the verdict line has to
    /// NAME the fan. Six builds of return-glide work were tested only against a scenario hand, and
    /// nothing in the log said the other fan even existed (see <c>RemoteAvatar.IsFanArcList</c>).
    /// </summary>
    private byte _fistListId = NetProtocol.HeldFaceListNone;

    /// <summary>The hand seat the fist's card came out of — the arc index the card returns
    /// to when it is released into the void, because <c>CardFan.Add</c> re-inserts at
    /// <c>HomeIndexFor</c>, i.e. its OWN place and not the right-hand end.
    ///
    /// <para>Written off THE WIRE ALONE (record 36's seat, belted by the sender's own two numbers —
    /// see <c>seatUsable</c> in <see cref="TrackFist"/>), never off this client's model, so it
    /// survives a shut reveal gate. It is an INDEX, not an identity: nothing it reaches can print a
    /// face.</para></summary>
    private int _fistSeat = -1;

    /// <summary>The occupancy mask as it stood while the card was still in their fist. The GAIN is
    /// measured against this rather than against the previous frame's mask, so the pairing survives
    /// the two facts landing in either order inside the grace window.</summary>
    private int _fistMask;

    /// <summary>The owner's own arc SLAB COUNT while the card was still in their fist. The release
    /// branch requires it to have grown back by exactly one, which is what separates "released into
    /// the void, CardFan.Add is carrying it home" from "burnt / discarded", where the arc stays one
    /// slab short and there is no seat to fly to.</summary>
    private int _fistCount;

    /// <summary>Whether the arc measured in <see cref="_fistCount"/> still CARRIED the held card
    /// (record 36's list length equalled it) - the mode the release test below is judged in. See
    /// <see cref="ResolveArcHeldSeats"/> for why both modes are real and which one is steady.
    /// </summary>
    private bool _fistArcKeptHeld;

    /// <summary>Was record 36's seat a valid ARC index for this hold? False whenever a hand card is
    /// seated on the owner's board, because the arc and the model list then have different
    /// membership and seat k no longer names slab k. The NAME stays usable in that state — see the
    /// consumer split in <see cref="TrackFist"/>.</summary>
    private bool _fistArcSeatUsable;

    /// <summary>The wire arc count this frame, so <see cref="Bank"/> can quote it without every
    /// call site having to thread it through. Written once at the top of <see cref="TrackFist"/>.
    /// </summary>
    private int _bankArcCount;

    /// <summary>Unscaled time the fist last held a nameable card (0 = never / consumed).</summary>
    private float _fistHeldAt;

    /// <summary>THE ARMED HAND-OFF: the card this client saw handed from a fist into a round
    /// recess. Read by <c>RemoteControlBoard.SeatSlots</c> for the recess face and by the belt
    /// above for the fan's own length.</summary>
    internal CAbilityCard? HandoffCard { get; private set; }

    /// <summary>Which recess <see cref="HandoffCard"/> went into (-1 = none armed).</summary>
    private int _handoffRecess = -1;

    /// <summary>Unscaled time the armed hand-off's backstop runs out.</summary>

    /// <summary>Session census: how many times a recess GAINED a card, and how many of those this
    /// client could name. The pair is the falsifier — see <see cref="LogHandoff"/>.</summary>
    private int _handoffGains;
    private int _handoffArmed;

    /// <summary>WHICH TERM left the fist without a NAME on the last frame the wire named it — the
    /// sentence <see cref="LogHandoff"/>'s NOT-ARMED branch prints. Empty while the fist IS named.
    /// See the block in <see cref="TrackFist"/> for why this had to become a measurement.</summary>
    private string _fistNameMiss = string.Empty;
    private int _loggedHandoff = -1;

    /// <summary>The occupancy mask this fan last saw, for the arrival census's gain edge.</summary>
    private int _seenMask;
    private bool _seenMaskValid;

    /// <summary>The actor the fist state was resolved for — a hand-off must never survive the board
    /// switching which character it is about (the same second reset <c>_latchedActor</c> exists for
    /// one surface over).</summary>
    private CPlayerActor? _fistActor;

    /// <summary>Slab index gliding home after a release into the void (-1 = none) — the
    /// ability-card twin of <c>RemoteItemFan._returnIndex</c>.</summary>
    private int _returnIndex = -1;

    /// <summary>The HAND-LIST seat (record 36's own index space) the live return flight belongs to,
    /// kept beside the arc seat because the arc seat is a DERIVED value that must be re-resolved
    /// whenever the order moves. See <see cref="RetargetReturnGlide"/>. -1 = no live flight.</summary>
    private int _returnListSeat = -1;

    /// <summary>Unscaled seconds left of that glide.</summary>
    private float _returnGlide;

    /// <summary>The WORLD pose the returning card was released at, still to be stamped onto the
    /// slab. Held as a world pose and applied late because the slab list is REBUILT between the
    /// release and the first glide frame (the wire count goes back up by one), so a fan-local seed
    /// would be captured against the wrong root state.</summary>
    private bool _returnSeedPending;
    private Vector3 _returnSeedPos;
    private Quaternion _returnSeedRot = Quaternion.identity;

    /// <summary>Session census of mirrored return flights, for <see cref="LogReturnGlide"/>.
    /// </summary>
    private int _returnsPlayed;
    private int _loggedReturns = -1;

    /// <summary>How many times the WIRE said this peer let go of a hand card this session — every
    /// true-to-false edge of <c>RemoteAvatar.SingleHeldHandSeat</c>, counted BEFORE any belt, any
    /// reveal gate and any destination test.
    ///
    /// <para>THE DENOMINATOR HAS TO BE GATE-FREE OR IT MEASURES THE FIX WITH THE FIX. The defect
    /// this counter exists to convict was exactly that every refusal happened where nothing was
    /// counted, so "no line" and "no release" were the same reading. <c>SingleHeldHandSeat</c> is
    /// pure record 36, so this number is the same on a client whose reveal gate never opens.</para>
    /// </summary>
    private int _releaseEdges;

    /// <summary>Release edges that correctly produced no flight because the card went into a ROUND
    /// RECESS — the hand-off branch. Subtracted from the denominator when reading the verdict line:
    /// a card laid on the board owes no fan flight.</summary>
    private int _releaseToRecess;

    /// <summary>Release edges this mirror REFUSED to fly.</summary>
    private int _releaseRefused;

    /// <summary>WHICH named expression is refusing the release currently in flight, as a code.
    ///
    /// <para>A CODE AND NOT A SENTENCE, because this is written on a per-frame path: the belt
    /// branches run every frame the peer holds a card, and composing the refusal prose there would
    /// allocate a string per peer per frame for as long as somebody is reading a card. The sentence
    /// is built once, inside <see cref="LogReturnVerdict"/>, out of this code and
    /// <see cref="_refusalA"/>/<see cref="_refusalB"/>.</para></summary>
    private RefusalTerm _refusal;

    /// <summary>The two numbers the refusal sentence quotes — what each one means depends on
    /// <see cref="_refusal"/>, and <see cref="LogReturnVerdict"/> is the only reader.</summary>
    private int _refusalA;
    private int _refusalB;

    /// <summary>Third banked number: how many HAND cards this client could see lying in that
    /// peer's recesses when the refusal was banked. It is the term the seat belt gained for report
    /// item 6, and printing it is what tells "the belt is arithmetically right and the card really
    /// did go to a pile" apart from "the seated term did not see the card on the board".</summary>
    private int _refusalC;

    /// <summary>Fourth banked number: the owner's own wire ARC count at the moment the refusal was
    /// banked. It is the term the refusal line was missing — the line named the seat and the LIST
    /// length and never the arc, so "the arc kept the held card" and "a card is lying on their
    /// board" printed identically and two lanes read the same eight refusals as two different
    /// defects.</summary>
    private int _refusalD;

    /// <summary>The vocabulary of things that can refuse a mirrored return flight. Every one of
    /// them names a real expression in <see cref="TrackFist"/> or
    /// <see cref="BeginReturnGlide"/> — a refusal that could not name its term is the failure this
    /// enum exists to make impossible, and it has cost this project two shipped-inert fixes.
    /// </summary>
    private enum RefusalTerm
    {
        /// <summary>Nothing has refused anything yet this release.</summary>
        None = 0,

        /// <summary><c>seatUsable</c>: record 36's seat/length and the wire arc count did not read
        /// <c>listLength == count + 1</c>. A = the seat, B = the sender's list length.</summary>
        SeatBelt,

        /// <summary>No seat was tracked at all while the card was held.</summary>
        NeverTracked,

        /// <summary>The recess occupancy mask moved, so this release is not a plain return to the
        /// fan. A = the mask while held, B = the mask now.</summary>
        RecessMask,

        /// <summary>The wire arc did not grow back by exactly one — a burn or a discard, which
        /// correctly owes no flight. A = the arc while held, B = the arc now.</summary>
        ArcGrowth,

        /// <summary>The fan is folding away and has no arc seat to fly to.</summary>
        FanClosing,

        /// <summary>This peer has no mirrored held-card slab, so there is no released pose to glide
        /// from. A = the pose slot that was asked for.</summary>
        NoHeldSlab,

        /// <summary>Their fist filled again before the release could be judged.</summary>
        Regrabbed,

        /// <summary>The retry window ran out with none of the above resolving it.</summary>
        GraceExpired,
    }

    /// <summary>Last release-edge count stated in the log, so the verdict line fires once per
    /// release rather than per frame.</summary>
    private int _loggedVerdictEdge = -1;

    /// <summary>Whether the wire named a hand card in this peer's fist on the previous tick — the
    /// edge <see cref="_releaseEdges"/> counts. Off the wire alone, like the counter.</summary>
    private bool _fistNamedLast;

    /// <summary>A counted release edge that has not yet been given a verdict. It stays up across
    /// the <see cref="HandoffGraceSeconds"/> retry window, because the destination branches
    /// deliberately fall through and try again while the arc count catches up with the held
    /// flag.</summary>
    private bool _releasePending;

    // ────────────────────────────── THE ARC REFLOW (report item 3, second half) ────────────────
    //
    // WHAT THE OWNER ACTUALLY SEES, read off their own code and not approximated. Plucking a card
    // runs CardFan.Remove -> Relayout(instant: false); releasing it runs CardFan.Add ->
    // Relayout(instant: false). Relayout hands EVERY card a new home (CardFan.cs:2021,
    // `card.SetHome(_root, pos, rot, scale, instant || opening || swapping)`) and VRCard's standing
    // home-lerp carries each of them there — VRCard.cs:2253, `1 - exp(-CardLerpSpeed * dt)` on
    // localPosition, localRotation AND localScale. So the neighbours that closed over the gap on
    // the pluck GLIDE back open while the released card glides in: one continuous motion of the
    // whole arc.
    //
    // WHAT THE MIRROR DID INSTEAD. LayoutCards wrote `t.localPosition = pos` with no easing at all,
    // and Rebuild destroys and recreates every slab whenever the count changes — which a pluck and
    // a release both are. Seven cards teleported while one slid. The user's ruling this round is
    // that a partial mirror of an animation is not done: "Es soll nicht fehlen. Fuer 1:1 Regel soll
    // es voll gleich da sein."
    //
    // THE STAND-DOWN TERM IS THE OWNER'S OWN, VERBATIM. `instant || opening || swapping` is the
    // fifth argument of the SetHome call above, and CardFan's collapse writes instant: true at
    // CardFan.cs:2218. Its comment says why: "instant is forced so VRCard tracks the blend exactly
    // (its own home-lerp would double-smooth the motion)". Those three animations compute an
    // explicit blend that must be written, not eased toward — so the reflow ease stands down for
    // exactly the three the owner stands it down for, and for nothing else.
    //
    // AND A SLAB THAT WAS JUST BORN MUST SNAP, which is the fourth term and also the owner's:
    // VRCard._instantNext, set by SetHome(instant: true) out of the pool. A fresh slab is parented
    // with worldPositionStays:false, i.e. at the fan root's ORIGIN, so easing it would streak every
    // new card out of the middle of the fan. _seeded is that flag, per slab.

    /// <summary>Per-slab "this slab already has a pose to ease FROM", index-aligned with
    /// <see cref="_cards"/>. False for a slab Rebuild has just created with nothing carried into
    /// it; the first <see cref="LayoutCards"/> pass writes such a slab instantly and sets it. The
    /// mirror of <c>VRCard._instantNext</c>.</summary>
    private readonly List<bool> _seeded = new(MaxCards);

    /// <summary>Slab poses banked by <see cref="Rebuild"/> from the set it is about to destroy, in
    /// FAN-ROOT-LOCAL space — the root object survives a Rebuild (only <see cref="_cards"/> is torn
    /// down), so local is both correct and free of the moving-hand term a world pose would carry.
    /// </summary>
    private readonly List<Vector3> _carryPos = new(MaxCards);
    private readonly List<Quaternion> _carryRot = new(MaxCards);
    private readonly List<float> _carryScale = new(MaxCards);

    /// <summary>Session census of the reflow, for the verdict line: slabs that were carried across
    /// a rebuild and therefore EASED to their new seat, against slabs that were born at the seat
    /// and therefore TELEPORTED to it.</summary>
    private int _reflowEased;
    private int _reflowSnapped;

    /// <summary>The last rebuild's own two numbers — how many of that rebuild's slabs were carried,
    /// out of how many it built. Quoted by the verdict line so a reader can see whether the OTHER
    /// slabs moved on the same release the returning card did.</summary>
    private int _lastCarried;
    private int _lastBuilt;

    /// <summary>Unscaled time of that rebuild. The verdict line quotes the pair ONLY when the
    /// rebuild belongs to the release being judged — a refused release whose arc count never moved
    /// has no rebuild of its own, and printing the last one that happened to occur would be a
    /// number from a different event dressed as this one's.</summary>
    private float _lastRebuildAt = float.NegativeInfinity;

    /// <summary>The exponential rate the OWNER's own released card flies home at
    /// (<c>[Cards] CardLerpSpeed</c>, off <see cref="RemoteAvatar.BoardTuning"/>) — theirs and
    /// never this client's, so the mirrored glide settles on the owner's clock. Falls back to the
    /// shipped default for a peer who has not moved the dial, exactly like every other term in
    /// <see cref="SyncTuning"/>.</summary>
    private float _lerpSpeed = Defaults.CardLerpSpeed;

    /// <summary>Scratch for <see cref="ApplyFanArcOrder"/>'s permutation. Reused; this runs every
    /// frame the fan is up.</summary>
    private readonly List<AbilityCardUI> _orderScratch = new(MaxCards);

    /// <summary>How many DISTINCT orders this peer has stated (record 44 arriving with content
    /// this fan had not already applied) — the "the order arrived" half of the falsifier.</summary>
    private int _orderStated;

    /// <summary>How many of those DISTINCT orders were an exact permutation of the seats this
    /// client held and were therefore APPLIED — the "the order was applied" half. Deliberately a
    /// SEPARATE number from <see cref="_orderStated"/>: stated &gt; 0 with applied == 0 is the one
    /// reading that says the record travelled and the belt refused it, which is a different defect
    /// from the record never being sent at all.
    ///
    /// <para>COUNTED PER DISTINCT ORDER AND NOT PER FRAME, which it was not until 2026-09-07 — and
    /// that made the pair unreadable in exactly the round it was shipped for. The 2026-09-06 logs
    /// carry <c>stated=23 applied=37</c> and <c>stated=25 applied=131</c>: applied &gt; stated is
    /// impossible under the reading the line's own prose gives ("how many of THEM were applied"),
    /// because applied was counting FRAMES while stated counted orders. Two populations, one ratio
    /// — the recorded failure this project calls "a ratio with two populations". Both numbers now
    /// count orders, so <c>applied &lt; stated</c> is a real refusal count and the pair
    /// subtracts.</para></summary>
    private int _orderApplied;

    /// <summary>Change key for <see cref="_orderApplied"/>, so one standing order applied over
    /// 200 frames counts once. Distinct from <see cref="_orderKey"/>: an order can be STATED for
    /// many frames before the seats this client holds let it be applied.</summary>
    private int _orderAppliedKey = int.MinValue;

    /// <summary>How many times the arc was drawn in the order this fan LAST APPLIED because the
    /// current packet stated none — see <see cref="_latchOrderIds"/>.</summary>
    private int _orderHeld;

    /// <summary>The last order this fan actually applied, as <c>CardInstanceID</c>s in arc order.
    /// Empty when nothing has ever applied for this peer.
    ///
    /// <para>THE RECEIVER HAD NOTHING TO KEEP, AND EVERY COMMENT AROUND IT SAID IT DID. Four source
    /// blocks — this file's, <c>LocalRigSampler.SampleFanArcOrder</c>'s, <c>NetProtocol</c>'s
    /// record-44 header and the sender's own economy argument — asserted that omitting the record
    /// is safe because "silence means keep the order you already had". It never did:
    /// <c>_handBuffer</c> is rebuilt from <c>CardsGameApi.HandFanMember</c> every frame, in the
    /// GAME's order, and <see cref="ApplyFanArcOrder"/> re-derives the arc from whatever the
    /// CURRENT packet states. Absence therefore meant "go back to the game's order", and the fan
    /// snapped between the owner's arrangement and the game's every time the sender's arc drifted
    /// into the identity the sender was withholding on. MEASURED, cross-log, same session
    /// (2026-09-07): remote 197677 applies <c>fp=27e7dccb</c> (right='Scurry', the host's own DRAWN
    /// fingerprint at host 196933); 79 lines later remote 197756 prints <c>fp=5469e8a7</c>
    /// (right='FearsomeBlade', the host's DERIVED fingerprint) with <c>none stated</c>, while the
    /// host's own <c>FAN ORDER MIRROR</c> had not changed at all. "Ganz rechts eine andere Karte",
    /// with both hex words and both card names.</para>
    ///
    /// <para>THE SENDER NOW STATES AN IDENTITY EXPLICITLY, so this latch is a belt and not the fix
    /// — it covers a genuinely lost packet and a transient sender refusal. It is keyed on CARD
    /// IDENTITY and expires the instant the membership changes, so it can never hold an order over
    /// a hand it no longer describes: a draw, a burn, a play or a pluck all invalidate it and the
    /// fan falls back to the game's order exactly as before.</para></summary>
    private readonly List<int> _latchOrderIds = new(MaxCards);

    /// <summary>The <c>CardInstanceID</c>s of the whole derived model list at the moment
    /// <see cref="_latchOrderIds"/> was recorded, ASCENDING. The latch may only be re-applied while
    /// the current model list holds exactly this set — same cards, any order.</summary>
    private readonly List<int> _latchMemberIds = new(MaxCards);

    /// <summary>Scratch for the latch's membership compare. Reused; this runs every frame the fan
    /// is up and no order is stated.</summary>
    private readonly List<int> _latchScratch = new(MaxCards);

    /// <summary>True while the order in force this frame came from <see cref="_latchOrderIds"/>
    /// rather than from a record on this packet — the HELD verdict of the log line.</summary>
    private bool _orderFromLatch;

    /// <summary>
    /// THE PERMUTATION THAT IS ACTUALLY IN FORCE, as MODEL indices: entry <c>k</c> is the hand-list
    /// index the arc holds at seat <c>k</c>. Written wherever <see cref="_orderScratch"/> is filled —
    /// the STATED path and the HELD path alike — and zeroed by <see cref="ApplyFanArcOrder"/>'s own
    /// first lines, so a refused frame can never leave a stale one standing.
    ///
    /// <para>WHY IT EXISTS AT ALL, when <c>_owner.FanArcOrder</c> is already the wire's own array:
    /// because the wire array is only ONE of the two ways an order comes into force. Since the
    /// 2026-09-07 order fix the receiver also HOLDS the last applied order across a packet that
    /// states none (<see cref="TryHoldFanArcOrder"/>), and a HELD order fills the gather with
    /// <c>_owner.FanArcOrder</c> STILL NULL. Anything translating off the wire array is therefore
    /// right for a stated order and silently falls back to the identity for a held one — in exactly
    /// the state the latch made common. This array is the one expression both are true of.</para>
    ///
    /// <para>AND IT IS READABLE IN BOTH WINDOWS OF THE FRAME, which the gather is not.
    /// <see cref="_orderScratch"/> lives only between <see cref="ApplyFanArcOrder"/> and
    /// <see cref="CommitFanArcOrder"/>; <see cref="ResolveArcHeldSeats"/> runs a whole method
    /// earlier, out of <see cref="LayoutCards"/>, where the gather is empty and
    /// <see cref="_handBuffer"/> already holds the PREVIOUS commit's arc order. Read there, this
    /// array is last frame's applied permutation — which is precisely the order the slabs being laid
    /// out are currently in, so the two agree by construction instead of by luck.</para>
    /// </summary>
    private readonly int[] _appliedOrder = new int[MaxCards];

    /// <summary>How many seats of <see cref="_appliedOrder"/> are meaningful. 0 = no permutation in
    /// force, which means the arc IS the model order and the identity is the correct answer.</summary>
    private int _appliedOrderCount;

    /// <summary>Why the most recent stated order was refused, for the log line. Empty when the last
    /// one was applied.</summary>
    private string _orderRefusal = string.Empty;

    /// <summary>Change key for the order clause of <see cref="ReportArcMembershipIfChanged"/>.
    /// </summary>
    private int _orderKey;

    /// <summary>
    /// Re-lay <see cref="_handBuffer"/> into the order the OWNER'S OWN ARC is drawn in, from
    /// extension record 44. Report item 2 of 2026-09-06.
    ///
    /// <para>THE RECORD IS A PERMUTATION OF THIS CLIENT'S OWN DERIVED LIST — entry k is the index,
    /// into the list this fan just built with <c>CardsGameApi.HandFanMember</c>, of the card the
    /// owner has at arc seat k. So applying it is one gather, and afterwards slab i is a name for
    /// card i in the OWNER's order rather than in the game's, which is what "exakt an den selben
    /// Stellen" means.</para>
    ///
    /// <para>EVERY REFUSAL KEEPS THE OLD ORDER AND NAMES ITSELF. There are four, and they are not
    /// interchangeable:</para>
    /// <list type="bullet">
    ///   <item><c>none stated</c> — no record 44 on the wire. A FLAT player, a peer on an older
    ///     build, or (much the commonest) a peer whose arc is already in this order, which the
    ///     sender deliberately does not spend bytes restating. NOT a defect.</item>
    ///   <item><c>model length</c> — this client's derived list is SHORTER than the arc on the
    ///     wire, so no index into it can name a slab. The ordinary cause is a model a beat behind
    ///     and it is transient. A LONGER derived list is no longer a refusal at all: that is a card
    ///     in the owner's fist or lying in a recess, and the order is what names it (report item 4).
    ///     </item>
    ///   <item><c>arc length</c> — the record states a different number of seats than there are
    ///     slabs on the wire, so it describes a neighbouring moment rather than this fan. Transient
    ///     around a draw, a burn or a pluck, because the count and the order ride the same packet
    ///     only in the normal case.</item>
    ///   <item><c>not a permutation</c> — the wire's own numbers are not distinct indices in range
    ///     of the list held: a repeated index or one past the end. THIS IS THE ONE THAT WOULD HAVE
    ///     SHIFTED EVERY FACE, and it must never be applied.</item>
    ///   <item><c>no fronts</c> — there is no resolved list to permute. The slabs are backs and
    ///     their order is unobservable, so nothing is owed.</item>
    /// </list>
    /// </summary>
    private void ApplyFanArcOrder(int count)
    {
        _orderArcValid = false;
        _orderFromLatch = false;
        // Zeroed HERE and written only by the two paths that actually stage a gather, so every
        // refusal below — "arc length", "not a permutation", "no fronts", "none stated" — leaves the
        // identity standing rather than a permutation from a frame that no longer describes this arc.
        _appliedOrderCount = 0;
        // A CLOSED FAN FORGETS. Nothing this latch holds survives the arc going away, so a fan
        // raised again is never drawn in an order its owner left behind minutes ago.
        if (count <= 0)
            ClearFanArcLatch();
        int[]? order = _owner.FanArcOrder;
        int stated = _owner.FanArcOrderCount;
        if (order == null || stated <= 0)
        {
            // SILENCE KEEPS THE ORDER, WHICH IS WHAT FOUR SOURCE COMMENTS HAVE CLAIMED SINCE
            // ModBuild 462 AND NO CODE DID. See _latchOrderIds for the cross-log reading that
            // convicts it. Keyed on card identity and on an UNCHANGED membership, so it holds a
            // dropped packet and never a stale hand.
            if (TryHoldFanArcOrder(count))
            {
                _orderRefusal = string.Empty;
                _orderArcValid = true;
                _orderFromLatch = true;
                _orderHeld++;
                ReportArcOrderIfChanged(count);
                return;
            }
            _orderRefusal = "none stated";
            ReportArcOrderIfChanged(count);
            return;
        }
        // ONE COUNT PER DISTINCT ORDER, not one per frame: the record rides every extras packet
        // while it is in force, so counting arrivals would count the packet rate.
        int key = stated;
        unchecked
        {
            for (int k = 0; k < stated && k < order.Length; k++)
                key = key * 31 + order[k];
        }
        if (key != _orderKey)
        {
            _orderKey = key;
            _orderStated++;
        }

        if (_handBuffer.Count == 0)
        {
            _orderRefusal = "no fronts";
            ReportArcOrderIfChanged(count);
            return;
        }
        // THE ORDER MUST DESCRIBE THIS FAN AND NOT A NEIGHBOURING MOMENT: one entry per SLAB on the
        // wire. This is the term that used to be spelled `_handBuffer.Count != count` — a test on
        // the MODEL's length, which refused the record in exactly the state it is most needed
        // (report item 4: a card in the owner's fist or lying in a recess makes the model longer
        // than the arc). The arc's own length is the honest denominator; the model's length is now
        // only a bound on the indices, which ValidateFanArcOrder checks.
        if (stated != count)
        {
            _orderRefusal = "arc length";
            ReportArcOrderIfChanged(count);
            return;
        }
        if (_handBuffer.Count < count)
        {
            // The model is SHORTER than the arc — this client has not caught up with the owner's
            // own fan, and no index into it can name a slab. The old name for this refusal is kept
            // because a hardware log's reason strings are a vocabulary and not prose.
            _orderRefusal = "model length";
            ReportArcOrderIfChanged(count);
            return;
        }
        if (!NetProtocol.ValidateFanArcOrder(order, stated, _handBuffer.Count))
        {
            _orderRefusal = "not a permutation";
            ReportArcOrderIfChanged(count);
            return;
        }
        // STAGED, NOT COMMITTED — see CommitFanArcOrder for why the swap cannot happen here. The
        // gather itself is the whole application: entry k names the derived index at arc seat k, so
        // a derived card the order does not name is one the arc does not carry and is dropped BY
        // NAME rather than by any arithmetic on lengths.
        _orderScratch.Clear();
        for (int k = 0; k < stated; k++)
        {
            _orderScratch.Add(_handBuffer[order[k]]);
            if (k < _appliedOrder.Length)
                _appliedOrder[k] = order[k];   // see _appliedOrder: the gather in index form
        }
        _appliedOrderCount = Mathf.Min(stated, _appliedOrder.Length);
        _orderRefusal = string.Empty;
        _orderArcValid = true;
        // ONE COUNT PER DISTINCT ORDER APPLIED, for the same reason _orderStated is: this runs
        // every frame the fan is up, and a per-frame count is not comparable with a per-order one.
        if (key != _orderAppliedKey)
        {
            _orderAppliedKey = key;
            _orderApplied++;
        }
        RecordFanArcLatch();
        ReportArcOrderIfChanged(count);
    }

    /// <summary>Forget the last applied order. Called when the arc goes away and whenever the
    /// membership the latch was recorded over no longer holds.</summary>
    private void ClearFanArcLatch()
    {
        _latchOrderIds.Clear();
        _latchMemberIds.Clear();
    }

    /// <summary>
    /// Remember the order just staged in <see cref="_orderScratch"/>, by CARD IDENTITY, together
    /// with the membership of the whole derived model list it was gathered out of.
    ///
    /// <para>Identities, not indices: a derived index means nothing once the list it indexed
    /// changes, which is precisely the state the latch must refuse to survive. Any card whose
    /// <c>CardInstanceID</c> is 0 or repeated makes the whole latch unrecordable — a fold that
    /// cannot name every card must not be used to reorder them.</para>
    /// </summary>
    private void RecordFanArcLatch()
    {
        ClearFanArcLatch();
        for (int k = 0; k < _orderScratch.Count; k++)
        {
            AbilityCardUI? w = _orderScratch[k];
            int id = w != null ? w.CardInstanceID : 0;
            if (id == 0 || _latchOrderIds.Contains(id))
            {
                ClearFanArcLatch();
                return;
            }
            _latchOrderIds.Add(id);
        }
        for (int i = 0; i < _handBuffer.Count; i++)
        {
            AbilityCardUI? w = _handBuffer[i];
            int id = w != null ? w.CardInstanceID : 0;
            if (id == 0 || _latchMemberIds.Contains(id))
            {
                ClearFanArcLatch();
                return;
            }
            _latchMemberIds.Add(id);
        }
        _latchMemberIds.Sort();
    }

    /// <summary>
    /// Re-lay <see cref="_handBuffer"/> into the order this fan LAST APPLIED, when the current
    /// packet states none. Returns false — and changes nothing — unless every term holds.
    ///
    /// <para>THE TERMS, and each one is what stops a held order from becoming a lie:</para>
    /// <list type="bullet">
    ///   <item>an order was applied at least once for this peer, so the latch describes something
    ///     they really stated rather than something this client invented;</item>
    ///   <item>it names exactly <paramref name="count"/> seats — the arc on the wire NOW, so a
    ///     draw, a burn, a play or a pluck since the latch was taken refuses it;</item>
    ///   <item>the derived model list holds exactly the membership the latch was recorded over,
    ///     compared as a SET of card identities, so the same cards in any order qualify and one
    ///     different card does not;</item>
    ///   <item>every latched id is found exactly once in that list.</item>
    /// </list>
    ///
    /// <para>Fails closed in the same direction as everything else here: on any doubt the arc is
    /// drawn in the game's order, which is what every build before ModBuild 462 drew.</para>
    /// </summary>
    private bool TryHoldFanArcOrder(int count)
    {
        if (_latchOrderIds.Count == 0 || _latchOrderIds.Count != count)
            return false;
        if (_handBuffer.Count == 0 || _handBuffer.Count != _latchMemberIds.Count)
            return false;
        _latchScratch.Clear();
        for (int i = 0; i < _handBuffer.Count; i++)
        {
            AbilityCardUI? w = _handBuffer[i];
            int id = w != null ? w.CardInstanceID : 0;
            if (id == 0)
                return false;
            _latchScratch.Add(id);
        }
        _latchScratch.Sort();
        for (int i = 0; i < _latchScratch.Count; i++)
        {
            if (_latchScratch[i] != _latchMemberIds[i])
            {
                // The membership moved on. The latch describes a hand that no longer exists and
                // must not outlive it.
                ClearFanArcLatch();
                return false;
            }
        }
        _orderScratch.Clear();
        for (int k = 0; k < _latchOrderIds.Count; k++)
        {
            int want = _latchOrderIds[k];
            AbilityCardUI? found = null;
            int foundAt = -1;
            for (int i = 0; i < _handBuffer.Count; i++)
            {
                AbilityCardUI? w = _handBuffer[i];
                if (w != null && w.CardInstanceID == want)
                {
                    found = w;
                    foundAt = i;
                    break;
                }
            }
            if (found == null)
            {
                _orderScratch.Clear();
                _appliedOrderCount = 0;
                return false;
            }
            _orderScratch.Add(found);
            // A HELD order is an order. Recording it in the SAME index form the stated path uses is
            // the whole of the ResolveArcHeldSeats fix: this path leaves _owner.FanArcOrder null, so
            // every consumer that read the wire array fell back to the identity here and hid the
            // wrong slab. See _appliedOrder.
            if (k < _appliedOrder.Length)
                _appliedOrder[k] = foundAt;
        }
        _appliedOrderCount = Mathf.Min(_latchOrderIds.Count, _appliedOrder.Length);
        return true;
    }

    /// <summary>
    /// Swap the staged record-44 gather into <see cref="_handBuffer"/>, or leave the buffer alone
    /// when no order applied this frame.
    ///
    /// <para>WHY THE GATHER IS STAGED AND COMMITTED AT TWO DIFFERENT POINTS OF THE FRAME. Between
    /// <see cref="ApplyFanArcOrder"/> and here sits <see cref="TrackFist"/>, and that method reads
    /// <see cref="_handBuffer"/> as THIS CLIENT'S WHOLE DERIVED MODEL LIST — record 36's held seat
    /// indexes that list, not the arc, and its own belt is <c>listLength == _handBuffer.Count</c>.
    /// Committing the gather before it would shorten the buffer under a seat that is not an index
    /// into the shortened list, which costs the fist its NAME and with it the recess hand-off. The
    /// two facts want the buffer in two states, so the frame gives them one each: the model list
    /// while the fist is being tracked, the ARC afterwards.</para>
    ///
    /// <para>This was a no-op before the injection widening, because an order that applied was
    /// always a permutation and a permutation never changes a list's LENGTH — which is exactly why
    /// the ordering could be ignored until now.</para>
    /// </summary>
    private void CommitFanArcOrder()
    {
        if (!_orderArcValid)
        {
            _orderScratch.Clear();
            return;
        }
        _handBuffer.Clear();
        for (int k = 0; k < _orderScratch.Count; k++)
            _handBuffer.Add(_orderScratch[k]);
        _orderScratch.Clear();
    }

    /// <summary>Change key for <see cref="ReportArcOrderIfChanged"/> — the verdict and the reason,
    /// so a standing refusal costs one line and not one per frame.</summary>
    private string _loggedOrderVerdict = "\u0000";

    /// <summary>
    /// HARDWARE EVIDENCE for report item 2 of 2026-09-06. Grep token: ARC ORDER NOT APPLIED.
    ///
    /// <para>THE USER'S RULING IS WHY THIS IS ITS OWN LINE AND NOT A CLAUSE: "Es ist sehr wichtig,
    /// dass im Faecher immer die richtigen Karten am richtigen Platz liegen. D.h. jegliche
    /// Umsortierungen die ein Spieler taetigt MUESSEN zwingend auch so von allen anderen Spielern
    /// gesehen werden. Das ist NICHT optional." A client that is drawing this peer's fan in an
    /// order its owner is not looking at is failing that requirement, and the standing rule is to
    /// disable a feature VISIBLY rather than silently. So it says so, every time, with the reason —
    /// and the reason is the whole value, because "the order never arrived" and "the order arrived
    /// and was refused" look identical through a headset and have different fixes.</para>
    ///
    /// <para>READ IT LIKE THIS, and this is meant to be the FIRST grep after "der Faecher war
    /// wieder falsch":</para>
    /// <list type="bullet">
    ///   <item><c>APPLIED</c> — this arc is in its owner's own order. The requirement is met and
    ///     item 2 cannot be live for this fan.</item>
    ///   <item><c>reason=peer build N</c> — THE PEER CANNOT SEND ONE. A FLAT player (N=0) or a
    ///     modded peer predating ModBuild 462. No receiver code can fix this and their fan WILL
    ///     diverge for everyone watching for as long as they run that build; the answer is that
    ///     they update. This is a limit of the feature, not a bug in it.</item>
    ///   <item><c>reason=none stated</c> — the peer CAN send one and THIS PACKET CARRIED NONE. It
    ///     is NOT a statement that their arc already matches ours, and it was read as one for seven
    ///     builds. This bullet used to end "which the sender only does when its arc already equals
    ///     the derived order — benign, and the picture is right"; that described ModBuild 462-470
    ///     and was made FALSE by the sender fix in this same tree at ModBuild 471 (commit
    ///     647c5103). <c>LocalRigSampler.SampleFanArcOrder</c> has had no identity shortcut since —
    ///     "NO IDENTITY SHORTCUT … State it." — so silence now means only that one of that method's
    ///     named exits fired, or that no packet arrived, and a receiver cannot tell those apart and
    ///     must not pretend to. The reading that CAN is the OWNER's own
    ///     <c>FAN ARC ORDER SENT … why=</c> line, which names the exit. Distinguished from the line
    ///     above precisely so that "he sent nothing" is never mistaken for "he cannot".</item>
    ///   <item><c>reason=not a permutation</c> — THE SERIOUS ONE. The record arrived and its own
    ///     numbers were self-contradictory (a repeated index, or one past the end of the list this
    ///     client holds). Nothing was applied, deliberately: a wrong order shifts every face after
    ///     the first mistake, which is a worse lie than the divergence it would fix. If this ever
    ///     appears, the SENDER is at fault and LocalRigSampler.SampleFanArcOrder is the file.
    ///     </item>
    ///   <item><c>reason=model length</c> / <c>reason=arc length</c> / <c>reason=no fronts</c> —
    ///     ordinary transients: a model a beat behind its own fan, a count and an order that rode
    ///     different packets around a burn, or a shut reveal gate leaving no list to permute.
    ///     Expected to appear and disappear; a PERSISTENT one is the finding. NOTE that a card in
    ///     the owner's FIST or lying in one of their RECESSES is no longer any of these: since
    ///     ModBuild 463 the order is an INJECTION and states exactly that, which is what lets the
    ///     fan keep its fronts through a pick (report item 4).</item>
    /// </list>
    /// </summary>
    private void ReportArcOrderIfChanged(int count)
    {
        // A FAN WITH NO SLABS HAS NO ORDER TO GET WRONG, and saying so every time one shuts is how
        // this line came to read as a standing refusal. MEASURED (2026-09-07 evening round): EVERY
        // 'reason=none stated' that follows an APPLIED/HELD burst in either log prints "this peer's
        // 0-slab fan" — user 18792 and 37160, mate 10730, 12581, 12730, 30248, 36697, 37060, 37189,
        // 38762 and 42757 — and those ten readings are the whole of why 'none stated' looked like
        // the dominant verdict when the fan was simply closed. The verdict is FORGOTTEN rather than
        // kept, so the first frame of a re-raised fan restates it in full instead of inheriting a
        // word about a fan that no longer exists.
        if (count <= 0)
        {
            _loggedOrderVerdict = "\u0000";
            return;
        }
        int build = VersionGuard.PeerBuild(_owner.PlayerId);
        // A PEER THAT CANNOT SEND ONE IS A DIFFERENT ANSWER FROM ONE THAT DID NOT, and only this
        // term can tell them apart. IT IS THE BUILD THAT DECIDES, NOT THE COUNTER — until
        // 2026-09-07 this said "peer build {build}" for ANY peer that had not yet stated an order,
        // so the very first grep of the 2026-09-07 host log returned `reason=peer build 470
        // ... peer ModBuild 470, ours 470` on a peer that is four builds PAST the record, under a
        // sentence reading "that player CANNOT send an order". A capable peer that has said nothing
        // yet is 'none stated'; only a build that has never heard of record 44 is a build answer.
        bool peerCanSend = build >= NetProtocol.FanArcOrderMinPeerBuild;
        string reason = _orderArcValid
            ? string.Empty
            : !peerCanSend && _orderRefusal == "none stated"
                ? $"peer build {build}"
                : _orderRefusal;
        string verdict = _orderArcValid ? (_orderFromLatch ? "HELD" : "APPLIED") : reason;
        if (verdict == _loggedOrderVerdict)
            return;
        _loggedOrderVerdict = verdict;
        // HW-VERIFY: report item 2 (2026-09-06, again 2026-09-07). Grep token: ARC ORDER NOT
        // APPLIED.
        VRLog.Note("Net", _orderArcValid
            ? $"ARC ORDER NOT APPLIED [player {_owner.PlayerId}]: (cleared, {verdict}) — this arc "
              + "IS in its owner's own left-to-right order over "
              + $"{count} seat(s). stated={_orderStated} applied={_orderApplied} held={_orderHeld}"
              + ". APPLIED means extension record 44 rode this packet and named it; HELD means this "
              + "packet named none and the arc was drawn in the order this fan LAST applied, which "
              + "is only allowed while the membership is unchanged card for card (see "
              + "TryHoldFanArcOrder). Before the 2026-09-07 fix there was no HELD: an absent record meant "
              + "the slab list stayed in the GAME's order, so the fan snapped back and forth every "
              + "time the sender's arc drifted into the identity the sender withheld on. The user's "
              + "requirement for this fan is met: \"jegliche Umsortierungen die ein Spieler taetigt "
              + "MUESSEN zwingend auch so von allen anderen Spielern gesehen werden\"."
            : $"ARC ORDER NOT APPLIED [player {_owner.PlayerId}]: this client is drawing this "
              + $"peer's {count}-slab fan in the GAME's order, which may not be the order its "
              + $"owner is looking at. reason={reason} (stated={_orderStated} applied="
              + $"{_orderApplied}, peer ModBuild {build}, ours {NetProtocol.ModBuild}). THE PAIR IS "
              + "NOT A RATE AND DOES NOT SUBTRACT: 'stated' is counted at the TOP of "
              + "ApplyFanArcOrder, ABOVE the 'no fronts' / 'arc length' / 'model length' / 'not a "
              + "permutation' gates, so it counts every distinct record-44 payload this fan was "
              + "handed INCLUDING every one handed to a fan that was drawing BACKS and had no list "
              + "to permute, while 'applied' counts only those that reached the gather. In the "
              + "2026-09-07 evening round that pair read 61 vs 4 on one client purely because the "
              + "peer's arc flapped 8-7-8 through a SINGLE pluck for ~35 census rows while the "
              + "selection phase kept the mirrored fan on backs. THE READING THAT ANSWERS 'DID THE "
              + "PICTURE GET THE ORDER' IS THE 'MIRRORED ARC ORDER' CENSUS ROW AND NOT THIS PAIR: "
              + "filter it to model>0 and read thisFrame=. In that round every such row but three "
              + "said APPLIED or HELD. THIS IS THE "
              + "FEATURE DEGRADING AND IT SAYS SO RATHER THAN DOING IT QUIETLY. 'peer build N' "
              + "means that player CANNOT send an order — a FLAT player reads 0, and anything below "
              + "462 predates the record; their fan will diverge for every watcher until they "
              + "update, and no receiver code can fix it. 'none stated' means they CAN send one "
              + "and THIS PACKET CARRIED NONE — it does NOT mean their arc already matches ours. "
              + "Their sender has had no identity shortcut since ModBuild 471 (LocalRigSampler."
              + "SampleFanArcOrder states the arc in full, identity or not), so silence means one "
              + "of that method's named exits fired or the packet did not arrive, and only the "
              + "OWNER's 'FAN ARC ORDER SENT ... why=' line can say which. This sentence read "
              + "'benign, and the picture is right' until ModBuild 479 and cost a whole diagnosis "
              + "round. 'not a permutation' is the serious one: "
              + "the record arrived and its own numbers did not form an exact permutation of the "
              + "seats held, so nothing was applied on purpose (a wrong order shifts every face "
              + "after the first mistake, a worse lie than the gap it would close) and the SENDER "
              + "is at fault. 'model length', 'arc length' and 'no fronts' are transients around a "
              + "burn, a packet boundary or a shut gate — a PERSISTENT one of those is the finding, "
              + "not the noise. A card in the owner's FIST or lying in one of their RECESSES is NOT "
              + "one of them any more: the order is an INJECTION and names exactly which derived "
              + "cards the arc does not hold, which is what keeps this fan's fronts through a pick "
              + "(2026-09-06 report item 4).");
    }

    /// <summary>True while the order applied this frame is a validated permutation — the term
    /// <see cref="ResolveArcHeldSeats"/> needs before it may translate a record-36 seat into an ARC
    /// seat.</summary>
    private bool _orderArcValid;

    /// <summary>
    /// WHICH SEATS OF THIS ARC THE OWNER IS HOLDING, and therefore which slabs must not be drawn.
    /// Report item 1 of 2026-09-06, and the FOURTH appearance of one membership defect.
    ///
    /// <para>THE PREMISE EVERY EARLIER BUILD REASONED FROM IS FALSE, and the 2026-09-06 host log
    /// convicts it. Three source blocks in this tree still assert that "plucking an ability card
    /// runs <c>CardFan.Remove</c>, so the wire count drops and the mirrored fan rebuilds one slab
    /// shorter for free" (RemoteAvatar's HeldItemSeat block, RemoteItemFan's header, and the motion
    /// belt in <see cref="TrackFist"/> below). <c>CardFan.Remove</c> does run on the grab
    /// (CardsDriver.5.Interactions.cs) - and <c>CardsDriver.Rebuild</c> then runs on very nearly
    /// the next frame, re-fills the fan from the game's own widget list through
    /// <c>CardsGameApi.HandFanMember</c>, and PUTS THE HELD CARD STRAIGHT BACK: a card in the
    /// player's fist has not been played, so its <c>CardType</c> is still <c>Hand</c> and it is
    /// still a member. <c>CardFan.SetCards</c> says so in its own words ("the incoming list
    /// legitimately still names a held card"), and <c>CardFan.Relayout</c> then does exactly what
    /// <c>ItemsPile.Relayout</c> and <c>PileBrowser.Relayout</c> do - keeps <c>n =
    /// _cards.Count</c> and merely declines the held card a POSE. The count on the wire never
    /// stays down.</para>
    ///
    /// <para>THE LOG READING, which is the evidence and not the argument. In the 2026-09-06 host
    /// session the mirrored fan's own geometry line steps <c>n=8</c>, <c>n=7</c>, <c>n=8</c> across
    /// a single pluck, twice, WHILE the card is still in the peer's fist - the removal and the
    /// rebuild's re-add, one frame apart. The observer was drawing what it was sent, and what it was
    /// sent still contained the card the peer was holding up.</para>
    ///
    /// <para>SEVEN OF THE EIGHT, NOT ALL EIGHT, AND THE CORRECTION MATTERS BECAUSE TWO LANES CLAIMED
    /// THE SAME EIGHT. This paragraph read "All 8 FAN RETURN VERDICT refusals in that session name
    /// term=seatUsable with an 8-card list, i.e. the belt asking for an arc of 7 and being handed
    /// 8". The refusal line prints the seat and the LIST length and never the ARC count, so it
    /// cannot say which side the mismatch was on and neither lane could read it off that line. The
    /// sender's own <c>Fan order [scenario hand] n</c> sequence can, by DWELL TIME: a pluck's n=7
    /// is back at 8 within 10-30 log lines (nine such blips, peer 85298 through 119735) and seven
    /// of the refusals sit in that stretch, while inside the <c>Pick fan source (LoseCard): real
    /// hand</c> window n=7 STANDS for 358, 1625, 2966 and once 14493 lines — and the eighth refusal
    /// (host 184623) is in there. Two mechanisms, one instrument that could not tell them apart.
    /// The belt in <see cref="TrackFist"/> now carries both terms and says which is which.</para>
    ///
    /// <para>SO THE REMEDY IS THE ONE THE OTHER TWO ARCS ALREADY USE, and it needs no wire field:
    /// record 36 names the seat, in this arc's own index space, so the slab is simply not drawn
    /// (<see cref="LayoutCards"/>). It runs with the REVEAL GATE SHUT - hiding a slab resolves no
    /// widget, reads no <c>fullAbilityCard</c> and prints no face, so a face gate in front of it
    /// would be a capability test doing duty as a policy. That matters: during the card-selection
    /// phase every slab is an identical BACK, which is the whole reason the user reports the
    /// duplicate as absent there ("bei den verdeckten Karten scheint das nicht der Fall zu sein").
    /// The slab count is identical in both phases - a duplicate back is simply invisible next to
    /// seven other backs - so there is no face-down code path that was ever correct.</para>
    ///
    /// <para>WHICH OF TWO REMEDIES IS OWED IS DECIDED BY THE WIRE'S OWN TWO NUMBERS, never by a
    /// model list, so the answer is the same with the gate open and shut:</para>
    /// <list type="bullet">
    ///   <item><c>listLength == count</c> - the arc STILL CARRIES the held card (the steady state,
    ///     the one the log shows). Hide those seats here; the face list is left alone, because
    ///     slab i is still a name for card i and the positional zip is already correct.</item>
    ///   <item><c>listLength == count + heldSeats</c> - the arc has already dropped it (the one or
    ///     two frames between <c>CardFan.Remove</c> and the next rebuild). Hide NOTHING: seat k no
    ///     longer names slab k, and the existing HELD SEAT DROPPED path in <see cref="UpdateFaces"/>
    ///     is what keeps the faces aligned across that window.</item>
    ///   <item>anything else - the two numbers do not agree about one hand. Hide nothing; a
    ///     duplicate is a smaller error than a hidden wrong card.</item>
    /// </list>
    /// </summary>
    private void ResolveArcHeldSeats(int count)
    {
        _arcHeldSeatA = -1;
        _arcHeldSeatB = -1;
        _arcHeldListSeatA = -1;
        _arcHeldListSeatB = -1;
        int held = _owner.HeldHandSeats(out int seatA, out int seatB, out int listLength);
        _arcHeldListLength = listLength;
        if (held == 0)
            return;
        // TWO POSE SLOTS CANNOT NAME ONE SEAT. A receiver never assumes a sender is well-formed,
        // and a duplicate would make `held` read 2 against an arc that only lost one card - which
        // would take the wrong branch below. Collapse it and let the arithmetic judge the result.
        if (held == 2 && seatA == seatB)
        {
            seatB = -1;
            held = 1;
        }
        // …AND NOT WHILE A HAND CARD IS SEATED ON THEIR BOARD. `listLength == count` is only the
        // "arc kept the held card" state while the arc and the model list have the same MEMBERSHIP.
        // CardsDriver.FillHandFan drops a hand card the mod has seated in a recess, so during a
        // modal pick over the real hand the two lists differ by that card and seat k names slab k
        // only by luck — it is k or k-1 depending on where the seated card sits, and nothing here
        // can say which. Hiding the wrong slab hides a card the owner IS looking at, which is worse
        // than the duplicate this method exists to remove, so this refuses rather than compensates:
        // "a duplicate is a smaller error than a hidden wrong card", the same ruling as the last
        // arm below. (RemoteControlBoard.SeatedHandCardExcess is a COUNT and never a name, so it
        // can say THAT the lists differ and never WHICH card differs — which is exactly why the
        // honest move here is a refusal.)
        if (_owner.SeatedHandCardExcess != 0)
            return;
        if (listLength != count)
            return;   // the arc already dropped them (or the two numbers disagree) - see the doc

        // ─── RECORD 36 NAMES A SEAT IN THE HAND LIST, NOT IN THE ARC ───────────────────────────
        // Those were the same index until ModBuild 462, and this fan simply assumed it. They are
        // not: record 36's seat is an index into the cardsUI walk, while the slab to hide is an
        // ARC seat, and record 44 exists precisely because the owner's arc is in their own
        // drag-reorder. So where an order is stated AND validates, translate through it; where it
        // is not, fall back to the identity, which is what every earlier build did and is exactly
        // right for a fan nobody has reordered.
        //
        // THE TRANSLATION IS BELTED ON ITS OWN TERMS and does not borrow UpdateFaces' verdict: it
        // needs no model list, so it runs with the reveal gate shut, and a permutation that does
        // not validate leaves the identity standing rather than hiding a slab at a guessed seat.
        //
        // ...AND THE TRANSLATION IS THE ONE IN FORCE, NOT THE ONE ON THE WIRE (2026-09-07). This
        // read `_owner.FanArcOrder` + ValidateFanArcOrder and fell back to the identity when the
        // wire array was null. That was complete until the same round's order fix gave the receiver
        // a second way to have an order in force: TryHoldFanArcOrder HOLDS the last applied one
        // across a packet that states none, and fills the gather with _owner.FanArcOrder still NULL.
        // From that build on, every HELD frame translated by the identity and hid the slab at the
        // MODEL seat — a card the owner is looking at goes dark while the one in their fist stays
        // drawn, which is the user's own "eine andere Karte an der falschen Stelle" symptom on the
        // hiding side rather than the ordering side. AppliedArcSeatOf is true of both paths.
        //
        // NO SEPARATE VALIDATE CALL: _appliedOrder is only ever written by a gather that already
        // passed ValidateFanArcOrder (the stated path) or that resolved every latched id to a live
        // widget (the held path), and it is zeroed on every refusal. An unvalidated permutation
        // cannot reach this method.
        if (seatA >= 0 && seatA < count)
        {
            _arcHeldListSeatA = seatA;
            _arcHeldSeatA = AppliedArcSeatOf(seatA);
        }
        if (seatB >= 0 && seatB < count)
        {
            _arcHeldListSeatB = seatB;
            _arcHeldSeatB = AppliedArcSeatOf(seatB);
        }
    }

    /// <summary>
    /// WHICH ARC SEAT holds the card at hand-list index <paramref name="listSeat"/>, per the
    /// permutation that is IN FORCE (<see cref="_appliedOrder"/>). -1 when the arc does not carry it;
    /// the identity when no permutation is in force, which is not a fallback but the correct answer —
    /// with nothing to permute the fan lays the model list out in its own order, so seat k IS slab k.
    ///
    /// <para>THIS IS THE DESTINATION QUESTION AND THE HIDE QUESTION, AND THEY HAVE ONE ANSWER. The
    /// user's requirement is "Wenn man eine Karte nimmt und sie loslässt muss die Animation exakt
    /// dorthin gehen wo die Karte auch für den Spieler ist" — the order and the seat a card is drawn
    /// at are ONE requirement, so neither the return flight nor the slab-hiding may resolve a seat by
    /// an expression of its own. Record 36's seat is an index into the MODEL list; a slab index is an
    /// ARC seat; record 44 exists precisely because the owner's arc is in their own drag-reorder.
    /// This method is the only place the mod turns one into the other.</para>
    ///
    /// <para>DO NOT "SIMPLIFY" THIS BACK TO <c>_owner.FanArcOrder</c>. That was what
    /// <see cref="ResolveArcHeldSeats"/> did, and it is wrong for half the states: the wire array is
    /// only one of the two ways an order comes into force, because the receiver also HOLDS the last
    /// applied order across a packet that states none (<see cref="TryHoldFanArcOrder"/>), and a HELD
    /// order leaves <c>_owner.FanArcOrder</c> NULL. Reading the wire array is therefore right for a
    /// stated order and silently the identity for a held one — hiding a slab whose card the owner
    /// can see and leaving the plucked one drawn twice. <see cref="_appliedOrder"/> is true of both
    /// paths and is readable in both windows of the frame; see its own note for the ordering.</para>
    /// </summary>
    private int AppliedArcSeatOf(int listSeat)
    {
        if (listSeat < 0)
            return -1;
        if (_appliedOrderCount <= 0)
            return listSeat;                       // no permutation: the arc IS the model order
        for (int k = 0; k < _appliedOrderCount && k < _appliedOrder.Length; k++)
        {
            if (_appliedOrder[k] == listSeat)
                return k;
        }
        return -1;                                 // the arc does not carry it — never guess a seat
    }

    /// <summary>
    /// Re-aim a LIVE return glide at the seat the arc holds the card in THIS frame. Called once per
    /// tick inside the staged window, right behind <see cref="TrackFist"/>.
    ///
    /// <para>WHY EVERY FRAME AND NOT ONCE AT ARM TIME. The glide runs for
    /// <see cref="ReleaseGlideSeconds"/> — several packets — and since the 2026-09-07 order fix the
    /// order in force can change inside that window: the owner drags another card, or a stated order
    /// supersedes a HELD one. A seat NUMBER latched at arm time then names a different card's slab,
    /// and the flight would drag the wrong slab out of the release pose while the returning card
    /// popped. A mid-flight re-order is the case that looks worst, so it is the one this exists for.
    /// </para>
    ///
    /// <para>AND IT RETARGETS RATHER THAN ABANDONING, because that is what the OWNER does: their
    /// <c>CardFan.Relayout</c> hands every card a new home mid-motion and <c>VRCard</c>'s standing
    /// lerp simply keeps carrying it to the new one — the card never stops and never snaps. The same
    /// sentence is already written into <see cref="Rebuild"/>'s carry note one screen down. The
    /// glide is abandoned only where the arc stops carrying the card at all, which is the one state
    /// with no destination to name.</para>
    /// </summary>
    private void RetargetReturnGlide()
    {
        if (_returnGlide <= 0f || _returnListSeat < 0)
            return;
        int seat = AppliedArcSeatOf(_returnListSeat);
        if (seat < 0)
        {
            // No seat: the arc no longer carries this card (it was played, burnt, or the order stops
            // naming it). There is nowhere to fly to, and a flight to a guessed seat is worse than
            // none — the same ruling ResolveArcHeldSeats states for hiding a slab.
            _returnRetargetsDropped++;
            LogReturnRetarget(-1);
            ClearReturnGlide();
            return;
        }
        if (seat == _returnIndex)
            return;
        // The slab we were flying is not the one that holds this card any more. Bank ITS live pose
        // as the new seed so the motion continues from where the eye last saw it instead of jumping
        // back to the release point, and hand the old slab back to the standing layout lerp, which
        // eases it home rather than snapping it.
        if (!_returnSeedPending && _returnIndex >= 0 && _returnIndex < _cards.Count
            && _cards[_returnIndex] != null)
        {
            Transform live = _cards[_returnIndex]!.transform;
            _returnSeedPos = live.position;
            _returnSeedRot = live.rotation;
            _returnSeedPending = true;
        }
        int was = _returnIndex;
        _returnIndex = seat;
        _returnRetargets++;
        LogReturnRetarget(was);
    }

    /// <summary>Session count of mid-flight re-aims and of flights dropped for want of a seat — the
    /// two readings <see cref="LogReturnRetarget"/> reports.</summary>
    private int _returnRetargets;
    private int _returnRetargetsDropped;

    /// <summary>
    /// HARDWARE EVIDENCE for the order/animation seam (2026-09-07). Grep token:
    /// <c>FAN RETURN RETARGET</c>. Edge-triggered — one line per re-aim, never per frame — because a
    /// glide that is never re-aimed must cost NOTHING to leave in.
    ///
    /// <para>WORKING: zero lines in a session where nobody reordered mid-flight, and one line
    /// reading <c>seat A -&gt; B</c> for each one who did. INERT: <c>FAN RETURN VERDICT ... ARMED</c>
    /// lines with a non-identity order in force (the <c>MIRRORED ARC ORDER ... APPLIED</c> /
    /// <c>HELD</c> line beside it) and no retarget line and no <c>arcSeat</c> clause — that says this
    /// translation never ran. STILL BEYOND: no <c>ARMED</c> lines at all, which says nobody put a
    /// card back and the round proves nothing either way.</para>
    /// </summary>
    private void LogReturnRetarget(int wasSeat)
    {
        // HW-VERIFY: the order/animation seam (2026-09-07). Grep token: FAN RETURN RETARGET.
        VRLog.Note("Net", $"FAN RETURN RETARGET [player {_owner.PlayerId}]: the arc order moved under "
            + $"a live return flight — hand-list seat {_returnListSeat} "
            + (wasSeat < 0
                ? $"is no longer carried by the arc, so the flight was DROPPED rather than aimed at a "
                  + "guessed slab"
                : $"moved from arc seat {wasSeat} to {_returnIndex}, and the flight was RE-AIMED "
                  + "there, continuing from the pose the eye last saw rather than restarting")
            + $". Session: {_returnRetargets} re-aim(s), {_returnRetargetsDropped} drop(s). THE "
            + "DESTINATION IS THE FAN'S OWN: it is read off the record-44 gather CommitFanArcOrder "
            + "is about to swap in, which covers a STATED order and a HELD one alike — never off "
            + "_owner.FanArcOrder, which is null for a held order.");
    }

    /// <summary>
    /// Watch the fist for one frame: resolve what is in it, and on the frame it empties decide
    /// whether the card went into a RECESS (arm the hand-off, item 5) or back to the FAN (start the
    /// mirrored return glide, item 2). See the block above for why each term is here.
    /// </summary>
    /// <param name="armed">Whether the front path resolved a hand list this frame. With the reveal
    /// gate shut there is no <see cref="_handBuffer"/> to name a seat in, so no IDENTITY is
    /// resolved and the RECESS HAND-OFF does not arm — and nothing should: a recess filling during
    /// the secret selection phase draws a back on every client by RevealGate's ruling, which this
    /// must not and does not widen.
    ///
    /// <para>IT NO LONGER GATES THE MOTION, and that is report item 3 of 2026-09-06. The
    /// return-to-fan glide moves an anonymous BACK slab from a pose to an arc index; it resolves no
    /// widget, reads no <c>fullAbilityCard</c> and prints no face, so a face gate standing in front
    /// of it was a capability test doing duty as a policy. It cost the whole card-SELECTION phase,
    /// which is the one phase in which a player fans their hand out and puts cards back — the exact
    /// case the user reported twice. The precedent is one file over: the fan EXCHANGE animation
    /// deliberately plays inside the same secret window off the RAW record-22 id, drawing
    /// backs.</para></param>
    /// <param name="actor">The character this fan is DISPLAYING, resolved once per tick by the
    /// caller and handed in — the same answer <see cref="UpdateFaces"/> resolved the arc from, so
    /// the seat and the character can never be about two different hands.</param>
    /// <param name="count">The owner's own arc SLAB COUNT off the wire — the term that separates a
    /// card released back into the fan from one that went to a pile.</param>
    private void TrackFist(bool armed, CPlayerActor? actor, int count)
    {
        _bankArcCount = count;
        int mask = _owner.SlotOccupancyKnown ? _owner.BoardSlotMask : 0;
        if (!_seenMaskValid)
        {
            _seenMaskValid = true;
            _seenMask = mask;
        }
        int gainedSinceFrame = mask & ~_seenMask;
        _seenMask = mask;
        if (gainedSinceFrame != 0)
        {
            _handoffGains += SlotBits(gainedSinceFrame);
            // PER RECESS, not just the total. A standing hand-off is superseded only by a later
            // arrival into ITS OWN recess; an arrival into the other one says nothing about it, and
            // retiring on the total would drop a good hand-off every time the peer filled their
            // second recess.
            for (int i = 0; i < 2; i++)
                if ((gainedSinceFrame & (1 << i)) != 0)
                    _recessGains[i]++;
        }
        // ASKED ON EVERY ARRIVAL, ARMED OR NOT. The arming branch below is reached only when a
        // tracked release pairs with the arrival; an arrival the fist was never resolved for is
        // precisely the INERT case the line has to be able to report, so the call cannot live
        // inside the branch that only runs when it worked. Change-gated on the PAIR inside, so a
        // settled board still costs nothing.
        LogHandoff();

        float now = Time.unscaledTime;

        // A hand-off is about ONE character's recesses; a board that has switched focus must not
        // inherit one. Same reset RemoteControlBoard._latchedActor exists for.
        if (!ReferenceEquals(actor, _fistActor))
        {
            _fistActor = actor;
            _fistCard = null;
            _fistSeat = -1;
            _fistHeldAt = 0f;
            ClearHandoff();
        }

        // ─── WHAT IS IN THE FIST ────────────────────────────────────────────────────────────────
        // "STILL HOLDING" IS THE WIRE'S ANSWER, NOT OURS, and the two are asked separately on
        // purpose. A resolve that fails — the reveal gate shut mid-hold, the two copies of the hand
        // list disagreeing in length for a frame — would otherwise read exactly like the peer
        // opening their fingers, and the release branch below would fire while the card is still
        // physically in their hand. So the record's own "a hand card is held" verdict gates the
        // branch, and the RESOLVE only decides whether we can name what is in there.
        bool named = _owner.SingleHeldHandSeat(out int seat, out int listLength, out int poseSlot,
                                              out byte listId);

        // THE RELEASE EDGE, COUNTED OFF THE WIRE AND NOTHING ELSE — the denominator of the verdict
        // line. Read before every belt below, because a belt that refuses is precisely what this
        // has to be able to say happened.
        if (_fistNamedLast && !named)
        {
            _releaseEdges++;
            // PENDING, NOT RESOLVED. The verdict is not owed on this frame: the branches below
            // deliberately fall through and RETRY for HandoffGraceSeconds, because the arc count
            // and the held flag ride the same packet only in the normal case. Resolving on the edge
            // frame would refuse every release whose two facts arrive one frame apart — which is
            // the grace window's entire reason for existing.
            _releasePending = true;
        }
        _fistNamedLast = named;

        // ─── THE MOTION BELT IS THE SENDER'S OWN TWO NUMBERS, AND IT IS NOT THE IDENTITY BELT ───
        // While a hand card is in the peer's fist their hand LIST still holds it (the card has not
        // been played) and their fan ARC does not (CardFan.Remove ran on the pluck). So the sender's
        // own record-36 list length and their own HandCardCount byte have to read
        // `listLength == count + 1` — two values off ONE packet, neither of which is this client's
        // model and neither of which asks a reveal gate. Verified against the 2026-09-06 host log:
        // every 'Remote held card FRONT ... hand fan seat k of L' line sits one line from a
        // 'geometry: n=L-1' line, for all five hand plucks in that session.
        //
        // That belt is what makes the seat safe to use as an ARC INDEX with the gate shut, and an
        // arc index is all the return glide ever wanted.
        // ...AND IT HAS TWO LEGAL READINGS, NOT ONE. THE SENTENCE ABOVE WAS FALSE FOR THE STEADY
        // STATE: CardsDriver.Rebuild puts the plucked card straight back into the arc (see
        // ResolveArcHeldSeats for the n=8/n=7/n=8 reading that convicts it), so
        // `listLength == count + 1` is only true in the one or two frames between CardFan.Remove
        // and that rebuild; the state the peer is actually looked at for the length of the hold is
        // `listLength == count`. Both answer the same question with the same number: the card comes
        // home to ARC INDEX `seat` either way.
        //
        // ─── AND THE ARC IS ALSO SHORT BY ANY HAND CARD LYING ON THEIR BOARD ────────────────────
        // TWO LANES GAVE THE SAME EIGHT REFUSALS TWO CAUSES, AND THE SENDER'S OWN `Fan order
        // [scenario hand] n` SEQUENCE SEPARATES THEM BY DWELL TIME. Both are real; neither is the
        // whole cause:
        //
        //   * PLUCK OSCILLATION (the reading above). Peer log, selection phase: n drops 8 -> 7 and
        //     is back at 8 within 10 to 30 log lines — 85298/85396, 99748/99766, 100700/100725,
        //     101379/101401, 109635/109649, 117799/117809, 119048/119078, 119724/119735. Nine
        //     blips, none of them lasting. SEVEN of the session's eight seatUsable refusals sit in
        //     exactly that stretch (host 108785, 109514, 110204, 117901, 125781, 126831, 127402).
        //
        //   * A HAND CARD SEATED ON THE BOARD (this term). Peer log, inside the
        //     `Pick fan source (LoseCard): real hand` window it opens at 157886 and closes at
        //     179525: n drops to 7 and STAYS there for 358, 1625, 2966 and once 14493 log lines
        //     (159292 -> 173785). Three orders of magnitude longer, because CardsDriver.FillHandFan
        //     drops a hand card the mod has seated in a recess — the `_halfBuffer.Contains(already)`
        //     arm, a term of the owner's fan SIZE that CardsGameApi.HandFanMember does not carry
        //     even though its own doc called that predicate the whole of the wire contract. The
        //     REMAINING refusal (host 184623, seat 4) is in this window, and the host's census reads
        //     `LENGTH BELT: 8 model card(s) vs 7 slab(s)` in ALL 11 intervals of it: eleven samples
        //     ten seconds apart cannot all land inside a two-frame blip, so that reading is the
        //     standing state and the oscillation cannot explain it.
        //
        // WHY THE SEATED TERM IS MORE NECESSARY WITH THE TWO READINGS, NOT LESS. Take the moment of
        // refusal 184623: one card in the fist AND one seated, so listLength(8) == count + 1 + 1 and
        // count is 6. `arcKeepsHeld` reads 8 == 6, false; `count + 1` reads 8 == 7, false — still
        // refused, so widening alone does not reach it. And in the neighbouring frames where the arc
        // KEPT the held card, count is 7 and `listLength == count + 1` reads 8 == 8 and ACCEPTS —
        // for the wrong reason. It would classify "arc kept the held card, one seated" as "arc
        // dropped the held card", ResolveArcHeldSeats would then not hide the duplicate slab, and
        // the HELD SEAT DROPPED path would remove a seat that IS still in the arc: every face after
        // it shifted by one, drawn confidently. That is the exact failure both belts exist to
        // prevent, so the seated term is a safety here and not an extra.
        //
        // ─── THE BELT IS SPLIT BY CONSUMER, WHICH IS WHAT NEITHER LANE HAD ──────────────────────
        // The two things a record-36 seat is used for are indexes into DIFFERENT lists, and lumping
        // them is what jammed the hand-off:
        //
        //   * AS A NAME it indexes _handBuffer — this client's own HandFanMember list, length
        //     `listLength`. That is exact whatever the ARC is doing, so no arc arithmetic belongs in
        //     front of it. Gating it on the arc belt is why 6 releases into a recess armed ZERO
        //     hand-offs: the name was thrown away for an arc mismatch it never depended on.
        //   * AS AN ARC INDEX (the return glide, and the slab-hiding in ResolveArcHeldSeats) it
        //     indexes the owner's wire arc, and that is only the same index space while the arc and
        //     the model list have the same MEMBERSHIP. A seated card breaks that: seat k maps to
        //     arc index k or k-1 depending on where the seated card sits, and nothing here can say
        //     which. So the arc reading is REFUSED outright when anything is seated, rather than
        //     arithmetically compensated — a refusal costs a return animation, a wrong arc index
        //     puts a card home on somebody else's seat.
        int seatedOnBoard = _owner.SeatedHandCardExcess;
        bool seatInRange = named && seat >= 0 && seat < listLength;
        bool arcKeepsHeld = seatInRange && seatedOnBoard == 0 && listLength == count;
        bool arcSeatUsable = seatInRange && seatedOnBoard == 0
                             && (listLength == count + 1 || arcKeepsHeld);
        // THE NAME NEEDS NO ARC TERM AT ALL — only that the two copies of the LIST are the same
        // length, which is the identical test RemoteHeldCardFace makes before it draws this very
        // card's own front. Without it a lagging model would name the card beside the right one.
        bool nameUsable = seatInRange && listLength == _handBuffer.Count
                          && seat < _handBuffer.Count;

        CAbilityCard? fist = null;
        if (nameUsable && armed)
        {
            AbilityCardUI? w = _handBuffer[seat];
            fist = w != null ? w.AbilityCard : null;
        }
        // ─── WHY THE FIST HAS NO NAME, BANKED WHERE IT IS DECIDED (2026-09-07 items 5a/5b) ──────
        // The RECESS HAND-OFF's own NOT-ARMED line told its reader to "read the FAN RETURN VERDICT
        // line beside this one, and if it says term=seatUsable ...". That clause is NEVER EMITTED on
        // the recess path: the arming branch below resolves with RefusalTerm.None by construction
        // ("a card laid on the board owes no fan flight either way"). Measured on ModBuild 476 —
        // every one of the 9 host and 8 peer occurrences of the token `term=` in those two logs is
        // this very line QUOTING it, and not one is a measurement. So a mechanism that armed 1 of 15
        // recess arrivals across a two-hour session could not say which of its four terms refused,
        // and two rounds of hardware testing had nothing to read. It is banked here instead, on the
        // frame the name is actually decided, and printed verbatim by LogHandoff.
        if (named)
            _fistNameMiss =
                fist != null ? string.Empty
              : !armed ? "armed — the front path resolved no hand list this frame (the reveal gate "
                         + "is shut for this character, or the fan is drawing the map loadout), so "
                         + "there was no buffer to take a name out of. Inside the secret selection "
                         + "window this is CORRECT and nothing is owed"
              : !seatInRange ? $"seatInRange — record 36 named seat {seat} of a {listLength}-card "
                               + "list, which is not a seat in that list at all"
              : listLength != _handBuffer.Count
                    ? $"nameUsable — the two copies of that list disagree in LENGTH: the owner says "
                      + $"{listLength}, this client resolves {_handBuffer.Count}. A positional seat "
                      + "is only a name while both machines build the list with the same expression, "
                      + "so this refuses rather than naming the card beside the right one"
              : $"the widget at seat {seat} carried no AbilityCard";

        if (named)
        {
            // Their fist has filled again while a release was still waiting for its verdict. Close
            // the old one rather than dropping it: every edge counted has to end in exactly one of
            // the three buckets or the line's own arithmetic stops being checkable.
            ResolveRelease(armed: false, recess: false, RefusalTerm.Regrabbed);
            if (!nameUsable && !arcSeatUsable)
            {
                // A card IS in their fist and NOTHING about it is usable — neither the name nor the
                // arc index. Not a release: forget everything so nothing stale can be handed over or
                // flown, keep the mask moving, and let the next frame try again. A frame where only
                // ONE of the two is usable no longer lands here: the name and the arc index are
                // independent facts and refusing both because one failed is what left six recess
                // arrivals unnamed.
                _fistCard = null;
                _fistSeat = -1;
                _fistMask = mask;
                Bank(RefusalTerm.SeatBelt, seat, listLength);
                ExpireHandoff(mask, now);
                return;
            }
            // Still holding: refresh BOTH memories and the two baselines the release edge is
            // measured against. A new card in the fist also retires any standing hand-off — one
            // fist, one event. `fist` may be null here (the reveal gate is shut, or this client's
            // hand list is a beat behind): that costs the hand-off, which needs a name, and costs
            // the return glide nothing, which needs only the seat.
            if (!ReferenceEquals(fist, _fistCard))
                ClearHandoff();
            _fistCard = fist;
            _fistSeat = seat;
            _fistPoseSlot = poseSlot;
            _fistListId = listId;
            _fistMask = mask;
            _fistCount = count;
            // WHICH OF THE TWO ARC STATES THIS HOLD IS IN, banked with the count it is measured
            // against, because the RELEASE test below is a different arithmetic for each: an arc
            // that kept the seat does not grow when the card comes home, it simply stops being
            // hidden.
            _fistArcKeptHeld = arcKeepsHeld;
            // …and whether the seat is an ARC index at all this hold. The return glide asks this
            // rather than assuming _fistSeat is one, because a seated card makes the seat a valid
            // NAME and an invalid arc index in the same frame.
            _fistArcSeatUsable = arcSeatUsable;
            _fistHeldAt = now;
            // A belt that failed earlier in THIS hold and has since recovered must not be quoted as
            // the reason a later release was refused. The banked term describes the frame it was
            // banked on, so a frame that resolves cleanly retires it.
            _refusal = RefusalTerm.None;
            return;
        }

        if (_fistSeat < 0)
        {
            ResolveRelease(armed: false, recess: false,
                           _refusal != RefusalTerm.None ? _refusal : RefusalTerm.NeverTracked);
            ExpireHandoff(mask, now);
            return;
        }

        // ─── THE FIST HAS EMPTIED ───────────────────────────────────────────────────────────────
        if (now - _fistHeldAt > HandoffGraceSeconds)
        {
            // The grace ran out with no verdict: the card went somewhere this client cannot see
            // (a pile, a swap, a teardown). Forget it rather than pair it with the next thing that
            // happens to move.
            _fistCard = null;
            _fistSeat = -1;
            ResolveRelease(armed: false, recess: false,
                           _refusal != RefusalTerm.None ? _refusal : RefusalTerm.GraceExpired);
            ExpireHandoff(mask, now);
            return;
        }

        int gained = mask & ~_fistMask;
        int lost = _fistMask & ~mask;
        if (gained != 0 && lost == 0 && SlotBits(gained) == 1)
        {
            // INTO A RECESS (item 5) — and this branch is the one that genuinely needs a NAME, so
            // it is the one that keeps the identity gate. A card laid on the board owes no fan
            // flight either way, so a nameless hand-over is counted as a correct non-flight rather
            // than as a refusal: what this verdict line is read against is releases that OWED a
            // flight.
            ResolveRelease(armed: false, recess: true, RefusalTerm.None);
            if (_fistCard != null)
            {
                _handoffRecess = (gained & 1) != 0 ? 0 : 1;
                HandoffCard = _fistCard;
                _handoffGainEpoch = _recessGains[_handoffRecess];
                _handoffArmed++;
                LogHandoff();   // the ARMED edge; the arrival edge above may have printed already
            }
            _fistCard = null;
            _fistSeat = -1;
            return;
        }

        // WHERE THE ARC MUST BE FOR THIS TO BE A CARD COMING HOME. An arc that carried the held
        // card all along is unchanged; an arc that had dropped it grows back by the one slab it
        // lost. Either way a card that went to a PILE leaves the arc one slab SHORT of this, which
        // is the discrimination this term exists for and which survives the widening intact.
        int arcHome = _fistArcKeptHeld ? _fistCount : _fistCount + 1;
        if (mask != _fistMask || count != arcHome || _closeElapsed >= 0f)
        {
            // NAME THE TERM THAT IS REFUSING — but do NOT resolve on it, and do NOT forget the
            // fist. This is the frame-ordering seam the grace window exists for: the wire arc may
            // still be a packet behind the held flag. The term is BANKED and the grace branch above
            // prints it if the retry never succeeds, so a release that is genuinely never flown
            // still says which of the three expressions was standing in the way.
            //
            // Three can, and they mean three different things: the recess mask moved in a shape
            // this client cannot pair, the arc did NOT grow back by the one slab the pluck took (a
            // burn or a discard, which correctly owes no flight to a seat that is not there), or
            // the fan is folding away and has no arc seat to fly to.
            if (mask != _fistMask)
                Bank(RefusalTerm.RecessMask, _fistMask, mask);
            else if (count != arcHome)
                Bank(RefusalTerm.ArcGrowth, _fistCount, count);
            else
                Bank(RefusalTerm.FanClosing, 0, 0);
            ExpireHandoff(mask, now);
            return;
        }

        // BACK TO THE FAN (item 2, and item 3 of 2026-09-06) — no recess changed AND the owner's own
        // arc grew back by exactly the one slab it lost when they plucked the card out, so the card
        // was released into the void and CardFan.Add is carrying it home to its authored seat right
        // now. The count term is what tells this apart from a card that went to a PILE: a burn or a
        // discard leaves the arc one slab SHORT and must not be mirrored as a flight to a seat that
        // is not there. (…and not while the fan is FOLDING AWAY: the collapse path hands this method
        // `_cards.Count` instead of the wire count, so that equality can be satisfied by the fold
        // rather than by a card coming home, and a collapsing fan has no arc seat to fly to anyway.)
        // …AND ONLY IF THE SEAT IS AN ARC INDEX. See the split above: with a hand card seated on
        // their board the arc and the model list have different membership, so this seat names a
        // card correctly and names a SLAB incorrectly. Refuse the motion, keep the name.
        // THE SEAT THE FLIGHT LANDS ON IS AN ARC SEAT, NOT A MODEL SEAT. _fistSeat is record 36's
        // index into the hand LIST; _cards is the ARC. They are the same number only while the
        // applied order is the identity, and since the 2026-09-07 order fix a non-identity order is
        // in force far more of the time (it is stated on every describable packet and HELD across
        // packets that state none). Translating here — through the very gather CommitFanArcOrder is
        // about to swap in — is what makes the user's requirement true: "die Animation muss exakt
        // dorthin gehen wo die Karte auch fuer den Spieler ist". A -1 means the arc does not carry
        // this card, and then no flight is owed.
        int arcSeat = AppliedArcSeatOf(_fistSeat);
        bool flew = _fistArcSeatUsable && arcSeat >= 0
                    && BeginReturnGlide(arcSeat, _fistPoseSlot, _fistSeat);
        ResolveRelease(flew, recess: false, _refusal);
        _fistCard = null;
        _fistSeat = -1;
    }

    /// <summary>
    /// Close the ONE pending release edge with the bucket it belongs in, and print its verdict.
    /// Idempotent: a frame that resolves nothing costs a bool test.
    ///
    /// <para>EVERY EDGE ENDS IN EXACTLY ONE BUCKET, which is what makes <c>armed + refused +
    /// recess == release(s)</c> a checkable claim rather than three unrelated counters. A reader
    /// who finds the sum short has found a path out of <see cref="TrackFist"/> that drops a release
    /// silently — the same class of hole this whole instrument was written for.</para>
    /// </summary>
    private void ResolveRelease(bool armed, bool recess, RefusalTerm term)
    {
        if (!_releasePending)
            return;
        _releasePending = false;
        if (!armed)
        {
            if (recess)
                _releaseToRecess++;
            else
                _releaseRefused++;
        }
        _refusal = term;
        LogReturnVerdict(armed, recess);
        _refusal = RefusalTerm.None;
    }

    /// <summary>Bank the term that is refusing this release, with the two numbers its sentence
    /// quotes. Three field writes, no allocation — see <see cref="_refusal"/> for why that
    /// matters on this path.</summary>
    private void Bank(RefusalTerm term, int a, int b)
    {
        _refusal = term;
        _refusalA = a;
        _refusalB = b;
        _refusalC = _owner.SeatedHandCardExcess;
        _refusalD = _bankArcCount;
    }

    /// <summary>The banked refusal as the sentence the log prints. Called once per release, never
    /// per frame.</summary>
    private string RefusalSentence() => _refusal switch
    {
        RefusalTerm.SeatBelt =>
            $"seatUsable — record 36 named hand seat {_refusalA} of a {_refusalB}-card list while "
            + $"their wire arc carried {_refusalD} slab(s) — THE ARC COUNT IS THE NUMBER THIS LINE "
            + "USED TO OMIT, and omitting it is why two lanes read the same eight refusals as two "
            + "different defects: list minus arc equal to 1 is a pluck the rebuild has not undone "
            + "yet, 0 is the steady state where the arc kept the held card, and 2 or more means a "
            + "card is also lying on their board. "
            + "the owner's own wire arc did not carry one slab fewer per card out of their fan "
            + $"(fist + {_refusalC} hand card(s) this client can see lying in their recesses), so "
            + "it could not tell which arc index the card would come home to. THE SEATED TERM IS "
            + "THE ONE ADDED FOR REPORT ITEM 6: if this line still fires with that number reading "
            + "0 while a card is visibly on their board, the excess is not being seen and "
            + "RemoteControlBoard.SeatedHandCardExcess is the place to look, not this belt",
        RefusalTerm.NeverTracked =>
            "seatUsable — no seat was tracked at any point while the card was in their fist, so "
            + "there was nothing to fly. On ModBuild 459 a shut RevealGate landed here for "
            + "every release in the card-selection phase; if it still does, the identity/motion "
            + "split did not take",
        RefusalTerm.RecessMask =>
            $"recess mask — it read {_refusalA} while the card was held and reads {_refusalB} now, "
            + "so the card did not simply go back into the fan",
        RefusalTerm.ArcGrowth =>
            $"arc growth — the owner's wire arc carried {_refusalA} slab(s) while the card was held "
            + $"and carries {_refusalB} now, not {(_fistArcKeptHeld ? _refusalA : _refusalA + 1)} "
            + $"(that arc {(_fistArcKeptHeld ? "KEPT the held card's seat, so a homecoming does not "
                                              + "grow it" : "had already dropped the held card, so a "
                                              + "homecoming grows it by one")}). This is the CORRECT refusal for a "
            + "card that went to a pile: a burn or a discard leaves the arc one slab short and "
            + "there is no seat to fly to",
        RefusalTerm.FanClosing =>
            "_closeElapsed — the fan is folding away, so it has no arc seat to fly to and the "
            + "collapse is the animation the owner is watching instead",
        RefusalTerm.NoHeldSlab =>
            $"RemoteAvatar.HeldSlab({_refusalA}) — this peer has no mirrored held-card slab, so "
            + "there is no released pose to seed the glide from",
        RefusalTerm.Regrabbed =>
            "their fist filled again before this release could be judged",
        RefusalTerm.GraceExpired =>
            $"HandoffGraceSeconds — the {HandoffGraceSeconds:F2}s retry window ran out with no "
            + "recess gain and no arc growth, so the card went somewhere this client cannot see",
        _ => "unnamed — and a refusal that names no term is the defect, not the reading",
    };

    /// <summary>Population of a two-bit recess mask — the recesses are two, so this is two tests.
    /// </summary>
    private static int SlotBits(int mask) => ((mask & 1) != 0 ? 1 : 0) + ((mask & 2) != 0 ? 1 : 0);

    /// <summary>Drop an armed hand-off whose recess has emptied, or which a LATER arrival into that
    /// same recess has superseded. Both are edges; see <see cref="_handoffGainEpoch"/> for why the
    /// wall clock that used to stand here was the defect rather than the safety.</summary>
    private void ExpireHandoff(int mask, float now)
    {
        if (HandoffCard == null)
            return;
        bool occupied = _handoffRecess >= 0 && (mask & (1 << _handoffRecess)) != 0;
        if (!occupied || _recessGains[_handoffRecess] != _handoffGainEpoch)
            ClearHandoff();
    }

    /// <summary>
    /// Forget the armed hand-off. Called by <c>RemoteControlBoard.SeatSlots</c> the instant this
    /// client's own model can name the recesses again — the hand-off is a stand-in for a fact that
    /// has not arrived, and the moment it arrives the walk owns the picture and this must get out
    /// of its way. THAT, and not the backstop, is the normal exit.
    /// </summary>
    internal void ClearHandoff()
    {
        HandoffCard = null;
        _handoffRecess = -1;
        _handoffGainEpoch = -1;
    }

    /// <summary>
    /// THE HAND-OFF'S SAFETY, DRIVEN BY THE BOARD INSTEAD OF BY THE FAN — drop an armed hand-off
    /// whose recess is no longer occupied on the owner's own board. Called from
    /// <c>RemoteControlBoard.SeatSlots</c> with the wire's occupancy nibble, every board pass.
    ///
    /// <para>WHY IT CANNOT LIVE IN <see cref="ExpireHandoff"/> ALONE, which is the same test one
    /// caller over. That one runs inside <see cref="TrackFist"/>, i.e. inside
    /// <see cref="UpdateFaces"/>, i.e. inside <see cref="Tick"/> — and <see cref="Tick"/> returns
    /// early, before any of them, the moment the owner's arc is empty and no slab is left
    /// (`count == 0 &amp;&amp; _leaving.Count == 0 &amp;&amp; _cards.Count == 0` → <see cref="Hide"/>).
    /// A peer who lays their picked card down and then closes their fan therefore froze the whole
    /// mechanism: nothing retired the hand-off either. Keeping the memory alive across a closed fan
    /// (which is what report items 5a/5b need) without this would be trading an anonymous back for
    /// a STALE FRONT — a face from an earlier pick drawn confidently in a recess that has since
    /// been refilled, which is the one failure this project ranks above every missing picture.
    /// So the memory now outlives the fan and its expiry no longer depends on the fan at all.</para>
    ///
    /// <para>IT ONLY EVER CLEARS. It touches no counter and no epoch, so it cannot race
    /// <see cref="TrackFist"/>'s arming arithmetic in a frame where both run: the arrival counting
    /// and the epoch stamp stay exactly where they were, and this is a pure refusal on top.</para>
    /// </summary>
    internal void ExpireHandoffAgainst(int occupancyMask)
    {
        if (HandoffCard == null)
            return;
        if (_handoffRecess < 0 || (occupancyMask & (1 << _handoffRecess)) == 0)
            ClearHandoff();
    }

    /// <summary>The card this client saw handed into round recess <paramref name="recess"/>, or
    /// null. Read by <c>RemoteControlBoard.SeatSlots</c> only where its own walk and its own face
    /// latch have both come up empty, so a resolved model always wins.</summary>
    internal CAbilityCard? HandoffFor(int recess)
        => recess >= 0 && recess == _handoffRecess
           && _recessGains[_handoffRecess] == _handoffGainEpoch
            ? HandoffCard
            : null;

    /// <summary>
    /// Seed the mirrored return-to-fan glide at the pose the card was RELEASED at (report item 2).
    /// The mirror of the owner's own seam: <c>VRCard.OnRelease</c> keeps the card's world pose
    /// across the re-parent and <c>CardFan.Add</c> asks <c>Relayout(instant: false)</c> to carry it
    /// to its arc slot on the standing home-lerp — so the slab starts exactly where their card was
    /// hanging and eases in on THEIR rate, never a tween of our own invention.
    /// </summary>
    private bool BeginReturnGlide(int seat, int poseSlot, int listSeat)
    {
        Transform? slab = _owner.HeldSlab(poseSlot);
        if (slab == null)
        {
            // The one refusal left, and it used to be the silent one: this peer's held-card slab
            // has never been built, so there is no pose to fly FROM. RemoteAvatar builds it lazily
            // on the first frame the wire says they hold something, so this can only be a peer
            // whose hold this client never saw at all.
            Bank(RefusalTerm.NoHeldSlab, poseSlot, 0);
            return false;
        }
        _returnIndex = seat;
        _returnListSeat = listSeat; // the key RetargetReturnGlide re-resolves the seat from
        _returnGlide = ReleaseGlideSeconds;
        _returnSeedPending = true;
        _returnSeedPos = slab.position;
        _returnSeedRot = slab.rotation;
        // STAMPED NOW WHERE THE SLAB ALREADY EXISTS. This runs inside UpdateFaces, i.e. AFTER
        // LayoutCards has already laid this frame's slabs out, so leaving the seed for the next
        // frame would present the returning card at its arc seat for one frame and then snap it
        // back to the release pose — a visible flick, and exactly the pop this fixes. Where the
        // slab does not exist yet (the wire count has not grown), the pending flag carries the seed
        // to the first frame that has one.
        if (seat < _cards.Count && _cards[seat] != null)
        {
            Transform t = _cards[seat].transform;
            t.position = _returnSeedPos;
            t.rotation = _returnSeedRot;
            _returnSeedPending = false;
        }
        _returnsPlayed++;
        LogReturnGlide();
        return true;
    }

    /// <summary>
    /// HARDWARE EVIDENCE for report item 3 (2026-09-06). Grep token: FAN RETURN VERDICT.
    ///
    /// <para>ONE LINE PER RELEASE, on the OBSERVER, whether or not a flight was armed — which is
    /// the whole reason it exists. Every refusal on this path used to be silent, so a log with no
    /// <c>FAN RETURN FLIGHT</c> line in it could not be told apart from a session in which nobody
    /// ever put a card back, and that ambiguity is what let the defect survive a shipped fix: the
    /// 2026-09-06 host log contains 4 <c>FAN RETURN FLIGHT</c> lines against 11 arc dip-and-return
    /// events, and nothing anywhere said what refused the other 7.</para>
    ///
    /// <para>READ IT LIKE THIS. The line carries three session counts —
    /// <c>armed N, refused R, to a recess C, of M release(s)</c> — and the arithmetic is
    /// <c>N + R + C == M</c>:</para>
    /// <list type="bullet">
    ///   <item><c>M == 0</c> — the wire never once said this peer let go of a hand card. The round
    ///     says NOTHING about item 3, and the silence of <c>FAN RETURN FLIGHT</c> proves nothing.
    ///     M is counted off <c>RemoteAvatar.SingleHeldHandSeat</c> alone, before every belt and
    ///     every reveal gate, precisely so it can be non-zero on a client that refuses everything.
    ///     </item>
    ///   <item><c>M &gt; 0</c> and <c>N == 0</c> — the fix is INERT. The <c>term=</c> clause names
    ///     the expression that refused the most recent one; that literal is the next thing to
    ///     read, not this count.</item>
    ///   <item><c>R &gt; 0</c> with <c>term=arc growth</c> — those releases went to a PILE, not to
    ///     the fan, and owed no flight. They are refusals of the right kind.</item>
    ///   <item><c>N &gt; 0</c> and the user still sees the card pop — the glide ARMED and was
    ///     overwritten. Read <c>FAN RETURN FLIGHT</c> beside the fan's own geometry line for a
    ///     count change inside the same 0.35 s, which now re-seeds the glide onto the rebuilt slab
    ///     rather than losing it.</item>
    /// </list>
    ///
    /// <para>AND THE SECOND CLAUSE ANSWERS THE OTHER SLABS, which is a different question with a
    /// different failure. <c>arc reflow: E of B slab(s) carried</c> is THIS release's rebuild: B is
    /// how many slabs it built, E is how many of them were handed the pose of the slab that stood
    /// for the same card a frame earlier and therefore EASED to their new seat. The rest were born
    /// at the seat and teleported to it.</para>
    /// <list type="bullet">
    ///   <item><c>E == B - 1</c> on a release (the returning card is seeded from the FIST, not from
    ///     the old arc, so it is never one of the carried) — the whole arc reflowed. This is the
    ///     working reading.</item>
    ///   <item><c>E == 0</c> with <c>B &gt; 0</c> — the arc TELEPORTED and only the returning card
    ///     moved. That is precisely "the fix worked and the symptom stayed": <c>armed</c> will read
    ///     healthy while the picture is still wrong, and it is the one combination that would
    ///     otherwise look like success. The key is record 36's seat; a pluck whose seat could not be
    ///     belted lands here.</item>
    ///   <item>Session <c>eased/snapped</c> beside them is every rebuild, not just releases — a
    ///     draw, a burn and a character exchange have no key and correctly snap, so this pair is
    ///     expected to carry a healthy snapped count. Compare the PER-RELEASE numbers, not the
    ///     session ones.</item>
    /// </list>
    ///
    /// <para>THE VALUE THAT CONVICTS THIS FIX is <c>N == 0 with M &gt; 0</c> on a round in which the
    /// user reports having put cards back during the CARD-SELECTION phase. That is the exact case
    /// the identity/motion split was made for, and if it still reads that way the split did not
    /// take. THE VALUE THAT CONVICTS THE REFLOW is <c>E == 0</c> on a release whose <c>armed</c>
    /// climbed.</para>
    /// </summary>
    private void LogReturnVerdict(bool armed, bool recess = false)
    {
        if (_loggedVerdictEdge == _releaseEdges)
            return;
        _loggedVerdictEdge = _releaseEdges;
        string verdict = armed
            ? "ARMED"
            : recess
                ? "no flight owed (the card went into a ROUND RECESS)"
                : "REFUSED, term=" + RefusalSentence();
        // HW-VERIFY: report item 3 (2026-09-06), fan NAMED for item 1 (2026-09-07). Grep token:
        // FAN RETURN VERDICT.
        VRLog.Note("Net", $"FAN RETURN VERDICT [player {_owner.PlayerId}] fan={RemoteAvatar.HeldFaceListName(_fistListId)}: "
            + $"this peer let go of a "
            + $"hand card and the mirrored return flight was {verdict}. Session: armed "
            + $"{_returnsPlayed}, refused {_releaseRefused}, to a recess {_releaseToRecess}, of "
            + $"{_releaseEdges} release(s) the WIRE reported — the three add up to the fourth. THE "
            + "DENOMINATOR IS GATE-FREE on purpose: it counts record 36's held-hand-seat going "
            + "away, before the reveal gate, before the length belt and before any destination "
            + "test, so a client that refuses every flight still says how many it refused. 0 "
            + "release(s) means nobody put a card back and the round proves nothing; release(s) "
            + "with 0 armed and 0 to a recess means this is INERT and the term above is the "
            + "blocker. NOTHING NEW IS ON THE WIRE for any of it: the seat is record 36's, the "
            + "seed pose is the mirrored held slab's own last pose, and no card identity is read "
            + "or drawn on this path — which is why it now runs with the reveal gate SHUT. "
            + (Time.unscaledTime - _lastRebuildAt <= HandoffGraceSeconds + ReleaseGlideSeconds
                ? $"ARC REFLOW on this release: {_lastCarried} of {_lastBuilt} slab(s) carried, so "
                  + $"{Mathf.Max(0, _lastBuilt - _lastCarried)} were born at their seat and "
                  + "TELEPORTED to it; "
                : "ARC REFLOW on this release: the arc never rebuilt, so no slab moved seat at all "
                  + "and there was nothing to reflow — which for a REFUSED release is the expected "
                  + "reading and not a second defect; ")
            + $"session {_reflowEased} eased / {_reflowSnapped} snapped. The owner's own "
            + "CardFan.Add hands EVERY card a new home and VRCard's standing lerp glides all of "
            + "them, so a release that carried none of them is the 1:1 breach even when the "
            + "returning card flew: read that number and not this line's 'armed' when the user "
            + "says the fan still pops. A rebuild with no record-36 seat to key on (a draw, a "
            + "burn, a character exchange) carries nothing BY DESIGN and is the reason the session "
            + "pair is expected to show snaps.");
    }

    /// <summary>Abandon a return glide (fan closed, rebuilt, or the slab count moved under it).
    /// </summary>
    private void ClearReturnGlide()
    {
        _returnIndex = -1;
        _returnListSeat = -1;
        _returnGlide = 0f;
        _returnSeedPending = false;
    }

    /// <summary>
    /// HARDWARE EVIDENCE for report item 5. Grep token: RECESS HAND-OFF.
    ///
    /// <para>READ IT LIKE THIS. The line carries TWO session counts, and the pair is the whole
    /// point: <c>armed N of M recess arrival(s)</c>. M counts every time a recess GAINED a card
    /// this session, whether or not anything could be named for it, so:</para>
    /// <list type="bullet">
    ///   <item>M == 0 — no card was ever placed in a recess. The round says NOTHING about item 5,
    ///     and 'ROUND SLOT COMPACTION REFUSED' going quiet means only that nobody played a card.
    ///     This is the falsifier the brief asked for, and it is why the census counts ARRIVALS
    ///     rather than refusals.</item>
    ///   <item>M &gt; 0 and N == 0 — cards were placed and not one could be named. The fix is
    ///     INERT: read the 'PEER CARD FACE CENSUS' beside it for whether the reveal gate was even
    ///     open, then record 36's own 'Remote held card FRONT' line for whether the fist was ever
    ///     named (a hand seat with a matching list length is the only thing that arms this).</item>
    ///   <item>M &gt; 0 and N == M — every arrival was named, and the recess should be showing the
    ///     owner's own front for the whole slide-in. If the user still reports a back there, the
    ///     face is being refused DOWNSTREAM: read 'ANONYMOUS RECESS' (it should be gone for those
    ///     arrivals) and the reveal verdict, not this line.</item>
    /// </list>
    /// <para>Change-gated on the ARMED count, so one placement costs one line and a settled board
    /// costs none.</para>
    /// </summary>
    private void LogHandoff()
    {
        // THE GATE WAS ON THE WRONG NUMBER, AND IT MADE THE FALSIFIER UNFIREABLE. This method's own
        // doc says the pair to read is "M arrivals, N armed" and that "arrivals with 0 armed means
        // this is inert" — and it was change-gated on _handoffArmed ALONE, which is exactly the
        // number that does not move in the inert case. In the ModBuild 461 host log 6 releases
        // resolved as "the card went into a ROUND RECESS" and this line printed ZERO times, so the
        // one reading it exists to produce could not be read. Gated on the PAIR now: an arrival
        // that arms nothing prints, and says why.
        int key = _handoffGains * 1000 + _handoffArmed;
        if (_loggedHandoff == key)
            return;
        _loggedHandoff = key;
        if (_handoffArmed < _handoffGains || HandoffCard == null || _handoffRecess < 0)
        {
            // HW-VERIFY: report item 6, the INERT reading. Grep token: RECESS HAND-OFF. This is the
            // branch that could not print before. WORKING = the armed line below, with
            // armed == arrivals; INERT = this line, arrivals climbing while armed stands still.
            VRLog.Note("Net", $"RECESS HAND-OFF [player {_owner.PlayerId}]: NOT ARMED — "
                + $"{_handoffGains} recess arrival(s) this session and only {_handoffArmed} of them "
                + "named a card, so the recess draws an anonymous back and the fan has nothing to "
                + "drop from its own length. The name comes from record 36's seat, resolved one "
                + "frame before the wire stopped naming it, so a nameless arrival means the fist "
                + "was never resolved. THE TERM THAT REFUSED, measured on the frame it refused: "
                + (_fistNameMiss.Length > 0
                    ? _fistNameMiss
                    : "none banked — the fist was never named at all this session, i.e. record 36 "
                      + "never reported a card of THIS FAN'S OWN LIST in their hand. Before "
                      + "ModBuild 477 that was the standing state for every modal pick over a PILE, "
                      + "because RemoteAvatar.IsFanArcList tested the literal pair "
                      + "Hand||MapLoadout while the long rest's pick fan is the DISCARD arc")
                + ". (This clause replaces a pointer at a 'term=' reading that the recess path never "
                + "emits — the release below resolves with RefusalTerm.None by construction, so the "
                + "old sentence sent every reader to a number that is not printed.) Beside it: "
                + $"{_owner.SeatedHandCardExcess} hand card(s) of theirs are lying in a recess, and "
                + "a 0 there while a card is visibly on their board means "
                + "RemoteControlBoard.SeatedHandCardExcess is not seeing it.");
            return;
        }
        // HW-VERIFY: report item 5. Grep token: RECESS HAND-OFF.
        VRLog.Note("Net", $"RECESS HAND-OFF [player {_owner.PlayerId}]: round recess "
            + $"{_handoffRecess + 1} just took the card this peer was holding at hand seat "
            + $"{_fistSeat} of {_handBuffer.Count} — armed {_handoffArmed} of {_handoffGains} "
            + "recess arrival(s) this session, and it now stands for as long as that recess keeps "
            + "the card (a per-recess arrival EDGE, not the six-second wall clock it used to be — "
            + "which expired mid-pick and is why report item 6's fan stayed face-down). It was "
            + "The recess draws that card's OWN front for the "
            + "length of the slide-in instead of an anonymous back, and the fan drops it from its "
            + "own length so every OTHER face in the fan keeps its front too. NOTHING NEW IS ON "
            + "THE WIRE: record 36 already named which seat of this client's hand list was in "
            + "their fist, and the identity was resolved from that seat one frame before the wire "
            + "stopped naming it. THE COMPACTION BELT IS UNCHANGED and still refuses the "
            + "positional walk — this fills the hole the refusal leaves with a card that was "
            + "OBSERVED being handed over, and it is dropped the instant this client's own model "
            + "can name the recesses again. THE FALSIFIER IS THE PAIR OF NUMBERS ABOVE, not the "
            + "silence of 'ROUND SLOT COMPACTION REFUSED': 0 arrival(s) means no card was placed "
            + "and the round proves nothing, while arrivals with 0 armed means this is inert.");
    }

    /// <summary>
    /// HARDWARE EVIDENCE for report item 2. Grep token: FAN RETURN FLIGHT.
    ///
    /// <para>FALSIFIERS. (1) This line absent all session: READ 'FAN RETURN VERDICT' INSTEAD, which
    /// exists because this one's silence was ambiguous for two rounds — it prints on every release
    /// the wire reports, armed or not, and names the term that refused. (2) The line present and the
    /// user still sees the card pop: the glide ran and was overwritten. A count change inside the
    /// same 0.35 s no longer drops it (Rebuild now banks the slab's live world pose and re-seeds the
    /// replacement), so the next suspect is the fan being HIDDEN mid-glide, which clears it by
    /// design. (3) The count climbing far past the number of releases the player remembers: the fist
    /// is being read as empty mid-hold and the returning slab is being seeded from a stale pose —
    /// the verdict line's release(s) count is the number to compare it against.</para>
    /// </summary>
    private void LogReturnGlide()
    {
        if (_loggedReturns == _returnsPlayed)
            return;
        _loggedReturns = _returnsPlayed;
        // HW-VERIFY: report item 2. Grep token: FAN RETURN FLIGHT.
        VRLog.Note("Net", $"FAN RETURN FLIGHT [player {_owner.PlayerId}]: this peer released a "
            + $"card into the void and slab {_returnIndex} now GLIDES home to its arc seat from "
            + $"(hand-list seat {_returnListSeat} -> arcSeat {_returnIndex}, translated through the "
            + $"record-44 gather this fan lays out from: {(_orderArcValid ? (_orderFromLatch ? "a HELD order" : "an APPLIED order") : "no order in force, so the identity")}) "
            + "the pose their card was let go at, instead of the held slab blinking out and a fan "
            + $"slab appearing at the seat — flight {_returnsPlayed} this session, over "
            + $"{ReleaseGlideSeconds:F2}s on the OWNER's own [Cards] CardLerpSpeed "
            + $"({_lerpSpeed:F1}/s, off record 27), which is the very exponential "
            + "VRCard.UpdateBody carries their own card home on. ZERO WIRE BYTES: the seat is "
            + "record 36's, the seed pose is the mirrored held slab's own last pose, and the "
            + "destination is the arc seat this fan already lays out — the same zero-byte replay "
            + "RemoteItemFan._returnGlide does for an item chip. A card released ONTO a recess "
            + "takes the 'RECESS HAND-OFF' branch instead and never reaches here.");
    }

    /// <summary>
    /// WHICH SLAB IS HIDDEN AND WHY IT IS THAT ONE — the <c>hidden=</c> clause of
    /// <see cref="ReportArcMembershipIfChanged"/>, as <c>modelSeat-&gt;arcSeat</c> pairs plus the
    /// permutation that did the translating.
    ///
    /// <para>WHY A COUNT WAS NOT ENOUGH, and this is the reading that would have caught the
    /// 2026-09-07 HELD-order regression on its first hardware round. <c>suppressed=1</c> is printed
    /// whether the RIGHT slab or the WRONG one went dark, so a fan hiding a card the owner is
    /// looking at while drawing the one in their fist twice produced a census line identical to a
    /// perfectly healthy one. The pair is what separates them: with a non-identity order in force,
    /// <c>3-&gt;1</c> is the translation working and <c>3-&gt;3</c> under <c>via a HELD order</c> is
    /// the identity leaking through — the exact defect.</para>
    ///
    /// <para>READ IT WITH THE <c>thisFrame=</c> VERDICT ON THE SAME LINE: <c>via none</c> beside
    /// <c>thisFrame=HELD</c> or <c>=APPLIED</c> is a contradiction and means
    /// <see cref="AppliedArcSeatOf"/> is not seeing the order the fan is drawn in.</para>
    /// </summary>
    private string HiddenSeatsClause()
    {
        if (_arcHeldSeatA < 0 && _arcHeldSeatB < 0)
            return "none";
        string a = _arcHeldSeatA >= 0 ? $"{_arcHeldListSeatA}->{_arcHeldSeatA}" : string.Empty;
        string b = _arcHeldSeatB >= 0 ? $"{_arcHeldListSeatB}->{_arcHeldSeatB}" : string.Empty;
        string pair = a.Length > 0 && b.Length > 0 ? a + "," + b : a + b;
        return pair + " via " + (_appliedOrderCount > 0
            ? (_orderFromLatch ? "a HELD order" : "an APPLIED order")
            : "none (the identity)");
    }

    /// <summary>The fan's source list in one word, for a log line. The HAND is the resting answer
    /// and is spelled out rather than left blank: a census row that says nothing about the list is
    /// a row a reader cannot tell from one printed by a build that had no list at all.</summary>
    /// <summary>Change key for <see cref="ReportArcMembershipIfChanged"/> - the arc length, the
    /// suppressed seats, the source list and the ORDER FINGERPRINT, so the line fires on a real
    /// edge (a pluck, a return, a draw, a burn, a REORDER) and never on the frame rate.</summary>
    private long _loggedArcMembership = long.MinValue;

    /// <summary>
    /// HARDWARE EVIDENCE for report items 1 and 2 of 2026-09-06. Grep token: MIRRORED ARC ORDER.
    ///
    /// <para>WHY IT EXISTS. Item 1 ("die in die Hand genommene Karte bleibt im remote Faecher
    /// sichtbar") and item 2 ("ganz rechts eine andere Karte gesehen als der Spieler selber") are
    /// two questions about ONE list, and until this line there was no reading that answered either
    /// without a screenshot. <c>FAN RETURN VERDICT</c> prints only when the peer LETS GO of a card,
    /// so a round in which nobody released one says nothing at all; and nothing anywhere printed
    /// the arc's ORDER, so "exakt an den selben Stellen" could not be checked even in principle.</para>
    ///
    /// <para>READ IT LIKE THIS. Five numbers and one hex word:</para>
    /// <list type="bullet">
    ///   <item><c>arc=N</c> - slabs this fan built, i.e. the owner's own wire count.</item>
    ///   <item><c>list=L</c> - record 36's hand-list length, straight off the same packet. 0 means
    ///     the peer named no held card this frame, which is the resting state.</item>
    ///   <item><c>held=H suppressed=S</c> - how many seats record 36 named for THIS list, and how
    ///     many of them this fan is actually refusing to draw. <b>H &gt; 0 with S == 0 is the
    ///     INERT reading</b> and the arithmetic beside it says which of the two arc states was
    ///     seen: <c>L == N</c> is the steady one and MUST suppress, <c>L == N + H</c> is the
    ///     one-or-two-frame window after CardFan.Remove and must NOT.</item>
    ///   <item><c>right='X'</c> - the RIGHT-MOST card of the mirrored arc, by name. This is the
    ///     literal object of the user's item-2 complaint, so it is printed as a name and not only
    ///     folded into the fingerprint.</item>
    ///   <item><c>fp=xxxxxxxx</c> - an ORDER-SENSITIVE fold over the arc's CardInstanceIDs in draw
    ///     order. CardInstanceID is host-replicated, so THE SAME HAND IN THE SAME ORDER PRINTS THE
    ///     SAME WORD ON EVERY MACHINE. Compare it against the owner's own <c>FAN ORDER MIRROR</c>
    ///     line for that hand: equal means the two players are looking at the same fan, different
    ///     means item 2 is live and the two clients disagree about the order.</item>
    /// </list>
    ///
    /// <para>AND IT IS NOT THE ONLY WAY TO CONVICT ITEM 2, because the co-player's log may be
    /// missing - it was this round. The owner-side <c>FAN ORDER MIRROR</c> line compares those two
    /// orders on ONE machine (the arc as drawn against the arc as any observer re-derives it), so
    /// the host's log alone can prove the divergence for the HOST's own hand even with no peer log
    /// at all. This line is what proves it for the PEER's hand once theirs is copied.</para>
    ///
    /// <para>fp is computed from identities only and prints no card name but the right-most one -
    /// it is a fold, not a face, and it runs with the reveal gate SHUT because an order is not an
    /// identity. Where the gate leaves this client with no resolved model list the fingerprint is
    /// <c>--------</c> and the line still carries every count, which is the honest answer.</para>
    /// </summary>
    private void ReportArcMembershipIfChanged(int count)
    {
        int suppressed = (_arcHeldSeatA >= 0 ? 1 : 0) + (_arcHeldSeatB >= 0 ? 1 : 0);
        int held = _owner.HeldHandSeats(out _, out _, out _);

        // THE ORDER FINGERPRINT. Order-sensitive by construction (the index is folded in), over
        // CardInstanceID, which is the model's own id and therefore the same integer on both
        // machines - a GetInstanceID() here would be machine-local and would compare to nothing.
        uint fp = 2166136261u;
        bool haveIds = _handBuffer.Count > 0;
        string rightMost = "?";
        unchecked
        {
            for (int i = 0; i < _handBuffer.Count; i++)
            {
                AbilityCardUI? w = _handBuffer[i];
                int id = w != null ? w.CardInstanceID : 0;
                if (w == null)
                    haveIds = false;
                fp = (fp ^ (uint)i) * 16777619u;
                fp = (fp ^ (uint)id) * 16777619u;
            }
            AbilityCardUI? last = _handBuffer.Count > 0 ? _handBuffer[_handBuffer.Count - 1] : null;
            if (last != null && !string.IsNullOrEmpty(last.CardName))
                rightMost = last.CardName;
        }

        // THE ARC SEAT IS IN THE KEY, not just the suppressed COUNT. A reorder that moves the
        // hidden slab from one seat to another changes neither count, and before the 2026-09-07
        // HELD-order fix that is exactly the edge that went unreported for a whole session.
        long key = ((long)fp << 24) ^ ((long)count << 12) ^ ((long)suppressed << 8)
                   ^ ((long)held << 4) ^ _owner.FanSourceList
                   ^ ((long)(_arcHeldSeatA + 2) << 32) ^ ((long)(_arcHeldSeatB + 2) << 40);
        if (key == _loggedArcMembership)
            return;
        _loggedArcMembership = key;

        // HW-VERIFY: report items 1 and 2 (2026-09-06). Grep token: MIRRORED ARC ORDER.
        VRLog.Note("Net", $"MIRRORED ARC ORDER [player {_owner.PlayerId}]: list="
            + $"{FanListName(_owner.FanSourceList)} arc={count} model={_handBuffer.Count} "
            + $"recordListLen={_arcHeldListLength} held={held} suppressed={suppressed} "
            + $"hidden={HiddenSeatsClause()} "
            + $"right='{rightMost}' fp={(haveIds ? fp.ToString("x8") : "--------")} | ORDER RECORD "
            + $"44: stated={_orderStated} applied={_orderApplied} held={_orderHeld} thisFrame="
            + $"{(_orderArcValid ? (_orderFromLatch ? "HELD" : "APPLIED") : "refused/" + (_orderRefusal.Length > 0 ? _orderRefusal : "none stated"))}"
            + ". MEMBERSHIP: "
            + "held is how many seats record 36 names in THIS fan's list and suppressed is how "
            + "many slabs this fan is therefore not drawing; held>0 with suppressed=0 is the INERT "
            + "reading of the item-1 fix and the two lengths beside it say why — recordListLen==arc "
            + "is the STEADY state (CardsDriver.Rebuild has put the plucked card back in the arc) "
            + "and MUST suppress, recordListLen==arc+held is the one-or-two-frame window right "
            + "after CardFan.Remove and must NOT, because seat k has stopped naming slab k there. "
            + "ORDER: fp is an order-sensitive fold over CardInstanceID, which is host-replicated, "
            + "so THE SAME HAND IN THE SAME ORDER PRINTS THE SAME WORD ON EVERY MACHINE — compare "
            + "it with the owner's own 'FAN ORDER MIRROR' line for that hand, and compare 'right' "
            + "with what they say is at the right edge, which is the literal complaint. A "
            + "DIFFERENT fp for one hand is report item 2: this fan draws cardsUI order unless "
            + "record 44 says otherwise, while the owner's arc draws CardsDriver._fanOrder, their "
            + "session-only drag-reorder. (Until the 2026-09-07 fix this sentence ended 'which is on no "
            + "wire' — false since 462, when record 44 shipped to carry exactly it.) '--------' "
            + "means this client has no "
            + "resolved model list to fold (the reveal gate, or a lagging model), so the ORDER half "
            + "of the line proves nothing that frame while every count above still holds. ORDER "
            + "RECORD 44 IS A SEPARATE VERDICT FROM BOTH OF THOSE, and its numbers are "
            + "deliberately not one: 'stated' counts the DISTINCT orders this peer put on the wire, "
            + "'applied' counts how many of THOSE were an exact permutation of the seats this "
            + "client held and were therefore used, and 'held' counts the frames the arc was drawn "
            + "in the last applied order because this packet stated none. Both of the first two "
            + "count ORDERS since the 2026-09-07 fix; before it 'applied' counted FRAMES, which is why "
            + "the 470 logs carry the impossible pair stated=23 applied=37. stated=0 means no order "
            + "has travelled yet — a FLAT player, a peer below ModBuild 462, or a peer whose fan "
            + "has not been up; since the 2026-09-07 fix a capable sender states its arc on every packet it can "
            + "describe, identity or not, so a LIVE fan with stated=0 is now itself the finding. "
            + "stated>0 with applied=0 IS the defect: the record travelled and "
            + "the belt refused it, and 'thisFrame' names which of the four terms did — "
            + "'not a permutation' is the serious one and means the wire's own numbers were "
            + "self-contradictory, while 'model length' and 'no fronts' are the ordinary transients "
            + "around a held card, a burn or a shut gate. NOTHING IS EVER APPLIED UNVALIDATED: a "
            + "wrong permutation would shift every face after the first mistake, which is worse "
            + "than the divergence the record exists to fix, so a refusal keeps the game's own "
            + "order — exactly the picture ModBuild 461 drew.");
    }

    /// <summary>Change key for <see cref="ReportSeatStackIfChanged"/> — the arc length, the front
    /// count and the held-seat count, so the line fires on a real edge and never on the frame rate.
    /// </summary>
    private int _reportedSeatStack = int.MinValue;

    /// <summary>
    /// THE OVERLAY READING FOR THIS FAN (2026-09-06 report item 7a, the user verbatim: "Dieses
    /// Rückseiten Raster ist auch auf den Handfächer bei einer langen Rast beim remote Spieler zu
    /// sehen — man sieht die Karten im Fächer und in der Hand … aber mit diesem Rückseiten Raster
    /// darauf drübergelegt").
    ///
    /// <para>WHY IT IS OWED HERE SPECIFICALLY. A long rest re-Shows the owner's hand over their
    /// DISCARD pile, so the arc the report is about is THIS one drawing a pile
    /// (<c>FanListName</c> = "pick fan: the DISCARD pile"), not the browse arc beside it. Every
    /// existing line on this surface measures the FACE POLICY — front or back, and which rule
    /// decided it — and none of them can say anything at all about a front that IS being drawn and
    /// is being painted over. The seat stack is that missing half, and it is
    /// <see cref="RemoteCardArt.DescribeStack"/> so this fan, the browse arc and the item arc quote
    /// one implementation rather than three walks that would drift.</para>
    ///
    /// <para>WORKING / INERT / BEYOND: a CanvasGroup product of 1.000 with the drawing count within
    /// a card's worth of the Graphic total is an opaque print and the lattice cannot be this seat's
    /// doing; a product below 1.000, or a drawing count far short of the total, IS the defect and
    /// says which of the two mechanisms it is. The line never appearing while a peer fans a hand
    /// means no front was ever printed here and NOTHING has been measured — read the census row
    /// beside it before concluding anything from silence.</para>
    /// </summary>
    private void ReportSeatStackIfChanged(int count, int frontCount, int heldSeatCount)
    {
        if (frontCount <= 0)
            return;
        int key = (count * 397 + frontCount) * 397 + heldSeatCount * 31 + _censusList;
        if (key == _reportedSeatStack)
            return;
        _reportedSeatStack = key;
        RemoteCardArt? seat = null;
        for (int i = 0; i < _faces.Count && seat == null; i++)
        {
            if (_faces[i] != null && _faces[i].HostDrawn)
                seat = _faces[i];
        }
        if (seat == null)
            return;
        // HW-VERIFY: report item 7a. Grep token: PEER FAN SEAT STACK.
        VRLog.Note("Net", $"PEER FAN SEAT STACK [player {_owner.PlayerId}]: source "
            + $"{FanListName(_censusList)}; the WIRE asked for {count} slab(s), {frontCount} of them "
            + $"are showing a FRONT, and the owner has {heldSeatCount} of this list's card(s) in "
            + "their fist (those seats are dropped from the FACE list, never from the arc). "
            + seat.DescribeStack()
            + " Read it beside the PEER CARD FACE CENSUS line, which says front-or-back, and "
            + "the PEER BROWSE ARC line, which says the same two things for the pile arc — both "
            + "named without their log anchors, so an anchored grep for either does not count THIS "
            + "line as one of them.");
    }

    private static string FanListName(byte list) => list switch
    {
        NetProtocol.HeldFaceListDiscard => "pick fan: the DISCARD pile",
        NetProtocol.HeldFaceListBurnt => "pick fan: the BURNT pile",
        _ => "hand fan: the HAND",
    };

    /// <summary>
    /// Fill <see cref="_handBuffer"/> with the widgets of the list the remote actor is ACTUALLY
    /// FANNING, in that list's own order — the exact set their local fan draws. Read straight off
    /// the game's own <c>CardsHandManager.GetHand(actor)</c> (publicized). Cleared + refilled each
    /// call; no allocation.
    ///
    /// <para>WHICH LIST IS <paramref name="fanList"/>'S TO SAY, and it is the correction of 2026-09-06
    /// (report item 7). This method used to be unconditionally the HAND pile
    /// (<c>widget.CardType == CardPileType.Hand</c>) and its own doc block asserted that excluding
    /// Round/Discard/Lost/Active was what kept the round-selection secret safe. THE SECOND HALF OF
    /// THAT SENTENCE WAS NEVER TRUE — the secret is kept by <see cref="RevealGate"/>, which is asked
    /// before this method is ever called and is asked the SAME question for every population — and
    /// the first half stopped being true the moment the game re-Showed a peer's hand over their
    /// DISCARD pile for a long rest's burn step. The list was then wrong, its length disagreed with
    /// the arc on the wire, and the caller's belt drew the whole fan as backs.</para>
    ///
    /// <para>ROUND AND ACTIVE ARE STILL UNREACHABLE HERE, which is the part of the old claim that
    /// survives: <see cref="NetProtocol.IsFanSourcePile"/> admits only the discard and lost lists, so
    /// a round-card slot can never become a fan seat by way of this argument. Those two populations
    /// have surfaces of their own (<c>RemoteControlBoard</c>, <c>RemoteActiveCards</c>) with rules of
    /// their own.</para>
    /// </summary>
    private void ResolveHandFronts(CPlayerActor actor, byte fanList)
    {
        _handBuffer.Clear();
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return;
        CardsHandUI hand = manager.GetHand(actor);
        if (hand == null)
            return;
        // ─── THE FAN IS A PILE, AND THE WIRE SAID SO (report item 7) ─────────────────────────────
        // During a modal card pick the owner's arc is their DISCARD or LOST pile rather than their
        // hand, and extension record 43 names which. Resolve it out of THE SAME EXPRESSION the pile
        // arc's own wire index space is built from (CardsGameApi.GetPileArcWidgets — the one record
        // 36's held-card seat and record 39's sacrifice seat are both indexed into), so the fan, the
        // card plucked out of it, and the browse arc beside it are three views of ONE list rather
        // than three filters that happen to agree.
        //
        // THE LENGTH BELT STILL HAS THE LAST WORD. All this branch does is choose WHICH list to
        // walk; the caller still refuses every face unless this client's copy of it is exactly as
        // long as the arc on the wire, so a pile the two clients disagree about draws the same
        // BACKS it drew before this record existed. Nothing here can put a face from one pile onto
        // a card from another.
        //
        // AN UNKNOWN OR ABSENT LIST IS THE HAND, decided in NetProtocol.IsFanSourcePile: a value
        // this build cannot name degrades to the picture it drew before, which is the only
        // degradation that cannot mislead.
        if (NetProtocol.IsFanSourcePile(fanList))
        {
            CardsGameApi.GetPileArcWidgets(hand, fanList == NetProtocol.HeldFaceListBurnt,
                                           _handBuffer);
            if (_handBuffer.Count > MaxCards)
                _handBuffer.RemoveRange(MaxCards, _handBuffer.Count - MaxCards);
            return;
        }
        List<AbilityCardUI> cards = hand.cardsUI; // publicized private field
        if (cards == null)
            return;
        // THE OWNER'S OWN MEMBERSHIP TEST, THE SAME EXPRESSION — see CardsGameApi.HandFanMember.
        // This used to be a local `CardType == Hand && fullAbilityCard != null`, which is not the
        // filter the owner's arc is built with, and the gap was not a subtlety: the LONG REST
        // placeholder lives in cardsUI with CardType Hand (CardsHandUI.cs:1309), so this buffer
        // counted one card MORE than the owner's fan holds — permanently — and the equality below
        // could never be true. Two full hardware logs contain no "FRONTS — content=HAND" line at
        // all while every sibling surface on the same RevealGate opened normally, which is what a
        // shut ARITHMETIC looks like rather than a shut gate (report item 5b).
        //
        // fullAbilityCard is no longer a membership term and that is deliberate: it is what a face is
        // DRAWN from, not what makes a card a member. A seat whose widget has none draws a BACK and
        // leaves every other seat correct, which is strictly better than dropping the seat and
        // shifting the rest.
        for (int i = 0; i < cards.Count && _handBuffer.Count < MaxCards; i++)
        {
            if (CardsGameApi.HandFanMember(cards[i], actor))
                _handBuffer.Add(cards[i]);
        }
    }

    /// <summary>Float the fan a palm standoff up the PALM normal and arc it to face the owner's head,
    /// easing smoothly (snapping on the first frame after a (re)activation).</summary>
    private void PoseFan(Transform holder, float dt)
    {
        if (_root == null)
            return;

        float scale = _owner.AppliedScale;
        Transform root = _root.transform;

        // Standoff up the PALM normal, in world meters. AppliedScale is multiplied in explicitly
        // here because we set the root's WORLD position (the card SIZES instead inherit AppliedScale
        // from the scaled holder we parent under).
        //
        // MP GAP #1 (fixed): this used holder.up — the HAND ROOT's +Y, which by the HandRig contract
        // points out of the BACK of the hand. The owner's fan hangs off Rig.PalmCenter, whose +Y is
        // the PALM normal (for the procedural hand literally a 180° Z-flip of the root; for a glove
        // prefab whatever Anchor_Palm was authored as). So a peer's fan floated 9 cm out of the back
        // of their hand instead of 9 cm above their palm — an ~18 cm error that also dragged the
        // FACING with it, because the billboard below is derived from the fan's own position. The
        // receiver builds the peer's hand from the same HandVisuals rig the owner does, so their palm
        // anchor is available exactly (RemoteAvatar.PalmAnchorFor) — no wire field needed. The holder
        // stays as the fallback for the frames before the rig exists.
        Transform anchor = _owner.PalmAnchorFor(holder) ?? holder;
        bool onPalm = !ReferenceEquals(anchor, holder);
        if (onPalm != _loggedPalmAnchor || !_anchorLogged)
        {
            _loggedPalmAnchor = onPalm;
            _anchorLogged = true;
            VRLog.Info("Net", $"Remote hand fan [player {_owner.PlayerId}] anchor = "
                + (onPalm
                    ? "PALM anchor (Rig.PalmCenter, +Y out of the palm) — matches the owner's CardFan."
                    : "HAND-ROOT fallback (+Y out of the BACK of the hand) — rig not built yet."));
        }
        Vector3 target = anchor.position + anchor.up * (_palmOffset * scale);

        // Face the owner's head: the fan's +Z points AWAY from the head so the card fronts (-Z)
        // look toward the owner and their BACKS face everyone else — exactly like the local fan.
        // On a (re)appearance the base is seeded from wherever the root actually is, so the
        // degenerate fallbacks below still mean "keep the current rotation", as they did when this
        // method eased root.rotation directly.
        if (_poseInit)
            _facing = root.rotation;

        Transform head = _owner.HeadHolder;
        Quaternion targetRot = _facing;
        float biasYaw = 0f;
        if (head != null)
        {
            Vector3 away = target - head.position;
            if (away.sqrMagnitude > 1e-6f)
            {
                targetRot = Quaternion.LookRotation(away.normalized, Vector3.up);

                // [Cards] FanGazeBias — the extra yaw that turns the fan partway toward the owner's
                // gaze. See the gaze-facing-bias region for the port and for why the toggle is the
                // only input that is not already on the wire. Inert (biasYaw stays exactly 0) for
                // every peer whose toggle is off, which today is all of them.
                if (_gazeBiasOn)
                {
                    biasYaw = UpdateGazeBias(away, head.forward, dt);
                }
                else if (_gazeBiasYaw != 0f || _gazeSide != 0)
                {
                    // Switched off mid-bias (their tuning revision moved): drop the residual so a
                    // re-enable eases out from centre — CardFan.Tick's own else-branch, verbatim.
                    _gazeBiasYaw = 0f;
                    _gazeSide = 0;
                }
            }
        }

        if (_poseInit)
        {
            _poseInit = false;
            _facing = targetRot;
            root.SetPositionAndRotation(target, ApplyGazeBias(_facing, biasYaw));
        }
        else if (_followSmoothing <= 0f)
        {
            // RIGID, because the owner is rigid (see _followSmoothing): at FanFollowSmoothing == 0
            // CardFan.Tick abandons the eased branch and parents the fan straight under PalmCenter,
            // so their fan arrives at the palm pose on the frame the palm does. Asserting the target
            // — the same two lines the _poseInit path uses — is that behaviour in this frame:
            // there is no lag left to reproduce and no residual to carry, so _facing is written
            // rather than slerped and a later return to a non-zero rate eases out of the true pose.
            // BRANCHED, not merely scaled: k = 1 - exp(0) = 0 would have FROZEN the fan wherever it
            // last stood instead of welding it to the hand — the opposite of rigid.
            _facing = targetRot;
            root.SetPositionAndRotation(target, ApplyGazeBias(_facing, biasYaw));
        }
        else
        {
            float k = 1f - Mathf.Exp(-_followSmoothing * Mathf.Max(dt, 0f));
            _facing = Quaternion.Slerp(_facing, targetRot, k);
            root.SetPositionAndRotation(
                Vector3.Lerp(root.position, target, k),
                ApplyGazeBias(_facing, biasYaw));
        }
    }

    /// <summary>Compose the eased gaze-bias yaw onto the base billboard — CardFan.Tick's
    /// <c>AngleAxis(biasYaw, up) * baseFacing</c>, including its exact-zero short circuit so a fan
    /// with the toggle off is handed the identical quaternion this file always produced.</summary>
    private static Quaternion ApplyGazeBias(Quaternion baseFacing, float biasYaw)
        => biasYaw != 0f ? Quaternion.AngleAxis(biasYaw, Vector3.up) * baseFacing : baseFacing;

    /// <summary>
    /// Arc the card slabs in the fan-local frame, reproducing CardFan.Relayout. Positions are real
    /// meters and inherit AppliedScale from the scaled holder above the root.
    ///
    /// WHAT IS REPRODUCED AND WHERE IT COMES FROM. The wire carries the card COUNT and nothing else
    /// about the fan (never identities) — but everything else CardFan's steady layout needs is a
    /// function of the count and of the OWNER's head, and the owner's head pose (position AND
    /// rotation) already rides the rig packet. So the receiver re-derives, rather than transmits:
    ///   * the arc + roll + curvature-by-fill (count only),
    ///   * the per-card TOE-IN toward the owner's head (head POSITION in fan-local space) —
    ///     MP GAP #2, previously absent, so every ghost card shared the fan root's single normal
    ///     while the owner sees ten cards each aimed at their own eyes,
    ///   * the depth BOW plus its gaze relief and stacking clamp (count + head GAZE) —
    ///     MP GAP #3, previously absent, so a peer's full hand read as a flat arc instead of the
    ///     35 mm cup the owner holds, with the looked-at end lifted out of it.
    /// The result is frame-for-frame the shape the owner sees, with no new wire field and no version
    /// bump. What is NOT derivable is listed on <see cref="RemoteHandFan"/>'s known-gaps note.
    /// </summary>
    private void LayoutCards(int n, float dt)
    {
        if (n <= 0 || _root == null)
            return;

        ResolveArcHeldSeats(n);

        float step = n > 1 ? Mathf.Min(_perCardStepDegrees, _arcSweepDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        // Curvature-by-fill: a few cards read nearly flat/untilted, a full hand arches and tilts —
        // but ONLY where the OWNER has [Cards] FanCurveByFill on (see _curveByFillOn). With it off
        // CardFan drops the fill term entirely and uses the two factors at every hand size (the
        // legacy, pre-Demeo look), so the mirror must too — this multiply used to run unconditionally.
        // MAGNITUDE at the shipped 0.55 arch / 0.85 tilt over FanMaxHandForCurve = 10: nothing at all
        // for a FULL hand (fill = 1, the two agree exactly) and largest in the middle of the range —
        // a 5-card hand's end card sits 7 mm deeper and rolled 12° further with the dial OFF than
        // with it on, a 3-card hand 2.5 mm and 8°. The ROLL is the half an onlooker actually reads.
        float fill = _curveByFillOn ? Mathf.Clamp01((float)n / _maxHandForCurve) : 1f;
        float arch = _archFactor * fill;
        float tilt = _tiltFactor * fill;

        // Fan-out reveal (see _openElapsed), now card-for-card what CardFan.Relayout blends on Open:
        // every card SEEDS at the MIDDLE slot's arc pose (not at the fan origin — the local fan-in
        // starts from the centre CARD, so a peer used to see the stack pop from a slightly wrong
        // place) and eases out to its own slot on an ease-out cubic with a per-card stagger delay
        // rippling outward from the middle. Runs on UNSCALED dt (the caller's already is).
        bool opening = _openElapsed >= 0f;
        bool closing = !opening && _closeElapsed >= 0f && _closeSeconds > 0f;
        float closeP = closing ? Mathf.Clamp01(_closeElapsed / _closeSeconds) : 0f;
        int mid = n / 2;
        float midAngle = start + step * mid;
        float midRad = midAngle * Mathf.Deg2Rad;
        var collapsedRot = Quaternion.Euler(0f, 0f, -midAngle * tilt);
        var collapsedXY = new Vector2(Mathf.Sin(midRad) * _radius,
                                      (Mathf.Cos(midRad) - 1f) * _radius * arch);
        if (opening)
        {
            _openElapsed += Mathf.Max(dt, 0f);
            if (_openElapsed >= _openSeconds + Mathf.Max(mid, n - 1 - mid) * _openStagger)
                _openElapsed = -1f;
        }

        // The arriving half's seed pose: the deal point off the arc's LOW-index end, computed once
        // per frame rather than per card (it is the same point for all of them).
        bool swapping = _swapElapsed >= 0f;
        Vector3 dealPos = default;
        Quaternion dealRot = Quaternion.identity;
        if (swapping)
            SwapGatherPoint(SwapArcSpan(), -1f, out dealPos, out dealRot);

        // The owner's head IN FAN-LOCAL SPACE — the toe-in target and the frame the gaze is measured
        // in, exactly as CardFan.TryGetHeadLocal / UpdateCardPresentation do it locally. The root was
        // posed THIS frame by PoseFan (call order guarantees it), so there is no one-frame lag
        // between the billboard and the presentation, same as the local fan. InverseTransform*
        // divides out AppliedScale, so these land in the same real metres as the arc constants.
        Transform root = _root.transform;
        Transform? head = _owner.HeadHolder;
        bool haveHead = head != null;
        Vector3 headLocal = default;
        if (haveHead)
        {
            headLocal = root.InverseTransformPoint(head!.position);
            TrackGazeApex(root, head, n, dt);
        }

        float apex = ComposeDepths(n);
        float maxToeDeg = 0f;

        // WHICH CARD THE OWNER IS SINGLING OUT (extension record 6 — defect (f) "das Hervorheben
        // von Karten ist gar nicht synchronisiert"). Locally, pointing at a card slides every OTHER
        // card sideways to open a gap around it (CardFan.Relayout + SplitOffset) and pops the card
        // itself toward the viewer (VRCard's pop). Both are reproduced below from the synced INDEX
        // alone — no card identity, no per-frame transform. Clamped against OUR live slab count:
        // an index may legitimately arrive a frame before/after the count it was measured against.
        int hovered = _owner.HandHighlightIndex;
        if (hovered < 0 || hovered >= _cards.Count)
            hovered = -1;

        for (int i = 0; i < _cards.Count; i++)
        {
            // ─── THE SEAT IN THE OWNER'S FIST IS NOT DRAWN HERE (report item 1, 2026-09-06) ────
            // "Wird eine Karte in die Hand genommen vom Mitspieler bleibt die in die Hand
            // genommene Karte im remote Faecher sichtbar." FOURTH copy of one membership defect,
            // and the last of the four arcs to be fixed; see ResolveArcHeldSeats for the log
            // evidence that convicted the premise every earlier build reasoned from.
            //
            // THE GAP IS KEPT, NOT CLOSED, exactly as it is for the browse and item arcs and for
            // the same reason: CardFan.Relayout keeps n = _cards.Count and merely `continue`s past
            // a held card, so the owner is looking at an arc of n with one seat empty. Hiding the
            // slab in place and leaving every neighbour's angle alone IS that picture. Re-spacing
            // would trade one 1:1 breach for a worse one (the ruling ModBuild 459 recorded for the
            // item arc), and it would also break the positional face zip below, which is what
            // keeps slab i a name for card i.
            bool inFist = i == _arcHeldSeatA || i == _arcHeldSeatB;
            if (_cards[i].activeSelf == inFist)
                _cards[i].SetActive(!inFist);
            if (inFist)
                continue;   // no pose for a card that is not in the arc — the owner gives it none either

            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * _radius,
                                  (Mathf.Cos(rad) - 1f) * _radius * arch,
                                  i < n ? _depths[i] : -ZStagger * i);
            var rot = Quaternion.Euler(0f, 0f, -angle * tilt);

            // Whole-fan SPLIT around the highlighted card: the neighbours slide along their own
            // local right, most for the nearest (CardFan.SplitOffset, same falloff, same gain).
            // The highlighted card is the pivot and does not move here — its lift is applied after
            // the toe-in below, exactly like VRCard applies it on top of the layout's home pose.
            if (hovered >= 0 && i != hovered)
                pos += rot * new Vector3(SplitOffset(i - hovered), 0f, 0f);

            // Per-card TOE-IN (CardFan.Relayout): aim THIS card's normal at the owner's head instead
            // of inheriting the root's single billboard normal. FromToRotation is the minimal arc
            // from the card's forward to the head, PRE-multiplied so the roll — the fan's signature
            // shape — survives exactly.
            if (haveHead && _faceViewer > 0f)
            {
                Vector3 toCard = pos - headLocal;
                if (toCard.sqrMagnitude > 1e-6f)
                {
                    Vector3 dir = toCard.normalized;
                    // _faceViewer is the toe-in GAIN. Slerped unconditionally from "no toe-in" (the
                    // branch CardFan takes only when the gain is < 1 would be dead code against a
                    // const, and Slerp at 1 returns the aim itself) so the constant stays honest if
                    // it is ever re-seeded from a retuned CardsConfig default.
                    Quaternion aim = Quaternion.Slerp(
                        Quaternion.identity, Quaternion.FromToRotation(Vector3.forward, dir), _faceViewer);
                    rot = aim * rot;
                    float deg = Vector3.Angle(Vector3.forward, dir) * _faceViewer;
                    if (deg > maxToeDeg)
                        maxToeDeg = deg;
                }
            }

            Transform t = _cards[i].transform;
            if (opening)
            {
                // Collapsed seed = the MIDDLE slot's pose, each card keeping its OWN z-stagger so
                // the draw order never flickers through the reveal (CardFan.Relayout verbatim).
                float e = OpenProgress(i, mid);
                var seed = new Vector3(collapsedXY.x, collapsedXY.y, -ZStagger * i);
                pos = Vector3.Lerp(seed, pos, e);
                rot = Quaternion.Slerp(collapsedRot, rot, e);
            }
            else if (closing)
            {
                // CardFan.TickCollapse verbatim: the reveal run backwards, on an EASE-IN (p*p —
                // an accelerating shut reads snappy) and with NO stagger, because the owner's
                // collapse has none. Same collapsed pose the open seeds from, so the two
                // animations are each other's mirror by construction rather than by agreement.
                float e = closeP * closeP;
                var seed = new Vector3(collapsedXY.x, collapsedXY.y, -ZStagger * i);
                pos = Vector3.Lerp(pos, seed, e);
                rot = Quaternion.Slerp(rot, collapsedRot, e);
            }
            // CHARACTER EXCHANGE, arriving half (CardFan.Relayout's swap blend verbatim): fly in
            // from the deal point off the arc's LOW end, bowing TOWARD the owner at mid-flight —
            // the opposite of the leaving half's duck, so the two hands cross in depth rather than
            // through each other — and settle with the back-ease overshoot. Mutually exclusive with
            // the reveal above (BeginSwap drops _openElapsed here, BeginSwapOut locally), exactly as it is locally.
            float swapScale = 1f;
            if (swapping)
            {
                float st = InProgress(i);
                float se = EaseOutBack(st, Mathf.Clamp(_swapSettleOvershoot, 0f, 3f));
                pos = Vector3.LerpUnclamped(dealPos, pos, se);
                pos.z -= Mathf.Max(0f, _swapArc) * Mathf.Sin(st * Mathf.PI);
                rot = Quaternion.Slerp(dealRot, rot, Mathf.Clamp01(se));
                swapScale = Mathf.LerpUnclamped(Mathf.Clamp(_swapSeedScale, 0.02f, 1f), 1f, se);
            }
            // The LIFT itself, applied on top of the finished home pose exactly as VRCard does:
            // toward the viewer along the card's own −Z, a touch up its +Y, and 18 % bigger. The
            // 0..1 ramp is eased locally on the same MoveTowards rate the local card uses, so the
            // pop grows and relaxes at the local speed and the wire only ever carries the index.
            float popT = PopAmount(i, hovered, dt);
            if (popT > 0f)
                pos += rot * new Vector3(0f, PopUp * popT, -_popForward * popT);
            // ─── THE RETURN FLIGHT (report item 2) ──────────────────────────────────────────────
            // ALL THIS DOES NOW IS SEED, and that is the correction the arc reflow brought with it.
            // The slab is STAMPED once at the world pose their card was released at (the held
            // slab's last pose, carried as a world pose because it comes from under the AVATAR's
            // hand holder, not from this fan's root), and the standing ease below carries it home
            // from there — which is exactly the division of labour on the owner's side, where
            // VRCard.OnRelease only re-applies the world pose and sets _releaseGlide while the
            // ordinary home-lerp does the moving. It used to run a SECOND lerp of its own here,
            // byte-identical to the one below; that was the only easing this loop had, so it read
            // as the mechanism rather than as the duplicate it now is.
            //
            // _returnGlide therefore no longer gates any motion. It is the window in which a seed
            // may still be pending (the wire count can grow a frame after the fist empties), which
            // is the last thing VRCard's own _releaseGlide is still for once its unscaled-dt job
            // is moot — this whole class already ticks on unscaled time.
            if (i == _returnIndex && _returnGlide > 0f)
            {
                if (_returnSeedPending)
                {
                    _returnSeedPending = false;
                    t.position = _returnSeedPos;
                    t.rotation = _returnSeedRot;
                    // A seeded slab HAS a pose to ease from, whatever Rebuild decided about it.
                    if (i < _seeded.Count)
                        _seeded[i] = true;
                }
                _returnGlide -= Mathf.Max(dt, 0f);
                if (_returnGlide <= 0f)
                    ClearReturnGlide();
            }

            // ─── THE WRITE, AND THE OWNER'S OWN RULE FOR WHICH KIND OF WRITE IT IS ──────────────
            // CardFan.cs:2021 is `card.SetHome(_root, pos, rot, scale, instant || opening ||
            // swapping)`, and CardFan.cs:2218 (the collapse) passes instant: true. Its comment says
            // why the three are exempt: "instant is forced so VRCard tracks the blend exactly (its
            // own home-lerp would double-smooth the motion)". Those three compute a blend that must
            // be WRITTEN; everything else is a home that must be EASED TOWARD. This is that
            // expression, term for term, plus the fourth case the owner also snaps: a slab with no
            // pose to ease from (VRCard._instantNext out of the pool — here, a slab Rebuild has
            // just created and could not carry a predecessor into, which sits at the fan root's
            // origin and would otherwise streak out of the middle of the hand).
            // ─── THE OWNER'S CARD SIZE, WHICH THIS STATEMENT USED TO DELETE ────────────────────
            // Rebuild writes SlabScale onto every fresh slab and this line overwrote it on the very
            // next statement with a product that had no width term in it — so a peer who had moved
            // [Cards] CardWidth has never had it reach their mirrored fan, on any build. The two
            // statements are three lines of execution apart and the comment on the first one
            // asserts the opposite ("so their ghost cards read the size they see"), which is why it
            // survived: every reader who checked stopped at the assertion.
            //
            // IT IS A PRODUCT OF THREE, and only the first is the size: the owner's card width, the
            // swap blend's own 0..1 seed ramp, and the hover pop. The two blends are FRACTIONS of a
            // card, so they multiply the size rather than replace it — which is exactly how VRCard
            // composes its own (`_homeScale * (1 + PopScale * _pop)`, VRCard.cs:2242).
            Vector3 want = Vector3.one * (SlabScale * swapScale * (1f + PopScale * popT));
            bool seeded = i < _seeded.Count && _seeded[i];
            if (opening || closing || swapping || !seeded)
            {
                if (i < _seeded.Count)
                    _seeded[i] = true;
                t.localPosition = pos;
                t.localRotation = rot;
                if (t.localScale != want)
                    t.localScale = want;
                continue;
            }

            // VRCard.cs:2253 verbatim — `1 - exp(-CardLerpSpeed * dt)` on all three channels, on
            // the OWNER's CardLerpSpeed off record 27 and never this client's. The pop is inside
            // the target rather than added after it, exactly as VRCard folds it into `target`
            // before the lerp, so a lifting card rises on the same curve it slides on.
            //
            // dt IS THIS CLASS'S OWN unscaled tick (NetAvatarDriver.cs:854), the same clock the
            // open, close and swap animations beside this line already run on. The owner's
            // ordinary reflow runs on scaled time and their release glide forces unscaled; a
            // mirror cannot see a peer's timeScale, so one clock for all four of this fan's
            // animations is the only self-consistent answer and it is the one already shipped.
            float k = 1f - Mathf.Exp(-_lerpSpeed * Mathf.Max(dt, 0f));
            t.localPosition = Vector3.Lerp(t.localPosition, pos, k);
            t.localRotation = Quaternion.Slerp(t.localRotation, rot, k);
            Vector3 scaled = Vector3.Lerp(t.localScale, want, k);
            // Settle onto the exact value rather than approaching it forever: an exponential never
            // arrives, and a per-frame transform write on a fan nobody is touching is the kind of
            // cost this class counts. The position and rotation writes above are unconditional
            // because the arc itself moves with the owner's gaze every frame anyway.
            t.localScale = (scaled - want).sqrMagnitude < 1e-8f ? want : scaled;
        }

        LogGeometry(n, apex, maxToeDeg, haveHead);
        LogHighlightIfChanged(hovered);
    }

    // ---------------------------------------------------------------- highlight (record 6) --

    /// <summary>Per-slab pop ramp (0..1), index-aligned with <c>_cards</c>. Kept per slab rather
    /// than as a single "the hovered card's ramp" so a lift MOVING from one card to the next has
    /// the old card relaxing while the new one rises — which is what the local fan does.</summary>
    private readonly float[] _pop = new float[MaxCards];

    /// <summary>Last highlighted index stated in the log (−2 = never), so the diagnostic fires on
    /// a real change and never per frame.</summary>
    private int _loggedHighlight = -2;

    /// <summary>Sideways slide of a split neighbour <paramref name="signed"/> cards away from the
    /// highlighted one — the shared <c>Cards.FanSweep.SplitOffset</c>, driven by the OWNER's three
    /// dials off extension record 28 rather than this client's. It used to be this file's own copy
    /// of those five terms, and <see cref="RemoteBrowserFan"/> and <see cref="RemoteItemFan"/> each
    /// carried a byte-identical third and fourth.</summary>
    private float SplitOffset(int signed)
        => Cards.FanSweep.SplitOffset(signed, _splitMultiplier, _splitFalloff, _splitScale);

    /// <summary>Advance and return slab <paramref name="i"/>'s pop ramp toward 1 while it is the
    /// highlighted card and toward 0 otherwise, on <c>VRCard</c>'s own <see cref="PopRate"/>.</summary>
    private float PopAmount(int i, int hovered, float dt)
    {
        if (i < 0 || i >= _pop.Length)
            return 0f;
        _pop[i] = Mathf.MoveTowards(_pop[i], i == hovered ? 1f : 0f, Mathf.Max(dt, 0f) * PopRate);
        return _pop[i];
    }

    /// <summary>Drop every pop ramp (fan closed / rebuilt) so a re-opened fan never starts with a
    /// stale card already lifted.</summary>
    /// <summary>
    /// Emit the crumble (<c>appear</c> = false) or materialise (<c>appear</c> = true) puff for one
    /// MIRRORED card, at that slab's current world pose.
    ///
    /// <para>The frame is <c>VRCard.EmitCardDust</c>'s, term for term — right/up off the slab, the
    /// out-normal <c>-forward</c> because a card's +Z points AWAY from the viewer, and the half
    /// extents scaled by <c>lossyScale</c>. Copying the construction rather than the numbers is the
    /// point: the two puffs are identical because they are the same expression, not because two
    /// formulas agree (the rule <c>RemoteDecisionWidgets</c> states for the decision row).</para>
    ///
    /// <para>The TONE is this class's default rather than the owner's per-card tone, which the wire
    /// does not carry. Said plainly rather than hidden: the dust is a monochrome puff and the tone
    /// only shifts its warmth; carrying it would be a colour field per card for a difference
    /// nobody has reported. If a hardware round says otherwise, that is one more field, not a
    /// redesign.</para>
    ///
    /// <para>THE GATE IS <see cref="_cardDustOn"/> AND NOTHING ELSE — the owner's bit off wire id
    /// <see cref="NetProtocol.TuneCardDustOn"/>. It reads that way here and always did, but the
    /// puff still never appeared: <c>CardDustFx.EmitAppear</c>/<c>EmitVanish</c> opened with a
    /// second gate on the VIEWER's own <c>[Cards] CardDust</c> dial, which ships OFF. So from the
    /// day this shipped (ModBuild 302) a peer drew the owner's dust only when the peer had ALSO
    /// switched their own cards' dust on — an AND of two permissions against a sender contract that
    /// says a peer "may not withhold it when they do". The emitter now makes the call site name
    /// WHICH question it is asking; this one asks the owner's, and answers it above.</para>
    /// </summary>
    private void EmitMirroredCardDust(GameObject? slab, bool appear)
    {
        if (!_cardDustOn || slab == null)
            return;
        Transform t = slab.transform;
        float lossy = t.lossyScale.x;
        // NOMINAL AGAINST THE LIVE LOSSY SCALE, and the change of numerator is the other half of
        // the SlabScale repair rather than a retune. The slab subtree is authored at the nominal
        // card and the owner's width now rides the slab root, so it is already inside `lossy`;
        // multiplying the tuned width by it as well would square the ratio. This line used to read
        // _cardWidth against a lossy that had had the ratio stripped out of it by LayoutCards, so
        // it was the one place in the file that came out RIGHT — by cancelling the defect. The
        // world size it computes is unchanged to the last float: DefaultCardWidth x (rig x ratio)
        // is the same product as _cardWidth x rig.
        float halfW = DefaultCardWidth * 0.5f * lossy;
        float halfH = DefaultCardHeight * 0.5f * lossy;
        if (halfW < 1e-4f || halfH < 1e-4f)
            return;
        if (appear)
            Cards.CardDustFx.EmitAppear(t.position, t.right, t.up, -t.forward, halfW, halfH,
                                        Cards.CardDustFx.DefaultTone,
                                        Cards.CardDustFx.Permission.OwnerAlreadySaidYes);
        else
            Cards.CardDustFx.EmitVanish(t.position, t.right, t.up, -t.forward, halfW, halfH,
                                        Cards.CardDustFx.DefaultTone,
                                        Cards.CardDustFx.Permission.OwnerAlreadySaidYes);
    }

    // ------------------------------------------------------------- the GAME's card plume --

    /// <summary>
    /// Per-slab latch for the game's card plume: true while the SOURCE widget behind slab
    /// <c>i</c> was running a card effect on the previous frame. EDGE-triggered, exactly as
    /// <c>Cards.BurnCardFx</c> edge-triggers its own on-card lifecycle — a level trigger would host
    /// a fresh plume on every frame of a two-second burn.
    /// </summary>
    private readonly bool[] _plume = new bool[MaxCards];

    /// <summary>One-shot latch for the "plume path is ARMED" line — see the log text for what its
    /// presence-without-a-spawn proves.</summary>
    private bool _plumeArmedLogged;

    /// <summary>
    /// Mirror the GAME's own card plume (wire id <see cref="NetProtocol.TuneGameCardParticlesOn"/>)
    /// onto any slab whose card is running a burn / lost / discard effect on the owner's screen.
    ///
    /// <para>THE TRIGGER IS THE PREDICATE <c>Cards.BurnCardFx</c> ALREADY USES, term for term
    /// (<c>HasEffect(BurnCard) || HasEffect(LostMode) || HasEffect(DiscardMode)</c>), read off the
    /// peer's own live <c>AbilityCardUI.fullAbilityCard</c> — the very widget
    /// <see cref="UpdateFaces"/> resolved this frame to print the slab's face. Zero extra wire, and
    /// the owner's picture and the mirror's agree because they are the same expression over the
    /// same object, not because two formulas were made to match (the rule
    /// <c>RemoteDecisionWidgets</c> states for the decision row). The CLONE on the slab is no use
    /// for this — <c>RemoteCardArt</c> <c>DestroyImmediate</c>s its <c>CardEffects</c> on purpose
    /// (the screen-space <c>_PosAndBounds</c> material is the "card renders DEEP BLACK" hazard) —
    /// so the SOURCE is the only thing that can be asked.</para>
    ///
    /// <para>SCOPE, said plainly: this is the HAND. A hand card genuinely burns (the classic
    /// "burn a card" cost, and the burn-available/burn-discarded prompts this fan's own board
    /// mirrors), and that is the case covered. A card burning in the PLAYED/round slots is a
    /// different surface with a different owner (<c>RemoteControlBoard</c> / <c>RemoteBoardCard</c>)
    /// and is NOT covered by this file — it is the next debt on this bit, not a thing this method
    /// silently half-does.</para>
    ///
    /// <para>WHY THE FRONTS GATE BOUNDS IT. <see cref="_handBuffer"/> is filled only while
    /// <c>RevealGate.ShowRoundCardFronts</c> is open, so during the secret selection phase there is
    /// no source widget and no plume. That is the safe direction twice over: nothing burns during
    /// selection, and a plume attached to ONE specific face-down slab would name WHICH card the
    /// owner is doing something to — the exact leak the backs-only rule exists to prevent.</para>
    ///
    /// <para>NOT YET SEEN ON HARDWARE, and the log says so. Whether a peer's own hidden
    /// <c>CardsHandUI</c> widget actually runs its <c>CardEffects</c> coroutine on THIS client is
    /// not established here; the game keeps that hand deactivated
    /// (<c>AbilityCardUI.ToggleFullCard(false)</c>). If it does not, this predicate never turns true
    /// and the failure is "no plume" — today's picture — never a plume on the wrong card. The
    /// ARMED line below is what makes that distinguishable in a log instead of a shrug: armed with
    /// no spawn means the TRIGGER needs a wire field, not the effect.</para>
    /// </summary>
    private void TickMirroredPlumes(int count, bool showFronts, bool mapFronts)
    {
        // The MAP phase has no AbilityCardUI at all (its faces come from this client's own
        // ObjectPool by card id), so there is nothing there that could be running a card effect.
        if (!_gameCardParticlesOn || !showFronts || mapFronts)
        {
            System.Array.Clear(_plume, 0, _plume.Length);
            return;
        }

        if (!_plumeArmedLogged)
        {
            _plumeArmedLogged = true;
            VRLog.Info("Net", $"Remote card plume ARMED [player {_owner.PlayerId}]: this owner has " +
                              "[Cards] GameCardParticles ON (wire id 236) and their hand widgets are " +
                              "resolved, so a burn/lost/discard on one of their HAND cards hosts the " +
                              "game's own CardSmoke on the matching slab (RemoteCardPlume). A log that " +
                              "carries this line and never a 'Remote card plume' spawn line proves the " +
                              "peer's hidden AbilityCardUI does not run its CardEffects coroutine on " +
                              "this client — i.e. the TRIGGER would need a wire field, not the effect.");
        }

        int n = Mathf.Min(count, Mathf.Min(_cards.Count, Mathf.Min(_handBuffer.Count, _plume.Length)));
        for (int i = 0; i < n; i++)
        {
            // ONE EXPRESSION, TWO CONSUMERS (2026-09-07). The three HasEffect calls used to be
            // spelled out here and again in RemoteBoardCard's recess resolver; the plume only needs
            // "is anything running", so it collapses the shared answer rather than keeping its own
            // copy of the terms. See UsedCardLook for why a fourth copy is the thing to avoid.
            AbilityCardUI? plumeWidget = _handBuffer[i];
            bool running = UsedCardLook.FromWidget(plumeWidget != null
                                                       ? plumeWidget.fullAbilityCard
                                                       : null)
                           != RemoteCardArt.CardFxLook.None;
            if (running == _plume[i])
                continue;
            _plume[i] = running;
            if (!running)
                continue;   // the falling edge only rearms the latch; the plume ends on its own
            GameObject? slab = _cards[i];
            if (slab != null)
                RemoteCardPlume.Spawn(slab.transform, _owner.PlayerId, i);
        }

        // Slots past the resolved window keep no stale latch: a shrunk hand must be able to plume
        // again at the same index without the fan having to be rebuilt first.
        for (int i = n; i < _plume.Length; i++)
            _plume[i] = false;
    }

    // ------------------------------------------------------- the GAME's card WASH (permanent) --

    /// <summary>Which card-FX look slab <c>i</c>'s face is currently wearing, and how far into the
    /// 2 s ramp it is. Parallel to <see cref="_faces"/>, cleared everywhere that list is.</summary>
    private readonly RemoteCardArt.CardFxLook[] _fxLook = new RemoteCardArt.CardFxLook[MaxCards];

    /// <inheritdoc cref="_fxLook"/>
    private readonly float[] _fxElapsed = new float[MaxCards];

    /// <summary>One-shot: the wash driver has actually written a look on this fan.</summary>
    private bool _fxLoggedOnce;

    /// <summary>
    /// Drive the game's own card-FX look — the CHAR of a burning card, the grey-out of a discarded
    /// or lost one — on the slabs of this peer's fan.
    ///
    /// <para>THIS SURFACE HAD NO WASH AT ALL UNTIL 2026-09-07, and the flow that convicts it is the
    /// one E1 is about: on a long rest the game re-Shows the owner's hand over their DISCARD pile
    /// and they pick a card to burn. Their card is an adopted LIVE <c>FullAbilityCard</c>, so
    /// <c>CardEffects.BurnCardTimeline</c> chars the whole face in front of them. The mirror's slab
    /// wears a CLONE whose <c>CardEffects</c> <c>RemoteCardArt</c> destroys on purpose, and nothing
    /// in this file drove the replacement rig — <c>grep -n "SetAbility"</c> over this file returned
    /// nothing. The only response the fan had was <see cref="TickMirroredPlumes"/>, gated on the
    /// owner's <c>[Cards] GameCardParticles</c> bit, which SHIPS FALSE. So every peer watched a
    /// bright, fresh card burn away on its owner's screen, which is the standing burn ruling read on
    /// the LOOK rather than on the face: the card is shown, and it is shown wrong.</para>
    ///
    /// <para>SAME TRIGGER AS THE PLUME, SAME 2 s RAMP AS THE RECESS, AND NO NEW DIAL. The look comes
    /// from <see cref="UsedCardLook.FromWidget"/> — the shared expression, read off the peer's own
    /// live widget — and it is NOT gated on the particle bit, because that bit is about smoke and
    /// this is about what the card looks like. The ramp is <see cref="UsedCardLook.RampSeconds"/>,
    /// the game's own duration, so the mirror's timing is the owner's timing.</para>
    ///
    /// <para>IT COMES OFF AGAIN. A look that falls back to <c>None</c> — the game's own
    /// <c>RestoreCard</c>, which runs when a card lands in the Hand or Activated pile — clears the
    /// rig rather than latching, the same one-way-ramp defect <c>RemoteBoardCard</c> paid for in
    /// its item-8a round.</para>
    ///
    /// <para><c>Surface</c> IS <c>FxSurface.HandFan</c>, which is the member this paragraph used to
    /// ask for. It read "DELIBERATELY LEFT Unnamed … that file was not handed to this lane", and
    /// the cost of that was not nothing: sharing index 0 with <c>RemoteCardFx</c>'s flight rig meant
    /// whichever surface armed first silenced the other's per-surface arming line for the process —
    /// the exact failure the latch exists to prevent (R2 NOTE 2, 2026-09-07). The enum is
    /// instrument-only — nothing behavioural reads it — so this was a blind LOG, never a wrong
    /// picture.</para>
    /// </summary>
    private void TickUsedCardFx(int count, bool showFronts, bool mapFronts)
    {
        // The MAP phase has no AbilityCardUI at all — its faces come from this client's own pool by
        // card id — so there is no widget whose effect state could be read, and a loadout card is
        // not in play in any case.
        if (!showFronts || mapFronts)
        {
            ClearUsedCardFx();
            return;
        }

        int n = Mathf.Min(count, Mathf.Min(_faces.Count, Mathf.Min(_handBuffer.Count, _fxLook.Length)));
        for (int i = 0; i < n; i++)
        {
            AbilityCardUI? widget = _handBuffer[i];
            RemoteCardArt.CardFxLook want =
                UsedCardLook.FromWidget(widget != null ? widget.fullAbilityCard : null);
            RemoteCardArt face = _faces[i];
            if (want != _fxLook[i])
            {
                RemoteCardArt.CardFxLook was = _fxLook[i];
                _fxLook[i] = want;
                _fxElapsed[i] = 0f;
                if (was != RemoteCardArt.CardFxLook.None && want == RemoteCardArt.CardFxLook.None)
                    face?.ClearAbilityCardFx();
            }
            if (want == RemoteCardArt.CardFxLook.None || face == null)
                continue;
            // NAME THE SURFACE (R2 NOTE 2, 2026-09-07). This driver used to leave Unnamed, so it
            // shared latch index 0 with RemoteCardFx's flight rig and whichever armed first
            // silenced the other's per-surface line for the whole process.
            face.Surface = RemoteCardArt.FxSurface.HandFan;
            if (_fxElapsed[i] < UsedCardLook.RampSeconds)
            {
                _fxElapsed[i] = Mathf.Min(UsedCardLook.RampSeconds,
                                          _fxElapsed[i] + Mathf.Max(0f, Time.unscaledDeltaTime));
            }
            bool took = face.SetAbilityCardFxProgress(want, _fxElapsed[i] / UsedCardLook.RampSeconds);
            if (took && !_fxLoggedOnce)
            {
                _fxLoggedOnce = true;
                // HW-VERIFY: the 2026-09-07 re-audit, TIER A. Grep token: REMOTE HAND CARD WASH.
                //
                // WORKING = this line appearing within a beat of the owner's own burn, on the flow
                // that convicts the defect (a long rest's burn step over the DISCARD arc), with the
                // mirrored slab reading as charred rather than fresh.
                //
                // INERT = a peer burns a hand card and this line never appears. The look comes off
                // the peer's HIDDEN CardsHandUI widget, and whether the game runs its CardEffects
                // coroutine on a deactivated hand on THIS client is NOT established here — the same
                // open question the plume's ARMED line was written to answer. If it does not, the
                // trigger owes a wire field (one look enum per fan seat, or a per-card FX record)
                // and the driver below is already correct for it.
                VRLog.Note("Net", $"REMOTE HAND CARD WASH [player {_owner.PlayerId}]: slab {i} of "
                    + $"{count} is wearing the game's own {want} look, driven from the owner's live "
                    + "FullAbilityCard through RemoteCardArt.SetAbilityCardFxProgress over "
                    + $"{UsedCardLook.RampSeconds:0.#} s. NOT gated on [Cards] GameCardParticles: "
                    + "that bit is the smoke plume, and before this build it was the ONLY response "
                    + "this fan had to a card being burnt — so with the dial at its shipped OFF a "
                    + "peer's burning hand card stayed bright and fresh for the whole animation. "
                    + "The clone cannot run the game's own timeline (RemoteCardArt destroys "
                    + "CardEffects on every clone on purpose), so this drives the same replacement "
                    + "rig the recess, the burn flight and the burnt pile already share.");
            }
        }

        // Slots past the resolved window keep no stale latch, and their faces are not ours to write.
        for (int i = n; i < _fxLook.Length; i++)
        {
            _fxLook[i] = RemoteCardArt.CardFxLook.None;
            _fxElapsed[i] = 0f;
        }
    }

    /// <summary>Forget every wash latch (fan hidden, rebuilt, or the gate shut). The FACES are not
    /// cleared here: a hidden fan's faces are torn down with it, and a rebuild mints new ones, so a
    /// write would be to an object that is about to die. What must not survive is the LATCH — a
    /// stale look would make the next card at that index skip the write that puts the wash on.
    /// </summary>
    private void ClearUsedCardFx()
    {
        for (int i = 0; i < _fxLook.Length; i++)
        {
            _fxLook[i] = RemoteCardArt.CardFxLook.None;
            _fxElapsed[i] = 0f;
        }
    }

    private void ClearPops()
    {
        for (int i = 0; i < _pop.Length; i++)
            _pop[i] = 0f;
        _loggedHighlight = -2;
    }

    /// <summary>Change-gated evidence that the synced highlight reached the render path (grep:
    /// "Remote hand fan highlight"). A future "the lift is still not synced" report is then
    /// answerable from the log alone: the sender's "Card highlight SENT" line, the receiver's
    /// "Tray anchor mode"/extras parse, and this line are the three points of the chain.</summary>
    private void LogHighlightIfChanged(int hovered)
    {
        if (hovered == _loggedHighlight)
            return;
        _loggedHighlight = hovered;
        VRLog.Info("Net", $"Remote hand fan highlight [{_owner.PlayerId}]: " +
                          $"index {(hovered >= 0 ? hovered.ToString() : "none")} of {_cards.Count} " +
                          $"slab(s) (wire index {_owner.HandHighlightIndex}) — the neighbours split " +
                          "apart on the authored FanSplit* curve and the card lifts on VRCard's own " +
                          "pop, from the INDEX alone (extension record 6: no card identity).");
    }

    // ----------------------------------------------------------------- gaze-facing bias (derived) --
    // [Cards] FanGazeBias, ported from CardFan.UpdateGazeBias term for term: an eased extra YAW about
    // world up that turns the whole fan ROOT partway toward where the owner is looking, so the
    // looked-at end of the arc tips toward them. OPT-IN and OFF in the shipped config, so every fan
    // drawn today is unchanged — biasYaw stays exactly 0 and ApplyGazeBias returns the billboard.
    //
    // WHY IT IS DERIVABLE. The yaw is a pure function of two things the rig packet already carries —
    // the head->fan vector and the head's GAZE, the same pair TrackGazeApex below already runs on —
    // and of six CONSTANTS that live in CardFan as private consts rather than in [Cards], so they are
    // copied here like every other presentation constant in this file. Nothing about the SHAPE needs
    // transmitting. The one input that is genuinely not derivable is the toggle itself: whether the
    // sender switched it on.
    //
    // RENDERER FIRST, FIELD SECOND — AND THE FIELD HAS SINCE LANDED (id 239, ModBuild 314). The
    // order was deliberate: this project's FanCloseDuration trap is a wire field whose receiver
    // ignores it, which turns scripts/check-wire-coverage.py green while the picture stays wrong, so
    // the picture was built first and the checker was left saying PENDING until it was real. It cost
    // exactly what the note predicted — one line in SyncTuning (`_gazeBiasOn = t.FanGazeBiasOn;`) and
    // nothing else in this file.
    //
    // THIS PARAGRAPH ITSELF SAT WRONG FOR A BUILD. It went on claiming "NOT on the wire yet" and
    // "the checker still says PENDING" after both had stopped being true, which is the exact shape
    // of the four defects on this project that each sat under a comment asserting a state the code
    // did not have. A note that describes a TRANSITION has to be retired by the change that
    // completes it; leaving it is not neutral, because the next reader trusts it over the code.
    //
    // MAGNITUDES at the shipped constants: no yaw at all until the gaze clears 20° off the fan
    // centre, smoothstep-ramped to full weight by 42°, 0.6 of the offset applied, hard-clamped at
    // 32°, eased at 9/s — so the visible swing runs 0° … 32° and the fan can never spin away from
    // the palm. How that 20° deadzone compares with the angle a hand actually subtends is argued in
    // CardFan's own ROUND-2 AUDIT NOTE beside these constants; read it before retuning either side,
    // and retune BOTH — a constant that drifts here and not there is a 1:1 defect by construction.

    /// <summary>
    /// Whether THIS OWNER has <c>[Cards] FanGazeBias</c> on.
    ///
    /// <para>WIRE-FED since ModBuild 314 — <see cref="NetProtocol.TuneFanGazeBiasOn"/> (id 239),
    /// read in <see cref="SyncTuning"/> as <c>_gazeBiasOn = t.FanGazeBiasOn;</c> beside the other
    /// mirrored dials. It falls back to the shipped default (<c>false</c>) only for a peer whose
    /// packet carries no record 28 at all, which is every untuned player — so an owner who has not
    /// touched the dial is drawn exactly as they were before the field existed.</para>
    /// </summary>
    private bool _gazeBiasOn = Defaults.FanGazeBias;

    /// <summary>Gaze offset (deg off "looking straight at the fan centre") the head must CLEAR before
    /// the bias commits to a side — CardFan.GazeBiasDeadzoneDeg. WIDE, and paired with
    /// <see cref="GazeBiasReleaseDeg"/>: once committed, a side only relaxes back to centre inside the
    /// smaller release band, so a head shake sweeping through centre cannot flip the lean's sign.</summary>
    private const float GazeBiasDeadzoneDeg = 20f;

    /// <summary>Gaze offset (deg) at which a committed side RELEASES back to centre — CardFan's
    /// hysteresis floor, below the deadzone.</summary>
    private const float GazeBiasReleaseDeg = 10f;

    /// <summary>Gaze offset (deg) at which the bias reaches full weight, smoothstep-ramped from the
    /// deadzone — CardFan.GazeBiasFullDeg (~the half-arc a full hand subtends).</summary>
    private const float GazeBiasFullDeg = 42f;

    /// <summary>Fraction of the gaze offset the fan turns at full weight — CardFan.GazeBiasGain.
    /// Below 1 on purpose: it opens the gazed edge forward without full gaze-lock swim.</summary>
    private const float GazeBiasGain = 0.6f;

    /// <summary>Hard clamp on the applied extra yaw (deg) — CardFan.GazeBiasMaxYawDeg.</summary>
    private const float GazeBiasMaxYawDeg = 32f;

    /// <summary>Exponential ease rate (1/s) of the applied yaw toward its target —
    /// CardFan.GazeBiasSmoothing. A CONSTANT on both sides, so unlike <see cref="_gazeSmoothing"/>
    /// there is no rate here for an owner to have tuned out from under the mirror.</summary>
    private const float GazeBiasSmoothing = 9f;

    /// <summary>Eased state: extra yaw (deg about world up) currently applied to this fan's root —
    /// CardFan._gazeBiasYaw.</summary>
    private float _gazeBiasYaw;

    /// <summary>Hysteresis state: which side the bias is COMMITTED to (0 = centre, -1 = the owner's
    /// left / card i=0, +1 = right) — CardFan._gazeSide. The target's SIGN comes from this latch and
    /// not from the live signed angle, which is what stops the dither.</summary>
    private int _gazeSide;

    /// <summary>Throttle clock for the gaze-bias diagnostic (unscaled seconds of the last line).</summary>
    private float _gazeBiasLogTime;

    /// <summary>
    /// Compute + ease the owner's gaze-facing bias — CardFan.UpdateGazeBias, term for term.
    /// <paramref name="away"/> is head->fan (the base billboard forward), <paramref name="headForward"/>
    /// is their gaze, and the return is the eased extra yaw in degrees about world up (0 = inside the
    /// centre dead zone, or eased back below the 0.05° floor). Allocation-free.
    /// </summary>
    private float UpdateGazeBias(Vector3 away, Vector3 headForward, float dt)
    {
        Vector3 up = Vector3.up;
        Vector3 awayH = Vector3.ProjectOnPlane(away, up);
        Vector3 gazeH = Vector3.ProjectOnPlane(headForward, up);

        float target = 0f;
        float gazeOffset = 0f;
        if (awayH.sqrMagnitude > 1e-6f && gazeH.sqrMagnitude > 1e-6f)
        {
            // Signed horizontal angle of the gaze off "looking straight at the fan centre".
            // AngleAxis(gazeOffset, up) rotates awayH exactly onto gazeH, so turning the fan toward
            // the gaze is sign-consistent with ApplyGazeBias's composition.
            gazeOffset = Vector3.SignedAngle(awayH, gazeH, up);
            float mag = Mathf.Abs(gazeOffset);
            int side = gazeOffset < 0f ? -1 : 1;

            // The committed side is a LATCH, not the live sign: centre commits only past the wide
            // deadzone, a committed side releases only inside the smaller band, and an opposite side
            // is taken only on a firm past-deadzone crossing.
            if (_gazeSide == 0)
            {
                if (mag > GazeBiasDeadzoneDeg)
                    _gazeSide = side;
            }
            else if (mag < GazeBiasReleaseDeg)
            {
                _gazeSide = 0;
            }
            else if (side != _gazeSide && mag > GazeBiasDeadzoneDeg)
            {
                _gazeSide = side;
            }

            if (_gazeSide != 0)
            {
                float t = Mathf.Clamp01((mag - GazeBiasDeadzoneDeg)
                                        / Mathf.Max(0.01f, GazeBiasFullDeg - GazeBiasDeadzoneDeg));
                t = t * t * (3f - 2f * t); // smoothstep ease-in/out of the weight
                target = Mathf.Clamp(_gazeSide * mag * GazeBiasGain * t,
                                     -GazeBiasMaxYawDeg, GazeBiasMaxYawDeg);
            }
        }
        else
        {
            _gazeSide = 0;
        }

        // Eased on the caller's already-unscaled dt, clamped like CardFan clamps unscaledDeltaTime so
        // one long frame cannot snap the lean.
        float d = Mathf.Min(Mathf.Max(dt, 0f), 0.05f);
        _gazeBiasYaw = Mathf.Lerp(_gazeBiasYaw, target, 1f - Mathf.Exp(-GazeBiasSmoothing * d));
        if (Mathf.Abs(_gazeBiasYaw) < 0.05f)
            _gazeBiasYaw = 0f;

        // Throttled diagnostic in the same shape and units as CardFan's own, so an owner's log and a
        // viewer's log can be read side by side — the only way to see that a peer's fan leans the
        // same WAY and by the same number of DEGREES as the fan its owner is holding.
        float now = Time.unscaledTime;
        if ((Mathf.Abs(gazeOffset) > GazeBiasDeadzoneDeg || Mathf.Abs(_gazeBiasYaw) > 0.5f)
            && now - _gazeBiasLogTime > 1f)
        {
            _gazeBiasLogTime = now;
            string end = _gazeSide == 0 ? "CENTER" : _gazeSide < 0 ? "LEFT" : "RIGHT";
            VRLog.Info("Net",
                $"Remote hand fan [player {_owner.PlayerId}] gaze-bias: gazeOff={gazeOffset:F1}deg " +
                $"committed={end} biasYaw={_gazeBiasYaw:F1}deg (n={_cards.Count})");
        }

        return _gazeBiasYaw;
    }

    // ---------------------------------------------------------------- card presentation (derived) --
    // Verbatim ports of CardFan's presentation maths, driven by the peer's already-synced head. The
    // formulas (and the round-2 root causes behind their exact shape — one shared corner at the fan
    // centre, the bow going the wrong way, the stacking clamp making the response one-sided) are
    // documented at length in Cards/CardFan.cs; they are NOT restated here, because the whole point
    // is that this is the same function of the same inputs. If CardFan's shape is ever retuned, these
    // constants are the list of things to re-seed.

    /// <summary>Track + ease the fan-local X where the OWNER's gaze crosses their fan plane — the
    /// depth-bow apex. CardFan.UpdateCardPresentation, with the peer's head holder standing in for
    /// the local HMD (the rig packet carries their head ROTATION, so the gaze is real, not a
    /// guess).</summary>
    private void TrackGazeApex(Transform root, Transform head, int n, float dt)
    {
        Vector3 headLocal = root.InverseTransformPoint(head.position);
        Vector3 gazeLocal = root.InverseTransformDirection(head.forward);
        float edgeX = ArcHalfWidth(n);

        float targetX = 0f;
        if (headLocal.z < 0f)
        {
            float front = Mathf.Clamp01((gazeLocal.z - GazeGrazeMinZ)
                                        / Mathf.Max(0.01f, GazeGrazeFullZ - GazeGrazeMinZ));
            front = front * front * (3f - 2f * front);
            if (front > 0f)
            {
                float cross = headLocal.x
                              + gazeLocal.x * (-headLocal.z / Mathf.Max(gazeLocal.z, GazeGrazeMinZ));
                targetX = Mathf.Clamp(cross, -edgeX, edgeX) * front;
            }
        }

        float d = Mathf.Min(Mathf.Max(dt, 0f), 0.05f);
        _gazeX = Mathf.Lerp(_gazeX, targetX, 1f - Mathf.Exp(-_gazeSmoothing * d));
    }

    /// <summary>The arc's own half-width in fan-local metres (CardFan.ArcHalfWidth).</summary>
    private float ArcHalfWidth(int n)
    {
        if (n < 2)
            return _radius;
        float step = Mathf.Min(_perCardStepDegrees, _arcSweepDegrees / (n - 1));
        float half = step * (n - 1) * 0.5f;
        return Mathf.Max(0.001f, Mathf.Sin(half * Mathf.Deg2Rad) * _radius);
    }

    /// <summary>The gaze apex as a FRACTIONAL card index (CardFan.GazeApexIndex).</summary>
    private float GazeApexIndex(int n)
    {
        float center = (n - 1) * 0.5f;
        if (n < 2 || _gazeApexFollow <= 0f)
            return center;
        float step = Mathf.Min(_perCardStepDegrees, _arcSweepDegrees / (n - 1));
        if (step <= 0.0001f)
            return center;
        float start = -step * (n - 1) * 0.5f;
        float angle = Mathf.Asin(Mathf.Clamp(_gazeX / _radius, -1f, 1f)) * Mathf.Rad2Deg;
        return Mathf.Clamp((angle - start) / step, 0f, n - 1f);
    }

    /// <summary>The resting, gaze-independent symmetric cup (CardFan.RestBowDepth).</summary>
    private float RestBowDepth(int i, int n)
    {
        if (n < 2 || _sideDepthCurve == 0f || n <= _curveMinCards)
            return 0f;
        float half = (n - 1) * 0.5f;
        float frac = Mathf.Clamp01(Mathf.Abs(i - half) / half);
        int hi = Mathf.Max(_curveMinCards + 1, _maxHandForCurve);
        float fill = Mathf.Clamp01((float)(n - _curveMinCards) / (hi - _curveMinCards));
        return _sideDepthCurve * Mathf.Pow(frac, _curvePower) * fill;
    }

    /// <summary>The resting cup with the gaze RELIEF applied (CardFan.BowDepth): the card under the
    /// apex comes fully out of the bow, every other card keeps at most what it had at rest.</summary>
    private float BowDepth(int i, int n, float apex)
    {
        float rest = RestBowDepth(i, n);
        if (rest == 0f || _gazeApexFollow <= 0f)
            return rest;
        float w = Mathf.Max(GazeReliefMinWidth, (n - 1) * 0.5f * GazeReliefWidthFactor);
        float d = (i - apex) / w;
        return rest * (1f - _gazeApexFollow * Mathf.Exp(-d * d));
    }

    /// <summary>Compose every card's fan-local Z (stagger + bow + stacking clamp) and return the
    /// apex used (CardFan.ComposeDepths). The clamp keeps each card one stagger in FRONT of its
    /// predecessor so a strong bow can curl the hand away but never re-order it.</summary>
    private float ComposeDepths(int n)
    {
        int count = Mathf.Min(n, _depths.Length);
        float apex = GazeApexIndex(n);
        for (int i = 0; i < count; i++)
            _depths[i] = -ZStagger * i + BowDepth(i, n, apex);
        if (_sideDepthCurve > 0f)
        {
            for (int i = 1; i < count; i++)
            {
                float ceiling = _depths[i - 1] - ZStagger;
                if (_depths[i] > ceiling)
                    _depths[i] = ceiling;
            }
        }
        return apex;
    }

    /// <summary>Hardware-log seam for the remote fan's ORIENTATION: one line per card-count change
    /// and a slow throttled line while a fan is up, carrying the numbers the next log has to check
    /// (anchor frame, apex tracking, toe-in, bow). Counts and geometry only — never identities.</summary>
    private void LogGeometry(int n, float apex, float maxToeDeg, bool haveHead)
    {
        float now = Time.unscaledTime;
        bool countChanged = n != _loggedCount;
        if (!countChanged && now - _geometryLogTime < 5f)
            return;
        _loggedCount = n;
        _geometryLogTime = now;
        VRLog.Info("Net", $"Remote hand fan [player {_owner.PlayerId}] geometry: n={n} "
            + $"anchor={(_loggedPalmAnchor ? "palm" : "handRoot")} head={(haveHead ? "yes" : "no")} "
            + $"gazeX={_gazeX * 1000f:F0}mm/edge={ArcHalfWidth(n) * 1000f:F0}mm apex={apex:F2} "
            + $"toeInMax={maxToeDeg:F1}deg bowEdge={BowDepth(0, n, apex) * 1000f:F1}mm "
            + $"(derived from the peer's synced head — no wire field).");
    }

    // ------------------------------------------------------------------ build / teardown --

    private void EnsureRoot(Transform holder)
    {
        // SELF-HEAL against EXTERNAL destruction (root cause of the 2026-08 MP hardware log's
        // repeating "RemoteHandFan.LayoutCards … get_transform NRE", hundreds of hits). The fan
        // subtree is parented under the avatar's HAND HOLDER, so anything that clears that
        // holder's children (RemoteAvatar.BuildHands used to sweep ALL of them on a hand-style
        // rebuild — fixed to spare attachments, but any future writer or a scene-side destroy
        // hits the same seam) kills _root and every slab in _cards while this class's
        // bookkeeping survives. _root == null then reads TRUE again (Unity's destroyed-object
        // null), a fresh root was created — but _cards still listed the DESTROYED slabs and
        // _builtCount still matched the live count, so Rebuild never ran and LayoutCards deref'd
        // dead GameObjects every frame, forever. The heal is structural, at the state-mutation
        // boundary rather than a null-skip in the hot loop: whenever the root (or any slab) is
        // found dead, drop ALL stale slab/face bookkeeping and invalidate _builtCount so the
        // caller's `count != _builtCount` check funnels straight into a full Rebuild this frame.
        bool rootDied = _root == null;
        if (rootDied && (_cards.Count > 0 || _faces.Count > 0))
        {
            // The GameObjects are already gone (destroyed with the old root); RemoteCardArt.Destroy
            // is Unity-null-tolerant and still releases any clone bookkeeping that survived.
            for (int i = _faces.Count - 1; i >= 0; i--)
                _faces[i].Destroy();
            _faces.Clear();
            _cards.Clear();
            _seeded.Clear();       // index-aligned with _cards; a stale entry would ease a fresh slab out of the root origin
            _bodyWearsBack.Clear();// index-aligned with _cards; a stale FALSE would skip a body write
            _mapPrinted.Clear();   // index-aligned with _faces; stale entries would claim prints that no longer exist
            ClearUsedCardFx();     // index-aligned with _faces; a stale look would skip the next card's wash
            // The outgoing wave hung off the same dead root — drop its bookkeeping with the rest, or
            // TickSwap would drive destroyed transforms every frame (the very defect this heal
            // exists for, one list over).
            EndSwap();
            _shownActorId = 0;
            _shownActor = null;
            _swapIdentity = default;
            _builtCount = -1;
            _frontsShown = false;
            ClearPops();
            ClearReturnGlide(); // the slab the glide named no longer exists
            VRLog.Warn("Net", $"Remote hand fan [player {_owner.PlayerId}]: fan root was destroyed " +
                              "externally — stale slab list dropped, fan rebuilds this frame " +
                              "(self-heal; see EnsureRoot).");
        }
        else if (!rootDied && _builtCount > 0)
        {
            // Root alive but a SLAB died (partial external destruction): same heal, same funnel.
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] == null)
                {
                    _builtCount = -1; // Rebuild destroys survivors + recreates the full set
                    VRLog.Warn("Net", $"Remote hand fan [player {_owner.PlayerId}]: card slab {i} was " +
                                      "destroyed externally — fan rebuilds this frame (self-heal).");
                    break;
                }
            }
        }

        if (rootDied)
        {
            _root = new GameObject($"GloomhavenVR.RemoteHandFan[{_owner.PlayerId}]");
            _root.transform.localScale = Vector3.one; // inherit AppliedScale from the holder
            _root.SetActive(false);
            _holder = null; // a fresh root must ALWAYS reparent, even onto the same holder object

            // USER ITEM 11a (2026-09): "Die Faecher vor dem Brett sind nicht mit transparent in
            // der Auswahlphase (wo nur die Rueckseiten sichtbar sind), sollen sie aber sein."
            //
            // This fan is parented to the peer's HAND HOLDER, so it is in neither the peer-board
            // see-through's subtree census nor — until now — its follower registry, and a peer's
            // hand fan hung fully solid in front of a board that was dissolving behind it.
            //
            // HandOwned, AND THIS REVERSES ITEM 11a FOR THIS SURFACE. User, 2026-09-07, verbatim:
            // "Der Handfaecher des Mitspielers wird transparent wenn das Board des Mitspielers
            // wegen Verdeckung transparent wird - das will ich nicht. Nur die Karten auf dem Board
            // selber sollen auch transparent werden." So the rule that mattered in item 11a — a fan
            // parked over the board counts as board content — is exactly the rule he is now
            // refusing. It was WhileOverBoard, which admitted the fan whenever the positional test
            // said it was over the board; the ModBuild 474 census read "6 follower root(s) fade
            // with this board ... and 0 registered root(s) are currently HELD OUT", four of those
            // six being unconditional, so both hand-anchored roots were being admitted every time.
            //
            // The classification the old sentence rested on is still TRUE and is not what changed:
            // a hand fan IS avatar content wherever its owner takes it. What changed is that being
            // parked over the board no longer makes it board content for this purpose.
            //
            // STILL REGISTERED, DELIBERATELY, AND REFUSED AT THE CENSUS INSTEAD, so "does not fade"
            // and "was never registered" stay distinguishable in one grep — see
            // PeerBoardFade.FollowRule.HandOwned, and RemoteEmptyFanHint, which takes the same
            // membership because it IS this fan whenever the hand is empty.
            //
            // REGISTERED HERE, at the one place a NEW root transform comes into existence, so the
            // self-heal above — which destroys and recreates this object wholesale — re-registers
            // as a matter of course. Registration is idempotent, and the see-through sweeps
            // Unity-null roots on its own census, so the dead one needs no teardown call.
            PeerBoardFade.Follow(_owner.PlayerId, _root.transform,
                                 PeerBoardFade.FollowRule.HandOwned);
        }

        // (Re)parent when the non-dominant holder changes (e.g. the sender flips dominant hand)
        // or when the root was just recreated (the old code compared holder identity only, so a
        // recreated root whose holder had not changed was never parented at all and floated at
        // the scene origin).
        if (_holder != holder)
        {
            _holder = holder;
            // `!` because the compiler cannot carry the null-state across the local `bool`:
            // `rootDied = _root == null` above, and the `if (rootDied)` branch assigns a fresh
            // root. Past that branch _root is non-null on both paths. Compiles to identical IL.
            _root!.transform.SetParent(holder, worldPositionStays: false);
            _poseInit = true; // snap to the new hand rather than easing across the body
        }
    }

    /// <summary>The <see cref="RemoteAvatar.BoardTuningRevision"/> the geometry fields below were
    /// last refreshed at (−1 = never). Latched rather than value-compared: the resolve already
    /// happens once per real change in <see cref="RemoteAvatar"/>.</summary>
    private int _tuningRevision = -1;

    /// <summary>
    /// Pull the owner's own fan geometry out of their resolved tuning (extension record 28) when it
    /// has actually changed. Everything here is either the value they set or — for every dial they
    /// have not touched — this client's shipped constant, which is the same number, so an untuned
    /// peer's fan is unchanged from every previous build.
    ///
    /// <para>A change in the CARD SIZE invalidates <see cref="_builtCount"/> so the slabs are
    /// rebuilt at the new size on this same frame; the angular/curve dials need no rebuild because
    /// the layout recomputes every card's pose every frame anyway.</para>
    /// </summary>
    private void SyncTuning()
    {
        if (_tuningRevision == _owner.BoardTuningRevision)
            return;
        _tuningRevision = _owner.BoardTuningRevision;
        RemoteBoardTuning t = _owner.BoardTuning;
        _cardDustOn = t.CardDustOn;
        _gameCardParticlesOn = t.GameCardParticlesOn;
        // The rate a released card flies home at — the owner's own, clamped the way RemoteItemFan
        // clamps its twin so a zero can never freeze a glide mid-air.
        _lerpSpeed = Mathf.Max(0.5f, t.CardLerpSpeed);

        float width = t.CardWidth > 0.001f ? t.CardWidth : DefaultCardWidth;
        bool sizeChanged = !Mathf.Approximately(width, _cardWidth);
        _cardWidth = width;
        _cardHeight = width * (88f / 63.5f);

        _palmOffset = t.FanPalmOffset;
        _radius = t.FanEffectiveRadius;
        _arcSweepDegrees = t.FanArcSweepDegrees;
        _perCardStepDegrees = t.FanPerCardStepDegrees;
        _archFactor = t.FanFlatCurvatureFactor;
        _tiltFactor = t.FanTiltFactor;
        _maxHandForCurve = Mathf.Max(1, t.FanMaxHandForCurve);
        // The GATE over the two factors above, not another factor: with it off CardFan uses them at
        // every hand size (the legacy, pre-Demeo look) instead of scaling them by how full the hand is.
        _curveByFillOn = t.FanCurveByFillOn;
        _faceViewer = t.FanFaceViewer;
        _sideDepthCurve = t.FanSideDepthCurve;
        _curvePower = t.FanCurvePower;
        _curveMinCards = Mathf.Max(0, t.FanCurveMinCards);
        _gazeApexFollow = t.FanGazeApexFollow;
        // The gaze-bias TOGGLE. Renderer-first: the yaw has been derived in this file since the
        // renderer landed, idle behind this bit. NOT a shape parameter — every one of those is a
        // const on both sides, which is why this dial costs exactly one bit and not seven fields.
        _gazeBiasOn = t.FanGazeBiasOn;
        // The RATE, not just the amplitude. RemoteBoardTuning has already applied the owner's own
        // 1..30 clamp, so this is a rate they could actually have been easing at.
        _gazeSmoothing = t.FanGazeSmoothing;
        // The fan's FOLLOW rate (id 181). RemoteBoardTuning has already applied the owner's own
        // 0..60 clamp, and deliberately does NOT clamp zero away: zero is the rigid branch, not a
        // degenerate value, so it has to survive the wire to reach the branch in TickPose.
        _followSmoothing = t.FanFollowSmoothing;
        _splitMultiplier = t.FanSplitMultiplier;
        _splitFalloff = t.FanSplitFalloff;
        _splitScale = t.FanHoverSplitScale;
        _popForward = t.FanSelectedPopForward;
        _swapDuration = t.FanSwapDuration;
        _swapStagger = t.FanSwapStagger;
        _swapOverlap = t.FanSwapOverlap;
        _swapTravel = t.FanSwapTravel;
        _swapArc = t.FanSwapArc;
        _swapSpinDegrees = t.FanSwapSpinDegrees;
        _swapSeedScale = t.FanSwapSeedScale;
        _swapSettleOvershoot = t.FanSwapSettleOvershoot;
        // The REVEAL's own timing (ids 154..155), wire-borne only since the record was paged — see
        // the field declarations. Guarded above zero because a zero duration would divide by it in
        // OpenProgress; a config range cannot reach 0, but a wire value is never trusted.
        _openSeconds = t.FanOpenDuration > 0.001f ? t.FanOpenDuration : Defaults.FanOpenDuration;
        // NOT floored away from zero like the open is: zero is a MEANING here ("vanish instantly"),
        // not a missing value, and an owner who set it must not be given an animation they turned
        // off. The open has no such setting, which is why its guard can treat 0 as absent.
        _closeSeconds = Mathf.Max(0f, t.FanCloseDuration);
        _openStagger = t.FanOpenStagger >= 0f ? t.FanOpenStagger : 0f;

        if (sizeChanged)
            _builtCount = -1;
        _faceRevision = -1; // the printed rect is a function of the card size — re-resolve it
    }

    /// <summary>
    /// Re-resolve the PRINTED face rectangle the slab bodies are scaled to (see
    /// <see cref="_visibleFace"/>) whenever this client learns a new face pixel size — i.e. the
    /// first time it hosts an ability card of its own, and never again in a normal session. A
    /// change invalidates <see cref="_builtCount"/> so the slabs are rebuilt at the corrected size
    /// on the same frame.
    ///
    /// <para>Revision-gated rather than value-compared for the same reason <see cref="SyncTuning"/>
    /// is: this runs once per fan per frame and the answer changes at most once per session.</para>
    /// </summary>
    private void SyncFaceRect()
    {
        if (_faceRevision == CardFace.FacePixelsRevision)
            return;
        _faceRevision = CardFace.FacePixelsRevision;
        Vector2 vis = CardFace.VisibleFaceRect(DefaultCardWidth, DefaultCardHeight);
        if (Mathf.Approximately(vis.x, _visibleFace.x) && Mathf.Approximately(vis.y, _visibleFace.y)
            && _loggedFaceRect)
            return;
        _visibleFace = vis;
        _builtCount = -1;

        // THE MEASUREMENT LINE (report 12). Grep: "Remote hand fan face rect". It states the two
        // rectangles and the margin between them in millimetres, so the next hardware round CHECKS
        // the fix rather than eyeballing the screenshot. A log whose margin is not 0.00 x 0.00 mm
        // disproves the claim; a log whose printed size is not the owner's own printed size
        // (VRCard's backing fit, the "CARD FACE RECT" line from Cards) disproves the 1:1 half.
        _loggedFaceRect = true;
        float ratio = _cardWidth / DefaultCardWidth;
        VRLog.Info("Net", $"Remote hand fan face rect [player {_owner.PlayerId}]: face "
            + $"{CardFace.ObservedFacePixels.x:F0}x{CardFace.ObservedFacePixels.y:F0} px letterboxed into the "
            + $"{DefaultCardWidth * 1000f:F1}x{DefaultCardHeight * 1000f:F1} mm nominal card PRINTS "
            + $"{vis.x * 1000f:F2}x{vis.y * 1000f:F2} mm; the slab BODY is now scaled to exactly that "
            + $"(x{vis.x / DefaultCardWidth:F4}, x{vis.y / DefaultCardHeight:F4}), so the margin of card-back "
            + "showing around the print is 0.00x0.00 mm. Before this build the body stayed at the nominal "
            + $"card and that margin measured {(DefaultCardWidth - vis.x) * 500f:F2} mm per side at the "
            + $"SIDES and {(DefaultCardHeight - vis.y) * 500f:F2} mm per side at the ENDS. "
            + $"Owner scale x{ratio:F3}. Same rect the LOCAL card's backing "
            + "is fit to (VRCard.SetCanvasSize) — no wire field, the face size is read off this client's "
            + "own hosted card widget.");
    }

    private readonly int[] _reflowFrom = new int[MaxCards];

    /// <summary>Destroy and recreate exactly <paramref name="count"/> back-on-both-faces slabs, each
    /// with its own (initially hidden) cloned-front overlay. Only called when the count changes
    /// (cheap). Re-applies the mod layer so the owned head camera renders the new slabs.</summary>
    private void Rebuild(int count)
    {
        // ─── CARRY A LIVE RETURN GLIDE ACROSS THE TEARDOWN (report item 3, 2026-09-06) ──────────
        // The slab this glide is moving is about to be DESTROYED, and its replacement is created at
        // the arc seat the glide was easing toward. Without this the flight silently became a lerp
        // from the destination to the destination — no motion, no line, and it self-cleared 0.35 s
        // later as if it had played. The count moving mid-flight is not exotic: a release IS a count
        // change, and the owner's own Rebuild re-applies their session fan order right behind it.
        //
        // The owner's card does not stop when their fan re-lays out — CardFan.Relayout hands every
        // card a new home and VRCard's standing lerp keeps carrying it — so the mirror must not
        // stop either. The pose is banked in WORLD space (the fan root is re-posed every frame off
        // a moving hand) and re-stamped by LayoutCards onto whichever slab lands at that index.
        if (_returnIndex >= 0 && _returnGlide > 0f)
        {
            if (_returnIndex >= count)
            {
                // The seat the glide was flying to no longer exists — the hand shrank under it.
                ClearReturnGlide();
            }
            else if (!_returnSeedPending && _returnIndex < _cards.Count && _cards[_returnIndex] != null)
            {
                Transform live = _cards[_returnIndex].transform;
                _returnSeedPos = live.position;
                _returnSeedRot = live.rotation;
                _returnSeedPending = true;
            }
        }

        // Preserve each surviving slab's actual pose across the rebuild. Record 36 names a
        // MODEL seat, while these transforms are in the previously APPLIED arc order. The old
        // direct index shift moved the wrong neighbours after a user reorder. Record 44 already
        // carries the incoming arc order; mapping both generations through model seats preserves
        // the same card even when the two arrays have different lengths. No face lookup is needed.
        bool carry = false;
        if (_builtCount > 0 && _cards.Count == _builtCount)
        {
            int heldSeat = -1;
            if (count == _builtCount - 1
                && _owner.SingleHeldHandSeat(out int liveSeat, out int liveLen, out _)
                && liveLen == count + 1)
                heldSeat = liveSeat;
            else if (count == _builtCount + 1 && _fistCount == _builtCount)
                heldSeat = _fistSeat;
            if (heldSeat >= 0)
                carry = FanReflowMap.TryBuild(_builtCount, count, heldSeat,
                    _appliedOrder, _appliedOrderCount, _owner.FanArcOrder, _owner.FanArcOrderCount, _reflowFrom);
            else if (count == _builtCount)
            {
                carry = true;
                for (int i = 0; i < count; i++) _reflowFrom[i] = i;
            }
        }

        _carryPos.Clear();
        _carryRot.Clear();
        _carryScale.Clear();
        if (carry)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] == null)
                {
                    // A dead slab has no pose to carry, and a partial carry must not shift the map
                    // under the survivors — refuse the whole thing, which lands on today's snap.
                    _carryPos.Clear();
                    _carryRot.Clear();
                    _carryScale.Clear();
                    carry = false;
                    break;
                }
                Transform t = _cards[i].transform;
                _carryPos.Add(t.localPosition);
                _carryRot.Add(t.localRotation);
                _carryScale.Add(t.localScale.x);
            }
        }

        // Tear down existing front overlays first (each owns cloned game widgets — no leaks), then the
        // slabs they hang off.
        for (int i = _faces.Count - 1; i >= 0; i--)
            _faces[i].Destroy();
        _faces.Clear();
        // The per-slab map-print latch is INDEX-ALIGNED with _faces, so it dies with them: a fresh
        // slab must re-print rather than inherit the id of the slab that used to be at its index.
        _mapPrinted.Clear();

        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        _seeded.Clear();
        _bodyWearsBack.Clear();   // index-aligned with _cards; a stale FALSE would skip a body write
        _frontsShown = false;
        ClearPops(); // a rebuilt fan must never open with a stale card already lifted
        ClearUsedCardFx(); // …and never with the last card's wash latched at this index

        // Ability KIND so a peer's card backs take the same silhouette clip the owner's do — the MP
        // 1:1 rule applies to the card's SHAPE as much as to its content.
        Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability); // shared: back texture on a Standard material

        // THE BODY IS SIZED TO THE FACE IT WILL WEAR (report 12, 2026-08-15) — see _visibleFace.
        Vector2 vis = _visibleFace;

        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Card{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            // The SLAB ROOT stays UNIFORM: RemoteCardArt hangs its world-space face canvas off this
            // transform, and a non-uniform scale here would stretch the printed art. The owner's own
            // CardWidth arrives as that uniform scale (record 28), so their ghost cards read the size
            // they see.
            //
            // THAT SENTENCE WAS FALSE FROM THE BUILD THAT WROTE IT UNTIL 2026-09-06, and it is the
            // reason nobody found the breach: LayoutCards overwrote this the same frame with a
            // product carrying no width term, so every peer's fan was drawn at the NOMINAL card
            // however they had tuned it. The seed below is now the pose the layout eases toward
            // rather than a value it deletes — see SlabScale, which is the same expression and is
            // what LayoutCards multiplies by.
            card.transform.localScale = Vector3.one * SlabScale;

            // …and the BODY, one level down, carries the non-uniform squash onto the face rect. This
            // is VRCard.SetCanvasSize's backing fit, term for term: the local card scales its backing
            // mesh to facePixels × fit × VisibleFaceFraction so that slab and print are the SAME
            // rectangle. This fan used to skip that step and leave the slab at the full nominal
            // 63.5 × 88 mm, which is exactly the reported rim of card-back braid around a peer's
            // print — and, unreported, made a peer's card 17.5 % wider than the owner's own.
            var body = new GameObject("Body");
            body.transform.SetParent(card.transform, worldPositionStays: false);
            body.transform.localScale = new Vector3(vis.x / DefaultCardWidth, vis.y / DefaultCardHeight, 1f);
            var mf = body.AddComponent<MeshFilter>();
            // Round 17 (1:1 board rule): the ghost card adopts the owner's PUNCHED-OUT body via
            // CardMesh.AttachBody — the shared materials lost their alpha cutout, so a hand-built
            // rectangle here would read as the pre-silhouette full rectangle. AttachBody registers
            // the filter and upgrades it in place the moment the Ability contour is learned.
            CardMesh.AttachBody(mf, CardBodyKind.Ability, DefaultCardWidth, DefaultCardHeight);
            var mr = body.AddComponent<MeshRenderer>();
            // The body mesh carries TWO submeshes (front+rim | back). This fan deliberately shows
            // the BACK texture on both faces — hidden information — so the shared back material
            // wears both slots instead of the owner's [edge, back] pair.
            mr.sharedMaterials = new[] { back, back };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // NO COLLIDER, AND THAT IS THE FIX (report 2, 2026-09-07). A trigger box plus a
            // BorrowTarget used to live on this slab root so the shared fan sweep could elect it and
            // hand the player a read-only copy of a teammate's card. Both are gone: a slab in a
            // PEER'S fan is a purely cosmetic ghost with no collider anywhere in its subtree, so
            // ProximityGrabber — which elects over colliders — has nothing here to win. See the
            // section header above for the ruling and for why the own-board fan is untouched.

            int from = carry ? _reflowFrom[i] : -1;
            bool carried = from >= 0 && from < _carryPos.Count;
            if (carried)
            {
                card.transform.localPosition = _carryPos[from];
                card.transform.localRotation = _carryRot[from];
                card.transform.localScale = Vector3.one * _carryScale[from];
                _reflowEased++;
            }
            else
            {
                _reflowSnapped++;
            }
            _seeded.Add(carried);
            // …AND A CARD THAT IS CONTINUING DOES NOT APPEAR. The dust is the mirror of the owner's
            // card-appear puff, and the owner's cards do not appear when somebody picks one out of
            // the fan — they stay put and slide. Emitting it for a carried slab would puff every
            // card in the hand on every pluck, which is a divergence this reflow would otherwise
            // have made far more visible.
            if (!carried)
                EmitMirroredCardDust(card, appear: true);
            _cards.Add(card);
            // NOMINAL size, not the owner's tuned one: the slab root carries SlabScale, so handing
            // the tuned width here would fit the face a second time and square the ratio —
            // invisible at the shipped default (ratio 1), a real mismatch for any peer who had
            // moved [Cards] CardWidth. THE PREMISE ONLY BECAME TRUE ON 2026-09-06: LayoutCards used
            // to strip that scale back off the root every frame, so this face was nominal on a
            // nominal slab and the two agreed by accident. Nothing here changes; it is now right
            // for the reason it always claimed.
            _faces.Add(new RemoteCardArt(card.transform, DefaultCardWidth, DefaultCardHeight));
        }

        _builtCount = count;
        _lastCarried = 0;
        for (int i = 0; i < _seeded.Count; i++)
        {
            if (_seeded[i])
                _lastCarried++;
        }
        _lastBuilt = count;
        _lastRebuildAt = Time.unscaledTime;

        // Owned head camera renders the mod layer only; put the whole fan subtree on it (no-op
        // when VR is not running, exactly like the local fan/hands).
        VRLayers.Apply(_root!);

        // THE LAYER-2 RE-STAMP THAT USED TO FOLLOW IS GONE WITH THE COLLIDER IT PROTECTED (report 2,
        // 2026-09-07). Each slab root used to carry a borrow trigger box, and RayInteractor's world
        // pick is a real Physics.Raycast against Physics.DefaultRaycastLayers WITH TRIGGERS ENABLED
        // (RayInteractor.cs:590) — the mod layer is an ordinary layer inside that mask, so a trigger
        // floating around every peer's hand would have stolen hex, figure and furniture picks
        // whenever a teammate's fan crossed the beam, and Unity's built-in Ignore Raycast layer was
        // the exclusion. With no collider left anywhere in the slab subtree there is nothing for the
        // world ray to hit, so the whole subtree can stay on the mod layer VRLayers just gave it —
        // which is also what keeps the Body and FrontArt children inside the owned head camera's
        // cull mask, exactly as before.
    }

    private void Hide()
    {
        // ZERO IS A READING (see PeerCardFaceCensus): a hidden fan must overwrite its census row
        // rather than leave the last frame's front count standing for the rest of the session.
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.HandFan, _owner.PlayerId, 0, 0,
            "no fan up — the owner is not holding their hand out");

        // Drop any cloned fronts so a hidden hand keeps no game-widget clones alive.
        for (int i = 0; i < _faces.Count; i++)
        {
            _faces[i].HideFront();
            // …AND THE BODY UNDER IT. A hidden fan that came back with an edge ring around a bare
            // card back would be user item 10 inverted.
            SetFrontFace(i, showsBack: true);
        }
        // …and with the fronts gone, the map-print latch is a lie about what is on the slabs.
        for (int i = 0; i < _mapPrinted.Count; i++)
            _mapPrinted[i] = -1;
        _mapFronts = false;
        if (_frontsShown)
        {
            _frontsShown = false;
            VRLog.Info("Net", $"Remote hand fan [player {_owner.PlayerId}] hidden — cloned fronts released.");
        }
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
        _openElapsed = -1f; // next appearance fans out again from the centre stack
        // …AND THE POP RAMPS, WHICH THIS METHOD FORGOT (2026-09-07 re-audit). Rebuild and BeginSwap
        // both call this and Hide did not, so a fan lowered while one card was lifted and raised
        // again at the SAME card count skipped the rebuild entirely (`count == _lastBuilt`) and came
        // back with that slab still popped — 18 % bigger and 35 mm proud of the arc — relaxing over
        // the ~125 ms PopRate takes. The owner's own fan has no such state to carry: CardFan.Open
        // re-lays every card out from the centre stack. Same rule as every other latch here, stated
        // in Rebuild's own comment: a fan that comes back must never come back mid-animation.
        ClearPops();
        // …and the card-FX wash latch with them, for the reason ClearUsedCardFx states: the faces
        // die with the fan, and a latch that outlived them would make the next card at that index
        // skip the write that puts its wash on.
        ClearUsedCardFx();
        // …and a collapse that was interrupted by a teardown (the holder went untracked mid-fold)
        // must not survive into the next appearance. The tick clears this too, on the frame the
        // fan comes back; clearing it here as well means the state cannot outlive the slabs it
        // describes, which is the same rule every other latch in this class follows.
        _closeElapsed = -1f;
        // A hidden fan stops ticking, so an exchange in the air would freeze half-way off the hand.
        // Landing it here also means the next appearance plays the fan-out REVEAL (the right
        // animation for a hand being raised) rather than resuming a wipe nobody can see the start of.
        EndSwap();
        _shownActorId = 0;
        _shownActor = null;
        _swapIdentity = default;
        // Presentation state resets exactly like CardFan.Open does: the apex starts centred (a fan
        // that popped open already leaning would read as a glitch) and the next appearance logs its
        // geometry once so a hardware log has a line per fan, not one per session.
        _gazeX = 0f;
        // …and the gaze LEAN with it: CardFan.Open zeroes _gazeBiasYaw beside _gazeX for the same
        // reason, so a raised hand starts squarely billboarded and eases into any bias. It leaves
        // _gazeSide latched — harmless, and mirrored here rather than "tidied": with the yaw at 0 a
        // still-committed side simply eases out from centre on the next frame, which is the intent.
        _gazeBiasYaw = 0f;
        _loggedCount = -1;
        // A hidden fan does not tick, so the fist edge it was measuring stops being measured. Drop
        // the memory of it rather than counting the next un-held frame as a release the peer never
        // made — a denominator that inflates while the fan is DOWN would make the verdict line read
        // as refusals on a surface that was not even up.
        _fistNamedLast = false;
        _fistCard = null;
        _fistSeat = -1;
        ClearReturnGlide();
    }

    public void Destroy()
    {
        // Leave the live-fan registry FIRST, so nothing can be handed a fan whose slabs are about to
        // be destroyed. Remove is O(n) over at most a handful of peers and runs once per departure.
        s_live.Remove(this);

        EndSwap(); // any outgoing wave dies with the fan — no orphaned slabs, no leaked clones
        _shownActorId = 0;
        _shownActor = null;
        _swapIdentity = default;
        for (int i = _faces.Count - 1; i >= 0; i--)
            _faces[i].Destroy();
        _faces.Clear();
        _cards.Clear();
        _bodyWearsBack.Clear();   // index-aligned with _cards; a stale FALSE would skip a body write
        _handBuffer.Clear();
        _frontsShown = false;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        _holder = null;
        _builtCount = -1;
    }

    // ------------------------------------------------------------------ card-back body mesh --
    //
    // Round 17: the hand-built two-quad back slab (BuildBackSlab) is GONE. Every ghost-card body
    // — this fan's, RemoteItemFan's, RemoteBrowserFan's, RemoteCardFx's, RemoteAvatar's held
    // slabs and WorldUI.AvatarMirror's pooled slabs — now goes through CardMesh.AttachBody, so
    // peers see the same punched-out card outline the owner sees (the 1:1 board rule). The shared
    // materials no longer carry the alpha cutout, so a rectangle here would have rendered as the
    // pre-silhouette full rectangle. AttachBody's meshes are SHARED caches — never destroyed by
    // the consumers that wear them.
}
