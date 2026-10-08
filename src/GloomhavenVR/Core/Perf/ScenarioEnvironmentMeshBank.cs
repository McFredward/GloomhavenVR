using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Private immutable environment render meshes. Build15's Unity batching corrupted
/// cloned native material slots; this bank never assigns a native mesh, material or batch flag.
/// A match requires unambiguous original metadata, hashed source-bundle provenance and hashed
/// prepared geometry. Unknown/currently unavailable originals remain the native renderer.</summary>
internal static class ScenarioEnvironmentMeshBank
{
    internal const string AssetRoot = "Assets/Bundle/EnvironmentMeshes/";
    private static readonly Dictionary<string, Entry> Entries = new();
    private static readonly Dictionary<string, Mesh> Meshes = new();
    private static readonly Dictionary<Mesh, Entry> NativeEntries = new();
    private static readonly Dictionary<string, bool> Provenance = new();
    private static readonly HashSet<string> Failed = new();
    private static bool _loaded;
    private static HashSet<string>? _questSources;
    private static Func<bool>? _ensureAssetsLoaded;
    internal static void ConfigureAssetPreparation(Func<bool> ensureAssetsLoaded) => _ensureAssetsLoaded = ensureAssetsLoaded;
    internal static bool IsReady => PrepareIndex();
    internal static bool IsUnavailable => Failed.Contains("index") || Failed.Contains("asset-loader");

    internal static bool TryGetExact(Mesh native, out Mesh mesh) => TryGet(native, 100, out mesh);
    internal static bool TryGetDetail(Mesh native, int percent, out Mesh mesh) => TryGet(native, percent >= 100 ? 100 : percent >= 50 ? 50 : 0, out mesh);
    internal static bool IsTerrainEligible(Mesh native) => TryGetExact(native, out _);

    // The role is exported from every original prefab use of this exact immutable
    // mesh. Runtime names, a matching bounds box or bank membership alone never
    // authorize a floor/architecture replacement. Ambiguous/interactive uses are
    // explicitly role=none in the all-game catalog and retain native geometry.
    // Roles:0 unsupported,1 core floor,2 structure,3 exact authored floor support.
    // Support role3 shares floor detail/fade safety but is never a core-floor group.
    internal static int RoomArchitectureRole(Mesh native)
    {
        if (native == null || !PrepareIndex()) return 0;
        if (!Entries.TryGetValue(Identity(native), out Entry entry)
            || !Matches(native, entry.signature) || !SourceValid(entry)) return 0;
        return entry.role == "floor" ? 1 : entry.role == "structure" ? FloorSupportIdentity(entry.signature.name) ? 3 : 2 : 0;
    }
    private static bool FloorSupportIdentity(string name) => name is "CV_Floor_HexOutline_Rock_02"
        or "CV_Floor_HexOutline_Rock_03" or "CV_Floor_HexOutline_Rock_04"
        or "TERRAIN_DU_Rubble_Floor" or "TERRAIN_DU_Thorns_Floor";
    internal static bool IsArchitecturalOrnament(Mesh native) => native != null && PrepareIndex()
        && Entries.TryGetValue(Identity(native), out Entry entry) && entry.ornament
        && Matches(native, entry.signature) && SourceValid(entry);

    private static bool TryGet(Mesh native, int tier, out Mesh mesh)
    {
        mesh = null!;
        if (native == null || !PrepareIndex()) return false;
        if (!NativeEntries.TryGetValue(native, out Entry entry))
        {
            string identity = Identity(native);
            if (!Entries.TryGetValue(identity, out entry)) return false;
            if (NativeEntries.Count >= 2048) NativeEntries.Clear();
            NativeEntries.Add(native, entry);
        }
        // Imported source metadata can change when native code replaces its geometry. Never
        // retain a Mesh instance-id verdict or make geometry equality a name-only assumption.
        if (!Matches(native, entry.signature) || !SourceValid(entry)) return false;
        Variant? variant = null;
        foreach (Variant candidate in entry.variants) if (candidate.tier == tier) { variant = candidate; break; }
        if (variant == null && tier != 100) return TryGet(native, 100, out mesh);
        if (variant == null || Failed.Contains(variant.file)) return false;
        if (Meshes.TryGetValue(variant.file, out mesh) && mesh != null) return true;
        try
        {
            TextAsset? asset = Asset(variant.file);
            if (asset == null) return false; // A later-loaded bank may still become available.
            byte[] bytes = asset.bytes;
            if (bytes.Length > 32 * 1024 * 1024 || Digest(bytes) != variant.sha256) throw new InvalidDataException("prepared geometry hash");
            mesh = ScenarioEnvironmentMeshStream.Read(bytes);
            if (mesh.vertexCount != entry.signature.vertices || mesh.subMeshCount != entry.signature.indices.Length
                || mesh.bounds != native.bounds || !mesh.isReadable) throw new InvalidDataException("prepared topology/bounds");
            if (tier == 100)
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (mesh.GetIndexCount(sub) != entry.signature.indices[sub]) throw new InvalidDataException("prepared original indices");
            mesh.name = "GloomhavenVR.Environment." + entry.key + "." + tier;
            Meshes.Add(variant.file, mesh);
            return true;
        }
        catch (Exception error)
        {
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
            mesh = null!;
            if (Failed.Add(variant.file)) VRLog.Note("Perf", "Scenario environment mesh unavailable (" + error.Message + "); native surfaces retained.");
            return false;
        }
    }
    private static bool PrepareIndex()
    {
        if (_loaded) return true;
        if (Failed.Contains("index")) return false;
        TextAsset? asset = Asset("index.json");
        if (asset == null) return false;
        try
        {
            byte[] bytes = asset.bytes;
            if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("environment index bound");
            using var input = new MemoryStream(bytes, false);
            Manifest manifest = (Manifest)new DataContractJsonSerializer(typeof(Manifest)).ReadObject(input);
            if (manifest.format != 1 || manifest.entries.Length > 8192) throw new InvalidDataException("environment index format/bound");
            HashSet<string>? questSources = null;
            if (QuestStandalonePlatform.Enabled)
            {
                TextAsset? origins = Asset("quest-owned-sources.json");
                if (origins == null) throw new InvalidDataException("missing owned environment source receipt");
                questSources = QuestStandalonePlatform.OwnedEnvironmentSources(bytes, origins.bytes);
            }
            var pending = new Dictionary<string, Entry>();
            foreach (Entry entry in manifest.entries)
            {
                if (!string.IsNullOrEmpty(entry.role) && entry.role != "none" && entry.role != "floor" && entry.role != "structure")
                    throw new InvalidDataException("environment architecture role");
                if (entry.signature.name.Length < 1 || entry.signature.name.Length > 1024 || entry.signature.vertices < 3 || entry.signature.vertices > 100000 || entry.signature.indices.Length < 1
                    || entry.signature.indices.Length > 32 || entry.signature.bounds.Length != 6 || entry.sources.Length < 1
                    || entry.sources.Length > 128 || entry.variants.Length < 1 || entry.variants.Length > 3)
                    throw new InvalidDataException("environment metadata bounds");
                foreach (Source source in entry.sources)
                    if (!SafeSource(source.path) || source.sha256.Length != 64) throw new InvalidDataException("environment source path/hash");
                foreach (Variant variant in entry.variants)
                    if (variant.file != Path.GetFileName(variant.file) || !variant.file.EndsWith(".bytes", StringComparison.Ordinal)
                        || variant.sha256.Length != 64 || (variant.tier != 0 && variant.tier != 50 && variant.tier != 100))
                        throw new InvalidDataException("environment variant path/hash");
                pending.Add(Identity(entry.signature), entry); // Any duplicate/ambiguous metadata rejects the entire index.
            }
            foreach (var item in pending) Entries.Add(item.Key, item.Value);
            _questSources = questSources;
            _loaded = true;
            return true;
        }
        catch (Exception error)
        {
            if (Failed.Add("index")) VRLog.Note("Perf", "Scenario environment mesh index unavailable (" + error.Message + "); native surfaces retained.");
            return false;
        }
    }
    private static TextAsset? Asset(string filename)
    {
        try { if (_ensureAssetsLoaded?.Invoke() == false) return null; }
        catch (Exception error)
        {
            if (Failed.Add("asset-loader")) VRLog.Note("Perf", "Scenario environment asset preparation unavailable (" + error.Message + "); native surfaces retained.");
            return null;
        }
        string path = AssetRoot + filename;
        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            if (bundle != null && bundle.Contains(path)) return bundle.LoadAsset<TextAsset>(path);
        return null;
    }
    private static bool SafeSource(string path) => path.StartsWith("pcg_databases_assets_assets/pcg/", StringComparison.Ordinal)
        && path.EndsWith(".bundle", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains("\\") && !Path.IsPathRooted(path);
    private static bool SourceValid(Entry entry)
    {
        if (entry.VerifiedSource != null && entry.VerifiedSource.sha256 == entry.VerifiedHash) return true;
        foreach (Source source in entry.sources)
        {
            string key = source.path + ":" + source.sha256;
            if (!Provenance.TryGetValue(key, out bool valid))
            {
                valid = false;
                if (QuestStandalonePlatform.Enabled)
                    valid = _questSources?.Contains(key) == true;
                else try
                {
                    string path = Path.Combine(Application.streamingAssetsPath, "aa", "StandaloneWindows64", source.path);
                    using var input = File.OpenRead(path);
                    using var sha = SHA256.Create();
                    valid = Hex(sha.ComputeHash(input)) == source.sha256;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Provenance.Add(key, valid);
            }
            if (valid) { entry.VerifiedSource = source; entry.VerifiedHash = source.sha256; return true; }
        }
        if (Failed.Count < 32 && Failed.Add("source:" + entry.key))
            VRLog.Note("Perf", "Scenario environment mesh source provenance unavailable; native surfaces retained.");
        return false;
    }
    private static string Digest(byte[] bytes) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(bytes)); }
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    private static string Identity(Mesh mesh)
    {
        var text = new StringBuilder(mesh.name).Append('|').Append(mesh.vertexCount).Append('|').Append(mesh.isReadable ? 1 : 0);
        Bounds bounds = mesh.bounds;
        AddBounds(text, bounds.center, bounds.extents);
        for (int sub = 0; sub < mesh.subMeshCount; sub++) text.Append('|').Append(mesh.GetIndexCount(sub));
        return text.ToString();
    }
    private static string Identity(Signature sig)
    {
        var text = new StringBuilder(sig.name).Append('|').Append(sig.vertices).Append('|').Append(sig.readable ? 1 : 0);
        for (int i = 0; i < 6; i++) text.Append('|').Append(BitConverter.ToInt32(BitConverter.GetBytes(sig.bounds[i]), 0));
        foreach (uint count in sig.indices) text.Append('|').Append(count);
        return text.ToString();
    }
    private static void AddBounds(StringBuilder text, Vector3 center, Vector3 extents)
    {
        for (int i = 0; i < 3; i++) text.Append('|').Append(BitConverter.ToInt32(BitConverter.GetBytes(center[i]), 0));
        for (int i = 0; i < 3; i++) text.Append('|').Append(BitConverter.ToInt32(BitConverter.GetBytes(extents[i]), 0));
    }
    private static bool Matches(Mesh mesh, Signature sig)
    {
        if (mesh.name != sig.name || mesh.vertexCount != sig.vertices || mesh.subMeshCount != sig.indices.Length || mesh.isReadable != sig.readable) return false;
        Bounds bounds = mesh.bounds;
        for (int i = 0; i < 3; i++) if (bounds.center[i] != sig.bounds[i] || bounds.extents[i] != sig.bounds[i + 3]) return false;
        for (int i = 0; i < sig.indices.Length; i++)
            if (mesh.GetIndexCount(i) != sig.indices[i] || mesh.GetTopology(i) != MeshTopology.Triangles) return false;
        return true;
    }
    [DataContract] private sealed class Manifest { [DataMember] public int format = 0; [DataMember] public Entry[] entries = Array.Empty<Entry>(); }
    [DataContract] private sealed class Entry
    { [DataMember] public string key = string.Empty; [DataMember] public string role = string.Empty; [DataMember] public bool ornament = false; [DataMember] public Signature signature = new(); [DataMember] public Source[] sources = Array.Empty<Source>(); [DataMember] public Variant[] variants = Array.Empty<Variant>(); internal Source? VerifiedSource; internal string? VerifiedHash; }
    [DataContract] private sealed class Signature
    { [DataMember] public string name = string.Empty; [DataMember] public bool readable = false; [DataMember] public int vertices = 0; [DataMember] public uint[] indices = Array.Empty<uint>(); [DataMember] public float[] bounds = Array.Empty<float>(); }
    [DataContract] private sealed class Source { [DataMember] public string path = string.Empty; [DataMember] public string sha256 = string.Empty; }
    [DataContract] private sealed class Variant { [DataMember] public int tier = 0; [DataMember] public string file = string.Empty; [DataMember] public string sha256 = string.Empty; }
}
