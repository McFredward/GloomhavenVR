using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public sealed class SceneController { public static SceneController Instance = new(); public Scene Current; public bool ThrowRead; public Scene GetCurrentScene=>ThrowRead?throw new InvalidOperationException("fixture scene context failure"):Current; public bool IsLoading, ScenarioIsLoading; }
public sealed class CMessageData
{
 public enum MessageType { ActionSelection=1, Throwing=2, Nested=4 }
 public MessageType m_Type;
}
public sealed class Choreographer
{
 public static Choreographer? s_Choreographer; public Scene m_ProcGenScene; public int Processed;
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 private void ProcessMessage(CMessageData message)
 {
  Processed++; CardsHandManager.Show();
  if (message.m_Type==CMessageData.MessageType.Throwing) throw new InvalidOperationException("original native failure");
  if (message.m_Type==CMessageData.MessageType.Nested) ProcessMessage(new CMessageData{m_Type=CMessageData.MessageType.ActionSelection});
 }
 public void Dispatch(CMessageData message)=>ProcessMessage(message);
}
public static class CardsHandManager
{
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public static void Show(){CardsHandUI.UpdateView();}
}
public static class CardsHandUI
{
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public static void UpdateView(){System.Threading.Thread.Sleep(1);}
}
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

public sealed class ProceduralMapTile : UnityEngine.MonoBehaviour { }
public sealed class ApparanceEntity : UnityEngine.MonoBehaviour { public bool IsBusy; }
public sealed class MaterialLoader : UnityEngine.MonoBehaviour { public readonly System.Collections.Generic.List<MaterialLoaderData> LoadersData=new(); }
public sealed class MaterialLoaderData { public UnityEngine.Renderer Renderer=null!; private UnityEngine.Material[]? _loadedMaterials; public void SetLoaded(UnityEngine.Material[] value){_loadedMaterials=value;} }
public static class RoomVisibilityTracker { public static event System.Action<ProceduralMapTile,bool>? ProceduralMapTileVisibilityStateChanged; public static void Emit(ProceduralMapTile tile,bool show)=>ProceduralMapTileVisibilityStateChanged?.Invoke(tile,show); }
namespace GloomhavenVR.Core {
internal static class ScenarioSceneryBudget { internal static bool IsPreparingPresentation; }
internal static class ScenarioEnvironmentBudget { internal static bool IsPreparingPresentation=>false; }
internal static class ScenarioFigureDetailBudget { internal static bool IsPreparingPresentation=>false; }
}
