using System;
using System.Collections.Generic;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// What the tab actually shows: a category strip, and the rows of the chosen category.
///
/// <para>ONE CATEGORY AT A TIME, and that is a performance decision rather than a layout one. The
/// mod has 438 settings; building them all would mean 438 cloned rows, each a small sub-tree with
/// an Image, a TMP label and a frame, rebuilt every time the tab opens. Only the selected
/// category is ever instantiated, which keeps a build to a few dozen objects.</para>
///
/// <para>BUILT ON FIRST SHOW, not on injection. <see cref="ConfigCatalog"/> reads the live BepInEx
/// registry, and at injection time the later modules may not have bound their entries yet — a list
/// built then would be missing whatever registered after it. Hooking the window's own show event
/// also means a player who never opens the tab never pays for it.</para>
///
/// <para>THIS STAGE IS THE COMPLETE, UNCURATED VIEW: every topic the catalog knows, in its own
/// category. It is deliberately the first thing wired up, because it needs no editorial judgement
/// and therefore cannot lose a setting. The curated everyday categories are layered on top of this,
/// not instead of it — this view stays as "Erweitert".</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>What the content pane is showing.</summary>
    private enum View
    {
        /// <summary>One of the hand-picked everyday categories.</summary>
        Curated,

        /// <summary>The list of every catalog topic, as links.</summary>
        AdvancedIndex,

        /// <summary>One catalog topic in full.</summary>
        AdvancedTopic,
    }

    private static View _view = View.Curated;

    /// <summary>Selected everyday category; also the index of its sub-tab in the column.</summary>
    private static int _curated;

    /// <summary>Topic shown by <see cref="View.AdvancedTopic"/>.</summary>
    private static ConfigCatalog.ConfigTopic _category = ConfigCatalog.ConfigTopic.Movement;

    /// <summary>Sub-tab index that stands for "Erweitert" — always the last one in the column.</summary>
    private static int AdvancedTabIndex => Curated.Length;

    /// <summary>Sub-tab switches, kept so the lit one can be marked without a full rebuild.</summary>
    private static readonly List<(Toggle toggle, int index)> CategoryToggles = new(16);

    /// <summary>
    /// Which sub-tab is lit. Both advanced views light Debug — drilling into a topic is
    /// still being in the advanced section, and unlighting the column there would leave the player
    /// with no indication of where they are.
    /// </summary>
    private static int SelectedTabIndex =>
        _view == View.Curated ? _curated : AdvancedTabIndex;

    private static bool _showHooked;

    /// <summary>
    /// Subscribe to the cloned window's show event. <c>OnShow</c> is a plain <c>Action</c> property
    /// rather than an event, so it is CHAINED, never assigned: overwriting it would silently drop
    /// whatever the game put there.
    /// </summary>
    private static void HookContentBuild(UISubmenuGOWindow window)
    {
        if (_showHooked)
            return;
        _showHooked = true;

        Action? previous = window.OnShow;
        window.OnShow = () =>
        {
            previous?.Invoke();
            TickGuard.Run("VROptionsTab.Build", Rebuild, "WorldUI");
        };
    }

    /// <summary>Tear the list down and build the current category.</summary>
    private static void Rebuild()
    {
        if (ContentRoot == null)
            return;

        ConfigCatalog.EnsureFresh();
        ClearRows();
        HideForeignContent();

        BuildCategoryStrip();

        int rows = _view switch
        {
            View.AdvancedIndex => BuildAdvancedIndex(),
            View.AdvancedTopic => BuildTopic(_category),
            _ => BuildCurated(),
        };

        if (rows == 0)
            BuildHeader(ContentRoot, Loc.Mod("cfg_empty"));

        MarkSelectedCategory();
        VRLog.Info("WorldUI", $"VR options tab: built {_view} — {rows} row(s); "
                              + $"{CategoryToggles.Count} sub-tab(s) in the column.");
    }

    /// <summary>Reused per-section resolve buffers (no per-rebuild allocation).</summary>
    private static readonly List<ConfigCatalog.ConfigItem> _sectionItems = new(16);
    private static readonly List<CuratedEntry> _sectionEntries = new(16);

    /// <summary>One everyday category: its hand-picked entries, in the order the list declares.</summary>
    private static int BuildCurated()
    {
        if (_curated < 0 || _curated >= Curated.Length || ContentRoot == null)
            return 0;

        CuratedCategory category = Curated[_curated];
        int rows = 0;

        for (int s = 0; s < category.Sections.Length; s++)
        {
            CuratedSection section = category.Sections[s];

            // Resolve first, so the heading can name the variant these rows belong to and an
            // all-filtered section does not leave a heading over nothing.
            _sectionItems.Clear();
            _sectionEntries.Clear();
            for (int i = 0; i < section.Entries.Length; i++)
            {
                CuratedEntry entry = section.Entries[i];
                ConfigCatalog.ConfigItem? item = Lookup(entry.Section, entry.Key);
                if (item == null || !IsShownForCurrentVariant(item))
                    continue;
                _sectionItems.Add(item);
                _sectionEntries.Add(entry);
            }
            if (_sectionItems.Count == 0)
                continue;

            BuildHeader(ContentRoot, section.Label);
            string? note = VariantNote(_sectionItems);
            if (note != null)
                BuildNote(ContentRoot, note);
            BuildVariantCopyRows(_sectionItems);

            for (int i = 0; i < _sectionItems.Count; i++)
                rows += BuildItem(_sectionItems[i], _sectionEntries[i].Caption, _sectionEntries[i].HintKey);
        }
        _sectionItems.Clear();
        _sectionEntries.Clear();
        return rows;
    }

    /// <summary>
    /// The advanced landing page: one link per topic rather than every setting at once. 440 rows
    /// would be several seconds of cloning and a list nobody can navigate; a topic at a time is
    /// both fast and findable.
    /// </summary>
    private static int BuildAdvancedIndex()
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Loc.Mod("cat_debug"));

        int rows = 0;
        for (int t = 0; t < ConfigCatalog.TopicCount; t++)
        {
            var topic = (ConfigCatalog.ConfigTopic)t;
            if (ConfigCatalog.Groups(topic).Count == 0)
                continue;

            BuildLinkRow(ContentRoot, ConfigCatalog.TopicLabel(topic), () =>
            {
                _category = topic;
                _view = View.AdvancedTopic;
                TickGuard.Run("VROptionsTab.Topic", Rebuild, "WorldUI");
            });
            rows++;
        }
        return rows;
    }

    /// <summary>
    /// Under a per-variant block: one row per OTHER variant, "take these settings from X".
    ///
    /// <para>Tuning a second hand style or a second board otherwise starts from the shipped
    /// defaults every time, even though what you want is almost always "the same as the one I
    /// already got right, then nudged". One row per source rather than a picker plus an apply
    /// button: with three hand styles that is two rows, it needs no extra state, and the row says
    /// exactly what pressing it does.</para>
    ///
    /// <para>Placed directly under the "which variant am I editing" note, so the two lines read
    /// together: this is the block you are editing, and here is where its values can come from.</para>
    /// </summary>
    private static void BuildVariantCopyRows(
        System.Collections.Generic.IReadOnlyList<ConfigCatalog.ConfigItem> items)
    {
        if (ContentRoot == null)
            return;
        var sources = VariantSources(items);
        if (sources == null)
            return;

        VariantFamily family = sources.Value.Family;
        string[] others = sources.Value.Others;
        // Snapshot: the callback runs after this build pass, and the pane's own lists get reused.
        var snapshot = new System.Collections.Generic.List<ConfigCatalog.ConfigItem>(items);
        for (int i = 0; i < others.Length; i++)
        {
            string from = others[i];
            BuildLinkRow(ContentRoot, Loc.Mod("vr_var_copy").Replace("{0}", family.Display(from)), () =>
            {
                CopyVariant(family, from, snapshot);
                TickGuard.Run("VROptionsTab.VariantCopy", Rebuild, "WorldUI");
            });
        }
    }

    /// <summary>One catalog topic in full, with the way back out at the top.</summary>
    private static int BuildTopic(ConfigCatalog.ConfigTopic topic)
    {
        if (ContentRoot == null)
            return 0;

        BuildLinkRow(ContentRoot, "‹ " + Loc.Mod("cat_debug"), () =>
        {
            _view = View.AdvancedIndex;
            TickGuard.Run("VROptionsTab.Back", Rebuild, "WorldUI");
        });

        // NAME THE OPEN TOPIC. The column keeps Debug lit, which says which SECTION you are in but
        // not which page — every topic looked identically headed "Debug" and there was no way to
        // tell from the screen what had just been opened.
        BuildHeader(ContentRoot, ConfigCatalog.TopicLabel(topic));

        IReadOnlyList<ConfigCatalog.ConfigGroup> groups = ConfigCatalog.Groups(topic);
        int rows = 0;

        for (int g = 0; g < groups.Count; g++)
        {
            ConfigCatalog.ConfigGroup group = groups[g];
            if (group.Items.Count == 0)
                continue;

            // COUNT BEFORE HEADING. Filtering per-variant entries down to the selected board or hand
            // style can empty a group completely, and a heading over nothing reads as a setting that
            // failed to load.
            int visible = 0;
            for (int i = 0; i < group.Items.Count; i++)
            {
                if (IsShownForCurrentVariant(group.Items[i]))
                    visible++;
            }
            if (visible == 0)
                continue;

            // A line under the heading says WHICH variant these rows are, because the rows
            // themselves no longer say so.
            BuildHeader(ContentRoot, group.Label);
            string? note = VariantNote(group.Items);
            if (note != null)
                BuildNote(ContentRoot, note);
            BuildVariantCopyRows(group.Items);

            for (int i = 0; i < group.Items.Count; i++)
            {
                ConfigCatalog.ConfigItem entry = group.Items[i];
                if (!IsShownForCurrentVariant(entry))
                    continue;
                rows += BuildItem(entry, VariantFreeCaption(entry));
            }
        }
        return rows;
    }

    /// <summary>
    /// One entry, as one row — except a vector or a colour, which is edited one component at a
    /// time, the same shape the catalog's own Step() takes.
    /// </summary>
    private static int BuildItem(ConfigCatalog.ConfigItem item, string? caption = null, string? hintKey = null)
    {
        if (ContentRoot == null)
            return 0;

        // A hand-made control stands for the WHOLE entry — a colour behind a named-preset dropdown
        // must not also produce four numeric component rows beside it.
        if (HasSpecialRow(item))
        {
            BuildRow(ContentRoot, item, 0, caption, hintKey);
            return 1;
        }

        int components = item.Kind == ConfigCatalog.ConfigKind.Bool
            ? 1
            : Mathf.Max(1, item.Components);

        for (int c = 0; c < components; c++)
            BuildRow(ContentRoot, item, c, caption, hintKey);

        return components;
    }

    /// <summary>
    /// The sub-tab chooser, in the strip pinned above the scroll area. Built ONCE and then left
    /// alone: it does not depend on which category is showing, and rebuilding it per switch would
    /// destroy the very button the player just pressed. Only topics that actually hold something
    /// get one — an empty sub-tab is a dead end the player has to discover by pressing it.
    /// </summary>
    private static void BuildCategoryStrip()
    {
        RectTransform? bar = TabBarRoot;
        if (bar == null || bar.childCount > 0)
            return;

        CategoryToggles.Clear();
        for (int i = 0; i < Curated.Length; i++)
            BuildCategoryButton(bar, i, Curated[i].Label);

        // Debug last, and set apart by being last: the everyday tabs read as the menu, and the
        // tuning constants are one deliberate step further in.
        BuildCategoryButton(bar, AdvancedTabIndex, Loc.Mod("cat_debug"));
    }

    /// <summary>
    /// One sub-tab, cloned from the window's own tab option so it carries the game's frame, font
    /// and highlight. Its switch drives the category; the toggle group makes exactly one lit.
    /// </summary>
    private static void BuildCategoryButton(Transform parent, int index, string caption)
    {
        if (_categoryTemplate == null)
            return;

        GameObject go = UnityEngine.Object.Instantiate(_categoryTemplate, parent);
        go.name = $"Cat.{index}";
        go.SetActive(true);

        // The same resting-state assertion the Debug links get. It was applied there and NOT here,
        // which is why the sub-tabs kept the fault after the links were fixed: the window calls
        // SetFocused on its own tabs when it opens, a clone nobody calls it on starts faint, and the
        // first hover ran the game's highlight and never handed the row back.
        RestVisuals(go);

        TMP_Text? label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = caption;
            label.enableWordWrapping = false;

            // SHRINK RATHER THAN TRUNCATE. The donor caption is sized for a one-word tab
            // ("Grafik", "Ton"); the mod's topics are two and three words, and ellipsis turned
            // them into "Messung...", "Bild&D...", "Bewegun..." — captions that no longer say
            // which category they are, which is the entire job of this column.
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = 9f;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        Toggle? toggle = go.GetComponentInChildren<Toggle>(true);
        if (toggle == null)
        {
            UnityEngine.Object.Destroy(go);
            return;
        }

        toggle.onValueChanged.RemoveAllListeners();
        toggle.group = CategoryGroup(parent);
        toggle.SetIsOnWithoutNotify(index == SelectedTabIndex);
        toggle.onValueChanged.AddListener(on =>
        {
            if (!on || index == SelectedTabIndex)
                return;

            if (index == AdvancedTabIndex)
            {
                _view = View.AdvancedIndex;
            }
            else
            {
                _view = View.Curated;
                _curated = index;
            }

            TickGuard.Run("VROptionsTab.Switch", Rebuild, "WorldUI");
        });

        CategoryToggles.Add((toggle, index));
    }

    /// <summary>
    /// One group for the column, created on it rather than reused from the window's own: sharing
    /// the game's group would make choosing a sub-tab deselect the VR tab itself.
    /// </summary>
    private static ToggleGroup CategoryGroup(Transform column)
    {
        ToggleGroup? group = column.GetComponent<ToggleGroup>();
        if (group == null)
        {
            group = column.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
        }
        return group;
    }

    /// <summary>Keep the lit sub-tab in step with the category actually being shown.</summary>
    private static void MarkSelectedCategory()
    {
        for (int i = 0; i < CategoryToggles.Count; i++)
        {
            (Toggle toggle, int index) = CategoryToggles[i];
            if (toggle == null)
                continue;
            toggle.SetIsOnWithoutNotify(index == SelectedTabIndex);
        }
    }

    /// <summary>
    /// Hide anything in the scroll content that is not ours.
    ///
    /// <para>The donor tab can REBUILD ITS OWN ROWS after we deactivated them — that is how the
    /// game's keybindings ("Aktion bestätigen", "Eine Karte verbrennen") ended up sitting above the
    /// mod's settings in the same list. Choosing a donor the game does not maintain is the real
    /// fix; this runs every time the tab is shown so that if any donor ever repopulates, it is
    /// hidden again rather than shipped.</para>
    /// </summary>
    private static void HideForeignContent()
    {
        RectTransform? content = ContentRoot;
        if (content == null || content.parent == null)
            return;

        Transform holder = content.parent;
        int hidden = 0;

        for (int i = 0; i < holder.childCount; i++)
        {
            Transform child = holder.GetChild(i);
            if (ReferenceEquals(child, content) || !child.gameObject.activeSelf)
                continue;
            child.gameObject.SetActive(false);
            hidden++;
        }

        if (hidden > 0)
            VRLog.Info("WorldUI", $"VR options tab: hid {hidden} row block(s) the donor tab rebuilt "
                                  + "into the shared content — the game's own rows, not the mod's.");
    }
}
