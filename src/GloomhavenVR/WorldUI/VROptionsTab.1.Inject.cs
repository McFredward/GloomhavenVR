using System;
using System.Collections.Generic;
using System.Text;
using AsmodeeNet.Foundation;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — see SettingsPanel.1.Core.cs for why.

/// <summary>
/// "VR Optionen" as a REAL TAB of the game's own options window, alongside Sprache / Anzeige /
/// Schwierigkeit — reachable from the main menu and, in a scenario, from the pause menu's Options
/// button. It replaces the mod's free-floating settings panel and the control-board gear entirely.
///
/// <para>WHY A CLONE AND NOT A PREFAB OF OUR OWN. The tab has to behave like the game's: join the
/// same <c>ToggleGroup</c> (so selecting it deselects the others), animate the same way, register
/// with <c>UIWindowManager</c> as an escapable, and be reachable by the gamepad navigation the
/// window builds over its tabs. All of that lives in serialized references and component wiring
/// that only exist inside the shipped prefab, so the tab toggle and its window are INSTANTIATED
/// FROM A LIVE DONOR TAB and then re-labelled. Nothing about the donor is modified.</para>
///
/// <para>WHY THE VR MODAL PATH NEEDS NO WORK. <c>UIOptionsWindow</c> and <c>UISubmenuGOWindow</c>
/// are already in <see cref="ModalFallback"/>'s family (ModalFallback.1.Core.cs :279/:281), so a
/// tab window built here floats in front of the player in VR by the same route every other option
/// tab already takes. This class deliberately adds no rendering, no placement and no input path.
/// </para>
///
/// <para>REVERSIBILITY. Everything this creates is a mod-owned clone parented into the game's
/// hierarchy; nothing of the game's is re-layered, re-parented or edited. <see cref="Shutdown"/>
/// removes our entry from <c>m_Tabs</c> and destroys the two clones, leaving the window exactly as
/// it shipped. The one game-object write outside our own clones is that removal, and it is undone
/// by the same call that made it.</para>
///
/// <para>THE PROBE. The prefab hierarchy inside a tab window cannot be read from the decompiled
/// sources — it is authored data. <see cref="Probe"/> therefore logs what was actually found the
/// first time the window exists, so the content stages are built against the real thing instead of
/// against a guess. It runs once per session and costs nothing after that.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>The options window we are currently injected into (null = not injected).</summary>
    private static UIOptionsWindow? _host;

    /// <summary>Our cloned tab toggle in the left-hand option list.</summary>
    private static UIMainMenuOption? _toggle;

    /// <summary>Our cloned tab window — the panel that opens when the toggle is selected.</summary>
    private static UISubmenuGOWindow? _window;

    /// <summary>The donor tab's index in <c>m_Tabs</c>, for the log line only.</summary>
    private static int _donorIndex = -1;

    private static bool _probed;
    private static bool _degraded;

    /// <summary>
    /// Root our content is built under — the clone's own child, so the donor's original content
    /// (deactivated, never destroyed) can be restored by simply dropping the clone. Null until the
    /// tab exists.
    /// </summary>
    internal static RectTransform? ContentRoot { get; private set; }

    /// <summary>
    /// The sub-tab bar: a strip pinned to the TOP of the tab window, outside the scroll view.
    ///
    /// <para>It is a COLUMN DOWN THE LEFT, not a strip across the top, because that is the shape
    /// this window already uses: the options window itself is a left column of tabs beside a
    /// content pane, and the sub-chooser reads as part of the menu when it repeats that. The strip
    /// version also had two practical faults — twelve captions across one width came out cramped
    /// and staggered, and it was anchored to the window root rather than the content pane, so it
    /// landed outside the visible panel where nothing could click it.</para>
    ///
    /// <para>Earlier still it was the first ROW of the scrolled list, where it scrolled out of
    /// sight almost immediately — the opposite of what a chooser is for.</para>
    /// </summary>
    internal static RectTransform? TabBarRoot { get; private set; }

    /// <summary>Width of that column.</summary>
    private const float TabColumnWidth = 210f;

    /// <summary>
    /// Clear space between the column and the first setting. The cloned tab option's frame art
    /// overhangs its own rect, so padding the content by the column width alone still left the
    /// buttons grazing the row backgrounds.
    /// </summary>
    private const float TabColumnGutter = 28f;

    /// <summary>True once the tab is live in the game's options window.</summary>
    internal static bool Injected => _host != null && _toggle != null;

    /// <summary>
    /// Idempotent, cheap after the first success. Called every frame from
    /// <c>WorldUIModule.Update</c> (via TickGuard, so a throw here can never starve input).
    ///
    /// <para>Re-injects when the options window is replaced — it is a <c>Singleton</c> that does not
    /// survive every scene, and a stale reference would leave the tab silently missing for the rest
    /// of the session.</para>
    /// </summary>
    internal static void Tick()
    {
        if (_degraded)
            return;

        // Desktop play stays 100% vanilla: no clone, no tab, nothing written.
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        UIOptionsWindow? host = Singleton<UIOptionsWindow>.IsInitialized
            ? Singleton<UIOptionsWindow>.Instance
            : null;

        if (host == null)
        {
            // Window gone (scene change): drop our references so the next one gets a fresh tab.
            if (_host != null)
            {
                VRLog.Info("WorldUI", "VR options tab: the options window went away (scene change) — "
                                      + "will re-inject into the next one.");
                Forget();
            }
            return;
        }

        if (ReferenceEquals(host, _host) && _toggle != null && _window != null)
            return;

        Inject(host);
    }

    /// <summary>
    /// Build the tab. Every failure degrades to "no tab" with one explanatory line — the options
    /// window must keep working exactly as it did, since it is the player's only way to reach the
    /// game's own settings.
    /// </summary>
    private static void Inject(UIOptionsWindow host)
    {
        try
        {
            Probe(host);

            if (host.m_Tabs == null || host.m_Tabs.Count == 0)
            {
                Degrade("the options window has no tabs to clone from");
                return;
            }

            UIOptionsWindow.OptionTab? donor = PickDonor(host, out int donorIndex);
            if (donor == null)
            {
                Degrade("no usable donor tab (need one with both a toggle and a tab window)");
                return;
            }

            _donorIndex = donorIndex;

            UIMainMenuOption? toggle = CloneToggle(donor.OptionToggle);
            if (toggle == null)
            {
                Degrade("the tab toggle could not be cloned");
                return;
            }

            UISubmenuGOWindow? window = CloneWindow(donor.TabWindow);
            if (window == null)
            {
                UnityEngine.Object.Destroy(toggle.gameObject);
                Degrade("the tab window could not be cloned");
                return;
            }

            _host = host;
            _toggle = toggle;
            _window = window;

            // The template comes from a LIVE row, so it has to be stamped while the donor tabs are
            // still intact — and the content is built on first show, not now, because the config
            // registry is not necessarily complete at injection time.
            CaptureRowTemplates(host);
            HookContentBuild(window);

            // Join the game's own tab bookkeeping LAST, so a half-built tab is never reachable.
            host.m_Tabs.Add(new UIOptionsWindow.OptionTab { OptionToggle = toggle, TabWindow = window });
            host.InitializeOption(toggle, window);

            VRLog.Info("WorldUI",
                $"VR options tab: injected as tab #{host.m_Tabs.Count} (cloned from tab #{donorIndex} "
                + $"'{donor.OptionToggle.name}'). Label '{Loc.Mod("vr_options")}'. Reachable from the "
                + "main menu and from the pause menu's Options button.");
        }
        catch (Exception e)
        {
            Degrade($"injection threw: {e}");
        }
    }

    /// <summary>
    /// A SCROLLING tab is the only usable donor, and the probe is what settled that. The window
    /// comes in two authored shapes: General/Audio/Combat Log put their rows straight into a
    /// <c>VerticalLayoutGroupExtended</c> and simply run off the bottom when there are more of them
    /// than fit, while Video/Controls/Perfomance wrap theirs in an <c>ExtendedScrollRect</c> with a
    /// masked viewport and the game's own scrollbar art. The mod has far more settings than fit on
    /// one screen, so cloning a non-scrolling tab would produce a list whose lower half is simply
    /// unreachable — and it would look wrong besides, since the game's own long lists all scroll.
    ///
    /// <para>Within the scrolling group an INACTIVE tab is preferred, which is the opposite of the
    /// first version and the fix for the game's own keybindings appearing above the mod's settings.
    /// The Controls tab POPULATES ITSELF: cloning it cloned whatever rebuilds that list, so the
    /// donor rows were duly deactivated and then promptly rebuilt into the very content transform
    /// the mod writes into. A tab the game hides on this platform (Perfomance, console-only) has
    /// nothing maintaining it and nothing the player can miss.</para>
    /// </summary>
    private static UIOptionsWindow.OptionTab? PickDonor(UIOptionsWindow host, out int index)
    {
        index = -1;
        UIOptionsWindow.OptionTab? scrollingActive = null, plain = null;
        int scrollingActiveIndex = -1, plainIndex = -1;

        for (int i = 0; i < host.m_Tabs.Count; i++)
        {
            UIOptionsWindow.OptionTab tab = host.m_Tabs[i];
            if (tab?.OptionToggle == null || tab.TabWindow == null)
                continue;

            bool active = tab.OptionToggle.gameObject.activeSelf;

            if (tab.TabWindow.GetComponentInChildren<ScrollRect>(true) != null)
            {
                if (!active)
                {
                    index = i;
                    return tab;
                }

                if (scrollingActive == null)
                {
                    scrollingActive = tab;
                    scrollingActiveIndex = i;
                }
                continue;
            }

            if (plain == null && active)
            {
                plain = tab;
                plainIndex = i;
            }
        }

        if (scrollingActive != null)
        {
            VRLog.Warn("WorldUI", "VR options tab: only ACTIVE scrolling donors exist — cloning one "
                                  + "the game still maintains. If its own rows reappear above the "
                                  + "mod's settings, that is why; the per-show sweep hides them.");
            index = scrollingActiveIndex;
            return scrollingActive;
        }

        if (plain != null)
        {
            VRLog.Warn("WorldUI", "VR options tab: no scrolling donor tab found — falling back to a "
                                  + "plain one. The settings list will not scroll, so anything past "
                                  + "the bottom of the panel will be out of reach.");
        }

        index = plainIndex;
        return plain;
    }

    /// <summary>
    /// Clone the tab toggle next to its donor, re-label it, and make sure nothing re-labels it
    /// back. The game drives option captions through <c>TextLocalizedListener</c>, which rewrites
    /// the text on every language change — left alive on the clone it would silently replace our
    /// caption with the donor's the first time the player switches language.
    /// </summary>
    private static UIMainMenuOption? CloneToggle(UIMainMenuOption donor)
    {
        var clone = UnityEngine.Object.Instantiate(donor.gameObject, donor.transform.parent)
            .GetComponent<UIMainMenuOption>();
        if (clone == null)
            return null;

        clone.name = "GloomhavenVR.OptionsTab";
        clone.transform.SetAsLastSibling();
        clone.gameObject.SetActive(true);

        foreach (TextLocalizedListener listener in clone.GetComponentsInChildren<TextLocalizedListener>(true))
            UnityEngine.Object.Destroy(listener);

        SetCaption(clone, Loc.Mod("vr_options"));

        // A donor that happened to be non-interactable (Perfomance outside the main menu) would
        // hand us its locked state along with its wiring.
        clone.IsInteractable = true;
        return clone;
    }

    /// <summary>
    /// Caption via the option's own <c>text</c> field when it has one, else the first TMP label in
    /// the clone — the field is the authored one, the sweep is what keeps this working if a game
    /// update re-authors the row.
    /// </summary>
    private static void SetCaption(UIMainMenuOption option, string caption)
    {
        TextMeshProUGUI? label = option.text;
        if (label == null)
            label = option.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label == null)
        {
            VRLog.Warn("WorldUI", "VR options tab: the cloned toggle has no TMP label — the tab will "
                                  + "carry the donor's caption. Harmless, but it will read wrong.");
            return;
        }

        label.text = caption;
    }

    /// <summary>
    /// Clone the tab window and hand back a content root INSIDE the donor's own scroll view, so the
    /// masked viewport, the scrollbar art and the wheel/drag behaviour are the game's rather than
    /// ours. Only the donor's ROWS are deactivated — never destroyed, and never the scroll
    /// machinery around them.
    ///
    /// <para>Deactivating rather than destroying keeps the clone's serialized references intact: the
    /// window's own components already ran their <c>Awake</c> and hold pointers into this hierarchy,
    /// and destroying children would leave them pointing at dead objects.</para>
    /// </summary>
    private static UISubmenuGOWindow? CloneWindow(UISubmenuGOWindow donor)
    {
        var clone = UnityEngine.Object.Instantiate(donor.gameObject, donor.transform.parent)
            .GetComponent<UISubmenuGOWindow>();
        if (clone == null)
            return null;

        clone.name = "GloomhavenVR.OptionsTabWindow";

        ScrollRect? scroll = clone.GetComponentInChildren<ScrollRect>(true);
        RectTransform holder = scroll != null && scroll.content != null
            ? scroll.content
            : (RectTransform)clone.transform;

        var deactivated = new List<string>();
        for (int i = 0; i < holder.childCount; i++)
        {
            Transform child = holder.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            child.gameObject.SetActive(false);
            deactivated.Add(child.name);
        }

        ContentRoot = BuildContentRoot(holder, scrolled: scroll != null && scroll.content != null);
        TabBarRoot = BuildTabBar(scroll);
        clone.gameObject.SetActive(false);

        LogCloneHierarchy(clone);

        VRLog.Info("WorldUI",
            $"VR options tab: window cloned from '{donor.name}'. "
            + (scroll != null
                ? $"Content sits inside the donor's own scroll view ('{holder.name}'), so the viewport "
                  + "mask and scrollbar are the game's. "
                : "NO scroll view on this donor — the list cannot scroll. ")
            + $"Donor rows deactivated ({deactivated.Count}: {string.Join(", ", deactivated.ToArray())}); "
            + "nothing was destroyed, so dropping the clone restores everything.");
        return clone;
    }

    /// <summary>
    /// Our own root under the donor's holder. Inside a scroll view it must SIZE ITSELF to its rows
    /// (a stretched rect would report a fixed height and the scroll range would stay zero no matter
    /// how many settings are added); outside one it simply fills the window.
    /// </summary>
    private static RectTransform BuildContentRoot(RectTransform holder, bool scrolled)
    {
        var root = (RectTransform)new GameObject("GloomhavenVR.Content", typeof(RectTransform)).transform;
        root.SetParent(holder, worldPositionStays: false);

        if (scrolled)
        {
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.offsetMin = new Vector2(0f, 0f);
            root.offsetMax = new Vector2(0f, 0f);

            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            // The gap the sub-tab column sits in. Padding OUR content is the one way to reserve it
            // that no animation of the game's can undo — see BuildTabBar.
            layout.padding = new RectOffset(Mathf.RoundToInt(TabColumnWidth + TabColumnGutter), 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }
        else
        {
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
        }

        return root;
    }

    /// <summary>
    /// Pin the sub-tab strip above the scroll area and shorten the scroll area by its height.
    ///
    /// <para>The ScrollRect sits ON the tab's "Main Area" (the probe), so that transform IS the
    /// scrolling region: moving its top edge down and putting the strip in the gap needs no
    /// reparenting of anything the game owns.</para>
    /// </summary>
    private static RectTransform? BuildTabBar(ScrollRect? scroll)
    {
        if (scroll == null)
            return null;

        // NOTHING THE GAME OWNS IS MOVED. The first version insetting the pane's left edge to make
        // room, and the column then drew straight over the settings: "Main Area" carries a
        // LeanTweenGUIAnimator that tweens the pane every time the tab is shown, and that tween
        // wrote the offsets back. Room is made instead by PADDING THE MOD'S OWN CONTENT (see
        // BuildContentRoot), which nothing else touches.
        //
        // The column is parented to the pane itself so it inherits the pane's exact rect and rides
        // that same show animation. It is not under the Viewport, so the scroll mask does not clip
        // it, and it is created after the Viewport so it draws over the padding strip rather than
        // under it.
        var area = (RectTransform)scroll.transform;

        var column = (RectTransform)new GameObject("GloomhavenVR.SubTabs", typeof(RectTransform)).transform;
        column.SetParent(area, worldPositionStays: false);
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.sizeDelta = new Vector2(TabColumnWidth, 0f);
        column.anchoredPosition = Vector2.zero;

        var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 2f;
        layout.padding = new RectOffset(0, 6, 0, 0);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return column;
    }

    /// <summary>
    /// Dump the finished clone once. The player reports "control options at the very top that have
    /// nothing to do with VR", and the donor is the Controls tab — so something authored is still
    /// showing. Guessing at which node it is from a screenshot is how the last two rounds were
    /// spent; this says it outright.
    /// </summary>
    private static void LogCloneHierarchy(UISubmenuGOWindow clone)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("VR options tab CLONE — '").Append(clone.name).Append('\'');
            DescribeChildren(sb, clone.transform, "    ", depth: 3);
            VRLog.Info("WorldUI", sb.ToString());
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: clone dump threw ({e.Message}).");
        }
    }

    /// <summary>
    /// One-shot dump of what the options window actually looks like. This exists because the tab
    /// contents are authored prefab data that no decompiled source can show: the alternative to
    /// logging it once is guessing at it repeatedly.
    /// </summary>
    private static void Probe(UIOptionsWindow host)
    {
        if (_probed)
            return;
        _probed = true;

        try
        {
            var sb = new StringBuilder();
            sb.Append("VR options tab PROBE — options window '").Append(host.name).Append("', ");
            sb.Append(host.m_Tabs?.Count ?? 0).Append(" tab(s), toggle group ");
            sb.Append(host.m_ToggleGroup == null ? "MISSING" : host.m_ToggleGroup.name).Append('.');

            if (host.m_Tabs != null)
            {
                for (int i = 0; i < host.m_Tabs.Count; i++)
                {
                    UIOptionsWindow.OptionTab tab = host.m_Tabs[i];
                    sb.Append("\n  [").Append(i).Append("] toggle=");
                    sb.Append(tab?.OptionToggle == null ? "null" : tab.OptionToggle.name);
                    if (tab?.OptionToggle != null)
                        sb.Append(tab.OptionToggle.gameObject.activeSelf ? " (active)" : " (INACTIVE)");
                    sb.Append(" window=");
                    sb.Append(tab?.TabWindow == null ? "null" : tab.TabWindow.name);

                    if (tab?.TabWindow != null)
                    {
                        var w = tab.TabWindow.GetComponent<UIWindow>();
                        if (w != null)
                            sb.Append(" id=").Append(w.ID);
                        DescribeChildren(sb, tab.TabWindow.transform, "      ", depth: 2);
                    }
                }
            }

            ProbeRowArchetypes(sb, host);
            VRLog.Info("WorldUI", sb.ToString());
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: probe threw ({e.Message}) — injection continues.");
        }
    }

    /// <summary>
    /// Dump ONE real example of each control shape the mod needs to reproduce — a toggle row, a
    /// slider row and a dropdown row — with their full sub-tree.
    ///
    /// <para>These are the rows the content stage clones, and cloning is only safe if the anatomy is
    /// known: which child carries the caption, which carries the control, and which game component
    /// is bound to the setting behind it and therefore has to go. Searching by COMPONENT TYPE
    /// rather than by name means this keeps finding them if a game update renames the rows.</para>
    /// </summary>
    private static void ProbeRowArchetypes(StringBuilder sb, UIOptionsWindow host)
    {
        Transform? toggleRow = null, sliderRow = null, dropdownRow = null;

        for (int i = 0; i < host.m_Tabs.Count; i++)
        {
            UISubmenuGOWindow? window = host.m_Tabs[i]?.TabWindow;
            if (window == null)
                continue;

            toggleRow ??= FindRowWith<Toggle>(window.transform);
            sliderRow ??= FindRowWith<Slider>(window.transform);
            dropdownRow ??= FindRowWith<TMP_Dropdown>(window.transform);
        }

        AppendArchetype(sb, "TOGGLE row", toggleRow);
        AppendArchetype(sb, "SLIDER row", sliderRow);
        AppendArchetype(sb, "DROPDOWN row", dropdownRow);
    }

    /// <summary>
    /// The ROW is the layout item, not the widget: walk up from the control to the child that sits
    /// directly under a layout group, because that is the unit the content stage instantiates.
    /// </summary>
    private static Transform? FindRowWith<T>(Transform root) where T : Component
    {
        T[] found = root.GetComponentsInChildren<T>(true);
        if (found.Length == 0)
            return null;

        Transform node = found[0].transform;
        while (node.parent != null && node.parent != root)
        {
            if (node.GetComponent<LayoutElement>() != null)
                return node;
            node = node.parent;
        }
        return found[0].transform;
    }

    private static void AppendArchetype(StringBuilder sb, string what, Transform? row)
    {
        sb.Append("\n  ").Append(what).Append(": ");
        if (row == null)
        {
            sb.Append("none found in any tab.");
            return;
        }

        sb.Append('\'').Append(row.name).Append("' under '")
          .Append(row.parent == null ? "?" : row.parent.name).Append('\'');
        Component[] own = row.GetComponents<Component>();
        sb.Append("  [");
        for (int c = 0; c < own.Length; c++)
        {
            if (c > 0)
                sb.Append(", ");
            sb.Append(own[c] == null ? "<missing>" : own[c].GetType().Name);
        }
        sb.Append(']');
        DescribeChildren(sb, row, "        ", depth: 3);
    }

    /// <summary>Component-annotated child listing, depth-limited: this is a diagnostic, not a dump.</summary>
    private static void DescribeChildren(StringBuilder sb, Transform parent, string indent, int depth)
    {
        if (depth <= 0)
            return;

        for (int i = 0; i < parent.childCount && i < 12; i++)
        {
            Transform child = parent.GetChild(i);
            sb.Append('\n').Append(indent).Append(child.name);
            if (!child.gameObject.activeSelf)
                sb.Append(" (inactive)");

            Component[] components = child.GetComponents<Component>();
            sb.Append("  [");
            for (int c = 0; c < components.Length; c++)
            {
                if (c > 0)
                    sb.Append(", ");
                sb.Append(components[c] == null ? "<missing>" : components[c].GetType().Name);
            }
            sb.Append(']');

            DescribeChildren(sb, child, indent + "  ", depth - 1);
        }

        if (parent.childCount > 12)
            sb.Append('\n').Append(indent).Append("… ").Append(parent.childCount - 12).Append(" more");
    }

    /// <summary>Log the first failure, then stay silent for the rest of the session.</summary>
    private static void Degrade(string reason)
    {
        if (_degraded)
            return;
        _degraded = true;
        Forget();
        VRLog.Warn("WorldUI", $"VR options tab: not available — {reason}. The game's own options "
                              + "window is untouched and every VR setting remains editable in "
                              + "BepInEx/config/dev.gloomhavenvr*.cfg.");
    }

    /// <summary>Drop references without touching anything (the objects are already gone).</summary>
    private static void Forget()
    {
        _host = null;
        _toggle = null;
        _window = null;
        ContentRoot = null;
        TabBarRoot = null;
        _donorIndex = -1;
    }

    /// <summary>
    /// Remove the tab and destroy both clones — the exact inverse of <see cref="Inject"/>. Called
    /// from <c>WorldUIModule.OnDestroy</c>; safe to call when nothing was ever injected.
    /// </summary>
    internal static void Shutdown()
    {
        try
        {
            if (_host != null && _host.m_Tabs != null && _toggle != null)
            {
                for (int i = _host.m_Tabs.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(_host.m_Tabs[i]?.OptionToggle, _toggle))
                        _host.m_Tabs.RemoveAt(i);
                }
            }

            if (_toggle != null)
                UnityEngine.Object.Destroy(_toggle.gameObject);
            if (_window != null)
                UnityEngine.Object.Destroy(_window.gameObject);
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: teardown threw ({e.Message}).");
        }
        finally
        {
            Forget();
        }
    }
}
