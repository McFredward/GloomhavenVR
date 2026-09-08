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
/// "Fan", <c>BarSizeScale</c> → "Bar"). That second rule is what keeps a 90-entry section
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
    /// alphabetical: EVERYDAY-ADJACENT TOPICS FIRST, measurement second-to-last, Other last.
    ///
    /// <para>RE-SORTED in the 2026-08 menu overhaul (user ruling: "Setze erstmal alle Vorschläge
    /// zu den Settings deinerseits so um", implementing the menu-audit's structure proposal,
    /// .planning/menu-audit/05-structure.md §2.2). The old order put "Messung &amp; Diagnose"
    /// FIRST — a reasoning ("measurement first, that is what a power user opens this pane for")
    /// that predates the curated everyday layer. Today this index is the second level for ANYONE
    /// who did not find a setting in the everyday tabs, and that person was landing on perf
    /// measurement switches. So: movement, hands, cards, panels, board, then the per-board page,
    /// then picture, buttons, multiplayer, system — and only then the measurement page (whose
    /// [Perf] pins inside it are untouched: whoever opens it still gets CullSubmitSplit first).
    /// <see cref="Other"/> stays last and exists so a module nobody mapped is still reachable
    /// instead of silently invisible.</para>
    ///
    /// <para>SAFE TO REORDER: the enum values are menu-internal only — nothing persists a topic
    /// ordinal (checked: only <c>VROptionsTab._category</c> holds one, at runtime). Re-checked at
    /// the 2026-08-22 settings audit before <see cref="Environment"/> and <see cref="Sound"/> were
    /// inserted mid-list: no wire record, no cfg key and no save file carries a topic ordinal.</para>
    ///
    /// <para>TWO TOPICS ADDED (audit (c), user verbatim: <i>"Überprüfe die Kategorien und ordne sie
    /// eventuell neu wenn du denkst das es intuitiver und Userfreundlicher wäre."</i>). The audit's
    /// Finding 2 was that the mod's ENTIRE audio surface was filed under a topic literally named
    /// "Bild &amp; Darstellung" / "Picture &amp; rendering" — because <c>[EnvSound]</c> binds on
    /// <c>rig.cfg</c> and the module table below sent the whole file to <see cref="Visual"/>.
    /// Nobody looks for a volume slider under Graphics. Finding 3 was the mirror image: the
    /// environment family (<c>[Sky]</c> 1 entry, <c>[Elements]</c> 2, <c>[Haunt]</c> 2, <c>[Rig]
    /// Vanilla2DMap</c> 1 — <c>Experimental3DMap</c> until ModBuild 230 renamed and inverted it)
    /// each fell under <see cref="MinClusterSize"/> and was swept into
    /// Visual's "Allgemein" collector, so the environment CHOOSER sat in a grab-bag two rows from
    /// the environment's own mood dials. A topic each fixes both permanently and mechanically: a
    /// setting added to one of those sections tomorrow lands in the right place with no edit
    /// here.</para>
    /// </summary>
    internal enum ConfigTopic
    {
        Movement,
        Hands,
        Cards,
        Panels,
        BoardTargeting,
        BoardGeometry,
        Visual,

        /// <summary>The world you stand in: sky/environment choice, element mood, haunt, the 3D
        /// map room. Properties OF the chosen environment, not of how it is rasterised.</summary>
        Environment,

        /// <summary>Everything you HEAR. Its own topic since the 2026-08-22 audit — see the
        /// class remark above for why "Bild &amp; Darstellung" was the wrong home.</summary>
        Sound,

        Buttons,
        Network,
        System,
        Diagnostics,
        Other,
    }

    internal const int TopicCount = 14;

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

        /// <summary>
        /// Step at multiplier 1. Filled in a SECOND PASS over the whole catalog, not in
        /// <see cref="Classify"/> — see <see cref="ResolveSteps"/>: the step depends on the largest
        /// magnitude in the entry's FAMILY, which is not knowable until every entry has been read.
        /// </summary>
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
        // "stereo" stood here for [Stereo] RenderMode, the ONE entry that file ever held. The
        // 2026-08-22 settings audit made it a constant (Core/StereoModeConfig: MultiPass is the
        // only mode that renders correctly in this game, and SinglePassInstanced is a black right
        // eye applied at session creation), so there is no stereo module to force-bind any more.
        Bind("mixedreality", MixedReality.Bind);
        Bind("wallfade", WallFadeTuning.Bind);
        Bind("worldui", WorldUIConfig.Bind);
        Bind("stereoscreen", FlatScreenStereo.BindConfig); // rides worldui's file — bind it AFTER
        // ModBuild 189: [WorldUI] WindowLegibility lives on worldui's file too but is owned by the
        // code that reads it (ModalFallback.9.Spawn.cs), so it binds late for the same reason the
        // stereo compositor's entries do — and must be force-bound here, or a session in which no
        // window has floated yet shows a hole where the window-size dial should be.
        Bind("modalwindows", ModalFallback.BindWindowConfig);
        Bind("bars", ActorBars.BindConfig);
        Bind("buttons", ButtonTuning.Bind);
        Bind("cards", Cards.CardsConfig.Bind);
        Bind("board", Board.BoardConfig.Bind);
        Bind("hexhighlight", Board.HexHighlightFix.BindConfig);
        Bind("selectionready", Board.SelectionReadyHighlighter.Bind);
        Bind("figuregrab", Board.FigureGrab.FigureGrabConfig.Bind);
        Bind("hands", Hands.HandsConfig.Bind);
        Bind("net", Net.NetModule.BindConfig);
        // CHEATS (temporary) — removal step 5. Force-bound so dev.gloomhavenvr.cheats.cfg EXISTS
        // the first time the options window is opened: the switch is off by default and hidden
        // from the menu (NotOffered), so the file is the only place the player can turn it on,
        // and a file that is never written is a gate with no handle.
        Bind("cheats", VROptionsTab.BindCheatsConfig);
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

        ResolveSteps(items);
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
    ///
    /// <para>THE 2026-08-22 SETTINGS AUDIT ADDED THE SECOND HALF OF THIS TABLE (user, verbatim):
    /// <i>"a) Lösche alle Einstellungen die das Spiel breaken könnten wenn die verändert werden.
    /// Etwas was das spiel kaputt macht wenn man es umstellt ist nicht optional und sollte daher
    /// nicht einstellbar sein."</i> The test applied, literally: <b>is there a value inside the
    /// allowed range that leaves the player unable to play, or unable to undo the change from
    /// inside the headset?</b> Every entry below names the harm and the value that causes it, so a
    /// future reader can check the judgement rather than take it. Twelve further entries of that
    /// sweep were not hidden but UNBOUND — where the value was never a choice at all, the constant
    /// now stands in the code that reads it (Core/StereoModeConfig, Plugin.cs, Rig/RenderQuality,
    /// WorldUI/WorldUIConfig, Board/HexHighlightFix), each with the same argument written beside
    /// it. Nothing here loses its cfg key: a hand-edit still works and the file still round-trips,
    /// which is what keeps a genuine escape hatch an escape hatch.</para>
    /// </summary>
    private static readonly Dictionary<string, string> NotOffered = new(StringComparer.Ordinal)
    {
        // Tints the PROCEDURAL hand — the capsule fallback built only when the asset bundle is
        // missing (HandVisuals.CreateHandMaterial). Any install that loads the bundle wears one of
        // the three hand models and never sees it. It is also an RRGGBB string, so the row offered
        // a left/right stepper with nothing to step.
        // CHEATS (temporary) — removal step 5. A gate that can be opened from inside the room it
        // locks is not a gate: the request was that the cheats page be enabled "über die cfg",
        // and a toggle on the Erweitert page would let one mis-click undo that. Hand-editing
        // dev.gloomhavenvr.cheats.cfg still works, which is the whole point.
        ["Cheats/Enabled"] = "the gate for the temporary cheats page — a .cfg decision by request, "
            + "so it must not be flippable from inside the menu it unlocks",
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

        // ==========================================================================================
        //  2026-08-22 audit (a) — "Lösche alle Einstellungen die das Spiel breaken könnten"
        // ==========================================================================================

        // ---- 1. Not settings at all: internal state and one-shot markers -----------------------
        // Three of these are literally captioned "Interne Marke" with the bound description "Do not
        // edit.", and the menu showed them as ordinary toggles BETWEEN REAL SETTINGS. Setting one
        // back to false does not undo its migration — it RE-ARMS it, and the migration then fires
        // again on the next start against values the player has since tuned.
        ["Cards/BoardScaleDefault04Applied"] = "internal one-shot marker — false re-arms the "
            + "migration that rewrites any BoardScale_<board> sitting at exactly 1.0 to 0.4 "
            + "(CardsConfig.cs:1292-1310): the board changes size on its own at the next start",
        ["Cards/DecisionOffsetYRebased"] = "internal one-shot marker — false re-arms the "
            + "DecisionOffset_*.y re-base (CardsConfig.cs:1327-1345)",
        ["Comfort/TableScaleDefault25Applied"] = "internal one-shot marker — false re-arms the "
            + "2.5x table-scale migration (ComfortSettings.cs:414-427), which moves the world out "
            + "from under a player who had already scaled it by hand",
        // ModBuild 230's map-presentation one-shot, listed the moment it was bound rather than
        // after somebody found it sitting between two real settings. Same harm as the three above
        // and one more besides: false does not "undo" the migration, it re-runs it against the
        // ORPHANED [Rig] Experimental3DMap line still in the file — so a player who has since
        // chosen the other map has that choice overwritten at the next start by a row that looks
        // like it only re-armed some bookkeeping. Its bound description opens with "Do not edit."
        ["Rig/MapPresentationMigrated230"] = "internal one-shot marker — false re-arms the "
            + "ModBuild 230 transfer of [Rig] Experimental3DMap to [Rig] Vanilla2DMap "
            + "(Plugin.cs), which overwrites whichever campaign map the player has since chosen",
        ["Comfort/SavedScaleMultiplier"] = "an OUTPUT, not a setting: written automatically after "
            + "every two-grip scale gesture, so a menu edit is overwritten by the next pinch and "
            + "only shows at all on a rig rebuild — a control that visibly does nothing",
        // ["WorldUI/CombatLogUserClosed"] stood here and the KEY ITSELF IS GONE (2026-09-05).
        // Withholding it from the menu was right and not enough: it was still BOUND, still
        // persisted, and it had already latched the combat log shut for good because the only
        // writer that could clear it had been deleted with the old settings panel. A row nobody
        // can see is not a safe home for state nobody can reset. See the tombstone in
        // WorldUIConfig and the seam doc in CombatLogSurface.

        // ---- 2. Kill switches for the mod and the VR bootstrap ---------------------------------
        // All read once at plugin Awake / XR bootstrap (IsStartupOnly says so by name), so the
        // change lands AT THE NEXT START, inside a headset that will no longer come up. There is no
        // route back except editing a .cfg on the desktop — which is exactly why the KEYS stay.
        ["General/Enabled"] = "the only switch that turns VR off for good, and the only UI that "
            + "could turn it back on is this menu, which then no longer exists: false is "
            + "unrecoverable from inside the headset. It stays in the .cfg, where a kill switch "
            + "belongs",
        ["Core/RuntimePriority"] = "picking a runtime the machine does not have ends XR init — no "
            + "session, no VR (e.g. 'steamvr' on a VDXR-only rig), and it applies at the next start",
        ["Core/SkipRuntimeCandidates"] = "an escape hatch by its own first word: true removes the "
            + "candidate failover that is what makes VR come up at all on most setups",
        ["Core/InitDelayFrames"] = "an unbounded int read once at bootstrap — a large value simply "
            + "means the mod never initialises, with nothing on screen to say why",
        ["General/RuntimeOverride"] = "a filesystem path, shown read-only, so it is a row a player "
            + "can neither use nor understand; a wrong path costs the next start its runtime",

        // ---- 3. Render-path, input-integrity and measurement dials ------------------------------
        // The three below are the free-text / measured members of that family: they cannot be
        // edited from the menu at all (read-only rows) or they configure a measurement, so they are
        // pure noise on a page of settings. The dangerous half of the family was UNBOUND instead —
        // see the class doc above.
        ["Compat/DisableComponents"] = "free text (comma-separated component type names to "
            + "disable) shown read-only — an unusable row, and a wrong name there disables a "
            + "component the mod needs",
        ["Optimize/HeadCullingMaskDrop"] = "free text shown read-only, and its own description says "
            + "which layers are safe is A MEASUREMENT, not something that can be guessed: a wrong "
            + "layer removes scene content from the headset only",
        ["Optimize/HeadDepthPrepass"] = "a perf EXPERIMENT whose answer is measured, not preferred",
        ["Optimize/HeadMaskFromScenarioCamera"] = "true rewrites the head camera's culling mask "
            + "from the game's ScenarioCamera, which deliberately excludes thirteen layers — the "
            + "content disappears in the headset while the desktop mirror still shows it",
        ["HexHighlight/LogMaterialDump"] = "a logging switch, not a setting",

        // ---- 4. Mixed-Reality internals whose OWN descriptions say they are not choices ---------
        // A plain bug, not a judgement call: all three carry the sentence "not offered in the VR
        // menu" in their bound description and were offered anyway, because only their sibling
        // HideSkyMeshes above was ever added to this table.
        ["MixedReality/OpaquePreviewTiles"] = "its own words: PART OF MIXED REALITY, not a choice "
            + "beside it (like HideSkyMeshes; not offered in the VR menu) — false makes the "
            + "fog-of-war stacks blend with passthrough, the defect MR exists to avoid",
        ["MixedReality/UnseenRegionMembership"] = "its own words: PART OF MIXED REALITY, not a "
            + "choice beside it (like HideSkyMeshes; not offered in the VR menu)",
        ["MixedReality/UnseenBackingDebugColors"] = "its own words: DIAGNOSTIC, turn this on only "
            + "when asked for a screenshot — true deliberately makes the fog-of-war region look "
            + "wrong (flat blue/magenta/red)",

        // ---- 5. Developer-only switches ---------------------------------------------------------
        // None of these is a preference, several are actively hostile if flipped, and none was
        // gated behind [Dev] Enabled in the menu — every one was an ordinary row between settings a
        // player is meant to touch. They stay hand-editable, which is all a developer needs.
        ["Dev/Enabled"] = "developer switch — not a preference",
        ["Dev/Overlay"] = "developer switch — the on-screen debug overlay",
        ["Dev/SimulateHands"] = "developer switch — fakes controller poses over the real ones",
        ["Dev/InputDeviceDumpInterval"] = "developer switch — periodic input-device dump into the log",
        ["WorldUI/DevShowAllPanels"] = "developer switch — forces every panel visible at once",
        ["WorldUI/DevForceConvert"] = "developer switch — forces canvas conversion on everything",
        ["Comfort/DebugGizmos"] = "developer switch — draws comfort gizmos into the scene",
        ["Hands/TestFist"] = "true forces a FULL fist (curl 1.0 on all five fingers of both hands) "
            + "regardless of controller input: no pointing, no fingertip hex touch, no laser origin "
            + "at the index tip — the mod's whole input vocabulary stops working, from a row called "
            + "'Debug: Faust erzwingen'",
        ["Cards/DevFakeHand"] = "spawns N dummy cards into the real card fan",
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
        // feed one-time migrations and the parked world tilt ([Rig] WorldTiltDegrees + the three MaskedReaim* companions, kept for
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
            item.Integral = false;
            // A COLOUR IS 0-1 CHANNELS and nothing about a length applies to it, so it keeps the
            // flat twentieth it always had. A VECTOR NO LONGER DOES: "vectors are board-local
            // METERS everywhere in this mod" was true and the conclusion drawn from it — one flat
            // 0.005 for all of them — was the largest hole in this file. It meant the 52 Vector2 /
            // Vector3 dials, which are ALL of the per-board furniture geometry, never reached
            // ResolveSteps at all: not the unit table, not the range, not the magnitude. The user's
            // report is one of them ([Cards] SlotOverlayOffset_{board}, a 5 mm press on a value of
            // 2 mm), and 25 of the 63 components he has hand-tuned are not multiples of 0.005 —
            // values his own arrows could not produce. They go through the same resolver as every
            // scalar now; the step falls out in the second pass with everything else.
            if (t == typeof(Color))
                item.BaseStep = 0.05d;
            return;
        }

        if (IsNumeric(t))
        {
            item.Kind = ConfigKind.Number;
            item.Integral = IsIntegral(t);
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

    /// <summary>
    /// |value| as a double, taking the LARGEST component of a vector or colour, or 0 for anything
    /// that is not a number at all (never throws).
    ///
    /// <para>The largest component and not the euclidean length, because the question this answers
    /// is "how big is the biggest thing this family is tuned to", one axis at a time — a length
    /// would make a diagonal offset read as larger than either of its axes.</para>
    /// </summary>
    private static double Magnitude(object? boxed)
    {
        if (boxed == null)
            return 0d;
        if (boxed is Vector2 v2)
            return Math.Max(Math.Abs(v2.x), Math.Abs(v2.y));
        if (boxed is Vector3 v3)
            return Math.Max(Math.Abs(v3.x), Math.Max(Math.Abs(v3.y), Math.Abs(v3.z)));
        if (boxed is Vector4 v4)
            return Math.Max(Math.Max(Math.Abs(v4.x), Math.Abs(v4.y)),
                            Math.Max(Math.Abs(v4.z), Math.Abs(v4.w)));
        if (!IsNumeric(boxed.GetType()))
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
    /// Fill in every entry's <see cref="ConfigItem.BaseStep"/>, once the whole catalog is known.
    ///
    /// <para>WHY THIS IS A SECOND PASS and not part of <see cref="Classify"/>. The step depends on
    /// the largest magnitude in the entry's FAMILY (<see cref="ConfigSteps.FamilyOf"/>) — every
    /// component of a vector, every board of a per-board family, every axis of a pose — and no
    /// entry can know that while the catalog is still being read. Reading it from the entry's own
    /// value instead is the defect three separate user reports were about; the argument is written
    /// out at <c>ConfigSteps.FamilyOf</c>.</para>
    ///
    /// <para>THE DEFAULT AND NOT THE CURRENT VALUE, throughout: the shipped value is a statement
    /// about the dial's scale, the tuned one is where this player happens to have left it, and a
    /// step that drifted as you turned the dial would be its own kind of unusable. A colour keeps
    /// the flat step <see cref="Classify"/> gave it (0-1 channels, no family, no unit).</para>
    /// </summary>
    private static void ResolveSteps(List<ConfigItem> items)
    {
        var scale = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            ConfigItem item = items[i];
            if (item.Kind != ConfigKind.Number && item.Kind != ConfigKind.Components)
                continue;
            double own = OwnScale(item);
            string family = ConfigSteps.FamilyOf(item.Section, item.Key);
            if (!scale.TryGetValue(family, out double best) || own > best)
                scale[family] = own;
        }

        for (int i = 0; i < items.Count; i++)
        {
            ConfigItem item = items[i];
            if (item.Kind == ConfigKind.Number
                || (item.Kind == ConfigKind.Components && item.Entry.SettingType != typeof(Color)))
            {
                scale.TryGetValue(ConfigSteps.FamilyOf(item.Section, item.Key), out double family);
                item.BaseStep = ConfigSteps.Resolve(item.Section, item.Key, family, item.Integral);
            }
        }
    }

    /// <summary>
    /// What ONE entry says about its own scale: its declared range's width, or its shipped
    /// default's magnitude when it declares no range. A declared range is a real statement of scale;
    /// a single default is only the best available stand-in — and a default of ZERO is no statement
    /// at all, which this reads through <see cref="ConfigSteps.OwnScale"/> so that an epsilon-zero
    /// (<c>1.1175871e-10</c>, the shape the in-VR sliders and BepInEx's float32 round-trip give an
    /// explicit zero) is treated as the zero it is rather than as a scale of one ten-billionth.
    /// See <see cref="ConfigSteps.ZeroMagnitude"/> for the measurement behind the threshold, and
    /// for the five card-overlay families whose step this collapsed from a millimetre to a micron.
    ///
    /// <para>THE ARITHMETIC LIVES IN <see cref="ConfigSteps"/>, not here, because the wire guard's
    /// <c>ConfigStepVectors.FamilyScale</c> has to be the same function and not a copy of it —
    /// otherwise the guard checks a different question from the one this menu answers.</para>
    ///
    /// <para>THE FAMILY TAKES THE LARGEST OF THESE and every member then steps by that one number.
    /// It has to be one number or the invariant collapses in a way that is invisible from inside the
    /// headset: <c>[Cards] SpawnSideMeters</c> declares 0.00…1.20 while its two siblings
    /// <c>SpawnDownMeters</c> and <c>SpawnForwardMeters</c> declare −0.50…1.50, so deriving each
    /// axis from its OWN range gave the side axis a 0.005 press beside their 0.01 — three dials of
    /// one spawn pose, two of which feel like the ones that work. That is the same defect as
    /// GloveOffsetX arriving through the range instead of through the magnitude, and it is why the
    /// max is taken over the family rather than per entry.</para>
    /// </summary>
    private static double OwnScale(ConfigItem item) =>
        ConfigSteps.OwnScale(item.HasRange, item.Min, item.Max, Magnitude(item.Entry.DefaultValue));

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
        // [RoundButtons] USED TO BE THE THIRD NAME HERE (user, hardware ModBuild 96: "Weiterhin
        // vermisse ich die Einstellungen im Debug Menu für genau diese 'Überspringen'-Tasten"), and
        // it is gone because the section is. The skip cap's dials WERE its siblings' dials all
        // along in the misfiled sense that report described; since 2026-08-25 they are its siblings'
        // dials literally — the cap is a generic board keycap and reads [BoardButtons], which the
        // first name below already claims.
        if (string.Equals(section, "BoardButtons", StringComparison.Ordinal)
            || string.Equals(section, "RestButtons", StringComparison.Ordinal))
            return ConfigTopic.BoardGeometry;

        // …AND THE SHARED KEYCAP SEAT FAMILY, BY NAME, because it stopped carrying a board suffix on
        // 2026-08-25 and would otherwise fall all the way through to the module table and file under
        // "Karten & Brett". These five were fifteen per-board entries (ConfirmUndoOffset_{board} and
        // friends) that the HasBoardSuffix test above claimed automatically; collapsing them to one
        // shared entry each — the per-board part of a keycap seat is the recess the board itself cut,
        // see CardsConfig's seat-family note — took that claim away with the suffix. Naming them is
        // the price of the collapse, and the alternative was leaving a dial the control-board page
        // shows in its own tree ALSO listed on a second page, which is the exact duplication this
        // method exists to prevent.
        if (string.Equals(section, "Cards", StringComparison.Ordinal)
            && (key is "ConfirmUndoOffset" or "ButtonStackSpacing"
                    or "RestButtonOffset" or "RestStackSpacing" or "RestButtonDiameter"))
            return ConfigTopic.BoardGeometry;

        // SECTION BEATS MODULE for the four sections that RIDE ALONG on another module's file
        // (2026-08-22 settings audit (c), Findings 2 and 3). All four are bound by
        // Rig/RenderQuality.Bind into dev.gloomhavenvr.rig.cfg — [Sky], [Elements], [Haunt] and
        // [EnvSound] have no file of their own — so the module table at the bottom of this method
        // filed every one of them under "Bild & Darstellung", which put the mod's whole audio
        // surface under a heading that says PICTURE. They are not properties of how the world is
        // drawn; they are properties of WHICH world, and of what it sounds like. Testing the
        // section here rather than the module costs one switch and cannot be undone by a later
        // module rename.
        switch (section)
        {
            case "EnvSound":
                return ConfigTopic.Sound;
            case "Sky":
            case "Elements":
            case "Haunt":
                return ConfigTopic.Environment;
        }

        if (string.Equals(module, ModuleConfig.MainModule, StringComparison.Ordinal))
        {
            switch (section)
            {
                case "General":
                case "Core":
                    return ConfigTopic.System;
                case "Dev":
                    return ConfigTopic.Diagnostics;
                case "MapRoom":
                    // The 3D map room's own dials file with the SWITCH that turns the room on —
                    // [Rig] Vanilla2DMap, which the "Rig" case below sends to the same topic.
                    // A player who just found that switch is then on the same page as the dials
                    // that tune what it turned on, which is the whole complaint the per-board page
                    // was rearranged for. (They also FOLD OUT under it: VROptionsTab's
                    // DependentSections hides the whole [MapRoom] section while the switch is off.)
                    //
                    // THE PAIR MOVED FROM Visual TO Environment at the 2026-08-22 audit, together,
                    // so the Erweitert index reads the same way the curated tab does: which world
                    // you stand in is not the same question as how it is rasterised. THE RULING
                    // "Symbolgrößen gehören ins ERWEITERT Menü!" IS UNTOUCHED — all five [MapRoom]
                    // size dials are still Erweitert-only; only the topic HEADING they sit under
                    // changed, and they are still adjacent and still in Pinned() reading order.
                    return ConfigTopic.Environment;
                case "Hands":
                    return ConfigTopic.Hands;
                case "Compat":
                    // Everything in [Compat] is a rendering fixup except the component blacklist.
                    return key == "DisableComponents" ? ConfigTopic.System : ConfigTopic.Visual;
                case "Rig":
                    // [Rig] is genuinely two things: where the world sits, and how it is drawn.
                    // VoidColor and ForwardRendering stood in this list until the 2026-08-22
                    // settings audit UNBOUND both (a: "Etwas was das spiel kaputt macht wenn man es
                    // umstellt ist nicht optional") — the campaign-map switch is what is left of
                    // the "how it is drawn" half, and the test stays a list because the next such
                    // key costs one word here rather than a second branch.
                    //
                    // RENAMED AND INVERTED AT ModBuild 230 (user ruling: the 3D map room is the
                    // default and the dial names the opt-out). The TOPIC did not move with it —
                    // "which campaign map you get" is the same "which world you stand in" question
                    // whichever way the switch is phrased — but the KEY had to, and this line is
                    // the one place in the catalog that names it. Getting it wrong would not throw:
                    // the row would simply file under Movement, i.e. land on the wrong Erweitert
                    // page, silently.
                    return key is "Vanilla2DMap"
                        ? ConfigTopic.Environment
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
            // "boardfade" IS MULTIPLAYER. [PeerBoardFade] is what a MITSPIELER's control board does
            // while it blocks your view of the field, and until the 2026-08-22 audit it had no line
            // here at all — so all six dials fell through to "Sonstiges", the collector that exists
            // so an UNMAPPED module is still reachable rather than as a filing decision. (They are
            // reachable at all only because NetModule.BindConfig calls PeerBoardFadeTuning.Bind
            // eagerly; the class binds lazily on the first peer board otherwise.) Its everyday
            // switch is curated in Avatar & Mehrspieler ▸ Zusammen spielen and its five thresholds
            // now sit one level down under the same subject.
            "net" or "boardfade" => ConfigTopic.Network,
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
        // [MapRoom] ONCE contributed two entries, below MinClusterSize, so FoldSmallGroups swept
        // them into the topic's "Allgemein" collector and this pin was a rescue: it floated them to
        // the top of a grab-bag. The section ships FIVE dials now, clears the threshold and owns its
        // own heading — so the pin has stopped being a rescue and has become what it should have
        // been all along, THE READING ORDER OF THE FAMILY.
        //
        // WHY THE ORDER IS WRITTEN DOWN RATHER THAN LEFT ALPHABETICAL. These five became the only
        // door to the map dials when the user sent them here ("Symbolgrößen gehören ins ERWEITERT
        // Menü!", ModBuild 196) — the curated rows on Grafik are gone, see VROptionsTab.4.Curated.
        // (The topic they sit in is "Umgebung" since the 2026-08-22 audit, not "Bild & Darstellung";
        // the ruling is about ERWEITERT vs curated and is untouched by which heading Erweitert
        // files them under — see TopicOf's "MapRoom" case.)
        // Sorting them by Display splits the two dials the PREVIOUS report exists to compare:
        // "Symbole Weltkarte" and "Symbole Stadtkarte" would end up two marker rows apart, because
        // "Gloomhaven-Marker" and "Gruppen-Marker" sort between them. The user asked to be able to
        // "separat justieren" world-map symbols against city-map symbols, which means seeing both
        // numbers at once. So: the two icon populations first and adjacent, then the capital's
        // marker (the door between the two maps), then the two things on the map that are not
        // symbols, marker before route — the same order the curated page shipped.
        if (section == "MapRoom")
        {
            return key switch
            {
                "IconScale" => 0,
                "CityIconScale" => 1,
                "GloomhavenIconScale" => 2,
                "PartyMarkerScale" => 3,
                "PathWidthScale" => 4,
                _ => 5,
            };
        }
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
            // [Rig] ForwardRendering was the one entry this arm existed for; it is a const since
            // the 2026-08-22 settings audit (Plugin.cs) and there is nothing left in [Rig] whose
            // read site was checked and found to be startup-only.
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
        ("WorldUI", "ModalStyle") => new object[] { "window", "screen" },
        ("Cards", "RevealMode") => new object[] { "tilt", "always" },

        // THE FIVE CARD SOUNDS, 2026-08-22 audit (d): "Prüfe für jede Einstellung die
        // Bedienmöglichkeit". They are game audio-item METHOD NAMES, so the generic classifier fell
        // to ReadOnly and shipped five rows a player could read and not change — which is worse
        // than no row, because it looks like a control that is broken. The option sets are not
        // invented: each name is one the entry's OWN bound description already names as the shipped
        // value or as a verified alternative "found in GH.Runtime", and all nine were re-checked
        // against the decompiled game before being listed here (the rule this table's doc states:
        // never cycle through values nobody verified). "Empty = silent" is deliberately NOT an
        // option — a blank dropdown row reads as a bug; a cfg that carries "" stays honestly
        // read-only, exactly as CuratedChoices' membership test promises.
        ("Cards", "FanRevealSound") => CardSounds,
        ("Cards", "FanHideSound") => CardSounds,
        ("Cards", "CardGrabSound") => CardSounds,
        ("Cards", "CardPlaceSound") => CardSounds,
        ("Cards", "CardTakeBackSound") => CardSounds,
        _ => null,
    };

    /// <summary>
    /// The card-sound option set — ONE list for all five entries, because the five moments they
    /// mark are interchangeable by construction (any of these items can play at any of them) and
    /// five near-identical lists would be five places for a typo. Ordered from the softest click to
    /// the most emphatic, which is the order someone auditioning them wants.
    /// </summary>
    private static readonly object[] CardSounds =
    {
        "PlaySound_UICardTabSelect",
        "PlaySound_CardUI_SelectCard",
        "PlaySound_UIButtonSelect",
        "PlaySound_EnemyCardDraw",
        "PlaySound_CardUI_DiscardedCard",
        "PlaySound_CardUI_BurnedCard",
        "PlaySound_ScenarioUI_TileConfirm",
        "PlaySound_UIUndoHex",
        "PlaySound_ScenarioUIUndo",
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
        "Elements" => Loc.Mod("cfg_sec_elements"),
        "Haunt" => Loc.Mod("cfg_sec_haunt"),
        "EnvSound" => Loc.Mod("cfg_sec_envsound"),
        "MapRoom" => Loc.Mod("cfg_sec_maproom"),
        "FigureGrab" => Loc.Mod("figure_offsets"),
        // Reached since per-variant keys group by SECTION rather than by the hand style in their
        // name; without it a German menu would head the block with the English "Hands".
        "Hands" => Loc.Mod("hands"),
        // The Mehrspieler topic's single group was headed with the raw section word "Net" —
        // the one place the whole page showed an internal name (menu audit 05, S4).
        "Net" => Loc.Mod("cfg_topic_network"),
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
        return VariantWords.Contains(word) ? SectionLabel(item.Section) : GroupWordLabel(word);
    }

    /// <summary>
    /// Localized heading for an automatic key-prefix cluster (menu audit 05, S4 / §2.4 — the
    /// GroupWordLabel switch): <c>GroupWord</c> used to return the leading key word RAW, so a
    /// German page was headed with the English words "Fan", "Item", "Held", "Screen", "Combat",
    /// "Bar" — <c>SectionLabel</c> localizes only section names, and prefix clusters had no
    /// table at all.
    ///
    /// <para>Only the prefix words the catalog demonstrably produces are listed (the [Cards] and
    /// [WorldUI] sections are the two past the split threshold; both now ALSO carry hand-built
    /// trees, so this switch is the safety net for entries added after those trees were arranged
    /// and for any section that grows past the threshold later). A word not listed keeps its raw
    /// form — readable, never blank, same degradation as an untranslated section.</para>
    /// </summary>
    private static string GroupWordLabel(string word) => word switch
    {
        "Fan" => Loc.Mod("cfg_gw_fan"),
        "Card" => Loc.Mod("cfg_gw_card"),
        "Item" => Loc.Mod("cfg_gw_item"),
        "Held" => Loc.Mod("cfg_gw_held"),
        "Tray" => Loc.Mod("cfg_gw_tray"),
        "Board" => Loc.Mod("cfg_gw_board"),
        "Spawn" => Loc.Mod("cfg_gw_spawn"),
        "Reveal" => Loc.Mod("cfg_gw_reveal"),
        "Pile" => Loc.Mod("cfg_gw_pile"),
        "Slot" => Loc.Mod("cfg_gw_slot"),
        "Screen" => Loc.Mod("cfg_gw_screen"),
        "Combat" => Loc.Mod("cfg_gw_combat"),
        "Bar" => Loc.Mod("cfg_gw_bar"),
        "Bars" => Loc.Mod("cfg_gw_bar"),
        "Hex" => Loc.Mod("cfg_gw_hex"),
        // [FigureGrab] passed SectionSplitThreshold long ago, so its nine "Prop*" keys have always
        // formed a cluster of their own on Erweitert ▸ Hände & Figuren — headed with the raw
        // English word "Prop" in a German menu, because nothing here named it. It went unnoticed
        // while those nine were ALSO a curated section; the 2026-09-05 ruling (b) sent them here
        // for good ("Sowas wie Offsets etc. gehört da nicht hin"), so the heading they arrive under
        // has to be the one they arrived with: "Map-Items in der Hand", the words the curated
        // section used.
        "Prop" => Loc.Mod("cfg_gw_prop"),
        "Flat" => Loc.Mod("cfg_gw_screen"),
        "Map" => Loc.Mod("cfg_gw_map"),
        "Net" => Loc.Mod("cfg_topic_network"),
        _ => word,
    };

    /// <summary>
    /// The key's leading word — the automatic cluster name inside an oversized section
    /// ("FanArcSweepDegrees" → "Fan", "BarSizeScale" → "Bar", "VRSettingsOffset" → "VR").
    /// Bounded by the key length; never returns empty for a non-empty key.
    /// </summary>
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
                    return v is bool b && b ? Loc.Mod("vr_on") : Loc.Mod("vr_off");
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
        ConfigTopic.Environment => Loc.Mod("cfg_topic_environment"),
        ConfigTopic.Sound => Loc.Mod("cfg_topic_sound"),
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
