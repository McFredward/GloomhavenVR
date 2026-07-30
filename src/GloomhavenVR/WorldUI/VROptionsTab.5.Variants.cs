using System;
using GloomhavenVR.Cards;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// PER-VARIANT SETTINGS ARE SHOWN FOR THE VARIANT YOU ARE USING, not three times over.
///
/// <para>The control board's element layout is authored once per board — <c>RestButtonOffset_Oak</c>,
/// <c>RestButtonOffset_Steel</c>, <c>RestButtonOffset_Bronze</c> — which is right for the config
/// file and wrong for a menu: every row appeared three times, two of them describing boards the
/// player is not looking at and cannot see change. Thirty-five properties became a hundred and five
/// rows, and the two-thirds that did nothing were indistinguishable from the third that did.</para>
///
/// <para>So the tab shows only the suffix matching <c>[Cards] Board</c>, and shows it WITHOUT the
/// suffix: the row reads "Rest Button Offset", because which board it belongs to is no longer a
/// question — it is whichever one you have. Switching boards re-labels nothing and re-reads
/// everything; the rows simply become the new board's.</para>
///
/// <para>Nothing is hidden permanently. The entries are untouched in the config file, and a board
/// you switch to brings its own rows with it.</para>
///
/// <para>The hand styles (<c>GloveScale</c> / <c>PlateScale</c> / <c>ArcaneScale</c>) have exactly
/// this shape and are deliberately NOT folded in: they sit in the curated Avatar tab, where seeing
/// all three at once is the point — you pick a style by comparing them. This is for the Debug
/// topic lists, where the three are noise.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>Board suffixes, longest first so a prefix can never shadow a longer name.</summary>
    private static readonly string[] BoardSuffixes = BuildBoardSuffixes();

    private static string[] BuildBoardSuffixes()
    {
        Array values = Enum.GetValues(typeof(ControlBoard));
        var names = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
            names[i] = "_" + values.GetValue(i);
        Array.Sort(names, (a, b) => b.Length.CompareTo(a.Length));
        return names;
    }

    /// <summary>
    /// The board suffix on this key, or null when the key is not per-board. Cheap enough to ask on
    /// every row: three ordinal suffix tests.
    /// </summary>
    private static string? BoardSuffixOf(string key)
    {
        for (int i = 0; i < BoardSuffixes.Length; i++)
        {
            if (key.EndsWith(BoardSuffixes[i], StringComparison.Ordinal))
                return BoardSuffixes[i];
        }
        return null;
    }

    /// <summary>
    /// Should this entry be listed at all? False only for a per-board entry belonging to a board
    /// other than the selected one. Read live, so switching boards and reopening the pane shows the
    /// new board's rows.
    /// </summary>
    private static bool IsShownForCurrentBoard(ConfigCatalog.ConfigItem item)
    {
        string? suffix = BoardSuffixOf(item.Key);
        if (suffix == null)
            return true;

        // The selected board, read through the same accessor the tray itself uses so the menu can
        // never disagree with the board in front of the player.
        string current = "_" + CardsConfig.CurrentBoard;
        return string.Equals(suffix, current, StringComparison.Ordinal);
    }

    /// <summary>
    /// The caption for a per-board row: its own name with the board suffix taken off. Returns null
    /// for everything else, which leaves the catalog's own display name in place.
    /// </summary>
    private static string? BoardFreeCaption(ConfigCatalog.ConfigItem item)
    {
        string? suffix = BoardSuffixOf(item.Key);
        return suffix == null
            ? null
            : ConfigCatalog.Spaced(item.Key.Substring(0, item.Key.Length - suffix.Length));
    }
}
