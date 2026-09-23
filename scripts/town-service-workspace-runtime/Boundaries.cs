using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Native roster, clock, canonical map/seat state and bundle location are boundaries.
// Both workspace and actual station placement/floor sampling execute production source.
// All workspace hierarchy, material ownership, placement, movement and disposal are production.
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Warn(string category,string message) { Debug.LogWarning(category + ": " + message); } }
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
            var bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b.name == System.IO.Path.GetFileName(path))
                ?? AssetBundle.LoadFromFile(path);
            return bundle.LoadAsset<GameObject>("assets/bundle/townservices/prefabs/" + name + ".prefab");
        }
    }
}

namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomSeat
    {
        internal struct Seat { internal float YawDegrees; internal Vector3 FloorPosition; }
    }
    internal static class MapRoomDriver
    {
        internal static MeshRenderer? ParchmentRenderer;
        internal static bool Active = true, Available = true;
        internal static Vector3 Center = new(70, 90, -140);
        internal static float Scale = 198f, Yaw = 58f;
        internal static bool TryGetParchmentFrame(out Vector3 center, out float scale)
        { center = Center; scale = Scale; return Available; }
        internal static bool TrySolveSeat(out MapRoomSeat.Seat seat, out string reason)
        { seat = new MapRoomSeat.Seat { YawDegrees = Yaw, FloorPosition = Center }; reason = "fixture"; return Available; }
    }
}
namespace GloomhavenVR.Core
{
    internal static class SkyAlternative { internal static Transform? PlacedRoomRoot; }
}

namespace GloomhavenVR.WorldUI
{
    // Native asynchronous prop loading and its independent practical-light component are boundaries.
    internal static class TownServiceDecor
    {
        internal static readonly Dictionary<byte, List<Transform>> Props = new();
        internal static int StaticPropCount(byte service) => Props.TryGetValue(service, out var props) ? props.Count : 0;
        internal static bool TryStaticProp(byte service, int index, out Transform? source, out string address)
        {
            source = Props[service][index]; address = "decor." + service + "." + index; return source != null;
        }
    }
    internal static class TownServiceWorkspacePractical
    { internal static void RebindClone(string key, GameObject clone) { } }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Quaternion YawOnly(Quaternion q){float m=Mathf.Sqrt(q.y*q.y+q.w*q.w);return m<1e-6f?Quaternion.identity:new Quaternion(0,q.y/m,0,q.w/m);} } }
