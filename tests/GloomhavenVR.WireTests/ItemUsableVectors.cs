using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

/// <summary>
/// SOURCE LINT, not a packet: the mirrored item-usable FRAME must be cut to the same numbers as the
/// owner's own.
///
/// <para>WHY THIS EXISTS. Record 35 carries WHICH items are usable; the frame that draws the answer
/// is built twice — once on the owner's chip (<c>Cards/Piles/ItemsPile.cs</c>,
/// <c>ItemChip.BuildUsableFrame</c>) and once on the peer's mirrored chip
/// (<c>Net/Remote/RemoteUsableFrame.cs</c>). Both use the SAME machinery
/// (<c>WorldUI.SoftCueArt.FrameSprite</c> + <c>WorldUI.SoftFramePulse</c>), which is the part that
/// matters; what they cannot share is the pixel geometry, because the owner's constants are private
/// to a nested class. "Share the machinery, not the look" is exactly the failure mode this project
/// has already paid for: matching the mechanism and letting the DIALS drift produces two cues that
/// are recognisably different and nobody can see it from inside their own headset, because from
/// there your own board is always right.</para>
///
/// <para>The drift is invisible in every other gate: both frames compile, both breathe, both appear
/// on the right cards. Only a side-by-side comparison in a headset with two players would show it,
/// which is the most expensive test this project has. So the numbers are compared HERE, against the
/// shipped source of both files, and a change to either side fails until the other is moved with
/// it.</para>
/// </summary>
internal static class ItemUsableVectors
{
    public static void Run(Harness t, string repoRoot)
    {
        t.Case("item-usable frame: the mirror is cut to the owner's numbers");

        string ownerPath = Path.Combine(repoRoot, "src", "GloomhavenVR", "Cards", "Piles",
                                        "ItemsPile.cs");
        string mirrorPath = Path.Combine(repoRoot, "src", "GloomhavenVR", "Net", "Remote",
                                         "RemoteUsableFrame.cs");
        t.True(File.Exists(ownerPath), "the owner's item pile source is where this lint expects it");
        t.True(File.Exists(mirrorPath), "…and so is the mirror's frame builder");
        if (!File.Exists(ownerPath) || !File.Exists(mirrorPath))
            return;

        string owner = File.ReadAllText(ownerPath);
        string mirror = File.ReadAllText(mirrorPath);

        // The four pixel/metre constants. Named identically on both sides on purpose — a rename is
        // itself a drift signal, and this lint is the thing that reports it.
        foreach (string name in new[] { "FrameReferencePixels", "FrameOutsetPixels",
                                        "FrameCornerRadiusPx", "FrameZ" })
        {
            string? a = Literal(owner, name);
            string? b = Literal(mirror, name);
            t.True(a != null, $"ItemsPile declares {name}");
            t.True(b != null, $"RemoteUsableFrame declares {name}");
            t.Equal(a ?? "<owner missing>", b ?? "<mirror missing>",
                    $"{name} is the same on the owner's chip and on the peer's mirror — a frame "
                    + "that is a different size on the two boards is a 1:1 violation nobody can "
                    + "see from inside their own headset");
        }

        // The breath. These ride as ARGUMENTS to SoftFramePulse.Init on both sides rather than as
        // named constants on the owner's, so they are matched against the mirror's named ones.
        t.Equal("0.45f", Literal(mirror, "FrameMinAlpha") ?? "<missing>",
                "the mirror's breath FLOOR is the owner's 0.45f — the frame never disappears "
                + "between beats on either board");
        t.Equal("1f", Literal(mirror, "FrameMaxAlpha") ?? "<missing>",
                "…its ceiling is the owner's full 1f");
        t.Equal("0.07f", Literal(mirror, "FrameScalePulse") ?? "<missing>",
                "…and its SILHOUETTE swing is the owner's 0.07f, which is the half of the cue a "
                + "bright passthrough room cannot swallow");
        t.True(owner.Contains("minAlpha: 0.45f, maxAlpha: 1f, scalePulse: 0.07f"),
               "and the OWNER still passes exactly those three to SoftFramePulse.Init — this is "
               + "the line the three numbers above are copied from");

        // The colour, stated as its four components on both sides.
        t.True(owner.Contains("new Color(1f, 0.80f, 0.32f, 1f)"),
               "the owner's telegraph gold is (1, 0.80, 0.32, 1)");
        t.True(mirror.Contains("new(1f, 0.80f, 0.32f, 1f)")
               || mirror.Contains("new Color(1f, 0.80f, 0.32f, 1f)"),
               "…and the mirror's is the same colour, not a look-alike");

        // Both must reach for the SAME machinery. A hand-rolled sine or a second sprite would make
        // the two cues breathe out of phase, which reads as two widgets flickering at each other
        // rather than as one cue — the exact reason the owner's own frame was moved onto the shared
        // heartbeat in the first place.
        t.True(mirror.Contains("WorldUI.SoftCueArt.FrameSprite("),
               "the mirror draws the SHARED outline sprite, not its own");
        t.True(mirror.Contains("WorldUI.SoftFramePulse>().Init("),
               "and breathes on the SHARED pulse component, so every framed card in the room beats "
               + "in phase off Time.unscaledTime");
        t.True(mirror.Contains("fillCenter = false"),
               "hollow on the mirror too — a frame, never a wash over the peer's card art");
        t.True(mirror.Contains("raycastTarget = false"),
               "and nothing on a peer's mirrored board may ever raycast");
        t.True(mirror.Contains("CardGlow.RankWithPanels("),
               "ranked against the panel ladder like the owner's frame — the reported 'die mixed "
               + "reality hintergründe schieben sich vor den outlines von karten' is a property of "
               + "this outline floating OUTSIDE the card's depth-writing silhouette, and it comes "
               + "back on the mirror if this is dropped");

        // The index-space hazard, asserted as a property of the SHIPPED text rather than as a
        // number: the owner's mask is set from the RAW AllItems index while both fan builders skip
        // null entries, so the mirror must re-seat the bits by counting non-nulls. A future edit
        // that "simplifies" ResolveSlots into mask >> i would put the frame on the neighbouring
        // card, and nothing else in this repo would notice.
        t.True(mirror.Contains("if (all[raw] == null)"),
               "the mirror's bit -> arc-slot mapping still SKIPS null inventory entries, because "
               + "the arc does and the mask does not");
        t.True(owner.Contains("if (i < UsableMaskBits)"),
               "and the owner still sets bit i from the RAW AllItems index, which is the half of "
               + "the pair that makes the skip necessary");

        RunAreaCaption(t, repoRoot, owner);
    }

    /// <summary>
    /// SOURCE LINT #2 (user report 2026-09-03): the item-use area's CAP and its engraved CAPTION
    /// name ONE zone and must not be able to disagree.
    ///
    /// <para>WHAT SHIPPED. The cap has carried a per-flow override since ModBuild 352 — an item
    /// SURRENDER demand relabels it so "the user must never read a surrender as an ordinary use" —
    /// and it travels to peers on wire record 13 bit 3. The CAPTION engraved under the recess was
    /// written once, at board build, from <c>Loc.Mod("item_use_area")</c>, on BOTH builders. So the
    /// keycap said "ITEM ABGEBEN" while the engraving under the very recess the item was being laid
    /// into still said "BENUTZEN", on the owner's board and on every mirror of it. Nothing in the
    /// build, the wire tests or any hardware log said so: the mod's own log line named the CAP and
    /// stopped there, and the KEYCAP SURFACE line printed a build-time GameObject NAME.</para>
    ///
    /// <para>The fix is one string set by one call on each side. This lint is what keeps it one: a
    /// future edit that re-introduces a bare <c>Loc.Mod("item_use_area")</c> at either caption site,
    /// or drops either side's shared accessor, fails here rather than on a co-op hardware round.</para>
    /// </summary>
    private static void RunAreaCaption(Harness t, string repoRoot, string ownerPile)
    {
        t.Case("item-use area: the cap and the recess caption cannot drift apart");

        string trayPath = Path.Combine(repoRoot, "src", "GloomhavenVR", "Cards", "Tray",
                                       "PlayTray.4.Slots.cs");
        string posePath = Path.Combine(repoRoot, "src", "GloomhavenVR", "Cards", "Tray",
                                       "PlayTray.3.Pose.cs");
        string mirrorPath = Path.Combine(repoRoot, "src", "GloomhavenVR", "Net", "Remote",
                                         "RemoteBoardFurniture.cs");
        t.True(File.Exists(trayPath) && File.Exists(posePath) && File.Exists(mirrorPath),
               "the three files that draw the item-use area are where this lint expects them");
        if (!File.Exists(trayPath) || !File.Exists(posePath) || !File.Exists(mirrorPath))
            return;

        string tray = File.ReadAllText(trayPath);
        string pose = File.ReadAllText(posePath);
        string mirror = File.ReadAllText(mirrorPath);

        // ONE ACCESSOR PER SIDE, and the caption built through it. The build-time literal is the
        // exact shape of the defect: a caption that is correct on the frame it is created and never
        // again.
        foreach ((string what, string src) in new[] { ("PlayTray", tray),
                                                      ("RemoteBoardFurniture", mirror) })
        {
            t.True(src.Contains("ItemUseAreaCaption()"),
                   $"{what} resolves the recess caption through the shared ItemUseAreaCaption() "
                   + "accessor, not from a literal at the build site");
            t.True(src.Contains("ApplyItemUseCaption()"),
                   $"{what} can RE-STATE the caption live — a caption that is only written at "
                   + "construction is the shipped defect");
        }
        t.True(tray.Contains("label.text = ItemUseAreaCaption();"),
               "the owner's berth caption is built from the accessor");
        t.True(mirror.Contains("caption.text = ItemUseAreaCaption();"),
               "…and so is the mirror's, so a peer reads the owner's word under the same recess");

        // THE PAIRING. Each side re-states the cap and the caption in ONE statement group: the cap
        // relabel is the line the caption relabel must sit next to, because relabelling only the cap
        // is exactly what shipped.
        t.True(Adjacent(pose, "_itemUseConfirm.SetLabel(", "ApplyItemUseCaption();", 12),
               "the owner re-states cap and caption together (PlayTray.RebuildAttachedControls)");
        t.True(Adjacent(mirror, "_use.SetLabel(use ??", "ApplyItemUseCaption();", 12),
               "and the mirror applies the wire wording to cap and caption together "
               + "(RemoteBoardFurniture.SetCapLabels)");

        // ONE WIRE FIELD FOR BOTH. Record 13 bit 3 carries the whole area's wording now; the seam
        // must report the AREA label first, or the caption's word never leaves the owner's machine
        // before the cap exists (which is after the card is already in the recess).
        t.True(Adjacent(tray, "internal string? ItemUseCapLabel =>", "_itemUseAreaLabel", 1)
               && Adjacent(tray, "internal string? ItemUseCapLabel =>", "_itemUseConfirm.CurrentLabel", 6),
               "ItemUseCapLabel reports the AREA wording first and falls back to the live cap text "
               + "— that ordering is what makes the caption's word reach a peer from the FIRST "
               + "frame of a demand, on the existing field and with no new one");

        // THE ELIGIBILITY REPLACEMENT (request (b)): the fan frame and the wire mask must come from
        // ONE predicate. Two predicates is how the stack could beat while no card was framed.
        t.True(ownerPile.Contains("private ItemCardPicker? DemandCandidateFilter()"),
               "ItemsPile has a single demand-eligibility accessor");
        t.True(CountOf(ownerPile, "DemandCandidateFilter()") >= 4,
               "…and it is what CanUseNow (the per-chip frame), UsableMask (the peer's bits), "
               + "HeldDemandCandidate (the approach ghost) and the diagnostic all ask — one "
               + "predicate rendered four ways, never four predicates");
        t.True(ownerPile.Contains("DemandItemsOverride() == null ? DemandCandidateFilter() : null"),
               "the WIRE mask still refuses the goal-chest forfeit, whose fan is built from REWARD "
               + "items and therefore has no Inventory.AllItems index a peer could resolve");

        // The approach ghost — the (a) half. The demand branch used to hard-force it off.
        t.True(ownerPile.Contains("TickUseGhost(_demandChip == null, HeldDemandCandidate());"),
               "the surrender demand drives the landing-preview ghost from its own candidate "
               + "finder; TickUseGhost(false, null) here was the missing approach feedback");
    }

    /// <summary>Do the two snippets appear within <paramref name="lines"/> lines of each other?
    /// The pairing this lint protects is a PROXIMITY fact, not a call-count one.</summary>
    private static bool Adjacent(string source, string a, string b, int lines)
    {
        int ia = source.IndexOf(a, StringComparison.Ordinal);
        if (ia < 0)
            return false;
        int ib = source.IndexOf(b, ia, StringComparison.Ordinal);
        if (ib < 0)
            return false;
        int newlines = 0;
        for (int i = ia; i < ib; i++)
            if (source[i] == '\n')
                newlines++;
        return newlines <= lines;
    }

    private static int CountOf(string source, string needle)
    {
        int n = 0;
        for (int i = source.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = source.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>The literal a <c>const</c>/<c>readonly</c> of <paramref name="name"/> is declared
    /// with, or null when the file does not declare one.</summary>
    private static string? Literal(string source, string name)
    {
        Match m = Regex.Match(source, @"\b" + Regex.Escape(name) + @"\s*=\s*([^;]+);");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
