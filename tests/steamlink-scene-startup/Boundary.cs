using System;
using System.Collections.Generic;
using System.Reflection;

// Only external Unity/XR/GPU boundaries are supplied here. The entire shipping
// SteamLinkSceneRuntime and all SceneHeadOutput lifecycle/callback bodies compile
// unchanged. Real material/coverage/GPU behavior is covered by loading-source.
namespace UnityEngine
{
    internal class Object { internal static void Destroy(Object value) { } }
    internal class GameObject
    {
        internal int Components;
        internal T AddComponent<T>() where T : MonoBehaviour,new()
        { Components++; return new T { gameObject=this }; }
    }
    internal class MonoBehaviour : Object { internal GameObject gameObject=new GameObject(); }
    [AttributeUsage(AttributeTargets.Class)] internal sealed class RequireComponent : Attribute
    { internal RequireComponent(Type type) { } }
    internal enum StereoTargetEyeMask { None,Left,Right,Both }
    internal enum CameraClearFlags { SolidColor,Depth }
    internal enum RenderingPath { Forward,DeferredShading }
    internal enum ColorSpace { Gamma,Linear }
    internal sealed class Camera : MonoBehaviour
    {
        internal enum MonoOrStereoscopicEye { Left,Right,Mono }
        internal bool isActiveAndEnabled=true,stereoEnabled=true,allowHDR=false;
        internal StereoTargetEyeMask stereoTargetEye=StereoTargetEyeMask.Both;
        internal MonoOrStereoscopicEye stereoActiveEye=MonoOrStereoscopicEye.Left;
        internal CameraClearFlags clearFlags=CameraClearFlags.SolidColor;
        internal RenderingPath actualRenderingPath=RenderingPath.Forward;
        internal RenderTexture? targetTexture=null;
        internal static event Action<Camera>? onPreRender,onPostRender;
        internal static void Begin(Camera head) => onPreRender?.Invoke(head);
        internal static void End(Camera head) => onPostRender?.Invoke(head);
    }
    internal sealed class RenderTexture : Object
    { internal int width=4040,height=4040,antiAliasing=8;internal string graphicsFormat="R8G8B8A8_UNorm"; }
    internal sealed class Shader : Object { internal string name="fixture"; }
    internal sealed class Renderer : Object { }
    internal static class QualitySettings { internal static ColorSpace activeColorSpace=ColorSpace.Gamma; }
    internal static class Time { internal static float unscaledTime=0;internal static int frameCount=100; }
    internal static class Graphics
    {
        internal static int Ordinary;
        internal static void Blit(RenderTexture source,RenderTexture? destination) => Ordinary++;
    }
}
namespace UnityEngine.Rendering
{ internal static class GraphicsSettings { internal static object? currentRenderPipeline=null; } }
namespace UnityEngine.XR
{
    internal static class XRSettings
    { internal enum StereoRenderingMode { MultiPass,SinglePass } internal static StereoRenderingMode stereoRenderingMode=StereoRenderingMode.MultiPass; internal static int eyeTextureWidth=4040,eyeTextureHeight=4040; }
}
namespace GloomhavenVR.Core
{
    internal sealed class Choice { internal bool Value=true; }
    internal static class MixedReality { internal static Choice SteamLinkNative=new Choice(),Enabled=new Choice(),SteamLinkEdgeQuality=new Choice(); }
    internal static class VRSession { internal static bool IsRunning=true; }
    internal static class FrameNativePassthrough { internal static bool Required=false; }
    internal static class VRCameraPolicy { internal static UnityEngine.Camera? AllowedHead; }
    internal static class StereoModeConfig { internal enum Mode { MultiPass,Other } internal static Mode Current=Mode.MultiPass; }
    internal static class MrNativeCamera { internal static void Apply(UnityEngine.Camera head) { } }
    internal static class VRLog
    {
        internal static bool WantsDebug=true;
        internal static readonly List<string> Warnings=new List<string>();
        internal static void Info(string category,string message) { }
        internal static void Warn(string category,string message) => Warnings.Add(message);
        internal static void Debug(string category,string message) { }
    }
}
namespace GloomhavenVR.WorldUI
{ internal static class FlatScreen { internal static UnityEngine.Renderer? OpaqueBackdrop=>null; } }
namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkConnection { internal static bool Available=true; }
    internal static class SteamLinkCoverageBridge
    {
        internal static bool CanAcceptOrdinaryRgba=false;
        internal static void Reset() { CanAcceptOrdinaryRgba=false; }
    }
    // The actual lease state machine is compiled/executed by stream-quality.
    // This fixture supplies only its current metadata authority, never a timer
    // or a pretend actual-encoder acknowledgement.
    internal static class SteamLinkStreamQuality
    {
        internal static bool LegacyReleased=true;
        internal static bool Prepared=true;
        internal static int Width=4040,Height=4040;
        internal static bool HostPrepared(bool enabled,int width,int height)
            => !enabled || Prepared && width==Width && height==Height;
    }
    internal static class SceneAtlasProfile
    { internal const string RobustShaderName="robust",FullShaderName="full",RobustProfile="v3",FullProfile="v2",Profile="v1"; }
    internal static class ScenePayloadAssets
    {
        internal static bool TryGet(out UnityEngine.Shader pack,out UnityEngine.Shader coverage,out string reason)
        { pack=new UnityEngine.Shader();coverage=new UnityEngine.Shader();reason="";return true; }
        internal static UnityEngine.Shader? FindVariant(string name)=>new UnityEngine.Shader();
    }
    internal sealed class SceneEyeCoverage : IDisposable
    {
        internal enum Refusal { None }
        internal bool Ready;internal Refusal Reason=>Refusal.None;
        internal SceneEyeCoverage(UnityEngine.Shader shader) { }
        internal void SetOpaqueBackdropSource(Func<UnityEngine.Renderer?> source) { }
        internal bool Arm(UnityEngine.Camera head,ulong epoch) { Ready=true;return true; }
        internal bool IsArmedFor(UnityEngine.Camera head,ulong epoch)=>Ready;
        internal void Disarm() => Ready=false;
        public void Dispose() => Ready=false;
    }
    internal enum SceneEyeResult { Rendered,UnknownEye,SourceFormat }
    internal sealed class SceneEyeOutput : IDisposable
    {
        internal static int Packed,Ordinary;
        internal static SceneEyeResult Next=SceneEyeResult.Rendered;
        internal string Profile=>"v3";
        internal SceneEyeOutput(UnityEngine.Shader shader) { }
        internal SceneEyeResult TryRender(UnityEngine.RenderTexture source,UnityEngine.RenderTexture destination,UnityEngine.Camera.MonoOrStereoscopicEye eye)
        { if(Next==SceneEyeResult.Rendered)Packed++;return Next; }
        internal SceneEyeResult TryRenderOrdinary(UnityEngine.RenderTexture source,UnityEngine.RenderTexture destination,UnityEngine.Camera.MonoOrStereoscopicEye eye)
        { if(Next==SceneEyeResult.Rendered)Ordinary++;return Next; }
        public void Dispose() { }
    }
    internal sealed class SceneTransparentMaterials : IDisposable
    {
        internal enum Refusal { None }
        internal bool Ready=true;internal Refusal Reason=>Refusal.None;
        internal SceneTransparentMaterials(Func<string,UnityEngine.Shader?> find) { }
        internal bool BindHead(UnityEngine.Camera head)=>true;
        internal bool RefreshCold()=>true;
        internal void EnterHead(UnityEngine.Camera head) { }
        internal void ExitHead(UnityEngine.Camera head) { }
        internal void DisarmImmediate() => Ready=false;
        public void Dispose() => Ready=false;
    }
}

namespace GloomhavenVR.Core.SteamLink
{
    // The optional Cheats probe owner has a separate lifecycle/real-Unity
    // fixture. Existing source tests keep the normal MR input unchanged.
    internal static class SteamLinkProjectionProbe
    {
        internal static void Tick() { }
        internal static void Stop() { }
        internal static void PackFailed() { }
        internal static UnityEngine.RenderTexture InputForFrame(UnityEngine.RenderTexture source,int frame) => source;
    }
}
