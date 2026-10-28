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
using UnityEngine.U2D;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Validates imported original Campaign objects and all native scene closures.</summary>
    public static class QuestCampaignAssetValidation
    {
        public const string InputPath = "Assets/QuestOriginalCampaign/campaign-addressables.json";
        public const string SceneInputPath = "Assets/QuestOriginalCampaign/campaign-scenes.json";
        public const string PackedSpriteInputPath = "Assets/QuestOriginalCampaign/packed-sprites.json";
        public const string BundledAudioInputPath = "Assets/QuestOriginalCampaign/bundled-audio.json";
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
            public int schema = 1, importedObjectCount, serializedValueLocationCount, packedSpriteCount, packedAtlasCount, bundledAudioClipCount;
            public string unityVersion, catalogSha256, sourceManifestSha256, sceneManifestSha256;
            public bool androidAssetsBuilt, allNativeScenesImported, typedOriginalObjectsImported;
            public bool androidPlayerBuilt, playableCampaignVerified, faithfulGraphicsVerified, packedSpriteDrawingStateImported;
            public ImportedObject[] objects;
            public SceneClosure[] scenes;
        }
        [Serializable] public sealed class NativeRectangle { public float x, y, width, height; }
        [Serializable] public sealed class PackedSprite
        {
            public string assetPath, guid, textureGuid;
            public int textureWidth, textureHeight;
            public NativeRectangle rect;
            public Vector2 pivot;
            public Vector2[] vertices, uv;
        }
        [Serializable] public sealed class PackedAtlas
        {
            public string assetPath, guid;
            public int spriteCount;
        }
        [Serializable] public sealed class PackedSprites
        {
            public int schema, spriteCount;
            public PackedAtlas[] atlases;
            public PackedSprite[] sprites;
        }
        [Serializable] public sealed class BundledAudioClip
        {
            public string assetPath, guid, sha256;
            public int channels, frequency, samples;
            public long fileId;
            public bool compressedAudioPacketsPreserved;
        }
        [Serializable] public sealed class BundledAudio
        {
            public int schema, bundledAudioClipCount;
            public BundledAudioClip[] assets;
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
            var progress = new QuestWizardProgress.Counter("unity-campaign-objects", androidAssetsBuilt ? "content-bank" : "unity-validation", input.entries.Length, "assets", "Validate original typed Campaign objects");
            int examined = 0;
            foreach (var row in input.entries)
            {
                progress.Report(examined, row.assetPath);
                if (row.status == "serialized-value-location-excluded")
                { progress.Report(++examined, row.assetPath); continue; }
                // Previous associations have already been checked and recorded as
                // plain receipts. Release their native prefab/texture closures
                // before loading the next bounded group on the player's PC.
                if (imported.Count != 0 && imported.Count % 128 == 0 && objectCache.Count != 0)
                {
                    objectCache.Clear();
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
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
                progress.Report(++examined, row.assetPath);
            }
            progress.Complete("Original typed Campaign objects validated");
            if (imported.Count != input.associatedEntryCount)
                throw new InvalidDataException("Campaign typed-object inventory count differs.");
            objectCache.Clear();
            EditorUtility.UnloadUnusedAssetsImmediate();
            var packed = ValidatePackedSprites();
            var audio = ValidateBundledAudio();
            var closures = new List<SceneClosure>();
            var previous = EditorSceneManager.GetSceneManagerSetup();
            Exception validationError = null;
            try
            {
                var sceneProgress = new QuestWizardProgress.Counter("unity-campaign-scenes", androidAssetsBuilt ? "content-bank" : "unity-validation", sceneInput.scenes.Length, "scenes", "Validate original Campaign scene closures");
                foreach (var original in sceneInput.scenes.OrderBy(scene => scene.index))
                {
                    sceneProgress.Report(closures.Count, original.path);
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
                    var objectsProgress = new QuestWizardProgress.Counter("unity-scene-objects", androidAssetsBuilt ? "content-bank" : "unity-validation", gameObjects.Length, "objects", original.path);
                    int objectsDone = 0;
                    int componentCount = 0;
                    foreach (var go in gameObjects)
                    {
                        objectsProgress.Report(objectsDone, original.path + "/" + go.name);
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) != 0)
                            throw new InvalidDataException("Original scene has missing gameplay script: " + original.path + "/" + go.name);
                        foreach (var component in go.GetComponents<Component>())
                        {
                            if (component == null) throw new InvalidDataException("Original scene contains a missing component.");
                            componentCount++;
                            RequireSerializedReferences(component, original.path + "/" + go.name);
                        }
                        objectsProgress.Report(++objectsDone, original.path + "/" + go.name);
                    }
                    objectsProgress.Complete("Original scene objects validated: " + original.path);
                    string[] dependencies = AssetDatabase.GetDependencies(original.path, true)
                        .OrderBy(path => path, StringComparer.Ordinal).ToArray();
                    var dependencyProgress = new QuestWizardProgress.Counter("unity-scene-dependencies", androidAssetsBuilt ? "content-bank" : "unity-validation", dependencies.Length, "dependencies", original.path);
                    int dependenciesDone = 0;
                    foreach (string dependency in dependencies)
                    {
                        dependencyProgress.Report(dependenciesDone, dependency);
                        if (dependency.StartsWith("Assets/", StringComparison.Ordinal) &&
                            (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(dependency)) ||
                             AssetDatabase.LoadMainAssetAtPath(dependency) == null))
                            throw new InvalidDataException("Original scene dependency failed import: " + dependency);
                        dependencyProgress.Report(++dependenciesDone, dependency);
                    }
                    dependencyProgress.Complete("Original scene dependencies validated: " + original.path);
                    closures.Add(new SceneClosure { index = original.index, path = original.path,
                        guid = original.guid, rootCount = roots.Length, gameObjectCount = gameObjects.Length,
                        componentCount = componentCount, dependencies = dependencies });
                    sceneProgress.Report(closures.Count, original.path);
                }
                sceneProgress.Complete("All original Campaign scene closures validated");
            }
            catch (Exception error)
            {
                validationError = error;
                throw;
            }
            finally
            {
                try { RestoreSceneSetup(previous); }
                catch (Exception cleanupError)
                {
                    if (validationError == null) throw;
                    // Keep the actual content failure as the thrown exception;
                    // scene cleanup is secondary evidence, never its replacement.
                    UnityEngine.Debug.LogError("[Quest Campaign] Scene cleanup failed after validation failure: " + cleanupError);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            File.WriteAllText(ReceiptPath, JsonUtility.ToJson(new Receipt { unityVersion = Application.unityVersion,
                catalogSha256 = input.catalogSha256, sourceManifestSha256 = Hash(InputPath),
                sceneManifestSha256 = Hash(SceneInputPath), androidAssetsBuilt = androidAssetsBuilt,
                importedObjectCount = imported.Count, serializedValueLocationCount = input.serializedValueLocationCount,
                packedSpriteCount = packed.spriteCount, packedAtlasCount = packed.atlases.Length,
                bundledAudioClipCount = audio.bundledAudioClipCount,
                packedSpriteDrawingStateImported = true,
                typedOriginalObjectsImported = true, allNativeScenesImported = true,
                objects = imported.ToArray(), scenes = closures.ToArray() }, true), new UTF8Encoding(false));
            UnityEngine.Debug.Log("[Quest Campaign] Imported " + imported.Count + " exact original typed objects and all 13 native scene closures.");
        }

        private static void RestoreSceneSetup(SceneSetup[] previous)
        {
            // The first full batch build opened all 13 scenes successfully, then
            // Unity rejected its original empty setup: there was no loaded active
            // scene to restore. A new empty Editor scene is the valid equivalent
            // of that initial state. Preserve an actual loaded/active setup through
            // Unity's normal restoration, including additive and unloaded scenes.
            if (previous != null && previous.Any(scene => scene.isLoaded && scene.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static BundledAudio ValidateBundledAudio()
        {
            var input = Read<BundledAudio>(BundledAudioInputPath);
            if (input.schema != 1 || input.assets == null || input.bundledAudioClipCount != input.assets.Length)
                throw new InvalidDataException("Original bundled-audio inventory is incomplete.");
            var progress = new QuestWizardProgress.Counter("unity-campaign-audio", null, input.assets.Length, "assets", "Validate original bundled audio identities");
            int done = 0;
            foreach (var row in input.assets)
            {
                progress.Report(done, row.assetPath);
                SafeAsset(row.assetPath);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(row.assetPath);
                string guid; long localId;
                if (!row.compressedAudioPacketsPreserved || Hash(row.assetPath) != row.sha256 || clip == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out guid, out localId) ||
                    guid != row.guid || localId != row.fileId || clip.channels != row.channels ||
                    clip.frequency != row.frequency || Math.Abs((long)clip.samples - row.samples) > 2)
                    throw new InvalidDataException("Original bundled AudioClip channel/sample identity failed import: " + row.assetPath);
                progress.Report(++done, row.assetPath);
            }
            progress.Complete("Original bundled audio identities validated");
            return input;
        }

        private static PackedSprites ValidatePackedSprites()
        {
            var input = Read<PackedSprites>(PackedSpriteInputPath);
            if (input.schema != 1 || input.atlases == null || input.sprites == null ||
                input.spriteCount != input.sprites.Length)
                throw new InvalidDataException("Original packed-Sprite source inventory is incomplete.");
            var atlasProgress = new QuestWizardProgress.Counter("unity-campaign-atlases", null, input.atlases.Length, "assets", "Validate original packed Sprite atlases");
            int atlasesDone = 0;
            foreach (var row in input.atlases)
            {
                atlasProgress.Report(atlasesDone, row.assetPath);
                SafeAsset(row.assetPath);
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(row.assetPath);
                if (AssetDatabase.AssetPathToGUID(row.assetPath) != row.guid || atlas == null || atlas.spriteCount != row.spriteCount)
                    throw new InvalidDataException("Original native packed atlas failed import: " + row.assetPath);
                var values = new Sprite[row.spriteCount];
                if (atlas.GetSprites(values) != row.spriteCount || values.Any(value => value == null || value.texture == null))
                    throw new InvalidDataException("Original native packed atlas lost a drawable member: " + row.assetPath);
                atlasProgress.Report(++atlasesDone, row.assetPath);
            }
            atlasProgress.Complete("Original packed Sprite atlases validated");
            var spriteProgress = new QuestWizardProgress.Counter("unity-campaign-packed-sprites", null, input.sprites.Length, "sprites", "Validate original packed Sprite geometry");
            int spritesDone = 0;
            foreach (var row in input.sprites)
            {
                spriteProgress.Report(spritesDone, row.assetPath);
                SafeAsset(row.assetPath);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(row.assetPath);
                if (sprite == null || AssetDatabase.AssetPathToGUID(row.assetPath) != row.guid || sprite.texture == null ||
                    sprite.texture.width != row.textureWidth || sprite.texture.height != row.textureHeight ||
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture)) != row.textureGuid || row.rect == null)
                    throw new InvalidDataException("Original packed-Sprite texture binding failed import: " + row.assetPath);
                var rect = sprite.rect;
                if (rect.x != row.rect.x || rect.y != row.rect.y || rect.width != row.rect.width || rect.height != row.rect.height ||
                    sprite.pivot.x != row.pivot.x * rect.width || sprite.pivot.y != row.pivot.y * rect.height)
                    throw new InvalidDataException("Original packed-Sprite rectangle/pivot differs: " + row.assetPath);
                RequireVectors(sprite.vertices, row.vertices, row.assetPath + " vertices");
                RequireVectors(sprite.uv, row.uv, row.assetPath + " UV");
                spriteProgress.Report(++spritesDone, row.assetPath);
            }
            spriteProgress.Complete("Original packed Sprite geometry validated");
            return input;
        }

        private static void RequireVectors(Vector2[] imported, Vector2[] original, string source)
        {
            if (imported == null || original == null || imported.Length != original.Length)
                throw new InvalidDataException("Original packed-Sprite stream length differs: " + source);
            for (int index = 0; index < imported.Length; index++)
                if (Math.Abs(imported[index].x - original[index].x) > 0.0000001f ||
                    Math.Abs(imported[index].y - original[index].y) > 0.0000001f)
                    throw new InvalidDataException("Original packed-Sprite drawing stream differs: " + source + " " + index);
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
