"""Real owned-cache consumers and causal controls for source-only XR rebuilds."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import discovery
import provision
import runtime_dependencies as xr
import state

VERSIONS = dict(zip(xr.PACKAGES.values(), ('4.5.0', '2.2.3', '1.10.0')))
SDK_FILES = ('MSBuild.dll', 'Roslyn/bincore/csc.dll', 'Roslyn/bincore/Microsoft.CodeAnalysis.dll',
             'Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll',
             'Sdks/Microsoft.NET.Sdk/Sdk/Sdk.props', 'Sdks/Microsoft.NET.Sdk/Sdk/Sdk.targets')


def record(path, relative):
    return {'path': relative, 'size': path.stat().st_size, 'sha256': state.digest(path)}


class SimulatedPinnedTools:
    """Only acquisition/compiler boundaries are faked; receipt/cache code is real."""
    def __init__(self, test):
        self.test, self.store, self.session = test, test.store, test.session
        self.calls, self.fail_build = [], None

    def run(self, arguments, log, **kwargs):
        arguments = list(map(str, arguments)); self.calls.append(arguments)
        log = Path(log); log.parent.mkdir(parents=True, exist_ok=True)
        if arguments[1:] == ['--version']: log.write_text('8.0.425\n'); return
        if '-C' in arguments:
            directory = Path(arguments[2])
            if 'init' in arguments: (directory / '.git').mkdir()
            elif 'checkout' in arguments:
                (directory / 'package.json').write_text(json.dumps({'version': VERSIONS[directory.name]}))
                source = directory / 'Runtime/Boundary.cs'; source.parent.mkdir()
                source.write_text('namespace Example { public class Boundary {} }')
            elif 'rev-parse' in arguments: log.write_text(hashlib.sha1(directory.name.encode()).hexdigest() + '\n')
            return
        if arguments[1] == 'build':
            project = Path(arguments[2]); name = project.stem
            self.test.assertEqual(sum('checkout' in row for row in self.calls) % 3, 0)
            self.test.assertIn('-p:GameManaged=' + str(self.test.managed), arguments)
            self.test.assertIn('-p:IncludeSourceRevisionInInformationalVersion=false', arguments)
            if name == self.fail_build: raise state.WizardError('fixture_compile', 'Fixture build interrupted.')
            target = project.parent / 'bin/Release/net472' / (name + '.dll')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b'compiled pinned package ' + name.encode())

    def builds(self): return [row for row in self.calls if len(row) > 1 and row[1] == 'build']


class RuntimeDependencyCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='quest XR cache ')
        self.root = Path(self.temp.name); self.store = state.Store(self.root / 'owned')
        self.managed = self.root / 'Owned Game/Gloomhaven_Data/Managed'; self.managed.mkdir(parents=True)
        (self.managed / 'Unity.InputSystem.dll').write_bytes(b'original input system reference')
        saved = self.store.create({'gameRoot': str(self.managed.parent.parent)})
        self.session = saved['session']; self.logs = self.store.session_dir(self.session) / 'logs'; self.logs.mkdir()
        sdk_root = self.store.root / 'tools/dotnet-8.0.425'
        executable = sdk_root / 'dotnet'; executable.parent.mkdir(parents=True); executable.write_bytes(b'exact dotnet host')
        for name in SDK_FILES:
            path = sdk_root / 'sdk/8.0.425' / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(('SDK ' + name).encode())
        self.details = {'git': 'git', 'dotnet8': str(executable)}
        self.supervisor = SimulatedPinnedTools(self)

    def tearDown(self): self.temp.cleanup()

    def source(self, name):
        checkout = self.store.root / 'source' / (self.session + '-' + name)
        recipe = checkout / 'scripts/build-runtimedeps.sh'; recipe.parent.mkdir(parents=True)
        recipe.write_text('\n'.join('"' + name + ' ' + version + '"' for name, version in VERSIONS.items()))
        common = checkout / 'tools/RuntimeDepsBuild/Common.props'; common.parent.mkdir(parents=True)
        common.write_text('<Project><PropertyGroup><DebugType>embedded</DebugType></PropertyGroup></Project>')
        for assembly, package in xr.PACKAGES.items():
            project = common.parent / assembly / (assembly + '.csproj'); project.parent.mkdir()
            project.write_text('<Project><PropertyGroup><PackageSourceVersion>' + VERSIONS[package] + '</PackageSourceVersion></PropertyGroup>'
                               '<ItemGroup><Reference Include="Unity.InputSystem" HintPath="$(GameManaged)/Unity.InputSystem.dll"/></ItemGroup></Project>')
        state.atomic_json(checkout.with_name(checkout.name + '.owner.json'),
                          {'schema': 1, 'session': self.session, 'releaseHash': state.value_hash(name), 'commit': 'a' * 40})
        return checkout

    def derive(self, checkout):
        helper = types.SimpleNamespace(game_data=lambda _: self.managed.parent)
        with mock.patch.object(discovery, 'builder', return_value=helper):
            return provision.derive_runtime_dependencies(checkout, self.managed.parent.parent, self.details,
                                                        self.supervisor, self.logs, lambda: None)

    def cache(self):
        return next(path for path in (self.store.root / 'tools/xr-compile-inputs').iterdir() if path.is_dir())

    def test_new_release_and_profile_reuse_identical_manifest_and_binaries(self):
        first = self.source('release-one'); manifest = self.derive(first); original = manifest.read_bytes()
        old_outputs = {name: (first / 'libs/RuntimeDeps' / (name + '.dll')).read_bytes() for name in xr.NAMES}
        second = self.source('release-two'); mod = second / 'src/Mod.cs'; mod.parent.mkdir(); mod.write_text('changed mod, unchanged XR recipe')
        self.derive(second)
        self.assertEqual(len(self.supervisor.builds()), 3)
        self.assertEqual(sum('fetch' in row for row in self.supervisor.calls), 3)
        self.assertEqual((second / 'libs/RuntimeDeps/wizard-dependencies.json').read_bytes(), original)
        self.assertEqual({name: (second / 'libs/RuntimeDeps' / (name + '.dll')).read_bytes() for name in xr.NAMES}, old_outputs)
        self.assertEqual({row['source'] for row in json.loads(original)['assemblies'].values()}, {'declared-provisional-project'})
        with mock.patch.object(xr.shutil, 'copyfile', wraps=xr.shutil.copyfile) as copied:
            self.derive(second)
        copied.assert_not_called()

    def test_existing_declared_manifest_does_not_flip_to_supplied_provenance(self):
        source = self.source('standalone-source')
        self.supervisor.store = None; self.supervisor.session = None
        manifest = self.derive(source); raw = manifest.read_bytes()
        self.derive(source)
        self.assertEqual(manifest.read_bytes(), raw)
        self.assertEqual(len(self.supervisor.builds()), 3)
        self.assertEqual({row['source'] for row in json.loads(raw)['assemblies'].values()}, {'declared-provisional-project'})

    def test_partial_compile_receipts_do_not_repeat_successful_package(self):
        first = self.source('release-one'); self.supervisor.fail_build = xr.NAMES[1]
        with self.assertRaises(state.WizardError): self.derive(first)
        self.supervisor.fail_build = None; self.derive(self.source('release-two'))
        names = [Path(row[2]).stem for row in self.supervisor.builds()]
        self.assertEqual(names.count(xr.NAMES[0]), 1)
        self.assertEqual(names.count(xr.NAMES[1]), 2)
        self.assertEqual(names.count(xr.NAMES[2]), 1)
        self.assertEqual(sum('fetch' in row for row in self.supervisor.calls), 3)

    def test_warm_cache_uses_persistent_witnesses_without_payload_reads(self):
        first = self.source('release-one'); self.derive(first)
        cache = self.cache()
        payloads = {cache / (name + '.dll') for name in xr.NAMES}
        payloads.add(self.managed / 'Unity.InputSystem.dll')
        sdk_root = Path(self.details['dotnet8']).parent
        payloads.add(Path(self.details['dotnet8']))
        payloads.update(sdk_root / 'sdk/8.0.425' / name for name in SDK_FILES)
        payloads.update(first / 'libs/RuntimeDeps' / (name + '.dll') for name in xr.NAMES)
        state._receipt_storage()._invocation_file_proofs.clear()
        opened = Path.open
        def guarded(path, *args, **kwargs):
            if path in payloads and (args[0] if args else kwargs.get('mode', 'r')) == 'rb':
                raise AssertionError('Warm cache reread payload: ' + str(path))
            return opened(path, *args, **kwargs)
        with mock.patch.object(Path, 'open', guarded): self.derive(first)
        self.assertEqual(len(self.supervisor.builds()), 3)

    def test_changed_consumed_reference_rebuilds_only_small_xr_cache(self):
        self.derive(self.source('release-one'))
        (self.managed / 'Unity.InputSystem.dll').write_bytes(b'updated original game API')
        self.derive(self.source('release-two'))
        self.assertEqual(len(self.supervisor.builds()), 6)

    def test_changed_compiler_bytes_rebuild_small_cache_even_same_sdk_version(self):
        self.derive(self.source('release-one'))
        compiler = Path(self.details['dotnet8']).parent / 'sdk/8.0.425/Roslyn/bincore/csc.dll'
        compiler.write_bytes(b'changed compiler binary')
        self.derive(self.source('release-two'))
        self.assertEqual(len(self.supervisor.builds()), 6)

    def test_changed_project_or_common_settings_rebuild_small_cache(self):
        for suffix, name in (('common', 'Common.props'), ('project', 'Unity.XR.Management/Unity.XR.Management.csproj')):
            with self.subTest(name=name):
                source = self.source(suffix)
                original_count = len(self.supervisor.builds())
                path = source / 'tools/RuntimeDepsBuild' / name
                path.write_text(path.read_text() + '<!-- actual recipe change ' + suffix + ' -->')
                self.derive(source)
                self.assertEqual(len(self.supervisor.builds()), original_count + 3)

    def test_corrupt_one_cached_output_rebuilds_only_that_package(self):
        self.derive(self.source('release-one'))
        (self.cache() / (xr.NAMES[1] + '.dll')).write_bytes(b'corrupted cached DLL')
        self.derive(self.source('release-two'))
        self.assertEqual([Path(row[2]).stem for row in self.supervisor.builds()], [*xr.NAMES, xr.NAMES[1]])

    def test_missing_one_target_restores_from_cache_without_compilation(self):
        source = self.source('release-one'); self.derive(source)
        (source / 'libs/RuntimeDeps' / (xr.NAMES[1] + '.dll')).unlink()
        self.derive(source)
        self.assertEqual(len(self.supervisor.builds()), 3)

    def test_unknown_cached_output_path_is_rejected(self):
        self.derive(self.source('release-one')); receipt = self.cache() / 'dependencies.json'
        value = state.read_json(receipt); value['outputs'][xr.NAMES[0]]['path'] = '../outside.dll'; state.atomic_json(receipt, value)
        with self.assertRaises(state.WizardError) as error: self.derive(self.source('release-two'))
        self.assertEqual(error.exception.code, 'runtime_cache_identity')

    def test_corrupt_witness_database_loses_only_metadata_not_compiled_results(self):
        first = self.source('release-one'); self.derive(first)
        database = self.store.root / 'tools/xr-compile-inputs/.xr-file-witnesses.sqlite3'
        database.write_bytes(b'invalid SQLite cache')
        self.derive(self.source('release-two'))
        self.assertEqual(len(self.supervisor.builds()), 3)

    def test_ambiguous_pathmap_delimiters_are_rejected_before_execution(self):
        for char in (',', ';', '='):
            with self.subTest(character=char):
                with self.assertRaises(state.WizardError) as error:
                    xr.deterministic_options(Path('/owned' + char + 'project'), Path('/game'))
                self.assertEqual(error.exception.code, 'runtime_build_path')

    def test_changed_package_sources_after_interruption_reject_receipt(self):
        self.supervisor.fail_build = xr.NAMES[1]
        with self.assertRaises(state.WizardError): self.derive(self.source('release-one'))
        source = self.cache() / 'build/tools/RuntimeDepsBuild/sources' / xr.PACKAGES[xr.NAMES[0]] / 'Runtime/Boundary.cs'
        source.write_text('changed consumed package source')
        self.supervisor.fail_build = None
        with self.assertRaises(state.WizardError) as error: self.derive(self.source('release-two'))
        self.assertEqual(error.exception.code, 'runtime_source_changed')

    def test_project_version_mismatch_stops_before_compilation(self):
        source = self.source('release-one')
        path = source / 'tools/RuntimeDepsBuild/Unity.XR.Management/Unity.XR.Management.csproj'
        path.write_text(path.read_text().replace('4.5.0', '4.6.0'))
        with self.assertRaises(state.WizardError) as error: self.derive(source)
        self.assertEqual(error.exception.code, 'runtime_recipe'); self.assertFalse(self.supervisor.builds())

    def legacy(self):
        """Exact old Wizard outputs + original build/tool input manifests."""
        old = self.source('previous-owned-release')
        state.atomic_json(self.store.session_dir(self.session) / 'tools.json', {'schema': 1, **self.details})
        (self.logs / 'dotnet8-version.log').write_text('8.0.425\n')
        tool_paths = [Path(self.details['dotnet8']), *(Path(self.details['dotnet8']).parent / 'sdk/8.0.425' / name for name in SDK_FILES)]
        state.atomic_json(self.store.receipt(self.session, 'tools'),
                          {'schema': 1, 'outputs': [record(path, path.relative_to(self.store.root).as_posix()) for path in tool_paths]})
        dependencies = {}
        for name in xr.NAMES:
            target = old / 'libs/RuntimeDeps' / (name + '.dll'); target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b'previous release-specific embedded-PDB ' + name.encode())
            dependencies[name] = {'sha256': state.digest(target), 'source': 'declared-provisional-project',
                                  'package': xr.PACKAGES[name], 'version': VERSIONS[xr.PACKAGES[name]]}
        manifest = old / 'libs/RuntimeDeps/wizard-dependencies.json'
        state.atomic_json(manifest, {'schema': 1, 'kind': xr.KIND, 'assemblies': dependencies})
        mods = xr.recipe_inputs(old) + [record(path, path.relative_to(old).as_posix()) for path in (old / 'libs/RuntimeDeps').iterdir()]
        game = [record(self.managed / 'Unity.InputSystem.dll', 'Managed/Unity.InputSystem.dll')]
        inputs = {'schema': 1, 'game': {'files': game}, 'mod': {'files': mods}}
        inputs['inputKey'] = state.value_hash(inputs)
        relative = 'manifests/' + inputs['inputKey'] + '.json'
        state.atomic_json(self.store.root / 'build' / relative, inputs)
        state.atomic_json(self.store.root / 'build/latest-input.json',
                          {'schema': 1, 'sourceRepo': str(old), 'sourceGameRoot': str(self.managed.parent), 'manifest': relative})
        return old, manifest

    def test_previous_owned_exact_outputs_adopt_without_network_or_recompile(self):
        old, manifest = self.legacy(); expected = manifest.read_bytes()
        current = self.source('new-release'); new = self.derive(current)
        self.assertEqual(new.read_bytes(), expected)
        self.assertFalse(self.supervisor.builds()); self.assertFalse(any('-C' in row for row in self.supervisor.calls))
        self.assertTrue(state.read_json(self.cache() / 'dependencies.json')['adopted'])
        for name in xr.NAMES:
            self.assertEqual((current / 'libs/RuntimeDeps' / (name + '.dll')).read_bytes(), (old / 'libs/RuntimeDeps' / (name + '.dll')).read_bytes())

    def test_legacy_changed_consumed_reference_cannot_adopt(self):
        self.legacy(); (self.managed / 'Unity.InputSystem.dll').write_bytes(b'changed original')
        self.derive(self.source('new-release')); self.assertEqual(len(self.supervisor.builds()), 3)

    def test_legacy_modified_dll_cannot_adopt(self):
        old, _ = self.legacy(); (old / 'libs/RuntimeDeps' / (xr.NAMES[0] + '.dll')).write_bytes(b'changed input')
        self.derive(self.source('new-release')); self.assertEqual(len(self.supervisor.builds()), 3)

    def test_legacy_compiler_change_cannot_adopt(self):
        self.legacy(); Path(self.details['dotnet8']).write_bytes(b'changed dotnet host same version')
        self.derive(self.source('new-release')); self.assertEqual(len(self.supervisor.builds()), 3)

    def test_legacy_unowned_original_directory_cannot_adopt(self):
        old, _ = self.legacy(); old.with_name(old.name + '.owner.json').unlink()
        self.derive(self.source('new-release')); self.assertEqual(len(self.supervisor.builds()), 3)


class ActualCompilerDeterminismTests(unittest.TestCase):
    def test_pathmap_and_revision_flags_produce_same_embedded_pdb_dll(self):
        executable = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        if not Path(executable).is_file(): self.skipTest('Actual .NET SDK is unavailable.')
        with tempfile.TemporaryDirectory(prefix='quest compiler paths ') as temporary:
            root = Path(temporary); outputs, uncontrolled = [], []
            for index, folder in enumerate(('first release', 'other release checkout'), 1):
                source = root / folder; source.mkdir()
                subprocess.run(['git', 'init', '-q', str(source)], check=True)
                project = source / 'PinnedDependency.csproj'
                project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
                                   '<DebugType>embedded</DebugType><InformationalVersion>1.2.3-provisional</InformationalVersion>'
                                   '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
                                   '<ItemGroup><Compile Include="Runtime.cs" /></ItemGroup></Project>')
                (source / 'Runtime.cs').write_text('public class XRReference { public static int Value => 7; }')
                # The parent checkout path and Git commit differ; assembly source does not.
                (source / 'release-marker').write_text(str(index))
                subprocess.run(['git', '-C', str(source), 'add', '.'], check=True)
                subprocess.run(['git', '-C', str(source), '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid',
                                'commit', '-qm', 'Different source release'], check=True)
                result = subprocess.run([executable, 'build', str(project), '-c', 'Release', '--nologo', '-v', 'quiet',
                                         *xr.deterministic_options(source, root / 'original Managed')],
                                        text=True, capture_output=True, timeout=90)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                outputs.append((source / 'bin/Release/net8.0/PinnedDependency.dll').read_bytes())
                # Causal control: native checkout-path/Git provenance differs
                # without the actual flags used by the production producer.
                result = subprocess.run([executable, 'build', str(project), '-c', 'Release', '--nologo', '-v', 'quiet',
                                         '-t:Rebuild', '-p:UseSharedCompilation=false'],
                                        text=True, capture_output=True, timeout=90)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                uncontrolled.append((source / 'bin/Release/net8.0/PinnedDependency.dll').read_bytes())
            self.assertEqual(hashlib.sha256(outputs[0]).hexdigest(), hashlib.sha256(outputs[1]).hexdigest())
            self.assertNotEqual(hashlib.sha256(uncontrolled[0]).hexdigest(), hashlib.sha256(uncontrolled[1]).hexdigest())


if __name__ == '__main__': unittest.main()
