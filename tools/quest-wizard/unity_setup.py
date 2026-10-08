"""Observable Unity prerequisites, repeatable user actions and bounded license probe."""
from __future__ import annotations
import hmac
import os
from pathlib import Path
import re
import time

import discovery
import provision
from state import WizardError, atomic_json, digest, ordinary, read_json

VERSION = '2021.3.5f1'
PROBE_MARKER = 'GHVRQ_UNITY_PREREQUISITES_OK'
ACTIONS = ('unity-open', 'unity-check')


def request_action(store, session, action, nonce):
    """The browser can repeat only a current declared action, never select an executable."""
    state = store.load(session)
    row = next((row for row in state['stages'] if row['id'] == 'unity'), {})
    waiting = row.get('waiting') or {}
    if (state['status'] != 'running' or row.get('status') != 'running' or action not in ACTIONS
            or not isinstance(nonce, str) or not hmac.compare_digest(nonce, waiting.get('nonce', ''))):
        raise WizardError('stale_action', 'This Unity action is no longer pending. Refresh its status.',
                          'Diese Unity-Aktion steht nicht mehr aus. Den aktuellen Status erneut laden.')
    store.check_cancel(session)
    path = ordinary(store.session_dir(session) / 'unity-action-request.json')
    if path.exists():
        raise WizardError('action_pending', 'The requested action is already being processed.',
                          'Die angeforderte Aktion wird bereits bearbeitet.')
    atomic_json(path, {'schema': 1, 'nonce': nonce, 'action': action})
    return {'schema': 1, 'event': 'action_requested', 'session': session, 'action': action}


def _open(executable, *, installer=False):
    executable = ordinary(executable)
    if not executable.is_file():
        raise WizardError('unity_window_missing', 'The Unity setup window cannot be opened; its executable is missing.',
                          'Das Unity-Fenster kann nicht geöffnet werden; die Programmdatei fehlt.')
    if installer and digest(executable, provision.LOCK['unityHub']['algorithm']) != provision.LOCK['unityHub']['hash']:
        raise WizardError('tool_checksum', 'The Unity Hub installer no longer matches its pinned checksum.')
    if os.name != 'nt':
        raise WizardError('windows_required', 'Interactive Unity setup currently supports Windows.')
    # Hub is a shared interactive application, not a build subprocess. ShellExecute
    # brings its window back without claiming or terminating an already open Hub.
    os.startfile(str(executable))


def _wait(store, session, code, en, de, window, *, opener=_open, poll=0.25):
    waiting = store.waiting(session, 'unity', code, {'en': en, 'de': de}, action='unity-open')
    request = ordinary(store.session_dir(session) / 'unity-action-request.json')
    announced = False
    while True:
        store.check_cancel(session)
        if not announced:
            try:
                executable, installer = window()
                opener(executable, installer=installer)
            except (WizardError, OSError) as error:
                # Keep the explicit instruction and repeat/check buttons available.
                # Closing a setup window never publishes a prerequisite receipt.
                store.progress(session, 'unity', 'unity-window-open', None, None, None,
                               'Window could not be opened: ' + str(error))
                store.record(session, 'unity_window_failed', 'unity', error=type(error).__name__, message=getattr(error, 'message', str(error)))
            announced = True
        if request.is_file():
            value = read_json(request, limit=4096); request.unlink()
            if (value.get('nonce') == waiting['nonce'] and value.get('action') in ACTIONS):
                if value['action'] == 'unity-check':
                    store.clear_waiting(session, 'unity'); return
                announced = False
        time.sleep(poll)


def _editor(state):
    editors, hubs = discovery.unity_paths()
    selected = state['choices'].get('unityEditor')
    if not selected:
        selected = next((row['path'] for row in editors if row['version'] == VERSION and row['androidSupport']), None)
    if not selected:
        selected = next((row['path'] for row in editors if row['version'] == VERSION), None)
    hub = state['choices'].get('unityHub') or next(iter(hubs), None)
    return selected, hub


def _android_complete(editor):
    if not editor: return False
    root = Path(editor).parent / 'Data/PlaybackEngines/AndroidPlayer'
    suffix = '.exe' if os.name == 'nt' else ''
    return all((root / name).is_file() for name in
               ('NDK/source.properties', 'SDK/platform-tools/adb' + suffix, 'OpenJDK/bin/java' + suffix))


def _probe(store, session, editor, supervisor):
    """Execute an empty Editor method; no game import, credentials or APK compilation."""
    project = ordinary(store.session_dir(session) / 'unity-probe')
    project.mkdir(exist_ok=True)
    owner = project / 'wizard-probe.json'
    expected = {'schema': 1, 'owner': 'GloomhavenVR Unity prerequisite probe', 'session': session}
    if owner.exists() and read_json(owner) != expected: raise WizardError('unowned_probe', 'Unity probe ownership changed.')
    if not owner.exists() and any(project.iterdir()): raise WizardError('unowned_probe', 'Unity probe directory is not owned.')
    atomic_json(owner, expected)
    for name in ('Assets/Editor', 'Packages', 'ProjectSettings'):
        ordinary(project / name).mkdir(parents=True, exist_ok=True)
    ordinary(project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n', encoding='utf-8')
    ordinary(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + VERSION + '\n', encoding='utf-8')
    method = ('using UnityEditor; using UnityEngine;\npublic static class QuestPrerequisiteProbe {\n'
              'public static void Check() { Debug.Log("' + PROBE_MARKER + '"); EditorApplication.Exit(0); }\n}\n')
    ordinary(project / 'Assets/Editor/QuestPrerequisiteProbe.cs').write_text(method, encoding='utf-8')
    log = store.session_dir(session) / 'logs/unity-license-probe.log'
    # A fresh success witness is required; a prior log cannot authorize this run.
    ordinary(log).unlink(missing_ok=True)
    supervisor.run([editor, '-batchmode', '-nographics', '-quit', '-projectPath', project,
                    '-executeMethod', 'QuestPrerequisiteProbe.Check', '-logFile', log],
                   store.session_dir(session) / 'logs/unity-license-process.log', timeout=180)
    if (not log.is_file() or log.stat().st_size > 8 * 1048576 or not re.search(
            r'^' + re.escape(PROBE_MARKER) + r'\s*$', log.read_text(encoding='utf-8', errors='replace'), re.MULTILINE)):
        raise WizardError('unity_license_unconfirmed', 'The Editor prerequisite probe did not complete. Check Unity Hub sign-in and licence activation.',
                          'Die Editor-Prüfung wurde nicht abgeschlossen. Anmeldung und Lizenzaktivierung in Unity Hub prüfen.')
    return log


def _login_guard(log):
    checked = [0.0]
    def poll(_process):
        if time.monotonic() - checked[0] < 1: return
        checked[0] = time.monotonic()
        if log.is_file():
            with log.open('rb') as stream:
                stream.seek(max(0, log.stat().st_size - 8192))
                text = stream.read(8192).decode('utf-8', errors='replace')
            if re.search(r'(?i)not (?:signed|logged) in|authentication required|login required|no valid unity licen[cs]e', text):
                raise WizardError('unity_login_required', 'Unity Hub requires sign-in or licence activation.')
    return poll


def prepare(store, state, supervisor):
    session = state['session']; log_root = store.session_dir(session) / 'logs'
    log_root.mkdir(exist_ok=True); setup = None; login_confirmed = False
    store.operation(session, 'unity', 'hub', detail='Checking Unity Hub or an already complete local Editor installation')
    store.progress(session, 'unity', 'unity-prerequisites', 0, 4, 'checks', 'Editor, Android tools, version and licence')
    while True:
        store.check_cancel(session)
        selected, hub = _editor(state)
        if not _android_complete(selected):
            if not state['choices']['acceptUnityTerms']:
                raise WizardError('unity_terms_required', 'Review Unity and Android module terms before installation.',
                                  'Vor der Installation die Bedingungen von Unity und den Android-Modulen bestätigen.')
            if not hub:
                spec = provision.LOCK['unityHub']
                setup = provision.download(spec, store.root / 'tools/downloads/UnityHubSetup-3.22.2-x64.exe',
                    lambda: store.check_cancel(session),
                    lambda done, total: store.progress(session, 'unity', 'unity-hub-download', done, total, 'bytes'))
                def window():
                    _, current = _editor(state)
                    return (current, False) if current else (setup, True)
                _wait(store, session, 'unity_hub_setup',
                      'Complete Unity Hub setup. If its window was closed, open it again. Then check prerequisites.',
                      'Unity Hub installieren. Falls das Fenster geschlossen wurde, erneut öffnen. Danach Voraussetzungen prüfen.', window)
                continue
            store.operation(session, 'unity', 'hub', complete=True, detail='Unity Hub installation found; licence remains independently checked')
            if not login_confirmed:
                _wait(store, session, 'unity_login_required',
                      'Sign in to Unity Hub and activate an eligible licence. Then select Check prerequisites. Closing Hub leaves this action pending.',
                      'In Unity Hub anmelden und eine passende Lizenz aktivieren. Danach „Voraussetzungen prüfen“ wählen. Beim Schließen des Hubs bleibt dieser Schritt offen.',
                      lambda: (hub, False))
                login_confirmed = True
            store.operation(session, 'unity', 'editor', detail='Installing Unity Editor and its Android SDK, NDK and OpenJDK modules')
            store.progress(session, 'unity', 'unity-editor-install', None, None, None, 'Unity Hub installs the Editor and Android modules')
            try:
                help_log = log_root / 'unity-hub-help.log'
                supervisor.run([hub, '--', '--headless', 'help', '--errors'], help_log, timeout=60)
                supported = help_log.read_text(encoding='utf-8', errors='replace')
                if not re.search(r'\binstall\b', supported) or not re.search(r'\beditors\b', supported):
                    raise WizardError('unity_cli_unavailable', 'This Hub cannot install the required Editor automatically.')
                install = ['install-modules', '--version', VERSION] if selected else ['install', '--version', VERSION, '--changeset', '40eb3a945986']
                install_log = log_root / 'unity-install.log'
                supervisor.run([hub, '--', '--headless', *install, '--module', 'android', '--childModules', '--errors'],
                               install_log, timeout=1800, on_poll=_login_guard(install_log))
                selected, hub = _editor(state)
                if not _android_complete(selected): raise WizardError('unity_install_incomplete', 'Editor or Android modules remain missing.')
            except WizardError as error:
                if error.code not in ('child_failed', 'child_timeout', 'unity_cli_unavailable', 'unity_install_incomplete', 'unity_login_required'): raise
                _wait(store, session, 'unity_install_incomplete',
                      'Unity setup is incomplete. In Hub install Unity 2021.3.5f1 with Android Build Support, SDK/NDK and OpenJDK, or check sign-in. The local Unity log contains the tool result. Then check prerequisites.',
                      'Unity ist noch nicht vollständig eingerichtet. In Hub Unity 2021.3.5f1 mit Android Build Support, SDK/NDK und OpenJDK installieren oder Anmeldung prüfen. Das lokale Unity-Log enthält das Werkzeugergebnis. Danach Voraussetzungen prüfen.',
                      lambda: (hub, False))
                login_confirmed = False; continue
        # This boundary is reached only after _android_complete checks actual
        # module files. Closing Hub or acknowledging sign-in cannot advance it.
        store.operation(session, 'unity', 'editor', complete=True, detail='Editor and actual Android SDK, NDK and OpenJDK files found')
        store.operation(session, 'unity', 'prerequisites', detail='Confirming exact Editor version and a fresh successful licence probe')
        store.progress(session, 'unity', 'unity-prerequisites', 2, 4, 'checks', 'Editor and Android tools found')
        version_log = log_root / 'unity-version.log'
        supervisor.run([selected, '-version'], version_log, timeout=30)
        if VERSION not in version_log.read_text(encoding='utf-8', errors='replace'):
            raise WizardError('unity_version', 'Select Unity 2021.3.5f1 with Android support.',
                              'Bitte Unity 2021.3.5f1 mit Android-Unterstützung wählen.')
        store.progress(session, 'unity', 'unity-prerequisites', 3, 4, 'checks', 'Editor version confirmed; checking licence')
        try:
            probe = _probe(store, session, selected, supervisor)
        except WizardError as error:
            if error.code not in ('child_failed', 'child_timeout', 'unity_license_unconfirmed'): raise
            def license_window():
                _, current = _editor(state)
                if current: return current, False
                if setup: return setup, True
                raise WizardError('unity_hub_required', 'Install Unity Hub to activate an eligible licence.')
            _wait(store, session, 'unity_license_unconfirmed',
                  'Unity prerequisites were not confirmed. Open Hub, check sign-in and activate an eligible licence, then check prerequisites again. See the Unity probe log for details.',
                  'Die Unity-Voraussetzungen konnten nicht bestätigt werden. Hub öffnen, Anmeldung und passende Lizenzaktivierung prüfen, dann Voraussetzungen erneut prüfen. Details stehen im Unity-Prüfprotokoll.',
                  license_window)
            continue
        store.clear_waiting(session, 'unity')
        store.progress(session, 'unity', 'unity-prerequisites', 4, 4, 'checks', 'All prerequisites confirmed')
        receipt = store.session_dir(session) / 'unity.json'
        atomic_json(receipt, {'schema': 1, 'unityEditor': selected, 'version': VERSION,
                              'prerequisitePolicy': 2, 'licenseVerified': True})
        return [receipt, version_log, probe], {'unityEditor': selected, 'version': VERSION, 'licenseVerified': True, 'prerequisitePolicy': 2}
