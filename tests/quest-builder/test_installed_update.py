"""Installed-APK update orchestration, with actual ZIP/seed/receipt boundaries.

Native compilation, signing and binary Unity Resource/AXML writers are separate
external boundaries; this fixture does not pretend to execute an Android app.
"""
from contextlib import ExitStack
import copy
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_update_driver as fixtures
from test_update_driver import driver, apk_update, storage
import update_seed
import preparation_identity


class InstalledUpdateTests(unittest.TestCase):
    def setUp(self):
        self.f = fixtures.UpdateDriverTests('runTest')
        self.f.setUp()
        self.addCleanup(self.f.doCleanups)

    def seeded(self):
        f = self.f
        replacements = update_seed.build(f.builder, f.game, f.manifest, f.output)
        with zipfile.ZipFile(f.apk) as archive:
            original = {name: archive.read(name) for name in archive.namelist()}
        for name, value in replacements.items(): original[name] = value.read_bytes() if isinstance(value, Path) else value
        with zipfile.ZipFile(f.apk, 'w') as archive:
            for name, value in original.items(): archive.writestr(name, value)
        shutil.rmtree(f.game)
        # No previously extracted/compiled PC intermediate is available.
        shutil.rmtree(f.output)
        f.args.game_root = None
        f.args.installed_game = True
        f.builder.game_data = Mock(side_effect=AssertionError('No PC installation may be queried'))
        f.builder.selected_profile = Mock(side_effect=AssertionError('No provider profile query'))
        f.builder.dlcs.capture = Mock(side_effect=AssertionError('No store entitlement query'))
        return original

    def pipeline(self):
        stack = ExitStack()
        stack.enter_context(patch.object(driver, 'signing_tools', return_value={}))
        stack.enter_context(patch.object(driver, '_signers', return_value=(lambda _: None, lambda _: None)))
        stack.enter_context(patch.object(apk_update, 'patch_resource_json', return_value={}))
        stack.enter_context(patch.object(apk_update, 'update_android_versions', return_value=b'AXML external boundary'))
        return stack

    def test_pc_assisted_profile_update_embeds_seed_without_unity_then_pc_can_be_removed(self):
        f = self.f
        with self.pipeline(), patch.object(driver, '_compile_code', side_effect=AssertionError('No compiler for seed migration')):
            updated = driver.run(f.builder, f.args, ROOT, f.output)
        base = apk_update.inspect_base(updated)
        self.assertTrue(update_seed.inspect_seed(updated, base)['available'])
        shutil.rmtree(f.game)
        shutil.rmtree(f.output / 'cache')
        base, data, proof = driver.preflight(f.builder, None, updated, 'update-mod', output=f.output, installed_game=True)
        self.assertEqual((data / 'Managed/GH.Runtime.dll').read_bytes(), b'owned input GH.Runtime.dll')
        self.assertTrue(proof['installedGameAccepted'])
        self.assertFalse(proof['ownedGameAssembliesQualified'])

    def test_installed_profile_update_uses_no_pc_provider_or_seed_extraction(self):
        self.seeded(); f = self.f
        f.args.profile_json = None
        with self.pipeline(), patch.object(driver, '_seed', return_value=update_seed), \
             patch.object(update_seed, 'extract', side_effect=AssertionError('Profile edits need no Mono compiler')):
            result = driver.run(f.builder, f.args, ROOT, f.output)
        after = apk_update.inspect_base(result)
        self.assertEqual(after['currentProfile'], f.manifest['profile'])
        self.assertTrue(after['dynamicProfile'])
        self.assertTrue(update_seed.inspect_seed(result, after)['available'])

    def test_installed_mod_update_retains_original_payload_and_all_seed_bytes(self):
        before = self.seeded(); f = self.f
        f.args.command = 'update-mod'
        f.builder.source_inventory = lambda repo: (f.current_mod['files'], '8' * 40, False)
        f.builder.mod_build = lambda repo: f.current_mod['modBuild']
        requirements = {'rebuildReasons': [], 'codeChanged': False, 'artChanged': False, 'nativeHelperChanged': False,
                        'figureBanksChanged': False, 'audioBanksChanged': False, 'movieBanksChanged': False, 'requiresUnity': False}
        with self.pipeline(), patch.object(driver, 'update_requirements', return_value=requirements), \
             patch.object(driver, '_game_subset', side_effect=AssertionError('No original snapshot')), \
             patch.object(driver, 'card_art_delivery', side_effect=AssertionError('No original UI extraction')):
            result = driver.run(f.builder, f.args, ROOT, f.output)
        with zipfile.ZipFile(result) as after:
            for name in (apk_update.LIBRARY, apk_update.METADATA, 'assets/fixture.resource', update_seed.MEMBER):
                self.assertEqual(after.read(name), before[name])

    def test_legacy_mod_update_requires_one_time_pc_assist_without_starting_compile(self):
        f = self.f
        with self.assertRaisesRegex(storage.BuildError, 'one PC-assisted'):
            driver.preflight(f.builder, None, f.apk, 'update-mod', output=f.output, installed_game=True)
        self.assertFalse((f.output / 'updates').exists())

    def test_legacy_profile_edit_needs_neither_pc_copy_nor_seed(self):
        f = self.f
        shutil.rmtree(f.game)
        base, data, proof = driver.preflight(f.builder, None, f.apk, 'update-profile', output=f.output, installed_game=True)
        self.assertIsNone(data)
        self.assertTrue(proof['installedGameAccepted'])

    def test_pc_path_still_requires_matching_original_files(self):
        f = self.f
        (f.game / 'Managed/GH.Runtime.dll').write_bytes(b'wrong source')
        with self.assertRaisesRegex(storage.BuildError, 'does not match'):
            driver.preflight(f.builder, f.game, f.apk, 'update-profile', output=f.output)

    def test_seed_progress_reports_completed_files_and_plan_is_stable_when_reused(self):
        self.seeded(); f = self.f
        calls = []
        _, data, first = driver.preflight(f.builder, None, f.apk, 'update-mod', output=f.output,
                                         installed_game=True, progress=calls.append)
        _, again, second = driver.preflight(f.builder, None, f.apk, 'update-mod', output=f.output, installed_game=True)
        self.assertEqual(data, again)
        self.assertEqual(first, second)
        self.assertEqual([row['done'] for row in calls], [1, 2])
        self.assertEqual([row['total'] for row in calls], [2, 2])

    def test_new_original_derived_asset_producer_requests_pc_instead_of_rebuilding(self):
        self.seeded(); f = self.f
        f.args.command = 'update-mod'
        f.builder.source_inventory = lambda repo: (f.current_mod['files'], '8' * 40, False)
        f.builder.mod_build = lambda repo: f.current_mod['modBuild']
        for flag in ('artChanged', 'figureBanksChanged', 'audioBanksChanged', 'movieBanksChanged'):
            requirements = {'rebuildReasons': [], 'codeChanged': False, 'artChanged': False,
                            'nativeHelperChanged': False, 'figureBanksChanged': False, 'audioBanksChanged': False,
                            'movieBanksChanged': False, 'requiresUnity': False, flag: True}
            with self.subTest(flag=flag), patch.object(driver, 'update_requirements', return_value=requirements), \
                 self.assertRaisesRegex(storage.BuildError, 'matching PC installation'):
                driver.inspect(f.builder, f.args, ROOT, f.output)

    def test_host_only_builder_change_preserves_asset_producer_but_unknown_drift_does_not(self):
        raw = (ROOT / 'tools/quest-builder/builder.py').read_bytes()
        self.assertEqual(preparation_identity.builder_producer_digest(raw), driver._LEGACY_BUILDER_AST)
        changed = raw.replace(b'--installed-game', b'--unreviewed-installed-game', 1)
        self.assertNotEqual(preparation_identity.builder_producer_digest(changed), driver._LEGACY_BUILDER_AST)

    def test_exact_host_only_update_rows_do_not_force_il2cpp_but_unknown_change_does(self):
        paths = ('tools/quest-builder/update_driver.py', 'tools/quest-builder/apk_update.py')
        old = []
        for path in paths:
            data = subprocess.check_output(['git', '-C', str(ROOT), 'show', '6f5c94ace:' + path])
            old.append({'path': path, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
        current = [storage.record_file(ROOT / path, path) for path in paths]
        normalize = lambda rows: driver._code_rows(rows, normalizer=preparation_identity)
        self.assertEqual(normalize(current), normalize(old))
        current[0]['sha256'] = '0' * 64
        self.assertNotEqual(normalize(current), normalize(old))


if __name__ == '__main__': unittest.main()
