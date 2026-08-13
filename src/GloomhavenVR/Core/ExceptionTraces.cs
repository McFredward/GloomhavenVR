using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Gives every logged exception its managed stack back.
///
/// <para>WHY THIS EXISTS (2026-08-13, restart-round investigation). Player.log is full of lines
/// that read exactly like this and nothing else:</para>
/// <code>
/// NullReferenceException: Object reference not set to an instance of an object
/// </code>
/// <para>No stack, no type, no owner — 1172 of them in the ModBuild 134 run alone. That is NOT
/// Unity failing to capture a native-invoked callback, which is what a stackless exception
/// usually means. READ FROM SOURCE: the game switches stack traces off for EVERY log type at
/// boot — <c>GloomhavenShared.LogBuildInfo()</c> (decompiled/GH.Shared/GloomhavenShared.cs:57-61)
/// runs five <c>Application.SetStackTraceLogType(..., StackTraceLogType.None)</c> calls, the last
/// of which is <c>LogType.Exception</c>. Its own marker line ("[INFO] Important Defines:") sits
/// at Player.log:214, i.e. AFTER the mod is loaded, so setting this once in <c>Plugin.Awake</c>
/// would simply be overwritten.</para>
///
/// <para>The consequence is that NOTHING in this project can be attributed from a log: a throw in
/// a mod <c>OnDestroy</c>, in a Harmony postfix, or in one of the game's own scene-teardown
/// callbacks all print the same single anonymous line. Every round that has chased a bare NRE has
/// been chasing it blind. Turning the traces back on costs nothing per frame — Unity builds a
/// stack only when an exception is actually logged — and the traces land in Player.log only
/// (BepInEx's own file, LogOutput.log, never receives Unity's exception channel), so the mod log
/// the reports are read from does not grow at all.</para>
///
/// <para>ONLY <see cref="LogType.Exception"/> is re-enabled. The game also mutes Log/Warning/
/// Assert/Error traces, and those channels carry its ordinary chatter — restoring them would add
/// a stack to tens of thousands of routine lines for no diagnostic gain.</para>
///
/// <para>REVERSIBLE: <see cref="Shutdown"/> writes the game's authored value
/// (<see cref="StackTraceLogType.None"/>, read from the source above) straight back, so a hot
/// reload leaves the process exactly as the game configured it.</para>
///
/// <para>MP-SAFE: a log-formatting setting. No game state, no net traffic, no rendering.</para>
/// </summary>
internal static class ExceptionTraces
{
    private const string Name = "Core";

    private static bool _installed;

    /// <summary>Arm the override and keep it armed across scene loads (idempotent).</summary>
    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        Apply();
        // The game only calls LogBuildInfo() once, at boot — but a scene load is the cheapest
        // place to re-assert a setting we do not own, and it costs one call per load.
        SceneManager.sceneLoaded += OnSceneLoaded;

        VRLog.Info(Name,
            "Exception stack traces RE-ENABLED (LogType.Exception → ScriptOnly). The game turns "
            + "every stack trace off at boot (GloomhavenShared.LogBuildInfo), which is why every "
            + "NullReferenceException in Player.log so far has been a single anonymous line with "
            + "no owner. From now on each one carries its managed stack in Player.log — that is "
            + "what says whether a throw is the mod's or the game's. Log/Warning/Assert/Error "
            + "stay as the game set them; only the exception channel is restored.");
    }

    /// <summary>Put the game's authored setting back (hot reload / shutdown).</summary>
    public static void Shutdown()
    {
        if (!_installed)
            return;
        _installed = false;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    private static void Apply() =>
        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
}
