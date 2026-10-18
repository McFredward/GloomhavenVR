using System;
using System.IO;
using System.Threading.Tasks;
namespace UnityEngine
{
    internal class Object { internal static int Destroyed=0; }
    internal static class GL { internal static int Clears=0; }
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
        internal static Action? TestAction,TestStopAction;
        internal static int CloseCalls;internal static bool ThrowClose=false;
        internal static void Close() { CloseCalls++;if(ThrowClose)throw new InvalidOperationException("fixture close refusal");IsOpen=false; }
        internal static void SetCheats(bool value) => _cheatsEnabled!.Value=value;
        internal static int BuildTest() => BuildSteamLinkProjectionProbe();
        private static void BuildHeader(object root,string label,bool sub) { }
        private static void BuildNote(object root,string label,bool multiline) { }
        private static UnityEngine.GameObject BuildLinkRow(object root,string caption,Action action,bool asAction) { if(caption==Core.Loc.SteamLinkProjectionProbeStop)TestStopAction=action;else TestAction=action;return new UnityEngine.GameObject(); }
        private static void RegisterCheatRow(UnityEngine.GameObject row,Func<string> caption) { }
    }
}
