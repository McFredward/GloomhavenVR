using System.Collections.Generic;
using UnityEngine;

// Native material clock values are explicit inputs. Production source sync and
// shader/UI rendering execute; only the surrounding logger/head reference is a port.
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static bool WantsDebug => false;
        internal static void Warn(string scope, string message) => Messages.Add(message);
        internal static void Debug(string scope, string message) => Messages.Add(message);
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Camera? HeadCamera = null; }
}
namespace GloomhavenVR.WorldUI.MenuDepth
{
    // No card owners exist in this native heightfield/material-clock fixture.
    // Actual card ownership and head-only depth leases run in the card/desktop
    // suites; this boundary must never authorize their rendering path here.
    internal sealed class MenuDepthCardPlane : MonoBehaviour
    {
        internal bool IsReady => false;
        internal bool HasHeadLease => false;
        internal bool OwnsDepthGraphic(UnityEngine.UI.Graphic value) => false;
    }
}
