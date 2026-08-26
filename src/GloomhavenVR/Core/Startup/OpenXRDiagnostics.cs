using System;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;

namespace GloomhavenVR.Core;

/// <summary>
/// P/Invoke diagnostics against the mod-shipped <c>UnityOpenXR.dll</c> native plugin.
/// Entry points and marshalling adapted from LCVR <c>Source/OpenXR.cs</c> (GPL-3.0,
/// (c) DaXcess — same license as this mod). Only call these after the preloader
/// installed the natives (the DllImport would otherwise throw DllNotFoundException —
/// callers catch everything).
/// </summary>
internal static class OpenXRDiagnostics
{
    [DllImport("UnityOpenXR", EntryPoint = "DiagnosticReport_GenerateReport")]
    private static extern IntPtr Internal_GenerateReport();

    [DllImport("UnityOpenXR", EntryPoint = "DiagnosticReport_ReleaseReport")]
    private static extern void Internal_ReleaseReport(IntPtr report);

    [DllImport("UnityOpenXR", EntryPoint = "NativeConfig_GetRuntimeName")]
    private static extern bool Internal_GetRuntimeName(out IntPtr runtimeNamePtr);

    [DllImport("UnityOpenXR", EntryPoint = "NativeConfig_GetRuntimeVersion")]
    private static extern bool Internal_GetRuntimeVersion(out ushort major, out ushort minor, out ushort patch);

    /// <summary>Full OpenXR diagnostics report (multi-line), or "" when unavailable.</summary>
    internal static string GenerateReport()
    {
        try
        {
            IntPtr handle = Internal_GenerateReport();
            if (handle == IntPtr.Zero)
                return "";

            string report = Marshal.PtrToStringAnsi(handle) ?? "";
            Internal_ReleaseReport(handle);
            return report;
        }
        catch (Exception e)
        {
            return $"(diagnostics report unavailable: {e.Message})";
        }
    }

    /// <summary>Path of the persistent diagnostics file testers should attach to bug reports.</summary>
    internal static string ReportFilePath => Path.Combine(Paths.BepInExRootPath, "openxr-diagnostics.log");

    /// <summary>
    /// Append the native OpenXR diagnostics report to <c>BepInEx/openxr-diagnostics.log</c>,
    /// timestamped and labeled with the runtime candidate/outcome it belongs to. The report
    /// contains the xrCreateInstance/xrGetSystem error codes the BepInEx log never sees
    /// (the native plugin only logs those to Player.log). Never throws.
    /// </summary>
    internal static void AppendReportToFile(string label)
    {
        try
        {
            string report = GenerateReport();
            if (string.IsNullOrEmpty(report))
                report = "(native diagnostics report was empty — did the OpenXR loader library load at all?)";

            File.AppendAllText(ReportFilePath,
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {label} ====={Environment.NewLine}" +
                report + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Could not write OpenXR diagnostics file ({ReportFilePath}): {e.Message}");
        }
    }

    /// <summary>Name of the OpenXR runtime the native plugin actually connected to.</summary>
    internal static bool TryGetActiveRuntimeName(out string name)
    {
        name = "";
        try
        {
            if (!Internal_GetRuntimeName(out IntPtr ptr) || ptr == IntPtr.Zero)
                return false;

            name = Marshal.PtrToStringAnsi(ptr) ?? "";
            return name.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetActiveRuntimeVersion(out ushort major, out ushort minor, out ushort patch)
    {
        try
        {
            return Internal_GetRuntimeVersion(out major, out minor, out patch);
        }
        catch
        {
            major = minor = patch = 0;
            return false;
        }
    }
}
