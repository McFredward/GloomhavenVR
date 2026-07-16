using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Rig;

/// <summary>
/// Neutralizes the game's hand-rolled orbit camera while VR drives the head pose.
///
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0) with ilspycmd 8.2 (2026-07-15):
/// <code>
///   // global namespace (no C# namespace), GH.Runtime.dll
///   public class CameraController : MonoBehaviour
///   {
///       public static CameraController s_CameraController;   // singleton
///       public Camera m_Camera;                                // scenario camera
///       public bool m_IsCameraCodeControlDisabled { get; set; }
///       public Vector3 FocusPoint => m_FocalPoint;             // orbit focus (ground plane)
///       private void LateUpdate() { ... }                      // ONE overload, no parameters
///   }
/// </code>
/// <c>LateUpdate</c> writes <c>m_Camera.transform.position</c>/<c>LookAt</c> and
/// <c>fieldOfView</c> (zoom) every frame (BOARD-INPUT §6) — prefix-skip while VR is
/// active. Since the owned-head-camera redesign (docs/CAMERA-POLICY.md §3) the game
/// scenario camera no longer renders into the HMD at all — these skips exist to keep
/// the scenario camera and <c>FocusPoint</c> PARKED: the focus is the rig/panel/
/// recenter anchor, and edge-scroll/zoom driven by the virtual mouse must not drag
/// it around under the diorama. While VR is NOT running the prefix returns true and
/// the game is 100% vanilla.
/// </summary>
[HarmonyPatch(typeof(CameraController), "LateUpdate")]
internal static class CameraController_LateUpdate_Patch
{
    private static bool Prefix() => !VRSession.IsRunning;
}

/// <summary>
/// Second durable camera writer, reached from scripted flows (SmartFocus/MoveToLook/
/// ZoomTo coroutines, message-profile camera moves) that bypass LateUpdate and also
/// re-toggle <c>m_IsCameraCodeControlDisabled</c> themselves — so skipping LateUpdate
/// alone is not durable (.planning/research/PATCH-TARGETS.md §1.3).
///
/// Verified against the REAL GH.Runtime.dll with ilspycmd (2026-07-15):
/// <code>
///   private void RefreshFocusPosition(float? y = null)   // ONE overload, 163 B IL
///   // writes m_Camera.transform.position + LookAt(m_FocalPoint + up * m_FocusPointHeight)
/// </code>
/// </summary>
[HarmonyPatch(typeof(CameraController), "RefreshFocusPosition")]
internal static class CameraController_RefreshFocusPosition_Patch
{
    private static bool Prefix() => !VRSession.IsRunning;
}
