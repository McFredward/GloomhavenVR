namespace GloomhavenVR.Core;

/// <summary>
/// TextMeshPro rich-text TAGS in a GAME string that a MOD-OWNED label is about to wear.
///
/// <para>ROOT CAUSE (user screenshot, .planning/debug/buttontext.jpg). The board's CONFIRM keycap
/// mirrors the pick confirm dialog's own commit option verbatim
/// (<c>CardsGameApi.PickDialogOptionLabel</c> reads <c>InputButton.ExtendedButton.buttonText.text</c>),
/// and the game's wording for burning a card is
/// <c>&lt;sprite name="LOST"&gt; Verbrennen 'Nagende Horde'</c> — a "lost card" glyph from a TMP
/// sprite asset, then the words. The game's own TextMeshProUGUI resolves that tag against a
/// sprite asset it has; the mod's keycap TextMeshPro is a bare label with NO sprite asset assigned
/// and none reachable through <c>TMP_Settings.defaultSpriteAsset</c> for that name, and TMP's rule
/// for an unresolvable sprite tag is to draw the tag AS TEXT. The key therefore read
/// <c>&lt;SPRITE NAME="LOST"&gt; VERBRENNEN 'NAGENDE HORDE'</c> (small caps by the font, not by any
/// upper-casing in this mod).</para>
///
/// <para>THE FIX IS TO STRIP, NOT TO RENDER, AND THE REASON IS STATED HERE SO IT IS NOT
/// RE-LITIGATED. Rendering the glyph means finding the game's sprite asset at label-read time
/// (it is not a mod asset), assigning it to the keycap TMP AND to every peer's mirror of that cap
/// (<c>Net.Remote.RemoteBoardFurniture.InertCap</c>), accepting a runtime <c>TMP_SubMesh</c>
/// renderer beside the label that the engraved outline/underlay recipe does not cover, and teaching
/// <c>TmpFit.FitCapLabel</c> to budget a glyph it cannot measure — for a glyph the word beside it
/// ("Verbrennen") already says. A keycap wears ONE engraved style, so a colour or size tag from the
/// game would be wrong on it too; every tag goes.</para>
///
/// <para>Pure string code, free of Unity on purpose: it is linked into the wire-test harness
/// (<c>tests/GloomhavenVR.WireTests/KeycapLabelVectors.cs</c>) so the exact screenshot string is
/// pinned, and so the fast path — a tag-free string hands back the SAME instance — stays a fast
/// path, because two change-gated callers compare by value every tick.</para>
/// </summary>
internal static class RichTextTags
{
    /// <summary>
    /// Remove every TMP rich-text tag from <paramref name="text"/> and collapse the whitespace the
    /// tags leave behind (runs to one space, no leading/trailing space). <paramref name="tags"/>
    /// receives how many tags were removed. A string with no tag is returned AS THE SAME INSTANCE.
    ///
    /// <para>A tag is <c>&lt;</c> followed by a letter, <c>/</c> or <c>#</c>, closed by the next
    /// <c>&gt;</c> with no further <c>&lt;</c> and no line break in between — which is TMP's own
    /// reading: a bare <c>&lt;</c> ("a &lt; b", "&lt;3") is text to TMP as well and stays text
    /// here.</para>
    /// </summary>
    internal static string Strip(string? text, out int tags)
    {
        tags = 0;
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        string s = text!;
        int first = FindTag(s, 0, out _);
        if (first < 0)
            return s;

        var sb = new System.Text.StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            int close;
            int open = FindTag(s, i, out close);
            if (open < 0)
            {
                sb.Append(s, i, s.Length - i);
                break;
            }
            sb.Append(s, i, open - i);
            tags++;
            i = close + 1;
        }
        return Collapse(sb.ToString());
    }

    /// <summary>Index of the next tag's <c>&lt;</c> at or after <paramref name="from"/>, with the
    /// index of its <c>&gt;</c> in <paramref name="close"/>; -1 when the rest is plain text.</summary>
    private static int FindTag(string s, int from, out int close)
    {
        close = -1;
        for (int i = from; i < s.Length; i++)
        {
            if (s[i] != '<' || i + 1 >= s.Length)
                continue;
            char n = s[i + 1];
            if (!char.IsLetter(n) && n != '/' && n != '#')
                continue;
            for (int j = i + 2; j < s.Length; j++)
            {
                char c = s[j];
                if (c == '>')
                {
                    close = j;
                    return i;
                }
                if (c == '<' || c == '\n' || c == '\r')
                    break; // not a tag: keep scanning from the next '<'
            }
        }
        return -1;
    }

    /// <summary>Whitespace runs to one space; no leading or trailing space.</summary>
    private static string Collapse(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool pendingSpace = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A raw string made safe to print inside a log line: angle brackets as
    /// <c>&amp;lt;</c>/<c>&amp;gt;</c> so no log viewer or later grep reads the tag as markup,
    /// line breaks as <c>\n</c>.</summary>
    internal static string Escape(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text!.Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "\\r").Replace("\n", "\\n");
}
