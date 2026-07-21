using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// TASK #2 — PRE-GRAB proximity highlight for a board figure: an ANIMATED additive glow overlaid
/// ON TOP OF the figure's own textures (NO size change) so the player can see which mini their hand
/// would pluck before they grab it.
///
/// The earlier build "popped" the figure 1.12× larger; the user did not want a scale change. This
/// implementation instead clones the figure's renderers (sharing the SAME bones, so the overlay
/// tracks the live idle animation) and re-draws them with the bundled <c>GloomhavenVR/Overlay</c>
/// shader in ADDITIVE blend with a warm amber tint — a candle-lit shimmer laid over the mini's own
/// meshes. It is OCCLUSION-CORRECT: <c>_ZTest = LEqual</c> + <c>_ZWrite = 0</c> means the figure's
/// own opaque depth hides the glow behind walls exactly like the mini (see <see cref="FigureOverlay"/>).
/// An <see cref="OverlayPulse"/> component animates the tint over <c>Time.unscaledTime</c> so it
/// visibly breathes.
///
/// Driven purely by the single-winner <c>OnGrabHighlight(hand, true/false)</c> callback (the driver
/// suppresses every non-winner, so this only ever engages on the one grab candidate). Snapshot-free:
/// the overlay is a separate throwaway object graph, so clearing it restores the figure byte-identical
/// (its own renderers/materials are never touched).
/// </summary>
internal sealed class FigureHighlight
{
    // Warm amber-gold — the candle-lit-dungeon palette of Gloomhaven, added as light over the mini.
    private static readonly Color GlowTint = new Color(1.0f, 0.62f, 0.26f);

    private GameObject? _overlayRoot;

    /// <summary>True while the highlight overlay exists.</summary>
    public bool Active => _overlayRoot != null;

    /// <summary>
    /// Build the animated additive overlay over <paramref name="animatedRoot"/> (the figure's
    /// Animator object — the parent of its renderers). No-op if already active, or if the bundled
    /// Overlay shader / any renderer is unavailable (returns false). The overlay container is parented
    /// under <paramref name="figureRoot"/> (a SIBLING of the Animator object) so the game's own
    /// renderer sweeps never enumerate the extra passes. Returns true when the overlay engaged.
    /// </summary>
    public bool Apply(GameObject figureRoot, GameObject animatedRoot)
    {
        if (Active)
            return true;
        if (figureRoot == null || animatedRoot == null)
            return false;

        Material? mat = FigureOverlay.MakeOverlayMaterial(GlowTint, additive: true);
        if (mat == null)
            return false; // bundle missing the Overlay shader — skip rather than pierce walls

        var root = new GameObject("VRFigureHighlight");
        root.transform.SetParent(figureRoot.transform, worldPositionStays: false);
        int cloned = FigureOverlay.CloneRenderersSharingBones(animatedRoot, root.transform, mat);
        if (cloned == 0)
        {
            Object.Destroy(root);
            Object.Destroy(mat);
            return false;
        }

        OverlayPulse pulse = root.AddComponent<OverlayPulse>();
        pulse.Init(mat, GlowTint); // pulse owns + destroys the material
        _overlayRoot = root;
        return true;
    }

    /// <summary>Destroy the overlay (idempotent). Restores the figure byte-identical — its own
    /// renderers and materials were never modified.</summary>
    public void Clear()
    {
        if (_overlayRoot != null)
        {
            Object.Destroy(_overlayRoot); // OverlayPulse.OnDestroy frees the material
            _overlayRoot = null;
        }
    }
}
