// THE PRINTED CARD FACE vs THE CARD BODY — the arithmetic behind user report 12 (2026-08-15).
//
// "Die remote Handkarten Vorderseiten werden etwas zu klein angezeigt, so dass sie nicht perfekt
// auf dem mesh liegen und der Hintergrund am rand durchscheint." (.planning/debug/remote_faecher.jpg)
//
// WHY THIS IS PINNED IN A PROJECT ABOUT WIRE FORMATS. It is the same shape of defect the stepper
// guard next door exists for: a number that can only be observed from inside a headset, by eye,
// and whose wrongness reads as "looks a bit off" rather than as a failure. The margin was 4.73 mm
// per side and survived every build since the ghost fan was written, because nobody could state it
// in millimetres — the screenshot could only say "there is a rim". So the millimetres are asserted
// here, and the two source facts that produce them are linted:
//
//   * a card face is letterboxed into the card with Mathf.Min and inset by CardFace.BorderFraction;
//   * therefore the card BODY must be scaled to that printed rect, not left at the nominal card.
//     That is what VRCard.SetCanvasSize has always done for the LOCAL card and what
//     Net/RemoteHandFan did NOT do for a peer's ghost slab.
//
// The numbers below are the ones the hardware log prints ("CARD FACE RECT", "Remote hand fan face
// rect"), so a future log can be checked against this file rather than against a memory.

using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class CardFaceRectVectors
{
    /// <summary>Nominal card, metres — [Cards] CardWidth's shipped default and the 63.5:88 poker
    /// ratio the height is derived from (Defaults.Cards.cs / RemoteHandFan.DefaultCardHeight).</summary>
    private const double CardWidth = 0.0635d;
    private const double CardHeight = CardWidth * (88d / 63.5d);

    /// <summary>CardFace.BorderFraction — the inset the fitted face is drawn at.</summary>
    private const double BorderFraction = 0.06d;

    internal static void Run(Harness t, string repoRoot)
    {
        Arithmetic(t);
        SourceLint(t, repoRoot);
    }

    // =============================================================================================
    //  1. The measured rectangles
    // =============================================================================================

    /// <summary>The one formula, replicated exactly as CardFace.VisibleFaceRect states it:
    /// <c>facePixels × Min(w/px.x, h/px.y) × (1 − BorderFraction)</c>.</summary>
    private static (double W, double H) Printed(double pxX, double pxY)
    {
        double fit = System.Math.Min(CardWidth / pxX, CardHeight / pxY);
        double k = 1d - BorderFraction;
        return (pxX * fit * k, pxY * fit * k);
    }

    private static double Mm(double metres) => System.Math.Round(metres * 1000d, 2);

    private static void Arithmetic(Harness t)
    {
        // ---- the REAL ability face, 294 x 450 px --------------------------------------------
        // Host log, verbatim: "CARD SILHOUETTE (Ability): first face offered ('Full', rect
        // 294x450 px)". Aspect 0.6533 against the card's 0.7216 — so the fit is HEIGHT-limited and
        // the leftover margin lands mostly on the WIDTH, which is exactly the shape the screenshot
        // shows (a wider rim at the sides than at the ends).
        t.Case("cardface/printed-rect-294x450");
        (double w, double h) = Printed(294d, 450d);
        t.Equal(54.04d, Mm(w), "printed face width (mm) for a 294x450 px face in a 63.5x88 mm card");
        t.Equal(82.72d, Mm(h), "printed face height (mm) for a 294x450 px face in a 63.5x88 mm card");

        // ---- THE DEFECT, in millimetres ------------------------------------------------------
        // What a body left at the NOMINAL card size shows around that print, per side. These are
        // the two numbers user report 12 was written about; the fix drives both to 0.00.
        t.Case("cardface/nominal-body-margin");
        t.Equal(4.73d, Mm((CardWidth - w) / 2d), "card-back margin per side at the SIDES (mm), body left at 63.5 mm");
        t.Equal(2.64d, Mm((CardHeight - h) / 2d), "card-back margin per side at the ENDS (mm), body left at 88.0 mm");

        // ---- and the scale factors the corrected body wears ----------------------------------
        // RemoteHandFan.Rebuild writes exactly these onto the slab's Body child, and
        // VRCard.SetCanvasSize writes the same pair onto the local backing. A body scaled by them
        // IS the printed rect, so the margin is zero by construction — that is the whole fix.
        t.Case("cardface/body-scale-factors");
        t.Equal(0.8511d, System.Math.Round(w / CardWidth, 4), "body X scale = printed width / nominal width");
        t.Equal(0.94d, System.Math.Round(h / CardHeight, 4), "body Y scale = printed height / nominal height");
        // The Y factor is 1 − BorderFraction exactly, and that is not a coincidence: the fit is
        // height-limited, so the height loses the inset and nothing else. If this ever stops being
        // true the face aspect has crossed the card's and the X/Y roles have swapped.
        t.Equal(0.94d, System.Math.Round(1d - BorderFraction, 4), "…which is 1 - BorderFraction while the fit is height-limited");

        // ---- the PLACEHOLDER face, 270 x 400 px ----------------------------------------------
        // What a card is built at before this client has hosted a real ability face
        // (CardFace.DefaultFacePixels). Height-limited too, so only the WIDTH differs — 1.8 mm,
        // which is the step a card used to take when its face was adopted.
        t.Case("cardface/printed-rect-270x400-placeholder");
        (double pw, double ph) = Printed(270d, 400d);
        t.Equal(55.84d, Mm(pw), "printed face width (mm) for the 270x400 px placeholder");
        t.Equal(82.72d, Mm(ph), "printed face height (mm) for the 270x400 px placeholder");
        t.Equal(1.80d, System.Math.Round(Mm(pw) - Mm(w), 2), "width step when the real 294x450 face is adopted (mm)");
    }

    // =============================================================================================
    //  2. Source lint — the two facts the arithmetic above is only true BECAUSE of
    // =============================================================================================

    private static void SourceLint(Harness t, string repoRoot)
    {
        string cardFace = Read(repoRoot, "src/GloomhavenVR/Cards/Art/CardFace.cs");
        string vrCard = Read(repoRoot, "src/GloomhavenVR/Cards/VRCard.cs");
        string remoteFan = Read(repoRoot, "src/GloomhavenVR/Net/Remote/RemoteHandFan.cs");

        // The inset this file's millimetres are computed from.
        t.Case("cardface/border-fraction-pinned");
        Match m = Regex.Match(cardFace, @"BorderFraction\s*=\s*([0-9.]+)f");
        t.True(m.Success, "CardFace declares BorderFraction");
        t.Equal("0.06", m.Groups[1].Value, "CardFace.BorderFraction");

        // ONE definition of the printed rect. It used to be two — an inline Mathf.Min in
        // VRCard.SetCanvasSize and a hand-copied 0.94 next to it, with a comment asking the reader
        // to keep them in sync by hand. That instruction was followed in VRCard and missed in
        // Net/RemoteHandFan, which is report 12.
        t.Case("cardface/single-visible-rect-definition");
        t.True(cardFace.Contains("internal static Vector2 VisibleFaceRect("),
               "CardFace exposes VisibleFaceRect — the one definition of the printed rectangle");
        t.True(cardFace.Contains("internal static float VisibleFaceFraction"),
               "CardFace exposes VisibleFaceFraction");
        t.True(vrCard.Contains("CardFace.VisibleFaceFraction"),
               "VRCard reads CardFace.VisibleFaceFraction instead of a hand-copied 0.94");
        t.True(!Regex.IsMatch(vrCard, @"VisibleFaceFraction\s*=\s*0\.94f"),
               "VRCard no longer declares its own 0.94 constant");

        // The ghost slab's BODY is scaled to the printed rect. Without this the arithmetic above
        // is a description of a bug rather than of the shipped geometry.
        t.Case("cardface/remote-slab-body-fits-the-print");
        t.True(remoteFan.Contains("CardFace.VisibleFaceRect(DefaultCardWidth, DefaultCardHeight)"),
               "RemoteHandFan resolves the printed rect from the shared definition");
        t.True(remoteFan.Contains("vis.x / DefaultCardWidth") && remoteFan.Contains("vis.y / DefaultCardHeight"),
               "RemoteHandFan scales the slab Body to the printed rect on both axes");

        // The cloned face is fitted against the NOMINAL card, not the owner's tuned width: the slab
        // root already carries that ratio, so passing it again squared it.
        t.Case("cardface/remote-clone-fitted-once");
        t.True(remoteFan.Contains("new RemoteCardArt(card.transform, DefaultCardWidth, DefaultCardHeight)"),
               "RemoteHandFan fits the cloned face against the nominal card size exactly once");
        t.True(!remoteFan.Contains("new RemoteCardArt(card.transform, _cardWidth, _cardHeight)"),
               "…and no longer double-applies the owner's tuned CardWidth");
    }

    private static string Read(string repoRoot, string relative)
    {
        string path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
