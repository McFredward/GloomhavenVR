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
// he was pointing at — was then closed by the diagnostic paint on hardware: "Die debug farbe
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
//   1b. THE DISPLACEMENT OUTRUNNING ITS BOUNDS. Since ModBuild 164 the film DOES displace — the
//      ruling was "Nicht nur 'calm' sondern auch wirklich 3D wellen einbauen. Aktuell waren es nur
//      weiße streifen auf einer flachen Oberfläche", and no shading term can give a sheet relief.
//      Unity culls a renderer against its BOUNDS and cannot see a vertex program — nor a domain
//      one — so displaced geometry vanishes as you approach and, under MultiPass, vanishes in ONE
//      EYE FIRST; this project has already lost a build to that. Checks 2b and 2f pin the three
//      things that make the payment exact: the displacement is vertical and a function of world
//      position and time only, its amplitude is capped by a declared Range, and Renderer.localBounds
//      is padded by that same cap and reset on release.
//
//   1c. THE GEOMETRY NOT ARRIVING AT ALL, WHICH IS WHAT MODBUILD 164 SHIPPED. That build got its
//      vertices by swapping in a subdivided COPY of the film's mesh; the game imports its meshes
//      without Read/Write, so the copy could never be built, and the census's own words for it were
//      "MESH SWAP: no film mesh handled yet" — an intention, printed before the code that would
//      have set it had ever run. The subdivision is now the shader's own hull/domain pair, which
//      needs no CPU access to anything, and check 2c pins that the stages are declared AND that
//      their factor is a plain uniform: a distance-scaled factor subdivides differently in each
//      MultiPass eye, which is the stereo defect arriving through the geometry instead of the
//      shading.
//
//   1d. THE FIELD REPEATING ON THE TILE LATTICE. "Aktuell scheint die Animation bei jedem tile
//      identisch zu sein." The films sit on a measured 1.73 x 1.998 m lattice and ModBuild 164 ran
//      a single 1.1 m wave, which is within 6% of repeating on it. Check 2e pins
//      WaterOwnSurface.LatticeMismatch — the distance of the WORST of the four components from
//      repeating — above a tenth of a cycle, and check 2d pins that the shader's own table and the
//      C# one that number is computed from are the same field.
//
//   2. THE BLEND GOING ADDITIVE. Overlay exposes its blend factors AS PROPERTIES, and the driver
//      writes them. `Blend One One` is additive: it would lay the film's colour ON TOP of the stone
//      and make the hexes LIGHTER than their surroundings — a pixel-for-pixel re-creation of the
//      photographed defect, produced this time by the mod's own shader. Check 3 pins the pair
//      against UnityEngine.Rendering.BlendMode itself, so a transposed digit cannot pass.
//
//   3. THE LOOK CEASING TO BE THE TILESET'S. The film's hue is the authored _Color_Tint verbatim
//      and its alpha is min(authored, WaterSettings.Opacity). A change that let either rise arrives as
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
    private const string WaterSrcRelDir = "src/GloomhavenVR/Core/Water";

    /// <summary>The film's authored body tint, read off the live material on hardware
    /// (LogOutput.log:1018). Every number this file compares against is measured.</summary>
    private static readonly Color AuthoredTint = new(0.195f, 0.311f, 0.131f, 0.737f);

    /// <summary><c>WaterSettings.Opacity</c>'s shipped value, READ FROM THE SHIPPED CONSTANT.</summary>
    private const float DefaultOpacity = WaterOwnSurface.ShippedOpacity;

    /// <summary>
    /// The shipped SWELL rate — the clock the relief bobs on.
    ///
    /// <para>THIS USED TO BE THE TEST'S OWN COPY OF THE NUMBER, AND THAT IS HOW THE FREEZE SHIPPED.
    /// It read <c>private const float ShippedSpeedDial = 0.0175f</c>. ModBuild 168 halved the real
    /// dial to 0.00875 and this copy was never touched, so <see cref="SpeedDialIsOneClock"/> went
    /// on asserting "the shipped bob period is inside 50..80 s" — and passing — while the build
    /// actually shipped 126 s and the user reported "gar keine Animation ... komplett
    /// stillstehend/freezed". A gate that keeps its own copy of the value it is gating is not a
    /// gate. Both now come from <c>WaterOwnSurface</c>, which is where the driver reads them.</para>
    /// </summary>
    private const float ShippedSwellDial = WaterOwnSurface.ShippedSwellSpeed;

    /// <summary>The shipped RIPPLE rate — the crossfade clock, separate from the swell's since
    /// ModBuild 170. <inheritdoc cref="ShippedSwellDial"/></summary>
    private const float ShippedRippleDial = WaterOwnSurface.ShippedRippleSpeed;

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
        WaterShaderDisplacementIsBounded(t, waterPath);
        TessellationIsFixedAndFactored(t, waterPath);
        SwellTableMatchesTheShader(t, waterPath);
        SwellCannotRepeatOnTheTileLattice(t);
        DisplacedBoundsArePaddedAndGivenBack(t, repoRoot);
        RenderStateIsNotAdditive(t);
        FilmColourIsTheTilesets(t);
        NeverBrighterSweep(t);
        PatternNeverTranslates(t);
        NoTranslationTermSurvives(t, waterPath, repoRoot);
        SpeedDialIsOneClock(t);
        SwellStaysWithinItsPhysicalBounds(t);
        TilingsAreTamedAndWeighted(t);
        SwellAmplitudeIsScaledAndCapped(t);
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

        // THE SWEEP FOLLOWS THE #includes. Since ModBuild 165 WaterVR's whole program lives in
        // WaterVR.cginc — the .shader is two SubShaders that include it, one tessellated and one
        // not — so a lint that only read the .shader would be reading the property table and
        // nothing else. Every hull, domain, vertex and fragment stage is inside the include, and
        // that is exactly where a view vector would be added.
        string text = ExpandedShaderSource(shaderPath);
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
    //  2b. THE DISPLACEMENT IS BOUNDED, VERTICAL, AND A FUNCTION OF POSITION AND TIME ONLY.
    // ---------------------------------------------------------------------------------------
    //  Through ModBuild 163 this check pinned that the vertex program handed v.vertex STRAIGHT to
    //  UnityObjectToClipPos, i.e. that the shader displaced nothing at all. That answered the
    //  culling hazard by avoiding it, and the surface it produced was reported as "nur weiße
    //  streifen auf einer flachen Oberfläche"; the standing ruling is now "Nicht nur 'calm'
    //  sondern auch wirklich 3D wellen einbauen". So the geometry moves, and what has to be
    //  pinned instead is the three properties that make the hazard PAYABLE:
    //
    //    * the displacement is a function of WORLD POSITION AND TIME ONLY (never the eye, or the
    //      two MultiPass passes would displace differently and the surface would be per-eye
    //      rivalrous — the very defect this module exists to remove),
    //    * it is VERTICAL and BOUNDED by a declared Range, so the swept volume is the rest bounds
    //      plus a segment and a constant pad is EXACT rather than a guess,
    //    * and the pad is bigger than the amplitude it is paying for, while the amplitude is
    //      smaller than the gap to the basin bed underneath the film.
    private static void WaterShaderDisplacementIsBounded(Harness t, string? shaderPath)
    {
        if (shaderPath == null)
            return;

        string text = ExpandedShaderSource(shaderPath);

        // THE DISPLACEMENT ITSELF. World position in, world Y out — written literally so that a
        // horizontal term (which would slide the ripple's own coordinate under it) or a term built
        // from anything but position and time cannot pass unnoticed.
        t.True(Regex.IsMatch(text, @"wp\.y\s*\+=\s*GhvrSwell\(\s*wp\.xz\s*,\s*t\s*\)\.x"),
            $"'{WaterOwnSurface.FilmShaderName}' must displace its vertices ONLY along world Y and "
            + "ONLY by GhvrSwell(worldXZ, t). A horizontal component would move the coordinate the "
            + "fragment re-evaluates the wave at, so the shading would no longer match the "
            + "geometry; and a displacement that is not a pure function of position and time is a "
            + "different surface in each MultiPass eye.");

        // ...AND THE FRAGMENT SHADES THE SAME FUNCTION. Relief lit as though it were flat is the
        // reported defect, not the fix, and the only way to be sure the light agrees with the
        // geometry is that both come out of one expression.
        t.True(Regex.IsMatch(text, @"GhvrSwell\(\s*i\.uv\.zw\s*,\s*t\s*\)"),
            $"'{WaterOwnSurface.FilmShaderName}' must take its shading normal from the ANALYTIC "
            + "gradient of the same GhvrSwell that displaced the vertex, evaluated at the same "
            + "world XZ. A surface whose light does not agree with its relief is 'weiße streifen "
            + "auf einer flachen Oberfläche' with extra vertices.");

        // THE BOUND, declared where the engine can enforce it: a Range, not a bare Float. The
        // driver's own clamp is WaterOwnSurface.SwellAmplitude, and this is the second lock —
        // a material authored by hand, or a future dial, cannot exceed it either.
        Match amp = Regex.Match(
            text,
            @"(?m)^\s*" + Regex.Escape(WaterOwnSurface.SwellAmpProperty)
            + @"\s*\(.*Range\(\s*0\s*,\s*(?<max>[0-9.]+)\s*\)");
        t.True(amp.Success,
            $"'{WaterOwnSurface.FilmShaderName}' must declare {WaterOwnSurface.SwellAmpProperty} "
            + "as a Range starting at 0, so the peak displacement the mesh bounds were padded for "
            + "is a number the engine itself clamps. A bare Float is a dial that can outrun its "
            + "own bounds, which is the culling trap arriving one config edit later.");
        if (amp.Success)
        {
            float max = float.Parse(amp.Groups["max"].Value,
                                    System.Globalization.CultureInfo.InvariantCulture);
            t.True(Math.Abs(max - WaterOwnSurface.MaxSwellAmplitude) < 1e-6f,
                $"the shader's {WaterOwnSurface.SwellAmpProperty} Range tops out at {max:0.###} "
                + $"but the driver pads the mesh bounds for "
                + $"{WaterOwnSurface.MaxSwellAmplitude:0.###} — the two must be the same number, "
                + "because the pad is only exact while it is the ceiling on the thing it pays for");
        }

        // AND THE CEILING CLEARS THE BASIN BED. The hardware FLOOR CENSUS reads the film at
        // y[0.0..0.0] with TERRAIN_Crypt_Water_02_Base at y[-0.34..-0.09] under it. A trough that
        // reached the stone would z-fight, which reads as the pool tearing open.
        t.True(WaterOwnSurface.MaxSwellAmplitude < WaterOwnSurface.FilmToBedGap,
            $"the swell's ceiling {WaterOwnSurface.MaxSwellAmplitude:0.###} must stay inside the "
            + $"{WaterOwnSurface.FilmToBedGap:0.###} the census measured between the film and the "
            + "basin bed beneath it — the wave is symmetric about the authored plane, so the "
            + "ceiling is exactly how deep a trough goes.");
    }

    // ---------------------------------------------------------------------------------------
    //  2c. THE GEOMETRY THE SWELL NEEDS COMES FROM THE GPU, AND ITS FACTOR IS NOT THE CAMERA'S.
    // ---------------------------------------------------------------------------------------
    //  THE FAILURE THIS REPLACES. ModBuild 164 got its geometry by handing the film a SUBDIVIDED
    //  COPY of its own mesh, and the copy was never built once: the game imports its meshes without
    //  Read/Write, so there was no index buffer to subdivide, and the whole round shipped a
    //  displacement shader with nothing to displace. The census said "MESH SWAP: no film mesh
    //  handled yet" and that read as a timing note.
    //
    //  The subdivision is now the shader's own hull/domain pair, which needs no CPU access to
    //  anything — and the ONE property of it that can silently re-open the stereo defect is the
    //  tessellation FACTOR, because every tutorial computes it from the distance to the camera.
    //  Under MultiPass that is a different mesh in each eye.
    private static void TessellationIsFixedAndFactored(Harness t, string? shaderPath)
    {
        if (shaderPath == null)
            return;
        string text = ExpandedShaderSource(shaderPath);

        // THE STAGES EXIST. A shader that quietly lost its #pragma hull would compile, draw, and
        // look exactly like ModBuild 164 — relief in the maths and none on screen.
        foreach (string stage in new[] { "#pragma hull", "#pragma domain", "#pragma target 4.6" })
        {
            t.True(text.IndexOf(stage, StringComparison.Ordinal) >= 0,
                $"'{WaterOwnSurface.FilmShaderName}' must declare '{stage}'. The film's own mesh is "
                + "33 vertices over a 1.73 x 2.0 m hex (hardware census), which samples the swell "
                + "about every 0.4 m; without the tessellator the displacement is present in the "
                + "maths and invisible on screen, which is precisely what ModBuild 164 shipped.");
        }

        // THE FACTOR READS NOTHING BUT THE UNIFORM. This is the check that matters: a
        // distance-scaled factor is a view dependency in the GEOMETRY, so the two MultiPass eyes
        // would subdivide the same patch differently and sample different crest heights — stereo
        // rivalry along every silhouette, which is the class of defect this module exists to
        // remove. (The camera-position spellings themselves are already banned by check 2; this
        // states the reason in the tessellation's own terms so a reader of the failure knows why a
        // "distance-based LOD" cannot simply be added.)
        Match pc = Regex.Match(
            text, @"GhvrPatchConstant\s*\([^)]*\)\s*\{(?<body>.*?)\n\s*\}", RegexOptions.Singleline);
        t.True(pc.Success,
            $"'{WaterOwnSurface.FilmShaderName}' must have a patch-constant function named "
            + "GhvrPatchConstant — it is the one place a tessellation factor can be computed, so it "
            + "is the one place this lint can watch.");
        if (pc.Success)
        {
            string body = pc.Groups["body"].Value;
            t.True(body.IndexOf(WaterOwnSurface.TessFactorProperty, StringComparison.Ordinal) >= 0,
                "the patch-constant function must take its factor from "
                + WaterOwnSurface.TessFactorProperty + ", which is a uniform");
            foreach (string term in new[] { "Camera", "distance(", "length(", "UnityObjectToClipPos" })
            {
                t.True(body.IndexOf(term, StringComparison.Ordinal) < 0,
                    $"the patch-constant function contains '{term}'. A tessellation factor that "
                    + "depends on where the camera is subdivides the same patch DIFFERENTLY IN EACH "
                    + "MULTIPASS EYE — the two eyes are separate passes 6.4 cm apart — so the crest "
                    + "heights they sample differ and every silhouette becomes stereo-rivalrous. "
                    + "That is the same class of defect as the head-bound reflection this whole "
                    + "module exists to remove. The factor must stay a plain uniform.");
            }
        }

        // AND THE CEILING IS THE SAME NUMBER ON BOTH SIDES. The driver clamps, the shader's Range
        // clamps, and the patch-constant function clamps again; all three have to agree or the
        // triangle budget printed in the census is not the one being drawn.
        Match range = Regex.Match(
            text,
            @"(?m)^\s*" + Regex.Escape(WaterOwnSurface.TessFactorProperty)
            + @"\s*\(.*Range\(\s*1\s*,\s*(?<max>[0-9.]+)\s*\)");
        t.True(range.Success,
            $"'{WaterOwnSurface.FilmShaderName}' must declare {WaterOwnSurface.TessFactorProperty} "
            + "as a Range starting at 1 — a factor below 1 is an undefined tessellation and a bare "
            + "Float is a dial with no ceiling on a headset that is already GPU-bound.");
        if (range.Success)
        {
            float max = float.Parse(range.Groups["max"].Value,
                                    System.Globalization.CultureInfo.InvariantCulture);
            t.True(Math.Abs(max - WaterOwnSurface.MaxTessellationFactor) < 1e-6f,
                $"the shader's {WaterOwnSurface.TessFactorProperty} Range tops out at {max:0.#} but "
                + $"the driver clamps to {WaterOwnSurface.MaxTessellationFactor:0.#}");
        }

        t.True(WaterOwnSurface.TessellationFactor >= 2f
               && WaterOwnSurface.TessellationFactor <= WaterOwnSurface.MaxTessellationFactor,
            $"the shipped tessellation factor {WaterOwnSurface.TessellationFactor:0.#} must be at "
            + "least 2 (a factor of 1 subdivides nothing at all, which is ModBuild 164) and no more "
            + $"than the {WaterOwnSurface.MaxTessellationFactor:0.#} ceiling");

        // THE TARGET EDGE IS A CLAIM ABOUT THE MEASURED MESH, so it is checked against the measured
        // mesh: 33 vertices over the census's 1.73 x 1.998 m hex. A planar patch with V vertices
        // carries about 2V-2 triangles at the low end and rather fewer as a fan; 32 is taken because
        // it is the CONSERVATIVE reading (fewer triangles means a longer authored edge, so a
        // tessellation factor that satisfies this bound satisfies the real mesh too).
        const float AuthoredTriangles = 32f;
        float authoredEdge = Mathf.Sqrt(
            2f * WaterOwnSurface.TileLatticeX * WaterOwnSurface.TileLatticeZ / AuthoredTriangles);
        float tessellated = authoredEdge / WaterOwnSurface.TessellationFactor;
        t.True(tessellated <= WaterOwnSurface.TargetEdgeWU + 1e-3f,
            $"the shipped factor leaves an edge of {tessellated:0.###} world units on the film the "
            + $"census measured, against the {WaterOwnSurface.TargetEdgeWU:0.###} target. Below "
            + "that the swell is sampled too coarsely to have a silhouette, which is the ModBuild "
            + "164 look.");

        // AND THE FALLBACK SUBSHADER STILL DRAWS WATER. A device without shader model 4.6 must get
        // the same wave at a coarser sampling, never a missing pass — an unreachable SubShader list
        // is a magenta pool.
        t.True(Regex.Matches(text, @"(?m)^\s*SubShader\b").Count >= 2,
            $"'{WaterOwnSurface.FilmShaderName}' must declare a second, non-tessellated SubShader. "
            + "A shader whose only SubShader needs shader model 4.6 draws NOTHING on a device that "
            + "lacks it, and 'the pool disappeared' is a worse outcome than 'the pool is flatter'.");
    }

    // ---------------------------------------------------------------------------------------
    //  2d. THE SWELL'S TABLE IS THE SAME ON BOTH SIDES OF THE FENCE.
    // ---------------------------------------------------------------------------------------
    //  The shader carries the four components' directions and wavelength ratios as literals; C#
    //  carries them so LatticeMismatch can compute what they leave, and so the census can print it.
    //  A number tuned on one side only would make the log's LATTICE MISMATCH a statement about a
    //  surface nobody is looking at.
    private static void SwellTableMatchesTheShader(Harness t, string? shaderPath)
    {
        if (shaderPath == null)
            return;
        string text = ExpandedShaderSource(shaderPath);

        MatchCollection calls = Regex.Matches(
            text,
            @"GhvrStanding\(p,\s*t,\s*float2\(\s*(?<dx>-?[0-9.]+),\s*(?<dy>-?[0-9.]+)\),\s*"
            + @"L\s*\*\s*(?<ratio>[0-9.]+),\s*"
            + @"T\s*\*\s*(?<period>[0-9.]+),\s*(?<amp>[0-9.]+)\s*/\s*GHVR_SWELL_ASUM");
        t.Equal(WaterOwnSurface.SwellRatios.Length, calls.Count,
            $"the shader must sum exactly {WaterOwnSurface.SwellRatios.Length} standing components "
            + $"(found {calls.Count}). Fewer is a field with fewer ways to avoid the tile lattice; "
            + "the C# table is what the census's LATTICE MISMATCH number is computed from, so the "
            + "two must be the same field.");
        if (calls.Count != WaterOwnSurface.SwellRatios.Length)
            return;

        for (int i = 0; i < calls.Count; i++)
        {
            float dx = ParseF(calls[i].Groups["dx"].Value);
            float dy = ParseF(calls[i].Groups["dy"].Value);
            float ratio = ParseF(calls[i].Groups["ratio"].Value);
            float rad = WaterOwnSurface.SwellDirections[i] * Mathf.Deg2Rad;
            t.True(Math.Abs(dx - Mathf.Cos(rad)) < 5e-4f && Math.Abs(dy - Mathf.Sin(rad)) < 5e-4f,
                $"swell component {i}: the shader points ({dx:0.####},{dy:0.####}) but "
                + $"WaterOwnSurface.SwellDirections says {WaterOwnSurface.SwellDirections[i]:0.#} "
                + $"degrees = ({Mathf.Cos(rad):0.####},{Mathf.Sin(rad):0.####})");
            t.True(Math.Abs(ratio - WaterOwnSurface.SwellRatios[i]) < 5e-4f,
                $"swell component {i}: the shader scales the wavelength by {ratio:0.#####} but "
                + $"WaterOwnSurface.SwellRatios says {WaterOwnSurface.SwellRatios[i]:0.#####}");

            // THE PERIOD AND THE AMPLITUDE ARE PART OF THE SAME TABLE, and since ModBuild 166 the
            // C# copy is not only read by the census — WaterOwnSurface.SwellHeightAt EVALUATES it,
            // and PatternNeverTranslates measures that evaluation to prove the field does not
            // travel. A mirror that had drifted would make that measurement a statement about a
            // surface nobody is looking at, so every number in the call is checked, not just the
            // two the lattice arithmetic needs.
            float period = ParseF(calls[i].Groups["period"].Value);
            float amp = ParseF(calls[i].Groups["amp"].Value);
            float wantPeriod = Mathf.Sqrt(WaterOwnSurface.SwellRatios[i]);
            t.True(Math.Abs(period - wantPeriod) < 5e-4f,
                $"swell component {i}: the shader scales the period by {period:0.#####} but "
                + $"deep-water dispersion over WaterOwnSurface.SwellRatios says "
                + $"sqrt({WaterOwnSurface.SwellRatios[i]:0.#####}) = {wantPeriod:0.#####}. The six "
                + "bob rates are what leave the sum without a beat; a rounded one is a rhythm.");
            t.True(Math.Abs(amp - WaterOwnSurface.SwellAmplitudes[i]) < 5e-4f,
                $"swell component {i}: the shader weights it {amp:0.#####} but "
                + $"WaterOwnSurface.SwellAmplitudes says "
                + $"{WaterOwnSurface.SwellAmplitudes[i]:0.#####}");
        }

        // AND THE NORMALISER IS THE SUM OF THAT TABLE. _SwellAmp has to MEAN the peak displacement,
        // because Renderer.localBounds is padded by it and a surface that outran its own stated
        // amplitude would be culled at the crests — one eye first, under MultiPass.
        float sum = 0f;
        foreach (float a in WaterOwnSurface.SwellAmplitudes)
            sum += a;
        t.True(Math.Abs(sum - WaterOwnSurface.SwellAmplitudeSum) < 1e-3f,
            $"WaterOwnSurface.SwellAmplitudes sums to {sum:0.#####} but SwellAmplitudeSum says "
            + $"{WaterOwnSurface.SwellAmplitudeSum:0.#####} — the two must agree or the peak "
            + "displacement is not the number the bounds were padded for");
        Match asum = Regex.Match(text, @"#define GHVR_SWELL_ASUM\s+(?<v>[0-9.]+)");
        t.True(asum.Success && Math.Abs(ParseF(asum.Groups["v"].Value)
                                        - WaterOwnSurface.SwellAmplitudeSum) < 1e-3f,
            "the shader's GHVR_SWELL_ASUM must be WaterOwnSurface.SwellAmplitudeSum "
            + $"({WaterOwnSurface.SwellAmplitudeSum:0.#####}); it is what makes |h| <= _SwellAmp an "
            + "exact bound rather than a hope");
    }

    // ---------------------------------------------------------------------------------------
    //  2e. THE FIELD CANNOT DRAW THE SAME FIGURE ON EVERY TILE.
    // ---------------------------------------------------------------------------------------
    //  User, verbatim, on ModBuild 164: "Aktuell scheint die Animation bei jedem tile identisch zu
    //  sein, bring mehr randomness rein! Es soll sich nicht auf jeden tile exakt gleichen was
    //  passiert." The films sit on a 1.73 x 1.998 m lattice, so a component whose wavelength
    //  divides a lattice vector along its own direction looks IDENTICAL on neighbouring tiles.
    //  LatticeMismatch measures the worst of the four; this pins that it stays far from zero, and
    //  it pins it as a NUMBER rather than as a promise about irrational ratios, because a retune of
    //  the wavelength is exactly how the promise would quietly stop holding.
    private static void SwellCannotRepeatOnTheTileLattice(Harness t)
    {
        float shipped = WaterOwnSurface.LatticeMismatch(WaterOwnSurface.SwellWavelength);
        t.True(shipped >= 0.10f,
            $"the shipped swell scores {shipped:0.###} cycles of lattice mismatch, under the 0.10 "
            + "floor. That is the worst of the four components against the "
            + $"{WaterOwnSurface.TileLatticeX:0.##} x {WaterOwnSurface.TileLatticeZ:0.###} m film "
            + "lattice: at 0 the whole field repeats tile for tile, which is the reported defect. "
            + "Move a wavelength or a direction until it is back above 0.10 — and note that this "
            + "cannot be fixed with a per-quad random phase, which would put a step in the surface "
            + "at every tile seam.");

        // ...AND THE OLD VALUE FAILS IT. Without this the floor could be met by a table that never
        // had the problem, and the check would be asserting nothing about the bug it is named for.
        float old = WaterOwnSurface.LatticeMismatch(1.1f);
        t.True(old < shipped,
            $"ModBuild 164's 1.1 m swell must score WORSE than the shipped one (it scores "
            + $"{old:0.###} against {shipped:0.###}). If it does not, this check is not measuring "
            + "the thing the user reported.");

        // THE DIAL CAN MOVE THE WAVELENGTH, AND SOME SETTINGS REALLY ARE WORSE. That is stated as a
        // measurement rather than wished away: the mismatch is the MINIMUM over four components and
        // two lattice vectors, so eight chances to land near a whole cycle, and no fixed set of
        // ratios can keep all of them far away at every scale. What CAN be pinned is that the
        // typical setting is comfortable and the SHIPPED one is deliberately good — and this is why
        // the census prints the live number rather than a claim.
        var swept = new List<float>();
        for (int i = 0; i <= 200; i++)
        {
            float scale = 0.25f + i * (4f - 0.25f) / 200f;
            swept.Add(WaterOwnSurface.LatticeMismatch(WaterOwnSurface.SwellWavelength * scale));
        }
        swept.Sort();
        float median = swept[swept.Count / 2];
        t.True(median >= 0.05f,
            $"the median WaterSettings.WaveScale setting scores {median:0.###} cycles of lattice "
            + "mismatch, under the 0.05 floor. Half the dial handing the user a field that nearly "
            + "repeats tile for tile is a component table whose ratios are not incommensurate "
            + "enough, whatever the shipped default happens to score.");

        // ...and the SHIPPED scale is the best of the settings a user actually types. If a retune
        // ever made 1.0 an unlucky one, this is where it is caught rather than on hardware.
        foreach (float scale in new[] { 0.5f, 0.75f, 1.25f, 1.5f, 2f, 3f, 4f })
        {
            float m = WaterOwnSurface.LatticeMismatch(WaterOwnSurface.SwellWavelength * scale);
            t.True(shipped >= m || m >= 0.10f,
                $"WaterSettings.WaveScale {scale:0.##} scores {m:0.###} against the shipped 1.0's "
                + $"{shipped:0.###}. The shipped default must be at least as good as any round "
                + "setting that beats the 0.10 floor — it is the one the user will actually see.");
        }

        t.True(WaterOwnSurface.LatticeMismatch(0f) == 0f
               && WaterOwnSurface.LatticeMismatch(float.NaN) == 0f,
            "a degenerate wavelength must report the WORST mismatch (0) rather than a comfortable "
            + "number — this value goes into the hardware log and a refusal must never read as a "
            + "pass");
    }

    // ---------------------------------------------------------------------------------------
    //  2f. THE BOUNDS PAD, which is all that is left of the mesh path — and it is the load-bearing
    //      part, because culling cannot see a domain program any more than a vertex one.
    // ---------------------------------------------------------------------------------------
    private static void DisplacedBoundsArePaddedAndGivenBack(Harness t, string repoRoot)
    {
        string path = Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Core", "Water", "WaterTerrainVR.cs");
        t.True(File.Exists(path), $"WaterTerrainVR.cs exists at '{path}'");
        if (!File.Exists(path))
            return;
        string src = File.ReadAllText(path);

        // Reading the source is the only way to assert this without a live Renderer, which no test
        // harness on this machine can build.
        t.True(src.Contains("WaterOwnSurface.MaxSwellAmplitude / scale"),
            "the driver must derive its bounds pad from WaterOwnSurface.MaxSwellAmplitude — the "
            + "CEILING and not the amplitude currently in force, because a dial raised mid-scene "
            + "would otherwise outrun bounds baked for a lower one, and the symptom is the water "
            + "vanishing as you lean in (one eye first, under MultiPass).");
        t.True(Regex.IsMatch(src, @"padded\.Expand\(\s*pad\s*\*\s*2f\s*\)"),
            "the driver must expand the film's local bounds by TWICE the pad, because "
            + "Bounds.Expand grows the SIZE and the pad is per side. Half a pad is a surface whose "
            + "crests are culled exactly when they are highest.");
        t.True(Regex.IsMatch(src, @"r\.localBounds\s*=\s*padded"),
            "the pad must be written to Renderer.localBounds. ModBuild 164 wrote it onto a "
            + "SUBDIVIDED COPY of the mesh instead, and the copy was never built because the game's "
            + "meshes are imported without Read/Write — so the bounds were never padded either, and "
            + "the only reason nobody saw the water vanish is that it was never displaced far "
            + "enough to leave them.");
        t.True(src.Contains("ResetLocalBounds()"),
            "every path that stops wanting the swell must hand the renderer's authored bounds back "
            + "with Renderer.ResetLocalBounds(). A bounds override that outlived the mod would be a "
            + "change to the game's own renderer that the game has no way to undo.");

        // AND THE CENSUS MUST REPORT THE MESH'S READABILITY BEFORE ITS TRIANGLE COUNT. This is the
        // whole lesson of the round: "33 verts / 0 tris" was Mesh.triangles refusing, and it was
        // read as a mesh with no triangles.
        t.True(src.Contains("mesh.isReadable"),
            "the census must read and print Mesh.isReadable for the film. ModBuild 164's line said "
            + "'33 verts / 0 tris', which is not a mesh without triangles — it is Mesh.triangles "
            + "returning empty on a non-readable mesh — and a whole hardware round was spent on the "
            + "hypothesis that number produced.");
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
            $"at the shipped WaterSettings.Opacity {DefaultOpacity:0.###} the film's alpha must be the "
            + $"cap (the tileset authors {AuthoredTint.a:0.###}), got {v.a:0.###}");

        // A cap ABOVE the authored alpha must not raise it — the dial can only ever hide less of
        // the floor than the tileset intended, never more.
        bool ok2 = WaterOwnSurface.TryBuildFilmColour(AuthoredTint, 1f, out Color v2, out _);
        t.True(ok2 && Math.Abs(v2.a - AuthoredTint.a) < 1e-6f,
            $"WaterSettings.Opacity at 1.0 must leave the film at the AUTHORED alpha "
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
    //  4c. THE PATTERN DOES NOT TRANSLATE. AT ALL. IN ANY DIRECTION. EVER.
    // ---------------------------------------------------------------------------------------
    //  THE RULING, verbatim, on ModBuild 165: "Immer noch viel zu hektisch und es fließt jetzt
    //  einmal in die eine Richtung, stoppt kurz und fließt dann wieder in die andere. Erscheint
    //  nicht mehr immersiv. Ich will außerdem so gut wie KEIN fließen, es ist kein Fluss sondern
    //  eine Pfütze."
    //
    //  This replaces ScrollRateIsTheTilesets, which pinned that the ripple moved at the rate the
    //  TILESET authored. That was the right check for four rounds and it is the wrong one now: the
    //  tileset's rate is a translation, and the user has rejected a translation three times. So the
    //  scroll is deleted, and what is checked is the OPPOSITE property.
    //
    //  IT IS MEASURED RATHER THAN ASSERTED, and that is the whole point of this check. A source
    //  lint can see that the identifier `drift` is gone; it cannot see that a plausible-looking new
    //  term moves the field 3 cm a minute. So WaterOwnSurface.SwellHeightAt — the C# mirror of the
    //  shader's own height field, kept honest by SwellTableMatchesTheShader — is sampled on a grid
    //  at two instants and the two grids are CROSS-CORRELATED over a range of offsets. For a field
    //  that does not travel the best match is at exactly zero offset. This is the same measurement
    //  the offscreen harness makes on the rendered pixels; doing it here as well means a
    //  re-introduced drift fails the BUILD rather than being noticed in a photograph.
    private static void PatternNeverTranslates(Harness t)
    {
        // The shipped resolution. Amplitude 1 — this is a test of PHASE and a scale factor cannot
        // move one.
        float period = WaterOwnSurface.ResolvedSwellPeriod(1f, ShippedSwellDial);
        const float L = WaterOwnSurface.SwellWavelength;

        // A SWEEP OF INSTANTS, not one. A drift is a phase that GROWS with the clock, so a single
        // sample could be caught at the moment it happens to be small; a minute of them cannot.
        float worst = 0f;
        float worstAt = 0f;
        int worstComp = 0;
        foreach (float clock in new[] { 0f, 3.7f, 11f, 29f, 61f, 137f, 400f })
        {
            for (int i = 0; i < WaterOwnSurface.SwellRatios.Length; i++)
            {
                float d = ComponentDisplacement(clock, period, i, 0f);
                if (Math.Abs(d) > Math.Abs(worst))
                {
                    worst = d;
                    worstAt = clock;
                    worstComp = i;
                }
            }
        }

        t.True(Math.Abs(worst) < 0.005f,
            $"swell component {worstComp} has moved {worst * 100f:0.##} cm along its own direction "
            + $"by t = {worstAt:0.#} s. IT MUST NOT MOVE AT ALL. The standing ruling on ModBuild "
            + "165 is 'Ich will außerdem so gut wie KEIN fließen, es ist kein Fluss sondern eine "
            + "Pfütze' — the pattern may not translate, in any direction, ever. Look for a clock "
            + "inside a spatial phase: the shipped form is sin(k dot(dir, p) + phi) * cos(w t + "
            + "psi), and any `t` that has found its way into the first factor is a drift.");

        // ...AND THE MEASUREMENT CAN SEE A DRIFT WHEN THERE IS ONE. Without this the check above
        // could be passing on a projection that resolves nothing, and a test that cannot fail is
        // not a test. ModBuild 165's own residual was 2.4 mm/s; the control uses 5 cm/s, which is a
        // flow anybody would call one, and requires it to be found within a millimetre of its true
        // size rather than merely to register.
        const float ControlSpeed = 0.05f;
        const float ControlClock = 8f;
        float found = 0f;
        for (int i = 0; i < WaterOwnSurface.SwellRatios.Length; i++)
        {
            float shift = ControlSpeed * ControlClock;
            float d = ComponentDisplacement(ControlClock, period, i, shift);
            // The projection reads each component's displacement ALONG ITS OWN DIRECTION, so a
            // shift along +x registers as shift * cos(direction) on component i.
            float expect = shift * Mathf.Cos(WaterOwnSurface.SwellDirections[i] * Mathf.Deg2Rad);
            // ...modulo HALF a wavelength. A phase cannot tell a whole cycle from none, and a
            // standing component's own sign flip costs another half — so the measurement resolves
            // a displacement only within +/- a quarter wavelength, and the control's expectation
            // has to be folded the same way. That limit is a property of the quantity, not a
            // weakness of the projection: a pattern that has moved exactly one wavelength IS the
            // pattern that has not moved.
            float lambda = L * WaterOwnSurface.SwellRatios[i];
            float half = lambda * 0.5f;
            float wrapped = Mathf.Repeat(expect + half * 0.5f, half) - half * 0.5f;
            found = Mathf.Max(found, Math.Abs(d - wrapped) < 0.01f ? 1f : 0f);
            t.True(Math.Abs(d - wrapped) < 0.01f,
                $"the control field was translated {shift:0.###} world units along +x, which is "
                + $"{wrapped:0.###} along component {i}'s own direction once folded into its "
                + $"+/-{half * 0.5f:0.###} range, and the measurement read {d:0.###}. The check "
                + "above is therefore not measuring translation and its pass means nothing. Fix "
                + "the projection, not the shader.");
        }
        t.True(found > 0f, "the drift control ran at all");

        // THE BLOOM DOES NOT TRAVEL EITHER, and it is the term most likely to smuggle one back:
        // ModBuild 165 wrote each of its long modulations as sin(k dot(d,p) + w t), which is a
        // TRAVELLING wave whose envelope of activity swept the pool at w/k. A band of livelier
        // water crossing the pool is the same flow the ruling forbids, only slower and larger.
        // IT IS MEASURED IN CYCLES rather than in metres, because these wavelengths are 10 to 22 m
        // and a centimetre means something completely different there than it does on a 64 cm
        // ripple. The residual the projection leaves on the shipped field is about 0.003 of a
        // cycle; ModBuild 165's travelling envelope swept at w/k = 0.021 m/s, which over the same
        // interval is 0.036 of a cycle — an order of magnitude clear of the floor below.
        float bloomWorst = 0f, bloomAt = 0f;
        int bloomComp = 0;
        foreach (float clock in new[] { 0f, 17f, 53f, 149f, 400f })
            for (int i = 0; i < 3; i++)
            {
                float lambda = L * WaterOwnSurface.BloomWavelengthScales[i];
                float cycles = BloomDisplacement(clock, period, i) / lambda;
                if (Math.Abs(cycles) > Math.Abs(bloomWorst))
                {
                    bloomWorst = cycles;
                    bloomAt = clock;
                    bloomComp = i;
                }
            }
        t.True(Math.Abs(bloomWorst) < 0.01f,
            $"bloom modulation {bloomComp} has moved {bloomWorst:0.####} of its own wavelength by "
            + $"t = {bloomAt:0.#} s. Each of the three must be sin(k dot(d,p) + phi) times "
            + "cos(w t + psi), never sin(k dot(d,p) + w t) — the second form makes the lively part "
            + "of the pool SWEEP across it, which is a current with a longer wavelength and "
            + "nothing else.");

        // AND THE ENVELOPE STAYS INSIDE ITS BOUND, because the peak amplitude is what the
        // renderer's bounds were padded for and a bloom above 1 would put the crests outside them.
        float lo = 1f, hi = 0f;
        for (int i = 0; i <= 40; i++)
            for (int j = 0; j <= 40; j++)
            {
                float v = WaterOwnSurface.SwellBloomAt(
                    i * 0.31f, j * 0.29f, i * 1.7f + j * 0.4f, L, period,
                    WaterOwnSurface.SwellCalmDepth);
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
            }
        t.True(hi <= 1f + 1e-5f && lo >= 1f - WaterOwnSurface.SwellCalmDepth - 1e-5f,
            $"the bloom envelope swept [{lo:0.####}, {hi:0.####}], outside its declared "
            + $"[{1f - WaterOwnSurface.SwellCalmDepth:0.###}, 1]. Above 1 it would carry the swell "
            + "past the amplitude Renderer.localBounds was padded for, and culling cannot see a "
            + "domain program.");

        // AND THE SURFACE IS NOT FROZEN EITHER. "Kein Fluss" is not "kein Wasser": if the swell
        // stopped changing altogether the pool would be a painted sheet, and every measurement
        // above would still pass. So the height at one point must actually move over a minute.
        float pLo = float.MaxValue, pHi = float.MinValue;
        for (int i = 0; i <= 240; i++)
        {
            float h = WaterOwnSurface.SwellHeightAt(
                1.13f, 0.71f, i * 0.5f, L, period, 1f, WaterOwnSurface.SwellCalmDepth);
            pLo = Mathf.Min(pLo, h);
            pHi = Mathf.Max(pHi, h);
        }
        t.True(pHi - pLo > 0.15f,
            $"over two minutes one point of the pool moved through only {pHi - pLo:0.###} of the "
            + "peak amplitude. The ruling is that the water be STILL, not that it be dead — a "
            + "surface that never changes is ModBuild 162's flat sheet arrived at by arithmetic.");
    }

    /// <summary>
    /// HOW FAR ONE STANDING COMPONENT HAS MOVED, in world units along its own direction, measured
    /// off the field itself.
    ///
    /// <para>WHY A PROJECTION AND NOT A CROSS-CORRELATION. The obvious measurement — correlate the
    /// height field against itself a while later and read off the offset of the best match — is
    /// AMBIGUOUS on a standing wave, and the first draft of this check drowned in that. A standing
    /// component passes through zero and returns INVERTED, so half of the six are anti-correlated
    /// with their own past at any given moment, and a small shift that merely DECORRELATES those
    /// scores better than no shift at all. The peak wanders a sample or two and the check becomes a
    /// coin toss. (The offscreen harness still cross-correlates, because it works on RENDERED
    /// PIXELS whose luminance sits on a large positive pedestal and does not invert.)</para>
    ///
    /// <para>WHAT THIS DOES INSTEAD IS EXACT. Translation and standing evolution are distinguishable
    /// in one line of algebra: evolving in place multiplies a component's spatial pattern by a REAL
    /// number, and translating it rotates its PHASE. So the field is projected onto that component's
    /// own sine and cosine, and the angle between the two projections is the phase it has picked up.
    /// Divided by the wavenumber that is a distance, in metres, which is the number the ruling is
    /// about — and it is reported that way in the failure message.</para>
    ///
    /// <para>The bloom is switched OFF for this (calm depth 0). It is a spatially varying envelope,
    /// so it spreads each component's spectrum and would leak into the projection; it gets its own
    /// measurement in <see cref="BloomDisplacement"/>, against its own three modulations.</para>
    /// </summary>
    /// <param name="t">Seconds on the shared clock.</param>
    /// <param name="period">The longest component's bob period.</param>
    /// <param name="comp">Which of the six components to measure.</param>
    /// <param name="shift">A deliberate translation along +x, in world units — 0 for the shipped
    /// field, non-zero for the control that proves this measurement works.</param>
    private static float ComponentDisplacement(float t, float period, int comp, float shift)
    {
        const int N = 160;
        const float Step = 0.11f;      // 17.6 m across: seven periods of the longest component
        float lambda = WaterOwnSurface.SwellWavelength * WaterOwnSurface.SwellRatios[comp];
        float k = 2f * Mathf.PI / lambda;
        float rad = WaterOwnSurface.SwellDirections[comp] * Mathf.Deg2Rad;
        float dx = Mathf.Cos(rad), dz = Mathf.Sin(rad);

        // A HANN WINDOW, and it is what makes the projection trustworthy. Six components at
        // incommensurate wavelengths are not exactly orthogonal over a finite square, so each one
        // LEAKS into the others' projections — and the loudest component leaking into the quietest
        // rotates its measured phase by enough to read as a centimetre of movement that is not
        // there. The window's sidelobes are ~30 dB down, which puts the leak below a tenth of a
        // millimetre. It is symmetric, so it cannot introduce a phase shift of its own.
        var win = new float[N];
        for (int i = 0; i < N; i++)
            win[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (N - 1f));

        double sinAcc = 0, cosAcc = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                float x = i * Step, z = j * Step;
                float h = WaterOwnSurface.SwellHeightAt(
                    x - shift, z, t, WaterOwnSurface.SwellWavelength, period, 1f, 0f)
                    * win[i] * win[j];
                float phase = k * (dx * x + dz * z);
                sinAcc += h * Mathf.Sin(phase);
                cosAcc += h * Mathf.Cos(phase);
            }

        // The authored spatial phase is the answer when nothing has moved; the DIFFERENCE from it
        // is the displacement. Taken modulo pi because the standing factor cos(w t + psi) is
        // negative half the time and a sign flip is not a movement.
        float measured = Mathf.Atan2((float)cosAcc, (float)sinAcc);
        float authored = WaterOwnSurface.SwellSpatialPhases[comp];
        float delta = Mathf.Repeat(measured - authored + Mathf.PI * 0.5f, Mathf.PI)
                      - Mathf.PI * 0.5f;
        return -delta / k;
    }

    /// <inheritdoc cref="ComponentDisplacement"/>
    private static float BloomDisplacement(float t, float period, int comp)
    {
        const int N = 160;
        const float Step = 0.65f;      // 104 m across: several periods of even the longest bloom

        float lambda = WaterOwnSurface.SwellWavelength
                       * WaterOwnSurface.BloomWavelengthScales[comp];
        float k = 2f * Mathf.PI / lambda;
        float rad = WaterOwnSurface.BloomDirections[comp] * Mathf.Deg2Rad;
        float dx = Mathf.Cos(rad), dz = Mathf.Sin(rad);

        var win = new float[N];
        for (int i = 0; i < N; i++)
            win[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (N - 1f));

        double sinAcc = 0, cosAcc = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                float x = i * Step, z = j * Step;
                // The envelope's mean is 1 - calm/2 and carries no shape; subtracting it leaves the
                // three modulations alone.
                float v = WaterOwnSurface.SwellBloomAt(
                              x, z, t, WaterOwnSurface.SwellWavelength, period,
                              WaterOwnSurface.SwellCalmDepth)
                          - (1f - WaterOwnSurface.SwellCalmDepth * 0.5f);
                v *= win[i] * win[j];
                float phase = k * (dx * x + dz * z);
                sinAcc += v * Mathf.Sin(phase);
                cosAcc += v * Mathf.Cos(phase);
            }

        float measured = Mathf.Atan2((float)cosAcc, (float)sinAcc);
        float delta = Mathf.Repeat(
                          measured - WaterOwnSurface.BloomSpatialPhases[comp] + Mathf.PI * 0.5f,
                          Mathf.PI)
                      - Mathf.PI * 0.5f;
        return -delta / k;
    }

    // ---------------------------------------------------------------------------------------
    //  4c-bis. AND NO NAME A SPEED COULD BE WRITTEN UNDER SURVIVES ANYWHERE.
    // ---------------------------------------------------------------------------------------
    //  The measurement above proves the FIELD does not travel. This proves there is nowhere left to
    //  put a translation back: the ruling was that the term be deleted rather than zeroed, so that
    //  no dial and no future edit can raise it. Three shipped property names carried the old motion
    //  and every one of them is swept out of both shaders' source and out of the driver.
    private static void NoTranslationTermSurvives(Harness t, string? shaderPath, string repoRoot)
    {
        if (shaderPath != null)
        {
            string text = ShaderCodeOnly(shaderPath);
            foreach (string name in WaterOwnSurface.ForbiddenTranslationProperties)
            {
                t.True(text.IndexOf(name, StringComparison.Ordinal) < 0,
                    $"'{WaterOwnSurface.FilmShaderName}' mentions '{name}'. That is one of the "
                    + "names a SPEED was written under up to ModBuild 165, and the standing ruling "
                    + "is 'so gut wie KEIN fließen, es ist kein Fluss sondern eine Pfütze'. The "
                    + "term was deleted rather than set to zero precisely so that re-declaring it "
                    + "is a deliberate act that fails this gate, not a number somebody nudges.");
            }

            // THE SPATIAL PHASE, WRITTEN OUT. This is the one line the whole ruling lives on: up to
            // ModBuild 165 it read `k * (dot(dir, p) - drift * t) + spatialPhase`, and a `t` inside
            // it is a translation whatever it is called. Pinning the literal text is cruder than
            // pinning behaviour and it is the right complement to it — the measurement above says
            // the field does not move, and this says the expression cannot be edited into moving
            // without the diff being obvious.
            t.True(Regex.IsMatch(text, @"float sp = k \* dot\(dir, p\) \+ spatialPhase;"),
                $"'{WaterOwnSurface.FilmShaderName}' must compute each standing component's "
                + "spatial phase as exactly `k * dot(dir, p) + spatialPhase` — no clock, no drift, "
                + "no offset that grows with time. The temporal factor is the SEPARATE cos(w t + "
                + "psi), which is what makes the component stand rather than travel.");

            // AND THE RIPPLE'S SAMPLING COORDINATE TAKES NO CLOCK EITHER. A signature with no time
            // parameter is a stronger statement than any regex over the body: there is nothing in
            // scope for a drift to be built from.
            t.True(Regex.IsMatch(
                       text,
                       @"float2 GhvrRippleUV \(float2 p, float2 tiling, float2 cs, float2 ofs\)"),
                $"'{WaterOwnSurface.FilmShaderName}' must build the ripple's sampling coordinate in "
                + "GhvrRippleUV(p, tiling, cs, ofs) — a function with NO time parameter, so the "
                + "coordinate the normal map is read at cannot depend on the clock. ModBuild 165's "
                + "GhvrRippleOffset(rate, period, tiling, t) is what this replaces.");
            t.True(text.IndexOf("GhvrRippleOffset", StringComparison.Ordinal) < 0,
                $"'{WaterOwnSurface.FilmShaderName}' still declares GhvrRippleOffset, which is "
                + "ModBuild 165's drift-plus-sway. It must be gone, not called with zero.");
        }

        // ...AND THE DRIVER HAS NOTHING LEFT TO RESOLVE A RATE WITH.
        string src = Path.Combine(repoRoot, "src", "GloomhavenVR", "Core", "Water", "WaterTerrainVR.cs");
        if (File.Exists(src))
        {
            // Comments are NOT stripped here: the driver's forbidden-name check already looks for
            // a property WRITE rather than a mention, which is the same distinction made the other
            // way round.
            string driver = File.ReadAllText(src);
            foreach (string name in WaterOwnSurface.ForbiddenTranslationProperties)
            {
                // The name may still appear in PROSE — the file has to be able to say what was
                // deleted and why — so only a real property write is a failure.
                t.True(!Regex.IsMatch(driver, @"Set(Vector|Float)\([^)]*" + Regex.Escape(name)),
                    $"WaterTerrainVR writes '{name}' onto a material. Nothing on the film may "
                    + "carry a speed: the ruling is that the pattern does not translate at all.");
            }
            t.True(driver.IndexOf("WaterOwnSurface.ScrollRate", StringComparison.Ordinal) < 0,
                "WaterTerrainVR still calls WaterOwnSurface.ScrollRate. That function resolved the "
                + "tileset's authored scroll speeds into world units per second and it is deleted "
                + "with the properties it fed.");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  4c-ter. ONE DIAL, ONE CLOCK — and 0 means STILL, not FLAT.
    // ---------------------------------------------------------------------------------------
    /// <summary>
    /// The bounds on the shipped swell that DO NOT depend on anyone's verdict.
    ///
    /// <para>WHAT USED TO BE HERE, AND WHY IT IS GONE. This method carried a calibration table
    /// pairing the user's "too fast" and "frozen" reports with the SHIPPED numbers, a deg/s band
    /// derived from it, and a visibility floor "proven to fire" against it. Every attribution was
    /// wrong: his machine had the <c>[Water]</c> section live and tuned a long way off the shipped
    /// defaults (RippleSpeed 1.0 against 0.00875), so the verdicts were never about the numbers
    /// they were being attributed to. A precise instrument calibrated against misattributed data is
    /// worse than none — it agrees with every broken build and it argues back — so it is deleted
    /// rather than re-derived. The shipped values are now HIS, lifted from his config file, and the
    /// only thing that can judge the look is him.</para>
    ///
    /// <para>What is left is physical and was never in doubt.</para>
    /// </summary>
    private static void SwellStaysWithinItsPhysicalBounds(Harness t)
    {
        float amp = WaterOwnSurface.SwellAmplitude(
            WaterOwnSurface.MeasuredQuadWidthWU, WaterOwnSurface.ShippedSwellHeight);

        // THE TROUGH MAY NOT DIP THROUGH THE POOL FLOOR. The census measured 9 cm between the film
        // and the basin bed; the amplitude ceiling exists for that and nothing else. His
        // SwellHeight 0.045 would resolve to 11.7 cm on a 2.6 m quad and is CLAMPED to 6 cm, so the
        // clamp is load-bearing at the shipped setting rather than theoretical.
        t.True(amp <= WaterOwnSurface.MaxSwellAmplitude + 1e-6f,
            $"the shipped amplitude {amp:0.####} wu must not exceed the "
            + $"{WaterOwnSurface.MaxSwellAmplitude:0.###} ceiling");
        t.True(amp < WaterOwnSurface.FilmToBedGap,
            $"the shipped amplitude {amp:0.####} wu must stay under the "
            + $"{WaterOwnSurface.FilmToBedGap:0.##} wu gap to the basin bed — a trough may never "
            + "dip through its own pool floor");
        t.True(Mathf.Abs(amp - WaterOwnSurface.MaxSwellAmplitude) < 1e-6f,
            $"at the shipped SwellHeight {WaterOwnSurface.ShippedSwellHeight} the amplitude is "
            + $"expected to CLAMP to the ceiling on the measured "
            + $"{WaterOwnSurface.MeasuredQuadWidthWU} wu quad (got {amp:0.####}). If this ever "
            + "stops clamping, the shipped height has been re-based and the clamp is no longer the "
            + "thing bounding the trough.");

        // AND THE SWELL STILL MUST NOT REPEAT ON THE TILE LATTICE, at the shipped wave scale — the
        // "bei jedem tile identisch" report, which is about geometry and not about tempo.
        float mismatch = WaterOwnSurface.LatticeMismatch(
            WaterOwnSurface.SwellWavelength * WaterOwnSurface.ShippedWaveScale);
        t.True(mismatch > 0.1f,
            $"the shipped swell scores {mismatch:0.###} cycles against the tile lattice, which is "
            + "close enough to repeating that the same figure would appear on every water hex");
    }

    private static void SpeedDialIsOneClock(Harness t)
    {
        float shipped = WaterOwnSurface.ResolvedSwellPeriod(1f, ShippedSwellDial);

        // NO VERDICT-DERIVED BOUND SURVIVES HERE, and that is deliberate.
        //
        // This block used to bracket the shipped period between ModBuild 166's and 167's, quoting
        // "Sehr gut ... nur noch etwas zu schnell" and "gerne noch langsamer" as if those had been
        // said about the shipped numbers. They were not: his machine had the [Water] section live
        // at RippleSpeed 1.0 while the shipped default was 0.00875, so every one of those bounds
        // was an assertion about a build he had never seen. Obeying them produced three rounds of
        // frozen water and a test suite that stayed green through all of it.
        //
        // The shipped clock is now HIS clock, and the only bound left on it is arithmetic: the
        // resolved period must be positive, finite, and must fall as the dial rises. Whether the
        // tempo is right is his call, made by looking, and no constant in this file gets to
        // pre-empt it.
        t.True(shipped > 0f && !float.IsInfinity(shipped) && !float.IsNaN(shipped),
            $"the resolved swell period must be a positive finite number (got {shipped})");
        t.True(WaterOwnSurface.ResolvedSwellPeriod(1f, ShippedSwellDial * 2f) < shipped,
            "doubling the swell dial must SHORTEN the resolved period — the dial is a rate, and a "
            + "sign error here would invert every tuning request that follows");

        // THE TWO CLOCKS REMAIN SEPARATE, but neither leads any more. ModBuild 170 split them and
        // asserted the ripple must be the slower of the two; his own tuning had a single value for
        // both, so the split is available and unused rather than wrong. Only the arithmetic holds.
        float rippleClock = WaterOwnSurface.ResolvedSwellPeriod(1f, ShippedRippleDial);
        t.True(rippleClock > 0f && !float.IsInfinity(rippleClock),
            $"the resolved ripple period must be a positive finite number (got {rippleClock})");

        // THE RIPPLE'S OWN CLOCK IS STILL ONE CLOCK over its three crossfades, so nothing can be
        // left ticking at a rhythm of its own — which is exactly what the first draft of ModBuild
        // 165 did, and the preview log caught it.
        Vector4 fade = WaterOwnSurface.ResolvedFadePeriods(rippleClock);
        shipped = rippleClock;
        t.True(Mathf.Abs(fade.x - shipped * WaterOwnSurface.RippleFadeRatios[0]) < 1e-3f
               && Mathf.Abs(fade.y - shipped * WaterOwnSurface.RippleFadeRatios[1]) < 1e-3f
               && Mathf.Abs(fade.z - shipped * WaterOwnSurface.RippleFadeRatios[2]) < 1e-3f,
            "the ripple's three crossfade periods must be fixed multiples of the swell's own "
            + "period, so WaterSettings.RippleSpeed moves the whole surface's clock at once");
        t.True(fade.x > shipped && fade.y > fade.x && fade.z > fade.y,
            $"the crossfade periods {fade.x:0.#}/{fade.y:0.#}/{fade.z:0.#} s must be strictly "
            + "increasing and all LONGER than the swell's own period. The ripple is the fine "
            + "detail: if it changed faster than the wave carrying it, the surface would read as "
            + "busy however slow the geometry was.");

        // ...AND THEY MUST NOT SHARE A COMMON MEASURE. Three cycles at a rational ratio have a
        // beat, and a beat is a rhythm — "die animationen sollen sehr dezent und random sein".
        for (int i = 0; i < WaterOwnSurface.RippleFadeRatios.Length; i++)
            for (int j = i + 1; j < WaterOwnSurface.RippleFadeRatios.Length; j++)
            {
                float ratio = WaterOwnSurface.RippleFadeRatios[j]
                              / WaterOwnSurface.RippleFadeRatios[i];
                bool nearSimple = false;
                for (int p = 1; p <= 4 && !nearSimple; p++)
                    for (int q = 1; q <= 4; q++)
                        if (Math.Abs(ratio - (float)p / q) < 0.02f)
                            nearSimple = true;
                t.True(!nearSimple,
                    $"crossfade periods {i} and {j} are in a ratio of {ratio:0.###}, which is "
                    + "within 2% of a ratio of small whole numbers. Three cycles that come back "
                    + "into step give the pool a rhythm, and a rhythm is the opposite of "
                    + "'random'.");
            }

        // ZERO MEANS STILL, NOT FLAT. The relief is the amplitude and the clock is the period; a
        // dial at 0 must stop the second without touching the first, or "freeze the water" would
        // silently mean "delete the waves".
        //
        // THE RATIO IS PINNED, NOT THE PERIOD, and that is the whole point of this check. The
        // dial's own floor used to be 0.01 against a shipped 0.12, and two re-bases of the default
        // walked the shipped value down past it — at 0.0175 a floor of 0.01 would have made "0"
        // less than twice as slow as the shipped setting, i.e. not frozen at all, and nothing but
        // this assertion would have said so.
        float frozen = WaterOwnSurface.ResolvedSwellPeriod(1f, 0f);
        t.True(frozen >= shipped * 50f,
            $"WaterSettings.RippleSpeed 0 must give a period far longer than the shipped one (got "
            + $"{frozen:0.#} s against {shipped:0.#} s), so the surface holds still WITH ITS "
            + "RELIEF rather than flattening. A re-base of the default that walks it toward "
            + "WaterOwnSurface.MinSpeedDial silently turns 'freeze' into 'a bit slower'.");

        // AND A BIGGER WAVE IS A SLOWER ONE — deep-water dispersion, so WaterSettings.WaveScale cannot
        // turn a lazy roll into a fast one.
        t.True(WaterOwnSurface.ResolvedSwellPeriod(4f, ShippedSwellDial)
               > WaterOwnSurface.ResolvedSwellPeriod(1f, ShippedSwellDial),
            "the swell's period must grow with WaterSettings.WaveScale (as its square root), or a longer "
            + "wave would run at the same rate and read as a faster current");

        float nan = WaterOwnSurface.ResolvedSwellPeriod(float.NaN, float.NaN);
        t.True(!float.IsNaN(nan) && !float.IsInfinity(nan) && nan > 0f,
            $"a non-finite dial or scale must still give a usable period, got {nan}");
    }

    // ---------------------------------------------------------------------------------------
    //  4c-bis. THE STREAK. The authored anisotropy is tamed and the layers are weighted.
    // ---------------------------------------------------------------------------------------
    private static void TilingsAreTamedAndWeighted(Harness t)
    {
        Vector4 raw = WaterOwnSurface.AuthoredTilings;   // (0.14, 6.00, -0.12, -0.20)
        Vector4 tame = WaterOwnSurface.TameTilings(raw, 1f);

        // THE MEASUREMENT THE REPORT IS ABOUT. 6.00 / 0.14 is 43:1 — the normal map stretched into
        // a band 7.1 world units long and 0.17 wide, which IS a streak, and which set moving is
        // "hektische weiße Streifen".
        float rawRatio = Math.Abs(raw.y / raw.x);
        float tameRatio = Math.Abs(tame.y / tame.x);
        t.True(rawRatio > 40f,
            $"the authored layer A is measured at {rawRatio:0.#}:1 anisotropy; if that ever stops "
            + "being true this whole tame is answering a problem the material no longer has");
        t.True(tameRatio < 4f,
            $"layer A's {rawRatio:0.#}:1 must come out under 4:1 after the tame, got "
            + $"{tameRatio:0.##}:1. A band is what a streak is; the tame is the only thing "
            + "standing between the authored value and the shipped look.");

        // THE MEAN FEATURE SIZE IS PRESERVED EXACTLY. The tame is a power blend about each layer's
        // own geometric mean, so the PRODUCT of the two components is unchanged — the waves are
        // rounder, not bigger or smaller, and nothing about the tame can be blamed for a scale
        // change that WaveScale did.
        t.True(Math.Abs(Math.Abs(tame.x * tame.y) - Math.Abs(raw.x * raw.y)) < 1e-4f,
            $"the tame must preserve each layer's geometric mean: authored |xy| "
            + $"{Math.Abs(raw.x * raw.y):0.####} vs tamed {Math.Abs(tame.x * tame.y):0.####}");

        // The direction of every component survives: a sign flip would run the ripple the other
        // way for no reason anybody could find.
        t.True(Math.Sign(tame.x) == Math.Sign(raw.x) && Math.Sign(tame.y) == Math.Sign(raw.y)
               && Math.Sign(tame.z) == Math.Sign(raw.z) && Math.Sign(tame.w) == Math.Sign(raw.w),
            "the tame must keep every component's sign — it changes the aspect ratio, not the "
            + "direction the layer runs");

        // WaterSettings.WaveScale is a pure size dial: twice the scale is half the repeats per metre.
        Vector4 big = WaterOwnSurface.TameTilings(raw, 2f);
        t.True(Near(big.x, tame.x * 0.5f) && Near(big.w, tame.w * 0.5f, 1e-4f),
            "WaterSettings.WaveScale must divide every resolved tiling, i.e. bigger waves are fewer "
            + "repeats per world unit");
        Vector4 zeroScale = WaterOwnSurface.TameTilings(raw, 0f);
        t.True(Near(zeroScale.x, tame.x) && Near(zeroScale.y, tame.y),
            "a WaveScale of 0 must be refused as 1 rather than dividing by zero — an infinite "
            + "tiling is a surface of pure noise");

        // A degenerate layer is passed through rather than guessed at: a tiling of 0 means
        // 'constant along this axis', which is a shape and not an accident.
        Vector4 flat = WaterOwnSurface.TameTilings(new Vector4(0f, 3f, -0.12f, -0.20f), 1f);
        t.True(Near(flat.x, 0f) && Near(flat.y, 3f),
            $"a layer with a zero component must pass through untamed, got ({flat.x:0.###},"
            + $"{flat.y:0.###})");

        // THE WEIGHTS. Coarse dominant, fine as detail, summing to 1.
        Vector4 w = WaterOwnSurface.LayerWeights(tame);
        t.True(Near(w.x + w.y, 1f, 1e-4f),
            $"the layer weights must sum to 1, got {w.x:0.###} + {w.y:0.###}. They used to sum to "
            + "2 (both layers added at full amplitude), which is why the same _NormalStrength "
            + "produced twice the tilt it reads as.");
        t.True(w.y > w.x * 3f,
            $"the COARSER layer must dominate: got A {w.x:0.###} against B {w.y:0.###}. A short "
            + "wave rides on a long one at a fraction of its height; equal weights are what let "
            + "the fine, fast, streaky layer be the whole of the surface.");
        t.True(Near(w.z, 0f) && Near(w.w, 0f),
            "the weight vector's z and w are unread by the shader and must stay zero");

        // ...and the ordering is derived, not hard-coded: swap the two layers over and the weights
        // swap with them.
        Vector4 swapped = WaterOwnSurface.LayerWeights(
            new Vector4(tame.z, tame.w, tame.x, tame.y));
        t.True(Near(swapped.x, w.y, 1e-4f) && Near(swapped.y, w.x, 1e-4f),
            "the weights must follow the FREQUENCIES, not the slot: with the layers exchanged the "
            + "weights must exchange too");

        Vector4 degenerate = WaterOwnSurface.LayerWeights(new Vector4(0f, 0f, -0.12f, -0.20f));
        t.True(Near(degenerate.x, 0.5f) && Near(degenerate.y, 0.5f),
            "a degenerate tiling must fall back to an even split — never to a zero weight, which "
            + "would delete a layer of the ripple with nothing in the log to say so");
    }

    // ---------------------------------------------------------------------------------------
    //  4c-ter. THE SWELL's amplitude scales with the quad and stops short of the basin bed.
    // ---------------------------------------------------------------------------------------
    private static void SwellAmplitudeIsScaledAndCapped(Harness t)
    {
        // The shipped case: a hex about a metre across at the shipped 2%.
        float a = WaterOwnSurface.SwellAmplitude(1f, 0.02f);
        t.True(Near(a, 0.02f),
            $"a 1 m film quad at WaterSettings.SwellHeight 0.02 must give a 2 cm peak, got {a:0.####}");

        // ...and the same dial at another diorama scale gives a wave that LOOKS the same, which is
        // the whole reason it is a fraction of the quad rather than a world constant.
        t.True(Near(WaterOwnSurface.SwellAmplitude(0.25f, 0.02f), 0.005f),
            "the amplitude must scale with the quad's own width");

        // THE CEILING IS A COLLISION LIMIT, not a taste one.
        float huge = WaterOwnSurface.SwellAmplitude(40f, 0.05f);
        t.True(huge <= WaterOwnSurface.MaxSwellAmplitude + 1e-6f,
            $"the swell must never exceed {WaterOwnSurface.MaxSwellAmplitude:0.###} world units, "
            + $"got {huge:0.###}. The census measured {WaterOwnSurface.FilmToBedGap:0.##} between "
            + "the film and the basin bed under it, and the wave is symmetric about the authored "
            + "plane: past the ceiling a trough dips through the pool's own floor and z-fights "
            + "with the stone, which reads as the water tearing open.");

        // Every refusal path is FLAT, never a wave nobody can predict: a NaN amplitude reaching a
        // vertex program is a triangle at infinity, i.e. a screen-wide smear.
        foreach ((float width, float dial, string what) in new[]
                 {
                     (0f, 0.02f, "a zero-width quad"),
                     (float.NaN, 0.02f, "a non-finite quad width"),
                     (1f, float.NaN, "a non-finite dial"),
                     (1f, -1f, "a negative dial"),
                     (-1f, 0.02f, "a negative width"),
                 })
        {
            float v = WaterOwnSurface.SwellAmplitude(width, dial);
            t.True(Near(v, 0f),
                $"{what} must give a FLAT film (0), got {v}. A non-finite displacement is a "
                + "vertex at infinity and a smear across the whole eye.");
        }

        // The dial's own OFF position has to be exact — it is the A/B for whether the relief is
        // worth its vertices.
        t.True(Near(WaterOwnSurface.SwellAmplitude(1f, 0f), 0f),
            "WaterSettings.SwellHeight at 0 must flatten the film exactly");
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

    /// <summary>
    /// A .shader's own text with every <c>#include "X.cginc"</c> it names from its own directory
    /// pasted in.
    ///
    /// <para>WHY. Since ModBuild 165 WaterVR's entire program — the four stages, the swell, the
    /// ripple, the light — lives in WaterVR.cginc, and the .shader is a property table plus two
    /// SubShaders that include it. Every source lint in this file would otherwise be reading the
    /// property table and calling it a sweep of the shader. Only the shader's OWN directory is
    /// followed: UnityCG.cginc and the rest of the engine's own includes are not ours and are not
    /// what a regression would be added to.</para>
    /// </summary>
    /// <summary>
    /// <see cref="ExpandedShaderSource"/> with every <c>//</c> comment stripped — the CODE and
    /// nothing else.
    ///
    /// <para>WHY IT IS NEEDED. The checks that sweep for a FORBIDDEN name have to be able to
    /// distinguish a declaration from a sentence about a declaration, and since ModBuild 166 the
    /// shader's header explains at length which three properties were deleted and why. A lint that
    /// could not tell the two apart would force the file to stop naming what it removed, which is
    /// the opposite of what the comment discipline here is for.</para>
    /// </summary>
    private static string ShaderCodeOnly(string shaderPath) =>
        Regex.Replace(ExpandedShaderSource(shaderPath), @"//[^\n]*", string.Empty);

    private static string ExpandedShaderSource(string shaderPath)
    {
        string text = File.ReadAllText(shaderPath);
        string dir = Path.GetDirectoryName(shaderPath) ?? ".";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, "#include\\s+\"(?<f>[^\"]+\\.cginc)\""))
        {
            string name = m.Groups["f"].Value;
            if (!seen.Add(name))
                continue;
            string p = Path.Combine(dir, name);
            if (File.Exists(p))
                text += "\n" + File.ReadAllText(p);
        }
        return text;
    }

    private static float ParseF(string s) =>
        float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

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
