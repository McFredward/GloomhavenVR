using System.Collections.Generic;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Receive-side twin of <see cref="HeldProps"/>: the map items a REMOTE player is currently
/// holding (extension record 37, <c>NetProtocol.ExtIdHeldProp</c>). Membership is OWNED by
/// <c>Net.NetProps</c>, which rebuilds it from its per-player held-prop records whenever a remote
/// grab, release or hand-switch arrives. Strict no-op offline: nothing is ever added unless a
/// modded VR peer reports a prop in a hand.
///
/// <para><b>WHY IT IS KEYED ON THE STABLE ID AND NOT ON <c>CObjectProp</c>, which is where it
/// differs from <see cref="NetHeldFigures"/>.</b> That class holds <c>ActorBehaviour</c>
/// references, which are Unity objects and live as long as the figure does. A
/// <c>CObjectProp</c> is a plain rule-library object and the scenario replaces every one of them
/// whenever a state sync hands back a fresh <c>ScenarioState</c> — that is not a hypothesis, it is
/// the whole diagnosis of <see cref="PropVisualLookup"/> ("the cache is keyed by reference", the
/// 2026-09-03 report in which eighteen props became unliftable for a session). A reference-keyed
/// remote set would therefore go silently empty mid-hold on exactly the machine that is watching
/// the hold, and a peer's chest would drop back onto the board while their hand still carried it.
/// The FNV-1a-32 of <c>PropGuid</c> is data and survives a re-key, so this set is asked the one
/// question it can always answer.</para>
///
/// <para><b>AND WHY THERE IS NO PATCH TWIN.</b> <see cref="NetHeldFigures"/> exists chiefly to gate
/// <see cref="ActorBehaviour_HeldTransform_Patch"/>, because the game rewrites a figure's transform
/// every Update and LateUpdate. Nothing writes a prop's transform per frame — re-derived for this
/// build rather than inherited: <c>Choreographer</c> places a prop once at spawn (SpawnProp:13159,
/// PlaceRandomProps:15459) and every other <c>GetPropObject</c> call in it sits inside a MESSAGE
/// handler (activate, reveal, disarm, destroy), while <c>Choreographer.Update</c> is a message pump
/// and a wait-state machine that touches no prop transform at all. So a remotely-held prop needs no
/// suppression: <c>NetProps.Tick</c> simply writes the pose and nothing writes back.</para>
///
/// <para><b>WHAT IT DOES STILL NEED is the ModBuild 349 correction</b>, and that one is not about
/// transform writes: a prop's root carries an <c>ApparanceEntity</c> that rebuilds the prop's whole
/// generated content whenever its transform CHANGES, which is what made a locally held item flicker
/// for three rounds. A remotely-held prop is moved every frame by exactly the same kind of write, so
/// the receive side freezes it the same way (<c>NetProps</c> does the freeze and the anim belt) —
/// "the game leaves a prop alone" must not be read as "a remote hold has no side effects"
/// either.</para>
/// </summary>
internal static class NetHeldProps
{
    /// <summary>Stable prop ids currently held by SOME remote player.</summary>
    private static readonly HashSet<int> Held = new();

    /// <summary>The visual root each remotely-held prop is being driven on, by the same id — the
    /// population <see cref="OwnsRendererOf"/> walks. Kept beside the id set rather than derived on
    /// demand because resolving an id to a visual is a dictionary rebuild, and the wall pass asks
    /// the renderer question thousands of times per rescan.</summary>
    private static readonly Dictionary<int, GameObject> Visuals = new();

    /// <summary>True while ANY remote player holds a prop — one <c>Count</c> compare, so the
    /// steady-state callers (the grab lock, the ghost reconciler, the wall pass) leave immediately
    /// on a board where nobody is carrying anything.</summary>
    internal static bool Any => Held.Count > 0;

    internal static int Count => Held.Count;

    /// <summary>Grab lock / ghost gate: is a REMOTE player holding this prop right now? Hashes the
    /// prop's guid, so it answers correctly for a <c>CObjectProp</c> instance the scenario has
    /// re-keyed since the hold began (see the class doc).</summary>
    internal static bool Owns(CObjectProp? prop)
    {
        if (Held.Count == 0 || prop == null)
            return false;
        int id = Net.NetProps.StablePropId(prop);
        return id != 0 && Held.Contains(id);
    }

    /// <summary>The same question by stable id, for a caller that already has one.</summary>
    internal static bool Owns(int propId) => propId != 0 && Held.Contains(propId);

    /// <summary>
    /// IS THIS RENDERER PART OF A PROP A PEER IS HOLDING? The remote half of
    /// <see cref="HeldProps.OwnsRendererOf"/>, which is what the mod's scenery systems actually
    /// call — see there for the defect the pair exists to end. A remotely-held prop is airborne on
    /// THIS machine exactly like a locally-held one, so without this it would be adopted by the
    /// wall-fade pass and hidden, and the sync would deliver an invisible chest.
    /// </summary>
    internal static bool OwnsRendererOf(Transform? t)
    {
        if (Visuals.Count == 0 || t == null)
            return false;
        for (Transform? cur = t; cur != null; cur = cur.parent)
        {
            foreach (GameObject v in Visuals.Values)
            {
                if (v != null && ReferenceEquals(v.transform, cur))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Replace the whole set with the union currently held by all remote players — the
    /// same reference-counting-free contract <see cref="NetHeldFigures.ReplaceWith"/> uses, and
    /// correct for the same reason: two peers naming one prop, or one peer holding two, are both
    /// just membership.</summary>
    internal static void ReplaceWith(IReadOnlyList<int> ids, IReadOnlyList<GameObject> visuals)
    {
        Held.Clear();
        Visuals.Clear();
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            if (id == 0)
                continue;
            Held.Add(id);
            if (i < visuals.Count && visuals[i] != null)
                Visuals[id] = visuals[i];
        }
    }

    internal static void Clear()
    {
        Held.Clear();
        Visuals.Clear();
    }
}
