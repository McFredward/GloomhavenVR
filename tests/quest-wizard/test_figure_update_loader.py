"""Run actual Wizard figure planning in a fresh isolated Python process.

Only APK metadata and external provisioning are fixture boundaries. The real
installer loader, restored module registry and update/figure planners execute.
No Unity, owned game-bank reads, conversion or APK modification is performed.
"""
from pathlib import Path
import json
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def copy_planning_sources(destination):
    for directory in ('tools/quest-builder', 'tools/figure-mesh'):
        for source in (ROOT / directory).rglob('*'):
            if source.is_file() and source.suffix in ('.py', '.cs', '.csproj', '.md') and not set(source.parts) & {'bin', 'obj', '__pycache__'}:
                target = destination / source.relative_to(ROOT)
                target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
    for name in ('tools/quest-installer/installer.py', 'src/GloomhavenVR/Core/Perf/ScenarioFigureMeshBank.cs',
                 'src/GloomhavenVR/Net/NetProtocol.cs',
                 'unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestFigureMeshContent.cs',
                 'unity/GloomhavenVR.Quest/Packages/manifest.json',
                 'tools/quest-recovery/NativePortableRecovery/Program.cs',
                 'tools/quest-recovery/NativePortableRecovery/NativePortableRecovery.csproj',
                 'tools/quest-recovery/NativePortableRecovery/packages.lock.json'):
        target = destination / name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(ROOT / name, target)


def probe(source, mode):
    import importlib.util
    import profile  # Existing host stdlib module must survive every Builder load.
    import types
    from unittest.mock import Mock, patch
    sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
    import discovery
    import wizard
    import unity_setup
    from state import Store, atomic_json

    if mode == 'hostile':
        foreign = types.ModuleType('storage')
        def refuse(name): raise AssertionError('Host storage module was consumed: ' + name)
        foreign.__getattr__ = refuse
        sys.modules['storage'] = foreign
    absent = object()
    names = ('storage', 'figure_banks', 'audio_banks', 'builder', 'profile', '_ghvr_wireless_figure_banks', '_ghvr_wireless_audio_banks')
    before = {name: sys.modules.get(name, absent) for name in names}
    search_path = list(sys.path)
    helper = discovery.builder(source)
    updates = helper._local_helper('update_driver')
    assert sys.path == search_path
    assert all(sys.modules.get(name, absent) is value for name, value in before.items())
    assert str(source / 'tools/quest-builder') not in sys.path
    # The audio helper runs after the same isolated aliases have been restored.
    # Its producer estimate must still use this captured Builder's dependencies.
    audio_plan = helper.audio_banks.plan(source, {'files': [
        {'path': name, 'size': 44, 'sha256': 'a' * 64} for name in ('resources.assets', 'resources.resource')]})
    assert len(audio_plan['producerKey']) == 64
    assert helper.audio_banks.original_clips.__module__ == '_ghvr_wireless_audio'

    rows = [helper.record_file(source / name, name) for name in (
        'tools/quest-builder/builder.py', 'tools/quest-builder/preparation_identity.py',
        'unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestFigureMeshContent.cs')]
    game = {'unityVersion': '2021.3.5f1', 'files': [{'path': 'StreamingAssets/aa/StandaloneWindows64/hero_fixture_assets_all.bundle',
        'size': 44, 'sha256': 'a' * 64}]}
    build = helper.mod_build(source)
    original = {'game': game, 'mod': {'files': rows, 'modBuild': build}}
    base = {'manifest': original, 'modBuild': build, 'capsule': {
        'packageAbi': updates.package_fingerprint(helper, source, original),
        'payloadProducer': updates.payload_producer(helper, source, rows)}}
    if mode == 'warm':
        base['figureBanks'] = {'producerKey': helper.figure_banks.plan(source, game)['producerKey']}
    observed = []
    actual_requirements = updates.update_requirements
    def requirements(*args, output=None):
        assert output == store.root / 'build'
        result = actual_requirements(*args); observed.append(result); return result

    with tempfile.TemporaryDirectory(prefix='quest wizard figure context ') as folder:
        root = Path(folder); store = Store(root / 'workspace')
        apk = root / 'existing.apk'; apk.write_bytes(b'Metadata-only fixture; no APK parser or signer invoked')
        state = store.create(wizard.choices({'mode': 'update-mod', 'gameRoot': str(root), 'baseApk': str(apk), 'install': False}))
        state['completed'] = {'source': {'details': {'sourceRoot': str(source)}}}
        signing = store.session_dir(state['session']) / 'apk-tools.json'; atomic_json(signing, {'schema': 1})
        with store.active(state), patch.object(wizard.discovery, 'builder', return_value=helper), \
             patch.object(helper.apk_updates, 'inspect_base', return_value=base), \
             patch.object(helper, 'source_inventory', return_value=(rows, 'b' * 40, False)), \
             patch.object(helper, '_local_helper', return_value=updates), \
             patch.object(updates, 'update_requirements', side_effect=requirements), \
             patch.object(helper, 'command', side_effect=AssertionError('Planning must not launch a compiler')), \
             patch.object(helper, 'snapshot', side_effect=AssertionError('Planning must not reconvert game data')), \
             patch.object(unity_setup, 'prepare', return_value=([], {'required': True})) as prepare, \
             patch.object(wizard.provision, 'profile_tools', return_value=([signing], {})) as signing_tools:
            _, details = wizard.Engine(store).stage_unity(state, Mock())
        assert len(observed) == 1 and observed[0]['rebuildReasons'] == []
        expected = mode != 'warm'
        assert observed[0]['figureBanksChanged'] is expected
        assert observed[0]['requiresUnity'] is expected and details['required'] is expected
        assert prepare.call_count == int(expected) and signing_tools.call_count == int(not expected)
        assert apk.read_bytes() == b'Metadata-only fixture; no APK parser or signer invoked'
    assert helper.figure_banks.BuildError is helper.BuildError
    assert helper.figure_banks._ordinary_owned is helper._ordinary_owned
    assert Path(helper.figure_banks.__file__).resolve() == (source / 'tools/quest-builder/figure_banks.py').resolve()
    assert Path(helper.figure_banks.record_file.__globals__['__file__']).resolve() == (source / 'tools/quest-builder/storage.py').resolve()
    assert updates._figures(helper) is helper.figure_banks
    assert sys.path == search_path
    assert all(sys.modules.get(name, absent) is value for name, value in before.items())
    return {'mode': mode, 'figureBanksChanged': expected, 'requiresUnity': expected,
            'moduleRegistryRestored': True, 'searchPathUnchanged': True, 'noConversion': True}


class FigureUpdateLoaderTests(unittest.TestCase):
    def run_probe(self, source, mode):
        return subprocess.run([sys.executable, '-I', '-B', str(Path(__file__).resolve()), '--probe', str(source), mode],
                              cwd=tempfile.gettempdir(), capture_output=True, text=True, timeout=30)

    def test_cold_wizard_uses_actual_retained_figure_dependency_without_builder_path(self):
        result = self.run_probe(ROOT, 'cold'); self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(json.loads(result.stdout)['figureBanksChanged'])

    def test_completed_bank_keeps_no_compiler_wizard_path(self):
        result = self.run_probe(ROOT, 'warm'); self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(json.loads(result.stdout)['requiresUnity'])

    def test_foreign_storage_and_stdlib_profile_survive_actual_wizard_planning(self):
        result = self.run_probe(ROOT, 'hostile'); self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(json.loads(result.stdout)['moduleRegistryRestored'])

    def test_captured_source_in_space_containing_path_never_uses_host_helpers(self):
        with tempfile.TemporaryDirectory(prefix='quest selected snapshot ') as folder:
            source = Path(folder); copy_planning_sources(source)
            result = self.run_probe(source, 'cold'); self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_negative_control_reopening_helper_reproduces_reported_storage_import_failure(self):
        with tempfile.TemporaryDirectory(prefix='quest rejected lazy import ') as folder:
            source = Path(folder); copy_planning_sources(source)
            path = source / 'tools/quest-builder/update_driver.py'; body = path.read_text()
            guard = '    if captured is not None:\n        return captured'
            self.assertEqual(body.count(guard), 1)
            path.write_text(body.replace(guard, '    if False:\n        return captured'))
            result = self.run_probe(source, 'cold')
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("ModuleNotFoundError: No module named 'storage'", result.stderr)
            self.assertIn('stage_unity', result.stderr)


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--probe':
        print(json.dumps(probe(Path(sys.argv[2]).absolute(), sys.argv[3])))
    else:
        unittest.main()
