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
/// (<see cref="RemoteCardArt"/>). The broadcast COUNT drives the fan size; the fronts are read
/// locally from the remote actor's own hand and gated strictly on the reveal rule.
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
///   * GAZE-BIAS YAW ([Cards] FanGazeBias): opt-in, OFF by default and superseded by the toe-in.
///     The receiver could compute the whole eased/hysteretic yaw from the peer's synced head gaze,
///     but not whether the SENDER has the toggle on — that one bit is the only missing input.
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

    /// <summary>Card slab width default (CardsConfig.CardWidth default) — shared with
    /// <see cref="RemoteAvatar"/>'s held-card slab so all remote card slabs match.</summary>
    internal const float DefaultCardWidth = 0.0635f;

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
    private float _palmOffset = Defaults.FanPalmOffset;

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

    /// <summary>Ease rate (1/s) of the tracked gaze apex (CardsConfig.FanGazeSmoothing).</summary>
    private const float GazeSmoothing = 8f;

    /// <summary>Relief half-width as a fraction of the hand's half-span, and its floor in cards —
    /// CardFan.GazeReliefWidthFactor / GazeReliefMinWidth.</summary>
    private const float GazeReliefWidthFactor = 0.55f;
    private const float GazeReliefMinWidth = 1.2f;

    /// <summary>Grazing-gaze fade window (CardFan.GazeGrazeMinZ / GazeGrazeFullZ): the fan-local +Z
    /// component of the gaze below which the plane crossing is faded out to "centred".</summary>
    private const float GazeGrazeMinZ = 0.05f;
    private const float GazeGrazeFullZ = 0.35f;

    /// <summary>Exponential follow/face sharpness (higher = snappier). Mirrors CardFan's eased
    /// follow (CardsConfig.FanFollowSmoothing default 16). CardFan's 4 mm dead zone is deliberately
    /// NOT reproduced: it exists to hold the fan still through raw hand-tracking jitter, and the
    /// peer's hand pose is already an interpolated, packet-rate signal.</summary>
    private const float Smoothing = 16f;

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

    /// <summary>VRCard's pop: the small upward component that rides with the forward lift.</summary>
    private const float PopUp = 0.012f;

    /// <summary>VRCard's pop: the extra size a lifted card takes (+18 %).</summary>
    private const float PopScale = 0.18f;

    /// <summary>VRCard's pop RAMP rate (units per second, MoveTowards) — the lift grows and relaxes
    /// at the local speed, so the wire never carries an animation, only the index.</summary>
    private const float PopRate = 8f;

    private readonly RemoteAvatar _owner;

    private GameObject? _root;              // fan pivot; child of the current hand holder
    private Transform? _holder;            // the holder we are currently parented under
    private readonly List<GameObject> _cards = new(MaxCards);
    private readonly List<RemoteCardArt> _faces = new(MaxCards); // per-slab cloned-front overlays (parallel to _cards)
    private int _builtCount = -1;          // how many card slabs currently exist (-1 = never built)
    private bool _poseInit;                // snap (no ease) on the first pose after (re)activation

    // FAN-OUT REVEAL (report 6, "Fächer ist sichtbar" / the fan being RAISED). The local fan does a
    // Demeo fan-in on Open (CardFan._openElapsed: every card seeds at the middle slot and flies out
    // to its own slot), but the remote ghost simply appeared fully spread the instant the count
    // arrived — a pop, not a raise. Seconds since this fan became visible; -1 = settled.
    private float _openElapsed = -1f;
    private const float OpenSeconds = Defaults.FanOpenDuration;    // CardsConfig.FanOpenDuration
    private const float OpenStagger = Defaults.FanOpenStagger;    // CardsConfig.FanOpenStagger (ripples outward)

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
        float p = Mathf.Clamp01((_openElapsed - Mathf.Abs(i - mid) * OpenStagger) / OpenSeconds);
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

    /// <summary>The character this fan is currently drawn for (0 = not resolved yet). A change in it
    /// while the fan is up IS the exchange edge.</summary>
    private int _shownActorId;

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
        float fill = Mathf.Clamp01((float)Mathf.Max(n, 1) / Mathf.Max(1, _maxHandForCurve));
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

    /// <summary>Reused scratch buffer for the remote actor's HAND-pile card widgets (no per-frame alloc).</summary>
    private readonly List<AbilityCardUI> _handBuffer = new(MaxCards);

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

    public RemoteHandFan(RemoteAvatar owner)
    {
        _owner = owner;
    }

    // ------------------------------------------------------------------ per frame --

    public void Tick(float dt)
    {
        SyncTuning();

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
        if (count == 0 && _leaving.Count == 0)
        {
            Hide();
            return;
        }

        EnsureRoot(holder);
        if (_root == null)
            return;

        // AN EMPTY INCOMING HAND STILL GETS ITS WIPE. count == 0 is a real switch target (every card
        // burnt, or a long rest) and the owner's own fan plays the full gather for it —
        // CardsDriver's swap edge fires on `_fan.Count > 0 || the incoming hand has widgets`. Hiding
        // here on the frame the wave sets off would have destroyed it outright (Hide -> EndSwap), so
        // the owner would see a wipe and every peer a blink. Once the wave has drained the count is
        // still 0, the branch above takes over, and the now-empty fan hides silently.
        if (count == 0)
        {
            TickSwap(dt);
            PoseFan(holder, dt);
            UpdateFaces(0, null);
            return;
        }

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
        if (shownId != 0 && _shownActorId != 0 && shownId != _shownActorId
            && _root.activeSelf && _cards.Count > 0)
        {
            // The wave keeps the OUTGOING character's faces, so it is handed that character — the
            // one the fan was showing until this frame — for its own reveal-gate check.
            BeginSwap(_shownActor);
            VRLog.Info("Net", $"Remote hand fan EXCHANGE [player {_owner.PlayerId}]: {_swapOutCount} slab(s) " +
                              $"gather off the arc while {count} deal in — the owner switched which character " +
                              "they are looking at (extension record 22's actor id, already on the wire). The " +
                              "MOTION is mirrored; no card identity is transmitted for it, and the leaving " +
                              "slabs are re-gated every frame on their OWN character's RevealGate verdict.");
        }
        _shownActorId = shownId;
        _shownActor = shownActor;
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
    /// Per-frame anti-cheat gate + front rendering. When <see cref="RevealGate.ShowRoundCardFronts"/>
    /// is true for the remote actor (never during the secret selection phase), overlay each slab with
    /// a CLONE of that actor's real hand-card face (<see cref="RemoteCardArt"/>); otherwise show BACKS.
    /// Every game deref is guarded and fails safe to BACKS on any error — no front can leak.
    /// </summary>
    private void UpdateFaces(int count, CPlayerActor? actor)
    {
        bool showFronts = false;
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
            // Also require an actual running scenario before touching the game's hand UI (the clone's
            // widget lifecycle depends on scenario singletons); off-scenario we simply show backs.
            if (actor != null && RevealGate.InScenario && RevealGate.ShowRoundCardFronts(actor))
            {
                ResolveHandFronts(actor);   // fills _handBuffer with the actor's HAND-pile widgets
                showFronts = _handBuffer.Count > 0;
            }
        }
        catch (System.Exception ex)
        {
            // ANY failure → no fronts, backs only (fail-safe = no cheat).
            showFronts = false;
            _handBuffer.Clear();
            VRLog.Warn("Net", $"RemoteHandFan front gate errored ({ex.Message}) — showing backs.");
        }

        for (int i = 0; i < _faces.Count; i++)
        {
            RemoteCardArt face = _faces[i];
            // Only slab indices that both (a) are within the built fan and (b) map to a resolved hand
            // widget with a real full card get a front; everything else stays a back.
            if (showFronts && i < count && i < _handBuffer.Count)
            {
                AbilityCardUI widget = _handBuffer[i];
                FullAbilityCard? full = widget != null ? widget.fullAbilityCard : null;
                if (full != null && face.ShowFront(full))
                {
                    frontCount++;
                    continue;
                }
            }
            face.HideFront();
        }

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
                leavingFronts = _leavingActor != null && RevealGate.InScenario
                                && RevealGate.ShowRoundCardFronts(_leavingActor);
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

        // Log exactly once per backs↔fronts transition — counts + gate state only, never identities.
        // The line NAMES the predicate on purpose: the same sentence appears on every other remote
        // card surface ("Remote pile browse fan faces", "Remote item fan faces", the board's
        // "Remote board content … fronts="), so one hardware log proves the phase rule across all of
        // them at once and a surface that disagrees is visible without a screenshot.
        bool nowFronts = frontCount > 0;
        if (nowFronts != _frontsShown)
        {
            _frontsShown = nowFronts;
            VRLog.Info("Net", nowFronts
                ? $"Remote hand fan faces [player {_owner.PlayerId}]: FRONTS — content=HAND, " +
                  $"{frontCount} card(s), gate: RevealGate.ShowRoundCardFronts(actor)=true."
                : $"Remote hand fan faces [player {_owner.PlayerId}]: BACKS — content=HAND, gate: " +
                  "RevealGate.ShowRoundCardFronts(actor)=false (the game's own secret " +
                  "SelectAbilityCardsOrLongRest phase) or no hand widget resolved.");
        }
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
        for (int i = 0; i < cards.Count && _handBuffer.Count < MaxCards; i++)
        {
            AbilityCardUI c = cards[i];
            if (c != null && c.CardType == CardPileType.Hand && c.fullAbilityCard != null)
                _handBuffer.Add(c);
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
        Transform head = _owner.HeadHolder;
        Quaternion targetRot;
        if (head != null)
        {
            Vector3 away = target - head.position;
            targetRot = away.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(away.normalized, Vector3.up)
                : root.rotation;
        }
        else
        {
            targetRot = root.rotation;
        }

        if (_poseInit)
        {
            _poseInit = false;
            root.SetPositionAndRotation(target, targetRot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-Smoothing * Mathf.Max(dt, 0f));
            root.SetPositionAndRotation(
                Vector3.Lerp(root.position, target, k),
                Quaternion.Slerp(root.rotation, targetRot, k));
        }
    }

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

        // Curvature-by-fill: a few cards read nearly flat/untilted, a full hand arches and tilts.
        float fill = Mathf.Clamp01((float)n / _maxHandForCurve);
        float arch = _archFactor * fill;
        float tilt = _tiltFactor * fill;

        // Fan-out reveal (see _openElapsed), now card-for-card what CardFan.Relayout blends on Open:
        // every card SEEDS at the MIDDLE slot's arc pose (not at the fan origin — the local fan-in
        // starts from the centre CARD, so a peer used to see the stack pop from a slightly wrong
        // place) and eases out to its own slot on an ease-out cubic with a per-card stagger delay
        // rippling outward from the middle. Runs on UNSCALED dt (the caller's already is).
        bool opening = _openElapsed >= 0f;
        int mid = n / 2;
        float midAngle = start + step * mid;
        float midRad = midAngle * Mathf.Deg2Rad;
        var collapsedRot = Quaternion.Euler(0f, 0f, -midAngle * tilt);
        var collapsedXY = new Vector2(Mathf.Sin(midRad) * _radius,
                                      (Mathf.Cos(midRad) - 1f) * _radius * arch);
        if (opening)
        {
            _openElapsed += Mathf.Max(dt, 0f);
            if (_openElapsed >= OpenSeconds + Mathf.Max(mid, n - 1 - mid) * OpenStagger)
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
    /// highlighted one — <c>CardFan.SplitOffset</c> against the AUTHORED defaults.</summary>
    private float SplitOffset(int signed)
    {
        float x = Mathf.Abs(signed) / Mathf.Max(0.0001f, _splitFalloff);
        return Mathf.Sign(signed) * Mathf.Exp(-x * x) * _splitMultiplier * Mathf.Max(0f, _splitScale);
    }

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
        _gazeX = Mathf.Lerp(_gazeX, targetX, 1f - Mathf.Exp(-GazeSmoothing * d));
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
            // The outgoing wave hung off the same dead root — drop its bookkeeping with the rest, or
            // TickSwap would drive destroyed transforms every frame (the very defect this heal
            // exists for, one list over).
            EndSwap();
            _shownActorId = 0;
            _shownActor = null;
            _builtCount = -1;
            _frontsShown = false;
            ClearPops();
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
        }

        // (Re)parent when the non-dominant holder changes (e.g. the sender flips dominant hand)
        // or when the root was just recreated (the old code compared holder identity only, so a
        // recreated root whose holder had not changed was never parented at all and floated at
        // the scene origin).
        if (_holder != holder)
        {
            _holder = holder;
            _root.transform.SetParent(holder, worldPositionStays: false);
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
        _faceViewer = t.FanFaceViewer;
        _sideDepthCurve = t.FanSideDepthCurve;
        _curvePower = t.FanCurvePower;
        _curveMinCards = Mathf.Max(0, t.FanCurveMinCards);
        _gazeApexFollow = t.FanGazeApexFollow;
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

        if (sizeChanged)
            _builtCount = -1;
    }

    /// <summary>Destroy and recreate exactly <paramref name="count"/> back-on-both-faces slabs, each
    /// with its own (initially hidden) cloned-front overlay. Only called when the count changes
    /// (cheap). Re-applies the mod layer so the owned head camera renders the new slabs.</summary>
    private void Rebuild(int count)
    {
        // Tear down existing front overlays first (each owns cloned game widgets — no leaks), then the
        // slabs they hang off.
        for (int i = _faces.Count - 1; i >= 0; i--)
            _faces[i].Destroy();
        _faces.Clear();

        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        _frontsShown = false;
        ClearPops(); // a rebuilt fan must never open with a stale card already lifted

        Mesh mesh = SharedCardMesh;
        Material back = CardMesh.CreateBackMaterial(); // shared: back texture on a Standard material

        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Card{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            // The slab MESH is the shared default-sized one; the owner's own CardWidth arrives as
            // a uniform scale (record 28), so their ghost cards read the size they see.
            card.transform.localScale = Vector3.one * (_cardWidth / DefaultCardWidth);
            var mf = card.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = card.AddComponent<MeshRenderer>();
            mr.sharedMaterial = back;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
            _faces.Add(new RemoteCardArt(card.transform, _cardWidth, _cardHeight));
        }

        _builtCount = count;

        // Owned head camera renders the mod layer only; put the whole fan subtree on it (no-op
        // when VR is not running, exactly like the local fan/hands).
        VRLayers.Apply(_root!);
    }

    private void Hide()
    {
        // Drop any cloned fronts so a hidden hand keeps no game-widget clones alive.
        for (int i = 0; i < _faces.Count; i++)
            _faces[i].HideFront();
        if (_frontsShown)
        {
            _frontsShown = false;
            VRLog.Info("Net", $"Remote hand fan [player {_owner.PlayerId}] hidden — cloned fronts released.");
        }
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
        _openElapsed = -1f; // next appearance fans out again from the centre stack
        // A hidden fan stops ticking, so an exchange in the air would freeze half-way off the hand.
        // Landing it here also means the next appearance plays the fan-out REVEAL (the right
        // animation for a hand being raised) rather than resuming a wipe nobody can see the start of.
        EndSwap();
        _shownActorId = 0;
        _shownActor = null;
        // Presentation state resets exactly like CardFan.Open does: the apex starts centred (a fan
        // that popped open already leaning would read as a glitch) and the next appearance logs its
        // geometry once so a hardware log has a line per fan, not one per session.
        _gazeX = 0f;
        _loggedCount = -1;
    }

    public void Destroy()
    {
        EndSwap(); // any outgoing wave dies with the fan — no orphaned slabs, no leaked clones
        _shownActorId = 0;
        _shownActor = null;
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

    // ------------------------------------------------------------------ card-back slab mesh --

    private static Mesh? _sharedCardMesh;

    /// <summary>A thin card-back slab whose BOTH faces show the mod's card-back texture: a front
    /// quad (-Z, normal back) and a back quad (+Z, normal forward), each a hair off centre so it
    /// reads as a solid card from either side. Built once and shared by every ghost card.</summary>
    /// <summary>Built at the DEFAULT card size and shared by every peer's fan; a peer whose own
    /// [Cards] CardWidth differs gets that size through the slab's localScale instead, so one tuned
    /// player cannot resize everybody else's cards through a shared mesh.</summary>
    private static Mesh SharedCardMesh => _sharedCardMesh != null ? _sharedCardMesh : (_sharedCardMesh = BuildBackSlab(DefaultCardWidth, DefaultCardHeight));

    /// <summary>Also consumed by <see cref="WorldUI.AvatarMirror"/> (mirrored local card fan):
    /// a thin both-faces-back card slab mesh. Caller owns the returned mesh.</summary>
    internal static Mesh BuildBackSlab(float w, float h)
    {
        float hw = w * 0.5f, hh = h * 0.5f, t = CardMesh.Thickness * 0.5f;

        // 8 verts: front face (z = -t, faces the viewer/owner at -Z) and back face (z = +t).
        var vertices = new[]
        {
            // front (-Z)
            new Vector3(-hw, -hh, -t), new Vector3(-hw, hh, -t), new Vector3(hw, hh, -t), new Vector3(hw, -hh, -t),
            // back (+Z)
            new Vector3(-hw, -hh, t), new Vector3(-hw, hh, t), new Vector3(hw, hh, t), new Vector3(hw, -hh, t),
        };
        var normals = new[]
        {
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
        };
        // Planar card-space UVs; mirror X on the back copy so the (symmetric) lattice lines up.
        var uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
        };
        // Winding chosen (verified via right-hand normal) so the front is visible from -Z and the
        // back from +Z — matching CardMesh's convention (+Z points away from the viewer).
        var tris = new[]
        {
            0, 1, 2, 0, 2, 3,       // front: RH normal -> -Z
            4, 6, 5, 4, 7, 6,       // back:  RH normal -> +Z
        };

        var mesh = new Mesh { name = "GloomhavenVR.RemoteCardBack" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}
