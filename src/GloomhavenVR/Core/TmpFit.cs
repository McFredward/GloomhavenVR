using System.Collections.Generic;
using GloomhavenVR.Cards;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// World-space TextMeshPro sizing helper (P8, hardware test #12): every mod-created
/// 3D TMP label is constrained to the plate/button/badge it sits on.
///
/// Background: 3D TMP world metrics are fontSize / 10 ≈ line height in local meters.
/// Several board labels were authored with font sizes LARGER than their container
/// (e.g. the initiative badge: fontSize 1.2 = 0.12 m line on a 0.042 m disc), and
/// localized strings (CONFIRM/UNDO/REST mirror the game's own localization) vary
/// wildly in length — with auto-sizing off, TMP happily renders past the rect, so
/// text overflowed and clipped across neighboring elements (test #12).
///
/// <para><b>THE POLICY CHANGED ON 2026-08-25, BECAUSE THE USER OVERRULED IT.</b> This class used to
/// end its own summary with <i>"past the readability floor the text truncates — text is always
/// fully inside its plate"</i>, and it implemented that with
/// <c>overflowMode = TextOverflowModes.Truncate</c> plus a <c>fontSizeMin</c> of 0.08. On hardware
/// (ModBuild 281) a board keycap therefore read <c>"AUSWAHL BEEN"</c> where the game had said
/// <c>"Auswahl beenden"</c>, and his report is unambiguous: <i>"WICHTIG: Der Text muss immer voll
/// lesbar sein. […] Das darf nicht passieren."</i></para>
///
/// <para><b>WHAT REPLACES IT — three changes, all here:</b>
/// <list type="number">
/// <item><b>NOTHING TRUNCATES ANY MORE.</b> <c>overflowMode</c> is <c>Overflow</c> on every label
/// this helper touches. Truncate discards glyphs with no flag any caller reads; Overflow draws them.
/// A caption that spills a millimetre past its plate is a cosmetic complaint that can be seen and
/// reported. A caption cut mid-word is a caption that means something else.</item>
/// <item><b>THE AUTO-SIZE FLOOR DROPPED FROM 0.08 TO <see cref="CapFaceLayout.MinFontSize"/> =
/// 0.045.</b> 0.08 was not a readability floor, it was the point at which the mod stopped shrinking
/// and started CUTTING. With 0.045 the shrink runway is nearly twice as long, so the overflow path
/// is reached far less often than truncation used to be.</item>
/// <item><b>THE BAND CAN NO LONGER INVERT.</b> <c>fontSizeMin</c> is clamped to
/// <c>fontSizeMax</c>. Callers legitimately ask for a max BELOW the floor (see
/// <c>Net/RemoteStatusReadouts.cs:110</c>, which documents having been bitten by exactly that),
/// and TMP's behaviour with min &gt; max is not defined by anything either of us has read.</item>
/// </list></para>
///
/// <para>The KEYCAP CAPTIONS do not use auto-sizing at all any more — see
/// <see cref="FitCapLabel"/>, which owns its own line breaks so the layout is a pure function that
/// the wire suite can drive without a headset.</para>
/// </summary>
internal static class TmpFit
{
    /// <summary>
    /// Auto-size floor (font-size units, ≈4.5 mm line). BELOW THIS THE TEXT OVERFLOWS — it is never
    /// cut. One definition, shared with the keycap caption solver so the two cannot drift.
    /// </summary>
    internal const float MinFontSize = CapFaceLayout.MinFontSize;

    /// <summary>Font-size cap per meter of container height (≈65 % line fill). One definition,
    /// shared with the keycap caption solver so a short caption is the same size under either
    /// path.</summary>
    private const float CapPerMeterHeight = CapFaceLayout.CapPerMeterHeight;

    /// <summary>
    /// Constrain a world-space TMP label to a width × height box (local meters).
    /// <paramref name="maxFontSize"/> is the preferred size for short strings; it is
    /// always clamped to the height-derived cap. <paramref name="wrap"/> = false for
    /// single-line labels (numbers, badges) — auto-size then fits the line to the
    /// box width instead of wrapping.
    /// </summary>
    internal static void Fit(TMP_Text tmp, float widthMeters, float heightMeters,
        float maxFontSize = 0f, bool wrap = true)
    {
        tmp.rectTransform.sizeDelta = new Vector2(widthMeters, heightMeters);
        float cap = heightMeters * CapPerMeterHeight;
        float max = maxFontSize > 0f ? Mathf.Min(maxFontSize, cap) : cap;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = max;
        // The floor never exceeds the ceiling — see the class header, item 3.
        tmp.fontSizeMin = Mathf.Min(MinFontSize, max);
        tmp.enableWordWrapping = wrap;
        // NEVER Truncate. See the class header: this single assignment is the user's report.
        tmp.overflowMode = TextOverflowModes.Overflow;
    }

    /// <summary>
    /// SIZE A BACKING PLATE FROM WHAT THE LABEL ACTUALLY DREW.
    ///
    /// <para><b>USER REPORT, verbatim, MP item 12:</b> <i>"Beim Test hatte die Anweisung so einen
    /// abgeschnitten Text oben (siehe abgeschnittener-text.jpg)."</i> That screenshot's own defect
    /// turned out to be a WIRE cap and not a layout one (see <c>Net.RemotePickBanner</c>), but the
    /// placard's plate and its text box really are two hard-coded sizes with nothing measuring the
    /// string, and lifting the wire cap is exactly what lets a longer line reach this box.
    /// <see cref="Fit"/> deliberately never truncates (see the class header), so a string needing
    /// more lines than the box holds is DRAWN past the bottom of its own parchment, onto whatever
    /// is behind it. That is not a clipping bug, it is a plate that was never told how big its
    /// text is.</para>
    ///
    /// <para><b>MEASURED, NOT MODELLED.</b> The extent comes from <c>GetRenderedValues</c> after a
    /// forced mesh update — the same readback <see cref="VerifyAndReport"/> uses, in the same
    /// order — so it is the real font, the real auto-sized size and the real line breaks. A
    /// modelled height would be a second opinion about the same thing, and this project has a
    /// ledger of those.</para>
    ///
    /// <para><b>GROW-ONLY, so a short caption is untouched.</b> The authored size stays the
    /// MINIMUM: the common case (one short line) renders byte-identically to before, and only a
    /// string that genuinely does not fit moves anything.</para>
    ///
    /// <para><b>FAILS TO THE AUTHORED SIZE — AND SAYS SO.</b> A label with no font asset yet
    /// returns zeros from the readback; a dead probe must never produce a layout, so a
    /// non-positive or NaN measurement returns the authored size unchanged rather than collapsing
    /// the plate. The authored size is ALSO what a short string legitimately produces, so the two
    /// are indistinguishable from the number alone — which is why <paramref name="measurement"/>
    /// names WHICH case fired, and why every caller prints it.</para>
    /// </summary>
    /// <param name="tmp">The label whose drawn extent decides the plate.</param>
    /// <param name="authoredPlate">The tuned plate size, local metres — kept as the MINIMUM.</param>
    /// <param name="paddingMeters">Margin per side between the drawn text and the plate edge.</param>
    /// <param name="measurement">What the readback did, for the caller's log line: the drawn size,
    /// or the reason there is none. Never null.</param>
    internal static Vector2 PlateSizeFor(TMP_Text? tmp, Vector2 authoredPlate, float paddingMeters,
                                         out string measurement)
    {
        if (tmp == null)
        {
            measurement = "NOT MEASURED — there is no label";
            return authoredPlate;
        }

        Vector2 rendered;
        try
        {
            // THE ORDER IS THE WHOLE POINT: force the mesh, THEN read it back. A size read before
            // the update is a flawless measurement of the PREVIOUS string.
            tmp.ForceMeshUpdate();
            rendered = tmp.GetRenderedValues(false);
        }
        catch (System.Exception e)
        {
            measurement = $"NOT MEASURED — the readback threw {e.GetType().Name}: {e.Message}";
            return authoredPlate;
        }

        if (float.IsNaN(rendered.x) || float.IsNaN(rendered.y)
            || !(rendered.x > 0f) || !(rendered.y > 0f))
        {
            measurement = $"NOT MEASURED — the readback returned {rendered.x:F4} x {rendered.y:F4} m "
                          + "(no font asset yet, or an empty string)";
            return authoredPlate;
        }

        measurement = $"TMP drew {rendered.x:F3} x {rendered.y:F3} m";
        return new Vector2(
            Mathf.Max(authoredPlate.x, rendered.x + 2f * paddingMeters),
            Mathf.Max(authoredPlate.y, rendered.y + 2f * paddingMeters));
    }

    // =====================================================================================
    // THE KEYCAP CAPTION — the one label family that gets a solved layout instead of auto-size.
    // =====================================================================================

    /// <summary>
    /// Strings already reported as overflowing or hard-broken, so a caption that is set every tick
    /// says so ONCE. Bounded: a caption is live game text and the set of distinct ones is small, but
    /// three of the keys that reach this cap interpolate a card title or a class name
    /// (<c>CapFaceLayout</c>'s header lists them), so the set is not closed and must not be allowed
    /// to grow without limit.
    /// </summary>
    private static readonly HashSet<string> _reported = new();

    /// <summary>Cap on <see cref="_reported"/>. Past it the set is cleared rather than grown — a
    /// repeated line is a much smaller problem than a leak on a per-tick path.</summary>
    private const int ReportCap = 64;

    /// <summary>
    /// Lay a KEYCAP CAPTION into its box: solved font size, solved line breaks, nothing discarded,
    /// and a loud line in the log when it genuinely does not fit.
    ///
    /// <para><b>THE METRICS ARE MEASURED FROM THIS VERY LABEL, not modelled.</b> The solver
    /// (<see cref="CapFaceLayout.FitCaption"/>) is a pure function of two numbers — the width of a
    /// line at font size 1.0 and the baseline-to-baseline step at font size 1.0 — and both are read
    /// off <paramref name="tmp"/> with its real font, at the moment of the fit, via
    /// <c>GetPreferredValues</c>. That is what lets the wire suite drive the same solver from a
    /// stated synthetic metric model while the game drives it from the harvested game font.</para>
    ///
    /// <para><b>AND THE OUTCOME IS MEASURED, NOT ASSUMED.</b> After the layout is applied the mesh
    /// is forced and the RENDERED extent is read back. Two different things can be wrong — the
    /// caption may not fit (the user-visible defect) or the solver's metrics may disagree with what
    /// TMP actually drew (the instrument being wrong) — and they are logged as two different lines,
    /// because a fix aimed at the wrong one of those is a wasted round.</para>
    /// </summary>
    /// <param name="context">Which cap this is, for the log line. Never null.</param>
    internal static void FitCapLabel(TMP_Text tmp, float widthMeters, float heightMeters,
                                     float maxFontSize, string context)
    {
        if (tmp == null)
            return;
        string src = tmp.text ?? string.Empty;
        tmp.rectTransform.sizeDelta = new Vector2(widthMeters, heightMeters);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.enableWordWrapping = false;   // the solver owns the breaks; TMP must not add its own
        tmp.overflowMode = TextOverflowModes.Overflow;

        if (string.IsNullOrWhiteSpace(src))
        {
            tmp.fontSize = Mathf.Max(MinFontSize, Mathf.Min(maxFontSize, CapFaceLayout.CrampedFontSize));
            return;
        }

        // ---- measure at font size 1.0, with this label's own font ------------------------------
        float saved = tmp.fontSize;
        tmp.fontSize = 1f;
        float lineStep;
        try
        {
            float one = tmp.GetPreferredValues("Ag", 0f, 0f).y;
            float two = tmp.GetPreferredValues("Ag\nAg", 0f, 0f).y;
            lineStep = two - one;
            if (!(lineStep > 0f))
                lineStep = 0.1f;      // TMP's own fontSize/10 rule, used only if the probe fails

            // A DEAD PROBE MUST NOT SILENTLY PRODUCE A LAYOUT. If this label has no font asset yet
            // (the harvested game font arrives asynchronously) GetPreferredValues returns zero for
            // everything, and a solver fed zeros would answer "it fits at the maximum size" for any
            // string — the confident wrong answer this project's ledger keeps recording. Fall back
            // to the auto-size helper, which at least still cannot truncate.
            if (!(tmp.GetPreferredValues(src, 0f, 0f).x > 0f))
            {
                tmp.fontSize = saved > 0f ? saved : Mathf.Max(MinFontSize, maxFontSize);
                Fit(tmp, widthMeters, heightMeters, maxFontSize);
                return;
            }

            var caption = CapFaceLayout.FitCaption(
                src, widthMeters, heightMeters, maxFontSize,
                s => tmp.GetPreferredValues(s, 0f, 0f).x, lineStep);

            tmp.fontSize = caption.FontSize;
            if (tmp.text != caption.Text)
                tmp.text = caption.Text;
            VerifyAndReport(tmp, context, src, caption, widthMeters, heightMeters);
        }
        catch (System.Exception e)
        {
            // A measurement path that throws must not take the caption with it: restore the size
            // the caller asked for and leave the untouched string on the cap.
            tmp.fontSize = saved > 0f ? saved : Mathf.Max(MinFontSize, maxFontSize);
            tmp.text = src;
            VRLog.Warn("Cards", $"CAP CAPTION FIT THREW on '{context}' for \"{src}\" — the caption is " +
                $"drawn unwrapped at font {tmp.fontSize:F3}. {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Read back what was actually drawn and say so when it is wrong. See <see cref="FitCapLabel"/>
    /// for why the two failure kinds are separate lines.
    /// </summary>
    private static void VerifyAndReport(TMP_Text tmp, string context, string src,
                                        CapFaceLayout.Caption caption, float boxW, float boxH)
    {
        Vector2 rendered;
        try
        {
            tmp.ForceMeshUpdate();
            rendered = tmp.GetRenderedValues(false);
        }
        catch
        {
            rendered = new Vector2(caption.BlockWidth, caption.BlockHeight);
        }

        bool spills = rendered.x > boxW * 1.02f || rendered.y > boxH * 1.02f;
        if (!caption.Overflows && !caption.HardBroke && !spills)
            return;

        string key = context + "" + src;
        if (_reported.Contains(key))
            return;
        if (_reported.Count >= ReportCap)
            _reported.Clear();
        _reported.Add(key);

        // THE LINE THAT REPLACES A SILENT CUT. It names the string, the box and the size the
        // caption needed, so the next report identifies the caption instead of a screenshot doing it.
        if (caption.Overflows || spills)
            VRLog.Warn("Cards", $"CAP CAPTION DOES NOT FIT on '{context}': \"{src}\" " +
                $"({src.Length} chars) was laid out as {caption.Lines} line(s) at font " +
                $"{caption.FontSize:F3} (the floor is {CapFaceLayout.MinFontSize:F3}) into a " +
                $"{boxW * 1000f:F1} × {boxH * 1000f:F1} mm box, and needs " +
                $"{caption.BlockWidth * 1000f:F1} × {caption.BlockHeight * 1000f:F1} mm " +
                $"(TMP drew {rendered.x * 1000f:F1} × {rendered.y * 1000f:F1} mm). " +
                "IT IS STILL DRAWN IN FULL — nothing was cut — so it overhangs its plate by " +
                $"{Mathf.Max(0f, rendered.x - boxW) * 1000f:F1} mm across and " +
                $"{Mathf.Max(0f, rendered.y - boxH) * 1000f:F1} mm down. " +
                "Either the cap needs more face or this caption needs a shorter wording.");
        else if (caption.HardBroke)
            VRLog.Info("Cards", $"CAP CAPTION HARD-BROKEN on '{context}': \"{src}\" contains a token " +
                $"wider than the {boxW * 1000f:F1} mm caption box at every size down to the " +
                $"{CapFaceLayout.MinFontSize:F3} floor, so it is split MID-WORD across " +
                $"{caption.Lines} lines at font {caption.FontSize:F3}. Every character is drawn — " +
                "this is a break, not a cut — but it is the same SHAPE as the ModBuild 281 defect " +
                "and is logged so the two are never confused in a screenshot.");

        // The instrument accusing itself: a solver whose predicted block disagrees with what TMP
        // drew is a solver whose metric model is wrong, and every number in the line above is then
        // void. Reported separately from the fit, because the two have different fixes.
        float predW = caption.BlockWidth, predH = caption.BlockHeight;
        if (predW > 1e-5f && predH > 1e-5f
            && (Mathf.Abs(rendered.x - predW) > predW * 0.15f
                || Mathf.Abs(rendered.y - predH) > predH * 0.25f))
            VRLog.Warn("Cards", $"CAP CAPTION METRIC MODEL DISAGREES WITH TMP on '{context}': solved " +
                $"{predW * 1000f:F1} × {predH * 1000f:F1} mm, TMP drew " +
                $"{rendered.x * 1000f:F1} × {rendered.y * 1000f:F1} mm for \"{caption.Text}\" at font " +
                $"{caption.FontSize:F3}. The fit line above is derived from the solved numbers, so " +
                "treat it as suspect until this one stops appearing — the width probe " +
                "(GetPreferredValues at fontSize 1) is the term to check first.");
    }
}
