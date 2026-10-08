"""Compile the real Editor checker with Unity SDK and exercise its filesystem gates.

The Mono execution substitutes JSON deserialization only because Unity JsonUtility
requires its native Editor host. Enumeration, hashing and validation are the exact
production methods. This is source/contract evidence, not a native Unity build.
"""
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs"
UNITY_DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
MONO = UNITY_DATA / "MonoBleedingEdge/bin/mono"
MCS = UNITY_DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
SDK_NAMES = ("UnityEngine.UI", "Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager",
             "Unity.ScriptableBuildPipeline", "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils")


def declaration(text, marker):
    start = text.index(marker)
    position = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[position] == "{") - (text[position] == "}")
        position += 1
    return text[start:position]


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity Mono compiler is unavailable")
class EditorPackageContractTests(unittest.TestCase):
    def test_actual_checker_excludes_payload_but_preserves_genuine_plugin_gates(self):
        text = SOURCE.read_text()
        excerpts = [declaration(text, marker) for marker in (
            "sealed class ApiFile", "sealed class ApiContract", "static string FileHash(string path)",
            "static void ValidatePackageApiContract()")]
        production = ("using System; using System.IO; using System.Linq; using UnityEngine;\n"
                      "namespace GloomhavenVR.Quest.Editor { public static class QuestBuild {\n" +
                      "\n".join(excerpts) + "\n public static void Check() { ValidatePackageApiContract(); } }}\n")
        shim = ("namespace UnityEngine { public static class JsonUtility {\n"
                "public static T FromJson<T>(string text) { return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(text); } }}\n")
        program = ("class Program { static int Main() { try { GloomhavenVR.Quest.Editor.QuestBuild.Check(); "
                   "System.Console.WriteLine(\"PASS production Editor package contract\"); return 0; } "
                   "catch(System.Exception e) { System.Console.WriteLine(e.Message); return 2; } }}\n")
        with tempfile.TemporaryDirectory(prefix="quest-editor-plugin-contract-") as folder:
            base = Path(folder)
            sdk_source = base / "Checker.cs"
            sdk_source.write_text(production)
            references = [UNITY_DATA / "Managed/UnityEngine" / name for name in
                          ("UnityEngine.CoreModule.dll", "UnityEngine.JSONSerializeModule.dll")]
            if not all(path.is_file() for path in references):
                self.skipTest("Actual pinned Unity SDK is unavailable")
            compile_sdk = subprocess.run([str(MONO), str(MCS), "-target:library", "-out:" + str(base / "Checker.dll"),
                                          *["-r:" + str(path) for path in references], str(sdk_source)],
                                         capture_output=True, text=True)
            self.assertEqual(compile_sdk.returncode, 0, compile_sdk.stdout + compile_sdk.stderr)
            witness = base / "Witness.cs"
            witness.write_text(production + shim + program)
            exe = base / "Witness.exe"
            compile_mono = subprocess.run([str(MONO), str(MCS), "-r:System.Web.Extensions", "-out:" + str(exe), str(witness)],
                                          capture_output=True, text=True)
            self.assertEqual(compile_mono.returncode, 0, compile_mono.stdout + compile_mono.stderr)

            project = base / "project"
            resources = project / "Assets/Quest/Resources"
            resources.mkdir(parents=True)
            report = resources / "quest-package-api-report.json"
            report.write_bytes(b"completed controlled API report")

            def write(relative, data):
                path = project / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
                return {"path": relative, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()}

            plugins = [write(name, ("managed plugin:" + name).encode()) for name in (
                "Assets/Plugins/GH.Runtime.dll", "Assets/Plugins/QuestGame/GloomhavenVR.dll",
                "Assets/Plugins/StreamingAssets/ManagedHelper.dll")]
            sdk = [write("QuestStartupEvidence/PlayerSdk/" + name + ".dll", name.encode()) for name in SDK_NAMES]
            for row in sdk:
                row["path"] = Path(row["path"]).name
            write("Assets/Plugins/Unity.InputSystem.dll", b"disabled replaced owned package")
            native = [write("Assets/StreamingAssets/ProceduralRuntime/" + name, b"MZ\0native:" + name.encode())
                      for name in ("ApparanceEngine.dll", "GH.Runtime.dll", "wine/lib/wine/x86_64-windows/kernel32.dll")]
            payload_before = {row["path"]: (project / row["path"]).read_bytes() for row in native}
            contract = {"schema": 1, "complete": True, "reportSha256": hashlib.sha256(report.read_bytes()).hexdigest(),
                        "sdkRoot": "QuestStartupEvidence/PlayerSdk", "plugins": plugins, "sdk": sdk}
            contract_path = resources / "quest-package-api-contract.json"

            def check(value, expected, message=None):
                contract_path.write_text(json.dumps(value))
                result = subprocess.run([str(MONO), str(exe)], cwd=project, capture_output=True, text=True)
                self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
                if message:
                    self.assertIn(message, result.stdout)

            check(contract, 0, "PASS production Editor package contract")
            for path, payload in payload_before.items():
                self.assertEqual((project / path).read_bytes(), payload)
            # Missing genuine plugins must fail even when same-name native DLLs exist.
            plugin = project / plugins[0]["path"]
            original = plugin.read_bytes()
            plugin.unlink()
            check(contract, 2, "inputs changed")
            plugin.write_bytes(original + b"modified")
            check(contract, 2, "inputs changed")
            plugin.write_bytes(original)
            additional = write("Assets/Plugins/Uncovered.dll", b"uncovered managed plugin")
            check(contract, 2, "does not cover all active plugins")
            (project / additional["path"]).unlink()
            for change in ("missing nested plugin", "payload claimed as plugin"):
                altered = copy.deepcopy(contract)
                if change == "missing nested plugin":
                    altered["plugins"] = altered["plugins"][:-1]
                else:
                    altered["plugins"].append(native[0])
                with self.subTest(control=change):
                    check(altered, 2, "does not cover all active plugins")
            altered = copy.deepcopy(contract)
            altered["reportSha256"] = "0" * 64
            check(altered, 2, "missing or stale")
            check(contract, 0, "PASS production Editor package contract")


if __name__ == "__main__":
    unittest.main()
