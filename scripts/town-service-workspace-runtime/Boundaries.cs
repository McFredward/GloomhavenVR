using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Only the connected native roster, clock and bundle-location boundary are fixtures.
// All workspace hierarchy, material ownership, placement, movement and disposal are production.
namespace GloomhavenVR.Core
{
    internal static class VRLayers
    {
        internal static void Apply(GameObject root)
        { foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31; }
    }
}
namespace GloomhavenVR.Net
{
    internal static class NetPlayerActors
    {
        internal static int Local;
        internal static readonly List<(int Id, string? Account, string? Name)> Roster = new();
        internal static int LocalPlayerId() => Local;
        internal static void CollectRoster(List<(int Id, string? Account, string? Name)> target) => target.AddRange(Roster);
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class WorkspaceClock { internal static float Now; }
    internal static class TownServiceAssets
    {
        internal static GameObject? Prefab(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var path = args[Array.IndexOf(args, "-workspaceBundle") + 1];
            var bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b.name == "ghvr-town.bundle")
                ?? AssetBundle.LoadFromFile(path);
            return bundle.LoadAsset<GameObject>("assets/bundle/townservices/prefabs/" + name + ".prefab");
        }
    }
}
