using System;
using System.Collections.Generic;
using BepInEx.Logging;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.Core;

/// <summary>Bounded startup verdict for the real module chain in the Android diagnostic.</summary>
public static class QuestStandaloneModuleHealth
{
    private static readonly List<string> Failures = new();
    public static int CompletedModules { get; private set; }
    public static bool InitializationComplete { get; private set; }
    public static string Failure => string.Join("; ", Failures);
    public static bool InviteKeyboardVisible => QuestStandalonePlatform.Enabled && VRKeyboard.IsShowing;

    internal static void Record(string module, Exception? failure)
    {
        if (!QuestStandalonePlatform.Enabled) return;
        if (failure == null) CompletedModules++;
        else if (Failures.Count < 16)
        {
            string text = module + ": " + failure.GetType().Name + ": " + failure.Message;
            Failures.Add(text.Length > 500 ? text.Substring(0, 500) : text);
        }
    }

    internal static void Complete()
    {
        if (QuestStandalonePlatform.Enabled) InitializationComplete = true;
    }

    /// <summary>The existing BepInEx logger feeds Unity's bounded standalone capture sink.</summary>
    public static void InstallUnityLogListener()
    {
        if (!QuestStandalonePlatform.Enabled) throw new InvalidOperationException("Quest player is not configured.");
        if (_listener != null) return;
        _listener = new UnityLogListener();
        Logger.Listeners.Add(_listener);
    }

    private static UnityLogListener? _listener;
    private sealed class UnityLogListener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            string message = "[GloomhavenVR] " + eventArgs.Source.SourceName + ": " + eventArgs.Data;
            if ((eventArgs.Level & (LogLevel.Fatal | LogLevel.Error)) != 0) UnityEngine.Debug.LogError(message);
            else if ((eventArgs.Level & LogLevel.Warning) != 0) UnityEngine.Debug.LogWarning(message);
            else UnityEngine.Debug.Log(message);
        }
        public void Dispose() { }
    }
}
