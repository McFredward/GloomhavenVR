using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE HANDEDNESS LINE, for BOTH grab paths (2026-09-05, the handedness round, second pass).
///
/// <para><b>WHY IT MOVED OUT OF <c>GrabbableProp</c>.</b> It was a prop-only diagnostic that
/// printed a figure column for comparison — which meant it could describe the figure path but could
/// never be emitted BY it. Two of the round's open questions could not be answered from a line with
/// that shape: "which path did the thing in his hand actually use" (a destructible obstacle goes
/// through <c>FigureGrabbable</c>, so the <c>PropHeld*</c> dials he was turning were not connected
/// to it) and "does the figure path really share the code" (a claim, until the same emitter runs on
/// both). One emitter, two call sites, and the <c>path=</c> clause names which one ran.</para>
///
/// <para><b>THE TOKENS ARE APPEND-ONLY.</b> The 434 sentence is reproduced byte for byte —
/// <c>HANDEDNESS</c>, <c>MAP ITEM:</c>, <c>mirrorSwing=</c>, <c>FIGURE, same hand, for
/// comparison:</c> — because earlier rounds' greps are anchored on them; only the leading subsystem
/// tag varies (<c>[Props]</c> on the map-item path, <c>[Figure]</c> on the figure path) and
/// everything this round added is appended after it. Anchor a grep on <c>"] "</c>: this mod's lines
/// quote other instruments' tokens inside their own prose and that has produced three wrong counts
/// on this very question.</para>
///
/// <para><b>WHAT THIS ROUND ADDED, AND WHICH QUESTION EACH TERM CLOSES.</b>
/// <list type="bullet">
///   <item><c>path=</c> / <c>item=</c> — which implementation held the thing, so "he tuned a
///   disconnected dial" stops being a question anyone has to ask him.</item>
///   <item><c>poseFrame=</c> and <c>uprightAtGrab=</c> — THE decisive term. With upright-at-grab on
///   the held rotation is composed against <c>Inverse(anchor.rotation) * W</c>, the anchor cancels
///   out of the world pose, and the mirror lands in a frame with no handedness in it. That is where
///   the swing comes from; see <see cref="HeldPoseMirror"/>.</item>
///   <item><c>grabHeading=</c> — the one number that says whether a mirror was ever the right
///   operation. Two hands with OPPOSITE headings reached mirror-symmetrically and the mirror
///   reproduces the hold exactly; two hands with the SAME heading reached for the same hex and the
///   mirror is a <c>wrap(2·yaw)</c> spin instead. Both hands' headings are printed whenever the
///   other hand exists, so one line decides it.</item>
///   <item><c>anchorMirrorResidual=</c> beside <c>anchorRawAngle=</c> — the anchor question,
///   settled rather than asked. The raw left↔right angle reads 11.4° on the Arcane set and has been
///   read as an authoring fault; the residual against the true mirror is what would actually show
///   one, and it is ~0 on every shipped set.</item>
/// </list></para>
///
/// <para>PRESENTATION ONLY: every value here is read, none is written, and the emitter is capped
/// per session and per path. Allocation happens once per grab at most, never on a per-frame
/// path.</para>
/// </summary>
internal static class HeldPoseReport
{
    /// <summary>The map-item path's budget. Its own counter, not shared with the figure path's:
    /// a burst of prop grabs must not silence the figure line that answers a different
    /// question.</summary>
    private static int _propLogsLeft = 8;

    /// <summary>The figure path's budget, separate for the same reason.</summary>
    private static int _figureLogsLeft = 8;

    /// <summary>The map-item call site — <c>GrabbableProp.OnGrab</c>.</summary>
    internal static void EmitForProp(HandSide side, string label, Transform? anchor)
    {
        if (_propLogsLeft <= 0)
            return;
        _propLogsLeft--;
        Emit("[Props]", side, $"MAP-ITEM (GrabbableProp; the [FigureGrab] PropHeld* dials)",
             label, anchor, PropHeldPose.HeldUprightAtGrab, _propLogsLeft);
    }

    /// <summary>The figure call site — <c>FigureGrabbable.OnGrab</c>, which also carries every
    /// destructible obstacle with health (<c>ActorPropBody.Hold</c>). Those are the ones the
    /// <c>PropHeld*</c> dials have never reached, so <paramref name="healthProp"/> says so on the
    /// line itself.</summary>
    internal static void EmitForFigure(HandSide side, string label, Transform? anchor, bool healthProp)
    {
        if (_figureLogsLeft <= 0)
            return;
        _figureLogsLeft--;
        string what = healthProp
            ? "FIGURE (FigureGrabbable + ActorPropBody; a DESTRUCTIBLE OBSTACLE with health — it "
              + "rides the FIGURE dials, and the [FigureGrab] PropHeld* keys are NOT connected to it)"
            : "FIGURE (FigureGrabbable; the [FigureGrab] {Glove,Plate,Arcane}Held* dials)";
        ConfigEntry<bool>? atGrab = FigureGrabConfig.HeldUprightAtGrab;
        Emit("[Figure]", side, what, label, anchor,
             atGrab != null ? atGrab.Value : Defaults.HeldUprightAtGrab, _figureLogsLeft);
    }

    private static void Emit(
        string tag, HandSide side, string what, string label, Transform? anchor,
        bool uprightAtGrab, int budgetLeft)
    {
        HandSide other = side == HandSide.Left ? HandSide.Right : HandSide.Left;

        bool propUpright = PropHeldPose.HeldUpright;
        Quaternion propThis = PropHeldPose.HeldRotationFor(side, propUpright);
        Quaternion propOther = PropHeldPose.HeldRotationFor(other, propUpright);
        Vector3 propOff = PropHeldPose.HeldOffsetFor(side);

        // The figure half is READ-ONLY and guarded: these entries are bound at plugin start, long
        // before any hand exists, but a diagnostic must never be the thing that throws inside a
        // grab. A null entry reads as the shipped mode rather than refusing the whole line.
        ConfigEntry<bool>? figUprightEntry = FigureGrabConfig.HeldUpright;
        bool figUpright = figUprightEntry != null ? figUprightEntry.Value : Defaults.HeldUpright;
        Quaternion figThis = FigureGrabConfig.HeldRotationFor(side, figUpright);
        Quaternion figOther = FigureGrabConfig.HeldRotationFor(other, figUpright);
        Vector3 figOff = FigureGrabConfig.HeldOffsetFor(side);

        // HW-VERIFY: the line that answers "why the figures and not the props". It must stay at a
        // tier the DEFAULT log level prints (Note/Alert/Error) — scripts/check-hw-verify.py.
        VRLog.Note("FigureGrab",
            $"{tag} HANDEDNESS {side} hand — [FigureGrab] PropHeldSameInBothHands="
            + $"{PropHeldPose.Alike} (on = both hands take the mirrored form, so the item sits the "
            + "same way in each; the figures are always mirrored and have no such key). "
            + $"MAP ITEM: applied rot {FmtSigned(propThis)}° in the hand's own frame, from authored "
            + $"pitch={PropHeldPose.Pitch:0.#}° yaw={PropHeldPose.Yaw:0.#}° roll={PropHeldPose.Roll:0.#}° "
            + $"(this hand takes yaw={PropHeldPose.YawFor(side):0.#}° roll={PropHeldPose.RollFor(side):0.#}°), "
            + $"offset=({propOff.x:0.###},{propOff.y:0.###},{propOff.z:0.###}) m, upright={propUpright}, "
            + $"mirrorSwing={Quaternion.Angle(propThis, propOther):0.#}° vs the {other} hand. "
            + $"FIGURE, same hand, for comparison: applied rot {FmtSigned(figThis)}° from authored "
            + $"pitch={FigureGrabConfig.ActiveHeldTilt:0.#}° yaw={FigureGrabConfig.ActiveHeldFaceYaw:0.#}° "
            + $"roll={FigureGrabConfig.ActiveHeldRoll:0.#}° (this hand takes "
            + $"yaw={FigureGrabConfig.HeldFaceYawFor(side):0.#}° roll={FigureGrabConfig.HeldRollFor(side):0.#}°), "
            + $"offset=({figOff.x:0.###},{figOff.y:0.###},{figOff.z:0.###}) m, upright={figUpright}, "
            + $"mirrorSwing={Quaternion.Angle(figThis, figOther):0.#}°. "
            + "Both swings come from ONE shared mirror (HeldPoseMirror): it flips the yaw, the roll "
            + "and the sideways offset and leaves the pitch alone, so the swing is set by the tuned "
            + "YAW and by nothing else — 0° at yaw 0 or ±180, widest near ±90. A map-item "
            + "mirrorSwing of 0° with PropHeldSameInBothHands on is the switch working, not the "
            + "dial being at a fixed point; the FIGURE swing beside it is the one that says which. "
            // ---- everything below is 2026-09-05 round 2, appended, no token above reworded ----
            + $"PATH: path={what} item={label}. "
            + $"FRAME: uprightAtGrab={uprightAtGrab} so poseFrame="
            + (uprightAtGrab
                ? "WORLD-YAW — the held rotation is composed against Inverse(anchor.rotation)*W, so "
                  + "the ANCHOR CANCELS OUT of the world pose and the mirror lands in a frame with no "
                  + "handedness left in it; what survives is a spin of wrap(2*yaw) between the hands, "
                  + "which is the whole defect"
                : "ANCHOR — the pose rides the hand frame, where the reflection is exactly right and "
                  + "the two hands really are a mirror pair")
            + ". " + Headings(side, anchor) + " " + AnchorPair()
            + " The OFFSET is written in ANCHOR space either way and its flip follows no switch. "
            + "PropHeldSameInBothHands assumes poseFrame=WORLD-YAW; on a poseFrame=ANCHOR line it "
            + "should be OFF, because there the reflection keeps the frame it is justified in. "
            + $"({budgetLeft} more {tag} handedness lines this session.)");
    }

    /// <summary>The grab-time heading of this hand and, when it exists, of the other — the term
    /// that decides whether the two hands reached mirror-symmetrically (opposite headings, mirror
    /// correct) or for the same hex (equal headings, mirror is a spin).</summary>
    private static string Headings(HandSide side, Transform? anchor)
    {
        if (anchor == null)
            return "HEADINGS: unavailable (no anchor at the grab).";

        Transform? baseline = Rig.VRRigDriver.RigRoot;
        Vector3 reference = baseline != null ? baseline.forward : Vector3.forward;
        float here = HeldPoseMirror.HeadingDegrees(anchor.forward, anchor.up, reference);

        HandSide other = side == HandSide.Left ? HandSide.Right : HandSide.Left;
        VRHand? otherHand = VRHands.Get(other);
        Transform? otherAnchor = otherHand != null && otherHand.Rig != null ? otherHand.Rig.GrabAnchor : null;
        if (otherAnchor == null)
            return $"HEADINGS: this {side} hand pointed {here:0.#}° off the rig forward at the grab; "
                   + $"the {other} hand does not exist right now, so the pair cannot be read.";

        float there = HeldPoseMirror.HeadingDegrees(otherAnchor.forward, otherAnchor.up, reference);
        float sum = here + there;   // ~0 when the two are opposite, ~2*heading when they agree
        float diff = here - there;
        return $"HEADINGS: {side}={here:0.#}° {other}={there:0.#}° off the rig forward "
               + $"(sum={sum:0.#}°, difference={diff:0.#}°). A SUM near 0 means the hands are posed "
               + "as mirror images and the mirror reproduces the hold exactly; a DIFFERENCE near 0 "
               + "means both hands point the same way — which is what reaching for the same hex "
               + "does — and then the mirror is not a mirror at all but a wrap(2*yaw) spin.";
    }

    /// <summary>The worn hand set's left↔right <c>Anchor_Grab</c> relation, both ways of measuring
    /// it — the raw angle (which is NOT an asymmetry) and the residual against the true mirror
    /// (which would be).</summary>
    private static string AnchorPair()
    {
        VRHand? l = VRHands.Left, r = VRHands.Right;
        if (l == null || r == null || l.Rig == null || r.Rig == null)
            return "ANCHOR PAIR: unreadable (one hand is down).";

        Transform la = l.Rig.GrabAnchor, ra = r.Rig.GrabAnchor;
        if (la == null || ra == null || l.Rig.Root == null || r.Rig.Root == null)
            return "ANCHOR PAIR: unreadable (an anchor or a rig root is missing).";

        // In each hand's OWN root frame, so the live tracked pose and the mirrored seat trim
        // (VRHand.SyncVisualOffset negates the seat yaw and roll for the left hand) are both out of
        // the comparison and what is left is the prefab-authored chain.
        Quaternion lq = Quaternion.Inverse(l.Rig.Root.rotation) * la.rotation;
        Quaternion rq = Quaternion.Inverse(r.Rig.Root.rotation) * ra.rotation;
        return $"ANCHOR PAIR ({l.Rig.VisualStyle}/{r.Rig.VisualStyle}): "
               + $"anchorRawAngle={Quaternion.Angle(lq, rq):0.###}° "
               + $"anchorMirrorResidual={HeldPoseMirror.MirrorResidualDegrees(lq, rq):0.###}°. "
               + "The RAW angle is not an asymmetry — it is twice each anchor's tilt out of the "
               + "mirror plane, and reads 0.021°/1.998°/11.401° on the shipped Glove/Plate/Arcane "
               + "prefab pairs purely for that reason. The RESIDUAL is the one that would show a "
               + "broken pair, and it is ~0 on all three, so the anchors are NOT a second cause.";
    }

    /// <summary>Euler angles wrapped to ±180 — the form the [FigureGrab] dials are written in, so
    /// a tuned −133° reads back as −133 and not as 227.</summary>
    private static string FmtSigned(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return $"({Signed(e.x):0.#},{Signed(e.y):0.#},{Signed(e.z):0.#})";
    }

    private static float Signed(float degrees) => degrees > 180f ? degrees - 360f : degrees;
}
