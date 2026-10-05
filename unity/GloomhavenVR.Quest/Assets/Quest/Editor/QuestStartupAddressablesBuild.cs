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
    /// <summary>Builds native catalogs while retaining original keys and asset ownership.</summary>
    public static class QuestStartupAddressablesBuild
    {
        [Serializable] sealed class Association
        {
            public string assetPath, recoveredGuid, resourceTypeName, sourceBundle, subObjectName, status;
            public bool initialObjectLoadEligible;
            public string[] keys, labels;
        }
        [Serializable] sealed class Associations
        {
            public int schema, associatedEntryCount, unresolvedEntryCount;
            public string scope;
            public Association[] entries;
        }
        [Serializable] sealed class ContentFile { public string path, sha256; public long size; }
        [Serializable] sealed class ContentManifest
        {
            public int schema;
            public string inputKey, archive, archiveSha256;
            public bool externalDelivery;
            public ContentFile[] files;
        }
        [Serializable] sealed class Alias { public string key, assetGuid; public string[] assetKeys; }
        [Serializable] sealed class RuntimeManifest
        {
            public int schema = 1;
            public string inputKey, runtimeSettingsPath;
            public string[] requiredLabels;
            public Alias[] aliases;
        }

        public static void Build()
        {
            bool campaign = Environment.GetEnvironmentVariable("GHVR_QUEST_TARGET") == "game";
            string mappingPath = campaign ? "Assets/QuestOriginalCampaign/campaign-addressables.json"
                : "Assets/QuestOriginalStartup/startup-addressables.json";
            var mapping = JsonUtility.FromJson<Associations>(File.ReadAllText(mappingPath));
            if (mapping == null || mapping.schema != 1 || mapping.entries == null || mapping.entries.Length == 0)
                throw new InvalidDataException("Original startup has no exact Addressables association inventory.");
            if (campaign && (mapping.associatedEntryCount <= 0 || mapping.unresolvedEntryCount != 0
                || mapping.entries.Count(row => row.status == "associated") != mapping.associatedEntryCount))
                throw new InvalidDataException("The Campaign catalog lacks complete original object-location coverage.");
            const string folder = "Assets/Quest/Settings/Addressables";
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(AddressableAssetSettingsDefaultObject.kDefaultConfigFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(folder + "/QuestStartup.asset")
                ?? AddressableAssetSettings.Create(folder, "QuestStartup", true, true);
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            AddressableAssetSettingsDefaultObject.Settings = settings;
            settings.BuildRemoteCatalog = false;
            settings.DisableCatalogUpdateOnStartup = true;
            var aliases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var labels = new HashSet<string>(StringComparer.Ordinal);
            var entries = new HashSet<string>(StringComparer.Ordinal);
            string[] required = { "always_loaded_base", "always_loaded_standalone", "always_loaded_base_high" };
            // Native AddSchema still saves assets. Pause their individual imports
            // while retaining its original schema/profile initialization and all
            // per-bundle groups. Stop before the explicit save and native build.
            AssetDatabase.StartAssetEditing();
            try
            {
                var group = GetOrCreateOwnedGroup(settings, "Owned original startup", true);
                settings.DefaultGroup = group;
                var bundled = group.GetSchema<BundledAssetGroupSchema>();
                bundled.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                bundled.IncludeAddressInCatalog = true;
                bundled.IncludeGUIDInCatalog = true;
                bundled.IncludeLabelsInCatalog = true;
                var ownedGroups = new Dictionary<string, AddressableAssetGroup>(StringComparer.Ordinal);
                var nativeOwners = new Dictionary<string, AddressableAssetGroup>(StringComparer.Ordinal);
                // The original AssetBundleManager preloads UnityEngine.Object only.
                // Catalog locations for serialized enum/value types are not objects
                // and must not become fabricated native assets during this startup check.
                Association[] objects = mapping.entries.Where(row => campaign ? row.status == "associated" : row.initialObjectLoadEligible).ToArray();
                if (objects.Length == 0) throw new InvalidDataException("Original startup contains no proven Unity object locations.");
                var keyTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (Association association in objects)
                    foreach (string key in association.keys ?? Array.Empty<string>())
                    {
                        if (!keyTargets.TryGetValue(key, out HashSet<string> targets))
                            keyTargets.Add(key, targets = new HashSet<string>(StringComparer.Ordinal));
                        targets.Add(NativeKey(association));
                    }
                foreach (Association association in objects)
                {
                    if (string.IsNullOrEmpty(association.assetPath) || string.IsNullOrEmpty(association.recoveredGuid))
                        throw new InvalidDataException("Startup mapping contains an unresolved source asset.");
                    string guid = AssetDatabase.AssetPathToGUID(association.assetPath);
                    if (guid != association.recoveredGuid || AssetDatabase.LoadMainAssetAtPath(association.assetPath) == null)
                        throw new InvalidDataException("Original startup asset identity failed import: " + association.assetPath);
                    AddressableAssetGroup owner = group;
                    if (campaign)
                    {
                        // Retain independent content lifetimes. A single all-game
                        // bundle would make loading one actor pin every scenario.
                        string bundle = string.IsNullOrEmpty(association.sourceBundle) ? "original-core" : association.sourceBundle;
                        if (!ownedGroups.TryGetValue(bundle, out owner))
                        {
                            string name = "Owned Campaign " + StableName(bundle);
                            owner = GetOrCreateOwnedGroup(settings, name, false);
                            var schema = owner.GetSchema<BundledAssetGroupSchema>();
                            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                            schema.IncludeAddressInCatalog = schema.IncludeGUIDInCatalog = schema.IncludeLabelsInCatalog = true;
                            ownedGroups.Add(bundle, owner);
                        }
                        if (nativeOwners.TryGetValue(guid, out AddressableAssetGroup previousOwner)) owner = previousOwner;
                        else nativeOwners.Add(guid, owner);
                        if (!string.IsNullOrEmpty(association.subObjectName)
                            && !AssetDatabase.LoadAllAssetsAtPath(association.assetPath).Any(asset => asset != null && asset.name == association.subObjectName))
                            throw new InvalidDataException("Original catalog subobject was lost: " + association.assetPath + "[" + association.subObjectName + "]");
                    }
                    var entry = settings.CreateOrMoveEntry(guid, owner, false, false);
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
                        if (!campaign && keyTargets[key].Count > 1)
                        {
                            // Original bucket keys may address several typed objects.
                            // Native labels preserve that whole set and its type filtering.
                            settings.AddLabel(key, false);
                            entry.SetLabel(key, true, false, false);
                            continue;
                        }
                        if (!aliases.TryGetValue(key, out HashSet<string> nativeKeys))
                            aliases.Add(key, nativeKeys = new HashSet<string>(StringComparer.Ordinal));
                        nativeKeys.Add(NativeKey(association));
                    }
                }
                foreach (string label in required)
                    if (!labels.Contains(label)) throw new InvalidDataException("Required original startup label was not recovered: " + label);
                EditorUtility.SetDirty(settings);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
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
                aliases = aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new Alias
                {
                    key = pair.Key,
                    assetGuid = pair.Value.Count == 1 ? pair.Value.Single() : null,
                    assetKeys = campaign ? pair.Value.OrderBy(value => value, StringComparer.Ordinal).ToArray() : null
                }).ToArray()
            }, true));
            AssetDatabase.Refresh();
            Debug.Log("[Quest startup build] native Android catalog assets=" + entries.Count + " original-key aliases=" + aliases.Count + " labels=" + labels.Count);
        }

        internal static AddressableAssetGroup GetOrCreateOwnedGroup(AddressableAssetSettings settings, string name, bool setAsDefault)
        {
            var group = settings.FindGroup(name) ?? settings.CreateGroup(name, setAsDefault, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            // A previous interrupted editor run may already have registered the
            // group while retaining only one schema. Complete it through the
            // package's native API rather than cloning or guessing serialized IDs.
            if (group.GetSchema<BundledAssetGroupSchema>() == null) group.AddSchema<BundledAssetGroupSchema>(false);
            if (group.GetSchema<ContentUpdateGroupSchema>() == null) group.AddSchema<ContentUpdateGroupSchema>(false);
            return group;
        }

        static string NativeKey(Association association)
        {
            if (string.IsNullOrEmpty(association.subObjectName)) return association.recoveredGuid;
            if (association.subObjectName.IndexOfAny(new[] { '[', ']' }) >= 0)
                throw new InvalidDataException("Original subobject name cannot be represented by native Addressables: " + association.subObjectName);
            return association.recoveredGuid + "[" + association.subObjectName + "]";
        }

        static string StableName(string source)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source)))
                    .Replace("-", "").ToLowerInvariant().Substring(0, 24);
        }

        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        [Serializable] sealed class NativePackRequest
        {
            public int schema = 1;
            public string projectRoot, nativeRoot;
        }
        [Serializable] sealed class NativePackReceipt
        {
            public int schema, fileCount, nativeBundleCount;
            public bool reused;
            public string inputKey, archiveSha256;
        }

        static void RepackContent(string nativeRoot)
        {
            string python = Environment.GetEnvironmentVariable("GHVR_QUEST_CONTENT_PACK_PYTHON");
            string helper = Environment.GetEnvironmentVariable("GHVR_QUEST_CONTENT_PACK_HELPER");
            if (string.IsNullOrEmpty(python) || string.IsNullOrEmpty(helper)
                || !File.Exists(python) || !File.Exists(helper))
                throw new InvalidOperationException("The builder's verified Python content packer is required.");
            const string evidence = "QuestStartupEvidence";
            Directory.CreateDirectory(evidence);
            string request = Path.GetFullPath(Path.Combine(evidence, "content-pack-request-" + Guid.NewGuid().ToString("N") + ".json"));
            File.WriteAllText(request, JsonUtility.ToJson(new NativePackRequest
            {
                projectRoot = Directory.GetCurrentDirectory(), nativeRoot = Path.GetFullPath(nativeRoot)
            }, true));
            try
            {
                using (var process = new System.Diagnostics.Process())
                {
                    process.StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = python, Arguments = "-I -B -X utf8 " + QuoteArgument(helper) + " --request " + QuoteArgument(request),
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        WorkingDirectory = Directory.GetCurrentDirectory()
                    };
                    // The standard-library helper emits one bounded result/error
                    // line. It never dumps content, environment or private keys.
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new InvalidDataException("Native content packer failed (" + process.ExitCode + "): "
                            + error.Substring(0, Math.Min(error.Length, 2048)));
                }
                var receipt = JsonUtility.FromJson<NativePackReceipt>(File.ReadAllText(Path.Combine(evidence, "native-content-pack.json")));
                var content = JsonUtility.FromJson<ContentManifest>(File.ReadAllText("Assets/Quest/Resources/quest-startup-content.json"));
                if (receipt == null || receipt.schema != 1 || content == null
                    || receipt.inputKey != content.inputKey || receipt.archiveSha256 != content.archiveSha256)
                    throw new InvalidDataException("Native content packer lost the exact content identity.");
                Debug.Log("[Quest content build] " + (receipt.reused ? "reused verified unchanged archive" : "packed native bundles without recompression")
                    + " files=" + receipt.fileCount + " native-bundles=" + receipt.nativeBundleCount);
            }
            finally { if (File.Exists(request)) File.Delete(request); }
        }

        static string QuoteArgument(string value)
        {
            if (value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new InvalidDataException("Content packer argument contains a control character.");
            // ProcessStartInfo arguments use literal quoting, never a shell.
            // Double backslashes only before quotes and the closing quote.
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { ++slashes; continue; }
                result.Append('\\', character == '\"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('\"').ToString();
        }
    }
}
#endif
