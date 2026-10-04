#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Validates imported original Campaign objects and all native scene closures.</summary>
    public static class QuestCampaignAssetValidation
    {
        public const string InputPath = "Assets/QuestOriginalCampaign/campaign-addressables.json";
        public const string SceneInputPath = "Assets/QuestOriginalCampaign/campaign-scenes.json";
        public const string ReceiptPath = "QuestCampaignEvidence/asset-import.json";

        [Serializable] public sealed class Association
        {
            public string assetPath, recoveredGuid, resourceTypeName, status, originalCollection, sourceBundle;
            public string associationProof, subObjectName;
            public long originalPathId, nativeFileId, recoveredFileId;
            public int originalLocationIndex;
        }
        [Serializable] public sealed class Associations
        {
            public int schema, unresolvedEntryCount, associatedEntryCount, serializedValueLocationCount;
            public string catalogSha256, association;
            public Association[] entries;
        }
        [Serializable] public sealed class OriginalScene
        {
            public int index;
            public string path, guid, originalCollection;
        }
        [Serializable] public sealed class Scenes
        {
            public int schema;
            public OriginalScene[] scenes;
        }
        [Serializable] public sealed class ImportedObject
        {
            public string path, guid, type, originalCollection;
            public long localFileId, originalPathId;
            public int originalLocationIndex;
        }
        [Serializable] public sealed class SceneClosure
        {
            public int index, rootCount, gameObjectCount, componentCount;
            public string path, guid;
            public string[] dependencies;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1, importedObjectCount, serializedValueLocationCount;
            public string unityVersion, catalogSha256, sourceManifestSha256, sceneManifestSha256;
            public bool androidAssetsBuilt, allNativeScenesImported, typedOriginalObjectsImported;
            public bool androidPlayerBuilt, playableCampaignVerified, faithfulGraphicsVerified;
            public ImportedObject[] objects;
            public SceneClosure[] scenes;
        }

        public static void Validate() { Validate(false); }
        public static void ValidateAfterAndroidBuild() { Validate(true); }

        public static void Validate(bool androidAssetsBuilt)
        {
            if (File.Exists(ReceiptPath)) File.Delete(ReceiptPath);
            var input = Read<Associations>(InputPath);
            var sceneInput = Read<Scenes>(SceneInputPath);
            if (input.schema != 1 || input.entries == null || input.unresolvedEntryCount != 0 ||
                input.association != "captured-original-UnityFS-CAB-and-serialized-pathID" ||
                sceneInput.schema != 1 || sceneInput.scenes == null || sceneInput.scenes.Length != 13)
                throw new InvalidDataException("Full Campaign source closure is incomplete.");
            var imported = new List<ImportedObject>();
            var nativeIdentities = new HashSet<string>(StringComparer.Ordinal);
            var objectCache = new Dictionary<string, UnityEngine.Object[]>(StringComparer.Ordinal);
            foreach (var row in input.entries)
            {
                if (row.status == "serialized-value-location-excluded") continue;
                if (row.status != "associated" || string.IsNullOrEmpty(row.associationProof) ||
                    string.IsNullOrEmpty(row.originalCollection) || row.originalPathId == 0 ||
                    row.nativeFileId == 0 || row.nativeFileId != row.recoveredFileId)
                    throw new InvalidDataException("Campaign catalog has an unproven original object.");
                SafeAsset(row.assetPath);
                if (AssetDatabase.AssetPathToGUID(row.assetPath) != row.recoveredGuid ||
                    AssetDatabase.GUIDToAssetPath(row.recoveredGuid) != row.assetPath)
                    throw new InvalidDataException("Imported Campaign GUID differs: " + row.assetPath);
                UnityEngine.Object[] values;
                if (!objectCache.TryGetValue(row.assetPath, out values))
                {
                    values = AssetDatabase.LoadAllAssetsAtPath(row.assetPath);
                    if (values == null || values.Length == 0)
                        throw new InvalidDataException("Campaign native asset failed import: " + row.assetPath);
                    objectCache.Add(row.assetPath, values);
                }
                var typeName = row.resourceTypeName.Split(',')[0].Trim();
                var matches = values.Where(value => value != null && NativeType(value, typeName) &&
                    (string.IsNullOrEmpty(row.subObjectName) || value.name == row.subObjectName)).ToArray();
                UnityEngine.Object selected = null;
                foreach (var value in matches)
                {
                    string guid; long fileId;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out guid, out fileId) &&
                        guid == row.recoveredGuid && fileId == row.nativeFileId)
                    {
                        if (selected != null) throw new InvalidDataException("Duplicate imported original local ID.");
                        selected = value;
                    }
                }
                if (selected == null)
                    throw new InvalidDataException("Original typed/local object identity failed import: " +
                        row.assetPath + " " + typeName + " " + row.nativeFileId);
                RequireSerializedReferences(selected, row.assetPath);
                var material = selected as Material;
                if (material != null && (material.shader == null ||
                    material.shader.name == "Hidden/InternalErrorShader"))
                    throw new InvalidDataException("Original material has no usable imported shader: " + row.assetPath);
                var identity = row.originalCollection + ":" + row.originalPathId + ":" + row.originalLocationIndex;
                if (!nativeIdentities.Add(identity))
                    throw new InvalidDataException("Duplicate captured Campaign catalog identity: " + identity);
                imported.Add(new ImportedObject { path = row.assetPath, guid = row.recoveredGuid,
                    type = selected.GetType().FullName, localFileId = row.nativeFileId,
                    originalCollection = row.originalCollection, originalPathId = row.originalPathId,
                    originalLocationIndex = row.originalLocationIndex });
            }
            if (imported.Count != input.associatedEntryCount)
                throw new InvalidDataException("Campaign typed-object inventory count differs.");
            var closures = new List<SceneClosure>();
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var original in sceneInput.scenes.OrderBy(scene => scene.index))
                {
                    if (original.index != closures.Count || original.originalCollection != "level" + original.index)
                        throw new InvalidDataException("Original build scene order/native collection differs.");
                    SafeAsset(original.path);
                    if (AssetDatabase.AssetPathToGUID(original.path) != original.guid ||
                        AssetDatabase.LoadAssetAtPath<SceneAsset>(original.path) == null)
                        throw new InvalidDataException("Original Campaign scene failed import: " + original.path);
                    var scene = EditorSceneManager.OpenScene(original.path, OpenSceneMode.Single);
                    var roots = scene.GetRootGameObjects();
                    var gameObjects = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                        .Select(transform => transform.gameObject).ToArray();
                    int componentCount = 0;
                    foreach (var go in gameObjects)
                    {
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) != 0)
                            throw new InvalidDataException("Original scene has missing gameplay script: " + original.path + "/" + go.name);
                        foreach (var component in go.GetComponents<Component>())
                        {
                            if (component == null) throw new InvalidDataException("Original scene contains a missing component.");
                            componentCount++;
                            RequireSerializedReferences(component, original.path + "/" + go.name);
                        }
                    }
                    string[] dependencies = AssetDatabase.GetDependencies(original.path, true)
                        .OrderBy(path => path, StringComparer.Ordinal).ToArray();
                    foreach (string dependency in dependencies)
                    {
                        if (dependency.StartsWith("Assets/", StringComparison.Ordinal) &&
                            (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(dependency)) ||
                             AssetDatabase.LoadMainAssetAtPath(dependency) == null))
                            throw new InvalidDataException("Original scene dependency failed import: " + dependency);
                    }
                    closures.Add(new SceneClosure { index = original.index, path = original.path,
                        guid = original.guid, rootCount = roots.Length, gameObjectCount = gameObjects.Length,
                        componentCount = componentCount, dependencies = dependencies });
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            File.WriteAllText(ReceiptPath, JsonUtility.ToJson(new Receipt { unityVersion = Application.unityVersion,
                catalogSha256 = input.catalogSha256, sourceManifestSha256 = Hash(InputPath),
                sceneManifestSha256 = Hash(SceneInputPath), androidAssetsBuilt = androidAssetsBuilt,
                importedObjectCount = imported.Count, serializedValueLocationCount = input.serializedValueLocationCount,
                typedOriginalObjectsImported = true, allNativeScenesImported = true,
                objects = imported.ToArray(), scenes = closures.ToArray() }, true), new UTF8Encoding(false));
            UnityEngine.Debug.Log("[Quest Campaign] Imported " + imported.Count + " exact original typed objects and all 13 native scene closures.");
        }

        private static bool NativeType(UnityEngine.Object value, string requested)
        {
            for (var type = value.GetType(); type != null; type = type.BaseType)
                if (type.FullName == requested) return true;
            return false;
        }

        private static void RequireSerializedReferences(UnityEngine.Object value, string path)
        {
            var serialized = new SerializedObject(value);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                    property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                    throw new InvalidDataException("Original imported object lost reference: " + path + " " + property.propertyPath);
        }

        private static T Read<T>(string path) where T : class
        {
            SafeAsset(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 128 * 1024 * 1024)
                throw new InvalidDataException("Campaign validation manifest missing or oversized: " + path);
            var value = JsonUtility.FromJson<T>(File.ReadAllText(path));
            if (value == null) throw new InvalidDataException("Invalid Campaign validation manifest: " + path);
            return value;
        }

        private static void SafeAsset(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !Path.GetFullPath(path).StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Campaign asset path escaped generated project.");
        }

        private static string Hash(string path)
        {
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
