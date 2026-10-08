using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloomhavenVR.Quest
{
    /// <summary>Pose data from the owning XR rig; all positions/directions are world-space.</summary>
    [Serializable] public struct QuestProbeTrackingSnapshot
    {
        public bool headTracked, leftTracked, rightTracked, passthroughActive;
        public bool leftAimTracked, rightAimTracked;
        public bool headPoseBound, leftGripBound, rightGripBound, leftAimBound, rightAimBound;
        public Vector3 headPosition, leftPosition, rightPosition;
        public Vector3 leftAimPosition, rightAimPosition, leftAimDirection, rightAimDirection, rigPosition;
        public float rigYaw;
        public string originMode, leftLayout, rightLayout;
    }
    /// <summary>Bounded visual comparisons and evidence for the diagnostic scene only.</summary>
    public sealed class QuestProbeDiagnostics : MonoBehaviour
    {
        [Serializable] public sealed class FrameStats
        {
            public int samples, longFrames;
            public float minMs, averageMs, p95Ms, maxMs, refreshHz, longFrameBudgetMs;
            public bool refreshAvailable, gpuTimingAvailable;
            public long managedBytes;
        }
        /// <summary>Fixed rolling storage; recording a frame allocates nothing.</summary>
        public sealed class FrameWindow
        {
            readonly float[] samples, sorted;
            int next, count;
            public FrameWindow(int capacity = 720)
            {
                if (capacity < 1 || capacity > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
                samples = new float[capacity]; sorted = new float[capacity];
            }
            public void Clear() { next = count = 0; }
            public void RecordMilliseconds(float milliseconds)
            {
                if (float.IsNaN(milliseconds) || float.IsInfinity(milliseconds) || milliseconds <= 0) return;
                samples[next] = milliseconds; next = (next + 1) % samples.Length;
                if (count < samples.Length) count++;
            }
            public FrameStats Read(float refreshHz, long managedBytes)
            {
                var stats = new FrameStats { samples = count, managedBytes = managedBytes,
                    refreshAvailable = refreshHz > 0 && !float.IsNaN(refreshHz) && !float.IsInfinity(refreshHz),
                    gpuTimingAvailable = false, longFrames = -1 };
                if (stats.refreshAvailable)
                { stats.refreshHz = refreshHz; stats.longFrameBudgetMs = 1500f / refreshHz; stats.longFrames = 0; }
                if (count == 0) return stats;
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    float value = samples[i]; sorted[i] = value; sum += value;
                    if (stats.refreshAvailable && value > stats.longFrameBudgetMs) stats.longFrames++;
                }
                Array.Sort(sorted, 0, count);
                stats.minMs = sorted[0]; stats.maxMs = sorted[count - 1];
                stats.averageMs = (float)(sum / count);
                stats.p95Ms = sorted[Math.Max(0, (int)Math.Ceiling(count * .95) - 1)];
                return stats;
            }
        }
        [Serializable] public sealed class MaterialInfo
        {
            public string renderer, material, sourceShader, runtimeShader, texture, textureProperty;
            public int slot, width, height;
            public bool texturePresent, sourceMetadataAvailable, originalShaderFidelity;
            public Vector2 uvScale, uvOffset;
            public Color tint, sourceSavedColor;
        }
        [Serializable] public sealed class LifecycleEvent
        { public int sequence; public string kind, utc; public bool value; }
        [Serializable] public sealed class HardwareSnapshot
        {
            public int schema = 1, modBuild, page, selectedMaterial, focusEvents, pauseEvents;
            public int headTrackingLosses, leftTrackingLosses, rightTrackingLosses, leftAimTrackingLosses, rightAimTrackingLosses;
            public string inputKey, utc, unityVersion, appVersion, graphicsDevice, renderMode, activeColorSpace;
            public bool inspectionScale, animationPaused, focused, appPaused;
            public bool cameraHdr, cameraMsaa, cameraStereoEnabled;
            public Color[] referenceColors;
            public Vector3 modelLocalScale;
            public FrameStats timing;
            public QuestProbeTrackingSnapshot tracking;
            public MaterialInfo[] materials;
            public LifecycleEvent[] lifecycle;
        }
        [Serializable] sealed class MaterialMetadata
        {
            public string materialName, sourceShader;
            public Vector2 albedoScale, albedoOffset;
            public Color sourceSavedColor, targetTint;
        }
        [Serializable] sealed class Metadata
        { public int schema; public bool originalShaderFidelity; public MaterialMetadata[] materials; }
        sealed class MaterialPair
        { internal Material source, lit, albedo; internal MaterialMetadata metadata; }
        sealed class RendererState
        { internal Renderer renderer; internal Material[] original, lit, albedo; }
        sealed class MaterialSlot
        { internal Renderer renderer; internal int slot; internal MaterialPair pair; }
        sealed class LegacyState
        { internal AnimationState state; internal float speed; }

        readonly FrameWindow frameWindow = new FrameWindow();
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>(2);
        readonly List<RendererState> rendererStates = new List<RendererState>();
        readonly List<MaterialPair> materialPairs = new List<MaterialPair>();
        readonly List<MaterialSlot> slots = new List<MaterialSlot>();
        readonly List<LegacyState> legacyStates = new List<LegacyState>();
        readonly LifecycleEvent[] lifecycle = new LifecycleEvent[16];
        static readonly Color[] ReferenceColors = { Color.white, new Color(.5f, .5f, .5f), Color.red, Color.green, Color.blue, Color.black };
        Animator[] animators = Array.Empty<Animator>();
        float[] animatorSpeeds = Array.Empty<float>();
        Transform stage;
        GameObject model;
        Camera view;
        Text title, details, note, state;
        RawImage atlas;
        GameObject atlasRoot, referenceRoot;
        readonly List<Text> localizedLabels = new List<Text>();
        readonly List<string> localizedKeys = new List<string>();
        readonly List<GameObject> materialButtons = new List<GameObject>();
        readonly List<GameObject> animationButtons = new List<GameObject>();
        Font font;
        Material albedoTemplate;
        bool initialized, german, albedoMode, inspection, animationPaused, focused, appPaused;
        bool discardNextFrame = true, warnedAlbedo, warnedWrite;
        bool trackingInitialized;
        int page, selectedMaterial, modBuild, lifecycleNext, lifecycleCount, lifecycleSequence, focusEvents, pauseEvents;
        int headTrackingLosses, leftTrackingLosses, rightTrackingLosses, leftAimTrackingLosses, rightAimTrackingLosses;
        string inputKey, statePath, stateKey = "probeStateSaved";
        float nextUi, nextSave;
        Vector3 originalScale, originalPosition;
        QuestProbeTrackingSnapshot tracking;
        public Transform Panel { get; private set; }

        public void Initialize(Transform stage, Camera view, GameObject model, int modBuild,
            string inputKey, bool german, string stateDirectory = null)
        {
            if (initialized) throw new InvalidOperationException("Quest diagnostics is already initialized.");
            if (stage == null || view == null) throw new ArgumentNullException(stage == null ? nameof(stage) : nameof(view));
            if (modBuild < 1 || string.IsNullOrWhiteSpace(inputKey)) throw new ArgumentException("Missing diagnostic build provenance.");
            this.stage = stage; this.view = view; this.model = model; this.modBuild = modBuild;
            this.inputKey = Bounded(inputKey, 512); this.german = german; focused = Application.isFocused;
            statePath = Path.Combine(stateDirectory ?? Application.persistentDataPath, "quest-hardware-state.json");
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            albedoTemplate = Resources.Load<Material>("quest-albedo-material");
            PrepareModel(); BuildPanel(); initialized = true;
            Debug.Log("[QuestProbe] battery initialized ModBuild=" + modBuild + " input=" + this.inputKey +
                " materialSlots=" + slots.Count + " scope=diagnostic-only GPU-timing=unavailable");
            SaveSnapshot(); RefreshUi(); nextSave = Time.unscaledTime + 5f;
        }
        void PrepareModel()
        {
            Metadata metadata = null;
            var asset = Resources.Load<TextAsset>("quest-probe-materials");
            if (asset != null)
            {
                try { metadata = JsonUtility.FromJson<Metadata>(asset.text); }
                catch (Exception error) { Debug.LogWarning("[QuestProbe] material metadata unavailable: " + error.Message); }
            }
            if (model == null) return;
            originalScale = model.transform.localScale; originalPosition = model.transform.localPosition;
            var pairs = new Dictionary<Material, MaterialPair>();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var original = renderer.sharedMaterials;
                if (slots.Count + original.Length > 64)
                    throw new InvalidOperationException("Diagnostic model exceeds the 64-material-slot evidence bound.");
                var saved = new RendererState { renderer = renderer, original = original,
                    lit = new Material[original.Length], albedo = new Material[original.Length] };
                rendererStates.Add(saved);
                for (int i = 0; i < original.Length; i++)
                {
                    Material source = original[i];
                    if (source == null) continue;
                    MaterialPair pair;
                    if (!pairs.TryGetValue(source, out pair))
                    {
                        pair = new MaterialPair { source = source, lit = new Material(source) };
                        pair.lit.name = source.name + " (Quest lit diagnostic)";
                        if (metadata != null && metadata.schema == 1 && metadata.materials != null)
                        {
                            string name = source.name.Replace(" (Instance)", "").Replace(" (Clone)", "");
                            foreach (var item in metadata.materials)
                                if (item != null && item.materialName == name)
                                { if (pair.metadata != null) { pair.metadata = null; break; } pair.metadata = item; }
                        }
                        if (albedoTemplate != null && albedoTemplate.HasProperty("_MainTex") && albedoTemplate.HasProperty("_Color"))
                        {
                            pair.albedo = new Material(albedoTemplate);
                            string property = TextureProperty(source);
                            if (property != null)
                            {
                                pair.albedo.SetTexture("_MainTex", source.GetTexture(property));
                                pair.albedo.SetTextureScale("_MainTex", source.GetTextureScale(property));
                                pair.albedo.SetTextureOffset("_MainTex", source.GetTextureOffset(property));
                            }
                            pair.albedo.SetColor("_Color", Tint(source));
                            pair.albedo.name = source.name + " (Quest albedo diagnostic)";
                        }
                        pairs.Add(source, pair); materialPairs.Add(pair);
                    }
                    saved.lit[i] = pair.lit; saved.albedo[i] = pair.albedo;
                    slots.Add(new MaterialSlot { renderer = renderer, slot = i, pair = pair });
                }
                renderer.sharedMaterials = saved.lit;
            }
            animators = model.GetComponentsInChildren<Animator>(true); animatorSpeeds = new float[animators.Length];
            for (int i = 0; i < animators.Length; i++) animatorSpeeds[i] = animators[i].speed;
            foreach (var animation in model.GetComponentsInChildren<Animation>(true))
                foreach (AnimationState animationState in animation)
                    legacyStates.Add(new LegacyState { state = animationState, speed = animationState.speed });
        }
        static string TextureProperty(Material material)
        { return material.HasProperty("_MainTex") ? "_MainTex" : material.HasProperty("_BaseMap") ? "_BaseMap" : null; }
        static Color Tint(Material material)
        { return material.HasProperty("_Color") ? material.GetColor("_Color") : material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white; }
        public void UpdateTracking(QuestProbeTrackingSnapshot snapshot)
        {
            if (trackingInitialized)
            {
                if (tracking.headTracked && !snapshot.headTracked) headTrackingLosses++;
                if (tracking.leftTracked && !snapshot.leftTracked) leftTrackingLosses++;
                if (tracking.rightTracked && !snapshot.rightTracked) rightTrackingLosses++;
                if (tracking.leftAimTracked && !snapshot.leftAimTracked) leftAimTrackingLosses++;
                if (tracking.rightAimTracked && !snapshot.rightAimTracked) rightAimTrackingLosses++;
            }
            trackingInitialized = true; tracking = snapshot;
        }
        public void SetLanguage(bool value)
        { german = value; if (initialized) { SaveSnapshot(); RefreshUi(); } }
        public void NextPage() { if (!initialized) return; page = (page + 1) % 3; Changed("page=" + page, false); }
        public void NextMaterial()
        { if (!initialized || slots.Count == 0) return; selectedMaterial = (selectedMaterial + 1) % slots.Count; Changed("material=" + selectedMaterial, false); }
        public void ToggleRenderMode()
        {
            if (!initialized || model == null) return;
            foreach (var pair in materialPairs)
                if (pair.albedo == null)
                {
                    if (!warnedAlbedo) { warnedAlbedo = true; Debug.LogWarning("[QuestProbe] retained tinted albedo shader is unavailable; lit mode retained."); }
                    return;
                }
            albedoMode = !albedoMode;
            foreach (var renderer in rendererStates)
                if (renderer.renderer != null) renderer.renderer.sharedMaterials = albedoMode ? renderer.albedo : renderer.lit;
            Changed("render=" + (albedoMode ? "albedo" : "lit"), true);
        }
        public void ToggleAnimation()
        {
            if (!initialized || model == null) return;
            animationPaused = !animationPaused;
            for (int i = 0; i < animators.Length; i++)
                if (animators[i] != null) animators[i].speed = animationPaused ? 0f : animatorSpeeds[i];
            foreach (var legacy in legacyStates) legacy.state.speed = animationPaused ? 0f : legacy.speed;
            Changed("animationPaused=" + animationPaused, true);
        }
        public void ToggleModelScale()
        {
            if (!initialized || model == null) return;
            inspection = !inspection;
            if (inspection)
            {
                Bounds before; bool grounded = TryModelBounds(out before);
                model.transform.localScale = originalScale * 3f;
                Bounds after;
                if (grounded && TryModelBounds(out after))
                    model.transform.position += Vector3.up * (before.min.y - after.min.y);
            }
            else { model.transform.localScale = originalScale; model.transform.localPosition = originalPosition; }
            Changed("inspectionScale=" + inspection, true);
        }
        bool TryModelBounds(out Bounds bounds)
        {
            bounds = new Bounds(); bool found = false;
            foreach (var saved in rendererStates)
                if (saved.renderer != null)
                { if (!found) { bounds = saved.renderer.bounds; found = true; } else bounds.Encapsulate(saved.renderer.bounds); }
            return found;
        }
        void Changed(string action, bool resetTiming)
        {
            if (resetTiming) { frameWindow.Clear(); discardNextFrame = true; }
            Debug.Log("[QuestProbe] " + action); SaveSnapshot(); RefreshUi();
        }
        void Update()
        {
            if (!initialized) return;
            if (!appPaused && focused)
            {
                if (discardNextFrame) discardNextFrame = false;
                else frameWindow.RecordMilliseconds(Time.unscaledDeltaTime * 1000f);
            }
            float now = Time.unscaledTime;
            if (now >= nextUi) { nextUi = now + 2f; RefreshUi(); }
            if (now >= nextSave) { nextSave = now + 5f; SaveSnapshot(); }
        }
        void OnApplicationPause(bool value)
        {
            if (!initialized || appPaused == value) return;
            appPaused = value; pauseEvents++; Lifecycle("pause", value); discardNextFrame = true; SaveSnapshot(); RefreshUi();
        }
        void OnApplicationFocus(bool value)
        {
            if (!initialized || focused == value) return;
            focused = value; focusEvents++; Lifecycle("focus", value); discardNextFrame = true; SaveSnapshot(); RefreshUi();
        }
        void Lifecycle(string kind, bool value)
        {
            lifecycle[lifecycleNext] = new LifecycleEvent { sequence = ++lifecycleSequence, kind = kind,
                value = value, utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            lifecycleNext = (lifecycleNext + 1) % lifecycle.Length;
            if (lifecycleCount < lifecycle.Length) lifecycleCount++;
            Debug.Log("[QuestProbe] lifecycle " + kind + "=" + value);
        }
        float DisplayRefreshRate()
        {
            SubsystemManager.GetInstances(displays);
            foreach (var display in displays)
                if (display.running && display.TryGetDisplayRefreshRate(out float refresh) && refresh > 0) return refresh;
            return 0;
        }
        public HardwareSnapshot CaptureSnapshot()
        {
            var snapshot = new HardwareSnapshot { modBuild = modBuild, inputKey = inputKey, page = page,
                selectedMaterial = selectedMaterial, utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion, appVersion = Application.version, graphicsDevice = SystemInfo.graphicsDeviceName,
                activeColorSpace = QualitySettings.activeColorSpace.ToString(), cameraHdr = view != null && view.allowHDR,
                cameraMsaa = view != null && view.allowMSAA, cameraStereoEnabled = view != null && view.stereoEnabled,
                referenceColors = ReferenceColors,
                renderMode = albedoMode ? "albedo" : "lit", inspectionScale = inspection, animationPaused = animationPaused,
                focused = focused, appPaused = appPaused, focusEvents = focusEvents, pauseEvents = pauseEvents,
                headTrackingLosses = headTrackingLosses, leftTrackingLosses = leftTrackingLosses,
                rightTrackingLosses = rightTrackingLosses, leftAimTrackingLosses = leftAimTrackingLosses, rightAimTrackingLosses = rightAimTrackingLosses,
                timing = frameWindow.Read(DisplayRefreshRate(), GC.GetTotalMemory(false)), tracking = tracking,
                modelLocalScale = model != null ? model.transform.localScale : Vector3.zero,
                materials = new MaterialInfo[slots.Count], lifecycle = new LifecycleEvent[lifecycleCount] };
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i]; Material material = albedoMode ? slot.pair.albedo : slot.pair.lit;
                string property = TextureProperty(material); Texture texture = property != null ? material.GetTexture(property) : null;
                snapshot.materials[i] = new MaterialInfo { renderer = Bounded(slot.renderer.name, 128), slot = slot.slot,
                    material = Bounded(slot.pair.source.name, 128), sourceMetadataAvailable = slot.pair.metadata != null,
                    sourceShader = slot.pair.metadata != null ? Bounded(slot.pair.metadata.sourceShader, 256) : null,
                    runtimeShader = material.shader != null ? Bounded(material.shader.name, 256) : null,
                    originalShaderFidelity = false, texturePresent = texture != null, texture = texture != null ? Bounded(texture.name, 256) : null,
                    textureProperty = property, width = texture != null ? texture.width : 0, height = texture != null ? texture.height : 0,
                    uvScale = property != null ? material.GetTextureScale(property) : Vector2.one,
                    uvOffset = property != null ? material.GetTextureOffset(property) : Vector2.zero, tint = Tint(material),
                    sourceSavedColor = slot.pair.metadata != null ? slot.pair.metadata.sourceSavedColor : Color.clear };
            }
            int start = (lifecycleNext - lifecycleCount + lifecycle.Length) % lifecycle.Length;
            for (int i = 0; i < lifecycleCount; i++) snapshot.lifecycle[i] = lifecycle[(start + i) % lifecycle.Length];
            return snapshot;
        }
        public void SaveSnapshot()
        {
            if (!initialized) return;
            try
            {
                string json = JsonUtility.ToJson(CaptureSnapshot(), true);
                if (json.Length > 131072) throw new InvalidOperationException("Diagnostic snapshot exceeds 128 KiB.");
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                string pending = statePath + ".tmp";
                File.WriteAllText(pending, json);
                if (File.Exists(statePath)) File.Replace(pending, statePath, null); else File.Move(pending, statePath);
                stateKey = "probeStateSaved"; warnedWrite = false;
            }
            catch (Exception error)
            {
                stateKey = "probeStateFailed";
                if (!warnedWrite) { warnedWrite = true; Debug.LogWarning("[QuestProbe] bounded snapshot write failed: " + error.Message); }
            }
        }
        static string Bounded(string value, int limit)
        { return value == null ? null : value.Length <= limit ? value : value.Substring(0, limit); }

        void BuildPanel()
        {
            var canvas = new GameObject("Quest diagnostic battery", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = view;
            Panel = canvas.transform; Panel.SetParent(stage, false);
            Panel.localPosition = new Vector3(.9f, 1.65f, 1.95f); Panel.localScale = Vector3.one * .001f;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(820, 760);
            var backing = Image(Panel, "Diagnostic UI backing", Vector2.zero, new Vector2(820, 760), new Color(.025f, .03f, .05f, .97f));
            backing.raycastTarget = false;
            title = Label(Panel, "Battery page", new Vector2(0, 329), new Vector2(760, 50), 30);
            details = Label(Panel, "Battery details", new Vector2(0, 156), new Vector2(756, 210), 21);
            details.alignment = TextAnchor.UpperLeft;
            note = Label(Panel, "Battery scope", new Vector2(0, -178), new Vector2(756, 85), 20);
            state = Label(Panel, "Battery storage", new Vector2(0, -236), new Vector2(756, 40), 19);
            atlasRoot = new GameObject("Original albedo atlas", typeof(RectTransform)); atlasRoot.transform.SetParent(Panel, false);
            atlas = atlasRoot.AddComponent<RawImage>(); atlas.rectTransform.anchoredPosition = new Vector2(0, -57);
            atlas.rectTransform.sizeDelta = new Vector2(250, 200); atlas.raycastTarget = false;
            referenceRoot = new GameObject("UI stereo colour reference", typeof(RectTransform)); referenceRoot.transform.SetParent(Panel, false);
            Color[] colors = ReferenceColors;
            for (int i = 0; i < colors.Length; i++)
                Image(referenceRoot.transform, "UI colour swatch " + i, new Vector2(-275 + i * 110, -55), new Vector2(100, 85), colors[i]);
            materialButtons.Add(Button("probeNextMaterial", new Vector2(-195, -285), () => NextMaterial()));
            materialButtons.Add(Button("probeToggleRender", new Vector2(195, -285), () => ToggleRenderMode()));
            animationButtons.Add(Button("probeToggleAnimation", new Vector2(-195, -285), () => ToggleAnimation()));
            animationButtons.Add(Button("probeToggleScale", new Vector2(195, -285), () => ToggleModelScale()));
            Button("probeNextPage", new Vector2(-195, -343), () => NextPage());
            Button("probeCapture", new Vector2(195, -343), () => { SaveSnapshot(); RefreshUi(); });
        }
        static Image Image(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false); image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size; image.color = color; image.raycastTarget = false; return image;
        }
        Text Label(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false); label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size; label.font = font; label.fontSize = fontSize; label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false; return label;
        }
        GameObject Button(string key, Vector2 position, Action callback)
        {
            var button = Image(Panel, key, position, new Vector2(366, 49), new Color(.15f, .22f, .30f));
            var label = Label(button.transform, key + " label", Vector2.zero, new Vector2(355, 45), 20);
            localizedLabels.Add(label); localizedKeys.Add(key);
            var collider = button.gameObject.AddComponent<BoxCollider>(); collider.size = new Vector3(366, 49, 16);
            button.gameObject.AddComponent<QuestProbeDiagnosticAction>().callback = callback;
            return button.gameObject;
        }
        public bool TryActivate(Collider hit)
        {
            if (!initialized || hit == null || !hit.transform.IsChildOf(Panel)) return false;
            var action = hit.GetComponent<QuestProbeDiagnosticAction>();
            if (action == null || !action.isActiveAndEnabled) return false;
            action.Activate(); return true;
        }
        string L(string key) { return QuestText.Get(key, german); }
        static string F(float value) { return value.ToString("F2", CultureInfo.InvariantCulture); }
        void RefreshUi()
        {
            if (Panel == null) return;
            for (int i = 0; i < localizedLabels.Count; i++) localizedLabels[i].text = L(localizedKeys[i]);
            title.text = L("probeBattery") + " " + (page + 1) + "/3 · " + L(page == 0 ? "probeMaterials" : page == 1 ? "probeAnimation" : "probeTiming");
            atlasRoot.SetActive(page == 0); referenceRoot.SetActive(page == 1);
            foreach (var button in materialButtons) button.SetActive(page == 0);
            foreach (var button in animationButtons) button.SetActive(page == 1);
            var text = new StringBuilder(1024);
            HardwareSnapshot snapshot = CaptureSnapshot();
            if (page == 0 && slots.Count > 0)
            {
                MaterialInfo material = snapshot.materials[selectedMaterial]; var source = slots[selectedMaterial].pair.source;
                string property = TextureProperty(source); atlas.texture = property != null ? source.GetTexture(property) : null;
                atlas.color = Tint(source);
                if (atlas.texture != null)
                { float ratio = (float)atlas.texture.width / Math.Max(1, atlas.texture.height); atlas.rectTransform.sizeDelta = ratio >= 1 ? new Vector2(250, 250 / ratio) : new Vector2(200 * ratio, 200); }
                text.Append(selectedMaterial + 1).Append('/').Append(slots.Count).Append(" · ").Append(material.material).Append('\n');
                text.Append(L("probeSourceShader")).Append(": ").Append(material.sourceShader ?? L("probeUnavailable")).Append('\n');
                text.Append(L("probeRuntimeShader")).Append(": ").Append(material.runtimeShader).Append(" · ").Append(L(albedoMode ? "probeAlbedo" : "probeLit")).Append('\n');
                text.Append(L("probeTexture")).Append(": ").Append(material.texturePresent ? material.texture + " " + material.width + "×" + material.height : L("probeNoTexture")).Append('\n');
                text.Append(L("probeTint")).Append(": ").Append(F(material.tint.r)).Append('/').Append(F(material.tint.g)).Append('/').Append(F(material.tint.b)).Append('/').Append(F(material.tint.a)).Append('\n');
                text.Append(L("probeUv")).Append(": ").Append(F(material.uvScale.x)).Append('/').Append(F(material.uvScale.y)).Append(" · ").Append(F(material.uvOffset.x)).Append('/').Append(F(material.uvOffset.y));
                note.text = L("probeParityUnknown") + "\n" + L("probeCheckAtlas");
            }
            else if (page == 1)
            {
                text.Append(L(animationPaused ? "probePaused" : "probeRunning")).Append(" · ").Append(L(inspection ? "probeInspectionSize" : "probeNormalSize")).Append('\n');
                text.Append(L("probeTracking")).Append(": ").Append(tracking.headTracked ? '1' : '0').Append('/').Append(tracking.leftTracked ? '1' : '0').Append('/').Append(tracking.rightTracked ? '1' : '0').Append('\n');
                text.Append(L("probeAim")).Append(": ").Append(tracking.leftAimTracked ? '1' : '0').Append('/').Append(tracking.rightAimTracked ? '1' : '0').Append(" · ").Append(tracking.originMode ?? L("probeUnavailable")).Append('\n');
                text.Append(L("probeParityUnknown")); note.text = L("probeCheckStereo");
            }
            else if (page == 2)
            {
                var timing = snapshot.timing;
                text.Append(L("probeFrameTiming")).Append(":\n").Append(F(timing.minMs)).Append(" / ").Append(F(timing.averageMs)).Append(" / ").Append(F(timing.p95Ms)).Append(" / ").Append(F(timing.maxMs)).Append('\n');
                text.Append(L("probeLongFrames")).Append(": ").Append(timing.longFrames >= 0 ? timing.longFrames.ToString(CultureInfo.InvariantCulture) : L("probeUnavailable")).Append('\n');
                text.Append(L("probeRefresh")).Append(": ").Append(timing.refreshAvailable ? F(timing.refreshHz) + " Hz" : L("probeUnavailable")).Append('\n');
                text.Append(L("probeManagedMemory")).Append(": ").Append(F(timing.managedBytes / 1048576f)).Append(" MiB\n");
                text.Append(L("probeLifecycle")).Append(": ").Append(focusEvents).Append('/').Append(pauseEvents);
                note.text = L("probeDiagnosticScope") + "\n" + L("probeCheckResume");
            }
            else { text.Append(L("modelMissing")); note.text = L("probeDiagnosticScope"); atlas.texture = null; }
            details.text = text.ToString(); state.text = "B" + modBuild + " · " + L(stateKey);
        }
        void OnDestroy()
        {
            foreach (var renderer in rendererStates)
                if (renderer.renderer != null) renderer.renderer.sharedMaterials = renderer.original;
            for (int i = 0; i < animators.Length; i++) if (animators[i] != null) animators[i].speed = animatorSpeeds[i];
            foreach (var legacy in legacyStates) legacy.state.speed = legacy.speed;
            if (model != null && inspection) { model.transform.localScale = originalScale; model.transform.localPosition = originalPosition; }
            foreach (var pair in materialPairs) { if (pair.lit != null) Destroy(pair.lit); if (pair.albedo != null) Destroy(pair.albedo); }
            if (Panel != null) Destroy(Panel.gameObject);
        }
    }
    public sealed class QuestProbeDiagnosticAction : MonoBehaviour
    {
        internal Action callback;
        public void Activate() { if (isActiveAndEnabled && callback != null) callback(); }
    }
}
