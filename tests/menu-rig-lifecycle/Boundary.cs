using System;
using System.Collections.Generic;
using UnityEngine;

// Explicit host boundaries only. The complete production SceneHeadOutput,
// SteamLinkSceneRuntime, coverage, output and material scope are compiled beside
// this file. No renderer, callback, ownership method or GPU output is mocked.
internal static class LoadingSourceBoundary
{
    internal static Camera.MonoOrStereoscopicEye CurrentEye = Camera.MonoOrStereoscopicEye.Left;
    internal static int Warnings, Prepared, EyeReads;
    internal static int FrameCount, OutputCalls;
    internal static GloomhavenVR.Core.SteamLink.SceneEyeResult OutputObserved(GloomhavenVR.Core.SteamLink.SceneEyeResult result, Camera.MonoOrStereoscopicEye eye)
    {
        if (result == GloomhavenVR.Core.SteamLink.SceneEyeResult.Rendered) OutputCalls++;
        return result;
    }
    internal static Camera.MonoOrStereoscopicEye EyeMetadata(Camera camera)
    { EyeReads++; return CurrentEye; }
    internal static bool StereoEnabled(Camera camera) => true;
    internal static class XrSettings
    {
        // Matches the fixture's real 256x256 per-eye camera target.
        internal static int eyeTextureWidth => 256;
        internal static int eyeTextureHeight => 256;
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
        internal static void RestoreAll() { SteamLink.SteamLinkSceneRuntime.Exit(); }
        internal static void TickBoundary() {
            if (Enabled.Value && VRCameraPolicy.AllowedHead != null)
                SteamLink.SteamLinkSceneRuntime.TryEnter(VRCameraPolicy.AllowedHead);
            else RestoreAll();
        }
    }
    internal static class VRSession { internal static bool IsRunning = true; internal static bool? InputFocus = true; }
    internal static class FrameNativePassthrough { internal static bool Required = false; }
    internal static class VRCameraPolicy {
        internal static Camera? AllowedHead;
        internal static void Sweep(string reason) { }
        internal static void RestoreAll() { AllowedHead = null; }
    }
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
        internal static bool CanAcceptOrdinaryRgba=true;
        internal static void Reset() { }
    }
    internal static class ScenePayloadAssets
    {
        private static AssetBundle? bank;
        private static Shader? coverage;
        private static readonly Dictionary<string, Shader> variants = new Dictionary<string, Shader>(StringComparer.Ordinal);
        internal static void Load(string path)
        {
            bank = AssetBundle.LoadFromFile(path);
            if (!bank) throw new InvalidOperationException("Shipping asset bundle missing.");
            foreach (Shader shader in bank!.LoadAllAssets<Shader>())
            {
                if (shader.name == SceneEyeCoverage.ShaderName) coverage = shader;
                const string suffix = ".SceneCoverage";
                if (shader.name.EndsWith(suffix, StringComparison.Ordinal))
                    variants.Add(shader.name.Substring(0, shader.name.Length - suffix.Length), shader);
            }
        }
        internal static bool TryGet(out Shader foundCoverage, out string reason)
        {
            foundCoverage = coverage!; reason = string.Empty;
            return coverage && coverage!.isSupported;
        }
        internal static Shader? FindVariant(string original)
            => variants.TryGetValue(original, out Shader shader) ? shader : null;
        internal static void Close() { if (bank) bank!.Unload(true); bank = null; variants.Clear(); }
    }
}
