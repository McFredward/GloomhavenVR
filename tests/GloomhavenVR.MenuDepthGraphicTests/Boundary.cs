using System.Collections.Generic;
using UnityEngine;

// Only surrounding feature/session state is supplied. Graphic generation,
// catalogue parsing, semantic discovery and native GH.Runtime fields are real.
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Warn(string scope, string message) => Messages.Add(message);
        internal static void Note(string scope, string message) => Messages.Add(message);
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Camera? HeadCamera = null; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static bool ConversionActive = true; }
    internal sealed class ConvertedPanel { internal Transform Target = null!; }
    internal static class CanvasConversion { internal static readonly List<ConvertedPanel> ActivePanels = new(); }
}
namespace GloomhavenVR.WorldUI.MenuDepth
{
    // Character camera rendering has its own source-bound GPU suite. No character
    // is present here; exercise the production illustration/decorative driver.
    internal static class MenuDepthCharacters
    {
        internal static void Tick() { }
        internal static void Shutdown() { }
    }
}
