using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Local render-density trade for the Steam Frame's large procedural scenarios, authorised by the
/// maintainer on 2026-10-01. Only collider-free floor-cover grass foliage beneath the game's
/// <c>PCG_FR_Floor_Grass_Hex_*</c> generator is eligible. Build 599 measured these generators with
/// two or three renderers each: every member must be named grass before any foliage leaf is
/// budgeted, and a non-foliage grass base stays visible. The same log proves that a Foliage
/// shader alone is unsafe: a grabbable ThreeHexObstacle contains two
/// <c>FR_Floor_Scatter_Grass_Medium_03</c> meshes on <c>Amp_Basic_Foliage</c>, and WallSegmentFade
/// owns wall-side foliage on that same shader. No tree, wall, bush, prop unit, actor, collider,
/// light, preview, door or UI is admitted by this rule. Cave crystals/stalagmites are intentionally
/// excluded until an equally narrow native hierarchy and obstacle veto are evidenced.
///
/// Only <see cref="Renderer.forceRenderingOff"/> is written, and only if it was false when this
/// driver took ownership. Every owned write is reversible on density 100, scene change and teardown.
/// Native objects, colliders, scripts, materials and property blocks continue untouched. This is a
/// local visual compromise, not a game-state or multiplayer-wire change. Hardware FPS improvement
/// is a hypothesis until measured in a matching headset A/B.
/// </summary>
internal static class ScenarioSceneryBudget
{
    private const string Scope = "Perf";
    private const string GeneratorPrefix = "PCG_FR_Floor_Grass_Hex_";
    private const string GrassPrefix = "FR_Floor_Grass_";
    private const string ScatterPrefix = "FR_Floor_Scatter_Grass_";
    private const string GrassShader = "Amp_Basic_Foliage";
    private const int NodesPerFrame = 96;
    private const int RetunesPerFrame = 96;
    private const int NamedDebugCap = 8;

    private static Driver? _driver;

    internal static void Install(GameObject root)
    {
        if (_driver != null)
            return;
        _driver = root.AddComponent<Driver>();
        try
        {
            if (VRSession.Harmony != null)
            {
                VRSession.Harmony.PatchAll(typeof(ProceduralBase_ContentPlaced_SceneryBudgetPatch));
                VRSession.Harmony.PatchAll(typeof(ProceduralMapTile_ShowContent_SceneryBudgetPatch));
            }
            else
                VRLog.Note(Scope, "Scenario scenery budget: placement hooks unavailable; existing "
                                  + "tiles are still sampled at scene entry and setting changes.");
        }
        catch (Exception e)
        {
            VRLog.Note(Scope, $"Scenario scenery budget: placement hook unavailable ({e.Message}); "
                              + "existing tiles are still sampled at scene entry and setting changes.");
        }
    }

    internal static void Shutdown()
    {
        if (_driver == null)
            return;
        _driver.RestoreAll();
        UnityEngine.Object.Destroy(_driver);
        _driver = null;
    }

    /// <summary>Patch callbacks only enqueue; no content walk runs inside native placement.</summary>
    internal static void ContentPlaced(ProceduralBase entity)
    {
        if (entity is ProceduralMapTile tile)
            _driver?.QueueTile(tile);
    }

    internal static void ContentShown(GameObject root)
    {
        if (root != null)
            _driver?.QueueTile(root.GetComponentInParent<ProceduralMapTile>());
    }

    private enum Verdict : byte
    {
        Eligible, Name, Generator, Ancestry, Composite, ColliderOrEffect, Shader, Geometry,
    }

    /// <summary>Pure final gate; keeping the terms together makes a broad shader-only edit fail
    /// the focused runtime negative controls before it can ship.</summary>
    private static Verdict Judge(
        bool name, bool generator, bool ancestrySafe, bool grassOnlyUnit, bool noColliderOrEffect,
        bool foliageShader, bool floorCoverBounds)
    {
        if (!name) return Verdict.Name;
        if (!generator) return Verdict.Generator;
        if (!ancestrySafe) return Verdict.Ancestry;
        if (!grassOnlyUnit) return Verdict.Composite;
        if (!noColliderOrEffect) return Verdict.ColliderOrEffect;
        if (!foliageShader) return Verdict.Shader;
        if (!floorCoverBounds) return Verdict.Geometry;
        return Verdict.Eligible;
    }

    private static bool ShouldHide(uint hash, int densityPercent) =>
        densityPercent < 100 && hash % 100u >= (uint)densityPercent;

    private sealed class Record
    {
        internal MeshRenderer Renderer = null!;
        internal Transform[] Chain = null!;
        internal int Id;
        internal uint Hash;
        internal bool Owned;
        internal bool Invalidated;
    }

    /// <summary>Own only false→true writes. Restoring an already-forced renderer would take an
    /// unrelated owner's mask away; a renderer we did not change is never in our restore set.</summary>
    private static void SetHidden(Record record, bool hide)
    {
        MeshRenderer renderer = record.Renderer;
        if (renderer == null)
        {
            record.Owned = false;
            return;
        }
        if (hide)
        {
            if (!record.Owned && !renderer.forceRenderingOff)
            {
                renderer.forceRenderingOff = true;
                record.Owned = true;
            }
        }
        else if (record.Owned)
        {
            if (renderer.forceRenderingOff)
                renderer.forceRenderingOff = false;
            record.Owned = false;
        }
    }

    private static uint StableHash(Transform unit, ProceduralMapTile tile)
    {
        // FNV-1a over the tile/unit path and sibling positions. No Unity instance IDs: Apparance
        // destroys and recreates generated content, and the same placement should keep its budget.
        uint hash = 2166136261u;
        for (Transform? node = unit; node != null && node != tile.transform; node = node.parent)
        {
            string name = node.name;
            for (int i = 0; i < name.Length; i++)
                hash = (hash ^ name[i]) * 16777619u;
            hash = (hash ^ (uint)node.GetSiblingIndex()) * 16777619u;
        }
        string tileName = tile.name;
        for (int i = 0; i < tileName.Length; i++)
            hash = (hash ^ tileName[i]) * 16777619u;
        return hash;
    }

    /// <summary>Inspect only a named grass renderer. A PCG floor-grass unit may have several
    /// leaves, but every renderer must be a named grass mesh. Only Foliage leaves are budgeted;
    /// a differently shaded ground/base member is retained. All ancestor checks run on the
    /// actual chain even when a room is inactive (GetComponentInParent would miss it).
    /// </summary>
    private static Verdict Classify(MeshRenderer renderer, ProceduralMapTile tile,
                                    out Transform? unit)
    {
        unit = null;
        string name = renderer.name;
        bool named = IsGrassName(name);
        if (!named)
            return Verdict.Name;

        bool generatedContent = false;
        bool safeAncestry = !FigureRendererGuard.IsFigureOrActorRenderer(renderer);
        bool reachedTile = false;
        bool insideUnit = true;
        for (Transform? t = renderer.transform; t != null; t = t.parent)
        {
            if (t == tile.transform)
            {
                reachedTile = true;
                break;
            }
            // A procedural wall above Generated Content is a known parent of floor cover
            // (Build 599: Walls/Wall 3/Generated Content/PCG_FR_Floor_Grass_Hex_Half_PR).
            // A wall/door/prop component INSIDE that floor-cover unit is a different object.
            if (t.GetComponent<ProceduralProp>() != null
                || t.GetComponent<ProceduralDoorway>() != null
                || t.GetComponent<UnityGameEditorDoorProp>() != null
                || (insideUnit && t.GetComponent<ProceduralWall>() != null))
                safeAncestry = false;
            if (t.name == "Preview" || FigureRendererGuard.CarriesFigureComponent(t))
                safeAncestry = false;
            if (t.name == "Generated Content")
                generatedContent = true;
            if (t.name.StartsWith(GeneratorPrefix, StringComparison.Ordinal))
            {
                unit = t;
                insideUnit = false;
            }
        }
        if (!reachedTile || unit == null || !generatedContent)
            return Verdict.Generator;

        bool grassOnly = IsGrassOnlyUnit(unit.GetComponentsInChildren<Renderer>(includeInactive: true));
        bool noEffects = unit.GetComponentInChildren<Collider>(includeInactive: true) == null
                         && unit.GetComponentInChildren<Light>(includeInactive: true) == null
                         && unit.GetComponentInChildren<Animator>(includeInactive: true) == null
                         && unit.GetComponentInChildren<ParticleSystem>(includeInactive: true) == null
                         && unit.GetComponentInChildren<Canvas>(includeInactive: true) == null;
        Material[] materials = renderer.sharedMaterials;
        bool shader = materials.Length == 1 && materials[0] != null
                      && materials[0].shader != null
                      && materials[0].shader.name == GrassShader;
        Vector3 size = renderer.bounds.size;
        bool floorBounds = size.y > 0f && size.y <= 0.60f
                           && size.x > 0f && size.x <= 3f
                           && size.z > 0f && size.z <= 3f;
        return Judge(named, true, safeAncestry, grassOnly, noEffects, shader, floorBounds);
    }

    private static bool IsGrassName(string name) =>
        name.StartsWith(GrassPrefix, StringComparison.Ordinal)
        || name.StartsWith(ScatterPrefix, StringComparison.Ordinal);

    private static bool IsGrassOnlyUnit(Renderer[] members)
    {
        if (members.Length < 1 || members.Length > 8)
            return false;
        for (int i = 0; i < members.Length; i++)
            if (members[i] is not MeshRenderer m || !IsGrassName(m.name)
                || m.GetComponent<MeshFilter>()?.sharedMesh == null)
                return false;
        return true;
    }

    private static Transform[] CaptureChain(Transform leaf, ProceduralMapTile tile)
    {
        var nodes = new List<Transform>(8);
        for (Transform? t = leaf; t != null; t = t.parent)
        {
            nodes.Add(t);
            if (t == tile.transform)
                break;
        }
        return nodes.ToArray();
    }

    /// <summary>Cheap per-frame safety over OWNED renderers only. A grab or hierarchy rebuild
    /// reparents a node; all links must still be exactly the ones admitted at classification.
    /// The held-prop test catches ownership changes before any scene traversal is needed.</summary>
    private static bool StillOnOriginalChain(Record record)
    {
        MeshRenderer renderer = record.Renderer;
        if (renderer == null || FigureRendererGuard.HeldByPlayer(renderer))
            return false;
        Transform? t = renderer.transform;
        for (int i = 0; i < record.Chain.Length; i++)
        {
            if (t == null || !ReferenceEquals(t, record.Chain[i]))
                return false;
            t = t.parent;
        }
        return true;
    }

    private sealed class Driver : MonoBehaviour
    {
        private readonly struct Node
        {
            internal readonly Transform Transform;
            internal readonly ProceduralMapTile Tile;
            internal Node(Transform transform, ProceduralMapTile tile)
            {
                Transform = transform;
                Tile = tile;
            }
        }

        private readonly Queue<ProceduralMapTile> _pending = new();
        private readonly HashSet<int> _pendingIds = new();
        private readonly Queue<Node> _nodes = new();
        private readonly List<ProceduralMapTile> _tiles = new(64);
        private readonly List<Record> _records = new(1024);
        private readonly Dictionary<int, Record> _byId = new(1024);
        private readonly HashSet<int> _tileIds = new();
        private readonly int[] _rejected = new int[8];
        private int _sceneHandle = int.MinValue;
        private int _density = 100;
        private int _retuneIndex = -1;
        private int _pruneIndex;
        private int _visitedNodes;
        private int _meshRenderers;
        private int _debugNames;
        private bool _inScenarioScene;
        private bool _actualScenario;
        private bool _summaryPrinted;
        private float _summaryDue;

        internal void QueueTile(ProceduralMapTile? tile)
        {
            if (!_inScenarioScene || _density >= 100 || tile == null)
                return;
            if (tile.GetComponentInParent<ProceduralScenario>() == null)
                return;
            _actualScenario = true;
            int id = tile.GetInstanceID();
            if (_pendingIds.Add(id))
                _pending.Enqueue(tile);
            _summaryDue = Mathf.Max(_summaryDue, Time.unscaledTime + 1f);
        }

        private void Update()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (_sceneHandle != scene.handle
                || _inScenarioScene != (VRSession.IsRunning && scene.name == "ProcGen"))
                EnterScene(scene);

            int wanted = Mathf.Clamp(PerfConfig.ScenarioSceneryDensityPercentValue, 0, 100);
            if (_density != wanted)
                ChangeDensity(wanted);
            if (!_inScenarioScene)
                return;

            RecheckOwned();
            PruneDead(16);
            Retune();
            if (_density < 100)
                WalkNodes();
            MaybeReport();
        }

        private void EnterScene(Scene scene)
        {
            RestoreAll();
            _sceneHandle = scene.handle;
            _inScenarioScene = VRSession.IsRunning && scene.name == "ProcGen";
            _density = 100;
            _actualScenario = false;
            _summaryPrinted = false;
            _summaryDue = Time.unscaledTime + 3f;
        }

        private void ChangeDensity(int wanted)
        {
            int previous = _density;
            _density = wanted;
            _retuneIndex = 0;
            _summaryPrinted = false;
            _summaryDue = Time.unscaledTime + 2f;
            if (wanted == 100)
            {
                _pending.Clear();
                _pendingIds.Clear();
                _nodes.Clear();
            }
            else if (previous == 100 && _inScenarioScene)
                SeedTiles();
        }

        private void SeedTiles()
        {
            // One registry read at scene entry or a 100→lower dial edge, never a per-frame scene
            // search. The registry includes live tiles; ShowContent catches an inactive room later.
            SceneRegistry.MapTiles.Collect(_tiles);
            for (int i = 0; i < _tiles.Count; i++)
                QueueTile(_tiles[i]);
            _tiles.Clear();
        }

        private void WalkNodes()
        {
            int budget = NodesPerFrame;
            while (budget-- > 0)
            {
                if (_nodes.Count == 0)
                {
                    if (_pending.Count == 0)
                        break;
                    ProceduralMapTile tile = _pending.Dequeue();
                    if (tile == null)
                        continue;
                    _pendingIds.Remove(tile.GetInstanceID());
                    if (tile.GetComponentInParent<ProceduralScenario>() == null)
                        continue;
                    _tileIds.Add(tile.GetInstanceID());
                    _nodes.Enqueue(new Node(tile.transform, tile));
                }

                Node node = _nodes.Dequeue();
                Transform t = node.Transform;
                if (t == null || node.Tile == null)
                    continue;
                _visitedNodes++;
                MeshRenderer? renderer = t.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    _meshRenderers++;
                    Examine(renderer, node.Tile);
                }
                for (int child = 0; child < t.childCount; child++)
                    _nodes.Enqueue(new Node(t.GetChild(child), node.Tile));
            }
        }

        private void Examine(MeshRenderer renderer, ProceduralMapTile tile)
        {
            int id = renderer.GetInstanceID();
            if (_byId.TryGetValue(id, out Record? known))
            {
                if (known.Renderer != null)
                    return;
                _records.Remove(known);
                _byId.Remove(id);
                if (_retuneIndex >= 0)
                    _retuneIndex = 0;
            }

            Verdict verdict = Classify(renderer, tile, out Transform? unit);
            _rejected[(int)verdict]++;
            if (verdict != Verdict.Eligible || unit == null)
                return;

            var record = new Record
            {
                Renderer = renderer,
                Chain = CaptureChain(renderer.transform, tile),
                Id = id,
                Hash = StableHash(unit, tile),
            };
            _records.Add(record);
            _byId.Add(id, record);
            SetHidden(record, ShouldHide(record.Hash, _density));
            if (_debugNames < NamedDebugCap && VRLog.WantsDebug)
            {
                _debugNames++;
                VRLog.Debug(Scope, "Scenario scenery candidate " + PathOf(renderer.transform, tile.transform)
                                   + $" hash={record.Hash} forceRenderingOff={renderer.forceRenderingOff}");
            }
        }

        private void Retune()
        {
            if (_retuneIndex < 0)
                return;
            int end = Math.Min(_records.Count, _retuneIndex + RetunesPerFrame);
            for (; _retuneIndex < end; _retuneIndex++)
            {
                Record record = _records[_retuneIndex];
                if (!record.Invalidated)
                    SetHidden(record, ShouldHide(record.Hash, _density));
            }
            if (_retuneIndex < _records.Count)
                return;
            _retuneIndex = -1;
            if (_density == 100)
            {
                _records.Clear();
                _byId.Clear();
            }
        }

        private void RecheckOwned()
        {
            for (int i = 0; i < _records.Count; i++)
            {
                Record record = _records[i];
                if (!record.Owned || StillOnOriginalChain(record))
                    continue;
                SetHidden(record, false);
                record.Invalidated = true;
            }
        }

        /// <summary>Apparance replaces generated renderer instances. Prune only our small record
        /// list, at most sixteen entries per frame, so destroyed clones cannot accumulate for a
        /// long session. Never mutate list indices while a setting retune is in progress.</summary>
        private void PruneDead(int budget)
        {
            if (_retuneIndex >= 0 || _records.Count == 0)
                return;
            while (budget-- > 0 && _records.Count > 0)
            {
                if (_pruneIndex >= _records.Count)
                    _pruneIndex = 0;
                Record record = _records[_pruneIndex];
                if (record.Renderer != null)
                {
                    _pruneIndex++;
                    continue;
                }
                if (_byId.TryGetValue(record.Id, out Record? mapped)
                    && ReferenceEquals(mapped, record))
                    _byId.Remove(record.Id);
                int last = _records.Count - 1;
                _records[_pruneIndex] = _records[last];
                _records.RemoveAt(last);
            }
        }

        private void MaybeReport()
        {
            if (_summaryPrinted || Time.unscaledTime < _summaryDue || _retuneIndex >= 0
                || _pending.Count != 0 || _nodes.Count != 0 || !_actualScenario)
                return;
            int actuallyForced = 0;
            int owned = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                Record record = _records[i];
                if (record.Renderer == null)
                    continue;
                if (record.Renderer.forceRenderingOff)
                    actuallyForced++;
                if (record.Owned)
                    owned++;
            }
            VRLog.Note(Scope, $"Scenario scenery budget: density {_density}% in ProcGen; "
                              + $"{_tileIds.Count} tile(s), {_visitedNodes} node(s), "
                              + $"{_meshRenderers} mesh renderer(s) visited, "
                              + $"{_records.Count} eligible foliage leaf renderer(s), "
                              + $"{owned} owned and {actuallyForced} actually forceRenderingOff. "
                              + $"Rejected named candidates: no generator {_rejected[(int)Verdict.Generator]}, "
                              + $"actor/preview {_rejected[(int)Verdict.Ancestry]}, "
                              + $"composite {_rejected[(int)Verdict.Composite]}, "
                              + $"collider/effect {_rejected[(int)Verdict.ColliderOrEffect]}, "
                              + $"shader {_rejected[(int)Verdict.Shader]}, "
                              + $"bounds {_rejected[(int)Verdict.Geometry]}. "
                              + "Decorative rendering only; headset frame gain is unverified.");
            _summaryPrinted = true;
        }

        internal void RestoreAll()
        {
            for (int i = 0; i < _records.Count; i++)
                SetHidden(_records[i], false);
            _records.Clear();
            _byId.Clear();
            _pending.Clear();
            _pendingIds.Clear();
            _nodes.Clear();
            _tileIds.Clear();
            Array.Clear(_rejected, 0, _rejected.Length);
            _visitedNodes = 0;
            _meshRenderers = 0;
            _debugNames = 0;
            _retuneIndex = -1;
            _pruneIndex = 0;
        }

        private static string PathOf(Transform leaf, Transform stop)
        {
            var parts = new List<string>(8);
            for (Transform? t = leaf; t != null && t != stop; t = t.parent)
                parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}

/// <summary>Pure notification; the driver does the bounded traversal outside the game callback.</summary>
[HarmonyPatch(typeof(ProceduralBase), nameof(ProceduralBase.NotifyContentPlacementComplete))]
internal static class ProceduralBase_ContentPlaced_SceneryBudgetPatch
{
    private static void Postfix(ProceduralBase __instance)
    {
        try { ScenarioSceneryBudget.ContentPlaced(__instance); }
        catch { /* no render-budget fault may escape into native placement */ }
    }
}

/// <summary>Generated Content may already exist but be inactive until a room is revealed.</summary>
[HarmonyPatch(typeof(ProceduralMapTile), nameof(ProceduralMapTile.ShowContent))]
internal static class ProceduralMapTile_ShowContent_SceneryBudgetPatch
{
    private static void Postfix(GameObject o)
    {
        try { ScenarioSceneryBudget.ContentShown(o); }
        catch { /* no render-budget fault may escape into room reveal */ }
    }
}
