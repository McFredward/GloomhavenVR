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
    /// <summary>Topic currently displayed. Survives a rebuild so reopening returns where you were.</summary>
    private static ConfigCatalog.ConfigTopic _category = ConfigCatalog.ConfigTopic.Movement;

    /// <summary>Sub-tab switches, kept so the lit one can be marked without a full rebuild.</summary>
    private static readonly List<(Toggle toggle, ConfigCatalog.ConfigTopic topic)> CategoryToggles = new(16);

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

        IReadOnlyList<ConfigCatalog.ConfigGroup> groups = ConfigCatalog.Groups(_category);
        int rows = 0;

        for (int g = 0; g < groups.Count; g++)
        {
            ConfigCatalog.ConfigGroup group = groups[g];
            if (group.Items.Count == 0)
                continue;

            BuildHeader(ContentRoot, group.Label);

            for (int i = 0; i < group.Items.Count; i++)
            {
                ConfigCatalog.ConfigItem item = group.Items[i];

                // A vector or a colour is edited one component at a time — the same shape the
                // catalog's own Step() takes, so nothing here has to know what the components mean.
                // Everything else is a single row; BuildRow picks the control shape from the entry.
                int components = item.Kind == ConfigCatalog.ConfigKind.Bool
                    ? 1
                    : Mathf.Max(1, item.Components);

                for (int c = 0; c < components; c++)
                {
                    BuildRow(ContentRoot, item, c);
                    rows++;
                }
            }
        }

        if (rows == 0)
            BuildHeader(ContentRoot, Loc.Mod("cfg_empty"));

        MarkSelectedCategory();
        VRLog.Info("WorldUI", $"VR options tab: built '{ConfigCatalog.TopicLabel(_category)}' — "
                              + $"{groups.Count} group(s), {rows} row(s); "
                              + $"{CategoryToggles.Count} sub-tab(s) in the column.");
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
        for (int t = 0; t < ConfigCatalog.TopicCount; t++)
        {
            var topic = (ConfigCatalog.ConfigTopic)t;
            if (ConfigCatalog.Groups(topic).Count == 0)
                continue;

            BuildCategoryButton(bar, topic);
        }
    }

    /// <summary>
    /// One sub-tab, cloned from the window's own tab option so it carries the game's frame, font
    /// and highlight. Its switch drives the category; the toggle group makes exactly one lit.
    /// </summary>
    private static void BuildCategoryButton(Transform parent, ConfigCatalog.ConfigTopic topic)
    {
        if (_categoryTemplate == null)
            return;

        GameObject go = UnityEngine.Object.Instantiate(_categoryTemplate, parent);
        go.name = $"Cat.{topic}";
        go.SetActive(true);

        TMP_Text? label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = ConfigCatalog.TopicLabel(topic);
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
        toggle.SetIsOnWithoutNotify(topic == _category);
        toggle.onValueChanged.AddListener(on =>
        {
            if (!on || _category == topic)
                return;
            _category = topic;
            TickGuard.Run("VROptionsTab.Switch", Rebuild, "WorldUI");
        });

        CategoryToggles.Add((toggle, topic));
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
            (Toggle toggle, ConfigCatalog.ConfigTopic topic) = CategoryToggles[i];
            if (toggle == null)
                continue;
            toggle.SetIsOnWithoutNotify(topic == _category);
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
