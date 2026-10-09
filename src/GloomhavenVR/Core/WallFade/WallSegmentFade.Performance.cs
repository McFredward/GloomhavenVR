using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GloomhavenVR.Board.FigureGrab;
using ScenarioRuleLibrary;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.Core;

/// <summary>
/// Build652: the maintainer explicitly permits instant, completely absent walls as a
/// configurable performance compromise. Regular retains the original view-dependent fades;
/// Hide all and Auto retire those decisions, animations, diagnostics and sender scans. Only
/// exact members of the existing complete wall collector are eligible. DoorRoot/gate-column
/// segments, their shared attachments, arch effects, actors, floors, water and held props retain
/// the existing exclusions. Renderer masks change presentation, never gameplay activation.
///
/// The settled hidden path reads counters only. Initial discovery and real native content/
/// room changes still need the existing collector: stopping discovery would leave regenerated
/// walls visible or misclassify new arches. These are lifecycle costs, not recurring rescans.
/// </summary>
internal static partial class WallSegmentFade
{
    private static bool _performanceWallsHidden;
    private static Action<Renderer>? _performanceMaskRestored;
    internal static void ConfigurePerformanceMaskRestored(Action<Renderer> restored) =>
        _performanceMaskRestored = restored;

    // A cooperating scenery owner can relinquish its mask while walls remain hidden. The
    // saved prior flag must then lose that foreign claim, rather than restore stale hiding.
    internal static bool RetainPerformanceMaskOnForeignRelease(Renderer renderer) =>
        _performanceWallsHidden && !ReferenceEquals(renderer, null)
        && _driver?.RetainPerformanceMaskOnForeignRelease(renderer) == true;
    internal static bool PerformanceWallsHidden => _performanceWallsHidden;

    // ReferenceEquals is deliberate here: consumers already validate renderer liveness.
    // The hot-path query adds one managed dictionary probe, not another Unity null call.
    internal static bool IsPerformanceHidden(Renderer renderer) => _performanceWallsHidden
        && !ReferenceEquals(renderer, null) && _driver?.HasPerformanceMask(renderer) == true;

    internal static void NotifyPerformanceContentChange()
    {
        if (_performanceWallsHidden) _driver?.InvalidatePerformanceInventory();
    }

    internal static void NotifyPerformanceRendererReady(Renderer renderer)
    {
        if (!_performanceWallsHidden || ReferenceEquals(renderer, null)) return;
        _driver?.PerformanceRendererReady(renderer);
    }

    /// <summary>Bounded, allocation-free frame-time observation. No timer, camera read,
    /// profiler API, scene walk or per-eye sample. A two-second continuous loaded window
    /// prevents a single ordinary hitch from being mistaken for sustained poor gameplay.
    /// Each observed delta is capped at250ms, so one long pause cannot fill the window;
    /// sustained very poor loaded gameplay still triggers instead of being ignored.</summary>
    internal sealed class PerformanceWallClock
    {
        private const float WindowSeconds = 2f;
        private int _frame = -1, _samples;
        private float _seconds;

        internal void Reset() { _frame = -1; _samples = 0; _seconds = 0f; }

        internal bool Observe(int frame, bool eligible, float delta, int belowFps)
        {
            if (frame == _frame) return false;
            _frame = frame;
            if (!eligible || delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta))
            {
                _samples = 0; _seconds = 0f;
                return false;
            }
            _seconds += Math.Min(delta, .25f); _samples++;
            if (_seconds < WindowSeconds) return false;
            bool slow = _samples / _seconds < belowFps;
            _samples = 0; _seconds = 0f;
            return slow;
        }
    }

    private sealed partial class FadeDriver
    {
        private readonly PerformanceWallClock _performanceClock = new();
        // Value is the forceRenderingOff state after all substitute consumers restored their
        // leases. Never undo native/foreign hiding that preceded this quality compromise.
        private readonly Dictionary<Renderer, bool> _performanceMasks = new();
        private readonly HashSet<Renderer> _performanceWanted = new();
        private readonly HashSet<Renderer> _performanceProtected = new();
        private readonly List<Renderer> _performanceRetired = new();
        private int _performanceMode = -1, _performanceScene = -1;
        private TilesOcclusionGenerator? _performanceGenerator;
        private bool _performanceAutoLatched;
        private uint _performanceRevision, _performanceCollectedRevision, _performanceCycleRevision;
        private int _performanceRoomCount = -1, _performanceWallCount = -1;
        private readonly HashSet<GameObject> _performanceHeldRoots = new();
        private readonly List<GameObject> _performanceHeldScratch = new();
        private readonly HashSet<GameObject> _performanceHeldNext = new();
        private readonly List<Renderer> _performanceHeldRenderers = new();
        private float _performanceRetryAt, _performanceReadySince = -1f;

        internal bool HasPerformanceMask(Renderer renderer) => _performanceMasks.ContainsKey(renderer);
        internal bool RetainPerformanceMaskOnForeignRelease(Renderer renderer)
        {
            if (!_performanceMasks.ContainsKey(renderer)) return false;
            _performanceMasks[renderer] = false;
            return true;
        }
        internal void InvalidatePerformanceInventory() { unchecked { _performanceRevision++; } }

        internal void PerformanceRendererReady(Renderer renderer)
        {
            InvalidatePerformanceInventory();
            // Native loading can clear a renderer mask. Reinstate an already classified wall
            // immediately; new renderer membership is decided by the full existing collector.
            if (_performanceMasks.ContainsKey(renderer)) renderer.forceRenderingOff = true;
        }

        private bool PerformanceScenarioStillCurrent()
        {
            // Scene handles are opaque identities: actual Unity can return a valid
            // loaded scene with a negative handle. Validate the scene itself below.
            if (!VRSession.IsRunning
                || Rig.VRRigDriver.HeadCamera == null
                || _performanceGenerator == null || TilesOcclusionGenerator.s_Instance != _performanceGenerator)
                return false;
            SceneController controller = SceneController.Instance;
            if (controller == null) return false;
            Scene scene = controller.GetCurrentScene;
            return scene.IsValid() && scene.isLoaded && scene.handle == _performanceScene && scene.name == "Game";
        }

        private bool TickPerformanceVisibility()
        {
            int mode = PerfConfig.WallVisibilityMode;
            if (mode != _performanceMode)
            {
                bool recoverWireKeys = _performanceWallsHidden && mode != 1
                    && PerformanceScenarioStillCurrent();
                ResetPerformanceVisibility("mode changed");
                _performanceMode = mode;
                // Hidden lifecycle commits do not need sender keys. New segments can have
                // WireKey=0, so recover once before ordinary fading resumes on this SAME table.
                // Teardown/scene departure and Hide-to-Hide do not enter this recovery path.
                if (recoverWireKeys) ComputeWireKeys();
            }
            if (mode == 0) return false;

            Camera? head = Rig.VRRigDriver.HeadCamera;
            TilesOcclusionGenerator gen = TilesOcclusionGenerator.s_Instance;
            SceneController controller = SceneController.Instance;
            if (!VRSession.IsRunning || head == null || gen == null || controller == null)
            {
                ResetPerformanceVisibility("no scenario / no head");
                return false;
            }
            Scene scene = controller.GetCurrentScene;
            if (!scene.IsValid() || !scene.isLoaded || scene.name != "Game" || ScenarioManager.Scenario == null)
            {
                ResetPerformanceVisibility("outside scenario");
                return false;
            }
            if (_performanceScene != scene.handle || _performanceGenerator != gen)
            {
                ResetPerformanceVisibility("scenario changed");
                _performanceScene = scene.handle;
                _performanceGenerator = gen;
            }

            float now = Time.unscaledTime;
            bool loaded = !controller.IsLoading && !controller.ScenarioIsLoading
                && !ScenarioInteractionPreparation.IsPreparing && !ScenarioRoomLoading.HasPendingReveal;
            if (mode == 2 && !_performanceAutoLatched)
            {
                // Options rendering is temporary UI overhead, not the fully loaded closed-
                // options gameplay this policy targets. Existing IsOpen is queried once per
                // application frame only while observing, never per eye or while hidden.
                bool eligible = loaded && Application.isFocused && !VROptionsTab.IsOpen;
                if (!eligible) _performanceReadySince = -1f;
                else if (_performanceReadySince < 0f) _performanceReadySince = now;
                // The first measured delta after a loading edge partly belongs to the old
                // edge. Allow half a second of ordinary frames before observing gameplay.
                bool settled = eligible && now - _performanceReadySince >= .5f;
                if (_performanceClock.Observe(Time.frameCount, settled, Time.unscaledDeltaTime,
                    PerfConfig.WallAutoHideBelowFps))
                {
                    _performanceAutoLatched = true;
                    VRLog.Info(Name, $"PERFORMANCE WALLS AUTO: loaded gameplay stayed below {PerfConfig.WallAutoHideBelowFps} FPS for two seconds; walls remain absent until Regular is selected or this scenario ends.");
                }
            }
            if (mode != 1 && !_performanceAutoLatched) return false;
            if (!_performanceWallsHidden) EnterPerformanceVisibility(gen, now);
            TickPerformanceInventory(gen, now, loaded);
            return true;
        }

        private void EnterPerformanceVisibility(TilesOcclusionGenerator gen, float now)
        {
            bool tableReady = _rescanStage == RescanStage.Idle && _committedSigValid
                && _live.BuiltRoomCount == gen.m_RoomRenderers.Count;
            // Cancel a partial census cleanly. A completed old table remains valid until an
            // actual collector commit; the scratch/memos/signature must not outlive cancellation.
            AbandonRescanCycle();
            ClearAllBlocks("performance walls hidden");
            ClearWallDrawTrace();
            ResetInsideBoardState();
            _pathAuditRunning = false; _pathAuditWalls.Clear();
            _peerFades.Clear();
            _performanceWallsHidden = true;
            ObservePerformanceHeldRoots();
            _performanceRoomCount = gen.m_RoomRenderers.Count;
            _performanceWallCount = ProceduralWall.m_WallCache?.Count ?? 0;
            _performanceRevision = tableReady ? 0u : 1u;
            _performanceCollectedRevision = 0;
            _performanceRetryAt = now;
            ReconcilePerformanceMasks();
            VRLog.Info(Name, $"PERFORMANCE WALLS HIDDEN: mode={_performanceMode}, renderers={_performanceMasks.Count}; ordinary fade decisions, animations, diagnostics and wire sampling paused. Door frames and gate arches remain.");
        }

        private void TickPerformanceInventory(TilesOcclusionGenerator gen, float now, bool loaded)
        {
            int rooms = gen.m_RoomRenderers.Count;
            int walls = ProceduralWall.m_WallCache?.Count ?? 0;
            if (rooms != _performanceRoomCount || walls != _performanceWallCount)
            {
                _performanceRoomCount = rooms; _performanceWallCount = walls;
                InvalidatePerformanceInventory();
            }
            // Exact root identity catches same-count swaps too. With no hands occupied this
            // is two managed count reads; occupied roots are a tiny already-owned registry.
            ObservePerformanceHeldRoots();
            if (_rescanStage == RescanStage.Idle
                && _performanceCollectedRevision == _performanceRevision) return;
            // During native generation coalesce changes. Recollect when native loading has
            // finished; neither hidden policy nor the old masks are dropped while waiting.
            if (!loaded || now < _performanceRetryAt) return;

            try
            {
                // Some existing adoption predicates read IsActuallyDrawing. Temporarily release
                // only our masks synchronously around the existing stage and put them back in
                // finally, before any normal camera can draw. enabled/material/native alpha are
                // untouched. This is never reached by the settled hidden path.
                SetPerformanceCollectionMasks(false);
                if (_rescanStage == RescanStage.Idle)
                {
                    // A dirty lifecycle can replace generated children without changing any
                    // registry count. Discard the old snapshot/signature explicitly; a reused
                    // warm snapshot would miss those new walls until the old six-second timer.
                    AbandonRescanCycle();
                    _performanceCycleRevision = _performanceRevision;
                    _rescanUrgent = true;
                    BeginRescanCycle(gen, now, urgent: true);
                }
                else
                {
                    StepRescanCycle(gen, now);
                    if (_rescanStage == RescanStage.Idle)
                    {
                        _performanceCollectedRevision = _performanceCycleRevision;
                        ReconcilePerformanceMasks();
                        if (VRLog.Wants(VRLogLevel.Debug))
                            VRLog.Debug(Name, $"PERFORMANCE WALL INVENTORY: renderers={_performanceMasks.Count}, rooms={rooms}, generationRevision={_performanceCollectedRevision}; lifecycle collector complete, recurring scans remain paused.");
                    }
                }
            }
            catch
            {
                AbandonRescanCycle();
                _performanceRetryAt = now + 2f;
                throw;
            }
            finally
            {
                SetPerformanceCollectionMasks(true);
            }
        }

        private void ObservePerformanceHeldRoots()
        {
            if (HeldProps.Count == 0 && NetHeldProps.Count == 0 && _performanceHeldRoots.Count == 0)
                return;
            _performanceHeldScratch.Clear(); _performanceHeldNext.Clear();
            for (int i = 0; i < HeldProps.Count; i++)
                if (HeldProps.TryGetSlot(i, out _, out GameObject visual, out _, out _))
                    _performanceHeldScratch.Add(visual);
            NetHeldProps.CopyVisualRoots(_performanceHeldScratch);
            foreach (GameObject visual in _performanceHeldScratch) _performanceHeldNext.Add(visual);
            if (_performanceHeldRoots.SetEquals(_performanceHeldNext)) return;

            // Only changed visual roots need renderer discovery. A held prop uses its SAME
            // native visual (GrabbableProp.OnGrab), so revoke exact wall masks this LateUpdate,
            // before the material path's hidden-member early-out. Same-count swaps work too.
            foreach (GameObject visual in _performanceHeldNext)
            {
                if (_performanceHeldRoots.Contains(visual) || visual == null) continue;
                _performanceHeldRenderers.Clear();
                visual.GetComponentsInChildren(includeInactive: true, _performanceHeldRenderers);
                foreach (Renderer renderer in _performanceHeldRenderers) ReleasePerformanceMask(renderer);
            }
            // HeldProps retains ownership through ordinary return flight/landing. An exact
            // formerly eligible member can resume hiding when its root really leaves the hand.
            foreach (GameObject visual in _performanceHeldRoots)
            {
                if (_performanceHeldNext.Contains(visual) || visual == null) continue;
                _performanceHeldRenderers.Clear();
                visual.GetComponentsInChildren(includeInactive: true, _performanceHeldRenderers);
                foreach (Renderer renderer in _performanceHeldRenderers)
                    if (_performanceWanted.Contains(renderer)) AcquirePerformanceMask(renderer);
            }
            _performanceHeldRoots.Clear();
            foreach (GameObject visual in _performanceHeldNext) _performanceHeldRoots.Add(visual);
        }

        private void SetPerformanceCollectionMasks(bool hide)
        {
            foreach (KeyValuePair<Renderer, bool> entry in _performanceMasks)
                if (entry.Key != null) entry.Key.forceRenderingOff = hide || entry.Value;
        }

        private static bool PerformanceProtectedSegment(Segment seg) => seg.DoorRoot != null || seg.IsGateColumn;

        private void GatherPerformanceMembers(Segment seg, HashSet<Renderer> into)
        {
            foreach (MeshRenderer renderer in seg.Renderers) if (renderer != null) into.Add(renderer);
            foreach (MeshRenderer renderer in seg.Foliage) if (renderer != null) into.Add(renderer);
            foreach (MeshRenderer renderer in seg.Siblings) if (renderer != null) into.Add(renderer);
            GatherPerformanceProps(seg.Body, into); GatherPerformanceProps(seg.Stacked, into);
            GatherPerformanceProps(seg.Mounted, into); GatherPerformanceProps(seg.UnitDressing, into);
        }

        private static void GatherPerformanceProps(List<MountedProp> props, HashSet<Renderer> into)
        {
            foreach (MountedProp prop in props)
                if (prop.Renderer != null) into.Add(prop.Renderer);
        }

        private void ReconcilePerformanceMasks()
        {
            _performanceWanted.Clear(); _performanceProtected.Clear();
            foreach (Segment seg in _live.Segments.Values)
                GatherPerformanceMembers(seg, PerformanceProtectedSegment(seg)
                    ? _performanceProtected : _performanceWanted);
            foreach (CornerPiece corner in _live.CornerPieces)
            {
                Renderer renderer = corner.Prop.Renderer;
                if (renderer == null) continue;
                if (PerformanceProtectedSegment(corner.A)
                    || (corner.B != null && PerformanceProtectedSegment(corner.B)))
                    _performanceProtected.Add(renderer);
                else _performanceWanted.Add(renderer);
            }
            _performanceRetired.Clear();
            foreach (KeyValuePair<Renderer, bool> entry in _performanceMasks)
                if (entry.Key == null || !_performanceWanted.Contains(entry.Key)
                    || _performanceProtected.Contains(entry.Key) || !PerformanceCanHide(entry.Key))
                    _performanceRetired.Add(entry.Key!); // destroyed Unity wrapper is a non-null ledger key
            foreach (Renderer renderer in _performanceRetired) ReleasePerformanceMask(renderer);
            foreach (Renderer renderer in _performanceWanted) AcquirePerformanceMask(renderer);
        }

        private void AcquirePerformanceMask(Renderer renderer)
        {
            if (_performanceProtected.Contains(renderer) || _performanceMasks.ContainsKey(renderer)
                || !PerformanceCanHide(renderer)) return;
            // Release current native-material/substitute consumers BEFORE registering
            // ownership, otherwise their undo path could clear the new wall mask.
            ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);
            bool prior = renderer.forceRenderingOff;
            _performanceMasks.Add(renderer, prior);
            renderer.forceRenderingOff = true;
        }

        private bool PerformanceCanHide(Renderer renderer)
        {
            if (renderer == null || IsModObject(renderer) || IsFigureOrActorRenderer(renderer)
                || FloorNeverFades(renderer) || HeldNeverFades(renderer)) return false;
            Bounds bounds = WallCommitGeometryReads.Read(renderer);
            return !IsWaterProtected(bounds) && !IsArchProtected(bounds, renderer.name)
                && !IsArchMountedEffect(renderer);
        }

        private void ReleasePerformanceMask(Renderer renderer)
        {
            if (!_performanceMasks.TryGetValue(renderer, out bool prior)) return;
            // Drop membership before consumer restitution so returning-to-Regular can recover
            // its original path. Native-enabled/activation changes while hidden are preserved.
            _performanceMasks.Remove(renderer);
            if (renderer == null) return;
            ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);
            renderer.forceRenderingOff = prior;
            // Re-evaluate an existing cooperating scenery claim only after exact wall
            // ownership is gone. Do not call this during temporary collector unmasking.
            _performanceMaskRestored?.Invoke(renderer);
        }

        private void ResetPerformanceVisibility(string reason)
        {
            bool wasHidden = _performanceWallsHidden;
            _performanceWallsHidden = false;
            _performanceRetired.Clear();
            foreach (Renderer renderer in _performanceMasks.Keys) _performanceRetired.Add(renderer);
            foreach (Renderer renderer in _performanceRetired) ReleasePerformanceMask(renderer);
            _performanceMasks.Clear(); _performanceWanted.Clear(); _performanceProtected.Clear();
            _performanceRetired.Clear(); _performanceClock.Reset(); _performanceAutoLatched = false;
            _performanceScene = -1; _performanceGenerator = null; _performanceReadySince = -1f;
            _performanceRevision = _performanceCollectedRevision = _performanceCycleRevision = 0;
            _performanceHeldRoots.Clear(); _performanceHeldScratch.Clear();
            _performanceHeldNext.Clear(); _performanceHeldRenderers.Clear();
            _performanceRetryAt = 0f; _performanceRoomCount = _performanceWallCount = -1;
            if (!wasHidden) return;
            AbandonRescanCycle(); _nextRescan = 0f; _nextEvalTime = 0f; _lastEvalTime = 0f;
            VRLog.Info(Name, $"PERFORMANCE WALLS RESTORED: {reason}; original view-dependent fading resumes when enabled.");
        }
    }
}
