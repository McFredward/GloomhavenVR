"""Compiler admission follows the captured update, not the selected UI mode."""
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import wizard
import unity_setup
from state import Store, WizardError, atomic_json


class UpdateCompilerRequirementsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.store = Store(Path(self.temp.name) / 'workspace')
        self.apk = Path(self.temp.name) / 'source.apk'; self.apk.write_bytes(b'fixture APK')
        self.state = self.store.create(wizard.choices({'mode': 'update-mod', 'gameRoot': self.temp.name,
            'baseApk': str(self.apk), 'install': False}))
        self.state['completed'] = {
            'source': {'key': 'source-key', 'details': {'sourceRoot': str(ROOT)}},
            'tools': {'details': {'dotnet8': 'dotnet8', 'dotnet10': 'dotnet10'}},
            'profile': {'details': {'profilePath': 'profile.json', 'steamLogo': 'logo.png'}}}
        self.engine = wizard.Engine(self.store)
        self.updates = SimpleNamespace(update_requirements=Mock(return_value={'requiresUnity': False}))
        self.helper = SimpleNamespace(_local_helper=lambda _: self.updates,
            apk_updates=SimpleNamespace(inspect_base=lambda _: {'signed fixture capsule': True}),
            source_inventory=lambda _: ([], 'a' * 40, False), mod_build=lambda _: 671)

    def test_observer_only_update_uses_signing_tools_without_launching_editor(self):
        signing = self.store.session_dir(self.state['session']) / 'apk-tools.json'
        atomic_json(signing, {'schema': 1, 'fixture': 'signing tools'})
        with self.store.active(self.state), patch.object(wizard.discovery, 'builder', return_value=self.helper), \
                patch.object(wizard.provision, 'profile_tools', return_value=([signing], {})) as provision, \
                patch.object(unity_setup, 'prepare', side_effect=AssertionError('No compiler required')):
            paths, details = self.engine.stage_unity(self.state, Mock())
        self.assertFalse(details['required']); self.assertIn(signing, paths)
        self.assertEqual(provision.call_args.kwargs['stage'], 'unity')
        self.assertFalse(json.loads(paths[0].read_text())['required'])
        self.state['completed']['unity'] = {'details': details}
        argv = self.engine.arguments(self.state, 'inspect')
        self.assertIn('--apk-tools-json', argv); self.assertNotIn('--unity-editor', argv)
        self.assertNotIn('--dotnet', argv)

    def test_code_or_real_asset_update_still_prepares_actual_unity(self):
        self.updates.update_requirements.return_value = {'requiresUnity': True}
        with self.store.active(self.state), patch.object(wizard.discovery, 'builder', return_value=self.helper), \
                patch.object(wizard.provision, 'profile_tools', side_effect=AssertionError('Compiler needed')), \
                patch.object(unity_setup, 'prepare', return_value=([], {'unityEditor': 'actual editor'})) as prepare:
            _, details = self.engine.stage_unity(self.state, Mock())
        self.assertEqual(details['unityEditor'], 'actual editor'); prepare.assert_called_once()

    def test_real_signing_provision_reports_inside_current_prerequisite_stage(self):
        suffix = '.exe' if wizard.provision.host_key() == 'windows-x64' else ''
        def tool(name, store, session, *, stage='tools'):
            root = store.root / 'tools' / name; root.mkdir(parents=True, exist_ok=True)
            store.progress(session, stage, 'tool-extract-' + name, 1, 1, 'files')
            if name == 'apkJdk':
                root = root / 'bin'; root.mkdir(exist_ok=True)
                for filename in ('java', 'keytool'): (root / (filename + suffix)).write_bytes(b'fixture tool')
                return root / ('java' + suffix), list(root.iterdir())
            (root / 'lib').mkdir(); (root / 'lib/apksigner.jar').write_bytes(b'fixture tool')
            for filename in ('aapt', 'zipalign'): (root / (filename + suffix)).write_bytes(b'fixture tool')
            return root / ('aapt' + suffix), list(root.rglob('*'))
        supervisor = SimpleNamespace(run=lambda argv, log, **kwargs: Path(log).write_text('openjdk version 17.0.16'))
        with self.store.active(self.state), patch.object(wizard.discovery, 'builder', return_value=self.helper), \
                patch.object(wizard.provision, '_provision_tool', side_effect=tool), \
                patch.object(unity_setup, 'prepare', side_effect=AssertionError('No compiler required')):
            _, details = self.engine.stage_unity(self.state, supervisor)
            current = self.store.load(self.state['session'])
        self.assertFalse(details['required'])
        self.assertEqual(current['stages'][0]['progress']['percent'], 0)
        self.assertEqual(current['stages'][2]['progress']['stageOperation'], 'prerequisites')

    def test_replaced_apk_at_same_path_invalidates_no_compiler_prerequisite(self):
        before = self.engine.key(self.state, 'unity')
        self.apk.write_bytes(b'changed signed input fixture')
        after = self.engine.key(self.state, 'unity')
        self.assertNotEqual(before, after)

    def test_new_session_adopts_closed_compiler_tools_without_archive_provision(self):
        import tool_reuse
        self.state['choices']['ownedDlc'] = []
        receipt = self.store.session_dir(self.state['session']) / 'tools.json'
        atomic_json(receipt, {'schema': 1})
        details = {'git': 'retained Git', 'dotnet8': 'retained .NET8', 'dotnet10': 'retained .NET10'}
        with self.store.active(self.state), patch.object(tool_reuse, 'lookup', return_value=([receipt], details)), \
                patch.object(wizard.provision, 'tools', side_effect=AssertionError('No repeat extraction')):
            paths, result = self.engine.stage_tools(self.state, Mock())
        self.assertEqual(paths, [receipt]); self.assertEqual(result, details)

    def inspect_plan(self, *, requires=False, strategy='compact-code'):
        root = self.store.root / 'build'; key = 'c' * 64
        atomic_json(root / 'latest-update-input.json', {'schema': 1, 'plan': 'updates/' + key + '/plan.json'})
        atomic_json(root / 'updates' / key / 'plan.json', {'schema': 1, 'mode': 'update-mod', 'updateKey': key,
            'gameInputKey': 'a' * 64, 'baseApkSha256': 'd' * 64,
            'inputs': {'game': {'key': 'b' * 64}, 'mod': {'modBuild': 671}},
            'updateStrategy': strategy, 'requiresUnity': requires, 'rebuildReasons': []})
        self.state['completed']['unity'] = {'details': {'required': False}}
        with patch.object(wizard.provision, 'environment', return_value={}):
            return self.engine.stage_inspect(self.state, Mock())

    def test_final_qualified_observer_plan_is_accepted_without_unity(self):
        _, details = self.inspect_plan()
        self.assertFalse(details['requiresUnity']); self.assertEqual(details['updateStrategy'], 'compact-code')

    def test_source_changed_after_provisioning_cannot_compile_without_editor(self):
        with self.assertRaises(WizardError) as raised: self.inspect_plan(requires=True)
        self.assertEqual(raised.exception.code, 'update_unity_required')
        with self.assertRaises(WizardError): self.inspect_plan(strategy='cached-full-build', requires=False)


if __name__ == '__main__': unittest.main()
