"""Run the production mode dispatch with bounded call spies, not native shader substitutes.

The compiled excerpt is copied from QuestBuild.cs byte-for-byte. The spies establish
which existing gates are called and how their outcomes are reported; actual native
shader/identity coverage lives in the separate completed Unity receipts.
"""
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs"


def declaration(text, marker):
    start = text.index(marker)
    opening = text.index("{", start)
    depth = 1
    position = opening + 1
    while depth:
        depth += (text[position] == "{") - (text[position] == "}")
        position += 1
    return text[start:position]


SPIES = r'''
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace UnityEngine {
 public static class Application { public static string unityVersion="2021.3.5f1"; }
 public static class JsonUtility {
  static JsonSerializerOptions options=new JsonSerializerOptions {IncludeFields=true};
  public static T FromJson<T>(string text)=>JsonSerializer.Deserialize<T>(text,options);
  public static string ToJson(object value,bool pretty)=>JsonSerializer.Serialize(value,options);
 }
 public static class Debug { public static void Log(string message) {} }
}
namespace GloomhavenVR.Quest.Editor {
 public static class QuestCampaignShaderValidation {
  public const string DefaultManifest="Assets/QuestOriginalCampaign/campaign-shaders.json";
  public sealed class Manifest {public int schema,requiredShaderCount,requiredMaterialCount;public string scope,graphicsApi,compilerPlatform;public Shader[] shaders;public Material[] materials;}
  public sealed class Shader {public Variant[] variants;} public sealed class Variant {} public sealed class Material {}
  public static int retain,identity,full,LastNativeCompileCount=73;
  public static bool LastValidationCacheReused=true,failRetain,failIdentity,failFull,drift;
  public static void Reset() {retain=identity=full=0;failRetain=failIdentity=failFull=drift=false;}
  public static void PrepareVariantCollection(string path) {++retain;if(failRetain)throw new InvalidOperationException("retention failed");}
  public static void VerifyImportedIdentities(Manifest input) {++identity;if(failIdentity)throw new InvalidOperationException("identity failed");if(drift)File.AppendAllText(DefaultManifest,"\n ");}
  public static void Validate(string path,string output) {++full;if(failFull)throw new InvalidOperationException("native validator failed");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"android-compiler.json"),"unit dispatch receipt, not native proof\n");}
 }
}
'''
PROGRAM = r'''
class Program {
 static int checks;
 static void Check(bool condition,string message){++checks;if(!condition)throw new Exception(message);}
 static string Valid="{\"schema\":1,\"scope\":\"campaign-compiler\",\"graphicsApi\":\"Vulkan\",\"compilerPlatform\":\"Vulkan\",\"requiredShaderCount\":2,\"requiredMaterialCount\":1,\"shaders\":[{\"variants\":[{},{}]},{\"variants\":[{}]}],\"materials\":[{}]}";
 static void Setup(string flag) {
  GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.Reset();
  Environment.SetEnvironmentVariable("GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS",flag);
  File.WriteAllText(GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.DefaultManifest,Valid);
 }
 static void Reject(Action action,string name){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
 static void Main() {
  Directory.CreateDirectory("Assets/QuestOriginalCampaign");Directory.CreateDirectory("QuestCampaignShaderEvidence");
  string prior="an inactive old compiler receipt must remain unchanged";
  File.WriteAllText("QuestCampaignShaderEvidence/android-compiler.json",prior);
  foreach(string flag in new string[]{null,"","0"}) {
   Setup(flag);var receipt=GloomhavenVR.Quest.Editor.QuestBuild.PrepareCampaignShaders();
   Check(receipt.mode=="minimum" && !receipt.exhaustiveValidationRequested && !receipt.exhaustiveCompilerValidationCompleted && !receipt.exhaustiveResultReused,"minimum honest flags");
   Check(receipt.nativeCompilerQueriesThisInvocation==0 && receipt.exhaustiveReceiptSha256==null,"minimum no fabricated native completion");
   Check(receipt.shaderCount==2 && receipt.nativeAliasCount==3 && receipt.materialCount==1 && receipt.requiredRetentionVerified && receipt.importedIdentitiesVerified,"complete retention/identity census");
   Check(GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.retain==1 && GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.identity==1 && GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.full==0,"default gate dispatch");
   Check(File.ReadAllText("QuestCampaignShaderEvidence/android-compiler.json")==prior,"minimum discarded prior completed native evidence");
   Check(!receipt.headsetPictureVerified && !receipt.originalPixelParityVerified,"false pixel claim");
  }
  foreach(bool reused in new[]{true,false}) {
   Setup("1");GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.LastValidationCacheReused=reused;
   GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.LastNativeCompileCount=reused?0:3;
   var receipt=GloomhavenVR.Quest.Editor.QuestBuild.PrepareCampaignShaders();
   Check(receipt.mode=="exhaustive" && receipt.exhaustiveValidationRequested && receipt.exhaustiveCompilerValidationCompleted,"explicit full flags");
   Check(receipt.exhaustiveResultReused==reused && receipt.nativeCompilerQueriesThisInvocation==(reused?0:3),"full result query/cache state");
   Check(receipt.exhaustiveReceiptSha256!=null && receipt.exhaustiveReceiptSha256.Length==64,"full receipt hash");
   Check(GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.retain==1 && GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.identity==1 && GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.full==1,"full gate dispatch");
  }
  foreach(string flag in new[]{"true","yes","2"," 1 "}) {
   Setup(flag);Reject(()=>GloomhavenVR.Quest.Editor.QuestBuild.PrepareCampaignShaders(),"invalid flag accepted");
   Check(GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.retain==0 && GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.full==0,"invalid flag ran gates");
  }
  foreach(string failure in new[]{"retain","identity","full","drift"}) {
   Setup(failure=="full"?"1":null);
   GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.failRetain=failure=="retain";
   GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.failIdentity=failure=="identity";
   GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.failFull=failure=="full";
   GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.drift=failure=="drift";
   Reject(()=>GloomhavenVR.Quest.Editor.QuestBuild.PrepareCampaignShaders(),failure);
   Check(!File.Exists(GloomhavenVR.Quest.Editor.QuestBuild.CampaignShaderModeReceiptPath),"failed gate left a successful mode receipt");
  }
  foreach(string change in new[]{"scope","backend","count"}) {
   Setup(null);string text=Valid;
   if(change=="scope")text=text.Replace("campaign-compiler","campaign");
   if(change=="backend")text=text.Replace("Vulkan","GLES3");
   if(change=="count")text=text.Replace("requiredShaderCount\":2","requiredShaderCount\":1");
   File.WriteAllText(GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.DefaultManifest,text);
   Reject(()=>GloomhavenVR.Quest.Editor.QuestBuild.PrepareCampaignShaders(),change);
   Check(GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.retain==0,"invalid provenance performed retention");
  }
  Console.WriteLine("PASS production Campaign shader build-mode dispatch: "+checks+" checks; gate spies only, no native GPU claim.");
 }
}
'''


class BuildModeDispatch(unittest.TestCase):
    def test_actual_production_dispatch_and_honest_receipts(self):
        dotnet = shutil.which("dotnet") or str(Path("/home/claw/.dotnet/dotnet"))
        if not Path(dotnet).is_file():
            self.skipTest(".NET SDK required for production method dispatch controls")
        text = SOURCE.read_text()
        excerpts = [declaration(text, marker) for marker in (
            "public sealed class CampaignShaderModeReceipt", "static string FileHash(string path)",
            "public static string CampaignShaderValidationMode(string value)", "public static CampaignShaderModeReceipt PrepareCampaignShaders()")]
        source = SPIES + '\nnamespace GloomhavenVR.Quest.Editor { using UnityEngine; public static class QuestBuild { public const string CampaignShaderModeReceiptPath="QuestCampaignShaderEvidence/build-mode.json";\n' + '\n'.join(excerpts) + '\n}}\n' + PROGRAM
        with tempfile.TemporaryDirectory(prefix="quest-build-mode-") as directory:
            path = Path(directory)
            (path / "Mode.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><EnableDefaultCompileItems>true</EnableDefaultCompileItems></PropertyGroup></Project>')
            (path / "Program.cs").write_text(source)
            env = os.environ.copy()
            env.pop("GHVR_QUEST_SHADER_MANIFEST", None)
            env.pop("GHVR_QUEST_SHADER_OUTPUT", None)
            result = subprocess.run([dotnet, "run", "--project", str(path / "Mode.csproj"), "--no-launch-profile", "--verbosity", "quiet"], cwd=path, env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("PASS production Campaign shader build-mode dispatch: 48 checks", result.stdout)

    def test_native_player_and_all_other_gates_remain_in_build(self):
        source = SOURCE.read_text()
        self.assertIn("report = BuildPipeline.BuildPlayer", source)
        self.assertIn("campaignShaderMode = PrepareCampaignShaders()", source)
        for gate in ("QuestCampaignAssetValidation.Validate", "QuestCampaignTextureValidation.Validate", "QuestCampaignSpriteValidation.Validate", "QuestCampaignComputeValidation.ValidateSources"):
            self.assertIn(gate, source)
        self.assertNotIn("QuestCampaignShaderValidation.Validate();", source)


if __name__ == "__main__":
    unittest.main()
