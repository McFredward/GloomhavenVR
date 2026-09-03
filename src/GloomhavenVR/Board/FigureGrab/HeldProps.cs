using System.Collections.Generic;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

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
/// <para><b>CORRECTION (ModBuild 349), because the paragraph above is true and was still
/// misleading.</b> Nothing writes a held prop's transform — but something READS it every frame and
/// acts on it. A prop spawns from <c>GetApparancePropPrefab</c>, so its root carries an
/// <c>ApparanceEntity</c>, and that component rebuilds the prop's whole generated content whenever
/// its transform changes. Moving a prop into a hand therefore had a per-frame consequence after
/// all, just not a transform write: three rounds of "das Item flackert in der Hand" were the prop
/// destroying and re-instantiating its own meshes. The suppression that case needs is in
/// <see cref="GrabbableProp.FreezeApparance"/>, not here — but "the game leaves a prop alone" must
/// not be read as "a hold has no side effects" ever again.</para>
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
/// <para><b>THE ID, RE-DERIVED 2026-09-03 — AND THE OLD NUMBER HERE WAS WRONG.</b> This note used
/// to read "ids 1..34 are in use, so the new record takes 35". Both halves have expired: ModBuild
/// 356 shipped <c>ExtIdItemUsable = 35</c> and <c>ExtIdHeldCardFace = 36</c>
/// (<c>Net/NetProtocol.cs:19114</c> and <c>:19190</c>). The next free id is <b>37</b>. This is
/// exactly the debt-outlives-its-truth case the project has recorded before: the evidence was
/// right when it was written, the conclusion was never re-checked, and an id outside its declared
/// range kills the WHOLE record rather than one field. Enumerate the <c>ExtId*</c> constants at
/// the moment of claiming; do not trust this sentence either.</para>
///
/// <para><b>AND THE HELD-PROP STRETCH NEEDS NOTHING HERE</b> (ModBuild 362). Map items can now be
/// resized in the hand exactly as figures can (<see cref="GrabbableProp.SetStretch"/>), and a
/// figure's factor needs the wire (<c>NetProtocol.ExtIdHeldStretch</c>, record 30) only because a
/// peer RENDERS the held figure and cannot derive the manual factor. No peer renders a held prop
/// at all, so a prop factor would be a number nothing reads. When the record above is finally
/// claimed, the stretch is one of its fields from the start — it syncs fully or not at all.</para>
/// </summary>
internal static class HeldProps
{
    // Parallel lists in GRAB ORDER (index 0 = the oldest still-held prop). Never more than one
    // entry per hand in practice (a ProximityGrabber holds a single object), so every linear scan
    // below is over <= 2 items.
    private static readonly List<CObjectProp> Held = new();
    private static readonly List<HandSide> Sides = new();

    /// <summary>The VISUAL ROOT each held prop rides the hand with, in the same GRAB ORDER as
    /// <see cref="Held"/>. Recorded so <see cref="OwnsRendererOf"/> can answer "is this renderer
    /// part of something the player is holding right now?" — see that method for the defect it
    /// exists to end.</summary>
    private static readonly List<GameObject> Visuals = new();

    /// <summary>True while the local player physically holds this prop (grab through landing).</summary>
    internal static bool Owns(CObjectProp? prop) => prop != null && IndexOf(prop) >= 0;

    internal static int Count => Held.Count;

    /// <summary>
    /// IS THIS RENDERER PART OF A PROP THE PLAYER IS HOLDING RIGHT NOW? The one question the
    /// mod's scenery systems have to be able to ask before they may touch a renderer.
    ///
    /// <para><b>WHY IT EXISTS (ModBuild 339, defect (a): "in der Hand ist es garnicht oder nur
    /// immer ganz kurz für einen Frame sichtbar").</b> <c>WallSegmentFade</c> adopts airborne
    /// scenery as wall dressing and as stacked shell, and both passes end in
    /// <c>renderer.enabled = false</c>. Its one exemption is
    /// <c>WallSegmentFade.IsFigureOrActorRenderer</c> — <c>SkinnedMeshRenderer</c>, or an
    /// <c>ActorBehaviour</c> / <c>CInteractableActor</c> / <c>Animator</c> on an ANCESTOR. A held
    /// MINIATURE passes that test twice over. A held PROP fails it by construction: a prop has no
    /// <c>ActorBehaviour</c> at all (the whole reason <see cref="GrabbableProp"/> is not a
    /// <c>FigureGrabbable</c>), most prop bodies are plain <c>MeshRenderer</c>s, and the walk is
    /// <c>GetComponentInParent</c> — so whatever an ancestor on the BOARD may have contributed is
    /// gone the moment the prop is reparented under the hand's grab anchor. Lifting a prop
    /// therefore makes it airborne, un-exempt and re-parented in one step: it is adopted on the
    /// very next wall pass and hidden, which is exactly "visible for one frame".</para>
    ///
    /// <para><b>COST.</b> One <c>List.Count</c> compare when nothing is held — the steady state,
    /// and the state this is called in ~3000 times per wall rescan. While a prop IS held it is an
    /// ancestor walk against at most two roots, and it terminates at the scene root.</para>
    /// </summary>
    internal static bool OwnsRendererOf(Transform? t)
    {
        if (Visuals.Count == 0 || t == null)
            return false;
        for (Transform? cur = t; cur != null; cur = cur.parent)
        {
            for (int i = 0; i < Visuals.Count; i++)
            {
                GameObject v = Visuals[i];
                if (v != null && ReferenceEquals(v.transform, cur))
                    return true;
            }
        }
        return false;
    }

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
    internal static void Add(CObjectProp? prop, GameObject? visual, HandSide side)
    {
        if (prop == null)
            return;
        int at = IndexOf(prop);
        if (at >= 0)
        {
            Sides[at] = side;
            if (visual != null)
                Visuals[at] = visual; // a re-grab may re-resolve the visual; keep the slot in step
            return;
        }
        Held.Add(prop);
        Sides.Add(side);
        Visuals.Add(visual!);
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
        Visuals.RemoveAt(at);
    }

    internal static void Clear()
    {
        Held.Clear();
        Sides.Clear();
        Visuals.Clear();
        // Nothing is held any more, so no window can be a held card. Cleared HERE rather than only
        // in GrabbableProp.ClearInfo because this path is the one that runs when a hold ends
        // WITHOUT a release — scenario teardown, the feature dial going off, a driver prune — and a
        // stale claim would leave the hover card looking for a hand that is not holding anything.
        HeldPropCard.Clear();
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
