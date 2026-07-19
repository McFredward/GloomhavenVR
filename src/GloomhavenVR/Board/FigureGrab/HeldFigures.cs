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

    /// <summary>Patch gate: true when the game must NOT drive this actor's transform.</summary>
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
}
