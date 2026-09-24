using System;
using System.Collections.Generic;
using UnityEngine;

// Only external adapters are fixtures. Capture, assets, materials, codec, deltas, playback,
// clone-neutralization and all Unity UI/rendering operations compile from production sources.
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Info(string channel, string message) { }
        internal static void Note(string channel, string message) => Messages.Add(channel + ": " + message);
    }
}
namespace GloomhavenVR.Rig
{ internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.Cards
{ internal static class CardFaceMipBake { internal static Sprite OriginalFor(Sprite sprite) => sprite; } }
namespace GloomhavenVR.WorldUI
{ internal static class PanelMipBake { internal static Texture OriginalFor(Texture texture) => texture; } }
namespace GloomhavenVR.Core
{
    internal static class VRLayers
    {
        internal static void Apply(GameObject root)
        { foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 9; }
    }
}
