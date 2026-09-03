using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// WHETHER A GAME STRING'S RICH-TEXT TAGS REACH THE KEY. The CONFIRM keycap on the board wears
/// the game's own pick-dialog wording, and for a card burn that wording is
/// <c>&lt;sprite name="LOST"&gt; Verbrennen 'Nagende Horde'</c>; the key printed the tag as text,
/// in small caps, on a user screenshot (.planning/debug/buttontext.jpg), and every gate on this
/// harness agreed with it. The seam that cleans it (<see cref="RichTextTags.Strip"/>) is pure
/// string code, so the exact screenshot string is pinned here, next to the two properties the two
/// change-gated callers depend on: a tag-free string comes back as the SAME instance, and a bare
/// <c>&lt;</c> that TMP would print stays printed.
/// </summary>
internal static class KeycapLabelVectors
{
    internal static void Run(Harness t)
    {
        t.Case("keycap label: the screenshot string");
        string raw = "<sprite name=\"LOST\"> Verbrennen 'Nagende Horde'";
        string clean = RichTextTags.Strip(raw, out int tags);
        t.Equal("Verbrennen 'Nagende Horde'", clean, "burn wording reads without the sprite tag");
        t.Equal(1, tags, "one tag counted");

        t.Case("keycap label: tag-free string is the same instance");
        string plain = "Auswahl beenden";
        t.True(ReferenceEquals(plain, RichTextTags.Strip(plain, out tags)), "same instance back");
        t.Equal(0, tags, "no tag counted");

        t.Case("keycap label: several tags, upper-cased tags, whitespace collapsed");
        clean = RichTextTags.Strip("<SPRITE NAME=\"LOST\">  <b>Verbrennen</b>\n'Nagende  Horde' <color=#ff0000>!</color> ", out tags);
        t.Equal("Verbrennen 'Nagende Horde' !", clean, "all tags gone, runs collapsed, trimmed");
        t.Equal(5, tags, "five tags counted");

        t.Case("keycap label: index and hash forms");
        clean = RichTextTags.Strip("<sprite=3>Karte <#00ff00>lost</color>", out tags);
        t.Equal("Karte lost", clean, "sprite index and colour hash forms");
        t.Equal(3, tags, "three tags counted");

        t.Case("keycap label: a bare '<' is text, as it is to TMP");
        string lt = "a < b und <3";
        t.True(ReferenceEquals(lt, RichTextTags.Strip(lt, out tags)), "no tag → same instance");
        t.Equal(0, tags, "bare angle brackets are not tags");
        clean = RichTextTags.Strip("x <b y </b>", out tags);
        t.Equal("x <b y", clean, "an unclosed '<b' before a real tag is text");
        t.Equal(1, tags, "only the closed tag counted");

        t.Case("keycap label: a tag cannot span a line break");
        clean = RichTextTags.Strip("<sprite\nname=\"LOST\"> Wort", out tags);
        t.Equal("<sprite\nname=\"LOST\"> Wort", clean, "broken tag left as text");
        t.Equal(0, tags, "no tag counted across a line break");

        t.Case("keycap label: null and empty");
        t.Equal(string.Empty, RichTextTags.Strip(null, out tags), "null → empty");
        t.Equal(0, tags, "null → no tag");
        t.Equal(string.Empty, RichTextTags.Strip("<sprite name=\"LOST\">", out tags), "a lone tag → empty");
        t.Equal(1, tags, "the lone tag counted");

        t.Case("keycap label: log escape");
        t.Equal("&lt;sprite name=\"LOST\"&gt; Verbrennen\\n", RichTextTags.Escape("<sprite name=\"LOST\"> Verbrennen\n"),
                "angle brackets and line breaks escaped for the log line");
    }
}
