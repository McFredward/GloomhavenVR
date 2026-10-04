#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
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
        int viewPercent;
        QuestLoadingView renderedView;
        string renderedState;
        int renderedPercent;
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
            RetargetLoadingView();
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
            startupAnchor.cullingMask = 0;
            startupAnchor.clearFlags = CameraClearFlags.SolidColor;
            startupAnchor.backgroundColor = new Color(.025f, .03f, .045f, 1f);
            startupAnchor.stereoTargetEye = StereoTargetEyeMask.Both;
            startupAnchor.depth = -100;
            startupAnchor.enabled = true;
            // The warm path submits neutral XR frames but creates no loading
            // canvas. Actual copy/extraction or an explicit failure owns artwork.
            UnityEngine.Debug.Log("[Quest startup] neutral stereo anchor available before content work; no loading artwork requested.");
        }

        public void UpdateStartupView(string state, int overallPercent)
        {
            viewState = state;
            viewPercent = overallPercent;
            if (state == "failed" && !deliveryViewRequested) BeginDeliveryView();
            // A genuine XR owner destruction can also destroy its child canvas.
            // Recreate only that lost object when an observed head returns;
            // ordinary anchor-to-rig handover always retains the same artwork.
            if (deliveryViewRequested && loadingView == null && QuestStandalonePlatform.HeadCamera != null) BeginDeliveryView();
            if (deliveryViewRequested) RetargetLoadingView();
            if (loadingView == null) return;
            bool german = Application.systemLanguage == SystemLanguage.German;
            if (loadingView == renderedView && state == renderedState && overallPercent == renderedPercent && german == renderedGerman) return;
            renderedView = loadingView; renderedState = state; renderedPercent = overallPercent; renderedGerman = german;
            // Keep phase names, paths and file counts out of the product flow.
            // This one global bar continues across content and native startup.
            loadingView.UpdateOverall(QuestText.Get(state == "failed" ? "startupFailed" : "loadingPreparing", german),
                overallPercent, state == "original-bootstrap-loaded");
        }

        void RetargetLoadingView()
        {
            if (loadingView == null) return;
            var head = QuestStandalonePlatform.HeadCamera;
            if (head == null || loadingView.transform.parent == head.transform) return;
            loadingView.Retarget(head, QuestStandalonePlatform.PresentationLayer);
            if (startupAnchor != null) { startupAnchor.cullingMask = 0; startupAnchor.stereoTargetEye = StereoTargetEyeMask.None; }
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
            if (loadingView == null)
            {
                var owner = QuestStandalonePlatform.HeadCamera ?? startupAnchor;
                if (owner == null) throw new InvalidOperationException("Content installation requires its startup or observed real VR camera.");
                bool temporary = owner == startupAnchor;
                if (temporary) startupAnchor.cullingMask = 1 << 31;
                loadingView = QuestLoadingView.Create(owner, temporary ? 31 : QuestStandalonePlatform.PresentationLayer);
            }
            RetargetLoadingView();
            UpdateStartupView(viewState, viewPercent);
        }
        public void EndDeliveryView() { deliveryViewRequested = false; StopLoadingView(); }

        void RetireStartupAnchor()
        {
            if (startupAnchor == null) return;
            // Do not destroy the current canvas parent until it has moved to
            // the observed real head. There is only one artwork owner throughout.
            if (loadingView != null && loadingView.transform.parent == startupAnchor.transform) return;
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
