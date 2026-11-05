using System.Collections.Generic;
using GLOOM;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Original results presentation subjects; never a second results/ready controller.</summary>
internal static class ScenarioResultsNative
{
    internal static UIResultsManager? Manager => Singleton<UIResultsManager>.IsInitialized
        ? Singleton<UIResultsManager>.Instance : null;
    internal static UIWindow? Window => Manager != null ? Manager.myWindow : null;

    internal static bool TryRead(out ScenarioResultsParts parts)
    {
        UIResultsManager? manager = Manager;
        UIWindow? window = manager != null ? manager.myWindow : null;
        if (manager == null || window == null) { parts = default; return false; }
        var adventure = manager as UINewAdventureResultsManager;
        UIReadyToggle? ready = null;
        if (window.IsOpen && FFSNetwork.IsOnline && Singleton<UIReadyToggle>.IsInitialized)
        {
            UIReadyToggle candidate = Singleton<UIReadyToggle>.Instance;
            if (candidate != null && candidate.readyUpToggleState == EReadyUpToggleStates.NotSet
                && candidate.ReadyUpType == UIReadyToggle.EReadyUpType.Player)
                ready = candidate;
        }
        parts = new ScenarioResultsParts(manager, window, adventure?.header,
            adventure?.resultsPanel, adventure?.statsPanel, adventure?.buttonsContainer,
            adventure?.header != null ? adventure.header.highlightAnimator : null,
            adventure?.showStatsAnimation, ready);
        return true;
    }

    internal static HelpBox? ClientWaitHelp
    {
        get
        {
            if (!FFSNetwork.IsClient || !Singleton<HelpBox>.IsInitialized) return null;
            HelpBox help = Singleton<HelpBox>.Instance;
            // This singleton serves many unrelated tips. Only the exact native
            // results wait-for-host message yields to the common public picture.
            if (help == null || help.myWindow == null || !help.myWindow.IsOpen || help.m_HelpInfo == null)
                return null;
            return help.m_HelpInfo.keybaordLines == LocalizationManager.GetTranslation("Consoles/GUI_WAIT_FOR_HOST_TIP")
                && help.m_HelpInfo.keyboardTitle == LocalizationManager.GetTranslation("Consoles/GUI_MULTIPLAYER_TIP_TITLE")
                ? help : null;
        }
    }

    /// <summary>Stable native modules. A binding excludes another declared module's root;
    /// oversized dynamic collections may be split further without changing native state.</summary>
    internal static void CollectRoots(List<ScenarioResultsRoot> roots)
    {
        roots.Clear();
        if (!TryRead(out ScenarioResultsParts p)) return;
        Add(roots, 1, p.Window.transform);
        Add(roots, 2, p.Header != null ? p.Header.transform : null);
        Add(roots, 3, p.Results != null ? p.Results.transform : null);
        Add(roots, 4, p.Stats != null ? p.Stats.transform : null);
        Add(roots, 5, p.Buttons != null ? p.Buttons.transform : null);
        Add(roots, 6, p.Ready != null ? p.Ready.transform : null);
    }

    private static void Add(List<ScenarioResultsRoot> roots, byte role, Transform? transform)
    {
        if (transform == null) return;
        foreach (ScenarioResultsRoot root in roots)
            if (root.Root == transform) return;
        roots.Add(new ScenarioResultsRoot(role, transform));
    }

    internal static bool TryGetConverted(out ConvertedPanel? panel)
    {
        panel = null;
        UIWindow? window = Window;
        if (window == null) return false;
        foreach (ConvertedPanel candidate in CanvasConversion.ActivePanels)
            if (candidate.IsAlive && candidate.Target == window.transform)
            { panel = candidate; return true; }
        return false;
    }
}

internal readonly struct ScenarioResultsRoot
{
    internal readonly byte Role;
    internal readonly Transform Root;
    internal ScenarioResultsRoot(byte role, Transform root) { Role = role; Root = root; }
}

internal readonly struct ScenarioResultsParts
{
    internal readonly UIResultsManager Manager;
    internal readonly UIWindow Window;
    internal readonly HeaderHighlightText? Header;
    internal readonly GameObject? Results, Stats, Buttons;
    internal readonly GUIAnimator? HeaderAnimator, StatsAnimator;
    internal readonly UIReadyToggle? Ready;
    internal ScenarioResultsParts(UIResultsManager manager, UIWindow window, HeaderHighlightText? header,
        GameObject? results, GameObject? stats, GameObject? buttons, GUIAnimator? headerAnimator,
        GUIAnimator? statsAnimator, UIReadyToggle? ready)
    {
        Manager = manager; Window = window; Header = header; Results = results; Stats = stats;
        Buttons = buttons; HeaderAnimator = headerAnimator; StatsAnimator = statsAnimator; Ready = ready;
    }
    // Outputs only. Applying a received phase to game controllers could complete
    // native animation callbacks or consume the host's final ready waiter.
    internal byte PhaseBits => (byte)((Window.IsOpen ? 1 : 0) | (Results != null && Results.activeSelf ? 2 : 0)
        | (Stats != null && Stats.activeSelf ? 4 : 0) | (Buttons != null && Buttons.activeSelf ? 8 : 0)
        | (HeaderAnimator != null && HeaderAnimator.IsPlaying ? 16 : 0)
        | (StatsAnimator != null && StatsAnimator.IsPlaying ? 32 : 0)
        | (Ready != null && Ready.IsVisible ? 64 : 0));
}
