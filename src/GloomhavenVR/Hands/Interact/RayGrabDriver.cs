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

    internal void Tick()
    {
        // Dominant hand only (the off-hand holds the fan) and only while the ray is the
        // live effective state — Ray.Active is false while THIS hand already holds a
        // grabbable (including a laser-carry in progress), so we stop ray-testing and let
        // the Grabber run the hold/release; PanelGrabHandle.Update carries the window.
        // User bug A (honest affordance): when the Grabber itself is policy-disabled,
        // ForceGrab below would refuse anyway — hovering/tinting/buzzing the bar then
        // PROMISED a grab that could never engage ("flickers and vibrates but won't
        // grab"). No grab available ⇒ no grab affordance.
        if (!_hand.Ray.Active || VRHands.Primary != _hand || !_hand.Grabber.Enabled)
        {
            ClearHover();
            return;
        }

        _hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        float scale = _hand.WorldScale;
        var ray = new Ray(origin, direction);
        float maxDist = MaxDistanceMeters * scale;

        PanelGrabHandle? best = null;
        Vector3 bestPoint = default;
        float bestDist = maxDist;

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
            ClearHover();
            return;
        }

        // A nearer game-UI hit (RayUgui ticked first this frame) wins — a click on a
        // window's own widget must never double as a drag of its bar (UI-consumed press).
        if (_hand.RayUgui.HasHit && _hand.RayUgui.HitDistance < bestDist)
        {
            ClearHover();
            return;
        }

        // A nearer solid physics hit (miniature/furniture) occludes the bar.
        PickPose pick = _hand.Ray.Current;
        if (pick.HasHit && pick.HitDistance < bestDist - OcclusionEpsilonMeters * scale)
        {
            ClearHover();
            return;
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
            ClearHover();
            return;
        }

        // Hover: tint the bar + clamp the visible beam to the hit point (mirror board laser).
        if (!ReferenceEquals(best, _hovered))
        {
            ClearHover();
            _hovered = best;
            best.OnGrabHighlight(_hand, true);
            _hand.SendHaptic(HapticPreset.HoverTick); // debounced: only on hover change
        }
        _hand.Ray.UiHitOverride = bestPoint;

        // TriggerDown → start a laser-carry grab at the captured range (release on trigger-up).
        if (_hand.TriggerDown && _hand.Grabber.Held == null)
        {
            PanelGrabHandle target = best;
            ClearHover();
            target.BeginLaserCarry(_hand, bestDist, bestPoint);
            if (!_hand.Grabber.ForceGrab(target, releaseOnTriggerUp: true))
                target.CancelLaserCarry();
        }
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
