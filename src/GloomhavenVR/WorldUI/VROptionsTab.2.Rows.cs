using System;
using System.Collections.Generic;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The row kit: setting rows made by CLONING the game's own, so the result IS the game's art.
///
/// <para>THREE TEMPLATES, ONE PER CONTROL SHAPE — the first version used only the toggle row and
/// hand-built a <c>◀ value ▶</c> control for everything else. It rendered as empty boxes: the
/// arrow glyphs do not exist in the font the menu uses, and a hand-drawn frame next to the game's
/// authored rows reads as exactly what it is. The game already ships all three shapes, so all three
/// are cloned:</para>
/// <list type="bullet">
/// <item><description><b>Toggle</b> (from 'Crossplay') — every boolean.</description></item>
/// <item><description><b>Dropdown</b> (from 'Language Option Keyboard') — every closed set of
/// options: enums, and strings or numbers with an acceptable-value list.</description></item>
/// <item><description><b>Slider</b> (from 'Master Volume') — a single number with a range.</description></item>
/// </list>
///
/// <para>What is left over — an unbounded number, or one component of a vector or colour — gets the
/// stepper, but its arrows now wear a sprite HARVESTED from the dropdown's own arrow rather than a
/// text glyph. Nothing in the tab draws a shape the menu did not already contain.</para>
///
/// <para>THE ROW SKELETON, as the probe found it (identical across all three):</para>
/// <code>
/// &lt;Row&gt;              LayoutElementExtended   &lt;- authored height; NOT overridden
///   Background        Image
///   Title             TextMeshProUGUI          &lt;- the caption
///   Option            (holder)
///     &lt;control&gt;       ExtendedToggle / Slider / ExtendedDropdown
///   MenuElementFrame  FrameView                &lt;- native hover/selection frame
/// </code>
///
/// <para>THE AUTHORED HEIGHT IS LEFT ALONE, and that is the fix for the first version's clustered
/// controls. Forcing a uniform height onto rows whose children are anchored against the authored
/// one made the captions lay out at one pitch and the controls at another.</para>
///
/// <para>WHAT IS STRIPPED FROM A CLONE, each of which would otherwise fail silently: the
/// <c>TextLocalizedListener</c> (rewrites our caption from the game's string table at the next
/// language change), the navigation registrations (dead weight in VR, where the mod forces mouse
/// mode and drives rows with the laser and the fingertip), and every listener on the control (the
/// donor's own binding would keep firing alongside ours).</para>
/// </summary>
internal static partial class VROptionsTab
{
    private static GameObject? _toggleTemplate;
    private static GameObject? _dropdownTemplate;
    private static GameObject? _sliderTemplate;

    /// <summary>Arrow art lifted off the game's dropdown, for the stepper's two buttons.</summary>
    private static Sprite? _arrowSprite;

    /// <summary>Rows built for the current content, in order — cleared and rebuilt on every refresh.</summary>
    private static readonly List<GameObject> Rows = new(64);

    /// <summary>Live value labels, refreshed together so one write cannot drift from another.</summary>
    private static readonly List<(TMP_Text label, Func<string> read)> ValueLabels = new(64);

    /// <summary>
    /// Components on a cloned row that belong to the GAME's binding of it. Matched by type NAME
    /// rather than by type: the navigation components live in an assembly the mod does not
    /// reference (they appear in the decompiled sources only as field types), so there is no symbol
    /// to compare against. A rename in a game update makes this MISS rather than fail to compile,
    /// which is the right way round — a surviving registration is cosmetic, a compile-time
    /// dependency on an unreferenced assembly is not.
    /// </summary>
    private static readonly string[] BinderTypeNames =
    {
        "TextLocalizedListener",
        "UINavigationSelectable",
        "NavigationSelectableWrapper",
        "UISliderController",
        "DropdownLanguageOption",
        "TextSelectorLanguageOption",
        "CombatLogFilterSetting",
    };

    // ==========================================================================================
    //  Templates
    // ==========================================================================================

    /// <summary>
    /// Stamp all three templates off live rows. Missing ones are not fatal — each builder falls
    /// back to the shape below it, and the last fallback is a plain row, so a game update that
    /// removes one archetype costs looks rather than access.
    /// </summary>
    private static void CaptureRowTemplates(UIOptionsWindow host)
    {
        if (_toggleTemplate != null)
            return;

        Transform? toggleRow = null, dropdownRow = null, sliderRow = null;
        for (int i = 0; i < host.m_Tabs.Count; i++)
        {
            UISubmenuGOWindow? window = host.m_Tabs[i]?.TabWindow;
            if (window == null)
                continue;

            toggleRow ??= FindRowWith<Toggle>(window.transform);
            dropdownRow ??= FindRowWith<TMP_Dropdown>(window.transform);
            sliderRow ??= FindRowWith<Slider>(window.transform);
        }

        _toggleTemplate = Stamp(toggleRow, "Toggle");
        _dropdownTemplate = Stamp(dropdownRow, "Dropdown");
        _sliderTemplate = Stamp(sliderRow, "Slider");
        _arrowSprite = HarvestArrowSprite(_dropdownTemplate);

        VRLog.Info("WorldUI",
            $"VR options tab: templates — toggle={Describe(toggleRow)}, dropdown={Describe(dropdownRow)}, "
            + $"slider={Describe(sliderRow)}; stepper arrow sprite "
            + (_arrowSprite != null ? $"'{_arrowSprite.name}'" : "NOT FOUND (steppers will be plain)") + ".");
    }

    private static string Describe(Transform? row) => row == null ? "none" : $"'{row.name}'";

    private static GameObject? Stamp(Transform? donor, string label)
    {
        if (donor == null)
            return null;

        GameObject clone = UnityEngine.Object.Instantiate(donor.gameObject);
        clone.name = $"GloomhavenVR.RowTemplate.{label}";
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);
        StripForReuse(clone);
        return clone;
    }

    /// <summary>
    /// The dropdown's own arrow image — the menu's existing "there is more this way" symbol. Found
    /// by name first (the probe saw it as 'Arrow') and then by taking the smallest square Image in
    /// the control, which is what an arrow glyph looks like among a background and a label.
    /// </summary>
    private static Sprite? HarvestArrowSprite(GameObject? dropdownTemplate)
    {
        if (dropdownTemplate == null)
            return null;

        TMP_Dropdown? dropdown = dropdownTemplate.GetComponentInChildren<TMP_Dropdown>(true);
        if (dropdown == null)
            return null;

        Transform? named = dropdown.transform.Find("Arrow");
        Image? arrow = named != null ? named.GetComponent<Image>() : null;

        if (arrow == null)
        {
            float best = float.MaxValue;
            foreach (Image candidate in dropdown.GetComponentsInChildren<Image>(true))
            {
                if (candidate == null || candidate.sprite == null)
                    continue;
                var rect = (RectTransform)candidate.transform;
                float area = Mathf.Abs(rect.rect.width * rect.rect.height);
                if (area > 0f && area < best)
                {
                    best = area;
                    arrow = candidate;
                }
            }
        }

        return arrow != null ? arrow.sprite : null;
    }

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
        }
        foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
            slider.onValueChanged.RemoveAllListeners();
        foreach (TMP_Dropdown dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
            dropdown.onValueChanged.RemoveAllListeners();
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
    //  Row builders — one per control shape
    // ==========================================================================================

    /// <summary>A section caption: a toggle row with its control and its hover frame taken out.</summary>
    private static void BuildHeader(Transform parent, string caption)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
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

    /// <summary>Pick the control shape from what the entry actually is, and build that row.</summary>
    private static void BuildRow(Transform parent, ConfigCatalog.ConfigItem item, int component)
    {
        if (item.Kind == ConfigCatalog.ConfigKind.Bool && BuildBoolRow(parent, item))
            return;

        if (item.Kind == ConfigCatalog.ConfigKind.Choice
            && item.Choices != null && item.Choices.Length > 0
            && BuildChoiceRow(parent, item))
            return;

        // A slider needs a scalar with both ends known; a vector component or an open-ended number
        // has no bar to sit on.
        if (item.Kind != ConfigCatalog.ConfigKind.Choice
            && item.HasRange && item.Components == 1 && item.Max > item.Min
            && BuildSliderRow(parent, item))
            return;

        BuildStepperRow(parent, item, component);
    }

    /// <summary>The game's own switch, rebound to the catalog entry.</summary>
    private static bool BuildBoolRow(Transform parent, ConfigCatalog.ConfigItem item)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        Toggle? toggle = option == null ? null : option.GetComponentInChildren<Toggle>(true);
        if (toggle == null)
        {
            UnityEngine.Object.Destroy(row);
            Rows.Remove(row);
            return false;
        }

        if (title != null)
            title.text = Caption(item);

        toggle.onValueChanged.RemoveAllListeners();
        toggle.isOn = item.Entry.BoxedValue is bool b && b;
        toggle.onValueChanged.AddListener(_ => Apply(item, () => ConfigCatalog.ToggleBool(item)));
        AttachTooltip(row, item);
        return true;
    }

    /// <summary>The game's own dropdown, filled from the entry's acceptable values.</summary>
    private static bool BuildChoiceRow(Transform parent, ConfigCatalog.ConfigItem item)
    {
        GameObject row = StampRow(_dropdownTemplate, parent, out TMP_Text? title, out Transform? option);
        TMP_Dropdown? dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
        if (dropdown == null)
        {
            UnityEngine.Object.Destroy(row);
            Rows.Remove(row);
            return false;
        }

        if (title != null)
            title.text = Caption(item);

        object[] choices = item.Choices!;
        var labels = new List<TMP_Dropdown.OptionData>(choices.Length);
        int current = 0;
        string now = item.Entry.BoxedValue?.ToString() ?? string.Empty;

        for (int i = 0; i < choices.Length; i++)
        {
            string text = choices[i]?.ToString() ?? string.Empty;
            labels.Add(new TMP_Dropdown.OptionData(text));
            if (string.Equals(text, now, StringComparison.Ordinal))
                current = i;
        }

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.ClearOptions();
        dropdown.AddOptions(labels);
        dropdown.SetValueWithoutNotify(current);
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(index =>
        {
            if (index >= 0 && index < choices.Length)
                Apply(item, () => item.Entry.BoxedValue = choices[index]);
        });

        AttachTooltip(row, item);
        return true;
    }

    /// <summary>The game's own slider, over the entry's declared range.</summary>
    private static bool BuildSliderRow(Transform parent, ConfigCatalog.ConfigItem item)
    {
        GameObject row = StampRow(_sliderTemplate, parent, out TMP_Text? title, out Transform? option);
        Slider? slider = row.GetComponentInChildren<Slider>(true);
        if (slider == null)
        {
            UnityEngine.Object.Destroy(row);
            Rows.Remove(row);
            return false;
        }

        if (title != null)
            title.text = Caption(item);

        slider.onValueChanged.RemoveAllListeners();
        slider.minValue = (float)item.Min;
        slider.maxValue = (float)item.Max;
        slider.wholeNumbers = item.Integral;
        slider.SetValueWithoutNotify(ReadNumber(item));
        slider.onValueChanged.AddListener(v => Apply(item, () => WriteNumber(item, v)));

        // The bar alone does not say what the value IS, and several of these settings are only
        // meaningful as a number (turn degrees, hold seconds).
        TMP_Text value = BuildValueLabel(row.transform);
        value.text = ConfigCatalog.ValueText(item, 0);
        ValueLabels.Add((value, () => ConfigCatalog.ValueText(item, 0)));

        AttachTooltip(row, item);
        return true;
    }

    /// <summary>
    /// The fallback for an unbounded number or one component of a vector or colour: two arrow
    /// buttons over <see cref="ConfigCatalog"/>'s own <c>Step</c>, wearing the menu's arrow sprite.
    /// </summary>
    private static void BuildStepperRow(Transform parent, ConfigCatalog.ConfigItem item, int component)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        if (title != null)
            title.text = item.Components > 1
                ? $"{Caption(item)} · {ConfigCatalog.ComponentLabel(item, component)}"
                : Caption(item);

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
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        BuildArrow(strip, flip: true, () => Edit(item, component, -1));
        TMP_Text value = BuildValueLabel(strip);
        BuildArrow(strip, flip: false, () => Edit(item, component, +1));

        value.text = ConfigCatalog.ValueText(item, component);
        ValueLabels.Add((value, () => ConfigCatalog.ValueText(item, component)));

        AttachTooltip(row, item);
    }

    // ==========================================================================================
    //  Editing
    // ==========================================================================================

    private static void Edit(ConfigCatalog.ConfigItem item, int component, int direction) =>
        Apply(item, () =>
        {
            if (item.Kind == ConfigCatalog.ConfigKind.Choice)
                ConfigCatalog.Cycle(item, direction);
            else if (item.Kind == ConfigCatalog.ConfigKind.Bool)
                ConfigCatalog.ToggleBool(item);
            else
                ConfigCatalog.Step(item, component, direction, multiplier: 1d);
        });

    /// <summary>
    /// One edit, then repaint every visible value. Refreshing only the row that was clicked would
    /// leave the rest quietly stale — the catalog clamps against live ranges, so one setting can
    /// move another.
    /// </summary>
    private static void Apply(ConfigCatalog.ConfigItem item, Action edit)
    {
        try
        {
            edit();
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: editing {item.Section}/{item.Key} threw ({e.Message}).");
            return;
        }

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
                // One unreadable entry must not stop the rest of the list repainting.
            }
        }
    }

    private static float ReadNumber(ConfigCatalog.ConfigItem item)
    {
        try
        {
            return Convert.ToSingle(item.Entry.BoxedValue);
        }
        catch
        {
            return (float)item.Min;
        }
    }

    /// <summary>
    /// Write back in the entry's OWN type. A float pushed into an int entry throws inside BepInEx
    /// and the setting silently stops responding, so the conversion happens here where it can be
    /// reported.
    /// </summary>
    private static void WriteNumber(ConfigCatalog.ConfigItem item, float value)
    {
        Type type = item.Entry.SettingType;
        item.Entry.BoxedValue = item.Integral
            ? Convert.ChangeType(Mathf.RoundToInt(value), type)
            : Convert.ChangeType(value, type);
    }

    // ==========================================================================================
    //  Pieces
    // ==========================================================================================

    /// <summary>
    /// Instantiate a template (or a bare row when there is none) and find its parts. The authored
    /// height is deliberately left alone — see the class docs.
    /// </summary>
    private static GameObject StampRow(GameObject? template, Transform parent,
                                       out TMP_Text? title, out Transform? option)
    {
        GameObject row;
        if (template != null)
        {
            row = UnityEngine.Object.Instantiate(template, parent);
            row.SetActive(true);
        }
        else
        {
            row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, worldPositionStays: false);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;
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

    /// <summary>
    /// One stepper arrow. The sprite is the menu's own, mirrored on X for the left one, so the two
    /// directions are the same symbol the player already reads elsewhere in this window.
    /// </summary>
    private static void BuildArrow(Transform parent, bool flip, Action onClick)
    {
        var go = new GameObject(flip ? "ArrowLeft" : "ArrowRight", typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);

        var image = go.AddComponent<Image>();
        image.sprite = _arrowSprite;
        image.preserveAspect = true;
        image.color = NativeButtonSkin.LabelColor;
        if (flip)
            go.transform.localScale = new Vector3(-1f, 1f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener(() => onClick());

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 26f;
        element.preferredHeight = 26f;
    }

    private static TMP_Text BuildValueLabel(Transform parent)
    {
        var go = new GameObject("Value", typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 17f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.color = NativeButtonSkin.LabelColor;
        NativeButtonSkin.ApplyFont(label);

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 130f;
        element.preferredHeight = 26f;
        return label;
    }

    /// <summary>
    /// The catalog's own explanation, on the mod's tooltip component. A setting whose name does not
    /// explain it is exactly what this menu exists to make approachable, so the text travels with
    /// the row.
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

    /// <summary>Drop every built row (the templates survive — they are reused).</summary>
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
