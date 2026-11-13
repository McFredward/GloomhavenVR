"""Exercise exact byte restoration and the actual Editor's durable mutation receipt."""
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import script_remap_resume as repair
from storage import BuildError

OLD = "a" * 32
NEW = "b" * 32
ASSET = "Assets/Scenes/Original.unity"

def encoded(value):
    return json.dumps(value, sort_keys=True).encode()

def pointer(guid, file_id):
    return ("m_Script: {fileID: " + str(file_id) + ", guid: " + guid + ", type: 3}").encode()

def frozen(name, raw):
    return {"path": name, "size": len(raw), "sha256": repair.sha(raw)}


class ScriptRemapResumeTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="quest-script-remap-")
        self.addCleanup(self.folder.cleanup)
        self.project = Path(self.folder.name)
        self.binding = {"assemblyName": "UnityEngine.UI", "fullName": "UnityEngine.UI.Button", "oldGuid": OLD, "oldFileId": -619905303}
        self.replacement = {**self.binding, "newGuid": NEW, "newFileId": 11500000}
        self.input = {"schema": 1, "assetPaths": [ASSET], "bindings": [self.binding], "disabledPluginGuids": [OLD]}
        self.receipt = {"schema": 1, "callbacksAndOtherSerializedBytesPreserved": True, "replacements": [self.replacement], "assets": []}
        self.original = b"%YAML 1.1\n  " + pointer(OLD, -619905303) + b"\n  m_Name: Button\n  m_OnClick: unchanged\n"
        self.changed = self.original.replace(pointer(OLD, -619905303), pointer(NEW, 11500000))
        self.latest = {}
        self.write(repair.MANIFESTS[0], encoded(self.input), retain=True)
        self.write("Packages/manifest.json", encoded({"dependencies": {"com.unity.ugui": "1.0.0"}}), retain=True)
        self.write("Packages/com.unity.ugui/Runtime/UI/Core/Button.cs", b"namespace UnityEngine.UI { public class Button {} }")
        self.write("Packages/com.unity.ugui/Runtime/UI/Core/Button.cs.meta", ("fileFormatVersion: 2\nguid: " + NEW + "\nMonoImporter:\n").encode())
        self.write(repair.RECEIPT, encoded(self.receipt))
        self.write(ASSET, self.original, retain=True)
        self.write(ASSET, self.changed)

    def write(self, name, raw, retain=False):
        path = self.project / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        if retain:
            self.latest[name] = (0, frozen(name, raw))
        return path

    def accept(self):
        return repair.ScriptRemap(self.project, self.latest).accept(ASSET, self.latest[ASSET][1])

    def test_real_pointer_only_transition_with_empty_repeat_receipt(self):
        library = self.write("Library/Artifacts/keep", b"unchanged imported artifact")
        before = library.stat().st_mtime_ns
        row = self.accept()
        self.assertEqual(row, frozen(ASSET, self.changed))
        self.assertEqual((self.project / ASSET).read_bytes(), self.changed)
        self.assertEqual(library.stat().st_mtime_ns, before)

    def test_all_production_serialized_extensions_use_the_same_original_hash_proof(self):
        # The producer scans seven YAML extensions; the actual Editor consumes
        # all assetPaths without a suffix filter. A controller/playable must
        # retain exactly the same pointer-only and full original-byte proof.
        names = ["Assets/Serialized/Original" + suffix for suffix in
                 (".asset", ".prefab", ".unity", ".anim", ".controller", ".overrideController", ".playable")]
        self.input["assetPaths"] = names
        self.write(repair.MANIFESTS[0], encoded(self.input), retain=True)
        for name in names:
            self.write(name, self.original, retain=True)
            self.write(name, self.changed)
        helper = repair.ScriptRemap(self.project, self.latest)
        for name in names:
            with self.subTest(name=name):
                self.assertEqual(helper.accept(name, self.latest[name][1]), frozen(name, self.changed))
                self.write(name, self.changed.replace(b"unchanged", b"foreign"))
                self.assertIsNone(helper.accept(name, self.latest[name][1]))
        unlisted = "Assets/Serialized/Unlisted.controller"
        self.write(unlisted, self.changed)
        self.assertIsNone(helper.accept(unlisted, frozen(unlisted, self.original)))

    def test_metadata_reader_runs_once_for_multiple_changed_files(self):
        second = "Assets/Prefabs/Second.prefab"
        self.input["assetPaths"].append(second)
        self.write(repair.MANIFESTS[0], encoded(self.input), retain=True)
        self.write(second, self.original, retain=True)
        self.write(second, self.changed)
        helper = repair.ScriptRemap(self.project, self.latest)
        with mock.patch.object(repair.ScriptRemap, "_package_identities", wraps=helper._package_identities) as checked:
            self.assertIsNotNone(helper.accept(ASSET, self.latest[ASSET][1]))
            self.assertIsNotNone(helper.accept(second, self.latest[second][1]))
            self.assertEqual(checked.call_count, 1)

    def test_foreign_field_or_script_mapping_edits_are_not_accepted(self):
        for changed in (self.changed.replace(b"Button", b"Foreign"), self.changed + b"\nnewField: true\n",
                        self.changed.replace(NEW.encode(), b"c" * 32), self.changed.replace(b"11500000", b"11500001"),
                        self.changed.replace(b"fileID: ", b"fileID:  "), self.original):
            with self.subTest(changed=changed):
                self.write(ASSET, changed)
                self.assertIsNone(self.accept())

    def test_wrong_original_hash_or_size_never_accepts_an_after_hash(self):
        for key, value in (("sha256", "c" * 64), ("size", len(self.original) + 1)):
            self.latest[ASSET] = (0, {**frozen(ASSET, self.original), key: value})
            self.assertIsNone(self.accept())

    def test_unlisted_asset_and_unrelated_file_do_not_read_or_accept_receipt(self):
        helper = repair.ScriptRemap(self.project, self.latest)
        self.assertIsNone(helper.accept("Assets/Shader/Hidden_Bloom.shader", frozen(ASSET, self.original)))
        self.assertFalse(helper.loaded)
        self.assertIsNone(helper.accept("Assets/Scenes/Foreign.unity", frozen(ASSET, self.original)))

    def test_changed_or_unqualified_binding_inputs_reject(self):
        self.write(repair.MANIFESTS[0], encoded({**self.input, "bindings": []}))
        with self.assertRaisesRegex(BuildError, "input changed"):
            self.accept()
        self.write(repair.MANIFESTS[0], encoded(self.input))
        del self.latest["Packages/manifest.json"]
        with self.assertRaisesRegex(BuildError, "unchanged retained input"):
            self.accept()

    def test_ambiguous_or_malformed_receipt_maps_reject(self):
        controls = [[], [self.replacement, self.replacement], [{**self.replacement, "newFileId": 0}],
                    [{**self.replacement, "newGuid": "c" * 32}], [{**self.replacement, "fullName": "Foreign.Type"}],
                    [{**self.replacement, "oldFileId": {}}], [{**self.replacement, "newAssetPath": "Packages/foreign/Button.cs"}]]
        for rows in controls:
            with self.subTest(rows=rows):
                self.write(repair.RECEIPT, encoded({**self.receipt, "replacements": rows}))
                with self.assertRaises(BuildError):
                    self.accept()

    def test_registry_sdk_guid_from_exact_imported_cache(self):
        replacement = {**self.replacement, "assemblyName": "Unity.InputSystem", "fullName": "UnityEngine.InputSystem.InputSettings"}
        binding = {k: replacement[k] for k in self.binding}
        self.write(repair.MANIFESTS[0], encoded({**self.input, "bindings": [binding]}), retain=True)
        self.write(repair.RECEIPT, encoded({**self.receipt, "replacements": [replacement]}))
        self.write("Packages/manifest.json", encoded({"dependencies": {"com.unity.inputsystem": "1.7.0"}}), retain=True)
        self.write("Library/PackageCache/com.unity.inputsystem@1.7.0/InputSystem/InputSettings.cs", b"class InputSettings {}")
        metadata = self.write("Library/PackageCache/com.unity.inputsystem@1.7.0/InputSystem/InputSettings.cs.meta", ("guid: " + NEW + "\n").encode())
        self.assertIsNotNone(self.accept())
        metadata.write_text("guid: " + "c" * 32 + "\n")
        with self.assertRaisesRegex(BuildError, "current imported SDK"):
            self.accept()

    def test_symlink_rejects(self):
        path = self.project / ASSET
        path.unlink()
        source = self.write("outside", self.changed)
        path.symlink_to(source)
        with self.assertRaises(BuildError):
            self.accept()


UNITY_DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
MONO = UNITY_DATA / "MonoBleedingEdge/bin/mono"
MCS = UNITY_DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestOriginalScriptBindings.cs"

PROGRESS = '''namespace GloomhavenVR.Quest.Editor { public static class QuestWizardProgress {
 public class Counter { string phase; public Counter(string p,string o,int t,string u,string d){phase=p;}
 public void Report(int n,string d){if(phase=="unity-script-write" && n==1 && System.Environment.GetEnvironmentVariable("CUT")=="1") throw new System.Exception("controlled write cut");}
 public void Complete(string d){} }
} }'''
SHIM = '''namespace UnityEngine { public static class JsonUtility {
 public static T FromJson<T>(string text){return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(text);}
 public static string ToJson(object value,bool pretty){return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value);}
 } public static class Debug { public static void Log(string s){System.Console.WriteLine(s);} } }
 namespace UnityEngine.UI { public class Button {} }
 namespace UnityEditor { public class MonoScript { public System.Type GetClass(){return typeof(UnityEngine.UI.Button);} }
 public static class MonoImporter { public static MonoScript[] GetAllRuntimeMonoScripts(){return new[]{new MonoScript()};} }
 public enum ImportAssetOptions {ForceSynchronousImport}
 public static class AssetDatabase { public static string GetAssetPath(MonoScript s){return "Packages/com.unity.ugui/Runtime/UI/Core/Button.cs";}
 public static bool TryGetGUIDAndLocalFileIdentifier(MonoScript s,out string g,out long id){g="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";id=11500000;return true;}
 public static void Refresh(ImportAssetOptions o){} } }
 class Program { static int Main(){try {GloomhavenVR.Quest.Editor.QuestOriginalScriptBindings.RemapAndValidate();return 0;} catch(System.Exception e){System.Console.WriteLine(e.Message);return 2;}} }
'''

@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity Mono SDK is unavailable")
class ProductionEditorScriptTransitionTests(unittest.TestCase):
    def test_actual_editor_retains_proof_across_second_run_and_interrupted_writes(self):
        with tempfile.TemporaryDirectory(prefix="quest-editor-script-transition-") as folder:
            base = Path(folder)
            source = base / "Editor.cs"
            source.write_text(SOURCE.read_text())
            progress = base / "Progress.cs"
            progress.write_text(PROGRESS)
            references = [UNITY_DATA / "Managed/UnityEngine" / name for name in
                          ("UnityEngine.CoreModule.dll", "UnityEngine.JSONSerializeModule.dll", "UnityEditor.CoreModule.dll")]
            compile_sdk = subprocess.run([str(MONO), str(MCS), "-define:UNITY_EDITOR,GHVR_QUEST_GAME", "-target:library", "-out:" + str(base / "ActualSdk.dll"),
                                          *["-r:" + str(r) for r in references], str(source), str(progress)], capture_output=True, text=True)
            self.assertEqual(compile_sdk.returncode, 0, compile_sdk.stdout + compile_sdk.stderr)
            shim = base / "Shim.cs"
            shim.write_text(SHIM)
            exe = base / "Witness.exe"
            build = subprocess.run([str(MONO), str(MCS), "-define:UNITY_EDITOR,GHVR_QUEST_GAME", "-r:System.Web.Extensions", "-out:" + str(exe), str(source), str(progress), str(shim)], capture_output=True, text=True)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            for interrupted in (False, True):
                project = base / ("cut" if interrupted else "clean")
                assets = ["Assets/Scenes/One.unity", "Assets/Scenes/Two.unity"]
                original = b"%YAML 1.1\n  " + pointer(OLD, -619905303) + b"\n  m_OnClick: originalCallback\n"
                current = original.replace(pointer(OLD, -619905303), pointer(NEW, 11500000))
                manifest = project / repair.MANIFESTS[0]
                manifest.parent.mkdir(parents=True)
                manifest.write_bytes(encoded({"schema": 1, "assetPaths": assets, "bindings": [{"assemblyName": "Witness", "fullName": "UnityEngine.UI.Button", "oldGuid": OLD, "oldFileId": -619905303}], "disabledPluginGuids": [OLD]}))
                for name in assets:
                    path = project / name;path.parent.mkdir(parents=True, exist_ok=True);path.write_bytes(original)
                def run(cut=False):
                    env = dict(os.environ);env["CUT"] = "1" if cut else "0"
                    return subprocess.run([str(MONO), str(exe)], cwd=project, env=env, capture_output=True, text=True)
                first = run(interrupted)
                self.assertEqual(first.returncode, 2 if interrupted else 0, first.stdout + first.stderr)
                receipt_path = project / repair.RECEIPT
                pending = json.loads(receipt_path.read_text())
                self.assertEqual(len(pending["assets"]), 2)
                self.assertTrue(all(row["beforeSha256"] == repair.sha(original) for row in pending["assets"]))
                if interrupted:
                    self.assertEqual((project / assets[0]).read_bytes(), current)
                    self.assertEqual((project / assets[1]).read_bytes(), original)
                again = run()
                self.assertEqual(again.returncode, 0, again.stdout + again.stderr)
                retained = json.loads(receipt_path.read_text())
                self.assertEqual(retained["assets"], pending["assets"])
                for name in assets:
                    self.assertEqual((project / name).read_bytes(), current)
                last = run()
                self.assertEqual(last.returncode, 0, last.stdout + last.stderr)
                self.assertEqual(json.loads(receipt_path.read_text())["assets"], retained["assets"])
                # Foreign serialized bytes must not be legitimized by replay.
                (project / assets[0]).write_bytes(current.replace(b"originalCallback", b"foreignCallback"))
                failed = run()
                self.assertEqual(failed.returncode, 2, failed.stdout + failed.stderr)
                self.assertIn("Retained original script asset changed", failed.stdout)
                self.assertEqual(json.loads(receipt_path.read_text()), retained)


if __name__ == "__main__":
    unittest.main()
