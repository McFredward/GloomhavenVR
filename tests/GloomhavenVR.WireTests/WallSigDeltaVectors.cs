using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE DECODER THAT NAMES THE RENDERER BEHIND A "RANDOM" 155 ms HITCH — driven here before it
/// is believed, and driven against the ACTUAL ModBuild 438 hardware numbers.
///
/// <para><b>WHY THIS ONE NEEDS VECTORS MORE THAN MOST.</b> Its whole claim rests on a
/// multiplicative inverse modulo 2^64. That is exactly the kind of arithmetic that compiles,
/// runs, produces confident-looking integers and is wrong — and this project's ledger has an
/// entry for what happens next ("An instrument shipped and lying", "Verify the instrument
/// first"). So the inverse is ASSERTED against the prime rather than trusted, the round trip is
/// driven over the whole 256-value bit space and over a NEGATIVE instance id (Unity hands those
/// out and a sign-extension slip would only ever show up on those), and the NULL input is
/// driven first: with nothing moved the classifier must say NOTHING and must not invent an
/// event.</para>
///
/// <para><b>THE HARDWARE CONTROLS.</b> Four of the assertions below are not synthetic. They are
/// signature pairs lifted verbatim from the user's ModBuild 438 <c>Player.log</c>, and they pin
/// the three decodes the fix was built on: one renderer ENTERING with bits 141, the SAME
/// renderer LEAVING with bits 13, and one delta that is exactly <c>-128 x FnvPrime</c>. If the
/// fold ever changes shape under this decoder, these fail with the real numbers in hand.</para>
/// </summary>
internal static class WallSigDeltaVectors
{
    /// <summary>The renderer the ModBuild 438 log's decodable refusals all name. Its bits say
    /// mesh + mountable + MOD-OWNED, with and without activeInHierarchy.</summary>
    private const int LogInstanceId = -14798;
    private const int LogBitsActive = 141;   // mesh|mountable|MOD|active
    private const int LogBitsHidden = 13;    // mesh|mountable|MOD

    // Straight out of the log, "banked <sum>/<xor>, live <sum>/<xor>".
    private const ulong EnterBankedSum = 0xE2B120D69D24E538UL - 0x1D4F5F2962DBF448UL;

    /// <summary>The harness's generic Equal constrains T : IEquatable&lt;T&gt;, which an enum
    /// does not satisfy. Compared by name rather than by ordinal so a FAILURE reads
    /// "expected Entered, actual Compound" instead of two integers.</summary>
    private static void Kind(Harness t, WallSigDelta.DeltaKind expected,
                             WallSigDelta.DeltaKind actual, string what) =>
        t.Equal(expected.ToString(), actual.ToString(), what);

    internal static void Run(Harness t)
    {
        // ---- CONTROL 0: THE INVERSE IS THE INVERSE ---------------------------------------
        t.Case("sigdelta/inverse");
        unchecked
        {
            t.Equal(1UL, WallSigDelta.FnvPrime * WallSigDelta.FnvPrimeInverse,
                    "FnvPrime * FnvPrimeInverse must be 1 mod 2^64 — the whole decoder rests "
                    + "on this and nothing else pins it");
        }

        // ---- CONTROL 1: THE NULL INPUT ---------------------------------------------------
        // Nothing moved. The classifier must say so; inventing an event here would be the
        // instrument reporting a cause for a cycle that had none.
        t.Case("sigdelta/null-input");
        WallSigDelta.Delta none = WallSigDelta.Classify(0x1234UL, 0x5678UL, 0x1234UL, 0x5678UL);
        Kind(t, WallSigDelta.DeltaKind.Nothing, none.Kind,
                "an unmoved signature must decode to Nothing");

        // ---- THE ROUND TRIP, over the whole bit space and both signs of the id ------------
        t.Case("sigdelta/round-trip");
        bool allRoundTrip = true;
        foreach (int id in new[] { LogInstanceId, 1, -1, int.MaxValue, int.MinValue, 0, 907341 })
        {
            for (int bits = 0; bits < 256; bits++)
            {
                ulong h = WallSigDelta.Row(id, bits);
                if (!WallSigDelta.TryDecodeRow(h, out int gotId, out int gotBits)
                    || gotId != id || gotBits != bits)
                {
                    allRoundTrip = false;
                }
            }
        }
        t.True(allRoundTrip,
               "every (instance id, bits) pair must survive Row -> TryDecodeRow unchanged, "
               + "including negative ids — Unity hands those out and a sign-extension slip "
               + "would show up on nothing else");

        // ---- THE HARDWARE CONTROL: the log's own hashes -----------------------------------
        // The two terms the ModBuild 438 log decoded to. If the fold's shape ever changes, this
        // is the assertion that says so with the real numbers attached.
        t.Case("sigdelta/modbuild-438-terms");
        t.Equal(0x1D4F5F2962DBF448UL, WallSigDelta.Row(LogInstanceId, LogBitsActive),
                "the term the log's ENTER deltas carried is instance -14798 with bits 141");
        t.Equal(0x1D4EDF2962DB1AC8UL, WallSigDelta.Row(LogInstanceId, LogBitsHidden),
                "the term the log's LEAVE deltas carried is the SAME renderer with bits 13");
        t.True((LogBitsActive & WallSigDelta.BitMod) != 0
               && (LogBitsHidden & WallSigDelta.BitMod) != 0,
               "both patterns must carry the MOD bit — the finding this round acts on is that "
               + "the churning renderer is one of OURS");
        t.Equal(WallSigDelta.BitActive, LogBitsActive ^ LogBitsHidden,
                "the two patterns must differ in activeInHierarchy and nothing else");

        // ---- ONE ROW ENTERED -------------------------------------------------------------
        // ds == dx, which only a lone ADDED term can produce.
        t.Case("sigdelta/one-entered");
        ulong entered = WallSigDelta.Row(LogInstanceId, LogBitsActive);
        WallSigDelta.Delta e = WallSigDelta.Classify(
            EnterBankedSum, 0xAAAA_BBBB_CCCC_DDDDUL,
            unchecked(EnterBankedSum + entered), 0xAAAA_BBBB_CCCC_DDDDUL ^ entered);
        Kind(t, WallSigDelta.DeltaKind.Entered, e.Kind, "a lone added term decodes as Entered");
        t.Equal(entered, e.Row, "and carries that renderer's own term");
        t.True(WallSigDelta.TryDecodeRow(e.Row, out int eid, out int ebits)
               && eid == LogInstanceId && ebits == LogBitsActive,
               "which inverts to the renderer the log names");

        // ---- ONE ROW LEFT ----------------------------------------------------------------
        t.Case("sigdelta/one-left");
        ulong left = WallSigDelta.Row(LogInstanceId, LogBitsHidden);
        WallSigDelta.Delta l = WallSigDelta.Classify(
            0x1111_2222_3333_4444UL, 0x5555_6666_7777_8888UL,
            unchecked(0x1111_2222_3333_4444UL - left), 0x5555_6666_7777_8888UL ^ left);
        Kind(t, WallSigDelta.DeltaKind.Left, l.Kind, "a lone removed term decodes as Left");
        t.Equal(left, l.Row, "and carries that renderer's own term");

        // ---- ONE RENDERER DESTROYED ------------------------------------------------------
        // The row keeps its index and its term becomes the per-hole constant.
        t.Case("sigdelta/one-died");
        ulong alive = WallSigDelta.Row(4242, 129);
        WallSigDelta.Delta d = WallSigDelta.Classify(
            0x0F0F_0F0F_0F0F_0F0FUL, 0x00FF_00FF_00FF_00FFUL,
            unchecked(0x0F0F_0F0F_0F0F_0F0FUL - alive + WallSigDelta.DeadRow),
            0x00FF_00FF_00FF_00FFUL ^ alive ^ WallSigDelta.DeadRow);
        Kind(t, WallSigDelta.DeltaKind.Died, d.Kind,
                "a row that went to the per-hole term decodes as Died");
        t.Equal(alive, d.Row, "and carries the term the renderer had while it was alive");

        // ---- VERDICT BITS ONLY, AND THE -128 THE LOG ACTUALLY CONTAINS -------------------
        // The claim being pinned is the strong one: a small signed multiple of FnvPrime can
        // ONLY come from rows whose identity did not change, so this arm is a positive
        // statement that nothing was born, died or left the snapshot.
        t.Case("sigdelta/bits-only");
        ulong before = WallSigDelta.Row(LogInstanceId, LogBitsActive);
        ulong after = WallSigDelta.Row(LogInstanceId, LogBitsHidden);
        WallSigDelta.Delta b = WallSigDelta.Classify(
            0x9999_9999_9999_9999UL, 0x1234_5678_9ABC_DEF0UL,
            unchecked(0x9999_9999_9999_9999UL - before + after),
            0x1234_5678_9ABC_DEF0UL ^ before ^ after);
        Kind(t, WallSigDelta.DeltaKind.BitsOnly, b.Kind,
                "one renderer changing only its verdict bits decodes as BitsOnly");
        t.True(b.Quotient == 128 || b.Quotient == -128,
               "and the quotient is exactly +/-128 — the activeInHierarchy bit, which is the "
               + "delta the ModBuild 438 log carries verbatim (ds*P^-1 == -128)");
        t.Equal(WallSigDelta.BitActive, WallSigDelta.BitDeltaOf(before, after),
                "BitDeltaOf must cancel the identity and leave exactly the moved bit");

        // ---- A DIFFERENT RENDERER IN THE SAME ROW IS NOT A BIT CHANGE ---------------------
        t.Case("sigdelta/row-reused");
        t.Equal(-1, WallSigDelta.BitDeltaOf(WallSigDelta.Row(7, 3), WallSigDelta.Row(8, 3)),
                "two DIFFERENT identities must not be reported as a bit change — the census "
                + "would otherwise print a bit delta for a re-swept row");

        // ---- COMPOUND IS REPORTED AS COMPOUND, NOT GUESSED --------------------------------
        t.Case("sigdelta/compound");
        ulong a1 = WallSigDelta.Row(11, 5), a2 = WallSigDelta.Row(12, 6);
        WallSigDelta.Delta comp = WallSigDelta.Classify(
            0UL, 0UL, unchecked(a1 + a2), a1 ^ a2);
        Kind(t, WallSigDelta.DeltaKind.Compound, comp.Kind,
                "two renderers entering at once must be reported as Compound — a ladder that "
                + "names one of them would be the confident wrong answer this file exists to "
                + "prevent");

        // ---- THE UNKEYABLE HOLES' ARITHMETIC (ModBuild 440) -------------------------------
        // The id-keyed row census cannot pair two holes that never had an id, so it carries
        // their contribution as a closed form instead. The PARITY term is the half that is easy
        // to get backwards, and getting it backwards would turn every CROSS-CHECK PASSES into a
        // CROSS-CHECK FAILS on exactly the sessions where a renderer died mid-sweep — i.e. it
        // would indict the census for the scene's behaviour. Driven over both parities and the
        // null input here rather than discovered on hardware.
        t.Case("sigdelta/hole-correction");
        WallSigDelta.HoleCorrection(0, 0, out ulong hs, out ulong hx);
        t.Equal(0UL, hs, "no holes on either side contributes nothing to the sum");
        t.Equal(0UL, hx, "and nothing to the xor");
        WallSigDelta.HoleCorrection(3, 3, out hs, out hx);
        t.Equal(0UL, hs, "an equal count on both sides cancels in the sum");
        t.Equal(0UL, hx, "and cancels in the xor, because the parities match");
        WallSigDelta.HoleCorrection(1, 0, out hs, out hx);
        t.Equal(WallSigDelta.DeadRow, hs, "one hole gained adds exactly one per-hole term");
        t.Equal(WallSigDelta.DeadRow, hx, "and flips the xor by it, the parity having changed");
        WallSigDelta.HoleCorrection(0, 1, out hs, out hx);
        t.Equal(unchecked(0UL - WallSigDelta.DeadRow), hs,
                "one hole LOST subtracts it — the sum term is signed and the live side leads");
        t.Equal(WallSigDelta.DeadRow, hx, "and the xor is direction-free, as xor is");
        WallSigDelta.HoleCorrection(4, 2, out hs, out hx);
        t.Equal(unchecked(2UL * WallSigDelta.DeadRow), hs, "two holes gained add two terms");
        t.Equal(0UL, hx,
                "and move the xor by NOTHING — 4 and 2 have the same parity, which is the term "
                + "that is easy to get backwards and the reason this vector exists");

        // ---- THE NAMES ARE THE NAMES -----------------------------------------------------
        t.Case("sigdelta/names");
        t.Equal("mesh+mountable+MOD-OWNED+activeInHierarchy", WallSigDelta.Names(LogBitsActive),
                "bits 141 must read back as the pattern the log's ENTER events carried");
        t.Equal("none", WallSigDelta.Names(0), "an empty pattern says so rather than nothing");
    }
}
