using System;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Move cold presentation work into a visible preparation period after native scenario loading.
/// Original gameplay/load flags and input are never held or written by this coordinator. A late
/// actor/resource still uses the existing immediate fallback; preparation cannot deadlock a quest.
/// Shared original caches serve local and remote presentation on every platform.
/// </summary>
internal static class ScenarioInteractionPreparation
{
    private const float MaximumSeconds = 90f;
    private static int _scene, _procedural;
    private static bool _wasLoading, _begun, _figureTurn, _faulted;
    private static float _startedAt;
    internal static bool IsPreparing { get; private set; }

    internal static void Install(GameObject host) => host.AddComponent<Driver>();

    internal static void Tick()
    {
        try
        {
            SceneController controller = SceneController.Instance;
            if (!VRSession.IsRunning || controller == null)
            {
                Reset();
                return;
            }
            bool loading = controller.IsLoading || controller.ScenarioIsLoading;
            if (loading)
            {
                if (!_wasLoading) Reset();
                _wasLoading = true;
                return;
            }
            Scene current = controller.GetCurrentScene;
            Scene procedural = Choreographer.s_Choreographer != null
                ? Choreographer.s_Choreographer.m_ProcGenScene : default;
            if (_begun && !IsPreparing && !_wasLoading && _scene == current.handle
                && _procedural == procedural.handle) return;
            // CampaignMap also retains game-model objects. Only the real loaded Game/ProcGen
            // presentation owns scenario figures; map and original 2D windows remain available.
            if (!current.IsValid() || !current.isLoaded || current.name != "Game"
                || !procedural.IsValid() || !procedural.isLoaded || ScenarioManager.Scenario == null)
            {
                Reset();
                return;
            }
            if (_scene != current.handle || _procedural != procedural.handle || _wasLoading)
            {
                Reset();
                _scene = current.handle;
                _procedural = procedural.handle;
            }
            _wasLoading = false;
            if (_faulted) return;
            if (!_begun)
            {
                _begun = true;
                _startedAt = Time.realtimeSinceStartup;
                IsPreparing = true;
                ScenarioCardPreparation.Begin();
                FigureInteractionPreparation.Begin();
                VRLog.Debug("Core", "Scenario interaction preparation started; original input remains available.");
            }
            if (!IsPreparing) return;
            using var scope = PerfMonitor.Scope("Core.ScenarioInteractionPreparation");
            // A ghost clone or one GPU mip/readback is indivisible. Do not stack both cold
            // operations into the same frame; the loading indicator stays visible between them.
            _figureTurn = !_figureTurn;
            if (!FigureInteractionPreparation.IsReady && (_figureTurn || ScenarioCardPreparation.IsReady))
                FigureInteractionPreparation.Tick();
            else if (!ScenarioCardPreparation.IsReady) ScenarioCardPreparation.Tick();
            if (ScenarioCardPreparation.IsReady && FigureInteractionPreparation.IsReady)
            {
                IsPreparing = false;
                if (ScenarioCardPreparation.Failures > 0)
                    VRLog.Warn("Core", $"Scenario card preparation skipped {ScenarioCardPreparation.Failures} resource(s); original lazy loading remains available.");
                VRLog.Debug("Core", $"Scenario interaction preparation complete after {Time.realtimeSinceStartup - _startedAt:0.00}s.");
            }
            else if (Time.realtimeSinceStartup - _startedAt >= MaximumSeconds)
            {
                // Never retain a spinner because an asynchronous native resource failed to arrive.
                // Stop pumping, but retain valid reserves and any already-held ghost. Input
                // stays available throughout preparation; cancellation is not a release event.
                CancelJobs();
                IsPreparing = false;
                VRLog.Warn("Core", "Scenario interaction preparation timed out; original lazy presentation remains available.");
            }
        }
        catch (Exception error)
        {
            // Preserve the completed edge: do not retry a fault on every ordinary frame.
            // Retain already-adopted live presentation until the real scene teardown.
            CancelJobs();
            IsPreparing = false;
            _begun = _faulted = true;
            VRLog.Warn("Core", "Scenario interaction preparation failed; original lazy presentation remains available: "
                + error.GetType().Name);
        }
    }

    internal static void Reset()
    {
        if (_begun || IsPreparing) ResetJobs();
        _scene = _procedural = 0;
        _wasLoading = _begun = _figureTurn = _faulted = IsPreparing = false;
    }

    private static void CancelJobs()
    {
        ScenarioCardPreparation.CancelPreparation();
        FigureInteractionPreparation.CancelPreparation();
    }

    private static void ResetJobs()
    {
        ScenarioCardPreparation.Reset();
        FigureInteractionPreparation.Reset();
    }

    [DefaultExecutionOrder(-29000)]
    private sealed class Driver : MonoBehaviour
    {
        private void Update() => Tick();
    }
}
