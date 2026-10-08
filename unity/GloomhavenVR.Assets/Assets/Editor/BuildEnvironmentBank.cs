using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    /// <summary>Independent bank; never repack the established main/town asset banks as a side effect.
    /// Original environment derivatives were prepared offline from immutable publisher sources.
    /// Use the game-exact 2021.3.5f1 editor, including when building in a private temporary project.
    /// </summary>
    public static class EnvironmentBankBuilder
    {
        public static void BuildAll()
        {
            try
            {
                const string root = "Assets/Bundle/EnvironmentMeshes";
                const string shader = "Assets/Bundle/Environments/ScenarioCheapTerrain.shader";
                const string worldShader = "Assets/Bundle/Environments/WorldSimpleMaterial.shader";
                const string output = "Build/Environment";
                if (Application.unityVersion != "2021.3.5f1")
                    throw new InvalidOperationException("Environment banks require Unity 2021.3.5f1.");
                // A minimal private project can compile these APIs while the
                // target module remains disabled. Unity then reports success
                // but omits the AssetBundle container and its loadable paths.
                if (!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Any(package => package.name == "com.unity.modules.assetbundle"))
                    throw new InvalidOperationException("Enable com.unity.modules.assetbundle before packing environment banks.");
                if (!File.Exists(root + "/index.json") || !File.Exists(shader) || !File.Exists(worldShader))
                    throw new FileNotFoundException("Generate environment meshes and both environment shaders before packing.");
                VerifyPreparedIndex(root);
                string[] assets = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Select(path => path.Replace('\\', '/'))
                    .Where(path => path.EndsWith(".bytes", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal))
                    .Concat(new[] { shader, worldShader }).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                foreach (string asset in assets)
                    if (AssetDatabase.AssetPathToGUID(asset) == string.Empty)
                        throw new InvalidOperationException("Unimported environment asset: " + asset);
                Directory.CreateDirectory(output);
                // The shipped game's globalgamemanagers PlayerSettings is Gamma.
                // UnityCG.ShadeSH9 compiles its conversion under
                // UNITY_COLORSPACE_GAMMA; the companion project's Linear setting
                // must not silently change the independent runtime shader bank.
                ColorSpace previousColorSpace = PlayerSettings.colorSpace;
                var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").Single());
                SerializedProperty instancing = RequiredSetting(graphics, "m_InstancingStripping", SerializedPropertyType.Enum);
                SerializedProperty fog = RequiredSetting(graphics, "m_FogStripping", SerializedPropertyType.Enum);
                SerializedProperty fogLinear = RequiredSetting(graphics, "m_FogKeepLinear", SerializedPropertyType.Boolean);
                SerializedProperty fogExp = RequiredSetting(graphics, "m_FogKeepExp", SerializedPropertyType.Boolean);
                SerializedProperty fogExp2 = RequiredSetting(graphics, "m_FogKeepExp2", SerializedPropertyType.Boolean);
                int previousInstancing = instancing.intValue, previousFog = fog.intValue;
                bool previousLinear = fogLinear.boolValue, previousExp = fogExp.boolValue, previousExp2 = fogExp2.boolValue;
                AssetBundleManifest manifest;
                try
                {
                    PlayerSettings.colorSpace = ColorSpace.Gamma;
                    // These independent bundles contain no scenes or instanced materials.
                    // Automatic stripping otherwise removes the runtime's regular instancing
                    // and fog modes despite the shader's keyword declarations. Resolve the
                    // actual 2021.3.5f1 serialized enum labels, not guessed numeric values.
                    SetEnum(instancing, "Keep All");
                    SetEnum(fog, "Custom");
                    fogLinear.boolValue = fogExp.boolValue = fogExp2.boolValue = true;
                    graphics.ApplyModifiedPropertiesWithoutUndo();
                    manifest = BuildPipeline.BuildAssetBundles(output,
                        new[] { new AssetBundleBuild { assetBundleName = "ghvr-environment.bundle", assetNames = assets } },
                        // Incremental native serialization can retain an invalid
                        // module-stripped bundle after module admission changes.
                        BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
                }
                finally
                {
                    instancing.intValue = previousInstancing; fog.intValue = previousFog;
                    fogLinear.boolValue = previousLinear; fogExp.boolValue = previousExp; fogExp2.boolValue = previousExp2;
                    graphics.ApplyModifiedPropertiesWithoutUndo();
                    PlayerSettings.colorSpace = previousColorSpace;
                }
                if (manifest == null) throw new InvalidOperationException("Environment bank build returned null.");
                long length = new FileInfo(output + "/ghvr-environment.bundle").Length;
                if (length >= 100L * 1024 * 1024) throw new InvalidOperationException("Environment bank exceeds ordinary Git file limit; split it before committing.");
                AssetBundle packed = AssetBundle.LoadFromFile(output + "/ghvr-environment.bundle");
                if (packed == null) throw new InvalidOperationException("Built environment bank cannot load.");
                try
                {
                    if (packed.GetAllAssetNames().Length != assets.Length
                        || !packed.Contains(root + "/index.json") || !packed.Contains(shader) || !packed.Contains(worldShader))
                        throw new InvalidOperationException("Built environment bank has no complete loadable asset container.");
                }
                finally { packed.Unload(true); }
                Debug.Log("[GloomhavenVR] Environment bank OK: " + assets.Length + " assets, " + length + " bytes.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[GloomhavenVR] Environment bank FAILED: " + error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static SerializedProperty RequiredSetting(SerializedObject graphics, string name, SerializedPropertyType type)
        {
            SerializedProperty property = graphics.FindProperty(name);
            if (property == null || property.propertyType != type)
                throw new InvalidOperationException("Required environment shader setting unavailable: " + name);
            return property;
        }

        private static void VerifyPreparedIndex(string root)
        {
            var info = new FileInfo(root + "/index.json");
            if (info.Length > 8 * 1024 * 1024) throw new InvalidDataException("Environment runtime index exceeds its decoder bound.");
            BankIndex index = JsonUtility.FromJson<BankIndex>(File.ReadAllText(info.FullName));
            if (index == null || index.format != 1 || index.entries == null || index.entries.Length < 1 || index.entries.Length > 8192)
                throw new InvalidDataException("Prepared environment index is incomplete.");
            var streams = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (BankEntry entry in index.entries)
            {
                if (entry.key == null || entry.key.Length != 24 || entry.roleEvidenceSha256 == null
                    || entry.roleEvidenceSha256.Length != 64 || (entry.role != "floor" && entry.role != "structure" && entry.role != "none")
                    || entry.variants == null || entry.variants.Length < 1 || entry.variants.Length > 3
                    || !entry.variants.Any(variant => variant.tier == 100))
                    throw new InvalidDataException("Prepared original role/geometry certificate is missing.");
                var tiers = new System.Collections.Generic.HashSet<int>();
                foreach (BankVariant variant in entry.variants)
                {
                    if ((variant.tier != 0 && variant.tier != 50 && variant.tier != 100) || !tiers.Add(variant.tier)
                        || variant.file != entry.key + "-" + variant.tier + ".bytes" || !streams.Add(variant.file))
                        throw new InvalidDataException("Ambiguous environment stream identity.");
                    using (var input = File.OpenRead(root + "/" + variant.file))
                    using (var sha = SHA256.Create())
                        if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", string.Empty).ToLowerInvariant() != variant.sha256)
                            throw new InvalidDataException("Environment source stream changed before packing: " + variant.file);
                }
            }
            if (Directory.GetFiles(root, "*.bytes").Length != streams.Count)
                throw new InvalidDataException("Unindexed environment geometry would enter the shipped bank.");
            Debug.Log("[GloomhavenVR] Certified environment originals: " + index.entries.Length + "; streams " + streams.Count + ".");
        }

        [Serializable] private sealed class BankIndex { public int format = 0; public BankEntry[] entries = Array.Empty<BankEntry>(); }
        [Serializable] private sealed class BankEntry
        { public string key = string.Empty; public string role = string.Empty; public string roleEvidenceSha256 = string.Empty; public BankVariant[] variants = Array.Empty<BankVariant>(); }
        [Serializable] private sealed class BankVariant { public int tier = 0; public string file = string.Empty; public string sha256 = string.Empty; }

        private static void SetEnum(SerializedProperty property, string name)
        {
            int index = Array.IndexOf(property.enumNames, name);
            if (index < 0) throw new InvalidOperationException("Required environment shader enum unavailable: " + property.propertyPath + "/" + name);
            property.enumValueIndex = index;
        }
    }
}
