#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Actual Unity Editor checks in a disposable thirteen-scene fixture, with no original game code.</summary>
public static class QuestSceneRestorationFixture
{
    private static int checks;
    private static QuestCampaignAssetValidation.Scenes input;
    private static readonly MethodInfo Restore = typeof(QuestCampaignAssetValidation).GetMethod("RestoreSceneSetup", BindingFlags.NonPublic | BindingFlags.Static);

    [Serializable] private sealed class Report
    {
        public string unityVersion;
        public int checks, initialSetupCount, validatedSceneCount, singleAwakes, singleEnables;
        public bool emptyNativeControlRejected, originalValidationExceptionPreserved, oneSceneRestored, twoScenesRestored;
        public bool normalReferenceRetained, previewOpenPublicApiAvailable, passed;
    }

    public static void Run()
    {
        var report = new Report { unityVersion = Application.unityVersion };
        try
        {
            Check(File.Exists("quest-scene-restoration-fixture.marker"), "Private fixture marker is missing.");
            report.initialSetupCount = EditorSceneManager.GetSceneManagerSetup().Length;
            CreateScenes();
            Stage();

            bool rejected = false;
            try { EditorSceneManager.RestoreSceneManagerSetup(new SceneSetup[0]); }
            catch (ArgumentException error) { rejected = error.Message.Contains("Invalid SceneManagerSetup"); }
            Check(rejected, "The planted original empty-setup control did not reproduce native rejection.");
            report.emptyNativeControlRejected = rejected;
            Restore.Invoke(null, new object[] { new SceneSetup[0] });
            CheckEmpty();

            // Exercise the production finally, not a reimplementation of cleanup.
            var broken = Clone(input);
            broken.scenes[1].originalCollection = "wrong-original-collection";
            Write(QuestCampaignAssetValidation.SceneInputPath, broken);
            RejectOriginal();
            CheckEmpty();
            report.originalValidationExceptionPreserved = true;

            Write(QuestCampaignAssetValidation.SceneInputPath, input);
            QuestCampaignAssetValidation.Validate();
            var receipt = JsonUtility.FromJson<QuestCampaignAssetValidation.Receipt>(File.ReadAllText(QuestCampaignAssetValidation.ReceiptPath));
            Check(receipt.allNativeScenesImported && receipt.scenes.Length == 13, "Production validation omitted a fixture scene.");
            report.validatedSceneCount = receipt.scenes.Length;
            CheckEmpty();

            EditorSceneManager.OpenScene(input.scenes[4].path, OpenSceneMode.Single);
            var one = EditorSceneManager.GetSceneManagerSetup();
            QuestCampaignAssetValidation.Validate();
            CheckSetup(one);
            report.oneSceneRestored = true;

            var second = EditorSceneManager.OpenScene(input.scenes[8].path, OpenSceneMode.Additive);
            Check(SceneManager.SetActiveScene(second), "Could not select the second authored scene.");
            var two = EditorSceneManager.GetSceneManagerSetup();
            Check(two.Length == 2 && two[1].isActive && two.All(scene => scene.isLoaded), "Two-scene fixture is invalid.");
            Write(QuestCampaignAssetValidation.SceneInputPath, broken);
            RejectOriginal();
            CheckSetup(two);
            report.twoScenesRestored = true;
            Write(QuestCampaignAssetValidation.SceneInputPath, input);

            // The pinned Editor has no public OpenPreviewScene API. Record the
            // bounded negative finding; do not rely on private Unity interfaces.
            report.previewOpenPublicApiAvailable = typeof(EditorSceneManager).GetMethod("OpenPreviewScene", BindingFlags.Public | BindingFlags.Static) != null;
            QuestSceneLifecycleWitness.Reset();
            var single = EditorSceneManager.OpenScene(input.scenes[3].path, OpenSceneMode.Single);
            report.singleAwakes = QuestSceneLifecycleWitness.awakes;
            report.singleEnables = QuestSceneLifecycleWitness.enables;
            Check(report.singleAwakes > 0 && report.singleEnables > 0, "Normal scene did not activate the edit-mode witness.");
            var root = single.GetRootGameObjects().Single();
            var witness = root.GetComponent<QuestSceneLifecycleWitness>();
            string referenceGuid; long referenceFileId;
            Check(witness != null && root.GetComponent<BoxCollider>() != null &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(witness.retainedReference, out referenceGuid, out referenceFileId) &&
                referenceGuid == AssetDatabase.AssetPathToGUID("Assets/Reference.asset") &&
                ((QuestSceneReferenceWitness)witness.retainedReference).authoredValue == 37,
                "The source native component/reference did not survive validation.");
            report.normalReferenceRetained = true;
            report.checks = checks; report.passed = true;
            File.WriteAllText("scene-restoration-result.json", JsonUtility.ToJson(report, true));
            Debug.Log("[Quest scene restoration fixture] " + JsonUtility.ToJson(report));
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            report.checks = checks;
            File.WriteAllText("scene-restoration-result.json", JsonUtility.ToJson(report, true));
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }

    private static void CreateScenes()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var retained = ScriptableObject.CreateInstance<QuestSceneReferenceWitness>(); retained.authoredValue = 37;
        AssetDatabase.CreateAsset(retained, "Assets/Reference.asset");
        input = new QuestCampaignAssetValidation.Scenes { schema = 1, scenes = new QuestCampaignAssetValidation.OriginalScene[13] };
        for (int index = 0; index < 13; index++)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("SourceRoot-" + index); root.AddComponent<BoxCollider>();
            if (index == 3) root.AddComponent<QuestSceneLifecycleWitness>().retainedReference = retained;
            string path = "Assets/Scenes/Scene" + index.ToString("00") + ".unity";
            Check(EditorSceneManager.SaveScene(scene, path), "Could not save the source fixture scene.");
            input.scenes[index] = new QuestCampaignAssetValidation.OriginalScene { index = index, path = path,
                guid = AssetDatabase.AssetPathToGUID(path), originalCollection = "level" + index };
        }
    }

    private static void Stage()
    {
        Write(QuestCampaignAssetValidation.InputPath, new QuestCampaignAssetValidation.Associations { schema = 1,
            association = "captured-original-UnityFS-CAB-and-serialized-pathID", entries = new QuestCampaignAssetValidation.Association[0] });
        Write(QuestCampaignAssetValidation.SceneInputPath, input);
        Write(QuestCampaignAssetValidation.PackedSpriteInputPath, new QuestCampaignAssetValidation.PackedSprites {
            schema = 1, atlases = new QuestCampaignAssetValidation.PackedAtlas[0], sprites = new QuestCampaignAssetValidation.PackedSprite[0] });
        Write(QuestCampaignAssetValidation.BundledAudioInputPath, new QuestCampaignAssetValidation.BundledAudio {
            schema = 1, assets = new QuestCampaignAssetValidation.BundledAudioClip[0] });
        AssetDatabase.Refresh();
    }
    private static void RejectOriginal()
    {
        bool rejected = false;
        try { QuestCampaignAssetValidation.Validate(); }
        catch (InvalidDataException error) { rejected = error.Message == "Original build scene order/native collection differs."; }
        Check(rejected, "Cleanup masked the original scene-validation failure.");
        Check(!File.Exists(QuestCampaignAssetValidation.ReceiptPath), "Rejected validation published a success receipt.");
    }
    private static void CheckEmpty()
    {
        var active = SceneManager.GetActiveScene();
        Check(SceneManager.sceneCount == 1 && active.IsValid() && active.isLoaded && active.path == "" && active.GetRootGameObjects().Length == 0,
            "Empty batch state was not restored to one legitimate empty scene.");
    }
    private static void CheckSetup(SceneSetup[] expected)
    {
        var actual = EditorSceneManager.GetSceneManagerSetup();
        Check(actual.Length == expected.Length && actual.Zip(expected, (a,b) => a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(value => value),
            "Original Editor scene order/loaded/active setup changed.");
    }
    private static T Clone<T>(T value) { return JsonUtility.FromJson<T>(JsonUtility.ToJson(value)); }
    private static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(value, true));
    }
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
}
#endif
