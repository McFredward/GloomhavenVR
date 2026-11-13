"""Run the production Editor relay against real child pipes, without an import.

API spies replace only Unity logging/callbacks. The actual process, pipe draining,
bounded observations, terminal tail and counter cadence execute unmodified.
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

PROGRAM = r'''
class Program {
 static int checks;
 static void Check(bool value,string name){++checks;if(!value)throw new System.Exception(name);}
 static void Main(string[] args) {
  bool enabled=args[2]!="disabled";
  System.Environment.SetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS",enabled?"1":"0");
  string marker=System.Environment.GetEnvironmentVariable("GHVR_TEST_MARKER");
  GloomhavenVR.Quest.Editor.QuestWizardProgress.Operation("content-bank",false,"Actual child audit");
  int before=UnityEngine.Debug.Lines.Count;
  bool live=false;
  UnityEngine.Debug.Observed=line=>{if(line.Contains("\"done\":524288") && !System.IO.File.Exists(marker))live=true;};
  using(var child=new System.Diagnostics.Process()) {
   child.StartInfo=new System.Diagnostics.ProcessStartInfo {
    FileName=args[0],Arguments="\""+args[1]+"\"",
    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
   };
   string error=GloomhavenVR.Quest.Editor.QuestWizardProgress.RunObservedProcess(child);
   Check(child.ExitCode==(args[2]=="failed"?7:0),"real child exit code preserved");
   Check(error.Length==4096,"stderr bounded while entire pipe is drained");
   Check(System.IO.File.Exists(marker),"helper executed to its original completion");
  }
  if(enabled) {
   Check(live,"measured byte progress arrived before helper exit");
   Check(UnityEngine.Debug.Lines.Count-before==3,"only bounded progress records relayed");
   string tail=UnityEngine.Debug.Lines[UnityEngine.Debug.Lines.Count-1];
   Check(tail.Contains(args[2]=="failed"?"\"status\":\"failed\"":"\"status\":\"progress\""),"final native child tail retained");
   Check(!tail.Contains("operation:content-bank"),"native child cannot close its owning operation");
   var counter=new GloomhavenVR.Quest.Editor.QuestWizardProgress.Counter("unity-observed-cadence","content-bank",4,"assets","Actual cadence");
   counter.Report(1,"First original asset");
   int sampled=UnityEngine.Debug.Lines.Count;
   System.Threading.Thread.Sleep(300);
   counter.Report(2,"Second original asset");
   Check(UnityEngine.Debug.Lines.Count==sampled+1 && UnityEngine.Debug.Lines[UnityEngine.Debug.Lines.Count-1].Contains("\"done\":2"),"actual observations publish between old half-second boundaries");
   counter.Complete("Original child loop returned");
   Check(UnityEngine.Debug.Lines[UnityEngine.Debug.Lines.Count-1].Contains("\"status\":\"complete\""),"terminal child bypasses cadence");
  } else Check(UnityEngine.Debug.Lines.Count==before,"disabled observer preserves work and emits no records");
  System.Console.WriteLine("PASS actual Editor helper relay: "+checks+" checks");
 }
}
'''

HELPER = r'''
import json, os, pathlib, sys, time
def emit(done, status="progress"):
    print("GHVRQ_PROGRESS " + json.dumps({"schema":1,"phase":"unity-native-shader-audit-read",
        "done":done,"total":1048576,"unit":"bytes","detail":"Actual native bytes",
        "status":status,"operation":"content-bank"}, separators=(",", ":")), flush=True)
emit(0, "start")
print("ordinary helper output is not a progress record", flush=True)
print("GHVRQ_PROGRESS " + "x" * 9000, flush=True)
os.write(2, b"x" * 1048576)
emit(524288)
time.sleep(.7)
pathlib.Path(os.environ["GHVR_TEST_MARKER"]).write_text("original helper returned")
emit(1048576, "failed" if os.environ["GHVR_TEST_MODE"] == "failed" else "progress")
sys.exit(7 if os.environ["GHVR_TEST_MODE"] == "failed" else 0)
'''


class ActualEditorHelperProgress(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dotnet = shutil.which("dotnet") or "/home/claw/.dotnet/dotnet"
        if not Path(cls.dotnet).is_file():
            raise unittest.SkipTest(".NET SDK required")
        cls.temporary = tempfile.TemporaryDirectory(prefix="quest-live-editor-helper-")
        cls.addClassCleanup(cls.temporary.cleanup)
        cls.directory = Path(cls.temporary.name)
        spec = importlib.util.spec_from_file_location("editor_spies", Path(__file__).with_name("test_editor_phase_progress.py"))
        spies = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(spies)
        stubs = spies.STUBS.split("class Program {")[0]
        stubs = stubs.replace("public static void Log(object value){Lines.Add(value.ToString());}",
            "public static System.Action<string> Observed; public static void Log(object value){Lines.Add(value.ToString());Observed?.Invoke(value.ToString());}")
        (cls.directory / "Relay.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>UNITY_EDITOR</DefineConstants><NoWarn>CS0067;CS0649</NoWarn></PropertyGroup></Project>')
        (cls.directory / "Program.cs").write_text(stubs + PROGRAM)
        (cls.directory / "QuestWizardProgress.cs").write_bytes((EDITOR / "QuestWizardProgress.cs").read_bytes())
        cls.helper = cls.directory / "native audit helper.py"
        cls.helper.write_text(HELPER)
        result = subprocess.run([cls.dotnet, "build", str(cls.directory / "Relay.csproj"), "--verbosity", "quiet"],
            text=True, capture_output=True, timeout=60)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)
        cls.binary = cls.directory / "bin/Debug/net8.0/Relay.dll"

    def run_helper(self, mode):
        environment = os.environ.copy()
        environment["GHVR_TEST_MODE"] = mode
        environment["GHVR_TEST_MARKER"] = str(self.directory / (mode + ".completed"))
        result = subprocess.run([self.dotnet, str(self.binary), sys.executable, str(self.helper), mode],
            env=environment, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("PASS actual Editor helper relay:", result.stdout)

    def test_actual_progress_arrives_before_child_exit_and_stderr_cannot_deadlock(self):
        self.run_helper("success")

    def test_failure_preserves_actual_exit_and_progress_tail_without_owner_completion(self):
        self.run_helper("failed")

    def test_disabled_observer_drains_and_executes_helper_without_logging(self):
        self.run_helper("disabled")


if __name__ == "__main__":
    unittest.main()
