#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;
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
        [SerializeField] Canvas loadingCanvas;
        [SerializeField] Text loadingLabel;
        string lastLoadingText;
        public bool StartupViewAvailable { get { return loadingCanvas != null && loadingCanvas.gameObject.activeInHierarchy
            && loadingCanvas.worldCamera != null && loadingCanvas.worldCamera.isActiveAndEnabled; } }
        string lastObservation;
        bool activated;
        bool deliveryViewRequested;

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
            // This camera renders only its temporary label; the plugin adopts
            // the same neutral reference once its verified assets are available.
            startupAnchor.cullingMask = 1 << 31;
            startupAnchor.clearFlags = CameraClearFlags.SolidColor;
            startupAnchor.backgroundColor = new Color(.025f, .03f, .045f, 1f);
            startupAnchor.stereoTargetEye = StereoTargetEyeMask.Both;
            startupAnchor.depth = -100;
            startupAnchor.enabled = true;
            GameObject panel = new GameObject("Quest temporary loading view", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            panel.layer = 31;
            panel.transform.SetParent(anchor.transform, false);
            loadingCanvas = panel.GetComponent<Canvas>();
            loadingCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            loadingCanvas.worldCamera = startupAnchor;
            loadingCanvas.planeDistance = 1f;
            CanvasScaler scaler = panel.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            GameObject label = new GameObject("Quest loading status", typeof(RectTransform), typeof(Text));
            label.layer = 31;
            label.transform.SetParent(panel.transform, false);
            RectTransform rectangle = label.GetComponent<RectTransform>();
            rectangle.anchorMin = new Vector2(.15f, .25f); rectangle.anchorMax = new Vector2(.85f, .75f);
            rectangle.offsetMin = rectangle.offsetMax = Vector2.zero;
            loadingLabel = label.GetComponent<Text>();
            loadingLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (loadingLabel.font == null) throw new InvalidDataException("The serialized startup font is unavailable.");
            loadingLabel.fontSize = 40;
            loadingLabel.alignment = TextAnchor.MiddleCenter;
            loadingLabel.color = Color.white;
            loadingLabel.raycastTarget = false;
            // No GraphicRaycaster, input actions, buttons, hands or pointer are
            // introduced here. Remove this canvas before original mod creation.
            UpdateStartupView("pending", null);
            UnityEngine.Debug.Log("[Quest startup] temporary stereo loading view available before content delivery; neutral anchor retained for original VR rig.");
        }

        public void UpdateStartupView(string state, QuestGameContentProgress progress)
        {
            if (deliveryViewRequested && loadingLabel == null) BeginDeliveryView();
            if (loadingLabel == null) return;
            bool german = Application.systemLanguage == SystemLanguage.German;
            string key = state == "failed" ? "startupFailed"
                : state.StartsWith("checking-mod-", StringComparison.Ordinal) ? "startupCheckingMod"
                : state.StartsWith("copying-mod-", StringComparison.Ordinal) ? "startupCopyingMod"
                : state.StartsWith("extracting-mod-", StringComparison.Ordinal) ? "startupExtractingMod"
                : state == "starting-real-mod" ? "startupStartingMod"
                : state == "checking-content" || state == "copying-content" ? "startupCheckingContent"
                : state == "extracting-content" ? "startupExtractingContent"
                : state == "initializing-native-addressables" ? "startupAddressables" : "startupPending";
            string value = QuestText.Get("title", german) + "\n\n" + QuestText.Get(key, german);
            if (progress != null && progress.TotalBytes > 0)
                value += "\n" + (progress.ProcessedBytes / 1048576f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    + " / " + (progress.TotalBytes / 1048576f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MiB";
            if (value != lastLoadingText) { lastLoadingText = value; loadingLabel.text = value; }
        }

        void StopLoadingView()
        {
            if (loadingCanvas != null) { loadingCanvas.gameObject.SetActive(false); Destroy(loadingCanvas.gameObject); }
            loadingCanvas = null; loadingLabel = null;
            if (startupAnchor != null) { startupAnchor.cullingMask = 0; startupAnchor.stereoTargetEye = StereoTargetEyeMask.None; }
        }

        public void BeginDeliveryView()
        {
            deliveryViewRequested = true;
            if (loadingCanvas != null) return;
            var head = QuestStandalonePlatform.HeadCamera;
            if (head == null) throw new InvalidOperationException("Original content delivery requires the observed real VR camera.");
            // The large original menu movies need worker verification too. Reuse
            // the existing rig for this temporary, noninteractive status canvas;
            // never add a second XR camera or a separate input/controller path.
            var panel = new GameObject("Quest original content status", typeof(RectTransform), typeof(Canvas));
            panel.layer = QuestStandalonePlatform.PresentationLayer;
            panel.transform.SetParent(head.transform, false);
            panel.transform.localPosition = new Vector3(0, -.05f, 1.5f);
            panel.transform.localScale = Vector3.one * .001f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1300, 260);
            loadingCanvas = panel.GetComponent<Canvas>();
            loadingCanvas.renderMode = RenderMode.WorldSpace;
            loadingCanvas.worldCamera = head;
            var label = new GameObject("Quest original content progress", typeof(RectTransform), typeof(Text));
            label.layer = panel.layer; label.transform.SetParent(panel.transform, false);
            var rectangle = label.GetComponent<RectTransform>();
            rectangle.anchorMin = Vector2.zero; rectangle.anchorMax = Vector2.one;
            rectangle.offsetMin = rectangle.offsetMax = Vector2.zero;
            loadingLabel = label.GetComponent<Text>();
            loadingLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (loadingLabel.font == null) throw new InvalidDataException("Original delivery font is unavailable.");
            loadingLabel.fontSize = 40; loadingLabel.alignment = TextAnchor.MiddleCenter;
            loadingLabel.color = Color.white; loadingLabel.raycastTarget = false;
            lastLoadingText = null;
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
