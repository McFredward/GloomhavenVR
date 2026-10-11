using System;
using System.Collections.Generic;
using UnityEngine;

// Only lifecycle/configuration and local hand tracking are supplied inputs. Both complete
// production cloth drivers run against actual Unity Cloth, meshes, bones and colliders.
public sealed class ActorBehaviour : MonoBehaviour
{
    private int m_ForcingPositionChangeCounter;
    public void ForceNativePosition(int count) { m_ForcingPositionChangeCounter=count; }
    public bool Forcing => m_ForcingPositionChangeCounter > 0;
    public static ActorBehaviour? GetActorBehaviour(GameObject root) => root.GetComponentInChildren<ActorBehaviour>(true);
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning=true; }
    internal static class PerfConfig { internal static bool FigureClothSimulationEnabled=true; }
    internal static class VRLog { internal static void Info(string scope,string text) { } }
    internal static class PerfMonitor
    {
        internal static readonly Dictionary<string,int> Counts=new();
        internal static void Register(string key) { }
        internal static void Count(string key,int count=1) { Counts.TryGetValue(key,out int value);Counts[key]=value+count; }
        internal static TestScope Scope(string key) => new();
        internal readonly struct TestScope : IDisposable { public void Dispose() { } }
    }
}
namespace GloomhavenVR.Hands
{
    internal enum HandSide { Left,Right }
    internal sealed class VRHand
    {
        internal bool IsTracked=true;
        internal float WorldScale=1f;
        internal sealed class HandRig
        {
            internal bool IsComplete=true;
            internal Transform PalmCenter=null!,IndexTip=null!;
        }
        internal HandRig Rig=new();
    }
    internal static class VRHands
    {
        internal static VRHand? Left=null,Right;
        internal static int Reads;
        internal static VRHand? Get(HandSide side) { Reads++; return side==HandSide.Left?Left:Right; }
    }
}
namespace GloomhavenVR.Board.FigureGrab
{
    internal sealed class TestBool { internal bool Value=true; }
    internal static class FigureGrabConfig
    {
        internal static TestBool? ClothFollowsFreeHand=new();
        internal static float ClothHandReachRealMeters=.5f;
        internal static float PickRadiusRealMeters => .1f;
    }
    internal static class FigureStretch { }
    internal sealed class FigureGrabbable
    {
        internal GameObject? RootObject;
        internal static FigureGrabbable? Left,Right;
        internal static int Reads;
        internal static FigureGrabbable? HeldBy(GloomhavenVR.Hands.HandSide side)
        { Reads++; return side==GloomhavenVR.Hands.HandSide.Left?Left:Right; }
    }
}
