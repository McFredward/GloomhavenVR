using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// WHY THE TEXTURES LOOK SOFT UP CLOSE — the <c>[Perf] TEX</c> line.
///
/// <para>THE REPORT THIS WAS BUILT FOR (user, 2026-08-23, verbatim): <i>"Wenn man in VR nah ran
/// geht, sind die Texturen doch manchmal matschig, siehe matschige_texturen.jpg; wäre es eine
/// Option, mit Super Resolution die Texturen im Spiel zu erhöhen? Würde das sehr auf die Performance
/// gehen? Was wäre dafür notwendig?"</i> The photograph is a lava cavern seen from close range: the
/// rock walls carry almost no diffuse detail, the shading is broad and smeared, and the sharpest
/// thing in the frame is a small stone plaque with a glowing rune.</para>
///
/// <para>THE POINT OF THIS FILE IS THAT "MATSCHIG" IS AT LEAST FIVE DIFFERENT DEFECTS AND THEY HAVE
/// DIFFERENT FIXES. Naming them is the whole job, because four of the five are cheap or free and
/// the fifth (buying new texture detail) is a project, and nobody can tell which one they are
/// looking at from a photograph:</para>
/// <list type="number">
/// <item><b>A GLOBAL MIP DROP.</b> <see cref="QualitySettings.masterTextureLimit"/> discards the top
/// N mip levels of every mipped texture in the process, so a 1024² albedo renders as 512² at 1 and
/// 128² at 3. THE GAME WRITES THIS FIELD ITSELF and persists it (decompiled
/// <c>GH.Runtime Gloomhaven/GraphicProfile.Setup</c>: <c>QualitySettings.masterTextureLimit =
/// (int)TextureQuality</c>, from a <c>TextureQualityRender</c> enum of FULL/HALF/QUARTER/EIGHTHEN,
/// stored in <c>SaveData.Global.CustomQualityProfile</c> and re-applied at every boot by
/// <c>GraphicSettings.SetupQualityLevel</c>). Its deserialisation FALLBACK for an unrecognised value
/// is <c>EIGHTHEN</c> — literally one-eighth resolution — and <c>QualitySettings.SetQualityLevel</c>
/// re-loads the field from the level asset on every quality swap, which is exactly the mechanism
/// this mod already re-asserts MSAA and the pixel-light cap against. NOTHING in the mod has ever
/// read this field. It is the first thing on the line for that reason: if it is not 0, every other
/// hypothesis on this page is downstream of it and none of them matter yet.</item>
/// <item><b>ANISOTROPIC FILTERING OFF.</b> At grazing angles — most of a floor, most of a wall seen
/// by a standing player — trilinear without aniso smears along the direction of maximum compression.
/// The game ships this OFF: the mod's own boot line reads <c>"Anisotropic filtering forced globally
/// (was Disable; min 8, max 16)"</c> (Player.log, ModBuild 226). So this one is ALREADY FIXED by
/// <c>Rig/RenderQuality.ApplyAniso</c> — but "already fixed" is a claim about a state that a quality
/// swap can undo, and no line printed the state. Now one does, together with the per-texture
/// <c>anisoLevel</c> distribution and the note that <see cref="AnisotropicFiltering.ForceEnable"/>
/// overrides those per-texture values (so <c>anisoLevel=1</c> in this census is NOT evidence of a
/// fault).</item>
/// <item><b>TEXTURE STREAMING starving the mip chain.</b> If <c>streamingMipmapsActive</c> is on and
/// the budget is tight, Unity keeps a texture at a lower <c>loadedMipmapLevel</c> than it wants and
/// the surface is soft until it catches up — a defect that comes and goes with where the head is,
/// which fits "manchmal matschig" precisely. Printed, with the count of textures whose loaded level
/// is behind their desired one.</item>
/// <item><b>NOT ENOUGH SOURCE TEXELS.</b> A 512² diffuse tiled once across a rock face that fills
/// half a 3072x3264 eye is MAGNIFIED — the GPU is asked for more detail than exists in the file, and
/// no filter, mip bias, MSAA or supersample setting can invent it. This is the case the user's
/// "Super Resolution" question is really about, and it is the ONLY case where new texture data is
/// the answer. It is also the one case that no state probe can see, which is why this instrument
/// computes TEXELS PER RENDERED PIXEL and not just a list of sizes.</item>
/// <item><b>NOT A TEXTURE AT ALL.</b> Simple silhouettes and broad flat shading are geometry and
/// lighting. A parallel lane this round is investigating the game's 2,560 <c>AutomaticLOD</c>
/// components and whether VR resolves them against the wrong camera, which would pin every object
/// at its lowest mesh LOD permanently and would look very much like this photograph. This line
/// deliberately does NOT guess at that; it reports how much of the softness the TEXTURE side can
/// account for, so the two lanes' evidence can be subtracted from each other instead of argued.</item>
/// </list>
///
/// <para>TEXELS PER RENDERED PIXEL IS THE MEASUREMENT THAT DECIDES, and this project has been here
/// before. <see cref="WorldUI.PanelSamplingProbe"/> settled the window-flicker rounds (ModBuild
/// 189–193) on exactly this number for uGUI graphics: above 1 the surface is MINIFIED and is
/// throwing texels away (fixable with mips, aniso, a bake or a bias); at or below 1 it is MAGNIFIED
/// and is stable under every filter — soft, and soft is all it can ever be. The same arithmetic is
/// applied here to world-space renderers, through the same eye-target figures
/// (<c>XRSettings.eyeTexture*</c> x <c>renderViewportScale</c>) so the two lines are directly
/// comparable, and with the same convention that a number is quoted with its assumption attached.</para>
///
/// <para>THE ASSUMPTION THIS LINE MAKES, STATED ONCE SO IT IS NEVER MISTAKEN FOR A MEASUREMENT.
/// Texels-across is taken as <c>texture.width x material texture SCALE</c> over the renderer's
/// LARGEST world-bounds dimension. That is the UV layout a wall/floor/prop kit almost always has,
/// and the tiling factor is read from the material rather than assumed — but it is still a MODEL,
/// not a UV-area integral, and a mesh whose UVs pack several surfaces into one chart will read
/// softer than it is. Reading the real UV area would mean <c>mesh.uv</c> + <c>mesh.triangles</c> on
/// meshes that are usually not <c>isReadable</c>, per renderer, per window — the cost of which is
/// the reason this instrument exists at all. The RAW numbers are therefore printed beside every
/// ratio ("512x512 albedo on a surface spanning 1,700 px"), because that pair is decisive on its own
/// even if the ratio is off by a factor of two.</para>
///
/// <para>IT RIDES THE WALK IT DOES NOT PAY FOR. Every renderer and every material slot this needs is
/// already in <see cref="PerfSceneProfile"/>'s hand during the SCENE walk, so this class adds no
/// <c>FindObjectsOfType</c> of its own: <see cref="Begin"/> caches the eye/projection terms,
/// <see cref="PixelSpan"/> is called once per SUBMITTED renderer (one <c>Renderer.bounds</c> read
/// plus arithmetic) and <see cref="Offer"/> only touches a material when that renderer is big enough
/// in the eye to be part of the complaint. Everything is rationed by the SCENE line's own
/// self-timing, and the population is capped by <see cref="MaxSurfaces"/> and
/// <see cref="MaxMaterialProbes"/> so a pathological scene cannot turn the instrument into the
/// problem.</para>
///
/// <para>REJECTED — a per-frame probe (this is a scene property, not a frame property, and a probe
/// that answered is spent); reading back the rendered image to measure blur (the eye texture is not
/// readable and the measurement would not separate texture softness from mesh LOD from lighting);
/// walking every texture in the process with <c>Resources.FindObjectsOfTypeAll</c> (that reports the
/// LOADED set, including every menu atlas and every asset nobody is looking at, which is precisely
/// the number that would make an offline-upscale estimate wrong by an order of magnitude).</para>
///
/// <para>MULTIPLAYER / REVERSIBILITY. Reads state, writes one log line. It never touches a game
/// object, a material, a texture, game state or wire traffic. It holds Texture/Material references
/// only between <see cref="Begin"/> and <see cref="Append"/> inside a single window, and both ends
/// clear them.</para>
/// </summary>
internal static class PerfTextureCensus
{
    private const string Scope = "Perf";

    /// <summary>
    /// Pixel span, in the eye's own pixels, below which a renderer is not part of this complaint.
    /// The report is about walking UP TO a wall, so the population that matters is the one that
    /// fills a real part of the view; 120 px of a 3264-px eye is ~3.7 % of its height, which still
    /// admits props and small rocks while excluding the thousands of distant tiles that would
    /// otherwise dominate every histogram and hide the answer.
    /// </summary>
    private const float MinPixelSpan = 120f;

    /// <summary>Distinct textures tracked per window. 155 distinct MATERIALS were submitted in the
    /// ModBuild 226 all-rooms-open capture, so a few hundred textures is the whole real population
    /// and this cap is a runaway guard, not a sampling policy.</summary>
    private const int MaxSurfaces = 512;

    /// <summary>Hard ceiling on material slots examined per window (see the class doc's cost note).</summary>
    private const int MaxMaterialProbes = 6000;

    /// <summary>How many of the SOFTEST surfaces are named individually, with every texture their
    /// material binds. These are the ones the photograph is of.</summary>
    private const int TopSoftest = 8;

    /// <summary>Albedo property names, in the order the game's own shader kit uses them. Cached as
    /// ids: <c>Material.HasProperty(string)</c> hashes on every call, and this runs thousands of
    /// times a window. <c>_Alb</c> first — that is the Amplify kit's name and
    /// <c>Amp_Basic_N_MRAO</c> alone is 2,374 of the 3,000 submitted material slots (ModBuild 226
    /// [Perf] SCENE); <c>_MainTex</c> second (the mainTexture fallback the map-capture path already
    /// documents); <c>_BaseMap</c> last, for anything authored against an SRP-shaped shader.</summary>
    private static readonly int[] AlbedoProps =
    {
        Shader.PropertyToID("_Alb"),
        Shader.PropertyToID("_MainTex"),
        Shader.PropertyToID("_BaseMap"),
    };

    /// <summary>Human names for <see cref="AlbedoProps"/>, same order — only read when a surface is
    /// actually printed.</summary>
    private static readonly string[] AlbedoPropNames = { "_Alb", "_MainTex", "_BaseMap" };

    /// <summary>One distinct texture and the largest surface it was seen on this window.</summary>
    private sealed class Surf
    {
        public Texture? Tex;
        public Material? Mat;
        public string? RendererName;
        public int Slots;           // how many submitted material slots bound this texture
        public float PixelSpan;     // eye pixels across the biggest renderer that used it
        public float Texels;        // modelled texels across that same span
        public float TexelsPerPixel;
        public float TilingX;
        public int PropIndex;       // which of AlbedoProps carried it
    }

    private static readonly List<Surf> Surfaces = new(MaxSurfaces);
    private static readonly Dictionary<int, int> SurfaceIndex = new(MaxSurfaces);
    private static readonly List<Surf> Ranked = new(MaxSurfaces);
    private static readonly List<float> RatioScratch = new(MaxSurfaces);

    /// <summary>Pixels of eye height per (world size / world distance). See <see cref="Begin"/>.</summary>
    private static float _pixelsPerUnitRatio;

    private static float _eyePxW;
    private static float _eyePxH;
    private static Vector3 _headPos;
    private static bool _armed;
    private static int _materialProbes;
    private static int _renderersConsidered;
    private static int _renderersInPopulation;
    private static double _costMs;

    // ==========================================================================================
    //  collection — driven from PerfSceneProfile's SCENE walk
    // ==========================================================================================

    /// <summary>
    /// Arm the census for one window: cache the head pose, the per-eye render target and the
    /// projection term, and drop every reference the previous window held.
    ///
    /// <para>THE PROJECTION TERM. For a world size <c>s</c> at world distance <c>d</c>, the vertical
    /// NDC span is <c>m11 * s / d</c> and the pixel span is half of that times the eye height — so
    /// <c>pixels = s / d * (0.5 * eyeHeight * m11)</c>, and the bracket is a constant for the whole
    /// window. <b>This is scale-invariant</b>, which matters here more than anywhere: the rig is
    /// scaled ~198x (198 world units to the metre) and <c>s</c> and <c>d</c> are BOTH world units,
    /// so the diorama scale divides out and no metre conversion is needed or wanted. <c>m11</c> is
    /// taken from the live projection matrix rather than <c>fieldOfView</c> because under XR the
    /// projection is handed to Unity by the runtime and may be asymmetric; <c>fieldOfView</c> is the
    /// fallback for the flat-screen case where no XR projection has been pushed.</para>
    /// </summary>
    internal static void Begin(Camera? head)
    {
        Reset();
        _armed = false;
        if (head == null)
            return;
        if (!TryEyeTarget(out _eyePxW, out _eyePxH))
            return;

        float m11;
        try
        {
            m11 = head.projectionMatrix.m11;
        }
        catch (Exception)
        {
            m11 = 0f;
        }
        if (!(m11 > 0.01f) || float.IsNaN(m11) || float.IsInfinity(m11))
            m11 = 1f / Mathf.Tan(Mathf.Clamp(head.fieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad);

        _pixelsPerUnitRatio = 0.5f * _eyePxH * m11;
        _headPos = head.transform.position;
        _armed = _pixelsPerUnitRatio > 1f;
    }

    /// <summary>
    /// The renderer's size in eye pixels, or 0 when it is not part of this census (not armed, no
    /// usable bounds, or below <see cref="MinPixelSpan"/>). Called ONCE per submitted renderer by
    /// the SCENE walk, so the <c>Renderer.bounds</c> read is paid once and not once per material.
    ///
    /// <para>The LARGEST bounds dimension is used, not the diagonal and not the projected area: the
    /// question is "how many texels does this surface's texture have across the direction it is
    /// stretched over", and for a wall panel, a floor tile or a rock face that direction is the
    /// long one. It is an over-estimate of the on-screen span for an object seen edge-on, which
    /// biases the ratio towards MAGNIFICATION — stated here because the conclusion this line most
    /// often reaches is "magnified", and a reader is entitled to know which way the model leans.</para>
    /// </summary>
    internal static float PixelSpan(Renderer r)
    {
        if (!_armed || r == null)
            return 0f;
        _renderersConsidered++;
        try
        {
            Bounds b = r.bounds;
            Vector3 s = b.size;
            float worldSize = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            if (!(worldSize > 0f) || float.IsNaN(worldSize))
                return 0f;
            float d = (b.center - _headPos).magnitude;
            if (!(d > 0.0001f) || float.IsNaN(d))
                return 0f;
            float px = worldSize * _pixelsPerUnitRatio / d;
            if (!(px >= MinPixelSpan) || float.IsInfinity(px))
                return 0f;
            _renderersInPopulation++;
            return px;
        }
        catch (Exception)
        {
            return 0f;
        }
    }

    /// <summary>
    /// Offer one submitted material slot of a renderer whose <see cref="PixelSpan"/> cleared the
    /// threshold. Keeps ONE record per distinct texture — the biggest surface it was seen on, since
    /// that is the one the complaint is about — and counts how many slots bound it.
    /// </summary>
    internal static void Offer(Renderer r, Material mat, float pixelSpan)
    {
        if (!_armed || mat == null || pixelSpan <= 0f)
            return;
        if (_materialProbes >= MaxMaterialProbes)
            return;
        _materialProbes++;

        Texture? tex = null;
        int propIndex = -1;
        try
        {
            for (int i = 0; i < AlbedoProps.Length; i++)
            {
                if (!mat.HasProperty(AlbedoProps[i]))
                    continue;
                Texture? candidate = mat.GetTexture(AlbedoProps[i]);
                if (candidate == null)
                    continue;
                tex = candidate;
                propIndex = i;
                break;
            }
        }
        catch (Exception)
        {
            return;
        }
        if (tex == null || tex.width < 2 || tex.height < 2)
            return;

        float tiling = 1f;
        try
        {
            Vector2 sc = mat.GetTextureScale(AlbedoProps[propIndex]);
            float t = Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y));
            if (t > 0.0001f && t < 10000f && !float.IsNaN(t))
                tiling = t;
        }
        catch (Exception)
        {
            // leave tiling at 1 — the ratio is a model either way and the raw size is printed
        }

        float texels = tex.width * tiling;
        float ratio = texels / Mathf.Max(pixelSpan, 0.01f);

        int id = tex.GetInstanceID();
        if (SurfaceIndex.TryGetValue(id, out int idx))
        {
            Surf existing = Surfaces[idx];
            existing.Slots++;
            if (pixelSpan > existing.PixelSpan)
            {
                existing.PixelSpan = pixelSpan;
                existing.Texels = texels;
                existing.TexelsPerPixel = ratio;
                existing.TilingX = tiling;
                existing.Mat = mat;
                existing.PropIndex = propIndex;
                existing.Tex = tex;
                // The record now describes a DIFFERENT renderer, so its name has to move with it —
                // an earlier version cleared this to null and left the biggest surface in the log
                // reading '<renderer gone>', which is the same string a destroyed object produces.
                try { existing.RendererName = r != null ? r.name : existing.RendererName; }
                catch (Exception) { /* keep whatever name we already had */ }
            }
            return;
        }
        if (Surfaces.Count >= MaxSurfaces)
            return;

        SurfaceIndex[id] = Surfaces.Count;
        Surfaces.Add(new Surf
        {
            Tex = tex,
            Mat = mat,
            RendererName = null,
            Slots = 1,
            PixelSpan = pixelSpan,
            Texels = texels,
            TexelsPerPixel = ratio,
            TilingX = tiling,
            PropIndex = propIndex,
        });
        // The renderer name is the only allocating read here (UnityEngine.Object.name mints a
        // string), so it is taken once per DISTINCT TEXTURE and never per slot.
        try { Surfaces[Surfaces.Count - 1].RendererName = r != null ? r.name : null; }
        catch (Exception) { /* a destroyed renderer still leaves a usable texture record */ }
    }

    // ==========================================================================================
    //  [Perf] TEX — the line
    // ==========================================================================================

    /// <summary>
    /// Emit the census and drop every reference. Safe to call when nothing was collected: it says
    /// so and says why, because a silent instrument and an instrument that never ran must never
    /// look the same (this project's standing rule, learned the expensive way).
    /// </summary>
    internal static void Log()
    {
        Stopwatch clock = Stopwatch.StartNew();
        StringBuilder sb = new(4096);
        try
        {
            sb.Append("TEX — why a surface looks soft up close: the source texels behind the "
                      + "renderers that fill the view, and the three global dials that can throw "
                      + "them away before they are ever sampled");
            AppendGlobals(sb);
            AppendPopulation(sb);
            AppendVerdict(sb);
        }
        catch (Exception e)
        {
            sb.Append(" | census threw ").Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        finally
        {
            clock.Stop();
            _costMs = clock.Elapsed.TotalMilliseconds;
            sb.Append(" | INSTRUMENT COST, measured not asserted: ")
              .Append(_costMs.ToString("F2"))
              .Append("ms for this line, on top of the SCENE walk it rides (it adds no scene walk "
                      + "of its own — see the class doc)");
            Reset();
        }
        VRLog.Info(Scope, sb.ToString());
    }

    /// <summary>
    /// The three global dials, FIRST, because each of them can make every other number on the line
    /// moot. Always printed, even when the population half found nothing — this half costs four
    /// field reads and is the half most likely to carry the answer.
    /// </summary>
    private static void AppendGlobals(StringBuilder sb)
    {
        int limit;
        try
        {
            limit = QualitySettings.masterTextureLimit;
        }
        catch (Exception e)
        {
            sb.Append(" | masterTextureLimit n/a (").Append(e.GetType().Name).Append(')');
            limit = -1;
        }

        if (limit >= 0)
        {
            sb.Append(" | masterTextureLimit=").Append(limit);
            if (limit == 0)
            {
                sb.Append(" (FULL — mip 0 is live, so no global mip drop is in play and nothing "
                          + "here is throwing source detail away)");
            }
            else
            {
                sb.Append(" ⇒ EVERY MIPPED TEXTURE IN THE PROCESS RENDERS AT 1/")
                  .Append(1 << limit)
                  .Append(" OF ITS AUTHORED SIZE PER SIDE (a 1024² albedo is a ")
                  .Append(Mathf.Max(1024 >> limit, 1)).Append("² albedo). This is the game's own "
                          + "Options ▸ Graphics ▸ Texture Quality dial (decompiled "
                          + "GraphicProfile.Setup writes masterTextureLimit = (int)TextureQuality "
                          + "from FULL/HALF/QUARTER/EIGHTHEN, persisted in "
                          + "SaveData.Global.CustomQualityProfile and re-applied at every boot; its "
                          + "deserialisation fallback for an unknown value is EIGHTHEN). IT IS ALSO "
                          + "RE-LOADED FROM THE LEVEL ASSET BY QualitySettings.SetQualityLevel, "
                          + "which the game calls on every quality swap. THIS IS THE CHEAPEST "
                          + "POSSIBLE WIN AND IT IS THE TOP OF THE LIST: nothing else on this line "
                          + "matters until it reads 0");
            }
        }

        try
        {
            AnisotropicFiltering aniso = QualitySettings.anisotropicFiltering;
            sb.Append(" | anisotropicFiltering=").Append(aniso);
            sb.Append(aniso == AnisotropicFiltering.ForceEnable
                ? " (FORCED — Rig/RenderQuality.ApplyAniso asserts this against the game's own "
                  + "'Disable', together with Texture.SetGlobalAnisotropicFilteringLimits(8,16). "
                  + "While it reads ForceEnable the per-texture anisoLevel figures below are "
                  + "OVERRIDDEN and anisoLevel=1 in the census is NOT a fault)"
                : aniso == AnisotropicFiltering.Enable
                    ? " (per-texture — each texture's own anisoLevel decides, and the census below "
                      + "says what those are. The mod's [RenderQuality] ForceAnisotropic is either "
                      + "off or has been overwritten by a quality swap since it last ran)"
                    : " ⇒ ANISOTROPIC FILTERING IS OFF. Every grazing-angle surface — most of a "
                      + "floor, most of a wall seen by a standing player — is smeared along its "
                      + "direction of maximum compression, for free and regardless of source "
                      + "resolution. [RenderQuality] ForceAnisotropic is the switch; if it is on, "
                      + "then a quality swap has overwritten it since it last ran and the "
                      + "re-assert is the fix");
        }
        catch (Exception e)
        {
            sb.Append(" | anisotropicFiltering n/a (").Append(e.GetType().Name).Append(')');
        }

        try
        {
            bool streaming = QualitySettings.streamingMipmapsActive;
            sb.Append(" | streamingMipmaps=").Append(streaming);
            if (streaming)
            {
                sb.Append(" budget=").Append(QualitySettings.streamingMipmapsMemoryBudget.ToString("F0"))
                  .Append("MB maxLevelReduction=").Append(QualitySettings.streamingMipmapsMaxLevelReduction)
                  .Append(" (ON — a starved budget holds textures BELOW their desired mip and the "
                          + "surface is soft until it catches up, which is what 'manchmal matschig' "
                          + "would look like; the population half counts how many are behind)");
            }
            else
            {
                sb.Append(" (off — every mipped texture is resident in full, so streaming cannot be "
                          + "the cause of an intermittent softness here)");
            }
        }
        catch (Exception e)
        {
            sb.Append(" | streamingMipmaps n/a (").Append(e.GetType().Name).Append(')');
        }

        sb.Append(" | eye target ").Append(_eyePxW.ToString("F0")).Append('x')
          .Append(_eyePxH.ToString("F0")).Append(" px/eye (XRSettings.eyeTexture* x "
                  + "renderViewportScale — the same figures Rig/RenderQuality's EYE-TARGET DIAG and "
                  + "WorldUI/PanelSamplingProbe use, so all three lines are comparable)");
    }

    /// <summary>The census proper: the population, its size/format/mip/aniso distributions, its
    /// VRAM bill, and the softest surfaces named individually.</summary>
    private static void AppendPopulation(StringBuilder sb)
    {
        if (!_armed)
        {
            sb.Append(" | POPULATION: not sampled this window — either the SCENE walk this census "
                      + "rides was rationed away (its own line says so, and this line is then the "
                      + "cheap half that still ran), or there is no head camera / no usable per-eye "
                      + "render target, in which case no rendered-pixel size and therefore no "
                      + "texels-per-pixel can be computed for anything. The global dials above are "
                      + "live readings either way");
            return;
        }
        if (Surfaces.Count == 0)
        {
            sb.Append(" | POPULATION: 0 textured surfaces cleared the ").Append(MinPixelSpan.ToString("F0"))
              .Append("-pixel threshold out of ").Append(_renderersConsidered)
              .Append(" submitted renderer(s) examined. Either the head is far from everything "
                      + "(the zoomed-out overview) or the game's shaders bind their albedo on a "
                      + "property this line does not probe (_Alb / _MainTex / _BaseMap)");
            return;
        }

        int mipped = 0, mipless = 0, readable = 0, streamingBehind = 0, streamingTracked = 0;
        int anisoMin = int.MaxValue, anisoMax = 0;
        long bytes = 0;
        int sub128 = 0, s256 = 0, s512 = 0, s1024 = 0, s2048 = 0, s4096 = 0;
        int magnified = 0;
        var formats = new Dictionary<string, int>(12);

        RatioScratch.Clear();
        for (int i = 0; i < Surfaces.Count; i++)
        {
            Surf s = Surfaces[i];
            Texture? t = s.Tex;
            if (t == null)
                continue;

            RatioScratch.Add(s.TexelsPerPixel);
            if (s.TexelsPerPixel < 1f)
                magnified++;

            int w = t.width;
            if (w <= 128) sub128++;
            else if (w <= 256) s256++;
            else if (w <= 512) s512++;
            else if (w <= 1024) s1024++;
            else if (w <= 2048) s2048++;
            else s4096++;

            int aniso = t.anisoLevel;
            if (aniso < anisoMin) anisoMin = aniso;
            if (aniso > anisoMax) anisoMax = aniso;

            var flat = t as Texture2D;
            int mips = flat != null ? flat.mipmapCount : t.mipmapCount;
            if (mips > 1) mipped++; else mipless++;

            string fmt = "?";
            if (flat != null)
            {
                fmt = flat.format.ToString();
                if (flat.isReadable)
                    readable++;
                try
                {
                    if (flat.streamingMipmaps)
                    {
                        streamingTracked++;
                        if (flat.loadedMipmapLevel > flat.desiredMipmapLevel)
                            streamingBehind++;
                    }
                }
                catch (Exception)
                {
                    // streaming fields are not valid on every texture source; silence is correct
                }
                bytes += EstimateBytes(flat.width, flat.height, flat.format, mips);
            }
            formats.TryGetValue(fmt, out int n);
            formats[fmt] = n + 1;
        }

        sb.Append(" | POPULATION: ").Append(Surfaces.Count)
          .Append(" DISTINCT albedo texture(s) on the ").Append(_renderersInPopulation)
          .Append(" submitted renderer(s) that span at least ").Append(MinPixelSpan.ToString("F0"))
          .Append(" px in the eye (of ").Append(_renderersConsidered).Append(" submitted, ")
          .Append(_materialProbes).Append(" material slot(s) probed)");

        sb.Append(" | AUTHORED SIZE (width, before masterTextureLimit): ≤128: ").Append(sub128)
          .Append(", 256: ").Append(s256).Append(", 512: ").Append(s512)
          .Append(", 1024: ").Append(s1024).Append(", 2048: ").Append(s2048)
          .Append(", ≥4096: ").Append(s4096);

        sb.Append(" | MIPS: ").Append(mipped).Append(" mipped, ").Append(mipless)
          .Append(" mipless")
          .Append(mipless > 0
              ? " (a mipless texture is IMMUNE to masterTextureLimit and cannot be helped by a mip "
                + "bias either — but it also shimmers under minification, which is the opposite "
                + "complaint)"
              : "");

        sb.Append(" | ANISO per-texture: min ").Append(anisoMin == int.MaxValue ? 0 : anisoMin)
          .Append(", max ").Append(anisoMax)
          .Append(" (see the global anisotropicFiltering reading above — under ForceEnable these "
                  + "are overridden and mean nothing)");

        if (streamingTracked > 0)
        {
            sb.Append(" | STREAMING: ").Append(streamingTracked)
              .Append(" of them are streamed, ").Append(streamingBehind)
              .Append(" currently BELOW their desired mip level")
              .Append(streamingBehind > 0
                  ? " ⇒ those are soft right now for a reason that has nothing to do with their "
                    + "authored size, and raising the streaming budget is the fix"
                  : " (none starved)");
        }

        sb.Append(" | READABLE: ").Append(readable).Append(" of ").Append(Surfaces.Count)
          .Append(readable == 0
              ? " — NONE. Any runtime read of this population has to go through the "
                + "Graphics.Blit + ReadPixels path (which this project already uses), never "
                + "GetPixels"
              : " (the rest need the Graphics.Blit + ReadPixels path for any runtime read)");

        sb.Append(" | FORMATS: ");
        bool first = true;
        foreach (KeyValuePair<string, int> kv in formats)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(kv.Key).Append(' ').Append(kv.Value);
        }

        double mb = bytes / (1024.0 * 1024.0);
        sb.Append(" | VRAM for this population: ~").Append(mb.ToString("F1"))
          .Append(" MB as authored (mip chains included, computed from format block sizes). "
                  + "Doubling every side would make that ~").Append((mb * 4.0).ToString("F0"))
          .Append(" MB and quadrupling it ~").Append((mb * 16.0).ToString("F0"))
          .Append(" MB — against a 24 GB card, and this is only the population in view");

        AppendMedian(sb, magnified);
        AppendSoftest(sb);
    }

    private static void AppendMedian(StringBuilder sb, int magnified)
    {
        if (RatioScratch.Count == 0)
            return;
        RatioScratch.Sort();
        float median = RatioScratch[RatioScratch.Count / 2];
        float p10 = RatioScratch[Mathf.Clamp(RatioScratch.Count / 10, 0, RatioScratch.Count - 1)];

        sb.Append(" | TEXELS PER RENDERED PIXEL across the population (modelled — see the class "
                  + "doc's stated assumption): median ").Append(median.ToString("F2"))
          .Append(", 10th percentile ").Append(p10.ToString("F2")).Append(", ")
          .Append(magnified).Append(" of ").Append(RatioScratch.Count)
          .Append(" below 1.0. BELOW 1.0 MEANS MAGNIFIED: the GPU is asked for more detail than the "
                  + "file contains, and no filter, mip bias, MSAA level or supersample factor can "
                  + "invent it — only more source texels can. ABOVE 1.0 means minified, which IS "
                  + "fixable camera-side and is the flicker complaint, not this one");
    }

    /// <summary>
    /// The softest surfaces, named, with EVERY texture their material binds — not just the albedo.
    /// A rock face with a 2048² albedo and no normal map is a different defect from one with a 256²
    /// albedo, and the photograph cannot tell them apart. The full texture list is taken by walking
    /// the SHADER's declared properties, which is exhaustive and costs nothing at this population
    /// size (<see cref="TopSoftest"/> materials).
    /// </summary>
    private static void AppendSoftest(StringBuilder sb)
    {
        Ranked.Clear();
        for (int i = 0; i < Surfaces.Count; i++)
        {
            if (Surfaces[i].Tex != null)
                Ranked.Add(Surfaces[i]);
        }
        if (Ranked.Count == 0)
            return;
        Ranked.Sort(CompareRatioAsc);

        int top = Mathf.Min(TopSoftest, Ranked.Count);
        sb.Append(" | THE ").Append(top).Append(" SOFTEST SURFACES IN VIEW (ranked by texels per "
                  + "rendered pixel, lowest first — these are the ones the photograph is of; every "
                  + "texture the material binds is listed, so a missing normal map shows up as a "
                  + "missing entry rather than as a guess): ");
        for (int i = 0; i < top; i++)
        {
            Surf s = Ranked[i];
            if (i > 0)
                sb.Append("; ");
            Texture t = s.Tex!;
            sb.Append('\'').Append(s.RendererName ?? "<renderer gone>").Append("' spans ~")
              .Append(s.PixelSpan.ToString("F0")).Append(" px in the eye against ~")
              .Append(s.Texels.ToString("F0")).Append(" modelled texel(s), x").Append(s.Slots)
              .Append(" slot(s) = ").Append(s.TexelsPerPixel.ToString("F2")).Append(" texel/px");
            if (s.TilingX > 1.0001f || s.TilingX < 0.9999f)
                sb.Append(" (tiling x").Append(s.TilingX.ToString("F2")).Append(')');
            sb.Append(" — ").Append(AlbedoPropNames[Mathf.Clamp(s.PropIndex, 0, AlbedoPropNames.Length - 1)])
              .Append('=').Append(Describe(t));
            AppendOtherTextures(sb, s.Mat, t);
        }
    }

    /// <summary>Every OTHER texture the material binds, by walking the shader's declared texture
    /// properties. Wrapped whole: a foreign shader with a property the reflection dislikes must cost
    /// one surface's detail, not the line.</summary>
    private static void AppendOtherTextures(StringBuilder sb, Material? mat, Texture albedo)
    {
        if (mat == null)
            return;
        try
        {
            Shader sh = mat.shader;
            if (sh == null)
                return;
            sb.Append(" [shader ").Append(sh.name);
            int count = sh.GetPropertyCount();
            int printed = 0;
            for (int p = 0; p < count && printed < 6; p++)
            {
                if (sh.GetPropertyType(p) != ShaderPropertyType.Texture)
                    continue;
                string pname = sh.GetPropertyName(p);
                Texture? bound = mat.GetTexture(pname);
                if (bound == null || bound.GetInstanceID() == albedo.GetInstanceID())
                    continue;
                sb.Append(", ").Append(pname).Append('=').Append(Describe(bound));
                printed++;
            }
            if (printed == 0)
                sb.Append(", no other texture bound");
            sb.Append(']');
        }
        catch (Exception e)
        {
            sb.Append(" [shader property walk threw ").Append(e.GetType().Name).Append(']');
        }
    }

    /// <summary>
    /// The verdict clause: which of the five causes on the class doc the numbers just ruled IN, in
    /// the order they should be spent. Written out rather than left to the reader because this line
    /// exists to answer a question that was asked in German by someone who is not going to read a
    /// histogram, and the integrator has to be able to hand the answer on.
    /// </summary>
    private static void AppendVerdict(StringBuilder sb)
    {
        sb.Append(" | VERDICT: ");
        int limit;
        try { limit = QualitySettings.masterTextureLimit; } catch (Exception) { limit = -1; }
        bool anisoOff;
        try { anisoOff = QualitySettings.anisotropicFiltering == AnisotropicFiltering.Disable; }
        catch (Exception) { anisoOff = false; }

        bool said = false;
        if (limit > 0)
        {
            sb.Append("(1) masterTextureLimit is ").Append(limit)
              .Append(" — FREE WIN, every mipped texture is at 1/").Append(1 << limit)
              .Append(" per side and this is upstream of everything else. ");
            said = true;
        }
        if (anisoOff)
        {
            sb.Append("(2) anisotropic filtering is OFF — FREE WIN at grazing angles. ");
            said = true;
        }
        if (RatioScratch.Count > 0)
        {
            float median = RatioScratch[RatioScratch.Count / 2];
            if (median < 1f)
            {
                sb.Append("(3) the median surface in view is MAGNIFIED (")
                  .Append(median.ToString("F2")).Append(" texel/px), so after the free wins above "
                          + "the remaining softness is A SHORTAGE OF SOURCE TEXELS. Bilinear or "
                          + "Lanczos upscaling at runtime would add ZERO information and only make "
                          + "a soft texture bigger; only new detail — an offline super-resolution "
                          + "pass over the game's own textures, or a sharpening post pass that "
                          + "fakes acutance — can change it. ");
                said = true;
            }
            else
            {
                sb.Append("(3) the median surface in view is MINIFIED (")
                  .Append(median.ToString("F2")).Append(" texel/px), which is the FLICKER "
                          + "complaint's shape and is fixable camera-side (mips, aniso, bias) "
                          + "rather than by adding source texels. ");
                said = true;
            }
        }
        if (!said)
            sb.Append("nothing measurable this window. ");
        sb.Append("(4) SIMPLE SILHOUETTES AND FLAT SHADING ARE NOT ON THIS LINE AT ALL — that is "
                  + "mesh LOD and lighting, and the AutomaticLOD lane owns it. Subtract this line's "
                  + "findings from the photograph and what is left is theirs.");
    }

    // ==========================================================================================
    //  helpers
    // ==========================================================================================

    private static int CompareRatioAsc(Surf a, Surf b) => a.TexelsPerPixel.CompareTo(b.TexelsPerPixel);

    private static string Describe(Texture t)
    {
        var flat = t as Texture2D;
        int mips = flat != null ? flat.mipmapCount : t.mipmapCount;
        string fmt = flat != null ? flat.format.ToString() : t.GetType().Name;
        return $"'{t.name}' {t.width}x{t.height} {fmt} mips={mips} aniso={t.anisoLevel} {t.filterMode}";
    }

    /// <summary>
    /// The per-eye render target in pixels, including the live resolution and viewport levers.
    /// Deliberately the same computation as <c>WorldUI/PanelSamplingProbe.TryEyeTarget</c> — it is
    /// duplicated rather than shared because that one is private to a module this file must not
    /// depend on, and six lines of arithmetic is a smaller debt than a cross-module coupling.
    /// </summary>
    private static bool TryEyeTarget(out float pxW, out float pxH)
    {
        pxW = 0f;
        pxH = 0f;
        try
        {
            float viewport = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
            int w = XRSettings.eyeTextureWidth;
            int h = XRSettings.eyeTextureHeight;
            if (w < 2 || h < 2)
            {
                w = Screen.width;
                h = Screen.height;
                viewport = 1f;
            }
            pxW = w * viewport;
            pxH = h * viewport;
            return pxW >= 2f && pxH >= 2f;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Bytes on the GPU for one texture including its mip chain. The block sizes are written out
    /// rather than taken from <c>GraphicsFormatUtility</c> deliberately: that helper lives in
    /// <c>UnityEngine.Experimental.Rendering</c> on this engine version and an estimate that
    /// vanishes on an engine bump is worse than one whose arithmetic is on the page. Unknown formats
    /// fall back to 32 bpp, which OVER-states them — the VRAM figure is therefore a ceiling, which
    /// is the safe direction for a budget question.
    /// </summary>
    private static long EstimateBytes(int w, int h, TextureFormat fmt, int mips)
    {
        double bpp = fmt switch
        {
            TextureFormat.Alpha8 or TextureFormat.R8 => 8,
            TextureFormat.RGB565 or TextureFormat.ARGB4444 or TextureFormat.RGBA4444
                or TextureFormat.R16 or TextureFormat.RHalf => 16,
            TextureFormat.RGB24 => 24,
            TextureFormat.RGBA32 or TextureFormat.ARGB32 or TextureFormat.BGRA32
                or TextureFormat.RGHalf or TextureFormat.RFloat => 32,
            TextureFormat.RGBAHalf or TextureFormat.RGFloat => 64,
            TextureFormat.RGBAFloat => 128,
            TextureFormat.DXT1 or TextureFormat.DXT1Crunched or TextureFormat.BC4
                or TextureFormat.ETC_RGB4 or TextureFormat.ETC2_RGB => 4,
            TextureFormat.DXT5 or TextureFormat.DXT5Crunched or TextureFormat.BC5
                or TextureFormat.BC6H or TextureFormat.BC7 or TextureFormat.ETC2_RGBA8 => 8,
            _ => 32,
        };
        double level0 = w * (double)h * bpp / 8.0;
        // A full mip chain adds 1/3 of level 0 in the limit; mips <= 1 adds nothing.
        double total = mips > 1 ? level0 * 4.0 / 3.0 : level0;
        return (long)total;
    }

    private static void Reset()
    {
        // Disarmed as well as emptied. A window the SCENE walk RATIONED away never calls Begin, and
        // a census left armed from the previous window would print a confident "0 surfaces" that
        // reads like a measurement instead of like the absence of one.
        _armed = false;
        Surfaces.Clear();
        SurfaceIndex.Clear();
        Ranked.Clear();
        RatioScratch.Clear();
        _materialProbes = 0;
        _renderersConsidered = 0;
        _renderersInPopulation = 0;
    }
}
