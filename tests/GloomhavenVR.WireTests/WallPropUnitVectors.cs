using System.Collections.Generic;
using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// WHICH WALL OWNS A STATUE THAT TWO WALLS CLAIM.
///
/// <para>WHY THIS IS PINNED, and it is the sharpest version of the reason every non-wire file on
/// this list is here. The failure this arithmetic ships is not an exception and not a log line: it
/// is a prop that changes owner from one rescan to the next, i.e. a statue that pops in and out
/// every two seconds while neither wall it stands in changes state. Nothing in this repository can
/// notice that. It is observed by eye, from inside a headset, one photograph per round — and the
/// defect it is meant to cure has now been photographed twice
/// (<c>.planning/debug/skelet.jpg</c>, reported 2026-08-15 and again on 2026-08-19 against
/// ModBuild 166: <i>"Der Kopf des Skeletts wird immer noch ausgeblendet"</i>).</para>
///
/// <para>THE FIRST CASE BELOW IS THE REPORT, taken number for number out of
/// <c>.planning/debug/LogOutput.log</c>:</para>
/// <code>
/// 'CR_OS_Skeleton_Statue_Skull'[mesh]  anchor 5.1 gap 0.00: already the wall renderer of 'Wall 2' (that wall's fade 1.00)
/// 'CR_OS_Skeleton_Statue_Body'[mesh]   anchor 3.5 gap 0.00: already the wall renderer of 'Wall 6' (that wall's fade 0.00)
/// 'CR_OS_Skeleton_Statue_Broken'[mesh] anchor 3.6 gap 0.00: already the wall renderer of 'Wall 6' (that wall's fade 0.00)
/// </code>
/// <para>One statue, three renderers, two walls, two fades — head gone, body there. The assertion
/// is that this resolves to ONE owner, that the owner is the wall holding most of the statue, and
/// therefore that every piece carries that one wall's fade.</para>
///
/// <para>The remaining cases pin the three properties that are not visible in the report but are
/// what keep it fixed: an owner is never taken away from a wall that is mid-dissolve, a tie is
/// broken by geometry rather than by iteration order, and a total tie is broken by a key that is
/// identical on every machine in a multiplayer session.</para>
/// </summary>
internal static class WallPropUnitVectors
{
    /// <summary>The claim keys as <c>ClaimKeyOf</c> builds them: anchor name plus quantised XZ.</summary>
    private const string Wall2 = "Wall 2|37|21";
    private const string Wall6 = "Wall 6|41|18";
    private const string Wall3 = "Wall 3|29|18";

    internal static void Run(Harness t)
    {
        // ---- the report: skull on a faded wall, body + broken half on an unfaded one ----------
        t.Case("propunit/skelet-jpg");
        var statue = new List<WallPropUnit.Claim>
        {
            // 'Wall 2', fade 1.00, holds the skull only.
            new WallPropUnit.Claim(Wall2, rendererCount: 1, centroidGapXZ: 3.1f, faded: true),
            // 'Wall 6', fade 0.00, holds the body and the broken half.
            new WallPropUnit.Claim(Wall6, rendererCount: 2, centroidGapXZ: 0.4f, faded: false),
        };
        int owner = WallPropUnit.ChooseOwner(statue, stickyOwnerKey: null, out string rule);
        t.Equal(1, owner, "the wall holding most of the statue owns the whole statue");
        t.Equal(Wall6, statue[owner].Key, "and that wall is 'Wall 6', the body's wall");
        t.Equal("majority 2/3", rule, "…by MAJORITY, and the census says so");
        // The consequence, which is the whole point: there is exactly ONE fade for the unit, and
        // it is the owner's. The skull can no longer be at 1.00 while the body sits at 0.00.
        t.True(!statue[owner].Faded, "so this rescan the statue stays whole and visible");

        // The same unit once 'Wall 6' itself dissolves: still one owner, still one fade — and now
        // the skull goes WITH the body instead of ahead of it. Fading together was always the
        // requirement; exempting the pieces was explicitly refused (a solid skull in a dissolved
        // wall is the worse artefact).
        t.Case("propunit/skelet-jpg-owner-fades");
        var fading = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall2, 1, 3.1f, faded: false),
            new WallPropUnit.Claim(Wall6, 2, 0.4f, faded: true),
        };
        owner = WallPropUnit.ChooseOwner(fading, stickyOwnerKey: Wall6, out rule);
        t.Equal(Wall6, fading[owner].Key, "the owner does not change just because it is fading");
        t.Equal("sticky (owner mid-fade)", rule, "STICKY answered first, before majority");
        t.True(fading[owner].Faded, "and all three renderers dissolve on that one fade");

        // ---- stickiness is exactly as wide as it needs to be ----------------------------------
        // A minority owner KEEPS the unit while it is faded: handing the statue to a solid wall
        // mid-dissolve would snap it back to opaque inside a hole in the masonry.
        t.Case("propunit/sticky-minority-mid-fade");
        var minority = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall2, 1, 3.1f, faded: true),
            new WallPropUnit.Claim(Wall6, 2, 0.4f, faded: false),
        };
        owner = WallPropUnit.ChooseOwner(minority, stickyOwnerKey: Wall2, out rule);
        t.Equal(Wall2, minority[owner].Key, "a faded owner keeps the unit even in the minority");
        t.Equal("sticky (owner mid-fade)", rule, "and the reason is named");

        // …and only while it is faded. Once it is solid there is nothing to protect, so the unit
        // returns to the wall it actually belongs to. Without this the very first owner a unit
        // ever got would be permanent, which is a different way to ship the same photograph.
        t.Case("propunit/sticky-releases-when-solid");
        var settled = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall2, 1, 3.1f, faded: false),
            new WallPropUnit.Claim(Wall6, 2, 0.4f, faded: false),
        };
        owner = WallPropUnit.ChooseOwner(settled, stickyOwnerKey: Wall2, out rule);
        t.Equal(Wall6, settled[owner].Key, "a SOLID previous owner does not hold the unit");
        t.Equal("majority 2/3", rule, "majority decides again");

        // A sticky key naming a wall that no longer claims anything is simply ignored — an
        // Apparance rebuild retires walls constantly, and a stale key must not veto a decision.
        t.Case("propunit/sticky-stale-key");
        owner = WallPropUnit.ChooseOwner(statue, stickyOwnerKey: "Wall 9|3|3", out rule);
        t.Equal(Wall6, statue[owner].Key, "a stale sticky key falls through to majority");

        // ---- ties: geometry first, then a key that is the same on every machine ---------------
        // One piece each. The wall whose AABB centre is nearest the statue's centroid takes it.
        t.Case("propunit/tie-nearest-centroid");
        var split = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall2, 1, 3.1f, faded: false),
            new WallPropUnit.Claim(Wall6, 1, 0.4f, faded: false),
        };
        owner = WallPropUnit.ChooseOwner(split, stickyOwnerKey: null, out rule);
        t.Equal(Wall6, split[owner].Key, "the nearer wall takes a count-tied unit");
        t.True(rule.StartsWith("nearest centroid", System.StringComparison.Ordinal),
               "…and the census says NEAREST, not MAJORITY");

        // Nearest is compared with a tolerance on purpose: a wall run's AABB centre moves by a
        // hair whenever its renderer list changes by one piece, and a hair must not flip the
        // owner — that flip IS the pop this whole file exists to prevent.
        t.Case("propunit/tie-centroid-tolerance");
        var hair = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall6, 1, 2.000f, faded: false),
            new WallPropUnit.Claim(Wall3, 1, 2.000f + WallPropUnit.CentroidTieWU * 0.5f, faded: false),
        };
        owner = WallPropUnit.ChooseOwner(hair, stickyOwnerKey: null, out rule);
        t.True(rule.StartsWith("key order", System.StringComparison.Ordinal),
               "a difference inside the tolerance is a TIE, not a winner");
        t.Equal(Wall3, hair[owner].Key, "…and 'Wall 3' sorts before 'Wall 6' ordinally");

        // Total tie: the winner must not depend on which order the segment dictionary happened to
        // enumerate in. Two peers iterate their own dictionaries; they must still agree.
        t.Case("propunit/tie-key-order-is-machine-independent");
        var a = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall6, 2, 1.0f, faded: false),
            new WallPropUnit.Claim(Wall3, 2, 1.0f, faded: false),
        };
        var b = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall3, 2, 1.0f, faded: false),
            new WallPropUnit.Claim(Wall6, 2, 1.0f, faded: false),
        };
        int oa = WallPropUnit.ChooseOwner(a, null, out _);
        int ob = WallPropUnit.ChooseOwner(b, null, out _);
        t.Equal(a[oa].Key, b[ob].Key, "reversing the claim order picks the same wall");
        t.Equal(Wall3, a[oa].Key, "and it is the ordinal-lowest key, not the first entry");

        // ---- degenerate inputs ----------------------------------------------------------------
        t.Case("propunit/no-claimant");
        int none = WallPropUnit.ChooseOwner(new List<WallPropUnit.Claim>(), null, out rule);
        t.Equal(WallPropUnit.NoOwner, none, "a unit nobody claims has no owner");
        t.Equal("no claimant", rule, "and says so rather than picking one");

        // A unit entirely inside one wall is the overwhelmingly common shape, and it must resolve
        // to that wall with no drama — this is the answer the pass reaches for the 62 wall-mounted
        // dressing props and every masonry course in the report's log.
        t.Case("propunit/single-claimant");
        var whole = new List<WallPropUnit.Claim>
        {
            new WallPropUnit.Claim(Wall6, 4, 0.2f, faded: false),
        };
        owner = WallPropUnit.ChooseOwner(whole, null, out rule);
        t.Equal(0, owner, "one claimant owns it");
        t.Equal("majority 4/4", rule, "…and the census reads as the whole prop, not a fraction");

        // The denominator the census prints is "pieces that had an owner", so a unit split three
        // ways still reads honestly.
        t.Case("propunit/total-held");
        t.Equal(3, WallPropUnit.TotalHeld(statue), "1 + 2 renderers were held by some wall");
    }
}
