using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core
{
    // Environment/terrain ownership delegates are explicit boundaries in this lane.
    // The actual world material owner, native-write release and camera culls execute.
    internal static class ScenarioEnvironmentBudget
    {
        internal static Material CanonicalMaterial(Material material) => WorldMaterialBudget.CanonicalMaterial(material);
        internal static void BeforeNativeRendererWrite(Renderer renderer) => WorldMaterialBudget.BeforeNativeRendererWrite(renderer);
    }
    internal static partial class WallSegmentFade
    {
        private const string Name = "WallSegmentFade";
        private sealed class MountedProp
        {
            public Renderer Renderer = null!;
            public ParticleSystem? System = null;
            public int ColorId = -1, CutoffId = -1, DissolveToggleId = -1, DissolveControlId = -1;
            public Color BaseColor = Color.white;
            public float BaseCutoff = .35f, BaseDissolveControl = 0, StartSize = 0, EmissionRate = 0;
            public ParticleSystem.MinMaxGradient StartColor = default;
            public bool HasStartColor = false, NativeFade = false, SwapChecked = false;
            public string Tier = "none";
            public string? DissolveWhy = null;
            public Material[]? SwapOriginals = null, SwapCopies = null;
            public bool[]? SwapOwned = null;
        }
        private sealed partial class FadeDriver
        {
            private readonly List<Material> _matScratch = new();
            private readonly Dictionary<Shader, bool> _shaderVerdict = new();
            private Shader? _masonryFadeShader;
            private int _nativeTotal, _swapTotal;
            private float _nextSwapLog;
            private static bool _censusMaterialsDirty;
            private Texture2D? _noiseTex, _occludedTex;
            private MaterialPropertyBlock? _mountedMpb;
            private const string WallFadeOnKeyword = "_WALLFADE_ON_ON";
            private static readonly int TintColorId = Shader.PropertyToID("_TintColor"),
                ColorPropId = Shader.PropertyToID("_Color"), BaseColorId = Shader.PropertyToID("_BaseColor"),
                CutoffId = Shader.PropertyToID("_Cutoff"), ToggleDissolvePropId = Shader.PropertyToID("_Toggle_Dissolve"),
                InvisibilityControlPropId = Shader.PropertyToID("_InvisibilityControl"),
                NativeMapEnableId = Shader.PropertyToID("_EnableOcclusionMap"),
                ToggleWallFadeId = Shader.PropertyToID("ToggleWallFade"), ToggleWallfadeMatId = Shader.PropertyToID("_ToggleWallfade"),
                WallFadeOnMatId = Shader.PropertyToID("_WallFade_On"), ToggleWallFadeLocalMatId = Shader.PropertyToID("_ToggleWallFadeLocal"),
                TilesOcclusionMapId = Shader.PropertyToID("_TilesOcclusionMap");
            internal static readonly HashSet<Renderer> Floors = new(), Held = new();
            private static bool FloorNeverFades(Renderer renderer) => Floors.Contains(renderer);
            private static bool HeldNeverFades(Renderer renderer) => Held.Contains(renderer);
            private static void RestoreHeldProp(MountedProp prop, Renderer renderer)
            { RestorePropSwap(prop, renderer); ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer); renderer.SetPropertyBlock(null); }
            private static string RendererKind(Renderer renderer) => renderer.GetType().Name;
            // Existing immutable shader-name memo has independent source coverage.
            private static bool IsWallFadeShaderName(string name) => name.Contains("WallFade");
            internal MountedProp Classify(Renderer renderer) => ClassifyProp(renderer);
            internal void Ensure(MountedProp prop) => EnsureDissolveChannel(prop);
            internal void Drive(MountedProp prop, float fade) => DriveProp(prop, fade);
            internal int Predict(Renderer renderer) => PredictClassOfRenderer(renderer);
            internal bool Capable(MeshRenderer renderer) => RendererIsWallFadeCapable(renderer);
            internal bool Named(MeshRenderer renderer) => RendererUsesWallFade(renderer);
            internal void Donor(Material material) => CaptureMasonryTemplate(material);
            internal Shader? DonorShader => _masonryFadeShader;
            internal void Restore(MountedProp prop)
            { RestorePropSwap(prop, prop.Renderer); ScenarioEnvironmentBudget.BeforeNativeRendererWrite(prop.Renderer); prop.Renderer.SetPropertyBlock(null); }
            internal void Dispose()
            {
                if (_noiseTex != null) UnityEngine.Object.DestroyImmediate(_noiseTex);
                if (_occludedTex != null) UnityEngine.Object.DestroyImmediate(_occludedTex);
                GC.KeepAlive(_nativeTotal); GC.KeepAlive(_censusMaterialsDirty);
                Floors.Clear(); Held.Clear();
            }
        }
        internal sealed class AttachmentFixture : IDisposable
        {
            private readonly FadeDriver driver = new();
            private MountedProp? prop;
            internal void Attach(Renderer renderer) { prop = driver.Classify(renderer); driver.Ensure(prop); }
            internal bool Native => prop!.NativeFade;
            internal int ColorChannel => prop!.ColorId;
            internal Material[]? Originals => prop!.SwapOriginals;
            internal bool Capable(MeshRenderer renderer) => driver.Capable(renderer);
            internal bool Named(MeshRenderer renderer) => driver.Named(renderer);
            internal int Predict(Renderer renderer) => driver.Predict(renderer);
            internal void Donor(Material material) => driver.Donor(material);
            internal Shader? DonorShader => driver.DonorShader;
            internal void Present(float fade) => driver.Drive(prop!, fade);
            internal void Guard(Renderer renderer, bool floor, bool held)
            { if (floor) FadeDriver.Floors.Add(renderer); else FadeDriver.Floors.Remove(renderer); if (held) FadeDriver.Held.Add(renderer); else FadeDriver.Held.Remove(renderer); }
            internal void Restore() { if (prop != null) driver.Restore(prop); }
            public void Dispose() { Restore(); driver.Dispose(); }
        }
    }
}
