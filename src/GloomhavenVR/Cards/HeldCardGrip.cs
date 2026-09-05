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
/// <para>THE STATE MACHINE IS ONE LINE: while a hand holds a card, the mode IS that hand's grip
/// button. Press to take the card into the fist, release to let it float readable again, press
/// again to show it — at any point in the hold, in either hand, however the card got there.</para>
///
/// <para>IT WAS TWO LINES FOR THREE BUILDS, and the extra one was wrong. The first version also
/// required the grip to be ALREADY DOWN when the trigger took the card ("armed"), so that a player
/// who never uses the gesture could not fall into it. That reading of the user's sentence quietly
/// excluded every card taken with the LASER — because <c>RayInteractor.Active</c> requires
/// <c>!GripSuppressed</c>, i.e. holding the grip TURNS THE BEAM OFF (his own request, 2026-08-24).
/// A laser-plucked card therefore could not be armed at grab time and could never enter the mode at
/// all. His follow-up settled it in his own words — "wenn man die Kartenhand wechselt und dann mit
/// der Greiftaste (gedrückt gehalten) den modus wechselt" — the grip SWITCHES the mode, whenever it
/// is pressed, not only at the moment of the grab.</para>
///
/// <para>Nothing is lost by dropping the arm: while a card is held the grip is otherwise completely
/// unused, and the mod knows it — <c>Rig.ComfortGizmos</c> literally prints "grip(unused)" for that
/// state. Every other grip consumer guards on <c>Grabber.Held == null</c>, and world locomotion
/// rides the thumbstick click. And the whole feature has a switch ([Cards] InHandHold) for anyone
/// who would rather the button stayed dead.</para>
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
        Evaluate(VRHands.Left, ref _leftInHand);
        Evaluate(VRHands.Right, ref _rightInHand);
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
        _leftInHand = _rightInHand = false;
        _leftBlend = _rightBlend = 0f;
    }

    private static void Evaluate(VRHand? hand, ref bool inHand)
    {
        // NOT HOLDING A CARD - back to the reading mode. Keyed on the ABSENCE of a held card rather
        // than on a release edge, because a card can leave a hand without OnRelease ever running
        // here (ProximityGrabber.HealDeadHeld force-drops a stuck hold, a pooled card is destroyed
        // under the hand, the interactor is switched off by mode policy). The blend then runs back
        // down on its own, so a hand whose card is taken away OPENS rather than staying clenched.
        //
        // NO MODE, PHASE OR TURN GATE, and that is deliberate rather than an omission (user:
        // "Das soll überall möglich sein, wo man die Karten nehmen kann, d.h. auch in der Map
        // Umgebung zB. oder auch wenn man nicht dran ist"). This asks ONE question - is this hand
        // holding a card? - so the mode reaches wherever a card can be held, by construction:
        //   * the map room builds real VRCards and hands them to the same CardsDriver and the same
        //     CardFan the scenario uses (WorldUI/MapRoom/MapRoomHand.2.Fan.cs: "There is exactly one
        //     implementation of 'a hand of cards' in this mod again"), and it resolves to
        //     VRMode.TableIdle, whose interactor row carries Grab on BOTH hands;
        //   * out of turn, CardsDriver's inspectGrab arm keeps hand-fan, browse-arc and
        //     active-column cards pickable "in every phase and every mode" (his 2026-08-08 ruling);
        //   * and the two card types named below are the only card grabbables that exist - the
        //     others are a pile STACK, a board figure and a window handle.
        if (hand == null || !IsCard(hand))
        {
            inHand = false;
            return;
        }

        bool want = Enabled && hand.GripPressed;
        if (want == inHand)
            return;
        inHand = want;
        // Edge-gated, never per-frame: this fires on a deliberate button press. It names every
        // consequence because the mode changes five things at once, and a hardware report saying
        // "the card behaved oddly" has to be attributable to one of them.
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
        // +1 on the right hand, -1 on the left: the anchor frames are mirrors, so the lateral axis
        // - which IS the card's face normal in this pose - points at the thumb on one hand and at
        // the pinky on the other, and the tuned lateral offset takes the same sign. ONE read of
        // the project's one definition (Board.FigureGrab.HeldPoseMirror.OffsetSign, which the
        // figure and prop grabs and both card reading poses also call) instead of the two separate
        // spellings this method used to carry - the rule the header above says has been broken
        // twice in this file's short life is not a rule anybody can spell correctly by hand often
        // enough.
        float thumbSide = Board.FigureGrab.HeldPoseMirror.OffsetSign(hand.Side == HandSide.Left);
        Vector3 offset = CardsConfig.InHandPinchOffset.Value;
        offset.x *= thumbSide;
        pinchLocal += offset;
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
