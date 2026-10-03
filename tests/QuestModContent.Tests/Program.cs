using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
    static void Main()
    {
        root = Path.Combine(Path.GetTempPath(), "ghvr-quest-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { Run(); }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS Quest mod/startup content: " + checks + " assertions (production verifier/extractor).");
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
}
