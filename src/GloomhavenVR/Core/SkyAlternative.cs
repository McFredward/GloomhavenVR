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

    /// <summary>The nerd D&amp;D cellar room (bundle prefab Env_Cellar).</summary>
    Cellar = 1,

    /// <summary>Night swamp under a star dome with shooting stars (bundle prefab Env_Swamp).</summary>
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
/// WHAT IT DOES
/// ------------
/// A dial (<c>[Sky] Style</c>: Default / Cellar / SwampNight, curated in Grafik ▸ Darstellung)
/// picks the surroundings. Default is the game's own sky, bit-identical to today —
/// <see cref="SkyBackdrop"/> keeps making it a non-occluding backdrop and this class does
/// nothing. A non-Default choice:
///
///  1. HIDES the game's sky sphere (<c>GH_SkySphere</c>) via <c>renderer.enabled = false</c> —
///     unchanged from the panorama build. That is safe for PURE hiding — <see cref="SkyBackdrop"/>'s
///     class doc documents the trap precisely: suppressing a renderer only bites when a
///     CommandBuffer is supposed to REDRAW it, which nothing does here. The sphere is found
///     through <see cref="SkyBackdrop.FindSky"/> (one shared set of name/shader hints),
///     recorded, and re-enabled on restore.
///  2. SPAWNS the chosen environment prefab from the asset bundle (a parallel content lane
///     authors them against a fixed contract): <c>Env_Cellar</c> — a ~8x8 m self-lit D&amp;D
///     cellar room, floor at y≈-0.02, a free 1.5 m radius at its origin for the real table —
///     or <c>Env_Swamp</c> — swamp night: ground plane, tree ring, star dome, shooting-star
///     bursts, ground fog, fireflies. Both are self-lit (BoardLit/emissive — no scene-light
///     dependency) and self-animating (Shuriken only, no scripts). The instance lives on the
///     MOD LAYER (only the rig head camera renders it; game cameras and the FlatScreen
///     composites never see it) and has every collider stripped defensively — non-interactive
///     by ruling, and it must never catch a laser/poke ray.
///
/// ANCHORING — the key design change vs. the panorama. The old mod sphere center-followed the
/// HEAD every frame (the classic skybox illusion). A room/terrain must do the opposite: stand
/// still in REAL space like the play table does. The chosen anchor is
/// <see cref="VRRigDriver.RigRoot"/>, with local pose IDENTITY (origin position, identity
/// rotation, unit scale), because RigRoot IS the real-space frame of this mod:
///
///  - XR device poses (head, hands) are LOCAL to RigRoot ("Tracking-space root of the VR rig
///    ... XR device poses are local to this transform"), and tracking is floor-origin
///    (VRRigDriver.Recenter: "with floor-origin tracking headLocal.y ≈ eye height") — so
///    RigRoot's local space is the player's real room in METERS with y=0 on the REAL floor.
///    The prefabs are authored in real meters with the floor at y≈-0.02: parenting with unit
///    local scale puts the cellar floor a hair under the real floor, by construction.
///  - LEANING/WALKING moves the head WITHIN RigRoot; the environment's rig-local pose never
///    changes, so the room stands perfectly still in real space — the goal.
///  - The DIORAMA ZOOM (WorldGrab writes RigRoot.position/rotation/localScale) must not scale
///    the room. Under RigRoot the instance's WORLD size tracks the rig scale exactly — which
///    is precisely what keeps its REAL (perceived) size constant: 8 authored meters read as
///    8 real meters at any zoom, while the world/board rescales around the player. A
///    world-anchored room would do the opposite (shrink/grow with every zoom — the pinned
///    PlayTray needs world anchoring for the reverse reason: it must keep WORLD size). The
///    tray's FOLLOW mode anchors under this same frame (the hands root under the rig), which
///    is what "stands still like the play table" means here.
///  - RECENTER / spawn-ring seating teleports RigRoot; the environment rides along — the room
///    re-seats around the player's new seat, exactly like the rest of the real-space furniture.
///  - RIG REBUILD destroys RigRoot and its children, the environment included; the next
///    <see cref="Tick"/> sees the fake-nulled instance and respawns under the new root.
///    Rebuilds are rare, logged events — an occasional prefab instantiate is fine.
///
/// PER-FRAME COST: ZERO transform writes while active — parenting does all the work; the rig
/// root is the only thing that ever moves and Unity's hierarchy carries the children. The
/// active tick is: enum read, sphere-hidden check, instance/anchor null+reference checks.
/// Default/MR-on: an enum read plus an idempotent early-out. (The panorama's per-frame
/// head-follow + far-plane-resize writes DIED with the panorama.)
///
/// PARTICLES: prefab systems auto-play (playOnAwake) and RigRoot is always active, so no kick
/// is strictly needed — a defensive <c>Play()</c> runs anyway after instantiate. Two module
/// normalisations make Shuriken honour the anchor design (the content lane authors at scale 1
/// in an editor scene and cannot know the prefab lands under a diorama-scaled parent):
/// <c>scalingMode = Hierarchy</c> (the established pattern — see
/// <c>Net.RemoteControlBoard</c>'s pile FX) so sizes/speeds/shapes follow the rig scale and
/// stay authored-real-size, and World simulation space is switched to Local so in-flight
/// particles ride the anchor during a world-grab instead of smearing behind the room
/// (rotation is identity, so local axes equal world axes and nothing else changes).
///
/// FAR PLANE: the environment is real-size, so at rig scale S its farthest geometry sits up
/// to (authored meters × S) world units from the head — a star dome can exceed a small
/// scenario far plane. <see cref="MinFarWorldUnits"/> hands VRRigDriver.TickClipPlanes a
/// floor of <see cref="EnvMinFarMeters"/> real meters while active (0 when idle), still
/// capped by the depth-precision far/near ratio.
///
/// MR PRECEDENCE (the user's rule: MR ON ⇒ the sky is ALWAYS off): <see cref="MixedReality.Tick"/>
/// calls <see cref="StandDown"/> FIRST on its MR-on path — the environment despawns and the
/// game sphere is re-enabled so MR's own <c>HideSkyGeometry</c> sweep records and disables a
/// clean renderer for the chroma key, whatever the dial says. When MR turns off the dial's
/// choice re-applies on the next tick. On the MR-off path this ticks BEFORE SkyBackdrop, and
/// while an environment is shown SkyBackdrop stands down through the same parameter MR uses
/// (a hidden sphere needs no non-occluding treatment).
///
/// ASSETS: prefabs are loaded LAZILY from the asset bundle on the first non-Default selection
/// via the established probe pattern (<c>AssetBundle.GetAllLoadedAssetBundles()</c> +
/// <c>LoadAsset</c>), so a player who never touches the dial pays zero. A loaded prefab
/// reference is KEPT for the session (it is a reference into the loaded bundle, not a copy).
/// Missing prefab (older bundle) = one-shot warn, the game's own sky stays fully in place —
/// the same degradation as the panorama build.
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

    /// <summary>Bundle paths of the environment prefabs, indexed by <see cref="SkyStyle"/>
    /// (0 = Default = none). Contract fixed with the content lane building the bundle.</summary>
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

    // The spawned environment instance (a RigRoot child — dies with rig rebuilds, fake-null then).
    private static GameObject? _envGo;
    private static SkyStyle _appliedStyle = SkyStyle.Default;

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
            "Which surroundings the table sits in (user ruling 2026-08-12: real 3D environments " +
            "instead of the earlier panorama skyboxes — like small VRChat worlds, not interactive, " +
            "purely as surroundings). Default = the game's own animated sky, exactly as before. " +
            "Cellar = a cozy nerd D&D cellar room around the play space; SwampNight = a night " +
            "swamp under a star dome with shooting stars, ground fog and fireflies (both bundled " +
            "with the mod). A non-Default choice hides the game's sky sphere and spawns the " +
            "environment anchored to the REAL play space — it stands still while you lean or " +
            "walk, the world-grab zoom never changes its size, and it can never catch the laser " +
            "(no colliders, mod layer only). Applies live from the VR menu. MIXED REALITY ALWAYS " +
            "WINS: while MR is on, every sky and environment is off so the chroma key can show " +
            "your room; the choice re-applies when MR turns off. Values from the old panorama " +
            "builds (Night/Sunset) no longer exist and fall back to Default. Local presentation " +
            "only, never synced to peers.");
    }

    /// <summary>
    /// Far-plane floor for <c>VRRigDriver.TickClipPlanes</c>: while an environment is shown its
    /// real-size geometry needs (<see cref="EnvMinFarMeters"/> × rig scale) world units of view
    /// distance; 0 while idle (one branch — the caller's Max degenerates to its old value).
    /// </summary>
    internal static float MinFarWorldUnits(float rigScale) =>
        _active ? EnvMinFarMeters * rigScale : 0f;

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
            // Mid-rebuild frame (the rig is being torn down/rebuilt): stand down cleanly; the
            // next tick under the new root re-applies everything.
            Deactivate();
            return false;
        }

        HideGameSphere();
        EnsureEnvironment(style, anchor);

        if (!_active || !_loggedActive)
        {
            _active = true;
            _loggedActive = true;
            VRLog.Info("Core", $"Sky alternative ON — style {style}: the game's sky sphere is hidden " +
                               "(pure renderer.enabled hiding; SkyBackdrop stands down) and the bundled " +
                               "3D environment is spawned under the rig root (real-space anchor, identity " +
                               "local pose, mod layer, zero per-frame transform writes). MR overrides it off.");
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
                return; // no sphere in this scene (Menu2D) — the environment still shows
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
    /// Spawn (or keep) the environment instance under the rig root. Steady state is checks only —
    /// NO transform writes (the anchor carries the instance; class doc). Instantiates on first
    /// activation, on style switch, and after a rig rebuild fake-nulled the previous instance.
    /// </summary>
    private static void EnsureEnvironment(SkyStyle style, Transform anchor)
    {
        if (_envGo != null && _appliedStyle == style)
        {
            // Steady state. The parent check is one reference compare and only fires in the
            // theoretical gap where an old instance outlived its rig root (children normally
            // die WITH the root — then _envGo is fake-null and we fall through to a respawn).
            if (_envGo.transform.parent != anchor)
                ParentToAnchor(_envGo.transform, anchor);
            return;
        }

        if (_envGo != null)
        {
            Object.Destroy(_envGo); // style switch — the old room goes
            _envGo = null;
        }

        GameObject prefab = Prefabs[(int)style]!;
        _envGo = Object.Instantiate(prefab);
        _envGo.name = "GloomhavenVR.SkyAlternative." + style;
        ParentToAnchor(_envGo.transform, anchor);
        VRLayers.Apply(_envGo); // mod layer, recursive — head camera only (we are gated on IsRunning)

        // NON-INTERACTIVE BY RULING ("Nicht interaktiv rein als Umgebung"): the contract says
        // the prefabs ship without colliders, but a stray one would silently eat laser/poke
        // rays across the whole room — strip defensively, once, at spawn.
        Collider[] colliders = _envGo.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            Object.Destroy(c);

        // Shuriken normalisation + defensive kick (class doc PARTICLES): Hierarchy scaling so
        // the FX follow the rig's diorama scale like the meshes do; Local simulation space so
        // in-flight particles ride the anchor during a world-grab (identity rotation — local
        // axes equal world axes, nothing else changes). RigRoot is always active, so playOnAwake
        // already ran — the Play() is belt-and-braces for systems authored with it off.
        ParticleSystem[] systems = _envGo.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
            if (!ps.isPlaying)
                ps.Play(withChildren: false);
        }

        bool wasSwitch = _appliedStyle != SkyStyle.Default && _loggedActive;
        _appliedStyle = style;
        VRLog.Info("Core", $"Sky alternative: environment '{prefab.name}' spawned under rig root " +
                           $"'{anchor.name}' (identity local pose — real-space anchored, floor on the " +
                           $"real floor, diorama zoom never rescales it). {systems.Length} particle " +
                           $"system(s) normalised (Hierarchy scaling, local simulation space)" +
                           $"{(colliders.Length > 0 ? $", {colliders.Length} stray collider(s) stripped" : "")}." +
                           $"{(wasSwitch ? " (style switch)" : "")}");
    }

    /// <summary>Identity local pose under the rig root: position at the play-space origin (the
    /// recentered real floor), rotation identity, unit scale (prefab meters = real meters).</summary>
    private static void ParentToAnchor(Transform t, Transform anchor)
    {
        t.SetParent(anchor, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
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
            Object.Destroy(_envGo);
            _envGo = null;
        }
        _appliedStyle = SkyStyle.Default;
        if (_active)
            VRLog.Info("Core", "Sky alternative OFF — game sphere restored, 3D environment despawned.");
        _active = false;
        _loggedActive = false;
    }
}
