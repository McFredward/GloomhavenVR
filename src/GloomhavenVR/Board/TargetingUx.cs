using GloomhavenVR.Hands;

namespace GloomhavenVR.Board;

/// <summary>
/// Targeting UX polish (Phase 3a): a haptic tick on the picking hand whenever the
/// pick lands on a NEW valid interactable. "Valid" mirrors the game's own cursor
/// validity in <c>Controller.LateUpdate</c> (verified, Controller.cs:175):
/// a hit with a <c>TileBehaviour</c> is valid when its <c>m_ClientTile</c> is set;
/// a hit WITHOUT a TileBehaviour (actor miniatures, door/chest props resolving to
/// CInteractableActor) is always dispatched by the game, so it counts as valid too.
///
/// Everything else in the "targeting UX" bucket rides on the picking patches for free:
/// - hover highlight: HoverRegisterer + WorldspaceStarHexDisplay pooled hexes follow
///   the patched pick (nothing to do here),
/// - reticle snap: the projected cursor snap is [Board] SnapToHexCenter in BoardPick.
///
/// PASSIVE HOVER ACTOR-INFO SUPPRESSION (bug #2): the game opens the actor's
/// <c>ActorStatPanel</c> whenever the cursor hovers a figure —
/// <c>WorldspaceStarHexDisplay.DisplayCursorHoverStar → ShowActorStatPanelForTile</c>
/// (verified, WSHD.cs:3200 → 3343 → 3360, the ONLY scenario-play caller of
/// <c>ActorStatPanel.Show(CActor)</c>; the other two callers are level-editor props).
/// In VR that means merely POINTING the laser at a miniature pops its stat panel.
/// That info now lives on the figure-grab flow instead, so we kill the passive-hover
/// path here. The game's <c>ShowActorStatPanelForTile</c> gates on
/// <c>ActorStatPanel.DoShow</c> (via <c>CanShow()</c>, ActorStatPanel.cs:389), and
/// <c>DoShow = false</c> also hides any panel already up — so lowering that flag is
/// a clean, no-flicker suppressor (the panel's <c>Show()</c> early-outs before it
/// ever appears). We gate the suppression to the passive case: while an ability/attack
/// target selection is active (<c>CurrentDisplayState == TargetSelection</c>) the
/// target's stats ARE wanted, so we leave the flag up. We only ever lower a flag we
/// then raise back (targeting starts, board closes, or level editor), so we never
/// clobber another owner's intent (e.g. FullCardHandViewer's own DoShow toggling).
/// </summary>
internal static class TargetingUx
{
    private static CInteractable? _lastHover;

    /// <summary>True while we are the ones holding <c>ActorStatPanel.DoShow</c> down.</summary>
    private static bool _statPanelSuppressed;

    public static void Reset()
    {
        _lastHover = null;
        RestoreActorStatPanel();
    }

    /// <summary>Per-frame from <see cref="BoardDriver"/>.</summary>
    public static void Tick()
    {
        // Runs regardless of the HoverHaptics toggle: killing the passive
        // point-at-figure info panel is not a haptics feature.
        SuppressHoverActorInfo();

        if (!BoardConfig.HoverHaptics.Value)
        {
            _lastHover = null;
            return;
        }

        CInteractable? current = null;
        if (BoardPick.HasHit && BoardPick.HitCollider != null)
            current = BoardPick.HitCollider.gameObject.GetComponentInParent<CInteractable>();

        if (current == _lastHover)
            return;
        _lastHover = current;

        if (current == null || !IsValidTarget(current))
            return;

        BoardPick.SourceHand?.SendHaptic(HapticPreset.HoverTick);
    }

    private static bool IsValidTarget(CInteractable interactable)
    {
        TileBehaviour? tile = interactable.GetComponent<TileBehaviour>();
        return tile == null || tile.m_ClientTile != null;
    }

    /// <summary>
    /// Suppresses the game's passive point-at-a-figure <c>ActorStatPanel</c> while the
    /// board is hover-active and we're NOT in an ability/attack target selection.
    /// See the class remarks for the full rationale.
    /// </summary>
    private static void SuppressHoverActorInfo()
    {
        if (!Singleton<ActorStatPanel>.IsInitialized)
            return;

        WorldspaceStarHexDisplay? display = WorldspaceStarHexDisplay.Instance;

        // During a real ability/attack target selection the target's stats ARE
        // wanted (same hover call, WSHD.cs:3360) — so don't suppress there.
        bool targeting = display != null
            && display.CurrentDisplayState
               == WorldspaceStarHexDisplay.WorldSpaceStarDisplayState.TargetSelection;

        // Level editor drives ActorStatPanel.Show directly from door/spawner props
        // (not the hover path); leave its DoShow flag alone.
        bool levelEditor = SaveData.Instance?.Global?.GameMode == EGameMode.LevelEditor;

        bool suppress = display != null && !targeting && !levelEditor;

        ActorStatPanel panel = Singleton<ActorStatPanel>.Instance;
        if (suppress)
        {
            // Re-lower only when something raised it (robust to other DoShow owners).
            if (panel.DoShow)
                panel.DoShow = false;
            _statPanelSuppressed = true;
        }
        else if (_statPanelSuppressed)
        {
            RestoreActorStatPanel();
        }
    }

    /// <summary>Raises <c>ActorStatPanel.DoShow</c> back up iff we were the one holding it down.</summary>
    private static void RestoreActorStatPanel()
    {
        if (!_statPanelSuppressed)
            return;
        _statPanelSuppressed = false;
        if (Singleton<ActorStatPanel>.IsInitialized)
        {
            ActorStatPanel panel = Singleton<ActorStatPanel>.Instance;
            if (!panel.DoShow)
                panel.DoShow = true;
        }
    }
}
