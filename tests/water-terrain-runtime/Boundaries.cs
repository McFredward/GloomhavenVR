using System;
using System.Collections.Generic;
using UnityEngine;

// Native procedural placement, registry, bundle ownership and config storage are
// explicit boundaries. Complete production water code runs against real Unity.
public class ProceduralBase : MonoBehaviour { public virtual void NotifyContentPlacementComplete() { } }
public sealed class ProceduralMapTile : ProceduralBase { }
namespace BepInEx { internal static class Paths { internal static string ConfigPath => "/tmp/ghvr-water-no-config"; } }
namespace GloomhavenVR.Core
{
    internal static class VRSession
    {
        internal static bool IsRunning = true;
        internal static HarmonyLib.Harmony? Harmony = null;
    }
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Info(string scope, string text) => Messages.Add(text);
        internal static void Warn(string scope, string text) => Messages.Add(text);
    }
    internal static class TickGuard { internal static void Run(string key, Action tick, string scope) => tick(); }
    internal static class BundleShaders
    {
        internal static bool Missing = false;
        internal static Shader? Resolve(string name, string scope, string yes, string no) => Missing ? null : Shader.Find(name);
        internal static string How(string name) => "actual Unity imported production shader";
    }
    internal static class SceneRegistry
    {
        internal static class MapTiles
        {
            internal static readonly List<ProceduralMapTile> Tiles = new();
            internal static int Collections;
            internal static void Collect(List<ProceduralMapTile> result)
            {
                Collections++; result.Clear();
                foreach (ProceduralMapTile tile in Tiles)
                    if (tile != null && tile.isActiveAndEnabled) result.Add(tile);
            }
        }
    }
}
public sealed class RFX4_DistortionAndBloom : MonoBehaviour { public float RenderTextureResolutoinFactor = .25f; }
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera => null; } }
namespace GloomhavenVR.Core { internal static class WallSegmentFade { } }
