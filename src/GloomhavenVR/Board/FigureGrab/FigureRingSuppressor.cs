using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// TASK #2 — while a figure is held in a hand (locally, or by a REMOTE player via the cosmetic
/// pickup sync), the game's selection ring (<c>ActorBehaviour.m_Hilight</c>, a child of the actor
/// subtree that therefore rides the hand) is kept OFF on the LIVE actor, so the ring shows ONLY at
/// the ghost/home cell (the ghost snapshot contains its own copy of the ring, preserved with the
/// game's original materials by <c>FigureOverlay.BuildFrozenGhost</c>). The game's intent is
/// remembered and restored on release.
///
/// Two mechanisms cooperate:
/// - <see cref="Tick"/> (every LateUpdate, after the game's Update writes, before render): hides an
///   active ring on every held actor, remembering that the game wanted it on;
/// - the <c>ActorBehaviour.SetHilighted</c> prefix in
///   <see cref="ActorBehaviour_HeldTransform_Patch"/> routes the game's MID-HOLD toggles here via
///   <see cref="RecordGameIntent"/> (skipping the SetActive), so a deselect-while-held is honoured
///   on release instead of wrongly resurrecting the ring.
///
/// Multiplayer: driven purely by "held by anyone" (<c>HeldFigures.Owns || NetHeldFigures.Owns</c>);
/// the ring is a purely local visual, so suppress/restore never touches game state or the wire.
/// Strict no-op when nothing is held (offline included).
/// </summary>
internal static class FigureRingSuppressor
{
    /// <summary>Held actors whose ring we manage → the game's last desired active state.</summary>
    private static readonly Dictionary<ActorBehaviour, bool> _tracked = new();
    private static readonly List<ActorBehaviour> _scratch = new(4);

    /// <summary>The game called <c>SetHilighted</c> for a HELD actor (patched away): remember what
    /// it wanted so release restores exactly that.</summary>
    internal static void RecordGameIntent(ActorBehaviour actor, bool wanted)
    {
        if (actor != null)
            _tracked[actor] = wanted;
    }

    /// <summary>Per-frame reconcile. Call from LateUpdate (after the game's Update, before render)
    /// so a ring the game re-activated this frame never survives to the screen while held.</summary>
    internal static void Tick()
    {
        // [Optimize] FigureScanCache (2026-07 perf pass): HeldFigures.All / NetHeldFigures.All are
        // typed IEnumerable<ActorBehaviour> over a List and a HashSet, so each foreach in Suppress
        // BOXES the struct enumerator — two heap allocations EVERY LateUpdate, unconditionally, even
        // though the overwhelmingly common case is "nothing is held at all" and Suppress then
        // iterates zero elements. Checking the counts first makes the idle case allocation-free
        // and behaviour-identical (an empty set has nothing to suppress by definition).
        bool lean = Core.PerfConfig.FigureScanCacheOn;
        if (!lean || HeldFigures.Count > 0)
            Suppress(HeldFigures.All);
        if (!lean || NetHeldFigures.Count > 0)
            Suppress(NetHeldFigures.All);

        if (_tracked.Count == 0)
            return;
        _scratch.Clear();
        foreach (KeyValuePair<ActorBehaviour, bool> kv in _tracked)
        {
            ActorBehaviour actor = kv.Key;
            if (actor == null || (!HeldFigures.Owns(actor) && !NetHeldFigures.Owns(actor)))
                _scratch.Add(actor!); // released (or torn down) → restore + forget
        }
        for (int i = 0; i < _scratch.Count; i++)
            Restore(_scratch[i]);
    }

    /// <summary>Restore every ring and forget everything (module shutdown / scene teardown).</summary>
    internal static void Clear()
    {
        _scratch.Clear();
        foreach (ActorBehaviour actor in _tracked.Keys)
            _scratch.Add(actor);
        for (int i = 0; i < _scratch.Count; i++)
            Restore(_scratch[i]);
        _tracked.Clear();
    }

    private static void Suppress(IEnumerable<ActorBehaviour> held)
    {
        foreach (ActorBehaviour actor in held)
        {
            if (actor == null)
                continue;
            bool known = _tracked.TryGetValue(actor, out bool wanted);
            GameObject ring = actor.m_Hilight;
            if (ring != null && ring.activeSelf)
            {
                wanted = true; // the game had (or re-asserted) the ring — remember, then hide
                ring.SetActive(false);
            }
            else if (!known)
            {
                wanted = false; // first sight, ring already off — nothing to restore later
            }
            _tracked[actor] = wanted;
        }
    }

    private static void Restore(ActorBehaviour? actor)
    {
        if (actor is null)
            return; // CLR-null never happens (keys are non-null refs) — keeps the analyzer honest
        ActorBehaviour key = actor; // CLR-non-null alias (the Unity != below muddies actor's state)
        // A DESTROYED actor stays a valid CLR dictionary key; the Unity null check skips the ring.
        if (_tracked.TryGetValue(key, out bool wanted)
            && wanted && actor != null && actor.m_Hilight != null && !actor.m_Hilight.activeSelf)
            actor.m_Hilight.SetActive(true);
        _tracked.Remove(key);
    }
}
