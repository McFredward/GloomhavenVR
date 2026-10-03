using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GloomhavenVR.Core;

internal static partial class PerfSceneProfile
{
    private static IEnumerator? _sample;
    private static readonly Stopwatch _sampleCpu = new();
    private static readonly StringBuilder _sceneLine = new(8192);
    private static readonly StringBuilder _gfxLights = new(1024);
    private static readonly List<Renderer> _inventoryRenderers = new(4096);
    private static readonly List<MonoBehaviour> _inventoryBehaviours = new(4096);
    private static readonly List<Animator> _inventoryAnimators = new(128);
    private static readonly List<ParticleSystem> _inventoryParticles = new(128);
    private static readonly List<Light> _inventoryLights = new(64);
    private static readonly List<LODGroup> _inventoryLodGroups = new(64);
    private static readonly List<GameObject> _rootScratch = new(128);
    private static readonly List<Component> _componentScratch = new(16);
    private static readonly Stack<Transform> _pendingNodes = new(512);
    private static readonly HashSet<int> _rendererAncestors = new(4096);
    private static readonly HashSet<int> _visitedNodes = new(8192);
    private static readonly HashSet<int> _sceneHandles = new(16);
    private static Scene _persistentScene;
    private static readonly List<Graphic> _inventoryGraphics = new(2048);
    private static readonly List<Renderer> _rosterRenderers = new(4096);
    private static readonly List<bool> _rosterShadow = new(4096);
    private static Renderer[]? _completedRenderers;
    private static bool[] _completedShadow = Array.Empty<bool>();
    private static int _completedEnabled, _completedVisible, _graphicEnabled, _graphicMod;
    private static int _rosterEnabled, _rosterVisible;
    private static int _completedGraphics, _completedGraphicEnabled, _completedGraphicMod;
    private static int _completedScene, _completedProcGen, _completedStamp;
    private static float _completedAt, _completedSpan;
    private static bool CensusOn => PerfConfig.SceneCensus == null || PerfConfig.SceneCensus.Value;

    private static bool _sampleFullProfile;
    private static int _requestedScene, _requestedProcGen, _requestedSceneStamp;
    private static int _lodEnabled, _sampleSlices;
    private static bool _inSim;
    private static double _simMs, _maxSliceMs, _maxUnitMs;
    private static float _sampleStart;
    private static double _budgetMilliseconds = .5;
    private static int _objectsPerFrame = 64;

    internal static void ConfigureBudget(double milliseconds, int objects)
    {
        _budgetMilliseconds = double.IsNaN(milliseconds) ? .5 : Math.Max(.1, Math.Min(4, milliseconds));
        _objectsPerFrame = Math.Max(8, Math.Min(512, objects));
    }

    // Requests are on the established summary cadence. The host subsequently pumps work;
    // neither the summary nor a Resources query owns a full-scene synchronous walk.
    internal static void AppendSceneLine(StringBuilder sb)
    {
        if (!VRLog.WantsDebug || !PerfConfig.SceneProfileOn)
        {
            Cancel();
            return;
        }
        if (!Ready(out int scene, out int procGen, out string provenance))
        {
            Cancel();
            sb.Append("SCENE — deferred: game scene has not finished loading").Append(provenance);
            return;
        }
        if (_sample != null)
        {
            sb.Append("SCENE — incremental census in progress").Append(provenance);
            return;
        }
        CensusDecision decision = _rationer.Decide(true, scene, procGen);
        if (decision == CensusDecision.Skipped)
        {
            sb.Append("SCENE — skipped this window. The last walk measured ")
                .Append(_lastWalkMs.ToString("F1")).Append("ms; ")
                .Append(_rationer.SkipWindows).Append(" more window(s) will be skipped before the next sample")
                .Append(provenance);
            return;
        }
        StartSample(scene, procGen, provenance, decision == CensusDecision.NewScene, true);
        sb.Append("SCENE — incremental census scheduled; budget ")
            .Append(_budgetMilliseconds.ToString("F3")).Append("ms or ")
            .Append(_objectsPerFrame).Append(" work units/frame").Append(provenance);
    }

    private static void StartSample(int scene, int procGen, string provenance,
        bool newScene, bool full)
    {
        _requestedScene = scene;
        _requestedProcGen = procGen;
        _requestedSceneStamp = SceneStamp();
        _sampleStart = Time.unscaledTime;
        _sampleSlices = 0;
        _simMs = _maxSliceMs = _maxUnitMs = 0;
        _sampleCpu.Reset();
        _sceneLine.Length = _gfxLights.Length = 0;
        _rosterRenderers.Clear(); _rosterShadow.Clear();
        _rosterEnabled = _rosterVisible = _graphicEnabled = _graphicMod = 0;
        _sampleFullProfile = full;
        _sample = full ? SampleSceneLine(_sceneLine, scene, procGen, provenance, newScene)
            : SampleRoster();
    }

    internal static void AppendFrameCensus(StringBuilder sb)
    {
        if (!VRLog.WantsDebug || !CensusOn
            || !Ready(out int scene, out int procGen, out string provenance))
        {
            Cancel();
            sb.Append(" | scene census n/a (incremental census awaits a loaded Debug scene)");
            return;
        }
        if (_completedRenderers != null && (scene != _completedScene
            || procGen != _completedProcGen || SceneStamp() != _completedStamp)) DropCompleted();
        if (_completedRenderers != null)
        {
            sb.Append(" | scene census: ").Append(_completedRenderers.Length).Append(" renderer(s), ")
                .Append(_completedEnabled).Append(" enabled, ").Append(_completedVisible)
                .Append(" visible to at least one camera (not ONE INSTANT; incremental capture span ")
                .Append(_completedSpan.ToString("F2")).Append("s, age ")
                .Append((Time.unscaledTime - _completedAt).ToString("F2"))
                .Append("s; rolling per-frame estimate remains on ZOOM; forced-off excluded)")
                .Append("; uGUI: ").Append(_completedGraphics).Append(" graphic(s), ")
                .Append(_completedGraphicEnabled).Append(" enabled, ").Append(_completedGraphicMod)
                .Append(" on the mod's own layer. A Graphic is NOT a Renderer; these counts ")
                .Append("do not assign unbracketed native canvas time");
        }
        else sb.Append(" | scene census n/a (incremental census scheduled/in progress)");
        if (_sample != null) return;
        bool full = PerfConfig.SceneProfileOn;
        CensusDecision decision = full ? _rationer.Decide(true, scene, procGen) : CensusDecision.Sample;
        StartSample(scene, procGen, provenance, decision == CensusDecision.NewScene,
            full && decision != CensusDecision.Skipped);
    }

    private static IEnumerator SampleRoster()
    {
        foreach (object? step in InventoryScene()) yield return step;
        for (int i = 0; i < _inventoryRenderers.Count; i++)
        {
            yield return null;
            Renderer renderer = _inventoryRenderers[i];
            if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
            bool enabled = renderer.enabled;
            RecordRoster(renderer, enabled, enabled && !renderer.forceRenderingOff && renderer.isVisible);
        }
        foreach (object? step in PrepareGraphics()) yield return step;
    }

    private static void RecordRoster(Renderer renderer, bool enabled, bool visible)
    {
        _rosterRenderers.Add(renderer); _rosterShadow.Add(visible);
        if (enabled) _rosterEnabled++;
        if (visible) _rosterVisible++;
    }

    private static IEnumerable PrepareGraphics()
    {
        for (int i = 0; i < _inventoryGraphics.Count; i++)
        {
            yield return null;
            Graphic graphic = _inventoryGraphics[i];
            if (graphic == null || !graphic.isActiveAndEnabled) continue;
            _graphicEnabled++;
            if (graphic.gameObject.layer == VRLayers.ModLayer) _graphicMod++;
        }
    }

    private static void PublishRoster()
    {
        if (!CensusOn) { DropCompleted(); return; }
        _completedRenderers = _rosterRenderers.ToArray();
        _completedShadow = _rosterShadow.ToArray();
        _completedEnabled = _rosterEnabled; _completedVisible = _rosterVisible;
        _completedGraphics = _inventoryGraphics.Count;
        _completedGraphicEnabled = _graphicEnabled; _completedGraphicMod = _graphicMod;
        _completedScene = _requestedScene; _completedProcGen = _requestedProcGen;
        _completedStamp = _requestedSceneStamp;
        _completedAt = Time.unscaledTime; _completedSpan = _completedAt - _sampleStart;
        PerfFrameSplit.AdoptCensusRoster(_completedRenderers, _completedVisible, _completedShadow);
    }

    private static void DropCompleted()
    {
        _completedRenderers = null; _completedShadow = Array.Empty<bool>();
        PerfFrameSplit.ClearCensusRoster();
    }

    internal static void Tick(Scene persistentScene)
    {
        _persistentScene = persistentScene;
        if (_sample == null && _completedRenderers == null) return;
        if (!VRLog.WantsDebug || (!PerfConfig.SceneProfileOn && !CensusOn) || PerfConfig.Enabled == null
            || !PerfConfig.Enabled.Value || IsPreMenuScene()
            || !Ready(out int scene, out int procGen, out _, includeProvenance: false)
            || (_sample != null && (scene != _requestedScene || procGen != _requestedProcGen
                || SceneStamp() != _requestedSceneStamp || (_sampleFullProfile && !PerfConfig.SceneProfileOn)))
            || (_sample == null && (scene != _completedScene || procGen != _completedProcGen
                || SceneStamp() != _completedStamp)))
        {
            Cancel();
            return;
        }
        if (!CensusOn) DropCompleted();
        if (_sample == null) return;
        using (PerfMonitor.Scope("Perf.SceneProfileSlice"))
        {
            long sliceStart = Stopwatch.GetTimestamp();
            _sampleSlices++;
            bool completed = false;
            for (int i = 0; i < _objectsPerFrame; i++)
            {
                long start = Stopwatch.GetTimestamp();
                bool sim = _inSim;
                _sampleCpu.Start();
                try { completed = !_sample.MoveNext(); }
                finally { _sampleCpu.Stop(); }
                double ms = ElapsedMs(start);
                if (sim) _simMs += ms;
                _maxUnitMs = Math.Max(_maxUnitMs, ms);
                if (completed || ElapsedMs(sliceStart) >= _budgetMilliseconds) break;
            }
            _maxSliceMs = Math.Max(_maxSliceMs, ElapsedMs(sliceStart));
            if (!completed) return;
            _sample = null;
            PublishRoster();
            if (_sampleFullProfile)
            {
                // Formatting and bounded top-bucket logging are separate atomic work.
                // Their actual cost stays in STEPS, without a second native inventory.
                VRLog.Info("Perf", _sceneLine.ToString());
                _sceneLine.Length = 0;
                AppendGfxLine(_sceneLine);
                VRLog.Info("Perf", _sceneLine.ToString());
            }
            ClearJob();
        }
    }

    internal static void Cancel()
    {
        DropCompleted();
        ClearJob();
    }

    private static void ClearJob()
    {
        (_sample as IDisposable)?.Dispose();
        _sample = null;
        _sampleCpu.Stop();
        _inSim = false;
        PerfTextureCensus.Cancel();
        _rosterRenderers.Clear(); _rosterShadow.Clear();
        _inventoryGraphics.Clear();
        _inventoryRenderers.Clear();
        _inventoryBehaviours.Clear();
        _inventoryAnimators.Clear();
        _inventoryParticles.Clear();
        _inventoryLights.Clear();
        _inventoryLodGroups.Clear();
        _rootScratch.Clear();
        _componentScratch.Clear();
        _pendingNodes.Clear();
        _rendererAncestors.Clear();
        _visitedNodes.Clear();
        _sceneHandles.Clear();
        SimSb.Length = _sceneLine.Length = _gfxLights.Length = 0;
    }

    private static double ElapsedMs(long start)
        => (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency);

    private static int SceneStamp()
    {
        int stamp = SceneManager.sceneCount;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            unchecked { stamp = stamp * 31 + SceneManager.GetSceneAt(i).handle; }
        return stamp;
    }

    private static bool Ready(out int sceneHandle, out int procGenHandle,
        out string provenance, bool includeProvenance = true)
    {
        SceneController controller = SceneController.Instance;
        Scene scene = controller != null ? controller.GetCurrentScene : default;
        bool valid = scene.IsValid(), loaded = valid && scene.isLoaded;
        bool loading = controller == null || controller.IsLoading;
        bool scenarioLoading = controller != null && controller.ScenarioIsLoading;
        sceneHandle = valid ? scene.handle : 0;
        Scene procGen = Choreographer.s_Choreographer != null
            ? Choreographer.s_Choreographer.m_ProcGenScene : default;
        procGenHandle = procGen.IsValid() && procGen.isLoaded ? procGen.handle : 0;
        provenance = includeProvenance ? $" | scene '{(valid ? scene.name : "unavailable")}' handle "
            + $"{sceneHandle} procgen {procGenHandle} loaded {loaded} loading {loading} "
            + $"scenarioLoading {scenarioLoading}" : "";
        return loaded && !loading && !scenarioLoading;
    }

    private static IEnumerable InventoryScene()
    {
        _lodEnabled = 0;
        // Scene root enumeration is a single atomic Unity call per loaded scene. Unlike a
        // full component query, it never walks descendants. Its measured overruns are reported.
        for (int i = 0; i <= SceneManager.sceneCount; i++)
        {
            yield return null;
            Scene scene = i < SceneManager.sceneCount ? SceneManager.GetSceneAt(i) : _persistentScene;
            if (!scene.IsValid() || !scene.isLoaded || !_sceneHandles.Add(scene.handle)) continue;
            _rootScratch.Clear();
            if (_rootScratch.Capacity < scene.rootCount) _rootScratch.Capacity = scene.rootCount;
            scene.GetRootGameObjects(_rootScratch);
            for (int r = 0; r < _rootScratch.Count; r++)
            {
                yield return null;
                if (_rootScratch[r] != null) _pendingNodes.Push(_rootScratch[r].transform);
            }
        }
        while (_pendingNodes.Count > 0)
        {
            yield return null;
            Transform node = _pendingNodes.Pop();
            if (node == null || !_visitedNodes.Add(node.GetInstanceID())) continue;
            _componentScratch.Clear();
            node.GetComponents(_componentScratch);
            // Inactive nodes are traversed solely to answer the original Animator descendant-
            // Renderer predicate (which includes inactive descendants); counts remain active-only.
            for (int c = 0; c < _componentScratch.Count; c++)
            {
                yield return null;
                Component component = _componentScratch[c];
                if (component == null) continue;
                if (_sampleFullProfile && component is Renderer)
                {
                    Transform parent = node;
                    for (int depth = 0; parent != null && depth < MaxHierarchyDepth; depth++, parent = parent.parent)
                        _rendererAncestors.Add(parent.GetInstanceID());
                }
                if (!node.gameObject.activeInHierarchy) continue;
                if (component is Graphic graphic) _inventoryGraphics.Add(graphic);
                if (component is Renderer renderer) _inventoryRenderers.Add(renderer);
                else if (!_sampleFullProfile) continue;
                else if (component is MonoBehaviour behaviour) _inventoryBehaviours.Add(behaviour);
                else if (component is Animator animator) _inventoryAnimators.Add(animator);
                else if (component is ParticleSystem particle) _inventoryParticles.Add(particle);
                else if (component is Light light) _inventoryLights.Add(light);
                else if (component is LODGroup lod)
                {
                    _inventoryLodGroups.Add(lod);
                    if (lod.enabled) _lodEnabled++;
                }
            }
            for (int child = 0; node != null && child < node.childCount; child++)
            {
                yield return null;
                if (node != null && child < node.childCount) _pendingNodes.Push(node.GetChild(child));
            }
        }
    }
}
