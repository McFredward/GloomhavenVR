using System.Collections.Generic;
using UnityEngine;

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
///
/// <para>DO NOT MERGE WITH <see cref="NetHeldFigures"/>, its near-identically-shaped twin.
/// This set's membership is owned by the LOCAL grab flow (<see cref="FigureGrabbable"/>'s
/// grab/glide/release lifetime); the other is REPLACED WHOLESALE by <c>Net/NetFigures</c>
/// whenever a remote grab/release/switch arrives. Sharing one set would couple local grab
/// lifetime to the wire — a dropped packet or a peer disconnect could then clear a figure
/// the local hand is still physically holding. The patch gate deliberately ORs the two
/// (<see cref="ActorBehaviour_HeldTransform_Patch"/>), which is the only place they need to
/// agree.</para>
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

    /// <summary>Enumerate every locally-held actor (task #2 — selection-ring suppression). The set
    /// is only mutated from grab/release paths, never during this enumeration.</summary>
    internal static IEnumerable<ActorBehaviour> All => Held;

    /// <summary>
    /// Issue C: re-pin every held actor's ANIMATED object at its (hand-riding) root — freezing the
    /// animation-driven translation. The game's <c>ApplyMotion</c> normally re-zeros
    /// <c>m_AnimatedGameObject.localPosition</c> every LateUpdate (ActorBehaviour:611) to absorb the
    /// walk/loco clip's root translation; we suppress <c>ApplyMotion</c> for held figures
    /// (<see cref="ActorBehaviour_HeldTransform_Patch"/>), so without this a figure grabbed
    /// mid-animation would animate straight OUT of the hand. Zeroing the animated child's
    /// localPosition each frame reproduces exactly that one write, so the mesh stays in the hand
    /// while the Animator keeps visually playing the clip (bones still move). MUST be called from a
    /// LateUpdate so it runs AFTER the Animator's update. Composes with the remote-held path
    /// (<see cref="NetHeldFigures.PinAnimatedRoots"/>).
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
