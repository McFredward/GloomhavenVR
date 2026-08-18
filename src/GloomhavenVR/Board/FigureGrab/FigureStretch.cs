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
/// s0 × (d / d0), clamped into the hold's factor envelope (next paragraph). A ratio makes the
/// gesture reversible inside one hold (slide back in and the figure is exactly where it started)
/// and proportional at every size — the same hand travel always multiplies by the same amount,
/// which an additive mapping cannot do. Distances are measured in REAL metres at the hand
/// (world ÷ rig scale), so a diorama zoom mid-gesture cannot masquerade as hand motion, and to
/// the mini's TRANSFORM position rather than its collider surface — a surface point moves WITH
/// the scale being written and would feed the output back into the input. Both d and d0 are
/// floored (<see cref="MinGestureDistanceRealMeters"/>) so a pinch started ON the mini's centre
/// cannot divide by a millimetre and explode.</para>
///
/// <para>THE CLAMP IS TOTAL-BASED, NOT A BARE FACTOR CLAMP (2026-08-11 size-bounds round).
/// [FigureGrab] StretchScaleMin/Max bound the figure's TOTAL held size relative to its
/// board-home size at the DEFAULT diorama zoom, and the grab-time latch already carries the zoom
/// the player grabbed at — so clamping the per-hold factor against Min/Max in isolation let a
/// figure grabbed at 2× total reach 6× total. The gesture's ratio mathematics are untouched;
/// only the clamp bounds changed: <c>FigureGrabbable.GetStretchFactorBounds</c> converts the
/// total bounds to this hold's factor envelope (Min/latchRatio .. Max/latchRatio), re-read every
/// frame so a live dial edit governs the next frame. With [FigureGrab] StretchLimits OFF the
/// envelope collapses to the technical floor alone (<c>FigureGrabConfig.StretchHardFloor</c> —
/// positive-and-finite, no size opinion), which is the user's requested "Ober und Untergrenzen
/// ganz abschalten". The grab-time half of the same bound (an over/under-sized latch entering
/// the hand AT the bound) lives in <c>FigureGrabbable.ApplyGrabTimeStretchClamp</c>.</para>
///
/// <para>THE CAPTURE ZONE IS SURFACE-BASED AND SCALES WITH THE FIGURE — hardware test report
/// (2026-08-11, verbatim): "Groß ziehen kann ich ohne Probleme aber wieder klein ziehen nicht,
/// weil der punkt der aktzeptiert wird mich mitskalliert. Ich will an jedem Punkt der Figur
/// greifen können (trigger) um sie größer oder kleine zu ziehen - wenn ich sie schon in der Hand
/// skalliert habe soll der Punkt ab dem ich ich sie greifen kann mitskallieren!" The first ship
/// measured capture to the mini's CENTRE, so a mini stretched to 3× had its whole visible body
/// OUTSIDE the 80 mm zone and shrinking meant reaching inside the model. Now
/// <see cref="CaptureDistanceReal"/> takes the distance from the pinch point to the NEAREST
/// point of the mini's visible body — min over its renderers' world AABBs
/// (<c>Bounds.ClosestPoint</c>, renderers cached per hold by
/// <see cref="FigureGrabbable.HeldRenderers"/>) — and captures when that is within
/// [FigureGrab] StretchReachMillimeters. Closest-point-on-bounds rather than
/// centre-plus-radius-sphere because a mini is tall and thin: a bounding SPHERE of a 3×-stretched
/// mini would push the zone half a body-height out SIDEWAYS where there is nothing to point at,
/// while the AABB test keeps the reach a true "distance from the visible surface" everywhere,
/// which is literally "an jedem Punkt der Figur". Renderer world bounds grow with the applied
/// stretch, so the zone scales with the figure by construction — no second dial. The distance is
/// converted to REAL metres exactly like the gesture distances (÷ hand rig scale), so a diorama
/// zoom cannot widen or starve the zone. IMPORTANT ASYMMETRY, deliberate: only the CAPTURE test
/// is surface-based; d0/d stay measured to the CENTRE, because a surface point moves with the
/// scale being written and would feed the output back into the input (see the previous
/// paragraph). And the capture test is an ENTRY test only — once the trigger latches, the gesture
/// runs until trigger-up regardless of distance (<see cref="TickActive"/> re-checks nothing but
/// the hold and the trigger), so leaving the zone outward IS the growing half of the gesture.
/// Safety: a renderer whose bounds imply a figure radius beyond
/// <see cref="MaxFigureRadiusRealMeters"/> (broken skinned-mesh bounds, a stray particle system)
/// is excluded from the test with a warning — a bounds bug must degrade to the old centre test,
/// never capture the whole room.</para>
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
/// <para>FEEDBACK: no VISUAL feedback, deliberately — the figure visibly tracking the hand IS
/// the feedback, the same argument as the held pose itself; a glow would announce a mode where
/// the player already sees the effect of the mode. HAPTICS are a different story, by user ruling
/// (hardware test report 2026-08-11, verbatim): "ich möchte ein vibrantionsfeedback in der freien
/// hand um anzuzeigen das sie jetzt genug dran ist zu ziehen. (Das Gleiche Vbrations-Feedback wie
/// wenn ich eine Figur drüber hovere)." — the pinch point is invisible and "close enough" has no
/// visual until the trigger is already down, so the zone edge needs announcing. The free hand
/// gets EXACTLY the figure-hover pulse (<c>HapticPreset.HoverTick</c> via
/// <c>VRHand.SendHaptic</c>, the same call <c>ProximityGrabber</c> fires when a figure becomes
/// Highlighted, including its rate limit), EDGE-TRIGGERED on <see cref="HandState.Captured"/>
/// false→true: once on entering the zone, silent while inside, re-armed by leaving. Refused
/// captures (full hand, hovered card) never pulse — they never set Captured.</para>
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

    /// <summary>Sanity ceiling on the figure radius the capture bounds test may derive, in REAL
    /// metres: centre-to-bounds-centre offset plus the bounds half-diagonal. StretchScaleMax on
    /// the largest boss mini stays well under this; only broken renderer bounds (a skinned mesh
    /// with an unbaked bounding box, a world-sized particle system) can exceed it. Such a
    /// renderer is EXCLUDED from the test — with a warning, once per figure — so a bounds bug
    /// degrades to the centre-distance fallback instead of capturing the entire room.</summary>
    private const float MaxFigureRadiusRealMeters = 0.5f;

    /// <summary>The figure last warned about by the bounds sanity clamp — the once-per-figure
    /// throttle for <see cref="CaptureDistanceReal"/>'s warning (the test runs every frame).</summary>
    private static FigureGrabbable? _boundsWarnTarget;

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
        _boundsWarnTarget = null; // do not pin a torn-down grabbable just to throttle a warning
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
        // Last tick's zone membership, read BEFORE the recompute — the edge the haptic pulse
        // fires on. Note this stays TRUE across a whole gesture (TickActive asserts Captured
        // every frame), so a trigger-up inside the zone does NOT re-pulse; only a genuine
        // leave-and-return does.
        bool wasCaptured = st.Captured;
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

        // Surface-based, so the zone scales with the applied stretch (class doc, capture-zone
        // paragraph). The gesture's own d0 below stays CENTRE-based on purpose.
        if (CaptureDistanceReal(hand, target, center) > FigureGrabConfig.StretchReachRealMeters)
            return;

        st.Captured = true;

        // The user's requested "close enough to pull" announcement: the figure-hover pulse, on
        // the zone-entry edge only (see the class doc's FEEDBACK paragraph).
        if (!wasCaptured)
            hand.SendHaptic(HapticPreset.HoverTick);

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
            // d0 is CENTRE distance, NOT the surface distance the capture used — the ratio's
            // reference must not move with the scale it drives (class doc, mathematics paragraph).
            st.StartDistReal = Mathf.Max(RealDistance(hand, center), MinGestureDistanceRealMeters);
            st.BaseFactor = target.Stretch;
            target.GetStretchFactorBounds(out float fMin, out float fMax);
            string clampText = FigureGrabConfig.StretchLimitsEnabled
                ? $"factor clamp [{fMin:0.###} .. {fMax:0.###}] (total bound "
                  + $"[{FigureGrabConfig.StretchScaleMinValue:0.##} "
                  + $".. {FigureGrabConfig.StretchScaleMaxValue:0.##}]× of default-zoom size, "
                  + "rebased to this hold's latch)"
                : $"NO clamp (StretchLimits off; technical floor {fMin:0.##}× only)";
            VRLog.Info("FigureGrab",
                $"{hand.Side} STRETCH engaged on {target.Label}: start {st.StartDistReal * 1000f:F0} mm "
                + $"real from the mini's centre, base factor {st.BaseFactor:0.###} — outward grows, "
                + $"inward shrinks, {clampText}. Committed at trigger-up; "
                + "this hold only, release still glides home to board size.");
        }
    }

    private static void TickActive(VRHand hand, HandState st)
    {
        FigureGrabbable? target = st.Target;

        // The hold under the gesture can end at any time (holder released, busy-gate auto-release,
        // authoritative move, teardown), and the gesture hand can still fill itself mid-gesture —
        // not with a card or a figure (both are trigger-only and the trigger is ours here) but via
        // the grip fall-through to a tray bar / panel (ProximityGrabber.TryGripFallThrough). Any of
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
        // Per-hold factor bounds, TOTAL-based (see the class doc's mathematics paragraph and
        // FigureGrabbable._latchTotalRatio): the hold converts [FigureGrab] StretchScaleMin/Max
        // from total-size bounds into the factor envelope of THIS latch, and answers "floor
        // only" while StretchLimits is off. Asked per frame on purpose — a live dial change
        // governs the very next frame, without ever re-clamping a resting size.
        target.GetStretchFactorBounds(out float min, out float max);
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

    /// <summary>
    /// The CAPTURE test's distance, in REAL metres at the hand: from the pinch point to the
    /// NEAREST point of the held mini's visible body — min over its renderers' world-space AABBs
    /// (<c>Bounds.ClosestPoint</c>; zero when the pinch is inside a box, so the inside of the
    /// model always captures). Renderer bounds grow with the applied stretch, which is exactly
    /// what makes the zone scale with the figure. Renderers whose bounds imply a figure radius
    /// beyond <see cref="MaxFigureRadiusRealMeters"/> are excluded (warned once per figure);
    /// disabled renderers do not count as visible body. When no usable renderer remains, falls
    /// back to the centre distance — the pre-fix behaviour, never a wider zone.
    /// </summary>
    private static float CaptureDistanceReal(VRHand hand, FigureGrabbable target, Vector3 centerWorld)
    {
        Vector3 pinch = PinchPoint(hand);
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        float best = float.PositiveInfinity;
        Renderer[]? renderers = target.HeldRenderers();
        if (renderers != null)
        {
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled)
                    continue;
                Bounds b = r.bounds;
                float impliedRadiusReal =
                    (Vector3.Distance(centerWorld, b.center) + b.extents.magnitude) / scale;
                if (impliedRadiusReal > MaxFigureRadiusRealMeters)
                {
                    if (!ReferenceEquals(_boundsWarnTarget, target))
                    {
                        _boundsWarnTarget = target;
                        VRLog.Warn("FigureGrab",
                            $"STRETCH capture bounds clamped on {target.Label}: renderer "
                            + $"'{r.name}' implies a figure radius of {impliedRadiusReal:0.##} m "
                            + $"real (> {MaxFigureRadiusRealMeters:0.##} m sanity ceiling) — "
                            + "excluded from the capture test so it cannot swallow the room. "
                            + "Likely broken skinned-mesh bounds or a stray particle system.");
                    }
                    continue;
                }
                float d = Vector3.Distance(pinch, b.ClosestPoint(pinch)) / scale;
                if (d < best)
                    best = d;
            }
        }
        return float.IsPositiveInfinity(best) ? Vector3.Distance(pinch, centerWorld) / scale : best;
    }

    /// <summary>Pinch-point-to-centre distance in REAL metres at the hand (world ÷ rig scale) —
    /// the same pinch point the pick election measures with, so the two dials read alike. This is
    /// the GESTURE's distance (d0/d); the capture zone uses <see cref="CaptureDistanceReal"/>.</summary>
    private static float RealDistance(VRHand hand, Vector3 centerWorld)
    {
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        return Vector3.Distance(PinchPoint(hand), centerWorld) / scale;
    }

    /// <summary>The gesture hand's pinch point in WORLD space — the held-offset point under the
    /// grab anchor, the same point the pick election measures with.</summary>
    private static Vector3 PinchPoint(VRHand hand)
        => hand.Rig.GrabAnchor.TransformPoint(FigureGrabConfig.HeldOffsetFor(hand.Side));

    private static HandSide Other(HandSide side)
        => side == HandSide.Left ? HandSide.Right : HandSide.Left;
}
