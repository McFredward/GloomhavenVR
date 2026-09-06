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
/// <para><b>MULTIPLAYER — LANDED 2026-09-05, and this class is the SEND side of it.</b> A prop
/// hold now goes on the wire as extension record 37 (<c>NetProtocol.ExtIdHeldProp</c>): one or two
/// 27-byte SLOTS of <c>[u8 hand flags][u32 propId LE][20 B pose][u16 held-size code LE]</c>, in the
/// grab order this class defines, sampled through <see cref="TryGetSlot"/> by
/// <c>Net.NetProps.TrySampleHeldSlot</c>. The receive side is <c>Net.NetProps</c> and
/// <see cref="NetHeldProps"/>; <see cref="PropGhosts"/> and <c>GrabbableProp.CanGrab</c> consult
/// the latter so a peer's carried prop leaves a home ghost here and cannot be grabbed out of their
/// hand. The user's ruling is the whole scope: <i>"sie sollen wie die Figuren vollständig
/// synchronisiert werden mit allem drum und dran (mach da keinen Unterschied zwischen Figuren und
/// Props!)"</i>.</para>
///
/// <para><b>WHERE THE SPEC THAT USED TO STAND HERE WAS WRONG</b>, kept because the next
/// design-in-advance note will be written with the same amount of knowledge this one was:</para>
/// <list type="number">
///   <item><b>"the byte-for-byte twin of record 8, 25 bytes"</b> could not hold what the same
///   paragraph demanded of it. Record 8 is 25 bytes because it describes ONE figure and the other
///   one rides the rig packet; there is no prop block in the rig packet, so a 25-byte record could
///   state only one of the two hands — and a player CAN hold a prop in each. It would also have had
///   nowhere to put the held size, which the note two paragraphs down required to be a field "from
///   the start". Shipped as a repeated SLOT read by record LENGTH, the shape records 20 and 36
///   already use: 27 bytes per slot, 27 or 54 total, both hands stated together.</item>
///   <item><b>"the stretch needs nothing here … no peer renders a held prop at all"</b> was true
///   only while the hold was local. It is the sync that makes a peer render it, so the factor
///   became load-bearing in the same commit that made the claim false; it is field four of the
///   slot, quantized by record 30's own codec.</item>
///   <item><b>"<see cref="TryGetSlot"/> has no callers"</b> (the sync brief, not this file) had
///   expired: <c>WorldUI.Surfaces.PropInfoSurface</c> docks a held prop's info card by it. The wire
///   sampler takes an OVERLOAD instead of widening that signature.</item>
///   <item>The id number was <b>right this time</b> — 1..36 were all in use on ModBuild 444 and 37
///   was free — but it was re-enumerated at the moment of writing rather than trusted, which is
///   what the paragraph below asks for and what the previous claim of "35" had skipped.</item>
/// </list>
///
/// <para><b>The id is the FNV-1a-32 hash of <c>CObjectProp.PropGuid</c></b>, which is the exact
/// analogue of what the figure records already do (<c>NetFigures.StableActorId</c> hashes
/// <c>CActor.ActorGuid</c>). <c>PropGuid</c> is the prop's replicated cross-client identity: it is
/// assigned from the scenario's own seeded GUID RNG (CObjectProp.cs:331), carried through the copy
/// constructor (:122) and through serialization (:219/:248-249), and the game itself names props
/// across clients with it (<c>ScenarioState.LogMismatch</c> prints "Prop GUID" when two clients'
/// prop lists disagree). NOT an index into <c>ScenarioState.Props</c> — that is a list order, not
/// an identity, and it moves. Not the raw 36-character string either; four bytes are enough and
/// match the established id space.</para>
///
/// <para><b>AND THE ID SPACE MOVES: ENUMERATE IT AT THE MOMENT OF CLAIMING.</b> This note once read
/// "ids 1..34 are in use, so the new record takes 35", and by the time the record was written
/// ModBuild 356 had shipped <c>ExtIdItemUsable = 35</c> and <c>ExtIdHeldCardFace = 36</c>. That is
/// the debt-outlives-its-truth case in miniature: the evidence was right when it was written and
/// the conclusion was never re-checked. An id outside a reader's declared range kills the WHOLE
/// record rather than one field, so the next claimant counts the <c>ExtId*</c> constants again and
/// does not trust this sentence either.</para>
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

    /// <summary>
    /// THE PROP'S BOARD-HOME WORLD SIZE, captured at the grab instant, in the same GRAB ORDER as
    /// <see cref="Held"/> — the divisor that turns the visual's live <c>lossyScale</c> into the
    /// HELD SIZE the wire carries (<c>Net.NetProps.SampleHeldStretch</c>, record 37's u16 field).
    ///
    /// <para><b>WHY IT IS CAPTURED HERE and not read out of <see cref="GrabbableProp"/>.</b> That
    /// class already captures the identical number (<c>_homeWorldScale</c>) one line above its call
    /// to <see cref="Add"/> — but it is private, and this build's file ownership does not extend
    /// there. It costs nothing to take it here because <see cref="Add"/> is called at exactly the
    /// right instant BY CONTRACT: <c>GrabbableProp.OnGrab</c> registers the prop BEFORE the
    /// <c>SetParent</c> that puts it in the hand (so no wall pass can see an unclaimed airborne
    /// prop), which is the same frame, and the same untouched transform, the home pose and the home
    /// ghost are taken from.</para>
    ///
    /// <para><b>FIRST CAPTURE WINS.</b> A re-<see cref="Add"/> of a prop already in the list is a
    /// re-grab during the release glide or a hand refresh, and by then the visual may already be in
    /// a hand — re-reading <c>lossyScale</c> there would record the HELD size as the home size and
    /// the wire factor would collapse to 1.0 for the rest of the hold. So the value is written only
    /// when the slot is new, or when what is stored is degenerate.</para>
    /// </summary>
    private static readonly List<float> HomeWorldScales = new();

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
    /// <para><b>IT ANSWERS FOR A PEER'S HAND TOO (2026-09-05, the held-prop sync).</b> The question
    /// the callers actually ask is "is this renderer IN A HAND?", and since record 37 a hand may be
    /// a remote player's. A remotely-held prop is exactly as airborne on this machine as a locally
    /// held one and fails <c>WallSegmentFade.IsFigureOrActorRenderer</c> for exactly the same three
    /// reasons — so without the remote term the wall pass would adopt a peer's carried chest and set
    /// <c>renderer.enabled = false</c> on it, i.e. the sync would deliver an invisible prop and the
    /// user's report would come back unchanged. The remote term is asked SECOND because the local
    /// list is the hot one and both are early-outs on a <c>Count</c> compare.</para>
    ///
    /// <para><b>COST.</b> One <c>List.Count</c> compare when nothing is held — the steady state,
    /// and the state this is called in ~3000 times per wall rescan. While a prop IS held it is an
    /// ancestor walk against at most two roots, and it terminates at the scene root.</para>
    /// </summary>
    internal static bool OwnsRendererOf(Transform? t) =>
        LocalOwnsRendererOf(t) || NetHeldProps.OwnsRendererOf(t);

    /// <summary>
    /// THE LOCAL HALF OF <see cref="OwnsRendererOf"/>, ON ITS OWN - "is this renderer part of a
    /// prop in one of THIS client's hands?", with no remote term.
    ///
    /// <para>Split out (2026-09-06) for ONE caller and one purpose: the wall system's held-prop
    /// census has to say WHOSE hand granted the exemption, because "the local hand is exempt and
    /// a peer's is not" and "neither is" produce the same picture on this machine - a tree
    /// dissolving - and they are not the same defect. No POLICY may branch on this: the user's
    /// ruling is that a prop in ANY hand is never sight-blocking, so every refusal still asks
    /// <see cref="OwnsRendererOf"/>. This one is for the sentence, not for the decision.</para>
    /// </summary>
    internal static bool LocalOwnsRendererOf(Transform? t)
    {
        if (t == null || Visuals.Count == 0)
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
    /// holding it. False when fewer props than that are held. This is the accessor the held-prop
    /// wire record samples (<c>Net.NetProps.TrySampleHeldSlot</c>, record 37), and it is why the
    /// slot a prop occupies does not move for the length of a hold: grab order is the only ordering
    /// that is stable while both hands are being used, so picking up a second chest cannot push the
    /// first one into the other wire slot mid-stream.
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

    /// <summary>
    /// The same slot, with the two things the WIRE needs beyond identity and hand: the VISUAL whose
    /// live transform is the pose and the size being sent, and the prop's board-home world size,
    /// which is the divisor that turns that size into a factor a peer can apply to its OWN copy of
    /// the prop (see <see cref="HomeWorldScales"/>). <paramref name="homeWorldScale"/> is 0 when the
    /// capture was degenerate; the caller then reports "board size" rather than dividing by it.
    ///
    /// <para>An OVERLOAD rather than a wider signature on purpose: the three-argument form above has
    /// a live caller (<c>WorldUI.Surfaces.PropInfoSurface</c> docks a held prop's info card by it),
    /// and widening it would have made an unrelated surface carry two out-parameters it has no use
    /// for.</para>
    /// </summary>
    internal static bool TryGetSlot(int slot, out CObjectProp prop, out GameObject visual,
                                    out HandSide side, out float homeWorldScale)
    {
        visual = null!;
        homeWorldScale = 0f;
        if (!TryGetSlot(slot, out prop, out side))
            return false;
        visual = Visuals[slot];
        homeWorldScale = HomeWorldScales[slot];
        return visual != null;
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
            // The home SIZE is deliberately not refreshed here — see HomeWorldScales: by the time a
            // re-Add runs the visual may already be riding the hand, and re-reading it there would
            // record the held size as the home size and flatten the wire factor to 1.0. It is
            // written only if the first capture failed outright.
            if (HomeWorldScales[at] <= 0f && visual != null)
                HomeWorldScales[at] = visual.transform.lossyScale.x;
            return;
        }
        Held.Add(prop);
        Sides.Add(side);
        Visuals.Add(visual!);
        // THE HOME SIZE, TAKEN ON THE ONE FRAME IT IS TRUE. OnGrab calls this before the
        // worldPositionStays reparent, so lossyScale here is still the prop's size on its board hex.
        HomeWorldScales.Add(visual != null ? visual.transform.lossyScale.x : 0f);
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
        HomeWorldScales.RemoveAt(at);
    }

    internal static void Clear()
    {
        Held.Clear();
        Sides.Clear();
        Visuals.Clear();
        HomeWorldScales.Clear();
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
