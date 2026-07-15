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
/// <c>AvailableRuntimes</c>) plus well-known runtime JSON paths (Meta, SteamVR,
/// Virtual Desktop). All registry access is gated behind a Windows platform check —
/// on other platforms only the system default (no override) is attempted.
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
    /// Build the ordered candidate list for init attempts:
    /// 1. explicit config override (always first when set),
    /// 2. registry ActiveRuntime,
    /// 3. registry AvailableRuntimes (enabled ones),
    /// 4. well-known JSON paths (Meta, SteamVR, Virtual Desktop),
    /// 5. system default (no XR_RUNTIME_JSON override) as the final fallback.
    /// Duplicates (by path) are removed, first occurrence wins.
    /// </summary>
    internal static List<RuntimeEntry> GetCandidates(string? configOverridePath)
    {
        var candidates = new List<RuntimeEntry>();

        if (!string.IsNullOrWhiteSpace(configOverridePath))
        {
            if (File.Exists(configOverridePath))
                candidates.Add(new RuntimeEntry(
                    ReadRuntimeName(configOverridePath!) ?? "config RuntimeOverride",
                    configOverridePath, isActiveDefault: false));
            else
                VRLog.Warn("Core", $"RuntimeOverride path does not exist, ignoring: {configOverridePath}");
        }

        if (IsWindows)
        {
            try
            {
                AddRegistryRuntimes(candidates);
            }
            catch (Exception e)
            {
                VRLog.Warn("Core", $"OpenXR registry enumeration failed ({e.Message}) — continuing with well-known paths.");
            }

            foreach (string path in WellKnownRuntimeJsons())
                AddCandidate(candidates, path, isActiveDefault: false);
        }

        // Final fallback: let the loader pick the system default without an override.
        candidates.Add(new RuntimeEntry("system default", null, isActiveDefault: false));

        return candidates;
    }

    private static void AddRegistryRuntimes(List<RuntimeEntry> candidates)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KhronosKey);
        if (key == null)
        {
            VRLog.Warn("Core", @"No HKLM\SOFTWARE\Khronos\OpenXR\1 key — is any OpenXR runtime installed?");
            return;
        }

        if (key.GetValue("ActiveRuntime") is string active && !string.IsNullOrEmpty(active))
            AddCandidate(candidates, active, isActiveDefault: true);

        // AvailableRuntimes: values are JSON paths; DWORD data 0 = enabled per spec.
        using RegistryKey? available = key.OpenSubKey("AvailableRuntimes");
        if (available == null)
            return;

        foreach (string valueName in available.GetValueNames())
        {
            object? data = available.GetValue(valueName);
            bool enabled = data is not int i || i == 0;
            if (enabled)
                AddCandidate(candidates, valueName, isActiveDefault: false);
        }
    }

    /// <summary>Well-known runtime JSON locations (TOOLCHAIN §5.4 / §6.1).</summary>
    private static IEnumerable<string> WellKnownRuntimeJsons()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // Meta (Quest Link / Air Link)
        yield return Path.Combine(programFiles, @"Oculus\Support\oculus-runtime\oculus_openxr_64.json");
        // SteamVR (also used by Steam Link)
        yield return Path.Combine(programFilesX86, @"Steam\steamapps\common\SteamVR\steamxr_win64.json");
        // Virtual Desktop (VDXR)
        yield return Path.Combine(programFiles, @"Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json");
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
