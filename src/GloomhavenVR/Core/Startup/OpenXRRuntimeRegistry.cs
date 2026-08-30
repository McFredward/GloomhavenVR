using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GloomhavenVR.Core;

/// <summary>
/// Enumerates installed OpenXR runtimes for the init failover in
/// <see cref="OpenXRBootstrap"/> (LCVR/RepoXR pattern, TOOLCHAIN §5.4):
/// registry <c>HKLM\SOFTWARE\Khronos\OpenXR\1</c> (<c>ActiveRuntime</c> +
/// <c>AvailableRuntimes</c>) plus well-known runtime JSON paths (Meta, VDXR, SteamVR).
/// The SYSTEM DEFAULT (no XR_RUNTIME_JSON override) is always attempted FIRST —
/// attempting a runtime boots it, and booting e.g. SteamVR on a Virtual Desktop
/// machine is disruptive (see <see cref="GetCandidates"/> for the full ordering).
/// All registry access is gated behind a Windows platform check — on other platforms
/// only the system default (no override) is attempted.
/// This file intentionally references no XR types (safe to JIT before RuntimeDeps load).
/// </summary>
internal static class OpenXRRuntimeRegistry
{
    private const string KhronosKey = @"SOFTWARE\Khronos\OpenXR\1";

    internal readonly struct RuntimeEntry
    {
        internal RuntimeEntry(string name, string? jsonPath, bool isActiveDefault)
        {
            Name = name;
            JsonPath = jsonPath;
            IsActiveDefault = isActiveDefault;
        }

        /// <summary>Human-readable name (from the runtime JSON when available).</summary>
        internal string Name { get; }

        /// <summary>Path for XR_RUNTIME_JSON; null = "system default, no override".</summary>
        internal string? JsonPath { get; }

        /// <summary>True when this is the registry ActiveRuntime.</summary>
        internal bool IsActiveDefault { get; }

        public override string ToString() => $"{Name} ({JsonPath ?? "system default"})";
    }

    private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <summary>
    /// Build the ordered candidate list for init attempts. Ordering rationale (first
    /// hardware test failure, 2026-07): merely *attempting* a runtime boots it —
    /// trying SteamVR's JSON first launched the whole SteamVR compositor on a
    /// Virtual-Desktop-only machine. So the machine's own default always goes first
    /// and SteamVR is never booted before it:
    ///
    /// 1. explicit config override ([General] RuntimeOverride — user intent, always first),
    /// 2. SYSTEM DEFAULT: no XR_RUNTIME_JSON at all — the OpenXR loader resolves
    ///    HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime itself (we log what it points to),
    /// 3. VDXR when the Virtual Desktop Streamer process is running (the streamer being
    ///    up is a strong signal the user is playing through Virtual Desktop right now),
    /// 4. remaining runtimes (registry AvailableRuntimes + well-known JSONs), with
    ///    SteamVR ordered last — attempting it side-launches the SteamVR compositor.
    ///
    /// [Core] RuntimePriority (default "auto") replaces 2–4 with an explicit order.
    /// Duplicates (by path) are removed, first occurrence wins. LCVR reference:
    /// OpenXR.Loader.InitializeXR() also tries the default runtime first, then the rest.
    /// </summary>
    internal static List<RuntimeEntry> GetCandidates(string? configOverridePath, string? priority = null)
    {
        var candidates = new List<RuntimeEntry>();

        string? activePath = null;
        if (IsWindows)
        {
            try
            {
                activePath = GetActiveRuntimePath();
            }
            catch (Exception e)
            {
                VRLog.Warn("Core", $"Could not read the OpenXR ActiveRuntime from the registry: {e.Message}");
            }
        }

        // Always tell testers what the machine's default is — the "system default"
        // attempt resolves to exactly this runtime.
        VRLog.Info("Core", activePath != null
            ? $"Registry ActiveRuntime: {ReadRuntimeName(activePath) ?? "unnamed"} ({activePath})"
            : "Registry ActiveRuntime: not set (no HKLM Khronos/OpenXR key?)");

        if (!string.IsNullOrWhiteSpace(configOverridePath))
        {
            if (File.Exists(configOverridePath))
                candidates.Add(new RuntimeEntry(
                    ReadRuntimeName(configOverridePath!) ?? "config RuntimeOverride",
                    configOverridePath, isActiveDefault: false));
            else
                VRLog.Warn("Core", $"RuntimeOverride path does not exist, ignoring: {configOverridePath}");
        }

        if (!string.IsNullOrWhiteSpace(priority) &&
            !string.Equals(priority!.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            AddPriorityCandidates(candidates, priority!, activePath);
        }
        else
        {
            AddAutoCandidates(candidates, activePath);
        }

        // Final safety net: the no-override attempt is always in the list.
        if (!candidates.Any(c => c.JsonPath == null))
            candidates.Add(SystemDefaultEntry(activePath));

        return candidates;
    }

    /// <summary>
    /// [Core] SkipRuntimeCandidates path: a single "no XR_RUNTIME_JSON" candidate.
    /// Still reads + logs the registry ActiveRuntime so testers see what it resolves to.
    /// </summary>
    internal static RuntimeEntry SystemDefaultOnly()
    {
        string? activePath = null;
        if (IsWindows)
        {
            try { activePath = GetActiveRuntimePath(); }
            catch (Exception e) { VRLog.Warn("Core", $"Could not read the OpenXR ActiveRuntime from the registry: {e.Message}"); }
        }
        VRLog.Info("Core", activePath != null
            ? $"Registry ActiveRuntime: {ReadRuntimeName(activePath) ?? "unnamed"} ({activePath})"
            : "Registry ActiveRuntime: not set (no HKLM Khronos/OpenXR key?)");
        return SystemDefaultEntry(activePath);
    }

    /// <summary>The "no XR_RUNTIME_JSON" candidate, labeled with what it will resolve to.</summary>
    internal static RuntimeEntry SystemDefaultEntry(string? activePath)
    {
        string name = activePath != null
            ? $"system default → {ReadRuntimeName(activePath) ?? Path.GetFileName(activePath)}"
            : "system default";
        return new RuntimeEntry(name, null, isActiveDefault: true);
    }

    private static void AddAutoCandidates(List<RuntimeEntry> candidates, string? activePath)
    {
        // 2. System default first — never boot another runtime before the machine's own.
        candidates.Add(SystemDefaultEntry(activePath));

        if (!IsWindows)
            return;

        // 3. Virtual Desktop boost: if the streamer is running, VDXR is almost certainly
        //    the runtime the user wants — try it before any other fallback even when the
        //    registry default points elsewhere (e.g. a stale SteamVR ActiveRuntime).
        string vdxr = VirtualDesktopJson();
        if (File.Exists(vdxr) && !PathsEqual(vdxr, activePath) && IsVirtualDesktopStreamerRunning())
        {
            VRLog.Info("Core", "Virtual Desktop Streamer process detected — preferring VDXR over other fallbacks.");
            AddCandidate(candidates, vdxr, isActiveDefault: false);
        }

        // 4. Everything else that's installed. SteamVR sorts last: merely attempting it
        //    launches the SteamVR compositor, which is disruptive on non-SteamVR setups.
        var fallbacks = new List<string>();
        try
        {
            CollectRegistryAvailableRuntimes(fallbacks);
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"OpenXR registry enumeration failed ({e.Message}) — continuing with well-known paths.");
        }
        fallbacks.AddRange(WellKnownRuntimeJsons());

        foreach (string path in fallbacks
                     .Where(p => !PathsEqual(p, activePath)) // covered by the system-default attempt
                     .OrderBy(IsSteamVRJson))                // false (0) before true (1) → SteamVR last
            AddCandidate(candidates, path, isActiveDefault: false);
    }

    /// <summary>
    /// [Core] RuntimePriority parser: comma-separated tokens, each a keyword
    /// (default/vdxr/steamvr/oculus) or a full path to a runtime JSON.
    /// </summary>
    private static void AddPriorityCandidates(List<RuntimeEntry> candidates, string priority, string? activePath)
    {
        foreach (string raw in priority.Split(','))
        {
            string token = raw.Trim();
            if (token.Length == 0)
                continue;

            switch (token.ToLowerInvariant())
            {
                case "auto":
                    AddAutoCandidates(candidates, activePath);
                    break;
                case "default":
                case "system":
                case "active":
                    if (!candidates.Any(c => c.JsonPath == null))
                        candidates.Add(SystemDefaultEntry(activePath));
                    break;
                case "vdxr":
                case "virtualdesktop":
                    AddCandidate(candidates, VirtualDesktopJson(), isActiveDefault: false);
                    break;
                case "steamvr":
                case "steam":
                    AddCandidate(candidates, SteamVRJson(), isActiveDefault: false);
                    break;
                case "oculus":
                case "meta":
                case "link":
                    AddCandidate(candidates, OculusJson(), isActiveDefault: false);
                    break;
                default:
                    if (File.Exists(token))
                        AddCandidate(candidates, token, isActiveDefault: false);
                    else
                        VRLog.Warn("Core", $"RuntimePriority token not understood / file not found, ignoring: '{token}' " +
                                           "(expected auto|default|vdxr|steamvr|oculus or a path to a runtime JSON).");
                    break;
            }
        }
    }

    /// <summary>Registry ActiveRuntime path (the runtime the OpenXR loader uses by default), or null.</summary>
    private static string? GetActiveRuntimePath()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KhronosKey);
        if (key == null)
        {
            // ALERT: without a registered runtime there is no VR at all, and the answer is
            // on the player's machine (install/repair Virtual Desktop, SteamVR, the Oculus
            // runtime). This is the one line that names the cause.
            VRLog.Alert("Core", @"No HKLM\SOFTWARE\Khronos\OpenXR\1 key — is any OpenXR runtime installed?");
            return null;
        }

        return key.GetValue("ActiveRuntime") is string active && !string.IsNullOrEmpty(active) ? active : null;
    }

    /// <summary>AvailableRuntimes: value names are JSON paths; DWORD data 0 = enabled per spec.</summary>
    private static void CollectRegistryAvailableRuntimes(List<string> paths)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KhronosKey);
        using RegistryKey? available = key?.OpenSubKey("AvailableRuntimes");
        if (available == null)
            return;

        foreach (string valueName in available.GetValueNames())
        {
            object? data = available.GetValue(valueName);
            bool enabled = data is not int i || i == 0;
            if (enabled)
                paths.Add(valueName);
        }
    }

    /// <summary>Well-known runtime JSON locations (TOOLCHAIN §5.4 / §6.1).</summary>
    private static IEnumerable<string> WellKnownRuntimeJsons()
    {
        // Meta (Quest Link / Air Link)
        yield return OculusJson();
        // Virtual Desktop (VDXR)
        yield return VirtualDesktopJson();
        // SteamVR (also used by Steam Link) — kept last by AddAutoCandidates anyway.
        yield return SteamVRJson();
    }

    private static string OculusJson() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        @"Oculus\Support\oculus-runtime\oculus_openxr_64.json");

    private static string VirtualDesktopJson() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        @"Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json");

    private static string SteamVRJson() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        @"Steam\steamapps\common\SteamVR\steamxr_win64.json");

    private static bool IsSteamVRJson(string path) =>
        Path.GetFileName(path).StartsWith("steamxr", StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string? a, string? b) =>
        a != null && b != null && string.Equals(
            Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Is the Virtual Desktop streamer app running on this machine right now?</summary>
    private static bool IsVirtualDesktopStreamerRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("VirtualDesktop.Streamer").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void AddCandidate(List<RuntimeEntry> candidates, string jsonPath, bool isActiveDefault)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            return;
        if (candidates.Any(c => string.Equals(c.JsonPath, jsonPath, StringComparison.OrdinalIgnoreCase)))
            return;

        candidates.Add(new RuntimeEntry(ReadRuntimeName(jsonPath) ?? Path.GetFileName(jsonPath), jsonPath, isActiveDefault));
    }

    /// <summary>
    /// Extract runtime.name from an OpenXR runtime JSON. Deliberately a tiny regex
    /// instead of a JSON library — the value is informational only.
    /// </summary>
    private static string? ReadRuntimeName(string jsonPath)
    {
        try
        {
            string json = File.ReadAllText(jsonPath);
            Match m = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
