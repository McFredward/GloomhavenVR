using System;
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
/// What the flight stick steers by (<c>[Comfort] FlightDirection</c>). Both options are
/// "where you are looking/pointing", the difference is which of the two the player wants to
/// aim with: the head cannot point away from what you are watching (steadier, and the usual
/// VR default), the hand can (you fly sideways while keeping your eyes on the board).
/// </summary>
internal enum FlightDirectionSource
{
    /// <summary>Fly along the HMD's forward, pitch included (default).</summary>
    Head,

    /// <summary>Fly along the DOMINANT hand's aim ray — the same ray the laser draws.</summary>
    Hand
}

/// <summary>
/// Typed accessor over one comfort config entry: read/write value + a typed change
/// event. UI binds its widgets to these — one wrapper per row — instead of touching BepInEx
/// types directly.
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
/// <c>[Comfort]</c>) owned by the Rig module. (The old <c>WorldScaleBase</c> wrapper over
/// the main config's frozen <c>[Rig] WorldScale</c> is gone with that setting — user ruling
/// 2026-08, "Tischgröße" removed; the entry itself stays bound in <c>Plugin.cs</c> as a
/// documented legacy no-op.) Bound in <see cref="RigModule.Init"/>, released in
/// <see cref="RigModule.Shutdown"/> — hot-reload clean.
///
/// The in-VR options UI (<see cref="WorldUI.VROptionsTab"/>: curated Komfort rows plus the
/// generic config browser) reads the <see cref="ComfortSetting{T}"/> wrappers and the runtime
/// ops on <see cref="Comfort"/>; nothing here depends on UI. The dependency runs one way —
/// keep it that way.
/// </summary>
internal static class ComfortSettings
{
    private const string SectionName = "Comfort";

    // Recenter preset (real meters) — the WHOLE seat definition now that nothing shifts on
    // top of it. The old seated preset is GONE (user: irrelevant — the world is freely
    // draggable), and so is the [Comfort] TableHeightOffset dial that used to add to the
    // height (user ruling 2026-08: "durch das freie Bewegen braucht man das nicht mehr" —
    // stick flight plus the world grab move the player anywhere they like, so a config dial
    // for eye height was a second, worse way to do the same thing). Recenter is now purely a
    // deterministic way BACK to the table edge; how high you sit while playing is locomotion.
    //
    // THE HEIGHT IS 0.30, NOT 0.70, AND THAT IS THE REMOVAL BEING HONEST. Retiring the dial must
    // not silently MOVE the player: the shipped default for TableHeightOffset was -0.40 — the very
    // bottom of its -0.40..+0.60 range — and the tuned cfg carried -0.40 as well, so the seat every
    // build so far actually produced was 0.70 + (-0.40) = 0.30 m. Deleting the addend and leaving
    // 0.70 behind would have raised every recenter by 40 real centimetres (times the rig scale — very
    // visible) as a side effect of a cleanup nobody asked to change the view. The tuned value is
    // folded into the constant instead, so the setting disappears and the seat does not move.
    // 0.30 m above the focus plane, 0.70 m back, is the leaning-in-over-the-board view the dial was
    // pinned to; from there stick flight and the stick-click world grab take over, which is the
    // whole point of the ruling.
    internal const float StandingEyeHeightMeters = 0.30f;
    internal const float StandingEyeBackMeters = 0.70f;

    private static ConfigFile? _file;

    /// <summary>True between <see cref="Bind"/> and <see cref="Unbind"/> — accessors are usable.</summary>
    public static bool IsBound { get; private set; }

    /// <summary>Fired with the entry key after ANY comfort setting changes (panel dirty-marking).</summary>
    public static event Action<string>? AnyChanged;

    // ---- typed accessors (the UI binding surface) ---------------------------------------

    /// <summary>Master switch for the STICK-CLICK world grab (drag/rotate/scale), <see cref="WorldGrab"/>.
    /// Not the grip: the grip has been figure-grab since P8.</summary>
    public static ComfortSetting<bool> WorldGrabEnabled { get; private set; } = null!;

    /// <summary>
    /// Fully free diorama movement (test #10, default ON): no positional clamps at all —
    /// drag the table in any direction including up/down past the head plane; only the
    /// (generously widened) scale limits remain. Off restores the pre-test-#10 comfort
    /// clamps (horizontal-only drag unless <see cref="VerticalDrag"/>, head-above-table
    /// <see cref="RigClamp"/>, configured scale range).
    /// </summary>
    public static ComfortSetting<bool> FreeMovement { get; private set; } = null!;

    /// <summary>Allow the one-hand drag to move the table vertically (always on while <see cref="FreeMovement"/>).</summary>
    public static ComfortSetting<bool> VerticalDrag { get; private set; } = null!;

    /// <summary>Two-hand (both sticks clicked) yaw rotation of the table.</summary>
    public static ComfortSetting<bool> RotateEnabled { get; private set; } = null!;

    /// <summary>Two-hand (both sticks clicked) pinch scale of the table.</summary>
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

    /// <summary>Stick flight: push the flight hand's stick forward to fly. See <see cref="Flight"/>.</summary>
    public static ComfortSetting<bool> FlightEnabled { get; private set; } = null!;

    /// <summary>What flight steers by: the head's forward, or the dominant hand's aim ray.</summary>
    public static ComfortSetting<FlightDirectionSource> FlightDirection { get; private set; } = null!;

    /// <summary>Speed at full stick deflection, in APPARENT meters/second (see <see cref="Flight"/>).</summary>
    public static ComfortSetting<float> FlightMaxSpeed { get; private set; } = null!;

    /// <summary>Which hand's stick flies (Dominant follows <c>[Hands] PrimaryHand</c>).</summary>
    public static ComfortSetting<TurnHandChoice> FlightHand { get; private set; } = null!;

    /// <summary>The TURN stick's forward axis as world up/down travel at <see cref="FlightMaxSpeed"/>.
    /// Off by default; the axis rule and the arbitration live in <see cref="Flight"/>.</summary>
    public static ComfortSetting<bool> TurnStickVertical { get; private set; } = null!;

    // TableHeightOffset ("Tischhöhe") lived here. REMOVED — user ruling 2026-08, see the
    // preset comment above: free locomotion replaced it. Do not re-add a height dial; the
    // answer to "the table sits wrong for me" is stick flight and the stick-click world grab,
    // not a slider.

    /// <summary>Hold B+Y on both hands this long to recenter (0 disables the chord).</summary>
    public static ComfortSetting<float> RecenterHoldSeconds { get; private set; } = null!;

    /// <summary>Last pinch-scale multiplier (persisted automatically after each two-hand gesture).</summary>
    public static ComfortSetting<float> SavedScaleMultiplier { get; private set; } = null!;

    /// <summary>Comfort debug overlay (world-grab state, scale, clamps).</summary>
    public static ComfortSetting<bool> DebugGizmos { get; private set; } = null!;

    /// <summary>Compensate a runtime re-origin (typical after an HMD doff/don) so the player stays
    /// where they were — see <c>VRRigDriver.TickOriginGuard</c>.</summary>
    public static ComfortSetting<bool> KeepPlaceOnReorigin { get; private set; } = null!;

    // ---- derived helpers -----------------------------------------------------------------

    /// <summary>Guaranteed scale floor/ceiling while <see cref="FreeMovement"/> is on (test #10).</summary>
    internal const float FreeScaleMinMultiplier = 0.1f;
    internal const float FreeScaleMaxMultiplier = 12f;

    /// <summary>Vertical drag policy: always allowed while <see cref="FreeMovement"/> (test #10).</summary>
    internal static bool EffectiveVerticalDrag =>
        IsBound && (FreeMovement.Value || VerticalDrag.Value);

    /// <summary>True while ALL positional clamps are disabled (test #10 free movement).</summary>
    internal static bool PositionalClampsDisabled =>
        IsBound && FreeMovement.Value;

    /// <summary>
    /// Effective lower scale clamp (multiplier of base WorldScale). While FreeMovement is
    /// on the range is AT LEAST 0.1×–12× regardless of the persisted ScaleMin/ScaleMax
    /// values (existing configs keep their old numbers on disk — the guarantee must not
    /// depend on them).
    /// </summary>
    internal static float EffectiveScaleMin =>
        !IsBound ? 1f : FreeMovement.Value ? Mathf.Min(ScaleMin.Value, FreeScaleMinMultiplier) : ScaleMin.Value;

    /// <summary>Effective upper scale clamp (multiplier of base WorldScale). See <see cref="EffectiveScaleMin"/>.</summary>
    internal static float EffectiveScaleMax =>
        !IsBound ? 1f : FreeMovement.Value ? Mathf.Max(ScaleMax.Value, FreeScaleMaxMultiplier) : ScaleMax.Value;

    /// <summary>
    /// Recenter eye height above the table plane, real meters — the standing preset, full stop.
    /// It used to be "preset + [Comfort] TableHeightOffset"; with that dial removed there is
    /// nothing left to add, and the pair with <see cref="EffectiveEyeBackMeters"/> is kept as a
    /// pair so the two halves of the seat are still read from one place.
    /// </summary>
    internal static float EffectiveEyeHeightMeters => StandingEyeHeightMeters;

    /// <summary>Recenter eye distance back from the table focus, real meters (standing preset).</summary>
    internal static float EffectiveEyeBackMeters => StandingEyeBackMeters;

    /// <summary>Persisted scale multiplier, clamped into the effective limits.</summary>
    internal static float ClampedSavedMultiplier =>
        !IsBound ? 1f : Mathf.Clamp(SavedScaleMultiplier.Value, EffectiveScaleMin, EffectiveScaleMax);

    /// <summary>Persist the scale multiplier after a gesture (no-op for sub-1% changes).</summary>
    internal static void PersistScaleMultiplier(float multiplier)
    {
        if (!IsBound)
            return;
        multiplier = Mathf.Clamp(multiplier, EffectiveScaleMin, EffectiveScaleMax);
        if (Mathf.Abs(multiplier - SavedScaleMultiplier.Value) > 0.01f)
            SavedScaleMultiplier.Value = multiplier;
    }

    // ---- lifecycle (RigModule only) --------------------------------------------------------

    internal static void Bind()
    {
        if (IsBound)
            return;

        // Same file, same saveOnInit — but routed through the canonical factory so the comfort
        // entries land in the ModuleConfig registry and are therefore reachable from the in-VR
        // config browser (Debug ▸ Alle Einstellungen) like every other module's.
        _file = Core.ModuleConfig.Create("comfort");

        WorldGrabEnabled = Bind("WorldGrabEnabled", Defaults.WorldGrabEnabled,
            "Grip-based table manipulation: one grip (away from grabbable objects) drags the " +
            "table, two grips rotate and pinch-scale it. Moves only the VR rig, never the game world.");
        FreeMovement = Bind("FreeMovement", Defaults.FreeMovement,
            "Fully free diorama movement: the one-grip drag moves the table in ANY direction " +
            "(including straight up/down, no head-above-table clamp, no positional limits at " +
            "all) and the pinch-scale range is at least 0.1x-12x of the base scale. Disable to " +
            "restore the old comfort clamps (horizontal drag unless VerticalDrag, head kept " +
            "above the table, configured ScaleMin/ScaleMax). Recenter (B+Y hold) always " +
            "returns to the table edge from anywhere.");
        VerticalDrag = Bind("VerticalDrag", Defaults.VerticalDrag,
            "Allow the one-grip drag to also move the table vertically. Off = horizontal plane " +
            "only. Ignored (always on) while FreeMovement is enabled.");
        RotateEnabled = Bind("RotateEnabled", Defaults.RotateEnabled,
            "Two-grip gesture rotates the table around the point between your hands (yaw only).");
        ScaleEnabled = Bind("ScaleEnabled", Defaults.ScaleEnabled,
            "Two-grip pinch scales the table (spread hands = board grows).");
        ScaleMin = Bind("ScaleMin", Defaults.ScaleMin,
            "Lower pinch-scale clamp as a multiplier of the base WorldScale. While FreeMovement " +
            "is on, the effective floor is at most 0.1x regardless of this value.",
            new AcceptableValueRange<float>(0.02f, 1f));
        ScaleMax = Bind("ScaleMax", Defaults.ScaleMax,
            "Upper pinch-scale clamp as a multiplier of the base WorldScale. While FreeMovement " +
            "is on, the effective ceiling is at least 12x regardless of this value.",
            new AcceptableValueRange<float>(1f, 20f));
        Turn = Bind("TurnMode", Defaults.Comfort_TurnMode,
            "Thumbstick turning: Snap = discrete steps, Smooth = continuous, Off = disabled. " +
            "Never active in board-targeting mode (the stick rotates AoE patterns there).");
        SnapTurnDegrees = Bind("SnapTurnDegrees", Defaults.SnapTurnDegrees,
            "Yaw degrees per snap-turn step (30 or 45 typical).",
            new AcceptableValueRange<float>(15f, 90f));
        SmoothTurnSpeed = Bind("SmoothTurnSpeed", Defaults.SmoothTurnSpeed,
            "Smooth-turn speed in degrees per second.",
            new AcceptableValueRange<float>(30f, 270f));
        TurnHand = Bind("TurnHand", Defaults.TurnHand,
            "Which thumbstick turns. Dominant follows [Hands] PrimaryHand.");
        FlightEnabled = Bind("FlightEnabled", Defaults.FlightEnabled,
            "Stick flight: push the flight hand's thumbstick forward to fly through the scene, back " +
            "to fly backwards, sideways to strafe level left/right. Off = that stick does nothing, " +
            "exactly as before this feature existed. Turning also reads the SIDEWAYS axis, so when " +
            "flight and turning are set to the same hand, turning keeps that axis and strafe stands " +
            "down; forward/backward flight always works. With the shipped defaults they are on " +
            "different hands (turn right, fly left) and both work at once.");
        FlightDirection = Bind("FlightDirection", Defaults.FlightDirection,
            "What flight steers by. Head = the HMD's forward, pitch included, so you fly where you " +
            "look. Hand = the dominant hand's aim ray - the same ray the laser draws - so you can " +
            "fly one way while looking another.");
        // MAXIMUM 3 (user, hardware ModBuild 137, finding 12: "Maximale Fluggeschwindigkeit auf 3
        // setzen. (Also voller Balken = 3)").
        //
        // ROOT CAUSE of the complaint: the declared range was 0.2..20, and a DECLARED RANGE IS THE
        // SLIDER — VROptionsTab.2.Rows.BuildSliderRow feeds item.Min/item.Max straight into
        // Slider.minValue/maxValue (VROptionsTab.2.Rows.cs:931-932), the row being a slider at all
        // is decided by ConfigCatalog's HasRange (ConfigCatalog.cs:575, VROptionsTab.2.Rows.cs:813).
        // So nineteen twentieths of the bar's travel sat above anything flyable, and his tuned
        // 1.43362 lived in the first 7 % of it: the dial was unusable at exactly the resolution it
        // needed. Full bar now means 3 apparent m/s, and his value sits at 45 % of the travel.
        //
        // The DEFAULT is deliberately untouched at 1.43362 — it is his own tuned value, taken over
        // verbatim from the cfg drop in ModBuild 137 (standing rule: a dropped cfg is always against
        // the newest build, so it is never re-expressed). It is inside the new range, so no config
        // file is silently rewritten by BepInEx's range clamp on load. Only the CEILING moved.
        //
        // The floor stays 0.2: it is the deflection below which flight cannot cross a hex in a
        // reasonable push, and the report asked about the maximum only.
        //
        // REJECTED: an explicit ConfigSteps entry to go with the narrower range. The row is a
        // slider, not a stepper — the arrow-button step is not even read on this path
        // (BuildSliderRow only uses Min/Max and wholeNumbers) — and ConfigSteps.cs is another
        // lane's file. Nothing about the step needs to change for this report.
        FlightMaxSpeed = Bind("FlightMaxSpeed", Defaults.FlightMaxSpeed,
            "Flight speed at FULL stick deflection, in apparent meters per second - i.e. meters as " +
            "the diorama looks to you, not world units. Zooming the table therefore never changes " +
            "how fast flying feels. Partial deflection is squared, so small pushes creep and the " +
            "full push is exactly this value. Maximum 3 (user ruling 2026-08-13).",
            new AcceptableValueRange<float>(0.2f, 3f));
        FlightHand = Bind("FlightHand", Defaults.FlightHand,
            "Which thumbstick flies. Dominant follows [Hands] PrimaryHand.");
        // OFF BY DEFAULT, and that is the request itself rather than caution added on top: the user
        // asked to be able to SET it ("Ich will es auch Optional einstelbar machen"). Its off state
        // is exactly today's behaviour, so nothing about the flow depends on it, and uncommanded
        // vertical motion is the one nausea risk in this feature.
        TurnStickVertical = Bind("TurnStickVertical", Defaults.TurnStickVertical,
            "Push the TURN stick forward to rise and back to sink, straight up and down, at the " +
            "same speed as FlightMaxSpeed. Turning keeps the sideways axis and is never blocked by " +
            "this: a push has to be clearly more vertical than sideways (about 56 degrees) before " +
            "it lifts at all, so a diagonal push at 45 degrees is a pure turn. Needs the turn hand " +
            "and the flight hand to be DIFFERENT hands - with both on one controller, forward and " +
            "backward flight already owns that axis and keeps it, and this does nothing.");
        // "TableHeightOffset" was bound here. REMOVED (user ruling 2026-08). BepInEx keeps the
        // orphaned line in an existing comfort.cfg until the file is rewritten; it binds to
        // nothing and does nothing, which is the intended outcome — no migration is needed for
        // a setting whose whole effect was an addend that no longer exists.
        RecenterHoldSeconds = Bind("RecenterHoldSeconds", Defaults.RecenterHoldSeconds,
            "Hold the upper face button (B + Y) on BOTH controllers this many seconds to " +
            "recenter at the table. 0 disables the chord.",
            new AcceptableValueRange<float>(0f, 5f));
        SavedScaleMultiplier = Bind("SavedScaleMultiplier", Defaults.SavedScaleMultiplier,
            "Last pinch-scale multiplier relative to the base WorldScale (the 'table scale' " +
            "shown in the in-VR settings panel). Written automatically after each two-grip " +
            "scale gesture and re-applied when the rig is rebuilt. Default 2.5 — the auto base " +
            "scale reads as a huge diorama; 2.5x shrinks it to a comfortable table size at " +
            "first spawn (user request: default table scale ~2.5).");
        DebugGizmos = Bind("DebugGizmos", Defaults.DebugGizmos,
            "Show the comfort debug overlay (world-grab state, scale multiplier, clamp status).");
        KeepPlaceOnReorigin = Bind("KeepPlaceOnReorigin", Defaults.KeepPlaceOnReorigin,
            "Stay where you were when the VR runtime moves its tracking origin under the game — " +
            "which is what usually happens when you take the headset off and put it back on, and " +
            "why you can end up standing somewhere else. The mod spots the origin change (the " +
            "head jumps further in ONE frame than a neck can move), waits a few frames to be sure " +
            "it was not a tracking blip, and shifts the rig by the same amount in reverse, so your " +
            "head ends up back at the exact spot and facing you had. Nothing world-anchored is " +
            "moved: you did not actually go anywhere. Off = the runtime's origin wins (old " +
            "behaviour).");

        // ONE-TIME defaults migration (user request: default table scale 2.5): BepInEx keeps
        // the values saved in an existing config file, so changing the declared default above
        // only affects FRESH installs. For existing files, adopt the new default ONLY when the
        // user never touched the old one (saved value == old default 1.0 — the multiplier is
        // written after every pinch gesture, so an exact 1.0 means it was never adjusted, or
        // was deliberately stepped back to the old default, which the marker below respects
        // from now on). The marker makes this migration run at most once per config file.
        ConfigEntry<bool> tableScaleMigrated = _file.Bind(SectionName, "TableScaleDefault25Applied",
            Defaults.TableScaleDefault25Applied,
            "Internal one-time migration marker: the 2.5x default table scale has been " +
            "offered to this config file. Do not edit.");
        if (!tableScaleMigrated.Value)
        {
            if (Mathf.Approximately(SavedScaleMultiplier.Value, 1f))
            {
                SavedScaleMultiplier.Value = 2.5f;
                Core.VRLog.Info("Comfort", "One-time migration: SavedScaleMultiplier was at the old " +
                                           "default 1.0 (never adjusted) — adopted the new default 2.5 " +
                                           "(table scale ~2.5 at first spawn).");
            }
            tableScaleMigrated.Value = true;
        }

        _file.SettingChanged += OnFileSettingChanged;

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

        WorldGrabEnabled.Detach();
        FreeMovement.Detach();
        VerticalDrag.Detach();
        RotateEnabled.Detach();
        ScaleEnabled.Detach();
        ScaleMin.Detach();
        ScaleMax.Detach();
        Turn.Detach();
        SnapTurnDegrees.Detach();
        SmoothTurnSpeed.Detach();
        TurnHand.Detach();
        FlightEnabled.Detach();
        FlightDirection.Detach();
        FlightMaxSpeed.Detach();
        FlightHand.Detach();
        TurnStickVertical.Detach();
        RecenterHoldSeconds.Detach();
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

}
