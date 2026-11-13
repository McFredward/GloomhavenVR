using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GloomhavenVR.Quest;

internal static class Program
{
    const string Payload = "quest-card-alpha.bin";
    const string Metadata = "Quest/card-alpha.json";
    static string Work = "";
    static int Assertions, Sequence;

    static void Check(bool condition, string key)
    {
        Assertions++;
        if (!condition) throw new InvalidOperationException("ASSERT[" + key + "]");
    }

    static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static bool Succeeds(Func<bool> action, string key)
    {
        try { return action(); }
        catch (Exception error) { throw new InvalidOperationException("ASSERT[" + key + "]", error); }
    }
    static string Manifest(byte[] bytes) => JsonSerializer.Serialize(new { schema = 1, bytes = bytes.Length, sha256 = Digest(bytes) });
    static byte[] Data(byte seed) => Enumerable.Range(0, 119).Select(i => (byte)(seed + i)).ToArray();
    static string Folder(string name)
    {
        string path = Path.Combine(Work, (++Sequence).ToString() + "-" + name); Directory.CreateDirectory(path); return path;
    }
    static void Local(string source, string manifest, byte[]? payload)
    {
        Directory.CreateDirectory(Path.Combine(source, "Quest"));
        File.WriteAllText(Path.Combine(source, Metadata), manifest);
        if (payload != null) File.WriteAllBytes(Path.Combine(source, Payload), payload);
        else File.Delete(Path.Combine(source, Payload));
    }
    static string Apk(string manifest, byte[]? payload)
    {
        string path = Path.Combine(Folder("apk"), "actual.zip.apk");
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        if (manifest.Length != 0)
        {
            using var writer = new StreamWriter(archive.CreateEntry("assets/" + Metadata).Open(), new UTF8Encoding(false));
            writer.Write(manifest);
        }
        if (payload != null)
        {
            using var output = archive.CreateEntry("assets/" + Payload, CompressionLevel.Optimal).Open();
            output.Write(payload);
        }
        return path;
    }
    static void CleanTemporary(string root, string key) => Check(!Directory.Exists(root)
        || !Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "no-temporary-" + key);
    static void Installed(string root, byte[] payload, string manifest, string key)
    {
        string target = Path.Combine(root, Payload);
        Check(File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(payload), "exact-payload-" + key);
        Check(Digest(File.ReadAllBytes(target)) == Digest(payload), "exact-sha-" + key);
        Check(File.ReadAllText(target + ".receipt.json") == manifest, "exact-receipt-" + key);
        CleanTemporary(root, key);
    }
    static void Fails<T>(Action action, string root, byte[] before, string receipt, string key) where T : Exception
    {
        bool rejected = false;
        try { action(); }
        catch (T) { rejected = true; }
        Check(rejected, key);
        Installed(root, before, receipt, key);
    }

    static void Main(string[] args)
    {
        Work = args[0]; Directory.CreateDirectory(Work);
        string source = Folder("streaming-assets"), root = Folder("resource-root");
        byte[] first = Data(7), second = Data(39), third = Data(73);
        string firstManifest = Manifest(first), secondManifest = Manifest(second), thirdManifest = Manifest(third);
        Check(!QuestCardAlphaContent.Install("", source, root), "optional-local-manifest");
        Check(!QuestCardAlphaContent.Install(Apk("", first), "unreadable-unused-path", root), "optional-apk-manifest");
        Check(!File.Exists(Path.Combine(root, Payload)), "older-optional-no-install");

        Local(source, firstManifest, first);
        Check(QuestCardAlphaContent.Install("", source, root), "local-install");
        Installed(root, first, firstManifest, "local");
        DateTime sentinel = new(2002, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(root, Payload), sentinel);
        File.SetLastWriteTimeUtc(Path.Combine(root, Payload) + ".receipt.json", sentinel);
        File.Delete(Path.Combine(source, Payload));
        Check(Succeeds(() => QuestCardAlphaContent.Install("", source, root), "warm-local-no-payload-reread"), "warm-local-no-payload-reread");
        Check(File.GetLastWriteTimeUtc(Path.Combine(root, Payload)) == sentinel
            && File.GetLastWriteTimeUtc(Path.Combine(root, Payload) + ".receipt.json") == sentinel, "warm-local-no-rewrite");
        Installed(root, first, firstManifest, "warm-local");

        Local(source, secondManifest, second);
        Check(QuestCardAlphaContent.Install("", source, root), "updated-same-sized-payload");
        Installed(root, second, secondManifest, "updated-same-sized-payload");

        foreach ((string text, string key) in new[] {
            (new string(' ',4097), "metadata-byte-bound"),
            ("{\"schema\":0,\"bytes\":119,\"sha256\":\"" + Digest(first) + "\"}", "metadata-schema"),
            ("{\"schema\":1,\"bytes\":8,\"sha256\":\"" + Digest(first) + "\"}", "metadata-lower-bound"),
            ("{\"schema\":1,\"bytes\":16777217,\"sha256\":\"" + Digest(first) + "\"}", "metadata-upper-bound"),
            ("{\"schema\":1,\"bytes\":119,\"sha256\":\"" + Digest(first).ToUpperInvariant() + "\"}", "metadata-digest-case"),
            ("{\"schema\":1,\"bytes\":119,\"sha256\":\"" + new string('x',64) + "\"}", "metadata-digest-hex"),
            ("{\"schema\":1,\"bytes\":119,\"sha256\":\"short\"}", "metadata-digest-length"),
            ("null", "metadata-null") })
        {
            Local(source, text, first);
            Fails<InvalidDataException>(() => QuestCardAlphaContent.Install("", source, root), root, second, secondManifest, key);
        }
        Local(source, "{broken", first);
        Fails<ArgumentException>(() => QuestCardAlphaContent.Install("", source, root), root, second, secondManifest, "metadata-malformed-json-boundary");
        Local(source, thirdManifest, third[..^1]);
        Fails<EndOfStreamException>(() => QuestCardAlphaContent.Install("", source, root), root, second, secondManifest, "truncated-local-payload");
        Local(source, thirdManifest, third.Concat(new byte[] { 1 }).ToArray());
        Fails<InvalidDataException>(() => QuestCardAlphaContent.Install("", source, root), root, second, secondManifest, "extra-local-payload");
        Local(source, thirdManifest, first);
        Fails<InvalidDataException>(() => QuestCardAlphaContent.Install("", source, root), root, second, secondManifest, "payload-sha-mismatch");

        string apk = Apk(thirdManifest, third);
        Check(QuestCardAlphaContent.Install(apk, "unused-android-streaming-path", root), "apk-install");
        Installed(root, third, thirdManifest, "apk");
        File.SetLastWriteTimeUtc(Path.Combine(root, Payload), sentinel);
        Check(Succeeds(() => QuestCardAlphaContent.Install(Apk(thirdManifest, null), "unreadable-unused-path", root), "warm-apk-no-payload-entry-reread"), "warm-apk-no-payload-entry-reread");
        Check(File.GetLastWriteTimeUtc(Path.Combine(root, Payload)) == sentinel, "warm-apk-no-rewrite");
        Check(!QuestCardAlphaContent.Install(Apk("", first), "unused", root), "older-apk-preserves-existing");
        Installed(root, third, thirdManifest, "older-apk");
        Fails<InvalidDataException>(() => QuestCardAlphaContent.Install(Apk(new string(' ',4097), first), "unused", root), root, third, thirdManifest, "apk-metadata-byte-bound");
        Fails<InvalidDataException>(() => QuestCardAlphaContent.Install(Apk(firstManifest, null), "unused", root), root, third, thirdManifest, "apk-payload-missing");
        Fails<InvalidDataException>(() => QuestCardAlphaContent.Install(Apk(firstManifest, first[..^1]), "unused", root), root, third, thirdManifest, "apk-payload-size-mismatch");
        Fails<InvalidDataException>(() => QuestCardAlphaContent.Install(Apk(firstManifest, second), "unused", root), root, third, thirdManifest, "apk-payload-sha-mismatch");
        string broken = Apk(firstManifest, first);
        byte[] archiveBytes = File.ReadAllBytes(broken); File.WriteAllBytes(broken, archiveBytes[..^22]);
        Fails<InvalidDataException>(() => QuestCardAlphaContent.Install(broken, "unused", root), root, third, thirdManifest, "truncated-apk");
        Check(QuestCardAlphaContent.Install(Apk(firstManifest, first), "unused", root), "apk-updated-payload");
        Installed(root, first, firstManifest, "apk-updated");
        CleanTemporary(root, "all");
        Console.WriteLine("PASS Quest card alpha content: " + Assertions + " assertions; actual file, ZipArchive, SHA256 and atomic replacement; JSON boundary stubbed.");
    }
}
