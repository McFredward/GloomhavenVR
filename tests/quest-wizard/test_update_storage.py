"""Published update history can shrink while exact compiler inputs remain reusable."""
import hashlib
import json
from pathlib import Path
import sys
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import state
import update_storage
import test_cleanup as fixtures


class UpdateStorageTests(unittest.TestCase):
    setUp = fixtures.CleanupTests.setUp
    file = fixtures.CleanupTests.file

    def update(self, number, *, latest=False):
        # Match update_driver.inspect's actual key: only local source paths are
        # omitted. A renamed/copy-pasted receipt cannot qualify another APK.
        plan = {"schema": 1, "mode": "update-mod", "baseApk": str(self.temp / "external.apk"),
                "baseApkSha256": str(number % 10) * 64, "gameInputKey": "a" * 64,
                "inputs": {"mod": {"modBuild": 690 + number}}, "sourcePackage": None}
        key = state.value_hash({name: value for name, value in plan.items() if name not in ("baseApk", "sourcePackage")})
        plan["updateKey"] = key
        folder = self.build / "updates" / key
        state.atomic_json(folder / "plan.json", plan)
        apk = self.file("updates/" + key + "/GloomhavenVR-Quest.apk", ("signed published APK " + str(number)).encode())
        details = {"updateKey": key, "inputKey": "a" * 64, "isDiagnostic": False,
                   "apkSha256": apk["sha256"], "contentFiles": [], "retainedGameContent": True}
        report = {"schema": 1, "buildResult": "Succeeded", "scope": "APK update", **details}
        state.atomic_json(self.build / (apk["path"] + ".build.json"), report)
        raw = (self.build / (apk["path"] + ".build.json")).read_bytes()
        row = {"path": apk["path"] + ".build.json", "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}
        receipt = {"schema": 1, "stage": "build", "key": key, "outputs": [apk, row], "details": details}
        receipt_name = "receipts/build/" + key + ".json"
        state.atomic_json(self.build / receipt_name, receipt)
        if latest:
            state.atomic_json(self.build / "latest-build.json", {"schema": 1, "apk": apk["path"],
                              "receipt": receipt_name, "details": details})
        return self.build / apk["path"], plan

    def test_only_obsolete_final_apks_are_pruned_and_all_fast_inputs_survive(self):
        old, plan = self.update(1); current, _ = self.update(2, latest=True)
        retained = [self.build / "updates" / plan["updateKey"] / "code-player.apk",
                    self.build / "updates/code-projects" / ("b" * 64) / "Library/Bee/native.o",
                    self.build / "updates/source-inputs" / ("c" * 64) / "src/Plugin.cs",
                    self.build / "updates/game-inputs" / ("d" * 64) / "code/Managed/GH.Runtime.dll",
                    self.build / "builds" / ("e" * 64) / "GloomhavenVR-Quest-content.zip"]
        for path in retained:
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"reusable owned input")
        size = old.stat().st_size
        result = update_storage.maintain(self.root)
        self.assertEqual((result["prunedFiles"], result["prunedBytes"]), (1, size))
        self.assertFalse(old.exists()); self.assertTrue(current.is_file())
        self.assertTrue(old.with_suffix(".apk.build.json").is_file())
        self.assertTrue(all(path.is_file() for path in retained))
        self.assertEqual(result["warnings"], [])
        self.assertEqual(update_storage.maintain(self.root)["prunedFiles"], 0)

    def device_source(self, generation, serial='Quest-device'):
        witness = {'package': 'dev.gloomhavenvr.quest', 'deviceSerial': serial,
                   'versionCode': generation, 'remoteStat': {'size': 20}}
        key = hashlib.sha256(json.dumps(witness, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        apk = self.file('device-inputs/' + key + '/base.apk', b'x' * 20)
        record = self.build / 'device-inputs' / key / 'source.json'
        state.atomic_json(record, {'schema': 1, 'source': 'quest', 'witness': witness})
        import os
        os.utime(record, ns=(generation * 1000000000, generation * 1000000000))
        return self.build / apk['path']

    def test_device_imports_keep_latest_per_headset_and_pending_base_without_payload_reads(self):
        self.update(1, latest=True)
        old = self.device_source(1)
        pending = self.device_source(2)
        latest = self.device_source(3)
        second = self.device_source(1, serial='Second-Quest')
        run = self.store.create({'mode': 'update-mod', 'baseApk': str(pending)})
        run['status'] = 'blocked'; self.store.save(run)
        original = Path.read_bytes
        def no_apk_read(path):
            if path.suffix == '.apk': raise AssertionError('Only metadata belongs in cleanup')
            return original(path)
        with mock.patch.object(Path, 'read_bytes', no_apk_read):
            result = update_storage.maintain(self.root)
        self.assertEqual(result['prunedFiles'], 1)
        self.assertFalse(old.exists())
        self.assertTrue(pending.is_file() and latest.is_file() and second.is_file())

    def test_seed_caches_remain_fast_inputs_and_deleting_them_warns(self):
        self.update(1, latest=True)
        for area in ('cache/update-seeds', 'cache/device-update-seeds'):
            self.file(area + '/' + 'b' * 64 + '/Managed/GH.Runtime.dll', b'reusable')
            impact = update_storage.impact(self.root, [self.build / area], mode='session')
            self.assertEqual(impact['fastUpdateImpact']['status'], 'slower')

    def test_pending_input_or_explicit_keep_prevents_history_deletion(self):
        first, _ = self.update(1); second, _ = self.update(2); self.update(3, latest=True)
        run = self.store.create({"mode": "update-mod", "baseApk": str(first)})
        run["status"] = "blocked"; self.store.save(run)
        result = update_storage.maintain(self.root, keep_paths=[str(second)])
        self.assertEqual(result["prunedFiles"], 0)
        self.assertTrue(first.is_file() and second.is_file())
        run["status"] = "complete"; self.store.save(run)
        self.assertEqual(update_storage.maintain(self.root)["prunedFiles"], 2)

    def test_history_preview_is_explicit_and_retains_fast_cache_with_no_payload_reads(self):
        old, _ = self.update(1); current, _ = self.update(2, latest=True)
        original = Path.read_bytes
        def metadata_only(path):
            if path in (old, current): raise AssertionError("Cleanup must not read/hash signed APK payloads")
            return original(path)
        with mock.patch.object(Path, "read_bytes", metadata_only):
            preview = cleanup.plan(self.root, "update-history")
            self.assertEqual(preview["fastUpdateImpact"]["status"], "preserved")
            self.assertEqual(preview["warnings"], [])
            self.assertTrue(old.exists())
            result = cleanup.execute(self.root, preview)
        self.assertEqual(result["deletedFiles"], 1)
        self.assertFalse(old.exists()); self.assertTrue(current.is_file())

    def test_changed_plan_unclosed_output_or_external_input_is_never_deleted(self):
        changed, _ = self.update(1); partial, _ = self.update(2); self.update(3, latest=True)
        path = changed.parent / "plan.json"; value = json.loads(path.read_text()); value["inputs"]["mod"]["modBuild"] = 999
        state.atomic_json(path, value)
        (self.build / "receipts/build" / (partial.parent.name + ".json")).unlink()
        external = self.temp / "external.apk"; external.write_bytes(b"selected user APK")
        result = update_storage.maintain(self.root)
        self.assertEqual(result["prunedFiles"], 0)
        self.assertTrue(changed.exists() and partial.exists() and external.exists())

    def test_keep_pin_and_borrowed_hardlink_are_not_removed(self):
        pinned, _ = self.update(1); linked, _ = self.update(2); self.update(3, latest=True)
        (pinned.parent / ".keep").write_text("")
        import os
        os.link(linked, self.temp / "borrowed.apk")
        self.assertEqual(update_storage.maintain(self.root)["prunedFiles"], 0)
        self.assertTrue(pinned.is_file() and linked.is_file())

    def test_new_pending_run_after_preview_invalidates_confirmation(self):
        old, _ = self.update(1); self.update(2, latest=True)
        self.store.create({"mode": "update-mod", "baseApk": str(old), "status": "blocked"})
        # Current pending run protects its input; retire it first to preview.
        for folder in (self.root / "sessions").iterdir():
            value = self.store.load(folder.name); value["status"] = "complete"; self.store.save(value)
        preview = cleanup.plan(self.root, "update-history")
        self.store.create({"mode": "update-mod", "baseApk": str(old)})
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(old.exists())

    def test_cache_removal_warns_before_confirmation_and_run_removal_does_not(self):
        self.update(1, latest=True)
        self.file("cache/derived/owned.bin", b"reusable")
        preview = cleanup.plan(self.root, "build-cache")
        self.assertEqual(preview["fastUpdateImpact"]["status"], "slower")
        self.assertEqual(preview["warnings"][0]["code"], "fast_update_cache_removed")
        run = self.store.create({"mode": "update-mod"}); run["status"] = "complete"; self.store.save(run)
        preview = cleanup.plan_session(self.root, run["session"])
        self.assertEqual(preview["fastUpdateImpact"]["status"], "preserved")
        cleanup.execute(self.root, preview)
        self.assertTrue((self.build / "cache/derived/owned.bin").exists())

    def test_native_cache_is_categorized_as_reusable_instead_of_delivered_apk(self):
        self.update(1, latest=True)
        self.file("updates/code-projects/" + "b" * 64 + "/Library/Bee/native.o", b"x" * 1024)
        self.file("updates/game-inputs/" + "c" * 64 + "/code/Managed/GH.Runtime.dll", b"x" * 512)
        inventory = cleanup.inventory(self.root)
        rows = {row["id"]: row for row in inventory["categories"]}
        self.assertEqual(rows["unity-caches"]["bytes"], 1024)
        self.assertEqual(rows["inputs"]["bytes"], 512)
        self.assertEqual(inventory["fastUpdateBytes"], 1536)
        self.assertTrue(inventory["fastUpdatePolicy"]["retainedOnRunRemoval"])

    def test_independent_asset_caches_are_counted_and_warn_before_removal(self):
        self.update(1, latest=True)
        areas = ('cache/quest-movies', 'cache/quest-figure-meshes', 'cache/quest-native-audio')
        for area in areas:
            self.file(area + '/' + 'b' * 64 + '/owned-result.bin', b'x' * 1024)
        inventory = cleanup.inventory(self.root)
        self.assertEqual(inventory['fastUpdateBytes'], 3 * 1024)
        for area in areas:
            impact = update_storage.impact(self.root, [self.build / area], mode='session')
            self.assertEqual(impact['fastUpdateImpact']['status'], 'slower')
            self.assertEqual(impact['warnings'][0]['code'], 'fast_update_cache_removed')
        run = self.store.create({'mode': 'update-mod'})
        run['status'] = 'complete'; self.store.save(run)
        cleanup.execute(self.root, cleanup.plan_session(self.root, run['session']))
        self.assertTrue(all((self.build / area / ('b' * 64) / 'owned-result.bin').is_file() for area in areas))

    def test_retained_compiler_owner_attributes_one_shared_project_to_matching_runs(self):
        _, plan = self.update(1, latest=True)
        project = self.build / "updates/code-projects" / ("b" * 64)
        state.atomic_json(project / "QuestCodeUpdate/code-project-owner.json", {
            "state": "ready", "owner": {"codeKey": plan["updateKey"], "gameInputKey": plan["gameInputKey"]}})
        self.file(project.relative_to(self.build).as_posix() + "/Library/Bee/native.o", b"native" * 100)
        for _ in range(2):
            run = self.store.create({"mode": "update-mod"})
            run["status"] = "complete"
            next(row for row in run["stages"] if row["id"] == "build")["details"] = {
                "inputKey": plan["gameInputKey"], "updateKey": plan["updateKey"]}
            self.store.save(run)
        inventory = cleanup.inventory(self.root)
        self.assertEqual(len(inventory["runs"]), 2)
        self.assertGreaterEqual(inventory["sharedBytes"], 600)
        self.assertTrue(all(row["sharedBytes"] >= 600 for row in inventory["runs"]))

    def test_engine_lock_can_be_retained_without_recursive_run_lock_acquisition(self):
        self.update(1); self.update(2, latest=True)
        with state.file_lock(self.root / "run.lock"):
            result = update_storage.maintain(self.root, run_lock_held=True)
        self.assertEqual(result["prunedFiles"], 1)

    def test_optional_maintenance_failure_does_not_fail_an_already_completed_update(self):
        old, _ = self.update(1); current, _ = self.update(2, latest=True)
        with mock.patch.object(cleanup, "_guard", side_effect=cleanup._helper().BuildError("busy")):
            result = update_storage.maintain(self.root)
        self.assertEqual(result["prunedFiles"], 0)
        self.assertTrue(old.is_file() and current.is_file())
        self.assertEqual(result["warnings"][0]["code"], "update_history_retained")

    def menu_cache(self, area, generation):
        if area == 'default-sky-resources':
            return self.sky_cache(generation)
        raw = ('closed reusable menu payload ' + str(generation)).encode()
        key = hashlib.sha256(raw).hexdigest()
        filename = 'menu-depth-android.bundle' if area == 'menu-depth-resources' else 'quest-mod-content.zip'
        recipe = {'scope': 'quest-menu-depth-native-card-v1', 'target': 'Android', 'graphicsApi': 'Vulkan', 'generation': generation}
        if area == 'menu-depth-resources': key = state.value_hash(recipe)
        folder = self.build / 'cache' / area / key
        row = self.file('cache/' + area + '/' + key + '/' + filename, raw)
        if area == 'menu-depth-resources':
            state.atomic_json(folder / 'menu-depth-android.provenance.json', {'schema': 1,
                'producerKey': key, 'recipe': recipe, 'bundle': {**row, 'path': filename}})
        else:
            if area == 'retained-mod-content':
                metadata = {'owner.json': {'schema': 1, 'scope': 'quest-retained-mod-content-v1',
                    'archiveSha256': key, 'files': [{**row, 'path': filename}]}}
                details = {'scope': 'quest-retained-mod-content-v1'}
            else:
                metadata = {'quest-mod-content.json': {'schema': 1, 'inputKey': 'a' * 64,
                    'archive': filename, 'archiveSha256': row['sha256']},
                    'quest-mod-bundles.json': {'menuDepthStory': {'contentKey': 'b' * 64}}}
                details = {'scope': 'quest-loose-menu-story-v1', 'inputKey': 'a' * 64, 'storyKey': 'b' * 64}
            outputs = [row]
            for name, value in metadata.items():
                state.atomic_json(folder / name, value)
                payload = (folder / name).read_bytes()
                outputs.append({'path': (folder / name).relative_to(self.build).as_posix(),
                    'size': len(payload), 'sha256': hashlib.sha256(payload).hexdigest()})
            stage = area
            state.atomic_json(self.build / 'receipts' / stage / (key + '.json'),
                {'schema': 1, 'stage': stage, 'key': key, 'outputs': outputs, 'details': details})
        return folder / filename

    def sky_cache(self, generation):
        raw = ('qualified native Android sky ' + str(generation)).encode()
        recipe = {'schema': 1, 'scope': 'quest-default-sky-resources-v1', 'target': 'Android',
            'graphicsApi': 'Vulkan', 'unityVersion': '2021.3.5f1', 'stereoRenderingPath': 'MultiPass',
            'typeTrees': True, 'sources': [{'path': 'tools/quest-builder/sky_resources.py',
                'size': 1, 'sha256': 'a' * 64}]}
        recipe_key = state.value_hash(recipe)
        basis = {'nativeAssetSha256': hashlib.sha256(raw).hexdigest()}
        producer_key = state.value_hash({'recipeKey': recipe_key, 'basis': basis})
        relative = 'cache/default-sky-resources/' + recipe_key + '/' + producer_key
        folder = self.build / relative
        filename = 'native-default-sky-android.bundle'
        row = self.file(relative + '/' + filename, raw)
        state.atomic_json(folder / 'native-default-sky-android.provenance.json', {
            'schema': 1, 'scope': recipe['scope'], 'recipeKey': recipe_key, 'producerKey': producer_key,
            'recipe': recipe, 'basis': basis, 'bundle': {**row, 'path': filename},
            'nativePlatformAudit': {'platforms': [13]}})
        return folder / filename

    def latest_fast_inputs(self, paths):
        latest = json.loads((self.build / 'latest-build.json').read_text())
        latest['details']['fastUpdateInputs'] = [path.parent.relative_to(self.build).as_posix() for path in paths]
        receipt = json.loads((self.build / latest['receipt']).read_text())
        receipt['details'] = latest['details']
        state.atomic_json(self.build / latest['receipt'], receipt)
        state.atomic_json(self.build / 'latest-build.json', latest)

    def sky_work(self, generation, *, closed=True):
        bank = self.sky_cache(generation)
        producer, recipe = bank.parent.name, bank.parent.parent.name
        folder = self.build / 'work' / ('sky-' + producer[:12])
        state.atomic_json(folder / 'owner.json', {'schema': 1, 'scope': 'quest-default-sky-work-v1',
            'recipeKey': recipe, 'producerKey': producer})
        caches = [folder / 'native-cache/bound', folder / 'unity-1234abcd/Library', folder / 'unity-1234abcd/bundles']
        for path in caches:
            path.mkdir(parents=True); (path / 'regenerable.bin').write_bytes(b'generated native cache')
        evidence = [folder / 'native-cache/original-shader-inventory.json', folder / 'unity-1234abcd/sky-shaders.json',
            folder / 'unity-1234abcd/Assets/Native.shader', folder / 'unity-1234abcd/Logs/Editor.log']
        for path in evidence:
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b'retained source or evidence')
        if not closed: bank.unlink()
        return folder, bank, caches, evidence

    def test_closed_sky_work_reclaims_caches_and_preserves_current_bank_sources_and_logs(self):
        apk, _ = self.update(1, latest=True)
        folder, bank, caches, evidence = self.sky_work(1)
        self.latest_fast_inputs([bank])
        latest = json.loads((self.build / 'latest-build.json').read_text())
        latest['details']['workRelativePath'] = folder.relative_to(self.build).as_posix()
        receipt = json.loads((self.build / latest['receipt']).read_text()); receipt['details'] = latest['details']
        state.atomic_json(self.build / latest['receipt'], receipt)
        state.atomic_json(self.build / 'latest-build.json', latest)
        original = Path.read_bytes
        def metadata_only(path):
            if path.suffix in ('.apk', '.zip', '.bundle'): raise AssertionError('work cleanup reads metadata only')
            return original(path)
        with mock.patch.object(Path, 'read_bytes', metadata_only):
            result = update_storage.maintain(self.root)
        self.assertEqual(result['prunedFiles'], len(caches))
        self.assertTrue(all(not path.exists() for path in caches))
        self.assertTrue(all(path.is_file() for path in evidence))
        self.assertTrue((folder / 'owner.json').is_file() and bank.is_file() and apk.is_file())
        self.assertNotIn(folder.relative_to(self.root).as_posix(), result['retainedFastUpdatePaths'])
        self.assertNotEqual(update_storage.impact(self.root, [folder], mode='session')['fastUpdateImpact']['status'], 'slower')

    def test_incomplete_or_changed_final_sky_pair_keeps_partial_native_work(self):
        self.update(1, latest=True)
        folder, bank, caches, _ = self.sky_work(1, closed=False)
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.assertTrue(all(path.is_dir() for path in caches))
        bank.write_bytes(b'partial')
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.assertTrue(all(path.is_dir() for path in caches))

    def test_sky_work_owner_prefix_and_unknown_contents_never_authorize_cleanup(self):
        self.update(1, latest=True)
        for generation, mode in enumerate(('owner', 'prefix', 'unknown'), 20):
            with self.subTest(mode=mode):
                folder, bank, caches, _ = self.sky_work(generation)
                self.latest_fast_inputs([bank])
                if mode == 'owner':
                    value = json.loads((folder / 'owner.json').read_text()); value['recipeKey'] = 'f' * 64
                    state.atomic_json(folder / 'owner.json', value)
                elif mode == 'prefix':
                    moved = folder.with_name('sky-' + '0' * 12); folder.rename(moved)
                    caches = [moved / path.relative_to(folder) for path in caches]
                else:
                    (folder / 'foreign-source.txt').write_text('unknown collision')
                update_storage.maintain(self.root)
                self.assertTrue(all(path.is_dir() for path in caches))

    def test_sky_work_live_editor_pending_project_and_explicit_keep_are_protected(self):
        self.update(1, latest=True)
        folder, bank, caches, _ = self.sky_work(1)
        self.latest_fast_inputs([bank])
        with mock.patch.object(cleanup, '_editor_busy', return_value=True):
            self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.assertTrue(all(path.is_dir() for path in caches))
        self.assertEqual(update_storage.maintain(self.root, keep_paths=[folder])['prunedFiles'], 0)
        run = self.store.create({'mode': 'update-mod'}); run['status'] = 'blocked'
        next(row for row in run['stages'] if row['id'] == 'inspect')['details'] = {
            'workRelativePath': (folder / 'unity-1234abcd').relative_to(self.build).as_posix()}
        self.store.save(run)
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        run['status'] = 'complete'; self.store.save(run)
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], len(caches))
        self.assertTrue(bank.is_file())

    def test_sky_work_pins_links_and_later_owner_changes_keep_unknown_data(self):
        self.update(1, latest=True)
        folder, bank, caches, _ = self.sky_work(1)
        self.latest_fast_inputs([bank])
        (folder / '.keep').write_text('retained capture')
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        (folder / '.keep').unlink()
        external = self.temp / 'external-source'; external.mkdir()
        (external / 'preserve.txt').write_text('original input')
        (caches[1] / 'external').symlink_to(external, target_is_directory=True)
        update_storage.maintain(self.root)
        self.assertTrue((external / 'preserve.txt').is_file() and caches[1].is_dir())
        (caches[1] / 'external').unlink()
        preview = cleanup.plan(self.root, 'update-history')
        value = json.loads((folder / 'owner.json').read_text()); value['producerKey'] = 'f' * 64
        state.atomic_json(folder / 'owner.json', value)
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(caches[1].is_dir())

    def test_menu_cache_families_are_reusable_inputs_and_run_retirement_keeps_them(self):
        self.update(1, latest=True)
        payloads = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
        inventory = cleanup.inventory(self.root)
        self.assertGreaterEqual(inventory['fastUpdateBytes'], sum(path.stat().st_size for path in payloads))
        for path in payloads:
            impact = update_storage.impact(self.root, [path.parent], mode='session')
            self.assertEqual(impact['fastUpdateImpact']['status'], 'slower')
        run = self.store.create({'mode': 'update-mod'})
        run['status'] = 'complete'; self.store.save(run)
        cleanup.execute(self.root, cleanup.plan_session(self.root, run['session']))
        self.assertTrue(all(path.is_file() for path in payloads))

    def test_superseded_menu_payloads_prune_metadata_only_and_current_pending_evidence_survive(self):
        current_apk, _ = self.update(1, latest=True)
        old = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
        current = [self.menu_cache(area, 2) for area in update_storage.MENU_CACHE_AREAS]
        pending = [self.menu_cache(area, 3) for area in update_storage.MENU_CACHE_AREAS]
        self.latest_fast_inputs(current)
        run = self.store.create({'mode': 'update-mod'})
        run['status'] = 'blocked'
        next(row for row in run['stages'] if row['id'] == 'inspect')['details'] = {
            'fastUpdateInputs': [path.parent.relative_to(self.build).as_posix() for path in pending]}
        self.store.save(run)
        evidence = old[0].parent / 'retained-evidence.log'; evidence.write_text('source and render audit')
        original = Path.read_bytes
        def metadata_only(path):
            if path.suffix in ('.apk', '.zip', '.bundle'): raise AssertionError('cleanup reads metadata only')
            return original(path)
        with mock.patch.object(Path, 'read_bytes', metadata_only):
            result = update_storage.maintain(self.root)
        self.assertEqual(result['prunedFiles'], len(update_storage.MENU_CACHE_AREAS))
        self.assertTrue(all(not path.exists() for path in old))
        self.assertTrue(all(path.is_file() for path in current + pending))
        self.assertTrue(current_apk.is_file() and evidence.is_file())
        self.assertTrue((old[0].parent / 'menu-depth-android.provenance.json').is_file())
        self.assertTrue((old[1].parent / 'quest-mod-content.json').is_file())
        self.assertTrue((old[2].parent / 'owner.json').is_file())
        self.assertTrue((old[3].parent / 'native-default-sky-android.provenance.json').is_file())

    def test_safe_and_history_previews_remove_only_explicitly_superseded_menu_payloads(self):
        for mode in ('safe', 'update-history'):
            with self.subTest(mode=mode):
                self.update(1, latest=True)
                old = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
                current = [self.menu_cache(area, 2) for area in update_storage.MENU_CACHE_AREAS]
                self.latest_fast_inputs(current)
                preview = cleanup.plan(self.root, mode)
                self.assertEqual({row['path'] for row in preview['paths']},
                                 {path.relative_to(self.root).as_posix() for path in old})
                cleanup.execute(self.root, preview)
                self.assertTrue(all(path.is_file() for path in current))
                self.assertTrue(all(not path.exists() for path in old))

    def test_unknown_pending_dependencies_and_legacy_latest_metadata_keep_all_menu_caches(self):
        self.update(1, latest=True)
        old = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
        current = [self.menu_cache(area, 2) for area in update_storage.MENU_CACHE_AREAS]
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.latest_fast_inputs(current)
        self.store.create({'mode': 'update-mod'})
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.assertTrue(all(path.is_file() for path in old + current))

    def test_menu_cache_owners_pins_and_payload_changes_cannot_authorize_guessed_deletion(self):
        self.update(1, latest=True)
        payloads = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
        self.latest_fast_inputs([])
        (payloads[0].parent / '.keep').write_text('')
        payloads[1].write_bytes(b'changed payload size')
        owner = payloads[2].parent / 'owner.json'
        value = json.loads(owner.read_text()); value['archiveSha256'] = 'f' * 64
        state.atomic_json(owner, value)
        sky_owner = payloads[3].parent / 'native-default-sky-android.provenance.json'
        sky_value = json.loads(sky_owner.read_text()); sky_value['basis']['nativeAssetSha256'] = 'f' * 64
        state.atomic_json(sky_owner, sky_value)
        unknown = self.file('cache/retained-mod-content/' + 'd' * 64 + '/quest-mod-content.zip', b'unproven')
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.assertTrue(all(path.is_file() for path in payloads))
        self.assertTrue((self.build / unknown['path']).is_file())

    def test_sky_cache_ownership_requires_both_closed_keys_and_android_recipe(self):
        self.update(1, latest=True)
        self.latest_fast_inputs([])
        mutations = {
            'schema': lambda value: value.update(schema=2),
            'scope': lambda value: value.update(scope='unknown'),
            'recipe_key': lambda value: value.update(recipeKey='f' * 64),
            'producer_key': lambda value: value.update(producerKey='f' * 64),
            'recipe_source': lambda value: value['recipe']['sources'][0].update(sha256='f' * 64),
            'basis': lambda value: value['basis'].update(nativeAssetSha256='f' * 64),
            'platform': lambda value: value['recipe'].update(graphicsApi='Direct3D11'),
            'bundle_path': lambda value: value['bundle'].update(path='other.bundle'),
        }
        for generation, (name, mutate) in enumerate(mutations.items(), 10):
            with self.subTest(name=name):
                payload = self.sky_cache(generation)
                path = payload.parent / 'native-default-sky-android.provenance.json'
                value = json.loads(path.read_text()); mutate(value); state.atomic_json(path, value)
                self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
                self.assertTrue(payload.is_file())

    def test_sky_recipe_pin_and_unknown_native_basis_survive_cleanup(self):
        self.update(1, latest=True)
        self.latest_fast_inputs([])
        payload = self.sky_cache(1)
        (payload.parent.parent / '.keep').write_text('pinned native shader evidence')
        unknown = self.file('cache/default-sky-resources/' + 'b' * 64 + '/' + 'c' * 64
                            + '/native-default-sky-android.bundle', b'unproven native bank')
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.assertTrue(payload.is_file() and (self.build / unknown['path']).is_file())

    def test_malformed_sky_dependency_keeps_all_four_cache_families(self):
        self.update(1, latest=True)
        payloads = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
        malformed = self.build / 'cache/default-sky-resources' / ('b' * 64) / 'unknown'
        self.latest_fast_inputs([malformed / 'native-default-sky-android.bundle'])
        self.assertEqual(update_storage.maintain(self.root)['prunedFiles'], 0)
        self.assertTrue(all(path.is_file() for path in payloads))

    def test_current_menu_dependency_change_invalidates_retained_cleanup_preview(self):
        self.update(1, latest=True)
        old = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
        current = [self.menu_cache(area, 2) for area in update_storage.MENU_CACHE_AREAS]
        self.latest_fast_inputs(current)
        preview = cleanup.plan(self.root, 'update-history')
        self.latest_fast_inputs(old)
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(all(path.is_file() for path in old + current))

    def test_missing_or_corrupt_dependency_publishers_keep_menu_cache_and_other_safe_cleanup_available(self):
        payloads = [self.menu_cache(area, 1) for area in update_storage.MENU_CACHE_AREAS]
        self.assertFalse(cleanup.plan(self.root, 'safe')['eligible'])
        self.assertTrue(all(path.is_file() for path in payloads))
        self.update(1, latest=True)
        self.latest_fast_inputs([])
        run = self.store.create({'mode': 'update-mod'})
        (self.root / 'sessions' / run['session'] / 'state.json').write_text('interrupted metadata write')
        self.assertFalse(cleanup.plan(self.root, 'safe')['eligible'])
        self.assertTrue(all(path.is_file() for path in payloads))


if __name__ == "__main__": unittest.main()
