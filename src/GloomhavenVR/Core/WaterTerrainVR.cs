using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core;

/// <summary>
/// THE MIRRORING FLOOR — user report 2026-08-15 (spiegeltiles.jpg), amended 2026-08-18.
/// Original: "In dem Level das wir gespielt haben wurde die Bodentiles eines Raumes nicht richtig
/// dargestellt. Stattdessen war dort eine Fläche zu sehen die spiegelt und sie mit den
/// Kopfbewegungen ändert." Amendment (user ruling, verbatim): <i>"Wichtige Ergänzung: Das Wasser
/// soll auf jeden Fall dargstellt werden - aber eben in einer VR-freundlichen Variante. Einfach
/// ausblenden ist keine Option."</i>
///
/// <para>THAT RULING IS THE SHAPE OF THIS FILE. ModBuild 158 hid the quads. That path is GONE —
/// this driver never writes <c>Renderer.enabled</c> and carries no hide dial at all. The water
/// renders; what changes is HOW.</para>
///
/// <para>WHAT THE SURFACE IS — measured in-process, not inferred. The census this driver ships
/// printed it on hardware (<c>LogOutput.log:942</c>, ModBuild 158): the quads are named
/// <c>TERRAIN_Water_Plane</c>, they run material <c>TERRAIN_GEN_WaterPlane_Crypt_Mat</c> on the
/// game's shader <c>VFX/Water_Shd_Trans</c> at renderQueue 2900, and there are 17 of them lying
/// on the floor plane over the tileset's own sunken <c>TERRAIN_Crypt_Water_02_Base</c> basin and
/// its <c>_Edge</c> rim. It is the game's WATER TERRAIN and it is meant to be there: an animated,
/// depth-faded, edge-foamed water surface (<c>_Normal_Map=WaterBump</c>, <c>_WaterUVAnimSpeedA/B</c>,
/// <c>_WaveFrequency=3</c>, <c>_WaveSpeed=2</c>, <c>_VertexOffsetWaves=0.05</c>).</para>
///
/// <para>WHERE THE MIRROR COMES FROM — the same census line names the mechanism, and it is not the
/// water's fault. <c>ENVIRONMENT: reflectionMode=Skybox reflectionIntensity=0.5
/// customReflection=&lt;null&gt; liveProbes=0</c>, against a material at <c>_Smoothness = 0.754</c>
/// and a renderer at <c>probeUsage=BlendProbes</c>. A near-smooth surface asking for a reflection
/// probe, in a scene that has NOT ONE, samples the only environment the engine can hand it: the
/// skybox cubemap of <c>GH_Evil_Sky_MAT</c>. The flat game looks almost straight down at this
/// pool, so the reflection vector points into the dark part of that sky and the term reads as a
/// faint sheen. Across a VR table the same pool is seen at GRAZING angles from a head that moves,
/// so the reflection vector sweeps the bright part of the sky and swings with every head motion —
/// a mirror that swims. That is "kaputter Spiegel … ändert sich mit den Kopfbewegungen", exactly,
/// and it is a reflection-environment problem, not a stereo problem.</para>
///
/// <para>WHY YOU COULD NOT SEE THE FLOOR THROUGH IT — the second, independent cause, also from the
/// same line. <c>CAMERAS: … head='GloomhavenVR.HeadCamera' depthTextureMode=None</c>. The shader's
/// depth fade and shore foam (<c>_Edge_Distance=0.2</c>, <c>_Edge_Colour_Distance=0.9</c>,
/// <c>_Edge_Colour</c> = near-white RGBA(0.887,0.887,0.887,0.867), <c>_EdgeColour_Toggle=1</c>
/// with keyword <c>_EDGECOLOUR_TOGGLE_ON</c> live, <c>_InvertDepthFade=0</c>) all read
/// <c>_CameraDepthTexture</c>. Our head camera does not write one: <c>Defaults.HeadDepthPrepass</c>
/// is <c>false</c> since the 2026-07 submission-cost pass, because on the built-in FORWARD path
/// that texture costs a whole extra opaque scene submission PER EYE. With no depth texture the
/// fade term is pinned at one extreme for the entire quad — either full deep tint at
/// <c>_Color_Tint</c> alpha 0.737, or full near-white foam — so the surface reads as a flat sheet
/// that hides the tiles instead of a film that reveals them. <c>Camera.main='ScenarioCamera'</c> is
/// PARKED under VR, so nothing else is writing a usable depth texture either.</para>
///
/// <para>WHAT THIS DRIVER DOES ABOUT IT — a per-renderer <see cref="MaterialPropertyBlock"/> on the
/// game's own quads, carrying values derived from the material's OWN authored ones. Nothing is
/// hidden, no shared material is written, no shader is replaced, and every animated term the eye
/// reads as water — the normal-map scroll, the wave frequency and speed, the vertex ripple, the
/// tint hue — is left exactly as authored. Three things change:</para>
/// <list type="number">
///   <item><b>The sky mirror is killed at its source.</b> <c>_Smoothness</c> 0.754 →
///   <c>[Water] Smoothness</c> (0.08). Whether the shader spends that value on an environment mip
///   or on a specular exponent, the effect is the same in both readings: the lobe widens from a
///   mirror into a broad low-contrast sheen, so the sampled sky collapses to roughly its average
///   colour and stops swinging with the head. It is a MATERIAL CONSTANT, identical in both eyes —
///   deliberately NOT the screen-space shape that parked the masonry fade
///   (<c>.planning/wall-fade-stereo-rivalry.md</c>).</item>
///   <item><b>The floor reads through again.</b> <c>_Color_Tint</c> keeps its authored RGB; its
///   alpha is capped at <c>[Water] Opacity</c> (0.45, down from 0.737). A shallow crypt pool you
///   can see the bottom of is what the report asked for, and lowering the tint's alpha is the
///   "less opaque" direction under every plausible reading of how that channel is spent.</item>
///   <item><b>The broken depth term can no longer paint a white sheet.</b> While the head camera
///   has no depth texture, <c>_Edge_Colour</c> is set to the BODY colour and
///   <c>_EdgeColour_Toggle</c> to 0 — so whatever the unfed depth fade computes, foam and body are
///   the same colour and the artifact has nothing to draw with. This is deliberately a COLOUR
///   neutralisation and not a distance one: zeroing <c>_Edge_Distance</c> would depend on the sign
///   of a term we cannot read (the shader ships compiled inside <c>always_loaded_base_high</c>,
///   Player.log:1621), and could just as easily pin the foam ON. Colour equality is
///   sign-independent.</item>
/// </list>
///
/// <para>AND THE FOAM CAN COME BACK, MEASURED. <c>[Water] ShoreFoam</c> (default OFF) makes this
/// driver REQUEST <see cref="DepthTextureMode.Depth"/> on the head camera — but only while water
/// terrain is actually tracked, so the cost is confined to water rooms instead of the whole game.
/// <see cref="Rig.VRRigDriver"/>'s existing depth-mode tick honours the request beside
/// <c>[Optimize] HeadDepthPrepass</c>. When the driver observes the bit actually granted (it reads
/// the camera, it does not assume its own request took), it stops neutralising the edge colour and
/// hands the authored foam back — under MultiPass each eye renders its own pass and therefore its
/// own depth texture, so the foam is per-eye CORRECT, not per-eye rivalrous. COST, STATED NOT
/// MEASURED: one extra full opaque scene submission per eye pass, which this project's own
/// <see cref="Rig.VRRigDriver"/> notes call the largest single piece of submission volume the mod
/// adds. That is why it is opt-in and why the default path above is built to look right WITHOUT
/// it.</para>
///
/// <para>WHAT WAS CONSIDERED AND NOT BUILT, so the next round does not re-derive it. (a) A LOCAL
/// REFLECTION PROBE over the basin would let the water reflect the room instead of the evil sky
/// and would allow a HIGHER smoothness to be kept — the renderer already asks for one
/// (<c>probeUsage=BlendProbes</c>), so it would be picked up with no material write at all. It is
/// the right next dial if the user wants a sharper sheen back; it is not built here because at
/// <c>[Water] Smoothness</c> 0.08 the environment sample is already collapsed to a flat average and
/// a probe would contribute almost nothing, while adding spawned scene content and a cubemap render
/// that no hardware run has ever exercised. The census below reports whether any probe reaches the
/// water, which is the reading that would justify building it. (b) REPLACING the material with a
/// mod-owned water shader from the bundle is the standing fallback if the game shader turns out to
/// be uncorrectable; it needs a bundle lane (<c>unity/**</c> + <c>Core/BundleShaders.cs</c>) that
/// this lane does not own.</para>
///
/// <para>THE FLICKER OF ModBuild 158, AND ITS ACTUAL CAUSE — found, not guessed. That build set
/// <c>Renderer.enabled = false</c> and re-surveyed on a slow round-robin, and the user saw the
/// quads "immer mal wieder aufflackernd". Nothing in the game re-enables those renderers. THEY ARE
/// DESTROYED AND RE-INSTANTIATED: <c>TERRAIN_Water_Plane</c> is an Apparance prefab instance, and
/// every content rebuild of the owning <c>ApparanceEntity</c> destroys the old objects
/// (<c>ApparanceEntity.Clear</c>/<c>ClearObject</c>, <c>IObjectPlacement.DestroyObject</c> →
/// <c>DestroyImmediate</c>) and instantiates the prefab again
/// (<c>ApparanceEntity.CreateInstance</c> → <c>Object.Instantiate(template…)</c>, which is also
/// where the name comes from). There is no pooling anywhere in <c>Apparance.Unity.dll</c> — it
/// never writes <c>Renderer.enabled</c> at all. The new instance arrives with the prefab's authored
/// state, and ModBuild 158's cursor did not come back round to that tile for up to 2.75 s. Rebuilds
/// are frequent because <c>ApparanceEngine.UpdateEngine</c> feeds the native engine a VIEW POSITION
/// every frame and drives detail focus from it, and because
/// <c>ProceduralTileObserver.Update</c>/<c>OnEnable</c>/<c>NotifyTile</c> flag a rebuild on every
/// activity or bounds change.</para>
///
/// <para>SO THE RACE IS CLOSED FROM BOTH ENDS. First, nothing here writes <c>enabled</c>, so there
/// is no visibility state to lose — a quad that respawns before we reach it renders as the game
/// authored it (water, just with the sky mirror back for a moment), never as a hole in the floor.
/// Second, the respawn now has an EVENT rather than a poll:
/// <see cref="ProceduralBase_NotifyContentPlacementComplete_WaterPatch"/> postfixes the one method
/// Apparance calls when it has finished (re)placing an entity's objects
/// (<c>ProceduralBase.NotifyContentPlacementComplete</c>, which both overrides —
/// <c>ProceduralMapTile</c> and <c>ProceduralProp</c> — call through). The affected subtree is
/// queued and retuned in the SAME frame's LateUpdate, before it is ever submitted. The sweep uses
/// <c>includeInactive: true</c> so a quad placed while its room is still in <c>Preview</c> (its
/// content is <c>SetActive(false)</c> until <c>ProceduralMapTile.ShowContent</c> reveals it) is
/// tuned before it is switched on, rather than after.</para>
///
/// <para>WHAT REMAINS IS A PROPERTY BLOCK, which the game has no reason to clear; it is
/// nevertheless ENFORCED, and the enforcement is instrumented so the next log can prove or refute
/// that claim: a per-frame <see cref="Renderer.HasPropertyBlock"/> compare over the small tracked
/// set (17 native calls per frame in the report's room, no allocation, no component walk) plus a
/// full re-assert on every slow tick. Both count into <c>reasserts=</c> in the census line. If that
/// number stays at 0 on hardware, nothing is fighting us; if it climbs, the census says how fast
/// and the seam becomes findable instead of guessed at. One known writer of these renderers exists
/// and is harmless to us now: <c>MaterialLoaderData.CheckAllMaterialLoaded</c> is the only
/// unconditional <c>Renderer.enabled = true</c> in the whole game assembly (it also swaps
/// <c>sharedMaterials</c> on an Addressables callback) — it was a live race for ModBuild 158's
/// hide, and is none for a property block, which survives a material swap.</para>
///
/// <para>SCOPE. Only renderers on the game's water shader family (the <c>Water_Sh</c> stem — the
/// same key <see cref="WallSegmentFade"/>'s fountain exemption uses, so there is one definition of
/// "this is water" in the mod) AND named for the terrain prop family
/// (<see cref="TerrainWaterNameTokens"/>). The name term keeps DECORATIVE water out: the
/// fountain/pond props of the <c>brunnen.png</c> ruling (<c>FR_SW_Pond_Small</c>,
/// <c>P_Waterfall_Circle_Small</c>) are set dressing nobody stands on and are not what the report
/// is about.</para>
///
/// <para>MULTIPLAYER: purely local rendering. A property block is per-renderer and per-process, no
/// shared material is written, nothing crosses the wire, and each peer decides for itself — the
/// report's three machines all logged the same 17 planes. REVERSIBLE: <c>SetPropertyBlock(null)</c>
/// on config change, scene teardown and uninstall puts every quad back to the authored material
/// bit-for-bit. TickGuard-safe.</para>
/// </summary>
internal static class WaterTerrainVR
{
    /// <summary>Log scope — lines read <c>[Compat] WATER SURFACE …</c> so this greps together
    /// with the other game-rendering fixups it ships beside.</summary>
    private const string Name = "Compat";

    private const string DriverName = "GloomhavenVR.WaterTerrainVR";

    /// <summary>The game's four water shaders all share this stem (<c>Water_Shd</c>,
    /// <c>Water_Shd_Trans</c>, <c>Water_Shr_Low</c>, <c>Water_Shr_Trans_Low</c>; they load from
    /// <c>always_loaded_base*</c>, Player.log:1621). Same key
    /// <c>WallSegmentFade.FadeDriver.IsWaterSurface</c> uses, deliberately.</summary>
    private const string WaterShaderStem = "Water_Sh";

    /// <summary>Authored name family of the game's water TERRAIN props — the hex-sized quads a
    /// figure stands in, as opposed to decorative ponds and fountains. <c>TERRAIN_Water_Plane</c>
    /// is what the 2026-08-15 report's room places (17 of them); the shorter
    /// <c>TERRAIN_Water</c> token covers a sibling set that names its quad differently. Extend
    /// this array if a tileset ever ships terrain water under another name — that is the only
    /// change such a set should need.</summary>
    private static readonly string[] TerrainWaterNameTokens = { "TERRAIN_Water", "Terrain_Water" };

    // --- the four properties this driver writes, by id (name→id resolution is a dictionary
    //     lookup in Unity; resolving once at type load keeps the per-frame path free of it).
    //     Every one of them was READ OFF THE LIVE MATERIAL on hardware (LogOutput.log:942), so
    //     these names are measured, not guessed from a shader we cannot open.
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int ColorTintId = Shader.PropertyToID("_Color_Tint");
    private static readonly int EdgeColourId = Shader.PropertyToID("_Edge_Colour");
    private static readonly int EdgeToggleId = Shader.PropertyToID("_EdgeColour_Toggle");

    private static Driver? _driver;

    /// <summary>
    /// Does the water terrain currently need <c>_CameraDepthTexture</c>? Read once per frame by
    /// <see cref="Rig.VRRigDriver"/>'s depth-mode tick, which ORs this beside
    /// <c>[Optimize] HeadDepthPrepass</c>. TRUE only while <c>[Water] ShoreFoam</c> is on AND at
    /// least one water quad is actually tracked, so a scenario without water never pays for it —
    /// the request follows the content, which is the only reason a per-eye depth prepass is
    /// defensible at all here. Two field reads; safe before <see cref="Install"/>.
    /// </summary>
    internal static bool WantsDepthTexture => _driver != null && _driver.NeedsDepth;

    /// <summary>Apparance has just finished (re)placing this entity's objects — any water quad
    /// under it is a BRAND NEW instance carrying the prefab's authored material state. Queue the
    /// subtree; the driver retunes it in the same frame's LateUpdate, before it is submitted.
    /// Called from the Harmony postfix only, and a no-op once uninstalled.</summary>
    internal static void NotifyContentPlaced(ProceduralBase entity) =>
        _driver?.QueueSubtree(entity);

    /// <summary>Install the driver (idempotent). No-op when VR isn't running — on a flat screen
    /// the game's water is exactly what its authors saw at the one camera pitch they authored it
    /// for, and nothing here applies.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        WaterConfig.Bind();
        var go = new GameObject(DriverName);
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<Driver>();
        // The respawn seam (see the class header): Apparance destroys and re-instantiates these
        // quads, so polling for them is a race by construction. Idempotent — Harmony no-ops a
        // second PatchAll of the same class, and the postfix itself no-ops once _driver is null.
        VRSession.Harmony?.PatchAll(typeof(ProceduralBase_NotifyContentPlacementComplete_WaterPatch));
        VRLog.Info(Name,
            "WaterTerrainVR installed — the game's water TERRAIN quads ('TERRAIN_Water_Plane', "
            + "shader family 'Water_Sh*') KEEP RENDERING (user ruling 2026-08-18: hiding is not an "
            + "option) and are retuned for a free camera through a per-renderer property block: "
            + "_Smoothness down so the skybox stops acting as a mirror at grazing angles, "
            + "_Color_Tint alpha capped so the floor reads through, and the depth-fed shore foam "
            + "neutralised while the head camera writes no _CameraDepthTexture. See the WATER "
            + "SURFACE census lines for the measured material, the reflection environment, which "
            + "camera feeds the depth term, and how often the block had to be re-asserted.");
    }

    /// <summary>Drop the driver, clearing every property block we wrote first.</summary>
    public static void Uninstall()
    {
        if (_driver == null)
            return;
        try
        {
            _driver.RestoreAll();
            Object.Destroy(_driver.gameObject);
        }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    /// <summary>Is this renderer one of the game's water TERRAIN quads? Shader family AND name
    /// family — see the class header for why the name term is there.</summary>
    private static bool IsTerrainWater(Renderer r, List<Material> matScratch)
    {
        if (r == null)
            return false;
        string n = r.name;
        bool named = false;
        foreach (string token in TerrainWaterNameTokens)
        {
            if (n.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                named = true;
                break;
            }
        }
        if (!named)
            return false;
        matScratch.Clear();
        r.GetSharedMaterials(matScratch);
        foreach (Material m in matScratch)
        {
            if (m != null && m.shader != null
                && m.shader.name.IndexOf(WaterShaderStem, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Module-owned config (the ModuleConfig pattern — the VR menu enumerates it
    /// automatically, so every dial here is reachable in-headset without touching a file, and the
    /// look can be ruled on live rather than rebuilt for).</summary>
    internal static class WaterConfig
    {
        private static ConfigFile? _file;

        /// <summary>ON (default): retune the game's water terrain for a free VR camera.
        /// OFF: the game's water renders exactly as authored, sky mirror and all.</summary>
        internal static ConfigEntry<bool>? VRFriendlyWater;

        /// <summary>The mirror dial. Authored 0.754.</summary>
        internal static ConfigEntry<float>? Smoothness;

        /// <summary>Upper bound on <c>_Color_Tint</c>'s alpha. Authored 0.737.</summary>
        internal static ConfigEntry<float>? Opacity;

        /// <summary>Request the head camera's depth texture so the authored shore foam works.</summary>
        internal static ConfigEntry<bool>? ShoreFoam;

        internal static void Bind()
        {
            if (_file != null)
                return;
            ConfigFile config = _file = ModuleConfig.Create("water");
            VRFriendlyWater = config.Bind("Water", "VRFriendlyWater", true,
                "Retune the game's water TERRAIN (TERRAIN_Water_Plane, shader VFX/Water_Shd*) for "
                + "a free VR camera. The water always RENDERS — this only changes how. The game's "
                + "water is authored for one fixed steep top-down camera pitch; across a VR table "
                + "it is seen at grazing angles, where its near-mirror smoothness reflects the "
                + "skybox and swings with the head (report 2026-08-15 spiegeltiles.jpg). ON writes "
                + "a per-renderer property block: lower smoothness, capped tint alpha, and the "
                + "depth-fed shore foam neutralised while no depth texture exists. The shared "
                + "material is never touched. OFF restores the game's water immediately.");
            Smoothness = config.Bind("Water", "Smoothness", 0.08f,
                new ConfigDescription(
                    "Water smoothness in VR (the game authors 0.754). This is THE mirror dial: "
                    + "high values make the surface sample the skybox sharply, which is what reads "
                    + "as a broken mirror that moves with your head, because the scene has no "
                    + "reflection probe to offer instead. Low values widen the lobe into a broad "
                    + "sheen. Raise it only if a local reflection probe is ever added over the "
                    + "basin.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Opacity = config.Bind("Water", "Opacity", 0.45f,
                new ConfigDescription(
                    "Upper bound on the water tint's alpha (the game authors 0.737). The tint's "
                    + "hue is left as authored; only how much of the floor it hides is capped, "
                    + "because with no camera depth texture the shader's depth fade is pinned at "
                    + "its deepest and tints the whole quad. Lower = you see more of the tiles "
                    + "under the pool. Never raises the authored value.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ShoreFoam = config.Bind("Water", "ShoreFoam", false,
                "Ask the VR head camera for a depth texture while water terrain is on screen, so "
                + "the game's authored shore foam and depth fade work instead of being "
                + "neutralised. COSTS ONE EXTRA FULL OPAQUE SCENE SUBMISSION PER EYE while water "
                + "is tracked (the built-in forward path has no G-buffer, so Unity builds the "
                + "depth texture by re-rendering every opaque object) — the same cost as "
                + "[Optimize] HeadDepthPrepass, which this ORs with rather than overriding. Off by "
                + "default: the water is built to look right without it. Water rooms only.");
            AnnounceRetiredHideKey(config);
        }

        /// <summary>
        /// <c>[Water] HideTerrainWaterInVR</c> — ModBuild 158's hide switch — was DELETED by the
        /// user ruling of 2026-08-18 ("Einfach ausblenden ist keine Option"). It is the only config
        /// key this module has ever removed, and <c>scripts/check-surface.py</c> is right that a
        /// removed key is a regression BECAUSE IT IS SILENT: the player's tuned value simply stops
        /// being read and their setting reverts with no message. So this removal is not silent.
        ///
        /// <para>BepInEx keeps an unbound entry verbatim — <c>ConfigFile.Save</c> concatenates
        /// <c>OrphanedEntries</c> back into the file — so a tester who set it still has the line in
        /// <c>dev.gloomhavenvr.water.cfg</c> and would otherwise have no way to learn it is inert.
        /// This says so once, names the dials that replaced it, and is read through reflection
        /// because <c>OrphanedEntries</c> is private on the BepInEx build we reference (a failure
        /// to read it costs nothing but this line).</para>
        /// </summary>
        private static void AnnounceRetiredHideKey(ConfigFile config)
        {
            try
            {
                var orphans = AccessTools.Field(typeof(ConfigFile), "OrphanedEntries")
                    ?.GetValue(config) as System.Collections.IDictionary;
                if (orphans == null)
                    return;
                foreach (System.Collections.DictionaryEntry e in orphans)
                {
                    if (e.Key is not ConfigDefinition def
                        || !string.Equals(def.Key, "HideTerrainWaterInVR", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    VRLog.Info(Name,
                        "WATER SURFACE: RETIRED CONFIG KEY [Water] HideTerrainWaterInVR = '"
                        + e.Value + "' is still in dev.gloomhavenvr.water.cfg and NOTHING READS IT "
                        + "ANY MORE. Hiding the game's water was removed by user ruling 2026-08-18 "
                        + "('Das Wasser soll auf jeden Fall dargstellt werden - aber eben in einer "
                        + "VR-freundlichen Variante. Einfach ausblenden ist keine Option.'). The "
                        + "water always renders now; what replaced that switch is [Water] "
                        + "VRFriendlyWater (master), [Water] Smoothness (the sky-mirror dial), "
                        + "[Water] Opacity (how much of the floor reads through) and [Water] "
                        + "ShoreFoam. The old line is harmless and can be deleted.");
                    return;
                }
            }
            catch { /* diagnostics only — a private field that moved must never block a bind */ }
        }
    }

    private sealed class Driver : MonoBehaviour
    {
        /// <summary>Discovery cadence. Slow on purpose: this must never become a per-frame scene
        /// sweep (the PERF S1 lesson — every full FindObjectsOfType cost 10-15 ms in a
        /// 3000-renderer room and was the measured cause of the hitches). ENFORCEMENT runs every
        /// frame, but only over the already-tracked set and without touching a component walk.</summary>
        private const float TickInterval = 0.25f;

        /// <summary>Census restatement floor — the heavy line is worth one every 30 s at most.</summary>
        private const float CensusInterval = 30f;

        /// <summary>Round-robin cursor over the map-tile registry: ONE tile examined per tick in
        /// the steady state. A reveal is caught by <see cref="_lastTileCount"/> instead of by
        /// waiting for the cursor to come round, so new water is retuned within one tick of the
        /// tile appearing rather than within a full lap.</summary>
        private int _cursor;

        private float _next;
        private float _nextCensus;

        /// <summary>Tile count at the last sweep. A change means content appeared or went — the
        /// one moment a FULL pass over every tile is worth its cost, and the moment ModBuild 158's
        /// round-robin was too slow for.</summary>
        private int _lastTileCount = -1;

        /// <summary>Every quad we touched → the AUTHORED values we derive from, snapshotted at
        /// first touch so a re-assert never compounds and a restore is exact.</summary>
        private readonly Dictionary<Renderer, Authored> _touched = new(32);

        /// <summary>Same set as <see cref="_touched"/>, in list form: the per-frame enforcement
        /// walk must not allocate an enumerator over a dictionary every frame.</summary>
        private readonly List<Renderer> _tracked = new(32);

        private readonly List<ProceduralMapTile> _tileScratch = new(16);
        private readonly List<Renderer> _rendererScratch = new(64);
        private readonly List<Material> _matScratch = new(4);

        /// <summary>Subtrees Apparance has just (re)placed content into, queued by the postfix and
        /// drained in the same frame's LateUpdate. Capped: a rebuild storm must degrade into one
        /// bounded sweep, never into an unbounded queue that grows faster than it drains.</summary>
        private readonly List<Transform> _pending = new(16);

        private const int PendingCap = 64;

        /// <summary>Set when <see cref="_pending"/> overflowed — the next tick sweeps every map
        /// tile once instead of trying to remember which ones moved.</summary>
        private bool _pendingOverflow;

        /// <summary>How many placement events the postfix delivered, and how many of them the
        /// queue had to collapse into a full sweep. Both go in the census: they say whether the
        /// event seam is carrying the load or whether the fallback poll is doing the work.</summary>
        private int _placements;
        private int _overflows;

        private readonly MaterialPropertyBlock _mpb = new();

        /// <summary>Materials already dumped in full (one heavy line each, cap 3).</summary>
        private readonly HashSet<string> _censused = new();

        /// <summary>A material we have dumped, kept for the periodic state line.</summary>
        private Renderer? _censusSample;

        /// <summary>Last applied mode, so a live config flip re-applies or restores exactly once.</summary>
        private bool _appliedTune;

        /// <summary>Whether the head camera was OBSERVED to carry the Depth bit at the last
        /// re-assert. Read from the camera, never assumed from our own request — the request can
        /// be refused by a rig that has no head camera yet, and the shore-foam decision has to
        /// follow what is actually true.</summary>
        private bool _depthGranted;

        /// <summary>Config values the tracked set was last written with, so a live dial change
        /// re-applies without a per-frame value compare on every renderer.</summary>
        private float _appliedSmoothness = float.NaN;
        private float _appliedOpacity = float.NaN;

        /// <summary>How many times a tracked quad was found WITHOUT our property block and had to
        /// be re-written. This is the number that says whether anything in the game is fighting
        /// us — the question ModBuild 158's flicker left open.</summary>
        private int _reasserts;

        /// <summary>Reassert count at the last census line, so the line can say the RATE.</summary>
        private int _lastCensusReasserts;

        private Action? _tick;

        /// <summary>The authored values a quad's own material shipped with. Everything this driver
        /// writes is derived from these, so the retune is a bounded transform of the game's own
        /// look rather than a set of invented constants.</summary>
        private readonly struct Authored
        {
            internal readonly Color Tint;
            internal readonly Color EdgeColour;
            internal readonly float EdgeToggle;
            internal readonly float Smoothness;

            internal Authored(Color tint, Color edgeColour, float edgeToggle, float smoothness)
            {
                Tint = tint;
                EdgeColour = edgeColour;
                EdgeToggle = edgeToggle;
                Smoothness = smoothness;
            }
        }

        private void Awake()
        {
            _appliedTune = Want;
            _tick = Tick;
        }

        private static bool Want =>
            VRSession.IsRunning
            && WaterConfig.VRFriendlyWater != null
            && WaterConfig.VRFriendlyWater.Value;

        /// <summary>See <see cref="WantsDepthTexture"/>. Content-gated on purpose.</summary>
        internal bool NeedsDepth =>
            _tracked.Count > 0
            && WaterConfig.ShoreFoam != null
            && WaterConfig.ShoreFoam.Value;

        private static float WantedSmoothness =>
            WaterConfig.Smoothness != null ? WaterConfig.Smoothness.Value : 0.08f;

        private static float WantedOpacity =>
            WaterConfig.Opacity != null ? WaterConfig.Opacity.Value : 0.45f;

        /// <summary>LateUpdate, not Update: this is the LAST word before the frame is submitted,
        /// so anything the game does to these renderers during its own Update is already done when
        /// we look. Cost in the report's room is 17 null compares and 17
        /// <see cref="Renderer.HasPropertyBlock"/> calls — no allocation, no component walk, no
        /// scene query — plus the quarter-second discovery tick.</summary>
        private void LateUpdate() => TickGuard.Run("Compat.WaterTerrainVR", _tick!, Name);

        private void Tick()
        {
            bool want = Want;
            if (want != _appliedTune)
            {
                _appliedTune = want;
                if (!want)
                {
                    RestoreAll();
                    VRLog.Info(Name,
                        "WATER SURFACE: [Water] VRFriendlyWater turned OFF — every water terrain "
                        + "quad's property block cleared; the game's own water shader renders "
                        + "again exactly as on a flat screen, sky mirror included.");
                    return;
                }
            }
            if (!want)
                return;

            EnforceEveryFrame();
            DrainPending();

            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + TickInterval;

            Discover();
            ReassertAll();
            MaybeLogState();
        }

        /// <summary>Queue a subtree Apparance just placed content into. Cheap and allocation-free
        /// on the hot path: a linear scan of a list that is empty in the steady state and holds a
        /// handful of entries during a rebuild.</summary>
        internal void QueueSubtree(ProceduralBase entity)
        {
            if (entity == null || !Want)
                return;
            _placements++;
            if (_pendingOverflow)
                return;
            if (_pending.Count >= PendingCap)
            {
                _pending.Clear();
                _pendingOverflow = true;
                _overflows++;
                return;
            }
            Transform t = entity.transform;
            for (int i = 0; i < _pending.Count; i++)
            {
                if (ReferenceEquals(_pending[i], t))
                    return;
            }
            _pending.Add(t);
        }

        /// <summary>Retune everything Apparance placed this frame, IN this frame. Empty on almost
        /// every frame — the whole cost in the steady state is one <c>Count == 0</c> compare.</summary>
        private void DrainPending()
        {
            if (_pendingOverflow)
            {
                _pendingOverflow = false;
                _pending.Clear();
                _lastTileCount = -1; // force the next Discover() into a full pass
                _next = 0f;          // …and let it happen on this tick rather than the next
                return;
            }
            if (_pending.Count == 0)
                return;
            for (int i = 0; i < _pending.Count; i++)
            {
                Transform t = _pending[i];
                if (t != null)
                    ExamineSubtree(t);
            }
            _pending.Clear();
        }

        /// <summary>The cheap per-frame guard. A property block the game cleared is the only way
        /// our retune can silently stop applying, and <see cref="Renderer.HasPropertyBlock"/>
        /// answers that without reading the block back. Dead entries are pruned here, which is
        /// also where the tracked count follows content going away.</summary>
        private void EnforceEveryFrame()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Renderer r = _tracked[i];
                if (r == null)
                {
                    _touched.Remove(r!); // by the SAME reference — a destroyed object still hashes
                    _tracked.RemoveAt(i);
                    continue;
                }
                if (r.HasPropertyBlock())
                    continue;
                _reasserts++;
                if (_touched.TryGetValue(r, out Authored a))
                    Apply(r, a);
            }
        }

        /// <summary>One tile per tick in the steady state; EVERY tile on the tick after the tile
        /// count moved. That is what makes a room reveal retune its water within a quarter second
        /// instead of within a full lap of the registry — the gap ModBuild 158's flicker lived in.
        /// The full pass costs one GetComponentsInChildren per tile and happens only on a content
        /// change, which already carries seconds of reveal animation.</summary>
        private void Discover()
        {
            SceneRegistry.MapTiles.Collect(_tileScratch);
            int count = _tileScratch.Count;
            if (count == 0)
            {
                _lastTileCount = 0;
                return;
            }
            bool sweepAll = count != _lastTileCount;
            _lastTileCount = count;

            if (sweepAll)
            {
                for (int i = 0; i < count; i++)
                    ExamineTile(_tileScratch[i]);
                _cursor = 0;
                return;
            }
            if (_cursor >= count)
                _cursor = 0;
            ExamineTile(_tileScratch[_cursor++]);
        }

        private void ExamineTile(ProceduralMapTile tile)
        {
            if (tile == null)
                return;
            ExamineSubtree(tile.transform);
        }

        /// <summary>
        /// Find and retune every water quad under one root. <c>includeInactive: true</c> is
        /// deliberate and is half the flicker fix: a room in <c>Preview</c> keeps its generated
        /// content <c>SetActive(false)</c> until <c>ProceduralMapTile.ShowContent</c> reveals it,
        /// so a sweep that skipped inactive objects could only ever meet those quads AFTER they
        /// were already on screen. Tuning an inactive renderer's property block is free and lands
        /// before its first submitted frame.
        /// </summary>
        private void ExamineSubtree(Transform root)
        {
            _rendererScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, _rendererScratch);
            foreach (Renderer r in _rendererScratch)
            {
                if (r == null || _touched.ContainsKey(r) || !IsTerrainWater(r, _matScratch))
                    continue;
                Material? mat = r.sharedMaterial;
                var a = new Authored(
                    mat != null && mat.HasProperty(ColorTintId) ? mat.GetColor(ColorTintId) : Color.white,
                    mat != null && mat.HasProperty(EdgeColourId) ? mat.GetColor(EdgeColourId) : Color.white,
                    mat != null && mat.HasProperty(EdgeToggleId) ? mat.GetFloat(EdgeToggleId) : 0f,
                    mat != null && mat.HasProperty(SmoothnessId) ? mat.GetFloat(SmoothnessId) : 0f);
                _touched[r] = a;
                _tracked.Add(r);
                // Unity's == , not ??= : the quads are destroyed and re-instantiated constantly
                // (see the class header), so a C#-non-null sample can be a destroyed object and
                // the census would lose its probe/bounds reading for the rest of the session.
                if (_censusSample == null)
                    _censusSample = r;
                LogWaterSurfaceOnce(r);
                Apply(r, a);
            }
            _rendererScratch.Clear();
        }

        /// <summary>Re-apply to the whole tracked set when a dial moved, when the head camera's
        /// depth bit changed under us, or once per slow tick as a cheap belt-and-braces against a
        /// property block the game REPLACED rather than cleared (which
        /// <see cref="Renderer.HasPropertyBlock"/> cannot see). 17 native writes per 250 ms in the
        /// report's room.</summary>
        private void ReassertAll()
        {
            Camera? head = Rig.VRRigDriver.HeadCamera;
            bool granted = head != null && (head.depthTextureMode & DepthTextureMode.Depth) != 0;
            float smoothness = WantedSmoothness;
            float opacity = WantedOpacity;
            bool changed = granted != _depthGranted
                || !Mathf.Approximately(smoothness, _appliedSmoothness)
                || !Mathf.Approximately(opacity, _appliedOpacity);
            _depthGranted = granted;
            _appliedSmoothness = smoothness;
            _appliedOpacity = opacity;

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Renderer r = _tracked[i];
                if (r == null)
                {
                    _touched.Remove(r!); // Unity-destroyed but still hashing — same reference
                    _tracked.RemoveAt(i);
                    continue;
                }
                if (_touched.TryGetValue(r, out Authored a))
                    Apply(r, a);
            }
            if (changed)
                _nextCensus = 0f; // a state change is exactly when the census line is worth its cost
        }

        /// <summary>
        /// Write the retune. Every value is derived from <paramref name="a"/> — the quad's OWN
        /// authored material values — so nothing here can invent a look the tileset never had.
        /// </summary>
        private void Apply(Renderer r, in Authored a)
        {
            _mpb.Clear();

            // (1) THE MIRROR. Authored 0.754 against a scene with liveProbes=0 and
            //     reflectionMode=Skybox: the only environment on offer is GH_Evil_Sky_MAT, and a
            //     near-smooth surface samples it sharply. Never RAISE what the tileset authored.
            _mpb.SetFloat(SmoothnessId, Mathf.Min(a.Smoothness, WantedSmoothness));

            // (2) THE OPAQUE SHEET. Hue as authored, alpha capped so the tiles read through.
            Color tint = a.Tint;
            tint.a = Mathf.Min(tint.a, WantedOpacity);
            _mpb.SetColor(ColorTintId, tint);

            // (3) THE UNFED DEPTH TERM. With no _CameraDepthTexture the shader's edge band is
            //     pinned at one extreme for the whole quad; painting it in the BODY colour makes
            //     that harmless whichever extreme it is, without depending on the sign of a term
            //     inside a shader we cannot open. Once the depth bit is actually granted the
            //     authored foam is handed straight back.
            if (_depthGranted)
            {
                _mpb.SetColor(EdgeColourId, a.EdgeColour);
                _mpb.SetFloat(EdgeToggleId, a.EdgeToggle);
            }
            else
            {
                _mpb.SetColor(EdgeColourId, tint);
                _mpb.SetFloat(EdgeToggleId, 0f);
            }

            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Clear every property block we wrote. Called on config flip, on uninstall and
        /// on destroy — a mod-written block must never outlive its owner.</summary>
        internal void RestoreAll()
        {
            foreach (Renderer r in _tracked)
            {
                if (r != null)
                    r.SetPropertyBlock(null);
            }
            _tracked.Clear();
            _touched.Clear();
            _pending.Clear();
            _pendingOverflow = false;
            _lastTileCount = -1;
            _cursor = 0;
        }

        private void OnDestroy()
        {
            try { RestoreAll(); }
            catch { /* teardown */ }
        }

        // ---- THE CENSUS ---------------------------------------------------------------------

        /// <summary>
        /// The periodic half of the census: the state that CHANGES, on the same
        /// <c>WATER SURFACE</c> prefix as the heavy per-material dump so both grep together. It
        /// answers the three questions ModBuild 158's log could not: how the retune is enforced and
        /// how often it was observed undone, what the head camera's depth mode actually is after
        /// our request, and whether any reflection probe reaches the water.
        /// </summary>
        private void MaybeLogState()
        {
            if (_tracked.Count == 0 || Time.unscaledTime < _nextCensus)
                return;
            _nextCensus = Time.unscaledTime + CensusInterval;

            Camera? head = Rig.VRRigDriver.HeadCamera;
            int deltaReasserts = _reasserts - _lastCensusReasserts;
            _lastCensusReasserts = _reasserts;

            var sb = new System.Text.StringBuilder(768);
            sb.Append("WATER SURFACE STATE: ").Append(_tracked.Count)
              .Append(" water terrain quad(s) tracked and RENDERING (user ruling 2026-08-18: the "
                      + "water is never hidden). ENFORCEMENT = per-renderer MaterialPropertyBlock, "
                      + "checked every frame with Renderer.HasPropertyBlock and fully re-asserted "
                      + "every ").Append(TickInterval.ToString("0.##"))
              .Append("s; no Renderer.enabled is written anywhere, so ModBuild 158's flicker race "
                      + "cannot recur by construction. UNDONE: ").Append(_reasserts)
              .Append(" re-assert(s) total, ").Append(deltaReasserts)
              .Append(" since the last line — 0 means nothing in the game is clearing our block; "
                      + "any climb is the seam ModBuild 158's round-robin was losing to, and the "
                      + "rate here says how hard it is fighting.");

            // The respawn seam. THIS is what ModBuild 158 actually lost to: Apparance destroys and
            // re-instantiates these quads, so a poll can only ever be late. placements= counts the
            // events that arrived; a 0 here with water on screen means the postfix is NOT taking
            // and the driver is running on the fallback poll alone.
            sb.Append(" | RESPAWN SEAM: ").Append(_placements)
              .Append(" content-placement event(s) caught on "
                      + "ProceduralBase.NotifyContentPlacementComplete (Apparance destroys and "
                      + "re-instantiates these quads — there is no pooling and nothing ever "
                      + "re-enables them), ").Append(_overflows)
              .Append(" collapsed into a full sweep by the ").Append(PendingCap)
              .Append("-entry cap; queued subtrees are retuned in the SAME frame's LateUpdate, "
                      + "with includeInactive:true so a quad placed while its room is still in "
                      + "Preview is tuned before ShowContent switches it on.");

            sb.Append(" | RETUNE: _Smoothness→").Append(_appliedSmoothness.ToString("0.###"))
              .Append(" (authored 0.754 — the sky-mirror dial), _Color_Tint.a capped at ")
              .Append(_appliedOpacity.ToString("0.###")).Append(" (authored 0.737), shore foam ")
              .Append(_depthGranted ? "AUTHORED (depth granted)" : "neutralised (no depth texture)")
              .Append('.');

            // The head camera's ACTUAL depth mode after our request — read, never assumed.
            sb.Append(" | DEPTH: [Water] ShoreFoam=")
              .Append(WaterConfig.ShoreFoam != null && WaterConfig.ShoreFoam.Value)
              .Append(" [Optimize] HeadDepthPrepass=").Append(PerfConfig.DepthPrepassOn)
              .Append(" request=").Append(NeedsDepth).Append(" head=");
            if (head == null)
            {
                sb.Append("<none>");
            }
            else
            {
                sb.Append('\'').Append(head.name).Append("' depthTextureMode=")
                  .Append(head.depthTextureMode).Append(" stereo=").Append(head.stereoEnabled);
            }
            sb.Append(" (Depth present = the shader's _Edge_Distance/_Edge_Colour_Distance/"
                      + "_InvertDepthFade terms have a per-eye-correct texture to read; absent = "
                      + "they read an unwritten one and are pinned, which is why the edge colour "
                      + "is neutralised above).");

            // Does any reflection probe reach the water? This is the reading that decides whether
            // a local probe is worth building — see the class header, option (a).
            try
            {
                ReflectionProbe[] probes = Object.FindObjectsOfType<ReflectionProbe>();
                sb.Append(" | REFLECTION: liveProbes=").Append(probes.Length)
                  .Append(" reflectionMode=").Append(RenderSettings.defaultReflectionMode)
                  .Append(" intensity=").Append(RenderSettings.reflectionIntensity.ToString("0.##"))
                  .Append(" customReflection=")
                  .Append(RenderSettings.customReflection != null
                      ? RenderSettings.customReflection.name : "<null>")
                  .Append(" skybox=")
                  .Append(RenderSettings.skybox != null ? RenderSettings.skybox.name : "<null>");
                Renderer? sample = _censusSample;
                if (sample != null)
                {
                    sb.Append(" probeUsage=").Append(sample.reflectionProbeUsage);
                    Vector3 at = sample.bounds.center;
                    int reaching = 0;
                    for (int i = 0; i < probes.Length; i++)
                    {
                        ReflectionProbe p = probes[i];
                        if (p == null || !p.isActiveAndEnabled || !p.bounds.Contains(at))
                            continue;
                        reaching++;
                        sb.Append(" REACHES['").Append(p.name).Append("' mode=").Append(p.mode)
                          .Append(" refresh=").Append(p.refreshMode)
                          .Append(" res=").Append(p.resolution).Append(']');
                    }
                    if (reaching == 0)
                    {
                        sb.Append(" NO PROBE REACHES THE WATER — so the surface reflects the "
                                  + "SKYBOX and nothing else, which is the whole mechanism of the "
                                  + "'kaputter Spiegel'. A local probe over the basin is the one "
                                  + "change that would let [Water] Smoothness go back up.");
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append(" | REFLECTION unreadable: ").Append(e.GetType().Name);
            }

            sb.Append(" WHAT WOULD DISPROVE THIS: re-asserts climbing with the water still looking "
                      + "wrong would mean the block is not the mechanism at all; a granted depth "
                      + "mode with the sheet still opaque would move the cause off the depth term "
                      + "onto _Color_Tint alone; and a probe appearing in REACHES while the mirror "
                      + "persists would mean _Smoothness, not the environment, is what to cut.");

            VRLog.Info(Name, sb.ToString());
        }

        /// <summary>
        /// The heavy half of the census — one line per distinct water material, cap 3. Everything
        /// in it was unavailable offline: the shader ships compiled in a game bundle, so its
        /// property list, the values on the live material and the reflection environment can only
        /// be read in-process. This is the line that identified the surface and named both causes
        /// (<c>LogOutput.log:942</c>, ModBuild 158).
        /// </summary>
        private void LogWaterSurfaceOnce(Renderer r)
        {
            Material? mat = r.sharedMaterial;
            string matName = mat != null ? mat.name : "<null>";
            string shaderName = mat != null && mat.shader != null ? mat.shader.name : "<null>";
            if (_censused.Count >= 3 || !_censused.Add(matName + "|" + shaderName))
                return;

            var sb = new System.Text.StringBuilder(1024);
            sb.Append("WATER SURFACE '").Append(r.name).Append("': material '").Append(matName)
              .Append("', shader '").Append(shaderName).Append("', renderQueue ")
              .Append(mat != null ? mat.renderQueue : -1)
              .Append(", bounds y[").Append(r.bounds.min.y.ToString("F2")).Append("..")
              .Append(r.bounds.max.y.ToString("F2")).Append("], probeUsage=")
              .Append(r.reflectionProbeUsage).Append(", staticBatch=")
              .Append(r.isPartOfStaticBatch);

            // --- every property with its CURRENT value (the decisive block) -------------------
            sb.Append(" | PROPERTIES: ");
            try
            {
                if (mat == null || mat.shader == null)
                {
                    sb.Append("<no material>");
                }
                else
                {
                    Shader sh = mat.shader;
                    int n = sh.GetPropertyCount();
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0)
                            sb.Append(", ");
                        string pn = sh.GetPropertyName(i);
                        UnityEngine.Rendering.ShaderPropertyType pt = sh.GetPropertyType(i);
                        sb.Append(pn).Append('(').Append(pt).Append(")=");
                        switch (pt)
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                Texture? tex = mat.GetTexture(pn);
                                sb.Append(tex == null
                                    ? "<null>"
                                    : $"'{tex.name}' {tex.width}x{tex.height} {tex.GetType().Name}");
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Color:
                                sb.Append(mat.GetColor(pn));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                sb.Append(mat.GetVector(pn));
                                break;
                            default:
                                sb.Append(mat.GetFloat(pn).ToString("0.###"));
                                break;
                        }
                    }
                    sb.Append(" | keywords=[").Append(string.Join(",", mat.shaderKeywords))
                      .Append(']');
                }
            }
            catch (Exception e)
            {
                sb.Append("unreadable: ").Append(e.GetType().Name);
            }

            // --- the reflection environment ---------------------------------------------------
            try
            {
                sb.Append(" | ENVIRONMENT: skybox=")
                  .Append(RenderSettings.skybox != null ? RenderSettings.skybox.name : "<null>")
                  .Append(" ambientMode=").Append(RenderSettings.ambientMode)
                  .Append(" ambientIntensity=").Append(RenderSettings.ambientIntensity.ToString("0.##"))
                  .Append(" reflectionMode=").Append(RenderSettings.defaultReflectionMode)
                  .Append(" reflectionIntensity=")
                  .Append(RenderSettings.reflectionIntensity.ToString("0.##"))
                  .Append(" customReflection=")
                  .Append(RenderSettings.customReflection != null
                      ? RenderSettings.customReflection.name : "<null>")
                  .Append(" fog=").Append(RenderSettings.fog)
                  .Append(" realtimeProbes=").Append(QualitySettings.realtimeReflectionProbes)
                  .Append(" liveProbes=")
                  .Append(Object.FindObjectsOfType<ReflectionProbe>().Length);
            }
            catch (Exception e)
            {
                sb.Append(" | ENVIRONMENT unreadable: ").Append(e.GetType().Name);
            }

            // --- WHICH CAMERA feeds any screen-space input (the report's own question) ---------
            try
            {
                Camera? main = Camera.main;
                sb.Append(" | CAMERAS: Camera.main=");
                if (main == null)
                {
                    sb.Append("<none>");
                }
                else
                {
                    Vector3 p = main.transform.position;
                    sb.Append('\'').Append(main.name).Append("' at (")
                      .Append(p.x.ToString("F2")).Append(',').Append(p.y.ToString("F2"))
                      .Append(',').Append(p.z.ToString("F2")).Append(") enabled=")
                      .Append(main.enabled).Append(" (PARKED under VR — anything reading it "
                          + "renders from the flat-screen viewpoint)");
                }
                // The VR head camera itself — its depthTextureMode decides whether a
                // depth-reading water shader has a VALID per-eye _CameraDepthTexture at all
                // (Rig/VRRigDriver.HeadCamera.cs owns that bit; this driver may REQUEST it, see
                // WantsDepthTexture, and the WATER SURFACE STATE line reports what came back).
                Camera? head = Rig.VRRigDriver.HeadCamera;
                sb.Append(" head=");
                if (head == null)
                {
                    sb.Append("<none>");
                }
                else
                {
                    sb.Append('\'').Append(head.name).Append("' depthTextureMode=")
                      .Append(head.depthTextureMode).Append(" hdr=").Append(head.allowHDR)
                      .Append(" stereo=").Append(head.stereoEnabled);
                }
                // The one component that republishes the GLOBAL _GrabTexture and
                // _CameraDepthTexture from a quarter-res re-render of Camera.current ?? Camera.main
                // (decompiled GH.Runtime/RFX4_DistortionAndBloom.cs:145-152, 246-269). Measured 0
                // in the ModBuild 158 log, which is what rules the parked-camera grab OUT.
                var grabbers = Object.FindObjectsOfType<RFX4_DistortionAndBloom>();
                sb.Append(" RFX4_DistortionAndBloom=").Append(grabbers.Length);
                foreach (RFX4_DistortionAndBloom g in grabbers)
                {
                    sb.Append(" ['").Append(g.gameObject.name).Append("' enabled=")
                      .Append(g.enabled && g.gameObject.activeInHierarchy)
                      .Append(" scale=").Append(g.RenderTextureResolutoinFactor.ToString("0.##"))
                      .Append(']');
                }
            }
            catch (Exception e)
            {
                sb.Append(" | CAMERAS unreadable: ").Append(e.GetType().Name);
            }

            VRLog.Info(Name, sb.ToString());
        }
    }
}

/// <summary>
/// THE RESPAWN SEAM — the answer to ModBuild 158's flicker, and the reason this driver does not
/// have to poll for new water.
///
/// <para>The game's water quads are Apparance prefab instances. Every content rebuild of the owning
/// <c>ApparanceEntity</c> DESTROYS the old objects and <c>Instantiate</c>s the prefab again; there
/// is no pooling in <c>Apparance.Unity.dll</c> and it never writes <c>Renderer.enabled</c> at all.
/// Rebuilds are common — <c>ApparanceEngine.UpdateEngine</c> drives detail focus from a per-frame
/// view position, and <c>ProceduralTileObserver</c> flags a rebuild on activity and bounds changes.
/// So any state a mod writes onto those renderers has a lifetime of one rebuild, and any POLL for
/// them is late by construction. That is what the user saw.</para>
///
/// <para><c>ProceduralBase.NotifyContentPlacementComplete</c> is the single method Apparance calls
/// once it has finished placing an entity's objects, and it is the right seam because BOTH
/// overrides in the game (<c>ProceduralMapTile</c>, <c>ProceduralProp</c>) call
/// <c>base.NotifyContentPlacementComplete()</c> first — so one postfix on the base sees every
/// placement, whichever concrete type owns it. The postfix does nothing but queue the subtree; the
/// work happens in the driver's own LateUpdate, in the same frame, so a fresh quad is retuned
/// before it is ever submitted.</para>
///
/// <para>Pure bookkeeping: the original method is untouched, the postfix cannot throw into the
/// game, and it is a no-op the moment <see cref="WaterTerrainVR"/> is uninstalled — the same
/// contract <c>SceneRegistry</c>'s enrolment postfixes ship under.</para>
/// </summary>
[HarmonyPatch(typeof(ProceduralBase), nameof(ProceduralBase.NotifyContentPlacementComplete))]
internal static class ProceduralBase_NotifyContentPlacementComplete_WaterPatch
{
    private static void Postfix(ProceduralBase __instance)
    {
        try { WaterTerrainVR.NotifyContentPlaced(__instance); }
        catch { /* bookkeeping is best-effort — never disturb the game's content pipeline */ }
    }
}
