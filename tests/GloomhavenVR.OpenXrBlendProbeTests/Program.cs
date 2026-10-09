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
        TestPassthrough();
        Console.WriteLine("OpenXR environment blend probe: " + _assertions + " assertions passed; fake-native/lifecycle/control boundary only, no headset or passthrough support claim.");
    }

    private static void TestPassthrough()
    {
        GloomhavenVR.FrameDefaults.Active = true;
        foreach (int[] modes in new[] { new[] { 1 }, new[] { 1, 2 }, new[] { 1, 3 }, new[] { 3 } })
        {
            using var native = new FakeNative { Modes = modes };
            var feature = CreateSession(native);
            bool alpha = modes.Contains(3);
            Check(FrameNativePassthrough.Required, "standalone marker requires native compatibility");
            Check(FrameNativePassthrough.IsAvailable == alpha, "actual alpha capability gates support");
            Check(FrameNativePassthrough.Status == (alpha ? FrameNativePassthroughStatus.Available : FrameNativePassthroughStatus.Unsupported), "opaque/additive alone does not count as native passthrough");
            if (!alpha)
            {
                Check(!feature.TryEnterPassthrough(1), "unsupported runtime cannot enter");
                Check(OpenXRFeature.GetCalls == 0 && OpenXRFeature.SetCalls == 0, "unsupported frame never controls native blend");
            }
            feature.Destroy(native.Instance);
        }
        using (var native = new FakeNative { Modes = new[] { 1, 3 } })
        {
            var feature = CreateSession(native, false);
            Check(FrameNativePassthrough.Status == FrameNativePassthroughStatus.Checking, "enumerated support waits for live begun session");
            Check(!feature.TryEnterPassthrough(1) && OpenXRFeature.SetCalls == 0, "unbegun session cannot be changed");
            feature.OnSessionBegin(99);
            Check(!FrameNativePassthrough.IsAvailable, "stale begin callback does not authorize control");
            feature.OnSessionBegin(7);
            Check(!feature.TryEnterPassthrough(10), "queued native mode is not mistaken for acceptance");
            Check(OpenXRFeature.RequestedMode == XrEnvironmentBlendMode.AlphaBlend && OpenXRFeature.ActualMode == XrEnvironmentBlendMode.Opaque, "setter requests alpha without changing current mode");
            Check(FrameNativePassthrough.IsAvailable && !FrameNativePassthrough.IsActive, "pending activation retains available control without transparent presentation");
            Check(!feature.TryEnterPassthrough(11) && OpenXRFeature.SetCalls == 1, "pending request does not repeat native write");
            OpenXRFeature.ActualMode = XrEnvironmentBlendMode.AlphaBlend;
            feature.Active(XrEnvironmentBlendMode.AlphaBlend);
            Check(feature.TryEnterPassthrough(11.1) && FrameNativePassthrough.IsActive, "authoritative native readback accepts alpha");
            int reads = OpenXRFeature.GetCalls, writes = OpenXRFeature.SetCalls;
            int markerReads = GloomhavenVR.FrameDefaults.ActiveReads;
            for (int i = 0; i < 100; i++) Check(feature.TryEnterPassthrough(12 + i), "active composition stays available");
            Check(OpenXRFeature.GetCalls == reads && OpenXRFeature.SetCalls == writes && native.EnumCalls == 2, "steady state has no native reads/writes/capability query");
            Check(GloomhavenVR.FrameDefaults.ActiveReads == markerReads, "steady MR predicates never repeat marker filesystem checks");
            FrameNativePassthrough.Exit();
            Check(!FrameNativePassthrough.IsActive && OpenXRFeature.RequestedMode == XrEnvironmentBlendMode.Opaque, "off requests saved original mode");
            int afterExit = OpenXRFeature.SetCalls;
            FrameNativePassthrough.Exit();
            Check(OpenXRFeature.SetCalls == afterExit, "repeat exit cannot rewrite externally owned mode");
            feature.Destroy(native.Instance);
        }
        foreach (XrEnvironmentBlendMode original in new[] { XrEnvironmentBlendMode.Opaque, XrEnvironmentBlendMode.Additive, XrEnvironmentBlendMode.AlphaBlend })
        {
            using var native = new FakeNative { Modes = new[] { 1, 2, 3 } };
            var feature = CreateSession(native);
            OpenXRFeature.ActualMode = original;
            bool entered = feature.TryEnterPassthrough(1);
            Check(entered == (original == XrEnvironmentBlendMode.AlphaBlend), "already selected alpha needs no queued transition");
            FrameNativePassthrough.Exit();
            Check(OpenXRFeature.RequestedMode == (original == XrEnvironmentBlendMode.AlphaBlend ? null : original), "cancel pending restores original instead of hardcoding opaque");
            Check(OpenXRFeature.SetCalls == (original == XrEnvironmentBlendMode.AlphaBlend ? 0 : 2), "unmodified original alpha never claims ownership");
            feature.Destroy(native.Instance);
        }
        using (var native = new FakeNative { Modes = new[] { 1, 3 } })
        {
            var feature = CreateSession(native);
            Check(!feature.TryEnterPassthrough(1), "deadline case requests alpha");
            Check(!feature.TryEnterPassthrough(1 + OpenXrEnvironmentBlendFeature.ActivationTimeoutSeconds), "opaque readback after bounded deadline fails activation");
            Check(FrameNativePassthrough.Status == FrameNativePassthroughStatus.ActivationFailed && !FrameNativePassthrough.IsAvailable, "failed transition disables native MR");
            Check(OpenXRFeature.RequestedMode == XrEnvironmentBlendMode.Opaque, "deadline cancels delayed alpha request");
            int logs = VRLog.Lines.Count, calls = OpenXRFeature.GetCalls;
            for (int i = 0; i < 30; i++) Check(!feature.TryEnterPassthrough(20 + i), "failure cannot retry every frame");
            Check(VRLog.Lines.Count == logs && OpenXRFeature.GetCalls == calls, "failure log and native operations stay bounded");
            feature.OnSessionEnd(7);
            feature.OnSessionBegin(7);
            Check(FrameNativePassthrough.IsAvailable, "new session begin resets activation failure");
            feature.Destroy(native.Instance);
        }
        foreach (string fault in new[] { "read", "write", "query" })
        {
            using var native = new FakeNative { Modes = new[] { 1, 3 }, Fault = fault == "query" ? "count" : "" };
            var feature = CreateSession(native);
            OpenXRFeature.ThrowGet = fault == "read";
            OpenXRFeature.ThrowSet = fault == "write";
            Check(!feature.TryEnterPassthrough(1), "native failure cannot enter: " + fault);
            Check(FrameNativePassthrough.Status == (fault == "query" ? FrameNativePassthroughStatus.QueryFailed : FrameNativePassthroughStatus.ActivationFailed), "native failure has an honest unavailable state: " + fault);
            Check(!FrameNativePassthrough.IsAvailable && !FrameNativePassthrough.IsActive, "native failure leaves ordinary VR: " + fault);
            feature.Destroy(native.Instance);
        }
        foreach (string ending in new[] { "end", "exiting", "destroy", "loss", "instanceLoss", "instanceDestroy" })
        {
            using var native = new FakeNative { Modes = new[] { 1, 3 } };
            var feature = CreateSession(native);
            feature.TryEnterPassthrough(1);
            feature.OnSessionDestroy(99);
            Check(FrameNativePassthrough.IsAvailable, "stale destruction cannot affect current native session");
            int writes = OpenXRFeature.SetCalls;
            switch (ending)
            {
                case "end": feature.OnSessionEnd(7); break;
                case "exiting": feature.OnSessionExiting(7); break;
                case "destroy": feature.OnSessionDestroy(7); break;
                case "loss": feature.OnSessionLossPending(7); break;
                case "instanceLoss": feature.Loss(native.Instance); break;
                case "instanceDestroy": feature.Destroy(native.Instance); break;
            }
            Check(!FrameNativePassthrough.IsActive && !FrameNativePassthrough.IsAvailable, "ended session cannot retain passthrough: " + ending);
            Check(OpenXRFeature.SetCalls == writes + (ending is "end" or "exiting" ? 1 : 0), "only still-live session receives restore request: " + ending);
            feature.Destroy(native.Instance);
        }
        using (var native = new FakeNative { Modes = new[] { 1, 3 } })
        {
            var feature = CreateSession(native);
            feature.TryEnterPassthrough(1);
            OpenXRFeature.ActualMode = XrEnvironmentBlendMode.AlphaBlend;
            feature.TryEnterPassthrough(2);
            OpenXRFeature.ActualMode = XrEnvironmentBlendMode.Additive;
            feature.Active(XrEnvironmentBlendMode.Additive);
            int writes = OpenXRFeature.SetCalls;
            FrameNativePassthrough.Exit();
            Check(!FrameNativePassthrough.IsActive && FrameNativePassthrough.Status == FrameNativePassthroughStatus.ActivationFailed, "external mode change immediately revokes transparent presentation");
            Check(OpenXRFeature.SetCalls == writes, "external mode owner is not overwritten on exit");
            native.System++;
            feature.System(native.System);
            Check(FrameNativePassthrough.IsAvailable, "new actual system refreshes compatibility");
            native.System--;
            feature.System(native.System);
            Check(FrameNativePassthrough.IsAvailable && native.EnumCalls == 4, "returning current system reuses exact cached result without query");
            feature.Destroy(native.Instance);
        }
        using (var native = new FakeNative { Modes = new[] { 1, 3 } })
        {
            var feature = CreateSession(native);
            feature.TryEnterPassthrough(1);
            OpenXRFeature.ActualMode = XrEnvironmentBlendMode.AlphaBlend;
            feature.TryEnterPassthrough(2);
            int writes = OpenXRFeature.SetCalls;
            OpenXRFeature.ActualMode = XrEnvironmentBlendMode.Additive;
            FrameNativePassthrough.Exit();
            Check(OpenXRFeature.SetCalls == writes && !FrameNativePassthrough.IsActive, "even a missing notification cannot overwrite an external actual mode on exit");
            feature.OnSessionLossPending(7);
            Check(!FrameNativePassthrough.IsAvailable, "lost session no longer authorizes control");
            feature.OnSessionCreate(8);
            feature.OnSessionBegin(8);
            Check(FrameNativePassthrough.IsAvailable && native.EnumCalls == 2, "new session safely reuses same-system capability result");
            feature.Destroy(native.Instance);
        }
        using (var native = new FakeNative { Modes = new[] { 1, 3 } })
        {
            var old = CreateSession(native);
            var current = new OpenXrEnvironmentBlendFeature();
            Check(current.Create(native.Instance), "new feature instance safely replaces old attachment");
            current.System(native.System);
            current.OnSessionCreate(9);
            current.OnSessionBegin(9);
            old.Destroy(native.Instance);
            Check(FrameNativePassthrough.IsAvailable, "stale feature destruction cannot detach the new facade owner");
            current.Destroy(native.Instance);
        }
        foreach (string pendingChange in new[] { "readback", "exit", "notification" })
        {
            using var native = new FakeNative { Modes = new[] { 1, 2, 3 } };
            var feature = CreateSession(native);
            feature.TryEnterPassthrough(1);
            OpenXRFeature.ActualMode = XrEnvironmentBlendMode.Additive;
            int writes = OpenXRFeature.SetCalls;
            if (pendingChange == "readback") Check(!feature.TryEnterPassthrough(2), "external pending mode prevents activation");
            else if (pendingChange == "notification") feature.Active(XrEnvironmentBlendMode.Additive);
            else FrameNativePassthrough.Exit();
            Check(!FrameNativePassthrough.IsActive && FrameNativePassthrough.Status == FrameNativePassthroughStatus.ActivationFailed, "pending external mode wins: " + pendingChange);
            FrameNativePassthrough.Exit();
            Check(OpenXRFeature.SetCalls == writes, "pending external mode never receives old restoration request: " + pendingChange);
            feature.Destroy(native.Instance);
        }
        using (var native = new FakeNative { Modes = new[] { 1, 3 } })
        {
            GloomhavenVR.FrameDefaults.Active = false;
            var feature = CreateSession(native);
            Check(!FrameNativePassthrough.Required && FrameNativePassthrough.IsAvailable, "PC streaming remains independent of native Frame compatibility");
            Check(FrameNativePassthrough.TryEnter(), "PC facade leaves existing chroma-key path available");
            Check(!feature.TryEnterPassthrough(1) && OpenXRFeature.GetCalls == 0 && OpenXRFeature.SetCalls == 0, "PC never reads or changes environment blend mode");
            feature.Destroy(native.Instance);
            GloomhavenVR.FrameDefaults.Active = true;
            Check(FrameNativePassthrough.Status == FrameNativePassthroughStatus.Checking, "destroyed instance detaches facade");
        }
        GloomhavenVR.FrameDefaults.Active = false;
    }

    private static OpenXrEnvironmentBlendFeature CreateSession(FakeNative native, bool begin = true)
    {
        OpenXRFeature.ProcAddress = native.Proc;
        OpenXRFeature.ActualMode = XrEnvironmentBlendMode.Opaque;
        OpenXRFeature.RequestedMode = null;
        OpenXRFeature.GetCalls = OpenXRFeature.SetCalls = 0;
        OpenXRFeature.ThrowGet = OpenXRFeature.ThrowSet = false;
        VRLog.Lines.Clear();
        var feature = new OpenXrEnvironmentBlendFeature();
        Check(feature.Create(native.Instance), "passthrough feature does not veto VR startup");
        feature.System(native.System);
        feature.OnSessionCreate(7);
        if (begin) feature.OnSessionBegin(7);
        return feature;
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
