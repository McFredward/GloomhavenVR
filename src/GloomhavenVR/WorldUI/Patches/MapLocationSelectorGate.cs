using Assets.Script.AdventureMap;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.Patches;

// ---------------------------------------------------------------------------
// THE GAME'S OWN MAP HOVER CANNOT WORK IN VR — and it does not merely fail, it FIGHTS.
//
//     // decompiled Assets.Script.AdventureMap/MapLocationSelector.cs:15,44
//     private readonly Vector2 _screenCenterPosition =
//         new Vector2((float)Screen.width / 2f, (float)Screen.height / 2f);
//     ...
//     Physics.RaycastNonAlloc(_camera.ScreenPointToRay(_screenCenterPosition),
//                             _results, _maxDistance, _layerMask)
//
// It hovers whatever sits under the SCREEN CENTRE of `Camera.main` every frame, which is the
// gamepad convention: the stick moves the camera, the reticle is fixed. In VR the map camera is
// deliberately FROZEN (the mod prefix-skips CameraController.LateUpdate, see
// Rig/CameraControllerPatches) so that screen centre is a fixed arbitrary point on the parchment
// — and this component would therefore hold a hover on some location the player is not pointing
// at, and Exit whatever MapLocationInteractor just entered, every frame.
//
// So it is switched OFF for exactly as long as the 3D map room stands, and its logic is
// reproduced from the VR ray instead: the same OnPointerEnter/OnPointerExit calls and the same
// StateMachine.Enter(LocationHover | WorldMap) transitions (WorldUI/MapRoom/MapLocationInteractor).
// The game's navigation state machine therefore sees the sequence it has always seen.
//
// REVERSIBLE BY CONSTRUCTION, which is what makes this acceptable under the standing "commit
// through UI seams only" rule: the patch is a pure early-out keyed on a live predicate, it holds
// no state, it touches no rule library and no Bolt, and the frame the map room stands down the
// component resumes exactly as before. Its private `_mapLocation` may be stale on resume; its own
// next miss calls OnPointerExit on it, which is the same self-healing the flat game relies on.
// ---------------------------------------------------------------------------

/// <summary>
/// Suppresses <c>MapLocationSelector.Update</c> while the 3D map room owns location hovering.
/// Registered by <c>WorldUIModule</c>.
/// </summary>
[HarmonyPatch(typeof(MapLocationSelector), "Update")]
internal static class MapLocationSelectorGate
{
    /// <summary>One line the first time the gate actually fires, so the log says WHY the game's
    /// own hover went quiet rather than leaving a reader to infer it.</summary>
    private static bool _logged;

    private static bool Prefix()
    {
        if (!MapRoom.MapRoomDriver.Active)
        {
            _logged = false;
            return true;
        }
        if (!_logged)
        {
            _logged = true;
            VRLog.Info("MapRoom", "MapLocationSelector.Update SUPPRESSED while the 3D map room stands — "
                                  + "it hovers whatever is under the frozen map camera's SCREEN CENTRE, "
                                  + "which in VR is a fixed arbitrary point on the parchment and would "
                                  + "cancel the laser's hover every frame. MapLocationInteractor "
                                  + "reproduces its enter/exit calls and its LocationHover/WorldMap state "
                                  + "transitions from the VR ray. Resumes untouched on stand-down.");
        }
        return false;
    }
}
