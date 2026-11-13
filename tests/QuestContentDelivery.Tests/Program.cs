using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GloomhavenVR.Quest;

static partial class Program
{
    static int checks, sequence;
    static string root = "";
    const string Archive = "quest-startup-content.zip";
    static void Check(bool condition, string detail) { ++checks; if (!condition) throw new InvalidOperationException(detail); }
    static void Reject(Action action, string detail)
    {
        try { action(); } catch (InvalidDataException) { ++checks; return; }
        throw new InvalidOperationException("accepted-" + detail);
    }
    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static byte[] Bytes(int count, int seed = 617) { byte[] data = new byte[count]; new Random(seed).NextBytes(data); return data; }
    sealed class Fixture
    {
        internal string Directory, Source, Output, Staged;
        internal QuestGameContentManifest Manifest;
        internal byte[][] Payloads;
        internal Fixture(bool apk = true, CompressionLevel compression = CompressionLevel.NoCompression, bool extra = false, bool wrongFileHash = false)
        {
            Directory = Path.Combine(root, (++sequence).ToString()); System.IO.Directory.CreateDirectory(Directory);
            Payloads = new[] { Bytes(2 * 1048576 + 13), Bytes(1048576 + 59, 619), Array.Empty<byte>() };
            string[] names = { "Movies/original-1.mp4", "Rulebase/original-2.yml", "StreamingAssets/empty.bin" };
            Manifest = new QuestGameContentManifest { schema = 1, inputKey = "fixture-owned-input", archive = Archive,
                files = names.Select((name, index) => new QuestGameContentFile { path = name, size = Payloads[index].Length,
                    sha256 = Sha(wrongFileHash && index == 0 ? Bytes(Payloads[index].Length, 900) : Payloads[index]) }).ToArray() };
            string zipPath = Path.Combine(Directory, Archive);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                for (int i = 0; i < names.Length; ++i)
                    using (var stream = zip.CreateEntry(names[i], compression).Open()) stream.Write(Payloads[i]);
                if (extra) using (var stream = zip.CreateEntry("unmanifested.txt").Open()) stream.Write(new byte[] { 1 });
            }
            Manifest.archiveSha256 = QuestGameContent.Hash(zipPath);
            Source = zipPath;
            if (apk)
            {
                Source = Path.Combine(Directory, "player.apk");
                using var zip = ZipFile.Open(Source, ZipArchiveMode.Create);
                using var stream = zip.CreateEntry("assets/" + Archive, compression).Open();
                stream.Write(File.ReadAllBytes(zipPath));
            }
            Staged = Path.Combine(Directory, "private", Archive + ".download"); Output = Path.Combine(Directory, "content");
        }
        internal QuestGameContentDeliveryResult Deliver(bool apk = true, Action<QuestGameContentProgress>? progress = null)
            => QuestGameContent.Deliver(Manifest, Output, Source, apk, Staged, Archive, progress);
        internal string Target(int index) => Path.Combine(Output, Manifest.files[index].path);
        internal void Match()
        {
            for (int i = 0; i < Payloads.Length; ++i) Check(File.ReadAllBytes(Target(i)).SequenceEqual(Payloads[i]), "exact-original-bytes");
        }
    }
    static void Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        if (args.Length > 1 && args[1] == "expect-native-unavailable")
        {
            Reject(QuestGameContent.ConfigureNativeHash, "native-unavailable");
            Console.WriteLine("PASS native unavailable fails before workers"); return;
        }
#if UNITY_ANDROID
        string unconfigured = Path.Combine(root, "before-configuration"); File.WriteAllText(unconfigured, "abc");
        Reject(() => QuestGameContent.Hash(unconfigured), "unconfigured-native");
#endif
        QuestGameContent.ConfigureNativeHash(); QuestGameContent.ConfigureNativeHash();
        Golden(); ColdWarm(); RepairAndResume(); Integrity(); ProgressInterruption(); InstallationCache();
        Console.WriteLine("PASS Quest content delivery: " + checks + " assertions; real worker/file/hash code, no headset timing claim.");
    }
    static void Golden()
    {
        // Padding boundaries and irregular chunk tails use platform SHA-256 as an
        // independent reference, in addition to the native fixed-vector self-test.
        foreach (int length in new[] { 0, 1, 3, 55, 56, 63, 64, 65, 127, 128, 262143, 262144, 262145, 1048576, 3 * 1048576 + 19 })
        {
            byte[] bytes = Bytes(length); string path = Path.Combine(root, "golden-" + length); File.WriteAllBytes(path, bytes);
            Check(QuestGameContent.Hash(path) == Sha(bytes), "hash-golden-" + length);
        }
        Parallel.For(0, 16, i =>
        {
            byte[] bytes = Bytes(100000 + i, i); string path = Path.Combine(root, "parallel-" + i); File.WriteAllBytes(path, bytes);
            if (QuestGameContent.Hash(path) != Sha(bytes)) throw new InvalidOperationException("independent-native-contexts");
        }); Check(true, "parallel-native-contexts");
    }
    static void Progress(List<QuestGameContentProgress> rows, long expected)
    {
        Check(rows.Count > 0 && rows[^1].Phase == "content-ready", "completed-progress");
        Check(rows[^1].OverallProcessedBytes == expected && rows[^1].OverallTotalBytes == expected, "aggregate-exact-work");
        long processed = 0, chosen = -1;
        foreach (var row in rows)
        {
            Check(row.OverallProcessedBytes >= processed, "aggregate-monotonic"); processed = row.OverallProcessedBytes;
            Check(row.FileCount == 3 && row.FileIndex >= 0 && row.FileIndex <= 3, "manifest-file-count");
            if (row.OverallTotalBytes >= 0)
            {
                if (chosen < 0) chosen = row.OverallTotalBytes;
                Check(row.OverallTotalBytes == chosen && row.OverallProcessedBytes <= chosen, "stable-aggregate-plan");
            }
            else Check(chosen == -1, "aggregate-no-reset-to-unknown");
        }
        Check(rows.Count <= 30, "bounded-progress-not-per-buffer");
    }
    static void ColdWarm()
    {
        foreach (bool apk in new[] { false, true })
        foreach (var compression in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
        {
            var fixture = new Fixture(apk, compression); var reports = new List<QuestGameContentProgress>();
            var result = fixture.Deliver(apk, reports.Add); fixture.Match();
            Check(!result.ReusedContent && result.CopiedArchive && !result.ReusedArchive && result.VerifiedFiles == 0 && result.ExtractedFiles == 3, "cold-result-facts");
            long content = fixture.Payloads.Sum(p => (long)p.Length), archive = new FileInfo(fixture.Staged).Length;
            Check(result.CopiedBytes == archive && result.ExtractedBytes == content, "cold-byte-facts");
            Progress(reports, archive + content);
            Check(!reports.Any(row => row.Phase == "verifying-archive" || row.Phase == "verifying-file" || row.Phase == "checking-files"), "cold-no-duplicate-pass");
            Check(reports.Where(r => r.FileIndex > 0).All(r => r.File == fixture.Manifest.files[r.FileIndex - 1].path), "one-based-current-file");
            DateTime previous = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc); File.SetLastWriteTimeUtc(fixture.Target(0), previous);
            File.Delete(fixture.Source); File.Delete(fixture.Staged); reports.Clear();
            result = fixture.Deliver(apk, reports.Add); fixture.Match();
            Check(result.ReusedContent && !result.CopiedArchive && !result.ReusedArchive && result.VerifiedFiles == 3 && result.ExtractedFiles == 0, "warm-result-facts");
            Check(result.VerifiedBytes == content && result.ExtractedBytes == 0 && result.CopiedBytes == 0, "warm-full-byte-verification");
            Check(File.GetLastWriteTimeUtc(fixture.Target(0)) == previous, "warm-no-rewrite");
            Check(reports.All(r => r.Phase == "checking-files" || r.Phase == "content-ready"), "warm-no-archive-or-extraction");
            Progress(reports, content);
        }
    }
    static void RepairAndResume()
    {
        var fixture = new Fixture(); fixture.Deliver();
        DateTime timestamp = File.GetLastWriteTimeUtc(fixture.Target(0)); byte[] changed = (byte[])fixture.Payloads[0].Clone(); changed[777] ^= 1;
        File.WriteAllBytes(fixture.Target(0), changed); File.SetLastWriteTimeUtc(fixture.Target(0), timestamp); File.Delete(fixture.Source);
        var reports = new List<QuestGameContentProgress>(); var result = fixture.Deliver(progress: reports.Add); fixture.Match();
        Check(result.ReusedArchive && !result.CopiedArchive && !result.ReusedContent && result.ExtractedFiles == 1 && result.VerifiedFiles == 2, "same-size-restored-mtime-repair");
        Progress(reports, fixture.Payloads.Sum(p => (long)p.Length) + new FileInfo(fixture.Staged).Length + fixture.Payloads[0].Length);
        var interrupted = new Fixture(); bool thrown = false;
        try
        {
            interrupted.Deliver(progress: row =>
            {
                if (row.Phase == "extracting-file" && row.FileIndex == 2 && row.ProcessedBytes == 0) throw new OperationCanceledException("fixture interruption");
            });
        }
        catch (OperationCanceledException) { thrown = true; }
        Check(thrown && File.Exists(interrupted.Target(0)) && !File.Exists(interrupted.Target(1)), "interrupted-keeps-completed-files");
        Check(Directory.GetFiles(interrupted.Output, "*.tmp", SearchOption.AllDirectories).Length == 0, "interruption-cleans-active-temporary");
        timestamp = File.GetLastWriteTimeUtc(interrupted.Target(0)); File.Delete(interrupted.Source); reports.Clear();
        result = interrupted.Deliver(progress: reports.Add); interrupted.Match();
        Check(result.ReusedArchive && !result.CopiedArchive && result.VerifiedFiles == 1 && result.ExtractedFiles == 2, "interrupted-resumable-result");
        Check(File.GetLastWriteTimeUtc(interrupted.Target(0)) == timestamp, "interrupted-verified-file-not-rewritten");
        var stale = new Fixture(); Directory.CreateDirectory(Path.GetDirectoryName(stale.Staged)!); File.WriteAllText(stale.Staged, "incomplete");
        result = stale.Deliver(); stale.Match(); Check(result.CopiedArchive, "partial-archive-replaced");
    }
    static void Integrity()
    {
        var badArchive = new Fixture(); badArchive.Manifest.archiveSha256 = new string('0', 64);
        Reject(() => badArchive.Deliver(), "archive-sha"); Check(!Directory.Exists(badArchive.Output), "unverified-archive-never-extracts");
        Check(!File.Exists(badArchive.Staged) && !Directory.GetFiles(badArchive.Directory, "*.tmp", SearchOption.AllDirectories).Any(), "bad-archive-no-publish");
        var wrongFile = new Fixture(wrongFileHash: true);
        Reject(() => wrongFile.Deliver(), "file-sha"); Check(!File.Exists(wrongFile.Target(0)), "unverified-file-never-published");
        var extra = new Fixture(extra: true); Reject(() => extra.Deliver(), "zip-membership");
        Check(!File.Exists(extra.Target(0)), "zip-validated-before-writing");
        var corruptStaged = new Fixture(); corruptStaged.Deliver(); File.Delete(corruptStaged.Target(0));
        byte[] corrupt = File.ReadAllBytes(corruptStaged.Staged); corrupt[45] ^= 1; File.WriteAllBytes(corruptStaged.Staged, corrupt);
        var result = corruptStaged.Deliver(); Check(result.CopiedArchive && !result.ReusedArchive, "corrupt-retained-archive-recopied"); corruptStaged.Match();
        var changedArchive = new Fixture(); bool changed = false;
        Reject(() => changedArchive.Deliver(progress: row =>
        {
            if (!changed && row.Phase == "extracting-file" && row.ProcessedBytes == 0)
            { changed = true; File.SetLastWriteTimeUtc(changedArchive.Staged, new DateTime(2000, 1, 1)); }
        }), "archive-operation-identity");
        var changedWarm = new Fixture(); changedWarm.Deliver();
        Reject(() => changedWarm.Deliver(progress: row =>
        {
            if (row.Phase == "content-ready") File.SetLastWriteTimeUtc(changedWarm.Target(0), new DateTime(2000, 1, 1));
        }), "completion-operation-identity");
        string callbackHash = Path.Combine(root, "hash-final-callback"); File.WriteAllBytes(callbackHash, Bytes(123));
        Reject(() => QuestGameContent.Hash(callbackHash, row =>
        {
            if (row.ProcessedBytes == row.TotalBytes) File.SetLastWriteTimeUtc(callbackHash, new DateTime(2000, 1, 1));
        }), "hash-final-callback-identity");
#if UNITY_ANDROID
        var replacedAfterHash = new Fixture(); replacedAfterHash.Deliver(); bool replaced = false;
        File.SetLastWriteTimeUtc(replacedAfterHash.Target(0), new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Reject(() => replacedAfterHash.Deliver(progress: row =>
        {
            if (!replaced && row.Phase == "checking-files" && row.FileIndex == 1 && row.ProcessedBytes == row.TotalBytes)
            {
                replaced = true; DateTime previous = File.GetLastWriteTimeUtc(replacedAfterHash.Target(0));
                byte[] altered = (byte[])replacedAfterHash.Payloads[0].Clone(); altered[234] ^= 1;
                File.WriteAllBytes(replacedAfterHash.Target(0), altered); File.SetLastWriteTimeUtc(replacedAfterHash.Target(0), previous);
            }
        }), "hash-final-restored-mtime");
        var changedPrior = new Fixture(); changedPrior.Deliver(); File.Delete(changedPrior.Target(1)); bool mutated = false;
        File.SetLastWriteTimeUtc(changedPrior.Target(0), new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Reject(() => changedPrior.Deliver(progress: row =>
        {
            if (!mutated && row.Phase == "extracting-file" && row.FileIndex == 2)
            {
                mutated = true; DateTime previous = File.GetLastWriteTimeUtc(changedPrior.Target(0));
                byte[] altered = (byte[])changedPrior.Payloads[0].Clone(); altered[129] ^= 1;
                File.WriteAllBytes(changedPrior.Target(0), altered); File.SetLastWriteTimeUtc(changedPrior.Target(0), previous);
            }
        }), "same-size-operation-ctime");
#endif
        if (!OperatingSystem.IsWindows())
        {
            var linked = new Fixture(); Directory.CreateDirectory(linked.Output); string outside = Path.Combine(linked.Directory, "outside"); Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(Path.Combine(linked.Output, "Movies"), outside);
            Reject(() => linked.Deliver(), "symbolic-content-parent"); Check(!File.Exists(Path.Combine(outside, "original-1.mp4")), "private-root-containment");
            var archived = new Fixture(); string alias = Path.Combine(archived.Directory, "alias"); Directory.CreateSymbolicLink(alias, outside);
            archived.Staged = Path.Combine(alias, "archive.zip"); Reject(() => archived.Deliver(), "symbolic-archive-parent");
        }
        var invalid = new Fixture(); invalid.Manifest.files[0].path = "../outside";
        Reject(() => invalid.Deliver(), "manifest-traversal");
        var aliased = new Fixture(); aliased.Staged = aliased.Target(0); Reject(() => aliased.Deliver(), "archive-content-alias");
    }
    static void ProgressInterruption()
    {
        var interrupted = new Fixture(); Directory.CreateDirectory(Path.GetDirectoryName(interrupted.Staged)!); File.WriteAllText(interrupted.Staged, "safe previous archive");
        bool caught = false;
        try { interrupted.Deliver(progress: row => { if (row.Phase == "copying-archive" && row.ProcessedBytes > 0) throw new OperationCanceledException(); }); }
        catch (OperationCanceledException) { caught = true; }
        Check(caught && File.ReadAllText(interrupted.Staged) == "safe previous archive", "failed-copy-retains-prior-archive");
        Check(!Directory.GetFiles(interrupted.Directory, "*.tmp", SearchOption.AllDirectories).Any(), "failed-copy-no-temporary");
    }
}
