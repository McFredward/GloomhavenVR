using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Board.FigureGrab;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Optional scenario-figure compromise requested on 2026-10-02, separately from wall/scenery
/// visibility. The supplied native heroes and drakes have real authored three-level LODs.
/// Replace only earlier levels' renderer references with those from a coarser ORIGINAL level;
/// original screen-height transitions, fade widths, far culling, bone animation, materials,
/// collision and gameplay stay intact. Original fine renderers no longer referenced by the capped
/// table get owned render-only masks so they cannot render as orphaned doubles. At 100% the exact original LOD table is restored.
/// This is a mesh-detail cap, not permission to hide random limbs, weapons or whole actors.
/// The supplied distant Frame figures already resolve to LOD2: no measured gain is claimed.
///
/// Held figures (both local and remote) obey the saved mesh detail and cloth OFF choices,
/// as explicitly requested on 2026-10-02. Neither
/// map/NPC actors, standalone effects nor UI are admitted. Discovery runs on scene/loading
/// edges and native SetActor, with bounded queues rather than repeated scene-wide walks.
/// </summary>
internal static class ScenarioFigureDetailBudget
{
    private const string Scope = "Perf";
    private const int ActorsPerFrame = 2;
    private const int LoadingActorsPerFrame = 16;
    private static Driver? _driver;
    private static readonly FieldInfo? NativeRoot = typeof(ActorBehaviour).GetField(
        "m_RootGameObject", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? ForcingPosition = typeof(ActorBehaviour).GetField(
        "m_ForcingPositionChangeCounter", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    internal static void Install(GameObject host)
    {
        if (_driver != null) return;
        _driver = host.AddComponent<Driver>();
        try
        {
            if (VRSession.Harmony != null)
                VRSession.Harmony.PatchAll(typeof(ActorBehaviour_SetActor_FigureDetailPatch));
        }
        catch (Exception error)
        {
            VRLog.Note(Scope, "Scenario figure detail: actor-ready hook unavailable ("
                              + error.Message + "); scene and loading edges still discover native figures.");
        }
    }

    internal static void Shutdown()
    {
        if (_driver == null) return;
        _driver.RestoreAll();
        UnityEngine.Object.Destroy(_driver);
        _driver = null;
    }

    private static bool BudgetActive => PerfConfig.PlayerFigureDetailPercent < 100
        || PerfConfig.EnemyFigureDetailPercent < 100 || !PerfConfig.FigureClothSimulationEnabled;

    internal static void ActorReady(GameObject root)
    {
        if (VRSession.IsRunning && BudgetActive) _driver?.QueueRoot(root);
    }

    private static int ActorDetail(ActorBehaviour actor)
    {
        CActor native = actor.Actor;
        if (native == null) return 100;
        return native.Type is CActor.EType.Player or CActor.EType.HeroSummon
            ? PerfConfig.PlayerFigureDetailPercent
            : native.IsMonsterType ? PerfConfig.EnemyFigureDetailPercent : 100;
    }

    private static bool SameTable(LOD[] left, LOD[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i].screenRelativeTransitionHeight != right[i].screenRelativeTransitionHeight
                || left[i].fadeTransitionWidth != right[i].fadeTransitionWidth
                || left[i].renderers.Length != right[i].renderers.Length) return false;
            for (int j = 0; j < left[i].renderers.Length; j++)
                if (left[i].renderers[j] != right[i].renderers[j]) return false;
        }
        return true;
    }

    private static int VertexCount(LOD level, Transform root)
    {
        if (level.renderers == null || level.renderers.Length == 0) return -1;
        int vertices = 0;
        foreach (Renderer renderer in level.renderers)
        {
            if (renderer == null || !renderer.transform.IsChildOf(root)
                || renderer.GetComponentInParent<Canvas>(true) != null) return -1;
            Mesh? mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0) return -1;
            vertices += mesh.vertexCount;
        }
        return vertices;
    }

    private sealed class LodRecord
    {
        internal LODGroup Group = null!;
        internal LOD[] Original = null!;
        internal LOD[]? Applied;
        internal int[] Levels = null!;
        internal int[] LevelVertices = null!;
        internal int Selected;
        internal bool Foreign;
        private readonly List<Renderer> _masked = new();

        private void RestoreMasks()
        {
            foreach (Renderer renderer in _masked)
                if (renderer != null && renderer.forceRenderingOff) renderer.forceRenderingOff = false;
            _masked.Clear();
        }

        private void MaskOmittedRenderers(LOD[] applied)
        {
            RestoreMasks();
            var retained = new HashSet<Renderer>();
            foreach (LOD level in applied)
                foreach (Renderer renderer in level.renderers) retained.Add(renderer);
            var inspected = new HashSet<Renderer>();
            foreach (LOD level in Original)
                foreach (Renderer renderer in level.renderers)
                    if (renderer != null && inspected.Add(renderer) && !retained.Contains(renderer)
                        && !renderer.forceRenderingOff)
                    { renderer.forceRenderingOff = true; _masked.Add(renderer); }
        }

        internal void Apply(int detail)
        {
            if (Group == null) { RestoreMasks(); return; }
            if (Foreign) return;
            int selected = Levels[Mathf.RoundToInt((100 - Mathf.Clamp(detail, 0, 100))
                * (Levels.Length - 1) / 100f)];
            if (selected == Selected) return;
            // Never overwrite a table another controller installed after our own write.
            LOD[] current = Group.GetLODs();
            if (!SameTable(current, Applied ?? Original))
            {
                RestoreMasks();
                Foreign = true;
                Applied = null;
                return;
            }
            LOD[] levels = (LOD[])Original.Clone();
            for (int i = 0; i < selected; i++) levels[i].renderers = Original[selected].renderers;
            MaskOmittedRenderers(levels);
            Group.SetLODs(levels);
            Applied = selected > 0 ? levels : null;
            Selected = selected;
        }

        internal void CheckOwnership()
        {
            if (Foreign || Applied == null) return;
            if (Group == null || !SameTable(Group.GetLODs(), Applied))
            { RestoreMasks(); Foreign = true; Applied = null; }
        }

        internal void Restore()
        {
            try { Apply(100); }
            finally { RestoreMasks(); }
        }
    }

    private sealed class ClothRecord
    {
        internal Cloth Cloth = null!;
        internal bool Owned;
        internal void Apply(bool native, bool forcingPosition)
        {
            if (Cloth == null) { Owned = false; return; }
            if (!native)
            {
                if (Cloth.enabled) { Cloth.enabled = false; Owned = true; }
            }
            else if (Owned)
            {
                // Native ForceSetLocoIntermediateTarget disables cloth for two frames. An
                // optional graphics setting must never cancel that teleport/reset protocol.
                if (!forcingPosition && !Cloth.enabled) Cloth.enabled = true;
                Owned = false;
            }
        }
    }

    private sealed class ActorRecord
    {
        internal ActorBehaviour Actor = null!;
        internal int Id;
        internal GameObject Root = null!;
        internal readonly List<LodRecord> Lods = new();
        internal readonly List<ClothRecord> Clothes = new();
        internal int Detail = 100;
        internal bool ClothNative = true;
        internal void Apply(bool restore = false)
        {
            if (Actor == null || Root == null) return;
            int wanted = restore ? 100 : Mathf.Clamp(ActorDetail(Actor), 0, 100);
            bool cloth = restore || PerfConfig.FigureClothSimulationEnabled;
            if (Detail != wanted)
                foreach (LodRecord lod in Lods) lod.Apply(wanted);
            Detail = wanted; ClothNative = cloth;
            if (Clothes.Count > 0)
            {
                // A rescale cook may already have disabled a formerly simulating cloth when
                // OFF is chosen. Transfer that original-enable ownership before its native
                // reset check, otherwise ON later leaves the cloth permanently disabled.
                foreach (ClothRecord item in Clothes)
                    item.Owned |= FigureCloth.TakeDisabledSimulationOwnership(item.Cloth);
                bool needsRestore = false;
                if (cloth)
                    foreach (ClothRecord item in Clothes) if (item.Owned) { needsRestore = true; break; }
                bool forcing = needsRestore && ForcingPosition?.GetValue(Actor) is int count && count > 0;
                foreach (ClothRecord item in Clothes) item.Apply(cloth, forcing);
            }
        }
    }

    // Native ActorBehaviour.LateUpdate can re-enable every cloth after a teleport/reset.
    // Enforce the optional OFF choice after native animation and held-figure updates; merely
    // changing enabled at discovery lets the original two-frame reset restore simulation.
    // Stay before the +30000 profiling seam so this optional work remains measured as logic.
    [DefaultExecutionOrder(29900)]
    private sealed class Driver : MonoBehaviour
    {
        private readonly List<ActorRecord> _actors = new();
        private readonly HashSet<int> _seen = new();
        private readonly Queue<GameObject> _pending = new();
        private readonly HashSet<int> _queued = new();
        private readonly Dictionary<int, bool> _scenarioScopes = new();
        private readonly List<LodRecord> _lods = new();
        private int _ownershipCursor;
        private int _scene = int.MinValue;
        private bool _running, _wasLoading;
        private int _players = 100, _enemies = 100;
        private bool _cloth = true;
        private float _reportAt;
        private bool _reportPending;
        private bool _faulted;
        private int _rejectedScope, _nativeCandidates;
        private bool _scopeAnomalyReported;

        private bool IsScenarioActorRoot(GameObject root, Scene scene)
        {
            if (root.scene == scene) return true;
            ClientScenarioManager manager = ClientScenarioManager.s_ClientScenarioManager;
            GameObject board = manager != null ? manager.m_Board : null!;
            // Hardware603 reported zero actors although both dials and cloth OFF were active.
            // ProcGen is additive scenery, while Choreographer creates every model beneath
            // ClientScenarioManager.m_Board in the main Game scene. A scene-name equality
            // silently excludes those original native actors. Admit that exact board subtree,
            // with ActorBehaviour/CActor identity still checked below; never all Game models.
            if (board != null && root.transform.IsChildOf(board.transform)) return true;
            // A quality change may occur after every original actor is already in a hand.
            // Those registries are exact original actor identities, not a scene-name guess.
            ActorBehaviour actor = ActorBehaviour.GetActorBehaviour(root);
            return actor != null && (HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor));
        }

        internal void QueueRoot(GameObject root)
        {
            if (_faulted || root == null || !VRSession.IsRunning || !BudgetActive) return;
            // A renamed procedural scene can acquire its native scenario root after loading.
            _scenarioScopes.Remove(root.scene.handle);
            if (_queued.Add(root.GetInstanceID())) _pending.Enqueue(root);
        }

        private bool HasScenario(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            if (scene.name == "ProcGen") return true;
            if (_scenarioScopes.TryGetValue(scene.handle, out bool known)) return known;
            bool found = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponent<ProceduralScenario>() != null) { found = true; break; }
            _scenarioScopes[scene.handle] = found;
            return found;
        }

        private void Seed(Scene scene)
        {
            // Only loading/scene/config edges allocate this snapshot. Newly spawned actors
            // use SetActor; no FindObjectsOfType is performed during steady play.
            _nativeCandidates = 0;
            ClientScenarioManager manager = ClientScenarioManager.s_ClientScenarioManager;
            GameObject board = manager != null ? manager.m_Board : null!;
            foreach (ActorBehaviour actor in UnityEngine.Object.FindObjectsOfType<ActorBehaviour>(true))
            {
                CActor native = actor.Actor;
                if (native == null || (native.Type is not (CActor.EType.Player or CActor.EType.HeroSummon)
                    && !native.IsMonsterType)) continue;
                if (board != null && actor.gameObject.scene == board.scene) _nativeCandidates++;
                if (_seen.Contains(actor.GetInstanceID())) continue;
                GameObject root = NativeRoot?.GetValue(actor) as GameObject ?? actor.gameObject;
                if (IsScenarioActorRoot(root, scene)) QueueRoot(root);
            }
            _reportPending = true; _reportAt = Time.unscaledTime + 1f;
        }

        private void Update()
        {
            if (_faulted) return;
            try { UpdateCore(); }
            catch (Exception error) { Fail(error); }
        }

        private void UpdateCore()
        {
            // Shipped PC/original settings cost config reads only. No scene snapshots,
            // actor discovery, per-frame reflection or LOD/cloth writes run on this path.
            if (!VRSession.IsRunning || !BudgetActive)
            {
                if (_running || _actors.Count > 0 || _pending.Count > 0) RestoreAll();
                _running = false; _scene = int.MinValue;
                return;
            }
            SceneController controller = SceneController.Instance;
            bool loading = controller != null && (controller.IsLoading || controller.ScenarioIsLoading);
            if (_wasLoading && !loading) _scenarioScopes.Clear();
            Scene procedural = Choreographer.s_Choreographer != null
                ? Choreographer.s_Choreographer.m_ProcGenScene : default;
            Scene scene = HasScenario(procedural) ? procedural : SceneManager.GetActiveScene();
            bool running = VRSession.IsRunning && HasScenario(scene);
            if (_scene != scene.handle || _running != running)
            {
                RestoreAll(); _scene = scene.handle; _running = running;
                if (running) Seed(scene);
            }
            if (!running) { _pending.Clear(); _queued.Clear(); return; }
            int players = PerfConfig.PlayerFigureDetailPercent, enemies = PerfConfig.EnemyFigureDetailPercent;
            bool cloth = PerfConfig.FigureClothSimulationEnabled;
            if (_players != players || _enemies != enemies || _cloth != cloth)
            {
                _players = players; _enemies = enemies; _cloth = cloth;
                Seed(scene); _reportPending = true; _reportAt = Time.unscaledTime + 1f;
            }
            if (_wasLoading && !loading) Seed(scene);
            _wasLoading = loading;
            int budget = loading ? LoadingActorsPerFrame : ActorsPerFrame;
            while (budget-- > 0 && _pending.Count > 0)
            {
                GameObject root = _pending.Dequeue();
                if (root == null) continue;
                _queued.Remove(root.GetInstanceID());
                if (!IsScenarioActorRoot(root, scene)) { _rejectedScope++; continue; }
                Adopt(root);
            }
            for (int i = _actors.Count - 1; i >= 0; i--)
            {
                if (_actors[i].Actor != null && _actors[i].Root != null) continue;
                foreach (LodRecord lod in _actors[i].Lods) { lod.Restore(); _lods.Remove(lod); }
                _seen.Remove(_actors[i].Id);
                foreach (ClothRecord item in _actors[i].Clothes) item.Apply(true, false);
                _actors.RemoveAt(i);
            }
            if (_lods.Count > 0)
            {
                if (_ownershipCursor >= _lods.Count) _ownershipCursor = 0;
                _lods[_ownershipCursor++].CheckOwnership(); // at most one allocating native read per frame
            }
            if (_reportPending && !loading && _pending.Count == 0 && Time.unscaledTime >= _reportAt)
            {
                _reportPending = false;
                if (_actors.Count == 0 && _nativeCandidates > 0 && !_scopeAnomalyReported)
                {
                    _scopeAnomalyReported = true;
                    VRLog.Note(Scope, "Scenario figure detail: native board has bound figure identities "
                        + "but the optional driver adopted none after loading; original figures remain "
                        + "playable, configured detail/cloth reductions were not applied.");
                }
                if (VRLog.WantsDebug)
                {
                    int lods = 0, changed = 0, clothes = 0, coarseActors = 0, nativeVertices = 0, chosenVertices = 0;
                    foreach (ActorRecord record in _actors)
                    {
                        lods += record.Lods.Count;
                        if (record.Lods.Count > 0) coarseActors++;
                        foreach (LodRecord lod in record.Lods)
                        {
                            if (lod.Selected > 0) changed++;
                            nativeVertices += lod.LevelVertices[0];
                            chosenVertices += lod.LevelVertices[lod.Selected];
                        }
                        foreach (ClothRecord item in record.Clothes) if (item.Owned) clothes++;
                    }
                    VRLog.Debug(Scope, $"Scenario figure detail: players={_players}% enemies={_enemies}% "
                        + $"nativeCloth={_cloth}; {_actors.Count} actor(s), {changed}/{lods} native LOD cap(s), "
                        + $"{clothes} cloth solver(s) disabled; {coarseActors}/{_actors.Count} actor(s) with authored coarse bodies; "
                        + "original/selected near-mesh vertices "
                        + $"{nativeVertices}/{chosenVertices}, rejected scope {_rejectedScope}. "
                        + "Native board models may live in Game while scenery lives in ProcGen; "
                        + "authored LODs may already be coarse at this view.");
                }
            }
        }

        private void Adopt(GameObject root)
        {
            ActorBehaviour actor = ActorBehaviour.GetActorBehaviour(root);
            if (actor == null || actor.Actor == null
                || (actor.Actor.Type is not (CActor.EType.Player or CActor.EType.HeroSummon)
                    && !actor.Actor.IsMonsterType) || !_seen.Add(actor.GetInstanceID())) return;
            var record = new ActorRecord { Actor = actor, Id = actor.GetInstanceID(), Root = root };
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!group.enabled) continue;
                // Nested native model controllers must never be attributed to this actor.
                ActorBehaviour other = group.GetComponentInParent<ActorBehaviour>(true);
                if (other != null && other != actor) continue;
                LOD[] levels = group.GetLODs();
                if (levels.Length < 2) continue;
                bool unsafeLevel = false;
                var vertices = new int[levels.Length];
                for (int i = 0; i < levels.Length; i++)
                {
                    vertices[i] = VertexCount(levels[i], root.transform);
                    if (levels[i].renderers != null && levels[i].renderers.Length > 0
                        && vertices[i] < 0) { unsafeLevel = true; break; }
                }
                if (unsafeLevel) continue;
                int full = vertices[0];
                var admitted = new List<int> { 0 };
                if (full <= 0) continue;
                for (int i = 1; i < levels.Length; i++)
                {
                    int coarse = vertices[i];
                    if (coarse > 0 && coarse < full) admitted.Add(i);
                }
                if (admitted.Count > 1) record.Lods.Add(new LodRecord
                    { Group = group, Original = levels, Levels = admitted.ToArray(), LevelVertices = vertices });
            }
            foreach (Cloth item in root.GetComponentsInChildren<Cloth>(true))
            {
                if (item.GetComponentInParent<Canvas>(true) != null) continue;
                ActorBehaviour other = item.GetComponentInParent<ActorBehaviour>(true);
                if (other == null || other == actor) record.Clothes.Add(new ClothRecord { Cloth = item });
            }
            _actors.Add(record); _lods.AddRange(record.Lods); record.Apply(); _reportPending = true;
            _reportAt = Mathf.Max(_reportAt, Time.unscaledTime + 1f);
        }

        private void LateUpdate()
        {
            if (_faulted || !_running) return;
            try
            {
                using var scope = PerfMonitor.Scope("FigureDetailBudget.Late");
                foreach (ActorRecord record in _actors) record.Apply();
            }
            catch (Exception error) { Fail(error); }
        }

        private void Fail(Exception error)
        {
            if (_faulted) return;
            _faulted = true;
            RestoreAll();
            enabled = false;
            VRLog.Note(Scope, "Scenario figure detail: optional driver stopped after a native "
                + "presentation fault (" + error.GetType().Name + ": " + error.Message
                + "); owned restoration attempted, native gameplay is untouched.");
        }

        internal void RestoreAll()
        {
            foreach (ActorRecord record in _actors)
            {
                try { record.Apply(true); } catch { /* restore each remaining owned part below */ }
                foreach (LodRecord lod in record.Lods)
                    try { lod.Restore(); } catch { /* destroyed native pieces cannot gate teardown */ }
                foreach (ClothRecord item in record.Clothes)
                    try
                    {
                        bool forcing = record.Actor != null
                            && ForcingPosition?.GetValue(record.Actor) is int count && count > 0;
                        item.Apply(true, forcing);
                    }
                    catch { /* restore the other native cloth components even if one disappeared */ }
            }
            _actors.Clear(); _seen.Clear(); _pending.Clear(); _queued.Clear();
            _lods.Clear(); _ownershipCursor = 0;
            _reportPending = false; _wasLoading = false; _rejectedScope = 0;
            _nativeCandidates = 0; _scopeAnomalyReported = false;
        }
        private void OnDestroy() => RestoreAll();
    }
}

/// <summary>Observe the completed native actor binding; gameplay and model initialization run first.</summary>
[HarmonyPatch(typeof(ActorBehaviour), nameof(ActorBehaviour.SetActor))]
internal static class ActorBehaviour_SetActor_FigureDetailPatch
{
    private static void Postfix(GameObject gameObject)
    {
        try { ScenarioFigureDetailBudget.ActorReady(gameObject); }
        catch { /* optional graphics may not escape into actor creation or scenario continuation */ }
    }
}
