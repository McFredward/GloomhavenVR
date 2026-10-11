using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GloomhavenVR.Core;

/// <summary>
/// Read-only OpenXR capability query for the loader's existing instance and system.
/// A successful opaque-only query is different from a failed or unavailable query:
/// the latter says nothing about passthrough support. This does not enable passthrough.
/// </summary>
internal sealed class OpenXrEnvironmentBlendProbe
{
    internal const int PrimaryStereo = 2;
    internal const int MaximumModes = 16;
    internal const int MaximumSystems = 16;

    // XRAPI_CALL is __stdcall on Windows. Winapi also gives the platform convention
    // in the fake-native harness; the shipped game and Proton use the 64-bit Windows ABI.
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    internal delegate int GetInstanceProcAddr(ulong instance,
        [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate int EnumerateEnvironmentBlendModes(ulong instance, ulong system,
        int viewConfiguration, uint capacity, out uint count, IntPtr modes);

    private ulong _instance;
    private IntPtr _getProcAddress;
    private readonly HashSet<ulong> _queriedSystems = new();
    private bool _systemLimitReported;

    internal void BeginInstance(ulong instance, IntPtr getProcAddress)
    {
        if (_instance == instance && _getProcAddress == getProcAddress)
            return;
        Clear();
        _instance = instance;
        _getProcAddress = getProcAddress;
    }

    internal void EndInstance(ulong instance)
    {
        if (_instance == instance)
            Clear();
    }

    internal void Clear()
    {
        _instance = 0;
        _getProcAddress = IntPtr.Zero;
        _queriedSystems.Clear();
        _systemLimitReported = false;
    }

    /// <summary>Null means no live system, or this exact instance/system was already queried.</summary>
    internal QueryResult? ObserveSystem(ulong system)
    {
        if (_instance == 0 || system == 0 || _queriedSystems.Contains(system))
            return null;
        // xrGetSystem normally supplies one ID. A broken native callback stream
        // must not turn a startup diagnostic into unbounded storage or logging.
        if (_queriedSystems.Count >= MaximumSystems)
        {
            if (_systemLimitReported)
                return null;
            _systemLimitReported = true;
            return QueryResult.Unavailable("instance system diagnostic limit reached");
        }
        _queriedSystems.Add(system);
        if (IntPtr.Size != 8)
            return QueryResult.Unavailable("unsupported process pointer width");
        if (_getProcAddress == IntPtr.Zero)
            return QueryResult.Unavailable("xrGetInstanceProcAddr unavailable");

        GetInstanceProcAddr? getProcAddress = null;
        EnumerateEnvironmentBlendModes? enumerate = null;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            getProcAddress = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddr>(_getProcAddress);
            int resolveResult = getProcAddress(_instance, "xrEnumerateEnvironmentBlendModes", out IntPtr function);
            if (resolveResult != 0)
                return QueryResult.Unavailable("function lookup failed", resolveResult);
            if (function == IntPtr.Zero)
                return QueryResult.Unavailable("enumeration function unavailable");

            enumerate = Marshal.GetDelegateForFunctionPointer<EnumerateEnvironmentBlendModes>(function);
            int countResult = enumerate(_instance, system, PrimaryStereo, 0, out uint count, IntPtr.Zero);
            if (countResult != 0)
                return QueryResult.Unavailable("mode count failed", countResult);
            if (count == 0 || count > MaximumModes)
                return QueryResult.Unavailable("invalid mode count " + count);

            buffer = Marshal.AllocHGlobal(checked((int)count * sizeof(int)));
            for (int i = 0; i < count; i++)
                Marshal.WriteInt32(buffer, i * sizeof(int), 0);
            int modesResult = enumerate(_instance, system, PrimaryStereo, count, out uint written, buffer);
            if (modesResult != 0)
                return QueryResult.Unavailable("mode enumeration failed", modesResult);
            if (written == 0 || written > count)
                return QueryResult.Unavailable("invalid returned mode count " + written);

            var modes = new int[written];
            for (int i = 0; i < modes.Length; i++)
            {
                modes[i] = Marshal.ReadInt32(buffer, i * sizeof(int));
                if (modes[i] <= 0)
                    return QueryResult.Unavailable("invalid environment blend mode " + modes[i]);
            }
            return QueryResult.Complete(modes);
        }
        catch (Exception e)
        {
            return QueryResult.Unavailable("query exception " + e.GetType().Name);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
            // Keep native delegate adapters rooted until both calls and buffer reads finish.
            GC.KeepAlive(enumerate);
            GC.KeepAlive(getProcAddress);
        }
    }

    internal static string DescribeMode(int mode) => mode switch
    {
        1 => "Opaque",
        2 => "Additive",
        3 => "AlphaBlend",
        _ => "Unknown(" + mode + ")"
    };

    internal sealed class QueryResult
    {
        private QueryResult(int[] modes, string? reason, int? nativeResult)
        { Modes = modes; Reason = reason; NativeResult = nativeResult; }

        internal int[] Modes { get; }
        internal string? Reason { get; }
        internal int? NativeResult { get; }
        internal bool IsComplete => Reason == null;

        internal static QueryResult Complete(int[] modes) => new(modes, null, null);
        internal static QueryResult Unavailable(string reason, int? nativeResult = null) =>
            new(Array.Empty<int>(), reason, nativeResult);
    }
}
