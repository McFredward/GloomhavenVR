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
///   * TUNED Fan* CONFIG: the constants above are seeded to the CardsConfig DEFAULTS and stay
///     self-contained (a peer's fan must not depend on the local player's Cards config being bound
///     or tuned). A sender who retunes their own fan geometry therefore reads slightly differently
///     to others than to themselves.
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

    private const float CardWidth = DefaultCardWidth;
    private const float CardHeight = DefaultCardHeight;
    private const float PalmOffset = 0.09f;                        // CardsConfig.FanPalmOffset

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
        return anchor.position + anchor.up * (PalmOffset * owner.AppliedScale);
    }
    private const float Radius = 0.2192f;                          // CardsConfig.FanEffectiveRadius
    private const float ArcSweepDegrees = 103f;                     // CardsConfig.FanArcSweepDegrees
    private const float PerCardStepDegrees = 14f;                  // CardsConfig.FanPerCardStepDegrees
    private const float ArchFactor = 0.55f;                        // CardsConfig.FanFlatCurvatureFactor
    private const float TiltFactor = 0.85f;                        // CardsConfig.FanTiltFactor
    private const int MaxHandForCurve = 10;                        // CardsConfig.FanMaxHandForCurve
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
    private const float FaceViewer = 1f;

    /// <summary>Depth bow at the ends of a full hand, metres (CardsConfig.FanSideDepthCurve).</summary>
    private const float SideDepthCurve = 0.035f;

    /// <summary>Bow exponent in the card's fraction-from-centre (CardsConfig.FanCurvePower).</summary>
    private const float CurvePower = 2f;

    /// <summary>Hands at or below this many cards stay flat (CardsConfig.FanCurveMinCards).</summary>
    private const int CurveMinCards = 3;

    /// <summary>Gaze relief amplitude (CardsConfig.FanGazeApexFollow): how much of the resting bow
    /// the card the owner is LOOKING at is lifted out of.</summary>
    private const float GazeApexFollow = 1f;

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
    private const float OpenSeconds = 0.14f;    // CardsConfig.FanOpenDuration
    private const float OpenStagger = 0.02f;    // CardsConfig.FanOpenStagger (ripples outward)

    /// <summary>Ease-out cubic progress (0..1) of card <paramref name="i"/> in the fan-out reveal —
    /// CardFan.OpenProgress verbatim, so a peer's fan opens on the owner's timing curve.</summary>
    private float OpenProgress(int i, int mid)
    {
        float p = Mathf.Clamp01((_openElapsed - Mathf.Abs(i - mid) * OpenStagger) / OpenSeconds);
        float inv = 1f - p;
        return 1f - inv * inv * inv;
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
        // Which hand does the fan hang off? owner.NonDominantHandHolder already resolves to the
        // LEFT holder when DominantRight is true (the sensible default), else RIGHT; fall back to
        // the avatar root when the holder is missing.
        // TODO(handedness): if fan is on wrong hand, foundation DominantRight predicate is inverted.
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
        if (count == 0)
        {
            Hide();
            return;
        }

        EnsureRoot(holder);
        if (_root == null)
            return;

        // Rebuild the card slabs only when the count actually changes (cheap; the sizes/poses of
        // existing slabs are refreshed every frame below and auto-inherit AppliedScale via the
        // scaled holder, so a scale change needs no rebuild).
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
        UpdateFaces(count);
    }

    // ------------------------------------------------------------------ front art (gated) --

    /// <summary>
    /// Per-frame anti-cheat gate + front rendering. When <see cref="RevealGate.ShowRoundCardFronts"/>
    /// is true for the remote actor (never during the secret selection phase), overlay each slab with
    /// a CLONE of that actor's real hand-card face (<see cref="RemoteCardArt"/>); otherwise show BACKS.
    /// Every game deref is guarded and fails safe to BACKS on any error — no front can leak.
    /// </summary>
    private void UpdateFaces(int count)
    {
        bool showFronts = false;
        int frontCount = 0;
        try
        {
            // Resolve the remote actor and the game's own reveal rule. NetPlayerActors.ActorFor and
            // RevealGate.ShowRoundCardFronts are both null-safe and degrade to no-front off-scenario.
            CPlayerActor? actor = NetPlayerActors.ActorFor(_owner.PlayerId);
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

        // Log exactly once per backs↔fronts transition — counts + gate state only, never identities.
        bool nowFronts = frontCount > 0;
        if (nowFronts != _frontsShown)
        {
            _frontsShown = nowFronts;
            VRLog.Info("Net", nowFronts
                ? $"Remote hand fan [player {_owner.PlayerId}] flipped to FRONTS (reveal gate open, {frontCount} card(s))."
                : $"Remote hand fan [player {_owner.PlayerId}] flipped back to BACKS (reveal gate closed).");
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
        Vector3 target = anchor.position + anchor.up * (PalmOffset * scale);

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

        float step = n > 1 ? Mathf.Min(PerCardStepDegrees, ArcSweepDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        // Curvature-by-fill: a few cards read nearly flat/untilted, a full hand arches and tilts.
        float fill = Mathf.Clamp01((float)n / MaxHandForCurve);
        float arch = ArchFactor * fill;
        float tilt = TiltFactor * fill;

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
        var collapsedXY = new Vector2(Mathf.Sin(midRad) * Radius,
                                      (Mathf.Cos(midRad) - 1f) * Radius * arch);
        if (opening)
        {
            _openElapsed += Mathf.Max(dt, 0f);
            if (_openElapsed >= OpenSeconds + Mathf.Max(mid, n - 1 - mid) * OpenStagger)
                _openElapsed = -1f;
        }

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

        for (int i = 0; i < _cards.Count; i++)
        {
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * Radius,
                                  (Mathf.Cos(rad) - 1f) * Radius * arch,
                                  i < n ? _depths[i] : -ZStagger * i);
            var rot = Quaternion.Euler(0f, 0f, -angle * tilt);

            // Per-card TOE-IN (CardFan.Relayout): aim THIS card's normal at the owner's head instead
            // of inheriting the root's single billboard normal. FromToRotation is the minimal arc
            // from the card's forward to the head, PRE-multiplied so the roll — the fan's signature
            // shape — survives exactly.
            if (haveHead && FaceViewer > 0f)
            {
                Vector3 toCard = pos - headLocal;
                if (toCard.sqrMagnitude > 1e-6f)
                {
                    Vector3 dir = toCard.normalized;
                    // FaceViewer is the toe-in GAIN. Slerped unconditionally from "no toe-in" (the
                    // branch CardFan takes only when the gain is < 1 would be dead code against a
                    // const, and Slerp at 1 returns the aim itself) so the constant stays honest if
                    // it is ever re-seeded from a retuned CardsConfig default.
                    Quaternion aim = Quaternion.Slerp(
                        Quaternion.identity, Quaternion.FromToRotation(Vector3.forward, dir), FaceViewer);
                    rot = aim * rot;
                    float deg = Vector3.Angle(Vector3.forward, dir) * FaceViewer;
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
            t.localPosition = pos;
            t.localRotation = rot;
        }

        LogGeometry(n, apex, maxToeDeg, haveHead);
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
    private static float ArcHalfWidth(int n)
    {
        if (n < 2)
            return Radius;
        float step = Mathf.Min(PerCardStepDegrees, ArcSweepDegrees / (n - 1));
        float half = step * (n - 1) * 0.5f;
        return Mathf.Max(0.001f, Mathf.Sin(half * Mathf.Deg2Rad) * Radius);
    }

    /// <summary>The gaze apex as a FRACTIONAL card index (CardFan.GazeApexIndex).</summary>
    private float GazeApexIndex(int n)
    {
        float center = (n - 1) * 0.5f;
        if (n < 2 || GazeApexFollow <= 0f)
            return center;
        float step = Mathf.Min(PerCardStepDegrees, ArcSweepDegrees / (n - 1));
        if (step <= 0.0001f)
            return center;
        float start = -step * (n - 1) * 0.5f;
        float angle = Mathf.Asin(Mathf.Clamp(_gazeX / Radius, -1f, 1f)) * Mathf.Rad2Deg;
        return Mathf.Clamp((angle - start) / step, 0f, n - 1f);
    }

    /// <summary>The resting, gaze-independent symmetric cup (CardFan.RestBowDepth).</summary>
    private static float RestBowDepth(int i, int n)
    {
        if (n < 2 || SideDepthCurve == 0f || n <= CurveMinCards)
            return 0f;
        float half = (n - 1) * 0.5f;
        float frac = Mathf.Clamp01(Mathf.Abs(i - half) / half);
        int hi = Mathf.Max(CurveMinCards + 1, MaxHandForCurve);
        float fill = Mathf.Clamp01((float)(n - CurveMinCards) / (hi - CurveMinCards));
        return SideDepthCurve * Mathf.Pow(frac, CurvePower) * fill;
    }

    /// <summary>The resting cup with the gaze RELIEF applied (CardFan.BowDepth): the card under the
    /// apex comes fully out of the bow, every other card keeps at most what it had at rest.</summary>
    private float BowDepth(int i, int n, float apex)
    {
        float rest = RestBowDepth(i, n);
        if (rest == 0f || GazeApexFollow <= 0f)
            return rest;
        float w = Mathf.Max(GazeReliefMinWidth, (n - 1) * 0.5f * GazeReliefWidthFactor);
        float d = (i - apex) / w;
        return rest * (1f - GazeApexFollow * Mathf.Exp(-d * d));
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
        if (SideDepthCurve > 0f)
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
        if (_root == null)
        {
            _root = new GameObject($"GloomhavenVR.RemoteHandFan[{_owner.PlayerId}]");
            _root.transform.localScale = Vector3.one; // inherit AppliedScale from the holder
            _root.SetActive(false);
        }

        // (Re)parent when the non-dominant holder changes (e.g. the sender flips dominant hand).
        if (_holder != holder)
        {
            _holder = holder;
            _root.transform.SetParent(holder, worldPositionStays: false);
            _poseInit = true; // snap to the new hand rather than easing across the body
        }
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

        Mesh mesh = SharedCardMesh;
        Material back = CardMesh.CreateBackMaterial(); // shared: back texture on a Standard material

        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Card{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            card.transform.localScale = Vector3.one;
            var mf = card.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = card.AddComponent<MeshRenderer>();
            mr.sharedMaterial = back;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
            _faces.Add(new RemoteCardArt(card.transform, CardWidth, CardHeight));
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
        // Presentation state resets exactly like CardFan.Open does: the apex starts centred (a fan
        // that popped open already leaning would read as a glitch) and the next appearance logs its
        // geometry once so a hardware log has a line per fan, not one per session.
        _gazeX = 0f;
        _loggedCount = -1;
    }

    public void Destroy()
    {
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
    private static Mesh SharedCardMesh => _sharedCardMesh != null ? _sharedCardMesh : (_sharedCardMesh = BuildBackSlab(CardWidth, CardHeight));

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
