// PEER-BOARD SEE-THROUGH — a stepper pin and a SOURCE LINT, no packets.
//
// This feature (user request 15, 2026-08: a Mitspieler's control board fades or vanishes while it
// hides the play field) puts nothing on the wire, so there are no bytes to pin. Two properties of
// it are nonetheless undiscoverable where they live, and both have precedent in this repo for
// having cost shipped builds:
//
//   1. ITS DIALS ARE FELT, NOT READ. "Wie transparent die betroffenen boards werden sollen, soll
//      einstellbar sein" — so OccludedAlpha is a dial the user will sit in the headset and press.
//      A step a hundred times too small does not fail, does not log and does not look wrong; it
//      produces the report "hat keinen Einfluss" after twenty presses. Three separate rounds of
//      exactly that reached the user before ConfigStepVectors.cs existed. These four keys do not
//      ship their defaults through src/GloomhavenVR/Defaults/, so the sweep at the bottom of that
//      file does not see them — they are pinned by name here instead.
//
//   2. ITS FADE MUST STAY PER-EYE IDENTICAL. .planning/wall-fade-stereo-rivalry.md is a whole
//      defect PARKED by user ruling ("das darf niemals passieren. Entweder faded es auf beiden
//      Augen oder gar nicht") whose root cause is a discard scalar containing a screen-radial term
//      measured from each eye's own screen centre. PeerBoardFade is free of that by construction —
//      every decision is a CPU scalar from the mono head camera, delivered as a uniform alpha or a
//      whole-renderer cull — but "by construction" is a property of the SOURCE, not of the
//      compiled form, and the compiled form is all refactor-guard.sh can see. A future edit that
//      reaches for a per-eye or screen-space term would reintroduce a defect the user has already
//      ruled on twice. So the source is read as text, exactly like BundledShaderVectors reads it.

using System;
using System.IO;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.WireTests;

internal static class PeerBoardFadeVectors
{
    private const string Source = "src/GloomhavenVR/Net/Board/PeerBoardFade.cs";

    internal static void Run(Harness t, string repoRoot)
    {
        Steps(t);
        StereoLint(t, repoRoot);
    }

    // =============================================================================================
    //  1. The dials must be steppable from inside the headset
    // =============================================================================================

    private static void Steps(Harness t)
    {
        t.Case("peerboardfade/steps");

        // "Alpha" — the residual opacity of an occluding board. 0.05 a press means the whole 0..1
        // range is twenty presses, which is the granularity every other 0..1 dial in the mod has.
        Unit(t, "OccludedAlpha", 0.05d, ConfigSteps.UnitScope.Value);
        // "Fraction" — the two Schmitt bars, same unit word the wall's own bars ride.
        Unit(t, "OnFraction", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "OffFraction", 0.05d, ConfigSteps.UnitScope.Value);
        // "Seconds" — the two come-back dwells.
        Unit(t, "ExitDwellMovedSeconds", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "ExitDwellStationarySeconds", 0.05d, ConfigSteps.UnitScope.Value);
    }

    private static void Unit(Harness t, string key, double step, ConfigSteps.UnitScope scope)
    {
        bool got = ConfigSteps.TryUnit(key, out double s, out ConfigSteps.UnitScope sc);
        t.True(got, $"'{key}' must resolve through a unit word, not the magnitude fallback");
        if (!got)
            return;
        t.Equal(step, s, $"'{key}' step");
        t.Equal(scope.ToString(), sc.ToString(), $"'{key}' scope");
    }

    // =============================================================================================
    //  2. Source lint — no per-eye / screen-space term may enter the fade
    // =============================================================================================

    private static void StereoLint(Harness t, string repoRoot)
    {
        t.Case("peerboardfade/stereo");
        string path = Path.Combine(repoRoot, Source.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            t.True(false, $"{Source} not found — the peer-board see-through driver must exist "
                          + "for its per-eye guarantee to mean anything");
            return;
        }
        string src = File.ReadAllText(path);

        // The API surface that makes a term per-EYE under MultiPass. Each of these is how the
        // parked wall defect gets in: a value that differs between the two stereo passes, or a
        // screen-space coordinate that is measured from each eye's own centre.
        string[] forbidden =
        {
            "stereoActiveEye",
            "StereoTargetEyeMask",
            "GetStereoViewMatrix",
            "GetStereoProjectionMatrix",
            "stereoSeparation",
            "_ScreenParams",
            "screenPos",
            "ComputeScreenPos",
            "_CameraDepthTexture",
        };
        foreach (string token in forbidden)
        {
            t.True(src.IndexOf(token, StringComparison.Ordinal) < 0,
                $"{Source} must not reference '{token}': a per-eye or screen-space term in the "
                + "fade chain is exactly the parked wall-fade rivalry "
                + "(.planning/wall-fade-stereo-rivalry.md), which the user has ruled may never "
                + "happen — 'Entweder faded es auf beiden Augen oder gar nicht'");
        }

        // …and the positive half: the delivery really is the per-eye-identical pair (a uniform
        // alpha through a property block / CanvasGroup, and a whole-renderer cull). If either
        // disappears, the class has been re-plumbed and the guarantee above needs re-arguing.
        t.True(src.IndexOf("SetPropertyBlock", StringComparison.Ordinal) >= 0,
            $"{Source} must still deliver a uniform per-renderer alpha through a property block");
        t.True(src.IndexOf("forceRenderingOff", StringComparison.Ordinal) >= 0,
            $"{Source} must still deliver its fully-faded state as a whole-renderer cull");
        t.True(src.IndexOf("CanvasGroup", StringComparison.Ordinal) >= 0,
            $"{Source} must still fade the board's UI graphics through one CanvasGroup");
    }
}
