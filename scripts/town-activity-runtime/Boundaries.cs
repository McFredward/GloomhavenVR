using System;
using System.Collections.Generic;
using UnityEngine;
internal static class FaceClock { internal static float Now, Delta=1f/90f; }
namespace GloomhavenVR.Net
{
    internal static class NetProtocol { internal const float StaleTimeoutSeconds=3f; }
    internal static class TownActivityCodec
    {
        internal static bool Valid(in TownActivityState value)=>true;
        internal static bool Matches(in TownActivityState a,in TownFaceState b)=>a.Active==b.Active&&(!a.Active||(a.Epoch==b.Epoch&&a.Sequence==b.Sequence&&a.Clock==b.Clock));
    }
    internal static class TownFaceCodec { internal static bool Valid(in TownFaceState value)=>true; }
    internal static class NetPlayerActors { internal static int Local=1; internal static int LocalPlayerId()=>Local; }
    internal static class NetAvatarDriver
    {
        internal static int HeadScans;
        internal static readonly Dictionary<int,Vector3> Heads=new();
        internal static bool TryGetTownFaceHead(int player,out Vector3 point)=>Heads.TryGetValue(player,out point);
        internal static void CollectTownFacePeers(List<int> into) {HeadScans++;foreach(int id in Heads.Keys)into.Add(id);}
    }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera=null; } }
namespace GloomhavenVR.WorldUI
{
    internal static class SkyAlternative {internal static Transform? PlacedRoomRoot=null;}
    internal static class TownServicePlacement {internal static float GroundHeight(Transform room,Vector3 world)=>world.y;}
    internal static class TownServicePresentation { internal static bool Active=false;internal static byte Service=0; }
}
namespace GloomhavenVR.Net.TownServices
{
    internal sealed class TownServiceSessionInfo { internal bool Active=false;internal byte Service=0;internal float ReceivedTime=0; }
    internal static class TownServiceMirror {internal static readonly Dictionary<int,TownServiceSessionInfo> RemoteSessions=new();}
}

namespace GloomhavenVR.Core { internal static class VRLayers {internal const int ModLayer=27;} internal static class VRLog { internal static int Warnings; internal static void Warn(string source,string text)=>Warnings++; } }
