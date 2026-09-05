using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// SELF-MAINTAINING REPLACEMENTS FOR THE MOD'S PERIODIC <c>FindObjectsOfType</c> SWEEPS
/// (2026-08-09 perf pass, strategy S1 of <c>.planning/perf-zoomed-out.md</c>).
///
/// <para>THE MEASUREMENT. Three independent hardware readings price one
/// <c>Object.FindObjectsOfType&lt;T&gt;()</c> in the big room at roughly 10–15 ms:
/// <c>UnseenTiles.Rescan</c> is 20.2 ms and is ONE such call plus two short subtree walks;
/// <c>Compat.LoaderHeal</c> is 23 ms and is ONE such call plus a per-tile walk; and
/// <c>WallFade</c>'s rescan is 50–97 ms and carries THREE of them. The call is O(every
/// loaded object), not O(objects of that type) — with Addressables holding a big room's
/// assets resident that is a six-figure scan for a set of ~10 components. Seven such calls
/// per second was ≈ 89 ms/s of the mod's measured 138 ms/s, delivered as 20–100 ms hitches:
/// the direct cause of the judder in the zoomed-out overview.</para>
///
/// <para>WHAT THIS IS. One list per hunted component type, filled by a Harmony postfix on
/// the component's own lifecycle method — the same "let the game tell us" shape the
/// <see cref="MaterialLoaderHeal"/> registry already uses (round 7 there). Reading a
/// registry is a walk of ~10 entries; the sweep it replaces was a walk of the whole heap.</para>
///
/// <para>WHY THE RESULT IS THE SAME SET, NOT MERELY A SIMILAR ONE. Three properties, each
/// of which the callers depend on:</para>
/// <list type="number">
/// <item><b>Completeness.</b> Every registry is SEEDED once at install with a real
///   <c>FindObjectsOfType&lt;T&gt;(includeInactive: true)</c>, so anything that already
///   existed when the patches landed (hot reload) is in it; from then on the postfix
///   enrolls every new instance at the exact moment the game runs its lifecycle method.
///   There is no third way for one of these components to come into existence.</item>
/// <item><b>The same visibility filter.</b> <c>Collect</c> returns only entries whose
///   GameObject is <c>activeInHierarchy</c> — which is precisely what the parameterless
///   <c>FindObjectsOfType&lt;T&gt;()</c> the callers used returns.</item>
/// <item><b>The same hideFlags filter.</b> <c>FindObjectsOfType</c> skips objects flagged
///   <c>DontSave</c> (this is the round-6 failure documented in
///   <see cref="MaterialLoaderHeal"/>: Apparance's generated containers are
///   <c>HideAndDontSave</c> and the sweep found nothing at all). <c>Collect</c> applies the
///   identical exclusion, so a registry read can never widen a caller's candidate set —
///   which for the wall system would be a look change.</item>
/// </list>
///
/// <para>The one measurable difference is COST. Nothing here changes which objects a caller
/// sees, so nothing here can change which walls fade, when they fade, or how anything
/// looks.</para>
///
/// <para>MP-SAFE: bookkeeping only — no wire traffic, no game state, no rendering.
/// REVERSIBLE: the postfixes are pure additions that leave the patched methods untouched,
/// and <c>UnpatchSelf</c> on hot reload drops them.</para>
/// </summary>
internal static class SceneRegistry
{
    private const string Name = "Core";

    /// <summary>Every <see cref="TilesOcclusionVolume"/> — the wall fade's room registry
    /// reads these for each room's CentralTile floor plane and CMap identity.</summary>
    internal static readonly ComponentRegistry<TilesOcclusionVolume> Volumes = new();

    /// <summary>Every <see cref="UnityGameEditorDoorProp"/> — the wall fade's doorway
    /// recognition (doorways never fade) and gate-column seeding read these.</summary>
    internal static readonly ComponentRegistry<UnityGameEditorDoorProp> DoorProps = new();

    /// <summary>Every <see cref="ProceduralMapTile"/> — the fog-tile order driver and the
    /// material-loader watchdog read these.</summary>
    internal static readonly ComponentRegistry<ProceduralMapTile> MapTiles = new();

    /// <summary>
    /// "HAS THIS ROOM BEEN DISCOVERED?" — ONE DEFINITION, for every subsystem that reads
    /// <see cref="MapTiles"/> (user ruling 2026-09: <i>"Boards sollen NICHT transparent werden
    /// wenn sie nur unaufgedeckte/unentdeckte Tiles verdecken, sondern nur bei bereits
    /// aufgedeckten Tiles."</i>).
    ///
    /// <para><b>WHY THIS FIELD IS THE AUTHORITY AND NOT A STRUCTURAL PROXY.</b> Three tests were
    /// available and they are not equals — they form a chain, and this is its head:</para>
    /// <list type="number">
    /// <item><c>ProceduralMapTile.visibility</c> (this). A public field on the component the
    ///   callers already hold. <c>RoomVisibilityTracker.ShowMaptile</c> writes exactly
    ///   <c>All</c> when the room is revealed and <c>Preview</c> when it is not (decompiled
    ///   GH.Runtime, lines 32-39); the level editor writes <c>All</c>/<c>Hidden</c>. And the GAME
    ///   ITSELF already uses this comparison as its own "is this room discovered" test —
    ///   <c>ProceduralScenario</c> line 620, <c>mapTiles[j].visibility ==
    ///   ProceduralMapTile.Visibility.All</c>, to pick the ambience room. Reusing the game's own
    ///   expression is the strongest form of "the same set" this mod can have.</item>
    /// <item><c>RoomVisibilityTracker.IsVisible()</c>. Correct, and it also excludes DESTROYED
    ///   rooms — but it is a <c>GetComponent</c> plus a four-deep client-tile chain per tile per
    ///   call, it is what WRITES (1) rather than something that agrees with it, and a room whose
    ///   visibility was overridden by the level editor disagrees with it.</item>
    /// <item>The ACTIVE <c>Generated Content/Preview</c> child (<c>UnseenTileOrder</c>'s own
    ///   test). This is a CONSEQUENCE of (1), one level downstream:
    ///   <c>ProceduralMapTile.ApplyVisibility</c> -> <c>ShowContent</c> sets that node active
    ///   exactly when <c>visibility</c> is <c>Preview</c>/<c>PreviewWithDoors</c>. It is also
    ///   strictly weaker: a tile whose content has not been generated yet has NO 'Generated
    ///   Content' node at all, and the structural test then reports it as discovered — which is
    ///   the wrong answer in the safe-looking direction.</item>
    /// </list>
    ///
    /// <para>A prior recon recommended (3) as the shared predicate. It is (1): (3) is a shadow of
    /// this field, and the shadow has a blind spot this field does not.</para>
    ///
    /// <para>THE WALL SEE-THROUGH ALREADY HAS THIS RULE, one level up and by construction, which
    /// is why it needs no call here: its play area is
    /// <c>TilesOcclusionGenerator.m_RoomRenderers</c>, and the game only appends a volume's
    /// renderers once <c>TilesOcclusionVolume.IsVisible()</c> — i.e. <c>CMap.Revealed</c> — is
    /// true (decompiled <c>TilesOcclusionGenerator.UpdateAwaitingVolumes</c>). An undiscovered
    /// room never enters its room registry and can never contribute a floor sample. The board
    /// fade walked <see cref="MapTiles"/> instead, which is every ACTIVE tile whatever its
    /// visibility, and that is the asymmetry this predicate closes.</para>
    ///
    /// <para>Null-tolerant (a destroyed tile is not a discovered room), and free: one enum
    /// compare on a field the caller's loop already has in hand.</para>
    /// </summary>
    internal static bool IsTileDiscovered(ProceduralMapTile? tile) =>
        tile != null && tile.visibility == ProceduralMapTile.Visibility.All;

    private static bool _installed;

    /// <summary>
    /// Arm each registry's enrolment postfix, then seed it from the live scene. Idempotent;
    /// called from <c>CompatModule.Init</c> BEFORE the drivers that read them.
    ///
    /// <para>FAIL-OPEN BY CONSTRUCTION. Each registry is only marked <c>Armed</c> when its
    /// patch actually took. If a patch throws (a renamed game method after a game update, a
    /// null Harmony instance) the registry stays disarmed and <c>Collect</c> falls back to
    /// the very <c>FindObjectsOfType</c> sweep it replaced — slow, exactly as before, and
    /// still correct. The one outcome that must be impossible is a registry that silently
    /// reports an empty scene, because for the wall system that would read as "no rooms, no
    /// doors" and change what the player sees.</para>
    /// </summary>
    internal static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        Volumes.Arm(ArmVolumes());
        DoorProps.Arm(ArmDoorProps());
        MapTiles.Arm(ArmMapTiles());
        VRLog.Info(Name,
            $"scene registries seeded ({Volumes.Count} occlusion volume(s), "
            + $"{DoorProps.Count} door prop(s), {MapTiles.Count} map tile(s)) — the mod's "
            + "periodic full-scene FindObjectsOfType sweeps read these instead. Same set "
            + "(same active + hideFlags filter), ~10 entries walked instead of the heap.");
    }

    // The three arming calls are spelled out one per method on purpose: the patch inventory
    // guard (scripts/patch-inventory.py) reads a literal `PatchAll(typeof(X))` as the proof
    // that a patch class is not shipping INERT, and a reflective helper would hide it.

    private static bool ArmVolumes()
    {
        try
        {
            if (VRSession.Harmony == null)
                return NotArmed(nameof(TilesOcclusionVolume_Start_RegisterPatch), "no Harmony instance");
            VRSession.Harmony.PatchAll(typeof(TilesOcclusionVolume_Start_RegisterPatch));
            return true;
        }
        catch (System.Exception e)
        {
            return NotArmed(nameof(TilesOcclusionVolume_Start_RegisterPatch), e.Message);
        }
    }

    private static bool ArmDoorProps()
    {
        try
        {
            if (VRSession.Harmony == null)
                return NotArmed(nameof(UnityGameEditorDoorProp_Start_RegisterPatch), "no Harmony instance");
            VRSession.Harmony.PatchAll(typeof(UnityGameEditorDoorProp_Start_RegisterPatch));
            return true;
        }
        catch (System.Exception e)
        {
            return NotArmed(nameof(UnityGameEditorDoorProp_Start_RegisterPatch), e.Message);
        }
    }

    private static bool ArmMapTiles()
    {
        try
        {
            if (VRSession.Harmony == null)
                return NotArmed(nameof(ProceduralTileObserver_OnEnable_RegisterPatch), "no Harmony instance");
            VRSession.Harmony.PatchAll(typeof(ProceduralTileObserver_OnEnable_RegisterPatch));
            return true;
        }
        catch (System.Exception e)
        {
            return NotArmed(nameof(ProceduralTileObserver_OnEnable_RegisterPatch), e.Message);
        }
    }

    /// <summary>One WARN naming the registry that stayed on the old sweep, and why.</summary>
    private static bool NotArmed(string patchClass, string why)
    {
        VRLog.Warn(Name,
            $"scene registry '{patchClass}' could NOT be armed ({why}) — its readers fall "
            + "back to the full-scene FindObjectsOfType sweep: slow, exactly as before this "
            + "change, and identical results.");
        return false;
    }

    /// <summary>Drop every registry (hot-reload teardown — the postfixes go with
    /// <c>UnpatchSelf</c>, so a stale list must not outlive them).</summary>
    internal static void Shutdown()
    {
        Volumes.Clear();
        DoorProps.Clear();
        MapTiles.Clear();
        _installed = false;
    }

    /// <summary>
    /// One hunted component type's live set. A LIST plus a membership SET: enrolment is
    /// idempotent (a lifecycle method can legitimately run more than once over an object's
    /// life) and reads are a linear walk with in-place pruning of the entries Unity has
    /// since destroyed — the same store shape <see cref="MaterialLoaderHeal"/> uses, for the
    /// same reason (a destroyed Unity object still hashes to its old slot, so it must be
    /// removed by the SAME reference that put it there).
    /// </summary>
    internal sealed class ComponentRegistry<T> where T : Component
    {
        private readonly List<T> _live = new();
        private readonly HashSet<T> _known = new();
        private bool _armed;

        /// <summary>Enrolled entries, destroyed ones included (diagnostics only).</summary>
        internal int Count => _live.Count;

        /// <summary>Record whether this type's enrolment postfix took, and — if it did —
        /// seed the store from the live scene so anything that existed BEFORE the patch
        /// landed (hot reload) is enrolled too. <c>includeInactive</c> deliberately ON: the
        /// active filter belongs to <see cref="Collect"/>, so an object that is inactive at
        /// seed time and active later is still known. A disarmed registry is never read —
        /// see <see cref="Collect"/>.</summary>
        internal void Arm(bool armed)
        {
            _armed = armed;
            if (!armed)
                return;
            foreach (T c in Object.FindObjectsOfType<T>(includeInactive: true))
                Add(c);
        }

        /// <summary>Enrol one instance (idempotent, null-safe) — the postfix hot path.</summary>
        internal void Add(T? c)
        {
            if (c == null)
                return;
            if (_known.Add(c))
                _live.Add(c);
        }

        internal void Clear()
        {
            _live.Clear();
            _known.Clear();
            _armed = false;
        }

        /// <summary>
        /// Fill <paramref name="into"/> with exactly what
        /// <c>Object.FindObjectsOfType&lt;T&gt;()</c> would have returned: enrolled, not
        /// destroyed, GameObject <c>activeInHierarchy</c>, and not <c>DontSave</c>-flagged
        /// (see the class header for why each of the three matters). Destroyed entries are
        /// pruned as they are met, so the store tracks live content by itself.
        /// </summary>
        internal void Collect(List<T> into)
        {
            into.Clear();
            if (!_armed)
            {
                // Disarmed (patch failed / already torn down): do exactly what the callers
                // did before this class existed. Slow, never wrong.
                foreach (T c in Object.FindObjectsOfType<T>())
                    into.Add(c);
                return;
            }
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                T c = _live[i];
                if (c == null)
                {
                    _known.Remove(_live[i]); // by the SAME reference — see the class header
                    _live.RemoveAt(i);
                    continue;
                }
                GameObject go = c.gameObject;
                if (!go.activeInHierarchy)
                    continue;
                if ((go.hideFlags & HideFlags.DontSave) != 0)
                    continue;
                into.Add(c);
            }
        }
    }
}

/// <summary>Enrolment postfix for <see cref="SceneRegistry.Volumes"/>: the game's own
/// <c>TilesOcclusionVolume.Start</c> is where a volume announces itself to the occlusion
/// generator, so it is also the one moment every volume provably passes through. Pure
/// bookkeeping — the original method is untouched and a throw here can never reach the
/// game.</summary>
[HarmonyPatch(typeof(TilesOcclusionVolume), "Start")]
internal static class TilesOcclusionVolume_Start_RegisterPatch
{
    private static void Postfix(TilesOcclusionVolume __instance)
    {
        try { SceneRegistry.Volumes.Add(__instance); }
        catch { /* bookkeeping is best-effort — never disturb the game */ }
    }
}

/// <summary>Enrolment postfix for <see cref="SceneRegistry.DoorProps"/> — see
/// <see cref="TilesOcclusionVolume_Start_RegisterPatch"/>.</summary>
[HarmonyPatch(typeof(UnityGameEditorDoorProp), "Start")]
internal static class UnityGameEditorDoorProp_Start_RegisterPatch
{
    private static void Postfix(UnityGameEditorDoorProp __instance)
    {
        try { SceneRegistry.DoorProps.Add(__instance); }
        catch { /* bookkeeping is best-effort — never disturb the game */ }
    }
}

/// <summary>Enrolment postfix for <see cref="SceneRegistry.MapTiles"/>. Patched on the BASE
/// class <c>ProceduralTileObserver</c> because that is where the lifecycle method lives —
/// <c>ProceduralMapTile</c> declares no <c>OnEnable</c> of its own — and filtered back down
/// to map tiles here. <c>OnEnable</c> rather than <c>Start</c>: a tile observer that is
/// disabled and re-enabled runs it again, which is harmless (enrolment is idempotent) and
/// strictly safer than a once-per-lifetime hook.</summary>
[HarmonyPatch(typeof(ProceduralTileObserver), "OnEnable")]
internal static class ProceduralTileObserver_OnEnable_RegisterPatch
{
    private static void Postfix(ProceduralTileObserver __instance)
    {
        try
        {
            if (__instance is ProceduralMapTile tile)
                SceneRegistry.MapTiles.Add(tile);
        }
        catch { /* bookkeeping is best-effort — never disturb the game */ }
    }
}
