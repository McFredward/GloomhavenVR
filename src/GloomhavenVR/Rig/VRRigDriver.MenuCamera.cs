using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    /// <summary>
    /// Retain the desktop anchor priority. Quest also accepts native backbuffer
    /// cameras already redirected by FlatScreen, then a native UI camera as the
    /// last reference-only anchor. The owned head remains the sole XR renderer.
    /// </summary>
    private static Camera? ResolveMenuCamera()
    {
        bool quest = QuestStandalonePlatform.Enabled;
        Camera? cam = NativeCameraRenderBudget.Main;
        if (cam != null && (!quest || cam != HeadCamera))
            return cam;

        // The owned-game MainMenu scene disables its MainCamera-tagged object;
        // only UICamera plus a private character-preview camera remain active.
        // The B616 capture records rig=False and that UICamera on our scrub sink.
        // Requiring an untouched backbuffer here prevents recovery after a scene
        // unload or anchor disable: both capture and scrub legitimately own those
        // native targets already. This changes only Quest's anchor eligibility,
        // never native camera activation, gameplay, output, or input routing.
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] all);
        Camera? best = null;
        Camera? ui = null;
        for (int i = 0; i < count; i++)
        {
            Camera candidate = all[i];
            if (candidate == null || !candidate.enabled || candidate == HeadCamera)
                continue;
            if (candidate.targetTexture != null && (!quest || !WorldUI.FlatScreen.OwnsPresentationTarget(candidate)))
                continue;
            if (candidate.CompareTag("UICamera"))
            {
                if (quest && (ui == null || candidate.depth > ui.depth))
                    ui = candidate;
                continue;
            }
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }
        return best != null ? best : ui;
    }
}
