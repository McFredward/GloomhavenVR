using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
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
///    XR_RUNTIME_JSON until an XRDisplaySubsystem exists.
///
/// SUCCESS CRITERION (post-mortem of the first hardware test): a candidate succeeded
/// when a display subsystem EXISTS (LCVR: <c>displays.Count > 0</c>) — NOT when it is
/// already <c>running</c>. OpenXRLoaderBase.StartInternal() returns true while the
/// session is still negotiating; the XrReady native event that actually starts the
/// display subsystem arrives via the Application.onBeforeRender message pump a few
/// rendered frames later. Requiring <c>running == true</c> synchronously made us tear
/// down healthy sessions (booting SteamVR, then declaring failure). A watchdog
/// coroutine now reports when rendering actually starts (or errors if it never does).
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

    /// <summary>How many frames the watchdog waits for the display subsystem to start rendering.</summary>
    private const int DisplayRunningWatchdogFrames = 900; // ~10-15 s

    /// <summary>Init OpenXR + start subsystems. Returns true when an XR display subsystem exists.</summary>
    internal static bool Start(string? runtimeOverridePath, string? runtimePriority, bool skipRuntimeCandidates)
    {
        LogEnvironment();

        if (!PreFlightCheck())
            return false;

        if (DisplayExists())
        {
            // e.g. ScriptEngine hot reload without a clean shutdown — reuse the session.
            VRLog.Warn("Core", "An XRDisplaySubsystem already exists — reusing the existing XR session.");
            VRSession.IsRunning = true;
            return true;
        }

        CreateSettings();

        List<OpenXRRuntimeRegistry.RuntimeEntry> candidates;
        if (skipRuntimeCandidates)
        {
            // Escape hatch ([Core] SkipRuntimeCandidates): no XR_RUNTIME_JSON fiddling at
            // all — one attempt on whatever the OS considers the active runtime.
            VRLog.Info("Core", "SkipRuntimeCandidates = true — only trying the system default runtime.");
            candidates = [OpenXRRuntimeRegistry.SystemDefaultOnly()];
        }
        else
        {
            candidates = OpenXRRuntimeRegistry.GetCandidates(runtimeOverridePath, runtimePriority);
        }

        VRLog.Info("Core", $"OpenXR runtime candidates ({candidates.Count}): {string.Join(" | ", candidates)}");

        foreach (OpenXRRuntimeRegistry.RuntimeEntry candidate in candidates)
        {
            if (TryStartWith(candidate))
            {
                VRSession.IsRunning = true;
                LogSuccess(candidate);
                return true;
            }
        }

        VRLog.Error("Core", "All OpenXR runtime candidates failed — VR unavailable. " +
                            "Is the headset connected and its runtime (Meta/SteamVR/VDXR) running? " +
                            $"Check {OpenXRDiagnostics.ReportFilePath} for per-candidate xrCreateInstance/xrGetSystem " +
                            "errors and Player.log (see docs/TESTING-P1.md §4) for '[XR]' native errors.");
        return false;
    }

    /// <summary>
    /// One-time environment fingerprint. Desktop OpenXR only supports D3D11 on this
    /// Unity/plugin combo — when the game came up on another graphics API the native
    /// session creation fails with errors that ONLY land in Player.log, so flag it
    /// loudly here where testers actually look.
    /// </summary>
    /// <summary>
    /// Report whether Unity's THREADED RENDER SUBMISSION is on — the single largest performance
    /// factor this project found, and one nothing in the engine exposes at runtime.
    ///
    /// <para>WHY IT IS REPORTED RATHER THAN MEASURED: there is no API for "are graphics jobs
    /// active". Both of the two ways it can be switched on ARE readable, though — the launch
    /// option from the command line, and the two boot.config keys the preloader writes — so this
    /// states the evidence it has instead of a verdict it cannot support. The engine decides the
    /// job mode before any managed code exists, which is also why the preloader has to write a file
    /// for the NEXT start rather than the plugin setting something now.</para>
    ///
    /// <para>The numbers in the "off" branch are from 2026-07-28 hardware, same scene, same build,
    /// changing nothing else — they are what makes this line worth reading rather than skipping.</para>
    /// </summary>
    private static void LogGraphicsJobs(string[] args)
    {
        if (args.Any(a => a.StartsWith("-force-gfx-jobs", StringComparison.OrdinalIgnoreCase)))
        {
            VRLog.Info("Core", "Graphics jobs: ON for this session (-force-gfx-jobs in the launch "
                               + "options). Unity submits draw calls on worker threads instead of the "
                               + "main thread — worth main-thread render 14.9 ms → 1.8 ms and "
                               + "45 Hz → 90 Hz on the hardware this was measured on.");
            return;
        }

        // The PRELOADER publishes what the engine actually booted with, read before it edited the
        // file. Absent = the preloader did not run at all (it lives in BepInEx/patchers/), which is
        // itself worth saying rather than papering over.
        string? session = Environment.GetEnvironmentVariable(SessionGraphicsJobsVariable);
        if (session == null)
        {
            VRLog.Warn("Core", "Graphics jobs: UNKNOWN — the mod's preloader did not run, so nothing "
                               + "here knows what the engine booted with, and nothing wrote the "
                               + "setting for you either. Check that "
                               + "BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll is installed. "
                               + "Meanwhile '-force-gfx-jobs native' in the game's launch options does "
                               + "the same job with no mod involvement.");
            return;
        }

        if (session == "1")
        {
            VRLog.Info("Core", "Graphics jobs: ON for this session (boot.config). Unity submits draw "
                               + "calls on worker threads instead of the main thread — worth "
                               + "main-thread render 14.9 ms → 1.8 ms and 45 Hz → 90 Hz on the "
                               + "hardware this was measured on.");
            return;
        }

        VRLog.Warn("Core", "Graphics jobs: OFF FOR THIS SESSION — the largest single performance "
                           + "factor found in this project. Without them Unity submits every draw "
                           + "call on ONE thread, which measured as the ENTIRE bottleneck in a "
                           + "scenario: with them on, main-thread render went 14.9 ms → 1.8 ms, the "
                           + "frame 17.5 ms → 11.14 ms, and the headset from locked-at-45 Hz to a "
                           + "clean 90 Hz (2026-07-28, same scene and build). Normally you never read "
                           + "this: the preloader writes the setting and restarts the game for you on "
                           + "the one boot that needs it, and this message is only reached when it did "
                           + "not. Its own log lines say which — [Core] EnableGraphicsJobs is off, "
                           + "[Core] AutoRestartForGraphicsJobs is off (then simply RESTART THE GAME "
                           + "ONCE yourself), or the write failed. '-force-gfx-jobs native' in the "
                           + "launch options does the same job from the very first start, with no file "
                           + "written at all.");
    }

    /// <summary>
    /// Mirror of <c>GloomhavenVR.Preload.Patcher.SessionStateVariable</c>. Duplicated as a literal
    /// rather than referenced: the plugin does not link the patcher assembly, and it must degrade to
    /// "unknown" when the patcher is absent, which a hard reference could not do.
    /// </summary>
    private const string SessionGraphicsJobsVariable = "GLOOMHAVENVR_GFXJOBS_SESSION";

    private static void LogEnvironment()
    {
        string[] args;
        try { args = Environment.GetCommandLineArgs(); }
        catch { args = []; }
        bool forceD3D11 = args.Any(a => string.Equals(a, "-force-d3d11", StringComparison.OrdinalIgnoreCase));

        GraphicsDeviceType gfx = SystemInfo.graphicsDeviceType;
        VRLog.Info("Core", $"VR init environment: Unity {Application.unityVersion}, graphics API {gfx} " +
                           $"({SystemInfo.graphicsDeviceName}), -force-d3d11 {(forceD3D11 ? "present" : "absent")} " +
                           $"in command line ({string.Join(" ", args)}).");

        LogGraphicsJobs(args);

        if (gfx != GraphicsDeviceType.Direct3D11)
        {
            VRLog.Error("Core",
                "==================================================================\n" +
                $"GRAPHICS API IS {gfx} — DESKTOP OPENXR NEEDS DIRECT3D 11.\n" +
                "XR init will almost certainly fail (native errors go to Player.log only).\n" +
                "FIX: add '-force-d3d11' to the game's Steam launch options\n" +
                "(Library → Gloomhaven → Properties → Launch Options), or pass it on the\n" +
                "command line when starting the game directly.\n" +
                "==================================================================");
        }
    }

    /// <summary>
    /// The engine registers subsystem descriptors from &lt;Game&gt;_Data/UnitySubsystems/*
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

        // Application.dataPath ends in the real data folder name (Gloomhaven ships GH_Data).
        string dataPath = Application.dataPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        VRLog.Error("Core",
            "Pre-flight FAILED: OpenXR subsystem descriptors not registered " +
            $"(display: {haveDisplay}, input: {haveInput}). The engine did not pick up " +
            "UnityOpenXR.dll / UnitySubsystemsManifest.json at boot. Check that the preloader ran " +
            "(look for 'GloomhavenVR.Preload' lines earlier in this log) and that " +
            $"{dataPath}\\Plugins\\x86_64\\UnityOpenXR.dll and " +
            $"{dataPath}\\UnitySubsystems\\UnityOpenXR\\UnitySubsystemsManifest.json exist. VR unavailable.");
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
        // m_Loaders is the private backing list of the activeLoaders property — the same
        // list LCVR mutates via `((List<XRLoader>)xrManagerSettings.activeLoaders)`
        // (LCVR OpenXR.cs InitializeScripts). registeredLoaders kept in sync so
        // TryAddLoader/TrySetLoaders semantics stay valid.
        _managerSettings.m_Loaders.Clear();
        _managerSettings.m_Loaders.Add(_loader);
        _managerSettings.registeredLoaders.Clear();
        _managerSettings.registeredLoaders.Add(_loader);

        // Interaction profiles MUST be enabled before the session starts or there is no
        // controller input (TOOLCHAIN §5.4). Quest 3 aliases Touch Plus to the generic
        // Touch profile; Index + KHR Simple cover SteamVR sticks and everything else.
        // (LCVR builds the same kind of array and assigns OpenXRSettings.Instance.features;
        // OpenXRLoaderBase.InitializeInternal re-sorts it by priority/name on init.)
        var oculusTouch = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
        var valveIndex = ScriptableObject.CreateInstance<ValveIndexControllerProfile>();
        var khrSimple = ScriptableObject.CreateInstance<KHRSimpleControllerProfile>();
        oculusTouch.enabled = true;
        valveIndex.enabled = true;
        khrSimple.enabled = true;
        _features = [oculusTouch, valveIndex, khrSimple];
        OpenXRSettings.Instance.features = _features;

        // Stereo render mode. MultiPass is the only mode that renders correctly on this game —
        // StereoModeConfig carries the evidence (no stereo shader variants in either the game's
        // shipped shaders or the mod's bundle, plus the stereo flat screen's hard dependency on
        // two passes per frame). It used to be [Stereo] RenderMode, a dial whose other value
        // shipped a black right eye at the next start; the 2026-08-22 settings audit made it a
        // constant ("Etwas was das spiel kaputt macht wenn man es umstellt ist nicht optional und
        // sollte daher nicht einstellbar sein"). The mapping still happens HERE because this is
        // the one place the XR assemblies are guaranteed resolvable (class doc), and it is still a
        // mapping rather than a literal so that flipping the one constant is the whole change.
        OpenXRSettings.Instance.renderMode =
            StereoModeConfig.Current == StereoModeConfig.Mode.SinglePassInstanced
                ? OpenXRSettings.RenderMode.SinglePassInstanced
                : OpenXRSettings.RenderMode.MultiPass;
        OpenXRSettings.Instance.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
    }

    /// <summary>
    /// One init attempt against one runtime. Mirrors LCVR OpenXR.Loader.InitializeXR
    /// (Runtime?) step by step, with a phase-by-phase log trail:
    /// env set → InitXRSDK (loader Initialize) → Start (StartSubsystems) → display census.
    /// </summary>
    private static bool TryStartWith(OpenXRRuntimeRegistry.RuntimeEntry candidate)
    {
        VRLog.Info("Core", $"Attempting OpenXR init on: {candidate}");

        // LCVR parity: null path ⇒ clear XR_RUNTIME_JSON so the OpenXR loader resolves the
        // registry ActiveRuntime itself (LCVR InitializeXR(null) does exactly this).
        Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", candidate.JsonPath);
        VRLog.Debug("Core", candidate.JsonPath != null
            ? $"  phase 1/4: XR_RUNTIME_JSON = {candidate.JsonPath}"
            : "  phase 1/4: XR_RUNTIME_JSON cleared (system default resolution)");

        try
        {
            // InitXRSDK → XRManagerSettings.InitializeLoaderSync → OpenXRLoader.Initialize:
            // loads openxr_loader, xrCreateInstance/xrGetSystem, creates the Display/Input
            // subsystems. On failure the loader deinitializes itself and activeLoader stays
            // null — no cleanup needed on our side (LCVR does none either).
            _generalSettings!.InitXRSDK();   // publicized private
            XRLoader? active = _managerSettings!.activeLoader;
            VRLog.Info("Core", $"  phase 2/4: InitXRSDK done — activeLoader: " +
                               (active != null ? active.GetType().Name : "null (loader Initialize failed)"));

            if (active == null)
            {
                // The native failure reason (xrCreateInstance/xrGetSystem error, D3D11
                // mismatch, ...) went to Player.log; persist the diagnostics report so
                // testers don't have to reproduce with debug logging on.
                OpenXRDiagnostics.AppendReportToFile($"FAILED (loader Initialize) — candidate: {candidate}");
                return false;
            }

            // Start → XRManagerSettings.StartSubsystems → OpenXRLoader.Start. NOTE: the
            // display subsystem may not be 'running' yet — StartInternal() defers until the
            // runtime reports XrReady (delivered via the onBeforeRender message pump).
            _generalSettings.Start();        // publicized private
            VRLog.Debug("Core", "  phase 3/4: Start (StartSubsystems) returned.");

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            VRLog.Info("Core", $"  phase 4/4: display subsystems: {displays.Count} " +
                               $"[{string.Join(", ", displays.Select(d => $"running={d.running}"))}]");

            // LCVR/RepoXR success criterion: a display subsystem EXISTS. Do NOT require
            // running — that flips a few frames later (see class doc). This was the P1
            // hardware-test bug: healthy sessions were torn down as "failed".
            if (displays.Count > 0)
                return true;

            // Loader initialized but no display subsystem — unwind so the next candidate
            // starts from a clean manager (StopSubsystems + Deinitialize).
            VRLog.Warn("Core", $"  Loader initialized on {candidate.Name} but no display subsystem was created — unwinding.");
            OpenXRDiagnostics.AppendReportToFile($"FAILED (no display subsystem) — candidate: {candidate}");
            StopAndDeinitQuiet();
            return false;
        }
        catch (Exception e)
        {
            // Unwrap reflection/invocation wrappers so the log shows the real failure.
            while (e is TargetInvocationException { InnerException: not null } tie)
                e = tie.InnerException!;
            VRLog.Error("Core", $"OpenXR init attempt threw on {candidate.Name}: {e}");
            for (Exception? inner = e.InnerException; inner != null; inner = inner.InnerException)
                VRLog.Error("Core", $"  inner exception: {inner.GetType().Name}: {inner.Message}");

            OpenXRDiagnostics.AppendReportToFile($"FAILED (exception: {e.GetType().Name}: {e.Message}) — candidate: {candidate}");
            StopAndDeinitQuiet();
            return false;
        }
    }

    private static bool DisplayExists()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetInstances(displays);
        return displays.Count > 0;
    }

    private static void LogSuccess(OpenXRRuntimeRegistry.RuntimeEntry candidate)
    {
        // Managed API first (OpenXRRuntime wraps the same NativeConfig_* entry points),
        // P/Invoke fallback second, candidate name as last resort.
        string name = "", version = "", pluginVersion = "";
        try
        {
            name = OpenXRRuntime.name;
            version = OpenXRRuntime.version;
            pluginVersion = OpenXRRuntime.pluginVersion;
        }
        catch (Exception e)
        {
            VRLog.Debug("Core", $"OpenXRRuntime managed API unavailable ({e.Message}) — using P/Invoke fallback.");
        }
        if (string.IsNullOrEmpty(name) && OpenXRDiagnostics.TryGetActiveRuntimeName(out string n))
            name = n;
        if (string.IsNullOrEmpty(version) &&
            OpenXRDiagnostics.TryGetActiveRuntimeVersion(out ushort maj, out ushort min, out ushort pat))
            version = $"{maj}.{min}.{pat}";
        if (string.IsNullOrEmpty(name))
            name = candidate.Name;

        VRSession.RuntimeName = name;
        VRLog.Info("Core", $"OpenXR session up — runtime: {name} {version} " +
                           $"(OpenXR plugin {(string.IsNullOrEmpty(pluginVersion) ? "?" : pluginVersion)}), " +
                           $"render mode: {OpenXRSettings.Instance.renderMode}, " +
                           $"activeLoader: {DescribeActiveLoader()}.");

        // Success report too — the file then contains the whole story of the run.
        OpenXRDiagnostics.AppendReportToFile($"SUCCESS — runtime: {name} {version}, candidate: {candidate}");

        // The display subsystem starts rendering only after the runtime reports XrReady
        // (a few frames). Watch it so the log states clearly whether the HMD ever lit up.
        if (VRSession.CoroutineHost != null)
            VRSession.CoroutineHost.StartCoroutine(WatchDisplayRunning());
        else
            VRLog.Warn("Core", "No coroutine host — cannot watch for the display subsystem to start rendering.");
    }

    private static string DescribeActiveLoader()
    {
        XRLoader? loader = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
            ? XRGeneralSettings.Instance.Manager.activeLoader
            : null;
        return loader != null ? loader.GetType().Name : "null";
    }

    /// <summary>
    /// Post-init watchdog: logs the moment the display subsystem actually starts
    /// rendering (XrReady processed), or an actionable error when it never does —
    /// which is exactly the "SteamVR/VD starts but the HMD stays black" symptom.
    /// </summary>
    private static IEnumerator WatchDisplayRunning()
    {
        var displays = new List<XRDisplaySubsystem>();
        for (int frame = 0; frame < DisplayRunningWatchdogFrames; frame++)
        {
            SubsystemManager.GetInstances(displays);
            if (displays.Any(d => d.running))
            {
                VRLog.Note("Core", $"XR display subsystem is RUNNING (HMD rendering) after {frame} frame(s).");
                yield break;
            }
            yield return null;
        }

        VRLog.Error("Core",
            $"XR display subsystem did NOT start rendering within {DisplayRunningWatchdogFrames} frames — " +
            "the runtime never reached the READY state (headset asleep/not connected? runtime waiting on " +
            "graphics requirements — desktop OpenXR needs D3D11, see the environment line above / " +
            "'-force-d3d11'). Check Player.log for '[XR]' native errors " +
            "(docs/TESTING-P1.md §4) and attach it together with LogOutput.log and openxr-diagnostics.log.");
        OpenXRDiagnostics.AppendReportToFile("WATCHDOG — display subsystem never reached running state");
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
