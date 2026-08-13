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

    /// <summary>A candle-lit stone cellar: ONE of the game's own AUTHORED SINGLE-ROOM
    /// scenario maps (preference list in MapGen's <c>MapPreference</c>, 'Map A' terminal)
    /// dressed in its dungeon art (Dungeon/StoneRooms/Candlelight fills, authored axes kept)
    /// plus the Env_Cellar FX shell (dust motes).</summary>
    Cellar = 1,

    /// <summary>A moonlit marsh: ONE of the game's own AUTHORED SINGLE-ROOM scenario maps
    /// (preference list in MapGen's <c>MapPreference</c>, 'Map A' terminal) dressed in its
    /// forest art (Forest/Marsh/StillWaters fills, Tone forced to ForestMoonlight for the
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
///  3. GENERATES the actual room from the game's own art: ONE of the game's AUTHORED
///     SINGLE-ROOM scenario maps (per-style preference lists, 'Map A' terminal — the
///     ModBuild-130 revision; a map that still arrives with several room tiles is CULLED to
///     the one containing the map's center) is instantiated offscreen and dressed by the
///     live Apparance engine — authored per-tile styles kept, unset axes filled with the
///     style vocabulary — then frozen WITH ITS LIFE INTACT (particles, animators, light
///     flicker keep ticking; floor integrity is gated) and seated LIFE-SIZE around the
///     player into the room branch (~<c>TargetMainRoomMeters</c> perceived meters across) —
///     the whole lane lives in <c>SkyAlternative.MapGen.cs</c> (staging pose,
///     hidden-layer/depth staging, borrowed detail focus, settle polling, red-cube heal,
///     floor gate, normalization math, lifecycle). Generation failure degrades to the FX
///     shell alone.
///
/// The game's own scenario diorama/table stays untouched and visible — the environment
/// surrounds it. Everything lives on the MOD LAYER (only the rig head camera renders it; game
/// cameras and the FlatScreen composites never see it) and has every collider stripped —
/// non-interactive by ruling, it must never catch a laser/poke ray.
///
/// ANCHORING — TWO BRANCHES, FOURTH revision. History, because each step was a hardware
/// ruling: (1) ModBuild 125 parented the instance under <see cref="VRRigDriver.RigRoot"/> —
/// killed because every mod locomotion also moves RigRoot, so the room rode along and could
/// never be moved through ("Weiterhin möchte ich mich auch in den umgebungen frei bewegen
/// und drehen können, das ist aktuell nicht möglich."). (2) ModBuilds 127-129 used ONE hybrid
/// frame: world-anchored position/yaw, rig-tracked scale mirrored through
/// <see cref="NotifyRigScaled"/>, so the whole environment read a constant ~10 real meters at
/// every zoom. THE ModBuild-129 FINDING THAT KILLED THE SINGLE FRAME (user, verbatim):
/// "Beim rein und rauszoomen verändert sich die Höhe in dem neuen Raum. Beim Test kam es
/// vor, dass irgendwann das Spielboard so unter der map geladent ist. Die Höhe sowie die
/// Position soll fix sein des Spielboards in dem Raum." Root cause: the zoom scale-follow
/// moved the ROOM's world pose about the zoom pivot while the BOARD's world pose is constant
/// — so zooming moved the board THROUGH the room, and his test ended with the board under
/// the map floor. His ruling makes the frozen-during-zoom board↔room relationship the
/// invariant — but not for the sky, which must keep reading as a distant sky at every zoom.
/// (3) ModBuild 130 then SIZED the world-fixed room off the BOARD's footprint (2.75× the
/// board extent, floor at the board's underside). THE ModBuild-130 VERDICT THAT KILLED
/// BOARD-RELATIVE SIZING (user, verbatim): "Die Umgebung spawned so klein wie das
/// Spielfeld selber! Das soll auf keinen Fall so sein. Der ganze Sinn ist es IN der
/// Umgebung zu sein." Root cause, from his own log: at rig scale 85.21 the board extent is
/// world-tiny at diorama zoom, so 2.75× board extent placed a 5.8-wu map — a PERCEIVED
/// ~7 cm miniature standing NEXT TO the play field. REJECTED ALTERNATIVE, do not bring it
/// back: any board-relative world size inherits the diorama's world-tininess; the room must
/// be sized in PERCEIVED meters at the moment it is seated. Hence the fourth revision:
///
///  - ROOM BRANCH (<c>_roomGo</c>, LIFE-SIZE AT SEAT, WORLD-FROZEN BETWEEN SEATS): the
///    placed game room plus the room-bound shell FX (<see cref="RoomBoundShellChildren"/> —
///    ground fog and fireflies lie on the room's floor). At every SEAT event the room is
///    placed around the PLAYER at PERCEIVED size: branch scale = the live rig scale S, so
///    the main room reads <c>TargetMainRoomMeters</c> (~11) real meters; origin = the
///    player's floor point (the room's center comes to the player — "IN der Umgebung");
///    frame y = 0 is the REAL floor under their feet; yaw from gaze. Between seats the
///    branch is FROZEN in world space: no rig-scale tracking, no per-frame writes, ever —
///    during any zoom gesture board↔room geometry is rigid (finding 3: the board can never
///    sink through the floor mid-gesture), and the scenario board simply stands wherever it
///    stands: a table diorama inside a life-size room. When a zoom SETTLES far outside the
///    seated scale, the room re-seats once (RE-SEAT RULE below).
///  - SKY BRANCH (<c>_skyGo</c>, PERCEIVED-CONSTANT): the star dome, moon, shooting stars
///    and the cellar dust motes keep the 127 model — world-anchored position/yaw
///    (unparented + DontDestroyOnLoad; locomotion moves the player relative to it),
///    rig-tracked scale. At rig scale S the dome's world size is (authored meters × S): its
///    perceived size is the authored meters, always — a distant sky at every zoom. ZOOM
///    ALGEBRA (sky branch only): the rig scales about a PIVOT (WorldGrab keeps the world
///    point under the hands glued, <c>Comfort.SetScaleMultiplier</c> keeps the head still),
///    so a merely world-fixed dome would keep its real size but DRIFT. Both scale writers
///    report through <see cref="NotifyRigScaled"/> (pivot, before, after) and the branch
///    mirrors it: <c>skyPos = pivot + (skyPos − pivot) · (after/before)</c>,
///    <c>skyScale = after</c>. Proof this is exact: with the rig mapping world = P + R·(s·t),
///    the perceived (tracking-space) pose of the branch is (1/s)·R⁻¹·(skyPos − P). The scale
///    writer keeps the pivot glued to a tracking point m: P = pivot − R·(s·m). Substituting
///    both updates, the perceived pose after the write equals the pose a pure locomotion
///    write (same P, R change, scale untouched) would produce — the scale component is
///    bit-cancelled, the sky neither grows nor drifts, while drag/turn components of the
///    same gesture still pass through as movement.
///
/// Shell children are routed BY NODE NAME at spawn (contract with the content lane, which
/// keeps the names stable; Env_Cellar gains the same star-dome/moon nodes as Env_Swamp):
/// names in <see cref="RoomBoundShellChildren"/> go to the room branch, every other child
/// stays on the sky branch — for an ambient effect around the player, perceived-constant is
/// the safe default reading, so unknown names default to the sky.
///
/// SPAWN POSE. SKY branch (unchanged from Finding 4): horizontal origin = the play field's
/// world center (<see cref="TryGetPlayFieldBounds"/>: the root <c>ProceduralScenario</c>'s
/// <c>MapTiles</c>, each tile's authored <c>BoxCollider</c> bounds encapsulated; ALL tiles,
/// hidden included, so a mid-scenario reveal never re-centers anything), origin height = the
/// player's floor point (the rig-space point under the head, y=0 — tracking is floor-origin
/// — mapped through the rig), yaw = the head's world forward projected to the horizon,
/// scale = the rig scale. ROOM branch (fourth revision — LIFE-SIZE, PLAYER-CENTERED):
///  - SCALE: the live rig scale S at the seat event. MapGen normalizes the room's main
///    tile to <c>TargetMainRoomMeters</c> (~11) frame units, so at branch scale S the room
///    reads ~11 PERCEIVED meters across — a walkable, life-size room at any zoom, by
///    construction. (The rejected 130 alternative sized it off the board extent: world-tiny
///    at diorama zoom, the miniature verdict above.)
///  - HORIZONTAL: the player's floor point = the room's center (MapGen anchors the room
///    tile's center to the frame origin) — the player spawns IN the room; the board stands
///    wherever it stands inside it.
///  - VERTICAL: the room floor (frame-local y = 0) sits at the player's REAL floor point —
///    the floor under their feet is the room's floor.
///  - YAW: the gaze yaw, same as the sky branch.
///  Board not measurable yet (scenario still assembling): the SKY spawns at the player-point
///  fallback and the throttled steady-state probe re-centers the SKY once, the moment the
///  tiles exist (sky only — the room is player-centered and never needs the board). Head not
///  tracked yet (rig just built) → the rig's own pose stands in; the first-pose recenter
///  bumps RigPoseVersion and re-places a frame later.
///
/// BOARD DRAG — determined, no follow cadence needed: every mod locomotion/zoom gesture
/// writes the RIG's transform, never the board's (READ FROM SOURCE: Rig/WorldGrab.cs writes
/// <c>rig.position</c>/<c>rig.rotation</c> at 304/401-403; Flight and SnapTurn likewise move
/// the rig), and the scenario diorama itself is static game world geometry no mod feature
/// drags. The board's world pose changes only when a scenario (re)builds — which re-enters
/// through the spawn/probe path above anyway. A world-frozen room branch therefore never
/// needs to chase the board.
///
/// RE-SEAT RULE: BOTH branches are re-placed (fresh spawn pose) whenever
/// <see cref="VRRigDriver.RigPoseVersion"/> changes — rig (re)build, deliberate recenter
/// (B+Y chord), spawn-ring seat, menu recenter. Those are exactly the "the player was
/// teleported" events (snap turns and world grabs do NOT bump it, by that counter's own
/// contract); free movement never re-seats anything. This also IS the "recenter environment"
/// affordance: the recenter chord re-derives both branches around you; re-selecting a style
/// (switch away and back, or to the other style) respawns both at the current pose — no new
/// UI. NEW IN THE FOURTH REVISION — SCALE-SETTLE RE-SEAT: a life-size room seated at scale
/// S_seat stops being life-size once the player zooms far away from S_seat, so when a
/// rig-scale change SETTLES (no scale write for <see cref="ScaleSettleSeconds"/>, and
/// |ln(S_now/S_seat)| exceeds ln(<see cref="ScaleReseatRatio"/>)), both branches re-seat
/// around the player at the new scale — the SAME event class as the recenter chord's
/// existing re-seat (that pop is established behavior; a mid-GESTURE write is still never
/// made: during the gesture everything stays frozen, finding 3 honored).
///
/// RIG REBUILD / MID-REBUILD FRAMES: on a frame with no RigRoot the whole feature stands down
/// (environment despawned, sphere restored) exactly as before — rebuilds are rare, logged
/// events, and the next tick under the new root respawns at the new player pose. The instance
/// is DontDestroyOnLoad so a scene unload can never fake-null it out from under a live rig.
///
/// PER-FRAME COST: ZERO transform writes while active and idle — both branches are
/// world-static between events. The active steady-state tick is: enum read, sphere-hidden
/// check, instance/anchor null checks, ONE static int compare (RigPoseVersion), one float
/// compare for the scale-settle sampler (<see cref="TickScaleSettleReseat"/>) and one scale
/// compare on the SKY branch (a defensive drift-heal that only ever fires if a future
/// rig-scale writer forgets to call <see cref="NotifyRigScaled"/>). Transform writes happen
/// only inside a pinch-zoom (one position+scale write per scaled frame on the SKY branch
/// only — the room branch never moves) and on the rare re-seat events. Default/MR-on: an
/// enum read plus an idempotent early-out.
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
/// FAR PLANE: the SKY branch is real-size, so at rig scale S its farthest geometry (the
/// star dome) sits up to (authored meters × S) world units from its ORIGIN — and the player
/// can fly away from that origin. The ROOM branch is world-fixed, so zooming IN (small S)
/// can make the room the farther of the two in world units. <see cref="MinFarWorldUnits"/>
/// hands VRRigDriver.TickClipPlanes the max of both branch budgets: (head-to-sky-origin
/// distance + <see cref="EnvMinFarMeters"/> × S) and (head-to-room-origin distance + the
/// placed map's world extent, tracked by MapGen as <c>_roomFrameExtent</c> × branch scale) —
/// 0 when idle, still capped by the depth-precision far/near ratio.
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

    // THE TWO BRANCHES (class doc ANCHORING, third revision) — WORLD-anchored root objects
    // (DontDestroyOnLoad, no parent). Destroyed by Deactivate, never by a scene unload.
    // _skyGo: perceived-constant (rig-tracked scale via NotifyRigScaled) — dome/moon/stars/motes.
    // _roomGo: world-fixed after placement — the placed game map + floor-bound shell FX.
    private static GameObject? _skyGo;
    private static GameObject? _roomGo;
    private static SkyStyle _appliedStyle = SkyStyle.Default;

    /// <summary>Shell child NODE NAMES that belong on the world-fixed ROOM branch — FX that
    /// lie on the room's floor. Everything else stays on the sky branch (class doc: for
    /// ambient FX around the player, perceived-constant is the safe default, so a future
    /// unknown node lands there). Contract with the content lane: names are stable, and
    /// Env_Cellar gains the same star-dome/moon nodes as Env_Swamp.</summary>
    private static readonly string[] RoomBoundShellChildren = { "GroundFog", "Fireflies" };

    /// <summary>SCALE-SETTLE RE-SEAT band (class doc RE-SEAT RULE, fourth revision): once a
    /// rig-scale change has settled and |ln(S_now/S_seat)| exceeds ln(this), the life-size
    /// room re-seats around the player at the new scale. 1.4 ≈ the point where an ~11 m room
    /// has drifted to reading under 8 m or over 15 m — outside the 10-14 m walkable band.</summary>
    private const float ScaleReseatRatio = 1.4f;

    /// <summary>How long the rig scale must hold still (unscaled seconds) before a scale
    /// change counts as SETTLED — during the gesture itself everything stays frozen
    /// (finding 3: no mid-gesture geometry writes, the board can never sink mid-zoom).</summary>
    private const float ScaleSettleSeconds = 0.7f;

    /// <summary>The rig scale the room was last SEATED at (class doc SPAWN POSE) — the
    /// reference for the scale-settle re-seat band. 0 = no seat yet.</summary>
    private static float _placedRigScale;

    // Scale-settle sampler state (TickScaleSettleReseat): the last rig scale seen and the
    // unscaled time it last CHANGED — "settled" = unchanged for ScaleSettleSeconds.
    private static float _lastSeenRigScale;
    private static float _lastScaleActivityTime;

    /// <summary>The <see cref="VRRigDriver.RigPoseVersion"/> the current placement was computed
    /// for — a mismatch means the player was (re)built/recentered/ring-seated and the room
    /// re-seats around their new pose (class doc RE-SEAT RULE). Sentinel: never a live version.</summary>
    private static int _placedPoseVersion = int.MinValue;

    /// <summary>Whether the SKY branch is centered on the scenario play field (class doc
    /// SPAWN POSE, Finding 4 — sky only; the room is player-centered by the fourth revision
    /// and never needs the board). False = the player-point fallback is standing in; the
    /// steady-state tick probes on the <see cref="ScanIntervalFrames"/> cadence and
    /// re-centers the SKY the moment the board's tiles exist.</summary>
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
            "Which surroundings you play in (user rulings 2026-08-12/13: the environment " +
            "renders ONLY inside a scenario, like the game's own default surroundings — " +
            "never in the menu; it uses ONE REAL single room from the game's own scenario " +
            "maps, never a whole multi-room level; and it must be LIFE-SIZE — the whole " +
            "point is to be IN the environment, not to look at a miniature next to the " +
            "board). Default = the game's own animated sky, exactly as before. Cellar = an " +
            "authored scenario room dressed as a candle-lit stone cellar, plus bundled dust " +
            "motes; SwampNight = an authored scenario room dressed as a moonlit marsh under " +
            "a bundled star dome with shooting stars, ground fog and fireflies. The room " +
            "keeps its authored styling AND its life: torch flames, dust, drips, animated " +
            "props and the room's own flickering light rigs keep playing (only unset style " +
            "axes are filled in; the swamp always gets the moonlight tone so the star dome " +
            "reads as night), and its floor is guaranteed closed — missing floor pieces are " +
            "healed from the game's own floor art or patched with a neighboring tile, never " +
            "left as holes. A non-Default choice in a scenario hides the game's sky sphere, " +
            "lets the game's own Apparance engine build the room fully out of view — a few " +
            "seconds; the FX shell shows immediately — then seats it LIFE-SIZE around YOU: " +
            "about 11 m across at your current zoom, centered on where you stand, its floor " +
            "at the real floor under your feet, facing your view. The scenario table simply " +
            "stands inside it like a diorama in a room. While you zoom, board and room stay " +
            "rigidly glued (only their perceived size changes — the board can never sink " +
            "below the floor mid-gesture); when a zoom SETTLES far outside the size the " +
            "room was seated at (about 1.4x either way), the room re-seats around you once " +
            "at the new scale — the same kind of jump as the recenter chord (B+Y), which " +
            "also re-seats everything, as does re-selecting a style. The star dome, moon " +
            "and dust motes stay a DISTANT SKY at every zoom level. Stick flight, turning, " +
            "the world-grab drag and physical walking all move you through the room. It can " +
            "never catch the laser (no colliders, mod layer only). Applies live from the VR " +
            "menu, takes effect when a scenario is running. MIXED REALITY ALWAYS WINS: " +
            "while MR is on, every sky and environment is off so the chroma key can show " +
            "your room; the choice re-applies when MR turns off. Values from the old " +
            "panorama builds (Night/Sunset) no longer exist and fall back to Default. Local " +
            "presentation only, never synced to peers.");
    }

    /// <summary>
    /// Far-plane floor for <c>VRRigDriver.TickClipPlanes</c>: the max of both branch budgets
    /// (class doc FAR PLANE). Sky: (<see cref="EnvMinFarMeters"/> × rig scale) world units
    /// from its origin plus the head-to-origin distance (the player can fly away from it).
    /// Room: world-FIXED size, so when the player zooms IN the room can be the farther of
    /// the two — its placed world extent (MapGen's <c>_roomFrameExtent</c> × branch scale)
    /// plus the head-to-origin distance. 0 while idle (the caller's Max degenerates to its
    /// old value).
    /// </summary>
    internal static float MinFarWorldUnits(float rigScale)
    {
        if (!_active)
            return 0f;
        float floor = EnvMinFarMeters * rigScale;
        Camera? head = VRRigDriver.HeadCamera;
        GameObject? sky = _skyGo;
        if (sky != null && head != null)
            floor += Vector3.Distance(sky.transform.position, head.transform.position);
        GameObject? room = _roomGo;
        if (room != null && head != null && _roomFrameExtent > 0f)
        {
            float roomFloor = _roomFrameExtent * room.transform.lossyScale.x
                + Vector3.Distance(room.transform.position, head.transform.position);
            if (roomFloor > floor)
                floor = roomFloor;
        }
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
        if (_roomGo != null)
            TickMapGen(style, _roomGo.transform); // the game-generated room lane (MapGen partial)
                                                  // — placed into the WORLD-FIXED room branch

        if (!_active || !_loggedActive)
        {
            _active = true;
            _loggedActive = true;
            VRLog.Info("Core", $"Sky alternative ON — style {style} (scenario active): the game's sky " +
                               "sphere is hidden (pure renderer.enabled hiding; SkyBackdrop stands down), " +
                               "the bundled FX shell is spawned split over the two world branches (sky = " +
                               "rig-tracked scale so it stays a distant sky; room = world-fixed, sized " +
                               "from the board — finding 3), and the game-built room is generating " +
                               "(MapGen). MR overrides it off; leaving the scenario despawns it.");
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
    /// Spawn (or keep) the two world-anchored branches. Steady state is checks only — NO
    /// transform writes (both branches are world-static between events; class doc PER-FRAME
    /// COST): one static int compare re-seats after a rig rebuild/recenter/ring seat, one
    /// scale compare is the SKY branch's defensive drift-heal. Instantiates on first
    /// activation and on style switch.
    /// </summary>
    private static void EnsureEnvironment(SkyStyle style, Transform anchor)
    {
        if (_skyGo != null && _roomGo != null && _appliedStyle == style)
        {
            // Steady state (class doc RE-SEAT RULE): RigPoseVersion bumps only on rig
            // (re)build, deliberate recenter, ring seat and menu recenter — the "player was
            // teleported" events. Free locomotion (flight/turn/grab) never bumps it, so both
            // branches stay fixed world places while the player moves through them.
            if (VRRigDriver.RigPoseVersion != _placedPoseVersion)
            {
                PlaceBothBranches(anchor,
                    "rig pose changed (rebuild/recenter/ring seat) — re-seating around the player");
                return;
            }
            // SCALE-SETTLE RE-SEAT (class doc RE-SEAT RULE, fourth revision): a zoom that
            // ended far outside the seated scale re-seats the life-size room once.
            if (TickScaleSettleReseat(anchor))
                return;
            // Finding-4 fallback recovery (class doc SPAWN POSE): a SKY placed before the
            // board's tiles existed is player-anchored — probe on the scan cadence and
            // re-center the SKY the moment the board appears. Sky ONLY: the room is
            // player-centered (fourth revision) and re-placing it here would be a pop
            // outside the sanctioned event classes.
            if (!_boardAnchored && Time.frameCount >= _nextBoardScanFrame)
            {
                _nextBoardScanFrame = Time.frameCount + ScanIntervalFrames;
                if (TryGetPlayFieldBounds(out _))
                {
                    PlaceBothBranches(anchor,
                        "play field appeared — centering the sky on the board (Finding 4)",
                        skyOnly: true);
                    return;
                }
            }
            HealScaleDrift(_skyGo.transform, anchor); // sky branch only — the room never rescales
            return;
        }

        if (_skyGo != null || _roomGo != null)
        {
            // Style switch (or a half-built pair) — both branches go, with any in-flight gen.
            if (_skyGo != null) Object.Destroy(_skyGo);
            if (_roomGo != null) Object.Destroy(_roomGo);
            _skyGo = null;
            _roomGo = null;
            CancelMapGen("style switch");
        }

        // THE TWO BRANCH ROOTS (class doc ANCHORING): empty WORLD-anchored roots — no parent,
        // so locomotion moves the rig relative to the world and therefore through them;
        // DontDestroyOnLoad so a scene unload can never fake-null a live branch (teardown is
        // always ours, Deactivate). Sky children: the bundled shell minus the floor FX.
        // Room children: the floor FX now, the game-built map after MapGen finalizes.
        GameObject prefab = Prefabs[(int)style]!;
        _skyGo = new GameObject("GloomhavenVR.SkyAlternative.Sky." + style);
        _roomGo = new GameObject("GloomhavenVR.SkyAlternative.RoomFrame." + style);
        Object.DontDestroyOnLoad(_skyGo);
        Object.DontDestroyOnLoad(_roomGo);
        PlaceBothBranches(anchor, "spawn");

        GameObject shell = Object.Instantiate(prefab, _skyGo.transform, false);
        shell.name = prefab.name; // authored disabled children (Cellar's 'GlowTemplate') stay disabled

        // SPLIT BY NODE NAME (class doc ANCHORING): floor-bound FX move to the world-fixed
        // room branch keeping their authored local pose (they are authored around the origin
        // at floor level — the room frame's origin IS its floor); everything else (dome,
        // moon, shooting stars, dust motes, future nodes) stays perceived-constant on the sky.
        int rehomed = 0;
        for (int ci = shell.transform.childCount - 1; ci >= 0; ci--)
        {
            Transform child = shell.transform.GetChild(ci);
            for (int n = 0; n < RoomBoundShellChildren.Length; n++)
            {
                if (string.Equals(child.name, RoomBoundShellChildren[n], System.StringComparison.Ordinal))
                {
                    child.SetParent(_roomGo.transform, false); // local pose preserved in the new frame
                    rehomed++;
                    break;
                }
            }
        }

        VRLayers.Apply(_skyGo);  // mod layer, recursive — head camera only (gated on IsRunning)
        VRLayers.Apply(_roomGo);

        // NON-INTERACTIVE BY RULING ("Nicht interaktiv rein als Umgebung"): the contract says
        // the shells ship without colliders, but a stray one would silently eat laser/poke
        // rays across the whole room — strip defensively, once, at spawn (both branches).
        int strippedColliders = 0;
        foreach (GameObject branch in new[] { _skyGo, _roomGo })
        {
            Collider[] colliders = branch.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in colliders)
                Object.Destroy(c);
            strippedColliders += colliders.Length;
        }

        // Shuriken normalisation + defensive kick (class doc PARTICLES): Hierarchy scaling so
        // the FX follow their branch root's scale like the meshes do; Local simulation space
        // so in-flight particles ride the root when it moves (sky: the zoom scale-follow;
        // room: the rare re-seat) instead of smearing behind it. playOnAwake already ran for
        // active systems — the Play() is belt-and-braces for ones authored with it off;
        // disabled template children are normalised but never kicked (templates, not FX).
        int systemCount = 0;
        foreach (GameObject branch in new[] { _skyGo, _roomGo })
        {
            ParticleSystem[] systems = branch.GetComponentsInChildren<ParticleSystem>(true);
            systemCount += systems.Length;
            foreach (ParticleSystem ps in systems)
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                if (!ps.isPlaying && ps.gameObject.activeInHierarchy)
                    ps.Play(withChildren: false);
            }
        }

        bool wasSwitch = _appliedStyle != SkyStyle.Default && _loggedActive;
        _appliedStyle = style;
        VRLog.Info("Core", $"Sky alternative: FX shell '{prefab.name}' spawned and SPLIT over the two " +
                           $"branches — {rehomed} floor-bound node(s) ({string.Join("/", RoomBoundShellChildren)}) " +
                           $"onto the world-fixed room frame, the rest (dome/stars/motes) on the " +
                           $"rig-scale-tracked sky frame (finding 3, user report 2026-08-12). " +
                           $"{systemCount} particle system(s) normalised (Hierarchy scaling, local " +
                           $"simulation space)" +
                           $"{(strippedColliders > 0 ? $", {strippedColliders} stray collider(s) stripped" : "")}." +
                           $"{(wasSwitch ? " (style switch)" : "")} Room generation follows (MapGen).");
    }

    /// <summary>
    /// Write the SPAWN POSE for BOTH branches (class doc SPAWN POSE). SKY: play-field-centered
    /// x/z (player floor point until the board exists), height = the tracked head's floor
    /// point (head rig-local position with y=0, mapped through the rig: tracking is
    /// floor-origin, so that is the real floor under the player), yaw = head world forward
    /// projected to the horizon, scale = the live rig scale. ROOM (fourth revision —
    /// LIFE-SIZE, PLAYER-CENTERED): origin = the player's floor point (room center around
    /// the player, room floor at the real floor under their feet), yaw = gaze, scale = the
    /// live rig scale — so the main room reads ~<c>TargetMainRoomMeters</c> PERCEIVED meters
    /// at this zoom, at any zoom. Head not tracked yet (rig just built, first pose pending)
    /// → the rig's own origin/yaw stand in; the first-pose recenter bumps RigPoseVersion and
    /// this re-runs with the real head a frame later. <paramref name="skyOnly"/> is the
    /// Finding-4 board probe: it re-centers the SKY on the board without disturbing the
    /// seated room (no room bookkeeping is touched).
    /// </summary>
    private static void PlaceBothBranches(Transform anchor, string why, bool skyOnly = false)
    {
        float rigScale = anchor.lossyScale.x;
        if (!(rigScale > 0f) || float.IsInfinity(rigScale))
            rigScale = 1f; // degenerate rig scale must not vanish/explode the environment

        Camera? head = VRRigDriver.HeadCamera;
        Vector3 floorPos;
        Quaternion yaw;
        bool tracked = head != null && head.transform.localPosition.sqrMagnitude > 1e-6f;
        if (tracked)
        {
            Vector3 headLocal = head!.transform.localPosition;
            floorPos = anchor.TransformPoint(new Vector3(headLocal.x, 0f, headLocal.z));
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f; // world-horizon yaw — the walls stay vertical in the world
            yaw = fwd.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(fwd)
                : VRRigDriver.YawOnly(anchor.rotation); // looking straight up/down: seat yaw
        }
        else
        {
            floorPos = anchor.position;
            yaw = VRRigDriver.YawOnly(anchor.rotation);
        }

        _boardAnchored = TryGetPlayFieldBounds(out Bounds board);

        // SKY branch (Finding 4 semantics, unchanged): board-centered x/z, player-floor
        // height, gaze yaw, rig scale — NotifyRigScaled keeps it perceived-constant from here.
        Vector3 skyPos = floorPos;
        if (_boardAnchored)
        {
            skyPos.x = board.center.x;
            skyPos.z = board.center.z;
        }
        if (_skyGo != null)
        {
            _skyGo.transform.SetPositionAndRotation(skyPos, yaw);
            _skyGo.transform.localScale = Vector3.one * rigScale;
        }
        if (skyOnly)
        {
            VRLog.Info("Core", $"Sky alternative: sky re-centered ({why}) — sky origin {skyPos} scale " +
                               $"{rigScale:F2} (rig-tracked); the seated room is untouched.");
            return;
        }

        // ROOM branch (fourth revision — the ModBuild-130 verdict "Der ganze Sinn ist es IN
        // der Umgebung zu sein"): LIFE-SIZE around the PLAYER. Scale = the live rig scale,
        // so the main room reads ~TargetMainRoomMeters PERCEIVED meters; origin = the
        // player's floor point (room center comes to the player, room floor = the real
        // floor under their feet). The board participates in NOTHING here — board-relative
        // sizing is the rejected 130 miniature (class doc ANCHORING).
        if (_roomGo != null)
        {
            _roomGo.transform.SetPositionAndRotation(floorPos, yaw);
            _roomGo.transform.localScale = Vector3.one * rigScale;
        }
        SyncRoomLightRanges();        // room light ranges track the room's lossy scale (MapGen)
        NotifyRoomFrameMoved();       // re-base world-space animation caches, e.g. LightFlicker (MapGen)

        _placedPoseVersion = VRRigDriver.RigPoseVersion;
        _placedRigScale = rigScale;                    // the scale-settle band's new reference
        _lastSeenRigScale = rigScale;
        _lastScaleActivityTime = Time.unscaledTime;
        VRLog.Info("Core", $"Sky alternative: environment placed ({why}) — sky origin {skyPos} scale " +
                           $"{rigScale:F2} (rig-tracked); room origin {floorPos} scale {rigScale:F2} " +
                           $"(LIFE-SIZE around the player: main room ~{TargetMainRoomMeters:F0} perceived m, " +
                           "world-frozen until the next seat event); " +
                           $"yaw {yaw.eulerAngles.y:F1}deg " +
                           $"({(tracked ? "tracked head pose" : "rig pose fallback, head not tracked yet")}).");
    }

    /// <summary>
    /// The SCALE-SETTLE RE-SEAT sampler (class doc RE-SEAT RULE, fourth revision). Steady
    /// state cost: one float compare. While a zoom gesture is writing the rig scale, every
    /// change re-arms the settle timer — geometry stays frozen for the whole gesture
    /// (finding 3). Once the scale has held for <see cref="ScaleSettleSeconds"/> AND the
    /// settled scale is outside the ln(<see cref="ScaleReseatRatio"/>) band around the
    /// seated scale, both branches re-seat around the player — the same event class as the
    /// recenter chord's re-seat (an established, sanctioned pop). True = re-seated.
    /// </summary>
    private static bool TickScaleSettleReseat(Transform anchor)
    {
        float s = anchor.lossyScale.x;
        if (!(s > 0f) || float.IsInfinity(s))
            return false;
        float now = Time.unscaledTime;
        if (Mathf.Abs(s - _lastSeenRigScale) > 1e-4f * Mathf.Max(s, _lastSeenRigScale))
        {
            _lastSeenRigScale = s;
            _lastScaleActivityTime = now; // gesture in flight — everything stays frozen
            return false;
        }
        if (now - _lastScaleActivityTime < ScaleSettleSeconds)
            return false;
        if (!(_placedRigScale > 0f)
            || Mathf.Abs(Mathf.Log(s / _placedRigScale)) < Mathf.Log(ScaleReseatRatio))
            return false;
        PlaceBothBranches(anchor,
            $"rig scale settled at {s:F2} vs seated {_placedRigScale:F2} — over the " +
            $"{ScaleReseatRatio:F1}x band, re-seating the life-size room at the new scale");
        return true;
    }

    /// <summary>
    /// The scenario play field's world-space BOUNDS (Findings 3+4): the root
    /// <c>ProceduralScenario</c>'s own <c>MapTiles</c> (immediate children carrying a
    /// <c>ProceduralMapTile</c> — decompiled ProceduralScenario.cs:208), each tile's authored
    /// <c>BoxCollider</c> bounds encapsulated. ALL tiles count, hidden ones included, so the
    /// result is stable across mid-scenario reveals. Center drives both branches' x/z;
    /// size/min drive the room branch's fixed scale and floor height (finding 3). The mod's
    /// own staged/placed room can never pollute this: its clone is parented under mod roots,
    /// never under the scenario root (and is guarded against here anyway). Called only on
    /// placement events and the throttled fallback probe — never per-frame.
    /// </summary>
    private static bool TryGetPlayFieldBounds(out Bounds bounds)
    {
        bounds = default;
        try
        {
            ProceduralScenario? scenario = null;
            foreach (ProceduralScenario s in Object.FindObjectsOfType<ProceduralScenario>())
            {
                if (s == null)
                    continue;
                Transform t = s.transform;
                if (_skyGo != null && t.IsChildOf(_skyGo.transform))
                    continue; // defensive: never our own clone
                if (_roomGo != null && t.IsChildOf(_roomGo.transform))
                    continue;
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
            bounds = b;
            return true;
        }
        catch
        {
            return false; // scenario tearing down mid-read — the fallback anchor stands
        }
    }

    /// <summary>
    /// Defensive scale re-sync, SKY BRANCH ONLY (class doc PER-FRAME COST): the sky's scale
    /// must equal the rig scale at all times — <see cref="NotifyRigScaled"/> keeps it there
    /// through every known scale writer (WorldGrab two-hand pinch, Comfort.SetScaleMultiplier)
    /// and the re-seat covers rig builds. This heal only ever fires if a FUTURE writer scales
    /// the rig without notifying; it re-syncs about the head pivot (the view does not lurch —
    /// the same pivot rule Comfort.SetScaleMultiplier uses) so the invariant is self-righting
    /// rather than silently broken. The ROOM branch is deliberately not touched: it is
    /// world-fixed by finding 3 and has no rig-scale invariant to heal. Steady-state cost:
    /// two float reads and a compare.
    /// </summary>
    private static void HealScaleDrift(Transform sky, Transform anchor)
    {
        float rigScale = anchor.lossyScale.x;
        if (!(rigScale > 0f) || float.IsInfinity(rigScale))
            return;
        float skyScale = sky.localScale.x;
        if (Mathf.Abs(skyScale - rigScale) <= ScaleDriftTolerance * rigScale)
            return;

        Camera? head = VRRigDriver.HeadCamera;
        Vector3 pivot = head != null ? head.transform.position : sky.position;
        sky.position = pivot + (sky.position - pivot) * (rigScale / skyScale);
        sky.localScale = Vector3.one * rigScale;
        if (Time.unscaledTime >= _nextHealLogTime)
        {
            _nextHealLogTime = Time.unscaledTime + HealLogIntervalSeconds;
            VRLog.Warn("Core", $"Sky alternative: sky-branch scale drifted from the rig scale " +
                               $"({skyScale:F3} vs {rigScale:F3}) and was healed about the head — " +
                               "some rig-scale writer is not calling SkyAlternative.NotifyRigScaled.");
        }
    }

    /// <summary>
    /// A rig-scale writer just rescaled the rig about <paramref name="pivotWorld"/> (the world
    /// point it kept glued to a tracking point: WorldGrab's hand midpoint, Comfort's head).
    /// Mirror it onto the SKY BRANCH ONLY so the dome/moon/stars stay bit-frozen in the
    /// player's REAL frame — same perceived size, same perceived offset, a distant sky at
    /// every zoom — while drag/turn components of the same gesture pass through as movement
    /// (invariance proof in the class doc ZOOM ALGEBRA note). The ROOM branch is deliberately
    /// NOT mirrored: finding 3 (ModBuild 129) fixes the board's pose in the room, so the room
    /// stays world-frozen and zoom changes only its perceived size. Cheap and re-entrant: two
    /// early-outs while no environment is shown, one transform write while one is.
    /// </summary>
    internal static void NotifyRigScaled(Vector3 pivotWorld, float scaleBefore, float scaleAfter)
    {
        GameObject? sky = _skyGo;
        if (sky == null || !_active)
            return;
        if (!(scaleBefore > 0f) || !(scaleAfter > 0f) || Mathf.Approximately(scaleBefore, scaleAfter))
            return;
        Transform t = sky.transform;
        t.position = pivotWorld + (t.position - pivotWorld) * (scaleAfter / scaleBefore);
        t.localScale = Vector3.one * scaleAfter; // absolute, not multiplied: no float-error creep
    }

    // ---- deactivate ---------------------------------------------------------------------------

    /// <summary>Back to vanilla: re-enable the game sphere and destroy both branch roots.
    /// The loaded prefab references stay (session-cached by design — class doc).</summary>
    private static void Deactivate()
    {
        if (!_active && _skyGo == null && _roomGo == null && _hiddenSphere == null)
            return;

        RestoreGameSphere();
        if (_skyGo != null)
        {
            Object.Destroy(_skyGo); // sky branch: shell dome/stars/motes
            _skyGo = null;
        }
        if (_roomGo != null)
        {
            Object.Destroy(_roomGo); // room branch: floor FX + placed room, all children
            _roomGo = null;
        }
        CancelMapGen("deactivate"); // returns a borrowed focus, drops staging, resets the phase
        _appliedStyle = SkyStyle.Default;
        _placedPoseVersion = int.MinValue; // a fresh activation always places fresh
        _placedRigScale = 0f;              // and the scale-settle band re-arms from that seat
        _lastSeenRigScale = 0f;
        _lastScaleActivityTime = 0f;
        _boardAnchored = false;
        _nextBoardScanFrame = 0;
        _nextHealLogTime = 0f;
        if (_active)
            VRLog.Info("Core", "Sky alternative OFF — game sphere restored, 3D environment despawned.");
        _active = false;
        _loggedActive = false;
    }
}
