"""Exercise real compact staging/deployment/binding across the Support143919 seam.

The fixture compiler emits controlled receipts, not executable CLR/Android code.
Production staging, importer repair, deployment, SDK census and disk receipts run
unchanged. Actual owned-assembly/weaver checks are recorded separately.
"""
import json
from pathlib import Path
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import apk_update as core
import builder
import storage
import update_driver as driver


class CompactPackageHandoffTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repo, self.project, self.game, self.output = (self.root / name for name in ("source", "project", "game", "output"))
        self.project = self.output / "updates/code-projects/owned-compiler"
        self.base = self.root / "base.apk"
        self.managed = self.game / "Managed"
        self.managed.mkdir(parents=True)
        self.originals = ["GH.Runtime.dll", "GH.Shared.dll", *(name + ".dll" for name in sorted(core.REPLACED_PACKAGES))]
        for name in self.originals:
            (self.managed / name).write_bytes(("owned original:" + name).encode())
        files = storage.inventory(self.game)
        manifest = {"schema": 1, "target": "game", "inputKey": "a" * 64, "game": {"files": files}, "mod": {"modBuild": 672}}
        capsule = {"schema": 1, "scope": "quest-code-update-v1", "inputKey": manifest["inputKey"],
            "unityVersion": "2021.3.5f1", "packageAbi": "b" * 64, "metadataVersion": 29,
            "managedKey": storage.value_hash(core._managed(manifest)), "modSchema": 1, "dynamicProfile": True,
            "scriptTypes": [{"assembly": "GH.Runtime.dll", "namespace": "", "class": "OwnedWidget",
                             "propertiesHash": "1" * 32, "executionOrder": 0}]}
        native = bytearray(64)
        native[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<H", native, 18, 183)
        values = {core.INPUT: storage.canonical(manifest), core.CAPSULE: storage.canonical(capsule),
            core.INSTALLATION: storage.canonical({"schema": 1, "inputKey": manifest["inputKey"]}),
            core.LIBRARY: bytes(native), core.METADATA: struct.pack("<II", 0xFAB11BAF, 29),
            core.ASSEMBLIES: storage.canonical({"names": self.originals, "types": [16] * len(self.originals)})}
        with zipfile.ZipFile(self.base, "w") as archive:
            for name, value in values.items(): archive.writestr(name, value)
        template = self.repo / "unity/GloomhavenVR.Quest"
        for name, value in {"Packages/manifest.json": '{"dependencies":{}}', "ProjectSettings/ProjectSettings.asset": "settings",
            "Assets/Quest/Runtime/QuestOfflineProfile.cs": "runtime", "Assets/Quest/Editor/QuestCodeUpdateBuild.cs": "editor"}.items():
            path = template / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(value)
        text = self.repo / "src/GloomhavenVR/Core/Loc/QuestText.cs"
        text.parent.mkdir(parents=True)
        text.write_text("namespace GloomhavenVR.Core {}")
        self.arguments = dict(profile={"displayName": "Fixture"}, version_code=673, version_name="B673",
                              package_abi="b" * 64, code_key="c" * 64)
        self.plugins = self.project / "Assets/Plugins/QuestGame"
        self.staged = self.root / "woven"
        self.staged.mkdir()
        for name in ("GH.Runtime.dll", "UnityEngine.UI.dll", "GloomhavenVR.dll", "QuestGame.Compatibility.dll"):
            (self.staged / name).write_bytes(("woven:" + name).encode())
        (self.staged / "link.xml").write_text("<linker/>")

    def stage(self):
        return core.stage_code_project(self.repo, self.project, self.base, self.managed, **self.arguments)

    def assert_packages_disabled(self):
        runtime = json.loads((self.project / "Assets/Quest/Runtime/QuestGame.Campaign.asmdef").read_text())
        self.assertFalse(set(runtime["precompiledReferences"]) & {name + ".dll" for name in core.REPLACED_PACKAGES})
        for name in core.REPLACED_PACKAGES:
            text = (self.plugins / (name + ".dll.meta")).read_text()
            self.assertIn("PluginImporter:", text)
            self.assertIn("isExplicitlyReferenced: 1", text)
            self.assertIn("enabled: 0", text)
            self.assertNotIn("enabled: 1", text)

    def bind(self, *, bad_report=False):
        commands = []
        sdk = self.project / "QuestStartupEvidence/PlayerSdk"
        expected = {"GH.Runtime.dll", "GH.Shared.dll", "GloomhavenVR.dll", "QuestGame.Compatibility.dll"}
        def compiler(argv, log, **kwargs):
            commands.append(argv)
            if "GloomhavenVR.Quest.Editor.QuestCodeUpdateBuild.CompileSdk" in argv:
                sdk.mkdir(parents=True)
                for name in builder.REPLACED_PACKAGES: (sdk / (name + ".dll")).write_bytes(("actual SDK fixture:" + name).encode())
                storage.write_json(sdk / "compilation.json", {"schema": 1, "target": "Android", "backend": "IL2CPP",
                    "compilation": "Player", "options": "DevelopmentBuild|Assertions", "unityVersion": "2021.3.5f1"})
            else:
                self.assertIn("package-api", argv)
                original = Path(argv[argv.index("--managed") + 1])
                rewritten = Path(argv[argv.index("--output") + 1])
                rewritten.mkdir(parents=True)
                self.assertEqual({path.name for path in original.glob("*.dll")}, expected)
                for path in original.glob("*.dll"): (rewritten / path.name).write_bytes(b"bound:" + path.read_bytes())
                report = {"schema": 1, "sdkTarget": "Android", "complete": True, "issues": [],
                    "inputAssemblies": {row["path"]: row["sha256"] for row in storage.inventory(original)},
                    "sdkAssemblies": {row["path"]: row["sha256"] for row in storage.inventory(sdk, [n + ".dll" for n in builder.REPLACED_PACKAGES])},
                    "outputAssemblies": {row["path"]: row["sha256"] for row in storage.inventory(rewritten)}}
                if bad_report: report["inputAssemblies"]["UnityEngine.UI.dll"] = "0" * 64
                storage.write_json(Path(argv[argv.index("--report") + 1]), report)
        with patch.object(builder, "command", side_effect=compiler):
            driver._bind_code_packages(builder, SimpleNamespace(dotnet=sys.executable), self.output, self.repo, self.game,
                                      self.project, {"editor": sys.executable}, {}, "d" * 64)
        return commands

    def test_fresh_project_weave_handoff_and_actual_package_binding_census(self):
        self.stage()
        before = storage.inventory(self.game)
        self.assertEqual(set(builder.REPLACED_PACKAGES), core.REPLACED_PACKAGES)
        self.assert_packages_disabled()
        ui_meta = (self.plugins / "UnityEngine.UI.dll.meta").read_bytes()
        builder.deploy_woven_assemblies(self.staged, self.game, self.project)
        builder.stage_campaign_runtime_assembly(self.project)
        self.assert_packages_disabled()
        self.assertEqual((self.plugins / "UnityEngine.UI.dll.meta").read_bytes(), ui_meta)
        woven_ui = (self.plugins / "UnityEngine.UI.dll").read_bytes()
        self.assertEqual(woven_ui, b"woven:UnityEngine.UI.dll")
        first = self.bind()
        self.assertEqual(len(first), 2)
        bound = (self.plugins / "GH.Runtime.dll").read_bytes()
        self.assertEqual(bound, b"bound:woven:GH.Runtime.dll")
        self.assertEqual((self.plugins / "UnityEngine.UI.dll").read_bytes(), woven_ui)
        self.assertEqual(storage.inventory(self.game), before)

    def test_failed_pre_repair_ready_project_is_repaired_without_resetting_library_or_code(self):
        self.stage()
        for path in self.plugins.iterdir():
            if path.name.removesuffix(".meta").removesuffix(".dll") in core.REPLACED_PACKAGES: path.unlink()
        game_plugin = self.plugins / "GH.Runtime.dll"
        game_plugin.write_bytes(b"completed prior weaving")
        library = self.project / "Library/retained-bee-object"
        library.parent.mkdir()
        library.write_bytes(b"expensive native object")
        result = self.stage()
        self.assertTrue(result["reused"])
        self.assert_packages_disabled()
        self.assertEqual(game_plugin.read_bytes(), b"completed prior weaving")
        self.assertEqual(library.read_bytes(), b"expensive native object")
        stamps = {path.name: path.stat().st_mtime_ns for path in self.plugins.iterdir()}
        self.stage()
        self.assertEqual({path.name: path.stat().st_mtime_ns for path in self.plugins.iterdir()}, stamps)
        builder.deploy_woven_assemblies(self.staged, self.game, self.project)

    def test_ready_project_repairs_only_disabled_importers_and_missing_references(self):
        self.stage()
        metadata = self.plugins / "UnityEngine.UI.dll.meta"
        metadata.write_text("accidentally edited importer")
        (self.plugins / "Unity.XR.OpenXR.dll").unlink()
        regular = self.plugins / "GH.Runtime.dll"
        old_stat = regular.stat()
        self.stage()
        self.assert_packages_disabled()
        self.assertEqual(regular.stat().st_mtime_ns, old_stat.st_mtime_ns)
        text = metadata.read_text().replace("enabled: 0", "enabled: 1")
        metadata.write_text(text)
        self.stage()
        self.assert_packages_disabled()

    def test_missing_package_boundary_reproduces_report_and_nonpackage_contract_stays_strict(self):
        self.stage()
        (self.plugins / "UnityEngine.UI.dll").unlink()
        with self.assertRaisesRegex(storage.BuildError, r"recovered DLL.*UnityEngine.UI.dll"):
            builder.deploy_woven_assemblies(self.staged, self.game, self.project)
        self.stage()
        (self.plugins / "GH.Runtime.dll.meta").unlink()
        with self.assertRaisesRegex(storage.BuildError, r"recovered DLL.*GH.Runtime.dll"):
            builder.deploy_woven_assemblies(self.staged, self.game, self.project)

    def test_duplicate_package_identity_is_not_silently_accepted(self):
        self.stage()
        extra = self.project / "Assets/Unexpected/UnityEngine.UI.dll"
        extra.parent.mkdir()
        extra.write_bytes(b"duplicate")
        extra.with_suffix(".dll.meta").write_text("guid: " + "e" * 32)
        with self.assertRaisesRegex(storage.BuildError, r"recovered DLL.*UnityEngine.UI.dll"):
            builder.deploy_woven_assemblies(self.staged, self.game, self.project)

    def test_package_reference_and_importer_links_are_rejected_before_writes(self):
        self.stage()
        external = self.root / "untouchable"
        external.write_bytes(b"not builder output")
        for name in ("UnityEngine.UI.dll", "UnityEngine.UI.dll.meta"):
            with self.subTest(name=name):
                path = self.plugins / name
                original = path.read_bytes()
                path.unlink()
                try: path.symlink_to(external)
                except OSError: self.skipTest("Host does not allow symlinks")
                with self.assertRaisesRegex(storage.BuildError, "cannot be links"): self.stage()
                self.assertEqual(external.read_bytes(), b"not builder output")
                path.unlink()
                path.write_bytes(original)

    def test_wrong_package_binding_census_cannot_publish_plugins(self):
        self.stage()
        builder.deploy_woven_assemblies(self.staged, self.game, self.project)
        before = (self.plugins / "GH.Runtime.dll").read_bytes()
        with self.assertRaisesRegex(storage.BuildError, "package API binding is incomplete"):
            self.bind(bad_report=True)
        self.assertEqual((self.plugins / "GH.Runtime.dll").read_bytes(), before)


if __name__ == "__main__": unittest.main()
