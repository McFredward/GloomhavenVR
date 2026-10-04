using GloomhavenVR.Hands;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The labelled purse and body share actual attachment and animation ownership.</summary>
internal static class TownServicePursePresentation
{
    internal static void RegisterMotion(TownServiceToken token, Transform root, Transform body, VRHand? previewHand)
    {
        // The token reparents its labelled root, including inscriptions/tooltips.
        // A bowl deposit has left the fan and must also clear the ancestor's hand.
        VRHand? hand = token.IsHeld ? token.HoldingHand : token.PhysicalAtHome ? previewHand : null;
        TownServiceMirror.RegisterMotionHand(root, hand, followsRotation: false);
        TownServiceMirror.RegisterMotionHand(body, hand, followsRotation: false);
    }

    internal static float Visibility(TownServiceToken token, float fanVisibility) => token.IsMoving ? 1f : fanVisibility;
}
