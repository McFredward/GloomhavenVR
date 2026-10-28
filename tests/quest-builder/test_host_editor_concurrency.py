"""Compile the actual Editor adapter SDK and route its unchanged body on Unity Mono.

Native Editor acceptance is not claimed: the hosted run substitutes engine calls,
but executes real Mono readonly reflection, exception restoration and the exact
production adapter. Windows CreateProcess/end-to-end builds need Windows hardware.
"""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
MONO = DATA / "MonoBleedingEdge/bin/mono"
MCS = DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuildConcurrency.cs"


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Installed Unity SDK/Mono unavailable")
class EditorConcurrency(unittest.TestCase):
    def test_actual_adapter_compiles_against_sdk_and_restores_real_mono_readonly_field(self):
        with tempfile.TemporaryDirectory(prefix="quest-host-editor-") as folder:
            root = Path(folder)
            stub = root / "QuestBuild.cs"
            stub.write_text("namespace GloomhavenVR.Quest.Editor { public static class QuestBuild { public static void Build() {} } }")
            references = [DATA / "Managed/UnityEngine" / name for name in ("UnityEngine.CoreModule.dll", "UnityEditor.CoreModule.dll")]
            sdk = subprocess.run([str(MONO), str(MCS), "-define:UNITY_EDITOR", "-target:library", "-out:" + str(root / "Adapter.dll"),
                                  *["-r:" + str(path) for path in references], str(SOURCE), str(stub)], capture_output=True, text=True)
            self.assertEqual(sdk.returncode, 0, sdk.stdout + sdk.stderr)
            fixture = root / "Fixture.cs"
            fixture.write_text(r'''
using System; using System.IO; using System.Reflection;
namespace UnityEngine {
 public enum RuntimePlatform { LinuxEditor, WindowsEditor }
 public static class Application { public static string unityVersion="2021.3.5f1"; public static RuntimePlatform platform=RuntimePlatform.LinuxEditor; }
 public static class Debug { public static void Log(object text) {} }
}
namespace UnityEditor {
 public static class EditorApplication { public static string applicationContentsPath=Environment.GetEnvironmentVariable("GHVRQ_FIXTURE_ROOT"); }
 public static class SessionState { private static string hash=""; public static string GetString(string key,string fallback){return hash;} public static void SetString(string key,string value){hash=value;} }
}
namespace UnityEditor.Scripting.ScriptCompilation {
 internal static class UnityBeeDriver { internal static readonly string BeeBackendExecutable=Path.Combine(UnityEditor.EditorApplication.applicationContentsPath,"bee_backend"); }
}
namespace GloomhavenVR.Quest.Editor {
 public static class QuestBuild {
  public static int calls; public static bool fail;
  public static void Build() {
   calls++;
   if(UnityEditor.Scripting.ScriptCompilation.UnityBeeDriver.BeeBackendExecutable != Environment.GetEnvironmentVariable("GHVRQ_BEE_LAUNCHER"))throw new Exception("routing not applied");
   if(UnityEditor.SessionState.GetString("BeeBackendHash", "") == "")throw new Exception("real backend identity missing");
   if(fail) throw new IOException("original build error");
  }
 }
}
class Program {
 static int assertions;
 static void Check(bool value,string message){ assertions++; if(!value)throw new Exception(message); }
 static void Reject(Action action,string text){try{action();throw new Exception("negative accepted");}catch(InvalidOperationException e){Check(e.Message.Contains(text),e.Message);}}
 static int Main(string[] args){try{
  string root=args[0]; UnityEditor.EditorApplication.applicationContentsPath=root;
  string backend=Path.Combine(root,"bee_backend"), launcher=Path.Combine(root,"launcher");
  File.WriteAllText(backend,"actual backend unchanged"); File.WriteAllText(launcher,"private native apphost");
  Environment.SetEnvironmentVariable("GHVRQ_BEE_REAL_PATH",backend); Environment.SetEnvironmentVariable("GHVRQ_BEE_LAUNCHER",launcher); Environment.SetEnvironmentVariable("GHVRQ_BEE_THREADS","3");
  GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build();
  Check(GloomhavenVR.Quest.Editor.QuestBuild.calls==1,"original build not called");
  Check(UnityEditor.Scripting.ScriptCompilation.UnityBeeDriver.BeeBackendExecutable==backend,"normal restore missing");
  Check(UnityEditor.SessionState.GetString("BeeBackendHash","")=="","cache identity not restored");
  Check(File.ReadAllText(backend)=="actual backend unchanged","installed backend edited");
  GloomhavenVR.Quest.Editor.QuestBuild.fail=true;
  try{GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build();throw new Exception("original error swallowed");}catch(IOException e){Check(e.Message=="original build error","original failure changed");}
  Check(UnityEditor.Scripting.ScriptCompilation.UnityBeeDriver.BeeBackendExecutable==backend,"exception restore missing");
  Check(UnityEditor.SessionState.GetString("BeeBackendHash","")=="","exception identity restore missing");
  foreach(string jobs in new[]{"0","1025","malicious"}){Environment.SetEnvironmentVariable("GHVRQ_BEE_THREADS",jobs);Reject(()=>GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Begin(),"missing");}
  Environment.SetEnvironmentVariable("GHVRQ_BEE_THREADS","3");
  UnityEngine.Application.unityVersion="2021.3.6f1";Reject(()=>GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Begin(),"2021.3.5f1");UnityEngine.Application.unityVersion="2021.3.5f1";
  Environment.SetEnvironmentVariable("GHVRQ_BEE_REAL_PATH",launcher);Reject(()=>GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Begin(),"unexpected");Environment.SetEnvironmentVariable("GHVRQ_BEE_REAL_PATH",backend);
  UnityEditor.SessionState.SetString("BeeBackendHash","stale hash");Reject(()=>GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Begin(),"changed");UnityEditor.SessionState.SetString("BeeBackendHash","");
  File.Delete(launcher);Reject(()=>GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Begin(),"missing");
  Check(UnityEditor.Scripting.ScriptCompilation.UnityBeeDriver.BeeBackendExecutable==backend,"rejection changed routing");
  Console.WriteLine("PASS actual adapter on Unity Mono: "+assertions+" assertions");return 0;
 }catch(Exception e){Console.WriteLine(e);return 2;}}
}
''')
            exe = root / "Witness.exe"
            compiled = subprocess.run([str(MONO), str(MCS), "-define:UNITY_EDITOR", "-out:" + str(exe), str(SOURCE), str(fixture)],
                                      capture_output=True, text=True)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            result = subprocess.run([str(MONO), str(exe), str(root)], env={**os.environ, "GHVRQ_FIXTURE_ROOT": str(root)}, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("15 assertions", result.stdout)


if __name__ == "__main__": unittest.main()
