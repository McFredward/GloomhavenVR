"""Bounded hard capacity checks; estimates do not certify reuse/license."""
from __future__ import annotations
import json
import errno
import os
from pathlib import Path
import platform
import shutil
import time
from state import WizardError, atomic_json, ordinary, value_hash

GIB = 1024 ** 3
MIN_SETUP_FREE_BYTES = 4 * GIB
MIN_RUNTIME_FREE_BYTES = 2 * GIB
SPACE_CHECK_SECONDS = 15
MAX_WINDOWS_ROOT_CHARS = 70
MAX_SCAN_FILES = 100000
MAX_SCAN_SECONDS = 3
# Full-port conversion keeps owner snapshots, recovered assets, imported Unity
# data and Android output/ZIPs concurrently. This planning reserve is intentionally
# an estimate, not a measured promise for another user's game/mod/editor/cache.
TOOLS_IMPORT_RESERVE_BYTES = 20 * GIB
CONTENT_FOOTPRINT_FACTOR = 8


def tree_bytes(root, *, deadline=None):
    root = ordinary(root); started = time.monotonic(); total = count = skipped = 0
    deadline = min(deadline, started + MAX_SCAN_SECONDS) if deadline is not None else started + MAX_SCAN_SECONDS
    if not root.is_dir(): return {'bytes': 0, 'files': 0, 'bounded': False, 'skippedLinks': 0}
    for directory, dirs, files in os.walk(root, followlinks=False):
        if time.monotonic() >= deadline:
            return {'bytes': total, 'files': count, 'bounded': True, 'skippedLinks': skipped}
        kept = []
        for name in dirs:
            path = Path(directory) / name
            if path.is_symlink() or getattr(path.lstat(), 'st_file_attributes', 0) & 0x400: skipped += 1
            else: kept.append(name)
        dirs[:] = kept
        for name in files:
            path = Path(directory) / name
            if path.is_symlink() or getattr(path.lstat(), 'st_file_attributes', 0) & 0x400: skipped += 1; continue
            total += path.stat().st_size; count += 1
            if count >= MAX_SCAN_FILES or time.monotonic() >= deadline:
                return {'bytes': total, 'files': count, 'bounded': True, 'skippedLinks': skipped}
    return {'bytes': total, 'files': count, 'bounded': False, 'skippedLinks': skipped}


def credit_game(root, game):
    """Recognize the proven old snapshot after the Windows ordering repair."""
    import re
    try:
        rows = game['files']
        if (not all(isinstance(row, dict) and set(row) == {'path', 'size', 'sha256'}
                    and isinstance(row['path'], str) and type(row['size']) is int and row['size'] >= 0
                    and re.fullmatch('[0-9a-f]{64}', row['sha256']) for row in rows)
                or game['key'] != value_hash({'files': rows})):
            return game
        ordered = sorted(rows, key=lambda row: row['path'])
        key = value_hash({'files': ordered})
        if key == game['key']: return game
        snapshot = ordinary(root / 'build/inputs/game' / key / '.snapshot.json')
        if snapshot.is_file() and snapshot.stat().st_size <= 16 * 1048576:
            owner = json.loads(snapshot.read_text(encoding='utf-8'))
            # A hash/key conversion alone must not credit a nonexistent or
            # incomplete old cache. The canonical snapshot must own these exact
            # records; the builder still qualifies their byte witnesses later.
            if owner == {'schema': 1, 'files': ordered}:
                return {**game, 'key': key, 'files': ordered}
    except (KeyError, TypeError, ValueError, OSError): pass
    return game


def space_estimate(root, game_root, repo):
    import discovery
    helper = discovery.builder(repo)
    data = helper.game_data(Path(game_root))
    original = tree_bytes(data)
    fresh = original['bytes'] * CONTENT_FOOTPRINT_FACTOR + TOOLS_IMPORT_RESERVE_BYTES
    cached = 0; candidate = False; cache_bounded = False; game = None
    pointer_path = ordinary(root / 'build/latest-input.json')
    if pointer_path.is_file() and pointer_path.stat().st_size < 65536:
        try:
            pointer = json.loads(pointer_path.read_text(encoding='utf-8'))
            # Reuse is merely a candidate until normal builder hashes pass. Never
            # credit another game's retained workspace or trust a path escape.
            import re
            name = pointer['manifest']
            if (Path(pointer['sourceGameRoot']).resolve() == data.resolve()
                    and re.fullmatch(r'manifests/[0-9a-f]{64}\.json', name)):
                manifest = ordinary(root / 'build' / name)
                if manifest.is_file() and manifest.stat().st_size <= 16 * 1048576:
                    game = json.loads(manifest.read_text())['game']
                    rows = game['files']
                    candidate = (not original['bounded'] and original['skippedLinks'] == 0
                                 and len(rows) == original['files'] and sum(row['size'] for row in rows) == original['bytes'])
        except (OSError, ValueError, KeyError, TypeError): candidate = False
    if candidate:
        game = credit_game(root, game)
        if not re.fullmatch('[0-9a-f]{64}', str(game.get('key', ''))): candidate = False
        else:
            roots = [root / 'build/inputs/game' / game['key']]
            try:
                roots.append(root / 'build/projects' / helper.import_workspace.workspace_key({'game': game}, 'game'))
            except (KeyError, RuntimeError): pass
            recovery = ordinary(root / 'build/cache/recovery')
            if recovery.is_dir():
                matching = []
                import itertools
                for folder in itertools.islice(recovery.iterdir(), 64):
                    if not re.fullmatch('[0-9a-f]{64}', folder.name): continue
                    owner = ordinary(folder / 'stage-owner.json')
                    if owner.is_file() and owner.stat().st_size < 65536:
                        try:
                            value = json.loads(owner.read_text())
                            if value.get('gameKey') == game['key'] and value.get('key') == folder.name:
                                matching.append((owner.stat().st_mtime_ns, folder))
                        except (ValueError, OSError): pass
                if matching: roots.append(max(matching)[1])
            # Cache credit is planning metadata, never a reason to walk hundreds
            # of thousands of retained files before every resumed run. All roots
            # share one bounded budget; unobserved bytes receive no credit.
            deadline = time.monotonic() + MAX_SCAN_SECONDS
            for cache_root in roots:
                result = tree_bytes(cache_root, deadline=deadline); cached += result['bytes']; cache_bounded |= result['bounded']
    # Even a large existing cache does not remove native/player/package/temporary
    # output reserves. No guarantee that cached bytes will be reusable is made.
    credit = min(cached, original['bytes'] * 4)
    additional = max(TOOLS_IMPORT_RESERVE_BYTES + original['bytes'] * 2, fresh - credit)
    return {'originalBytes': original['bytes'], 'originalFiles': original['files'],
            'scanBounded': original['bounded'], 'skippedLinks': original['skippedLinks'],
            'freshEstimatedBytes': fresh, 'additionalEstimatedBytes': additional,
            'cacheReuseCandidate': candidate, 'cacheCreditBytes': credit, 'cacheScanBounded': cache_bounded,
            'cacheGameKey': game.get('key') if candidate else None,
            'method': '8x owned data + 20 GiB tool/import reserve; at most 4x owned-data cache credit',
            'certifiedExact': False}


def space_error(root, free, required, *, code="workspace_build_space_low", during=False, **parameters):
    """Put the actionable capacity figures in the displayed message and logs."""
    root = ordinary(root)
    if during:
        en = (f"Build stopped before the drive filled: {free / GIB:.1f} GiB free at {root}; "
              f"keep at least {required / GIB:.1f} GiB free. Free space and continue; completed work is retained.")
        de = (f"Build vor dem Volllaufen des Laufwerks gestoppt: {free / GIB:.1f} GiB frei in {root}; "
              f"mindestens {required / GIB:.1f} GiB frei halten. Platz freigeben und fortsetzen; abgeschlossene Arbeit bleibt erhalten.")
    else:
        en = (f"Not enough free space for this build: {free / GIB:.1f} GiB free at {root}; "
              f"approximately {required / GIB:.1f} GiB additionally required. Free space or choose a larger workspace drive, then continue. Keep the existing workspace.")
        de = (f"Zu wenig freier Speicher für diesen Build: {free / GIB:.1f} GiB frei in {root}; "
              f"voraussichtlich {required / GIB:.1f} GiB zusätzlich benötigt. Platz freigeben oder ein größeres Arbeitslaufwerk wählen, dann fortsetzen. Bestehenden Arbeitsordner behalten.")
    return WizardError(code, en, de, workspaceRoot=str(root), freeBytes=free, requiredBytes=required,
                       estimatedBytes=required, completedWorkRetained=True, **parameters)


def check_runtime_space(store_root, *, free_bytes=None):
    """Cheap polling uses only the drive counter, never another cache scan."""
    root = ordinary(store_root)
    free = shutil.disk_usage(root).free if free_bytes is None else free_bytes
    if free < MIN_RUNTIME_FREE_BYTES:
        raise space_error(root, free, MIN_RUNTIME_FREE_BYTES, code="workspace_runtime_space_low", during=True)
    return free


def qualify(store_root, *, system=None, machine=None, free_bytes=None, game_root=None, repo=None, mode="build", enforce=True):
    root = ordinary(store_root)
    system = system or platform.system(); machine = machine or platform.machine()
    if system == 'Windows':
        if machine.lower() not in ('amd64', 'x86_64'):
            raise WizardError('windows_x64_required', 'Use Windows x64 for the Unity and native builder tools.',
                              'Für Unity und die nativen Build-Werkzeuge Windows x64 verwenden.')
        if len(str(root)) > MAX_WINDOWS_ROOT_CHARS:
            raise WizardError('workspace_path_long', 'Choose a short workspace such as D:\\GHQ and relaunch with -StateRoot D:\\GHQ.',
                              'Einen kurzen Arbeitsordner wie D:\\GHQ wählen und mit -StateRoot D:\\GHQ neu starten.', maxCharacters=MAX_WINDOWS_ROOT_CHARS)
    free = shutil.disk_usage(root).free if free_bytes is None else free_bytes
    result = {'schema': 1, 'supportedAutomation': system == 'Windows', 'system': system, 'machine': machine,
              'freeBytes': free, 'minimumSetupBytes': MIN_SETUP_FREE_BYTES, 'rootCharacters': len(str(root)),
              'workspaceRoot': str(root), 'buildSpaceCheckPassed': False,
              'licenseVerified': False, 'unityAction': 'Sign in and activate an eligible license in Unity Hub; version does not verify licensing.',
              'fullBuildSpaceVerified': False}
    if game_root and mode != "build":
        import discovery
        discovery.builder(repo or Path(__file__).resolve().parents[2]).game_data(Path(game_root))
        required = 4 * GIB if mode == "update-profile" else 40 * GIB
        result["spaceEstimate"] = {"additionalEstimatedBytes": required, "scanBounded": False,
                                   "method": "APK update: retained game content; signing/code workspace reserve", "certifiedExact": False}
        result["spaceWarning"] = free < required
    elif game_root:
        result['spaceEstimate'] = space_estimate(root, game_root, repo or Path(__file__).resolve().parents[2])
        result['spaceWarning'] = free < result['spaceEstimate']['additionalEstimatedBytes']
    # The reported October 8 run had 44 GiB free and a 124 GiB additional
    # estimate, yet the old 4 GiB setup floor allowed a fresh export to fill the
    # disk. A cache candidate does not waive the remaining build reserve.
    required = result.get('spaceEstimate', {}).get('additionalEstimatedBytes', MIN_SETUP_FREE_BYTES)
    result['requiredFreeBytes'] = required
    result['spaceWarning'] = free < required
    result['buildSpaceCheckPassed'] = not result['spaceWarning'] and not result.get('spaceEstimate', {}).get('scanBounded', False)
    try:
        atomic_json(root / 'qualification.json', result)
    except OSError as error:
        # Keep the existing workspace's UI and cleanup accessible even when no
        # new diagnostic JSON fits. Capacity failures still take precedence.
        if error.errno != errno.ENOSPC and getattr(error, 'winerror', None) != 112:
            raise
        result['qualificationFileSaved'] = False
    if enforce and result.get('spaceEstimate', {}).get('scanBounded'):
        raise WizardError('workspace_space_estimate_incomplete',
                          'The bounded game-file scan did not finish, so enough build space cannot be established. Keep the workspace and save the diagnostic package before retrying.',
                          'Die begrenzte Erfassung der Spieldateigrößen wurde nicht abgeschlossen; ausreichender Build-Speicher ist damit nicht nachgewiesen. Arbeitsordner behalten und vor dem Wiederholen das Diagnosepaket speichern.',
                          workspaceRoot=str(root), freeBytes=free, spaceEstimate=result['spaceEstimate'])
    if enforce and result['spaceWarning']:
        raise space_error(root, free, required, code='workspace_build_space_low' if game_root else 'workspace_space_low',
                          minimumSetupBytes=MIN_SETUP_FREE_BYTES, spaceEstimate=result.get('spaceEstimate'))
    return result
