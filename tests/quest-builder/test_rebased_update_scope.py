"""A current-dev mod update retains the original owned Campaign conversion.

Separate movie bytes are not compiler input. These controls also pin the
opposite boundary: runtime, embedded shaders and unknown files remain qualified.
The independent movie delivery/ownership tests cover the actual media publisher.
"""
import copy
from pathlib import Path
import sys
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch
import zipfile
import json

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import apk_update
import update_driver as driver
import preparation_identity
import native_update_resume
import storage
import recovery_resume
import test_update_driver as fixtures


def rows(*paths, digest="1"):
    return [{"path": path, "size": 10, "sha256": digest * 64} for path in paths]


class RebasedUpdateScopeTests(unittest.TestCase):
    def test_movie_content_does_not_invalidate_native_pair_or_original_assets(self):
        media = rows("src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.mov",
                     "src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.sbs-v2.mov",
                     "src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.sbs-v2.provenance.json",
                     "src/GloomhavenVR/Assets/Movies/Heroes/Doomstalker.sbs-v1.mov")
        self.assertEqual(driver._code_rows(media), [])
        self.assertEqual(driver._native_producer_rows(media), [])
        self.assertEqual(driver.rebuild_reasons(media, []), [])
        self.assertEqual(driver._art_rows(media), [])
        for path in ("src/GloomhavenVR/Assets/Movies/Future.cs",
                     "src/GloomhavenVR/Assets/Movies/Future.bundle",
                     "src/GloomhavenVR/Assets/StereoVideo/ui-sbs.bundle",
                     "src/GloomhavenVR/Assets/SteamLink/stream-quality.exe",
                     "src/GloomhavenVR/GloomhavenVR.csproj"):
            with self.subTest(path=path):
                self.assertEqual(driver._code_rows(rows(path)), rows(path))

    def test_compiler_and_authored_bank_snapshots_do_not_duplicate_separate_movies(self):
        runtime = "src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs"
        embedded = "src/GloomhavenVR/Assets/StereoVideo/ui-sbs.bundle"
        authored = "unity/GloomhavenVR.Assets/Assets/Bundle/board.prefab"
        movies = ("src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.mov",
                  "src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.sbs-v2.provenance.json")
        recipes = ("tools/quest-movies/films/one.depth.gz", "tools/quest-builder/movie_banks.py",
                   "tools/quest-builder/movie_render.py", "tools/quest-builder/requirements-movies.txt")
        files = rows(runtime, embedded, authored, *movies, *recipes)
        for art_changed in (False, True):
            with self.subTest(art_changed=art_changed):
                selected = driver._update_source_rows(files, art_changed=art_changed)
                self.assertTrue(all(row["path"] not in movies for row in selected))
                self.assertTrue(all(row["path"] not in recipes for row in selected))
                self.assertEqual({row["path"] for row in selected},
                                 {runtime, embedded, authored} if art_changed else {runtime, embedded})

    def test_current_dev_runtime_and_stereo_shader_request_only_compact_compilation(self):
        fixture = fixtures.UpdateDriverTests("runTest")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        before = rows("src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.cs")
        after = rows("src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.cs",
                     "src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs",
                     "src/GloomhavenVR/Assets/StereoVideo/ui-sbs.bundle", digest="2")
        # The mod's existing original producer/template identities are intact.
        # New C# and an embedded shader need a matched native pair, never an
        # original scene/shader/media conversion or a new Campaign bank.
        fixture.current_mod["files"] = copy.deepcopy(before)
        fixture.write_apk()
        base = apk_update.inspect_base(fixture.apk)
        with patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]), \
             patch.object(driver, "_movies", return_value=SimpleNamespace(plan=lambda *_: {"producerKey": "3" * 64})):
            plan = driver.update_requirements(fixture.builder, ROOT, base, after, fixture.current_mod["modBuild"] + 1)
        self.assertEqual(plan["rebuildReasons"], [])
        self.assertEqual(plan["updateStrategy"], "compact-code")
        self.assertTrue(plan["codeChanged"])
        self.assertTrue(plan["requiresUnity"])
        self.assertFalse(plan["artChanged"])
        self.assertFalse(plan["nativeHelperChanged"])
        self.assertFalse(plan["figureBanksChanged"])
        self.assertFalse(plan["audioBanksChanged"])
        self.assertTrue(plan["movieBanksChanged"])

    def test_metadata_and_checkout_path_changes_do_not_reconvert_game(self):
        runtime = rows("src/GloomhavenVR/Core/Plugin.cs")
        before = runtime + rows("quest-builder-release.json")
        after = runtime + rows("quest-builder-release.json", digest="2")
        self.assertEqual(driver._code_rows(before), driver._code_rows(after))
        self.assertEqual(driver._native_producer_rows(before), driver._native_producer_rows(after))
        self.assertEqual(driver.rebuild_reasons(after, before), [])

    def test_exact_late_movie_staging_preserves_original_producer_identity(self):
        source = (ROOT / "tools/quest-builder/builder.py").read_bytes()
        boundary = b"        audio_bank.stage(project, audio_root, audio_manifest)\n"
        seam = (b'        movie_bank = _local_helper("movie_banks")\n'
                b'        movie_payload = movie_bank.produce(_module_context(), args, source, inputs, output, game)\n'
                b'        movie_bank.stage(project, movie_payload)\n')
        source = source.replace(seam, b"")
        with_movies = source.replace(boundary, boundary + seam)
        self.assertNotEqual(with_movies, source)
        for prefix in (False, True):
            with self.subTest(prefix=prefix):
                self.assertEqual(preparation_identity.builder_producer_digest(source, original_prefix=prefix),
                                 preparation_identity.builder_producer_digest(with_movies, original_prefix=prefix))
        changed = with_movies.replace(b'movie_bank.stage(project, movie_payload)', b'movie_bank.stage(project, inputs)')
        self.assertNotEqual(preparation_identity.builder_producer_digest(source),
                            preparation_identity.builder_producer_digest(changed))
        earlier = with_movies.replace(
            b"    tools = toolchain(args, output)", b"    tools = toolchain(args, output)\n    movie_bank.touch_original_game()", 1)
        self.assertNotEqual(preparation_identity.builder_producer_digest(source),
                            preparation_identity.builder_producer_digest(earlier))

    def test_movie_selector_stays_in_complete_native_recipe(self):
        import tempfile
        source = (ROOT / "tools/quest-builder/update_driver.py").read_text()
        # Changing a selector can admit new compiler sources; its complete body
        # belongs in the producer recipe even though movie publication does not.
        changed = source.replace('return name.startswith("src/GloomhavenVR/Assets/Movies/") and name.endswith((".mov", ".provenance.json"))',
                                 'return False')
        self.assertNotEqual(source, changed)
        builder = SimpleNamespace(apk_updates=apk_update, value_hash=lambda value: repr(value), BuildError=RuntimeError)
        with tempfile.TemporaryDirectory() as folder:
            a = Path(folder) / "a.py"
            a.write_text(source)
            before = native_update_resume.compiler_recipe(builder, a)
            # Match filenames so the witness differs solely in executable AST.
            a.write_text(changed)
            after = native_update_resume.compiler_recipe(builder, a)
        self.assertNotEqual(before, after)

    def test_movie_only_inspect_and_update_retain_native_pair_and_campaign(self):
        fixture = fixtures.UpdateDriverTests("runTest")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        runtime = "src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs"
        media = "src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.sbs-v2.mov"
        previous = rows(runtime, media)
        current = rows(runtime) + rows(media, digest="2")
        fixture.current_mod["files"] = previous
        fixture.write_apk()
        fixture.args.command = "update-mod"
        fixture.builder.source_inventory = lambda _: (current, "8" * 40, False)
        fixture.builder.mod_build = lambda _: fixture.current_mod["modBuild"]
        fixture.builder.snapshot = Mock(side_effect=AssertionError("No compiler or recipe snapshot for separate media"))
        part = fixture.root / "intro.mov"
        part.write_bytes(b"current authored intro" * 10000)
        member = apk_update.MOVIE_PREFIX + "CP_Intro/GH_CP_Intro.sbs-v2.mov"
        metadata = {"schema": 1, "target": "Android", "producerKey": "3" * 64,
                    "files": [storage.record_file(part, "CP_Intro/GH_CP_Intro.sbs-v2.mov")]}
        helper = SimpleNamespace(plan=lambda *_: {"producerKey": "3" * 64},
                                 delivery=Mock(return_value={apk_update.MOVIES: storage.canonical(metadata), member: part}))
        with patch.object(driver, "_movies", return_value=helper), \
             patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_compile_code", side_effect=AssertionError("Media changes retain native code and metadata")), \
             patch.object(driver, "_game_subset", side_effect=AssertionError("Never reconvert Campaign for a movie")), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML qualified separately"):
            plan = driver.inspect(fixture.builder, fixture.args, ROOT, fixture.output)
            self.assertFalse(plan["codeChanged"])
            self.assertFalse(plan["artChanged"])
            self.assertTrue(plan["movieBanksChanged"])
            self.assertEqual(plan["rebuildReasons"], [])
            updated = driver.run(fixture.builder, fixture.args, ROOT, fixture.output)
        helper.delivery.assert_called_once()
        self.assertEqual(helper.delivery.call_args.args[1], ROOT)
        self.assertEqual(helper.delivery.call_args.args[4], fixture.game)
        self.assertIs(helper.delivery.call_args.kwargs["args"], fixture.args)
        fixture.builder.snapshot.assert_not_called()
        with zipfile.ZipFile(fixture.apk) as before, zipfile.ZipFile(updated) as after:
            for name in (apk_update.LIBRARY, apk_update.METADATA, "assets/fixture.resource"):
                self.assertEqual(before.read(name), after.read(name))
            self.assertEqual(after.read(member), part.read_bytes())
            self.assertEqual(json.loads(after.read(apk_update.MOVIES)), metadata)

    def test_current_movie_key_reuses_media_without_owning_a_compiler(self):
        fixture = fixtures.UpdateDriverTests("runTest")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        current = rows("src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs")
        fixture.current_mod["files"] = current
        fixture.write_apk()
        base = apk_update.inspect_base(fixture.apk)
        base["movieBanks"] = {"producerKey": "3" * 64}
        helper = SimpleNamespace(plan=lambda *_: {"producerKey": "3" * 64},
                                 delivery=Mock(side_effect=AssertionError("Current media needs no producer")))
        with patch.object(driver, "_movies", return_value=helper), \
             patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]):
            plan = driver.update_requirements(fixture.builder, ROOT, base, current, fixture.current_mod["modBuild"])
        self.assertFalse(plan["movieBanksChanged"])
        self.assertFalse(plan["requiresUnity"])
        self.assertFalse(plan["codeChanged"])
        helper.delivery.assert_not_called()

    def test_completed_preparation_scope_keeps_b693_producers_across_rebase(self):
        import tempfile
        seam = (b'        movie_bank = _local_helper("movie_banks")\n'
                b'        movie_payload = movie_bank.produce(_module_context(), args, source, inputs, output, game)\n'
                b'        movie_bank.stage(project, movie_payload)\n')
        original = (ROOT / preparation_identity.BUILDER).read_bytes().replace(seam, b"")
        boundary = b"        audio_bank.stage(project, audio_root, audio_manifest)\n"
        updated = original.replace(boundary, boundary + seam)
        current = [copy.deepcopy(pair[0]) for pair in preparation_identity.REBASE_MEDIA_DELIVERY_ROWS.values()]
        previous = [copy.deepcopy(pair[1]) for pair in preparation_identity.REBASE_MEDIA_DELIVERY_ROWS.values()]
        for row in current:
            actual = storage.record_file(ROOT / row["path"], row["path"])
            for aliases in (preparation_identity.SKY_INSTALLED_UPDATE_DELIVERY_ROWS,
                            preparation_identity.INSTALLED_UPDATE_DELIVERY_ROWS,
                            preparation_identity.INSTALLED_UPDATE_DELIVERY_PREVIOUS_ROWS):
                observed, preceding = aliases.get(row["path"], (None, actual))
                if actual == observed: actual = preceding
            self.assertEqual(actual, row,
                             "The finite reviewed byte alias must be refreshed only after reviewing the changed host.")
        independent = rows("tools/quest-builder/movie_banks.py", "tools/quest-builder/movie_render.py",
                           "tools/quest-builder/requirements-movies.txt", "tools/quest-movies/films/one.depth.gz")
        with tempfile.TemporaryDirectory() as folder:
            scopes = []
            for name, body, files, build in (("previous", original, previous, 693),
                                              ("current", updated, current + independent, 709)):
                source = Path(folder) / name
                path = source / preparation_identity.BUILDER
                path.parent.mkdir(parents=True)
                path.write_bytes(body)
                inputs = {"target": "game", "inputKey": name, "game": {"key": "owned-game"},
                          "profile": {"displayName": "retained owner"},
                          "mod": {"key": name, "modBuild": build,
                                  "files": files + [storage.record_file(path, preparation_identity.BUILDER)]}}
                scope = preparation_identity._scope(inputs, source, recovery_resume, original_prefix=True, completed=True)
                scopes.append(scope)
            self.assertEqual(storage.value_hash(scopes[0]), storage.value_hash(scopes[1]))
            inputs["mod"]["files"][0]["sha256"] = "0" * 64
            changed = preparation_identity._scope(inputs, source, recovery_resume, original_prefix=True, completed=True)
            self.assertNotEqual(storage.value_hash(scopes[0]), storage.value_hash(changed))

    def test_reviewed_host_media_delivery_does_not_force_an_unchanged_native_pair(self):
        current = [copy.deepcopy(pair[0]) for pair in preparation_identity.REBASE_MEDIA_DELIVERY_ROWS.values()]
        previous = [copy.deepcopy(pair[1]) for pair in preparation_identity.REBASE_MEDIA_DELIVERY_ROWS.values()]
        self.assertEqual(driver._code_rows(previous, normalizer=preparation_identity),
                         driver._code_rows(current, normalizer=preparation_identity))
        current[0]["sha256"] = "0" * 64
        self.assertNotEqual(driver._code_rows(previous, normalizer=preparation_identity),
                            driver._code_rows(current, normalizer=preparation_identity))

    def shader_fixture(self, *, bank=b"UnityFS\0current Android shader", include_bank=True, shader_key="3" * 64):
        fixture = fixtures.UpdateDriverTests("runTest")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        current = rows("src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs")
        fixture.current_mod["files"] = current
        member = apk_update.MOVIE_PREFIX + "ghvr-quest-movies.bundle"
        import hashlib
        metadata = {"schema": 1, "target": "Android", "producerKey": "4" * 64, "shaderKey": shader_key,
                    "files": [{"path": "ghvr-quest-movies.bundle", "size": len(bank), "sha256": hashlib.sha256(bank).hexdigest()}]}
        extra = {apk_update.MOVIES: storage.canonical(metadata)}
        if include_bank: extra[member] = bank
        fixture.write_apk(extra=extra)
        return fixture, apk_update.inspect_base(fixture.apk), current

    def test_movie_only_update_uses_verified_small_apk_shader_without_unity(self):
        fixture, base, current = self.shader_fixture()
        helper = SimpleNamespace(plan=lambda *_: {"producerKey": "5" * 64, "shaderKey": "3" * 64})
        with patch.object(driver, "_movies", return_value=helper), \
             patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]):
            plan = driver.update_requirements(fixture.builder, ROOT, base, current, fixture.current_mod["modBuild"])
            self.assertTrue(plan["movieBanksChanged"])
            self.assertFalse(plan["codeChanged"])
            self.assertFalse(plan["requiresUnity"])
            # A repeated requirements probe uses the exact APK/bank witness.
            with patch.object(zipfile.ZipFile, "open", side_effect=AssertionError("Never rehash unchanged bank/films")):
                self.assertFalse(driver.update_requirements(fixture.builder, ROOT, base, current,
                                                           fixture.current_mod["modBuild"])["requiresUnity"])

    def test_missing_damaged_or_changed_shader_requires_only_its_own_compiler(self):
        variants = ({"include_bank": False}, {"bank": b"broken native bank"}, {"shader_key": "0" * 64})
        for variant in variants:
            fixture, base, current = self.shader_fixture(**variant)
            helper = SimpleNamespace(plan=lambda *_: {"producerKey": "5" * 64, "shaderKey": "3" * 64})
            with self.subTest(variant=variant), patch.object(driver, "_movies", return_value=helper), \
                 patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]):
                plan = driver.update_requirements(fixture.builder, ROOT, base, current, fixture.current_mod["modBuild"])
            self.assertEqual(plan["rebuildReasons"], [])
            self.assertTrue(plan["movieBanksChanged"])
            self.assertTrue(plan["requiresUnity"])
            self.assertFalse(plan["codeChanged"])

    def test_closed_shader_cache_skips_unity_when_apk_lacks_bank(self):
        fixture, base, current = self.shader_fixture(include_bank=False)
        helper = SimpleNamespace(plan=lambda *_: {"producerKey": "5" * 64, "shaderKey": "3" * 64},
                                 shader_cached=Mock(return_value=True))
        with patch.object(driver, "_movies", return_value=helper), \
             patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]):
            plan = driver.update_requirements(fixture.builder, ROOT, base, current, fixture.current_mod["modBuild"],
                                              output=fixture.output)
        self.assertFalse(plan["requiresUnity"])
        self.assertFalse(plan["codeChanged"])
        helper.shader_cached.assert_called_once_with(fixture.output, "3" * 64)


if __name__ == "__main__":
    unittest.main()
