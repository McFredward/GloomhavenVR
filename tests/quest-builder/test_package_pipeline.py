"""Execute private mod-art/package stages; mocked compiler receipts are not hardware evidence."""
import copy
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import builder
import storage

SDK_NAMES = ("UnityEngine.UI", "Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager",
             "Unity.ScriptableBuildPipeline", "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils")
ART_PREFIX = "unity/GloomhavenVR.Assets/"
LAYOUT = "Runtime/UI/Core/Layout/LayoutRebuilder.cs"
LAYOUT_SOURCE = ("namespace UnityEngine.UI\n{\n    public class LayoutRebuilder : ICanvasElement\n    {\n"
                 "        public static void MarkLayoutForRebuild(RectTransform rect)\n        {\n"
                 "            OriginalLayoutWork(rect);\n        }\n    }\n}\n")


class Temporary(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source, self.project, self.output = (self.root / name for name in ("source", "player", "output"))
        self.editor = self.root / "editor/Unity"
        self.write(self.editor, b"controlled editor executable")
        self.write(self.project / "Assets/Quest/Resources/.keep", b"resource directory")

    def tearDown(self):
        self.temp.cleanup()

    @staticmethod
    def write(path, payload):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
        return path


class AuthoredModPipelineTests(Temporary):
    def setUp(self):
        super().setUp()
        self.authored = self.source / ART_PREFIX
        self.art_names = ["Assets/Bundle/Menu/Asset" + str(i) + ".prefab" for i in range(23)]
        for name in self.art_names:
            self.write(self.authored / name, ("authored:" + name).encode())
            self.write(self.authored / (name + ".meta"), ("original-guid:" + name).encode())
        self.write(self.authored / "Assets/Editor/QuestModBundles.cs", b"controlled bundle recipe input")
        self.files = storage.inventory(self.authored)
        self.inputs = {"inputKey": "fixture-input", "mod": {"files": [{**row, "path": ART_PREFIX + row["path"]} for row in self.files]}}
        self.commands = []

    def receipt(self, folder, authored):
        bank = self.write(folder / "gloomhavenvr.bundle", b"UnityFS\0controlled-native-compiler-output")
        value = {"schema": 1, "target": "Android", "unityVersion": "2021.3.5f1", "bundleName": bank.name,
                 "graphicsApi": "OpenGLES3", "colorSpace": "Linear", "stereoRenderingPath": "SinglePass",
                 "typeTreesEnabled": True, "chunkBasedCompression": True, "townBanksIncluded": False,
                 "assetNames": list(self.art_names), "requiredAssetNames": list(self.art_names),
                 "sourceFiles": storage.inventory(authored, [name for name in self.art_names] + [name + ".meta" for name in self.art_names]),
                 "bundle": storage.record_file(bank, bank.name)}
        storage.write_json(folder / "quest-mod-bundles.json", value)
        return value

    def compile(self, argv, log, **kwargs):
        self.commands.append(argv)
        self.assertIn("GloomhavenVR.QuestModBundles.BuildAll", argv)
        self.assertEqual(argv[argv.index("-buildTarget") + 1], "Android")
        self.receipt(Path(kwargs["env"]["GHVR_QUEST_MOD_BUNDLE_OUTPUT"]), Path(argv[argv.index("-projectPath") + 1]))
        return "controlled compiler receipt"

    def package(self):
        return builder.package_mod_content(self.project, self.inputs, self.output, self.source, self.editor)

    def test_native_bank_archive_and_verified_cache_reuse(self):
        original = storage.inventory(self.authored)
        with patch.object(builder, "command", side_effect=self.compile):
            first = self.package()
            second = self.package()
        self.assertEqual(first, second)
        self.assertEqual(len(self.commands), 1)
        self.assertEqual(storage.inventory(self.authored), original)
        archive = self.project / "Assets/StreamingAssets/quest-mod-content.zip"
        with zipfile.ZipFile(archive) as zipped:
            self.assertEqual(zipped.namelist(), ["StreamingAssets/gloomhavenvr.bundle"])
            self.assertEqual(zipped.read(zipped.namelist()[0]), b"UnityFS\0controlled-native-compiler-output")
            self.assertEqual(zipped.getinfo(zipped.namelist()[0]).compress_type, zipfile.ZIP_STORED)
        self.assertEqual(first["archiveSha256"], storage.digest(archive))
        self.assertEqual(first["files"][0]["path"], "StreamingAssets/gloomhavenvr.bundle")

    def test_changed_cached_bank_or_receipt_requires_recompile(self):
        with patch.object(builder, "command", side_effect=self.compile):
            self.package()
            cache = next((self.output / "cache/mod-bundle").iterdir())
            (cache / "bundles/gloomhavenvr.bundle").write_bytes(b"changed cached payload")
            self.package()
            (cache / "bundles/quest-mod-bundles.json").write_text("{}")
            self.package()
        self.assertEqual(len(self.commands), 3)

    def test_normal_mod_code_changes_reuse_art_without_builder_edits(self):
        with patch.object(builder, "command", side_effect=self.compile):
            first = self.package()
            change = self.write(self.source / "src/GloomhavenVR/NewNormalPatch.cs", b"ordinary mod development")
            self.inputs["mod"]["files"].append(storage.record_file(change, "src/GloomhavenVR/NewNormalPatch.cs"))
            self.inputs["inputKey"] = "next-mod-input"
            second = self.package()
        self.assertEqual(len(self.commands), 1)
        self.assertNotEqual(first["inputKey"], second["inputKey"])
        self.assertEqual(first["archiveSha256"], second["archiveSha256"])

    def test_changed_authored_cache_never_reuses_old_art(self):
        with patch.object(builder, "command", side_effect=self.compile):
            self.package()
            cache = next((self.output / "cache/mod-bundle").iterdir())
            (cache / "project" / self.art_names[0]).write_bytes(b"modified compiler input")
            with self.assertRaisesRegex(storage.BuildError, "changed during Android"):
                self.package()
        self.assertEqual(len(self.commands), 1)

    def test_unknown_art_recipe_cannot_be_accepted(self):
        self.inputs["mod"]["files"] = [row for row in self.inputs["mod"]["files"] if not row["path"].endswith("QuestModBundles.cs")]
        with patch.object(builder, "command") as command, self.assertRaisesRegex(storage.BuildError, "missing.*recipe"):
            self.package()
        command.assert_not_called()

    def test_receipt_contract_and_provenance_controls(self):
        folder = self.root / "controlled-bundles"
        good = self.receipt(folder, self.authored)
        for name, mutate in (
            ("desktop", lambda r: r.update(target="StandaloneWindows64")),
            ("no type trees", lambda r: r.update(typeTreesEnabled=False)),
            ("wrong stereo", lambda r: r.update(stereoRenderingPath="MultiPass")),
            ("wrong GPU", lambda r: r.update(graphicsApi="Vulkan")),
            ("missing menu art", lambda r: r.update(assetNames=r["assetNames"][1:])),
            ("duplicate named art", lambda r: r["assetNames"].append(r["assetNames"][0])),
            ("foreign art path", lambda r: r["assetNames"].append("Other/Outside.prefab")),
            ("escaped art path", lambda r: r["assetNames"].append("Assets/Bundle/../../outside.prefab")),
            ("Windows art path", lambda r: r["assetNames"].append("Assets/Bundle/Menu\\outside.prefab")),
            ("drive art path", lambda r: r["assetNames"].append("Assets/Bundle/C:/outside.prefab")),
            ("empty art segment", lambda r: r["assetNames"].append("Assets/Bundle//outside.prefab")),
            ("dot art segment", lambda r: r["assetNames"].append("Assets/Bundle/./outside.prefab")),
            ("unknown authored art", lambda r: r["assetNames"].append("Assets/Bundle/Unknown.prefab")),
            ("town art", lambda r: r["assetNames"].append("Assets/Bundle/TownServices/Unshipped.prefab")),
            ("duplicate source", lambda r: r["sourceFiles"].append(r["sourceFiles"][0])),
            ("unknown source", lambda r: r["sourceFiles"].append({"path": "Assets/unknown.asset", "sha256": "a" * 64, "size": 1})),
            ("source changed", lambda r: r["sourceFiles"][0].update(sha256="b" * 64)),
            ("source escape", lambda r: r["sourceFiles"][0].update(path="../outside")),
            ("bundle hash", lambda r: r["bundle"].update(sha256="c" * 64)),
        ):
            with self.subTest(case=name):
                altered = copy.deepcopy(good); mutate(altered)
                storage.write_json(folder / "quest-mod-bundles.json", altered)
                with self.assertRaises(storage.BuildError):
                    builder.validate_mod_bundle(folder, self.authored, self.files)

    def test_new_authored_required_asset_does_not_need_builder_recipe_edit(self):
        name = "Assets/Bundle/Menu/NextModAsset.prefab"
        self.art_names.append(name)
        self.write(self.authored / name, b"new authored asset from ordinary mod development")
        self.write(self.authored / (name + ".meta"), b"new source GUID")
        files = storage.inventory(self.authored)
        folder = self.root / "expanded-controlled-bundles"
        self.receipt(folder, self.authored)
        result = builder.validate_mod_bundle(folder, self.authored, files)
        self.assertEqual(len(result["requiredAssetNames"]), 24)


class UgUiLayoutGateTests(Temporary):
    def setUp(self):
        super().setUp()
        self.game = self.root / "owned-game"
        self.original = self.write(self.game / "Managed/UnityEngine.UI.dll", b"owned UGUI with audited batching ABI")
        self.package = self.editor.parent / "Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui"
        self.layout = self.write(self.package / LAYOUT, LAYOUT_SOURCE.encode())
        self.write(self.package / (LAYOUT + ".meta"), b"original editor MonoScript GUID")
        self.write(self.package / "package.json", b'{"name":"com.unity.ugui","version":"1.0.0"}')
        self.write(self.package / "Tests/Runtime/RequiresUnityTestEnvironment.cs", b"builtin test source")
        self.write(self.package / "Runtime/UI/Other.cs", b"other editor package code")

    def restore(self):
        with patch.object(builder, "ORIGINAL_UGUI_SHA256", storage.digest(self.original)), \
                patch.object(builder, "UGUI_LAYOUT_SOURCE_SHA256", storage.digest(self.layout)):
            builder.restore_ugui_layout_gate(self.project, self.game, self.editor)

    def test_exact_original_batch_gate_and_other_private_package_bytes(self):
        original = storage.inventory(self.package)
        self.restore()
        target = self.project / "Packages/com.unity.ugui"
        derived = (target / LAYOUT).read_text()
        self.assertIn("public static bool Enable { get; set; } = true;", derived)
        self.assertIn("public static void MarkLayoutForRebuild(RectTransform rect)\n        {\n            if (!Enable) return;\n            OriginalLayoutWork(rect);", derived)
        for row in original:
            if row["path"] != LAYOUT and not row["path"].startswith("Tests/"):
                self.assertEqual(storage.digest(target / row["path"]), row["sha256"])
        self.assertFalse((target / "Tests").exists())
        self.assertTrue((self.package / "Tests/Runtime/RequiresUnityTestEnvironment.cs").is_file())
        self.assertEqual(storage.inventory(self.package), original)
        receipt = json.loads((self.project / "QuestStartupEvidence/ugui-layout-gate.json").read_text())
        self.assertEqual(receipt["derivedSha256"], storage.digest(target / LAYOUT))
        self.assertEqual(receipt["originalAssemblySha256"], storage.digest(self.original))
        self.assertTrue(receipt["defaultEnabled"])

    def test_existing_private_package_is_never_overwritten(self):
        self.restore()
        target = self.project / "Packages/com.unity.ugui"
        before = storage.inventory(target)
        with self.assertRaisesRegex(storage.BuildError, "already exists"):
            self.restore()
        self.assertEqual(storage.inventory(target), before)

    def test_changed_original_or_editor_source_stops_before_private_copy(self):
        for constant in ("ORIGINAL_UGUI_SHA256", "UGUI_LAYOUT_SOURCE_SHA256"):
            with self.subTest(pin=constant), patch.object(builder, constant, "f" * 64), self.assertRaisesRegex(storage.BuildError, "changed"):
                builder.restore_ugui_layout_gate(self.project, self.game, self.editor)
            self.assertFalse((self.project / "Packages/com.unity.ugui").exists())

    def test_new_source_layout_cannot_be_laundered_through_changed_pin(self):
        self.layout.write_text(LAYOUT_SOURCE.replace("MarkLayoutForRebuild", "ChangedMethod"))
        with self.assertRaisesRegex(storage.BuildError, "anchors changed"):
            self.restore()
        self.assertFalse((self.project / "Packages/com.unity.ugui").exists())


class PackageApiStageTests(Temporary):
    def setUp(self):
        super().setUp()
        self.dotnet = self.write(self.root / "dotnet", b"controlled CLI executable")
        self.args = SimpleNamespace(dotnet=str(self.dotnet))
        self.tools = {"editor": str(self.editor), "androidSdk": "controlled-sdk",
                      "androidNdk": "controlled-ndk", "jdk": "controlled-jdk"}
        self.sdk = self.project / "QuestStartupEvidence/PlayerSdk"
        for name in SDK_NAMES:
            self.write(self.sdk / (name + ".dll"), ("imported package identity:" + name).encode())
        self.plugin = self.write(self.project / "Assets/Plugins/GH.Runtime.dll", b"current generated game plugin")
        self.mod = self.write(self.project / "Assets/Plugins/QuestGame/GloomhavenVR.dll", b"current statically woven mod")
        self.metas = {}
        for path in (self.plugin, self.mod):
            meta = self.write(Path(str(path) + ".meta"), ("original-script-guid:" + path.name).encode()); self.metas[meta] = meta.read_bytes()
        self.write(self.project / "Assets/Plugins/Unity.InputSystem.dll", b"disabled owned package reference")
        storage.write_json(self.project / "Assets/StreamingAssets/Quest/input-manifest.json", {"game": {"key": "owned-game-key"}})
        self.write(self.output / "inputs/game/owned-game-key/Managed/System.dll", b"immutable framework resolution")
        self.cli_count, self.import_count = 0, 0
        self.alter_report = None
        self.change_sdk = False
        self.fail_cli = False
        self.rewrite_payload = False
        self.player_evidence = {"schema": 1, "target": "Android", "backend": "IL2CPP", "compilation": "Player",
                                "options": "DevelopmentBuild|Assertions", "unityVersion": "2021.3.5f1"}

    def command(self, argv, log, **kwargs):
        if "package-api" not in argv:
            self.import_count += 1
            self.assertIn("-buildTarget", argv)
            self.assertIn("GloomhavenVR.Quest.Editor.QuestBuild.CompileStartupSdk", argv)
            storage.write_json(self.sdk / "compilation.json", self.player_evidence)
            return "controlled package import"
        self.cli_count += 1
        original = Path(argv[argv.index("--managed") + 1]); rewritten = Path(argv[argv.index("--output") + 1])
        rewritten.mkdir(parents=True)
        self.assertEqual(Path(argv[argv.index("--reference-managed") + 1]), self.output / "inputs/game/owned-game-key/Managed")
        input_map = {row["path"]: row["sha256"] for row in storage.inventory(original)}
        for path in original.glob("*.dll"):
            suffix = b":audited package signature fix" if self.rewrite_payload and path.name == "GH.Runtime.dll" else b""
            self.write(rewritten / path.name, path.read_bytes() + suffix)
        report = {"schema": 1, "sdkTarget": "Android", "complete": True, "issues": [], "inputAssemblies": input_map,
                  "outputAssemblies": {row["path"]: row["sha256"] for row in storage.inventory(rewritten)},
                  "sdkAssemblies": {row["path"]: row["sha256"] for row in storage.inventory(self.sdk, [name + ".dll" for name in SDK_NAMES])}}
        if self.alter_report:
            self.alter_report(report)
        storage.write_json(Path(argv[argv.index("--report") + 1]), report)
        if self.change_sdk:
            (self.sdk / "Unity.InputSystem.dll").write_bytes(b"SDK changed during package API audit")
        if self.fail_cli:
            raise storage.BuildError("controlled API CLI failure")
        return "controlled API audit"

    def bind(self):
        return builder.bind_startup_package_apis(self.args, self.output, self.source, self.project, self.tools, "controlled-build-key")

    def test_all_eight_sdk_hashes_exact_plugins_and_meta_guids_survive_reuse(self):
        self.assertEqual(set(builder.REPLACED_PACKAGES), set(SDK_NAMES))
        before = {path: path.read_bytes() for path in (self.plugin, self.mod)}
        with patch.object(builder, "command", side_effect=self.command):
            first = self.bind(); second = self.bind()
        self.assertEqual(first, second)
        self.assertEqual((self.import_count, self.cli_count), (2, 1))
        self.assertEqual(set(first["sdkAssemblies"]), {name + ".dll" for name in SDK_NAMES})
        self.assertEqual(set(first["inputAssemblies"]), {"GH.Runtime.dll", "GloomhavenVR.dll"})
        self.assertEqual(first["sdkAssemblies"], {row["path"]: row["sha256"] for row in storage.inventory(self.sdk, [name + ".dll" for name in SDK_NAMES])})
        for path, data in before.items():
            self.assertEqual(path.read_bytes(), data)
        for meta, data in self.metas.items():
            self.assertEqual(meta.read_bytes(), data)
        contract = json.loads((self.project / "Assets/Quest/Resources/quest-package-api-contract.json").read_text())
        self.assertTrue(contract["complete"])
        self.assertEqual(len(contract["sdk"]), 8)
        self.assertEqual(contract["reportSha256"], storage.digest(self.project / "Assets/Quest/Resources/quest-package-api-report.json"))
        self.assertEqual({row["path"] for row in contract["plugins"]},
                         {"Assets/Plugins/GH.Runtime.dll", "Assets/Plugins/QuestGame/GloomhavenVR.dll"})
        self.assertTrue(storage.verify_files(self.project, contract["plugins"]))
        self.assertEqual(contract["sdkRoot"], "QuestStartupEvidence/PlayerSdk")

    def test_editor_or_wrong_player_compilation_cannot_reach_api_audit(self):
        for key, value in (("compilation", "Editor"), ("target", "StandaloneWindows64"), ("backend", "Mono"),
                           ("options", "None"), ("unityVersion", "2021.3.6f1")):
            original = self.player_evidence[key]
            with self.subTest(key=key):
                self.player_evidence[key] = value
                with patch.object(builder, "command", side_effect=self.command), self.assertRaises(storage.BuildError):
                    self.bind()
                self.assertEqual(self.cli_count, 0)
                self.assertEqual(self.plugin.read_bytes(), b"current generated game plugin")
            self.player_evidence[key] = original

    def test_audited_rewritten_bytes_deploy_with_original_script_guid(self):
        before = self.plugin.read_bytes(); self.rewrite_payload = True
        with patch.object(builder, "command", side_effect=self.command):
            report = self.bind()
        self.assertEqual(self.plugin.read_bytes(), before + b":audited package signature fix")
        self.assertNotEqual(report["inputAssemblies"][self.plugin.name], report["outputAssemblies"][self.plugin.name])
        cached_original = next((self.output / "cache/package-api").glob("*/input/GH.Runtime.dll"))
        self.assertEqual(cached_original.read_bytes(), before)
        for meta, payload in self.metas.items():
            self.assertEqual(meta.read_bytes(), payload)

    def test_invalid_or_failed_api_reports_never_deploy(self):
        cases = (
            ("incomplete", lambda r: r.update(complete=False)),
            ("issues", lambda r: r.update(issues=["missing original package method"])),
            ("wrong input", lambda r: r["inputAssemblies"].update({"GH.Runtime.dll": "a" * 64})),
            ("missing SDK", lambda r: r["sdkAssemblies"].pop("Unity.XR.OpenXR.dll")),
            ("output mismatch", lambda r: r["outputAssemblies"].update({"GH.Runtime.dll": "b" * 64})),
            ("extra output", lambda r: r["outputAssemblies"].update({"Injected.dll": "c" * 64})),
        )
        before = {path: path.read_bytes() for path in (self.plugin, self.mod)}
        for name, alter in cases:
            with self.subTest(case=name):
                self.alter_report = alter
                with patch.object(builder, "command", side_effect=self.command), self.assertRaises(storage.BuildError):
                    self.bind()
                for path, data in before.items():
                    self.assertEqual(path.read_bytes(), data)
                self.assertFalse((self.project / "Assets/Quest/Resources/quest-package-api-contract.json").exists())
        self.alter_report = None; self.fail_cli = True
        with patch.object(builder, "command", side_effect=self.command), self.assertRaisesRegex(storage.BuildError, "CLI failure"):
            self.bind()
        self.assertEqual(self.plugin.read_bytes(), before[self.plugin])

    def test_sdk_drift_during_audit_never_deploys(self):
        self.change_sdk = True
        before = self.plugin.read_bytes()
        with patch.object(builder, "command", side_effect=self.command), self.assertRaisesRegex(storage.BuildError, "inputs changed"):
            self.bind()
        self.assertEqual(self.plugin.read_bytes(), before)

    def test_missing_sdk_and_duplicate_plugin_stop_before_cli(self):
        sdk = self.sdk / "Unity.XR.CoreUtils.dll"; payload = sdk.read_bytes(); sdk.unlink()
        with patch.object(builder, "command", side_effect=self.command), self.assertRaisesRegex(storage.BuildError, "missing"):
            self.bind()
        self.assertEqual(self.cli_count, 0)
        sdk.write_bytes(payload)
        self.write(self.project / "Assets/Foreign/GH.Runtime.dll", b"ambiguous other game plugin")
        with patch.object(builder, "command", side_effect=self.command), self.assertRaisesRegex(storage.BuildError, "one staged plugin"):
            self.bind()
        self.assertEqual(self.cli_count, 0)

    def test_corrupt_cached_api_report_requires_new_audit(self):
        with patch.object(builder, "command", side_effect=self.command):
            self.bind()
            report = next((self.output / "cache/package-api").glob("*/report.json")); report.write_text("{}")
            self.bind()
        self.assertEqual(self.cli_count, 2)

    def test_rehashed_cache_receipt_cannot_hide_wrong_sdk_report(self):
        with patch.object(builder, "command", side_effect=self.command):
            self.bind()
            report = next((self.output / "cache/package-api").glob("*/report.json"))
            value = json.loads(report.read_text()); value["sdkAssemblies"]["Unity.XR.OpenXR.dll"] = "f" * 64
            storage.write_json(report, value)
            stage = next((self.output / "receipts/package-api").glob("*.json"))
            receipt = json.loads(stage.read_text())
            for row in receipt["outputs"]:
                if row["path"] == report.relative_to(self.output).as_posix():
                    row.update(storage.record_file(report, row["path"]))
            storage.write_json(stage, receipt)
            with self.assertRaisesRegex(storage.BuildError, "different inputs"):
                self.bind()
        self.assertEqual(self.cli_count, 1)
