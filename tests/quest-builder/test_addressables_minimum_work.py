"""Exercise real catalog registration without loading complete asset closures.

Native Addressables/import calls are bounded spies. The production key/label,
group ownership, per-path/per-GUID reuse and optional QA branches execute on the
pinned Unity Mono. This does not prove cooked bundles or headset presentation.
"""
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
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Security.Cryptography;
using UnityEngine;using UnityEditor;using UnityEditor.AddressableAssets;using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.AddressableAssets;
static class Spy {public static Dictionary<string,int> Calls=new Dictionary<string,int>();
 public static void Call(string name){Calls[name]=Calls.ContainsKey(name)?Calls[name]+1:1;}}
namespace UnityEngine {
 public class Object {public string name;}
 public static class JsonUtility {
  public static T FromJson<T>(string text)=>new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(text);
  public static string ToJson(object value,bool pretty=false)=>new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value);
 }
 public static class Debug {public static void Log(object value){}}
}
namespace UnityEngine.AddressableAssets {public static class Addressables {public static string BuildPath="Native/Android";}}
namespace UnityEditor {
 public enum ImportAssetOptions {ForceSynchronousImport}
 public static class EditorUtility {public static void SetDirty(object value){}}
 public static class AssetDatabase {
  public static void Refresh(){}public static void Refresh(ImportAssetOptions value){}
  public static void StartAssetEditing(){}public static void StopAssetEditing(){}public static void SaveAssets(){}
  public static T LoadAssetAtPath<T>(string path) where T:class {return null;}
  public static string AssetPathToGUID(string path){Spy.Call("guid");return new string(path.Contains("Hero")?'a':'b',32);}
  public static object LoadMainAssetAtPath(string path){Spy.Call("load-main");return new object();}
  public static UnityEngine.Object[] LoadAllAssetsAtPath(string path){Spy.Call("load-subobjects");return new[]{new UnityEngine.Object{name="Sword"}};}
 }
}
namespace UnityEditor.AddressableAssets {public static class AddressableAssetSettingsDefaultObject {
 public const string kDefaultConfigFolder="Assets/AddressableAssetsData";
 public static UnityEditor.AddressableAssets.Settings.AddressableAssetSettings Settings;
}}
namespace UnityEditor.AddressableAssets.Build {public sealed class AddressablesPlayerBuildResult {public string Error;}}
namespace UnityEditor.AddressableAssets.Settings.GroupSchemas {
 public sealed class BundledAssetGroupSchema {
  public enum BundlePackingMode {PackTogether}public BundlePackingMode BundleMode;
  public bool IncludeAddressInCatalog,IncludeGUIDInCatalog,IncludeLabelsInCatalog;
 }
 public sealed class ContentUpdateGroupSchema {}
}
namespace UnityEditor.AddressableAssets.Settings {
 public sealed class AddressableAssetEntry {
  public string guid,address;public AddressableAssetGroup parentGroup;public HashSet<string> labels=new HashSet<string>();
  public void SetLabel(string name,bool value,bool a,bool b){if(value)labels.Add(name);else labels.Remove(name);}
 }
 public sealed class AddressableAssetGroup {
  public string name;Dictionary<Type,object> schemas=new Dictionary<Type,object>();
  public T GetSchema<T>() where T:class {return schemas.ContainsKey(typeof(T))?(T)schemas[typeof(T)]:null;}
  public T AddSchema<T>(bool ignored) where T:class,new(){var schema=new T();schemas[typeof(T)]=schema;return schema;}
 }
 public sealed class AddressableAssetSettings {
  public enum PlayerBuildOption {DoNotBuildWithPlayer}public PlayerBuildOption BuildAddressablesWithPlayerBuild;
  public AddressableAssetGroup DefaultGroup;public bool BuildRemoteCatalog,DisableCatalogUpdateOnStartup;
  public Dictionary<string,AddressableAssetGroup> Groups=new Dictionary<string,AddressableAssetGroup>();
  public Dictionary<string,AddressableAssetEntry> Entries=new Dictionary<string,AddressableAssetEntry>();
  public static AddressableAssetSettings Create(string a,string b,bool c,bool d){Spy.Call("settings-create");return new AddressableAssetSettings();}
  public static void BuildPlayerContent(out AddressablesPlayerBuildResult result){Spy.Call("native-build");Directory.CreateDirectory(Addressables.BuildPath);File.WriteAllText(Path.Combine(Addressables.BuildPath,"settings.json"),"{}");result=new AddressablesPlayerBuildResult();}
  public AddressableAssetGroup FindGroup(string name)=>Groups.ContainsKey(name)?Groups[name]:null;
  public AddressableAssetGroup CreateGroup(string name,bool a,bool b,bool c,object d,params Type[] types){var group=new AddressableAssetGroup{name=name};Groups.Add(name,group);return group;}
  public AddressableAssetEntry CreateOrMoveEntry(string guid,AddressableAssetGroup owner,bool a,bool b){Spy.Call("entry-create");var entry=new AddressableAssetEntry{guid=guid,parentGroup=owner};Entries.Add(guid,entry);return entry;}
  public void AddLabel(string name,bool a){}
 }
}
namespace GloomhavenVR.Quest.Editor {
 public static class QuestWizardProgress {
  public static void Publish(string phase,string detail,double done=-1,double total=-1,string unit=null,string operation=null,string status="progress"){}
  public sealed class Counter {public Counter(string a,string b,long c,string d,string e){}public void Report(long n,string d){}public void Complete(string d){}}
 }
}
'''

PROGRAM = r'''
class Program {
 static int Main(){try {
  GloomhavenVR.Quest.Editor.QuestStartupAddressablesBuild.Build();
  var settings=AddressableAssetSettingsDefaultObject.Settings;
  Console.WriteLine(JsonUtility.ToJson(new {success=true,calls=Spy.Calls,groups=settings.Groups.Count,
   entries=settings.Entries.Values.OrderBy(e=>e.guid).Select(e=>new {guid=e.guid,address=e.address,owner=e.parentGroup.name,labels=e.labels.OrderBy(s=>s).ToArray()}).ToArray()}));
  return 0;
 }catch(Exception error){Console.WriteLine(JsonUtility.ToJson(new {success=false,message=error.Message,calls=Spy.Calls}));return 2;}}
}
'''


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity Mono compiler unavailable")
class MinimumCatalogWorkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-minimum-catalog-")
        self.root = Path(self.temp.name)
        source = (EDITOR / "QuestStartupAddressablesBuild.cs").read_text()
        names = ("sealed class Association", "sealed class Associations", "sealed class ContentFile",
            "sealed class ContentManifest", "sealed class Alias", "sealed class RuntimeManifest",
            "sealed class NativeShaderReceipt", "public static void Build()",
            "internal static AddressableAssetGroup GetOrCreateOwnedGroup(", "static string NativeKey(",
            "static string StableName(", "static void RecordOmittedNativeShaderAudit()")
        build = (EDITOR / "QuestBuild.cs").read_text()
        helpers = [declaration(build, marker) for marker in ("public static bool ImportedAssetValidationRequested(string value)",
            "public static bool ImportedAssetValidationRequested()")]
        production = (SHIMS + "\nnamespace GloomhavenVR.Quest.Editor {public static class QuestBuild {\n" +
            "\n".join(helpers) + "}\npublic static class QuestStartupAddressablesBuild {\n" +
            "\n".join(declaration(source, marker) for marker in names) +
            '\nstatic void AddCampaignShaderRetention(AddressableAssetSettings value){Spy.Call("shader-retention");}\n' +
            'static void ValidateNativeCampaignShaders(string value){Spy.Call("native-audit");}\n' +
            'static bool CheckNativeContentCheckpoint(string value){return false;}\n' +
            'static void RecordNativeContentCheckpoint(string value){}\n' +
            'static void RepackContent(string value){Spy.Call("pack");File.WriteAllText("Assets/Quest/Resources/quest-startup-content.json", "{\\"inputKey\\":\\"'+ "a" * 64 +'\\"}");}\n' +
            "}}\n" + PROGRAM)
        path = self.root / "Catalog.cs"
        path.write_text(production)
        self.exe = self.root / "Catalog.exe"
        result = subprocess.run([str(MONO), str(MCS), "-langversion:latest", "-r:System.Web.Extensions",
            "-nowarn:0649", "-out:" + str(self.exe), str(path)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def tearDown(self): self.temp.cleanup()

    def run_catalog(self, *, full=False, bad_guid=False, missing=False, repeat=1):
        project = self.root / ("project-" + str(len(list(self.root.glob("project-*")))))
        resources = project / "Assets/Quest/Resources"
        resources.mkdir(parents=True)
        actors = project / "Assets/Actors"
        actors.mkdir()
        (actors / "Hero.prefab").write_text("controlled hero source stays unchanged")
        if not missing: (actors / "Enemy.prefab").write_text("controlled enemy source stays unchanged")
        rows = [
            {"assetPath": "Assets/Actors/Hero.prefab", "recoveredGuid": "a" * 32, "sourceBundle": "core-hero",
             "status": "associated", "keys": ["hero", "shared"], "labels": ["always_loaded_base", "always_loaded_standalone"]},
            {"assetPath": "Assets/Actors/Hero.prefab", "recoveredGuid": "a" * 32, "sourceBundle": "other-owner",
             "status": "associated", "subObjectName": "Sword", "keys": ["sword", "shared"], "labels": ["always_loaded_base_high"]},
            {"assetPath": "Assets/Actors/Enemy.prefab", "recoveredGuid": ("c" if bad_guid else "b") * 32,
             "sourceBundle": "core-enemy", "status": "associated", "keys": ["enemy", "shared"], "labels": ["always_loaded_base_high"]}]
        rows *= repeat
        mapping = project / "Assets/QuestOriginalCampaign/campaign-addressables.json"
        mapping.parent.mkdir()
        mapping.write_text(json.dumps({"schema": 1, "associatedEntryCount": len(rows), "unresolvedEntryCount": 0, "entries": rows}))
        env = {**os.environ, "GHVR_QUEST_TARGET": "game", "GHVR_QUEST_VALIDATE_IMPORTED_ASSETS": "1" if full else "0",
               "GHVR_QUEST_VERIFICATION": "full" if full else "minimum"}
        result = subprocess.run([str(MONO), str(self.exe)], cwd=project, env=env, capture_output=True, text=True)
        value = json.loads(result.stdout)
        value["exitCode"] = result.returncode
        manifest = resources / "quest-startup-addressables.json"
        value["manifest"] = json.loads(manifest.read_text()) if manifest.exists() else None
        omitted = project / "QuestCampaignShaderEvidence/native-addressables-validation.json"
        value["omitted"] = json.loads(omitted.read_text()) if omitted.exists() else None
        self.assertEqual((actors / "Hero.prefab").read_text(), "controlled hero source stays unchanged")
        return value

    def test_minimum_catalog_preserves_aliases_labels_owners_without_loading_all_assets(self):
        value = self.run_catalog()
        self.assertEqual(value["exitCode"], 0, value)
        self.assertEqual(value["calls"]["guid"], 2)
        self.assertEqual(value["calls"]["entry-create"], 2)
        self.assertEqual(value["calls"]["native-build"], 1)
        self.assertEqual(value["calls"]["shader-retention"], 1)
        self.assertEqual(value["calls"]["pack"], 1)
        self.assertNotIn("load-main", value["calls"])
        self.assertNotIn("load-subobjects", value["calls"])
        self.assertNotIn("native-audit", value["calls"])
        self.assertEqual(value["groups"], 4)  # default, first hero owner, unused alternate group, enemy owner
        self.assertEqual(value["entries"][0]["labels"], ["always_loaded_base", "always_loaded_base_high", "always_loaded_standalone"])
        aliases = {row["key"]: row["assetKeys"] for row in value["manifest"]["aliases"]}
        self.assertEqual(aliases["shared"], ["a" * 32, "a" * 32 + "[Sword]", "b" * 32])
        self.assertEqual(aliases["hero"], ["a" * 32])
        self.assertEqual(aliases["sword"], ["a" * 32 + "[Sword]"])
        self.assertFalse(value["omitted"]["auditRequested"])
        self.assertFalse(value["omitted"]["auditCompleted"])
        self.assertFalse(value["omitted"]["allOriginalAliasesRetained"])
        self.assertFalse(value["omitted"]["hardwarePictureVerified"])

    def test_many_subobject_catalog_rows_reuse_one_registration_per_native_guid(self):
        small = self.run_catalog()
        repeated = self.run_catalog(repeat=40)
        self.assertEqual(repeated["exitCode"], 0, repeated)
        self.assertEqual(repeated["calls"]["guid"], 2)
        self.assertEqual(repeated["calls"]["entry-create"], 2)
        self.assertEqual(repeated["manifest"], small["manifest"])
        self.assertEqual(repeated["entries"], small["entries"])

    def test_full_qa_alone_loads_object_closures_and_native_audit(self):
        value = self.run_catalog(full=True)
        self.assertEqual(value["exitCode"], 0, value)
        self.assertEqual(value["calls"]["load-main"], 2)
        self.assertEqual(value["calls"]["load-subobjects"], 1)
        self.assertEqual(value["calls"]["native-audit"], 1)
        self.assertIsNone(value["omitted"])

    def test_bad_source_guid_or_missing_file_still_fails_before_native_build(self):
        for kwargs in ({"bad_guid": True}, {"missing": True}):
            with self.subTest(kwargs=kwargs):
                value = self.run_catalog(**kwargs)
                self.assertEqual(value["exitCode"], 2)
                self.assertIn("identity failed import", value["message"])
                self.assertNotIn("native-build", value["calls"])
                self.assertNotIn("pack", value["calls"])


if __name__ == "__main__": unittest.main()
