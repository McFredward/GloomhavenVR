// Local instrumentation of the pinned, open-source AssetRipper exporter.
// This records original object identity; it does not modify imported game data.
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetRipper.Yaml;

namespace AssetRipper.Export.UnityProjects;

internal static class QuestExportIdentity
{
    public static bool ShouldExport(IExportContainer container)
    {
        CaptureRedirects(container);
        IExportCollection collection = ((ProjectAssetContainer)container).CurrentCollection;
        // The Python source patch invokes this method before Exportable so
        // non-exportable engine redirects still retain their native pointers.
        // They have no exported asset/GUID entry in the independent core.
        // Recording them as skippedCore makes a fresh full recovery fail its
        // original-object closure check (Windows support report 2026-10-06).
        if (!collection.Exportable) return false;
        if (Environment.GetEnvironmentVariable("QUEST_EXPORT_BUNDLE_ONLY") != "1") return true;
        // Global engine managers are written into ProjectSettings and cannot
        // be referenced through exported GUIDs (upstream explicitly throws).
        if (collection is AssetRipper.Export.UnityProjects.Project.ManagerExportCollection) return false;
        IUnityObjectBase[] original = collection.Assets
            .Where(asset => asset.ClassName != "MonoScript" && asset.Collection is SerializedAssetCollection).ToArray();
        if (original.Any(asset => asset.Collection.Name.StartsWith("CAB-", StringComparison.OrdinalIgnoreCase))) return true;
        // Keep pointers for skipped core collections, so references from an
        // incoming bundle can be mapped to the independently exported core.
        foreach (string guid in original.Select(asset => collection.CreateExportPointer(container, asset, false).GUID.ToString()).Distinct())
            Record(container, guid, "");
        return false;
    }

    private static void CaptureRedirects(IExportContainer container)
    {
        string? destination = Environment.GetEnvironmentVariable("QUEST_EXPORT_REDIRECT_IDENTITIES");
        if (string.IsNullOrEmpty(destination)) return;
        IExportCollection collection = ((ProjectAssetContainer)container).CurrentCollection;
        if (collection is not RedirectExportCollection && collection is not SingleRedirectExportCollection) return;
        foreach (IUnityObjectBase asset in collection.Assets)
        {
            if (asset.Collection is not SerializedAssetCollection) continue;
            MetaPtr pointer = collection.CreateExportPointer(container, asset, false);
            // A missing redirect is not evidence of a usable target.
            if (pointer.GUID.ToString() == "0000000deadbeef15deadf00d0000000") continue;
            File.AppendAllText(destination, JsonSerializer.Serialize(new
            {
                collection = asset.Collection.Name,
                pathId = asset.PathID,
                classId = asset.ClassID,
                className = asset.ClassName,
                guid = pointer.GUID.ToString(),
                fileId = pointer.FileID,
                type = (int)pointer.AssetType,
                exportCollection = collection.GetType().FullName,
            }) + "\n");
        }
    }

    public static void CaptureNativeRecipe(IUnityObjectBase asset)
    {
        string? destination = Environment.GetEnvironmentVariable("QUEST_EXPORT_NATIVE_RECIPES");
        if (string.IsNullOrEmpty(destination) || asset.Collection is not SerializedAssetCollection) return;
        // Packed atlases are deliberately omitted by the normal exporter.
        // Core managed components can have only the base native type tree;
        // retain the exact original assembly-parsed field graph as evidence.
        if (asset.ClassID != 687078895 && (asset.ClassID != 114 ||
            asset.Collection.Name.StartsWith("CAB-", StringComparison.OrdinalIgnoreCase))) return;
        string key = asset.Collection.Name.ToLowerInvariant() + ":" + asset.PathID.ToString(CultureInfo.InvariantCulture);
        string filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".yaml";
        Directory.CreateDirectory(destination);
        using StringWriter stream = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
        YamlWriter writer = new();
        writer.WriteHead(stream);
        writer.WriteDocument(new YamlWalker().ExportYamlDocument(asset, ExportIdHandler.GetMainExportID(asset)));
        writer.WriteTail(stream);
        string yaml = stream.ToString();
        string path = Path.Combine(destination, filename);
        if (File.Exists(path))
        {
            if (File.ReadAllText(path) != yaml) throw new InvalidDataException("Original native recipe changed within one export.");
            return;
        }
        File.WriteAllText(path, yaml);
        File.AppendAllText(Path.Combine(destination, "index.jsonl"), JsonSerializer.Serialize(new
        {
            collection = asset.Collection.Name,
            pathId = asset.PathID,
            classId = asset.ClassID,
            yamlPath = filename,
            yamlSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml))).ToLowerInvariant(),
        }) + "\n");
    }

    public static void Record(IExportContainer container, string guid, string path)
    {
        string? destination = Environment.GetEnvironmentVariable("QUEST_EXPORT_IDENTITIES");
        if (string.IsNullOrEmpty(destination)) return;
        IExportCollection collection = ((ProjectAssetContainer)container).CurrentCollection;
        using MemoryStream bytes = new();
        using (Utf8JsonWriter writer = new(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString("guid", guid);
            writer.WriteString("path", path);
            writer.WriteBoolean("skippedCore", path.Length == 0);
            writer.WriteString("exportCollection", collection.GetType().FullName);
            writer.WriteStartArray("objects");
            foreach (IUnityObjectBase asset in collection.Assets)
            {
                CaptureNativeRecipe(asset);
                // Generated prefab/scene/sprite bookkeeping is not an original
                // CAB/pathID identity and its export ID may not exist. Script
                // DLL GUIDs already derive from the original assembly name.
                if (asset.ClassName == "MonoScript" || asset.Collection is not SerializedAssetCollection) continue;
                if (collection.CreateExportPointer(container, asset, false).GUID.ToString() != guid) continue;
                writer.WriteStartObject();
                writer.WriteString("collection", asset.Collection.Name);
                writer.WriteNumber("pathId", asset.PathID);
                writer.WriteNumber("fileId", collection.GetExportID(container, asset));
                writer.WriteNumber("classId", asset.ClassID);
                writer.WriteString("className", asset.ClassName);
                writer.WriteString("originalPath", asset.OriginalPath);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        File.AppendAllText(destination, System.Text.Encoding.UTF8.GetString(bytes.ToArray()) + "\n");
    }
}
