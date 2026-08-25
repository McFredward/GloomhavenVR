using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE CAPTION MUST NEVER BE CUT. This file is the deliverable of the 2026-08-25 truncation round.
///
/// <para><b>THE REPORT.</b> A board keycap on hardware (ModBuild 281) read <c>"AUSWAHL BEEN"</c>
/// where the game had said <c>"Auswahl beenden"</c>. The user: <i>"WICHTIG: Der Text muss immer voll
/// lesbar sein. […] Das darf nicht passieren."</i> The cause was <c>TextOverflowModes.Truncate</c>
/// plus an auto-size floor of 0.08 in a caption band ModBuild 281 had just cut by 58 %.</para>
///
/// <para><b>WHY A PROPERTY AND NOT A LIST OF STRINGS.</b> The corpus harvest for this round read
/// every <c>readyButton.Toggle</c>, <c>m_UndoButton</c>, <c>m_SkipButton.Toggle</c> and
/// pick-<c>DialogOption</c> call site in <c>decompiled/</c>. The longest FIXED caption is 23
/// characters (DE <c>"Wähle eine andere Karte"</c>, <c>GUI_CHOOSE_OTHER_CARD</c>,
/// <c>CardsHandUI.cs:2099</c>) — but three of the keys these caps display are FORMAT STRINGS with
/// runtime insertions and are therefore unbounded:
/// <c>GUI_END_TURN</c>/<c>GUI_END_EXTRA_TURN</c> interpolate the actor's CLASS NAME
/// (<c>Choreographer.cs:12709</c>); <c>GUI_LOSE_CARD</c>/<c>GUI_DISCARD_CARD</c> interpolate the
/// ABILITY CARD TITLE (<c>CardsHandUI.cs:2034/2038</c>, which is exactly what
/// <c>PickDialogOptionLabel(cancel: false)</c> reads); <c>GUI_CONFIRM_TARGETS</c> appends a live
/// counter (<c>Choreographer.cs:4918</c>). <b>There is no longest string.</b> So the assertion is
/// not "these strings fit", it is "<i>whatever arrives, every character of it is drawn</i>" —
/// <c>StripWhitespace(out) == StripWhitespace(in)</c>, over the harvested corpus, over adversarial
/// inputs, over a NULL input, and at three different font-metric models.
/// </para>
///
/// <para><b>THE INSTRUMENT IS STATED, NOT HIDDEN.</b> The real font (MarcellusSC, harvested from
/// the game at runtime) cannot be measured on this machine, so these vectors drive the solver with
/// a UNIFORM-ADVANCE model — every glyph the same width, in em. That is not the real font and is
/// not claimed to be. It is run at THREE advances, <see cref="AdvanceNarrow"/> 0.50,
/// <see cref="AdvanceTypical"/> 0.62 and <see cref="AdvanceWide"/> 0.75, which brackets any serif
/// small-caps face by a wide margin — and the two things being asserted (no character is dropped;
/// a string that cannot fit is FLAGGED rather than cut) are properties of the wrap, not of the
/// metric. The one number that IS metric-dependent — the achieved font size — is asserted only as
/// "at or above the floor", and the floor is the mod's own constant.</para>
///
/// <para><b>AND THE RUNTIME CHECKS ITS OWN METRICS.</b> <c>Core.TmpFit.FitCapLabel</c> reads the
/// rendered extent back off TMP after applying the layout and logs when it disagrees with the
/// solved block by more than 15 %. This file cannot see the real font; that line can, and it says
/// so on hardware rather than leaving it to a screenshot.</para>
/// </summary>
internal static class CapLabelFitVectors
{
    // ---- the three fitted board caps (Cards/BoardAnchors.FitCapSize against each board's own
    //      measured seat recess; the same table PreviewKeycaps.cs and cap_atlas.py carry) --------
    private static readonly (string Name, float W, float H)[] Boards =
    {
        ("Oak",    0.0630f, 0.0563f),
        ("Steel",  0.0630f, 0.0621f),
        ("Bronze", 0.0532f, 0.0439f),
    };

    /// <summary>Uniform glyph advance in em — narrow, typical and deliberately pessimistic. See the
    /// class header: the model is stated because it is a model.</summary>
    private const float AdvanceNarrow = 0.50f;

    private const float AdvanceTypical = 0.62f;

    private const float AdvanceWide = 0.75f;

    /// <summary>TMP world metric: font size 1.0 draws an em of about 0.1 local meters, and a line
    /// advance of about 1.2 em. Both are the shipped helper's own documented rule
    /// (<c>Core/TmpFit.cs</c>: "fontSize / 10 ≈ line height in local meters").</summary>
    private const float EmPerFontUnit = 0.1f;

    private const float LineStep = EmPerFontUnit * 1.2f;

    /// <summary>The keycap builders' own <c>maxFontSize</c> — <c>BoardButton.Create</c> and
    /// <c>InertCap.BuildLabel</c> both pass 0.40.</summary>
    private const float MaxFont = 0.40f;

    /// <summary>
    /// THE HARVESTED CORPUS. Every string a board keycap can display that could be reached from
    /// this machine, longest first. Provenance is on each line; the four marked TRANSCRIBED live in
    /// mod comments rather than in the game's own string table (the game runs on a Windows box and
    /// its I2 table is not on this machine), so they are plausible rather than proven — which is
    /// why nothing here asserts an exact width for any of them, only the property.
    /// </summary>
    private static readonly string[] Corpus =
    {
        "Wähle eine andere Karte",   // 23 DE  GUI_CHOOSE_OTHER_CARD (CardsHandUI.cs:2099) TRANSCRIBED
        "Bewegung überspringen",     // 21 DE  GUI_SKIP_MOVEMENT (SkipButton.cs:47)        TRANSCRIBED
        "Angriff überspringen",      // 20 DE  GUI_SKIP_ATTACK (Choreographer, 7 sites)     TRANSCRIBED
        "Choose another card",       // 19 EN  mod fallback, CardsDriver.6.Flows.cs:463
        "BELOHNUNG ABGEBEN",         // 17 DE  Loc.cs:210 item_lose_reward
        "ITEM AUFFRISCHEN",          // 16 DE  Loc.cs:204 item_refresh_confirm
        "Change selection",          // 16 EN  Loc.cs:944 confirm_unready
        "Mach dich bereit",          // 16 DE  GUI_READY, user report (NativeButtonSkin.cs:528)
        "Auswahl beenden",           // 15 DE  OBSERVED ON THE RIG — Player.log:5208. The report.
        "Karten abwerfen",           // 15 DE  GUI_DISCARD_CARDS (CardsHandUI.cs:2038)      TRANSCRIBED
        "FORFEIT REWARD",            // 14 EN  Loc.cs:210
        "SURRENDER ITEM",            // 14 EN  Loc.cs:203
        "ELEMENT WÄHLEN",            // 14 DE  Loc.cs:947 item_choose_element
        "CHOOSE ELEMENT",            // 14 EN  Loc.cs:947
        "Auswahl ändern",            // 14 DE  Loc.cs:944
        "END SELECTION",             // 13 EN  PlayTray.5.Status.cs:242
        "REFRESH ITEM",              // 12 EN  Loc.cs:204
        "ITEM ABGEBEN",              // 12 DE  Loc.cs:203
        "Fortfahren",                // 10 DE  OBSERVED ON THE RIG — GUI_CONTINUE
        "BENUTZEN",                  //  8 DE  Loc.cs:202 item_use_area
        "Confirm",                   //  7 EN  CardsGameApi.cs:2509
        "WEITER",                    //  6 DE  Loc.cs:189 pick_batch_next
        "NEXT",                      //  4 EN  Loc.cs:189
        "Undo",                      //  4 EN  CardsGameApi.cs:2558
        "Skip",                      //  4 EN  CardsGameApi.cs:2632
        "USE",                       //  3 EN  Loc.cs:202
    };

    /// <summary>
    /// THE UNBOUNDED CASES, written out. These are not harvested strings — they are what
    /// <c>GUI_END_TURN</c> and <c>GUI_LOSE_CARD</c> BECOME once the game has interpolated a class
    /// name or an ability-card title into them, which is the thing the corpus above cannot bound.
    /// The German compounds are the point: they are single tokens longer than the cap, i.e. the
    /// hard-break path.
    /// </summary>
    private static readonly string[] Interpolated =
    {
        "Zug beenden: Zauberweberin",
        "Verlieren: Mächtiges Band",
        "Abwerfen: Unerschütterlichkeit",
        "Zug beenden: Gedankendieb",
        "Ziele bestätigen 3/5",
    };

    internal static void Run(Harness t, string repoRoot)
    {
        NullInputIsNotALayout(t);
        NothingIsEverDropped(t);
        TheHarvestedCorpusFitsEveryBoard(t);
        AStringThatCannotFitIsFlaggedAndStillWhole(t);
        TheReportedStringWrapsInsteadOfCutting(t);
        BiggerBoxNeverMeansSmallerText(t);
        TheSolveIsDeterministic(t);
        TheFaceBudgetIsInternallyConsistent(t);
        TheAtlasGeneratorAgreesWithTheCode(t, repoRoot);
        NoTruncateModeSurvivesInTheMod(t, repoRoot);
        DumpTableIfAsked();
    }

    /// <summary>
    /// With <c>GHVR_CAPTION_TABLE=1</c> in the environment, print every corpus string's solved
    /// layout as TSV. It asserts nothing — it exists so the CONTACT SHEET that goes to the user can
    /// be drawn from THIS solver's output rather than from a second implementation of it. (The sheet
    /// is drawn outside Unity because TextMeshPro is not in the companion project's package
    /// manifest, so no station in this repository can render the shipped SDF material — the same
    /// limitation the ModBuild 281 <c>standin_*</c> files carry, and it is stated on the sheet.)
    /// </summary>
    private static void DumpTableIfAsked()
    {
        if (Environment.GetEnvironmentVariable("GHVR_CAPTION_TABLE") != "1")
            return;
        Console.WriteLine("#CAPTABLE\tboard\tadvance\tfont\tlines\toverflow\thardbroke\ttext");
        for (int b = 0; b < Boards.Length; b++)
        {
            var (bw, bh) = CaptionBox(b);
            foreach (float adv in new[] { AdvanceNarrow, AdvanceTypical, AdvanceWide })
            {
                foreach (string str in Everything())
                {
                    var c = Fit(str, bw, bh, adv);
                    Console.WriteLine($"#CAPTABLE\t{Boards[b].Name}\t{adv:F2}\t{c.FontSize:F5}\t{c.Lines}"
                        + $"\t{c.Overflows}\t{c.HardBroke}\t{c.Text.Replace("\n", "|")}");
                }
            }
        }
    }

    // ------------------------------------------------------------------ the metric model -------

    private static Func<string, float> Model(float advanceEm) =>
        s => EmPerFontUnit * advanceEm * s.Length;

    private static CapFaceLayout.Caption Fit(string text, float boxW, float boxH, float advance) =>
        CapFaceLayout.FitCaption(text, boxW, boxH, MaxFont, Model(advance), LineStep);

    private static (float W, float H) CaptionBox(int board)
    {
        Vector2 frac = CapFaceLayout.CaptionBoxWithSymbol;
        return (Boards[board].W * frac.x, Boards[board].H * frac.y);
    }

    private static IEnumerable<string> Everything()
    {
        foreach (string s in Corpus) yield return s;
        foreach (string s in Interpolated) yield return s;
    }

    // ------------------------------------------------------------------ the null control -------

    /// <summary>
    /// THE NULL INPUT, run first and on purpose. A new instrument's first output is a hypothesis,
    /// and a solver that answers confidently about nothing is a solver that will answer confidently
    /// about a string too. Empty, null and all-whitespace must come back untouched, at a legible
    /// size, and NOT flagged as an overflow — an empty caption fits every box there is.
    /// </summary>
    private static void NullInputIsNotALayout(Harness t)
    {
        var (bw, bh) = CaptionBox(2);
        foreach (string? s in new[] { null, "", "   ", "\n\t " })
        {
            var c = Fit(s!, bw, bh, AdvanceTypical);
            t.True(CapFaceLayout.StripWhitespace(c.Text).Length == 0,
                   $"null control: an empty caption must stay empty, got \"{c.Text}\"");
            t.True(!c.Overflows, "null control: an empty caption cannot overflow its box");
            t.True(c.FontSize >= CapFaceLayout.MinFontSize,
                   $"null control: font {c.FontSize:F4} fell below the floor {CapFaceLayout.MinFontSize:F4}");
        }
        // A degenerate BOX, which is the other null: a caller that has not sized its plate yet must
        // get its string back whole rather than a layout invented for a zero-area rectangle.
        var d = Fit("Auswahl beenden", 0f, 0f, AdvanceTypical);
        t.True(CapFaceLayout.StripWhitespace(d.Text) == CapFaceLayout.StripWhitespace("Auswahl beenden"),
               "null control: a zero-area box must not eat the caption");
        t.True(d.Overflows, "null control: a zero-area box must report that it did not fit");
    }

    // ------------------------------------------------------------------ THE PROPERTY -----------

    /// <summary>
    /// <b>THE ASSERTION THIS WHOLE ROUND EXISTS FOR.</b> For every string, on every board, at every
    /// metric model, in every box down to absurdly small ones: the laid-out caption contains exactly
    /// the input's characters, in order, once whitespace is removed. Line breaks may be added; a
    /// word may be split; nothing may be removed. "AUSWAHL BEEN" fails this test.
    /// </summary>
    private static void NothingIsEverDropped(Harness t)
    {
        float[] advances = { AdvanceNarrow, AdvanceTypical, AdvanceWide };
        // Boxes from the real caption box down to one glyph wide — the shrinking end is where a
        // truncating implementation stops being able to hide.
        float[] scales = { 1f, 0.5f, 0.25f, 0.1f, 0.02f };
        int checks = 0;
        foreach (string s in Everything())
        {
            string want = CapFaceLayout.StripWhitespace(s);
            for (int b = 0; b < Boards.Length; b++)
            {
                var (bw, bh) = CaptionBox(b);
                foreach (float adv in advances)
                {
                    foreach (float k in scales)
                    {
                        var c = Fit(s, bw * k, bh * k, adv);
                        string got = CapFaceLayout.StripWhitespace(c.Text);
                        t.True(got == want,
                               $"CHARACTERS DROPPED on {Boards[b].Name} at scale {k:G} adv {adv:G}: " +
                               $"\"{s}\" became \"{c.Text}\"");
                        checks++;
                    }
                }
            }
        }
        // A sweep that silently ran zero cases would pass every assertion above it. Say how many.
        // 31 strings x 3 boards x 3 metric models x 5 box scales = 1395.
        t.True(checks == 1395, $"the no-drop sweep must run every case: {checks} were checked, expected 1395");
    }

    // ------------------------------------------------------------------ the corpus fits --------

    /// <summary>
    /// Every harvested caption fits inside its plate on all three boards, at or above the floor,
    /// with no overflow — at the narrow and typical metric models. THE WIDE MODEL IS DELIBERATELY
    /// NOT ASSERTED HERE: see <see cref="AStringThatCannotFitIsFlaggedAndStillWhole"/>, which uses
    /// it as the known-positive control for the loud path.
    /// </summary>
    private static void TheHarvestedCorpusFitsEveryBoard(Harness t)
    {
        foreach (float adv in new[] { AdvanceNarrow, AdvanceTypical })
        {
            for (int b = 0; b < Boards.Length; b++)
            {
                var (bw, bh) = CaptionBox(b);
                foreach (string s in Corpus)
                {
                    var c = Fit(s, bw, bh, adv);
                    t.True(!c.Overflows,
                           $"{Boards[b].Name} @adv {adv:G}: \"{s}\" does NOT fit its caption box " +
                           $"({bw * 1000f:F1} x {bh * 1000f:F1} mm) — needed " +
                           $"{c.BlockWidth * 1000f:F1} x {c.BlockHeight * 1000f:F1} mm at font {c.FontSize:F4}");
                    t.True(c.FontSize >= CapFaceLayout.MinFontSize - 1e-6f,
                           $"{Boards[b].Name} @adv {adv:G}: \"{s}\" solved BELOW the floor " +
                           $"({c.FontSize:F4} < {CapFaceLayout.MinFontSize:F4})");
                    t.True(c.Lines >= 1 && c.Lines <= 4,
                           $"{Boards[b].Name} @adv {adv:G}: \"{s}\" wrapped to {c.Lines} lines — a caption " +
                           "that needs five lines on a 44 mm key is not a fit, it is a paragraph");
                }
            }
        }
    }

    // ------------------------------------------------------------------ the loud path ----------

    /// <summary>
    /// THE KNOWN-POSITIVE CONTROL. A test that only ever sees things fit cannot tell a working
    /// solver from one that reports success unconditionally — which is exactly what
    /// <c>Truncate</c> did for eleven builds. So: drive a case that genuinely does not fit and
    /// assert BOTH halves of the contract — <see cref="CapFaceLayout.Caption.Overflows"/> is raised
    /// (so <c>TmpFit</c> logs it by name), AND every character is still there.
    ///
    /// <para>Two cases. The first is synthetic and unmistakable: one 200-character token in the real
    /// Bronze box. The second is the realistic boundary — the interpolated
    /// <c>"Zug beenden: Zauberweberin"</c> on Bronze under the deliberately WIDE metric model, which
    /// is the case the corpus sweep above stops short of. If either ever starts passing as "fits",
    /// the flag has stopped meaning anything.</para>
    /// </summary>
    private static void AStringThatCannotFitIsFlaggedAndStillWhole(Harness t)
    {
        var (bw, bh) = CaptionBox(2);

        string monster = new string('W', 200);
        var m = Fit(monster, bw, bh, AdvanceTypical);
        t.True(m.Overflows, "known-positive: a 200-glyph token in a 39 x 20 mm box must be FLAGGED");
        t.True(CapFaceLayout.StripWhitespace(m.Text) == monster,
               "known-positive: the flagged caption must still contain every character");
        t.True(m.HardBroke, "known-positive: a single token wider than the box must be hard-broken");
        t.True(Math.Abs(m.FontSize - CapFaceLayout.MinFontSize) < 1e-6f,
               $"known-positive: a caption that cannot fit is drawn at the floor, got {m.FontSize:F4}");

        var w = Fit("Zug beenden: Zauberweberin", bw, bh, AdvanceWide);
        t.True(w.Overflows,
               "known-positive: the interpolated END-TURN caption on Bronze under the WIDE metric " +
               "model is the realistic boundary case and must be flagged, not silently accepted");
        t.True(CapFaceLayout.StripWhitespace(w.Text)
               == CapFaceLayout.StripWhitespace("Zug beenden: Zauberweberin"),
               "known-positive: the boundary caption must still contain every character");
    }

    /// <summary>
    /// THE REPORT, REPRODUCED AND CLOSED. <c>"Auswahl beenden"</c> in the OLD caption box on the
    /// Bronze cap could not be laid out in two lines above the OLD 0.08 floor — which is precisely
    /// why the old code fell through to <c>Truncate</c> and produced "AUSWAHL BEEN". In the NEW box
    /// it wraps to two lines at a font size comfortably above the new floor. Both halves are
    /// asserted, because the second one alone would not show that anything had changed.
    /// </summary>
    private static void TheReportedStringWrapsInsteadOfCutting(Harness t)
    {
        const string report = "Auswahl beenden";
        const float oldFloor = 0.08f;
        var (bwOld, bhOld) = (Boards[2].W * 0.92f, Boards[2].H * 0.36f);   // the ModBuild 281 box

        // The old box, judged by the old floor: two lines need 2 x lineStep x 0.08 of height.
        t.True(2f * LineStep * oldFloor > bhOld,
               $"the ModBuild 281 box ({bhOld * 1000f:F1} mm) should NOT have held two lines at the old " +
               $"{oldFloor:F2} floor — if it does, the reproduction of the report is wrong and every " +
               "conclusion drawn from it is suspect");

        var (bw, bh) = CaptionBox(2);
        var c = Fit(report, bw, bh, AdvanceTypical);
        t.True(!c.Overflows, $"\"{report}\" must fit the new Bronze caption box");
        t.True(c.Lines == 2, $"\"{report}\" should wrap to two lines on Bronze, got {c.Lines}");
        t.True(c.Text == "Auswahl\nbeenden",
               $"\"{report}\" should break at its one space, got \"{c.Text}\"");
        t.True(c.FontSize > oldFloor,
               $"\"{report}\" solves at {c.FontSize:F4}, which must clear the OLD {oldFloor:F2} floor — " +
               "the fix is supposed to make the caption bigger, not merely whole");
        t.True(!c.HardBroke, $"\"{report}\" must not need a mid-word break on any board");

        // …and on the two larger boards it must be at least as comfortable.
        for (int b = 0; b < 2; b++)
        {
            var (w2, h2) = CaptionBox(b);
            var c2 = Fit(report, w2, h2, AdvanceTypical);
            t.True(!c2.Overflows && c2.Lines == 2 && c2.FontSize >= c.FontSize - 1e-6f,
                   $"{Boards[b].Name}: \"{report}\" solved at {c2.FontSize:F4}/{c2.Lines} lines, " +
                   $"worse than Bronze's {c.FontSize:F4}/{c.Lines} on a LARGER cap");
        }
    }

    // ------------------------------------------------------------------ solver sanity ----------

    /// <summary>
    /// MONOTONICITY, AND THE ONE PLACE IT IS ALLOWED TO BREAK.
    ///
    /// <para>Each solve is a bisection over "does it fit", and that predicate is only monotone if a
    /// bigger box can never produce a smaller font. A violation would not throw and would not look
    /// wrong — it would produce one cap in a row of three with visibly smaller text, which gets
    /// reported as "the buttons look inconsistent" and then attributed to anything but the wrap.</para>
    ///
    /// <para><b>The exception is deliberate and is asserted rather than excused.</b>
    /// <c>FitCaption</c> solves TWICE: whole words first, mid-word breaks only if that fails. So as
    /// a box grows there is exactly ONE size at which the answer switches from "big, with a word
    /// split" to "smaller, with the words whole" — and the font drops there, once, and only there.
    /// This asserts precisely that: at most one decrease over the whole sweep, and the decrease must
    /// coincide with <c>HardBroke</c> going true → false. A second drop, or a drop with no policy
    /// change under it, is the real defect.</para>
    /// </summary>
    private static void BiggerBoxNeverMeansSmallerText(Harness t)
    {
        foreach (string s in Everything())
        {
            var (bw, bh) = CaptionBox(2);
            float prev = 0f;
            bool prevHard = false, first = true;
            int drops = 0;
            for (float k = 0.2f; k <= 2.0f; k += 0.05f)
            {
                var c = Fit(s, bw * k, bh * k, AdvanceTypical);
                if (!first && c.FontSize < prev - 1e-4f)
                {
                    drops++;
                    t.True(prevHard && !c.HardBroke,
                           $"non-monotone on \"{s}\": box x{k:F2} solved {c.FontSize:F4} after " +
                           $"{prev:F4}, and the break policy did NOT change under it " +
                           $"(hardBroke {prevHard} -> {c.HardBroke})");
                }
                prev = c.FontSize;
                prevHard = c.HardBroke;
                first = false;
            }
            t.True(drops <= 1,
                   $"\"{s}\": the size dropped {drops} times as the box grew — the whole-words-first " +
                   "policy can cost size exactly once, when it stops needing a mid-word break");
        }
    }

    /// <summary>Same input, same answer. A solver with a hidden dependence on call order would give
    /// a cap whose caption re-lays differently on a rebuild — and every board keycap is rebuilt on
    /// any live [BoardButtons] edit, ten times over in one session of the user's own tuning.</summary>
    private static void TheSolveIsDeterministic(Harness t)
    {
        var (bw, bh) = CaptionBox(1);
        foreach (string s in Everything())
        {
            var a = Fit(s, bw, bh, AdvanceTypical);
            var b = Fit(s, bw, bh, AdvanceTypical);
            t.True(a.Text == b.Text && Math.Abs(a.FontSize - b.FontSize) < 1e-7f && a.Lines == b.Lines,
                   $"non-deterministic on \"{s}\": ({a.Lines}, {a.FontSize:F6}) then ({b.Lines}, {b.FontSize:F6})");
        }
    }

    // ------------------------------------------------------------------ the face budget --------

    /// <summary>
    /// THE THREE BANDS MUST TILE THE FIELD, and they are three numbers in two files plus a bezel in
    /// a third. The caption band, the gap and the symbol band must all lie inside the recessed field
    /// and must not overlap — a caption band that runs into the symbol band puts live game text
    /// straight through a carved crescent, which is a defect nothing else here can see.
    /// </summary>
    private static void TheFaceBudgetIsInternallyConsistent(Harness t)
    {
        t.True(Math.Abs(CapFaceLayout.BezelTotal
                        - (CapFaceLayout.BezelChamfer + CapFaceLayout.BezelRim + CapFaceLayout.BezelStep)) < 1e-6f,
               "the bezel total must be the sum of its three zones");
        t.True(Math.Abs(CapFaceLayout.FieldLo - CapFaceLayout.BezelTotal) < 1e-6f,
               $"the field must start where the bezel ends: FieldLo {CapFaceLayout.FieldLo:F4} vs " +
               $"BezelTotal {CapFaceLayout.BezelTotal:F4}");
        t.True(Math.Abs((CapFaceLayout.FieldLo + CapFaceLayout.FieldHi) - 1f) < 1e-6f,
               "the field must be centred on the cap");

        Vector2 box = CapFaceLayout.CaptionBoxWithSymbol;
        float dy = CapFaceLayout.CaptionCentreYWithSymbol;
        float lo = 0.5f + dy - box.y * 0.5f;      // caption band bottom, in cap v
        float hi = 0.5f + dy + box.y * 0.5f;      // caption band top
        t.True(lo >= CapFaceLayout.FieldLo - 1e-4f,
               $"the caption band bottom {lo:F4} falls off the field ({CapFaceLayout.FieldLo:F4})");
        t.True(hi <= CapFaceLayout.FieldHi - 0.02f,
               $"the caption band top {hi:F4} leaves no gap below the symbol band " +
               $"(field ends {CapFaceLayout.FieldHi:F4})");
        t.True(box.x <= CapFaceLayout.FieldHi - CapFaceLayout.FieldLo + 1e-4f,
               $"the caption box is {box.x:F4} of the cap width but the field is only " +
               $"{CapFaceLayout.FieldHi - CapFaceLayout.FieldLo:F4} — the caption would overhang the bezel, " +
               "which is what the (0.92, 0.85) it replaced actually did");

        Vector2 plain = CapFaceLayout.CaptionBoxNoSymbol;
        t.True(plain.x <= CapFaceLayout.FieldHi - CapFaceLayout.FieldLo + 1e-4f
               && plain.y <= CapFaceLayout.FieldHi - CapFaceLayout.FieldLo + 1e-4f,
               $"the no-symbol caption box {plain} must also stay inside the field");
        t.True(plain.y > box.y, "a cap with no symbol must give its caption MORE height, not less");
    }

    /// <summary>
    /// A SOURCE LINT, and the one gate that can catch the ModBuild 281 defect at its origin. The
    /// symbol is CARVED into the atlas by <c>unity/board-prep/buttons/cap_atlas.py</c> and the
    /// caption is placed by <c>Cards/CapFaceLayout.cs</c>. They are two files that never meet, and
    /// the last round changed one caption number without the other — so the caption landed in a band
    /// the generator had not left room for. Compare the numbers as TEXT, because the compiled form
    /// cannot show a Python constant.
    /// </summary>
    private static void TheAtlasGeneratorAgreesWithTheCode(Harness t, string repoRoot)
    {
        string path = Path.Combine(repoRoot, "unity", "board-prep", "buttons", "cap_atlas.py");
        if (!File.Exists(path))
        {
            t.True(false, $"cap_atlas.py not found at {path} — the face-budget lint cannot run");
            return;
        }
        string py = File.ReadAllText(path);

        float Num(string name)
        {
            var m = Regex.Match(py, @"^" + Regex.Escape(name) + @"\s*=\s*(-?[0-9.]+)",
                                RegexOptions.Multiline);
            t.True(m.Success, $"cap_atlas.py: could not read {name}");
            return m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : float.NaN;
        }

        var plateau = Regex.Match(py, @"^PLATEAU_LO,\s*PLATEAU_HI\s*=\s*(-?[0-9.]+),\s*(-?[0-9.]+)",
                                  RegexOptions.Multiline);
        t.True(plateau.Success, "cap_atlas.py: could not read PLATEAU_LO/PLATEAU_HI");
        if (plateau.Success)
        {
            float lo = float.Parse(plateau.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float hi = float.Parse(plateau.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            t.True(Math.Abs(lo - CapFaceLayout.FieldLo) < 1e-4f,
                   $"PLATEAU_LO {lo} != CapFaceLayout.FieldLo {CapFaceLayout.FieldLo} — the symbol is being " +
                   "carved against a different field than the caption is placed in");
            t.True(Math.Abs(hi - CapFaceLayout.FieldHi) < 1e-4f,
                   $"PLATEAU_HI {hi} != CapFaceLayout.FieldHi {CapFaceLayout.FieldHi}");
        }

        var lb = Regex.Match(py, @"^TEXT_LABEL_BOX\s*=\s*\((-?[0-9.]+),\s*(-?[0-9.]+)\)",
                             RegexOptions.Multiline);
        t.True(lb.Success, "cap_atlas.py: could not read TEXT_LABEL_BOX");
        if (lb.Success)
        {
            float bx = float.Parse(lb.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float by = float.Parse(lb.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            t.True(Math.Abs(bx - CapFaceLayout.CaptionBoxWithSymbol.x) < 1e-4f
                   && Math.Abs(by - CapFaceLayout.CaptionBoxWithSymbol.y) < 1e-4f,
                   $"TEXT_LABEL_BOX ({bx}, {by}) != CapFaceLayout.CaptionBoxWithSymbol " +
                   $"{CapFaceLayout.CaptionBoxWithSymbol} — this exact disagreement is the ModBuild 281 defect");
        }

        float dy = Num("TEXT_LABEL_CENTRE_DY");
        t.True(Math.Abs(dy - CapFaceLayout.CaptionCentreYWithSymbol) < 1e-4f,
               $"TEXT_LABEL_CENTRE_DY {dy} != CapFaceLayout.CaptionCentreYWithSymbol " +
               $"{CapFaceLayout.CaptionCentreYWithSymbol}");

        // The symbol must sit in the band ABOVE the caption, inside the field, with a real gap.
        float size = Num("TEXT_SIZE");
        float cy = Num("TEXT_CY");                 // from the TOP of the cell
        float symLo = 1f - (cy + size * 0.5f);     // in cap v
        float symHi = 1f - (cy - size * 0.5f);
        float capTop = 0.5f + CapFaceLayout.CaptionCentreYWithSymbol
                       + CapFaceLayout.CaptionBoxWithSymbol.y * 0.5f;
        t.True(symLo > capTop + 0.005f,
               $"the carved symbol's band starts at v {symLo:F4} but the caption band ends at {capTop:F4} — " +
               "the caption would be drawn through the symbol");
        t.True(symHi <= CapFaceLayout.FieldHi + 1e-4f,
               $"the carved symbol's band reaches v {symHi:F4}, past the field's {CapFaceLayout.FieldHi:F4} — " +
               "it would be cut in half by the inner chamfer");

        float solo = Num("SOLO_SIZE");
        t.True(solo <= CapFaceLayout.FieldHi - CapFaceLayout.FieldLo + 1e-4f,
               $"SOLO_SIZE {solo} does not fit the field {CapFaceLayout.FieldHi - CapFaceLayout.FieldLo:F4}");
    }

    /// <summary>
    /// A SOURCE LINT with one job: <c>TextOverflowModes.Truncate</c> must not exist anywhere in the
    /// shipped mod. It is the single assignment that produced "AUSWAHL BEEN", and it is one
    /// autocomplete away from coming back — silently, because a truncated label looks like a short
    /// label and every other gate in this repository agrees with it.
    ///
    /// <para><b>Ellipsis is deliberately NOT linted.</b> Three labels use it (the wrist HUD's peer
    /// name, the options rows, the map room's wrist name) and it is a different contract: it drops
    /// characters but PRINTS A MARK SAYING SO, so a player can see that a name was shortened and a
    /// screenshot of it is a report rather than a mystery. Truncate is the one that lies.</para>
    /// </summary>
    private static void NoTruncateModeSurvivesInTheMod(Harness t, string repoRoot)
    {
        string src = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(src))
        {
            t.True(false, $"src/ not found at {src} — the Truncate lint cannot run");
            return;
        }
        var offenders = new List<string>();
        foreach (string f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.TrimStart().StartsWith("//") || l.TrimStart().StartsWith("///"))
                    continue;
                if (l.Contains("TextOverflowModes.Truncate"))
                    offenders.Add($"{Path.GetFileName(f)}:{i + 1}");
            }
        }
        t.True(offenders.Count == 0,
               "a label somewhere can DISCARD its own text SILENTLY again: " + string.Join(", ", offenders) +
               " — the user's requirement is that the text is always fully readable, and Truncate " +
               "is the mode that drops the tail with nothing to show it did.");
    }
}
