using System.Collections.Generic;
using GloomhavenVR.Hands;
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
/// <para>IT IS AN ORDERED LIST, NOT A SET, and it remembers WHICH HAND holds each figure. Both
/// properties are what the two-handed figure sync needs (MP wire record
/// <c>NetProtocol.ExtIdSecondFigure</c>): the wire has a FIRST slot (the rig packet, 15 Hz) and a
/// SECOND slot (the extras tail), and the assignment has to be STABLE or the two minis trade slots
/// on every peer the moment a hand grabs or releases. Grab order gives that stability for free —
/// the OLDEST still-held figure keeps the first slot for its whole hold, so grabbing a second mini
/// never disturbs the first one's stream, and releasing the second one never disturbs it either.
/// The hand rides the wire alongside it so a receiver can prove the two records describe DIFFERENT
/// hands instead of driving both minis into one palm.</para>
///
/// <para>DO NOT MERGE WITH <see cref="NetHeldFigures"/>, its near-identically-shaped twin.
/// This list's membership is owned by the LOCAL grab flow (<see cref="FigureGrabbable"/>'s
/// grab/glide/release lifetime); the other is REPLACED WHOLESALE by <c>Net/NetFigures</c>
/// whenever a remote grab/release/switch arrives. Sharing one store would couple local grab
/// lifetime to the wire — a dropped packet or a peer disconnect could then clear a figure
/// the local hand is still physically holding. The patch gate deliberately ORs the two
/// (<see cref="ActorBehaviour_HeldTransform_Patch"/>), which is the only place they need to
/// agree.</para>
/// </summary>
internal static class HeldFigures
{
    // Parallel lists in GRAB ORDER (index 0 = the oldest still-held figure). Two lists rather than
    // a list of tuples so <see cref="All"/> can hand out the actor list itself, allocation-free,
    // to the per-frame ring suppressor. Never more than one entry per hand in practice (a
    // ProximityGrabber holds a single object), so every linear scan below is over <= 2 items.
    private static readonly List<ActorBehaviour> Held = new();
    private static readonly List<HandSide> Sides = new();

    // The most-recently-grabbed actor still in <see cref="Held"/> — the single figure the
    // flat-screen avatar mirror clones into its hand. DELIBERATELY still "last grabbed" while the
    // wire uses "oldest grabbed" (see TryGetSlot): the mirror shows the figure you just picked up,
    // the wire needs a slot assignment that does not move under a running stream.
    private static ActorBehaviour? _current;

    /// <summary>Patch gate: true when the game must NOT drive this actor's transform.</summary>
    internal static bool Owns(ActorBehaviour actor) => actor != null && IndexOf(actor) >= 0;

    /// <summary>The figure the local player grabbed most recently, or null. Consumed by the
    /// flat-screen <c>WorldUI/AvatarMirror</c>. The NET send side does NOT use this — it walks
    /// <see cref="TryGetSlot"/> in grab order, so its slots stay put while both hands are full.</summary>
    internal static ActorBehaviour? Current => _current != null && Owns(_current) ? _current : null;

    /// <summary>
    /// The <paramref name="slot"/>-th still-held figure in GRAB ORDER (0 = oldest), with the hand
    /// holding it. False when fewer figures are held than that.
    ///
    /// <para>Grab order is the whole point: it is the only ordering that does not change while a
    /// figure stays held, so the wire's first/second slots stay pinned to the same mini for the
    /// whole hold. Ordering by hand instead would re-key a hold when a figure changes hands, and
    /// ordering by "most recent" (<see cref="Current"/>) would move the first figure out of the
    /// 15 Hz rig slot the instant the other hand grabbed anything.</para>
    /// </summary>
    internal static bool TryGetSlot(int slot, out ActorBehaviour actor, out HandSide side)
    {
        actor = null!;
        side = HandSide.Right;
        if (slot < 0 || slot >= Held.Count)
            return false;
        ActorBehaviour candidate = Held[slot];
        if (candidate == null)
            return false;
        actor = candidate;
        side = Sides[slot];
        return true;
    }

    /// <summary>Record a grab. <paramref name="side"/> is the hand that took it — carried so the
    /// figure sync can say which mini is in which hand (see <see cref="TryGetSlot"/>). Re-adding an
    /// actor already held keeps its ORIGINAL grab position in the order (a re-grab during the
    /// release glide must not demote it out of the wire's first slot) and refreshes its hand.</summary>
    internal static void Add(ActorBehaviour actor, HandSide side)
    {
        if (actor == null)
            return;
        int at = IndexOf(actor);
        if (at >= 0)
        {
            Sides[at] = side;
        }
        else
        {
            Held.Add(actor);
            Sides.Add(side);
        }
        _current = actor;
    }

    internal static void Remove(ActorBehaviour actor)
    {
        if (actor == null)
            return;
        int at = IndexOf(actor);
        if (at < 0)
            return;
        Held.RemoveAt(at);
        Sides.RemoveAt(at);
        if (ReferenceEquals(_current, actor))
            _current = Held.Count > 0 ? Held[Held.Count - 1] : null;
    }

    internal static int Count => Held.Count;

    /// <summary>Enumerate every locally-held actor (task #2 — selection-ring suppression). The list
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
        for (int i = 0; i < Held.Count; i++)
        {
            ActorBehaviour a = Held[i];
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
        Sides.Clear();
        _current = null;
    }

    /// <summary>Reference identity, never <c>Equals</c>: <c>UnityEngine.Object</c> overrides
    /// equality so two DESTROYED actors compare equal to each other, which would make
    /// <c>List.Contains</c> match the wrong entry during a scenario teardown.</summary>
    private static int IndexOf(ActorBehaviour actor)
    {
        for (int i = 0; i < Held.Count; i++)
        {
            if (ReferenceEquals(Held[i], actor))
                return i;
        }
        return -1;
    }
}
