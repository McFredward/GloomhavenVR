#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloomhavenVR.Quest
{
    /// <summary>Temporary XR presentation of original menu objects and callbacks. Not the VR mod.</summary>
    public sealed class QuestGameMenu : MonoBehaviour
    {
        Transform origin;
        Camera view;
        QuestProbePoseInput head, aim, leftGrip, rightGrip;
        Transform leftMarker, rightMarker;
        LineRenderer aimLine;
        Material markerMaterial;
        InputAction trigger, leftStick, rightStick, primary;
        readonly QuestProbeLocomotion navigation = new QuestProbeLocomotion();
        readonly List<Canvas> canvases = new List<Canvas>();
        readonly List<Component> excluded = new List<Component>();
        readonly Dictionary<Component, string> excludedText = new Dictionary<Component, string>();
        readonly List<Camera> originalCameras = new List<Camera>();
        readonly List<AudioListener> originalListeners = new List<AudioListener>();
        readonly HashSet<Camera> reportedCameras = new HashSet<Camera>();
        QuestGamePointer pointer;
        QuestGameKeyboard keyboard;
        Text tooltip;
        Text startupStatus;
        QuestGameBootstrap startup;
        string lastStatus, lastFailure;
        bool statusGerman;
        bool focused = true, paused;
        readonly QuestGameButtonGate mrButton = new QuestGameButtonGate();
        bool mrRequested, lastMrActive;
        string mrError;
        Text mrStatus;
        float nextScan;
        bool originConfigured;

        void Awake()
        {
            origin = new GameObject("Quest original-menu tracking origin").transform; origin.SetParent(transform, false);
            view = new GameObject("Quest original-menu head").AddComponent<Camera>(); view.transform.SetParent(origin, false);
            view.transform.localPosition = new Vector3(0, 1.6f, 0);
            view.nearClipPlane = .02f; view.farClipPlane = 250; view.tag = "MainCamera";
            view.depth = 100; view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.025f, .035f, .045f, 1);
            view.gameObject.AddComponent<AudioListener>();
            head = new QuestProbePoseInput("Startup head", "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation", "<XRHMD>/isTracked", "<XRHMD>/trackingState");
            aim = QuestProbePoseInput.ForController("Startup UI aim", "<XRController>{RightHand}/", true);
            leftGrip = QuestProbePoseInput.ForController("Startup left grip", "<XRController>{LeftHand}/", false);
            rightGrip = QuestProbePoseInput.ForController("Startup right grip", "<XRController>{RightHand}/", false);
            Material source = Resources.Load<Material>("quest-albedo-material");
            markerMaterial = source != null ? new Material(source) : new Material(Shader.Find("Sprites/Default"));
            if (markerMaterial.HasProperty("_Color")) markerMaterial.SetColor("_Color", Color.cyan);
            leftMarker = Marker("Startup left grip marker"); rightMarker = Marker("Startup right grip marker");
            aimLine = new GameObject("Startup right aim ray").AddComponent<LineRenderer>(); aimLine.transform.SetParent(transform, false);
            aimLine.sharedMaterial = markerMaterial; aimLine.positionCount = 2; aimLine.startWidth = .0025f; aimLine.endWidth = .001f; aimLine.useWorldSpace = true;
            trigger = QuestProbePoseInput.Create("Startup UI trigger", "<XRController>{RightHand}/trigger");
            leftStick = QuestProbePoseInput.Create("Startup move", "<XRController>{LeftHand}/thumbstick");
            rightStick = QuestProbePoseInput.Create("Startup turn/height", "<XRController>{RightHand}/thumbstick");
            primary = QuestProbePoseInput.Create("Startup mixed reality", "<XRController>{RightHand}/primaryButton");
            var ui = new GameObject("Quest excluded-entry tooltip", typeof(Canvas)); ui.transform.SetParent(transform, false);
            var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = view; canvas.sortingOrder = 32760;
            var rect = (RectTransform)ui.transform; rect.sizeDelta = new Vector2(800, 80); rect.localScale = Vector3.one * .001f; rect.position = new Vector3(0, 1.1f, 1.65f);
            var label = new GameObject("Tooltip", typeof(RectTransform), typeof(Text)); label.transform.SetParent(ui.transform, false);
            var labelRect = (RectTransform)label.transform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            tooltip = label.GetComponent<Text>(); tooltip.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); tooltip.fontSize = 28; tooltip.alignment = TextAnchor.MiddleCenter; tooltip.color = Color.white; tooltip.raycastTarget = false;
            startup = GetComponent<QuestGameBootstrap>();
            keyboard = gameObject.GetComponent<QuestGameKeyboard>() ?? gameObject.AddComponent<QuestGameKeyboard>();
            var status = new GameObject("Quest original-startup status", typeof(Canvas)); status.transform.SetParent(transform, false);
            var statusCanvas = status.GetComponent<Canvas>(); statusCanvas.renderMode = RenderMode.WorldSpace; statusCanvas.worldCamera = view; statusCanvas.sortingOrder = 32760;
            var statusRect = (RectTransform)status.transform; statusRect.sizeDelta = new Vector2(1000, 400); statusRect.localScale = Vector3.one * .001f; statusRect.position = new Vector3(0, 1.5f, 1.7f);
            var statusLabel = new GameObject("Startup status", typeof(RectTransform), typeof(Text)); statusLabel.transform.SetParent(status.transform, false);
            var statusLabelRect = (RectTransform)statusLabel.transform; statusLabelRect.anchorMin = Vector2.zero; statusLabelRect.anchorMax = Vector2.one; statusLabelRect.offsetMin = statusLabelRect.offsetMax = Vector2.zero;
            startupStatus = statusLabel.GetComponent<Text>(); startupStatus.font = tooltip.font; startupStatus.fontSize = 30; startupStatus.alignment = TextAnchor.MiddleCenter; startupStatus.color = Color.white; startupStatus.raycastTarget = false;
            var mrLabel = new GameObject("Startup mixed-reality status", typeof(RectTransform), typeof(Text)); mrLabel.transform.SetParent(ui.transform, false);
            var mrRect = (RectTransform)mrLabel.transform; mrRect.anchorMin = mrRect.anchorMax = new Vector2(.5f, 0); mrRect.anchoredPosition = new Vector2(0, -65); mrRect.sizeDelta = new Vector2(1000, 80);
            mrStatus = mrLabel.GetComponent<Text>(); mrStatus.font = tooltip.font; mrStatus.fontSize = 24; mrStatus.alignment = TextAnchor.MiddleCenter; mrStatus.color = Color.white; mrStatus.raycastTarget = false;
            RefreshMrStatus();
            RefreshStatus();
            SceneManager.sceneLoaded += SceneLoaded;
            Camera.onPreCull += BeforeCameraRender;
            Debug.LogWarning("[Quest startup] presentation=original-menu-diagnostic; original widgets/callbacks retained. Full mod, campaign and visual parity remain unverified.");
        }
        void Update()
        {
            bool valid = head.TryRead(out Vector3 headPosition, out Quaternion headRotation);
            RefreshStatus();
            if (valid) { view.transform.localPosition = headPosition; view.transform.localRotation = headRotation; }
            Vector3 aimPosition = Vector3.zero; Quaternion aimRotation = Quaternion.identity;
            bool pointerValid = focused && !paused && valid && aim.TryRead(out aimPosition, out aimRotation);
            Grip(leftGrip, leftMarker); bool rightTracked = Grip(rightGrip, rightMarker);
            UpdateMixedReality(focused && !paused && valid && rightTracked);
            aimLine.enabled = pointerValid;
            if (pointerValid)
            {
                Vector3 start = origin.TransformPoint(aimPosition), direction = origin.TransformDirection(aimRotation * Vector3.forward);
                var menuPlane = new Plane(Vector3.back, new Vector3(0, 1.4f, 1.85f)); float length = 3;
                if (menuPlane.Raycast(new Ray(start, direction), out float hit) && hit > 0 && hit < 20) length = hit;
                aimLine.SetPosition(0, start); aimLine.SetPosition(1, start + direction * length);
            }
            if (pointer != null)
            {
                pointer.inputValid = pointerValid; pointer.pressed = trigger.ReadValue<float>() > .55f;
                if (pointerValid) { pointer.ray = new Ray(origin.TransformPoint(aimPosition), origin.TransformDirection(aimRotation * Vector3.forward)); pointer.view = view; }
            }
            Vector2 right = rightStick.ReadValue<Vector2>();
            navigation.Step(origin, view.transform, leftStick.ReadValue<Vector2>(), right.x, right.y, Time.unscaledDeltaTime, focused && !paused && valid);
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1; Scan(); }
        }
        Transform Marker(string name)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Capsule); marker.name = name;
            marker.transform.SetParent(origin, false); marker.transform.localScale = new Vector3(.035f, .06f, .035f);
            Destroy(marker.GetComponent<Collider>()); marker.GetComponent<Renderer>().sharedMaterial = markerMaterial; return marker.transform;
        }
        bool Grip(QuestProbePoseInput input, Transform marker)
        {
            Vector3 position = Vector3.zero; Quaternion rotation = Quaternion.identity;
            bool tracked = focused && !paused && input.TryRead(out position, out rotation);
            marker.gameObject.SetActive(tracked);
            if (tracked) { marker.localPosition = position; marker.localRotation = rotation; }
            return tracked;
        }
        void UpdateMixedReality(bool inputValid)
        {
            bool pressed = primary.ReadValue<float>() > .5f;
            if (mrButton.Step(inputValid, pressed))
            {
                mrRequested = !mrRequested; mrError = null;
                if (!QuestPassthroughFeature.SetEnabled(mrRequested) && mrRequested) { mrRequested = false; mrError = "mrFailed"; }
                Debug.Log("[Quest startup] mixed reality requested=" + mrRequested + " active=" + QuestPassthroughFeature.Active + " available=" + QuestPassthroughFeature.Available);
                RefreshMrStatus();
            }
            bool active = QuestPassthroughFeature.Active;
            if (active != lastMrActive)
            {
                lastMrActive = active;
                // A native underlay is visible only through transparent clear pixels.
                // Keep ordinary VR opaque when the native feature is unavailable/stopped.
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = active ? Color.clear : new Color(.025f, .035f, .045f, 1);
                Debug.Log("[Quest startup] tracked-camera passthrough active=" + active);
                RefreshMrStatus();
            }
        }
        void RefreshMrStatus()
        {
            if (mrStatus == null) return;
            bool german = Application.systemLanguage == SystemLanguage.German;
            mrStatus.text = QuestText.Get("mr", german) + "\n" + QuestText.Get(mrError ?? (QuestPassthroughFeature.Active ? "mrActive" : "vrActive"), german);
        }
        void LateUpdate()
        {
            EnforceViewOwnership();
            foreach (Component entry in excluded)
            {
                if (entry == null) continue;
                if (entry is Selectable selectable) { selectable.interactable = false; continue; }
                PropertyInfo property = entry.GetType().GetProperty("IsInteractable");
                if (property != null && property.CanWrite && (bool)property.GetValue(entry)) property.SetValue(entry, false);
            }
        }
        void Scan()
        {
            ScanCameras();
            if (!originConfigured)
            {
                var inputs = new List<XRInputSubsystem>(); SubsystemManager.GetInstances(inputs);
                foreach (XRInputSubsystem input in inputs)
                    if (input.running)
                    {
                        if ((input.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0 && input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                            Debug.Log("[Quest startup] original-menu origin=Floor");
                        else { origin.position = new Vector3(0, 1.6f, 0); Debug.LogWarning("[Quest startup] original-menu origin=Device with initial eye-height offset=1.6m"); }
                        originConfigured = true; break;
                    }
            }
            EventSystem events = EventSystem.current;
            if (events != null)
            {
                if (pointer == null || pointer.gameObject != events.gameObject) pointer = events.gameObject.GetComponent<QuestGamePointer>() ?? events.gameObject.AddComponent<QuestGamePointer>();
                pointer.owner = this;
                foreach (BaseInputModule module in events.GetComponents<BaseInputModule>()) if (module != pointer) module.enabled = false;
            }
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (!canvas.gameObject.scene.IsValid() || !canvas.gameObject.scene.isLoaded || canvas.transform.IsChildOf(transform) || !canvas.isRootCanvas || canvases.Contains(canvas)) continue;
                if (canvas.renderMode == RenderMode.WorldSpace) continue;
                RectTransform rect = (RectTransform)canvas.transform;
                float width = Mathf.Max(1, rect.rect.width);
                canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = view;
                rect.position = new Vector3(0, 1.4f, 1.85f); rect.rotation = Quaternion.identity; rect.localScale = Vector3.one * (1.8f / width);
                var raycaster = canvas.GetComponent<GraphicRaycaster>(); if (raycaster != null) raycaster.ignoreReversedGraphics = false;
                canvases.Add(canvas);
                Debug.Log("[Quest startup] original canvas presented=" + canvas.name + " width=" + width + " sorting=" + canvas.sortingOrder);
            }
            // Bind the exact serialized native Guildmaster entry, not localized text/name heuristics.
            foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null || !behaviour.gameObject.scene.IsValid()) continue;
                bool voice = behaviour.GetType().FullName == "VoiceChat.VoceChatOptions";
                if (!voice && behaviour.GetType().FullName != "GLOOM.MainMenu.UIMainOptionsMenu") continue;
                FieldInfo field = behaviour.GetType().GetField(voice ? "_switchChatButton" : "guildmasterButton", BindingFlags.NonPublic | BindingFlags.Instance);
                object entry = field != null ? field.GetValue(behaviour) : null;
                object button = voice ? entry : (entry != null ? entry.GetType().GetProperty("Button").GetValue(entry) : null);
                if (button is Component component && !excluded.Contains(component))
                {
                    excluded.Add(component);
                    string key = voice ? "startupVoiceUnavailable" : "excluded"; excludedText[component] = key;
                    if (component is Selectable selectable) selectable.interactable = false;
                    MethodInfo setTooltip = component.GetType().GetMethod("SetTooltip");
                    if (setTooltip != null) setTooltip.Invoke(component, new object[] { true, QuestText.Get(key, Application.systemLanguage == SystemLanguage.German) });
                    Debug.Log(voice ? "[Quest startup] original voice opt-in button retained and excluded for this startup diagnostic; voiceNativeAvailable=false."
                        : "[Quest startup] original Guildmaster entry retained and excluded.");
                }
            }
        }
        void RefreshStatus()
        {
            if (startup == null || startupStatus == null) return;
            bool visible = !startup.OriginalBootstrapStarted || startup.State == "failed";
            startupStatus.transform.parent.gameObject.SetActive(visible);
            if (!visible) return;
            bool german = Application.systemLanguage == SystemLanguage.German;
            if (lastStatus == startup.State && lastFailure == startup.FailureDetail && german == statusGerman) return;
            lastStatus = startup.State; lastFailure = startup.FailureDetail; statusGerman = german;
            string key;
            switch (startup.State)
            {
                case "checking-content": key = "startupCheckingContent"; break;
                case "extracting-content": key = "startupExtractingContent"; break;
                case "initializing-native-addressables": key = "startupAddressables"; break;
                case "loading-original-bootstrap": key = "startupLoadingOriginal"; break;
                case "original-bootstrap-loaded": key = "startupOriginalLoaded"; break;
                case "failed": key = "startupFailed"; break;
                default: key = "startupPending"; break;
            }
            startupStatus.text = QuestText.Get("startupTitle", german) + "\n\n" + QuestText.Get(key, german)
                + (string.IsNullOrEmpty(lastFailure) ? "" : "\n" + lastFailure) + "\n\n" + QuestText.Get("startupDiagnosticScope", german);
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode) { ScanCameras(); }
        void ScanCameras()
        {
            originalCameras.RemoveAll(camera => camera == null); originalListeners.RemoveAll(listener => listener == null);
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == view || !camera.gameObject.scene.IsValid() || !camera.gameObject.scene.isLoaded) continue;
                if (!originalCameras.Contains(camera)) originalCameras.Add(camera);
            }
            foreach (AudioListener listener in Resources.FindObjectsOfTypeAll<AudioListener>())
                if (!listener.transform.IsChildOf(view.transform) && listener.gameObject.scene.IsValid() && listener.gameObject.scene.isLoaded && !originalListeners.Contains(listener)) originalListeners.Add(listener);
            EnforceViewOwnership();
        }
        void EnforceViewOwnership()
        {
            foreach (Camera camera in originalCameras)
            {
                if (camera == null) continue;
                // Preserve native RenderTexture rendering (portraits/movie/UI helpers).
                // Display cameras retain their GameObjects/scripts and serialized refs,
                // but only this diagnostic tracked camera may render the headset view.
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                if (camera.targetTexture == null && camera.targetDisplay == 0)
                {
                    camera.enabled = false;
                    if (camera.CompareTag("MainCamera")) camera.tag = "Untagged";
                    if (reportedCameras.Add(camera)) Debug.Log("[Quest startup] original display camera retained with rendering disabled=" + camera.name + "; tracked diagnostic camera owns XR view.");
                }
            }
            foreach (AudioListener listener in originalListeners) if (listener != null) listener.enabled = false;
        }
        void BeforeCameraRender(Camera camera)
        {
            if (camera != view && camera.targetTexture == null && camera.targetDisplay == 0 && camera.gameObject.scene.IsValid() && camera.gameObject.scene.isLoaded)
            {
                camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
                if (camera.CompareTag("MainCamera")) camera.tag = "Untagged";
            }
        }
        internal bool Excluded(GameObject hit)
        {
            foreach (Component entry in excluded) if (entry != null && hit.transform.IsChildOf(entry.transform)) return true;
            foreach (MonoBehaviour row in hit.GetComponentsInParent<MonoBehaviour>())
            {
                if (row == null) continue;
                foreach (FieldInfo field in row.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType.FullName != "GLOOM.MainMenu.IMenuSuboption" && field.FieldType.FullName != "GLOOM.MainMenu.MenuSuboption") continue;
                    object option = field.GetValue(row);
                    if (option != null && option.GetType().GetProperty("NameLocKey").GetValue(option) as string == "GUI_MODDING") return true;
                }
            }
            return false;
        }
        internal void Hover(GameObject hit)
        {
            string key = null;
            if (hit != null)
            {
                foreach (Component entry in excluded)
                    if (entry != null && hit.transform.IsChildOf(entry.transform)) { key = excludedText[entry]; break; }
                if (key == null && Excluded(hit)) key = "excluded";
            }
            tooltip.text = key != null ? QuestText.Get(key, Application.systemLanguage == SystemLanguage.German) : "";
        }
        internal void BeforeOriginalClick(GameObject target) { if (keyboard != null) keyboard.BeforeOriginalClick(target); }
        internal void AfterOriginalClick(GameObject target) { if (keyboard != null) keyboard.AfterOriginalClick(target); }
        void OnApplicationFocus(bool value) { focused = value; if (!value) { navigation.Suspend(); mrButton.Reset(); } }
        void OnApplicationPause(bool value) { paused = value; if (value) { navigation.Suspend(); mrButton.Reset(); } }
        void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; Camera.onPreCull -= BeforeCameraRender; head.Dispose(); aim.Dispose(); leftGrip.Dispose(); rightGrip.Dispose(); trigger.Dispose(); leftStick.Dispose(); rightStick.Dispose(); primary.Dispose(); if (markerMaterial != null) Destroy(markerMaterial); }
    }

    /// <summary>Delivers native Unity pointer events; never calls a game action directly.</summary>
    public sealed class QuestGamePointer : BaseInputModule
    {
        internal QuestGameMenu owner;
        internal bool inputValid, pressed;
        internal Ray ray;
        internal Camera view;
        PointerEventData data;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        bool previous, neutral;
        public override bool ShouldActivateModule() { return enabled; }
        public override void Process()
        {
            if (data == null) data = new PointerEventData(eventSystem) { pointerId = -610, button = PointerEventData.InputButton.Left };
            if (!inputValid || view == null)
            {
                Release(false); HandlePointerExitAndEnter(data, null); previous = false; neutral = false; if (owner != null) owner.Hover(null); return;
            }
            if (!neutral) { if (!pressed) neutral = true; else return; }
            // Project the real aim ray onto the fixed menu plane, then let original
            // GraphicRaycasters determine order, interactability and target objects.
            var plane = new Plane(Vector3.back, new Vector3(0, 1.4f, 1.85f));
            GameObject hit = null;
            if (plane.Raycast(ray, out float distance) && distance > 0 && distance < 20)
            {
                data.position = view.WorldToScreenPoint(ray.GetPoint(distance)); hits.Clear(); eventSystem.RaycastAll(data, hits);
                foreach (RaycastResult result in hits)
                    if (result.module is GraphicRaycaster) { data.pointerCurrentRaycast = result; hit = result.gameObject; break; }
            }
            HandlePointerExitAndEnter(data, hit); if (owner != null) owner.Hover(hit);
            bool excluded = hit != null && owner != null && owner.Excluded(hit);
            if (pressed && !previous && hit != null && !excluded)
            {
                data.pressPosition = data.position; data.pointerPressRaycast = data.pointerCurrentRaycast;
                data.pointerPress = ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
                data.rawPointerPress = hit; data.eligibleForClick = true;
                data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(hit);
                if (data.pointerDrag != null) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
            }
            if (pressed && data.pointerDrag != null)
            {
                if (!data.dragging && (data.position - data.pressPosition).sqrMagnitude >= eventSystem.pixelDragThreshold * eventSystem.pixelDragThreshold)
                { ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler); data.dragging = true; }
                if (data.dragging) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
            }
            if (!pressed && previous) Release(!excluded);
            previous = pressed;
        }
        void Release(bool click)
        {
            if (data == null) return;
            if (data.pointerPress != null)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
                GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(data.pointerEnter);
                if (click && data.eligibleForClick && !data.dragging && data.pointerPress == target)
                {
                    if (owner != null) owner.BeforeOriginalClick(target);
                    ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
                    if (owner != null) owner.AfterOriginalClick(target);
                }
            }
            if (data.dragging && data.pointerDrag != null) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            data.pointerPress = data.rawPointerPress = data.pointerDrag = null; data.eligibleForClick = data.dragging = false;
        }
        public override void DeactivateModule() { Release(false); if (data != null) HandlePointerExitAndEnter(data, null); base.DeactivateModule(); }
    }
}
#endif
