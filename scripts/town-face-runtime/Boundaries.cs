using System;
using System.Collections.Generic;
using UnityEngine;
internal static class FaceClock { internal static float Now, Delta=1f/90f; }
namespace GloomhavenVR.Net
{
    internal static class NetProtocol { internal const float StaleTimeoutSeconds=3f; }
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
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.WorldUI
{
    internal static class TownServicePresentation { internal static bool Active=false;internal static byte Service=0; }
}
namespace GloomhavenVR.Net.TownServices
{
    internal sealed class TownServiceSessionInfo { internal bool Active;internal byte Service;internal float LastSeenTime; }
    internal static class TownServiceMirror
    {
        internal static readonly Dictionary<int,TownServiceSessionInfo> RemoteSessions=new();
        internal static int InteractionOwner(byte service)=>0;
    }
}

namespace GloomhavenVR.Core { internal static class VRLayers {internal const int ModLayer=27;} internal static class VRLog { internal static int Warnings; internal static void Warn(string source,string text)=>Warnings++; } }
internal static class FaceWrites { internal static int Count;internal static void Set(SkinnedMeshRenderer renderer,int index,float weight){Count++;renderer.SetBlendShapeWeight(index,weight);} }
