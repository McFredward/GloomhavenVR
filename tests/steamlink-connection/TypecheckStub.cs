#if NETFRAMEWORK
namespace GloomhavenVR.Core
{
    // Only the logging sink is stubbed; JsonUtility is the actual game Unity API.
    internal static class VRLog { internal static bool WantsDebug => false; internal static void Debug(string area,string text) { } internal static void Info(string area,string text) { } internal static void Warn(string area,string text) { } }
}
#endif
