using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GloomhavenVR.Quest;
using UnityEngine;

internal static class Program
{
    static int assertions;
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    static string evidence = "";
    static void Check(bool condition, string token, string detail)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(token + ": " + detail);
    }
    static object? Call(QuestGameBootstrap owner, string name, params object[] args)
    {
        try { return typeof(QuestGameBootstrap).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static byte[] Zip(string name, byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        using (Stream entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open()) entry.Write(bytes);
        return output.ToArray();
    }
    sealed class Inputs
    {
        internal string Directory = "";
        internal QuestGameContentManifest Mod = null!, Original = null!;
        internal byte[] ModPayload = null!, OriginalPayload = null!, ModZip = null!, OriginalZip = null!;
    }
    static Inputs Setup(string name, bool editor = false, bool warm = false, string defect = "")
    {
        Fixture.Reset();
        string root = Path.Combine(evidence, name); System.IO.Directory.CreateDirectory(root);
        string persistent = Path.Combine(root, "persistent"), streaming = Path.Combine(root, "streaming");
        System.IO.Directory.CreateDirectory(persistent); System.IO.Directory.CreateDirectory(streaming);
        Fixture.Root = Application.Persistent = persistent;
        Application.Streaming = streaming; Application.Data = Path.Combine(root, "installed.apk");
        Application.Platform = RuntimePlatform.Android; Application.Editor = editor;
        Time.Clock = 0; Time.Frame = 0; Resources.Assets.Clear();
        var inputs = new Inputs { Directory = root, ModPayload = new byte[1024 * 1024 + 27], OriginalPayload = new byte[512 * 1024 + 13] };
        new Random(614).NextBytes(inputs.ModPayload); new Random(615).NextBytes(inputs.OriginalPayload);
        const string key = "quest-startup-loading-fixture";
        inputs.ModZip = Zip("StreamingAssets/gloomhavenvr.bundle", inputs.ModPayload);
        inputs.OriginalZip = Zip("StreamingAssets/original.rules", inputs.OriginalPayload);
        inputs.Mod = Manifest("quest-mod-content.zip", "StreamingAssets/gloomhavenvr.bundle", inputs.ModPayload, inputs.ModZip, key);
        inputs.Original = Manifest("quest-startup-content.zip", "StreamingAssets/original.rules", inputs.OriginalPayload, inputs.OriginalZip, key);
        if (defect == "payload")
        {
            byte[] bad = (byte[])inputs.ModPayload.Clone(); bad[42] ^= 1;
            inputs.ModZip = Zip("StreamingAssets/gloomhavenvr.bundle", bad);
            inputs.Mod.archiveSha256 = Hash(inputs.ModZip); // Valid archive, invalid manifested payload.
        }
        if (defect == "mod-archive") inputs.ModZip[42] ^= 1;
        if (defect == "original-archive") inputs.OriginalZip[42] ^= 1;
        File.WriteAllBytes(Path.Combine(streaming, inputs.Mod.archive), inputs.ModZip);
        File.WriteAllBytes(Path.Combine(streaming, inputs.Original.archive), inputs.OriginalZip);
        using (var archive = new ZipArchive(File.Create(Application.Data), ZipArchiveMode.Create))
        {
            if (defect != "missing-entry") WriteEntry(archive, "assets/" + inputs.Mod.archive, inputs.ModZip);
            WriteEntry(archive, "assets/" + inputs.Original.archive, inputs.OriginalZip);
            // Irrelevant APK contents must not become content files.
            WriteEntry(archive, "assets/bin/Data/private-not-a-content-file", new byte[] { 9 });
        }
        if (warm)
        {
            WriteWarm(persistent, "quest-mod-resources", inputs.Mod.files[0].path, inputs.ModPayload);
            WriteWarm(persistent, "quest-owned-game", inputs.Original.files[0].path, inputs.OriginalPayload);
            // A ready installation must not read its delivery source again.
            File.Delete(Application.Data);
            File.Delete(Path.Combine(streaming, inputs.Mod.archive)); File.Delete(Path.Combine(streaming, inputs.Original.archive));
        }
        Resources.Assets["quest-build"] = new TextAsset(JsonSerializer.Serialize(new { schema = 1, modBuild = 614, inputKey = key }));
        Resources.Assets["quest-mod-content"] = new TextAsset(JsonSerializer.Serialize(inputs.Mod, Json));
        Resources.Assets["quest-startup-content"] = new TextAsset(JsonSerializer.Serialize(inputs.Original, Json));
        Resources.Assets["quest-startup-addressables"] = new TextAsset("{\"schema\":1}");
        Resources.Assets["quest-startup-movies"] = new TextAsset("{\"schema\":1}");
        Fixture.OnLog = message =>
        {
            if (message.Contains("archive copy started phase=content ", StringComparison.Ordinal)) Fixture.Event("original-copy");
            if (message.Contains("archive copy started phase=mod-content ", StringComparison.Ordinal)) Fixture.Event("mod-copy");
        };
        Fixture.OnDurableRecord = message =>
        {
            if (message.Contains("content worker phase=", StringComparison.Ordinal) && Environment.CurrentManagedThreadId == Fixture.MainThread)
                throw new InvalidOperationException("main-hash: content progress ran synchronously on the Unity main thread");
        };
        return inputs;
    }
    static QuestGameContentManifest Manifest(string archive, string path, byte[] bytes, byte[] zip, string key) => new()
    {
        schema = 1, inputKey = key, archive = archive, archiveSha256 = Hash(zip),
        files = new[] { new QuestGameContentFile { path = path, size = bytes.Length, sha256 = Hash(bytes) } }
    };
    static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        using Stream entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open(); entry.Write(bytes);
    }
    static void WriteWarm(string root, string scope, string relative, byte[] bytes)
    {
        string target = Path.Combine(root, scope, relative); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes);
    }
    sealed class Pump
    {
        readonly Stack<IEnumerator> routines = new();
        readonly QuestGameBootstrap owner;
        internal bool Complete => routines.Count == 0;
        internal Pump(QuestGameBootstrap component) { owner = component; routines.Push((IEnumerator)Call(owner, "Start")!); }
        internal void Tick()
        {
            Time.Frame++; Time.Clock += .25f;
            Call(owner, "Update");
            // Unity schedules nested IEnumerators until a null/async yield.
            for (int steps = 0; steps < 100 && routines.Count != 0; steps++)
            {
                IEnumerator current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); (current as IDisposable)?.Dispose(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
        }
        internal void Finish()
        {
            var clock = Stopwatch.StartNew();
            while (!Complete && clock.Elapsed.TotalSeconds < 15) { Tick(); Thread.Sleep(1); }
            Check(Complete, "yield-main-pump", "startup coroutine did not complete within fixture deadline");
        }
    }
    static QuestGameBootstrap Owner()
    {
        var owner = new QuestGameBootstrap(); Call(owner, "Awake");
        Check(Fixture.ViewPrepared && Fixture.ViewVisible, "early-view", "loading view must be prepared in Awake before worker content starts");
        return owner;
    }
    static JsonElement State()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.Root, "quest-startup-state.json")));
        return document.RootElement.Clone();
    }
    static void Successful(QuestGameBootstrap owner, Inputs inputs, bool warm)
    {
        Check(owner.State != "failed", "startup-completes", "startup failed: " + owner.FailureDetail);
        Check(Fixture.WorkerApiCalls == 0, "worker-api", "content worker accessed Unity API");
        Check(owner.ModContentReady && owner.ContentReady && owner.OriginalBootstrapStarted, "verified-order", "ready flags must follow actual successful files and original scene");
        Check(Fixture.Activations == 1 && Fixture.Addressables == 1 && Fixture.Loads == 1, "one-owner", "one downstream activation is required");
        string[] events = Fixture.Events.ToArray();
        Check(Array.IndexOf(events, "original-paths") >= 0 && Array.IndexOf(events, "original-paths") < Array.IndexOf(events, "real-mod"),
            "cached-original-paths", "original managed paths must be initialized before the plugin or native loader starts");
        Check(Array.IndexOf(events, "early-view") < Array.IndexOf(events, "real-mod"), "early-view", "temporary view precedes mod");
        Check(Array.IndexOf(events, "real-mod") < Array.IndexOf(events, "addressables") && Array.IndexOf(events, "addressables") < Array.IndexOf(events, "original-scene"), "verified-order", "mod, original content, Addressables and native scene order changed");
        Check(warm || Array.IndexOf(events, "real-mod") < Array.IndexOf(events, "original-copy"), "mod-before-original", "cold original archive must wait until mod activation");
        Check(!warm || !events.Contains("mod-copy") && !events.Contains("original-copy"), "warm-no-delivery", "warm content must avoid archive transfer");
        Check(File.ReadAllBytes(Path.Combine(Fixture.Root, "quest-mod-resources", inputs.Mod.files[0].path)).SequenceEqual(inputs.ModPayload), "verified-bytes", "installed mod bytes differ");
        Check(File.ReadAllBytes(Path.Combine(Fixture.Root, "quest-owned-game", inputs.Original.files[0].path)).SequenceEqual(inputs.OriginalPayload), "verified-bytes", "installed original bytes differ");
        Check(!System.IO.Directory.EnumerateFiles(Fixture.Root, "*.download", SearchOption.AllDirectories).Any(), "archive-cleanup", "completed archives must be removed");
        Check(!System.IO.Directory.EnumerateFiles(Fixture.Root, "*.tmp", SearchOption.AllDirectories).Any(), "temp-cleanup", "no partial files after success");
        Check(!Fixture.ViewVisible, "view-retired", "temporary loading presentation retired with mod activation");
        JsonElement state = State();
        Check(state.GetProperty("modBuild").GetInt32() == 614 && !state.GetProperty("fullGameReady").GetBoolean(), "state-boundary", "a started original scene does not prove full-game readiness");
        Call(owner, "OnDestroy");
    }
    static void HeldWorker()
    {
        Inputs inputs = Setup("cold-android-held");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int held = 0;
        Fixture.OnDurableRecord = message =>
        {
            if (message.Contains("content worker phase=", StringComparison.Ordinal) && Environment.CurrentManagedThreadId == Fixture.MainThread)
                throw new InvalidOperationException("main-hash: content progress ran synchronously on the Unity main thread");
            if (Environment.CurrentManagedThreadId != Fixture.MainThread && message.Contains("content worker phase=", StringComparison.Ordinal) && Interlocked.CompareExchange(ref held, 1, 0) == 0)
            {
                Fixture.Event("held-worker"); entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("yield-main-pump: fixture worker was not released");
            }
        };
        var owner = Owner(); var pump = new Pump(owner);
        try
        {
            var clock = Stopwatch.StartNew();
            while (!entered.IsSet && clock.Elapsed.TotalSeconds < 5) { pump.Tick(); Thread.Sleep(1); }
            Check(owner.State != "failed", "startup-completes", "worker failed before hold seam: " + owner.FailureDetail);
            Check(entered.IsSet, "held-progress", "production worker progress did not reach durable sink seam");
            int before = Fixture.MainUpdates;
            for (int i = 0; i < 40; i++) pump.Tick();
            Check(!pump.Complete && Fixture.Activations == 0, "wait-worker", "mod activated while its content worker was still held");
            Check(Fixture.MainUpdates >= before + 40, "yield-main-pump", "loading-view updates stopped while content worker was held");
            JsonElement state = State();
            Check(state.GetProperty("mainThreadFrames").GetInt32() >= 40, "main-frame-state", "snapshot does not report live main-thread frames");
            Check(state.GetProperty("loadingViewAvailable").GetBoolean(), "early-view-state", "loading view missing during held content worker");
            Check(!string.IsNullOrEmpty(state.GetProperty("contentPhase").GetString()), "phase-state", "held worker phase not published");
            Check(state.GetProperty("contentTotalBytes").GetInt64() >= state.GetProperty("contentProcessedBytes").GetInt64(), "byte-state", "invalid byte snapshot");
            Check(DateTime.TryParse(state.GetProperty("utc").GetString(), out _), "utc-state", "snapshot requires timestamp");
        }
        finally { release.Set(); }
        pump.Finish(); Fixture.OnDurableRecord = null; Successful(owner, inputs, false);
    }
    static void Success(string name, bool editor = false, bool warm = false)
    {
        Inputs inputs = Setup(name, editor, warm); var owner = Owner(); new Pump(owner).Finish(); Successful(owner, inputs, warm);
    }
    static void LogWriteFailure()
    {
        Inputs inputs = Setup("progress-log-io-failure");
        Fixture.OnDurableRecord = message =>
        {
            if (message.Contains("content worker phase=", StringComparison.Ordinal)) throw new IOException("fixture progress log destination failed");
        };
        var owner = Owner(); new Pump(owner).Finish(); Successful(owner, inputs, false);
    }
    static void Failure(string defect, int expectedActivations = 0)
    {
        Inputs inputs = Setup("reject-" + defect, defect: defect);
        Fixture.ModFailure = defect == "mod-failure"; Fixture.AddressablesFailure = defect == "addressables-failure";
        Fixture.SceneAvailable = defect != "scene-unavailable";
        if (defect == "missing-manifest") Resources.Assets.Remove("quest-mod-content");
        var owner = Owner(); new Pump(owner).Finish();
        Check(owner.State == "failed" && !string.IsNullOrEmpty(owner.FailureDetail), "corrupt-delivery-gate", "bad startup input must leave explicit failure");
        Check(Fixture.Activations == expectedActivations, "corrupt-never-activates", "bad mod archive or manifest reached mod activation");
        Check(!owner.OriginalBootstrapStarted && Fixture.Loads == 0, "corrupt-never-loads", "native scene started after rejected input");
        Check(Fixture.WorkerApiCalls == 0, "worker-api", "worker API misuse in failure path");
        if (expectedActivations == 0) Check(!owner.ModContentReady && Fixture.Addressables == 0, "corrupt-ready-state", "invalid mod content was published ready");
        if (defect == "original-archive" || defect == "mod-failure") Check(Fixture.Addressables == 0, "downstream-error-gate", "Addressables ran after prior failure");
        JsonElement state = State();
        Check(state.GetProperty("state").GetString() == "failed" && !state.GetProperty("fullGameReady").GetBoolean(), "failure-state", "retained snapshot must remain failed");
        Check(Fixture.ViewVisible || expectedActivations > 0, "failure-view", "failure before plugin must retain loading view");
        Call(owner, "OnDestroy");
    }
    static int Main(string[] args)
    {
        try
        {
            evidence = Path.GetFullPath(args[0]); System.IO.Directory.CreateDirectory(evidence);
            HeldWorker(); Success("cold-editor", editor: true); Success("warm-android", warm: true); Success("warm-editor", editor: true, warm: true); LogWriteFailure();
            foreach (string defect in new[] { "mod-archive", "payload", "missing-entry", "missing-manifest" }) Failure(defect);
            foreach (string defect in new[] { "original-archive", "mod-failure", "addressables-failure", "scene-unavailable" }) Failure(defect, 1);
            Console.WriteLine("PASS Quest startup loading: " + assertions + " assertions; actual Bootstrap/content/delivery, Unity/logger/downstream seams");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
