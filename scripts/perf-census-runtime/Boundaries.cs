using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public sealed class SceneController { public static SceneController Instance = new(); public Scene Current; public bool ThrowRead; public Scene GetCurrentScene=>ThrowRead?throw new InvalidOperationException("fixture scene context failure"):Current; public bool IsLoading, ScenarioIsLoading; }
public sealed class Choreographer { public static Choreographer? s_Choreographer; public Scene m_ProcGenScene; }
namespace GloomhavenVR.Core {
 internal sealed class Entry<T> { public T Value; public Entry(T value){Value=value;} }
 internal static class VRLog { internal static bool WantsDebug=true; internal static readonly List<string> Lines=new(); internal static void Info(string s,string text)=>Lines.Add(text); internal static void Debug(string s,string text)=>Lines.Add(text); internal static void Warn(string s,string text)=>Lines.Add(text); internal static void Error(string s,string text)=>Lines.Add(text); }
 internal static class VRLayers { internal const int ModLayer=31; }
 internal static class VRSession { internal static HarmonyLib.Harmony? Harmony; internal static bool IsRunning=true; }
 internal static class Defaults { internal const bool DesktopMirrorLeftEye=false; }
 internal static class PerfConfig {
 internal static void Bind() { }
 internal static Entry<bool> FrameSplit=new(true); internal static Entry<bool> Enabled=new(true); internal static Entry<bool> SceneCensus=new(true); internal static bool SceneProfileOn=true;
 internal static int ScenarioSceneryDensityPercentValue=0, ScenarioDecorationDensityPercentValue=0, ScenarioVegetationDensityPercentValue=0, PlayerFigureDetailPercent=0,EnemyFigureDetailPercent=0, EnvironmentEffectsDensityPercent=0,HeadMaskDropMask=0;
 internal static bool FigureClothSimulationEnabled=false,StaticScenarioBatchesOn=false,SimpleEnvironmentShadingOn=false,ReducedScenarioGenerationOn=false,SharedWallReadCacheOn=false,LightStabiliserWorkCacheOn=false;
 }
}
namespace GloomhavenVR.Rig { internal static class RenderQuality { internal static bool TextureStreamingForcedOff=>false; internal static bool TextureStreamingForceRequested=>false; } internal static class VRRigDriver { internal static Camera? HeadCamera=null; } }
namespace GloomhavenVR.WorldUI { internal static class WorldUIConfig { internal static GloomhavenVR.Core.Entry<bool> DesktopMirrorLeftEye=new(false); } }
namespace GloomhavenVR.Compat { internal enum ControlAction { ProximityGrab } internal static class ControlsProgress { internal static int Grabs; internal static void Notify(ControlAction a){Grabs++;} } }
namespace GloomhavenVR.Hands { internal enum HapticPreset { GrabPulse } internal sealed class VRHand { internal string Side=>"Fixture"; internal void SendHaptic(HapticPreset p){} } }
namespace GloomhavenVR.Hands.Interact { internal static class VRInteractables { internal readonly struct GrabbableEntry { internal readonly IGrabbable Target; internal readonly Collider Collider; internal GrabbableEntry(IGrabbable t,Collider c){Target=t;Collider=c;} } } }
