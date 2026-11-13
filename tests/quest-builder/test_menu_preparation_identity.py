"""Exact late-menu aliases retain original assets without concealing producers.

Exercise whole real Builder trees and the actual preparation scope/profile
normalizers. The previous source is immutable Git history; new data producers
are late owners, while unknown earlier consumers and template inputs still fail.
"""
import ast
import copy
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import preparation_identity as identity
import recovery_resume as recovery
import storage


class MenuPreparationIdentityTests(unittest.TestCase):
    def setUp(self):
        self.previous = subprocess.check_output(['git', '-C', str(ROOT), 'show',
            '595791b7825c28b87069db8b2037386b755197ef:tools/quest-builder/builder.py'])
        self.current = (ROOT / identity.BUILDER).read_bytes()

    def test_complete_frozen_builder_and_original_prefix_retain_the_actual_previous_producers(self):
        for prefix, expected in ((False, '6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1'),
                                 (True, 'cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f')):
            self.assertEqual(identity.builder_producer_digest(self.previous, original_prefix=prefix), expected)
            self.assertEqual(identity.builder_producer_digest(self.current, original_prefix=prefix), expected)
        before = {node.name: ast.dump(node, include_attributes=False) for node in ast.parse(self.previous).body
                  if isinstance(node, ast.FunctionDef)}
        after = {node.name: ast.dump(node, include_attributes=False) for node in ast.parse(self.current).body
                 if isinstance(node, ast.FunctionDef)}
        for name in ('package_mod_content', 'validate_mod_bundle', '_mod_bank_workspace', 'mod_bank_recipe'):
            self.assertEqual(before[name], after[name], name)

    def test_unknown_earlier_consumer_arguments_tail_scope_and_constants_cannot_use_whole_ast_alias(self):
        changes = (
            (b'    def generate_files(resume):',
             b'    def generate_files(resume):\n        menu_depth_resources.story_records(source, inputs["mod"]["files"])'),
            (b'base_project, base_contracts', b'loading_resources, base_contracts'),
            (b'lambda: package_menu_content(project, inputs, output, source)',
             b'lambda: package_menu_content(project, inputs, output, game)'),
            (b'refresh_tail=marker != {"schema": 1, "contentKey": story_key}', b'refresh_tail=True'),
            (b'RECIPE = 1', b'RECIPE = 999'),
        )
        for before, after in changes:
            changed = self.current.replace(before, after, 1)
            self.assertNotEqual(changed, self.current)
            for prefix in (False, True):
                with self.subTest(change=after, prefix=prefix):
                    self.assertNotEqual(identity.builder_producer_digest(changed, original_prefix=prefix),
                                        identity.builder_producer_digest(self.current, original_prefix=prefix))

    def test_unreviewed_late_compiler_argument_remains_distinct_from_full_original_compatibility(self):
        changed = self.current.replace(b'GloomhavenVRMenuDepthSkipStoryCopy=true', b'GloomhavenVRMenuDepthSkipStoryCopy=false', 1)
        self.assertNotEqual(changed, self.current)
        self.assertNotEqual(identity.builder_producer_digest(changed), identity.builder_producer_digest(self.current))

    def test_old_and_new_complete_tail_reader_profiles_normalize_but_unknown_or_missing_rows_do_not(self):
        old = list(recovery.OBSERVATION_MENU_STORY_PREVIOUS.values())
        current = list(recovery.OBSERVATION_EDITOR_OVERLAY.values())
        self.assertEqual(recovery.preparation_source_rows(old), recovery.preparation_source_rows(current))
        name = 'tools/quest-builder/prepare_resume.py'
        bad = copy.deepcopy(current)
        next(row for row in bad if row['path'] == name)['sha256'] = 'f' * 64
        unknown = next(row for row in bad if row['path'] == name)
        self.assertIn(unknown, recovery.preparation_source_rows(bad))
        incomplete = [row for row in current if row['path'] != name]
        metadata = next(row for row in incomplete if row['path'] == identity.METADATA_IDENTITY)
        self.assertIn(metadata, recovery.preparation_source_rows(incomplete))

    def test_exact_delivery_rows_and_late_helpers_preserve_completed_scope_without_ignoring_template_producers(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            old_source, new_source = root / 'old', root / 'new'
            for source, raw in ((old_source, self.previous), (new_source, self.current)):
                path = source / identity.BUILDER; path.parent.mkdir(parents=True)
                path.write_bytes(raw)
            old_rows = [storage.record_file(old_source / identity.BUILDER, identity.BUILDER),
                        *recovery.OBSERVATION_MENU_STORY_PREVIOUS.values(),
                        *(pair[1] for pair in identity.MENU_DEPTH_DELIVERY_ROWS.values())]
            new_rows = [storage.record_file(new_source / identity.BUILDER, identity.BUILDER),
                        *recovery.OBSERVATION_EDITOR_OVERLAY.values(),
                        *(pair[0] for pair in identity.MENU_DEPTH_DELIVERY_ROWS.values())]
            new_rows += [{'path': name, 'size': 5, 'sha256': 'b' * 64} for name in (
                'tools/quest-builder/menu_depth_resources.py', 'tools/quest-builder/menu_depth_content.py',
                'tools/quest-builder/menu_depth_update.py', 'tools/quest-menu-depth/Editor/MenuDepthResourceBuilder.cs')]
            def scoped(source, rows):
                inputs = {'schema': 1, 'target': 'game', 'game': {'key': 'a' * 64}, 'profile': {},
                          'mod': {'files': rows, 'modBuild': 717}}
                return identity._scope(inputs, source, recovery, original_prefix=True, completed=True)
            before = scoped(old_source, old_rows)
            self.assertEqual(before, scoped(new_source, new_rows))
            for name in ('tools/quest-builder/apk_update.py', 'tools/quest-builder/installation_manifest.py'):
                changed = copy.deepcopy(new_rows)
                next(row for row in changed if row['path'] == name)['sha256'] = 'f' * 64
                self.assertNotEqual(before, scoped(new_source, changed), name)
            for name in ('unity/GloomhavenVR.Quest/Assets/Quest/Runtime/UnreviewedTemplate.prefab',
                         'src/GloomhavenVR/Assets/GloomhavenVR_logo.png',
                         'libs/RuntimeDeps/UnreviewedOriginalConsumer.dll'):
                row = {'path': name, 'size': 5, 'sha256': 'c' * 64}
                after = scoped(new_source, [*new_rows, row])
                self.assertNotEqual(before, after, name)
                self.assertIn(row, after['mod']['producerFiles'])


if __name__ == '__main__':
    unittest.main()
