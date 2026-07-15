using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Something a hand can grab (FROZEN Phase-2 API).
///
/// Implement on a MonoBehaviour and register it together with a Collider via
/// <see cref="VRInteractables.RegisterGrabbable"/> — or derive from
/// <see cref="GrabbableBehaviour"/> which also handles snap-to-hand parenting.
///
/// Flow: when the palm comes within reach and <see cref="CanGrab"/> is true the object
/// is highlighted (see <see cref="IGrabHighlight"/>); a grip press calls
/// <see cref="OnGrab"/>; releasing the grip calls <see cref="OnRelease"/> with the
/// measured palm velocity (world units/s — already diorama-scaled).
/// The grabber does NOT reparent anything itself; the grabbable decides what
/// "being held" means (typically: snap to <c>hand.Rig.GrabAnchor</c>).
/// </summary>
internal interface IGrabbable
{
    /// <summary>Gate: return false to be ignored (e.g. card not selectable right now).</summary>
    bool CanGrab { get; }

    /// <summary>The hand closed on the object.</summary>
    void OnGrab(VRHand hand);

    /// <summary>The hand released the object. <paramref name="velocity"/> is the palm velocity (world units/s).</summary>
    void OnRelease(VRHand hand, Vector3 velocity);
}

/// <summary>
/// Optional companion to <see cref="IGrabbable"/>: hover highlight hook
/// (emissive pulse etc.). Called with true when this becomes the grab candidate of a
/// hand, false when it stops being one.
/// </summary>
internal interface IGrabHighlight
{
    void OnGrabHighlight(VRHand hand, bool highlighted);
}
