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
/// - enemy stat popup: WorldspaceStarHexDisplay.DisplayCursorHoverStar →
///   ShowActorStatPanelForTile (verified, WSHD.cs:3200/3237) shows the game's
///   ActorStatPanel for the actor on the hovered tile — so pointing at or
///   near-touching an enemy miniature opens the same stat panel the mouse hover
///   does. (World-space restyling of that panel is Phase 3c.)
/// - reticle snap: the projected cursor snap is [Board] SnapToHexCenter in BoardPick.
/// </summary>
internal static class TargetingUx
{
    private static CInteractable? _lastHover;

    public static void Reset() => _lastHover = null;

    /// <summary>Per-frame from <see cref="BoardDriver"/>.</summary>
    public static void Tick()
    {
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
}
