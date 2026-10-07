"""Bounded early setup/build-space checks; estimates do not certify reuse/license."""
from __future__ import annotations
import json
import os
from pathlib import Path
import platform
import shutil
import time
from state import WizardError, atomic_json, ordinary

GIB = 1024 ** 3
MIN_SETUP_FREE_BYTES = 4 * GIB
MAX_WINDOWS_ROOT_CHARS = 70
MAX_SCAN_FILES = 100000
MAX_SCAN_SECONDS = 3
# Full-port conversion keeps owner snapshots, recovered assets, imported Unity
# data and Android output/ZIPs concurrently. This planning reserve is intentionally
# an estimate, not a measured promise for another user's game/mod/editor/cache.
TOOLS_IMPORT_RESERVE_BYTES = 20 * GIB
CONTENT_FOOTPRINT_FACTOR = 8


def tree_bytes(root):
    root = ordinary(root); started = time.monotonic(); total = count = skipped = 0
    if not root.is_dir(): return {'bytes': 0, 'files': 0, 'bounded': False, 'skippedLinks': 0}
    for directory, dirs, files in os.walk(root, followlinks=False):
        if time.monotonic() - started >= MAX_SCAN_SECONDS:
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
            if count >= MAX_SCAN_FILES or time.monotonic() - started >= MAX_SCAN_SECONDS:
                return {'bytes': total, 'files': count, 'bounded': True, 'skippedLinks': skipped}
    return {'bytes': total, 'files': count, 'bounded': False, 'skippedLinks': skipped}


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
            for cache_root in roots:
                result = tree_bytes(cache_root); cached += result['bytes']; cache_bounded |= result['bounded']
    # Even a large existing cache does not remove native/player/package/temporary
    # output reserves. No guarantee that cached bytes will be reusable is made.
    credit = min(cached, original['bytes'] * 4)
    additional = max(TOOLS_IMPORT_RESERVE_BYTES + original['bytes'] * 2, fresh - credit)
    return {'originalBytes': original['bytes'], 'originalFiles': original['files'],
            'scanBounded': original['bounded'], 'skippedLinks': original['skippedLinks'],
            'freshEstimatedBytes': fresh, 'additionalEstimatedBytes': additional,
            'cacheReuseCandidate': candidate, 'cacheCreditBytes': credit, 'cacheScanBounded': cache_bounded,
            'method': '8x owned data + 20 GiB tool/import reserve; at most 4x owned-data cache credit',
            'certifiedExact': False}


def qualify(store_root, *, system=None, machine=None, free_bytes=None, game_root=None, repo=None):
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
    if system == 'Windows' and free < MIN_SETUP_FREE_BYTES:
        raise WizardError('workspace_space_low', 'Free at least 4 GiB before tool setup; full conversion needs substantially more.',
                          'Vor der Werkzeug-Einrichtung mindestens 4 GiB freigeben; der vollständige Umbau braucht wesentlich mehr.',
                          freeBytes=free, minimumSetupBytes=MIN_SETUP_FREE_BYTES)
    result = {'schema': 1, 'supportedAutomation': system == 'Windows', 'system': system, 'machine': machine,
              'freeBytes': free, 'minimumSetupBytes': MIN_SETUP_FREE_BYTES, 'rootCharacters': len(str(root)),
              'licenseVerified': False, 'unityAction': 'Sign in and activate an eligible license in Unity Hub; version does not verify licensing.',
              'fullBuildSpaceVerified': False}
    if game_root:
        result['spaceEstimate'] = space_estimate(root, game_root, repo or Path(__file__).resolve().parents[2])
        result['spaceWarning'] = free < result['spaceEstimate']['additionalEstimatedBytes']
        # A fresh immutable owner snapshot alone has a provable size. Do not
        # start SDK downloads if even snapshot + setup cannot fit. Candidate
        # cache reuse stays provisional and is checked later by actual hashes.
        minimum = MIN_SETUP_FREE_BYTES + (0 if result['spaceEstimate']['cacheReuseCandidate'] else result['spaceEstimate']['originalBytes'])
        if system == 'Windows' and free < minimum:
            raise WizardError('workspace_game_space_low', 'The current drive cannot fit tool setup and your game snapshot. Free space or choose a larger build drive.',
                              'Auf diesem Laufwerk passen Werkzeug-Einrichtung und Spielkopie nicht. Platz freigeben oder ein größeres Build-Laufwerk wählen.',
                              freeBytes=free, minimumBytes=minimum, estimatedBytes=result['spaceEstimate']['additionalEstimatedBytes'])
    atomic_json(root / 'qualification.json', result)
    return result
