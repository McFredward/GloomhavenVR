"""Pinned acquisition repairs retain Proton assets without guessing other backends.

The native-cache fixtures contain inert bytes. They exercise the real cache
inventory and preparation/update identity boundaries, never guest execution or
Android compilation. Baseline hashes identify the actual B672 native sources.
"""
import copy
import hashlib
import importlib.util
from pathlib import Path
import shutil
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import preparation_identity as identity
import recovery_resume
import storage
import update_driver as driver

RUNTIME = ROOT / "tools/quest-procedural-runtime"
BACKEND = "proton-arm64ec-fex"
NATIVE_BASELINE = {
    "proton_runtime.py": "66c4da1a384090d0959fd8930230d6f456445c723b0be485c2b24ee750cacb15",
    "proton_layout.py": "656e626e5097e62c702e29e3845a833fb0ad4f776b76ad3c07677a54ce8f924c",
    "proton_launcher.c": "266e85b860c39deabf19407b68a511c9cf9e45c33baddc0d04727cb216892133",
    "proton.lock.json": "0f833bf80c23ee3d394880a11b0e332f0a7e52f02912b73cc8c39fcd4cb73f94",
    "bridge.c": "b0854b7b8216d6029cdcb5ae1029acb1bb0439111db4013a10bdffbf10393339",
    "worker.c": "e8f27b055a2e06f6a68678f00f49a49cf42d44c2e9ceeccfd65982c273ff3d48",
    "protocol.h": "ff353f38e7ff9d1aabd7c48ccba93248e4764111e8a4d29649cd4435fac323a5",
}


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


proton = module("acquisition_identity_proton", RUNTIME / "proton_runtime.py")
layout = proton.local("proton_layout")


def row(name):
    return storage.record_file(ROOT / name, name)


class PinnedNativeCacheTests(unittest.TestCase):
    def test_acquisition_repair_leaves_actual_seven_native_sources_unchanged(self):
        self.assertEqual({name: storage.digest(RUNTIME / name) for name in NATIVE_BASELINE}, NATIVE_BASELINE)
        self.assertEqual(row(identity.PROTON_DOWNLOAD_FIXED["path"]), identity.PROTON_DOWNLOAD_FIXED)
        self.assertEqual(row(identity.UPDATE_TOOL_HANDOFF_FIXED["path"]), identity.UPDATE_TOOL_HANDOFF_FIXED)
        self.assertEqual(driver._PROTON_DOWNLOAD_ROWS,
                         (identity.PROTON_DOWNLOAD_PREVIOUS, identity.PROTON_DOWNLOAD_FIXED))

    def test_real_proton_cache_reuses_old_native_key_after_acquisition_only_changes(self):
        with tempfile.TemporaryDirectory(prefix="Quest acquisition cache ") as temporary:
            root = Path(temporary)
            source = root / "source"
            source.mkdir()
            for name in NATIVE_BASELINE:
                shutil.copyfile(RUNTIME / name, source / name)
            runtime = source / "runtime.py"
            runtime.write_text("# Inert previous acquisition source; never imported.\n")
            ndk = root / "ndk"
            ndk.mkdir()
            (ndk / "source.properties").write_text("Pkg.Revision = native-cache-fixture\n")
            ndk_hash = storage.digest(ndk / "source.properties")
            key = storage.value_hash({"backend": BACKEND, "sources": NATIVE_BASELINE, "ndkSha256": ndk_hash})
            cache = root / "cache"
            output = cache / "procedural-runtime-proton" / key
            required = {"native/" + name for name in layout.NATIVE_MAP.values()}
            required |= {"native/libQuestApparance.so", "native/libquest_proton.so", "native/libquest_proton_server.so",
                         "payload/ApparanceWorker.exe", "payload/wine/lib/wine/aarch64-windows/ntdll.dll",
                         "payload/wine/lib/wine/aarch64-windows/libarm64ecfex.dll"}
            records = []
            for name in sorted(required):
                path = output / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"Inert native identity fixture; never executed or delivered.")
                records.append(storage.record_file(path, name))
            receipt = {"inputKey": key, "backend": BACKEND, "sources": dict(NATIVE_BASELINE),
                       "ndkSha256": ndk_hash, "wine": proton.LOCK["wine"], "fex": proton.LOCK["fex"],
                       "artifacts": records}
            storage.write_json(output / "native-build.json", receipt)
            fetch = Mock(side_effect=AssertionError("Unexpected download or native compiler invocation"))
            helpers = {"digest": storage.digest, "fetch": fetch, "tool": fetch, "ndk_bin": fetch}
            with patch.object(proton, "HERE", source):
                self.assertEqual(proton.build(cache, ndk, helpers=helpers), (output, receipt))
                runtime.write_bytes((RUNTIME / "runtime.py").read_bytes())
                self.assertEqual(proton.build(cache, ndk, helpers=helpers), (output, receipt))
                fetch.assert_not_called()
                # A real native producer change must leave that old owner.
                with (source / "bridge.c").open("ab") as stream:
                    stream.write(b"\n/* Unknown native source change. */\n")
                with self.assertRaisesRegex(AssertionError, "Unexpected download"):
                    proton.build(cache, ndk, helpers=helpers)
                self.assertEqual(fetch.call_count, 1)


class PreparationAcquisitionTests(unittest.TestCase):
    def inputs(self, runtime_row, *, backend=BACKEND, driver_row=None):
        files = [row(identity.BUILDER), copy.deepcopy(runtime_row)]
        if driver_row is not None:
            files.append(copy.deepcopy(driver_row))
        inputs = {"target": "game", "recipe": 1, "inputKey": "a" * 64,
                  "game": {"key": "b" * 64}, "profile": {"displayName": "Fixture Owner"},
                  "mod": {"modBuild": 672, "files": files}}
        if backend is not None:
            inputs["proceduralBackend"] = backend
        return inputs

    def scope(self, inputs, **options):
        return storage.value_hash(identity._scope(inputs, ROOT, recovery_resume, **options))

    def test_exact_proton_acquisition_alias_retains_prefix_and_complete_producer_scope(self):
        previous = self.inputs(identity.PROTON_DOWNLOAD_PREVIOUS)
        current = self.inputs(identity.PROTON_DOWNLOAD_FIXED)
        for prefix in (False, True):
            with self.subTest(original_prefix=prefix):
                self.assertEqual(self.scope(previous, original_prefix=prefix),
                                 self.scope(current, original_prefix=prefix))

    def test_paired_late_classifier_repair_retains_actual_game_preparation_owners(self):
        previous = self.inputs(identity.PROTON_DOWNLOAD_PREVIOUS, driver_row=identity.UPDATE_DOWNLOAD_PREVIOUS)
        current = self.inputs(identity.PROTON_DOWNLOAD_FIXED, driver_row=identity.UPDATE_DOWNLOAD_FIXED)
        for completed in (False, True):
            with self.subTest(completed=completed):
                self.assertEqual(self.scope(previous, original_prefix=True, completed=completed),
                                 self.scope(current, original_prefix=True, completed=completed))

    def test_missing_unknown_or_box64_backend_does_not_gain_runtime_alias(self):
        for backend in (None, "box64-wine9", "unknown-runtime"):
            previous = self.inputs(identity.PROTON_DOWNLOAD_PREVIOUS, backend=backend)
            current = self.inputs(identity.PROTON_DOWNLOAD_FIXED, backend=backend)
            for prefix in (False, True):
                with self.subTest(backend=backend, original_prefix=prefix):
                    self.assertNotEqual(self.scope(previous, original_prefix=prefix),
                                        self.scope(current, original_prefix=prefix))

    def test_unknown_runtime_or_other_helper_edit_still_changes_preparation_scope(self):
        previous = self.inputs(identity.PROTON_DOWNLOAD_PREVIOUS)
        current = self.inputs(identity.PROTON_DOWNLOAD_FIXED)
        unknown = copy.deepcopy(current)
        unknown["mod"]["files"][1]["sha256"] = "f" * 64
        helper = "tools/quest-procedural-runtime/proton_runtime.py"
        previous["mod"]["files"].append(row(helper))
        current["mod"]["files"].append(row(helper))
        changed_helper = copy.deepcopy(current)
        changed_helper["mod"]["files"][-1]["sha256"] = "f" * 64
        for prefix in (False, True):
            with self.subTest(original_prefix=prefix):
                self.assertNotEqual(self.scope(self.inputs(identity.PROTON_DOWNLOAD_PREVIOUS), original_prefix=prefix),
                                    self.scope(unknown, original_prefix=prefix))
                self.assertEqual(self.scope(previous, original_prefix=prefix), self.scope(current, original_prefix=prefix))
                self.assertNotEqual(self.scope(previous, original_prefix=prefix),
                                    self.scope(changed_helper, original_prefix=prefix))

    def test_unknown_late_classifier_row_stays_distinct(self):
        previous = self.inputs(identity.PROTON_DOWNLOAD_PREVIOUS, driver_row=identity.UPDATE_DOWNLOAD_PREVIOUS)
        current = self.inputs(identity.PROTON_DOWNLOAD_FIXED, driver_row=identity.UPDATE_DOWNLOAD_FIXED)
        current["mod"]["files"][-1]["sha256"] = "f" * 64
        self.assertNotEqual(self.scope(previous, original_prefix=True), self.scope(current, original_prefix=True))

    def test_exact_tool_handoff_repair_keeps_completed_preparation_ownership(self):
        previous = self.inputs(identity.PROTON_DOWNLOAD_FIXED, driver_row=identity.UPDATE_RETAINED_APK_FIXED)
        current = self.inputs(identity.PROTON_DOWNLOAD_FIXED, driver_row=identity.UPDATE_TOOL_HANDOFF_FIXED)
        unknown = copy.deepcopy(current)
        unknown["mod"]["files"][-1]["sha256"] = "f" * 64
        for completed in (False, True):
            with self.subTest(completed=completed):
                self.assertEqual(self.scope(previous, original_prefix=True, completed=completed),
                                 self.scope(current, original_prefix=True, completed=completed))
                self.assertNotEqual(self.scope(previous, original_prefix=True, completed=completed),
                                    self.scope(unknown, original_prefix=True, completed=completed))

    def test_asset_recovery_recipe_does_not_change_for_procedural_acquisition_sources(self):
        previous = self.inputs(identity.PROTON_DOWNLOAD_PREVIOUS)
        current = self.inputs(identity.PROTON_DOWNLOAD_FIXED)
        for value in (previous, current):
            value["mod"]["files"].append(row("tools/quest-recovery/recover.py"))
        self.assertEqual(recovery_resume.recipe_key(previous, 1), recovery_resume.recipe_key(current, 1))


class UpdateAcquisitionTests(unittest.TestCase):
    def test_actual_tool_handoff_revision_needs_no_unity_but_unknown_driver_does(self):
        previous = [identity.PROTON_DOWNLOAD_FIXED, identity.UPDATE_RETAINED_APK_FIXED]
        current = [identity.PROTON_DOWNLOAD_FIXED, identity.UPDATE_TOOL_HANDOFF_FIXED]
        base = {"manifest": {"proceduralBackend": BACKEND, "mod": {"files": previous, "modBuild": 672}},
                "capsule": {"packageAbi": "fixture"}}
        selected_builder = SimpleNamespace(preparation_identity=identity)
        with patch.object(driver, "payload_producer", return_value=None), \
                patch.object(driver, "package_fingerprint", return_value="fixture"):
            result = driver.update_requirements(selected_builder, ROOT, base, current, 672)
            self.assertEqual(result["rebuildReasons"], [])
            for name in ("requiresUnity", "codeChanged", "artChanged"):
                self.assertFalse(result[name], name)
            changed = copy.deepcopy(current)
            changed[-1]["sha256"] = "f" * 64
            result = driver.update_requirements(selected_builder, ROOT, base, changed, 672)
            self.assertTrue(result["requiresUnity"])
            self.assertTrue(result["codeChanged"])

    def test_exact_acquisition_repair_can_retain_proton_native_payload(self):
        self.assertEqual(driver.rebuild_reasons([identity.PROTON_DOWNLOAD_FIXED], [identity.PROTON_DOWNLOAD_PREVIOUS],
                                              procedural_backend=BACKEND), [])
        self.assertEqual(driver.rebuild_reasons([identity.PROTON_DOWNLOAD_PREVIOUS], [identity.PROTON_DOWNLOAD_FIXED],
                                              procedural_backend=BACKEND), [])

    def test_missing_unknown_or_box64_backend_requires_real_migration(self):
        for backend in (None, "box64-wine9", "unknown-runtime"):
            with self.subTest(backend=backend):
                reasons = driver.rebuild_reasons([identity.PROTON_DOWNLOAD_FIXED], [identity.PROTON_DOWNLOAD_PREVIOUS],
                                                 procedural_backend=backend)
                self.assertTrue(reasons)
                self.assertIn("Native Quest programs", reasons[0])
                self.assertIn("runtime.py", reasons[0])

    def test_unknown_or_missing_runtime_pin_and_native_producer_are_never_aliased(self):
        unknown = copy.deepcopy(identity.PROTON_DOWNLOAD_FIXED)
        unknown["sha256"] = "f" * 64
        cases = [([unknown], [identity.PROTON_DOWNLOAD_PREVIOUS]),
                 ([identity.PROTON_DOWNLOAD_FIXED], []), ([], [identity.PROTON_DOWNLOAD_PREVIOUS])]
        for name in ("proton.lock.json", "proton_runtime.py", "bridge.c"):
            previous = row("tools/quest-procedural-runtime/" + name)
            current = dict(previous, sha256="f" * 64)
            cases.append(([identity.PROTON_DOWNLOAD_FIXED, current], [identity.PROTON_DOWNLOAD_PREVIOUS, previous]))
        for current, previous in cases:
            with self.subTest(current=current, previous=previous):
                reasons = driver.rebuild_reasons(current, previous, procedural_backend=BACKEND)
                self.assertTrue(reasons)
                self.assertIn("Native Quest programs", reasons[0])

    def test_actual_update_requirements_uses_original_manifest_backend(self):
        selected = [identity.PROTON_DOWNLOAD_FIXED]
        manifest = {"mod": {"files": [identity.PROTON_DOWNLOAD_PREVIOUS], "modBuild": 672}}
        base = {"manifest": manifest, "capsule": {"packageAbi": "fixture"}}
        for backend in (BACKEND, "box64-wine9", None):
            if backend is None:
                manifest.pop("proceduralBackend", None)
            else:
                manifest["proceduralBackend"] = backend
            with self.subTest(backend=backend), patch.object(driver, "payload_producer", return_value=None), \
                    patch.object(driver, "package_fingerprint", return_value="fixture"), \
                    patch.object(driver, "rebuild_reasons", wraps=driver.rebuild_reasons) as classify:
                result = driver.update_requirements(SimpleNamespace(), ROOT, base, selected, 672)
                self.assertEqual(classify.call_args.kwargs["procedural_backend"], backend)
                self.assertEqual(result["updateStrategy"], "compact-code" if backend == BACKEND else "cached-full-build")
                self.assertEqual(bool(result["rebuildReasons"]), backend != BACKEND)
                self.assertEqual(result["requiresUnity"], backend != BACKEND)

    def test_actual_paired_host_repair_needs_no_unity_but_unknown_classifier_does(self):
        current = [identity.PROTON_DOWNLOAD_FIXED, identity.UPDATE_DOWNLOAD_FIXED]
        previous = [identity.PROTON_DOWNLOAD_PREVIOUS, identity.UPDATE_DOWNLOAD_PREVIOUS]
        base = {"manifest": {"proceduralBackend": BACKEND, "mod": {"files": previous, "modBuild": 672}},
                "capsule": {"packageAbi": "fixture"}}
        selected_builder = SimpleNamespace(preparation_identity=identity)
        with patch.object(driver, "payload_producer", return_value=None), \
                patch.object(driver, "package_fingerprint", return_value="fixture"):
            result = driver.update_requirements(selected_builder, ROOT, base, current, 672)
            self.assertEqual(result["updateStrategy"], "compact-code")
            for name in ("requiresUnity", "codeChanged", "artChanged"):
                self.assertFalse(result[name], name)
            self.assertEqual(result["rebuildReasons"], [])
            unknown = copy.deepcopy(current)
            unknown[-1]["sha256"] = "f" * 64
            changed = driver.update_requirements(selected_builder, ROOT, base, unknown, 672)
            self.assertTrue(changed["requiresUnity"])
            self.assertTrue(changed["codeChanged"])
            # Without the actual external reviewed producer profile, the host
            # driver keeps its ordinary code identity rather than guessing.
            unqualified = driver.update_requirements(SimpleNamespace(), ROOT, base, current, 672)
            self.assertTrue(unqualified["requiresUnity"])
            self.assertTrue(unqualified["codeChanged"])


if __name__ == "__main__":
    unittest.main()
