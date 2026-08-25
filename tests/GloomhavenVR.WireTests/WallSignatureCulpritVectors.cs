using System.Collections.Generic;
using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE INSTRUMENT THAT NAMES WHICH RENDERERS FORCED A ~90 ms COMMIT — driven here BEFORE it is
/// believed.
///
/// <para><b>WHY IT IS ON THIS LIST, and it is the reason every non-wire file here is.</b> This
/// project's own ledger has an entry titled <i>"An instrument shipped and lying"</i>, and
/// another called <i>"A truncated list is not absence"</i>, whose case was an ellipsis-capped
/// census list from which somebody concluded "X never appears" — X appeared, the list had simply
/// run out of room. A diagnostic's first output is a HYPOTHESIS, and a diagnostic that is wrong
/// does not throw, does not warn and does not look wrong: it produces a confident sentence in a
/// log that the next round then acts on. Sixteen builds have been lost to that shape.</para>
///
/// <para><b>SO THE TWO CONTROLS COME FIRST.</b> The NULL input — a live census identical to the
/// banked one — must report nothing found, and must report it in a form that ACCUSES THE
/// INSTRUMENT rather than reassuring the reader, because the only way that state can be reached
/// in the field is a cycle where the hash disagreed while the census agreed, i.e. the two are not
/// built from the same population. The KNOWN-POSITIVE control is a hand-built scene with exactly
/// one renderer entering, one leaving and one flipping a named bit, and the assertion is that the
/// census names exactly those three and no others.</para>
///
/// <para><b>THE ELISION PROPERTY IS PINNED SEPARATELY</b>, because it is the one this project has
/// already been burned by: with more name groups than the line can carry, the counts of what was
/// left out must be exact — groups AND renderers — and the total must still be the total. A cap
/// that silently drops the tail is how "the top five defects were all fine" once became a whole
/// wrong conclusion.</para>
/// </summary>
internal static class WallSignatureCulpritVectors
{
    private const int Mesh = WallSegmentFadeCulprits.BitMesh;
    private const int Wall = WallSegmentFadeCulprits.BitWallFade;
    private const int Active = WallSegmentFadeCulprits.BitActive;

    private static WallSegmentFadeCulprits.Entry E(int id, string name, int bits) =>
        new(id, name, bits);

    internal static void Run(Harness t)
    {
        // ---- CONTROL 1: THE NULL INPUT ---------------------------------------------------
        // Nothing moved. The census must find nothing — and the LINE must say so loudly,
        // because reaching this state in the field means the signature and the census disagree
        // about their own population and NOTHING may be concluded from that run.
        t.Case("sigculprits/null-input");
        var banked = new Dictionary<int, WallSegmentFadeCulprits.Banked>
        {
            [1] = new("Wall 2", Mesh | Wall | Active),
            [2] = new("Wall 3", Mesh | Wall | Active),
            [3] = new("Torch_Flame", Active),
        };
        var live = new List<WallSegmentFadeCulprits.Entry>
        {
            E(1, "Wall 2", Mesh | Wall | Active),
            E(2, "Wall 3", Mesh | Wall | Active),
            E(3, "Torch_Flame", Active),
        };
        WallSegmentFadeCulprits.Census c = WallSegmentFadeCulprits.Diff(banked, live, 10);
        t.Equal(0, c.Entered, "a null input enters nothing");
        t.Equal(0, c.Left, "…leaves nothing");
        t.Equal(0, c.Changed, "…and changes nothing");
        t.Equal(3, c.Unchanged, "every renderer is accounted for as identical");
        t.True(c.FoundNothing, "and the census says outright that it found nothing");
        string nullLine = WallSegmentFadeCulprits.Format(c, live.Count, banked.Count, true);
        t.True(nullLine.Contains("NOTHING MOVED"),
            "the null line ACCUSES THE INSTRUMENT rather than reading as a clean bill of health");
        t.True(nullLine.Contains("Do not narrow the signature"),
            "…and forbids the very action a reader would otherwise take from it");

        // The ORDER-FREEDOM half of the same control. One cycle in three takes a fresh
        // FindObjectsOfType sweep whose order Unity does not specify; a census that reported a
        // REORDERED array as a churning scene would indict the innocent on every sweep cycle,
        // which is exactly the mistake the signature's commutative fold exists to avoid.
        t.Case("sigculprits/order-is-not-change");
        var shuffled = new List<WallSegmentFadeCulprits.Entry>
        {
            E(3, "Torch_Flame", Active),
            E(1, "Wall 2", Mesh | Wall | Active),
            E(2, "Wall 3", Mesh | Wall | Active),
        };
        WallSegmentFadeCulprits.Census shuffledC =
            WallSegmentFadeCulprits.Diff(banked, shuffled, 10);
        t.True(shuffledC.FoundNothing, "a reordered snapshot is not a changed scene");

        // ---- CONTROL 2: THE KNOWN POSITIVE -----------------------------------------------
        // One in, one out, one bit flipped. Nothing else may be named.
        t.Case("sigculprits/known-positive");
        var live2 = new List<WallSegmentFadeCulprits.Entry>
        {
            E(1, "Wall 2", Mesh | Wall | Active),          // untouched
            E(2, "Wall 3", Mesh | Wall),                   // LOST activeInHierarchy
            E(9, "Torch_Flame", Active),                   // ENTERED
            // id 3 ('Torch_Flame') is gone from the live side -> LEFT
        };
        WallSegmentFadeCulprits.Census c2 = WallSegmentFadeCulprits.Diff(banked, live2, 10);
        t.Equal(1, c2.Entered, "exactly one renderer entered");
        t.Equal(1, c2.Left, "exactly one renderer left");
        t.Equal(1, c2.Changed, "exactly one renderer changed a verdict bit");
        t.Equal(1, c2.Unchanged, "and the one that did not move is not named");
        t.True(!c2.FoundNothing, "the census found movers, so it does not accuse itself");
        // WHICH BIT, which is the whole point: 'a renderer changed' and 'a renderer stopped
        // being wall-fade-capable' are different findings and only one of them indicts the
        // signature's width.
        t.Equal(1, c2.BitFlips[7], "…and the bit it moved is activeInHierarchy (bit 7)");
        t.Equal(0, c2.BitFlips[4], "…not the wall-fade-shader bit, which did not move");
        t.Equal(1, c2.ChangedGroupCount, "one name group among the changed");
        t.Equal("Wall 3", c2.ChangedGroups[0].Name, "…and it is named");
        t.Equal("Torch_Flame", c2.EnteredGroups[0].Name, "the entrant is named");
        // The LEAVER can only be named from the BANKED name — it no longer exists to be asked.
        // That is the whole reason RendererFact carries a Name at all.
        t.Equal("Torch_Flame", c2.LeftGroups[0].Name,
            "the leaver is named from the BANKED name, because it is gone and cannot be asked");

        // A renderer that moves TWO bits is counted under BOTH, and the report says so rather
        // than letting a reader add the per-bit tallies and get a wrong population.
        t.Case("sigculprits/two-bits-one-renderer");
        var live3 = new List<WallSegmentFadeCulprits.Entry>
        {
            E(1, "Wall 2", Mesh),                          // lost Wall AND Active
            E(2, "Wall 3", Mesh | Wall | Active),
            E(3, "Torch_Flame", Active),
        };
        WallSegmentFadeCulprits.Census c3 = WallSegmentFadeCulprits.Diff(banked, live3, 10);
        t.Equal(1, c3.Changed, "one renderer changed");
        t.Equal(1, c3.BitFlips[4], "…and it is counted under the wall-fade bit");
        t.Equal(1, c3.BitFlips[7], "…AND under activeInHierarchy — a tally, not a partition");
        t.True(WallSegmentFadeCulprits.Format(c3, live3.Count, banked.Count, true)
                .Contains("sum to at least the"),
            "and the line warns that the per-bit tallies are not a partition");

        // ---- THE ELISION PROPERTY --------------------------------------------------------
        // A truncated list is not absence. With more groups than the cap, the counts of what
        // was omitted must be EXACT on both axes and the class total must still be the total.
        t.Case("sigculprits/elision-is-counted-not-hidden");
        var empty = new Dictionary<int, WallSegmentFadeCulprits.Banked>();
        var many = new List<WallSegmentFadeCulprits.Entry>();
        int id = 100;
        // 12 groups: group k has k renderers, k = 1..12. Total 78.
        for (int k = 1; k <= 12; k++)
        {
            for (int n = 0; n < k; n++)
                many.Add(E(id++, "Prefab_" + k, Mesh));
        }
        WallSegmentFadeCulprits.Census c4 = WallSegmentFadeCulprits.Diff(empty, many, 4);
        t.Equal(78, c4.Entered, "the TOTAL is the whole population, never the listed subset");
        t.Equal(12, c4.EnteredGroupCount, "…and the group count is the whole group population");
        t.Equal(4, c4.EnteredGroups.Count, "the line lists the cap");
        t.Equal("Prefab_12", c4.EnteredGroups[0].Name, "ranked by count, largest first");
        t.Equal("Prefab_9", c4.EnteredGroups[3].Name, "…down to the fourth");
        t.Equal(8, c4.EnteredElidedGroups, "8 groups did not fit");
        // 12+11+10+9 = 42 listed, so 78-42 = 36 renderers elided.
        t.Equal(36, c4.EnteredElidedRenderers, "…covering exactly 36 renderers, counted");
        string manyLine = WallSegmentFadeCulprits.Format(c4, many.Count, 0, true);
        t.True(manyLine.Contains("8 further group(s) covering 36 renderer(s) NOT LISTED"),
            "and the LINE says both numbers — not an ellipsis, which is how a truncated list "
            + "has already been read as absence in this project");

        // Ranking must be STABLE, or two logs of the same scene disagree for no reason and the
        // reader chases a difference that is only Dictionary iteration order.
        t.Case("sigculprits/ties-break-by-name");
        var tied = new List<WallSegmentFadeCulprits.Entry>
        {
            E(1, "zebra", Mesh), E(2, "alpha", Mesh), E(3, "mango", Mesh),
        };
        WallSegmentFadeCulprits.Census c5 = WallSegmentFadeCulprits.Diff(empty, tied, 3);
        t.Equal("alpha", c5.EnteredGroups[0].Name, "equal counts sort by ordinal name…");
        t.Equal("mango", c5.EnteredGroups[1].Name, "…so the same scene prints the same line…");
        t.Equal("zebra", c5.EnteredGroups[2].Name, "…twice running");

        // ---- GROUPING ---------------------------------------------------------------------
        // Two instances of one prefab are one group; two DIFFERENT walls are two. The second
        // half is the one a cleverer normaliser would break, and 'Wall 2' vs 'Wall 3' is
        // precisely the distinction this whole census exists to make.
        t.Case("sigculprits/grouping");
        t.Equal("Torch", WallSegmentFadeCulprits.GroupKey("Torch(Clone)"),
            "a clone groups with its prefab");
        t.Equal("Torch", WallSegmentFadeCulprits.GroupKey("Torch (Clone)"),
            "…with or without the space Unity leaves");
        t.Equal("Wall 2", WallSegmentFadeCulprits.GroupKey("Wall 2"),
            "and a trailing number is part of the NAME — 'Wall 2' is not 'Wall 3'");
        t.Equal("<unnamed>", WallSegmentFadeCulprits.GroupKey(null),
            "a nameless renderer is still counted, under a name that says so");
        t.Equal("<unnamed>", WallSegmentFadeCulprits.GroupKey("(Clone)"),
            "…and so is one whose whole name was the clone tag");

        // ---- THE THIRD STATE --------------------------------------------------------------
        // No banked table at all (first commit of a session, or a scene load since). The line
        // must NOT read as an empty diff, which a reader would take for "nothing changed".
        t.Case("sigculprits/no-baseline");
        string cold = WallSegmentFadeCulprits.Format(
            new WallSegmentFadeCulprits.Census(), 5803, 0, tableBanked: false);
        t.True(cold.Contains("no banked census"),
            "with no baseline the line says so instead of printing a diff of nothing");
        t.True(!cold.Contains("NOTHING MOVED"),
            "…and does not borrow the self-accusing text, which means something else entirely");
    }
}
