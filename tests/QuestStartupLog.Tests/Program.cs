using System.Reflection;
using System.Text.Json;
using GloomhavenVR.Quest;
using UnityEngine;

internal static class Program
{
    static int assertions;
    static void Check(bool condition, string detail)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(detail);
    }
    static void Invoke(QuestGameBootstrap bootstrap, string method) => typeof(QuestGameBootstrap).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bootstrap, null);
    static int Count(string text, string needle) => text.Split(needle, StringSplitOptions.None).Length - 1;
    static void Main(string[] args)
    {
        string root = args[0]; Directory.CreateDirectory(root);
        // Replay the observed B612 signature through the REAL bootstrap callback:
        // many identical input exceptions must not hide a later first loader cause.
        Application.persistentDataPath = Path.Combine(root, "bootstrap");
        Directory.CreateDirectory(Application.persistentDataPath);
        var bootstrap = new QuestGameBootstrap(); Invoke(bootstrap, "Awake");
        string path = Path.Combine(Application.persistentDataPath, "quest-startup.log");
        for (int i = 0; i < 1000; i++) Application.Emit("Pointer::get_delta missing", "input stack", LogType.Exception);
        Application.Emit("Rulebase initial loading failed", "loader first cause\n at worker.LoadRules()", LogType.Error);
        string text = File.ReadAllText(path);
        Check(Count(text, "Pointer::get_delta missing") == 1 && text.Contains("Rulebase initial loading failed"), "distinct-budget: duplicate input errors consumed the loader cause budget");
        Check(text.Contains("loader first cause\n at worker.LoadRules()"), "first-stack: original loader stack was not persisted");
        Invoke(bootstrap, "SaveState");
        using (var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(Application.persistentDataPath, "quest-startup-state.json"))))
            Check(state.RootElement.GetProperty("originalErrors").GetInt32() == 2, "distinct-budget: startup state counts duplicate errors instead of retained distinct causes");
        // The main thread deliberately waits rather than running Update. A worker
        // log must already be readable from disk when that worker completes.
        Task.Run(() => Application.Emit("worker-only load failure", "worker full stack", LogType.Error, mainThread: false)).GetAwaiter().GetResult();
        text = File.ReadAllText(path);
        Check(text.Contains("worker-only load failure") && text.Contains("worker full stack"), "worker-callback: diagnostic requires a main-thread callback or Update flush");
        Application.Emit("[Quest startup] fatal gate evidence", "gate stack", LogType.Error);
        Check(File.ReadAllText(path).Contains("gate stack"), "gate-stack: startup gate discarded the supplied stack");
        Application.Emit("ordinary frame", "", LogType.Log); Application.Emit("ordinary warning", "", LogType.Warning);
        text = File.ReadAllText(path);
        Check(!text.Contains("ordinary frame") && !text.Contains("ordinary warning"), "log-filter: ordinary original messages create diagnostic noise");
        Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++) Application.Emit("Checking YML CheckSums", "", LogType.Log, mainThread: false);
            Application.Emit("[YML] Finished parse for GlobalRules Duration: 4.2", "", LogType.Log, mainThread: false);
            Application.Emit("[SceneController] Entitlements refreshing...", "", LogType.Warning, mainThread: false);
            for (int i = 0; i < 100; i++) Application.Emit("[YML] Starting parse for library-" + i, "", LogType.Log, mainThread: false);
        }).GetAwaiter().GetResult();
        text = File.ReadAllText(path);
        Check(Count(text, "original phase Checking YML CheckSums") == 1 && text.Contains("Finished parse for GlobalRules Duration: 4.2"), "native-phases: source-proven worker milestones are not retained/deduplicated");
        Check(!text.Contains("Entitlements refreshing...") && !text.Contains("Starting parse for library-99"), "native-phase-bound: native per-frame or unbounded milestones were retained");
        Application.Emit("[DIAGNOSTICS]: Init Cards elapsed 24.5", "", LogType.Log);
        Check(File.ReadAllText(path).Contains("Init Cards elapsed 24.5"), "native-phase-reserve: repeated YAML phases swallowed the card initialization duration");
        typeof(QuestGameBootstrap).GetField("nativeStartupComplete", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrap, true);
        Application.Emit("LoadAlwaysLoadedAddressable after-menu", "", LogType.Log);
        Check(!File.ReadAllText(path).Contains("after-menu"), "native-phase-window: native gameplay milestones leak into startup evidence");
        Invoke(bootstrap, "OnDestroy");
        long size = new FileInfo(path).Length;
        Application.Emit("after destroy", "", LogType.Error, mainThread: false);
        Check(new FileInfo(path).Length == size, "unsubscribe: destroyed bootstrap retained the threaded callback");

        path = Path.Combine(root, "stack.log"); var stackLog = new QuestGameStartupLog(path, "stack fixture");
        stackLog.AppendOriginalError("same failure text", "first distinct call stack");
        stackLog.AppendOriginalError("same failure text", "second distinct call stack");
        stackLog.AppendOriginalError("same failure text", "first distinct call stack");
        text = File.ReadAllText(path);
        Check(stackLog.OriginalErrors == 2 && text.Contains("first distinct call stack") && text.Contains("second distinct call stack"), "stack-identity: equal messages from distinct stacks were collapsed");
        stackLog.AppendOriginalError("IL2CPP supplied no stack", "");
        Check(File.ReadAllText(path).Contains("[stack not supplied by Unity]"), "missing-stack: absent runtime stack was silently omitted");

        path = Path.Combine(root, "reserved.log"); var reserved = new QuestGameStartupLog(path, "retained build/input provenance");
        for (int i = 0; i < QuestGameStartupLog.MaxRecords * 2; i++) reserved.Append("scene transition " + i);
        reserved.AppendOriginalError("first original loader failure after lifecycle cap", "retained first cause stack");
        text = File.ReadAllText(path);
        Check(text.Contains("first original loader failure after lifecycle cap") && text.Contains("retained first cause stack"), "lifecycle-reserve: routine lifecycle saturation swallowed original loader evidence");
        Check(text.Contains("retained build/input provenance") && new FileInfo(path).Length <= QuestGameStartupLog.MaxBytes, "combined-bound: lifecycle/error partition lost provenance or exceeded total bound");
        reserved.Append("[Quest startup] original scene loaded=fixture", "unused routine stack");
        text = File.ReadAllText(path);
        Check(text.Contains("original scene loaded=fixture") && !text.Contains("unused routine stack"), "startup-reserve: mod trace cap swallowed native startup evidence");

        path = Path.Combine(root, "causes.log"); var causes = new QuestGameStartupLog(path, "cause cap");
        for (int i = 0; i < QuestGameStartupLog.MaxOriginalErrors * 2; i++) causes.AppendOriginalError("cause " + i, "stack " + i);
        text = File.ReadAllText(path); size = new FileInfo(path).Length;
        Check(causes.OriginalErrors == QuestGameStartupLog.MaxOriginalErrors && Count(text, "original error log limit reached") == 1, "error-record-bound: first-cause count/cap marker is incorrect");
        causes.AppendOriginalError("new beyond cap", "late stack");
        Check(new FileInfo(path).Length == size && !File.ReadAllText(path).Contains("new beyond cap"), "error-record-bound: capped original error stream kept growing");
        causes.Append("[Quest startup] scene after error cap");
        Check(File.ReadAllText(path).Contains("scene after error cap"), "error-reserve: original error saturation swallowed lifecycle evidence");
        // HashSet is only generic ICollection; inspect Count without binding to implementation layout.
        object seenSet = typeof(QuestGameStartupLog).GetField("errorsSeen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(causes)!;
        Check((int)seenSet.GetType().GetProperty("Count")!.GetValue(seenSet)! <= QuestGameStartupLog.MaxOriginalErrors, "memory-bound: distinct original error identities grew past the record cap");

        path = Path.Combine(root, "unicode.log"); var unicode = new QuestGameStartupLog(path, "Unicode byte bound");
        for (int i = 0; i < 100; i++)
        {
            unicode.Append(i + new string('漢', 20000), new string('界', 20000));
            unicode.AppendOriginalError(i + new string('漢', 20000), new string('界', 20000));
        }
        text = File.ReadAllText(path);
        Check(new FileInfo(path).Length <= QuestGameStartupLog.MaxBytes && text.Contains("[truncated]") && text.Contains("diagnostic log limit reached") && text.Contains("original error log limit reached"), "utf8-bound: multi-byte text or two capped streams exceeded total byte budget");
        Check(unicode.OriginalErrors < QuestGameStartupLog.MaxOriginalErrors, "utf8-bound: long stack record limit did not stop at the reserved byte budget");
        new QuestGameStartupLog(path, "next run");
        Check(File.ReadAllText(Path.ChangeExtension(path, ".previous.log")).Contains("Unicode byte bound") && File.ReadAllText(path).Contains("next run"), "rotation: bounded previous run lost first-cause evidence");

        path = Path.Combine(root, "concurrent.log"); var concurrent = new QuestGameStartupLog(path, "concurrent original loggers");
        using var start = new ManualResetEventSlim();
        Task[] tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            start.Wait();
            for (int n = 0; n < 500; n++)
            {
                concurrent.AppendOriginalError("shared root", "shared stack");
                concurrent.AppendOriginalError("worker root " + i, "worker stack " + i);
                concurrent.Append("shared lifecycle transition");
            }
        })).ToArray();
        start.Set(); Task.WhenAll(tasks).GetAwaiter().GetResult();
        text = File.ReadAllText(path);
        Check(concurrent.OriginalErrors == 17 && Count(text, "shared root") == 1 && Count(text, "shared lifecycle transition") == 1, "thread-concurrency: simultaneous callers duplicated or lost first causes");
        for (int i = 0; i < 16; i++) Check(Count(text, "worker root " + i + "\n") == 1 && text.Contains("worker stack " + i + "\n"), "thread-concurrency: worker first stack missing or corrupted");
        Check(new FileInfo(path).Length <= QuestGameStartupLog.MaxBytes, "thread-concurrency: parallel append exceeded the bound");

        path = Path.Combine(root, "retry.log"); var retry = new QuestGameStartupLog(path, "IO retry");
        File.Delete(path); Directory.CreateDirectory(path);
        try { retry.AppendOriginalError("first load failure", "retry stack"); throw new Exception("IO fixture did not fail"); }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        Check(retry.OriginalErrors == 0, "io-retry: failed persistence published a retained cause");
        Directory.Delete(path); File.WriteAllText(path, ""); retry.AppendOriginalError("first load failure", "retry stack");
        Check(retry.OriginalErrors == 1 && File.ReadAllText(path).Contains("retry stack"), "io-retry: failed persistence consumed the deduplication identity");

        path = Path.Combine(root, "presentation-reserve.log");
        var details = new QuestGameStartupLog(path, "B619 saturation replay");
        for (int i = 0; i < 2000; i++)
        {
            details.Append("[Quest startup] original movie state unique=" + i);
            details.Append("[Quest startup] original movie pixels unique=" + i);
            details.Append("[Quest startup] original movie consumer unique=" + i);
            details.Append("[Quest startup] presentation camera unique=" + i);
            details.Append("[Quest startup] audio source unique=" + i);
            details.Append("[Quest startup] frame summary unique=" + i);
        }
        details.Append("[Quest startup] original scene loaded=MainMenu state=running");
        details.Append("[Quest startup] original movie URL bound MainMenu source=ambient");
        details.AppendOriginalError("later original failure", "full later stack");
        text = File.ReadAllText(path);
        Check(text.Contains("original scene loaded=MainMenu") && text.Contains("original movie URL bound MainMenu"), "detail-reserve: detailed observations swallowed final menu lifecycle and video binding");
        Check(Count(text, "presentation diagnostic log limit reached") == 1 && text.Contains("full later stack"), "detail-reserve: bounded detail stream lost first errors or repeated its cap marker");
        Check(new FileInfo(path).Length <= QuestGameStartupLog.MaxBytes, "detail-reserve: independent lanes exceeded the global byte ceiling");

        var frames = new QuestFrameEvidence();
        Check(frames.Observe("MainMenu", true, false, 1, 0, 0, true) == null && !frames.Snapshot.sampling, "frame-debug: normal logging ran frame sampling");
        frames.Observe("MainMenu", true, true, 9, 1, 0, true);
        frames.Observe("MainMenu", true, true, .016f, 2, 0, true);
        frames.Observe("MainMenu", true, true, .120f, 3, 1, true);
        frames.Observe("MainMenu", true, true, .090f, 6, 1, true);
        frames.Observe("MainMenu", true, true, .140f, 9, 2, true);
        Check(frames.Snapshot.frames == 4 && frames.Snapshot.over40Ms == 3 && frames.Snapshot.over100Ms == 2, "frame-resume: scene/resume delta counted as a real frame");
        Check(frames.Snapshot.gcCollections == 2 && frames.Snapshot.spikeGcDelta.Take(3).SequenceEqual(new[] { 1, 0, 1 }), "frame-gc: frame/GC association lost the independent non-GC hitch");
        Check(frames.Snapshot.spikeTimes.Take(3).SequenceEqual(new[] { 3f, 6f, 9f }), "frame-cadence: retained timestamps cannot show a three-second recurrence");
        frames.Observe("MainMenu", false, true, 40, 10, 2, true);
        frames.Observe("MainMenu", true, true, 40, 50, 2, true);
        Check(frames.Snapshot.frames == 4, "frame-resume: time without focused gameplay contaminated hitch counts");
        frames.Observe("MainMenu", true, true, float.NaN, 51, 2, true);
        frames.Observe("MainMenu", true, true, -1, 51, 2, true);
        Check(frames.Snapshot.frames == 4, "frame-invalid: invalid delta poisoned aggregates");
        frames.RecordSnapshotWrite(12); frames.RecordSnapshotWrite(3); frames.RecordSnapshotWrite(double.NaN);
        Check(frames.Snapshot.maxSnapshotWriteMs == 12, "frame-io: state writer cost does not retain its maximum");
        int reports = 0;
        for (int i = 0; i < 2000; i++)
            if (frames.Observe("MainMenu", true, true, .1f, 52 + i, 2, true) != null) reports++;
        Check(reports <= 12 && frames.Snapshot.spikeCount == 24 && frames.Snapshot.over40Ms == 2003, "frame-bound: reports or retained spike arrays grew with test duration");
        Check(frames.Snapshot.spikeTimes.Max() >= 2050 && frames.Snapshot.spikeCursor < 24,
            "frame-window: retained samples lost sustained late-session hitches");
        frames.RecordDiscovery(25, 340, .7f, 2.1f);
        Check(frames.Snapshot.discoveryScans == 25 && frames.Snapshot.discoverySceneComponents == 340
            && frames.Snapshot.discoveryLastMs == .7f && frames.Snapshot.discoveryWorstMs == 2.1f,
            "frame-discovery: actual scope timings were not carried into existing state");
        var noAlloc = new QuestFrameEvidence();
        noAlloc.Observe("MainMenu", true, true, .016f, 0, 0, true);
        for (int i = 0; i < 100; i++) noAlloc.Observe("MainMenu", true, true, .016f, 1, 0, true);
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) noAlloc.Observe("MainMenu", true, true, .016f, 1, 0, true);
        Check(GC.GetAllocatedBytesForCurrentThread() == allocationStart, "frame-allocation: ordinary per-frame measurement creates managed garbage");
        Console.WriteLine("PASS Quest startup logging: " + assertions + " assertions");
    }
}
