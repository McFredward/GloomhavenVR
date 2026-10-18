using System;
using System.Text.Json;
namespace UnityEngine
{
    internal static class JsonUtility
    {
        internal static T FromJson<T>(string raw) => JsonSerializer.Deserialize<T>(raw,
            new JsonSerializerOptions { IncludeFields = true })!;
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Info(string category,string text) { } }
}
namespace GloomhavenVR.Core.SteamLink
{
    // Public-path parsing is separately exercised by steamlink-connection.
    internal static class SteamLinkConnection
    {
        internal static string CacheFolder(string? environment,Func<string> known) => environment ?? known();
        internal static bool LocalWindowsPath(string path) => path != null && path.Length >= 4
            && path.Length < 1024 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\'
            && !path.Contains("..") && !path.Contains("/") && !path.Contains("\"");
    }
}
