using GloomhavenVR.Core;
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
/// - tick <see cref="CameraArrivalGuard"/> (complete camera-follow transitions the
///   VR-parked camera can never finish — releases their TimeManager pause),
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
        // Each sub-tick is isolated + attributed via the shared Core.TickGuard so one
        // throwing step can never abort the rest of the frame's ticks AND the next run's
        // log NAMES the thrower ("[Board] Tick 'Board.<step>' threw <exc + stack>") instead
        // of the anonymous per-frame NullReferenceException flood. Static method groups →
        // the delegates are cached by the compiler, so no per-frame allocation. Call order
        // is unchanged (SyncRayMask → CameraArrival → Click → Aoe → Targeting).
        TickGuard.Run("Board.SyncRayMask", SyncRayMask);
        TickGuard.Run("Board.CameraArrival", CameraArrivalGuard.Tick);
        TickGuard.Run("Board.Click", BoardClickDriver.Tick);
        TickGuard.Run("Board.Aoe", AoeControl.Tick);
        TickGuard.Run("Board.Targeting", TargetingUx.Tick);
    }

    // Test #14 item 2: the former SyncReticleSnap (visible reticle snapped to the
    // hovered hex center via RayInteractor.ReticleOverride) is REMOVED — moving the
    // dot off the aim line visibly re-aimed the beam. [Board] SnapToHexCenter still
    // snaps the GAME-side cursor projection (BoardPick.ResolveCursorWorld), so the
    // game's own hex hover highlight communicates the snapped hex.

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
