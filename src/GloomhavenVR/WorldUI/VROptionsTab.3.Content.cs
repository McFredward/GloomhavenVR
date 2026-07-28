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

    /// <summary>Category buttons, kept so the selected one can be marked without a full rebuild.</summary>
    private static readonly List<(Button button, ConfigCatalog.ConfigTopic topic)> CategoryButtons = new(16);

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
                              + $"{groups.Count} group(s), {rows} row(s).");
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

        CategoryButtons.Clear();
        for (int t = 0; t < ConfigCatalog.TopicCount; t++)
        {
            var topic = (ConfigCatalog.ConfigTopic)t;
            if (ConfigCatalog.Groups(topic).Count == 0)
                continue;

            BuildCategoryButton(bar, topic);
        }
    }

    private static void BuildCategoryButton(Transform parent, ConfigCatalog.ConfigTopic topic)
    {
        var go = new GameObject($"Cat.{topic}", typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);

        var image = go.AddComponent<Image>();
        image.sprite = NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle);
        image.color = NativeButtonSkin.ColorFor(NativeButtonSkin.FaceState.Idle);
        image.type = Image.Type.Sliced;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            _category = topic;
            TickGuard.Run("VROptionsTab.Switch", Rebuild, "WorldUI");
        });

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, worldPositionStays: false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = ConfigCatalog.TopicLabel(topic);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 14f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.color = NativeButtonSkin.LabelColor;
        NativeButtonSkin.ApplyFont(label);

        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 0f);
        labelRect.offsetMax = new Vector2(-4f, 0f);

        CategoryButtons.Add((button, topic));
    }

    /// <summary>Light the selected category the way the game lights a chosen option.</summary>
    private static void MarkSelectedCategory()
    {
        for (int i = 0; i < CategoryButtons.Count; i++)
        {
            (Button button, ConfigCatalog.ConfigTopic topic) = CategoryButtons[i];
            if (button == null || button.targetGraphic == null)
                continue;

            bool selected = topic == _category;
            button.targetGraphic.color = NativeButtonSkin.ColorFor(
                selected ? NativeButtonSkin.FaceState.Accent : NativeButtonSkin.FaceState.Idle);
        }
    }
}
