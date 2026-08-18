using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core;

/// <summary>
/// THE MIRRORING FLOOR — user report 2026-08-15 (spiegeltiles.jpg): "In dem Level das wir
/// gespielt haben wurde die Bodentiles eines Raumes nicht richtig dargestellt. Stattdessen war
/// dort eine Fläche zu sehen die spiegelt und sie mit den Kopfbewegungen ändert."
///
/// <para>WHAT THE SURFACE IS — measured, not inferred. The mod's own floor census names it
/// outright (<c>LogOutput.log:38041</c>, identical at <c>:35109 :35166 :37972</c>, and on both
/// peers):</para>
/// <code>
/// FLOOR CENSUS room 1 'Room_2'x4 center(5.2,8.9) floorY 0.00:
///   [FLOOR] 'TERRAIN_Crypt_Water_02_Edge' y[-0.2.. 0.0] sh='Amp_Basic_N_MRAO'  q2000;
///   [FLOOR] 'TERRAIN_Crypt_Water_02_Base' y[-0.3..-0.1] sh='Amp_Basic_N_MRAO'  q2000;
///   [FLOOR] 'TERRAIN_Water_Plane'         y[ 0.0.. 0.0] sh='VFX/Water_Shd_Trans' q2900; …
/// </code>
/// <para>It is the game's own WATER TERRAIN: one zero-thickness <c>TERRAIN_Water_Plane</c> quad
/// per water hex, running the game's transparent water shader <c>VFX/Water_Shd_Trans</c> at
/// renderQueue 2900, laid exactly on the floor plane over a <c>TERRAIN_Crypt_Water_02_Base</c>
/// basin and its <c>_Edge</c> rim. The room is a pool: the mod's water census climbs
/// 5 → 9 → 10 → 16 → 17 planes as the room reveals (<c>LogOutput.log:35020…35159</c>) and the
/// game spawns exactly 17 <c>TerrainWater</c> props in the same burst
/// (<c>Player.log:492738…493212</c>). Scenario: the Guildmaster mission "Zahltag", scene
/// ProcGen, tilesets PCG_Necropolis/PCG_Crypt, mod environment style SwampNight.</para>
///
/// <para>WHAT IT IS NOT, and this is what the search cost. Over the whole 81 MB Player.log of
/// that session there is <b>no</b> <c>ReflectionProbe</c>, <b>no</b> cubemap, <b>no</b>
/// <c>GrabPass</c> and <b>no</b> planar-reflection RenderTexture — zero occurrences of each. So
/// the "spiegelt" reading is not a stale probe, not a reflection camera pointed at the parked
/// flat-screen viewpoint and not a screen grab: it is the water shader's own view-dependent
/// shading. The runner-up, the fog-of-war hex <c>Amp_Basic_Unseen</c>, is ruled out by the same
/// log — that room's preview node is dead at the time (<c>LogOutput.log:38050</c>,
/// <c>child[0] 'Preview' self=OFF hier=OFF … 0 drawing</c>). And the mod has never written to
/// these renderers: the census entry carries neither the <c>OUR-MPB</c> nor the <c>OFF</c> flag.
/// This is the game rendering its own water, untouched.</para>
///
/// <para>WHY IT LOOKS WRONG IN VR AND RIGHT ON A FLAT SCREEN. The flat game only ever views this
/// pool from one camera pitch — a fixed steep top-down. In VR the same surface is seen across
/// the table at grazing angles, from a head that moves, at a world scale where the board is
/// metres wide. Every view-dependent term in a water shader (fresnel, specular, whatever ripple
/// normal it drives) is authored against that single flat-game viewpoint, and none of it is
/// reachable from outside the shader: the shader ships compiled inside
/// <c>always_loaded_base_high</c> (<c>Player.log:1621</c> lists <c>Water_Shd.shader</c> /
/// <c>Water_Shd_Trans.shader</c> in the bundle's dependency list). That is the same wall the
/// masonry analysis hit in <c>.planning/wall-fade-stereo-rivalry.md</c>: a game shader whose
/// view coupling cannot be corrected without replacing the shader, which means a bundle.</para>
///
/// <para>SO THIS IS A DELIBERATE FALLBACK, NOT A FIX OF THE SHADING, and it is stated as one.
/// A per-eye-correct water surface is not available from mod code at all here, so the choice is
/// between "a milky sheet where the floor should be" and "no water film". This driver takes the
/// second: it hides the <c>TERRAIN_Water_Plane</c> quads and leaves everything else standing.
/// <b>WHAT THAT COSTS, exactly:</b> the pool loses its water film — no sheen, no ripple, no
/// translucency. What remains is what the same census line shows underneath it: the sunken
/// <c>TERRAIN_Crypt_Water_02_Base</c> basin and its <c>_Edge</c> rim, authored water-terrain art
/// in the room's own tileset, so the hexes still read as a water basin and the game's own hex
/// overlay still marks them as water terrain. <b>What it buys:</b> the floor tiles under the
/// pool become visible again, which is what the report asked for, and nothing on that surface
/// moves with the head any more. Live-reversible from the VR menu
/// (<see cref="WaterConfig.HideTerrainWater"/> → off restores every plane bit-for-bit in the
/// same tick), so the user can compare both and rule.</para>
///
/// <para>THE INSTRUMENT THAT MAKES THE REAL FIX POSSIBLE NEXT ROUND. Once per distinct water
/// material the driver prints a <c>WATER SURFACE</c> line with everything a decision needs and
/// none of it was available offline: every shader property with its name, type and CURRENT VALUE
/// (textures resolved to their asset name or <c>&lt;null&gt;</c>), the material's keywords and
/// render queue, the renderer's reflection-probe usage, the whole reflection environment
/// (skybox, ambient mode/intensity, default/custom reflection, realtime-probe quality setting,
/// live probe count) and — the question the report asks by name — WHICH CAMERA feeds any
/// screen-space input: <c>Camera.main</c> with its position (parked at (0.00, 14.17, 0.00) in
/// this session, <c>LogOutput.log:31576</c>), the head camera's <c>depthTextureMode</c>, and
/// whether the game's <c>RFX4_DistortionAndBloom</c> is alive — that component republishes the
/// GLOBAL <c>_GrabTexture</c> / <c>_CameraDepthTexture</c> every LateUpdate from a quarter-res
/// re-render of <c>Camera.current ?? Camera.main</c> (decompiled
/// <c>GH.Runtime/RFX4_DistortionAndBloom.cs:145-152, 246-269</c>), which under VR is the parked
/// camera. If the next log shows that component alive AND a depth/grab property on this
/// material, the honest fix moves from "hide" to "feed it the head camera", and this line is
/// what will say so.</para>
///
/// <para>WHAT WOULD DISPROVE THE READING ABOVE: a <c>WATER SURFACE</c> line showing the material
/// exposes a reflection/cubemap texture slot that is non-null and points at a probe or RT — then
/// the surface IS sampling a captured image and the parked-camera route is back in play, against
/// this session's zero probe/cubemap/grab hits. Equally, a line showing no view-dependent
/// property at all would mean the milkiness is lighting, not shading, and belongs to the
/// environment lane rather than here.</para>
///
/// <para>SCOPE. Only renderers on the game's water shader family (the <c>Water_Sh</c> stem —
/// the same rule <see cref="WallSegmentFade"/>'s fountain exemption keys on, so there is one
/// definition of "this is water" in the mod) AND named for the terrain prop family
/// (<see cref="TerrainWaterNameTokens"/>). The name term is what keeps DECORATIVE water out of
/// it: the fountain/pond props of the <c>brunnen.png</c> ruling (<c>FR_SW_Pond_Small</c>,
/// <c>P_Waterfall_Circle_Small</c>) are small set dressing nobody has to stand on, they are not
/// what the report is about, and they keep their water.</para>
///
/// <para>MULTIPLAYER: purely local rendering. One boolean per renderer, no material is written,
/// nothing crosses the wire, and each peer decides for itself — the report's three machines all
/// logged the same 17 planes, so all three hide the same 17. REVERSIBLE: the authored
/// <c>enabled</c> flag is snapshotted before the first write and restored on config change,
/// scene teardown and uninstall. TickGuard-safe.</para>
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

    private static Driver? _driver;

    /// <summary>Install the driver (idempotent). No-op when VR isn't running — on a flat screen
    /// the game's water is exactly what its authors saw, and nothing here applies.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        WaterConfig.Bind();
        var go = new GameObject(DriverName);
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<Driver>();
        VRLog.Info(Name,
            "WaterTerrainVR installed — the game's water TERRAIN quads "
            + "('TERRAIN_Water_Plane', shader family 'Water_Sh*') are surveyed on a slow "
            + "round-robin over the map-tile registry; see the WATER SURFACE census line for "
            + "what each one is and which camera feeds it.");
    }

    /// <summary>Drop the driver, restoring every plane's authored enabled flag first.</summary>
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
    /// automatically, so this switch is reachable in-headset without touching a file).</summary>
    internal static class WaterConfig
    {
        private static ConfigFile? _file;

        /// <summary>ON (default): hide the game's water TERRAIN quads in VR — the deliberate
        /// fallback of the 2026-08-15 spiegeltiles.jpg report, cost stated in the class header.
        /// OFF: the game's water renders untouched, mirror and all.</summary>
        internal static ConfigEntry<bool>? HideTerrainWater;

        internal static void Bind()
        {
            if (_file != null)
                return;
            ConfigFile config = _file = ModuleConfig.Create("water");
            HideTerrainWater = config.Bind("Water", "HideTerrainWaterInVR", true,
                "Hide the game's water TERRAIN quads (TERRAIN_Water_Plane, shader VFX/Water_Shd*) "
                + "while VR runs. ON is a DELIBERATE FALLBACK, not a fix of the shading: the "
                + "game's water shader is authored for one fixed top-down camera pitch and its "
                + "view-dependent terms cannot be corrected from mod code, so across a VR table "
                + "the pool reads as a pale sheet that moves with the head (report 2026-08-15, "
                + "spiegeltiles.jpg). ON costs the water film — sheen, ripple, translucency — and "
                + "leaves the sunken basin and rim the tileset draws underneath, so the hexes "
                + "still read as water. OFF restores the game's water immediately.");
        }
    }

    private sealed class Driver : MonoBehaviour
    {
        /// <summary>Survey cadence. Slow on purpose: a revealed pool has seconds of reveal
        /// animation ahead of it, and this must never become a per-frame scene sweep (the
        /// PERF S1 lesson — every full FindObjectsOfType cost 10-15 ms in a 3000-renderer
        /// room and was the measured cause of the hitches).</summary>
        private const float TickInterval = 0.25f;

        /// <summary>ONE map tile is examined per tick, round-robin over the registry. With the
        /// report's ~11 tiles that is a full pass every ~2.75 s at a cost of one
        /// GetComponentsInChildren on a single tile per 250 ms — bounded by construction, and
        /// it never grows into a scene sweep however big the map gets.</summary>
        private int _cursor;

        private float _next;

        /// <summary>Every plane we touched → its AUTHORED enabled flag, so a restore is
        /// bit-for-bit and not a guess. Dead keys are pruned on the sweep.</summary>
        private readonly Dictionary<Renderer, bool> _touched = new(32);

        private readonly List<ProceduralMapTile> _tileScratch = new(16);
        private readonly List<Renderer> _rendererScratch = new(64);
        private readonly List<Material> _matScratch = new(4);
        private readonly List<Renderer> _deadScratch = new(8);

        /// <summary>Materials already surveyed (one census line each, cap 3).</summary>
        private readonly HashSet<string> _censused = new();

        /// <summary>Last applied mode, so a live config flip restores/re-applies once.</summary>
        private bool _appliedHide;

        private Action? _tick;

        private void Awake()
        {
            _appliedHide = Want;
            _tick = Tick;
        }

        private static bool Want =>
            VRSession.IsRunning
            && WaterConfig.HideTerrainWater != null
            && WaterConfig.HideTerrainWater.Value;

        private void Update() => TickGuard.Run("Compat.WaterTerrainVR", _tick!, Name);

        private void Tick()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + TickInterval;

            bool want = Want;
            if (want != _appliedHide)
            {
                _appliedHide = want;
                if (!want)
                {
                    RestoreAll();
                    VRLog.Info(Name,
                        "WATER SURFACE: [Water] HideTerrainWaterInVR turned OFF — every water "
                        + "terrain quad restored to its authored state; the game's own water "
                        + "shader renders again exactly as on a flat screen.");
                    return;
                }
            }

            PruneDead();
            if (!want)
                return;

            SceneRegistry.MapTiles.Collect(_tileScratch);
            if (_tileScratch.Count == 0)
                return;
            if (_cursor >= _tileScratch.Count)
                _cursor = 0;
            ProceduralMapTile tile = _tileScratch[_cursor++];
            if (tile == null || !tile.gameObject.activeInHierarchy)
                return;

            _rendererScratch.Clear();
            tile.GetComponentsInChildren(includeInactive: false, _rendererScratch);
            int hidden = 0;
            foreach (Renderer r in _rendererScratch)
            {
                if (r == null || !IsTerrainWater(r, _matScratch))
                    continue;
                LogWaterSurfaceOnce(r);
                if (!_touched.ContainsKey(r))
                    _touched[r] = r.enabled;
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden++;
                }
            }
            _rendererScratch.Clear();
            if (hidden > 0)
            {
                VRLog.Info(Name,
                    $"WATER SURFACE: {hidden} water terrain quad(s) hidden on map tile "
                    + $"'{tile.name}' (deliberate VR fallback, user report 2026-08-15 "
                    + $"spiegeltiles.jpg — the game's water shader is authored for one fixed "
                    + $"top-down camera pitch and mirrors across a VR table; the tileset's own "
                    + $"sunken basin and rim stay, so the hexes still read as water). "
                    + $"{_touched.Count} quad(s) tracked in total; "
                    + $"[Water] HideTerrainWaterInVR = off restores them all.");
            }
        }

        /// <summary>Drop tracking entries whose renderer is gone (Apparance rebirths content
        /// constantly — a destroyed Unity object still hashes, so the key must be swept).</summary>
        private void PruneDead()
        {
            _deadScratch.Clear();
            foreach (KeyValuePair<Renderer, bool> kv in _touched)
            {
                if (kv.Key == null)
                    _deadScratch.Add(kv.Key!); // destroyed Unity object — the reference still hashes
            }
            foreach (Renderer dead in _deadScratch)
                _touched.Remove(dead);
            _deadScratch.Clear();
        }

        /// <summary>Restore every quad's authored enabled flag. Called on config flip, on
        /// uninstall and on destroy — a hidden renderer must never outlive its owner.</summary>
        internal void RestoreAll()
        {
            foreach (KeyValuePair<Renderer, bool> kv in _touched)
            {
                if (kv.Key != null)
                    kv.Key.enabled = kv.Value;
            }
            _touched.Clear();
        }

        private void OnDestroy()
        {
            try { RestoreAll(); }
            catch { /* teardown */ }
        }

        /// <summary>
        /// THE CENSUS the next hardware log is meant to settle this with — one line per distinct
        /// water material, cap 3. Everything in it was unavailable offline: the shader ships
        /// compiled in a game bundle, so its property list, the values on the live material and
        /// the reflection environment can only be read in-process.
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
                // (Rig/VRRigDriver.HeadCamera.cs owns that bit, [Perf] gated).
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
                // (decompiled GH.Runtime/RFX4_DistortionAndBloom.cs:145-152, 246-269). If it is
                // alive, EVERY VFX-family shader in the game reads a parked-camera screen grab.
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
