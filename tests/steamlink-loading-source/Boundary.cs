using System;
using System.Collections.Generic;
using UnityEngine;

// Explicit host boundaries only. The complete production SceneHeadOutput,
// SteamLinkSceneRuntime, coverage, packer and material scope are compiled beside
// this file. No renderer, callback, ownership method or GPU output is mocked.
internal static class LoadingSourceBoundary
{
    internal static Camera.MonoOrStereoscopicEye CurrentEye = Camera.MonoOrStereoscopicEye.Left;
    internal static int Warnings, Prepared, EyeReads;
    internal static int FrameCount;
    internal static Camera.MonoOrStereoscopicEye EyeMetadata(Camera camera)
    { EyeReads++; return CurrentEye; }
    internal static bool StereoEnabled(Camera camera) => true;
    internal static class XrSettings
    {
        internal enum StereoRenderingMode { MultiPass }
        internal static StereoRenderingMode stereoRenderingMode => StereoRenderingMode.MultiPass;
    }
}
namespace GloomhavenVR.Core
{
    internal sealed class FixtureChoice { internal bool Value; }
    internal static class MixedReality
    {
        internal static FixtureChoice Enabled = new FixtureChoice { Value = true };
        internal static FixtureChoice SteamLinkNative = new FixtureChoice { Value = true };
        internal static FixtureChoice SteamLinkEdgeQuality = new FixtureChoice { Value = true };
    }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class FrameNativePassthrough { internal static bool Required = false; }
    internal static class VRCameraPolicy { internal static Camera? AllowedHead; }
    internal static class StereoModeConfig
    {
        internal enum Mode { MultiPass }
        internal static Mode Current = Mode.MultiPass;
    }
    internal static class MrNativeCamera
    {
        // The separate MixedReality presentation fixture owns the actual camera
        // save/restore policy. This boundary merely asserts its admitted inputs.
        internal static void Apply(Camera camera)
        {
            if (camera.allowHDR || camera.clearFlags != CameraClearFlags.SolidColor)
                throw new InvalidOperationException("Fixture requires an admitted SDR solid-clear camera.");
        }
    }
    internal static class VRLog
    {
        internal static bool WantsDebug => false;
        internal static void Info(string category, string message) { LoadingSourceBoundary.Prepared++; }
        internal static void Warn(string category, string message) { LoadingSourceBoundary.Warnings++; }
        internal static void Debug(string category, string message) { }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class FlatScreen { internal static Renderer? OpaqueBackdrop => null; }
}
namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkConnection { internal static bool Available = true; }
    internal static class SteamLinkCoverageBridge
    {
        internal static bool CanAcceptOrdinaryRgba=false;
        internal static void Reset() { CanAcceptOrdinaryRgba=false; }
    }
    // External host metadata only. The actual lease/epoch authority executes in
    // steamlink-stream-quality; this GPU fixture does not claim encoder readiness.
    internal static class SteamLinkStreamQuality
    { internal static bool LegacyReleased=true; internal static bool HostPrepared(bool enabled, int width, int height) => true; }
    internal static class ScenePayloadAssets
    {
        private static AssetBundle? bank;
        private static Shader? pack, coverage;
        private static readonly Dictionary<string, Shader> variants = new Dictionary<string, Shader>(StringComparer.Ordinal);
        internal static void Load(string path)
        {
            bank = AssetBundle.LoadFromFile(path);
            if (!bank) throw new InvalidOperationException("Shipping asset bundle missing.");
            foreach (Shader shader in bank!.LoadAllAssets<Shader>())
            {
                if (shader.name == SceneAtlasProfile.ShaderName) pack = shader;
                if (shader.name == SceneEyeCoverage.ShaderName) coverage = shader;
                const string suffix = ".SceneCoverage";
                if (shader.name.EndsWith(suffix, StringComparison.Ordinal))
                    variants.Add(shader.name.Substring(0, shader.name.Length - suffix.Length), shader);
            }
        }
        internal static bool TryGet(out Shader foundPack, out Shader foundCoverage, out string reason)
        {
            foundPack = pack!; foundCoverage = coverage!; reason = string.Empty;
            return pack && coverage && pack!.isSupported && coverage!.isSupported;
        }
        internal static Shader? FindVariant(string original)
            => variants.TryGetValue(original, out Shader shader) ? shader : null;
        internal static void Close() { if (bank) bank!.Unload(true); bank = null; variants.Clear(); }
    }
}

namespace GloomhavenVR.Core.SteamLink
{
    // The optional Cheats probe owner has a separate lifecycle/real-Unity
    // fixture. Existing source tests keep the normal MR input unchanged.
    internal static class SteamLinkProjectionProbe
    {
        internal static void Tick() { }
        internal static void Stop() { }
        internal static void PackFailed() { }
        internal static UnityEngine.RenderTexture InputForFrame(UnityEngine.RenderTexture source,int frame) => source;
    }
}
