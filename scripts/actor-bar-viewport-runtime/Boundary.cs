using UnityEngine;
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Debug(string scope, string value) { } internal static void Warn(string scope, string value) { } }
    internal static class VRLayers { internal const int ModLayer = 31; }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Camera? HeadCamera = null; }
}
namespace GloomhavenVR.Hands { internal enum HandSide { Left, Right } }
namespace GloomhavenVR.Board.FigureGrab
{
    internal sealed class FigureGrabbable
    {
        internal static FigureGrabbable? HeldBy(GloomhavenVR.Hands.HandSide side) => null;
        internal Renderer[]? HeldRenderers() => null;
    }
    internal sealed class GrabbableProp
    {
        internal static GrabbableProp? HeldBy(GloomhavenVR.Hands.HandSide side) => null;
        internal Renderer[]? HeldRenderers() => null;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class ActorBarDepthShaders { internal static void SetCaptureAvailable(bool ready) { } }
}
