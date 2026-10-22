using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine.XR;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
if (args.Length == 1)
{
    string managed = Path.GetFullPath(args[0]);
    AssemblyLoadContext.Default.Resolving += (context, name) =>
    {
        string path = Path.Combine(managed, name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    };
    Type native = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "UnityEngine.XRModule.dll"))
        .GetType("UnityEngine.XR.XRDisplaySubsystem", true)!;
    foreach (string name in new[] { "TryGetAppGPUTimeLastFrame", "TryGetDroppedFrameCount", "TryGetFramePresentCount", "TryGetMotionToPhoton" })
    {
        Type parameter = name.Contains("Count") ? typeof(int) : typeof(float);
        MethodInfo? method = native.GetMethod(name, new[] { parameter.MakeByRefType() });
        Check(method != null && !method.IsStatic && method.ReturnType == typeof(bool) && method.GetParameters()[0].IsOut,
            "actual Unity display provider ABI: " + name);
    }
    Console.WriteLine("Actual owned Unity provider metadata verified; no engine callback invoked.");
}
var display = new XRDisplaySubsystem { running = true, GpuSeconds = .0125f, GpuAvailable = true };
UnityEngine.SubsystemManager.Current = display;
PerfMonitor.ResolveProbe();
Check(PerfMonitor.GpuMs(out float ms) && Math.Abs(ms - 12.5f) < .0001f, "provider seconds become millisecond samples");
Check(display.GpuCalls == 1 && XRStats.Calls == 0, "provider sample is authoritative before legacy fallback");
Check(PerfMonitor.DescribeProbe().Contains("gpuTime=ok"), "provider method binding appears in capabilities");
foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.MaxValue })
{
    display.GpuSeconds = invalid;
    Check(!PerfMonitor.GpuMs(out ms) && ms == 0f, "invalid provider data remains unavailable");
}
display.GpuSeconds = .02f; display.GpuAvailable = false;
Check(!PerfMonitor.GpuMs(out ms) && ms == 0f, "provider false does not become an invented zero sample");
XRStats.Available = true; XRStats.Milliseconds = 37.5f;
Check(PerfMonitor.GpuMs(out ms) && ms == 37.5f, "legacy millisecond fallback is not multiplied again");
XRStats.Available = false; display.GpuAvailable = true; display.GpuSeconds = 0f;
Check(PerfMonitor.GpuMs(out ms) && ms == 0f, "reported genuine zero is distinct from unavailable");
PerfMonitor.MarkProbe(); display.Dropped += 3; display.Presented += 4;
var text = new StringBuilder(); PerfMonitor.AppendProbe(text);
Check(PerfMonitor.RefreshProbe(out string source) && source == "XRDisplaySubsystem.TryGetDisplayRefreshRate", "display refresh remains native");
UnityEngine.SubsystemManager.Current = null;
Check(!PerfMonitor.GpuMs(out _), "absent display retains unavailable fallback");
Console.WriteLine($"XR provider probe: {checks} assertions passed.");

namespace GloomhavenVR.Core
{
    internal static partial class PerfMonitor
    {
        internal static void ResolveProbe() => XrProbe.Resolve();
        internal static bool GpuMs(out float ms) => XrProbe.TryGetGpuTimeMs(out ms);
        internal static string DescribeProbe() => XrProbe.Describe();
        internal static void MarkProbe() => XrProbe.Mark();
        internal static void AppendProbe(StringBuilder result) => XrProbe.AppendWindowStats(result);
        internal static bool RefreshProbe(out string source) => XrProbe.TryGetRefreshRate(out source);
    }
    internal static class PerfConfig
    {
        internal static bool IsBound => true;
        internal static Setting XrStats = new();
        internal sealed class Setting { internal bool Value => true; }
    }
}
namespace UnityEngine
{
    internal static class SubsystemManager
    {
        internal static XRDisplaySubsystem? Current;
        internal static void GetInstances(List<XRDisplaySubsystem> result) { if (Current != null) result.Add(Current); }
    }
}
namespace UnityEngine.XR
{
    internal sealed class XRDisplaySubsystem
    {
        internal bool running, GpuAvailable;
        internal float GpuSeconds;
        internal int GpuCalls, Dropped = 10, Presented = 20;
        public bool TryGetAppGPUTimeLastFrame(out float value) { GpuCalls++; value = GpuSeconds; return GpuAvailable; }
        public bool TryGetDroppedFrameCount(out int value) { value = Dropped; return true; }
        public bool TryGetFramePresentCount(out int value) { value = Presented; return true; }
        public bool TryGetMotionToPhoton(out float value) { value = .015f; return true; }
        public bool TryGetDisplayRefreshRate(out float value) { value = 72f; return true; }
    }
    internal static class XRStats
    {
        internal static bool Available;
        internal static float Milliseconds;
        internal static int Calls;
        public static bool TryGetGPUTimeLastFrame(out float value) { Calls++; value = Milliseconds; return Available; }
        public static bool TryGetDroppedFrameCount(XRDisplaySubsystem display, out int value) { value = -100; return true; }
        public static bool TryGetFramePresentCount(XRDisplaySubsystem display, out int value) { value = -100; return true; }
        public static bool TryGetMotionToPhoton(XRDisplaySubsystem display, out float value) { value = -100; return true; }
    }
    internal static class XRDevice { public static float refreshRate => 72f; }
}
