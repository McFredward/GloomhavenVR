using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Far-ray GRAB-AT-A-DISTANCE for world panels (feature #8): the dominant hand's aim
/// ray hits a window's brass drag bar and, while the trigger is held, drags the whole
/// window at range (drop on release). This complements the near-hand grip grab
/// (<see cref="ProximityGrabber"/> + <see cref="PanelGrabHandle"/>): grip when the palm
/// is on the bar, laser when you point at it from a distance.
///
/// Mechanism (mirrors <see cref="Cards.CardsDriver"/>.UpdateBoardLaser): the drag bar is
/// a TRIGGER collider on the mod render layer, so it is invisible to the physics ray
/// (<see cref="RayInteractor"/> excludes the mod layer and ignores triggers BY DESIGN —
/// grabs route through the <see cref="VRInteractables"/> registry, not physics layers).
/// So we ray-test the registered <see cref="PanelGrabHandle"/> colliders geometrically
/// with <see cref="Collider.Raycast"/> (works on triggers, no layer coupling), pick the
/// nearest, clamp the visible beam onto it via <see cref="RayInteractor.UiHitOverride"/>
/// on hover, and on TriggerDown start a LASER-CARRY grab
/// (<see cref="PanelGrabHandle.BeginLaserCarry"/> + <see cref="ProximityGrabber.ForceGrab"/>
/// with releaseOnTriggerUp) so the window slides ALONG the ray at its captured distance
/// instead of snapping to the palm.
///
/// Priority: a nearer game-UI hit (<see cref="RayUguiDriver"/>, which ticks first) blocks
/// the grab entirely — a click on a window's widget always wins over dragging its bar. A
/// nearer physics hit (miniature/furniture) also blocks it (no grabbing through objects).
///
/// One instance per hand (created by <see cref="VRHand.Initialize"/>, ticked AFTER
/// RayUgui and BEFORE the Grabber); inert unless this is the dominant hand and its ray is
/// active. No per-frame allocations: a for-loop over the grabbable registry.
/// </summary>
internal sealed class RayGrabDriver
{
    /// <summary>
    /// Max pointing distance in meters (scale 1) — matches the ray interactor.
    ///
    /// <para>INTERNAL since 2026-08-23 because it is now also the reel's FAR BOUND:
    /// <see cref="PanelGrabHandle"/>'s stick-driven carry distance may not push a window past the
    /// distance at which THIS loop would still find its bar, or the player would have shoved a
    /// window somewhere no far-ray path in the mod can reach it again. Reading the one number that
    /// defines the reach is the only way those two can never disagree — the alternative, a second
    /// "how far may a window go" constant, is a value that drifts the first time this one is tuned.
    /// Nothing else about it changed, and it is still applied here exactly as before.</para>
    /// </summary>
    internal const float MaxDistanceMeters = 20f;

    /// <summary>A physics hit closer than the bar by more than this blocks the grab (meters, scale 1).</summary>
    private const float OcclusionEpsilonMeters = 0.005f;

    private readonly VRHand _hand;
    private PanelGrabHandle? _hovered;

    internal RayGrabDriver(VRHand hand) => _hand = hand;

    /// <summary>
    /// Falsifier readback (grip-held laser suppression): is a panel drag-bar currently
    /// hovered/tinted by this hand's beam? The LIVE field — <see cref="ClearHover"/> is what
    /// drops it, so a line built from this reports the outcome, not the intent.
    /// </summary>
    internal bool IsHovering => _hovered != null;

    internal void Tick()
    {
        // Dominant hand only (the off-hand holds the fan) and only while the ray is the
        // live effective state — Ray.Active is false while THIS hand already holds a
        // grabbable (including a laser-carry in progress), so we stop ray-testing and let
        // the Grabber run the hold/release; PanelGrabHandle.Update carries the window.
        // Ray.Active is ALSO false while this hand's GRIP is held (RayInteractor.GripSuppressed,
        // 2026-08-24): the player is in the physical fingertip-press posture, so the bar loses
        // its hover tint here and no trigger can start a new laser carry until the grip opens.
        // A carry ALREADY in flight is untouched — by then Grabber.Held != null owns it, this
        // method is not what keeps it alive, and its hold button is the trigger.
        // User bug A (honest affordance): when the Grabber itself is policy-disabled,
        // ForceGrab below would refuse anyway — hovering/tinting/buzzing the bar then
        // PROMISED a grab that could never engage ("flickers and vibrates but won't
        // grab"). No grab available ⇒ no grab affordance.
        if (!_hand.Ray.Active || VRHands.Primary != _hand || !_hand.Grabber.Enabled)
        {
            ClearHover();
            return;
        }

        if (!TryPickBar(out PanelGrabHandle? best, out Vector3 bestPoint, out float bestDist)
            || _hand.RayUgui.IsPressing
            || (_hand.RayUgui.HasHit && _hand.RayUgui.HitDistance < bestDist))
        {
            ClearHover();
            return;
        }

        // Hover: tint the bar + clamp the visible beam to the hit point (mirror board laser).
        if (!ReferenceEquals(best, _hovered))
        {
            ClearHover();
            _hovered = best;
            best!.OnGrabHighlight(_hand, true);
            _hand.SendHaptic(HapticPreset.HoverTick); // debounced: only on hover change
        }
        // LABELLED as a panel hover (ModBuild 359, RayInteractor.SetPanelUiHit): like the uGUI
        // canvas raise, this happens on MERE HOVER of a floated window's drag bar — no press. It
        // is the second of the two menu-dependent producers that were vetoing near-hand figure
        // grabs, and ProximityGrabber's near-field arbitration may now outrank it. The raise
        // itself is unchanged, so the beam still clamps to the bar and the board far-click stays
        // suppressed exactly as before.
        _hand.Ray.SetPanelUiHit(bestPoint, $"a HOVER of the window drag bar '{best!.name}' "
                                           + "(RayGrabDriver — the bar collider, no press required)");

        // TriggerDown → start a laser-carry grab at the captured range (release on trigger-up).
        if (_hand.TriggerDown && _hand.Grabber.Held == null)
        {
            // EXCLUSIVITY (ModBuild 359): the near-hand grab now outranks this driver's beam
            // clamp, so the laser carry must not start on the same pull — otherwise the window
            // would fly off on its reel in the moment the player closed his hand on a figure.
            // This is the exact defect the LOST-MENU comment above records, arriving through the
            // other door. A carry ALREADY in flight is untouched: Grabber.Held is non-null by
            // then and this branch is not what keeps it alive.
            //
            // THE HOVER TINT IS DELIBERATELY LEFT ON. The file's rule is "no grab available ⇒ no
            // grab affordance", written for a SUSTAINED unavailability (the interactor being
            // policy-off). This one is momentary and self-explaining — it lasts exactly as long
            // as the hand sits inside a figure, the figure carries its own pre-grab glow saying
            // so, and dropping and restoring the bar's tint every time the hand brushes past a
            // card is the "grab flashes" defect class.
            if (_hand.Grabber.TriggerGrabOffered)
            {
                LogCarryYielded(best!);
                return;
            }
            PanelGrabHandle target = best!;
            ClearHover();
            target.BeginLaserCarry(_hand, bestDist, bestPoint);
            if (!_hand.Grabber.ForceGrab(target, releaseOnTriggerUp: true))
                target.CancelLaserCarry();
        }
    }

    /// <summary>Read-only nearest reachable bar query, shared with uGUI before it dispatches
    /// pointer events. Trigger colliders are absent from Ray.Current; checking only in Tick
    /// let a farther widget receive pointer-down before the same pull grabbed the front bar.
    /// The existing interactor order and nearest-widget priority remain unchanged.</summary>
    internal bool TryPickBar(out PanelGrabHandle? best, out Vector3 bestPoint, out float bestDist)
    {
        best = null; bestPoint = default; bestDist = float.PositiveInfinity;
        if (!_hand.Ray.Active || VRHands.Primary != _hand || !_hand.Grabber.Enabled)
            return false;
        _hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        float scale = _hand.WorldScale;
        var ray = new Ray(origin, direction);
        float maxDist = MaxDistanceMeters * scale;

        bestDist = maxDist;

        var entries = VRInteractables.Grabbables;
        for (int i = 0; i < entries.Count; i++)
        {
            // Only panel/modal drag handles are laser-draggable; cards etc. are not.
            if (entries[i].Target is not PanelGrabHandle handle || !handle.CanGrab)
                continue;
            // LOST-MENU FIX: when the handle exposes a dedicated BAR collider (floated modal
            // windows), the laser tests ONLY that narrow visible drag-bar strip. The wider
            // registered grab ZONE stays palm-only (ProximityGrabber) — a floated menu sits
            // between the user and the board, and ray-testing its generous zone made EVERY
            // trigger aimed at the cards/board start a laser-carry of the menu instead. The
            // hover beam-clamp (UiHitOverride below) follows the same collider, so the beam
            // only latches onto the visible bar too.
            Collider col = handle.BarCollider != null ? handle.BarCollider : entries[i].Collider;
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                continue;
            if (col.Raycast(ray, out RaycastHit hit, bestDist))
            {
                best = handle;
                bestPoint = hit.point;
                bestDist = hit.distance;
            }
        }

        if (best == null)
        {
            return false;
        }

        // A nearer solid physics hit (miniature/furniture) occludes the bar.
        PickPose pick = _hand.Ray.Current;
        if (pick.HasHit && pick.HitDistance < bestDist - OcclusionEpsilonMeters * scale)
        {
            return false;
        }

        // Solid occlusion (fan cards AND the control board, 2026-08-04): a solid mod-owned
        // surface blocks the grab bar BEHIND it — pointing THROUGH the hand of cards or the
        // board must not drag a window's bar visible past them. Uses the ray's precomputed
        // nearest solid distance (RayInteractor.SolidOccluderDistance).
        if (_hand.Ray.SolidOccluderDistance < bestDist - OcclusionEpsilonMeters * scale)
        {
            // [Optimize] LeanLogStrings: skip the per-frame string build when the note is throttled.
            if (RayInteractor.WantFanOcclusionNote)
                _hand.Ray.NoteFanOcclusion($"panel grab bar '{best.name}'", bestDist);
            return false;
        }

        return true;
    }

    /// <summary>Next unscaled time <see cref="LogCarryYielded"/> may print, and what it swallowed.</summary>
    private float _nextCarryYieldLogAt;
    private int _carryYieldsSinceLastLog;

    /// <summary>
    /// The cost line for the ModBuild 359 arbitration on this driver: a laser-carry this beam
    /// would have started was handed to the hand instead. Throttled to one per second per hand,
    /// carrying the count it swallowed.
    /// </summary>
    private void LogCarryYielded(PanelGrabHandle bar)
    {
        _carryYieldsSinceLastLog++;
        if (Time.unscaledTime < _nextCarryYieldLogAt)
            return;
        _nextCarryYieldLogAt = Time.unscaledTime + 1f;
        int swallowed = _carryYieldsSinceLastLog - 1;
        _carryYieldsSinceLastLog = 0;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        Core.VRLog.Note("Interact",
            $"{_hand.Side} laser-carry YIELDED to the hand — the beam was on the drag bar '{bar.name}', "
            + "but this hand has an elected, lit near-field grab offer within the palm reach, and a "
            + "hand inside an object outranks a beam pointing at a window. No carry was started; the "
            + "trigger went to the grab."
            + (swallowed > 0
                ? $" {swallowed} further yield(s) in the last second are not printed."
                : ""));
    }

    /// <summary>Drop any hover tint (hand lost, ray off, dominance switch, target gone).</summary>
    internal void Cancel() => ClearHover();

    private void ClearHover()
    {
        if (_hovered != null)
        {
            _hovered.OnGrabHighlight(_hand, false);
            _hovered = null;
        }
    }
}
