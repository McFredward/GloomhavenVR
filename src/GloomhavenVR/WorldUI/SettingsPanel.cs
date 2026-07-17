using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// In-VR settings panel (Phase 5, MISSION B): a compact world-space panel of poke-able
/// steppers/toggles bound to the Phase-4 <see cref="ComfortSettings"/> accessors and
/// key module toggles. Changes apply LIVE (the comfort stack subscribes to its own
/// change events; module toggles are read live every frame) and persist automatically
/// (BepInEx saves on every ConfigEntry write).
///
/// Opening:
/// - a small "gear" pokeable at the table edge (next to the P3c button cluster),
///   scenario-only ([SettingsPanel] GearButton), and
/// - a controller chord anywhere hands exist: hold the NON-dominant lower face button
///   (A/X — free for features, the recenter chord uses B+Y on both hands) for at least
///   [SettingsPanel] ChordHoldSeconds and RELEASE (P6: fires on release, because the
///   same button held longer is the manual flat-screen chord — see NonDominantHold).
///
/// Interaction: the panel canvas registers with <see cref="UguiPokeSurfaces"/>
/// (SmallDialog tuning — MISSION A.10) so the Phase-2 fingertip poke drives real uGUI
/// buttons; no new input paths. Menu2D includes Poke since P5, so the panel also works
/// in the main menu (menu rig).
///
/// Availability deliberately does NOT gate on <see cref="WorldUIConfig.ConversionActive"/>:
/// the panel hosts the [WorldUI] Master switch, so it must stay reachable to turn the
/// physicalized UI back ON.
/// </summary>
internal sealed class SettingsPanel
{
    private const float PanelWidthPx = 380f;
    private const float RowHeightPx = 34f;
    private const float RefreshInterval = 0.25f;

    private GameObject? _root;
    private Canvas? _canvas;
    private GearButton? _gear;
    private bool _open;
    private float _nextRefresh;
    private readonly List<Action> _refreshers = new(16);

    // ---- cross-module seam (test #15) ------------------------------------------------------

    /// <summary>The driver-owned live instance (single WorldUI driver; null after shutdown).</summary>
    private static SettingsPanel? _instance;

    public SettingsPanel() => _instance = this;

    /// <summary>
    /// Test #15: toggle the panel from outside WorldUI — the tray dashboard's gear
    /// button (Cards) uses this. No-op while no panel exists (WorldUI off).
    /// </summary>
    internal static void RequestToggle() => _instance?.Toggle();

    // ---- per-frame -----------------------------------------------------------------------

    public void Tick()
    {
        bool available = VRSession.IsRunning || Plugin.DevMode.Value;
        if (!available)
        {
            if (_open)
                SetOpen(false);
            _gear?.SetVisible(false);
            return;
        }

        TickChord();
        TickGear();

        if (!_open || _root == null)
            return;

        // Keep the canvas camera fresh and hide when no world camera exists at all.
        Camera? cam = CanvasConversion.WorldCamera;
        if (cam == null)
        {
            SetOpen(false);
            return;
        }
        if (_canvas != null && _canvas.worldCamera != cam)
            _canvas.worldCamera = cam;

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshAll();
        }
    }

    public void Shutdown()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
        SetOpen(false);
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _canvas = null;
        }
        _gear?.Destroy();
        _gear = null;
        _refreshers.Clear();
    }

    // ---- open/close ----------------------------------------------------------------------

    internal void Toggle() => SetOpen(!_open);

    private void SetOpen(bool open)
    {
        if (_open == open)
            return;
        _open = open;

        if (open)
        {
            if (_root == null)
                Build();
            if (_root == null)
                return;
            PlaceInFrontOfHead();
            _root.SetActive(true);
            if (_canvas != null)
                UguiPokeSurfaces.Register(_canvas, PokeSurfaceTuning.SmallDialog);
            CanvasConversion.AddMaskRequest(); // head camera must render the UI layer
            RefreshAll();
            VRLog.Info("WorldUI", "Settings panel opened.");
        }
        else if (_root != null)
        {
            if (_canvas != null)
                UguiPokeSurfaces.Unregister(_canvas);
            CanvasConversion.RemoveMaskRequest();
            _root.SetActive(false);
            VRLog.Info("WorldUI", "Settings panel closed.");
        }
    }

    private void PlaceInFrontOfHead()
    {
        if (_root == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
            fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 pos = h.position + fwd * (0.55f * scale) - Vector3.up * (0.12f * scale);
        // uGUI front faces -forward: +Z away from the player.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        Transform t = _root.transform;
        t.SetPositionAndRotation(pos, rot);
        t.localScale = Vector3.one * (0.0007f * scale); // 0.7 mm/px → ~27 cm wide
    }

    // ---- chord ---------------------------------------------------------------------------

    /// <summary>
    /// P6: fires on RELEASE via the shared <see cref="NonDominantHold"/> tracker —
    /// the same button carries the LONG-hold manual flat-screen chord (FlatScreen,
    /// fires at its threshold while held and marks the press Consumed). A short hold
    /// (≥ ChordHoldSeconds, released before the screen chord fired) toggles the
    /// settings panel; a consumed long hold does nothing extra here.
    /// </summary>
    private void TickChord()
    {
        float hold = WorldUIConfig.SettingsChordHoldSeconds.Value;
        if (hold <= 0f || !NonDominantHold.ReleasedThisFrame || NonDominantHold.Consumed)
            return;
        if (NonDominantHold.ReleasedAfterSeconds < hold)
            return;
        NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
        Toggle();
    }

    // ---- gear button ---------------------------------------------------------------------

    private void TickGear()
    {
        bool want = WorldUIConfig.SettingsGearButton.Value
                    && Choreographer.s_Choreographer != null
                    && PanelLayout.TryGetPose(PanelSlot.ButtonCluster, out _, out _);
        if (!want)
        {
            _gear?.SetVisible(false);
            return;
        }

        _gear ??= GearButton.Create(Toggle);
        _gear.SetVisible(true);
        _gear.Place();
    }

    // ---- construction ----------------------------------------------------------------------

    private void Build()
    {
        _root = new GameObject("GloomhavenVR.SettingsPanel") { layer = 5 };
        var rect = _root.AddComponent<RectTransform>();
        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = CanvasConversion.WorldCamera;
        _root.AddComponent<GraphicRaycaster>();
        rect.sizeDelta = new Vector2(PanelWidthPx, 100f); // height grows via layout

        var bg = _root.AddComponent<Image>();
        bg.color = new Color(0.07f, 0.07f, 0.10f, 0.92f);

        var layout = _root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 12);
        layout.spacing = 4f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        var fitter = _root.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header ------------------------------------------------------------------
        var header = Row();
        Label(header, "GloomhavenVR", 20f, bold: true, flexible: true);
        Button(header, "X", 40f, () => SetOpen(false));

        Section("Comfort");

        // Table scale: SetScaleMultiplier applies live around the head + persists.
        Stepper("Table scale",
            () => $"{CurrentScaleMultiplier():0.00}x",
            delta =>
            {
                if (ComfortSettings.IsBound)
                    Comfort.SetScaleMultiplier(CurrentScaleMultiplier() + delta * 0.25f);
            });

        // Turn mode + degrees.
        var turnRow = Row();
        Label(turnRow, "Turning", 16f, flexible: true);
        CycleButton(turnRow, 86f,
            () => ComfortSettings.IsBound ? ComfortSettings.Turn.Value.ToString() : "-",
            () =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.Turn.Value = (TurnMode)(((int)ComfortSettings.Turn.Value + 1) % 3);
            });
        MiniStepper(turnRow,
            () => ComfortSettings.IsBound ? $"{ComfortSettings.SnapTurnDegrees.Value:0}°" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.SnapTurnDegrees.Value =
                    Mathf.Clamp(ComfortSettings.SnapTurnDegrees.Value + delta * 15f, 15f, 90f);
            });

        Toggle("Seated mode",
            () => ComfortSettings.IsBound && ComfortSettings.SeatedMode.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.SeatedMode.Value = v; });

        Stepper("Table height",
            () => ComfortSettings.IsBound ? $"{ComfortSettings.TableHeightOffset.Value:+0.00;-0.00;0.00}m" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.TableHeightOffset.Value =
                    Mathf.Clamp(ComfortSettings.TableHeightOffset.Value + delta * 0.05f, -0.4f, 0.6f);
            });

        var vignetteRow = Row();
        Label(vignetteRow, "Vignette", 16f, flexible: true);
        ToggleButton(vignetteRow,
            () => ComfortSettings.IsBound && ComfortSettings.VignetteEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.VignetteEnabled.Value = v; });
        MiniStepper(vignetteRow,
            () => ComfortSettings.IsBound ? $"{ComfortSettings.VignetteStrength.Value:0.00}" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.VignetteStrength.Value =
                    Mathf.Clamp(ComfortSettings.VignetteStrength.Value + delta * 0.1f, 0.2f, 1f);
            });

        Toggle("Free movement",
            () => ComfortSettings.IsBound && ComfortSettings.FreeMovement.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.FreeMovement.Value = v; });

        var grabRow = Row();
        Label(grabRow, "World grab", 16f, flexible: true);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.WorldGrabEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.WorldGrabEnabled.Value = v; });
        Label(grabRow, "rot", 13f);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.RotateEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.RotateEnabled.Value = v; });
        Label(grabRow, "scl", 13f);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.ScaleEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.ScaleEnabled.Value = v; });

        var recenterRow = Row();
        Button(recenterRow, "Recenter now", 0f, Comfort.RequestRecenter, flexible: true);

        Section("Modules");

        Toggle("Dominant hand right",
            () => !string.Equals(Plugin.PrimaryHand.Value, "Left", StringComparison.OrdinalIgnoreCase),
            v => Plugin.PrimaryHand.Value = v ? "Right" : "Left");

        Toggle("Board: far ray only",
            () => BoardConfigSafe(() => Board.BoardConfig.ForceFarMode.Value),
            v => { if (Board.BoardConfig.ForceFarMode != null) Board.BoardConfig.ForceFarMode.Value = v; });

        Toggle("World UI surfaces",
            () => WorldUIConfig.Master.Value,
            v => WorldUIConfig.Master.Value = v);

        Toggle("Disable post-processing*",
            () => Plugin.DisablePostProcessing.Value,
            v => Plugin.DisablePostProcessing.Value = v);

        var note = Row(22f);
        Label(note, "* applies on next VR start", 12f, flexible: true);

        // Mod layer in VR (inline 5s remain the dev-sim fallback; CAMERA-POLICY §2).
        VRLayers.Apply(_root);
        _root.SetActive(false);
    }

    private static bool BoardConfigSafe(Func<bool> read)
    {
        try
        {
            return Board.BoardConfig.ForceFarMode != null && read();
        }
        catch
        {
            return false;
        }
    }

    private static float CurrentScaleMultiplier()
    {
        Transform? rig = RigTarget.Current;
        float baseScale = RigTarget.BaseScale;
        if (rig == null || baseScale <= 0f)
            return ComfortSettings.IsBound ? ComfortSettings.SavedScaleMultiplier.Value : 1f;
        return rig.localScale.x / baseScale;
    }

    private void RefreshAll()
    {
        for (int i = 0; i < _refreshers.Count; i++)
            _refreshers[i]();
    }

    // ---- widget builders --------------------------------------------------------------------

    private RectTransform Row(float height = RowHeightPx)
    {
        var go = new GameObject("Row") { layer = 5 };
        var rect = go.AddComponent<RectTransform>();
        go.transform.SetParent(_root!.transform, worldPositionStays: false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        var el = go.AddComponent<LayoutElement>();
        el.preferredHeight = height;
        el.minHeight = height;
        return rect;
    }

    private void Section(string title)
    {
        var row = Row(24f);
        Label(row, $"— {title} —", 14f, bold: true, flexible: true, center: true);
    }

    private TextMeshProUGUI Label(RectTransform row, string text, float size,
        bool bold = false, bool flexible = false, bool center = false)
    {
        var go = new GameObject("Label") { layer = 5 };
        go.transform.SetParent(row, worldPositionStays: false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.92f, 0.9f, 0.85f);
        tmp.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(tmp);
        var el = go.AddComponent<LayoutElement>();
        if (flexible)
            el.flexibleWidth = 1f;
        else
            el.preferredWidth = Mathf.Max(28f, text.Length * size * 0.55f);
        return tmp;
    }

    private (Button button, TextMeshProUGUI label) Button(RectTransform row, string text, float width,
        Action onClick, bool flexible = false)
    {
        var go = new GameObject("Button") { layer = 5 };
        go.transform.SetParent(row, worldPositionStays: false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.22f, 0.24f, 0.32f, 0.95f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => Safe(onClick));
        var el = go.AddComponent<LayoutElement>();
        if (flexible)
            el.flexibleWidth = 1f;
        else
            el.preferredWidth = width;

        var textGo = new GameObject("Text") { layer = 5 };
        textGo.transform.SetParent(go.transform, worldPositionStays: false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 15f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(tmp);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return (button, tmp);
    }

    /// <summary>Label ..... [-] value [+]</summary>
    private void Stepper(string label, Func<string> read, Action<int> step)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row, read, step);
    }

    /// <summary>[-] value [+] appended to an existing row.</summary>
    private void MiniStepper(RectTransform row, Func<string> read, Action<int> step)
    {
        Button(row, "-", 36f, () => { step(-1); RefreshAll(); });
        TextMeshProUGUI value = Label(row, read(), 15f, center: true);
        value.GetComponent<LayoutElement>().preferredWidth = 64f;
        Button(row, "+", 36f, () => { step(+1); RefreshAll(); });
        _refreshers.Add(() => value.text = read());
    }

    /// <summary>Label ..... [On/Off]</summary>
    private void Toggle(string label, Func<bool> read, Action<bool> write)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, read, write);
    }

    private void ToggleButton(RectTransform row, Func<bool> read, Action<bool> write)
    {
        (Button button, TextMeshProUGUI text) = Button(row, read() ? "On" : "Off", 58f,
            () => { write(!read()); RefreshAll(); });
        Image image = (Image)button.targetGraphic;
        _refreshers.Add(() =>
        {
            bool on = read();
            text.text = on ? "On" : "Off";
            image.color = on ? new Color(0.20f, 0.42f, 0.26f, 0.95f) : new Color(0.30f, 0.22f, 0.22f, 0.95f);
        });
    }

    private void CycleButton(RectTransform row, float width, Func<string> read, Action advance)
    {
        (Button _, TextMeshProUGUI text) = Button(row, read(), width, () => { advance(); RefreshAll(); });
        _refreshers.Add(() => text.text = read());
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"Settings panel action threw: {ex}");
        }
    }

    // ---- gear ---------------------------------------------------------------------------------

    /// <summary>Small pokeable gear at the table edge, right of the Ready/Undo/Skip cluster.</summary>
    private sealed class GearButton : IPokeable
    {
        private GameObject _root = null!;
        private Transform _cap = null!;
        private Action _onPoke = null!;

        public static GearButton Create(Action onPoke)
        {
            var gear = new GearButton { _onPoke = onPoke };
            gear._root = new GameObject("GloomhavenVR.SettingsGear");

            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Cap";
            UnityEngine.Object.Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(gear._root.transform, worldPositionStays: false);
            cap.transform.localScale = new Vector3(0.05f, 0.007f, 0.05f);
            cap.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            cap.GetComponent<Renderer>().sharedMaterial =
                WorldUIAssets.CreateFlatMaterial(new Color(0.5f, 0.5f, 0.55f));
            gear._cap = cap.transform;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(gear._root.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            labelGo.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "SET";
            tmp.fontSize = 0.28f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(0.1f, 0.04f);
            WorldUIAssets.TryAssignGameFont(tmp);

            var collider = gear._root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.015f, 0f);
            collider.size = new Vector3(0.055f, 0.03f, 0.055f);
            VRInteractables.RegisterPokeable(gear, collider);
            VRLayers.Apply(gear._root); // pokes are registry-driven; layer is render-only
            return gear;
        }

        public void Place()
        {
            if (_root == null || !PanelLayout.TryGetPose(PanelSlot.ButtonCluster, out Vector3 pos, out Quaternion rot))
                return;
            float scale = PanelLayout.WorldScale;
            // Sit to the right of the Undo|Ready|Skip cluster (cluster is yaw-flipped
            // toward the player; +X in its flipped frame = the player's right).
            Quaternion flipped = rot * Quaternion.Euler(0f, 180f, 0f);
            _root.transform.SetPositionAndRotation(pos + flipped * new Vector3(0.22f * scale, 0f, 0f), flipped);
            _root.transform.localScale = Vector3.one * scale;

            // Cap eases back up after a press dip.
            if (_cap != null)
                _cap.localPosition = Vector3.Lerp(_cap.localPosition,
                    new Vector3(0f, 0.012f, 0f), Time.deltaTime * 8f);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }

        public void Destroy()
        {
            VRInteractables.UnregisterPokeable(this);
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }

        public void OnPokeEnter(VRHand hand) { }

        public void OnPokeExit(VRHand hand) { }

        public void OnPoke(VRHand hand)
        {
            if (_cap != null)
                _cap.localPosition = new Vector3(0f, 0.006f, 0f); // brief press dip
            _onPoke();
        }
    }
}
