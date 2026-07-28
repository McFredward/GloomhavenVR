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
    /// Prefer a tab that is ACTIVE and fully wired: an inactive donor (the game hides Perfomance on
    /// PC and Difficulty/HouseRules outside a campaign) would clone its inactive state and its
    /// hidden-tab quirks along with it.
    /// </summary>
    private static UIOptionsWindow.OptionTab? PickDonor(UIOptionsWindow host, out int index)
    {
        index = -1;
        UIOptionsWindow.OptionTab? fallback = null;
        int fallbackIndex = -1;

        for (int i = 0; i < host.m_Tabs.Count; i++)
        {
            UIOptionsWindow.OptionTab tab = host.m_Tabs[i];
            if (tab?.OptionToggle == null || tab.TabWindow == null)
                continue;

            if (tab.OptionToggle.gameObject.activeSelf)
            {
                index = i;
                return tab;
            }

            if (fallback == null)
            {
                fallback = tab;
                fallbackIndex = i;
            }
        }

        index = fallbackIndex;
        return fallback;
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
    /// Clone the tab window and DEACTIVATE (never destroy) the donor's content, so our own root is
    /// the only thing visible. Deactivating keeps the clone's serialized references intact —
    /// destroying children would leave the window's own components pointing at dead objects, and
    /// <c>UISubmenuGOWindow.Awake</c> has already run by then.
    /// </summary>
    private static UISubmenuGOWindow? CloneWindow(UISubmenuGOWindow donor)
    {
        var clone = UnityEngine.Object.Instantiate(donor.gameObject, donor.transform.parent)
            .GetComponent<UISubmenuGOWindow>();
        if (clone == null)
            return null;

        clone.name = "GloomhavenVR.OptionsTabWindow";

        var host = (RectTransform)clone.transform;
        var deactivated = new List<string>();
        for (int i = 0; i < host.childCount; i++)
        {
            Transform child = host.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            child.gameObject.SetActive(false);
            deactivated.Add(child.name);
        }

        var content = new GameObject("GloomhavenVR.Content", typeof(RectTransform));
        var rect = (RectTransform)content.transform;
        rect.SetParent(host, worldPositionStays: false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        ContentRoot = rect;

        clone.gameObject.SetActive(false);

        VRLog.Info("WorldUI", $"VR options tab: window cloned; donor content deactivated "
                              + $"({deactivated.Count}: {string.Join(", ", deactivated.ToArray())}). "
                              + "Content root is a child of the CLONE, so dropping the clone restores "
                              + "everything.");
        return clone;
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

            VRLog.Info("WorldUI", sb.ToString());
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: probe threw ({e.Message}) — injection continues.");
        }
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
