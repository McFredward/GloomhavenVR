using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEngine;

// Runs only in a disposable project prepared by run.py, with local owned assets.
public static class ProbeMaterialFixture
{
    static int checks;
    const string Root = QuestProbeMaterialConversion.RecoveredRoot;
    const string FixtureRoot = Root + "/Fixture";
    const string AtlasGuid = "ec11ade57207ee6418cda8645eb93dce";
    static string BodyPath => Root + "/NativeDependencies/Material/HM_MO_BanditGuard_MAT 1.mat";

    public static void Run()
    {
        try
        {
            Check(Application.unityVersion == "2021.3.5f1", "exact original editor");
            var originals = AssetDatabase.FindAssets("t:Material", new[] { Root })
                .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            Check(originals.Length == 2, "two unique owned figure materials");
            foreach (var material in originals)
            {
                var capture = QuestProbeMaterialConversion.CaptureOriginal(material);
                Check(material.HasProperty("_Diffuse"), "original HasProperty gate succeeds");
                Check(!material.HasProperty("_Color"), "original shader does not expose dormant orange tint");
                Check(capture.Evidence.albedoGuid == AtlasGuid, "original atlas GUID resolves");
                Check(capture.Albedo.width == 2048 && capture.Albedo.height == 2048, "actual imported atlas dimensions");
                Check(capture.Evidence.sourceSavedColor.g < .45f && capture.Evidence.sourceSavedColor.b < .06f,
                    "actual dormant orange saved property retained for evidence");
                Check(capture.Normal != null && capture.Evidence.normalMapped, "actual imported original normal map");
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Reviewed609" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Check(material.shader.name == "Standard", "reviewed build converted Standard");
                Check(!material.HasProperty("_Diffuse"), "repeat old conversion would skip saved _Diffuse");
                Check(material.GetTexture("_MainTex") != null &&
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.GetTexture("_MainTex"))) == AtlasGuid,
                    "reviewed build retained atlas; missing albedo hypothesis rejected");
                Check(material.GetColor("_Color").g < .45f && material.GetColor("_Color").b < .06f,
                    "reviewed Standard activates dormant orange tint");
            }

            QuestProbeMaterialConversion.Prepare();
            AssetDatabase.SaveAssets();
            var model = Resources.Load<GameObject>("quest-original-model");
            Check(model != null, "actual owned model imports");
            QuestProbeMaterialConversion.ValidateModel(model);
            var report = JsonUtility.FromJson<QuestProbeMaterialConversion.EvidenceReport>(
                File.ReadAllText(QuestProbeMaterialConversion.ReportPath));
            Check(!report.originalShaderFidelity && report.materials.Length == 2, "explicit approximation evidence per unique material");
            Check(report.materials.All(entry => entry.sourceSavedColor.b < .06f && entry.targetTint == Color.white),
                "report separates original dormant color from target albedo tint");
            Check(report.materials.All(entry => entry.albedoGuid == AtlasGuid), "retained dependency identity in report");
            foreach (var material in originals)
            {
                Check(material.GetColor("_Color") == Color.white, "dormant orange is not activated on Standard");
                var capture = QuestProbeMaterialConversion.CaptureOriginal(material);
                QuestProbeMaterialConversion.Apply(capture);
                QuestProbeMaterialConversion.ValidateMapped(capture);
                Check(material.GetTexture("_MainTex") == capture.Albedo, "idempotent conversion retains original atlas");
            }

            Directory.CreateDirectory(FixtureRoot);
            AssetDatabase.Refresh();
            LegacySavedProperty();
            NegativeControls();
            AssetDatabase.DeleteAsset(FixtureRoot);
            AssetDatabase.SaveAssets();
            string output = Environment.GetEnvironmentVariable("GHVR_MATERIAL_FIXTURE_OUTPUT");
            File.WriteAllText(output, "{\"schema\":1,\"passed\":true,\"checks\":" + checks +
                ",\"unityVersion\":\"" + Application.unityVersion + "\",\"hardwareVerified\":false,\"originalShaderFidelity\":false}");
            Debug.Log("PROBE_MATERIAL_FIXTURE_PASS checks=" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static void LegacySavedProperty()
    {
        var material = Copy("legacy-unexposed");
        material.shader = Shader.Find("Unlit/Color");
        var scale = new Vector2(2.5f, .75f);
        var offset = new Vector2(.2f, -.15f);
        EditTexture(material, "_Diffuse", null, scale, offset, setTexture: false);
        EditTexture(material, "_NormalMap", null, new Vector2(.8f, 1.4f), new Vector2(-.1f, .3f), setTexture: false);
        Check(!material.HasProperty("_Diffuse"), "fixture shader lacks legacy property");
        var capture = QuestProbeMaterialConversion.CaptureOriginal(material);
        Check(capture.Albedo != null, "serialized capture resolves legacy property independently of shader");
        QuestProbeMaterialConversion.Apply(capture);
        Check(material.GetTextureScale("_MainTex") == scale && material.GetTextureOffset("_MainTex") == offset,
            "nondefault original albedo UV scale and offset retained");
        Check(material.GetTextureScale("_BumpMap") == new Vector2(.8f, 1.4f) &&
            material.GetTextureOffset("_BumpMap") == new Vector2(-.1f, .3f), "independent original normal UV retained");
        Check(material.GetColor("_Color") == Color.white, "neutral tint remains neutral on shader without legacy property");
    }

    static void NegativeControls()
    {
        var missing = Copy("missing-albedo");
        EditTexture(missing, "_Diffuse", null, Vector2.one, Vector2.zero);
        Reject(() => QuestProbeMaterialConversion.CaptureOriginal(missing), "missing saved albedo fails closed");
        var missingNormal = Copy("missing-normal");
        EditTexture(missingNormal, "_NormalMap", null, Vector2.one, Vector2.zero);
        Reject(() => QuestProbeMaterialConversion.CaptureOriginal(missingNormal), "missing original normal fails closed");

        var textureLoss = Copy("lost-target-albedo");
        var capture = QuestProbeMaterialConversion.CaptureOriginal(textureLoss);
        QuestProbeMaterialConversion.Apply(capture);
        textureLoss.SetTexture("_MainTex", null);
        Reject(() => QuestProbeMaterialConversion.ValidateMapped(capture), "target texture loss is detected");
        QuestProbeMaterialConversion.Apply(capture);
        textureLoss.SetColor("_Color", Color.red);
        Reject(() => QuestProbeMaterialConversion.ValidateMapped(capture), "unwanted tint activation is detected");
        QuestProbeMaterialConversion.Apply(capture);
        textureLoss.SetTextureOffset("_MainTex", Vector2.one);
        Reject(() => QuestProbeMaterialConversion.ValidateMapped(capture), "target UV loss is detected");
        QuestProbeMaterialConversion.Apply(capture);
        textureLoss.DisableKeyword("_NORMALMAP");
        Reject(() => QuestProbeMaterialConversion.ValidateMapped(capture), "lost normal variant is detected");

        var unsupported = Copy("unsupported-tint");
        using (var serialized = new SerializedObject(unsupported))
        {
            Saved(serialized, "m_Colors", "_MOD_TINT").colorValue = new Color(.5f, 1, 1, .3f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        Reject(() => QuestProbeMaterialConversion.CaptureOriginal(unsupported), "nonneutral original shader modifier needs reconstruction");

        var originalCapture = QuestProbeMaterialConversion.CaptureOriginal(AssetDatabase.LoadAssetAtPath<Material>(BodyPath));
        string textureCopy = FixtureRoot + "/invalid-normal.png";
        Check(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(originalCapture.Normal), textureCopy), "isolated normal fixture copy");
        var importer = (TextureImporter)AssetImporter.GetAtPath(textureCopy);
        importer.textureType = TextureImporterType.Default;
        importer.SaveAndReimport();
        var invalidNormal = Copy("invalid-normal-import");
        EditTexture(invalidNormal, "_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textureCopy), Vector2.one, Vector2.zero);
        Reject(() => QuestProbeMaterialConversion.CaptureOriginal(invalidNormal), "non-normal importer is rejected");

        string albedoCopy = FixtureRoot + "/invalid-android.png";
        Check(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(originalCapture.Albedo), albedoCopy), "isolated albedo fixture copy");
        importer = (TextureImporter)AssetImporter.GetAtPath(albedoCopy);
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.DXT1;
        importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
        var invalidAndroid = Copy("invalid-android-format");
        EditTexture(invalidAndroid, "_Diffuse", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoCopy), Vector2.one, Vector2.zero);
        Reject(() => QuestProbeMaterialConversion.CaptureOriginal(invalidAndroid), "desktop-only compression is rejected for Android");

        var brokenLayout = Copy("missing-serialized-binding");
        using (var serialized = new SerializedObject(brokenLayout))
        {
            var entries = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int index = entries.arraySize - 1; index >= 0; index--)
                if (entries.GetArrayElementAtIndex(index).FindPropertyRelative("first").stringValue == "_Diffuse")
                    entries.DeleteArrayElementAtIndex(index);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        Reject(() => QuestProbeMaterialConversion.CaptureOriginal(brokenLayout), "absent legacy saved binding fails closed");
        var atomic = Copy("capture-before-mutation");
        atomic.shader = Shader.Find("Unlit/Color");
        Reject(QuestProbeMaterialConversion.Prepare, "invalid material blocks complete probe preparation");
        Check(atomic.shader.name == "Unlit/Color", "all captures validate before any target shader mutation");
    }

    static Material Copy(string name)
    {
        string path = FixtureRoot + "/" + name + ".mat";
        Check(AssetDatabase.CopyAsset(BodyPath, path), "isolated material fixture " + name);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }
    static void EditTexture(Material material, string name, Texture texture, Vector2 scale, Vector2 offset, bool setTexture = true)
    {
        using (var serialized = new SerializedObject(material))
        {
            var binding = Saved(serialized, "m_TexEnvs", name);
            if (setTexture) binding.FindPropertyRelative("m_Texture").objectReferenceValue = texture;
            binding.FindPropertyRelative("m_Scale").vector2Value = scale;
            binding.FindPropertyRelative("m_Offset").vector2Value = offset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    static SerializedProperty Saved(SerializedObject serialized, string collection, string name)
    {
        var entries = serialized.FindProperty("m_SavedProperties." + collection);
        for (int index = 0; index < entries.arraySize; index++)
        {
            var pair = entries.GetArrayElementAtIndex(index);
            if (pair.FindPropertyRelative("first").stringValue == name) return pair.FindPropertyRelative("second");
        }
        throw new Exception("Fixture cannot find saved property " + name);
    }
    static void Reject(Action action, string label)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            Check(error.Message.StartsWith("Probe material conversion blocked:", StringComparison.Ordinal), label);
            return;
        }
        throw new Exception("Negative control did not fail: " + label);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("Fixture failed: " + label);
        checks++;
        Debug.Log("PROBE_MATERIAL_CHECK " + label);
    }
}
