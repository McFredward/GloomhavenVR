using System;
using System.Text;

namespace GloomhavenVR.Core;

/// <summary>
/// THE SCENE SIGNATURE'S DELTA IS DECODABLE, AND THE DELTA IS THE ANSWER.
///
/// <para><b>THE QUESTION THIS ROUND ASKS.</b> The user's ModBuild 438 report separates two
/// stalls that had been treated as one: <i>"Es ist okay dass es mal hängt wenn eine neue Tür
/// geöffnet wird … Aber diese Hänger lange danach die einfach 'random' auftreten sind
/// störend."</i> A commit on a room reveal is accepted. A commit long afterwards, with nothing
/// the player did to explain it, is the defect. In his log those late commits have exactly one
/// cause — the <c>WHY A CYCLE COMMITTED</c> counters in every window past line 11147 read
/// <c>0 no table yet, 0 room reveal, 0 asked for, 0 dissolve material swap, 0 board moved,
/// 2 scene signature moved, 0 wall signature moved, 0 STALENESS CEILING, 0 segment AABB
/// drift</c> — and nothing shipped could say WHICH renderer moved that signature.</para>
///
/// <para><b>WHY NOT THE CENSUS THAT EXISTS.</b> <see cref="WallSegmentFadeCulprits"/> answers
/// the same question and is switched off (<c>Defaults.Core.cs</c>, ModBuild 284), and its
/// producer costs ~9000 <c>GetInstanceID()</c> interop calls plus ~9000 dictionary inserts on
/// the commit frame — the frame this whole exercise exists to shrink. Nothing here replaces
/// it or turns it on. What is added instead is the observation this project's ledger already
/// carries under "A hash delta is decodable": the scene half is
/// <c>sum += h</c> / <c>xor ^= h</c> over <c>h = ((((FnvOffset ^ id) * P) ^ bits) * P)</c>,
/// and <c>P</c> is odd, so <c>P</c> is INVERTIBLE modulo 2^64. Every step of that fold can
/// therefore be run backwards, and a delta of two 64-bit pairs names its own cause with no
/// per-renderer state at all.</para>
///
/// <para><b>WHAT IT DECODES, AND THIS IS NOT A PROJECTION — IT WAS RUN AGAINST THE ModBuild 438
/// LOG BEFORE IT WAS WRITTEN.</b> Six of that log's twenty-five distinct scene-signature
/// refusals decode to a SINGLE event, and all six name the same renderer:
/// <list type="bullet">
///   <item><c>ds*P⁻¹ == -128</c> — one renderer changed exactly the <c>activeInHierarchy</c>
///     bit and nothing else moved (see <see cref="BitDeltaOf"/> for why a signed multiple of
///     P cannot be produced by a birth or a death).</item>
///   <item><c>ds == dx</c> — one renderer ENTERED the snapshot; <c>h = dx</c>, which
///     <see cref="TryDecodeRow"/> inverts to instance id <c>-14798</c>, bits <c>141</c> =
///     Mesh|Mountable|MOD|active.</item>
///   <item><c>ds == -dx</c> — one renderer LEFT it; <c>h = dx</c> inverts to the SAME instance
///     id <c>-14798</c>, bits <c>13</c> = Mesh|Mountable|MOD, i.e. the same object with its
///     active bit clear.</item>
/// </list>
/// One MOD-OWNED MeshRenderer, switching itself on and off, is moving a signature that gates a
/// ~155 ms table rebuild. That is the "random" hitch, and it is ours.</para>
///
/// <para><b>WHY IT IS A PURE FILE.</b> Modular inverses are exactly the kind of arithmetic that
/// looks right and is off by a factor. <c>WallSigDeltaVectors</c> in the wire suite drives the
/// round trip over the whole bit space and both signs of the id, pins
/// <see cref="FnvPrimeInverse"/> against <see cref="FnvPrime"/>, and drives the NULL input (no
/// delta ⇒ the decoder must say NOTHING MOVED and not invent an event). This project has been
/// burned three times by an instrument that was believed on sight; see "An instrument shipped
/// and lying".</para>
///
/// <para><b>WHAT IT DOES NOT DO.</b> It decides nothing. It never narrows a signature and it
/// never skips a commit. Every arm below either names an event or says plainly that the delta
/// is compound and it cannot — a decoder that guesses when it is out of information is worse
/// than one that reports "compound".</para>
/// </summary>
internal static class WallSigDelta
{
    /// <summary>The fold's constants, restated here rather than referenced, so the wire suite
    /// can drive this file with no Unity types in scope. They MUST equal
    /// <c>WallSegmentFade.FadeDriver.FnvOffset</c> / <c>FnvPrime</c>; the vectors pin that by
    /// reproducing a known row hash end to end.</summary>
    internal const ulong FnvOffset = 14695981039346656037UL;

    internal const ulong FnvPrime = 1099511628211UL;

    /// <summary>The multiplicative inverse of <see cref="FnvPrime"/> modulo 2^64 — the whole
    /// reason the fold is reversible. <c>FnvPrime * FnvPrimeInverse == 1</c> in unchecked
    /// 64-bit arithmetic, which <c>WallSigDeltaVectors</c> asserts rather than trusts.</summary>
    internal const ulong FnvPrimeInverse = 14886173955864302971UL;

    /// <summary>The per-hole term <c>ClassifySlice</c> folds for a snapshot row whose renderer
    /// has been destroyed. Restated for the same reason the two constants above are.</summary>
    internal const ulong DeadRow = 0xD1CE_D1CE_D1CE_D1CFUL;

    /// <summary>The eight verdict bits, in the order <c>ClassifySlice</c> packs them. Same
    /// values as <see cref="WallSegmentFadeCulprits"/>'s, deliberately not shared: that file is
    /// the OTHER instrument and coupling the two would mean a change to one silently re-reading
    /// the other's answer.</summary>
    internal const int BitMesh = 1;
    internal const int BitParticles = 2;
    internal const int BitMountable = 4;
    internal const int BitMod = 8;
    internal const int BitWallFade = 16;
    internal const int BitFoliage = 32;
    internal const int BitWater = 64;
    internal const int BitActive = 128;

    /// <summary>One renderer's contribution to the scene half — the EXACT expression
    /// <c>ClassifySlice</c> folds, so a vector can reproduce a hardware hash from an id and a
    /// bit pattern and prove the two agree.</summary>
    internal static ulong Row(int instanceId, int bits)
    {
        unchecked
        {
            ulong ident = (FnvOffset ^ (uint)instanceId) * FnvPrime;
            return (ident ^ (uint)bits) * FnvPrime;
        }
    }

    /// <summary>
    /// Run <see cref="Row"/> backwards: recover the instance id and the eight verdict bits from
    /// one renderer's term.
    ///
    /// <para>HOW IT IS UNIQUE RATHER THAN A GUESS. <c>h * P⁻¹</c> gives <c>ident ^ bits</c>
    /// exactly, and <c>bits</c> is one of 256 values, so there are 256 candidate <c>ident</c>s.
    /// Each is tested by running the OUTER step backwards too: <c>ident * P⁻¹</c> must equal
    /// <c>FnvOffset ^ (uint)id</c>, whose top 32 bits are therefore forced to match
    /// <c>FnvOffset</c>'s. That is a 32-bit check applied to 256 candidates, so a second
    /// candidate survives with probability ~2^-24 — and if one ever does, this returns false
    /// rather than picking the first, because an ambiguous decode reported as a fact is the
    /// failure mode this file exists to avoid.</para>
    /// </summary>
    internal static bool TryDecodeRow(ulong h, out int instanceId, out int bits)
    {
        instanceId = 0;
        bits = 0;
        unchecked
        {
            ulong identXorBits = h * FnvPrimeInverse;
            int found = 0;
            for (int b = 0; b < 256; b++)
            {
                ulong probe = (identXorBits ^ (uint)b) * FnvPrimeInverse;
                ulong id = probe ^ FnvOffset;
                if ((id >> 32) != 0)
                    continue;
                found++;
                if (found > 1)
                    return false; // ambiguous — say so instead of choosing
                instanceId = unchecked((int)(uint)id);
                bits = b;
            }
            return found == 1;
        }
    }

    /// <summary>
    /// The signed number the SUM delta divides by, when the delta is nothing but verdict bits
    /// moving under unchanged identities.
    ///
    /// <para>WHY IT SEPARATES THAT CASE FROM EVERY OTHER ONE. For a row whose identity did not
    /// change, <c>h' - h = ((ident^b') - (ident^b)) * P</c>, and <c>ident^b'</c> and
    /// <c>ident^b</c> differ only in their low eight bits — so the bracket is a small signed
    /// integer in [-255, 255], whatever the identity was. A birth, a death or a departure
    /// contributes a FULL-WIDTH term instead, and <c>ds * P⁻¹</c> for such a term is a 64-bit
    /// value with no reason to be small. So a small quotient is a positive statement: no
    /// renderer was born, died or left the snapshot this cycle, and the whole delta is verdict
    /// bits. (It can be defeated by an adversary; it cannot be defeated by a scene.)</para>
    /// </summary>
    internal static long SumDeltaQuotient(ulong sumDelta)
    {
        unchecked
        {
            return (long)(sumDelta * FnvPrimeInverse);
        }
    }

    /// <summary>The bits that differ between two terms of the SAME row. <c>h * P⁻¹</c> is
    /// <c>ident ^ bits</c>, so xoring two of them cancels the identity exactly and leaves
    /// <c>bits ^ bits'</c> — which is ≤ 255 iff the identity really did not change. Returns -1
    /// when it did (a re-swept row, or a row that went to <see cref="DeadRow"/>).</summary>
    internal static int BitDeltaOf(ulong oldRow, ulong newRow)
    {
        unchecked
        {
            ulong d = (oldRow * FnvPrimeInverse) ^ (newRow * FnvPrimeInverse);
            return d <= 255UL ? (int)d : -1;
        }
    }

    /// <summary>Name the bits of one verdict pattern, so a report says "stopped being drawn"
    /// rather than "141 -> 13".</summary>
    internal static string Names(int bits)
    {
        if (bits == 0)
            return "none";
        var sb = new StringBuilder(48);
        Append(sb, bits, BitMesh, "mesh");
        Append(sb, bits, BitParticles, "particles");
        Append(sb, bits, BitMountable, "mountable");
        Append(sb, bits, BitMod, "MOD-OWNED");
        Append(sb, bits, BitWallFade, "wallfade-shader");
        Append(sb, bits, BitFoliage, "foliage-shader");
        Append(sb, bits, BitWater, "water-surface");
        Append(sb, bits, BitActive, "activeInHierarchy");
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, int bits, int bit, string name)
    {
        if ((bits & bit) == 0)
            return;
        if (sb.Length > 0)
            sb.Append('+');
        sb.Append(name);
    }

    /// <summary>What kind of single event a scene-signature delta is, when it is a single event
    /// at all. See <see cref="Classify"/>.</summary>
    internal enum DeltaKind
    {
        /// <summary>Both accumulators stood still — the caller asked about a cycle that did not
        /// refuse on this term. Reported, never inferred away.</summary>
        Nothing,
        /// <summary>Identities all unchanged; only verdict bits moved. <c>Quotient</c> is their
        /// net signed movement and <c>Row</c>/<c>Bits</c> are unset.</summary>
        BitsOnly,
        /// <summary>Exactly one row appeared in the snapshot. <c>Row</c> is its term.</summary>
        Entered,
        /// <summary>Exactly one row left the snapshot. <c>Row</c> is its term.</summary>
        Left,
        /// <summary>Exactly one renderer was destroyed under a reused snapshot, so its row went
        /// to <see cref="DeadRow"/>. <c>Row</c> is the term it had before.</summary>
        Died,
        /// <summary>More than one thing moved (or one thing this ladder has no arm for). The
        /// per-row census is the instrument for this case; the arithmetic cannot separate it and
        /// says so.</summary>
        Compound,
    }

    internal readonly struct Delta
    {
        internal Delta(DeltaKind kind, long quotient, ulong row)
        {
            Kind = kind;
            Quotient = quotient;
            Row = row;
        }

        internal DeltaKind Kind { get; }

        /// <summary>Only meaningful for <see cref="DeltaKind.BitsOnly"/>.</summary>
        internal long Quotient { get; }

        /// <summary>Only meaningful for Entered / Left / Died — the renderer's own term, ready
        /// for <see cref="TryDecodeRow"/>.</summary>
        internal ulong Row { get; }
    }

    /// <summary>
    /// Name the event behind one scene-signature delta, or refuse.
    ///
    /// <para>ORDER IS LOAD-BEARING. The bits-only arm is tried FIRST because it is the only one
    /// that is a statement about every row at once ("nothing was born or died"); the single-row
    /// arms are tried after it and each is an exact identity, not a heuristic:
    /// <c>ds == dx</c> can only hold for a lone added term, <c>ds == -dx</c> for a lone removed
    /// one, and the death arm reconstructs the pre-death term from the xor and then CHECKS it
    /// against the sum. Anything that satisfies none of them is
    /// <see cref="DeltaKind.Compound"/>.</para>
    /// </summary>
    internal static Delta Classify(ulong bankedSum, ulong bankedXor, ulong liveSum, ulong liveXor)
    {
        unchecked
        {
            ulong ds = liveSum - bankedSum;
            ulong dx = bankedXor ^ liveXor;
            if (ds == 0 && dx == 0)
                return new Delta(DeltaKind.Nothing, 0, 0);
            long q = SumDeltaQuotient(ds);
            // The bound is 255 per row that moved, and a cycle in which a handful of renderers
            // toggled at once is the common case rather than the exception (the ModBuild 438 log
            // has one delta of exactly zero with a non-zero xor, which is two rows toggling the
            // SAME bit in opposite directions). Four rows' worth is the widest window in which
            // the "no full-width term is present" conclusion still holds by a comfortable margin
            // against 2^64.
            // q == 0 with a non-zero xor reaches this arm on purpose: that is two rows toggling
            // the same bit in OPPOSITE directions in one cycle, which cancels in the sum and
            // survives in the xor. It is a bits-only cycle like any other and the ModBuild 438
            // log contains two of them.
            if (q >= -1020 && q <= 1020)
                return new Delta(DeltaKind.BitsOnly, q, 0);
            if (ds == dx)
                return new Delta(DeltaKind.Entered, 0, dx);
            if (ds + dx == 0)
                return new Delta(DeltaKind.Left, 0, dx);
            ulong before = dx ^ DeadRow;
            if (DeadRow - before == ds)
                return new Delta(DeltaKind.Died, 0, before);
            return new Delta(DeltaKind.Compound, q, 0);
        }
    }
}
