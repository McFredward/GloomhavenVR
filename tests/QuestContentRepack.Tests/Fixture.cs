// The native JsonUtility icall requires an Editor process. This host-only shim
// isolates JSON parsing while executing the complete actual Editor source and
// Unity's own Mono ZIP implementation, with its actual package SDK references.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using GloomhavenVR.Quest.Editor;

namespace UnityEngine
{
    public static class JsonUtility
    {
        public static T FromJson<T>(string value) { return JsonConvert.DeserializeObject<T>(value); }
        public static string ToJson(object value, bool prettyPrint) { return JsonConvert.SerializeObject(value, prettyPrint ? Formatting.Indented : Formatting.None); }
    }
    public static class Debug { public static void Log(object value) { Console.WriteLine(value); } }
}

public sealed class ContentFile { public string path, sha256; public long size; }
public sealed class ContentManifest
{
    public int schema = 1;
    public string inputKey = new string('f', 64), archive = "quest-startup-content.zip", archiveSha256;
    public bool externalDelivery = true;
    public ContentFile[] files;
}

internal static class Program
{
    const string ManifestPath = "Assets/Quest/Resources/quest-startup-content.json";
    const string ArchivePath = "Assets/StreamingAssets/quest-startup-content.zip";
    const string NativePath = "StreamingAssets/aa/Android/original.bundle";
    static readonly DateTimeOffset Stamp = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly MethodInfo Repack = typeof(QuestStartupAddressablesBuild).GetMethod("RepackContent", BindingFlags.NonPublic | BindingFlags.Static);
    static int assertions;
    static byte[] native, movie;
    static string proofRoot;

    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    static ContentFile Row(string path, byte[] bytes) { return new ContentFile { path = path, size = bytes.LongLength, sha256 = Hash(bytes) }; }
    static ContentManifest Manifest() { return JsonConvert.DeserializeObject<ContentManifest>(File.ReadAllText(ManifestPath)); }
    static void Save(ContentManifest manifest) { File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented)); }

    static void Zip(Dictionary<string, byte[]> entries)
    {
        using (var zip = new ZipArchive(File.Create(ArchivePath), ZipArchiveMode.Create))
            foreach (var pair in entries)
            {
                var entry = zip.CreateEntry(pair.Key, CompressionLevel.Optimal); entry.LastWriteTime = Stamp;
                using (var stream = entry.Open()) stream.Write(pair.Value, 0, pair.Value.Length);
            }
    }

    static void Seed(string name, bool existingNative)
    {
        string root = Path.Combine(proofRoot, name); Directory.CreateDirectory(root); Directory.SetCurrentDirectory(root);
        Directory.CreateDirectory("Assets/Quest/Resources"); Directory.CreateDirectory("Assets/StreamingAssets");
        Directory.CreateDirectory("native/Android"); File.WriteAllBytes("native/Android/original.bundle", native);
        File.WriteAllText("native/settings.json", "{\"originalCatalog\":true}"); File.WriteAllText("native/ignored.meta", "not payload");
        var entries = new Dictionary<string, byte[]> { { "StreamingAssets/Movies/original.mp4", movie }, { "StreamingAssets/retained.bundle", native } };
        if (existingNative)
        {
            entries.Add(NativePath, native); entries.Add("StreamingAssets/aa/settings.json", File.ReadAllBytes("native/settings.json"));
        }
        Zip(entries); Save(new ContentManifest { files = entries.Select(pair => Row(pair.Key, pair.Value)).ToArray(), archiveSha256 = Hash(File.ReadAllBytes(ArchivePath)) });
    }

    static void RunRepack()
    {
        try { Repack.Invoke(null, new object[] { "native" }); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    static void MustFail(Action mutate, string label)
    {
        mutate(); byte[] archive = File.ReadAllBytes(ArchivePath), manifest = File.ReadAllBytes(ManifestPath);
        try { RunRepack(); throw new InvalidOperationException("did not reject " + label); }
        catch (InvalidDataException) { Check(true, "reject " + label); }
        Check(File.ReadAllBytes(ArchivePath).SequenceEqual(archive), "failure preserves archive " + label);
        Check(File.ReadAllBytes(ManifestPath).SequenceEqual(manifest), "failure preserves manifest " + label);
        Check(!Directory.GetFiles("Assets/StreamingAssets", "*.repack-*").Any(), "failure removes only owned temporary ZIP " + label);
    }

    static void VerifyContent()
    {
        var manifest = Manifest(); Check(manifest.inputKey == new string('f', 64) && manifest.externalDelivery, "runtime identity unchanged");
        Check(manifest.archiveSha256 == Hash(File.ReadAllBytes(ArchivePath)), "published actual ZIP hash");
        Check(manifest.files.Select(row => row.path).SequenceEqual(manifest.files.Select(row => row.path).OrderBy(path => path, StringComparer.Ordinal)), "published sorted files");
        using (var zip = new ZipArchive(File.OpenRead(ArchivePath), ZipArchiveMode.Read))
        {
            Check(zip.Entries.Count == manifest.files.Length, "exact entry count");
            foreach (var row in manifest.files)
            {
                var entry = zip.GetEntry(row.path); Check(entry != null && entry.Length == row.size, "exact path/size " + row.path);
                using (var stream = entry.Open()) using (var data = new MemoryStream())
                {
                    stream.CopyTo(data); Check(Hash(data.ToArray()) == row.sha256, "exact bytes " + row.path);
                }
            }
            Check(zip.GetEntry("StreamingAssets/aa/ignored.meta") == null, "native metas are not payload");
        }
    }

    static Dictionary<string, ushort> CompressionMethods(string path)
    {
        byte[] bytes = File.ReadAllBytes(path); var result = new Dictionary<string, ushort>(StringComparer.Ordinal);
        int eocd = bytes.Length - 22;
        for (; eocd >= 0 && BitConverter.ToUInt32(bytes, eocd) != 0x06054b50; --eocd) { }
        Check(eocd >= 0, "ordinary fixture has an EOCD");
        int offset = checked((int)BitConverter.ToUInt32(bytes, eocd + 16));
        for (int count = BitConverter.ToUInt16(bytes, eocd + 10); count > 0; --count)
        {
            Check(BitConverter.ToUInt32(bytes, offset) == 0x02014b50, "actual central directory record");
            int nameLength = BitConverter.ToUInt16(bytes, offset + 28), extra = BitConverter.ToUInt16(bytes, offset + 30), comment = BitConverter.ToUInt16(bytes, offset + 32);
            string name = System.Text.Encoding.UTF8.GetString(bytes, offset + 46, nameLength);
            result.Add(name, BitConverter.ToUInt16(bytes, offset + 10)); offset += 46 + nameLength + extra + comment;
        }
        return result;
    }

    static void FunctionalControls()
    {
        Seed("first spaces &100%", false); RunRepack(); VerifyContent();
        var methods = CompressionMethods(ArchivePath);
        Check(methods[NativePath] == 0, "actual Unity Mono native bundle ZIP Stored");
        Check(methods["StreamingAssets/Movies/original.mp4"] == 8, "original movie retains Deflate policy");
        Check(methods["StreamingAssets/retained.bundle"] == 8, "original non-aa bundle retains Deflate policy");
        Check(methods["StreamingAssets/aa/settings.json"] == 8, "native catalog retains Deflate policy");
        byte[] beforeZip = File.ReadAllBytes(ArchivePath), beforeManifest = File.ReadAllBytes(ManifestPath);
        File.SetLastWriteTimeUtc(ArchivePath, Stamp.UtcDateTime); File.SetLastWriteTimeUtc(ManifestPath, Stamp.UtcDateTime);
        RunRepack(); Check(File.ReadAllBytes(ArchivePath).SequenceEqual(beforeZip), "unchanged ZIP byte-exact skip");
        Check(File.ReadAllBytes(ManifestPath).SequenceEqual(beforeManifest), "unchanged manifest byte-exact skip");
        Check(File.GetLastWriteTimeUtc(ArchivePath) == Stamp.UtcDateTime && File.GetLastWriteTimeUtc(ManifestPath) == Stamp.UtcDateTime, "skip performs no publish write");
        Check(!Directory.GetFiles("Assets/StreamingAssets", "*.repack-*").Any(), "skip has no repack temporary");
        byte[] changed = (byte[])native.Clone(); changed[changed.Length - 1] ^= 1; File.WriteAllBytes("native/Android/original.bundle", changed);
        RunRepack(); VerifyContent(); Check(!File.ReadAllBytes(ArchivePath).SequenceEqual(beforeZip), "same-size changed native bytes trigger repack");
        Check(Manifest().files.Single(row => row.path == NativePath).sha256 == Hash(changed), "changed native bytes receive actual hash");
        File.WriteAllBytes("native/Android/added.bundle", native); RunRepack(); VerifyContent();
        Check(Manifest().files.Any(row => row.path == "StreamingAssets/aa/Android/added.bundle"), "new native member added");
        File.Delete("native/Android/original.bundle"); RunRepack(); VerifyContent();
        Check(!Manifest().files.Any(row => row.path == NativePath), "removed native member removed");

        Seed("policy-migration", true); File.SetLastWriteTimeUtc(ArchivePath, Stamp.UtcDateTime); RunRepack();
        Check(File.GetLastWriteTimeUtc(ArchivePath) == Stamp.UtcDateTime, "valid older Deflate ZIP reused without policy-only rebuild");
        Check(CompressionMethods(ArchivePath)[NativePath] == 8, "existing equivalent native ZIP retained exactly");

        Seed("archive-sha", true); MustFail(() => { var manifest = Manifest(); manifest.archiveSha256 = new string('0', 64); Save(manifest); }, "archive SHA");
        Seed("entry-sha", true); MustFail(() => { var manifest = Manifest(); manifest.files[0].sha256 = new string('0', 64); Save(manifest); }, "entry SHA despite valid archive hash");
        Seed("entry-size", true); MustFail(() => { var manifest = Manifest(); manifest.files[0].size++; Save(manifest); }, "entry size");
        Seed("entry-name", true); MustFail(() => { var manifest = Manifest(); manifest.files[0].path = "StreamingAssets/not-the-original"; Save(manifest); }, "exact entry name");
        Seed("unexpected", true); MustFail(() => { using (var zip = new ZipArchive(File.Open(ArchivePath, FileMode.Open), ZipArchiveMode.Update)) zip.CreateEntry("undeclared"); var manifest = Manifest(); manifest.archiveSha256 = Hash(File.ReadAllBytes(ArchivePath)); Save(manifest); }, "unexpected ZIP member");
        Seed("duplicate", true); MustFail(() => { using (var zip = new ZipArchive(File.Open(ArchivePath, FileMode.Open), ZipArchiveMode.Update)) zip.CreateEntry(NativePath); var manifest = Manifest(); manifest.archiveSha256 = Hash(File.ReadAllBytes(ArchivePath)); manifest.files = manifest.files.Concat(new[] { Row(NativePath, new byte[0]) }).ToArray(); Save(manifest); }, "duplicate ZIP member");
        Seed("schema", true); MustFail(() => { var manifest = Manifest(); manifest.schema = 2; Save(manifest); }, "manifest schema");
        Seed("empty-native", true); MustFail(() => { Directory.Delete("native", true); Directory.CreateDirectory("native"); }, "empty native output");

    }

    static Dictionary<string, object> Benchmark(string name, CompressionLevel policy)
    {
        string path = Path.Combine(proofRoot, name + ".zip"); var timings = new List<double>();
        for (int round = 0; round < 4; ++round)
        {
            var watch = Stopwatch.StartNew();
            using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(NativePath, policy); entry.LastWriteTime = Stamp;
                using (var input = new MemoryStream(native)) using (var output = entry.Open()) input.CopyTo(output, 65536);
            }
            watch.Stop(); if (round != 0) timings.Add(watch.Elapsed.TotalMilliseconds);
        }
        using (var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
        using (var input = zip.GetEntry(NativePath).Open()) using (var output = new MemoryStream())
        {
            input.CopyTo(output); Check(output.ToArray().SequenceEqual(native), name + " preserves exact native LZ4 bytes");
        }
        return new Dictionary<string, object> { { "zipBytes", new FileInfo(path).Length }, { "zipSha256", Hash(File.ReadAllBytes(path)) }, { "compressionMethod", CompressionMethods(path)[NativePath] }, { "medianMilliseconds", timings.OrderBy(value => value).ElementAt(1) }, { "runsMilliseconds", timings.ToArray() }, { "nativeBytesSha256", Hash(native) } };
    }

    public static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("fixture ORIGINAL_LZ4_BUNDLE OWNED_PROOF_ROOT");
        native = File.ReadAllBytes(args[0]); proofRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(proofRoot);
        movie = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("original retained movie fixture\n", 10000)));
        Check(Repack != null, "exact actual production method loaded"); FunctionalControls();
        var optimal = Benchmark("native-optimal", CompressionLevel.Optimal); var noCompression = Benchmark("mono-no-compression-enum", CompressionLevel.NoCompression);
        var receipt = new Dictionary<string, object> { { "schema", 1 }, { "scope", "actual-Unity-Mono-content-repack" }, { "assertions", assertions }, { "actualNativeBundleBytes", native.LongLength }, { "nativeBundleSha256", Hash(native) }, { "monoOptimal", optimal }, { "monoNoCompression", noCompression }, { "jsonIcallShimmed", true }, { "unityEditorLaunched", false }, { "fullArchiveTimingVerified", false }, { "headsetVerified", false } };
        File.WriteAllText(Path.Combine(proofRoot, "result.json"), JsonConvert.SerializeObject(receipt, Formatting.Indented));
        Console.WriteLine("PASS actual Unity Mono repack: " + assertions + " assertions; JSON icall shim only; native byte SHA " + Hash(native));
        Console.WriteLine("Mono Optimal/NoCompression enum median milliseconds " + optimal["medianMilliseconds"] + "/" + noCompression["medianMilliseconds"] + "; bytes " + optimal["zipBytes"] + "/" + noCompression["zipBytes"]);
    }
}
