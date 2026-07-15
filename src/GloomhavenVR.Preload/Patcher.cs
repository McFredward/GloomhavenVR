using System.Collections.Generic;
using BepInEx.Logging;
using Mono.Cecil;

namespace GloomhavenVR.Preload;

/// <summary>
/// BepInEx 5 preloader patcher entry point (lives in <c>BepInEx/patchers</c>).
///
/// We do not rewrite any game assembly — this patcher exists purely because it runs
/// <b>before Unity engine init</b>, which is the only moment at which OpenXR native
/// plugins and the <c>UnitySubsystems</c> manifest can be installed so the engine
/// picks them up on the same boot (no restart required). See
/// <c>.planning/ARCHITECTURE.md</c> §2 and <c>.planning/research/TOOLCHAIN.md</c> §5.2.
/// </summary>
public static class Patcher
{
    internal static readonly ManualLogSource Log = Logger.CreateLogSource("GloomhavenVR.Preload");

    /// <summary>No assemblies are patched; empty list keeps the patcher contract valid.</summary>
    public static IEnumerable<string> TargetDLLs => [];

    /// <summary>Required by the BepInEx patcher contract; intentionally a no-op.</summary>
    public static void Patch(AssemblyDefinition _)
    {
        // Intentionally empty — we never modify game assemblies (see class docs).
    }

    /// <summary>Runs before any assembly is loaded — earliest hook we get.</summary>
    public static void Initialize()
    {
        Log.LogInfo("GloomhavenVR preloader loaded (Phase 0 stub — no natives installed yet).");

        InstallNatives();
        InstallSubsystemsManifest();
    }

    /// <summary>
    /// TODO(Phase 1): copy <c>UnityOpenXR.dll</c> + <c>openxr_loader.dll</c> (harvested from a
    /// dummy Unity 2021.3.5f1 build, shipped next to this patcher) into
    /// <c>Gloomhaven_Data/Plugins/x86_64/</c>. Skip if already present and hashes match.
    /// </summary>
    private static void InstallNatives()
    {
        Log.LogDebug("InstallNatives(): stub — implemented in Phase 1 (feat/xr-bootstrap).");
    }

    /// <summary>
    /// TODO(Phase 1): write <c>Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json</c>
    /// (version-matched to the shipped OpenXR plugin package, 1.10.0) so the engine registers the
    /// "OpenXR Display"/"OpenXR Input" subsystem descriptors at boot.
    /// </summary>
    private static void InstallSubsystemsManifest()
    {
        Log.LogDebug("InstallSubsystemsManifest(): stub — implemented in Phase 1 (feat/xr-bootstrap).");
    }
}
