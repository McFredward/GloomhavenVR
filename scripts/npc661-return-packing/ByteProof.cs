using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GloomhavenVR.Net;

internal static class Packing661ByteProof
{
    private static int checks;
    private static void Check(bool good, string description)
    { checks++; if (!good) throw new InvalidOperationException(description); }
    static int Main(string[] args)
    {
        if (args.Length < 3 || args.Length > 4) throw new ArgumentException("Expected actual assembly, raw directory, proof directory and optional frozen legacy assembly");
        string assemblyFile = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(args[2]);
        Assembly assembly = Assembly.LoadFile(assemblyFile);
        Type codec = assembly.GetType("GloomhavenVR.Net.TownServices.TownServiceMotionCodec", true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo readCore = codec.GetMethod("TryReadCore", flags)!;
        MethodInfo read = codec.GetMethod("TryRead", flags)!;
        MethodInfo writeRaw = codec.GetMethod("WriteRaw", flags)!;
        MethodInfo writePacked = codec.GetMethod("TryWritePacked", flags)!;
        MethodInfo transformRoots = codec.GetMethod("TryPackReturnRoots", flags)!;
        object? Decode(byte[] bytes, bool raw = false)
        {
            object?[] call = raw ? new object?[] { bytes, bytes.Length, null, true } : new object?[] { bytes, bytes.Length, null };
            bool good = (bool)(raw ? readCore : read).Invoke(null, call)!;
            return good ? call[2] : null;
        }
        byte[] Raw(object packet) => (byte[])writeRaw.Invoke(null, new object[] { packet, 8192, 128 })!;
        byte[] Packed(byte[] raw, byte record = 115, int expectedLength = -1)
        {
            byte[] packed = PresentationCompression.TryCompress(raw, raw.Length, true)!;
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(raw, 0, 21);
            for (int at = 0; at < packed.Length; at += 248)
            {
                int count = Math.Min(248, packed.Length - at);
                writer.Write(record); writer.Write((byte)(count + 7)); writer.Write((byte)1);
                writer.Write((ushort)(expectedLength < 0 ? raw.Length : expectedLength));
                writer.Write((ushort)packed.Length); writer.Write((ushort)at); writer.Write(packed, at, count);
            }
            return stream.ToArray();
        }
        var rows = new List<object>();
        foreach (string file in Directory.GetFiles(args[1], "*.bin").OrderBy(x => x))
        {
            byte[] original = File.ReadAllBytes(file);
            object? packet = Decode(original, true);
            Check(packet != null, "Actual raw fixture decodes with full production guards: " + file);
            Check(Raw(packet!).AsSpan().SequenceEqual(original), "Source raw grammar writes back bit-identically");
            byte[]? encoded = (byte[]?)writePacked.Invoke(null, new[] { packet });
            if (encoded == null) continue;
            Check(encoded.Length <= 864, "Event budget unchanged");
            object? observed = Decode(encoded);
            Check(observed != null && Raw(observed).AsSpan().SequenceEqual(original), "Actual candidate reader restores original97/113 bytes");
            rows.Add(new { file = Path.GetFileName(file), originalSHA256 = Convert.ToHexString(SHA256.HashData(original)),
                raw = original.Length, encoded = encoded.Length, record = encoded[21], readback = "BITIDENTICAL" });
        }
        byte[] sample = File.ReadAllBytes(Path.Combine(args[1], "NativeReturn658capacity-seq1-parts8-raw1521.bin"));
        byte[]? packedSample = (byte[]?)writePacked.Invoke(null, new[] { Decode(sample, true) });
        Check(packedSample != null && packedSample.Length <= 864, "Real eight-part full native root recipe fits unchanged864-byte event");
        byte[] current = packedSample!;
        Check(current[21] == 115, "Eight-part real input uses additive115");
        using (JsonDocument goldens = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1], "goldens.json"))))
        foreach (JsonElement golden in goldens.RootElement.EnumerateArray())
        {
            byte[] literal = Convert.FromBase64String(golden.GetProperty("base64").GetString()!);
            Check(Convert.ToHexString(SHA256.HashData(literal)).ToLowerInvariant() == golden.GetProperty("sha256").GetString(), "Literal golden payload hash");
            byte[] expected = File.ReadAllBytes(Path.Combine(args[1], golden.GetProperty("raw").GetString()!));
            Check(Decode(literal) is object exact && Raw(exact).AsSpan().SequenceEqual(expected), "Literal98/115 reconstructs complete original97/113 bytes");
            if (golden.GetProperty("record").GetInt32() == 115)
                Check(current.AsSpan().SequenceEqual(literal), "Current115 writer matches literal golden byte grammar");
        }
        object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        object CloneEntry(object entry)
        {
            object copy = Activator.CreateInstance(entry.GetType(), true)!;
            foreach (FieldInfo field in entry.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                field.SetValue(copy, field.GetValue(entry));
            return copy;
        }
        byte[] baseRecipe = File.ReadAllBytes(Directory.GetFiles(args[1], "*seq1-parts6-*.bin").Single());
        foreach (int shape in new[] { 70, 71, 156 })
        {
            object recipe = Decode(baseRecipe, true)!;
            var entries = (System.Collections.IList)Field(recipe, "Entries");
            object root = entries.Cast<object>().First(entry => (byte)Field(entry, "Kind") == 1);
            Set(root, "ParentModule", (ushort)17); Set(root, "Binding", 0x10203040u);
            Set(root, "ParentAlpha", .37f); Set(root, "Visible", false); Set(root, "Hand", (byte)4);
            Set(root, "HasCanvasUpdate", shape != 70); Set(root, "HasCanvasFrame", shape == 156);
            if (shape == 156)
            {
                Set(root, "CanvasOnHand", true); Set(root, "CanvasPose", ((float[])Field(root, "Pose")).ToArray());
                Set(root, "CanvasRect", new[] { 130f, 270f, .4f, .5f });
                Set(root, "CanvasSettings", new[] { .75f, 1f, 0f, 1f, .12f });
                Set(root, "CanvasSortingOrder", -13); Set(root, "CanvasSortingLayer", 17);
            }
            byte[] native = Raw(recipe);
            byte[]? packed = (byte[]?)writePacked.Invoke(null, new[] { recipe });
            Check(packed != null && packed.Length <= 864 && Decode(packed) is object restored
                && Raw(restored).AsSpan().SequenceEqual(native), "Exact70/71/156-byte root and nondefault parent/binding/alpha/visibility/hand/fullcanvas preserved shape=" + shape);
        }
        object multiple = Decode(baseRecipe, true)!;
        var multipleEntries = (System.Collections.IList)Field(multiple, "Entries");
        object second = Decode(baseRecipe, true)!;
        foreach (object entry in (System.Collections.IList)Field(second, "Entries"))
        {
            Set(entry, "Module", (ushort)((ushort)Field(entry, "Module") + 100));
            Set(entry, "Structure", (uint)Field(entry, "Structure") + 1000u);
            if ((byte)Field(entry, "Kind") == 10)
            {
                Set(entry, "ReturnMembers", ((ushort[])Field(entry, "ReturnMembers")).Select(value => (ushort)(value + 100)).ToArray());
                Set(entry, "ReturnStructures", ((uint[])Field(entry, "ReturnStructures")).Select(value => value + 1000u).ToArray());
            }
            multipleEntries.Add(entry);
        }
        byte[] multipleRaw = Raw(multiple);
        object?[] multipleTransform = { multipleRaw, false, null };
        Check((bool)transformRoots.Invoke(null, multipleTransform)!, "Multiple disjoint cohorts have exact independent references");
        object?[] multipleInverse = { (byte[])multipleTransform[2]!, true, null };
        Check((bool)transformRoots.Invoke(null, multipleInverse)! && ((byte[])multipleInverse[2]!).AsSpan().SequenceEqual(multipleRaw), "Multiple cohorts reconstruct every original byte");
        object ambiguous = Decode(baseRecipe, true)!;
        var ambiguousEntries = (System.Collections.IList)Field(ambiguous, "Entries");
        object shifted = CloneEntry(ambiguousEntries.Cast<object>().Single(entry => (byte)Field(entry, "Kind") == 10));
        Set(shifted, "Module", (ushort)((ushort)Field(shifted, "Module") + 1));
        Set(shifted, "Structure", (uint)Field(shifted, "Structure") + 1u);
        Set(shifted, "ReturnMembers", ((ushort[])Field(shifted, "ReturnMembers")).Select(value => (ushort)(value + 1)).ToArray());
        Set(shifted, "ReturnStructures", ((uint[])Field(shifted, "ReturnStructures")).Select(value => value + 1u).ToArray());
        ambiguousEntries.Add(shifted);
        object?[] ambiguousTransform = { Raw(ambiguous), false, null };
        Check(!(bool)transformRoots.Invoke(null, ambiguousTransform)!, "Overlapping source-identical cohorts never guess a Child affinity");
        if (args.Length == 4)
        {
            object legacyRecipe = Decode(baseRecipe, true)!;
            var legacyEntries = (System.Collections.IList)Field(legacyRecipe, "Entries");
            object nativeRoot = legacyEntries.Cast<object>().First(entry => (byte)Field(entry, "Kind") == 1);
            legacyEntries.Clear();
            for (int index = 0; index < 20; index++)
            {
                object root = CloneEntry(nativeRoot); Set(root, "Module", (ushort)(index + 1)); Set(root, "Structure", (uint)(658 + index));
                legacyEntries.Add(root);
            }
            byte[] legacyNative = Raw(legacyRecipe);
            byte[] legacyCurrent = (byte[])writePacked.Invoke(null, new[] { legacyRecipe })!;
            Assembly oldAssembly = Assembly.LoadFile(Path.GetFullPath(args[3]));
            Type oldCodec = oldAssembly.GetType(codec.FullName!, true)!;
            object?[] oldRead = { legacyNative, legacyNative.Length, null, true };
            Check((bool)oldCodec.GetMethod("TryReadCore", flags)!.Invoke(null, oldRead)!, "Frozen6e source accepts full original fallback recipe");
            byte[] oldWire = (byte[])oldCodec.GetMethod("TryWritePacked", flags)!.Invoke(null, new[] { oldRead[2] })!;
            Check(legacyCurrent[21] == 98 && legacyCurrent.AsSpan().SequenceEqual(oldWire), "Actual current98 fallback writer bytes equal frozen6e writer exactly");
        }
        for (int length = 0; length < current.Length; length++)
            Check(Decode(current.AsSpan(0, length).ToArray()) == null, "Every truncation rejected at " + length);
        int nonsemanticDeflateMutations = 0;
        for (int at = 0; at < current.Length; at++)
        {
            byte[] broken = (byte[])current.Clone(); broken[at] ^= 1;
            object? decoded = Decode(broken);
            // Deflate framing contains bits with no effect on expanded bytes.
            // Existing CRC protects the expanded content, not canonical Deflate.
            Check(decoded == null || Raw(decoded).AsSpan().SequenceEqual(sample), "Every mutation rejects or reconstructs unchanged original bytes at " + at);
            if (decoded != null) nonsemanticDeflateMutations++;
        }
        byte[] mixed = (byte[])current.Clone(); mixed[21] = 98;
        Check(Decode(mixed) == null, "Mixed98/115 stream rejected");
        byte[] unknown = current.Concat(new byte[] { 200, 1, 7 }).ToArray();
        Check(Decode(unknown) is object unchanged && Raw(unchanged).AsSpan().SequenceEqual(sample), "Unknown sibling extension keeps exact known payload");
        object?[] transformCall = { sample, false, null };
        Check((bool)transformRoots.Invoke(null, transformCall)!, "Real sample has exact child affinities");
        byte[] transformed = (byte[])transformCall[2]!;
        Check(Decode(Packed(transformed, expectedLength: 8193)) == null, "Expanded size8192 guard retained");
        Check(Decode(Packed(transformed, expectedLength: transformed.Length - 1)) == null, "Deflate expansion bomb fails exact advertised length");
        byte[] badReference = (byte[])transformed.Clone(); badReference[24] = 255;
        Check(Decode(Packed(badReference)) == null, "Missing same-packet cohort reference rejected with valid CRC");
        byte[] missingChild = (byte[])transformed.Clone(); missingChild[25] = 63;
        Check(Decode(Packed(missingChild)) == null, "Membership without transmitted exact Child rejected with valid CRC");
        byte[] badMask = (byte[])transformed.Clone(); badMask[39] |= 4;
        Check(Decode(Packed(badMask)) == null, "XOR mask above ten exact words rejected with valid CRC");
        string six = Directory.GetFiles(args[1], "*seq1-parts6-*.bin").Single();
        byte[] legacyRaw = File.ReadAllBytes(six);
        byte[] legacy = Packed(legacyRaw, 98);
        Check(legacy.Length <= 864 && Decode(legacy) is object old && Raw(old).AsSpan().SequenceEqual(legacyRaw), "Legacy98 grammar remains accepted byte-identically");
        var proof = new { checks, nonsemanticDeflateMutations, assembly = assemblyFile, assemblySHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyFile))),
            fullOriginal = "97/113 BITIDENTICAL", maxEvent = 864, maxExpanded = 8192, maxEntries = 128, rows };
        File.WriteAllText(Path.Combine(args[2], "codec-proof.json"), JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllBytes(Path.Combine(args[2], "real-eight-original97-113.bin"), sample);
        File.WriteAllBytes(Path.Combine(args[2], "real-eight-private115.bin"), current);
        File.WriteAllBytes(Path.Combine(args[2], "real-six-legacy98.bin"), legacy);
        Console.WriteLine("PASS actual compiled return-root codec: " + checks + " assertions; old98 + exact new readbacks + every truncation/bit mutation + bounded adversarial inputs.");
        return 0;
    }
}
