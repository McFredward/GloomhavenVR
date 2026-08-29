using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR;

/// <summary>
/// GloomhavenVR entry point. Binds config, and — only when enabled — creates the
/// Harmony instance and initializes all feature modules. With
/// <c>[General] Enabled = false</c> the plugin is a strict no-op and the game
/// runs 100% vanilla.
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    /// <summary>Master switch. When false the plugin does nothing at all.</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>How much the mod writes to the log. Live — changing it takes effect on the next
    /// line, no restart.</summary>
    internal static ConfigEntry<VRLogLevel> LogLevel = null!;

    /// <summary>
    /// Optional path to an OpenXR runtime JSON (sets <c>XR_RUNTIME_JSON</c>).
    /// Empty = enumerate installed runtimes (registry + well-known paths) with failover.
    /// </summary>
    internal static ConfigEntry<string> RuntimeOverride = null!;

    /// <summary>
    /// Order in which OpenXR runtimes are attempted. "auto" (default) = system default
    /// first, then VDXR when Virtual Desktop is streaming, then the rest (SteamVR last).
    /// </summary>
    internal static ConfigEntry<string> RuntimePriority = null!;

    /// <summary>Escape hatch: single init attempt on the system default runtime, no XR_RUNTIME_JSON fiddling.</summary>
    internal static ConfigEntry<bool> SkipRuntimeCandidates = null!;

    /// <summary>
    /// Write Unity's graphics-jobs keys into boot.config (read by the PRELOADER, which is the only
    /// place early enough to matter — see <c>GloomhavenVR.Preload.Patcher.EnsureGraphicsJobs</c>).
    /// Bound here so it appears in the config file and in the in-VR config browser like every other
    /// setting; the plugin itself never reads it.
    /// </summary>
    internal static ConfigEntry<bool> EnableGraphicsJobs = null!;

    /// <summary>
    /// Let the preloader close and reopen the game once, on the single boot at which
    /// <see cref="EnableGraphicsJobs"/> is newly written. Read by the PRELOADER only; bound here for
    /// the config file and the in-VR browser, like <see cref="EnableGraphicsJobs"/> itself.
    /// </summary>
    internal static ConfigEntry<bool> AutoRestartForGraphicsJobs = null!;

    /// <summary>Escape hatch: delay mod init (and thus VR init) by N rendered frames.</summary>
    internal static ConfigEntry<int> InitDelayFrames = null!;


    /// <summary>
    /// Multiplayer join placement (<see cref="Rig.SpawnRing"/>): seat an arriving VR player on a
    /// ring around the BOARD at the azimuth that maximises the minimum angular distance to every
    /// peer already present, facing the board, just outside the board's own footprint. Replaces
    /// the old evenly-spaced-by-participant-index circle, which measured its azimuth in a
    /// per-client frame and ignored where peers actually stood — hence "spawnt man direkt hinter
    /// oder IN der anderen Maske" ("you spawn directly behind, or INSIDE, the other mask"). No
    /// effect single-player; the key name is kept so existing
    /// .cfg files keep loading.
    /// </summary>
    internal static ConfigEntry<bool> SpawnInCircle = null!;

    /// <summary>[Rig] Vanilla2DMap — show the game's ORIGINAL FLAT 2D CAMPAIGN MAP instead of the
    /// 3D map room. Default <c>false</c>: a fresh install stands IN the map.
    ///
    /// <para>THE RULING THAT INVERTED IT (ModBuild 230, verbatim): <i>"Die '3D-Map' Einstellung ist
    /// nicht mehr Experimentell und sollte der Standart sein. Nenne die neue Einstellung eher so
    /// etwas wie 'Vanilla 2D map' oder etwas ähnliches passendes was standartmäßig aus sein soll.
    /// Die Einstellung soll außerhalb von Erweitert einstellbar sein."</i> Until 229 this entry was
    /// <c>[Rig] Experimental3DMap</c> (default false, ON built the room). Not one line of the
    /// FEATURE changed here — the DIAL did: the key now names the thing that is off, so the mod's
    /// default presentation is the map room and the flat map is the opt-out. The single reader
    /// inverted with it (<c>WorldUI/MapRoom/MapRoomDriver.cs:224</c>), and so did the four
    /// dependency rules that fold the map-room family (<c>VROptionsTab.8.Dependencies.cs</c>).</para>
    ///
    /// <para>THE DOC THAT USED TO STAND HERE WAS FALSE, and had been for seventy-odd builds. It
    /// read "RESERVED placeholder, currently unimplemented" and "no code reads <c>.Value</c>
    /// (refactor Batch D, verified at HEAD)", which stopped being true at ModBuild 158 when the
    /// map room shipped behind this very switch. Two derived documents inherited the error and
    /// still carry it (<c>.planning/menu-audit/03-core-rig-perf.md:30</c> "DEAD … zero readers",
    /// <c>docs/INTERFACES-P2.md:339</c> "reserved, unimplemented placeholder") — this is the
    /// original, corrected at the source. It matters mechanically as well as editorially: the
    /// prefix "RESERVED —" is one of <c>ConfigCatalog.RetiredMarkers</c>, so a description that
    /// had ever been rewritten to match that doc would have deleted the row from every menu page
    /// in the mod (<c>ConfigCatalog.Describe</c> → <c>IsRetired</c> → <c>return null</c>).</para>
    ///
    /// <para>MULTIPLAYER: nothing on the wire depends on which side of this switch a peer is on.
    /// Extension records 20 and 21 (the map room and its shared windows) are written only while
    /// this client's own room stands — <c>RemoteMapRoom.Sample</c>/<c>RemoteMapStory.Sample</c>
    /// are guarded by <c>MapRoomDriver.Active</c> — and both <c>Resolve</c> passes return on the
    /// same comparison, so a peer on the flat map neither sends nor applies them and its packets
    /// stay byte-identical to a pre-record build (<c>Net/NetAvatarDriver.cs:1665-1670, 2931-2933</c>).
    /// The handshake compares <c>NetProtocol.ModBuild</c> and nothing else, so a mixed table is
    /// legal; what changes at 230 is only which side is the majority.</para></summary>
    internal static ConfigEntry<bool> Vanilla2DMap = null!;

    /// <summary>[Rig] MapPresentationMigrated230 — one-shot migration marker, not a setting.
    /// False on a fresh install; set true the first time a build carries a pre-230 install's
    /// <c>[Rig] Experimental3DMap</c> choice across to <see cref="Vanilla2DMap"/>. Kept out of the
    /// menu by <c>ConfigCatalog.NotOffered</c>, for the reason that table's own doc gives: setting
    /// a marker back to false does not undo its migration, it RE-ARMS it.</summary>
    internal static ConfigEntry<bool> MapPresentationMigrated230 = null!;

    /// <summary>
    /// The raw text of <c>[Rig] Experimental3DMap</c> as it stood in the main config FILE at the
    /// moment <c>Awake</c> began — before the first <c>Config.Bind</c> could write to it — or
    /// <c>null</c> when the file did not exist or did not carry that key.
    ///
    /// <para>THIS IS THE ONLY THING THAT CAN TELL THE THREE STARTING STATES APART, and the whole
    /// reason it is read from the file rather than from BepInEx. A renamed key is an ORPHAN: it is
    /// still in the .cfg (BepInEx 5 preserves orphaned entries across a save, which is why the line
    /// does not simply vanish at 230) but it is no longer bound, so <c>Config.TryGetEntry</c>
    /// cannot see it and <c>ConfigFile.OrphanedEntries</c> is <c>protected internal</c> in
    /// BepInEx.Core 5.4.20 — which this project references un-publicized (see GloomhavenVR.csproj:
    /// only the GAME assemblies carry <c>Publicize="true"</c>). Re-BINDING the old key to read it
    /// would defeat the purpose: BepInEx returns the shipped default when the key is absent, so a
    /// fresh install and a returning player who left the switch at its old default would both read
    /// <c>false</c> and be indistinguishable. The file itself is the only place where "the key is
    /// not there at all" is a value.</para></summary>
    private static string? _legacy3DMapRaw;

    /// <summary>
    /// [MapRoom] Size factor for the campaign map's LOCATION ICONS in the 3D map room
    /// (<c>WorldUI.MapRoom.MapIconLayer</c>). 1 = the size the icons shipped at. Read live on every
    /// icon tick, so a step in the options pane shows on the next frame with no reload. The flat
    /// map's own icon draw (<c>FlatScreenStereo.3.Map.DrawMapIcons</c>) never reads it — "Nur in
    /// der 3D Umgebung", by construction rather than by a guard.
    /// </summary>
    internal static ConfigEntry<float> MapIconScale = null!;

    /// <summary>
    /// [MapRoom] Size factor for the GLOOMHAVEN icon alone — the capital's own marker, identified
    /// by the game's classification <c>MapLocation.MapLocationType == Headquarters</c> and not by
    /// any name or texture (the reasoning is in <c>MapIconLayer.IsCapital</c>). Overrides
    /// <see cref="MapIconScale"/> for that one icon; both default to 1, so nothing changes until a
    /// dial is turned.
    /// </summary>
    internal static ConfigEntry<float> MapGloomhavenIconScale = null!;

    /// <summary>
    /// [MapRoom] Size factor for the icons on the GLOOMHAVEN CITY map in the 3D map room — the
    /// shopfronts (merchant, enhancer, temple, trainer) and the City-type quests, i.e. everything
    /// the room draws while <c>MapChoreographer.cityMap</c> is the active map.
    ///
    /// <para>WHY IT EXISTS (user report against ModBuild 192: "Trenne die Größe des Symbole auf der
    /// Weltkarte und die Symbole auf der Karte für Gloomhaven. Die müssen separat justiert
    /// werden."). Until this build <see cref="MapIconScale"/> governed every non-capital icon on
    /// WHICHEVER map was on screen, so one number had to serve two populations authored at
    /// different sizes. <see cref="MapIconScale"/> keeps its meaning for the world map — a dropped
    /// .cfg value in this project is always against the newest build, and his tuned 2.30 was
    /// measured there — and this entry takes the city map. Defaults to 1, so the city icons return
    /// to their authored size until this dial is turned; that is the separation he asked for, not a
    /// regression. See <c>MapIconLayer</c>'s class doc for the source that establishes the two
    /// populations and why they can never overlap in one drawn frame.</para>
    /// </summary>
    internal static ConfigEntry<float> MapCityIconScale = null!;

    /// <summary>
    /// [MapRoom] Size factor for the PARTY MARKER — the token showing where the party currently is
    /// (<c>MapChoreographer.m_PartyToken</c>).
    ///
    /// <para>WHY IT EXISTS (user request against ModBuild 193: "Ich will auch die Größe des Markers
    /// wo man sich befindet sowie des eingezeichneten Weges von einem zum anderen Punkt einstellen
    /// können"). Through ModBuild 193 <c>MapIconLayer</c>'s own class doc stated that this token
    /// "takes NO dial and cannot", because it is drawn with <c>CommandBuffer.DrawRenderer</c> from
    /// the game's renderers. That verdict was too narrow: the same renderer's mesh can be issued as
    /// <c>CommandBuffer.DrawMesh</c> with a matrix of our choosing, which scales the marker in OUR
    /// recording and writes nothing to the game's transform. At 1 the old call is issued unchanged.
    /// See <c>MapIconLayer.DrawToken</c>.</para>
    /// </summary>
    internal static ConfigEntry<float> MapPartyMarkerScale = null!;

    /// <summary>
    /// [MapRoom] Width factor for the DRAWN ROUTE between two map locations — both the active path
    /// to the place you are pointing at and the permanent roads between unlocked villages, which the
    /// game builds from one prefab and one routine (decompiled MapLocation.cs:783-806 and
    /// :1015-1054).
    ///
    /// <para>Applied through the <c>LineRenderer</c>'s <c>widthMultiplier</c>, which multiplies the
    /// game's own randomly-ragged width CURVE — so the road keeps its hand-drawn profile and only
    /// gets thicker or thinner as a whole. This is the ONE game-component write the map room's icon
    /// layer makes; the original is recorded before the first write, re-asserted level-triggered and
    /// restored on stand-down. The game itself never writes <c>widthMultiplier</c> on a map line
    /// (grep of the whole decompile: only <c>ClientScenarioManager</c> and <c>RFX4_ParticleTrail</c>,
    /// neither on the map), so there is no write war to lose.</para>
    /// </summary>
    internal static ConfigEntry<float> MapPathWidthScale = null!;

    /// <summary>
    /// [Rig] Demeo-style world tilt (degrees, 0-60) — FEATURE PARKED (user ruling 2026-08:
    /// "macht zu viele Probleme, vorerst entfernen"). The entry stays bound so a tuned value
    /// survives in the .cfg, but the runtime clamps the effective tilt to 0
    /// (VRRigDriver.WorldTilt.cs, <c>TargetTiltDegrees</c>) and the UI row is gone.
    /// Revival = remove that one clamp + restore the curated row (VROptionsTab.4.Curated.cs).
    /// </summary>
    internal static ConfigEntry<float> WorldTiltDegrees = null!;

    /// <summary>
    /// [Rig] Masked tilt re-aim: head yaw angular speed (deg/s) above which the frozen tilt
    /// axis may silently rotate toward the current view (VRRigDriver.TickWorldTilt).
    /// Dormant while the world tilt is parked (tilt is forced to 0).
    /// </summary>
    internal static ConfigEntry<float> MaskedReaimHeadRate = null!;

    /// <summary>[Rig] Masked tilt re-aim: correction speed as a fraction of the head's yaw speed (subthreshold gain). Dormant while the world tilt is parked.</summary>
    internal static ConfigEntry<float> MaskedReaimGain = null!;

    /// <summary>[Rig] Masked tilt re-aim: view-vs-tilt yaw errors below this (degrees) are ignored entirely. Dormant while the world tilt is parked.</summary>
    internal static ConfigEntry<float> MaskedReaimDeadband = null!;

    /// <summary>Disable PPv2 (PostProcessLayer/PostProcessVolume) while VR runs (P1 default: on).</summary>
    internal static ConfigEntry<bool> DisablePostProcessing = null!;

    /// <summary>Disable VolumetricFogAndMist.VolumetricFog while VR runs.</summary>
    internal static ConfigEntry<bool> DisableVolumetricFog = null!;

    /// <summary>Extra comma-separated component type full names to disable while VR runs.</summary>
    internal static ConfigEntry<string> DisableComponents = null!;

    /// <summary>
    /// [Compat] Whole-wall see-through fade in VR (OFF = walls always solid, the VR
    /// default). Consulted live by Core.WallSegmentFade each frame.
    /// </summary>
    internal static ConfigEntry<bool> WallFade = null!;
    internal static ConfigEntry<bool> ControlsLesson = null!;


    /// <summary>Dominant hand ("Right"/"Left") — its ray is the default pick source.</summary>
    internal static ConfigEntry<string> PrimaryHand = null!;

    /// <summary>Visible hand model style (Glove/Plate/Arcane). Live-rebuilds on change
    /// (HandsDriver) and is synced to other VR players (Net.AvatarState.HandStyle).</summary>
    internal static ConfigEntry<Hands.HandStyle> HandStyle = null!;

    // ---- per-STYLE hand tunables (indexed by (int)Hands.HandStyle: Glove/Plate/Arcane) ----
    // The three hand models differ hugely in raw bulk (measured on the prepped meshes,
    // all normalized to the same 0.19 m wrist->middle-tip length): knuckle-region width
    // glove 0.123 m vs plate 0.198 m, palm thickness glove 0.042 m vs plate 0.084 m /
    // arcane 0.072 m, four-finger MCP span glove 0.074 m vs plate 0.101 m / arcane
    // 0.113 m — the armored styles read 1.5-2x too big at scale 1. These entries scale
    // each style independently; the seat pose itself is per style too, in HandsConfig
    // ([Hands] {Style}GripPitchDegrees / {Style}LateralOffset / …, measured defaults).

    /// <summary>Uniform visual scale per style (1 = author size). Applied live by VRHand.SyncVisualOffset.</summary>
    internal static ConfigEntry<float>[] HandStyleScale = null!;

    /// <summary>Visible laser starts at the index fingertip instead of the aim pose (test #6).</summary>
    internal static ConfigEntry<bool> LaserFingerOrigin = null!;

    /// <summary>Scroll lists with the stick only — a press whose sole drag target is the scroll
    /// view stays a clean click (UguiPointer.Press). NOTE (2026-08 dead-settings sweep): the
    /// audit called this dead; it is NOT — the reader is live and load-bearing.</summary>
    internal static ConfigEntry<bool> ScrollWithStickOnly = null!;

    /// <summary>Fine-tune: beam start offset (meters, along the beam) from the index fingertip.</summary>
    internal static ConfigEntry<float> LaserFingerOffsetMeters = null!;
    internal static ConfigEntry<string> HandColor = null!;

    /// <summary>[Hands] HandColor hex → Color; falls back to the default on parse failure.</summary>
    internal static UnityEngine.Color ParseHandColor()
    {
        string hex = HandColor?.Value?.Trim().TrimStart('#') ?? "D9C9B5";
        if (UnityEngine.ColorUtility.TryParseHtmlString("#" + hex, out UnityEngine.Color c))
            return c;
        return new UnityEngine.Color(0.85f, 0.79f, 0.71f);
    }

    /// <summary>
    /// Clear colour of the owned head camera — the void around the floating menu screen and
    /// outside the diorama. BLACK, and no longer a dial.
    ///
    /// <para>2026-08-22 settings audit (user, verbatim): <i>"a) Lösche alle Einstellungen die das
    /// Spiel breaken könnten wenn die verändert werden. Etwas was das spiel kaputt macht wenn man
    /// es umstellt ist nicht optional und sollte daher nicht einstellbar sein."</i> [Rig] VoidColor
    /// was a <c>Color</c>, so the generic row builder gave it FOUR numeric steppers (R/G/B/A) —
    /// the exact control shape <c>HasSpecialRow</c> was written to replace on
    /// <c>MixedReality/KeyColor</c> — and its own description said it existed "For DEBUGGING".
    /// The harm, and the value that causes it: any non-black clear colour is painted over the
    /// whole field of view outside the diorama, and under Mixed Reality it is painted over the
    /// CHROMA KEY, so passthrough stops keying and the room disappears. There is no reading of
    /// "the void" a player is choosing between. The diagnostic use survives: one edit here, one
    /// rebuild — which is what a diagnostic is.</para>
    /// </summary>
    internal static readonly UnityEngine.Color VoidColor = UnityEngine.Color.black;

    /// <summary>
    /// FORWARD rendering on the mod's head camera, instead of the game's DeferredShading. Always
    /// on, and no longer a dial.
    ///
    /// <para>This is the fix for transparent effects (fire/torch glow, hex selection ring, health
    /// bars) rendering THROUGH walls in VR: the sky depth-reset renderer (queue 1999, ZTest
    /// Always) has no deferred pass, so on a deferred camera it runs in the forward-opaque
    /// fallback AFTER the walls and wipes their depth, leaving nothing for transparents to test
    /// against. Forward rendering restores strict queue order (reset 1999 runs BEFORE walls 2000,
    /// walls overwrite it), so the depth buffer keeps the walls and transparents occlude
    /// correctly.</para>
    ///
    /// <para>2026-08-22 settings audit, and the sharpest case of the three the audit named. It was
    /// a CURATED row on Grafik ▸ Darstellung, which is as prominent as a row gets. The harm, and
    /// the value that causes it: <c>false</c> reinstates the exact see-through-walls defect the
    /// forward path exists to fix, AND it silently disables the curated MSAA row two lines above
    /// it — <c>[RenderQuality] MsaaLevel</c>'s own text says "Requires the forward rendering path
    /// ([Rig] ForwardRendering)". It is also start-up-only, so a player who flips it sees nothing
    /// happen, plays on, and meets both consequences at the next launch with no memory of the
    /// cause. The one trade the old description offered — "disable ONLY if forward lighting looks
    /// wrong (deferred handles many dynamic lights per pixel; forward has a per-object light
    /// limit)" — is served by <c>[RenderQuality] PixelLightCount</c>, which is a live dial with a
    /// visible effect, and not by a start-up switch that also takes the wall occlusion with
    /// it.</para>
    /// </summary>
    internal const bool ForwardRendering = true;

    /// <summary>Master dev switch: event bus + hands run without an HMD, dev console installed.</summary>
    internal static ConfigEntry<bool> DevMode = null!;

    /// <summary>Show the dev overlay on startup (toggle at runtime with F10).</summary>
    internal static ConfigEntry<bool> DevOverlay = null!;

    /// <summary>Animate fake hands on the desktop (toggle at runtime with F8).</summary>
    internal static ConfigEntry<bool> SimulateHands = null!;

    /// <summary>Dump UnityEngine.XR.InputDevices every N seconds (0 = off).</summary>
    internal static ConfigEntry<float> InputDeviceDumpInterval = null!;

    private Harmony? _harmony;

    /// <summary>
    /// Feature module registry. Order matters: Core first (XR bootstrap), Compat last
    /// AMONG THE FEATURE MODULES (its fixups must land on top of everything else). The
    /// Dev harness is appended after Compat and is inert unless <c>[Dev] Enabled</c> —
    /// <c>DevModule.Init</c> returns immediately otherwise, and even when enabled it only
    /// adds a <c>DevConsole</c> overlay GameObject: it applies no fixups, patches nothing
    /// and touches no game state, so it cannot get between Compat and anything.
    /// See <see cref="RegisterModules"/>; shutdown runs this list in REVERSE.
    /// </summary>
    private readonly List<IVRModule> _modules = [];

    private void Awake()
    {
        VRLog.Init(Logger);

        // READ THE CONFIG FILE BEFORE THE FIRST BIND TOUCHES IT. BepInEx writes the whole file on
        // the first Bind (ConfigFile.Save runs when SaveOnConfigSet is on, which is the default),
        // so this line has to stand ahead of every Bind in Awake — including the [General] Enabled
        // one below — or the snapshot would be of a file the mod has already rewritten. It costs
        // one File.ReadAllLines of a ~200-line text file, once per process, and it is the input to
        // the ModBuild 230 map-presentation one-shot further down (see _legacy3DMapRaw's doc for
        // why nothing in the BepInEx API can answer this).
        _legacy3DMapRaw = ReadRawConfigValueBeforeBind(Config.ConfigFilePath, "Rig", "Experimental3DMap");

        // The main plugin config is the one file the mod does NOT create through
        // ModuleConfig.Create (BaseUnityPlugin owns it), so it is registered by hand — otherwise
        // the in-VR config browser (Debug ▸ Alle Einstellungen) could not reach the cross-cutting
        // [General]/[Core]/[Rig]/[Hands]/[Compat]/[Dev] sections. Registered BEFORE the first bind
        // so the browser sees the file even if a later bind ever throws.
        Core.ModuleConfig.Register(Core.ModuleConfig.MainModule, Config);

        Enabled = Config.Bind(
            "General", "Enabled", Defaults.General_Enabled,
            "Master switch. Set to false to run the game completely vanilla (the mod does nothing).");
        LogLevel = Config.Bind(
            "General", "LogLevel", Defaults.LogLevel,
            "How much the mod writes to LogOutput.log. Off = silent. Errors = only what failed. " +
            "Warnings = also what degraded silently (a missing asset, a fallback engaging) — the " +
            "floor at which a bug report is still worth reading. Normal = also the few lines that " +
            "say which build is running and whether VR came up. Verbose = also every subsystem's " +
            "running commentary, several hundred lines a session. Trace = everything, including " +
            "debug chatter. Trace is the DEFAULT for now, so the log is exactly what it has always " +
            "been; drop to Warnings or Normal once the current round of debugging is done. Takes " +
            "effect on the next line — no restart.");
        VRLog.Level = LogLevel.Value;
        // BUILD BANNER (added after a hardware round was accidentally run on the previous
        // build and its log misread as a fix failure): ModBuild is the same monotonic
        // counter the MP version handshake compares, so the log's first lines now prove
        // exactly which build produced them. Note-level: survives every verbosity that can
        // still print at all.
        VRLog.Note("Core", $"GloomhavenVR ModBuild {Net.NetProtocol.ModBuild} " +
                           $"(assembly {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}).");
        LogLevel.SettingChanged += (_, _) =>
        {
            VRLog.Level = LogLevel.Value;
            // Deliberately a Note: it survives every level that can still print anything, so the
            // log always records the moment its own verbosity changed. Without that, a log that
            // suddenly goes quiet looks like a hang.
            VRLog.Note("Core", $"log level is now {LogLevel.Value}.");
        };
        RuntimeOverride = Config.Bind(
            "General", "RuntimeOverride", Defaults.RuntimeOverride,
            "Optional path to an OpenXR runtime JSON file (e.g. SteamVR's steamxr_win64.json). " +
            "Sets XR_RUNTIME_JSON before XR init and is tried first. Leave empty to auto-detect " +
            "(active runtime from the registry, then all available runtimes, then well-known paths).");
        RuntimePriority = Config.Bind(
            "Core", "RuntimePriority", Defaults.RuntimePriority,
            "Order in which OpenXR runtimes are attempted. 'auto' = system default runtime first " +
            "(what the OS/registry points at), then VDXR when the Virtual Desktop Streamer is " +
            "running, then remaining installed runtimes with SteamVR last (attempting SteamVR " +
            "boots its compositor). Or a comma-separated list of: default, vdxr, steamvr, oculus, " +
            "or full paths to runtime JSON files — tried in exactly that order.");
        SkipRuntimeCandidates = Config.Bind(
            "Core", "SkipRuntimeCandidates", Defaults.SkipRuntimeCandidates,
            "Escape hatch: make a single init attempt on the system default OpenXR runtime and " +
            "never set XR_RUNTIME_JSON (no candidate failover). Use when the failover itself " +
            "causes trouble (e.g. it keeps booting runtimes you don't use).");
        EnableGraphicsJobs = Config.Bind(
            "Core", "EnableGraphicsJobs", Defaults.EnableGraphicsJobs,
            "Let the mod switch Unity's THREADED RENDER SUBMISSION on for you, by writing "
            + "gfx-enable-gfx-jobs and gfx-enable-native-gfx-jobs into GH_Data/boot.config. "
            + "This is the single largest performance finding of the whole project. Unity normally "
            + "submits every draw call on ONE thread — the same thread that must finish before a "
            + "frame can be shown — and in a scenario that thread was the entire bottleneck. "
            + "Measured 2026-07-28, same scene and same build, changing nothing but this: "
            + "main-thread render loop 14.9 ms -> 1.8 ms, head camera 13.4 ms -> 1.45 ms, frame "
            + "17.5 ms -> 11.14 ms, and the headset went from locked at 45 Hz to a clean 90 Hz. The "
            + "reported ghosting on head movement disappeared entirely. IT TAKES EFFECT AT THE NEXT "
            + "GAME START: the engine reads boot.config before any mod code exists, which is also "
            + "why the mod cannot simply set this at runtime. The original boot.config is copied to "
            + "boot.config.gloomhavenvr-backup before the first edit; setting this to false writes "
            + "the keys back to 0 on the next start. Passing -force-gfx-jobs yourself in the launch "
            + "options overrides this and the file is then left alone. If the game ever fails to "
            + "start, restore that backup by hand — the mod cannot help you there, because it never "
            + "runs.");
        AutoRestartForGraphicsJobs = Config.Bind(
            "Core", "AutoRestartForGraphicsJobs", Defaults.AutoRestartForGraphicsJobs,
            "On the ONE boot where the setting above is newly written, close the game and start it "
            + "again automatically, so you get the performance immediately instead of being told to "
            + "restart yourself. This happens once after installing or updating the mod, takes a few "
            + "seconds, and cannot happen twice in a row: the relaunched process is marked as such "
            + "and refuses to relaunch again, and a counter in BepInEx/patchers/GloomhavenVR/ caps it "
            + "at two attempts until the setting actually takes hold. Nothing of yours is at risk — "
            + "this runs during engine startup, before any save or campaign is loaded. Set to false "
            + "if you would rather quit and start the game yourself; the log then says so. Has no "
            + "effect at all when EnableGraphicsJobs is false, when you pass -force-gfx-jobs "
            + "yourself, or once boot.config already has the setting — which is every start after "
            + "the first.");
        InitDelayFrames = Config.Bind(
            "Core", "InitDelayFrames", Defaults.InitDelayFrames,
            "Escape hatch: delay mod initialization (including OpenXR init) by this many rendered " +
            "frames. Some runtime/GPU combos need the graphics device fully up before " +
            "xrCreateSession works. 0 (default) = initialize immediately in plugin Awake.");
        // [Rig] MenuRig is GONE (user ruling 2026-08-13). Its OFF did not produce "a static
        // viewpoint": VRRigDriver's desired kind fell to RigKind.None outside a scenario, so the
        // mod built NO rig at all — no head tracking, no tracked anchor for the hands, and no
        // anchor for the floating 2D screen that carries the main menu. That is the same class of
        // brick the 2026-08-11 round removed [WorldUI] Master and FlatScreen for. The menu rig is
        // unconditional now (VRRigDriver.UpdateBody).
        SpawnInCircle = Config.Bind(
            "Rig", "SpawnInCircle", Defaults.SpawnInCircle,
            "Multiplayer: when you join a session or enter a scenario, seat you ACROSS the board " +
            "from the players already there — with one other player that is exactly opposite them, " +
            "looking straight at their mask; with several, in the widest free gap — instead of " +
            "inside or behind somebody else's head. You are placed AT THE TABLE EDGE on that side " +
            "(the board's real edge in that direction plus a standing clearance), so neither over " +
            "the board nor parked far in front of it, at any board size or zoom level. Placement " +
            "happens ONCE on arrival — plus at most one correction in the first seconds if a " +
            "player shows up right after you — and it stops for good the moment you move yourself. " +
            "Purely local: nothing extra is sent over the network. No effect in single-player / " +
            "offline (the solo seat is unchanged). Off = every player keeps the same shared seat " +
            "as before. Every outcome is written to the log (seated / waiting for a peer / no " +
            "board yet / off).");
        // ---- [Rig] Vanilla2DMap — WHICH CAMPAIGN MAP YOU GET, and it ships OFF -----------------
        //
        // The description must NOT begin "RESERVED —", "LEGACY — no effect" or "DEPRECATED —":
        // those three prefixes are ConfigCatalog.RetiredMarkers and any of them takes the row out
        // of every page of the menu (ConfigCatalog.Describe → IsRetired → return null), which is
        // the exact opposite of the ruling this entry exists to serve.
        Vanilla2DMap = Config.Bind(
            "Rig", "Vanilla2DMap", Defaults.Vanilla2DMap,
            "OFF BY DEFAULT, and off is the 3D map room: you stand IN the campaign map, which is " +
            "a table-sized parchment you walk around and lean over, and in multiplayer you see " +
            "each other there. Turn this ON to get the game's ORIGINAL FLAT 2D MAP back instead, " +
            "on the 2D screen, unchanged in every detail. Everything the room adds goes with it " +
            "when you do: the pressable location icons, the travel confirmation, the shared map " +
            "windows and story pages, the map-room card hand and the five [MapRoom] size dials, " +
            "which is why the options pane folds all of those away while this is on. The room was " +
            "shipped as [Rig] Experimental3DMap up to ModBuild 229 and was off by default; it is " +
            "the default presentation from 230 and this entry is the way back, so an existing " +
            "config's choice is carried across once, automatically ([Rig] " +
            "MapPresentationMigrated230). Multiplayer: which map you are on is a purely local " +
            "choice — nothing about it is negotiated, no packet depends on it, and two players on " +
            "different presentations is a supported table. Everything is written to the log — " +
            "look for MAP ROOM ENGAGED and MAP SCENE REPORT.");

        // ---- THE ModBuild 230 ONE-SHOT: an INVERTED, RENAMED key must not silently re-decide ---
        //
        // WHY IT IS NEEDED AT ALL. BepInEx reads the FILE, not the shipped default, so flipping a
        // default only ever reaches a FRESH install — the same fact the ModBuild 227 one-shot in
        // Core/PerfConfig.cs:474-518 was written for. Here it bites twice as hard, because the key
        // was RENAMED as well as inverted: to a returning player [Rig] Vanilla2DMap is a brand-new
        // entry that binds at its shipped default (false = the room), and their old
        // Experimental3DMap line becomes an orphan the mod no longer reads. Without this block a
        // player who deliberately chose the flat map would be put in the map room at the next
        // start with no line in the log saying why.
        //
        // THE THREE STARTING STATES, AND HOW THEY ARE TOLD APART. _legacy3DMapRaw is the raw text
        // of [Rig] Experimental3DMap as it stood in the file BEFORE the first bind (read at the top
        // of Awake), so "the key is not in the file" is itself a value here — which is what makes
        // the third state observable at all:
        //   * "true"  — they were IN the 3D map room  → Vanilla2DMap = false. Same picture.
        //   * "false" — they had the flat map         → Vanilla2DMap = true.  Same picture.
        //   * absent  — no cfg file, or a file written before the key ever existed: they never
        //               made this choice → nothing is written and the SHIPPED DEFAULT stands,
        //               i.e. the 3D map room. That is the ruling ("sollte der Standart sein"),
        //               and it is the only reading of "never touched it" the data can support: a
        //               bound entry is written to the file on first bind, so an install that ran
        //               any build from 158 onward carries the key whether or not a human ever
        //               looked at it, and "false in the file" cannot be told from "never opened
        //               the menu". Where the two readings disagree this one keeps the player's
        //               PICTURE stable, which is the promise the log line makes.
        //
        // A MARKER, NOT A FORCE — the 227 block's own words, and the reason the marker exists
        // rather than just the raw read: BepInEx 5 PRESERVES orphaned entries when it saves, so
        // the Experimental3DMap line stays in the file for ever and a re-read every start would
        // overwrite the player's later choice on every single boot. Keyed on the marker, the
        // transfer happens exactly once; after it, Vanilla2DMap is an ordinary switch and setting
        // it either way sticks. Setting the marker back to false re-runs the one-shot.
        MapPresentationMigrated230 = Config.Bind(
            "Rig", "MapPresentationMigrated230", Defaults.MapPresentationMigrated230,
            "One-shot migration marker, not a setting. Do not edit. FALSE on a fresh install; set "
            + "TRUE the first time this build carries a pre-ModBuild-230 config's [Rig] "
            + "Experimental3DMap choice across to [Rig] Vanilla2DMap, which replaced it and means "
            + "the opposite. Once it is true, Vanilla2DMap is an ordinary switch and stays wherever "
            + "you put it. Setting this back to false re-runs the one-shot against the old key, "
            + "which is still in this file as an unread leftover.");
        if (!MapPresentationMigrated230.Value)
        {
            MapPresentationMigrated230.Value = true;
            // bool.TryParse is the right reader and not merely a convenient one: BepInEx writes
            // bools with TomlTypeConverter, i.e. lowercase "true"/"false", and TryParse is
            // case-insensitive and whitespace-tolerant, so a hand-edited "True" migrates too. Any
            // value that is not a bool at all (a hand-edit typo) falls through to "leave it alone",
            // which lands the player on the new default rather than on a guess.
            if (_legacy3DMapRaw != null && bool.TryParse(_legacy3DMapRaw, out bool had3DMap))
            {
                bool wantVanilla = !had3DMap;
                if (Vanilla2DMap.Value != wantVanilla)
                    Vanilla2DMap.Value = wantVanilla;
                VRLog.Info("Core", "One-shot migration: [Rig] Experimental3DMap = "
                                   + (had3DMap ? "true" : "false") + " in this install's config "
                                   + "became [Rig] Vanilla2DMap = " + (wantVanilla ? "true" : "false")
                                   + " — the same map you had before, under the key that replaced "
                                   + "it and means the opposite. The 3D map room is the DEFAULT "
                                   + "presentation from ModBuild 230, so the new entry is the way "
                                   + "back to the flat 2D map rather than the way into the room. "
                                   + "This runs exactly once; the row is 'Originale 2D-Karte' under "
                                   + "Umgebung & Ton ▸ Kampagnenkarte and it stays wherever you put "
                                   + "it. The old key is left in the file as an unread leftover.");
            }
            else
            {
                VRLog.Info("Core", "One-shot migration: this config carries no readable [Rig] "
                                   + "Experimental3DMap line, so there is no earlier map choice to "
                                   + "carry over and [Rig] Vanilla2DMap keeps its shipped default "
                                   + "(off = the 3D map room, which is the default presentation "
                                   + "from ModBuild 230). Expected on a fresh install.");
            }
        }

        // ---- [MapRoom] the 3D map room's own dials --------------------------------------------
        // Bound in the MAIN config file next to [Rig] Vanilla2DMap, which is the switch that
        // decides whether these do anything at all — a player who found that switch is one section
        // away from the three dials that tune what it turned on.
        //
        // ONE DIAL PER ICON POPULATION (ModBuild 193, user report: "Trenne die Größe des Symbole
        // auf der Weltkarte und die Symbole auf der Karte für Gloomhaven"). The KEYS and DEFAULTS
        // of the two that already shipped are untouched — a dropped .cfg value in this project is
        // always against the newest build, and IconScale's tuned 2.30 was measured on the world
        // map, so it keeps governing exactly those icons. Only CityIconScale is new.
        MapIconScale = Config.Bind(
            "MapRoom", "IconScale", Defaults.MapIconScale,
            new ConfigDescription(
                "SIZE of the WORLD MAP's location icons WHILE YOU STAND IN THE 3D MAP ROOM "
                + "([Rig] Vanilla2DMap off) — the village, scenario and boss markers on the "
                + "parchment. Range 0.5-4, default 1 = the size they have always had, so nothing "
                + "changes until you move this; the floor is there because an icon scaled to "
                + "nothing is a scenario you can no longer point at. The factor multiplies each "
                + "icon's authored footprint, so every icon keeps its own proportions and its own "
                + "place on the map: it is a zoom of the marker, not a re-layout of the map. The "
                + "flat 2D map is NOT affected — this dial exists only on the room's draw path. "
                + "Applies live: the icon layer refills its draw list every frame, so the icons "
                + "resize as you step the value, no restart and no leaving the map. The Gloomhaven "
                + "marker has its own dial ([MapRoom] GloomhavenIconScale) and ignores this one, "
                + "and so does the whole Gloomhaven CITY map ([MapRoom] CityIconScale). The "
                + "invisible target area you point at grows WITH the icon: it is built from the "
                + "same number, so you keep hitting exactly what you see.",
                new AcceptableValueRange<float>(0.5f, 4f)));
        MapGloomhavenIconScale = Config.Bind(
            "MapRoom", "GloomhavenIconScale", Defaults.MapGloomhavenIconScale,
            new ConfigDescription(
                "SIZE of the GLOOMHAVEN marker alone in the 3D map room — the capital city, the one "
                + "location the party always returns to. Range 0.5-4, default 1 = the size it has "
                + "always had. Separate from [MapRoom] IconScale because the capital's icon is "
                + "authored much larger than a village pin and rarely wants the same factor; where "
                + "they disagree, this one wins for that single icon. It is picked out by the "
                + "GAME'S OWN classification of that location (its headquarters type), not by its "
                + "name or its artwork, so this keeps working in any language and after any art "
                + "update; there is exactly one such location per map. Applies live, 3D map room "
                + "only, flat map unaffected. Same hover-pad note as above.",
                new AcceptableValueRange<float>(0.5f, 4f)));
        MapCityIconScale = Config.Bind(
            "MapRoom", "CityIconScale", Defaults.MapCityIconScale,
            new ConfigDescription(
                "SIZE of the icons on the GLOOMHAVEN CITY map in the 3D map room — the merchant, "
                + "enhancer, temple and trainer shopfronts and the city's own quests, i.e. "
                + "everything you see after stepping from the world map into the city. Range "
                + "0.5-4, default 1 = the size they have always had. SEPARATE from [MapRoom] "
                + "IconScale, which now governs the WORLD map only: the two maps draw different "
                + "populations at different authored sizes, and one number could not serve both. "
                + "The game never shows both maps at once, so exactly one of the two dials is "
                + "acting at any moment — whichever map you are looking at. The floor is there for "
                + "the same reason as everywhere else: a shopfront scaled to nothing is a merchant "
                + "you can no longer point at. Applies live, 3D map room only, flat 2D map "
                + "unaffected; the invisible target area grows with the icon.",
                new AcceptableValueRange<float>(0.5f, 4f)));
        // THE TWO THINGS ON THE MAP THAT ARE NOT ICONS (ModBuild 194, user: "Ich will auch die Größe
        // des Markers wo man sich befindet sowie des eingezeichneten Weges von einem zum anderen
        // Punkt einstellen können"). Same section, same range, same default and the same 0.5 floor as
        // the three icon dials, because they are the same kind of comfort setting and a player who
        // found one row expects the next one to behave identically.
        MapPartyMarkerScale = Config.Bind(
            "MapRoom", "PartyMarkerScale", Defaults.MapPartyMarkerScale,
            new ConfigDescription(
                "SIZE of the PARTY MARKER in the 3D map room — the token that shows where your group "
                + "currently is, and that walks the route when you travel. Range 0.5-4, default 1 = "
                + "the size it has always had. Separate from the location icons: the marker is a "
                + "different object drawn a different way, and it is the one thing on the map you "
                + "look for first. The floor is there for the same reason as everywhere else — a "
                + "marker shrunk to nothing is a party you can no longer find. It is enlarged in the "
                + "room's own drawing, so the game's own map object is never modified and the flat "
                + "2D map is unaffected. Applies live, no restart. If part of the marker refuses to "
                + "grow, the log says so in numbers rather than leaving you to guess.",
                new AcceptableValueRange<float>(0.5f, 4f)));
        MapPathWidthScale = Config.Bind(
            "MapRoom", "PathWidthScale", Defaults.MapPathWidthScale,
            new ConfigDescription(
                "WIDTH of the ROUTE drawn between two places in the 3D map room — both the path to "
                + "the location you are pointing at and the permanent roads between the villages "
                + "you have unlocked. Range 0.5-4, default 1 = the width the game draws. The value "
                + "multiplies the game's own hand-drawn, deliberately ragged line, so a wider road "
                + "still looks drawn by hand and does not become a flat ribbon. This is the only "
                + "setting in the map room that changes something on the game's own map object; it "
                + "is put back exactly as it was the moment you leave the room, and it is never "
                + "sent to other players. Applies live, no restart.",
                new AcceptableValueRange<float>(0.5f, 4f)));
        WorldTiltDegrees = Config.Bind(
            "Rig", "WorldTiltDegrees", Defaults.WorldTiltDegrees,
            new ConfigDescription(
                "LEGACY — no effect (feature PARKED 2026-08 by user ruling: the world tilt " +
                "caused too many problems and is disabled for now; it may return later). This " +
                "was the Demeo-style world tilt in degrees (0-60): the entire play area " +
                "appeared tilted toward you, implemented rig-side and multiplayer-safe. The " +
                "value is kept so a tuned angle survives in this file, but the runtime clamps " +
                "the effective tilt to 0 regardless (VRRigDriver.WorldTilt.cs, " +
                "TargetTiltDegrees). Revival = remove that one clamp and restore the curated " +
                "options row.",
                new AcceptableValueRange<float>(0f, 60f)));
        MaskedReaimHeadRate = Config.Bind(
            "Rig", "MaskedReaimHeadRate", Defaults.MaskedReaimHeadRate,
            "LEGACY — no effect while the world tilt is parked (see [Rig] WorldTiltDegrees). " +
            "World tilt only: when you physically turn your body/head, the direction the " +
            "tilt tips toward is silently re-aimed to your view — but ONLY while your head " +
            "is rotating faster than this threshold (degrees per second), so the correction " +
            "is perceptually masked by your own motion (redirected-rotation technique). " +
            "Below the threshold the world stays bit-frozen. Default 30.");
        MaskedReaimGain = Config.Bind(
            "Rig", "MaskedReaimGain", Defaults.MaskedReaimGain,
            "LEGACY — no effect while the world tilt is parked (see [Rig] WorldTiltDegrees). " +
            "World tilt only: speed of the masked tilt re-aim as a fraction of your head's " +
            "yaw speed (0-0.5). 0.15 = the axis re-aims at 15% of however fast your head is " +
            "turning — far below the ~20% rotation-gain detection threshold, so the world " +
            "never visibly moves. Higher converges faster but risks being noticeable.");
        MaskedReaimDeadband = Config.Bind(
            "Rig", "MaskedReaimDeadband", Defaults.MaskedReaimDeadband,
            "LEGACY — no effect while the world tilt is parked (see [Rig] WorldTiltDegrees). " +
            "World tilt only: view-vs-tilt direction errors smaller than this (degrees) are " +
            "ignored — ordinary looking-around never triggers any correction, and a residual " +
            "misalignment this small is visually indistinguishable from a perfect aim.");
        DisablePostProcessing = Config.Bind(
            "Compat", "DisablePostProcessing", Defaults.DisablePostProcessing,
            "Disable PostProcessing v2 (PostProcessLayer/PostProcessVolume) while VR is active. " +
            "Phase 1 default: true (PPv2 is unverified under stereo rendering).");
        DisableVolumetricFog = Config.Bind(
            "Compat", "DisableVolumetricFog", Defaults.DisableVolumetricFog,
            "Disable the VolumetricFogAndMist.VolumetricFog image effect while VR is active.");
        DisableComponents = Config.Bind(
            "Compat", "DisableComponents", Defaults.DisableComponents,
            "Extra comma-separated component type full names (optionally 'FullName, Assembly') " +
            "to disable while VR is active, e.g. 'BeautifyEffect.Beautify'.");
        WallFade = Config.Bind(
            "Compat", "WallFade", Defaults.WallFade,
            "See-through walls in VR. When ON, a wall that stands between your head and the " +
            "part of the play area you are looking at fades out AS A WHOLE (soft dissolve, " +
            "~0.35s) down to its foundation course, and fades back in once it no longer " +
            "blocks the view. The decision is temporally smoothed (must persist ~0.4s), so " +
            "quick head movements never make walls flicker. OFF (default) keeps every wall " +
            "solid — the VR behavior so far. Purely visual and local (per-renderer material " +
            "property blocks): multiplayer peers are unaffected. Live-togglable from the VR " +
            "settings panel.");
        ControlsLesson = Config.Bind(
            "Compat", "ControlsLesson", Defaults.ControlsLesson,
            "Teach the VR controls during the first tutorial. Your hands become the controller " +
            "you are actually holding, the key you need lights up on it, and every step ends " +
            "when you DO the thing rather than when you have read about it: point and click, " +
            "reach and grab, drag the table, zoom and turn it, take a card, hold one properly " +
            "in your hand, fly, snap-turn, pick with a fingertip, reel a window in, ping a hex " +
            "and re-seat yourself. Any step can be passed with NEXT and the whole lesson with " +
            "SKIP, so it can never strand you. Runs in tutorial scenarios only, which are " +
            "single-player, so nothing about it goes on the wire. OFF: the tutorial behaves as " +
            "before.");
        // [Compat] TutorialVRAdapt is GONE (user ruling 2026-08-13). "OFF restores fully vanilla
        // tutorial behavior" was true and that is exactly the problem: vanilla's camera-
        // familiarization step waits on CameraRoomButtonPressed, whose only producer
        // (RoomCameraButton.OnClick) is unreachable while the rig patches park the game camera —
        // so the scripted hint chain DEADLOCKS right after the camera hint (hardware log
        // .planning/debug/tutorial/LogOutput.log:989). A dial that can strand the tutorial is not
        // an option. The bridge is unconditional now; it is still runtime-gated to tutorial
        // scenarios (TutorialVR.IsTutorialActive, which also refuses online sessions) and still
        // disarms itself permanently on any exception.
        PrimaryHand = Config.Bind(
            "Hands", "PrimaryHand", Defaults.PrimaryHand,
            "Dominant hand (Right/Left). Its index-finger ray is the default pick source " +
            "for board targeting.");
        HandStyle = Config.Bind(
            "Hands", "HandStyle", Defaults.Hands_HandStyle,
            "Which hand model to wear: Glove (leather glove, default), Plate (plate-armor " +
            "gauntlet) or Arcane (arcane-runes mage glove). Applies live (the hands rebuild " +
            "on change) and is synchronized in multiplayer so other VR players see your " +
            "chosen hands on your avatar. Falls back to Glove when the styled prefab is " +
            "missing from an older asset bundle, and to the procedural hand without any " +
            "bundle.");
        BindHandStyleEntries();
        LaserFingerOrigin = Config.Bind(
            "Hands", "LaserFingerOrigin", Defaults.LaserFingerOrigin,
            "Start the VISIBLE laser beam at the hand rig's index fingertip (converging on " +
            "the aim-pose ray's end point) so it reads as leaving the pointing finger. The " +
            "pick ray itself always uses the OpenXR aim pose. Off = beam starts at the aim " +
            "pose origin (controller).");
        ScrollWithStickOnly = Config.Bind(
            "Hands", "ScrollWithStickOnly", Defaults.ScrollWithStickOnly,
            "Scroll lists with the STICK only. A laser never holds perfectly still, so with " +
            "the drag threshold disabled every press also pans the list it is over, which is " +
            "what makes options hard to hit. With this on, a press whose only drag target is " +
            "the scroll view itself stays a clean click. Sliders, scrollbars and dropdowns are " +
            "unaffected — they resolve to themselves, not to the scroll view.");
        LaserFingerOffsetMeters = Config.Bind(
            "Hands", "LaserFingerOffsetMeters", Defaults.LaserFingerOffsetMeters,
            "Fine-tune for LaserFingerOrigin: how far (meters, along the beam) in front of " +
            "the index fingertip the visible beam starts.");
        HandColor = Config.Bind(
            "Hands", "HandColor", Defaults.HandColor,
            "Base color of the procedural hands as RRGGBB hex (no '#'). Must stay light " +
            "enough to read against the black void — the hand shader is unlit (the void " +
            "has no lights; lit shaders render black there). The left hand gets a slight " +
            "cool tint automatically so the sides stay distinguishable.");
        // [Rig] VoidColor and [Rig] ForwardRendering stood here. Both are CONSTANTS since the
        // 2026-08-22 settings audit — the argument for each, and the value that used to break the
        // game, is written at the constant (see VoidColor / ForwardRendering above). Their cfg
        // keys are gone with their binds; a stale line in an existing dev.gloomhavenvr.cfg is
        // simply round-tripped and read by nothing.
        DevMode = Config.Bind(
            "Dev", "Enabled", Defaults.Dev_Enabled,
            "Developer mode: wires the VR event bus and hand simulation even without an HMD " +
            "and installs the dev console (F8 sim hands, F9 poke Ready, F10 overlay).");
        DevOverlay = Config.Bind(
            "Dev", "Overlay", Defaults.Overlay,
            "Show the dev overlay on startup when dev mode is enabled (F10 toggles at runtime).");
        SimulateHands = Config.Bind(
            "Dev", "SimulateHands", Defaults.SimulateHands,
            "Animate fake hand transforms on the desktop (no HMD needed). Hold T = trigger, " +
            "G = grip. Toggle at runtime with F8. Ignored while real VR is running.");
        InputDeviceDumpInterval = Config.Bind(
            "Dev", "InputDeviceDumpInterval", Defaults.InputDeviceDumpInterval,
            "Log all UnityEngine.XR.InputDevices every N seconds (0 = off).");

        if (!Enabled.Value)
        {
            VRLog.Note("Disabled via config ([General] Enabled = false) — game runs vanilla.");
            return;
        }

        // Created up front so all modules/patch classes share one instance.
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        VRSession.Harmony = _harmony;
        VRSession.CoroutineHost = this;

        RegisterModules();

        // [Core] InitDelayFrames escape hatch: module init (VR init included) can be
        // deferred by N rendered frames — some runtime/GPU combos need the graphics
        // device fully up before xrCreateSession works. The whole module chain is
        // deferred (not just Core) because downstream modules gate their Init() on
        // VRSession.IsRunning. Default 0 = synchronous in Awake, exactly as before.
        int delayFrames = InitDelayFrames.Value;
        if (delayFrames > 0)
        {
            VRLog.Info($"[Core] InitDelayFrames = {delayFrames} — deferring module init " +
                       $"(VR init included) by {delayFrames} rendered frame(s).");
            StartCoroutine(DelayedInit(delayFrames));
        }
        else
        {
            InitModules();
            LogStartupSummary();
        }
    }

    /// <summary>
    /// Bind the per-STYLE <c>[Hands] {Style}Scale</c> entries (Glove/Plate/Arcane), read live by
    /// <c>Hands.HandVisuals</c>. The per-style SEAT pose is not here — it lives as absolute keys in
    /// <c>dev.gloomhavenvr.hands.cfg</c> ([Hands] {Style}GripPitchDegrees / {Style}LateralOffset /
    /// …) with its own measured defaults. The four <c>{Style}*Trim</c> relative entries this method
    /// used to bind alongside the scales are gone with that move; do not reintroduce them.
    ///
    /// Scale defaults are EVIDENCE-BASED: all three meshes are normalized to the
    /// same 0.19 m hand length by the prep pipeline, but the armored styles are 1.5-2x
    /// bulkier (knuckle-region width 0.198/0.14+ m and palm thickness 0.084/0.072 m vs
    /// the glove's 0.123/0.042 m; four-finger MCP span 0.101/0.113 m vs 0.074 m —
    /// measured on the prepped GLBs). 0.62 matches the Plate knuckle width and the
    /// Arcane MCP span to the glove's real-world hand bulk. Live-applied per frame
    /// via VRHand.SyncVisualOffset — no rebuild needed.
    ///
    /// <para>PlateScale RE-CHECKED, NOT RE-TUNED, AT ModBuild 243. The plate mesh was replaced
    /// wholesale by the artist's, and a scale chosen for one mesh is worthless on another
    /// unless the bulk it was measured against survived. Measured on the shipped rigs (mesh,
    /// not bones — a 12 mm slab across the middle knuckle): plate 179.5 x 84.1 mm where the
    /// AI shell was 181.8 x 86.7, i.e. 1.3 % narrower and 3.0 % thinner. Wrist-to-fingertip
    /// is 183.0 mm against 183.2 mm, and all four shipped hands agree on that length to
    /// 0.6 mm because the prep pipeline normalizes it. So the number this default was fitted
    /// to is intact and 0.62 stays. The rig's four-finger MCP span DID shrink (96.4 -> 83.1 mm)
    /// — that is the artist placing the knuckle roots differently inside the same silhouette,
    /// and it is why the mesh slab is the measurement here and the bone span is not.</para>
    /// </summary>
    /// <summary>
    /// Read ONE raw value straight out of a BepInEx .cfg file, without binding anything. Returns
    /// the text to the right of the '=' (trimmed) when the key stands under the given section, and
    /// <c>null</c> when the file is missing, unreadable, or simply does not carry that key.
    ///
    /// <para>WHY THIS EXISTS: see <see cref="_legacy3DMapRaw"/>. Short version — a RENAMED key is
    /// an orphan, and the mod cannot see orphans through the BepInEx API it has (OrphanedEntries is
    /// protected internal in un-publicized BepInEx.Core), while re-binding the old key would erase
    /// the one distinction the migration turns on: "absent" versus "present and equal to the old
    /// default". Only the file has that distinction, so the file is what is read.</para>
    ///
    /// <para>THE PARSER IS DELIBERATELY MINIMAL AND MATCHES THE WRITER, not the TOML spec. BepInEx
    /// 5's ConfigFile.Save emits exactly three shapes: blank lines, '#' comment lines, '[Section]'
    /// headers and "Key = value" lines — one entry per line, never quoted for bools, never
    /// continued. So: skip blanks and '#', track the current section from '[...]', split the first
    /// '=' and compare the trimmed key ordinally-case-insensitively (BepInEx's own ConfigDefinition
    /// comparer is ordinal, but a hand-edited file is the case this is reading and a human types
    /// what they remember). Anything it cannot make sense of yields null, which the caller treats
    /// as "no earlier choice" — the safe direction, because that leaves the shipped default alone
    /// instead of acting on a guess.</para>
    ///
    /// <para>NEVER THROWS. It runs at the very top of Awake, before the log has a level and before
    /// a single module exists; an IO fault here must degrade to "no migration", not to a plugin
    /// that fails to load. That is also why it does not use VRLog on the failure path — VRLog.Init
    /// has run, but a config-file read that failed is not worth a line at Warn on every start of an
    /// install that simply has no config yet.</para>
    /// </summary>
    private static string? ReadRawConfigValueBeforeBind(string path, string section, string key)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return null;

            string current = string.Empty;
            string[] lines = System.IO.File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (!string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return line.Substring(eq + 1).Trim();
            }
        }
        catch (Exception)
        {
            // Deliberately swallowed — see the doc above. "Could not read it" and "it was not
            // there" are the same instruction to the caller: leave the shipped default alone.
        }
        return null;
    }

    private void BindHandStyleEntries()
    {
        string[] styles = { "Glove", "Plate", "Arcane" }; // index == (int)Hands.HandStyle
        float[] scaleDefaults = { Defaults.GloveScale, Defaults.PlateScale, Defaults.ArcaneScale };
        int n = styles.Length;
        HandStyleScale = new ConfigEntry<float>[n];
        for (int i = 0; i < n; i++)
        {
            string s = styles[i];
            // THE RANGE IS THE CLAMP THE CODE ALREADY APPLIES, declared (2026-08-22 settings
            // audit, user: "Prüfe für jede Einstellung die Bedienmöglichkeit"). HandVisuals
            // .StyleScale reads this through Mathf.Clamp(…, 0.2f, 3f) — so before, the arrows
            // never stopped and every press past 3 moved the NUMBER ON SCREEN and nothing on the
            // hand. That is the "der X-Offset hat keinen Einfluss" failure this project has
            // already been reported for twice. Declaring it costs no behaviour (every value was
            // clamped at read anyway; the live cfg ships 1.12/0.62/0.62, all well inside) and it
            // is what turns the curated "Handgröße" row into a bar with arrows instead of an
            // endless stepper: HasRange is what BuildRow tests.
            HandStyleScale[i] = Config.Bind(
                "Hands", $"{s}Scale", scaleDefaults[i],
                new ConfigDescription(
                    $"Uniform visual scale of the {s} hand style (1 = authored size). The " +
                    "styled meshes share the same 0.19 m hand length but differ hugely in " +
                    "bulk — the armored styles default below 1 so their knuckle width " +
                    "matches the leather glove's real-world hand size. Applies live " +
                    "(no rebuild); grabbed objects, the card fan and the wrist HUD keep " +
                    "their own size (the rig sockets they attach to are scale-compensated). " +
                    "Range 0.2-3, the clamp HandVisuals.StyleScale applies at read.",
                    new AcceptableValueRange<float>(0.2f, 3f)));
        }
    }

    private System.Collections.IEnumerator DelayedInit(int frames)
    {
        for (int i = 0; i < frames; i++)
            yield return null; // one rendered frame each

        InitModules();
        LogStartupSummary();
    }

    // Note, not Info: at "Normal" this is the line that answers "which build is running and did
    // VR come up at all" — the first thing anyone asks of a log, and useless if it is filtered out
    // together with the running commentary.
    private void LogStartupSummary() =>
        VRLog.Note($"v{MyPluginInfo.PLUGIN_VERSION} build {BuildInfo.GitHash} [{BuildInfo.GitBranch}] " +
                   $"(built {BuildInfo.BuildTimeUtc}) loaded — " +
                   $"{_modules.Count} modules initialized, " +
                   $"VR {(VRSession.IsRunning ? $"RUNNING on '{VRSession.RuntimeName}'" : "not running")}. " +
                   $"IF THIS IS NOT THE COMMIT YOU EXPECTED, install.ps1 deployed a stale DLL — rebuild.");

    /// <summary>
    /// ScriptEngine (F6 hot reload) calls OnDestroy on the old instance before loading
    /// the new one — undo everything we did so reloads stay clean (TOOLCHAIN §3.3):
    /// modules shut down in reverse order (Core stops XR last),
    /// then all Harmony patches are removed.
    /// </summary>
    private void OnDestroy()
    {
        StopAllCoroutines(); // pending DelayedInit / XR watchdog die with this instance
        VRSession.CoroutineHost = null;

        for (int i = _modules.Count - 1; i >= 0; i--)
        {
            try
            {
                _modules[i].Shutdown();
            }
            catch (Exception e)
            {
                VRLog.Error(_modules[i].Name, $"Shutdown failed: {e}");
            }
        }
        _modules.Clear();

        _harmony?.UnpatchSelf();
        _harmony = null;
        VRSession.Harmony = null;
    }

    // This order is the module init order and (reversed) the shutdown order — INVARIANTS §12.
    // DevModule sitting AFTER CompatModule is correct and deliberate, not a slip: see the
    // _modules field doc. Do not "fix" it by moving CompatModule down; changing module init
    // order for a cosmetic match with a comment is a Tier-3 behaviour change.
    private void RegisterModules()
    {
        _modules.Add(new Core.CoreModule());
        _modules.Add(new Core.Events.VREventsModule());
        _modules.Add(new Rig.RigModule());
        _modules.Add(new Hands.HandsModule());
        _modules.Add(new Cards.CardsModule());
        _modules.Add(new Board.BoardModule());
        _modules.Add(new WorldUI.WorldUIModule());
        _modules.Add(new Net.NetModule());
        // AFTER NetModule: spatial voice reads the remote avatars NetModule owns
        // (NetAvatarDriver.TryGetPeerHeadHolder). It degrades to plain 2D voice when there are
        // none, so the order is a quality constraint rather than a correctness one.
        _modules.Add(new Voice.VoiceModule());
        _modules.Add(new Compat.CompatModule());
        _modules.Add(new Core.DevModule());
        // LAST on purpose. It asks GitHub once, when the main menu is up and VR is running, whether
        // a newer release exists — nothing else in the mod depends on it, nothing it does is on a
        // frame path, and initialising it after everything else means a throw in its Init (which is
        // caught there anyway) cannot come between two modules that DO depend on each other.
        _modules.Add(new Core.SelfUpdateModule());
    }

    private void InitModules()
    {
        foreach (IVRModule module in _modules)
        {
            try
            {
                module.Init();
            }
            catch (Exception e)
            {
                VRLog.Error(module.Name, $"Init failed: {e}");
            }
        }
    }
}
