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

    /// <summary>
    /// True: this grabbable is ALWAYS taken with the GRIP button, ignoring the
    /// <c>[Cards] GrabButton</c> config (hardware test #27: boards/world panels grip-
    /// grab, more intuitive than the Demeo trigger). False: obey the config — Trigger
    /// (Demeo default) or Grip — like cards do. net472 has no default-interface-method
    /// support, so every <see cref="IGrabbable"/> implementer states this explicitly
    /// (the <see cref="GrabbableBehaviour"/> base defaults it to false for cards).
    /// </summary>
    bool GrabWithGrip { get; }

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

/// <summary>
/// Optional companion to <see cref="IGrabbable"/> (P7 additive, hardware test #10):
/// per-hand gate. The <see cref="ProximityGrabber"/> skips this target entirely
/// (no highlight, no grab, no haptic) for hands the filter rejects. The card fan uses
/// this to exclude the fan-owning hand — its palm sits INSIDE the fan, and its own
/// proximity hover made two cards flip-flop highlights forever without user input.
/// </summary>
internal interface IGrabbableHandFilter
{
    /// <summary>Return false to make this target invisible to <paramref name="hand"/>.</summary>
    bool AllowsHand(VRHand hand);
}

/// <summary>
/// Optional MARKER companion to <see cref="IGrabbable"/> (additive, hardware MP test 2026-08,
/// user requirement (b): "Figuren sollen — wie die Karten — nur mit dem Trigger aufgenommen
/// werden können"). A target carrying this marker is picked up by the TRIGGER edge ONLY:
/// <see cref="ProximityGrabber"/> withholds its universal "a closing fist (grip) always grabs
/// the highlighted candidate" fallback for it, so a grip squeeze near the target — the most
/// common accidental gesture over a crowded board — never starts a hold. Board figures
/// (<c>Board.FigureGrab.FigureGrabbable</c>) carry it; cards deliberately do NOT (the grip
/// fallback exists for them — the un-grabbable placed pick card of 2026-08-04). Meaningless on
/// a <see cref="IGrabbable.GrabWithGrip"/> target (that branch never consults it).
/// </summary>
internal interface ITriggerOnlyGrabbable
{
}
