// THE GUARD FOR "NO ENVIRONMENT EFFECT MAY EVER BLINK".
//
// USER RULING, hardware, 2026-09-06, verbatim: "Auch das Eis blinkt in nem Loop wenn es nur zur
// Hälfte aktiv ist. Ich hatte die 'Hälften' nie getestet daher ist mir das nie aufgefallen. Ich will
// so ein Blinken generell nicht. Die Umgebungseffekte sollen genau wie beim 'vollen' sein - nur
// weniger in der Anzahl oder weniger intensiv - aber niemals blinkend."
//
// In one line: A HALF-ACTIVE ELEMENT DIFFERS FROM A FULL ONE IN COUNT OR IN AMPLITUDE, NEVER IN
// TIME.
//
// WHY THIS IS A TEST AND NOT A COMMENT. It is the THIRD round on the same defect shape:
//   * ModBuild 445 — the forest GRASS looped while Earth waned. Fixed on the CARD FOLD only, and
//     the fix's own doc block closed with "THE FROST PATH IS UNTOUCHED ... and the pixels still
//     creep", on the argument that a breathing frontier is right for a pixel effect.
//   * ModBuild 448 — the ICE looped while Ice waned, i.e. on the pixel effect that exemption was
//     written for. The user then ruled the motion out everywhere rather than for geometry.
// A sentence in a doc block did not stop the second one, so the invariant is expressed here, where
// a build fails instead of a hardware round.
//
// WHAT IS CHECKED, on the SOURCE of Core/Environment/ElementMood.cs — which is the one and only
// writer of the six element intensities and of the growth channel, so a property of that file's
// text is a property of every environment effect the elements drive:
//
//   1. NO PERIODIC FUNCTION ANYWHERE IN THE FILE. `Mathf.Sin`, `Mathf.Cos`, `Mathf.PingPong`,
//      `Mathf.Repeat` and `Mathf.DeltaAngle` may not appear in real code. The deleted defect was
//      exactly `WaningPlateau + WaningEbbAmplitude * Mathf.Sin(clock * (2f * Mathf.PI / 2.4f))`,
//      and there is no legitimate use for any of them in a channel whose whole contract is three
//      plateaux and two one-shot ramps.
//   2. THE TARGET IS A PURE FUNCTION OF THE COLUMN. `TargetFor` must take exactly one parameter,
//      an `EColumn`, and its body must not mention a clock. A target that cannot see the clock
//      cannot oscillate, whatever else is edited around it.
//   3. THE THREE PLATEAUX ARE CONSTANTS. `TargetFor`'s three arms must be `1f`, the plateau
//      identifier and `0f` — no expression, no second term.
//   4. THE INSTRUMENT MEASURES THE SHIPPED CURVE. `TimeDrift` — the hardware line's steadiness
//      field — must call `ValueAt`, the same evaluator the per-frame smoothing calls. An
//      instrument that re-implemented the curve would agree with itself forever while the shipped
//      one drifted, which is this project's most expensive recorded failure mode.
//
// It reads SOURCE and not the compiled DLL, in the manner of BundledShaderVectors and
// ConfigStepVectors: the property is a property of the text.
//
// COMMENTS AND STRING LITERALS ARE STRIPPED FIRST, and that is load-bearing rather than tidy. This
// file's own subject is discussed at length in that file's comments — the deleted `Mathf.Sin` line
// is quoted verbatim in the code it was deleted from, on purpose, so the next reader knows what was
// there. A scan that counted the explanation as an occurrence would fail on a correct build, which
// is a gate nobody would keep.

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class ElementSteadyVectors
{
    private const string MoodRelPath = "src/GloomhavenVR/Core/Environment/ElementMood.cs";

    /// <summary>The periodic functions a mood channel may never contain. Spelled in pieces so this
    /// file could never itself trip the sweep if it were ever widened past src/.</summary>
    private static readonly string[] Periodic =
    {
        "Mathf" + ".Sin", "Mathf" + ".Cos", "Mathf" + ".PingPong", "Mathf" + ".Repeat",
        "Mathf" + ".DeltaAngle",
    };

    internal static void Run(Harness t, string repoRoot)
    {
        t.Case("element mood — the channel carries no clock");

        string path = System.IO.Path.Combine(repoRoot, MoodRelPath);
        if (!System.IO.File.Exists(path))
        {
            t.True(false, $"{MoodRelPath} not found — the element channel moved and this guard did not");
            return;
        }

        string code = StripCommentsAndStrings(System.IO.File.ReadAllText(path));

        // ---- 1. no periodic function anywhere in the channel ----
        foreach (string fn in Periodic)
        {
            t.True(code.IndexOf(fn, StringComparison.Ordinal) < 0,
                   $"{MoodRelPath} contains {fn} in real code. The element channel publishes three "
                   + "plateaux (Strong 1.00, Waning 0.40, Inert 0.00) and two one-shot ramps, and a "
                   + "periodic function in it is the 2026-09-06 no-blinking ruling being broken: "
                   + "\"Die Umgebungseffekte sollen genau wie beim 'vollen' sein - nur weniger in "
                   + "der Anzahl oder weniger intensiv - aber niemals blinkend.\"");
        }

        // ---- 2/3. the target is a pure function of the column, and its arms are constants ----
        var target = new Regex(
            @"private\s+static\s+float\s+TargetFor\s*\(\s*ElementInfusionBoardManager\.EColumn\s+\w+\s*\)"
            + @"\s*=>\s*\w+\s+switch\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);
        Match m = target.Match(code);
        t.True(m.Success,
               "TargetFor(EColumn) => column switch { … } not found in " + MoodRelPath
               + ". It is the one place a clock could re-enter the published channel, so its "
               + "SHAPE — one parameter, a column, no second argument — is the invariant.");
        if (!m.Success)
            return;

        string body = m.Groups["body"].Value;
        t.True(body.IndexOf("clock", StringComparison.OrdinalIgnoreCase) < 0,
               "TargetFor's body mentions a clock. A target that can see the clock can oscillate, "
               + "which is precisely the term deleted on 2026-09-06.");
        t.True(Regex.IsMatch(body, @"Strong\s*=>\s*1f\s*,"),
               "TargetFor's Strong arm is not the constant 1f.");
        t.True(Regex.IsMatch(body, @"Waning\s*=>\s*WaningPlateau\s*,"),
               "TargetFor's Waning arm is not the bare WaningPlateau constant. Anything else there "
               + "is a half-active element differing from a full one by more than its amplitude.");
        t.True(Regex.IsMatch(body, @"_\s*=>\s*0f\s*,"),
               "TargetFor's default (Inert) arm is not the constant 0f.");

        // ---- 4. the instrument measures the shipped curve ----
        var drift = new Regex(@"private\s+static\s+float\s+TimeDrift\s*\([^)]*\)\s*\{(?<body>.*?)\n    \}",
                              RegexOptions.Singleline);
        Match d = drift.Match(code);
        t.True(d.Success, "TimeDrift not found — the ELEMENT STEADY line's steadiness field is gone.");
        if (d.Success)
            t.True(d.Groups["body"].Value.IndexOf("ValueAt(", StringComparison.Ordinal) >= 0,
                   "TimeDrift does not call ValueAt. The steadiness reading must re-run the SHIPPED "
                   + "evaluator; an instrument with its own copy of the curve agrees with itself "
                   + "forever while the published value drifts.");
    }

    /// <summary>
    /// Blank out <c>//</c> and <c>/* */</c> comments and every string literal, keeping the text's
    /// length and line structure so a future assertion could still report a position. See the header
    /// for why: the file under test quotes the deleted defect verbatim in its own comments, and a
    /// scan that read the explanation as an occurrence would fail a correct build.
    /// </summary>
    private static string StripCommentsAndStrings(string src)
    {
        var sb = new StringBuilder(src.Length);
        int i = 0;
        while (i < src.Length)
        {
            char c = src[i];

            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                sb.Append("  ");
                i += 2;
                while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/'))
                {
                    sb.Append(src[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i < src.Length) { sb.Append("  "); i += 2; }
                continue;
            }
            if (c == '"')
            {
                // Verbatim strings are not used in the file under test; a plain literal ends at the
                // first unescaped quote and cannot span a line, so a newline also closes it rather
                // than running the stripper off the end of a malformed file.
                sb.Append(' ');
                i++;
                while (i < src.Length && src[i] != '"' && src[i] != '\n')
                {
                    if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(' '); i++; }
                    sb.Append(' ');
                    i++;
                }
                if (i < src.Length && src[i] == '"') { sb.Append(' '); i++; }
                continue;
            }
            if (c == '\'')
            {
                sb.Append(' ');
                i++;
                while (i < src.Length && src[i] != '\'' && src[i] != '\n')
                {
                    if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(' '); i++; }
                    sb.Append(' ');
                    i++;
                }
                if (i < src.Length && src[i] == '\'') { sb.Append(' '); i++; }
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
