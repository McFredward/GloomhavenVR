#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Sprites;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Verify the imported native spinner drawing geometry and original promotion art.</summary>
    public static class QuestSpriteGeometryValidation
    {
        public const string InputPath = "QuestStartupEvidence/loading-sprite-geometry.json";
        public const string ReceiptPath = "QuestStartupEvidence/loading-sprite-import.json";

        [Serializable] public sealed class Rectangle
        {
            public float x, y, width, height;
        }
        [Serializable] public sealed class Pair
        {
            public float x, y;
        }
        [Serializable] public sealed class SourceAsset
        {
            public string name = "", asset = "", guid = "", metaSha256 = "", restoredSha256 = "";
            public long sourcePathId;
            public Rectangle restoredAtlasRect = new Rectangle(), textureCrop = new Rectangle();
            public Pair trimOffset = new Pair(), pivot = new Pair();
        }
        [Serializable] public sealed class SourceReceipt
        {
            public int schema;
            public string sourceSha256 = "";
            public SourceAsset[] assets = Array.Empty<SourceAsset>();
        }
        [Serializable] public sealed class ImportedSprite
        {
            public string assetPath = "", guid = "", sourceSha256 = "", metaSha256 = "";
            public string texturePath = "", textureGuid = "";
            public Rect rect, textureRect;
            public Vector2 pivot;
            public Vector4 padding, outerUV;
            public bool originalDrawingGeometryVerified;
        }
        [Serializable] public sealed class ValidationReceipt
        {
            public int schema = 1;
            public string unityVersion = "", sourceReceiptSha256 = "";
            public ImportedSprite[] spinner = Array.Empty<ImportedSprite>(), promotions = Array.Empty<ImportedSprite>();
            public bool allImportedAssetsVerified;
            public bool finalHeadsetPictureVerified;
        }

        public static void ValidateStartupAssets()
        {
            if (!File.Exists(InputPath))
                throw new InvalidOperationException("Owned loading sprite geometry receipt is missing.");
            var source = JsonUtility.FromJson<SourceReceipt>(File.ReadAllText(InputPath));
            if (source == null || source.schema != 1 || source.assets == null || source.assets.Length < 2
                || source.sourceSha256.Length != 64)
                throw new InvalidOperationException("Owned loading sprite geometry receipt is invalid: schema="
                    + (source == null ? "null" : source.schema.ToString()) + ", assets="
                    + (source == null || source.assets == null ? "null" : source.assets.Length.ToString()) + ", source hash length="
                    + (source == null || source.sourceSha256 == null ? "null" : source.sourceSha256.Length.ToString()));
            var spinner = new List<ImportedSprite>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var progress = new QuestWizardProgress.Counter("unity-validation-sprites", "unity-validation",
                source.assets.Length + 2, "sprites", "Loading and promotion sprites");
            int completed = 0;
            foreach (var entry in source.assets)
            {
                if (entry == null || (entry.name != "LoadingBase" && entry.name != "LoadingOverlay")
                    || !entry.asset.StartsWith("Assets/Sprite/Loading", StringComparison.Ordinal)
                    || entry.asset.Contains("..") || !identities.Add(entry.guid) || entry.sourcePathId <= 0)
                    throw new InvalidOperationException("Original loading sprite identity is invalid or duplicated.");
                progress.Report(completed, entry.asset);
                ImportedSprite imported = Read(entry.asset, entry.guid);
                if (imported.sourceSha256 != entry.restoredSha256 || imported.metaSha256 != entry.metaSha256)
                    throw new InvalidOperationException("Restored loading sprite source changed before import: " + entry.asset);
                Rect rect = imported.rect, crop = imported.textureRect;
                Close(rect.x, entry.restoredAtlasRect.x, entry.asset);
                Close(rect.y, entry.restoredAtlasRect.y, entry.asset);
                Close(rect.width, entry.restoredAtlasRect.width, entry.asset);
                Close(rect.height, entry.restoredAtlasRect.height, entry.asset);
                Close(crop.x, entry.textureCrop.x, entry.asset);
                Close(crop.y, entry.textureCrop.y, entry.asset);
                Close(crop.width, entry.textureCrop.width, entry.asset);
                Close(crop.height, entry.textureCrop.height, entry.asset);
                Close(imported.pivot.x, rect.width * entry.pivot.x, entry.asset);
                Close(imported.pivot.y, rect.height * entry.pivot.y, entry.asset);
                Close(imported.padding.x, entry.trimOffset.x, entry.asset);
                Close(imported.padding.y, entry.trimOffset.y, entry.asset);
                Close(imported.padding.z, rect.width - entry.trimOffset.x - crop.width, entry.asset);
                Close(imported.padding.w, rect.height - entry.trimOffset.y - crop.height, entry.asset);
                imported.originalDrawingGeometryVerified = true;
                spinner.Add(imported); names.Add(entry.name);
                progress.Report(++completed, entry.asset);
            }
            if (!names.SetEquals(new[] { "LoadingBase", "LoadingOverlay" }))
                throw new InvalidOperationException("Original loading sprite receipt lacks an animation layer.");
            string[] promotionPaths = { "Assets/Sprite/DLC_Promo_JawsOfTheLion.asset",
                "Assets/Sprite/DLC_Promo_SoloScenarios_0.asset" };
            var promotions = new ImportedSprite[promotionPaths.Length];
            for (int index = 0; index < promotionPaths.Length; index++)
            {
                progress.Report(completed, promotionPaths[index]);
                promotions[index] = ReadPromotion(promotionPaths[index]);
                progress.Report(++completed, promotionPaths[index]);
            }
            var receipt = new ValidationReceipt {
                unityVersion = Application.unityVersion, sourceReceiptSha256 = Hash(InputPath),
                spinner = spinner.ToArray(), promotions = promotions, allImportedAssetsVerified = true,
                finalHeadsetPictureVerified = false
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            File.WriteAllText(ReceiptPath, JsonUtility.ToJson(receipt, true) + "\n");
            progress.Complete("Loading and promotion sprite import verified");
            Debug.Log("Quest original sprite import verified: spinner=" + spinner.Count + ", promotions=" + promotions.Length);
        }

        private static ImportedSprite ReadPromotion(string path)
        {
            // Capture200517 reaches this gate after a successful first import.
            // AssetRipper assigns a new GUID to each independent export. The
            // former literals came from our local export and cannot identify a
            // player's Windows/GOG/Epic export. Preparation already qualifies
            // these owned .meta bytes; retain their current export identity and
            // require Unity to import that same identity. Campaign validation
            // separately verifies native collection/pathID drawing provenance.
            string metadata = path + ".meta";
            if (!File.Exists(metadata) || new FileInfo(metadata).Length > 16384)
                throw new InvalidOperationException("Original promotion sprite metadata is missing or oversized: " + path);
            var matches = Regex.Matches(File.ReadAllText(metadata), @"^guid:[ \t]*([0-9a-f]{32})[ \t]*\r?$", RegexOptions.Multiline);
            if (matches.Count != 1 || matches[0].Groups[1].Value == new string('0', 32))
                throw new InvalidOperationException("Original promotion sprite metadata has no unique export GUID: " + path);
            return Read(path, matches[0].Groups[1].Value);
        }

        private static ImportedSprite Read(string path, string guid)
        {
            string importedGuid = AssetDatabase.AssetPathToGUID(path);
            if (importedGuid != guid)
                throw new InvalidOperationException("Original sprite GUID does not match its imported asset: " + path
                    + "; exported=" + guid + "; imported=" + importedGuid);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null || sprite.texture == null)
                throw new InvalidOperationException("Original sprite or texture failed to import: " + path);
            Vector4 uv = DataUtility.GetOuterUV(sprite);
            if (!Finite(uv.x) || !Finite(uv.y) || !Finite(uv.z) || !Finite(uv.w)
                || uv.x < -0.001f || uv.y < -0.001f || uv.z > 1.001f || uv.w > 1.001f || uv.z <= uv.x || uv.w <= uv.y)
                throw new InvalidOperationException("Original sprite has invalid native outer UVs: " + path);
            var texturePath = AssetDatabase.GetAssetPath(sprite.texture);
            if (String.IsNullOrEmpty(texturePath) || sprite.textureRect.width <= 0 || sprite.textureRect.height <= 0)
                throw new InvalidOperationException("Original sprite has no sampled native texture crop: " + path);
            return new ImportedSprite {
                assetPath = path, guid = guid, sourceSha256 = Hash(path), metaSha256 = Hash(path + ".meta"),
                texturePath = texturePath, textureGuid = AssetDatabase.AssetPathToGUID(texturePath),
                rect = sprite.rect, textureRect = sprite.textureRect, pivot = sprite.pivot,
                padding = DataUtility.GetPadding(sprite), outerUV = uv
            };
        }
        private static bool Finite(float value) => !Single.IsNaN(value) && !Single.IsInfinity(value);
        private static void Close(float actual, float expected, string path)
        {
            if (!Finite(actual) || !Finite(expected) || Math.Abs(actual - expected) > 0.003f)
                throw new InvalidOperationException("Imported original loading sprite geometry differs from source: " + path);
        }
        private static string Hash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
