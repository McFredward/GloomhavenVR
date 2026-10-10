"""Execute the production Editor counters against API event spies, without importing assets."""
from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
EDITOR = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"

STUBS = r'''
using System;
using System.Collections.Generic;
using System.Text.Json;
namespace UnityEngine {
 public static class Debug {public static List<string> Lines=new List<string>();public static void Log(object value){Lines.Add(value.ToString());}public static void LogWarning(object value){Lines.Add(value.ToString());}}
}
namespace UnityEngine.SceneManagement {public struct Scene {public string path;}}
namespace UnityEditor {
 public sealed class InitializeOnLoadAttribute:Attribute {}
 public static class EditorApplication {public static double timeSinceStartup;public static event Action update;}
 public sealed class EditorBuildSettingsScene {public bool enabled;}
 public static class EditorBuildSettings {public static EditorBuildSettingsScene[] scenes={new EditorBuildSettingsScene{enabled=true},new EditorBuildSettingsScene{enabled=true}};}
 public static class Progress {
  public enum Status {Running,Succeeded,Failed,Canceled,Paused}
  public static event Action<Item[]> added,updated;
  public class Item {public bool exists=true,running=true,indefinite;public int totalSteps,currentStep,id,parentId;public Status status;public float progress;public string name,description;}
  public static void Emit(Item item){updated?.Invoke(new[]{item});}
  public static void EmitBatch(Item[] items){updated?.Invoke(items);}
  public static IEnumerable<Item> EnumerateItems(){return new Item[0];}
 }
}
namespace UnityEditor.Android { public interface IPostGenerateGradleAndroidProject {int callbackOrder {get;}void OnPostGenerateGradleAndroidProject(string path);} }
namespace UnityEditor.Build {
 public interface IPreprocessBuildWithReport {int callbackOrder {get;}void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report);}
 public interface IPostprocessBuildWithReport {int callbackOrder {get;}void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report);}
 public interface IProcessSceneWithReport {int callbackOrder {get;}void OnProcessScene(UnityEngine.SceneManagement.Scene scene,UnityEditor.Build.Reporting.BuildReport report);}
}
namespace UnityEditor.Build.Reporting {
 public enum BuildResult {Succeeded,Failed}
 public class BuildSummary {public BuildResult result;}
 public class BuildReport {public BuildSummary summary=new BuildSummary();}
}
class Program {
 static int checks;
 static void Check(bool value,string name){++checks;if(!value)throw new Exception(name);}
 static List<JsonElement> Rows() {
  var result=new List<JsonElement>();foreach(string line in UnityEngine.Debug.Lines)
   if(line.StartsWith("GHVRQ_PROGRESS "))result.Add(JsonDocument.Parse(line.Substring(15)).RootElement.Clone());
  return result;
 }
 static void Main(string[] args) {
  Environment.SetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS","1");
  GloomhavenVR.Quest.Editor.QuestWizardProgress.Operation("unity-validation",false,"Original validation");
  var tasks=new GloomhavenVR.Quest.Editor.QuestWizardProgress.TaskSequence("unity-validation-tasks","unity-validation",3);
  int actions=0;
  tasks.Run("First original task",()=>{++actions;var latest=Rows()[Rows().Count-1];Check(latest.GetProperty("done").GetInt32()==0 && latest.GetProperty("detail").GetString()=="First original task","label must precede native action");});
  tasks.Run("Second original task",()=>++actions);
  bool rejected=false;try{tasks.Run("Failing Sprite task",()=>{++actions;throw new InvalidOperationException("exact original identity differs");});}catch(InvalidOperationException){rejected=true;}
  var rows=Rows();var failure=rows[rows.Count-1];
  Check(rejected && actions==3,"exception and actual action count preserved");
  Check(failure.GetProperty("status").GetString()=="failed","failed native task visibly marked");
  Check(failure.GetProperty("done").GetInt32()==2 && failure.GetProperty("total").GetInt32()==3,"failure cannot count unfinished task");
  Check(failure.GetProperty("operation").GetString()=="unity-validation","correct task ownership");
  tasks.Run("Retry exact task",()=>++actions);rows=Rows();var complete=rows[rows.Count-1];
  Check(complete.GetProperty("done").GetInt32()==3 && complete.GetProperty("status").GetString()=="complete","child plan complete only after task return");
  Check(complete.GetProperty("phase").GetString()=="unity-validation-tasks","child is not operation boundary");
  var bytes=new GloomhavenVR.Quest.Editor.QuestWizardProgress.Counter("unity-content-delivery","player",24,"bytes","Copy actual original bank");
  for(int i=0;i<3;++i)bytes.Report(8*(i+1),"Original bank");bytes.Complete("Actual bank copied");
  rows=Rows();complete=rows[rows.Count-1];
  Check(complete.GetProperty("done").GetInt32()==24 && complete.GetProperty("total").GetInt32()==24,"copy consumes actual chunk bytes");
  Check(complete.GetProperty("operation").GetString()=="player","copy stays with actual Player preparation");
  var callback=new GloomhavenVR.Quest.Editor.QuestWizardBuildProgress();var report=new UnityEditor.Build.Reporting.BuildReport();
  callback.OnPreprocessBuild(report);callback.OnProcessScene(new UnityEngine.SceneManagement.Scene{path="bundle scene"},null);
  callback.OnProcessScene(new UnityEngine.SceneManagement.Scene{path="Player startup"},report);rows=Rows();var scene=rows[rows.Count-1];
  Check(scene.GetProperty("done").GetInt32()==1 && scene.GetProperty("total").GetInt32()==2,"Addressables scenes cannot contaminate Player counter");
  callback.OnPostprocessBuild(report);rows=Rows();complete=rows[rows.Count-1];
  Check(complete.GetProperty("phase").GetString()=="unity-player-native-result","native callback cannot prematurely close final Player evidence");
  Check(complete.GetProperty("status").GetString()=="progress","successful native build is one observed child result");
  var native=new UnityEditor.Progress.Item{id=42,parentId=7,name="Native import",indefinite=true};
  UnityEditor.Progress.Emit(native);rows=Rows();var task=rows[rows.Count-1];
  Check(task.GetProperty("phase").GetString()=="unity-progress:42:7" && task.GetProperty("done").GetInt32()==0 && task.GetProperty("total").GetInt32()==1,"indivisible native task has finite scoped denominator");
  native.running=false;native.status=UnityEditor.Progress.Status.Succeeded;UnityEditor.Progress.Emit(native);rows=Rows();task=rows[rows.Count-1];
  Check(task.GetProperty("done").GetInt32()==1 && task.GetProperty("status").GetString()=="complete","native success bypasses throttling and counts actual completion");
  var failedNative=new UnityEditor.Progress.Item{id=43,parentId=7,name="Failed native task",indefinite=true,running=false,status=UnityEditor.Progress.Status.Failed};
  UnityEditor.Progress.Emit(failedNative);rows=Rows();task=rows[rows.Count-1];
  Check(task.GetProperty("done").GetInt32()==0 && task.GetProperty("status").GetString()=="failed","failed native task never receives completion credit");
  var bulk=new UnityEditor.Progress.Item[130];for(int i=0;i<bulk.Length;++i)bulk[i]=new UnityEditor.Progress.Item{id=100+i,parentId=8,name="Terminal native task",indefinite=true,running=false,status=UnityEditor.Progress.Status.Succeeded};
  UnityEditor.Progress.EmitBatch(bulk);rows=Rows();task=rows[rows.Count-1];
  Check(task.GetProperty("phase").GetString()=="unity-progress:229:8" && task.GetProperty("done").GetInt32()==1,"large completion batches retain their terminal tail");
  if(args.Length>0)System.IO.File.WriteAllText(args[0],GloomhavenVR.Quest.Editor.QuestWizardGradleProgress.Script(args[1]));
  int count=UnityEngine.Debug.Lines.Count;Environment.SetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS","0");
  var disabled=new GloomhavenVR.Quest.Editor.QuestWizardProgress.TaskSequence("unity-validation-tasks","unity-validation",1);
  disabled.Run("Disabled observation",()=>++actions);
  Check(UnityEngine.Debug.Lines.Count==count && actions==5,"disabled observer neither logs nor removes work");
  Console.WriteLine("PASS production Editor task and byte counters: "+checks+" checks; API spies, no native import or hardware claim.");
 }
}
'''


class EditorPhaseProgressTests(unittest.TestCase):
    def test_production_observer_and_validators_compile_with_pinned_unity_sdk(self):
        data = Path("/home/claw/unity-2021.3.5/Editor/Data")
        mono, compiler = data / "MonoBleedingEdge/bin/mono", data / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
        if not mono.is_file() or not compiler.is_file(): self.skipTest("Pinned Unity SDK required")
        names = ("QuestWizardProgress.cs", "QuestOriginalScriptBindings.cs", "QuestCampaignAssetValidation.cs",
            "QuestCampaignSpriteValidation.cs", "QuestSpriteGeometryValidation.cs",
            "QuestCampaignTextureValidation.cs", "QuestCampaignComputeValidation.cs", "QuestCampaignShaderValidation.cs",
            "QuestCampaignShaderCache.cs", "QuestVulkanShaderValidation.cs", "QuestSmolvDecoder.cs")
        with tempfile.TemporaryDirectory(prefix="quest-editor-api-") as directory:
            argv = [str(mono), str(compiler), "-langversion:latest", "-nowarn:0649", "-define:UNITY_EDITOR,GHVR_QUEST_GAME", "-target:library",
                "-out:" + str(Path(directory) / "ActualEditorProgress.dll")]
            argv += ["-r:" + str(path) for path in sorted((data / "Managed/UnityEngine").glob("*.dll"))]
            argv += ["-r:" + str(data / "PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll")]
            argv += [str(EDITOR / name) for name in names]
            result = subprocess.run(argv, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            # VertexLMRGBM is an inherited, required original LightMode mapping,
            # rather than a newly introduced observer warning/API replacement.
            self.assertEqual(result.stderr.count("warning"), 1, result.stderr)
            self.assertIn("PassType.VertexLMRGBM", result.stderr)

    def test_production_counter_failure_and_callback_semantics(self):
        dotnet = shutil.which("dotnet") or "/home/claw/.dotnet/dotnet"
        if not Path(dotnet).is_file(): self.skipTest(".NET SDK required")
        with tempfile.TemporaryDirectory(prefix="quest-editor-progress-") as directory:
            path = Path(directory)
            (path / "Counter.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>UNITY_EDITOR</DefineConstants><NoWarn>CS0067;CS0649</NoWarn></PropertyGroup></Project>')
            (path / "QuestWizardProgress.cs").write_bytes((EDITOR / "QuestWizardProgress.cs").read_bytes())
            (path / "Program.cs").write_text(STUBS)
            result = subprocess.run([dotnet, "run", "--project", str(path / "Counter.csproj"), "--no-launch-profile", "--verbosity", "quiet"],
                cwd=path, env=os.environ.copy(), text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("PASS production Editor task and byte counters: 17 checks", result.stdout)

    def test_counter_annotations_preserve_native_validation_and_pointer_writes(self):
        # Strip only this change's observer statements, then compare complete
        # source tokens with the shipped checkpoint. This catches accidental
        # native validation/load/write changes, not just matching call names.
        def native_tokens(source):
            source = re.sub(r"^\s*var \w+ = new QuestWizardProgress\.Counter\([^\n]+\);\n", "", source, flags=re.M)
            source = re.sub(r"\b\w+\.(?:Report|Complete)\([^;\n]*\);", "", source)
            source = re.sub(r"\bint (?:done|examined|objectsDone|dependenciesDone|atlasesDone|spritesDone|bindingsDone|assetsDone|written) = 0;", "", source)
            source = source.replace("using GloomhavenVR.Quest.Editor;", "")
            source = re.sub(r'if \(row\.status == "serialized-value-location-excluded"\)\s*\{\s*continue;\s*\}', 'if (row.status == "serialized-value-location-excluded") continue;', source)
            source = re.sub(r"foreach \(var plan in plans\)\s*\{\s*(File\.WriteAllText\([^\n]+?;)[\s]*\}", r"foreach (var plan in plans) \1", source)
            return re.sub(r"\s+", "", source)
        for name in ("QuestOriginalScriptBindings.cs", "QuestCampaignAssetValidation.cs", "QuestCampaignTextureValidation.cs",
            "QuestCampaignComputeValidation.cs", "QuestCampaignShaderValidation.cs"):
            with self.subTest(source=name):
                relative = str((EDITOR / name).relative_to(ROOT))
                baseline = "cb6f84fb" if name == "QuestOriginalScriptBindings.cs" else "2c28ce989"
                old = subprocess.run(["git", "show", baseline + ":" + relative], cwd=ROOT, text=True, capture_output=True, check=True).stdout
                current = (EDITOR / name).read_text()
                self.assertEqual(native_tokens(current), native_tokens(old))
                # A deliberately weakened original identity gate must fail the
                # equivalence check; a stripped observer alone must pass.
                mutated = current.replace("throw new", "return; // throw new", 1)
                self.assertNotEqual(native_tokens(mutated), native_tokens(old))

    def test_plan_names_each_existing_validation_and_does_not_skip_failed_native_work(self):
        source = (EDITOR / "QuestBuild.cs").read_text()
        body = source[source.index("static string[] PrepareOriginalStartup("):source.index("public static string CampaignShaderValidationMode")]
        self.assertIn('campaign ? 17 : 11', body)
        calls = re.findall(r'tasks.Run\("[^"\n]+",', body)
        self.assertEqual(len(calls), 17)
        for method in ("QuestOriginalScriptBindings.RemapAndValidate", "QuestOriginalScriptOrders.RestoreAndVerify", "QuestAudioValidation.Validate",
            "QuestSpriteGeometryValidation.ValidateStartupAssets", "QuestCampaignAssetValidation.Validate", "QuestCampaignTextureValidation.Validate",
            "QuestCampaignSpriteValidation.Validate", "QuestCampaignComputeValidation.ValidateSources"):
            self.assertIn(method, body)
        self.assertLess(body.index('post.Run("Prepare and save'), body.index('Operation("content-bank", true'))
        build = source[source.index("static void BuildPlayer()"):source.index("static void ConfigureFixedEyeMsaa()")]
        self.assertLess(build.index('File.WriteAllText(apk + ".build.json"'), build.index('Operation("player", true'))


if __name__ == "__main__": unittest.main()
