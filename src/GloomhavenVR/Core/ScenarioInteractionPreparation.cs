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
    private static bool _wasLoading, _begun, _figureTurn, _faulted, _roomPass, _resourceJobsBegun, _wallMasksPrepared;
    private static float _startedAt, _nextProgressAt;
    internal static bool IsPreparing { get; private set; }
    internal static bool AcceptsRoomReveal(ProceduralMapTile tile) => _begun && !_faulted
        && VRSession.IsRunning && tile != null && tile.gameObject.scene.handle == _procedural;

    internal static void NotifyRoomReveal()
    {
        if (IsPreparing) return;
        _roomPass = IsPreparing = true;
        _startedAt = Time.realtimeSinceStartup;
        _nextProgressAt = _startedAt + 10f;
        VRLog.Debug("Core", "Scenario room preparation started; original input remains available.");
    }

    internal static void Install(GameObject host)
    {
        ScenarioRoomLoading.Install();
        host.AddComponent<Driver>();
    }

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
            bool newRoom = ScenarioRoomLoading.HasPendingReveal;
            if (_begun && !IsPreparing && !newRoom && !_wasLoading && _scene == current.handle
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
            if (newRoom && _begun && !IsPreparing) NotifyRoomReveal();
            if (!_begun)
            {
                _begun = true;
                _startedAt = Time.realtimeSinceStartup;
                _nextProgressAt = _startedAt + 10f;
                IsPreparing = true;
                VRLog.Debug("Core", "Scenario interaction preparation started; original input remains available.");
            }
            if (!IsPreparing) return;
            if (VRLog.WantsDebug && Time.realtimeSinceStartup >= _nextProgressAt)
            {
                _nextProgressAt = Time.realtimeSinceStartup + 10f;
                VRLog.Debug("Core", $"Scenario interaction preparation progress after {Time.realtimeSinceStartup - _startedAt:0.0}s: {ScenarioCardPreparation.PendingDescription}; figures={FigureInteractionPreparation.CompletedCount}/{FigureInteractionPreparation.TotalCount}; roomReveal={ScenarioRoomLoading.HasPendingReveal} ({ScenarioRoomLoading.PendingDescription}); masks={PresentationBudgetPending}.");
            }
            using var scope = PerfMonitor.Scope("Core.ScenarioInteractionPreparation");
            // A ghost clone or one GPU mip/readback is indivisible. Do not stack both cold
            // operations into the same frame; the loading indicator stays visible between them.
            // A reveal is authored by the native room event. Wait for actual procedural
            // content/material jobs before enumerating its newly created actors; no recurring
            // scene inventory or ordinary maintenance queue can re-arm this loading gate.
            if (ScenarioRoomLoading.HasPendingReveal)
            {
                if (!ScenarioRoomLoading.Tick() || PresentationBudgetPending)
                {
                    if (TimedOut()) StopTimedOut();
                    return;
                }
                ScenarioRoomLoading.Complete();
                if (_resourceJobsBegun) FigureInteractionPreparation.BeginNewActors();
                _roomPass = true;
            }
            // Seal prepared original ghosts only after the actual detail/material queues
            // have settled. Sealing earlier cached yesterday's topology/masks and forced
            // valid replacement fallback on the player's first pickup.
            if (PresentationBudgetPending)
            {
                if (TimedOut()) StopTimedOut();
                return;
            }
            if (!_wallMasksPrepared)
            {
                // The fixed native-HIGH rank-mask bank is about one MiB. Its one-time
                // texture upload belongs under this existing spinner, in a separate cold
                // frame before card/ghost construction. Room reveals reuse the same bank.
                _wallMasksPrepared = true;
                using var maskScope = PerfMonitor.Scope("Core.WallMaskPreparation");
                WallSegmentFade.PreparePresentationMasks();
                return;
            }
            if (!_resourceJobsBegun)
            {
                _resourceJobsBegun = true;
                ScenarioCardPreparation.Begin();
                FigureInteractionPreparation.Begin();
            }
            _figureTurn = !_figureTurn;
            if (!FigureInteractionPreparation.IsReady && (_figureTurn || ScenarioCardPreparation.IsReady))
                FigureInteractionPreparation.Tick();
            else if (!ScenarioCardPreparation.IsReady) ScenarioCardPreparation.Tick();
            if (ScenarioCardPreparation.IsReady && FigureInteractionPreparation.IsReady
                && !PresentationBudgetPending)
            {
                IsPreparing = false;
                if (ScenarioCardPreparation.Failures > 0)
                    VRLog.Warn("Core", $"Scenario card preparation skipped {ScenarioCardPreparation.Failures} resource(s); original lazy loading remains available.");
                VRLog.Debug("Core", $"Scenario interaction preparation complete after {Time.realtimeSinceStartup - _startedAt:0.00}s; roomReveal={_roomPass}.");
            }
            else if (TimedOut()) StopTimedOut();
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
        ScenarioRoomLoading.Reset();
        _scene = _procedural = 0;
        _wasLoading = _begun = _figureTurn = _faulted = _roomPass = _resourceJobsBegun = _wallMasksPrepared = IsPreparing = false;
    }

    private static bool PresentationBudgetPending => ScenarioSceneryBudget.IsPreparingPresentation
        || ScenarioFigureDetailBudget.IsPreparingPresentation || ScenarioEnvironmentBudget.IsPreparingPresentation;

    private static bool TimedOut() => Time.realtimeSinceStartup - _startedAt >= MaximumSeconds;

    private static void StopTimedOut()
    {
        // A finite safety net for a genuinely failed async resource. Bounded Debug detail
        // explains which owner remains, without expanding ordinary player log streams.
        if (VRLog.WantsDebug)
            VRLog.Debug("Core", $"Scenario interaction preparation pending at timeout: {ScenarioCardPreparation.PendingDescription}; figures={FigureInteractionPreparation.CompletedCount}/{FigureInteractionPreparation.TotalCount}; room={ScenarioRoomLoading.HasPendingReveal} ({ScenarioRoomLoading.PendingDescription}); masks={PresentationBudgetPending}.");
        CancelJobs();
        ScenarioRoomLoading.Complete();
        IsPreparing = false;
        VRLog.Warn("Core", "Scenario interaction preparation timed out; original lazy presentation remains available.");
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
        private void OnDestroy() { ScenarioRoomLoading.Shutdown(); Reset(); }
    }
}
