using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// READING A SHARED WINDOW'S GRAB FRAME for the wire — the one expression, shared by the two
/// records that publish one (<see cref="RemoteStorySync"/>'s story window, record 19, and
/// <see cref="RemoteMapStory"/>'s map story / quest windows, record 21).
///
/// <para>WHY IT IS ITS OWN FILE. Both records ship the same fact — where a grabbable modal's frame
/// stands and how big its owner has pulled it — and both had a byte-identical private copy of this
/// method (the 2026-09 refactor's duplication census found them as one 13-line group). They are
/// not a local↔remote MIRROR PAIR, which is the case this project deliberately keeps split: they
/// are two callers of one sender-side reading, and a difference between them could only ever be a
/// bug. The single copy also means the clamp below cannot come back in two versions.</para>
/// </summary>
internal static class SharedWindowFrame
{
    /// <summary>
    /// The window's world pose and its owner's size factor, or false when it is not currently a
    /// grabbable, revealed float (nothing to publish, and the caller writes no record).
    ///
    /// <para>ModBuild 450 — THE PUBLISHED FACTOR IS THE WIRE'S OWN VALUE, not a float near it.
    /// <see cref="SharedWindowSizeLaw.SharedGrabFactor"/> is <c>Decode(Encode(x))</c> against the
    /// very codec these records use, so this clamp IS the wire's window by construction and can
    /// never be a different window from the one the size encoder enforces. That was the last place
    /// a shared window's size could differ between the puller and every follower (the puller kept
    /// the unrounded pinch value), and it is also the answer to "what if one client's clamp
    /// changes": there is only one clamp left, and it is the wire's.</para>
    /// </summary>
    internal static bool TryRead(GrabbableModal grab, out Vector3 pos, out Quaternion rot,
                                 out float size)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        size = 1f;
        var owner = (IPanelGrabOwner)grab;
        if (!owner.GrabVisible)
            return false;
        Transform? frame = owner.GrabRoot;
        if (frame == null)
            return false;
        pos = frame.position;
        rot = frame.rotation;
        size = SharedWindowSizeLaw.SharedGrabFactor(frame.localScale.x);
        return true;
    }
}
