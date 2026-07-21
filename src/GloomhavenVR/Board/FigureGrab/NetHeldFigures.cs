using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Receive-side twin of <see cref="HeldFigures"/>: the set of board figures a REMOTE player is
/// currently holding (cosmetic pickup sync). While an <see cref="ActorBehaviour"/> is in this set
/// the game's per-frame POSITION writers are suppressed for it exactly as for a locally-held
/// figure (<see cref="ActorBehaviour_HeldTransform_Patch"/> gates on both sets), so
/// <c>Net/NetFigures.Tick</c> can drive the mini toward the pose the remote grabber sends. On
/// release the actor leaves the set and the game's own Update snaps it back to its authoritative
/// board cell — no manual return math (identical contract to <see cref="HeldFigures"/>).
///
/// Membership is OWNED by <c>Net/NetFigures</c>, which rebuilds it from its per-player held-figure
/// records whenever a remote grab/release/switch arrives. Strict no-op offline: nothing is ever
/// added unless a modded VR peer reports a held figure.
/// </summary>
internal static class NetHeldFigures
{
    private static readonly HashSet<ActorBehaviour> Held = new();

    /// <summary>Patch gate: true when a remote player holds this actor (skip the game's writes).</summary>
    internal static bool Owns(ActorBehaviour actor) => actor != null && Held.Contains(actor);

    internal static void Add(ActorBehaviour actor)
    {
        if (actor != null)
            Held.Add(actor);
    }

    internal static void Remove(ActorBehaviour actor)
    {
        if (actor != null)
            Held.Remove(actor);
    }

    internal static int Count => Held.Count;

    /// <summary>Enumerate every remotely-held actor (task #2 — selection-ring suppression). The set
    /// is only mutated by <c>Net/NetFigures</c> record updates, never during this enumeration.</summary>
    internal static IEnumerable<ActorBehaviour> All => Held;

    /// <summary>
    /// Issue C (remote): freeze the animation-driven translation on every REMOTELY-held figure,
    /// identical to <see cref="HeldFigures.PinAnimatedRoots"/>. <c>Net/NetFigures.Tick</c> eases the
    /// ROOT toward the synced pose, but the game's <c>ApplyMotion</c> re-zero of
    /// <c>m_AnimatedGameObject.localPosition</c> is suppressed for these actors too, so a figure a
    /// peer grabbed mid-walk would otherwise drift out of the synced pose. Called from the same
    /// LateUpdate, after the Animator, so both hands' held figures stay pinned.
    /// </summary>
    internal static void PinAnimatedRoots()
    {
        foreach (ActorBehaviour a in Held)
        {
            if (a == null)
                continue;
            GameObject animated = a.m_AnimatedGameObject;
            if (animated != null)
                animated.transform.localPosition = Vector3.zero;
        }
    }

    internal static void Clear() => Held.Clear();

    /// <summary>Replace the whole set with <paramref name="actors"/> (the union currently held by
    /// all remote players). Keeps the reference-counting trivially correct when two peers hold
    /// different figures, or the same one.</summary>
    internal static void ReplaceWith(IEnumerable<ActorBehaviour> actors)
    {
        Held.Clear();
        foreach (ActorBehaviour a in actors)
        {
            if (a != null)
                Held.Add(a);
        }
    }
}
