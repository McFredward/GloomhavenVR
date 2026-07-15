using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace GloomhavenVR.Preload;

/// <summary>
/// BepInEx 5 preloader patcher entry point (lives in <c>BepInEx/patchers/GloomhavenVR/</c>).
///
/// We do not rewrite any game assembly — this patcher exists purely because it runs
/// <b>before Unity engine init</b>, which is the only moment at which OpenXR native
/// plugins and the <c>UnitySubsystems</c> manifest can be installed so the engine
/// picks them up on the same boot (no restart required). See
/// <c>.planning/ARCHITECTURE.md</c> §2 and <c>.planning/research/TOOLCHAIN.md</c> §5.2
/// (RepoXR/LCVR-1.3.2 pattern).
///
/// Shipping layout (see README "Install"):
/// <code>
/// BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll   (this assembly)
/// BepInEx/patchers/GloomhavenVR/Natives/UnityOpenXR.dll
/// BepInEx/patchers/GloomhavenVR/Natives/openxr_loader.dll
/// </code>
/// Installed at boot (idempotent, hash-compared):
/// <code>
/// Gloomhaven_Data/Plugins/x86_64/UnityOpenXR.dll
/// Gloomhaven_Data/Plugins/x86_64/openxr_loader.dll
/// Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json
/// </code>
///
/// HARD RULE: <see cref="Initialize"/> must never throw. Any failure here must degrade
/// to "VR unavailable" (the plugin's pre-flight check reports it) — never break the
/// vanilla game boot.
/// </summary>
public static class Patcher
{
    internal static readonly ManualLogSource Log = Logger.CreateLogSource("GloomhavenVR.Preload");

    /// <summary>
    /// OpenXR package version of the shipped set. Must match libs/Natives (fetch pin)
    /// and libs/RuntimeDeps (managed Unity.XR.OpenXR.dll) — TOOLCHAIN.md risk R5.
    /// </summary>
    private const string OpenXRPackageVersion = "1.10.0";

    /// <summary>Exact manifest content from TOOLCHAIN.md §5.2 (RepoXR ships the same).</summary>
    private const string SubsystemsManifestJson = @"{
  ""name"": ""OpenXR XR Plugin"",
  ""version"": """ + OpenXRPackageVersion + @""",
  ""libraryName"": ""UnityOpenXR"",
  ""displays"": [
    {
      ""id"": ""OpenXR Display""
    }
  ],
  ""inputs"": [
    {
      ""id"": ""OpenXR Input""
    }
  ]
}";

    private static readonly string[] NativeFiles = { "UnityOpenXR.dll", "openxr_loader.dll" };

    /// <summary>No assemblies are patched; empty list keeps the patcher contract valid.</summary>
    public static IEnumerable<string> TargetDLLs => [];

    /// <summary>Required by the BepInEx patcher contract; intentionally a no-op.</summary>
    public static void Patch(AssemblyDefinition _)
    {
        // Intentionally empty — we never modify game assemblies (see class docs).
    }

    /// <summary>Runs before any game assembly is loaded — earliest hook we get.</summary>
    public static void Initialize()
    {
        try
        {
            if (!IsVREnabledInConfig())
            {
                Log.LogInfo("[General] Enabled = false in dev.gloomhavenvr.cfg — skipping OpenXR runtime asset install (game stays vanilla).");
                return;
            }

            bool nativesOk = InstallNatives();
            bool manifestOk = InstallSubsystemsManifest();

            if (nativesOk && manifestOk)
            {
                WriteInstallMarker();
                Log.LogInfo($"OpenXR runtime assets ready (package {OpenXRPackageVersion}).");
            }
            else
            {
                Log.LogError("OpenXR runtime asset install incomplete — VR will be unavailable this session (game itself is unaffected).");
            }
        }
        catch (Exception e)
        {
            // Never propagate: a broken preloader must not take the game down.
            Log.LogError($"Unexpected preloader failure — VR will be unavailable this session. {e}");
        }
    }

    /// <summary>
    /// <c>Gloomhaven_Data/</c> — derived from BepInEx's ManagedPath (…/Gloomhaven_Data/Managed)
    /// rather than hardcoding the product name.
    /// </summary>
    private static string DataPath => Path.GetDirectoryName(Paths.ManagedPath)!;

    /// <summary>Directory this patcher assembly was loaded from (BepInEx/patchers/GloomhavenVR/).</summary>
    private static string PatcherDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    /// <summary>
    /// Copy <c>UnityOpenXR.dll</c> + <c>openxr_loader.dll</c> (shipped next to this patcher
    /// under <c>Natives/</c>) into <c>Gloomhaven_Data/Plugins/x86_64/</c>.
    /// Idempotent: SHA256-compared, skipped when identical, overwritten when different.
    /// </summary>
    private static bool InstallNatives()
    {
        string sourceDir = Path.Combine(PatcherDir, "Natives");
        string destDir = Path.Combine(DataPath, "Plugins", "x86_64");

        bool allOk = true;
        foreach (string name in NativeFiles)
        {
            try
            {
                string source = Path.Combine(sourceDir, name);
                string dest = Path.Combine(destDir, name);

                if (!File.Exists(source))
                {
                    Log.LogError($"Missing shipped native '{source}' — the mod package is incomplete " +
                                 "(re-install; developers: run scripts/fetch-natives.sh and scripts/deploy.ps1).");
                    allOk = false;
                    continue;
                }

                if (File.Exists(dest) && FileHashesEqual(source, dest))
                {
                    Log.LogDebug($"Native up to date: {dest}");
                    continue;
                }

                Directory.CreateDirectory(destDir);
                File.Copy(source, dest, overwrite: true);
                Log.LogInfo($"Installed native: {dest}");
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to install native '{name}': {e}");
                allOk = false;
            }
        }

        return allOk;
    }

    /// <summary>
    /// Write <c>Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json</c>
    /// (version-matched to the shipped OpenXR package) so the engine registers the
    /// "OpenXR Display"/"OpenXR Input" subsystem descriptors at boot.
    /// Idempotent: content-compared, only rewritten when different.
    /// </summary>
    private static bool InstallSubsystemsManifest()
    {
        try
        {
            string dir = Path.Combine(DataPath, "UnitySubsystems", "UnityOpenXR");
            string manifest = Path.Combine(dir, "UnitySubsystemsManifest.json");

            if (File.Exists(manifest) && File.ReadAllText(manifest) == SubsystemsManifestJson)
            {
                Log.LogDebug($"Subsystems manifest up to date: {manifest}");
                return true;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(manifest, SubsystemsManifestJson);
            Log.LogInfo($"Installed subsystems manifest: {manifest}");
            return true;
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to install UnitySubsystemsManifest.json: {e}");
            return false;
        }
    }

    /// <summary>
    /// Record what was installed (version + file hashes + when) next to this patcher —
    /// diagnostic breadcrumb for the failure-triage table in docs/TESTING-P1.md.
    /// </summary>
    private static void WriteInstallMarker()
    {
        try
        {
            string destDir = Path.Combine(DataPath, "Plugins", "x86_64");
            var lines = new List<string>
            {
                "{",
                $"  \"openxrPackageVersion\": \"{OpenXRPackageVersion}\",",
                $"  \"installedUtc\": \"{DateTime.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'}\",",
                "  \"files\": {"
            };
            for (int i = 0; i < NativeFiles.Length; i++)
            {
                string dest = Path.Combine(destDir, NativeFiles[i]);
                string comma = i < NativeFiles.Length - 1 ? "," : "";
                lines.Add($"    \"{NativeFiles[i]}\": \"{(File.Exists(dest) ? Sha256Of(dest) : "missing")}\"{comma}");
            }
            lines.AddRange(["  }", "}"]);

            File.WriteAllLines(Path.Combine(PatcherDir, "install-state.json"), lines);
        }
        catch (Exception e)
        {
            // Marker is purely diagnostic — never fail the install over it.
            Log.LogWarning($"Could not write install-state.json: {e.Message}");
        }
    }

    /// <summary>
    /// Read the plugin's own config file to honor the master switch. The BepInEx config
    /// API isn't reliably usable in patcher context (config binding belongs to the plugin),
    /// so this is a minimal INI scan of <c>BepInEx/config/dev.gloomhavenvr.cfg</c>.
    /// Missing file / unparsable content ⇒ enabled (first run creates the file later).
    /// </summary>
    private static bool IsVREnabledInConfig()
    {
        try
        {
            string cfg = Path.Combine(Paths.ConfigPath, "dev.gloomhavenvr.cfg");
            if (!File.Exists(cfg))
                return true;

            string? section = null;
            foreach (string raw in File.ReadAllLines(cfg))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0 || !string.Equals(section, "General", StringComparison.OrdinalIgnoreCase))
                    continue;

                string key = line.Substring(0, eq).Trim();
                if (!string.Equals(key, "Enabled", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line.Substring(eq + 1).Trim();
                return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
        catch (Exception e)
        {
            Log.LogWarning($"Could not read dev.gloomhavenvr.cfg ({e.Message}) — assuming VR enabled.");
            return true;
        }
    }

    private static bool FileHashesEqual(string a, string b) => Sha256Of(a) == Sha256Of(b);

    private static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
