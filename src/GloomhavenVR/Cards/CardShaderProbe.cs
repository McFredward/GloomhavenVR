using System;
using UnityEngine;
using UnityEngine.Rendering;
using GloomhavenVR.Core;

namespace GloomhavenVR.Cards;

/// <summary>
/// ROUND 11 (a) — THE GPU EXPERIMENT THAT ENDS THE GUESSING. Ten rounds against the black card
/// frame have produced zero visible change: nine punched frame pixels to alpha 0 on mod-owned
/// sprite copies, the tenth (ModBuild 114) provably shrank the face rects and the user still
/// reported "unverändert". The one mechanism that explains all ten at once is the card shader
/// itself: every card-face Image renders through a per-image CLONE of 'GUI_CardEffect_Mat'
/// (shader 'GUI/AbilityCard_Shd' — CardEffects.Awake, decompiled/GH.Runtime/CardEffects.cs:336),
/// the shader exposes NO readable blend state (CARD SHADER IDENTITY, ModBuild 114), and the
/// user's own observation is the smoking gun: during the character-switch DISSOLVE animation the
/// black band briefly turns TRANSPARENT. A dissolve clip of the shape
/// <c>clip(f(tex.a) - _Dissolve·…)</c> discards NOTHING at <c>_Dissolve = 0</c> and would draw
/// alpha-0 pixels as opaque black — which is exactly what nine punch rounds saw.
///
/// The compiled shader is not on this machine (only Managed DLLs ship in ressources/), so the
/// theory cannot be settled by reading source. This class settles it AT RUNTIME instead, on the
/// user's GPU, with the live material: render a tiny two-half test texture (frame-dark opaque
/// left, the punch's exact alpha-0 right) through a CLONE of the first card material seen, into
/// an offscreen target cleared to a magenta sentinel, once at <c>_Dissolve = 0</c> (rest) and
/// once at the epsilon the DISSOLVE FLOOR fix writes (<see cref="CardDissolveFloor"/>). The
/// readbacks decide between exactly four outcomes, spelled out in the VERDICT clause of the one
/// <c>CARD SHADER PROBE</c> line this logs — so whatever the user reports next round, the log
/// already names the truth.
///
/// The draw path deliberately mirrors what a CanvasRenderer feeds the shader: a unit quad with
/// UVs 0..1 and WHITE vertex color, drawn through <see cref="CommandBuffer.DrawMesh"/> under
/// orthographic view/projection matrices (<c>Graphics.Blit</c> would feed a UI shader the wrong
/// vertex inputs), with <c>_PosAndBounds</c> set to a sane card-shaped value mirroring what
/// <c>CardEffects.Initialize</c> writes (canvas position + header rect bounds,
/// CardEffects.cs:347) so the shader does not degenerate.
///
/// Crash-proof by contract: everything is wrapped, every RT/texture/material clone is released,
/// the LIVE material is never mutated, and a failure logs verdict UNPROVEN without affecting the
/// dissolve-floor fix, which is unconditional either way.
/// </summary>
internal static class CardShaderProbe
{
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int PosAndBoundsId = Shader.PropertyToID("_PosAndBounds");
    private static readonly int GreyOutId = Shader.PropertyToID("_GreyOut");
    private static readonly int BurnId = Shader.PropertyToID("_Burn");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    /// <summary>The epsilon leg of the experiment — the same rest-state floor
    /// <see cref="CardDissolveFloor"/> writes, so a (b) verdict proves that fix by this very log.</summary>
    private const float EpsilonDissolve = 0.004f;

    /// <summary>One experiment per session — the first card-FX material seen is representative,
    /// every card Image clones the same 'GUI_CardEffect_Mat'.</summary>
    private static bool s_ran;

    /// <summary>
    /// Run the probe once per session, on the first CARD-FX material seen (the caller is the
    /// shader-identity diagnostic in <c>CardFace.FramePunch</c> — the same "first card material"
    /// moment). Materials outside the card-FX family (uGUI default, decoration shaders) are
    /// ignored: the experiment is only meaningful on the shader that owns the band.
    /// </summary>
    internal static void MaybeRun(Material? liveMat)
    {
        if (s_ran || liveMat == null || liveMat.shader == null)
            return;
        if (!liveMat.HasProperty(DissolveId) || !liveMat.HasProperty(PosAndBoundsId))
            return; // not the card-FX family — wait for the real suspect
        s_ran = true;
        try
        {
            Run(liveMat);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Cards", "CARD SHADER PROBE failed before producing a readback " +
                                $"({ex.GetType().Name}: {ex.Message}). VERDICT: UNPROVEN — the GPU " +
                                "experiment did not run to completion, so this build's log cannot " +
                                "settle whether the card shader honors alpha at rest; the dissolve " +
                                "floor still ships and the next log must be read instead.");
        }
    }

    private static void Run(Material liveMat)
    {
        Texture2D? testTex = null;
        Texture2D? readback = null;
        Material? clone = null;
        Mesh? quad = null;
        RenderTexture? rt = null;
        RenderTexture? prevActive = RenderTexture.active;
        try
        {
            // 1. The two-half test texture: LEFT half frame-dark but OPAQUE (the printed frame as
            // shipped), RIGHT half the same RGB at alpha 0 (exactly what nine rounds of punching
            // produced). 8x8 point-filtered so each half is a flat field with no filtering bleed.
            testTex = new Texture2D(8, 8, TextureFormat.RGBA32, mipChain: false)
            {
                name = "VRCardShaderProbeTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var dark = new Color32(30, 30, 30, 255);
            var punched = new Color32(30, 30, 30, 0);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    testTex.SetPixel(x, y, x < 4 ? dark : punched);
            testTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            // 2. A CLONE of the live material — the real one is never mutated. Rest state pinned
            // explicitly (the clone inherits whatever the live card was doing this frame), and
            // _PosAndBounds mirrors CardEffects.Initialize's canvasPosition + header-rect bounds
            // shape so the shader's position math does not degenerate to a zero-sized card.
            clone = new Material(liveMat) { name = "VRCardShaderProbeMat" };
            clone.SetTexture(MainTexId, testTex);
            if (clone.HasProperty(GreyOutId)) clone.SetFloat(GreyOutId, 0f);
            if (clone.HasProperty(BurnId)) clone.SetFloat(BurnId, 0f);
            if (clone.HasProperty(FlowId)) clone.SetFloat(FlowId, 0f);
            clone.SetVector(PosAndBoundsId, new Vector4(0f, 0f, 294f, 450f));

            // 3. A unit quad with UVs 0..1 and WHITE vertex color — the vertex stream a
            // CanvasRenderer feeds a UI material — drawn under GL.LoadOrtho-style matrices.
            quad = new Mesh { name = "VRCardShaderProbeQuad" };
            quad.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
            };
            quad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            quad.colors32 = new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            };
            quad.triangles = new[] { 0, 1, 2, 2, 1, 3 };

            rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32);
            readback = new Texture2D(64, 64, TextureFormat.RGBA32, mipChain: false)
            {
                name = "VRCardShaderProbeReadback",
            };

            // 4. The experiment, twice: rest state (_Dissolve = 0) and the epsilon the floor
            // writes. One pixel from the middle of each half per pass.
            DrawAndSample(clone, quad, rt, readback, 0f,
                          out Color32 restLeft, out Color32 restRight);
            DrawAndSample(clone, quad, rt, readback, EpsilonDissolve,
                          out Color32 epsLeft, out Color32 epsRight);

            LogVerdict(liveMat, restLeft, restRight, epsLeft, epsRight);
            LogPropertyTable(liveMat);
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (testTex != null) UnityEngine.Object.Destroy(testTex);
            if (readback != null) UnityEngine.Object.Destroy(readback);
            if (clone != null) UnityEngine.Object.Destroy(clone);
            if (quad != null) UnityEngine.Object.Destroy(quad);
        }
    }

    /// <summary>Magenta sentinel the target is cleared to — nothing on a card is magenta, so a
    /// sentinel pixel in the readback means the shader DISCARDED (or fully alpha-blended away)
    /// that fragment.</summary>
    private static readonly Color Sentinel = new(1f, 0f, 1f, 1f);

    // ------------------------------------------------------------ round 12: generic rest probe --

    /// <summary>
    /// ROUND 12 — THE "WHO PAINTS WHEN GIVEN NOTHING DARK" PROBE, for materials the round-11
    /// experiment never looked at. The round-11 verdict (a) exonerated the card-FX shader
    /// ('GUI/AbilityCard_Shd' honors alpha at rest), which means the band the user sees has
    /// ANOTHER painter — and the face carries graphics the band inventory can only judge by
    /// their <c>Image.color</c>: a sprite-less Image renders uGUI's built-in WHITE texture
    /// through whatever material the prefab assigned it (the burn/FX overlay 'UIFX_Overlay'
    /// spans 120 % of the face through its own custom material, <c>CardEffects.fgFx</c>), and
    /// what such a shader OUTPUTS at rest is invisible to any inventory that reads colors.
    ///
    /// This renders a CLONE of <paramref name="liveMat"/> — live property values as-is, i.e.
    /// the material's CURRENT rest state — over the same CanvasRenderer-style unit quad as the
    /// round-11 probe, feeding it exactly what uGUI feeds a sprite-less Image (the opaque white
    /// texture, white vertex color), against the magenta sentinel. One sentence comes back:
    /// paints NOTHING (sentinel survived), paints DARK OPAQUE (a band painter, convicted by its
    /// own output), or paints its input (bright — dark can then only come from a dark input).
    /// Crash-proof and allocation-bounded like the round-11 probe; the live material is never
    /// mutated. Called once per distinct shader by <see cref="CardBandPainter"/>.
    /// </summary>
    internal static string DescribeRestOutput(Material? liveMat)
    {
        if (liveMat == null || liveMat.shader == null)
            return "no material — nothing to probe";
        Material? clone = null;
        Mesh? quad = null;
        RenderTexture? rt = null;
        Texture2D? readback = null;
        RenderTexture? prevActive = RenderTexture.active;
        try
        {
            clone = new Material(liveMat) { name = "VRCardBandPainterProbeMat" };
            if (clone.HasProperty(MainTexId))
                clone.SetTexture(MainTexId, Texture2D.whiteTexture);
            quad = new Mesh { name = "VRCardBandPainterProbeQuad" };
            quad.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
            };
            quad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            quad.colors32 = new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            };
            quad.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32);
            readback = new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: false)
            {
                name = "VRCardBandPainterProbeReadback",
            };
            using (var cmd = new CommandBuffer { name = "VRCardBandPainterProbe" })
            {
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(clearDepth: true, clearColor: true, backgroundColor: Sentinel);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,
                                              Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
                cmd.DrawMesh(quad, Matrix4x4.identity, clone, 0, -1);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0, recalculateMipMaps: false);
            Color32 c = readback.GetPixel(8, 8);
            if (IsSentinel(c))
                return "paints NOTHING at rest on a white input (sentinel survived) — this material " +
                       "cannot be the band painter in its current state";
            if (IsDark(c))
                return $"paints DARK OPAQUE {Fmt(c)} at rest on an all-WHITE input — this material " +
                       "darkens/replaces whatever uGUI feeds it, and a graphic wearing it over the " +
                       "frame band IS a band painter regardless of any sprite punch";
            return $"paints {Fmt(c)} at rest on a white input (its input shows through) — a dark band " +
                   "under it can only come from dark INPUT pixels, not from the shader itself";
        }
        catch (Exception ex)
        {
            return $"probe failed ({ex.GetType().Name}: {ex.Message}) — no verdict for this material";
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (readback != null) UnityEngine.Object.Destroy(readback);
            if (clone != null) UnityEngine.Object.Destroy(clone);
            if (quad != null) UnityEngine.Object.Destroy(quad);
        }
    }

    // ------------------------------------------------ round 16: cutout EXECUTION probe --

    /// <summary>
    /// ROUND 16 — DOES THE ALPHA CLIP ACTUALLY EXECUTE AT RENDER TIME? USER VERDICT on ModBuild
    /// 119, verbatim: "Ein erster teilerfolg: Die Ränder sind jetzt nun nicht mehr schwarz sondern
    /// bech (siehe karten5.png) aber immer noch nicht transparent wie sie sein sollten."
    ///
    /// <para>THE PARADOX THIS SETTLES. The 119 log holds two statements that cannot both describe
    /// the same GPU state: the painter inventory reads the Backing slab's material as 'Standard'
    /// CUTOUT-clipped (<c>_ALPHATEST_ON</c> set, the baked Edge footprint as <c>mainTexture</c>,
    /// <c>_Cutoff</c> 0.5), and the CARD BAND PIXELS strips measure the band OPAQUE umber
    /// (125,97,68,≈255) — which is EdgeColor (0.42,0.33,0.23) × (light ≈0.16 + emission 1.0) to
    /// within 1/255, i.e. the slab itself painting where its texture says alpha 0. A material
    /// KEYWORD is only a request: in a player build Unity ships exactly the shader VARIANTS some
    /// build-time material referenced, and <c>EnableKeyword("_ALPHATEST_ON")</c> on a shader whose
    /// cutout variant was stripped silently selects the closest surviving variant — which never
    /// calls <c>clip()</c>. Then the slab draws its FULL rounded-rect envelope, and since the baked
    /// Edge texture's RGB is EdgeColor in EVERY texel (transparent ones included — only alpha
    /// differs), the band renders exactly the measured wood. The emission floor (round 15) already
    /// documented this same stripping risk for <c>_EMISSION</c>; the 119 log proves THAT variant
    /// exists (the band brightened to albedo × 1.16 exactly), which makes the cutout variant the
    /// one still unproven.</para>
    ///
    /// <para>THE EXPERIMENT. Draw a unit quad through a CLONE of the LIVE material — the clone
    /// copies keywords, <c>_Cutoff</c> AND the live <c>mainTexture</c>, so it renders through the
    /// same variant Unity resolves for the real slab — into a sentinel-cleared offscreen target,
    /// twice: once with every vertex UV pinned to a texel the CPU footprint says is alpha-0 (the
    /// band), once pinned to the opaque card centre (the control). Constant UVs mean zero UV
    /// derivatives, so the sample is mip 0 — no trilinear/aniso ambiguity. If the clip executes,
    /// the alpha-0 draw is discarded and the sentinel survives; if the variant is stripped, the
    /// quad paints. Verdict names which, or UNPROVEN when the opaque control fails to paint.</para>
    ///
    /// <para>Crash-proof like the other probes: live material never mutated, every temp released,
    /// a throw returns an UNPROVEN sentence instead of propagating. Caller caches per material.</para>
    /// </summary>
    internal static string DescribeCutoutClip(Material? liveMat, Vector2 uvAlpha0, Vector2 uvOpaque)
    {
        if (liveMat == null || liveMat.shader == null)
            return "no material — nothing to probe";
        Material? clone = null;
        Mesh? quad = null;
        RenderTexture? rt = null;
        Texture2D? readback = null;
        RenderTexture? prevActive = RenderTexture.active;
        try
        {
            clone = new Material(liveMat) { name = "VRCardCutoutClipProbeMat" };
            quad = new Mesh { name = "VRCardCutoutClipProbeQuad" };
            quad.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
            };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.colors32 = new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            };
            quad.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            rt = RenderTexture.GetTemporary(16, 16, 16, RenderTextureFormat.ARGB32);
            readback = new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: false)
            {
                name = "VRCardCutoutClipProbeReadback",
            };
            Color32 DrawAt(Vector2 uv)
            {
                quad.uv = new[] { uv, uv, uv, uv }; // constant UV → one texel, mip 0, no filtering
                using (var cmd = new CommandBuffer { name = "VRCardCutoutClipProbe" })
                {
                    cmd.SetRenderTarget(rt);
                    cmd.ClearRenderTarget(clearDepth: true, clearColor: true, backgroundColor: Sentinel);
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity,
                                                  Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
                    // Pass 0 = the Standard shader's FORWARD base pass — the pass the slab is
                    // actually seen through; -1 would also splat ShadowCaster/Meta into the target.
                    cmd.DrawMesh(quad, Matrix4x4.identity, clone, 0, 0);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0, recalculateMipMaps: false);
                return readback.GetPixel(8, 8);
            }
            Color32 opaque = DrawAt(uvOpaque);
            Color32 hole = DrawAt(uvAlpha0);
            string samples = $"opaque-UV {uvOpaque:F3} → {Fmt(opaque)}, alpha0-UV {uvAlpha0:F3} → {Fmt(hole)}";
            if (IsSentinel(opaque))
            {
                return $"UNPROVEN ({samples}) — even the OPAQUE control UV came back as the " +
                       "sentinel, so this draw never exercised the shader and the clip question " +
                       "stays open; judge the differential passes instead";
            }
            if (IsSentinel(hole))
            {
                return $"clip EXECUTES ({samples}) — a fragment sampling an alpha-0 texel of the " +
                       "material's OWN cutout texture was discarded on this GPU/build, so the " +
                       "_ALPHATEST_ON variant is live and the band's opaque wood is NOT this " +
                       "material clipping wrong: the differential passes name the real painter";
            }
            return $"clip DOES NOT EXECUTE ({samples}) — _ALPHATEST_ON is set and _Cutoff is " +
                   $"{(clone.HasProperty("_Cutoff") ? clone.GetFloat("_Cutoff").ToString("F2") : "n/a")}, " +
                   "yet a fragment sampling an alpha-0 texel of the material's OWN cutout texture " +
                   "painted instead of being discarded. The game build's Standard shader is missing " +
                   "the cutout variant (a keyword is only a request — a stripped variant silently " +
                   "falls back to one that never calls clip()), so the slab draws its FULL " +
                   "rounded-rect envelope; the baked Edge texture's RGB is EdgeColor in EVERY texel, " +
                   "which is exactly the measured umber band. The fix is a clip-capable shader (or " +
                   "real mesh trimming), not more texture/keyword work";
        }
        catch (Exception ex)
        {
            return $"probe failed ({ex.GetType().Name}: {ex.Message}) — no clip verdict; judge the " +
                   "differential passes instead";
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (readback != null) UnityEngine.Object.Destroy(readback);
            if (clone != null) UnityEngine.Object.Destroy(clone);
            if (quad != null) UnityEngine.Object.Destroy(quad);
        }
    }

    private static void DrawAndSample(Material mat, Mesh quad, RenderTexture rt,
                                      Texture2D readback, float dissolve,
                                      out Color32 left, out Color32 right)
    {
        mat.SetFloat(DissolveId, dissolve);
        using (var cmd = new CommandBuffer { name = "VRCardShaderProbe" })
        {
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(clearDepth: true, clearColor: true, backgroundColor: Sentinel);
            // GL.LoadOrtho semantics: identity view, 0..1 ortho projection — the unit quad fills
            // the whole target.
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,
                                          Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            // Pass -1 = every pass of the material, so a multi-pass UI shader is exercised whole.
            cmd.DrawMesh(quad, Matrix4x4.identity, mat, 0, -1);
            Graphics.ExecuteCommandBuffer(cmd);
        }
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, recalculateMipMaps: false);
        left = readback.GetPixel(16, 32);   // middle of the OPAQUE dark half
        right = readback.GetPixel(48, 32);  // middle of the punched alpha-0 half
    }

    /// <summary>Sentinel test: the magenta clear survived — the shader put nothing there.</summary>
    private static bool IsSentinel(Color32 c) => c.r > 200 && c.b > 200 && c.g < 60;

    /// <summary>Dark test: the frame-dark texel (or something near it) was painted opaque.</summary>
    private static bool IsDark(Color32 c) => c.r < 90 && c.g < 90 && c.b < 90;

    private static string Fmt(Color32 c) => $"({c.r},{c.g},{c.b},{c.a})";

    private static void LogVerdict(Material liveMat, Color32 restLeft, Color32 restRight,
                                   Color32 epsLeft, Color32 epsRight)
    {
        string samples =
            $"raw readbacks against a magenta sentinel — _Dissolve=0: opaque-dark half {Fmt(restLeft)}, " +
            $"alpha-0 half {Fmt(restRight)}; _Dissolve={EpsilonDissolve}: opaque-dark half {Fmt(epsLeft)}, " +
            $"alpha-0 half {Fmt(epsRight)}";
        string verdict;
        if (IsSentinel(restLeft))
        {
            // The OPAQUE half came back sentinel: the draw did not exercise the shader at all
            // (wrong vertex inputs, a pass that never ran, a degenerate _PosAndBounds reading) —
            // no conclusion about alpha semantics is honest from this.
            verdict = "UNPROVEN — even the OPAQUE test half came back as the sentinel, so the " +
                      "probe's draw never exercised the shader and neither alpha reading was " +
                      "actually tested; the draw path needs a different harness before this " +
                      "shader can be convicted or cleared";
        }
        else if (IsSentinel(restRight))
        {
            verdict = "alpha HONORED at rest — the alpha-0 half shows the sentinel at " +
                      "_Dissolve=0, so erasing frame pixels to alpha 0 genuinely makes them " +
                      "invisible on this shader and nine invisible punch rounds CANNOT be its " +
                      "fault: the band the user sees has ANOTHER painter (a different layer, a " +
                      "different material, or geometry behind the face), and the dissolve floor " +
                      "shipping in this build is harmless but not the cure";
        }
        else if (IsDark(restRight) && IsSentinel(epsRight))
        {
            verdict = "alpha IGNORED at rest, EPSILON DISCARDS — at _Dissolve=0 the shader " +
                      "paints the punched alpha-0 pixels as opaque dark (the black band, and the " +
                      "reason nine texture-side rounds changed nothing), while at _Dissolve=" +
                      $"{EpsilonDissolve} the same pixels are discarded to the sentinel. The " +
                      "DISSOLVE EPSILON FLOOR shipping in this build is therefore PROVEN by this " +
                      "very log: holding every card-FX material at that epsilon makes the punched " +
                      "frame pixels disappear everywhere";
        }
        else if (IsDark(restRight))
        {
            verdict = "alpha IGNORED at rest AND at the epsilon — the punched half stays opaque " +
                      "dark in both passes, so no _Dissolve value this small discards anything " +
                      "and the dissolve floor CANNOT remove the band (it ships anyway and does " +
                      "no harm); the only remaining road is replacing the shader on the card " +
                      "images with one that alpha-blends";
        }
        else
        {
            verdict = "UNPROVEN — the alpha-0 half came back neither sentinel nor frame-dark " +
                      "(see the raw readbacks), which matches no predicted outcome; read the " +
                      "numbers before the next round instead of trusting any classification";
        }
        VRLog.Info("Cards", "CARD SHADER PROBE: a clone of the live card material " +
                            $"'{liveMat.name}' (shader '{liveMat.shader.name}') rendered an 8x8 " +
                            "two-half test texture — frame-dark OPAQUE left half, the punch's " +
                            "exact alpha-0 right half — as a CanvasRenderer-style unit quad " +
                            "(CommandBuffer.DrawMesh, ortho matrices, white vertex color, " +
                            "_PosAndBounds=(0,0,294,450)) into an offscreen target cleared to " +
                            $"magenta. {samples}. VERDICT: {verdict}.");
    }

    /// <summary>
    /// The shader's COMPLETE property table with the live material's current values — one compact
    /// line, so any future round argues from facts instead of from HasProperty probing.
    /// </summary>
    private static void LogPropertyTable(Material mat)
    {
        try
        {
            Shader shader = mat.shader;
            int count = shader.GetPropertyCount();
            var sb = new System.Text.StringBuilder(256);
            for (int i = 0; i < count; i++)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                string name = shader.GetPropertyName(i);
                ShaderPropertyType type = shader.GetPropertyType(i);
                sb.Append(name).Append('=');
                switch (type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        sb.Append(mat.GetFloat(name).ToString("0.###"));
                        break;
                    case ShaderPropertyType.Vector:
                        Vector4 v = mat.GetVector(name);
                        sb.Append('(').Append(v.x.ToString("0.###")).Append(',')
                          .Append(v.y.ToString("0.###")).Append(',')
                          .Append(v.z.ToString("0.###")).Append(',')
                          .Append(v.w.ToString("0.###")).Append(')');
                        break;
                    case ShaderPropertyType.Color:
                        Color c = mat.GetColor(name);
                        sb.Append('(').Append(c.r.ToString("0.###")).Append(',')
                          .Append(c.g.ToString("0.###")).Append(',')
                          .Append(c.b.ToString("0.###")).Append(',')
                          .Append(c.a.ToString("0.###")).Append(')');
                        break;
                    default: // Texture
                        Texture? t = mat.GetTexture(name);
                        sb.Append(t != null ? $"tex'{t.name}'" : "tex-null");
                        break;
                }
            }
            VRLog.Info("Cards", $"CARD SHADER PROBE property table ('{shader.name}', {count} " +
                                $"properties, live values): {sb}");
        }
        catch (Exception ex)
        {
            VRLog.Warn("Cards", "CARD SHADER PROBE property table failed " +
                                $"({ex.GetType().Name}: {ex.Message}) — the verdict line above " +
                                "stands on its own.");
        }
    }
}
