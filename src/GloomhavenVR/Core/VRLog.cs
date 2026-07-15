using BepInEx.Logging;

namespace GloomhavenVR.Core;

/// <summary>
/// Structured logging wrapper over the plugin's BepInEx <see cref="ManualLogSource"/>.
/// Adds an optional scope prefix (module name) so log lines are grep-able:
/// <c>[Info   :GloomhavenVR] [Rig] ...</c>.
/// </summary>
internal static class VRLog
{
    private static ManualLogSource? _log;

    internal static void Init(ManualLogSource log) => _log = log;

    internal static void Debug(string message) => _log?.LogDebug(message);
    internal static void Info(string message) => _log?.LogInfo(message);
    internal static void Warn(string message) => _log?.LogWarning(message);
    internal static void Error(string message) => _log?.LogError(message);

    internal static void Debug(string scope, string message) => _log?.LogDebug($"[{scope}] {message}");
    internal static void Info(string scope, string message) => _log?.LogInfo($"[{scope}] {message}");
    internal static void Warn(string scope, string message) => _log?.LogWarning($"[{scope}] {message}");
    internal static void Error(string scope, string message) => _log?.LogError($"[{scope}] {message}");
}
