using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using GloomhavenVR.Quest;

internal static class Program
{
    const string ModArchive = "quest-mod-content.zip", StartupArchive = "quest-startup-content.zip", Bundle = "StreamingAssets/gloomhavenvr.bundle";
    static int checks;
    static string root = "";
    static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    static void Check(bool value, string detail) { checks++; if (!value) throw new InvalidOperationException(detail); }
    static void Reject(Action action, string detail)
    {
        try { action(); } catch (InvalidDataException) { checks++; return; }
        throw new InvalidOperationException("Accepted " + detail);
    }
    static QuestGameContentFile FileEntry(string path, byte[] data) => new() { path = path, size = data.Length, sha256 = Hash(data) };
    static (QuestGameContentManifest manifest, string archive, string output) Fixture(string id, string scope, QuestGameContentFile[] files, params (string name, byte[] data)[] entries)
    {
        string directory = Path.Combine(root, id); Directory.CreateDirectory(directory);
        string archive = Path.Combine(directory, scope), output = Path.Combine(directory, "output");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            foreach (var entry in entries)
                using (var stream = zip.CreateEntry(entry.name).Open()) stream.Write(entry.data);
        var manifest = new QuestGameContentManifest { schema = 1, inputKey = "owned-test-input", archive = scope, archiveSha256 = QuestGameContent.Hash(archive), files = files };
        return (manifest, archive, output);
    }
    static void Main(string[] args)
    {
        root = Path.Combine(Path.GetTempPath(), "ghvr-quest-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { Run(); Delivery(); if (args.Length != 0) ActualApk(args); }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS Quest mod/startup content: " + checks + " assertions (production delivery/verifier/extractor).");
    }
    static void Run()
    {
        byte[] payload = Bytes("owned test bundle bytes; no original game content");
        var file = FileEntry(Bundle, payload);
        var mod = Fixture("mod", ModArchive, new[] { file }, (Bundle, payload));
        QuestGameContent.Validate(mod.manifest, "owned-test-input", ModArchive);
        Reject(() => QuestGameContent.Validate(mod.manifest, "owned-test-input"), "mod archive through default original-content API");
        Reject(() => QuestGameContent.Extract(mod.manifest, mod.archive, mod.output), "mod extraction through default original-content API");
        Check(!Directory.Exists(mod.output), "Wrong archive scope created output before rejecting.");
        Reject(() => QuestGameContent.Validate(mod.manifest, "other-owned-input", ModArchive), "another build's mod content identity");
        Reject(() => QuestGameContent.Validate(mod.manifest, "owned-test-input", "arbitrary.zip"), "untrusted explicit archive scope");
        QuestGameContent.Extract(mod.manifest, mod.archive, mod.output, ModArchive);
        Check(QuestGameContent.IsReady(mod.manifest, mod.output), "Explicit mod archive did not verify after extraction.");
        string extracted = QuestGameContent.ResolveVerifiedPath(mod.manifest, mod.output, Bundle);
        Check(System.IO.File.ReadAllBytes(extracted).SequenceEqual(payload), "Mod bundle bytes changed during extraction.");
        Reject(() => QuestGameContent.ResolveVerifiedPath(mod.manifest, mod.output, "unmanifested.bundle"), "unmanifested bundle resolution");
        DateTime untouched = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        System.IO.File.SetLastWriteTimeUtc(extracted, untouched);
        QuestGameContent.Extract(mod.manifest, mod.archive, mod.output, ModArchive);
        Check(System.IO.File.GetLastWriteTimeUtc(extracted) == untouched, "Already verified mod content was unnecessarily rewritten.");
        System.IO.File.WriteAllBytes(extracted, Bytes("corrupt existing bundle"));
        Check(!QuestGameContent.IsReady(mod.manifest, mod.output), "Corrupt existing mod bundle was considered ready.");
        Reject(() => QuestGameContent.ResolveVerifiedPath(mod.manifest, mod.output, Bundle), "corrupt verified bundle resolution");
        QuestGameContent.Extract(mod.manifest, mod.archive, mod.output, ModArchive);
        Check(System.IO.File.ReadAllBytes(extracted).SequenceEqual(payload), "Corrupt prior mod bundle was not repaired from its verified archive.");

        var startup = Fixture("startup", StartupArchive, new[] { FileEntry("Rulebase/fixture.yml", payload) }, ("Rulebase/fixture.yml", payload));
        QuestGameContent.Validate(startup.manifest, "owned-test-input");
        QuestGameContent.Extract(startup.manifest, startup.archive, startup.output);
        Check(QuestGameContent.IsReady(startup.manifest, startup.output), "Original startup default archive API changed.");
        Reject(() => QuestGameContent.Validate(startup.manifest, "owned-test-input", ModArchive), "original archive under mod scope");

        var corruptArchive = Fixture("archive-hash", ModArchive, new[] { file }, (Bundle, payload));
        System.IO.File.AppendAllText(corruptArchive.archive, "changed");
        Reject(() => QuestGameContent.Extract(corruptArchive.manifest, corruptArchive.archive, corruptArchive.output, ModArchive), "modified archive hash");
        Check(!Directory.Exists(corruptArchive.output), "Unverified archive created extraction output.");
        var extra = Fixture("extra", ModArchive, new[] { file }, (Bundle, payload), ("unknown.txt", payload));
        Reject(() => QuestGameContent.Extract(extra.manifest, extra.archive, extra.output, ModArchive), "unmanifested ZIP entry");
        Check(!System.IO.File.Exists(Path.Combine(extra.output, Bundle)), "Unexpected ZIP entry was detected only after writing the mod bundle.");
        var duplicate = Fixture("duplicate", ModArchive, new[] { file }, (Bundle, payload), (Bundle, payload));
        Reject(() => QuestGameContent.Extract(duplicate.manifest, duplicate.archive, duplicate.output, ModArchive), "duplicate ZIP entry");
        var missing = Fixture("missing", ModArchive, new[] { file });
        Reject(() => QuestGameContent.Extract(missing.manifest, missing.archive, missing.output, ModArchive), "missing manifested bundle");
        var wrongSize = Fixture("wrong-size", ModArchive, new[] { file }, (Bundle, Bytes("short")));
        Reject(() => QuestGameContent.Extract(wrongSize.manifest, wrongSize.archive, wrongSize.output, ModArchive), "wrong ZIP entry size");
        var wrongHash = Fixture("file-hash", ModArchive, new[] { FileEntry(Bundle, payload.Select(b => (byte)(b ^ 1)).ToArray()) }, (Bundle, payload));
        string previous = Path.Combine(wrongHash.output, Bundle); Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        System.IO.File.WriteAllText(previous, "existing safe bytes");
        Reject(() => QuestGameContent.Extract(wrongHash.manifest, wrongHash.archive, wrongHash.output, ModArchive), "manifested file hash mismatch");
        Check(System.IO.File.ReadAllText(previous) == "existing safe bytes", "Failed file verification overwrote the prior bundle.");
        Check(Directory.GetFiles(wrongHash.output, "*.tmp", SearchOption.AllDirectories).Length == 0, "Failed verification left a partial temporary bundle.");
        var traversal = Fixture("traversal", ModArchive, new[] { FileEntry("../outside.bundle", payload) }, ("../outside.bundle", payload));
        Reject(() => QuestGameContent.Validate(traversal.manifest, "owned-test-input", ModArchive), "path traversal manifest");
        Reject(() => QuestGameContent.Extract(traversal.manifest, traversal.archive, traversal.output, ModArchive), "path traversal extraction");
        Check(!System.IO.File.Exists(Path.Combine(root, "traversal/outside.bundle")), "Manifest path escaped its private root.");
        var aliases = Fixture("aliases", ModArchive, new[] { file, FileEntry(Bundle.ToUpperInvariant(), payload) }, (Bundle, payload));
        Reject(() => QuestGameContent.Validate(aliases.manifest, "owned-test-input", ModArchive), "case-aliased content destinations");
        if (!OperatingSystem.IsWindows())
        {
            var link = Fixture("symlink", ModArchive, new[] { file }, (Bundle, payload));
            string outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); Directory.CreateDirectory(link.output);
            Directory.CreateSymbolicLink(Path.Combine(link.output, "StreamingAssets"), outside);
            Reject(() => QuestGameContent.Extract(link.manifest, link.archive, link.output, ModArchive), "symbolic-link parent escape");
            Check(!System.IO.File.Exists(Path.Combine(outside, "gloomhavenvr.bundle")), "Mod content escaped through a symbolic-link parent.");
        }
    }

    static string Apk(string directory, string id, CompressionLevel compression, params (string name, byte[] data)[] entries)
    {
        string path = Path.Combine(directory, id + ".apk");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var entry in entries)
                using (var stream = zip.CreateEntry(entry.name, compression).Open()) stream.Write(entry.data);
        return path;
    }

    static QuestGameContentManifest WithHash(QuestGameContentManifest original, string hash) => new()
    { schema = original.schema, inputKey = original.inputKey, archive = original.archive, archiveSha256 = hash, files = original.files };

    static void Progress(List<QuestGameContentProgress> reports, string phase, string file, long expectedBytes)
    {
        var rows = reports.Where(item => item.Phase == phase && item.File == file).ToArray();
        Check(rows.Length >= 2, "No start/completion progress for " + phase);
        Check(rows[0].ProcessedBytes == 0 && rows[^1].ProcessedBytes == expectedBytes, "Incomplete progress for " + phase);
        Check(rows.All(row => row.TotalBytes == expectedBytes && row.ProcessedBytes >= 0 && row.ProcessedBytes <= expectedBytes), "Progress exceeded byte bounds for " + phase);
        // A later operation may legitimately re-verify the same file, starting a new series.
        for (int i = 1; i < rows.Length; i++)
            Check(rows[i].ProcessedBytes == 0 || rows[i].ProcessedBytes >= rows[i - 1].ProcessedBytes, "Progress regressed within " + phase);
        Check(rows.Length <= 2 + 2 * (expectedBytes / 1048576 + 2), "Progress reports scale with chunks rather than MiB/completion for " + phase);
    }

    static void Delivery()
    {
        byte[] payload = new byte[3 * 1048576 + 31]; new Random(614).NextBytes(payload);
        foreach (var scope in new[] { ModArchive, StartupArchive })
        foreach (var compression in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
        {
            string name = scope == ModArchive ? Bundle : "Rulebase/fixture.yml";
            var fixture = Fixture("delivery-" + scope + "-" + compression, scope, new[] { FileEntry(name, payload) }, (name, payload));
            byte[] archive = System.IO.File.ReadAllBytes(fixture.archive);
            string apk = Apk(Path.GetDirectoryName(fixture.archive)!, "player", compression,
                ("assets/other-owned-content", Bytes("unrelated asset")), ("assets/" + scope, archive));
            string staged = Path.Combine(Path.GetDirectoryName(fixture.archive)!, "private", scope + ".download");
            var reports = new List<QuestGameContentProgress>();
            QuestGameArchiveDelivery.Stage(fixture.manifest, apk, staged, true, reports.Add);
            Check(System.IO.File.ReadAllBytes(staged).SequenceEqual(archive), "APK archive delivery changed bytes for " + compression);
            Check(QuestGameContent.Hash(staged) == fixture.manifest.archiveSha256, "Staged APK archive differs from manifest.");
            Progress(reports, "copying-archive", scope, archive.Length);
            QuestGameContent.Extract(fixture.manifest, staged, fixture.output, scope, reports.Add);
            Check(QuestGameContent.IsReady(fixture.manifest, fixture.output), "APK -> staged archive -> extracted files did not verify.");
            Progress(reports, "verifying-archive", scope, archive.Length);
            Progress(reports, "extracting-file", name, payload.Length);
            Progress(reports, "verifying-file", name, payload.Length);
            Progress(reports, "checking-files", name, payload.Length);
            Check(System.IO.File.ReadAllBytes(Path.Combine(fixture.output, name)).SequenceEqual(payload), "Extracted content differs from owned bytes.");
            reports.Clear();
            QuestGameContent.IsReady(fixture.manifest, fixture.output, reports.Add);
            Progress(reports, "checking-files", name, payload.Length);

            DateTime originalTime = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            System.IO.File.SetLastWriteTimeUtc(fixture.archive, originalTime);
            byte[] prior = Bytes("previous safe staged archive"); System.IO.File.WriteAllBytes(staged, prior);
            QuestGameArchiveDelivery.Stage(fixture.manifest, fixture.archive, staged, false);
            Check(System.IO.File.ReadAllBytes(staged).SequenceEqual(archive), "Direct Editor archive delivery failed to replace its staged target.");
            Check(System.IO.File.GetLastWriteTimeUtc(fixture.archive) == originalTime, "Delivery modified its source archive.");
            Check(Directory.GetFiles(Path.GetDirectoryName(staged)!, "*.tmp").Length == 0, "Successful archive staging left partial files.");

            // This uses the production synchronous core in a real worker. Stop the worker
            // inside a bounded callback to prove the caller retains control while delivery
            // is pending, without fragile timing or scheduling/deadline assertions.
            int caller = Environment.CurrentManagedThreadId, worker = caller;
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            Task task = Task.Run(() => QuestGameArchiveDelivery.Stage(fixture.manifest, apk, staged, true, update =>
            {
                worker = Environment.CurrentManagedThreadId;
                if (update.ProcessedBytes == 0) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Test worker was not released."); }
            }));
            bool arrived = entered.Wait(TimeSpan.FromSeconds(10));
            Check(arrived && !task.IsCompleted && worker != caller, "Archive delivery did not run independently of the caller.");
            int frames = 0; for (int i = 0; i != 100; i++) frames++;
            Check(frames == 100 && !task.IsCompleted, "Caller could not advance while archive delivery was pending.");
            release.Set(); task.GetAwaiter().GetResult();
            Check(QuestGameContent.Hash(staged) == fixture.manifest.archiveSha256, "Worker delivery produced incorrect bytes.");
        }

        var test = Fixture("delivery-errors", ModArchive, new[] { FileEntry(Bundle, Bytes("owned bytes")) }, (Bundle, Bytes("owned bytes")));
        string directory = Path.GetDirectoryName(test.archive)!, destination = Path.Combine(directory, "private", ModArchive + ".download");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        byte[] safe = Bytes("previous verified archive remains intact"); System.IO.File.WriteAllBytes(destination, safe);
        byte[] nested = System.IO.File.ReadAllBytes(test.archive);
        void Preserved(string detail)
        {
            Check(System.IO.File.ReadAllBytes(destination).SequenceEqual(safe), detail + " overwrote the previous archive.");
            Check(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp").Length == 0, detail + " left a partial temporary archive.");
        }
        string missing = Apk(directory, "missing", CompressionLevel.NoCompression, ("assets/not-the-archive", nested));
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, missing, destination, true), "APK with missing content archive"); Preserved("Missing archive");
        string duplicate = Apk(directory, "duplicate", CompressionLevel.NoCompression, ("assets/" + ModArchive, nested), ("assets/" + ModArchive, nested));
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, duplicate, destination, true), "APK with duplicate content archive"); Preserved("Duplicate archive");
        string wrongName = Apk(directory, "wrong-case", CompressionLevel.NoCompression, ("assets/" + ModArchive.ToUpperInvariant(), nested));
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, wrongName, destination, true), "APK with case-aliased archive name"); Preserved("Wrong archive name");
        string good = Apk(directory, "good", CompressionLevel.Optimal, ("assets/" + ModArchive, nested));
        Reject(() => QuestGameArchiveDelivery.Stage(WithHash(test.manifest, new string('0', 64)), good, destination, true), "APK with wrong manifested archive hash"); Preserved("Wrong archive SHA");
        string corrupt = Path.Combine(directory, "corrupt.apk"); System.IO.File.WriteAllBytes(corrupt, Bytes("not an APK ZIP"));
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, corrupt, destination, true), "corrupt APK ZIP"); Preserved("Corrupt APK");
        string badBytes = Apk(directory, "modified-entry", CompressionLevel.Optimal, ("assets/" + ModArchive, nested.Select(b => (byte)(b ^ 1)).ToArray()));
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, badBytes, destination, true), "corrupt delivered archive bytes"); Preserved("Corrupt inner archive");
        string oversized = Apk(directory, "oversized", CompressionLevel.Optimal, ("assets/" + ModArchive, new byte[2 * 1048576]));
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, oversized, destination, true), "oversized inflated APK archive"); Preserved("Oversized APK archive");
        string directLarge = Path.Combine(directory, "oversized.zip"); System.IO.File.WriteAllBytes(directLarge, new byte[2 * 1048576]);
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, directLarge, destination, false), "oversized Editor archive"); Preserved("Oversized Editor archive");
        var overflow = WithHash(test.manifest, test.manifest.archiveSha256); overflow.files = new[] { new QuestGameContentFile { path = Bundle, sha256 = test.manifest.files[0].sha256, size = long.MaxValue } };
        Reject(() => QuestGameArchiveDelivery.Stage(overflow, good, destination, true), "overflowed manifested size bound"); Preserved("Manifest size overflow");
        Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, test.archive, test.archive, false), "source archive as writable destination");
        Check(QuestGameContent.Hash(test.archive) == test.manifest.archiveSha256, "Delivery overwrote its owned source archive.");
        var unsupported = WithHash(test.manifest, test.manifest.archiveSha256); unsupported.archive = "other.zip";
        Reject(() => QuestGameArchiveDelivery.Stage(unsupported, good, destination, true), "unsupported archive scope"); Preserved("Unsupported archive");
        Reject(() => QuestGameArchiveDelivery.Stage(null!, good, destination, true), "null delivery manifest"); Preserved("Null manifest");
        try
        {
            QuestGameArchiveDelivery.Stage(test.manifest, good, destination, true, _ => throw new InvalidOperationException("receiver failed"));
            throw new Exception("Receiver failure was ignored.");
        }
        catch (InvalidOperationException error) { Check(error.Message == "receiver failed", "Delivery altered the callback exception."); }
        Preserved("Receiver failure");
        if (!OperatingSystem.IsWindows())
        {
            string outside = Path.Combine(directory, "outside"); Directory.CreateDirectory(outside);
            string linked = Path.Combine(directory, "linked"); Directory.CreateSymbolicLink(linked, outside);
            Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, good, Path.Combine(linked, "content.zip"), true), "symlink archive destination parent");
            Check(!System.IO.File.Exists(Path.Combine(outside, "content.zip")), "Delivery escaped through a symbolic-link parent.");
            string linkedFile = Path.Combine(directory, "linked-file.zip"); System.IO.File.CreateSymbolicLink(linkedFile, destination);
            Reject(() => QuestGameArchiveDelivery.Stage(test.manifest, good, linkedFile, true), "symlink archive destination file"); Preserved("Symlink target");
        }
    }

    static void ActualApk(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected optional actual APK and content manifest paths.");
        var manifest = JsonSerializer.Deserialize<QuestGameContentManifest>(System.IO.File.ReadAllText(args[1]), new JsonSerializerOptions { IncludeFields = true })!;
        string destination = Path.Combine(root, "actual", manifest.archive), output = Path.Combine(root, "actual", "extracted");
        var reports = new List<QuestGameContentProgress>(); var timer = Stopwatch.StartNew();
        QuestGameArchiveDelivery.Stage(manifest, args[0], destination, true, reports.Add);
        long stagedMs = timer.ElapsedMilliseconds;
        QuestGameContent.Extract(manifest, destination, output, manifest.archive, reports.Add);
        Check(QuestGameContent.IsReady(manifest, output), "Actual APK content delivery failed verification.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-apk-content-delivery", archive = manifest.archive, bytes = new FileInfo(destination).Length,
            sha256 = QuestGameContent.Hash(destination), stagedMilliseconds = stagedMs, totalMilliseconds = timer.ElapsedMilliseconds,
            contentBytes = manifest.files.Sum(file => file.size), files = manifest.files.Length, progressReports = reports.Count }));
    }
}
