using BepInEx.Configuration;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// The player's environment choice. Stored as the mod's own enum (BepInEx serialises the member
/// name into the cfg, and the catalog's Choice classification gives it a dropdown for free); the
/// member order IS the dropdown index map (Default=0/Cellar=1/SwampNight=2), exactly like
/// <c>Cards.BoardMoveMode</c>.
///
/// CONFIG MIGRATION from the panorama era (ModBuild 124 shipped Default/Night/Sunset/Cellar):
/// BepInEx 5's <c>ConfigEntryBase.SetSerializedValue</c> wraps the enum parse
/// (<c>TomlTypeConverter</c> → <c>Enum.Parse(type, value, ignoreCase: true)</c>) in a try/catch —
/// a persisted name that no longer exists ("Night", "Sunset") throws inside the parse, BepInEx
/// logs <c>Config value of setting "..." could not be parsed and will be ignored</c> (string
/// verified in the referenced BepInEx.dll 5.4.20) and the entry KEEPS ITS BOUND DEFAULT, i.e.
/// <see cref="SkyStyle.Default"/>. A persisted "Cellar" still parses and lands on the new cellar
/// ENVIRONMENT — the intended upgrade (the player asked for a cellar, they get the real room).
/// One hole remains: <c>Enum.Parse</c> accepts NUMERIC strings ("3" from a hand-edited cfg
/// parses into the undefined value <c>(SkyStyle)3</c>), so <see cref="SkyAlternative.Tick"/>
/// treats every value that is not a defined non-Default member as Default instead of trusting
/// the range.
/// </summary>
internal enum SkyStyle
{
    /// <summary>The game's own scenario sky (GH_SkySphere), exactly today's behaviour.</summary>
    Default = 0,

    /// <summary>A candle-lit stone cellar: the game's own AUTHORED multi-room map 'Map ABHM'
    /// dressed in its dungeon art (Dungeon/StoneRooms/Candlelight fills, authored axes kept)
    /// plus the Env_Cellar FX shell (dust motes).</summary>
    Cellar = 1,

    /// <summary>A moonlit marsh: the game's own AUTHORED multi-room map 'Map DDM' dressed in
    /// its forest art (Forest/Marsh/StillWaters fills, Tone forced to ForestMoonlight for the
    /// star dome) plus the Env_Swamp FX shell (star dome, shooting stars, fireflies, ground
    /// fog).</summary>
    SwampNight = 2,
}

/// <summary>
/// SKY ALTERNATIVES — replace the game's scenario sky with a real 3D ENVIRONMENT around the
/// play space.
///
/// THE RULING (user, 2026-08-12, verbatim — it killed the ModBuild-124 panorama skyboxes):
/// "statt so eine Skybox will ich am Besten einen wirklichen 'Keller' mit samt 3D assets ...
/// Stell ich mir wie kleine VRChats worlds. Nicht interaktiv rein als Umgebung. Entferne die
/// Umegbungen die du mir gemacht hast und bau mir stattdessen ein visuell ansprechenden 'Nerd
/// DnD-Keller' oder ähnliches und einen Sternenhimmel samt Sternschnuppen und animationen in
/// einer Sumpfumgebung."
///
/// THE SECOND RULING (user, 2026-08-12, verbatim — it killed the menu scope AND the third-party
/// bundle art): "Ich WILL garnicht das die Umgebung im Menu rendert - sondern nur im Szenario so
/// wie es die Default originale Umgebung auch macht. Leg daher mit der Umsetzung los von den
/// zwei neuen Umgebungen. Lösche die alten assets und räum da wieder auf."
///
/// WHAT IT DOES
/// ------------
/// A dial (<c>[Sky] Style</c>: Default / Cellar / SwampNight, curated in Grafik ▸ Darstellung)
/// picks the surroundings. Default is the game's own sky, bit-identical to today —
/// <see cref="SkyBackdrop"/> keeps making it a non-occluding backdrop and this class does
/// nothing. SCENARIO-ONLY SCOPE (second ruling): everything below happens exclusively while an
/// actual scenario board exists (<see cref="Events.VRModeStateMachine.ScenarioBoardExists"/> —
/// the Choreographer-alive signal the rig/WorldUI/mode machine already read; deliberately not
/// the save-state phase, which flips during loading/travel before any board exists). In the
/// menu and on the world map: nothing, ever — the game's default look. A non-Default choice in
/// a scenario:
///
///  1. HIDES the game's sky sphere (<c>GH_SkySphere</c>) via <c>renderer.enabled = false</c> —
///     unchanged from the panorama build. That is safe for PURE hiding — <see cref="SkyBackdrop"/>'s
///     class doc documents the trap precisely: suppressing a renderer only bites when a
///     CommandBuffer is supposed to REDRAW it, which nothing does here. The sphere is found
///     through <see cref="SkyBackdrop.FindSky"/> (one shared set of name/shader hints),
///     recorded, and re-enabled on restore. Hidden for BOTH styles: SwampNight's star dome
///     must own the sky, and the cellar is an enclosed room.
///  2. SPAWNS the FX SHELL prefab from the asset bundle (a parallel content lane authors them
///     against a fixed contract): <c>Env_Swamp</c> = star dome + shooting stars + fireflies +
///     ground fog ONLY; <c>Env_Cellar</c> = dust motes + a disabled 'GlowTemplate' child.
///     Self-lit, self-animating (Shuriken only, no scripts), authored in real meters.
///  3. GENERATES the actual rooms from the game's own art: one of the game's AUTHORED
///     multi-room maps (Cellar = 'Map ABHM', SwampNight = 'Map DDM'; ModBuild-128 finding 2 —
///     the user rejected the generic single-room 'Map A' template and asked for "zwei echte
///     interesannte fertige Räume aus dem Spiel") is instantiated offscreen and dressed by
///     the live Apparance engine — authored per-tile styles kept, unset axes filled with the
///     style vocabulary — then frozen, normalized so the MAIN room reads ~10 real meters
///     across (whole map capped at 24 m) and placed into the same frame with the main room
///     centered on the play field — the whole lane lives in <c>SkyAlternative.MapGen.cs</c>
///     (staging pose, hidden-layer/depth staging, borrowed detail focus, settle polling,
///     red-cube heal, normalization math, lifecycle). Generation failure degrades to the FX
///     shell alone.
///
/// The game's own scenario diorama/table stays untouched and visible — the environment
/// surrounds it. Everything lives on the MOD LAYER (only the rig head camera renders it; game
/// cameras and the FlatScreen composites never see it) and has every collider stripped —
/// non-interactive by ruling, it must never catch a laser/poke ray.
///
/// ANCHORING — THE FRAME, second revision. The first environment build (ModBuild 125)
/// parented the instance under <see cref="VRRigDriver.RigRoot"/> at identity, chosen so the
/// room stood still in REAL space while the player leaned or walked physically. That anchor
/// had a blind spot the hardware test found immediately: every mod LOCOMOTION also moves
/// RigRoot — stick flight adds to <c>rig.position</c> (Rig/Flight.cs), snap/smooth turn
/// rotates the rig about the head (Rig/SnapTurn.cs), the world grab drags/yaws/scales the rig
/// (Rig/WorldGrab.cs) — so the room rode along with the player and could never be moved
/// through. THE RULING THAT KILLED THE RIG ANCHOR (user report, 2026-08-12, verbatim):
/// "Weiterhin möchte ich mich auch in den umgebungen frei bewegen und drehen können, das ist
/// aktuell nicht möglich."
///
/// TWO PURE FRAMES WERE EVALUATED, and each fails one hard requirement:
///
///  - RIG FRAME (the old anchor): physical walking works, zoom can never rescale the room —
///    but locomotion moves rig and room together, so free movement is impossible BY
///    CONSTRUCTION. That is exactly the report.
///  - PURE WORLD FRAME (parent it like scenario geometry, world-fixed size — the "behaves
///    exactly like the diorama" option): all locomotion moves the player through it, which is
///    what "wie kleine VRChats worlds" implies — but the prefabs are authored in REAL METERS
///    around the table while the world is a DIORAMA at ~12x base scale: an 8 m cellar would
///    read as a 0.7 m dollhouse, and pre-scaling it once only defers the problem — every
///    pinch-zoom would then balloon/shrink the room with the board, the exact perceptual
///    growth the design forbids.
///
/// THE CHOSEN FRAME IS THE HYBRID: world-anchored POSITION and YAW, rig-tracked SCALE.
///
///  - POSITION/ROTATION live in WORLD space (the instance is unparented + DontDestroyOnLoad).
///    Flight, turning, world-grab drag and physical walking all move the player relative to
///    the world, so they all move the player THROUGH the room — requirement (b) and (c).
///    The world tilt ([Rig] WorldTiltDegrees) pitches the tracking space against the world,
///    so under an active tilt the room reads as tilted exactly like the board does — it is
///    world geometry now; that is the consistent reading, and the tilt ships at 0.
///  - SCALE tracks the rig scale (spawned at rig scale; kept there by
///    <see cref="NotifyRigScaled"/>). At rig scale S the room's world size is
///    (authored meters × S), i.e. its REAL, perceived size is the authored meters — always.
///  - ZOOM (the subtle part, requirement (d)): "env scale tracks rig scale, position stays
///    world-fixed" is NOT enough — the rig scales about a PIVOT (WorldGrab keeps the world
///    point under the hands glued, <c>Comfort.SetScaleMultiplier</c> keeps the head still), so
///    a world-fixed room would keep its real size but DRIFT: the player's perceived distance
///    to it changes with every pinch. Both scale writers therefore report their write through
///    <see cref="NotifyRigScaled"/> (pivot, before, after), and the environment mirrors it:
///    <c>envPos = pivot + (envPos − pivot) · (after/before)</c>, <c>envScale = after</c>.
///    Proof this is exact: with the rig mapping world = P + R·(s·t), the perceived
///    (tracking-space) pose of the env is (1/s)·R⁻¹·(envPos − P). The scale writer keeps the
///    pivot glued to a tracking point m: P = pivot − R·(s·m). Substituting both updates,
///    the perceived env pose after the write equals the pose a pure locomotion write (same
///    P, R change, scale untouched) would produce — the scale component is bit-cancelled,
///    the room neither grows nor drifts, while drag/turn components of the same gesture
///    still pass through as movement. Zoom rescales the BOARD around the player; the ROOM
///    stands still around them, like the real room MR shows.
///
/// SPAWN POSE (requirement (a)) — placed ONCE, floor-aligned, PLAY-FIELD-CENTERED. THE
/// FINDING-4 RULING (user, ModBuild-127 report, verbatim): "In der neuen Umgebung die du
/// baust soll das SPiefeld absolut mittig angeordnet sein." The frame origin is therefore
/// the scenario play field's horizontal CENTER — the world-space bounds center of the
/// scenario's own map tiles (<see cref="TryGetPlayFieldCenter"/>: the root
/// <c>ProceduralScenario</c>'s <c>MapTiles</c>, each tile's authored <c>BoxCollider</c>
/// bounds encapsulated; ALL tiles, hidden included, so a mid-scenario reveal never re-centers
/// anything) — and the generated map is normalized so its MAIN ROOM's center lands exactly
/// there (MapGen placement math; the whole-map bounds center of a multi-room composite can
/// fall inside a wall between rooms): the diorama sits mid-main-room by construction. Floor and yaw are
/// UNCHANGED from before: origin height = the player's floor point (the rig-space point
/// under the head, y=0 — tracking is floor-origin, so that is the real floor, times the rig
/// mapping), yaw = the head's world forward projected to the horizon. If the board's tiles
/// do not exist yet at spawn (scenario still assembling), the player's floor point stands in
/// HORIZONTALLY too, and the steady-state tick re-centers on the board the moment it appears
/// (throttled probe; the FX shell's ambient particles make that one-time move imperceptible,
/// and after a re-seat event the board anchor is re-derived anyway). Head not tracked yet
/// (rig just built) → the rig's own pose stands in, and the first-pose recenter re-places it
/// a frame later via the pose-version rule below. NOTE the deliberate trade: the zoom
/// scale-follow (<see cref="NotifyRigScaled"/>) keeps the ROOM bit-frozen in the player's
/// real frame, so a pinch-zoom shifts the room's world position off the board center — the
/// no-drift machinery outranks perfect centering mid-gesture (task ruling), and every
/// re-seat/re-spawn restores exact centering.
///
/// RE-SEAT RULE: the placement is refreshed whenever <see cref="VRRigDriver.RigPoseVersion"/>
/// changes — rig (re)build, deliberate recenter (B+Y chord), spawn-ring seat, menu recenter.
/// Those are exactly the "the player was teleported" events (snap turns and world grabs do
/// NOT bump it, by that counter's own contract), so the room re-seats around the player's new
/// seat like the old rig anchor did, while free movement never re-seats anything. This also
/// IS the "recenter environment" affordance (requirement (e)): the recenter chord brings the
/// room back around you; re-selecting a style (switch away and back, or to the other style)
/// respawns it at the current pose — no new UI.
///
/// RIG REBUILD / MID-REBUILD FRAMES: on a frame with no RigRoot the whole feature stands down
/// (environment despawned, sphere restored) exactly as before — rebuilds are rare, logged
/// events, and the next tick under the new root respawns at the new player pose. The instance
/// is DontDestroyOnLoad so a scene unload can never fake-null it out from under a live rig.
///
/// PER-FRAME COST: ZERO transform writes while active and idle — the room is world-static.
/// The active steady-state tick is: enum read, sphere-hidden check, instance/anchor null
/// checks, ONE static int compare (RigPoseVersion) and one scale compare (a defensive
/// drift-heal that only ever fires if a future rig-scale writer forgets to call
/// <see cref="NotifyRigScaled"/>). Transform writes happen only inside a pinch-zoom (one
/// position+scale write per scaled frame, mirroring the rig write) and on the rare re-seat
/// events. Default/MR-on: an enum read plus an idempotent early-out.
///
/// PARTICLES: prefab systems auto-play (playOnAwake) and the instance is always active, so no
/// kick is strictly needed — a defensive <c>Play()</c> runs anyway after instantiate. Two
/// module normalisations make Shuriken honour the frame (the content lane authors at scale 1
/// in an editor scene and cannot know the instance runs at diorama scale):
/// <c>scalingMode = Hierarchy</c> (the established pattern — see
/// <c>Net.RemoteControlBoard</c>'s pile FX) so sizes/speeds/shapes follow the root scale and
/// stay authored-real-size, and World simulation space is switched to Local so in-flight
/// particles ride the root when the zoom scale-follow moves it instead of smearing behind
/// the room (the root now carries a spawn yaw, so a World-authored velocity direction is
/// rotated by that constant yaw — harmless for ambient FX, and constant after placement).
///
/// FAR PLANE: the environment is real-size, so at rig scale S its farthest geometry sits up
/// to (authored meters × S) world units from its ORIGIN — and the player can now fly away
/// from that origin. <see cref="MinFarWorldUnits"/> hands VRRigDriver.TickClipPlanes a floor
/// of (head-to-origin distance + <see cref="EnvMinFarMeters"/> × S) world units while active
/// (0 when idle), still capped by the depth-precision far/near ratio.
///
/// MR PRECEDENCE (the user's rule: MR ON ⇒ the sky is ALWAYS off): <see cref="MixedReality.Tick"/>
/// calls <see cref="StandDown"/> FIRST on its MR-on path — the environment despawns and the
/// game sphere is re-enabled so MR's own <c>HideSkyGeometry</c> sweep records and disables a
/// clean renderer for the chroma key, whatever the dial says. When MR turns off the dial's
/// choice re-applies on the next tick. On the MR-off path this ticks BEFORE SkyBackdrop, and
/// while an environment is shown SkyBackdrop stands down through the same parameter MR uses
/// (a hidden sphere needs no non-occluding treatment).
///
/// ASSETS: LAZY by design — nothing (bundle prefab or game map) loads until a style is first
/// selected in a scenario. FX shells come from the mod bundle via the established probe pattern
/// (<c>AssetBundle.GetAllLoadedAssetBundles()</c> + <c>LoadAsset</c>); a loaded prefab
/// reference is KEPT for the session (it is a reference into the loaded bundle, not a copy).
/// Missing shell prefab (older bundle) = one-shot warn, the game's own sky stays fully in
/// place. The game's map prefab loads through a session-cached Addressables handle
/// (SkyAlternative.MapGen.cs).
///
/// MULTIPLAYER: local presentation only — nothing about the environment is on the wire. [Sky]
/// is not a board section, so the wire-coverage checker does not demand an exemption.
/// </summary>
internal static partial class SkyAlternative
{
    /// <summary>The environment choice. Bound by <see cref="BindConfig"/> into the RIG module
    /// file (<c>dev.gloomhavenvr.rig.cfg</c>, section [Sky]) so the config catalog's force-bind
    /// of <see cref="Rig.RenderQuality"/> surfaces it, and module "rig" files it under the
    /// Visual topic — beside the other look-of-the-picture dials.</summary>
    internal static ConfigEntry<SkyStyle> Style = null!;

    private static bool _bound;

    /// <summary>Frames between sphere re-scans while a non-Default style is active (the sphere
    /// can generate late, and a scene change fake-nulls the acquired renderer). Same cadence as
    /// <see cref="SkyBackdrop"/> / MR's sky sweep.</summary>
    private const int ScanIntervalFrames = 60;

    /// <summary>Far-plane floor in REAL meters while an environment is active — must cover the
    /// farthest authored geometry (the swamp star dome). See the class doc's FAR PLANE note.</summary>
    private const float EnvMinFarMeters = 100f;

    /// <summary>Bundle paths of the FX SHELL prefabs, indexed by <see cref="SkyStyle"/>
    /// (0 = Default = none). Contract fixed with the content lane rebuilding the bundle:
    /// Env_Swamp = star dome + shooting stars + fireflies + ground fog ONLY; Env_Cellar =
    /// dust motes + a disabled 'GlowTemplate' child (kept disabled — it is a template). The
    /// room geometry itself is game-generated (SkyAlternative.MapGen.cs), not bundled.</summary>
    private static readonly string?[] PrefabBundlePaths =
    {
        null,
        "Assets/Bundle/Environments/Env_Cellar.prefab",
        "Assets/Bundle/Environments/Env_Swamp.prefab",
    };

    // Lazily loaded bundle prefab references — kept for the session once found (doc above).
    private static readonly GameObject?[] Prefabs = new GameObject?[3];
    private static bool _missingWarned; // one-shot: bundle lacks the environment (older bundle)

    // The game sphere we hid (renderer.enabled = false) — re-enabled on restore. Unity fake-null
    // when its scene unloads; then simply forgotten (the scene took the state with it).
    private static Renderer? _hiddenSphere;
    private static int _scanNextFrame;

    // The spawned environment instance — a WORLD-anchored root object (DontDestroyOnLoad, no
    // parent; class doc ANCHORING). Destroyed by Deactivate, never by a scene unload.
    private static GameObject? _envGo;
    private static SkyStyle _appliedStyle = SkyStyle.Default;

    /// <summary>The <see cref="VRRigDriver.RigPoseVersion"/> the current placement was computed
    /// for — a mismatch means the player was (re)built/recentered/ring-seated and the room
    /// re-seats around their new pose (class doc RE-SEAT RULE). Sentinel: never a live version.</summary>
    private static int _placedPoseVersion = int.MinValue;

    /// <summary>Whether the current placement is centered on the scenario play field (class
    /// doc SPAWN POSE, Finding 4). False = the player-point fallback is standing in; the
    /// steady-state tick probes on the <see cref="ScanIntervalFrames"/> cadence and re-centers
    /// the moment the board's tiles exist.</summary>
    private static bool _boardAnchored;
    private static int _nextBoardScanFrame;

    /// <summary>Relative scale drift (vs. the live rig scale) beyond which the defensive heal in
    /// <see cref="EnsureEnvironment"/> re-syncs the environment scale about the head pivot. Only
    /// reachable if a rig-scale writer forgets <see cref="NotifyRigScaled"/> (class doc).</summary>
    private const float ScaleDriftTolerance = 0.001f;

    /// <summary>Throttle for the drift-heal log line (unscaled seconds) — the heal itself is
    /// exact, so repeats mean a writer keeps scaling without notifying, worth one line per
    /// interval rather than one per frame.</summary>
    private const float HealLogIntervalSeconds = 5f;
    private static float _nextHealLogTime;

    private static bool _active;             // non-Default environment currently shown
    private static bool _loggedActive;       // change-dedup for the on/off log

    /// <summary>
    /// Bind the dial into the rig module's config file. Called from
    /// <see cref="Rig.RenderQuality.Bind"/> (which owns that file), AFTER its own binds — the
    /// same ride-along pattern as FlatScreenStereo on the worldui file.
    /// </summary>
    internal static void BindConfig(ConfigFile file)
    {
        if (_bound)
            return;
        _bound = true;
        Style = file.Bind("Sky", "Style", Defaults.SkyStyle,
            "Which surroundings the scenario table sits in (user ruling 2026-08-12: the " +
            "environment renders ONLY inside a scenario, like the game's own default " +
            "surroundings — never in the menu; and it uses the game's own REAL, authored " +
            "level maps, not assembled pieces). Default = the game's own animated sky, exactly " +
            "as before. Cellar = one of the game's authored multi-room dungeon maps dressed as " +
            "a candle-lit stone cellar, plus bundled dust motes; SwampNight = an authored " +
            "multi-room map dressed as a moonlit marsh under a bundled star dome with shooting " +
            "stars, ground fog and fireflies. Rooms keep their authored styling and light rigs; " +
            "only unset style axes are filled in (the swamp always gets the moonlight tone so " +
            "the star dome reads as night). A non-Default choice in a scenario hides the game's " +
            "sky sphere, lets the game's own Apparance engine build the map fully out of view " +
            "(far below the world, beyond the camera's far plane — a few seconds; the FX shell " +
            "shows immediately), then places it as a real-size PLACE IN THE WORLD — the main " +
            "room about 10 m across (whole map up to 24 m), floor-aligned, facing your view, " +
            "and CENTERED ON THE SCENARIO PLAY FIELD: the table diorama sits in the middle of " +
            "the main room (user ruling 2026-08-12), with the other rooms around you to walk " +
            "through and the scenario table untouched. Stick flight, turning, the world-grab " +
            "drag and physical walking all move you through it; the world-grab zoom rescales " +
            "only the board, never the room; the recenter chord (B+Y) re-seats the room around " +
            "you, and re-selecting a style rebuilds it at your current pose. It can never catch " +
            "the laser (no colliders, mod layer only). Applies live from the VR menu, takes " +
            "effect when a scenario is running. MIXED REALITY ALWAYS WINS: while MR is on, " +
            "every sky and environment is off so the chroma key can show your room; the choice " +
            "re-applies when MR turns off. Values from the old panorama builds (Night/Sunset) " +
            "no longer exist and fall back to Default. Local presentation only, never synced " +
            "to peers.");
    }

    /// <summary>
    /// Far-plane floor for <c>VRRigDriver.TickClipPlanes</c>: while an environment is shown its
    /// real-size geometry extends (<see cref="EnvMinFarMeters"/> × rig scale) world units from
    /// its ORIGIN — and since the environment is world-anchored the player can move away from
    /// that origin (class doc FAR PLANE), so the head-to-origin distance is added on top.
    /// 0 while idle (one branch — the caller's Max degenerates to its old value).
    /// </summary>
    internal static float MinFarWorldUnits(float rigScale)
    {
        if (!_active)
            return 0f;
        float floor = EnvMinFarMeters * rigScale;
        GameObject? env = _envGo;
        Camera? head = VRRigDriver.HeadCamera;
        if (env != null && head != null)
            floor += Vector3.Distance(env.transform.position, head.transform.position);
        return floor;
    }

    // ---- per-frame driver ---------------------------------------------------------------------

    /// <summary>
    /// Per-frame driver for the MR-OFF path, called from <see cref="MixedReality.Tick"/> BEFORE
    /// <see cref="SkyBackdrop.Tick"/>. Returns true while an environment is being shown — the
    /// caller passes that straight into SkyBackdrop's stand-down parameter (a hidden sphere
    /// needs no non-occluding treatment). Self-gates on <see cref="VRSession.IsRunning"/>.
    /// </summary>
    internal static bool Tick()
    {
        if (!VRSession.IsRunning)
        {
            RestoreAll();
            return false;
        }

        // Ensure the dial is bound (RenderQuality.Bind rides SkyAlternative.BindConfig along).
        if (!_bound)
            Rig.RenderQuality.Bind();

        // Anything that is not a defined non-Default member is Default — including undefined
        // numeric leftovers a hand-edited cfg can smuggle past Enum.Parse (enum doc above).
        SkyStyle style = Style.Value;
        if (style != SkyStyle.Cellar && style != SkyStyle.SwampNight)
        {
            Deactivate();
            return false;
        }

        // SCENARIO-ONLY SCOPE (class doc, second ruling): outside a live scenario board the
        // feature stands down entirely — the menu keeps the game's default look, exactly like
        // the original surroundings. This is also the lifecycle teardown: leaving/ending a
        // scenario deactivates (and cancels any in-flight generation) on the next tick, before
        // the ProcGen teardown could strand game-generated content in our frame.
        if (!Events.VRModeStateMachine.ScenarioBoardExists)
        {
            Deactivate();
            return false;
        }

        if (!EnsurePrefab(style))
        {
            // Older bundle without the FX shell prefabs — leave the game's own sky fully in
            // place (SkyBackdrop keeps treating it) rather than hiding it with nothing to show.
            Deactivate();
            return false;
        }

        Transform? anchor = VRRigDriver.RigRoot;
        if (anchor == null)
        {
            // No rig this frame (rig-less menu state, or a teardown whose rebuild has not
            // happened yet — ordinary rebuilds tear down and rebuild within one UpdateBody, so
            // they never reach here): stand down cleanly; the first tick under a new root
            // re-applies everything at the player's fresh pose.
            Deactivate();
            return false;
        }

        HideGameSphere();
        EnsureEnvironment(style, anchor);
        if (_envGo != null)
            TickMapGen(style, _envGo.transform); // the game-generated room lane (MapGen partial)

        if (!_active || !_loggedActive)
        {
            _active = true;
            _loggedActive = true;
            VRLog.Info("Core", $"Sky alternative ON — style {style} (scenario active): the game's sky " +
                               "sphere is hidden (pure renderer.enabled hiding; SkyBackdrop stands down), " +
                               "the bundled FX shell is spawned as a world place at the player's pose " +
                               "(world-anchored position/yaw, rig-tracked scale, mod layer — locomotion " +
                               "moves the player through it, zoom never rescales it), and the game-built " +
                               "room is generating (MapGen). MR overrides it off; leaving the scenario " +
                               "despawns it.");
        }
        return true;
    }

    /// <summary>
    /// MR-precedence stand-down, called on <see cref="MixedReality.Tick"/>'s MR-ON path BEFORE
    /// MR's own sky sweep runs: despawns the environment and RE-ENABLES the game sphere, so
    /// <c>HideSkyGeometry</c> records and disables a clean renderer for the chroma key. Cheap and
    /// idempotent (an early-out when nothing is applied); loaded prefab references stay cached.
    /// </summary>
    internal static void StandDown() => Deactivate();

    /// <summary>Full teardown (VR stop / hot reload): stand down and drop the one-shot warn latch.
    /// The cached prefab references are kept — they are session-lifetime by design (class doc).</summary>
    internal static void RestoreAll()
    {
        Deactivate();
        ReleaseMapPrefab(); // the Addressables handle must not outlive the session (MapGen doc)
        _missingWarned = false;
        _scanNextFrame = 0;
    }

    // ---- the game sphere ----------------------------------------------------------------------

    private static void HideGameSphere()
    {
        // Fake-null: the sphere died with its scene — forget it and re-scan (throttled).
        if (_hiddenSphere == null)
        {
            _hiddenSphere = null;
            if (Time.frameCount < _scanNextFrame)
                return;
            _scanNextFrame = Time.frameCount + ScanIntervalFrames;

            Renderer? sphere = SkyBackdrop.FindSky();
            if (sphere == null)
                return; // sphere not generated yet (scenario still loading) — rescan on cadence
            _hiddenSphere = sphere;
            VRLog.Info("Core", $"Sky alternative: hiding the game's sky sphere '{sphere.gameObject.name}' " +
                               "(renderer.enabled = false — pure hiding, no CommandBuffer redraw involved). " +
                               "Re-enabled on Default / MR-on / VR stop.");
        }
        if (_hiddenSphere.enabled)
            _hiddenSphere.enabled = false;
    }

    private static void RestoreGameSphere()
    {
        if (_hiddenSphere != null && !_hiddenSphere.enabled)
            _hiddenSphere.enabled = true;
        _hiddenSphere = null;
        _scanNextFrame = 0; // a re-activation scans immediately
    }

    // ---- bundle assets ------------------------------------------------------------------------

    /// <summary>Load the CHOSEN style's environment prefab from whichever loaded bundle holds it
    /// (established probe pattern — see class doc). True when the prefab is ready.</summary>
    private static bool EnsurePrefab(SkyStyle style)
    {
        int i = (int)style;
        if (i <= 0 || i >= PrefabBundlePaths.Length)
            return false;

        if (Prefabs[i] == null)
        {
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b == null) continue;
                var prefab = b.LoadAsset<GameObject>(PrefabBundlePaths[i]!);
                if (prefab != null) { Prefabs[i] = prefab; break; }
            }
        }

        bool ready = Prefabs[i] != null;
        if (!ready && !_missingWarned)
        {
            _missingWarned = true;
            VRLog.Warn("Core", $"Sky alternative: environment prefab '{PrefabBundlePaths[i]}' not found in any " +
                               "loaded bundle — gloomhavenvr.bundle predates the 3D environments? The game's " +
                               "own sky stays; update the bundle to use the environment styles.");
        }
        return ready;
    }

    // ---- the environment ----------------------------------------------------------------------

    /// <summary>
    /// Spawn (or keep) the world-anchored environment instance. Steady state is checks only —
    /// NO transform writes (the room is world-static; class doc PER-FRAME COST): one static int
    /// compare re-seats it after a rig rebuild/recenter/ring seat, one scale compare is the
    /// defensive drift-heal. Instantiates on first activation and on style switch.
    /// </summary>
    private static void EnsureEnvironment(SkyStyle style, Transform anchor)
    {
        if (_envGo != null && _appliedStyle == style)
        {
            // Steady state (class doc RE-SEAT RULE): RigPoseVersion bumps only on rig
            // (re)build, deliberate recenter, ring seat and menu recenter — the "player was
            // teleported" events. Free locomotion (flight/turn/grab) never bumps it, so the
            // room stays a fixed world place while the player moves through it.
            if (VRRigDriver.RigPoseVersion != _placedPoseVersion)
            {
                PlaceAtPlayer(_envGo.transform, anchor,
                    "rig pose changed (rebuild/recenter/ring seat) — re-seating around the player");
                return;
            }
            // Finding-4 fallback recovery (class doc SPAWN POSE): a placement made before the
            // board's tiles existed is horizontally player-anchored — probe on the scan
            // cadence and re-center on the play field the moment it appears.
            if (!_boardAnchored && Time.frameCount >= _nextBoardScanFrame)
            {
                _nextBoardScanFrame = Time.frameCount + ScanIntervalFrames;
                if (TryGetPlayFieldCenter(out _))
                {
                    PlaceAtPlayer(_envGo.transform, anchor,
                        "play field appeared — centering the room on the board (Finding 4)");
                    return;
                }
            }
            HealScaleDrift(_envGo.transform, anchor);
            return;
        }

        if (_envGo != null)
        {
            Object.Destroy(_envGo); // style switch — the old frame (shell + placed room) goes
            _envGo = null;
            CancelMapGen("style switch"); // and any in-flight generation with it
        }

        // THE AMBIENT FRAME (class doc ANCHORING): an empty WORLD-anchored root — no parent, so
        // locomotion moves the rig relative to the world and therefore through the room;
        // DontDestroyOnLoad so a scene unload can never fake-null a live frame (teardown is
        // always ours, Deactivate). Children: the bundled FX shell (local identity — authored
        // real meters) and, once generated, the game-built room (MapGen, normalized).
        GameObject prefab = Prefabs[(int)style]!;
        _envGo = new GameObject("GloomhavenVR.SkyAlternative." + style);
        Object.DontDestroyOnLoad(_envGo);
        PlaceAtPlayer(_envGo.transform, anchor, "spawn");

        GameObject shell = Object.Instantiate(prefab, _envGo.transform, false);
        shell.name = prefab.name; // authored disabled children (Cellar's 'GlowTemplate') stay disabled
        VRLayers.Apply(_envGo); // mod layer, recursive — head camera only (we are gated on IsRunning)

        // NON-INTERACTIVE BY RULING ("Nicht interaktiv rein als Umgebung"): the contract says
        // the shells ship without colliders, but a stray one would silently eat laser/poke
        // rays across the whole room — strip defensively, once, at spawn.
        Collider[] colliders = shell.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            Object.Destroy(c);

        // Shuriken normalisation + defensive kick (class doc PARTICLES): Hierarchy scaling so
        // the FX follow the root's diorama scale like the meshes do; Local simulation space so
        // in-flight particles ride the root when the zoom scale-follow moves it (the root
        // carries a constant spawn yaw — a World-authored velocity direction is rotated by
        // that constant, harmless for ambient FX). playOnAwake already ran for active systems —
        // the Play() is belt-and-braces for ones authored with it off; disabled template
        // children are normalised but never kicked (they are templates, not FX).
        ParticleSystem[] systems = shell.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
            if (!ps.isPlaying && ps.gameObject.activeInHierarchy)
                ps.Play(withChildren: false);
        }

        bool wasSwitch = _appliedStyle != SkyStyle.Default && _loggedActive;
        _appliedStyle = style;
        VRLog.Info("Core", $"Sky alternative: FX shell '{prefab.name}' spawned in the ambient frame at " +
                           $"the player's pose (floor under the head, facing the view, rig-tracked " +
                           $"scale — locomotion moves the player through it; user report 2026-08-12). " +
                           $"{systems.Length} particle system(s) normalised (Hierarchy scaling, local " +
                           $"simulation space)" +
                           $"{(colliders.Length > 0 ? $", {colliders.Length} stray collider(s) stripped" : "")}." +
                           $"{(wasSwitch ? " (style switch)" : "")} Room generation follows (MapGen).");
    }

    /// <summary>
    /// Write the SPAWN POSE (class doc): floor-aligned, play-field-centered — the horizontal
    /// origin is the scenario play field's world center (Finding 4: "das SPiefeld absolut
    /// mittig"; player floor point as the fallback until the board's tiles exist), the origin
    /// HEIGHT is the tracked head's floor point (head rig-local position with y=0, mapped
    /// through the rig: tracking is floor-origin, so that is the real floor under the player),
    /// yaw = head world forward projected to the horizon, scale = the live rig scale (authored
    /// meters read as real meters). Head not tracked yet (rig just built, first pose pending)
    /// → the rig's own origin/yaw stand in; the first-pose recenter bumps RigPoseVersion and
    /// this re-runs with the real head a frame later.
    /// </summary>
    private static void PlaceAtPlayer(Transform env, Transform anchor, string why)
    {
        float scale = anchor.lossyScale.x;
        if (!(scale > 0f) || float.IsInfinity(scale))
            scale = 1f; // degenerate rig scale must not vanish/explode the room

        Camera? head = VRRigDriver.HeadCamera;
        Vector3 pos;
        Quaternion yaw;
        bool tracked = head != null && head.transform.localPosition.sqrMagnitude > 1e-6f;
        if (tracked)
        {
            Vector3 headLocal = head!.transform.localPosition;
            pos = anchor.TransformPoint(new Vector3(headLocal.x, 0f, headLocal.z));
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f; // world-horizon yaw — the room's walls stay vertical in the world
            yaw = fwd.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(fwd)
                : VRRigDriver.YawOnly(anchor.rotation); // looking straight up/down: seat yaw
        }
        else
        {
            pos = anchor.position;
            yaw = VRRigDriver.YawOnly(anchor.rotation);
        }

        // FINDING 4: the room's horizontal center is the play field's center — only x/z; the
        // height stays the real floor from the player math above, the yaw stays the gaze.
        _boardAnchored = TryGetPlayFieldCenter(out Vector3 boardCenter);
        if (_boardAnchored)
        {
            pos.x = boardCenter.x;
            pos.z = boardCenter.z;
        }

        env.SetPositionAndRotation(pos, yaw);
        env.localScale = Vector3.one * scale;
        SyncRoomLightRanges(); // room light ranges track the room's lossy scale (MapGen)
        _placedPoseVersion = VRRigDriver.RigPoseVersion;
        VRLog.Info("Core", $"Sky alternative: environment placed ({why}) — origin {pos} " +
                           $"({(_boardAnchored ? "play-field-centered" : "player-point fallback, re-centers when the board appears")}), " +
                           $"yaw {yaw.eulerAngles.y:F1}deg, scale {scale:F2} " +
                           $"({(tracked ? "tracked head pose" : "rig pose fallback, head not tracked yet")}).");
    }

    /// <summary>
    /// The scenario play field's world-space center (Finding 4): the root
    /// <c>ProceduralScenario</c>'s own <c>MapTiles</c> (immediate children carrying a
    /// <c>ProceduralMapTile</c> — decompiled ProceduralScenario.cs:208), each tile's authored
    /// <c>BoxCollider</c> bounds encapsulated. ALL tiles count, hidden ones included, so the
    /// center is stable across mid-scenario reveals. The mod's own staged/placed room can
    /// never pollute this: its clone is parented under mod roots, never under the scenario
    /// root (and is guarded against here anyway). Called only on placement events and the
    /// throttled fallback probe — never per-frame.
    /// </summary>
    private static bool TryGetPlayFieldCenter(out Vector3 center)
    {
        center = default;
        try
        {
            ProceduralScenario? scenario = null;
            foreach (ProceduralScenario s in Object.FindObjectsOfType<ProceduralScenario>())
            {
                if (s == null)
                    continue;
                Transform t = s.transform;
                if (_envGo != null && t.IsChildOf(_envGo.transform))
                    continue; // defensive: never our own clone
                if (_stagingRoot != null && t.IsChildOf(_stagingRoot.transform))
                    continue;
                scenario = s;
                break;
            }
            if (scenario == null)
                return false;

            Bounds b = default;
            bool has = false;
            foreach (GameObject tileGo in scenario.MapTiles)
            {
                if (tileGo == null)
                    continue;
                ProceduralMapTile tile = tileGo.GetComponent<ProceduralMapTile>();
                Collider? c = tile != null ? tile.BoxCollider : tileGo.GetComponent<BoxCollider>();
                if (c == null)
                    continue;
                if (!has) { b = c.bounds; has = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!has)
                return false;
            center = b.center;
            return true;
        }
        catch
        {
            return false; // scenario tearing down mid-read — the fallback anchor stands
        }
    }

    /// <summary>
    /// Defensive scale re-sync (class doc PER-FRAME COST): the environment's scale must equal
    /// the rig scale at all times — <see cref="NotifyRigScaled"/> keeps it there through every
    /// known scale writer (WorldGrab two-hand pinch, Comfort.SetScaleMultiplier) and the
    /// re-seat covers rig builds. This heal only ever fires if a FUTURE writer scales the rig
    /// without notifying; it re-syncs about the head pivot (the view does not lurch — the same
    /// pivot rule Comfort.SetScaleMultiplier uses) so the invariant is self-righting rather
    /// than silently broken. Steady-state cost: two float reads and a compare.
    /// </summary>
    private static void HealScaleDrift(Transform env, Transform anchor)
    {
        float rigScale = anchor.lossyScale.x;
        if (!(rigScale > 0f) || float.IsInfinity(rigScale))
            return;
        float envScale = env.localScale.x;
        if (Mathf.Abs(envScale - rigScale) <= ScaleDriftTolerance * rigScale)
            return;

        Camera? head = VRRigDriver.HeadCamera;
        Vector3 pivot = head != null ? head.transform.position : env.position;
        env.position = pivot + (env.position - pivot) * (rigScale / envScale);
        env.localScale = Vector3.one * rigScale;
        SyncRoomLightRanges(); // room light ranges track the room's lossy scale (MapGen)
        if (Time.unscaledTime >= _nextHealLogTime)
        {
            _nextHealLogTime = Time.unscaledTime + HealLogIntervalSeconds;
            VRLog.Warn("Core", $"Sky alternative: environment scale drifted from the rig scale " +
                               $"({envScale:F3} vs {rigScale:F3}) and was healed about the head — " +
                               "some rig-scale writer is not calling SkyAlternative.NotifyRigScaled.");
        }
    }

    /// <summary>
    /// A rig-scale writer just rescaled the rig about <paramref name="pivotWorld"/> (the world
    /// point it kept glued to a tracking point: WorldGrab's hand midpoint, Comfort's head).
    /// Mirror it onto the environment so the room stays bit-frozen in the player's REAL frame —
    /// same real size, same real offset — while drag/turn components of the same gesture pass
    /// through as movement (invariance proof in the class doc ZOOM note). Cheap and re-entrant:
    /// two early-outs while no environment is shown, one transform write while one is.
    /// </summary>
    internal static void NotifyRigScaled(Vector3 pivotWorld, float scaleBefore, float scaleAfter)
    {
        GameObject? env = _envGo;
        if (env == null || !_active)
            return;
        if (!(scaleBefore > 0f) || !(scaleAfter > 0f) || Mathf.Approximately(scaleBefore, scaleAfter))
            return;
        Transform t = env.transform;
        t.position = pivotWorld + (t.position - pivotWorld) * (scaleAfter / scaleBefore);
        t.localScale = Vector3.one * scaleAfter; // absolute, not multiplied: no float-error creep
        SyncRoomLightRanges(); // room light ranges track the room's lossy scale (MapGen)
    }

    // ---- deactivate ---------------------------------------------------------------------------

    /// <summary>Back to vanilla: re-enable the game sphere and destroy the environment instance.
    /// The loaded prefab references stay (session-cached by design — class doc).</summary>
    private static void Deactivate()
    {
        if (!_active && _envGo == null && _hiddenSphere == null)
            return;

        RestoreGameSphere();
        if (_envGo != null)
        {
            Object.Destroy(_envGo); // frame + shell + placed room, all children
            _envGo = null;
        }
        CancelMapGen("deactivate"); // returns a borrowed focus, drops staging, resets the phase
        _appliedStyle = SkyStyle.Default;
        _placedPoseVersion = int.MinValue; // a fresh activation always places fresh
        _boardAnchored = false;
        _nextBoardScanFrame = 0;
        _nextHealLogTime = 0f;
        if (_active)
            VRLog.Info("Core", "Sky alternative OFF — game sphere restored, 3D environment despawned.");
        _active = false;
        _loggedActive = false;
    }
}
