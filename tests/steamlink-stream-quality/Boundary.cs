using System;
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Info(string category,string text){}internal static void Warn(string category,string text){} }
    internal static class VRSession { internal static bool IsRunning=true; }
    internal static class FrameNativePassthrough { internal static bool Required=false; }
    internal sealed class Flag { internal bool Value=true; }
    internal static class MixedReality { internal static Flag Enabled=new();internal static Flag SteamLinkNative=new(); }
}
namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkCompositorMonitor { internal static void Tick(bool requested){} internal static void Reset(){} }
    internal static class SteamLinkConnection
    {
        internal static bool Available=true;
        internal const string ApiResource="GloomhavenVR.SteamLink.openvr_api.dll",ApiSha256="";
        internal const int ApiBytes=0;
        internal static string? PrepareVerifiedResource(string resource,int bytes,string hash,string name,bool dll,ref string stage)
            =>throw new Exception("Real filesystem/API is outside the state fixture");
    }
}
