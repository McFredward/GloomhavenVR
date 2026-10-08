"""Pinned user-local tools; resumable verified downloads and owned extraction."""
from __future__ import annotations
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import shutil
import sys
import platform
import posixpath
import stat
import tarfile
import discovery
import urllib.request
import zipfile

from state import WizardError, atomic_json, digest, ordinary, read_json, value_hash

HERE = Path(__file__).resolve().parent
LOCK = json.loads((HERE / "tools.lock.json").read_text(encoding="utf-8"))


def host_key():
    machine = platform.machine().lower()
    if machine not in ('amd64', 'x86_64'):
        raise WizardError('host_architecture', 'Unity 2021.3 Android builds require an x86-64 Windows or Linux host.',
                          'Unity 2021.3 benötigt zum Bauen einen Windows- oder Linux-Rechner mit x86-64-Prozessor.')
    if os.name == 'nt': return 'windows-x64'
    if sys.platform.startswith('linux'): return 'linux-x64'
    raise WizardError('host_platform', 'Automatic build tool setup supports Windows and Linux x86-64.')


def spec_for(name):
    return LOCK[name] if host_key() == 'windows-x64' else LOCK['linux'][name]


def download(spec, destination, check_cancel=lambda: None, progress=lambda done, total: None, opener=urllib.request.urlopen):
    """Retain incomplete bytes; Range responses must match the requested offset."""
    destination = ordinary(destination); destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if digest(destination, spec["algorithm"]) != spec["hash"]:
            raise WizardError("tool_checksum", "A cached tool differs from its pinned checksum.", "Ein zwischengespeichertes Werkzeug stimmt nicht mit der Prüfsumme überein.")
        progress(destination.stat().st_size, destination.stat().st_size); return destination
    partial = ordinary(destination.with_name(destination.name + ".partial"))
    offset = partial.stat().st_size if partial.exists() else 0
    # A process can die after the final byte and before atomic publication.
    # Complete verified bytes need no second HTTP request (often answered 416).
    if offset and (spec.get("size") is None or offset == spec["size"]) and digest(partial, spec["algorithm"]) == spec["hash"]:
        check_cancel(); os.replace(partial, destination)
        progress(destination.stat().st_size, destination.stat().st_size); return destination
    headers = {"User-Agent": "GloomhavenVR-Quest-Wizard", "Accept-Encoding": "identity"}
    if offset: headers["Range"] = "bytes=" + str(offset) + "-"
    request = urllib.request.Request(spec["url"], headers=headers)
    check_cancel()
    with opener(request, timeout=60) as response:
        code = response.status
        if code == 206:
            match = re.fullmatch(r"bytes (\d+)-(\d+)/(\d+)", response.headers.get("Content-Range", ""))
            if not match or int(match[1]) != offset or int(match[2]) < offset or int(match[2]) >= int(match[3]):
                raise WizardError("download_range", "Download continuation did not match the retained bytes.")
            total = int(match[3])
        elif code == 200:
            offset = 0
            total = int(response.headers.get("Content-Length", "0")) or spec.get("size")
        else: raise WizardError("download_response", "The tool server returned an unsupported download response.")
        progress(offset, total)
        with partial.open("ab" if offset else "wb") as stream:
            done = offset
            while True:
                check_cancel(); block = response.read(1048576)
                if not block: break
                stream.write(block); done += len(block)
                if total and done > total: raise WizardError("download_range", "Tool response exceeded its declared size.")
                progress(done, total)
            stream.flush(); os.fsync(stream.fileno())
    check_cancel()
    if (spec.get("size") is not None and done != spec["size"]) or digest(partial, spec["algorithm"]) != spec["hash"]:
        partial.unlink(missing_ok=True)
        raise WizardError("tool_checksum", "A downloaded tool failed its pinned checksum.", "Ein heruntergeladenes Werkzeug hat eine falsche Prüfsumme.")
    os.replace(partial, destination)
    return destination


def _archive_name(name):
    # Tar archives commonly prefix every member with ./; remove that prefix only.
    while name.startswith('./'): name = name[2:]
    name = name.rstrip('/')
    parts = PurePosixPath(name).parts
    if (not parts or PurePosixPath(name).is_absolute() or PureWindowsPath(name).is_absolute()
            or any(p in ('.', '..') or ':' in p for p in parts) or "\\" in name):
        raise WizardError('tool_archive', 'Tool archive has an unsafe member path.')
    return name


def _mode(target, mode):
    if os.name != 'nt': target.chmod((mode & 0o777) | 0o400)


def _tar_owned(archive, destination, check_cancel, progress):
    """Extract regular files first, then validated internal links; never use extractall."""
    with tarfile.open(archive, 'r:*') as package:
        members = package.getmembers()
        if len(members) > 100000 or sum(row.size for row in members) > 8 * 1024**3:
            raise WizardError('tool_archive', 'Tool archive exceeds its supported bounds.')
        rows = {}; links = {}
        for row in members:
            if row.name.rstrip('/') in ('.', '') and row.isdir(): continue
            name = _archive_name(row.name)
            if name in rows or not (row.isfile() or row.isdir() or row.issym() or row.islnk()):
                raise WizardError('tool_archive', 'Tool archive has duplicate or unsupported members.')
            rows[name] = row
            if row.issym() or row.islnk():
                raw = row.linkname
                if not raw or PurePosixPath(raw).is_absolute() or PureWindowsPath(raw).is_absolute() or "\\" in raw or ':' in raw:
                    raise WizardError('tool_archive', 'Tool archive link has an unsafe target.')
                target = posixpath.normpath(posixpath.join(posixpath.dirname(name), raw) if row.issym() else raw)
                if target in ('.', '..') or target.startswith('../'):
                    raise WizardError('tool_archive', 'Tool archive link escapes the owned tool directory.')
                links[name] = target
        for name, row in rows.items():
            if any(parent.as_posix() in links for parent in PurePosixPath(name).parents):
                raise WizardError('tool_archive', 'Tool archive writes through a linked parent.')
        def resolve(name, seen=()):
            if name in seen or len(seen) > 40:
                raise WizardError('tool_archive', 'Tool archive link has a cycle.')
            if name in links: return resolve(links[name], (*seen, name))
            if name not in rows: raise WizardError('tool_archive', 'Tool archive link target is missing.')
            return name
        for name in links: resolve(name)
        total = sum(not row.isdir() for row in rows.values()); done = 0; progress(done, total)
        for name, row in rows.items():
            if name in links: continue
            check_cancel(); target = ordinary(destination / name)
            if row.isdir(): target.mkdir(parents=True, exist_ok=True); continue
            target.parent.mkdir(parents=True, exist_ok=True)
            # Compare exact bytes while reading the verified archive. An interrupted
            # extraction retains completed files without trusting their size alone.
            source = package.extractfile(row)
            import hashlib
            sha = hashlib.sha256()
            temp = ordinary(target.with_name(target.name + '.extracting'))
            with source, temp.open('wb') as output:
                while True:
                    check_cancel(); block = source.read(1048576)
                    if not block: break
                    sha.update(block); output.write(block)
                output.flush(); os.fsync(output.fileno())
            if target.is_file() and target.stat().st_size == row.size and digest(target) == sha.hexdigest():
                temp.unlink()
            else: os.replace(temp, target)
            _mode(target, row.mode); done += 1; progress(done, total)
        # Symlinks remain symlinks (JDK and native SDK loaders depend on them).
        # Hard links are copied to ordinary files so state receipts have no shared inode.
        for name, linked in links.items():
            check_cancel(); row = rows[name]; target = destination / name
            ordinary(target.parent); target.parent.mkdir(parents=True, exist_ok=True)
            if target.is_symlink():
                if row.issym() and os.readlink(target) == row.linkname:
                    done += 1; progress(done, total); continue
                raise WizardError('tool_archive', 'An extracted tool link changed; retained for review.')
            if target.exists():
                if row.issym(): raise WizardError('tool_archive', 'An extracted tool link was replaced.')
                ordinary(target)
            source = ordinary(destination / resolve(name))
            if row.issym(): os.symlink(row.linkname, target, target_is_directory=source.is_dir())
            else:
                if not source.is_file(): raise WizardError('tool_archive', 'Tool hard link target is not a regular file.')
                shutil.copyfile(source, target); _mode(target, rows[resolve(name)].mode)
            done += 1; progress(done, total)
        return {name: rows[name].linkname for name in links if rows[name].issym()}


def extract_owned(archive, destination, spec, check_cancel=lambda: None, progress=lambda done, total: None):
    """Resume the same pinned extraction; no unowned directory deletion."""
    destination = ordinary(destination); key = value_hash(spec)
    marker = destination / 'wizard-tool.json'
    if destination.exists():
        if not marker.is_file() or read_json(marker).get('key') != key:
            raise WizardError('unowned_tool', 'Tool extraction belongs to another input; it was retained.')
    else:
        destination.mkdir(parents=True)
        atomic_json(marker, {'schema': 1, 'key': key, 'complete': False})
    links = {}
    if spec.get('archive') in ('tar.gz', 'tar.xz', 'tar'):
        links = _tar_owned(archive, destination, check_cancel, progress)
    else:
        with zipfile.ZipFile(archive) as package:
            members = package.infolist()
            if len(members) > 100000 or sum(row.file_size for row in members) > 8 * 1024**3:
                raise WizardError('tool_archive', 'Tool archive exceeds its supported bounds.')
            seen = set()
            total = sum(not row.is_dir() for row in members); done = 0; progress(done, total)
            for row in members:
                name = _archive_name(row.filename)
                identity = name.casefold() if os.name == 'nt' else name
                mode = row.external_attr >> 16
                if identity in seen or stat.S_ISLNK(mode):
                    raise WizardError('tool_archive', 'Tool archive has unsafe or duplicate members.')
                seen.add(identity); target = ordinary(destination / name)
                if row.is_dir(): target.mkdir(parents=True, exist_ok=True); continue
                check_cancel(); target.parent.mkdir(parents=True, exist_ok=True)
                import zlib
                if target.is_file() and target.stat().st_size == row.file_size:
                    crc = 0
                    with target.open('rb') as stream:
                        for block in iter(lambda: stream.read(1048576), b''): crc = zlib.crc32(block, crc)
                    if crc & 0xFFFFFFFF == row.CRC:
                        _mode(target, mode or 0o644); done += 1; progress(done, total); continue
                temp = ordinary(target.with_name(target.name + '.extracting'))
                with package.open(row) as source, temp.open('wb') as output:
                    while True:
                        check_cancel(); block = source.read(1048576)
                        if not block: break
                        output.write(block)
                    output.flush(); os.fsync(output.fileno())
                os.replace(temp, target); _mode(target, mode or 0o644)
                done += 1; progress(done, total)
    executable = ordinary(destination / spec['executable'])
    if not executable.is_file(): raise WizardError('missing_tool', 'Pinned tool archive lacks its executable.')
    atomic_json(marker, {'schema': 1, 'key': key, 'complete': True,
                         'executableSha256': digest(executable), 'links': links})
    return executable


def _tool_outputs(root):
    # State receipts deliberately reject links. Record their ordinary targets and
    # the archive-bound link manifest, while the extractor verifies the links.
    return sorted(path for path in root.rglob('*') if path.is_file() and not path.is_symlink())


def _provision_tool(name, store, session):
    spec = spec_for(name); platform_name = host_key()
    suffix = '.tar.gz' if spec.get('archive') == 'tar.gz' else '.zip'
    archive = download(spec, store.root / 'tools/downloads' / (name + '-' + spec['version'] + ('-linux-x64' if platform_name == 'linux-x64' else '') + suffix),
                       lambda: store.check_cancel(session),
                       lambda done, total: store.progress(session, 'tools', 'tool-download-' + name, done, total, 'bytes'))
    root = store.root / 'tools' / (name + '-' + spec['version'] + ('-linux-x64' if platform_name == 'linux-x64' else ''))
    executable = extract_owned(archive, root, spec, lambda: store.check_cancel(session),
                               lambda done, total: store.progress(session, 'tools', 'tool-extract-' + name, done, total, 'files'))
    return executable, _tool_outputs(root)

def tools(store, session, supervisor):
    host = host_key(); found = {}; outputs = []; pins = {}
    for name in ('git', 'dotnet8', 'dotnet10'):
        log = store.session_dir(session) / 'logs' / (name + '-version.log')
        store.operation(session, 'tools', 'verify-' + name, detail='Checking ' + name + ' executable version and support files')
        if name == 'git' and host == 'linux-x64':
            candidate = shutil.which('git')
            if not candidate:
                raise WizardError('linux_git_missing', 'Git is required for mod source dependencies. Install Git with your distribution package manager, then retry.',
                                  'Für die Mod-Abhängigkeiten wird Git benötigt. Git über die Paketverwaltung deiner Distribution installieren und erneut versuchen.')
            executable = Path(candidate).resolve()
            supervisor.run([executable, '--version'], log)
            version = log.read_text(encoding='utf-8').strip()
            match = re.fullmatch(r'git version (\d+)\.(\d+)(?:\..*)?', version)
            if not match or (int(match[1]), int(match[2])) < (2, 25):
                raise WizardError('tool_version', 'The Linux system Git must be version 2.25 or newer.')
            pins[name] = {'kind': 'system-unpinned', 'version': version, 'path': str(executable)}
        else:
            spec = spec_for(name)
            executable, extracted = _provision_tool(name, store, session); outputs.extend(extracted)
            supervisor.run([executable, '--version'], log)
            expected = 'git version ' + spec['version'] if name == 'git' else spec['version']
            if log.read_text(encoding='utf-8').strip() != expected:
                raise WizardError('tool_version', 'A provisioned tool reported an unexpected version.')
            pins[name] = value_hash(spec)
            version = spec['version']
        store.record(session, 'tool_verified', 'tools', tool=name, version=version)
        store.operation(session, 'tools', 'verify-' + name, complete=True, detail=name + ' version confirmed')
        found[name] = str(executable); outputs.append(log)
    path = store.session_dir(session) / 'tools.json'
    atomic_json(path, {'schema': 1, **found, 'host': host, 'pins': pins})
    return [path, *outputs], found


def profile_tools(store, session, supervisor):
    """Provision only Android signing tools; a profile edit needs no Unity licence."""
    host = host_key(); found, outputs = {}, []
    for name in ('apkJdk', 'apkBuildTools'):
        executable, extracted = _provision_tool(name, store, session)
        store.operation(session, 'tools', 'verify-' + name, detail='Pinned signing tool files extracted and qualified.', complete=True)
        found[name] = str(executable); outputs.extend(extracted)
    java, aapt = Path(found['apkJdk']), Path(found['apkBuildTools'])
    suffix = '.exe' if host == 'windows-x64' else ''
    selected = {'java': str(java), 'keytool': str(java.with_name('keytool' + suffix)),
                'jdk': str(java.parent.parent), 'aapt': str(aapt),
                'apksigner': str(aapt.parent / 'lib/apksigner.jar'), 'zipalign': str(aapt.parent / ('zipalign' + suffix))}
    if not all(Path(selected[name]).is_file() for name in ('java', 'keytool', 'aapt', 'apksigner', 'zipalign')):
        raise WizardError('apk_tools_incomplete', 'The pinned Android signing tools are incomplete.')
    log = store.session_dir(session) / 'logs/apk-java-version.log'
    supervisor.run([java, '-version'], log, timeout=30)
    if '17.0.16' not in log.read_text(encoding='utf-8', errors='replace'):
        raise WizardError('tool_version', 'The pinned APK JDK reported an unexpected version.')
    outputs.append(log)
    path = store.session_dir(session) / 'apk-tools.json'
    atomic_json(path, {'schema': 1, **selected, 'host': host})
    return [path, *outputs], {'apkTools': selected}

def environment(details):
    env = dict(os.environ)
    env["PATH"] = os.pathsep.join([*(str(Path(details[name]).parent) for name in ("git", "dotnet8") if name in details), env.get("PATH", "")])
    if "dotnet8" in details: env["DOTNET_ROOT"] = str(Path(details["dotnet8"]).parent)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", PYTHONUTF8="1", GHVRQ_WIZARD_PROGRESS="1")
    return env


def derive_runtime_dependencies(checkout, game_root, details, supervisor, logs, check_cancel):
    """Run the selected source's existing package projects without a Bash dependency."""
    module = __import__("discovery").builder(checkout)
    managed = module.game_data(Path(game_root)) / "Managed"
    recipe = (checkout / "scripts/build-runtimedeps.sh").read_text(encoding="utf-8")
    packages = re.findall(r'"(com\.unity\.xr\.[a-z-]+) ([0-9]+\.[0-9]+\.[0-9]+)"', recipe)
    projects = sorted((checkout / "tools/RuntimeDepsBuild").glob("*/*.csproj"))
    if len(packages) != 3 or len(projects) != 3:
        raise WizardError("runtime_recipe", "The selected mod has changed its XR dependency recipe; its declared package projects need review.")
    versions = dict(packages); actual = {}; pending = []
    store, session = getattr(supervisor, 'store', None), getattr(supervisor, 'session', None)
    if store and session: store.operation(session, 'source', 'xr-sources', detail='Preparing declared XR package sources')
    for project in projects:
        text = project.read_text(encoding="utf-8")
        version = re.search(r"<PackageSourceVersion>([^<]+)</PackageSourceVersion>", text)
        package = "com.unity.xr." + {"Management": "management", "CoreUtils": "core-utils", "OpenXR": "openxr"}.get(project.stem.rsplit(".", 1)[-1], "")
        if not version or versions.get(package) != version[1]: raise WizardError("runtime_recipe", "XR source package/project versions disagree.")
        directory = checkout / "tools/RuntimeDepsBuild/sources" / package
        target = checkout / "libs/RuntimeDeps" / (project.stem + ".dll")
        if target.is_file(): actual[project.stem] = {"sha256": digest(target), "source": "supplied-local-assembly"}; continue
        directory.mkdir(parents=True, exist_ok=True)
        env = environment(details); git = details["git"]
        if not (directory / ".git").is_dir():
            supervisor.run([git, "-C", directory, "init", "-q"], logs / (package + "-init.log"), env=env)
        if not (directory / "package.json").is_file():
            supervisor.run([git, "-C", directory, "fetch", "--depth", "1", "https://github.com/needle-mirror/" + package + ".git", "refs/tags/" + version[1]], logs / (package + "-fetch.log"), env=env)
            supervisor.run([git, "-C", directory, "checkout", "--detach", "FETCH_HEAD"], logs / (package + "-checkout.log"), env=env)
        if json.loads((directory / "package.json").read_text(encoding="utf-8"))["version"] != version[1]: raise WizardError("runtime_recipe", "Cached XR package has another version.")
        pending.append((project, target, package, version[1]))
    # Project references may build another XR package, so all sources must be
    # present before the first .NET build (the Bash recipe has the same order).
    if store and session: store.operation(session, 'source', 'xr-build', detail='Compiling the selected source\'s XR dependency projects')
    for project, target, package, version in pending:
        supervisor.run([details["dotnet8"], "build", project, "-c", "Release", "--nologo", "-v", "quiet", "-p:GameManaged=" + str(managed)], logs / (package + "-build.log"), env=environment(details))
        check_cancel(); built = project.parent / "bin/Release/net472" / target.name
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(built, target)
        actual[project.stem] = {"sha256": digest(target), "source": "declared-provisional-project", "package": package, "version": version}
    manifest = checkout / "libs/RuntimeDeps/wizard-dependencies.json"
    atomic_json(manifest, {"schema": 1, "kind": "compile-time dependency inputs; Quest uses Android PlayerSdk packages", "assemblies": actual})
    if store and session: store.operation(session, 'source', 'xr-build', complete=True, detail='XR compile-time dependencies verified')
    return manifest


def release_source(store, session, choices, details, supervisor, source):
    """Copy only verified release inputs; keep immutable per-release workspaces."""
    release = discovery.local_support_module(source, "release")
    store.operation(session, 'source', 'source-verify', detail='Checking the shipped source manifest against its exact files')
    records, commit, dirty = release.verified_source_inventory(source)
    if choices.get("sourceCommit") and choices["sourceCommit"] != commit:
        raise WizardError("source_changed", "Selected commit differs from the shipped release.")
    identity = value_hash(records)
    checkout = ordinary(store.root / "source" / (session + "-" + identity[:16]))
    owner = checkout.with_name(checkout.name + ".owner.json")
    expected = {"schema": 1, "session": session, "releaseHash": identity, "commit": commit}
    if owner.exists():
        if read_json(owner) != expected: raise WizardError("unowned_source", "Release workspace ownership differs.")
    elif checkout.exists(): raise WizardError("unowned_source", "Release workspace has no ownership record.")
    else: atomic_json(owner, expected)
    checkout.mkdir(parents=True, exist_ok=True)
    store.progress(session, 'source', 'source-copy', 0, len(records), 'files')
    for index, row in enumerate(records, 1):
        store.check_cancel(session)
        target = ordinary(checkout / row["path"]); target.parent.mkdir(parents=True, exist_ok=True)
        retained = target.is_file() and target.stat().st_size == row["size"] and digest(target) == row["sha256"]
        if not retained:
            shutil.copyfile(source / row["path"], target)
            if digest(target) != row["sha256"]: raise WizardError("source_changed", "A release input changed while copied.")
        store.progress(session, 'source', 'source-copy', index, len(records), 'files', Path(row['path']).name)
    derived = derive_runtime_dependencies(checkout, choices["gameRoot"], details, supervisor,
                                          store.session_dir(session) / "logs", lambda: store.check_cancel(session))
    # The builder validates shipped sources and adds local derived references
    # separately. No game or license files are copied into the release itself.
    store.operation(session, 'source', 'source-inventory', detail='Recording selected source, ModBuild and derived dependency identities')
    current_records, current, _ = discovery.builder(checkout).source_inventory(checkout)
    receipt = store.session_dir(session) / "source.json"
    atomic_json(receipt, {"schema": 1, "sourceRoot": str(checkout), "commit": current, "dirty": False,
                          "sourceHash": value_hash(current_records), "releaseHash": identity})
    return [receipt, owner, derived, *(checkout / row["path"] for row in current_records)], {
        "sourceRoot": str(checkout), "commit": current, "sourceHash": value_hash(current_records), "releaseHash": identity}


def source_checkout(store, session, choices, details, supervisor, repo):
    source = ordinary(Path(choices.get("sourceRoot") or repo))
    if (source / "quest-builder-release.json").is_file() and not (source / ".git").exists():
        return release_source(store, session, choices, details, supervisor, source)
    checkout = ordinary(store.root / "source" / session)
    store.operation(session, 'source', 'source-verify', detail='Resolving the selected source to one immutable commit')
    git = details["git"]; env = environment(details)
    log_root = store.session_dir(session) / "logs"
    owner = store.session_dir(session) / "source-attempt.json"
    source = Path(choices.get("sourceRoot") or repo)
    local = (source / ".git").exists()
    origin = str(source) if local else LOCK["source"]["url"]
    if checkout.exists() and not owner.exists(): raise WizardError("unowned_source", "Source checkout has no wizard ownership record.")
    if not owner.exists(): atomic_json(owner, {"schema": 1, "session": session, "origin": origin})
    if read_json(owner).get("origin") != origin: raise WizardError("source_changed", "Source origin changed; use a new session.")
    # git removes failed clone directories itself when possible. A hard kill can
    # leave its owned incomplete clone; retaining the receipt lets us safely retry.
    if checkout.exists() and not (checkout / ".git").is_dir(): shutil.rmtree(checkout)
    if checkout.exists() and not (store.session_dir(session) / "source-resolution.json").exists():
        try:
            supervisor.run([git, "-C", checkout, "rev-parse", "--verify", "HEAD"], log_root / "source-clone-complete.log", env=env)
        except WizardError as error:
            if error.code != "child_failed": raise
            # Only the matching attempt-owned clone with no resolved commit is
            # repaired. A cancelled command or completed checkout is retained.
            shutil.rmtree(checkout)
    if not checkout.exists():
        argv = [git, "-c", "core.longpaths=true", "clone", "--no-checkout", "--no-hardlinks"]
        if not local: argv += ["--depth", "1", "--single-branch", "--branch", choices.get("sourceRef") or LOCK["source"]["ref"]]
        supervisor.run([*argv, origin, checkout], log_root / "source-clone.log", env=env)
    commit = choices.get("sourceCommit")
    resolution = store.session_dir(session) / "source-resolution.json"
    if resolution.is_file():
        resolved = read_json(resolution)["commit"]
        if commit and commit != resolved: raise WizardError("source_changed", "This session is pinned to another source commit; use a new session.")
        commit = resolved
    if not commit:
        supervisor.run([git, "-C", checkout, "rev-parse", "HEAD"], log_root / "source-commit.log", env=env)
        commit = (log_root / "source-commit.log").read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"[0-9a-f]{40}", commit): raise WizardError("source_commit", "Source did not resolve to one immutable commit.")
    if not local and choices.get("sourceCommit"):
        # An explicitly selected older commit may lie outside the shallow ref.
        # Fetch that immutable object alone instead of the project's asset history.
        try:
            supervisor.run([git, "-C", checkout, "cat-file", "-e", commit + "^{commit}"],
                           log_root / "source-selected-present.log", env=env)
        except WizardError as error:
            if error.code != "child_failed": raise
            supervisor.run([git, "-C", checkout, "fetch", "--depth", "1", origin, commit],
                           log_root / "source-selected-commit.log", env=env)
    atomic_json(resolution, {"schema": 1, "commit": commit})
    store.operation(session, 'source', 'source-copy', detail='Preparing the selected mod checkout')
    supervisor.run([git, "-C", checkout, "checkout", "--detach", commit], log_root / "source-checkout.log", env=env)
    def source_inventory(directory, filename):
        # The existing inventory invokes `git` on PATH. Run in a private child
        # with portable Git instead of mutating the HTTP server's environment.
        record = store.session_dir(session) / filename
        code = ("import sys;from pathlib import Path;sys.path.insert(0,sys.argv[1]);"
                "import discovery;from state import atomic_json;"
                "files,commit,dirty=discovery.builder(Path(sys.argv[2])).source_inventory(Path(sys.argv[3]));"
                "atomic_json(Path(sys.argv[4]),dict(schema=1,files=files,commit=commit,dirty=dirty))")
        supervisor.run([sys.executable, "-I", "-B", "-c", code, HERE, checkout, directory, record],
                       log_root / (filename + ".log"), env=env)
        value = read_json(record, limit=64 * 1048576)
        return value["files"], value["commit"], value["dirty"]
    if local:
        records, original_commit, _ = source_inventory(source, "source-local-inventory.json")
        if original_commit != commit: raise WizardError("source_changed", "The local checkout differs from the session's source commit; select a new session.")
        # Copy the exact source inventory, including ordinary uncommitted mod
        # work and explicitly declared ignored XR DLLs. Secret/output rules stay
        # in the existing builder; source is read-only.
        for index, row in enumerate(records, 1):
            store.check_cancel(session)
            original = source / row["path"]; target = ordinary(checkout / row["path"])
            target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(original, target)
            if digest(target) != row["sha256"]: raise WizardError("source_changed", "A source file changed while copied; retry when editing stops.")
            store.progress(session, 'source', 'source-copy', index, len(records), 'files', Path(row['path']).name)
    derived = derive_runtime_dependencies(checkout, choices["gameRoot"], details, supervisor, log_root, lambda: store.check_cancel(session))
    store.operation(session, 'source', 'source-inventory', detail='Recording the exact source and runtime-dependency manifest')
    records, current, dirty = source_inventory(checkout, "source-owned-inventory.json")
    receipt = store.session_dir(session) / "source.json"
    atomic_json(receipt, {"schema": 1, "sourceRoot": str(checkout), "commit": current, "dirty": dirty,
                          "sourceHash": value_hash(records)})
    return [receipt, resolution, owner, derived, *(checkout / row["path"] for row in records)], {"sourceRoot": str(checkout), "commit": current, "sourceHash": value_hash(records)}
