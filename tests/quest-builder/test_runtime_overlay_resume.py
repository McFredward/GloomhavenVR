"""Completed preparation refreshes a tiny runtime overlay without asset work."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import builder
import editor_overlay
import preparation_identity as identity
import preparation_metadata as metadata
import recovery_resume
import runtime_overlay as overlay
import storage

spec = importlib.util.spec_from_file_location('runtime_completed_fixture', ROOT / 'tests/quest-builder/test_completed_preparation_resume.py')
completed = importlib.util.module_from_spec(spec); spec.loader.exec_module(completed)


def record(name, raw):
    return {'path': name, 'size': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}


class RuntimeOverlayTests(unittest.TestCase):
    def setUp(self):
        self.fixture = completed.CompletedPreparationTests('test_runtime_update_keeps_all_twenty_seven_asset_steps_and_later_owners')
        self.before_bytes = {name: ('// Previous runtime ' + name + '\n').encode() for name in overlay.TARGETS[:3]}
        self.after_bytes = {name: ('// Current runtime ' + name + '\n').encode() for name in overlay.TARGETS}
        self.before_profile = {name: record(name, raw) for name, raw in self.before_bytes.items()}
        self.after_profile = {name: record(name, raw) for name, raw in self.after_bytes.items()}
        self.metas = {name + '.meta': ('fileFormatVersion: 2\nguid: ' + format(i, '032x') + '\n').encode()
                      for i, name in enumerate(self.before_bytes, 1)}
        self.fixture.editor_sources = {**self.before_bytes, **self.metas}
        self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.f, self.project, self.journal = self.fixture.f, self.fixture.project, self.fixture.journal
        self.f.stack.enter_context(patch.object(overlay, 'B692', self.before_profile))
        self.f.stack.enter_context(patch.object(overlay, 'CURRENT', self.after_profile))
        for name, raw in self.after_bytes.items(): self.f.write(self.f.source, name, raw)
        self.current = self.fixture.updated()

    def assert_retained(self):
        self.fixture.assert_retained(self.current)
        value = json.loads(self.journal.read_text())
        latest = metadata._latest(value)
        for name, after in self.after_profile.items():
            relative = name[len(overlay.PREFIX):]
            self.assertEqual((self.project / relative).read_bytes(), self.after_bytes[name])
            self.assertEqual(latest[relative][1], {**after, 'path': relative})
            self.assertEqual(value['steps'][latest[relative][0]]['name'], 'base-project')
            self.assertEqual(sum(row['path'] == relative for step in value['steps'] for row in step['outputs']), 1)
        for name, raw in self.metas.items():
            self.assertEqual((self.project / name[len(overlay.PREFIX):]).read_bytes(), raw)
        self.assertFalse((self.journal.parent / 'metadata-refresh.json').exists())

    def test_whole_runtime_overlay_appends_new_contracts_and_retains_all_asset_owners(self):
        self.assertEqual(identity.rebind_key(self.f.output, self.project, self.current, self.f.source,
                         target='game', recipe=builder.RECIPE, recovery=recovery_resume), self.fixture.before['inputKey'])
        with patch.object(builder.prepare_resume, 'copy_changed', side_effect=AssertionError('No original project recopy')):
            self.f.run_prepare()
        self.assert_retained()
        stamps = {name: (self.project / name[len(overlay.PREFIX):]).stat().st_mtime_ns for name in self.after_bytes}
        self.current = self.fixture.updated(2)
        self.f.run_prepare(); self.assert_retained()
        self.assertEqual(stamps, {name: (self.project / name[len(overlay.PREFIX):]).stat().st_mtime_ns for name in self.after_bytes})

    def test_each_existing_and_new_script_publication_cut_replays_only_bounded_files(self):
        actual = editor_overlay.publish
        for cut in range(len(overlay.TARGETS)):
            with self.subTest(cut=cut):
                case = RuntimeOverlayTests('test_whole_runtime_overlay_appends_new_contracts_and_retains_all_asset_owners')
                case.setUp()
                try:
                    target = case.project / overlay.TARGETS[cut][len(overlay.PREFIX):]
                    def killed(path, raw):
                        actual(path, raw)
                        if Path(path) == target: raise RuntimeError('cut after runtime publication')
                    with patch.object(editor_overlay, 'publish', killed):
                        with self.assertRaisesRegex(RuntimeError, 'cut after runtime'): case.f.run_prepare()
                    self.assertEqual(json.loads(case.journal.read_text())['inputKey'], case.fixture.before['inputKey'])
                    case.f.run_prepare(); case.assert_retained()
                finally: case.doCleanups()

    def test_plan_and_journal_cuts_replay_without_reopening_original_banks(self):
        actual = metadata.write_json
        transaction = self.journal.parent / 'metadata-refresh.json'
        def killed_plan(path, value):
            actual(path, value)
            if Path(path) == transaction: raise RuntimeError('cut after plan')
        with patch.object(metadata, 'write_json', killed_plan):
            with self.assertRaisesRegex(RuntimeError, 'cut after plan'): self.f.run_prepare()
        self.assertEqual(json.loads(transaction.read_text())['schema'], 3)
        def killed_journal(path, value):
            actual(path, value)
            if Path(path) == self.journal: raise RuntimeError('cut after journal')
        with patch.object(metadata, 'write_json', killed_journal):
            with self.assertRaisesRegex(RuntimeError, 'cut after journal'): self.f.run_prepare()
        permitted = set(metadata.PATHS) | {name[len(overlay.PREFIX):] for name in overlay.TARGETS}
        actual_digest = builder.prepare_resume.digest
        def bounded(path):
            if Path(path).relative_to(self.project).as_posix() not in permitted:
                self.fail('Original bank reopened during replay: ' + str(path))
            return actual_digest(path)
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(builder.prepare_resume, 'digest', bounded):
            self.f.run_prepare()
        self.assert_retained()

    def test_unknown_new_script_bytes_are_not_overwritten_or_partially_published(self):
        name = overlay.TARGETS[-1]
        target = self.f.write(self.project, name[len(overlay.PREFIX):], b'foreign new source')
        before = {path: (self.project / path).read_bytes() for path in metadata.PATHS}
        with self.assertRaisesRegex(storage.BuildError, 'project/snapshot'):
            self.f.run_prepare()
        self.assertEqual(target.read_bytes(), b'foreign new source')
        self.assertEqual(before, {path: (self.project / path).read_bytes() for path in metadata.PATHS})
        self.assertEqual(self.f.calls, self.fixture.calls)

    def test_transaction_new_file_owner_payload_and_write_set_cannot_be_forged(self):
        actual = metadata.write_json
        transaction = self.journal.parent / 'metadata-refresh.json'
        def killed(path, value):
            actual(path, value)
            if Path(path) == transaction: raise RuntimeError('fixture cut')
        with patch.object(metadata, 'write_json', killed):
            with self.assertRaisesRegex(RuntimeError, 'fixture cut'): self.f.run_prepare()
        original = json.loads(transaction.read_text())
        before = {path: (self.project / path).read_bytes() for path in metadata.PATHS}
        for change in ('owner', 'payload', 'missing', 'extra'):
            with self.subTest(change=change):
                bad = copy.deepcopy(original)
                row = bad['files'][-1]
                if change == 'owner': row['step'] = 26
                elif change == 'payload': row['original'] = 'Zm9yZWlnbg=='
                elif change == 'missing': bad['files'].pop()
                else: bad['files'].append(copy.deepcopy(row))
                storage.write_json(transaction, bad)
                with self.assertRaisesRegex(storage.BuildError, 'transaction|contract|write set'): self.f.run_prepare()
                self.assertEqual(before, {path: (self.project / path).read_bytes() for path in metadata.PATHS})
        storage.write_json(transaction, original)
        self.f.run_prepare(); self.assert_retained()

    def test_unknown_mixed_or_extra_runtime_inputs_remain_consumed(self):
        for change in ('mixed', 'extra', 'metadata'):
            with self.subTest(change=change):
                bad = copy.deepcopy(self.current)
                if change == 'extra': bad['mod']['files'].append(record(overlay.RUNTIME + 'New.cs', b'unknown producer'))
                else:
                    name = overlay.TARGETS[0] if change == 'mixed' else identity.METADATA_IDENTITY
                    next(row for row in bad['mod']['files'] if row['path'] == name)['sha256'] = 'f' * 64
                bad['mod']['key'] = storage.value_hash({'files': bad['mod']['files']})
                bad.pop('inputKey'); bad['inputKey'] = storage.value_hash(bad)
                storage.write_json(self.f.output / 'manifests' / (bad['inputKey'] + '.json'), bad)
                self.assertIsNone(identity.rebind_key(self.f.output, self.project, bad, self.f.source,
                                  target='game', recipe=builder.RECIPE, recovery=recovery_resume))


class ExactRuntimeProfilesTests(unittest.TestCase):
    def test_only_complete_b692_b693_current_profiles_normalize(self):
        canonical = list(overlay.B692.values())
        for profile in (overlay.B692, overlay.B693, overlay.B717, overlay.CURRENT):
            self.assertEqual(overlay.normalize_rows(list(profile.values())), canonical)
        for profile in (overlay.B692, overlay.B693, overlay.B717, overlay.CURRENT):
            rows = copy.deepcopy(list(profile.values())); rows[0]['sha256'] = 'f' * 64
            self.assertEqual(overlay.normalize_rows(rows), rows)
        rows = [*overlay.CURRENT.values(), overlay.CURRENT[overlay.TARGETS[0]]]
        self.assertEqual(overlay.normalize_rows(rows), rows)

    def test_current_and_previous_bootstrap_bytes_are_exact_and_only_one_runtime_script_changes(self):
        for name, expected in overlay.B717.items():
            raw = subprocess.check_output(['git', '-C', str(ROOT), 'show', '595791b7:' + name])
            self.assertEqual(record(name, raw), expected)
        self.assertEqual({name: storage.record_file(ROOT / name, name) for name in overlay.TARGETS}, overlay.CURRENT)
        changes = overlay.changes({'mod': {'files': list(overlay.B717.values())}},
                                  {'mod': {'files': list(overlay.CURRENT.values())}})
        self.assertEqual([name for name, _, _ in changes], [overlay.TARGETS[0]])
        bad = copy.deepcopy(list(overlay.CURRENT.values()))
        bad[0]['sha256'] = 'f' * 64
        with self.assertRaisesRegex(storage.BuildError, 'whole reviewed'):
            overlay.changes({'mod': {'files': list(overlay.B717.values())}}, {'mod': {'files': bad}})


class ActualBootstrapOverlayTests(unittest.TestCase):
    def setUp(self):
        self.fixture = completed.CompletedPreparationTests('test_runtime_update_keeps_all_twenty_seven_asset_steps_and_later_owners')
        self.before = {name: subprocess.check_output(['git', '-C', str(ROOT), 'show', '595791b7:' + name])
                       for name in overlay.TARGETS}
        self.after = {name: (ROOT / name).read_bytes() for name in overlay.TARGETS}
        self.metas = {name + '.meta': ('fileFormatVersion: 2\nguid: ' + format(index, '032x') + '\n').encode()
                      for index, name in enumerate(overlay.TARGETS, 1)}
        self.fixture.editor_sources = {**self.before, **self.metas}
        self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.f, self.project = self.fixture.f, self.fixture.project
        for name, raw in self.after.items(): self.f.write(self.f.source, name, raw)
        self.current = self.fixture.updated()

    def assert_bootstrap(self):
        self.fixture.assert_retained(self.current)
        value = json.loads(self.fixture.journal.read_text())
        latest = metadata._latest(value)
        for name, expected in overlay.CURRENT.items():
            relative = name[len(overlay.PREFIX):]
            self.assertEqual((self.project / relative).read_bytes(), self.after[name])
            self.assertEqual(latest[relative][1], {**expected, 'path': relative})
            self.assertEqual(value['steps'][latest[relative][0]]['name'], 'base-project')
            self.assertEqual(sum(row['path'] == relative for step in value['steps'] for row in step['outputs']), 1)
            self.assertEqual((self.project / (relative + '.meta')).read_bytes(), self.metas[name + '.meta'])

    def test_real_b717_to_story_reader_refresh_changes_only_original_bootstrap_and_warm_update_reuses_it(self):
        self.assertEqual(identity.rebind_key(self.f.output, self.project, self.current, self.f.source,
            target='game', recipe=builder.RECIPE, recovery=recovery_resume), self.fixture.before['inputKey'])
        published = []
        actual = editor_overlay.publish
        def observed(path, raw):
            published.append(Path(path)); actual(path, raw)
        with patch.object(editor_overlay, 'publish', side_effect=observed), \
             patch.object(builder.prepare_resume, 'copy_changed', side_effect=AssertionError('No original bank/project recopy')):
            self.f.run_prepare()
        bootstrap = self.project / overlay.TARGETS[0][len(overlay.PREFIX):]
        self.assertEqual(published, [bootstrap])
        self.assert_bootstrap()
        stamp = bootstrap.stat().st_mtime_ns
        self.current = self.fixture.updated(2)
        with patch.object(editor_overlay, 'publish', side_effect=AssertionError('Already current Bootstrap retained')):
            self.f.run_prepare()
        self.assert_bootstrap()
        self.assertEqual(bootstrap.stat().st_mtime_ns, stamp)

    def test_actual_bootstrap_publication_cut_replays_its_exact_base_owner_without_original_conversions(self):
        bootstrap = self.project / overlay.TARGETS[0][len(overlay.PREFIX):]
        actual = editor_overlay.publish
        def interrupted(path, raw):
            actual(path, raw)
            if Path(path) == bootstrap: raise RuntimeError('cut after exact Bootstrap publication')
        with patch.object(editor_overlay, 'publish', side_effect=interrupted), \
             self.assertRaisesRegex(RuntimeError, 'exact Bootstrap publication'):
            self.f.run_prepare()
        self.assertEqual(json.loads(self.fixture.journal.read_text())['inputKey'], self.fixture.before['inputKey'])
        self.f.run_prepare()
        self.assert_bootstrap()


if __name__ == '__main__': unittest.main()
