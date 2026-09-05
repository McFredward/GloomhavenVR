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
///   * HOVER SPLIT / insertion GAP: driven by the owner's laser or fingertip hovering one card.
///     Nothing on the wire says which card, so a peer's fan never splits. Costs a byte + a flag to
///     fix; the extras flag byte is full, so it would have to claim one of the RESERVED bits 5-7 of
///     the pile-browse payload byte A (see PresenceState's layout contract).
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
internal sealed class RemoteHandFan : IBorrowedCardSource
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
    // a change in the character the owner is DISPLAYING, which this receiver already resolves every
    // frame for the front-art gate (RemoteBoardFocus.DisplayedActor — extension record 22's actor
    // id plus this client's own host-replicated actor table). The animation itself is a function of
    // that edge, the card COUNT that was already broadcast, and the owner's synced hand and head —
    // exactly like the fan-out reveal and the depth bow above. A MOTION CARRIES NO IDENTITY: what
    // is mirrored here is where slabs move, never which cards they are. The slabs on their way out
    // keep whatever face state the reveal gate had already granted them and are re-gated every
    // frame by UpdateFaces, so a phase that turns secret mid-wipe turns the leavers to BACKS in the
    // same frame it turns the arrivers — the gate is never outrun by an animation.
    //
    // Deliberately resolved from DisplayedActor rather than from the raw focus id: that is the same
    // predicate the FRONTS use, so the fan can never be exchanging for one reason while its faces
    // follow another (the ModBuild 84 mismatch, stated at UpdateFaces).

    /// <summary>Seconds since the owner's exchange began (-1 = none). Advanced on the caller's
    /// unscaled dt, like the reveal.</summary>
    private float _swapElapsed = -1f;

    /// <summary>How many slabs the outgoing wave started with — it fixes the wipe's rhythm and the
    /// gather point's place on the arc (CardFan._swapOutCount).</summary>
    private int _swapOutCount;

    /// <summary>The character this fan is currently drawn for (0 = not resolved yet) — the FACE
    /// question, and the only one <see cref="RemoteBoardFocus.DisplayedActor"/> answers.</summary>
    private int _shownActorId;

    /// <summary>
    /// The id the MOTION edge is tracked on: the peer's RAW record-22 focus id
    /// (<c>CharacterFocus.FocusIdForPeer</c>), falling back to <see cref="_shownActorId"/> for a
    /// peer that sends no record at all. A change in it while the fan is up IS the exchange edge.
    ///
    /// <para>Deliberately a SECOND id rather than a reuse of <see cref="_shownActorId"/>: the two
    /// answer different questions and come apart in exactly the window the exchange matters most
    /// (the secret card-selection phase). See the edge in <c>Tick</c> for the full argument and the
    /// user report it answers.</para>
    /// </summary>
    private int _swapMotionId;

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
                tr.localScale = Vector3.one * Mathf.LerpUnclamped(_leaveScale[i], seed, e);
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

    // ---- BORROWING A CARD OFF THIS FAN (report 7, 2026-08-15) ---------------------------------
    //
    // "Ich will auch in der Lage sein, dass man die fremden Handkarten jederzeit auch in der Hand
    // nehmen kann (inklusive Hand wechsel etc) damit man sie näher betrachten kann. Nur
    // interagieren oder umsortieren etc soll man nicht können. Es geht hier rein um die Info."
    //
    // NOTHING NEW CROSSES THE WIRE FOR THIS, and nothing could: the whole feature is a second
    // consumer of the mechanism this fan's FRONTS already run on. The count is the only thing this
    // fan is told; the card CONTENT is read locally off the host-replicated CPlayerActor hand and
    // rendered as a throwaway CLONE (RemoteCardArt) strictly under RevealGate.ShowRoundCardFronts.
    // A borrow re-asks that same gate, per slot, on the grab AND on every frame of the hold — see
    // BorrowAllowed, which is literally the predicate UpdateFaces already computed this frame.
    //
    // WHICH PHASES: exactly the ones this fan already shows fronts in. During the secret
    // card-selection window the gate is false, the slabs are BACKS, BorrowAllowed is false, and the
    // slab is not even a sweep candidate — so the affordance itself disappears rather than
    // promising a look it may not give. Cards/CardBorrow.cs states the full list.
    //
    // The GEOMETRY of the affordance lives here because the slabs do: Rebuild puts a BorrowTarget
    // and a strip-sized trigger collider on each slab (the same tiling-collider trick the local fan
    // uses so a sweep picks one card at a time — FanSweep's class doc explains why full-width
    // colliders on overlapping cards make a sweep skip cards).

    /// <summary>Whether the fronts gate was OPEN on the last <see cref="UpdateFaces"/> pass — the
    /// borrow's permission, kept as a field so it is the SAME verdict the faces were drawn under
    /// rather than a second, separately-derived one (the ModBuild 84 mismatch, one surface over).</summary>
    private bool _borrowGateOpen;

    /// <summary>The cloned face of a borrowed copy (null = nothing borrowed off this fan). Its own
    /// overlay, never one of <see cref="_faces"/>: the owner's slab keeps its face for the whole
    /// hold, so the fan a peer is looking at never changes because someone borrowed from it.</summary>
    private RemoteCardArt? _borrowArt;

    /// <summary>The transform <see cref="_borrowArt"/> was built against, so a second borrow on a
    /// fresh copy rebuilds rather than re-using an overlay parented to a destroyed card.</summary>
    private Transform? _borrowHost;

    string IBorrowedCardSource.BorrowOwnerLabel => $"player {_owner.PlayerId}";

    Color IBorrowedCardSource.BorrowTint => _owner.Tint;

    string IBorrowedCardSource.BorrowGateLabel =>
        _mapFronts
            ? "RevealGate.ShowMapPhaseHandFronts — the map phase, where no scenario is running and "
              + "therefore no card choice is in flight; the hand is public and INSPECT-ONLY"
            : "RevealGate.ShowRoundCardFronts(the owner's displayed character) — the game's own rule, "
              + "false in the secret SelectAbilityCardsOrLongRest window for a character not under my control";

    /// <summary>The per-slot permission: the fronts gate this frame AND a resolved card behind that
    /// slot — a hand WIDGET in a scenario, a loadout MODEL in the map phase. Both halves are exactly
    /// what <see cref="UpdateFaces"/> requires before it draws a front, and the buffer asked is the
    /// one it drew from (<see cref="_mapFronts"/>), so a card can never be borrowed that is not
    /// already legally visible on that very slab.</summary>
    bool IBorrowedCardSource.BorrowAllowed(int slot)
    {
        if (!_borrowGateOpen || slot < 0)
            return false;
        if (_mapFronts)
            return slot < _mapBuffer.Count && _mapBuffer[slot] != null;
        return slot < _handBuffer.Count
               && _handBuffer[slot] != null && _handBuffer[slot].fullAbilityCard != null;
    }

    /// <summary>Build or refresh the borrowed copy's cloned face. Same class, same clone, same
    /// non-interactive neutralisation and same mip-bake upkeep the fan's own faces get — the copy
    /// is not a second rendering path, it is one more instance of the existing one.
    ///
    /// <para>CardBorrow calls this EVERY FRAME of a hold (it relies on the dedup for the refresh),
    /// so the map path carries the same id latch the fan's slabs do — see
    /// <see cref="PrintMapFace"/> for why a pooled borrow cannot use RemoteCardArt's own dedup.</para></summary>
    bool IBorrowedCardSource.ShowBorrowedFace(int slot, Transform host, float cardWidth, float cardHeight)
    {
        if (!((IBorrowedCardSource)this).BorrowAllowed(slot))
            return false;

        bool map = _mapFronts;
        FullAbilityCard? full = map ? null : _handBuffer[slot].fullAbilityCard;
        CAbilityCard? model = map ? _mapBuffer[slot] : null;
        if (!map && full == null)
            return false;

        if (_borrowArt != null && !ReferenceEquals(_borrowHost, host))
            ((IBorrowedCardSource)this).ReleaseBorrowedFace();
        if (_borrowArt == null)
        {
            _borrowArt = new RemoteCardArt(host, cardWidth, cardHeight);
            _borrowHost = host;
        }

        if (!map)
        {
            _borrowMapId = -1;
            return _borrowArt.ShowFront(full!);
        }

        if (_borrowMapId == model!.ID)
        {
            _borrowArt.MaintainMipBake();
            return true;
        }
        if (RemoteAbilityCardSource.ShowFullFace(_borrowArt, null, model)
            == RemoteAbilityCardSource.FacePath.None)
            return false;
        _borrowMapId = model.ID;
        return true;
    }

    void IBorrowedCardSource.ReleaseBorrowedFace()
    {
        _borrowArt?.Destroy();
        _borrowArt = null;
        _borrowHost = null;
        _borrowMapId = -1;
    }

    /// <summary>Card id currently printed on the borrowed copy through the MAP path (-1 = none).
    /// See <see cref="PrintMapFace"/>: a pooled borrow has no stable source instance id, so the
    /// per-frame refresh needs a latch of its own or it re-clones every frame of the hold.</summary>
    private int _borrowMapId = -1;

    /// <summary>Reused scratch buffer for the remote actor's HAND-pile card widgets (no per-frame alloc).</summary>
    private readonly List<AbilityCardUI> _handBuffer = new(MaxCards);

    /// <summary>Set on the frames the length belt above refuses the fronts, to (this client's model
    /// count, the owner's wire count). Null whenever the belt did not fire. It exists ONLY so the
    /// BACKS line can name the blocker rather than list the candidates — see the log block.</summary>
    private (int Model, int Wire)? _countBelt;

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

    /// <summary>The peer's map-phase loadout, index-aligned with the slabs (empty = unresolved, i.e.
    /// backs). Models rather than widgets: the map phase has no <c>AbilityCardUI</c> anywhere.</summary>
    private readonly List<CAbilityCard> _mapBuffer = new(MaxCards);

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

    /// <summary>Card count the map loadout was last resolved for (-1 = never), and the next unscaled
    /// time the resolve may run again. The resolve walks the party and is NOT a per-frame cost: it
    /// re-runs on a count change or on this slow cadence, which also picks up a loadout edit made on
    /// the peer's side while their fan is up.</summary>
    private int _mapResolvedForCount = -1;

    private float _nextMapResolveAt;

    /// <summary>The map room's own sentence about WHICH tier identified the hand (or why none did),
    /// written verbatim into the faces diagnostic.</summary>
    private string _mapVerdict = "not resolved yet";

    /// <summary>The verdict already reported, so the diagnostic fires on a CHANGE of answer rather
    /// than per resolve.</summary>
    private string _loggedMapVerdict = string.Empty;

    /// <summary>True while the faces currently up came from <see cref="_mapBuffer"/> (the map phase)
    /// rather than from <see cref="_handBuffer"/> (a scenario). Drives the borrow, which must read a
    /// card out of the same buffer the slab's face was drawn from.</summary>
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
        // THE BORROW GESTURE (report 7). Driven from here because this lane owns no module-level
        // ticker and the borrowable slabs are this class's own; CardBorrow.Tick is frame-guarded,
        // so every peer's fan may call it and only the first one in a frame does the work. It is
        // FIRST, before every early return below, so a hand already on a peer's card keeps its
        // hover and its trigger claim even on a frame this particular fan bails out of.
        CardBorrow.Tick();

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
        // THE OWNER'S FAN IS COMING DOWN AND THERE ARE STILL SLABS TO FOLD (ModBuild 306). Their
        // CardFan.Close keeps the root visible and blends every card back into the centre stack
        // over FanCloseDuration before hiding; this is that, on the mirror.
        //
        // Deliberately NOT run while a swap or a leaving wave is in flight: those already animate
        // the very cards this would fold, and the owner's own Close does not run during an exchange
        // either. Nor with the duration at 0 — that is the owner's "vanish instantly", and honouring
        // it is the same 1:1 rule as honouring the animation.
        if (count == 0 && _cards.Count > 0 && _leaving.Count == 0 && _swapElapsed < 0f
            && _closeSeconds > 0f && _root != null && _root.activeSelf)
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

        if (count == 0 && _leaving.Count == 0 && _cards.Count == 0)
        {
            Hide();
            return;
        }

        EnsureRoot(holder);
        if (_root == null)
            return;

        // WHICH CHARACTER'S HAND IS THIS? Resolved ONCE per tick and handed to both consumers — the
        // exchange edge below and the front-art gate at the bottom — because those two disagreeing
        // is precisely the ModBuild 84 defect (n slabs from one character wearing another's faces),
        // and an exchange is the moment they would drift. Guarded: any failure reads as "unknown",
        // which suppresses the exchange and shows backs, i.e. the pre-feature behaviour.
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
            // NEVER SILENT. A permanently-throwing resolve would disable the exchange AND pin this
            // fan to backs forever, with nothing anywhere to say why — the same class of defect the
            // EnsureRoot self-heal was written about. Once per instance, like every other latched
            // diagnostic here.
            if (!_loggedShownActorError)
            {
                _loggedShownActorError = true;
                VRLog.Warn("Net", $"Remote hand fan [player {_owner.PlayerId}]: could not resolve which " +
                                  $"character the owner is displaying ({ex.Message}). The fan falls back to " +
                                  "BACKS and plays no character-swap exchange — both are the pre-feature " +
                                  "behaviour, so this degrades rather than breaks.");
            }
        }

        // THE EXCHANGE EDGE, mirroring CardsDriver.Rebuild's: a DIFFERENT character is being shown,
        // both ids are known, and there is a fan on screen to exchange. A fan that is not up yet
        // plays its fan-out reveal instead, which is the right animation for a hand being raised.
        //
        // ─── WHY THE EDGE IS THE RAW RECORD-22 ID AND NOT THE DISPLAYED ACTOR ────────────────────
        // User, verbatim (2026-08-09): "Die Animation des Fächers, wenn der Character von dem
        // Mitspieler geändert wird, ist nicht sichtbar."
        //
        // Everything downstream of this edge was already built and correct — BeginSwap invalidates
        // _builtCount so two hands of the SAME SIZE still exchange, TickSwap replays the whole wipe
        // on THIS client's own clock from the FanSwap* dials that already ride the tuning record,
        // and the leaving slabs stay gated on their own character. The animation never ran because
        // the TRIGGER could not see the switch:
        //
        //   * the OWNER's edge is Board.CharacterFocus.PresentedActorId
        //     (Cards/CardsDriver.4.Rebuild.cs:260) — the character whose hand their board presents,
        //     which is exactly what record 22 carries (CharacterFocus.LookingAt ⇒ Sample);
        //   * this receiver's edge was RemoteBoardFocus.DisplayedActor, which is NOT that id. Its
        //     RULE 1 deliberately IGNORES the focus record for the whole of
        //     RevealGate.IsSecretSelectionPhase and answers NetPlayerActors.ActorFor instead — and
        //     ActorFor returns the FIRST controllable of that player (NetPlayerActors.cs:137-144),
        //     the same object no matter which of their characters they are editing.
        //
        // The card-selection window is precisely when a two-character player swaps hands, so on the
        // owner's screen the exchange played and on every mirror the id never moved: no edge, no
        // animation. Hardware log confirms the shape — .planning/debug/remote/LogOutput.log:965 and
        // :1022 report the board character changing "their OWNED character: the game is in the
        // secret card-selection phase", and no "Remote hand fan EXCHANGE" line exists in either log.
        //
        // So the MOTION edge now reads the record itself (CharacterFocus.FocusIdForPeer — the raw
        // id as received, no secret-phase filter) while the FACES keep reading DisplayedActor,
        // untouched. That split is the whole point: RULE 1 exists to stop a mirror ASKING ABOUT a
        // character's cards during the secret window, and this asks about none. The edge is an
        // integer INEQUALITY on an id this client already holds and already draws with (the
        // mirrored initiative ring moves on the very same value), the arriving slabs are BACKS
        // because RevealGate.ShowRoundCardFronts is false in that window, and the leaving wave is
        // re-gated every frame on the OUTGOING character. Nothing that was secret becomes visible;
        // a movement that was invisible becomes visible.
        //
        // Falls back to the displayed actor's id when a peer sends no record 22 at all (an older
        // build, a scenario-less client), which is byte-for-byte the previous behaviour.
        int focusId = Board.CharacterFocus.FocusIdForPeer(_owner.PlayerId);
        int motionId = focusId != 0 ? focusId : shownId;
        if (motionId != 0 && _swapMotionId != 0 && motionId != _swapMotionId
            && _root.activeSelf && _cards.Count > 0)
        {
            // The wave keeps the OUTGOING character's faces, so it is handed that character — the
            // one the fan was showing until this frame — for its own reveal-gate check.
            BeginSwap(_shownActor);
            VRLog.Info("Net", $"Remote hand fan EXCHANGE [player {_owner.PlayerId}]: {_swapOutCount} slab(s) " +
                              $"gather off the arc while {count} deal in — the owner switched which character " +
                              "they are looking at (extension record 22's actor id, already on the wire; the " +
                              "RAW id, so the exchange still plays inside the secret card-selection window " +
                              "where RemoteBoardFocus deliberately pins the DISPLAYED character to the " +
                              "owner's owned one). The MOTION is mirrored; no card identity is transmitted " +
                              "for it, the arriving slabs are BACKS whenever the viewer's own RevealGate " +
                              "says so, and the leaving slabs are re-gated every frame on their OWN " +
                              "character's verdict.");
        }
        _swapMotionId = motionId;
        _shownActorId = shownId;
        _shownActor = shownActor;

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
            UpdateFaces(0, null);
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
            switch (RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor))
            {
                case RevealGate.CardFaceSource.Scenario:
                    ResolveHandFronts(actor!);  // fills _handBuffer with the actor's HAND-pile widgets
                    showFronts = _handBuffer.Count > 0;
                    break;
                case RevealGate.CardFaceSource.MapLoadout:
                    ResolveMapFronts(count);
                    mapFronts = _mapBuffer.Count > 0;
                    showFronts = mapFronts;
                    break;
                default:
                    ClearMapFronts();
                    break;
            }
        }
        catch (System.Exception ex)
        {
            // ANY failure → no fronts, backs only (fail-safe = no cheat).
            showFronts = false;
            mapFronts = false;
            _handBuffer.Clear();
            ClearMapFronts();
            VRLog.Warn("Net", $"RemoteHandFan front gate errored ({ex.Message}) — showing backs.");
        }

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

        int heldSeatCount = 0;
        if (showFronts && !mapFronts && _handBuffer.Count != count)
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
        if (showFronts && !mapFronts && _handBuffer.Count == count + 1 && HandoffCard != null)
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

        _countBelt = showFronts && !mapFronts && _handBuffer.Count != count
            ? (Model: _handBuffer.Count, Wire: count)
            : ((int Model, int Wire)?)null;
        if (_countBelt != null)
            showFronts = false;

        // Which buffer this frame's faces come from, latched for the borrow — a borrow must read the
        // card out of the SAME buffer the slab's face was drawn from, which is the ModBuild 84 rule
        // one surface over.
        _mapFronts = mapFronts;

        // THE BORROW PERMISSION IS THIS VERY VERDICT (report 7), latched here rather than
        // re-derived on demand: a borrow that asked its own copy of the rule could answer
        // differently from the slab it is looking at, which is the ModBuild 84 defect one surface
        // over. Closing the gate also EMPTIES the widget buffer, so a stale entry from the last
        // open frame can never be borrowed after the phase turned secret.
        _borrowGateOpen = showFronts;
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
                    if (i < _mapBuffer.Count && PrintMapFace(i, face, _mapBuffer[i]))
                    {
                        frontCount++;
                        continue;
                    }
                }
                else if (i < _handBuffer.Count)
                {
                    AbilityCardUI widget = _handBuffer[i];
                    FullAbilityCard? full = widget != null ? widget.fullAbilityCard : null;
                    if (full != null && face.ShowFront(full))
                    {
                        frontCount++;
                        continue;
                    }
                }
            }
            face.HideFront();
            if (i < _mapPrinted.Count)
                _mapPrinted[i] = -1;
        }

        // …and, on the SAME resolved widgets, the game's own card plume (wire id 236). It rides
        // this pass rather than a pass of its own because the source widget the predicate needs is
        // exactly the one just used for the face: a second resolve would be a second answer, which
        // is the ModBuild 84 mismatch one surface over.
        TickMirroredPlumes(count, showFronts, mapFronts);

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
                    _leavingFaces[i]?.HideFront();
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
                ? $"LENGTH BELT: {_countBelt.Value.Model} model card(s) vs {_countBelt.Value.Wire} "
                  + "slab(s) on the wire"
                : !showFronts
                    ? "RevealGate.CardFaces(Selectable) named no source, or no widget resolved"
                    : mapFronts
                        ? "RevealGate map-phase fronts (peer's replicated map loadout)"
                        : heldSeatCount > 0
                            ? $"RevealGate scenario fronts, {heldSeatCount} held seat(s) dropped"
                            : "RevealGate scenario fronts");

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
                    $"content=HAND, {frontCount} card(s), gate: " +
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
                        + $"content=HAND, and the reveal gate was OPEN. The fronts were refused by "
                        + $"the LENGTH BELT: this client resolves {_countBelt.Value.Model} hand "
                        + $"card(s) for that character while the owner's own fan reports "
                        + $"{_countBelt.Value.Wire} slab(s) on the wire. Slab i is only a name for "
                        + "card i while those two agree, so a front here would be the wrong card's "
                        + "face — a back is the safe direction. Both sides compute membership from "
                        + "the one shared expression (CardsGameApi.HandFanMember), so a BRIEF "
                        + "disagreement around a burn or a played card is this client's copy of the "
                        + "peer's hand lagging a choreographer turn and will clear itself. A "
                        + "disagreement that STANDS means the two sides have drifted apart on what "
                        + "counts as a hand card, which is the defect this belt exists to make "
                        + "visible rather than to hide.");
                }
                else
                {
                    VRLog.Info("Net", $"Remote hand fan faces [player {_owner.PlayerId}]: BACKS — "
                        + "content=HAND, gate: RevealGate.ShowRoundCardFronts(actor)=false (the game's "
                        + "own secret SelectAbilityCardsOrLongRest phase) or no hand widget resolved."
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
    /// wire field that would make them exact. Cached because that walk is O(party) and this is
    /// called every frame: it re-runs when the peer's card COUNT moves (a card ticked on or off, a
    /// character switch) and otherwise on <see cref="MapResolveInterval"/>, which is what picks up
    /// an edit that did not change the size.
    /// </summary>
    private void ResolveMapFronts(int count)
    {
        if (count == _mapResolvedForCount && Time.unscaledTime < _nextMapResolveAt)
            return;
        _mapResolvedForCount = count;
        _nextMapResolveAt = Time.unscaledTime + MapResolveInterval;

        int before = _mapBuffer.Count;
        int firstBefore = before > 0 && _mapBuffer[0] != null ? _mapBuffer[0].ID : 0;
        // The peer's own statement of WHICH character their fan is showing, when their build sends
        // one (extension record 20, ModBuild 226). 0 from an older peer, and then the resolver falls
        // back to deducing the owner from the hand size exactly as it did before the field existed.
        RemoteMapRoom.TryGetPeerFanCharacterKey(_owner.PlayerId, out uint characterKey);
        WorldUI.MapRoom.MapRoomHand.TryResolvePeerLoadout(
            _owner.PlayerId, count, characterKey, _mapBuffer, out _mapVerdict);
        int firstAfter = _mapBuffer.Count > 0 && _mapBuffer[0] != null ? _mapBuffer[0].ID : 0;

        // A DIFFERENT HAND MUST NOT INHERIT THE OLD HAND'S PRINTS. The per-slab latch below is what
        // keeps the pooled borrow off the per-frame path, so it has to be invalidated whenever the
        // resolved set can have moved under it — the cheap, always-safe test is "the size or the
        // leading card changed", and a false positive costs one re-print pass.
        if (_mapBuffer.Count != before || firstAfter != firstBefore)
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
        if (_mapResolvedForCount < 0 && _mapBuffer.Count == 0)
            return;
        _mapBuffer.Clear();
        _mapResolvedForCount = -1;
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

    /// <summary>How long an armed hand-off may stand before it is abandoned. It is a BACKSTOP, not
    /// the normal exit: <c>RemoteControlBoard.SeatSlots</c> drops it the instant this client's
    /// model catches up (the walk becomes compactable), and the recess emptying drops it too.
    /// Without it a hand-off whose model never arrives would keep a face lying in a recess for the
    /// rest of the session — a stale front, i.e. the very failure the belt exists to prevent.
    /// </summary>
    private const float HandoffHoldSeconds = 6f;

    /// <summary>The card in this peer's fist right now, resolved from THIS client's own hand list
    /// through record 36's seat — null whenever the fist is empty, names two cards, or the sender's
    /// list length disagrees with this client's.</summary>
    private CAbilityCard? _fistCard;

    /// <summary>The pose slot <see cref="_fistCard"/> is held in (1 or 2), so the return glide can
    /// read the slab it was released at.</summary>
    private int _fistPoseSlot;

    /// <summary>The hand seat <see cref="_fistCard"/> came out of — the arc index the card returns
    /// to when it is released into the void, because <c>CardFan.Add</c> re-inserts at
    /// <c>HomeIndexFor</c>, i.e. its OWN place and not the right-hand end.</summary>
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

    /// <summary>Unscaled time the fist last held a nameable card (0 = never / consumed).</summary>
    private float _fistHeldAt;

    /// <summary>THE ARMED HAND-OFF: the card this client saw handed from a fist into a round
    /// recess. Read by <c>RemoteControlBoard.SeatSlots</c> for the recess face and by the belt
    /// above for the fan's own length.</summary>
    internal CAbilityCard? HandoffCard { get; private set; }

    /// <summary>Which recess <see cref="HandoffCard"/> went into (-1 = none armed).</summary>
    private int _handoffRecess = -1;

    /// <summary>Unscaled time the armed hand-off's backstop runs out.</summary>
    private float _handoffUntil;

    /// <summary>Session census: how many times a recess GAINED a card, and how many of those this
    /// client could name. The pair is the falsifier — see <see cref="LogHandoff"/>.</summary>
    private int _handoffGains;
    private int _handoffArmed;
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

    /// <summary>The exponential rate the OWNER's own released card flies home at
    /// (<c>[Cards] CardLerpSpeed</c>, off <see cref="RemoteAvatar.BoardTuning"/>) — theirs and
    /// never this client's, so the mirrored glide settles on the owner's clock. Falls back to the
    /// shipped default for a peer who has not moved the dial, exactly like every other term in
    /// <see cref="SyncTuning"/>.</summary>
    private float _lerpSpeed = Defaults.CardLerpSpeed;

    /// <summary>
    /// Watch the fist for one frame: resolve what is in it, and on the frame it empties decide
    /// whether the card went into a RECESS (arm the hand-off, item 5) or back to the FAN (start the
    /// mirrored return glide, item 2). See the block above for why each term is here.
    /// </summary>
    /// <param name="armed">Whether the front path resolved a hand list this frame. With the reveal
    /// gate shut there is no <see cref="_handBuffer"/> to name a seat in, so nothing arms — and
    /// nothing should: a recess filling during the secret selection phase draws a back on every
    /// client by RevealGate's ruling, which this must not and does not widen.</param>
    /// <param name="actor">The character this fan is DISPLAYING, resolved once per tick by the
    /// caller and handed in — the same answer <see cref="UpdateFaces"/> resolved the arc from, so
    /// the seat and the character can never be about two different hands.</param>
    /// <param name="count">The owner's own arc SLAB COUNT off the wire — the term that separates a
    /// card released back into the fan from one that went to a pile.</param>
    private void TrackFist(bool armed, CPlayerActor? actor, int count)
    {
        int mask = _owner.SlotOccupancyKnown ? _owner.BoardSlotMask : 0;
        if (!_seenMaskValid)
        {
            _seenMaskValid = true;
            _seenMask = mask;
        }
        int gainedSinceFrame = mask & ~_seenMask;
        _seenMask = mask;
        if (gainedSinceFrame != 0)
            _handoffGains += SlotBits(gainedSinceFrame);

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
        bool named = _owner.SingleHeldHandSeat(out int seat, out int listLength, out int poseSlot);
        CAbilityCard? fist = null;
        if (named && armed
            // THE LENGTH BELT, ONE SURFACE OVER. A positional seat is only a NAME while both copies
            // of the list are the same length — the identical term RemoteHeldCardFace checks before
            // it draws this very card's own front. Without it a lagging model would name the card
            // beside the right one and this would paint that wrong face confidently into a recess,
            // which is exactly the defect the compaction belt was built to stop.
            && listLength == _handBuffer.Count && seat >= 0 && seat < _handBuffer.Count)
        {
            AbilityCardUI? w = _handBuffer[seat];
            fist = w != null ? w.AbilityCard : null;
        }

        if (named)
        {
            if (fist == null)
            {
                // A card IS in their fist and this client cannot name it. Not a release: forget the
                // identity so nothing stale can be handed over, keep the mask moving, and let the
                // next frame try again.
                _fistCard = null;
                _fistSeat = -1;
                _fistMask = mask;
                ExpireHandoff(mask, now);
                return;
            }
            // Still holding, and named: refresh the memory AND the two baselines the release edge
            // is measured against. A new card in the fist also retires any standing hand-off — one
            // fist, one event.
            if (!ReferenceEquals(fist, _fistCard))
                ClearHandoff();
            _fistCard = fist;
            _fistSeat = seat;
            _fistPoseSlot = poseSlot;
            _fistMask = mask;
            _fistCount = count;
            _fistHeldAt = now;
            return;
        }

        if (_fistCard == null)
        {
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
            ExpireHandoff(mask, now);
            return;
        }

        int gained = mask & ~_fistMask;
        int lost = _fistMask & ~mask;
        if (gained != 0 && lost == 0 && SlotBits(gained) == 1)
        {
            // INTO A RECESS (item 5).
            _handoffRecess = (gained & 1) != 0 ? 0 : 1;
            HandoffCard = _fistCard;
            _handoffUntil = now + HandoffHoldSeconds;
            _handoffArmed++;
            LogHandoff();
            _fistCard = null;
            _fistSeat = -1;
            return;
        }

        if (mask == _fistMask && _fistSeat >= 0 && count == _fistCount + 1 && _closeElapsed < 0f)
        {
            // (…and not while the fan is FOLDING AWAY. The collapse path hands this method
            // `_cards.Count` instead of the wire count, so the equality above can be satisfied by
            // the fold rather than by a card coming home, and a fan that is collapsing has no arc
            // seat to fly to anyway.)
            // BACK TO THE FAN (item 2) — no recess changed AND the owner's own arc grew back by
            // exactly the one slab it lost when they plucked the card out, so the card was released
            // into the void and CardFan.Add is carrying it home to its authored seat right now.
            // The count term is what tells this apart from a card that went to a PILE: a burn or a
            // discard leaves the arc one slab SHORT and must not be mirrored as a flight to a seat
            // that is not there.
            BeginReturnGlide(_fistSeat, _fistPoseSlot);
            _fistCard = null;
            _fistSeat = -1;
            return;
        }

        ExpireHandoff(mask, now);
    }

    /// <summary>Population of a two-bit recess mask — the recesses are two, so this is two tests.
    /// </summary>
    private static int SlotBits(int mask) => ((mask & 1) != 0 ? 1 : 0) + ((mask & 2) != 0 ? 1 : 0);

    /// <summary>Drop an armed hand-off whose recess has emptied or whose backstop has run out.
    /// </summary>
    private void ExpireHandoff(int mask, float now)
    {
        if (HandoffCard == null)
            return;
        bool occupied = _handoffRecess >= 0 && (mask & (1 << _handoffRecess)) != 0;
        if (!occupied || now > _handoffUntil)
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
        _handoffUntil = 0f;
    }

    /// <summary>The card this client saw handed into round recess <paramref name="recess"/>, or
    /// null. Read by <c>RemoteControlBoard.SeatSlots</c> only where its own walk and its own face
    /// latch have both come up empty, so a resolved model always wins.</summary>
    internal CAbilityCard? HandoffFor(int recess)
        => recess >= 0 && recess == _handoffRecess && Time.unscaledTime <= _handoffUntil
            ? HandoffCard
            : null;

    /// <summary>
    /// Seed the mirrored return-to-fan glide at the pose the card was RELEASED at (report item 2).
    /// The mirror of the owner's own seam: <c>VRCard.OnRelease</c> keeps the card's world pose
    /// across the re-parent and <c>CardFan.Add</c> asks <c>Relayout(instant: false)</c> to carry it
    /// to its arc slot on the standing home-lerp — so the slab starts exactly where their card was
    /// hanging and eases in on THEIR rate, never a tween of our own invention.
    /// </summary>
    private void BeginReturnGlide(int seat, int poseSlot)
    {
        Transform? slab = _owner.HeldSlab(poseSlot);
        if (slab == null)
            return;
        _returnIndex = seat;
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
    }

    /// <summary>Abandon a return glide (fan closed, rebuilt, or the slab count moved under it).
    /// </summary>
    private void ClearReturnGlide()
    {
        _returnIndex = -1;
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
        if (_loggedHandoff == _handoffArmed)
            return;
        _loggedHandoff = _handoffArmed;
        // HW-VERIFY: report item 5. Grep token: RECESS HAND-OFF.
        VRLog.Note("Net", $"RECESS HAND-OFF [player {_owner.PlayerId}]: round recess "
            + $"{_handoffRecess + 1} just took the card this peer was holding at hand seat "
            + $"{_fistSeat} of {_handBuffer.Count} — armed {_handoffArmed} of {_handoffGains} "
            + "recess arrival(s) this session. The recess draws that card's OWN front for the "
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
    /// <para>FALSIFIERS. (1) This line absent all session: either no peer ever released a card back
    /// into their fan, or the fist was never NAMED — record 36's 'Remote held card FRONT' line for
    /// that player tells the two apart, and its absence means this could not have armed, so the
    /// silence proves nothing about the flight. (2) The line present and the user still sees the
    /// card pop: the glide ran and was overwritten — read the fan's own geometry line for a REBUILD
    /// inside the same 0.35 s, which drops the seed. (3) The count climbing far past the number of
    /// releases the player remembers: the fist is being read as empty mid-hold and the returning
    /// slab is being seeded from a stale pose.</para>
    /// </summary>
    private void LogReturnGlide()
    {
        if (_loggedReturns == _returnsPlayed)
            return;
        _loggedReturns = _returnsPlayed;
        // HW-VERIFY: report item 2. Grep token: FAN RETURN FLIGHT.
        VRLog.Note("Net", $"FAN RETURN FLIGHT [player {_owner.PlayerId}]: this peer released a "
            + $"card into the void and slab {_returnIndex} now GLIDES home to its arc seat from "
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
    /// Fill <see cref="_handBuffer"/> with the remote actor's live HAND-pile card widgets, in hand
    /// order — the exact set the local fan draws (<c>widget.CardType == CardPileType.Hand</c>). Read
    /// straight off the game's own <c>CardsHandManager.GetHand(actor).cardsUI</c> (publicized). This
    /// deliberately EXCLUDES Round/Discard/Lost/Active piles, so the secret round-selection cards are
    /// never even candidates for a front here. Cleared + refilled each call; no allocation.
    /// </summary>
    private void ResolveHandFronts(CPlayerActor actor)
    {
        _handBuffer.Clear();
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return;
        CardsHandUI hand = manager.GetHand(actor);
        if (hand == null)
            return;
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
            // The mirror of VRCard's own home-lerp, not a tween of ours: the slab is STAMPED once
            // at the world pose their card was released at (the held slab's last pose, carried as
            // a world pose because the slab list is rebuilt between the release and this frame),
            // and from there it eases toward its arc seat on the OWNER's [Cards] CardLerpSpeed —
            // the identical `1 - exp(-speed * dt)` VRCard.UpdateBody runs, for the identical
            // ReleaseGlideSeconds window. Everything else in this loop is unchanged, so a slab
            // that is not the returning one is laid out exactly as before.
            if (i == _returnIndex && _returnGlide > 0f)
            {
                if (_returnSeedPending)
                {
                    _returnSeedPending = false;
                    t.position = _returnSeedPos;
                    t.rotation = _returnSeedRot;
                }
                _returnGlide -= Mathf.Max(dt, 0f);
                float rk = 1f - Mathf.Exp(-_lerpSpeed * Mathf.Max(dt, 0f));
                pos = Vector3.Lerp(t.localPosition, pos, rk);
                rot = Quaternion.Slerp(t.localRotation, rot, rk);
                if (_returnGlide <= 0f)
                    ClearReturnGlide(); // landed: the plain assignment below owns the seat again
            }
            t.localPosition = pos;
            t.localRotation = rot;
            Vector3 want = Vector3.one * (swapScale * (1f + PopScale * popT));
            if (t.localScale != want)
                t.localScale = want;
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
        float halfW = _cardWidth * 0.5f * lossy;
        float halfH = _cardHeight * 0.5f * lossy;
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
            bool running;
            try
            {
                AbilityCardUI widget = _handBuffer[i];
                FullAbilityCard? full = widget != null ? widget.fullAbilityCard : null;
                CardEffects? fx = full != null ? full.cardEffects : null;
                running = fx != null
                          && (fx.HasEffect(CardEffects.FXTask.BurnCard)
                              || fx.HasEffect(CardEffects.FXTask.LostMode)
                              || fx.HasEffect(CardEffects.FXTask.DiscardMode));
            }
            catch (System.Exception)
            {
                running = false; // any deref failure -> no plume, like every other gate in this file
            }
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
            _mapPrinted.Clear();   // index-aligned with _faces; stale entries would claim prints that no longer exist
            // The outgoing wave hung off the same dead root — drop its bookkeeping with the rest, or
            // TickSwap would drive destroyed transforms every frame (the very defect this heal
            // exists for, one list over).
            EndSwap();
            _shownActorId = 0;
            _shownActor = null;
            _swapMotionId = 0;
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
            // WhileOverBoard, NOT Always, and the distinction is the point. A hand fan is AVATAR
            // content wherever its owner takes it (RemoteBoardGate says so in as many words and
            // that classification is unchanged); it becomes one of "die Faecher vor dem Brett"
            // only while it is actually parked over the board, which is what the see-through's own
            // predicate measures, in the BOARD's local space, and freezes for the duration of a
            // fade so membership can never strobe. See PeerBoardFade.Follow.
            //
            // REGISTERED HERE, at the one place a NEW root transform comes into existence, so the
            // self-heal above — which destroys and recreates this object wholesale — re-registers
            // as a matter of course. Registration is idempotent, and the see-through sweeps
            // Unity-null roots on its own census, so the dead one needs no teardown call.
            PeerBoardFade.Follow(_owner.PlayerId, _root.transform,
                                 PeerBoardFade.FollowRule.WhileOverBoard);
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

    /// <summary>Destroy and recreate exactly <paramref name="count"/> back-on-both-faces slabs, each
    /// with its own (initially hidden) cloned-front overlay. Only called when the count changes
    /// (cheap). Re-applies the mod layer so the owned head camera renders the new slabs.</summary>
    private void Rebuild(int count)
    {
        // A BORROWED COPY MUST NEVER OUTLIVE THE SLABS IT WAS READ FROM (report 7). The hand it
        // came from is being replaced — a card was played, burnt, drawn, or the owner switched
        // character — so the slot index it names stops meaning what it meant. It glides back and
        // dies here rather than becoming a card of unknown provenance in someone's hand.
        CardBorrow.EndIfFrom(this, "the owner's fan was rebuilt");

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
            if (_cards[i] == null)
                continue;
            // Drop the slab's borrow registration BEFORE the deferred Destroy, so the sweep never
            // considers a slab that is on its way out this frame.
            _cards[i].GetComponent<BorrowTarget>()?.Retire();
        }

        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        _frontsShown = false;
        ClearPops(); // a rebuilt fan must never open with a stale card already lifted

        // Ability KIND so a peer's card backs take the same silhouette clip the owner's do — the MP
        // 1:1 rule applies to the card's SHAPE as much as to its content.
        Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability); // shared: back texture on a Standard material

        // THE BODY IS SIZED TO THE FACE IT WILL WEAR (report 12, 2026-08-15) — see _visibleFace.
        Vector2 vis = _visibleFace;

        // The arc pitch this hand is laid out on — LayoutCards' own `step`, needed here so the
        // borrow collider can be shrunk to the visible strip between neighbouring cards.
        float stepDegrees = count > 1
            ? Mathf.Min(_perCardStepDegrees, _arcSweepDegrees / (count - 1))
            : 0f;

        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Card{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            // The SLAB ROOT stays UNIFORM: RemoteCardArt hangs its world-space face canvas off this
            // transform, and a non-uniform scale here would stretch the printed art. The owner's own
            // CardWidth arrives as that uniform scale (record 28), so their ghost cards read the size
            // they see.
            card.transform.localScale = Vector3.one * (_cardWidth / DefaultCardWidth);

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

            // BORROW AFFORDANCE (report 7): a trigger collider on the slab ROOT plus the reach
            // surface the shared sweep elects over. It is sized to the slab's VISIBLE STRIP, not
            // to the whole card — the peer's slabs overlap by 40-60 % exactly like the local fan's,
            // and FanSweep's class doc records what full-width colliders on overlapping cards do to
            // a sweep (several cards measure 0.0 cm at once, ties never switch the incumbent, cards
            // are skipped). The collider is a trigger and is used only for ClosestPoint, so it
            // never touches game physics and the slab stays a purely cosmetic ghost.
            var borrow = card.AddComponent<BorrowTarget>();
            borrow.Configure(this, i, vis.x, vis.y,
                FanSweep.StripWidth(count, _radius, stepDegrees, vis.x, _cardWidth / DefaultCardWidth));

            EmitMirroredCardDust(card, appear: true);
            _cards.Add(card);
            // NOMINAL size, not the owner's tuned one: the slab root ALREADY carries
            // _cardWidth/DefaultCardWidth, so handing the tuned width here fitted the face a second
            // time and squared the ratio — invisible at the shipped default (ratio 1), a real
            // mismatch for any peer who had moved [Cards] CardWidth.
            _faces.Add(new RemoteCardArt(card.transform, DefaultCardWidth, DefaultCardHeight));
        }

        _builtCount = count;

        // Owned head camera renders the mod layer only; put the whole fan subtree on it (no-op
        // when VR is not running, exactly like the local fan/hands).
        VRLayers.Apply(_root!);

        // …and then take the BORROW COLLIDERS back off it, onto Unity's built-in Ignore Raycast
        // layer (2). This is not tidiness, it is the one way this feature could have broken
        // something unrelated: RayInteractor's world pick is a real Physics.Raycast against
        // Physics.DefaultRaycastLayers with triggers enabled (RayInteractor.cs:590), and the mod
        // layer is an ordinary layer inside that mask. A trigger box floating around every peer's
        // hand would therefore have become a laser hit — stealing hex, figure and furniture picks
        // whenever a teammate's fan crossed the beam. Layer 2 is excluded from
        // DefaultRaycastLayers by Unity itself, and Collider.ClosestPoint (the only thing the borrow
        // sweep uses) does not consult layers at all, so the affordance keeps working with zero
        // exposure to the world ray. Only the slab ROOT moves — it carries no renderer, so nothing
        // leaves the owned head camera's cull mask; the Body and FrontArt children keep the mod
        // layer VRLayers just gave them.
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i] != null)
                _cards[i].layer = IgnoreRaycastLayer;
        }
    }

    /// <summary>Unity's built-in "Ignore Raycast" layer — the one layer
    /// <c>Physics.DefaultRaycastLayers</c> excludes. See the note at the end of <see cref="Rebuild"/>.</summary>
    private const int IgnoreRaycastLayer = 2;

    private void Hide()
    {
        // A hidden fan has no slabs on screen to have borrowed from, so a copy in the air would be
        // orphaned the moment the owner lowered their hand (report 7's "must not survive").
        CardBorrow.EndIfFrom(this, "the owner's fan was hidden");

        // ZERO IS A READING (see PeerCardFaceCensus): a hidden fan must overwrite its census row
        // rather than leave the last frame's front count standing for the rest of the session.
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.HandFan, _owner.PlayerId, 0, 0,
            "no fan up — the owner is not holding their hand out");

        // Drop any cloned fronts so a hidden hand keeps no game-widget clones alive.
        for (int i = 0; i < _faces.Count; i++)
            _faces[i].HideFront();
        // …and with the fronts gone, the map-print latch is a lie about what is on the slabs.
        for (int i = 0; i < _mapPrinted.Count; i++)
            _mapPrinted[i] = -1;
        _mapFronts = false;
        _borrowGateOpen = false;
        if (_frontsShown)
        {
            _frontsShown = false;
            VRLog.Info("Net", $"Remote hand fan [player {_owner.PlayerId}] hidden — cloned fronts released.");
        }
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
        _openElapsed = -1f; // next appearance fans out again from the centre stack
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
        _swapMotionId = 0;
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
    }

    public void Destroy()
    {
        // Leave the live-fan registry FIRST, so nothing can be handed a fan whose slabs are about to
        // be destroyed. Remove is O(n) over at most a handful of peers and runs once per departure.
        s_live.Remove(this);

        // THE PEER LEFT (or the scenario tore down). A borrowed copy of their card dies with them
        // — report 7's second lifetime rule, and the only one a hardware session can hit by
        // accident (a disconnect mid-look).
        CardBorrow.EndIfFrom(this, "the owner's avatar was destroyed (peer left / teardown)");
        ((IBorrowedCardSource)this).ReleaseBorrowedFace();

        EndSwap(); // any outgoing wave dies with the fan — no orphaned slabs, no leaked clones
        _shownActorId = 0;
        _shownActor = null;
        _swapMotionId = 0;
        for (int i = _faces.Count - 1; i >= 0; i--)
            _faces[i].Destroy();
        _faces.Clear();
        _cards.Clear();
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
