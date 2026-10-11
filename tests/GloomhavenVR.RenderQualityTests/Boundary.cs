using System;
using System.Collections.Generic;
// This is an explicit XR-provider boundary model, not a headset/pixel proof. The complete
// production RenderQuality source executes its real lifecycle, Update and diagnostic paths.
namespace BepInEx.Configuration
{
    public sealed class ConfigDescription { public ConfigDescription(string text, object? acceptable = null) { } }
    public sealed class AcceptableValueList<T> { public AcceptableValueList(params T[] values) { } }
    public sealed class AcceptableValueRange<T> { public AcceptableValueRange(T a, T b) { } }
    public sealed class ConfigEntry<T> { public ConfigEntry(T value) { Value = value; } public T Value { get; set; } }
    public sealed class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T value, object description) => new(value);
    }
}
namespace UnityEngine
{
    public class Object { public static T[] FindObjectsOfType<T>() => Array.Empty<T>(); }
    public class Camera : Object
    {
        public static Camera? current;
        public bool allowMSAA = true, allowHDR;
        public RenderingPath actualRenderingPath = RenderingPath.Forward;
        public string depthTextureMode = "None";
        public RenderTexture? targetTexture;
    }
    public sealed class RenderTexture { public string name = "fixture"; }
    public enum RenderingPath { Forward, DeferredShading, DeferredLighting }
    public enum AnisotropicFiltering { Disable, Enable, ForceEnable }
    public enum LightType { Point, Spot, Directional, Area }
    public enum LightRenderMode { Auto, ForceVertex, ForcePixel }
    public enum LightmapBakeType { Realtime, Mixed, Baked }
    public enum LightShadows { None, Hard, Soft }
    public struct LightBakingOutput { public LightmapBakeType lightmapBakeType; }
    public class Light : Object
    {
        public LightType type;
        public LightRenderMode renderMode;
        public LightBakingOutput bakingOutput;
        public LightShadows shadows;
    }
    public static class Time { public static float unscaledTime; }
    public static class Mathf
    {
        public static float Abs(float a) => MathF.Abs(a);
        public static float Round(float a) => MathF.Round(a);
        public static float Clamp(float a, float min, float max) => Math.Clamp(a,min,max);
        public static float Min(float a, float b) => MathF.Min(a,b);
        public static float Max(float a, float b) => MathF.Max(a,b);
        public static int Max(int a, int b) => Math.Max(a,b);
    }
    public static class QualitySettings
    {
        private static int _antiAliasing;
        public static int AntiAliasingWrites;
        public static int antiAliasing
        {
            get => _antiAliasing;
            set { XR.XRSettings.RecordResourceWrite(); AntiAliasingWrites++; _antiAliasing=value; }
        }
        public static void ResetAntiAliasing() { _antiAliasing=0; AntiAliasingWrites=0; }
        public static int pixelLightCount;
        public static string[] names = { "Fastest" };
        public static int GetQualityLevel() => 0;
        public static float shadowDistance = 150;
        public static AnisotropicFiltering anisotropicFiltering;
        public static int masterTextureLimit;
        public static bool streamingMipmapsActive;
        public static float streamingMipmapsMemoryBudget = 900;
        public static int streamingMipmapsMaxLevelReduction = 2;
    }
    public static class Texture { public static void SetGlobalAnisotropicFilteringLimits(int a,int b) { } }
    public struct RenderTextureDescriptor
    {
        public int width,height,msaaSamples;
        public string colorFormat,dimension;
        public bool sRGB;
    }
    public static class SystemInfo { public static string graphicsDeviceType => "provider-model"; }
    public static class SubsystemManager
    {
        public static readonly List<XR.XRDisplaySubsystem> Displays = new();
        public static void GetInstances(List<XR.XRDisplaySubsystem> result) { result.Clear(); result.AddRange(Displays); }
    }
}
namespace UnityEngine.XR
{
    public static class XRSettings
    {
        public enum StereoRenderingMode { MultiPass, SinglePass }
        public static StereoRenderingMode stereoRenderingMode => StereoRenderingMode.MultiPass;
        public static string loadedDeviceName => "explicit provider model";
        public static int eyeTextureWidth = 3408, eyeTextureHeight = 3408;
        public static RenderTextureDescriptor eyeTextureDesc => new() { width=eyeTextureWidth,height=eyeTextureHeight,msaaSamples=1,colorFormat="ARGB32",dimension="Tex2D" };
        public static bool Live, RefuseViewport, ProviderInitialized;
        public static int ResourceWritesBeforeInitialization;
        public static int AllocationWrites, LiveAllocationWrites, ViewportWrites;
        private static float _allocation=1f, _viewport=1f;
        public static float eyeTextureResolutionScale
        {
            get => _allocation;
            set { RecordResourceWrite(); AllocationWrites++; if (Live) LiveAllocationWrites++; _allocation=value; }
        }
        public static float renderViewportScale
        {
            get => _viewport;
            set { RecordResourceWrite(); ViewportWrites++; if (!RefuseViewport) _viewport=value; }
        }
        public static void RecordResourceWrite()
        {
            if (ProviderInitialized) return;
            ResourceWritesBeforeInitialization++;
            throw new InvalidOperationException("native render resource touched before provider initialization");
        }
        public static void AcceptDisplayAllocation(float value) => _allocation=value;
        public static void AcceptDisplayViewport(float value) => _viewport=value;
        public static void Reset()
        { _allocation=1; _viewport=1; Live=false; RefuseViewport=false; ProviderInitialized=false; ResourceWritesBeforeInitialization=0; AllocationWrites=LiveAllocationWrites=ViewportWrites=0; QualitySettings.ResetAntiAliasing(); }
    }
    public sealed class XRDisplaySubsystem
    {
        public bool running;
        public bool RejectStartupAllocation;
        public int AllocationWrites, MsaaWrites, ViewportWrites;
        private float _allocation=1, _viewport=1;
        public float scaleOfAllRenderTargets
        { get => _allocation; set { XRSettings.RecordResourceWrite(); AllocationWrites++; if(running) throw new InvalidOperationException("live display allocation setter reached"); if(RejectStartupAllocation) throw new InvalidOperationException("startup display allocation rejected"); _allocation=value; XRSettings.AcceptDisplayAllocation(value); } }
        public float scaleOfAllViewports { get => _viewport; set { XRSettings.RecordResourceWrite(); ViewportWrites++; _viewport=value; XRSettings.AcceptDisplayViewport(value); } }
        public int Samples;
        public void SetMSAALevel(int value) { XRSettings.RecordResourceWrite(); MsaaWrites++; Samples=value; }
        public int GetRenderPassCount() => 2;
        public void GetRenderPass(int index, out XRRenderPass pass) { pass=new(); }
        public struct XRRenderParameter { public string viewport; }
        public struct XRRenderPass
        {
            public RenderTextureDescriptor renderTargetDesc => XRSettings.eyeTextureDesc;
            public int GetRenderParameterCount() => 1;
            public void GetRenderParameter(Camera head, int index, out XRRenderParameter p)
            { p=new() { viewport="modeled viewport" }; }
        }
    }
}
namespace GloomhavenVR.Core
{
    internal static class PerfTextureCensus { }
    internal static class PerfMonitor { internal static void MarkChange(string reason) { } }
    internal static class ModuleConfig { internal static BepInEx.Configuration.ConfigFile Create(string module) => new(); }
    internal static class VRSession { internal static bool IsRunning; }
    internal static class VRLog
    {
        internal static readonly List<string> Lines = new();
        internal static void Info(string area,string text) => Lines.Add(text);
        internal static void Note(string area,string text) => Lines.Add(text);
        internal static void Warn(string area,string text) => Lines.Add(text);
    }
    internal static class SkyAlternative { internal static void BindConfig(object file) { } }
    internal static class ElementMood { internal static void BindConfig(object file) { } }
    internal static class Haunt { internal static void BindConfig(object file) { } }
    internal static class EnvSound { internal static void BindConfig(object file) { } }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static UnityEngine.Camera? HeadCamera = new(); }
    internal static class LightStabiliser
    { internal static void BindConfig(object file) { } internal static void Tick(int cap) { } }
}
