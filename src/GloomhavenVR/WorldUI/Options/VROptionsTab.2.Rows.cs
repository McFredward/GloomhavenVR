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
    /// Live BARS, refreshed with the labels for exactly the same reason: an edit that did not come
    /// from the bar itself must still move the handle.
    ///
    /// <para>This is new work for the bar+arrows row (2026-08-22, question d) — pressing ◀ there
    /// would otherwise move the number and leave the handle where the last drag put it, which is a
    /// control disagreeing with itself. It is also a latent fix for the plain slider rows: the
    /// catalog clamps against LIVE ranges, so one setting can move another (BoardPitchMin against
    /// BoardPitchMax), and only the labels used to follow.</para>
    /// </summary>
    private static readonly List<(Slider bar, ConfigCatalog.ConfigItem item)> Sliders = new(32);

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
        // ButtonSwitch's Awake calls Refresh(isOn), which ends in an unguarded
        // text.SetTextKey(...) on its serialized TextLocalizedListener — the line above, which we
        // destroy on every row. It has therefore NEVER completed on one of these rows and drives
        // nothing; the mod supplies the row's visuals and handlers itself. See StampRow, which also
        // had to start cloning the template INACTIVE so this strip happens before any Awake runs.
        "ButtonSwitch",
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

    /// <summary>
    /// Every setting caption, whatever control it belongs to, reads the same — and NEVER ends in
    /// an ellipsis.
    ///
    /// <para>USER RULING 2026-08-03: "Manche Optionsnamen sind immer noch mit '...' abgekürzt —
    /// das soll gar nicht sein - mach die Namen kürzer wenn sie nicht hinpassen aber niemals
    /// '...'". The ellipsis was not ours: the caption inherits the GAME row template's TMP
    /// settings, and that template ellipsizes. A truncated name is strictly worse than a small
    /// one — "Fächer: Winkel je…" identifies nothing — so the caption now behaves like the action
    /// rows already do (see the BuildActionRow branch, which learned this first): no wrapping,
    /// overflow instead of ellipsis, and auto-sizing that shrinks the text a little rather than
    /// cutting it. The floor is 78% of the sampled size, which is still comfortably readable at
    /// arm's length; anything that does not fit even then is LOGGED by name (see
    /// <see cref="ProbeCaptionFit"/>) so the name itself can be shortened instead of guessing
    /// which ones are too long.</para>
    /// </summary>
    private static void ApplyOptionCaption(TMP_Text? title)
    {
        ApplyStyle(title, _titleStyle);
        if (title == null)
            return;
        float authored = _titleStyle != null ? _titleStyle.Value.size : title.fontSize;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Overflow; // NEVER Ellipsis — see the ruling above
        title.enableAutoSizing = true;
        title.fontSizeMax = authored;
        title.fontSizeMin = Mathf.Max(9f, authored * CaptionMinScale);
    }

    /// <summary>How far a caption may shrink before it is reported as too long (fraction of the
    /// sampled style size).</summary>
    private const float CaptionMinScale = 0.78f;

    /// <summary>Left shift of a dependent row's caption, in the menu canvas's units.</summary>
    private const float DependentIndent = 18f;

    /// <summary>
    /// Nudge a DEPENDENT row's caption right so it visibly hangs under the parent that folds it
    /// out (VROptionsTab.8.Dependencies.cs — user item 4). Only the Title rect moves: the control
    /// column stays aligned with every other row, which keeps the list readable while still
    /// saying "this one belongs to the row above". Applied after the caption style, before the
    /// fit probe, so the probe measures the narrowed budget the caption actually has.
    /// </summary>
    private static void IndentDependent(TMP_Text? title, ConfigCatalog.ConfigItem item)
    {
        if (title == null || !HasDependency(item))
            return;
        var rect = (RectTransform)title.transform;
        rect.offsetMin = new Vector2(rect.offsetMin.x + DependentIndent, rect.offsetMin.y);
    }

    /// <summary>Config keys already reported as not fitting — one line per name, not per frame.</summary>
    private static readonly System.Collections.Generic.HashSet<string> ReportedLongCaptions = new();

    /// <summary>
    /// Report a caption that does not fit its label column even at the smallest allowed size. This
    /// is the DATA behind the "shorten the name" half of the ruling: instead of guessing which of
    /// the 362 localized names are too long, the log names them, with the measured overflow and
    /// the language they overflowed in, so each one can be shortened deliberately.
    /// </summary>
    private static void ProbeCaptionFit(TMP_Text? title, string key)
    {
        if (title == null || string.IsNullOrEmpty(title.text))
            return;
        var rect = (RectTransform)title.transform;
        float budget = rect.rect.width;
        if (budget <= 1f)
            return; // layout has not run yet — the next rebuild probes again
        float needed = title.GetPreferredValues(title.text, 0f, 0f).x * CaptionMinScale;
        if (needed <= budget + 0.5f || !ReportedLongCaptions.Add(key))
            return;
        VRLog.Warn("WorldUI",
            $"OPTION NAME TOO LONG: '{title.text}' ({key}, {Loc.CurrentLanguage}) needs " +
            $"{needed:F0} px at the smallest allowed size but its label column is {budget:F0} px " +
            "— it will OVERFLOW rather than be cut (ellipsis is banned, user ruling 2026-08-03). " +
            "Shorten the name in Loc.ConfigNames for this language.");
    }


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
    private static void ApplyHeaderCaption(TMP_Text? title, float sizeFactor = HeaderSizeFactor)
    {
        ApplyOptionCaption(title);
        if (title == null || _titleStyle == null)
            return;

        (TMP_FontAsset? font, float size, Color colour, FontStyles style, TextAlignmentOptions align) plain = _titleStyle.Value;
        title.fontSize = plain.size * sizeFactor;
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
    ///
    /// <para>THE OLD CONTENTS GO IMMEDIATELY, not at end of frame, and that is the fix for the
    /// "50/50" report (user, 2026-08-11: "alle Optionen die mit einer verschiebaren Leiste
    /// angezeigt werden [haben] einen '50/50' Text darin"). <c>Destroy</c> defers to end of frame,
    /// so while the row was still being built the DOOMED toggle switch — state label included —
    /// was still the first thing <see cref="ExistingValueLabel"/> found. The slider rows bound
    /// their value readout to that dying label (gone one frame later, value with it) while the
    /// slider clone's OWN label kept the donor's baked caption: the volume row's literal "50/50",
    /// authored there and stripped of the game's UISliderController that used to drive it.
    /// Clearing immediately means the first non-title label really is the placed control's own,
    /// and the rebind lands where the readout is visible.</para>
    /// </summary>
    private static T? PlaceControl<T>(Transform? option, GameObject? control) where T : Component
    {
        if (option == null || control == null)
            return null;

        option.gameObject.SetActive(true);
        for (int i = option.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(option.GetChild(i).gameObject);

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
                    SafeDestroyNow(component);
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

    /// <summary>
    /// Strip a binder RIGHT NOW rather than at the end of the frame — the difference matters for
    /// exactly one of them and would have traded one exception storm for another.
    ///
    /// <para><c>Object.Destroy</c> defers <c>OnDestroy</c> to the end of the frame, and by then this
    /// row has also had its native <c>Toggle</c> destroyed (see the bool-row path). <c>ButtonSwitch</c>
    /// holds a serialized reference to that same toggle and its <c>OnDestroy</c> calls
    /// <c>toggle.onValueChanged.RemoveAllListeners()</c> unguarded, so with both queued in one frame
    /// and no ordering guarantee between them it would throw for the same reason its <c>Awake</c>
    /// already did. Destroying it immediately, while the clone is still inactive and still whole,
    /// means its <c>OnDestroy</c> runs against a live toggle and finishes.</para>
    ///
    /// <para>Iterating a <c>GetComponentsInChildren</c> array while destroying immediately is safe
    /// here: entries for destroyed components read as null and the caller's loop already skips
    /// those. Falls back to the deferred destroy if the immediate one is refused.</para>
    /// </summary>
    private static void SafeDestroyNow(Component? component)
    {
        if (component == null)
            return;
        try
        {
            UnityEngine.Object.DestroyImmediate(component);
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: immediate strip of {component.GetType().Name} "
                                  + $"refused ({e.Message}) — falling back to a deferred destroy.");
            SafeDestroy(component);
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
    //  Row builders — one per control shape
    // ==========================================================================================

    /// <summary>
    /// A section caption: a toggle row with its control and its hover frame taken out.
    ///
    /// <para><paramref name="hintKey"/> is the heading's OWN hover explanation (user report
    /// 2026-08: "Geb auch den Überschriften Tooltipps die kurz erklären was auf dem Controllboard
    /// mit den folgenden Einstellungen kontrolliert wird") — the same game-tooltip mechanism the
    /// settings rows use (<see cref="AttachHoverHint"/>), keyed into <see cref="Loc.Mod"/> so it is
    /// localized like everything else. A heading whose key has no text simply has no hint, the
    /// same degradation <see cref="HintFor"/> already allows a row.</para>
    ///
    /// <para><paramref name="sub"/> renders the SECOND heading level of the per-board page: same
    /// bold small-caps treatment so it still reads as structure, one size step down (the plain
    /// caption size) so the page's two levels are tellable apart at a glance.</para>
    /// </summary>
    private static void BuildHeader(Transform parent, string caption, string? hintKey = null,
                                    bool sub = false)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        if (title != null)
        {
            title.text = sub ? "· " + caption : caption;
            ApplyHeaderCaption(title, sub ? 1f : HeaderSizeFactor);
            if (!string.IsNullOrEmpty(hintKey))
            {
                // Loc.Mod hands the id back when a key is absent — the "no hint" signal HintFor
                // already relies on; never show a raw key as a tooltip.
                string hint = Loc.Mod(hintKey!);
                if (!string.Equals(hint, hintKey, StringComparison.Ordinal))
                    AttachHoverHint(title, hint, caption);
            }
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
    /// <returns>
    /// The row that was built, so a caller that has to REPAINT the caption later can hold on to it.
    /// Added for the test-trigger page, whose rows became latches (press = on, press again = off) and
    /// therefore have to show their own state — and that page may not rebuild itself on a press,
    /// which would destroy the very button under the pointer (VROptionsTab.9.TestTriggers.cs). Every
    /// existing caller ignores the value; nothing else about this method changed.
    /// </returns>
    private static GameObject BuildLinkRow(Transform parent, string caption, Action onClick,
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
                // Same fit as the sub-tab column (FitTabCaption): these rows are the same tab
                // clone with the same donor label rect, so a topic name the rect cannot hold
                // single-line ("Messung & Diagnose") shrank here exactly as the category names
                // did there. In the wide content pane the wrap simply never triggers as long as
                // the line fits; a name too long for its rect breaks instead of going tiny.
                FitTabCaption(tabLabel);
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
                return row;
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
        return row;
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
    /// Fit a tab-clone's caption by WRAPPING it rather than only shrinking it.
    ///
    /// <para>SHRINK-ONLY WAS NOT ENOUGH (user report 2026-08: "die Tab Namen sind unter Umständen
    /// sehr lang und der Text wird damit sehr klein"). The donor caption is authored for a
    /// one-word tab ("Video", "Audio"); the mod's names are two and three words, and the
    /// single-line auto-shrink drove "Avatar &amp; Mehrspieler" toward the 9pt floor. With wrapping
    /// on, a long name breaks onto a second line — at the explicit break the Loc string carries,
    /// or at a space — and the fitter only has to fit the longest LINE, which is roughly half the
    /// name and therefore roughly twice the font.</para>
    ///
    /// <para>THE LABEL RECT IS GIVEN THE WIDGET'S HEIGHT FIRST. The donor rect is one line tall,
    /// and TMP's auto-size fits BOTH axes of the rect: wrapped into a one-line-tall rect, a
    /// two-line caption would come out SMALLER than the single-line fit, not larger. Only the
    /// vertical anchors are touched — the horizontal extent is the donor's, and it is what the
    /// single-line fit already wrapped and shrank against. Two lines is also the practical cap:
    /// the auto-size floor keeps a third line from ever paying, and no curated name has one.</para>
    /// </summary>
    private static void FitTabCaption(TMP_Text label)
    {
        var rect = (RectTransform)label.transform;
        rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
        rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
        rect.offsetMin = new Vector2(rect.offsetMin.x, 2f);
        rect.offsetMax = new Vector2(rect.offsetMax.x, -2f);

        label.enableWordWrapping = true;
        // A one-line caption sat centred in the one-line donor rect; keep it centred in the
        // stretched one instead of letting a prefab top/bottom alignment pin it to an edge.
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        // Never LARGER than the donor's caption — autosizing in a stretched rect would otherwise
        // happily inflate a short name past the tab style it is supposed to match. Read before
        // enableAutoSizing so the cap is the donor's authored size, never an autosized one.
        label.fontSizeMax = label.fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = 9f;
        label.overflowMode = TextOverflowModes.Overflow;
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
            IndentDependent(title, item);
            ProbeCaptionFit(title, item.Key);
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
        // …and three that are not edited with a CONTROL at all: the environment, the hand style and
        // the head mask are picked from a strip of PICTURES (user, 2026-09-02: "das umstellen der
        // Assets etwas praesenter […] als Kacheln mit einem Bild darin"). This runs BEFORE
        // TryBuildSpecialRow because [Sky] Style and [Net] MaskId already have dropdown definitions
        // there — and those definitions stay, deliberately, as the working fallback if this hook is
        // ever removed. See WorldUI/Options/VariantTiles.cs.
        if (component == 0 && TryBuildVariantTiles(parent, item, caption, hintKey))
            return;

        // A few entries' stored type says nothing useful about how they should be edited.
        if (component == 0 && TryBuildSpecialRow(parent, item, caption, hintKey))
            return;

        // …and a couple whose type is fine but whose VALUE SET is not what the generic ladder
        // assumes (a six-position bar, a magic -1). See TryBuildWidgetOverrideRow.
        if (component == 0 && TryBuildWidgetOverrideRow(parent, item, caption, hintKey))
            return;

        if (item.Kind == ConfigCatalog.ConfigKind.Bool && BuildBoolRow(parent, item, caption, hintKey))
            return;

        if (item.Kind == ConfigCatalog.ConfigKind.Choice
            && item.Choices != null && item.Choices.Length > 0
            && BuildChoiceRow(parent, item, caption, hintKey))
            return;

        // A bar needs a scalar with both ends known; a vector component or an open-ended number has
        // no bar to sit on and gets the arrows alone. …and two bounded scalars ASK for the arrows
        // alone anyway (PrefersStepper, VROptionsTab.4.Curated.cs): a bar is a gesture, a stepper is
        // an amount, and a dial the user tunes to a number he can repeat needs the amount.
        //
        // EVERYTHING ELSE BOUNDED GETS BOTH. See BuildBarAndArrowsRow for why the allow-list that
        // used to stand here is gone.
        if (item.Kind != ConfigCatalog.ConfigKind.Choice
            && item.HasRange && item.Components == 1 && item.Max > item.Min
            && !PrefersStepper(item)
            && BuildBarAndArrowsRow(parent, item, caption, hintKey))
            return;

        BuildStepperRow(parent, item, component, caption, hintKey);
    }

    // ==============================================================================================
    //  THE ALLOW-LIST THAT USED TO STAND HERE — and why it is gone
    // ==============================================================================================
    //
    // Twenty-five key stems named the rows that got the bar AND the arrows; everything else bounded
    // got a bare bar. It is inverted now: EVERY bounded scalar row is a bar-and-arrows row, and the
    // only exceptions are PrefersStepper's two (a standing user ruling, "nicht Schieberegler,
    // sondern die Pfeile") and the two value sets that are not continua at all
    // (TryBuildWidgetOverrideRow).
    //
    // THE USER ASKED FOR THE PER-BOARD ROWS (2026-08, verbatim): "In den VR Einstellungen pro board
    // die Slider mach sie zu diesen hybriden slidern, so dass man sie besser einstellen kann."
    // Extending the list by the board stems would have answered that sentence and left the question
    // underneath it unanswered, so the list was measured against its own stated rule first.
    //
    // WHAT THE MEASUREMENT SAID. The rule the table wrote down for itself is "a bounded number a
    // player tunes to a value they want back". Of the 115 bounded scalars it did NOT contain, there
    // is not one that fails that test — and four of them carry the table's own evidence:
    //
    //     [MapRoom] IconScale          = 2.29637
    //     [MapRoom] PartyMarkerScale   = 2.76815
    //     [MapRoom] PathWidthScale     = 2.72753
    //     [MapRoom] GloomhavenIconScale= 1.00027
    //
    // Five-decimal numbers on a gridless float bar are the exact fingerprint this row builder was
    // created to remove, and GloomhavenIconScale is one more "attempt to put a bar back on 1.0 that
    // could not be made" — the same defect as [Cards] ActiveCardScale_Oak = 0.9999998, which the doc
    // comment below cites AS THE REASON THE FEATURE EXISTS while leaving ActiveCardScale itself out
    // of the table. A table that omits its own evidence is not separating gestures from amounts; it
    // is a record of what somebody got to that afternoon.
    //
    // AND THERE IS NO GESTURE CLASS LEFT TO PROTECT. The obvious candidates for "a bar alone is
    // right here" — a volume, an effect strength, a hand size you eyeball ([EnvSound] Gain,
    // [Elements] ResponseStrength, [Hands] GloveScale) — were already IN the table. The user had
    // already ruled that even those want the arrows. Nothing was left on the other side of the line.
    //
    // WHAT IT COSTS AND WHAT IT BUYS. 115 rows gain two arrows and, more importantly, gain
    // SnapToStep: a drag now lands on Min + n x step instead of anywhere at all, so every value the
    // bar can reach is one the arrows can return to. That is the half of BuildBarAndArrowsRow that
    // is a fix rather than a decoration, and it was reaching a fifth of the rows that needed it.
    //
    // WHAT IT MEANS FOR THE REPORT HE ACTUALLY FILED, stated plainly because the answer is "not
    // much": of the 204 per-board rows, 174 have NO DECLARED RANGE and therefore no bar to make
    // hybrid — including both dials he names ([Cards] SlotOverlayOffset_{board} is a Vector3, and
    // SlotOverlaySpacing_{board} declares no range). 24 were bar-only and become hybrid here; 6
    // already were. The per-board precision he is asking about is bought by ConfigSteps' step sizes,
    // not by this row shape. Declaring ranges on those 174 would give them bars, and that is a
    // decision about his hand-tuned clamps, not one to take inside a row builder.
    //
    // BuildSliderRow went with the table: with nothing selecting it, a bare bar is unreachable.

    /// <summary>The snap-turn angles anyone actually wants — <see cref="ConfigSteps"/>'s own words.</summary>
    private static readonly float[] SnapTurnPresets = { 15f, 30f, 45f, 60f, 90f };

    /// <summary>
    /// Two rows whose TYPE is an ordinary bounded number but whose VALUE SET is not a continuum,
    /// so the generic ladder's bar is wrong for a reason no range can express (2026-08-22 audit,
    /// question d: <i>"nicht jedes Felt macht sinn mit einer verschibaren Bar"</i>).
    ///
    /// <list type="number">
    /// <item><b><c>[Comfort] SnapTurnDegrees</c> → a named-preset dropdown.</b> Declared 15…90, and
    /// <see cref="ConfigSteps"/> writes its step down as 15° with the argument "15/30/45/60/90 are
    /// the angles anyone actually wants". That is SIX positions, and a six-position bar is a
    /// dropdown drawn badly: at arm's length the ray lands wherever it lands and 43° is as
    /// reachable as 45°. A value that is not on the list (a hand-edited cfg) is not swallowed —
    /// it gets its own trailing entry naming the raw number, exactly as the head-mask row does,
    /// and nothing is written until a real angle is picked.</item>
    /// <item><b><c>[RenderQuality] PixelLightCount</c> → a labelled stepper.</b> Its −1 means "leave
    /// the game's own value alone" — a MAGIC VALUE at one end of the bar that a bar cannot label, so
    /// the row read "-1" and said nothing. As a stepper it steps one light at a time (the
    /// written-down step) and the readout says "Standard" at −1. WHAT SHIPS IS 0, not −1, since the
    /// 2026-08-23 ruling ("zu gefährlich für normale Nutzer"): the cap is 0 on a fresh install and in
    /// all four <c>RenderQuality.Presets</c>, and the row itself left the curated Bild page for
    /// Erweitert. The override still applies wherever the catalog builds the row, which is the only
    /// reason the move did not turn it back into an unlabelled bar.</item>
    /// </list>
    /// </summary>
    private static bool TryBuildWidgetOverrideRow(Transform parent, ConfigCatalog.ConfigItem item,
                                                  string? caption, string? hintKey)
    {
        if (string.Equals(item.Section, "Comfort", StringComparison.Ordinal)
            && string.Equals(item.Key, "SnapTurnDegrees", StringComparison.Ordinal))
        {
            float now = ReadNumber(item);
            int offered = SnapTurnPresets.Length;
            var names = new string[offered];
            int current = -1;
            for (int i = 0; i < offered; i++)
            {
                names[i] = SnapTurnPresets[i].ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "°";
                if (Mathf.Abs(SnapTurnPresets[i] - now) < 0.01f)
                    current = i;
            }

            if (current < 0)
            {
                // AN ANGLE WE DO NOT OFFER IS SHOWN, NOT SWALLOWED (the [Net] MaskId rule): a cfg
                // hand-edited to 37° would otherwise be silently rewritten to 15° by the dropdown's
                // own clamp on the first press.
                var widened = new string[offered + 1];
                names.CopyTo(widened, 0);
                widened[offered] = now.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "°?";
                current = offered;
                names = widened;
            }

            BuildPresetRow(parent, item, caption, hintKey, names, current,
                           index =>
                           {
                               if (index >= 0 && index < offered)
                                   WriteNumber(item, SnapTurnPresets[index]); // BepInEx persists on set
                           });
            return true;
        }

        if (string.Equals(item.Section, "RenderQuality", StringComparison.Ordinal)
            && string.Equals(item.Key, "PixelLightCount", StringComparison.Ordinal))
        {
            BuildStepperRow(parent, item, component: 0, caption, hintKey,
                            readValue: () => Mathf.RoundToInt(ReadNumber(item)) < 0
                                ? Loc.Mod("cfg_default")
                                : ConfigCatalog.ValueText(item, 0));
            return true;
        }

        return false;
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
            IndentDependent(title, item);
            ProbeCaptionFit(title, item.Key);
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
            IndentDependent(title, item);
            ProbeCaptionFit(title, item.Key);
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

    /// <summary>
    /// THE SHAPE OF EVERY BOUNDED ROW: the game's own slider and the stepper's two arrows in ONE
    /// row. It was the exception until 2026-08 and it is the rule now — see the block above
    /// THE ALLOW-LIST THAT USED TO STAND HERE for what was measured before inverting it.
    ///
    /// <para>Layout is <c>◀ [ bar ] ▶</c>, and the number sits inside the bar — the donor slider
    /// ('Master Volume') carries its own amount label, which <see cref="BindValueLabels"/> rebinds
    /// to the live value, so there is exactly ONE readout in the row and it cannot drift between
    /// the two inputs. That is the shape the user asked for: <i>Balken zum groben Ziehen, Pfeile
    /// für die letzte Kommastelle, Zahl dazwischen</i>.</para>
    ///
    /// <para>THE BAR SNAPS TO THE STEP, and that is the actual fix rather than a decoration. A
    /// float bar is gridless — Unity's <c>Slider</c> has a grid only when <c>wholeNumbers</c> is
    /// set, and it is set from <c>item.Integral</c> — which is how <c>0.9999998</c> got written
    /// into a shipped default by somebody aiming at 1.0.
    /// Here a drag lands on <c>Min + n × step</c> — the same step one arrow press moves, from
    /// <see cref="ConfigSteps"/> — so a coarse drag and a fine press speak the same units, every
    /// value the bar can reach is one the arrows can return to, and a round number is reachable by
    /// hand. The grid is anchored at <c>Min</c>, not at the current value, so it does not drift as
    /// you tune.</para>
    ///
    /// <para>A VALUE ALREADY OFF THE GRID IS NOT TOUCHED until the player moves this control:
    /// nothing is written on build, and the arrows keep <see cref="ConfigCatalog.Step"/>'s plain
    /// add. So a hand-tuned <c>−31.067</c> still reads and still ships; it is only a deliberate
    /// drag that puts the value onto the grid, which is the one moment the player IS choosing a
    /// new number.</para>
    ///
    /// <para>Returns false when the donor has no harvestable slider, so the row falls through to
    /// <see cref="BuildStepperRow"/> rather than leaving an empty row on the page. That fallback is
    /// the ONLY bar-less path a bounded scalar has now, which is why it must keep working.</para>
    /// </summary>
    private static bool BuildBarAndArrowsRow(Transform parent, ConfigCatalog.ConfigItem item,
                                             string? caption, string? hintKey)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        if (option == null || _sliderControl == null)
        {
            UnityEngine.Object.Destroy(row);
            Rows.Remove(row);
            return false;
        }

        if (title != null)
        {
            title.text = Caption(item, caption);
            ApplyOptionCaption(title);
            IndentDependent(title, item);
            ProbeCaptionFit(title, item.Key);
        }

        // The holder is cleared IMMEDIATELY (not deferred) for the reason PlaceControl documents:
        // a Destroy that defers to end of frame leaves the dying toggle's own label as the first
        // TMP in the row, and the readout binds to a label that is gone one frame later ("50/50").
        option.gameObject.SetActive(true);
        for (int i = option.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(option.GetChild(i).gameObject);

        var strip = (RectTransform)new GameObject("BarAndArrows", typeof(RectTransform)).transform;
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

        // The arrows repeat here too. The bar beside them is the fast way across the range, so the
        // hold is not what makes this row usable — but a row where the arrows behave differently
        // from every other row's arrows is its own small lie, and the fine steps ConfigSteps hands
        // out now are exactly as fine on a bounded dial as on an unbounded one.
        BuildArrow(strip, flip: true, () => Edit(item, 0, -1), repeat: true);

        // The bar gets its own holder so PlaceControl can stretch the harvested slider into it
        // while the layout group decides how wide "it" is: the arrows keep their 26 px, the bar
        // takes everything left over.
        var barHolder = (RectTransform)new GameObject("Bar", typeof(RectTransform)).transform;
        barHolder.SetParent(strip, worldPositionStays: false);
        LayoutElement barSize = barHolder.gameObject.AddComponent<LayoutElement>();
        barSize.flexibleWidth = 1f;
        barSize.preferredHeight = 26f;

        Slider? slider = PlaceControl<Slider>(barHolder, _sliderControl);
        if (slider == null)
        {
            UnityEngine.Object.Destroy(row);
            Rows.Remove(row);
            return false;
        }

        slider.onValueChanged.RemoveAllListeners();
        slider.minValue = (float)item.Min;
        slider.maxValue = (float)item.Max;
        slider.wholeNumbers = item.Integral;
        slider.SetValueWithoutNotify(ReadNumber(item));
        slider.onValueChanged.AddListener(v => Apply(item, () => WriteNumber(item, SnapToStep(item, v))));

        BuildArrow(strip, flip: false, () => Edit(item, 0, +1), repeat: true);

        // Every non-caption label in the row follows the live value — the slider's own amount text
        // included, which is the number the player reads between the arrows. Registering the
        // SLIDER as well is what keeps the bar's handle under an ARROW press: Apply repaints the
        // labels, and without this the handle would sit where the last drag left it.
        BindValueLabels(row, title, item, component: 0);
        Sliders.Add((slider, item));

        AttachTooltip(row, item, title, hintKey);
        return true;
    }

    /// <summary>
    /// The nearest value on the entry's own step grid, anchored at its range minimum. See
    /// <see cref="BuildBarAndArrowsRow"/> for why a bar without a grid is the defect and not the
    /// feature. A step that is zero or worse (there is no such entry today, but a future
    /// <see cref="ConfigSteps"/> line could produce one) leaves the value exactly as dragged.
    /// </summary>
    private static float SnapToStep(ConfigCatalog.ConfigItem item, float value)
    {
        double step = item.BaseStep;
        if (step <= 0d || double.IsNaN(step) || double.IsInfinity(step))
            return value;
        double snapped = item.Min + Math.Round((value - item.Min) / step) * step;
        return (float)Math.Min(item.Max, Math.Max(item.Min, snapped));
    }

    /// <summary>
    /// The fallback for an unbounded number or one component of a vector or colour: two arrow
    /// buttons over <see cref="ConfigCatalog"/>'s own <c>Step</c>, wearing the menu's arrow sprite.
    /// </summary>
    /// <param name="readValue">
    /// Optional replacement readout, for a stepper whose number is not the whole truth — see
    /// <see cref="TryBuildWidgetOverrideRow"/> and <c>[RenderQuality] PixelLightCount</c>, whose
    /// −1 means "the game's own value" and reads as "Standard" rather than as a negative count.
    /// Null keeps the catalog's own formatting, which is what every other stepper wants.
    /// </param>
    private static void BuildStepperRow(Transform parent, ConfigCatalog.ConfigItem item, int component,
                                        string? caption, string? hintKey, Func<string>? readValue = null)
    {
        GameObject row = StampRow(_toggleTemplate, parent, out TMP_Text? title, out Transform? option);
        if (title != null)
        {
            title.text = item.Components > 1
                ? $"{Caption(item, caption)} · {ConfigCatalog.ComponentLabel(item, component)}"
                : Caption(item, caption);
            ApplyOptionCaption(title);
            IndentDependent(title, item);
            ProbeCaptionFit(title, item.Key);
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

        // HOLD-TO-REPEAT, ON EVERY STEPPER SINCE ModBuild 271, and it used to be on exactly two.
        //
        // IT WAS ADDED FOR [WorldUI] TravelButtonOffsetX/YWindowHeights, which are steppers by
        // explicit user ruling ("sollen keine Schieberegler sein, sondern die Pfeile, wo man den
        // echten Wert einfach einstellen kann") and therefore may not buy their press count down
        // with a bar. It was withheld from everything else on the argument that "on a pose dial an
        // accidental long press would run the value away, and a press there is meant to be a
        // press".
        //
        // THAT ARGUMENT DOES NOT SURVIVE THE STEP SIZES. ConfigSteps' length unit is a millimetre
        // now, measured off the grid the user's own values sit on, and the 174 per-board rows that
        // declare no range have NO BAR to cross with — a stepper is the whole control there. What
        // was a 5-press nudge is a 50-press one, and "every press is a press" stops being a virtue
        // and becomes the thing the report was about.
        //
        // AND THE FEAR IS ANSWERED BY ArrowRepeat'S OWN SHAPE: a tap is still exactly one press
        // (onClick stays wired and the repeat only starts after a 0.45 s hold), and it lets go on
        // pointer-up, pointer-exit and disable, so a VR laser sliding off the arrow stops it. The
        // cost of an accidental hold is bounded and undoable in the same gesture; the cost of no
        // repeat is a dial the player gives up on, which is what he wrote in.
        BuildArrow(strip, flip: true, () => Edit(item, component, -1), repeat: true);
        TMP_Text value = BuildValueLabel(strip);
        BuildArrow(strip, flip: false, () => Edit(item, component, +1), repeat: true);

        Func<string> read = readValue ?? (() => ConfigCatalog.ValueText(item, component));
        value.text = read();
        ValueLabels.Add((value, read));

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

        // …and every bar follows its own entry, so an ARROW press in a bar+arrows row moves the
        // handle too. SetValueWithoutNotify, or the write below would come straight back in as a
        // drag and re-enter Apply.
        for (int i = 0; i < Sliders.Count; i++)
        {
            (Slider bar, ConfigCatalog.ConfigItem owner) = Sliders[i];
            if (bar == null)
                continue;
            try
            {
                bar.SetValueWithoutNotify(ReadNumber(owner));
            }
            catch
            {
                // Same rule as the labels: one unreadable entry must not stop the rest.
            }
        }

        // CHOOSING A BOARD OR A HAND STYLE CHANGES WHICH ROWS EXIST — and so does flipping a
        // setting other rows DEPEND on (VROptionsTab.8.Dependencies.cs): per-variant entries are
        // filtered down to the selected one and dependent rows fold under their parent, so either
        // kind of edit has to rebuild the list. Repainting alone would leave you editing rows that
        // should no longer be there.
        if (SelectsAVariant(item) || IsDependencyParent(item))
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
    ///
    /// <para>THE CLONE IS BORN INACTIVE, AND THAT IS THE WHOLE POINT OF THE DANCE BELOW. Unity runs
    /// <c>Awake</c> during <c>Instantiate</c> when the source is active, so an active template ran
    /// the game's own row scripts on the clone BEFORE <see cref="StripForReuse"/> could take them
    /// off — and one of them threw every single time. <c>ButtonSwitch.Awake</c> calls
    /// <c>Refresh(isOn)</c>, whose last statement is an UNGUARDED <c>text.SetTextKey(...)</c> on its
    /// serialized <c>TextLocalizedListener</c> — the first name in <see cref="BinderTypeNames"/>,
    /// i.e. a component this method destroys on every row it stamps. Cloning a row whose listener
    /// is already gone hands <c>Refresh</c> a destroyed object and it throws. The user's ModBuild
    /// 169 log carries <b>107 NullReferenceExceptions from this one line</b>, one per row, every
    /// time the options tab is built; they were invisible until ModBuild 136 restored stack traces
    /// and anonymous before that.</para>
    ///
    /// <para>So: deactivate the template for the length of the <c>Instantiate</c> call, strip the
    /// game's binders off the still-inactive clone, and only then switch it on — by which point
    /// there is no <c>ButtonSwitch</c> left to wake. <c>ButtonSwitch</c> itself joined
    /// <see cref="BinderTypeNames"/> for that reason: its Awake has never once completed on one of
    /// these rows, so it drives nothing, and the mod supplies the row's own visuals and handlers.
    /// The template is put back exactly as it was found — it is the game's own live object, not
    /// ours to leave switched off.</para>
    /// </summary>
    private static GameObject StampRow(GameObject? template, Transform parent,
                                       out TMP_Text? title, out Transform? option)
    {
        GameObject row;
        if (template != null)
        {
            bool templateWasActive = template.activeSelf;
            if (templateWasActive)
                template.SetActive(false);
            try
            {
                row = UnityEngine.Object.Instantiate(template, parent);
            }
            finally
            {
                if (templateWasActive)
                    template.SetActive(true);
            }
        }
        else
        {
            row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, worldPositionStays: false);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;
        }

        StripForReuse(row);
        row.SetActive(true);

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
    /// <param name="repeat">
    /// Keep firing while the arrow is held down (see <see cref="ArrowRepeat"/>). Every row passes
    /// true since ModBuild 271; the parameter stays because "does this arrow repeat" is a real
    /// property of an arrow and a future row shape may want a bare one. The argument for turning
    /// it on everywhere is at the call site in <see cref="BuildStepperRow"/>.
    /// </param>
    private static void BuildArrow(Transform parent, bool flip, Action onClick, bool repeat = false)
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

        if (repeat)
            go.AddComponent<ArrowRepeat>().Drive(onClick);
    }

    /// <summary>
    /// PRESS-AND-HOLD REPEAT for a stepper arrow — new work for the 2026-08-22 settings audit's
    /// question (d).
    ///
    /// <para>WHY IT EXISTS. <see cref="BuildArrow"/> is a plain <c>Button.onClick</c>, so a press
    /// is a press, and <c>ConfigSteps</c> used to say so in prose: "The arrows do not repeat when
    /// held … so every press is a press and the count has to stay humane." For two rows the count
    /// was not humane — <c>[WorldUI] TravelButtonOffsetYWindowHeights</c> is 121 presses across its
    /// range and its sibling X is 51 — and those two may NOT become bars (standing user ruling).
    /// This is the third option: hold the arrow and it keeps stepping.</para>
    ///
    /// <para>IT IS ON EVERY ARROW SINCE ModBuild 271, because the constraint it was working around
    /// stopped being a constraint on two rows and became one on all of them: the length steps are
    /// millimetres now, and 174 per-board rows have no bar to cross with. The argument, and the
    /// reason a tap is still a tap, is at the call site in <see cref="BuildStepperRow"/>.</para>
    ///
    /// <para>THE FIRST STEP IS STILL THE BUTTON'S. The <c>onClick</c> stays wired, so a tap
    /// behaves exactly as before (and keeps the game's own press feedback); this component only
    /// adds the REPEATS after the hold delay, which is why nothing fires twice on a short press.</para>
    ///
    /// <para>IT LETS GO OF EVERY WAY A PRESS CAN END. Pointer up, pointer exit and being disabled
    /// all stop it — a VR laser that slips off the arrow mid-press must not leave a value running,
    /// and the options list rebuilds itself on some edits (Apply → Rebuild), which destroys this
    /// object mid-repeat. <see cref="Update"/> is wrapped because an unguarded Update that throws
    /// starves the whole input pump.</para>
    /// </summary>
    private sealed class ArrowRepeat : MonoBehaviour,
                                       UnityEngine.EventSystems.IPointerDownHandler,
                                       UnityEngine.EventSystems.IPointerUpHandler,
                                       UnityEngine.EventSystems.IPointerExitHandler
    {
        /// <summary>How long the arrow must be held before the repeat starts, in seconds.</summary>
        private const float HoldDelay = 0.45f;

        /// <summary>Seconds between repeats once it has started — ~7/s, fast but still countable.</summary>
        private const float RepeatInterval = 0.14f;

        private Action? _fire;
        private bool _down;
        private float _nextFire;

        internal void Drive(Action fire) => _fire = fire;

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData _)
        {
            _down = true;
            _nextFire = Time.unscaledTime + HoldDelay;
        }

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData _) => _down = false;

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) => _down = false;

        private void OnDisable() => _down = false;

        private void Update()
        {
            if (!_down || _fire == null || Time.unscaledTime < _nextFire)
                return;
            _nextFire = Time.unscaledTime + RepeatInterval;
            try
            {
                _fire();
            }
            catch (Exception e)
            {
                _down = false;
                VRLog.Warn("WorldUI", $"VR options tab: an arrow's hold-repeat threw ({e.Message}) — "
                                      + "the repeat stops; the arrow still works as a plain press.");
            }
        }
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

    /// <summary>Scratch for <see cref="BindValueLabels"/>; one list, reused, never handed out.</summary>
    private static readonly System.Collections.Generic.List<TMP_Text> ValueLabelScratch = new(8);

    /// <summary>True once the multi-label line below has been logged — one line, not one per row.</summary>
    private static bool _multiValueLabelLogged;

    /// <summary>
    /// Bind EVERY label a row carries that is not its caption to the setting's live value.
    ///
    /// <para>THIS IS THE SECOND "50/50" REPORT (user, 2026-08-13: "Wenn ein balken in den VR
    /// Einstellungen auf maximum gestellt wird steht da immer noch bei manchen Einstellungen 50/50
    /// statt der eigentliche echte Wert"). The FIRST one (2026-08-11) was the deferred clear in
    /// <see cref="PlaceControl"/>, which made <see cref="ExistingValueLabel"/> answer with the
    /// dying toggle label; that fix stands and is not the cause here.</para>
    ///
    /// <para>WHAT IS LEFT, PROVEN BY ELIMINATION rather than by guessing at the donor prefab:
    /// <see cref="Apply"/> repaints EVERY label in <see cref="ValueLabels"/> after EVERY edit of
    /// ANY row — so a label still reading the donor's baked "50/50" AFTER the player has dragged
    /// that very slider to its maximum cannot be a label this class ever bound. It is a SECOND (or
    /// third) TMP inside the placed control that nothing writes and nothing repaints, and
    /// <c>ExistingValueLabel</c> — "the FIRST label that is not the Title" — binds exactly one.
    /// The donor is built for several: <c>UISliderBar.amountTexts</c> is a
    /// <c>List&lt;TextMeshProUGUI&gt;</c> that its <c>SetAmountText</c> fills with the SAME
    /// "{0}/{1}" string, and the menu's controls also carry an inactive gamepad key-tip label of
    /// their own (the probe dump names one under the dropdown archetype: 'UI Controller Key Tip
    /// (inactive)' → 'Text (TMP)'). Which of them the mod happened to bind decided whether a row
    /// read right — hence "manche".</para>
    ///
    /// <para>THE FIX IS TO STOP CHOOSING. Every non-caption label in the row becomes the readout:
    /// whichever one the prefab actually shows now carries the live value, a hidden one costs
    /// nothing, and no baked donor string can survive anywhere in the row. All of them go into
    /// <see cref="ValueLabels"/>, so all of them follow the drag. The toggle rows are deliberately
    /// NOT routed through here — their single label is a STATE caption ("Ein"/"Aus"), the probe
    /// dump shows the switch carries exactly one, and painting a number into it would be wrong.</para>
    /// </summary>
    private static void BindValueLabels(GameObject row, TMP_Text? title, ConfigCatalog.ConfigItem item,
                                        int component)
    {
        ValueLabelScratch.Clear();
        foreach (TMP_Text candidate in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (candidate == null || ReferenceEquals(candidate, title))
                continue;
            // …EXCEPT AN ARROW'S OWN GLYPH. BuildArrow falls back to a TMP reading "<" / ">" when
            // the menu's arrow sprite could not be harvested, and the bar+arrows row (2026-08-22,
            // question d) is the first row to put arrows and a bound readout in the same row — so
            // without this the two arrows would each be repainted with the value and the row would
            // read "1.25 1.25 1.25". Matched by the object name BuildArrow gives them, which is
            // the only thing that separates a control glyph from a readout here.
            if (IsArrowGlyph(candidate))
                continue;
            ValueLabelScratch.Add(candidate);
        }

        // A donor that carries none at all (a game update that moves the readout out of the
        // control) still gets a readout — the same fallback the single-label version had.
        if (ValueLabelScratch.Count == 0)
            ValueLabelScratch.Add(BuildValueLabel(row.transform));

        string text = ConfigCatalog.ValueText(item, component);
        int shown = 0;
        for (int i = 0; i < ValueLabelScratch.Count; i++)
        {
            TMP_Text label = ValueLabelScratch[i];
            label.text = text;
            ValueLabels.Add((label, () => ConfigCatalog.ValueText(item, component)));
            if (label.gameObject.activeInHierarchy)
                shown++;
        }

        // ONE line, always — not only when the count is >1. If the next hardware log says ONE
        // label and the row STILL reads "50/50", then the stale text is not inside the row at all
        // and the search has to widen; that is exactly the fact this line exists to settle, and it
        // is worth more than a line that stays silent in the case it cannot explain.
        if (!_multiValueLabelLogged)
        {
            _multiValueLabelLogged = true;
            VRLog.Info("WorldUI",
                $"SLIDER VALUE: the game's slider control brings {ValueLabelScratch.Count} text "
                + $"label(s) into a row, {shown} of them visible (first seen on "
                + $"{item.Section}/{item.Key}); ALL of them are now bound to the live value and "
                + "repainted on every edit. Binding only the FIRST is what left the donor's baked "
                + "'50/50' standing on the rows whose visible label was not the first one — and an "
                + "unbound label is never repainted either, so no edit could ever clear it.");
        }

        ValueLabelScratch.Clear();
    }

    /// <summary>Is this label an arrow's own glyph rather than a readout? See BuildArrow.</summary>
    private static bool IsArrowGlyph(TMP_Text label) =>
        label.gameObject != null
        && (string.Equals(label.gameObject.name, "ArrowLeft", StringComparison.Ordinal)
            || string.Equals(label.gameObject.name, "ArrowRight", StringComparison.Ordinal));

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

        string text = HintFor(item, hintKey);
        if (string.IsNullOrEmpty(text))
            return;

        AttachHoverHint(title, text, item.Key);
    }

    /// <summary>
    /// Put <paramref name="text"/> in the game's own tooltip box when this caption is hovered.
    /// The shared tail of <see cref="AttachTooltip"/>, split out so a HEADING (which has no
    /// <see cref="ConfigCatalog.ConfigItem"/> behind it) can carry a hover hint through the very
    /// same mechanism the rows use.
    /// </summary>
    private static void AttachHoverHint(TMP_Text title, string text, string what)
    {
        try
        {
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
            VRLog.Warn("WorldUI", $"VR options tab: the game tooltip for {what} could not be "
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
        Sliders.Clear();
    }
}
