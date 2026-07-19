using System.Collections.Generic;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Registry of the actors currently held in a hand — the gate for the
/// <see cref="ActorBehaviour_HeldTransform_Patch"/> transform-write suppression (exact
/// precedent: <c>WorldUI/ActorBars.Owns()</c>, which gates the
/// <c>WorldspaceDisplayPanelBase</c> transform prefix-skip the same way).
///
/// While an <see cref="ActorBehaviour"/> is in this set the game's per-frame POSITION
/// writes (<c>ActorBehaviour.Update → DoTransform</c> and
/// <c>ActorBehaviour.LateUpdate → ApplyMotion</c>) are skipped for THAT actor only, so
/// the mini can ride the hand. The actor's <c>Animator</c> is a separate component that
/// Unity keeps ticking on its own — animations keep playing (see the patch doc). On
/// release the actor leaves the set, the game's own Update resumes and snaps it straight
/// back to its board cell (<c>m_LocoIntermediateTarget</c>) — no manual return math.
/// </summary>
internal static class HeldFigures
{
    private static readonly HashSet<ActorBehaviour> Held = new();

    // The most-recently-grabbed actor still in <see cref="Held"/> — the single figure the
    // send side of figure-grab sync mirrors to peers (the wire format carries one held
    // figure). Cleared / reassigned to another still-held actor on Remove/Clear so it never
    // points at a released mini.
    private static ActorBehaviour? _current;

    /// <summary>Patch gate: true when the game must NOT drive this actor's transform.</summary>
    internal static bool Owns(ActorBehaviour actor) => actor != null && Held.Contains(actor);

    /// <summary>The figure the local player is currently holding (last grabbed), or null.
    /// Consumed by <c>Net/NetFigures.TrySampleHeld</c> for cosmetic pickup sync.</summary>
    internal static ActorBehaviour? Current => _current != null && Held.Contains(_current) ? _current : null;

    internal static void Add(ActorBehaviour actor)
    {
        if (actor != null)
        {
            Held.Add(actor);
            _current = actor;
        }
    }

    internal static void Remove(ActorBehaviour actor)
    {
        if (actor != null)
        {
            Held.Remove(actor);
            if (ReferenceEquals(_current, actor))
                _current = FirstHeld();
        }
    }

    internal static int Count => Held.Count;

    internal static void Clear()
    {
        Held.Clear();
        _current = null;
    }

    private static ActorBehaviour? FirstHeld()
    {
        foreach (ActorBehaviour a in Held)
            return a;
        return null;
    }
}
