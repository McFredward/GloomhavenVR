using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// HELD-FIGURE STRETCH — resize the mini in one hand by pinch-dragging it with the OTHER hand.
///
/// <para>USER REQUEST (2026-08-11, verbatim): "Ich möchte, dass die Größe der Figur in der Hand
/// änderbar ist. Dabei stelle ich mir vor, dass ich mit der anderen Hand zu der Figur gehe und
/// dann Trigger gedrückt halte und nach innen oder außen schiebe (nach außen heißt größer, nach
/// innen kleiner) und somit die Größe der Figur skaliert."</para>
///
/// <para>THE MATHEMATICS ARE RATIO-BASED, NOT INCREMENTAL. At trigger-down the distance from the
/// gesture hand's pinch point to the held mini's CENTRE is latched (d0), together with the hold's
/// current stretch factor (s0); every frame the trigger stays held the target factor is
/// s0 × (d / d0), clamped to [FigureGrab] StretchScaleMin/Max. A ratio makes the gesture
/// reversible inside one hold (slide back in and the figure is exactly where it started) and
/// proportional at every size — the same hand travel always multiplies by the same amount, which
/// an additive mapping cannot do. Distances are measured in REAL metres at the hand
/// (world ÷ rig scale), so a diorama zoom mid-gesture cannot masquerade as hand motion, and to
/// the mini's TRANSFORM position rather than its collider surface — a surface point moves WITH
/// the scale being written and would feed the output back into the input. Both d and d0 are
/// floored (<see cref="MinGestureDistanceRealMeters"/>) so a pinch started ON the mini's centre
/// cannot divide by a millimetre and explode.</para>
///
/// <para>CONTINUITY BY CONSTRUCTION, no pops anywhere: at trigger-down d == d0, so the first
/// frame's target IS the current factor; every later frame moves through a light exponential
/// smoothing (<see cref="SmoothSharpness"/> — a ~33 ms time constant, insurance against raw
/// tracking jitter at well under the ~100 ms lag-perception threshold); trigger-up simply stops
/// writing, so the factor commits at the value on screen. Release is untouched: the glide reads
/// the transform's live scale as its start, so a stretched mini eases home over the same 0.28 s
/// curve as any other.</para>
///
/// <para>WHO OWNS THE TRIGGER INSIDE THE CAPTURE ZONE — the collision story, resolved from
/// source, deterministic at every point:</para>
/// <list type="bullet">
///   <item>The HELD figure itself can never be steal-grabbed: <c>FigureGrabbable.CanGrab</c> is
///   false while <c>_holder != null</c>, and both the <c>ProximityGrabber</c> highlight walk and
///   <c>FigureGrabDriver.TryLaserGrab</c> gate on CanGrab.</item>
///   <item>OTHER board figures behind/near the held mini: while a hand is captured (or
///   mid-gesture) <c>FigureGrabDriver.SelectByOffsetAnchor</c> elects NOBODY for it and vetoes
///   every adopted figure (<c>ApplySuppression(hand, null)</c>), and <c>TryLaserGrab</c> skips
///   the hand outright — so neither the near pick nor the far pluck can fire. The veto is read by
///   the grabber one frame in arrears (its standing property): on the single frame a hand ENTERS
///   the zone with the trigger already going down, an ALREADY-HIGHLIGHTED board figure may still
///   win — which is the affordance the player is literally looking at, so the highlight never
///   lies.</item>
///   <item>CARDS: a hand hovering a card (<c>Grabber.Highlighted</c> is a card) does NOT capture —
///   the card's visible highlight keeps its promise and the trigger grabs it, exactly as outside
///   the zone. Once captured, the beam is clamped to the mini (<c>Ray.UiHitOverride</c>), which
///   is the same fresh-UI-hit signal every trigger consumer already defers to
///   (<c>ProximityGrabber.Tick</c>'s <c>!HasFreshUiHit</c> arbitration, the board far-click) —
///   so inside the zone the gesture owns the trigger and nothing else can double-fire on it. The
///   clamp deliberately does NOT touch the driver's own clamp-frame bookkeeping, so
///   <c>TryLaserGrab</c> reads it as FOREIGN and defers too.</item>
///   <item>The BUSY gate (<see cref="FigureBusy"/>) is untouched: it guards GRAB edges (CanGrab /
///   AllowsHand) and this gesture never grabs — it writes a presentation-only factor on a figure
///   that is already legitimately held. If the figure becomes busy mid-gesture the existing
///   auto-release restores it, the hold ends, and the gesture ends with it on the same
///   frame.</item>
/// </list>
///
/// <para>MULTIPLAYER: the factor rides <c>NetProtocol.ExtIdHeldStretch</c> (record 30) — a manual
/// stretch is the one component of the held size a peer cannot derive (see
/// <c>NetFigures.EaseSlot</c>, which already reconstructs boardSize × zoom ratio from data it
/// has). Nothing here talks to Net/ directly: the wire samples <see cref="FigureGrabbable.Stretch"/>
/// on its own cadence, so offline this whole feature is a strict local no-op.</para>
///
/// <para>NO EXTRA FEEDBACK, deliberately: the figure visibly tracking the hand IS the feedback,
/// the same argument as the held pose itself. A sound or glow would announce a mode where the
/// player already sees the effect of the mode.</para>
/// </summary>
internal static class FigureStretch
{
    /// <summary>Exponential smoothing sharpness (1/s) for the factor write — time constant ~33 ms:
    /// imperceptible as lag, enough to eat single-frame tracking jitter in the distance ratio.</summary>
    private const float SmoothSharpness = 30f;

    /// <summary>Floor on both gesture distances, in REAL metres at the hand. 10 mm is the same
    /// order as the steadiness bound the pick radius states (<see cref="FigureGrabConfig.PickRadiusMinMm"/>):
    /// below it the ratio d/d0 is tracking noise, and a d0 of near zero would make the first
    /// centimetre of travel a ×10.</summary>
    private const float MinGestureDistanceRealMeters = 0.01f;

    private sealed class HandState
    {
        /// <summary>This hand is inside the capture zone of the OTHER hand's held mini and free to
        /// start the gesture (pre-trigger). Recomputed every tick.</summary>
        public bool Captured;

        /// <summary>The gesture is live: trigger held, factor being written.</summary>
        public bool Active;

        /// <summary>The figure being stretched (only meaningful while <see cref="Active"/>).</summary>
        public FigureGrabbable? Target;

        /// <summary>Pinch-to-centre distance at trigger-down, real metres, floored.</summary>
        public float StartDistReal;

        /// <summary>The hold's stretch factor at trigger-down — the s0 the ratio multiplies.</summary>
        public float BaseFactor;
    }

    private static readonly HandState[] Hands = { new HandState(), new HandState() };

    /// <summary>True while <paramref name="side"/> is captured by (or actively stretching) the
    /// other hand's held mini — the driver's gate: an engaged hand elects no figure and fires no
    /// laser pluck. See <c>FigureGrabDriver.SelectByOffsetAnchor</c> / <c>TryLaserGrab</c>.</summary>
    internal static bool Engaged(HandSide side)
    {
        HandState st = Hands[(int)side];
        return st.Captured || st.Active;
    }

    /// <summary>Drop all gesture state (driver teardown / config-gate ReleaseAll). Committed
    /// factors live on the grabbables and are not touched — a mini still held keeps its size.</summary>
    internal static void Clear()
    {
        for (int i = 0; i < Hands.Length; i++)
        {
            Hands[i].Captured = false;
            Hands[i].Active = false;
            Hands[i].Target = null;
        }
    }

    /// <summary>Per-frame gesture tick. Called from <c>FigureGrabDriver.Update</c> BEFORE the
    /// election (<c>FigureGrab.OffsetAnchorSelect</c>) so the engagement gate is fresh for the
    /// same frame's veto, and before the laser pluck for the same reason.</summary>
    internal static void Tick()
    {
        TickHand(VRHands.Left);
        TickHand(VRHands.Right);
    }

    private static void TickHand(VRHand? hand)
    {
        if (hand == null)
            return;
        HandState st = Hands[(int)hand.Side];

        // A hand that is not usable ends everything it was doing. The factor stays where the last
        // written frame left it — a tracking dropout commits, exactly like a trigger-up, because
        // un-writing a size the player watched happen would itself be a pop.
        if (!hand.HasPose)
        {
            EndGesture(hand, st, "hand lost tracking");
            st.Captured = false;
            return;
        }

        if (st.Active)
        {
            TickActive(hand, st);
            return;
        }

        // ---- capture (pre-trigger) -----------------------------------------------------------
        st.Captured = false;
        if (hand.Grabber.Held != null)
            return; // a full hand cannot gesture (this also covers "one figure per hand" holds)

        // A hovered CARD keeps its trigger — its highlight is a promise the player is looking at.
        // A hovered FIGURE does not block: the capture veto clears that highlight one frame later
        // (see the class doc's collision story).
        if (hand.Grabber.Highlighted != null && hand.Grabber.Highlighted is not FigureGrabbable)
            return;

        FigureGrabbable? target = FigureGrabbable.HeldBy(Other(hand.Side));
        if (target == null || !target.TryGetHeldCenter(out Vector3 center))
            return;

        float distReal = RealDistance(hand, center);
        if (distReal > FigureGrabConfig.StretchReachRealMeters)
            return;

        st.Captured = true;

        // Own the trigger inside the zone: clamp the beam to the mini. This raises the SAME
        // fresh-UI-hit signal a fan card raises, so the proximity trigger-grab and the board
        // far-click defer to us — and because the driver's own clamp-frame bookkeeping is NOT
        // updated here, TryLaserGrab reads the hit as foreign and defers as well (belt to its
        // explicit Engaged() early-out).
        hand.Ray.UiHitOverride = center;

        if (hand.TriggerDown)
        {
            st.Active = true;
            st.Target = target;
            st.StartDistReal = Mathf.Max(distReal, MinGestureDistanceRealMeters);
            st.BaseFactor = target.Stretch;
            VRLog.Info("FigureGrab",
                $"{hand.Side} STRETCH engaged on {target.Label}: start {st.StartDistReal * 1000f:F0} mm "
                + $"real from the mini's centre, base factor {st.BaseFactor:0.###} — outward grows, "
                + $"inward shrinks, clamp [{FigureGrabConfig.StretchScaleMinValue:0.##} "
                + $".. {FigureGrabConfig.StretchScaleMaxValue:0.##}]. Committed at trigger-up; "
                + "this hold only, release still glides home to board size.");
        }
    }

    private static void TickActive(VRHand hand, HandState st)
    {
        FigureGrabbable? target = st.Target;

        // The hold under the gesture can end at any time (holder released, busy-gate auto-release,
        // authoritative move, teardown) and a fist can still grip-grab a card mid-gesture. Any of
        // those ends the gesture; the factor stays committed on the grabbable (irrelevant if the
        // hold ended — the next grab resets it to 1).
        if (target == null || !target.IsHeld || !target.TryGetHeldCenter(out Vector3 center))
        {
            EndGesture(hand, st, "the hold under the gesture ended");
            return;
        }
        if (hand.Grabber.Held != null)
        {
            EndGesture(hand, st, "the gesture hand grabbed something");
            return;
        }
        if (!hand.TriggerPressed)
        {
            EndGesture(hand, st, "trigger released — factor committed");
            return;
        }

        // Keep owning the trigger while the gesture runs — the drag may leave the capture radius
        // (moving OUT is the growing half of the gesture) and must keep the beam clamped so a
        // trigger re-press race can never fall through to a board click mid-hold.
        hand.Ray.UiHitOverride = center;
        st.Captured = true;

        float distReal = Mathf.Max(RealDistance(hand, center), MinGestureDistanceRealMeters);
        float min = FigureGrabConfig.StretchScaleMinValue;
        float max = FigureGrabConfig.StretchScaleMaxValue;
        float raw = st.BaseFactor * (distReal / st.StartDistReal);
        float clamped = Mathf.Clamp(raw, min, max);

        // Exponential smoothing toward the clamped target. Unscaled time: the gesture is a human
        // hand, not game time, and must not freeze with a pause menu.
        float k = 1f - Mathf.Exp(-SmoothSharpness * Mathf.Max(Time.unscaledDeltaTime, 0f));
        target.SetStretch(Mathf.Lerp(target.Stretch, clamped, k));
    }

    private static void EndGesture(VRHand hand, HandState st, string why)
    {
        if (!st.Active)
            return;
        st.Active = false;
        FigureGrabbable? target = st.Target;
        st.Target = null;
        VRLog.Info("FigureGrab",
            $"{hand.Side} STRETCH ended ({why}) — "
            + (target != null ? $"{target.Label} at factor {target.Stretch:0.###}." : "target gone."));
    }

    /// <summary>Pinch-point-to-centre distance in REAL metres at the hand (world ÷ rig scale) —
    /// the same pinch point the pick election measures with, so the two dials read alike.</summary>
    private static float RealDistance(VRHand hand, Vector3 centerWorld)
    {
        Vector3 pinch = hand.Rig.GrabAnchor.TransformPoint(FigureGrabConfig.HeldOffsetFor(hand.Side));
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        return Vector3.Distance(pinch, centerWorld) / scale;
    }

    private static HandSide Other(HandSide side)
        => side == HandSide.Left ? HandSide.Right : HandSide.Left;
}
