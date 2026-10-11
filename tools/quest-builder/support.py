"""Bounded, opt-in diagnostic archives from explicit build-log allowlists only."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import uuid
import zipfile
from storage import BuildError, canonical, digest
from release import ordinary

MAX_FILE_BYTES = 2 * 1048576
MAX_TOTAL_BYTES = 24 * 1048576
MAX_FILES = 64
# Completed preparation retains per-output receipts: capture 134446 reports
# 25,864,174 journal bytes. This is a journal input bound, not an archive or
# generic metadata allowance; only identities/counts enter the support package.
MAX_PREPARATION_JOURNAL_BYTES = 64 * 1048576
SAFE_DETAIL_KEYS = {'schema', 'inputKey', 'gameKey', 'sourceHash', 'commit', 'modBuild', 'apkSha256',
                    'requested', 'hardwareVerified', 'status', 'version', 'backend', 'proceduralBackend', 'outcome'}
SAFE_EVENT_KEYS = {'phase', 'done', 'total', 'unit', 'percent', 'detail', 'updatedAt', 'executable',
                   'exitCode', 'durationSeconds', 'log', 'logs', 'cause', 'builderError', 'failureStage',
                   'error', 'message', 'traceback', 'outputCount', 'controlledStop', 'tool', 'version',
                   'freeBytes', 'estimatedBytes', 'command', 'nativeMemory', 'backupPath'}
RESOURCE_FILES = ('resource-policy.json', 'resource-events.jsonl', 'build-metrics.json')
WIZARD_LOG = re.compile(r'^(?:(?:tools|source|unity|profile|inspect|build|install)|progress(?:\.previous)?|(?:git|dotnet8|dotnet10)-version|unity-(?:hub-help|hub-window|install|install-path|version|license-probe|license-process|protocol-(?:query|recheck|register|confirm))|source-(?:clone(?:-complete)?|commit|checkout|selected-(?:present|commit)|(?:local|owned)-inventory\.json)|com\.unity\.xr\.(?:management|core-utils|openxr)-(?:init|fetch|checkout|build))\.log$')
BUILD_LOG = re.compile(r'^(unity-(build|launch)|native|weave|mod|package-(import|api)|recovery|recover|dotnet|apk-(signature|badging)|adb-[a-z-]+|campaign-[a-z-]+|build)[a-zA-Z0-9_.-]*\.log$')
# These command logs are written after Unity has produced the Player. Capture
# 075701 lost the failed zipalign log because the old allowlist excluded it;
# retain exact known names, without accepting arbitrary update/profile files.
UPDATE_LOGS = frozenset({'update-align.log', 'update-sign.log', 'update-signature.log',
                        'update-base-signature.log', 'update-key-certificate.log',
                        'update-code-launch.log', 'update-code-unity.log',
                        'update-sdk-launch.log', 'update-sdk-unity.log',
                        'update-package-api.log'})
SENSITIVE_KEY = re.compile(r'(?i)(token|password|passwd|secret|credential|authorization|license|entitlement|steamid|providerid|accountid|displayname|persona)')


def redact(text, replacements=()):
    """Remove common credentials plus exact selected paths/account values."""
    count = 0
    for value, label in sorted(replacements, key=lambda row: len(row[0]), reverse=True):
        if isinstance(value, str) and len(value) >= 3:
            count += text.count(value); text = text.replace(value, label)
            if '\\' in value:
                alternate = value.replace('\\', '\\\\'); count += text.count(alternate); text = text.replace(alternate, label)
    # Remove complete sensitive log lines, not just a guess at token punctuation.
    lines = []
    for line in text.splitlines(keepends=True):
        if re.fullmatch(r'[A-Z][A-Z0-9_]{2,}=.*', line.strip()) or re.search(r'(?i)(cookie\s*[:=]|[a-z0-9_-]*(?:secret|api[_-]?key|token)[a-z0-9_-]*\s*["\x27]?\s*[:=]|authorization\s*[:=]|(?:access|refresh|id)[_-]?token\s*["\x27]?\s*[:=]|password\s*[:=]|(?:license|entitlement)[_-]?(?:key|token|data|signature)\s*[:=]|client[_-]?secret\s*[:=]|serial\s*(?:number)?\s*[:=]|(?:steamid(?:64)?|providerid|accountid|displayname|personaname)\s*["\x27]?\s*[:=])', line):
            lines.append('[redacted sensitive log line]\n'); count += 1
        else: lines.append(line)
    text = ''.join(lines)
    patterns = (
        (r'(?i)(https?://)[^/\s:@]+:[^/\s@]+@', r'\1[redacted userinfo]@'),
        (r'(?i)Bearer\s+[A-Za-z0-9._~+/=-]+', 'Bearer [redacted]'),
        (r'\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '[redacted JWT]'),
        (r'\b7656119\d{10}\b', '[profile-id]'),
        (r'(https?://[^\s?"<>]+)\?[^\s"<>]+', r'\1?[redacted query]'),
        (r'(unityhub://[^\s?"<>]+)\?[^\s"<>]+', r'\1?[redacted query]'),
        (r'(?is)<(?:Serial|Signature|AccessToken|RefreshToken)>.*?</(?:Serial|Signature|AccessToken|RefreshToken)>', '[redacted license/token field]'),
        (r'-----BEGIN [^-]+-----[\s\S]*?-----END [^-]+-----', '[redacted key block]'),
    )
    for pattern, replacement in patterns: text, n = re.subn(pattern, replacement, text); count += n
    return text, count


def safe_json(value, replacements=(), depth=0):
    if depth > 12: return '[depth limit]'
    if isinstance(value, dict): return {str(key): ('[redacted]' if SENSITIVE_KEY.search(str(key)) else safe_json(item, replacements, depth + 1)) for key, item in list(value.items())[:256]}
    if isinstance(value, list): return [safe_json(item, replacements, depth + 1) for item in value[:512]]
    if isinstance(value, str): return redact(value[:16384], replacements)[0]
    if value is None or type(value) in (bool, int, float): return value
    return '[unsupported value]'


def failed_command_log(build_root, failure):
    """Name only an allowlisted, directly owned log from the failure message."""
    if not isinstance(failure, dict): return None
    message = failure.get('message')
    if not isinstance(message, str): return None
    match = re.fullmatch(r'[^\r\n]*; inspect ([^\r\n]+[\\/](?P<name>[^\\/]+\.log))', message.splitlines()[0] if message else '')
    if not match or not (BUILD_LOG.fullmatch(match['name']) or match['name'] in UPDATE_LOGS): return None
    path = build_root / 'logs' / match['name']
    normalize = lambda value: os.path.normcase(str(value).replace('\\', '/'))
    if normalize(match[1]) != normalize(path): return None
    return 'build/' + match['name']


def installation_logs(root, session_root, state):
    """Select only the current install attempt's explicitly named ADB log."""
    try:
        stage = next((row for row in state.get('stages', []) if row.get('id') == 'install'), {})
        started = stage.get('startedAt')
        if isinstance(started, bool) or not isinstance(started, (int, float)) or not math.isfinite(started) or started <= 0:
            return []
        log = ordinary(session_root / 'logs/install.log')
        if not log.is_file() or log.stat().st_mtime < started: return []
        with log.open('rb') as stream:
            stream.seek(max(0, log.stat().st_size - 8192))
            tail = stream.read(8192).decode('utf-8', errors='replace')
        names = re.findall(r'^ADB log: ([^\r\n]+)\r?$', tail, re.MULTILINE)
        if not names: return []
        reference = names[-1]
        name = reference.replace('\\', '/').rsplit('/', 1)[-1]
        if not re.fullmatch(r'wireless-install-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{8}\.log', name): return []
        path = root / 'device' / name
        normalize = lambda value: os.path.normcase(str(value).replace('\\', '/'))
        if normalize(reference) != normalize(path): return []
        path = ordinary(path)
        if not path.is_file() or path.stat().st_mtime < started: return []
        return [('device/' + name, path)]
    except (BuildError, OSError, ValueError):
        # Missing/linked diagnostics must not prevent saving the support report.
        return []


def read_object(path, limit=4 * 1048576):
    path = ordinary(path)
    if not path.is_file(): return None
    if path.stat().st_size > limit: raise BuildError('Diagnostic metadata is unexpectedly large.')
    # Stat alone cannot bound a read when the running build extends its journal.
    with path.open('rb') as stream: raw = stream.read(limit + 1)
    if len(raw) > limit: raise BuildError('Diagnostic metadata grew beyond its read limit.')
    return json.loads(raw.decode('utf-8'))


def signing_recovery_stats(root, session_root, state, *, diagnostic=None):
    """Summarize only the current installation's explicitly named transaction.

    Capture112552 included the ADB failure but not the durable recovery frontier.
    Never archive app files, tar members, filenames or an unfiltered journal;
    counts and completed archive sizes are sufficient to diagnose resumption.
    Capture120937 still omitted this optional summary. Its selected ADB log,
    transaction trailer and build identity match, but the archive cannot prove
    whether its local journal was absent, inaccessible or invalid. Record only a
    fixed reason code for that named current attempt; never scan other recovery
    folders or expose exception text containing private paths/member names.
    """
    selected = False
    reason = 'journal-unreadable'

    def unavailable(code):
        if diagnostic is not None:
            diagnostic.update(reason=code, hardwareVerified=False)
        return None

    try:
        if not installation_logs(root, session_root, state): return None
        log = ordinary(session_root / 'logs/install.log')
        with log.open('rb') as stream:
            stream.seek(max(0, log.stat().st_size - 16384))
            tail = stream.read(16384).decode('utf-8', errors='replace')
        matches = re.findall(r'Original recovery data is retained at ([^\r\n]+?)\. Retry with the same APK and headset\.', tail)
        if not matches: return None
        selected = True
        normalized = matches[-1].replace('\\', '/')
        base = str(root / 'device/signing-recovery').replace('\\', '/')
        if not os.path.normcase(normalized).startswith(os.path.normcase(base) + '/'):
            return unavailable('path-binding-mismatch')
        parts = normalized[len(base) + 1:].split('/')
        if (len(parts) != 2 or not re.fullmatch(r'[A-Za-z0-9_-]{1,128}', parts[0])
                or not re.fullmatch(r'[0-9a-f]{32}', parts[1])):
            return unavailable('path-binding-mismatch')
        reason = 'journal-path-unsafe'
        home = ordinary(root / 'device/signing-recovery' / parts[0])
        journal = ordinary(home / 'active.json')
        reason = 'journal-unreadable'
        if not journal.is_file(): return unavailable('journal-missing')
        if journal.stat().st_size > 32 * 1048576: return unavailable('journal-oversized')
        try:
            value = read_object(journal, limit=32 * 1048576)
        except ValueError:
            return unavailable('journal-invalid')
        if (not isinstance(value, dict) or value.get('schema') != 1
                or value.get('package') != 'dev.gloomhavenvr.quest'
                or value.get('deviceSerial') != parts[0] or value.get('transaction') != parts[1]):
            return unavailable('journal-identity-mismatch')
        reason = 'build-receipt-unreadable'
        latest = read_object(root / 'build/latest-build.json')
        if latest is None: return unavailable('build-receipt-missing')
        latest = latest.get('details', latest) if isinstance(latest, dict) else {}
        if not isinstance(latest, dict): return unavailable('build-receipt-invalid')
        if (not re.fullmatch(r'[0-9a-f]{64}', str(value.get('apkSha256', '')))
                or not re.fullmatch(r'[0-9a-f]{64}', str(value.get('inputKey', '')))
                or value.get('apkSha256') != latest.get('apkSha256')
                or value.get('inputKey') != latest.get('inputKey')):
            return unavailable('build-identity-mismatch')
        reason = 'backup-path-unsafe'
        folder = ordinary(home / parts[1])
        reason = 'journal-invalid'
        if not isinstance(value.get('roots', []), list) or not isinstance(value.get('banks', []), list):
            return unavailable(reason)
        roots = []
        for item in value.get('roots', [])[:4]:
            if not isinstance(item, dict) or item.get('name') not in ('private', 'device-protected', 'external'): continue
            inventory = item.get('inventory', {})
            if not isinstance(inventory, dict): continue
            row = {'name': item['name'], 'journalled': True,
                   'files': sum(isinstance(v, dict) and v.get('type') == 'file' for v in inventory.values()),
                   'directories': sum(isinstance(v, dict) and v.get('type') == 'directory' for v in inventory.values()),
                   'symlinks': sum(isinstance(v, dict) and v.get('type') == 'symlink' for v in inventory.values())}
            archive = item.get('archive')
            if isinstance(archive, str) and re.fullmatch(r'(?:private|device-protected|external)-[0-9a-f]{32}\.tar', archive):
                reason = 'backup-path-unsafe'
                path = ordinary(folder / archive)
                reason = 'backup-stat-unreadable'
                row['archivePresent'] = path.is_file()
                if path.is_file(): row['archiveBytes'] = path.stat().st_size
            roots.append(row)
        reason = 'backup-path-unsafe'
        original = ordinary(folder / 'original.apk')
        reason = 'backup-stat-unreadable'
        states = ('created', 'backed-up', 'parking', 'parked', 'uninstalling', 'removed', 'installing',
                  'restoring', 'rolling-back', 'rollback-restoring', 'verified', 'complete')
        return {'state': value.get('state') if value.get('state') in states else 'unknown',
                'apkSha256': value['apkSha256'], 'inputKey': value['inputKey'],
                'originalApkPresent': original.is_file(),
                'originalApkBytes': original.stat().st_size if original.is_file() else 0,
                'roots': roots, 'retainedBanks': [name for name in value.get('banks', [])
                                                  if name in ('files/quest-owned-game', 'files/quest-mod-resources')],
                'hardwareVerified': False}
    except (BuildError, OSError, ValueError, TypeError, AttributeError):
        # Optional metadata must not suppress the actual failure logs.
        return unavailable(reason) if selected else None


def recovery_workspaces(build_root, failure=None):
    """Select diagnostic folders, including an observed raw-export binding.

    This is log selection, not qualification of reusable game bytes. Only an
    owner-labelled binding with matching current key can name another hash folder;
    no arbitrary path from metadata is followed. Before binding publication a
    migration error may have no current folder, so expose the two recent retained
    folders as candidates, without claiming either is the failed export.
    """
    root = ordinary(build_root / 'cache/full-original-recovery')
    if not root.is_dir(): return []
    key = failure.get('key') if isinstance(failure, dict) and failure.get('stage') == 'recovery' else None
    folders = []
    if isinstance(key, str) and re.fullmatch(r'[0-9a-f]{64}', key):
        current = ordinary(root / key)
        if current.is_dir(): folders.append((current, 'failure-key'))
        try:
            binding = read_object(build_root / 'cache/raw-recovery-resume' / (key + '.json'), limit=65536)
            if (isinstance(binding, dict) and binding.get('schema') == 1
                    and binding.get('owner') == 'Quest raw recovery resume'
                    and binding.get('currentRecipeKey') == key
                    and re.fullmatch(r'[0-9a-f]{64}', str(binding.get('originalWorkspaceKey', '')))):
                bound = ordinary(root / binding['originalWorkspaceKey'])
                if bound.is_dir() and bound != current: folders.insert(0, (bound, 'raw-resume-binding'))
        except (OSError, ValueError, BuildError):
            pass  # Damaged binding must not suppress the actual failure logs.
    if not folders:
        recent = sorted((ordinary(row) for row in root.iterdir() if re.fullmatch(r'[0-9a-f]{64}', row.name)
                         and not row.is_symlink() and row.is_dir()), key=lambda row: row.stat().st_mtime, reverse=True)[:2]
        folders = [(row, 'recent-candidate') for row in recent]
    return folders[:2]


def recovery_receipt_stats(folders):
    """Sizes/existence only; never read or export proprietary asset catalogs."""
    result = []
    for folder, selection in folders:
        files = []
        for name in ('core-recovery.json', 'original-source.json', 'core-identities.jsonl',
                     'RecoveredProject/quest-full-recovery-progress.json', 'BundleRecovery/merge-pending.json'):
            path = ordinary(folder / name)
            row = {'name': name, 'exists': path.is_file()}
            if row['exists']: row['bytes'] = path.stat().st_size
            files.append(row)
        result.append({'workspaceKey': folder.name, 'selection': selection, 'files': files,
                       'contentVerified': False})
    return result


def preparation_receipt_stats(build_root):
    """Export checkpoint identities/counts, never output paths or original data.

    The previous support capture showed a repeated preparation but omitted its
    journal frontier. Read at most two bounded owned journals without touching the
    retained project, Library, output manifests or asset verification indexes.
    """
    root = ordinary(build_root / 'cache/prepare-resume')
    if not root.is_dir(): return []
    folders = sorted((row for row in root.iterdir() if re.fullmatch(r'[0-9a-f]{64}', row.name)
                      and not row.is_symlink() and row.is_dir()),
                     key=lambda row: row.stat().st_mtime_ns, reverse=True)[:2]
    operations = {'project-files', 'startup-content', 'native-runtime', 'audio',
                  'textures', 'graphics', 'mod-banks', 'compiler-adapters', 'preparation-contracts'}
    def checkpoint(value):
        if not isinstance(value, dict) or not re.fullmatch(r'[a-z][a-z0-9-]{0,79}', str(value.get('name', ''))):
            return {'invalid': True}
        return {'name': value['name'], 'operation': value.get('operation') if value.get('operation') in operations else 'unknown',
                'outputCount': len(value['outputs']) if isinstance(value.get('outputs'), list) else None}
    result = []
    for folder in folders:
        row = {'workspaceKey': folder.name, 'contentVerified': False}
        try:
            value = read_object(folder / 'journal.json', limit=MAX_PREPARATION_JOURNAL_BYTES)
            if (not isinstance(value, dict) or value.get('schema') != 1
                    or value.get('owner') != 'Quest preparation substage journal'
                    or value.get('project') != 'projects/' + folder.name):
                row['journalStatus'] = 'missing-or-unowned'
            else:
                steps = value.get('steps')
                row.update(journalStatus='observed', inputKey=value.get('inputKey')
                           if re.fullmatch(r'[0-9a-f]{64}', str(value.get('inputKey', ''))) else None,
                           target=value.get('target') if value.get('target') in ('game', 'startup', 'probe') else None,
                           recipe=value.get('recipe') if type(value.get('recipe')) is int else None,
                           completedCount=len(steps) if isinstance(steps, list) else None,
                           completed=[checkpoint(step) for step in steps[:128]] if isinstance(steps, list) else [],
                           metadataRefreshPending=ordinary(folder / 'metadata-refresh.json').is_file(),
                           fileRepairPending=ordinary(folder / 'repair.json').is_file())
                repairs = value.get('fileRepairs')
                if isinstance(repairs, dict):
                    recent = repairs.get('recent', [])
                    row['fileRepairs'] = safe_json({'count': repairs.get('count'),
                                                  'recent': recent[-32:] if isinstance(recent, list) else []})
                pending = value.get('pending')
                row['pending'] = checkpoint(pending) if pending is not None else None
        except (OSError, ValueError, BuildError):
            row['journalStatus'] = 'unreadable-or-oversized'
        result.append(row)
    return result


def compute_contract_stats(build_root, preparation, replacements=()):
    """Report bounded owner identities, never source programs or asset inventories.

    Capture 092443 includes the actual ancillary-bank hash but omits the
    corresponding exported GUID/kernel contract. Read only this one small,
    declared manifest per observed game project; never enumerate Assets/Library
    or treat diagnostic metadata as qualification of the retained Player.
    """
    result = []
    hexadecimal = lambda value, width: value if isinstance(value, str) and re.fullmatch(
        '[0-9a-f]{' + str(width) + '}', value) else None
    for project in preparation[:2]:
        key = hexadecimal(project.get('workspaceKey'), 64)
        if not key or project.get('target') != 'game' or project.get('journalStatus') != 'observed':
            continue
        row = {'workspaceKey': key, 'contentVerified': False}
        try:
            path = build_root / 'projects' / key / 'Assets/QuestOriginalCampaign/campaign-computes.json'
            value = read_object(path, limit=1048576)
            if value is None:
                row['manifestStatus'] = 'missing'
            elif not isinstance(value, dict) or value.get('schema') != 1 or not isinstance(value.get('shaders'), list):
                row['manifestStatus'] = 'invalid'
            else:
                owners = []
                for owner in value['shaders'][:13]:
                    if not isinstance(owner, dict): continue
                    kernels = owner.get('kernels')
                    owners.append({
                        'name': safe_json(owner['name'][:64], replacements) if isinstance(owner.get('name'), str) else None,
                        'guid': hexadecimal(owner.get('guid'), 32),
                        'classId': owner.get('classId') if type(owner.get('classId')) is int else None,
                        'localFileId': owner.get('localFileId') if type(owner.get('localFileId')) is int else None,
                        'sourceSha256': hexadecimal(owner.get('sourceSha256'), 64),
                        'kernelCount': len(kernels) if isinstance(kernels, list) else None,
                        'kernels': [{'name': safe_json(kernel['name'][:128], replacements) if isinstance(kernel.get('name'), str) else None,
                                     'originalDxbcSha256': hexadecimal(kernel.get('originalDxbcSha256'), 64),
                                     'threadGroups': kernel.get('threadGroups') if isinstance(kernel.get('threadGroups'), list)
                                         and len(kernel['threadGroups']) == 3 and all(type(n) is int and 0 <= n <= 65536
                                             for n in kernel['threadGroups']) else None}
                                    for kernel in kernels[:36] if isinstance(kernel, dict)] if isinstance(kernels, list) else []})
                row.update(manifestStatus='observed', shaderCount=len(value['shaders']), shaders=owners)
        except (OSError, ValueError, BuildError, RecursionError):
            row['manifestStatus'] = 'unreadable-or-oversized'
        result.append(row)
    return result


def recovery_logs(build_root, failure=None, *, workspaces=None):
    """Known export logs only; never enumerate assets or recurse through caches."""
    folders = recovery_workspaces(build_root, failure) if workspaces is None else workspaces
    result = []
    for folder, _ in folders:
        for name in ('core-export.log', 'core-export.console.log'):
            core = ordinary(folder / name)
            if core.is_file(): result.append(('recovery/' + folder.name + '/' + name, core))
        batches = ordinary(folder / 'BundleRecovery')
        if not batches.is_dir(): continue
        # At most the newest eight batch logs, including the failing export.
        rows = sorted((ordinary(row / 'export.log') for row in batches.iterdir()
                       if re.fullmatch(r'batch-\d{3,6}', row.name) and not row.is_symlink() and row.is_dir()),
                      key=lambda row: row.stat().st_mtime if row.is_file() else 0, reverse=True)[:8]
        for row in rows:
            for path in (row, ordinary(row.with_name('export.console.log'))):
                if path.is_file(): result.append(('recovery/' + folder.name + '/' + row.parent.name + '/' + path.name, path))
    return result


def procedural_logs(build_root):
    """Only the latest known native build/audit files, never DLL/data inventories."""
    result = []
    for backend in ('procedural-runtime-proton', 'procedural-runtime'):
        root = ordinary(build_root / 'tool-cache/campaign-native' / backend)
        if not root.is_dir(): continue
        folders = sorted((ordinary(row) for row in root.iterdir()
                          if re.fullmatch(r'[0-9a-f]{64}', row.name) and not row.is_symlink() and row.is_dir()),
                         key=lambda row: row.stat().st_mtime_ns, reverse=True)[:2]
        for folder in folders:
            for name in ('native-build.log', 'proton-stage-summary.json'):
                path = ordinary(folder / name)
                if path.is_file(): result.append(('procedural/' + backend + '/' + folder.name + '/' + name, path))
    return result


def export_support(state_root, session, destination=None):
    """Snapshot even a running/failed build without altering stage receipts."""
    root = ordinary(state_root)
    if not re.fullmatch('[0-9a-f]{32}', str(session)): raise BuildError('Invalid wizard session ID.')
    if read_object(root / 'wizard-owner.json') != {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'}:
        raise BuildError('Diagnostic source is not a wizard-owned workspace.')
    session_root = ordinary(root / 'sessions' / session)
    state = read_object(session_root / 'state.json')
    if not isinstance(state, dict) or state.get('session') != session: raise BuildError('Missing diagnostic session.')
    choices = state.get('choices', {})
    replacements = [(str(root), '<workspace>'), (str(Path.home()), '<user-home>')]
    for key, label in (('gameRoot', '<owned-game>'), ('sourceRoot', '<source>'), ('steamRoot', '<store>')):
        if isinstance(choices.get(key), str): replacements.append((choices[key], label))
    # These inputs are consulted solely to redact values, never archived.
    profiles = [choices.get('profile', {}), read_object(session_root / 'profile.json') or {}]
    for profile in profiles:
        for key in ('steamId', 'providerId', 'accountId', 'displayName'):
            if profile.get(key) is not None: replacements.append((str(profile[key]), '<profile>'))
    if choices.get('steamId'): replacements.append((str(choices['steamId']), '<profile>'))
    stages = []
    for stage in state.get('stages', [])[:16]:
        stages.append({key: safe_json(stage[key], replacements) for key in ('id', 'status', 'attempts', 'progress', 'waiting', 'durationSeconds') if key in stage} |
                      {'details': {key: safe_json(value, replacements) for key, value in stage.get('details', {}).items() if key in SAFE_DETAIL_KEYS}})
    meta = {'schema': 1, 'kind': 'GloomhavenVR Quest build support', 'createdUtc': datetime.now(timezone.utc).isoformat(),
            'session': session, 'status': state.get('status'), 'created': state.get('created'), 'updated': state.get('updated'),
            'host': {'system': platform.system(), 'machine': platform.machine(), 'python': platform.python_version(), 'logicalCpuCount': os.cpu_count()},
            'stages': stages, 'needsActions': safe_json(state.get('needsActions', []), replacements),
            'events': [{key: safe_json(event[key], replacements) for key in ('sequence', 'time', 'code', 'stage') if key in event} |
                       {'parameters': {key: safe_json(value, replacements) for key, value in event.get('parameters', {}).items() if key in SAFE_EVENT_KEYS}}
                       for event in state.get('events', [])[-128:]],
            'limits': {'fileBytes': MAX_FILE_BYTES, 'totalBytes': MAX_TOTAL_BYTES, 'files': MAX_FILES},
            'files': [], 'omitted': [], 'hardwareVerified': False}
    repo = Path(__file__).resolve().parents[2]
    protocol = ordinary(repo / 'src/GloomhavenVR/Net/NetProtocol.cs')
    if protocol.is_file() and protocol.stat().st_size <= 1048576:
        match = re.search(r'const (?:int|ushort) ModBuild\s*=\s*(\d+)', protocol.read_text(encoding='utf-8'))
        if match: meta['modBuild'] = int(match[1])
    release = read_object(repo / 'quest-builder-release.json')
    if release: meta['release'] = {key: release[key] for key in ('sourceCommit', 'modBuild', 'schema') if key in release}
    qualification = read_object(root / 'qualification.json')
    if qualification: meta['qualification'] = safe_json(qualification, replacements)
    build_root = ordinary(root / 'build')
    signing_diagnostic = {}
    signing = signing_recovery_stats(root, session_root, state, diagnostic=signing_diagnostic)
    if signing: meta['signingRecovery'] = signing
    elif signing_diagnostic: meta['signingRecoveryUnavailable'] = signing_diagnostic
    failure = read_object(build_root / 'last-failure.json')
    if isinstance(failure, dict):
        meta['buildFailure'] = {key: safe_json(failure[key], replacements) for key in ('stage', 'error', 'message') if key in failure}
    recovery_folders = recovery_workspaces(build_root, failure)
    if recovery_folders: meta['recoveryReceipts'] = recovery_receipt_stats(recovery_folders)
    preparation = preparation_receipt_stats(build_root)
    if preparation: meta['preparationReceipts'] = preparation
    compute = compute_contract_stats(build_root, preparation, replacements)
    if compute: meta['computeContracts'] = compute
    # Include only the selected build's small identity fields, never copy complete
    # receipts (which contain profile data, full game inventories and file paths).
    for name in ('latest-input.json', 'latest-build.json'):
        value = read_object(build_root / name)
        if isinstance(value, dict):
            details = value.get('details', value)
            meta[name] = {key: safe_json(item, replacements) for key, item in details.items() if key in SAFE_DETAIL_KEYS}
    candidates = []
    network_report = ordinary(session_root / 'network-preflight.json')
    if network_report.is_file():
        candidates.append((network_report.stat().st_mtime_ns, 'wizard/network-preflight.json', network_report, True))
    for name in ('wizard-requests.log', 'wizard-requests.previous.log', 'storage-cleanup.log', 'storage-cleanup.previous.log'):
        path = ordinary(root / 'logs' / name)
        if path.is_file(): candidates.append((path.stat().st_mtime_ns, 'wizard/' + name, path, False))
    for log_root, prefix, accept in ((session_root / 'logs', 'wizard', lambda name: bool(WIZARD_LOG.fullmatch(name))),
                                     (build_root / 'logs', 'build', lambda name: bool(BUILD_LOG.fullmatch(name)) or name in UPDATE_LOGS)):
        log_root = ordinary(log_root)
        if not log_root.is_dir(): continue
        # Never recurse into tool caches, games, Unity projects or save folders.
        for path in log_root.iterdir():
            if accept(path.name):
                path = ordinary(path)
                if path.is_file(): candidates.append((path.stat().st_mtime_ns, prefix + '/' + path.name, path, False))
    for name in RESOURCE_FILES:
        path = ordinary(build_root / 'evidence' / name)
        if path.is_file(): candidates.append((path.stat().st_mtime_ns, 'resources/' + name, path, True))
    for name, path in recovery_logs(build_root, failure, workspaces=recovery_folders):
        candidates.append((path.stat().st_mtime_ns, name, path, False))
    for name, path in procedural_logs(build_root):
        candidates.append((path.stat().st_mtime_ns, name, path, name.endswith('.json')))
    for name, path in installation_logs(root, session_root, state):
        candidates.append((path.stat().st_mtime_ns, name, path, False))
    # The actual failed child comes before historic Unity logs even if those
    # were copied/touched later. Keep resource and current Wizard context next,
    # then the bounded host-delivery command set. The existing byte/file caps
    # and credential redaction still apply to every selected record.
    failed_log = failed_command_log(build_root, failure)
    def priority(row):
        _, name, _, structured = row
        return (5 if name == failed_log else 4 if structured else
                3 if name in ('wizard/build.log', 'wizard/install.log', 'wizard/progress.log') or name.startswith('device/') else
                2 if name.startswith('build/') and name[6:] in UPDATE_LOGS else 1)
    candidates.sort(key=lambda row: (priority(row), row[0]), reverse=True)
    archive_rows = []; used = 0
    for _, name, path, structured in candidates:
        if len(archive_rows) >= MAX_FILES or used >= MAX_TOTAL_BYTES:
            meta['omitted'].append({'name': name, 'reason': 'archive limit'}); continue
        limit = min(MAX_FILE_BYTES, MAX_TOTAL_BYTES - used); original_size = path.stat().st_size
        with path.open('rb') as stream:
            if original_size <= limit: raw = stream.read(limit)
            else:
                marker = b'\n[... diagnostic middle omitted ...]\n'
                first = max(0, limit // 4 - len(marker)); tail = limit - first - len(marker)
                raw = stream.read(first); stream.seek(max(first, original_size - tail))
                raw += marker + stream.read(tail)
        decoded = raw.decode('utf-8', errors='replace')
        text, redactions = redact(decoded, replacements)
        if structured and original_size > limit:
            text = '[Resource record exceeds diagnostic size bounds; inspect the retained local record.]\n'
        elif structured:
            try:
                if name.endswith('.jsonl'): text = '\n'.join(json.dumps(safe_json(json.loads(line), replacements), ensure_ascii=False) for line in decoded.splitlines() if line.strip()) + '\n'
                else: text = json.dumps(safe_json(json.loads(decoded), replacements), ensure_ascii=False, indent=2) + '\n'
            except ValueError: text = '[Resource record was incomplete during export; retry after the current stage.]\n'
        delivered = text.encode('utf-8')
        if len(delivered) > limit:
            marker = b'\n[... redacted diagnostic middle omitted ...]\n'
            if limit <= len(marker): delivered = delivered[-limit:]
            else:
                first = (limit - len(marker)) // 4
                delivered = delivered[:first] + marker + delivered[-(limit - first - len(marker)):]
        used += len(delivered)
        archive_rows.append((name, delivered))
        meta['files'].append({'name': name, 'sourceBytes': original_size, 'exportedBytes': len(delivered),
                              'truncated': original_size > limit, 'redactions': redactions, 'sha256': hashlib.sha256(delivered).hexdigest()})
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    destination = ordinary(destination or root / 'exports' / ('quest-build-support-' + stamp + '-' + uuid.uuid4().hex[:8] + '.zip'))
    destination.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation prevents an export from overwriting any existing user file.
    with destination.open('xb') as stream:
        with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('diagnostic.json', canonical(meta) + b'\n')
            for name, raw in archive_rows: archive.writestr(name, raw)
    return {'schema': 1, 'event': 'support_exported', 'path': str(destination), 'name': destination.name,
            'sha256': digest(destination), 'fileCount': len(archive_rows) + 1, 'bytes': destination.stat().st_size}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--state-root', type=Path, default=Path.home() / '.ghvrq')
    parser.add_argument('--session'); parser.add_argument('--output', type=Path)
    args = parser.parse_args(argv)
    try:
        session = args.session or read_object(args.state_root / 'latest-session.json')['session']
        print(json.dumps(export_support(args.state_root, session, args.output))); return 0
    except (BuildError, OSError, ValueError, KeyError, TypeError) as error:
        print('Build support export: ' + str(error)); return 1

if __name__ == '__main__': raise SystemExit(main())
