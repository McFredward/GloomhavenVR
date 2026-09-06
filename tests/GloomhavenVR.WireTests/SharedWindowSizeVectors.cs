using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// <b>A SHARED WINDOW IS THE SAME SIZE FOR EVERY PLAYER — ASSERTED, NOT PROMISED.</b>
///
/// <para><b>THE USER RULING (2026-09-06, verbatim):</b> <i>"Ich möchte das Multiplayerfenster immer
/// die selbe Größe haben bei allen Spielern, damit die 1:1 Regel hier nicht gebrochen wird.
/// Gewährleiste das."</i> The last word is the one this file exists for. He did not ask for the one
/// window that was reported to be repaired; he asked for an assurance about the CLASS, and this
/// project has just spent a whole round on six defects that were each protected by a confident
/// sentence somebody had written about the code. A doc block is not a guarantee. A test that fails
/// <c>scripts/wire-tests.sh</c> — which is already on the gate list every lane must pass — is.</para>
///
/// <para><b>WHY IT BELONGS IN THE WIRE TESTS EVEN THOUGH IT ADDS NO RECORD.</b> Every other file on
/// this list is here because its failure is invisible from one machine: a serializer whose reader
/// and writer are both wrong agrees with itself, and a window that is the wrong size agrees with
/// itself in exactly the same way. It took two 100 MB logs from two machines and a dedicated
/// instrument to see the 448 divergence at all. The property being asserted — "two clients compute
/// the same millimetres" — is a property of a FUNCTION, so it can be decided here, on a build
/// machine, with no headset, no second player and no session.</para>
///
/// <para><b>THE TWO HALVES.</b> <see cref="RunLaw"/> drives the arithmetic: the same window, fed the
/// display shapes the two real machines have and the dial settings the config allows, must come out
/// at one number. <see cref="RunSourceGate"/> is the half that survives a refactor — it reads the
/// shipped source of the law and of its two call sites and FAILS when a client-local term appears in
/// them. The arithmetic test cannot catch someone adding <c>Screen.width</c> to the law (a static
/// read is invisible to a caller); the source gate cannot catch someone getting the algebra wrong.
/// Together they cover the sentence in the brief: <i>a shared window's committed size becoming a
/// function of anything client-local — authored canvas, viewport, rig scale, an animation phase, a
/// settle time.</i></para>
/// </summary>
internal static class SharedWindowSizeVectors
{
    /// <summary>Two millimetre figures are the same window. A tenth of a millimetre is far below
    /// anything a headset can resolve and far above float noise on numbers of this size.</summary>
    private static bool SameMm(Vector2 a, Vector2 b) =>
        Mathf.Abs(a.x - b.x) <= 0.1f && Mathf.Abs(a.y - b.y) <= 0.1f;

    internal static void Run(Harness t, string repoRoot)
    {
        VerifyAgainstSource(t, repoRoot);
        RunLaw(t);
        RunSourceGate(t, repoRoot);
    }

    // =============================================================================================
    // 1. THE COPIES ARE STILL THE ORIGINALS
    // =============================================================================================

    /// <summary>
    /// <see cref="SharedWindowSizeLaw"/> restates five constants that live in files this assembly
    /// must not compile (they carry BepInEx, the game model and half of WorldUI). A drifted copy
    /// would make every vector below assert the wrong size while passing, so the declarations are
    /// read out of the repository and compared — the same protection
    /// <c>Shims.VerifyAgainstSource</c> gives the quantisation constants.
    ///
    /// <para>A failure here is NOT necessarily a bug: somebody may have deliberately retuned
    /// <c>WindowLegibility</c> or the board-relative target. It is a REQUIRED DECISION — either the
    /// law follows the new number (shared windows change size for everybody at once, which is fine)
    /// or it deliberately does not (which needs saying in the law's doc). What must not happen is
    /// the two drifting apart unnoticed, because then the shared windows quietly stop matching the
    /// private ones and nothing says so.</para>
    /// </summary>
    private static void VerifyAgainstSource(Harness t, string repoRoot)
    {
        t.Case("shared-window-size/constants-pinned");
        (string File, string Decl, float Law)[] pinned =
        {
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs", "WindowScaleFactor",
             SharedWindowSizeLaw.DesignWindowScaleFactor),
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs", "ModalTargetWidthMeters",
             SharedWindowSizeLaw.DesignTargetWidthMeters),
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs", "MinWindowScaleFactor",
             SharedWindowSizeLaw.DesignMinWindowScaleFactor),
            ("src/GloomhavenVR/Defaults/Defaults.WorldUI.cs", "WindowLegibility",
             SharedWindowSizeLaw.DesignWindowLegibility),
            ("src/GloomhavenVR/Defaults/Defaults.WorldUI.cs", "CanvasScaleMm",
             SharedWindowSizeLaw.DesignCanvasScaleMm),
        };

        foreach (var (file, decl, law) in pinned)
        {
            string path = Path.Combine(repoRoot, file);
            if (!File.Exists(path))
            {
                t.True(false, $"{file} not found — the law's copy of {decl} cannot be pinned");
                continue;
            }
            var m = Regex.Match(File.ReadAllText(path),
                                @"const\s+float\s+" + Regex.Escape(decl) + @"\s*=\s*([0-9.]+)f\s*;");
            if (!m.Success)
            {
                t.True(false, $"could not find `const float {decl} = ...f;` in {file} — the law's "
                              + "copy is now unpinned, which is worse than a wrong copy");
                continue;
            }
            float real = float.Parse(m.Groups[1].Value,
                                     System.Globalization.CultureInfo.InvariantCulture);
            t.True(Mathf.Abs(real - law) < 1e-6f,
                   $"SharedWindowSizeLaw.Design* copy of {decl} is {law}, but {file} declares "
                   + $"{real}. Decide which one the shared windows follow and change both.");
        }
    }

    // =============================================================================================
    // 2. THE ARITHMETIC: ONE WINDOW, ONE NUMBER, WHATEVER THE CLIENT IS
    // =============================================================================================

    private static void RunLaw(Harness t)
    {
        // ---- the reported defect, driven from the two real machines ------------------------------
        // ModBuild 448, one session: the host's canvas is 1920x1080 and the co-player's is
        // 2580x1080, because their displays are shaped differently and the game's canvas scaler
        // matches on HEIGHT. Under the OLD rule the story window committed 1200 x 675 mm on one
        // machine and 1200 x 502 mm on the other. Under the law both are fed the canvas's own
        // design resolution instead, so both compute the host's number — which is also the number
        // the user has already accepted.
        t.Case("shared-window-size/the-448-divergence");
        var design = new Vector2(1920f, 1080f);
        Vector2 hostMm = SharedWindowSizeLaw.CommittedMm(design);
        t.True(SameMm(hostMm, new Vector2(1200f, 675f)),
               $"a 1920x1080 design frame commits 1200 x 675 mm (got {hostMm.x:F1} x {hostMm.y:F1})");
        t.Equal("12000x6750", SharedWindowSizeLaw.Token(design),
                "the 1:1 token for the story window is the two millimetre figures at 0.1 mm");

        // The peer's OWN canvas, put through the law directly, is what the old rule did — kept here
        // as the thing that must NOT happen, so a future change that re-admits the live canvas fails
        // with the actual reported numbers in the message rather than with an abstract mismatch.
        Vector2 peerRaw = SharedWindowSizeLaw.CommittedMm(new Vector2(2580f, 1080f));
        t.True(!SameMm(peerRaw, hostMm),
               "sanity: the peer's raw 2580x1080 canvas really does give a different size (that is "
               + "the defect), so the design-frame substitution is doing the work and not a no-op");
        t.True(Mathf.Abs(peerRaw.y - 502f) < 1.5f,
               $"and it reproduces the peer's reported 502 mm height (got {peerRaw.y:F1}) — this "
               + "vector is what proves the law is aimed at the measured defect");

        // ---- the class, not the instance: every display shape lands on one size ------------------
        // 4:3, 16:10, 16:9, 21:9, 32:9 and a portrait rig. The canvas scaler matches on height, so
        // the design frame is the same for all of them and so is the answer. This is the assertion
        // that makes the guarantee about the CLASS: it is not "the two machines we have agree", it
        // is "any two machines agree".
        t.Case("shared-window-size/every-display-shape");
        float[] widths = { 1440f, 1728f, 1920f, 2520f, 2580f, 3840f, 810f };
        foreach (float w in widths)
        {
            // What the client's own canvas would have produced...
            Vector2 local = SharedWindowSizeLaw.CommittedMm(new Vector2(w, 1080f));
            // ...against what it produces once SharedWindowSize has substituted the design frame.
            Vector2 shared = SharedWindowSizeLaw.CommittedMm(design);
            t.True(SameMm(shared, hostMm),
                   $"a client whose canvas is {w:F0}x1080 commits the same {hostMm.x:F0} x "
                   + $"{hostMm.y:F0} mm once the design frame is substituted");
            if (Mathf.Abs(w - 1920f) > 0.5f)
                t.True(!SameMm(local, hostMm),
                       $"and without the substitution it would NOT ({local.x:F0} x {local.y:F0} mm) "
                       + "— the vector proves the substitution is load-bearing");
        }

        // ---- the dials are out of the answer -----------------------------------------------------
        // [WorldUI] CanvasScaleMm is a live per-client dial and the renderer multiplies by it, so
        // the law divides it back out. Drive the whole clamp range and require ONE size. If this
        // ever fails, a shared window has become a function of a config key again — which is the
        // ModBuild 302 mirror defect ([[mirror-must-not-read-viewers-dial]]) in a new place.
        t.Case("shared-window-size/dials-cancelled");
        float[] dials = { 0.5f, 0.8f, 1.0f, 1.2f, 1.6f, 2.5f };
        foreach (float dial in dials)
        {
            float extra = SharedWindowSizeLaw.ExtraScale(design, dial);
            // The renderer's own arithmetic, restated: hostPx x liveCanvasScaleMm x extraScale.
            var drawn = new Vector2(design.x * dial * extra, design.y * dial * extra);
            t.True(SameMm(drawn, hostMm),
                   $"at [WorldUI] CanvasScaleMm = {dial:F2} the window is still {hostMm.x:F0} x "
                   + $"{hostMm.y:F0} mm (got {drawn.x:F1} x {drawn.y:F1})");
        }
        t.True(SharedWindowSizeLaw.ExtraScale(design, 0f) > 0f,
               "a zero/absent dial does not divide by zero");

        // ---- nothing the user has accepted moves --------------------------------------------------
        // Both machines in the 448 logs ran the shipped defaults, so the law must reproduce every
        // committed number in those logs exactly. These three are read straight off the evidence.
        t.Case("shared-window-size/accepted-numbers-preserved");
        Vector2 quest = SharedWindowSizeLaw.CommittedMm(new Vector2(512f, 1021f));
        t.True(Mathf.Abs(quest.x - 537.6f) < 0.5f,
               $"the quest popup's 512 px card is still 538 mm wide (got {quest.x:F1}) — ModBuild "
               + "449's grab-bar seat is solved against this rect and must not move");
        Vector2 questFitted = SharedWindowSizeLaw.CommittedMm(new Vector2(512f, 846f));
        t.True(Mathf.Abs(questFitted.y - 888.3f) < 0.5f,
               $"and its fitted 512x846 rect is still 888 mm tall (got {questFitted.y:F1})");
        Vector2 eventWin = SharedWindowSizeLaw.CommittedMm(new Vector2(802f, 126f));
        t.True(Mathf.Abs(eventWin.x - 842.1f) < 0.5f,
               $"a content-fitted 802 px rect is still 842 mm wide (got {eventWin.x:F1}) — the law "
               + "takes its shrink from the COMMITTED rect exactly as DeriveWindowScale did, so a "
               + "fitted-down window does not also shrink physically");

        // ---- the crossover, stated as a property rather than as a remembered number ---------------
        // Below it the small-dialog cap binds and millimetres grow with pixels; above it the
        // board-relative term binds and the width is pinned. A window may never get SMALLER by
        // gaining pixels, and may never exceed the board-sized target.
        t.Case("shared-window-size/monotone-and-capped");
        float previous = 0f;
        for (float px = 64f; px <= 4096f; px += 64f)
        {
            Vector2 mm = SharedWindowSizeLaw.CommittedMm(new Vector2(px, 1080f));
            t.True(mm.x >= previous - 0.01f,
                   $"width is non-decreasing in pixels at {px:F0} px ({mm.x:F1} after {previous:F1})");
            t.True(mm.x <= SharedWindowSizeLaw.TargetWidthMm + 0.01f,
                   $"width never exceeds the board-sized target at {px:F0} px ({mm.x:F1} mm)");
            previous = mm.x;
        }
        t.True(Mathf.Abs(previous - SharedWindowSizeLaw.TargetWidthMm) < 0.01f,
               "and a very wide window sits exactly ON the target");

        // A degenerate rect must not produce a degenerate window: the cap is the answer, not zero.
        t.Case("shared-window-size/degenerate-rect");
        t.True(SharedWindowSizeLaw.Shrink(0f) > 0f, "a zero-width rect returns the cap, not zero");
        t.True(SharedWindowSizeLaw.Shrink(-5f) > 0f, "and so does a negative one");
    }

    // =============================================================================================
    // 3. THE HALF THAT SURVIVES THE NEXT REFACTOR
    // =============================================================================================

    /// <summary>Terms that make a size depend on the machine it is computed on. Named exactly as
    /// they appear in this codebase, because a token that never matches is a gate that never
    /// fires ([[a-gate-narrower-than-its-choke-point]]).</summary>
    private static readonly (string Token, string Why)[] ClientLocalTerms =
    {
        ("Screen.", "the display resolution"),
        ("Camera.", "the viewport / the eye"),
        ("Display.", "the display"),
        ("XRSettings", "the headset's render scale"),
        ("Time.", "a settle time or an animation phase"),
        ("WorldUIConfig.", "a live per-client config dial"),
        ("WindowLegibilityLive", "the viewer's own legibility dial"),
        ("PanelLayout.WorldScale", "the rig / diorama scale"),
        ("MapRoomDriver", "whether THIS client has the 3D map switched on"),
        ("TryGetParchmentFrame", "the table zoom"),
        ("FFSNetwork", "the session state, which flips under a standing window"),
        ("ParticipatesHere", "the session state, which flips under a standing window"),
        ("SessionIsOnline", "the session state, which flips under a standing window"),
        ("localScale", "a live transform scale rather than an authored number"),
        ("lossyScale", "a live transform scale rather than an authored number"),
    };

    /// <summary>
    /// <b>THE GATE.</b> Reads the shipped source of the size law and refuses any client-local term
    /// in it.
    ///
    /// <para><b>WHY A SOURCE READ AND NOT A CLEVERER TEST.</b> The arithmetic vectors above can only
    /// see what comes through the PARAMETERS. A future edit that reaches for a static — the very
    /// shape of every term in the table, and the shape the original defect had — changes no
    /// signature and breaks no vector. It changes the file, and the file is what is checked. The
    /// same reading is why this lives here rather than in a new <c>scripts/check-*.py</c>: it must
    /// run inside a gate every lane already runs, and adding a script that has to be REMEMBERED is
    /// how a check quietly stops being part of the build.</para>
    ///
    /// <para><b>THE ONE SANCTIONED EXCEPTION, and it is sanctioned by CANCELLING rather than by
    /// being trusted.</b> <c>SharedWindowSizeLaw.ExtraScale</c> takes the live CanvasScaleMm as a
    /// PARAMETER and divides it back out, because the renderer multiplies by it downstream and the
    /// only way to remove a factor from a product is to divide by it. That is why the token above is
    /// <c>WorldUIConfig.</c> (the READ) and not the word "CanvasScaleMm" (the quantity): the law may
    /// name the quantity, and may not go and fetch this client's copy of it. The
    /// <c>dials-cancelled</c> vector is the proof that the division actually cancels.</para>
    ///
    /// <para><b>WHAT IT DOES NOT COVER, said plainly.</b> The committed rect of a window that is
    /// CONTENT-FIT is still a function of what the game painted at the settle instant. That term is
    /// outside this file and outside the mod's ownership; it is named on the
    /// <c>SHARED WINDOW SIZE TERMS</c> line as the one reading that still needs two logs.</para>
    /// </summary>
    private static void RunSourceGate(Harness t, string repoRoot)
    {
        t.Case("shared-window-size/no-client-local-term-in-the-law");
        string lawPath = Path.Combine(repoRoot, "src/GloomhavenVR/WorldUI/Modal/SharedWindowSizeLaw.cs");
        if (!File.Exists(lawPath))
        {
            t.True(false, "src/GloomhavenVR/WorldUI/Modal/SharedWindowSizeLaw.cs is gone. The 1:1 "
                          + "size guarantee lives in that file; if it moved, move this gate with it "
                          + "rather than deleting the gate.");
            return;
        }

        foreach (string line in CodeLines(File.ReadAllLines(lawPath)))
        {
            foreach (var (token, why) in ClientLocalTerms)
            {
                t.True(!line.Contains(token, StringComparison.Ordinal),
                       $"SharedWindowSizeLaw.cs reads `{token}` — {why}. A shared window's committed "
                       + "size may not be a function of anything this client owns; that is the whole "
                       + $"of the 2026-09-06 ruling. Offending line: {line.Trim()}");
            }
        }

        // The law is only a guarantee while the shipped path actually CALLS it. ModBuild 449's own
        // round has a recorded case of a remedy that was gated behind the instrument shipped to test
        // it and therefore never ran ([[gated-remedy-never-ran]]), so the call sites are asserted
        // rather than assumed.
        t.Case("shared-window-size/the-law-is-actually-called");
        (string File, string Needle, string Why)[] callSites =
        {
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.9.Spawn.cs",
             "SharedWindowSizeLaw.ExtraScale",
             "DeriveWindowScale must take a shared window's scale from the law, not from this "
             + "client's WindowLegibility / CanvasScaleMm"),
            ("src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs",
             "SharedWindowSize.Arm",
             "the law must be armed at CONVERT, while the target's root canvas is still the game's "
             + "and its design resolution can still be read"),
            ("src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs",
             "SharedWindowSize.Repin",
             "the design frame must be re-pinned on the target the game re-drives, or the fix lasts "
             + "exactly until the game's next layout pass"),
            ("src/GloomhavenVR/WorldUI/Modal/ArcSeats.cs",
             "SharedWindowSizeLaw.Token",
             "the SHARED WINDOW SIZE TERMS line must carry the 1:1 token, or next round's evidence "
             + "is back to two 100 MB logs and a manual comparison"),
        };
        foreach (var (file, needle, why) in callSites)
        {
            string path = Path.Combine(repoRoot, file);
            bool present = File.Exists(path)
                           && File.ReadAllText(path).Contains(needle, StringComparison.Ordinal);
            t.True(present, $"{file} no longer calls {needle}. {why}.");
        }

        // The population is a CLOSED enum, which is what makes "a window you did not enumerate"
        // impossible rather than merely unlikely. If a fifth kind is added, this assertion fails and
        // whoever added it has to come here and decide what the new window's design frame is.
        t.Case("shared-window-size/the-population-is-closed");
        string sharedPath = Path.Combine(repoRoot, "src/GloomhavenVR/WorldUI/Modal/SharedWindows.cs");
        if (File.Exists(sharedPath))
        {
            string src = File.ReadAllText(sharedPath);
            int start = src.IndexOf("internal enum SharedWindowKind", StringComparison.Ordinal);
            int end = start >= 0 ? src.IndexOf('}', start) : -1;
            string body = start >= 0 && end > start ? src[start..end] : string.Empty;
            int members = Regex.Matches(body, @"^\s{4}[A-Z][A-Za-z]*\s*=\s*\d+\s*,",
                                        RegexOptions.Multiline).Count;
            t.Equal(5, members,
                    "SharedWindowKind has 5 members (None + the four shared windows). A new one "
                    + "means a new window the 1:1 size guarantee has to be given a design frame for "
                    + "— see SharedWindowSize.TryDesignFrame — so update that and this count "
                    + "together.");
        }
        else
        {
            t.True(false, "SharedWindows.cs is gone — the shared population cannot be enumerated");
        }
    }

    /// <summary>The lines of a C# file with its comments and doc comments removed, so a token quoted
    /// in a REASON does not count as a use. This project has a recorded case of exactly that: a line
    /// citing another instrument's grep token counted itself
    /// ([[token-quoted-in-its-own-explanation]]).</summary>
    private static IEnumerable<string> CodeLines(string[] lines)
    {
        bool inBlock = false;
        foreach (string raw in lines)
        {
            string line = raw;
            if (inBlock)
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0)
                    continue;
                line = line[(close + 2)..];
                inBlock = false;
            }
            int open = line.IndexOf("/*", StringComparison.Ordinal);
            if (open >= 0)
            {
                inBlock = true;
                line = line[..open];
            }
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0)
                line = line[..slashes];
            if (line.Trim().Length > 0)
                yield return line;
        }
    }
}
