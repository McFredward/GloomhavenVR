using System;
using System.Collections.Generic;
using FFSNet;
using GloomhavenVR.Core;
using GLOOM;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Original results presentation subjects; never a second results/ready controller.</summary>
internal static class ScenarioResultsNative
{
    private static UIReadyToggle? _reportedReady;
    private static UIWindow? _reportedWindow;
    internal const byte ContinueReady = 1, ReturnToMap = 2, RetryScenario = 3;
    internal static Func<byte, uint, bool>? SubmitContinuation;
    internal static UIResultsManager? Manager => Singleton<UIResultsManager>.IsInitialized
        ? Singleton<UIResultsManager>.Instance : null;
    internal static UIWindow? Window => Manager != null ? Manager.myWindow : null;

    /// <summary>The final waiter uses all players, including spectators. The
    /// game's visibility bookkeeping nevertheless counts only participants in
    /// OnReadiedPlayersChanged. A spectator's automatic acknowledgement can
    /// therefore hide the still-unvoted host's only Continue control. Correct
    /// that presentation flag without inventing any vote, ACK or completion.</summary>
    internal static void RefreshReadyVisibility(ScenarioResultsParts parts)
    {
        UIReadyToggle? ready = parts.Ready;
        if (!FFSNetwork.IsOnline || ready == null || !parts.Window.IsOpen
            || !parts.Window.gameObject.activeInHierarchy
            || ready.ReadyUpType != UIReadyToggle.EReadyUpType.Player
            || ready.readyUpToggleState != EReadyUpToggleStates.NotSet
            || ActionProcessor.CurrentPhase != ActionPhaseType.ScenarioEnded)
            return;
        bool repaired = ready._allPlayersReady && ready.PlayersReady.Count < PlayerRegistry.AllPlayers.Count;
        if (repaired) ready._allPlayersReady = false;
        // Player-mode readiness also awaits a host without a character. Offer
        // its original manual control after the original reveal permission;
        // the participant-only Show condition otherwise leaves no way to vote.
        bool offer = !FFSNetwork.IsClient && !PlayerRegistry.MyPlayer.IsParticipant
            && ready.IsInteractable && !ready.ToggledOn && !ready._requestVisible
            && ready.PlayersReady.Count < PlayerRegistry.AllPlayers.Count;
        if (!repaired && !offer) return;
        // ToggleVisibility only re-evaluates the existing native visibility
        // request, interactability and blockers. It never readies a player.
        ready.ToggleVisibility(offer || ready._requestVisible);
        if (_reportedReady == ready && _reportedWindow == parts.Window) return;
        _reportedReady = ready; _reportedWindow = parts.Window;
        VRLog.Alert("WorldUI", $"Results Continue visibility repaired: {ready.PlayersReady.Count}/{PlayerRegistry.AllPlayers.Count} players ready; participant-only visibility had omitted an outstanding player decision. Native votes and completion remain unchanged.");
    }

    /// <summary>Geometry identity only. Observers use their matching native
    /// template to place metadata input; local hidden/client permission is not
    /// substituted for the common author's actual original-control permission.</summary>
    internal static Selectable? ContinuationTarget(byte operation)
    {
        if (!TryRead(out ScenarioResultsParts parts)) return null;
        if (operation == ContinueReady) return Singleton<UIReadyToggle>.IsInitialized
            ? Singleton<UIReadyToggle>.Instance.GetComponent<Toggle>() : null;
        UIResultsOption? option = operation == ReturnToMap ? parts.Manager.returnToMapOption
            : operation == RetryScenario ? parts.Manager.retryOption : null;
        return option is UIResultsButtonOption button ? button._button : null;
    }

    /// <summary>Only a validated explicit public request reaches this method.
    /// Execute the original pointer handler, retaining native click interception,
    /// reveal/readiness, progress confirmation, callbacks and ACK authority.</summary>
    internal static bool TryInvokeContinuation(byte operation)
    {
        if (!FFSNetwork.IsOnline || FFSNetwork.IsClient || EventSystem.current == null
            || ActionProcessor.CurrentPhase != ActionPhaseType.ScenarioEnded
            || !TryRead(out ScenarioResultsParts parts) || !parts.Window.IsOpen
            || !parts.Window.gameObject.activeInHierarchy) return false;
        if (operation == ContinueReady && (parts.Ready == null || !parts.Ready.IsVisible
            || !parts.Ready.IsInteractable || parts.Ready.ToggledOn
            || parts.Ready.ReadyUpType != UIReadyToggle.EReadyUpType.Player
            || parts.Ready.readyUpToggleState != EReadyUpToggleStates.NotSet)) return false;
        Selectable? target = ContinuationTarget(operation);
        if (target == null || !target.IsActive() || !target.IsInteractable()) return false;
        if (target is ExtendedButton button && !InteractabilityManager.ShouldAllowClickForExtendedButton(button)
            || target is ExtendedToggle toggle && !InteractabilityManager.ShouldAllowClickForExtendedToggle(toggle)) return false;
        bool clicked = ExecuteEvents.Execute(target.gameObject,
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
            ExecuteEvents.pointerClickHandler);
        return clicked && (operation != ContinueReady || parts.Ready!.ToggledOn);
    }

    internal static bool TryRead(out ScenarioResultsParts parts)
    {
        UIResultsManager? manager = Manager;
        UIWindow? window = manager != null ? manager.myWindow : null;
        if (manager == null || window == null) { parts = default; return false; }
        var adventure = manager as UINewAdventureResultsManager;
        UIReadyToggle? ready = null;
        if ((window.IsOpen || window.IsVisible) && FFSNetwork.IsOnline && Singleton<UIReadyToggle>.IsInitialized)
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

/// <summary>Own transparent hit surface on an inert common picture. This
/// component contains operation/opening metadata, never a copied game callback.</summary>
internal sealed class ScenarioResultsContinuationProxy : MonoBehaviour, IPointerClickHandler
{
    internal byte Operation;
    internal uint Opening;
    internal CanvasGroup[] NativeGroups = Array.Empty<CanvasGroup>();
    internal void Refresh(uint opening)
    {
        Opening = opening;
        float alpha = 1f;
        foreach (CanvasGroup group in NativeGroups)
        {
            if (group == null || !group.enabled) continue;
            alpha *= group.alpha;
            if (group.ignoreParentGroups) break;
        }
        gameObject.SetActive(alpha > .001f);
    }
    public void OnPointerClick(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left && isActiveAndEnabled && Opening != 0)
            ScenarioResultsNative.SubmitContinuation?.Invoke(Operation, Opening);
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
