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
/// <para>ROUND FOUR, AND WHAT THE PHOTOGRAPH FINALLY SETTLED. ModBuild 160's writes all landed —
/// the hardware log proves it property by property (<c>_Smoothness 0.754 -> 0.08</c>,
/// <c>_Color_Tint.a</c> capped at 0.45, <c>_Edge_Colour</c> repainted in the body colour,
/// <c>_EDGECOLOUR_TOGGLE_ON</c> cleared, a local flat-cubemap probe built and REACHING the water)
/// — and the user's verdict was <i>"Keinen Unterschied bei der Reflektion."</i> Not "a bit
/// better": none. <c>.planning/debug/spiegeltiles.jpg</c> is the evidence nobody had put beside
/// those numbers. The affected hexes are a flat, pale, milky near-WHITE sheet, LIGHTER than the
/// stone around them, with the hex grid showing through. <b>The authored body colour is
/// <c>_Color_Tint</c> = RGBA(0.195, 0.311, 0.131, 0.737) — dark green — and a dark green body
/// cannot produce a near-white sheet under any tuning of its alpha.</b> So the visible pixels were
/// never the body term; they are the EDGE / FOAM / BORDER term, whose <c>_Edge_Colour</c> is
/// RGBA(0.887, 0.887, 0.887, 0.867) and whose second, independent mechanism
/// <c>_WaterBorderCol</c> = RGBA(0.670, 0.617, 0.528, 0.561) has no toggle keyword at all and had
/// never been touched by anything this module shipped. Three rounds were spent tuning the body of
/// a surface whose visible pixels come from its shoreline.</para>
///
/// <para>WHAT ROUND FOUR ADDS, and why in this order. (a) <c>[Water] DebugPaint</c> — the
/// instrument, and the most valuable thing here. Three hardware rounds died on an ambiguity no log
/// line could resolve: whether the renderers this driver adopts are the renderers the user is
/// pointing at. One toggle now paints every tracked film flat MAGENTA and every tracked basin flat
/// CYAN, on an unlit shader with the game's shading bypassed entirely, and the answer is visible in
/// one second: magenta means we own the film and the only remaining question is WHICH property;
/// cyan means the basin bed is what he has been pointing at; unchanged pale white means we have
/// been tuning objects that are not what he is looking at, and the <c>FLOOR CENSUS</c> in the same
/// log names what actually is. (b) The band is now collapsed NUMERICALLY, not through a keyword —
/// every width to zero and every band colour to the body hue at alpha zero, <c>_WaterBorderWidth</c>
/// and <c>_WaterBorderCol</c> included — because a <c>DisableKeyword</c> is invisible in the log's
/// own terms and cannot reach a mechanism that has no keyword. See <see cref="WaterEdgeBand"/>.
/// (c) <c>[Water] BodyOnly</c> — the last resort the user can reach without a rebuild: the film
/// forced to its authored green at the capped alpha with every edge, foam and border term at zero
/// and every reflection scalar at zero, ripple normals intact. If the white survives THAT while
/// DebugPaint proves we own the renderer, the white is a texture or a constant inside the compiled
/// shader, no property can reach it, and the only remaining move is the mod's own water shader.
/// The census says exactly that, in those words, rather than shipping another guess.</para>
///
/// <para>THE MEASUREMENT THAT DROVE ModBuild 160, kept because its answer is now known. ModBuild
/// 159 dropped <c>_Color_Tint</c>'s alpha from 0.737 to 0.45 through a
/// <see cref="MaterialPropertyBlock"/> and the user reported the look IDENTICAL. The hardware
/// census answered why: <c>instancing=True</c> on both materials, and in the built-in pipeline a
/// batched instanced draw takes non-instanced properties from the MATERIAL, so that property block
/// never reached the shader. H1 is confirmed and dead; the material instances below are what
/// killed it, and nothing in round four re-opens it.</para>
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
/// PARKED under VR, so nothing else is writing a usable depth texture either.</para>
///
/// <para>AND ROUND THREE PROVED THAT NEUTRALISING THAT BAND BY COLOUR AND KEYWORD WAS NOT ENOUGH.
/// ModBuild 160 landed both halves — <c>_Edge_Colour</c> repainted in the body colour AND
/// <c>_EDGECOLOUR_TOGGLE_ON</c> cleared — and the sheet is still white in the user's own words. Two
/// things follow, and both are acted on. First, a keyword clear is invisible in the log's own
/// terms: we can report that <c>DisableKeyword</c> was CALLED, never that the compiled variant
/// branches the way we assumed. Second, and decisively, <c>_WaterBorderWidth</c> /
/// <c>_WaterBorderCol</c> is a SECOND border mechanism with no toggle keyword whatsoever, so the
/// keyword clear could not have reached it and nothing this module ever shipped had. The band is
/// therefore attacked by NUMBER now — every width to zero, every band colour to the body hue at
/// alpha zero, which draws nothing at EITHER extreme of a pinned depth fade and so does not depend
/// on a sign nobody here can read. <see cref="WaterEdgeBand"/> owns that arithmetic and its
/// never-raise invariant, and <c>WaterEdgeVectors</c> pins it.</para>
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
/// <para>ROUND FIVE — <c>[Water] OwnSurface</c>, AND WHY THERE IS NOTHING LEFT TO TUNE. ModBuild
/// 161's <c>BAND READ-BACK</c> block reads values back off the LIVE MATERIAL INSTANCE after the
/// writes, i.e. exactly what the shader samples, and it proves every property this module can reach
/// is already neutral: <c>_Edge_Distance 0.2 -> 0</c>, <c>_Edge_Colour_Distance 0.9 -> 0</c>,
/// <c>_WaterBorderWidth 0.1 -> 0</c>, <c>_Edge_Colour</c> and <c>_WaterBorderCol</c> both repainted
/// in the body hue at alpha 0, <c>_EdgeColour_Toggle 1 -> 0</c>, the instance's keyword list EMPTY,
/// <c>_Smoothness 0.754 -> 0.08</c>, every metal/reflection scalar at 0, <c>_Color_Tint.a</c> at
/// 0.45. Verdict: <i>"Keine Änderungen bei der Wasser Problematik."</i> Every band is gone and the
/// sheet is still pale, and he adds <i>"Ich konnte aber mit den anderen Einstellungen die
/// kopf-gebundene Reflektion nicht deaktivieren, egal was ich eingestellt hab."</i></para>
///
/// <para>AND THE ONE THING THAT WAS STILL AMBIGUOUS IS NOW MEASURED. <c>[Water] DebugPaint</c> was
/// built for exactly this and he has now run it: <i>"Die debug farbe funktioniert - alles färbt
/// sich magenta wie gewollt."</i> MAGENTA is the FILM's colour. <b>These are our renderers</b> —
/// three rounds of "are we even tuning what he is looking at" closed in one second, and that
/// question is dead. OWNED RENDERER + EVERY REACHABLE PROPERTY NEUTRAL + THE DEFECT UNCHANGED
/// leaves exactly one explanation, and it is no longer a hypothesis: the pale sheet AND the
/// head-bound reflection are a TEXTURE or a CONSTANT compiled into <c>VFX/Water_Shd_Trans</c>,
/// and nothing addressed by property name reaches either.</para>
///
/// <para>SO THE FILM'S WHOLE MATERIAL IS REPLACED — <c>[Water] OwnSurface</c>, default ON
/// (<see cref="WaterOwnSurface"/>): a mod-owned material out of the mod's own bundle, carrying the
/// tileset's OWN authored values, at the authored render queue. That deletes the game's shader from
/// the surface and everything compiled inside it. Only the FILM is re-based; the basin
/// (<c>Amp_Basic_N_MRAO</c>) is opaque ground, not a film, and keeps the property retune
/// below.</para>
///
/// <para>ModBuild 162 SHIPPED THAT ON <c>GloomhavenVR/Overlay</c> AND IT WORKED — <i>"Beide
/// Probleme gelöst, top!"</i> — AND THE CURE COST TOO MUCH LOOK: <i>"Allerdings: Das Wasser sieht
/// jetzt sehr viel schlechter aus. Das echte Wasser hatte ANimation und co. das will ich auch
/// wieder. Ich will es so nah wie möglich an dem 'echten' Wasser haben - aber eben so dass es in VR
/// funktioniert."</i> Overlay is a flat unlit sheet; it was chosen because it was the only bundled
/// shader that provably sampled no environment, which was the right emergency move and is not the
/// answer. The film now draws on <c>GloomhavenVR/WaterVR</c>, a shader authored into this mod's own
/// bundle for this one job: two scrolling normal layers at the tileset's own two tilings and two
/// speeds — which the census proves is the whole of the game water's motion — shaded against a
/// FIXED light direction so the highlights travel with the waves instead of with the head. Overlay
/// remains the fallback when the bundle cannot yield <c>WaterVR</c>, and the OWN SURFACE census
/// block names which of the two is live rather than which was wanted.</para>
///
/// <para>THE HARD REQUIREMENT SURVIVES THE UPGRADE, and it is why the new shader has no specular in
/// the usual sense: <b>nothing in it may depend on the view direction.</b> No
/// <c>unity_SpecCube</c>, no <c>reflect()</c>, no cube sample, no <c>worldRefl</c>, no Fresnel and
/// no Blinn-Phong half vector — a half-vector highlight slides across the surface with the head,
/// which is the reported defect re-created out of the mod's own shader. It is also the strongest
/// stereo guarantee available: under MultiPass each eye renders its own pass, so a view-dependent
/// term is a different image per eye, and this project has already parked one feature permanently
/// over that (<c>.planning/wall-fade-stereo-rivalry.md</c>).
/// <c>WaterOwnSurfaceVectors</c> sweeps both shaders' source and fails the build gate on a
/// hit.</para>
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
    private static readonly int InvertDepthFadeId =
        Shader.PropertyToID(WaterEdgeBand.InvertDepthFadeProperty);

    // --- the ANIMATION values, read off the GAME's shared material and handed to
    //     GloomhavenVR/WaterVR. Every one of these names was printed by the hardware WATER SURFACE
    //     census of TERRAIN_GEN_WaterPlane_Crypt_Mat, so none is guessed from a shader nobody can
    //     open. What is read is what the tileset authored; what is written is derived from it and
    //     from nothing else (WaterOwnSurface.ScrollRate / NormalStrength), and the OWN SURFACE
    //     block prints the derivation so a reader of the log can check it against these numbers.
    private static readonly int NormalMapId =
        Shader.PropertyToID(WaterOwnSurface.NormalMapProperty);
    private static readonly int NormalTilingsId =
        Shader.PropertyToID(WaterOwnSurface.NormalTilingsProperty);
    private static readonly int SpeedAId = Shader.PropertyToID(WaterOwnSurface.ScrollAProperty);
    private static readonly int SpeedBId = Shader.PropertyToID(WaterOwnSurface.ScrollBProperty);
    private static readonly int NoiseSpeedId =
        Shader.PropertyToID(WaterOwnSurface.GameNoiseSpeedProperty);
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

    /// <summary>The two flat colours <c>[Water] DebugPaint</c> writes. Chosen to be impossible to
    /// confuse with anything the tileset can produce — there is no magenta and no cyan anywhere in
    /// <c>spiegeltiles.jpg</c> — and to be distinguishable from each other at a glance across a VR
    /// table, which is the whole job.</summary>
    private static readonly Color FilmPaint = new(1f, 0f, 1f, 1f);

    /// <inheritdoc cref="FilmPaint"/>
    private static readonly Color BasinPaint = new(0f, 1f, 1f, 1f);

    /// <summary>Unlit shaders <c>[Water] DebugPaint</c> may borrow, best first. Both ship inside
    /// the mod's own bundle, so both are reachable through <see cref="BundleShaders"/> even though
    /// nothing else in a water room references them — the SHADER.FIND lesson, which has cost this
    /// project two builds. <c>GloomhavenVR/HeadUnlit</c> is the better instrument: it is fully
    /// unlit (albedo × tint, no lighting, no environment), two-sided (<c>Cull Off</c>, so a quad
    /// wound away from the viewer still paints — the WINDING lesson), and its <c>_MainTex</c>
    /// declares a <c>"white"</c> default, so clearing the texture leaves the tint alone on
    /// screen.</summary>
    private static readonly string[] PaintShaderCandidates =
        { "GloomhavenVR/HeadUnlit", "GloomhavenVR/Overlay" };

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
            + "free camera. ROUND FIVE STOPS TUNING THE GAME'S SHADER AND REPLACES IT. Two "
            + "hardware readings close the question between them: [Water] DebugPaint came back "
            + "'alles färbt sich magenta wie gewollt', so these ARE the renderers in "
            + "spiegeltiles.jpg; and ModBuild 161's BAND READ-BACK proves every property this "
            + "module can reach is already neutral on the live instance — every band width 0, "
            + "every band colour at alpha 0, the keyword list empty, _Smoothness 0.08, the "
            + "metal/reflection ceiling 0 — against 'Keine Änderungen bei der Wasser Problematik' "
            + "and 'Ich konnte aber mit den anderen Einstellungen die kopf-gebundene Reflektion "
            + "nicht deaktivieren'. Owned renderer plus every reachable property neutral plus the "
            + "defect unchanged leaves one explanation: a texture or a constant compiled INSIDE "
            + "VFX/Water_Shd_Trans. So [Water] OwnSurface (default ON) gives the FILM a material "
            + "of the mod's own on '" + WaterOwnSurface.FilmShaderName + "' — ANIMATED: the "
            + "tileset's own _Normal_Map scrolled twice at its own two _NormalTilings and two "
            + "_WaterUVAnimSpeed rates, in the tileset's own green at the [Water] Opacity alpha, at "
            + "the authored render queue, with no shoreline, no foam, no depth read and NO VIEW "
            + "DIRECTION ANYWHERE IN IT — no cube sample, no reflect(), no Fresnel and no "
            + "half-vector specular, because a half-vector highlight slides with the head exactly "
            + "as the report describes. The glints come from the moving NORMALS against a fixed "
            + "light direction, so they travel with the waves and are identical in both MultiPass "
            + "eyes by construction. ModBuild 162's flat sheet on '"
            + WaterOwnSurface.FallbackFilmShaderName + "' is now only the FALLBACK for a bundle "
            + "that does not yield the water shader ('Das Wasser sieht jetzt sehr viel schlechter "
            + "aus'). The basin keeps the property retune. Read the OWN SURFACE block of the WATER "
            + "SURFACE STATE line: it names which of the two shaders is actually on the renderers "
            + "and counts them, rather than which one was wanted.");
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

        /// <summary>ROUND FIVE. Replace the water FILM's material outright with a mod-owned
        /// material on <see cref="WaterOwnSurface.FilmShaderName"/>, instead of retuning the
        /// game's. Default ON — see the class header for why there is nothing left to
        /// retune.</summary>
        internal static ConfigEntry<bool>? OwnSurface;

        /// <summary>Multiplier on how fast the mod's own water film scrolls its two normal layers.
        /// The rates come from the tileset's own <c>_WaterUVAnimSpeedA/B</c>; this is the dial that
        /// exists because the game's shaders ship COMPILED and the exact convention behind those
        /// numbers cannot be read offline (see
        /// <see cref="WaterOwnSurface.ScrollRate"/>).</summary>
        internal static ConfigEntry<float>? RippleSpeed;

        /// <summary>How strong the moving glints on the mod's own water film are. Zero leaves the
        /// wave shading and removes only the highlights.</summary>
        internal static ConfigEntry<float>? Shimmer;

        /// <summary>How BIG the waves are — one multiplier over both the swell's wavelength and
        /// the two ripple layers' resolved tilings, so the whole surface scales together.</summary>
        internal static ConfigEntry<float>? WaveScale;

        /// <summary>How high the swell actually lifts the water's geometry, as a fraction of a
        /// water quad's own width. 0 gives the film its authored mesh back and makes it flat
        /// again.</summary>
        internal static ConfigEntry<float>? SwellHeight;

        /// <summary>THE INSTRUMENT. Paint every tracked renderer a flat unlit colour — film
        /// magenta, basin cyan — so one look answers whether we own what the user is pointing
        /// at.</summary>
        internal static ConfigEntry<bool>? DebugPaint;

        /// <summary>The last resort a user can reach without a rebuild: body tint only, every
        /// edge / foam / border term and every reflection scalar at zero.</summary>
        internal static ConfigEntry<bool>? BodyOnly;

        /// <summary>Which way <c>_InvertDepthFade</c> points. See
        /// <see cref="WaterDepthFadeMode"/> — a dial, never a silent choice.</summary>
        internal static ConfigEntry<WaterDepthFadeMode>? DepthFade;

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
                + "default because of that cost, NOT because the water looks right without it: it "
                + "does not, and this is the one switch that lets you see whether a real depth "
                + "texture is what the shoreline was missing all along. [Water] BodyOnly overrides "
                + "it. Water rooms only.");
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
            OwnSurface = config.Bind("Water", "OwnSurface", true,
                "Give the water FILM a material of the mod's own instead of retuning the game's. "
                + "The water still renders, at the same place in the draw order, in the tileset's "
                + "own colour and with the tileset's own ripple — what changes is that the shader "
                + "drawing it is GloomhavenVR/WaterVR out of this mod's bundle rather than the "
                + "game's VFX/Water_Shd_Trans. WHY THIS EXISTS: four rounds of property tuning are "
                + "read back off the live material and every one of them landed — every shoreline, "
                + "foam and border width at 0, every band colour at alpha 0, the keyword list "
                + "empty, every gloss and metal value at 0 — and the pool was still a pale, milky "
                + "sheet with a reflection that swung with your head, which no dial could switch "
                + "off. That can only come from a texture or a constant compiled INSIDE the game's "
                + "shader, and replacing the whole shader removes it. WHAT THE MOD'S OWN WATER IS: "
                + "the game's own normal map, scrolled as TWO layers at the game's own two tilings "
                + "and two speeds, which is the whole of the animation the real water had. The "
                + "glints come from those moving ripples lit by a FIXED light direction — never "
                + "from the view direction, which is the one thing this shader may not contain, "
                + "because a highlight computed from where your eye is slides across the water as "
                + "you turn your head (that is the original defect) and is a different image in "
                + "each eye. AND THE SURFACE ACTUALLY HAS RELIEF: the film is handed a finer mesh "
                + "and a slow swell moves its vertices, because no shading term can make a flat "
                + "sheet three-dimensional and 'weiße Streifen auf einer flachen Oberfläche' was "
                + "the verdict on trying. Tune it with [Water] SwellHeight (how high), [Water] "
                + "WaveScale (how big), [Water] RippleSpeed (how fast) and [Water] Shimmer (how "
                + "much sparkle). The basin bed "
                + "and rim under the water are NOT replaced — they are opaque ground, not a film, "
                + "and keep the ordinary retune. OFF puts the game's own water shader back "
                + "immediately and hands [Water] BodyOnly, ShoreFoam and DepthFade back their "
                + "meaning.");
            RippleSpeed = config.Bind("Water", "RippleSpeed", 0.5f,
                new ConfigDescription(
                    "How fast the water moves, as a multiple of the rate the tileset authored. It "
                    + "scales BOTH the swell's phase speed and the two ripple layers' drift. THE "
                    + "DEFAULT IS HALF, not one, and that is a re-base rather than a preference: "
                    + "through ModBuild 163 the authored rate was fed to the shader in TEXTURE "
                    + "repeats per second, so on screen it came out as rate divided by tiling — "
                    + "4.3 and 5.0 WORLD UNITS per second across a one-metre hex, which is the "
                    + "'extrem schnelle hektische weiße Streifen die vorbeisausen' of the report. "
                    + "The rate now means world units per second whatever the tiling is, and half "
                    + "of the authored 0.6 is the drift of quiet water. The census prints the "
                    + "resolved rate in world units per second, so 'too fast' is a number in the "
                    + "log rather than an argument. 0 freezes the surface without otherwise "
                    + "changing how it looks.",
                    new AcceptableValueRange<float>(0f, 3f)));
            Shimmer = config.Bind("Water", "Shimmer", 0.10f,
                new ConfigDescription(
                    "How strong the moving glints on the mod's own water film are. They are made "
                    + "by the moving surface turning toward a FIXED light, so they are born on a "
                    + "crest, travel with it and break up where the swell and the ripple disagree "
                    + "— and they do NOT move when you move your head, which is the difference "
                    + "between this and the reflection the original report was about. RE-BASED "
                    + "FROM 0.35: the highlight used to be a tight lobe that was also added into "
                    + "the film's OPACITY, so a crest went bright and opaque at once, which is a "
                    + "white streak by construction. It is now a broad, dim sheen measured against "
                    + "the flat sheet's own brightness — still water glints exactly zero — and it "
                    + "never touches the opacity. 0 removes the highlights and keeps the wave "
                    + "shading. Raise it if the pool looks dead; lower it if the sparkle is busy.",
                    new AcceptableValueRange<float>(0f, 2f)));
            WaveScale = config.Bind("Water", "WaveScale", 1f,
                new ConfigDescription(
                    "How BIG the waves are, as a multiple of the shipped size — one dial over the "
                    + "swell's wavelength AND the two ripple layers, so the whole surface scales "
                    + "together and never comes apart into a big wave carrying the wrong-sized "
                    + "detail. 1.0 puts about three swell crests across a pool of one-metre hexes "
                    + "(a 1.6 m wavelength). Raise it for a longer, lazier swell; lower it for a "
                    + "choppier one. It does not change how fast the water travels: the phase "
                    + "speed scales with the wavelength, so a longer wave takes proportionally "
                    + "longer to pass and the water keeps the same character. The census prints "
                    + "the resolved wavelengths in metres.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            SwellHeight = config.Bind("Water", "SwellHeight", 0.045f,
                new ConfigDescription(
                    "How HIGH the swell lifts the water, as a fraction of one water quad's own "
                    + "width — so a hex about a metre across gets 4.5 cm at the default, and a "
                    + "diorama at another scale gets a wave that looks the same rather than one "
                    + "that is invisible or enormous. THIS IS REAL GEOMETRY, not shading: the film "
                    + "is handed a finer mesh and its vertices actually move, which is what makes "
                    + "the surface read as three-dimensional at a grazing angle instead of as "
                    + "stripes painted on a flat sheet. The wave is a function of world position "
                    + "and time only, so it is identical in both eyes. 0 gives the film its "
                    + "authored mesh back and makes it flat again — which is also the A/B for "
                    + "whether the relief is worth its vertices. The ceiling is set by the 9 cm "
                    + "the census measured between the water and the basin bed underneath it: a "
                    + "trough may never dip through its own pool floor.",
                    new AcceptableValueRange<float>(0f, 0.05f)));
            DebugPaint = config.Bind("Water", "DebugPaint", false,
                "DIAGNOSTIC — paints every water renderer this mod has taken over in a flat, "
                + "unmistakable colour, with the game's own shading switched off entirely: the "
                + "water FILM becomes solid MAGENTA, the basin bed and rim become solid CYAN. It "
                + "does NOT hide anything; the water still renders, just in one flat colour. Turn "
                + "it on for one second and look at the pale hexes: MAGENTA means the mod has hold "
                + "of the water film and the only open question is which of its properties makes "
                + "it pale; CYAN means the basin floor under the water is what you have been "
                + "looking at; STILL PALE AND WHITE means the mod is tuning objects that are not "
                + "what you see, and the log's FLOOR CENSUS names what actually is. Three rounds "
                + "of tuning have died on exactly that ambiguity. IT HAS NOW BEEN RUN AND THE "
                + "ANSWER WAS MAGENTA — the mod holds the surfaces you were pointing at, which is "
                + "what made [Water] OwnSurface the right next move. Keep this switch: it is still "
                + "the fastest way to re-confirm ownership in one second if anything about the "
                + "pool changes again. Turn it back off afterwards — the mod restores the authored "
                + "materials immediately.");
            BodyOnly = config.Bind("Water", "BodyOnly", false,
                "LAST RESORT — forces the water film to nothing but its own authored colour (dark "
                + "green) at the capped opacity, with every shoreline, foam and border term at "
                + "zero and every gloss and metal value at zero, keeping only the ripple normals. "
                + "The photograph (spiegeltiles.jpg) shows a near-WHITE sheet where the game "
                + "authors a dark green tint, so the visible pixels are the shoreline band and not "
                + "the water body; this switch removes every band this shader exposes at once, "
                + "regardless of ShoreFoam. ONLY HAS AN EFFECT WHILE [Water] OwnSurface IS OFF: "
                + "this switch tunes the GAME's water shader, and OwnSurface replaces that shader "
                + "outright. Its own question is already answered — the log read every band back "
                + "off the live material at zero and the sheet stayed pale — which is precisely "
                + "why OwnSurface was built and is on by default. Keep this for the A/B that shows "
                + "what the game's water looks like with every band removed.");
            // No AcceptableValueList here: it constrains T : IEquatable<T>, which an enum is not.
            // BepInEx already enumerates an enum entry itself, and so does the VR options menu.
            DepthFade = config.Bind("Water", "DepthFade", WaterDepthFadeMode.Authored,
                    "Which way the shader's depth fade points (_InvertDepthFade, authored 0). The "
                    + "VR head camera writes no depth texture, so this term is CONSTANT across the "
                    + "whole surface and this setting only chooses which extreme the entire "
                    + "surface sits at — shoreline everywhere, or open water everywhere. Authored "
                    + "(default) leaves the game's own value alone and is the honest setting: "
                    + "nobody has read this shader's source, so which extreme is which cannot be "
                    + "derived, only seen. NotInverted and Inverted force it. Inverted is the only "
                    + "setting in this whole section that raises a value above what the game "
                    + "authored, which is why it is never a default. Try it only if the surface is "
                    + "still wrong with BodyOnly on.");
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
        private float _appliedRippleSpeed = float.NaN;
        private float _appliedShimmer = float.NaN;
        private float _appliedWaveScale = float.NaN;
        private float _appliedSwellHeight = float.NaN;

        /// <summary>What the last film mesh swap did, or why it did nothing. Printed by the
        /// census's FILM MESH block — a mesh swap that silently failed leaves a flat pool that
        /// looks exactly like a shader that shipped and did nothing, which is the trap this whole
        /// module was built out of.</summary>
        private string _swellNote = "no film mesh handled yet";
        private bool _appliedBasin = true;
        private bool _appliedBodyOnly;
        private WaterDepthFadeMode _appliedDepthFade = WaterDepthFadeMode.Authored;

        /// <summary>Whether the tracked set currently carries the diagnostic paint. A change here
        /// is NOT a re-apply: the paint replaces the SHADER on our instance, and the only exact
        /// way back is a fresh instance off the authored shared material — so the flip releases
        /// everything and lets the next tick re-adopt.</summary>
        private bool _appliedDebugPaint;

        /// <summary>The unlit shader the paint borrowed, resolved once, and how. Null while
        /// nothing has asked for it, or when no bundled unlit shader could be reached at all —
        /// which is a stated fallback, not a silent one.</summary>
        private Shader? _paintShader;
        private string _paintHow = "no unlit shader resolved yet";

        /// <summary>Whether the tracked FILMS currently carry the mod's own material. Like
        /// <see cref="_appliedDebugPaint"/> and for the same reason, a change here is NOT a
        /// re-apply: <c>Material.shader =</c> has no exact inverse, so a flip releases everything
        /// and lets the next tick re-adopt off the authored shared material.</summary>
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

            // ---- THE FILM'S MESH. The swell displaces vertices, and TERRAIN_Water_Plane is a
            //      flat plane with a handful of them, so the film is handed a subdivided copy
            //      (WaterSwellMesh) and owes the original back. Everything here is recorded BEFORE
            //      the swap and restored by ReleaseAt, exactly as the material array is: an
            //      Apparance quad that kept a mod mesh after the mod let go would be a change the
            //      game has no way to undo.

            /// <summary>The film's MeshFilter, when it has one at all. Null for a renderer we
            /// never swapped — a SkinnedMeshRenderer, or a film whose mesh was too fine or not
            /// readable.</summary>
            internal MeshFilter? Filter;

            /// <summary>The mesh the game had on that filter, restored on release.</summary>
            internal Mesh? AuthoredMesh;

            /// <summary>What we put there instead, so the next tick can tell OUR mesh from a mesh
            /// the game swapped in behind us.</summary>
            internal Mesh? SwellMesh;

            /// <summary>The film's largest horizontal extent in world units, measured off the
            /// renderer while the AUTHORED mesh was still on it. The swell's amplitude is a
            /// fraction of this, so it must never be read off the padded bounds of our own mesh —
            /// that would grow the wave a little every time it was measured.</summary>
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
            _appliedDebugPaint = WantDebugPaint;
            _appliedBodyOnly = WantBodyOnly;
            _appliedDepthFade = WantedDepthFade;
            _tick = Tick;
        }

        private static bool Want =>
            VRSession.IsRunning
            && WaterConfig.VRFriendlyWater != null
            && WaterConfig.VRFriendlyWater.Value;

        /// <summary>See <see cref="WantsDepthTexture"/>. Content-gated on purpose — and gated on
        /// <see cref="OwnSurfaceActive"/> as well, because the whole point of the depth texture is
        /// the GAME shader's depth-fed shoreline: once the film draws on the mod's own shader,
        /// which reads no depth at all, that request would buy a full extra opaque scene submission
        /// per eye for nothing. The basin never read depth either.</summary>
        internal bool NeedsDepth =>
            _tracked.Count > 0
            && !OwnSurfaceActive
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

        private static bool WantOwnSurface =>
            WaterConfig.OwnSurface == null || WaterConfig.OwnSurface.Value;

        private static float WantedRippleSpeed =>
            WaterConfig.RippleSpeed != null ? WaterConfig.RippleSpeed.Value : 1f;

        private static float WantedShimmer =>
            WaterConfig.Shimmer != null ? WaterConfig.Shimmer.Value : 0.10f;

        private static float WantedWaveScale =>
            WaterConfig.WaveScale != null ? WaterConfig.WaveScale.Value : 1f;

        private static float WantedSwellHeight =>
            WaterConfig.SwellHeight != null ? WaterConfig.SwellHeight.Value : 0.045f;

        /// <summary>Is the film actually drawing on the mod's own shader right now? The dial ANDed
        /// with the shader having been reached — never the dial alone. Four rounds have ended with
        /// a log full of intentions, so nothing in this file may report a wish as an outcome, and
        /// a bundle that failed to yield the shader has to leave every dependent decision
        /// (the depth request, the band writes, the census wording) exactly where it was.</summary>
        private bool OwnSurfaceActive => WantOwnSurface && _ownShader != null;

        private static bool WantDebugPaint =>
            WaterConfig.DebugPaint != null && WaterConfig.DebugPaint.Value;

        private static bool WantBodyOnly =>
            WaterConfig.BodyOnly != null && WaterConfig.BodyOnly.Value;

        private static WaterDepthFadeMode WantedDepthFade =>
            WaterConfig.DepthFade != null ? WaterConfig.DepthFade.Value : WaterDepthFadeMode.Authored;

        /// <summary>The sharpness ceiling actually in force. <c>[Water] BodyOnly</c> drives it to
        /// zero outright: the whole point of that switch is that NOTHING but the authored body
        /// colour can be contributing when it is on, so that a surface still white under it is
        /// proof about the shader rather than about our tuning. Zero is a LOWERING of the dial,
        /// so the never-raise invariant of <see cref="WaterReflectionCaps"/> is untouched.</summary>
        private static float EffectiveSmoothnessCap => WantBodyOnly ? 0f : WantedSmoothness;

        /// <inheritdoc cref="EffectiveSmoothnessCap"/>
        private static float EffectiveReflectivityCap => WantBodyOnly ? 0f : WantedReflectivity;

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

            // Before Discover AND before ReassertAll: Adopt() writes the film in the same call, so
            // a shader that resolved only afterwards would leave the first room's water on the
            // game's material until something else moved. Both resolvers are one dictionary hit
            // once they have succeeded.
            MaintainOwnShader();
            MaintainPaintShader();
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
            // THE PAINT FLIP IS A RELEASE, NOT A RE-APPLY. [Water] DebugPaint replaces the SHADER
            // on our material instance, and Material.shader= is not an operation with an exact
            // inverse: property values that the flat shader does not declare are gone, and the
            // ones it shares have been overwritten. The only restoration this module is willing to
            // claim is the one it can prove — hand every renderer its AUTHORED shared material
            // back, destroy the instances, and take fresh ones on the next tick. One frame of the
            // game's own water is the whole cost, and it is the same path RestoreAll already
            // guarantees on uninstall.
            bool paint = WantDebugPaint;
            if (paint != _appliedDebugPaint)
            {
                _appliedDebugPaint = paint;
                if (paint)
                    MaintainPaintShader();
                int had = _tracked.Count;
                RestoreAll();
                _next = 0f;
                _nextCensus = 0f;
                VRLog.Info(Name,
                    "WATER SURFACE: [Water] DebugPaint went " + (paint ? "ON" : "OFF") + " — "
                    + had + " tracked renderer(s) were handed their AUTHORED shared material back "
                    + "and every material instance we owned was destroyed; the next frame re-adopts "
                    + "them from scratch and "
                    + (paint
                        ? "paints the film flat MAGENTA and the basin flat CYAN. Look at the pale "
                          + "hexes now: MAGENTA means this driver owns the water film and the "
                          + "remaining question is purely WHICH property makes it pale; CYAN means "
                          + "the basin bed is the surface in the photograph; STILL PALE means "
                          + "neither of these renderers is what is being looked at and the FLOOR "
                          + "CENSUS names what is."
                        : "writes the ordinary retune. The DEBUG PAINT block of the next WATER "
                          + "SURFACE STATE line reports what is actually on the renderers, not "
                          + "what was intended."));
                return;
            }

            // [Water] OwnSurface is a shader swap on our instance too, so it flips the same way and
            // for the same reason: Material.shader= has no exact inverse. Hand every renderer its
            // AUTHORED shared material back, destroy the instances, and re-adopt from scratch on
            // the next tick. One frame of the game's own water is the whole cost.
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
                    "WATER SURFACE: [Water] OwnSurface went " + (own ? "ON" : "OFF") + " — "
                    + hadOwn + " tracked renderer(s) were handed their AUTHORED shared material "
                    + "back and every material instance we owned was destroyed; the next frame "
                    + "re-adopts them and "
                    + (own
                        ? "draws the water FILM on '" + _ownShaderName
                          + "' out of the mod's own bundle, in the tileset's own green at the "
                          + "[Water] Opacity alpha, rippling on the tileset's own normal map at "
                          + "its own two tilings and speeds, with no shoreline, no foam and no "
                          + "view-dependent term of any kind. The basin keeps the property retune. "
                          + "Read the OWN SURFACE block of the next WATER SURFACE STATE line: it "
                          + "names which of the two mod shaders is ACTUALLY on the renderers and "
                          + "counts them, not the ones we tried to swap."
                        : "puts the game's own VFX/Water_Shd_Trans back on the film, which also "
                          + "gives [Water] BodyOnly, ShoreFoam and DepthFade their meaning again."));
                return;
            }

            Camera? head = Rig.VRRigDriver.HeadCamera;
            bool granted = head != null && (head.depthTextureMode & DepthTextureMode.Depth) != 0;
            float smoothness = WantedSmoothness;
            float opacity = WantedOpacity;
            float reflectivity = WantedReflectivity;
            bool basin = WantBasin;
            bool bodyOnly = WantBodyOnly;
            WaterDepthFadeMode depthFade = WantedDepthFade;
            float rippleSpeed = WantedRippleSpeed;
            float shimmer = WantedShimmer;
            float waveScale = WantedWaveScale;
            float swellHeight = WantedSwellHeight;
            bool changed = granted != _depthGranted
                || !Mathf.Approximately(smoothness, _appliedSmoothness)
                || !Mathf.Approximately(opacity, _appliedOpacity)
                || !Mathf.Approximately(reflectivity, _appliedReflectivity)
                || !Mathf.Approximately(rippleSpeed, _appliedRippleSpeed)
                || !Mathf.Approximately(shimmer, _appliedShimmer)
                || !Mathf.Approximately(waveScale, _appliedWaveScale)
                // A SwellHeight change is not only a property write: at 0 the film gives its own
                // mesh back and at anything else it takes a subdivided one, so this compare is
                // what drives the mesh swap as well.
                || !Mathf.Approximately(swellHeight, _appliedSwellHeight)
                || basin != _appliedBasin
                || bodyOnly != _appliedBodyOnly
                || depthFade != _appliedDepthFade
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
            _depthGranted = granted;
            _appliedBodyOnly = bodyOnly;
            _appliedDepthFade = depthFade;
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
            bool paint = WantDebugPaint;

            // THE MESH FIRST, because the amplitude the material writes below is measured off the
            // renderer while its AUTHORED mesh is still on it. The swell is the one part of this
            // retune that is geometry rather than a property, so it is also the one part that has
            // to be given back: every path that stops wanting it — [Water] DebugPaint, OwnSurface
            // off, SwellHeight 0, release, uninstall — goes through RestoreFilmMesh.
            if (o.IsFilm && !paint && OwnSurfaceActive && WantedSwellHeight > 0f)
                MaintainFilmMesh(r, o);
            else
                RestoreFilmMesh(o);

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

                if (paint)
                {
                    PaintOne(shared, inst, o.IsFilm);
                    continue;
                }

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

        // ---- THE INSTRUMENT: [Water] DebugPaint ------------------------------------------------

        /// <summary>
        /// Paint ONE material instance a flat unlit colour, bypassing the game's shading entirely.
        ///
        /// <para>WHY THIS IS THE MOST VALUABLE THING IN ROUND FOUR. Three hardware rounds have now
        /// ended with a log full of writes that landed and a user who saw no change, and every one
        /// of them left the same question open: are the renderers this driver adopts the renderers
        /// he is pointing at? No log line can settle that — the FLOOR CENSUS can say a quad exists
        /// at a position, it cannot say that quad is the pale sheet in the photograph. A flat
        /// colour can, in one second, and it distinguishes the two candidates from each other at
        /// the same time.</para>
        ///
        /// <para>WHY IT SWAPS THE SHADER RATHER THAN TINTING. The whole hypothesis under test is
        /// that some term of the game's shader we have not reached is painting these pixels. A
        /// paint written into that same shader's colour properties would be filtered through
        /// exactly the term in question, so a null result would prove nothing — the instrument
        /// would share the failure mode of the thing it is measuring. Replacing the shader on OUR
        /// instance removes every one of the game's terms at once. The shared material is still
        /// never touched, and the renderer is never disabled: the water goes on rendering, in one
        /// flat colour (user ruling 2026-08-18).</para>
        ///
        /// <para>The instance keeps the AUTHORED render queue, so the paint draws exactly where
        /// the water drew — a magenta quad that sorted differently from the water it replaced
        /// would answer a different question than the one being asked.</para>
        /// </summary>
        private void PaintOne(Material shared, Material inst, bool isFilm)
        {
            Color paint = isFilm ? FilmPaint : BasinPaint;
            Shader? flat = _paintShader;
            try
            {
                inst.shaderKeywords = Array.Empty<string>();
                if (flat != null)
                {
                    inst.shader = flat;
                    if (inst.HasProperty("_Color"))
                        inst.SetColor("_Color", paint);
                    // Null leaves the shader's own declared default bound, which is "white" on
                    // both candidates — so the tint above is what reaches the screen.
                    if (inst.HasProperty("_MainTex"))
                        inst.SetTexture("_MainTex", null);
                    if (inst.HasProperty("_Cull"))
                        inst.SetFloat("_Cull", 0f);   // two-sided: winding must not hide the answer
                    if (inst.HasProperty("_ZWrite"))
                        inst.SetFloat("_ZWrite", 1f);
                    if (inst.HasProperty("_ZTest"))
                        inst.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                    if (inst.HasProperty("_SrcBlend"))
                        inst.SetFloat("_SrcBlend", (float)BlendMode.One);
                    if (inst.HasProperty("_DstBlend"))
                        inst.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    inst.renderQueue = shared.renderQueue;
                    return;
                }

                // NO UNLIT SHADER REACHED. This is a WEAKER instrument and the census says so
                // rather than letting a half-answer read as a whole one: the paint now goes
                // through the game's own shader, so a term that ignores the colour properties —
                // which is precisely what is suspected — will still show through. Every colour the
                // shader declares is written, so whichever one is on screen carries the paint.
                Shader sh = shared.shader;
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    if (sh.GetPropertyType(i) != ShaderPropertyType.Color)
                        continue;
                    inst.SetColor(sh.GetPropertyName(i), paint);
                }
            }
            catch (Exception e)
            {
                VRLog.Info(Name,
                    "WATER SURFACE: DebugPaint could not paint an instance of '" + shared.name
                    + "' (" + e.GetType().Name + ") — that renderer keeps the ordinary retune, so "
                    + "an unpainted surface in the headset may be this failure rather than an "
                    + "answer. The DEBUG PAINT count in the WATER SURFACE STATE line is what to "
                    + "read: it counts instances that ACTUALLY carry the flat shader.");
            }
        }

        /// <summary>
        /// The unlit shader the paint borrows, resolved through <see cref="BundleShaders"/>.
        ///
        /// <para><see cref="Shader.Find"/> alone would not do: a bundled shader referenced only
        /// from runtime C# is never loaded, <c>Shader.Find</c> returns null with the bundle open
        /// and nothing is thrown — the failure that has silently cost this project two shipped
        /// builds. <see cref="BundleShaders.Resolve"/> also tries the bundle asset path and a sweep
        /// of loaded shader objects, and prints an inventory when all three miss.</para>
        ///
        /// <para>Called from the SLOW tick and from the config flip, never from the per-renderer
        /// paint: a miss is deliberately not cached by <see cref="BundleShaders"/> (a bundle can
        /// load later than the first lookup), so calling it per renderer would put a
        /// <c>Shader.Find</c> and a walk of every loaded AssetBundle into the frame once per water
        /// quad. Four attempts a second is enough to pick a bundle up the moment it arrives.</para>
        /// </summary>
        private Shader? MaintainPaintShader()
        {
            if (_paintShader != null)
                return _paintShader;
            if (!WantDebugPaint)
                return null;
            for (int i = 0; i < PaintShaderCandidates.Length; i++)
            {
                string name = PaintShaderCandidates[i];
                Shader? sh = BundleShaders.Resolve(
                    name, Name,
                    "[Water] DebugPaint can now paint the tracked water renderers a flat unlit "
                    + "colour, which is what answers whether this driver owns the surfaces in "
                    + "spiegeltiles.jpg at all.",
                    "[Water] DebugPaint falls back to writing the paint colour into the GAME "
                    + "shader's own colour properties, which is a weaker instrument — a shader "
                    + "term that ignores those colours is exactly what is under suspicion, so a "
                    + "surface that stays pale under the fallback proves nothing.");
                if (sh == null)
                    continue;
                _paintShader = sh;
                _paintHow = name;
                return sh;
            }
            _paintHow = "NO BUNDLED UNLIT SHADER REACHED — painting through the game's own shader "
                        + "colour properties instead, which cannot answer the question on its own";
            return null;
        }

        // ---- ROUND FIVE: THE MOD'S OWN FILM ----------------------------------------------------

        /// <summary>
        /// Re-base ONE film material instance onto the mod's own shader, in the tileset's own
        /// colour.
        ///
        /// <para>WHY THE WHOLE MATERIAL AND NOT ANOTHER PROPERTY. Two facts from hardware close the
        /// question between them. First, <c>[Water] DebugPaint</c> was finally run and the user's
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
        /// draws on the mod's own water shader, which reproduces what the census measured as the
        /// whole of the game water's motion — the tileset's own <c>_Normal_Map</c> sampled twice at
        /// its own two <c>_NormalTilings</c>, scrolled at its own two <c>_WaterUVAnimSpeed</c>
        /// rates — and shades it against a FIXED light direction. Every value below is read off the
        /// game's SHARED material here and handed straight over; nothing is a look chosen in this
        /// file.</para>
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

            bool haveA = shared.HasProperty(SpeedAId);
            bool haveB = shared.HasProperty(SpeedBId);
            bool haveNoise = shared.HasProperty(NoiseSpeedId);
            Vector4 speedA = haveA ? shared.GetVector(SpeedAId) : WaterOwnSurface.AuthoredSpeedA;
            Vector4 speedB = haveB ? shared.GetVector(SpeedBId) : WaterOwnSurface.AuthoredSpeedB;
            Vector4 noise = haveNoise
                ? shared.GetVector(NoiseSpeedId)
                : WaterOwnSurface.AuthoredNoiseSpeed;

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
            Vector4 rateA = WaterOwnSurface.ScrollRate(speedA, noise, dial);
            Vector4 rateB = WaterOwnSurface.ScrollRate(speedB, noise, dial);
            float strength = WaterOwnSurface.NormalStrength(strVec);

            // THE TILINGS ARE RESOLVED, NOT PASSED THROUGH. The authored (0.14, 6.00) is 43:1
            // anisotropy — a band 7.1 m long and 17 cm wide, which is what "weiße Streifen" IS —
            // and the two layers used to be summed at equal weight, so the fine one competed with
            // the coarse one instead of riding it. See WaterOwnSurface.TameTilings/LayerWeights.
            Vector4 resolved = WaterOwnSurface.TameTilings(tilings, waveScale);
            Vector4 weights = WaterOwnSurface.LayerWeights(resolved);

            // THE SWELL, in world units, off THIS quad's own width — so the same dial gives the
            // same-looking wave in a diorama at another scale. The mesh that carries it was
            // handed over by MaintainFilmMesh before this ran; if that refused, the amplitude
            // below still writes and a 4-vertex quad simply has nothing to bend, which is the flat
            // film and never a broken one.
            float amp = WaterOwnSurface.SwellAmplitude(o.FilmWidthWU, WantedSwellHeight);
            float swellWave = WaterOwnSurface.SwellWavelength * waveScale;
            float swellSpeed = WaterOwnSurface.SwellSpeed * waveScale * Mathf.Max(dial, 0f);

            inst.SetTexture(WaterOwnSurface.NormalMapProperty, bump);
            inst.SetVector(WaterOwnSurface.NormalTilingsProperty, resolved);
            inst.SetVector(WaterOwnSurface.LayerWeightsProperty, weights);
            inst.SetVector(WaterOwnSurface.ScrollAProperty, rateA);
            inst.SetVector(WaterOwnSurface.ScrollBProperty, rateB);
            inst.SetFloat(WaterOwnSurface.NormalStrengthProperty, strength);
            inst.SetFloat(WaterOwnSurface.ProcNormalProperty, bump != null ? 0f : 1f);
            inst.SetFloat(WaterOwnSurface.SmoothnessProperty, Mathf.Clamp01(smoothness));
            inst.SetFloat(WaterOwnSurface.ShimmerProperty, Mathf.Max(WantedShimmer, 0f));
            inst.SetVector(WaterOwnSurface.LightDirProperty, _lightLocal);
            inst.SetFloat(WaterOwnSurface.SwellAmpProperty, amp);
            inst.SetFloat(WaterOwnSurface.SwellWaveProperty, swellWave);
            inst.SetFloat(WaterOwnSurface.SwellSpeedProperty, swellSpeed);

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
                + ", [Water] WaveScale " + waveScale.ToString("0.###") + ")"
                + "; layer weights A " + weights.x.ToString("0.###")
                + " / B " + weights.y.ToString("0.###")
                + "; _WaterUVAnimSpeedA " + (haveA ? "READ " : "DEFAULTED ") + Fmt(speedA)
                + " -> " + rateA.x.ToString("0.###") + "," + rateA.y.ToString("0.###")
                + " WORLD UNITS/s"
                + "; _WaterUVAnimSpeedB " + (haveB ? "READ " : "DEFAULTED ") + Fmt(speedB)
                + " -> " + rateB.x.ToString("0.###") + "," + rateB.y.ToString("0.###")
                + " WORLD UNITS/s"
                + " (x _WaterNoiseSpeed.x " + (haveNoise ? "READ " : "DEFAULTED ")
                + noise.x.ToString("0.###")
                + " x [Water] RippleSpeed " + dial.ToString("0.###") + ")"
                + "; SWELL amplitude " + amp.ToString("0.####") + " world units (quad width "
                + o.FilmWidthWU.ToString("0.###") + " x [Water] SwellHeight "
                + WantedSwellHeight.ToString("0.###") + ", ceiling "
                + WaterOwnSurface.MaxSwellAmplitude.ToString("0.###") + " against the "
                + WaterOwnSurface.FilmToBedGap.ToString("0.##") + " gap to the basin bed)"
                + ", wavelength " + swellWave.ToString("0.##") + " world units at "
                + swellSpeed.ToString("0.###") + " world units/s = one crest every "
                + (swellSpeed > 0.0001f ? (swellWave / swellSpeed).ToString("0.#") : "inf") + " s"
                + "; _DetailOpacityBaseNormalStr " + (haveStr ? "READ " : "DEFAULTED ")
                + Fmt(strVec) + " -> ripple strength " + strength.ToString("0.###")
                + "; _Smoothness " + (haveSmooth ? "READ " : "DEFAULTED ")
                + smoothness.ToString("0.###") + " -> glint exponent "
                + Mathf.Lerp(1f, 8f, Mathf.Clamp01(smoothness)).ToString("0.#")
                + "; [Water] Shimmer " + WantedShimmer.ToString("0.###");
        }

        private static string Fmt(Vector4 v) =>
            "(" + v.x.ToString("0.###") + "," + v.y.ToString("0.###") + ","
            + v.z.ToString("0.###") + "," + v.w.ToString("0.###") + ")";

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
                "[Water] OwnSurface can now replace the game's water film with the mod's own "
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
                    "[Water] OwnSurface runs on the FALLBACK film — a flat, still, translucent "
                    + "sheet in the tileset's own colour. It removes the head-bound reflection and "
                    + "the pale sheet, and it does not animate.",
                    "[Water] OwnSurface CANNOT RUN AT ALL: neither the mod's water shader nor its "
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
            float smoothnessCap = EffectiveSmoothnessCap;
            float reflectivityCap = EffectiveReflectivityCap;

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
        /// The water FILM's own treatments: the tint alpha, the depth-fade dial, and the
        /// EDGE / FOAM / BORDER BAND — which is where round four says the visible pixels actually
        /// come from.
        ///
        /// <para>THE READING THAT CHANGED THIS METHOD. <c>spiegeltiles.jpg</c> shows a near-WHITE
        /// sheet; <c>_Color_Tint</c> is dark green RGBA(0.195, 0.311, 0.131). No alpha on a dark
        /// green body makes a pale sheet, so the body is not what is on screen and every round that
        /// tuned it was tuning something the user was not looking at. What IS pale on this material
        /// is <c>_Edge_Colour</c> (0.887, 0.887, 0.887, 0.867) and <c>_WaterBorderCol</c>
        /// (0.670, 0.617, 0.528, 0.561), and with the head camera writing no
        /// <c>_CameraDepthTexture</c> the depth term that would normally confine them to a
        /// shoreline is a constant across the whole quad.</para>
        ///
        /// <para>SO THE BAND IS COLLAPSED BY NUMBER, NOT BY KEYWORD. ModBuild 160 cleared
        /// <c>_EDGECOLOUR_TOGGLE_ON</c> and repainted <c>_Edge_Colour</c>, and the sheet stayed
        /// white. A keyword clear cannot be verified from a log — only the call can be reported —
        /// and, decisively, <c>_WaterBorderWidth</c>/<c>_WaterBorderCol</c> is a SECOND border
        /// mechanism with no keyword at all, which that clear could never have reached. Every band
        /// WIDTH now goes to zero and every band COLOUR to the body hue at alpha zero
        /// (<see cref="WaterEdgeBand"/>, which also holds the invariant that neither write can ever
        /// raise what the tileset authored). A band of width zero covers no pixels at EITHER
        /// extreme of a pinned depth fade, so this does not depend on a sign nobody here can
        /// read.</para>
        /// </summary>
        /// <param name="report">When non-null, every band property is appended with its authored
        /// value and what was written — or a loud line naming it as ABSENT from this shader.</param>
        private void ApplyWaterFilm(
            Material shared, Material inst, System.Text.StringBuilder? report)
        {
            bool bodyOnly = WantBodyOnly;

            Color tint = shared.HasProperty(ColorTintId) ? shared.GetColor(ColorTintId) : Color.white;
            // Hue as authored, alpha capped so the tiles read through.
            tint.a = Mathf.Min(tint.a, WantedOpacity);
            if (shared.HasProperty(ColorTintId))
                inst.SetColor(ColorTintId, tint);

            ApplyDepthFadeDial(shared, inst, report);

            // ShoreFoam asks for the depth texture so the AUTHORED band can work per eye. BodyOnly
            // overrides it: its entire purpose is that nothing but the body can be contributing.
            if (_depthGranted && !bodyOnly)
            {
                // The depth texture is actually there — hand the authored band back, keyword and
                // all. Read from the shared material so this is exact.
                RestoreAuthoredBand(shared, inst, report);
                return;
            }

            CollapseBand(shared, inst, tint, report);
        }

        /// <summary>Hand the whole authored band back, every property this driver can collapse.
        /// Reached only while a real <c>_CameraDepthTexture</c> exists, i.e. while the band would
        /// actually be confined to a shoreline instead of covering the quad.</summary>
        private void RestoreAuthoredBand(
            Material shared, Material inst, System.Text.StringBuilder? report)
        {
            report?.Append("depth texture GRANTED, so the AUTHORED band is handed back: ");
            for (int i = 0; i < BandWidthIds.Length; i++)
            {
                if (!shared.HasProperty(BandWidthIds[i]))
                    continue;
                inst.SetFloat(BandWidthIds[i], shared.GetFloat(BandWidthIds[i]));
                report?.Append(WaterEdgeBand.BandWidthProperties[i]).Append(" restored to ")
                       .Append(shared.GetFloat(BandWidthIds[i]).ToString("0.###")).Append("; ");
            }
            for (int i = 0; i < BandColourIds.Length; i++)
            {
                if (!shared.HasProperty(BandColourIds[i]))
                    continue;
                inst.SetColor(BandColourIds[i], shared.GetColor(BandColourIds[i]));
                report?.Append(WaterEdgeBand.BandColourProperties[i]).Append(" restored; ");
            }
            if (shared.HasProperty(EdgeToggleId))
                inst.SetFloat(EdgeToggleId, shared.GetFloat(EdgeToggleId));
            if (shared.IsKeywordEnabled(EdgeColourKeyword))
                inst.EnableKeyword(EdgeColourKeyword);
            else
                inst.DisableKeyword(EdgeColourKeyword);
            report?.Append("keyword ").Append(EdgeColourKeyword).Append(" set to the authored ")
                   .Append(shared.IsKeywordEnabled(EdgeColourKeyword) ? "ON" : "OFF");
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

        /// <summary>Write <c>_InvertDepthFade</c> per <c>[Water] DepthFade</c>. Always written,
        /// including in <see cref="WaterDepthFadeMode.Authored"/>, so winding the dial back
        /// restores the surface exactly instead of leaving the last setting standing.</summary>
        private void ApplyDepthFadeDial(
            Material shared, Material inst, System.Text.StringBuilder? report)
        {
            if (!shared.HasProperty(InvertDepthFadeId))
            {
                report?.Append(WaterEdgeBand.InvertDepthFadeProperty)
                       .Append(" ABSENT FROM THIS SHADER — [Water] DepthFade has nothing to write "
                               + "to here; ");
                return;
            }
            float authored = shared.GetFloat(InvertDepthFadeId);
            if (WaterEdgeBand.TryResolveDepthFade(
                    WantedDepthFade, authored, out float v, out string why))
            {
                inst.SetFloat(InvertDepthFadeId, v);
            }
            report?.Append(WaterEdgeBand.InvertDepthFadeProperty).Append(' ').Append(why)
                   .Append("; ");
        }

        // ---- THE FILM'S MESH -------------------------------------------------------------------

        /// <summary>
        /// Make sure this film is carrying a mesh the swell can actually wave, and remember the
        /// one it had.
        ///
        /// <para>WHY A MESH SWAP AT ALL. <c>GloomhavenVR/WaterVR</c> displaces vertices vertically
        /// so the pool has real relief — the user's ruling after ModBuild 163 was <i>"Nicht nur
        /// 'calm' sondern auch wirklich 3D wellen einbauen. Aktuell waren es nur weiße streifen auf
        /// einer flachen Oberfläche"</i> — and <c>TERRAIN_Water_Plane</c> is a flat plane with a
        /// handful of vertices. A vertex program cannot make a wave out of four corners, so the
        /// geometry has to arrive from somewhere, and midpoint subdivision of the game's OWN mesh
        /// is the only source that cannot get the pool's outline wrong. See
        /// <see cref="WaterSwellMesh"/> for the culling bill and how it is paid exactly.</para>
        ///
        /// <para>THE WIDTH IS MEASURED HERE AND NOWHERE ELSE, while the authored mesh is still on
        /// the renderer: <see cref="WaterSwellMesh"/> pads the bounds it hands back, so measuring
        /// off our own mesh would inflate the quad's width — and the amplitude is a fraction of
        /// that width, so the wave would grow a little every time it was re-measured.</para>
        /// </summary>
        private void MaintainFilmMesh(Renderer r, Owned o)
        {
            try
            {
                MeshFilter? mf = o.Filter;
                if (mf == null)
                {
                    mf = r.GetComponent<MeshFilter>();
                    if (mf == null)
                    {
                        _swellNote = "'" + r.name + "' has no MeshFilter (a SkinnedMeshRenderer or "
                                     + "a procedural quad) — it keeps its own geometry and stays "
                                     + "flat; nothing else about the film changes";
                        return;
                    }
                    o.Filter = mf;
                }

                Mesh? live = mf.sharedMesh;
                if (live == null)
                    return;
                if (o.SwellMesh != null && live == o.SwellMesh)
                    return; // already ours, and the cache guarantees it is the right one

                // Either the first pass over this renderer, or the game put its own mesh back.
                // Both mean: record what is there NOW as the mesh we owe back.
                o.AuthoredMesh = live;
                Bounds wb = r.bounds;
                o.FilmWidthWU = Mathf.Max(wb.size.x, wb.size.z);

                Vector3 s = r.transform.lossyScale;
                float minScale = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));

                Mesh? swell = WaterSwellMesh.Build(live, o.FilmWidthWU, minScale, out string note);
                _swellNote = note;
                if (swell == null)
                {
                    o.SwellMesh = null;
                    return;
                }
                mf.sharedMesh = swell;
                o.SwellMesh = swell;
            }
            catch (Exception e)
            {
                _swellNote = "handling '" + r.name + "'s mesh threw " + e.GetType().Name
                             + " — the film keeps its own geometry and stays flat";
            }
        }

        /// <summary>Give the film its authored mesh back. Idempotent, and safe on a renderer that
        /// was never swapped — every path that stops wanting the swell calls it, so the one thing
        /// it must never do is throw.</summary>
        private static void RestoreFilmMesh(Owned o)
        {
            if (o.SwellMesh == null)
                return;
            try
            {
                if (o.Filter != null && o.AuthoredMesh != null
                    && o.Filter.sharedMesh == o.SwellMesh)
                {
                    o.Filter.sharedMesh = o.AuthoredMesh;
                }
            }
            catch { /* the renderer went away between the null check and here */ }
            o.SwellMesh = null;
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
            // The mesh goes back whether or not the materials do: `restore: false` means the
            // renderer is gone or the game already replaced our materials, and in the second case
            // the filter is still live and still holding a mesh of ours.
            RestoreFilmMesh(o);
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
            // The read-back sample is an index into a set that no longer exists. Dropping it here
            // rather than letting FilmSample() notice keeps "no film tracked" meaning exactly that.
            _censusSample = null;
            DestroyProbes();
            // Every film has had its authored mesh handed back by ReleaseAt above, so the built
            // meshes have no owner left. A mod-owned mesh must no more outlive its owner than a
            // mod-owned material does.
            WaterSwellMesh.Clear();
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

            // The EFFECTIVE ceilings, not the dial positions: [Water] BodyOnly drives both to zero
            // and a line that printed the dial would misreport what the shader was actually given.
            float smoothnessInForce = EffectiveSmoothnessCap;
            sb.Append(" | RETUNE: sharpness ceiling ")
              .Append(smoothnessInForce.ToString("0.###"))
              .Append(" (dial ").Append(_appliedSmoothness.ToString("0.###"))
              .Append("; the film authors _Smoothness 0.754; a roughness property is floored at ")
              .Append((1f - smoothnessInForce).ToString("0.###"))
              .Append(" instead), metal/reflection ceiling ")
              .Append(EffectiveReflectivityCap.ToString("0.###"))
              .Append(" (dial ").Append(_appliedReflectivity.ToString("0.###")).Append(')')
              .Append(", _Color_Tint.a capped at ").Append(_appliedOpacity.ToString("0.###"))
              .Append(" (authored 0.737), basin surfaces ")
              .Append(_appliedBasin ? "IN SCOPE" : "released ([Water] BasinSurfaces OFF)")
              .Append(", [Water] BodyOnly ")
              .Append(_appliedBodyOnly
                  ? "ON (every band term AND every gloss/metal scalar forced to zero — if the "
                    + "surface is still pale under this while DebugPaint proves we own the "
                    + "renderer, no property can reach the pale pixels)"
                  : "off")
              .Append(", edge/foam/border band ")
              .Append(_depthGranted && !_appliedBodyOnly
                  ? "AUTHORED (depth granted)"
                  : "COLLAPSED BY NUMBER — every width to 0 and every band colour to the body hue "
                    + "at alpha 0, _WaterBorderWidth/_WaterBorderCol included (a SECOND border "
                    + "mechanism with no keyword, which ModBuild 160's DisableKeyword could never "
                    + "have reached)")
              .Append(", [Water] DepthFade=").Append(_appliedDepthFade)
              .Append(", probe usage forced Off->BlendProbes on ").Append(_probeUsageForced)
              .Append(" renderer(s).");

            AppendOwnSurface(sb);
            AppendDebugPaint(sb);
            AppendReadBack(sb);

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

            sb.Append(" WHAT IS SETTLED, so the next round is decidable from this line alone. "
                      + "(1) THE WRITE LANDS. There is no property block left; every value goes on "
                      + "a per-renderer material instance and the BAND READ-BACK reads it back off "
                      + "that instance. 'instancing=True' on the WATER SURFACE line is the "
                      + "retroactive reason ModBuild 159's block never reached the shader. "
                      + "(2) THE RENDERERS ARE OURS — measured, not argued. The user ran [Water] "
                      + "DebugPaint on hardware: 'Die debug farbe funktioniert - alles färbt sich "
                      + "magenta wie gewollt.' Three rounds of ambiguity closed in one second, and "
                      + "'we are tuning objects he is not looking at' is dead. (3) NO PROPERTY OF "
                      + "THE GAME'S SHADER REACHES THE DEFECT. Every band width read back at 0, "
                      + "every band colour at alpha 0, the keyword list empty, _Smoothness 0.08, "
                      + "the metal/reflection ceiling 0, a flat local probe REACHING the surface — "
                      + "and 'Keine Änderungen bei der Wasser Problematik', plus 'Ich konnte aber "
                      + "mit den anderen Einstellungen die kopf-gebundene Reflektion nicht "
                      + "deaktivieren, egal was ich eingestellt hab.' Owned renderer + every "
                      + "reachable property neutral + the defect unchanged leaves exactly one "
                      + "explanation: a TEXTURE or a CONSTANT compiled into VFX/Water_Shd_Trans. "
                      + "THAT IS WHY [Water] OwnSurface IS ON BY DEFAULT and is the only mechanism "
                      + "in this build that is new — it deletes the game's shader from the film "
                      + "rather than addressing it. The OWN SURFACE block above is the one to "
                      + "read. WHAT IS STILL A DIAL RATHER THAN AN ANSWER: [Water] BasinSurfaces "
                      + "and [Water] LocalProbe are the A/Bs for the SWIMMING half of the report "
                      + "on the BASIN, which is opaque ground, is not replaced by OwnSurface and "
                      + "is a different complaint from the film's paleness — do not test them in "
                      + "the same toggle. [Water] BodyOnly, ShoreFoam and DepthFade only mean "
                      + "anything while OwnSurface is OFF; they address the shader OwnSurface "
                      + "removes.");

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
            sb.Append(" | OWN SURFACE: [Water] OwnSurface=").Append(WantOwnSurface);
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
                  .Append(". No vertex is displaced by any of this — the tileset's own "
                          + "_addSphericalWaves is 0, and Unity culls a renderer against its MESH's "
                          + "authored bounds, so displaced geometry vanishes as you approach and, "
                          + "under MultiPass, vanishes in ONE EYE FIRST. All the motion is in the "
                          + "fragment's normal.");
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
                      + "order. The user has now run [Water] DebugPaint on hardware — 'Die debug "
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
        /// What the diagnostic paint ACTUALLY reached, counted off the live instances.
        ///
        /// <para>This line is written the way it is because of a specific failure: ModBuild 160's
        /// census reported <c>UNDONE: 0 re-asserts</c>, which says only that our write was still
        /// attached to the renderer, and that read as confirmation for a whole round while the
        /// surface was unchanged. So nothing here reports an intention. The counts below come from
        /// comparing each instance's CURRENT shader against the flat shader we resolved — a
        /// renderer that failed to paint is counted as unpainted, and the difference between
        /// "tracked" and "painted" is visible in the same line.</para>
        /// </summary>
        private void AppendDebugPaint(System.Text.StringBuilder sb)
        {
            sb.Append(" | DEBUG PAINT: [Water] DebugPaint=").Append(_appliedDebugPaint);
            if (!_appliedDebugPaint)
            {
                sb.Append(" — the surfaces carry the ordinary retune. TURN IT ON FOR ONE SECOND if "
                          + "the pool still looks wrong: it is the only reading that says whether "
                          + "this driver owns the renderers in spiegeltiles.jpg at all, and three "
                          + "hardware rounds have now ended without that answer.");
                return;
            }

            int filmTracked = 0, basinTracked = 0, filmPainted = 0, basinPainted = 0;
            for (int i = 0; i < _tracked.Count; i++)
            {
                Renderer r = _tracked[i];
                if (r == null || !_owned.TryGetValue(r, out Owned o))
                    continue;
                if (o.IsFilm)
                    filmTracked++;
                else
                    basinTracked++;
                Material? inst = o.Instances.Length > 0 ? o.Instances[0] : null;
                bool painted = inst != null && _paintShader != null
                               && ReferenceEquals(inst.shader, _paintShader);
                if (!painted)
                    continue;
                if (o.IsFilm)
                    filmPainted++;
                else
                    basinPainted++;
            }

            sb.Append(" — FILM painted flat MAGENTA on ").Append(filmPainted).Append(" of ")
              .Append(filmTracked).Append(" film renderer(s); BASIN painted flat CYAN on ")
              .Append(basinPainted).Append(" of ").Append(basinTracked)
              .Append(" basin renderer(s); shader borrowed = ").Append(_paintHow)
              .Append(". WHAT THE HEADSET ANSWERS IN ONE SECOND, and it is the reading three "
                      + "rounds have lacked: the pale hexes turn MAGENTA => this driver owns the "
                      + "water FILM and the only open question is which of its properties paints "
                      + "them pale; they turn CYAN => the basin bed is the surface in "
                      + "spiegeltiles.jpg and the film was never the subject; they stay PALE AND "
                      + "WHITE => we have been tuning objects that are not what is being looked "
                      + "at, and the FLOOR CENSUS in this same log names what actually is. A count "
                      + "of 0 painted against a non-zero tracked count means the paint itself "
                      + "failed and NONE of those three readings applies.");
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
            if (_appliedDebugPaint)
            {
                sb.Append("suppressed — [Water] DebugPaint has replaced the shader on this "
                          + "instance, so the band properties are not what it draws with.");
                return;
            }
            if (OwnSurfaceActive)
            {
                sb.Append("suppressed — [Water] OwnSurface has replaced this instance's whole "
                          + "material, so VFX/Water_Shd_Trans' band properties are not what it "
                          + "draws with and printing them would report on a shader that is "
                          + "deliberately gone. The OWN SURFACE block above is the read-back that "
                          + "applies now. Everything this block used to say is settled: every band "
                          + "width read back at 0 and every band colour at alpha 0 on ModBuild "
                          + "161, the user's verdict was 'Keine Änderungen bei der Wasser "
                          + "Problematik', and [Water] DebugPaint has since confirmed on hardware "
                          + "that the renderers are ours.");
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
                AppendFloatReadBack(
                    sb, shared, inst, WaterEdgeBand.InvertDepthFadeProperty, InvertDepthFadeId);
                sb.Append("keyword ").Append(EdgeColourKeyword).Append(" authored ")
                  .Append(shared.IsKeywordEnabled(EdgeColourKeyword) ? "ON" : "OFF")
                  .Append(" -> instance ")
                  .Append(inst.IsKeywordEnabled(EdgeColourKeyword) ? "STILL ON" : "OFF")
                  .Append("; instance keywords=[").Append(string.Join(",", inst.shaderKeywords))
                  .Append("]. THE READING THAT MATTERS, and it has already been taken: ModBuild "
                          + "161 read every one of these back at 0 / alpha 0 with an empty keyword "
                          + "list, [Water] DebugPaint has since proved on hardware that these "
                          + "renderers are ours, and the user's verdict was still 'Keine "
                          + "Änderungen bei der Wasser Problematik'. So the pale sheet and the "
                          + "head-bound reflection come from a texture or a constant compiled into "
                          + "VFX/Water_Shd_Trans and no shader property reaches them. This block "
                          + "is now only running because [Water] OwnSurface is OFF or its shader "
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
                    else if (WantDebugPaint)
                        sb.Append("suppressed — [Water] DebugPaint owns this instance's shader");
                    else if (OwnSurfaceActive)
                        sb.Append("suppressed — [Water] OwnSurface has replaced this material "
                                  + "outright; the band belongs to a shader this instance no "
                                  + "longer runs");
                    else
                        ApplyWaterFilm(mat, inst, sb);
                }
                catch (Exception e)
                {
                    sb.Append("unreadable: ").Append(e.GetType().Name);
                }

                // THE ONE TERM OF OUR OWN SHADER THAT COMES FROM THE MESH RATHER THAN FROM US.
                // GloomhavenVR/Overlay's fragment is tex2D(_MainTex,uv) * _Color * i.color, so a
                // quad that ships a COLOR channel multiplies our tint by it — and a dark or
                // zero-alpha vertex colour would make the film darker or invisible for a reason no
                // property of ours could explain. HasVertexAttribute answers that without touching
                // mesh.colors32, which would allocate and would log an error on a non-readable
                // mesh. Read once per material, in the capped heavy line.
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
                        // THE COUNTS AND THE BOUNDS, because the swell needs vertices to move and
                        // a four-corner quad has none to spare: a film that reads 4 verts here is
                        // a film whose mesh swap did not happen, which looks from inside the
                        // headset exactly like a displacement that shipped and did nothing.
                        // MESH SWAP says which, in words, on the same line.
                        bool hasColour = mesh.HasVertexAttribute(VertexAttribute.Color);
                        Bounds lb = mesh.bounds;
                        sb.Append('\'').Append(mesh.name).Append("' ")
                          .Append(mesh.vertexCount).Append(" verts / ")
                          .Append(mesh.triangles.Length / 3).Append(" tris, local bounds centre (")
                          .Append(lb.center.x.ToString("0.##")).Append(',')
                          .Append(lb.center.y.ToString("0.##")).Append(',')
                          .Append(lb.center.z.ToString("0.##")).Append(") size (")
                          .Append(lb.size.x.ToString("0.###")).Append(',')
                          .Append(lb.size.y.ToString("0.###")).Append(',')
                          .Append(lb.size.z.ToString("0.###")).Append(')')
                          .Append(" | MESH SWAP: ").Append(_swellNote)
                          .Append(" [built ").Append(WaterSwellMesh.MeshesBuilt)
                          .Append(" mesh(es), ").Append(WaterSwellMesh.VerticesBuilt)
                          .Append(" verts total] | vertexColour=")
                          .Append(hasColour)
                          .Append(hasColour
                              ? " — GloomhavenVR/Overlay MULTIPLIES its tint by the mesh's vertex "
                                + "colour, so if [Water] OwnSurface produces a film that is darker "
                                + "than the tint above, or invisible in places, this is where it "
                                + "comes from and no property of ours can correct it"
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
