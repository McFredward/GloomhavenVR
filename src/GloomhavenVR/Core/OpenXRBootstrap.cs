using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace GloomhavenVR.Core;

/// <summary>
/// Runtime OpenXR bootstrap — the DaXcess pattern (LCVR/RepoXR <c>Source/OpenXR.cs</c>,
/// GPL-3.0) per ARCHITECTURE §2 / TOOLCHAIN §5.4, using publicized internals of the
/// mod-shipped Unity.XR.Management/Unity.XR.OpenXR assemblies (no reflection):
///
/// 1. Pre-flight: the engine must already know the "OpenXR Display"/"OpenXR Input"
///    subsystem descriptors (registered at boot from UnitySubsystemsManifest.json,
///    which the preloader installed) — otherwise abort with an actionable error.
/// 2. Create XRGeneralSettings/XRManagerSettings/OpenXRLoader ScriptableObjects,
///    enable interaction profiles BEFORE session start, MultiPass, no depth submission.
/// 3. InitXRSDK() + Start() (publicized privates), trying runtime candidates via
///    XR_RUNTIME_JSON until an XRDisplaySubsystem is running.
///
/// IMPORTANT: this type references mod-shipped XR assemblies — it must only be
/// JIT-compiled after <see cref="RuntimeDepsLoader.LoadAll"/> succeeded
/// (see <see cref="CoreModule.Init"/>).
/// </summary>
internal static class OpenXRBootstrap
{
    private static XRGeneralSettings? _generalSettings;
    private static XRManagerSettings? _managerSettings;
    private static OpenXRLoader? _loader;

    /// <summary>Kept alive so hot-reload teardown can disable them.</summary>
    private static OpenXRFeature[] _features = [];

    /// <summary>Init OpenXR + start subsystems. Returns true when an HMD is rendering.</summary>
    internal static bool Start(string? runtimeOverridePath)
    {
        if (!PreFlightCheck())
            return false;

        if (DisplayRunning())
        {
            // e.g. ScriptEngine hot reload without a clean shutdown — reuse the session.
            VRLog.Warn("Core", "An XRDisplaySubsystem is already running — reusing the existing XR session.");
            VRSession.IsRunning = true;
            return true;
        }

        CreateSettings();

        List<OpenXRRuntimeRegistry.RuntimeEntry> candidates =
            OpenXRRuntimeRegistry.GetCandidates(runtimeOverridePath);
        VRLog.Info("Core", $"OpenXR runtime candidates ({candidates.Count}): {string.Join(" | ", candidates)}");

        foreach (OpenXRRuntimeRegistry.RuntimeEntry candidate in candidates)
        {
            if (TryStartWith(candidate))
            {
                VRSession.IsRunning = true;
                LogDiagnostics(candidate);
                return true;
            }
        }

        VRLog.Error("Core", "All OpenXR runtime candidates failed — VR unavailable. " +
                            "Is the headset connected and its runtime (Meta/SteamVR/VDXR) running? " +
                            "See docs/TESTING-P1.md for triage.");
        return false;
    }

    /// <summary>
    /// The engine registers subsystem descriptors from Gloomhaven_Data/UnitySubsystems/*
    /// at boot. If "OpenXR Display"/"OpenXR Input" are missing, the preloader install
    /// didn't take effect (wrong install, or natives/manifest missing) — XR init would
    /// silently do nothing, so abort with a precise message instead.
    /// </summary>
    private static bool PreFlightCheck()
    {
        var descriptors = new List<ISubsystemDescriptor>();
        SubsystemManager.GetAllSubsystemDescriptors(descriptors);

        bool haveDisplay = descriptors.Any(d => d.id == "OpenXR Display");
        bool haveInput = descriptors.Any(d => d.id == "OpenXR Input");
        if (haveDisplay && haveInput)
        {
            VRLog.Info("Core", "Pre-flight OK: OpenXR Display/Input subsystem descriptors are registered.");
            return true;
        }

        VRLog.Error("Core",
            "Pre-flight FAILED: OpenXR subsystem descriptors not registered " +
            $"(display: {haveDisplay}, input: {haveInput}). The engine did not pick up " +
            "UnityOpenXR.dll / UnitySubsystemsManifest.json at boot. Check that the preloader ran " +
            "(look for 'GloomhavenVR.Preload' lines earlier in this log) and that " +
            "Gloomhaven_Data/Plugins/x86_64/UnityOpenXR.dll and " +
            "Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json exist. VR unavailable.");
        if (descriptors.Count > 0)
            VRLog.Debug("Core", "Registered subsystem descriptors: " + string.Join(", ", descriptors.Select(d => d.id)));
        return false;
    }

    private static void CreateSettings()
    {
        if (_generalSettings != null)
            return;

        // XRGeneralSettings.Awake() (runs inside CreateInstance) assigns the static
        // Instance and marks the object DontDestroyOnLoad — required by InitXRSDK().
        _generalSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
        _managerSettings = ScriptableObject.CreateInstance<XRManagerSettings>();
        _loader = ScriptableObject.CreateInstance<OpenXRLoader>();

        _generalSettings.Manager = _managerSettings;
        // Publicized private backing list of activeLoaders (the public `loaders`
        // property is obsolete in XR Management 4.5); registeredLoaders kept in sync.
        _managerSettings.m_Loaders.Clear();
        _managerSettings.m_Loaders.Add(_loader);
        _managerSettings.registeredLoaders.Clear();
        _managerSettings.registeredLoaders.Add(_loader);

        // Interaction profiles MUST be enabled before the session starts or there is no
        // controller input (TOOLCHAIN §5.4). Quest 3 aliases Touch Plus to the generic
        // Touch profile; Index + KHR Simple cover SteamVR sticks and everything else.
        var oculusTouch = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
        var valveIndex = ScriptableObject.CreateInstance<ValveIndexControllerProfile>();
        var khrSimple = ScriptableObject.CreateInstance<KHRSimpleControllerProfile>();
        oculusTouch.enabled = true;
        valveIndex.enabled = true;
        khrSimple.enabled = true;
        _features = [oculusTouch, valveIndex, khrSimple];
        OpenXRSettings.Instance.features = _features;

        // MultiPass: safe default for built-in RP + PPv2 (SPI is a later opt-in, R2).
        OpenXRSettings.Instance.renderMode = OpenXRSettings.RenderMode.MultiPass;
        OpenXRSettings.Instance.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
    }

    private static bool TryStartWith(OpenXRRuntimeRegistry.RuntimeEntry candidate)
    {
        VRLog.Info("Core", $"Attempting OpenXR init on: {candidate}");
        Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", candidate.JsonPath);

        try
        {
            _generalSettings!.InitXRSDK();   // publicized private: InitializeLoaderSync via manager
            _generalSettings.Start();        // publicized private: StartSubsystems

            if (DisplayRunning())
                return true;

            // Failed attempt: unwind loader state so the next candidate starts clean.
            StopAndDeinitQuiet();
            return false;
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"OpenXR init attempt threw on {candidate.Name}: {e.Message}");
            StopAndDeinitQuiet();
            return false;
        }
    }

    private static bool DisplayRunning()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetInstances(displays);
        return displays.Count > 0 && displays.Any(d => d.running);
    }

    private static void LogDiagnostics(OpenXRRuntimeRegistry.RuntimeEntry candidate)
    {
        string name = OpenXRDiagnostics.TryGetActiveRuntimeName(out string n) ? n : candidate.Name;
        string version = OpenXRDiagnostics.TryGetActiveRuntimeVersion(out ushort maj, out ushort min, out ushort pat)
            ? $"{maj}.{min}.{pat}"
            : "unknown";
        VRSession.RuntimeName = name;

        VRLog.Info("Core", $"OpenXR session up — runtime: {name} {version}, " +
                           $"render mode: {OpenXRSettings.Instance.renderMode}.");

        string report = OpenXRDiagnostics.GenerateReport();
        if (!string.IsNullOrEmpty(report))
        {
            VRLog.Debug("Core", "---- OpenXR diagnostics report ----");
            foreach (string line in report.Split('\n'))
                VRLog.Debug("Core", line.TrimEnd('\r'));
            VRLog.Debug("Core", "---- end of diagnostics report ----");
        }
    }

    /// <summary>
    /// Clean teardown for plugin OnDestroy (ScriptEngine hot-reload contract):
    /// stop subsystems, deinitialize the loader, destroy the ScriptableObjects.
    /// </summary>
    internal static void Stop()
    {
        VRSession.IsRunning = false;
        VRSession.RuntimeName = null;

        StopAndDeinitQuiet();

        foreach (OpenXRFeature feature in _features)
            UnityEngine.Object.Destroy(feature);
        _features = [];

        if (_loader != null) UnityEngine.Object.Destroy(_loader);
        if (_managerSettings != null) UnityEngine.Object.Destroy(_managerSettings);
        if (_generalSettings != null) UnityEngine.Object.Destroy(_generalSettings);
        _loader = null;
        _managerSettings = null;
        _generalSettings = null;
    }

    private static void StopAndDeinitQuiet()
    {
        try
        {
            XRManagerSettings? manager = _managerSettings;
            if (manager == null)
                return;

            if (manager.activeLoader != null)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"XR teardown reported: {e.Message}");
        }
    }
}
