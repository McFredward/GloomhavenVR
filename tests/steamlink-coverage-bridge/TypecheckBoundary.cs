using System;
namespace GloomhavenVR.Core
{ internal static class VRLog { internal static void Info(string category,string text) { } } }
namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkConnection
    {
        internal static string CacheFolder(string? environment,Func<string> known) => environment ?? known(); internal static bool LocalWindowsPath(string value) => false; }
}
