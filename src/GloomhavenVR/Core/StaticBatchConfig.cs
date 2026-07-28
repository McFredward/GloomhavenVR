using BepInEx.Configuration;

namespace GloomhavenVR.Core;

/// <summary>What <see cref="StaticBatcher"/> is allowed to do this session.</summary>
internal enum BatchMode
{
    /// <summary>Nothing at all. Any batch already applied is handed back on the next tick.</summary>
    Off,

    /// <summary>Measure and report only — never touches a single game object.</summary>
    Probe,

    /// <summary>Measure, report, then combine.</summary>
    On,
}

/// <summary>
/// Config surface of the EXPERIMENTAL runtime static batching pass
/// (<c>dev.gloomhavenvr.batching.cfg</c>, canonical <see cref="ModuleConfig"/> pattern).
///
/// <para>ITS OWN FILE, deliberately. Everything here is one experiment with one rollback: delete
/// the file and the mod is back to what it shipped. Folding fourteen entries into
/// <c>[Optimize]</c> — whose contract is "one entry switches exactly ONE optimization" — would
/// have buried them among the A/B switches and made "turn the whole thing off" a fourteen-line
/// edit instead of a one-line one.</para>
///
/// <para>WHY THE DEFAULT IS <see cref="BatchMode.Off"/>. This is the only lever in the 2026-07
/// performance investigation that MUTATES GAME OBJECTS rather than render state. It is reversible
/// by construction (see <see cref="StaticBatcher"/>'s ledger and <see cref="StaticBatchInterop"/>),
/// but "reversible" is not "invisible", and the mod's standing rule is that anything a player can
/// see change has to be switched on deliberately. <see cref="BatchMode.Probe"/> exists so the FIRST
/// hardware round answers "is this even possible here" without a single mutation.</para>
///
/// <para>MULTIPLAYER. Nothing here travels. Static batching rewrites which vertex buffer a local
/// renderer draws from; it changes no game state, no card identity, no wire byte, and two peers
/// with different settings see the same game — one of them just submits fewer draw calls. So this
/// is compatible with the standing multiplayer requirement by construction rather than by
/// arrangement, and it is the one lever in this investigation for which that is true for free.</para>
/// </summary>
internal static class StaticBatchConfig
{
    private static ConfigFile? _file;

    /// <summary>True once <see cref="Bind"/> has run (entries are safe to read).</summary>
    internal static bool IsBound => _file != null;

    /// <summary>Off / Probe / On — the master switch.</summary>
    internal static ConfigEntry<BatchMode> Mode = null!;

    /// <summary>Scene-root object names the pass is allowed to walk (comma-separated).</summary>
    internal static ConfigEntry<string> Roots = null!;

    /// <summary>Fall back to finding the busiest scene roots when no configured name matches.</summary>
    internal static ConfigEntry<bool> AutoDetectRoots = null!;

    /// <summary>A root with fewer eligible renderers than this is left alone.</summary>
    internal static ConfigEntry<int> MinRenderers = null!;

    /// <summary>Hard ceiling on the vertices copied into combined meshes, per pass.</summary>
    internal static ConfigEntry<int> MaxVertices = null!;

    /// <summary>Seconds after a scene load before the pass runs (procedural geometry settles).</summary>
    internal static ConfigEntry<float> SettleSeconds = null!;

    /// <summary>Seconds between "did new geometry appear" re-checks (0 = never re-check).</summary>
    internal static ConfigEntry<float> RescanSeconds = null!;

    /// <summary>How many new eligible renderers a re-check needs before it batches again.</summary>
    internal static ConfigEntry<int> RescanGrowth = null!;

    /// <summary>Also batch renderers on INACTIVE objects (unrevealed rooms).</summary>
    internal static ConfigEntry<bool> IncludeInactive = null!;

    /// <summary>Watch a rotating sample of batched objects for movement.</summary>
    internal static ConfigEntry<bool> Watchdog = null!;

    /// <summary>Hand everything back automatically the moment the watchdog sees movement.</summary>
    internal static ConfigEntry<bool> WatchdogAutoRevert = null!;

    /// <summary>Add the offender's name to the exclusion list and try again, instead of just giving up.</summary>
    internal static ConfigEntry<bool> WatchdogAutoExclude = null!;

    /// <summary>Layer names/indices never batched (comma-separated; the mod's own layer always is).</summary>
    internal static ConfigEntry<string> ExcludeLayers = null!;

    /// <summary>Object-name fragments never batched (comma-separated, case-insensitive).</summary>
    internal static ConfigEntry<string> ExcludeNames = null!;

    /// <summary>Extra component type NAMES that disqualify an object and its subtree.</summary>
    internal static ConfigEntry<string> ExcludeComponents = null!;

    /// <summary>Release the CPU-side copy of each combined mesh after it is uploaded.</summary>
    internal static ConfigEntry<bool> FreeCombinedCpuCopy = null!;

    /// <summary>Name every rejected renderer and its reason (very loud — one line per object).</summary>
    internal static ConfigEntry<bool> VerboseLog = null!;

    // ---- safe accessors ---------------------------------------------------------------------
    // The driver's Update runs before anything guarantees Bind() succeeded (an unwritable config
    // directory, a hot reload mid-frame). Each accessor answers with the SHIPPED default while
    // unbound, so an unbound config can never turn the experiment on by accident.

    /// <summary>The mode, defaulting to <see cref="BatchMode.Off"/> while unbound.</summary>
    internal static BatchMode CurrentMode => Mode == null ? BatchMode.Off : Mode.Value;

    internal static string RootNames => Roots == null ? "Maps" : Roots.Value ?? string.Empty;

    internal static bool AutoRoots => AutoDetectRoots == null || AutoDetectRoots.Value;

    internal static int MinRenderersPerRoot =>
        MinRenderers == null ? 8 : UnityEngine.Mathf.Clamp(MinRenderers.Value, 2, 2000);

    internal static int VertexBudget =>
        MaxVertices == null ? 4_000_000 : UnityEngine.Mathf.Clamp(MaxVertices.Value, 10_000, 20_000_000);

    internal static float Settle =>
        SettleSeconds == null ? 4f : UnityEngine.Mathf.Clamp(SettleSeconds.Value, 0f, 60f);

    internal static float Rescan =>
        RescanSeconds == null ? 30f : UnityEngine.Mathf.Clamp(RescanSeconds.Value, 0f, 300f);

    internal static int Growth =>
        RescanGrowth == null ? 8 : UnityEngine.Mathf.Clamp(RescanGrowth.Value, 1, 1000);

    internal static bool Inactive => IncludeInactive == null || IncludeInactive.Value;

    internal static bool WatchdogOn => Watchdog == null || Watchdog.Value;

    internal static bool AutoRevert => WatchdogAutoRevert == null || WatchdogAutoRevert.Value;

    internal static bool AutoExclude => WatchdogAutoExclude == null || WatchdogAutoExclude.Value;

    internal static bool FreeCpuCopy => FreeCombinedCpuCopy == null || FreeCombinedCpuCopy.Value;

    internal static bool Verbose => VerboseLog != null && VerboseLog.Value;

    /// <summary>
    /// Bind-once against the batching module's own config file. Lazy and idempotent: called from
    /// the driver's install, from the settings-panel accessors and from the config browser's
    /// force-bind list, so no module-init ordering is assumed.
    /// </summary>
    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("batching");

        Mode = _file.Bind("Batching", "Mode", BatchMode.Off,
            "EXPERIMENTAL, AND PROBABLY UNNECESSARY NOW — read the last paragraph before switching "
            + "it on. What the runtime static-batching pass may do. OFF (the shipped default) does "
            + "nothing at all and hands back anything already combined. PROBE measures the scene and "
            + "writes one [Batch] PROBE line saying how many renderers COULD be combined, over how "
            + "many materials, at what memory cost — and mutates NOTHING, so it is safe to leave on. "
            + "ON does the same and then combines. Why this exists: the VR frame's wall was "
            + "main-thread DRAW-CALL SUBMISSION (culling 0.08 ms against 21.5 ms of submission), and "
            + "a scenario submits ~1481 renderers over only ~103 distinct materials — 14.4 renderers "
            + "per material. Static batching is the one lever that can collapse that ratio, because "
            + "it needs no shader variants (unlike single-pass stereo and unlike GPU instancing, "
            + "which cannot help here: this dungeon's geometry is generated procedurally, so nearly "
            + "every renderer has a mesh of its own). The game's own developers shipped the same call "
            + "on a debug hotkey (PerformanceUtility.Combine → Shift+F2 → "
            + "StaticBatchingUtility.Combine on 'Maps'). "
            + "SUPERSEDED 2026-07-28: it works — 1666 renderers combined into 47 meshes, submitted "
            + "material slots 1208 → ~132, worth about 8 ms — but the real fix turned out to be "
            + "THREADED RENDER SUBMISSION ([Core] EnableGraphicsJobs, on by default), which took the "
            + "main-thread render loop from 14.9 ms to 1.8 ms and the headset from 45 Hz to 90 Hz. "
            + "With that in place there is almost nothing left on the main thread for batching to "
            + "win, and whether it still helps at all is UNMEASURED. It costs ~100 MB and a ~60 ms "
            + "pause per scenario load, so leave it off unless you are testing weak hardware. See "
            + ".planning/perf/FINDINGS.md.");

        Roots = _file.Bind("Batching", "Roots", "Maps",
            "Comma-separated names of SCENE-ROOT objects the pass may walk. Nothing outside these "
            + "roots is ever touched. The default is the single root the game's own batching hotkey "
            + "uses and the one the [Perf] SCENE line shows carrying essentially the whole dungeon "
            + "('Maps/L : (guid)' 791 renderers, 'Maps/G : (guid)' 580, 'Maps/I : (guid)' 69 — 1440 "
            + "of the scenario's 1481 submitted renderers). Read the SCENE line's "
            + "'by scene-root/child group' list for the names in YOUR scenario; a name that matches "
            + "nothing is reported and ignored, never guessed at.");

        AutoDetectRoots = _file.Bind("Batching", "AutoDetectRoots", true,
            "When NONE of the names in Roots exists in the loaded scene(s), fall back to finding the "
            + "scene roots that hold the most mesh objects and using those instead. WHY THIS IS ON BY "
            + "DEFAULT: Roots is free text, and free text is the one thing the in-VR config browser "
            + "can only SHOW — a player whose scenario names its geometry root something other than "
            + "'Maps' could not fix it from inside the headset, and the feature would silently do "
            + "nothing with no way out. This is the way out, and it is not a guess in the dark: the "
            + "mod's own roots are skipped, a candidate still has to clear MinRenderers, at most four "
            + "are taken, and the [Batch] PROBE line NAMES every root it picked and every root it "
            + "considered with its mesh count — so an auto-detected pass is never a mystery, and the "
            + "right name for Roots can be read straight off the log. Off = only ever use the "
            + "configured names, and report that none of them exists.");

        MinRenderers = _file.Bind("Batching", "MinRenderers", 8, new ConfigDescription(
            "A root offering fewer eligible renderers than this is skipped. Combining a handful of "
            + "objects costs memory and buys nothing measurable, and every object combined is one "
            + "more entry in the ledger that has to be handed back on revert.",
            new AcceptableValueRange<int>(2, 2000)));

        MaxVertices = _file.Bind("Batching", "MaxVertices", 4_000_000, new ConfigDescription(
            "Hard ceiling on the vertices copied into combined meshes in one pass. THIS IS THE "
            + "MEMORY GUARD, and it is the real cost of static batching: the combined mesh is a "
            + "COPY of every vertex it holds, in both system and video memory, on top of the "
            + "originals (which stay alive so the pass can be undone). At a typical ~44 bytes per "
            + "vertex, 4 million vertices is roughly 170 MB per copy. Candidates are taken in scan "
            + "order until the budget is spent; the rest stay unbatched and the [Batch] APPLY line "
            + "says exactly how many were left out, so a truncated pass can never look like a "
            + "complete one.",
            new AcceptableValueRange<int>(10_000, 20_000_000)));

        SettleSeconds = _file.Bind("Batching", "SettleSeconds", 4f, new ConfigDescription(
            "Seconds to wait after a scene load before batching. This dungeon's geometry is "
            + "generated procedurally over several frames, and an object combined before it exists "
            + "is simply not combined — so the pass waits rather than racing the generator. Also "
            + "applied after a manual 'apply now'.",
            new AcceptableValueRange<float>(0f, 60f)));

        RescanSeconds = _file.Bind("Batching", "RescanSeconds", 30f, new ConfigDescription(
            "Seconds between 'did new geometry appear' re-checks (0 = never re-check). Rooms are "
            + "revealed as you explore, so a single pass at scenario start cannot cover a scenario "
            + "that grows. The re-check is cheap — it walks the configured ROOTS only (a handful of "
            + "objects), not the whole scene — and it only combines again when at least "
            + "RescanGrowth new eligible renderers have appeared. Anything that appears between two "
            + "re-checks simply renders unbatched, which is correct, just not merged.",
            new AcceptableValueRange<float>(0f, 300f)));

        RescanGrowth = _file.Bind("Batching", "RescanGrowth", 8, new ConfigDescription(
            "How many NEW eligible renderers a re-check has to find before it combines again. Low "
            + "values chase every single spawned prop and pay a combine for it; high values leave "
            + "a newly opened room unbatched until the next big change.",
            new AcceptableValueRange<int>(1, 1000)));

        IncludeInactive = _file.Bind("Batching", "IncludeInactive", true,
            "Also combine renderers whose GameObject is currently INACTIVE — the rooms you have not "
            + "opened yet. ON means one pass at scenario start covers the whole map, and revealing a "
            + "room does not drop it out of the batch. This is a genuine capability rather than a "
            + "flag: Unity's own StaticBatchingUtility.Combine(root) skips inactive objects because "
            + "it collects them with FindObjectsOfType, while the array overload this pass uses does "
            + "not — the eligibility rule Unity applies is renderer.enabled, not activeInHierarchy. "
            + "OFF restricts the pass to what is on screen at the time, which is the conservative "
            + "reading if a scenario ever turns out to REBUILD rooms on reveal rather than reveal "
            + "them.");

        Watchdog = _file.Bind("Batching", "Watchdog", true,
            "Watch a rotating sample of combined objects for MOVEMENT. This is the one way static "
            + "batching can go visibly wrong: a combined object's vertices are baked into the "
            + "batch root's space, so its own transform stops driving where it is drawn — if the "
            + "game ever moves one, it renders at the place it was combined. The watchdog compares "
            + "each sampled object's pose RELATIVE TO ITS BATCH ROOT against the pose it was "
            + "combined at (so moving the whole root, or the mod's world grab, is correctly not a "
            + "movement), names the offender in the log, and — with WatchdogAutoRevert on — hands "
            + "the whole pass back on the spot. Costs a few dozen matrix compares per second.");

        WatchdogAutoRevert = _file.Bind("Batching", "WatchdogAutoRevert", true,
            "When the watchdog sees a combined object move, undo the whole pass immediately instead "
            + "of only logging it. ON is the safe default: the alternative is a correct log line "
            + "next to a wrong picture. OFF keeps the batch so the misplaced object can be "
            + "photographed and identified — a diagnostic setting, not a preference.");

        WatchdogAutoExclude = _file.Bind("Batching", "WatchdogAutoExclude", true,
            "When the watchdog catches a combined object moving, add ITS NAME to ExcludeNames and "
            + "try the pass again, instead of only undoing it. THIS IS WHAT MAKES THE FEATURE "
            + "SELF-CORRECTING, and the 2026-07 hardware round is why it exists: the first pass "
            + "combined 1666 renderers and cut the submitted material slots 1225 → ~130, then an "
            + "object called 'Glow' moved one second later and the whole thing was correctly handed "
            + "back — leaving a working optimisation unusable over a handful of torch flames. "
            + "Excluding by NAME rather than by object is deliberate: these things come in families, "
            + "and one name covers all of them. The name is written into your cfg so the next "
            + "scenario starts out already knowing it, and the log names every addition, so an "
            + "exclusion that turns out to be too broad can simply be deleted. Bounded by the same "
            + "give-up counter as before, so a scene full of movers ends in one clear verdict rather "
            + "than an endless combine/undo cycle. OFF = undo and stop, as before.");

        ExcludeLayers = _file.Bind("Batching", "ExcludeLayers", "",
            "Layer names or indices never combined, comma-separated (e.g. 'Hero, Monster'). Empty = "
            + "exclude nothing extra. The mod's OWN layer is always excluded and cannot be re-added "
            + "here — combining the hands, cards or control board would freeze them in place, and "
            + "the mod does not get to break itself through a config typo. The [Perf] SCENE line "
            + "prints every layer by name with its renderer count. Unknown names are reported and "
            + "ignored, never silently applied.");

        ExcludeNames = _file.Bind("Batching", "ExcludeNames", "",
            "Object-name fragments never combined, comma-separated, case-insensitive (e.g. "
            + "'Door, Chest'). Empty = exclude nothing by name. This is the escape hatch for the "
            + "case the watchdog is there to catch: if the log names an object that moved, put a "
            + "piece of its name here and the rest of the map still batches.");

        ExcludeComponents = _file.Bind("Batching", "ExcludeComponents", "",
            "EXTRA component type names that disqualify an object AND everything under it, "
            + "comma-separated. Animator, Animation and Rigidbody are excluded unconditionally and "
            + "do not need listing — they are the three components that mean 'this transform is "
            + "driven', which is exactly what static batching cannot survive. This entry is for "
            + "game-specific movers found later; matching is on the type's short name.");

        FreeCombinedCpuCopy = _file.Bind("Batching", "FreeCombinedCpuCopy", true,
            "After a combined mesh is uploaded to the GPU, release its system-memory copy "
            + "(Mesh.UploadMeshData(true)). This halves the memory the pass costs and cannot affect "
            + "what is drawn — the GPU buffer is what renders, and Unity's own build-time static "
            + "batching produces combined meshes that are not readable either. It also cannot "
            + "affect the undo: handing the pass back restores each object's ORIGINAL mesh and "
            + "destroys the combined one; nothing ever reads the combined mesh back. OFF keeps the "
            + "CPU copy, which is only useful for inspecting it.");

        VerboseLog = _file.Bind("Batching", "VerboseLog", false,
            "Name every REJECTED renderer and the reason it was rejected — one log line per object, "
            + "so a scenario with 1700 renderers writes 1700 lines. Off, the [Batch] PROBE line "
            + "still reports every rejection reason as a COUNT, which is what a decision needs; "
            + "this is for the case where a specific object has to be found by name.");
    }
}
