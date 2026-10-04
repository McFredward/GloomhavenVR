using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver
    {
        internal static int RigPoseVersion => 0;
        internal static Transform? RigRoot => null;
    }
}

namespace GloomhavenVR.Core
{
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
        private sealed class Segment
        {
            public Transform? DoorRoot => null;
            public readonly List<MeshRenderer> Renderers = new();
            public readonly List<object> Foliage = new(), Siblings = new(), Mounted = new(), Stacked = new(), Body = new();
            public float Fade;
            public bool HasBlock, DissolveCensusLogged;
            public float HeldCutoff => 0.5f;
        }
        private sealed partial class FadeDriver
        {
            private const float FadeTauSeconds = OcclusionFade.FadeTauSeconds;
            private static readonly int ToggleWallFadeId = Shader.PropertyToID("ToggleWallFade"),
                ToggleWallfadeMatId = Shader.PropertyToID("_ToggleWallfade"),
                WallFadeOnMatId = Shader.PropertyToID("_WallFade_On"),
                TilesOcclusionMapId = Shader.PropertyToID("_TilesOcclusionMap"),
                CutoffId = Shader.PropertyToID("_Cutoff");
            private Texture2D? _noiseTex, _occludedTex;
            private MaterialPropertyBlock? _mpb;
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
            internal float Step(float delta, bool target)
            {
                FixtureSegment.Fade = OcclusionFade.Ramp(FixtureSegment.Fade, target ? 1f : 0f,
                    OcclusionFade.StepFactor(AnimationDelta(delta), FadeTauSeconds));
                Apply(FixtureSegment);
                return FixtureSegment.Fade;
            }
            internal void DisposeFixture()
            {
                if (_noiseTex != null) UnityEngine.Object.DestroyImmediate(_noiseTex);
                if (_occludedTex != null) UnityEngine.Object.DestroyImmediate(_occludedTex);
                GC.KeepAlive(_nextRescan);
            }
        }
        internal sealed class Fixture : IDisposable
        {
            private readonly FadeDriver driver = new();
            internal Fixture(MeshRenderer renderer) => driver.FixtureSegment.Renderers.Add(renderer);
            internal float Step(float delta, bool target) => driver.Step(delta, target);
            public void Dispose() => driver.DisposeFixture();
        }
    }
}
