"""Exact native repair admission, retained contracts and two real NDK outputs."""
import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import native_plugins
import proton_helper_update as helper
import storage


class NativeHelperContracts(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="Quest NLS update ")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.builder = SimpleNamespace(BuildError=storage.BuildError, native_plugins=native_plugins,
            digest=storage.digest, record_file=storage.record_file, value_hash=storage.value_hash,
            write_json=storage.write_json, canonical=storage.canonical, Stages=storage.Stages,
            build_progress=Mock())
        self.document = {"schema": 1, "backend": helper.BACKEND, "files": []}
        self.old = {}
        for index, name in enumerate(sorted(native_plugins.PROTON_REQUIRED | {"libQuestApparance.so", "libopus_egpv.so"})):
            body = b"inert signed member fixture " + bytes([index]) * 128
            self.old[name] = body
            self.document["files"].append({"path": native_plugins.PREFIX + name, "size": len(body),
                                            "sha256": hashlib.sha256(body).hexdigest()})
        self.apk = self.root / "old.apk"
        self.write_apk()

    def write_apk(self, *, changed=None):
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr(helper.CONTRACT_MEMBER, storage.canonical(self.document))
            for name, body in self.old.items():
                archive.writestr("lib/arm64-v8a/" + name, changed.get(name, body) if changed else body)
            archive.writestr("assets/retained-game-data", b"Game banks are never read by this helper.")

    def test_only_the_complete_exact_reviewed_backend_transition_is_admitted(self):
        current, previous = copy.deepcopy(list(helper.CURRENT_SOURCE_ROWS)), copy.deepcopy(list(helper.PREVIOUS_SOURCE_ROWS))
        expected = {row["path"] for row in current if row not in previous}
        self.assertEqual(helper.changed_rows(current, previous, helper.BACKEND), expected)
        legacy = copy.deepcopy(list(helper.LEGACY_SOURCE_ROWS))
        self.assertEqual(helper.changed_rows(current, legacy, helper.BACKEND),
                         {row["path"] for row in current if row not in legacy})
        self.assertEqual(helper.changed_rows(current, current, helper.BACKEND), set())
        for backend in (None, "box64-wine9", "proton-similar"):
            self.assertEqual(helper.changed_rows(current, previous, backend), set())
        for side in ("old", "new"):
            for mutation in ("missing", "duplicate", "sha", "size"):
                old, new = copy.deepcopy(previous), copy.deepcopy(current)
                rows = old if side == "old" else new
                if mutation == "missing": rows.pop()
                elif mutation == "duplicate": rows.append(rows[0])
                elif mutation == "sha": rows[0]["sha256"] = "f" * 64
                else: rows[0]["size"] += 1
                with self.subTest(side=side, mutation=mutation):
                    self.assertEqual(helper.changed_rows(new, old, helper.BACKEND), set())

    def test_actual_apk_contract_and_two_helper_bytes_are_qualified(self):
        document, rows = helper._read_native_contract(self.builder, self.apk)
        self.assertEqual(document, self.document)
        self.assertEqual(set(rows), set(helper.NATIVE_NAMES))
        for name in helper.NATIVE_NAMES:
            self.write_apk(changed={name: b"x" * len(self.old[name])})
            with self.subTest(name=name), self.assertRaisesRegex(storage.BuildError, "differs from its signed contract"):
                helper._read_native_contract(self.builder, self.apk)

    def test_contract_sizes_and_backend_cannot_be_guessed(self):
        for mutation in ("backend", "missing", "size"):
            original = copy.deepcopy(self.document)
            if mutation == "backend": self.document["backend"] = "box64-wine9"
            elif mutation == "missing": self.document["files"] = self.document["files"][1:]
            else:
                next(row for row in self.document["files"] if row["path"].endswith("/libQuestApparance.so"))["size"] += 1
            self.write_apk()
            with self.subTest(mutation=mutation), self.assertRaises(storage.BuildError):
                helper._read_native_contract(self.builder, self.apk)
            self.document = original

    def test_update_replaces_only_helpers_and_both_contract_copies(self):
        compiled = self.root / "compiled"
        compiled.mkdir()
        rows = []
        for name in helper.NATIVE_NAMES:
            path = compiled / name
            path.write_bytes(b"new inert helper fixture " + name.encode() * 4)
            rows.append(storage.record_file(path, native_plugins.PREFIX + name))
        evidence = {"schema": 1, "scope": helper.SCOPE, "backend": helper.BACKEND,
                    "nativeFiles": rows, "androidDeviceExecutionVerified": False}
        def patch_resource(base, updates, *, replacements):
            self.assertEqual(base, self.apk)
            self.resource = copy.deepcopy(updates["quest-procedural-native"])
            return {**replacements, "assets/bin/Data/resources.assets": b"qualified resource object witness"}
        core = SimpleNamespace(patch_resource_json=patch_resource)
        original = self.apk.read_bytes()
        with patch.object(helper, "_compile", return_value=(compiled, evidence)):
            result, actual = helper.produce(self.builder, ROOT, self.root, {}, self.apk, self.root / "update", core,
                                            {"existing-replacement": b"retained"})
        self.assertEqual(self.apk.read_bytes(), original)
        self.assertEqual(set(result), {"existing-replacement", helper.CONTRACT_MEMBER,
            "assets/bin/Data/resources.assets", *("lib/arm64-v8a/" + n for n in helper.NATIVE_NAMES)})
        contract = json.loads(result[helper.CONTRACT_MEMBER])
        self.assertEqual(contract, self.resource)
        self.assertFalse(actual["androidDeviceExecutionVerified"])
        for row in self.document["files"]:
            if Path(row["path"]).name not in helper.NATIVE_NAMES:
                self.assertIn(row, contract["files"])
        self.assertEqual(contract["helperUpdate"], actual)
        self.assertTrue((self.root / "update/native-helper-update.json").is_file())

    def test_unknown_helper_source_cannot_reach_compiler_or_apk_publication(self):
        source = self.root / "source"
        for name in helper.SOURCE_NAMES:
            destination = source / helper.PREFIX / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes((ROOT / helper.PREFIX / name).read_bytes())
        path = source / helper.PREFIX / "bridge.c"
        path.write_bytes(path.read_bytes() + b"\n/* Unknown edit, no compact exception. */\n")
        self.builder.command = Mock(side_effect=AssertionError("No compiler for unrecognized sources"))
        with self.assertRaisesRegex(storage.BuildError, "exact reviewed source revision"):
            helper._compile(self.builder, source, self.root, {})
        self.builder.command.assert_not_called()

    def test_actual_cached_ndk_compiles_only_two_small_helpers_and_reuses_receipt(self):
        selected = os.environ.get("QUEST_PROTON_NDK")
        if not selected:
            self.skipTest("Set QUEST_PROTON_NDK to compile the actual Android bridge and launcher.")
        source = self.root / "source"
        for name in (*helper.SOURCE_NAMES, "runtime.py", "upstream.lock.json"):
            destination = source / helper.PREFIX / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            body = (ROOT / helper.PREFIX / name).read_bytes()
            destination.write_bytes(body)
        commands = []
        def command(arguments, log):
            commands.append(arguments)
            result = subprocess.run(arguments, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=30)
            log.write_text(result.stdout, encoding="utf-8")
            if result.returncode:
                raise storage.BuildError(result.stdout)
            return result.stdout
        self.builder.command = command
        cache, evidence = helper._compile(self.builder, source, self.root, {"androidNdk": selected})
        self.assertEqual(len(commands), 9)
        self.assertEqual(sum(Path(args[0]).name in ("clang", "clang.exe") for args in commands), 3)
        self.assertEqual([Path(row["path"]).name for row in evidence["nativeFiles"]], list(helper.NATIVE_NAMES))
        self.assertGreater(evidence["embeddedWorker"]["size"], 64)
        worker = (cache / "ApparanceWorker.exe").read_bytes()
        bridge = (cache / "libQuestApparance.so").read_bytes()
        self.assertEqual(evidence["embeddedWorker"]["sha256"], hashlib.sha256(worker).hexdigest())
        self.assertTrue(worker.startswith(b"MZ"))
        self.assertIn(worker, bridge)
        self.assertTrue(any("-DGHPR_EMBEDDED_WORKER=1" in command for command in commands))
        self.assertFalse(evidence["wineBinaryChanged"])
        self.assertFalse(evidence["winePayloadChanged"])
        self.assertFalse(evidence["originalGameAssetsRebuilt"])
        self.assertFalse(evidence["androidDeviceExecutionVerified"])
        self.assertLess(sum(row["size"] for row in evidence["nativeFiles"]), 128 * 1024)
        self.builder.command = Mock(side_effect=AssertionError("Retained compiled helpers must not recompile"))
        again, reused = helper._compile(self.builder, source, self.root, {"androidNdk": selected})
        self.assertEqual(again, cache)
        self.assertEqual(reused, evidence)
        self.builder.command.assert_not_called()
        retained = os.environ.get("QUEST_PROTON_EVIDENCE")
        if retained:
            destination = Path(retained)
            destination.mkdir(parents=True, exist_ok=True)
            storage.write_json(destination / "actual-native-helper-proof.json", {**evidence, "commands": commands})
            for name in helper.NATIVE_NAMES:
                shutil.copy2(cache / name, destination / name)
                shutil.copy2(cache / (name + ".symbols.log"), destination / (name + ".symbols.log"))


if __name__ == "__main__":
    unittest.main()
