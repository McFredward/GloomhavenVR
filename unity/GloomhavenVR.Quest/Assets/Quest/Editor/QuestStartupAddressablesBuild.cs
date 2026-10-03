#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Builds real Android Addressables for the bounded original-startup closure.</summary>
    public static class QuestStartupAddressablesBuild
    {
        [Serializable] sealed class Association
        {
            public string assetPath, recoveredGuid, resourceTypeName;
            public bool initialObjectLoadEligible;
            public string[] keys, labels;
        }
        [Serializable] sealed class Associations { public int schema; public Association[] entries; }
        [Serializable] sealed class ContentFile { public string path, sha256; public long size; }
        [Serializable] sealed class ContentManifest
        {
            public int schema;
            public string inputKey, archive, archiveSha256;
            public ContentFile[] files;
        }
        [Serializable] sealed class Alias { public string key, assetGuid; }
        [Serializable] sealed class RuntimeManifest
        {
            public int schema = 1;
            public string inputKey, runtimeSettingsPath;
            public string[] requiredLabels;
            public Alias[] aliases;
        }

        public static void Build()
        {
            const string mappingPath = "Assets/QuestOriginalStartup/startup-addressables.json";
            var mapping = JsonUtility.FromJson<Associations>(File.ReadAllText(mappingPath));
            if (mapping == null || mapping.schema != 1 || mapping.entries == null || mapping.entries.Length == 0)
                throw new InvalidDataException("Original startup has no exact Addressables association inventory.");
            const string folder = "Assets/Quest/Settings/Addressables";
            Directory.CreateDirectory(folder);
            var settings = AddressableAssetSettings.Create(folder, "QuestStartup", true, true);
            AddressableAssetSettingsDefaultObject.Settings = settings;
            settings.BuildRemoteCatalog = false;
            settings.DisableCatalogUpdateOnStartup = true;
            var group = settings.CreateGroup("Owned original startup", true, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            settings.DefaultGroup = group;
            var bundled = group.GetSchema<BundledAssetGroupSchema>();
            bundled.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundled.IncludeAddressInCatalog = true;
            bundled.IncludeGUIDInCatalog = true;
            bundled.IncludeLabelsInCatalog = true;
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var labels = new HashSet<string>(StringComparer.Ordinal);
            var entries = new HashSet<string>(StringComparer.Ordinal);
            // The original AssetBundleManager preloads UnityEngine.Object only.
            // Catalog locations for serialized enum/value types are not objects
            // and must not become fabricated native assets during this startup check.
            Association[] objects = mapping.entries.Where(row => row.initialObjectLoadEligible).ToArray();
            if (objects.Length == 0) throw new InvalidDataException("Original startup contains no proven Unity object locations.");
            var keyTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (Association association in objects)
                foreach (string key in association.keys ?? Array.Empty<string>())
                {
                    if (!keyTargets.TryGetValue(key, out HashSet<string> targets))
                        keyTargets.Add(key, targets = new HashSet<string>(StringComparer.Ordinal));
                    targets.Add(association.recoveredGuid);
                }
            foreach (Association association in objects)
            {
                if (string.IsNullOrEmpty(association.assetPath) || string.IsNullOrEmpty(association.recoveredGuid))
                    throw new InvalidDataException("Startup mapping contains an unresolved source asset.");
                string guid = AssetDatabase.AssetPathToGUID(association.assetPath);
                if (guid != association.recoveredGuid || AssetDatabase.LoadMainAssetAtPath(association.assetPath) == null)
                    throw new InvalidDataException("Original startup asset identity failed import: " + association.assetPath);
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = guid;
                entries.Add(guid);
                foreach (string label in association.labels ?? Array.Empty<string>())
                {
                    settings.AddLabel(label, false);
                    entry.SetLabel(label, true, false, false);
                    labels.Add(label);
                }
                foreach (string key in association.keys ?? Array.Empty<string>())
                {
                    // Labels resolve their whole group through the native catalog;
                    // aliases route only exact original object keys to exact targets.
                    if ((association.labels ?? Array.Empty<string>()).Contains(key)) continue;
                    if (keyTargets[key].Count > 1)
                    {
                        // Original bucket keys may address several typed objects.
                        // Native labels preserve that whole set and its type filtering.
                        settings.AddLabel(key, false);
                        entry.SetLabel(key, true, false, false);
                        continue;
                    }
                    if (aliases.TryGetValue(key, out string previous) && previous != guid)
                        throw new InvalidDataException("Original key maps to multiple startup objects: " + key);
                    aliases[key] = guid;
                }
            }
            string[] required = { "always_loaded_base", "always_loaded_standalone", "always_loaded_base_high" };
            foreach (string label in required)
                if (!labels.Contains(label)) throw new InvalidDataException("Required original startup label was not recovered: " + label);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException("Native startup Addressables failed: " + result.Error);
            string built = Addressables.BuildPath;
            if (!File.Exists(Path.Combine(built, "settings.json")))
                throw new InvalidDataException("Native Android Addressables did not produce actual RuntimeSettings.");
            RepackContent(built);
            var content = JsonUtility.FromJson<ContentManifest>(File.ReadAllText("Assets/Quest/Resources/quest-startup-content.json"));
            File.WriteAllText("Assets/Quest/Resources/quest-startup-addressables.json", JsonUtility.ToJson(new RuntimeManifest
            {
                inputKey = content.inputKey, runtimeSettingsPath = "StreamingAssets/aa/settings.json", requiredLabels = required,
                aliases = aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new Alias { key = pair.Key, assetGuid = pair.Value }).ToArray()
            }, true));
            AssetDatabase.Refresh();
            Debug.Log("[Quest startup build] native Android catalog assets=" + entries.Count + " original-key aliases=" + aliases.Count + " labels=" + labels.Count);
        }

        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static void RepackContent(string nativeRoot)
        {
            const string manifestPath = "Assets/Quest/Resources/quest-startup-content.json";
            var manifest = JsonUtility.FromJson<ContentManifest>(File.ReadAllText(manifestPath));
            string archive = Path.Combine("Assets/StreamingAssets", manifest.archive);
            if (Hash(archive) != manifest.archiveSha256)
                throw new InvalidDataException("Owned startup archive changed before native catalog packaging.");
            var retained = manifest.files.Where(file => !file.path.StartsWith("StreamingAssets/aa/", StringComparison.Ordinal)).ToArray();
            var nativeFiles = Directory.GetFiles(nativeRoot, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (nativeFiles.Length == 0) throw new InvalidDataException("Native startup Addressables output is empty.");
            var added = nativeFiles.Select(path => new ContentFile
            {
                path = "StreamingAssets/aa/" + path.Substring(nativeRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'),
                sha256 = Hash(path), size = new FileInfo(path).Length
            }).ToArray();
            string temp = archive + ".repack-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var input = new ZipArchive(File.OpenRead(archive), ZipArchiveMode.Read))
                using (var output = new ZipArchive(File.Create(temp), ZipArchiveMode.Create))
                {
                    foreach (ContentFile file in retained)
                    {
                        ZipArchiveEntry old = input.GetEntry(file.path);
                        if (old == null || old.Length != file.size) throw new InvalidDataException("Owned startup archive lost original file: " + file.path);
                        Copy(old.Open, output, file.path);
                    }
                    for (int index = 0; index < nativeFiles.Length; ++index)
                    {
                        string source = nativeFiles[index];
                        Copy(() => File.OpenRead(source), output, added[index].path);
                    }
                }
                File.Delete(archive);
                File.Move(temp, archive);
                manifest.files = retained.Concat(added).OrderBy(file => file.path, StringComparer.Ordinal).ToArray();
                manifest.archiveSha256 = Hash(archive);
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        static void Copy(Func<Stream> source, ZipArchive archive, string name)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (Stream input = source())
            using (Stream output = entry.Open()) input.CopyTo(output, 65536);
        }
    }
}
#endif
