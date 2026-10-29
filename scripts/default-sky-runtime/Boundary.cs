using System.Collections.Generic;
using UnityEngine;
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLayers { internal const int ModLayer=26;internal static void Apply(GameObject go)=>go.layer=ModLayer; }
    internal static class VRLog
    {
        internal static readonly List<string> Lines=new();
        internal static void Info(string area,string value)=>Lines.Add(value);
        internal static void Warn(string area,string value)=>Lines.Add(value);
    }
    internal static class MixedReality { internal static void Tick(){} }
    internal static class SkyAlternative { }
    internal static class VRCameraPolicy { }
}
namespace GloomhavenVR.Core.Events
{
    // Explicit port for the native menu/recenter lifecycle owned by MenuWorldContext.
    internal static class MenuWorldContext
    {
        internal static bool IsReady=true;
        internal static bool AllowsSurroundings=>IsReady;
    }
}
namespace GloomhavenVR.Cards
{
    internal static class PlayTray { internal static Shader? OverlayShader()=>Shader.Find("GloomhavenVR/Overlay"); }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Transform? RigRoot;internal static Camera? HeadCamera; }
}
