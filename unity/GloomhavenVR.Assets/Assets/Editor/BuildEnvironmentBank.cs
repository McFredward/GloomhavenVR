using System;
using System.IO;
using System.Linq;
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
                if (!File.Exists(root + "/index.json") || !File.Exists(shader) || !File.Exists(worldShader))
                    throw new FileNotFoundException("Generate environment meshes and both environment shaders before packing.");
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
                        BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
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

        private static void SetEnum(SerializedProperty property, string name)
        {
            int index = Array.IndexOf(property.enumNames, name);
            if (index < 0) throw new InvalidOperationException("Required environment shader enum unavailable: " + property.propertyPath + "/" + name);
            property.enumValueIndex = index;
        }
    }
}
