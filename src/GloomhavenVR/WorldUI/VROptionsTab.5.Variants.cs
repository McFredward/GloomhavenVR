using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// PER-VARIANT SETTINGS ARE SHOWN FOR THE VARIANT YOU ARE USING, not once per variant.
///
/// <para>Two families of settings are authored once per thing-you-can-choose: the control board's
/// element layout (<c>RestButtonOffset_Oak</c> / <c>_Steel</c> / <c>_Bronze</c>) and everything that
/// depends on which hand model is worn (<c>GloveScale</c>, <c>PlateGripPitchDegrees</c>,
/// <c>ArcaneOffsetY</c>, …). That is right for a config file and wrong for a menu: 35 board
/// properties became 105 rows and 22 hand properties became 64, and in both cases two-thirds of them
/// described something the player is not using and cannot see change.</para>
///
/// <para>The tab shows only the variant that is currently selected, and shows it WITHOUT the
/// variant in its name — the row reads "Rest Button Offset", not "Rest Button Offset Bronze",
/// because which one it belongs to is no longer a question. The heading above says which that is.
/// Switching the board or the hand style rebuilds the list; the rows simply become the new one's.</para>
///
/// <para>NOTHING IS HIDDEN PERMANENTLY. The entries are untouched in the config file, and the
/// variant you switch to brings its own rows with it.</para>
///
/// <para>FOLDING REQUIRES A SIBLING. A key is only treated as a variant when the SAME property
/// genuinely exists for at least one other variant in the catalog. That is what keeps the rule from
/// guessing: <c>GlovePinkyCounterAbduction</c> exists for the glove alone (it is that mesh's pinky),
/// has no sibling, and therefore always shows — a setting with no alternative is never hidden
/// behind a choice. It also means a future key that merely happens to start with a variant name
/// cannot be swallowed by accident.</para>
///
/// <para>THERE IS NO PER-MASK FAMILY. The head mask is chosen by <c>[Net] MaskId</c> and sized by
/// <c>[Net] MaskSize</c>; both are single entries that apply to whichever mask is picked, so there
/// is nothing per-mask to fold. (<c>[Rig] MaskedReaim*</c> is the tilt re-aim, an unrelated word.)</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>
    /// One family of per-variant settings: how its keys are marked, what is selected right now, and
    /// what to call both in the player's language.
    /// </summary>
    private sealed class VariantFamily
    {
        internal string[] Names = Array.Empty<string>();

        /// <summary>True when the variant is a SUFFIX (<c>_Oak</c>); false when it is a prefix (<c>Glove…</c>).</summary>
        internal bool Trailing;

        internal Func<string> Current = () => string.Empty;

        /// <summary>Localized name of the family itself ("Control board", "Hand style").</summary>
        internal Func<string> Label = () => string.Empty;

        /// <summary>Localized name of one variant ("Bronze", "Gauntlet").</summary>
        internal Func<string, string> Display = n => n;

        internal string Token(string name) => Trailing ? "_" + name : name;

        /// <summary>The variant this key belongs to, or null.</summary>
        internal string? VariantOf(string key)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                string token = Token(Names[i]);
                bool hit = Trailing
                    ? key.EndsWith(token, StringComparison.Ordinal)
                    : key.StartsWith(token, StringComparison.Ordinal);
                if (hit && key.Length > token.Length)
                    return Names[i];
            }
            return null;
        }

        /// <summary>The key with the variant taken off — the property's own name.</summary>
        internal string Strip(string key, string variant)
        {
            string token = Token(variant);
            return Trailing
                ? key.Substring(0, key.Length - token.Length)
                : key.Substring(token.Length);
        }

        /// <summary>The same property's key for another variant.</summary>
        internal string SiblingKey(string property, string variant) =>
            Trailing ? property + Token(variant) : Token(variant) + property;
    }

    private static readonly VariantFamily[] Families =
    {
        new()
        {
            Names = Enum.GetNames(typeof(ControlBoard)),
            Trailing = true,
            Current = () => CardsConfig.CurrentBoard.ToString(),
            Label = () => Loc.Mod("vr_var_board"),
            Display = n => Loc.Mod("vr_board_" + n.ToLowerInvariant()),
        },
        new()
        {
            Names = Enum.GetNames(typeof(HandStyle)),
            Trailing = false,
            Current = () => HandStyles.Clamp((int)Plugin.HandStyle.Value).ToString(),
            Label = () => Loc.Mod("vr_var_hand"),
            Display = n => Loc.Mod("vr_style_" + n.ToLowerInvariant()),
        },
    };

    /// <summary>
    /// The family and variant this entry belongs to, or null when it is not a per-variant setting
    /// (which includes a key that looks like one but has no sibling — see the class remarks).
    /// </summary>
    private static (VariantFamily Family, string Variant)? VariantOf(ConfigCatalog.ConfigItem item)
    {
        for (int f = 0; f < Families.Length; f++)
        {
            VariantFamily family = Families[f];
            string? variant = family.VariantOf(item.Key);
            if (variant == null)
                continue;

            string property = family.Strip(item.Key, variant);
            for (int i = 0; i < family.Names.Length; i++)
            {
                if (string.Equals(family.Names[i], variant, StringComparison.Ordinal))
                    continue;
                if (ByKey.ContainsKey(Id(item.Section, family.SiblingKey(property, family.Names[i]))))
                    return (family, variant);
            }
            return null; // no sibling: a one-off that merely starts or ends like a variant
        }
        return null;
    }

    /// <summary>
    /// Should this entry be listed? False only for a per-variant entry belonging to a variant other
    /// than the selected one. Read live, so switching rebuilds into the new one's rows.
    /// </summary>
    private static bool IsShownForCurrentVariant(ConfigCatalog.ConfigItem item)
    {
        EnsureLookup();
        var v = VariantOf(item);
        return v == null || string.Equals(v.Value.Variant, v.Value.Family.Current(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The caption for a per-variant row: the property's own name, without the variant. Null for
    /// everything else, which leaves the catalog's own display name in place.
    /// </summary>
    private static string? VariantFreeCaption(ConfigCatalog.ConfigItem item)
    {
        EnsureLookup();
        var v = VariantOf(item);
        return v == null
            ? null
            : ConfigCatalog.Spaced(v.Value.Family.Strip(item.Key, v.Value.Variant));
    }

    /// <summary>
    /// "Hand style: Gauntlet" for a heading that sits over per-variant rows, or null. Stated at the
    /// top of the block rather than on every row: once the rows have dropped the variant from their
    /// names, the heading is the only place that still says which one you are editing.
    /// </summary>
    private static string? VariantNote(System.Collections.Generic.IReadOnlyList<ConfigCatalog.ConfigItem> items)
    {
        EnsureLookup();
        for (int i = 0; i < items.Count; i++)
        {
            var v = VariantOf(items[i]);
            if (v == null)
                continue;
            VariantFamily family = v.Value.Family;
            return family.Label() + ": " + family.Display(family.Current());
        }
        return null;
    }

    /// <summary>Entries whose value decides which OTHER rows the pane lists.</summary>
    private static bool SelectsAVariant(ConfigCatalog.ConfigItem item) =>
        (string.Equals(item.Section, "Cards", StringComparison.Ordinal)
         && string.Equals(item.Key, "Board", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Hands", StringComparison.Ordinal)
            && string.Equals(item.Key, "HandStyle", StringComparison.Ordinal));
}
