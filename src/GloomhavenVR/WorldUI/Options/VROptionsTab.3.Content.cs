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

        /// <summary>The TEST TRIGGER page — buttons, not settings. See VROptionsTab.9.TestTriggers.cs.</summary>
        AdvancedTriggers,

        /// <summary>The CHEATS page. See VROptionsTab.Cheats.cs.</summary>
        AdvancedCheats,   // CHEATS (temporary) — removal step 2, see VROptionsTab.Cheats.cs
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
        // ModBuild 437's full-pane ground is taken OFF here, on every show (idempotent — see
        // VROptionsTab.10.Skin.cs). It was "einfarbig" and "zu groß" and the user rejected it; the
        // backing is per-row now, on the rectangular elements that carry text. The removal runs on
        // every show for the same reason its predecessor was asserted on every show: the pane is
        // re-parented and re-shown by machinery this file does not own, and a removal that only
        // happens if one particular call ran is a removal that sometimes did not.
        //
        // NEVER-EMPTY GUARD: Rebuild's own TickGuard catches by ABORTING the rest of the callback,
        // so an unguarded throw this early would cost the whole list rather than the decoration.
        // A window wearing a ground nobody wanted is a complaint; a window with no rows is the
        // standing ruling, and decoration may never outrank content.
        try
        {
            RemoveLegacyGround();
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: the ModBuild 437 window ground could not be "
                                  + $"removed ({e.Message}) — the menu opens as that build shipped "
                                  + "it, with one opaque plate behind the whole pane.");
        }
        ClearRows();
        HideForeignContent();

        BuildCategoryStrip();

        int rows = _view switch
        {
            View.AdvancedIndex => BuildAdvancedIndex(),
            View.AdvancedTopic => BuildTopic(_category),
            View.AdvancedTriggers => BuildTestTriggers(),
            View.AdvancedCheats => BuildCheatsPage(),   // CHEATS (temporary) — removal step 3
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
                // An ACTION row has no ConfigEntry, so there is nothing to look up and nothing to
                // filter on: it draws whenever its section draws. _sectionItems stays the CONFIG
                // rows only — it is what VariantNote reads, and an action has no variant.
                if (entry.IsAction)
                {
                    _sectionEntries.Add(entry);
                    continue;
                }
                ConfigCatalog.ConfigItem? item = Lookup(entry.Section, entry.Key);
                if (item == null || !IsRowVisible(item))
                    continue;
                _sectionItems.Add(item);
                _sectionEntries.Add(entry);
            }
            if (_sectionEntries.Count == 0)
                continue;

            BuildHeader(ContentRoot, section.Label);
            string? note = _sectionItems.Count > 0 ? VariantNote(_sectionItems) : null;
            if (note != null)
                BuildNote(ContentRoot, note);
            // No copy-from-variant rows here, deliberately: bulk-copying a whole tuning block is
            // a power move that belongs next to the tuning itself (the Debug topics, where the
            // rows still appear). In a curated tab it sat between two everyday choices and
            // overwrote a dozen settings on one press.

            // _sectionEntries is the DRAW ORDER (config rows and action rows interleaved as the
            // list declares them); _sectionItems is the config subset, consumed in step.
            int configIndex = 0;
            for (int i = 0; i < _sectionEntries.Count; i++)
            {
                CuratedEntry entry = _sectionEntries[i];
                if (entry.IsAction)
                {
                    // asAction: the settings-row path, i.e. the whole row is the button. The tab
                    // template carries a Toggle, which on a one-shot action reads as a checkbox
                    // the player is supposed to tick — the exact confusion this row replaces.
                    BuildLinkRow(ContentRoot, entry.Caption, entry.OnPress!, asAction: true);
                    rows++;
                    continue;
                }
                rows += BuildItem(_sectionItems[configIndex++], entry.Caption, entry.HintKey);
            }
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

        // THE TEST TRIGGERS, LAST AND BY THEMSELVES. They are not a catalog topic — there is no
        // ConfigEntry behind any of them — so they cannot come out of the loop above; and last is
        // where the one page that DOES something rather than editing something belongs, below every
        // page that only edits. The user asked for exactly this door ("im Erweitert Menu").
        BuildLinkRow(ContentRoot, Loc.Mod("vr_tt_page"), () =>
        {
            _view = View.AdvancedTriggers;
            TickGuard.Run("VROptionsTab.Triggers", Rebuild, "WorldUI");
        });
        rows++;

        rows += BuildCheatsIndexLink();   // CHEATS (temporary) — removal step 4

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
            }, asAction: true);
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

        // THREE topics are HAND-ARRANGED (user report 2026-08: "Aktuell sucht man dort
        // immer rum ohne wirklich zu finden wonach man sucht"): the automatic key-prefix grouping
        // below makes a dozen leading-word clusters out of an oversized section and then folds the
        // small ones into one "Allgemein" grab-bag. The control board page got the explicit
        // two-level heading tree first (VROptionsTab.6.BoardTopic.cs); the 2026-08 menu overhaul
        // (audit 05, S3 — the two largest topics had the same disease) gave "Menüs & Tafeln" and
        // "Karten & Fächer" theirs (VROptionsTab.7.TopicTrees.cs). Every other topic keeps the
        // automatic grouping, which maintains itself.
        if (topic == ConfigCatalog.ConfigTopic.BoardGeometry)
            return BuildBoardTopicBody();
        if (topic == ConfigCatalog.ConfigTopic.Panels)
            return BuildTreeTopicBody(topic, PanelsTree);
        if (topic == ConfigCatalog.ConfigTopic.Cards)
            return BuildTreeTopicBody(topic, CardsTree);

        IReadOnlyList<ConfigCatalog.ConfigGroup> groups = ConfigCatalog.Groups(topic);
        int rows = 0;

        for (int g = 0; g < groups.Count; g++)
        {
            ConfigCatalog.ConfigGroup group = groups[g];
            if (group.Items.Count == 0)
                continue;

            // COUNT BEFORE HEADING. Filtering per-variant entries down to the selected board or hand
            // style — or dependent rows down to satisfied parents — can empty a group completely,
            // and a heading over nothing reads as a setting that failed to load.
            int visible = 0;
            for (int i = 0; i < group.Items.Count; i++)
            {
                if (IsRowVisible(group.Items[i]))
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
                if (!IsRowVisible(entry))
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

        // Erweitert last, and set apart by being last: the everyday tabs read as the menu, and
        // the tuning constants are one deliberate step further in. (Key kept as "cat_debug" —
        // the 2026-08 overhaul renamed only the TEXT to "Erweitert"/"Advanced", audit 05 S1.)
        BuildCategoryButton(bar, AdvancedTabIndex, Loc.Mod("cat_debug"));

        // WHAT THE COLUMN ACTUALLY SAYS, once, at a printed tier — the state after the tab
        // captions have been through NoOrphanCaption. It answers two of the 2026-09-02 items from
        // a log file instead of from a squint at a screenshot: whether "Umgebung" arrived as one
        // word, and whether any remaining caption still carries a fusible symbol (those show as
        // <NBSP>, and a bare "&" between two spaces would mean the rule did not run). It CANNOT
        // say how the column renders — a caption that fits the string test can still be shrunk to
        // 9pt by the fitter — so it narrows the question rather than answering it.
        var captions = new System.Text.StringBuilder(160);
        for (int i = 0; i < bar.childCount; i++)
        {
            TMP_Text? text = bar.GetChild(i).GetComponentInChildren<TMP_Text>(true);
            if (text == null)
                continue;
            if (captions.Length > 0)
                captions.Append(" | ");
            // ESCAPES, not the characters: a literal U+00A0 in this source line would be
            // invisible to every reviewer and one careless reformat away from an ordinary
            // space, at which point the line would silently stop telling the two apart.
            captions.Append(text.text.Replace("\u00A0", "<NBSP>").Replace("\n", "<NL>"));
        }
        // HW-VERIFY
        VRLog.Note("WorldUI", $"VR options tab: sub-tab column built, {bar.childCount} tab(s) — {captions}");
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

            // WRAP, THEN SHRINK, RATHER THAN TRUNCATE. Ellipsis turned the captions into
            // "Messung...", "Bild&D..." — names that no longer say which category they are,
            // which is the entire job of this column. Shrink-only fixed that but drove the
            // two-word names ("Avatar & Mehrspieler") toward the 9pt floor, so the fit is now
            // against the wrapped text: the long names carry an explicit break in their Loc
            // strings (cat_boardcards, cat_avatar_mp) and get the font of their longest line.
            FitTabCaption(label);
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
        // THE `index == SelectedTabIndex` GUARD THAT STOOD HERE IS GONE, and that one deletion is
        // the whole of user request (b) — see SelectTabRoot. uGUI already delivers the press on
        // the tab you are already inside; this listener was throwing it away.
        toggle.onValueChanged.AddListener(on =>
        {
            if (on)
                SelectTabRoot(index);
        });

        CategoryToggles.Add((toggle, index));
    }

    /// <summary>
    /// THE LEFT COLUMN IS "UP ONE LEVEL": pressing a tab shows that tab's ROOT page, whether or
    /// not it is the tab you are already in.
    ///
    /// <para>USER REQUEST, 2026-09-02, verbatim: <i>"Wenn ich in einem Sub-Menu in 'Erweitert' bin
    /// und dann links wieder auf den 'Erweitert' Knopf drücke erwarte ich, dass ich wieder zu der
    /// Übersicht des Erweitert-Menus komme. Aktuell komm ich nur zurück wenn ich ganz oben auf
    /// '&lt; Erweitert' drücke - ich will es links aber aber auch zusätzlich."</i></para>
    ///
    /// <para>STATED AS A RULE FOR EVERY TAB, not as a special case for Erweitert. "Click the
    /// section you are in to go to the top of that section" is how every sidebar the player has
    /// ever used behaves, and writing it per-tab would mean the next tab that grows a sub-page
    /// silently does not have it. Concretely: from a topic page, the trigger page or the cheats
    /// page, pressing "Erweitert" lands on the Erweitert index; pressing a curated tab you are
    /// already on is a no-op because a curated tab has no deeper page to come back from — and if
    /// one ever gets a sub-page, it inherits the behaviour from this method rather than from a
    /// new branch.</para>
    ///
    /// <para>THE BREADCRUMB STAYS ("ich will es links aber aber auch zusätzlich" — the word is
    /// ZUSÄTZLICH, additionally). <c>BuildTopic</c> still draws "‹ Erweitert" at the top of every
    /// topic page and it is untouched.</para>
    ///
    /// <para>WHY THE FIX IS A DELETION AND NOT A NEW CLICK PATH — verified against the game's own
    /// <c>UnityEngine.UI.dll</c> rather than reasoned about, because the obvious reading is wrong.
    /// It looks as though pressing the lit toggle in a group with <c>allowSwitchOff = false</c>
    /// cannot raise an event at all. It does. <c>Toggle.OnPointerClick</c> flips the field, so
    /// <c>Set(false)</c> is entered with <c>m_IsOn == true</c>; the group clause then finds no
    /// other toggle on, forces <c>m_IsOn</c> back to <c>true</c> — and <c>onValueChanged.Invoke(
    /// m_IsOn)</c> runs unconditionally at the end of that same branch. So the press was arriving
    /// here all along, and the listener's own <c>index == SelectedTabIndex</c> early-return was
    /// what swallowed it. Removing that guard is the entire mechanism; nothing new listens for a
    /// click, and no extra <c>IPointerClickHandler</c> is attached (one was written and then
    /// deleted — it would have been a second handler firing on every real tab switch, justified by
    /// a claim about uGUI that the decompiled Set() disproves).</para>
    ///
    /// <para>IDEMPOTENT ANYWAY. Each branch returns before rebuilding when the view is already the
    /// one asked for, which is what makes pressing the lit CURATED tab free — a curated tab has no
    /// deeper page, so its root is the page already on screen.</para>
    /// </summary>
    private static void SelectTabRoot(int index)
    {
        // Captured BEFORE the state changes: "was the tab I pressed already the lit one" is what
        // separates an ordinary switch from the up-one-level press this round adds, and after the
        // assignment below the two are indistinguishable.
        bool wasAlreadyInside = index == SelectedTabIndex;
        View from = _view;

        if (index == AdvancedTabIndex)
        {
            if (_view == View.AdvancedIndex)
                return;
            _view = View.AdvancedIndex;
        }
        else
        {
            if (index < 0 || index >= Curated.Length)
                return;
            if (_view == View.Curated && _curated == index)
                return;
            _view = View.Curated;
            _curated = index;
        }

        if (wasAlreadyInside)
        {
            // HW-VERIFY
            VRLog.Note("WorldUI", $"VR options tab: left column pressed on the tab already open "
                                  + $"(#{index}) — went up one level, {from} -> {_view}.");
        }

        TickGuard.Run("VROptionsTab.TabRoot", Rebuild, "WorldUI");
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
            // THE MOD'S OWN GROUND IS NOT FOREIGN — kept although the plate is no longer built. On
            // a donor with no scroll view the content root is a direct child of the pane, so this
            // sweep's holder IS the pane, and this row is what stopped ModBuild 437's ground being
            // swept off as a donor leftover on the very rebuild that created it. RemoveLegacyGround
            // destroys the plate immediately, so on a fresh session nothing matches; a plate left
            // behind by a hot-reloaded assembly must be DESTROYED by that method rather than merely
            // deactivated here, which is why the skip stays. Matched by name because that is the
            // only handle a sweep over a holder it did not build can have.
            if (string.Equals(child.name, GroundName, StringComparison.Ordinal))
                continue;
            child.gameObject.SetActive(false);
            hidden++;
        }

        if (hidden > 0)
            VRLog.Info("WorldUI", $"VR options tab: hid {hidden} row block(s) the donor tab rebuilt "
                                  + "into the shared content — the game's own rows, not the mod's.");
    }
}
