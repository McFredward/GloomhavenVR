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
// engine
// ---------------------------------------------------------------------------

public static class Engine
{
    private enum Mode { Scan, Verify, Patch }

    public static readonly string[] TargetShaders =
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

    /// <summary>Diagnostic: print raw serialized state of every matched shader (tags, stencil, queue, props).</summary>
    public static int RunDump(string gameData)
    {
        var am = new AssetsManager();
        am.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        string loaded = "";
        void EnsureDb(string v) { if (loaded != v) { am.LoadClassDatabaseFromPackage(v); loaded = v; } }

        void DumpFile(AssetsFileInstance inst, string label)
        {
            EnsureDb(inst.file.Metadata.UnityVersion);
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.Shader))
            {
                AssetTypeValueField bf;
                try { bf = am.GetBaseField(inst, info); } catch { continue; }
                var pf = bf["m_ParsedForm"];
                if (pf.IsDummy || !TargetShaders.Contains(pf["m_Name"].AsString)) continue;
                Console.WriteLine($"\n===== {pf["m_Name"].AsString}  ({label}, pathId={info.PathId}) =====");
                Console.WriteLine($"  props: " + string.Join(", ",
                    pf["m_PropInfo"]["m_Props"]["Array"].Select(p =>
                        $"{p["m_Name"].AsString}(def={p["m_DefValue[0]"].AsFloat})")));
                var subs = pf["m_SubShaders"]["Array"];
                for (int si = 0; si < subs.Children.Count; si++)
                {
                    var sub = subs.Children[si];
                    Console.WriteLine($"  subshader {si} LOD={sub["m_LOD"].AsInt} tags=[{DumpTags(sub["m_Tags"])}]");
                    var passes = sub["m_Passes"]["Array"];
                    for (int pi = 0; pi < passes.Children.Count; pi++)
                    {
                        var pass = passes.Children[pi];
                        var st = pass["m_State"];
                        Console.WriteLine($"    pass {pi} type={pass["m_Type"].AsInt} name='{st["m_Name"].AsString}' tags=[{DumpTags(st["m_Tags"])}]");
                        foreach (var fld in new[] { "zClip", "zTest", "zWrite", "culling", "offsetFactor", "offsetUnits", "alphaToMask" })
                        {
                            var f = st[fld];
                            if (f.IsDummy) continue;
                            Console.WriteLine($"      {fld}: val={f["val"].AsFloat} name='{f["name"].AsString}'");
                        }
                        var b = st["rtBlend0"];
                        if (!b.IsDummy)
                            Console.WriteLine($"      blend0: src={b["srcBlend"]["val"].AsFloat} dst={b["destBlend"]["val"].AsFloat}");
                    }
                }
            }
        }

        foreach (var path in EnumerateSerializedFiles(gameData))
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
        return 0;
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
