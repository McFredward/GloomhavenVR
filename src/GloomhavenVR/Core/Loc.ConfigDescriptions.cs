using System;
using System.Collections.Generic;

namespace GloomhavenVR.Core;

/// <summary>
/// CONFIG-ENTRY DESCRIPTIONS — the other half of the in-VR config browser's hover text.
///
/// <para>WHY THIS FILE EXISTS (user, 2026-07: "Die Tooltipps bitte nicht immer in beiden Sprachen,
/// sondern der jeweiligen Sprache des Spiels … Mach alles auf die jeweilige lokalisierte Sprache").
/// Every tooltip in Debug ▸ Alle Einstellungen was structurally BILINGUAL: the chrome around it
/// (default, range, "wirkt sofort") came from <see cref="Loc.Mod"/> and was correctly German, while
/// the paragraph in the middle was the raw BepInEx description — English, because that is the
/// language a config FILE is written in. Half a tooltip in the wrong language is worse than a
/// consistent one, so the paragraph is localized here too.</para>
///
/// <para>SOURCE OF TRUTH STAYS AT THE BIND SITE. The English text remains the description passed to
/// <c>ConfigFile.Bind</c> — it is what the .cfg file on disk shows, and a config file is a developer
/// artifact. This table only holds the TRANSLATIONS, keyed by <c>"Section/Key"</c>. A description
/// added tomorrow with no entry here therefore degrades to its English source (readable, complete),
/// never to a missing-string placeholder, and never to both languages at once — see
/// <see cref="ConfigDescription"/>, whose miss returns <c>null</c> and lets the caller keep the
/// original.</para>
///
/// <para>FAMILIES. Twenty-one settings are bound once PER HAND STYLE (<c>GloveHeldScale</c>,
/// <c>PlateHeldScale</c>, …) and thirty-five once PER CONTROL BOARD (<c>BoardTilt_Oak</c>,
/// <c>BoardTilt_Steel</c>, …) — 489 bound entries from 377 written descriptions, whose text differs
/// only in the style/board it names. Those share ONE table entry under a wildcard key
/// (<c>Hands/*HeldScale</c>, <c>Cards/BoardTilt_*</c>); the exact key is always tried first, so a
/// real setting that merely happens to start with a style name (<c>Hands/GlovePinkyCounterAbduction</c>)
/// is never confused for a family member. The wildcard German text drops the style/board tag the
/// English one carries — the tooltip's first line already reads "[Cards] BoardTilt_Steel".</para>
///
/// <para>COST. The table is built LAZILY on the first hover in the config browser (most sessions
/// never open it) and then reused; a lookup is at most two dictionary probes plus six bounded
/// string comparisons. Nothing here runs per frame, and nothing here iterates without a bound.</para>
/// </summary>
internal static partial class Loc
{
    /// <summary>
    /// Localized text for a bound config entry's description, or <c>null</c> when the current
    /// language has no translation for it — in which case the caller keeps the English text the
    /// entry was bound with. Never returns a key or a placeholder.
    /// </summary>
    internal static string? ConfigDescription(string section, string key)
    {
        if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key))
            return null;

        Dictionary<string, string>? table = DescriptionTable(CurrentLanguage);
        if (table == null)
            return null; // English (the source language) or a language nobody translated into

        if (table.TryGetValue(section + "/" + key, out string exact))
            return exact;

        string? family = FamilyKey(key);
        return family != null && table.TryGetValue(section + "/" + family, out string shared)
            ? shared
            : null;
    }

    // ---- family keys (per-hand-style / per-control-board settings) --------------------------

    /// <summary>Hand-style prefixes of the per-style config keys — mirrors <c>HandStyle</c>.</summary>
    private static readonly string[] StylePrefixes = { "Glove", "Plate", "Arcane" };

    /// <summary>Control-board suffixes of the per-board config keys — mirrors <c>ControlBoard</c>.</summary>
    private static readonly string[] BoardSuffixes = { "_Oak", "_Steel", "_Bronze" };

    /// <summary>
    /// The wildcard key a per-style / per-board entry shares with its siblings, or <c>null</c> for
    /// an ordinary key. Only ever consulted AFTER an exact lookup missed, so this cannot hijack a
    /// setting whose own name starts with a style name. Bounded: six comparisons, no allocation on
    /// the common (null) path.
    /// </summary>
    private static string? FamilyKey(string key)
    {
        for (int i = 0; i < StylePrefixes.Length; i++)
        {
            string prefix = StylePrefixes[i];
            if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal))
                return "*" + key.Substring(prefix.Length);
        }
        for (int i = 0; i < BoardSuffixes.Length; i++)
        {
            string suffix = BoardSuffixes[i];
            if (key.Length > suffix.Length && key.EndsWith(suffix, StringComparison.Ordinal))
                return key.Substring(0, key.Length - suffix.Length + 1) + "*";
        }
        return null;
    }

    // ---- table -------------------------------------------------------------------------------

    /// <summary>
    /// Language name → (section/key → text). Built once, on the first config-browser hover; main
    /// thread only (the UI), so no lock: a racing second build would only waste one table.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>>? _descriptions;

    private static Dictionary<string, string>? DescriptionTable(string language)
    {
        Dictionary<string, Dictionary<string, string>> byLanguage =
            _descriptions ??= BuildDescriptions();
        return byLanguage.TryGetValue(language, out Dictionary<string, string> table) ? table : null;
    }

    /// <summary>
    /// One table per translated language. English is deliberately ABSENT: it is the source language,
    /// so a missing entry there is not a gap. A new language is a new entry here plus its own
    /// builder — the resolution above needs no change.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> BuildDescriptions() =>
        new(2, StringComparer.Ordinal)
        {
            ["German"] = BuildGerman(),
        };
}
