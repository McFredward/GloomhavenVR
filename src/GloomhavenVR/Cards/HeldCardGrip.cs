using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// WHICH OF THE TWO WAYS a hand is holding its card — the mode machine behind
/// <see cref="CardGripPose"/>.
///
/// <para>THE TWO MODES, and the gesture that picks one (user, 2026-08-29, verbatim: "Wenn man mit
/// trigger eine Karte greift, schwebt sie immer so, dass man sie direkt sehen kann. Das will ich
/// auch weiterhin so. Jetzt kann es aber auch nützlich sein, die Karte so halten, dass wie die
/// Hand-Orientierung ist um zB anderen Spielern aktiv die Karte zeigen zu können. … Wenn man die
/// Greiftaste gedrückt hält und dann trigger drückt um eine Karte zu nehmen, soll man die Karte
/// wirklich 'in die Hand nehmen' und vollständig rotieren können. Lässt man die Greiftaste wieder
/// los, hält aber trigger gedrückt, soll man wieder in den jetzt normal existierenden Modus
/// gehen."):</para>
/// <list type="bullet">
/// <item><description>READING (unchanged, and still the default): the card's position follows the
/// wrist but its face is re-billboarded to the head every frame, so it is legible however the
/// wrist is turned. The hand ghosts while it does this.</description></item>
/// <item><description>IN-HAND: the card is rigid in the fist. Turning the wrist turns the card,
/// which is what lets a player AIM the face at somebody. The fingers close on it in the modelled
/// grip (<see cref="CardGripPose.Curls"/>) and the hand does NOT ghost — the whole point is that
/// the other player sees a hand holding a card.</description></item>
/// </list>
///
/// <para>THE STATE MACHINE IS TWO LINES, and both of them are the user's sentences:</para>
/// <list type="number">
/// <item><description>ARM — the hold begins in-hand only if the GRIP was already down when the
/// trigger took the card. This is what keeps the mode out of the way of a player who never uses
/// the gesture: a plain trigger grab can never turn into an in-hand hold, however the grip is
/// squeezed afterwards.</description></item>
/// <item><description>FOLLOW — while armed, the mode simply IS the grip button. Releasing the grip
/// drops back to reading mode (his second sentence, literally), and squeezing it again returns —
/// which he did not ask for and gets for free, and is the behaviour that makes the mode usable:
/// read the card, show the card, read it again, without ever letting go.</description></item>
/// </list>
///
/// <para>WHY A ONE-PLACE ANSWER RATHER THAN A FLAG ON THE CARD. Five subsystems have to agree on
/// this bit within a frame — the card's own pose solver, the finger curls
/// (<c>Hands.VRHand.UpdateCurlTargets</c>), the ghost-hand policy (<c>Hands.HandGhosts</c>), the
/// mirror (<c>WorldUI.AvatarMirror</c>) and the wire (<c>Net.Avatar.LocalRigSampler</c> →
/// extension record 34). Two of those cannot see the card object at all; they know a HAND. So the
/// state is keyed by hand, computed once per frame in <see cref="Tick"/>, and read everywhere
/// else — the shape <see cref="HandGhosts"/> already uses for the same reason, and the one that
/// stops "is this card in-hand?" being answered two different ways in one frame.</para>
///
/// <para>MULTIPLAYER. The mode changes what everyone else sees, so it goes on the wire: peers draw
/// a held card's slab by re-deriving the billboard at the OWNER's head, which is exactly right for
/// reading mode and exactly wrong for this one. Extension record 34 carries one bit per held-card
/// slot and the receiver keeps the transmitted rotation instead (see
/// <c>NetProtocol.ExtIdHeldCardGrip</c>). The finger pose needs no field at all — curls already
/// ride every rig packet, so a peer's hand closes into the modelled grip on its own.</para>
/// </summary>
internal static class HeldCardGrip
{
    private static bool _leftArmed;
    private static bool _rightArmed;
    private static bool _leftInHand;
    private static bool _rightInHand;

    // THE GRASP, 0 = reading pose, 1 = fully in the fist. Raw linear progress; every reader takes
    // it through CardGripPose.Ease. One per hand, advanced once per frame in Tick, and it is the
    // ONLY thing that moves between the two poses - the curls, the card's position, its rotation,
    // the mirror and the ghost all read this same number so nothing can arrive early.
    private static float _leftBlend;
    private static float _rightBlend;

    /// <summary>The feature switch ([Cards] InHandHold); false before the config is bound. Same
    /// defensive read as <see cref="HandGhosts.Enabled"/>, and for the same reason: this is called
    /// from a per-frame path that runs before and after the config's lifetime.</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return CardsConfig.InHandHold != null && CardsConfig.InHandHold.Value;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>True while the LEFT hand holds its card in the rigid in-hand grip.</summary>
    internal static bool LeftInHand => _leftInHand;

    /// <summary>True while the RIGHT hand holds its card in the rigid in-hand grip.</summary>
    internal static bool RightInHand => _rightInHand;

    /// <summary>The mode of one hand — false = reading (billboard), true = in-hand. This is the
    /// INTENT (the grip button), which flips in one frame; what actually moves is
    /// <see cref="Blend"/>.</summary>
    internal static bool InHand(HandSide side) =>
        side == HandSide.Left ? _leftInHand : _rightInHand;

    /// <summary>
    /// HOW FAR INTO THE GRASP one hand is, eased: 0 = the card is billboarding at the head and the
    /// fingers are wherever the controller puts them, 1 = the card is rigid in the modelled fist.
    /// Everything in between is the animation.
    ///
    /// <para>Every consumer reads THIS rather than <see cref="InHand"/>, and each of them means
    /// something slightly different by it, which is worth stating once:</para>
    /// <list type="bullet">
    /// <item><description>the FINGERS blend from the controller's curls to the modelled ones;</description></item>
    /// <item><description>the CARD blends between the billboard pose and the grip pose;</description></item>
    /// <item><description>the MIRROR blends between the same two rules, so the reflection travels
    /// with the real card instead of switching under it;</description></item>
    /// <item><description>the WIRE bit is set for anything above 0 — a peer must use the transmitted
    /// rotation for the whole journey, because for the whole journey the card is somewhere the
    /// billboard rule cannot predict;</description></item>
    /// <item><description>the GHOST crosses at the halfway mark, because it is a material swap with
    /// no midpoint and the least conspicuous place for one is the middle of a motion.</description></item>
    /// </list>
    /// </summary>
    internal static float Blend(HandSide side) =>
        CardGripPose.Ease(side == HandSide.Left ? _leftBlend : _rightBlend);

    /// <summary>The grasp progress of one hand; 0 for a hand that does not exist this frame.</summary>
    internal static float Blend(VRHand? hand) => hand != null ? Blend(hand.Side) : 0f;

    /// <summary>True once the grasp is past halfway — the ghost hand's edge. See
    /// <see cref="Blend(HandSide)"/> for why this one is a threshold and the others are not.</summary>
    internal static bool PastHalf(HandSide side) =>
        (side == HandSide.Left ? _leftBlend : _rightBlend) >= 0.5f;

    /// <summary>Seconds the grasp takes ([Cards] InHandGraspSeconds); the shipped default before
    /// the config is bound. Clamped away from zero — a zero duration is the snap this exists to
    /// remove, and it would also divide by itself.</summary>
    private static float GraspSeconds
    {
        get
        {
            try
            {
                return CardsConfig.InHandGraspSeconds != null
                    ? Mathf.Max(0.02f, CardsConfig.InHandGraspSeconds.Value)
                    : CardGripPose.DefaultGraspSeconds;
            }
            catch
            {
                return CardGripPose.DefaultGraspSeconds;
            }
        }
    }

    /// <summary>The mode of one hand; false for a hand that does not exist this frame.</summary>
    internal static bool InHand(VRHand? hand) => hand != null && InHand(hand.Side);

    /// <summary>
    /// Per-frame policy step, called from <see cref="HandsDriver"/> under its own
    /// <see cref="TickGuard"/> after the rig step (so a hand rebuilt this frame is already in
    /// place) and before the ghost step (which reads the answer).
    /// </summary>
    internal static void Tick()
    {
        Evaluate(VRHands.Left, ref _leftArmed, ref _leftInHand);
        Evaluate(VRHands.Right, ref _rightArmed, ref _rightInHand);
        // UNSCALED time on purpose. The card phases pause timeScale (the same reason the fan reveal
        // and the card release glide run unscaled), and a hand that freezes half-closed round a card
        // because the game paused is exactly the frame-to-frame artefact this animation exists to
        // avoid. Capped per frame so a hitch cannot teleport the grasp.
        float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f) / GraspSeconds;
        Advance(ref _leftBlend, _leftInHand, step);
        Advance(ref _rightBlend, _rightInHand, step);
    }

    private static void Advance(ref float blend, bool want, float step) =>
        blend = Mathf.Clamp01(blend + (want ? step : -step));

    /// <summary>Module shutdown / hot reload: forget both hands.</summary>
    internal static void Shutdown()
    {
        _leftArmed = _rightArmed = false;
        _leftInHand = _rightInHand = false;
        _leftBlend = _rightBlend = 0f;
    }

    private static void Evaluate(VRHand? hand, ref bool armed, ref bool inHand)
    {
        // NOT HOLDING A CARD - disarm. This is the only place the arm latch clears, and it clears
        // on the ABSENCE of a held card rather than on a release edge on purpose: a card can leave
        // a hand without OnRelease ever running here (ProximityGrabber.HealDeadHeld force-drops a
        // stuck hold, a pooled card is destroyed under the hand, the interactor is switched off by
        // mode policy). A latch that only a release edge clears is a latch that survives all three.
        if (hand == null || !IsCard(hand))
        {
            armed = false;
            inHand = false;
            return;   // the blend runs back down on its own — a released card's hand OPENS
        }

        // ARM: the grip must ALREADY be down when the card arrives. Sampled here rather than in
        // VRCard.OnGrab because the two card grabbables (ability card and item chip) would need
        // the identical hook twice, and because the grab can also come from the laser pluck
        // (ProximityGrabber.ForceGrab), which is a third entry. This step runs after the hands
        // have ticked, so the grip state read here is the one the frame's grab saw.
        if (!armed)
        {
            if (!Enabled || !hand.GripPressed)
                return;      // reading mode; re-checked next frame only while the card is held
            armed = true;
        }

        bool want = Enabled && hand.GripPressed;
        if (want == inHand)
            return;
        inHand = want;
        // Edge-gated, never per-frame: this fires on a deliberate button press, at most a handful
        // of times per hold. It names every consequence because the mode changes five things at
        // once, and a hardware report saying "the card behaved oddly" has to be attributable to
        // one of them.
        VRLog.Info("Cards", $"Held card ({hand.Side}): {(want ? "IN-HAND" : "READING")} - "
            + (want
                ? "grip held, so the card is rigid in the fist (turn the wrist to show it); "
                  + "modelled grip curls applied, hand ghost off, peers get the transmitted rotation."
                : "grip released, so the card billboards to your head again; hand ghost back on, "
                  + "peers re-derive the billboard at your head."));
    }

    /// <summary>
    /// The in-hand pose for a card of <paramref name="cardWidth"/> x <paramref name="cardHeight"/>
    /// metres (at its HELD scale), in the holding hand's grab-anchor frame. False - and nothing written - when that hand is
    /// not in the in-hand mode this frame, which is the caller's cue to keep the billboard.
    ///
    /// <para>SAMPLED EVERY FRAME, not captured at grab time like the reading pose. The pinch point
    /// is the midpoint of the live thumb and index TIPS, and in this mode those tips are moving:
    /// the modelled grip curls (<see cref="CardGripPose.Curls"/>) ease in over the same fraction of
    /// a second the card is flying into the hand. A pose captured on the grab edge would be built
    /// from whatever the fingers happened to be doing when the trigger went down - which, since the
    /// gesture requires the GRIP to be held, is a closed fist.</para>
    ///
    /// <para>Both card grabbables call this (<see cref="VRCard"/> and
    /// <see cref="ItemsPile.ItemChip"/>) rather than each carrying its own copy of the sampling.
    /// The reading pose next door is duplicated between them - "VRCard.GetHeldPose verbatim" says
    /// its own comment - and the 2026-08-09 report is what that cost: the left-hand mirror was
    /// fixed in one copy and not the other, and the item card sat 11 cm out for five days.</para>
    /// </summary>
    internal static bool TryPose(VRHand? hand, float cardWidth, float cardHeight,
                                 out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        // Gated on the BLEND, not on the mode: the grip pose is needed for the whole journey, and
        // on the way back out too — the caller is interpolating toward it, or away from it.
        if (hand == null || Blend(hand.Side) <= 0f || hand.Rig == null || hand.Rig.GrabAnchor == null)
            return false;

        // THE TWO THINGS THAT SANDWICH THE CARD, and they are NOT the two fingertips. This hold
        // is the thumb flat along the card's face with the fingers curled behind it (see
        // CardGripPose.Curls for why a tip-to-tip pinch is not producible on these rigs), so the
        // contacts are the THUMB TIP and the INDEX KNUCKLE — the middle phalanx a card really rests
        // against. Taking the index TIP instead would put the reference deep in the curled palm and
        // drag the card in after it.
        Vector3 pinchLocal;
        FingerJoints thumb = hand.Rig.GetFinger(Finger.Thumb);
        FingerJoints index = hand.Rig.GetFinger(Finger.Index);
        if (thumb.IsValid && index.IsValid)
        {
            Vector3 pinchWorld = (thumb.Tip.position + index.Mid.position) * 0.5f;
            pinchLocal = hand.Rig.GrabAnchor.InverseTransformPoint(pinchWorld);
        }
        else
        {
            // Same fallback the reading pose uses for a partial rig: the palm-offset approximation.
            // Never taken by the procedural hand or any bundle glove - all five digits exist on
            // both - but the HandRig contract permits a rig without them.
            pinchLocal = new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
        }

        // AUTHORED RIGHT, MIRRORED LEFT - the X term only. The two grab anchors are anatomical
        // mirrors (+Y out of the palm and +Z along the fingers on BOTH hands), so +X is the thumb
        // side on the right hand and the pinky side on the left. See CardsConfig.InHandPinchOffset
        // and, for what the raw form costs, the root-cause note on VRCard.GetHeldPose.
        Vector3 offset = CardsConfig.InHandPinchOffset.Value;
        if (hand.Side == HandSide.Left)
            offset.x = -offset.x;
        pinchLocal += offset;

        // +1 on the right hand, -1 on the left: the anchor frames are mirrors, so the lateral axis
        // - which IS the card's face normal in this pose - points at the thumb on one hand and at
        // the pinky on the other. Handed once, here, exactly like the offset above.
        float thumbSide = hand.Side == HandSide.Right ? 1f : -1f;
        CardGripPose.Solve(CardsConfig.InHandPitch.Value, thumbSide, pinchLocal,
                           cardWidth, cardHeight, out pos, out rot);
        return true;
    }

    /// <summary>"Does this hand hold a CARD?" — the SAME two types <see cref="HandGhosts"/>'s
    /// ghost gate and the wire sampler name, and named the same way (two explicit type tests, not
    /// a capability interface), so a new grabbable can never acquire this mode by accident. See
    /// <c>HandGhosts.IsHeldCard</c> for the root cause of that shape.</summary>
    private static bool IsCard(VRHand? hand) =>
        hand != null && hand.Grabber != null
        && hand.Grabber.Held is VRCard or ItemsPile.ItemChip;
}
