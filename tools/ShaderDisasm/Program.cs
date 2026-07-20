using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace ShaderDisasm;

// Extract Unity Shader (ClassID 48) parsed-form + compiled DXBC sub-programs.
// Usage: ShaderDisasm <outdir> <shaderName> <file1> [file2 ...]
//   files may be serialized assets (resources.assets) or .bundle files.
internal static class Program
{
    private static readonly string[] GpuProgramType =
    {
        /*0*/ "Unknown","GLLegacy","GLES31AEP","GLES31","GLES3","GLES","GLCore32","GLCore41","GLCore43",
        /*9*/ "DX9VertexSM20","DX9VertexSM30","DX9PixelSM20","DX9PixelSM30","DX10Level9Vertex","DX10Level9Pixel",
        /*15*/"DX11VertexSM40","DX11VertexSM50","DX11PixelSM40","DX11PixelSM50","DX11GeometrySM40","DX11GeometrySM50",
        /*21*/"DX11HullSM50","DX11DomainSM50","MetalVS","MetalFS","SPIRV","ConsoleVS","ConsoleFS","ConsoleHS",
        /*29*/"ConsoleDS","ConsoleGS","RayTracing","PS5NGGC",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: ShaderDisasm <outdir> <shaderName> <file...>");
            return 1;
        }
        string outDir = args[0];
        string wantName = args[1];
        var files = args.Skip(2).ToArray();
        Directory.CreateDirectory(outDir);

        var am = new AssetsManager();
        am.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        string loaded = "";
        void EnsureDb(string v) { if (loaded != v) { am.LoadClassDatabaseFromPackage(v); loaded = v; } }

        var log = new StringBuilder();
        void Both(string s) { Console.WriteLine(s); log.AppendLine(s); }

        foreach (var path in files)
        {
            if (!File.Exists(path)) { Both($"[skip missing] {path}"); continue; }
            bool isBundle = path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase);
            if (isBundle)
            {
                var bun = am.LoadBundleFile(path, true);
                var dirs = bun.file.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < dirs.Count; i++)
                {
                    if (!bun.file.IsAssetsFile(i)) continue;
                    AssetsFileInstance inst;
                    try { inst = am.LoadAssetsFileFromBundle(bun, i, false); } catch { continue; }
                    EnsureDb(inst.file.Metadata.UnityVersion);
                    Scan(am, inst, $"{Path.GetFileName(path)}:{dirs[i].Name}", wantName, outDir, Both);
                }
                am.UnloadBundleFile(bun);
                am.UnloadAllAssetsFiles(true);
            }
            else
            {
                var inst = am.LoadAssetsFile(path, false);
                EnsureDb(inst.file.Metadata.UnityVersion);
                Scan(am, inst, Path.GetFileName(path), wantName, outDir, Both);
                am.UnloadAssetsFile(inst);
            }
        }

        File.WriteAllText(Path.Combine(outDir, "extract-report.txt"), log.ToString());
        Console.WriteLine($"\n[report] {Path.Combine(outDir, "extract-report.txt")}");
        return 0;
    }

    private static void Scan(AssetsManager am, AssetsFileInstance inst, string label,
        string wantName, string outDir, Action<string> Both)
    {
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.Shader))
        {
            AssetTypeValueField bf;
            try { bf = am.GetBaseField(inst, info); } catch { continue; }
            var pf = bf["m_ParsedForm"];
            if (pf.IsDummy) continue;
            string name = pf["m_Name"].AsString;
            if (name != wantName) continue;

            Both($"\n################ SHADER '{name}'  ({label}, pathId={info.PathId}) ################");
            DumpParsedForm(pf, Both);
            ExtractBlobs(bf, name, outDir, Both);
            return; // first match is enough per invocation
        }
    }

    private static void DumpParsedForm(AssetTypeValueField pf, Action<string> Both)
    {
        // properties
        var props = pf["m_PropInfo"]["m_Props"]["Array"];
        Both("\n-- Properties --");
        if (!props.IsDummy)
            foreach (var p in props)
            {
                string pname = p["m_Name"].AsString;
                string desc = p["m_Description"].IsDummy ? "" : p["m_Description"].AsString;
                int type = p["m_Type"].IsDummy ? -1 : p["m_Type"].AsInt;
                Both($"   {pname,-32} type={type} \"{desc}\"");
            }

        // shader-level keyword names table (2021.2+)
        var kwNames = new List<string>();
        var kwArr = pf["m_KeywordNames"]["Array"];
        if (!kwArr.IsDummy) foreach (var k in kwArr) kwNames.Add(k.AsString);
        Both($"\n-- Shader keyword name table ({kwNames.Count}) --");
        for (int i = 0; i < kwNames.Count; i++) Both($"   [{i}] {kwNames[i]}");

        var subs = pf["m_SubShaders"]["Array"];
        for (int si = 0; si < subs.Children.Count; si++)
        {
            var sub = subs.Children[si];
            Both($"\n== SubShader {si}  tags=[{Tags(sub["m_Tags"])}] ==");
            var passes = sub["m_Passes"]["Array"];
            for (int pi = 0; pi < passes.Children.Count; pi++)
            {
                var pass = passes.Children[pi];
                var st = pass["m_State"];
                string pname = st.IsDummy ? "" : st["m_Name"].AsString;
                string ztest = st.IsDummy ? "?" : st["zTest"]["val"].AsFloat.ToString();
                string ztestProp = st.IsDummy || st["zTest"]["name"].IsDummy ? "" : st["zTest"]["name"].AsString;
                string zwrite = st.IsDummy ? "?" : st["zWrite"]["val"].AsFloat.ToString();
                string tags = st.IsDummy ? "" : Tags(st["m_Tags"]);
                Both($"\n  -- Pass {pi} name='{pname}' zTest={ztest} zTestProp='{ztestProp}' zWrite={zwrite} tags=[{tags}] --");

                // NameIndices (name -> integer index used by the compiled blob's binding tables)
                var ni = pass["m_NameIndices"]["Array"];
                if (!ni.IsDummy && ni.Children.Count > 0)
                    Both("     nameIndices: " + string.Join(", ",
                        ni.Select(x => $"{x["first"].AsString}={x["second"].AsInt}")));

                foreach (var stage in new[] { "progVertex", "progFragment", "progGeometry", "progHull", "progDomain" })
                {
                    var prog = pass[stage];
                    if (prog.IsDummy) continue;
                    var sp = prog["m_SubPrograms"]["Array"];
                    if (sp.IsDummy || sp.Children.Count == 0) continue;
                    Both($"     [{stage}] {sp.Children.Count} sub-program(s):");
                    for (int k = 0; k < sp.Children.Count; k++)
                        DumpSubProgram(sp.Children[k], kwNames, Both);
                }
            }
        }
    }

    private static void DumpSubProgram(AssetTypeValueField sp, List<string> kwNames, Action<string> Both)
    {
        uint blobIndex = sp["m_BlobIndex"].IsDummy ? 0xffffffff : (uint)sp["m_BlobIndex"].AsLong;
        int gpt = sp["m_GpuProgramType"].IsDummy ? -1 : sp["m_GpuProgramType"].AsInt;
        string gptName = gpt >= 0 && gpt < GpuProgramType.Length ? GpuProgramType[gpt] : gpt.ToString();

        var kws = new List<string>();
        void ReadKwField(string field, string tag)
        {
            var f = sp[field];
            if (f.IsDummy) return;
            var a = f["Array"];
            if (a.IsDummy) return;
            foreach (var x in a)
            {
                int idx = x.AsInt;
                kws.Add(idx >= 0 && idx < kwNames.Count ? kwNames[idx] : $"{tag}#{idx}");
            }
        }
        // 2021.x: m_KeywordIndices (u16 array) into shader-level kwNames
        ReadKwField("m_KeywordIndices", "kw");
        // fallback older layouts
        ReadKwField("m_GlobalKeywordIndices", "g");
        ReadKwField("m_LocalKeywordIndices", "l");

        Both($"        blob={blobIndex,-4} gpu={gptName,-16} keywords=[{string.Join(" ", kws)}]");
    }

    private static string Tags(AssetTypeValueField tagMap)
    {
        if (tagMap.IsDummy) return "";
        var tags = tagMap["tags"]["Array"];
        if (tags.IsDummy) return "";
        return string.Join(", ", tags.Select(p => $"{p["first"].AsString}={p["second"].AsString}"));
    }

    // ---- blob extraction: decompress LZ4 segments, carve DXBC blobs ----
    private static void ExtractBlobs(AssetTypeValueField bf, string shaderName, string outDir, Action<string> Both)
    {
        var platformsF = bf["platforms"]["Array"];
        var blobField = bf["compressedBlob"]["Array"];
        var offsets = bf["offsets"]["Array"];
        var compLens = bf["compressedLengths"]["Array"];
        var decompLens = bf["decompressedLengths"]["Array"];
        if (blobField.IsDummy || offsets.IsDummy)
        {
            Both("   [blob] no compressedBlob/offsets present");
            return;
        }
        byte[] blob = blobField.AsByteArray;
        var platforms = new List<int>();
        if (!platformsF.IsDummy) foreach (var p in platformsF) platforms.Add(p.AsInt);
        Both($"\n-- Blob: {blob.Length} compressed bytes, platforms=[{string.Join(",", platforms.Select(p => p >= 0 && p < GpuProgramType.Length ? GpuProgramType[p] : p.ToString()))}] --");

        string safe = shaderName.Replace('/', '_');
        int globalBlobCounter = 0;

        for (int i = 0; i < offsets.Children.Count; i++) // per platform
        {
            var offs = offsets.Children[i]["Array"];
            var cls = compLens.Children[i]["Array"];
            var dls = decompLens.Children[i]["Array"];
            string platName = i < platforms.Count && platforms[i] >= 0 && platforms[i] < GpuProgramType.Length
                ? GpuProgramType[platforms[i]] : $"plat{i}";
            var whole = new MemoryStream();
            for (int j = 0; j < offs.Children.Count; j++) // per segment
            {
                long off = offs.Children[j].AsLong;
                int clen = (int)cls.Children[j].AsLong;
                int dlen = (int)dls.Children[j].AsLong;
                if (off < 0 || clen <= 0 || dlen <= 0 || off + clen > blob.Length) continue;
                byte[] dec = new byte[dlen];
                if (clen == dlen) Array.Copy(blob, off, dec, 0, dlen);
                else
                {
                    using var ms = new MemoryStream(blob, (int)off, clen);
                    using var lz4 = new AssetsTools.NET.Extra.Decompressors.LZ4.Lz4DecoderStream(ms);
                    int read = 0, r;
                    while (read < dlen && (r = lz4.Read(dec, read, dlen - read)) > 0) read += r;
                }
                whole.Write(dec, 0, dec.Length);
            }
            byte[] platData = whole.ToArray();
            File.WriteAllBytes(Path.Combine(outDir, $"{safe}.plat{i}.{platName}.rawblob.bin"), platData);
            Both($"\n   [platform {i} = {platName}] decompressed {platData.Length} bytes (raw dumped)");

            // carve DXBC blobs by magic + internal size
            int pos = 0;
            int idxInPlat = 0;
            while (true)
            {
                int at = IndexOf(platData, DXBC, pos);
                if (at < 0) break;
                // DXBC total size at offset 24 (u32 LE)
                if (at + 32 > platData.Length) break;
                uint total = BitConverter.ToUInt32(platData, at + 24);
                if (total < 32 || at + total > platData.Length)
                {
                    pos = at + 4; continue;
                }
                byte[] dxbc = new byte[total];
                Array.Copy(platData, at, dxbc, 0, (int)total);
                string kind = DxbcProgramKind(dxbc);
                string fn = Path.Combine(outDir, $"{safe}.{platName}.blob{globalBlobCounter:D2}.{kind}.dxbc");
                File.WriteAllBytes(fn, dxbc);
                Both($"      blob#{globalBlobCounter} (platIdx {idxInPlat}) at 0x{at:X} size {total} kind={kind} -> {Path.GetFileName(fn)}");
                globalBlobCounter++; idxInPlat++;
                pos = at + (int)total;
            }
            if (idxInPlat == 0) Both("      (no DXBC magic found — non-DirectX platform?)");
        }
    }

    private static readonly byte[] DXBC = { (byte)'D', (byte)'X', (byte)'B', (byte)'C' };

    private static int IndexOf(byte[] hay, byte[] needle, int start)
    {
        for (int i = start; i <= hay.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++) if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    // Read the SHDR/SHEX chunk version token to classify program type.
    private static string DxbcProgramKind(byte[] dxbc)
    {
        try
        {
            uint chunkCount = BitConverter.ToUInt32(dxbc, 28);
            for (int c = 0; c < chunkCount; c++)
            {
                uint chunkOff = BitConverter.ToUInt32(dxbc, 32 + c * 4);
                if (chunkOff + 8 > dxbc.Length) continue;
                string fourcc = Encoding.ASCII.GetString(dxbc, (int)chunkOff, 4);
                if (fourcc == "SHDR" || fourcc == "SHEX")
                {
                    // chunk data starts at chunkOff+8; first dword = version token
                    uint verTok = BitConverter.ToUInt32(dxbc, (int)chunkOff + 8);
                    uint progType = (verTok >> 16) & 0xffff;
                    return progType switch
                    {
                        0 => "PIXEL",
                        1 => "VERTEX",
                        2 => "GEOMETRY",
                        3 => "HULL",
                        4 => "DOMAIN",
                        5 => "COMPUTE",
                        _ => $"type{progType}",
                    };
                }
            }
        }
        catch { }
        return "UNKNOWN";
    }
}
