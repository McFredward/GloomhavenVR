// THE MOD'S OWN WATER FILM — pinned here, and pinned partly as a SOURCE LINT against the .shader
// file itself, because two of the three things that could silently re-open this defect are
// properties of text rather than of arithmetic.
//
// WHAT THIS IS DEFENDING. Five hardware rounds have now been spent on one pool. ModBuild 161 read
// every reachable property back OFF THE LIVE MATERIAL INSTANCE — every shoreline/foam/border width
// at 0, every band colour at alpha 0, an empty keyword list, _Smoothness 0.08, the metal and
// reflection ceiling 0 — and the verdict was "Keine Änderungen bei der Wasser Problematik", plus
// "Ich konnte aber mit den anderen Einstellungen die kopf-gebundene Reflektion nicht deaktivieren,
// egal was ich eingestellt hab." The remaining ambiguity — whether the mod even held the surfaces
// he was pointing at — was then closed by [Water] DebugPaint on hardware: "Die debug farbe
// funktioniert - alles färbt sich magenta wie gewollt."
//
// Owned renderer + every reachable property neutral + the defect unchanged leaves one explanation:
// the pale sheet and the head-bound reflection come from a TEXTURE or a CONSTANT compiled inside
// VFX/Water_Shd_Trans. So the film's whole material is replaced with a mod-owned one.
//
// ModBuild 162 SHIPPED THAT AND IT WORKED — "Beide Probleme gelöst, top!" — AND THE CURE COST TOO
// MUCH LOOK: "Allerdings: Das Wasser sieht jetzt sehr viel schlechter aus. Das echte Wasser hatte
// ANimation und co. das will ich auch wieder. Ich will es so nah wie möglich an dem 'echten' Wasser
// haben - aber eben so dass es in VR funktioniert." ModBuild 162's film was GloomhavenVR/Overlay, a
// flat unlit sheet, chosen because it was the only bundled shader that provably sampled no
// environment. The film now draws on GloomhavenVR/WaterVR, written for this one job, with Overlay
// left as the fallback — so BOTH shaders are swept by every source check here, because either can
// end up on the water.
//
// Things that can silently undo all of it, and each has a check below:
//
//   1. A REPLACEMENT SHADER ACQUIRING A VIEW-DEPENDENT TERM. This is a HARD requirement, not a
//      preference, and it is now the thing most at risk: WaterVR exists precisely to LOOK like
//      reflective water, and the obvious way to make water sparkle is a specular built from the
//      view vector. That is the defect. A half-vector highlight slides across the surface as the
//      head turns — "bewegen sich schnell mit den Kopfbewegungen mit" — and under MULTIPASS any
//      view-dependent term is a different image in each eye, which is what got the wall dissolve
//      parked permanently (.planning/wall-fade-stereo-rivalry.md). Check 2 sweeps both shaders'
//      source for every spelling of an environment sample AND of a view vector, so an edit that
//      adds a Blinn-Phong lobe fails the build gate rather than a sixth photograph.
//
//   1b. VERTEX DISPLACEMENT COMING BACK. The tileset's own _addSphericalWaves is 0, so the water it
//      authored does not displace; and Unity culls a renderer against its MESH's authored bounds,
//      so displaced geometry vanishes as you approach and, under MultiPass, vanishes in ONE EYE
//      FIRST. This project has already lost a build to that. Check 2b pins that WaterVR's vertex
//      program hands v.vertex straight to UnityObjectToClipPos and that no displacement property
//      exists for anyone to turn up.
//
//   2. THE BLEND GOING ADDITIVE. Overlay exposes its blend factors AS PROPERTIES, and the driver
//      writes them. `Blend One One` is additive: it would lay the film's colour ON TOP of the stone
//      and make the hexes LIGHTER than their surroundings — a pixel-for-pixel re-creation of the
//      photographed defect, produced this time by the mod's own shader. Check 3 pins the pair
//      against UnityEngine.Rendering.BlendMode itself, so a transposed digit cannot pass.
//
//   3. THE LOOK CEASING TO BE THE TILESET'S. The film's hue is the authored _Color_Tint verbatim
//      and its alpha is min(authored, [Water] Opacity). A change that let either rise arrives as
//      "jetzt ist es noch heller" and nothing else in this repository could notice — the same
//      invariant WaterEdgeBand holds, for the same reason. Check 4 sweeps it.
//
// And one thing that is not about the film at all: the standing user ruling of 2026-08-18, "Das
// Wasser soll auf jeden Fall dargestellt werden - aber eben in einer VR-freundlichen Variante.
// Einfach ausblenden ist keine Option." ModBuild 158 hid the quads and that path was deleted. Check
// 5 is a source lint that no water file writes Renderer.enabled, because a re-introduction would
// look, from inside the headset, exactly like the flicker that ruling came from.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WireTests;

internal static class WaterOwnSurfaceVectors
{
    private const string BundleRelRoot = "unity/GloomhavenVR.Assets/Assets/Bundle";
    private const string WaterSrcRelDir = "src/GloomhavenVR/Core";

    /// <summary>The film's authored body tint, read off the live material on hardware
    /// (LogOutput.log:1018). Every number this file compares against is measured.</summary>
    private static readonly Color AuthoredTint = new(0.195f, 0.311f, 0.131f, 0.737f);

    /// <summary><c>[Water] Opacity</c>'s shipped default.</summary>
    private const float DefaultOpacity = 0.45f;

    /// <summary>`Shader "GloomhavenVR/Name"` — the name a .shader file DECLARES.</summary>
    private static readonly Regex ShaderDecl = new(
        @"^\s*Shader\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Every spelling of "this fragment samples the environment" that the built-in
    /// pipeline offers, AND every spelling of "this fragment knows where the camera is". A shader
    /// containing ANY of them can produce a term that swings with the head — which is the one thing
    /// the replacement film may not do, and which is also the one thing that makes a MultiPass
    /// surface differ between the two eyes.</summary>
    private static readonly string[] ReflectionTerms =
    {
        "unity_SpecCube",
        "reflect(",
        "samplerCUBE",
        "texCUBE",
        "UNITY_SAMPLE_TEXCUBE",
        "TextureCube",
        "worldRefl",
        "WorldReflectionVector",
        "ShadeSH9",          // not a reflection, but an environment sample all the same
        // ---- the VIEW VECTOR, in every form the built-in pipeline hands one out. These are the
        // additions that matter most now: WaterVR exists to look like reflective water, and the
        // obvious way to make water sparkle is a specular lobe built from one of these. That IS
        // the reported defect ("bewegen sich schnell mit den Kopfbewegungen mit"), it is also
        // per-eye rivalrous under MultiPass, and nothing else in this repository would notice.
        "ObjSpaceViewDir",
        "WorldSpaceViewDir",
        "_WorldSpaceCameraPos",
        "UNITY_MATRIX_V",
        "UNITY_MATRIX_I_V",
        "viewDir",
    };

    /// <summary>Both shaders the water film can end up on. Every SOURCE check runs over both: the
    /// fallback is what is on screen whenever the bundle does not yield the water shader, so a
    /// view-dependent term added to Overlay by another feature would ship the defect just as
    /// surely.</summary>
    private static readonly string[] FilmShaders =
    {
        WaterOwnSurface.FilmShaderName,
        WaterOwnSurface.FallbackFilmShaderName,
    };

    internal static void Run(Harness t, string repoRoot)
    {
        t.Case("water own surface");

        string? waterPath = FindShaderSource(repoRoot, WaterOwnSurface.FilmShaderName);
        foreach (string name in FilmShaders)
        {
            string? path = FindShaderSource(repoRoot, name);
            ShaderIsReachable(t, name, path);
            ShaderHasNoEnvironmentSample(t, name, path);
            ShaderDeclaresProperties(t, name, path, WaterOwnSurface.CommonProperties);
        }
        ShaderDeclaresProperties(
            t, WaterOwnSurface.FilmShaderName, waterPath, WaterOwnSurface.WaterProperties);
        WaterShaderDisplacesNoVertex(t, waterPath);
        RenderStateIsNotAdditive(t);
        FilmColourIsTheTilesets(t);
        NeverBrighterSweep(t);
        ScrollRateIsTheTilesets(t);
        NormalStrengthIsBounded(t);
        LightDirectionIsSaneOrRefused(t);
        WaterNeverHidesARenderer(t, repoRoot);
    }

    // ---------------------------------------------------------------------------------------
    //  1. The shader exists, declares the name we file it under, and is a TABLE entry.
    // ---------------------------------------------------------------------------------------
    private static void ShaderIsReachable(Harness t, string name, string? shaderPath)
    {
        t.True(shaderPath != null,
            $"the water film's shader '{name}' must be a "
            + $".shader file under {BundleRelRoot} that DECLARES that exact name. No file declares "
            + "it, so BundleShaders.Resolve has nothing to load out of the bundle and every water "
            + "film would silently keep the game's own material — which is a build that looks "
            + "identical to the five that came before it.");

        // The name is a plain string literal in non-comment src/ code, so BundledShaderVectors'
        // check 4 already requires it to be a key of BundleShaders.Paths. Stated here so a reader
        // of THIS file knows the bundled-shader trap is covered and does not add a second table.
        t.True(name.StartsWith("GloomhavenVR/", StringComparison.Ordinal),
            "the replacement film shader must be one of the MOD's shaders — a game shader would "
            + "put us back to tuning something we cannot open. Got: " + name);
    }

    // ---------------------------------------------------------------------------------------
    //  2. THE HARD REQUIREMENT. No environment sample, no view vector, in the shader's own source.
    // ---------------------------------------------------------------------------------------
    private static void ShaderHasNoEnvironmentSample(Harness t, string name, string? shaderPath)
    {
        if (shaderPath == null)
            return;

        string text = File.ReadAllText(shaderPath);
        var found = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            // Comments are exempt for the reason BundledShaderVectors gives: these files are this
            // project's changelog and quote the terms they explain. A commented-out sample cannot
            // compile, let alone run.
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal))
                continue;
            foreach (string term in ReflectionTerms)
            {
                if (line.IndexOf(term, StringComparison.Ordinal) >= 0 && !found.Contains(term))
                    found.Add(term);
            }
        }

        t.Equal(0, found.Count,
            $"'{name}' is a shader the water film is re-based onto and "
            + "it MUST NOT sample the environment or the view direction, but its source now "
            + "contains: " + string.Join(", ", found) + ". The user's report is that the head-bound "
            + "reflection cannot be switched off by any property dial — 'Ich konnte aber mit den "
            + "anderen Einstellungen die kopf-gebundene Reflektion nicht deaktivieren, egal was ich "
            + "eingestellt hab' — so a replacement that can compute a view-dependent term answers "
            + "nothing and ships the same defect under a different name. That includes a plain "
            + "specular: a half-vector highlight slides across the surface with the head, exactly "
            + "as reported, and under MultiPass it is a DIFFERENT image in each eye — which is what "
            + "got the wall dissolve parked permanently. GloomhavenVR/EnvPuddle was disqualified "
            + "for the same reason (it builds its mirror images out of reflect(-V, N)). Get the "
            + "sparkle from the scrolling NORMALS against a fixed light direction instead; that is "
            + "what GloomhavenVR/WaterVR does and it is per-eye identical by construction.");
    }

    // ---------------------------------------------------------------------------------------
    //  2b. NO VERTEX DISPLACEMENT, and no dial that could add one.
    // ---------------------------------------------------------------------------------------
    private static void WaterShaderDisplacesNoVertex(Harness t, string? shaderPath)
    {
        if (shaderPath == null)
            return;

        string text = File.ReadAllText(shaderPath);

        // The one call the vertex program is allowed to make with the incoming position, written
        // out so that a displacement — `v.vertex.y += ...` before it, or a modified expression
        // inside it — cannot pass.
        t.True(Regex.IsMatch(text, @"UnityObjectToClipPos\(\s*v\.vertex\s*\)"),
            $"'{WaterOwnSurface.FilmShaderName}' must hand v.vertex STRAIGHT to "
            + "UnityObjectToClipPos. Unity culls a renderer against its MESH's authored bounds, so "
            + "geometry a vertex program pushes outside them is still culled: a displaced water "
            + "surface vanishes as you walk up to it and, under MultiPass, vanishes in ONE EYE "
            + "FIRST because the two eye frustums differ. This project has already lost a build to "
            + "exactly that.");

        // And no dial anyone could turn up. The tileset's own _addSphericalWaves is 0, so the
        // water it authored does not displace either — a displacement property here would be a
        // look the tileset never had AND the culling trap above, in one.
        foreach (string banned in new[] { "_VertexOffsetWaves", "_addSphericalWaves", "_WaveHeight" })
        {
            t.True(!Regex.IsMatch(
                       text, @"(?m)^\s*(\[[^\]]*\]\s*)*" + Regex.Escape(banned) + @"\s*\("),
                $"'{WaterOwnSurface.FilmShaderName}' declares a vertex-displacement property "
                + $"'{banned}'. The tileset's own material authors _addSphericalWaves = 0, so its "
                + "water does not displace and neither may this; and a displacement dial is the "
                + "culling trap above waiting for somebody to raise it off zero.");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  3a. Every property the driver writes is one the shader actually DECLARES.
    // ---------------------------------------------------------------------------------------
    private static void ShaderDeclaresProperties(
        Harness t, string name, string? shaderPath, string[] written)
    {
        if (shaderPath == null)
            return;

        string text = File.ReadAllText(shaderPath);
        foreach (string p in written)
        {
            // `_Name (` is how a Unity Properties block declares one. A property the shader does
            // not declare is a SetFloat that Unity silently discards, which is indistinguishable
            // in the log from a write that landed and did nothing — the exact trap this whole
            // module was built out of.
            t.True(Regex.IsMatch(text, @"(?m)^\s*(\[[^\]]*\]\s*)*" + Regex.Escape(p) + @"\s*\("),
                $"the driver writes '{p}' onto the replacement film material, but "
                + $"'{name}' does not declare a property by that name. "
                + "Material.SetFloat/SetColor on an undeclared property is a silent no-op, so the "
                + "film would draw with the shader's authored defaults and nothing would say so.");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  3b. THE BLEND. Pinned against UnityEngine.Rendering itself, not against a remembered digit.
    // ---------------------------------------------------------------------------------------
    private static void RenderStateIsNotAdditive(Harness t)
    {
        t.Equal((float)BlendMode.SrcAlpha, WaterOwnSurface.SrcBlendSrcAlpha,
            "the film's source blend factor must be BlendMode.SrcAlpha");
        t.Equal((float)BlendMode.OneMinusSrcAlpha, WaterOwnSurface.DstBlendOneMinusSrcAlpha,
            "the film's destination blend factor must be BlendMode.OneMinusSrcAlpha");

        // THE ASSERTION THIS FILE EXISTS FOR, stated in its own terms rather than as a corollary.
        bool additive = Math.Abs(WaterOwnSurface.SrcBlendSrcAlpha - (float)BlendMode.One) < 1e-6f
                        && Math.Abs(WaterOwnSurface.DstBlendOneMinusSrcAlpha - (float)BlendMode.One)
                           < 1e-6f;
        t.True(!additive,
            "the replacement water film must never blend ADDITIVELY. Blend One One lays the film's "
            + "colour on top of the stone instead of over it, which makes the water hexes LIGHTER "
            + "than the floor around them — that is a pixel-for-pixel re-creation of the defect in "
            + ".planning/debug/spiegeltiles.jpg, produced this time by the mod's own shader.");

        t.Equal((float)CullMode.Off, WaterOwnSurface.CullOff,
            "the replacement film must be TWO-SIDED. Four meshes have shipped in this project wound "
            + "against the side they are seen from and one was invisible for ten builds; a water "
            + "quad across a VR table is seen from above and from the side, and a film that "
            + "vanished from one seat would read as exactly the bug this round is ending.");

        t.Equal((float)CompareFunction.LessEqual, WaterOwnSurface.ZTestLessEqual,
            "the replacement film's ZTest must be LessEqual — it is water lying on a basin bed, not "
            + "an overlay that draws through the world");

        t.True(Math.Abs(WaterOwnSurface.ZWriteOff) < 1e-6f,
            "the replacement film must NOT write depth: it is a transparent sheet drawn after the "
            + "opaque basin, and writing depth would let it occlude whatever sorts after it");
    }

    // ---------------------------------------------------------------------------------------
    //  4. The look is the TILESET's — hue verbatim, alpha capped, never raised.
    // ---------------------------------------------------------------------------------------
    private static void FilmColourIsTheTilesets(Harness t)
    {
        bool ok = WaterOwnSurface.TryBuildFilmColour(
            AuthoredTint, DefaultOpacity, out Color v, out string why);
        t.True(ok, $"the authored water tint must yield a film colour, not a refusal: {why}");

        // THE HUE IS NOT OURS TO CHOOSE. This is the write that stops the replacement from being a
        // look the tileset never had — the same reason WaterEdgeBand repaints the band in the body
        // hue rather than in a colour of its own.
        t.True(ok && Math.Abs(v.r - AuthoredTint.r) < 1e-6f
               && Math.Abs(v.g - AuthoredTint.g) < 1e-6f
               && Math.Abs(v.b - AuthoredTint.b) < 1e-6f,
            $"the film must carry the AUTHORED _Color_Tint hue RGB({AuthoredTint.r:0.###},"
            + $"{AuthoredTint.g:0.###},{AuthoredTint.b:0.###}) verbatim, got RGB({v.r:0.###},"
            + $"{v.g:0.###},{v.b:0.###}). A hue this module chose for itself would be a pool colour "
            + "the tileset never authored, and the report is already about a surface that does not "
            + "look like the game's.");

        t.True(ok && Math.Abs(v.a - DefaultOpacity) < 1e-6f,
            $"at the shipped [Water] Opacity {DefaultOpacity:0.###} the film's alpha must be the "
            + $"cap (the tileset authors {AuthoredTint.a:0.###}), got {v.a:0.###}");

        // A cap ABOVE the authored alpha must not raise it — the dial can only ever hide less of
        // the floor than the tileset intended, never more.
        bool ok2 = WaterOwnSurface.TryBuildFilmColour(AuthoredTint, 1f, out Color v2, out _);
        t.True(ok2 && Math.Abs(v2.a - AuthoredTint.a) < 1e-6f,
            $"[Water] Opacity at 1.0 must leave the film at the AUTHORED alpha "
            + $"{AuthoredTint.a:0.###}, got {v2.a:0.###} — the cap is a ceiling, never a target");

        // The reason string is the only place a reader of the hardware log can check the
        // derivation, so it has to carry both numbers.
        t.True(ok && why.IndexOf("_Color_Tint", StringComparison.Ordinal) >= 0,
            "the film colour's reason string goes verbatim into the OWN SURFACE log block and must "
            + "name the authored property it was derived from. Got: " + why);

        Refuses(t, "NaN tint", new Color(float.NaN, 0.3f, 0.1f, 0.7f), DefaultOpacity);
        Refuses(t, "Infinite alpha", new Color(0.2f, 0.3f, 0.1f, float.PositiveInfinity),
                DefaultOpacity);
        Refuses(t, "NaN opacity dial", AuthoredTint, float.NaN);
    }

    // ---------------------------------------------------------------------------------------
    //  4b. THE INVARIANT, swept. Never brighter, never more opaque, always finite.
    // ---------------------------------------------------------------------------------------
    private static void NeverBrighterSweep(Harness t)
    {
        int checks = 0;
        for (int a = 0; a <= 20; a++)
        {
            float authoredAlpha = a / 20f;
            var authored = new Color(0.195f, 0.311f, 0.131f, authoredAlpha);
            for (int c = -2; c <= 22; c++)
            {
                float cap = c / 20f;
                if (!WaterOwnSurface.TryBuildFilmColour(
                        authored, cap, out Color v, out string why))
                    continue;
                checks++;
                t.True(v.a <= authoredAlpha + 1e-6f,
                    $"the film alpha may never exceed the authored one: authored "
                    + $"{authoredAlpha:0.###}, cap {cap:0.###} -> {v.a:0.###} ({why})");
                t.True(v.a >= -1e-6f && v.a <= 1f + 1e-6f,
                    $"the film alpha must stay in [0,1]: {v.a:0.###} from cap {cap:0.###}");
                t.True(Math.Abs(v.r + v.g + v.b - (authored.r + authored.g + authored.b)) < 1e-6f,
                    $"the film may never be brighter than the authored body: got RGB sum "
                    + $"{v.r + v.g + v.b:0.###} against {authored.r + authored.g + authored.b:0.###}");
                t.True(!float.IsNaN(v.a) && !float.IsInfinity(v.a),
                    $"the film alpha must be finite: {v.a}");
            }
        }
        t.True(checks > 400,
            $"the water film colour sweep actually ran (only {checks} colours were built — a "
            + "gating change that refused everything would make this file assert nothing)");
    }

    // ---------------------------------------------------------------------------------------
    //  4c. THE ANIMATION comes from the tileset's own speeds, and the dial only scales it.
    // ---------------------------------------------------------------------------------------
    private static void ScrollRateIsTheTilesets(Harness t)
    {
        Vector4 noise = WaterOwnSurface.AuthoredNoiseSpeed;

        // The measured pair, at the shipped dial. These are the numbers the hardware census read
        // off TERRAIN_GEN_WaterPlane_Crypt_Mat, resolved by the one rule ScrollRate states.
        Vector4 a = WaterOwnSurface.ScrollRate(WaterOwnSurface.AuthoredSpeedA, noise, 1f);
        Vector4 b = WaterOwnSurface.ScrollRate(WaterOwnSurface.AuthoredSpeedB, noise, 1f);
        t.True(Near(a.x, 0.60f) && Near(a.y, 0.60f),
            $"layer A's authored (1.00, 1.00, 0.60, 0.00) must resolve to 0.60 UV/s on both axes, "
            + $"got ({a.x:0.###},{a.y:0.###}). If this changed, the water is no longer moving at "
            + "the rate the tileset authored and the only symptom is a pool that looks wrong.");
        t.True(Near(b.x, 0.50f) && Near(b.y, 1.00f),
            $"layer B's authored (0.50, 1.00, 1.00, 0.00) must resolve to (0.50, 1.00) UV/s, got "
            + $"({b.x:0.###},{b.y:0.###})");

        // THE TWO LAYERS MUST NOT AGREE. Two normal layers scrolling at the same rate over the
        // same tiling are one layer: the interference that makes the crests break up and the
        // glints travel is the DIFFERENCE between them, and a change that collapsed the two would
        // read as "the water moves but looks like a sliding texture".
        t.True(!(Near(a.x, b.x) && Near(a.y, b.y)),
            $"the two ripple layers must scroll at DIFFERENT rates — got A ({a.x:0.###},"
            + $"{a.y:0.###}) and B ({b.x:0.###},{b.y:0.###}). Two layers moving together are one "
            + "layer, and the whole reason there are two is that their interference is what makes "
            + "the surface read as water rather than as a scrolling texture.");

        // THE DIAL IS A PURE SCALE. It may change how fast, never the direction or the ratio.
        Vector4 half = WaterOwnSurface.ScrollRate(WaterOwnSurface.AuthoredSpeedA, noise, 0.5f);
        t.True(Near(half.x, a.x * 0.5f) && Near(half.y, a.y * 0.5f),
            "[Water] RippleSpeed must scale both axes equally — a dial that changed the RATIO "
            + "would change the direction the water runs, which is not what it is for");

        Vector4 stopped = WaterOwnSurface.ScrollRate(WaterOwnSurface.AuthoredSpeedA, noise, 0f);
        t.True(Near(stopped.x, 0f) && Near(stopped.y, 0f),
            "[Water] RippleSpeed at 0 must freeze the surface exactly, not merely slow it");

        // The shared clock scale multiplies both layers.
        Vector4 fast = WaterOwnSurface.ScrollRate(
            WaterOwnSurface.AuthoredSpeedA, new Vector4(2f, 1f, 1f, 0f), 1f);
        t.True(Near(fast.x, a.x * 2f) && Near(fast.y, a.y * 2f),
            "_WaterNoiseSpeed.x is the tileset's own shared clock scale and must multiply the rate");

        // REFUSALS, and every one of them keeps the water MOVING. A layer frozen by a component
        // nobody can read, or by a garbage dial, would look exactly like this shader failing.
        Vector4 zeroZ = WaterOwnSurface.ScrollRate(new Vector4(1f, 1f, 0f, 0f), noise, 1f);
        t.True(Near(zeroZ.x, 1f) && Near(zeroZ.y, 1f),
            "a .z of zero must be treated as 1 rather than freezing the layer — a tileset that "
            + "meant 'no motion' would have authored .xy at zero, and a layer stopped by a "
            + "component whose meaning cannot be read from a compiled shader is indistinguishable "
            + "from a bug");
        Vector4 nanDial = WaterOwnSurface.ScrollRate(WaterOwnSurface.AuthoredSpeedA, noise,
                                                    float.NaN);
        t.True(Near(nanDial.x, a.x) && Near(nanDial.y, a.y),
            "a non-finite [Water] RippleSpeed must fall back to 1, not to NaN — a NaN scroll rate "
            + "is a surface nobody can predict");
        Vector4 negDial = WaterOwnSurface.ScrollRate(WaterOwnSurface.AuthoredSpeedA, noise, -3f);
        t.True(Near(negDial.x, 0f) && Near(negDial.y, 0f),
            "a negative [Water] RippleSpeed must clamp to 0, never run the water backwards at "
            + "three times speed");

        // The z/w channels are never written: the shader adds .xy to a UV and reads nothing else.
        t.True(Near(a.z, 0f) && Near(a.w, 0f),
            "the resolved scroll rate's z and w must be zero — the shader adds .xy to the tiled UV "
            + "and reads nothing else, so a stray value there is a silent no-op waiting to be read "
            + "as a setting");
    }

    // ---------------------------------------------------------------------------------------
    //  4d. THE RIPPLE STRENGTH is the tileset's, normalised, and bounded at both ends.
    // ---------------------------------------------------------------------------------------
    private static void NormalStrengthIsBounded(Harness t)
    {
        float authored = WaterOwnSurface.NormalStrength(WaterOwnSurface.AuthoredNormalStrength);
        t.True(Near(authored, WaterOwnSurface.NominalRippleStrength),
            $"the measured _DetailOpacityBaseNormalStr (5.00, 5.00, 0.00, 0.00) must map to the "
            + $"nominal ripple strength {WaterOwnSurface.NominalRippleStrength:0.###}, got "
            + $"{authored:0.###}. That mapping is what makes the authored number a RELATIVE "
            + "statement — the receiving expression in the game's shader cannot be read, because "
            + "the game's shaders ship compiled and there is no install to open them with.");

        // A tileset authoring half of it gets half the ripple: the pass-through is proportional.
        float half = WaterOwnSurface.NormalStrength(new Vector4(2.5f, 2.5f, 0f, 0f));
        t.True(Near(half, WaterOwnSurface.NominalRippleStrength * 0.5f),
            $"half the authored strength must give half the ripple, got {half:0.###}");

        // AND IT IS CAPPED, which is a comfort limit as much as a look one: past the ceiling the
        // blended normals tilt far enough that the glints become per-pixel noise, and per-pixel
        // noise on a 90 Hz headset aliases into a crawling carpet.
        float huge = WaterOwnSurface.NormalStrength(new Vector4(1000f, 0f, 0f, 0f));
        t.True(huge <= WaterOwnSurface.MaxRippleStrength + 1e-6f,
            $"the ripple strength must never exceed {WaterOwnSurface.MaxRippleStrength:0.###}, got "
            + $"{huge:0.###} — beyond it the surface is more slope than water and the sparkle "
            + "aliases into a crawling carpet, which is a discomfort report and not merely an ugly "
            + "one");

        // ...and a missing or garbage reading leaves the water RIPPLING rather than flattening it,
        // because a flat sheet is precisely the outcome this build exists to end.
        foreach (Vector4 bad in new[]
                 {
                     new Vector4(0f, 0f, 0f, 0f),
                     new Vector4(float.NaN, 5f, 0f, 0f),
                     new Vector4(-4f, 5f, 0f, 0f),
                 })
        {
            float s = WaterOwnSurface.NormalStrength(bad);
            t.True(s > 0f && s <= WaterOwnSurface.MaxRippleStrength + 1e-6f,
                $"an unusable _DetailOpacityBaseNormalStr {bad} must still leave the water "
                + $"rippling (got {s:0.###}) — a still sheet is the exact look ModBuild 162 was "
                + "asked to stop being");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  4e. THE LIGHT DIRECTION — the only direction in the shader, and never a degenerate one.
    // ---------------------------------------------------------------------------------------
    private static void LightDirectionIsSaneOrRefused(Harness t)
    {
        // No scene light: the fixed constant, and it must be usable — a light lying in the water's
        // own plane produces no glint anywhere.
        bool used = WaterOwnSurface.TryBuildLightDirection(
            Vector3.zero, false, out Vector4 none, out string why);
        t.True(!used, "with no scene light the fixed constant must be reported as such: " + why);
        t.True(none.z >= WaterOwnSurface.MinLightElevation,
            $"the fixed light constant must stand above the water's own plane, got z {none.z:0.###}"
            + $" against the {WaterOwnSurface.MinLightElevation:0.###} floor — a light at or below "
            + "the surface leaves sparks on the steepest slopes and nothing else");
        t.True(Near(new Vector3(none.x, none.y, none.z).magnitude, 1f, 1e-3f),
            "the fixed light constant must be a unit vector");

        // A light overhead: the swizzle puts world +Y on the surface normal.
        used = WaterOwnSurface.TryBuildLightDirection(
            new Vector3(0f, 1f, 0f), true, out Vector4 up, out why);
        t.True(used, "a light straight overhead must be used: " + why);
        t.True(Near(up.x, 0f) && Near(up.y, 0f) && Near(up.z, 1f),
            $"world +Y must map to surface-local +Z, got ({up.x:0.##},{up.y:0.##},{up.z:0.##})");

        // ...and the horizontal axes swap, because the frame is (U, V, surface normal) on a
        // horizontal quad. A transposed swizzle would light the water from the wrong side of the
        // room and nothing in the picture would say so.
        used = WaterOwnSurface.TryBuildLightDirection(
            new Vector3(0.6f, 0.5f, 0.6244998f).normalized, true, out Vector4 lean, out why);
        t.True(used, "an ordinary elevated light must be used: " + why);
        t.True(lean.z > 0f && Near(new Vector3(lean.x, lean.y, lean.z).magnitude, 1f, 1e-3f),
            "the resolved light direction must be a unit vector standing above the surface");

        // REFUSALS. Each one must fall back to the constant rather than to a degenerate vector: a
        // zero _LightDir is a black, still film, i.e. worse than the bug.
        foreach ((Vector3 dir, string what) in new[]
                 {
                     (new Vector3(1f, 0f, 0f), "a light lying in the water's own plane"),
                     (new Vector3(0f, -1f, 0f), "a light UNDER the water"),
                     (new Vector3(float.NaN, 1f, 0f), "a non-finite light direction"),
                     (Vector3.zero, "a zero-length light direction"),
                 })
        {
            bool ok = WaterOwnSurface.TryBuildLightDirection(
                dir, true, out Vector4 v, out string reason);
            t.True(!ok, $"{what} must be refused; it was used instead ({reason})");
            t.True(Near(v.x, WaterOwnSurface.DefaultLightLocal.x)
                   && Near(v.y, WaterOwnSurface.DefaultLightLocal.y)
                   && Near(v.z, WaterOwnSurface.DefaultLightLocal.z),
                $"{what} must fall back to the fixed constant, got ({v.x:0.##},{v.y:0.##},"
                + $"{v.z:0.##}) — a degenerate light direction is a black, still film, which is "
                + "worse than the defect this whole module is fixing");
            t.True(!string.IsNullOrEmpty(reason),
                $"{what} must carry a reason for the hardware log");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  5. SOURCE LINT: the water is never hidden. User ruling 2026-08-18.
    // ---------------------------------------------------------------------------------------
    private static void WaterNeverHidesARenderer(Harness t, string repoRoot)
    {
        string dir = Path.Combine(repoRoot, WaterSrcRelDir.Replace('/', Path.DirectorySeparatorChar));
        t.True(Directory.Exists(dir), $"the water module's directory exists at '{dir}'");
        if (!Directory.Exists(dir))
            return;

        string[] files = Directory.GetFiles(dir, "Water*.cs", SearchOption.TopDirectoryOnly);
        t.True(files.Length >= 3,
            $"found {files.Length} Water*.cs files under {WaterSrcRelDir}; the module is at least "
            + "WaterTerrainVR, WaterReflectionCaps, WaterEdgeBand and WaterOwnSurface. A sudden "
            + "drop means this lint stopped looking at the driver.");

        var offenders = new List<string>();
        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal))
                    continue;
                if (Regex.IsMatch(lines[i], @"\.enabled\s*=\s*(true|false)"))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }
        t.Equal(0, offenders.Count,
            "the water module assigns a renderer/component `enabled` at: "
            + string.Join(", ", offenders)
            + ". STANDING USER RULING, 2026-08-18, verbatim: \"Das Wasser soll auf jeden Fall "
            + "dargestellt werden - aber eben in einer VR-freundlichen Variante. Einfach ausblenden "
            + "ist keine Option.\" ModBuild 158 hid these quads and the user saw them flickering "
            + "back in, because Apparance destroys and re-instantiates them constantly and the "
            + "fresh instances arrive enabled. The whole design of this module — material "
            + "instances, never visibility — depends on nothing here writing that field.");
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>The .shader file under the bundle that DECLARES <paramref name="name"/>, or null.
    /// Found by declaration rather than by path so that moving the file inside the bundle does not
    /// fail this test for the wrong reason — BundledShaderVectors is what pins the path.</summary>
    private static string? FindShaderSource(string repoRoot, string name)
    {
        string root = Path.Combine(repoRoot, BundleRelRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root))
            return null;
        foreach (string f in Directory.GetFiles(root, "*.shader", SearchOption.AllDirectories))
        {
            Match m = ShaderDecl.Match(File.ReadAllText(f));
            if (m.Success && string.Equals(m.Groups[1].Value, name, StringComparison.Ordinal))
                return f;
        }
        return null;
    }

    private static bool Near(float a, float b, float eps = 1e-5f) => Math.Abs(a - b) < eps;

    private static void Refuses(Harness t, string what, Color authored, float cap)
    {
        bool ok = WaterOwnSurface.TryBuildFilmColour(authored, cap, out _, out string reason);
        t.True(!ok,
            $"the film colour '{what}' must be REFUSED rather than written — a non-finite value "
            + "reaching a material is a surface nobody can predict, and the honest fallback is to "
            + "leave the renderer on the game's own water");
        t.True(!string.IsNullOrEmpty(reason),
            $"the film colour refusal '{what}' must carry a reason for the hardware log");
    }
}
