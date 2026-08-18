using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE EDGE / FOAM / BORDER BAND — pinned here for the reason the rest of this list exists: the
/// verdict is observed ONLY by eye, from inside a headset, one photograph per hardware round, and
/// a wrong answer does not throw, does not log and does not look like a failure. It looks like a
/// pool that is still wrong, which is a report this project has now received four times.
///
/// <para>WHAT THIS FILE IS ACTUALLY DEFENDING. Three rounds tuned the water film's BODY —
/// <c>_Color_Tint</c>'s alpha and its reflection scalars — and the hardware log proves every one
/// of those writes landed. The user's verdict on all of it was <i>"Keinen Unterschied bei der
/// Reflektion."</i> <c>.planning/debug/spiegeltiles.jpg</c> is what settled it: the hexes are a
/// flat, pale, milky near-WHITE sheet, LIGHTER than the stone around them, and the authored body
/// colour is <c>_Color_Tint</c> = RGBA(0.195, 0.311, 0.131, 0.737) — dark green. A dark green body
/// cannot render as a pale sheet at any alpha, so the visible pixels are the SHORELINE BAND. The
/// band is what <see cref="WaterEdgeBand"/> collapses and what this file pins.</para>
///
/// <para>THE THREE THINGS THAT WOULD SILENTLY RE-OPEN THE BUG, each of which has a vector below:</para>
/// <list type="number">
///   <item><b>Losing <c>_WaterBorderWidth</c> / <c>_WaterBorderCol</c> from the name tables.</b>
///   That pair is a SECOND border mechanism on the same material, it carries no toggle keyword at
///   all, and nothing this project shipped through ModBuild 160 had ever touched it — which is
///   precisely why the keyword clear of that build could not have worked. A tidy-up that trimmed
///   the arrays to "the edge properties" would put the mod back where round three ended, and the
///   only symptom would be a photograph that still looks the same.</item>
///
///   <item><b>Collapsing a value that is already at or below zero.</b> Writing 0 over a negative
///   authored width RAISES it. The whole module rests on never making a surface brighter or
///   shinier than the tileset authored it, because a violation arrives as "jetzt ist es noch
///   heller" and nothing in this repository could otherwise notice.</item>
///
///   <item><b>Making <see cref="WaterDepthFadeMode.Inverted"/> reachable without the user asking.</b>
///   It is the ONE write in this module that raises a value above the authored one (0 -> 1), and
///   it is defensible only as a dial. The sweep below states that fact as an assertion rather than
///   as a comment.</item>
/// </list>
///
/// <para>And one derivation that is easy to get wrong and impossible to see: the hardware census
/// prints <c>_EdgeColour_Toggle(Float [Toggle])=1</c> — a BARE <c>[Toggle]</c> with no keyword
/// name — beside a live keyword list of exactly <c>[_EDGECOLOUR_TOGGLE_ON]</c>. Unity derives that
/// keyword as the property name uppercased plus <c>_ON</c>. A rename of the property without the
/// matching rename of the keyword would leave <c>DisableKeyword</c> silently addressing nothing,
/// which is exactly the class of failure that ate ModBuild 159's half of the foam neutralisation.</para>
/// </summary>
internal static class WaterEdgeVectors
{
    /// <summary>The band as the hardware census read it off <c>VFX/Water_Shd_Trans</c>
    /// (LogOutput.log:941). Every number below is measured, not invented.</summary>
    private const float AuthoredEdgeDistance = 0.2f;
    private const float AuthoredEdgeColourDistance = 0.9f;
    private const float AuthoredWaterBorderWidth = 0.1f;

    /// <summary>The film's authored body tint — the dark green that cannot be the pale sheet.</summary>
    private static readonly Color AuthoredBody = new(0.195f, 0.311f, 0.131f, 0.737f);

    /// <summary>The near-white shoreline colour, and the pale beige of the second border
    /// mechanism. Between them they are the only pale things this material declares.</summary>
    private static readonly Color AuthoredEdgeColour = new(0.887f, 0.887f, 0.887f, 0.867f);
    private static readonly Color AuthoredBorderColour = new(0.670f, 0.617f, 0.528f, 0.561f);

    internal static void Run(Harness t)
    {
        NameTables(t);
        KeywordDerivation(t);
        WidthCollapse(t);
        ColourCollapse(t);
        DepthFadeDial(t);
        NeverBrighterSweep(t);
    }

    // ---------------------------------------------------------------------------------------
    //  1. The name tables. Trap 1: the second border mechanism must stay in scope.
    // ---------------------------------------------------------------------------------------
    private static void NameTables(Harness t)
    {
        Contains(t, WaterEdgeBand.BandWidthProperties, "_Edge_Distance");
        Contains(t, WaterEdgeBand.BandWidthProperties, "_Edge_Colour_Distance");
        // THE ONE THAT MATTERS. No toggle keyword exists for this mechanism, so ModBuild 160's
        // DisableKeyword could never have reached it and nothing else ever wrote it.
        Contains(t, WaterEdgeBand.BandWidthProperties, "_WaterBorderWidth");

        Contains(t, WaterEdgeBand.BandColourProperties, "_Edge_Colour");
        Contains(t, WaterEdgeBand.BandColourProperties, "_WaterBorderCol");

        // A width table and a colour table must not overlap: the driver writes floats from one and
        // colours from the other, and a name in both would be written twice with two types.
        foreach (string w in WaterEdgeBand.BandWidthProperties)
        {
            foreach (string c in WaterEdgeBand.BandColourProperties)
            {
                t.True(!string.Equals(w, c, StringComparison.Ordinal),
                    $"water band tables overlap on '{w}': a name cannot be both a width and a "
                    + "colour, and the driver would write it as both");
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  2. The [Toggle] keyword derivation — measured against the live keyword list.
    // ---------------------------------------------------------------------------------------
    private static void KeywordDerivation(Harness t)
    {
        string derived = WaterEdgeBand.EdgeToggleProperty.ToUpperInvariant() + "_ON";
        t.True(string.Equals(derived, WaterEdgeBand.EdgeToggleKeyword, StringComparison.Ordinal),
            $"the shoreline toggle's keyword must be the property name uppercased plus _ON: "
            + $"'{WaterEdgeBand.EdgeToggleProperty}' derives '{derived}' but the module uses "
            + $"'{WaterEdgeBand.EdgeToggleKeyword}'. The hardware census read the live material's "
            + "keyword list as exactly [_EDGECOLOUR_TOGGLE_ON], so a mismatch here means "
            + "DisableKeyword is addressing a keyword that does not exist and is silently inert.");
        t.True(string.Equals(WaterEdgeBand.EdgeToggleKeyword, "_EDGECOLOUR_TOGGLE_ON",
                   StringComparison.Ordinal),
            "the shoreline keyword must stay exactly the one the hardware census observed live on "
            + "the material: _EDGECOLOUR_TOGGLE_ON");
    }

    // ---------------------------------------------------------------------------------------
    //  3. Width collapse — the measured values, and the never-raise refusals.
    // ---------------------------------------------------------------------------------------
    private static void WidthCollapse(Harness t)
    {
        Collapses(t, "_Edge_Distance", AuthoredEdgeDistance, 0f);
        Collapses(t, "_Edge_Colour_Distance", AuthoredEdgeColourDistance, 0f);
        Collapses(t, "_WaterBorderWidth", AuthoredWaterBorderWidth, 0f);
        Collapses(t, "a wide band", 1f, 0f);

        // Trap 2: nothing at or below zero may be written, because writing 0 over a negative
        // authored width would RAISE it.
        RefusesWidth(t, "already 0", 0f);
        RefusesWidth(t, "negative", -0.5f);
        RefusesWidth(t, "NaN", float.NaN);
        RefusesWidth(t, "Infinity", float.PositiveInfinity);
        RefusesWidth(t, "-Infinity", float.NegativeInfinity);
    }

    // ---------------------------------------------------------------------------------------
    //  4. Colour collapse — the hue becomes the body's, the alpha goes to zero.
    // ---------------------------------------------------------------------------------------
    private static void ColourCollapse(Harness t)
    {
        bool ok = WaterEdgeBand.TryCollapseColour(
            AuthoredEdgeColour, AuthoredBody, out Color v, out string reason);
        t.True(ok, $"the near-white shoreline colour must be collapsed, not refused: {reason}");
        t.True(ok && Math.Abs(v.a) < 1e-6f,
            $"_Edge_Colour alpha must go to 0, got {v.a:0.#####} ({reason})");
        // The HUE half matters independently of the alpha half: a shader that ignores the band
        // alpha still draws this colour, and it must then draw WATER rather than shoreline. This
        // is the write that must not be dropped as redundant.
        t.True(ok && Math.Abs(v.r - AuthoredBody.r) < 1e-6f
               && Math.Abs(v.g - AuthoredBody.g) < 1e-6f
               && Math.Abs(v.b - AuthoredBody.b) < 1e-6f,
            $"_Edge_Colour must be repainted in the BODY hue RGB({AuthoredBody.r:0.###},"
            + $"{AuthoredBody.g:0.###},{AuthoredBody.b:0.###}), got RGB({v.r:0.###},{v.g:0.###},"
            + $"{v.b:0.###}). A near-white band colour is what spiegeltiles.jpg shows on screen.");

        bool ok2 = WaterEdgeBand.TryCollapseColour(
            AuthoredBorderColour, AuthoredBody, out Color v2, out string reason2);
        t.True(ok2, $"the second border mechanism's colour must be collapsed too: {reason2}");
        t.True(ok2 && Math.Abs(v2.a) < 1e-6f,
            $"_WaterBorderCol alpha must go to 0, got {v2.a:0.#####}");

        // Never raise: a band that is already fully transparent is left exactly alone.
        RefusesColour(t, "already transparent", new Color(1f, 1f, 1f, 0f));
        RefusesColour(t, "negative alpha", new Color(1f, 1f, 1f, -0.25f));
        RefusesColour(t, "NaN alpha", new Color(1f, 1f, 1f, float.NaN));
    }

    // ---------------------------------------------------------------------------------------
    //  5. The depth-fade dial. Trap 3: Inverted is the only raise in the module.
    // ---------------------------------------------------------------------------------------
    private static void DepthFadeDial(Harness t)
    {
        // Authored WRITES the authored value rather than skipping, so winding the dial back
        // restores the surface exactly instead of leaving the last setting standing on our
        // instance. It is a write, and it is an identity.
        bool a = WaterEdgeBand.TryResolveDepthFade(
            WaterDepthFadeMode.Authored, 0f, out float va, out string ra);
        t.True(a && Math.Abs(va) < 1e-6f,
            $"DepthFade=Authored must write the authored 0 back (got ok={a}, {va:0.###}, {ra})");
        bool a1 = WaterEdgeBand.TryResolveDepthFade(
            WaterDepthFadeMode.Authored, 1f, out float va1, out _);
        t.True(a1 && Math.Abs(va1 - 1f) < 1e-6f,
            $"DepthFade=Authored must be an identity for any authored value, got {va1:0.###}");

        bool n = WaterEdgeBand.TryResolveDepthFade(
            WaterDepthFadeMode.NotInverted, 1f, out float vn, out _);
        t.True(n && Math.Abs(vn) < 1e-6f,
            $"DepthFade=NotInverted must force 0, got ok={n} value {vn:0.###}");

        bool i = WaterEdgeBand.TryResolveDepthFade(
            WaterDepthFadeMode.Inverted, 0f, out float vi, out string ri);
        t.True(i && Math.Abs(vi - 1f) < 1e-6f,
            $"DepthFade=Inverted must force 1, got ok={i} value {vi:0.###}");
        t.True(ri.IndexOf("RAISES", StringComparison.Ordinal) >= 0,
            "DepthFade=Inverted is the only write in this module that raises a value above the "
            + "authored one, and its reason string must SAY so in the hardware log — that string "
            + "is the only place a reader of the log learns it. Got: " + ri);

        // The default of the enum is what a fresh install gets, and it must be the one setting
        // that makes no claim about a shader nobody here has read.
        t.True(default(WaterDepthFadeMode) == WaterDepthFadeMode.Authored,
            "the default WaterDepthFadeMode must be Authored: any other default silently forces a "
            + "sign onto a compiled shader this project has never opened, and Inverted would raise "
            + "_InvertDepthFade above the tileset's own 0 without anyone asking");

        RefusesDepthFade(t, "NaN", float.NaN);
        RefusesDepthFade(t, "Infinity", float.PositiveInfinity);
    }

    // ---------------------------------------------------------------------------------------
    //  6. THE INVARIANT. The reason this file is on the list at all.
    // ---------------------------------------------------------------------------------------
    private static void NeverBrighterSweep(Harness t)
    {
        int widthChecks = 0;
        for (int a = -20; a <= 40; a++)
        {
            float authored = a / 20f;
            if (!WaterEdgeBand.TryCollapseWidth(authored, out float v, out string why))
            {
                // A refusal must leave the authored value untouched AND say why, or the hardware
                // log cannot tell "we declined" from "we wrote something".
                t.True(Math.Abs(v - authored) < 1e-6f || float.IsNaN(authored),
                    $"water band width refusal must not alter the value: authored "
                    + $"{authored:0.###} came back {v:0.###} ({why})");
                t.True(!string.IsNullOrEmpty(why),
                    $"water band width refusal at {authored:0.###} must carry a reason");
                continue;
            }
            widthChecks++;
            t.True(v <= authored + 1e-6f,
                $"water band width never widened: authored {authored:0.###} -> {v:0.###} ({why})");
            t.True(!float.IsNaN(v) && !float.IsInfinity(v),
                $"water band width finite: authored {authored:0.###} -> {v:0.###}");
        }
        t.True(widthChecks > 30,
            $"the water band width sweep actually ran (only {widthChecks} collapses were applied — "
            + "a gating change that refused everything would make this file assert nothing)");

        int colourChecks = 0;
        for (int a = -5; a <= 20; a++)
        {
            float alpha = a / 20f;
            var authored = new Color(0.887f, 0.887f, 0.887f, alpha);
            if (!WaterEdgeBand.TryCollapseColour(
                    authored, AuthoredBody, out Color v, out string why))
            {
                t.True(Math.Abs(v.a - alpha) < 1e-6f,
                    $"water band colour refusal must not alter the alpha: {alpha:0.###} came back "
                    + $"{v.a:0.###} ({why})");
                continue;
            }
            colourChecks++;
            t.True(v.a <= alpha + 1e-6f,
                $"water band colour alpha never raised: {alpha:0.###} -> {v.a:0.###} ({why})");
            // The collapsed band must never be BRIGHTER than the body it is painted in — that is
            // the whole complaint, a sheet lighter than the stone around it.
            float bodyLuma = AuthoredBody.r + AuthoredBody.g + AuthoredBody.b;
            t.True(v.r + v.g + v.b <= bodyLuma + 1e-6f,
                $"water band colour never brighter than the body it is painted in: got "
                + $"RGB({v.r:0.###},{v.g:0.###},{v.b:0.###}) against a body sum of {bodyLuma:0.###}");
        }
        t.True(colourChecks > 15,
            $"the water band colour sweep actually ran (only {colourChecks} collapses applied)");

        // Every mode, every plausible authored value: only Inverted may ever raise.
        foreach (WaterDepthFadeMode mode in Enum.GetValues(typeof(WaterDepthFadeMode)))
        {
            for (int a = 0; a <= 4; a++)
            {
                float authored = a / 4f;
                if (!WaterEdgeBand.TryResolveDepthFade(mode, authored, out float v, out string why))
                    continue;
                bool raised = v > authored + 1e-6f;
                t.True(!raised || mode == WaterDepthFadeMode.Inverted,
                    $"only DepthFade=Inverted may raise _InvertDepthFade: {mode} took authored "
                    + $"{authored:0.###} to {v:0.###} ({why})");
                t.True(!float.IsNaN(v) && !float.IsInfinity(v),
                    $"_InvertDepthFade write finite: {mode} {authored:0.###} -> {v:0.###}");
            }
        }
    }

    // ---------------------------------------------------------------------------------------

    private static void Contains(Harness t, string[] table, string name)
    {
        bool found = false;
        for (int i = 0; i < table.Length; i++)
        {
            if (string.Equals(table[i], name, StringComparison.Ordinal))
                found = true;
        }
        t.True(found,
            $"'{name}' must stay in the water band property table — it was read off the live "
            + "material on hardware, and a name dropped from this table is a band the driver "
            + "silently stops collapsing, with a photograph that looks identical as the only sign");
    }

    private static void Collapses(Harness t, string what, float authored, float expected)
    {
        bool ok = WaterEdgeBand.TryCollapseWidth(authored, out float v, out string reason);
        t.True(ok, $"water band width '{what}' should have collapsed but was refused: {reason}");
        t.True(ok && Math.Abs(v - expected) < 1e-6f,
            $"water band width '{what}': expected {expected:0.#####}, got {v:0.#####} ({reason})");
    }

    private static void RefusesWidth(Harness t, string what, float authored)
    {
        bool ok = WaterEdgeBand.TryCollapseWidth(authored, out float v, out string reason);
        t.True(!ok, $"water band width '{what}' should have been REFUSED but wrote {v:0.#####}");
        t.True(!string.IsNullOrEmpty(reason),
            $"water band width '{what}': a refusal must always carry a reason for the log");
    }

    private static void RefusesColour(Harness t, string what, Color authored)
    {
        bool ok = WaterEdgeBand.TryCollapseColour(
            authored, AuthoredBody, out Color v, out string reason);
        t.True(!ok, $"water band colour '{what}' should have been REFUSED but wrote alpha {v.a}");
        t.True(!string.IsNullOrEmpty(reason),
            $"water band colour '{what}': a refusal must always carry a reason for the log");
    }

    private static void RefusesDepthFade(Harness t, string what, float authored)
    {
        bool ok = WaterEdgeBand.TryResolveDepthFade(
            WaterDepthFadeMode.NotInverted, authored, out _, out string reason);
        t.True(!ok, $"_InvertDepthFade '{what}' should have been REFUSED rather than written");
        t.True(!string.IsNullOrEmpty(reason),
            $"_InvertDepthFade '{what}': a refusal must always carry a reason for the log");
    }
}
