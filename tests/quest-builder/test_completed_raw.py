"""Completed exports skip orchestration; consumed bytes still qualify at staging."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import recovery_resume
import storage
import full_assets
import builder


class CompletedRawTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / 'output/inputs/game' / ('1' * 64)
        self.workspace = self.root / 'output/cache/full-original-recovery' / ('2' * 64)
        self.raw = self.workspace / 'RecoveredProject'
        self.core = self.workspace / 'CoreExport/ExportedProject'
        self.file(self.game / 'Managed/GH.Runtime.dll', b'owned managed original')
        self.file(self.game / 'StreamingAssets/aa/catalog.json', b'owned catalog')
        self.file(self.game / 'original.bundle', b'owned original bundle')
        self.file(self.core / 'Assets/core.asset', b'actual core fixture')
        self.asset = self.raw / 'Assets/native.asset'
        self.file(self.asset, b'actual retained raw asset')
        self.managed = self.raw / 'QuestRecovery/managed-types.json'
        self.file(self.managed, b'{"originalTypes":[]}')
        game_files = storage.inventory(self.game)
        source_files = [{'path': row['path'], 'bytes': row['size'], 'sha256': row['sha256']} for row in game_files]
        fingerprint = hashlib.sha256(json.dumps(source_files, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        identity = self.workspace / 'core-identities.jsonl'
        self.file(identity, b'{"collection":"original-core"}\n')
        self.inputs = {'game': {'files': game_files, 'key': storage.value_hash({'files': game_files})},
                       'mod': {'files': [{'path': 'tools/quest-recovery/QuestExportIdentity.cs',
                                         'size': 1, 'sha256': 'a' * 64}]}}
        storage.write_json(self.workspace / 'original-source.json', {
            'schema': 1, 'sourceInventory': source_files, 'sourceFingerprint': fingerprint})
        storage.write_json(self.workspace / 'core-recovery.json', {
            'schema': 1, 'sourceFingerprint': fingerprint,
            'exporterSource': {'instrumentationSha256': 'a' * 64},
            'identitiesSha256': storage.digest(identity), 'files': self.records(self.core)})
        original = next(row for row in source_files if row['path'] == 'original.bundle')
        group = {'bytes': original['bytes'], 'bundles': [original]}
        self.plan = {'catalogSha256': storage.digest(self.game / 'StreamingAssets/aa/catalog.json'), 'groups': [group]}
        storage.write_json(self.workspace / 'BundleRecovery/bundle-plan.json', self.plan)
        storage.write_json(self.workspace / 'original-cab-bundles.json', {'CAB-original': 'original.bundle'})
        self.checkpoint = {'schema': 1, 'sourceFingerprint': fingerprint, 'sourceInventory': source_files,
            'coreIdentitySha256': storage.digest(identity), 'catalogSha256': self.plan['catalogSha256'],
            'completedGroups': [0], 'groups': [{'index': 0, 'input': group}], 'assetsRecovered': True,
            'assetReferences': {'missingGuidCount': 0, 'duplicateGuidCount': 0}, 'files': self.records(self.raw)}
        self.checkpoint_path = self.raw / 'quest-full-recovery-progress.json'
        storage.write_json(self.checkpoint_path, self.checkpoint)
        self.result = {'schema': 1, 'fullOriginalCatalogRecovered': True,
                      'sourceFingerprint': fingerprint, 'recoveryProject': str(self.raw),
                      'coreProject': str(self.core), 'managedTypes': str(self.managed),
                      'cabBundles': str(self.workspace / 'original-cab-bundles.json')}
        storage.write_json(self.workspace / 'full-recovery.json', self.result)

    @staticmethod
    def file(path, data):
        path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)

    @staticmethod
    def records(folder):
        return [{'path': row['path'], 'bytes': row['size'], 'sha256': row['sha256']}
                for row in storage.inventory(folder)]

    def complete(self):
        return recovery_resume.completed_raw(self.workspace, self.inputs, self.game)

    def test_real_complete_result_is_adopted_without_redundant_raw_asset_reads(self):
        with patch.object(recovery_resume, 'digest', wraps=recovery_resume.digest) as read:
            result = self.complete()
        self.assertEqual(result['_completedRaw'], {'packages': 1, 'files': 2})
        self.assertNotIn(self.asset, [call.args[0] for call in read.call_args_list])
        self.assertEqual(self.asset.read_bytes(), b'actual retained raw asset')

    def test_adopted_raw_bytes_are_checked_at_the_real_staging_writer(self):
        self.complete()
        row = next(row for row in self.checkpoint['files'] if row['path'] == 'Assets/native.asset')
        destination = self.root / 'derived/Assets/native.asset'
        proofs = full_assets._StageProofs()
        full_assets._copy_recovered(self.asset, destination, row['sha256'],
                                    expected_size=row['bytes'], proofs=proofs)
        self.assertEqual(destination.read_bytes(), self.asset.read_bytes())
        destination.unlink()
        changed = b'X' * row['bytes']
        self.asset.write_bytes(changed)
        # Adoption validates closed metadata, not a redundant asset sweep.
        self.assertEqual(self.complete()['_completedRaw']['files'], 2)
        with self.assertRaisesRegex(storage.BuildError, 'differs from recovery receipt'):
            full_assets._copy_recovered(self.asset, destination, row['sha256'],
                                        expected_size=row['bytes'], proofs=full_assets._StageProofs())
        self.assertFalse(destination.exists())
        self.assertEqual(self.asset.read_bytes(), changed)

    def test_real_builder_dispatch_adopts_closed_result_without_launching_raw_tools(self):
        source = self.root / 'selected-source'
        self.file(source / 'tools/quest-recovery/full_recovery.py', b'fixture launcher must not execute')
        args = builder.parser().parse_args(['prepare', '--target', 'game', '--dotnet', sys.executable])
        def select(output, inputs, selected_source, game, recipe, *, qualifications):
            qualifications[str(self.workspace)] = recovery_resume._qualification(self.workspace, inputs, game)
            return self.workspace
        with patch.object(builder.recovery_resume, 'select_workspace', side_effect=select), \
             patch.object(builder, 'recovery_helper_preflight'), \
             patch.object(builder, 'command', side_effect=AssertionError('closed export orchestration replayed')), \
             patch.object(builder, 'owned_tmp_source_archive', return_value=self.root / 'public-tmp.zip'), \
             patch.object(builder.full_assets, 'stage', side_effect=RuntimeError('actual derived consumer reached')) as stage:
            with self.assertRaisesRegex(RuntimeError, 'actual derived consumer reached'):
                builder.prepare(args, self.inputs, self.root / 'output', source, self.game)
        self.assertEqual(stage.call_args.args[:2], (self.raw, self.game))
        self.assertEqual(stage.call_args.kwargs['resume_owner']['gameKey'], self.inputs['game']['key'])
        self.assertEqual(self.asset.read_bytes(), b'actual retained raw asset')

    def test_pending_merge_never_adopts_a_stale_complete_marker(self):
        storage.write_json(self.workspace / 'BundleRecovery/merge-pending.json', {'state': 'incomplete'})
        with patch.object(recovery_resume, '_qualification', side_effect=AssertionError('must follow journal replay')):
            self.assertIsNone(self.complete())

    def test_unfinished_result_follows_existing_export_resume(self):
        self.result['fullOriginalCatalogRecovered'] = False
        storage.write_json(self.workspace / 'full-recovery.json', self.result)
        self.assertIsNone(self.complete())

    def test_missing_result_follows_existing_export_resume(self):
        (self.workspace / 'full-recovery.json').unlink()
        self.assertIsNone(self.complete())

    def test_incomplete_or_changed_closed_checkpoint_is_an_explicit_failure(self):
        for changes in ({'completedGroups': []}, {'completedGroups': [True]}, {'assetsRecovered': False},
                        {'coreIdentitySha256': 'b' * 64}, {'sourceFingerprint': 'b' * 64},
                        {'catalogSha256': 'b' * 64}, {'groups': []}, {'assetReferences': None}):
            with self.subTest(changes=changes):
                storage.write_json(self.checkpoint_path, {**self.checkpoint, **changes})
                with self.assertRaises(storage.BuildError): self.complete()

    def test_changed_managed_metadata_and_unowned_result_paths_fail(self):
        self.managed.write_bytes(b'changed metadata')
        with self.assertRaisesRegex(storage.BuildError, 'managed metadata changed'): self.complete()
        self.managed.write_bytes(b'{"originalTypes":[]}')
        self.result['recoveryProject'] = str(self.root / 'foreign')
        storage.write_json(self.workspace / 'full-recovery.json', self.result)
        with self.assertRaisesRegex(storage.BuildError, 'owned recoveryProject path'): self.complete()

    def test_changed_package_plan_is_not_silently_exported_again(self):
        self.plan['groups'][0]['bundles'][0]['sha256'] = 'b' * 64
        storage.write_json(self.workspace / 'BundleRecovery/bundle-plan.json', self.plan)
        with self.assertRaisesRegex(storage.BuildError, 'package plan differs'): self.complete()

    def test_lost_bundle_ownership_and_final_source_guard_are_explicit(self):
        storage.write_json(self.workspace / 'original-cab-bundles.json', {'CAB-wrong': 'unknown.bundle'})
        with self.assertRaisesRegex(storage.BuildError, 'bundle ownership'): self.complete()
        storage.write_json(self.workspace / 'original-cab-bundles.json', {'CAB-original': 'original.bundle'})
        self.checkpoint['sourceInventory'] = self.checkpoint['sourceInventory'][:-1]
        storage.write_json(self.checkpoint_path, self.checkpoint)
        with self.assertRaisesRegex(storage.BuildError, 'source inventory'): self.complete()

    def test_same_invocation_qualification_is_not_read_again(self):
        proof = recovery_resume._qualification(self.workspace, self.inputs, self.game)
        with patch.object(recovery_resume, '_qualification', side_effect=AssertionError('duplicate core qualification')):
            result = recovery_resume.completed_raw(self.workspace, self.inputs, self.game,
                                                   qualifications={str(self.workspace): proof})
        self.assertEqual(result['_completedRaw']['packages'], 1)

    def test_new_derived_journal_never_invalidates_the_unchanged_exporter_contract(self):
        names = sorted(recovery_resume.ORCHESTRATION_FILES | {
            'tools/quest-recovery/QuestExportIdentity.cs', 'tools/quest-recovery/export_identity.py',
            'tools/quest-recovery/tool-lock.json', 'tools/quest-builder/full_assets.py'})
        previous = {'mod': {'files': [{'path': name, 'size': 1, 'sha256': 'a' * 64} for name in names]}}
        current = copy.deepcopy(previous)
        current['mod']['files'].append({'path': 'tools/quest-builder/staging_resume.py', 'size': 2, 'sha256': 'b' * 64})
        self.assertTrue(recovery_resume._compatible(previous, current))
        current['mod']['files'].append({'path': 'tools/quest-recovery/unknown-export-contract.py', 'size': 1, 'sha256': 'c' * 64})
        self.assertFalse(recovery_resume._compatible(previous, current))

    def test_actual_reused_export_counts_do_not_publish_derived_success(self):
        result = self.complete()
        with patch.object(recovery_resume.build_progress, 'event') as emit:
            recovery_resume.announce_completed_raw(result)
        events = [call.args[0] for call in emit.call_args_list]
        self.assertEqual(events.count('recovery-batches'), 1)
        self.assertNotIn('recovery-section:staging', events)
        self.assertTrue(all(call.kwargs['status'] == 'reuse' for call in emit.call_args_list))


if __name__ == '__main__': unittest.main()
