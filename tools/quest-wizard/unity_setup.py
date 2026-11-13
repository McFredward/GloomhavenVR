"""Observable Unity prerequisites, repeatable user actions and bounded license probe."""
from __future__ import annotations
import hmac
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
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


def _open(executable, *, installer=False, log=None):
    executable = ordinary(executable)
    if not executable.is_file():
        raise WizardError('unity_window_missing', 'The Unity setup window cannot be opened; its executable is missing.',
                          'Das Unity-Fenster kann nicht geöffnet werden; die Programmdatei fehlt.')
    spec = provision.spec_for('unityHub')
    if installer and digest(executable, spec['algorithm']) != spec['hash']:
        raise WizardError('tool_checksum', 'The Unity Hub installer no longer matches its pinned checksum.')
    # Hub is a shared interactive application, not a build subprocess. ShellExecute
    # brings its window back without claiming or terminating an already open Hub.
    if os.name == 'nt': os.startfile(str(executable)); return
    _linux_desktop()
    log = ordinary(log) if log is not None else executable.parent / 'wizard-hub-window.log' if executable.suffix == '.AppImage' else None
    if log is not None: log.parent.mkdir(parents=True, exist_ok=True)
    # Preserve GUI failure details, including distribution-specific shared libraries
    # and sandbox diagnostics. Never disable Electron's sandbox automatically.
    with log.open('wb') if log else open(os.devnull, 'ab') as output:
        child = subprocess.Popen(_hub_command(executable), stdin=subprocess.DEVNULL,
                                 stdout=output, stderr=output, start_new_session=True,
                                 env=_hub_environment(executable))
    time.sleep(.25)
    if child.poll() not in (None, 0):
        raise WizardError('unity_linux_window',
                          'Unity Hub could not open. Check unity-hub-window.log in the session logs for missing desktop libraries or sandbox setup; then retry.',
                          'Unity Hub konnte nicht geöffnet werden. Fehlende Desktop-Bibliotheken oder Sandbox-Einrichtung in unity-hub-window.log bei den Sitzungsprotokollen prüfen und erneut versuchen.')


def _linux_desktop():
    if not os.environ.get('DISPLAY') and not os.environ.get('WAYLAND_DISPLAY'):
        raise WizardError('unity_linux_desktop',
                          'Unity Hub sign-in requires a graphical Linux desktop. Start the wizard from your desktop session, then retry.',
                          'Die Unity-Hub-Anmeldung benötigt einen grafischen Linux-Desktop. Den Wizard in deiner Desktop-Sitzung starten und erneut versuchen.')
    if hasattr(os, 'geteuid') and os.geteuid() == 0:
        raise WizardError('unity_linux_root', 'Run the Unity wizard as your desktop user, without sudo.',
                          'Den Unity-Wizard als normalen Desktop-Benutzer ohne sudo starten.')


def _hub_command(executable, *arguments):
    command = [str(executable)]
    if os.name != 'nt' and Path(executable).suffix == '.AppImage':
        # Official AppImage fallback avoids requiring FUSE or administrator access.
        command.append('--appimage-extract-and-run')
    if arguments: command.extend(['--', '--headless'] if os.name == 'nt' else ['--headless'])
    return [*command, *arguments]


def _hub_environment(executable):
    env = dict(os.environ)
    if os.name != 'nt' and Path(executable).suffix == '.AppImage':
        # Hub's AppImage runtime extracts itself here, on the capacity-checked drive.
        runtime = ordinary(Path(executable).parent / 'runtime'); runtime.mkdir(exist_ok=True)
        env['TMPDIR'] = str(runtime)
    return env


def _linux_hub(store, session):
    spec = provision.spec_for('unityHub')
    root = ordinary(store.root / ('tools/unity-hub-' + spec['version']))
    root.mkdir(parents=True, exist_ok=True)
    path = provision.download(spec, root / 'UnityHub.AppImage', lambda: store.check_cancel(session),
                              lambda done, total: store.progress(session, 'unity', 'unity-hub-download', done, total, 'bytes'))
    path.chmod(0o755)
    atomic_json(root / 'wizard-tool.json', {'schema': 1, 'key': provision.value_hash(spec),
                                          'complete': True, 'executableSha256': digest(path)})
    return str(path)


def _desktop_argument(value):
    """Encode one Exec argument using both Desktop Entry escaping layers."""
    # https://specifications.freedesktop.org/desktop-entry/1.2/exec-variables.html
    if any(char in value for char in ('\0', '\n', '\r', '\t', '=')):
        raise WizardError('unity_protocol_path', 'The Unity Hub path cannot be represented as a desktop executable.')
    quoted = ''.join('\\' + char if char in ('\\', '"', '`', '$') else char for char in value)
    return '"' + quoted.replace('\\', '\\\\').replace('%', '%%') + '"'


def _publish_desktop_entry(path, content):
    """Publish once atomically; never replace another application's desktop file."""
    path = ordinary(path)
    if path.exists():
        if path.stat().st_size > 16384 or path.read_bytes() != content:
            raise WizardError('unity_protocol_entry', 'The user desktop entry has different content and was retained.')
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary = tempfile.mkstemp(prefix='.ghvrq-unity-', dir=path.parent)
    try:
        with os.fdopen(handle, 'wb') as stream:
            stream.write(content); stream.flush(); os.fsync(stream.fileno())
            os.fchmod(stream.fileno(), 0o644)
        try: os.link(temporary, path, follow_symlinks=False)
        except FileExistsError:
            if ordinary(path).stat().st_size > 16384 or path.read_bytes() != content:
                raise WizardError('unity_protocol_entry', 'The user desktop entry changed and was retained.')
    finally: Path(temporary).unlink(missing_ok=True)


def _ensure_linux_protocol(store, session, hub, supervisor):
    """Supply missing AppImage desktop integration without replacing a handler."""
    if os.name == 'nt': return
    spec = provision.spec_for('unityHub')
    expected = ordinary(store.root / ('tools/unity-hub-' + spec['version'] + '/UnityHub.AppImage'))
    if Path(hub).absolute() != expected: return  # An installed external Hub owns its integration.
    marker = expected.parent / 'wizard-tool.json'
    owner = read_json(marker) if marker.is_file() else {}
    if owner.get('key') != provision.value_hash(spec) or not owner.get('complete'):
        raise WizardError('unity_protocol_owner', 'The local Hub has no matching verified ownership record.')
    mime = shutil.which('xdg-mime')
    if not mime:
        raise WizardError('unity_linux_xdg', 'Unity Hub browser sign-in requires xdg-utils. Install your distribution desktop utilities, then retry.',
                          'Für die Unity-Hub-Anmeldung im Browser wird xdg-utils benötigt. Die Desktop-Werkzeuge deiner Distribution installieren und erneut versuchen.')
    logs = store.session_dir(session) / 'logs'; scheme = 'x-scheme-handler/unityhub'
    def query(name):
        log = logs / name
        supervisor.run([mime, 'query', 'default', scheme], log, timeout=15)
        return log.read_text(encoding='utf-8', errors='replace').strip()
    current = query('unity-protocol-query.log')
    if current:
        store.record(session, 'unity_protocol_preserved', 'unity', desktopEntry=current)
        return
    # This boundary modifies only the current user's XDG registration, after
    # Unity terms acceptance, and only for the exact pinned owned AppImage.
    if digest(expected, spec['algorithm']) != spec['hash']:
        raise WizardError('tool_checksum', 'Unity Hub no longer matches the verified pinned AppImage.')
    directory = os.environ.get('XDG_DATA_HOME') or str(Path.home() / '.local/share')
    if not Path(directory).is_absolute():
        raise WizardError('unity_linux_xdg', 'XDG_DATA_HOME must be an absolute user data directory.')
    identity = provision.value_hash(str(expected))[:16]
    entry = ordinary(Path(directory) / ('applications/gloomhavenvr-quest-unityhub-' + identity + '.desktop'))
    launcher = Path('/usr/bin/env')
    if not launcher.is_file():
        raise WizardError('unity_linux_xdg', 'The Linux desktop requires /usr/bin/env to launch the local Unity Hub.')
    # GLib checks the executable before expanding %% in its name. A fixed env
    # launcher keeps percent-containing user paths as an ordinary argument.
    content = ('[Desktop Entry]\nVersion=1.0\nType=Application\nName=Unity Hub\n'
               'Exec=/usr/bin/env -- ' + _desktop_argument(str(expected)) + ' --appimage-extract-and-run %u\n'
               'Terminal=false\nNoDisplay=true\nMimeType=' + scheme + ';\n'
               'X-GloomhavenVR-Quest-Builder=1\n').encode('utf-8')
    _publish_desktop_entry(entry, content)
    # Recheck after publication: another application may have registered while
    # the pinned archive was checked. Preserve that choice rather than reclaim it.
    if query('unity-protocol-recheck.log'):
        store.record(session, 'unity_protocol_preserved', 'unity', detail='Handler appeared during desktop entry publication')
        return
    supervisor.run([mime, 'default', entry.name, scheme], logs / 'unity-protocol-register.log', timeout=15)
    if query('unity-protocol-confirm.log') != entry.name:
        raise WizardError('unity_protocol_registration',
                          'The Linux desktop did not confirm the Unity Hub sign-in callback. Check unity-protocol logs and retry.',
                          'Der Linux-Desktop hat die Unity-Hub-Anmelderückgabe nicht bestätigt. unity-protocol-Protokolle prüfen und erneut versuchen.')
    store.record(session, 'unity_protocol_registered', 'unity', desktopEntry=entry.name)


def _wait(store, session, code, en, de, window, *, opener=_open, poll=0.25):
    waiting = store.waiting(session, 'unity', code, {'en': en, 'de': de}, action='unity-open')
    request = ordinary(store.session_dir(session) / 'unity-action-request.json')
    announced = False
    while True:
        store.check_cancel(session)
        if not announced:
            try:
                executable, installer = window()
                opener(executable, installer=installer,
                       log=store.session_dir(session) / 'logs/unity-hub-window.log')
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


def _editor(state, workspace=None):
    editors, hubs = discovery.unity_paths(workspace) if workspace is not None else discovery.unity_paths()
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
        selected, hub = _editor(state, store.root if os.name != 'nt' else None)
        if not _android_complete(selected):
            if not state['choices']['acceptUnityTerms']:
                raise WizardError('unity_terms_required', 'Review Unity and Android module terms before installation.',
                                  'Vor der Installation die Bedingungen von Unity und den Android-Modulen bestätigen.')
            if os.name != 'nt': _linux_desktop()
            if not hub:
                if os.name != 'nt':
                    hub = _linux_hub(store, session)
                else:
                    spec = provision.spec_for('unityHub')
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
            _ensure_linux_protocol(store, session, hub, supervisor)
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
                supervisor.run(_hub_command(hub, 'help', '--errors'), help_log, timeout=60, env=_hub_environment(hub))
                supported = help_log.read_text(encoding='utf-8', errors='replace')
                if not re.search(r'\binstall\b', supported) or not re.search(r'\beditors\b', supported):
                    raise WizardError('unity_cli_unavailable', 'This Hub cannot install the required Editor automatically.')
                install = ['install-modules', '--version', VERSION] if selected else ['install', '--version', VERSION, '--changeset', '40eb3a945986']
                if os.name != 'nt' and not selected:
                    location = ordinary(store.root / 'tools/unity-editors'); location.mkdir(parents=True, exist_ok=True)
                    supervisor.run(_hub_command(hub, 'install-path', '-s', str(location)),
                                   log_root / 'unity-install-path.log', timeout=60, env=_hub_environment(hub))
                install_log = log_root / 'unity-install.log'
                supervisor.run(_hub_command(hub, *install, '--module', 'android', '--childModules', '--errors'),
                               install_log, timeout=1800, on_poll=_login_guard(install_log), env=_hub_environment(hub))
                selected, hub = _editor(state, store.root if os.name != 'nt' else None)
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
            if os.name != 'nt': _linux_desktop()
            def license_window():
                _, current = _editor(state, store.root if os.name != 'nt' else None)
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
