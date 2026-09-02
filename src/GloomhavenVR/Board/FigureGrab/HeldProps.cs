using System.Collections.Generic;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Registry of the PROPS currently held in a hand — the prop-side twin of
/// <see cref="HeldFigures"/>, and the only thing <see cref="PropGhosts"/> reconciles against.
///
/// <para><b>WHY IT IS NOT <see cref="HeldFigures"/>.</b> That list keys on
/// <c>ActorBehaviour</c>, and a chest, a gold pile or a plain obstacle has none: a prop only
/// receives a <c>CObjectActor</c> when it is configured for HEALTH (CMap.cs:502-518,
/// CObjectActor.SetAttachedToProp refuses otherwise), which is exactly why the ModBuild 335
/// census found <c>Choreographer.m_ClientObjects</c> empty of props while
/// <c>ScenarioState.Props</c> held fifteen. A prop is keyed on its <c>CObjectProp</c>.</para>
///
/// <para><b>AND WHY IT IS FAR SIMPLER.</b> <see cref="HeldFigures"/> exists chiefly to GATE
/// <see cref="ActorBehaviour_HeldTransform_Patch"/> — the game rewrites a figure's transform
/// every Update and every LateUpdate, so holding one in the hand needs those writes suppressed.
/// Nothing writes a prop's transform per frame: the prop visual is a plain instantiated prefab
/// that the game places once at spawn and then leaves alone (Choreographer.SpawnProp:13159,
/// PlaceRandomProps:15459). So this class needs no patch, no pin, no ring suppressor and no
/// busy predicate — it is a membership record and a hand record, nothing more.</para>
///
/// <para><b>GRAB ORDER IS LOAD-BEARING, for the same reason it is in
/// <see cref="HeldFigures"/>:</b> it is the only ordering that does not move while a hold lasts,
/// so a future wire slot stays pinned to the same prop for the whole hold instead of trading
/// slots with the other hand's prop on every grab and release.</para>
///
/// <para><b>MULTIPLAYER SEAM — THE ONE PLACE THE SYNC ATTACHES.</b> This build is deliberately
/// LOCAL-ONLY and adds nothing to the wire (<c>src/GloomhavenVR/Net/**</c> is owned elsewhere).
/// When the held-prop sync is landed it needs exactly two things, and both meet the code here:
/// a receive-side twin (<c>NetHeldProps</c>, shaped like <see cref="NetHeldFigures"/>) whose
/// <c>Owns</c> is ORed into <see cref="PropGhosts.Tick"/>'s still-held test and into
/// <see cref="GrabbableProp.CanGrab"/>'s grab-lock, and a wire field naming WHICH prop is held.</para>
///
/// <para><b>THE WIRE FIELD, exactly.</b> A NEW extension record — a held prop is not a held
/// figure and must not share <c>ExtIdSecondFigure</c>'s slots, or a peer holding a chest in one
/// hand and a mini in the other would collide on one id space. Shape it as the byte-for-byte twin
/// of that record so the receive path is the same code: <c>[u8 hand flags][u32 propId LE]
/// [20 B pose]</c> = <b>25 bytes</b>, hand bit 0 = "this prop rides the sender's LEFT hand", the
/// pose in the same board-local encoding the held-figure block already uses. Written only while a
/// prop is really held, so an idle packet stays byte-identical and an older peer steps over it by
/// its length.</para>
///
/// <para><b>The id is the FNV-1a-32 hash of <c>CObjectProp.PropGuid</c></b>, which is the exact
/// analogue of what the figure records already do (<c>NetFigures.StableActorId</c> hashes
/// <c>CActor.ActorGuid</c>). <c>PropGuid</c> is the prop's replicated cross-client identity: it is
/// assigned from the scenario's own seeded GUID RNG (CObjectProp.cs:331), carried through the copy
/// constructor (:122) and through serialization (:219/:248-249), and the game itself names props
/// across clients with it (<c>UIEvent.PropDestroyed</c>). Do NOT send an index into
/// <c>ScenarioState.Props</c> — that is a list order, not an identity, and it moves. Do not send
/// the raw 36-character string either; four bytes are enough and match the established id
/// space.</para>
///
/// <para>Record ids in use today are 1..34 contiguously, so the new record takes <b>35</b> (the
/// file's own free-list note says 35..255 are free, and it says to re-derive rather than trust
/// it — enumerate the <c>ExtId*</c> constants before claiming).</para>
/// </summary>
internal static class HeldProps
{
    // Parallel lists in GRAB ORDER (index 0 = the oldest still-held prop). Never more than one
    // entry per hand in practice (a ProximityGrabber holds a single object), so every linear scan
    // below is over <= 2 items.
    private static readonly List<CObjectProp> Held = new();
    private static readonly List<HandSide> Sides = new();

    /// <summary>True while the local player physically holds this prop (grab through landing).</summary>
    internal static bool Owns(CObjectProp? prop) => prop != null && IndexOf(prop) >= 0;

    internal static int Count => Held.Count;

    /// <summary>
    /// The <paramref name="slot"/>-th still-held prop in GRAB ORDER (0 = oldest), with the hand
    /// holding it. False when fewer props than that are held. This is the accessor a held-prop
    /// wire record would sample; it exists now so the sync lane does not have to invent an
    /// ordering later (see the class doc).
    /// </summary>
    internal static bool TryGetSlot(int slot, out CObjectProp prop, out HandSide side)
    {
        prop = null!;
        side = HandSide.Right;
        if (slot < 0 || slot >= Held.Count)
            return false;
        prop = Held[slot];
        side = Sides[slot];
        return true;
    }

    /// <summary>Record a grab. Re-adding a prop already held keeps its ORIGINAL grab position in
    /// the order (a re-grab during the release glide must not demote it) and refreshes its hand.</summary>
    internal static void Add(CObjectProp? prop, HandSide side)
    {
        if (prop == null)
            return;
        int at = IndexOf(prop);
        if (at >= 0)
        {
            Sides[at] = side;
            return;
        }
        Held.Add(prop);
        Sides.Add(side);
    }

    internal static void Remove(CObjectProp? prop)
    {
        if (prop == null)
            return;
        int at = IndexOf(prop);
        if (at < 0)
            return;
        Held.RemoveAt(at);
        Sides.RemoveAt(at);
    }

    internal static void Clear()
    {
        Held.Clear();
        Sides.Clear();
    }

    /// <summary>Reference identity, never <c>Equals</c>: <c>CObjectProp</c> is a plain rule-library
    /// class and a state copy can produce two instances that describe the same prop. The registry
    /// is keyed on the instance the discovery pass actually resolved a GameObject for, so an
    /// equality that matched a COPY would return a prop nobody is holding.</summary>
    private static int IndexOf(CObjectProp prop)
    {
        for (int i = 0; i < Held.Count; i++)
        {
            if (ReferenceEquals(Held[i], prop))
                return i;
        }
        return -1;
    }
}
