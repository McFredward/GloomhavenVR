using System;
using System.Collections.Generic;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using Script.GUI.IngameMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The row kit: native-looking setting rows, made by CLONING one of the game's own and refilling
/// it, so the result is the game's art rather than an imitation of it.
///
/// <para>THE SKELETON, as the probe found it — every option row in the game shares it:</para>
/// <code>
/// &lt;Row&gt;              LayoutElementExtended, UINavigationSelectable
///   Background        Image
///   Title             TextMeshProUGUI, TextLocalizedListener   &lt;- the caption
///   Option            (holder)
///     &lt;control&gt;       ExtendedToggle / Slider / ExtendedDropdown
///   MenuElementFrame  FrameView, Image, CanvasGroup            &lt;- native hover/selection frame
/// </code>
///
/// <para>ONE TEMPLATE, TWO FILLINGS. The toggle row is used as the universal skeleton because it is
/// the only archetype that carries NO binder component of its own (the probe: 'Crossplay' is just
/// LayoutElementExtended + UINavigationSelectable) — there is nothing on it that has to be
/// understood before it is safe to reuse. Booleans keep its <c>ExtendedToggle</c>; everything else
/// has the toggle removed and a <c>◀ value ▶</c> control built into the same Option holder.</para>
///
/// <para>WHY ARROWS AND NOT SLIDERS OR DROPDOWNS for the non-boolean settings. Both native
/// alternatives need something the mod's settings do not all have: a dropdown needs a closed set of
/// options, a slider needs a min and a max. <see cref="ConfigCatalog"/> covers enums, bounded
/// numbers, unbounded numbers, strings with a value list, and multi-component vectors and colours,
/// and its <c>Cycle</c>/<c>Step</c> pair already edits every one of them correctly — the same pair
/// the mod's old panel used. Arrows are the one control shape that fits all of them, so there is a
/// single code path and no setting is left uneditable.</para>
///
/// <para>WHAT IS STRIPPED FROM A CLONE, and why each would fail silently:
/// <list type="bullet">
/// <item><description><c>TextLocalizedListener</c> — rewrites the caption from the game's string
/// table on the next language change, replacing ours with the donor's.</description></item>
/// <item><description><c>UINavigationSelectable</c> / <c>NavigationSelectableWrapper</c> — register
/// with the tab's gamepad navigation root. The mod forces mouse mode (<see cref="InputModeGuard"/>)
/// and drives these rows with the laser and the fingertip, so the registrations are dead weight
/// that would still be walked when the game builds a navigation order.</description></item>
/// <item><description>Every listener on the toggle — the donor's binding to the game's own setting
/// would still fire alongside ours.</description></item>
/// </list></para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>Inactive clone of a real option row, kept as the prefab every row is stamped from.</summary>
    private static GameObject? _rowTemplate;

    /// <summary>Rows built for the current content, in order — cleared and rebuilt on every refresh.</summary>
    private static readonly List<GameObject> Rows = new(64);

    /// <summary>Live value labels, refreshed together so one write cannot drift from another.</summary>
    private static readonly List<(TMP_Text label, Func<string> read)> ValueLabels = new(64);

    private const float RowHeight = 46f;
    private const float HeaderHeight = 38f;

    /// <summary>
    /// Stamp the template from a live row. Called once per injection; a failure here is what turns
    /// the tab into a plain (still working) mod-drawn list rather than a native-looking one.
    /// </summary>
    private static void CaptureRowTemplate(UIOptionsWindow host)
    {
        if (_rowTemplate != null)
            return;

        Transform? donor = null;
        for (int i = 0; i < host.m_Tabs.Count && donor == null; i++)
        {
            UISubmenuGOWindow? window = host.m_Tabs[i]?.TabWindow;
            if (window != null)
                donor = FindRowWith<Toggle>(window.transform);
        }

        if (donor == null)
        {
            VRLog.Warn("WorldUI", "VR options tab: no toggle row to use as a template — rows will be "
                                  + "plain. Every setting stays editable; only the look is affected.");
            return;
        }

        _rowTemplate = UnityEngine.Object.Instantiate(donor.gameObject);
        _rowTemplate.name = "GloomhavenVR.RowTemplate";
        _rowTemplate.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(_rowTemplate);
        StripForReuse(_rowTemplate);

        VRLog.Info("WorldUI", $"VR options tab: row template stamped from '{donor.name}' "
                              + $"(under '{(donor.parent == null ? "?" : donor.parent.name)}').");
    }

    /// <summary>
    /// Components on a cloned row that belong to the GAME's binding of that row and must not come
    /// along. Matched by type NAME rather than by type: the navigation components live in an
    /// assembly the mod does not reference (they appear in the decompiled sources only as field
    /// types), so there is no symbol to compare against. A rename in a game update makes this miss
    /// rather than crash, which is the right way round — a surviving navigation registration is
    /// cosmetic, a compile-time dependency on an unreferenced assembly is not.
    /// </summary>
    private static readonly string[] BinderTypeNames =
    {
        "TextLocalizedListener",        // rewrites our caption on the next language change
        "UINavigationSelectable",       // registers the row with the tab's gamepad navigation root
        "NavigationSelectableWrapper",  // ditto, one level up
    };

    /// <summary>
    /// Remove what would keep driving a clone from the game's side. Each destroy is guarded because
    /// removal order can trip <c>RequireComponent</c> on types we do not control.
    /// </summary>
    private static void StripForReuse(GameObject root)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;

            string name = component.GetType().Name;
            for (int i = 0; i < BinderTypeNames.Length; i++)
            {
                if (string.Equals(name, BinderTypeNames[i], StringComparison.Ordinal))
                {
                    SafeDestroy(component);
                    break;
                }
            }
        }

        foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true))
        {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.group = null;
            toggle.isOn = false;
        }
    }

    private static void SafeDestroy(Component? component)
    {
        if (component == null)
            return;
        try
        {
            UnityEngine.Object.Destroy(component);
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: could not strip {component.GetType().Name} "
                                  + $"({e.Message}) — the row may still be driven by the game.");
        }
    }

    // ==========================================================================================
    //  Row builders
    // ==========================================================================================

    /// <summary>A section caption: the same row with its control and its hover frame taken out.</summary>
    private static void BuildHeader(Transform parent, string caption)
    {
        GameObject row = StampRow(parent, HeaderHeight, out TMP_Text? title, out Transform? option);
        if (title != null)
        {
            title.text = caption;
            title.fontStyle |= FontStyles.SmallCaps;
        }

        if (option != null)
            option.gameObject.SetActive(false);

        Transform? frame = row.transform.Find("MenuElementFrame");
        if (frame != null)
            frame.gameObject.SetActive(false);
    }

    /// <summary>A boolean: the donor's own switch, rebound to the catalog entry.</summary>
    private static void BuildBoolRow(Transform parent, ConfigCatalog.ConfigItem item)
    {
        GameObject row = StampRow(parent, RowHeight, out TMP_Text? title, out Transform? option);
        if (title != null)
            title.text = Caption(item);

        Toggle? toggle = option == null ? null : option.GetComponentInChildren<Toggle>(true);
        if (toggle == null)
        {
            // No switch to rebind (unusual template) — fall back to the arrow control, which can
            // express a boolean perfectly well. Better a different-looking row than a dead one.
            BuildValueControl(row, option, item, component: 0);
            return;
        }

        toggle.onValueChanged.RemoveAllListeners();
        toggle.isOn = item.Entry.BoxedValue is bool b && b;
        toggle.onValueChanged.AddListener(_ => ConfigCatalog.ToggleBool(item));
        AttachTooltip(row, item);
    }

    /// <summary>Everything that is not a boolean: <c>◀ value ▶</c> over the catalog's own editor.</summary>
    private static void BuildValueRow(Transform parent, ConfigCatalog.ConfigItem item, int component)
    {
        GameObject row = StampRow(parent, RowHeight, out TMP_Text? title, out Transform? option);
        if (title != null)
            title.text = item.Components > 1
                ? $"{Caption(item)} · {ConfigCatalog.ComponentLabel(item, component)}"
                : Caption(item);

        BuildValueControl(row, option, item, component);
    }

    /// <summary>
    /// The arrow control, built into the donor's own Option holder so it inherits the row's
    /// alignment. The holder is emptied first — its switch belongs to the boolean case.
    /// </summary>
    private static void BuildValueControl(GameObject row, Transform? option,
                                          ConfigCatalog.ConfigItem item, int component)
    {
        if (option == null)
            return;

        option.gameObject.SetActive(true);
        for (int i = option.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(option.GetChild(i).gameObject);

        var strip = (RectTransform)new GameObject("Value", typeof(RectTransform)).transform;
        strip.SetParent(option, worldPositionStays: false);
        strip.anchorMin = Vector2.zero;
        strip.anchorMax = Vector2.one;
        strip.offsetMin = Vector2.zero;
        strip.offsetMax = Vector2.zero;

        var layout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TMP_Text? value = null;
        BuildArrow(strip, "◀", () => Edit(item, component, -1));
        value = BuildValueLabel(strip);
        BuildArrow(strip, "▶", () => Edit(item, component, +1));

        if (value != null)
        {
            value.text = ConfigCatalog.ValueText(item, component);
            ValueLabels.Add((value, () => ConfigCatalog.ValueText(item, component)));
        }

        AttachTooltip(row, item);
    }

    /// <summary>
    /// One edit, through the catalog's own editors — <c>Cycle</c> for a closed set of options,
    /// <c>Step</c> for anything numeric. Nothing here knows what a setting means; that is exactly
    /// why every setting can be edited by the same two lines.
    /// </summary>
    private static void Edit(ConfigCatalog.ConfigItem item, int component, int direction)
    {
        try
        {
            if (item.Kind == ConfigCatalog.ConfigKind.Choice)
                ConfigCatalog.Cycle(item, direction);
            else if (item.Kind == ConfigCatalog.ConfigKind.Bool)
                ConfigCatalog.ToggleBool(item);
            else
                ConfigCatalog.Step(item, component, direction, multiplier: 1d);

            RefreshValues();
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: editing {item.Section}/{item.Key} threw ({e.Message}).");
        }
    }

    /// <summary>
    /// Repaint every visible value at once. One setting can move another (the catalog clamps against
    /// live ranges), so refreshing only the row that was clicked would leave the rest quietly stale.
    /// </summary>
    private static void RefreshValues()
    {
        for (int i = 0; i < ValueLabels.Count; i++)
        {
            (TMP_Text label, Func<string> read) = ValueLabels[i];
            if (label == null)
                continue;
            try
            {
                label.text = read();
            }
            catch
            {
                // A single unreadable entry must not stop the rest of the list repainting.
            }
        }
    }

    // ==========================================================================================
    //  Pieces
    // ==========================================================================================

    /// <summary>Instantiate the template (or a bare row when there is none) and find its parts.</summary>
    private static GameObject StampRow(Transform parent, float height,
                                       out TMP_Text? title, out Transform? option)
    {
        GameObject row;
        if (_rowTemplate != null)
        {
            row = UnityEngine.Object.Instantiate(_rowTemplate, parent);
            row.SetActive(true);
        }
        else
        {
            row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, worldPositionStays: false);
        }

        StripForReuse(row);

        title = FindPart<TMP_Text>(row.transform, "Title");
        option = row.transform.Find("Option");

        if (title == null)
        {
            var made = new GameObject("Title", typeof(RectTransform));
            made.transform.SetParent(row.transform, worldPositionStays: false);
            title = made.AddComponent<TextMeshProUGUI>();
            NativeButtonSkin.ApplyFont(title);
        }

        if (option == null)
        {
            var made = new GameObject("Option", typeof(RectTransform));
            made.transform.SetParent(row.transform, worldPositionStays: false);
            option = made.transform;
        }

        LayoutElement element = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;
        element.flexibleHeight = 0f;

        Rows.Add(row);
        return row;
    }

    /// <summary>Named child first (the authored one), then any component of the type.</summary>
    private static T? FindPart<T>(Transform row, string name) where T : Component
    {
        Transform? named = row.Find(name);
        if (named != null)
        {
            var onNamed = named.GetComponent<T>();
            if (onNamed != null)
                return onNamed;
        }
        return row.GetComponentInChildren<T>(true);
    }

    private static void BuildArrow(Transform parent, string glyph, Action onClick)
    {
        var go = new GameObject($"Arrow{glyph}", typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);

        var image = go.AddComponent<Image>();
        image.sprite = NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle);
        image.color = NativeButtonSkin.ColorFor(NativeButtonSkin.FaceState.Idle);
        image.type = Image.Type.Sliced;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 34f;
        element.preferredHeight = 30f;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, worldPositionStays: false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = glyph;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = false;
        label.fontSize = 20f;
        label.color = NativeButtonSkin.LabelColor;
        NativeButtonSkin.ApplyFont(label);

        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private static TMP_Text BuildValueLabel(Transform parent)
    {
        var go = new GameObject("Value", typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 17f;
        label.color = NativeButtonSkin.LabelColor;
        NativeButtonSkin.ApplyFont(label);

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 150f;
        element.preferredHeight = 30f;
        return label;
    }

    /// <summary>
    /// The catalog's own tooltip text, on the game's tooltip component when the row has one. A
    /// setting whose name does not explain it is exactly the thing this menu exists to make
    /// approachable, so the explanation travels with the row.
    /// </summary>
    private static void AttachTooltip(GameObject row, ConfigCatalog.ConfigItem item)
    {
        try
        {
            string text = ConfigCatalog.Tooltip(item);
            if (string.IsNullOrEmpty(text))
                return;

            var target = row.GetComponent<SettingsTooltipTarget>() ?? row.AddComponent<SettingsTooltipTarget>();
            target.Text = () => text;
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: tooltip for {item.Key} threw ({e.Message}).");
        }
    }

    /// <summary>Caption plus the marker for a setting that only takes effect at the next start.</summary>
    private static string Caption(ConfigCatalog.ConfigItem item) =>
        item.NeedsRestart ? item.Display + " *" : item.Display;

    /// <summary>Drop every built row (the template survives — it is reused).</summary>
    private static void ClearRows()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i] != null)
                UnityEngine.Object.Destroy(Rows[i]);
        }
        Rows.Clear();
        ValueLabels.Clear();
    }
}
