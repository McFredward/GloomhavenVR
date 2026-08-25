using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE TWO CLAMPS THAT STAND BETWEEN THE RE-AUTHORED BOARDS AND THE USER'S OWN CONFIG — and the
/// reason they need a harness at all, which is not the usual one.
///
/// <para>Nothing here is a wire format. These are the arithmetic that decides WHERE a keycap, a
/// rest disc and the board mesh itself end up, and they are on this harness because the defect
/// they exist for is invisible to every other gate in the repository. The build compiles, the wire
/// tests pass, the mirrors agree, the bundle loads — and the Bronze board stands on edge with its
/// controls hanging in the air, because <c>AssetPitchDegrees_Bronze = 57</c> in his live cfg was
/// tuned against a mesh that no longer exists. That was measured, not imagined: replaying his two
/// dials against the re-authored plate left THREE of the five seat and rest anchors with no mesh
/// behind them at all and the other two 151.5 and 167.1 mm off the surface
/// (<c>Editor/PreviewBoard.ApplyAssetPose</c>, 2026-08-25).</para>
///
/// <para><b>THE INPUTS ARE THE REAL ONES, NOT ROUND NUMBERS.</b> Every extent below was read off
/// the built prefabs and every dial off <c>.planning/debug/default/dev.gloomhavenvr.cards.cfg</c>.
/// A test that invents its own inputs proves the function is self-consistent and nothing else —
/// this project has already paid for one of those (<c>tex_symbols.py --selftest</c> passed 9/9
/// while every real sheet came out shattered). The whole point is to pin the behaviour against the
/// numbers that are actually going to reach it on his machine.</para>
///
/// <para><b>AND THE UNMEASURED PATH IS ASSERTED AS HARD AS THE MEASURED ONE.</b> Both clamps are
/// gated on whether the board carries a measured recess, and the guarantee that makes them safe to
/// ship is that a board WITHOUT one is laid out bit-identically to the build before they existed —
/// the bundle currently installed on his machine, where his +0.462 and his 57 degrees are correct.
/// A regression that made the clamp fire on an old-bundle board would move Oak's caps 8 mm and
/// re-flatten nothing; it would look like a tuning drift and be blamed on anything else.</para>
/// </summary>
internal static class BoardSeatVectors
{
    // ---- the three boards as BUILT, mm converted to the half-extents the prefab carries --------
    // BuildBoard.cs measures each recess floor off the mesh and writes half-width/half-height into
    // SeatExtent{1,2,3} and RestExtent{Short,Long}. Verified against a real rebuild 2026-08-25:
    //   seat floors  Oak 74.6 x 64.3   Steel 81.0 x 70.1   Bronze 61.2 x 51.9 mm
    //   rest pads    Oak 81.6          Steel 81.7          Bronze 68.1 mm square
    private static readonly Vector2 OakSeat = new(0.0373f, 0.03215f);
    private static readonly Vector2 SteelSeat = new(0.0405f, 0.03505f);
    private static readonly Vector2 BronzeSeat = new(0.0306f, 0.02595f);
    private static readonly Vector2 OakPad = new(0.0408f, 0.0408f);
    private static readonly Vector2 BronzePad = new(0.03405f, 0.03405f);

    // [BoardButtons] Width/Height — the BOUND default (Defaults.BoardButtons_Width/Height), which
    // is what his cfg holds. NOT ButtonTuning.DefaultBoardWidth's 0.073: that is the pre-Bind
    // fallback and reaches no install. This distinction has cost this project two rounds on two
    // unrelated subsystems, so it is asserted here rather than assumed.
    private static readonly Vector2 TunedCap = new(0.063f, 0.065f);
    private const float Margin = 0.004f;   // [BoardButtons] Travel, clamped into 1..8 mm


    /// <summary>Float equality within a tolerance. <see cref="Harness"/> has no such assertion —
    /// every other vector on it compares BYTES, where exactness is the whole point. These are
    /// metres computed through trigonometry, so a tolerance is required rather than convenient,
    /// and each call below states one appropriate to what it is checking rather than sharing a
    /// single loose epsilon.</summary>
    private static void Near(Harness t, string what, float expected, float actual, float tol) =>
        t.True(Mathf.Abs(expected - actual) <= tol,
               $"{what}: expected {expected:F6}, got {actual:F6} (tolerance {tol:G})");

    internal static void Run(Harness t)
    {
        FitOnlyShrinks(t);
        SeatClampContainsTheMirrorCompensation(t);
        SeatClampIsInertWithoutAMeasurement(t);
        AssetPoseClampContainsTheBronzeDials(t);
        AssetPoseClampIsInertWithoutAMeasurement(t);
        AssetPoseClampLeavesSmallCorrectionsAlone(t);
    }

    // ------------------------------------------------------------------ the fit ----------------
    private static void FitOnlyShrinks(Harness t)
    {
        // The tuned pair is the CEILING. On a board roomier than the tuning the cap is unchanged;
        // there is no board like that among the three, which is itself worth pinning — every board
        // shrinks its caps and a rebuild that stopped shrinking would go unnoticed by eye.
        Vector2 oak = BoardAnchors.FitCapSize(TunedCap, OakSeat, Margin);
        Vector2 steel = BoardAnchors.FitCapSize(TunedCap, SteelSeat, Margin);
        Vector2 bronze = BoardAnchors.FitCapSize(TunedCap, BronzeSeat, Margin);

        Near(t, "fit/oak.w", 0.063f, oak.x, 1e-4f);            // 2*(37.3-4) = 66.6 > 63, so the ceiling holds
        Near(t, "fit/oak.h", 0.0563f, oak.y, 1e-4f);           // 2*(32.15-4) = 56.3 < 65, so the seat wins
        Near(t, "fit/steel.w", 0.063f, steel.x, 1e-4f);
        Near(t, "fit/steel.h", 0.0621f, steel.y, 1e-4f);
        Near(t, "fit/bronze.w", 0.0532f, bronze.x, 1e-4f);
        Near(t, "fit/bronze.h", 0.0439f, bronze.y, 1e-4f);

        // Never larger than tuned, on any axis, on any board.
        t.True(oak.x <= TunedCap.x + 1e-6f && oak.y <= TunedCap.y + 1e-6f, "fit/oak never grows");
        t.True(steel.x <= TunedCap.x + 1e-6f && steel.y <= TunedCap.y + 1e-6f, "fit/steel never grows");
        t.True(bronze.x <= TunedCap.x + 1e-6f && bronze.y <= TunedCap.y + 1e-6f, "fit/bronze never grows");

        // No measurement -> the tuned size, untouched. This is every board in the shipped bundle.
        Vector2 unmeasured = BoardAnchors.FitCapSize(TunedCap, null, Margin);
        Near(t, "fit/unmeasured.w", TunedCap.x, unmeasured.x, 1e-6f);
        Near(t, "fit/unmeasured.h", TunedCap.y, unmeasured.y, 1e-6f);

        // The pressability floor: a recess tighter than 20 x 15 mm yields an overhanging cap, not
        // one nobody can hit.
        Vector2 tiny = BoardAnchors.FitCapSize(TunedCap, new Vector2(0.006f, 0.006f), Margin);
        Near(t, "fit/floor.w", 0.020f, tiny.x, 1e-6f);
        Near(t, "fit/floor.h", 0.015f, tiny.y, 1e-6f);
    }

    // ------------------------------------------------- the cap may not leave its seat ----------
    private static void SeatClampContainsTheMirrorCompensation(Harness t)
    {
        // ConfirmUndoOffset_Steel.x = +0.462 — 46 cm on a 64 cm board, because the SHIPPED Steel
        // board had its zones mirrored and he dragged the whole cluster back across it. Applied raw
        // to a canonical board that is a third of a metre off the edge.
        Vector2 cap = BoardAnchors.FitCapSize(TunedCap, SteelSeat, Margin);
        Vector2 slack = BoardAnchors.SeatSlack(SteelSeat, cap);
        Near(t, "slack/steel.x", 0.0090f, slack.x, 1e-4f);
        Near(t, "slack/steel.y", 0.0040f, slack.y, 1e-4f);

        var wanted = new Vector3(0.462f, 0.011f, 0.003f);
        Vector3 got = BoardAnchors.ClampSeatPose(wanted, 0.005f, SteelSeat, cap);
        Near(t, "seat/steel clamped x", slack.x, got.x, 1e-5f);
        Near(t, "seat/steel clamped y", slack.y, got.y, 1e-5f);
        // Z IS NOT CLAMPED — it is the proud depth toward the player and has nothing to do with the
        // recess walls. A clamp that ate it would sink every cap into its own board.
        Near(t, "seat/steel z untouched", 0.003f, got.z, 1e-6f);
        t.True(BoardAnchors.SeatPoseWasClamped(
            wanted + new Vector3(0f, 0.005f, 0f), got), "seat/steel was clamped");

        // The rest discs carry the same mirroring in the other direction: RestButtonOffset_Bronze.x
        // = -0.445. Round discs, so the fit is square.
        Vector2 disc = BoardAnchors.FitCapSize(new Vector2(0.071f, 0.071f), BronzePad, Margin);
        Vector3 rest = BoardAnchors.ClampSeatPose(new Vector3(-0.445f, 0.033f, 0f), 0.013f,
                                                  BronzePad, disc);
        Vector2 padSlack = BoardAnchors.SeatSlack(BronzePad, disc);
        Near(t, "rest/bronze clamped x", -padSlack.x, rest.x, 1e-5f);
        Near(t, "rest/bronze clamped y", padSlack.y, rest.y, 1e-5f);

        // THE WHOLE POINT, stated as the property rather than as three numbers: whatever he dials,
        // the cap's own edge cannot pass the recess wall. Swept over a metre in both directions.
        Vector2 oakCap = BoardAnchors.FitCapSize(TunedCap, OakSeat, Margin);
        Vector2 oakSlack = BoardAnchors.SeatSlack(OakSeat, oakCap);
        for (int i = -100; i <= 100; i++)
        {
            var probe = new Vector3(i * 0.01f, i * -0.01f, 0f);
            Vector3 p = BoardAnchors.ClampSeatPose(probe, i * 0.002f, OakSeat, oakCap);
            t.True(Mathf.Abs(p.x) <= oakSlack.x + 1e-6f, $"seat/oak inside x @{i}");
            t.True(Mathf.Abs(p.y) <= oakSlack.y + 1e-6f, $"seat/oak inside y @{i}");
        }
    }

    private static void SeatClampIsInertWithoutAMeasurement(Harness t)
    {
        // THE ACCEPTANCE CONDITION FOR SHIPPING THIS AT ALL. A board with no measured recess is a
        // board from the bundle installed on his machine, where +0.462 is CORRECT. Bit-identical,
        // including the stack term, including Oak's genuine 8 mm nudges.
        var wanted = new Vector3(0.462f, 0.011f, 0.003f);
        Vector3 got = BoardAnchors.ClampSeatPose(wanted, 0.005f, null, TunedCap);
        Near(t, "seat/unmeasured x", 0.462f, got.x, 1e-6f);
        Near(t, "seat/unmeasured y", 0.016f, got.y, 1e-6f);   // 0.011 + the 0.005 stack term
        Near(t, "seat/unmeasured z", 0.003f, got.z, 1e-6f);
        t.True(!BoardAnchors.SeatPoseWasClamped(wanted + new Vector3(0f, 0.005f, 0f), got), "seat/unmeasured not clamped");

        var oakNudge = new Vector3(-0.008f, 0f, 0.009f);
        Vector3 oakGot = BoardAnchors.ClampSeatPose(oakNudge, 0f, null, TunedCap);
        Near(t, "seat/oak nudge survives x", -0.008f, oakGot.x, 1e-6f);
        Near(t, "seat/oak nudge survives z", 0.009f, oakGot.z, 1e-6f);
    }

    // ------------------------------------ the mesh may not leave the controls on it -------------
    private static void AssetPoseClampContainsTheBronzeDials(Harness t)
    {
        // Bronze's furthest pinned anchor is 0.2323 m from the board root (ButtonSeat1 at
        // (0.2215, 0.0701, 0.0004), |.| = 0.2323). Budget 5 mm, half to each term, so the tilt
        // bound is asin(0.0025 / 0.2323) = 0.6168 deg.
        const float r = 0.2323f;
        var offset = new Vector3(0f, -0.11f, 0.08f);       // AssetOffset_Bronze, live cfg
        var euler = new Vector3(57f, 0f, 0f);              // AssetPitchDegrees_Bronze, live cfg
        Vector3 reqO = offset, reqE = euler;
        BoardAnchors.ClampAssetPose(ref offset, ref euler, r);

        float half = BoardAnchors.MaxAnchorLift * 0.5f;
        // The offset is scaled as a VECTOR, so its DIRECTION survives and only its length is
        // refused: (0, -0.11, +0.08) has magnitude 0.136015, so it comes back as
        // (0, -0.0020218, +0.0014704). Clamping per axis instead would have returned
        // (0, -0.0025, +0.0025) — a different direction AND sqrt(2) too long.
        Near(t, "asset/bronze offset.x", 0f, offset.x, 1e-6f);
        Near(t, "asset/bronze offset.y", -0.0020218f, offset.y, 1e-6f);
        Near(t, "asset/bronze offset.z", 0.0014704f, offset.z, 1e-6f);
        Near(t, "asset/bronze offset length", half, offset.magnitude, 1e-6f);
        Near(t, "asset/bronze pitch", 0.6168f, euler.x, 2e-3f);
        t.True(BoardAnchors.AssetPoseWasClamped(reqO, reqE, offset, euler), "asset/bronze was clamped");

        // THE GUARANTEE ITSELF, checked as geometry rather than as the two numbers above: the
        // furthest anchor's lift is offset (which moves every anchor by its own magnitude) plus
        // r*sin(tilt), and it must not exceed the whole budget. This is the sentence the summary
        // of ClampAssetPose makes, and the reason the budget is SPLIT — with the whole budget on
        // each term this assertion fails at exactly 2x.
        float lift = offset.magnitude + r * Mathf.Sin(euler.magnitude * Mathf.Deg2Rad);
        t.True(lift <= BoardAnchors.MaxAnchorLift + 1e-4f, "asset/bronze lift within budget");

        // Swept: no dial he can type produces a bigger lift.
        for (int i = -180; i <= 180; i += 5)
        {
            var o = new Vector3(0.5f, -0.5f, 0.5f);
            var e = new Vector3(i, i, i);
            BoardAnchors.ClampAssetPose(ref o, ref e, r);
            // THE SWEEP THAT CAUGHT THE REAL DEFECT. All three axes at once is the worst case, and
            // with the original per-axis clamp this measured 8.66 mm against a 5 mm budget —
            // sqrt(3) too much offset and sqrt(3) too much tilt, because a per-axis clamp bounds a
            // CUBE while the guarantee is about a BALL. Left as a magnitude assertion for exactly
            // that reason: an axis-wise check would have passed the broken version.
            float l = o.magnitude + r * Mathf.Sin(Mathf.Min(90f, e.magnitude) * Mathf.Deg2Rad);
            t.True(l <= BoardAnchors.MaxAnchorLift + 1e-4f, $"asset/sweep lift @{i}");
        }
    }

    private static void AssetPoseClampIsInertWithoutAMeasurement(Harness t)
    {
        // The bundle on his machine right now: no measured recess, so no bound, so the raked
        // Bronze lectern still gets laid flat by the numbers that were tuned to lay it flat.
        var offset = new Vector3(0f, -0.11f, 0.08f);
        var euler = new Vector3(57f, 0f, 0f);
        BoardAnchors.ClampAssetPose(ref offset, ref euler, null);
        Near(t, "asset/unmeasured offset.y", -0.11f, offset.y, 1e-6f);
        Near(t, "asset/unmeasured offset.z", 0.08f, offset.z, 1e-6f);
        Near(t, "asset/unmeasured pitch", 57f, euler.x, 1e-6f);

        // A degenerate radius is the same "no bound is known" state, not a divide-by-zero.
        var o2 = new Vector3(0f, -0.11f, 0.08f);
        var e2 = new Vector3(57f, 0f, 0f);
        BoardAnchors.ClampAssetPose(ref o2, ref e2, 0f);
        Near(t, "asset/zero radius pitch", 57f, e2.x, 1e-6f);
    }

    private static void AssetPoseClampLeavesSmallCorrectionsAlone(Harness t)
    {
        // The dial is a control he asked for and the clamp must not confiscate it. Anything inside
        // the budget passes through untouched — that is the difference between bounding a stale
        // value and deleting a feature, and it is the reason this is a clamp and not a hard zero.
        // Inside the budget as a VECTOR, which is the tighter reading: the offset's magnitude
        // is 1.73 mm against a 2.5 mm ball, and the euler's is 0.539 deg against 0.617.
        // A first attempt used (1, -2, 1.5) mm — magnitude 2.69 mm — and was correctly
        // clamped. That is worth leaving in the record: the offset dial is bounded to a
        // 2.5 mm BALL, not to 2.5 mm per axis, and 2.5 mm is most of what an 8.6 mm-deep
        // seat has to give away.
        var offset = new Vector3(0.001f, -0.001f, 0.001f);
        var euler = new Vector3(0.4f, -0.3f, 0.2f);
        Vector3 reqO = offset, reqE = euler;
        BoardAnchors.ClampAssetPose(ref offset, ref euler, 0.2323f);
        Near(t, "asset/small offset.x", reqO.x, offset.x, 1e-6f);
        Near(t, "asset/small offset.y", reqO.y, offset.y, 1e-6f);
        Near(t, "asset/small offset.z", reqO.z, offset.z, 1e-6f);
        Near(t, "asset/small pitch", reqE.x, euler.x, 1e-6f);
        Near(t, "asset/small yaw", reqE.y, euler.y, 1e-6f);
        Near(t, "asset/small roll", reqE.z, euler.z, 1e-6f);
        t.True(!BoardAnchors.AssetPoseWasClamped(reqO, reqE, offset, euler), "asset/small not clamped");

        // A SMALLER BOARD GETS A WIDER TILT, because the bound is asin(lift / r) and r is measured.
        // If this ever comes out equal for two different radii the bound has become a constant.
        var eBig = new Vector3(45f, 0f, 0f);
        var oBig = Vector3.zero;
        BoardAnchors.ClampAssetPose(ref oBig, ref eBig, 0.05f);
        var eSmall = new Vector3(45f, 0f, 0f);
        var oSmall = Vector3.zero;
        BoardAnchors.ClampAssetPose(ref oSmall, ref eSmall, 0.2323f);
        t.True(eBig.x > eSmall.x * 3f, "asset/bound scales with the measured radius");
    }
}
