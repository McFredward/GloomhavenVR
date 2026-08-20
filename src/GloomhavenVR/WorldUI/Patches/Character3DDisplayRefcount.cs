using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Patches;

// ---------------------------------------------------------------------------
// THE FLICKER, MEASURED AND NAMED AT LAST — and it is a refcount the game drops.
//
// Five builds looked for a stereo bug. Two instruments say there is none:
//   * PanelFlickerProbe (182) ran three sessions silent  ⇒ the panel's own state is steady.
//   * CameraOrderProbe (185) logged ONE order shape for a whole session —
//       MapCamera[→RT] → UI Camera[→RT] → GUI 3D Camera[→RT] → Head[Left] → Head[Right]
//     with ZERO cameras between the eye passes ⇒ both eyes sample the same pixels.
// So the flicker is TEMPORAL and identical in both eyes. RenderTargetProbe (186) then measured
// the thing that actually flickers and printed the answer:
//
//   RENDER TARGET ALTERNATION on 'RawImage' (RenderTexture 'Character 3D assembly wide render
//   texture', writer 'GUI 3D Camera'): writer camera component enabled-bits 0xD↔0xF
//   COMPONENTS ON THE WRITER: [0:Camera, 1:Beautify, 2:Character3DDisplayManager,
//                              3:Character3DDisplayCameraSettings]
//
// Bit 1 is Beautify — a full-screen image effect — and it is going ON, OFF, ON, OFF. The graded
// and ungraded renders alternate in the texture, which is exactly why the LIVE CHARACTER flickers
// while the four portrait sprites beside it, on the same canvas at the same sorting order, do not.
//
// WHY IT ALTERNATES (decompiled Character3DDisplayManager):
//
//     public void Display(Component request, ECharacter character, ...)
//     {
//         beautify.enabled = true;
//         if (character3D != null && character3D.TypeCharacter == character && character3D.Skin == skin)
//         {
//             character3D.Show(playAnimation);
//             return;                    // <-- RETURNS WITHOUT REGISTERING THE REQUEST
//         }
//         showRequests.Add(request);
//         ...
//     }
//
//     public void Hide(Component request)
//     {
//         showRequests.Remove(request);
//         if (showRequests.Count == 0) beautify.enabled = false;
//         if (character3D != null) character3D.Hide();     // model SetActive(false)
//     }
//
// The show-requests are a refcount, and the early-return path forgets to take a reference. With
// ONE requester that never shows: it registers on the first call and its own Hide empties the set
// correctly. With TWO requesters asking for the SAME character, the second one is never counted —
// so when the first hides, the count hits zero, Beautify goes off and the model is switched off
// underneath the requester that still wants it. The next frame it asks again and everything comes
// back. On, off, on, off.
//
// AND THE SECOND REQUESTER IS OURS. The flat game runs a single-window discipline, so the party
// display and the party-assembly screen are never open together and only one of them can ask. The
// map room's whole point is that they ARE open together (user ruling, ModBuild 180: "Anders als in
// Flat soll es hier möglich sein mehrere Fenster parallel offen zu haben"). We created the
// two-requester case, so we own making the manager survive it.
//
// THE PATCH IS THE MISSING LINE AND NOTHING ELSE: register the request BEFORE the original runs,
// so the early-return path leaves it counted. HashSet.Add is idempotent, so the original's own
// Add on the other path is a harmless duplicate, and with a single requester the behaviour is
// byte-identical to vanilla (it registered there anyway). No game state is otherwise touched, no
// rule library, nothing on the wire. If the private field is ever renamed the patch stands down
// with one Warn and the flicker simply returns — it cannot break anything by failing.
// ---------------------------------------------------------------------------

/// <summary>
/// Repairs the show-request refcount in <c>Character3DDisplayManager.Display</c> so two open
/// windows can display the same character without switching each other's render off. Registered
/// by <c>WorldUIModule</c>.
/// </summary>
[HarmonyPatch(typeof(Character3DDisplayManager), "Display",
    typeof(Component), typeof(ECharacter), typeof(string), typeof(string))]
internal static class Character3DDisplayRefcount
{
    private static FieldInfo? _showRequests;
    private static bool _resolved;
    private static bool _logged;

    private static void Prefix(Character3DDisplayManager __instance, Component request)
    {
        if (!VRSession.IsRunning || request == null || __instance == null)
            return;
        if (!_resolved)
        {
            _resolved = true;
            _showRequests = AccessTools.Field(typeof(Character3DDisplayManager), "showRequests");
            if (_showRequests == null)
                VRLog.Warn("WorldUI", "CHARACTER 3D REFCOUNT: the private 'showRequests' set was not "
                                      + "found on Character3DDisplayManager — the patch stands down. The "
                                      + "consequence is the ModBuild-186 flicker returning whenever two "
                                      + "open windows display the same character; nothing else changes.");
        }
        if (_showRequests?.GetValue(__instance) is not HashSet<Component> set)
            return;
        if (!set.Add(request))
            return;
        if (!_logged)
        {
            _logged = true;
            VRLog.Info("WorldUI", $"CHARACTER 3D REFCOUNT: registering '{request.GetType().Name}' as a "
                                  + "show-request that the game's own early-return path skips. Two windows "
                                  + "open on the same character is a case the flat game cannot produce "
                                  + "(single-window discipline) but the map room's parallel windows can — "
                                  + "and without the reference the first Hide() switched Beautify and the "
                                  + "model off under the second window, which is the measured 0xD↔0xF "
                                  + "alternation behind the reported flicker.");
        }
    }
}
