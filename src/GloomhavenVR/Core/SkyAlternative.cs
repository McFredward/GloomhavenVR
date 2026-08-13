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

    /// <summary>A candle-lit cellar atmosphere from the mod's own bundle content
    /// (Env_Cellar FX shell: dust motes today; the content lane extends it with custom
    /// environment art in the game's painterly style).</summary>
    Cellar = 1,

    /// <summary>A moonlit swamp night from the mod's own bundle content (Env_Swamp FX
    /// shell: star dome, shooting stars, fireflies, ground fog; the content lane extends
    /// it with custom environment art in the game's painterly style).</summary>
    SwampNight = 2,
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
/// HISTORY: game-asset room generation (ModBuilds 127–131 — instantiating the game's own
/// scenario maps, dressing them through the live Apparance engine and seating them life-size
/// around the player) was removed by user ruling 2026-08-13 — see
/// <c>.planning/game-env-postmortem.md</c> for everything those five hardware rounds learned.
/// Environments are custom bundle content again. NEVER re-seat an occupied room: re-seating
/// geometry the player stands IN reads as a sudden player teleport and visually displaces the
/// board (the ModBuild-131 finding — five such events in the final log ended the approach).
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
///  1. HIDES the game's sky sphere (<c>GH_SkySphere</c>) via <c>renderer.enabled = false</c>.
///     That is safe for PURE hiding — <see cref="SkyBackdrop"/>'s class doc documents the trap
///     precisely: suppressing a renderer only bites when a CommandBuffer is supposed to REDRAW
///     it, which nothing does here. The sphere is found through <see cref="SkyBackdrop.FindSky"/>
///     (one shared set of name/shader hints), recorded, and re-enabled on restore. Hidden for
///     BOTH styles: SwampNight's star dome must own the sky, and the cellar mood wants darkness.
///  2. SPAWNS the FX SHELL prefab from the mod's asset bundle as the frame's ONLY child (the
///     content lane authors them against a fixed contract): <c>Env_Swamp</c> = star dome +
///     shooting stars + fireflies + ground fog; <c>Env_Cellar</c> = dust motes + a disabled
///     'GlowTemplate' child (kept disabled — it is a template). Self-lit, self-animating
///     (Shuriken only, no scripts), authored in real meters. The content lane extends these
///     prefabs with custom environment art; this class treats whatever the prefab contains
///     as one opaque shell.
///
/// The game's own scenario diorama/table stays untouched and visible — the environment
/// surrounds it. Everything lives on the MOD LAYER (only the rig head camera renders it; game
/// cameras and the FlatScreen composites never see it) and has every collider stripped —
/// non-interactive by ruling, it must never catch a laser/poke ray.
///
/// ANCHORING — ONE ambient frame, the model ModBuild 128 proved on hardware: a WORLD-anchored
/// root (unparented + DontDestroyOnLoad; every mod locomotion writes the RIG's transform, so
/// flight/turn/grab/walking move the player THROUGH the environment — the ModBuild-125
/// rig-child model was killed for exactly that: "Weiterhin möchte ich mich auch in den
/// umgebungen frei bewegen und drehen können, das ist aktuell nicht möglich."), with a
/// rig-tracked scale so it reads PERCEIVED-CONSTANT: at rig scale S the shell's world size is
/// (authored meters × S), so its perceived size is the authored meters, always — a distant sky
/// and ambient FX at every zoom. ZOOM ALGEBRA: the rig scales about a PIVOT (WorldGrab keeps
/// the world point under the hands glued, <c>Comfort.SetScaleMultiplier</c> keeps the head
/// still), so a merely world-fixed frame would keep its real size but DRIFT. Both scale
/// writers report through <see cref="NotifyRigScaled"/> (pivot, before, after) and the frame
/// mirrors it: <c>pos = pivot + (pos − pivot) · (after/before)</c>, <c>scale = after</c>.
/// Proof this is exact: with the rig mapping world = P + R·(s·t), the perceived
/// (tracking-space) pose of the frame is (1/s)·R⁻¹·(pos − P). The scale writer keeps the
/// pivot glued to a tracking point m: P = pivot − R·(s·m). Substituting both updates, the
/// perceived pose after the write equals the pose a pure locomotion write (same P, R change,
/// scale untouched) would produce — the scale component is bit-cancelled, the shell neither
/// grows nor drifts, while drag/turn components of the same gesture still pass through as
/// movement.
///
/// SPAWN POSE: origin = the player's floor point (the rig-space point under the head, y=0 —
/// tracking is floor-origin — mapped through the rig), yaw = the head's world forward
/// projected to the horizon, scale = the live rig scale. Head not tracked yet (rig just
/// built) → the rig's own pose stands in; the first-pose recenter bumps RigPoseVersion and
/// re-places a frame later.
///
/// RE-SEAT RULE: the frame is re-placed (fresh spawn pose) whenever
/// <see cref="VRRigDriver.RigPoseVersion"/> changes — rig (re)build, deliberate recenter
/// (B+Y chord), spawn-ring seat, menu recenter — and on NO OTHER TRIGGER, of any kind. Those
/// are exactly the "the player was teleported" events (snap turns and world grabs do NOT bump
/// it, by that counter's own contract); free movement never re-seats anything. Any other
/// re-seat trigger reads as an unprompted player teleport — the ModBuild-131 finding above;
/// do not add one. This also IS the "recenter environment" affordance: the recenter chord
/// re-derives the frame around you; re-selecting a style (switch away and back, or to the
/// other style) respawns it at the current pose — no new UI.
///
/// RIG REBUILD / MID-REBUILD FRAMES: on a frame with no RigRoot the whole feature stands down
/// (environment despawned, sphere restored) — rebuilds are rare, logged events, and the next
/// tick under the new root respawns at the new player pose. The instance is DontDestroyOnLoad
/// so a scene unload can never fake-null it out from under a live rig.
///
/// PER-FRAME COST: ZERO transform writes while active and idle — the frame is world-static
/// between events. The active steady-state tick is: enum read, sphere-hidden check,
/// instance/anchor null checks, ONE static int compare (RigPoseVersion) and one scale compare
/// (a defensive drift-heal that only ever fires if a future rig-scale writer forgets to call
/// <see cref="NotifyRigScaled"/>). Transform writes happen only inside a pinch-zoom (one
/// position+scale write per scaled frame) and on the rare RigPoseVersion re-seats.
/// Default/MR-on: an enum read plus an idempotent early-out.
///
/// PARTICLES: prefab systems auto-play (playOnAwake) and the instance is always active, so no
/// kick is strictly needed — a defensive <c>Play()</c> runs anyway after instantiate. Two
/// module normalisations make Shuriken honour the frame (the content lane authors at scale 1
/// in an editor scene and cannot know the instance runs at diorama scale):
/// <c>scalingMode = Hierarchy</c> (the established pattern — see
/// <c>Net.RemoteControlBoard</c>'s pile FX) so sizes/speeds/shapes follow the root scale and
/// stay authored-real-size, and World simulation space is switched to Local so in-flight
/// particles ride the root when the zoom scale-follow moves it instead of smearing behind
/// the shell (the root carries a spawn yaw, so a World-authored velocity direction is
/// rotated by that constant yaw — harmless for ambient FX, and constant after placement).
///
/// FAR PLANE: the shell is real-size, so at rig scale S its farthest geometry (the star dome)
/// sits up to (authored meters × S) world units from its ORIGIN — and the player can fly away
/// from that origin. <see cref="MinFarWorldUnits"/> hands VRRigDriver.TickClipPlanes
/// (<see cref="EnvMinFarMeters"/> × S + head-to-origin distance) as a far-plane floor — 0
/// when idle, still capped by the depth-precision far/near ratio.
///
/// MR PRECEDENCE (the user's rule: MR ON ⇒ the sky is ALWAYS off): <see cref="MixedReality.Tick"/>
/// calls <see cref="StandDown"/> FIRST on its MR-on path — the environment despawns and the
/// game sphere is re-enabled so MR's own <c>HideSkyGeometry</c> sweep records and disables a
/// clean renderer for the chroma key, whatever the dial says. When MR turns off the dial's
/// choice re-applies on the next tick. On the MR-off path this ticks BEFORE SkyBackdrop, and
/// while an environment is shown SkyBackdrop stands down through the same parameter MR uses
/// (a hidden sphere needs no non-occluding treatment).
///
/// ASSETS: LAZY by design — nothing loads until a style is first selected in a scenario. FX
/// shells come from the mod bundle via the established probe pattern
/// (<c>AssetBundle.GetAllLoadedAssetBundles()</c> + <c>LoadAsset</c>); a loaded prefab
/// reference is KEPT for the session (it is a reference into the loaded bundle, not a copy).
/// Missing shell prefab (older bundle) = one-shot warn, the game's own sky stays fully in
/// place.
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
    /// can generate late, and a scene change fake-nulls the acquired renderer). Same cadence as
    /// <see cref="SkyBackdrop"/> / MR's sky sweep.</summary>
    private const int ScanIntervalFrames = 60;

    /// <summary>Far-plane floor in REAL meters while an environment is active — must cover the
    /// farthest authored geometry (the swamp star dome). See the class doc's FAR PLANE note.</summary>
    private const float EnvMinFarMeters = 100f;

    /// <summary>Bundle paths of the FX SHELL prefabs, indexed by <see cref="SkyStyle"/>
    /// (0 = Default = none). Contract fixed with the content lane building the bundle:
    /// Env_Swamp = star dome + shooting stars + fireflies + ground fog; Env_Cellar = dust
    /// motes + a disabled 'GlowTemplate' child (kept disabled — it is a template). The
    /// content lane extends these prefabs with custom environment art in the game's style;
    /// this class treats the prefab as one opaque shell.</summary>
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

    // THE FRAME (class doc ANCHORING) — ONE WORLD-anchored root (DontDestroyOnLoad, no
    // parent), perceived-constant via NotifyRigScaled. Destroyed by Deactivate, never by a
    // scene unload. Its only child is the instantiated FX shell.
    private static GameObject? _envGo;
    private static SkyStyle _appliedStyle = SkyStyle.Default;

    /// <summary>The <see cref="VRRigDriver.RigPoseVersion"/> the current placement was computed
    /// for — a mismatch means the player was (re)built/recentered/ring-seated and the frame
    /// re-seats around their new pose (class doc RE-SEAT RULE — the ONLY re-seat trigger).
    /// Sentinel: never a live version.</summary>
    private static int _placedPoseVersion = int.MinValue;

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
            "never in the menu; it is built from the mod's OWN bundle content, styled to " +
            "match the game's painterly look — no game-asset room generation). Default = " +
            "the game's own animated sky, exactly as before. Cellar = an atmospheric " +
            "candle-lit cellar mood with drifting dust motes; SwampNight = a moonlit swamp " +
            "night under a star dome with shooting stars, ground fog and fireflies. A " +
            "non-Default choice in a scenario hides the game's sky sphere and places the " +
            "atmosphere around where you stand, facing your view. It stays a DISTANT, " +
            "steady surrounding at every zoom level — zooming the board never moves it, " +
            "never moves you, and never moves the board. Stick flight, turning, the " +
            "world-grab drag and physical walking all move you through it. The only thing " +
            "that re-places it around you is the recenter chord (B+Y) — the same gesture " +
            "that recenters you — or re-selecting a style. It can never catch the laser " +
            "(no colliders, mod layer only). Applies live from the VR menu, takes effect " +
            "when a scenario is running. MIXED REALITY ALWAYS WINS: while MR is on, every " +
            "sky and environment is off so the chroma key can show your room; the choice " +
            "re-applies when MR turns off. Values from the old panorama builds " +
            "(Night/Sunset) no longer exist and fall back to Default. Local presentation " +
            "only, never synced to peers.");
    }

    /// <summary>
    /// Far-plane floor for <c>VRRigDriver.TickClipPlanes</c> (class doc FAR PLANE):
    /// (<see cref="EnvMinFarMeters"/> × rig scale) world units from the frame's origin plus
    /// the head-to-origin distance (the player can fly away from it). 0 while idle (the
    /// caller's Max degenerates to its old value).
    /// </summary>
    internal static float MinFarWorldUnits(float rigScale)
    {
        if (!_active)
            return 0f;
        float floor = EnvMinFarMeters * rigScale;
        Camera? head = VRRigDriver.HeadCamera;
        GameObject? env = _envGo;
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
        // the original surroundings. Leaving/ending a scenario deactivates on the next tick.
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

        if (!_active || !_loggedActive)
        {
            _active = true;
            _loggedActive = true;
            VRLog.Info("Core", $"Sky alternative ON — style {style} (scenario active): the game's sky " +
                               "sphere is hidden (pure renderer.enabled hiding; SkyBackdrop stands down) " +
                               "and the bundled FX shell is spawned on the world-anchored, rig-scale-" +
                               "tracked frame (perceived-constant — a distant surrounding at every zoom). " +
                               "MR overrides it off; leaving the scenario despawns it.");
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
    /// Spawn (or keep) the world-anchored frame. Steady state is checks only — NO transform
    /// writes (the frame is world-static between events; class doc PER-FRAME COST): one
    /// static int compare re-seats after a rig rebuild/recenter/ring seat (the ONLY re-seat
    /// trigger — class doc RE-SEAT RULE), one scale compare is the defensive drift-heal.
    /// Instantiates on first activation and on style switch.
    /// </summary>
    private static void EnsureEnvironment(SkyStyle style, Transform anchor)
    {
        if (_envGo != null && _appliedStyle == style)
        {
            // Steady state (class doc RE-SEAT RULE): RigPoseVersion bumps only on rig
            // (re)build, deliberate recenter, ring seat and menu recenter — the "player was
            // teleported" events. Free locomotion (flight/turn/grab) never bumps it, so the
            // frame stays a fixed world place while the player moves through it. There is
            // deliberately NO other re-seat trigger (the ModBuild-131 finding: any other
            // re-seat reads as an unprompted player teleport).
            if (VRRigDriver.RigPoseVersion != _placedPoseVersion)
            {
                PlaceFrame(anchor,
                    "rig pose changed (rebuild/recenter/ring seat) — re-seating around the player");
                return;
            }
            HealScaleDrift(_envGo.transform, anchor);
            return;
        }

        if (_envGo != null)
        {
            Object.Destroy(_envGo); // style switch — the old shell goes
            _envGo = null;
        }

        // THE FRAME ROOT (class doc ANCHORING): an empty WORLD-anchored root — no parent, so
        // locomotion moves the rig relative to the world and therefore through it;
        // DontDestroyOnLoad so a scene unload can never fake-null a live frame (teardown is
        // always ours, Deactivate). Its only child is the instantiated FX shell.
        GameObject prefab = Prefabs[(int)style]!;
        _envGo = new GameObject("GloomhavenVR.SkyAlternative." + style);
        Object.DontDestroyOnLoad(_envGo);
        PlaceFrame(anchor, "spawn");

        GameObject shell = Object.Instantiate(prefab, _envGo.transform, false);
        shell.name = prefab.name; // authored disabled children (Cellar's 'GlowTemplate') stay disabled

        VRLayers.Apply(_envGo); // mod layer, recursive — head camera only (gated on IsRunning)

        // NON-INTERACTIVE BY RULING ("Nicht interaktiv rein als Umgebung"): the contract says
        // the shells ship without colliders, but a stray one would silently eat laser/poke
        // rays across the whole environment — strip defensively, once, at spawn.
        Collider[] colliders = _envGo.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            Object.Destroy(c);

        // Shuriken normalisation + defensive kick (class doc PARTICLES): Hierarchy scaling so
        // the FX follow the frame root's scale like the meshes do; Local simulation space so
        // in-flight particles ride the root when the zoom scale-follow moves it instead of
        // smearing behind it. playOnAwake already ran for active systems — the Play() is
        // belt-and-braces for ones authored with it off; disabled template children are
        // normalised but never kicked (templates, not FX).
        ParticleSystem[] systems = _envGo.GetComponentsInChildren<ParticleSystem>(true);
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
        VRLog.Info("Core", $"Sky alternative: FX shell '{prefab.name}' spawned on the world-anchored, " +
                           $"rig-scale-tracked frame (perceived-constant, NotifyRigScaled algebra). " +
                           $"{systems.Length} particle system(s) normalised (Hierarchy scaling, local " +
                           $"simulation space)" +
                           $"{(colliders.Length > 0 ? $", {colliders.Length} stray collider(s) stripped" : "")}." +
                           $"{(wasSwitch ? " (style switch)" : "")}");
    }

    /// <summary>
    /// Write the SPAWN POSE (class doc SPAWN POSE): origin = the tracked head's floor point
    /// (head rig-local position with y=0, mapped through the rig: tracking is floor-origin,
    /// so that is the real floor under the player), yaw = head world forward projected to
    /// the horizon, scale = the live rig scale — <see cref="NotifyRigScaled"/> keeps the
    /// frame perceived-constant from here. Head not tracked yet (rig just built, first pose
    /// pending) → the rig's own origin/yaw stand in; the first-pose recenter bumps
    /// RigPoseVersion and this re-runs with the real head a frame later.
    /// </summary>
    private static void PlaceFrame(Transform anchor, string why)
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
            fwd.y = 0f; // world-horizon yaw — the environment stays upright in the world
            yaw = fwd.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(fwd)
                : VRRigDriver.YawOnly(anchor.rotation); // looking straight up/down: seat yaw
        }
        else
        {
            floorPos = anchor.position;
            yaw = VRRigDriver.YawOnly(anchor.rotation);
        }

        if (_envGo != null)
        {
            _envGo.transform.SetPositionAndRotation(floorPos, yaw);
            _envGo.transform.localScale = Vector3.one * rigScale;
        }

        _placedPoseVersion = VRRigDriver.RigPoseVersion;
        VRLog.Info("Core", $"Sky alternative: environment placed ({why}) — origin {floorPos} scale " +
                           $"{rigScale:F2} (rig-tracked, perceived-constant), yaw {yaw.eulerAngles.y:F1}deg " +
                           $"({(tracked ? "tracked head pose" : "rig pose fallback, head not tracked yet")}).");
    }

    /// <summary>
    /// Defensive scale re-sync (class doc PER-FRAME COST): the frame's scale must equal the
    /// rig scale at all times — <see cref="NotifyRigScaled"/> keeps it there through every
    /// known scale writer (WorldGrab two-hand pinch, Comfort.SetScaleMultiplier) and the
    /// re-seat covers rig builds. This heal only ever fires if a FUTURE writer scales the
    /// rig without notifying; it re-syncs about the head pivot (the view does not lurch —
    /// the same pivot rule Comfort.SetScaleMultiplier uses) so the invariant is self-righting
    /// rather than silently broken. Steady-state cost: two float reads and a compare.
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
    /// Mirror it onto the frame so the environment stays bit-frozen in the player's REAL
    /// frame — same perceived size, same perceived offset, a distant surrounding at every
    /// zoom — while drag/turn components of the same gesture pass through as movement
    /// (invariance proof in the class doc ZOOM ALGEBRA note). Cheap and re-entrant: two
    /// early-outs while no environment is shown, one transform write while one is.
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
    }

    // ---- deactivate ---------------------------------------------------------------------------

    /// <summary>Back to vanilla: re-enable the game sphere and destroy the frame root.
    /// The loaded prefab references stay (session-cached by design — class doc).</summary>
    private static void Deactivate()
    {
        if (!_active && _envGo == null && _hiddenSphere == null)
            return;

        RestoreGameSphere();
        if (_envGo != null)
        {
            Object.Destroy(_envGo); // frame root: the FX shell and everything under it
            _envGo = null;
        }
        _appliedStyle = SkyStyle.Default;
        _placedPoseVersion = int.MinValue; // a fresh activation always places fresh
        _nextHealLogTime = 0f;
        if (_active)
            VRLog.Info("Core", "Sky alternative OFF — game sphere restored, 3D environment despawned.");
        _active = false;
        _loggedActive = false;
    }
}
