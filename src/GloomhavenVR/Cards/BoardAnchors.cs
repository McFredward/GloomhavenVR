using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE ONE TABLE OF NAMED ANCHOR EMPTIES a bundled control board may carry, and the resolver that
/// turns those names into transforms.
///
/// <para><b>WHY THIS IS ITS OWN FILE.</b> The very same name list had been written out FOUR times —
/// <c>PlayTray.EnsureBuilt</c> (the local board), <c>Net.RemoteTrayVisual.Build</c> (a peer's copy of
/// that board), <c>Board.BoardFrame.AnchorNames</c> (the contour trace's exclusion set) and
/// <c>unity/…/Editor/BuildBoard.cs</c> (the asset assembler, a different assembly and therefore the
/// one copy that HAS to stay a copy). Three of those four are in this solution, and a board that
/// renames an anchor is exactly the change where one of them gets missed: the local board would seat
/// its keycaps on the new recess while a peer's mirror kept drawing them at the Oak fallback mount —
/// a divergence in the picture, which is the one thing the 1:1 rule forbids. One table, three
/// readers.</para>
///
/// <para><b>THE BUTTON SEATS ARE NUMBERED, NOT NAMED AFTER THEIR OCCUPANT.</b> The boards ship three
/// physical button recesses (user, 2026-08: "ist 3 das Maximum an gleichzeitigen Knöpfen. Daher
/// möchte alle 3 Boards so umgebaut haben, dass sie auf der rechten Seite … 3 statt 2 Slots für die
/// buttons haben"), and which control sits in which recess is a runtime question — Confirm and the
/// item "Use" cap already share seat 0 because they are mutually exclusive. So the mesh names a
/// SEAT (<c>ButtonSeat1/2/3</c>) and the mod decides the occupant.</para>
///
/// <para><b>BOTH SPELLINGS RESOLVE, AND THAT IS DELIBERATE, NOT TRANSITIONAL.</b> Every seat carries
/// an ALIAS list, tried in order, and the legacy <c>ConfirmButton</c> / <c>UndoButton</c> names are
/// permanent members of it:
/// <list type="bullet">
///   <item>seat 0 — <c>ButtonSeat1</c>, then <c>ConfirmButton</c></item>
///   <item>seat 1 — <c>ButtonSeat2</c>, then <c>UndoButton</c></item>
///   <item>seat 2 — <c>ButtonSeat3</c>, then <c>SkipButton</c></item>
/// </list>
/// The mod ships one DLL against whatever bundle is installed, and the shipped bundle
/// (72,966,925 bytes, unchanged for twenty builds — installs have been DLL-only) still carries the
/// two-anchor boards. A resolver that only knew the new spelling would put every existing player's
/// keycaps back on the procedural fallback anchors the moment they updated the plugin. The reverse
/// is just as real: the asset lane regenerating the three boards reports what it emits, and this
/// resolver must not care which of the two it turns out to be.</para>
///
/// <para><b>ADDITIVE OR RENAME?</b> Both, and the alias list is what makes that a non-question: for
/// seats 0 and 1 it is a RENAME WITH ALIASES (same recess, new preferred spelling, old spelling kept
/// forever), and seat 2 is genuinely ADDITIVE — no old board has a third recess, so its absence is
/// the normal case a two-anchor board must survive, not an error. Naming seat 2's alias
/// <c>SkipButton</c> costs nothing and covers the one other name a mesh author would plausibly reach
/// for, since Skip is the control the third recess exists for (see the three-cap enumeration in
/// <c>WorldUI/ButtonCluster.cs</c>).</para>
/// </summary>
internal static class BoardAnchors
{
    /// <summary>How many button seats the cluster can address. THREE, and the number is a finding,
    /// not a preference: <c>WorldUI/ButtonCluster.cs</c> enumerated every
    /// <c>readyButton</c>/<c>m_UndoButton</c>/<c>m_SkipButton</c> toggle site in the decompiled
    /// <c>Choreographer</c> and found six states where all three are live at once, and none where a
    /// fourth mod cap could join (<c>m_selectButton</c> is mutually exclusive with the ready button
    /// at every site that raises it, and the mod draws no cap for it).</summary>
    internal const int ButtonSeatCount = 3;

    /// <summary>
    /// The four anchors the board's ORIENTATION FRAME is derived from — <c>Slot1→Slot2</c> is the
    /// long axis, <c>ShortRestToken→LongRestToken</c> the short one, their cross product the
    /// decorated-face normal. Load-bearing in three places (<c>PlayTray.EnsureBuilt</c>,
    /// <c>RemoteTrayVisual.Build</c> and the editor assembler) and deliberately NOT extended by the
    /// button seats: the frame must not change meaning when a board gains or loses a recess.
    /// </summary>
    internal static readonly string[] FrameAnchorNames =
        { "Slot1", "Slot2", "ShortRestToken", "LongRestToken" };

    /// <summary>Accepted names per button seat, MOST PREFERRED FIRST. See the class note.</summary>
    private static readonly string[][] SeatAliases =
    {
        new[] { "ButtonSeat1", "ConfirmButton" },
        new[] { "ButtonSeat2", "UndoButton" },
        new[] { "ButtonSeat3", "SkipButton" },
    };

    /// <summary>
    /// EVERY name a bundled board may legitimately carry — the four frame anchors plus every accepted
    /// spelling of every seat. This is the set <c>BoardFrame</c> excludes from the contour trace
    /// (nothing parked on an anchor is part of the board's silhouette), so it has to list the
    /// ALIASES too: a board authored with the new spelling whose caps were traced as board geometry
    /// would push the stroke out past the real rim.
    /// </summary>
    internal static readonly string[] AllAnchorNames = BuildAllNames();

    private static string[] BuildAllNames()
    {
        int n = FrameAnchorNames.Length;
        foreach (string[] seat in SeatAliases)
            n += seat.Length;
        var all = new string[n];
        int w = 0;
        foreach (string s in FrameAnchorNames)
            all[w++] = s;
        foreach (string[] seat in SeatAliases)
            foreach (string s in seat)
                all[w++] = s;
        return all;
    }

    /// <summary>The preferred (authoring) name of button seat <paramref name="seat"/> — what a
    /// freshly generated board is expected to carry, and what the log names when it is missing.</summary>
    internal static string SeatName(int seat) =>
        seat >= 0 && seat < SeatAliases.Length ? SeatAliases[seat][0] : $"ButtonSeat{seat + 1}";

    /// <summary>Every accepted spelling of one seat, for a log line.</summary>
    internal static string SeatNamesJoined(int seat) =>
        seat >= 0 && seat < SeatAliases.Length ? string.Join("/", SeatAliases[seat]) : SeatName(seat);

    /// <summary>True when <paramref name="name"/> is one of the anchor names in
    /// <see cref="AllAnchorNames"/> (any seat, any spelling).</summary>
    internal static bool IsAnchorName(string name)
    {
        foreach (string s in AllAnchorNames)
            if (s == name)
                return true;
        return false;
    }

    /// <summary>
    /// Resolve button seat <paramref name="seat"/> under <paramref name="visualRoot"/>, trying every
    /// accepted spelling in order. Null when the board has no such recess — which is the ORDINARY
    /// answer for seat 2 on every board shipped so far, and the caller's cue to fall back
    /// (<c>PlayTray.BuildButtons</c> synthesises a procedural anchor; the peer mirror keeps its
    /// authored mount).
    /// </summary>
    internal static Transform? FindSeat(Transform visualRoot, int seat)
    {
        if (visualRoot == null || seat < 0 || seat >= SeatAliases.Length)
            return null;
        foreach (string name in SeatAliases[seat])
        {
            Transform? t = FindDeep(visualRoot, name);
            if (t != null)
                return t;
        }
        return null;
    }

    /// <summary>
    /// Resolve all <see cref="ButtonSeatCount"/> seats into <paramref name="into"/> (length
    /// <see cref="ButtonSeatCount"/>) and return how many the board actually supplies. A board that
    /// supplies seats 0 and 1 but not 2 returns 2 — the two-anchor fallback, not a failure.
    /// </summary>
    internal static int ResolveSeats(Transform visualRoot, Transform?[] into)
    {
        int found = 0;
        for (int i = 0; i < ButtonSeatCount && i < into.Length; i++)
        {
            into[i] = FindSeat(visualRoot, i);
            if (into[i] != null)
                found++;
        }
        return found;
    }

    /// <summary>Depth-first name lookup — the same one every board reader already used privately.</summary>
    internal static Transform? FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }
}
