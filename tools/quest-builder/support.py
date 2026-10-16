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
MAX_WORKSPACE_RUNS = 128
MAX_WORKSPACE_MANIFESTS = 32
MAX_INSTALLATION_RUNS = 3
# Completed preparation retains per-output receipts: capture 134446 reports
# 25,864,174 journal bytes. This is a journal input bound, not an archive or
# generic metadata allowance; only identities/counts enter the support package.
MAX_PREPARATION_JOURNAL_BYTES = 64 * 1048576
SAFE_DETAIL_KEYS = {'schema', 'inputKey', 'gameKey', 'sourceHash', 'commit', 'modBuild', 'apkSha256',
                    'requested', 'hardwareVerified', 'status', 'version', 'backend', 'proceduralBackend', 'outcome',
                    'updateStrategy', 'requiresUnity', 'rebuildReasons'}
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
        # Start credential identifiers at a word boundary. A bounded 2-MiB
        # token-free line otherwise retries a greedy prefix at every character.
        if re.fullmatch(r'[A-Z][A-Z0-9_]{2,}=.*', line.strip()) or re.search(r'(?i)(cookie\s*[:=]|\b[a-z0-9_-]*(?:secret|api[_-]?key|token)[a-z0-9_-]*\s*["\x27]?\s*[:=]|authorization\s*[:=]|(?:access|refresh|id)[_-]?token\s*["\x27]?\s*[:=]|password\s*[:=]|(?:license|entitlement)[_-]?(?:key|token|data|signature)\s*[:=]|client[_-]?secret\s*[:=]|serial\s*(?:number)?\s*[:=]|(?:steamid(?:64)?|providerid|accountid|displayname|personaname)\s*["\x27]?\s*[:=])', line):
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
        (r'(?i)\b[A-Z]:[\\/]+Users[\\/]+[^\\/\s"<>]+', '<user-home>'),
        (r'/(?:home|Users)/[^/\s"<>]+', '<user-home>'),
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


def optional_object(path, omitted, name, limit=4 * 1048576):
    """An incomplete optional diagnostic must not prevent exporting other logs."""
    try:
        return read_object(path, limit=limit)
    except (BuildError, OSError, ValueError, TypeError, RecursionError):
        omitted.append({'name': name, 'reason': 'unreadable or incomplete metadata'})
        return None


def add_log(candidates, name, path, structured, omitted, *, strict_links=False):
    """Collect a directly owned optional log, without following a link."""
    try:
        path = ordinary(path)
        if path.is_file(): candidates.append((path.stat().st_mtime_ns, name, path, structured))
    except BuildError:
        if strict_links: raise
        omitted.append({'name': name, 'reason': 'unsafe or unavailable log'})
    except (OSError, ValueError):
        omitted.append({'name': name, 'reason': 'unsafe or unavailable log'})


def account_replacements(folder, state, replacements, omitted):
    """Read only selected redaction inputs, never include account/profile data."""
    choices = state.get('choices', {})
    if not isinstance(choices, dict): choices = {}
    for key, label in (('gameRoot', '<owned-game>'), ('sourceRoot', '<source>'), ('steamRoot', '<store>')):
        if isinstance(choices.get(key), str): replacements.append((choices[key], label))
    profile = optional_object(folder / 'profile.json', omitted, 'runs/' + folder.name + '/profile.json', 128 * 1024)
    for value in (choices.get('profile'), profile, choices):
        if not isinstance(value, dict): continue
        for key in ('steamId', 'providerId', 'accountId', 'displayName'):
            if value.get(key) is not None: replacements.append((str(value[key]), '<profile>'))


def run_summary(state, removed, replacements):
    """Report retained identities and observed status; never qualify ownership."""
    choices = state.get('choices', {})
    result = {key: safe_json(value[:80] if isinstance(value, str) else value, replacements)
              for key, value in state.items() if key in
              ('schema', 'session', 'status', 'created', 'updated', 'revision') and
              (value is None or type(value) in (str, int, float, bool))}
    result.update(removed=removed, contentVerified=False)
    # A workspace export may have no selected session. Capture183123 therefore
    # showed an old compiler failure at the top while its live ADB timeout was
    # visible only inside progress.log. Preserve each blocked run's current
    # small action independently of historical build/last-failure.json.
    if state.get('status') in ('blocked', 'failed') and isinstance(state.get('needsActions'), list):
        actions = []
        for action in state['needsActions'][:4]:
            if not isinstance(action, dict) or not re.fullmatch(r'[a-z0-9_-]{1,80}', str(action.get('code', ''))): continue
            item = {'code': action['code'], 'message': safe_json(action.get('message', {}), replacements)}
            parameters = action.get('parameters', {})
            if isinstance(parameters, dict):
                item['parameters'] = {key: safe_json(parameters[key], replacements) for key in
                    ('failedDependencies', 'completedWorkRetained', 'path', 'cause', 'errorType', 'retryable',
                     'winerror', 'retryAttempts', 'readOnlyRepaired', 'failureStage') if key in parameters}
            actions.append(item)
        if actions: result['needsActions'] = actions
    if isinstance(choices, dict) and choices.get('mode') in ('build', 'update-mod', 'update-profile'):
        result['mode'] = choices['mode']
    stages = state.get('stages', [])
    if isinstance(stages, list):
        result['stages'] = []
        for row in stages[:16]:
            if not isinstance(row, dict): continue
            summary = {key: safe_json(value[:80] if isinstance(value, str) else value, replacements)
                       for key, value in row.items() if key in ('id', 'status', 'attempts', 'durationSeconds')
                       and (value is None or type(value) in (str, int, float, bool))}
            progress = row.get('progress')
            if isinstance(progress, dict):
                summary['progress'] = {key: safe_json(value[:512] if isinstance(value, str) else value, replacements)
                                       for key, value in progress.items() if key in
                                       ('phase', 'done', 'total', 'unit', 'percent', 'detail') and
                                       (value is None or type(value) in (str, int, float, bool))}
            result['stages'].append(summary)
    owners = {key: set() for key in ('inputKeys', 'gameKeys', 'sourceHashes', 'updateKeys')}
    identity_keys = {'inputKey': 'inputKeys', 'gameInputKey': 'inputKeys', 'gameKey': 'gameKeys',
                     'cacheGameKey': 'gameKeys', 'sourceHash': 'sourceHashes', 'updateKey': 'updateKeys'}
    def observe(value, depth=0):
        if depth > 8: return
        if isinstance(value, dict):
            for key, member in list(value.items())[:256]:
                target = identity_keys.get(key)
                if target and isinstance(member, str) and re.fullmatch(r'[0-9a-f]{64}', member):
                    owners[target].add(member)
                elif isinstance(member, (dict, list)): observe(member, depth + 1)
        elif isinstance(value, list):
            for member in value[:256]: observe(member, depth + 1)
    for field in ('completed', 'qualification', 'stages', 'storageProvenance'): observe(state.get(field))
    # Engine.qualify historically emitted the census instead of attaching a
    # state.qualification record. Preserve its observed identities without
    # inferring ownership from arbitrary event text or absolute source paths.
    events = state.get('events')
    if isinstance(events, list):
        for event in events[-128:]:
            if not isinstance(event, dict) or event.get('code') not in ('workspace_space_checked', 'space_estimate_warning'):
                continue
            parameters = event.get('parameters')
            if isinstance(parameters, dict): observe(parameters.get('spaceEstimate'))
    result['observedOwners'] = {key: sorted(values)[:16] for key, values in owners.items() if values}
    return result


def workspace_evidence(root, replacements, omitted):
    """Finite metadata summaries, including retired runs, without payload scans.

    A cleanup-only report must work after the last run is removed. Enumerate only
    known session metadata directories and direct input manifests; do not enter
    game inputs, asset conversion, Unity projects, banks, signing or save data.
    These observations explain retained disk ownership, not deletion permission.
    """
    runs = []; retained = []
    for relative, removed in (('sessions', False), ('logs/removed-runs', True)):
        try:
            folder = ordinary(root / relative)
            if not folder.is_dir(): continue
            count = 0
            for entry in folder.iterdir():
                if not re.fullmatch(r'[0-9a-f]{32}', entry.name): continue
                count += 1
                if count > MAX_WORKSPACE_RUNS:
                    omitted.append({'name': relative, 'reason': 'run summary limit'}); break
                try:
                    entry = ordinary(entry)
                    if not entry.is_dir(): continue
                    state = optional_object(entry / 'state.json', omitted, relative + '/' + entry.name + '/state.json')
                    if not isinstance(state, dict) or state.get('session') != entry.name:
                        omitted.append({'name': relative + '/' + entry.name, 'reason': 'missing or mismatched run metadata'}); continue
                    account_replacements(entry, state, replacements, omitted)
                    # Retain compact facts, not up to 256 full event histories.
                    retained.append((entry, run_summary(state, removed, replacements), removed, installation_state(state)))
                except (BuildError, OSError, ValueError):
                    omitted.append({'name': relative + '/' + entry.name, 'reason': 'unsafe or unavailable run metadata'})
        except (BuildError, OSError, ValueError):
            omitted.append({'name': relative, 'reason': 'unsafe or unavailable run directory'})
    retained.sort(key=lambda row: row[0].name)
    for _, summary, _, _ in retained: runs.append(summary)
    manifests = []
    try:
        folder = ordinary(root / 'build/manifests')
        if folder.is_dir():
            count = 0
            for path in folder.iterdir():
                if not re.fullmatch(r'[0-9a-f]{64}\.json', path.name): continue
                count += 1
                if count > MAX_WORKSPACE_MANIFESTS:
                    omitted.append({'name': 'build/manifests', 'reason': 'manifest summary limit'}); break
                value = optional_object(path, omitted, 'build/manifests/' + path.name, 16 * 1048576)
                if not isinstance(value, dict) or value.get('schema') != 1 or value.get('inputKey') != path.stem:
                    continue
                row = {'inputKey': path.stem, 'contentVerified': False}
                if value.get('target') in ('game', 'startup', 'probe'): row['target'] = value['target']
                if type(value.get('recipe')) is int: row['recipe'] = value['recipe']
                for key in ('game', 'mod'):
                    member = value.get(key)
                    if not isinstance(member, dict): continue
                    identity = member.get('key')
                    if isinstance(identity, str) and re.fullmatch(r'[0-9a-f]{64}', identity): row[key + 'Key'] = identity
                    files = member.get('files')
                    if isinstance(files, list): row[key + 'FileCount'] = len(files)
                manifests.append(row)
    except (BuildError, OSError, ValueError):
        omitted.append({'name': 'build/manifests', 'reason': 'unsafe or unavailable manifest directory'})
    return {'runs': runs, 'inputManifests': manifests, 'contentVerified': False}, retained


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


def installation_stage(state):
    stages = state.get('stages') if isinstance(state, dict) else None
    if not isinstance(stages, list): return {}
    return next((row for row in stages[:16] if isinstance(row, dict) and row.get('id') == 'install'), {})


def installation_state(state):
    """Retain only install status and build identities, never full run histories."""
    stage = installation_stage(state)
    result = {'stages': [{key: value[:80] if isinstance(value, str) else value
                          for key, value in stage.items() if key in
                          ('id', 'status', 'attempts', 'startedAt', 'durationSeconds')
                          and type(value) in (str, int, float, bool)}]}
    completed = state.get('completed')
    build = completed.get('build') if isinstance(completed, dict) else None
    details = build.get('details') if isinstance(build, dict) else None
    if isinstance(details, dict):
        result['completed'] = {'build': {'details': {key: value for key in ('apkSha256', 'inputKey')
                                                    if isinstance(value := details.get(key), str) and len(value) <= 64}}}
    return result


def installation_logs(root, session_root, state):
    """Select only the current install attempt's explicitly named ADB log."""
    try:
        stage = installation_stage(state)
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


def update_route_stats(build_root, state, replacements=()):
    """Explain the selected update without exporting either source inventory.

    Capture114856 really reconverts assets, but lacks the old/new game identities
    needed to separate a compatibility false positive from changed PC inputs.
    Consult bounded, already-qualified manifests only; never rehash a game/APK.
    A newer unrelated inspect pointer cannot claim to describe this update.
    """
    if state.get('choices', {}).get('mode') not in ('update-mod', 'update-profile'): return None
    try:
        pointer = read_object(build_root / 'latest-update-input.json', limit=65536)
        relative = pointer.get('plan') if isinstance(pointer, dict) else None
        if not isinstance(relative, str) or not re.fullmatch(r'updates/[0-9a-f]{64}/plan\.json', relative): return None
        value = read_object(build_root / relative, limit=16 * 1048576)
        inspected = state.get('completed', {}).get('inspect', {}).get('details', {})
        if (not isinstance(value, dict) or value.get('schema') != 1
                or value.get('updateKey') != relative.split('/')[1]
                or value.get('mode') != state['choices']['mode']
                or value.get('gameInputKey') != inspected.get('inputKey')
                or value.get('baseApkSha256') != inspected.get('baseApkSha256')): return None
        result = {key: safe_json(value[key], replacements) for key in
                  ('mode', 'updateKey', 'gameInputKey', 'baseApkSha256', 'updateStrategy',
                   'requiresUnity', 'codeChanged', 'artChanged', 'rebuildReasons') if key in value}
        result['hardwareVerified'] = False
        original = value.get('inputs', {}).get('game', {})
        result['retainedGameKey'] = original.get('key')
        if value.get('updateStrategy') == 'cached-full-build':
            latest = read_object(build_root / 'latest-input.json', limit=65536)
            manifest = latest.get('manifest') if isinstance(latest, dict) else None
            if isinstance(manifest, str) and re.fullmatch(r'manifests/[0-9a-f]{64}\.json', manifest):
                current = read_object(build_root / manifest, limit=16 * 1048576)
                selected = value.get('inputs', {})
                if (isinstance(current, dict) and current.get('inputKey') == Path(manifest).stem
                        and current.get('target') == 'game'
                        and current.get('mod', {}).get('key') == selected.get('mod', {}).get('key')
                        and current.get('profileKey') == selected.get('profileKey')):
                    def records(game):
                        rows = game.get('files')
                        if not isinstance(rows, list) or len(rows) > 100000: raise ValueError('unbounded inventory')
                        result = {}
                        for row in rows:
                            if not isinstance(row, dict) or not isinstance(row.get('path'), str): raise ValueError('invalid inventory')
                            if row['path'] in result: raise ValueError('duplicate inventory path')
                            result[row['path']] = (row.get('size'), row.get('sha256'))
                        return result
                    before, after = records(original), records(current.get('game', {}))
                    added, removed = sorted(after.keys() - before.keys()), sorted(before.keys() - after.keys())
                    changed = sorted(name for name in before.keys() & after.keys() if before[name] != after[name])
                    result['observedGameDelta'] = {'currentGameKey': current.get('game', {}).get('key'),
                        'addedCount': len(added), 'removedCount': len(removed), 'changedCount': len(changed),
                        'paths': safe_json((added + removed + changed)[:12], replacements), 'contentVerified': False}
        return result
    except (BuildError, OSError, ValueError, TypeError, AttributeError): return None


def device_tool_evidence(root, replacements):
    """Retain the current utility task, never enumerate captures or game data."""
    try:
        folder = ordinary(root / 'device')
        value = read_object(folder / 'device-tools-state.json', limit=512 * 1024)
        if not isinstance(value, dict) or value.get('status') not in ('idle', 'running', 'connecting', 'connected', 'capturing', 'complete', 'waiting', 'failed', 'cancelled',
                                                                   'scanning', 'cleaning', 'storage-preview', 'storage-complete',
                                                                   'starting', 'started', 'debug-wait'): return None, []
        for serial in [value.get('serial'), value.get('hardwareSerial'),
                       *(row.get('serial') for row in value.get('devices', [])[:32] if isinstance(row, dict))]:
            if isinstance(serial, str) and 3 <= len(serial) <= 256: replacements.append((serial, '<quest>'))
        summary = {key: safe_json(value[key], replacements) for key in
                   ('status', 'operation', 'updatedAt', 'elapsedSeconds', 'message', 'progress', 'activity') if key in value}
        summary['hardwareVerified'] = False
        # Device cleanup reports counts and known/unknown byte estimates, never
        # arbitrary cache paths or save member names. Inaccessible storage must
        # remain unknown instead of becoming a misleading zero-byte result.
        def count(item):
            return item if type(item) is int and 0 <= item <= 2**63 - 1 else None
        plan = value.get('storagePlan')
        if isinstance(plan, dict) and plan.get('schema') == 1:
            record = {key: count(plan.get(key)) for key in ('bytes', 'files')}
            record['complete'] = plan.get('complete') is True
            for name in ('candidates', 'protected', 'unknown'):
                rows = plan.get(name)
                if not isinstance(rows, list): continue
                record[name + 'Count'] = len(rows)
                record[name] = [{**{key: row[key] for key in ('kind', 'reason')
                                   if isinstance(row.get(key), str) and re.fullmatch(r'[a-z0-9-]{1,80}', row[key])},
                                 'bytes': count(row.get('bytes')), 'files': count(row.get('files'))}
                                for row in rows[:128] if isinstance(row, dict)]
            summary['storagePlan'] = record
        cleaned = value.get('storageResult')
        if isinstance(cleaned, dict):
            summary['storageResult'] = {key: count(cleaned.get(key)) for key in ('freedBytes', 'deletedFiles')}
            for name in ('removed', 'retained'):
                if isinstance(cleaned.get(name), list): summary['storageResult'][name + 'Count'] = len(cleaned[name])
        candidates = []
        log = value.get('log')
        if isinstance(log, str):
            name = log.replace('\\', '/').rsplit('/', 1)[-1]
            if re.fullmatch(r'device-tools-[0-9a-f]{32}\.log', name):
                path = ordinary(folder / name)
                normalize = lambda item: os.path.normcase(str(item).replace('\\', '/'))
                if normalize(path) == normalize(log) and path.is_file():
                    candidates.append(('device/' + name, path, False))
        progress = ordinary(folder / 'device-tools-progress.jsonl')
        if progress.is_file(): candidates.append(('device/device-tools-progress.jsonl', progress, True))
        return summary, candidates
    except (BuildError, OSError, ValueError, TypeError, AttributeError): return None, []


def signing_recovery_stats(root, session_root, state, *, diagnostic=None, require_run_build=False):
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
        if not matches:
            # Capture200458 finishes clean APK installation and then fails its
            # content transfer, outside the recovery exception wrapper. Bind
            # the current transaction's banner only within this attempt's APK
            # header; its journal still must match the run's full APK identity.
            headers = list(re.finditer(r'^Selected local APK: ', tail, re.M))
            if headers:
                attempt = tail[headers[-1].start():]
                matches = re.findall(r'^Retained signing migration backup: ([^\r\n]{1,2048})\r?$', attempt, re.M)
        if not matches: return None
        selected = True
        normalized = matches[-1].replace('\\', '/')
        base = str(root / 'device/signing-recovery').replace('\\', '/')
        # Windows normcase also converts separators. Normalize the directory
        # boundary with the prefix so a current Windows transaction can match.
        if not os.path.normcase(normalized).startswith(os.path.normcase(base + '/')):
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
        except (ValueError, RecursionError):
            return unavailable('journal-invalid')
        if (not isinstance(value, dict) or value.get('schema') != 1
                or value.get('package') != 'dev.gloomhavenvr.quest'
                or value.get('deviceSerial') != parts[0] or value.get('transaction') != parts[1]):
            return unavailable('journal-identity-mismatch')
        reason = 'build-receipt-unreadable'
        # A workspace report has several independent runs. Its latest-build
        # pointer cannot bind an older run's installation to somebody else's
        # APK. Prefer the named run's completed build; retain the historical
        # pointer fallback only for an explicitly selected legacy session.
        completed = state.get('completed')
        build = completed.get('build') if isinstance(completed, dict) else None
        latest = build.get('details') if isinstance(build, dict) else None
        if latest is None:
            if require_run_build: return unavailable('run-build-receipt-missing')
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
            # Metadata only: capture141941's retained archive contained fewer
            # members than the current positive census. Never reread the archive
            # or expose its names just to describe that repair frontier.
            planned = item.get('captureFiles')
            if type(planned) is int and 1 <= planned <= 100001:
                row['plannedMembers'] = planned - 1  # The root '.' is omitted.
            planned_bytes = item.get('captureBytes')
            if type(planned_bytes) is int and 0 <= planned_bytes <= 2**63 - 1:
                row['plannedFileBytes'] = planned_bytes
            if item.get('accessMode') in ('shell', 'app', 'app-direct', 'app-process'):
                row['accessMode'] = item['accessMode']
            if item.get('captureTransport') in ('exec-out-tar', 'host-pax'):
                row['captureTransport'] = item['captureTransport']
            archive = item.get('archive')
            if isinstance(archive, str) and re.fullmatch(r'(?:private|device-protected|external)-[0-9a-f]{32}\.tar', archive):
                reason = 'backup-path-unsafe'
                path = ordinary(folder / archive)
                reason = 'backup-stat-unreadable'
                row['archivePresent'] = path.is_file()
                if path.is_file():
                    row['archiveBytes'] = path.stat().st_size
                    # A remainder proves broken framing; zero alone is not a
                    # validation claim. This stat-only observation is cheap.
                    row['archiveBlockRemainder'] = row['archiveBytes'] % 512
            roots.append(row)
        policy = value.get('dataPolicy', 'all')
        omissions = value.get('policyOmissions', [])
        if (policy not in ('all', 'saves') or not isinstance(omissions, list)
                or len(omissions) > 500000 or (policy == 'all' and omissions)
                or any(not isinstance(row, dict) or type(row.get('size')) is not int
                       or row['size'] < 0 or row.get('reason') != 'unreadable-non-save' for row in omissions)):
            return unavailable('journal-invalid')
        reason = 'backup-path-unsafe'
        original = ordinary(folder / 'original.apk')
        reason = 'backup-stat-unreadable'
        states = ('created', 'backed-up', 'parking', 'parked', 'uninstalling', 'removed', 'installing',
                  'restoring', 'rolling-back', 'rollback-restoring', 'verified', 'complete',
                  'reset-approved', 'reset-removing', 'reset-installing', 'reset-installed', 'reset-complete')
        result = {'state': value.get('state') if value.get('state') in states else 'unknown',
                'apkSha256': value['apkSha256'], 'inputKey': value['inputKey'],
                'dataPolicy': policy, 'omittedNonSaveEntries': len(omissions),
                'omittedNonSaveBytes': sum(row['size'] for row in omissions),
                'originalApkPresent': original.is_file(),
                'originalApkBytes': original.stat().st_size if original.is_file() else 0,
                'smallDataOnly': value.get('smallDataOnly') is True,
                'parkingMode': value.get('parkingMode') if value.get('parkingMode') in ('none', 'atomic', 'copy', 'reinstall') else 'unselected',
                'plannedAppDataBytes': sum(row.get('plannedFileBytes', 0) for row in roots),
                'roots': roots, 'retainedBanks': [name for name in value.get('banks', [])
                                                  if name in ('files/quest-owned-game', 'files/quest-mod-resources')],
                'hardwareVerified': False}
        if value.get('state') in states[-5:]:
            result.update(saveDataReset=value.get('saveDataReset') is True,
                          restorationSkipped=value.get('restorationSkipped') is True,
                          resetConsentBound=value.get('resetConsentTransaction') == value['transaction'])
        repair = value.get('captureRepair')
        counts = ('expectedCount', 'observedCount', 'missingCount', 'unexpectedCount',
                  'typeMismatchCount', 'sizeMismatchCount')
        if (isinstance(repair, dict) and repair.get('root') in ('private', 'device-protected', 'external')
                and repair.get('method') in ('exec-out-tar', 'host-pax')
                and repair.get('reason') in ('verified', 'backup-census-mismatch', 'backup-transfer-unavailable')
                and all(type(repair.get(key)) is int and 0 <= repair[key] <= 100001 for key in counts)
                and all(type(repair.get(key)) is int and 0 <= repair[key] <= 100 for key in ('attempts', 'totalAttempts'))
                and type(repair.get('archiveBytes')) is int and 0 <= repair['archiveBytes'] <= 2**63 - 1
                and type(repair.get('archiveAligned')) is bool):
            result['captureRepair'] = {key: repair[key] for key in
                                      ('root', 'method', 'reason', 'attempts', 'totalAttempts',
                                       *counts, 'archiveBytes', 'archiveAligned')}
        return result
    except (BuildError, OSError, ValueError, TypeError, AttributeError):
        # Optional metadata must not suppress the actual failure logs.
        return unavailable(reason) if selected else None


def workspace_installations(root, records, omitted):
    """Select recent owned per-run logs; a workspace has no selected session.

    Capture181310 contains the blocked ninth installation but loses install.log,
    its exact ADB child and signing frontier in the home-page support export.
    Keep three recent installation runs separately from the general run-log
    selection. An incomplete stage may still provide its owned install log,
    but only the valid current-attempt tail marker can admit an ADB log.
    """
    observed = []
    for folder, summary, removed, state in records:
        logs = []
        name = 'runs/' + folder.name + '/install.log'
        add_log(logs, name, folder / 'logs/install.log', False, omitted)
        if not logs: continue
        stage = installation_stage(state)
        started = stage.get('startedAt')
        recent = started if type(started) in (int, float) and math.isfinite(started) and started > 0 else summary.get('updated', summary.get('created', 0))
        if type(recent) not in (int, float) or not math.isfinite(recent): recent = 0
        observed.append((recent, folder, removed, state, stage, logs))
    observed.sort(key=lambda row: (row[0], row[1].name), reverse=True)
    result = []; candidates = []
    for _, folder, removed, state, stage, logs in observed[:MAX_INSTALLATION_RUNS]:
        started = stage.get('startedAt')
        current = type(started) in (int, float) and math.isfinite(started) and started > 0 and logs[0][0] / 10**9 >= started
        row = {'session': folder.name, 'removed': removed, 'installLog': logs[0][1],
               'currentAttemptLog': current, 'hardwareVerified': False}
        for key in ('status', 'attempts', 'startedAt', 'durationSeconds'):
            value = stage.get(key)
            if type(value) in (str, int, float, bool) and (not isinstance(value, float) or math.isfinite(value)):
                row[key] = value[:80] if isinstance(value, str) else value
        for name, path in installation_logs(root, folder, state):
            row['adbLog'] = name
            add_log(logs, name, path, False, omitted)
        diagnostic = {}
        signing = signing_recovery_stats(root, folder, state, diagnostic=diagnostic, require_run_build=True)
        if signing: row['signingRecovery'] = signing
        elif diagnostic: row['signingRecoveryUnavailable'] = diagnostic
        result.append(row); candidates.extend(logs)
    if len(observed) > MAX_INSTALLATION_RUNS:
        omitted.append({'name': 'installations', 'reason': 'recent installation run limit',
                        'count': len(observed) - MAX_INSTALLATION_RUNS})
    return result, candidates


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


def export_support(state_root, session=None, destination=None):
    """Snapshot owned workspace, live or retired run without altering receipts."""
    root = ordinary(state_root)
    if session is not None and not re.fullmatch('[0-9a-f]{32}', str(session)):
        raise BuildError('Invalid wizard session ID.')
    if read_object(root / 'wizard-owner.json') != {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'}:
        raise BuildError('Diagnostic source is not a wizard-owned workspace.')
    omitted = []; state = {}; session_root = None; scope = 'workspace'
    if session is not None:
        session_root = ordinary(root / 'sessions' / session)
        if session_root.exists(): scope = 'session'
        else:
            session_root = ordinary(root / 'logs/removed-runs' / session); scope = 'removed-session'
        state = read_object(session_root / 'state.json')
        if not isinstance(state, dict) or state.get('session') != session:
            raise BuildError('Missing diagnostic session.')
    replacements = [(str(root), '<workspace>'), (str(Path.home()), '<user-home>')]
    workspace, run_records = workspace_evidence(root, replacements, omitted)
    device_summary, device_logs = device_tool_evidence(root, replacements)
    if session_root is not None: account_replacements(session_root, state, replacements, omitted)
    stages = []
    for stage in state['stages'][:16] if isinstance(state.get('stages'), list) else []:
        if not isinstance(stage, dict): continue
        details = stage.get('details') if isinstance(stage.get('details'), dict) else {}
        stages.append({key: safe_json(stage[key], replacements) for key in ('id', 'status', 'attempts', 'progress', 'waiting', 'durationSeconds') if key in stage} |
                      {'details': {key: safe_json(value, replacements) for key, value in details.items() if key in SAFE_DETAIL_KEYS}})
    events = state['events'][-128:] if isinstance(state.get('events'), list) else []
    meta = {'schema': 1, 'kind': 'GloomhavenVR Quest build support', 'createdUtc': datetime.now(timezone.utc).isoformat(),
            'scope': scope, 'session': session,
            'host': {'system': platform.system(), 'machine': platform.machine(), 'python': platform.python_version(), 'logicalCpuCount': os.cpu_count()},
            'stages': stages, 'needsActions': safe_json(state.get('needsActions', []), replacements),
            'events': [{key: safe_json(event[key], replacements) for key in ('sequence', 'time', 'code', 'stage') if key in event} |
                       {'parameters': {key: safe_json(value, replacements) for key, value in
                                       (event['parameters'].items() if isinstance(event.get('parameters'), dict) else []) if key in SAFE_EVENT_KEYS}}
                       for event in events if isinstance(event, dict)],
            'limits': {'fileBytes': MAX_FILE_BYTES, 'totalBytes': MAX_TOTAL_BYTES, 'files': MAX_FILES},
            'files': [], 'omitted': omitted, 'hardwareVerified': False, 'workspace': workspace}
    if session is not None:
        meta.update({key: safe_json(state[key], replacements) for key in ('status', 'created', 'updated') if key in state})
    else:
        # A deleted or absent run is not an invented current build.
        for key in ('stages', 'needsActions', 'events'): meta.pop(key)
        # A pre-stage Wizard failure can be newer than last-failure.json from
        # an earlier compiler attempt. Expose its actual run action directly;
        # keep the individual workspace summaries as the source of this fact.
        recent = [row for row in workspace.get('runs', []) if not row.get('removed')]
        def recent_time(row):
            value = row.get('updated', row.get('created', 0))
            return value if type(value) in (int, float) and math.isfinite(value) else 0
        recent.sort(key=lambda row: (recent_time(row), row.get('session', '')), reverse=True)
        if recent and recent[0].get('status') in ('blocked', 'failed') and recent[0].get('needsActions'):
            meta['currentRunIssue'] = {key: recent[0][key] for key in
                                      ('session', 'status', 'updated', 'mode', 'needsActions') if key in recent[0]}
    if device_summary: meta['deviceTools'] = device_summary
    repo = Path(__file__).resolve().parents[2]
    protocol = ordinary(repo / 'src/GloomhavenVR/Net/NetProtocol.cs')
    if protocol.is_file() and protocol.stat().st_size <= 1048576:
        match = re.search(r'const (?:int|ushort) ModBuild\s*=\s*(\d+)', protocol.read_text(encoding='utf-8'))
        if match: meta['modBuild'] = int(match[1])
    release = optional_object(repo / 'quest-builder-release.json', omitted, 'builder-release')
    if isinstance(release, dict):
        meta['release'] = {key: release[key] for key in ('sourceCommit', 'modBuild', 'schema') if key in release}
    qualification = optional_object(root / 'qualification.json', omitted, 'qualification.json')
    if qualification: meta['qualification'] = safe_json(qualification, replacements)
    exported = optional_object(root / 'packages/latest-export.json', omitted, 'packages/latest-export.json')
    if isinstance(exported, dict):
        result = exported.get('result', {})
        if isinstance(result, dict):
            meta['portableExport'] = {key: safe_json(result[key], replacements)
                                      for key in ('schema', 'package', 'path', 'size', 'sha256') if key in result}
    build_root = ordinary(root / 'build') if session_root else root / 'build'
    route = update_route_stats(build_root, state, replacements)
    if route: meta['updateRoute'] = route
    signing_diagnostic = {}
    signing = signing_recovery_stats(root, session_root, state, diagnostic=signing_diagnostic) if session_root else None
    if signing: meta['signingRecovery'] = signing
    elif signing_diagnostic: meta['signingRecoveryUnavailable'] = signing_diagnostic
    failure = optional_object(build_root / 'last-failure.json', omitted, 'build/last-failure.json')
    if isinstance(failure, dict):
        meta['buildFailure'] = {key: safe_json(failure[key], replacements) for key in ('stage', 'error', 'message') if key in failure}
    # Workspace utility exports stay independent of all proprietary cache trees.
    recovery_folders = recovery_workspaces(build_root, failure) if session_root else []
    if recovery_folders: meta['recoveryReceipts'] = recovery_receipt_stats(recovery_folders)
    preparation = preparation_receipt_stats(build_root) if session_root else []
    if preparation: meta['preparationReceipts'] = preparation
    compute = compute_contract_stats(build_root, preparation, replacements)
    if compute: meta['computeContracts'] = compute
    # Include only the selected build's small identity fields, never copy complete
    # receipts (which contain profile data, full game inventories and file paths).
    for name in ('latest-input.json', 'latest-build.json'):
        value = optional_object(build_root / name, omitted, 'build/' + name)
        if isinstance(value, dict):
            details = value.get('details', value)
            if isinstance(details, dict):
                meta[name] = {key: safe_json(item, replacements) for key, item in details.items() if key in SAFE_DETAIL_KEYS}
                report = details.get('buildReport')
                if isinstance(report, dict):
                    meta[name]['buildReport'] = {key: safe_json(item, replacements) for key, item in report.items()
                                                 if key in SAFE_DETAIL_KEYS}
    candidates = []
    installation_names = set()
    if session_root:
        for name, path in installation_logs(root, session_root, state):
            add_log(candidates, name, path, False, omitted)
            installation_names.update(('wizard/install.log', name))
    else:
        installations, installation_candidates = workspace_installations(root, run_records, omitted)
        if installations: meta['installations'] = installations
        candidates.extend(installation_candidates)
        installation_names.update(row[1] for row in installation_candidates)
    if session_root:
        add_log(candidates, 'wizard/network-preflight.json', session_root / 'network-preflight.json', True, omitted,
                strict_links=True)
    analysis = optional_object(root / 'logs/storage-analysis.json', omitted, 'wizard/storage-analysis.json', MAX_FILE_BYTES)
    if isinstance(analysis, dict):
        for field in ('runs', 'retiredRuns'):
            for row in analysis.get(field, [])[:512] if isinstance(analysis.get(field), list) else []:
                if isinstance(row, dict) and isinstance(row.get('gameRoot'), str):
                    replacements.append((row['gameRoot'], '<owned-game>'))
        add_log(candidates, 'wizard/storage-analysis.json', root / 'logs/storage-analysis.json', True, omitted)
    for name in ('wizard-requests.log', 'wizard-requests.previous.log', 'storage-cleanup.log', 'storage-cleanup.previous.log',
                 'package-export.jsonl', 'package-export.previous.jsonl', 'metadata-repair.jsonl'):
        add_log(candidates, 'wizard/' + name, root / 'logs' / name, False, omitted)
    log_sources = [(build_root / 'logs', 'build', lambda name: bool(BUILD_LOG.fullmatch(name)) or name in UPDATE_LOGS)]
    if session_root:
        log_sources.insert(0, (session_root / 'logs', 'wizard', lambda name: bool(WIZARD_LOG.fullmatch(name))))
    else:
        # Keep recent qualifier events when retirement left no current session.
        def updated(row):
            value = row[1].get('updated', row[1].get('created', 0))
            return value if type(value) in (int, float) and math.isfinite(value) else 0
        for folder, _, _, _ in sorted(run_records, key=updated, reverse=True)[:3]:
            prefix = 'runs/' + folder.name
            log_sources.append((folder / 'logs', prefix, lambda name: name in ('progress.log', 'build.log', 'inspect.log')))
    for log_root, prefix, accept in log_sources:
        try:
            log_root = ordinary(log_root)
            if not log_root.is_dir(): continue
            # Never recurse into tool caches, games, Unity projects or save folders.
            for path in log_root.iterdir():
                if accept(path.name): add_log(candidates, prefix + '/' + path.name, path, False, omitted,
                                              strict_links=session_root is not None)
                if len(candidates) >= 1024:
                    omitted.append({'name': prefix + '/logs', 'reason': 'candidate log limit'}); break
        except BuildError:
            if session_root is not None: raise
            omitted.append({'name': prefix + '/logs', 'reason': 'unsafe or unavailable log directory'})
        except (OSError, ValueError):
            omitted.append({'name': prefix + '/logs', 'reason': 'unsafe or unavailable log directory'})
    for name in RESOURCE_FILES:
        add_log(candidates, 'resources/' + name, build_root / 'evidence' / name, True, omitted)
    for name, path in recovery_logs(build_root, failure, workspaces=recovery_folders):
        candidates.append((path.stat().st_mtime_ns, name, path, False))
    for name, path in procedural_logs(build_root) if session_root else []:
        candidates.append((path.stat().st_mtime_ns, name, path, name.endswith('.json')))
    for name, path, structured in device_logs:
        candidates.append((path.stat().st_mtime_ns, name, path, structured))
    # The actual failed child comes before historic Unity logs even if those
    # were copied/touched later. Keep resource and current Wizard context next,
    # then the bounded host-delivery command set. The existing byte/file caps
    # and credential redaction still apply to every selected record.
    failed_log = failed_command_log(build_root, failure)
    def priority(row):
        _, name, _, structured = row
        # Capture130723 loses its actual cleanup failure to dozens of older
        # Unity/export logs. Admission failures happen before any build child;
        # preserve these two bounded utility/request records even when the
        # current build event history only reports a disk-capacity stop.
        return (7 if name in installation_names else
                6 if name in ('wizard/storage-cleanup.log', 'wizard/wizard-requests.log', 'wizard/storage-analysis.json') else
                5 if name == failed_log else 4 if structured else
                3 if name in ('wizard/build.log', 'wizard/install.log', 'wizard/progress.log') or name.startswith('device/') else
                2 if name.startswith('build/') and name[6:] in UPDATE_LOGS else 1)
    candidates = list({row[1]: row for row in candidates}.values())
    candidates.sort(key=lambda row: (priority(row), row[0]), reverse=True)
    archive_rows = []; used = 0
    for _, name, path, structured in candidates:
        if len(archive_rows) >= MAX_FILES or used >= MAX_TOTAL_BYTES:
            meta['omitted'].append({'name': name, 'reason': 'archive limit'}); continue
        limit = min(MAX_FILE_BYTES, MAX_TOTAL_BYTES - used)
        try:
            path = ordinary(path); original_size = path.stat().st_size
            with path.open('rb') as stream:
                if original_size <= limit: raw = stream.read(limit)
                else:
                    marker = b'\n[... diagnostic middle omitted ...]\n'
                    first = max(0, limit // 4 - len(marker)); tail = max(0, limit - first - len(marker))
                    raw = stream.read(first); stream.seek(max(first, original_size - tail))
                    raw = (raw + marker + stream.read(tail))[:limit]
        except (BuildError, OSError, ValueError):
            omitted.append({'name': name, 'reason': 'log disappeared or became unavailable'}); continue
        decoded = raw.decode('utf-8', errors='replace')
        text, redactions = redact(decoded, replacements)
        if structured and original_size > limit:
            text = '[Resource record exceeds diagnostic size bounds; inspect the retained local record.]\n'
        elif structured:
            try:
                if name.endswith('.jsonl'): text = '\n'.join(json.dumps(safe_json(json.loads(line), replacements), ensure_ascii=False) for line in decoded.splitlines() if line.strip()) + '\n'
                else: text = json.dumps(safe_json(json.loads(decoded), replacements), ensure_ascii=False, indent=2) + '\n'
            except (ValueError, TypeError, RecursionError):
                text = '[Resource record was incomplete during export; retry after the current stage.]\n'
                omitted.append({'name': name, 'reason': 'incomplete structured log'})
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
    # Run summaries are compacted during enumeration. A later live/retired run
    # can supply the account value mentioned in an earlier progress detail.
    # Apply the complete replacement set before publishing any summary.
    meta['workspace'] = safe_json(meta['workspace'], replacements)
    if 'installations' in meta: meta['installations'] = safe_json(meta['installations'], replacements)
    if len(omitted) > 512:
        meta['omitted'] = omitted[:511] + [{'name': 'additional diagnostics', 'reason': 'omission summary limit',
                                          'count': len(omitted) - 511}]
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
        session = args.session
        if session is None:
            # Historical pointers can remain after interrupted/manual retirement.
            # Explicit API None means workspace; the CLI still prefers a real run.
            latest = optional_object(args.state_root / 'latest-session.json', [], 'latest-session.json', 65536)
            selected = latest.get('session') if isinstance(latest, dict) else None
            if isinstance(selected, str) and re.fullmatch(r'[0-9a-f]{32}', selected):
                value = optional_object(args.state_root / 'sessions' / selected / 'state.json', [], 'run')
                if isinstance(value, dict) and value.get('session') == selected: session = selected
        print(json.dumps(export_support(args.state_root, session, args.output))); return 0
    except (BuildError, OSError, ValueError, KeyError, TypeError) as error:
        print('Build support export: ' + str(error)); return 1

if __name__ == '__main__': raise SystemExit(main())
