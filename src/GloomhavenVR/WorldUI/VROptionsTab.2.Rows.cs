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

    /// <summary>
    /// The sub-tab button, cloned from one of the window's OWN tab options (Allgemeine, Grafik,
    /// Ton …). The first version drew these by hand and they read as foreign: wrong frame, wrong
    /// font, wrong highlight. Reusing the very control the player is looking at one column over is
    /// what makes the sub-chooser belong to this menu.
    /// </summary>
    private static GameObject? _categoryTemplate;

    /// <summary>
    /// The dropdown and slider CONTROLS, lifted out of their donor rows.
    ///
    /// <para>ONE ROW SKELETON FOR EVERYTHING — the toggle row — and only the control inside it
    /// changes. Cloning three different rows was the root of a run of problems that no amount of
    /// restyling could fix: the dropdown row is authored TALLER and with a TRANSPARENT background
    /// (it holds a language picker), so those settings stood out however their caption was styled,
    /// and their tooltip anchored to a differently-shaped rect and opened somewhere else. Copying
    /// that row's properties around made it worse — its caption colour is barely opaque, and
    /// applying it to the section headers made them vanish outright.</para>
    ///
    /// <para>Taking just the control and dropping it into the toggle row's own Option holder means
    /// every row in the list is the same row. Nothing can stand out, because there is nothing left
    /// to differ.</para>
    /// </summary>
    private static GameObject? _dropdownControl;
    private static GameObject? _sliderControl;

    /// <summary>Arrow art lifted off the game's dropdown, for the stepper's two buttons.</summary>
    private static Sprite? _arrowSprite;

    /// <summary>
    /// The caption style every row is forced to, sampled once from the TOGGLE row.
    ///
    /// <para>The three templates do NOT share a caption style: the dropdown row's Title is authored
    /// large and gold because it heads a language picker, and cloning it verbatim made every
    /// enum setting read as a section heading rather than a setting. Sampling one style and
    /// applying it everywhere is what makes a list of mixed control shapes look like one list.</para>
    /// </summary>
    private static (TMP_FontAsset? font, float size, Color colour, FontStyles style, TextAlignmentOptions align)? _titleStyle;

    /// <summary>How much larger a section header is than a setting caption.</summary>
    private const float HeaderSizeFactor = 1.16f;

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
        _dropdownControl = HarvestControl<TMP_Dropdown>(_dropdownTemplate, "Dropdown");
        _sliderControl = HarvestControl<Slider>(_sliderTemplate, "Slider");
        _arrowSprite = HarvestArrowSprite(_dropdownTemplate);
        _titleStyle = SampleStyle(_toggleTemplate);

        for (int i = 0; i < host.m_Tabs.Count && _categoryTemplate == null; i++)
        {
            UIMainMenuOption? option = host.m_Tabs[i]?.OptionToggle;
            if (option != null)
                _categoryTemplate = Stamp(option.transform, "Category");
        }

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

        // The named node is a CONTAINER in this prefab, not the glyph — its own Image carries no
        // sprite, which is why the first version harvested null and every stepper drew a plain
        // white quad (Unity's fallback for a sprite-less Image). So the subtree is searched, not
        // just the node.
        Transform? named = dropdown.transform.Find("Arrow");
        Sprite? sprite = named != null ? FirstSpriteIn(named) : null;

        if (sprite == null)
        {
            // Anything with a sprite that is not the control's own background. Measuring rects is
            // useless here: the template is inactive, so every rect reads as zero.
            Image? background = dropdown.GetComponent<Image>();
            foreach (Image candidate in dropdown.GetComponentsInChildren<Image>(true))
            {
                if (candidate == null || candidate == background || candidate.sprite == null)
                    continue;
                sprite = candidate.sprite;
                break;
            }
        }

        return sprite;
    }

    private static (TMP_FontAsset? font, float size, Color colour, FontStyles style, TextAlignmentOptions align)?
        SampleStyle(GameObject? template)
    {
        if (template == null)
            return null;

        TMP_Text? title = FindPart<TMP_Text>(template.transform, "Title");
        return title == null
            ? null
            : (title.font, title.fontSize, title.color, title.fontStyle, title.alignment);
    }

    /// <summary>
    /// Force one look onto a caption. Applied by the ROW BUILDERS after the text is set, not only
    /// when the row is stamped: a builder that assigns text last would otherwise be the final word
    /// on a label whose style someone else set, which is how the dropdown rows kept their heading
    /// look through a style pass that was supposed to level them.
    /// </summary>
    private static void ApplyStyle(TMP_Text? title,
        (TMP_FontAsset? font, float size, Color colour, FontStyles style, TextAlignmentOptions align)? sampled)
    {
        if (title == null || sampled == null)
            return;

        (TMP_FontAsset? font, float size, Color colour, FontStyles style, TextAlignmentOptions align) s = sampled.Value;
        if (s.font != null)
            title.font = s.font;
        title.enableAutoSizing = false;
        title.fontSize = s.size;
        title.color = s.colour;
        title.fontStyle = s.style;
        title.alignment = s.align;
    }

    /// <summary>Every setting caption, whatever control it belongs to, reads the same.</summary>
    private static void ApplyOptionCaption(TMP_Text? title) => ApplyStyle(title, _titleStyle);

    /// <summary>
    /// A section header: the plain caption style, DERIVED upward — larger, brighter, bold small
    /// caps.
    ///
    /// <para>DERIVED AND NOT SAMPLED, because sampling is what made the headers disappear. The
    /// emphasis was taken from the dropdown row, whose caption is barely opaque (it sits on a
    /// transparent row), and copying its colour onto a header over a solid background left text
    /// nobody could see. Deriving from the style that demonstrably reads, and forcing full alpha,
    /// means a header cannot end up invisible no matter what any donor is authored like.</para>
    /// </summary>
    private static void ApplyHeaderCaption(TMP_Text? title)
    {
        ApplyOptionCaption(title);
        if (title == null || _titleStyle == null)
            return;

        (TMP_FontAsset? font, float size, Color colour, FontStyles style, TextAlignmentOptions align) plain = _titleStyle.Value;
        title.fontSize = plain.size * HeaderSizeFactor;
        title.color = new Color(
            Mathf.Clamp01(plain.colour.r * 1.25f + 0.10f),
            Mathf.Clamp01(plain.colour.g * 1.25f + 0.10f),
            Mathf.Clamp01(plain.colour.b * 1.25f + 0.10f),
            1f); // full alpha, always — an inherited alpha of 0 is what hid these once already
        title.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
    }

    /// <summary>Clone the sub-tree that carries a control, detached from the row it was authored in.</summary>
    private static GameObject? HarvestControl<T>(GameObject? template, string label) where T : Component
    {
        T? control = template == null ? null : template.GetComponentInChildren<T>(true);
        if (control == null)
            return null;

        GameObject clone = UnityEngine.Object.Instantiate(control.gameObject);
        clone.name = $"GloomhavenVR.Control.{label}";
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);
        StripForReuse(clone);
        return clone;
    }

    /// <summary>
    /// Put a harvested control into a row's own Option holder, filling it. The holder's existing
    /// contents (the toggle row's switch) go first — a row shows one control.
    /// </summary>
    private static T? PlaceControl<T>(Transform? option, GameObject? control) where T : Component
    {
        if (option == null || control == null)
            return null;

        option.gameObject.SetActive(true);
        for (int i = option.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(option.GetChild(i).gameObject);

        GameObject placed = UnityEngine.Object.Instantiate(control, option);
        placed.SetActive(true);

        var rect = (RectTransform)placed.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        return placed.GetComponent<T>() ?? placed.GetComponentInChildren<T>(true);
    }

    /// <summary>First Image WITH a sprite anywhere under a node, itself included.</summary>
    private static Sprite? FirstSpriteIn(Transform node)
    {
        foreach (Image image in node.GetComponentsInChildren<Image>(true))
        {
            if (image != null && image.sprite != null)
                return image.sprite;
        }
        return null;
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
            ApplyHeaderCaption(title);
        }

        if (option != null)
            option.gameObject.SetActive(false);

        Transform? frame = row.transform.Find("MenuElementFrame");
        if (frame != null)
            frame.gameObject.SetActive(false);
    }

    /// <summary>
    /// A quieter line under a header, saying which variant the rows below belong to
    /// ("Control board: Bronze").
    ///
    /// <para>ITS OWN LINE, not appended to the header. Merging the two would make headings like
    /// "Board &amp; Layout — Control board: Bronze", and captions in this pane clip with an ellipsis
    /// rather than wrap — the last round of this menu was spent shortening text that had run past
    /// its row. A short line of its own cannot run out of room, and reads as what it is: a statement
    /// about the block, not the name of it.</para>
    ///
    /// <para>Plain caption style, so it sits visibly below the header rather than competing with
    /// it.</para>
    /// </summary>
    private static void BuildNote(Transform parent, string text)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        if (title != null)
        {
            title.text = text;
            ApplyOptionCaption(title);
        }

        if (option != null)
            option.gameObject.SetActive(false);

        Transform? frame = row.transform.Find("MenuElementFrame");
        if (frame != null)
            frame.gameObject.SetActive(false);
    }

    /// <summary>
    /// A row that navigates instead of editing — the Debug topic list, and the way back out of it.
    ///
    /// <para>BUILT FROM THE TAB TEMPLATE, not the settings row. These behave like tabs, so they are
    /// made of the same thing the tabs are: the window's own <c>UIMainMenuOption</c>. That is what
    /// gives them the hover highlight and the click sound the rest of the menu has — hand-rolling
    /// either onto a settings row would be an imitation that drifts, and the first attempt at a
    /// hand-made link was not even clickable (a Button needs a Graphic under the pointer, and the
    /// settings row has none of its own).</para>
    /// </summary>
    /// <param name="asAction">
    /// True for a row that DOES something once (copy these settings) rather than navigating
    /// somewhere. The category template is a tab, and a tab carries a Toggle — which is right for
    /// "you are here" navigation and wrong for a one-shot action, where it reads as a checkbox you
    /// are supposed to tick. Those rows take the settings-row path instead, which has no toggle at
    /// all: the whole row is the button.
    /// </param>
    private static void BuildLinkRow(Transform parent, string caption, Action onClick,
                                     bool asAction = false)
    {
        GameObject row;
        if (_categoryTemplate != null && !asAction)
        {
            row = UnityEngine.Object.Instantiate(_categoryTemplate, parent);
            row.SetActive(true);
            StripForReuse(row);
            Rows.Add(row);
            RestVisuals(row);

            TMP_Text? tabLabel = row.GetComponentInChildren<TMP_Text>(true);
            if (tabLabel != null)
            {
                tabLabel.text = caption;
                tabLabel.enableWordWrapping = false;
                tabLabel.enableAutoSizing = true;
                tabLabel.fontSizeMin = 9f;
                tabLabel.overflowMode = TextOverflowModes.Overflow;
            }

            Toggle? tabToggle = row.GetComponentInChildren<Toggle>(true);
            if (tabToggle != null)
            {
                // No group: this is a button wearing a toggle. It never stays lit, because
                // choosing it rebuilds the list out from under itself.
                tabToggle.onValueChanged.RemoveAllListeners();
                tabToggle.group = null;
                tabToggle.SetIsOnWithoutNotify(false);
                tabToggle.onValueChanged.AddListener(on =>
                {
                    if (on)
                        onClick();
                });
                return;
            }
        }

        // No tab template — fall back to a settings row made clickable. Keeps Debug reachable at
        // the cost of the native hover and sound.
        row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        if (title != null)
        {
            title.text = caption;
            // A button's caption sits in its middle; a setting's caption sits at the left edge.
            title.alignment = TextAlignmentOptions.Center;
            // The template's Title rect only spans the LABEL COLUMN and ellipsizes what does not
            // fit — "Alle Einstellungen von Led…" says nothing. The option column is hidden on an
            // action row, so the caption may have the whole row: stretch the rect and autosize
            // down rather than truncate. A button whose label is cut off does not say what it does.
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(14f, 2f);
            titleRect.offsetMax = new Vector2(-14f, -2f);
            title.enableWordWrapping = false;
            title.enableAutoSizing = true;
            title.fontSizeMin = 9f;
            title.overflowMode = TextOverflowModes.Overflow;
        }
        if (option != null)
            option.gameObject.SetActive(false);

        Toggle? toggle = row.GetComponentInChildren<Toggle>(true);
        if (toggle != null)
        {
            // Destroying the component removes the BEHAVIOUR and leaves the LOOK: the box and the
            // checkmark are plain Images on their own objects, and that surviving box is exactly
            // the "checkbox" these action rows kept showing. The toggle owns its visuals, so
            // deactivating its object takes box and checkmark with it; if the Toggle ever sits on
            // the row root, its two known graphics are hidden individually instead.
            if (toggle.gameObject != row)
            {
                toggle.gameObject.SetActive(false);
            }
            else
            {
                if (toggle.targetGraphic != null)
                    toggle.targetGraphic.gameObject.SetActive(false);
                if (toggle.graphic != null)
                    toggle.graphic.gameObject.SetActive(false);
            }
            SafeDestroy(toggle);
        }
        // Belt and braces for stray toggle imagery living OUTSIDE the toggle's own subtree.
        Image[] leftovers = row.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < leftovers.Length; i++)
        {
            string n = leftovers[i].gameObject.name;
            if (n.IndexOf("check", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("toggle", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("tick", StringComparison.OrdinalIgnoreCase) >= 0)
                leftovers[i].gameObject.SetActive(false);
        }

        // The plate IS the button look: the image itself stays white and the Button's ColorBlock
        // below paints it, so resting/hover/press are visibly different states — which is what
        // separates "a button" from "a row of text".
        var hit = new GameObject("ClickArea", typeof(RectTransform)).AddComponent<Image>();
        var hitRect = (RectTransform)hit.transform;
        hitRect.SetParent(row.transform, worldPositionStays: false);
        hitRect.SetAsFirstSibling();
        hitRect.anchorMin = Vector2.zero;
        hitRect.anchorMax = Vector2.one;
        hitRect.offsetMin = new Vector2(4f, 4f);
        hitRect.offsetMax = new Vector2(-4f, -4f);
        hit.color = Color.white;
        hit.raycastTarget = true;

        var button = row.AddComponent<Button>();
        button.targetGraphic = hit;
        ColorBlock colors = button.colors;
        // The first plate (black at 30 %) vanished on the window's own dark ground — the rows
        // read as bare text again. A warm bronze plate is visible at rest on that background.
        colors.normalColor = new Color(0.30f, 0.25f, 0.15f, 0.50f);
        colors.highlightedColor = new Color(0.50f, 0.40f, 0.18f, 0.70f); // hover: the game's gold
        colors.pressedColor = new Color(0.68f, 0.55f, 0.24f, 0.85f);
        colors.selectedColor = colors.normalColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() => onClick());
    }

    /// <summary>
    /// Put a freshly cloned tab option into the state the game would have put it in before the
    /// player touched it.
    ///
    /// <para>The window itself calls SetFocused on its tabs when it opens; a clone nobody calls it
    /// on starts in the prefab's authored state, which is faint — and the first hover then ran the
    /// game's highlight and left the row permanently lit instead of returning it. So the resting
    /// state is asserted once here: focused, not selected, caption at its default colour. Hovering
    /// then changes appearance while the pointer is over the row and gives it back afterwards,
    /// which is what hovering is supposed to do.</para>
    /// </summary>
    private static void RestVisuals(GameObject row)
    {
        var option = row.GetComponent<UIMainMenuOption>();
        if (option == null)
            return;

        try
        {
            option.SetFocused(true);
            option.Deselect();
            if (option.text != null)
                option.text.color = option.defaultTextColor;
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: could not reset a link row's visuals ({e.Message}) — "
                                  + "it may look faint until hovered.");
        }
    }

    /// <summary>
    /// A dropdown over a hand-written list of named values, for an entry whose stored type says
    /// nothing useful about how it should be edited (see TryBuildSpecialRow).
    /// </summary>
    private static void BuildPresetRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption,
                                       string? hintKey, string[] names, int current, Action<int> apply)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        TMP_Dropdown? dropdown = PlaceControl<TMP_Dropdown>(option, _dropdownControl);
        if (dropdown == null)
            return;

        if (title != null)
        {
            title.text = Caption(item, caption);
            ApplyOptionCaption(title);
        }

        var options = new List<TMP_Dropdown.OptionData>(names.Length);
        for (int i = 0; i < names.Length; i++)
            options.Add(new TMP_Dropdown.OptionData(names[i]));

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(Mathf.Clamp(current, 0, names.Length - 1));
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(index => Apply(item, () => apply(index)));

        AttachTooltip(row, item, title, hintKey);
    }

    /// <summary>Pick the control shape from what the entry actually is, and build that row.</summary>
    private static void BuildRow(Transform parent, ConfigCatalog.ConfigItem item, int component,
                                string? caption = null, string? hintKey = null)
    {
        // A few entries' stored type says nothing useful about how they should be edited.
        if (component == 0 && TryBuildSpecialRow(parent, item, caption, hintKey))
            return;

        if (item.Kind == ConfigCatalog.ConfigKind.Bool && BuildBoolRow(parent, item, caption, hintKey))
            return;

        if (item.Kind == ConfigCatalog.ConfigKind.Choice
            && item.Choices != null && item.Choices.Length > 0
            && BuildChoiceRow(parent, item, caption, hintKey))
            return;

        // A slider needs a scalar with both ends known; a vector component or an open-ended number
        // has no bar to sit on.
        if (item.Kind != ConfigCatalog.ConfigKind.Choice
            && item.HasRange && item.Components == 1 && item.Max > item.Min
            && BuildSliderRow(parent, item, caption, hintKey))
            return;

        BuildStepperRow(parent, item, component, caption, hintKey);
    }

    /// <summary>The game's own switch, rebound to the catalog entry.</summary>
    private static bool BuildBoolRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption, string? hintKey)
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
        {
            title.text = Caption(item, caption);
            ApplyOptionCaption(title);
        }

        // THE SWITCH HAS TO SAY WHICH WAY IT IS. Its caption is static text the game's own binder
        // used to drive; with that binder stripped every row read "Ein" whether it was on or off,
        // including the ones that were plainly unlit. Driven here, and localized — the game says
        // "Aus" in German, so a mod row beside it must too.
        TMP_Text? state = ExistingValueLabel(row, title);
        toggle.onValueChanged.RemoveAllListeners();
        toggle.isOn = item.Entry.BoxedValue is bool b && b;
        PaintToggleState(state, toggle.isOn);
        toggle.onValueChanged.AddListener(on =>
        {
            Apply(item, () => ConfigCatalog.ToggleBool(item));
            // Read the ENTRY back rather than trusting the click: a bool the catalog refused to
            // change would otherwise leave a caption claiming a state the setting is not in.
            bool now = item.Entry.BoxedValue is bool v && v;
            PaintToggleState(state, now);
            if (now != on)
                toggle.SetIsOnWithoutNotify(now);
        });
        AttachTooltip(row, item, title, hintKey);
        return true;
    }

    /// <summary>The game's own dropdown, filled from the entry's acceptable values.</summary>
    private static bool BuildChoiceRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption, string? hintKey)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        TMP_Dropdown? dropdown = PlaceControl<TMP_Dropdown>(option, _dropdownControl);
        if (dropdown == null)
        {
            UnityEngine.Object.Destroy(row);
            Rows.Remove(row);
            return false;
        }

        if (title != null)
        {
            title.text = Caption(item, caption);
            ApplyOptionCaption(title);
        }

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

        AttachTooltip(row, item, title, hintKey);
        return true;
    }

    /// <summary>The game's own slider, over the entry's declared range.</summary>
    private static bool BuildSliderRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption, string? hintKey)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        Slider? slider = PlaceControl<Slider>(option, _sliderControl);
        if (slider == null)
        {
            UnityEngine.Object.Destroy(row);
            Rows.Remove(row);
            return false;
        }

        if (title != null)
        {
            title.text = Caption(item, caption);
            ApplyOptionCaption(title);
        }

        slider.onValueChanged.RemoveAllListeners();
        slider.minValue = (float)item.Min;
        slider.maxValue = (float)item.Max;
        slider.wholeNumbers = item.Integral;
        slider.SetValueWithoutNotify(ReadNumber(item));
        slider.onValueChanged.AddListener(v => Apply(item, () => WriteNumber(item, v)));

        // The bar alone does not say what the value IS, and several of these settings are only
        // meaningful as a number (turn degrees, hold seconds). The donor row ALREADY has a label
        // for exactly that — the volume row's "50/50" — so it is rebound rather than joined by a
        // second one. Adding one left the donor's stale text sitting next to ours.
        TMP_Text value = ExistingValueLabel(row, title) ?? BuildValueLabel(row.transform);
        value.text = ConfigCatalog.ValueText(item, 0);
        ValueLabels.Add((value, () => ConfigCatalog.ValueText(item, 0)));

        AttachTooltip(row, item, title, hintKey);
        return true;
    }

    /// <summary>
    /// The fallback for an unbounded number or one component of a vector or colour: two arrow
    /// buttons over <see cref="ConfigCatalog"/>'s own <c>Step</c>, wearing the menu's arrow sprite.
    /// </summary>
    private static void BuildStepperRow(Transform parent, ConfigCatalog.ConfigItem item, int component,
                                        string? caption, string? hintKey)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        if (title != null)
        {
            title.text = item.Components > 1
                ? $"{Caption(item, caption)} · {ConfigCatalog.ComponentLabel(item, component)}"
                : Caption(item, caption);
            ApplyOptionCaption(title);
        }

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

        AttachTooltip(row, item, title, hintKey);
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

        // CHOOSING A BOARD OR A HAND STYLE CHANGES WHICH ROWS EXIST. Per-variant entries are
        // filtered down to the selected one, so the switch has to rebuild the list — repainting the
        // labels of rows belonging to the variant you just left would leave you editing the wrong one.
        if (SelectsAVariant(item))
            TickGuard.Run("VROptionsTab.RebuildAfterEdit", Rebuild, "WorldUI");
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
        else
        {
            ApplyOptionCaption(title);
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

        Graphic face;
        if (_arrowSprite != null)
        {
            var image = go.AddComponent<Image>();
            image.sprite = _arrowSprite;
            image.preserveAspect = true;
            image.color = NativeButtonSkin.LabelColor;
            if (flip)
                go.transform.localScale = new Vector3(-1f, 1f, 1f);
            face = image;
        }
        else
        {
            // NEVER a sprite-less Image: Unity draws that as a solid white box, which is exactly
            // what the first version shipped. ASCII, because the menu font demonstrably lacks the
            // ◀ ▶ glyphs — the log names it: LiberationSans SDF.
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = flip ? "<" : ">";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 20f;
            label.color = NativeButtonSkin.LabelColor;
            NativeButtonSkin.ApplyFont(label);
            face = label;
        }

        var button = go.AddComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener(() => onClick());

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 26f;
        element.preferredHeight = 26f;
    }

    /// <summary>
    /// The donor row's own value caption: any TMP label that is not the row's Title. Returned so it
    /// can be rebound in place, which keeps the game's authored position instead of dropping a
    /// second label wherever it lands.
    /// </summary>
    private static TMP_Text? ExistingValueLabel(GameObject row, TMP_Text? title)
    {
        foreach (TMP_Text candidate in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (candidate == null || ReferenceEquals(candidate, title))
                continue;
            return candidate;
        }
        return null;
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
    /// The setting's explanation, in the GAME'S OWN tooltip box — the same one that answers
    /// "Optionen" with "Ändere die Spieleinstellungen…".
    ///
    /// <para>WHY THE GAME'S AND NOT OURS. A strip of our own worked but read as a mod bolted on;
    /// <c>UITextTooltipTarget</c> drives the box the player already knows, positions it the way
    /// every other tooltip in the menu is positioned, and inherits whatever the game does about
    /// hiding tooltips. It needs no serialized wiring: the box itself is global (<c>UITooltip</c>),
    /// the raycast filter comes with the component, and the geometry is set through the public
    /// <c>Initialize</c> instead of relying on a prefab's defaults.</para>
    ///
    /// <para>ON THE CAPTION, NOT THE ROW. Hovering the switch or the dropdown is the player aiming
    /// AT the control — they want to change it, not read about it. The explanation belongs to the
    /// name, so the target sits on the caption's own object and the caption is made to take
    /// raycasts (a TMP label does not by default).</para>
    /// </summary>
    private static void AttachTooltip(GameObject row, ConfigCatalog.ConfigItem item, TMP_Text? title,
                                     string? hintKey)
    {
        if (title == null)
            return;

        try
        {
            string text = HintFor(item, hintKey);
            if (string.IsNullOrEmpty(text))
                return;

            title.raycastTarget = true;

            var target = title.gameObject.GetComponent<UITextTooltipTarget>()
                         ?? title.gameObject.AddComponent<UITextTooltipTarget>();
            target.Initialize(UITooltip.Corner.Auto, Vector2.zero, anchorToExactMouseTargetInstead: false,
                              width: HintWidth, height: 50f, autoAdjustHeight: true, hideBackground: false);

            // THE ONE FLAG THAT DECIDES WHETHER THE BOX GROWS. UITooltip.Internal_SetVerticalControls
            // branches on it, and in the "prefab tooltip" branch an auto-height tooltip gets
            //   childControlHeight = false, verticalFit = Unconstrained
            // — the box CANNOT follow its text, which is why it stayed a thin strip with the
            // paragraph hanging out of it. The other branch gives childControlHeight = true and
            // verticalFit = PreferredSize. It defaults to true because every authored tooltip in the
            // game is on a prefab; ours is not.
            target.tempIsPrefabTooltip = false;

            target.TooltipEnabled = true;
            target.SetText(text);
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: the game tooltip for {item.Key} could not be "
                                  + $"attached ({e.Message}) — that row simply has no hint.");
        }
    }

    /// <summary>
    /// The hint text: the SHORT curated sentence when the row has one, else the catalog's own
    /// description.
    ///
    /// <para>The config descriptions are written for whoever is reading the config file — several
    /// hundred words with measurements and reasoning in them. In a tooltip they were clipped
    /// mid-sentence, which is worse than a short line: the player reads half a thought and then an
    /// ellipsis. Curated rows therefore carry a one-or-two-sentence hint of their own; the Debug
    /// pages keep the long text, which is the right register for the audience that opens them.</para>
    ///
    /// <para>A setting that only applies at the next start says so here, in a sentence, instead of
    /// wearing an asterisk in its caption.</para>
    /// </summary>
    private static string HintFor(ConfigCatalog.ConfigItem item, string? hintKey)
    {
        string text = string.Empty;

        if (!string.IsNullOrEmpty(hintKey))
        {
            // Loc.Mod hands the id back when a key is absent — that is the "no curated hint" signal.
            string curated = Loc.Mod(hintKey!);
            if (!string.Equals(curated, hintKey, StringComparison.Ordinal))
                text = curated;
        }

        if (text.Length == 0)
            text = ConfigCatalog.Hint(item);

        if (item.NeedsRestart && text.Length > 0)
            text += "\n\n" + Loc.Mod("vr_needs_restart");

        return text;
    }

    /// <summary>Width of the hint box, in the menu canvas's units.</summary>
    private const float HintWidth = 420f;

    /// <summary>
    /// What the row is called: the curated localized caption when there is one, else the catalog's
    /// display name (the config key with its camel humps spaced out — a programmer's name, and the
    /// reason curated rows carry their own).
    ///
    /// <para>NO MARKER for a setting that only takes effect at the next start. It used to carry an
    /// asterisk, which is a footnote to a footnote nobody wrote: it made the caption longer without
    /// saying anything a player could act on. The information is not lost — it is a plain sentence
    /// at the end of that setting's hint, where there is room to say it in words.</para>
    /// </summary>
    private static string Caption(ConfigCatalog.ConfigItem item, string? caption) =>
        string.IsNullOrEmpty(caption) ? item.Display : caption!;

    private static void PaintToggleState(TMP_Text? label, bool on)
    {
        if (label != null)
            label.text = Loc.Mod(on ? "vr_on" : "vr_off");
    }

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
