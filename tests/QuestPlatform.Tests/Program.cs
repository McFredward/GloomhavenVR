using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR;
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
        EyeAllocationCases(directory);
        Application.platform = RuntimePlatform.WindowsPlayer;
        SystemInfo.graphicsDeviceType = GraphicsDeviceType.Direct3D11;
        Check(!FrameDefaults.Active, "desktop-mobile-defaults: ordinary PC must keep its own defaults");
        FrameLaunchOptIn.Marker = true;
        Check(FrameDefaults.Active, "frame-mobile-defaults: opted-in standalone Frame retains its defaults");
        FrameLaunchOptIn.Marker = false;
        Check(!QuestStandalonePlatform.Enabled && !QuestStandalonePlatform.ModRunning && !QuestStandalonePlatform.RigReady, "desktop-gate: unconfigured desktop unexpectedly enabled standalone");
        GloomhavenVR.WorldUI.FlatScreen.OwnedCamera = new Camera();
        Check(!QuestStandalonePlatform.IsFlatScreenVideoTarget(GloomhavenVR.WorldUI.FlatScreen.OwnedCamera), "desktop-video: configured capture must not enable Quest output");
        int videoPrepared = 0, videoSampled = 0;
        Action prepareVideo = () => videoPrepared++;
        Action sampledVideo = () => videoSampled++;
        QuestStandalonePlatform.FlatScreenVideoSampling += prepareVideo;
        QuestStandalonePlatform.FlatScreenVideoSampled += sampledVideo;
        QuestStandalonePlatform.PrepareFlatScreenVideoSample();
        QuestStandalonePlatform.ObserveFlatScreenVideoSample();
        Check(videoPrepared == 0 && videoSampled == 0,
            "desktop-video: standalone sampling callbacks must not run on desktop");
        var desktopScreen = new Shader { name = "Hidden/BlitCopy" };
        Check(QuestStandalonePlatform.SelectFlatScreenShader(desktopScreen) == desktopScreen && Resources.Loads == 0,
            "desktop-screen: desktop shader choice must remain unchanged without resource reads");
        var desktopMaterial = new Material { shader = desktopScreen };
        QuestStandalonePlatform.SetFlatScreenEyes(desktopMaterial, new Texture(), new Texture());
        QuestStandalonePlatform.SetFlatScreenMono(desktopMaterial, new Texture());
        Check(desktopMaterial.textures.Count == 0 && desktopMaterial.floats.Count == 0,
            "desktop-screen: standalone stereo binding must not touch desktop materials");
        Reject(() => QuestStandalonePlatform.Configure(directory, _ => true, () => true, () => true), "desktop-gate: desktop accepted standalone configuration");
        Color desktop = QuestStandalonePlatform.MixedRealityClearColor(new Color(.1f, .9f, .2f, .25f));
        Check(desktop.r == .1f && desktop.g == .9f && desktop.b == .2f && desktop.a == 1, "desktop-key: existing chroma key RGB/opaque alpha changed");
        Check(RuntimeDepsLoader.PluginDir == Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "desktop-resource: desktop plugin resource directory changed");
        Check(!RuntimeDepsLoader.LoadAll(), "desktop-deps: missing desktop RuntimeDeps was silently accepted");
        SubsystemManager.Descriptors.Add(new Descriptor { id = "OpenXR Display" });
        SubsystemManager.Descriptors.Add(new Descriptor { id = "OpenXR Input" });
        Check(OpenXRBootstrap.Start(null, null, true), "desktop-init: original desktop bootstrap no longer initialized its own loader");
        var desktopManager = XRGeneralSettings.Instance.Manager;
        Check(ScriptableObject.Creations == 7 && XRGeneralSettings.Initializations == 1 && desktopManager.Starts == 1, "desktop-init: original settings/profiles/blend feature/init/start path changed");
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
        EnvironmentReceiptCases();
        Check(FrameDefaults.Active && QuestStandalonePlatform.StandaloneMsaaDefault == FrameDefaults.MsaaLevel,
            "quest-mobile-defaults: Quest must share current Frame defaults without a desktop opt-in marker");
        Check(!FrameNativePassthrough.Required, "quest-native-backend: shared defaults must not select Frame composition on Quest");
        FrameNativePassthrough.Attach(new OpenXrEnvironmentBlendFeature());
        Check(!FrameNativePassthrough.Required, "quest-native-reattach: a new native feature must retain Quest's own backend");
        Check(QuestStandalonePlatform.Enabled && QuestStandalonePlatform.ResourceDirectory == Path.GetFullPath(directory) && RuntimeDepsLoader.PluginDir == Path.GetFullPath(directory), "android-resource: player-owned resource root was not used");
        Check(QuestStandalonePlatform.SelectFlatScreenShader(desktopScreen) == Resources.ScreenShader,
            "quest-screen: enabled platform selects validated Tex2D world shader");
        var eyeMaterial = new Material { shader = Resources.ScreenShader };
        var left = new Texture(); var right = new Texture();
        QuestStandalonePlatform.SetFlatScreenEyes(eyeMaterial, left, right);
        Check(eyeMaterial.textures["_MainTex"] == left && eyeMaterial.textures["_RightTex"] == right && eyeMaterial.floats["_StereoCapture"] == 1,
            "quest-screen: actual distinct capture identities must reach the GPU eye inputs");
        QuestStandalonePlatform.SetFlatScreenEyes(eyeMaterial, left, null);
        Check(eyeMaterial.textures["_RightTex"] == left, "quest-screen: unavailable right capture safely uses the current left input");
        QuestStandalonePlatform.SetFlatScreenEyes(eyeMaterial, left, right);
        QuestStandalonePlatform.SetFlatScreenMono(eyeMaterial, left);
        Check(eyeMaterial.textures["_MainTex"] == left && eyeMaterial.textures["_RightTex"] == left && eyeMaterial.floats["_StereoCapture"] == 0,
            "quest-screen: persistent material mono handover clears stale right-eye inputs and state");
        Resources.ScreenShader.isSupported = false;
        Reject(() => QuestStandalonePlatform.SelectFlatScreenShader(desktopScreen), "quest-screen: unsupported shader must fail visibly");
        Resources.ScreenShader.isSupported = true;
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
        QuestStandalonePlatform.PrepareFlatScreenVideoSample(new Camera());
        Check(videoPrepared == 0, "video-head: unrelated camera must not trigger final capture composition");
        QuestStandalonePlatform.PrepareFlatScreenVideoSample(VRRigDriver.HeadCamera);
        QuestStandalonePlatform.ObserveFlatScreenVideoSample();
        Check(videoPrepared == 1 && videoSampled == 1,
            "video-head: original head consumer must run preparation and observation boundaries");
        QuestStandalonePlatform.FlatScreenVideoSampling -= prepareVideo;
        QuestStandalonePlatform.FlatScreenVideoSampled -= sampledVideo;
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
        Check(!QuestEyeResolution.TryApply(1.5f), "gles-eye-allocation: Quest GLES must retain the original render controller");
        SystemInfo.graphicsDeviceType = GraphicsDeviceType.Vulkan;
        XRSettings.Seed(1f, .8f);
        float savedScale = 1.5f;
        RenderQualityAllocationFixture.Tick();
        Check(XRSettings.AllocationWrites == 0 && XRDisplaySubsystem.MsaaWrites == 0
            && QualitySettings.antiAliasing == FrameDefaults.MsaaLevel && RenderQualityAllocationFixture.SavedMsaa == 8,
            "vulkan-render-controller: actual render controller must keep startup eye allocation and sample count without rewriting config");
        Check(QuestStandalonePlatform.FixedEyeTextureAllocation, "vulkan-eye-gate: configured Quest Vulkan must retain swapchain allocation");
        Check(QuestEyeResolution.TryApply(savedScale) && savedScale == 1.5f
            && QuestEyeResolution.EffectiveScale(savedScale) == 1.5f,
            "vulkan-eye-range: supersampling must remain selectable for restart without resizing the live allocation");
        Check(XRSettings.AllocationWrites == 0 && XRSettings.renderViewportScale == 1f,
            "vulkan-eye-allocation: fallback must restore full viewport without native resolution setter");
        int reports = VRLog.Lines.Count;
        for (int i = 0; i < 1000; i++) QuestEyeResolution.TryApply(savedScale);
        Check(VRLog.Lines.Count == reports && XRSettings.AllocationWrites == 0 && XRSettings.ViewportWrites == 1,
            "vulkan-eye-bounded: unchanged saved setting must not repeat logs or native writes");
        QuestEyeResolution.TryApply(0.7f);
        Check(XRSettings.renderViewportScale == 1f && XRSettings.ViewportWrites == 1 && XRSettings.AllocationWrites == 0,
            "vulkan-eye-viewport: changed desired scale must remain saved for restart with a full viewport");
        QuestResolutionOptionsCases();
        VRLog.WantsDebug = true;
        for (int i = 0; i < 1000; i++) QuestEyeResolution.TryApply(i % 2 == 0 ? 1.2f : 1.4f);
        Check(Debug.Lines.Count(line => line.Contains("[Quest startup] resolution ")) == 2,
            "quest-resolution-debug-bounded: repeated user choices must retain at most two protected Debug records");
        VRLog.WantsDebug = false;
        QuestEyeResolution.TryApply(1f);
        Check(XRSettings.renderViewportScale == 1f && XRSettings.ViewportWrites == 1 && XRSettings.AllocationWrites == 0,
            "vulkan-eye-restore: later desired scale must not write any running eye resource");
        Application.platform = RuntimePlatform.WindowsPlayer;
        Check(!QuestStandalonePlatform.Enabled, "configured-desktop-gate: configured bridge stayed active outside Android");
        var desktopResolution = new GloomhavenVR.WorldUI.ConfigCatalog.ConfigItem("RenderQuality", "EyeResolutionScale");
        Check(!GloomhavenVR.WorldUI.VROptionsTab.TestQuestResolution(desktopResolution),
            "quest-resolution-desktop: ordinary PC keeps its shared generic row without extra notes");
        Check(Loc.ConfigHelpForPlayers("RenderQuality", "EyeResolutionScale") != QuestText.Get("fixedEyeResolution", false),
            "quest-resolution-desktop-help: desktop player help stays unchanged");
        Check(!QuestEyeResolution.TryApply(1.5f) && QuestEyeResolution.EffectiveScale(1.5f) == 1.5f,
            "desktop-eye-allocation: desktop Vulkan must retain its original supersampling controller");
        Check(!QuestStandalonePlatform.SetPassthrough(true) && nativeCalls == 7, "configured-desktop-gate: desktop called the Android native bridge");
        RenderQualityAllocationFixture.Reset(); RenderQualityAllocationFixture.Tick();
        Check(XRSettings.AllocationWrites == 0 && XRDisplaySubsystem.MsaaWrites == 1
            && QualitySettings.antiAliasing == 8 && XRSettings.eyeTextureResolutionScale == 1f,
            "desktop-render-controller: current desktop MSAA and stable-allocation viewport controller must remain reachable");
        Console.WriteLine("PASS Quest platform/core lifecycle: " + assertions + " assertions (XR/environment seams; not native rendering/AOT proof)");
    }

    static void EnvironmentReceiptCases()
    {
        var index = System.Text.Encoding.UTF8.GetBytes("{\"format\":1,\"entries\":[]}");
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(index)).ToLowerInvariant();
        string source = "{\"path\":\"pcg_databases_assets_assets/pcg/a.bundle\",\"sha256\":\"" + new string('b', 64) + "\"}";
        string document = "{\"schema\":1,\"association\":\"owned-PC-original-static-environment-GHEM1\",\"gameKey\":\"" + new string('a',64)
            + "\",\"indexSha256\":\"" + hash + "\",\"sources\":[" + source + "]}";
        byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);
        Check(QuestStandalonePlatform.OwnedEnvironmentSources(index, Bytes(document)).SetEquals(new[] {
            "pcg_databases_assets_assets/pcg/a.bundle:" + new string('b',64) }), "environment-receipt: exact index and source association");
        void Invalid(string text, string name)
        {
            try { QuestStandalonePlatform.OwnedEnvironmentSources(index, Bytes(text)); }
            catch (InvalidDataException) { Check(true, "environment-receipt: " + name); return; }
            Check(false, "environment-receipt: rejected " + name);
        }
        Invalid(document.Replace(hash, new string('c',64)), "different index");
        Invalid(document.Replace("a.bundle", "../a.bundle"), "escaping source");
        Invalid(document.Replace("[" + source + "]", "[" + source + "," + source + "]"), "duplicate source");
        Invalid(document.Replace("[" + source + "]", "[]"), "empty source set");
        Invalid(document.Replace("\"schema\":1", "\"schema\":1.5"), "fractional schema");
        Invalid(document.Replace("\"sources\":[", "\"sources\":null,\"ignored\":["), "null sources");
        Check(SelfUpdateJson.Parse(new string(' ', 4 * 1024 * 1024) + "{}") == null,
            "environment-json: ordinary self-update limit remains four MiB");
        Check(QuestEnvironmentJson.Read(Bytes(new string(' ', 4 * 1024 * 1024) + "{}")).Count == 0,
            "environment-json: owned eight MiB allowance does not lower the native index bound");
    }

    static void QuestResolutionOptionsCases()
    {
        Check(!VRLog.Lines.Any(line => line.Contains("Live changes use the viewport")),
            "quest-resolution-log: actual controller must not announce desktop live-viewport behavior on Quest");
        Check(QuestEyeResolution.EffectiveScale(2.5f) == 2f && QuestEyeResolution.EffectiveScale(.1f) == .5f
            && QuestEyeResolution.EffectiveScale(float.NaN) == FrameDefaults.EyeResolutionScale,
            "quest-resolution-sanitize: UI and startup share finite range and existing mobile default");
        var item = new GloomhavenVR.WorldUI.ConfigCatalog.ConfigItem("RenderQuality", "EyeResolutionScale");
        foreach (string language in new[] { "English", "German" })
        {
            Loc.CurrentLanguage = language;
            bool german = language == "German";
            string help = QuestText.Get("fixedEyeResolution", german);
            Check(Loc.ConfigHelpForPlayers(item.Section, item.Key) == help
                && Loc.ModHelpForPlayers("h_vr_o_eyeres") == help,
                "quest-resolution-help: short player help must reach the Quest restart contract in both languages");
            GloomhavenVR.WorldUI.VROptionsTab.ResetRows();
            Check(GloomhavenVR.WorldUI.VROptionsTab.TestQuestResolution(item)
                && GloomhavenVR.WorldUI.VROptionsTab.Bars == 1
                && GloomhavenVR.WorldUI.VROptionsTab.Steppers == 0
                && ReferenceEquals(GloomhavenVR.WorldUI.VROptionsTab.ReceivedItem, item)
                && GloomhavenVR.WorldUI.VROptionsTab.Caption == QuestText.Get("eyeResolutionName", german),
                "quest-resolution-row: Quest reuses its exact current catalog item and ordinary bar/arrows with a restart caption");
            Check(GloomhavenVR.WorldUI.VROptionsTab.Note == string.Format(QuestText.Get("eyeResolutionCurrent", german),
                    XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight, 3410, 3572)
                && GloomhavenVR.WorldUI.VROptionsTab.Multiline,
                "quest-resolution-pixels: visible note distinguishes actual dimensions from an estimated next-start target");
            item.Number = .8f;
            GloomhavenVR.WorldUI.VROptionsTab.RefreshNotes();
            Check(GloomhavenVR.WorldUI.VROptionsTab.Note == string.Format(QuestText.Get("eyeResolutionCurrent", german),
                    XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight, 2728, 2858),
                "quest-resolution-live-readout: arrow/slider refresh must update the estimated target while actual pixels remain unchanged");
            item.Number = 1f;
        }
        GloomhavenVR.WorldUI.VROptionsTab.ResetRows();
        GloomhavenVR.WorldUI.VROptionsTab.BarAvailable = false;
        Check(GloomhavenVR.WorldUI.VROptionsTab.TestQuestResolution(item)
            && GloomhavenVR.WorldUI.VROptionsTab.Steppers == 1,
            "quest-resolution-fallback: missing slider donor must retain shared stepper editing");
        GloomhavenVR.WorldUI.VROptionsTab.ResetRows();
        Check(!GloomhavenVR.WorldUI.VROptionsTab.TestQuestResolution(
            new GloomhavenVR.WorldUI.ConfigCatalog.ConfigItem("Other", "EyeResolutionScale"))
            && !GloomhavenVR.WorldUI.VROptionsTab.TestQuestResolution(
            new GloomhavenVR.WorldUI.ConfigCatalog.ConfigItem("RenderQuality", "Other"))
            && GloomhavenVR.WorldUI.VROptionsTab.Bars == 0,
            "quest-resolution-address: unrelated rows must retain generic behavior");
        Loc.CurrentLanguage = "English";
    }

    static void EyeAllocationCases(string directory)
    {
        // Independent hardware anchors: do not derive expectations through the
        // production conversion. Frame B661 actually submitted2728px at0.8;
        // Quest B678 submitted1680x1760 at1.0 and1344x1408 at0.8.
        Check(Math.Abs(1680f * QuestEyeAllocation.ProviderScale(.8f) - 2728f) < .001f,
            "eye-density-reference: saved0.8 must match the actual Frame shorter-axis raster density");
        Check(Math.Abs(1680f * QuestEyeAllocation.ProviderScale(1f) - 3410f) < .001f
            && Math.Abs(1760f * QuestEyeAllocation.ProviderScale(1f) - 3572.381f) < .002f,
            "eye-density-aspect: calibrated Quest scale must preserve provider aspect at the Frame reference");
        Check(QuestEyeAllocation.ProviderScale(.8f) > .8f
            && QuestEyeAllocation.ProviderScale(1f) > QuestEyeAllocation.ProviderScale(.8f)
            && QuestEyeAllocation.ProviderScale(2f) > QuestEyeAllocation.ProviderScale(1f),
            "eye-density-monotone: every scale remains meaningful above the former small Quest default");
        Check(Math.Abs(QuestEyeAllocation.ProviderScale(float.NaN) - QuestEyeAllocation.ProviderScale(.8f)) < .0001f
            && QuestEyeAllocation.ProviderScale(.1f) == QuestEyeAllocation.ProviderScale(.5f)
            && QuestEyeAllocation.ProviderScale(3f) == QuestEyeAllocation.ProviderScale(2f),
            "eye-density-sanitize: invalid values retain the mobile default and configured range");
        string config = Path.Combine(directory, "allocation.cfg");
        Check(QuestEyeAllocation.ReadSavedScale(config, out string source) == FrameDefaults.EyeResolutionScale
            && source == "shared-mobile-default", "eye-startup-default: missing config must use the shared Frame default");
        foreach (var row in new[] {
            ("[RenderQuality]\nEyeResolutionScale = 0.7\n", .7f),
            ("[RenderQuality]\nEyeResolutionScale = 1.5\n", 1.5f),
            ("[RenderQuality]\nEyeResolutionScale = 2.0\n", 2f),
            ("[RenderQuality]\nEyeResolutionScale = 2.5\n", 2f),
            ("[RenderQuality]\nEyeResolutionScale = 0.1\n", .5f),
            ("[RenderQuality]\nEyeResolutionScale = NaN\n", FrameDefaults.EyeResolutionScale),
            ("[RenderQuality]\nEyeResolutionScale = Infinity\n", FrameDefaults.EyeResolutionScale),
            ("[RenderQuality]\nEyeResolutionScale = broken\n", FrameDefaults.EyeResolutionScale),
            ("[Other]\nEyeResolutionScale = 0.5\n", FrameDefaults.EyeResolutionScale),
            ("# ignored\n[RenderQuality]\nEyeResolutionScale = .6\n[Other]\nEyeResolutionScale = .9\n", .6f),
            ("[RenderQuality]\nEyeResolutionScale = .6\nEyeResolutionScale = .7\n", .7f) })
        {
            File.WriteAllText(config, row.Item1);
            Check(Math.Abs(QuestEyeAllocation.ReadSavedScale(config, out source) - row.Item2) < .0001f,
                "eye-startup-config: saved scale must be bounded, section-scoped and finite: " + row.Item1);
            Check(File.ReadAllText(config) == row.Item1, "eye-startup-config: config reading must not rewrite saved bytes");
        }
        File.WriteAllText(config, "[RenderQuality]\nEyeResolutionScale=.5\n" + new string('#', 256 * 1024));
        Check(QuestEyeAllocation.ReadSavedScale(config, out source) == FrameDefaults.EyeResolutionScale
            && source == "oversized-config-default", "eye-startup-bound: oversized config must not be scanned");

        Application.persistentDataPath = Path.Combine(directory, "persistent");
        string canonical = Path.Combine(Application.persistentDataPath, "quest-bepinex", "config", "dev.gloomhavenvr.rig.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
        File.WriteAllText(canonical, "[RenderQuality]\nEyeResolutionScale = .7\n");
        SubsystemManager.Displays.Clear();
        var display = new XRDisplaySubsystem(); SubsystemManager.Displays.Add(display);
        Application.platform = RuntimePlatform.WindowsPlayer; SystemInfo.graphicsDeviceType = GraphicsDeviceType.Vulkan;
        QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(display.AllocationWrites == 0 && Debug.Lines.Count == 0,
            "eye-startup-desktop: early hook must leave desktop Vulkan untouched");
        Application.platform = RuntimePlatform.Android; SystemInfo.graphicsDeviceType = GraphicsDeviceType.OpenGLES3;
        QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(display.AllocationWrites == 0 && Debug.Lines.Count == 0,
            "eye-startup-gles: early hook must leave the ordinary GLES controller untouched");
        SystemInfo.graphicsDeviceType = GraphicsDeviceType.Vulkan;
        QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(display.AllocationWrites == 1 && display.LiveAllocationWrites == 0
            && Math.Abs(display.scaleOfAllRenderTargets - QuestEyeAllocation.ProviderScale(.7f)) < .0001f
            && display.scaleOfAllViewports == 1f,
            "eye-startup-stopped: only the initialized stopped display may receive its saved allocation");
        Check(File.ReadAllText(canonical) == "[RenderQuality]\nEyeResolutionScale = .7\n",
            "eye-startup-preserve: startup selection must preserve the canonical rig config");
        for (int i = 0; i < 1000; i++) QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(display.AllocationWrites == 1 && Debug.Lines.Count == 1,
            "eye-startup-bounded: repeated feature callbacks must not repeat setters or logs");

        foreach (float scale in new[] { .5f, .8f, 1f, 1.5f, 2f })
        {
            ResetStartup(); Debug.Lines.Clear(); SubsystemManager.Displays.Clear();
            File.WriteAllText(canonical, "[RenderQuality]\nEyeResolutionScale=" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var stopped = new XRDisplaySubsystem(); SubsystemManager.Displays.Add(stopped);
            QuestStandalonePlatform.PrepareDisplayBeforeStart();
            Check(stopped.AllocationWrites == 1 && stopped.LiveAllocationWrites == 0
                && Math.Abs(stopped.scaleOfAllRenderTargets - QuestEyeAllocation.ProviderScale(scale)) < .0001f
                && stopped.scaleOfAllViewports == 1f,
                "eye-startup-supersample: every saved density must reach the stopped display through its calibrated provider scale: " + scale);
        }

        ResetStartup(); Debug.Lines.Clear(); SubsystemManager.Displays.Clear();
        File.WriteAllText(canonical, "[RenderQuality]\nEyeResolutionScale=2");
        var clamped = new XRDisplaySubsystem { MaximumAllocation = 2f };
        SubsystemManager.Displays.Add(clamped);
        QuestStandalonePlatform.PrepareDisplayBeforeStart(); QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(clamped.AllocationWrites == 1 && clamped.scaleOfAllRenderTargets == 2f
            && Debug.Lines.Count == 1 && Debug.Lines[0].Contains("providerRequested=4.0595")
            && Debug.Lines[0].Contains("allocation=2.00")
            && File.ReadAllText(canonical) == "[RenderQuality]\nEyeResolutionScale=2",
            "eye-density-provider-limit: a native clamp must remain visible in actual readback without retries or config rewriting");

        ResetStartup(); Debug.Lines.Clear(); SubsystemManager.Displays.Clear();
        var live = new XRDisplaySubsystem { running = true }; var other = new XRDisplaySubsystem();
        SubsystemManager.Displays.Add(other); SubsystemManager.Displays.Add(live);
        QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(live.AllocationWrites == 0 && other.AllocationWrites == 0 && live.LiveAllocationWrites == 0
            && Debug.Lines.Count == 1 && Debug.Lines[0].Contains("already running"),
            "eye-startup-live: late callback must retain every allocation, including other stopped displays");

        ResetStartup(); Debug.Lines.Clear(); SubsystemManager.Displays.Clear();
        QuestStandalonePlatform.PrepareDisplayBeforeStart(); QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(Debug.Lines.Count == 1 && Debug.Lines[0].Contains("no initialized display"),
            "eye-startup-absent: unavailable display must emit one bounded fallback report");
        ResetStartup(); Debug.Lines.Clear();
        var refusing = new XRDisplaySubsystem { RejectAllocation = true }; SubsystemManager.Displays.Add(refusing);
        QuestStandalonePlatform.PrepareDisplayBeforeStart(); QuestStandalonePlatform.PrepareDisplayBeforeStart();
        Check(refusing.AllocationWrites == 1 && Debug.Lines.Count == 1 && Debug.Lines[0].Contains("NotSupportedException"),
            "eye-startup-refusal: unsupported resource API must retain provider state without retries");
        SubsystemManager.Displays.Clear(); XRSettings.Seed(); Debug.Lines.Clear(); ResetStartup();
    }

    static void ResetStartup() => typeof(QuestEyeAllocation).GetField("_attempted", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
}
