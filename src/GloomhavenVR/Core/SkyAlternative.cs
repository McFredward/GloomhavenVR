using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Rig;
using Script.Controller;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// The player's environment choice. Stored as the mod's own enum (BepInEx serialises the member
/// name into the cfg, and the catalog's Choice classification gives it a dropdown for free); the
/// member order IS the dropdown index map (Default=0/Cellar=1/SwampNight=2/OffBlack=3), exactly
/// like <c>Cards.BoardMoveMode</c>.
///
/// APPENDING ONLY, NEVER RENUMBERING: the cfg persists the member NAME, so reordering would
/// silently repoint a peer's or a player's stored choice at a different environment, and the
/// curated dropdown in <c>WorldUI/VROptionsTab.4.Curated.cs</c> maps its index 1:1 onto these
/// values. <see cref="SkyStyle.OffBlack"/> is therefore appended at 3 even though "nothing"
/// would read more naturally next to Default.
///
/// CONFIG MIGRATION from the panorama era (ModBuild 124 shipped Default/Night/Sunset/Cellar):
/// BepInEx 5's <c>ConfigEntryBase.SetSerializedValue</c> wraps the enum parse
/// (<c>TomlTypeConverter</c> → <c>Enum.Parse(type, value, ignoreCase: true)</c>) in a try/catch —
/// a persisted name that no longer exists ("Night", "Sunset") throws inside the parse, BepInEx
/// logs <c>Config value of setting "..." could not be parsed and will be ignored</c> (string
/// verified in the referenced BepInEx.dll 5.4.20) and the entry KEEPS ITS BOUND DEFAULT, i.e.
/// <see cref="SkyStyle.Default"/>. A persisted "Cellar" still parses and lands on the current
/// cellar atmosphere. One hole remains: <c>Enum.Parse</c> accepts NUMERIC strings ("3" from a
/// hand-edited cfg parses into the undefined value <c>(SkyStyle)3</c>), so
/// <see cref="SkyAlternative.Tick"/> treats every value that is not a defined non-Default
/// member as Default instead of trusting the range.
/// </summary>
internal enum SkyStyle
{
    /// <summary>The game's own scenario sky (GH_SkySphere), exactly today's behaviour.</summary>
    Default = 0,

    /// <summary>A candle-lit cellar built from the mod's own bundle content (Env_Cellar:
    /// a photoscanned stone room under 'RoomGeo', plus star dome and dust motes).</summary>
    Cellar = 1,

    /// <summary>A moonlit swamp night from the mod's own bundle content (Env_Swamp: a
    /// photoscanned marsh clearing under 'RoomGeo', plus star dome, shooting stars,
    /// fireflies and ground fog).</summary>
    SwampNight = 2,

    /// <summary>
    /// NO surroundings at all — just black (user, 2026-08-13, verbatim: "Ich möchte auch 'Aus'
    /// bzw. 'Schwarz' in dem Dropdown zur Auswahl haben, dass jegliche Umgebung deaktiviert OHNE
    /// die mixed reality änderungen zusätzlich zu aktivieren."). The game's sky sphere is hidden
    /// exactly the way the two environments hide it, and NOTHING is put in its place: no prefab
    /// is loaded, no GameObject is instantiated, no particle system exists, no far-plane budget
    /// is claimed. That the result is BLACK is not an assumption — it is the ModBuild-129
    /// hardware finding quoted in <c>unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironments.cs</c>:
    /// "with the game's sky sphere hidden, everything above the generated room was pure black",
    /// which is why the cellar had to grow a star dome in the first place. Here that void IS the
    /// feature.
    ///
    /// DELIBERATELY NOT MIXED REALITY: MR is its own dial with its own precedence (see the class
    /// doc's MR PRECEDENCE note) and it additionally key-colours the camera clear and sweeps the
    /// game's sky meshes for the chroma key. This value touches none of that — it is one more
    /// ENVIRONMENT choice that happens to consist of nothing.
    /// </summary>
    OffBlack = 3,
}

/// <summary>
/// SKY ALTERNATIVES — replace the game's scenario sky with an atmospheric 3D surrounding
/// around the play space, built from the MOD'S OWN bundle content.
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
/// THE ANCHOR RULING (user, ModBuild 132 hardware round, verbatim — it decides EVERYTHING
/// below about where the room lives): "Es ist wieder passiert, dass ich durch zoomen und
/// verschieben im raum das Spielbrett dazu hatten plötzlich unter der map zu sein. Das darf
/// nicht passieren. Von der Position im Raum im Verhältnis zum Raum drumrum, MUSS es fix an
/// der Stelle bleiben. Es darf sich nur drehen und kleine bzw. größer werden wenn man zoomed."
///
/// Read precisely, that is an INVARIANT ON A RELATIVE POSE: the board's pose relative to the
/// room never changes. Zoom may make the pair (board + room) appear larger or smaller
/// together; a turn may rotate the pair. Nothing else. Both objects are therefore fixed in
/// WORLD space, because every locomotion and zoom in this mod writes the RIG (proof below) —
/// two world-fixed objects have a relative pose that is constant by construction, and no
/// per-frame code can break it.
///
/// THE PROPORTIONS RULING (user, ModBuild 133 hardware round, verbatim — it decides the two
/// numbers below and NOTHING about the invariance above, which he explicitly kept):
/// "Das Board ist nun fester Bestandteil der Umgebung und kann innerhalb der Map nicht mehr
/// kleiner gezogen werden. Dadurch ist zB bei der Sumpflandschaft die Bäume der Umgebung IN
/// dem Level integriert das soll nicht sein. Es soll in der Mitte schweben in einer
/// angemessenen Größe, so das es immer noch ein Spielfeld ist in der Umgebung drum rum (wie
/// Spielfiguren) nicht ansatzweise die Größe von der Umgebung."
///
/// The target picture: a SMALL game board FLOATING in the middle of a MUCH larger place, the
/// way a tabletop with figures sits in the middle of a room. Never anything close to the
/// environment's own size, and never planted into the environment's ground with the forest's
/// trees standing inside the play field.
///
/// ROOT CAUSE OF WHAT HE SAW — two independent defects, both readable in his ModBuild 133 log
/// (.planning/debug/LogOutput.log):
///  1. WRONG NORMALIZATION BASIS. Line 537 (swamp): "board 30.95 world units across over 199
///     hex tile(s) ... room = 3.0x that = 92.86 world units ... scale 1.542 from an authored
///     60.2 m". Those 60.2 m are the prefab's TOTAL renderer extent — it includes the deep
///     tree bands out to ~28.5 m from the centre. Scaling the TOTAL to three board widths
///     leaves the usable CLEARING far SMALLER than the board, so the tree ring necessarily
///     ends up standing inside the play field. Line 2485 (cellar): "scale 7.311 from an
///     authored 12.7 m" — the cellar only looked sane because its total extent happens to be
///     its interior, so the accident hid the defect for one of two styles.
///  2. RATIO FAR TOO SMALL AND THE BOARD PLANTED. 3.0x is not "a board in a room", and the
///     room floor sat exactly AT the board underside ("floor at the board underside y -0.05"
///     in both lines), so the board was embedded in the ground instead of floating above it.
///
/// WHY THE PREVIOUS MODEL PRODUCED HIS BUG (root cause, ModBuild 132). Until now BOTH the
/// room and the sky rode ONE frame that was world-anchored but tracked the rig scale
/// (<see cref="NotifyRigScaled"/>), so the frame kept a constant PERCEIVED size (~11 m). The
/// board is a plain world object at game scale. Zooming changes the rig scale ⇒ the board's
/// perceived size changes while the room's does not ⇒ the board grows and shrinks RELATIVE to
/// the room, and far enough out it pokes through the room floor ("plötzlich unter der map").
/// The world-grab drag moves the rig ⇒ the player AND the board move relative to the room.
/// Both halves of his report fall straight out of that model, so the model is wrong for the
/// room and is removed here.
///
/// WHY PERCEIVED-CONSTANT IS STILL RIGHT FOR THE SKY. A sky is defined by NOT having a
/// relation to the board: it must read as infinitely far away at every zoom. A world-fixed
/// dome would grow into the room when you zoom in and shrink to a marble when you zoom out.
/// So the two branches get two different rules — that is the whole design (ANCHORING below).
///
/// HISTORY: game-asset room generation (ModBuilds 127–131 — instantiating the game's own
/// scenario maps, dressing them through the live Apparance engine and seating them life-size
/// around the player) was removed by user ruling 2026-08-13 — see
/// <c>.planning/game-env-postmortem.md</c> for everything those five hardware rounds learned.
/// Environments are custom bundle content again. NEVER re-seat an occupied room: re-seating
/// geometry the player stands IN reads as a sudden player teleport and visually displaces the
/// board (the ModBuild-131 finding — five such events in the final log ended that approach).
///
/// WHAT IT DOES
/// ------------
/// A dial (<c>[Sky] Style</c>: Default / Cellar / SwampNight / OffBlack, curated in
/// Grafik ▸ Darstellung) picks the surroundings. Default is the game's own sky, bit-identical to
/// today — <see cref="SkyBackdrop"/> keeps making it a non-occluding backdrop and this class does
/// nothing.
///
/// OFF (BLACK) — the fourth choice (user, 2026-08-13, verbatim): "Ich möchte auch 'Aus' bzw.
/// 'Schwarz' in dem Dropdown zur Auswahl haben, dass jegliche Umgebung deaktiviert OHNE die mixed
/// reality änderungen zusätzlich zu aktivieren." <see cref="SkyStyle.OffBlack"/> takes step 1
/// below (hide the sphere) and NOTHING ELSE: step 2 is never reached, so no bundle asset is
/// loaded, no GameObject is instantiated, no particle system exists, the board is never measured,
/// the room probe never runs and <see cref="MinFarWorldUnits"/> stays 0 — a style that shows
/// nothing must not inflate the far plane. It is an ENVIRONMENT choice, not an MR switch: mixed
/// reality keeps its own dial, its own key-colour clear and its own precedence (MR PRECEDENCE
/// below), and none of that is touched from here. Its settled state is documented on hardware by
/// the one-shot <c>SKY IDLE</c> log line.
///
/// SCENARIO-ONLY SCOPE (second ruling): everything below happens exclusively while an
/// actual scenario board exists (<see cref="Events.VRModeStateMachine.ScenarioBoardExists"/> —
/// the Choreographer-alive signal the rig/WorldUI/mode machine already read; deliberately not
/// the save-state phase, which flips during loading/travel before any board exists). In the
/// menu and on the world map: nothing, ever — the game's default look. A non-Default choice in
/// a scenario:
///
///  1. HIDES the game's sky sphere (<c>GH_SkySphere</c>) via <c>renderer.enabled = false</c>.
///     That is safe for PURE hiding — <see cref="SkyBackdrop"/>'s class doc documents the trap
///     precisely: suppressing a renderer only bites when a CommandBuffer is supposed to REDRAW
///     it, which nothing does here. The sphere is found through <see cref="SkyBackdrop.FindSky"/>
///     (one shared set of name/shader hints), recorded, and re-enabled on restore. Hidden for
///     ALL THREE non-Default styles: SwampNight's star dome must own the sky, the cellar mood
///     wants darkness, and OffBlack is nothing BUT this step.
///  2. SPAWNS the environment prefab from the mod's asset bundle and SPLITS its children over
///     two roots by NODE NAME (the content lane authors against that fixed contract — see
///     <c>unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironmentRooms.cs</c>, which states
///     the same contract from its side): <c>Env_Cellar</c> = StarDome + DustMotes + a disabled
///     'GlowTemplate' child + a 'RoomGeo' stone room; <c>Env_Swamp</c> = StarDome +
///     ShootingStars + GroundFog + GroundFogFar + two Fireflies swarms + a 'RoomGeo' marsh
///     clearing. Self-lit, self-animating (Shuriken and shader time only, no scripts),
///     authored in real meters with the room floor at local y = 0.
///
/// The game's own scenario diorama/table stays untouched and visible — the environment
/// surrounds it. Everything lives on the MOD LAYER (only the rig head camera renders it; game
/// cameras and the FlatScreen composites never see it) and has every collider stripped —
/// non-interactive by ruling, it must never catch a laser/poke ray.
///
/// ANCHORING — TWO branches, two different rules
/// ---------------------------------------------
/// ROOM BRANCH (<see cref="RoomBoundShellChildren"/>: 'RoomGeo' and the floor-bound FX
/// 'GroundFog', 'GroundFogFar', 'Fireflies'): BOARD-ANCHORED, WORLD-FIXED, NEVER RE-SEATED.
/// Its transform is derived ONCE from the BOARD — not from the player, not from the rig:
///  - SCALE, NORMALIZED ON THE PLAY SPACE (defect 1 above). What has to be sized against the
///    board is the USABLE OPEN AREA — the forest clearing, the cellar interior — not the
///    prefab's total renderer extent, which for the swamp is mostly tree bands that must land
///    far OUTSIDE the board. The prefab therefore carries a marker: an empty child of the
///    prefab root named exactly <see cref="PlaySpaceMarkerName"/>, whose <c>localScale.x</c>
///    is the authored DIAMETER of that open area in authored meters (a contract with the
///    content lane, which authors it in
///    <c>unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironmentRooms.cs</c>). It has no
///    renderer, is read and then DESTROYED at spawn, and therefore never reaches the
///    name splitter, the layer pass, the collider strip or the particle pass — it is a
///    measurement, not content. Then
///        roomScale = (<see cref="PlaySpaceToBoardRatio"/> × boardWorldExtent) ÷ authoredPlayExtent
///    so the clearing/interior is <see cref="PlaySpaceToBoardRatio"/> board widths across and
///    everything the content lane authored beyond it — trees, walls — lands proportionally
///    further out. FALLBACK: a bundle without the marker (an older bundle, or a
///    plugin/bundle mismatch) falls back to the old total-renderer measurement
///    (<see cref="MeasureAuthoredRoomExtent"/>) and the placement log NAMES the basis it
///    used, so the degradation is visible in the very next log instead of silent.
///  - POSITION: the board's horizontal center; vertically the room floor (frame y = 0, the
///    authored floor plane) sits <see cref="FloatGapToBoardRatio"/> × the board's world extent
///    BELOW the board's underside, so the board FLOATS above the forest floor / cellar floor
///    like a tabletop diorama instead of being planted in the ground (defect 2 above). The
///    underside itself is still derived exactly as before — the lowest of the hex renderers
///    and the scenario's room-chunk volumes — and remains the reference plane the gap is
///    measured DOWN from. The gap is proportional to the BOARD, never an authored-meter
///    constant: a metre value would be a fixed WORLD distance and would therefore change its
///    relation to the board the moment the zoom changed the board's perceived size, which is
///    exactly the class of bug the invariance ruling exists to kill. Proportional means the
///    picture "board hovering that far above the ground" is identical at every zoom.
///  - ROTATION: the yaw of the BOARD's own world transform (a constant). Deliberately NOT the
///    head gaze: a gaze-derived yaw is player-dependent, and a player-dependent room pose is
///    by definition not invariant relative to the board.
/// The placement log line prints the gap BOTH in world units and as its perceived value at
/// the live rig scale, plus the player's own real floor height relative to the new room floor
/// — the three numbers the next hardware round needs to judge whether he stands on the ground
/// or above it, and to tune the two ratios.
/// After that placement there are ZERO per-frame writes, NO rig-scale tracking and NO re-seat
/// of any kind — not on <see cref="VRRigDriver.RigPoseVersion"/>, not on zoom, not ever
/// (ModBuild-131 ruling: re-seating a room the player stands in IS a teleport). The only
/// lifecycle events are style change / scenario end / MR on / VR stop → despawn, and a fresh
/// spawn. If the board's own world pose ever changed, the room would have to FOLLOW it
/// rigidly; it never does today (see MEASURING THE BOARD).
///
/// SKY BRANCH ('StarDome' and every other/unknown shell child): unchanged from ModBuild 128 —
/// world-anchored, rig-scale tracked, so it reads PERCEIVED-CONSTANT: at rig scale S its world
/// size is (authored meters × S) and its perceived size is the authored meters, always. ZOOM
/// ALGEBRA: the rig scales about a PIVOT (WorldGrab keeps the world point under the hands
/// glued, <c>Comfort.SetScaleMultiplier</c> keeps the head still), so a merely world-fixed sky
/// would keep its real size but DRIFT. Both scale writers report through
/// <see cref="NotifyRigScaled"/> (pivot, before, after) and the sky mirrors it:
/// <c>pos = pivot + (pos − pivot) · (after/before)</c>, <c>scale = after</c>. Proof this is
/// exact: with the rig mapping world = P + R·(s·t), the perceived (tracking-space) pose of the
/// frame is (1/s)·R⁻¹·(pos − P). The scale writer keeps the pivot glued to a tracking point m:
/// P = pivot − R·(s·m). Substituting both updates, the perceived pose after the write equals
/// the pose a pure locomotion write (same P, R change, scale untouched) would produce — the
/// scale component is bit-cancelled, the dome neither grows nor drifts, while drag/turn
/// components of the same gesture still pass through as movement. The sky keeps its
/// RigPoseVersion re-seat (RE-SEAT RULE below).
///
/// THE ACCEPTED CONSEQUENCE (it follows from the ruling and is not a defect): zoomed far out
/// the whole place reads as a model standing in front of you, because the board reads as a
/// model too and the two keep their proportion; zoomed in you stand inside it. That is
/// precisely "nur kleiner bzw. größer werden wenn man zoomed".
///
/// MEASURING THE BOARD — and why ModBuild 130's version of this same design failed
/// -------------------------------------------------------------------------------
/// 130 already tried "room = 2.75 × the board extent, floor at the board underside" and
/// shipped a SEVEN-CENTIMETRE room next to a normal board (his log: room 5.8 world units at
/// rig scale 85 ⇒ 5.8/85 ≈ 0.068 m). The design was fine; the MEASUREMENT was wrong. It read
/// <c>ProceduralScenario.MapTiles</c> and encapsulated each chunk's
/// <c>ProceduralMapTile.BoxCollider.bounds</c> — that is the procedural generator's ROOM-CHUNK
/// bookkeeping, not the visible diorama, and a DISABLED collider reports a zero-size bounds at
/// the world origin, so the encapsulation collapsed to about one world unit. Nothing checked
/// the result, so a nonsense number went straight into a shipped build.
///
/// This class therefore measures the set the GAME itself calls the board, and checks it:
///  - SOURCE: <c>ObjectCacheService.GetTileBehaviors()</c> — the live hex tiles
///    (<c>TileBehaviour</c> registers on OnEnable / de-registers on OnDisable, decompiled
///    TileBehaviour:33-46), i.e. the currently revealed board. It is the same set the game's
///    own camera derives <c>CameraController.m_FocalBounds</c> from (decompiled
///    CameraController.InitCamera) and the same set <c>Rig/SpawnRing.TryBoardFootprint</c>
///    uses to seat multiplayer arrivals "at the table" — proven on hardware for many rounds.
///  - HORIZONTAL EXTENT, IN WORLD SPACE: min/max over the tiles' world positions, widened by
///    half a hex (<c>UnityGameEditorRuntime.s_TileSize.x</c>, the runtime hex width — the
///    footprint is measured from tile ORIGINS), exactly SpawnRing's math.
///  - UNDERSIDE: the lowest of (a) the <c>Renderer.bounds.min.y</c> under those tiles (renderer
///    bounds are already world-space AABBs; particle renderers are skipped, their bounds follow
///    live particles rather than geometry) and (b) the bottom of the scenario's ROOM-CHUNK
///    volumes that overlap the footprint (<see cref="LowestRoomChunkY"/>). (b) is usually the
///    decisive one and it is why the floor is not simply put at the hex plane: the map's own
///    floor ART belongs to the generated content under a <c>ProceduralMapTile</c>, not to the
///    hex tiles, so a room floor at the hex plane would be drawn OVER the dungeon floor and the
///    board would lose its own ground. The result is clamped to at most half a board extent
///    below the hex plane, so one pathological renderer can never drop the floor into the void.
///    Since the board FLOATS, this underside is no longer where the room floor goes — it is the
///    REFERENCE PLANE the float gap is measured down from, and it still has to be the true
///    bottom of the visible diorama or the board would appear to hang by a different amount in
///    every scenario.
///  - THE SANITY CHECK that 130 lacked: the PERCEIVED extent (world extent ÷ live rig scale)
///    must lie in [<see cref="MinPlausibleBoardMeters"/>, <see cref="MaxPlausibleBoardMeters"/>]
///    = 0.2–20 m. Outside that the placement is REFUSED, one warn is logged, and the probe
///    retries on the next cadence tick. A wrong measurement must never ship a miniature or a
///    giant again. Both numbers — world and perceived — go into the placement log line so the
///    next hardware log can be read without guessing.
///
/// NOTHING MOVES THE DIORAMA IN WORLD SPACE, so no follow code is needed (read from source,
/// ModBuild 130 round and re-verified here): <c>Rig/WorldGrab.cs</c> writes only
/// <c>rig.localScale</c> / <c>rig.rotation</c> / <c>rig.position</c> (lines 362, 401, 403);
/// <c>Rig/Flight.cs</c> writes only <c>rig.position += step</c> (line 213);
/// <c>Rig/SnapTurn.cs</c> writes only <c>rig.RotateAround(pivot, up, degrees)</c> (line 162);
/// <c>Comfort.SetScaleMultiplier</c> writes the rig scale; and the Demeo-style world tilt
/// pitches the TRACKING SPACE, with <c>VRRigDriver</c>'s own comment stating "no game-world
/// object ever moves". The board is a game-world object: it stands still and the player moves
/// around, above and through it.
///
/// BOARD NOT MEASURABLE YET (a scenario still loading has no tiles): the room branch is NOT
/// placed and NOT shown — a stand-in room would be a lie that then has to be re-seated, i.e. a
/// teleport. The sky branch alone carries the look until the first tick on which the board
/// measures plausibly; that write is a FIRST PLACEMENT, not a re-seat, and is logged as
/// "first placement (board became measurable)".
///
/// RE-SEAT RULE (SKY BRANCH ONLY): the sky is re-placed (fresh spawn pose) whenever
/// <see cref="VRRigDriver.RigPoseVersion"/> changes — rig (re)build, deliberate recenter
/// (B+Y chord), spawn-ring seat, menu recenter — and on NO OTHER TRIGGER, of any kind. Those
/// are exactly the "the player was teleported" events (snap turns and world grabs do NOT bump
/// it, by that counter's own contract); free movement never re-seats anything. The ROOM branch
/// has NO re-seat path at all, deliberately: it is pinned to the board, and the board does not
/// move.
///
/// RIG REBUILD / MID-REBUILD FRAMES: on a frame with no RigRoot the whole feature stands down
/// (environment despawned, sphere restored) — rebuilds are rare, logged events, and the next
/// tick under the new root respawns and re-measures. Both instances are DontDestroyOnLoad so a
/// scene unload can never fake-null them out from under a live rig.
///
/// PER-FRAME COST: ZERO transform writes while active and idle. The active steady-state tick
/// is: enum read, sphere-hidden check, instance/anchor null checks, ONE static int compare
/// (RigPoseVersion) and one scale compare (a defensive drift-heal on the sky that only ever
/// fires if a future rig-scale writer forgets to call <see cref="NotifyRigScaled"/>). While
/// the room is still unplaced there is additionally one frame-counter compare, and the board
/// measurement itself runs at most once per <see cref="ScanIntervalFrames"/> frames until it
/// succeeds. Transform writes happen only inside a pinch-zoom (one position+scale write on the
/// sky per scaled frame), on the rare RigPoseVersion sky re-seats, and once for the room.
/// Default/MR-on: an enum read plus an idempotent early-out.
///
/// WHAT IS ALIVE WHILE NOTHING IS SHOWN (user, 2026-08-13: "prüfe nochmal dass wenn eine
/// Umgebung deaktiviert ist die deaktivierten assets nicht irgendwie perfomance fressen obwohl
/// sie nicht gezeichnet werden"). The answer is structural, not a tuning: the mod NEVER hides an
/// environment — it DESTROYS it. There is no disabled-renderer path and no
/// active-but-invisible path anywhere in this file, which is the only way to be sure no Shuriken
/// system keeps simulating (a ParticleSystem whose RENDERER is merely disabled still simulates
/// every frame; so does an active off-screen one whose culling mode falls back to
/// AlwaysSimulate). Concretely:
///  - Default / OffBlack / MR-on / after leaving a scenario: <see cref="Deactivate"/> has run
///    <c>Object.Destroy</c> on BOTH branch roots, so ZERO ParticleSystem, MeshRenderer or
///    Transform instances of this feature exist. Nothing to cull, nothing to simulate.
///  - The ONE inactive object that ever exists is the cellar's authored 'GlowTemplate' child
///    (<c>glow.SetActive(false)</c> in the content lane's BuildEnvironments.cs), and it is a
///    plain MeshFilter+MeshRenderer with no ParticleSystem and no script: an inactive GameObject
///    is not rendered, not culled and not updated by Unity, so it costs its memory and nothing
///    else. It only exists at all while the cellar IS shown.
///  - The room branch is <c>SetActive(false)</c> for the window between spawn and the board
///    becoming measurable. Its four Shuriken systems (GroundFog, GroundFogFar, 2× Fireflies)
///    are on an INACTIVE root and therefore do not simulate; that is also why
///    <see cref="KickRoomParticles"/> exists — <c>playOnAwake</c> never fired for them, so they
///    have to be started when the placement makes the root active.
///  - No environment asset is ever <c>Resources.UnloadAsset</c>-able waste either: the cached
///    <see cref="Prefabs"/> entries are references INTO the mod's own always-loaded bundle
///    (ASSETS below), i.e. memory that the bundle holds regardless. A prefab asset is not in a
///    scene; its ParticleSystems never tick.
///  - Per-frame mod code while nothing is shown: Default and MR-on cost one enum read plus
///    <see cref="Deactivate"/>'s idempotent early-out. OffBlack costs that read, the
///    ScenarioBoardExists compare (one static Unity-null check), one bool latch compare and one
///    <c>renderer.enabled</c> read to keep the sphere down. No probe, no heal, no re-seat, no
///    scan: the sphere rescan is gated on <c>_hiddenSphere == null</c> and stops the moment the
///    sphere is acquired, and the board-measure probe lives behind
///    <see cref="EnsureEnvironment"/>, which OffBlack never reaches.
///  - The board-measure probe's retry IS unbounded while an environment is shown and the board
///    stays unmeasurable, by design (giving up would leave the room hidden forever). It is
///    bounded in cost, not in count: at most once per <see cref="ScanIntervalFrames"/> frames,
///    over two REGISTRIES (ObjectCacheService's tile set, SceneRegistry's map tiles) and never a
///    heap sweep, and it stops permanently on the first success.
///
/// PARTICLES: prefab systems auto-play (playOnAwake) and the sky instance is always active, so
/// no kick is strictly needed — a defensive <c>Play()</c> runs anyway after instantiate, and
/// the room branch is kicked when its first placement makes it visible. Two module
/// normalisations make Shuriken honour the branch roots (the content lane authors at scale 1
/// in an editor scene and cannot know the instance runs at diorama scale):
/// <c>scalingMode = Hierarchy</c> (the established pattern — see
/// <c>Net.RemoteControlBoard</c>'s pile FX) so sizes/speeds/shapes follow the root scale, and
/// World simulation space is switched to Local so in-flight particles ride their root instead
/// of smearing behind it when the sky's zoom scale-follow moves it (the room root never moves
/// after placement; Local is kept there too, so both branches behave identically and the look
/// does not depend on which branch a node landed on).
///
/// FAR PLANE: the far-plane floor must cover BOTH branches. The sky is real-size, so at rig
/// scale S its farthest geometry sits up to (<see cref="EnvMinFarMeters"/> × S) world units
/// from its origin; the room is world-fixed and can out-distance the dome when you zoom in, so
/// its own world extent counts too. NOTE, since the play-space normalization split the two
/// apart: the room's far-plane budget is the placed size of the room's TOTAL geometry
/// (authored total extent × roomScale), NOT the play-space size — for the swamp the tree
/// bands now reach several times further out than the clearing, and budgeting the clearing
/// would clip them away. <see cref="MinFarWorldUnits"/> hands
/// <c>VRRigDriver.TickClipPlanes</c> the larger of the two budgets, each plus the head's
/// distance to that branch's origin (the player can fly away from either) — 0 when idle, still
/// capped by the depth-precision far/near ratio. OffBlack is IDLE for this purpose: it never
/// sets <c>_active</c>, so <see cref="MinFarWorldUnits"/> returns 0 and the clip planes keep the
/// game's own values. A style that shows nothing must never widen the depth range.
///
/// MR PRECEDENCE (the user's rule: MR ON ⇒ the sky is ALWAYS off): <see cref="MixedReality.Tick"/>
/// calls <see cref="StandDown"/> FIRST on its MR-on path — the environment despawns and the
/// game sphere is re-enabled so MR's own <c>HideSkyGeometry</c> sweep records and disables a
/// clean renderer for the chroma key, whatever the dial says. When MR turns off the dial's
/// choice re-applies on the next tick. On the MR-off path this ticks BEFORE SkyBackdrop, and
/// while an environment is shown SkyBackdrop stands down through the same parameter MR uses
/// (a hidden sphere needs no non-occluding treatment).
///
/// ASSETS: LAZY by design — nothing loads until a style is first selected in a scenario. The
/// prefabs come from the mod bundle via the established probe pattern
/// (<c>AssetBundle.GetAllLoadedAssetBundles()</c> + <c>LoadAsset</c>); a loaded prefab
/// reference is KEPT for the session (it is a reference into the loaded bundle, not a copy).
/// Missing prefab (older bundle) = one-shot warn, the game's own sky stays fully in place.
/// OffBlack owns NO asset: its <see cref="PrefabBundlePaths"/> slot is null and
/// <see cref="EnsurePrefab"/> is never called for it, so selecting it can never be what pulls
/// an environment into memory — that is the "load NOTHING" half of the user's request, and the
/// SKY IDLE line reports which prefabs an earlier selection had already cached.
///
/// REJECTED ALTERNATIVES (each cost at least one hardware round — do not retry them):
///  - Rig-child room (125): the room rides every locomotion write. Rejected by the user,
///    "Weiterhin möchte ich mich auch in den umgebungen frei bewegen und drehen können".
///  - Perceived-constant room (126–129, 132): the board drifts relative to the room under zoom
///    and eventually sits under the map. That is the report this file answers.
///  - Board-relative room WITHOUT a measurement check (130): a 7 cm miniature. Same design as
///    here; the fix is the source of the measurement and the sanity check, not the design.
///  - Life-size room with a re-seat when the zoom settles (131): the re-seat reads as a player
///    teleport and displaces the board. Five events in his final log ended the approach.
///  - Head-gaze yaw for the room: player-dependent, so the room pose would depend on where you
///    happened to look — the opposite of the invariance the ruling demands.
///  - Normalizing on the prefab's TOTAL renderer extent (133): for the swamp that is 60.2 m of
///    which the clearing is a fraction, so three board widths of TOTAL left the clearing
///    smaller than the board and the trees stood in the play field. That is his ModBuild-133
///    report. The basis, not the ratio alone, was wrong.
///  - Hard-coding the play-space diameter per style in this file: it would have to be re-tuned
///    by a code change every time the content lane re-authors a room, and it would silently go
///    stale against a newer bundle. The marker travels WITH the art; the fallback plus the
///    logged basis makes a mismatch visible instead of silent.
///  - A float gap in authored METERS: a fixed world distance, so the board's height above the
///    floor would change relative to the board itself under zoom. Proportional-to-the-board is
///    the only formulation that is invariant, which is what the anchor ruling demands.
///  - Implementing OffBlack as "turn mixed reality on": explicitly refused by the user ("OHNE
///    die mixed reality änderungen zusätzlich zu aktivieren"). MR additionally repaints the
///    camera clear in the key colour and sweeps every sky MESH for the chroma key; he wants
///    black, not a chroma key.
///  - Implementing OffBlack by spawning an all-black sphere/box: geometry that has to be sized,
///    anchored, layered, far-planed and re-seated — all the machinery this file exists to get
///    right — in order to render the same pixels the empty camera clear already produces. The
///    ModBuild-129 finding (a hidden sphere leaves pure black) makes the geometry pointless.
///  - Making OffBlack set <c>_active</c> so it shares the on/off log: it would hand
///    <see cref="MinFarWorldUnits"/> a 100 m × rig-scale far-plane floor for a scene with
///    nothing in it, and would make <see cref="NotifyRigScaled"/> chase a null sky.
///  - Shrinking the BOARD instead of growing the room, to get "nicht ansatzweise die Größe von
///    der Umgebung": the board is the game's own object and the mod does not resize it — and
///    it would break every board-relative system at once (spawn ring, control board, grabs).
///    The same proportion is achieved by sizing the room, which the mod does own.
///
/// MULTIPLAYER: local presentation only — nothing about the environment is on the wire. [Sky]
/// is not a board section, so the wire-coverage checker does not demand an exemption.
/// </summary>
internal static class SkyAlternative
{
    /// <summary>The environment choice. Bound by <see cref="BindConfig"/> into the RIG module
    /// file (<c>dev.gloomhavenvr.rig.cfg</c>, section [Sky]) so the config catalog's force-bind
    /// of <see cref="Rig.RenderQuality"/> surfaces it, and module "rig" files it under the
    /// Visual topic — beside the other look-of-the-picture dials.</summary>
    internal static ConfigEntry<SkyStyle> Style = null!;

    private static bool _bound;

    /// <summary>Frames between sphere re-scans while a non-Default style is active (the sphere
    /// can generate late, and a scene change fake-nulls the acquired renderer), and the cadence
    /// of the board-measurement probe that performs the room's first placement. Same cadence as
    /// <see cref="SkyBackdrop"/> / MR's sky sweep.</summary>
    private const int ScanIntervalFrames = 60;

    /// <summary>Far-plane floor in REAL meters while an environment is active — must cover the
    /// farthest authored SKY geometry (the star dome). See the class doc's FAR PLANE note.</summary>
    private const float EnvMinFarMeters = 100f;

    /// <summary>
    /// How many BOARD widths across the room's usable PLAY SPACE is — the forest clearing, the
    /// cellar interior (class doc ANCHORING, and the PROPORTIONS RULING it serves). 4.5 was
    /// chosen from the band 3.5–6: at 4.5 the board occupies 22% of the clearing's width and
    /// about 5% of its area, so it reads unmistakably as a game board standing in a place — a
    /// tabletop with figures — with roughly 1.75 board widths of open ground on every side.
    /// Below 3.5 the place starts to crowd the board again, which is what ModBuild 133's 3.0
    /// on the wrong basis produced; above 6 the board is lost in a field and the room art no
    /// longer reads as a room. THIS IS THE KNOB HIS NEXT REPORT TUNES: "zu groß" moves it up,
    /// "zu klein / zu eng" moves it down, and nothing else in the placement has to change.
    /// Not a config dial on purpose: it is a look constant, and a [Sky] dial that changes
    /// board-relative geometry would owe the wire-coverage checker an answer.
    /// </summary>
    private const float PlaySpaceToBoardRatio = 4.5f;

    /// <summary>
    /// How far the room floor sits BELOW the board's underside, as a multiple of the board's
    /// world extent — the "floating" of the PROPORTIONS RULING (class doc ANCHORING). 0.75 was
    /// chosen from the band 0.5–1.0 against his own ModBuild 133 numbers: his board measured
    /// 30.95 world units, so the gap is 23.2 world units, and his log line 537 puts the rig
    /// root — the tracking floor, i.e. his real floor — at y -23.59 with the board underside at
    /// y -0.05, a drop of 23.54. The new floor therefore lands within a third of a world unit
    /// of the floor he physically stands on, i.e. he stands ON the forest ground while the
    /// board hovers at about chest height in front of him. Proportional to the BOARD and not
    /// in meters, so the picture is identical at every zoom (class doc, and the rejected
    /// alternatives). The placement log prints the achieved gap and his floor delta so the next
    /// round can retune this without guessing.
    /// </summary>
    private const float FloatGapToBoardRatio = 0.75f;

    /// <summary>
    /// Name of the empty marker child on the environment prefab's ROOT whose
    /// <c>localScale.x</c> is the authored DIAMETER, in authored meters, of the room's usable
    /// open area (class doc ANCHORING). A CONTRACT with the content lane — never rename it on
    /// only one side. Read once at spawn and then destroyed, so no other pass ever sees it.
    /// </summary>
    private const string PlaySpaceMarkerName = "PlaySpace";

    /// <summary>Plausibility window for the board's PERCEIVED horizontal extent, real meters
    /// (class doc MEASURING THE BOARD). A diorama is knee-high-table-sized when zoomed to a
    /// normal play distance and room-sized when you zoom into it; anything outside this window
    /// is a broken measurement, not a board, and must never be built on. 130 shipped a 7 cm
    /// room precisely because no such window existed.</summary>
    private const float MinPlausibleBoardMeters = 0.2f;
    private const float MaxPlausibleBoardMeters = 20f;

    /// <summary>Fallback authored room extent in meters when a prefab carries no room geometry
    /// at all (an older bundle predating 'RoomGeo'). The floor-bound FX are still authored in
    /// real meters around the origin, so scaling them as if the room were this wide keeps fog
    /// and fireflies alive instead of silently dropping them.</summary>
    private const float FallbackAuthoredRoomMeters = 10f;

    /// <summary>Shell child nodes that belong to the WORLD-FIXED, board-anchored ROOM branch —
    /// the room geometry itself and the floor-bound FX. Everything else (StarDome, DustMotes,
    /// ShootingStars, GlowTemplate, and any node a future content round adds) rides the
    /// perceived-constant SKY branch. The names are a CONTRACT with the content lane, which
    /// states the same contract from its side in
    /// <c>unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironmentRooms.cs</c> — never rename
    /// one on only one side. Two swamp nodes are both called 'Fireflies'; the splitter walks
    /// every child, so duplicates are fine.</summary>
    private static readonly string[] RoomBoundShellChildren =
    {
        "RoomGeo", "GroundFog", "GroundFogFar", "Fireflies",
    };

    /// <summary>Bundle paths of the environment prefabs, indexed by <see cref="SkyStyle"/>. A
    /// null slot means the style OWNS NO ASSET and must never touch the bundle — Default (the
    /// game's own sky) and OffBlack (nothing at all). <see cref="EnsurePrefab"/> reads the null
    /// rather than a length, so appending a further style cannot accidentally make one of these
    /// load something.</summary>
    private static readonly string?[] PrefabBundlePaths =
    {
        null,                                            // Default    — the game's own sky
        "Assets/Bundle/Environments/Env_Cellar.prefab",  // Cellar
        "Assets/Bundle/Environments/Env_Swamp.prefab",   // SwampNight
        null,                                            // OffBlack   — loads NOTHING, by ruling
    };

    // Lazily loaded bundle prefab references — kept for the session once found (doc above).
    private static readonly GameObject?[] Prefabs = new GameObject?[PrefabBundlePaths.Length];
    private static bool _missingWarned; // one-shot: bundle lacks the environment (older bundle)

    // The game sphere we hid (renderer.enabled = false) — re-enabled on restore. Unity fake-null
    // when its scene unloads; then simply forgotten (the scene took the state with it).
    private static Renderer? _hiddenSphere;
    private static int _scanNextFrame;

    // THE TWO BRANCH ROOTS (class doc ANCHORING) — both WORLD-anchored (DontDestroyOnLoad, no
    // parent). SKY: rig-scale tracked, perceived-constant. ROOM: pinned to the board, frozen.
    // Destroyed by Deactivate, never by a scene unload.
    private static GameObject? _skyGo;
    private static GameObject? _roomGo;
    private static SkyStyle _appliedStyle = SkyStyle.Default;

    /// <summary>The <see cref="VRRigDriver.RigPoseVersion"/> the current SKY placement was
    /// computed for — a mismatch means the player was (re)built/recentered/ring-seated and the
    /// sky re-seats around their new pose (class doc RE-SEAT RULE, sky only). Sentinel: never a
    /// live version.</summary>
    private static int _placedPoseVersion = int.MinValue;

    // ROOM branch state. _roomPlaced latches the ONE placement: while it is false the room root
    // is hidden and the probe retries on the scan cadence; once true nothing ever writes the
    // room transform again for the life of this activation.
    /// <summary>
    /// True once the SKY branch's yaw came from the BOARD (and not from the player's head).
    ///
    /// <para>USER REPORT (ModBuild 137 round, verbatim): "Mond und Lichtstrahlen sollen im
    /// Multiplayer (falls beide Spieler die selbe Umgebung ausgewählt haben) auch synchronisiert
    /// werden." — and before multiplayer can even be discussed the two had to agree on ONE
    /// machine, which they did not.</para>
    ///
    /// <para>ROOT CAUSE. Every consumer of the moon reads ONE authored constant
    /// (<c>EnvironmentsBuilder.MoonDir</c>, <c>Editor/BuildEnvironments.cs:77</c>, re-exported as
    /// <c>EnvRoomBuilder.MoonDir</c>): the moon sprite in <c>EnvStars.shader</c>, the star
    /// occlusion in <c>EnvStarPoints.shader</c>, both baked light rigs, the canopy tear, the shaft
    /// axes and the cellar's window beam. The asset is therefore self-consistent by construction.
    /// The RUNTIME then split the shell over two roots and gave them DIFFERENT yaws:
    /// <see cref="PlaceSky"/> rotated the sky branch (StarDome/StarField — the moon) by the
    /// player's HEAD yaw at spawn, while <see cref="TryPlaceRoom"/> rotated the room branch
    /// (RoomGeo — shafts, canopy tear, rim-lit trunks, window beam) by the BOARD yaw. In game the
    /// moon stood at <c>headYawAtSpawn + 40°</c> and the shafts at <c>boardYaw + 40°</c>; the error
    /// was exactly the difference, i.e. wherever the player happened to be looking on the frame the
    /// sky was seated. It also meant two players in one scenario saw the moon in two directions,
    /// because each derived it from his own head.</para>
    ///
    /// <para>THE FIX, and why it is this one. The sky takes the ROOM's yaw. The board hierarchy is
    /// identical on every client, so a board-derived yaw is the same number everywhere — which is
    /// why this single change also makes the moon agree ACROSS players, with no wire field at all
    /// (see the ENV SYNC table in <see cref="EnvClockSeconds"/>). The sky is normally seated BEFORE
    /// the board is measurable (the room waits for hex tiles, the sky does not wait for anything),
    /// so the head yaw survives as the FALLBACK for that window and this latch records that the
    /// debt is outstanding; <see cref="TryPlaceRoom"/> pays it the moment the room lands, by
    /// re-yawing the sky about its own origin.</para>
    ///
    /// <para>REJECTED: re-seating the SKY on every room placement unconditionally (a second
    /// transform write per activation for nothing, and it would have to run before the first frame
    /// the player can see); moving the moon into the room branch (it is a 5 km dome — parenting it
    /// to a board-scaled, board-positioned frame makes it a small object hanging over the table,
    /// and it would lose <see cref="NotifyRigScaled"/>'s perceived-constant guarantee); re-seating
    /// the ROOM to the sky (the ModBuild-131 ruling: re-seating a room the player stands in reads
    /// as an unprompted teleport).</para>
    /// </summary>
    private static bool _skyYawFromBoard;

    private static bool _roomPlaced;
    private static float _roomAuthoredExtent;      // prefab's TOTAL horizontal extent, meters (measured)
    private static float _roomAuthoredPlayExtent;  // the usable open area's authored diameter, meters
    private static bool _roomPlayExtentFromMarker; // true = from the 'PlaySpace' marker, false = fallback
    private static float _roomWorldExtent;         // placed TOTAL world size — the far-plane budget
    private static int _nextRoomProbeFrame;
    private static bool _implausibleWarned;     // one-shot warn for a refused measurement

    /// <summary>Relative scale drift (vs. the live rig scale) beyond which the defensive heal in
    /// <see cref="EnsureEnvironment"/> re-syncs the SKY scale about the head pivot. Only
    /// reachable if a rig-scale writer forgets <see cref="NotifyRigScaled"/> (class doc).</summary>
    private const float ScaleDriftTolerance = 0.001f;

    /// <summary>Throttle for the drift-heal log line (unscaled seconds) — the heal itself is
    /// exact, so repeats mean a writer keeps scaling without notifying, worth one line per
    /// interval rather than one per frame.</summary>
    private const float HealLogIntervalSeconds = 5f;
    private static float _nextHealLogTime;

    private static bool _active;             // non-Default environment currently shown
    private static bool _loggedActive;       // change-dedup for the on/off log

    // OFF (BLACK) state. Deliberately NOT _active: nothing is shown, so the far-plane budget and
    // the zoom scale-follow must both stay idle (class doc FAR PLANE / rejected alternatives).
    // _blackShown latches the settled state so the transition work (despawning whatever the
    // previous style had built) runs ONCE, and so Deactivate knows there is something to undo
    // even on a frame where the sphere was never found. _blackLogged is the SKY IDLE one-shot,
    // held back until the sphere is actually down so the line describes the settled picture.
    private static bool _blackShown;
    private static bool _blackLogged;

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
            "never in the menu; it is built from the mod's OWN bundle content, styled to " +
            "match the game's painterly look). FOUR choices. Default = the game's own " +
            "animated sky, exactly as before. Cellar = a candle-lit stone cellar. " +
            "SwampNight = a moonlit swamp clearing under a star dome with shooting stars, " +
            "ground fog and fireflies. OffBlack = NO surroundings at all: the game's sky " +
            "sphere is hidden and nothing is put in its place, so everything around the " +
            "table is plain black. OffBlack loads nothing from the mod's bundle, spawns no " +
            "object, runs no effect and does not widen the view distance — and it is NOT " +
            "mixed reality: MR stays its own separate setting with its own chroma key. " +
            "Cellar and SwampNight in a scenario hide the game's sky sphere and " +
            "build the environment as a FIXED PLACE AROUND THE BOARD, with the board as a " +
            "SMALL GAME BOARD FLOATING IN THE MIDDLE OF A MUCH LARGER PLACE — like a " +
            "tabletop with figures standing in a room, never anything close to the size of " +
            "the surroundings. The open area you stand in — the forest clearing, the cellar " +
            "interior — is several times as wide as the board, so everything the " +
            "environment is made of, trees and walls included, stays well outside the play " +
            "field; and the board hovers a board-proportional height above the ground " +
            "instead of being planted in it. THE BOARD NEVER MOVES INSIDE THE ROOM. Zooming " +
            "scales the whole scene — board and room together, keeping their proportion, so " +
            "far out the place reads as a model in front of you and zoomed in you stand " +
            "inside it, and the board floats exactly the same way at every zoom. The board " +
            "can no longer end up under the floor. Stick flight, turning, the world-grab " +
            "drag and physical walking all move you through the place; none of them ever " +
            "re-places it. Only the sky itself is re-placed around you, and only by the " +
            "recenter chord (B+Y) or a rig rebuild — re-selecting a style rebuilds " +
            "everything. It can never catch the laser (no colliders, mod layer only). " +
            "Applies live from the VR menu, takes effect when a scenario is running. MIXED " +
            "REALITY ALWAYS WINS: while MR is on, every sky and environment is off so the " +
            "chroma key can show your room; the choice re-applies when MR turns off. Values " +
            "from the old panorama builds (Night/Sunset) no longer exist and fall back to " +
            "Default. Local presentation only, never synced to peers.");
    }

    /// <summary>
    /// Far-plane floor for <c>VRRigDriver.TickClipPlanes</c> (class doc FAR PLANE): the larger
    /// of the SKY budget (<see cref="EnvMinFarMeters"/> × rig scale) and the ROOM budget (its
    /// fixed world extent), each plus the head's distance to that branch's origin — the player
    /// can fly away from either. 0 while idle (the caller's Max degenerates to its old value).
    /// </summary>
    internal static float MinFarWorldUnits(float rigScale)
    {
        if (!_active)
            return 0f;
        Camera? head = VRRigDriver.HeadCamera;

        float skyFloor = EnvMinFarMeters * rigScale;
        GameObject? sky = _skyGo;
        if (sky != null && head != null)
            skyFloor += Vector3.Distance(sky.transform.position, head.transform.position);

        float roomFloor = 0f;
        GameObject? room = _roomGo;
        if (_roomPlaced && room != null)
        {
            roomFloor = _roomWorldExtent;
            if (head != null)
                roomFloor += Vector3.Distance(room.transform.position, head.transform.position);
        }
        return Mathf.Max(skyFloor, roomFloor);
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
        // ELEMENT MOOD is NOT ticked here, and the near miss is worth recording: this looks like
        // the environment's per-frame entry point, but MixedReality.Tick only reaches it on its
        // MR-OFF branch (MixedReality.cs:684). The mood has to keep sensing while MR is ON — the
        // user asked for element effects in passthrough as well — so it is ticked one level up, at
        // the top of MixedReality.Tick, which is the only per-frame call that runs on BOTH
        // branches. See Core/ElementMood.cs, "MIXED REALITY KEEPS SENSING".
        //
        // THE HAUNT IS TICKED HERE, and the contrast with the line above is the whole reason both
        // comments exist. A mood is a NUMBER and has to reach the player under every presentation
        // including passthrough, so it rides the level above. A haunt is a SURFACE inside one of
        // two bundled room prefabs, so it exists exactly where those prefabs do — and this method
        // is only reached on the MR-OFF branch, which is precisely the gating it wants. It does its
        // own full gating (setting, session, scenario board, style) and publishes only numbers.
        // See Core/Haunt.cs.
        Haunt.Tick();

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
        if (style != SkyStyle.Cellar && style != SkyStyle.SwampNight && style != SkyStyle.OffBlack)
        {
            Deactivate();
            return false;
        }

        // SCENARIO-ONLY SCOPE (class doc, second ruling): outside a live scenario board the
        // feature stands down entirely — the menu keeps the game's default look, exactly like
        // the original surroundings. Leaving/ending a scenario deactivates on the next tick.
        // OffBlack keeps that scope too: he asked for an environment choice, and "no environment"
        // must behave like the other environments — the menu and the world map stay untouched.
        if (!Events.VRModeStateMachine.ScenarioBoardExists)
        {
            Deactivate();
            return false;
        }

        // OFF (BLACK) — the whole style, in full (class doc OFF (BLACK)). It is step 1 of the
        // pipeline and nothing else: hide the game's sky sphere and stop. No prefab is asked for,
        // no rig anchor is needed (nothing is placed relative to the player), no branch root is
        // built, no probe is armed and _active stays FALSE so the far plane keeps the game's own
        // value. Returning true stands SkyBackdrop down — a hidden sphere needs no non-occluding
        // treatment, which is exactly the same reason the two environments return true.
        if (style == SkyStyle.OffBlack)
        {
            if (!_blackShown)
            {
                // Coming from Cellar/SwampNight: DESTROY what that style built rather than hiding
                // it. This is the whole answer to "die deaktivierten assets fressen keine
                // Performance" — there is no disabled-but-alive path, so no Shuriken system can
                // survive the switch and keep simulating behind a style that shows nothing.
                DespawnEnvironment();
                _blackShown = true;
            }
            HideGameSphere();
            LogIdleOnce();
            return true;
        }

        // OffBlack -> Cellar/SwampNight goes straight on without passing through Deactivate (the
        // sphere stays hidden either way, so there is nothing to restore). Drop the black latches
        // here, or a later return to OffBlack would inherit a spent SKY IDLE one-shot and the
        // hardware log would be missing exactly the line the audit exists to produce.
        if (_blackShown)
        {
            _blackShown = false;
            _blackLogged = false;
        }

        if (!EnsurePrefab(style))
        {
            // Older bundle without the environment prefabs — leave the game's own sky fully in
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
        // THE ROOM'S KIND, before anything that reads it. One float compare per frame once settled;
        // see GhvrIndoorId for why the two rooms have to be distinguishable at all.
        ApplyIndoor(style);
        EnsureEnvironment(style, anchor);
        TickEnvClock(); // the shared-clock walk — one float compare once settled (EnvClockSeconds)

        // ENV SOUND — the environment HEARD. Ticked here, after TickEnvClock, and the order matters:
        // every sound it schedules (the drip landing, the rat crossing, an apparition's cue) is a
        // function of EnvClockSeconds, so it must run on the clock value for THIS frame rather than
        // the last one, or every cue would be systematically one frame stale.
        //
        // The four arguments are the four things it needs and NONE of them has an accessor, which is
        // deliberate: the branch roots are private with no getter (see ElementMood's class doc on why
        // that is a design fact rather than an oversight), so handing them in keeps the encapsulation
        // intact instead of opening the environment up to the whole mod. The room is passed only once
        // it is PLACED — before that it is hidden at an unresolved pose, and a spatialised sound at an
        // unresolved pose would come from the wrong corner of the room.
        //
        // Like the haunt above and unlike the element mood, this is attached to GEOMETRY: it is only
        // reached on the MR-OFF branch, which is exactly the gating it wants.
        EnvSound.Tick(_roomPlaced ? _roomGo : null, style, anchor.lossyScale.x);

        // HAUNT FIGURES — the apparitions that are real game monsters (Core/HauntFigures.cs).
        // Ticked HERE, immediately after the sound and for the same three reasons: it needs THIS
        // frame's shared clock (TickEnvClock ran above), it needs the room root, and neither the
        // root nor the applied style has an accessor — handing them in keeps that encapsulation
        // rather than opening the branches up to the whole mod. Like the sound and unlike the
        // element mood it is attached to GEOMETRY, so the MR-OFF-only gating this method already
        // provides is exactly the gating it wants. The room is passed only once PLACED: a monster
        // walking past a window whose pose is not resolved yet would walk through the wrong wall.
        HauntFigures.Tick(_roomPlaced ? _roomGo : null, style);

        if (!_active || !_loggedActive)
        {
            _active = true;
            _loggedActive = true;
            VRLog.Info("Core", $"Sky alternative ON — style {style} (scenario active): the game's sky " +
                               "sphere is hidden (pure renderer.enabled hiding; SkyBackdrop stands down). " +
                               "The ROOM is a fixed place around the board — world-anchored, never " +
                               "re-seated, its open play space several board widths across and its floor " +
                               "a board-proportional gap BELOW the board, so the board floats in the " +
                               "middle of it like a tabletop diorama — and the SKY is world-anchored " +
                               "but rig-scale tracked " +
                               "(perceived-constant, a distant sky at every zoom). MR overrides it off; " +
                               "leaving the scenario despawns it.");
        }
        return true;
    }

    /// <summary>
    /// MR-precedence stand-down, called on <see cref="MixedReality.Tick"/>'s MR-ON path BEFORE
    /// MR's own sky sweep runs: despawns the environment and RE-ENABLES the game sphere, so
    /// <c>HideSkyGeometry</c> records and disables a clean renderer for the chroma key. Cheap and
    /// idempotent (an early-out when nothing is applied); loaded prefab references stay cached.
    /// </summary>
    internal static void StandDown()
    {
        // ELEMENT MOOD DELIBERATELY DOES NOT GO DOWN HERE. It used to, and that was the ModBuild
        // 139 miss: this path is what MR calls (MixedReality.cs:692), and the user's requirement is
        // that the elements still reach the player in passthrough. The mood is ticked from
        // MixedReality.Tick on both branches and keeps publishing; the ROOM and the SKY stand down,
        // which is what the MR ruling is actually about. Teardown still zeroes the globals —
        // RestoreAll below.
        //
        // THE HAUNT DOES GO DOWN HERE, and that is the deliberate opposite of the paragraph above.
        // This path is what MR calls (MixedReality.cs:692), the apparitions are geometry in the
        // room that is being torn down, and the standing MR ruling is that the mod puts no
        // occluding surface over passthrough. A mood is a number; a face is not.
        Haunt.StandDown("the environment stood down (mixed reality, or the style changed)");

        // ENV SOUND GOES DOWN HERE TOO, for exactly the haunt's reason and not the mood's: its
        // sources are components ON the room's nodes, and with the room gone there is nothing left
        // for a sound to come from. An ambience playing over passthrough with no visible source
        // would also be precisely the disembodied stereo bed the user ruled out when he asked for
        // sounds that are "verortbar von seinen entsprechenden Quellen".
        EnvSound.StandDown("the environment stood down (mixed reality, or the style changed)");
        HauntFigures.StandDown("the environment stood down (mixed reality, or the style changed)");
        Deactivate();
    }

    /// <summary>Full teardown (VR stop / hot reload): stand down and drop the one-shot warn latch.
    /// The cached prefab references are kept — they are session-lifetime by design (class doc).</summary>
    internal static void RestoreAll()
    {
        // Same reason as StandDown: a teardown must leave nothing standing in a shader global.
        ElementMood.StandDown("the environment was torn down (VR stopped, or the rig was destroyed)");
        Haunt.StandDown("the environment was torn down (VR stopped, or the rig was destroyed)");
        // ReleaseAll, not StandDown: this is the FULL teardown, so the synthesized clips go too.
        // An ordinary stand-down keeps them (a clip nobody plays is inert, and re-synthesizing ~2 MB
        // of noise on every mixed-reality toggle would be pure waste) — but a rig that is gone must
        // leave nothing at all behind, audio buffers included.
        EnvSound.ReleaseAll("the environment was torn down (VR stopped, or the rig was destroyed)");
        HauntFigures.ReleaseAll("the environment was torn down (VR stopped, or the rig was destroyed)");
        Deactivate();
        _missingWarned = false;
        _scanNextFrame = 0;
    }

    /// <summary>
    /// THE ONE 'SKY IDLE' LINE (Task B, user 2026-08-13: "Nur zur sicherheit prüfen ... dass wenn
    /// eine Umgebung deaktiviert ist die deaktivierten assets nicht irgendwie perfomance fressen
    /// obwohl sie nicht gezeichnet werden"). Emitted ONCE per settled OffBlack state — held back
    /// until the game sphere is actually down, so the line describes the finished picture rather
    /// than a frame in the middle of acquiring it — and it names the three things the next
    /// hardware log has to be able to answer without re-reading this file: what is LOADED, what is
    /// INSTANTIATED and what still TICKS. The latch is cleared by <see cref="Deactivate"/>, so
    /// each fresh settle documents itself once instead of once per session.
    /// </summary>
    private static void LogIdleOnce()
    {
        if (_blackLogged || _hiddenSphere == null)
            return;
        _blackLogged = true;

        int cached = 0;
        for (int i = 0; i < Prefabs.Length; i++)
        {
            if (Prefabs[i] != null)
                cached++;
        }
        string loaded = cached == 0
            ? "nothing — no environment prefab has been loaded this session"
            : $"{cached} environment prefab reference/s cached by an earlier style selection. That " +
              "is MEMORY ONLY: a reference into the mod's already-loaded bundle, not a copy, and a " +
              "prefab asset lives outside every scene, so its particle systems never simulate";

        VRLog.Info("Core", $"SKY IDLE — [Sky] Style = OffBlack: the environment feature is showing " +
                           $"NOTHING and this line is everything it still owns. LOADED: {loaded}. " +
                           $"INSTANTIATED: sky root {(_skyGo == null ? "none" : "LIVE — UNEXPECTED")}, " +
                           $"room root {(_roomGo == null ? "none" : "LIVE — UNEXPECTED")}; both branches " +
                           "are DESTROYED on this path, never disabled and never hidden, so ZERO " +
                           "ParticleSystem, MeshRenderer or Transform instances of the cellar/swamp art " +
                           "exist — nothing can simulate off-camera or behind a disabled renderer. " +
                           $"TICKS: this class does one enum read, one scenario check, one latch compare " +
                           $"and one renderer.enabled read per frame to hold the game's sky sphere " +
                           $"'{_hiddenSphere.gameObject.name}' down. No board-measure probe, no scale-drift " +
                           "heal, no sphere rescan, no transform write; MinFarWorldUnits returns 0 so the " +
                           "clip planes keep the game's own far value. Mixed reality is untouched and " +
                           "stays on its own dial.");
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
        // A style with no path owns no asset (Default, OffBlack) — return without warning and,
        // above all, without probing a single bundle: "load NOTHING" is a promise of the OffBlack
        // ruling, and this is the line that keeps it even if a future caller reaches here.
        if (i <= 0 || i >= PrefabBundlePaths.Length || PrefabBundlePaths[i] == null)
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
    /// transform writes (class doc PER-FRAME COST): one static int compare re-seats the SKY
    /// after a rig rebuild/recenter/ring seat (the ONLY re-seat trigger, and only for the sky),
    /// one scale compare is the sky's defensive drift-heal, and one frame-counter compare drives
    /// the room's first-placement probe until it succeeds. Instantiates on first activation and
    /// on style switch.
    /// </summary>
    private static void EnsureEnvironment(SkyStyle style, Transform anchor)
    {
        if (_skyGo != null && _roomGo != null && _appliedStyle == style)
        {
            // SKY steady state (class doc RE-SEAT RULE): RigPoseVersion bumps only on rig
            // (re)build, deliberate recenter, ring seat and menu recenter — the "player was
            // teleported" events. Free locomotion (flight/turn/grab) never bumps it, so the
            // sky stays a fixed world place while the player moves through it. There is
            // deliberately NO other re-seat trigger (the ModBuild-131 finding: any other
            // re-seat reads as an unprompted player teleport).
            if (VRRigDriver.RigPoseVersion != _placedPoseVersion)
                PlaceSky(anchor, "rig pose changed (rebuild/recenter/ring seat)");

            // ROOM: never re-seated, ever. The only write is the ONE first placement, which
            // waits for a board that measures plausibly (class doc BOARD NOT MEASURABLE YET).
            if (!_roomPlaced && Time.frameCount >= _nextRoomProbeFrame)
            {
                _nextRoomProbeFrame = Time.frameCount + ScanIntervalFrames;
                TryPlaceRoom(anchor, "first placement (board became measurable)");
            }

            HealScaleDrift(_skyGo.transform, anchor); // sky only — the room has no scale invariant
            return;
        }

        if (_skyGo != null || _roomGo != null)
        {
            // Style switch (or a half-built pair) — both branches go.
            if (_skyGo != null) Object.Destroy(_skyGo);
            if (_roomGo != null) Object.Destroy(_roomGo);
            _skyGo = null;
            _roomGo = null;
            ResetRoomState();
        }

        // THE TWO BRANCH ROOTS (class doc ANCHORING): empty WORLD-anchored roots — no parent,
        // so locomotion moves the rig relative to the world and therefore through them;
        // DontDestroyOnLoad so a scene unload can never fake-null a live branch (teardown is
        // always ours, Deactivate).
        GameObject prefab = Prefabs[(int)style]!;
        _skyGo = new GameObject("GloomhavenVR.SkyAlternative.Sky." + style);
        _roomGo = new GameObject("GloomhavenVR.SkyAlternative.Room." + style);
        Object.DontDestroyOnLoad(_skyGo);
        Object.DontDestroyOnLoad(_roomGo);
        // The room root starts at IDENTITY on purpose: the authored extent is measured off it
        // below, and at identity the children's world bounds ARE their authored local bounds.
        _roomGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _roomGo.transform.localScale = Vector3.one;
        PlaceSky(anchor, "spawn");

        GameObject shell = Object.Instantiate(prefab, _skyGo.transform, false);
        shell.name = prefab.name; // authored disabled children (Cellar's 'GlowTemplate') stay disabled

        // PLAY-SPACE MARKER FIRST, before anything else walks the shell (class doc ANCHORING):
        // it is a measurement, not content, so it is read and removed here — the name splitter,
        // the layer pass, the collider strip and the particle pass all run on what is left and
        // can never see it, and it can never fall through the name router onto the sky branch.
        float markedPlayExtent = TakePlaySpaceMarker(shell);

        // SPLIT BY NODE NAME (class doc ANCHORING): the room geometry and the floor-bound FX
        // move to the board-anchored, world-fixed room branch keeping their authored local pose
        // (they are authored around the origin with the floor at y = 0 — the room frame's origin
        // IS its floor); everything else (dome, motes, shooting stars, future nodes) stays
        // perceived-constant on the sky.
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

        // The prefab's own TOTAL room size, measured while the room root still stands at
        // identity. It no longer drives the scale (defect 1 in the class doc) — it is the
        // far-plane budget and the fallback basis.
        _roomAuthoredExtent = MeasureAuthoredRoomExtent(_roomGo);

        // THE NORMALIZATION BASIS (class doc ANCHORING). The marker wins; a bundle without one
        // degrades to the old total-renderer basis and SAYS SO, here and in the placement line,
        // so a plugin/bundle mismatch is one grep away instead of a silently wrong room.
        _roomPlayExtentFromMarker = markedPlayExtent > 0.01f;
        _roomAuthoredPlayExtent = _roomPlayExtentFromMarker ? markedPlayExtent : _roomAuthoredExtent;
        if (!_roomPlayExtentFromMarker)
        {
            VRLog.Warn("Core", $"Sky alternative: environment '{prefab.name}' carries no " +
                               $"'{PlaySpaceMarkerName}' marker on its root — a bundle older than the " +
                               "play-space contract, or a plugin/bundle mismatch. Falling back to the " +
                               $"TOTAL authored extent {_roomAuthoredExtent:F1} m as the play space, which " +
                               "is the ModBuild-133 behaviour: for a room whose art reaches far beyond its " +
                               "open area the board will be framed too tightly. Rebuild the bundle.");
        }

        VRLayers.Apply(_skyGo);  // mod layer, recursive — head camera only (gated on IsRunning)
        VRLayers.Apply(_roomGo);

        // NON-INTERACTIVE BY RULING ("Nicht interaktiv rein als Umgebung"): the contract says
        // the prefabs ship without colliders, but a stray one would silently eat laser/poke
        // rays across the whole environment — strip defensively, once, at spawn (both branches).
        int strippedColliders = StripColliders(_skyGo) + StripColliders(_roomGo);

        // Shuriken normalisation + defensive kick (class doc PARTICLES).
        int systemCount = NormaliseParticles(_skyGo) + NormaliseParticles(_roomGo);

        bool wasSwitch = _appliedStyle != SkyStyle.Default && _loggedActive;
        _appliedStyle = style;

        // FIRST PLACEMENT ATTEMPT. While the board is not measurable the room root stays HIDDEN
        // rather than standing somewhere provisional: a stand-in room would have to be re-seated
        // later, and re-seating a room the player stands in is the teleport the 131 round proved
        // unacceptable. The probe above retries on the scan cadence.
        bool placed = TryPlaceRoom(anchor, "spawn");
        if (!placed)
            _roomGo.SetActive(false);

        VRLog.Info("Core", $"Sky alternative: environment '{prefab.name}' spawned and SPLIT over the two " +
                           $"branches — {rehomed} board-anchored node(s) onto the world-fixed room frame, " +
                           $"the rest onto the rig-scale-tracked sky frame. Authored room extent " +
                           $"{_roomAuthoredExtent:F1} m total, play space {_roomAuthoredPlayExtent:F1} m " +
                           $"{(_roomPlayExtentFromMarker ? $"from the '{PlaySpaceMarkerName}' marker" : "from the total extent — NO marker, fallback basis")}" +
                           $". {systemCount} particle system(s) normalised " +
                           $"(Hierarchy scaling, local simulation space)" +
                           $"{(strippedColliders > 0 ? $", {strippedColliders} stray collider(s) stripped" : "")}. " +
                           $"{(placed ? "Room placed on the board." : "Room HIDDEN until the board measures — no stand-in is ever shown.")}" +
                           $"{(wasSwitch ? " (style switch)" : "")}");
    }

    /// <summary>Destroy every collider under a branch and report how many there were.</summary>
    private static int StripColliders(GameObject branch)
    {
        Collider[] colliders = branch.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            Object.Destroy(c);
        return colliders.Length;
    }

    /// <summary>
    /// Make Shuriken honour a branch root (class doc PARTICLES): <c>Hierarchy</c> scaling so
    /// sizes/speeds/shapes follow the root scale, and World simulation space switched to Local
    /// so in-flight particles ride the root instead of smearing behind it. Disabled template
    /// children are normalised but never kicked (templates, not FX).
    /// </summary>
    private static int NormaliseParticles(GameObject branch)
    {
        ParticleSystem[] systems = branch.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
            if (!ps.isPlaying && ps.gameObject.activeInHierarchy)
                ps.Play(withChildren: false);
        }
        return systems.Length;
    }

    /// <summary>
    /// Kick the room branch's particle systems the first time its placement makes it visible —
    /// a system whose root was inactive at instantiate time never ran <c>playOnAwake</c>.
    /// </summary>
    private static void KickRoomParticles(GameObject branch)
    {
        foreach (ParticleSystem ps in branch.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!ps.isPlaying && ps.gameObject.activeInHierarchy)
                ps.Play(withChildren: false);
        }
    }

    /// <summary>
    /// Read and REMOVE the play-space marker (class doc ANCHORING): the empty child named
    /// <see cref="PlaySpaceMarkerName"/> whose x scale is the authored DIAMETER, in authored
    /// meters, of the room's usable open area. Returns 0 when the prefab carries none — the
    /// caller then falls back to the total-renderer basis and logs that it did.
    ///
    /// <para>The value is taken as the marker's scale RELATIVE TO THE SHELL ROOT
    /// (lossy ÷ lossy) rather than as a raw <c>localScale</c>: the shell already hangs under
    /// the rig-scaled sky root at this point, and a future prefab could nest the marker one
    /// level deeper. For the contracted layout — a direct child of an unscaled prefab root —
    /// the two are identical, and the degenerate case falls back to the raw local scale.</para>
    ///
    /// <para>Destroying it is what keeps every other pass honest: the name splitter would
    /// otherwise route an unknown node onto the SKY branch, where a rig-scale-tracked empty
    /// would be harmless but would still show up in every future sweep as content. A
    /// measurement must not survive into the scene.</para>
    /// </summary>
    private static float TakePlaySpaceMarker(GameObject shell)
    {
        Transform root = shell.transform;
        Transform? marker = null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, PlaySpaceMarkerName, System.StringComparison.Ordinal))
            {
                marker = child;
                break;
            }
        }
        if (marker == null)
            return 0f;

        float shellScale = Mathf.Abs(root.lossyScale.x);
        float markerScale = Mathf.Abs(marker.lossyScale.x);
        float authored = shellScale > 1e-6f ? markerScale / shellScale : Mathf.Abs(marker.localScale.x);

        // Detach first, then destroy: Destroy is deferred to the end of the frame, and the
        // splitter/layer/collider/particle passes all run within THIS call.
        marker.SetParent(null, false);
        Object.Destroy(marker.gameObject);

        return float.IsNaN(authored) || float.IsInfinity(authored) ? 0f : authored;
    }

    /// <summary>
    /// The room prefab's OWN horizontal size in authored meters, measured from the mesh
    /// renderers now parented under <paramref name="room"/> while that root still stands at
    /// identity (so a world-space AABB is the authored AABB). Measured rather than hard-coded so
    /// the content lane can resize a room — or add a third style — without touching this file,
    /// and so both styles frame the board identically no matter how big their art is. Particle
    /// renderers are excluded implicitly (they are not MeshRenderers) because their bounds
    /// follow live particles rather than geometry. Returns
    /// <see cref="FallbackAuthoredRoomMeters"/> with one warn when a prefab carries no room
    /// geometry at all (an older bundle predating 'RoomGeo').
    /// </summary>
    private static float MeasureAuthoredRoomExtent(GameObject room)
    {
        MeshRenderer[] renderers = room.GetComponentsInChildren<MeshRenderer>();
        Bounds b = default;
        bool has = false;
        foreach (MeshRenderer r in renderers)
        {
            if (r == null)
                continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        float extent = has ? Mathf.Max(b.size.x, b.size.z) : 0f;
        if (extent > 0.01f)
            return extent;

        VRLog.Warn("Core", "Sky alternative: the environment prefab carries no room geometry " +
                           "under the board-anchored nodes — an older bundle without 'RoomGeo'? " +
                           $"Falling back to {FallbackAuthoredRoomMeters:F0} m of authored room " +
                           "size so the floor-bound FX still scale with the board.");
        return FallbackAuthoredRoomMeters;
    }

    /// <summary>
    /// Write the SKY branch's spawn pose (class doc ANCHORING, sky): origin = the tracked head's
    /// floor point (head rig-local position with y = 0, mapped through the rig: tracking is
    /// floor-origin, so that is the real floor under the player), yaw = head world forward
    /// projected to the horizon, scale = the live rig scale — <see cref="NotifyRigScaled"/>
    /// keeps the sky perceived-constant from here. Head not tracked yet (rig just built, first
    /// pose pending) → the rig's own origin/yaw stand in; the first-pose recenter bumps
    /// RigPoseVersion and this re-runs with the real head a frame later. THE ROOM IS NOT TOUCHED
    /// HERE — it is board-anchored and must never follow the player.
    /// </summary>
    private static void PlaceSky(Transform anchor, string why)
    {
        float rigScale = anchor.lossyScale.x;
        if (!(rigScale > 0f) || float.IsInfinity(rigScale))
            rigScale = 1f; // degenerate rig scale must not vanish/explode the environment

        Camera? head = VRRigDriver.HeadCamera;
        Vector3 floorPos;
        Quaternion yaw;
        bool tracked = head != null && head.transform.localPosition.sqrMagnitude > 1e-6f;
        floorPos = tracked
            ? anchor.TransformPoint(new Vector3(head!.transform.localPosition.x, 0f,
                                                head.transform.localPosition.z))
            : anchor.position;

        // THE YAW IS THE BOARD'S, NOT THE HEAD'S (see _skyYawFromBoard for the whole root cause).
        // The moon is a fixed authored direction inside this branch, and the shafts that the same
        // constant aims live in the ROOM branch, which is yawed by the board — so the ONLY yaw at
        // which moon and shafts can agree is the board's. The head yaw survives ONLY for the window
        // in which no board is measurable yet (which is the normal case at spawn: the room itself
        // is waiting for the same tiles), and TryPlaceRoom re-yaws this branch the moment it lands.
        string yawSource;
        if (_roomPlaced && _roomGo != null)
        {
            // A RE-SEAT AFTER THE ROOM STANDS (recenter, rig rebuild, ring seat). Take the yaw the
            // room is ACTUALLY wearing, not a fresh measurement: it is the same number by
            // construction and it cannot fail, whereas a measurement can (a teardown mid-read) —
            // and a failure here would drop the sky back onto the head yaw with no second chance,
            // because TryPlaceRoom never runs again for this activation.
            yaw = VRRigDriver.YawOnly(_roomGo.transform.rotation);
            _skyYawFromBoard = true;
            yawSource = "the PLACED ROOM's own yaw (exact, and immune to a failed re-measurement)";
        }
        else if (TryMeasureBoardYaw(out Quaternion boardYaw))
        {
            yaw = boardYaw;
            _skyYawFromBoard = true;
            yawSource = "BOARD yaw (agrees with the room branch, and with every other client)";
        }
        else if (tracked)
        {
            Vector3 fwd = head!.transform.forward;
            fwd.y = 0f; // world-horizon yaw — the sky stays upright in the world
            yaw = fwd.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(fwd)
                : VRRigDriver.YawOnly(anchor.rotation); // looking straight up/down: seat yaw
            _skyYawFromBoard = false;
            yawSource = "head yaw, PROVISIONAL — no board measurable yet; the room's placement re-yaws it";
        }
        else
        {
            yaw = VRRigDriver.YawOnly(anchor.rotation);
            _skyYawFromBoard = false;
            yawSource = "rig yaw, PROVISIONAL — no board and no tracked head yet; the room's placement re-yaws it";
        }

        if (_skyGo != null)
        {
            _skyGo.transform.SetPositionAndRotation(floorPos, yaw);
            _skyGo.transform.localScale = Vector3.one * rigScale;
        }

        _placedPoseVersion = VRRigDriver.RigPoseVersion;
        VRLog.Info("Core", $"SKY YAW: SKY placed ({why}) — origin {floorPos} scale " +
                           $"{rigScale:F2} (rig-tracked, perceived-constant), yaw {yaw.eulerAngles.y:F1}deg " +
                           $"from {yawSource}" +
                           $"; head is {(tracked ? "tracked" : "not tracked yet")}. " +
                           "The room branch is board-anchored and is NOT touched by this.");
    }

    /// <summary>
    /// The ROOM branch's ONE placement (class doc ANCHORING, room). Derives the pose purely
    /// from the board: scale so the room's PLAY SPACE spans <see cref="PlaySpaceToBoardRatio"/>
    /// × the board's world extent, position at the board's horizontal centre with the room
    /// floor <see cref="FloatGapToBoardRatio"/> × that extent BELOW the board's underside so
    /// the board floats, rotation from the board's own world yaw. Refuses — with one warn and
    /// no write — when the board is not measurable or measures implausibly; the caller retries
    /// on the scan cadence. Returns true once the room stands; after that it is never called
    /// again for this activation, so the room is provably frozen in world space.
    /// </summary>
    private static bool TryPlaceRoom(Transform anchor, string why)
    {
        GameObject? room = _roomGo;
        if (room == null || _roomPlaced || _roomAuthoredPlayExtent <= 0.01f)
            return false;

        if (!TryMeasureBoardWorld(out Vector3 center, out float undersideY, out float extent,
                                  out Quaternion boardYaw, out int tileCount))
            return false;

        float rigScale = anchor.lossyScale.x;
        if (!(rigScale > 0f) || float.IsInfinity(rigScale))
            rigScale = 1f;
        float perceived = extent / rigScale;

        // THE CHECK 130 DID NOT HAVE (class doc MEASURING THE BOARD). A measurement outside the
        // plausible window is a bug in the measurement, never a board — refuse, say so once, and
        // let the probe try again. A wrong number must never reach a shipped placement.
        if (!(perceived >= MinPlausibleBoardMeters) || !(perceived <= MaxPlausibleBoardMeters))
        {
            if (!_implausibleWarned)
            {
                _implausibleWarned = true;
                VRLog.Warn("Core", $"Sky alternative: REFUSING to place the room — the board measured " +
                                   $"{extent:F2} world units across over {tileCount} hex tile(s), which at " +
                                   $"rig scale {rigScale:F2} is a perceived {perceived:F3} m, outside the " +
                                   $"plausible {MinPlausibleBoardMeters:F1}–{MaxPlausibleBoardMeters:F0} m " +
                                   "window. The sky is shown alone and the probe retries; this is the " +
                                   "guard that the ModBuild-130 seven-centimetre room did not have.");
            }
            return false;
        }

        // THE PROPORTIONS (class doc ANCHORING). The board sizes the PLAY SPACE — the clearing,
        // the interior — and the scale that follows carries the rest of the art proportionally
        // outward, so tree bands and walls land far outside the board instead of inside it.
        float playWorld = PlaySpaceToBoardRatio * extent;
        float roomScale = playWorld / _roomAuthoredPlayExtent;
        float roomTotalWorld = _roomAuthoredExtent * roomScale; // the far-plane budget, not the play space

        // THE FLOAT. The underside stays the reference plane; the floor drops a board-proportional
        // gap below it, so the board hovers over the ground like a tabletop diorama at EVERY zoom.
        float floatGap = FloatGapToBoardRatio * extent;
        float floorY = undersideY - floatGap;

        // The player's own real floor, for the log only: tracking space is floor-origin, so the
        // floor point under the head is where he physically stands. No head yet -> the rig origin.
        Camera? headCam = VRRigDriver.HeadCamera;
        Vector3 headLocal = headCam != null ? headCam.transform.localPosition : Vector3.zero;
        float playerFloorY = headCam != null && headLocal.sqrMagnitude > 1e-6f
            ? anchor.TransformPoint(new Vector3(headLocal.x, 0f, headLocal.z)).y
            : anchor.position.y;
        float standDelta = playerFloorY - floorY; // + = he stands above the room floor

        room.transform.SetPositionAndRotation(new Vector3(center.x, floorY, center.z), boardYaw);
        room.transform.localScale = Vector3.one * roomScale;

        // PAY THE SKY'S OUTSTANDING YAW DEBT (see _skyYawFromBoard). The sky is normally seated
        // several seconds before this — no hex tile exists at rig-build time — so it is standing at
        // the provisional head/rig yaw right now, which is exactly the ModBuild-137 defect: moon
        // over there, its own shafts over here. One rotation write, once per activation, about the
        // sky's OWN origin: a dome is rotationally symmetric about its centre, so nothing moves,
        // nothing pops, and NotifyRigScaled's position/scale invariant is untouched (it writes
        // position and scale, never rotation). This is also the only reason a peer's moon can agree
        // with mine: boardYaw is read from the board hierarchy, which is identical on every client.
        // THE TEST IS THE VALUE, NOT THE LATCH: the sky may already have taken a board yaw of its
        // own, and both readings walk a HashSet of tiles to find the hierarchy root. All tiles of a
        // board share that root, so the two agree — but "agree by argument" is not "agree by
        // measurement", and the room's number is the authoritative one because the shafts are IN it.
        if (_skyGo != null
            && Mathf.Abs(Mathf.DeltaAngle(_skyGo.transform.rotation.eulerAngles.y,
                                          boardYaw.eulerAngles.y)) > 0.01f)
        {
            Quaternion had = _skyGo.transform.rotation;
            _skyGo.transform.rotation = boardYaw;
            bool wasProvisional = !_skyYawFromBoard;
            _skyYawFromBoard = true;
            VRLog.Info("Core", $"SKY YAW: room landed and the sky was on " +
                               $"{(wasProvisional ? "its provisional" : "a DIFFERENT board")} yaw " +
                               $"{had.eulerAngles.y:F1}deg — RE-YAWED to the board's " +
                               $"{boardYaw.eulerAngles.y:F1}deg (delta " +
                               $"{Mathf.DeltaAngle(had.eulerAngles.y, boardYaw.eulerAngles.y):F1}deg). " +
                               "Moon and light shafts now stand on the same authored MoonDir, and every " +
                               "client that measures this board resolves the same number. Rotation only: " +
                               "the dome's origin, scale and rig-scale tracking are not touched.");
        }
        else if (_skyGo != null)
        {
            _skyYawFromBoard = true;
            VRLog.Info("Core", $"SKY YAW: room landed at {boardYaw.eulerAngles.y:F1}deg and the sky was " +
                               $"ALREADY on that yaw — no re-yaw needed (the board was measurable when " +
                               "the sky was placed). Moon and light shafts stand on the same authored " +
                               "MoonDir.");
        }
        _roomWorldExtent = roomTotalWorld;
        _roomPlaced = true;
        if (!room.activeSelf)
        {
            room.SetActive(true);
            KickRoomParticles(room);
        }

        VRLog.Info("Core", $"Sky alternative: ROOM placed ({why}) — board {extent:F2} world units across " +
                           $"over {tileCount} hex tile(s), perceived {perceived:F2} m at rig scale " +
                           $"{rigScale:F2}. PLAY SPACE = {PlaySpaceToBoardRatio:F1}x the board = " +
                           $"{playWorld:F2} world units, perceived {playWorld / rigScale:F2} m; scale " +
                           $"{roomScale:F3} from an authored play space of {_roomAuthoredPlayExtent:F1} m " +
                           $"[{(_roomPlayExtentFromMarker ? $"'{PlaySpaceMarkerName}' marker" : "FALLBACK: total renderer extent, no marker in this bundle")}], " +
                           $"total room art {_roomAuthoredExtent:F1} m authored -> {roomTotalWorld:F2} world " +
                           $"units, perceived {roomTotalWorld / rigScale:F2} m. FLOAT: board underside y " +
                           $"{undersideY:F2}, floor dropped {floatGap:F2} world units = perceived " +
                           $"{floatGap / rigScale:F2} m below it to y {floorY:F2}; the player's real floor " +
                           $"is y {playerFloorY:F2}, i.e. {standDelta:F2} world units = perceived " +
                           $"{standDelta / rigScale:F2} m above the room floor. Centre {center:F2}, yaw " +
                           $"{boardYaw.eulerAngles.y:F1}deg from the board. WORLD-FIXED from now on: no " +
                           "per-frame writes, no rig-scale tracking, no re-seat of any kind — the board can " +
                           "never move inside the room again.");
        return true;
    }

    /// <summary>
    /// The board's world YAW alone — the cheap half of <see cref="TryMeasureBoardWorld"/>, for the
    /// SKY branch, which needs the rotation and nothing else (its origin is the player's floor
    /// point and its scale is the rig's). One dictionary-free walk that stops at the FIRST live hex
    /// tile and reads its hierarchy root, because every tile of a board shares that root — the same
    /// source, and therefore provably the same number, as the room's <c>boardYaw</c>.
    ///
    /// <para>False while no tile exists, which is the normal state on the frame the rig is built:
    /// the caller then seats the sky provisionally and <see cref="TryPlaceRoom"/> re-yaws it. Called
    /// only on the sky's placement events (spawn, rig rebuild/recenter/ring seat) — never per
    /// frame.</para>
    /// </summary>
    private static bool TryMeasureBoardYaw(out Quaternion yaw)
    {
        yaw = Quaternion.identity;
        try
        {
            if (!Singleton<ObjectCacheService>.IsInitialized)
                return false;
            ObjectCacheService cache = Singleton<ObjectCacheService>.Instance;
            if (cache == null)
                return false;
            HashSet<TileBehaviour> tiles = cache.GetTileBehaviors();
            if (tiles == null || tiles.Count == 0)
                return false;
            foreach (TileBehaviour tile in tiles)
            {
                if (tile == null)
                    continue;
                yaw = VRRigDriver.YawOnly(tile.transform.root.rotation);
                return true;
            }
            return false;
        }
        catch
        {
            return false; // scenario tearing down mid-read — the caller falls back and retries
        }
    }

    /// <summary>
    /// The board's world-space measurement (class doc MEASURING THE BOARD). Source: the live
    /// hex tiles in <c>ObjectCacheService</c> — the set the game's own camera and the mod's
    /// spawn-ring seat solver both use. Horizontal extent from the tiles' world positions
    /// widened by half a hex; underside from the lowest non-particle
    /// <c>Renderer.bounds.min.y</c> under those tiles, clamped to at most half an extent below
    /// the hex plane so a single stray renderer cannot drop the floor into the void; yaw from
    /// the board hierarchy's own root transform (constant, and never the player's gaze). False
    /// while no tile exists — the caller's "not yet" signal. Called only on placement attempts,
    /// at most once per <see cref="ScanIntervalFrames"/> frames, and never again once the room
    /// stands.
    /// </summary>
    private static bool TryMeasureBoardWorld(out Vector3 center, out float undersideY,
                                            out float extent, out Quaternion boardYaw,
                                            out int tileCount)
    {
        center = Vector3.zero;
        undersideY = 0f;
        extent = 0f;
        boardYaw = Quaternion.identity;
        tileCount = 0;

        try
        {
            if (!Singleton<ObjectCacheService>.IsInitialized)
                return false;
            ObjectCacheService cache = Singleton<ObjectCacheService>.Instance;
            if (cache == null)
                return false;
            HashSet<TileBehaviour> tiles = cache.GetTileBehaviors();
            if (tiles == null || tiles.Count == 0)
                return false;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float planeY = float.MaxValue;
            float lowestRenderer = float.MaxValue;
            Transform? boardRoot = null;

            foreach (TileBehaviour tile in tiles)
            {
                if (tile == null)
                    continue;
                Transform t = tile.transform;
                Vector3 p = t.position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
                if (p.y < planeY) planeY = p.y;
                boardRoot ??= t.root;
                tileCount++;

                foreach (Renderer r in tile.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r is ParticleSystemRenderer)
                        continue; // particle bounds follow live particles, not the board
                    float bottom = r.bounds.min.y;
                    if (bottom < lowestRenderer)
                        lowestRenderer = bottom;
                }
            }
            if (tileCount == 0)
                return false;

            // Half a hex on every side: the footprint above is measured from tile ORIGINS.
            // s_TileSize.x is the runtime hex WIDTH in world units (decompiled
            // UnityGameEditorRuntime: taken from the 'Hex' resource's BoxCollider), so a
            // single-tile board still yields a sane non-zero extent.
            float halfHex = Mathf.Max(UnityGameEditorRuntime.s_TileSize.x, 0f) * 0.5f;
            float sizeX = maxX - minX + 2f * halfHex;
            float sizeZ = maxZ - minZ + 2f * halfHex;
            extent = Mathf.Max(sizeX, sizeZ);
            if (!(extent > 0f) || float.IsNaN(extent) || float.IsInfinity(extent))
                return false;

            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            // The room-chunk volumes are the SECOND underside source and usually the decisive
            // one: the map's own floor art belongs to the Apparance-generated content under a
            // ProceduralMapTile, NOT to the hex tiles, so a floor placed at the hex plane would
            // be drawn OVER the dungeon floor and the board would lose its own ground.
            float chunkBottom = LowestRoomChunkY(centerX, centerZ, sizeX, sizeZ);

            float bottomY = planeY;
            if (lowestRenderer < bottomY) bottomY = lowestRenderer;
            if (chunkBottom < bottomY) bottomY = chunkBottom;
            undersideY = Mathf.Max(bottomY, planeY - 0.5f * extent);
            center = new Vector3(centerX, undersideY, centerZ);
            boardYaw = boardRoot != null ? VRRigDriver.YawOnly(boardRoot.rotation) : Quaternion.identity;
            return true;
        }
        catch
        {
            return false; // scenario tearing down mid-read — the caller simply tries again
        }
    }

    /// <summary>
    /// The lowest world y of the scenario's ROOM-CHUNK volumes that overlap the board footprint
    /// — the second, usually decisive underside source (see <see cref="TryMeasureBoardWorld"/>).
    /// <see cref="float.MaxValue"/> when none qualifies.
    ///
    /// <para>THE 130 TRAP, DISARMED. ModBuild 130 read exactly these chunks through
    /// <c>Collider.bounds</c>, and a DISABLED collider reports a zero-size bounds at the WORLD
    /// ORIGIN — encapsulating those collapsed the whole measurement to about one world unit and
    /// shipped a seven-centimetre room. Here the box is reconstructed from its own
    /// <c>center</c>/<c>size</c> through the transform, which is exact whatever the collider's
    /// enabled state, and a chunk is only accepted when it has a real horizontal size AND its
    /// footprint actually overlaps the hex footprint. A stray box at the origin can no longer
    /// reach this number.</para>
    ///
    /// <para>The chunk set comes from <see cref="SceneRegistry.MapTiles"/> — about ten entries,
    /// not a heap sweep (and if that registry ever failed to arm, its own fallback is the plain
    /// FindObjectsOfType it replaced: slower, never wrong).</para>
    /// </summary>
    private static float LowestRoomChunkY(float centerX, float centerZ, float sizeX, float sizeZ)
    {
        float lowest = float.MaxValue;
        float boardMinX = centerX - sizeX * 0.5f, boardMaxX = centerX + sizeX * 0.5f;
        float boardMinZ = centerZ - sizeZ * 0.5f, boardMaxZ = centerZ + sizeZ * 0.5f;

        SceneRegistry.MapTiles.Collect(ChunkScratch);
        foreach (ProceduralMapTile chunk in ChunkScratch)
        {
            if (chunk == null)
                continue;
            BoxCollider box = chunk.BoxCollider;
            if (box == null)
                continue;
            Transform t = box.transform;
            Vector3 lossy = t.lossyScale;
            Vector3 half = new Vector3(box.size.x * Mathf.Abs(lossy.x), box.size.y * Mathf.Abs(lossy.y),
                                       box.size.z * Mathf.Abs(lossy.z)) * 0.5f;
            if (half.x <= 0.01f || half.z <= 0.01f)
                continue; // degenerate box — never let one contribute a floor height
            Vector3 world = t.TransformPoint(box.center);
            if (world.x + half.x < boardMinX || world.x - half.x > boardMaxX)
                continue; // does not overlap the board footprint — not this board's floor
            if (world.z + half.z < boardMinZ || world.z - half.z > boardMaxZ)
                continue;
            float bottom = world.y - half.y;
            if (bottom < lowest)
                lowest = bottom;
        }
        ChunkScratch.Clear(); // do not pin destroyed components between placements
        return lowest;
    }

    /// <summary>Reused buffer for <see cref="LowestRoomChunkY"/> — the registry fills it; it is
    /// only ever touched on a placement attempt, never per frame.</summary>
    private static readonly List<ProceduralMapTile> ChunkScratch = new();

    /// <summary>
    /// Defensive scale re-sync, SKY BRANCH ONLY (class doc PER-FRAME COST): the sky's scale must
    /// equal the rig scale at all times — <see cref="NotifyRigScaled"/> keeps it there through
    /// every known scale writer (WorldGrab two-hand pinch, Comfort.SetScaleMultiplier) and the
    /// re-seat covers rig builds. This heal only ever fires if a FUTURE writer scales the rig
    /// without notifying; it re-syncs about the head pivot (the view does not lurch — the same
    /// pivot rule Comfort.SetScaleMultiplier uses) so the invariant is self-righting rather than
    /// silently broken. THE ROOM IS DELIBERATELY NOT TOUCHED: it is world-fixed by ruling and
    /// has no rig-scale invariant to heal. Steady-state cost: two float reads and a compare.
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
            VRLog.Warn("Core", $"Sky alternative: the sky's scale drifted from the rig scale " +
                               $"({skyScale:F3} vs {rigScale:F3}) and was healed about the head — " +
                               "some rig-scale writer is not calling SkyAlternative.NotifyRigScaled.");
        }
    }

    /// <summary>
    /// A rig-scale writer just rescaled the rig about <paramref name="pivotWorld"/> (the world
    /// point it kept glued to a tracking point: WorldGrab's hand midpoint, Comfort's head).
    /// Mirror it onto the SKY so it stays bit-frozen in the player's REAL frame — same perceived
    /// size, same perceived offset, a distant sky at every zoom — while drag/turn components of
    /// the same gesture pass through as movement (invariance proof in the class doc ZOOM ALGEBRA
    /// note). THE ROOM IS NEVER TOUCHED HERE: it is world-fixed to the board, and mirroring the
    /// zoom onto it is exactly the bug this round removes. Cheap and re-entrant: two early-outs
    /// while no environment is shown, one transform write while one is.
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

    // ---- the shared environment clock (multiplayer) --------------------------------------------

    /// <summary>
    /// USER REQUEST (ModBuild 137 round, verbatim): "Mond und Lichtstrahlen sollen im Multiplayer
    /// (falls beide Spieler die selbe Umgebung ausgewählt haben) auch synchronisiert werden. Das
    /// gilt generell für alle Effekt zB auch die Maus. Ich will das alle Spieler sie gleichzeitig
    /// sehen (wenn die spieler es an haben)."
    ///
    /// <para>WHAT "die Maus" IS. It is the cellar's RAT — <c>EnvCritter.shader</c>, the ModBuild-134
    /// ruling "eine Ratte huscht durch den Raum": a real lit animal that runs a fixed Bézier across
    /// the floor every <c>_Period</c> seconds and is invisible in between. It is the clearest
    /// example of the class he is actually describing: an EVENT, something a player says "look" at.
    /// (It is not a mouse cursor; the mod has no world mouse cursor, and the laser pointer's hit
    /// marker is per-viewer aiming feedback, not an event — see the report.)</para>
    ///
    /// <para>WHY ONE NUMBER FIXES ALL OF THEM. The environment bundle ships with NO MonoBehaviours
    /// (<c>Editor/BuildEnvironments.cs:35</c>): every animation in it is either Shuriken or
    /// <c>_Time</c> in a shader. And every <c>_Time</c>-reading Env* shader already reads it as
    /// <c>_Time.y + _GhvrTimeOfs</c> — a global float that the runtime has never written (it exists
    /// for the editor's still-frame preview, <c>EnvRoom.shader:63</c>). So a single
    /// <c>Shader.SetGlobalFloat</c> moves the rat, the drip and its puddle rings, the candle
    /// flicker and glow, the canopy sway, the water glints and the shafts'/beam's shimmer onto one
    /// clock — for zero per-effect traffic and zero per-frame cost. The offset is chosen so that
    /// <c>_Time.y + _GhvrTimeOfs</c> equals the SAME number on every client that shows the same
    /// environment; the sum is by construction the clock OWNER's own <c>_Time.y</c>, which is ≥ 0,
    /// so no shader's <c>fmod</c> can ever see a negative argument no matter which client joined
    /// first.</para>
    ///
    /// <para>WHAT IS DELIBERATELY NOT SYNCHRONISED, and why (the full table is in the round's
    /// report):
    /// <list type="bullet">
    /// <item>THE MOON — already identical, and not by this clock: it does not ride the celestial
    /// rotation at all (<c>EnvStars.shader:28</c> — "it does NOT ride the celestial rotation"), it
    /// is the authored <c>_MoonDir</c> constant. Item A's board yaw is what makes it agree.</item>
    /// <item>CELESTIAL ROTATION AND PER-STAR TWINKLE — <b>now ON the shared clock</b> (ModBuild
    /// 143). This entry used to say the sky clock could not be reached because <c>EnvStars</c> and
    /// <c>EnvStarPoints</c> built it from raw <c>fmod(_Time.y, SKY_PERIOD)</c>; both shaders now
    /// fold <c>_GhvrTimeOfs</c> in, because the LUNAR ECLIPSE needed it — an event with a position
    /// that moves is the first sky content two players could catch each other disagreeing about,
    /// and its 30 s period divides the 2880 s sky wrap exactly (96 periods) so the wrapped and the
    /// raw clock give the same phase. The twinkle came along for free.</item>
    /// <item>SHOOTING STARS, FIREFLIES, DUST MOTES, FOG — Shuriken, per-client seeded. Meteors are
    /// the one arguable "event", but syncing them needs either a per-event packet or a
    /// deterministic re-seed plus a <c>Simulate</c> catch-up on a system whose emission is a rate,
    /// not a burst; at ~one every eight seconds, in a random direction, over a sky that no two
    /// players are looking at anyway, that is a byte and a risk spent on a coincidence.</item>
    /// </list></para>
    ///
    /// <para>ACCURACY. The owner's reading is applied on arrival, so a follower runs one one-way
    /// latency behind (tens of milliseconds on the side channel) — three orders below the rat's
    /// period and below a frame at 90 Hz for the flicker. Both clocks then advance on the same
    /// real-time base, so there is nothing left to drift; the correction below exists for the
    /// re-election and scene-reload cases, not for oscillator drift.</para>
    /// </summary>
    internal static float EnvClockSeconds => Time.timeSinceLevelLoad + _timeOfs;

    /// <summary>The live global shader offset — the number that makes <c>_Time.y + _GhvrTimeOfs</c>
    /// read the same on every client showing this environment. 0 = we are the clock owner, or there
    /// is nobody to agree with (single player, MR, a peer on another style).</summary>
    private static float _timeOfs;

    /// <summary>Where <see cref="_timeOfs"/> is heading. Set by the net layer, walked to by
    /// <see cref="TickEnvClock"/> so a correction is frame-rate-correct and never a pop.</summary>
    private static float _timeOfsTarget;

    /// <summary>The player id whose clock we follow; 0 = ours (we are the lowest id present, or
    /// alone). Purely diagnostic bookkeeping plus the "owner changed ⇒ jump, do not slew" test.</summary>
    private static int _clockOwner;
    private static int _loggedClockOwner = int.MinValue;
    private static bool _timeOfsWritten;   // the uniform has been written at least once
    private static readonly int GhvrTimeOfsId = Shader.PropertyToID("_GhvrTimeOfs");

    /// <summary>
    /// THE ROOM'S KIND, as a shader global: <c>1</c> while the CELLAR stands, <c>0</c> while the
    /// forest does, <c>0</c> while no environment stands at all.
    ///
    /// <para>WHY IT EXISTS (user report, ModBuild 146): "Der 'Hell'-Effekt im Keller ... es soll
    /// wirklich den Mondschein heller machen statt den ganzen Raum." The element gains in
    /// <c>EnvElement.cginc</c> are shared by both rooms, and the two rooms want OPPOSITE things
    /// from Light — the forest clearing is supposed to brighten (his own ruling one round earlier:
    /// "Licht und Dunkelheit beeinflussen zwar den Mond aber nicht die Lichtverhältnisse in der
    /// Lichtung"), while the cellar is supposed to keep its darkness and put the whole gain into
    /// the moonlight coming through the window. Without a way to tell the two apart, one of those
    /// two rulings has to lose.</para>
    ///
    /// <para>WHY A GLOBAL AND NOT A MATERIAL FLOAT. A per-material property would have to be
    /// declared in nine shaders and written in <c>ApplyRig</c>, which means a re-bake and a
    /// regenerated prefab for what is a single constant per room — and it would put the change on
    /// the one file (<c>BuildEnvironmentRooms.cs</c>) that every other lane this round also has to
    /// touch. A global costs one <c>SetGlobalFloat</c> when the style changes and nothing at all
    /// per frame, and it follows the established pattern of <c>_GhvrTimeOfs</c>, <c>_GhvrElemA/B</c>
    /// and <c>_GhvrHaunt</c>: the environment publishes NUMBERS, the shaders decide what to do with
    /// them.</para>
    ///
    /// <para>It is presentation only and NEVER goes on the wire — every client derives it from its
    /// own style dial, exactly as it derives which room to instantiate.</para>
    /// </summary>
    private static readonly int GhvrIndoorId = Shader.PropertyToID("_GhvrIndoor");

    /// <summary>Last value pushed to <see cref="GhvrIndoorId"/>, so the write is one float compare
    /// per frame in the settled case. NaN forces the first write.</summary>
    private static float _indoorWritten = float.NaN;

    /// <summary>Publish <see cref="GhvrIndoorId"/> for the style now standing. Called from the
    /// active branch of <see cref="Tick"/> and from the teardown paths.</summary>
    private static void ApplyIndoor(SkyStyle style)
    {
        float v = style == SkyStyle.Cellar ? 1f : 0f;
        if (v == _indoorWritten)
            return;
        _indoorWritten = v;
        Shader.SetGlobalFloat(GhvrIndoorId, v);
        VRLog.Info("Core", $"ENV ROOM KIND: _GhvrIndoor = {v:F0} ({(v > 0f ? "CELLAR — indoors" : "not indoors")}). " +
                           "The element gains read it to tell the two rooms apart: the cellar keeps its " +
                           "darkness and spends Light on the moonbeam through the window, the forest " +
                           "clearing brightens. Presentation only — never on the wire; every client " +
                           "derives it from its own style dial.");
    }

    /// <summary>Above this error the correction is a JUMP, not a walk: at that size the two clocks
    /// were never the same clock (a fresh adoption, a re-election, a scene reload that reset
    /// <c>_Time.y</c>), and walking there would take minutes of visibly wrong phase.</summary>
    private const float ClockJumpSeconds = 1.5f;

    /// <summary>Below this the offset is left alone — a packet-rate ±ms jitter must not produce a
    /// per-packet shader write.</summary>
    private const float ClockDeadbandSeconds = 0.02f;

    /// <summary>How fast a sub-jump correction is walked off, in seconds of clock per second of
    /// wall time. Slow enough that the rat's gait and the candle flicker cannot be seen to
    /// stretch, fast enough to close the deadband-to-jump band in under ten seconds.</summary>
    private const float ClockSlewRate = 0.2f;

    /// <summary>
    /// The style code this client puts on the wire: 0 = nothing to share, else the
    /// <see cref="SkyStyle"/> value. Non-zero ONLY while a shell with animated content is really
    /// standing (Cellar/SwampNight) — Default shares the game's own sky and OffBlack shares an empty
    /// black void, and neither has a single <c>_Time</c>-driven effect to agree about. MR forces
    /// the whole feature off locally, so an MR player automatically reports 0.
    /// </summary>
    internal static byte WireStyleCode =>
        _active && _skyGo != null && (_appliedStyle == SkyStyle.Cellar
                                      || _appliedStyle == SkyStyle.SwampNight)
            ? (byte)_appliedStyle
            : (byte)0;

    /// <summary>This client's current shared-environment clock in milliseconds — the value that
    /// goes on the wire. Wraps at 2^32 ms (49.7 days of level time); no session survives that, and
    /// the follower's offset is a DIFFERENCE, so even a wrap would cost one jump and heal.</summary>
    internal static uint EnvClockMillis
    {
        get
        {
            double s = EnvClockSeconds;
            if (!(s > 0d))
                return 0u;
            return (uint)((long)System.Math.Round(s * 1000d) & 0xFFFFFFFFL);
        }
    }

    /// <summary>
    /// Follow <paramref name="ownerPlayerId"/>'s environment clock. The net layer elects the owner
    /// (the lowest player id among everyone showing this same style, self included) and calls this
    /// with that peer's last reading; when the local player IS the lowest, it calls
    /// <see cref="OwnEnvClock"/> instead. Election by lowest id is what makes the result the same
    /// on every machine without a host concept and without a handshake.
    ///
    /// <para><paramref name="localClockAtArrival"/> is <c>Time.timeSinceLevelLoad</c> AS IT WAS when
    /// that reading arrived, and pairing the two is the whole correctness of this: the caller is
    /// re-evaluated every frame with the same (unreliable, ≤5 Hz) reading, and pairing an old
    /// reading with the CURRENT local time would drag the target backwards by a second per second
    /// between packets. Both clocks advance at the same rate, so the offset computed from the pair
    /// is constant and re-computing it every frame is a no-op.</para>
    /// </summary>
    internal static void FollowEnvClock(int ownerPlayerId, uint ownerClockMillis,
                                        float localClockAtArrival)
    {
        float target = (float)(ownerClockMillis * 0.001d) - localClockAtArrival;
        bool ownerChanged = _clockOwner != ownerPlayerId;
        _clockOwner = ownerPlayerId;
        _timeOfsTarget = target;
        if (ownerChanged || Mathf.Abs(target - _timeOfs) > ClockJumpSeconds)
            ApplyTimeOfs(target, ownerChanged ? "new clock owner" : "resync (error beyond the walk band)");
    }

    /// <summary>We are the clock: the offset is 0 by definition and everyone else walks to us.
    /// Also the state a lone player, an MR player and a player whose peers chose another
    /// environment are in — which is why it is the same call.</summary>
    internal static void OwnEnvClock()
    {
        bool ownerChanged = _clockOwner != 0;
        _clockOwner = 0;
        _timeOfsTarget = 0f;
        if (ownerChanged || Mathf.Abs(_timeOfs) > ClockJumpSeconds)
            ApplyTimeOfs(0f, ownerChanged ? "clock owner gone — this client is the reference again" : "reference reset");
    }

    /// <summary>Walk <see cref="_timeOfs"/> to its target and push the uniform when it moved.
    /// Called once per <see cref="Tick"/> while an environment stands; costs one float compare in
    /// the settled case, which is every frame after the first packet.</summary>
    private static void TickEnvClock()
    {
        float err = _timeOfsTarget - _timeOfs;
        if (Mathf.Abs(err) <= ClockDeadbandSeconds)
        {
            if (!_timeOfsWritten)
                ApplyTimeOfs(_timeOfs, "first write");
            return;
        }
        if (Mathf.Abs(err) > ClockJumpSeconds)
        {
            ApplyTimeOfs(_timeOfsTarget, "walk band exceeded");
            return;
        }
        float step = ClockSlewRate * Mathf.Max(Time.unscaledDeltaTime, 0f);
        ApplyTimeOfs(_timeOfs + Mathf.Clamp(err, -step, step), null);
    }

    /// <summary>The ONE writer of the global <c>_GhvrTimeOfs</c> uniform (and the reason nothing
    /// else in the mod may write it: it is a global, and the whole room has to move together — the
    /// shader's own note). Logs only when the reason is worth a line, never per walked frame.</summary>
    private static void ApplyTimeOfs(float value, string? why)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return; // a poisoned wire value must never reach a shader global
        _timeOfs = value;
        _timeOfsWritten = true;
        Shader.SetGlobalFloat(GhvrTimeOfsId, value);
        if (why == null && _clockOwner == _loggedClockOwner)
            return;
        _loggedClockOwner = _clockOwner;
        VRLog.Info("Core", $"ENV SYNC: environment clock offset {value:F3}s applied " +
                           $"({why ?? "walked"}) — " +
                           (_clockOwner == 0
                               ? "this client OWNS the clock (lowest player id, or nobody else shows this environment)"
                               : $"following player {_clockOwner}") +
                           $". Shared clock now {EnvClockSeconds:F2}s; the rat, the drip and its puddle " +
                           "rings, the candle flicker, the canopy sway, the shafts' shimmer, the " +
                           "apparitions' schedule and — since ModBuild 143 — the star rotation and the " +
                           "lunar eclipse run on it. The moon's DIRECTION never needed it (fixed " +
                           "_MoonDir plus the board yaw); the eclipse crossing it does, which is what " +
                           "put _GhvrTimeOfs into EnvStars/EnvStarPoints.");
    }

    /// <summary>Drop the shared clock back to the local one. Called from the teardown path: a
    /// leftover global offset must not survive into a session that shows no environment, or into
    /// the editor-preview contract the uniform was originally built for.</summary>
    private static void ResetEnvClock()
    {
        _timeOfsTarget = 0f;
        _clockOwner = 0;
        _loggedClockOwner = int.MinValue;
        if (_timeOfs == 0f && !_timeOfsWritten)
            return;
        _timeOfs = 0f;
        _timeOfsWritten = false;
        Shader.SetGlobalFloat(GhvrTimeOfsId, 0f);
    }

    // ---- deactivate ---------------------------------------------------------------------------

    /// <summary>Clear the room branch's placement bookkeeping (spawn, style switch, teardown).</summary>
    private static void ResetRoomState()
    {
        _roomPlaced = false;
        _roomAuthoredExtent = 0f;
        _roomAuthoredPlayExtent = 0f;
        _roomPlayExtentFromMarker = false;
        _roomWorldExtent = 0f;
        _nextRoomProbeFrame = 0;
        _implausibleWarned = false;
        _skyYawFromBoard = false; // a fresh activation owes the sky a board yaw again
    }

    /// <summary>
    /// DESTROY both branch roots and forget everything that describes a SHOWN environment —
    /// without touching the game sphere, which the OffBlack style still wants hidden.
    ///
    /// <para>DESTROY, never disable: this is the mechanism behind the class doc's WHAT IS ALIVE
    /// WHILE NOTHING IS SHOWN answer. A ParticleSystem whose renderer is merely switched off goes
    /// on simulating every frame, and so can an active one that is only off-camera; a destroyed
    /// GameObject cannot. Because this is the ONLY teardown path in the file, "no environment
    /// shown" and "no environment instance exists" are the same statement.</para>
    ///
    /// <para>Clearing <see cref="_active"/> here is load-bearing beyond bookkeeping: it is what
    /// returns <see cref="MinFarWorldUnits"/> to 0 and what makes <see cref="NotifyRigScaled"/>
    /// early-out, so a style that shows nothing neither widens the depth range nor chases a sky
    /// that is no longer there.</para>
    /// </summary>
    private static void DespawnEnvironment()
    {
        if (_skyGo != null)
        {
            Object.Destroy(_skyGo);
            _skyGo = null;
        }
        if (_roomGo != null)
        {
            Object.Destroy(_roomGo);
            _roomGo = null;
        }
        _appliedStyle = SkyStyle.Default;
        _placedPoseVersion = int.MinValue; // a fresh activation always places fresh
        _nextHealLogTime = 0f;
        ResetRoomState();
        ResetEnvClock(); // no environment ⇒ no shared clock; the global goes back to the shipped 0
        ApplyIndoor(SkyStyle.Default); // ...and no room ⇒ not indoors, or a torn-down cellar would
                                       // leave every shader in the game believing it still stands
        _active = false;
        _loggedActive = false;
    }

    /// <summary>Back to vanilla: re-enable the game sphere and destroy both branch roots.
    /// The loaded prefab references stay (session-cached by design — class doc). The
    /// <c>_blackShown</c> latch is part of the early-out test because OffBlack can hold the
    /// sphere down on a frame where nothing else is set — without it, leaving OffBlack for
    /// Default would keep the latch and the next OffBlack settle would never re-log.</summary>
    private static void Deactivate()
    {
        if (!_active && !_blackShown && _skyGo == null && _roomGo == null && _hiddenSphere == null)
            return;

        RestoreGameSphere();
        bool wasActive = _active;
        DespawnEnvironment();
        _blackShown = false;
        _blackLogged = false;
        if (wasActive)
        {
            VRLog.Info("Core", "Sky alternative OFF — game sphere restored, 3D environment despawned.");
            TeardownReport.Note("sky alternative (game sphere restored, room + sky despawned)");
        }
    }
}
