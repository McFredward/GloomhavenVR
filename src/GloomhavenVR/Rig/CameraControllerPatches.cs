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
/// active so the <see cref="VRRigDriver"/>-owned TrackedPoseDriver owns the transform.
/// While VR is NOT running the prefix returns true and the game is 100% vanilla.
/// </summary>
[HarmonyPatch(typeof(CameraController), "LateUpdate")]
internal static class CameraController_LateUpdate_Patch
{
    private static bool Prefix() => !VRSession.IsRunning;
}
