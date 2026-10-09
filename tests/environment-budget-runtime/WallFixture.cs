using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver
    {
        internal static int RigPoseVersion => 0;
        internal static Transform? RigRoot => null;
        internal static Camera? HeadCamera;
    }
}

namespace GloomhavenVR.Core
{
    // The production sampler body is unchanged. Only its native frame-count read is
    // aliased by the binder so a synchronous Editor runner can exercise successive
    // draw phases without pretending Camera.Render advances Unity's player loop.
    internal static class WallFixtureClock { public static int frameCount; }
    // Only scene/attachment bookkeeping is a boundary: this fixture has one original
    // wall renderer and no attachments, floor or held props. The runner compiles the
    // complete original Apply/EnsureTextures, shared ramp and wall animation clock.
    internal static class ScenarioSceneryBudget
    {
        internal static bool IsOwnedHidden(Renderer renderer) => false;
    }
    internal static partial class WallSegmentFade
    {
        private const string Name = "WallSegmentFade";
        // Old animated-delivery lane deliberately models the new policy boundary. The
        // complete actual policy has its own wall-performance Unity lane.
        internal static bool PerformanceWallsHidden;

        private sealed class Segment
        {
            public Transform? DoorRoot => null;
            public readonly List<MeshRenderer> Renderers = new();
            public readonly List<object> Foliage = new(), Siblings = new(), Mounted = new(), Stacked = new(), Body = new();
            public float Fade;
            public bool VariantHigh;
            public bool HasBlock, DissolveCensusLogged;
            public float HeldCutoff => 0.5f;
        }
        private sealed class MountedProp
        {
            public Renderer Renderer=null!;
            public int CutoffId=-1;
            public float BaseCutoff=.5f;
        }
        private sealed partial class FadeDriver
        {
            private const float FadeTauSeconds = OcclusionFade.FadeTauSeconds;
            private static readonly int NativeMapEnableId = Shader.PropertyToID("_EnableOcclusionMap");
            private static readonly int ToggleWallFadeId = Shader.PropertyToID("ToggleWallFade"),
                ToggleWallfadeMatId = Shader.PropertyToID("_ToggleWallfade"),
                WallFadeOnMatId = Shader.PropertyToID("_WallFade_On"),
                TilesOcclusionMapId = Shader.PropertyToID("_TilesOcclusionMap"),
                CutoffId = Shader.PropertyToID("_Cutoff");
            private Texture2D? _noiseTex, _occludedTex;
            private MaterialPropertyBlock? _mpb, _mountedMpb;
            private float _nextRescan;
            private enum TickPhase { ApplyFoliage, ApplySiblings, ApplyMounted, ApplyStacked, ApplyBody, ApplyWall }
            private readonly struct EmptyScope : IDisposable { public void Dispose() { } }
            private static EmptyScope ApplyPhase(TickPhase phase) => new EmptyScope();
            private static void NoteTickWalk(TickPhase phase, int n) { }
            private static void ApplyFoliage(Segment seg) { }
            private static void ApplySiblings(Segment seg) { }
            private static void ApplyMounted(Segment seg) { }
            private static void ApplyStacked(Segment seg) { }
            private static void ApplyBody(Segment seg) { }
            private static void LogDissolveCensus(Segment seg) { }
            private static void NoteBlockEdge(Segment seg, bool installed) { }
            private static void NoteAnimationPath(Segment seg, bool smooth) { }
            private static bool FloorNeverFades(Renderer r) => false;
            private static bool HeldNeverFades(Renderer r) => false;
            private static void ShowIfWeHid(Renderer r) { }
            internal readonly Segment FixtureSegment = new();
            private readonly Dictionary<int, Segment> TraceSegments = new();
            internal void EnableFixture() => OnEnable();
            private void ResetPerformanceVisibility(string reason) { PerformanceWallsHidden=false; }
            private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ClearWallDrawTrace();
            internal float Step(float delta, bool target)
            {
                WallFixtureClock.frameCount++;
                FixtureSegment.Fade = OcclusionFade.Ramp(FixtureSegment.Fade, target ? 1f : 0f,
                    OcclusionFade.StepFactor(AnimationDelta(delta), FadeTauSeconds));
                Apply(FixtureSegment);
                return FixtureSegment.Fade;
            }
            internal void SetHigh(bool high) => FixtureSegment.VariantHigh = high;
            internal void PresentMounted(Renderer renderer,float fade)
            {DriveNativeProp(new MountedProp {Renderer=renderer},fade);}
            internal void Present(float fade)
            { WallFixtureClock.frameCount++; FixtureSegment.Fade=fade; Apply(FixtureSegment); }
            internal void DisposeFixture()
            {
                OnDisable();
                if (_noiseTex != null) UnityEngine.Object.DestroyImmediate(_noiseTex);
                if (_occludedTex != null) UnityEngine.Object.DestroyImmediate(_occludedTex);
                GC.KeepAlive(_nextRescan);
            }
            internal void TraceWrite(MeshRenderer renderer, int id, float fade)
            {
                if (!TraceSegments.TryGetValue(id, out Segment segment))
                    TraceSegments.Add(id, segment = new Segment());
                segment.Renderers.Clear(); segment.Renderers.Add(renderer); segment.Fade = fade;
                WallFixtureClock.frameCount++;
                Apply(segment);
            }
        }
        internal sealed class Fixture : IDisposable
        {
            private readonly FadeDriver driver = new();
            internal Fixture(MeshRenderer renderer)
            { driver.FixtureSegment.Renderers.Add(renderer); driver.EnableFixture(); }
            internal float Step(float delta, bool target) => driver.Step(delta, target);
            internal void SetHigh(bool high) => driver.SetHigh(high);
            internal void PresentMounted(Renderer renderer,float fade) => driver.PresentMounted(renderer,fade);
            internal void Present(float fade) => driver.Present(fade);
            internal void TraceWrite(MeshRenderer renderer, int segment, float fade) => driver.TraceWrite(renderer, segment, fade);
            public void Dispose() => driver.DisposeFixture();
        }
    }
}
