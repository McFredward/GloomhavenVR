using System.Collections.Generic;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Debug > Steuerbrett (pro Brett): the ONE hand-arranged topic page.
///
/// <para>ROOT CAUSE OF ITS EXISTENCE (user report 2026-08): "Im Debug-Menu 'pro Board' bitte
/// überarbeite die Überschriften und Gruppen nochmal. [...] Aktuell sucht man dort immer rum ohne
/// wirklich zu finden wonach man sucht." The automatic grouping in <see cref="ConfigCatalog"/>
/// splits an oversized section by each key's LEADING WORD -- which for the ~42 per-board keys
/// produced eighteen clusters ("Confirm", "Generic", "Pin", "Readout", "Hover", ...), most of them
/// under the fold threshold, so <c>FoldSmallGroups</c> swept them into one "Allgemein" grab-bag.
/// A player looking for the Best./Zurück buttons had to know the setting's internal first word to
/// guess its cluster; the same report's other half ("hat keinen Effekt") started there.</para>
///
/// <para>WHY AN EXPLICIT TREE AND NOT A BETTER RULE -- the same argument
/// <c>VROptionsTab.4.Curated.cs</c> already makes: every mechanical shortcut fails on real
/// entries. No key prefix distinguishes "a button" (<c>PinOffset</c>, <c>ClusterScale</c>) from
/// "a readout" (<c>ReadoutOffset</c>) from "the board itself" (<c>BoardScale</c>); only a person
/// looking at the control board can say what belongs together ON it. So the tree is written down,
/// two levels deep -- a TOP heading for what the player is trying to adjust (Tasten, Brett, Karten,
/// Stapel, Anzeigen) and SUB-headings inside the large ones -- and each heading carries a short
/// localized hover hint saying what on the control board the rows beneath it move
/// (<c>h_</c>-prefixed sibling of its label key, resolved by <see cref="VROptionsTab"/>'s
/// BuildHeader).</para>
///
/// <para>NOTHING CAN BE LOST. Resolution marks every catalog item it places; whatever remains in
/// the topic afterwards -- typically a per-board key added after this file was written -- is
/// rendered under a final "Allgemein" heading rather than dropped. A curation mistake here costs
/// one wrong heading, never access to a setting.</para>
///
/// <para>THE TREE ALSO HOLDS NON-PER-BOARD ENTRIES. The [BoardButtons] and [RestButtons]
/// keycap-geometry quads live here (routed into this topic by <c>ConfigCatalog.TopicOf</c>),
/// directly under the same sub-heading as their per-board siblings -- the report's first half was
/// exactly that "Best./Zurück: Breite/Höhe/Tiefe/Hub" lived in one topic while "Best./Zurück:
/// Position/Abstand/Form" lived in another.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>
    /// One row of the tree: a config entry named by section + key. A per-board reference names the
    /// BASE key ("ConfirmUndoOffset") and resolves to every board's sibling
    /// ("ConfirmUndoOffset_Oak/_Steel/_Bronze") -- all of them are claimed from the catch-all, the
    /// pane's variant filter then shows only the selected board's.
    /// </summary>
    private readonly struct BoardRef
    {
        internal readonly string Section;
        internal readonly string Key;
        internal readonly bool PerBoard;

        internal BoardRef(string section, string key, bool perBoard = true)
        {
            Section = section;
            Key = key;
            PerBoard = perBoard;
        }
    }

    /// <summary>A sub-heading and its rows. A null <see cref="LocKey"/> is an unlabeled block --
    /// used by a top heading small enough to need no second level.</summary>
    private sealed class BoardSub
    {
        internal string? LocKey;
        internal BoardRef[] Entries = System.Array.Empty<BoardRef>();
    }

    /// <summary>A top heading of the control-board page.</summary>
    private sealed class BoardTop
    {
        internal string LocKey = string.Empty;
        internal BoardSub[] Subs = System.Array.Empty<BoardSub>();
    }

    /// <summary>
    /// The page, in reading order: buttons first (the elements a player touches and therefore
    /// tunes most), then the board itself, then what lies and shows on it. Labels and hover hints
    /// live in <c>Loc.cs</c> under the same keys ("vr_bg_*" / "h_vr_bg_*").
    /// </summary>
    private static readonly BoardTop[] BoardTree =
    {
        new()
        {
            LocKey = "vr_bg_buttons",
            Subs = new BoardSub[]
            {
                new()
                {
                    // EVERY generic-keycap dial in one block (the report's literal ask). It covers
                    // all THREE caps now — Confirm, Undo and the turn-flow SKIP, which joined this
                    // cluster on 2026-08-25 — and two of its rows stopped being per-board on the same
                    // day: the seat NUDGE and the stack SPACING are single shared entries, because
                    // the per-board part of a keycap seat is the recess the board itself cut (see
                    // CardsConfig's seat-family note). Only the SHAPE is still per board, and that is
                    // taste rather than geometry: no anchor can say whether this player wants round
                    // pucks on their bronze board.
                    LocKey = "vr_bg_cu",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "ConfirmUndoOffset", perBoard: false),
                        new BoardRef("Cards", "ButtonStackSpacing", perBoard: false),
                        new BoardRef("Cards", "GenericButtonShape"),
                        new BoardRef("BoardButtons", "Width", perBoard: false),
                        new BoardRef("BoardButtons", "Height", perBoard: false),
                        new BoardRef("BoardButtons", "Depth", perBoard: false),
                        new BoardRef("BoardButtons", "Travel", perBoard: false),
                    },
                },
                new()
                {
                    // Same shape as Best./Zurück on purpose: per-board seat + shared geometry.
                    LocKey = "vr_bg_rest",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "RestButtonOffset", perBoard: false),
                        new BoardRef("Cards", "RestButtonDiameter", perBoard: false),
                        new BoardRef("Cards", "RestStackSpacing", perBoard: false),
                        new BoardRef("Cards", "RestButtonShape"),
                        new BoardRef("RestButtons", "Width", perBoard: false),
                        new BoardRef("RestButtons", "Height", perBoard: false),
                        new BoardRef("RestButtons", "Depth", perBoard: false),
                        new BoardRef("RestButtons", "Travel", perBoard: false),
                    },
                },
                new()
                {
                    // THE SKIP CAP'S OWN BLOCK IS GONE (2026-08-25), and its rows with it. It held
                    // [Cards] ClusterOffset/ClusterScale plus the nine [RoundButtons] entries — the
                    // seat, size, shape and travel of a cap that had a geometry family of its own
                    // because it was drawn by a separate group. The user retired that group ("Ich
                    // möchte daher, dass die Button-Gruppe der 'Überspringen Buttons' komplett
                    // verschwindet ... so dass all diese buttons gleich aussehen"), so the skip's
                    // dials ARE the Best./Zurück block's dials above: same seat nudge, same stack
                    // spacing, same [BoardButtons] size, same shape. The ModBuild-96 report this
                    // block answered ("die Einstellungen sollen unter der Tasten-Kategorie genauso
                    // einstellbar sein") is satisfied more completely than it was: the cap is
                    // reachable in the same place as its siblings because it IS one of them.
                    //
                    // What is left here is the one control that never belonged to the cluster.
                    LocKey = "vr_bg_cluster",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "PinOffset"),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_bg_board",
            Subs = new BoardSub[]
            {
                new()
                {
                    LocKey = "vr_bg_pose",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "BoardTilt"),
                        new BoardRef("Cards", "BoardPitchMin"),
                        new BoardRef("Cards", "BoardPitchMax"),
                        new BoardRef("Cards", "BoardYaw"),
                        new BoardRef("Cards", "BoardScale"),
                        new BoardRef("Cards", "BoardPosOffset"),
                    },
                },
                new()
                {
                    LocKey = "vr_bg_mesh",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "AssetOffset"),
                        new BoardRef("Cards", "AssetPitchDegrees"),
                        new BoardRef("Cards", "AssetYawDegrees"),
                        new BoardRef("Cards", "AssetRollDegrees"),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_bg_cards",
            Subs = new BoardSub[]
            {
                new()
                {
                    LocKey = "vr_bg_active",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "ActiveOffset"),
                        new BoardRef("Cards", "ActiveCardScale"),
                        new BoardRef("Cards", "ActiveGridSpacing"),
                    },
                },
                new()
                {
                    LocKey = "vr_bg_slots",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "SlotOverlayOffset"),
                        new BoardRef("Cards", "SlotOverlaySpacing"),
                        new BoardRef("Cards", "SlotOverlayScale"),
                        new BoardRef("Cards", "ItemUseSlotOffset"),
                        new BoardRef("Cards", "ItemCardOffset"),
                    },
                },
            },
        },
        new()
        {
            // Small enough for one level: three dials need no sub-heading under themselves.
            LocKey = "vr_bg_piles",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Cards", "PileOffset"),
                        new BoardRef("Cards", "PileScale"),
                        new BoardRef("Cards", "PileSpacing"),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_bg_readouts",
            Subs = new BoardSub[]
            {
                new()
                {
                    LocKey = "vr_bg_placards",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "InitiativeOffset"),
                        new BoardRef("Cards", "PickBannerOffset"),
                        new BoardRef("Cards", "HoverHintOffset"),
                        new BoardRef("Cards", "ReadoutOffset"),
                    },
                },
                new()
                {
                    LocKey = "vr_bg_docks",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "ObjectivesOffset"),
                        new BoardRef("Cards", "ObjectivesScale"),
                        new BoardRef("Cards", "ObjectivesWidth"),
                        new BoardRef("Cards", "ElementsOffset"),
                        new BoardRef("Cards", "ElementsScale"),
                    },
                },
                new()
                {
                    LocKey = "vr_bg_decision",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "DecisionOffset"),
                        new BoardRef("Cards", "DecisionScale"),
                        new BoardRef("Cards", "DecisionGap"),
                    },
                },
            },
        },
    };

    /// <summary>Resolve one tree row to its catalog items (all boards for a per-board key), and
    /// CLAIM every one of them so the catch-all cannot show it a second time.</summary>
    private static void ResolveBoardRef(BoardRef re, List<ConfigCatalog.ConfigItem> into,
                                        HashSet<ConfigCatalog.ConfigItem> placed)
    {
        if (!re.PerBoard)
        {
            ConfigCatalog.ConfigItem? item = Lookup(re.Section, re.Key);
            if (item != null && placed.Add(item))
                into.Add(item);
            return;
        }

        // Board names from the enum, like the variant machinery -- a board added later resolves
        // here by existing, with no edit to this file.
        string[] boards = System.Enum.GetNames(typeof(Cards.ControlBoard));
        for (int i = 0; i < boards.Length; i++)
        {
            ConfigCatalog.ConfigItem? item = Lookup(re.Section, re.Key + "_" + boards[i]);
            if (item != null && placed.Add(item))
                into.Add(item);
        }
    }

    /// <summary>
    /// The control-board page's body: the hand-arranged two-level heading tree, then the safety
    /// net. Called by <c>BuildTopic</c> in place of the automatic grouping; the back link and the
    /// topic header are already built by the caller.
    /// </summary>
    private static int BuildBoardTopicBody()
    {
        if (ContentRoot == null)
            return 0;

        IReadOnlyList<ConfigCatalog.ConfigGroup> groups =
            ConfigCatalog.Groups(ConfigCatalog.ConfigTopic.BoardGeometry);
        var all = new List<ConfigCatalog.ConfigItem>(160);
        for (int g = 0; g < groups.Count; g++)
            all.AddRange(groups[g].Items);

        // WHICH BOARD, ONCE, AT THE TOP -- with the whole-page copy rows under it. The automatic
        // page repeated the note and the two copy rows under every one of its ~12 groups (the
        // copy already walked that group only); here the copy covers every per-board row on the
        // page, which is what "start this board from the one I already tuned" actually means.
        string? note = VariantNote(all);
        if (note != null)
            BuildNote(ContentRoot, note);
        BuildVariantCopyRows(all);

        var placed = new HashSet<ConfigCatalog.ConfigItem>();
        var resolved = new List<ConfigCatalog.ConfigItem>(24);
        int rows = 0;

        for (int t = 0; t < BoardTree.Length; t++)
        {
            BoardTop top = BoardTree[t];

            // Resolve the WHOLE top first: a heading over nothing reads as a setting that failed
            // to load (same rule the automatic page follows), and every resolved item -- visible
            // for this board or not -- is claimed away from the catch-all.
            var subItems = new List<ConfigCatalog.ConfigItem>[top.Subs.Length];
            int visibleInTop = 0;
            for (int s = 0; s < top.Subs.Length; s++)
            {
                resolved.Clear();
                BoardRef[] entries = top.Subs[s].Entries;
                for (int e = 0; e < entries.Length; e++)
                    ResolveBoardRef(entries[e], resolved, placed);

                subItems[s] = new List<ConfigCatalog.ConfigItem>(resolved.Count);
                for (int i = 0; i < resolved.Count; i++)
                {
                    if (!IsRowVisible(resolved[i]))
                        continue;
                    subItems[s].Add(resolved[i]);
                    visibleInTop++;
                }
            }

            if (visibleInTop == 0)
                continue;

            BuildHeader(ContentRoot, Loc.Mod(top.LocKey), "h_" + top.LocKey);
            for (int s = 0; s < top.Subs.Length; s++)
            {
                List<ConfigCatalog.ConfigItem> items = subItems[s];
                if (items.Count == 0)
                    continue;
                string? locKey = top.Subs[s].LocKey;
                if (locKey != null)
                    BuildHeader(ContentRoot, Loc.Mod(locKey), "h_" + locKey, sub: true);
                for (int i = 0; i < items.Count; i++)
                    rows += BuildItem(items[i], VariantFreeCaption(items[i]));
            }
        }

        // THE SAFETY NET: everything the tree did not claim -- typically a per-board key bound
        // after this file was arranged -- still renders, under "Allgemein", instead of silently
        // vanishing. This is the invariant that makes an explicit list safe (the same promise
        // the curated tab's doc makes): a curation gap costs one heading, never a setting.
        bool miscHeaded = false;
        for (int i = 0; i < all.Count; i++)
        {
            ConfigCatalog.ConfigItem item = all[i];
            if (placed.Contains(item) || !IsRowVisible(item))
                continue;
            if (!miscHeaded)
            {
                miscHeaded = true;
                BuildHeader(ContentRoot, Loc.Mod("cfg_group_misc"), "h_vr_bg_misc");
            }
            rows += BuildItem(item, VariantFreeCaption(item));
        }

        return rows;
    }
}
