using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace ShaderOcclusionPatcher;

// ---------------------------------------------------------------------------
// manifest model
// ---------------------------------------------------------------------------

public sealed class PassRecord
{
    public int SubShader { get; set; }
    public int Pass { get; set; }
    public string PassName { get; set; } = "";
    public int PassType { get; set; }             // 0=Normal 1=Use 2=Grab
    public float ZTestBefore { get; set; }
    public float ZTestAfter { get; set; }         // == before when untouched
    public string ZTestProp { get; set; } = "";   // non-empty => value driven by a material property
    public float ZWrite { get; set; }
    public string ZWriteProp { get; set; } = "";
    public string Queue { get; set; } = "";       // subshader/pass Queue tag if present
}

public sealed class ShaderRecord
{
    public string Name { get; set; } = "";
    public long PathId { get; set; }
    public string InnerFile { get; set; } = "";   // CAB name for bundles, "" for loose files
    public List<PassRecord> Passes { get; set; } = new();
    public bool Patched { get; set; }             // this run changed at least one pass
    public bool NoAlwaysPass { get; set; }        // no zTest==8 found (needs re-analysis OR already patched)
}

public sealed class FileRecord
{
    public string RelativePath { get; set; } = "";
    public string Container { get; set; } = "";   // "serialized" | "bundle"
    public string Compression { get; set; } = ""; // bundles only
    public string HashBefore { get; set; } = "";
    public string HashAfter { get; set; } = "";
    public bool Modified { get; set; }
    public bool BackedUpThisRun { get; set; }
    public bool BackupAlreadyExisted { get; set; }
    public List<ShaderRecord> Shaders { get; set; } = new();
}

public sealed class Manifest
{
    public string Tool { get; set; } = "ShaderOcclusionPatcher";
    public string Version { get; set; } = Cli.ToolVersion;
    public string Mode { get; set; } = "";
    public string Date { get; set; } = "";
    public string GameData { get; set; } = "";
    public string? BackupDir { get; set; }
    public CatalogCrcReport? CatalogCrc { get; set; }
    public List<FileRecord> Files { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

// ---------------------------------------------------------------------------
// dump model (diagnostic `dump` command)
// ---------------------------------------------------------------------------

public sealed class DumpPassRecord
{
    public int Index { get; set; }
    public int Type { get; set; }                 // 0=Normal 1=Use 2=Grab
    public string Name { get; set; } = "";
    public Dictionary<string, string> Tags { get; set; } = new();
    public float ZTest { get; set; }
    public string ZTestProp { get; set; } = "";
    public float ZWrite { get; set; }
    public string ZWriteProp { get; set; } = "";
    public float SrcBlend { get; set; }
    public float DstBlend { get; set; }
    public string Queue { get; set; } = "";
    public List<string> ReferencedNames { get; set; } = new();  // per-pass m_NameIndices keys
    public List<string> WatchlistHits { get; set; } = new();
}

public sealed class DumpSubShaderRecord
{
    public int Index { get; set; }
    public int Lod { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public List<DumpPassRecord> Passes { get; set; } = new();
}

public sealed class DumpShaderRecord
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";        // "file" or "bundle:CAB"
    public long PathId { get; set; }
    public int SubShaderCount { get; set; }
    public List<DumpSubShaderRecord> SubShaders { get; set; } = new();
    public bool NamesFromBlobFallback { get; set; }
    public int ReferencedNamesTotal { get; set; }
    public List<string> ReferencedNames { get; set; } = new();  // union: passes + props + keywords (capped)
    public List<string> WatchlistHits { get; set; } = new();
}

// ---------------------------------------------------------------------------
// engine
// ---------------------------------------------------------------------------

public static class Engine
{
    private enum Mode { Scan, Verify, Patch }

    public static string[] TargetShaders =
    {
        "VFX/ParticleMasterUnlitAdd_Shd",
        "SimpleParticleAlphaDFade",
        "VFX/GPU_Bits_Shd",
        "VFX/HexWaypointPath_Shd",
        "Amp_Basic_Unseen",
        "VFX/WingFlap_Shd",
        "OmniDecal_Shd",
    };

    private const float ZTestAlways = 8f;
    private const float ZTestLEqual = 4f;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------------- public entry points ----------------

    public static int RunScanOrVerify(string gameData, string? manifestOut, bool verifyMode)
    {
        var mode = verifyMode ? Mode.Verify : Mode.Scan;
        var manifest = Process(gameData, mode, backupDir: null);
        manifest.CatalogCrc = CatalogCrc.Inspect(gameData);

        PrintTable(manifest);
        PrintCatalogSummary(manifest.CatalogCrc);

        manifestOut ??= Path.Combine(Environment.CurrentDirectory,
            verifyMode ? "verify-manifest.json" : "scan-manifest.json");
        WriteManifest(manifest, manifestOut);

        int alwaysLeft = manifest.Files.Sum(f => f.Shaders.Sum(s => s.Passes.Count(p => p.ZTestAfter == ZTestAlways)));
        var missing = TargetShaders.Except(
            manifest.Files.SelectMany(f => f.Shaders).Select(s => s.Name)).ToList();
        foreach (var m in missing)
            Warn(manifest, $"target shader NOT FOUND anywhere: {m}");

        if (verifyMode)
        {
            if (alwaysLeft == 0)
            {
                Console.WriteLine($"\nVERIFY OK: no ZTest Always(8) passes remain in the {TargetShaders.Length} target shaders.");
                return 0;
            }
            Console.WriteLine($"\nVERIFY: {alwaysLeft} pass(es) still have ZTest Always(8) — game is NOT (fully) patched.");
            return 2;
        }

        Console.WriteLine($"\nSCAN: {alwaysLeft} pass(es) with ZTest Always(8) found across " +
                          $"{manifest.Files.Count(f => f.Shaders.Count > 0)} file(s).");
        foreach (var f in manifest.Files)
            foreach (var s in f.Shaders.Where(s => s.NoAlwaysPass))
                Console.WriteLine($"  WARNING: '{s.Name}' in {f.RelativePath} has NO ZTest Always pass — " +
                                  "bleed must come from something else; re-analysis needed (tool will not touch it).");
        return 0;
    }

    public static int RunPatch(string gameData, string backupDir, string? manifestOut)
    {
        var manifest = Process(gameData, Mode.Patch, backupDir);
        manifest.CatalogCrc = CatalogCrc.Inspect(gameData);

        // CRC safety: if any bundle we modified has a nonzero CRC in the
        // Addressables catalog, zero it (length-preserving edit) or the game
        // would refuse to load the patched bundle. In the shipped catalog all
        // 3255 entries carry m_Crc:0, so this is a no-op safety net.
        var patchedBundles = manifest.Files
            .Where(f => f.Modified && f.Container == "bundle")
            .Select(f => Path.GetFileNameWithoutExtension(f.RelativePath))
            .ToList();
        if (manifest.CatalogCrc is { NonZeroCrcCount: > 0 })
            CatalogCrc.ZeroCrcs(gameData, backupDir, patchedBundles, manifest);

        PrintTable(manifest);
        PrintCatalogSummary(manifest.CatalogCrc);

        manifestOut ??= Path.Combine(
            Path.GetDirectoryName(backupDir.TrimEnd(Path.DirectorySeparatorChar, '/')) ?? backupDir,
            "patch-manifest.json");
        WriteManifest(manifest, manifestOut);

        int patched = manifest.Files.Count(f => f.Modified);
        int already = manifest.Files.Count(f => !f.Modified && f.Shaders.Count > 0
                                                && f.Shaders.All(s => s.Passes.All(p => p.ZTestAfter != ZTestAlways)));
        int backedUp = manifest.Files.Count(f => f.BackedUpThisRun);
        Console.WriteLine($"\nPATCH SUMMARY: {patched} file(s) patched, {already} already patched/clean, " +
                          $"{backedUp} newly backed up (backups: {backupDir}).");
        foreach (var f in manifest.Files)
            foreach (var s in f.Shaders.Where(s => s.NoAlwaysPass && !f.Modified))
                Console.WriteLine($"  note: '{s.Name}' in {f.RelativePath} had no Always pass (already patched, or needs re-analysis).");
        return 0;
    }

    public static int RunRestore(string gameData, string backupDir)
    {
        if (!Directory.Exists(backupDir))
        {
            Console.Error.WriteLine($"Backup dir not found: {backupDir}");
            return 1;
        }
        int restored = 0;
        foreach (var src in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(backupDir, src);
            string dst = Path.Combine(gameData, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
            Console.WriteLine($"  restored {rel}");
            restored++;
        }
        Console.WriteLine($"RESTORE: {restored} file(s) copied back into {gameData}.");
        if (restored == 0)
            Console.WriteLine("  (backup dir was empty — nothing had been patched.)");
        return 0;
    }

    /// <summary>
    /// Globals we specifically look for in each shader's referenced-name set.
    /// Matched case-insensitively as substrings (so e.g. "SoftParticle" hits
    /// the SOFTPARTICLES_ON keyword and "_DepthFade" hits _DepthFade_Distance).
    /// </summary>
    public static readonly string[] GlobalsWatchlist =
    {
        "_CameraDepthTexture",
        "_CameraDepthNormalsTexture",
        "unity_GUIZTestMode",
        "ToggleWallFade",
        "_ToggleWallFadeLocal",
        "SoftParticle",
        "_InvFade",
        "_FadeDistance",
        "_DepthFade",
    };

    /// <summary>Cap for the referenced-name list stored per shader in the dump JSON.</summary>
    private const int DumpNameCap = 400;

    /// <summary>
    /// Diagnostic: print + JSON-dump raw serialized state of every matched shader
    /// (all subshaders with LOD/tags, all passes with tags/state) and the set of
    /// global names its programs reference (per-pass m_NameIndices, shader props,
    /// keyword names; LZ4 blob strings-scan as fallback), with watchlist matches.
    /// </summary>
    public static int RunDump(string gameData, string? manifestOut)
    {
        var am = new AssetsManager();
        am.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        string loaded = "";
        void EnsureDb(string v) { if (loaded != v) { am.LoadClassDatabaseFromPackage(v); loaded = v; } }

        var records = new List<DumpShaderRecord>();

        void DumpFile(AssetsFileInstance inst, string label)
        {
            EnsureDb(inst.file.Metadata.UnityVersion);
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.Shader))
            {
                AssetTypeValueField bf;
                try { bf = am.GetBaseField(inst, info); } catch { continue; }
                var pf = bf["m_ParsedForm"];
                if (pf.IsDummy || !TargetShaders.Contains(pf["m_Name"].AsString)) continue;
                records.Add(DumpOneShader(bf, pf, label, info.PathId));
            }
        }

        // dump-only extra scope: Unity built-in shaders (UI/Default etc.) live in
        // Resources/unity_builtin_extra + "unity default resources" — the
        // scan/patch enumeration deliberately excludes them, but for diagnostics
        // we want to resolve built-ins too.
        var builtinFiles = new[]
        {
            Path.Combine(gameData, "Resources", "unity_builtin_extra"),
            Path.Combine(gameData, "Resources", "unity default resources"),
        }.Where(File.Exists);

        foreach (var path in EnumerateSerializedFiles(gameData).Concat(builtinFiles))
        {
            var inst = am.LoadAssetsFile(path, false);
            DumpFile(inst, Path.GetFileName(path));
            am.UnloadAssetsFile(inst);
        }
        foreach (var path in EnumerateBundles(gameData))
        {
            var bun = am.LoadBundleFile(path, true);
            var dirInfos = bun.file.BlockAndDirInfo.DirectoryInfos;
            for (int i = 0; i < dirInfos.Count; i++)
            {
                if (IsResourceEntry(dirInfos[i].Name) || !bun.file.IsAssetsFile(i)) continue;
                try
                {
                    var afInst = am.LoadAssetsFileFromBundle(bun, i, false);
                    DumpFile(afInst, $"{Path.GetFileName(path)}:{dirInfos[i].Name}");
                }
                catch { /* diagnostic only */ }
            }
            am.UnloadBundleFile(bun);
            am.UnloadAllAssetsFiles(true);
        }

        var missing = TargetShaders.Except(records.Select(r => r.Name)).ToList();
        foreach (var m in missing)
            Console.WriteLine($"\nNOT FOUND anywhere in game files: '{m}'");

        manifestOut ??= Path.Combine(Environment.CurrentDirectory, "dump-manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestOut))!);
        File.WriteAllText(manifestOut, JsonSerializer.Serialize(new
        {
            Tool = "ShaderOcclusionPatcher",
            Version = Cli.ToolVersion,
            Mode = "dump",
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'"),
            GameData = gameData,
            Watchlist = GlobalsWatchlist,
            NotFound = missing,
            Shaders = records,
        }, JsonOpts));
        Console.WriteLine($"\nDump JSON written: {manifestOut}");
        return 0;
    }

    private static DumpShaderRecord DumpOneShader(AssetTypeValueField bf, AssetTypeValueField pf,
        string label, long pathId)
    {
        string name = pf["m_Name"].AsString;
        var rec = new DumpShaderRecord { Name = name, File = label, PathId = pathId };
        var shaderNames = new SortedSet<string>(StringComparer.Ordinal);

        Console.WriteLine($"\n===== {name}  ({label}, pathId={pathId}) =====");
        var propsArr = pf["m_PropInfo"]["m_Props"]["Array"];
        if (!propsArr.IsDummy)
        {
            foreach (var p in propsArr) shaderNames.Add(p["m_Name"].AsString);
            Console.WriteLine("  props: " + string.Join(", ",
                propsArr.Select(p => $"{p["m_Name"].AsString}(def={p["m_DefValue[0]"].AsFloat})")));
        }
        // shader-wide keyword names (Unity 2021.2+ parsed form; dummy on older layouts)
        var kwArr = pf["m_KeywordNames"]["Array"];
        if (!kwArr.IsDummy)
            foreach (var k in kwArr) shaderNames.Add(k.AsString);

        var subs = pf["m_SubShaders"]["Array"];
        rec.SubShaderCount = subs.Children.Count;
        for (int si = 0; si < subs.Children.Count; si++)
        {
            var sub = subs.Children[si];
            var subRec = new DumpSubShaderRecord
            {
                Index = si,
                Lod = sub["m_LOD"].IsDummy ? 0 : sub["m_LOD"].AsInt,
                Tags = ReadTagsDict(sub["m_Tags"]),
            };
            Console.WriteLine($"  subshader {si} LOD={subRec.Lod} tags=[{DumpTags(sub["m_Tags"])}]");
            var passes = sub["m_Passes"]["Array"];
            for (int pi = 0; pi < passes.Children.Count; pi++)
            {
                var pass = passes.Children[pi];
                var st = pass["m_State"];
                var passNames = new SortedSet<string>(StringComparer.Ordinal);
                var ni = pass["m_NameIndices"]["Array"];
                if (!ni.IsDummy)
                    foreach (var pair in ni) passNames.Add(pair["first"].AsString);

                var pRec = new DumpPassRecord
                {
                    Index = pi,
                    Type = pass["m_Type"].IsDummy ? 0 : pass["m_Type"].AsInt,
                    Name = st.IsDummy ? "" : st["m_Name"].AsString,
                    Tags = st.IsDummy ? new() : ReadTagsDict(st["m_Tags"]),
                };
                Console.WriteLine($"    pass {pi} type={pRec.Type} name='{pRec.Name}' tags=[{DumpTags(st["m_Tags"])}]");
                if (!st.IsDummy)
                {
                    foreach (var fld in new[] { "zClip", "zTest", "zWrite", "culling", "offsetFactor", "offsetUnits", "alphaToMask" })
                    {
                        var f = st[fld];
                        if (f.IsDummy) continue;
                        float val = f["val"].AsFloat;
                        string prop = f["name"].IsDummy ? "" : f["name"].AsString;
                        // a state value driven by a property (e.g. zTest name
                        // 'unity_GUIZTestMode') is a referenced global too
                        if (prop.Length > 0 && prop != "<noninit>")
                            passNames.Add(prop);
                        switch (fld)
                        {
                            case "zTest": pRec.ZTest = val; pRec.ZTestProp = prop; break;
                            case "zWrite": pRec.ZWrite = val; pRec.ZWriteProp = prop; break;
                        }
                        Console.WriteLine($"      {fld}: val={val} name='{prop}'");
                    }
                    var b = st["rtBlend0"];
                    if (!b.IsDummy)
                    {
                        pRec.SrcBlend = b["srcBlend"]["val"].AsFloat;
                        pRec.DstBlend = b["destBlend"]["val"].AsFloat;
                        Console.WriteLine($"      blend0: src={pRec.SrcBlend} dst={pRec.DstBlend}");
                    }
                    pRec.Queue = ReadQueueTag(st["m_Tags"]);
                }
                if (pRec.Queue.Length == 0) pRec.Queue = ReadQueueTag(sub["m_Tags"]);
                shaderNames.UnionWith(passNames);
                pRec.ReferencedNames = passNames.ToList();
                pRec.WatchlistHits = MatchWatchlist(passNames);
                Console.WriteLine($"      refs ({passNames.Count}): {string.Join(", ", passNames)}");
                subRec.Passes.Add(pRec);
            }
            rec.SubShaders.Add(subRec);
        }

        // fallback: no names in the parsed form at all -> strings-scan the LZ4 blob
        if (shaderNames.Count == 0)
        {
            rec.NamesFromBlobFallback = true;
            shaderNames.UnionWith(ExtractBlobIdentifiers(bf));
            Console.WriteLine($"  (no parsed-form names; blob strings-scan found {shaderNames.Count} identifiers)");
        }

        rec.ReferencedNames = shaderNames.Take(DumpNameCap).ToList();
        rec.ReferencedNamesTotal = shaderNames.Count;
        rec.WatchlistHits = MatchWatchlist(shaderNames);
        Console.WriteLine(rec.WatchlistHits.Count == 0
            ? "  WATCHLIST: (no matches)"
            : $"  WATCHLIST: {string.Join(", ", rec.WatchlistHits)}");
        return rec;
    }

    private static List<string> MatchWatchlist(IEnumerable<string> names) =>
        names.Where(n => GlobalsWatchlist.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)))
             .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static Dictionary<string, string> ReadTagsDict(AssetTypeValueField tagMap)
    {
        var d = new Dictionary<string, string>();
        if (tagMap.IsDummy) return d;
        var tags = tagMap["tags"]["Array"];
        if (tags.IsDummy) return d;
        foreach (var p in tags) d[p["first"].AsString] = p["second"].AsString;
        return d;
    }

    /// <summary>
    /// Last-resort name source: decompress the shader's LZ4 program blob segments
    /// and scan for ASCII identifiers. Only used when the parsed form exposes no
    /// names (older serialization layouts).
    /// </summary>
    private static SortedSet<string> ExtractBlobIdentifiers(AssetTypeValueField bf)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            var blobField = bf["compressedBlob"]["Array"];
            var offsets = bf["offsets"]["Array"];
            var compLens = bf["compressedLengths"]["Array"];
            var decompLens = bf["decompressedLengths"]["Array"];
            if (blobField.IsDummy || offsets.IsDummy || compLens.IsDummy || decompLens.IsDummy)
                return found;
            byte[] blob = blobField.AsByteArray;

            for (int i = 0; i < offsets.Children.Count; i++)          // per platform
            {
                var offs = offsets.Children[i]["Array"];
                var cls = compLens.Children[i]["Array"];
                var dls = decompLens.Children[i]["Array"];
                for (int j = 0; j < offs.Children.Count; j++)          // per segment
                {
                    long off = offs.Children[j].AsLong;
                    int clen = (int)cls.Children[j].AsLong;
                    int dlen = (int)dls.Children[j].AsLong;
                    if (off < 0 || clen <= 0 || dlen <= 0 || off + clen > blob.Length) continue;
                    byte[] decompressed;
                    if (clen == dlen)
                    {
                        decompressed = new byte[dlen];
                        Array.Copy(blob, off, decompressed, 0, dlen);
                    }
                    else
                    {
                        try
                        {
                            using var ms = new MemoryStream(blob, (int)off, clen);
                            using var lz4 = new AssetsTools.NET.Extra.Decompressors.LZ4.Lz4DecoderStream(ms);
                            decompressed = new byte[dlen];
                            int read = 0, r;
                            while (read < dlen && (r = lz4.Read(decompressed, read, dlen - read)) > 0)
                                read += r;
                        }
                        catch { continue; }
                    }
                    ScanIdentifiers(decompressed, found);
                }
            }
        }
        catch { /* diagnostic only */ }
        return found;
    }

    private static void ScanIdentifiers(byte[] data, SortedSet<string> sink)
    {
        int start = -1;
        for (int i = 0; i <= data.Length; i++)
        {
            byte c = i < data.Length ? data[i] : (byte)0;
            bool idChar = c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                          || (start >= 0 && c >= '0' && c <= '9');
            if (idChar)
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                if (i - start >= 3 && i - start <= 64)
                    sink.Add(System.Text.Encoding.ASCII.GetString(data, start, i - start));
                start = -1;
            }
        }
    }

    private static string DumpTags(AssetTypeValueField tagMap)
    {
        if (tagMap.IsDummy) return "<dummy>";
        var tags = tagMap["tags"]["Array"];
        if (tags.IsDummy) return "<no tags array>";
        return string.Join(", ", tags.Select(p => $"{p["first"].AsString}={p["second"].AsString}"));
    }

    // ---------------- core processing ----------------

    private static Manifest Process(string gameData, Mode mode, string? backupDir)
    {
        var manifest = new Manifest
        {
            Mode = mode.ToString().ToLowerInvariant(),
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'"),
            GameData = gameData,
            BackupDir = backupDir,
        };

        var am = new AssetsManager();
        string tpk = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
        if (!File.Exists(tpk))
            throw new FileNotFoundException($"classdata.tpk not found next to the tool binary: {tpk}");
        am.LoadClassPackage(tpk);
        string loadedDbVersion = "";

        void EnsureClassDb(string unityVersion)
        {
            if (loadedDbVersion == unityVersion) return;
            am.LoadClassDatabaseFromPackage(unityVersion);
            loadedDbVersion = unityVersion;
        }

        // 1. loose serialized files in the data root
        foreach (var path in EnumerateSerializedFiles(gameData))
            ProcessSerializedFile(am, EnsureClassDb, gameData, path, mode, backupDir, manifest);

        // 2. Addressables bundles
        var bundles = EnumerateBundles(gameData).ToList();
        int done = 0;
        foreach (var path in bundles)
        {
            ProcessBundle(am, EnsureClassDb, gameData, path, mode, backupDir, manifest);
            if (++done % 250 == 0)
                Console.Error.WriteLine($"  ... {done}/{bundles.Count} bundles scanned");
        }
        if (bundles.Count > 0)
            Console.Error.WriteLine($"  ... {done}/{bundles.Count} bundles scanned");

        return manifest;
    }

    private static IEnumerable<string> EnumerateSerializedFiles(string gameData)
    {
        foreach (var f in Directory.EnumerateFiles(gameData))
        {
            string name = Path.GetFileName(f);
            bool match =
                name.Equals("globalgamemanagers.assets", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("globalgamemanagers", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("resources.assets", StringComparison.OrdinalIgnoreCase) ||
                (name.StartsWith("sharedassets", StringComparison.OrdinalIgnoreCase) &&
                 name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase)) ||
                System.Text.RegularExpressions.Regex.IsMatch(name, @"^level\d+$");
            if (match) yield return f;
        }
    }

    private static IEnumerable<string> EnumerateBundles(string gameData)
    {
        string aa = Path.Combine(gameData, "StreamingAssets", "aa");
        if (!Directory.Exists(aa)) yield break;
        foreach (var f in Directory.EnumerateFiles(aa, "*.bundle", SearchOption.AllDirectories)
                                   .OrderBy(p => p, StringComparer.Ordinal))
            yield return f;
    }

    private static void ProcessSerializedFile(AssetsManager am, Action<string> ensureClassDb,
        string gameData, string path, Mode mode, string? backupDir, Manifest manifest)
    {
        string rel = Path.GetRelativePath(gameData, path);
        AssetsFileInstance? inst = null;
        try
        {
            inst = am.LoadAssetsFile(path, false);
            ensureClassDb(inst.file.Metadata.UnityVersion);

            var shaders = InspectAndMaybePatch(am, inst, innerFile: "", mode, out bool anyChanged);
            if (shaders.Count == 0) return;

            var rec = new FileRecord { RelativePath = rel, Container = "serialized", Shaders = shaders };
            manifest.Files.Add(rec);

            if (mode == Mode.Patch && anyChanged)
            {
                rec.HashBefore = Sha256(path);
                Backup(gameData, path, backupDir!, rec);

                string tmp = path + ".sopatch-tmp";
                using (var writer = new AssetsFileWriter(tmp))
                    inst.file.Write(writer);
                am.UnloadAssetsFile(inst);
                inst = null;
                File.Move(tmp, path, overwrite: true);
                rec.HashAfter = Sha256(path);
                rec.Modified = true;
                Console.WriteLine($"  patched {rel}");
            }
        }
        catch (Exception ex)
        {
            if (mode == Mode.Patch)
                throw new InvalidOperationException($"Failed while patching '{rel}': {ex.Message}", ex);
            Warn(manifest, $"failed to read '{rel}': {ex.Message}");
        }
        finally
        {
            if (inst is not null) am.UnloadAssetsFile(inst);
        }
    }

    private static void ProcessBundle(AssetsManager am, Action<string> ensureClassDb,
        string gameData, string path, Mode mode, string? backupDir, Manifest manifest)
    {
        string rel = Path.GetRelativePath(gameData, path);
        BundleFileInstance? bun = null;
        bool unloaded = false;
        try
        {
            // capture the original compression before AssetsManager unpacks it
            AssetBundleCompressionType comp = DetectCompression(path);

            bun = am.LoadBundleFile(path, unpackIfPacked: true);
            var dirInfos = bun.file.BlockAndDirInfo.DirectoryInfos;

            var allShaders = new List<ShaderRecord>();
            bool anyChanged = false;
            for (int i = 0; i < dirInfos.Count; i++)
            {
                if (IsResourceEntry(dirInfos[i].Name) || !bun.file.IsAssetsFile(i)) continue;
                AssetsFileInstance afInst;
                try
                {
                    afInst = am.LoadAssetsFileFromBundle(bun, i, false);
                }
                catch (Exception ex)
                {
                    // e.g. a raw data entry misdetected as a serialized file — the
                    // entry is left untouched, so scanning/patching stays safe.
                    Warn(manifest, $"bundle '{rel}': inner entry '{dirInfos[i].Name}' not readable as a serialized file ({ex.Message}) — skipped.");
                    continue;
                }
                ensureClassDb(afInst.file.Metadata.UnityVersion);

                var shaders = InspectAndMaybePatch(am, afInst, dirInfos[i].Name, mode, out bool changed);
                allShaders.AddRange(shaders);
                if (changed && mode == Mode.Patch)
                {
                    dirInfos[i].SetNewData(afInst.file);
                    anyChanged = true;
                }
            }
            if (allShaders.Count == 0) return;

            var rec = new FileRecord
            {
                RelativePath = rel,
                Container = "bundle",
                Compression = comp.ToString(),
                Shaders = allShaders,
            };
            manifest.Files.Add(rec);

            if (mode == Mode.Patch && anyChanged)
            {
                rec.HashBefore = Sha256(path);
                Backup(gameData, path, backupDir!, rec);

                string tmpUnpacked = path + ".sopatch-unpacked";
                string tmpFinal = path + ".sopatch-tmp";
                try
                {
                    using (var writer = new AssetsFileWriter(tmpUnpacked))
                        bun.file.Write(writer);
                    am.UnloadBundleFile(bun);
                    bun = null;
                    unloaded = true;

                    if (comp == AssetBundleCompressionType.None)
                    {
                        File.Move(tmpUnpacked, tmpFinal, overwrite: true);
                    }
                    else
                    {
                        var repack = new AssetBundleFile();
                        var reader = new AssetsFileReader(File.OpenRead(tmpUnpacked));
                        try
                        {
                            repack.Read(reader);
                            using var writer = new AssetsFileWriter(tmpFinal);
                            repack.Pack(writer, comp);
                        }
                        finally
                        {
                            repack.Close();
                        }
                    }
                    File.Move(tmpFinal, path, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tmpUnpacked)) File.Delete(tmpUnpacked);
                    if (File.Exists(tmpFinal)) File.Delete(tmpFinal);
                }
                rec.HashAfter = Sha256(path);
                rec.Modified = true;
                Console.WriteLine($"  patched {rel} ({comp} repack)");
            }
        }
        catch (Exception ex)
        {
            if (mode == Mode.Patch)
                throw new InvalidOperationException($"Failed while patching bundle '{rel}': {ex.Message}", ex);
            Warn(manifest, $"failed to read bundle '{rel}': {ex.Message}");
        }
        finally
        {
            if (bun is not null && !unloaded) am.UnloadBundleFile(bun);
            am.UnloadAllAssetsFiles(true);
        }
    }

    /// <summary>
    /// Find target shaders in one serialized file; in Patch mode flip zTest 8->4
    /// and stage the new asset bytes. Returns one record per matched shader.
    /// </summary>
    private static List<ShaderRecord> InspectAndMaybePatch(AssetsManager am, AssetsFileInstance inst,
        string innerFile, Mode mode, out bool anyChanged)
    {
        anyChanged = false;
        var result = new List<ShaderRecord>();

        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.Shader))
        {
            AssetTypeValueField bf;
            try { bf = am.GetBaseField(inst, info); }
            catch { continue; } // unreadable shader asset — not one of ours (ours parse fine)

            var parsedForm = bf["m_ParsedForm"];
            if (parsedForm.IsDummy) continue;
            string name = parsedForm["m_Name"].AsString;
            if (!TargetShaders.Contains(name)) continue;

            var rec = new ShaderRecord { Name = name, PathId = info.PathId, InnerFile = innerFile };
            bool shaderChanged = false;

            var subShaders = parsedForm["m_SubShaders"]["Array"];
            for (int si = 0; si < subShaders.Children.Count; si++)
            {
                var sub = subShaders.Children[si];
                string subQueue = ReadQueueTag(sub["m_Tags"]);
                var passes = sub["m_Passes"]["Array"];
                for (int pi = 0; pi < passes.Children.Count; pi++)
                {
                    var pass = passes.Children[pi];
                    var state = pass["m_State"];
                    if (state.IsDummy) continue;

                    var zTestVal = state["zTest"]["val"];
                    var zWriteVal = state["zWrite"]["val"];
                    string passQueue = ReadQueueTag(state["m_Tags"]);

                    var pr = new PassRecord
                    {
                        SubShader = si,
                        Pass = pi,
                        PassName = state["m_Name"].AsString,
                        PassType = pass["m_Type"].IsDummy ? 0 : pass["m_Type"].AsInt,
                        ZTestBefore = zTestVal.AsFloat,
                        ZTestAfter = zTestVal.AsFloat,
                        ZTestProp = state["zTest"]["name"].IsDummy ? "" : state["zTest"]["name"].AsString,
                        ZWrite = zWriteVal.AsFloat,
                        ZWriteProp = state["zWrite"]["name"].IsDummy ? "" : state["zWrite"]["name"].AsString,
                        Queue = passQueue.Length > 0 ? passQueue : subQueue,
                    };

                    // PATCH RULE: only 8 (Always) -> 4 (LEqual). 0 = unset
                    // (platform default LEqual) and everything else stays.
                    if (mode == Mode.Patch && pr.ZTestBefore == ZTestAlways)
                    {
                        zTestVal.AsFloat = ZTestLEqual;
                        pr.ZTestAfter = ZTestLEqual;
                        shaderChanged = true;
                    }
                    rec.Passes.Add(pr);
                }
            }

            rec.NoAlwaysPass = !rec.Passes.Any(p => p.ZTestBefore == ZTestAlways);
            rec.Patched = shaderChanged;
            if (shaderChanged)
            {
                info.SetNewData(bf);
                anyChanged = true;
            }
            result.Add(rec);
        }
        return result;
    }

    private static string ReadQueueTag(AssetTypeValueField tagMap)
    {
        if (tagMap.IsDummy) return "";
        var tags = tagMap["tags"]["Array"];
        if (tags.IsDummy) return "";
        foreach (var pair in tags)
            if (string.Equals(pair["first"].AsString, "QUEUE", StringComparison.OrdinalIgnoreCase))
                return pair["second"].AsString;
        return "";
    }

    private static bool IsResourceEntry(string name) =>
        name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase);

    private static AssetBundleCompressionType DetectCompression(string path)
    {
        var bf = new AssetBundleFile();
        var reader = new AssetsFileReader(File.OpenRead(path));
        try
        {
            bf.Read(reader);
            return bf.GetCompressionType();
        }
        finally
        {
            bf.Close();
        }
    }

    /// <summary>
    /// Copy the pristine original into the backup dir (relative layout preserved).
    /// NEVER overwrites an existing backup — the backup always holds the
    /// original, even across repeated installs/patch runs.
    /// </summary>
    private static void Backup(string gameData, string filePath, string backupDir, FileRecord rec)
    {
        string rel = Path.GetRelativePath(gameData, filePath);
        string dst = Path.Combine(backupDir, rel);
        if (File.Exists(dst))
        {
            rec.BackupAlreadyExisted = true;
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        string tmp = dst + ".tmp";
        File.Copy(filePath, tmp, overwrite: true);
        File.Move(tmp, dst, overwrite: true);
        rec.BackedUpThisRun = true;
        Console.WriteLine($"  backed up {rel}");
    }

    // ---------------- output ----------------

    private static void PrintTable(Manifest manifest)
    {
        Console.WriteLine();
        Console.WriteLine($"{"File",-58} {"Shader",-34} {"SS/P",-5} {"PassName",-14} {"zTest",-12} {"zTestProp",-14} {"zWrite",-7} Queue");
        Console.WriteLine(new string('-', 160));
        foreach (var f in manifest.Files)
        {
            foreach (var s in f.Shaders)
            {
                foreach (var p in s.Passes)
                {
                    string file = f.RelativePath.Length <= 58 ? f.RelativePath : "..." + f.RelativePath[^55..];
                    string zTest = p.ZTestBefore == p.ZTestAfter
                        ? ZTestName(p.ZTestBefore)
                        : $"{ZTestName(p.ZTestBefore)}->{(int)p.ZTestAfter}";
                    Console.WriteLine($"{file,-58} {s.Name,-34} {$"{p.SubShader}/{p.Pass}",-5} {Trunc(p.PassName, 14),-14} {zTest,-12} {Trunc(p.ZTestProp, 14),-14} {ZWriteName(p.ZWrite),-7} {p.Queue}");
                }
            }
        }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "~";

    private static string ZTestName(float v) => v switch
    {
        0 => "0=Unset",
        1 => "1=Disabled",
        2 => "2=Never",
        3 => "3=Less",
        4 => "4=LEqual",
        5 => "5=Equal",
        6 => "6=GEqual",
        7 => "7=Greater",
        8 => "8=Always",
        _ => v.ToString("0.##"),
    };

    private static string ZWriteName(float v) => v switch
    {
        0 => "0=Off",
        1 => "1=On",
        _ => v.ToString("0.##"),
    };

    private static void PrintCatalogSummary(CatalogCrcReport? c)
    {
        Console.WriteLine();
        if (c is null)
        {
            Console.WriteLine("Addressables catalog: not found (skipping CRC check).");
            return;
        }
        Console.WriteLine($"Addressables catalog: {c.EntryCount} bundle option entries, " +
                          $"{c.NonZeroCrcCount} with nonzero CRC" +
                          (c.NonZeroCrcCount == 0
                              ? " — CRC verification disabled by the game, no catalog patching needed."
                              : $" — nonzero CRCs will be zeroed for patched bundles. Zeroed this run: {c.ZeroedThisRun}."));
    }

    private static void WriteManifest(Manifest manifest, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOpts));
        Console.WriteLine($"Manifest written: {path}");
    }

    internal static void Warn(Manifest manifest, string msg)
    {
        manifest.Warnings.Add(msg);
        Console.Error.WriteLine($"  WARNING: {msg}");
    }

    internal static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
