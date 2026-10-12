"""Execute the production preparation policy through the pinned Unity Mono.

Only native Editor/cooking APIs are spies; production task routing, environment
parsing, source-path guards, receipt flags and terminal boundaries are real.
No original game assets, full Editor import or hardware outcome is claimed.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from test_package_editor_contract import declaration

ROOT = Path(__file__).resolve().parents[2]
EDITOR = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
MONO = DATA / "MonoBleedingEdge/bin/mono"
MCS = DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"

SHIMS = r'''
using System; using System.IO; using System.Linq; using System.Collections.Generic;
using UnityEngine; using UnityEditor; using UnityEditor.SceneManagement;
static class Spy {
 public static List<string> Calls=new List<string>();
 public static string Fail;
 public static void Call(string value){Calls.Add(value);if(Fail==value)throw new InvalidDataException("injected "+value);}
}
namespace UnityEngine {
 public static class Application {public static string unityVersion="2021.3.5f1";}
 public static class Debug {public static void Log(object value) {}}
 public static class JsonUtility {
  public static T FromJson<T>(string text)=>new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(text);
  public static string ToJson(object value,bool pretty=false)=>new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value);
 }
 public class GameObject {
  public GameObject(string name){Spy.Call("bootstrap");}
  public T AddComponent<T>() where T:new(){return new T();}
 }
}
namespace UnityEditor {
 public class SceneAsset {}
 public static class AssetDatabase {
  public static string Missing;
  public static string AssetPathToGUID(string path){Spy.Call("scene-path");return path==Missing ? "" : new string('a',32);}
  public static T LoadAssetAtPath<T>(string path) where T:new(){Spy.Call("scene-load");return new T();}
 }
}
namespace UnityEditor.SceneManagement {
 public enum NewSceneSetup {EmptyScene} public enum NewSceneMode {Single}
 public static class EditorSceneManager {
  public static int NewScene(NewSceneSetup a,NewSceneMode b){Spy.Call("new-scene");return 1;}
  public static bool SaveScene(int scene,string path){Spy.Call("save-scene");File.WriteAllText(path,"owned saved startup");return true;}
 }
}
namespace GloomhavenVR.Quest.Editor {
 public static class QuestWizardProgress {
  public static Dictionary<string,int[]> Sequences=new Dictionary<string,int[]>();
  public static List<string> Operations=new List<string>();
  public static void Operation(string operation,bool complete,string detail){Operations.Add(operation+":"+complete);}
  public sealed class TaskSequence {
   string phase;int total,done;
   public TaskSequence(string a,string b,int count){phase=a;total=count;Sequences[a]=new[]{0,count};}
   public void Run(string detail,Action action){Spy.Call("task:"+detail);action();Sequences[phase]=new[]{++done,total};}
  }
  public sealed class Counter {
   public Counter(string a,string b,long c,string d,string e){} public void Report(long n,string d){} public void Complete(string d){}
  }
 }
 public sealed class QuestGameBootstrap {}
 public sealed class QuestGameModLifecycle {public void PrepareStartupView(){Spy.Call("startup-view");}}
 public static class QuestOriginalScriptBindings {public static void RemapAndValidate(){Spy.Call("script-remap");}}
 public static class QuestOriginalScriptOrders {public static void RestoreAndVerify(){Spy.Call("script-orders");}}
 public static class QuestAudioValidation {public static void Validate(){Spy.Call("qa:audio");}}
 public static class QuestSpriteGeometryValidation {public static void ValidateStartupAssets(){Spy.Call("qa:startup-sprites");}}
 public static class QuestUiAssetValidation {public static void Validate(bool post){Spy.Call("qa:ui:"+post);}}
 public static class QuestPostEffectValidation {public static void Validate(bool post){Spy.Call("qa:post:"+post);}}
 public static class QuestVideoValidation {public static void Validate(bool post){Spy.Call("qa:video:"+post);}}
 public static class QuestWorldScreenValidation {public static void Validate(bool post){Spy.Call("qa:screen:"+post);}}
 public static class QuestCampaignAssetValidation {public static void Validate(){Spy.Call("qa:campaign-assets");}public static void ValidateAfterAndroidBuild(){Spy.Call("qa:campaign-assets:post");}}
 public static class QuestCampaignTextureValidation {public static object Validate(){Spy.Call("qa:textures");return null;}}
 public static class QuestCampaignSpriteValidation {public static object Validate(){Spy.Call("qa:campaign-sprites");return null;}}
 public static class QuestCampaignComputeValidation {public static void ValidateSources(){Spy.Call("compute-contract");}}
 public static class QuestStartupAddressablesBuild {public static void Build(){Spy.Call("native-addressables");}}
}
'''

PROGRAM = r'''
class Program {
 static int Main(string[] args){
  try {
   Spy.Fail=args[2];UnityEditor.AssetDatabase.Missing=args[3];
   var scenes=GloomhavenVR.Quest.Editor.QuestBuild.Prepare(args[0]=="campaign");
   Console.WriteLine(UnityEngine.JsonUtility.ToJson(new {success=true,scenes=scenes,calls=Spy.Calls,
    tasks=GloomhavenVR.Quest.Editor.QuestWizardProgress.Sequences,operations=GloomhavenVR.Quest.Editor.QuestWizardProgress.Operations}));
   return 0;
  } catch(Exception error){Console.WriteLine(UnityEngine.JsonUtility.ToJson(new {success=false,message=error.Message,calls=Spy.Calls,
   tasks=GloomhavenVR.Quest.Editor.QuestWizardProgress.Sequences,operations=GloomhavenVR.Quest.Editor.QuestWizardProgress.Operations}));return 2;}
 }
}
'''


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity Mono compiler unavailable")
class MinimumUnityWorkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-minimum-work-")
        self.base = Path(self.temp.name)
        source = (EDITOR / "QuestBuild.cs").read_text()
        excerpts = [declaration(source, marker) for marker in (
            "sealed class ModInput", "sealed class InputManifest", "sealed class StartupEvidence",
            "sealed class StandaloneEvidence", "public sealed class BuildWorkReceipt",
            "static string Required(string key)", "static string[] PrepareOriginalStartup(",
            "public static bool ImportedAssetValidationRequested(string value)",
            "public static bool ImportedAssetValidationRequested()", "static void WriteBuildWorkReceipt()")]
        self.excerpts = excerpts
        production = (SHIMS + "\nnamespace GloomhavenVR.Quest.Editor {public static class QuestBuild {\n"
            "const string BuildWorkReceiptPath=\"QuestStartupEvidence/build-work.json\";\n"
            "static BuildWorkReceipt buildWork;static object campaignShaderMode;\n"
            "static void ValidatePackageApiContract(){Spy.Call(\"package-api\");}\n"
            "static void ValidateCampaignBuildContract(){Spy.Call(\"campaign-contract\");}\n"
            "static object PrepareCampaignShaders(){Spy.Call(\"shader-retention\");return new object();}\n"
            "static void PrepareLoadingLogo(){Spy.Call(\"loading-logo\");}\n" + "\n".join(excerpts) +
            "\npublic static string[] Prepare(bool campaign){return PrepareOriginalStartup(campaign);} }}\n" + PROGRAM)
        path = self.base / "MinimumWork.cs"
        path.write_text(production)
        self.exe = self.base / "MinimumWork.exe"
        result = subprocess.run([str(MONO), str(MCS), "-langversion:latest", "-r:System.Web.Extensions",
            "-nowarn:0649,0414", "-out:" + str(self.exe), str(path)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def tearDown(self):
        self.temp.cleanup()

    def run_policy(self, campaign=True, *, flag="0", verification="minimum", failure="", missing="", unsafe=False):
        project = self.base / ("project-" + str(len(list(self.base.glob("project-*")))))
        resource = project / "Assets/Quest/Resources"
        resource.mkdir(parents=True)
        names = ["Bootstrap", "Intro", "Gloomhaven_unified", "MainMenu"]
        if campaign: names += ["Scene" + str(index) for index in range(9)]
        scenes = ["Assets/Scenes/" + name + ".unity" for name in names]
        for name in scenes:
            path = project / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("original controlled scene payload")
        if unsafe: scenes[-1] = "Assets/Scenes/../MainMenu.unity"
        (resource / "quest-startup-report.json").write_text(json.dumps({"schema": 1,
            "target": "campaign" if campaign else "startup", "fullGameReady": False, "selectedScenes": scenes}))
        (resource / "quest-standalone-report.json").write_text(json.dumps({"startupAdapterComplete": True, "fullGameReady": False}))
        manifest = project / "input-manifest.json"
        manifest.write_text(json.dumps({"inputKey": "a" * 64, "mod": {"modBuild": 672}}))
        env = {**os.environ, "GHVR_QUEST_MANIFEST_PATH": str(manifest),
            "GHVR_QUEST_VERIFICATION": verification}
        if flag is None: env.pop("GHVR_QUEST_VALIDATE_IMPORTED_ASSETS", None)
        else: env["GHVR_QUEST_VALIDATE_IMPORTED_ASSETS"] = flag
        result = subprocess.run([str(MONO), str(self.exe), "campaign" if campaign else "startup",
            str(flag), failure, missing], cwd=project, env=env, capture_output=True, text=True)
        response = json.loads(result.stdout)
        receipt_path = project / "QuestStartupEvidence/build-work.json"
        response["receipt"] = json.loads(receipt_path.read_text()) if receipt_path.exists() else None
        response["exitCode"] = result.returncode
        return response

    def test_cold_campaign_default_runs_required_work_and_no_full_asset_qa(self):
        result = self.run_policy(flag=None)
        self.assertEqual(result["exitCode"], 0, result)
        self.assertEqual(result["tasks"], {"unity-validation-tasks": [8, 8], "unity-content-post-tasks": [1, 1]})
        self.assertEqual(len(result["scenes"]), 14)
        for name in ("package-api", "campaign-contract", "script-remap", "script-orders", "compute-contract",
                     "shader-retention", "native-addressables", "save-scene"):
            self.assertEqual(result["calls"].count(name), 1)
        self.assertFalse(any(name.startswith("qa:") for name in result["calls"]))
        self.assertNotIn("scene-load", result["calls"])
        self.assertTrue(result["receipt"]["requiredPreparationCompleted"])
        self.assertFalse(result["receipt"]["importedAssetQaRequested"])
        self.assertFalse(result["receipt"]["importedAssetQaCompleted"])
        self.assertFalse(result["receipt"]["originalPixelParityVerified"])
        self.assertFalse(result["receipt"]["headsetPictureVerified"])

    def test_preparation_and_real_validation_consumers_compile_against_pinned_unity_sdk(self):
        # Compile the changed preparation method with actual Unity types and
        # real native validation consumers, independently of the behavioral
        # API spies above. Only startup runtime construction/cooking and the
        # small surrounding Build entry point are bounded declarations here.
        path = self.base / "ActualSdk.cs"
        path.write_text("using System;using System.IO;using System.Linq;using UnityEngine;"
            "using UnityEditor;using UnityEditor.SceneManagement;\n"
            "namespace GloomhavenVR.Quest.Editor {"
            "public sealed class QuestGameBootstrap:MonoBehaviour {}"
            "public sealed class QuestGameModLifecycle:MonoBehaviour {public void PrepareStartupView(){}}"
            "public static class QuestStartupAddressablesBuild {public static void Build(){}}"
            "public static class QuestBuild {"
            "const string BuildWorkReceiptPath=\"QuestStartupEvidence/build-work.json\";"
            "static BuildWorkReceipt buildWork;static object campaignShaderMode;"
            "static void ValidatePackageApiContract(){}static void ValidateCampaignBuildContract(){}"
            "static object PrepareCampaignShaders(){return null;}static void PrepareLoadingLogo(){}\n" +
            "\n".join(self.excerpts) + "}}\n")
        names = ("QuestWizardProgress.cs", "QuestOriginalScriptBindings.cs", "QuestOriginalScriptOrders.cs",
            "QuestAudioValidation.cs", "QuestSpriteGeometryValidation.cs", "QuestUiAssetValidation.cs",
            "QuestPostEffectValidation.cs", "QuestVideoValidation.cs", "QuestWorldScreenValidation.cs",
            "QuestBlurValidation.cs", "QuestCampaignAssetValidation.cs", "QuestCampaignTextureValidation.cs",
            "QuestCampaignSpriteValidation.cs", "QuestCampaignComputeValidation.cs", "QuestCampaignShaderValidation.cs",
            "QuestCampaignShaderCache.cs", "QuestVulkanShaderValidation.cs", "QuestSmolvDecoder.cs")
        # Unity's shipped Roslyn supports the native sources' C# 8/9 nullable
        # directives and lambda shadowing; legacy mcs does not. Use that pinned
        # compiler for the complete native consumer set, without rewriting it.
        roslyn = DATA / "MonoBleedingEdge/lib/mono/4.5/csc.exe"
        self.assertTrue(roslyn.is_file(), "Pinned Unity Roslyn compiler is required")
        result = subprocess.run([str(MONO), str(roslyn), "-nologo", "-langversion:9", "-target:library",
            "-define:UNITY_EDITOR,GHVR_QUEST_STARTUP,GHVR_QUEST_GAME", "-nowarn:0649,0414",
            "-out:" + str(self.base / "ActualSdk.dll"),
            *["-r:" + str(file) for file in sorted((DATA / "Managed/UnityEngine").glob("*.dll"))],
            "-r:" + str(DATA / "PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll"),
            str(path), *[str(EDITOR / name) for name in names]], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        compiler_output = result.stdout + result.stderr
        self.assertEqual(compiler_output.count("warning"), 1, compiler_output)
        self.assertIn("PassType.VertexLMRGBM", compiler_output)

    def test_explicit_campaign_qa_preserves_existing_17_plus_6_actual_tasks(self):
        result = self.run_policy(flag="1")
        self.assertEqual(result["exitCode"], 0, result)
        self.assertEqual(result["tasks"], {"unity-validation-tasks": [17, 17], "unity-content-post-tasks": [6, 6]})
        self.assertEqual(sum(name.startswith("qa:") for name in result["calls"]), 14)
        self.assertEqual(result["calls"].count("scene-load"), 13)
        self.assertTrue(result["receipt"]["importedAssetQaRequested"])
        self.assertTrue(result["receipt"]["importedAssetQaCompleted"])

    def test_full_host_verification_also_enables_imported_asset_qa(self):
        result = self.run_policy(flag="0", verification="full")
        self.assertEqual(result["exitCode"], 0, result)
        self.assertEqual(result["tasks"]["unity-validation-tasks"], [17, 17])
        self.assertEqual(result["receipt"]["mode"], "exhaustive")

    def test_startup_default_and_full_have_only_their_actual_scheduled_work(self):
        for flag, expected in (("0", (5, 1, 0)), ("1", (11, 5, 10))):
            with self.subTest(flag=flag):
                result = self.run_policy(False, flag=flag)
                self.assertEqual(result["exitCode"], 0, result)
                self.assertEqual(result["tasks"]["unity-validation-tasks"], [expected[0], expected[0]])
                self.assertEqual(result["tasks"]["unity-content-post-tasks"], [expected[1], expected[1]])
                self.assertEqual(sum(name.startswith("qa:") for name in result["calls"]), expected[2])
                self.assertEqual(len(result["scenes"]), 5)

    def test_minimum_policy_never_skips_or_swallows_required_mutation_failure(self):
        result = self.run_policy(failure="script-remap")
        self.assertEqual(result["exitCode"], 2)
        self.assertIn("injected script-remap", result["message"])
        self.assertNotIn("native-addressables", result["calls"])
        self.assertNotIn("save-scene", result["calls"])
        self.assertFalse(result["receipt"]["requiredPreparationCompleted"])
        self.assertNotIn("unity-validation:True", result["operations"])

    def test_optional_failure_is_real_only_when_qa_was_requested(self):
        default = self.run_policy(failure="qa:audio")
        self.assertEqual(default["exitCode"], 0, default)
        full = self.run_policy(flag="1", failure="qa:audio")
        self.assertEqual(full["exitCode"], 2)
        self.assertFalse(full["receipt"]["importedAssetQaCompleted"])
        self.assertFalse(full["receipt"]["requiredPreparationCompleted"])
        self.assertNotIn("native-addressables", full["calls"])

    def test_bad_mode_rejects_before_any_scheduled_asset_work(self):
        for flag in ("yes", "minimum", "2"):
            with self.subTest(flag=flag):
                result = self.run_policy(flag=flag)
                self.assertEqual(result["exitCode"], 2)
                self.assertIn("accepts only 0 or 1", result["message"])
                self.assertEqual(result["calls"], [])
                self.assertIsNone(result["receipt"])

    def test_missing_or_unsafe_scene_fails_without_opening_scene_or_cooking(self):
        for kwargs in ({"missing": "Assets/Scenes/MainMenu.unity"}, {"unsafe": True}):
            with self.subTest(kwargs=kwargs):
                result = self.run_policy(**kwargs)
                self.assertEqual(result["exitCode"], 2)
                self.assertIn("Required original scene is unavailable", result["message"])
                self.assertNotIn("scene-load", result["calls"])
                self.assertNotIn("script-remap", result["calls"])
                self.assertNotIn("native-addressables", result["calls"])


if __name__ == "__main__": unittest.main()
