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
    }

    /// <summary>The literal a <c>const</c>/<c>readonly</c> of <paramref name="name"/> is declared
    /// with, or null when the file does not declare one.</summary>
    private static string? Literal(string source, string name)
    {
        Match m = Regex.Match(source, @"\b" + Regex.Escape(name) + @"\s*=\s*([^;]+);");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
