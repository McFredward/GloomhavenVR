using System;
using System.Linq;
using System.Runtime.InteropServices;
using GloomhavenVR.Core;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.NativeTypes;

internal static class Program
{
    private static int _assertions;

    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        foreach (int[] modes in new[] { new[] { 1 }, new[] { 1, 3 }, new[] { 2, 1 }, new[] { 1, 1000000123 } })
        {
            using var native = new FakeNative { Modes = modes };
            var probe = new OpenXrEnvironmentBlendProbe();
            probe.BeginInstance(native.Instance, native.Proc);
            var result = probe.ObserveSystem(native.System);
            Check(result is { IsComplete: true }, "successful native enumeration is complete");
            Check(result!.Modes.SequenceEqual(modes), "native preference order and unknown future modes retained");
            Check(native.ResolveCalls == 1 && native.EnumCalls == 2, "exactly one lookup and two enum calls");
            Check(probe.ObserveSystem(native.System) == null && native.EnumCalls == 2, "same system never re-queried");
        }

        foreach (string fault in new[] { "resolve", "null", "count", "zero", "oversized", "enum", "grown", "empty", "invalid", "unwritten", "exception" })
        {
            using var native = new FakeNative { Fault = fault };
            var probe = new OpenXrEnvironmentBlendProbe();
            probe.BeginInstance(native.Instance, native.Proc);
            var result = probe.ObserveSystem(native.System);
            Check(result is { IsComplete: false }, "fault remains unavailable: " + fault);
            Check(result!.Modes.Length == 0, "failure cannot masquerade as opaque-only success: " + fault);
            Check(result.Reason != null, "failure has bounded reason: " + fault);
            Check(native.EnumCalls <= 2, "query never retries: " + fault);
            Check(probe.ObserveSystem(native.System) == null, "failed pair still queried at most once: " + fault);
        }

        var missing = new OpenXrEnvironmentBlendProbe();
        Check(missing.ObserveSystem(42) == null, "no instance is never queried");
        missing.BeginInstance(8, IntPtr.Zero);
        Check(missing.ObserveSystem(0) == null, "no system is never queried");
        Check(missing.ObserveSystem(42) is { IsComplete: false }, "null resolver means unknown support");

        using (var native = new FakeNative())
        {
            var probe = new OpenXrEnvironmentBlendProbe();
            probe.BeginInstance(native.Instance, native.Proc);
            Check(probe.ObserveSystem(native.System) is { IsComplete: true }, "first instance queried");
            probe.EndInstance(native.Instance + 1);
            Check(probe.ObserveSystem(native.System) == null, "stale destruction cannot clear current instance");
            native.System++;
            Check(probe.ObserveSystem(native.System) is { IsComplete: true }, "new actual system gets its own query");
            native.System--;
            Check(probe.ObserveSystem(native.System) == null, "return to old system does not requery");
            probe.EndInstance(native.Instance);
            Check(probe.ObserveSystem(native.System) == null, "destroyed instance never queried");
            probe.BeginInstance(native.Instance, native.Proc);
            Check(probe.ObserveSystem(native.System) is { IsComplete: true }, "reused handle after recreation queried");
        }

        using (var native = new FakeNative { Modes = new[] { 1, 3 } })
        {
            OpenXRFeature.ProcAddress = native.Proc;
            VRLog.Lines.Clear();
            var feature = new OpenXrEnvironmentBlendFeature();
            Check(feature.Create(native.Instance), "diagnostic allows startup");
            feature.System(native.System);
            Check(VRLog.Lines.Count == 1 && VRLog.Lines[0].Contains("alphaBlend=yes"), "capability log reports successful alpha support");
            feature.System(native.System);
            Check(VRLog.Lines.Count == 1 && native.EnumCalls == 2, "duplicate lifecycle events are silent");
            feature.Active(XrEnvironmentBlendMode.Opaque);
            feature.Active(XrEnvironmentBlendMode.Opaque);
            Check(VRLog.Lines.Count == 2 && VRLog.Lines[1].Contains("active=Opaque"), "active mode is reported once from actual notification");
            feature.Loss(native.Instance);
            feature.Active(XrEnvironmentBlendMode.AlphaBlend);
            feature.System(native.System);
            Check(VRLog.Lines.Count == 2, "loss clears query and active notification state");
            Check(feature.Create(native.Instance), "recreated reused instance allows startup");
            feature.System(native.System);
            Check(VRLog.Lines.Count == 3 && native.EnumCalls == 4, "recreated instance is queried again");
            feature.Destroy(native.Instance + 1);
            feature.Active(XrEnvironmentBlendMode.Opaque);
            Check(VRLog.Lines.Count == 4, "stale destroy cannot clear current active notification");
            feature.Destroy(native.Instance);
            feature.Active(XrEnvironmentBlendMode.AlphaBlend);
            Check(VRLog.Lines.Count == 4, "destroy clears active notification state");
            // Instance callbacks mean a new actual instance, even if its numerical handle is reused.
            feature.Create(native.Instance);
            feature.System(native.System);
            feature.Create(native.Instance);
            feature.System(native.System);
            Check(native.EnumCalls == 8, "new creation callback never inherits a previous query");
        }
        using (var native = new FakeNative())
        {
            var probe = new OpenXrEnvironmentBlendProbe();
            probe.BeginInstance(native.Instance, native.Proc);
            for (int i = 0; i < OpenXrEnvironmentBlendProbe.MaximumSystems; i++)
            {
                native.System++;
                Check(probe.ObserveSystem(native.System) is { IsComplete: true }, "bounded distinct system queried");
            }
            native.System++;
            Check(probe.ObserveSystem(native.System) is { IsComplete: false }, "unexpected system flood reported once");
            native.System++;
            Check(probe.ObserveSystem(native.System) == null, "system flood does not grow storage or normal logs");
            Check(native.EnumCalls == 32, "system diagnostic cap avoids additional native calls");
            probe.Clear();
            probe.BeginInstance(native.Instance, native.Proc);
            Check(probe.ObserveSystem(native.System) is { IsComplete: true }, "new instance resets system cap");
        }
        using (var native = new FakeNative { Fault = "resolve" })
        {
            OpenXRFeature.ProcAddress = native.Proc;
            VRLog.Lines.Clear();
            var feature = new OpenXrEnvironmentBlendFeature();
            Check(feature.Create(native.Instance), "query failure does not veto startup");
            feature.System(native.System);
            Check(VRLog.Lines.Count == 1 && VRLog.Lines[0].Contains("query=unavailable") &&
                VRLog.Lines[0].Contains("support remains unknown"), "query failure does not claim unsupported");
        }
        Check(OpenXrEnvironmentBlendProbe.DescribeMode(1) == "Opaque" &&
            OpenXrEnvironmentBlendProbe.DescribeMode(2) == "Additive" &&
            OpenXrEnvironmentBlendProbe.DescribeMode(3) == "AlphaBlend", "OpenXR enum meanings match ABI");
        Console.WriteLine("OpenXR environment blend probe: " + _assertions + " assertions passed; fake-native/lifecycle boundary only, no headset or passthrough activation.");
    }

    private sealed class FakeNative : IDisposable
    {
        internal ulong Instance = 0x123456789abcde12;
        internal ulong System = 0x8877665544332211;
        internal int[] Modes = { 1 };
        internal string Fault = "";
        internal int ResolveCalls, EnumCalls;
        internal readonly OpenXrEnvironmentBlendProbe.GetInstanceProcAddr Resolver;
        internal readonly OpenXrEnvironmentBlendProbe.EnumerateEnvironmentBlendModes Enumerator;
        internal IntPtr Proc => Marshal.GetFunctionPointerForDelegate(Resolver);

        internal FakeNative() { Resolver = Resolve; Enumerator = Enumerate; }
        private int Resolve(ulong instance, string name, out IntPtr function)
        {
            ResolveCalls++;
            Check(instance == Instance, "uses actual 64-bit instance");
            Check(name == "xrEnumerateEnvironmentBlendModes", "only core read-only enumeration looked up");
            function = Fault == "null" ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(Enumerator);
            if (Fault == "exception") throw new InvalidOperationException("injected lookup failure");
            return Fault == "resolve" ? -7 : 0;
        }
        private int Enumerate(ulong instance, ulong system, int view, uint capacity, out uint count, IntPtr buffer)
        {
            EnumCalls++;
            Check(instance == Instance && system == System, "uses current actual instance and system");
            Check(view == 2, "uses PRIMARY_STEREO");
            count = (uint)Modes.Length;
            if (capacity == 0)
            {
                Check(buffer == IntPtr.Zero, "first query has no output buffer");
                if (Fault == "zero") count = 0;
                if (Fault == "oversized") count = 17;
                return Fault == "count" ? -1 : 0;
            }
            Check(buffer != IntPtr.Zero && capacity == Modes.Length, "second query uses exact bounded capacity");
            if (Fault == "grown") count = capacity + 1;
            if (Fault == "empty") count = 0;
            for (int i = 0; i < Modes.Length && Fault != "unwritten"; i++)
                Marshal.WriteInt32(buffer, i * sizeof(int), Fault == "invalid" ? 0 : Modes[i]);
            return Fault == "enum" ? -4 : 0;
        }
        public void Dispose() { GC.KeepAlive(Resolver); GC.KeepAlive(Enumerator); }
    }
}
