#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Android;
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
        static int pollCursor;
        static string activeOperation;
        const double SampleSeconds = .25;
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
            if (!Enabled || EditorApplication.timeSinceStartup - lastSample < SampleSeconds) return;
            lastSample = EditorApplication.timeSinceStartup;
            int count = 0, seen = 0;
            foreach (var item in Progress.EnumerateItems())
            {
                if (!item.running) continue;
                if (seen++ < pollCursor) continue;
                if (count++ >= 16) break;
                ObserveOne(item);
            }
            pollCursor = count > 16 ? seen - 1 : 0;
        }

        static void Observe(Progress.Item[] items)
        {
            if (!Enabled) return;
            int count = 0;
            foreach (var item in items)
            {
                if (item == null) continue;
                // A large native completion batch must not lose its terminal
                // tail: those items no longer appear in the running poll.
                if (item.running && count++ >= 128) continue;
                ObserveOne(item);
            }
        }

        static void ObserveOne(Progress.Item item)
        {
            if (item == null || !item.exists) return;
            bool succeeded = item.status == Progress.Status.Succeeded;
            bool failed = item.status == Progress.Status.Failed || item.status == Progress.Status.Canceled;
            if (!item.running && !succeeded && !failed) return;
            double done = 0, total = 1;
            string unit = "tasks";
            if (!item.indefinite && item.totalSteps > 0 && item.currentStep >= 0 && item.currentStep <= item.totalSteps)
            { done = item.currentStep; total = item.totalSteps; unit = "steps"; }
            else if (!item.indefinite && !float.IsNaN(item.progress) && !float.IsInfinity(item.progress) && item.progress >= 0 && item.progress <= 1)
            { done = item.progress; total = 1; unit = "native fraction"; }
            if (succeeded) done = total;
            string status = succeeded ? "complete" : failed ? "failed" : "progress";
            string detail = "Unity: " + item.name + (string.IsNullOrEmpty(item.description) ? "" : " — " + item.description);
            if (unit == "tasks") detail += " [indivisible-task]";
            string signature = done.ToString(CultureInfo.InvariantCulture) + ":" + total + ":" + item.parentId + ":" + status + ":" + detail;
            string previous;
            if (Last.TryGetValue(item.id, out previous) && previous == signature) return;
            double previousTime;
            if (!succeeded && !failed && LastReported.TryGetValue(item.id, out previousTime) && Clock.Elapsed.TotalSeconds - previousTime < SampleSeconds) return;
            if (Last.Count >= 128 && !Last.ContainsKey(item.id)) { Last.Clear(); LastReported.Clear(); }
            Last[item.id] = signature;
            LastReported[item.id] = Clock.Elapsed.TotalSeconds;
            // Native identities keep independent tasks and their totals separate.
            Publish("unity-progress:" + item.id + ":" + item.parentId, detail, done, total, unit, status: status);
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
                if (!Enabled || Clock.Elapsed.TotalSeconds - last < SampleSeconds) return;
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

        /// <summary>
        /// Drain verified helper output while its native read/pack work is still
        /// running. Reading stdout to its end before stderr used to hide all
        /// child progress and could block a helper whose error pipe filled up.
        /// Callbacks only queue bounded records; Unity logging stays on the
        /// Editor thread. The caller still owns the exit/receipt validation.
        /// </summary>
        internal static string RunObservedProcess(System.Diagnostics.Process process)
        {
            const int MaxQueued = 64, MaxLine = 8192, MaxError = 4096;
            var lines = new Queue<string>();
            var errors = new StringBuilder();
            object gate = new object();
            process.OutputDataReceived += (sender, args) =>
            {
                string line = args.Data;
                if (!Enabled || line == null || line.Length > MaxLine || !line.StartsWith("GHVRQ_PROGRESS ", StringComparison.Ordinal)) return;
                lock (gate)
                {
                    if (lines.Count >= MaxQueued) lines.Dequeue();
                    lines.Enqueue(line);
                }
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data == null) return;
                lock (gate)
                {
                    if (errors.Length >= MaxError) return;
                    errors.Append(args.Data, 0, Math.Min(args.Data.Length, MaxError - errors.Length));
                    if (errors.Length < MaxError) errors.Append('\n');
                }
            };
            Action drain = () =>
            {
                lock (gate) { while (lines.Count != 0) Debug.Log(lines.Dequeue()); }
            };
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            while (!process.WaitForExit(100)) drain();
            // The timed overload does not wait for the asynchronous stdout and
            // stderr readers. Join them before publishing the final tail.
            process.WaitForExit();
            drain();
            return errors.ToString();
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

    /// <summary>Observe the actual Gradle graph, including cached and skipped tasks.</summary>
    public sealed class QuestWizardGradleProgress : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder { get { return int.MaxValue - 1; } }
        const string Marker = "// GHVRQ actual Gradle graph progress";
        internal const string GraphScript = @"
import groovy.json.JsonOutput
def ghvrqFile = new File('__GHVRQ_PATH__')
def ghvrqPlanned = new LinkedHashSet()
def ghvrqCompleted = new LinkedHashSet()
def ghvrqGraphId = java.util.UUID.randomUUID().toString()
def ghvrqWarned = false
def ghvrqReport = { status, detail ->
    def line = 'GHVRQ_PROGRESS ' + JsonOutput.toJson([schema:1,phase:'unity-gradle-tasks',done:ghvrqCompleted.size(),total:ghvrqPlanned.size(),unit:'tasks',detail:'[gradle-graph:' + ghvrqGraphId + '] ' + detail,status:status,operation:'player'])
    try { ghvrqFile.append(line + '\n', 'UTF-8') }
    catch (Exception error) { if (!ghvrqWarned) { ghvrqWarned = true; logger.warn('Quest Gradle progress sidecar is unavailable; build execution continues.') } }
}
gradle.taskGraph.whenReady { graph ->
    synchronized(ghvrqFile) {
        ghvrqPlanned.addAll(graph.allTasks.collect { it.path })
        ghvrqReport('start', 'Gradle task graph ready')
    }
}
gradle.taskGraph.beforeTask { task ->
    synchronized(ghvrqFile) { if (ghvrqPlanned.contains(task.path)) ghvrqReport('progress', 'Gradle: ' + task.path) }
}
gradle.taskGraph.afterTask { task, state ->
    synchronized(ghvrqFile) {
        if (ghvrqPlanned.contains(task.path)) {
            if (state.failure == null) ghvrqCompleted.add(task.path)
            ghvrqReport(state.failure == null ? (ghvrqCompleted.size() == ghvrqPlanned.size() ? 'complete' : 'progress') : 'failed', 'Gradle: ' + task.path + (state.skipped ? ' (retained/skipped)' : ''))
        }
    }
}
";
        internal static string Script(string sidecar)
        {
            return GraphScript.Replace("__GHVRQ_PATH__", sidecar.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n"));
        }
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (Environment.GetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS") != "1") return;
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                int index = Array.FindIndex(arguments, value => string.Equals(value, "-logFile", StringComparison.OrdinalIgnoreCase));
                if (index < 0 || index + 1 >= arguments.Length || arguments[index + 1] == "-") return;
                string sidecar = Path.GetFullPath(arguments[index + 1]) + ".gradle-progress.jsonl";
                string root = Directory.GetParent(Path.GetFullPath(path)).FullName;
                string build = Path.Combine(root, "build.gradle");
                if (!File.Exists(build)) return;
                // Unity buffers Gradle stdout; this current-invocation sidecar
                // exposes live counters without executing a second Gradle job.
                File.WriteAllText(sidecar, "", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(root, "quest-wizard-progress.gradle"), Script(sidecar), new UTF8Encoding(false));
                if (!File.ReadAllText(build).Contains(Marker))
                    File.AppendAllText(build, "\n" + Marker + "\nif (System.getenv('GHVRQ_WIZARD_PROGRESS') == '1') { apply from: rootProject.file('quest-wizard-progress.gradle') }\n", new UTF8Encoding(false));
            }
            catch (Exception)
            {
                Debug.LogWarning("[Quest build] Gradle progress observation is unavailable; original build execution continues.");
            }
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
                    1, 1, "steps", "player", "complete");
        }
    }
}
#endif
