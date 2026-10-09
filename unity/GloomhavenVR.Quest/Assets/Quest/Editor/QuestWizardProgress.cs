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
        static string activeOperation;
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
            if (!complete) activeOperation = name;
            Publish("operation:" + name, detail, complete ? 1 : -1, complete ? 1 : -1, "operations", name,
                    complete ? "complete" : "start");
        }

        /// <summary>Count existing work only; observing never loads or hashes an additional asset.</summary>
        public sealed class Counter
        {
            readonly string phase, operation, unit;
            readonly long total;
            long done;
            double last = double.NegativeInfinity;
            public Counter(string phase, string operation, long total, string unit, string detail)
            {
                this.phase = phase; this.operation = operation; this.total = total; this.unit = unit;
                Publish(phase, detail, 0, total, unit, operation, "start");
            }
            public void Report(long completed, string detail)
            {
                done = completed;
                if (!Enabled || Clock.Elapsed.TotalSeconds - last < .5) return;
                last = Clock.Elapsed.TotalSeconds;
                Publish(phase, detail, done, total, unit, operation);
            }
            public void Complete(string detail)
            {
                // Completion belongs to this actual child loop, never its
                // Editor/Player owner. A failed last task must remain open.
                Publish(phase, detail, total, total, unit, operation, "complete");
            }
        }

        public sealed class TaskSequence
        {
            readonly string phase, operation;
            readonly int total;
            int done;
            public TaskSequence(string phase, string operation, int total)
            { this.phase = phase; this.operation = operation; this.total = total; }
            public void Run(string detail, Action action)
            {
                Publish(phase, detail, done, total, "steps", operation, done == 0 ? "start" : "progress");
                try { action(); }
                catch (Exception)
                { Publish(phase, detail, done, total, "steps", operation, "failed"); throw; }
                ++done;
                Publish(phase, detail, done, total, "steps", operation, done == total ? "complete" : "progress");
            }
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
                      ",\"status\":" + Quote(status) + (operation == null && activeOperation == null ? "" : ",\"operation\":" + Quote(operation ?? activeOperation)) + "}");
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
        int processedScenes;
        public int callbackOrder { get { return int.MinValue; } }
        public void OnPreprocessBuild(BuildReport report)
        {
            processedScenes = 0;
            QuestWizardProgress.Operation("player", false, "Unity Player build: processing scenes, IL2CPP and native Android compilation.");
        }
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Addressables also processes scenes with a null Player report.
            // Do not let those scenes enter the later Player scene counter.
            if (report == null) return;
            ++processedScenes;
            int total = Array.FindAll(EditorBuildSettings.scenes, row => row.enabled).Length;
            QuestWizardProgress.Publish("unity-player-scenes", "Unity Player scene: " + scene.path,
                processedScenes, total, "scenes", "player");
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.result == BuildResult.Succeeded)
                QuestWizardProgress.Publish("unity-player-native-result", "Native Android Player succeeded; original final evidence is still being written.",
                    1, 1, "steps", "player");
        }
    }
}
#endif
