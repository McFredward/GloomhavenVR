"""Small pre-run download checks; cache qualification stays with each producer.

Presence and small metadata reads decide which known downloads to probe. This
does not certify an offline tool closure, hash game assets or change receipts.
"""
from __future__ import annotations
import ast
from concurrent.futures import ThreadPoolExecutor, wait, FIRST_COMPLETED
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import ssl
import sys
import sysconfig
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

import provision
from state import WizardError, read_json, value_hash


def present(path, size=None):
    path = Path(path)
    try: return path.is_file() and not path.is_symlink() and path.stat().st_size > 0 and (size is None or path.stat().st_size == size)
    except OSError: return False


def metadata(path):
    try: return read_json(Path(path), limit=1048576)
    except (OSError, ValueError, WizardError): return {}


def constants(path):
    """Read literal recipe declarations without importing converter packages."""
    values = {}
    def literal(node):
        if isinstance(node, ast.Name): return values[node.id]
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add): return literal(node.left) + literal(node.right)
        if isinstance(node, ast.JoinedStr):
            return ''.join(str(literal(part.value)) if isinstance(part, ast.FormattedValue) else literal(part) for part in node.values)
        return ast.literal_eval(node)
    for node in ast.parse(Path(path).read_text(encoding='utf-8')).body:
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name):
            try: values[node.targets[0].id] = literal(node.value)
            except (KeyError, ValueError, TypeError): pass
    return values


def requirements_key(source):
    seen = {}
    def visit(path):
        path = path.resolve()
        if path in seen: return
        seen[path] = hashlib.sha256(path.read_bytes()).hexdigest()
        for line in path.read_text().splitlines():
            if line.startswith('-r '): visit(path.parent / line[3:].strip())
    for name in ('tools/quest-builder/requirements.txt', 'tools/quest-builder/requirements-bootstrap.txt',
                 'tools/quest-builder/requirements-source.txt', 'tools/quest-procedural-runtime/requirements.txt'):
        visit(source / name)
    return hashlib.sha256('\n'.join(sorted(path.name + ':' + sha for path, sha in seen.items())).encode()).hexdigest()


def inventory(workspace, source, state, *, pending, editor=None, hub=None, host=None):
    """Find known remote inputs for this run; no payload scans or child tools."""
    workspace, source = Path(workspace), Path(source)
    host = host or provision.host_key(); linux = host == 'linux-x64'
    selected = state['choices']; session = workspace / 'sessions' / state['session']
    cache = workspace / 'build/tool-cache'; rows = []
    def need(name, label, urls, local=False, conditional=False):
        rows.append({'id': name, 'label': label, 'urls': list(urls), 'local': bool(local), 'conditional': conditional})
    def tool(name):
        spec = provision.LOCK['linux'][name] if linux else provision.LOCK[name]
        suffix = '.tar.gz' if spec.get('archive') == 'tar.gz' else '.zip'
        archive = workspace / 'tools/downloads' / (name + '-' + spec['version'] + ('-linux-x64' if linux else '') + suffix)
        need(name, name, [spec['url']], present(archive, spec.get('size')))
    mode = selected.get('mode', 'build')
    if 'install' in pending and selected.get('install', True):
        adb = constants(source / 'tools/quest-installer/adb_bootstrap.py')
        spec = adb['LINUX'] if linux else {'url': adb['URL'], 'bytes': adb['BYTES']}
        candidates = [shutil.which('adb'), source / ('scripts/.quest-adb/platform-tools/adb' + ('' if linux else '.exe'))]
        for name in ('ANDROID_HOME', 'ANDROID_SDK_ROOT', 'SDK_ROOT'):
            if os.environ.get(name): candidates.append(Path(os.environ[name]) / ('platform-tools/adb' + ('' if linux else '.exe')))
        android = Path.home() / 'Android/Sdk' if linux else Path(os.environ.get('LOCALAPPDATA', Path.home() / 'AppData/Local')) / 'Android/Sdk'
        candidates.append(android / ('platform-tools/adb' + ('' if linux else '.exe')))
        if linux:
            candidates.append(Path.home() / '.android/sdk/platform-tools/adb')
        else:
            local = Path(os.environ.get('LOCALAPPDATA', Path.home() / 'AppData/Local'))
            roaming = Path(os.environ.get('APPDATA', Path.home() / 'AppData/Roaming'))
            candidates += [roaming / 'SideQuest/platform-tools/adb.exe',
                           roaming / 'SideQuest/platform-tools/adb',
                           local / 'SideQuest/platform-tools/adb.exe',
                           local / 'Programs/SideQuest/resources/app.asar.unpacked/build/platform-tools/adb.exe']
        local = any(path and Path(path).is_file() for path in candidates)
        local = local or present(source / 'scripts/.quest-adb' / Path(spec['url']).name, spec['bytes'])
        need('adb', 'Android platform-tools for installation', [spec['url']], local)
    if 'tools' in pending:
        for name in (('apkJdk', 'apkBuildTools') if mode == 'update-profile' else ('git', 'dotnet8', 'dotnet10')):
            if name != 'git' or not linux: tool(name)
    if 'profile' in pending:
        need('steam-logo', 'Steam logo', [provision.LOCK['steamLogo']['url']],
             present(selected['steamLogo']) if selected.get('steamLogo') else present(session / 'steam-logo.png'))
    if mode == 'update-profile': return rows
    if 'source' in pending:
        local_source = (source / '.git').exists() or (source / 'quest-builder-release.json').is_file()
        need('mod-source', 'Selected mod source', [provision.LOCK['source']['url']], local_source)
        recipe = (source / 'scripts/build-runtimedeps.sh').read_text(encoding='utf-8')
        for package, version in re.findall(r'"(com\.unity\.xr\.[a-z-]+) ([0-9]+\.[0-9]+\.[0-9]+)"', recipe):
            assembly = {'management': 'Management', 'core-utils': 'CoreUtils', 'openxr': 'OpenXR'}[package.removeprefix('com.unity.xr.')]
            local = present(source / ('libs/RuntimeDeps/Unity.XR.' + assembly + '.dll'))
            package_path = source / 'tools/RuntimeDepsBuild/sources' / package / 'package.json'
            local = local or metadata(package_path).get('version') == version
            need('xr-' + package, package + ' ' + version, ['https://github.com/needle-mirror/' + package + '.git/info/refs?service=git-upload-pack'], local)
    if 'unity' in pending:
        import unity_setup
        if not unity_setup._android_complete(editor):
            spec = provision.LOCK['linux']['unityHub'] if linux else provision.LOCK['unityHub']
            if not (hub and Path(hub).is_file()):
                need('unity-hub', 'Unity Hub installation', [spec['url']])
            unity = provision.LOCK['unity']
            installer = ('LinuxEditorInstaller/Unity-' + unity['version'] + '.tar.xz' if linux else
                         'Windows64EditorInstaller/UnitySetup64-' + unity['version'] + '.exe')
            need('unity-install', 'Unity Editor / Android installation',
                 ['https://download.unity3d.com/download_unity/' + unity['changeset'] + '/' + installer])
    if 'build' not in pending: return rows
    # Existing shared restore caches may satisfy all packages offline. Their mere
    # presence cannot certify a full closure, so failed probes in that case are
    # logged as conditional rather than blocking a potentially local build.
    config = source / 'nuget.config'
    nuget_cached = Path(os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget/packages'))).is_dir()
    if config.is_file():
        for feed in ET.parse(config).findall('./packageSources/add'):
            need('nuget-' + feed.get('key'), 'NuGet: ' + feed.get('key'), [feed.get('value')], conditional=nuget_cached)
    global_packages = Path(os.environ.get('LOCALAPPDATA', Path.home() / 'AppData/Local')) / 'Unity/cache' if not linux else Path.home() / '.config/unity3d/cache'
    package_cached = global_packages.is_dir() or any((workspace / 'build/projects').glob('*/Library/PackageCache'))
    need('unity-packages', 'Unity Package Manager', ['https://packages.unity.com/com.unity.xr.openxr'], conditional=package_cached)
    if mode == 'update-mod': return rows
    abi = {'implementation': sys.implementation.name, 'cacheTag': sys.implementation.cache_tag,
           'version': list(sys.version_info[:3]), 'platform': sysconfig.get_platform(),
           'soabi': sysconfig.get_config_var('SOABI'),
           'baseExecutable': str(Path(getattr(sys, '_base_executable', sys.executable)).resolve())}
    python_root = cache / ('build-python-' + value_hash(abi)[:16])
    receipt = metadata(python_root / '.ghvr-build-python.json')
    python_local = (receipt.get('owner') == 'GloomhavenVR builder' and receipt.get('pythonAbi') == abi
                    and receipt.get('requirementsKey') == requirements_key(source)
                    and (python_root / ('bin/python' if linux else 'Scripts/python.exe')).is_file())
    need('python-packages', 'Pinned Python conversion packages', ['https://pypi.org/simple/unitypy/'], python_local)
    tmp = constants(source / 'tools/quest-recovery/tmp_shaders.py')
    need('tmp', 'TextMeshPro sources', [tmp['TMP_URL']], present(cache / 'official-tmp/com.unity.textmeshpro-3.0.6.tgz'))
    effects = constants(source / 'tools/quest-builder/shaders.py')
    effect_cache = cache / 'legacy-post-effects' / effects['CACHE_NAME']
    need('post-effects', 'Legacy image-effect sources', [effects['SOURCE_URL']],
         present(effect_cache / 'StandardAssets.pkg', effects['SOURCE_BYTES']) or
         all(present(effect_cache / (name + '.shader')) for name in effects['SHADERS']))
    identity = constants(source / 'tools/quest-recovery/export_identity.py')
    generation = hashlib.sha256((source / 'tools/quest-recovery/QuestExportIdentity.cs').read_bytes()).hexdigest()[:16]
    recovery_cache = cache / 'full-recovery'
    need('asset-exporter', 'Asset exporter sources', [identity['SOURCE_URL']],
         present(recovery_cache / 'assetripper-source-1ac666f.tar.gz') or
         present(recovery_cache / ('identity-' + generation) / 'assetripper-source-1ac666f.tar.gz'))
    headers = constants(source / 'scripts/build-quest-native.py')
    for name in headers['HEADERS']:
        need('openxr-' + name, 'OpenXR header: ' + name,
             ['https://raw.githubusercontent.com/KhronosGroup/OpenXR-SDK/' + headers['TAG'] + '/include/openxr/' + name],
             present(cache / 'openxr-headers/openxr' / name))
    native = cache / 'campaign-native'
    proton = json.loads((source / 'tools/quest-procedural-runtime/proton.lock.json').read_text())
    ndk = Path(editor).parent / 'Data/PlaybackEngines/AndroidPlayer/NDK/source.properties' if editor else None
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    proton_local = False
    if ndk and present(ndk):
        names = ('proton_runtime.py', 'proton_layout.py', 'proton_launcher.c', 'proton.lock.json', 'bridge.c', 'worker.c', 'protocol.h')
        inputs = {name: sha(source / 'tools/quest-procedural-runtime' / name) for name in names}
        key = value_hash({'backend': 'proton-arm64ec-fex', 'sources': inputs, 'ndkSha256': sha(ndk)})
        proton_local = present(native / 'procedural-runtime-proton' / key / 'native-build.json')
    for name, spec in [('proton-11.0-2-arm64ec.wcp', proton['wine']), ('fex-emu-wine_2609.1-1~n_arm64.deb', proton['fex']), *proton['licenses'].items()]:
        need('proton-' + name, name, [spec['url']], proton_local or present(native / 'procedural-runtime-proton/downloads' / name, spec.get('size')))
    voice = constants(source / 'tools/quest-network/native.py'); voice_local = False
    if ndk and present(ndk):
        key = hashlib.sha256((voice['OPUS_SHA256'] + sha(source / 'tools/quest-network/opus_bridge.c') +
                              sha(source / 'tools/quest-network/native.py') + sha(ndk) + 'False').encode()).hexdigest()
        root = native / 'voice' / key
        voice_local = (present(root / ('verified-source/opus-' + voice['OPUS_VERSION'] + '.tar.gz'), voice['OPUS_ARCHIVE_BYTES']) or
                       present(root / 'native-voice.json') and present(root / 'libopus_egpv.so'))
    need('opus', 'Opus voice codec sources', [voice['OPUS_URL'], voice['OPUS_FALLBACK_URL']], voice_local)
    return rows


def location(url):
    parsed = urllib.parse.urlsplit(url)
    return urllib.parse.urlunsplit((parsed.scheme, parsed.netloc.rsplit('@', 1)[-1], parsed.path, '', ''))[:512]


class HttpsRedirect(urllib.request.HTTPRedirectHandler):
    max_redirections = 3
    def redirect_request(self, request, response, code, message, headers, url):
        if urllib.parse.urlsplit(url).scheme.lower() != 'https':
            raise ValueError('Download server redirected outside HTTPS.')
        return super().redirect_request(request, response, code, message, headers, url)


def probe(row, *, opener=None):
    """Read at most one response byte; verify the real endpoint/redirect TLS."""
    opener = opener or urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl.create_default_context()), HttpsRedirect()).open
    attempts = []
    public = {**row, 'urls': [location(url) for url in row['urls']]}
    for url in row['urls']:
        try:
            if urllib.parse.urlsplit(url).scheme.lower() != 'https': raise ValueError('Dependency URL is not HTTPS.')
            request = urllib.request.Request(url, headers={'Range': 'bytes=0-0', 'Accept-Encoding': 'identity', 'User-Agent': 'GloomhavenVR-Quest-Wizard'})
            with opener(request, timeout=5) as response:
                if urllib.parse.urlsplit(response.geturl()).scheme.lower() != 'https': raise ValueError('Response is not HTTPS.')
                if response.status not in (200, 206): raise ValueError('HTTP ' + str(response.status))
                if not response.read(1): raise ValueError('Download server returned no data.')
                attempts.append({'url': location(url), 'responseUrl': location(response.geturl()), 'status': 'reachable'})
                return {**public, 'status': 'reachable', 'attempts': attempts}
        except (OSError, ValueError, urllib.error.URLError, http.client.HTTPException) as error:
            reason = getattr(error, 'reason', error)
            if isinstance(error, urllib.error.HTTPError): detail = 'HTTP ' + str(error.code) + ': ' + str(reason)
            elif isinstance(reason, ssl.SSLCertVerificationError): detail = 'TLS certificate verification failed: ' + getattr(reason, 'verify_message', str(reason))
            else: detail = str(reason)
            detail = re.sub(r'https?://[^\s<>]+', lambda m: location(m[0]), detail)
            attempts.append({'url': location(url), 'status': 'unreachable', 'reason': detail[:512]})
    return {**public, 'status': 'unreachable', 'attempts': attempts}


def check(rows, *, check_cancel=lambda: None, report=lambda row: None, probe_fn=probe):
    results = []
    remote = [row for row in rows if not row['local']]
    for row in rows:
        if row['local']:
            result = {**row, 'urls': [location(url) for url in row['urls']], 'status': 'local-presence'}; results.append(result); report(result)
    with ThreadPoolExecutor(max_workers=4) as pool:
        futures = {pool.submit(probe_fn, row): row for row in remote}
        while futures:
            check_cancel()
            completed, _ = wait(futures, timeout=.1, return_when=FIRST_COMPLETED)
            for future in completed:
                result = future.result(); futures.pop(future); results.append(result); report(result)
    failed = [row for row in results if row['status'] == 'unreachable' and not row.get('conditional')]
    if failed:
        labels = ', '.join(row['label'] for row in failed[:5])
        absent = any(re.search(r'HTTP (404|410)\b', attempt.get('reason', '')) for row in failed for attempt in row.get('attempts', []))
        if absent:
            raise WizardError('network_dependency_unavailable',
                              'A required build dependency is unavailable at its download link: ' + labels + '. Save diagnostics and resume with a corrected builder or the matching local dependency. Completed build data is retained.',
                              'Eine benötigte Build-Abhängigkeit ist über ihren Downloadlink nicht verfügbar: ' + labels + '. Diagnosepaket speichern und mit einem korrigierten Builder oder der passenden lokalen Abhängigkeit fortsetzen. Fertige Build-Daten bleiben erhalten.',
                              failedDependencies=failed, completedWorkRetained=True)
        raise WizardError('network_preflight_failed',
                          'Required download servers are unavailable: ' + labels + '. Connect to the internet or check the HTTPS certificate/proxy settings, then resume. Completed build data is retained.',
                          'Benötigte Downloadserver sind nicht erreichbar: ' + labels + '. Internetverbindung bzw. HTTPS-Zertifikate/Proxy prüfen und anschließend fortsetzen. Fertige Build-Daten bleiben erhalten.',
                          failedDependencies=failed, completedWorkRetained=True)
    return {'schema': 1, 'checkedDependencies': len(remote), 'localDependencies': len(rows) - len(remote),
            'offlineClosureVerified': False, 'results': results}
