using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

internal static class Program
{
    sealed class Descriptor : ISubsystemDescriptor { public string id { get; init; } = ""; }
    static int assertions;
    static void Check(bool condition, string detail) { assertions++; if (!condition) throw new InvalidOperationException(detail); }
    static void Reject(Action call, string detail)
    {
        try { call(); }
        catch (ArgumentException) { Check(true, detail); return; }
        catch (InvalidOperationException) { Check(true, detail); return; }
        Check(false, detail);
    }
    static void Main(string[] args)
    {
        string directory = Path.Combine(args[0], "owned mod resources"); Directory.CreateDirectory(directory);
        Application.platform = RuntimePlatform.WindowsPlayer;
        SystemInfo.graphicsDeviceType = GraphicsDeviceType.Direct3D11;
        Check(!QuestStandalonePlatform.Enabled && !QuestStandalonePlatform.ModRunning && !QuestStandalonePlatform.RigReady, "desktop-gate: unconfigured desktop unexpectedly enabled standalone");
        Reject(() => QuestStandalonePlatform.Configure(directory, _ => true, () => true, () => true), "desktop-gate: desktop accepted standalone configuration");
        Color desktop = QuestStandalonePlatform.MixedRealityClearColor(new Color(.1f, .9f, .2f, .25f));
        Check(desktop.r == .1f && desktop.g == .9f && desktop.b == .2f && desktop.a == 1, "desktop-key: existing chroma key RGB/opaque alpha changed");
        Check(RuntimeDepsLoader.PluginDir == Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "desktop-resource: desktop plugin resource directory changed");
        Check(!RuntimeDepsLoader.LoadAll(), "desktop-deps: missing desktop RuntimeDeps was silently accepted");
        SubsystemManager.Descriptors.Add(new Descriptor { id = "OpenXR Display" });
        SubsystemManager.Descriptors.Add(new Descriptor { id = "OpenXR Input" });
        Check(OpenXRBootstrap.Start(null, null, true), "desktop-init: original desktop bootstrap no longer initialized its own loader");
        var desktopManager = XRGeneralSettings.Instance.Manager;
        Check(ScriptableObject.Creations == 6 && XRGeneralSettings.Initializations == 1 && desktopManager.Starts == 1, "desktop-init: original settings/profiles/init/start path changed");
        Check(SubsystemManager.Displays.Count == 1 && !SubsystemManager.Displays[0].running && VRSession.IsRunning, "desktop-existence: asynchronous display existence criterion changed to synchronous running");
        OpenXRBootstrap.Stop();
        Check(desktopManager.Stops == 1 && desktopManager.Deinitializations == 1 && Object.Destroyed.Contains(desktopManager), "desktop-teardown: owned desktop session was not stopped/deinitialized/destroyed");
        Check(!VRSession.IsRunning && VRSession.RuntimeName == null, "desktop-teardown: desktop VR state was not cleared");

        // The Android player provides pre-existing RUNNING subsystems and settings.
        // Its graphics API and missing desktop descriptors must never trigger the
        // desktop D3D11/preloader/registry/settings path.
        VRLog.Lines.Clear();
        Application.platform = RuntimePlatform.Android;
        SystemInfo.graphicsDeviceType = GraphicsDeviceType.OpenGLES3;
        SubsystemManager.Descriptors.Clear(); SubsystemManager.Displays.Clear(); SubsystemManager.Inputs.Clear();
        var display = new XRDisplaySubsystem { running = true }; var input = new XRInputSubsystem { running = true };
        SubsystemManager.Displays.Add(display); SubsystemManager.Inputs.Add(input);
        var ownerLoader = new OpenXRLoader(); var ownerManager = new XRManagerSettings { activeLoader = ownerLoader };
        var ownerSettings = new XRGeneralSettings { Manager = ownerManager }; XRGeneralSettings.Instance = ownerSettings;
        var ownerFeatures = OpenXRSettings.Instance.features;
        int creations = ScriptableObject.Creations, init = XRGeneralSettings.Initializations, registry = OpenXRRuntimeRegistry.CandidatesRead, descriptorReads = SubsystemManager.DescriptorReads;
        Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", "owned-player-env-sentinel");
        Check(!RuntimeDepsLoader.LoadAll(), "android-explicit-gate: unconfigured Android attempted dependency loading or silently adopted packages");
        Check(!OpenXRBootstrap.Start("forbidden", "forbidden", false), "android-explicit-gate: unconfigured Android adopted a running display without authorization");
        Check(ScriptableObject.Creations == creations && OpenXRRuntimeRegistry.CandidatesRead == registry && SubsystemManager.DescriptorReads == descriptorReads, "android-explicit-gate: failed Android gate executed desktop work");
        bool nativeActive = false, requestedActive = true, ownerRunning = true;
        long sessionGeneration = 1;
        int nativeCalls = 0;
        Func<bool, bool> setNative = wanted => { nativeCalls++; nativeActive = wanted && requestedActive; return nativeActive; };
        Reject(() => QuestStandalonePlatform.Configure("relative-resources", setNative, () => nativeActive, () => ownerRunning), "resource-validation: relative Android resource path accepted");
        Reject(() => QuestStandalonePlatform.Configure(Path.Combine(directory, "missing"), setNative, () => nativeActive, () => ownerRunning), "resource-validation: missing Android resource path accepted");
        Reject(() => QuestStandalonePlatform.Configure(directory, setNative, null!, () => ownerRunning), "resource-validation: null passthrough observer accepted");
        Check(!QuestStandalonePlatform.Enabled, "resource-validation: failed configuration partially enabled standalone");
        QuestStandalonePlatform.Configure(directory, setNative, () => nativeActive, () => ownerRunning, () => sessionGeneration);
        Check(QuestStandalonePlatform.Enabled && QuestStandalonePlatform.ResourceDirectory == Path.GetFullPath(directory) && RuntimeDepsLoader.PluginDir == Path.GetFullPath(directory), "android-resource: player-owned resource root was not used");
        Reject(() => QuestStandalonePlatform.Configure(directory, setNative, () => nativeActive, () => ownerRunning), "single-config: live player callbacks were silently replaced");
        Color clear = QuestStandalonePlatform.MixedRealityClearColor(new Color(0, 1, 0, 1));
        Check(clear.r == 0 && clear.g == 0 && clear.b == 0 && clear.a == 0, "native-alpha: Quest MR retained an opaque/colored greenscreen clear");
        Check(RuntimeDepsLoader.LoadAll() && RuntimeDepsLoader.LoadAll(), "android-deps: compiled player packages were not adopted idempotently");
        Check(AppDomain.CurrentDomain.GetData("GloomhavenVR.RuntimeInitializersInvoked") == null, "android-initializers: player XR initialization hooks were replayed");

        ownerRunning = false;
        Check(!OpenXRBootstrap.Start(null, null, true) && !VRSession.IsRunning, "owner-session-gate: player callback reported no active OpenXR session yet adoption succeeded");
        ownerRunning = true; display.running = false;
        Check(!OpenXRBootstrap.Start(null, null, true), "display-running-gate: Android adopted a display that had not reached RUNNING");
        display.running = true; input.running = false;
        Check(!OpenXRBootstrap.Start(null, null, true), "input-running-gate: Android adopted a session without running input");
        input.running = true;
        var core = new CoreModule(); core.Init();
        Check(VRSession.IsRunning && QuestStandalonePlatform.ModRunning && VRSession.RuntimeName == "Quest player OpenXR", "core-lifecycle: actual CoreModule did not publish the adopted player session");
        Check(Loc.Starts == 1 && ExceptionTraces.Starts == 1 && PerfMonitor.Starts == 1, "core-lifecycle: original localization/trace/performance init was bypassed");
        Check(GameObject.AddedComponents.Contains(typeof(VRHeartbeat)) && GameObject.AddedComponents.Contains(typeof(VRPresenceWatch)), "core-lifecycle: running-session heartbeat/presence integration was bypassed");
        Check(!QuestStandalonePlatform.RigReady, "rig-ready: session existence was mislabeled as a built mod rig");
        VRRigDriver.HeadCamera = new Camera(); Check(QuestStandalonePlatform.RigReady, "rig-ready: real mod head camera did not publish rig availability");
        Check(ReferenceEquals(QuestStandalonePlatform.HeadCamera, VRRigDriver.HeadCamera) && QuestStandalonePlatform.PresentationLayer == 27,
            "delivery-head: temporary delivery UI must use the running original mod camera and layer");
        Check(!QuestStandalonePlatform.DebugLogging, "presentation-debug: detailed startup census must remain disabled without Debug logging");
        VRLog.WantsDebug = true;
        Check(QuestStandalonePlatform.DebugLogging, "presentation-debug: Debug startup census did not follow the actual logging gate");
        VRRigDriver.HeadCamera.enabled = false;
        Check(!QuestStandalonePlatform.RigReady, "active-head-gate: disabled head camera was reported as a running rig");
        Check(QuestStandalonePlatform.HeadCamera == null, "delivery-head: disabled camera cannot own the delivery view");
        VRRigDriver.HeadCamera.enabled = true; VRRigDriver.HeadCamera.gameObject.activeInHierarchy = false;
        Check(!QuestStandalonePlatform.RigReady, "active-head-gate: inactive head camera object was reported as a running rig");
        VRRigDriver.HeadCamera.gameObject.activeInHierarchy = true;
        Check(QuestStandalonePlatform.RigReady, "active-head-gate: reactivated head camera did not restore rig availability");
        Check(ScriptableObject.Creations == creations && XRGeneralSettings.Initializations == init && OpenXRRuntimeRegistry.CandidatesRead == registry && SubsystemManager.DescriptorReads == descriptorReads, "android-no-second-session: adoption executed desktop preflight/settings/registry/init");
        Check(ReferenceEquals(XRGeneralSettings.Instance, ownerSettings) && ReferenceEquals(ownerManager.activeLoader, ownerLoader) && ReferenceEquals(OpenXRSettings.Instance.features, ownerFeatures), "android-owner: player settings/loader/interaction profiles were replaced");
        Check(Environment.GetEnvironmentVariable("XR_RUNTIME_JSON") == "owned-player-env-sentinel", "android-runtime-env: adoption changed the player runtime selector");
        Check(!VRLog.Lines.Any(line => line.Contains("GRAPHICS API IS OpenGLES3") || line.Contains("Graphics jobs: UNKNOWN")), "android-desktop-warning: Android emitted desktop D3D11/preloader guidance");

        Check(QuestStandalonePlatform.SetPassthrough(true) && QuestStandalonePlatform.PassthroughActive, "native-enable: existing native underlay was not activated");
        for (int i = 0; i < 1000; i++) Check(QuestStandalonePlatform.SetPassthrough(true), "native-enable: deduplicated enabled state was lost");
        Check(nativeCalls == 1, "native-dedupe: repeated MR ticks called/logged native enable every frame");
        Check(QuestStandalonePlatform.SetPassthrough(false) && !QuestStandalonePlatform.PassthroughActive, "native-disable: inactive native return was mistaken for disable failure");
        requestedActive = false;
        Check(!QuestStandalonePlatform.SetPassthrough(true), "native-failure: unavailable native underlay was presented as active");
        int warningCount = VRLog.Lines.Count(line => line.Contains("did not reach the requested state"));
        for (int i = 0; i < 1000; i++) Check(!QuestStandalonePlatform.SetPassthrough(true), "native-failure: failed state falsely matched");
        Check(VRLog.Lines.Count(line => line.Contains("did not reach the requested state")) == warningCount && nativeCalls == 3, "native-failure-bound: failed MR repeats native work or warnings every frame");
        // Toggle off/on retries the unavailable feature while retaining normal VR.
        Check(QuestStandalonePlatform.SetPassthrough(false), "native-retry: failed feature could not be disabled");
        requestedActive = true; Check(QuestStandalonePlatform.SetPassthrough(true), "native-retry: fresh player request did not recover passthrough");
        // Native instance recreation clears wanted=false and can finish between
        // two frames. The original request remains true; the new session epoch
        // must nevertheless trigger exactly one fresh enable request.
        nativeActive = false; sessionGeneration++;
        Check(QuestStandalonePlatform.SetPassthrough(true) && nativeActive && nativeCalls == 6, "native-session-recreate: unchanged MR request did not re-enable the new native session");
        for (int i = 0; i < 1000; i++) Check(QuestStandalonePlatform.SetPassthrough(true), "native-session-recreate: new native session lost the requested mode");
        Check(nativeCalls == 6, "native-session-recreate: native epoch recovery repeated work every frame");
        core.Shutdown();
        Check(!VRSession.IsRunning && VRSession.RuntimeName == null && !QuestStandalonePlatform.ModRunning && !QuestStandalonePlatform.RigReady, "core-teardown: plugin-local VR state survived shutdown");
        Check(!nativeActive && nativeCalls == 7, "core-teardown: plugin teardown did not release its native passthrough request");
        Check(ownerManager.Stops == 0 && ownerManager.Deinitializations == 0 && ownerManager.Starts == 0, "external-teardown: plugin stopped/reinitialized the player-owned XR manager");
        Check(!Object.Destroyed.Contains(ownerSettings) && !Object.Destroyed.Contains(ownerManager) && !Object.Destroyed.Contains(ownerLoader) && display.running && input.running, "external-teardown: plugin destroyed player settings/loader or stopped its running subsystems");
        Check(Loc.Stops == 1 && ExceptionTraces.Stops == 1 && PerfMonitor.Stops == 1, "core-teardown: original core module cleanup was bypassed");
        Application.platform = RuntimePlatform.WindowsPlayer;
        Check(!QuestStandalonePlatform.Enabled, "configured-desktop-gate: configured bridge stayed active outside Android");
        Check(!QuestStandalonePlatform.SetPassthrough(true) && nativeCalls == 7, "configured-desktop-gate: desktop called the Android native bridge");
        Console.WriteLine("PASS Quest platform/core lifecycle: " + assertions + " assertions (XR/environment seams; not native rendering/AOT proof)");
    }
}
