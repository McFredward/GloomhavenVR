using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The curated everyday view: the categories of settings a player actually reaches for, chosen by
/// hand, plus "Debug" which still reaches every one of the mod's remaining entries.
///
/// <para>WHY CURATION IS AN EXPLICIT LIST AND NOT A RULE. Every mechanical shortcut tried on this
/// data fails on real entries. Sorting by topic leaves migration flags like
/// <c>TableScaleDefault25Applied</c> and <c>BoardScaleDefault04Applied</c> sitting between
/// "Free Movement" and "Turn Mode" — internal bookkeeping the player must never touch, with names
/// that look like settings. Filtering on a key-name pattern would take <c>DebugGizmos</c> out and
/// leave <c>SavedScaleMultiplier</c> in. The catalog's <c>Pin</c> rank covers only the [Perf]
/// section. So the everyday list is written down, one entry at a time, and everything not on it
/// stays reachable under "Erweitert" rather than being hidden.</para>
///
/// <para>NOTHING IS LOST BY BEING LEFT OFF. That is what makes an explicit list safe here: the
/// advanced view is the complete catalog, so a curation mistake costs one extra click, never
/// access to a setting.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>
    /// One curated row: which config entry it edits, and WHAT IT IS CALLED.
    ///
    /// <para>The caption is a localization key rather than the config key, because the config key is
    /// a programmer's name. Left to itself the row reads "Actor Bars", "Action Element Hints",
    /// "Masked Reaim Deadband" — in German too, since the catalog's display name is just the key
    /// with its camel humps spaced out. A player-facing menu has to be in the player's language,
    /// captions included, so every curated row carries its own.</para>
    /// </summary>
    internal readonly struct CuratedEntry
    {
        internal readonly string Section;
        internal readonly string Key;
        internal readonly string CaptionKey;

        internal CuratedEntry(string section, string key, string captionKey)
        {
            Section = section;
            Key = key;
            CaptionKey = captionKey;
        }

        internal string Caption => Loc.Mod(CaptionKey);

        /// <summary>Hint key, derived from the caption key so the two can never drift apart.</summary>
        internal string HintKey => "h_" + CaptionKey;
    }

    /// <summary>A titled block of settings inside a category — the second level of order.</summary>
    internal sealed class CuratedSection
    {
        internal string LocKey = string.Empty;
        internal CuratedEntry[] Entries = System.Array.Empty<CuratedEntry>();

        internal string Label => Loc.Mod(LocKey);
    }

    /// <summary>One sub-tab: a caption and the sections beneath it.</summary>
    internal sealed class CuratedCategory
    {
        internal string LocKey = string.Empty;
        internal CuratedSection[] Sections = System.Array.Empty<CuratedSection>();

        internal string Label => Loc.Mod(LocKey);
    }

    /// <summary>
    /// THE TABS THE MOD'S OWN PANEL USED — Komfort, Grafik, Tafeln, Avatar, Debug — and the same
    /// reasoning behind them: each names what the player is trying to DO (sit comfortably, make the
    /// picture right, deal with the mod's panels, decide how they look, tune). That arrangement was
    /// arrived at once already; re-deriving it differently would only make the menu disagree with
    /// itself.
    ///
    /// <para>MULTIPLAYER JOINED THEM because it failed that test: it was a section inside Avatar, so
    /// "how much of other players' boards you see" was filed under how your own hands look. It names
    /// something a player is trying to do, so it is a tab.</para>
    ///
    /// <para>Section headers inside a tab are one navigation level cheaper than another tab, which
    /// is why grouping happens there and a new tab has to earn itself.</para>
    ///
    /// <para>DEBUG IS THE ODD ONE and gets no hand-picked list at all. Hand-curating several
    /// hundred tuning constants would be guesswork with no user to serve, and any list would rot
    /// silently as entries are added. It is instead the catalog's own topic index: a page of topic
    /// links, one topic at a time behind each. That subdivision maintains itself — a new setting
    /// appears under its topic without anyone remembering to add it — and it is the only place in
    /// the tab where completeness matters more than selection.</para>
    /// </summary>
    internal static readonly CuratedCategory[] Curated =
    {
        new()
        {
            LocKey = "comfort",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "sec_table_world",
                    Entries = new CuratedEntry[]
                    {
                        new("Rig", "WorldScale", "table_scale"),
                        new("Comfort", "TableHeightOffset", "table_height"),
                        new("Rig", "WorldTiltDegrees", "vr_o_worldtilt"),
                    },
                },
                new()
                {
                    LocKey = "sec_movement",
                    Entries = new CuratedEntry[]
                    {
                        new("Comfort", "TurnMode", "turning"),
                        new("Comfort", "SnapTurnDegrees", "vr_o_snapdeg"),
                        new("Comfort", "SmoothTurnSpeed", "vr_o_smoothspeed"),
                        new("Comfort", "TurnHand", "vr_o_turnhand"),
                        new("Comfort", "FreeMovement", "free_movement"),
                        new("Comfort", "WorldGrabEnabled", "world_grab"),
                        new("Comfort", "VerticalDrag", "vr_o_vdrag"),
                        new("Comfort", "RotateEnabled", "vr_o_rotate"),
                        new("Comfort", "ScaleEnabled", "vr_o_scale"),
                        new("Comfort", "RecenterHoldSeconds", "vr_o_recenterhold"),
                    },
                },
                new()
                {
                    LocKey = "sec_hands_aim",
                    Entries = new CuratedEntry[]
                    {
                        new("Hands", "PrimaryHand", "vr_o_primaryhand"),
                        new("Hands", "RayAlwaysOn", "vr_o_rayalways"),
                        new("Hands", "ScrollWithStickOnly", "vr_o_stickscroll"),
                        new("Hands", "LaserFingerOrigin", "vr_o_laserorigin"),
                        new("Hands", "ModalRayConeDegrees", "vr_o_raycone"),
                    },
                },
            },
        },
        new()
        {
            LocKey = "cat_graphics",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "sec_presentation",
                    Entries = new CuratedEntry[]
                    {
                        new("Compat", "DisablePostProcessing", "disable_post"),
                        new("Compat", "DisableVolumetricFog", "vr_o_fog"),
                        new("Compat", "WallFade", "wall_see_through"),
                        new("Rig", "ForwardRendering", "vr_o_forward"),
                        new("Rig", "MenuRig", "vr_o_menurig"),
                    },
                },
                new()
                {
                    // The mod's own panel offered Mixed Reality here, with a key-colour picker.
                    // Rig/VoidColor is NOT the same setting and does not replace it: MR also
                    // disables every skybox and sweeps the sky geometry, which is the half that
                    // makes passthrough compositing work at all.
                    LocKey = "mixed_reality",
                    Entries = new CuratedEntry[]
                    {
                        new("MixedReality", "Enabled", "mixed_reality"),
                        new("MixedReality", "KeyColor", "key_color"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_performance",
                    Entries = new CuratedEntry[]
                    {
                        new("Core", "EnableGraphicsJobs", "vr_o_gfxjobs"),
                        new("Core", "AutoRestartForGraphicsJobs", "vr_o_autorestart"),
                    },
                },
            },
        },
        new()
        {
            LocKey = "cat_panels",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "vr_sec_panels",
                    Entries = new CuratedEntry[]
                    {
                        new("WorldUI", "CombatLog", "show_combat_log"),
                        new("WorldUI", "ActorBars", "vr_o_actorbars"),
                        new("WorldUI", "ActionElementHints", "element_hints"),
                        new("WorldUI", "ButtonCluster", "vr_o_buttoncluster"),
                        new("WorldUI", "Dialogs", "vr_o_dialogs"),
                        new("WorldUI", "DecisionDock", "vr_o_decisiondock"),
                        new("WorldUI", "EnemyReveal", "vr_o_enemyreveal"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_keyboard",
                    Entries = new CuratedEntry[]
                    {
                        new("Keyboard", "Enabled", "vr_o_keyboard"),
                        new("Keyboard", "AutoCapitalise", "vr_o_keyboardcase"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_cards",
                    Entries = new CuratedEntry[]
                    {
                        new("Cards", "Board", "control_board"),
                        new("Cards", "TrayScale", "vr_o_trayscale"),
                        new("Cards", "TrayFollow", "vr_o_trayfollow"),
                        new("Cards", "InspectScale", "vr_o_inspectscale"),
                        new("Cards", "RevealMode", "vr_o_revealmode"),
                        new("Cards", "GrabButton", "vr_o_grabbutton"),
                    },
                },
            },
        },
        new()
        {
            // AVATAR WAS ALMOST ENTIRELY DEAD. Of its six appearance rows only HandStyle did
            // anything: four were the shared hand-seat keys, superseded by absolute per-style keys
            // and marked "LEGACY — no effect" in their own descriptions, and the fifth was
            // HandColor — an RRGGBB string, so the left/right stepper had nothing to step and the
            // player pressed a control that could not move. It also only ever tinted the
            // PROCEDURAL fallback hand, which nobody wearing one of the three hand models sees.
            // The live successors ({Style}Scale, {Style}GripPitchDegrees, …) are per-style and
            // per-controller calibration; the ones a player actually chooses are here, the rest
            // stay under Erweitert.
            LocKey = "avatar",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "sec_appearance",
                    Entries = new CuratedEntry[]
                    {
                        new("Hands", "HandStyle", "hands"),
                        // All three are listed and exactly ONE survives: the per-variant filter
                        // keeps the style being worn, and the section heading names it. Listing
                        // them is what makes the row appear at all for every style.
                        new("Hands", "GloveScale", "vr_o_handscale"),
                        new("Hands", "PlateScale", "vr_o_handscale"),
                        new("Hands", "ArcaneScale", "vr_o_handscale"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_mirror",
                    Entries = new CuratedEntry[]
                    {
                        new("Net", "MirrorEnabled", "mirror"),
                    },
                },
            },
        },
        new()
        {
            // MULTIPLAYER IS ITS OWN AREA NOW. These rows had been a section inside Avatar, which
            // put "how much of other players' boards you see" under the heading for how your own
            // hands look. What stays in Avatar is what you choose about YOURSELF and can check on
            // your own; what moved here is everything that only means anything once someone else
            // is in the game. Rig/SpawnInCircle comes along from the Graphics tab for the same
            // reason: its own description opens with "Multiplayer:".
            LocKey = "cat_multiplayer",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "vr_sec_mp_presence",
                    Entries = new CuratedEntry[]
                    {
                        new("Net", "Enabled", "vr_o_netenabled"),
                        new("Rig", "SpawnInCircle", "vr_o_circle"),
                        new("Net", "RemoteBoards", "remote_boards"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_mp_avatar",
                    Entries = new CuratedEntry[]
                    {
                        new("Net", "MaskId", "head_mask"),
                        new("Net", "MaskSize", "mask_size"),
                    },
                },
            },
        },
    };

    /// <summary>
    /// Section+Key → the catalog entry, rebuilt whenever the catalog is. Section and key alone
    /// are enough: the mod's config files do not repeat a section name between them.
    /// </summary>
    private static readonly Dictionary<string, ConfigCatalog.ConfigItem> ByKey = new(512);

    private static int _lookupSignature = -1;

    /// <summary>
    /// Refresh the lookup when the catalog has changed. Keyed on the entry count rather than a
    /// dirty flag, because the catalog rebuilds itself on its own schedule and the tab only ever
    /// sees the result.
    /// </summary>
    private static void EnsureLookup()
    {
        if (_lookupSignature == ConfigCatalog.TotalEntries && ByKey.Count > 0)
            return;

        ByKey.Clear();
        for (int t = 0; t < ConfigCatalog.TopicCount; t++)
        {
            IReadOnlyList<ConfigCatalog.ConfigGroup> groups = ConfigCatalog.Groups((ConfigCatalog.ConfigTopic)t);
            for (int g = 0; g < groups.Count; g++)
            {
                List<ConfigCatalog.ConfigItem> items = groups[g].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    string id = Id(items[i].Section, items[i].Key);
                    if (!ByKey.ContainsKey(id))
                        ByKey[id] = items[i];
                }
            }
        }

        _lookupSignature = ConfigCatalog.TotalEntries;
    }

    private static string Id(string section, string key) => section + "/" + key;

    /// <summary>
    /// Rows whose CONTROL does not follow from the entry's type.
    ///
    /// <para>The key colour is stored as a Color, so the generic builder would offer four numeric
    /// steppers — R, G, B and A. That is not a setting a player can use: the value is only
    /// meaningful as one of a handful of named chroma keys, which is exactly how the mod's own
    /// panel presented it. So this one entry gets a named-preset dropdown instead, over
    /// <see cref="MixedReality"/>'s own preset list.</para>
    ///
    /// <para>Kept as a lookup rather than an if-chain inside the row kit: the kit stays free of
    /// knowledge about individual settings, and the next entry that needs a hand-made control is
    /// one table row rather than another branch.</para>
    /// </summary>
    private static bool HasSpecialRow(ConfigCatalog.ConfigItem item) =>
        string.Equals(item.Section, "MixedReality", StringComparison.Ordinal)
        && string.Equals(item.Key, "KeyColor", StringComparison.Ordinal);

    private static bool TryBuildSpecialRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption,
                                           string? hintKey)
    {
        if (!HasSpecialRow(item))
            return false;

        BuildPresetRow(parent, item, caption, hintKey, MixedReality.KeyColorNames, MixedReality.KeyColorIndex,
                       MixedReality.SetKeyColor);
        return true;
    }

    /// <summary>
    /// Look one curated entry up. A miss is REPORTED, not silently skipped: it means a setting was
    /// renamed or removed and the everyday list has quietly lost a row — exactly the kind of decay
    /// that is invisible until a player goes looking for something that used to be there.
    /// </summary>
    private static ConfigCatalog.ConfigItem? Lookup(string section, string key)
    {
        EnsureLookup();
        if (ByKey.TryGetValue(Id(section, key), out ConfigCatalog.ConfigItem item))
            return item;

        VRLog.Warn("WorldUI", $"VR options tab: curated entry [{section}] {key} no longer exists — "
                              + "the row is skipped. It was renamed or removed; the curated list in "
                              + "VROptionsTab.4.Curated.cs needs updating.");
        return null;
    }
}
