using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE MODEL BEHIND "Alle config einstellungen sollen im VR Menu anpassbar sein!" (user, 2026-07).
///
/// <para>WHY THIS IS A REFLECTION WALK AND NOT 400 HAND-WRITTEN ROWS. The mod binds well over four
/// hundred <see cref="ConfigEntryBase"/> across nineteen config files. A hand-written row per entry
/// is stale the week it is written, and the entry someone binds tomorrow would be unreachable from
/// inside the headset again — which is the exact complaint this answers. So the browser walks the
/// <see cref="ModuleConfig"/> registry instead: every file the mod opens is remembered there, every
/// entry bound into one of those files is enumerable, and a new entry therefore appears in the VR
/// menu with no UI change at all. The one thing that CAN drift is the topic a new entry is filed
/// under — and that degrades safely: an unmapped module lands in "Sonstiges", visible, never lost.</para>
///
/// <para>WHY IT IS NOT A DUMP (user: "Achte im VR Menu immer auf eine übersichtliche Ordnung und
/// Bedienbarkeit. AUCH im Debug Menu."). Four hundred rows in file/section order is a wall, and
/// <c>dev.gloomhavenvr.cards.cfg ▸ [Cards]</c> is a developer artifact, not a mental model. So the
/// catalog imposes two layers the raw config files do not have:</para>
/// <list type="number">
/// <item>TOPIC (<see cref="ConfigTopic"/>) — what the setting is ABOUT: movement, hands, cards,
/// buttons, panels, picture, multiplayer, measurement. Derived from (module, section) with a
/// handful of per-key overrides, so it costs one table line per module, not one per entry.</item>
/// <item>GROUP (<see cref="ConfigGroup"/>) — inside a topic, the section when the section is small
/// enough to read in one go, otherwise the key's own leading word (<c>FanArcSweepDegrees</c> →
/// "Fan", <c>BarZoomMinScale</c> → "Bar"). That second rule is what keeps a 90-entry section
/// navigable without anyone naming the clusters by hand.</item>
/// </list>
///
/// <para>COST / SAFETY. The walk is LAZY (nothing happens until the browser page is actually open),
/// built ONCE and cached, and re-built only when the registry's entry count changes — which is how
/// a module that binds late still shows up. Every loop here is bounded by a collection's own Count;
/// there is no "search until found". Reflection is used only to read the two acceptable-value
/// shapes, and only once per entry at build time, never per frame.</para>
/// </summary>
internal static class ConfigCatalog
{
    // ==========================================================================================
    //  Topics — the FIRST navigation level (what the setting is about)
    // ==========================================================================================

    /// <summary>
    /// Topic order IS the order of the chooser in the panel, and it is deliberate rather than
    /// alphabetical: measurement first (that is what a power user opens this pane for, and the
    /// [Perf] switches a hardware measurement blocks on live there), then the everyday-visible
    /// things, then the deep geometry, then the plumbing. <see cref="Other"/> is last and exists so
    /// a module nobody mapped is still reachable instead of silently invisible.
    /// </summary>
    internal enum ConfigTopic
    {
        Diagnostics,
        Visual,
        Movement,
        Hands,
        Cards,
        Buttons,
        Panels,
        BoardTargeting,
        BoardGeometry,
        Network,
        System,
        Other,
    }

    internal const int TopicCount = 12;

    /// <summary>How an entry can be edited — one control shape per case, decided once at build.</summary>
    internal enum ConfigKind
    {
        /// <summary>bool — a toggle.</summary>
        Bool,

        /// <summary>enum, or a string/number with an AcceptableValueList, or a curated string set — a cycle.</summary>
        Choice,

        /// <summary>Any numeric type — a stepper.</summary>
        Number,

        /// <summary>Vector2/3/4 or Color — an axis/channel picker plus a stepper.</summary>
        Components,

        /// <summary>Free-form text or an unsupported type — shown, never silently no-op'd.</summary>
        ReadOnly,
    }

    // ==========================================================================================
    //  Item / group model
    // ==========================================================================================

    /// <summary>One browsable config entry, with everything the UI needs precomputed.</summary>
    internal sealed class ConfigItem
    {
        internal ConfigEntryBase Entry = null!;
        internal string Module = string.Empty;
        internal string Section = string.Empty;
        internal string Key = string.Empty;

        /// <summary>
        /// What the row is called: the localized display name from the name table
        /// (<see cref="Loc.ConfigDisplayName"/> — "Fächer: Radius (m)", never a bare "Enabled"),
        /// falling back to the key with its camel humps spaced out ("CullSubmitSplit" →
        /// "Cull Submit Split") for an entry nobody named yet.
        /// </summary>
        internal string Display = string.Empty;

        internal ConfigTopic Topic;
        internal ConfigKind Kind;

        /// <summary>1 for scalars; 2/3/4 for Vector2/3/4 and Color (the per-component stepper).</summary>
        internal int Components = 1;

        /// <summary>Cycle options for <see cref="ConfigKind.Choice"/> (boxed enum members or strings).</summary>
        internal object[]? Choices;

        internal bool HasRange;
        internal double Min;
        internal double Max;

        /// <summary>Step at multiplier 1 — from the range when there is one, else a type default.</summary>
        internal double BaseStep = 1d;

        /// <summary>Integer-valued setting type (step never goes fractional).</summary>
        internal bool Integral;

        /// <summary>Verified NOT live: bound once at startup / rig build, so a change waits.</summary>
        internal bool NeedsRestart;

        /// <summary>Sort rank — lower floats to the top of its group (see <see cref="Pinned"/>).</summary>
        internal int Pin = 100;
    }

    /// <summary>A named cluster of items inside a topic — the SECOND navigation level.</summary>
    internal sealed class ConfigGroup
    {
        internal string Label = string.Empty;
        internal readonly List<ConfigItem> Items = new(16);
    }

    // ==========================================================================================
    //  Build / freshness
    // ==========================================================================================

    /// <summary>Section is split by key-prefix once it holds more than this many entries.</summary>
    private const int SectionSplitThreshold = 24;

    /// <summary>A key-prefix cluster smaller than this is folded into the topic's "Allgemein" group.</summary>
    private const int MinClusterSize = 3;

    /// <summary>Hard ceiling on groups per topic — the chooser has a fixed row pool this size.</summary>
    internal const int MaxGroupsPerTopic = 12;

    private static readonly List<ConfigGroup>[] Topics = NewTopics();

    private static bool _bound;
    private static bool _built;

    /// <summary>Registry signature the cache was built at (file count + total entry count).</summary>
    private static int _signature = -1;

    /// <summary>Total entries in the last build — shown in the pane's footer.</summary>
    internal static int TotalEntries { get; private set; }

    /// <summary>Entries whose type the browser cannot edit — shown in the pane's footer.</summary>
    internal static int ReadOnlyEntries { get; private set; }

    private static List<ConfigGroup>[] NewTopics()
    {
        var t = new List<ConfigGroup>[TopicCount];
        for (int i = 0; i < TopicCount; i++)
            t[i] = new List<ConfigGroup>(MaxGroupsPerTopic);
        return t;
    }

    /// <summary>
    /// Make sure the catalog reflects reality, cheaply. Called from the browser's own refresh (four
    /// times a second WHILE THE PAGE IS OPEN, never otherwise) and before every click: the common
    /// path is one pass over ~19 files reading an O(1) Count, and only a changed count pays for a
    /// rebuild. That is what makes a module which binds late still appear without a reopen.
    /// </summary>
    internal static void EnsureFresh()
    {
        EnsureBound();
        int sig = Signature();
        if (_built && sig == _signature)
            return;
        Rebuild(sig);
    }

    /// <summary>
    /// Drop the cache so the next <see cref="EnsureFresh"/> rebuilds it. Called on a LANGUAGE
    /// change: the group and section labels are resolved once at build time (they are read four
    /// times a second otherwise), so without this the chooser would keep the previous language's
    /// names until an entry count happened to change.
    /// </summary>
    internal static void Invalidate() => _built = false;

    private static int Signature()
    {
        KeyValuePair<string, ConfigFile>[] files = ModuleConfig.Snapshot();
        int sig = files.Length * 7919;
        for (int i = 0; i < files.Length; i++)
        {
            try
            {
                sig += ((ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>)files[i].Value).Count;
            }
            catch (Exception)
            {
                // A file that refuses to be counted must not take the whole pane down with it.
            }
        }
        return sig;
    }

    /// <summary>
    /// Force every module's bind-once to have run, so the browser shows the WHOLE config surface and
    /// not just the modules that happened to initialise this session. Each call is idempotent and
    /// each is isolated: one module that throws must not cost the others their entries. This list is
    /// the ONE place a new module has to be named — and forgetting to is not fatal, the module's
    /// entries simply appear as soon as it binds on its own.
    /// </summary>
    private static void EnsureBound()
    {
        if (_bound)
            return;
        _bound = true; // set FIRST: a throwing binder must never make this retry every refresh

        // Group labels AND the items' display names are resolved at build time, so a language
        // change must drop the cache (the doc on Invalidate). Subscribed once, here, because
        // this is the catalog's own once-per-process gate; Loc.Dispose clears the handler on
        // hot reload.
        Loc.OnChanged += Invalidate;

        Bind("comfort", Rig.ComfortSettings.Bind);
        Bind("perf", PerfConfig.Bind);
        Bind("renderquality", Rig.RenderQuality.Bind);
        Bind("stereo", StereoModeConfig.Bind);
        Bind("mixedreality", MixedReality.Bind);
        Bind("wallfade", WallFadeTuning.Bind);
        Bind("worldui", WorldUIConfig.Bind);
        Bind("stereoscreen", FlatScreenStereo.BindConfig); // rides worldui's file — bind it AFTER
        Bind("bars", ActorBars.BindConfig);
        Bind("buttons", ButtonTuning.Bind);
        Bind("cards", Cards.CardsConfig.Bind);
        Bind("board", Board.BoardConfig.Bind);
        Bind("hexhighlight", Board.HexHighlightFix.BindConfig);
        Bind("selectionready", Board.SelectionReadyHighlighter.Bind);
        Bind("figuregrab", Board.FigureGrab.FigureGrabConfig.Bind);
        Bind("hands", Hands.HandsConfig.Bind);
        Bind("net", Net.NetModule.BindConfig);
    }

    private static void Bind(string what, Action binder)
    {
        try
        {
            binder();
        }
        catch (Exception ex)
        {
            VRLog.Warn("Config", $"Config browser could not force-bind '{what}': {ex.GetType().Name} — "
                                 + "its entries appear as soon as the module binds them itself.");
        }
    }

    private static void Rebuild(int signature)
    {
        _signature = signature;
        _built = true;
        for (int i = 0; i < TopicCount; i++)
            Topics[i].Clear();
        TotalEntries = 0;
        ReadOnlyEntries = 0;
        RetiredEntries = 0;

        var items = new List<ConfigItem>(512);
        KeyValuePair<string, ConfigFile>[] files = ModuleConfig.Snapshot();
        for (int f = 0; f < files.Length; f++)
        {
            string module = files[f].Key;
            var pairs = new List<KeyValuePair<ConfigDefinition, ConfigEntryBase>>(64);
            try
            {
                // COPY, not a live enumeration: the walk below must not be invalidated by a module
                // binding an entry on another code path while we are inside the loop. Copied by
                // FOREACH rather than by the List(ICollection) constructor on purpose — ConfigFile
                // implements the dictionary interface explicitly, and its enumerator is the member
                // BepInEx itself uses, so this does not depend on its CopyTo being implemented.
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv
                         in (IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>>)files[f].Value)
                {
                    pairs.Add(kv);
                }
            }
            catch (Exception ex)
            {
                VRLog.Warn("Config", $"Config file '{module}' could not be enumerated: {ex.GetType().Name}.");
                continue;
            }

            for (int p = 0; p < pairs.Count; p++)
            {
                KeyValuePair<ConfigDefinition, ConfigEntryBase> pair = pairs[p];
                ConfigItem? item = Describe(module, pair.Key, pair.Value);
                if (item != null)
                    items.Add(item);
            }
        }

        TotalEntries = items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == ConfigKind.ReadOnly)
                ReadOnlyEntries++;
        }

        GroupAll(items);

        VRLog.Info("Config", $"In-VR config browser catalogued {TotalEntries} entries from "
                             + $"{files.Length} config files ({ReadOnlyEntries} free-text/unsupported, "
                             + $"shown read-only; {RetiredEntries} left out as retired — their own "
                             + "description says they no longer do anything).");
    }

    // ==========================================================================================
    //  Per-entry description
    // ==========================================================================================

    /// <summary>
    /// Prefixes the project already uses to mark an entry as RETIRED — it is still bound so old
    /// config files keep loading, but changing it does nothing.
    ///
    /// <para>Matched as a PREFIX, never as a substring: "does nothing" occurs in perfectly live
    /// descriptions ("[General] Enabled … the mod does nothing", "[Optimize] RemoteContentInterval
    /// … does nothing at all in single player"), and a substring test would have hidden three
    /// working settings. The prefix is the deliberate marker; the phrase is ordinary prose.</para>
    ///
    /// <para>"DEPRECATED —" joined the list in the 2026-08 naming sweep: the ~20 retired
    /// map-capture knobs ([WorldUI] Map*) carry that prefix instead of "LEGACY — no effect"
    /// and were sitting in Menüs &amp; Tafeln as twenty dead rows.</para>
    /// </summary>
    private static readonly string[] RetiredMarkers = { "LEGACY — no effect", "RESERVED —", "DEPRECATED —" };

    /// <summary>Entries left out of the catalog because they are marked retired.</summary>
    internal static int RetiredEntries { get; private set; }

    /// <summary>
    /// Entries left out of the menu for a reason their own description cannot carry, with that
    /// reason written down beside them.
    ///
    /// <para>Separate from the retirement marker on purpose: these DO still work, so claiming
    /// "LEGACY — no effect" in their description would be a lie to whoever reads the config file.
    /// They are simply unreachable in a normal install, and a control the player can move with
    /// nothing to show for it is worse than no control at all.</para>
    /// </summary>
    private static readonly Dictionary<string, string> NotOffered = new(StringComparer.Ordinal)
    {
        // Tints the PROCEDURAL hand — the capsule fallback built only when the asset bundle is
        // missing (HandVisuals.CreateHandMaterial). Any install that loads the bundle wears one of
        // the three hand models and never sees it. It is also an RRGGBB string, so the row offered
        // a left/right stepper with nothing to step.
        ["Hands/HandColor"] = "only tints the procedural fallback hand, which a normal install never shows",
        // Half of what Mixed Reality IS. Keying the camera clear without also removing the opaque
        // backdrop leaves the backdrop drawn over the key colour, so offering it as a separate
        // switch offers a way to make MR not work. Bound as an escape hatch, not as a choice.
        ["MixedReality/HideSkyMeshes"] = "part of Mixed Reality itself — MR turns it on",
        // One-time migration SOURCES (ButtonTuning.MigrateLegacy): their values were copied into
        // the live [RoundButtons]/[BoardButtons]/… entries once and are never read again, but
        // their bound description is the literal "legacy", which the prefix markers above do not
        // catch — so they showed as eight dead rows whose hover said one unexplained word.
        ["TransientButtons/OffsetX"] = "one-time migration source, consumed at first start",
        ["TransientButtons/OffsetY"] = "one-time migration source, consumed at first start",
        ["TransientButtons/Shape"] = "one-time migration source, consumed at first start",
        ["TransientButtons/CapSize"] = "one-time migration source, consumed at first start",
        ["SquareCaps/Width"] = "one-time migration source, consumed at first start",
        ["SquareCaps/Height"] = "one-time migration source, consumed at first start",
        ["SquareCaps/Depth"] = "one-time migration source, consumed at first start",
        ["SquareCaps/Travel"] = "one-time migration source, consumed at first start",
    };

    /// <summary>
    /// True when this entry's own description says it no longer does anything. Read from the
    /// ENGLISH bound description (the one the mod authored), not from the translated text, so a
    /// translation can never accidentally hide or reveal a setting.
    /// </summary>
    private static bool IsRetired(ConfigEntryBase entry)
    {
        string? text = entry.Description?.Description;
        if (string.IsNullOrEmpty(text))
            return false;
        for (int i = 0; i < RetiredMarkers.Length; i++)
        {
            if (text!.StartsWith(RetiredMarkers[i], StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static ConfigItem? Describe(string module, ConfigDefinition def, ConfigEntryBase entry)
    {
        if (entry == null)
            return null;

        // A SETTING THAT DOES NOTHING IS WORSE THAN A MISSING ONE: the player turns the dial,
        // nothing happens, and they are left doubting the mod rather than the row. The plainly
        // dead entries (WristHud twins, shared hand seats + trims, map-capture knobs, WorldScale,
        // HeldScale family, ForceFarMode, …) were DELETED outright in the 2026-08 dead-settings
        // sweep; what remains marked retired is the deliberate residue: LEGACY-SEED entries that
        // feed one-time migrations, the reserved [Rig] Experimental3DMap placeholder, and the
        // parked world tilt ([Rig] WorldTiltDegrees + the three MaskedReaim* companions, kept for
        // the documented revival). Those stay BOUND so the seeds and the parked tuning survive;
        // they simply stop being offered.
        if (IsRetired(entry))
        {
            RetiredEntries++;
            return null;
        }

        if (NotOffered.ContainsKey((def.Section ?? string.Empty) + "/" + (def.Key ?? string.Empty)))
        {
            RetiredEntries++;
            return null;
        }

        try
        {
            var item = new ConfigItem
            {
                Entry = entry,
                Module = module,
                Section = def.Section ?? string.Empty,
                Key = def.Key ?? string.Empty,
            };
            // Localized, self-explanatory name first (user round 2026-08); the spaced-out key
            // only for entries nobody named. The catalog rebuilds on a language change
            // (Loc.OnChanged → Invalidate, hooked in EnsureBound), so this follows the game.
            item.Display = Loc.ConfigDisplayName(item.Section, item.Key) ?? Spaced(item.Key);
            item.Topic = TopicOf(item.Module, item.Section, item.Key);
            item.NeedsRestart = IsStartupOnly(item.Module, item.Section, item.Key);
            item.Pin = Pinned(item.Section, item.Key);
            Classify(item);
            return item;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Config", $"Config entry {def.Section}/{def.Key} could not be described: {ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>Decide the control shape and the numeric envelope for one entry.</summary>
    private static void Classify(ConfigItem item)
    {
        Type t = item.Entry.SettingType;
        ReadAcceptable(item, out object[]? list);

        if (t == typeof(bool))
        {
            item.Kind = ConfigKind.Bool;
            return;
        }

        if (t.IsEnum)
        {
            item.Kind = ConfigKind.Choice;
            item.Choices = list ?? EnumValues(t);
            return;
        }

        if (list != null && list.Length > 1)
        {
            // A number or string constrained to a fixed set is a CYCLE, never a stepper: stepping
            // off the list would only be clamped back and would read as a dead control.
            item.Kind = ConfigKind.Choice;
            item.Choices = list;
            return;
        }

        if (t == typeof(string))
        {
            // A curated set is only offered when the CURRENT value is one of its members. A cfg
            // that was hand-edited to something outside the set (e.g. [Core] RuntimePriority as a
            // comma-separated runtime order) would otherwise be silently replaced by the first
            // option on the first press — losing a value the browser never understood. Shown
            // read-only instead, which is the honest answer for free text.
            object[]? curated = CuratedChoices(item.Section, item.Key);
            if (curated != null && IndexOf(curated, item.Entry.BoxedValue) >= 0)
            {
                item.Kind = ConfigKind.Choice;
                item.Choices = curated;
            }
            else
            {
                item.Kind = ConfigKind.ReadOnly;
            }
            return;
        }

        if (t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) || t == typeof(Color))
        {
            item.Kind = ConfigKind.Components;
            item.Components = t == typeof(Vector2) ? 2 : t == typeof(Vector3) ? 3 : 4;
            // Vectors are board-local METERS everywhere in this mod, colours are 0-1 channels.
            item.BaseStep = t == typeof(Color) ? 0.05d : 0.005d;
            item.Integral = false;
            return;
        }

        if (IsNumeric(t))
        {
            item.Kind = ConfigKind.Number;
            item.Integral = IsIntegral(t);
            item.BaseStep = ResolveStep(item);
            return;
        }

        item.Kind = ConfigKind.ReadOnly;
    }

    /// <summary>
    /// Read <c>AcceptableValueRange&lt;T&gt;</c> / <c>AcceptableValueList&lt;T&gt;</c> without
    /// knowing T. Reflection by PROPERTY NAME rather than by generic type identity: it is the same
    /// two names in every BepInEx 5.x, and a shape we do not recognise simply yields no constraint
    /// instead of throwing.
    /// </summary>
    private static void ReadAcceptable(ConfigItem item, out object[]? list)
    {
        list = null;
        AcceptableValueBase? av = item.Entry.Description?.AcceptableValues;
        if (av == null)
            return;
        Type t = av.GetType();
        try
        {
            PropertyInfo? values = t.GetProperty("AcceptableValues", BindingFlags.Public | BindingFlags.Instance);
            if (values != null && values.GetValue(av, null) is Array arr && arr.Length > 0)
            {
                var boxed = new object[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    boxed[i] = arr.GetValue(i)!;
                list = boxed;
                return;
            }

            PropertyInfo? min = t.GetProperty("MinValue", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo? max = t.GetProperty("MaxValue", BindingFlags.Public | BindingFlags.Instance);
            if (min == null || max == null)
                return;
            object? lo = min.GetValue(av, null);
            object? hi = max.GetValue(av, null);
            if (lo == null || hi == null || !IsNumeric(lo.GetType()))
                return;
            item.Min = Convert.ToDouble(lo, CultureInfo.InvariantCulture);
            item.Max = Convert.ToDouble(hi, CultureInfo.InvariantCulture);
            item.HasRange = item.Max > item.Min;
        }
        catch (Exception)
        {
            // Unknown acceptable-value shape: no constraint, the control still works.
        }
    }

    private static object[] EnumValues(Type t)
    {
        Array values = Enum.GetValues(t);
        var boxed = new object[values.Length];
        for (int i = 0; i < values.Length; i++)
            boxed[i] = values.GetValue(i)!;
        return boxed;
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(float) || t == typeof(double) || t == typeof(decimal) || IsIntegral(t);

    private static bool IsIntegral(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
        || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);

    /// <summary>|value| as a double, or 0 for anything that is not a number (never throws).</summary>
    private static double Magnitude(object? boxed)
    {
        if (boxed == null || !IsNumeric(boxed.GetType()))
            return 0d;
        try
        {
            return Math.Abs(Convert.ToDouble(boxed, CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            return 0d;
        }
    }

    /// <summary>
    /// How far one ◀ / ▶ press moves this entry, in four falling steps.
    ///
    /// <list type="number">
    /// <item><description>A step WRITTEN DOWN for this entry (<see cref="ConfigSteps"/>) — every
    /// curated everyday row, where the right step is a judgement about the setting.</description></item>
    /// <item><description>The unit named in the key — degrees step in degrees, metres in
    /// centimetres — bounded by the value's own scale ONLY where that scale means something (a lone
    /// scalar; never a coordinate, see <see cref="ConfigSteps.UnitScope"/>). This is what replaced
    /// "a hundredth of the default's magnitude", which had no answer at all for the twenty-eight
    /// entries whose default is 0 and gave the WORLD TILT a step of 0.01°.</description></item>
    /// <item><description>A fiftieth of the declared range, or of the DEFAULT's magnitude when
    /// there is no range (the default, not the current value, so the step never drifts as you
    /// tune) — the last resort, and the only one that can serve a depth-buffer epsilon of
    /// 0.0002.</description></item>
    /// </list>
    ///
    /// <para>Then two guards. A step is snapped to a 1/2/5 grid so the readout lands on round
    /// numbers, and it is capped at a quarter of the declared range — a step that crosses its own
    /// range in three presses is a choice list wearing a stepper's clothes.</para>
    /// </summary>
    private static double ResolveStep(ConfigItem item)
    {
        // A WRITTEN-DOWN STEP IS RETURNED AS WRITTEN. It is neither snapped nor capped: snap-turn's
        // 15° is deliberately off the 1/2/5 grid, and NiceStep rounded it to 20° — turning the one
        // value in the table chosen for what players actually want into one nobody asked for.
        if (ConfigSteps.TryExplicit(item.Section, item.Key, out double step))
            return step;

        // A fiftieth of the value's own scale: whatever the entry is, about fifty presses should
        // cross the span it is plausibly tuned over.
        double own = item.HasRange
            ? (item.Max - item.Min) / 50d
            : Magnitude(item.Entry.DefaultValue) / 50d;

        if (ConfigSteps.TryUnit(item.Key, out step, out ConfigSteps.UnitScope scope))
        {
            // THE UNIT GIVES THE RESOLUTION AND NOTHING ELSE, so the value's own magnitude bounds
            // it from both sides — the rule failed in both directions when it did not. Too fine:
            // [Perf] SummaryIntervalSeconds sits at 30 s and stepped in twentieths of a second, six
            // hundred presses to double it. Too coarse: [Cards] FanFollowDeadzone is 0.004 and
            // "Deadzone" would have stepped it by 0.05, twelve times the whole value, so one press
            // could only overshoot.
            //
            // …BUT ONLY WHERE THE MAGNITUDE MEANS ANYTHING. Both of those are lone scalars, where
            // "how big it is" really is the best guide to "how finely it wants to move". A
            // COORDINATE is not: [WristHud] GloveOffsetX ships -0.003 and GloveOffsetY -0.053, the
            // same offset of the same HUD, and the only reason they differ by an order of magnitude
            // is that X sits near its origin. Bounding by that made X step 0.05 mm and Y 1 mm —
            // reported, correctly, as "der X-Offset hat keinen Einfluss". Two dials of one vector
            // must move together, so a Component takes the unit's answer untouched and a Variant
            // (one board of a per-board family) lets its own magnitude only COARSEN it, never
            // sharpen it. See ConfigSteps.UnitScope.
            //
            // A Component is still capped at a quarter of a DECLARED range further down, which is
            // the one thing that keeps a wide unit step honest on a narrow dial. A declared range is
            // a real statement of the dial's scale; a single shipped default is not.
            double magnitude = Magnitude(item.Entry.DefaultValue);
            if (scope == ConfigSteps.UnitScope.Variant)
                step = Math.Max(step, own);
            else if (scope == ConfigSteps.UnitScope.Value && magnitude > 0d)
                // The bounds apply only when there IS a magnitude. A default of 0 is precisely the
                // case the unit rule exists for — it is what the world tilt has — and zero must not
                // bound anything.
                step = Math.Min(Math.Max(step, own), magnitude / 4d);
        }
        else
        {
            step = own > 0d ? own : (item.Integral ? 1d : 0.01d);
        }

        step = NiceStep(step, item.Integral);

        if (item.HasRange)
        {
            double quarter = (item.Max - item.Min) / 4d;
            if (quarter > 0d && step > quarter)
                step = NiceStep(quarter, item.Integral);
        }

        return step;
    }

    /// <summary>Round a raw step to 1/2/5 x 10^k so the readout lands on round numbers.</summary>
    private static double NiceStep(double raw, bool integral)
    {
        if (integral)
            return Math.Max(1d, Math.Round(raw));
        if (raw <= 0d || double.IsNaN(raw) || double.IsInfinity(raw))
            return 0.01d;
        double exp = Math.Floor(Math.Log10(raw));
        double pow = Math.Pow(10d, exp);
        double m = raw / pow;
        double snapped = m < 1.5d ? 1d : m < 3.5d ? 2d : m < 7.5d ? 5d : 10d;
        // Floor at a millionth, not a thousandth: the old floor was five times LARGER than
        // [HexHighlight] StableDepthBias's whole value (0.0002), so its stepper could only ever
        // overshoot. Nothing a player meets is anywhere near this small.
        return Math.Max(0.000001d, snapped * pow);
    }

    // ==========================================================================================
    //  Topic mapping
    // ==========================================================================================

    /// <summary>
    /// (module, section) → topic, with a handful of per-KEY overrides for the cross-cutting main
    /// config, whose sections mix concerns ([Rig] holds both the world scale and the renderer path).
    /// One line per module: adding a module costs one line, adding an ENTRY costs nothing.
    /// </summary>
    private static ConfigTopic TopicOf(string module, string section, string key)
    {
        // Per-board geometry, whatever module it comes from: keys carry the board name as a
        // suffix ("BoardScale_Steel"), so this stays true for boards added later.
        if (HasBoardSuffix(key))
            return ConfigTopic.BoardGeometry;

        // The control board's OWN button geometry files with the control-board page, not with the
        // generic buttons topic (user report 2026-08: every "Best./Zurück" dial has to be findable
        // in ONE place — [BoardButtons] Width/Height/Depth/Travel were in Tasten ▸ "Generisch"
        // while their siblings ConfirmUndoOffset_*/GenericButtonSpacing_* sat on the per-board
        // page). [RestButtons] moves with it: the rest keycaps are split across the same two
        // families ([Cards] RestButton*_{board} + [RestButtons] W/H/D/T), so leaving them behind
        // would recreate the exact misfiling one report later.
        //
        // …AND [RoundButtons] JOINS THEM (user, hardware ModBuild 96): "Weiterhin vermisse ich die
        // Einstellungen im Debug Menu für genau diese 'Überspringen'-Tasten (offsets, Form, Größe,
        // etc..) — die Einstellungen sollen unter der Tasten-Kategorie genauso einstellbar sein."
        // The dials existed and were never missing from the menu — they were in the OTHER of the two
        // places a keycap family can live, which is the very misfiling the two lines above were
        // written to end. The docked turn-flow cluster shows exactly one member, the SKIP cap
        // (ButtonCluster.Tick forces the Ready/Undo twins off), and its geometry is split across the
        // same two families every sibling is: [Cards] ClusterOffset_*/ClusterScale_* on the
        // control-board page and [RoundButtons] Offset/Shape/CapSize/W/H/D/Travel over in Tasten. A
        // player who opens the board page's "Tasten" heading and finds Best./Zurück and Rast-Tasten
        // complete has every reason to conclude the skip cap simply has no dials. Now all nine sit
        // in the "Rundenknöpfe & Fixier-Taste" block beside ClusterOffset/ClusterScale — see
        // VROptionsTab.6.BoardTopic.cs, whose tree claims them.
        if (string.Equals(section, "BoardButtons", StringComparison.Ordinal)
            || string.Equals(section, "RestButtons", StringComparison.Ordinal)
            || string.Equals(section, "RoundButtons", StringComparison.Ordinal))
            return ConfigTopic.BoardGeometry;

        if (string.Equals(module, ModuleConfig.MainModule, StringComparison.Ordinal))
        {
            switch (section)
            {
                case "General":
                case "Core":
                    return ConfigTopic.System;
                case "Dev":
                    return ConfigTopic.Diagnostics;
                case "Hands":
                    return ConfigTopic.Hands;
                case "Compat":
                    // Everything in [Compat] is a rendering fixup except the component blacklist.
                    return key == "DisableComponents" ? ConfigTopic.System : ConfigTopic.Visual;
                case "Rig":
                    // [Rig] is genuinely two things: where the world sits, and how it is drawn.
                    return key is "VoidColor" or "ForwardRendering" or "Experimental3DMap"
                        ? ConfigTopic.Visual
                        : ConfigTopic.Movement;
                default:
                    return ConfigTopic.Other;
            }
        }

        return module switch
        {
            "comfort" => ConfigTopic.Movement,
            "hands" or "figuregrab" => ConfigTopic.Hands,
            "cards" => ConfigTopic.Cards,
            "buttons" => ConfigTopic.Buttons,
            "board" or "hexhighlight" or "selectionready" => ConfigTopic.BoardTargeting,
            "worldui" or "bars" => ConfigTopic.Panels,
            "rig" or "mixedreality" or "stereo" or "wallfade" => ConfigTopic.Visual,
            "net" => ConfigTopic.Network,
            "perf" => ConfigTopic.Diagnostics,
            _ => ConfigTopic.Other,
        };
    }

    /// <summary>True when the key ends in "_&lt;ControlBoard&gt;" (per-board tuning).</summary>
    private static bool HasBoardSuffix(string key)
    {
        int cut = key.LastIndexOf('_');
        if (cut <= 0 || cut >= key.Length - 1)
            return false;
        string tail = key.Substring(cut + 1);
        string[] boards = BoardNames;
        for (int i = 0; i < boards.Length; i++)
        {
            if (string.Equals(boards[i], tail, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string[]? _boardNames;

    private static string[] BoardNames
    {
        get
        {
            if (_boardNames != null)
                return _boardNames;
            try
            {
                _boardNames = Enum.GetNames(typeof(Cards.ControlBoard));
            }
            catch (Exception)
            {
                _boardNames = Array.Empty<string>();
            }
            return _boardNames;
        }
    }

    /// <summary>
    /// Entries the browser floats to the top of their group because they are what someone opens this
    /// pane FOR. Small and explicit: everything unlisted keeps the default rank and sorts normally.
    /// </summary>
    private static int Pinned(string section, string key)
    {
        if (section == "Perf")
        {
            return key switch
            {
                "CullSubmitSplit" => 0,
                "SceneProfile" => 1,
                "Enabled" => 2,
                "FrameSplit" => 3,
                _ => 100,
            };
        }
        return 100;
    }

    /// <summary>
    /// VERIFIED not-live entries: read once during the plugin's Awake or during the XR bootstrap,
    /// so writing them from the panel persists but changes nothing until the next start. Everything
    /// NOT listed carries the generic caveat instead — this list is deliberately short and only
    /// holds entries whose single read site was checked, rather than a guess per entry.
    /// </summary>
    private static bool IsStartupOnly(string module, string section, string key)
    {
        if (!string.Equals(module, ModuleConfig.MainModule, StringComparison.Ordinal))
            return false;
        return section switch
        {
            "General" => key is "Enabled" or "RuntimeOverride",
            "Core" => true, // RuntimePriority / SkipRuntimeCandidates / InitDelayFrames: bootstrap only
            "Compat" => key is "DisablePostProcessing" or "DisableVolumetricFog" or "DisableComponents",
            "Rig" => key is "ForwardRendering" or "MenuRig",
            "Dev" => key == "Enabled",
            _ => false,
        };
    }

    /// <summary>
    /// Curated option sets for the handful of FREE-FORM string entries whose legal values are a
    /// closed set the code itself compares against. UI-side only on purpose: writing an
    /// <c>AcceptableValueList</c> into the bind would make BepInEx reset any differently-cased
    /// value already saved in a player's cfg, and every reader of these keys compares
    /// case-insensitively. A key that is NOT listed here stays honestly read-only rather than
    /// getting a control that cycles through values nobody verified.
    /// </summary>
    private static object[]? CuratedChoices(string section, string key) => (section, key) switch
    {
        ("Core", "RuntimePriority") => new object[] { "auto", "default", "vdxr", "steamvr", "oculus" },
        ("Hands", "PrimaryHand") => new object[] { "Right", "Left" },
        ("WorldUI", "ClickMode") => new object[] { "execute", "virtualmouse", "both" },
        ("WorldUI", "ModalStyle") => new object[] { "window", "screen" },
        ("Cards", "RevealMode") => new object[] { "tilt", "always" },
        _ => null,
    };

    // ==========================================================================================
    //  Grouping
    // ==========================================================================================

    private static void GroupAll(List<ConfigItem> items)
    {
        // How many entries each SECTION contributes to each TOPIC — decides per section whether it
        // is small enough to stay one group or must be split by key prefix.
        var perSection = new Dictionary<string, int>(64, StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            string k = SectionCountKey(items[i]);
            perSection.TryGetValue(k, out int n);
            perSection[k] = n + 1;
        }

        var byLabel = new Dictionary<string, ConfigGroup>(32, StringComparer.Ordinal);
        for (int topic = 0; topic < TopicCount; topic++)
        {
            byLabel.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                ConfigItem item = items[i];
                if ((int)item.Topic != topic)
                    continue;
                perSection.TryGetValue(SectionCountKey(item), out int n);
                string label = n > SectionSplitThreshold ? GroupWord(item) : SectionLabel(item.Section);
                if (string.IsNullOrEmpty(label))
                    label = MiscLabel;
                if (!byLabel.TryGetValue(label, out ConfigGroup? group))
                {
                    group = new ConfigGroup { Label = label };
                    byLabel[label] = group;
                }
                group.Items.Add(item);
            }

            List<ConfigGroup> groups = Topics[topic];
            foreach (KeyValuePair<string, ConfigGroup> kv in byLabel)
                groups.Add(kv.Value);

            FoldSmallGroups(groups);
            SortGroups(groups);
            for (int g = 0; g < groups.Count; g++)
                SortItems(groups[g].Items);
        }
    }

    /// <summary>Section identity for the size test — same section name in two files counts as one topic block.</summary>
    private static string SectionCountKey(ConfigItem item) => (int)item.Topic + "/" + item.Section;

    /// <summary>
    /// Fold clusters too small to be worth their own chooser row into ONE "Allgemein" group, then
    /// keep folding the smallest cluster until the topic fits the chooser's fixed row pool.
    ///
    /// <para>TERMINATION IS STRUCTURAL, not incidental — this pane is being written the week a
    /// per-frame walk with no bound hung the game before the main menu. The collector is created
    /// up front and is never itself a fold candidate, the first pass is a single descending index
    /// walk, and the second loop removes exactly one element from <paramref name="groups"/> per
    /// iteration with the loop condition reading that same Count. Neither can revisit a group.</para>
    /// </summary>
    private static void FoldSmallGroups(List<ConfigGroup> groups)
    {
        if (groups.Count == 0)
            return;

        var misc = new ConfigGroup { Label = MiscLabel };

        for (int i = groups.Count - 1; i >= 0; i--)
        {
            ConfigGroup g = groups[i];
            if (g.Items.Count >= MinClusterSize && !string.Equals(g.Label, MiscLabel, StringComparison.Ordinal))
                continue;
            misc.Items.AddRange(g.Items);
            groups.RemoveAt(i);
        }

        // Reserve one slot for the collector, so folding can never be defeated by its own arrival.
        int cap = misc.Items.Count > 0 ? MaxGroupsPerTopic - 1 : MaxGroupsPerTopic;
        while (groups.Count > cap && groups.Count > 0)
        {
            int smallest = 0;
            for (int i = 1; i < groups.Count; i++)
            {
                if (groups[i].Items.Count < groups[smallest].Items.Count)
                    smallest = i;
            }
            misc.Items.AddRange(groups[smallest].Items);
            groups.RemoveAt(smallest);
            cap = MaxGroupsPerTopic - 1; // the collector is now non-empty and needs its slot
        }

        if (misc.Items.Count > 0)
            groups.Add(misc);
    }

    /// <summary>Biggest cluster first (that is where the settings are), "Allgemein" always last.</summary>
    private static void SortGroups(List<ConfigGroup> groups) => groups.Sort(static (a, b) =>
    {
        bool am = string.Equals(a.Label, MiscLabel, StringComparison.Ordinal);
        bool bm = string.Equals(b.Label, MiscLabel, StringComparison.Ordinal);
        if (am != bm)
            return am ? 1 : -1;
        int byCount = b.Items.Count.CompareTo(a.Items.Count);
        return byCount != 0 ? byCount : string.CompareOrdinal(a.Label, b.Label);
    });

    /// <summary>
    /// Pinned entries first, then the on/off decisions, then the dials, then what cannot be edited —
    /// so the top of every page is the part a person can act on, and read-only text never sits
    /// between two controls. Alphabetical inside each block, which is what makes scanning work.
    /// </summary>
    private static void SortItems(List<ConfigItem> items) => items.Sort(static (a, b) =>
    {
        if (a.Pin != b.Pin)
            return a.Pin.CompareTo(b.Pin);
        int ka = KindRank(a.Kind);
        int kb = KindRank(b.Kind);
        if (ka != kb)
            return ka.CompareTo(kb);
        return string.CompareOrdinal(a.Display, b.Display);
    });

    private static int KindRank(ConfigKind k) => k switch
    {
        ConfigKind.Bool => 0,
        ConfigKind.Choice => 1,
        ConfigKind.Number => 2,
        ConfigKind.Components => 3,
        _ => 4,
    };

    private static string MiscLabel => Loc.Mod("cfg_group_misc");

    /// <summary>
    /// Section name as shown in the chooser. Localized where the raw name is jargon; otherwise the
    /// section name itself, spaced out — a section nobody translated is still readable, never blank.
    /// </summary>
    private static string SectionLabel(string section) => section switch
    {
        "Perf" => Loc.Mod("cfg_sec_perf"),
        "Optimize" => Loc.Mod("cfg_sec_optimize"),
        "Dev" => Loc.Mod("cfg_sec_dev"),
        "Comfort" => Loc.Mod("cfg_sec_comfort"),
        "WristHud" => Loc.Mod("cat_wrist"),
        "RestButtons" => Loc.Mod("rest"),
        "RoundButtons" => Loc.Mod("round_buttons"),
        "BoardButtons" => Loc.Mod("generic"),
        "BoardDashboard" => Loc.Mod("board_dashboard"),
        "ButtonColors" => Loc.Mod("button_colors"),
        "ButtonAnim" => Loc.Mod("cfg_sec_buttonanim"),
        "TransientButtons" => Loc.Mod("cfg_sec_transient"),
        "SquareCaps" => Loc.Mod("cfg_sec_squarecaps"),
        "WallFade" => Loc.Mod("wall_fade"),
        "HexHighlight" => Loc.Mod("cfg_sec_hexhighlight"),
        "SelectionReady" => Loc.Mod("cfg_sec_selectionready"),
        "MixedReality" => Loc.Mod("mixed_reality"),
        "RenderQuality" => Loc.Mod("cfg_sec_renderquality"),
        "FigureGrab" => Loc.Mod("figure_offsets"),
        // Reached since per-variant keys group by SECTION rather than by the hand style in their
        // name; without it a German menu would head the block with the English "Hands".
        "Hands" => Loc.Mod("hands"),
        _ => Spaced(section),
    };

    // ==========================================================================================
    //  Text helpers
    // ==========================================================================================

    /// <summary>
    /// "CullSubmitSplit" → "Cull Submit Split", "VRSettingsOffset_Oak" → "VR Settings Offset Oak".
    /// One forward pass, no allocation beyond the result. Acronym runs stay glued to the word that
    /// follows them, which is what makes "VRSettings" read as "VR Settings" and not "V R Settings".
    /// </summary>
    internal static string Spaced(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;
        var sb = new StringBuilder(key.Length + 8);
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c == '_')
            {
                sb.Append(' ');
                continue;
            }
            bool boundary = i > 0 && char.IsUpper(c) && key[i - 1] != '_'
                            && (char.IsLower(key[i - 1])
                                || char.IsDigit(key[i - 1])
                                || (i + 1 < key.Length && char.IsLower(key[i + 1])));
            if (boundary && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The key's leading word — the automatic cluster name inside an oversized section
    /// ("FanArcSweepDegrees" → "Fan", "BarZoomMinScale" → "Bar", "VRSettingsOffset" → "VR").
    /// Bounded by the key length; never returns empty for a non-empty key.
    /// </summary>
    /// <summary>
    /// Names that identify a VARIANT rather than a subject: the control boards and the hand styles.
    /// Read from the enums, so a new board or hand style joins the list by existing.
    /// </summary>
    private static readonly HashSet<string> VariantWords = BuildVariantWords();

    private static HashSet<string> BuildVariantWords()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string n in Enum.GetNames(typeof(Cards.ControlBoard)))
            set.Add(n);
        foreach (string n in Enum.GetNames(typeof(Hands.HandStyle)))
            set.Add(n);
        return set;
    }

    /// <summary>
    /// The label a big section's entry is grouped under.
    ///
    /// <para>NOT THE VARIANT. A section past the split threshold is grouped by the first word of the
    /// key, which for the hand settings is the hand STYLE — so <c>GloveGripPitchDegrees</c>,
    /// <c>PlateOffsetX</c> and <c>ArcaneHeldScale</c> produced groups called "Glove", "Plate" and
    /// "Arcane": three grab-bags named after a choice, each mixing the hand's seat, the wrist HUD's
    /// pose and the figure-grab pose. Someone looking for where the hands are aimed had to guess
    /// that it lived under the name of the glove they happened to be wearing, and the pane now folds
    /// per-variant rows down to the selected one anyway, so two of those three groups would be empty
    /// and the third would be named after the one thing no longer shown.</para>
    ///
    /// <para>When the leading word names a variant, the SECTION is the meaningful axis instead —
    /// "Hands", "Wrist HUD", "Figure grab" — which is what the settings are ABOUT.</para>
    /// </summary>
    private static string GroupWord(ConfigItem item)
    {
        string word = LeadingWord(item.Key);
        return VariantWords.Contains(word) ? SectionLabel(item.Section) : word;
    }

    internal static string LeadingWord(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;
        int i = 1;
        while (i < key.Length)
        {
            char c = key[i];
            if (c == '_')
                break;
            bool boundary = char.IsUpper(c)
                            && (char.IsLower(key[i - 1])
                                || char.IsDigit(key[i - 1])
                                || (i + 1 < key.Length && char.IsLower(key[i + 1])));
            if (boundary)
                break;
            i++;
        }
        return key.Substring(0, i);
    }

    // ==========================================================================================
    //  Read / write — the operations the panel's controls call
    // ==========================================================================================

    internal static IReadOnlyList<ConfigGroup> Groups(ConfigTopic topic)
    {
        int idx = (int)topic;
        return idx >= 0 && idx < TopicCount ? Topics[idx] : Array.Empty<ConfigGroup>();
    }

    /// <summary>Component name for the axis/channel picker: X/Y/Z/W, or R/G/B/A for a colour.</summary>
    internal static string ComponentLabel(ConfigItem item, int component)
    {
        bool color = item.Entry.SettingType == typeof(Color);
        string[] names = color ? ColorNames : AxisNames;
        return names[Mathf.Clamp(component, 0, names.Length - 1)];
    }

    private static readonly string[] AxisNames = { "X", "Y", "Z", "W" };
    private static readonly string[] ColorNames = { "R", "G", "B", "A" };

    /// <summary>The value as the row shows it (compact — the tooltip carries the full text).</summary>
    internal static string ValueText(ConfigItem item, int component)
    {
        try
        {
            object? v = item.Entry.BoxedValue;
            switch (item.Kind)
            {
                case ConfigKind.Bool:
                    return v is bool b && b ? Loc.Mod("on") : Loc.Mod("off");
                case ConfigKind.Choice:
                    return v?.ToString() ?? "-";
                case ConfigKind.Number:
                    return Num(Convert.ToDouble(v, CultureInfo.InvariantCulture), item.Integral,
                               item.BaseStep);
                case ConfigKind.Components:
                    return Num(Component(v, component), false, item.BaseStep);
                default:
                    string s = v?.ToString() ?? string.Empty;
                    return string.IsNullOrEmpty(s) ? Loc.Mod("cfg_empty") : Clip(s, 22);
            }
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// <summary>
    /// The readout for one number.
    ///
    /// <para>THE PRECISION FOLLOWS THE STEP, not the magnitude. Formatting by magnitude alone means
    /// a value can be shown to fewer decimals than one press changes it by — and then the player
    /// presses ◀ / ▶, the value moves, and the row does not: the control reads as broken while
    /// working perfectly. Showing at least enough decimals to see one step makes every press
    /// visible, which is the only way a stepper can be trusted.</para>
    ///
    /// <para><paramref name="step"/> is 0 where there is no stepper (the range readout below), and
    /// the magnitude rule then applies on its own as before.</para>
    /// </summary>
    private static string Num(double v, bool integral, double step = 0d)
    {
        if (integral)
            return v.ToString("0", CultureInfo.InvariantCulture);

        double abs = Math.Abs(v);
        int decimals = abs >= 100d ? 1 : abs >= 1d ? 2 : 4;

        if (step > 0d && step < 1d)
        {
            // Decimals needed to show one step: 0.05 → 2, 0.002 → 3. Capped at six, which is the
            // floor NiceStep can produce.
            int needed = (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
            decimals = Math.Max(decimals, Math.Min(6, needed));
        }

        return v.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
    }

    private static double Component(object? boxed, int c) => boxed switch
    {
        Vector2 v2 => c == 0 ? v2.x : v2.y,
        Vector3 v3 => c == 0 ? v3.x : c == 1 ? v3.y : v3.z,
        Vector4 v4 => v4[Mathf.Clamp(c, 0, 3)],
        Color col => col[Mathf.Clamp(c, 0, 3)],
        _ => 0d,
    };

    /// <summary>Flip a bool entry. No-op (and logged) for anything else.</summary>
    internal static void ToggleBool(ConfigItem item)
    {
        if (item.Kind != ConfigKind.Bool)
            return;
        Write(item, item.Entry.BoxedValue is bool b && !b);
    }

    /// <summary>Advance a choice entry by <paramref name="dir"/> steps, wrapping.</summary>
    internal static void Cycle(ConfigItem item, int dir)
    {
        object[]? choices = item.Choices;
        if (choices == null || choices.Length == 0)
            return;
        int at = IndexOf(choices, item.Entry.BoxedValue);
        // A value outside the list can only happen for an enum/list entry after a cfg edit BepInEx
        // did not clamp; start the cycle at the first option rather than doing nothing.
        int next = at < 0 ? 0 : ((at + dir) % choices.Length + choices.Length) % choices.Length;
        Write(item, choices[next]);
    }

    private static int IndexOf(object[] choices, object? value)
    {
        for (int i = 0; i < choices.Length; i++)
        {
            if (SameValue(choices[i], value))
                return i;
        }
        return -1;
    }

    private static bool SameValue(object? a, object? b)
    {
        if (a == null || b == null)
            return ReferenceEquals(a, b);
        if (a is string sa && b is string sb)
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        return a.Equals(b);
    }

    /// <summary>
    /// Nudge a numeric (or one component of a vector/colour) by <paramref name="dir"/> steps of
    /// <see cref="ConfigItem.BaseStep"/> x <paramref name="multiplier"/>, clamped into the entry's
    /// acceptable range when it has one. BepInEx clamps again on write, so the row can never show a
    /// value the entry would refuse.
    /// </summary>
    internal static void Step(ConfigItem item, int component, int dir, double multiplier)
    {
        try
        {
            double step = item.BaseStep * multiplier;
            if (item.Integral)
                step = Math.Max(1d, Math.Round(step));

            if (item.Kind == ConfigKind.Number)
            {
                double v = Convert.ToDouble(item.Entry.BoxedValue, CultureInfo.InvariantCulture) + dir * step;
                if (item.HasRange)
                    v = Math.Min(item.Max, Math.Max(item.Min, v));
                Write(item, Convert.ChangeType(item.Integral ? Math.Round(v) : v,
                    item.Entry.SettingType, CultureInfo.InvariantCulture));
                return;
            }

            if (item.Kind != ConfigKind.Components)
                return;

            object? boxed = item.Entry.BoxedValue;
            double nv = Component(boxed, component) + dir * step;
            if (item.Entry.SettingType == typeof(Color))
                nv = Math.Min(1d, Math.Max(0d, nv));
            Write(item, WithComponent(boxed, component, (float)nv));
        }
        catch (Exception ex)
        {
            VRLog.Warn("Config", $"Stepping {item.Section}/{item.Key} failed: {ex.GetType().Name}.");
        }
    }

    private static object? WithComponent(object? boxed, int c, float v)
    {
        switch (boxed)
        {
            case Vector2 v2:
                if (c == 0) v2.x = v; else v2.y = v;
                return v2;
            case Vector3 v3:
                if (c == 0) v3.x = v; else if (c == 1) v3.y = v; else v3.z = v;
                return v3;
            case Vector4 v4:
                v4[Mathf.Clamp(c, 0, 3)] = v;
                return v4;
            case Color col:
                col[Mathf.Clamp(c, 0, 3)] = v;
                return col;
            default:
                return boxed;
        }
    }

    /// <summary>
    /// The single write path. BepInEx clamps to the acceptable values, raises SettingChanged (which
    /// is what makes the live consumers pick the value up) and saves the file — so nothing here has
    /// to persist or notify by hand.
    /// </summary>
    private static void Write(ConfigItem item, object? value)
    {
        try
        {
            item.Entry.BoxedValue = value;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Config", $"Writing {item.Section}/{item.Key} failed: {ex.GetType().Name} — value unchanged.");
        }
    }

    // ==========================================================================================
    //  Hover text
    // ==========================================================================================

    /// <summary>Longest description shown in the bubble — past this the paragraph is clipped.</summary>
    private const int MaxDescriptionChars = 620;

    /// <summary>
    /// The row's hover explanation: where the entry lives, what the config file says about it, its
    /// default and range, and — honestly — whether the change is live.
    ///
    /// <para>ONE LANGUAGE, ALWAYS (user, 2026-07: "Die Tooltipps bitte nicht immer in beiden
    /// Sprachen, sondern der jeweiligen Sprache des Spiels"). The chrome around the paragraph is
    /// <see cref="Loc.Mod"/>, so it follows the game's language; the paragraph itself used to be the
    /// raw BepInEx description, which is English because that is the language a config FILE is
    /// written in — every tooltip was therefore half English, half German. It now asks
    /// <see cref="Loc.ConfigDescription"/> first and falls back to the bound English text only when
    /// the current language has no translation for that entry. The fallback is what keeps a
    /// description added tomorrow readable instead of blank.</para>
    /// </summary>
    /// <summary>
    /// The one-paragraph, localized explanation of what a setting DOES — nothing else.
    ///
    /// <para>Deliberately not <see cref="Tooltip"/>, which is the power-user readout: it leads with
    /// "[Net] MaskId", the config file name, the default and the range, because that is what the
    /// config BROWSER is for. Shown to a player next to a menu row it answers a question nobody
    /// asked and buries the one they did. Same source text, none of the scaffolding.</para>
    ///
    /// <para>Falls back to the entry's own description when a language has no translation — English
    /// prose is more use than an empty box — and returns empty only when there is no text at all.</para>
    /// </summary>
    internal static string Hint(ConfigItem item)
    {
        string? desc = Loc.ConfigDescription(item.Section, item.Key)
                       ?? item.Entry.Description?.Description;
        return string.IsNullOrEmpty(desc) ? string.Empty : Clip(Collapse(desc!), MaxDescriptionChars);
    }

    internal static string Tooltip(ConfigItem item)
    {
        var sb = new StringBuilder(512);
        sb.Append('[').Append(item.Section).Append("] ").Append(item.Key).Append('\n');
        sb.Append(FileNameOf(item)).Append('\n');

        string? desc = Loc.ConfigDescription(item.Section, item.Key)
                       ?? item.Entry.Description?.Description;
        if (!string.IsNullOrEmpty(desc))
            sb.Append('\n').Append(Clip(Collapse(desc!), MaxDescriptionChars)).Append('\n');

        sb.Append('\n').Append(Loc.Mod("cfg_default")).Append(": ")
          .Append(Clip(item.Entry.DefaultValue?.ToString() ?? "-", 40));
        if (item.HasRange)
        {
            sb.Append("   ").Append(Loc.Mod("cfg_range")).Append(": ")
              .Append(Num(item.Min, item.Integral)).Append(" … ").Append(Num(item.Max, item.Integral));
        }
        sb.Append('\n');

        sb.Append('\n').Append(item.NeedsRestart ? Loc.Mod("cfg_needs_restart") : Loc.Mod("cfg_live_note"));

        if (item.Kind == ConfigKind.ReadOnly)
            sb.Append('\n').Append('\n').Append(Loc.Mod("cfg_readonly_note"));

        return sb.ToString();
    }

    /// <summary>The cfg file the entry lives in — the answer to "where do I edit this by hand".</summary>
    internal static string FileNameOf(ConfigItem item) =>
        string.Equals(item.Module, ModuleConfig.MainModule, StringComparison.Ordinal)
            ? $"{MyPluginInfo.PLUGIN_GUID}.cfg"
            : $"{MyPluginInfo.PLUGIN_GUID}.{item.Module}.cfg";

    /// <summary>Squash the config file's hard-wrapped whitespace so the bubble can re-wrap it.</summary>
    private static string Collapse(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n' || c == '\r' || c == '\t' || c == ' ')
            {
                space = true;
                continue;
            }
            if (space && sb.Length > 0)
                sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Clip(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, Math.Max(0, max - 1)) + "…";

    /// <summary>Localized topic name — the element chooser's readout.</summary>
    internal static string TopicLabel(ConfigTopic topic) => topic switch
    {
        ConfigTopic.Diagnostics => Loc.Mod("cfg_topic_diagnostics"),
        ConfigTopic.Visual => Loc.Mod("cfg_topic_visual"),
        ConfigTopic.Movement => Loc.Mod("cfg_topic_movement"),
        ConfigTopic.Hands => Loc.Mod("cfg_topic_hands"),
        ConfigTopic.Cards => Loc.Mod("cfg_topic_cards"),
        ConfigTopic.Buttons => Loc.Mod("cat_buttons"),
        ConfigTopic.Panels => Loc.Mod("cfg_topic_panels"),
        ConfigTopic.BoardTargeting => Loc.Mod("cfg_topic_board"),
        ConfigTopic.BoardGeometry => Loc.Mod("cfg_topic_board_geometry"),
        ConfigTopic.Network => Loc.Mod("cfg_topic_network"),
        ConfigTopic.System => Loc.Mod("cfg_topic_system"),
        _ => Loc.Mod("cfg_topic_other"),
    };
}
