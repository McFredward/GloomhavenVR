using System;
using System.IO;
using System.Threading.Tasks;
namespace UnityEngine.Experimental.Rendering { internal enum GraphicsFormat { R8G8B8A8_UNorm } }
namespace UnityEngine.Rendering { internal enum TextureDimension { Tex2D } }
namespace UnityEngine
{
    internal class Object { internal static int Destroyed; internal static void Destroy(Object value) { Destroyed++; } }
    internal enum HideFlags { HideAndDontSave }
    internal struct Color { internal static Color clear => default; }
    internal struct RenderTextureDescriptor
    {
        internal int width,height,msaaSamples,volumeDepth;internal bool useMipMap,autoGenerateMips,useDynamicScale;
        internal Rendering.TextureDimension dimension;internal Experimental.Rendering.GraphicsFormat graphicsFormat;
        internal RenderTextureDescriptor(int w,int h,Experimental.Rendering.GraphicsFormat format,int depth)
        { width=w;height=h;graphicsFormat=format;msaaSamples=volumeDepth=1;useMipMap=autoGenerateMips=useDynamicScale=false;dimension=Rendering.TextureDimension.Tex2D; }
    }
    internal sealed class RenderTexture : Object
    {
        internal static RenderTexture? active;
        internal readonly RenderTextureDescriptor Descriptor; internal HideFlags hideFlags;
        internal bool Created=true;
        internal RenderTexture(RenderTextureDescriptor descriptor) { Descriptor=descriptor; }
        internal bool Create() => Created;
        internal bool IsCreated() => Created;
    }
    internal static class GL { internal static bool sRGBWrite=true;internal static int Clears;internal static void Clear(bool depth,bool color,Color value) { Clears++; } }
    internal static class Time { internal static int frameCount; }
    internal sealed class GameObject { }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static bool WantsDebug=true;internal static void Debug(string a,string b) { } }
    internal static class VRSession { internal static bool IsRunning=true; }
    internal sealed class Choice { internal bool Value=true; }
    internal static class MixedReality { internal static readonly Choice Enabled=new Choice(); }
    internal static partial class Loc { internal static string CurrentLanguage="English"; }
}
namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkSceneRuntime { internal static bool Requested=true,ProjectionProbeSourceReady=true; }
    internal static class SteamLinkConnection
    {
        internal const string ApiResource="api",ApiSha256="hash"; internal const int ApiBytes=0;
        internal static string? PrepareVerifiedResource(string r,int b,string h,string f,bool d,ref string s) => throw new InvalidOperationException("No native process in fixture");
    }
    internal static class SteamLinkCompositorMonitor
    {
        internal const int StopGraceMs=5000;
        internal interface IChild { TextReader Output { get; } Task Exited { get; } Task<bool> StopAsync(); }
        internal sealed class OwnershipUnknownException : IOException { }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static partial class VROptionsTab
    {
        internal static bool IsOpen=true;
        private static Core.Choice? _cheatsEnabled=new Core.Choice();
        private static bool CheatsAvailable => _cheatsEnabled!=null && _cheatsEnabled.Value;
        private static object? ContentRoot=new object();
        internal static Action? TestAction;
        internal static void SetCheats(bool value) => _cheatsEnabled!.Value=value;
        internal static int BuildTest() => BuildSteamLinkProjectionProbe();
        private static void BuildHeader(object root,string label,bool sub) { }
        private static void BuildNote(object root,string label,bool multiline) { }
        private static UnityEngine.GameObject BuildLinkRow(object root,string caption,Action action,bool asAction) { TestAction=action;return new UnityEngine.GameObject(); }
        private static void RegisterCheatRow(UnityEngine.GameObject row,Func<string> caption) { }
    }
}
