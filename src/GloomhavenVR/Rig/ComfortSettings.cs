using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>Thumbstick turn behavior (<c>[Comfort] TurnMode</c>).</summary>
internal enum TurnMode
{
    /// <summary>No stick-based turning.</summary>
    Off,

    /// <summary>Discrete yaw steps on stick flick (default).</summary>
    Snap,

    /// <summary>Continuous yaw while the stick is deflected.</summary>
    Smooth
}

/// <summary>Which hand's thumbstick drives turning (<c>[Comfort] TurnHand</c>).</summary>
internal enum TurnHandChoice
{
    /// <summary>Follow <c>[Hands] PrimaryHand</c> (default).</summary>
    Dominant,
    Left,
    Right
}

/// <summary>
/// Typed accessor over one comfort config entry: read/write value + a typed change
/// event. The future P3c in-VR settings panel binds its widgets to these — one
/// wrapper per row — instead of touching BepInEx types directly.
/// </summary>
internal sealed class ComfortSetting<T>
{
    private readonly ConfigEntry<T> _entry;
    private readonly EventHandler _handler;

    internal ComfortSetting(ConfigEntry<T> entry)
    {
        _entry = entry;
        _handler = OnSettingChanged;
        _entry.SettingChanged += _handler;
    }

    /// <summary>Current value. Setting it persists to disk (BepInEx saves on set).</summary>
    public T Value
    {
        get => _entry.Value;
        set => _entry.Value = value;
    }

    /// <summary>Config-declared default.</summary>
    public T DefaultValue => (T)_entry.DefaultValue;

    public string Section => _entry.Definition.Section;
    public string Key => _entry.Definition.Key;
    public string Description => _entry.Description.Description;

    /// <summary>
    /// Underlying BepInEx entry (for acceptable-value ranges when building sliders).
    /// Prefer <see cref="Value"/>/<see cref="Changed"/> for everything else.
    /// </summary>
    public ConfigEntry<T> Entry => _entry;

    /// <summary>Fired with the new value after the entry changes (any writer: us, panel, file reload).</summary>
    public event Action<T>? Changed;

    public void Reset() => _entry.Value = (T)_entry.DefaultValue;

    private void OnSettingChanged(object sender, EventArgs args)
    {
        try
        {
            Changed?.Invoke(_entry.Value);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Comfort", $"Setting '{Key}' change subscriber threw: {ex}");
        }
    }

    internal void Detach()
    {
        _entry.SettingChanged -= _handler;
        Changed = null;
    }
}

/// <summary>
/// Phase-4 comfort configuration (docs/INTERFACES-P4.md). All entries live in a
/// dedicated config file (<c>BepInEx/config/dev.gloomhavenvr.comfort.cfg</c>, section
/// <c>[Comfort]</c>) owned by the Rig module — the main plugin config
/// (<c>Plugin.cs</c>) is frozen shared surface and is only *wrapped* here
/// (<see cref="WorldScaleBase"/>). Bound in <see cref="RigModule.Init"/>, released in
/// <see cref="RigModule.Shutdown"/> — hot-reload clean.
///
/// The deferred P3c in-VR settings panel binds to the <see cref="ComfortSetting{T}"/>
/// wrappers and the runtime ops on <see cref="Comfort"/>; nothing here depends on UI.
/// </summary>
internal static class ComfortSettings
{
    private const string SectionName = "Comfort";

    // Recenter presets (real meters). TableHeightOffset shifts on top of the preset.
    internal const float StandingEyeHeightMeters = 0.70f;
    internal const float StandingEyeBackMeters = 0.70f;
    internal const float SeatedEyeHeightMeters = 0.50f;
    internal const float SeatedEyeBackMeters = 0.55f;

    private static ConfigFile? _file;

    /// <summary>True between <see cref="Bind"/> and <see cref="Unbind"/> — accessors are usable.</summary>
    public static bool IsBound { get; private set; }

    /// <summary>Fired with the entry key after ANY comfort setting changes (panel dirty-marking).</summary>
    public static event Action<string>? AnyChanged;

    // ---- typed accessors (the P3c panel binding surface) --------------------------------

    /// <summary>Base diorama scale (wraps the frozen <c>[Rig] WorldScale</c> entry, 0 = auto). Rig rebuild required.</summary>
    public static ComfortSetting<float> WorldScaleBase { get; private set; } = null!;

    /// <summary>Master switch for grip-based world grab (drag/rotate/scale).</summary>
    public static ComfortSetting<bool> WorldGrabEnabled { get; private set; } = null!;

    /// <summary>Allow one-grip drag to move the table vertically (default: horizontal plane only).</summary>
    public static ComfortSetting<bool> VerticalDrag { get; private set; } = null!;

    /// <summary>Two-grip yaw rotation of the table.</summary>
    public static ComfortSetting<bool> RotateEnabled { get; private set; } = null!;

    /// <summary>Two-grip pinch scale of the table.</summary>
    public static ComfortSetting<bool> ScaleEnabled { get; private set; } = null!;

    /// <summary>Lower scale clamp, as a multiplier of the base WorldScale.</summary>
    public static ComfortSetting<float> ScaleMin { get; private set; } = null!;

    /// <summary>Upper scale clamp, as a multiplier of the base WorldScale.</summary>
    public static ComfortSetting<float> ScaleMax { get; private set; } = null!;

    /// <summary>Thumbstick turn behavior: Off / Snap / Smooth.</summary>
    public static ComfortSetting<TurnMode> Turn { get; private set; } = null!;

    /// <summary>Degrees per snap step.</summary>
    public static ComfortSetting<float> SnapTurnDegrees { get; private set; } = null!;

    /// <summary>Smooth-turn speed in degrees/second.</summary>
    public static ComfortSetting<float> SmoothTurnSpeed { get; private set; } = null!;

    /// <summary>Which hand's stick turns (Dominant follows <c>[Hands] PrimaryHand</c>).</summary>
    public static ComfortSetting<TurnHandChoice> TurnHand { get; private set; } = null!;

    /// <summary>Seated preset: recenter puts the eyes lower and closer to the table.</summary>
    public static ComfortSetting<bool> SeatedMode { get; private set; } = null!;

    /// <summary>Extra eye height above the table on recenter, real meters (+ = table lower).</summary>
    public static ComfortSetting<float> TableHeightOffset { get; private set; } = null!;

    /// <summary>Hold B+Y on both hands this long to recenter (0 disables the chord).</summary>
    public static ComfortSetting<float> RecenterHoldSeconds { get; private set; } = null!;

    /// <summary>Radial comfort vignette during world grab / turns (default off — stationary tabletop game).</summary>
    public static ComfortSetting<bool> VignetteEnabled { get; private set; } = null!;

    /// <summary>Peak vignette opacity 0..1.</summary>
    public static ComfortSetting<float> VignetteStrength { get; private set; } = null!;

    /// <summary>Last pinch-scale multiplier (persisted automatically after each two-grip gesture).</summary>
    public static ComfortSetting<float> SavedScaleMultiplier { get; private set; } = null!;

    /// <summary>Comfort debug overlay (world-grab state, scale, clamps).</summary>
    public static ComfortSetting<bool> DebugGizmos { get; private set; } = null!;

    // ---- derived helpers -----------------------------------------------------------------

    /// <summary>Recenter eye height above the table plane, real meters (preset + offset).</summary>
    internal static float EffectiveEyeHeightMeters =>
        !IsBound
            ? StandingEyeHeightMeters
            : (SeatedMode.Value ? SeatedEyeHeightMeters : StandingEyeHeightMeters) + TableHeightOffset.Value;

    /// <summary>Recenter eye distance back from the table focus, real meters.</summary>
    internal static float EffectiveEyeBackMeters =>
        !IsBound
            ? StandingEyeBackMeters
            : SeatedMode.Value ? SeatedEyeBackMeters : StandingEyeBackMeters;

    /// <summary>Persisted scale multiplier, clamped into the configured limits.</summary>
    internal static float ClampedSavedMultiplier =>
        !IsBound ? 1f : Mathf.Clamp(SavedScaleMultiplier.Value, ScaleMin.Value, ScaleMax.Value);

    /// <summary>Persist the scale multiplier after a gesture (no-op for sub-1% changes).</summary>
    internal static void PersistScaleMultiplier(float multiplier)
    {
        if (!IsBound)
            return;
        multiplier = Mathf.Clamp(multiplier, ScaleMin.Value, ScaleMax.Value);
        if (Mathf.Abs(multiplier - SavedScaleMultiplier.Value) > 0.01f)
            SavedScaleMultiplier.Value = multiplier;
    }

    // ---- lifecycle (RigModule only) --------------------------------------------------------

    internal static void Bind()
    {
        if (IsBound)
            return;

        _file = new ConfigFile(Path.Combine(Paths.ConfigPath, "dev.gloomhavenvr.comfort.cfg"), true);

        // Frozen shared entry from Plugin.cs, wrapped so the panel has ONE binding surface.
        WorldScaleBase = new ComfortSetting<float>(Plugin.WorldScale);

        WorldGrabEnabled = Bind("WorldGrabEnabled", true,
            "Grip-based table manipulation: one grip (away from grabbable objects) drags the " +
            "table, two grips rotate and pinch-scale it. Moves only the VR rig, never the game world.");
        VerticalDrag = Bind("VerticalDrag", false,
            "Allow the one-grip drag to also move the table vertically. Off = horizontal plane only.");
        RotateEnabled = Bind("RotateEnabled", true,
            "Two-grip gesture rotates the table around the point between your hands (yaw only).");
        ScaleEnabled = Bind("ScaleEnabled", true,
            "Two-grip pinch scales the table (spread hands = board grows).");
        ScaleMin = Bind("ScaleMin", 0.5f,
            "Lower pinch-scale clamp as a multiplier of the base WorldScale.",
            new AcceptableValueRange<float>(0.1f, 1f));
        ScaleMax = Bind("ScaleMax", 4f,
            "Upper pinch-scale clamp as a multiplier of the base WorldScale.",
            new AcceptableValueRange<float>(1f, 10f));
        Turn = Bind("TurnMode", TurnMode.Snap,
            "Thumbstick turning: Snap = discrete steps, Smooth = continuous, Off = disabled. " +
            "Never active in board-targeting mode (the stick rotates AoE patterns there).");
        SnapTurnDegrees = Bind("SnapTurnDegrees", 45f,
            "Yaw degrees per snap-turn step (30 or 45 typical).",
            new AcceptableValueRange<float>(15f, 90f));
        SmoothTurnSpeed = Bind("SmoothTurnSpeed", 90f,
            "Smooth-turn speed in degrees per second.",
            new AcceptableValueRange<float>(30f, 270f));
        TurnHand = Bind("TurnHand", TurnHandChoice.Dominant,
            "Which thumbstick turns. Dominant follows [Hands] PrimaryHand.");
        SeatedMode = Bind("SeatedMode", false,
            "Seated preset: recenter places your eyes lower and closer to the table edge. " +
            "Changing this re-runs recenter immediately.");
        TableHeightOffset = Bind("TableHeightOffset", 0f,
            "Extra eye height above the table on recenter, in real meters (positive = table " +
            "sits lower). Changing this re-runs recenter immediately.",
            new AcceptableValueRange<float>(-0.4f, 0.6f));
        RecenterHoldSeconds = Bind("RecenterHoldSeconds", 1.0f,
            "Hold the upper face button (B + Y) on BOTH controllers this many seconds to " +
            "recenter at the table. 0 disables the chord.",
            new AcceptableValueRange<float>(0f, 5f));
        VignetteEnabled = Bind("VignetteEnabled", false,
            "Fade in a radial vignette while the table is being dragged/rotated/scaled or " +
            "during stick turns. Default off — the diorama is stationary and rarely needs it.");
        VignetteStrength = Bind("VignetteStrength", 0.85f,
            "Peak vignette opacity (0..1).",
            new AcceptableValueRange<float>(0.2f, 1f));
        SavedScaleMultiplier = Bind("SavedScaleMultiplier", 1f,
            "Last pinch-scale multiplier relative to the base WorldScale. Written automatically " +
            "after each two-grip scale gesture and re-applied when the rig is rebuilt.");
        DebugGizmos = Bind("DebugGizmos", false,
            "Show the comfort debug overlay (world-grab state, scale multiplier, clamp status).");

        _file.SettingChanged += OnFileSettingChanged;
        WorldScaleBase.Changed += OnWorldScaleBaseChanged;

        IsBound = true;
        Core.VRLog.Debug("Comfort", "Comfort settings bound (dev.gloomhavenvr.comfort.cfg).");
    }

    internal static void Unbind()
    {
        if (!IsBound)
            return;
        IsBound = false;

        if (_file != null)
            _file.SettingChanged -= OnFileSettingChanged;
        WorldScaleBase.Changed -= OnWorldScaleBaseChanged;

        WorldScaleBase.Detach();
        WorldGrabEnabled.Detach();
        VerticalDrag.Detach();
        RotateEnabled.Detach();
        ScaleEnabled.Detach();
        ScaleMin.Detach();
        ScaleMax.Detach();
        Turn.Detach();
        SnapTurnDegrees.Detach();
        SmoothTurnSpeed.Detach();
        TurnHand.Detach();
        SeatedMode.Detach();
        TableHeightOffset.Detach();
        RecenterHoldSeconds.Detach();
        VignetteEnabled.Detach();
        VignetteStrength.Detach();
        SavedScaleMultiplier.Detach();
        DebugGizmos.Detach();

        AnyChanged = null;
        _file = null;
    }

    private static ComfortSetting<T> Bind<T>(string key, T defaultValue, string description,
        AcceptableValueBase? range = null)
    {
        ConfigEntry<T> entry = _file!.Bind(SectionName, key,
            defaultValue, new ConfigDescription(description, range));
        return new ComfortSetting<T>(entry);
    }

    private static void OnFileSettingChanged(object sender, SettingChangedEventArgs args)
    {
        try
        {
            AnyChanged?.Invoke(args.ChangedSetting.Definition.Key);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Comfort", $"AnyChanged subscriber threw: {ex}");
        }
    }

    // WorldScaleBase lives in the main plugin config file, so the file-level event above
    // doesn't cover it — bridge it into AnyChanged here.
    private static void OnWorldScaleBaseChanged(float _)
    {
        try
        {
            AnyChanged?.Invoke(WorldScaleBase.Key);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Comfort", $"AnyChanged subscriber threw: {ex}");
        }
    }
}
