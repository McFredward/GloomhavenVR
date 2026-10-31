using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GloomhavenVR.Quest;

internal static class Program
{
    static int Assertions;
    const string A = "ghvr-figure-meshes-quest-20-00.bundle", B = "ghvr-figure-meshes-quest-5-00.bundle", Index = "ghvr-figure-meshes-index.json";
    static void Check(bool yes, string name) { Assertions++; if (!yes) throw new Exception("ASSERT[" + name + "]"); }
    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    static string Metadata(Dictionary<string, byte[]> parts) => JsonSerializer.Serialize(new { schema = 1, producerKey = new string('a', 64), target = "Android",
        files = parts.Select(pair => new { path = pair.Key, sha256 = Sha(pair.Value), size = pair.Value.LongLength }) });
    static void Apk(string apk, string manifest, Dictionary<string, byte[]> parts)
    {
        using var zip = new ZipArchive(File.Create(apk), ZipArchiveMode.Create);
        using (var text = new StreamWriter(zip.CreateEntry("assets/Quest/figure-meshes.json").Open())) text.Write(manifest);
        foreach (var item in parts) using (var stream = zip.CreateEntry("assets/QuestFigureMeshes/" + item.Key).Open()) stream.Write(item.Value);
    }
    static void Reject(Action action, string name)
    { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, name); }
    static int Main(string[] args)
    {
        string work = args[0]; Directory.CreateDirectory(work);
        string apk = Path.Combine(work, "source.apk"), target = Path.Combine(work, "resources");
        var parts = new Dictionary<string, byte[]> { [Index] = "{\"entries\":[]}"u8.ToArray(), [A] = Enumerable.Repeat((byte)42, 20000).ToArray(), [B] = Enumerable.Repeat((byte)43, 30000).ToArray() };
        string manifest = Metadata(parts); Apk(apk, manifest, parts);
        var events = new List<QuestGameContentProgress>();
        Check(QuestFigureMeshContent.Install(apk, "", target, events.Add), "cold-install");
        foreach (var item in parts) Check(File.ReadAllBytes(Path.Combine(target, item.Key)).SequenceEqual(item.Value), "exact-" + item.Key);
        Check(events.Last().File == Index, "index-last");
        Check(events.Last().OverallProcessedBytes == events.Last().OverallTotalBytes, "exact-global-progress");
        Check(events.Zip(events.Skip(1)).All(pair => pair.First.OverallProcessedBytes <= pair.Second.OverallProcessedBytes), "monotonic-progress");
        Apk(apk, manifest, new()); events.Clear();
        Check(QuestFigureMeshContent.Install(apk, "", target, events.Add), "warm-with-no-source-entries");
        Check(events.Count == 0, "warm-no-bank-read");
        // Only a changed part is copied. All other source entries may be absent.
        parts[A] = Enumerable.Repeat((byte)99, 20000).ToArray(); manifest = Metadata(parts);
        Apk(apk, manifest, new() { [A] = parts[A] });
        Check(QuestFigureMeshContent.Install(apk, "", target, events.Add), "targeted-update");
        Check(events.All(item => item.File == A), "unchanged-parts-not-read");
        // A missing/corrupt part never publishes its index or a success receipt.
        string fresh = Path.Combine(work, "corrupt");
        foreach (byte[] bad in new[] { new byte[] { 1 }, Enumerable.Repeat((byte)1, 20000).ToArray() })
        {
            Apk(apk, manifest, new() { [A] = bad, [B] = parts[B], [Index] = parts[Index] });
            Reject(() => QuestFigureMeshContent.Install(apk, "", fresh, _ => { }), "bad-part-rejected");
            Check(!File.Exists(Path.Combine(fresh, Index)), "no-premature-index");
            Check(!Directory.EnumerateFiles(fresh, "*.tmp").Any(), "no-temp-leak");
        }
        Apk(apk, manifest, parts);
        Reject(() => QuestFigureMeshContent.Install(apk, "", fresh, _ => throw new OperationCanceledException()), "cancelled-copy");
        Check(!Directory.EnumerateFiles(fresh, "*.tmp").Any(), "cancelled-copy-cleanup");
        Check(QuestFigureMeshContent.Install(apk, "", fresh, _ => { }), "resume-after-cancel");
        string duplicate = manifest.Replace('"' + B + '"', '"' + A + '"'); Apk(apk, duplicate, parts);
        Reject(() => QuestFigureMeshContent.Install(apk, "", target, _ => { }), "duplicate-member");
        string unsafePath = manifest.Replace(A, "../" + A); Apk(apk, unsafePath, parts);
        Reject(() => QuestFigureMeshContent.Install(apk, "", target, _ => { }), "unsafe-path");
        Apk(apk, manifest, parts); int owner = Environment.CurrentManagedThreadId, reports = 0;
        var routine = QuestFigureMeshContent.InstallRoutine(apk, "", Path.Combine(work, "routine"),
            _ => { Check(Environment.CurrentManagedThreadId == owner, "presentation-callback-on-owner-thread"); reports++; },
            () => Check(Environment.CurrentManagedThreadId == owner, "present-on-owner-thread"));
        while (routine.MoveNext()) Thread.Sleep(1);
        Check(reports > 0, "cold-routine-reports-copy");
        Console.WriteLine("PASS " + Assertions + " exact file/ZIP/SHA/progress/targeted-resume assertions; JSON boundary stubbed; Android runtime unverified.");
        return 0;
    }
}
