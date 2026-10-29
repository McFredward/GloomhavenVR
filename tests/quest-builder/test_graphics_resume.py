"""The captured texture frontier survives reviewed observers and pending graphics updates."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('graphics_preparation_identity', ROOT / 'tests/quest-builder/test_preparation_identity.py')
checks = importlib.util.module_from_spec(spec); spec.loader.exec_module(checks)


class GraphicsResumeTests(unittest.TestCase):
    def setUp(self):
        self.fixture = checks.ActualPreparationMigrationTests('test_runtime_update_preserves_all_closed_original_conversions_before_mod_banks')
        self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.f = self.fixture.fixture
        self.fixture.stop = False
        for name in (*checks.recovery_resume.TEXTURE_PROGRESS_FIXED, *checks.recovery_resume.SHADER_PROGRESS_FIXED, *checks.identity.PREFIX_UNCONSUMED_GRAPHICS):
            self.f.write(self.f.source, name, (ROOT / name).read_bytes())
        self.before = self.fixture.inputs()
        self.before_source = self.f.output / 'inputs/mod' / self.before['mod']['key']
        shutil.copytree(self.f.source, self.before_source, dirs_exist_ok=True)

    def frontier(self):
        def boundary(*args, **kwargs):
            raise checks.storage.BuildError('Captured pending compute contract frontier')
        observer = patch.object(sys.modules['campaign_compute'], 'stage', boundary)
        self.f.stack.enter_context(observer)
        with self.assertRaisesRegex(checks.storage.BuildError, 'pending compute contract frontier'):
            self.f.run_prepare()
        project = self.f.project()
        journal = self.f.output / 'cache/prepare-resume' / project.name / 'journal.json'
        self.closed = json.loads(journal.read_text())['steps']
        self.assertEqual(len(self.closed), 17)
        self.assertEqual(self.closed[-1]['name'], 'native-texture2d')
        return project, journal

    def test_pending_graphics_changes_retain_all_seventeen_closed_steps_and_library(self):
        project, journal = self.frontier()
        library = self.f.write(project, 'Library/retained-frontier', b'qualified original Unity imports')
        calls = dict(self.f.calls)
        for name in checks.identity.PREFIX_UNCONSUMED_GRAPHICS:
            self.f.write(self.f.source, name, (self.f.source / name).read_bytes() + b'\n# Current pending-graphics update.\n')
        current = self.fixture.inputs()
        self.assertEqual(checks.identity.rebind_key(self.f.output, project, current, self.f.source,
            target='game', recipe=checks.builder.RECIPE, recovery=checks.recovery_resume), self.before['inputKey'])
        real_copy = checks.builder.prepare_resume.copy_changed
        def pending_copy_only(source, target, **options):
            self.assertTrue('undo-campaign-compute' in Path(source).parts or 'undo-campaign-compute' in Path(target).parts)
            return real_copy(source, target, **options)
        with patch.object(checks.builder.prepare_resume, 'copy_changed', side_effect=pending_copy_only):
            with self.assertRaisesRegex(checks.storage.BuildError, 'pending compute contract frontier'):
                self.f.run_prepare()
        self.assertEqual(json.loads(journal.read_text())['steps'], self.closed)
        self.assertEqual(self.f.calls, calls)
        self.assertEqual(library.read_bytes(), b'qualified original Unity imports')
        # These generators become consumed at the next committed checkpoint.
        value = json.loads(journal.read_text()); value['inputKey'] = self.before['inputKey']
        value['steps'].append({'name':'campaign-compute', 'operation':'graphics', 'outputs':[]})
        value['pending'] = {'name':'campaign-shaders', 'operation':'graphics', 'undo':[]}
        checks.storage.write_json(journal, value)
        self.assertIsNone(checks.identity.rebind_key(self.f.output, project, current, self.f.source,
            target='game', recipe=checks.builder.RECIPE, recovery=checks.recovery_resume))

    def test_unknown_consumed_texture_changes_cannot_use_observation_equivalence(self):
        project, _ = self.frontier()
        name = 'tools/quest-builder/full_texture2d.py'
        self.f.write(self.f.source, name, (self.f.source / name).read_bytes() + b'\n# Unreviewed texture producer.\n')
        current = self.fixture.inputs()
        self.assertIsNone(checks.identity.rebind_key(self.f.output, project, current, self.f.source,
            target='game', recipe=checks.builder.RECIPE, recovery=checks.recovery_resume))

    def test_progress_alias_requires_exact_complete_pairs(self):
        pairs = ((checks.recovery_resume.TEXTURE_PROGRESS_PREVIOUS, checks.recovery_resume.TEXTURE_PROGRESS_FIXED),
                 (checks.recovery_resume.SHADER_PROGRESS_PREVIOUS, checks.recovery_resume.SHADER_PROGRESS_FIXED))
        for previous_rows, fixed_rows in pairs:
            previous, fixed = list(previous_rows.values()), list(fixed_rows.values())
            self.assertEqual(checks.recovery_resume.preparation_source_rows(fixed), previous)
            changed = copy.deepcopy(fixed); changed[0]['sha256'] = 'f' * 64
            self.assertEqual(checks.recovery_resume.preparation_source_rows(changed), changed)
            self.assertEqual(checks.recovery_resume.preparation_source_rows(fixed[:1]), fixed[:1])
            duplicated = [*fixed, fixed[0]]
            self.assertEqual(checks.recovery_resume.preparation_source_rows(duplicated), duplicated)
        # All observer and producer profiles occur together in actual releases.
        # Every reviewed set must normalize, without an earlier short circuit.
        combined = [*checks.recovery_resume.OBSERVATION_GRAPHICS_CONTRACT.values(),
                    *(row for _, fixed in pairs for row in fixed.values())]
        expected = [*checks.recovery_resume.OBSERVATION_PREVIOUS.values(),
                    *(row for previous, _ in pairs for row in previous.values())]
        self.assertEqual(checks.recovery_resume.preparation_source_rows(combined), expected)


if __name__ == '__main__': unittest.main()
