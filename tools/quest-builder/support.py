"""Bounded, opt-in diagnostic archives from explicit build-log allowlists only."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import json
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
SAFE_DETAIL_KEYS = {'schema', 'inputKey', 'gameKey', 'sourceHash', 'commit', 'modBuild', 'apkSha256',
                    'requested', 'hardwareVerified', 'status', 'version', 'backend', 'proceduralBackend', 'outcome'}
SAFE_EVENT_KEYS = {'phase', 'done', 'total', 'unit', 'percent', 'detail', 'updatedAt', 'executable',
                   'exitCode', 'durationSeconds', 'log', 'logs', 'cause', 'builderError', 'failureStage',
                   'error', 'message', 'traceback', 'outputCount', 'controlledStop', 'tool', 'version',
                   'freeBytes', 'estimatedBytes', 'command'}
RESOURCE_FILES = ('resource-policy.json', 'resource-events.jsonl', 'build-metrics.json')
WIZARD_LOG = re.compile(r'^(?:(?:tools|source|unity|profile|inspect|build|install)|progress(?:\.previous)?|(?:git|dotnet8|dotnet10)-version|unity-(?:hub-help|install|version|license-probe|license-process)|source-(?:clone(?:-complete)?|commit|checkout|selected-(?:present|commit)|(?:local|owned)-inventory\.json)|com\.unity\.xr\.(?:management|core-utils|openxr)-(?:init|fetch|checkout|build))\.log$')
BUILD_LOG = re.compile(r'^(unity-(build|launch)|native|weave|mod|package-(import|api)|recovery|recover|dotnet|apk-(signature|badging)|adb-[a-z-]+|campaign-[a-z-]+|build)[a-zA-Z0-9_.-]*\.log$')
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


def read_object(path, limit=4 * 1048576):
    path = ordinary(path)
    if not path.is_file(): return None
    if path.stat().st_size > limit: raise BuildError('Diagnostic metadata is unexpectedly large.')
    return json.loads(path.read_text(encoding='utf-8'))


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
    failure = read_object(build_root / 'last-failure.json')
    if isinstance(failure, dict):
        meta['buildFailure'] = {key: safe_json(failure[key], replacements) for key in ('stage', 'error', 'message') if key in failure}
    recovery_folders = recovery_workspaces(build_root, failure)
    if recovery_folders: meta['recoveryReceipts'] = recovery_receipt_stats(recovery_folders)
    # Include only the selected build's small identity fields, never copy complete
    # receipts (which contain profile data, full game inventories and file paths).
    for name in ('latest-input.json', 'latest-build.json'):
        value = read_object(build_root / name)
        if isinstance(value, dict):
            details = value.get('details', value)
            meta[name] = {key: safe_json(item, replacements) for key, item in details.items() if key in SAFE_DETAIL_KEYS}
    candidates = []
    for name in ('wizard-requests.log', 'wizard-requests.previous.log', 'storage-cleanup.log', 'storage-cleanup.previous.log'):
        path = ordinary(root / 'logs' / name)
        if path.is_file(): candidates.append((path.stat().st_mtime_ns, 'wizard/' + name, path, False))
    for log_root, prefix, accept in ((session_root / 'logs', 'wizard', lambda name: bool(WIZARD_LOG.fullmatch(name))),
                                     (build_root / 'logs', 'build', lambda name: bool(BUILD_LOG.fullmatch(name)))):
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
    # Resource records first, then newest logs. Older logs beyond bounds are listed.
    candidates.sort(key=lambda row: (row[3], row[0]), reverse=True)
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
