using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Per-frame pump for the Board module (one hidden persistent GameObject, owned by
/// <see cref="BoardModule"/>). Runs in Update — i.e. before the game's
/// <c>Controller.LateUpdate</c> click dispatch consumes what we produce.
///
/// Responsibilities:
/// - keep both hands' <see cref="Hands.Interact.RayInteractor.Mask"/> synced to the
///   game's selection layer (INTERFACES-P2 §2: "Phase-3a sets
///   Controller.m_ActiveSelectionRaycastLayer here"),
/// - tick <see cref="BoardClickDriver"/> (click commit),
/// - tick <see cref="AoeControl"/> (thumbstick AoE rotation),
/// - tick <see cref="TargetingUx"/> (hover haptics).
///
/// BoardPick itself is frame-memoized and needs no pump.
/// </summary>
internal sealed class BoardDriver : MonoBehaviour
{
    private void Update()
    {
        SyncRayMask();
        SyncReticleSnap();
        BoardClickDriver.Tick();
        AoeControl.Tick();
        TargetingUx.Tick();
    }

    /// <summary>
    /// P5 (MISSION A.1): while far-picking with [Board] SnapToHexCenter, snap the
    /// VISIBLE ray reticle to the hovered hex center via the P2 ray's ReticleOverride
    /// (a one-frame latch — no clearing needed when the pick moves off the board).
    /// </summary>
    private static void SyncReticleSnap()
    {
        if (!BoardConfig.SnapToHexCenter.Value || BoardPick.Source != BoardPick.PickSource.Far)
            return;
        if (BoardPick.TryGetCursorWorld(out UnityEngine.Vector3 world))
            BoardPick.SourceHand!.Ray.ReticleOverride = world;
    }

    private static void SyncRayMask()
    {
        Controller? controller = Controller.Instance;
        if (controller == null)
            return;
        int mask = controller.m_ActiveSelectionRaycastLayer.value;

        VRHand? left = VRHands.Left;
        if (left != null && left.Ray.Mask.value != mask)
            left.Ray.Mask = controller.m_ActiveSelectionRaycastLayer;

        VRHand? right = VRHands.Right;
        if (right != null && right.Ray.Mask.value != mask)
            right.Ray.Mask = controller.m_ActiveSelectionRaycastLayer;
    }
}
