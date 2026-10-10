"""Run the exact Editor compute gate through its public entry points.

The native Editor imports/compiler metadata are fault-injected; complete source
bytes, production hashing/parsing, retry scope and receipt publication are real.
A separate test compiles unchanged production C# against the pinned Unity SDK.
No original game content or full Unity project is imported by this fixture.
"""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignComputeValidation.cs"
UNITY_DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
NAMES = sorted(("EyeHistogram", "Lut3DBaker", "MultiScaleVODownsample2", "Vectorscope", "Histogram",
    "AutoExposure", "MultiScaleVOUpsample", "Waveform", "ExposureHistogram", "MultiScaleVODownsample1",
    "Texture3DLerp", "GaussianDownsample", "MultiScaleVORender"))
MANIFEST = "Assets/QuestOriginalCampaign/campaign-computes.json"

HARNESS = r'''
using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using System.Text.Json;
namespace UnityEngine.Rendering { public enum GraphicsDeviceType { Vulkan, OpenGLES3 } }
namespace UnityEngine {
 public class ComputeShader { public string name,path; }
 public static class Application { public static string unityVersion="2021.3.5f1"; }
 public static class Debug { public static List<string> Logs=new List<string>();public static void Log(object v){Logs.Add(v.ToString());} }
 public static class JsonUtility {
  static object Decode(JsonElement e,Type t){
   if(e.ValueKind==JsonValueKind.Null)return null;if(t==typeof(string))return e.GetString();if(t==typeof(int))return e.GetInt32();
   if(t==typeof(bool))return e.GetBoolean();if(t==typeof(long))return e.GetInt64();
   if(t.IsArray){var a=Array.CreateInstance(t.GetElementType(),e.GetArrayLength());int i=0;foreach(var v in e.EnumerateArray())a.SetValue(Decode(v,t.GetElementType()),i++);return a;}
   var r=Activator.CreateInstance(t,true);foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public))if(e.TryGetProperty(f.Name,out var v))f.SetValue(r,Decode(v,f.FieldType));return r;
  }
  public static T FromJson<T>(string text){using(var d=JsonDocument.Parse(text))return (T)Decode(d.RootElement,typeof(T));}
  public static string ToJson(object v,bool pretty=false)=>JsonSerializer.Serialize(v,new JsonSerializerOptions{IncludeFields=true});
 }
}
namespace UnityEditor.Rendering { public enum ShaderCompilerMessageSeverity { Warning,Error }public struct ShaderCompilerMessage {public string message,platform;public ShaderCompilerMessageSeverity severity;} }
namespace UnityEditor {
 public enum BuildTarget { Android }public enum BuildTargetGroup { Android }
 [Flags]public enum ImportAssetOptions {ForceUpdate=1,ForceSynchronousImport=2}
 [Flags]public enum BuildAssetBundleOptions {ChunkBasedCompression=1,ForceRebuildAssetBundle=2,StrictMode=4}
 public struct AssetBundleBuild {public string assetBundleName;public string[] assetNames;}
 public class AssetBundleManifest {public string[] GetAllAssetBundles()=>new string[]{"quest-compute-proof.bundle"};}
 public static class BuildPipeline {public static AssetBundleManifest BuildAssetBundles(string p,AssetBundleBuild[] m,BuildAssetBundleOptions o,BuildTarget t)=>new AssetBundleManifest();}
 public static class EditorUserBuildSettings {public static BuildTarget activeBuildTarget=BuildTarget.Android;public static bool SwitchActiveBuildTarget(BuildTargetGroup g,BuildTarget t)=>true;}
 public static class PlayerSettings {
  public static UnityEngine.Rendering.GraphicsDeviceType Required;
  public static bool GetUseDefaultGraphicsAPIs(BuildTarget t)=>false;public static void SetUseDefaultGraphicsAPIs(BuildTarget t,bool v){}
  public static UnityEngine.Rendering.GraphicsDeviceType[] GetGraphicsAPIs(BuildTarget t)=>new[]{Required};
  public static void SetGraphicsAPIs(BuildTarget t,UnityEngine.Rendering.GraphicsDeviceType[] v){Required=v[0];}
 }
 public static class AssetDatabase {
  public sealed class Row {public string assetPath,name,guid;public int localFileId;public Kernel[] kernels;}
  public sealed class Kernel {public string name;}
  public sealed class Input {public string graphicsApi;public Row[] shaders;}
  public sealed class Fault {public string path,before,after;}
  public sealed class Configuration {public Fault[] faults;}
  public static Dictionary<string,Row> Rows;
  static Dictionary<string,Fault> Faults;
  public static List<string> Imports=new List<string>();public static List<int> Options=new List<int>();
  public static void Initialize(string manifest,string setup){
   var input=UnityEngine.JsonUtility.FromJson<Input>(File.ReadAllText(manifest));Rows=input.shaders.ToDictionary(r=>r.assetPath);
   PlayerSettings.Required=input.graphicsApi=="OpenGLES3"?UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:UnityEngine.Rendering.GraphicsDeviceType.Vulkan;
   Faults=UnityEngine.JsonUtility.FromJson<Configuration>(File.ReadAllText(setup)).faults.ToDictionary(f=>f.path);
  }
  public static string State(string path)=>Faults.ContainsKey(path)?(Imports.Contains(path)?Faults[path].after:Faults[path].before):"correct";
  public static T LoadAssetAtPath<T>(string path) where T:class {
   string state=State(path);if(state=="missing"||state=="importthrows")return null;
   return new UnityEngine.ComputeShader{path=path,name=state=="name"?"stale name":Rows[path].name} as T;
  }
  public static bool TryGetGUIDAndLocalFileIdentifier(UnityEngine.ComputeShader shader,out string guid,out long id){
   string state=State(shader.path);guid=state=="guid"?new string('0',32):Rows[shader.path].guid;id=state=="fileid"?0:Rows[shader.path].localFileId;return state!="identifier";
  }
  public static void ImportAsset(string path,ImportAssetOptions options){string before=State(path);Imports.Add(path);Options.Add((int)options);if(before=="importthrows")throw new IOException("fixture import failure");}
 }
 public static class ShaderUtil {
  public static UnityEditor.Rendering.ShaderCompilerMessage[] GetComputeShaderMessages(UnityEngine.ComputeShader shader)=>AssetDatabase.State(shader.path)=="compiler"?
   new[]{new UnityEditor.Rendering.ShaderCompilerMessage{message="fixture stale compilation error",platform="Android",severity=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error}}:
   new[]{new UnityEditor.Rendering.ShaderCompilerMessage{message="fixture warning",platform="Android",severity=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning}};
  static int GetComputeShaderPlatformCount(UnityEngine.ComputeShader shader){string s=AssetDatabase.State(shader.path);return s=="bankmissing"?0:s=="bankduplicate"?2:1;}
  static UnityEngine.Rendering.GraphicsDeviceType GetComputeShaderPlatformType(UnityEngine.ComputeShader shader,int p)=>AssetDatabase.State(shader.path)=="bankapi"?
   (PlayerSettings.Required==UnityEngine.Rendering.GraphicsDeviceType.Vulkan?UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:UnityEngine.Rendering.GraphicsDeviceType.Vulkan):PlayerSettings.Required;
  static int GetComputeShaderPlatformKernelCount(UnityEngine.ComputeShader shader,int p)=>AssetDatabase.Rows[shader.path].kernels.Length;
  static string GetComputeShaderPlatformKernelName(UnityEngine.ComputeShader shader,int p,int k){var kernels=AssetDatabase.Rows[shader.path].kernels;return kernels[AssetDatabase.State(shader.path)=="bankorder"?kernels.Length-1-k:k].name;}
 }
}
namespace GloomhavenVR.Quest.Editor {
 public static class QuestWizardProgress {
  public static List<object> Events=new List<object>();
  public sealed class Counter {string phase;int total;public Counter(string p,string group,int t,string unit,string description){phase=p;total=t;}public void Report(int done,string detail){Events.Add(new{phase,done,total,detail,complete=false});}public void Complete(string detail){Events.Add(new{phase,done=total,total,detail,complete=true});}}
 }
}
class Program {
 static int Main(string[] args){
  UnityEditor.AssetDatabase.Initialize(args[0],args[1]);Environment.SetEnvironmentVariable("GHVR_QUEST_COMPUTE_MANIFEST",args[0]);
  object receipt=null;string error=null;
  try{if(args[2]=="sources")QuestCampaignComputeValidation.ValidateSources();else receipt=QuestCampaignComputeValidation.Validate(args[0],"Temp/compiled.json");}
  catch(Exception failure){error=failure.GetType().Name+": "+failure.Message;}
  Console.WriteLine(JsonSerializer.Serialize(new{receipt,error,imports=UnityEditor.AssetDatabase.Imports,options=UnityEditor.AssetDatabase.Options,
   logs=UnityEngine.Debug.Logs,events=GloomhavenVR.Quest.Editor.QuestWizardProgress.Events},new JsonSerializerOptions{IncludeFields=true}));return 0;
 }
}
'''


class ComputeEditorRepair(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
        if not Path(dotnet).is_file(): raise unittest.SkipTest("A local .NET SDK is required.")
        cls.temp = tempfile.TemporaryDirectory(prefix="quest-compute-editor-")
        cls.root = Path(cls.temp.name); cls.dotnet = dotnet
        (cls.root / "Program.cs").write_text(HARNESS, encoding="utf-8")
        shutil.copy2(SOURCE, cls.root / "Production.cs")
        (cls.root / "proof.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>UNITY_EDITOR</DefineConstants><Nullable>disable</Nullable></PropertyGroup></Project>', encoding="utf-8")
        cls.env = {**os.environ, "DOTNET_ROOT": str(Path(dotnet).parent), "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
        built = subprocess.run([dotnet, "build", "proof.csproj", "--nologo", "-v:q", "--disable-build-servers", "-p:UseSharedCompilation=false"], cwd=cls.root, env=cls.env, text=True, capture_output=True, timeout=60)
        if built.returncode: raise AssertionError(built.stdout + built.stderr)
        cls.program = cls.root / "bin/Debug/net8.0/proof.dll"

    @classmethod
    def tearDownClass(cls): cls.temp.cleanup()

    def setUp(self):
        self.case = tempfile.TemporaryDirectory(prefix="case-", dir=self.root); self.addCleanup(self.case.cleanup)
        self.project = Path(self.case.name); self.manifest = {"schema": 1, "shaderCount": 13, "kernelCount": 36, "graphicsApi": "Vulkan", "shaders": []}

    def fixture(self, newline="\n", api="Vulkan"):
        self.manifest["graphicsApi"] = api
        for index, name in enumerate(NAMES):
            kernels = [{"name": "K" + name + str(k), "threadGroups": [k + 1, 8, 1]} for k in range(3 if index < 10 else 2)]
            pragmas = ["#pragma kernel " + kernel["name"] + " QUEST_ORIGINAL_KERNEL_" + str(k) for k, kernel in enumerate(kernels)]
            functions = ["#if defined(QUEST_ORIGINAL_KERNEL_" + str(k) + ")\n[numthreads(" + str(k+1) + ", 8, 1)]\nvoid " + kernel["name"] + "(uint3 id : SV_DispatchThreadID) {}\n#endif" for k, kernel in enumerate(kernels)]
            data = ("\n".join(pragmas + functions) + "\n").replace("\n", newline).encode()
            path = self.project / ("Assets/Compute/" + name + ".compute"); path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
            guid = f"{index + 1:032x}"; meta = ("fileFormatVersion: 2\nguid: " + guid + "\nComputeShaderImporter:\n").replace("\n", newline).encode()
            Path(str(path) + ".meta").write_bytes(meta)
            self.manifest["shaders"].append({"name": name, "assetPath": path.relative_to(self.project).as_posix(), "guid": guid,
                "classId": 72, "localFileId": 7200000, "sourceSha256": hashlib.sha256(data).hexdigest(), "metaSha256": hashlib.sha256(meta).hexdigest(),
                "kernelCount": len(kernels), "kernels": kernels})

    def run_gate(self, mode="compiled", faults=()):
        manifest = self.project / MANIFEST; manifest.parent.mkdir(parents=True, exist_ok=True); manifest.write_text(json.dumps(self.manifest))
        setup = self.project / "setup.json"; setup.write_text(json.dumps({"faults": list(faults)}))
        before = {p.relative_to(self.project).as_posix(): p.read_bytes() for p in self.project.rglob("*") if p.is_file()}
        result = subprocess.run([self.dotnet, str(self.program), MANIFEST, "setup.json", mode], cwd=self.project, env=self.env, text=True, capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        row = json.loads(result.stdout.splitlines()[-1])
        for name, data in before.items(): self.assertEqual((self.project / name).read_bytes(), data)
        self.assertEqual((self.project / "Temp/compiled.json").is_file(), mode == "compiled" and row["error"] is None)
        return row

    def success(self, newline, api, mode):
        self.fixture(newline, api); row = self.run_gate(mode)
        self.assertIsNone(row["error"]); self.assertEqual(row["imports"], [])
        completed = [e for e in row["events"] if e["phase"] == "unity-compute-identities" and e["complete"]]
        self.assertEqual([(e["done"], e["total"]) for e in completed], [(13, 13)])
        kernel_totals = [e["total"] for e in row["events"] if e["phase"] == "unity-compute-kernels" and e["complete"]]
        self.assertEqual(sum(kernel_totals), 36)
        if mode == "compiled":
            receipt = row["receipt"]; self.assertEqual((receipt["shaderCount"], receipt["kernelCount"]), (13, 36))
            self.assertEqual(receipt["targetedReimportedShaders"], 0); self.assertTrue(receipt["androidCompiled"])
            self.assertFalse(receipt["hardwareVerified"]); self.assertEqual(receipt["graphicsApi"], api)

    def repair(self, state, after="correct", mode="compiled", api="Vulkan"):
        self.fixture("\r\n", api); path = self.manifest["shaders"][6]["assetPath"]
        row = self.run_gate(mode, [{"path": path, "before": state, "after": after}])
        self.assertEqual(row["imports"], [path]); self.assertEqual(row["options"], [3])
        self.assertTrue(any("attempt=1/1" in line for line in row["logs"]))
        if after == "correct" and state != "importthrows":
            self.assertIsNone(row["error"])
            self.assertEqual(sum(e["total"] for e in row["events"]
                if e["phase"] == "unity-compute-kernels" and e["complete"]), 36)
            if mode == "compiled":
                self.assertEqual(row["receipt"]["targetedReimportedShaders"], 1)
                self.assertEqual([r["assetPath"] for r in row["receipt"]["shaders"] if r["targetedReimported"]], [path])
        else:
            self.assertIn("attempt=1/1", row["error"])
            self.assertIn(path, row["error"])
            self.assertFalse(any(e["phase"] == "unity-compute-identities" and e["complete"] for e in row["events"]))

    def bad_source(self, kind):
        self.fixture("\r\n"); contract = self.manifest["shaders"][0]; path = self.project / contract["assetPath"]
        if kind == "source-sha": path.write_bytes(path.read_bytes() + b"changed")
        elif kind == "meta-sha": Path(str(path) + ".meta").write_bytes(b"changed")
        elif kind == "missing-source": path.unlink()
        elif kind == "missing-meta": Path(str(path) + ".meta").unlink()
        elif kind == "guid":
            meta = Path(str(path) + ".meta"); meta.write_bytes(meta.read_bytes().replace(contract["guid"].encode(), b"0" * 32)); contract["metaSha256"] = hashlib.sha256(meta.read_bytes()).hexdigest()
        elif kind == "duplicate-meta-guid":
            meta = Path(str(path) + ".meta"); meta.write_bytes(meta.read_bytes() + b"guid: " + contract["guid"].encode() + b"\r\n"); contract["metaSha256"] = hashlib.sha256(meta.read_bytes()).hexdigest()
        elif kind == "missing-contract-group": contract["kernels"][0]["threadGroups"] = None
        elif kind == "incomplete-contract-group": contract["kernels"][0]["threadGroups"] = [1, 8]
        else:
            data = path.read_bytes()
            if kind == "kernel-order":
                first, second = contract["kernels"][:2]; data = data.replace(first["name"].encode(), b"TEMP").replace(second["name"].encode(), first["name"].encode()).replace(b"TEMP", second["name"].encode())
            elif kind == "pragma-index": data = data.replace(b" QUEST_ORIGINAL_KERNEL_0", b" QUEST_ORIGINAL_KERNEL_4", 1)
            elif kind == "define-index": data = data.replace(b"defined(QUEST_ORIGINAL_KERNEL_0)", b"defined(QUEST_ORIGINAL_KERNEL_4)", 1)
            elif kind == "threadgroups": data = data.replace(b"numthreads(1, 8, 1)", b"numthreads(2, 8, 1)", 1)
            elif kind == "overflow-threadgroups": data = data.replace(b"numthreads(1, 8, 1)", b"numthreads(99999999999999, 8, 1)", 1)
            elif kind == "missing-kernel": data = data.replace(b"#pragma kernel", b"//pragma kernel", 1)
            elif kind == "duplicate-kernel": data += data.split(b"\r\n", 1)[0] + b"\r\n"
            elif kind == "duplicate-group": data += b"[numthreads(1, 8, 1)]\r\nvoid " + contract["kernels"][0]["name"].encode() + b"() {}\r\n"
            elif kind == "missing-group": data = data.replace(b"[numthreads(1, 8, 1)]", b"// dispatch omitted", 1)
            elif kind == "missing-define": data = data.replace(b"#if defined(QUEST_ORIGINAL_KERNEL_0)", b"#if 1", 1)
            path.write_bytes(data); contract["sourceSha256"] = hashlib.sha256(data).hexdigest()
        row = self.run_gate(); self.assertIsNotNone(row["error"]); self.assertEqual(row["imports"], [])
        self.assertIn(contract["assetPath"], row["error"])


for newline, label in (("\n", "lf"), ("\r\n", "crlf")):
    for api in ("Vulkan", "OpenGLES3"):
        for mode in ("sources", "compiled"):
            setattr(ComputeEditorRepair, f"test_complete_13_36_{label}_{api}_{mode}", lambda self, n=newline, a=api, m=mode: self.success(n, a, m))
for state in ("missing", "name", "guid", "fileid", "identifier", "compiler", "bankmissing", "bankorder", "bankduplicate", "bankapi"):
    setattr(ComputeEditorRepair, "test_targeted_repair_" + state, lambda self, s=state: self.repair(s))
    setattr(ComputeEditorRepair, "test_failed_repair_is_bounded_" + state, lambda self, s=state: self.repair(s, s))
for state in ("missing", "guid", "compiler"):
    setattr(ComputeEditorRepair, "test_sources_repair_" + state, lambda self, s=state: self.repair(s, mode="sources"))
for state in ("bankmissing", "bankorder", "bankduplicate", "bankapi"):
    setattr(ComputeEditorRepair, "test_gles_bank_repair_" + state, lambda self, s=state: self.repair(s, api="OpenGLES3"))
for kind in ("source-sha", "meta-sha", "missing-source", "missing-meta", "guid", "duplicate-meta-guid", "kernel-order",
        "pragma-index", "define-index", "threadgroups", "overflow-threadgroups", "missing-contract-group", "incomplete-contract-group",
        "missing-kernel", "duplicate-kernel", "duplicate-group", "missing-group", "missing-define"):
    setattr(ComputeEditorRepair, "test_unproved_source_not_reimported_" + kind.replace("-", "_"), lambda self, k=kind: self.bad_source(k))
setattr(ComputeEditorRepair, "test_import_exception_is_bounded", lambda self: self.repair("importthrows"))


class PinnedComputeEditorSdk(unittest.TestCase):
    def test_exact_production_compiles_with_pinned_editor_sdk(self):
        mono = UNITY_DATA / "MonoBleedingEdge/bin/mono"; compiler = UNITY_DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
        if not mono.is_file() or not compiler.is_file(): self.skipTest("Pinned Unity compiler is unavailable.")
        refs = sorted((UNITY_DATA / "Managed/UnityEngine").glob("*.dll"))
        with tempfile.TemporaryDirectory(prefix="quest-compute-sdk-") as folder:
            stub = Path(folder) / "Progress.cs"
            stub.write_text('namespace GloomhavenVR.Quest.Editor { public static class QuestWizardProgress { public sealed class Counter { public Counter(string a,string b,int c,string d,string e){} public void Report(int a,string b){} public void Complete(string a){} } } }')
            result = subprocess.run([str(mono), str(compiler), "-target:library", "-define:UNITY_EDITOR", "-langversion:latest", "-out:" + str(Path(folder) / "Compute.dll"), *["-r:" + str(ref) for ref in refs], str(SOURCE), str(stub)], text=True, capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__": unittest.main()
