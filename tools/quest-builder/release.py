"""Audited, game-free builder source releases and immutable Git-free identity."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import tempfile
import zipfile
from storage import BuildError, canonical, digest, record_file, write_json, ImmutableFileHashes

MANIFEST = 'quest-builder-release.json'
AUTHORED_LINKS = {
    'unity/GloomhavenVR.Assets/Assets/Editor/CardGripPoseLink.cs': 'src/GloomhavenVR/Cards/CardGripPose.cs',
    'unity/GloomhavenVR.Assets/Assets/Editor/GrabBarMeshLink.cs': 'src/GloomhavenVR/Core/GrabBar.cs',
}
PUBLIC_PACKAGES = {'prebuilt/quest-converters-win64-v1.zip': '61f7d664384b12663fb4fb799ffb8566bf11e99e15ce72c7afb00d5b62199f2d'}
ROOT_FILES = {'Quest-Builder.cmd', 'Quest-Builder.sh', 'Quest-Repair-Access.cmd', 'Quest-Repair-Access.sh', 'QUEST-BUILDER-START.txt', 'LICENSE', 'Directory.Build.props', 'GloomhavenVR.sln', 'global.json', 'nuget.config', '.editorconfig'}
TOOL_ROOTS = {'QuestCampaignInventory', 'QuestProceduralExport', 'QuestWeaver', 'RuntimeDepsBuild',
              'ShaderOcclusionPatcher', 'environment-mesh', 'figure-mesh', 'quest-builder', 'quest-campaign-audit', 'quest-compute',
              'quest-installer', 'quest-movies', 'quest-native', 'quest-network', 'quest-procedural',
              'quest-procedural-runtime', 'quest-recovery', 'quest-shaders', 'quest-wizard', 'quest-wizard-ui'}
UNITY_ROOTS = {'GloomhavenVR.Assets', 'GloomhavenVR.Quest', 'GloomhavenVR.FigureMeshes'}
SCRIPTS = {'build-quest.py', 'build-quest-native.py', 'recover-quest.py', 'build-runtimedeps.sh', 'repair-quest-access.ps1',
           'quest-builder-wizard.cmd', 'quest-builder-wizard.ps1', 'quest-builder-wizard.sh', 'package-quest-builder.py',
           'export-quest-build-support.py', 'install-quest-wireless.cmd', 'install-quest-wireless.ps1',
           'install-quest-wireless.py', 'collect-quest-logs.cmd', 'collect-quest-logs.ps1',
           'collect-quest-logs.py', 'quest-saves.cmd', 'quest-saves.ps1', 'quest-saves.py', 'generate-environment-meshes.py'}
SECRET_SUFFIXES = {'.dll', '.exe', '.apk', '.bundle', '.zip', '.ulf', '.alf', '.keystore', '.jks', '.p12', '.pem', '.key'}
GENERATED_PARTS = {'bin', 'obj', 'Library', 'Temp', 'Logs', 'Builds', '__pycache__', '.git', 'node_modules'}
# Tool source checkouts and compiled reference DLLs are derived on the owner's PC;
# they are not release inputs and the builder inventories declared DLLs separately.
LEGACY_LOCAL_DEPENDENCIES = ('libs/RuntimeDeps/', 'libs/Natives/', 'tools/RuntimeDepsBuild/sources/',
                             'scripts/.quest-venv/', 'scripts/.quest-python/')
LOCAL_DEPENDENCIES = (*LEGACY_LOCAL_DEPENDENCIES, 'scripts/.quest-python-linux/', 'scripts/.quest-adb/')
RELEASE_KIND = 'GloomhavenVR game-free Windows and Linux builder'
LEGACY_RELEASE_KIND = 'GloomhavenVR game-free Windows builder'
LINUX_REQUIRED = {'Quest-Builder.sh', 'scripts/quest-builder-wizard.sh', 'tools/quest-installer/bootstrap.sh',
                  'tools/quest-installer/linux_bootstrap.py', 'tools/quest-wizard/host_paths.py'}
# The current Frame mod tracks its offline original-derived environment bank.
# A source Builder recreates those streams from the player's immutable game;
# original geometry and its derived receipt never become public ZIP inputs.
OWNED_DERIVED_ROOTS = ('unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/',)
OWNED_DERIVED_FILES = {'tools/environment-mesh/bank.json', 'tools/environment-mesh/manifest.json',
                       'tools/figure-mesh/manifest.json', 'tools/figure-mesh/distance-manifest.json',
                       'tools/environment-mesh/catalog.json', 'tools/environment-mesh/catalog.json.gz',
                       'tools/environment-mesh/ornaments.json'}

# These explicitly named resources contain authored shader programs, isolated
# public-SDK helpers and the public Valve loader, never original game content.
# They are compiler inputs of the current mod, including source-only APK updates.
# Release1.2.0 retired the separate Windows quality/compositor/projection probes;
# require only the resources still embedded by its actual mod project.
AUTHORED_RESOURCES = {'src/GloomhavenVR/Assets/StereoVideo/ui-sbs.bundle',
    'src/GloomhavenVR/Assets/QuestLoading/ghvr-quest-loading.bundle',
    'src/GloomhavenVR/Assets/QuestLoading/QuestLoadingSpinner.shader',
    'src/GloomhavenVR/Assets/QuestLoading/QuestLoadingSpinner.shader.meta',
    'src/GloomhavenVR/Assets/QuestLoading/provenance.json'} | {
    'src/GloomhavenVR/Assets/SteamLink/' + name for name in (
        'scene.bundle', 'connection-reader.exe', 'openvr_api.dll')}
AUTHORED_MOVIES = {'src/GloomhavenVR/Assets/Movies/CP_Intro/' + name for name in (
    'GH_CP_Intro.mov', 'GH_CP_Intro.sbs-v2.mov')}
GAME_MOVIE_ROOTS = tuple('src/GloomhavenVR/Assets/Movies/' + name + '/'
                        for name in ('Heroes', 'Ambient', 'Embedded'))


def owned_derived_source(name):
    """One exclusion boundary shared by Git and released Builder inventories."""
    return (name.startswith(OWNED_DERIVED_ROOTS + GAME_MOVIE_ROOTS)
            or name in OWNED_DERIVED_FILES)


# B716 main1.2.0 retires these exact public B713 sources/resources. Players
# commonly extract a new Builder over the old directory. Admit no unlisted
# source except these complete old byte identities, then remove them only after
# the entire current release and its remaining directory contents are valid.
# Provenance: B713 f4539f297a948476b593f6fd0ebc561f04e22a8e, archive SHA256
# fa0a762d7a6a38ea23644090947b122f6f86acbb067d2864bb19955d79f42a01.
RETIRED_B713_INPUTS = {
    "src/GloomhavenVR/Assets/SteamLink/compositor-state-probe-provenance.json": (1339, "a5e1013eec72214915b0bc2c2d40220734423e82bea1ddaaa9b4eec6f224c82a"),
    "src/GloomhavenVR/Assets/SteamLink/compositor-state-probe.exe": (957952, "fd5496814dbcc4a37de7d73e46fae155000197404adec353f29bdeb2e0009b38"),
    "src/GloomhavenVR/Assets/SteamLink/projection-overlay-probe-provenance.json": (1373, "98b0e500631fab69e1325e05ba09e9359a1d5c7420065042fc886488bf7e4824"),
    "src/GloomhavenVR/Assets/SteamLink/projection-overlay-probe.exe": (210432, "0b5d8fcda22da91f1ee43259384ce0e5e119d870ef9b4faeeb200973b1600fd0"),
    "src/GloomhavenVR/Assets/SteamLink/stream-quality-provenance.json": (836, "9079226cab4d32e43c5a98ef7c1d28843b60b98940350edf10d1b33b2a2774a0"),
    "src/GloomhavenVR/Assets/SteamLink/stream-quality.exe": (46080, "aae6bf6a0da3fd3a0f2e9e6b65ff6375786b5058fc4c8fc7c752c47298202ccd"),
    "src/GloomhavenVR/Core/Loc/Loc.SteamLinkProjectionProbe.cs": (1853, "7119ce6ab27d3c93c6c0bd4c2a06df75103b49955735f2f0287c60915a483d3d"),
    "src/GloomhavenVR/Core/SteamLink/SceneAtlasProfile.cs": (1857, "63b09c1697376eb65f5351099423a2b8661c32cb99b229fac9d5113e32e05266"),
    "src/GloomhavenVR/Core/SteamLink/ScenePayloadDraft.cs": (4795, "0c56d3c2ec0d554b156137dce64a0a4f3e45ada4c9b964790c648ab747c0454a"),
    "src/GloomhavenVR/Core/SteamLink/SteamLinkCompositorMonitor.cs": (18537, "f531a1879e625ad2b9c5f82638bf66f91855e97e71e7e5d5e95fdbee10401b37"),
    "src/GloomhavenVR/Core/SteamLink/SteamLinkProjectionProbe.cs": (14676, "c8d93a9a95e2d0b5ff76ffc1b16e2031c0a96ee95539a6fc2d676755d47ae182"),
    "src/GloomhavenVR/Core/SteamLink/SteamLinkResolutionPolicy.cs": (1130, "e1c52d442c1f675a4b6f83d33d90e096f569b1228734b927782d5f1f72fc3460"),
    "src/GloomhavenVR/Core/SteamLink/SteamLinkStreamQuality.cs": (24809, "59648a23cee13f86f6e67afa19b5b658e21817cf5da860d37117db098989a301"),
    "src/GloomhavenVR/Rig/VRRigDriver.MenuCamera.cs": (2052, "25474cc9d00ab4b7c22935cbb0baae02428b519d8569cd84468488b763ae454e"),
    "src/GloomhavenVR/WorldUI/Options/VROptionsTab.SteamLinkProjectionProbe.cs": (2308, "3dc9536c07deb7b9bf090f9693bbeb030515827b9ea58b7c7aa6a99a79a89049"),
}


PROMOTIONAL_ASSETS = {'tools/quest-wizard-ui/assets/promo/' + name for name in (
    'cragheart.png', 'spellweaver.png', 'brute.jpg', 'scoundrel.png', 'bandit-guard.jpg', 'bandit-archer.jpg',
    'living-bones.jpg', 'living-corpse.jpg', 'living-spirit.jpg', 'cultist.jpg', 'sun-demon.jpg', 'night-demon-elite.jpg',
    'bandit-guard-elite.jpg', 'bandit-archer-elite.jpg', 'living-bones-elite.jpg', 'living-corpse-elite.jpg',
    'living-spirit-elite.jpg', 'cultist-elite.jpg', 'sun-demon-elite.jpg', 'bandit-commander.jpg',
    'nightcrawler-scoundrel.jpg')}
REQUIRED = set(PUBLIC_PACKAGES) | {"tools/quest-builder/staging_copy.py", "tools/quest-builder/native_bank_resume.py", 'tools/quest-recovery/recovery_workers.py', 'Quest-Builder.cmd', 'QUEST-BUILDER-START.txt', 'scripts/quest-builder-wizard.cmd', 'scripts/quest-builder-wizard.ps1', 'scripts/build-quest.py',
            'Quest-Repair-Access.cmd', 'Quest-Repair-Access.sh', 'scripts/repair-quest-access.ps1',
            'tools/quest-wizard/wizard.py', 'tools/quest-wizard-ui/index.html', 'tools/quest-installer/bootstrap.ps1',
            'tools/quest-wizard/server.py', 'tools/quest-wizard/cleanup.py',
            'tools/quest-wizard/store_launcher.py', 'tools/quest-wizard/update_storage.py', 'tools/quest-builder/store_launchers.py',
            'tools/quest-wizard/cleanup_io.py', 'tools/quest-wizard/run_storage.py',
            'tools/quest-installer/installer.py', 'tools/quest-installer/signing_recovery.py',
            'tools/quest-installer/content_access.py', 'tools/quest-installer/device_cleanup.py',
            'tools/quest-installer/android_storage.py', 'tools/quest-installer/android/StorageAccess.java',
            'tools/quest-installer/android/StorageAccess.dex', 'tools/quest-installer/wireless.py',
            'tools/quest-builder/portable_package.py', 'tools/quest-wizard/package_tools.py',
            'tools/quest-installer/native_rename.py', 'tools/quest-wizard/device_tools.py',
            'tools/quest-builder/builder.py', 'tools/quest-builder/release.py',
            'tools/quest-builder/preparation_metadata.py', 'tools/quest-builder/editor_overlay.py',
            'tools/quest-builder/runtime_overlay.py',
            'tools/quest-builder/script_remap_resume.py',
            'tools/quest-recovery/full_recovery.py', 'tools/QuestWeaver/Program.cs',
            'tools/quest-procedural-runtime/worker.c', 'tools/quest-procedural-runtime/runtime.py',
            'tools/quest-procedural-runtime/proton_runtime.py', 'tools/quest-procedural-runtime/proton_layout.py',
            'tools/quest-procedural-runtime/proton_audit.py', 'tools/quest-procedural-runtime/proton.lock.json',
            'tools/quest-procedural-runtime/proton_launcher.c', 'tools/quest-builder/native_plugins.py',
            'src/GloomhavenVR/GloomhavenVR.csproj',
            'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs',
            'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestNativePluginContract.cs'}
REQUIRED |= AUTHORED_RESOURCES | AUTHORED_MOVIES | PROMOTIONAL_ASSETS | {
    'tools/quest-builder/movie_banks.py', 'tools/quest-builder/movie_render.py',
    'tools/quest-builder/requirements-movies.txt', 'tools/quest-movies/recipe.json',
    'tools/quest-movies/MovieBankBuild.cs', 'tools/quest-movies/QuestMovieEye.shader',
    'tools/quest-movies/StereoVideoUI.shader', 'tools/quest-movies/Unity-LICENSE.txt',
    'src/GloomhavenVR/Core/QuestMovieAssets.cs',
    'unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestMovieContent.cs',
    'tools/quest-wizard-ui/promo-artwork.json',
    'tools/quest-wizard-ui/assets/meta-quest-logo.svg',
    'tools/quest-wizard-ui/assets/github-mark.svg',
    'tools/quest-wizard-ui/assets/buymeacoffee-cup.svg',
    'tools/quest-wizard-ui/assets/octicons-LICENSE.txt',
    'tools/quest-wizard-ui/assets/provenance.json',
    'tools/quest-builder/recovery_resume.py',
    'tools/quest-builder/environment_bank.py',
    'tools/quest-builder/figure_banks.py',
    'tools/quest-builder/quest_card_pixels.py',
    'src/GloomhavenVR/Cards/Art/QuestCardPixels.cs',
    'unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestCardPixelContent.cs',
    'tools/figure-mesh/QuestFigureMeshGenerator.cs',
    'tools/figure-mesh/Generator.csproj',
    'tools/figure-mesh/export-native.py',
    'tools/figure-mesh/NativeFigureMeshStream.cs',
    'tools/figure-mesh/ScenarioFigureMeshTopology.cs',
    'tools/figure-mesh/UnityMeshSimplifier/MeshSimplifier.cs',
    'tools/figure-mesh/UnityMeshSimplifier/LICENSE.md',
    'tools/environment-mesh/export-native.py',
    'tools/environment-mesh/geometry.py',
    'tools/environment-mesh/roles.py',
    'scripts/generate-environment-meshes.py',
    'tools/quest-wizard/stage_plan.py',
    'tools/quest-wizard/runtime_dependencies.py',
    'tools/quest-wizard/timing.py',
    'tools/quest-wizard/tool_reuse.py',
    'tools/quest-builder/native_admission.py',
    'tools/quest-builder/update_capacity.py',
    'tools/quest-builder/apk_input_cache.py',
    'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestWizardProgress.cs'}


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
    if name in AUTHORED_RESOURCES: return True
    if owned_derived_source(name): return False
    if Path(name).suffix.lower() in {'.mov', '.mp4'} and name not in AUTHORED_MOVIES:
        return False
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


def movie_recipe_closure(repo, rows):
    """Check recipe references against byte records already read for the release.

    Never hash the large numeric payload a second time for this closure check.
    Missing chunks must fail packaging, before a player starts conversion.
    """
    path = ordinary(repo / 'tools/quest-movies/recipe.json')
    try:
        if not path.is_file() or path.stat().st_size > 4 * 1048576:
            raise ValueError('missing or oversized recipe')
        recipe = json.loads(path.read_text(encoding='utf-8'))
        if recipe.get('schema') != 1 or not isinstance(recipe.get('movies'), list) or len(recipe['movies']) > 64:
            raise ValueError('invalid recipe header')
        records = {row['path']: row for row in rows}
        for movie in recipe['movies']:
            for shot in movie['shots']:
                for chunk in shot.get('depth', {}).get('chunks', []):
                    name = chunk['path']; safe_name(name)
                    if not name.startswith('depth/') or not name.endswith('.zst'):
                        raise ValueError('invalid depth chunk path')
                    row = records.get('tools/quest-movies/' + name)
                    if row is None or (row['size'], row['sha256']) != (chunk['size'], chunk['sha256']):
                        raise BuildError('Movie recipe chunk is missing or differs: ' + name)
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as exc:
        raise BuildError('Builder movie recipe is incomplete or invalid.') from exc


def _retired_source_witness(path, relative):
    expected = RETIRED_B713_INPUTS.get(relative)
    if expected is None:
        raise BuildError('Unlisted builder release file: ' + relative
                         + '. Extract the new Builder into a clean folder; keep its separate work folder for reuse.')
    path = ordinary(path)
    before = path.lstat()
    stamp = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
    if (not stat.S_ISREG(before.st_mode) or before.st_size != expected[0]
            or digest(path) != expected[1] or stamp(before) != stamp(path.lstat())):
        raise BuildError('Retired B713 Builder file was modified: ' + relative
                         + '. Extract the new Builder into a clean folder; keep its separate work folder for reuse.')
    return path, stamp(before)


def _remove_retired_sources(retired):
    for path, witness in retired:
        ordinary(path)
        try:
            value = path.lstat()
        except FileNotFoundError:
            # Another verified loader may already have removed this exact obsolete
            # file. Its absence is the intended result, never a new source input.
            continue
        stamp = (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
        if stamp != witness:
            raise BuildError('Retired Builder file changed during verification: ' + str(path))
        try:
            path.unlink()
        except FileNotFoundError:
            continue
        except OSError as exc:
            raise BuildError('Cannot remove the verified obsolete Builder file: ' + str(path)
                             + '. Close other Builder windows or extract the new Builder into a clean folder;'
                             + ' keep its separate work folder for reuse.') from exc


def verified_source_inventory(repo):
    """Validate all delivered bytes, reject unlisted source; never infer Git state."""
    repo = ordinary(repo); path = ordinary(repo / MANIFEST)
    if not path.is_file() or path.stat().st_size > 4 * 1048576: raise BuildError('Missing or oversized builder release manifest.')
    value = json.loads(path.read_text(encoding='utf-8'))
    if set(value) != {'schema', 'kind', 'sourceCommit', 'modBuild', 'files', 'localDependencyRoots'} or value['schema'] != 1 or value['kind'] not in (RELEASE_KIND, LEGACY_RELEASE_KIND):
        raise BuildError('Unsupported builder release manifest.')
    if not re.fullmatch('[0-9a-f]{40}', str(value['sourceCommit'])) or type(value['modBuild']) is not int:
        raise BuildError('Invalid builder release source identity.')
    if value['localDependencyRoots'] not in (list(LOCAL_DEPENDENCIES), list(LEGACY_LOCAL_DEPENDENCIES)): raise BuildError('Invalid local dependency exception.')
    pinned_hashes = ImmutableFileHashes()
    for name, expected in PUBLIC_PACKAGES.items():
        if pinned_hashes.digest(ordinary(repo / name)) != expected: raise BuildError('Public converter package differs from its pinned release.')
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
        if not item.is_file() or item.stat().st_size != row['size'] or (pinned_hashes.digest(item) if name in PUBLIC_PACKAGES else digest(item)) != row['sha256']:
            raise BuildError('Builder release file changed: ' + name)
    if not REQUIRED <= names: raise BuildError('Builder release is incomplete.')
    movie_recipe_closure(repo, rows)
    if value['kind'] == RELEASE_KIND and not LINUX_REQUIRED <= names:
        raise BuildError('Cross-platform builder release is missing its Linux launcher.')
    # Traverse without following junctions; ignore only declared per-user generated
    # directories. Unknown files in source-bearing roots are a tampered release.
    retired = []
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
            if relative in (MANIFEST, 'scripts/.quest-bootstrap.lock', 'scripts/.quest-bootstrap-linux.lock', 'scripts/.quest-adb.lock'): continue
            if relative in names: continue
            # Even excluded binaries in a shipped input root are rejected. Local
            # dependency trees above are the only assembly exceptions.
            safe_name(relative)
            retired.append(_retired_source_witness(repo / relative, relative))
    _remove_retired_sources(retired)
    return [*rows, record_file(path, MANIFEST)], value['sourceCommit'], False


def assemble(repo, destination):
    repo = ordinary(repo); destination = ordinary(destination)
    tracked = subprocess.run(['git', '-C', str(repo), 'ls-files', '-z'], check=True, stdout=subprocess.PIPE).stdout.decode().split('\0')
    names = sorted(name for name in tracked if name and selected(name))
    if not (REQUIRED | LINUX_REQUIRED) <= set(names): raise BuildError('Required builder release sources are not tracked.')
    commit = subprocess.run(['git', '-C', str(repo), 'rev-parse', 'HEAD'], check=True, capture_output=True, text=True).stdout.strip()
    dirty = subprocess.run(['git', '-C', str(repo), 'diff', '--quiet', 'HEAD', '--', *names]).returncode
    if dirty: raise BuildError('Commit the release source files before packaging.')
    originals = {name: authored_input(repo, name) for name in names}
    if any(AUTHORED_LINKS[name] not in names for name in names if name in AUTHORED_LINKS):
        raise BuildError('Authored link target is missing from the release source inventory.')
    records = [record_file(originals[name], name) for name in names]
    movie_recipe_closure(repo, records)
    protocol = (repo / 'src/GloomhavenVR/Net/NetProtocol.cs').read_text(encoding='utf-8')
    match = re.search(r'const (?:int|ushort) ModBuild\s*=\s*(\d+)', protocol)
    if not match: raise BuildError('Cannot identify the released mod build.')
    manifest = {'schema': 1, 'kind': RELEASE_KIND, 'sourceCommit': commit,
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
                info = zipfile.ZipInfo('GloomhavenVR-Quest-Builder/' + row['path'])
                info.create_system = 3
                # Authored H.264, numeric zstd recipes and the pinned converter
                # archive are already compressed. Preserve their bytes directly.
                info.compress_type = (zipfile.ZIP_STORED if Path(row['path']).suffix.lower()
                                      in {'.mov', '.mp4', '.zst', '.zip'} else zipfile.ZIP_DEFLATED)
                info.external_attr = (0o100755 if row['path'].endswith('.sh') else 0o100644) << 16
                archive.writestr(info, raw)
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
              'promotionalArtworkFiles': sorted(PROMOTIONAL_ASSETS),
              'excluded': ['owned game/decompiled code', 'reference DLLs', 'original-derived figure/environment mesh banks',
                           'original-derived movie pixels/audio',
                           'generated APKs/caches', 'credentials/licenses/savegames'], 'materializedAuthoredLinks': [name for name in names if (repo / name).is_symlink()],
              'supportedHosts': ['windows-x64', 'linux-x64'],
              'windowsEndToEndVerified': False, 'linuxEndToEndVerified': False}
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
