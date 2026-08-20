using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core;

/// <summary>
/// THE MIRRORING FLOOR — user report 2026-08-15 (spiegeltiles.jpg), amended 2026-08-18.
/// Original: <i>"In dem Level das wir gespielt haben wurde die Bodentiles eines Raumes nicht
/// richtig dargestellt. Stattdessen war dort eine Fläche zu sehen die spiegelt und sie mit den
/// Kopfbewegungen ändert."</i> Amendment, and it is the shape of this file: <i>"Wichtige
/// Ergänzung: Das Wasser soll auf jeden Fall dargstellt werden - aber eben in einer
/// VR-freundlichen Variante. Einfach ausblenden ist keine Option."</i> ModBuild 158 hid the quads;
/// that path is GONE — this driver never writes <c>Renderer.enabled</c> and carries no hide dial.
/// The water renders; what changes is HOW.
///
/// <para>WHAT THE ROOM ACTUALLY CONTAINS, from the hardware FLOOR CENSUS — two surfaces, two
/// different complaints, and this driver treats them differently:
/// <code>
/// [FLOOR] 'TERRAIN_Crypt_Water_02_Edge' y[-0.2..0.0] sh='Amp_Basic_N_MRAO' q2000
/// [FLOOR] 'TERRAIN_Crypt_Water_02_Base' y[-0.3..-0.1] sh='Amp_Basic_N_MRAO' q2000
/// [FLOOR] 'TERRAIN_Water_Plane'         y[0.0..0.0]   sh='VFX/Water_Shd_Trans' q2900
/// </code>
/// The FILM is the transparent quad on the game's water shader. The BASIN is the opaque bed and
/// rim beneath it on <b>M</b>etallic/<b>R</b>oughness/<b>AO</b> — and a metallic surface in a
/// scene with <c>liveProbes=0</c> mirrors the skybox, which is infinitely far away and therefore
/// sweeps with every head movement. The user's own first word for the defect was <i>"Tiles"</i>,
/// not "Wasser".</para>
///
/// <para>THE FILM: ITS WHOLE MATERIAL IS REPLACED — <c>WaterSettings.OwnSurface</c>, default ON
/// (<see cref="WaterOwnSurface"/>). Four rounds of property tuning were read back OFF THE LIVE
/// MATERIAL INSTANCE and every one of them landed — every band width at 0, every band colour at
/// alpha 0, the keyword list empty, <c>_Smoothness 0.754 -> 0.08</c>, every metal/reflection
/// scalar at 0, <c>_Color_Tint.a</c> at 0.45 — and the verdict was <i>"Keine Änderungen bei der
/// Wasser Problematik."</i> A diagnostic paint (since retired) had already settled that these ARE
/// our renderers: <i>"Die debug farbe funktioniert - alles färbt sich magenta wie gewollt."</i>
/// Owned renderer + every reachable property neutral + the defect unchanged leaves one
/// explanation: the pale sheet and the head-bound reflection are a TEXTURE or a CONSTANT compiled
/// into <c>VFX/Water_Shd_Trans</c>, which no property name can reach. So the film draws on
/// <c>GloomhavenVR/WaterVR</c> out of this mod's own bundle instead, in the tileset's own colour
/// at the authored render queue, with the tileset's own normal map and a swell the shader's own
/// tessellator carries. ModBuild 162's flat sheet on <c>GloomhavenVR/Overlay</c> is the FALLBACK
/// when the bundle yields nothing else, and the OWN SURFACE census block names which of the two is
/// actually on the renderers rather than which was wanted.</para>
///
/// <para><b>THE HARD REQUIREMENT, and why the replacement has no specular in the usual sense:
/// nothing in it may depend on the view direction.</b> No <c>unity_SpecCube</c>, no
/// <c>reflect()</c>, no cube sample, no <c>worldRefl</c>, no Fresnel and no Blinn-Phong half
/// vector — a half-vector highlight slides across the surface with the head, which is the reported
/// defect re-created out of the mod's own shader. It is also the strongest stereo guarantee
/// available: under MultiPass each eye renders its own pass, so a view-dependent term is a
/// different image per eye, and this project has already parked one feature permanently over that
/// (<c>.planning/wall-fade-stereo-rivalry.md</c>). <c>WaterOwnSurfaceVectors</c> sweeps both
/// shaders' source and fails the build gate on a hit.</para>
///
/// <para>THE BASIN IS NOT REPLACED — it is opaque ground, not a film — so it keeps the PROPERTY
/// RETUNE, and that retune is what <c>WaterSettings.Smoothness</c>, <c>WaterSettings.Reflectivity</c> and
/// <c>WaterSettings.BasinSurfaces</c> still drive. It is not a hard-coded name list: the driver walks
/// the shader's WHOLE property table at runtime and caps every property whose name belongs to a
/// reflection family — gloss, metal, explicit reflection strength — flooring every ROUGHNESS
/// property instead, which is the same axis backwards. It has to work that way because the game's
/// shaders ship compiled inside <c>always_loaded_base*</c> and there is no game install on the
/// build machine to open them with. <see cref="WaterReflectionCaps"/> owns the classification,
/// holds the invariant that no cap can ever make a surface shinier, and is pinned by
/// <c>WaterReflectionVectors</c>. Every property found, its authored value, what was written and
/// every REFUSAL with its reason go in the census.</para>
///
/// <para>AND A MECHANISM THAT DOES NOT CARE WHICH SURFACE IS AT FAULT. Whatever samples
/// <c>unity_SpecCube0</c> in this room gets the skybox, because there is not one reflection probe
/// in the scene. <c>WaterSettings.LocalProbe</c> (default ON) puts a local
/// <see cref="ReflectionProbe"/> over each water feature in
/// <see cref="ReflectionProbeMode.Custom"/> mode, carrying a FLAT cubemap built from the scene's
/// own ambient probe. A flat cube returns the same colour in every direction, so the reflected
/// colour cannot change as the head turns — <b>no matter which property, which renderer or which
/// shader the reflection comes from.</b> Box projection is off explicitly: it would reintroduce
/// exactly the parallax being removed.</para>
///
/// <para>THE EDGE / FOAM / BORDER BAND still exists in the code and is reached only while the film
/// draws on the GAME's shader — <c>WaterSettings.OwnSurface</c> off, or a bundle that yielded neither
/// mod shader. With no <c>_CameraDepthTexture</c> the shader's depth term is constant across the
/// whole quad, so the near-white <c>_Edge_Colour</c> covers the hex instead of a shoreline; the
/// band is collapsed BY NUMBER (every width to 0, every colour to the body hue at alpha 0), which
/// draws nothing at EITHER extreme of a pinned fade and so does not depend on a sign nobody here
/// can read. <see cref="WaterEdgeBand"/> owns that arithmetic and its never-raise invariant. The
/// water no longer ASKS for a depth texture: the shipped film reads no depth at all, so a per-eye
/// opaque re-submission would buy it nothing.</para>
///
/// <para>THE WRITE IS A MATERIAL INSTANCE, NOT A PROPERTY BLOCK. ModBuild 159 dropped
/// <c>_Color_Tint</c>'s alpha through a <see cref="MaterialPropertyBlock"/> and the look was
/// reported IDENTICAL; the census answered why — <c>instancing=True</c> on both materials, and in
/// the built-in pipeline a batched instanced draw takes non-instanced properties from the
/// MATERIAL, so the block never reached the shader. Every tuned renderer now carries its own
/// <see cref="Renderer.materials"/> instance with <c>enableInstancing</c> off, so no batch can
/// form over it, and a keyword — which a property block cannot set at all — is reachable.</para>
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

    // --- the ANIMATION values, read off the GAME's shared material and handed to
    //     GloomhavenVR/WaterVR. Every one of these names was printed by the hardware WATER SURFACE
    //     census of TERRAIN_GEN_WaterPlane_Crypt_Mat, so none is guessed from a shader nobody can
    //     open. What is read is what the tileset authored; what is written is derived from it and
    //     from nothing else (WaterOwnSurface.TameTilings / NormalStrength), and the OWN SURFACE
    //     block prints the derivation so a reader of the log can check it against these numbers.
    //
    //     THE TILESET'S THREE SPEED VECTORS ARE NO LONGER AMONG THEM. _WaterUVAnimSpeedA/B and
    //     _WaterNoiseSpeed were read here through ModBuild 165 and resolved into scroll rates; the
    //     standing ruling is that the film has no current at all, so there is nothing left for a
    //     rate to drive. See WaterOwnSurface.ForbiddenTranslationProperties.
    private static readonly int NormalMapId =
        Shader.PropertyToID(WaterOwnSurface.NormalMapProperty);
    private static readonly int NormalTilingsId =
        Shader.PropertyToID(WaterOwnSurface.NormalTilingsProperty);
    private static readonly int NormalStrengthId =
        Shader.PropertyToID(WaterOwnSurface.GameNormalStrengthProperty);
    private static readonly int SmoothnessId =
        Shader.PropertyToID(WaterOwnSurface.SmoothnessProperty);

    /// <summary>Ids for <see cref="WaterEdgeBand.BandWidthProperties"/> and
    /// <see cref="WaterEdgeBand.BandColourProperties"/>, resolved once at type load and held
    /// parallel to the name arrays so the log can print the NAME of a property the live shader
    /// turns out not to declare. That loud line is the point: ModBuild 160 wrote by name into a
    /// shader nobody has read, and a name that silently does not exist is indistinguishable from a
    /// write that landed and did nothing.</summary>
    private static readonly int[] BandWidthIds = ToIds(WaterEdgeBand.BandWidthProperties);
    private static readonly int[] BandColourIds = ToIds(WaterEdgeBand.BandColourProperties);

    private static int[] ToIds(string[] names)
    {
        var ids = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
            ids[i] = Shader.PropertyToID(names[i]);
        return ids;
    }

    /// <summary>The keyword the water material ships ENABLED (<c>keywords=[_EDGECOLOUR_TOGGLE_ON]</c>,
    /// hardware census). An Amplify <c>[Toggle(…)]</c> property compiles to a
    /// <c>shader_feature</c> branch on exactly this keyword and never reads the float, which is
    /// why ModBuild 159's <c>_EdgeColour_Toggle = 0</c> could not have done anything through a
    /// property block. Clearing it needs a material, and now there is one.</summary>
    private const string EdgeColourKeyword = "_EDGECOLOUR_TOGGLE_ON";

    private static Driver? _driver;

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
        WaterSettings.AnnounceRetiredFile();
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
            + "option) and are retuned for a free camera. THE FILM does not run the game's "
            + "shader at all: WaterSettings.OwnSurface (default ON) gives it a material of the mod's "
            + "own on '" + WaterOwnSurface.FilmShaderName + "', in the tileset's own green at "
            + "the WaterSettings.Opacity alpha, at the authored render queue, carrying the tileset's "
            + "own normal map and a tessellated swell — with no shoreline, no foam, no depth "
            + "read, nothing that translates, and NO VIEW DIRECTION ANYWHERE IN IT (no cube "
            + "sample, no reflect(), no Fresnel, no half-vector specular, because a half-vector "
            + "highlight slides with the head exactly as the report describes and is a different "
            + "image in each MultiPass eye). Four rounds of tuning the game's shader were read "
            + "back off the live instance at every band width 0, every band colour at alpha 0, "
            + "an empty keyword list, _Smoothness 0.08 and the metal/reflection ceiling at 0, "
            + "and the verdict was still 'Keine Änderungen bei der Wasser Problematik' — which "
            + "is why the shader is replaced rather than addressed. ModBuild 162's flat sheet on "
            + "'" + WaterOwnSurface.FallbackFilmShaderName + "' is only the FALLBACK for a "
            + "bundle that does not yield the water shader ('Das Wasser sieht jetzt sehr viel "
            + "schlechter aus'). THE BASIN is opaque ground and keeps the property retune. Read "
            + "the OWN SURFACE block of the WATER SURFACE STATE line: it names which shader is "
            + "actually on the renderers and counts them, rather than which one was wanted.");
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

    /// <summary>
    /// The water look, as CONSTANTS. There is no <c>[Water]</c> config section any more and no
    /// <c>dev.gloomhavenvr.water.cfg</c> — this class is what stands where thirteen dials stood.
    ///
    /// <para>WHY THE DIALS WENT (user, hardware, 2026-08-20): "Das Menu im Erweiterten VR Menu das
    /// sich mit dem Wasser-Einstellungen und dem alten Mesh beschäftigt ist immer noch zu sehen —
    /// entferne das komplett (In der UI und im Code)." Every one of them existed to answer a
    /// question during the six rounds it took to get from the game's head-swimming mirror to a
    /// still crypt puddle: <c>DebugPaint</c> asked which renderers this driver owns,
    /// <c>LocalProbe</c> and <c>BasinSurfaces</c> asked whether the environment or the basin was
    /// the reflection, <c>OwnSurface</c> asked whether replacing the shader was worth it, and
    /// <c>RippleSpeed</c> / <c>SwellHeight</c> / <c>Shimmer</c> / <c>WaveScale</c> carried the
    /// tuning of the answer. All of them are answered and the answers are ruled on. What is left
    /// is not a preference a player has: an A/B switch whose OFF side restores a defect the user
    /// reported is not a setting, and the standing rule for this menu is that anything whose
    /// OFF-state breaks the experience is removed rather than defaulted.</para>
    ///
    /// <para>The values are ModBuild 168's verbatim — the build whose water the user accepted —
    /// so this is a removal of the dials and not a retune. Each one's reasoning lives at its
    /// field. Changing the look now means changing a number here and shipping a build, which is
    /// the point: the look is the mod's, not the session's.</para>
    /// </summary>
    internal static class WaterSettings
    {
        /// <summary>Retune the game's water FEATURE (the film TERRAIN_Water_Plane and the basin
        /// bed/rim under it) for a free VR camera. The water ALWAYS renders — user ruling
        /// 2026-08-18, "Einfach ausblenden ist keine Option" — this only ever changed how.</summary>
        internal const bool VRFriendlyWater = true;

        /// <summary>Ceiling for every SHARPNESS property on a water-feature shader. The film
        /// authors _Smoothness 0.754, which mirrors the skybox sharply — and a skybox is
        /// infinitely far away, so its reflection sweeps with your head. That was the original
        /// report ("kaputter Spiegel", spiegeltiles.jpg).</summary>
        internal const float Smoothness = 0.08f;

        /// <summary>Upper bound on the water tint's alpha (the game authors 0.737). Without a
        /// camera depth texture the shader's depth fade pins at its deepest and tints the whole
        /// quad, hiding the floor under the pool.</summary>
        internal const float Opacity = WaterOwnSurface.ShippedOpacity;

        /// <summary>Ceiling for every METALLIC and explicit reflection-strength property. The
        /// basin runs Amp_Basic_N_MRAO, and a metallic surface in a scene with no reflection
        /// probe IS a mirror of the skybox. 0 = wet stone rather than chrome.</summary>
        internal const float Reflectivity = 0f;

        /// <summary>Take the water feature's SOLID geometry into scope as well as the film — the
        /// sunken bed and the rim inside the water's own footprint. Through ModBuild 159 only the
        /// transparent film was touched, and the user's own word for the defect was "Tiles".</summary>
        internal const bool BasinSurfaces = true;

        /// <summary>Put a local reflection probe with a FLAT cubemap over each water feature. The
        /// scene ships no reflection probe at all (liveProbes=0), so everything glossy reflects
        /// the skybox; a flat cubemap returns the same colour in every direction, so the
        /// reflected colour cannot change with your head whatever produces it.</summary>
        internal const bool LocalProbe = true;

        /// <summary>Multiplier on that probe's brightness. 1 = as bright as the room already
        /// is (the scene's own ambient probe times its reflection intensity).</summary>
        internal const float ProbeBrightness = 1f;

        /// <summary>Draw the film with the mod's own shader instead of retuning the game's. Four
        /// rounds of property tuning were read back off the live material and every one landed —
        /// every foam and border width 0, every band colour alpha 0, the keyword list empty,
        /// every gloss and metal value 0 — and the pool was still a milky sheet with a reflection
        /// that swung with the head. That can only come from a texture or a constant compiled
        /// INSIDE the game's shader, and replacing the shader is what removes it.</summary>
        internal const bool OwnSurface = true;

        /// <summary>
        /// How fast the RIPPLE's crossfades run — the fine detail on the surface, NOT the swell.
        /// Nothing on this surface moves from place to place, so this can only mean "how often the
        /// three fixed ripple patterns trade places"; at 0.00875 they do so on 203 / 329 / 533 s.
        ///
        /// <para>THIS IS THE DIAL HE KEPT ASKING TO SLOW, and it stays exactly where ModBuild 168
        /// left it. It used to drive the swell too — see <see cref="SwellSpeed"/> for why that one
        /// number is the whole reason this took six rounds.</para>
        /// </summary>
        internal const float RippleSpeed = WaterOwnSurface.ShippedRippleSpeed;

        /// <summary>
        /// How fast the SWELL — the 3D relief, the thing that makes the pool read as water rather
        /// than as a painted sheet — rises and falls. Separate from <see cref="RippleSpeed"/> as of
        /// ModBuild 170, and the split is the actual fix rather than a refactor.
        ///
        /// <para>ONE NUMBER DROVE BOTH CLOCKS FOR SIX ROUNDS. Every "slower" he asked for was aimed
        /// at the busy fine detail, and every time it also slowed the ONE component whose motion he
        /// could see. Two halvings later the swell's longest component bobbed once every 126 s at a
        /// crest slope of 2.9 degrees, which comes to 0.19 degrees per second of surface-normal
        /// rotation — under the threshold at which anything reads as moving — and the verdict was
        /// "gar keine Animation beim Wasser mehr! Komplett stillstehend/freezed".</para>
        ///
        /// <para>RE-BASED FOR ModBuild 173, from 0.0124, and the reason is a correction rather than
        /// a preference. ModBuild 170 set 0.0124 believing that what an eye judges is the PRODUCT
        /// amplitude x steepness x frequency, and so bought visibility back with height while
        /// letting the clock stay slow. ModBuild 172 disproved it: at 0.0124 the surface moves 44 %
        /// MORE per second than ModBuild 167 did, and 167 was visible while 172 was reported as
        /// standing still. Temporal frequency has a FLOOR that amplitude cannot buy past — see
        /// <c>WaterOwnSurface.VisibleFastestPeriodSeconds</c> for the four measured builds that
        /// bracket it.</para>
        ///
        /// <para>0.0175 is ModBuild 167's own value: the SLOWEST setting the user has ever confirmed
        /// seeing move ("gerne noch langsamer" presupposes there was something to slow). Its fastest
        /// component runs 32.5 s = 0.031 Hz, inside the floor with margin. The doubled
        /// <see cref="SwellHeight"/> from 170 STAYS — it is what makes this read as gentle relief
        /// rather than as a twitch, and it is not the axis he has ever called hectic. Any further
        /// "calmer" must be answered with height, wavelength or shimmer; walking this number down
        /// again has now cost two rounds.</para>
        /// </summary>
        internal const float SwellSpeed = WaterOwnSurface.ShippedSwellSpeed;

        /// <summary>How strong the moving glints are. Made by the moving surface turning toward a
        /// FIXED light, so they are born on a crest and do NOT move when you move your head —
        /// which is the whole difference between this and the reflection that was reported. A
        /// broad dim sheen measured against the flat sheet's own brightness (still water glints
        /// exactly zero), and it never touches the film's opacity: adding the highlight into ALPHA
        /// is what made a crest go bright and opaque at once, i.e. a white streak by
        /// construction.</summary>
        internal const float Shimmer = WaterOwnSurface.ShippedShimmer;

        /// <summary>How BIG the waves are — one multiplier over the swell's wavelength AND both
        /// ripple layers, so the surface never comes apart into a big wave carrying wrong-sized
        /// detail. 1.0 makes the longest swell component 2.4 m, longer than either side of a water
        /// hex (1.73 x 2.0 m); a component shorter than a tile is what let ModBuild 164's surface
        /// read as the same figure on every tile.</summary>
        internal const float WaveScale = WaterOwnSurface.ShippedWaveScale;

        /// <summary>
        /// How HIGH the swell lifts the water, as a fraction of one water quad's own width — so a
        /// 2.6 m film gets a 2.6 cm peak and a diorama at another scale gets a wave that looks the
        /// same rather than one that is invisible or enormous. THIS IS REAL GEOMETRY: the film is
        /// subdivided by the shader's own tessellator and the vertices actually move, because
        /// "weiße Streifen auf einer flachen Oberfläche" was the verdict on trying to fake relief
        /// with shading.
        ///
        /// <para>DOUBLED FOR ModBuild 170, from 0.005, and this is the half of the freeze fix that
        /// does NOT speed anything up. Crest slope goes 2.9 -> 5.8 degrees. That is the axis he has
        /// never complained about: the slope has sat at 2.9 degrees through every one of the three
        /// "too fast" reports since ModBuild 166, and the two builds he DID call hectic ran 13.2 and
        /// 8.1 degrees while also translating, which was the real complaint. 5.8 leaves a 28 %
        /// margin under the lower of those two.</para>
        ///
        /// <para>The ceiling is unchanged and is the 9 cm the census measured between the water and
        /// the basin bed: a trough may never dip through its own pool floor. At 2.6 cm the peak is
        /// well under it.</para>
        /// </summary>
        internal const float SwellHeight = WaterOwnSurface.ShippedSwellHeight;

        /// <summary>
        /// The config file this module no longer has, announced once if a tester still has it.
        ///
        /// <para>A removed config key is silent by construction: the tuned value simply stops
        /// being read and the setting reverts with no message. Removing the whole SECTION is
        /// worse — BepInEx never opens <c>dev.gloomhavenvr.water.cfg</c> again, so the file sits
        /// on disk looking exactly as authoritative as its neighbours while nothing in the build
        /// can see it. This is the one line that says so. It reads the file, it never writes it:
        /// deleting a tester's tuning behind their back is not this function's business.</para>
        /// </summary>
        internal static void AnnounceRetiredFile()
        {
            try
            {
                string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                                                     "dev.gloomhavenvr.water.cfg");
                if (!System.IO.File.Exists(path))
                {
                    VRLog.Info(Name,
                        "WATER SURFACE: no legacy dev.gloomhavenvr.water.cfg on disk, so this "
                        + "machine never carried per-player water tuning and ModBuild 168 ran on "
                        + "the same numbers that are constants now.");
                    return;
                }

                // AND WHAT IT SAYS, WHICH IS THE POINT. ModBuild 168 and 169 shipped BIT-IDENTICAL
                // water — the only difference between them in this whole subsystem is comment
                // renames — and the verdicts were "Beide Probleme behoben, top" and then "gar keine
                // Animation ... komplett freezed". Identical code cannot produce opposite results,
                // so something outside it changed, and the obvious candidate is THIS FILE: it was
                // live at 168 and 169 is the build that retired the section under it. If it holds
                // values other than the shipped ones, the water the user approved was never the
                // water this build draws, and three rounds of tuning were aimed at the wrong
                // number. Read-only: deleting someone's tuning behind their back is not this
                // function's business, and neither is quietly adopting it.
                var found = new List<string>(8);
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == '[')
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                        found.Add(line.Substring(0, eq).Trim() + "=" + line.Substring(eq + 1).Trim());
                }
                VRLog.Info(Name,
                    "WATER SURFACE: the [Water] config section is RETIRED and " + path + " is "
                    + "INERT — nothing in this build reads it. WHAT IT STILL CONTAINS, because "
                    + "ModBuild 168's water was accepted and 169's identical water was not, and "
                    + "this file is the only thing that changed under them: ["
                    + (found.Count == 0 ? "no key=value lines" : string.Join(", ", found.ToArray()))
                    + "]. COMPARE EACH AGAINST THE SHIPPED CONSTANT on the WATER SURFACE STATE "
                    + "line — RippleSpeed " + WaterOwnSurface.ShippedRippleSpeed.ToString("0.#####")
                    + ", SwellHeight " + WaterOwnSurface.ShippedSwellHeight.ToString("0.#####")
                    + ", WaveScale " + WaterOwnSurface.ShippedWaveScale.ToString("0.###")
                    + ", Shimmer " + WaterOwnSurface.ShippedShimmer.ToString("0.###")
                    + ". A DIFFERENCE HERE IS THE ANSWER: it means the surface the user approved "
                    + "ran on these values and not on the defaults, and the shipped constants "
                    + "should be re-based onto them rather than tuned further. The file is "
                    + "otherwise harmless and can be deleted once that is settled.");
            }
            catch { /* diagnostics only — an unreadable config path must never block install */ }
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

        /// <summary>Config values the tracked set was last written with, so a live dial change
        /// re-applies without a per-frame value compare on every material.</summary>
        private float _appliedSmoothness = float.NaN;
        private float _appliedOpacity = float.NaN;
        private float _appliedReflectivity = float.NaN;
        private float _appliedRippleSpeed = float.NaN;
        private float _appliedShimmer = float.NaN;
        private float _appliedWaveScale = float.NaN;
        private float _appliedSwellHeight = float.NaN;

        /// <summary>
        /// What the last bounds pad DID, or why it did nothing. Printed by the census's FILM MESH
        /// block.
        ///
        /// <para>THE DEFAULT SAYS WHAT IS ON SCREEN, not what has not happened yet. ModBuild 164's
        /// default here read "no film mesh handled yet", the census printed it, and it was taken as
        /// a timing note when it actually meant that the round had shipped with no added geometry
        /// at all — a whole hardware round lost to a log line that reported an intention. Every
        /// state this string can be in now names the consequence for what the player sees.</para>
        /// </summary>
        private string _swellNote =
            "NOT PADDED — no film has reached PadFilmBounds in this driver, so any film on screen "
            + "is being culled against its UNDISPLACED bounds and its crests can vanish as you "
            + "approach, one eye first under MultiPass";
        private bool _appliedBasin = true;

        /// <summary>Whether the tracked FILMS currently carry the mod's own material. A change
        /// here is NOT a re-apply: <c>Material.shader =</c> has no exact inverse, so a flip
        /// releases everything and lets the next tick re-adopt off the authored shared
        /// material.</summary>
        private bool _appliedOwnSurface;

        /// <summary>The mod's own film shader — <see cref="WaterOwnSurface.FilmShaderName"/> when
        /// the bundle yields it, else <see cref="WaterOwnSurface.FallbackFilmShaderName"/> —
        /// resolved once through <see cref="BundleShaders"/>. Null while nothing has asked for it,
        /// or when NEITHER could be reached, in which case every film KEEPS the game's material and
        /// the census says so loudly, because a silent fallback here would look exactly like a
        /// round that changed nothing.</summary>
        private Shader? _ownShader;

        /// <summary>Which of the two the resolved shader IS. True is the animated water; false is
        /// ModBuild 162's flat sheet, which is a strictly worse look and which the census names
        /// rather than glossing — "the water stopped moving again" and "the shader never loaded"
        /// are the same photograph otherwise.</summary>
        private bool _ownIsWater;

        /// <summary>The resolved shader's declared name, so every log line reports what is on the
        /// renderers rather than what was asked for.</summary>
        private string _ownShaderName = WaterOwnSurface.FilmShaderName;

        /// <summary>The derivation of the last film colour written, verbatim from
        /// <see cref="WaterOwnSurface.TryBuildFilmColour"/> — printed in the OWN SURFACE block so a
        /// reader of the log can check it against the authored numbers on the same line.</summary>
        private string _ownNote = "not attempted yet";

        /// <summary>What the last film's ANIMATION was derived from: which authored values were
        /// read off the game material, which had to fall back to the measured constants, and what
        /// they resolved to. Printed in the OWN SURFACE block for the same reason
        /// <see cref="_ownNote"/> is — a scroll rate nobody can check is a scroll rate nobody can
        /// argue about.</summary>
        private string _animNote = "not attempted yet";

        /// <summary>Where the glints are lit from and which source won, verbatim from
        /// <see cref="WaterOwnSurface.TryBuildLightDirection"/>.</summary>
        private string _lightNote = "not attempted yet";

        /// <summary>The value written into <c>_LightDir</c> — surface-local, see
        /// <see cref="WaterOwnSurface.TryBuildLightDirection"/>. Initialised to the fixed constant
        /// rather than to zero: a zero light direction is a black, still film, and a field that is
        /// only correct after the first successful resolve would make a one-tick window in which
        /// the water looked like the bug.</summary>
        private Vector4 _lightLocal = new(
            WaterOwnSurface.DefaultLightLocal.x,
            WaterOwnSurface.DefaultLightLocal.y,
            WaterOwnSurface.DefaultLightLocal.z, 0f);

        /// <summary>The scene light the glints are currently lit by, cached so the slow tick does
        /// not walk the scene for it. Null means the fixed constant is in use.</summary>
        private Light? _sun;

        /// <summary>Set when <see cref="_lightLocal"/> actually moved, so
        /// <see cref="ReassertAll"/> re-writes the live instances exactly once instead of every
        /// tick.</summary>
        private bool _lightDirty = true;

        /// <summary>When the next scan for a directional light may run. <see cref="RenderSettings.sun"/>
        /// is a free property and is tried every tick; the SCAN behind it is not, so it is rate
        /// limited. A scenario's lights arrive with the scene, so a few seconds of the fixed
        /// constant at load is invisible — an unbounded per-tick scan would not be.</summary>
        private float _nextLightScan;

        private const float LightScanInterval = 5f;

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

            // ---- THE FILM'S BOUNDS. The swell displaces geometry after culling has already run,
            //      so the renderer's bounds have to contain where the surface GOES and not only
            //      where its mesh sits. ModBuild 164 did that by swapping in a padded, subdivided
            //      copy of the mesh; that path is gone (the game's meshes are not readable, so it
            //      could never have run) and the pad now goes straight onto Renderer.localBounds,
            //      which needs no access to the mesh's data at all and is undone exactly by
            //      Renderer.ResetLocalBounds().

            /// <summary>True once we have written <see cref="Renderer.localBounds"/> on this
            /// renderer, so release knows there is an override to reset. A renderer we never
            /// padded must not be reset — that would discard an override the GAME had set.</summary>
            internal bool BoundsPadded;

            /// <summary>The film's largest horizontal extent in world units, measured off the
            /// renderer BEFORE the pad is written. The swell's amplitude is a fraction of this, so
            /// it must never be read back off our own padded bounds — that would grow the wave a
            /// little every time it was measured.</summary>
            internal float FilmWidthWU;

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
            _appliedOwnSurface = WantOwnSurface;
            _tick = Tick;
        }

        // The look is fixed in WaterSettings; these stay as named accessors rather than being
        // inlined at the ~40 call sites, so the census keeps printing WHAT VALUE WAS IN FORCE
        // instead of a literal, and a future retune is still one edit.
        private static bool Want => VRSession.IsRunning && WaterSettings.VRFriendlyWater;

        private static float WantedSmoothness => WaterSettings.Smoothness;

        private static float WantedOpacity => WaterSettings.Opacity;

        private static float WantedReflectivity => WaterSettings.Reflectivity;

        private static bool WantBasin => WaterSettings.BasinSurfaces;

        private static bool WantProbe => WaterSettings.LocalProbe;

        private static float WantedProbeBrightness => WaterSettings.ProbeBrightness;

        private static bool WantOwnSurface => WaterSettings.OwnSurface;

        private static float WantedRippleSpeed => WaterSettings.RippleSpeed;

        private static float WantedSwellSpeed => WaterSettings.SwellSpeed;

        private static float WantedShimmer => WaterSettings.Shimmer;

        private static float WantedWaveScale => WaterSettings.WaveScale;

        private static float WantedSwellHeight => WaterSettings.SwellHeight;

        /// <summary>Is the film actually drawing on the mod's own shader right now? The dial ANDed
        /// with the shader having been reached — never the dial alone. Four rounds have ended with
        /// a log full of intentions, so nothing in this file may report a wish as an outcome, and
        /// a bundle that failed to yield the shader has to leave every dependent decision (the
        /// band writes, the census wording) exactly where it was.</summary>
        private bool OwnSurfaceActive => WantOwnSurface && _ownShader != null;

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
                        "WATER SURFACE: WaterSettings.VRFriendlyWater turned OFF — every water-feature "
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

            // Before Discover AND before ReassertAll: Adopt() writes the film in the same call, so
            // a shader that resolved only afterwards would leave the first room's water on the
            // game's material until something else moved. One dictionary hit once it has
            // succeeded.
            MaintainOwnShader();
            MaintainLightDirection();
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

            // APPLY FIRST, THEN THE CENSUS — and this order is the fix for the defect that cost
            // ModBuild 164 its whole round. The heavy line reports what this driver DID to the
            // renderer (the bounds pad, the resolved animation, the readback off the live
            // instance); it used to be written BEFORE the first Apply had run, so its BOUNDS PAD /
            // MESH SWAP field could only ever print the field's initial value. It printed "no film
            // mesh handled yet", that read as a timing note, and it meant the build had shipped
            // with no added geometry at all. A log line that reports an intention is worse than no
            // line, because it is believed.
            Apply(r, o);
            LogWaterSurfaceOnce(r, o);
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
            // THE OwnSurface FLIP IS A RELEASE, NOT A RE-APPLY. It swaps the SHADER on our
            // material instance, and Material.shader= has no exact inverse: property values the
            // other shader does not declare are gone, and the ones it shares have been
            // overwritten. The only restoration this module is willing to claim is the one it can
            // prove — hand every renderer its AUTHORED shared material back, destroy the
            // instances, and re-adopt from scratch on the next tick. One frame of the game's own
            // water is the whole cost, and it is the same path RestoreAll guarantees on uninstall.
            bool own = WantOwnSurface;
            if (own != _appliedOwnSurface)
            {
                _appliedOwnSurface = own;
                if (own)
                    MaintainOwnShader();
                int hadOwn = _tracked.Count;
                RestoreAll();
                _next = 0f;
                _nextCensus = 0f;
                VRLog.Info(Name,
                    "WATER SURFACE: WaterSettings.OwnSurface went " + (own ? "ON" : "OFF") + " — "
                    + hadOwn + " tracked renderer(s) were handed their AUTHORED shared material "
                    + "back and every material instance we owned was destroyed; the next frame "
                    + "re-adopts them and "
                    + (own
                        ? "draws the water FILM on '" + _ownShaderName
                          + "' out of the mod's own bundle, in the tileset's own green at the "
                          + "WaterSettings.Opacity alpha, rippling on the tileset's own normal map at "
                          + "its own two tilings and speeds, with no shoreline, no foam and no "
                          + "view-dependent term of any kind. The basin keeps the property retune. "
                          + "Read the OWN SURFACE block of the next WATER SURFACE STATE line: it "
                          + "names which of the two mod shaders is ACTUALLY on the renderers and "
                          + "counts them, not the ones we tried to swap."
                        : "puts the game's own VFX/Water_Shd_Trans back on the film, band collapse "
                          + "and all, which is the A/B for what the replacement is worth."));
                return;
            }

            float smoothness = WantedSmoothness;
            float opacity = WantedOpacity;
            float reflectivity = WantedReflectivity;
            bool basin = WantBasin;
            float rippleSpeed = WantedRippleSpeed;
            float shimmer = WantedShimmer;
            float waveScale = WantedWaveScale;
            float swellHeight = WantedSwellHeight;
            bool changed = !Mathf.Approximately(smoothness, _appliedSmoothness)
                || !Mathf.Approximately(opacity, _appliedOpacity)
                || !Mathf.Approximately(reflectivity, _appliedReflectivity)
                || !Mathf.Approximately(rippleSpeed, _appliedRippleSpeed)
                || !Mathf.Approximately(shimmer, _appliedShimmer)
                || !Mathf.Approximately(waveScale, _appliedWaveScale)
                // A SwellHeight change is not only a property write: at 0 the film gives its
                // authored bounds back and at anything else it takes the padded ones, so this
                // compare is what drives the bounds override as well.
                || !Mathf.Approximately(swellHeight, _appliedSwellHeight)
                || basin != _appliedBasin
                // The scene's light can arrive after the water does (a scenario loads its lights
                // with the rest of the room), so this is an EVENT and not a config compare: without
                // it the first room's film would keep the fixed constant for as long as it lived
                // and the census would report a light nothing on screen was using.
                || _lightDirty;
            _lightDirty = false;
            _appliedRippleSpeed = rippleSpeed;
            _appliedShimmer = shimmer;
            _appliedWaveScale = waveScale;
            _appliedSwellHeight = swellHeight;
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
            // THE BOUNDS FIRST, because the amplitude the material writes below is a fraction of
            // the width measured here and the pad must not be inside that measurement. The bounds
            // override is the one part of this retune that is not a material property, so it is
            // also the one part that has to be given back: every path that stops wanting the swell
            // — OwnSurface off, SwellHeight 0, release, uninstall — goes through
            // RestoreFilmBounds.
            if (o.IsFilm && OwnSurfaceActive && WantedSwellHeight > 0f)
                PadFilmBounds(r, o);
            else
                RestoreFilmBounds(r, o);

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

                // ROUND FIVE. The FILM's whole material is REPLACED rather than retuned, so none
                // of the band or reflection writes below apply to it: those name properties of
                // VFX/Water_Shd_Trans, and the instance no longer runs that shader. Skipping them
                // is not an optimisation — writing them would fill the census with "ABSENT FROM
                // THIS SHADER" lines about a shader that is deliberately gone, and that is exactly
                // the kind of line four rounds have already misread as a finding.
                if (o.IsFilm && OwnSurfaceActive)
                {
                    OwnFilmOne(o, shared, inst);
                    continue;
                }

                CapReflectionProperties(shared, inst, null);
                if (o.IsFilm)
                    ApplyWaterFilm(shared, inst, null);
            }
        }

        // ---- ROUND FIVE: THE MOD'S OWN FILM ----------------------------------------------------

        /// <summary>
        /// Re-base ONE film material instance onto the mod's own shader, in the tileset's own
        /// colour.
        ///
        /// <para>WHY THE WHOLE MATERIAL AND NOT ANOTHER PROPERTY. Two facts from hardware close the
        /// question between them. First, the diagnostic paint (since retired) was run and the user's
        /// verdict is <i>"Die debug farbe funktioniert - alles färbt sich magenta wie gewollt"</i>
        /// — so these ARE the renderers in <c>spiegeltiles.jpg</c>, and "we are tuning the wrong
        /// objects" is dead, measured. Second, ModBuild 161's read-back proves every property this
        /// module can reach is already neutral on the live instance, and his verdict on that was
        /// <i>"Keine Änderungen bei der Wasser Problematik"</i> plus <i>"Ich konnte aber mit den
        /// anderen Einstellungen die kopf-gebundene Reflektion nicht deaktivieren, egal was ich
        /// eingestellt hab."</i> Owned renderer + every reachable property neutral + the defect
        /// unchanged = the pale sheet and the head-bound reflection are a TEXTURE or a CONSTANT
        /// compiled into <c>VFX/Water_Shd_Trans</c>. Nothing addressed by name can reach either.
        /// Replacing the shader removes the whole of it, including whatever that is.</para>
        ///
        /// <para>WHY <c>GloomhavenVR/WaterVR</c> AND WHAT IT DOES. ModBuild 162 put the film on
        /// <c>GloomhavenVR/Overlay</c>, a flat unlit sheet, and the user's verdict was that the
        /// defect was gone and the look with it: <i>"Das Wasser sieht jetzt sehr viel schlechter
        /// aus. Das echte Wasser hatte ANimation und co. das will ich auch wieder."</i> The film now
        /// draws on the mod's own water shader: the tileset's own <c>_Normal_Map</c> at its own two
        /// <c>_NormalTilings</c>, shaded against a FIXED light direction, over a tessellated swell.
        /// The tileset's own SCROLL RATES are deliberately not read — nothing on this surface
        /// translates (<see cref="WaterOwnSurface.ForbiddenTranslationProperties"/>) — so the
        /// ripple is sampled at fixed frames of the world plane and crossfaded instead. Every other
        /// value below is read off the game's SHARED material here and handed straight over;
        /// nothing is a look chosen in this file.</para>
        ///
        /// <para>THE HARD REQUIREMENT IS UNCHANGED: no view-dependent term of any kind, because a
        /// head-bound reflection is precisely the thing that cannot survive this build. Neither
        /// shader contains <c>unity_SpecCube</c>, <c>reflect()</c>, a cube sample, <c>worldRefl</c>,
        /// a Fresnel or a half-vector specular — verified by sweeping both .shader files in
        /// <c>WaterOwnSurfaceVectors</c>, which fails the build gate on a hit rather than trusting
        /// this paragraph. <c>GloomhavenVR/EnvPuddle</c> remains DISQUALIFIED on exactly that: it
        /// computes <c>float3 R = reflect(-V, N)</c> and builds its moon and candle mirror images
        /// out of it. See <see cref="WaterOwnSurface"/> for the second, independent reason it could
        /// not be used here.</para>
        ///
        /// <para>The instance keeps the AUTHORED render queue, so the film draws exactly where the
        /// water drew. No <c>_MainTex</c> is bound: it declares a <c>"white"</c> default, so
        /// clearing it leaves the tint alone on screen — and a texture compiled into the game's
        /// shader is the surviving suspect for the pale sheet, so binding one would be the one move
        /// guaranteed to keep that question open. The RIPPLE normal map is a different thing
        /// entirely and IS bound: it is the tileset's own, it is read off the game material, and it
        /// carries no colour into the frame — only slope.</para>
        /// </summary>
        private void OwnFilmOne(Owned o, Material shared, Material inst)
        {
            Shader? own = _ownShader;
            if (own == null)
                return;
            try
            {
                Color authored = shared.HasProperty(ColorTintId)
                    ? shared.GetColor(ColorTintId)
                    : Color.white;
                if (!WaterOwnSurface.TryBuildFilmColour(
                        authored, WantedOpacity, out Color film, out string why))
                {
                    // Refused: leave this renderer on the game's material rather than paint it in
                    // a colour this module invented. The census prints the reason.
                    _ownNote = why;
                    return;
                }
                _ownNote = why;

                inst.shaderKeywords = Array.Empty<string>();
                inst.shader = own;
                inst.SetColor(WaterOwnSurface.TintProperty, film);
                inst.SetTexture(WaterOwnSurface.MainTexProperty, null);
                inst.SetFloat(WaterOwnSurface.CullProperty, WaterOwnSurface.CullOff);
                inst.SetFloat(WaterOwnSurface.ZWriteProperty, WaterOwnSurface.ZWriteOff);
                inst.SetFloat(WaterOwnSurface.ZTestProperty, WaterOwnSurface.ZTestLessEqual);
                // STRAIGHT ALPHA, never additive — see WaterOwnSurface.SrcBlendSrcAlpha: One/One
                // here would re-create the photographed defect out of the mod's own shader.
                inst.SetFloat(
                    WaterOwnSurface.SrcBlendProperty, WaterOwnSurface.SrcBlendSrcAlpha);
                inst.SetFloat(
                    WaterOwnSurface.DstBlendProperty, WaterOwnSurface.DstBlendOneMinusSrcAlpha);
                inst.renderQueue = shared.renderQueue;
                if (_ownIsWater)
                    AnimateFilmOne(o, shared, inst);
            }
            catch (Exception e)
            {
                _ownNote = "the material swap threw (" + e.GetType().Name + ") on '" + shared.name
                           + "' — that renderer keeps the game's water, and the OWN SURFACE count "
                           + "below is what says how many actually carry our shader";
            }
        }

        /// <summary>
        /// Hand <c>GloomhavenVR/WaterVR</c> the tileset's own animation, read off the game's SHARED
        /// material one property at a time.
        ///
        /// <para>EVERY READ FALLS BACK TO THE MEASURED CONSTANT AND SAYS SO. The hardware census of
        /// <c>TERRAIN_GEN_WaterPlane_Crypt_Mat</c> read all of these off the live material, so the
        /// constants in <see cref="WaterOwnSurface"/> are not defaults somebody liked — they are
        /// what the report's own tileset authors. A tileset that spells one of them differently
        /// gets the measured value for that one property and its own for the rest, and
        /// <see cref="_animNote"/> names exactly which were READ and which were DEFAULTED. Without
        /// that distinction a shader animating off the wrong numbers and a shader animating off the
        /// right ones are the same photograph.</para>
        ///
        /// <para>AND IF THERE IS NO NORMAL MAP, THE SURFACE STILL MOVES. A material with no
        /// <c>_Normal_Map</c> would leave the shader sampling its <c>"bump"</c> default, which is a
        /// perfectly flat normal — i.e. exactly ModBuild 162's still sheet, arrived at silently.
        /// <c>_ProcNormal</c> switches the shader to an analytic two-wave ripple instead, and the
        /// census says which of the two is live.</para>
        /// </summary>
        private void AnimateFilmOne(Owned o, Material shared, Material inst)
        {
            bool haveTex = shared.HasProperty(NormalMapId);
            Texture? bump = haveTex ? shared.GetTexture(NormalMapId) : null;

            bool haveTilings = shared.HasProperty(NormalTilingsId);
            Vector4 tilings = haveTilings
                ? shared.GetVector(NormalTilingsId)
                : WaterOwnSurface.AuthoredTilings;

            bool haveStr = shared.HasProperty(NormalStrengthId);
            Vector4 strVec = haveStr
                ? shared.GetVector(NormalStrengthId)
                : WaterOwnSurface.AuthoredNormalStrength;

            bool haveSmooth = shared.HasProperty(SmoothnessId);
            float smoothness = haveSmooth
                ? shared.GetFloat(SmoothnessId)
                : WaterOwnSurface.AuthoredSmoothness;

            float dial = WantedRippleSpeed;
            float waveScale = WantedWaveScale;
            float strength = WaterOwnSurface.NormalStrength(strVec);

            // THE TILINGS ARE RESOLVED, NOT PASSED THROUGH. The authored (0.14, 6.00) is 43:1
            // anisotropy — a band 7.1 m long and 17 cm wide, which is what "weiße Streifen" IS —
            // and the two layers used to be summed at equal weight, so the fine one competed with
            // the coarse one instead of riding it. See WaterOwnSurface.TameTilings/LayerWeights.
            Vector4 resolved = WaterOwnSurface.TameTilings(tilings, waveScale);
            Vector4 weights = WaterOwnSurface.LayerWeights(resolved);

            // THE SWELL, in world units, off THIS quad's own width — so the same dial gives the
            // same-looking wave in a diorama at another scale. The geometry that carries it is
            // produced by the shader's own tessellator, so unlike ModBuild 164 there is no mesh
            // handover that can silently refuse; what CAN still be missing is the tessellation
            // stage itself on a device below shader model 4.6, and the census reports that as a
            // measured device capability rather than as an assumption.
            float amp = WaterOwnSurface.SwellAmplitude(o.FilmWidthWU, WantedSwellHeight);
            float swellWave = WaterOwnSurface.SwellWavelength * waveScale;

            // TWO CLOCKS, AND SPLITTING THEM IS THE ModBuild 170 FIX. There is no rate left on this
            // surface to multiply — the ruling of ModBuild 165 was that the pattern must not travel
            // at all — so "faster" can only mean "shorter cycles". Until now ONE dial set every
            // cycle there is, which sounded tidy and was the reason this took six rounds: each time
            // he asked for the busy fine detail to slow down, the SWELL slowed with it, and the
            // swell is the only thing on this surface whose motion the eye can actually follow. Two
            // halvings later it had stopped moving in any perceptible sense — 0.19 degrees per
            // second of normal rotation — and the report was "komplett stillstehend/freezed".
            //
            //   SWELL   -> WaterSettings.SwellSpeed  : the 3D relief, the part that must stay visible
            //   RIPPLE  -> WaterSettings.RippleSpeed : the crossfades, the part he wanted slow
            //
            // The BLOOM stays on the swell's clock deliberately. It is the envelope deciding which
            // part of the pool is lively, it is measured in whole minutes either way, and keeping it
            // there means the shader needs no new property — GhvrSwellBloom already derives it from
            // _SwellPeriod, so this whole split is C#-side and the bundle is untouched.
            //
            // THE PERIOD SCALES AS sqrt(WAVELENGTH), not linearly — deep-water dispersion, the same
            // rule the six components are spaced by. A longer swell is a slower one, which is what
            // stops WaterSettings.WaveScale from turning a lazy ocean roll into a fast one.
            float swellPeriod = WaterOwnSurface.ResolvedSwellPeriod(waveScale, WantedSwellSpeed);
            Vector4 fadePeriods = WaterOwnSurface.ResolvedFadePeriods(
                WaterOwnSurface.ResolvedSwellPeriod(waveScale, dial));
            float tess = Mathf.Clamp(WaterOwnSurface.TessellationFactor, 1f,
                                     WaterOwnSurface.MaxTessellationFactor);

            inst.SetTexture(WaterOwnSurface.NormalMapProperty, bump);
            inst.SetVector(WaterOwnSurface.NormalTilingsProperty, resolved);
            inst.SetVector(WaterOwnSurface.LayerWeightsProperty, weights);
            inst.SetFloat(WaterOwnSurface.NormalStrengthProperty, strength);
            inst.SetFloat(WaterOwnSurface.ProcNormalProperty, bump != null ? 0f : 1f);
            inst.SetFloat(WaterOwnSurface.SmoothnessProperty, Mathf.Clamp01(smoothness));
            inst.SetFloat(WaterOwnSurface.ShimmerProperty, Mathf.Max(WantedShimmer, 0f));
            inst.SetVector(WaterOwnSurface.LightDirProperty, _lightLocal);
            inst.SetFloat(WaterOwnSurface.SwellAmpProperty, amp);
            inst.SetFloat(WaterOwnSurface.SwellWaveProperty, swellWave);
            inst.SetFloat(WaterOwnSurface.SwellPeriodProperty, swellPeriod);
            inst.SetFloat(WaterOwnSurface.SwellCalmProperty, WaterOwnSurface.SwellCalmDepth);
            inst.SetVector(WaterOwnSurface.RippleFadeProperty, fadePeriods);
            inst.SetFloat(WaterOwnSurface.TessFactorProperty, tess);

            _animNote =
                "_Normal_Map " + (bump != null
                    ? "READ ('" + bump.name + "' " + bump.width + "x" + bump.height + "')"
                    : (haveTex
                        ? "DECLARED BUT EMPTY"
                        : "ABSENT FROM THIS SHADER")
                      + " -> the ANALYTIC two-wave ripple is live instead (_ProcNormal=1), which "
                      + "moves but is not the tileset's own pattern")
                + "; _NormalTilings " + (haveTilings ? "READ " : "DEFAULTED ")
                + Fmt(tilings) + " -> tamed " + Fmt(resolved)
                + " = one repeat every " + Wavelengths(resolved)
                + " (aniso tame " + WaterOwnSurface.AnisoTame.ToString("0.##")
                + ", WaterSettings.WaveScale " + waveScale.ToString("0.###") + ")"
                + "; layer weights A " + weights.x.ToString("0.###")
                + " / B " + weights.y.ToString("0.###")
                // THE RESOLVED DRIFT, FIRST, AND IT IS A CONSTANT ZERO BY CONSTRUCTION. The user
                // has now rejected a flow three times, so the number he is judging goes at the head
                // of the line rather than buried in it. It is not a value this code computed and
                // then found to be small: there is no rate property left on the shader, no scroll
                // term left in it and no time in any sampling coordinate, so this is the ONLY
                // number this term can print. If a future log ever shows anything else here,
                // something has re-declared a speed.
                + "; RESOLVED DRIFT 0.000 world units/s (0.00 m a minute) — NOTHING ON THIS SURFACE "
                + "TRANSLATES: _WaterUVAnimSpeedA/B and _SwellSpeed were DELETED rather than zeroed, "
                + "the ripple is sampled at fixed frames of the world plane and CROSSFADED with "
                + "periods " + fadePeriods.x.ToString("0.#") + " / " + fadePeriods.y.ToString("0.#")
                + " / " + fadePeriods.z.ToString("0.#")
                + " s, and the swell's spatial phase contains no clock"
                + "; SWELL amplitude " + amp.ToString("0.####") + " world units (quad width "
                + o.FilmWidthWU.ToString("0.###") + " x WaterSettings.SwellHeight "
                + WantedSwellHeight.ToString("0.###") + ", ceiling "
                + WaterOwnSurface.MaxSwellAmplitude.ToString("0.###") + " against the "
                + WaterOwnSurface.FilmToBedGap.ToString("0.##") + " gap to the basin bed)"
                // THE SWELL IS FOUR STANDING COMPONENTS, so "one crest every N seconds" is a BOB
                // period and not a travel time, and there is no single wavelength either. Both
                // numbers the ruling is about — how slow, and how far it is from repeating on the
                // tile lattice — are printed, because "es fließt noch viel zu schnell" and "bei
                // jedem tile identisch" must be answerable from this line next time.
                + ", " + WaterOwnSurface.SwellRatios.Length + " STANDING components at "
                + SwellWaves(swellWave) + " world units, rising and falling once every "
                + BobPeriods(swellPeriod)
                + " s IN PLACE — they do not travel, and the six rates share no common measure, so "
                + "the sum has no beat a player could learn; under a BLOOM of "
                + (WaterOwnSurface.SwellCalmDepth * 100f).ToString("0")
                + "% breathing on " + (swellPeriod * WaterOwnSurface.SwellBloomRatios[0]).ToString("0")
                + " / " + (swellPeriod * WaterOwnSurface.SwellBloomRatios[1]).ToString("0")
                + " / " + (swellPeriod * WaterOwnSurface.SwellBloomRatios[2]).ToString("0")
                + " s, so a different part of the pool is the lively one every few minutes and it "
                + "gets there by fading rather than by travelling"
                // HOW FAST IT ACTUALLY MOVES, which is not the period. Six rounds were judged on
                // periods alone and the last two of them shipped a surface the user called frozen
                // while every printed number moved exactly as intended. The eye follows the surface
                // NORMAL, and that rate is amplitude x steepness x frequency — a product of which
                // the period is one factor. Printed with the band his own five verdicts set, so a
                // "too fast" or "frozen" report is a number in a range before anyone puts a headset
                // on. See WaterOwnSurface.PeakNormalRate.
                + "; " + MotionBand(amp, swellWave, swellPeriod)
                + "; LATTICE MISMATCH " + WaterOwnSurface.LatticeMismatch(swellWave).ToString("0.###")
                + " cycles — the WORST of the six components against the "
                + WaterOwnSurface.TileLatticeX.ToString("0.##") + " x "
                + WaterOwnSurface.TileLatticeZ.ToString("0.##")
                + " m film lattice, i.e. how far the field is from drawing the same figure on "
                + "every tile (0 would be identical tiles; ModBuild 164's single 1.1 m train "
                + "scored " + WaterOwnSurface.LatticeMismatch(1.1f).ToString("0.###") + ")"
                + "; TESSELLATION factor " + tess.ToString("0.#")
                + " per authored edge (fixed, NEVER distance-scaled — a factor read off the camera "
                + "would subdivide differently in each MultiPass eye), so each authored triangle "
                + "becomes " + (tess * tess).ToString("0") + " and the film's ~"
                + WaterOwnSurface.TargetEdgeWU.ToString("0.##") + " m target edge is met; "
                + SubShaderInForce(inst)
                + "; _DetailOpacityBaseNormalStr " + (haveStr ? "READ " : "DEFAULTED ")
                + Fmt(strVec) + " -> ripple strength " + strength.ToString("0.###")
                + "; _Smoothness " + (haveSmooth ? "READ " : "DEFAULTED ")
                + smoothness.ToString("0.###") + " -> glint exponent "
                + Mathf.Lerp(1f, 8f, Mathf.Clamp01(smoothness)).ToString("0.#")
                + "; WaterSettings.Shimmer " + WantedShimmer.ToString("0.###");
        }

        /// <summary>
        /// The one measurement that would have caught the freeze before it shipped, with the band
        /// the user's own verdicts set around it and a plain verdict word for this build.
        ///
        /// <para>PERIODS ARE NOT PERCEPTION. Everything above this on the line is a period, and two
        /// builds running periods exactly as designed were reported as having no animation at all.
        /// The eye follows the surface NORMAL; how fast that turns is amplitude x steepness x
        /// frequency, and only the last of those three ever moved. This prints the product.</para>
        /// </summary>
        private static string MotionBand(float amp, float swellWave, float swellPeriod)
        {
            float rate = WaterOwnSurface.PeakNormalRate(amp, swellWave, swellPeriod) * Mathf.Rad2Deg;
            float vert = WaterOwnSurface.PeakVerticalSpeed(amp, swellPeriod) * 1000f;
            float slope = WaterOwnSurface.PeakCrestSlope(amp, swellWave) * Mathf.Rad2Deg;

            // THE FREQUENCY IS THE VERDICT, and the rate is only how strong it is once it is fast
            // enough to be seen. ModBuild 172 shipped a higher rate than a build the user could
            // see and was still reported as standing still; what sorted the four builds was this
            // number alone.
            float fastest = WaterOwnSurface.FastestSwellPeriod(swellPeriod);
            string verdict =
                fastest > WaterOwnSurface.VisibleFastestPeriodSeconds
                    ? "OVER THE VISIBILITY FLOOR — the fastest component takes "
                      + fastest.ToString("0.#") + " s (" + (1f / fastest).ToString("0.0000")
                      + " Hz) where the floor is "
                      + WaterOwnSurface.VisibleFastestPeriodSeconds.ToString("0.#")
                      + " s. THIS BUILD WILL BE REPORTED AS FROZEN however strong its rate is; "
                      + "amplitude cannot buy past a temporal floor and ModBuild 172 proved it"
                    : "fastest component " + fastest.ToString("0.#") + " s ("
                      + (1f / fastest).ToString("0.0000") + " Hz), inside the visibility floor of "
                      + WaterOwnSurface.VisibleFastestPeriodSeconds.ToString("0.#") + " s"
                      + (rate >= WaterOwnSurface.BriskNormalRateDeg
                             ? "; rate is at or over the one he called good-but-slightly-fast"
                             : "; rate is "
                               + (100f * rate / WaterOwnSurface.BriskNormalRateDeg).ToString("0")
                               + "% of the brisk one");

            var sb = new System.Text.StringBuilder(420);
            sb.Append("HOW FAST IT ACTUALLY MOVES (periods are not perception — the eye follows the "
                      + "surface NORMAL, and that rate is amplitude x steepness x frequency, of "
                      + "which the period is one factor): peak normal rate ")
              .Append(rate.ToString("0.000")).Append(" deg/s, peak vertical ")
              .Append(vert.ToString("0.00")).Append(" mm/s, crest slope ")
              .Append(slope.ToString("0.0")).Append(" deg — ").Append(verdict)
              .Append(". CALIBRATION, from the user's own verdicts rather than from taste [");
            for (int i = 0; i < WaterOwnSurface.MotionVerdicts.Length; i++)
            {
                (int build, float h, float d, float r, bool seen, string words) =
                    WaterOwnSurface.MotionVerdicts[i];
                if (i > 0)
                    sb.Append("; ");
                sb.Append("MB").Append(build).Append(' ')
                  .Append(WaterOwnSurface.FastestSwellPeriod(
                              WaterOwnSurface.ResolvedSwellPeriod(1f, d)).ToString("0.#"))
                  .Append(" s at ").Append(r.ToString("0.00")).Append(" deg/s = ")
                  .Append(seen ? "SEEN" : "FROZEN").Append(" '").Append(words).Append('\'');
            }
            sb.Append("]. THE TWO CLOCKS ARE SEPARATE as of ModBuild 170: WaterSettings.SwellSpeed ")
              .Append(WantedSwellSpeed.ToString("0.#####"))
              .Append(" drives the relief above, WaterSettings.RippleSpeed ")
              .Append(WantedRippleSpeed.ToString("0.#####"))
              .Append(" drives only the crossfades. One number drove both for six rounds, so every "
                      + "'slower' aimed at the busy detail also slowed the one thing that could be "
                      + "seen — which is how a surface ends up frozen with every printed number "
                      + "moving exactly as intended");
            return sb.ToString();
        }

        /// <summary>
        /// WHICH SubShader is actually drawing this film — MEASURED, not inferred.
        ///
        /// <para>This line used to read the device's shader level and conclude "so the TESSELLATED
        /// SubShader is the one being drawn". That is an inference about the HARDWARE, and it has
        /// been reporting success on every frozen build. Whether the LOD 300 SubShader is selected
        /// depends on the hardware AND on the LOD ceilings, and a ceiling below 300 silently picks
        /// the LOD 100 fallback instead — which runs the identical wave on the game's own 33-vertex
        /// hex, i.e. a 2.4 m swell with almost no vertices to carry it. That surface is flat and
        /// therefore genuinely motionless, and no amount of amplitude or frequency tuning can reach
        /// it. Three rounds have now been spent tuning a number while this was only assumed.</para>
        ///
        /// <para>Both ceilings are read: <c>Shader.globalMaximumLOD</c>, which anything in the
        /// process may lower (a game quality setting is the obvious candidate), and the shader
        /// instance's own <c>maximumLOD</c>. The lower of the two decides.</para>
        /// </summary>
        private static string SubShaderInForce(Material inst)
        {
            int global = Shader.globalMaximumLOD;
            int local = inst != null && inst.shader != null ? inst.shader.maximumLOD : -1;
            int effective = local < 0 ? global : Mathf.Min(global, local);
            bool hardwareOk = SystemInfo.graphicsShaderLevel >= 46;
            bool tessellated = hardwareOk && effective >= 300;

            string s = "SUBSHADER IN FORCE (measured, not inferred): shader level "
                       + SystemInfo.graphicsShaderLevel + " (needs 46), Shader.globalMaximumLOD "
                       + global + ", this shader's maximumLOD "
                       + (local < 0 ? "unreadable" : local.ToString())
                       + " -> effective LOD ceiling " + effective + ", passes " 
                       + (inst != null ? inst.passCount : -1) + " -> ";
            if (tessellated)
                return s + "the LOD 300 TESSELLATED SubShader, which is the one that carries the "
                       + "relief";
            return s + "THE LOD 100 FALLBACK. The film is drawing WITHOUT tessellation, so the "
                   + "swell displaces only the game's own 33-vertex hex and the surface is flat and "
                   + "motionless whatever the periods and amplitudes on this line say. "
                   + (hardwareOk
                          ? "The hardware is capable — the LOD CEILING is what excludes it, and "
                            + "that is set by something else in the process, not by this driver."
                          : "The hardware cannot run it.")
                   + " A report of 'the water does not move' is THIS FIELD and nothing else on the "
                   + "line.";
        }

        private static string Fmt(Vector4 v) =>
            "(" + v.x.ToString("0.###") + "," + v.y.ToString("0.###") + ","
            + v.z.ToString("0.###") + "," + v.w.ToString("0.###") + ")";

        private static string Fmt3(Vector3 v) =>
            "(" + v.x.ToString("0.###") + "," + v.y.ToString("0.###") + ","
            + v.z.ToString("0.###") + ")";

        /// <summary>The resolved tilings said in the unit a reader can hold a ruler against: world
        /// units per repeat, per axis, per layer. "Too big" and "too small" are then numbers in the
        /// next log instead of an argument about a photograph.</summary>
        private static string Wavelengths(Vector4 tilings) =>
            "A " + Wave(tilings.x) + " x " + Wave(tilings.y)
            + " m, B " + Wave(tilings.z) + " x " + Wave(tilings.w) + " m";

        private static string Wave(float tiling)
        {
            float a = Mathf.Abs(tiling);
            return a > 1e-4f ? (1f / a).ToString("0.##") : "inf";
        }

        /// <summary>The six swell components' wavelengths, from the longest. Printed in full
        /// because "how big are the waves" is a question the log has had to answer at every round,
        /// and one number for six components is a claim rather than a measurement.</summary>
        private static string SwellWaves(float longest) =>
            Series(longest, WaterOwnSurface.SwellRatios, "0.##");

        /// <summary>The six bob periods, from the longest — the ratios are the square roots of the
        /// wavelength ratios (deep-water dispersion). This is the line "viel zu hektisch" is about,
        /// so the whole set is printed rather than the first of them.</summary>
        private static string BobPeriods(float longest)
        {
            var sqrt = new float[WaterOwnSurface.SwellRatios.Length];
            for (int i = 0; i < sqrt.Length; i++)
                sqrt[i] = Mathf.Sqrt(WaterOwnSurface.SwellRatios[i]);
            return Series(longest, sqrt, "0.#");
        }

        private static string Series(float scale, float[] ratios, string format)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ratios.Length; i++)
            {
                if (i > 0)
                    sb.Append(" / ");
                sb.Append((scale * ratios[i]).ToString(format));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The mod's own film shader, resolved through <see cref="BundleShaders"/>.
        ///
        /// <para>Never a bare <see cref="Shader.Find"/>: a bundled shader referenced only from
        /// runtime C# is never loaded, <c>Shader.Find</c> returns null with the bundle open and
        /// nothing is thrown — the failure that has silently cost this project two shipped builds,
        /// and one that would be indistinguishable here from a fifth round that changed nothing.
        /// <c>BundleShaders.Resolve</c> also loads the shader ASSET out of every open bundle by
        /// path and sweeps the loaded shader objects, and prints an inventory when all three
        /// miss.</para>
        ///
        /// <para>Called from the SLOW tick only. A miss is deliberately not cached by
        /// <see cref="BundleShaders"/> (a bundle can load later than the first lookup), so calling
        /// it per renderer would put a <c>Shader.Find</c> and a walk of every loaded AssetBundle
        /// into the frame once per water quad; four attempts a second picks the bundle up the
        /// moment it arrives.</para>
        /// </summary>
        private Shader? MaintainOwnShader()
        {
            // ONLY THE ANIMATED WATER ENDS THE SEARCH. Sitting on the fallback is not a resolved
            // state: Overlay can be reached by a mechanism the water shader cannot (another feature
            // holding it alive), so "we have a shader" and "we have the right shader" are different
            // facts and only the second may stop the retry.
            if (_ownShader != null && _ownIsWater)
                return _ownShader;
            if (!WantOwnSurface)
                return null;

            // THE ANIMATED WATER FIRST. Nothing in the mod's own bundle prefabs references it — it
            // goes on the GAME's quads — so Shader.Find can never resolve it on its own and
            // BundleShaders' bundle-asset step is the mechanism that will.
            Shader? water = BundleShaders.Resolve(
                WaterOwnSurface.FilmShaderName, Name,
                "WaterSettings.OwnSurface can now replace the game's water film with the mod's own "
                + "ANIMATED water: the tileset's own normal map scrolled as two layers at its own "
                + "two tilings and two speeds, which the hardware census measured as the whole of "
                + "the game water's motion, lit by a FIXED light direction. NO VIEW-DEPENDENT TERM "
                + "OF ANY KIND — no cube sample, no reflect(), no Fresnel, not even a half-vector "
                + "specular, whose highlight would slide with the head exactly as the report "
                + "describes. That is the requirement rather than a preference: the user reports "
                + "the head-bound reflection cannot be switched off by any property dial, and it is "
                + "also what makes the surface per-eye identical under MultiPass by construction.",
                "the ANIMATED water shader could not be reached; falling back to '"
                + WaterOwnSurface.FallbackFilmShaderName + "', which is ModBuild 162's FLAT STILL "
                + "SHEET. The defect stays fixed — that shader has no view-dependent term either — "
                + "but the look the user asked to have back ('Das echte Wasser hatte ANimation und "
                + "co. das will ich auch wieder') is NOT in this build, and a report of 'still no "
                + "animation' would be this line and not the shader.");
            if (water != null)
            {
                _ownShader = water;
                _ownIsWater = true;
                _ownShaderName = WaterOwnSurface.FilmShaderName;
            }
            else if (_ownShader == null)
            {
                _ownShader = BundleShaders.Resolve(
                    WaterOwnSurface.FallbackFilmShaderName, Name,
                    "WaterSettings.OwnSurface runs on the FALLBACK film — a flat, still, translucent "
                    + "sheet in the tileset's own colour. It removes the head-bound reflection and "
                    + "the pale sheet, and it does not animate.",
                    "WaterSettings.OwnSurface CANNOT RUN AT ALL: neither the mod's water shader nor its "
                    + "overlay fallback could be reached, every water film keeps the game's own "
                    + "VFX/Water_Shd_Trans material, and this build therefore carries no new "
                    + "mechanism. Do NOT read an unchanged pool as a finding until this line is "
                    + "gone — the OWN SURFACE block counts how many renderers actually carry our "
                    + "shader, and that count is the only thing that makes a photograph mean "
                    + "anything.");
                _ownIsWater = false;
                if (_ownShader != null)
                    _ownShaderName = WaterOwnSurface.FallbackFilmShaderName;
            }
            else
            {
                // Already on the fallback and the water shader is still out of reach. Keep the
                // fallback rather than churning the tracked set, and try again next tick.
                return _ownShader;
            }

            // THE BUNDLE CAN ARRIVE LATE. A miss is not cached, so this retries four times a
            // second — and a film adopted while the lookup was still missing is sitting on the
            // game's material with no event that would ever bring it back here. Release the whole
            // tracked set the moment the shader turns up; RestoreAll clears the tile-count memo, so
            // the Discover() immediately after this call re-adopts every one of them onto our
            // shader in the SAME tick.
            if (_ownShader != null && _tracked.Count > 0)
            {
                RestoreAll();
                _nextCensus = 0f;
            }
            return _ownShader;
        }

        /// <summary>
        /// Where the water's glints are lit from — the ONLY direction anywhere in
        /// <c>GloomhavenVR/WaterVR</c>, and a uniform rather than anything derived from the camera.
        ///
        /// <para>PREFERENCE ORDER, and it is the order the task's own instruction gives: the
        /// scene's own main directional light if one exists, else the fixed constant, and the
        /// census says WHICH — because a pool lit from the wrong place and a pool whose light
        /// resolution silently failed look identical from inside the headset.</para>
        ///
        /// <para>COST. <see cref="RenderSettings.sun"/> is a free property read and is tried every
        /// slow tick. The SCAN behind it is not free and is rate-limited to
        /// <see cref="LightScanInterval"/>; it is a walk of the scene's <see cref="Light"/>
        /// components, not of its renderers, which is what makes it affordable at all (the PERF S1
        /// lesson was about 3000-renderer sweeps; a scenario has a few dozen lights).</para>
        ///
        /// <para>The brightest DIRECTIONAL light wins rather than the nearest of any type. A point
        /// light has a position, and a position would make the glint direction depend on where on
        /// the pool a fragment is — legitimate physics, and a per-fragment vector this shader
        /// deliberately does not have, because every direction in it is a uniform.</para>
        /// </summary>
        private void MaintainLightDirection()
        {
            Light? sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled)
                sun = _sun != null && _sun.isActiveAndEnabled ? _sun : null;
            if (sun == null && Time.unscaledTime >= _nextLightScan)
            {
                _nextLightScan = Time.unscaledTime + LightScanInterval;
                sun = BrightestDirectional();
            }
            _sun = sun;

            bool have = sun != null;
            Vector3 toward = have ? -sun!.transform.forward : Vector3.zero;
            Vector4 before = _lightLocal;
            WaterOwnSurface.TryBuildLightDirection(toward, have, out _lightLocal, out _lightNote);
            if ((before - _lightLocal).sqrMagnitude > 1e-8f)
                _lightDirty = true;
        }

        private static Light? BrightestDirectional()
        {
            Light[] all = FindObjectsOfType<Light>();
            Light? best = null;
            float bestI = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                Light l = all[i];
                if (l == null || l.type != LightType.Directional || !l.isActiveAndEnabled)
                    continue;
                if (best == null || l.intensity > bestI)
                {
                    best = l;
                    bestI = l.intensity;
                }
            }
            return best;
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
                              + "WaterSettings.LocalProbe can reach it");
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
        /// The water FILM's own treatments while it still draws on the GAME's shader — i.e. while
        /// <c>WaterSettings.OwnSurface</c> is off, or while the bundle has not yielded the mod's own
        /// shader: the tint alpha, and the EDGE / FOAM / BORDER BAND, which is where the visible
        /// pixels of <c>spiegeltiles.jpg</c> actually came from.
        ///
        /// <para>THE READING THAT SHAPED IT. The photograph shows a near-WHITE sheet;
        /// <c>_Color_Tint</c> is dark green RGBA(0.195, 0.311, 0.131). No alpha on a dark green
        /// body makes a pale sheet, so the body was never what was on screen. What IS pale on this
        /// material is <c>_Edge_Colour</c> (0.887, 0.887, 0.887, 0.867) and <c>_WaterBorderCol</c>
        /// (0.670, 0.617, 0.528, 0.561), and with the head camera writing no
        /// <c>_CameraDepthTexture</c> the depth term that would normally confine them to a
        /// shoreline is a constant across the whole quad.</para>
        ///
        /// <para>SO THE BAND IS COLLAPSED BY NUMBER, NOT BY KEYWORD. A keyword clear cannot be
        /// verified from a log — only the call can be reported — and, decisively,
        /// <c>_WaterBorderWidth</c>/<c>_WaterBorderCol</c> is a SECOND border mechanism with no
        /// keyword at all, which a clear could never have reached. Every band WIDTH goes to zero
        /// and every band COLOUR to the body hue at alpha zero (<see cref="WaterEdgeBand"/>, which
        /// also holds the invariant that neither write can ever raise what the tileset authored).
        /// A band of width zero covers no pixels at EITHER extreme of a pinned depth fade, so this
        /// does not depend on a sign nobody here can read.</para>
        /// </summary>
        /// <param name="report">When non-null, every band property is appended with its authored
        /// value and what was written — or a loud line naming it as ABSENT from this shader.</param>
        private void ApplyWaterFilm(
            Material shared, Material inst, System.Text.StringBuilder? report)
        {
            Color tint = shared.HasProperty(ColorTintId) ? shared.GetColor(ColorTintId) : Color.white;
            // Hue as authored, alpha capped so the tiles read through.
            tint.a = Mathf.Min(tint.a, WantedOpacity);
            if (shared.HasProperty(ColorTintId))
                inst.SetColor(ColorTintId, tint);

            CollapseBand(shared, inst, tint, report);
        }

        /// <summary>
        /// Collapse every shoreline, foam and border term this shader declares. See
        /// <see cref="ApplyWaterFilm"/> for why by number rather than by keyword, and
        /// <see cref="WaterEdgeBand"/> for the arithmetic and the never-raise invariant.
        /// </summary>
        /// <param name="body">The film's own tint, already capped — the hue every band colour is
        /// repainted in, so a band that still draws draws water rather than shoreline.</param>
        private void CollapseBand(
            Material shared, Material inst, Color body, System.Text.StringBuilder? report)
        {
            for (int i = 0; i < BandWidthIds.Length; i++)
            {
                string name = WaterEdgeBand.BandWidthProperties[i];
                if (!shared.HasProperty(BandWidthIds[i]))
                {
                    report?.Append(name).Append(" ABSENT FROM THIS SHADER — nothing was written "
                                                + "and no band of that name exists to collapse; ");
                    continue;
                }
                float authored = shared.GetFloat(BandWidthIds[i]);
                if (WaterEdgeBand.TryCollapseWidth(authored, out float v, out string why))
                    inst.SetFloat(BandWidthIds[i], v);
                report?.Append(name).Append(' ').Append(why).Append("; ");
            }

            for (int i = 0; i < BandColourIds.Length; i++)
            {
                string name = WaterEdgeBand.BandColourProperties[i];
                if (!shared.HasProperty(BandColourIds[i]))
                {
                    report?.Append(name).Append(" ABSENT FROM THIS SHADER — nothing was written; ");
                    continue;
                }
                Color authored = shared.GetColor(BandColourIds[i]);
                if (WaterEdgeBand.TryCollapseColour(authored, body, out Color v, out string why))
                    inst.SetColor(BandColourIds[i], v);
                report?.Append(name).Append(' ').Append(why).Append("; ");
            }

            // The keyword half is KEPT, but it is no longer the load-bearing write: the material
            // ships _EDGECOLOUR_TOGGLE_ON live, an Amplify [Toggle] with no explicit keyword name
            // compiles to exactly that shader_feature, and clearing it costs nothing. What round
            // three proved is only that it is not SUFFICIENT — which is why the numbers above
            // exist and why the census reads the instance back rather than reporting these calls.
            if (shared.HasProperty(EdgeToggleId))
            {
                float authoredToggle = shared.GetFloat(EdgeToggleId);
                inst.SetFloat(EdgeToggleId, Mathf.Min(authoredToggle, 0f));
                report?.Append(WaterEdgeBand.EdgeToggleProperty).Append(' ')
                       .Append(authoredToggle.ToString("0.###")).Append(" -> ")
                       .Append(Mathf.Min(authoredToggle, 0f).ToString("0.###")).Append("; ");
            }
            else
            {
                report?.Append(WaterEdgeBand.EdgeToggleProperty)
                       .Append(" ABSENT FROM THIS SHADER; ");
            }
            inst.DisableKeyword(EdgeColourKeyword);
            report?.Append("keyword ").Append(EdgeColourKeyword)
                   .Append(" DisableKeyword called (verify it in the read-back below, not here)");
        }

        // ---- THE FILM'S BOUNDS ------------------------------------------------------------------

        /// <summary>
        /// Measure the film's width and pay the culling bill the swell comes with.
        ///
        /// <para><b>CULLING CANNOT SEE A VERTEX OR DOMAIN PROGRAM.</b> Unity culls a renderer
        /// against its bounds, so geometry the shader pushes outside them is culled anyway: a
        /// displaced surface vanishes as you walk up to it and, under MultiPass, vanishes in ONE
        /// EYE FIRST because the two eye frustums differ. This project has already lost a build to
        /// exactly that. The census reads this film's local bounds as size (1.73, <b>0</b>, 1.998)
        /// — zero height — so every crest the swell raises is outside them by construction.</para>
        ///
        /// <para><b>WHY THIS IS A BOUNDS WRITE AND NOT A MESH SWAP ANY MORE.</b> ModBuild 164
        /// handed the film a subdivided, padded COPY of its mesh, and the copy was never built: the
        /// game's meshes are imported without Read/Write (the census reads
        /// <c>'TERRAIN_Water_Plane' 33 verts / <b>0 tris</b></c>, which is what
        /// <c>Mesh.triangles</c> returns on a non-readable mesh), so there was no index buffer to
        /// subdivide and the whole round shipped with no added geometry at all. The subdivision is
        /// now the GPU's job and needs no CPU access to anything; the pad is all that is left of
        /// that path, and <see cref="Renderer.localBounds"/> takes it directly. It works on a
        /// non-readable mesh, it clones nothing, and it is undone exactly by
        /// <see cref="Renderer.ResetLocalBounds"/>.</para>
        ///
        /// <para>THE PAD IS THE CEILING, NOT THE CURRENT DIAL — it comes from
        /// <see cref="WaterOwnSurface.MaxSwellAmplitude"/> and never from the amplitude in force,
        /// so a player who raises <c>WaterSettings.SwellHeight</c> mid-scene cannot outrun bounds baked
        /// for a lower one. The displacement is a translation along world Y bounded by that
        /// ceiling, so the swept volume is exactly the rest bounds Minkowski-summed with a segment
        /// of twice it; padding every LOCAL axis contains that segment whatever the quad's
        /// orientation. It is the true swept volume and not a guess-pad.</para>
        ///
        /// <para>THE WIDTH IS MEASURED BEFORE THE PAD IS WRITTEN, because the amplitude is a
        /// fraction of the width and reading it back off our own padded bounds would grow the wave
        /// a little on every re-measure.</para>
        /// </summary>
        private void PadFilmBounds(Renderer r, Owned o)
        {
            try
            {
                if (o.BoundsPadded)
                    return; // already ours, and the pad is a constant

                Bounds wb = r.bounds;
                o.FilmWidthWU = Mathf.Max(wb.size.x, wb.size.z);

                // THE PAD IS A WORLD LENGTH AND localBounds IS LOCAL, so it is divided by the
                // smallest scale component. This project's armatures really do use a scale of 100,
                // and a hundredfold pad would be a renderer that is never culled at all.
                Vector3 s = r.transform.lossyScale;
                float minScale = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                float scale = float.IsNaN(minScale) || minScale <= 1e-4f ? 1f : minScale;
                float pad = WaterOwnSurface.MaxSwellAmplitude / scale;

                Bounds lb = r.localBounds;
                Bounds padded = lb;
                padded.Expand(pad * 2f);   // Expand() takes the TOTAL growth, i.e. half per side
                r.localBounds = padded;
                o.BoundsPadded = true;

                _swellNote =
                    "'" + r.name + "' local bounds " + Fmt3(lb.size) + " -> " + Fmt3(padded.size)
                    + " (padded " + pad.ToString("0.###")
                    + " local units on every axis, from the "
                    + WaterOwnSurface.MaxSwellAmplitude.ToString("0.###")
                    + " world-unit amplitude CEILING at a lossy scale of "
                    + scale.ToString("0.###") + ") — the crests are now inside the volume Unity "
                    + "culls against, so the film cannot vanish as you lean in, one eye first";
            }
            catch (Exception e)
            {
                _swellNote =
                    "padding '" + r.name + "'s bounds threw " + e.GetType().Name
                    + " — THE FILM STILL DISPLACES BUT IS CULLED AGAINST ITS UNDISPLACED BOUNDS, "
                    + "so it can disappear as you approach it and can disappear in ONE EYE FIRST "
                    + "under MultiPass. That is a defect in this driver, not a look.";
            }
        }

        /// <summary>Give the film its authored bounds back. Idempotent, and safe on a renderer we
        /// never padded — every path that stops wanting the swell calls it, so the one thing it
        /// must never do is throw.</summary>
        private static void RestoreFilmBounds(Renderer? r, Owned o)
        {
            if (!o.BoundsPadded)
                return;
            try
            {
                if (r != null)
                    r.ResetLocalBounds();
            }
            catch { /* the renderer went away between the null check and here */ }
            o.BoundsPadded = false;
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
            // The bounds go back whether or not the materials do: `restore: false` means the
            // renderer is gone or the game already replaced our materials, and in the second case
            // the renderer is still live and still carrying a bounds override of ours.
            RestoreFilmBounds(r, o);
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
        /// <c>WaterSettings.BasinSurfaces</c>.</summary>
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
            // The read-back sample is an index into a set that no longer exists. Dropping it here
            // rather than letting FilmSample() notice keeps "no film tracked" meaning exactly that.
            _censusSample = null;
            DestroyProbes();
            // No mesh cache to clear any more: since ModBuild 165 the swell's geometry is produced
            // by the shader's tessellator and the only thing this driver ever owned on the
            // renderer's geometry side is a bounds override, which ReleaseAt above has already
            // reset on every film.
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
                    _probeNote = "WaterSettings.LocalProbe is OFF — every surface here samples the "
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
                    + e.GetType().Name + ") — WaterSettings.LocalProbe is inert this session and the "
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

            float smoothnessInForce = WantedSmoothness;
            sb.Append(" | RETUNE: sharpness ceiling ")
              .Append(smoothnessInForce.ToString("0.###"))
              .Append(" (dial ").Append(_appliedSmoothness.ToString("0.###"))
              .Append("; the film authors _Smoothness 0.754; a roughness property is floored at ")
              .Append((1f - smoothnessInForce).ToString("0.###"))
              .Append(" instead), metal/reflection ceiling ")
              .Append(WantedReflectivity.ToString("0.###"))
              .Append(" (dial ").Append(_appliedReflectivity.ToString("0.###")).Append(')')
              .Append(", _Color_Tint.a capped at ").Append(_appliedOpacity.ToString("0.###"))
              .Append(" (authored 0.737), basin surfaces ")
              .Append(_appliedBasin ? "IN SCOPE" : "released (WaterSettings.BasinSurfaces OFF)")
              .Append(", edge/foam/border band COLLAPSED BY NUMBER wherever the film still draws "
                      + "on the game's shader — every width to 0 and every band colour to the body "
                      + "hue at alpha 0, _WaterBorderWidth/_WaterBorderCol included (a SECOND "
                      + "border mechanism with no keyword, which a DisableKeyword could never have "
                      + "reached)")
              .Append(", probe usage forced Off->BlendProbes on ").Append(_probeUsageForced)
              .Append(" renderer(s).");

            AppendOwnSurface(sb);
            AppendReadBack(sb);

            // Mechanism four, and whether it reached anything.
            sb.Append(" | LOCAL PROBE: WaterSettings.LocalProbe=").Append(WantProbe)
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
                                  + "movement. If WaterSettings.LocalProbe is ON and this still says "
                                  + "zero, the probe is not being built where the water is and "
                                  + "mechanism four is not in play at all.");
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append(" | REFLECTION unreadable: ").Append(e.GetType().Name);
            }

            sb.Append(" WHAT IS SETTLED, so the next round is decidable from this line alone. "
                      + "(1) THE WRITE LANDS. There is no property block left; every value goes on "
                      + "a per-renderer material instance and the BAND READ-BACK reads it back off "
                      + "that instance. (2) THE RENDERERS ARE OURS — measured on hardware with the "
                      + "diagnostic paint that has since been retired: 'Die debug farbe "
                      + "funktioniert - alles färbt sich magenta wie gewollt.' (3) NO PROPERTY OF "
                      + "THE GAME'S SHADER REACHES THE DEFECT: every band width read back at 0, "
                      + "every band colour at alpha 0, the keyword list empty, _Smoothness 0.08, "
                      + "the metal/reflection ceiling 0, a flat local probe REACHING the surface — "
                      + "and 'Keine Änderungen bei der Wasser Problematik'. The pale sheet and the "
                      + "head-bound reflection were a TEXTURE or a CONSTANT compiled into "
                      + "VFX/Water_Shd_Trans, which is why WaterSettings.OwnSurface deletes that shader "
                      + "from the film rather than addressing it. AND NONE OF THIS IS A DIAL ANY "
                      + "MORE: the section was removed at ModBuild 169 and every value above is a "
                      + "constant in WaterSettings, so a report about this surface is a report "
                      + "about the build and not about a setting. The two that still separate two "
                      + "different complaints are WaterSettings.BasinSurfaces and "
                      + "WaterSettings.LocalProbe: they carry the SWIMMING half of the report on "
                      + "the BASIN, which is opaque ground and is not replaced by OwnSurface — "
                      + "changing them means editing WaterSettings and shipping, and they must "
                      + "not be moved together with the film's paleness.");

            VRLog.Info(Name, sb.ToString());
        }

        /// <summary>
        /// THE OWN SURFACE BLOCK — which shader was resolved, by which of
        /// <see cref="BundleShaders"/>' three mechanisms, how many renderers ACTUALLY carry it,
        /// what the look was derived from, and what a still-pale report proves after this build.
        ///
        /// <para>Everything here is counted off the live material instances, never off an
        /// intention. That distinction is the whole reason this block is worth its length: ModBuild
        /// 160's census reported <c>UNDONE: 0 re-asserts</c> — which says only that our write was
        /// still attached — and that read as confirmation for a whole round while the surface was
        /// unchanged.</para>
        /// </summary>
        private void AppendOwnSurface(System.Text.StringBuilder sb)
        {
            sb.Append(" | OWN SURFACE: WaterSettings.OwnSurface=").Append(WantOwnSurface);
            if (!WantOwnSurface)
            {
                sb.Append(" — the film runs the game's own VFX/Water_Shd_Trans and the band and "
                          + "reflection writes above are what is carrying the fix. That is the "
                          + "configuration ModBuild 161 shipped, and its verdict was 'Keine "
                          + "Änderungen bei der Wasser Problematik'.");
                return;
            }

            if (_ownShader == null)
            {
                sb.Append(" shader '").Append(WaterOwnSurface.FilmShaderName)
                  .Append("' NOT RESOLVED, and neither was the '")
                  .Append(WaterOwnSurface.FallbackFilmShaderName)
                  .Append("' fallback — the mod's bundle did not yield either, EVERY FILM IS STILL "
                          + "ON THE GAME'S MATERIAL, and this build carries no new mechanism at "
                          + "all. An unchanged pool proves NOTHING while this line reads NOT "
                          + "RESOLVED; the BUNDLED SHADER error line earlier in this log carries "
                          + "the inventory that says whether the bundle loaded.");
                return;
            }
            sb.Append(" shader '").Append(_ownShaderName).Append("' resolved via ")
              .Append(BundleShaders.How(_ownShaderName));
            sb.Append(_ownIsWater
                ? " — this is the ANIMATED water, which is what the user asked to have back ('Das "
                  + "echte Wasser hatte ANimation und co. das will ich auch wieder')."
                : " — THIS IS THE FALLBACK, NOT THE WATER. '" + WaterOwnSurface.FilmShaderName
                  + "' could not be reached, so the film is ModBuild 162's FLAT STILL SHEET: the "
                  + "head-bound reflection and the pale sheet are still gone, and the animation the "
                  + "user asked for is NOT in what is on screen. A report of 'it still does not "
                  + "move' is THIS LINE and not the water shader — read the BUNDLED SHADER error "
                  + "above it for the inventory.");

            int filmTracked = 0, filmOwned = 0;
            for (int i = 0; i < _tracked.Count; i++)
            {
                Renderer r = _tracked[i];
                if (r == null || !_owned.TryGetValue(r, out Owned o) || !o.IsFilm)
                    continue;
                filmTracked++;
                Material? inst = o.Instances.Length > 0 ? o.Instances[0] : null;
                if (inst != null && ReferenceEquals(inst.shader, _ownShader))
                    filmOwned++;
            }

            sb.Append(". SWAPPED: ").Append(filmOwned).Append(" of ").Append(filmTracked)
              .Append(" film renderer(s) actually DRAW with the mod's material now (read off the "
                      + "instance's live shader, not counted from the calls). The basin is "
                      + "deliberately NOT swapped — it is opaque ground, not a film, and keeps the "
                      + "property retune. DERIVED FROM THE TILESET'S OWN VALUES: ")
              .Append(_ownNote)
              .Append("; render queue kept at the authored value so the film draws exactly where "
                      + "the water drew; no _MainTex bound (it defaults to \"white\", and a texture "
                      + "compiled into the game's shader is the prime SUSPECT for the pale sheet, "
                      + "so binding one would keep the question open); blend "
                      + "SrcAlpha/OneMinusSrcAlpha, ZWrite off, two-sided.");

            if (_ownIsWater)
            {
                sb.Append(" THE ANIMATION, value by value, so it can be checked against the "
                          + "tileset's own numbers on this line: ").Append(_animNote)
                  .Append(". LIT FROM: ").Append(_lightNote)
                  // THIS SENTENCE USED TO SAY "No vertex is displaced by any of this". That was
                  // true of ModBuild 163 and it was still in the log through 164, which DID
                  // displace — so the one line a reader would have checked the geometry against
                  // asserted the opposite of what the shader was doing. It now states the
                  // mechanism and its bill, both of which the SWELL and BOUNDS PAD fields above
                  // report as measurements.
                  .Append(". THE GEOMETRY REALLY MOVES: the film is tessellated in the shader's "
                          + "own hull/domain stages and displaced VERTICALLY by the swell above, "
                          + "as a function of world position and time only, so both MultiPass eyes "
                          + "displace identically. Unity culls a renderer against its BOUNDS and "
                          + "cannot see a domain program, so the driver pads Renderer.localBounds "
                          + "by the amplitude CEILING — the BOUNDS PAD field of the per-material "
                          + "line above says what that came to on the film it measured. The "
                          + "tileset's own _addSphericalWaves is still 0 and still not read: this "
                          + "swell is the VR-side relief the user asked for by name.");
            }

            sb.Append(" WHAT THIS SHADER CANNOT DO, which is the point: neither '")
              .Append(WaterOwnSurface.FilmShaderName).Append("' nor '")
              .Append(WaterOwnSurface.FallbackFilmShaderName)
              .Append("' contains unity_SpecCube, reflect(), a cube sample, worldRefl, a Fresnel "
                      + "or a half-vector specular — swept in their own source by the build gate, "
                      + "not asserted here — so neither has a view-dependent term and neither can "
                      + "produce a head-bound reflection, which the user reports no property dial "
                      + "could switch off. The glints come from the moving NORMALS against a fixed "
                      + "light direction, which is also why they are identical in both MultiPass "
                      + "eyes by construction. Neither has a shoreline, a foam term or a depth "
                      + "read, so no band can be pinned on by a missing _CameraDepthTexture.");

            sb.Append(" WHAT A STILL-PALE REPORT PROVES AFTER THIS BUILD, and read it in this "
                      + "order. The user ran the diagnostic paint on hardware — 'Die debug "
                      + "farbe funktioniert - alles färbt sich magenta wie gewollt' — so 'these "
                      + "are not our renderers' is SETTLED AND DEAD: the pale hexes are this "
                      + "driver's tracked water film. With the renderer confirmed ours, every band "
                      + "width read back at 0, every band colour at alpha 0, the keyword list "
                      + "empty, _Smoothness at 0.08, the metal/reflection ceiling at 0 and a flat "
                      + "local probe reaching the surface, the pale sheet AND the head-bound "
                      + "reflection can only have come from a texture or a constant compiled into "
                      + "VFX/Water_Shd_Trans — which is exactly what this build removes. So: if "
                      + "SWAPPED above equals the film count and the pool is STILL pale, the "
                      + "material swap took and the pale pixels are not being drawn by the film's "
                      + "material at all, which means a SECOND renderer is stacked over it and the "
                      + "FLOOR CENSUS is where to look. If SWAPPED is 0 or short of the film "
                      + "count, the swap FAILED on those renderers and the photograph says nothing "
                      + "about the hypothesis — that is a defect in this driver, not a finding.");
        }

        /// <summary>
        /// Read the band back OFF THE LIVE MATERIAL INSTANCE the renderer draws with, beside the
        /// authored value on the shared material.
        ///
        /// <para>This is the answer to the <c>UNDONE: 0 re-asserts</c> failure mode. A line that
        /// says what we called is worth nothing — ModBuild 160 called <c>DisableKeyword</c> and
        /// repainted <c>_Edge_Colour</c>, both lines appeared, and the sheet stayed white. What is
        /// worth something is the value the shader will actually sample, read out of the instance
        /// after every write in the frame has run, plus the instance's live keyword list. A
        /// property this shader does not declare is named LOUDLY rather than omitted, because a
        /// name that silently does not exist is otherwise indistinguishable from a write that
        /// landed and did nothing — which is exactly the trap of writing into a compiled shader
        /// nobody in this project has read.</para>
        /// </summary>
        private void AppendReadBack(System.Text.StringBuilder sb)
        {
            sb.Append(" | BAND READ-BACK (off the live material instance, after the writes — not "
                      + "what we called, what the shader will sample): ");
            Renderer? sample = FilmSample();
            if (sample == null || !_owned.TryGetValue(sample, out Owned o))
            {
                sb.Append("no film renderer is currently tracked, so there is nothing to read "
                          + "back — check the tracked count at the head of this line.");
                return;
            }
            Material? shared = o.Shared.Length > 0 ? o.Shared[0] : null;
            Material? inst = o.Instances.Length > 0 ? o.Instances[0] : null;
            if (shared == null || inst == null)
            {
                sb.Append("the film sample has no material pair to compare.");
                return;
            }
            if (OwnSurfaceActive)
            {
                sb.Append("suppressed — WaterSettings.OwnSurface has replaced this instance's whole "
                          + "material, so VFX/Water_Shd_Trans' band properties are not what it "
                          + "draws with and printing them would report on a shader that is "
                          + "deliberately gone. The OWN SURFACE block above is the read-back that "
                          + "applies now. Everything this block used to say is settled: every band "
                          + "width read back at 0 and every band colour at alpha 0 on ModBuild "
                          + "161, the user's verdict was 'Keine Änderungen bei der Wasser "
                          + "Problematik', and the diagnostic paint confirmed on hardware that the "
                          + "renderers are ours.");
                return;
            }

            try
            {
                sb.Append('\'').Append(sample.name).Append("': ");
                AppendColourReadBack(sb, shared, inst, "_Color_Tint", ColorTintId);
                for (int i = 0; i < BandWidthIds.Length; i++)
                {
                    AppendFloatReadBack(
                        sb, shared, inst, WaterEdgeBand.BandWidthProperties[i], BandWidthIds[i]);
                }
                for (int i = 0; i < BandColourIds.Length; i++)
                {
                    AppendColourReadBack(
                        sb, shared, inst, WaterEdgeBand.BandColourProperties[i], BandColourIds[i]);
                }
                AppendFloatReadBack(
                    sb, shared, inst, WaterEdgeBand.EdgeToggleProperty, EdgeToggleId);
                sb.Append("keyword ").Append(EdgeColourKeyword).Append(" authored ")
                  .Append(shared.IsKeywordEnabled(EdgeColourKeyword) ? "ON" : "OFF")
                  .Append(" -> instance ")
                  .Append(inst.IsKeywordEnabled(EdgeColourKeyword) ? "STILL ON" : "OFF")
                  .Append("; instance keywords=[").Append(string.Join(",", inst.shaderKeywords))
                  .Append("]. THE READING THAT MATTERS, and it has already been taken: ModBuild "
                          + "161 read every one of these back at 0 / alpha 0 with an empty keyword "
                          + "list, the diagnostic paint proved on hardware that these renderers "
                          + "are ours, and the user's verdict was still 'Keine "
                          + "Änderungen bei der Wasser Problematik'. So the pale sheet and the "
                          + "head-bound reflection come from a texture or a constant compiled into "
                          + "VFX/Water_Shd_Trans and no shader property reaches them. This block "
                          + "is now only running because WaterSettings.OwnSurface is OFF or its shader "
                          + "did not resolve — the mechanism that answers this is the OWN SURFACE "
                          + "block, which replaces the whole material rather than tuning it.");
            }
            catch (Exception e)
            {
                sb.Append("unreadable: ").Append(e.GetType().Name);
            }
        }

        /// <summary>
        /// A live, tracked FILM renderer to read the band back off, self-healing.
        ///
        /// <para><see cref="_censusSample"/> is set once and these quads are destroyed and
        /// re-instantiated constantly (Apparance: 109 placements in one logged session), so a
        /// sample that is merely REMEMBERED goes stale and the read-back would report "nothing to
        /// read" for the rest of a session while 17 films were tracked. That failure would look
        /// exactly like the driver having stopped, which is the confusion this whole round exists
        /// to end. So a dead or un-owned sample is replaced from the tracked set instead.</para>
        /// </summary>
        private Renderer? FilmSample()
        {
            Renderer? cached = _censusSample;
            if (cached != null && _owned.TryGetValue(cached, out Owned held) && held.IsFilm)
                return cached;
            for (int i = 0; i < _tracked.Count; i++)
            {
                Renderer r = _tracked[i];
                if (r == null || !_owned.TryGetValue(r, out Owned o) || !o.IsFilm)
                    continue;
                _censusSample = r;
                return r;
            }
            return null;
        }

        private static void AppendFloatReadBack(
            System.Text.StringBuilder sb, Material shared, Material inst, string name, int id)
        {
            if (!shared.HasProperty(id))
            {
                sb.Append(name).Append(" ABSENT FROM THIS SHADER; ");
                return;
            }
            sb.Append(name).Append(' ').Append(shared.GetFloat(id).ToString("0.###"))
              .Append(" -> ").Append(inst.GetFloat(id).ToString("0.###")).Append("; ");
        }

        private static void AppendColourReadBack(
            System.Text.StringBuilder sb, Material shared, Material inst, string name, int id)
        {
            if (!shared.HasProperty(id))
            {
                sb.Append(name).Append(" ABSENT FROM THIS SHADER; ");
                return;
            }
            sb.Append(name).Append(' ').Append(shared.GetColor(id)).Append(" -> ")
              .Append(inst.GetColor(id)).Append("; ");
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

            // --- the EDGE / FOAM / BORDER band, authored -> written, property by property -------
            // This is the block round four exists for. spiegeltiles.jpg shows a near-WHITE sheet
            // where _Color_Tint is dark green, so the visible pixels are this band and not the
            // body; and _WaterBorderWidth/_WaterBorderCol is a second border mechanism with no
            // keyword, which nothing before this build had ever touched.
            if (o.IsFilm)
            {
                sb.Append(" | EDGE BAND WRITES: ");
                try
                {
                    Material? inst = o.Instances.Length > 0 ? o.Instances[0] : null;
                    if (mat == null || inst == null)
                        sb.Append("<no material>");
                    else if (OwnSurfaceActive)
                        sb.Append("suppressed — WaterSettings.OwnSurface has replaced this material "
                                  + "outright; the band belongs to a shader this instance no "
                                  + "longer runs");
                    else
                        ApplyWaterFilm(mat, inst, sb);
                }
                catch (Exception e)
                {
                    sb.Append("unreadable: ").Append(e.GetType().Name);
                }

                // THE MESH THE GAME ACTUALLY SUPPLIED, and the two things about it that decide
                // what is on screen: whether it carries a vertex COLOUR (our fragment multiplies
                // its tint by it, so a dark or zero-alpha one would darken the film for a reason no
                // property of ours could explain), and how coarsely it samples the swell.
                //
                // MESH.TRIANGLES IS ONLY READ WHEN THE MESH SAYS IT IS READABLE. ModBuild 164's
                // census printed "33 verts / 0 tris" for this film, which is not a mesh with no
                // triangles — it is Mesh.triangles returning an empty array (and logging an engine
                // error) because the game imports its meshes without Read/Write. Printing a 0 that
                // means "we are not allowed to look" next to a 33 that means "there really are 33"
                // is how a whole round was spent on the wrong hypothesis, so isReadable is stated
                // FIRST and the triangle count is simply absent when it cannot be had.
                sb.Append(" | FILM MESH: ");
                try
                {
                    var mf = r.GetComponent<MeshFilter>();
                    Mesh? mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null)
                    {
                        sb.Append("no MeshFilter/sharedMesh (a SkinnedMeshRenderer or a "
                                  + "procedural quad) — vertex colour unknown");
                    }
                    else
                    {
                        bool hasColour = mesh.HasVertexAttribute(VertexAttribute.Color);
                        bool readable = mesh.isReadable;
                        Bounds lb = mesh.bounds;
                        sb.Append('\'').Append(mesh.name).Append("' ")
                          .Append(mesh.vertexCount).Append(" verts, ")
                          .Append(readable
                              ? (mesh.triangles.Length / 3) + " tris"
                              : "isReadable=FALSE so the triangle count CANNOT be read at all "
                                + "(and no CPU code could subdivide this mesh either — which is "
                                + "why the swell's geometry comes from the shader's own "
                                + "tessellator and not from a mesh swap)")
                          .Append(", local bounds centre (")
                          .Append(lb.center.x.ToString("0.##")).Append(',')
                          .Append(lb.center.y.ToString("0.##")).Append(',')
                          .Append(lb.center.z.ToString("0.##")).Append(") size (")
                          .Append(lb.size.x.ToString("0.###")).Append(',')
                          .Append(lb.size.y.ToString("0.###")).Append(',')
                          .Append(lb.size.z.ToString("0.###")).Append(')')
                          .Append(" | BOUNDS PAD: ").Append(_swellNote)
                          .Append(" | vertexColour=")
                          .Append(hasColour)
                          .Append(hasColour
                              ? " — our fragment MULTIPLIES its tint by the mesh's vertex colour, "
                                + "so if WaterSettings.OwnSurface produces a film that is darker than "
                                + "the tint above, or invisible in places, this is where it comes "
                                + "from and no property of ours can correct it"
                              : " — nothing modulates our tint, so the film draws exactly the "
                                + "colour named in the OWN SURFACE block");
                    }
                }
                catch (Exception e)
                {
                    sb.Append("unreadable: ").Append(e.GetType().Name);
                }
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
                // depth-reading shader has a VALID per-eye _CameraDepthTexture at all
                // (Rig/VRRigDriver.HeadCamera.cs owns that bit, and [Optimize] HeadDepthPrepass is
                // the only thing that asks for it now). Printed because the game's water shader
                // reads it, and a room whose film fell back to that shader is a room where this
                // value decides whether its shoreline is pinned across the whole quad.
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
