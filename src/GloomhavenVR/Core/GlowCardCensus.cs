using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE PALE FLOATING RECTANGLES ON THE GATE — the <c>[Perf] GLOW CARDS</c> line.
///
/// <para>THE REPORT THIS WAS BUILT FOR (user, 2026-08-24, verbatim): <i>"Diese schwebenden
/// viereckigen Lichter an dem Tor erscheinen mir komisch, wird das richtig gerendert? Fehlt hier
/// irgendwas? Das ist dauerhaft so egal was ein oder ausgeblendet wird - ich kann mich nicht
/// erinnern, dass es flat sowas gab."</i> (schwebende_lichter.jpg). Three pale, hard-edged,
/// near-opaque rectangles sit on the gate's door frames and on the masonry beside them. A candle in
/// an alcove two metres to the right renders correctly and warmly, so the room's lighting is fine.
/// A second photograph from the same build (walls_gone.jpg) shows the SAME three rectangles at the
/// SAME brightness while the masonry around them has dissolved almost to nothing — measured off the
/// two JPEGs, the quads read (240,239,205) / (221,218,170) / (222,219,173) against a solid wall and
/// (219,222,202) / (206,208,179) / (229,221,182) against a dissolved one. Their output does not
/// depend on what is behind them and does not depend on their wall's fade.</para>
///
/// <para>WHAT THIS FILE IS FOR. The mod already knows one of those three renderers by NAME — the
/// wall-fade census prints <c>'Glow'[mesh→alpha] anchor 2.2 gap 0.00 → 'Wall 3'</c> and
/// <c>'Glow'[→alpha] base 2.2 top 2.8 gap 0.22 → 'ThickDoor : (1a010af9-…)'</c> — but nothing in
/// this codebase has ever printed that renderer's SHADER, its MATERIAL, its blend state or its
/// keywords, and the game's own C# never references a child called "Glow" at all (it is authored in
/// the tileset prefab, so it cannot be read out of <c>decompiled/</c>). Every hypothesis about why
/// the quads draw the way they do is a hypothesis about those fields. This line prints them.</para>
///
/// <para>THE HYPOTHESIS IT IS BUILT TO KILL OR CONFIRM, stated so a reader can check it against the
/// line rather than against my confidence. The mod's own head camera runs the built-in FORWARD path
/// with <c>depthTextureMode=None</c> in every window of both hardware sessions. The game's VFX
/// shaders soft-fade against <c>_CameraDepthTexture</c>; <see cref="Rig.VRRigDriver"/>'s
/// <c>CreateHeadCamera</c> already documents this exact failure in the past tense — <i>"the fade
/// sampled nothing and FAILED OPEN → glow rendered fully through walls"</i> — and
/// <c>[Optimize] HeadDepthPrepass</c> exists to A/B it. Under D3D11 Unity uses a reversed depth
/// buffer, so an unwritten depth texture reads as the FAR plane: a fade term of the shape
/// <c>saturate((sceneZ - fragZ) * _InvFade)</c> then saturates to 1 for every fragment, i.e. FULL
/// opacity with no softening anywhere, which is what a hard-edged rectangle at constant brightness
/// over two different backgrounds looks like. If that is the mechanism, the cards below will carry a
/// depth-fade property and the head camera will report no Depth bit, and both halves of that are
/// printed here side by side.</para>
///
/// <para>THE COMPETING EXPLANATION IT ALSO SETTLES. The rectangles could instead simply never be
/// claimed by the wall-fade path, in which case they would stay solid when their wall goes for a
/// reason that has nothing to do with rendering. That is why every record prints the LIVE
/// MaterialPropertyBlock alpha next to the material's authored alpha: the wall fade drives these
/// props by writing <c>_TintColor</c>/<c>_Color</c>/<c>_BaseColor</c> with the alpha scaled by
/// <c>1 - fade</c> through a per-renderer block. A card with no block is not being driven (a
/// claiming question); a card with a block whose alpha is near zero while the card is still bright
/// in the headset is being driven and ignoring it (a rendering question, and the blend state on the
/// same record says why — an additive pass with <c>_DstBlend=One</c> cannot be dimmed by an alpha
/// write at all). Those two are the leads the photographs cannot separate, and this line separates
/// them without another hardware round.</para>
///
/// <para>COST. This class adds NO scene walk. It rides
/// <see cref="PerfSceneProfile.AppendSceneLine"/>'s existing <c>FindObjectsOfType&lt;Renderer&gt;</c>
/// — the standing rule in this repo is that a per-frame scene sweep is the default suspect and has
/// shipped as a defect three times. Per material slot it costs ONE dictionary lookup keyed by the
/// SHADER's instance id, so <c>Shader.name</c> (a managed-string marshal) is paid once per distinct
/// shader in the scene (43 of them in the reported room) and never once per renderer. Everything
/// expensive — the GameObject name, the ancestor walk, the keyword array, the property probes, the
/// block read-back — happens only for renderers that already matched, and is capped at
/// <see cref="MaxCards"/>. <see cref="Log"/> times itself and prints the number.</para>
/// </summary>
internal static class GlowCardCensus
{
    private const string Scope = "Perf";

    /// <summary>Hard cap on detailed records. The reported defect is three quads; sixteen is room
    /// for the whole family plus the torches, and it bounds both the cost and the line length.</summary>
    private const int MaxCards = 16;

    /// <summary>Hard cap on materials probed in detail, so a scene full of glow shaders cannot turn
    /// this into the thing it measures.</summary>
    private const int MaxProbes = 64;

    // ==========================================================================================
    //  what counts as a glow card
    // ==========================================================================================

    /// <summary>Shader-name fragments that mark a light/glow/decal card. Matched case-insensitively
    /// against the SHADER name, not the object name: the object name is authored per tileset and
    /// varies ('Glow', 'CandleFlame', 'p_fire_torch'), while the shader is the thing whose fade term
    /// is in question. Names taken from this game's own always-loaded shader list
    /// (SimpleParticleAlphaDFade, glow_bowl_Shd, LightShaftShd, OmniDecalSimple_Shd,
    /// ParticleMasterUnlitAdd_Shd/Blend_Shd) and from the shader ranking the SCENE line already
    /// prints for the reported room (VFX/BendyGlowPlane_Shd, 21 submitted slots).</summary>
    private static readonly string[] ShaderMarks =
    {
        "glow", "dfade", "depthfade", "softparticle", "lightshaft", "shaft",
        "flare", "halo", "beam", "decal", "particlemaster", "lightplane"
    };

    /// <summary>Property names whose PRESENCE means the material's opacity depends on the camera
    /// depth texture. <c>_InvFade</c> is Unity's own soft-particle term; the rest are the names this
    /// game's VFX shader family uses. Probed with <see cref="Material.HasProperty(int)"/>, which
    /// does not need the shader source.</summary>
    private static readonly string[] DepthFadePropNames =
    {
        "_InvFade", "_SoftParticleFactor", "_DepthFade", "_DFade", "_SoftFade",
        "_FadeDistance", "_IntersectionFade", "_CameraFadeDistance", "_Softness"
    };

    private static readonly int[] DepthFadeProps = BuildIds(DepthFadePropNames);

    /// <summary>The three colour properties the wall-fade path drives, in the SAME priority order it
    /// uses (WallSegmentFade.Mounted.cs) — so "which one is it driving" is answerable from here.</summary>
    private static readonly string[] TintPropNames = { "_TintColor", "_Color", "_BaseColor" };

    private static readonly int[] TintProps = BuildIds(TintPropNames);

    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    private static int[] BuildIds(string[] names)
    {
        int[] ids = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
            ids[i] = Shader.PropertyToID(names[i]);
        return ids;
    }

    // ==========================================================================================
    //  collected state
    // ==========================================================================================

    private sealed class Card
    {
        public string Path = string.Empty;
        public string Shader = string.Empty;
        public string Material = string.Empty;
        public string Keywords = string.Empty;
        public string DepthProps = string.Empty;   // "" = none found
        public string TintProp = string.Empty;     // "" = none of the three present
        public bool Enabled;
        public bool Visible;
        public bool Submitted;
        public int Layer;
        public int Queue;
        public int Src = -1, Dst = -1, ZWrite = -1;
        public bool SoftKeyword;
        public Color Tint = Color.clear;
        public bool HasBlock;
        public bool BlockHasTint;
        public float BlockAlpha = -1f;
        public Vector3 Size;
        public Vector3 Centre;
        public float PixelSpan;
    }

    private static readonly List<Card> Cards = new(MaxCards);
    private static readonly Dictionary<int, bool> ShaderInterest = new(64);
    private static readonly HashSet<int> SeenRenderers = new(MaxCards * 2);
    private static readonly List<string> Scratch = new(8);
    private static MaterialPropertyBlock? _blockScratch;

    private static bool _armed;
    /// <summary>Did <see cref="Begin"/> run for the window <see cref="Log"/> is about to print?
    /// PerfMonitor calls AppendGfxLine — and therefore this line — even on a window where
    /// AppendSceneLine rationed itself and never walked, so "found nothing" and "never looked" are
    /// two different sentences and this flag is what tells them apart.</summary>
    private static bool _sampled;
    private static int _probes;
    private static int _matched;
    private static int _depthFadeMaterials;
    private static int _distinctGlowShaders;
    private static DepthTextureMode _headDepth = DepthTextureMode.None;
    private static bool _headKnown;
    private static bool _softParticles;
    private static bool _depthDialOn;
    private static double _costMs;

    // ==========================================================================================
    //  collection — driven from PerfSceneProfile's SCENE walk
    // ==========================================================================================

    /// <summary>Arm the census for one window and latch the two GLOBAL facts the verdict needs, at
    /// the same instant the population is sampled. Latching them here rather than reading them in
    /// <see cref="Log"/> matters: the head camera's depth bit is owned by a per-frame tick and the
    /// whole point of the line is that the cards and the depth state are read TOGETHER.</summary>
    internal static void Begin(Camera? head)
    {
        Reset();
        _armed = true;
        _sampled = true;
        _headKnown = false;
        _depthDialOn = PerfConfig.DepthPrepassOn;
        try
        {
            _softParticles = QualitySettings.softParticles;
        }
        catch (Exception)
        {
            _softParticles = false;
        }
        if (head == null)
            return;
        try
        {
            _headDepth = head.depthTextureMode;
            _headKnown = true;
        }
        catch (Exception)
        {
            _headKnown = false;
        }
    }

    /// <summary>
    /// Offer one material slot of one renderer. Called for EVERY slot of every renderer in the SCENE
    /// walk, so the fast path — a shader that is not a glow shader — must stay at one dictionary
    /// lookup, and it does: <paramref name="sh"/> is the reference
    /// <see cref="PerfSceneProfile"/>'s own shader tally has already resolved (so there is no second
    /// <c>Material.shader</c> marshal), and <c>Shader.name</c> is fetched once per distinct Shader
    /// object and never once per renderer.
    /// </summary>
    internal static void Offer(Renderer r, Material mat, Shader sh, bool submitted, float pixelSpan)
    {
        if (!_armed || r == null || mat == null || sh == null)
            return;

        int shaderId = sh.GetInstanceID();
        if (!ShaderInterest.TryGetValue(shaderId, out bool interesting))
        {
            interesting = IsGlowShader(sh);
            ShaderInterest[shaderId] = interesting;
            if (interesting)
                _distinctGlowShaders++;
        }
        if (!interesting)
            return;

        _matched++;
        if (Cards.Count >= MaxCards || _probes >= MaxProbes)
            return;
        // One record per RENDERER, not per slot: a two-slot glow card is one rectangle in the eye.
        if (!SeenRenderers.Add(r.GetInstanceID()))
            return;
        _probes++;

        try
        {
            Cards.Add(Describe(r, mat, sh, submitted, pixelSpan));
        }
        catch (Exception)
        {
            SeenRenderers.Remove(r.GetInstanceID());
        }
    }

    private static bool IsGlowShader(Shader sh)
    {
        string name;
        try
        {
            name = sh.name;
        }
        catch (Exception)
        {
            return false;
        }
        if (string.IsNullOrEmpty(name))
            return false;
        for (int i = 0; i < ShaderMarks.Length; i++)
        {
            if (name.IndexOf(ShaderMarks[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static Card Describe(Renderer r, Material mat, Shader sh, bool submitted, float pixelSpan)
    {
        Card c = new()
        {
            Path = PathOf(r.transform),
            Shader = sh.name,
            Material = mat.name,
            Enabled = r.enabled,
            Visible = r.isVisible,
            Submitted = submitted,
            Layer = r.gameObject.layer,
            Queue = mat.renderQueue,
            PixelSpan = pixelSpan
        };

        try
        {
            Bounds b = r.bounds;
            c.Size = b.size;
            c.Centre = b.center;
        }
        catch (Exception)
        {
            // leave zero — a renderer without usable bounds is still worth naming
        }

        // ---- the depth contract -------------------------------------------------------------
        Scratch.Clear();
        for (int i = 0; i < DepthFadeProps.Length; i++)
        {
            if (!mat.HasProperty(DepthFadeProps[i]))
                continue;
            float v;
            try
            {
                v = mat.GetFloat(DepthFadeProps[i]);
            }
            catch (Exception)
            {
                v = float.NaN;
            }
            Scratch.Add(DepthFadePropNames[i] + "=" + v.ToString("F3"));
        }
        if (Scratch.Count > 0)
        {
            c.DepthProps = string.Join(" ", Scratch.ToArray());
            _depthFadeMaterials++;
        }

        try
        {
            c.SoftKeyword = mat.IsKeywordEnabled("SOFTPARTICLES_ON");
        }
        catch (Exception)
        {
            c.SoftKeyword = false;
        }

        // ---- blend state: can an alpha write dim this at all? ---------------------------------
        c.Src = ReadInt(mat, SrcBlendId);
        c.Dst = ReadInt(mat, DstBlendId);
        c.ZWrite = ReadInt(mat, ZWriteId);

        // ---- the colour the wall fade would drive, and whether it IS being driven --------------
        int tintIndex = -1;
        for (int i = 0; i < TintProps.Length; i++)
        {
            if (!mat.HasProperty(TintProps[i]))
                continue;
            tintIndex = i;
            break;
        }
        if (tintIndex >= 0)
        {
            c.TintProp = TintPropNames[tintIndex];
            try
            {
                c.Tint = mat.GetColor(TintProps[tintIndex]);
            }
            catch (Exception)
            {
                c.Tint = Color.clear;
            }
        }

        try
        {
            c.HasBlock = r.HasPropertyBlock();
            if (c.HasBlock && tintIndex >= 0)
            {
                _blockScratch ??= new MaterialPropertyBlock();
                _blockScratch.Clear();
                r.GetPropertyBlock(_blockScratch);
                if (_blockScratch.HasColor(TintProps[tintIndex]))
                {
                    c.BlockHasTint = true;
                    c.BlockAlpha = _blockScratch.GetColor(TintProps[tintIndex]).a;
                }
            }
        }
        catch (Exception)
        {
            c.HasBlock = false;
        }

        try
        {
            string[] kw = mat.shaderKeywords;
            c.Keywords = kw.Length == 0 ? "(none)" : string.Join(",", kw);
        }
        catch (Exception)
        {
            c.Keywords = "(unreadable)";
        }

        return c;
    }

    private static int ReadInt(Material mat, int id)
    {
        try
        {
            return mat.HasProperty(id) ? Mathf.RoundToInt(mat.GetFloat(id)) : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Hierarchy path, deepest-last, capped at five levels — enough to tell 'Wall 3' from
    /// 'ThickDoor : (guid)' without printing an Apparance path that fills the log line.</summary>
    private static string PathOf(Transform t)
    {
        Scratch.Clear();
        Transform? cur = t;
        for (int i = 0; i < 5 && cur != null; i++)
        {
            Scratch.Add(cur.name);
            cur = cur.parent;
        }
        Scratch.Reverse();
        return string.Join("/", Scratch.ToArray());
    }

    // ==========================================================================================
    //  [Perf] GLOW CARDS — the line
    // ==========================================================================================

    /// <summary>
    /// Emit the census and drop every reference. It ALWAYS prints, including when it found nothing,
    /// and when it found nothing it says which shader-name fragments it looked for — a silent
    /// instrument and an instrument that never ran must never look the same, which is this repo's
    /// standing rule and was learned the expensive way.
    /// </summary>
    internal static void Log()
    {
        Stopwatch clock = Stopwatch.StartNew();
        if (!_sampled)
        {
            VRLog.Info(Scope, "GLOW CARDS — NOT SAMPLED this window. The SCENE walk rationed itself "
                              + "(see the SCENE line above), so this census was never armed and the "
                              + "absence of records below carries NO information about the scene. "
                              + "This sentence exists because an instrument that found nothing and "
                              + "an instrument that never ran must never look the same.");
            return;
        }

        StringBuilder sb = new(4096);
        try
        {
            sb.Append("GLOW CARDS — the light/glow/decal quads and whether their opacity can work "
                      + "on this camera. Built for the report 'Diese schwebenden viereckigen Lichter "
                      + "an dem Tor erscheinen mir komisch' (schwebende_lichter.jpg, 2026-08-24): "
                      + "three pale hard-edged rectangles on a gate, at the SAME measured brightness "
                      + "whether the wall behind them is solid or dissolved");
            AppendGlobals(sb);
            AppendCards(sb);
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
              .Append("ms for this line; the collection half is one dictionary lookup per material "
                      + "slot on the SCENE walk this rides — on the Shader reference that walk had "
                      + "already resolved — and it adds no scene walk and no extra shader marshal "
                      + "of its own");
            Reset();
        }
        VRLog.Info(Scope, sb.ToString());
    }

    /// <summary>The two globals that can make every per-card number moot. Printed FIRST and always,
    /// because between them they ARE the hypothesis.</summary>
    private static void AppendGlobals(StringBuilder sb)
    {
        bool depthOn = _headKnown && (_headDepth & DepthTextureMode.Depth) != 0;
        sb.Append(" | DEPTH CONTRACT: head camera depthTextureMode=");
        sb.Append(_headKnown ? _headDepth.ToString() : "UNKNOWN (no head camera this window)");
        sb.Append(", [Optimize] HeadDepthPrepass=").Append(_depthDialOn ? "true" : "false");
        sb.Append(", QualitySettings.softParticles=").Append(_softParticles ? "true" : "false");
        if (_headKnown && !depthOn)
        {
            sb.Append(" ⇒ _CameraDepthTexture IS NOT WRITTEN THIS FRAME. Under D3D11 Unity uses a "
                      + "REVERSED depth buffer, so an unwritten depth texture reads as the FAR "
                      + "plane: any fade of the shape saturate((sceneZ - fragZ) * _InvFade) "
                      + "evaluates to 1 for every fragment, i.e. the card draws at FULL authored "
                      + "opacity with a hard edge everywhere instead of melting into the surface "
                      + "behind it. Rig/VRRigDriver.HeadCamera.cs records this failure in the past "
                      + "tense ('the fade sampled nothing and FAILED OPEN → glow rendered fully "
                      + "through walls') and the dial above is the A/B for it");
        }
        else if (depthOn)
        {
            sb.Append(" ⇒ depth IS written, so a depth-fade term can work and this line cannot be "
                      + "the explanation for a hard-edged card");
        }
    }

    private static void AppendCards(StringBuilder sb)
    {
        sb.Append(" | POPULATION: ").Append(_matched)
          .Append(" material slot(s) on ").Append(_distinctGlowShaders)
          .Append(" distinct glow/light/decal shader(s); ").Append(Cards.Count)
          .Append(" renderer(s) described below (cap ").Append(MaxCards).Append("), ")
          .Append(_depthFadeMaterials)
          .Append(" of them carry a DEPTH-FADE property");

        if (Cards.Count == 0)
        {
            sb.Append(" | NOTHING MATCHED. The selector is a case-insensitive SHADER-name contains "
                      + "over: ").Append(string.Join(", ", ShaderMarks))
              .Append(". An empty population here means the quads' shader is named nothing like any "
                      + "of those, NOT that there are no glow quads — widen ShaderMarks against the "
                      + "'by SHADER' ranking on the [Perf] SCENE line before concluding anything");
            return;
        }

        for (int i = 0; i < Cards.Count; i++)
        {
            Card c = Cards[i];
            sb.Append(" | [").Append(i).Append("] '").Append(c.Path).Append("' shader '")
              .Append(c.Shader).Append("' material '").Append(c.Material).Append("' q")
              .Append(c.Queue)
              .Append(c.Enabled ? " enabled" : " DISABLED")
              .Append(c.Visible ? "+visible" : "+offscreen")
              .Append(c.Submitted ? "+submitted" : "+not-submitted")
              .Append(" layer ").Append(c.Layer)
              .Append(" AABB c(").Append(c.Centre.x.ToString("F1")).Append(',')
              .Append(c.Centre.y.ToString("F1")).Append(',').Append(c.Centre.z.ToString("F1"))
              .Append(") s(").Append(c.Size.x.ToString("F2")).Append(',')
              .Append(c.Size.y.ToString("F2")).Append(',').Append(c.Size.z.ToString("F2"))
              .Append(") ~").Append(c.PixelSpan.ToString("F0")).Append("px");

            sb.Append(" | blend src=").Append(BlendName(c.Src)).Append(" dst=").Append(BlendName(c.Dst))
              .Append(" zwrite=").Append(c.ZWrite < 0 ? "n/a" : c.ZWrite.ToString());
            if (c.Dst == 1)
            {
                sb.Append(" ⇒ ADDITIVE: the destination factor is One, so this material's contribution "
                          + "is ADDED to whatever is behind it and a colour-ALPHA write cannot dim it "
                          + "at all — the wall fade's alpha ramp is INERT on this renderer by "
                          + "construction, whatever the block below says");
            }

            sb.Append(" | depth-fade props: ")
              .Append(string.IsNullOrEmpty(c.DepthProps) ? "NONE" : c.DepthProps)
              .Append(", SOFTPARTICLES_ON=").Append(c.SoftKeyword ? "yes" : "no");

            sb.Append(" | tint: ");
            if (string.IsNullOrEmpty(c.TintProp))
            {
                sb.Append("no _TintColor/_Color/_BaseColor on this material ⇒ the wall fade cannot "
                          + "classify it as an alpha prop at all");
            }
            else
            {
                sb.Append(c.TintProp).Append(" authored a=").Append(c.Tint.a.ToString("F3"));
                if (!c.HasBlock)
                {
                    sb.Append(", NO property block on the renderer ⇒ nothing is driving this card "
                              + "right now (a CLAIMING answer, not a rendering one)");
                }
                else if (!c.BlockHasTint)
                {
                    sb.Append(", a property block IS attached but it carries no ").Append(c.TintProp)
                      .Append(" ⇒ something else owns this renderer's block");
                }
                else
                {
                    sb.Append(", LIVE block a=").Append(c.BlockAlpha.ToString("F3"));
                    if (c.BlockAlpha < 0.05f)
                    {
                        sb.Append(" ⇒ the fade IS writing this card to invisible. If it is still "
                                  + "bright in the headset the write is being ignored, and the blend "
                                  + "state above says whether it ever could have worked");
                    }
                }
            }

            sb.Append(" | keywords: ").Append(c.Keywords);
        }
    }

    private static void AppendVerdict(StringBuilder sb)
    {
        if (Cards.Count == 0)
            return;

        bool depthOn = _headKnown && (_headDepth & DepthTextureMode.Depth) != 0;
        int additive = 0, driven = 0, unclaimed = 0;
        for (int i = 0; i < Cards.Count; i++)
        {
            if (Cards[i].Dst == 1)
                additive++;
            if (Cards[i].BlockHasTint)
                driven++;
            else if (!string.IsNullOrEmpty(Cards[i].TintProp) && !Cards[i].HasBlock)
                unclaimed++;
        }

        sb.Append(" | VERDICT: ");
        if (_depthFadeMaterials > 0 && !depthOn)
        {
            sb.Append(_depthFadeMaterials)
              .Append(" of the cards above depend on a depth texture this camera does not write. "
                      + "THAT IS SUFFICIENT ON ITS OWN to make them draw hard-edged at full "
                      + "opacity, and it is independent of the wall fade — which is exactly the "
                      + "'dauerhaft so egal was ein oder ausgeblendet wird' in the report. THE "
                      + "TEST: set [Optimize] HeadDepthPrepass = true and look at the same gate. "
                      + "It is not free — on the built-in forward path Unity builds the depth "
                      + "texture by re-submitting every opaque renderer through its shadow-caster "
                      + "pass, once PER EYE PASS, so read the [Perf] FRAME line on both sides of "
                      + "the flip before keeping it");
        }
        else if (_depthFadeMaterials == 0)
        {
            sb.Append("no card carries a depth-fade property, so the missing depth texture is NOT "
                      + "why these draw hard-edged, and the depth prepass would not fix them");
        }
        else
        {
            sb.Append("depth is written and the cards can fade, so look at the blend state and the "
                      + "block alphas above, not at the camera");
        }

        sb.Append(". CLAIMING: ").Append(driven)
          .Append(" card(s) carry a live fade write, ").Append(unclaimed)
          .Append(" carry none at all, ").Append(additive)
          .Append(" are additive and cannot be dimmed by an alpha write however they are claimed");
    }

    private static string BlendName(int v) => v switch
    {
        -1 => "n/a",
        0 => "Zero",
        1 => "One",
        2 => "DstColor",
        3 => "SrcColor",
        4 => "OneMinusDstColor",
        5 => "SrcAlpha",
        6 => "OneMinusSrcColor",
        7 => "DstAlpha",
        8 => "OneMinusDstAlpha",
        9 => "SrcAlphaSaturate",
        10 => "OneMinusSrcAlpha",
        _ => v.ToString()
    };

    private static void Reset()
    {
        Cards.Clear();
        ShaderInterest.Clear();
        SeenRenderers.Clear();
        Scratch.Clear();
        _armed = false;
        _sampled = false;
        _probes = 0;
        _matched = 0;
        _depthFadeMaterials = 0;
        _distinctGlowShaders = 0;
        _headDepth = DepthTextureMode.None;
        _headKnown = false;
    }
}
