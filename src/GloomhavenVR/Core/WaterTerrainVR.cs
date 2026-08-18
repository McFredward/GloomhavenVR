using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core;

/// <summary>
/// THE MIRRORING FLOOR — user report 2026-08-15 (spiegeltiles.jpg), amended 2026-08-18, and
/// REPORTED UNCHANGED TWICE SINCE. Original: "In dem Level das wir gespielt haben wurde die
/// Bodentiles eines Raumes nicht richtig dargestellt. Stattdessen war dort eine Fläche zu sehen
/// die spiegelt und sie mit den Kopfbewegungen ändert." Amendment (user ruling, verbatim):
/// <i>"Wichtige Ergänzung: Das Wasser soll auf jeden Fall dargstellt werden - aber eben in einer
/// VR-freundlichen Variante. Einfach ausblenden ist keine Option."</i> Third round, after
/// ModBuild 159 retuned the water through a property block: <i>"Die Spiegelreflektionen sehen
/// immer noch identisch unatürlich aus und bewegen sich schnell mit den Kopfbewegungen mit."</i>
///
/// <para>THAT RULING IS THE SHAPE OF THIS FILE. ModBuild 158 hid the quads. That path is GONE —
/// this driver never writes <c>Renderer.enabled</c> and carries no hide dial at all. The water
/// renders; what changes is HOW.</para>
///
/// <para>"IDENTICAL" IS THE MEASUREMENT THAT DROVE THIS BUILD, and it is a strong one. ModBuild
/// 159 dropped <c>_Color_Tint</c>'s alpha from 0.737 to 0.45 through a
/// <see cref="MaterialPropertyBlock"/>. A third of the tint's opacity coming off is not a subtle
/// change; had it reached the shader the pool could not have looked the same. So the report is
/// evidence about the MECHANISM, not about the tuning, and this build answers it three ways at
/// once instead of re-fitting numbers against a lever that may never have been connected — the
/// DARKENING LEVER lesson of ModBuild 152, where four constants were refitted four times against
/// photographs in which the write did nothing.</para>
///
/// <list type="number">
///   <item><b>THE WRITE ITSELF IS NO LONGER A PROPERTY BLOCK.</b> Every tuned renderer now
///   carries its OWN material instance (<see cref="Renderer.materials"/>, which instantiates),
///   and the values are written onto that. There is no longer any question of whether the write
///   reaches the shader: the instance IS the material the renderer draws with. Two concrete
///   mechanisms could have swallowed the old block and both die here. (a) GPU INSTANCING: 17
///   identical quads sharing one material are a perfect instancing batch, and in the built-in
///   pipeline a batched draw takes non-instanced properties from the MATERIAL — a property block
///   that is not backed by a <c>UNITY_INSTANCING_BUFFER</c> entry is simply not there. Our
///   instances are unique per renderer, so no batch can form, and <c>enableInstancing</c> is
///   switched off on each of them explicitly. The census now prints the material's authored
///   <c>instancing=</c> flag, which settles retroactively whether that is what ate ModBuild 159.
///   (b) SHADER KEYWORDS: <c>_EdgeColour_Toggle</c> is almost certainly an Amplify
///   <c>[Toggle(_EDGECOLOUR_TOGGLE_ON)]</c> — the material's live keyword list is exactly
///   <c>[_EDGECOLOUR_TOGGLE_ON]</c> — and a keyword-driven property is compiled into a
///   <c>shader_feature</c> branch that never reads the float at all. <b>A MaterialPropertyBlock
///   cannot set or clear a shader keyword.</b> ModBuild 159's foam neutralisation therefore had a
///   half that was inert by construction. A material instance can, and does,
///   <see cref="Material.DisableKeyword"/> it. The census now also prints
///   <c>Shader.GetPropertyAttributes</c> for every property, so the next log states in the
///   shader's own words whether that toggle is keyword-driven rather than inferring it.</item>
///
///   <item><b>THE PROPERTY IS NO LONGER GUESSED BY NAME.</b> ModBuild 159 wrote one hard-coded
///   name, <c>_Smoothness</c>, because that is the name the census printed. Whether this shader
///   spends it on an environment mip or on a specular exponent is UNKNOWN and cannot be found
///   out offline: the four water shaders ship compiled inside <c>always_loaded_base*</c> and
///   there is no game install on the build machine to open them with (checked, 2026-08-18 — no
///   bundle, no <c>StreamingAssets</c>, nothing but the managed DLLs). So the driver now walks
///   the shader's WHOLE property table at runtime and caps every property whose name belongs to
///   a reflection family — gloss, metal, explicit reflection strength — and floors every
///   roughness property, which means the same axis backwards. <see cref="WaterReflectionCaps"/>
///   owns that classification, holds the invariant that no cap can ever make a surface shinier,
///   and is pinned by <c>WaterReflectionVectors</c> in the wire tests. Every property found, its
///   authored value, what was written and every REFUSAL with its reason go in the log.</item>
///
///   <item><b>THE MIRROR IS PROBABLY NOT ON THE WATER PLANE AT ALL.</b> This is the strongest
///   new lead and it comes from the user's own first word: he wrote <i>"Tiles"</i>, not "Wasser".
///   The FLOOR CENSUS of the same hardware log lists, in the same room, immediately under and
///   around the water film:
///   <code>
///   [FLOOR] 'TERRAIN_Crypt_Water_02_Edge' y[-0.2..0.0] sh='Amp_Basic_N_MRAO' q2000
///   [FLOOR] 'TERRAIN_Crypt_Water_02_Base' y[-0.3..-0.1] sh='Amp_Basic_N_MRAO' q2000
///   [FLOOR] 'TERRAIN_Water_Plane'         y[0.0..0.0]  sh='VFX/Water_Shd_Trans' q2900
///   </code>
///   <c>Amp_Basic_N_MRAO</c> is <b>M</b>etallic / <b>R</b>oughness / <b>AO</b>. A metallic surface
///   in a scene with <c>reflectionMode=Skybox</c> and <c>liveProbes=0</c> has exactly one
///   environment to sample — <c>GH_Evil_Sky_MAT</c> — and a metallic term is a mirror by
///   definition. Seen from a moving head at grazing angles that is a broken mirror that swims,
///   and through ModBuild 159 this driver had never touched those two renderers: its scope was
///   the <c>Water_Sh</c> shader stem, which excludes them precisely. The scope is now the whole
///   water FEATURE — the basin edge and base that lie inside the water's own footprint — matched
///   by name family AND geometry, never by shader alone, so the room's ordinary floor tiles
///   (<c>CR_RU_Floor_01</c>, same shader, same room) are not swept up with them.</item>
/// </list>
///
/// <para>AND A FOURTH MECHANISM THAT DOES NOT CARE WHICH OF THE THREE WAS RIGHT. Whatever samples
/// <c>unity_SpecCube0</c> in this room gets the skybox, because <c>liveProbes=0</c>: there is not
/// one reflection probe in the scene. A skybox reflection is INFINITELY FAR AWAY, so its
/// reflection vector sweeps the whole sky as the head moves — that is the swimming, and it is a
/// property of the environment, not of any one material. <c>[Water] LocalProbe</c> (default ON)
/// puts a local <see cref="ReflectionProbe"/> over each water feature, in
/// <see cref="ReflectionProbeMode.Custom"/> mode, carrying a FLAT cubemap built from the scene's
/// own ambient probe. A flat cube returns the same colour in every direction, so the reflected
/// colour cannot change as the head turns — <b>no matter which property, which renderer or which
/// shader the reflection comes from.</b> Box projection is switched OFF explicitly for the same
/// reason: it would reintroduce exactly the parallax being removed. This removes the SYMPTOM by
/// construction even if all three hypotheses above are wrong, and it is a dial so the user can
/// A/B it in the headset in one press.</para>
///
/// <para>WHY YOU COULD NOT SEE THE FLOOR THROUGH IT — the second, independent cause, from the
/// same census line. <c>CAMERAS: … head='GloomhavenVR.HeadCamera' depthTextureMode=None</c>. The
/// shader's depth fade and shore foam (<c>_Edge_Distance=0.2</c>,
/// <c>_Edge_Colour_Distance=0.9</c>, <c>_Edge_Colour</c> = near-white RGBA(0.887,0.887,0.887,
/// 0.867), <c>_EdgeColour_Toggle=1</c> with keyword <c>_EDGECOLOUR_TOGGLE_ON</c> live,
/// <c>_InvertDepthFade=0</c>) all read <c>_CameraDepthTexture</c>. Our head camera does not write
/// one: <c>Defaults.HeadDepthPrepass</c> is <c>false</c> since the 2026-07 submission-cost pass,
/// because on the built-in FORWARD path that texture costs a whole extra opaque scene submission
/// PER EYE. With no depth texture the fade term is pinned at one extreme for the entire quad —
/// and the near-white foam colour pinned ON across a whole hex is what spiegeltiles.jpg actually
/// shows: pale, chalky hexes that are LIGHTER than the stone around them, which a dark green
/// tint of RGBA(0.195,0.311,0.131) could never produce. <c>Camera.main='ScenarioCamera'</c> is
/// PARKED under VR, so nothing else is writing a usable depth texture either. Both halves of the
/// neutralisation — the colour AND the keyword — now land, where before only the colour could
/// have.</para>
///
/// <para>AND THE FOAM CAN COME BACK, MEASURED. <c>[Water] ShoreFoam</c> (default OFF) makes this
/// driver REQUEST <see cref="DepthTextureMode.Depth"/> on the head camera — but only while water
/// terrain is actually tracked, so the cost is confined to water rooms instead of the whole game.
/// <see cref="Rig.VRRigDriver"/>'s existing depth-mode tick honours the request beside
/// <c>[Optimize] HeadDepthPrepass</c>. When the driver observes the bit actually granted (it reads
/// the camera, it does not assume its own request took), it stops neutralising the edge colour and
/// hands the authored foam and its keyword back — under MultiPass each eye renders its own pass
/// and therefore its own depth texture, so the foam is per-eye CORRECT, not per-eye rivalrous.
/// COST, STATED NOT MEASURED: one extra full opaque scene submission per eye pass, which this
/// project's own <see cref="Rig.VRRigDriver"/> notes call the largest single piece of submission
/// volume the mod adds. That is why it is opt-in.</para>
///
/// <para>WHAT IS STILL NOT BUILT, so the next round does not re-derive it: REPLACING the material
/// with a mod-owned water shader from the mod's own bundle. That is the standing fallback if the
/// game shader turns out to be uncorrectable from outside; it needs a bundle lane
/// (<c>unity/**</c> + <c>Core/BundleShaders.cs</c>) that this lane does not own, and it should
/// not be reached for until the log below has said which of the four mechanisms above was the
/// live one.</para>
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
/// authored it (water, just with the mirror back for a moment), never as a hole in the floor.
/// Second, the respawn has an EVENT rather than a poll:
/// <see cref="ProceduralBase_NotifyContentPlacementComplete_WaterPatch"/> postfixes the one method
/// Apparance calls when it has finished (re)placing an entity's objects
/// (<c>ProceduralBase.NotifyContentPlacementComplete</c>, which both overrides —
/// <c>ProceduralMapTile</c> and <c>ProceduralProp</c> — call through). The affected subtree is
/// queued and retuned in the SAME frame's LateUpdate, before it is ever submitted. The sweep uses
/// <c>includeInactive: true</c> so a quad placed while its room is still in <c>Preview</c> (its
/// content is <c>SetActive(false)</c> until <c>ProceduralMapTile.ShowContent</c> reveals it) is
/// tuned before it is switched on, rather than after.</para>
///
/// <para>MATERIAL INSTANCES ARE OWNED, AND OWNERSHIP IS THE COST OF THIS APPROACH. A material
/// obtained from <see cref="Renderer.materials"/> is a live <see cref="Object"/> that Unity does
/// not reliably collect when its renderer dies, and this content churns: 109 placement events in
/// one logged session. So every instance is tracked and explicitly destroyed — when its renderer
/// is pruned, when the material is replaced under us, on config flip, on uninstall and on
/// destroy — and the census counts live instances so a leak would show as a number that only
/// climbs. The shared material is NEVER written. The batching cost is 17 quads plus their basin,
/// which is negligible and was already <c>staticBatch=False</c>.</para>
///
/// <para>ONE KNOWN WRITER OF THESE RENDERERS EXISTS and it matters more now than it did for a
/// property block: <c>MaterialLoaderData.CheckAllMaterialLoaded</c> swaps <c>sharedMaterials</c>
/// on an Addressables callback (it is also the only unconditional <c>Renderer.enabled = true</c>
/// in the whole game assembly). A swap would drop our instance on the floor. The per-frame
/// enforcement is therefore a reference compare of <see cref="Renderer.sharedMaterial"/> against
/// the instance we handed it — one native call per tracked renderer, no allocation — and a
/// mismatch destroys the orphans and re-adopts from the NEW materials. Both count into
/// <c>UNDONE:</c> in the census.</para>
///
/// <para>A NOTE FOR WHOEVER GREPS THE NEXT LOG: <c>WallSegmentFade</c>'s FLOOR CENSUS annotates a
/// renderer with <c>OUR-MPB</c> when it carries a property block. From this build on, the water
/// plane will NOT carry that annotation — because it no longer carries a block. Its absence is
/// the confirmation that the mechanism changed, not a sign that the driver stopped running; the
/// <c>WATER SURFACE STATE</c> line below is what says whether it ran.</para>
///
/// <para>SCOPE. Two sets, both narrow, both logged individually. (1) THE FILM: renderers on the
/// game's water shader family (the <c>Water_Sh</c> stem — the same key
/// <see cref="WallSegmentFade"/>'s fountain exemption uses, so there is one definition of "this
/// is water" in the mod) AND named for the terrain prop family
/// (<see cref="TerrainWaterNameTokens"/>). The name term keeps DECORATIVE water out: the
/// fountain/pond props of the <c>brunnen.png</c> ruling (<c>FR_SW_Pond_Small</c>,
/// <c>P_Waterfall_Circle_Small</c>) are set dressing nobody stands on and are not what the report
/// is about. (2) THE BASIN: renderers named for the water-feature family
/// (<see cref="BasinNameTokens"/>) whose bounds lie inside a tracked film's own footprint and do
/// not rise above it. Both terms are load-bearing — the room's ordinary floor runs the same
/// <c>Amp_Basic_N_MRAO</c> shader as the basin, so a shader-only rule would retune the whole
/// room, and a name-only rule would reach across the map.</para>
///
/// <para>MULTIPLAYER: purely local rendering. Material instances are per-process, no shared
/// material is written, no probe crosses the wire, nothing is serialised and each peer decides
/// for itself — the report's three machines all logged the same 17 planes. REVERSIBLE: restoring
/// <c>sharedMaterials</c> and destroying the instances puts every renderer back to the authored
/// material bit-for-bit, and the probes are destroyed with them. TickGuard-safe.</para>
/// </summary>
internal static class WaterTerrainVR
{
    /// <summary>Log scope — lines read <c>[Compat] WATER SURFACE …</c> so this greps together
    /// with the other game-rendering fixups it ships beside.</summary>
    private const string Name = "Compat";

    private const string DriverName = "GloomhavenVR.WaterTerrainVR";

    /// <summary>Name of the probe objects this driver spawns. Prefixed so the census (and any
    /// other sweep in the mod) can tell OUR probes from a tileset's own.</summary>
    internal const string ProbeObjectName = "GloomhavenVR.WaterReflectionProbe";

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

    /// <summary>Authored name family of the SOLID geometry that makes up a water feature's
    /// basin — the sunken bed under the film and the rim around it. The report's room places
    /// <c>TERRAIN_Crypt_Water_02_Base</c> and <c>TERRAIN_Crypt_Water_02_Edge</c>, both on
    /// <c>Amp_Basic_N_MRAO</c>. Matched case-insensitively as a substring AND gated on geometry
    /// (see <see cref="Driver.IsBasinCandidate"/>), because "Water" alone would also match a
    /// water-themed banner on the far side of the map. Extend this array for a tileset that
    /// names its pool bed something else — that is the only change such a set should need.</summary>
    private static readonly string[] BasinNameTokens =
        { "Water", "Pool", "Pond", "Basin", "Trough", "Cistern" };

    // --- the four water-FILM properties this driver writes by id (name→id resolution is a
    //     dictionary lookup in Unity; resolving once at type load keeps the path free of it).
    //     Every one of them was READ OFF THE LIVE MATERIAL on hardware (LogOutput.log:1018), so
    //     these names are measured, not guessed from a shader we cannot open. Everything ELSE
    //     this driver writes is discovered from the shader's own property table at runtime —
    //     see WaterReflectionCaps for why that difference matters.
    private static readonly int ColorTintId = Shader.PropertyToID("_Color_Tint");
    private static readonly int EdgeColourId = Shader.PropertyToID("_Edge_Colour");
    private static readonly int EdgeToggleId = Shader.PropertyToID("_EdgeColour_Toggle");

    /// <summary>The keyword the water material ships ENABLED (<c>keywords=[_EDGECOLOUR_TOGGLE_ON]</c>,
    /// hardware census). An Amplify <c>[Toggle(…)]</c> property compiles to a
    /// <c>shader_feature</c> branch on exactly this keyword and never reads the float, which is
    /// why ModBuild 159's <c>_EdgeColour_Toggle = 0</c> could not have done anything through a
    /// property block. Clearing it needs a material, and now there is one.</summary>
    private const string EdgeColourKeyword = "_EDGECOLOUR_TOGGLE_ON";

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
            "WaterTerrainVR installed — the parts of the game's water FEATURE (the film "
            + "'TERRAIN_Water_Plane' on shader family 'Water_Sh*' AND the basin bed/rim inside "
            + "its own footprint) all KEEP RENDERING (user ruling 2026-08-18: hiding is not an "
            + "option) and are retuned for a "
            + "free camera. ModBuild 160 changes the MECHANISM, not the numbers, because the "
            + "third report said the look was IDENTICAL after a tint-alpha cut of a third: the "
            + "retune is now written onto a per-renderer MATERIAL INSTANCE instead of a "
            + "MaterialPropertyBlock (so neither GPU instancing nor a keyword-driven toggle can "
            + "swallow it), the reflection properties are DISCOVERED from each shader's own "
            + "property table instead of being one hard-coded name, the basin's metallic "
            + "surfaces are in scope for the first time, and a local flat-cubemap reflection "
            + "probe removes the head-swimming by construction whichever of those was the cause. "
            + "See the WATER SURFACE census lines for what each write actually found.");
    }

    /// <summary>Drop the driver, restoring every material we replaced and destroying everything
    /// we spawned.</summary>
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

    /// <summary>Does any of this renderer's shared materials run the game's water shader
    /// family?</summary>
    private static bool IsWaterShader(Renderer r, List<Material> matScratch)
    {
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

    private static bool NameHasAny(string n, string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (n.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>Is this renderer one of the game's water TERRAIN quads — the FILM? Shader family
    /// AND name family; see the class header for why the name term is there.</summary>
    private static bool IsTerrainWater(Renderer r, List<Material> matScratch)
    {
        if (r == null)
            return false;
        return NameHasAny(r.name, TerrainWaterNameTokens) && IsWaterShader(r, matScratch);
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

        /// <summary>The sharpness dial. The water film authors <c>_Smoothness</c> 0.754.</summary>
        internal static ConfigEntry<float>? Smoothness;

        /// <summary>Upper bound on <c>_Color_Tint</c>'s alpha. Authored 0.737.</summary>
        internal static ConfigEntry<float>? Opacity;

        /// <summary>Request the head camera's depth texture so the authored shore foam works.</summary>
        internal static ConfigEntry<bool>? ShoreFoam;

        /// <summary>Upper bound on every METALLIC / explicit reflection-strength scalar found on
        /// a water-feature shader. Default 0 — a crypt pool's bed is wet stone, not chrome.</summary>
        internal static ConfigEntry<float>? Reflectivity;

        /// <summary>Take the basin bed and rim into scope, not only the water film.</summary>
        internal static ConfigEntry<bool>? BasinSurfaces;

        /// <summary>Spawn a local flat reflection probe over each water feature.</summary>
        internal static ConfigEntry<bool>? LocalProbe;

        /// <summary>Multiplier on the local probe's brightness.</summary>
        internal static ConfigEntry<float>? ProbeBrightness;

        internal static void Bind()
        {
            if (_file != null)
                return;
            ConfigFile config = _file = ModuleConfig.Create("water");
            VRFriendlyWater = config.Bind("Water", "VRFriendlyWater", true,
                "Retune the game's water FEATURE (the film TERRAIN_Water_Plane on shader "
                + "VFX/Water_Shd*, and the basin bed/rim under it) for a free VR camera. The "
                + "water always RENDERS — this only changes how. The game's water is authored "
                + "for one fixed steep top-down camera pitch; across a VR table it is seen at "
                + "grazing angles, where the metallic basin and the near-mirror film both "
                + "reflect the skybox and swing with the head (report 2026-08-15 "
                + "spiegeltiles.jpg, unchanged through two rebuilds). ON gives each affected "
                + "renderer its OWN material instance and writes onto that: every reflection, "
                + "gloss and metal property the shader declares is capped, the tint alpha is "
                + "capped so the floor reads through, and the depth-fed shore foam is "
                + "neutralised (colour AND keyword) while no depth texture exists. The shared "
                + "material is never touched. OFF restores the game's water immediately.");
            Smoothness = config.Bind("Water", "Smoothness", 0.08f,
                new ConfigDescription(
                    "Ceiling for every SHARPNESS property on a water-feature shader (the water "
                    + "film authors _Smoothness 0.754). High values make a surface mirror the "
                    + "skybox sharply, which is what reads as a broken mirror that swims with "
                    + "your head. Low values widen the lobe into a broad sheen. A shader that "
                    + "exposes ROUGHNESS instead — the same axis backwards — is floored at "
                    + "1 minus this value, so one dial covers both spellings. Never raises what "
                    + "the tileset authored.",
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
            Reflectivity = config.Bind("Water", "Reflectivity", 0f,
                new ConfigDescription(
                    "Ceiling for every METALLIC and explicit reflection-strength property on a "
                    + "water-feature shader. The basin under the report's pool runs "
                    + "Amp_Basic_N_MRAO — Metallic/Roughness/AO — and a metallic surface in a "
                    + "scene with no reflection probe IS a mirror of the skybox, which is the "
                    + "likeliest source of the 'kaputter Spiegel'. 0 = wet stone rather than "
                    + "chrome. Raise it only if the pool bed looks too flat. Never raises what "
                    + "the tileset authored.",
                    new AcceptableValueRange<float>(0f, 1f)));
            BasinSurfaces = config.Bind("Water", "BasinSurfaces", true,
                "Take the water feature's SOLID geometry into scope as well as the film — the "
                + "sunken bed and the rim inside the water's own footprint "
                + "(TERRAIN_Crypt_Water_02_Base / _Edge in the report's room). Through ModBuild "
                + "159 only the transparent film was ever touched, and the user's own word for "
                + "the defect was 'Tiles'. A renderer is only adopted when its NAME belongs to "
                + "the water-feature family AND its bounds lie inside a tracked water film's "
                + "footprint without rising above it, so the room's ordinary floor — which runs "
                + "the same shader — is never swept up. OFF confines the retune to the film "
                + "again, which is the direct A/B for whether the basin was the mirror.");
            LocalProbe = config.Bind("Water", "LocalProbe", true,
                "Put a local reflection probe over each water feature, carrying a FLAT cubemap "
                + "built from the scene's own ambient light. This scene has NO reflection probe "
                + "at all (liveProbes=0), so every glossy or metallic surface in it reflects the "
                + "skybox — and a skybox is infinitely far away, so its reflection sweeps as you "
                + "move your head. That is the swimming. A flat cubemap returns the same colour "
                + "in every direction, so the reflected colour cannot change with your head no "
                + "matter which property or which renderer the reflection comes from. Box "
                + "projection is off deliberately: it would put the parallax back. OFF is the "
                + "A/B for whether the environment, rather than any material, was the cause.");
            ProbeBrightness = config.Bind("Water", "ProbeBrightness", 1f,
                new ConfigDescription(
                    "Multiplier on the local water reflection probe's brightness. The probe's "
                    + "colour comes from the scene's own ambient probe times the scene's "
                    + "reflection intensity, so 1.0 means 'as bright as the room already is'. "
                    + "Lower if the pool looks washed out, higher if it looks dead. Only has an "
                    + "effect while LocalProbe is on.",
                    new AcceptableValueRange<float>(0f, 2f)));
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
                        + "VRFriendlyWater (master), [Water] Smoothness (the sharpness ceiling), "
                        + "[Water] Reflectivity (the metallic ceiling), [Water] BasinSurfaces, "
                        + "[Water] LocalProbe, [Water] ProbeBrightness and [Water] Opacity. The "
                        + "old line is harmless and can be deleted.");
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

        /// <summary>How far past a water film's own XZ footprint the basin search reaches. 1.0 wu
        /// is roughly half a hex — the same reach <c>WallSegmentFade</c>'s water-feature rect
        /// uses, deliberately, so the mod has one idea of how big "the water feature" is.</summary>
        private const float BasinMarginXZ = 1.0f;

        /// <summary>How far ABOVE the film a basin piece may reach and still count as basin. The
        /// bed and the rim sit at y[-0.3..0.0] under a film at y=0.0 (hardware FLOOR CENSUS), so
        /// this is generous; what it excludes is a wall or a prop that merely STANDS in the
        /// pool, which is not part of the water surface and must keep its authored look.</summary>
        private const float BasinHeadroomWU = 0.6f;

        /// <summary>A basin piece is one tile's worth of geometry. Anything spanning more than
        /// this in XZ is a room-wide floor mesh that happens to carry a matching name, and
        /// adopting it would retune a whole room off one pool. Bounded on purpose.</summary>
        private const float BasinMaxSpanWU = 20f;

        /// <summary>How far past the water feature's own bounds the local reflection probe's box
        /// reaches. A renderer is affected by a probe when its bounds CENTRE is in the box, so
        /// this only has to cover the film and the bed; it is kept small so the probe cannot
        /// claim the room's walls.</summary>
        private static readonly Vector3 ProbePadding = new(1.5f, 1.5f, 1.5f);

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

        /// <summary>Every renderer we own the materials of, and what we have to hand back.</summary>
        private readonly Dictionary<Renderer, Owned> _owned = new(48);

        /// <summary>Same set as <see cref="_owned"/>, in list form: the per-frame enforcement
        /// walk must not allocate an enumerator over a dictionary every frame.</summary>
        private readonly List<Renderer> _tracked = new(48);

        /// <summary>Bounds of every tracked FILM, refreshed on the slow tick. The basin search
        /// and the probe boxes are both built off this and nothing else.</summary>
        private readonly List<Bounds> _filmBounds = new(24);

        private readonly List<ProceduralMapTile> _tileScratch = new(16);
        private readonly List<Renderer> _rendererScratch = new(64);
        private readonly List<Material> _matScratch = new(4);
        private readonly List<Bounds> _clusterScratch = new(8);

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

        /// <summary>Materials already dumped in full (one heavy line each, cap 4 — the film's
        /// shader plus the basin's, with room for a second tileset's pair).</summary>
        private readonly HashSet<string> _censused = new();

        /// <summary>A film renderer we have dumped, kept for the periodic state line.</summary>
        private Renderer? _censusSample;

        /// <summary>Last applied mode, so a live config flip re-applies or restores exactly once.</summary>
        private bool _appliedTune;

        /// <summary>Whether the head camera was OBSERVED to carry the Depth bit at the last
        /// re-assert. Read from the camera, never assumed from our own request — the request can
        /// be refused by a rig that has no head camera yet, and the shore-foam decision has to
        /// follow what is actually true.</summary>
        private bool _depthGranted;

        /// <summary>Config values the tracked set was last written with, so a live dial change
        /// re-applies without a per-frame value compare on every material.</summary>
        private float _appliedSmoothness = float.NaN;
        private float _appliedOpacity = float.NaN;
        private float _appliedReflectivity = float.NaN;
        private bool _appliedBasin = true;

        /// <summary>How many times a tracked renderer was found NOT carrying the material
        /// instance we handed it, and had to be re-adopted. This is the number that says whether
        /// anything in the game is fighting us — <c>MaterialLoaderData.CheckAllMaterialLoaded</c>
        /// is the one known writer (see the class header).</summary>
        private int _reasserts;

        /// <summary>Reassert count at the last census line, so the line can say the RATE.</summary>
        private int _lastCensusReasserts;

        /// <summary>Live material instances we own. Counted rather than assumed: a leak in the
        /// ownership below would show here as a number that only ever climbs, against a tracked
        /// count that does not.</summary>
        private int _liveInstances;

        /// <summary>Total instances ever created and ever destroyed — the pair that makes the
        /// leak check readable at a glance in one log line.</summary>
        private int _instancesMade;
        private int _instancesDestroyed;

        /// <summary>How many renderers had <c>reflectionProbeUsage=Off</c> and were switched to
        /// BlendProbes so our local probe could reach them at all. Restored on release.</summary>
        private int _probeUsageForced;

        // ---- the local reflection probes -----------------------------------------------------

        private readonly List<ReflectionProbe> _probes = new(4);
        private Cubemap? _flatCube;
        private Color _flatCubeColour = new(-1f, -1f, -1f, -1f);
        private int _probeSig = int.MinValue;
        private bool _appliedLocalProbe;
        private float _appliedProbeBrightness = float.NaN;
        private string _probeNote = "not built yet";

        private Action? _tick;

        /// <summary>
        /// What we took from a renderer and what we owe it back.
        /// <para><see cref="Shared"/> is the AUTHORED array, and it is also the source every
        /// write reads from — never the instance — so a re-assert can never compound and a
        /// restore is exact. <see cref="Instances"/> is ours to destroy.</para>
        /// </summary>
        private sealed class Owned
        {
            internal Material[] Shared = Array.Empty<Material>();
            internal Material[] Instances = Array.Empty<Material>();

            /// <summary>True for the transparent water FILM (which also gets the tint/foam
            /// treatment), false for the solid basin (reflection caps only).</summary>
            internal bool IsFilm;

            /// <summary>The renderer's authored probe usage, restored on release. We only ever
            /// change it in one direction — Off → BlendProbes — and only while a local probe
            /// exists to be reached; see <see cref="_probeUsageForced"/>.</summary>
            internal ReflectionProbeUsage AuthoredProbeUsage = ReflectionProbeUsage.BlendProbes;

            internal bool ProbeUsageWasForced;

            /// <summary>Whether slot 0 held a real material when we adopted, i.e. whether the
            /// per-frame reference compare has a key to compare against. False for a renderer
            /// whose first slot is empty — comparing a null against a null would read as "the
            /// game replaced our material" on every single frame, and would churn this renderer
            /// through adopt/release forever while <c>UNDONE:</c> counted up a race that was
            /// never happening.</summary>
            internal bool EnforceFirstSlot;
        }

        /// <summary>One reflection-family property of one shader, classified once. See
        /// <see cref="GetCapPlan"/>.</summary>
        private readonly struct CapProp
        {
            internal readonly string Name;
            internal readonly int Id;
            internal readonly WaterCapFamily Family;
            internal readonly bool IsScalar;
            internal readonly bool HasRange;
            internal readonly float Lo;
            internal readonly float Hi;

            internal CapProp(
                string name, WaterCapFamily family, bool isScalar, bool hasRange, float lo, float hi)
            {
                Name = name;
                Id = Shader.PropertyToID(name);
                Family = family;
                IsScalar = isScalar;
                HasRange = hasRange;
                Lo = lo;
                Hi = hi;
            }
        }

        /// <summary>Per-shader reflection plans, built once. A shader is a shared asset and its
        /// property table is immutable at runtime, so this is exact and not merely a cache.</summary>
        private readonly Dictionary<Shader, CapProp[]> _capPlans = new(8);

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

        private static float WantedReflectivity =>
            WaterConfig.Reflectivity != null ? WaterConfig.Reflectivity.Value : 0f;

        private static bool WantBasin =>
            WaterConfig.BasinSurfaces == null || WaterConfig.BasinSurfaces.Value;

        private static bool WantProbe =>
            WaterConfig.LocalProbe == null || WaterConfig.LocalProbe.Value;

        private static float WantedProbeBrightness =>
            WaterConfig.ProbeBrightness != null ? WaterConfig.ProbeBrightness.Value : 1f;

        /// <summary>LateUpdate, not Update: this is the LAST word before the frame is submitted,
        /// so anything the game does to these renderers during its own Update is already done when
        /// we look. Cost in the report's room is one null compare and one
        /// <see cref="Renderer.sharedMaterial"/> read per tracked renderer — no allocation, no
        /// component walk, no scene query — plus the quarter-second discovery tick.</summary>
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
                        "WATER SURFACE: [Water] VRFriendlyWater turned OFF — every water-feature "
                        + "renderer got its authored shared material back, our material instances "
                        + "and local reflection probes are destroyed, and the game's own water "
                        + "renders again exactly as on a flat screen, sky mirror and all.");
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
            MaintainProbes();
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

        /// <summary>
        /// The cheap per-frame guard. With a material INSTANCE there is no property block to be
        /// cleared; the one way our retune can silently stop applying is the game assigning new
        /// materials over ours — which <c>MaterialLoaderData.CheckAllMaterialLoaded</c> does on
        /// an Addressables callback. A reference compare of the first slot answers that without
        /// allocating the <c>sharedMaterials</c> array. Dead entries are pruned here, which is
        /// also where the tracked count follows content going away AND where the instances of a
        /// destroyed renderer are released.
        /// </summary>
        private void EnforceEveryFrame()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Renderer r = _tracked[i];
                if (r == null)
                {
                    ReleaseAt(i, restore: false); // the renderer is gone; only the instances remain
                    continue;
                }
                if (!_owned.TryGetValue(r, out Owned o))
                {
                    _tracked.RemoveAt(i);
                    continue;
                }
                if (!o.EnforceFirstSlot)
                    continue; // slot 0 was empty when we adopted; there is no key to compare
                Material? first = o.Instances.Length > 0 ? o.Instances[0] : null;
                if (ReferenceEquals(r.sharedMaterial, first) && first != null)
                    continue;
                // Something replaced the materials under us — MaterialLoaderData's Addressables
                // callback is the one known writer. Hand nothing back (the array we recorded is
                // no longer what the renderer wants), destroy our orphans and re-adopt from the
                // NEW set. The re-adoption is forced onto the very next tick rather than left to
                // the round-robin, because a lap of the tile registry is exactly the gap
                // ModBuild 158's flicker lived in and this renderer is currently un-retuned.
                _reasserts++;
                ReleaseAt(i, restore: false);
                _lastTileCount = -1;
                _next = 0f;
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
        /// Find and retune every water-feature renderer under one root, in TWO passes.
        ///
        /// <para>Pass one adopts the FILM. Pass two adopts the BASIN, and it needs pass one to
        /// have run because the basin test is geometric — a piece only counts when it lies inside
        /// a film's own footprint. It is checked against <see cref="_filmBounds"/>, which holds
        /// every film tracked ANYWHERE, not merely the ones in this subtree, because Apparance
        /// parents these props flat under big section containers and there is no guarantee the
        /// bed and the film arrive under the same root. A basin piece that arrives before any
        /// film is simply not adopted this pass; it is retried on the next sweep, which the
        /// quarter-second round-robin guarantees.</para>
        ///
        /// <para><c>includeInactive: true</c> is deliberate and is half the flicker fix: a room in
        /// <c>Preview</c> keeps its generated content <c>SetActive(false)</c> until
        /// <c>ProceduralMapTile.ShowContent</c> reveals it, so a sweep that skipped inactive
        /// objects could only ever meet those renderers AFTER they were already on screen. Tuning
        /// an inactive renderer lands before its first submitted frame.</para>
        /// </summary>
        private void ExamineSubtree(Transform root)
        {
            _rendererScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, _rendererScratch);

            foreach (Renderer r in _rendererScratch)
            {
                if (r == null || _owned.ContainsKey(r) || !IsTerrainWater(r, _matScratch))
                    continue;
                Adopt(r, isFilm: true);
            }
            RefreshFilmBounds();

            if (WantBasin)
            {
                foreach (Renderer r in _rendererScratch)
                {
                    if (r == null || _owned.ContainsKey(r) || !IsBasinCandidate(r))
                        continue;
                    Adopt(r, isFilm: false);
                }
            }
            _rendererScratch.Clear();
        }

        /// <summary>
        /// Is this renderer part of a tracked water feature's SOLID geometry — the bed or the rim?
        /// NAME family and GEOMETRY, both required, and never the film itself.
        ///
        /// <para>Both terms are load-bearing and each covers the other's failure. The room's own
        /// floor (<c>CR_RU_Floor_01</c>) runs the SAME <c>Amp_Basic_N_MRAO</c> shader as the
        /// basin, so a shader test would retune the whole room off one pool; and a water-themed
        /// name can appear anywhere on the map, so a name test would reach across it. Together
        /// they select exactly <c>TERRAIN_Crypt_Water_02_Base</c> and <c>…_Edge</c>, which is
        /// what the hardware FLOOR CENSUS says is sitting under the film.</para>
        /// </summary>
        private bool IsBasinCandidate(Renderer r)
        {
            if (_filmBounds.Count == 0)
                return false;
            string n = r.name;
            // Our own spawned objects and the film itself are never basin.
            if (n.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                return false;
            if (!NameHasAny(n, BasinNameTokens))
                return false;
            if (IsWaterShader(r, _matScratch))
                return false;

            Bounds b = r.bounds;
            if (b.size.x > BasinMaxSpanWU || b.size.z > BasinMaxSpanWU)
                return false; // a room-wide mesh that happens to carry a matching name

            for (int i = 0; i < _filmBounds.Count; i++)
            {
                Bounds f = _filmBounds[i];
                if (b.max.y > f.max.y + BasinHeadroomWU)
                    continue; // stands IN the pool rather than being part of it
                if (b.max.x < f.min.x - BasinMarginXZ || b.min.x > f.max.x + BasinMarginXZ)
                    continue;
                if (b.max.z < f.min.z - BasinMarginXZ || b.min.z > f.max.z + BasinMarginXZ)
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Take ownership of one renderer's materials and write the retune.
        ///
        /// <para><see cref="Renderer.materials"/> is the whole point of this build: reading it
        /// INSTANTIATES every slot and assigns the copies back to the renderer, so from here on
        /// the renderer draws with materials nobody else shares and a write onto them cannot be
        /// swallowed by instancing, by a batch, or by a property block that never reached the
        /// shader. The price is ownership, which <see cref="ReleaseAt"/> pays.</para>
        /// </summary>
        private void Adopt(Renderer r, bool isFilm)
        {
            Material[] shared;
            Material[] instances;
            try
            {
                shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0)
                    return;
                instances = r.materials; // instantiates AND assigns
            }
            catch (Exception e)
            {
                VRLog.Info(Name,
                    "WATER SURFACE: could not take a material instance on '" + r.name + "' ("
                    + e.GetType().Name + ") — this renderer keeps the game's authored look and "
                    + "nothing else is affected.");
                return;
            }
            if (instances == null)
                return;

            var o = new Owned
            {
                Shared = shared,
                Instances = instances,
                IsFilm = isFilm,
                AuthoredProbeUsage = r.reflectionProbeUsage,
                EnforceFirstSlot = instances.Length > 0 && instances[0] != null,
            };
            _instancesMade += instances.Length;
            _liveInstances += instances.Length;
            _owned[r] = o;
            _tracked.Add(r);

            // A renderer with probe usage OFF cannot see our local probe at all — it takes the
            // scene default, which is the skybox, which is the swimming mirror. Nudging it to
            // BlendProbes is the smallest change that lets mechanism four reach it, it is
            // recorded and restored, and it is only done while there is a probe to reach.
            if (WantProbe && o.AuthoredProbeUsage == ReflectionProbeUsage.Off)
            {
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                o.ProbeUsageWasForced = true;
                _probeUsageForced++;
            }

            if (isFilm && _censusSample == null)
            {
                // Unity's == , not ??= : the quads are destroyed and re-instantiated constantly
                // (see the class header), so a C#-non-null sample can be a destroyed object and
                // the census would lose its probe/bounds reading for the rest of the session.
                _censusSample = r;
            }

            LogWaterSurfaceOnce(r, o);
            Apply(r, o);
        }

        /// <summary>Refresh the film footprint list the basin search and the probe boxes are both
        /// built from. Cheap (one bounds read per tracked film) and always current — a film that
        /// respawned somewhere else must not leave a stale rect behind, which is exactly the
        /// mistake a persistent rect would make here.</summary>
        private void RefreshFilmBounds()
        {
            _filmBounds.Clear();
            for (int i = 0; i < _tracked.Count; i++)
            {
                Renderer r = _tracked[i];
                if (r == null || !_owned.TryGetValue(r, out Owned o) || !o.IsFilm)
                    continue;
                Bounds b = r.bounds;
                // A film with a degenerate AABB would put a match rect AT THE WORLD ORIGIN, and
                // the basin test would then adopt whatever happens to sit there. A renderer that
                // has never been submitted can report exactly that, and these quads are placed
                // while their room is still inactive in Preview — so the guard is not
                // hypothetical. Such a film is simply skipped; the next tick reads it again.
                if (b.size.x <= 0.001f || b.size.z <= 0.001f)
                    continue;
                _filmBounds.Add(b);
            }
        }

        /// <summary>
        /// Prune the tracked set every slow tick, and re-apply ONLY when something changed.
        ///
        /// <para>ModBuild 159 re-wrote its property block on every tick as a belt-and-braces
        /// against a block the game might have replaced rather than cleared. A material instance
        /// needs no such thing: nothing but a material SWAP can change what these renderers draw
        /// with, and <see cref="EnforceEveryFrame"/> catches that by reference compare every
        /// frame. So the periodic re-apply is not merely redundant here, it is the wrong trade —
        /// a full pass walks each shader's property table and would allocate a few hundred small
        /// strings a second inside a VR frame for no observable benefit. Newly adopted renderers
        /// are written in <see cref="Adopt"/>; everything else waits for a dial to move.</para>
        /// </summary>
        private void ReassertAll()
        {
            Camera? head = Rig.VRRigDriver.HeadCamera;
            bool granted = head != null && (head.depthTextureMode & DepthTextureMode.Depth) != 0;
            float smoothness = WantedSmoothness;
            float opacity = WantedOpacity;
            float reflectivity = WantedReflectivity;
            bool basin = WantBasin;
            bool changed = granted != _depthGranted
                || !Mathf.Approximately(smoothness, _appliedSmoothness)
                || !Mathf.Approximately(opacity, _appliedOpacity)
                || !Mathf.Approximately(reflectivity, _appliedReflectivity)
                || basin != _appliedBasin;
            _depthGranted = granted;
            _appliedSmoothness = smoothness;
            _appliedOpacity = opacity;
            _appliedReflectivity = reflectivity;

            // BasinSurfaces turned OFF is a RELEASE, not a re-apply: the basin has to get its
            // authored material back for the A/B to mean anything.
            if (basin != _appliedBasin)
            {
                _appliedBasin = basin;
                if (basin)
                    _lastTileCount = -1; // turned ON: sweep every tile once so it takes at once
                else
                    ReleaseBasin();
            }

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Renderer r = _tracked[i];
                if (r == null)
                {
                    ReleaseAt(i, restore: false);
                    continue;
                }
                if (changed && _owned.TryGetValue(r, out Owned o))
                    Apply(r, o);
            }
            RefreshFilmBounds();
            if (changed)
                _nextCensus = 0f; // a state change is exactly when the census line is worth its cost
        }

        /// <summary>
        /// Write the retune onto the renderer's OWN material instances. Every value is read from
        /// the SHARED material — the tileset's own authored value — so nothing here can invent a
        /// look the tileset never had, and re-applying is exactly idempotent.
        /// </summary>
        private void Apply(Renderer r, Owned o)
        {
            for (int i = 0; i < o.Instances.Length; i++)
            {
                Material inst = o.Instances[i];
                Material shared = i < o.Shared.Length ? o.Shared[i] : null!;
                if (inst == null || shared == null || shared.shader == null)
                    continue;

                // Our instance is unique to this renderer, so no instancing batch can form over
                // it — but say so to the engine as well, because a batch is the one mechanism
                // that could take these values from a material other than this one, and it is the
                // prime suspect for why ModBuild 159's property block never landed.
                inst.enableInstancing = false;

                CapReflectionProperties(shared, inst, null);
                if (o.IsFilm)
                    ApplyWaterFilm(shared, inst);
            }
        }

        /// <summary>
        /// The generic half — walk the shader's OWN property table and cap everything that names
        /// itself a reflection. This is what replaces ModBuild 159's single hard-coded
        /// <c>_Smoothness</c> write, and it is the only approach that can reach a shader whose
        /// property names we have never read (see the class header: the game's shaders ship
        /// compiled and there is no install to open them with).
        /// </summary>
        /// <param name="report">When non-null, every property considered is appended with what
        /// was done or why nothing was — the census's decisive block.</param>
        private void CapReflectionProperties(
            Material shared, Material inst, System.Text.StringBuilder? report)
        {
            CapProp[]? plan = GetCapPlan(shared.shader);
            if (plan == null)
                return;
            float smoothnessCap = WantedSmoothness;
            float reflectivityCap = WantedReflectivity;

            int written = 0, refused = 0;
            for (int i = 0; i < plan.Length; i++)
            {
                CapProp p = plan[i];
                if (!p.IsScalar)
                {
                    // Only a SCALAR can be a strength. A Color or a Vector carrying a reflection
                    // word in its name (say a reflection tint) is left alone deliberately:
                    // capping a colour channel-wise would change the hue, and the hue is the
                    // tileset's.
                    refused++;
                    report?.Append(", ").Append(p.Name)
                          .Append(" REFUSED: reflection family but not a scalar");
                    continue;
                }

                float authored;
                try { authored = shared.GetFloat(p.Id); }
                catch { continue; }

                if (!WaterReflectionCaps.TryCap(
                        p.Family, authored, p.HasRange, p.Lo, p.Hi, smoothnessCap, reflectivityCap,
                        out float value, out string reason))
                {
                    refused++;
                    report?.Append(", ").Append(p.Name).Append(" REFUSED: ").Append(reason);
                    continue;
                }
                try { inst.SetFloat(p.Id, value); }
                catch { continue; }
                written++;
                report?.Append(", ").Append(p.Name).Append(' ').Append(reason);
            }
            report?.Append(" | ").Append(written).Append(" capped, ").Append(refused)
                  .Append(" refused");
            if (report != null && written == 0)
            {
                report.Append(" — NO REFLECTION-FAMILY SCALAR ON THIS SHADER: if this surface is "
                              + "still mirroring, its reflection comes from a texture (an MRAO "
                              + "pack) or from a constant inside the compiled shader, and only "
                              + "[Water] LocalProbe can reach it");
            }
        }

        /// <summary>
        /// The reflection-family properties of one shader, classified ONCE.
        ///
        /// <para>The classification is string work — a lowercase copy and a substring sweep per
        /// property name — and Apparance re-instantiates this content constantly, so a plan
        /// rebuilt on every adoption would put a few hundred small allocations into the frame a
        /// room reveals in. A shader's property table cannot change at runtime, so caching it is
        /// exact rather than merely cheap. Keyed by <see cref="Shader"/>, which is a shared asset:
        /// a handful of entries for a whole session.</para>
        /// </summary>
        private CapProp[]? GetCapPlan(Shader? sh)
        {
            if (sh == null)
                return null;
            if (_capPlans.TryGetValue(sh, out CapProp[] cached))
                return cached;

            var found = new List<CapProp>(4);
            try
            {
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    string pn = sh.GetPropertyName(i);
                    WaterCapFamily family = WaterReflectionCaps.Classify(pn);
                    if (family == WaterCapFamily.None)
                        continue;
                    ShaderPropertyType pt = sh.GetPropertyType(i);
                    bool scalar = pt == ShaderPropertyType.Float || pt == ShaderPropertyType.Range;
                    bool hasRange = pt == ShaderPropertyType.Range;
                    float lo = 0f, hi = 1f;
                    if (hasRange)
                    {
                        try
                        {
                            Vector2 limits = sh.GetPropertyRangeLimits(i);
                            lo = limits.x;
                            hi = limits.y;
                        }
                        catch { hasRange = false; }
                    }
                    found.Add(new CapProp(pn, family, scalar, hasRange, lo, hi));
                }
            }
            catch
            {
                // A shader whose table cannot be walked gets an EMPTY plan rather than none, so
                // the failure is paid for once instead of on every adoption.
            }
            CapProp[] plan = found.ToArray();
            _capPlans[sh] = plan;
            return plan;
        }

        /// <summary>
        /// The water FILM's own two treatments — the tint alpha, and the shore foam that has no
        /// depth texture to read. Both are per-property writes onto our instance, and the foam
        /// one clears a shader KEYWORD, which is the thing a property block could never do.
        /// </summary>
        private void ApplyWaterFilm(Material shared, Material inst)
        {
            Color tint = shared.HasProperty(ColorTintId) ? shared.GetColor(ColorTintId) : Color.white;
            // Hue as authored, alpha capped so the tiles read through.
            tint.a = Mathf.Min(tint.a, WantedOpacity);
            if (shared.HasProperty(ColorTintId))
                inst.SetColor(ColorTintId, tint);

            if (!shared.HasProperty(EdgeColourId) && !shared.HasProperty(EdgeToggleId))
                return;

            if (_depthGranted)
            {
                // The depth texture is actually there — hand the authored foam back, keyword and
                // all. Read from the shared material so this is exact.
                if (shared.HasProperty(EdgeColourId))
                    inst.SetColor(EdgeColourId, shared.GetColor(EdgeColourId));
                if (shared.HasProperty(EdgeToggleId))
                    inst.SetFloat(EdgeToggleId, shared.GetFloat(EdgeToggleId));
                if (shared.IsKeywordEnabled(EdgeColourKeyword))
                    inst.EnableKeyword(EdgeColourKeyword);
                else
                    inst.DisableKeyword(EdgeColourKeyword);
                return;
            }

            // No _CameraDepthTexture: the shader's edge band is pinned at one extreme for the
            // whole quad, and the authored edge colour is near-white RGBA(0.887,0.887,0.887) —
            // which is what spiegeltiles.jpg shows, hexes PALER than the stone around them.
            // Two independent neutralisations, because either one alone might be the inert half:
            //   (1) paint the edge in the BODY colour, so whichever extreme the unfed fade lands
            //       on there is nothing to draw with. Sign-independent, and it does not depend on
            //       knowing which way _InvertDepthFade points inside a shader we cannot open.
            //   (2) clear the keyword. The material ships with _EDGECOLOUR_TOGGLE_ON live, and an
            //       Amplify [Toggle(...)] property is compiled into a shader_feature branch that
            //       never reads the float — so (2) is very probably the write that matters and it
            //       was UNREACHABLE from ModBuild 159's property block.
            if (shared.HasProperty(EdgeColourId))
                inst.SetColor(EdgeColourId, tint);
            if (shared.HasProperty(EdgeToggleId))
                inst.SetFloat(EdgeToggleId, 0f);
            inst.DisableKeyword(EdgeColourKeyword);
        }

        // ---- OWNERSHIP -----------------------------------------------------------------------

        /// <summary>
        /// Give one renderer back and destroy the instances we made for it.
        /// <paramref name="restore"/> is false when the renderer is gone or when the game has
        /// already replaced its materials — in both cases the array we recorded is no longer what
        /// anything wants, and only the instances still need releasing.
        /// </summary>
        private void ReleaseAt(int index, bool restore)
        {
            Renderer r = _tracked[index];
            _tracked.RemoveAt(index);
            if (!_owned.TryGetValue(r, out Owned o))
                return;
            _owned.Remove(r);
            if (restore && r != null)
            {
                try
                {
                    r.sharedMaterials = o.Shared;
                    if (o.ProbeUsageWasForced)
                        r.reflectionProbeUsage = o.AuthoredProbeUsage;
                }
                catch { /* the renderer went away between the null check and here */ }
            }
            if (o.ProbeUsageWasForced)
                _probeUsageForced--;
            DestroyInstances(o);
        }

        private void DestroyInstances(Owned o)
        {
            for (int i = 0; i < o.Instances.Length; i++)
            {
                Material m = o.Instances[i];
                // Decrement whatever the slot holds: once this record is dropped we no longer
                // hold the reference either way, and a slot the engine already collected must
                // not leave the live count drifting upward for the rest of the session — that
                // count is the whole leak check.
                _liveInstances--;
                if (m == null)
                    continue;
                try { Object.Destroy(m); }
                catch { /* already gone with the scene */ }
                _instancesDestroyed++;
            }
            o.Instances = Array.Empty<Material>();
        }

        /// <summary>Hand the basin back without touching the film — the live A/B behind
        /// <c>[Water] BasinSurfaces</c>.</summary>
        private void ReleaseBasin()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Renderer r = _tracked[i];
                if (r != null && _owned.TryGetValue(r, out Owned o) && o.IsFilm)
                    continue;
                ReleaseAt(i, restore: true);
            }
        }

        /// <summary>Give everything back. Called on config flip, on uninstall and on destroy — a
        /// mod-owned material must never outlive its owner, and neither must a mod-spawned
        /// probe.</summary>
        internal void RestoreAll()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
                ReleaseAt(i, restore: true);
            _tracked.Clear();
            _owned.Clear();
            _filmBounds.Clear();
            _pending.Clear();
            _pendingOverflow = false;
            _lastTileCount = -1;
            _cursor = 0;
            DestroyProbes();
            _probeSig = int.MinValue;
        }

        private void OnDestroy()
        {
            try { RestoreAll(); }
            catch { /* teardown */ }
            if (_flatCube != null)
            {
                try { Object.Destroy(_flatCube); }
                catch { /* teardown */ }
                _flatCube = null;
            }
        }

        // ---- THE LOCAL REFLECTION PROBE --------------------------------------------------------

        /// <summary>
        /// Keep one local reflection probe over each water feature.
        ///
        /// <para>THE MECHANISM, stated plainly because it is the one that does not depend on any
        /// hypothesis being right: a skybox reflection is sampled at infinity, so its reflection
        /// vector sweeps the whole sky as the head moves and the reflected colour changes
        /// constantly. That IS the swimming. A probe carrying a FLAT cubemap returns one colour
        /// in every direction, so the reflected colour is constant however the head moves —
        /// whatever property, whatever renderer and whatever shader the reflection came from.
        /// Box projection stays OFF: it computes an intersection with the probe box and would put
        /// exactly the parallax back.</para>
        ///
        /// <para>Rebuilt only when the FOOTPRINT changed, not when the renderer set did. Apparance
        /// destroys and re-instantiates this content constantly (109 placements in one logged
        /// session) and a probe that blinked with every rebuild would pop the reflection once per
        /// respawn; the signature below is cut from rounded cluster geometry, so a pool that came
        /// back in the same place keeps the probe it already had.</para>
        /// </summary>
        private void MaintainProbes()
        {
            bool want = WantProbe;
            float brightness = WantedProbeBrightness;
            if (!want)
            {
                if (_appliedLocalProbe || _probes.Count > 0)
                {
                    DestroyProbes();
                    _probeSig = int.MinValue;
                    _probeNote = "[Water] LocalProbe is OFF — every surface here samples the "
                                 + "skybox again";
                }
                _appliedLocalProbe = false;
                return;
            }

            BuildClusters();
            int sig = ClusterSignature();
            bool dirty = sig != _probeSig
                         || !_appliedLocalProbe
                         || !Mathf.Approximately(brightness, _appliedProbeBrightness)
                         || AnyProbeLost();
            _appliedLocalProbe = true;
            _appliedProbeBrightness = brightness;
            if (!dirty)
                return;
            _probeSig = sig;

            DestroyProbes();
            if (_clusterScratch.Count == 0)
            {
                _probeNote = "no water feature tracked, so no probe is needed";
                return;
            }

            Color colour = FlatReflectionColour();
            Cubemap? cube = EnsureFlatCube(colour);
            if (cube == null)
            {
                _probeNote = "FLAT CUBEMAP COULD NOT BE CREATED — the local probe is unavailable "
                             + "on this build and the surfaces below still sample the skybox";
                return;
            }

            float intensity = Mathf.Max(0f, brightness * RenderSettings.reflectionIntensity);
            for (int i = 0; i < _clusterScratch.Count; i++)
            {
                Bounds b = _clusterScratch[i];
                var go = new GameObject(ProbeObjectName);
                // Deliberately NOT DontDestroyOnLoad: a probe belongs to the scene whose pool it
                // covers, and must go when that scene does.
                go.transform.position = b.center;
                var p = go.AddComponent<ReflectionProbe>();
                p.mode = ReflectionProbeMode.Custom;
                p.refreshMode = ReflectionProbeRefreshMode.ViaScripting; // Custom never renders
                p.customBakedTexture = cube;
                p.hdr = false;             // a plain LDR cube, not RGBM
                p.boxProjection = false;   // see the summary: projection = parallax = swimming
                p.blendDistance = 0f;      // a hard edge; nothing to blend the skybox back in
                p.intensity = intensity;
                p.size = b.size;
                p.center = Vector3.zero;
                p.importance = 1;          // a real tileset probe, if one ever appears, may win
                _probes.Add(p);
            }
            _probeNote = _probes.Count + " local flat-cubemap probe(s) at intensity "
                         + intensity.ToString("0.###") + ", colour " + colour;
        }

        /// <summary>Merge the tracked footprints (film AND basin) into as few boxes as there are
        /// separate pools. Union-merge over a handful of bounds; a room has one pool and a big
        /// scenario perhaps three.</summary>
        private void BuildClusters()
        {
            _clusterScratch.Clear();
            for (int i = 0; i < _tracked.Count; i++)
            {
                Renderer r = _tracked[i];
                if (r == null)
                    continue;
                Bounds b = r.bounds;
                b.Expand(ProbePadding * 2f);
                bool merged = false;
                for (int j = 0; j < _clusterScratch.Count; j++)
                {
                    Bounds c = _clusterScratch[j];
                    if (!c.Intersects(b))
                        continue;
                    c.Encapsulate(b);
                    _clusterScratch[j] = c;
                    merged = true;
                    break;
                }
                if (!merged)
                    _clusterScratch.Add(b);
            }
            // One consolidation pass: growing a cluster can bring it into contact with another.
            for (int i = _clusterScratch.Count - 1; i > 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (!_clusterScratch[j].Intersects(_clusterScratch[i]))
                        continue;
                    Bounds c = _clusterScratch[j];
                    c.Encapsulate(_clusterScratch[i]);
                    _clusterScratch[j] = c;
                    _clusterScratch.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>A geometry signature, rounded to a quarter world unit, so a pool that
        /// respawned in the same place does not churn its probe.</summary>
        private int ClusterSignature()
        {
            int sig = 17 + _clusterScratch.Count * 31;
            for (int i = 0; i < _clusterScratch.Count; i++)
            {
                Bounds b = _clusterScratch[i];
                sig = sig * 31 + Mathf.RoundToInt(b.center.x * 4f);
                sig = sig * 31 + Mathf.RoundToInt(b.center.y * 4f);
                sig = sig * 31 + Mathf.RoundToInt(b.center.z * 4f);
                sig = sig * 31 + Mathf.RoundToInt(b.size.x * 4f);
                sig = sig * 31 + Mathf.RoundToInt(b.size.z * 4f);
            }
            return sig;
        }

        /// <summary>Did a scene change take our probes out from under us? Cheap; the list holds
        /// one entry per pool.</summary>
        private bool AnyProbeLost()
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                if (_probes[i] == null)
                    return true;
            }
            return false;
        }

        private void DestroyProbes()
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                ReflectionProbe p = _probes[i];
                if (p == null)
                    continue;
                try { Object.Destroy(p.gameObject); }
                catch { /* the scene already took it */ }
            }
            _probes.Clear();
        }

        /// <summary>
        /// The colour a flat reflection should be: the scene's own ambient, so the pool reflects
        /// the light the room is actually lit by rather than a number we invented.
        /// <c>RenderSettings.ambientProbe</c> is the spherical harmonic Unity builds from the
        /// active skybox (<c>ambientMode=Skybox</c> in the report's room), so averaging its six
        /// axis evaluations gives the sky's mean colour without rendering anything.
        /// </summary>
        private static Color FlatReflectionColour()
        {
            try
            {
                var dirs = new[]
                {
                    Vector3.up, Vector3.down, Vector3.left,
                    Vector3.right, Vector3.forward, Vector3.back,
                };
                var res = new Color[dirs.Length];
                RenderSettings.ambientProbe.Evaluate(dirs, res);
                var sum = new Color(0f, 0f, 0f, 1f);
                for (int i = 0; i < res.Length; i++)
                {
                    sum.r += res[i].r;
                    sum.g += res[i].g;
                    sum.b += res[i].b;
                }
                float k = 1f / dirs.Length;
                var avg = new Color(
                    Mathf.Clamp01(sum.r * k), Mathf.Clamp01(sum.g * k), Mathf.Clamp01(sum.b * k), 1f);
                if (avg.r + avg.g + avg.b > 0.0001f)
                    return avg;
            }
            catch { /* fall through to the flat ambient below */ }
            Color a = RenderSettings.ambientLight;
            return new Color(Mathf.Clamp01(a.r), Mathf.Clamp01(a.g), Mathf.Clamp01(a.b), 1f);
        }

        /// <summary>Build (or recolour) the 4x4 flat cubemap every local probe shares. Tiny and
        /// created once: 96 pixels, no mipmaps, no render.</summary>
        private Cubemap? EnsureFlatCube(Color c)
        {
            try
            {
                if (_flatCube != null
                    && Mathf.Abs(_flatCubeColour.r - c.r) < 0.002f
                    && Mathf.Abs(_flatCubeColour.g - c.g) < 0.002f
                    && Mathf.Abs(_flatCubeColour.b - c.b) < 0.002f)
                {
                    return _flatCube;
                }
                if (_flatCube == null)
                {
                    _flatCube = new Cubemap(4, TextureFormat.RGBA32, false)
                    {
                        name = "GloomhavenVR.WaterFlatReflection",
                        hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                    };
                }
                var px = new Color[16];
                for (int i = 0; i < px.Length; i++)
                    px[i] = c;
                for (int face = 0; face < 6; face++)
                    _flatCube.SetPixels(px, (CubemapFace)face);
                _flatCube.Apply(false, false);
                _flatCubeColour = c;
                return _flatCube;
            }
            catch (Exception e)
            {
                VRLog.Info(Name,
                    "WATER SURFACE: the flat reflection cubemap could not be built ("
                    + e.GetType().Name + ") — [Water] LocalProbe is inert this session and the "
                    + "material caps are carrying the fix alone.");
                return null;
            }
        }

        // ---- THE CENSUS ---------------------------------------------------------------------

        /// <summary>
        /// The periodic half of the census: the state that CHANGES, on the same
        /// <c>WATER SURFACE</c> prefix as the heavy per-material dump so both grep together. It
        /// is written to DISCRIMINATE between the four mechanisms this build ships, because that
        /// is the single most valuable thing to have in hand if the look is still wrong.
        /// </summary>
        private void MaybeLogState()
        {
            if (_tracked.Count == 0 || Time.unscaledTime < _nextCensus)
                return;
            _nextCensus = Time.unscaledTime + CensusInterval;

            Camera? head = Rig.VRRigDriver.HeadCamera;
            int deltaReasserts = _reasserts - _lastCensusReasserts;
            _lastCensusReasserts = _reasserts;

            int films = 0;
            for (int i = 0; i < _tracked.Count; i++)
            {
                Renderer r = _tracked[i];
                if (r != null && _owned.TryGetValue(r, out Owned o) && o.IsFilm)
                    films++;
            }

            var sb = new System.Text.StringBuilder(1400);
            sb.Append("WATER SURFACE STATE: ").Append(_tracked.Count)
              .Append(" water-feature renderer(s) tracked and RENDERING (user ruling 2026-08-18: "
                      + "the water is never hidden) — ").Append(films)
              .Append(" film + ").Append(_tracked.Count - films)
              .Append(" basin. ENFORCEMENT = per-renderer MATERIAL INSTANCE (ModBuild 160 "
                      + "replaced ModBuild 159's MaterialPropertyBlock, which the user reported "
                      + "as having changed NOTHING even though it cut the tint alpha by a "
                      + "third), checked every frame by comparing Renderer.sharedMaterial "
                      + "against the instance we handed it and fully re-written every ")
              .Append(TickInterval.ToString("0.##"))
              .Append("s; no Renderer.enabled is written anywhere, so ModBuild 158's flicker race "
                      + "cannot recur by construction. UNDONE: ").Append(_reasserts)
              .Append(" material replacement(s) total, ").Append(deltaReasserts)
              .Append(" since the last line — 0 means nothing in the game is taking our material "
                      + "back off the renderer; any climb names "
                      + "MaterialLoaderData.CheckAllMaterialLoaded as a live race. INSTANCES: ")
              .Append(_liveInstances).Append(" live (").Append(_instancesMade).Append(" made, ")
              .Append(_instancesDestroyed)
              .Append(" destroyed) — live must track the renderer count, not climb with it.");

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

            sb.Append(" | RETUNE: sharpness ceiling ")
              .Append(_appliedSmoothness.ToString("0.###"))
              .Append(" (the film authors _Smoothness 0.754; a roughness property is floored at ")
              .Append((1f - _appliedSmoothness).ToString("0.###"))
              .Append(" instead), metal/reflection ceiling ")
              .Append(_appliedReflectivity.ToString("0.###"))
              .Append(", _Color_Tint.a capped at ").Append(_appliedOpacity.ToString("0.###"))
              .Append(" (authored 0.737), basin surfaces ")
              .Append(_appliedBasin ? "IN SCOPE" : "released ([Water] BasinSurfaces OFF)")
              .Append(", shore foam ")
              .Append(_depthGranted
                  ? "AUTHORED (depth granted)"
                  : "neutralised — edge colour set to the body colour AND the "
                    + "_EDGECOLOUR_TOGGLE_ON keyword cleared, which a property block could never "
                    + "have done")
              .Append(", probe usage forced Off->BlendProbes on ").Append(_probeUsageForced)
              .Append(" renderer(s).");

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

            // Mechanism four, and whether it reached anything.
            sb.Append(" | LOCAL PROBE: [Water] LocalProbe=").Append(WantProbe)
              .Append(" brightness=").Append(WantedProbeBrightness.ToString("0.##"))
              .Append(" -> ").Append(_probeNote);

            try
            {
                ReflectionProbe[] probes = Object.FindObjectsOfType<ReflectionProbe>();
                int ours = 0;
                for (int i = 0; i < probes.Length; i++)
                {
                    if (probes[i] != null && probes[i].name == ProbeObjectName)
                        ours++;
                }
                sb.Append(" | REFLECTION: liveProbes=").Append(probes.Length)
                  .Append(" (").Append(ours).Append(" ours) reflectionMode=")
                  .Append(RenderSettings.defaultReflectionMode)
                  .Append(" intensity=").Append(RenderSettings.reflectionIntensity.ToString("0.##"))
                  .Append(" customReflection=")
                  .Append(RenderSettings.customReflection != null
                      ? RenderSettings.customReflection.name : "<null>")
                  .Append(" skybox=")
                  .Append(RenderSettings.skybox != null ? RenderSettings.skybox.name : "<null>");
                Renderer? sample = _censusSample;
                if (sample != null)
                {
                    sb.Append(" sample='").Append(sample.name).Append("' probeUsage=")
                      .Append(sample.reflectionProbeUsage);
                    Vector3 at = sample.bounds.center;
                    int reaching = 0;
                    for (int i = 0; i < probes.Length; i++)
                    {
                        ReflectionProbe p = probes[i];
                        if (p == null || !p.isActiveAndEnabled || !p.bounds.Contains(at))
                            continue;
                        reaching++;
                        sb.Append(" REACHES['").Append(p.name).Append("' mode=").Append(p.mode)
                          .Append(" boxProjection=").Append(p.boxProjection)
                          .Append(" intensity=").Append(p.intensity.ToString("0.##"))
                          .Append(" cube=")
                          .Append(p.customBakedTexture != null
                              ? p.customBakedTexture.name : "<null>")
                          .Append(']');
                    }
                    if (reaching == 0)
                    {
                        sb.Append(" NO PROBE REACHES THE WATER — so the surface reflects the "
                                  + "SKYBOX and nothing else, which is a reflection that is "
                                  + "sampled at infinity and therefore sweeps with every head "
                                  + "movement. If [Water] LocalProbe is ON and this still says "
                                  + "zero, the probe is not being built where the water is and "
                                  + "mechanism four is not in play at all.");
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append(" | REFLECTION unreadable: ").Append(e.GetType().Name);
            }

            sb.Append(" WHAT WOULD DISPROVE THIS, mechanism by mechanism, so the next round is "
                      + "decidable from this line alone: (H1 the write never landed) it is dead "
                      + "by construction — there is no property block left; if the look changed "
                      + "at all this build, H1 was the story and the per-material 'instancing=' "
                      + "flag on the WATER SURFACE line above names why. (H2 the wrong property) "
                      + "read the per-shader cap list on that same line: if it says '0 capped' "
                      + "for a surface that still mirrors, no NAMED scalar governs its "
                      + "reflection and only the probe can. (H3 the mirror is the basin, not the "
                      + "film) toggle [Water] BasinSurfaces in the headset — if the pool changes "
                      + "with it, the metallic bed was the 'Tiles' the report meant; if a basin "
                      + "count of 0 appears above while the pool still mirrors, the geometric "
                      + "adoption test is too tight and the FLOOR CENSUS names what it missed. "
                      + "(H4 the environment) toggle [Water] LocalProbe — a mirror that stops "
                      + "SWIMMING but stays bright means the environment was the swim and a "
                      + "material cap is what is left to do; a mirror unchanged by a probe that "
                      + "REACHES above means the reflection is not coming through "
                      + "unity_SpecCube0 at all and the next move is the mod's own water shader.");

            VRLog.Info(Name, sb.ToString());
        }

        /// <summary>
        /// The heavy half of the census — one line per distinct material, cap 4 (the film's
        /// shader and the basin's, with room for a second tileset's pair). Everything in it is
        /// unavailable offline: the shaders ship compiled in the game's bundles and there is no
        /// game install on the build machine, so their property tables, their attribute strings
        /// and the values on the live materials can only be read in-process. This is the line
        /// that identified the surface in ModBuild 158 and it now also carries the two readings
        /// that settle ModBuild 159's failure: the material's <c>instancing</c> flag, and each
        /// property's declared ATTRIBUTES — which is where a <c>[Toggle(_EDGECOLOUR_TOGGLE_ON)]</c>
        /// says in the shader's own words that its float is never read.
        /// </summary>
        private void LogWaterSurfaceOnce(Renderer r, Owned o)
        {
            Material? mat = o.Shared.Length > 0 ? o.Shared[0] : null;
            string matName = mat != null ? mat.name : "<null>";
            string shaderName = mat != null && mat.shader != null ? mat.shader.name : "<null>";
            if (_censused.Count >= 4 || !_censused.Add(matName + "|" + shaderName))
                return;

            var sb = new System.Text.StringBuilder(2048);
            sb.Append("WATER SURFACE '").Append(r.name).Append("' (")
              .Append(o.IsFilm ? "FILM" : "BASIN").Append("): material '").Append(matName)
              .Append("', shader '").Append(shaderName).Append("', renderQueue ")
              .Append(mat != null ? mat.renderQueue : -1)
              .Append(", bounds y[").Append(r.bounds.min.y.ToString("F2")).Append("..")
              .Append(r.bounds.max.y.ToString("F2")).Append("], probeUsage=")
              .Append(r.reflectionProbeUsage).Append(", staticBatch=")
              .Append(r.isPartOfStaticBatch).Append(", instancing=")
              .Append(mat != null && mat.enableInstancing)
              .Append(" (instancing TRUE here is the retroactive answer to ModBuild 159: in the "
                      + "built-in pipeline a batched draw takes non-instanced properties from the "
                      + "MATERIAL, so a property block never reached the shader at all)");

            // --- what the reflection cap actually did on this shader (the decisive block) ------
            sb.Append(" | REFLECTION CAPS: ");
            try
            {
                Material? inst = o.Instances.Length > 0 ? o.Instances[0] : null;
                if (mat == null || mat.shader == null || inst == null)
                    sb.Append("<no material>");
                else
                    CapReflectionProperties(mat, inst, sb);
            }
            catch (Exception e)
            {
                sb.Append("unreadable: ").Append(e.GetType().Name);
            }

            // --- every property with its CURRENT value, its type and its ATTRIBUTES ------------
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
                        ShaderPropertyType pt = sh.GetPropertyType(i);
                        sb.Append(pn).Append('(').Append(pt);
                        if (pt == ShaderPropertyType.Range)
                        {
                            Vector2 lim = sh.GetPropertyRangeLimits(i);
                            sb.Append(' ').Append(lim.x.ToString("0.##")).Append("..")
                              .Append(lim.y.ToString("0.##"));
                        }
                        // The attribute strings are the reading that settles whether a [Toggle]
                        // property is consumed through a shader KEYWORD rather than as a float —
                        // i.e. whether ModBuild 159's _EdgeColour_Toggle write was inert.
                        string[] attrs = sh.GetPropertyAttributes(i);
                        if (attrs != null && attrs.Length > 0)
                            sb.Append(" [").Append(string.Join(",", attrs)).Append(']');
                        sb.Append(")=");
                        switch (pt)
                        {
                            case ShaderPropertyType.Texture:
                                Texture? tex = mat.GetTexture(pn);
                                sb.Append(tex == null
                                    ? "<null>"
                                    : $"'{tex.name}' {tex.width}x{tex.height} {tex.GetType().Name}");
                                break;
                            case ShaderPropertyType.Color:
                                sb.Append(mat.GetColor(pn));
                                break;
                            case ShaderPropertyType.Vector:
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
                  .Append(" flatProbeColour=").Append(FlatReflectionColour())
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
