"""Owned XR compile-input cache, independent of mod release and source path.

These are references for compiling the mod, not Android runtime package DLLs.
The 2026-10-09 capture rebuilt the same packages under a new release directory;
embedded PDB paths changed their bytes and invalidated 27 asset preparations.
Keep the exact recipe, compiler and consumed game references qualified instead.
"""
from __future__ import annotations

from contextlib import contextmanager
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import uuid

from state import WizardError, _receipt_storage, atomic_json, digest, ordinary, read_json, value_hash

NAMES = ('Unity.XR.Management', 'Unity.XR.CoreUtils', 'Unity.XR.OpenXR')
PACKAGES = dict(zip(NAMES, ('com.unity.xr.management', 'com.unity.xr.core-utils', 'com.unity.xr.openxr')))
KIND = 'compile-time dependency inputs; Quest uses Android PlayerSdk packages'
CACHE_OWNER = 'GloomhavenVR.QuestWizard.XRCompileInputs'
CACHE_CONTRACT = 1
MAPPED_SOURCE, MAPPED_MANAGED = '/_/quest-xr', '/_/game-managed'
COMPILER_FLAGS = ('-p:Deterministic=true', '-p:IncludeSourceRevisionInInformationalVersion=false',
                  '-p:EnableSourceControlManagerQueries=false', '-p:EnableSourceLink=false',
                  '-p:UseSharedCompilation=false')


def recipe_inputs(checkout):
    """Select actual MSBuild inputs, not the containing mod commit or profile."""
    checkout = Path(checkout)
    names = ['scripts/build-runtimedeps.sh', 'tools/RuntimeDepsBuild/Common.props']
    names += ['tools/RuntimeDepsBuild/' + name + '/' + name + '.csproj' for name in NAMES]
    names += [name for name in ('Directory.Build.props', 'Directory.Build.targets', 'global.json', 'nuget.config')
              if (checkout / name).is_file()]
    result = []
    for name in names:
        path = ordinary(checkout / name)
        if not path.is_file(): raise WizardError('runtime_recipe', 'The XR compilation recipe is incomplete: ' + name)
        result.append({'path': name, 'size': path.stat().st_size, 'sha256': digest(path)})
    return result


def reference_names(checkout):
    """Reject newly introduced unqualified reference paths rather than guessing."""
    result = set()
    for name in NAMES:
        raw = (Path(checkout) / 'tools/RuntimeDepsBuild' / name / (name + '.csproj')).read_text(encoding='utf-8')
        for hint in re.findall(r'HintPath="([^"]+)"', raw):
            match = re.fullmatch(r'\$\(GameManaged\)/([A-Za-z0-9_.-]+\.dll)', hint)
            if not match: raise WizardError('runtime_recipe', 'The XR project declares an unsupported reference path.')
            result.add(match[1])
    return sorted(result)


@contextmanager
def witnesses(root, namespace):
    """Reuse strong byte proofs under the existing Windows change-stamp rules."""
    root = ordinary(root); root.mkdir(parents=True, exist_ok=True)
    database = ordinary(root / '.xr-file-witnesses.sqlite3')
    for path in (database, database.with_name(database.name + '-journal')):
        ordinary(path)
        if path.exists() and (not path.is_file() or path.stat().st_nlink != 1):
            raise WizardError('runtime_cache_identity', 'The XR witness database is not a regular owned file.')
    connection = sqlite3.connect(database)
    try:
        try:
            connection.execute('SELECT 1 FROM sqlite_master LIMIT 1').fetchone()
        except sqlite3.DatabaseError as error:
            if getattr(error, 'sqlite_errorcode', None) not in (sqlite3.SQLITE_CORRUPT, sqlite3.SQLITE_NOTADB): raise
            connection.close(); ordinary(database); database.unlink()
            connection = sqlite3.connect(database)
        with connection:
            yield lambda owned, purpose: _receipt_storage().ValidatedFileWitnesses(
                connection, owned, {'owner': CACHE_OWNER, 'scope': namespace, 'purpose': purpose})
    finally: connection.close()


def compiler_inputs(details, supervisor, logs, env, qualified):
    executable = ordinary(details['dotnet8'])
    if not executable.is_file(): raise WizardError('runtime_compiler', 'The selected .NET executable is missing.')
    log = logs / 'xr-sdk-version.log'
    supervisor.run([executable, '--version'], log, env=env)
    version = log.read_text(encoding='utf-8').strip()
    if not re.fullmatch(r'8\.0\.\d+', version):
        raise WizardError('runtime_compiler', 'The XR dependency cache requires the selected .NET 8 SDK.')
    sdk = ordinary(executable.parent / 'sdk' / version)
    names = ['MSBuild.dll', 'Roslyn/bincore/csc.dll', 'Roslyn/bincore/Microsoft.CodeAnalysis.dll',
             'Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll',
             'Sdks/Microsoft.NET.Sdk/Sdk/Sdk.props', 'Sdks/Microsoft.NET.Sdk/Sdk/Sdk.targets']
    observer = qualified(executable.parent, 'compiler')
    rows = [{'path': 'dotnet', 'sha256': observer.observe(executable)}]
    for name in names:
        path = ordinary(sdk / name)
        if not path.is_file(): raise WizardError('runtime_compiler', 'The selected SDK is incomplete: ' + name)
        rows.append({'path': name, 'sha256': observer.observe(path)})
    return {'version': version, 'files': rows}


def deterministic_options(build_root, managed):
    # MSBuild's escaped comma keeps this one property when either owned root
    # contains a space. Reject ambiguous mapping delimiters before invocation.
    for path in (build_root, managed):
        if any(c in str(path) for c in (',', ';', '=')):
            raise WizardError('runtime_build_path', 'XR compiler paths cannot contain comma, semicolon or equals signs.')
    return ['-p:GameManaged=' + str(managed),
            '-p:PathMap=' + str(build_root) + '=' + MAPPED_SOURCE + '%2C' + str(managed) + '=' + MAPPED_MANAGED,
            *COMPILER_FLAGS]


def _publish(source, target, expected, observer):
    target = ordinary(target); target.parent.mkdir(parents=True, exist_ok=True)
    if target.is_file() and observer.qualify(target, expected['sha256'], expected['size']): return
    temp = ordinary(target.with_name(target.name + '.' + uuid.uuid4().hex + '.tmp'))
    try:
        shutil.copyfile(source, temp)
        if digest(temp) != expected['sha256']: raise WizardError('runtime_changed', 'An XR compile dependency changed while copied.')
        os.replace(temp, target); observer.remember(target, expected['sha256'])
    finally: temp.unlink(missing_ok=True)


def _legacy(checkout, managed, store, session, recipe, references, compiler, details, packages, qualified):
    """Adopt only a byte-qualified previous build's own declared XR outputs.

    The original input manifest ties the outputs to their exact package projects
    and consumed Managed references. A directory full of similarly named DLLs
    or a user's installed mod cannot provide this evidence.
    """
    pointer = ordinary(store.root / 'build/latest-input.json')
    if not pointer.is_file(): return None
    try:
        value = json.loads(pointer.read_text(encoding='utf-8'))
        relative = value.get('manifest', '')
        if not re.fullmatch(r'manifests/[0-9a-f]{64}\.json', relative): return None
        previous = ordinary(value.get('sourceRepo', ''))
        if (previous == checkout or previous.parent != ordinary(store.root / 'source')
                or ordinary(value.get('sourceGameRoot', '')) != managed.parent): return None
        owner = read_json(previous.with_name(previous.name + '.owner.json'))
        if owner.get('session') != session or not re.fullmatch(r'[0-9a-f]{64}', str(owner.get('releaseHash'))): return None
        tools = read_json(store.session_dir(session) / 'tools.json')
        previous_sdk = Path(tools['dotnet8'])
        # An earlier tools-stage receipt qualifies this exact selected compiler.
        # Never infer a previous compiler from package versions alone.
        old_version = (store.session_dir(session) / 'logs/dotnet8-version.log').read_text(encoding='utf-8').strip()
        if previous_sdk != Path(details['dotnet8']) or old_version != compiler['version']: return None
        tool_receipt = ordinary(store.receipt(session, 'tools'))
        tool_rows = {row['path']: row for row in read_json(tool_receipt, limit=64 * 1048576)['outputs']}
        for row in compiler['files']:
            compiled_path = (previous_sdk if row['path'] == 'dotnet' else
                             previous_sdk.parent / 'sdk' / compiler['version'] / row['path'])
            recorded = tool_rows.get(compiled_path.relative_to(store.root).as_posix())
            if not recorded or recorded['sha256'] != row['sha256']: return None
        manifest = read_json(store.root / 'build' / relative, limit=64 * 1048576)
        if (manifest.get('inputKey') != Path(relative).stem
                or value_hash({k: v for k, v in manifest.items() if k != 'inputKey'}) != manifest['inputKey']): return None
        mods = {row['path']: row for row in manifest['mod']['files']}
        game = {row['path']: row for row in manifest['game']['files']}
        for row in recipe:
            if mods.get(row['path']) != row or digest(ordinary(previous / row['path'])) != row['sha256']: return None
        for row in references:
            if game.get('Managed/' + row['path']) != {**row, 'path': 'Managed/' + row['path']}: return None
        dependency = ordinary(previous / 'libs/RuntimeDeps/wizard-dependencies.json')
        if dependency.stat().st_mtime_ns < tool_receipt.stat().st_mtime_ns: return None
        record = mods.get('libs/RuntimeDeps/wizard-dependencies.json')
        if not record or digest(dependency) != record['sha256']: return None
        provenance = read_json(dependency)
        if provenance.get('kind') != KIND or set(provenance.get('assemblies', {})) != set(NAMES): return None
        rows = {}; observer = qualified(previous, 'legacy-xr-output')
        for name in NAMES:
            row = mods.get('libs/RuntimeDeps/' + name + '.dll'); declared = provenance['assemblies'][name]
            if (not row or declared.get('sha256') != row['sha256']
                    or declared.get('source') != 'declared-provisional-project'
                    or declared.get('package') != PACKAGES[name]
                    or declared.get('version') != packages[PACKAGES[name]]
                    or not observer.qualify(previous / row['path'], row['sha256'], row['size'])): return None
            rows[name] = {'path': name + '.dll', 'size': row['size'], 'sha256': row['sha256']}
        return previous, provenance, rows
    except (OSError, ValueError, KeyError, TypeError, WizardError): return None


def derive(checkout, managed, details, supervisor, logs, check_cancel, packages, env):
    """Materialize the same qualified three DLLs in each immutable release copy."""
    checkout, managed, logs = ordinary(checkout), ordinary(managed), ordinary(logs)
    store, session = supervisor.store, supervisor.session
    logs.mkdir(parents=True, exist_ok=True)
    root = ordinary(store.root / 'tools/xr-compile-inputs'); root.mkdir(parents=True, exist_ok=True)
    root_owner = root / 'wizard-xr-owner.json'
    expected_owner = {'schema': 1, 'owner': CACHE_OWNER}
    if root_owner.is_file():
        if read_json(root_owner) != expected_owner:
            raise WizardError('unowned_runtime_cache', 'The XR compile-input cache has another owner.')
    else:
        if any(root.iterdir()): raise WizardError('unowned_runtime_cache', 'The XR compile-input cache has no ownership record.')
        atomic_json(root_owner, expected_owner)
    recipe = recipe_inputs(checkout)
    for name in NAMES:
        raw = (checkout / 'tools/RuntimeDepsBuild' / name / (name + '.csproj')).read_text(encoding='utf-8')
        version = re.search(r'<PackageSourceVersion>([^<]+)</PackageSourceVersion>', raw)
        if not version or version[1] != packages[PACKAGES[name]]:
            raise WizardError('runtime_recipe', 'XR source package/project versions disagree.')
    with witnesses(root, CACHE_CONTRACT) as qualified:
        compiler = compiler_inputs(details, supervisor, logs, env, qualified)
        reference_observer = qualified(managed, 'game-references')
        references = []
        for name in reference_names(checkout):
            path = ordinary(managed / name)
            if not path.is_file(): raise WizardError('runtime_reference', 'The owned game is missing an XR compile reference: ' + name)
            references.append({'path': name, 'size': path.stat().st_size, 'sha256': reference_observer.observe(path)})
        identity = {'schema': 1, 'contract': CACHE_CONTRACT, 'recipe': recipe,
                    'packages': packages, 'compiler': compiler, 'references': references,
                    'compilerFlags': list(COMPILER_FLAGS), 'mappedRoots': [MAPPED_SOURCE, MAPPED_MANAGED]}
        key = value_hash(identity); cache = ordinary(root / key)
        marker = cache / 'owner.json'
        if cache.exists():
            if not marker.is_file() or read_json(marker) != {'schema': 1, 'owner': CACHE_OWNER, 'key': key}:
                raise WizardError('unowned_runtime_cache', 'The XR compile-input cache has another owner.')
        else:
            cache.mkdir(); atomic_json(marker, {'schema': 1, 'owner': CACHE_OWNER, 'key': key})
        receipt = cache / 'dependencies.json'
        saved = read_json(receipt) if receipt.is_file() else {'schema': 1, 'key': key, 'inputs': identity,
                                                           'outputs': {}, 'assemblies': {}, 'packages': {}}
        if saved.get('key') != key or saved.get('inputs') != identity:
            raise WizardError('runtime_cache_identity', 'The XR dependency receipt differs from its selected recipe.')
        if (not all(isinstance(saved.get(name), dict) for name in ('outputs', 'assemblies', 'packages'))
                or not set(saved['outputs']) <= set(NAMES)
                or set(saved['outputs']) != set(saved['assemblies'])
                or not set(saved['packages']) <= set(PACKAGES.values())):
            raise WizardError('runtime_cache_identity', 'The XR dependency receipt has an invalid assembly set.')
        for name, row in saved['outputs'].items():
            if (not isinstance(row, dict) or row.get('path') != name + '.dll'
                    or type(row.get('size')) is not int or row['size'] <= 0
                    or not re.fullmatch(r'[0-9a-f]{64}', str(row.get('sha256')))
                    or not isinstance(saved['assemblies'][name], dict)
                    or saved['assemblies'][name].get('sha256') != row['sha256']):
                raise WizardError('runtime_cache_identity', 'The XR dependency receipt has an invalid output record.')
        observer = qualified(cache, 'compiled-output')
        for name in list(saved['outputs']):
            row = saved['outputs'][name]; path = ordinary(cache / row['path'])
            if not path.is_file() or not observer.qualify(path, row['sha256'], row['size']):
                saved['outputs'].pop(name); saved['assemblies'].pop(name, None)
        if not saved['outputs']:
            previous = _legacy(checkout, managed, store, session, recipe, references, compiler, details, packages, qualified)
            if previous:
                old, provenance, rows = previous
                for name, row in rows.items(): _publish(old / 'libs/RuntimeDeps' / row['path'], cache / row['path'], row, observer)
                saved.update(outputs=rows, assemblies=provenance['assemblies'], adopted=True)
                atomic_json(receipt, saved)
        built_count = 0
        if len(saved['outputs']) != len(NAMES):
            build = ordinary(cache / 'build'); build.mkdir(parents=True, exist_ok=True)
            # Only a handful of declared build inputs are copied, never the mod,
            # original assets, Unity project, profile or old release checkout.
            for row in recipe:
                target = ordinary(build / row['path']); target.parent.mkdir(parents=True, exist_ok=True)
                if not target.is_file() or digest(target) != row['sha256']: shutil.copyfile(checkout / row['path'], target)
            package_root = build / 'tools/RuntimeDepsBuild/sources'
            for name in NAMES:
                check_cancel(); package = PACKAGES[name]; version = packages[package]
                source = ordinary(package_root / package); source.mkdir(parents=True, exist_ok=True)
                if not (source / '.git').is_dir():
                    supervisor.run([details['git'], '-C', source, 'init', '-q'], logs / (package + '-init.log'), env=env)
                if not (source / 'package.json').is_file():
                    supervisor.run([details['git'], '-C', source, 'fetch', '--depth', '1',
                                    'https://github.com/needle-mirror/' + package + '.git', 'refs/tags/' + version],
                                   logs / (package + '-fetch.log'), env=env)
                    supervisor.run([details['git'], '-C', source, 'checkout', '--detach', 'FETCH_HEAD'],
                                   logs / (package + '-checkout.log'), env=env)
                if json.loads((source / 'package.json').read_text(encoding='utf-8')).get('version') != version:
                    raise WizardError('runtime_recipe', 'The retained XR package source has another version.')
                commit_log = logs / (package + '-commit.log')
                supervisor.run([details['git'], '-C', source, 'rev-parse', 'HEAD'], commit_log, env=env)
                commit = commit_log.read_text(encoding='utf-8').strip()
                if not re.fullmatch(r'[0-9a-f]{40}', commit): raise WizardError('runtime_recipe', 'The XR package commit is invalid.')
                old_package = saved['packages'].get(package)
                source_observer = qualified(source, 'package-source-' + commit)
                consumed = [{'path': path.relative_to(source).as_posix(), 'size': path.stat().st_size,
                             'sha256': source_observer.observe(path)}
                            for path in sorted((source / 'Runtime').rglob('*.cs'))]
                package_identity = {'version': version, 'commit': commit, 'files': consumed}
                if old_package is not None and old_package != package_identity:
                    raise WizardError('runtime_source_changed', 'An owned XR package source changed; its retained compile inputs cannot be reused.')
                saved['packages'][package] = package_identity; atomic_json(receipt, saved)
            store.operation(session, 'source', 'xr-build', detail='Compiling missing XR references; completed packages are retained')
            for index, name in enumerate(NAMES, 1):
                check_cancel()
                if name not in saved['outputs']:
                    project = build / 'tools/RuntimeDepsBuild' / name / (name + '.csproj')
                    supervisor.run([details['dotnet8'], 'build', project, '-c', 'Release', '--nologo', '-v', 'quiet',
                                    *deterministic_options(build, managed)], logs / (PACKAGES[name] + '-build.log'), env=env)
                    check_cancel(); built = ordinary(project.parent / 'bin/Release/net472' / (name + '.dll'))
                    row = {'path': name + '.dll', 'size': built.stat().st_size, 'sha256': digest(built)}
                    _publish(built, cache / row['path'], row, observer)
                    saved['outputs'][name] = row
                    saved['assemblies'][name] = {'sha256': row['sha256'], 'source': 'declared-provisional-project',
                                                'package': PACKAGES[name], 'version': packages[PACKAGES[name]]}
                    built_count += 1
                    atomic_json(receipt, saved)
                store.progress(session, 'source', 'xr-build', index, len(NAMES), 'assemblies', name)
        targets = checkout / 'libs/RuntimeDeps'; targets.mkdir(parents=True, exist_ok=True)
        target_observer = qualified(checkout, 'materialized-output-' + key)
        for name in NAMES:
            check_cancel(); row = saved['outputs'][name]
            _publish(cache / row['path'], targets / row['path'], row, target_observer)
        manifest = targets / 'wizard-dependencies.json'
        expected = {'schema': 1, 'kind': KIND, 'assemblies': saved['assemblies']}
        if not manifest.is_file() or read_json(manifest) != expected: atomic_json(manifest, expected)
        store.record(session, 'runtime_dependencies_ready', 'source', cacheKey=key,
                     assemblies=len(NAMES), builtAssemblies=built_count, adopted=bool(saved.get('adopted')),
                     packageCommits={name: row['commit'] for name, row in saved['packages'].items()})
        store.operation(session, 'source', 'xr-build', complete=True, detail='Qualified XR compile-time dependencies retained')
        return manifest
