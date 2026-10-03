using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Quest.Editor
{
    // Only staged assets in the labelled hardware diagnostic use this approximation.
    // Saved properties survive shader changes; HasProperty queries the current shader.
    // Neither this mapping nor a successful Android import establishes game shader parity.
    public static class QuestProbeMaterialConversion
    {
        public const string RecoveredRoot = "Assets/Quest/Recovered";
        public const string ReportPath = "Assets/Quest/Resources/quest-probe-materials.json";

        [Serializable] public sealed class MaterialEvidence
        {
            public string materialPath, materialName, sourceShader, sourceTextureProperty;
            public string albedoPath, albedoGuid, albedoAndroidFormat, normalPath, normalAndroidFormat;
            public int albedoWidth, albedoHeight;
            public Vector2 albedoScale, albedoOffset;
            public Color sourceSavedColor, targetTint;
            public bool sourceShaderExposesDiffuse, sourceShaderExposesColor, normalMapped;
            public string targetShader = "Standard";
            public string[] approximationNotes;
        }
        [Serializable] public sealed class EvidenceReport
        {
            public int schema = 1;
            public string scope = "diagnostic-owned-asset-only";
            public bool originalShaderFidelity = false;
            public MaterialEvidence[] materials;
        }
        public sealed class Capture
        {
            public Material Material;
            public Texture2D Albedo, Normal;
            public Vector2 NormalScale, NormalOffset;
            public float BumpScale;
            public MaterialEvidence Evidence;
        }

        public static void Prepare()
        {
            if (!AssetDatabase.IsValidFolder(RecoveredRoot)) return;
            // Capture and validate the entire set before any shader mutation.
            var captures = AssetDatabase.FindAssets("t:Material", new[] { RecoveredRoot })
                .Select(guid => CaptureOriginal(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid))))
                .OrderBy(capture => capture.Evidence.materialPath, StringComparer.Ordinal).ToArray();
            foreach (var capture in captures)
            {
                Apply(capture);
                var evidence = capture.Evidence;
                Debug.Log("[GloomhavenVR Quest] probe material=" + evidence.materialName +
                    " sourceShader=" + evidence.sourceShader + " savedAlbedo=" + evidence.sourceTextureProperty +
                    " albedo=" + evidence.albedoPath + " dimensions=" + evidence.albedoWidth + "x" + evidence.albedoHeight +
                    " androidFormat=" + evidence.albedoAndroidFormat + " tint=" + evidence.targetTint +
                    " normalMapped=" + evidence.normalMapped + " originalShaderFidelity=false");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new EvidenceReport
            {
                materials = captures.Select(capture => capture.Evidence).ToArray()
            }, true));
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        public static Capture CaptureOriginal(Material material)
        {
            if (material == null) throw Invalid("Missing recovered material");
            string path = AssetDatabase.GetAssetPath(material);
            RequireRecoveredPath(path);
            using (var serialized = new SerializedObject(material))
            {
                var diffuse = Saved(serialized, "m_TexEnvs", "_Diffuse");
                if (diffuse == null) throw Invalid(path + " has no saved _Diffuse binding; unsupported probe material family");
                var texture = RequiredRelative(diffuse, "m_Texture").objectReferenceValue as Texture2D;
                string androidFormat = ValidateTexture(texture, "albedo", requireNormal: false);
                var scale = RequiredRelative(diffuse, "m_Scale").vector2Value;
                var offset = RequiredRelative(diffuse, "m_Offset").vector2Value;
                RequireFinite(scale, path + " albedo scale");
                RequireFinite(offset, path + " albedo offset");

                // These Amp families have neutral tint modifiers on the recovered figure.
                // _Color is not declared by their original shader. Its saved orange value
                // is dormant there, but becomes an albedo multiplier after a Standard switch.
                // Nonneutral modifiers require shader reconstruction, not a guessed mapping.
                foreach (string name in new[] { "_MOD_TINT", "_MOD_TINT1", "_Tint" })
                {
                    var modifier = Saved(serialized, "m_Colors", name);
                    if (modifier != null && modifier.colorValue != new Color(1, 1, 1, 0))
                        throw Invalid(path + " has an unsupported nonneutral " + name + "; shader reconstruction required");
                }
                foreach (string name in new[] { "_Cutout", "_InvisibilityControl" })
                {
                    var value = Saved(serialized, "m_Floats", name);
                    if (value != null && value.floatValue != 0)
                        throw Invalid(path + " has unsupported " + name + "; probe only supports the opaque original figure");
                }
                var opacity = Saved(serialized, "m_Floats", "_Opacity");
                if (opacity != null && opacity.floatValue != 1) throw Invalid(path + " has unsupported opacity");

                var normalBinding = Saved(serialized, "m_TexEnvs", "_NormalMap");
                if (normalBinding == null) throw Invalid(path + " has no saved original _NormalMap binding");
                var normal = RequiredRelative(normalBinding, "m_Texture").objectReferenceValue as Texture2D;
                string normalFormat = ValidateTexture(normal, "normal", requireNormal: true);
                var normalScale = RequiredRelative(normalBinding, "m_Scale").vector2Value;
                var normalOffset = RequiredRelative(normalBinding, "m_Offset").vector2Value;
                RequireFinite(normalScale, path + " normal scale");
                RequireFinite(normalOffset, path + " normal offset");
                var bump = Saved(serialized, "m_Floats", "_BumpScale");
                float bumpScale = bump == null ? 1 : bump.floatValue;
                if (!Finite(bumpScale)) throw Invalid(path + " has invalid normal strength");
                var savedColor = Saved(serialized, "m_Colors", "_Color");
                string albedoPath = AssetDatabase.GetAssetPath(texture);
                return new Capture
                {
                    Material = material, Albedo = texture, Normal = normal,
                    NormalScale = normalScale, NormalOffset = normalOffset, BumpScale = bumpScale,
                    Evidence = new MaterialEvidence
                    {
                        materialPath = path, materialName = material.name,
                        sourceShader = material.shader == null ? "missing" : material.shader.name,
                        sourceShaderExposesDiffuse = material.HasProperty("_Diffuse"),
                        sourceShaderExposesColor = material.HasProperty("_Color"),
                        sourceTextureProperty = "_Diffuse", albedoPath = albedoPath,
                        albedoGuid = AssetDatabase.AssetPathToGUID(albedoPath),
                        albedoWidth = texture.width, albedoHeight = texture.height,
                        albedoScale = scale, albedoOffset = offset, albedoAndroidFormat = androidFormat,
                        sourceSavedColor = savedColor == null ? Color.white : savedColor.colorValue,
                        targetTint = Color.white, normalMapped = normal != null,
                        normalPath = AssetDatabase.GetAssetPath(normal), normalAndroidFormat = normalFormat,
                        approximationNotes = new[]
                        {
                            "Standard opaque matte approximation; original diffuse and imported normal bindings retained.",
                            "Dormant _Color is not an Amp albedo tint; neutral original tint modifiers map to white.",
                            "Original packed MRAO, two-sided culling, outlines, dissolve and character lighting are not reconstructed."
                        }
                    }
                };
            }
        }

        public static void Apply(Capture capture)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) throw Invalid("Standard diagnostic shader is unavailable");
            var material = capture.Material;
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.SetTexture("_MainTex", capture.Albedo);
            material.SetTextureScale("_MainTex", capture.Evidence.albedoScale);
            material.SetTextureOffset("_MainTex", capture.Evidence.albedoOffset);
            material.SetColor("_Color", capture.Evidence.targetTint);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Glossiness", 0);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetTexture("_OcclusionMap", null);
            material.SetColor("_EmissionColor", Color.black);
            material.SetTexture("_EmissionMap", null);
            material.SetTexture("_BumpMap", capture.Normal);
            if (capture.Normal != null)
            {
                material.SetTextureScale("_BumpMap", capture.NormalScale);
                material.SetTextureOffset("_BumpMap", capture.NormalOffset);
                material.SetFloat("_BumpScale", capture.BumpScale);
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_Mode", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1);
            material.renderQueue = -1;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            ValidateMapped(capture);
        }

        public static void ValidateMapped(Capture capture)
        {
            var material = capture.Material;
            if (material == null || material.shader == null || material.shader.name != "Standard" ||
                material.GetTexture("_MainTex") != capture.Albedo || capture.Albedo == null ||
                material.GetTextureScale("_MainTex") != capture.Evidence.albedoScale ||
                material.GetTextureOffset("_MainTex") != capture.Evidence.albedoOffset ||
                material.GetColor("_Color") != capture.Evidence.targetTint)
                throw Invalid("Converted probe material lost its original albedo binding, UV transform or neutral tint");
            if (capture.Normal != null && (material.GetTexture("_BumpMap") != capture.Normal ||
                !material.IsKeywordEnabled("_NORMALMAP") ||
                material.GetTextureScale("_BumpMap") != capture.NormalScale ||
                material.GetTextureOffset("_BumpMap") != capture.NormalOffset))
                throw Invalid("Converted probe material lost its original imported normal binding");
            string[] dependencies = AssetDatabase.GetDependencies(capture.Evidence.materialPath, true);
            if (!dependencies.Contains(capture.Evidence.albedoPath))
                throw Invalid("Converted probe material has no albedo build dependency");
        }

        public static void ValidateModel(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            RequireRecoveredPath(path);
            var materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Distinct().ToArray();
            if (materials.Length == 0) throw Invalid("Original diagnostic model has no materials");
            string[] dependencies = AssetDatabase.GetDependencies(path, true);
            foreach (var material in materials)
            {
                var capture = CaptureOriginal(material);
                ValidateMapped(capture);
                if (!dependencies.Contains(capture.Evidence.albedoPath))
                    throw Invalid("Original diagnostic model has no albedo build dependency: " + capture.Evidence.materialName);
            }
            Debug.Log("[GloomhavenVR Quest] probe model albedo dependencies verified uniqueMaterials=" + materials.Length +
                " originalShaderFidelity=false");
        }

        static string ValidateTexture(Texture2D texture, string role, bool requireNormal)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                throw Invalid("Missing or invalid original " + role + " texture");
            string path = AssetDatabase.GetAssetPath(texture);
            RequireRecoveredPath(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.vtOnly)
                throw Invalid("Original " + role + " is not a portable imported Texture2D: " + path);
            if (requireNormal && importer.textureType != TextureImporterType.NormalMap)
                throw Invalid("Original normal is not imported as a Unity normal map: " + path);
            if (!requireNormal && importer.textureType != TextureImporterType.Default)
                throw Invalid("Original albedo has an unsupported import type: " + path);
            var settings = importer.GetPlatformTextureSettings("Android");
            var format = settings.overridden && settings.format != TextureImporterFormat.Automatic
                ? settings.format : importer.GetAutomaticFormat("Android");
            if (format == TextureImporterFormat.Automatic ||
                !TextureImporter.IsPlatformTextureFormatValid(importer.textureType, BuildTarget.Android, format) ||
                !IsQuestTextureFormat(format))
                throw Invalid("Original " + role + " texture format is unsupported by the Quest GLES3 probe: " + path + " format=" + format);
            return format.ToString();
        }

        static bool IsQuestTextureFormat(TextureImporterFormat format)
        {
            // Unity's Android-wide validation also accepts DXT (for other Android GPUs).
            // This probe targets Quest GLES3: retain ASTC/ETC/EAC or basic uncompressed data.
            string name = format.ToString();
            return name.StartsWith("ASTC_", StringComparison.Ordinal) ||
                name.StartsWith("ETC_", StringComparison.Ordinal) ||
                name.StartsWith("ETC2_", StringComparison.Ordinal) ||
                name.StartsWith("EAC_", StringComparison.Ordinal) ||
                new[] { "RGBA32", "RGB24", "RGBA16", "RGB16", "Alpha8", "R8", "RG16" }.Contains(name);
        }

        static SerializedProperty Saved(SerializedObject serialized, string collection, string name)
        {
            var entries = serialized.FindProperty("m_SavedProperties." + collection);
            if (entries == null || !entries.isArray) throw Invalid("Unsupported Unity material serialization: " + collection);
            SerializedProperty result = null;
            for (int i = 0; i < entries.arraySize; i++)
            {
                var pair = entries.GetArrayElementAtIndex(i);
                if (RequiredRelative(pair, "first").stringValue != name) continue;
                if (result != null) throw Invalid("Duplicate saved material property: " + name);
                result = RequiredRelative(pair, "second");
            }
            return result;
        }
        static SerializedProperty RequiredRelative(SerializedProperty property, string name)
        {
            var value = property.FindPropertyRelative(name);
            if (value == null) throw Invalid("Unsupported Unity material serialization: " + name);
            return value;
        }
        static void RequireRecoveredPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(RecoveredRoot + "/", StringComparison.Ordinal))
                throw Invalid("Probe material inputs must remain in staged " + RecoveredRoot + ": " + path);
        }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void RequireFinite(Vector2 value, string label)
        {
            if (!Finite(value.x) || !Finite(value.y)) throw Invalid("Invalid " + label);
        }
        static InvalidOperationException Invalid(string message)
        {
            return new InvalidOperationException("Probe material conversion blocked: " + message);
        }
    }
}
