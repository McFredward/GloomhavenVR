using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Immutable offline native-actor derivatives. No decimator, source readback,
/// native asset mutation or per-grab generation runs in the game. The bank owns a bounded
/// set of shared meshes for the process lifetime, including all extant home/highlight twins.
/// Never unload/destroy a mesh merely because a setting or scenario changes.</summary>
internal static class ScenarioFigureMeshBank
{
    internal const string Pattern = "ghvr-figure-meshes-*.bundle";
    private static bool _indexAttempted;
    private static readonly Dictionary<string, string> Index = new();
    private static readonly HashSet<string> AttemptedBanks = new();
    private static readonly List<AssetBundle> Banks = new();
    private static readonly Dictionary<string, Mesh> Meshes = new();
    private static readonly Dictionary<int, string> SourceKeys = new();
    private static readonly HashSet<string> Missing = new();

    internal static string Key(Mesh source, bool? nativeReadable = null)
    {
        // Metadata is accessible even when native imported read/write is OFF. Native bank
        // generation additionally pins complete original mesh/bundle hashes in its manifest.
        // The six bound floats and per-submesh index counts distinguish repeated LOD names.
        ulong hash = 14695981039346656037UL;
        foreach (char value in source.name) hash = (hash ^ value) * 1099511628211UL;
        void Add(uint value) { for (int b = 0; b < 4; b++) { hash = (hash ^ (byte)value) * 1099511628211UL; value >>= 8; } }
        Add((nativeReadable ?? source.isReadable) ? 1u : 0u);
        Add((uint)source.vertexCount); Add((uint)source.subMeshCount);
        for (int sub = 0; sub < source.subMeshCount; sub++) Add(source.GetIndexCount(sub));
        Bounds bounds = source.bounds;
        foreach (float value in new[] { bounds.center.x, bounds.center.y, bounds.center.z, bounds.extents.x, bounds.extents.y, bounds.extents.z })
            Add(BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));
        // Native variants can reuse identical geometry metadata with DIFFERENT bindposes.
        // These skeleton matrices remain accessible with imported read/write OFF; no vertex
        // buffer or GPU readback is needed. Readability also distinguishes native atlas variants
        // with otherwise identical bones/geometry. Generator rejects any remaining ambiguity.
        Matrix4x4[] poses = source.bindposes;
        Add((uint)poses.Length);
        foreach (Matrix4x4 pose in poses)
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                Add(BitConverter.ToUInt32(BitConverter.GetBytes(pose[row, column]), 0));
        return "figure-" + hash.ToString("x16");
    }

    internal static void Prepare(int detail)
    {
        if (detail >= 100) return;
        if (_indexAttempted) return;
        _indexAttempted = true;
        try
        {
            string folder = Path.GetDirectoryName(typeof(ScenarioFigureMeshBank).Assembly.Location) ?? string.Empty;
            string path = Path.Combine(folder, "ghvr-figure-meshes-index.json");
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("unbounded figure index");
            using var input = File.OpenRead(path);
            // BepInEx and the behavioral fixture load the assembly dynamically. Unity's
            // serializer can return an empty field array for an unregistered nested DTO;
            // use the ordinary managed data contract, independent of Unity script discovery.
            Manifest manifest = (Manifest)new DataContractJsonSerializer(typeof(Manifest)).ReadObject(input);
            if (manifest == null || manifest.entries == null || manifest.entries.Length > 4096)
                throw new InvalidDataException("invalid or unbounded figure index");
            foreach (Entry entry in manifest.entries)
            {
                if (entry.bank != Path.GetFileName(entry.bank) || !entry.bank.StartsWith("ghvr-figure-meshes-", StringComparison.Ordinal)
                    || !entry.bank.EndsWith(".bundle", StringComparison.Ordinal)) throw new InvalidDataException("invalid figure bank filename");
                Index.Add(entry.mesh, entry.bank);
            }
        }
        catch (Exception error)
        { VRLog.Note("Perf", "Scenario figure mesh index unavailable (" + error.Message + "); authored meshes remain intact."); }
    }
    [DataContract] private sealed class Manifest { [DataMember] public Entry[] entries = Array.Empty<Entry>(); }
    [DataContract] private sealed class Entry { [DataMember] public string mesh = string.Empty; [DataMember] public string bank = string.Empty; }

    internal static void Prepare(Mesh source, int detail)
    {
        if (detail >= 100) return;
        Prepare(detail);
        string identity = SourceKey(source) + "-" + Tier(detail);
        if (!Index.TryGetValue(identity, out string filename) || !AttemptedBanks.Add(filename)) return;
        try
        {
            string folder = Path.GetDirectoryName(typeof(ScenarioFigureMeshBank).Assembly.Location) ?? string.Empty;
            string path = Path.Combine(folder, filename);
            AssetBundle? bank = null;
            foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                if (loaded.name == filename) { bank = loaded; break; }
            bank ??= AssetBundle.LoadFromFile(path);
            if (bank == null) throw new IOException("asset bank missing or incompatible");
            Banks.Add(bank);
            foreach (Mesh mesh in bank.LoadAllAssets<Mesh>()) Meshes.Add(mesh.name, mesh);
            if (VRLog.WantsDebug) VRLog.Debug("Perf", "Scenario figure mesh part prepared: " + filename
                + "; " + Meshes.Count + " immutable derivatives cached, " + Banks.Count + " resident parts.");
        }
        catch (Exception error)
        { VRLog.Note("Perf", "Scenario figure mesh part unavailable (" + filename + ": " + error.Message + "); authored surfaces remain intact."); }
    }

    internal static Mesh? Resolve(Mesh source, int detail)
    {
        if (detail >= 100) return source;
        string key = SourceKey(source);
        int tier = Tier(detail);
        if (Meshes.TryGetValue(key + "-" + tier, out Mesh mesh)) return mesh;
        if (VRLog.WantsDebug && Missing.Count < 32 && Missing.Add(key))
            VRLog.Debug("Perf", "Scenario figure mesh: no verified derivative for " + source.name + " (" + key + "); original surface retained.");
        return null;
    }
    private static string SourceKey(Mesh source)
    {
        if (!SourceKeys.TryGetValue(source.GetInstanceID(), out string key))
        {
            // Bound source identity cache by scene records, not every historical clone.
            if (SourceKeys.Count >= 2048) SourceKeys.Clear();
            SourceKeys.Add(source.GetInstanceID(), key = Key(source));
        }
        return key;
    }
    private static int Tier(int detail) => detail < 34 ? 20 : detail < 67 ? 45 : 75;

    /// <summary>Exact renderer identity owns restoration. Original collider/bones/materials and
    /// culling bounds are never assigned; foreign native changes relinquish this visual slot.</summary>
    internal sealed class Record
    {
        internal Renderer Renderer = null!;
        internal Mesh Original = null!;
        private Mesh? _applied;
        private bool _foreign;
        private int _detail = 100;
        internal bool UsesDerivative => !_foreign && _applied != null && Current == _applied;
        internal Mesh? Current => Renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
            : Renderer != null ? Renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        private void Assign(Mesh mesh)
        {
            if (Renderer is SkinnedMeshRenderer skin) skin.sharedMesh = mesh;
            else if (Renderer != null && Renderer.TryGetComponent(out MeshFilter filter)) filter.sharedMesh = mesh;
        }
        internal void Apply(int detail)
        {
            if (Renderer == null || _foreign) return;
            Mesh? current = Current;
            if (current != (_applied ?? Original)) { _foreign = true; _applied = null; return; }
            if (detail == _detail) return; // No key/string construction or asset access while unchanged.
            Mesh wanted = Resolve(Original, detail) ?? Original;
            if (current != wanted) Assign(wanted);
            _applied = wanted != Original ? wanted : null;
            _detail = detail;
        }
        internal void Restore() => Apply(100);
    }
}
