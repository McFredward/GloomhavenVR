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
