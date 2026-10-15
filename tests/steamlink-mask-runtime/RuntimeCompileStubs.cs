// Type-check only: actual Config/VR/options integration is compiled by the complete mod build.
using UnityEngine;
namespace GloomhavenVR
{
    internal static class FrameDefaults { internal static bool Active => false; }
}
namespace GloomhavenVR.Core
{
    internal static class SteamLinkMaskConfig
    {
        internal static bool Requested => false;
        internal static void Bind() { }
        internal static void SetRequested(bool value) { }
    }
    internal static class VRSession { internal static bool IsRunning => false; }
    internal static class VRLog
    {
        internal static bool WantsDebug => false;
        internal static void Alert(string scope, string text) { }
        internal static void Debug(string scope, string text) { }
        internal static void Note(string scope, string text) { }
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Camera? HeadCamera => null; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class VROptionsTab { internal static bool IsOpen => false; }
}
