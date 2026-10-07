#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>
    /// Observes Unity 2021.3's public Progress tasks and Player build callbacks.
    /// It never creates/cancels Editor tasks, refreshes assets or compiles extra
    /// shader variants. Unity does not expose one reliable end-to-end percentage:
    /// these task counters supplement the host's durable scheduled-work plan.
    /// </summary>
    [InitializeOnLoad]
    public static class QuestWizardProgress
    {
        static readonly Dictionary<int, string> Last = new Dictionary<int, string>();
        static readonly Dictionary<int, double> LastReported = new Dictionary<int, double>();
        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        static double lastSample;
        static bool Enabled { get { return Environment.GetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS") == "1"; } }

        static QuestWizardProgress()
        {
            if (!Enabled) return;
            Progress.added += Observe;
            Progress.updated += Observe;
            EditorApplication.update += Poll;
            Publish("unity-editor-ready", "Unity Editor scripting domain loaded; importing project assets.");
        }

        static void Poll()
        {
            if (!Enabled || EditorApplication.timeSinceStartup - lastSample < .5) return;
            lastSample = EditorApplication.timeSinceStartup;
            int count = 0;
            foreach (var item in Progress.EnumerateItems())
            {
                if (count++ >= 16) break;
                ObserveOne(item);
            }
        }

        static void Observe(Progress.Item[] items)
        {
            if (!Enabled) return;
            int count = 0;
            foreach (var item in items)
            {
                if (count++ >= 16) break;
                ObserveOne(item);
            }
        }

        static void ObserveOne(Progress.Item item)
        {
            if (item == null || !item.exists || !item.running) return;
            double done = -1, total = -1;
            string unit = null;
            if (!item.indefinite && item.totalSteps > 0 && item.currentStep >= 0 && item.currentStep <= item.totalSteps)
            { done = item.currentStep; total = item.totalSteps; unit = "steps"; }
            else if (!item.indefinite && !float.IsNaN(item.progress) && !float.IsInfinity(item.progress) && item.progress >= 0 && item.progress <= 1)
            { done = Math.Round(item.progress * 1000, 3); total = 1000; unit = "Unity task units"; }
            string detail = "Unity: " + item.name + (string.IsNullOrEmpty(item.description) ? "" : " — " + item.description);
            string signature = done.ToString(CultureInfo.InvariantCulture) + ":" + total + ":" + detail;
            string previous;
            if (Last.TryGetValue(item.id, out previous) && previous == signature) return;
            double previousTime;
            if (LastReported.TryGetValue(item.id, out previousTime) && Clock.Elapsed.TotalSeconds - previousTime < .5) return;
            if (Last.Count >= 128 && !Last.ContainsKey(item.id)) { Last.Clear(); LastReported.Clear(); }
            Last[item.id] = signature;
            LastReported[item.id] = Clock.Elapsed.TotalSeconds;
            Publish("unity-progress", detail, done, total, unit);
        }

        public static void Operation(string name, bool complete, string detail)
        {
            Publish("operation:" + name, detail, complete ? 1 : -1, complete ? 1 : -1, "operations", name,
                    complete ? "complete" : "start");
        }

        internal static void Publish(string phase, string detail, double done = -1, double total = -1,
                                     string unit = null, string operation = null, string status = "progress")
        {
            if (!Enabled) return;
            string counts = done >= 0 && total >= done && !double.IsNaN(done) && !double.IsInfinity(done) && !double.IsInfinity(total)
                ? "\"done\":" + done.ToString(CultureInfo.InvariantCulture) + ",\"total\":" + total.ToString(CultureInfo.InvariantCulture)
                : "\"done\":null,\"total\":null";
            Debug.Log("GHVRQ_PROGRESS {\"schema\":1,\"phase\":" + Quote(phase) + "," + counts +
                      ",\"unit\":" + (unit == null ? "null" : Quote(unit)) + ",\"detail\":" + Quote(detail) +
                      ",\"status\":" + Quote(status) + (operation == null ? "" : ",\"operation\":" + Quote(operation)) + "}");
        }

        static string Quote(string text)
        {
            var result = new StringBuilder("\"");
            foreach (char character in (text ?? ""))
            {
                if (result.Length >= 1024) break;
                if (character == '\\' || character == '"') { result.Append('\\'); result.Append(character); }
                else if (character < 32) result.Append(' ');
                else result.Append(character);
            }
            return result.Append('"').ToString();
        }
    }

    public sealed class QuestWizardBuildProgress : IPreprocessBuildWithReport, IProcessSceneWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return int.MinValue; } }
        public void OnPreprocessBuild(BuildReport report)
        { QuestWizardProgress.Operation("player", false, "Unity Player build: processing scenes, IL2CPP and native Android compilation."); }
        public void OnProcessScene(Scene scene, BuildReport report)
        { QuestWizardProgress.Publish("unity-player-scene", "Unity Player scene: " + scene.path); }
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.result == BuildResult.Succeeded)
                QuestWizardProgress.Operation("player", true, "Unity Android Player build succeeded; host delivery verification follows.");
        }
    }
}
#endif
