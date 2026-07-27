using System;
using System.Collections.Generic;

namespace GloomhavenVR.Core;

/// <summary>
/// GERMAN text for the bound config entries' descriptions — the paragraph in the middle of
/// every hover bubble in Debug ▸ Alle Einstellungen. Keyed <c>"Section/Key"</c>; a <c>*</c> in
/// the key marks a per-hand-style / per-control-board FAMILY whose members share one text (see
/// <c>Loc.ConfigDescriptions.cs</c> for the resolution and for why the English at the bind site
/// stays the source of truth).
///
/// <para>ADDING A SETTING. Nothing here is required: an entry with no line in this table shows
/// its English description, complete and readable, and the tooltip stays in ONE language
/// either way. Translating it later is one line — no UI change, no registration.</para>
///
/// <para>The tooltip clips a description at 620 characters after collapsing whitespace, so these
/// are written tight rather than expansive; where the English already overran, so does this.</para>
/// </summary>
internal static partial class Loc
{
    private static Dictionary<string, string> BuildGerman() =>
        new(16, StringComparer.Ordinal)
        {
        };
}
