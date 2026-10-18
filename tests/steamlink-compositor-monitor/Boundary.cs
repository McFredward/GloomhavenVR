using System;
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static bool WantsDebug=false;
        internal static int Logs;
        internal static void Debug(string category,string text){Logs++;}
    }
}
namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkConnection
    {
        internal const string ApiResource="api",ApiSha256="";
        internal const int ApiBytes=0;
        internal static int Preparations;
        internal static string? PrepareVerifiedResource(string resource,int bytes,string hash,string name,bool dll,ref string stage)
        {Preparations++;throw new InvalidOperationException("Fixture never prepares native helper resources");}
    }
}
