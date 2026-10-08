#if GHVR_QUEST_GAME && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Remove unsupported native light/shadow products, never replace original programs.</summary>
    public sealed class QuestCampaignShaderStrip : IPreprocessShaders
    {
        const string DefaultManifest = "Assets/QuestOriginalCampaign/campaign-shaders.json";
        [Serializable] public sealed class Bank { public string passType; public string[] keywords; }
        [Serializable] public sealed class OriginalShader
        {
            public string guid, assetPath, originalName, sourceRestoration;
            public Bank[] variants;
            [NonSerialized] public HashSet<string> forwardAddLighting;
        }
        [Serializable] public sealed class Manifest
        {
            public int schema, requiredShaderCount;
            public string scope, graphicsApi;
            public OriginalShader[] shaders;
        }
        Dictionary<string, OriginalShader> owned;
        string manifestPath;
        long manifestLength, manifestWriteTicks;
        public int callbackOrder { get { return 0; } }
        public static int RemovedCount { get; private set; }

        static readonly HashSet<string> LightingKeys = new HashSet<string>(new[] {
            "DIRECTIONAL", "DIRECTIONAL_COOKIE", "POINT", "POINT_COOKIE", "SPOT",
            "SHADOWS_CUBE", "SHADOWS_DEPTH", "SHADOWS_SCREEN", "SHADOWS_SOFT"
        }, StringComparer.Ordinal);

        public static string LightingProjection(IEnumerable<string> keywords)
        {
            return string.Join(" ", keywords.Where(LightingKeys.Contains).Distinct().OrderBy(key => key, StringComparer.Ordinal));
        }

        // The reconstructed original pragmas form a Cartesian light/shadow
        // product, while the original DXBC contains a finite subset. The actual
        // Android Player exposed DIRECTIONAL+SHADOWS_CUBE first; also exclude
        // other absent light/shadow products before the same compiler boundary.
        // Project only these nine native engine keywords: authored features,
        // instancing and Unity-added XR state keep their original meanings.
        public static bool RejectLightingProduct(OriginalShader row, PassType pass, IEnumerable<string> keywords)
        {
            if (pass != PassType.ForwardAdd) return false;
            if (row == null || row.forwardAddLighting == null)
                throw new InvalidDataException("Native light/shadow ownership was not audited.");
            return !row.forwardAddLighting.Contains(LightingProjection(keywords));
        }

        public static Dictionary<string, OriginalShader> Audit(Manifest manifest)
        {
            if (manifest == null || manifest.schema != 1 || manifest.scope != "campaign-compiler" ||
                manifest.graphicsApi != "Vulkan" || manifest.shaders == null || manifest.shaders.Length == 0 ||
                manifest.requiredShaderCount != manifest.shaders.Length)
                throw new InvalidDataException("Original shader keyword-strip ownership is incomplete.");
            var result = new Dictionary<string, OriginalShader>(StringComparer.Ordinal);
            var allGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in manifest.shaders)
            {
                if (row == null || !Regex.IsMatch(row.guid ?? "", "^[0-9a-f]{32}$") || !allGuids.Add(row.guid) ||
                    string.IsNullOrEmpty(row.originalName) || string.IsNullOrEmpty(row.assetPath) ||
                    !row.assetPath.StartsWith("Assets/", StringComparison.Ordinal) || row.variants == null || row.variants.Length == 0)
                    throw new InvalidDataException("Original shader keyword-strip identity is invalid.");
                foreach (var bank in row.variants)
                {
                    PassType pass;
                    if (bank == null || bank.keywords == null || !Enum.TryParse(bank.passType, out pass) ||
                        bank.keywords.Any(key => !Regex.IsMatch(key ?? "", "^[A-Za-z0-9_]+$")))
                        throw new InvalidDataException("Original shader keyword-strip bank is invalid.");
                    if (pass == PassType.ForwardAdd && bank.keywords.Contains("DIRECTIONAL") && bank.keywords.Contains("SHADOWS_CUBE"))
                        throw new InvalidDataException("Keyword removal would discard an original native shader bank.");
                }
                if (row.sourceRestoration == "exact-original-dxbc" && row.variants.Any(bank => bank.passType == "ForwardAdd"))
                {
                    row.forwardAddLighting = new HashSet<string>(row.variants.Where(bank => bank.passType == "ForwardAdd")
                        .Select(bank => LightingProjection(bank.keywords)), StringComparer.Ordinal);
                    result.Add(row.guid, row);
                }
            }
            return result;
        }

        void ReadOwnership()
        {
            string path = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST") ?? DefaultManifest;
            var file = new FileInfo(path);
            if (owned != null)
            {
                if (path != manifestPath || !file.Exists || file.Length != manifestLength || file.LastWriteTimeUtc.Ticks != manifestWriteTicks)
                    throw new InvalidDataException("Original shader keyword ownership changed during native compilation.");
                return;
            }
            owned = Audit(JsonUtility.FromJson<Manifest>(File.ReadAllText(path)));
            manifestPath = path; manifestLength = file.Length; manifestWriteTicks = file.LastWriteTimeUtc.Ticks;
            RemovedCount = 0;
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (snippet.passType != PassType.ForwardAdd || !data.Any(bank => bank.shaderCompilerPlatform == ShaderCompilerPlatform.Vulkan)) return;
            ReadOwnership();
            string path = AssetDatabase.GetAssetPath(shader), guid = AssetDatabase.AssetPathToGUID(path);
            OriginalShader row;
            if (!owned.TryGetValue(guid, out row)) return;
            if (row.assetPath != path || row.originalName != shader.name)
                throw new InvalidDataException("Original shader keyword-strip GUID changes its imported identity.");
            int removed = 0;
            for (int index = data.Count - 1; index >= 0; --index)
                if (data[index].shaderCompilerPlatform == ShaderCompilerPlatform.Vulkan &&
                    RejectLightingProduct(row, snippet.passType, data[index].shaderKeywordSet.GetShaderKeywords().Select(key => key.name)))
                {
                    data.RemoveAt(index); ++removed;
                }
            if (removed != 0)
            {
                RemovedCount += removed;
                Debug.Log("[Quest Campaign] Excluded unsupported original light/shadow compiler products: guid=" + guid +
                    " pass=" + snippet.passName + " stage=" + snippet.shaderType + " count=" + removed + ". Original native banks retained.");
            }
        }
    }
}
#endif
