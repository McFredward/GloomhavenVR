using System.Collections.Generic;

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
