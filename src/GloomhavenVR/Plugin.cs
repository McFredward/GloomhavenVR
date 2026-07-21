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

    /// <summary>Escape hatch: delay mod init (and thus VR init) by N rendered frames.</summary>
    internal static ConfigEntry<int> InitDelayFrames = null!;

    /// <summary>
    /// Diorama scale: game world units per real-world meter (the VR rig is scaled up
    /// by this factor so the board reads as a table).
    /// 0 = auto: derived from the hex tile size (UnityGameEditorRuntime.s_TileSize).
    /// </summary>
    internal static ConfigEntry<float> WorldScale = null!;

    /// <summary>Head-track the menu camera outside scenarios (menu rig, P5). Off = static menu view.</summary>
    internal static ConfigEntry<bool> MenuRig = null!;

    /// <summary>Seat multiple VR players evenly around the board (distinct azimuth per player) instead of stacking them at one shared seat. No effect single-player.</summary>
    internal static ConfigEntry<bool> SpawnInCircle = null!;

    /// <summary>[Rig] Experimental3DMap — RESERVED placeholder, currently unimplemented.</summary>
    internal static ConfigEntry<bool> Experimental3DMap = null!;

    /// <summary>
    /// [Rig] Demeo-style world tilt (degrees, 0-60): the whole diorama APPEARS tilted toward
    /// the player by counter-rotating the tracking space around the board center
    /// (VRRigDriver.TickWorldTilt). World coordinates never change — multiplayer-safe.
    /// </summary>
    internal static ConfigEntry<float> WorldTiltDegrees = null!;

    /// <summary>Disable PPv2 (PostProcessLayer/PostProcessVolume) while VR runs (P1 default: on).</summary>
    internal static ConfigEntry<bool> DisablePostProcessing = null!;

    /// <summary>Disable VolumetricFogAndMist.VolumetricFog while VR runs.</summary>
    internal static ConfigEntry<bool> DisableVolumetricFog = null!;

    /// <summary>Extra comma-separated component type full names to disable while VR runs.</summary>
    internal static ConfigEntry<string> DisableComponents = null!;

    /// <summary>
    /// [Compat] Re-enable the game's own view-dependent wall see-through fade in VR
    /// (OFF = walls always solid, the VR default). Consulted live by WallFadeDisable.
    /// </summary>
    internal static ConfigEntry<bool> WallFade = null!;

    /// <summary>Dominant hand ("Right"/"Left") — its ray is the default pick source.</summary>
    internal static ConfigEntry<string> PrimaryHand = null!;

    /// <summary>Visible hand model style (Glove/Plate/Arcane). Live-rebuilds on change
    /// (HandsDriver) and is synced to other VR players (Net.AvatarState.HandStyle).</summary>
    internal static ConfigEntry<Hands.HandStyle> HandStyle = null!;

    /// <summary>Pitch between the OpenXR grip pose and the visual hand model (degrees; negative = fingers down). One of the four [Hands] seat controls.</summary>
    internal static ConfigEntry<float> GripPitchOffsetDegrees = null!;

    /// <summary>Lateral offset (meters, device-space X) of the visual hand from the grip pose (positive = toward the thumb side). One of the four [Hands] seat controls.</summary>
    internal static ConfigEntry<float> HandLateralOffset = null!;

    /// <summary>Vertical offset (meters, device-space Y) of the visual hand from the grip pose so its palm sits on the controller (positive = up). One of the four [Hands] seat controls.</summary>
    internal static ConfigEntry<float> HandVerticalOffset = null!;

    /// <summary>Forward offset (meters, device-space Z) of the visual hand from the grip pose (positive = toward the fingertips). One of the four [Hands] seat controls.</summary>
    internal static ConfigEntry<float> HandForwardOffset = null!;

    /// <summary>Visible laser starts at the index fingertip instead of the aim pose (test #6).</summary>
    internal static ConfigEntry<bool> LaserFingerOrigin = null!;

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

    /// <summary>Clear color of the owned head camera (the void around menus). Default black.</summary>
    internal static ConfigEntry<UnityEngine.Color> VoidColor = null!;
    internal static ConfigEntry<bool> ForwardRendering = null!;

    /// <summary>Force the ray interactor on in every VR mode (accessibility/preference).</summary>
    internal static ConfigEntry<bool> RayAlwaysOn = null!;

    /// <summary>ModalUI: ray visuals only show within this cone of a UI surface (0 = always show). P5.</summary>
    internal static ConfigEntry<float> ModalRayConeDegrees = null!;

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
    /// Feature module registry. Order matters: Core first (XR bootstrap),
    /// Compat last (fixups on top of everything else).
    /// </summary>
    private readonly List<IVRModule> _modules = [];

    private void Awake()
    {
        VRLog.Init(Logger);

        Enabled = Config.Bind(
            "General", "Enabled", true,
            "Master switch. Set to false to run the game completely vanilla (the mod does nothing).");
        RuntimeOverride = Config.Bind(
            "General", "RuntimeOverride", "",
            "Optional path to an OpenXR runtime JSON file (e.g. SteamVR's steamxr_win64.json). " +
            "Sets XR_RUNTIME_JSON before XR init and is tried first. Leave empty to auto-detect " +
            "(active runtime from the registry, then all available runtimes, then well-known paths).");
        RuntimePriority = Config.Bind(
            "Core", "RuntimePriority", "auto",
            "Order in which OpenXR runtimes are attempted. 'auto' = system default runtime first " +
            "(what the OS/registry points at), then VDXR when the Virtual Desktop Streamer is " +
            "running, then remaining installed runtimes with SteamVR last (attempting SteamVR " +
            "boots its compositor). Or a comma-separated list of: default, vdxr, steamvr, oculus, " +
            "or full paths to runtime JSON files — tried in exactly that order.");
        SkipRuntimeCandidates = Config.Bind(
            "Core", "SkipRuntimeCandidates", false,
            "Escape hatch: make a single init attempt on the system default OpenXR runtime and " +
            "never set XR_RUNTIME_JSON (no candidate failover). Use when the failover itself " +
            "causes trouble (e.g. it keeps booting runtimes you don't use).");
        InitDelayFrames = Config.Bind(
            "Core", "InitDelayFrames", 0,
            "Escape hatch: delay mod initialization (including OpenXR init) by this many rendered " +
            "frames. Some runtime/GPU combos need the graphics device fully up before " +
            "xrCreateSession works. 0 (default) = initialize immediately in plugin Awake.");
        WorldScale = Config.Bind(
            "Rig", "WorldScale", 0f,
            "Diorama scale: game world units per real-world meter (the rig is scaled by this, " +
            "making the board read as a table). 0 = auto from the hex tile size (~10-20 typical).");
        MenuRig = Config.Bind(
            "Rig", "MenuRig", true,
            "Head-track the game's menu camera while no scenario runs (main menu, guildmaster " +
            "map) so the floating 2D screen and the hands work outside scenarios. Off = the " +
            "menu renders from a static viewpoint.");
        SpawnInCircle = Config.Bind(
            "Rig", "SpawnInCircle", true,
            "Multiplayer: seat each VR player at a distinct azimuth evenly spaced around the " +
            "board (360 / player-count apart), each facing the board center, so avatars no longer " +
            "spawn stacked inside each other. No effect in single-player / offline (the solo seat " +
            "is unchanged). Off = every player keeps the same shared seat as before.");
        Experimental3DMap = Config.Bind(
            "Rig", "Experimental3DMap", false,
            "RESERVED — CURRENTLY UNIMPLEMENTED placeholder for a future feature: explore the " +
            "campaign/world map as a head-tracked 3D diorama instead of the flat 2D screen. " +
            "Today this switch has NO effect: everything before an actual combat scenario " +
            "(campaign map, guildmaster, merchant, level-up) deliberately stays in Menu2D on " +
            "the floating screen, because the map scene was never authored for a free VR " +
            "camera (test #8: giant map below the player, black flat window). The wish is " +
            "saved here so it survives into a later phase.");
        WorldTiltDegrees = Config.Bind(
            "Rig", "WorldTiltDegrees", 0f,
            "Demeo-style world tilt in degrees (0-60, 0 = off/default). The ENTIRE play area " +
            "(board, figures, everything) appears tilted toward you — great when playing " +
            "reclined or lying down. Implemented rig-side: the VR tracking space is " +
            "counter-rotated around the board center, so your viewpoint orbits up and over " +
            "the board while world coordinates stay untouched (multiplayer-safe: boards and " +
            "figures never move for anyone; other players merely see your avatar orbit, " +
            "which is the physically honest picture). COMFORT WARNING: tilting reorients " +
            "gravity relative to your head — the horizon no longer matches your inner ear. " +
            "Increase in small steps (the settings panel steps 5 degrees) and prefer " +
            "moderate angles. Live: changes apply immediately and persist.");
        DisablePostProcessing = Config.Bind(
            "Compat", "DisablePostProcessing", true,
            "Disable PostProcessing v2 (PostProcessLayer/PostProcessVolume) while VR is active. " +
            "Phase 1 default: true (PPv2 is unverified under stereo rendering).");
        DisableVolumetricFog = Config.Bind(
            "Compat", "DisableVolumetricFog", true,
            "Disable the VolumetricFogAndMist.VolumetricFog image effect while VR is active.");
        DisableComponents = Config.Bind(
            "Compat", "DisableComponents", "",
            "Extra comma-separated component type full names (optionally 'FullName, Assembly') " +
            "to disable while VR is active, e.g. 'BeautifyEffect.Beautify'.");
        WallFade = Config.Bind(
            "Compat", "WallFade", false,
            "Re-enable the game's own see-through wall fade in VR. When ON, a wall fades " +
            "exactly while the angle of your view onto it would hide the play area behind it " +
            "(the game's own view-dependent shader logic, evaluated against your headset eye " +
            "position because the mod's head camera is the one rendering the scene) and " +
            "turns solid again as soon as it no longer occludes. OFF (default) pins the fade " +
            "off so walls always render solid — the VR behavior so far. Purely visual and " +
            "local (a shader global): multiplayer peers are unaffected. Live-togglable from " +
            "the VR settings panel.");
        PrimaryHand = Config.Bind(
            "Hands", "PrimaryHand", "Right",
            "Dominant hand (Right/Left). Its index-finger ray is the default pick source " +
            "for board targeting.");
        HandStyle = Config.Bind(
            "Hands", "HandStyle", Hands.HandStyle.Glove,
            "Which hand model to wear: Glove (leather glove, default), Plate (plate-armor " +
            "gauntlet) or Arcane (arcane-runes mage glove). Applies live (the hands rebuild " +
            "on change) and is synchronized in multiplayer so other VR players see your " +
            "chosen hands on your avatar. Falls back to Glove when the styled prefab is " +
            "missing from an older asset bundle, and to the procedural hand without any " +
            "bundle.");
        GripPitchOffsetDegrees = Config.Bind(
            "Hands", "GripPitchOffsetDegrees", -30f,
            "Pitch offset (degrees) between the tracked OpenXR grip pose and the visual hand " +
            "model, around the controller's X axis. NEGATIVE tilts the fingertips DOWN from " +
            "the grip-pose forward. The OpenXR grip pose points up along the controller " +
            "handle, not where a relaxed hand points. Together with HandLateralOffset / " +
            "HandVerticalOffset / HandForwardOffset this seats the visual hand ON the " +
            "physical controller; position 0/0/0 with pitch 0 places the hand EXACTLY at " +
            "the tracked grip pose. Default -30 keeps the palm wrapped on a HELD controller " +
            "(a stronger down-pitch reads as a relaxed hand and floats the palm off the " +
            "device — hardware test #27). Hot-reloadable: edit while the game runs and the " +
            "hands re-pose on the next frame. Tuning guide: docs/TESTING-P2.md.");
        HandLateralOffset = Config.Bind(
            "Hands", "HandLateralOffset", 0f,
            "Lateral offset (meters) of the visual hand model from the tracked OpenXR grip " +
            "pose, along the controller's local X axis. POSITIVE shifts the hand toward the " +
            "thumb side (device-space; the sign is mirrored per hand by the rig geometry). " +
            "One of the four [Hands] seat controls (HandLateralOffset / HandVerticalOffset / " +
            "HandForwardOffset / GripPitchOffsetDegrees) — together they place the visual " +
            "hand ON the physical controller, and position 0/0/0 with pitch 0 puts the hand " +
            "EXACTLY at the tracked grip pose. Default 0 keeps the hand centred on the " +
            "controller handle. Hot-reloadable: edit while the game runs and the hands " +
            "re-seat on the next frame.");
        HandVerticalOffset = Config.Bind(
            "Hands", "HandVerticalOffset", 0f,
            "Vertical offset (meters) of the visual hand model from the tracked OpenXR grip " +
            "pose, along the controller's local up (Y) axis. POSITIVE raises the hand. One " +
            "of the four [Hands] seat controls (HandLateralOffset / HandVerticalOffset / " +
            "HandForwardOffset / GripPitchOffsetDegrees) — together they place the visual " +
            "hand ON the physical controller, and position 0/0/0 with pitch 0 puts the hand " +
            "EXACTLY at the tracked grip pose. Default 0 seats the palm at the grip pose: " +
            "the modest -30 pitch no longer drops the palm the way the old -60 did, so no " +
            "vertical lift is needed to start — raise it if the palm still reads low on your " +
            "controller (hardware tests #24/#27). Hot-reloadable: edit while the game runs " +
            "and the hands re-seat on the next frame.");
        HandForwardOffset = Config.Bind(
            "Hands", "HandForwardOffset", -0.06f,
            "Forward/depth offset (meters) of the visual hand model from the tracked OpenXR " +
            "grip pose, along the controller's local forward (Z) axis. POSITIVE pushes the " +
            "hand toward the fingertips; NEGATIVE sits the wrist behind the grip origin. One " +
            "of the four [Hands] seat controls (HandLateralOffset / HandVerticalOffset / " +
            "HandForwardOffset / GripPitchOffsetDegrees) — together they place the visual " +
            "hand ON the physical controller, and position 0/0/0 with pitch 0 puts the hand " +
            "EXACTLY at the tracked grip pose. Default -0.06 sits the wrist just behind the " +
            "grip origin so the palm wraps the controller handle. Hot-reloadable: edit while " +
            "the game runs and the hands re-seat on the next frame.");
        LaserFingerOrigin = Config.Bind(
            "Hands", "LaserFingerOrigin", true,
            "Start the VISIBLE laser beam at the hand rig's index fingertip (converging on " +
            "the aim-pose ray's end point) so it reads as leaving the pointing finger. The " +
            "pick ray itself always uses the OpenXR aim pose. Off = beam starts at the aim " +
            "pose origin (controller).");
        LaserFingerOffsetMeters = Config.Bind(
            "Hands", "LaserFingerOffsetMeters", 0.02f,
            "Fine-tune for LaserFingerOrigin: how far (meters, along the beam) in front of " +
            "the index fingertip the visible beam starts.");
        HandColor = Config.Bind(
            "Hands", "HandColor", "D9C9B5",
            "Base color of the procedural hands as RRGGBB hex (no '#'). Must stay light " +
            "enough to read against the black void — the hand shader is unlit (the void " +
            "has no lights; lit shaders render black there). The left hand gets a slight " +
            "cool tint automatically so the sides stay distinguishable.");
        VoidColor = Config.Bind(
            "Rig", "VoidColor", UnityEngine.Color.black,
            "Clear color of the mod's head camera — the void around the floating menu screen " +
            "and outside the diorama. Default pure black. For DEBUGGING set a dark grey " +
            "(e.g. 1F2126FF): grey distinguishes 'camera renders but content missing' from " +
            "'camera dead / not rendering' (pitch black), which is invaluable in HMD reports.");
        ForwardRendering = Config.Bind(
            "Rig", "ForwardRendering", true,
            "Render the mod's head camera in FORWARD instead of the game's DeferredShading. " +
            "This is the fix for transparent effects (fire/torch glow, hex selection ring, health " +
            "bars) rendering THROUGH walls in VR: the sky depth-reset renderer (queue 1999, ZTest " +
            "Always) has no deferred pass, so on a deferred camera it runs in the forward-opaque " +
            "fallback AFTER the walls and wipes their depth, leaving nothing for transparents to " +
            "test against. Forward rendering restores strict queue order (reset 1999 runs BEFORE " +
            "walls 2000, walls overwrite it), so the depth buffer keeps the walls and transparents " +
            "occlude correctly. Disable ONLY if forward lighting looks wrong (deferred handles many " +
            "dynamic lights per pixel; forward has a per-object light limit).");
        RayAlwaysOn = Config.Bind(
            "Hands", "RayAlwaysOn", false,
            "Keep the laser/ray interactor enabled in every VR mode instead of only in " +
            "far-interaction contexts.");
        ModalRayConeDegrees = Config.Bind(
            "Hands", "ModalRayConeDegrees", 25f,
            "While a modal dialog is up (ModalUI mode) the ray stays usable but its laser " +
            "only shows when pointing within this many degrees of a UI surface (world dialog, " +
            "flat screen). 0 = always show the laser in ModalUI.");
        DevMode = Config.Bind(
            "Dev", "Enabled", false,
            "Developer mode: wires the VR event bus and hand simulation even without an HMD " +
            "and installs the dev console (F8 sim hands, F9 poke Ready, F10 overlay).");
        DevOverlay = Config.Bind(
            "Dev", "Overlay", true,
            "Show the dev overlay on startup when dev mode is enabled (F10 toggles at runtime).");
        SimulateHands = Config.Bind(
            "Dev", "SimulateHands", false,
            "Animate fake hand transforms on the desktop (no HMD needed). Hold T = trigger, " +
            "G = grip. Toggle at runtime with F8. Ignored while real VR is running.");
        InputDeviceDumpInterval = Config.Bind(
            "Dev", "InputDeviceDumpInterval", 0f,
            "Log all UnityEngine.XR.InputDevices every N seconds (0 = off).");

        if (!Enabled.Value)
        {
            VRLog.Info("Disabled via config ([General] Enabled = false) — game runs vanilla.");
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

    private System.Collections.IEnumerator DelayedInit(int frames)
    {
        for (int i = 0; i < frames; i++)
            yield return null; // one rendered frame each

        InitModules();
        LogStartupSummary();
    }

    private void LogStartupSummary() =>
        VRLog.Info($"v{MyPluginInfo.PLUGIN_VERSION} build {BuildInfo.GitHash} [{BuildInfo.GitBranch}] " +
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
        _modules.Add(new Compat.CompatModule());
        _modules.Add(new Core.DevModule());
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
