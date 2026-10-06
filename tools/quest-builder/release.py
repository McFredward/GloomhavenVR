"""Audited, game-free builder source releases and immutable Git-free identity."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
import zipfile
from storage import BuildError, canonical, digest, record_file, write_json

MANIFEST = 'quest-builder-release.json'
AUTHORED_LINKS = {
    'unity/GloomhavenVR.Assets/Assets/Editor/CardGripPoseLink.cs': 'src/GloomhavenVR/Cards/CardGripPose.cs',
    'unity/GloomhavenVR.Assets/Assets/Editor/GrabBarMeshLink.cs': 'src/GloomhavenVR/Core/GrabBar.cs',
}
PUBLIC_PACKAGES = {'prebuilt/quest-converters-win64-v1.zip': '61f7d664384b12663fb4fb799ffb8566bf11e99e15ce72c7afb00d5b62199f2d'}
ROOT_FILES = {'Quest-Builder.cmd', 'QUEST-BUILDER-START.txt', 'LICENSE', 'Directory.Build.props', 'GloomhavenVR.sln', 'global.json', 'nuget.config', '.editorconfig'}
TOOL_ROOTS = {'QuestCampaignInventory', 'QuestProceduralExport', 'QuestWeaver', 'RuntimeDepsBuild',
              'ShaderOcclusionPatcher', 'quest-builder', 'quest-campaign-audit', 'quest-compute',
              'quest-installer', 'quest-native', 'quest-network', 'quest-procedural',
              'quest-procedural-runtime', 'quest-recovery', 'quest-shaders', 'quest-wizard', 'quest-wizard-ui'}
UNITY_ROOTS = {'GloomhavenVR.Assets', 'GloomhavenVR.Quest', 'GloomhavenVR.FigureMeshes'}
SCRIPTS = {'build-quest.py', 'build-quest-native.py', 'recover-quest.py', 'build-runtimedeps.sh',
           'quest-builder-wizard.cmd', 'quest-builder-wizard.ps1', 'package-quest-builder.py',
           'export-quest-build-support.py', 'install-quest-wireless.cmd', 'install-quest-wireless.ps1',
           'install-quest-wireless.py', 'collect-quest-logs.cmd', 'collect-quest-logs.ps1',
           'collect-quest-logs.py', 'quest-saves.cmd', 'quest-saves.ps1', 'quest-saves.py'}
SECRET_SUFFIXES = {'.dll', '.exe', '.apk', '.bundle', '.zip', '.ulf', '.alf', '.keystore', '.jks', '.p12', '.pem', '.key'}
GENERATED_PARTS = {'bin', 'obj', 'Library', 'Temp', 'Logs', 'Builds', '__pycache__', '.git', 'node_modules'}
# Tool source checkouts and compiled reference DLLs are derived on the owner's PC;
# they are not release inputs and the builder inventories declared DLLs separately.
LOCAL_DEPENDENCIES = ('libs/RuntimeDeps/', 'libs/Natives/', 'tools/RuntimeDepsBuild/sources/',
                      'scripts/.quest-venv/', 'scripts/.quest-python/')
REQUIRED = set(PUBLIC_PACKAGES) | {'Quest-Builder.cmd', 'QUEST-BUILDER-START.txt', 'scripts/quest-builder-wizard.cmd', 'scripts/quest-builder-wizard.ps1', 'scripts/build-quest.py',
            'tools/quest-wizard/wizard.py', 'tools/quest-wizard-ui/index.html', 'tools/quest-installer/bootstrap.ps1',
            'tools/quest-builder/builder.py', 'tools/quest-builder/release.py',
            'tools/quest-recovery/full_recovery.py', 'tools/QuestWeaver/Program.cs',
            'tools/quest-procedural-runtime/worker.c', 'src/GloomhavenVR/GloomhavenVR.csproj',
            'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs'}


def safe_name(name):
    if not isinstance(name, str) or not name or '\\' in name or ':' in name or '\0' in name:
        raise BuildError('Unsafe release path.')
    path = PurePosixPath(name)
    if path.is_absolute() or any(p in ('', '.', '..') for p in name.split('/')):
        raise BuildError('Unsafe release path.')
    for part in path.parts:
        if part.rstrip(' .') != part or re.fullmatch(r'(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?', part):
            raise BuildError('Release path cannot be represented safely on Windows.')
    return path


def selected(name):
    parts = safe_name(name).parts
    if name in PUBLIC_PACKAGES: return True
    if any(part in GENERATED_PARTS for part in parts) or any(part.startswith('.env') for part in parts): return False
    if Path(name).suffix.lower() in SECRET_SUFFIXES or 'evidence' in parts or 'decompiled' in parts: return False
    if len(parts) == 1: return name in ROOT_FILES
    return (parts[0] == 'src' or
            parts[0] == 'tools' and parts[1] in TOOL_ROOTS or
            parts[0] == 'unity' and parts[1] in UNITY_ROOTS or
            parts[0] == 'scripts' and len(parts) == 2 and parts[1] in SCRIPTS)


def ordinary(path):
    path = Path(path).absolute()
    for item in (path, *path.parents):
        if item.exists() and (item.is_symlink() or getattr(item.lstat(), 'st_file_attributes', 0) & 0x400):
            raise BuildError('Release inputs cannot use symlinks or Windows reparse points.')
    return path


def authored_input(repo, name):
    """Materialize only the two declared authoring links into ordinary ZIP files."""
    path = repo / name
    if path.is_symlink():
        target_name = AUTHORED_LINKS.get(name)
        if not target_name: raise BuildError('Undeclared source symlink cannot enter a release.')
        target = ordinary(repo / target_name)
        ordinary(path.parent)
        if path.resolve() != target or not selected(target_name) or not target.is_file():
            raise BuildError('Authored source link no longer targets its declared mod source.')
        return target
    return ordinary(path)


def verified_source_inventory(repo):
    """Validate all delivered bytes, reject unlisted source; never infer Git state."""
    repo = ordinary(repo); path = ordinary(repo / MANIFEST)
    if not path.is_file() or path.stat().st_size > 4 * 1048576: raise BuildError('Missing or oversized builder release manifest.')
    value = json.loads(path.read_text(encoding='utf-8'))
    if set(value) != {'schema', 'kind', 'sourceCommit', 'modBuild', 'files', 'localDependencyRoots'} or value['schema'] != 1 or value['kind'] != 'GloomhavenVR game-free Windows builder':
        raise BuildError('Unsupported builder release manifest.')
    if not re.fullmatch('[0-9a-f]{40}', str(value['sourceCommit'])) or type(value['modBuild']) is not int:
        raise BuildError('Invalid builder release source identity.')
    if value['localDependencyRoots'] != list(LOCAL_DEPENDENCIES): raise BuildError('Invalid local dependency exception.')
    for name, expected in PUBLIC_PACKAGES.items():
        if digest(ordinary(repo / name)) != expected: raise BuildError('Public converter package differs from its pinned release.')
    rows = value['files']
    if not isinstance(rows, list) or not 1 <= len(rows) <= 20000: raise BuildError('Invalid release inventory.')
    seen = set(); names = set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {'path', 'size', 'sha256'}: raise BuildError('Invalid release file record.')
        name = row['path']; safe_name(name)
        if not selected(name) or name.casefold() in seen: raise BuildError('Excluded or duplicate release input.')
        seen.add(name.casefold()); names.add(name)
        item = ordinary(repo / name)
        if type(row['size']) is not int or row['size'] < 0 or not re.fullmatch('[0-9a-f]{64}', str(row['sha256'])):
            raise BuildError('Invalid release byte identity.')
        if not item.is_file() or item.stat().st_size != row['size'] or digest(item) != row['sha256']:
            raise BuildError('Builder release file changed: ' + name)
    if not REQUIRED <= names: raise BuildError('Builder release is incomplete.')
    # Traverse without following junctions; ignore only declared per-user generated
    # directories. Unknown files in source-bearing roots are a tampered release.
    for directory, dirs, files in os.walk(repo, followlinks=False):
        rel = Path(directory).relative_to(repo)
        kept = []
        for name in dirs:
            child = Path(directory) / name; key = (rel / name).as_posix() + '/'
            if name in GENERATED_PARTS or any(key.startswith(prefix) for prefix in LOCAL_DEPENDENCIES): continue
            ordinary(child); kept.append(name)
        dirs[:] = kept
        for name in files:
            relative = (rel / name).as_posix()
            if relative in (MANIFEST, 'scripts/.quest-bootstrap.lock'): continue
            if relative in names: continue
            # Even excluded binaries in a shipped input root are rejected. Local
            # dependency trees above are the only assembly exceptions.
            parts = safe_name(relative).parts
            raise BuildError('Unlisted builder release file: ' + relative)
    return [*rows, record_file(path, MANIFEST)], value['sourceCommit'], False


def assemble(repo, destination):
    repo = ordinary(repo); destination = ordinary(destination)
    tracked = subprocess.run(['git', '-C', str(repo), 'ls-files', '-z'], check=True, stdout=subprocess.PIPE).stdout.decode().split('\0')
    names = sorted(name for name in tracked if name and selected(name))
    if not REQUIRED <= set(names): raise BuildError('Required builder release sources are not tracked.')
    commit = subprocess.run(['git', '-C', str(repo), 'rev-parse', 'HEAD'], check=True, capture_output=True, text=True).stdout.strip()
    dirty = subprocess.run(['git', '-C', str(repo), 'diff', '--quiet', 'HEAD', '--', *names]).returncode
    if dirty: raise BuildError('Commit the release source files before packaging.')
    originals = {name: authored_input(repo, name) for name in names}
    if any(AUTHORED_LINKS[name] not in names for name in names if name in AUTHORED_LINKS):
        raise BuildError('Authored link target is missing from the release source inventory.')
    records = [record_file(originals[name], name) for name in names]
    protocol = (repo / 'src/GloomhavenVR/Net/NetProtocol.cs').read_text(encoding='utf-8')
    match = re.search(r'const (?:int|ushort) ModBuild\s*=\s*(\d+)', protocol)
    if not match: raise BuildError('Cannot identify the released mod build.')
    manifest = {'schema': 1, 'kind': 'GloomhavenVR game-free Windows builder', 'sourceCommit': commit,
                'modBuild': int(match[1]), 'files': records, 'localDependencyRoots': list(LOCAL_DEPENDENCIES)}
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists(): raise BuildError('Release destination already exists; choose a new file.')
    temp = destination.with_name(destination.name + '.partial')
    if temp.exists(): raise BuildError('Release staging file already exists.')
    try:
        with zipfile.ZipFile(temp, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for row in records:
                raw = originals[row['path']].read_bytes()
                import hashlib
                if len(raw) != row['size'] or hashlib.sha256(raw).hexdigest() != row['sha256']: raise BuildError('Source changed during release assembly.')
                archive.writestr('GloomhavenVR-Quest-Builder/' + row['path'], raw)
            archive.writestr('GloomhavenVR-Quest-Builder/' + MANIFEST, canonical(manifest) + b'\n')
        with zipfile.ZipFile(temp) as archive:
            if archive.testzip() is not None: raise BuildError('Release ZIP failed CRC validation.')
            with tempfile.TemporaryDirectory(prefix='ghvrq-release-check-') as folder:
                archive.extractall(folder)
                verified_source_inventory(Path(folder) / 'GloomhavenVR-Quest-Builder')
        temp.replace(destination)
    finally: temp.unlink(missing_ok=True)
    report = {'schema': 1, 'sourceCommit': commit, 'modBuild': manifest['modBuild'], 'fileCount': len(records),
              'sourceBytes': sum(row['size'] for row in records), 'archiveSha256': digest(destination),
              'excluded': ['owned game/decompiled code', 'reference DLLs', 'original-derived figure mesh banks',
                           'generated APKs/caches', 'credentials/licenses/savegames'], 'materializedAuthoredLinks': [name for name in names if (repo / name).is_symlink()],
              'windowsEndToEndVerified': False}
    write_json(Path(str(destination) + '.audit.json'), report)
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo-root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    try: print(json.dumps(assemble(args.repo_root, args.output))); return 0
    except (BuildError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print('Builder release: ' + str(error)); return 1

if __name__ == '__main__': raise SystemExit(main())
