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
// VFX/Water_Shd_Trans. So the film's whole material is replaced with a mod-owned one. Three things
// can silently undo that, and each has a check below:
//
//   1. THE REPLACEMENT SHADER ACQUIRING A REFLECTION. This is now a HARD requirement, not a
//      preference: the symptom he cannot switch off IS a head-bound reflection, so a replacement
//      that can compute one answers nothing. GloomhavenVR/Overlay has no view-dependent term today.
//      Nothing stops a later edit adding one — it is a general-purpose overlay shader used by other
//      features — and the only symptom would be a sixth photograph that looks the same. Check 2
//      sweeps its source for every spelling of an environment sample.
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
    /// pipeline offers. A shader containing ANY of them can produce a reflection that swings with
    /// the head, which is the one thing the replacement film may not do.</summary>
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
    };

    internal static void Run(Harness t, string repoRoot)
    {
        t.Case("water own surface");

        string? shaderPath = FindShaderSource(repoRoot, WaterOwnSurface.FilmShaderName);
        ShaderIsReachable(t, shaderPath);
        ShaderHasNoEnvironmentSample(t, shaderPath);
        ShaderDeclaresEveryPropertyWeWrite(t, shaderPath);
        RenderStateIsNotAdditive(t);
        FilmColourIsTheTilesets(t);
        NeverBrighterSweep(t);
        WaterNeverHidesARenderer(t, repoRoot);
    }

    // ---------------------------------------------------------------------------------------
    //  1. The shader exists, declares the name we file it under, and is a TABLE entry.
    // ---------------------------------------------------------------------------------------
    private static void ShaderIsReachable(Harness t, string? shaderPath)
    {
        t.True(shaderPath != null,
            $"the water film's replacement shader '{WaterOwnSurface.FilmShaderName}' must be a "
            + $".shader file under {BundleRelRoot} that DECLARES that exact name. No file declares "
            + "it, so BundleShaders.Resolve has nothing to load out of the bundle and every water "
            + "film would silently keep the game's own material — which is a build that looks "
            + "identical to the four that came before it.");

        // The name is a plain string literal in non-comment src/ code, so BundledShaderVectors'
        // check 4 already requires it to be a key of BundleShaders.Paths. Stated here so a reader
        // of THIS file knows the bundled-shader trap is covered and does not add a second table.
        t.True(WaterOwnSurface.FilmShaderName.StartsWith("GloomhavenVR/", StringComparison.Ordinal),
            "the replacement film shader must be one of the MOD's shaders — a game shader would "
            + "put us back to tuning something we cannot open. Got: "
            + WaterOwnSurface.FilmShaderName);
    }

    // ---------------------------------------------------------------------------------------
    //  2. THE HARD REQUIREMENT. No environment sample, verified in the shader's own source.
    // ---------------------------------------------------------------------------------------
    private static void ShaderHasNoEnvironmentSample(Harness t, string? shaderPath)
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
            $"'{WaterOwnSurface.FilmShaderName}' is the shader the water film is re-based onto and "
            + "it MUST NOT sample the environment, but its source now contains: "
            + string.Join(", ", found) + ". The user's report is that the head-bound reflection "
            + "cannot be switched off by any property dial — 'Ich konnte aber mit den anderen "
            + "Einstellungen die kopf-gebundene Reflektion nicht deaktivieren, egal was ich "
            + "eingestellt hab' — so a replacement that can compute a view-dependent reflection "
            + "answers nothing and ships the same defect under a different name. GloomhavenVR/"
            + "EnvPuddle was disqualified for exactly this (it builds its mirror images out of "
            + "reflect(-V, N)). Either keep this shader free of environment samples, or point "
            + "WaterOwnSurface.FilmShaderName at one that is.");
    }

    // ---------------------------------------------------------------------------------------
    //  3a. Every property the driver writes is one the shader actually DECLARES.
    // ---------------------------------------------------------------------------------------
    private static void ShaderDeclaresEveryPropertyWeWrite(Harness t, string? shaderPath)
    {
        if (shaderPath == null)
            return;

        string text = File.ReadAllText(shaderPath);
        string[] written =
        {
            WaterOwnSurface.TintProperty,
            WaterOwnSurface.MainTexProperty,
            WaterOwnSurface.CullProperty,
            WaterOwnSurface.ZTestProperty,
            WaterOwnSurface.ZWriteProperty,
            WaterOwnSurface.SrcBlendProperty,
            WaterOwnSurface.DstBlendProperty,
        };
        foreach (string p in written)
        {
            // `_Name (` is how a Unity Properties block declares one. A property the shader does
            // not declare is a SetFloat that Unity silently discards, which is indistinguishable
            // in the log from a write that landed and did nothing — the exact trap this whole
            // module was built out of.
            t.True(Regex.IsMatch(text, @"(?m)^\s*(\[[^\]]*\]\s*)*" + Regex.Escape(p) + @"\s*\("),
                $"the driver writes '{p}' onto the replacement film material, but "
                + $"'{WaterOwnSurface.FilmShaderName}' does not declare a property by that name. "
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
