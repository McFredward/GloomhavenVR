using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using Script.Controller;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// <b>WHICH GAMEOBJECT IS THIS PROP, WHEN THE GAME'S OWN LOOKUP HAS STOPPED ANSWERING.</b>
///
/// <para>User, 2026-09-03: <i>"Deine letzte Anpassung hat das greifen der props nun völlig kaputt
/// gemacht - weder kann ich nun irgendeinen der props greifen, noch kommt das highlighting. Davor
/// hat es funktioniert."</i></para>
///
/// <para><b>THE LOG NAMED THE BLOCKER IN ONE LINE, AND IT IS NOT THE GLOW.</b> The whole hover
/// story — no grab AND no highlight — has a single cause upstream of both, because a prop that is
/// not in the registry has no <see cref="GrabbableProp"/>, and the glow, the pick collider, the
/// ghost, the card and the grab all hang off that object. The 2026-09-03 census:</para>
/// <code>
/// ScenarioState.Props held 21 prop(s), 18 liftable by import type
///   ... PropGrab registry: 0 prop(s) grabbable, 18 still unresolved
///   ... 'OneHexObstacle : (368f916c-…)' visual=NOT IN ObjectCacheService ... GRABBABLE=NO
/// </code>
/// <para>Every one of the eighteen, for the whole session — 278 <c>Failed to find prop</c> warnings
/// over exactly 18 distinct instance GUIDs, still missing at log line 12500. The build BEFORE it
/// (ModBuild 363, same scenario, the SAME GUIDs — <c>b13c37e7-…</c> appears in both logs) reached
/// <c>18 prop(s) grabbable</c>, and then degraded on its own to <c>1 grabbable, 17 still
/// unresolved</c> without a single prop leaving the board. So this is not a glow rule and it is not
/// the ModBuild 366 diff, which never touched this path; it is a lookup that stops answering, and
/// 363 was already half-broken by it.</para>
///
/// <para><b>WHY IT STOPS ANSWERING: THE CACHE IS KEYED BY REFERENCE.</b>
/// <c>ObjectCacheService._propsCache</c> is a <c>Dictionary&lt;CObjectProp, GameObject&gt;</c> with
/// no comparer, so its key is object identity. Its entries are written once, at spawn
/// (Choreographer.SpawnProp:13159, PlaceRandomProps:15459, DelayedDropSMB:181,
/// UnityGameEditorRuntime:784/:832, ClientScenarioManager:220). Nothing rewrites them when a state
/// sync hands back a <c>ScenarioState</c> whose <c>Props</c> list holds FRESH <c>CObjectProp</c>
/// instances describing the same board — and that this happens is not a guess: <c>PropGrab.Scan</c>
/// has a whole paragraph about it and orders its own drop/add around it. From that moment the
/// game's dictionary maps OLD instances to the LIVE GameObjects while every caller holds NEW ones,
/// and <c>GetPropObject</c> can only miss, for ever, for every prop. Which is exactly the shape of
/// the two logs: 363 resolved and then lost 17 of 18 in one census interval; 366 was already past
/// the swap before its first successful resolve.</para>
///
/// <para><b>THE REPAIR IS TO ASK BY THE GAME'S OWN STABLE IDENTITY.</b> <c>InstanceName</c> is that
/// identity: it carries the prop's GUID (<c>'OneHexObstacle : (368f916c-714a-aa1c-9dc6-…)'</c>), it
/// survives a re-key because it is DATA rather than a pointer, and it is what the game itself
/// reverse-looks-up with when it has a GameObject and wants the CObject
/// (<c>UIPropInfoPanel.ShowTrap</c>: <c>Props.OfType&lt;CObjectTrap&gt;().SingleOrDefault(s =&gt;
/// s.InstanceName == trap.name)</c>). The visual is NAMED by it too — the ModBuild 363 log's own
/// success line reads <c>visual 'Trap : (bf0…'</c> — which is what makes the third fallback
/// possible.</para>
///
/// <para><b>THREE ROUTES, TRIED IN ORDER, AND THE ROUTE IS REPORTED.</b>
/// <list type="number">
///   <item><b>reference</b> — the dictionary hit the game intends. Free, and the only one that runs
///   while nothing has gone wrong.</item>
///   <item><b>instance name</b> — the same dictionary, indexed by its keys' <c>InstanceName</c>.
///   Answers after a re-key. The index is rebuilt only when the dictionary's <c>Count</c> changes,
///   so the steady state is one <c>int</c> comparison and one string hash.</item>
///   <item><b>none</b> — and this is a real answer, not a failure to look: it means the visual is
///   not in the cache under ANY key, i.e. the prop has genuinely not spawned yet (an unrevealed
///   room) or has been destroyed. The caller retries; the log says which of the three answered so
///   the next hardware round does not have to infer it.</item>
/// </list></para>
///
/// <para><b>AND IT READS THE DICTIONARY DIRECTLY RATHER THAN CALLING <c>GetPropObject</c>.</b> Two
/// reasons, both load-bearing. The first is that <c>GetPropObject</c> logs a warning of the GAME's
/// own on every miss (ObjectCacheService.cs:104) — 278 of them in the 2026-09-03 log, all of them
/// ours, which is a discovery pass being the loudest thing in the log it is meant to make readable.
/// The second follows from the first: that noise is the ONLY reason <c>PropGrab</c> had to stop
/// retrying after a bounded settle budget, and a budget that expires is why a late prop could never
/// come back. A silent lookup lets discovery keep looking for as long as anything is missing, which
/// is the behaviour the problem actually needs.</para>
///
/// <para><b>IF THE FIELD IS EVER RENAMED</b> the reflection fails once, says so once, and every
/// call falls back to <c>GetPropObject</c> — i.e. to exactly today's behaviour, warnings and all.
/// A resolver that cannot read the cache must not be a resolver that cannot resolve.</para>
///
/// <para><b>NOTHING IS WRITTEN.</b> This reads one private dictionary and never mutates it, never
/// adds a prop the game did not spawn, and never touches game state. Local-only, so no multiplayer
/// surface: a peer runs its own copy of this over its own cache.</para>
/// </summary>
internal static class PropVisualLookup
{
    private const string Scope = "FigureGrab";

    /// <summary>How the last resolve answered — see the class note. Reported by the census.</summary>
    internal enum Route
    {
        /// <summary>The game's own reference key hit.</summary>
        Reference,

        /// <summary>The reference key missed and the prop's <c>InstanceName</c> found it anyway —
        /// i.e. the cache has been re-keyed under this prop.</summary>
        InstanceName,

        /// <summary>Not in the cache under any key: not spawned yet, or gone.</summary>
        None,
    }

    private static FieldInfo? _cacheField;
    private static bool _cacheFieldMissing;
    private static bool _loggedFieldMissing;
    private static bool _loggedRekey;

    /// <summary>The last dictionary this index was built from, and the count it had. Reference plus
    /// count, because the service is a Singleton that survives scenarios: a new scenario gives the
    /// same dictionary a different population rather than a new dictionary.</summary>
    private static object? _indexedFrom;

    private static int _indexedCount = -1;

    /// <summary><c>InstanceName</c> → visual, rebuilt from the cache's keys whenever its count
    /// changes. Instance names are unique by construction (each carries the prop's GUID); a
    /// duplicate would mean two live props with one identity, and the first is kept because the
    /// alternative is to pick arbitrarily and then be wrong silently.</summary>
    private static readonly Dictionary<string, GameObject> ByInstanceName = new(32);

    /// <summary>How many props have resolved through the re-key route since the scenario started —
    /// the census prints it, because "the game's cache has been re-keyed" is the one fact that
    /// explains a whole board going unliftable and there is no other line that can say it.</summary>
    internal static int RekeyedThisScenario { get; private set; }

    /// <summary>Scenario teardown: the index is rebuilt on demand, the counter is per scenario.
    /// Deliberately does NOT reset <see cref="_loggedFieldMissing"/> — a missing field is a build
    /// fact, not a scenario fact, and re-logging it per scenario would be noise.</summary>
    internal static void Reset()
    {
        _indexedFrom = null;
        _indexedCount = -1;
        ByInstanceName.Clear();
        RekeyedThisScenario = 0;
        _loggedRekey = false;
    }

    /// <summary>
    /// The visual for <paramref name="prop"/>, or <c>null</c> with <paramref name="route"/> =
    /// <see cref="Route.None"/>. Never logs a warning of the game's own, and never throws: any
    /// reflection or dictionary failure degrades to the game's own <c>GetPropObject</c>.
    /// </summary>
    internal static GameObject? Resolve(CObjectProp? prop, out Route route)
    {
        route = Route.None;
        if (prop == null)
            return null;

        Dictionary<CObjectProp, GameObject>? cache = Cache();
        if (cache == null)
            return FallBackToTheGame(prop, ref route);

        // The key can be present with a DESTROYED value - Unity's fake null. That is not a
        // reference hit, and reporting it as one would put "the game's own reference key" beside a
        // null visual in the census, which is the shape of a line that answers the wrong question.
        // Fall through instead: the InstanceName index skips destroyed values too, so this ends at
        // Route.None, which is the true answer.
        if (cache.TryGetValue(prop, out GameObject direct) && direct != null)
        {
            route = Route.Reference;
            return direct;
        }

        string? name = prop.InstanceName;
        if (string.IsNullOrEmpty(name))
            return null;

        EnsureIndex(cache);
        if (!ByInstanceName.TryGetValue(name!, out GameObject byName) || byName == null)
            return null;

        route = Route.InstanceName;
        RekeyedThisScenario++;
        ReportRekeyOnce(prop, cache.Count);
        return byName;
    }

    /// <summary>Human-readable route for a log line.</summary>
    internal static string Describe(Route route) => route switch
    {
        Route.Reference => "the game's own reference key",
        Route.InstanceName => "its InstanceName, because the cache has been RE-KEYED under it",
        _ => "nothing — it is not in the cache under any key",
    };

    private static Dictionary<CObjectProp, GameObject>? Cache()
    {
        if (_cacheFieldMissing)
            return null;
        if (!Singleton<ObjectCacheService>.IsInitialized)
            return null;
        ObjectCacheService service = Singleton<ObjectCacheService>.Instance;
        if (service == null)
            return null;
        try
        {
            _cacheField ??= typeof(ObjectCacheService).GetField(
                "_propsCache", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_cacheField == null)
            {
                _cacheFieldMissing = true;
                ReportFieldMissing("the field does not exist on this build");
                return null;
            }
            return _cacheField.GetValue(service) as Dictionary<CObjectProp, GameObject>;
        }
        catch (Exception ex)
        {
            _cacheFieldMissing = true;
            ReportFieldMissing($"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void EnsureIndex(Dictionary<CObjectProp, GameObject> cache)
    {
        if (ReferenceEquals(cache, _indexedFrom) && cache.Count == _indexedCount)
            return;
        _indexedFrom = cache;
        _indexedCount = cache.Count;
        ByInstanceName.Clear();
        foreach (KeyValuePair<CObjectProp, GameObject> kv in cache)
        {
            if (kv.Key == null || kv.Value == null)
                continue;
            string? key = kv.Key.InstanceName;
            if (string.IsNullOrEmpty(key))
                continue;
            // First wins — see the field note. A second entry under one identity is not a tie this
            // code is allowed to break by iteration order.
            if (!ByInstanceName.ContainsKey(key!))
                ByInstanceName[key!] = kv.Value;
        }
    }

    /// <summary>The behaviour before this class existed, kept for the one case it is right for: the
    /// cache could not be read at all. Warnings and all — the game's own miss line is worth more
    /// than a silent null when the alternative is no lookup.</summary>
    private static GameObject? FallBackToTheGame(CObjectProp prop, ref Route route)
    {
        if (!Singleton<ObjectCacheService>.IsInitialized)
            return null;
        GameObject? go = Singleton<ObjectCacheService>.Instance.GetPropObject(prop);
        if (go != null)
            route = Route.Reference;
        return go;
    }

    private static void ReportFieldMissing(string why)
    {
        if (_loggedFieldMissing)
            return;
        _loggedFieldMissing = true;
        VRLog.Alert(Scope, "[Props] PROP CACHE UNREADABLE: ObjectCacheService._propsCache could not "
            + $"be read ({why}), so prop discovery falls back to the game's own GetPropObject — "
            + "which is keyed by REFERENCE and therefore stops answering for every prop the moment "
            + "a state sync re-keys the scenario's CObjectProp instances. That is the 2026-09-03 "
            + "'no prop can be grabbed and none highlights' report. Grabbing still works while the "
            + "keys hold; it will stop for the rest of the scenario the first time they do not, and "
            + "the [Props] census line is where that shows.");
    }

    private static void ReportRekeyOnce(CObjectProp prop, int cacheCount)
    {
        if (_loggedRekey)
            return;
        _loggedRekey = true;
        // HW-VERIFY: this line is the proof of the 2026-09-03 cause. It must stay at a tier the
        // DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note(Scope, $"[Props] PROP CACHE RE-KEYED: '{prop.InstanceName}' is NOT in "
            + $"ObjectCacheService._propsCache under its own reference, but its InstanceName is — "
            + $"the cache holds {cacheCount} entr(y/ies) written at spawn and keyed by object "
            + "identity, and the scenario state has since handed back FRESH CObjectProp instances "
            + "for the same board. Every GetPropObject(prop) call in the game would miss from here "
            + "on; this prop resolved through its InstanceName instead. THIS IS THE 2026-09-03 "
            + "REPORT ('weder kann ich nun irgendeinen der props greifen, noch kommt das "
            + "highlighting'): with only the reference route, the registry stays empty for the rest "
            + "of the scenario and every prop loses its glow, its collider, its ghost and its card "
            + "at once. Logged once per scenario; the [Props] census counts the rest.");
    }
}
