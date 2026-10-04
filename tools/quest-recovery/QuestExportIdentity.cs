// Local instrumentation of the pinned, open-source AssetRipper exporter.
// This records original object identity; it does not modify imported game data.
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using System.Text.Json;

namespace AssetRipper.Export.UnityProjects;

internal static class QuestExportIdentity
{
    public static bool ShouldExport(IExportContainer container)
    {
        if (Environment.GetEnvironmentVariable("QUEST_EXPORT_BUNDLE_ONLY") != "1") return true;
        IExportCollection collection = ((ProjectAssetContainer)container).CurrentCollection;
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
