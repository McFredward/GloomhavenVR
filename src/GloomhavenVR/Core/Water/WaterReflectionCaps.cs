using System;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WHICH SHADER PROPERTY IS THE MIRROR, AND WHICH WAY DOES IT POINT — the arithmetic behind
/// <see cref="WaterTerrainVR"/>'s reflection retune, kept in a file free of everything but
/// <see cref="Mathf"/> so it can be driven property name by property name in
/// <c>tests/GloomhavenVR.WireTests</c>, without a headset and without a game.
///
/// <para>WHY IT IS ITS OWN FILE. Through ModBuild 159 the retune wrote ONE hard-coded property
/// name, <c>_Smoothness</c>, because that is the name the hardware census happened to print off
/// the water material. The surfaces the user actually pointed at — he wrote "Tiles", and the
/// screenshot <c>.planning/debug/spiegeltiles.jpg</c> shows the pool's floor, not only its
/// film — run a DIFFERENT shader, <c>Amp_Basic_N_MRAO</c> (Metallic / Roughness / AO), whose
/// property names we have never read and cannot read offline: the shaders ship compiled inside
/// the game's <c>always_loaded_base*</c> bundles and there is no game install on the build
/// machine to open them with. A retune that depends on knowing a name in advance therefore
/// cannot cover them at all. So the name is discovered at runtime from the shader's own property
/// table and classified HERE.</para>
///
/// <para>THE ONE INVARIANT, and the only thing that makes an unattended write onto an
/// unknown shader defensible: <b>every cap this file returns moves the surface towards LESS
/// environment reflection, never more.</b> Gloss, metal and reflection-strength families are
/// capped DOWNWARD (<c>min</c>) and a roughness family — which means the opposite thing by the
/// same physical quantity — is floored UPWARD (<c>max</c>). Neither can ever raise what the
/// tileset authored, in the sense that matters: neither can make the surface shinier. That is
/// the property <c>WaterReflectionVectors</c> asserts exhaustively, because it is the one whose
/// violation would be invisible in a log and would arrive as "now it mirrors WORSE".</para>
///
/// <para>WHY ROUGHNESS IS NOT SIMPLY CAPPED. <c>_Roughness</c> and <c>_Smoothness</c> are the
/// same axis read from opposite ends. Applying the gloss rule to a roughness property would
/// drive it towards 0 — a perfect mirror — which is precisely the defect being fixed, three
/// rounds in. So roughness inverts explicitly (<see cref="WaterCapFamily.Rough"/>) and is
/// floored at <c>1 - smoothnessCap</c>. A property whose name says BOTH (something matching
/// "rough" and "gloss" at once) has no readable direction at all and is refused outright rather
/// than guessed at — <c>Classify</c> returns <see cref="WaterCapFamily.None"/> and the
/// caller logs the refusal by name, so the next hardware log can name what we declined to
/// touch.</para>
///
/// <para>WHY SELECTORS ARE EXCLUDED. Unity's own Standard shader ships
/// <c>_SmoothnessTextureChannel</c>: a property whose name contains "smoothness" and whose value
/// is an ENUM choosing which texture channel the smoothness is read from. Writing 0.08 into it
/// would not dim a reflection, it would change where the shader reads its data — a semantic
/// corruption that could look like anything. <see cref="WaterReflectionCaps.SelectorTokens"/> is the guard: a name
/// carrying any of those substrings is a map, a channel, a mode or a toggle, never a strength,
/// and is refused. This is a real family of names, not a hypothetical one, which is why the list
/// is a plain documented array rather than a regex.</para>
///
/// <para>WHY THE DECLARED RANGE IS CONSULTED. A cap of 0.08 only means "almost matte" if the
/// property is a normalised 0..1 strength. On a property declared <c>Range(0, 8)</c> — a
/// tiling count, a wave frequency, a power — the same number means something else entirely, and
/// the FREQUENCY-SCRUB lesson of ModBuild 149 (an element strength that was quietly scaling a
/// frequency) is exactly what that costs. So a Range property is only capped when its declared
/// limits lie inside 0..1, and a plain Float — which declares no limits — only when its AUTHORED
/// value already does. Everything else is refused with a stated reason.</para>
/// </summary>
internal enum WaterCapFamily
{
    /// <summary>Not a reflection-strength property, or one whose direction cannot be read.</summary>
    None = 0,

    /// <summary>Smoothness / glossiness: higher = sharper environment reflection. Capped down.</summary>
    Gloss = 1,

    /// <summary>Roughness: the SAME axis inverted, higher = broader lobe. Floored up.</summary>
    Rough = 2,

    /// <summary>Metalness: higher = the environment term replaces the albedo. Capped down.</summary>
    Metal = 3,

    /// <summary>An explicit reflection / cubemap strength scalar. Capped down.</summary>
    Reflection = 4,
}

/// <summary>See <see cref="WaterCapFamily"/> for the whole design record — that enum carries this
/// file's header because the classification is the thing being documented.</summary>
internal static class WaterReflectionCaps
{
    /// <summary>Sharpness family. "smooth" catches <c>_Smoothness</c>/<c>_SmoothnessPower</c>,
    /// "gloss" catches <c>_Glossiness</c>/<c>_GlossMapScale</c>-style names.</summary>
    internal static readonly string[] GlossTokens = { "smooth", "gloss" };

    /// <summary>The inverted reading of the same axis. Kept separate because the fix direction
    /// is the opposite one — see the enum header.</summary>
    internal static readonly string[] RoughTokens = { "rough" };

    /// <summary>Metalness. <c>Amp_Basic_N_MRAO</c> is literally named for it.</summary>
    internal static readonly string[] MetalTokens = { "metal" };

    /// <summary>An explicit environment-reflection strength. Deliberately does NOT include
    /// "spec": <c>_SpecColor</c> is a colour (refused by type at the call site anyway) and
    /// <c>_SpecularPower</c> is an exponent on a scale we cannot read, so the gloss dial already
    /// covers the only reading of specular that is a 0..1 strength.</summary>
    internal static readonly string[] ReflectionTokens = { "reflect", "cubemap", "envmap" };

    /// <summary>Substrings that mark a name as a MAP, a CHANNEL, a MODE or a TOGGLE rather than
    /// a strength — see the enum header for <c>_SmoothnessTextureChannel</c>, the real name this
    /// list exists for. A property carrying any of these is refused whatever else it matches.</summary>
    internal static readonly string[] SelectorTokens =
    {
        "map", "tex", "channel", "chan", "mode", "source", "toggle", "enable", "keyword",
        "index", "uv", "tiling", "offset", "mask", "select",
    };

    /// <summary>
    /// Which reflection family does this shader property name belong to? Case-insensitive
    /// substring matching on the authored name, because that is all a compiled shader hands us.
    /// </summary>
    /// <remarks>
    /// ORDER OF DECISION, and why it is this order:
    /// <list type="number">
    ///   <item>A SELECTOR name is refused first, before any family can claim it. A name is a
    ///   selector or it is a strength; there is no useful third reading.</item>
    ///   <item>A name matching the roughness family AND any downward family at once is refused:
    ///   the two say opposite things about the same quantity, so there is no direction to write
    ///   in. Refusing is the only answer that cannot make the mirror worse.</item>
    ///   <item>Roughness next, so an unambiguous <c>_Roughness</c> inverts rather than being
    ///   swallowed by a looser match.</item>
    ///   <item>Then metal, then reflection, then gloss. Metal and reflection are the stronger
    ///   claims (a name that says "metallic" is about the environment term outright), and all
    ///   three cap in the SAME direction, so this order only decides WHICH dial governs the
    ///   property, never whether the surface gets shinier.</item>
    /// </list>
    /// </remarks>
    internal static WaterCapFamily Classify(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return WaterCapFamily.None;
        string n = propertyName!.ToLowerInvariant();

        // "cubemap" and "envmap" CONTAIN "map", so the selector veto below would fire on every
        // name in the reflection family and that family could never be reached at all. The veto
        // is aimed at a MAP HANDLE — a texture, or a scale applied to one — and the compound
        // words are not that, so they are elided from the probe string only. A genuine handle
        // still loses: '_ReflectionMap' keeps its trailing "map" and is refused.
        string selectorProbe = n.Replace("cubemap", "cube").Replace("envmap", "env");
        if (ContainsAny(selectorProbe, SelectorTokens))
            return WaterCapFamily.None;

        bool rough = ContainsAny(n, RoughTokens);
        bool metal = ContainsAny(n, MetalTokens);
        bool reflection = ContainsAny(n, ReflectionTokens);
        bool gloss = ContainsAny(n, GlossTokens);

        if (rough && (metal || reflection || gloss))
            return WaterCapFamily.None; // two directions in one name — see the remarks above
        if (rough)
            return WaterCapFamily.Rough;
        if (metal)
            return WaterCapFamily.Metal;
        if (reflection)
            return WaterCapFamily.Reflection;
        if (gloss)
            return WaterCapFamily.Gloss;
        return WaterCapFamily.None;
    }

    /// <summary>
    /// What should this property be written to, given what the tileset authored?
    /// </summary>
    /// <param name="family">From <see cref="Classify"/>.</param>
    /// <param name="authored">The value on the shared material — always the source, never the
    /// value already written, so a re-assert can never compound.</param>
    /// <param name="hasRange">True for a <c>Range(a,b)</c> property, false for a plain Float.
    /// The two are gated differently; see the enum header's last paragraph.</param>
    /// <param name="rangeMin">Declared lower limit (ignored when <paramref name="hasRange"/> is
    /// false).</param>
    /// <param name="rangeMax">Declared upper limit (ignored when <paramref name="hasRange"/> is
    /// false).</param>
    /// <param name="smoothnessCap"><c>WaterSettings.Smoothness</c> — the sharpness ceiling, and the
    /// value the roughness floor is derived from.</param>
    /// <param name="reflectivityCap"><c>WaterSettings.Reflectivity</c> — the ceiling for metal and
    /// explicit reflection-strength scalars.</param>
    /// <param name="value">The value to write. Only meaningful when this returns true.</param>
    /// <param name="reason">Always set: either what was done, or why nothing was. Goes verbatim
    /// into the hardware log, so the next round can read what we declined to touch and why —
    /// which is the whole point of refusing rather than guessing.</param>
    /// <returns>True when <paramref name="value"/> should be written.</returns>
    internal static bool TryCap(
        WaterCapFamily family,
        float authored,
        bool hasRange,
        float rangeMin,
        float rangeMax,
        float smoothnessCap,
        float reflectivityCap,
        out float value,
        out string reason)
    {
        value = authored;
        if (family == WaterCapFamily.None)
        {
            reason = "not a reflection-strength property";
            return false;
        }

        // A NaN anywhere in the arithmetic would be written straight onto the material and would
        // read as a black or invisible surface. Refuse before it can.
        if (float.IsNaN(authored) || float.IsInfinity(authored))
        {
            reason = "authored value is not finite";
            return false;
        }

        float lo, hi;
        if (hasRange)
        {
            if (rangeMax <= rangeMin)
            {
                reason = $"declared Range({rangeMin:0.###},{rangeMax:0.###}) is empty";
                return false;
            }
            // Epsilon rather than exact: a shader authored Range(0, 1) can round-trip through the
            // serializer as 1.0000001 and would otherwise be refused for no reason.
            if (rangeMin < -0.001f || rangeMax > 1.001f)
            {
                reason = $"declared Range({rangeMin:0.###},{rangeMax:0.###}) is not a normalised "
                         + "0..1 strength — a cap would mean something else on this scale";
                return false;
            }
            lo = Mathf.Max(rangeMin, 0f);
            hi = Mathf.Min(rangeMax, 1f);
        }
        else
        {
            // A plain Float declares nothing. The only evidence available is the authored value
            // itself: one already inside 0..1 is at least CONSISTENT with a normalised strength,
            // one outside it certainly is not (a wave frequency of 3, an opaque-detail multiplier
            // of 2 — both real values off this very material, ModBuild 159 census).
            if (authored < 0f || authored > 1f)
            {
                reason = $"plain Float authored {authored:0.###} lies outside 0..1, so it is not "
                         + "a normalised strength";
                return false;
            }
            lo = 0f;
            hi = 1f;
        }

        float target = family switch
        {
            // The inverted axis. 1 - cap, so WaterSettings.Smoothness stays the ONE dial that governs
            // sharpness whichever end of the axis a given shader chose to expose.
            WaterCapFamily.Rough => Mathf.Max(authored, 1f - smoothnessCap),
            WaterCapFamily.Metal => Mathf.Min(authored, reflectivityCap),
            WaterCapFamily.Reflection => Mathf.Min(authored, reflectivityCap),
            WaterCapFamily.Gloss => Mathf.Min(authored, smoothnessCap),
            _ => authored,
        };
        value = Mathf.Clamp(target, lo, hi);

        // The clamp above is the last line of defence and it can, in principle, undo the
        // inversion: a shader declaring Range(0, 0.5) for a roughness would clamp our 0.92 floor
        // back down to 0.5, which is still >= authored, so the invariant holds. Assert it anyway
        // rather than trust the reading — a violation here is the one failure that would arrive
        // as "now it mirrors WORSE", and it must never leave this method.
        bool shinier = family == WaterCapFamily.Rough ? value < authored : value > authored;
        if (shinier)
        {
            value = authored;
            reason = "refused: the clamped result would have made the surface SHINIER than "
                     + "authored, which this retune may never do";
            return false;
        }

        reason = Mathf.Approximately(value, authored)
            ? $"{family}: already at or past the cap ({authored:0.###})"
            : $"{family}: {authored:0.###} -> {value:0.###}";
        return true;
    }

    private static bool ContainsAny(string lowered, string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (lowered.IndexOf(tokens[i], StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }
}
