#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace GloomhavenVR.Quest
{
    /// <summary>Adopts Unity's XR session and starts the original, statically woven mod.</summary>
    public sealed class QuestGameModLifecycle : MonoBehaviour
    {
        const float SessionTimeout = 30f, ModuleTimeout = 30f;
        public bool ContentReady { get; private set; }
        public bool PluginCreated { get { return plugin != null; } }
        public bool InitializationComplete { get { return PluginCreated && QuestStandaloneModuleHealth.InitializationComplete; } }
        public int CompletedModules { get { return PluginCreated ? QuestStandaloneModuleHealth.CompletedModules : 0; } }
        public bool RigReady { get { return PluginCreated && QuestStandalonePlatform.RigReady; } }
        public bool InviteKeyboardVisible { get { return PluginCreated && QuestStandaloneModuleHealth.InviteKeyboardVisible; } }
        public bool Available { get { return ContentReady && InitializationComplete && RigReady && UnitySessionRunning() && string.IsNullOrEmpty(Failure); } }
        public string Stage { get; private set; } = "pending";
        public string Failure { get; private set; }
        Plugin plugin;
        [SerializeField] Camera startupAnchor;
        [SerializeField] QuestLoadingView loadingView;
        public bool StartupViewAvailable { get { return loadingView != null && loadingView.Available; } }
        string lastObservation;
        bool activated;
        bool deliveryViewRequested;
        string viewState = "pending";
        QuestGameContentProgress viewProgress;
        int preparationSteps, preparationTotal = 5;
        QuestLoadingView renderedView;
        QuestGameContentProgress renderedProgress;
        string renderedState;
        int renderedSteps, renderedTotal;
        bool renderedGerman;

        public IEnumerator Activate(string resourceRoot)
        {
            if (activated) throw new InvalidOperationException("The real Quest mod lifecycle was already requested.");
            activated = true;
            ContentReady = true; // Bootstrap verified the selected bundle before calling us.
            Stage = "waiting-unity-xr";
            float until = Time.realtimeSinceStartup + SessionTimeout;
            while (!UnitySessionRunning())
            {
                if (Time.realtimeSinceStartup >= until) { Block("Unity OpenXR display/input session did not become ready before mod creation."); yield break; }
                yield return null;
            }
            try
            {
                // Configure before any plugin/config constructor. Unity owns the
                // loader/session throughout; no desktop XR teardown is invoked.
                QuestStandalonePlatform.Configure(Path.Combine(resourceRoot, "StreamingAssets"), QuestPassthroughFeature.SetEnabled,
                    () => QuestPassthroughFeature.Active, UnitySessionRunning, () => QuestPassthroughFeature.SessionGeneration);
                string bepRoot = Path.Combine(Application.persistentDataPath, "quest-bepinex");
                InitializeBepInEx(bepRoot);
                PrepareDiagnosticConfig(bepRoot);
                QuestStandaloneModuleHealth.InstallUnityLogListener();
                PrepareStartupView();
                StopLoadingView();
                Stage = "creating-real-plugin";
                plugin = gameObject.AddComponent<Plugin>();
                if (plugin == null) throw new InvalidOperationException("Unity refused the original GloomhavenVR.Plugin component.");
                Stage = "observing-real-modules";
                UnityEngine.Debug.Log("[Quest startup] original GloomhavenVR.Plugin component created; module completion and rig readiness require observation.");
            }
            catch (Exception error) { Block("Real mod creation: " + RootException(error)); }
            if (Failure != null) yield break;
            until = Time.realtimeSinceStartup + ModuleTimeout;
            while (!Available)
            {
                Observe();
                if (Failure != null) yield break;
                if (Time.realtimeSinceStartup >= until)
                {
                    Block("Real mod initialization did not reach a running rig: modules=" + CompletedModules
                        + " complete=" + InitializationComplete + " rig=" + RigReady + " UnityXR=" + UnitySessionRunning());
                    yield break;
                }
                yield return null;
            }
            Stage = "real-mod-running";
            Observe();
        }

        public void Observe()
        {
            if (!PluginCreated) return;
            string moduleFailure = QuestStandaloneModuleHealth.Failure;
            if (!string.IsNullOrEmpty(moduleFailure) && Failure == null) Block("Original module initialization: " + moduleFailure);
            RetireStartupAnchor();
            string observation = "modules=" + CompletedModules + " complete=" + InitializationComplete + " rig=" + RigReady
                + " UnityXR=" + UnitySessionRunning() + " stage=" + Stage;
            if (observation != lastObservation)
            {
                lastObservation = observation;
                // Keep lifecycle evidence newer than the five-second periodic
                // snapshot. B615's native abort followed real module creation
                // while its saved state still described file verification.
                // Observe runs on the Unity main thread and only persists when
                // this bounded lifecycle signature changes.
                GetComponent<QuestGameBootstrap>()?.SaveState();
                UnityEngine.Debug.Log("[Quest startup] real mod observation " + observation + " fullGameReady=false");
            }
        }

        static bool UnitySessionRunning()
        {
            var settings = XRGeneralSettings.Instance;
            var manager = settings != null ? settings.Manager : null;
            var loader = manager != null ? manager.activeLoader : null;
            if (loader == null) return false;
            var display = loader.GetLoadedSubsystem<XRDisplaySubsystem>();
            var input = loader.GetLoadedSubsystem<XRInputSubsystem>();
            return display != null && input != null && display.running && input.running;
        }

        static void InitializeBepInEx(string root)
        {
            Directory.CreateDirectory(root);
            Type standalone = Type.GetType("BepInEx.QuestStandalone, BepInEx", true);
            MethodInfo initialize = standalone.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (initialize == null || initialize.ReturnType != typeof(void)) throw new InvalidDataException("Generated standalone BepInEx initializer ABI is missing.");
            initialize.Invoke(null, new object[] { root });
        }

        static void PrepareDiagnosticConfig(string root)
        {
            string guid = null;
            foreach (object metadata in typeof(Plugin).GetCustomAttributes(false))
                if (metadata.GetType().FullName == "BepInEx.BepInPlugin")
                {
                    guid = metadata.GetType().GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance)?.GetValue(metadata) as string;
                    break;
                }
            if (string.IsNullOrEmpty(guid) || guid.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 || guid == "." || guid == "..")
                throw new InvalidDataException("Original plugin config identity is missing or unsafe.");
            string directory = Path.Combine(root, "config"), path = Path.Combine(directory, guid + ".cfg");
            Directory.CreateDirectory(directory);
            if (File.Exists(path)) return;
            // The maintainer tests at Debug. Preserve every existing config byte;
            // only a fresh diagnostic install receives these two initial values.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream)) writer.Write("[General]\nEnabled = true\nLogLevel = Debug\n");
            UnityEngine.Debug.Log("[Quest startup] fresh real-mod diagnostic configuration created; existing configuration is preserved.");
        }

        public void PrepareStartupView()
        {
            if (startupAnchor != null) return;
            GameObject anchor = new GameObject("Quest original startup camera anchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.position = new Vector3(0, 1.6f, 0);
            startupAnchor = anchor.AddComponent<Camera>();
            startupAnchor.tag = "MainCamera";
            // The initial scene must submit frames while bank delivery runs.
            // This camera renders only its temporary artwork; the plugin adopts
            // the same neutral reference once its verified assets are available.
            startupAnchor.cullingMask = 1 << 31;
            startupAnchor.clearFlags = CameraClearFlags.SolidColor;
            startupAnchor.backgroundColor = new Color(.025f, .03f, .045f, 1f);
            startupAnchor.stereoTargetEye = StereoTargetEyeMask.Both;
            startupAnchor.depth = -100;
            startupAnchor.enabled = true;
            loadingView = QuestLoadingView.Create(startupAnchor, 31);
            // No GraphicRaycaster, input actions, buttons, hands or pointer are
            // introduced here. Remove this artwork before original mod creation.
            UpdateStartupView(viewState, viewProgress, preparationSteps, preparationTotal);
            UnityEngine.Debug.Log("[Quest startup] temporary stereo loading view available before content delivery; neutral anchor retained for original VR rig.");
        }

        public void UpdateStartupView(string state, QuestGameContentProgress progress, int completedSteps = 0, int totalSteps = 5)
        {
            viewState = state;
            viewProgress = progress;
            preparationSteps = completedSteps;
            preparationTotal = totalSteps;
            if (deliveryViewRequested && (loadingView == null || !loadingView.Available))
            {
                // A native XR restart can temporarily remove the existing head.
                // Resume the noninteractive artwork when its owner returns.
                if (QuestStandalonePlatform.HeadCamera == null) return;
                if (loadingView != null) loadingView.Retire();
                loadingView = null;
                BeginDeliveryView();
            }
            if (loadingView == null) return;
            bool german = Application.systemLanguage == SystemLanguage.German;
            if (loadingView == renderedView && ReferenceEquals(progress, renderedProgress) && state == renderedState
                && completedSteps == renderedSteps && totalSteps == renderedTotal && german == renderedGerman) return;
            // Worker packets are immutable and bounded. Reformat only a new
            // packet/state or a recreated owner, rather than every Unity frame.
            renderedView = loadingView; renderedProgress = progress; renderedState = state;
            renderedSteps = completedSteps; renderedTotal = totalSteps; renderedGerman = german;
            bool measuring = progress != null && (state.StartsWith("checking-", StringComparison.Ordinal)
                || state.StartsWith("copying-", StringComparison.Ordinal) || state.StartsWith("extracting-", StringComparison.Ordinal));
            string phase = measuring ? progress.Phase : state;
            string key = state == "failed" ? "startupFailed"
                : phase == "copying-archive" ? "loadingReading"
                : phase == "extracting-file" ? "loadingUnpackingFile"
                : phase == "checking-files" || phase == "verifying-file" ? "loadingVerifyingFile"
                : phase == "verifying-archive" ? "loadingVerifying"
                : state == "starting-real-mod" || state == "loading-original-bootstrap" ? "loadingStarting"
                : state == "initializing-native-addressables" ? "loadingReading" : "loadingPreparing";
            int filePercent = measuring ? QuestLoadingView.Percent(progress.ProcessedBytes, progress.TotalBytes) : -1;
            bool selectedFile = measuring && progress.FileIndex > 0 && progress.FileIndex <= progress.FileCount;
            string file = selectedFile ? string.Format(CultureInfo.InvariantCulture, QuestText.Get("loadingFile", german),
                progress.FileIndex, progress.FileCount) : "";
            if (selectedFile && filePercent >= 0) file += " · " + filePercent.ToString(CultureInfo.InvariantCulture) + " %";
            string step = totalSteps > 0 ? string.Format(CultureInfo.InvariantCulture, QuestText.Get("loadingStep", german),
                Math.Min(totalSteps, Math.Max(0, completedSteps) + (completedSteps < totalSteps ? 1 : 0)), totalSteps) : "";
            loadingView.UpdateProgress(QuestText.Get(key, german), QuestText.Get("loadingOverall", german),
                completedSteps, totalSteps, measuring ? progress.OverallProcessedBytes : 0, measuring ? progress.OverallTotalBytes : -1,
                step, file, selectedFile ? QuestLoadingView.Basename(progress.File) : "");
        }

        void StopLoadingView()
        {
            if (loadingView != null) loadingView.Retire();
            loadingView = null;
            if (startupAnchor != null) { startupAnchor.cullingMask = 0; startupAnchor.stereoTargetEye = StereoTargetEyeMask.None; }
        }

        public void BeginDeliveryView()
        {
            deliveryViewRequested = true;
            if (loadingView != null) return;
            var head = QuestStandalonePlatform.HeadCamera;
            if (head == null) throw new InvalidOperationException("Original content delivery requires the observed real VR camera.");
            // The large original menu movies need worker verification too. Reuse
            // the existing rig for this temporary, noninteractive status canvas;
            // never add a second XR camera or a separate input/controller path.
            loadingView = QuestLoadingView.Create(head, QuestStandalonePlatform.PresentationLayer);
            UpdateStartupView(viewState, viewProgress, preparationSteps, preparationTotal);
        }
        public void EndDeliveryView() { deliveryViewRequested = false; StopLoadingView(); }

        void RetireStartupAnchor()
        {
            if (startupAnchor == null) return;
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || camera == startupAnchor || !camera.isActiveAndEnabled
                    || !camera.gameObject.scene.IsValid() || !camera.gameObject.scene.isLoaded
                    || camera.CompareTag("UICamera") || camera.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal)) continue;
                // FlatScreen can already have redirected the native MainCamera
                // to its own render target. Its tag still wins ResolveMenuCamera;
                // retaining our tagged anchor would prevent the genuine switch.
                if (!camera.CompareTag("MainCamera") && camera.targetTexture != null) continue;
                UnityEngine.Debug.Log("[Quest startup] startup anchor retired after native camera became available=" + camera.name);
                startupAnchor.enabled = false;
                Destroy(startupAnchor.gameObject); startupAnchor = null;
                break;
            }
        }

        void Block(string detail)
        {
            Failure = detail.Length > 1500 ? detail.Substring(0, 1500) + " [truncated]" : detail;
            Stage = "failed";
            UnityEngine.Debug.LogError("[Quest startup] real mod lifecycle blocked: " + Failure);
        }
        static Exception RootException(Exception error) { return error is TargetInvocationException && error.InnerException != null ? error.InnerException : error; }
        // The real plugin owns its lifecycle. Do not destroy it or stop Unity XR
        // when the bootstrap observes failure; retained logs remain collectable.
    }
}
#endif
