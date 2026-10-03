using System;
using System.Globalization;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace GloomhavenVR.Quest
{
    /// <summary>Explicitly diagnostic hardware scene, never presented as the recovered game.</summary>
    public sealed class QuestHardwareProbe : MonoBehaviour
    {
        [Serializable] sealed class Profile
        {
            public int schema;
            public string provider, steamId, displayName, source;
            public uint accountId;
            public bool isDummy;
        }
        [Serializable] sealed class StorageRecord
        {
            public int schema = 1;
            public string steamId, marker;
            public int writes;
        }
        [Serializable] sealed class BuildStamp { public int schema, modBuild; public string inputKey; }
        sealed class Controller
        {
            public XRNode node;
            public Transform pose;
            public LineRenderer ray;
            public bool primary, secondary, trigger, tracked, aimTracked, buttonsReady;
            public Vector3 aimPosition, aimDirection;
            public QuestProbePoseInput gripInput, aimInput;
            public InputAction primaryAction, secondaryAction, triggerAction, stick;
        }
        Camera view;
        Transform stage, rig;
        QuestProbeDiagnostics diagnostics;
        BuildStamp stamp;
        readonly QuestProbeLocomotion locomotion = new QuestProbeLocomotion();
        Controller left, right;
        Controller[] controllers;
        Profile profile;
        Text instructions, tooltip, status, performance, modelStatus, profileStatus, buildStatus, trackingStatus;
        GameObject model;
        bool german, mrRequested, storagePassed;
        bool lastActive;
        string saveStatus = "saveMissing";
        string savePath;
        float nextRefresh, elapsed;
        int frames;
        float fps;
        Font font;
        string logPath;
        string mrError;
        QuestProbePoseInput headInput;
        bool headTracked, focused = true, paused;
        string originMode = "unavailable";
        bool floorConfigured;
        float nextOriginCheck;

        void Awake()
        {
            german = Application.systemLanguage == SystemLanguage.German;
            Application.targetFrameRate = 72;
            QualitySettings.vSyncCount = 0;
            logPath = Path.Combine(Application.persistentDataPath, "quest-hardware.log");
            Application.logMessageReceived += CaptureLog;
            Debug.Log("[GloomhavenVR Quest] target=probe schema=1 Unity=" + Application.unityVersion +
                " version=" + Application.version + " platform=" + Application.platform +
                " device=" + SystemInfo.deviceModel + " graphics=" + SystemInfo.graphicsDeviceName +
                " storage=" + Application.persistentDataPath);
            var stampAsset = Resources.Load<TextAsset>("quest-build");
            if (stampAsset == null) throw new InvalidOperationException("Quest build provenance is missing");
            stamp = JsonUtility.FromJson<BuildStamp>(stampAsset.text);
            Debug.Log("[GloomhavenVR Quest] ModBuild=" + stamp.modBuild + " input=" + stamp.inputKey);
            var asset = Resources.Load<TextAsset>("quest-profile");
            if (asset != null) profile = JsonUtility.FromJson<Profile>(asset.text);
            if (!ValidProfile(profile)) throw new InvalidOperationException(QuestText.Get("profileMissing", german));
            Debug.Log("[GloomhavenVR Quest] profile provider=" + profile.provider + " dummy=" + profile.isDummy + " source=" + profile.source);
            savePath = Path.Combine(Application.persistentDataPath, "quest-hardware-storage.json");
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildRig();
            BuildStage();
            Application.onBeforeRender += BeforeRender;
            ReadStorage();
            RefreshText();
        }
        static bool ValidProfile(Profile value)
        {
            if (value == null || value.schema != 1 || value.provider != "steam" || string.IsNullOrWhiteSpace(value.displayName)) return false;
            ulong id;
            if (!ulong.TryParse(value.steamId, NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;
            if (value.isDummy) return id == 0 && value.accountId == 0 && value.displayName.IndexOf("DUMMY", StringComparison.OrdinalIgnoreCase) >= 0;
            return id > uint.MaxValue && (uint)id == value.accountId;
        }
        void BuildRig()
        {
            rig = new GameObject("Quest tracking origin").transform;
            view = new GameObject("Tracked head").AddComponent<Camera>();
            view.tag = "MainCamera";
            view.transform.SetParent(rig, false);
            view.transform.localPosition = new Vector3(0, 1.6f, 0);
            view.nearClipPlane = .03f;
            view.farClipPlane = 30;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.018f, .025f, .045f, 1);
            view.gameObject.AddComponent<AudioListener>();
            headInput = new QuestProbePoseInput("Head", "<XRHMD>/centerEyePosition",
                "<XRHMD>/centerEyeRotation", "<XRHMD>/isTracked", "<XRHMD>/trackingState");
            left = MakeController(XRNode.LeftHand, rig, new Color(.2f, .7f, 1));
            right = MakeController(XRNode.RightHand, rig, new Color(1, .7f, .2f));
            controllers = new[] { left, right };
            var light = new GameObject("Diagnostic light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
            foreach (var subsystem in GetDisplays())
                Debug.Log("[GloomhavenVR Quest] XR display running=" + subsystem.running);
        }
        static System.Collections.Generic.List<XRDisplaySubsystem> GetDisplays()
        {
            var list = new System.Collections.Generic.List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(list);
            return list;
        }
        Controller MakeController(XRNode node, Transform parent, Color color)
        {
            var pose = new GameObject(node.ToString()).transform;
            pose.SetParent(parent, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Diagnostic controller marker";
            body.transform.SetParent(pose, false);
            body.transform.localScale = new Vector3(.035f, .06f, .035f);
            Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().material = Material(color);
            var ray = new GameObject("Controller pointing ray").AddComponent<LineRenderer>();
            ray.transform.SetParent(pose, false);
            ray.positionCount = 2;
            ray.startWidth = .0025f;
            ray.endWidth = .001f;
            ray.material = Material(color, true);
            string prefix = "<XRController>{" + (node == XRNode.LeftHand ? "LeftHand" : "RightHand") + "}/";
            return new Controller
            {
                node = node, pose = pose, ray = ray,
                gripInput = QuestProbePoseInput.ForController(node + " grip", prefix, false),
                aimInput = QuestProbePoseInput.ForController(node + " aim", prefix, true),
                triggerAction = Action(node + " trigger", prefix + "trigger"),
                stick = Action(node + " stick", prefix + "thumbstick"),
                primaryAction = Action(node + " primary", prefix + "primaryButton"),
                secondaryAction = Action(node + " secondary", prefix + "secondaryButton")
            };
        }
        static InputAction Action(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Value, binding);
            action.Enable();
            return action;
        }
        static Material Material(Color color, bool unlit = false)
        {
            var template = Resources.Load<Material>(unlit ? "quest-ray-material" : "quest-surface-material");
            if (template == null || template.shader == null)
                throw new InvalidOperationException("Diagnostic material resource missing");
            return new Material(template) { color = color };
        }
        void BuildStage()
        {
            stage = new GameObject("Placeable diagnostic table and UI").transform;
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Diagnostic tabletop";
            board.transform.SetParent(stage, false);
            board.transform.localPosition = new Vector3(0, .8f, 1.3f);
            board.transform.localScale = new Vector3(.85f, .035f, .55f);
            board.GetComponent<Renderer>().material = Material(new Color(.13f, .10f, .075f));
            var prefab = Resources.Load<GameObject>("quest-original-model");
            if (prefab != null)
            {
                model = Instantiate(prefab, stage);
                model.name = "Recovered original Bandit Guard (diagnostic)";
                model.transform.localPosition = new Vector3(0, .83f, 1.3f);
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var item in renderers) bounds.Encapsulate(item.bounds);
                    if (bounds.size.y > .0001f) model.transform.localScale *= .25f / bounds.size.y;
                    bounds = renderers[0].bounds;
                    foreach (var item in renderers) bounds.Encapsulate(item.bounds);
                    model.transform.position += Vector3.up * (.83f - bounds.min.y);
                }
                Debug.Log("[GloomhavenVR Quest] recovered original model loaded renderers=" + renderers.Length);
            }
            var canvas = new GameObject("Hardware diagnostic panel").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.transform.SetParent(stage, false);
            canvas.transform.localPosition = new Vector3(.35f, 1.65f, 1.95f);
            canvas.transform.localScale = Vector3.one * .001f;
            var rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1100, 1000);
            var background = new GameObject("UI backing").AddComponent<Image>();
            background.transform.SetParent(canvas.transform, false);
            background.color = new Color(.025f, .03f, .05f, .96f);
            background.rectTransform.sizeDelta = rect.sizeDelta;
            TextLine(canvas.transform, "title", 445, 38, Color.white);
            TextLine(canvas.transform, "diagnostic", 393, 26, new Color(1, .75f, .3f));
            buildStatus = TextLine(canvas.transform, null, 340, 23, new Color(.6f, .9f, 1));
            profileStatus = TextLine(canvas.transform, null, 270, 30, Color.white);
            var logoTexture = Resources.Load<Texture2D>("quest-steam-logo");
            if (logoTexture != null)
            {
                var logo = new GameObject("Embedded static Steam logo").AddComponent<RawImage>();
                logo.transform.SetParent(canvas.transform, false);
                logo.texture = logoTexture;
                logo.rectTransform.anchoredPosition = new Vector2(-420, 270);
                logo.rectTransform.sizeDelta = new Vector2(140, 42);
            }
            if (profile.isDummy) TextLine(canvas.transform, "dummy", 210, 26, new Color(1, .6f, .3f));
            instructions = TextLine(canvas.transform, null, 120, 27, Color.white);
            ExcludedRow(canvas.transform, "guildmaster", -5);
            ExcludedRow(canvas.transform, "workshop", -55);
            tooltip = TextLine(canvas.transform, "excluded", -105, 25, new Color(1, .8f, .4f));
            tooltip.enabled = false;
            status = TextLine(canvas.transform, null, -170, 26, Color.white);
            modelStatus = TextLine(canvas.transform, null, -225, 25, Color.white);
            performance = TextLine(canvas.transform, null, -280, 25, Color.white);
            trackingStatus = TextLine(canvas.transform, null, -370, 22, Color.white);
            TextLine(canvas.transform, "navigation", -440, 24, Color.white);
            diagnostics = stage.gameObject.AddComponent<QuestProbeDiagnostics>();
            diagnostics.Initialize(stage, view, model, stamp.modBuild, stamp.inputKey, german);
            diagnostics.Panel.localPosition = new Vector3(-.68f, 1.65f, 1.95f);
        }
        Text TextLine(Transform parent, string key, float y, int size, Color color)
        {
            var text = new GameObject(key ?? "Diagnostic status").AddComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchoredPosition = new Vector2(0, y);
            text.rectTransform.sizeDelta = new Vector2(1040, 85);
            if (key != null)
            {
                text.text = QuestText.Get(key, german);
                text.gameObject.AddComponent<QuestLabel>().key = key;
            }
            return text;
        }
        void ExcludedRow(Transform parent, string key, float y)
        {
            var row = TextLine(parent, key, y, 29, new Color(.5f, .5f, .5f));
            var collider = row.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(750, 45, 20);
            row.gameObject.AddComponent<QuestExcludedEntry>();
        }
        void Update()
        {
            TrackHead();
            UpdateControllerPose(left);
            UpdateControllerPose(right);
            bool inputAllowed = headTracked && focused && !paused;
            locomotion.Step(rig, view.transform, left.tracked ? left.stick.ReadValue<Vector2>() : Vector2.zero,
                right.tracked ? right.stick.ReadValue<Vector2>().x : 0, Time.unscaledDeltaTime,
                inputAllowed && left.tracked && right.tracked);
            // Origin movement changes world-space aim without changing the tracked local samples.
            UpdateControllerPose(left);
            UpdateControllerPose(right);
            bool hovered = HandleController(left, inputAllowed) | HandleController(right, inputAllowed);
            tooltip.enabled = hovered;
            diagnostics.UpdateTracking(new QuestProbeTrackingSnapshot
            {
                headTracked = headTracked, leftTracked = left.tracked, rightTracked = right.tracked,
                headPosition = view.transform.position, leftPosition = left.pose.position, rightPosition = right.pose.position,
                leftAimTracked = left.aimTracked, rightAimTracked = right.aimTracked,
                leftAimPosition = left.aimPosition, rightAimPosition = right.aimPosition,
                leftAimDirection = left.aimDirection, rightAimDirection = right.aimDirection,
                rigPosition = rig.position, rigYaw = rig.eulerAngles.y,
                originMode = originMode, passthroughActive = QuestPassthroughFeature.Active,
                headPoseBound = headInput.Bound, leftGripBound = left.gripInput.Bound, rightGripBound = right.gripInput.Bound,
                leftAimBound = left.aimInput.Bound, rightAimBound = right.aimInput.Bound,
                leftLayout = left.gripInput.Layout, rightLayout = right.gripInput.Layout
            });
            bool active = QuestPassthroughFeature.Active;
            if (active != lastActive)
            {
                view.backgroundColor = active ? Color.clear : new Color(.018f, .025f, .045f, 1);
                lastActive = active;
                Debug.Log("[GloomhavenVR Quest] camera passthrough active=" + active);
            }
            elapsed += Time.unscaledDeltaTime;
            ++frames;
            if (Time.unscaledTime >= nextRefresh)
            {
                fps = elapsed > 0 ? frames / elapsed : 0;
                frames = 0;
                elapsed = 0;
                nextRefresh = Time.unscaledTime + 1;
                RefreshText();
            }
        }
        void TrackHead()
        {
            if (Time.unscaledTime >= nextOriginCheck)
            {
                nextOriginCheck = Time.unscaledTime + .5f;
                var inputs = new System.Collections.Generic.List<XRInputSubsystem>();
                SubsystemManager.GetInstances(inputs);
                foreach (var input in inputs)
                {
                    if (!input.running) continue;
                    if (!floorConfigured) floorConfigured = input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    string observedOrigin = input.GetTrackingOriginMode().ToString();
                    if (originMode != observedOrigin) Debug.Log("[GloomhavenVR Quest] tracking origin=" + observedOrigin);
                    originMode = observedOrigin;
                    break;
                }
            }
            Vector3 position;
            Quaternion rotation;
            headTracked = headInput.Bound ? headInput.TryRead(out position, out rotation) :
                QuestProbePoseInput.TryReadDevice(InputDevices.GetDeviceAtXRNode(XRNode.Head), out position, out rotation);
            if (!headTracked) return;
            view.transform.localPosition = position;
            view.transform.localRotation = rotation;
        }
        void BeforeRender()
        {
            if (controllers == null) return;
            // Pose sampling is shared; navigation and button edges are authored only in Update.
            TrackHead();
            foreach (var controller in controllers) { UpdateControllerPose(controller); UpdateRay(controller, out _); }
        }
        void UpdateControllerPose(Controller controller)
        {
            Vector3 position;
            Quaternion rotation;
            controller.tracked = controller.gripInput.Bound ? controller.gripInput.TryRead(out position, out rotation) :
                QuestProbePoseInput.TryReadDevice(InputDevices.GetDeviceAtXRNode(controller.node), out position, out rotation);
            controller.pose.gameObject.SetActive(controller.tracked);
            if (controller.tracked)
            {
                controller.pose.localPosition = position;
                controller.pose.localRotation = rotation;
            }
            // OpenXR aim is never reconstructed from grip orientation or an arbitrary fixed angle.
            controller.aimTracked = controller.aimInput.TryRead(out position, out rotation);
            controller.aimPosition = controller.aimTracked ? rig.TransformPoint(position) : Vector3.zero;
            controller.aimDirection = controller.aimTracked ? rig.TransformDirection(rotation * Vector3.forward) : Vector3.zero;
            controller.ray.enabled = controller.aimTracked && controller.tracked && headTracked && focused && !paused;
        }
        bool UpdateRay(Controller controller, out RaycastHit hit)
        {
            hit = default;
            if (!controller.ray.enabled) return false;
            bool hasHit = Physics.Raycast(controller.aimPosition, controller.aimDirection, out hit, 5);
            controller.ray.SetPosition(0, controller.aimPosition);
            controller.ray.SetPosition(1, hasHit ? hit.point : controller.aimPosition + controller.aimDirection * 3);
            return hasHit;
        }
        bool HandleController(Controller controller, bool allowed)
        {
            bool hasHit = UpdateRay(controller, out RaycastHit hit);
            if (!controller.tracked || !allowed)
            {
                controller.buttonsReady = false;
                controller.primary = controller.secondary = controller.trigger = false;
                return false;
            }
            bool primary = controller.primaryAction.ReadValue<float>() > .5f;
            bool secondary = controller.secondaryAction.ReadValue<float>() > .5f;
            bool trigger = controller.triggerAction.ReadValue<float>() > .65f;
            if (!controller.buttonsReady)
            {
                controller.buttonsReady = !primary && !secondary && !trigger;
            }
            else
            {
                if (primary && !controller.primary)
                {
                    if (controller.node == XRNode.RightHand) ToggleMr(); else WriteStorage();
                }
                if (secondary && !controller.secondary)
                {
                    if (controller.node == XRNode.RightHand) PlaceTable();
                    else { german = !german; diagnostics.SetLanguage(german); RefreshText(); }
                }
                if (trigger && !controller.trigger && hasHit) diagnostics.TryActivate(hit.collider);
            }
            controller.primary = primary;
            controller.secondary = secondary;
            controller.trigger = trigger;
            return hasHit && hit.collider.GetComponent<QuestExcludedEntry>() != null;
        }
        void ToggleMr()
        {
            mrRequested = !mrRequested;
            mrError = null;
            if (!QuestPassthroughFeature.SetEnabled(mrRequested) && mrRequested)
            {
                mrRequested = false;
                mrError = "mrFailed";
            }
            RefreshText();
        }
        void PlaceTable()
        {
            var forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            stage.position = new Vector3(view.transform.position.x, 0, view.transform.position.z);
            stage.rotation = Quaternion.LookRotation(forward, Vector3.up);
            Debug.Log("[GloomhavenVR Quest] diagnostic table placed");
        }
        void ReadStorage()
        {
            if (!File.Exists(savePath)) return;
            try
            {
                var record = JsonUtility.FromJson<StorageRecord>(File.ReadAllText(savePath));
                storagePassed = record != null && record.schema == 1 && record.steamId == profile.steamId && record.marker == "quest-hardware-diagnostic";
                saveStatus = storagePassed ? "savePassed" : "saveFailed";
                Debug.Log("[GloomhavenVR Quest] diagnostic storage restored=" + storagePassed);
            }
            catch (Exception error) { saveStatus = "saveFailed"; Debug.LogException(error); }
        }
        void WriteStorage()
        {
            try
            {
                int writes = 0;
                if (File.Exists(savePath))
                {
                    var previous = JsonUtility.FromJson<StorageRecord>(File.ReadAllText(savePath));
                    if (previous != null && previous.steamId == profile.steamId) writes = previous.writes;
                }
                string pending = savePath + ".pending";
                File.WriteAllText(pending, JsonUtility.ToJson(new StorageRecord
                {
                    steamId = profile.steamId, marker = "quest-hardware-diagnostic", writes = writes + 1
                }));
                if (File.Exists(savePath)) File.Delete(savePath);
                File.Move(pending, savePath);
                ReadStorage();
            }
            catch (Exception error) { saveStatus = "saveFailed"; Debug.LogException(error); }
            RefreshText();
        }
        string Tracked(bool value) => QuestText.Get(value ? "probeYes" : "probeNo", german);
        void RefreshText()
        {
            buildStatus.text = "ModBuild " + stamp.modBuild + " · " + stamp.inputKey.Substring(0, Math.Min(12, stamp.inputKey.Length));
            trackingStatus.text = QuestText.Get("probeTracking", german) + ": " + Tracked(headTracked) + " / " + Tracked(left.tracked) + " / " + Tracked(right.tracked) +
                "\n" + QuestText.Get("probeAim", german) + ": " + Tracked(left.aimTracked) + " / " + Tracked(right.aimTracked) + " · " + originMode;
            profileStatus.text = profile.displayName + "\n" + QuestText.Get("steamId", german) + ": " + profile.steamId;
            foreach (var label in stage.GetComponentsInChildren<QuestLabel>())
                label.GetComponent<Text>().text = QuestText.Get(label.key, german);
            instructions.text = QuestText.Get("mr", german) + " · " + QuestText.Get("recenter", german) +
                "\n" + QuestText.Get("save", german) + " · " + QuestText.Get("language", german);
            status.text = QuestText.Get(mrError ?? (QuestPassthroughFeature.Active ? "mrActive" : "vrActive"), german) +
                "\n" + QuestText.Get(saveStatus, german);
            modelStatus.text = QuestText.Get(model != null ? "model" : "modelMissing", german);
            performance.text = QuestText.Get("performance", german) + ": " + fps.ToString("F1", CultureInfo.InvariantCulture) + " fps";
        }
        void OnApplicationPause(bool value) { paused = value; SuspendInput(); Debug.Log("[GloomhavenVR Quest] pause=" + value); }
        void OnApplicationFocus(bool value) { focused = value; SuspendInput(); Debug.Log("[GloomhavenVR Quest] focus=" + value); }
        void SuspendInput()
        {
            locomotion.Suspend();
            floorConfigured = false; nextOriginCheck = 0;
            if (controllers != null) foreach (var controller in controllers) controller.buttonsReady = false;
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= CaptureLog;
            Application.onBeforeRender -= BeforeRender;
            headInput?.Dispose();
            if (controllers == null) return;
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                controller.gripInput.Dispose(); controller.aimInput.Dispose();
                controller.primaryAction.Dispose(); controller.secondaryAction.Dispose();
                controller.triggerAction.Dispose(); controller.stick.Dispose();
            }
        }
        void CaptureLog(string message, string trace, LogType type)
        {
            try
            {
                // Diagnostic-only log, bounded across restarts; no per-frame traces.
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 1024 * 1024)
                {
                    string previous = logPath + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(logPath, previous);
                }
                File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " " + type + " " + message +
                    ((type == LogType.Exception || type == LogType.Error) ? "\n" + trace : "") + "\n");
            }
            catch (IOException) { /* Android logcat remains available if local storage fails. */ }
        }
    }
    public sealed class QuestLabel : MonoBehaviour { public string key; }
    public sealed class QuestExcludedEntry : MonoBehaviour { }
}
