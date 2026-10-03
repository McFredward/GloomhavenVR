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
/// Build606 adds offline topology-constrained ORIGINAL-vertex derivatives, including native
/// bodies with no authored LOD and beyond the authored far level. Shared immutable derivatives
/// preserve UV/skin/material topology; renderer slots restore exact originals at 100%. The
/// bank is prepared at loading/config discovery, never generated during a first grab.
/// Identified authored ambient FX have a separate density control. Debug evidence samples the
/// actual current shared mesh rather than a stale capture of the original vertex count.
/// Held figures (both local and remote) obey the saved mesh detail, effects and cloth OFF choices,
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
        try
        {
            if (VRSession.Harmony != null)
                VRSession.Harmony.PatchAll(typeof(MaterialLoaderData_CheckAllMaterialLoaded_FigureEffectsPatch));
        }
        catch (Exception error)
        {
            VRLog.Note(Scope, "Scenario figure effects: native material-ready hook unavailable ("
                              + error.Message + "); completed actor bindings still discover original ambient figures.");
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
        || PerfConfig.EnemyFigureDetailPercent < 100 || PerfConfig.FigureEffectsDensityPercent < 100
        || !PerfConfig.FigureClothSimulationEnabled;

    // A scalar stamp from the completed late enforcement pass. The monitor must not
    // census actors/meshes every frame merely to know when a quality change is applied.
    internal static bool MeasurementReady => _driver == null || _driver.MeasurementReady;

    /// <summary>Read the native fine mesh only while its exact renderer still belongs to our
    /// detail record. Pose envelopes must not change when a player chooses a derivative.</summary>
    internal static Mesh? OriginalMeshFor(Renderer renderer) => _driver?.OriginalMeshFor(renderer);
    internal static ScenarioFigureMeshBank.Record? OriginalRecordFor(Renderer renderer)
        => _driver?.OriginalRecordFor(renderer);

    internal static void ActorReady(GameObject root)
    {
        if (VRSession.IsRunning && BudgetActive) _driver?.QueueRoot(root);
    }
    internal static void MaterialReady(Renderer renderer)
    {
        if (VRSession.IsRunning && BudgetActive) _driver?.MaterialReady(renderer);
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
        internal int RootId;
        internal GameObject Root = null!;
        internal readonly List<LodRecord> Lods = new();
        internal readonly List<ClothRecord> Clothes = new();
        internal ScenarioFigureEffects.Record Effects = null!;
        internal readonly List<KeyValuePair<Renderer, int>> Meshes = new();
        internal readonly List<ScenarioFigureMeshBank.Record> MeshDetails = new();
        internal int Detail = 100;
        internal bool ClothNative = true;
        internal void Apply(bool restore = false)
        {
            if (Actor == null || Root == null) return;
            int wanted = restore ? 100 : Mathf.Clamp(ActorDetail(Actor), 0, 100);
            bool cloth = restore || PerfConfig.FigureClothSimulationEnabled;
            if (Detail != wanted)
                foreach (LodRecord lod in Lods) lod.Apply(wanted);
            foreach (ScenarioFigureMeshBank.Record mesh in MeshDetails) mesh.Apply(wanted);
            Detail = wanted; ClothNative = cloth;
            Effects.Apply(restore ? 100 : PerfConfig.FigureEffectsDensityPercent);
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

        internal Mesh? OriginalMeshFor(Renderer renderer)
        {
            foreach (ActorRecord actor in _actors)
                foreach (ScenarioFigureMeshBank.Record mesh in actor.MeshDetails)
                    if (mesh.Renderer == renderer && (mesh.UsesDerivative || mesh.Current == mesh.Original))
                        return mesh.Original;
            return null; // a foreign replacement is its own source, never an owned derivative
        }
        internal ScenarioFigureMeshBank.Record? OriginalRecordFor(Renderer renderer)
        {
            foreach (ActorRecord actor in _actors)
                foreach (ScenarioFigureMeshBank.Record mesh in actor.MeshDetails)
                    if (mesh.Renderer == renderer && (mesh.UsesDerivative || mesh.Current == mesh.Original))
                        return mesh;
            return null;
        }
        private readonly Dictionary<int, ActorRecord> _roots = new();
        private readonly HashSet<int> _seen = new();
        private readonly Queue<GameObject> _pending = new();
        private readonly HashSet<int> _queued = new();
        private readonly Dictionary<int, bool> _scenarioScopes = new();
        private readonly List<LodRecord> _lods = new();
        private int _ownershipCursor;
        private int _scene = int.MinValue;
        private bool _running, _wasLoading;
        private int _players = 100, _enemies = 100, _effects = 100;
        private bool _cloth = true;
        private float _reportAt;
        private bool _reportPending;
        private bool _faulted;
        private bool _measurementApplied;
        internal bool MeasurementReady => !_running || (!_faulted && !_wasLoading
            && _pending.Count == 0 && _measurementApplied
            && _players == PerfConfig.PlayerFigureDetailPercent
            && _enemies == PerfConfig.EnemyFigureDetailPercent
            && _effects == PerfConfig.FigureEffectsDensityPercent
            && _cloth == PerfConfig.FigureClothSimulationEnabled);
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
        internal void MaterialReady(Renderer renderer)
        {
            if (_faulted || renderer == null) return;
            try
            {
                // Completion callbacks target exactly one renderer. Its ancestry reaches a
                // cached native actor root, including the GUID wrapper and local/remote hands.
                // Never rescan the scene or rebuild every actor for an async material edge.
                int depth = 0;
                for (Transform? current = renderer.transform; current != null && depth++ < 64; current = current.parent)
                {
                    if (!_roots.TryGetValue(current.gameObject.GetInstanceID(), out ActorRecord record)) continue;
                    if (record.Actor != null && record.Root != null
                        && record.Effects.MaterialReady(renderer, record.Root, record.Actor))
                    {
                        record.Meshes.RemoveAll(mesh => mesh.Key == renderer);
                        record.Apply(); _reportPending = true; _reportAt = Time.unscaledTime + 1f;
                    }
                    break;
                }
            }
            catch (Exception error) { Fail(error); }
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
            _measurementApplied = false;
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
            ScenarioFigureMeshBank.Prepare(players); ScenarioFigureMeshBank.Prepare(enemies);
            int effects = PerfConfig.FigureEffectsDensityPercent;
            bool cloth = PerfConfig.FigureClothSimulationEnabled;
            if (_players != players || _enemies != enemies || _effects != effects || _cloth != cloth)
            {
                _players = players; _enemies = enemies; _effects = effects; _cloth = cloth;
                Seed(scene); _reportPending = true; _reportAt = Time.unscaledTime + 1f;
                foreach (ActorRecord record in _actors)
                {
                    if (record.Actor == null || record.Root == null) continue;
                    foreach (ScenarioFigureMeshBank.Record mesh in record.MeshDetails)
                        if (mesh.Renderer != null && mesh.Original != null)
                            ScenarioFigureMeshBank.Prepare(mesh.Original, ActorDetail(record.Actor));
                }
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
                _actors[i].Effects.Restore();
                foreach (ScenarioFigureMeshBank.Record mesh in _actors[i].MeshDetails) mesh.Restore();
                _seen.Remove(_actors[i].Id);
                _roots.Remove(_actors[i].RootId);
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
                    int fx = 0, maskedFx = 0, pausedFx = 0, visibleMeshes = 0, visibleVertices = 0;
                    int derivedMeshes = 0, sourceBodyVertices = 0, currentBodyVertices = 0;
                    foreach (ActorRecord record in _actors)
                    {
                        lods += record.Lods.Count;
                        if (record.Lods.Count > 0) coarseActors++;
                        foreach (LodRecord lod in record.Lods)
                        {
                            // A native controller can replace our table. The driver relinquishes
                            // that group, so its stale selection must not imply an applied saving.
                            if (lod.Foreign) continue;
                            if (lod.Selected > 0) changed++;
                            nativeVertices += lod.LevelVertices[0];
                            chosenVertices += lod.LevelVertices[lod.Selected];
                        }
                        foreach (ClothRecord item in record.Clothes) if (item.Owned) clothes++;
                        fx += record.Effects.Count; maskedFx += record.Effects.MaskedRenderers;
                        pausedFx += record.Effects.PausedParticles;
                        foreach (ScenarioFigureMeshBank.Record mesh in record.MeshDetails)
                        {
                            Mesh? current = mesh.Current;
                            if (current == null) continue;
                            sourceBodyVertices += mesh.Original.vertexCount; currentBodyVertices += current.vertexCount;
                            if (mesh.UsesDerivative) derivedMeshes++;
                        }
                        foreach (KeyValuePair<Renderer, int> mesh in record.Meshes)
                        {
                            Renderer renderer = mesh.Key;
                            // isVisible is actual last-frame visibility from ANY native camera,
                            // not a speculative head-camera LOD calculation. Count cached body
                            // meshes, including actors without LODs, only when truly unmasked.
                            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy
                                && !renderer.forceRenderingOff && renderer.isVisible)
                            {
                                Mesh? actual = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                                visibleMeshes++; visibleVertices += actual != null ? actual.vertexCount : 0;
                            }
                        }
                    }
                    VRLog.Debug(Scope, $"Scenario figure detail: players={_players}% enemies={_enemies}% "
                        + $"nativeCloth={_cloth}; {_actors.Count} actor(s), {changed}/{lods} native LOD cap(s), "
                        + $"{clothes} cloth solver(s) disabled; {coarseActors}/{_actors.Count} actor(s) with authored coarse bodies; "
                        + "original/selected near-mesh vertices "
                        + $"{nativeVertices}/{chosenVertices}, rejected scope {_rejectedScope}. "
                        + $"Ambient FX density={_effects}%; {maskedFx} owned renderer(s) masked, {pausedFx} particle solver(s) paused "
                        + $"of {fx} identified optional part(s); last-frame visible unmasked body meshes/vertices "
                        + $"{visibleMeshes}/{visibleVertices} (any camera, including bodies without authored LOD). "
                        + $"Verified derivatives={derivedMeshes}; original/current admitted body vertices {sourceBodyVertices}/{currentBodyVertices}. "
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
            record.Effects = ScenarioFigureEffects.Record.Capture(root, actor);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (record.Effects.Contains(renderer) || renderer.GetComponentInParent<Canvas>(true) != null) continue;
                ActorBehaviour other = renderer.GetComponentInParent<ActorBehaviour>(true);
                if (other != null && other != actor) continue;
                Mesh? mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh != null)
                {
                    record.Meshes.Add(new KeyValuePair<Renderer, int>(renderer, mesh.vertexCount));
                    // Cloth topology feeds native coefficients; weapons and tiny ornaments
                    // retain original silhouette. Never simplify our visual mirrors themselves.
                    bool owned = false;
                    for (Transform? node = renderer.transform; node != null && node != root.transform; node = node.parent)
                        if (node.name.StartsWith("VR_", StringComparison.Ordinal)) { owned = true; break; }
                    if (!owned && mesh.vertexCount >= 1000 && !mesh.name.StartsWith("WP_", StringComparison.Ordinal)
                        && renderer.GetComponent<Cloth>() == null)
                    {
                        ScenarioFigureMeshBank.Prepare(mesh, ActorDetail(actor));
                        record.MeshDetails.Add(new ScenarioFigureMeshBank.Record { Renderer = renderer, Original = mesh });
                    }
                }
            }
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
            record.RootId = root.GetInstanceID(); _roots[record.RootId] = record;
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
                _measurementApplied = true;
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
                try { record.Effects.Restore(); } catch { /* other actors still restore if a cosmetic disappeared */ }
                foreach (ScenarioFigureMeshBank.Record mesh in record.MeshDetails)
                    try { mesh.Restore(); } catch { /* foreign/destroyed slots cannot gate native teardown */ }
                foreach (ClothRecord item in record.Clothes)
                    try
                    {
                        bool forcing = record.Actor != null
                            && ForcingPosition?.GetValue(record.Actor) is int count && count > 0;
                        item.Apply(true, forcing);
                    }
                    catch { /* restore the other native cloth components even if one disappeared */ }
            }
            _actors.Clear(); _roots.Clear(); _seen.Clear(); _pending.Clear(); _queued.Clear();
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

/// <summary>Observe an actually completed native material assignment; never block its continuation.</summary>
[HarmonyPatch(typeof(MaterialLoaderData), "CheckAllMaterialLoaded")]
internal static class MaterialLoaderData_CheckAllMaterialLoaded_FigureEffectsPatch
{
    private static void Postfix(MaterialLoaderData __instance)
    {
        try
        {
            Renderer renderer = __instance.Renderer;
            if (renderer != null && renderer.enabled) ScenarioFigureDetailBudget.MaterialReady(renderer);
        }
        catch { /* optional cosmetics must never escape into native async material loading */ }
    }
}
