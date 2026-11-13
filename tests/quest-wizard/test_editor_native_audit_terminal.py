"""Exercise the exact native-helper receipt gate and its terminal observations.

The child is a fixture, not an actual Shader audit. Only Unity logging/JSON APIs
are substituted at execution; compile also uses the pinned real Unity SDK.
"""
from pathlib import Path
import importlib.util
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
EDITOR = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
DATA = Path("/home/claw/unity-2021.3.5/Editor/Data")


def declaration(text, marker):
    start = text.index(marker)
    index = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[index] == "{") - (text[index] == "}")
        index += 1
    return text[start:index]


def production():
    source = (EDITOR / "QuestStartupAddressablesBuild.cs").read_text()
    methods = "\n".join(declaration(source, marker) for marker in (
        "sealed class NativeShaderReceipt", "static void ValidateNativeCampaignShaders(string nativeRoot)",
        "static string QuoteArgument(string value)"))
    return ("using System; using System.IO; using UnityEngine;\n"
        "namespace GloomhavenVR.Quest.Editor { public static class QuestStartupAddressablesBuild {\n"
        "[Serializable] " + methods + "\n public static void Check() {ValidateNativeCampaignShaders(Directory.GetCurrentDirectory());} }}")


HELPER = r'''
import argparse, json, os, pathlib, sys
parser=argparse.ArgumentParser()
for name in ("source", "project", "native-root", "evidence"): parser.add_argument("--"+name)
args=parser.parse_args()
print('GHVRQ_PROGRESS {"schema":1,"phase":"unity-native-shader-audit","done":2,"total":2,"unit":"bundles","detail":"Original roots decoded","status":"progress","operation":"content-bank"}', flush=True)
value={"schema":1,"scope":"actual-native-addressables-before-player","shaderCount":688,
    "originalNativeAliasCount":51564,"compilerQueries":0,"allOriginalAliasesRetained":True,
    "signedApkAuditPerformed":False,"hardwarePictureVerified":False,"selectedNativeBundleCount":2}
mode=os.environ["GHVR_TEST_MODE"]
if mode=="bad-receipt": value["allOriginalAliasesRetained"]=False
if mode=="bad-count": value["selectedNativeBundleCount"]=0
path=pathlib.Path(args.evidence); path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(value))
if mode=="failed": print("exact helper failure", file=sys.stderr); sys.exit(7)
'''

PROGRAM = r'''
namespace UnityEngine {
 public static class JsonUtility {public static T FromJson<T>(string text){return System.Text.Json.JsonSerializer.Deserialize<T>(text,new System.Text.Json.JsonSerializerOptions {IncludeFields=true});}}
}
class Program {
 static void Main() {
  string mode=System.Environment.GetEnvironmentVariable("GHVR_TEST_MODE");
  bool rejected=false;
  try {GloomhavenVR.Quest.Editor.QuestStartupAddressablesBuild.Check();}
  catch(System.IO.InvalidDataException error) {rejected=true;System.Console.WriteLine(error.Message);}
  if(rejected!=(mode!="success"))throw new System.Exception("exact native gate rejected/accepted incorrectly");
  int complete=0;
  foreach(string line in UnityEngine.Debug.Lines) {
   if(!line.StartsWith("GHVRQ_PROGRESS "))continue;
   var row=System.Text.Json.JsonDocument.Parse(line.Substring(15)).RootElement;
   if(row.GetProperty("phase").GetString()=="unity-native-shader-audit" && row.GetProperty("status").GetString()=="complete") {
    ++complete;
    if(row.GetProperty("done").GetInt32()!=2 || row.GetProperty("total").GetInt32()!=2 || row.GetProperty("unit").GetString()!="bundles")throw new System.Exception("terminal receipt lost actual bundle total");
   }
  }
  if(complete!=(mode=="success"?1:0))throw new System.Exception("audit closed before exact native receipt accepted");
  System.Console.WriteLine("PASS production native-audit receipt terminal: "+mode);
 }
}
'''


class ActualNativeAuditTerminal(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dotnet = shutil.which("dotnet") or "/home/claw/.dotnet/dotnet"
        if not Path(cls.dotnet).is_file(): raise unittest.SkipTest(".NET SDK required")
        cls.temporary=tempfile.TemporaryDirectory(prefix="quest-native-audit-terminal-")
        cls.addClassCleanup(cls.temporary.cleanup)
        cls.directory=Path(cls.temporary.name)
        spec=importlib.util.spec_from_file_location("editor_spies", Path(__file__).with_name("test_editor_phase_progress.py"))
        spies=importlib.util.module_from_spec(spec); spec.loader.exec_module(spies)
        (cls.directory / "Audit.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>UNITY_EDITOR</DefineConstants><NoWarn>CS0067;CS0649</NoWarn></PropertyGroup></Project>')
        (cls.directory / "Program.cs").write_text(spies.STUBS.split("class Program {")[0] + PROGRAM)
        (cls.directory / "QuestWizardProgress.cs").write_bytes((EDITOR / "QuestWizardProgress.cs").read_bytes())
        (cls.directory / "NativeAudit.cs").write_text(production())
        cls.helper=cls.directory / "audit helper.py"; cls.helper.write_text(HELPER)
        result=subprocess.run([cls.dotnet,"build",str(cls.directory / "Audit.csproj"),"--verbosity","quiet"],text=True,capture_output=True,timeout=60)
        if result.returncode: raise AssertionError(result.stdout+result.stderr)
        cls.binary=cls.directory / "bin/Debug/net8.0/Audit.dll"

    def run_gate(self, mode):
        project=self.directory / mode; project.mkdir()
        environment=os.environ.copy()
        environment.update(GHVRQ_WIZARD_PROGRESS="1",GHVR_TEST_MODE=mode,GHVR_QUEST_NATIVE_SHADER_PYTHON=sys.executable,
            GHVR_QUEST_NATIVE_SHADER_HELPER=str(self.helper),GHVR_QUEST_NATIVE_SHADER_SOURCE=str(project))
        result=subprocess.run([self.dotnet,str(self.binary)],cwd=project,env=environment,text=True,capture_output=True,timeout=30)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        self.assertIn("PASS production native-audit receipt terminal: "+mode,result.stdout)

    def test_exact_receipt_closes_actual_bundle_counter(self): self.run_gate("success")
    def test_decoded_all_bundles_without_valid_receipt_stays_open(self): self.run_gate("bad-receipt")
    def test_missing_native_bundle_denominator_is_rejected(self): self.run_gate("bad-count")
    def test_failed_process_never_closes_audit_despite_existing_receipt(self): self.run_gate("failed")

    def test_native_receipt_and_relay_compile_against_pinned_unity_sdk(self):
        mono=DATA / "MonoBleedingEdge/bin/mono"; compiler=DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
        if not mono.is_file() or not compiler.is_file(): self.skipTest("Pinned Unity SDK required")
        source=self.directory / "SdkAudit.cs"; source.write_text(production())
        result=subprocess.run([str(mono),str(compiler),"-langversion:latest","-nowarn:0649","-define:UNITY_EDITOR","-target:library",
            "-out:"+str(self.directory / "ActualSdkAudit.dll"),
            *["-r:"+str(path) for path in sorted((DATA / "Managed/UnityEngine").glob("*.dll"))],
            "-r:"+str(DATA / "PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll"),
            str(source),str(EDITOR / "QuestWizardProgress.cs")],text=True,capture_output=True,timeout=30)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        self.assertNotIn("warning",result.stderr)


if __name__ == "__main__": unittest.main()
