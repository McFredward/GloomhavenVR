"""Prove the pinned Unity Player handoff without rebuilding or importing game assets."""
from pathlib import Path
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
UNITY = Path("/home/claw/unity-2021.3.5/Editor/Data")
EDITOR = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestWizardProgress.cs"
CORE = UNITY / "Managed/UnityEngine/UnityEditor.CoreModule.dll"
ANDROID = UNITY / "PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll"
CECIL = UNITY / "MonoBleedingEdge/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll"


INSPECTOR = r'''
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
class Program {
 static int checks;
 static void Check(bool value,string message) { ++checks;if(!value)throw new Exception(message); }
 static MethodDefinition Method(ModuleDefinition module,string type,string name,int parameters) {
  return module.GetType(type).Methods.Single(row=>row.Name==name && row.Parameters.Count==parameters);
 }
 static string[] Calls(MethodDefinition method) {
  return method.Body.Instructions.Where(row=>row.OpCode.Code==Code.Call || row.OpCode.Code==Code.Callvirt)
   .Select(row=>((MethodReference)row.Operand).DeclaringType.FullName+"::"+((MethodReference)row.Operand).Name).ToArray();
 }
 static bool Ordered(MethodDefinition method,params string[] names) {
  var calls=Calls(method);int index=-1;
  foreach(string name in names) {index=Array.FindIndex(calls,index+1,row=>row==name);if(index<0)return false;}
  return true;
 }
 static bool Literal(MethodDefinition method,string value) {
  return method.Body.Instructions.Any(row=>row.OpCode.Code==Code.Ldstr && (string)row.Operand==value);
 }
 static void Main(string[] args) {
  using(var core=ModuleDefinition.ReadModule(args[0]))using(var android=ModuleDefinition.ReadModule(args[1])) {
   var dispatch=Method(core,"UnityEditor.Modules.DefaultBuildPostprocessor","PostProcess",2);
   Check(Calls(dispatch).Contains("UnityEditor.Modules.DefaultBuildPostprocessor::PostProcess"),"out-properties wrapper invokes platform virtual postprocessing");
   Check(dispatch.Body.Instructions.Any(row=>row.OpCode.Code==Code.Callvirt && row.Operand is MethodReference && ((MethodReference)row.Operand).Name=="PostProcess"),"wrapper preserves virtual Bee dispatch");
   var platform=Method(android,"UnityEditor.Android.AndroidBuildPostprocessor","PostProcess",2);
   Check(Ordered(platform,"UnityEditor.Android.AndroidBuildPostprocessor::PrepareForBuildProgram","UnityEditor.Modules.DefaultBuildPostprocessor::PostProcess","UnityEditor.Android.PostProcessAndroidPlayer::PostProcess"),"Android prepares platform inputs, executes Bee, then packages the result");
   var bee=Method(core,"UnityEditor.Modules.BeeBuildPostprocessor","PostProcess",1);
   string[] boundary={"UnityEditor.Modules.BeeBuildPostprocessor::SetupBeeDriver","Bee.BeeDriver.BeeDriver::BuildAsync","Bee.BeeDriver.BeeDriver::Tick","UnityEditor.Modules.DefaultBuildPostprocessor::PostProcessCompletedBuild","UnityEditor.Modules.BeeBuildPostprocessor::ReportBuildResults"};
   Check(Ordered(bee,boundary),"Player Bee construction precedes asynchronous native execution and result reporting");
   Check(Literal(bee,"Player") && Literal(bee,"Incremental player build"),"observed graph is the Player graph rather than a script/import graph");
   var setup=Method(core,"UnityEditor.Modules.BeeBuildPostprocessor","SetupBeeDriver",1);
   Check(Ordered(setup,"UnityEditor.Modules.BeeBuildPostprocessor::MakePlayerBuildProgram","UnityEditor.Scripting.ScriptCompilation.UnityBeeDriver::Make"),"actual Player executable launches through the native Bee driver");
   var executable=Method(core,"UnityEditor.Modules.BeeBuildPostprocessor","MakePlayerBuildProgram",1);
   Check(Literal(executable,"PlayerBuildProgram.exe"),"actual platform executable suffix anchors the log boundary");
   Check(Calls(executable).Contains("UnityEditorInternal.IL2CPPUtils::GetExePath"),"IL2CPP belongs to this same native Player producer");
   var inputs=Method(core,"UnityEditor.Modules.BeeBuildPostprocessor","PlayerBuildConfigFor",1);
   Check(inputs.Body.Instructions.Any(row=>row.OpCode.Code==Code.Ldfld && row.Operand is FieldReference && ((FieldReference)row.Operand).Name=="stagingArea"),"platform graph receives the previously prepared staging area");
   Check(inputs.Body.Instructions.Any(row=>row.OpCode.Code==Code.Stfld && row.Operand is FieldReference && ((FieldReference)row.Operand).Name=="UseIl2Cpp"),"graph carries the current Player IL2CPP choice");
   // This scope is a platform handoff. Native C++ scene serialization is not
   // public C# source, so a final scene callback alone is not its completion.
   var start=bee.Body.Instructions.First(row=>row.Operand is MethodReference && ((MethodReference)row.Operand).Name=="BuildAsync");
   var opcode=start.OpCode;start.OpCode=OpCodes.Nop;
   Check(!Ordered(bee,boundary),"negative control: removing actual graph execution cannot prove the handoff");start.OpCode=opcode;
   var prepare=platform.Body.Instructions.First(row=>row.Operand is MethodReference && ((MethodReference)row.Operand).Name=="PrepareForBuildProgram");
   var post=platform.Body.Instructions.First(row=>row.Operand is MethodReference && ((MethodReference)row.Operand).DeclaringType.FullName=="UnityEditor.Modules.DefaultBuildPostprocessor");
   var operand=prepare.Operand;prepare.Operand=post.Operand;post.Operand=operand;
   Check(!Ordered(platform,"UnityEditor.Android.AndroidBuildPostprocessor::PrepareForBuildProgram","UnityEditor.Modules.DefaultBuildPostprocessor::PostProcess","UnityEditor.Android.PostProcessAndroidPlayer::PostProcess"),"negative control: reversed preparation/execution order must fail");
   Console.WriteLine("PASS pinned Unity Player platform boundary: "+checks+" checks; actual DLL IL, no native game build.");
  }
 }
}
'''


SCENE_PROGRAM = r'''
class Program {
 static void Main() {
  System.Environment.SetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS","1");
  var callback=new GloomhavenVR.Quest.Editor.QuestWizardBuildProgress();
  var report=new UnityEditor.Build.Reporting.BuildReport();callback.OnPreprocessBuild(report);
  callback.OnProcessScene(new UnityEngine.SceneManagement.Scene{path="Addressables scene"},null);
  callback.OnProcessScene(new UnityEngine.SceneManagement.Scene{path="Player first"},report);
  callback.OnProcessScene(new UnityEngine.SceneManagement.Scene{path="Player last"},report);
  string line=UnityEngine.Debug.Lines[UnityEngine.Debug.Lines.Count-1];
  var row=System.Text.Json.JsonDocument.Parse(line.Substring(15)).RootElement;
  if(row.GetProperty("done").GetInt32()!=2 || row.GetProperty("total").GetInt32()!=2)
   throw new System.Exception("actual Player callback census is separate from Addressables");
  if(row.GetProperty("status").GetString()!="progress")
   throw new System.Exception("the final scene callback entry cannot close native scene serialization");
  if(UnityEngine.Debug.Lines.Exists(value=>value.Contains("\"phase\":\"operation:player\"") && value.Contains("\"status\":\"complete\"")))
   throw new System.Exception("scene count cannot close the parent Player build");
  System.Console.WriteLine("PASS final scene entry is not a native serialization terminal: 3 checks.");
 }
}
'''


class PlayerPlatformBoundaryTests(unittest.TestCase):
    def test_actual_pinned_player_platform_dispatch_and_staging_inputs(self):
        mono = UNITY / "MonoBleedingEdge/bin/mono"
        compiler = UNITY / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
        if not all(path.is_file() for path in (mono, compiler, CORE, ANDROID, CECIL)):
            self.skipTest("Pinned Unity 2021.3.5 SDK and Cecil required")
        with tempfile.TemporaryDirectory(prefix="quest-player-boundary-") as directory:
            path = Path(directory)
            source = path / "Proof.cs"
            source.write_text(INSPECTOR)
            output = path / "Proof.exe"
            compile_result = subprocess.run([str(mono), str(compiler), "-langversion:latest", "-r:" + str(CECIL),
                "-out:" + str(output), str(source)], text=True, capture_output=True)
            self.assertEqual(compile_result.returncode, 0, compile_result.stdout + compile_result.stderr)
            shutil.copyfile(CECIL, path / "Mono.Cecil.dll")
            result = subprocess.run([str(mono), str(output), str(CORE), str(ANDROID)], text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("12 checks; actual DLL IL", result.stdout)
            destination = os.environ.get("GHVRQ_PINNED_BOUNDARY_RECEIPT")
            if destination:
                rows = [{"path": str(value), "size": value.stat().st_size,
                         "sha256": hashlib.sha256(value.read_bytes()).hexdigest()} for value in (CORE, ANDROID, CECIL)]
                Path(destination).write_text(json.dumps({"schema": 1, "scope": "Pinned Unity Player platform handoff IL; not native scene serialization or an APK acceptance claim",
                    "checks": 12, "sourceDlls": rows, "stdout": result.stdout.strip()}, indent=2) + "\n")

    def test_final_scene_entry_never_closes_native_serialization_or_player(self):
        dotnet = shutil.which("dotnet") or "/home/claw/.dotnet/dotnet"
        if not Path(dotnet).is_file():
            self.skipTest(".NET SDK required")
        spec = importlib.util.spec_from_file_location("editor_phase_stubs", Path(__file__).with_name("test_editor_phase_progress.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory(prefix="quest-scene-boundary-") as directory:
            path = Path(directory)
            (path / "Proof.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>UNITY_EDITOR</DefineConstants><NoWarn>CS0067;CS0649</NoWarn></PropertyGroup></Project>')
            (path / "QuestWizardProgress.cs").write_bytes(EDITOR.read_bytes())
            (path / "Proof.cs").write_text(module.STUBS.split("class Program {", 1)[0] + SCENE_PROGRAM)
            result = subprocess.run([dotnet, "run", "--project", str(path / "Proof.csproj"), "--no-launch-profile", "--verbosity", "quiet"],
                cwd=path, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("3 checks", result.stdout)


if __name__ == "__main__":
    unittest.main()
