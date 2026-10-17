#!/usr/bin/env python3
"""Execute real release layouts with substitute binary assets and reviewed calibration media."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def main():
    source = Path(sys.argv[1]).resolve()
    sys.path.insert(0, str(source / 'scripts'))
    from town_run_retention import cleanup_completed_run
    output = source / '.planning/debug/native-video-packaging'
    output.mkdir(parents=True, exist_ok=True)
    evidence = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    completion = evidence / 'completion.log'
    completion.write_text('evidence: ' + str(evidence) + '\n')
    print('Evidence: ' + str(evidence), flush=True)
    status = 1
    count = 0

    def check(condition, label):
        nonlocal count
        count += 1
        if not condition:
            raise AssertionError(label)

    def execute(command, log, environment):
        with log.open('w') as stream:
            process = subprocess.Popen(command, env=environment, stdout=stream,
                                       stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = process.wait(timeout=120)
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
        return code, log.read_text()

    try:
        root = evidence / 'fixture'
        root.mkdir()

        def write(name, data):
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            return path

        for name in ('package-release.sh', 'check-package-text.py', 'check-frame-launchers.py',
                     'check-figure-mesh-bank.py', 'frame-archive.ps1'):
            write('scripts/' + name, (source / 'scripts' / name).read_bytes())
        payload = b'approved mod-local intro\0\xff\x11'
        write('src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.mov', payload)
        media = {'CP_Intro/GH_CP_Intro.mov': payload}
        provenance = json.loads((source / 'src/GloomhavenVR/Assets/Movies/Calibration/provenance.json').read_text())
        project = ET.parse(source / 'src/GloomhavenVR/GloomhavenVR.csproj').getroot()
        for profile in ('sbs', 'mono'):
            artifact = provenance['artifacts'][profile]
            key = 'Calibration/' + artifact['file']
            data = (source / 'src/GloomhavenVR/Assets/Movies' / key).read_bytes()
            check(hashlib.sha256(data).hexdigest() == artifact['sha256'], 'reviewed calibration hash: ' + key)
            write('src/GloomhavenVR/Assets/Movies/' + key, data)
            media[key] = data
            include = 'Assets\\Movies\\' + key.replace('/', '\\')
            content = [item for item in project.findall('.//Content') if item.get('Include') == include]
            check(len(content) == 1 and content[0].get('CopyToOutputDirectory') == 'PreserveNewest'
                  and content[0].get('TargetPath') == 'Movies\\' + key.replace('/', '\\'),
                  'ordinary build copies calibration beside plugin: ' + key)
        check(not any('.mp4' in item.get('Include', '').lower() or 'Movies' in item.get('Include', '')
                      for item in project.findall('.//EmbeddedResource')), 'movies must never be embedded in DLL')
        write('src/GloomhavenVR/GloomhavenVR.csproj', b'<Project><PropertyGroup><Version>0.0.0-test</Version></PropertyGroup></Project>\n')
        for configuration in ('Debug', 'Release'):
            write('src/GloomhavenVR/bin/' + configuration + '/net472/GloomhavenVR.dll', (configuration + ' plugin substitute').encode())
            write('src/GloomhavenVR.Preload/bin/' + configuration + '/net472/GloomhavenVR.Preload.dll', (configuration + ' preloader substitute').encode())
        for name in ('UnityOpenXR.dll', 'openxr_loader.dll'):
            write('libs/Natives/' + name, b'native substitute')
        write('libs/RuntimeDeps/Unity.XR.OpenXR.dll', b'dependency substitute')
        for name in ('gloomhavenvr.bundle', 'ghvr-town.bundle', 'ghvr-town-voices.bundle', 'ghvr-environment.bundle'):
            write('prebuilt/' + name, b'asset substitute')
        part = 'ghvr-figure-meshes-5-00.bundle'
        write('prebuilt/' + part, b'UnityFS\0' + (7).to_bytes(4, 'big') + b'5.x.x\0' + b'2021.3.5f1\0')
        write('prebuilt/ghvr-figure-meshes-index.json', json.dumps({'entries': [{
            'mesh': 'figure-0000000000000000-5', 'bank': part}]}).encode())
        for name in ('LICENSE', 'packaging/INSTALL.txt.in', 'packaging/INSTALL.de.txt.in',
                     'packaging/THIRD-PARTY.txt', 'packaging/licenses/SOURCES.txt'):
            write(name, b'Fixture notice @VERSION@.\n')
        for name in ('install-steam-frame.sh', 'steam-frame-config.py', 'frame-boot-config.py'):
            write('scripts/' + name, (source / 'scripts' / name).read_bytes())
        write('GloomhavenVR-Setup.desktop', (source / 'GloomhavenVR-Setup.desktop').read_bytes())
        for name in ('library_600x900.png', 'library_header.png', 'library_hero.png', 'logo.png', 'icon.png'):
            write('src/GloomhavenVR/Assets/SteamFrameArtwork/' + name, b'\x89PNG\r\n\x1a\nfixture')
        compiler = write('tools/dotnet', b'#!/usr/bin/env bash\nprintf "%s\\n" "$@" > "$GHVR_FIXTURE_BUILD_LOG"\n')
        compiler.chmod(0o755)
        environment = dict(os.environ, PATH=str(compiler.parent) + os.pathsep + os.environ['PATH'], GHVR_USE_LOCAL_BUNDLE='0')
        environment.pop('GHVR_PACKAGE_CONFIGURATION', None)
        environment['GHVR_FIXTURE_BUILD_LOG'] = str(evidence / 'build-arguments.log')

        def package(label):
            return execute(['bash', str(root / 'scripts/package-release.sh')], evidence / (label + '.log'), environment)

        code, log = package('bash-package')
        check(code == 0, log)
        archive_path = root / 'dist/GloomhavenVR-0.0.0-test.zip'
        prefix = 'BepInEx/plugins/GloomhavenVR/Movies/'
        with zipfile.ZipFile(archive_path) as archive:
            check(archive.read('BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll') == b'Release plugin substitute', 'default package selects Release plugin')
            arguments = (evidence / 'build-arguments.log').read_text().splitlines()
            check(arguments[arguments.index('-c') + 1] == 'Release', 'default packager builds Release')
            for key, data in media.items():
                check(archive.read(prefix + key) == data, 'release ZIP carries exact plugin-local movie: ' + key)
            check(set(name for name in archive.namelist() if name.endswith(('.mov', '.mp4')))
                  == {prefix + key for key in media}, 'release ZIP includes exactly the three expected movies')
            check(all('StreamingAssets' not in name for name in archive.namelist()), 'release never overwrites game movies')
            archive.extractall(root / 'game')
        environment['GHVR_PACKAGE_CONFIGURATION'] = 'Debug'
        code, log = package('bash-debug-package')
        check(code == 0, log)
        with zipfile.ZipFile(archive_path) as archive:
            check(archive.read('BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll') == b'Debug plugin substitute', 'explicit Debug package selects Debug plugin')
            for key, data in media.items():
                check(archive.read(prefix + key) == data, 'Debug ZIP includes exact movie: ' + key)
        arguments = (evidence / 'build-arguments.log').read_text().splitlines()
        check(arguments[arguments.index('-c') + 1] == 'Debug', 'explicit Debug packager builds Debug')
        environment['GHVR_PACKAGE_CONFIGURATION'] = 'Invalid'
        saved_archive = archive_path.read_bytes()
        code, log = package('invalid-configuration')
        check(code != 0 and 'GHVR_PACKAGE_CONFIGURATION must be Debug or Release' in log, 'invalid configuration is refused')
        check(archive_path.read_bytes() == saved_archive, 'invalid configuration preserves existing ZIP')
        environment.pop('GHVR_PACKAGE_CONFIGURATION')
        previous_archive = archive_path.read_bytes()
        for key, data in media.items():
            path = root / 'src/GloomhavenVR/Assets/Movies' / key
            check(path.read_bytes() == data, 'packager preserves media source: ' + key)
            path.unlink()
            code, log = package('missing-' + path.stem)
            expected = 'missing approved campaign intro' if key.startswith('CP_Intro/') else 'missing stereo calibration movie'
            check(code != 0 and expected in log and path.name in log, 'missing media preflight rejects: ' + key)
            check(archive_path.read_bytes() == previous_archive, 'missing media preserves existing ZIP: ' + key)
            path.write_bytes(data)
        script = root / 'scripts/package-release.sh'
        original = script.read_text()
        needle = 'cp "${CALIBRATION_MOVIES[@]}" "$PLUGDIR/Movies/Calibration/"'
        check(original.count(needle) == 1, 'causal staging control binds actual calibration copy')
        for retained, missing in ((0, 'calibration-mono.mp4'), (1, 'calibration-sbs.mp4')):
            script.write_text(original.replace(needle, 'cp "${CALIBRATION_MOVIES[' + str(retained) + ']}" "$PLUGDIR/Movies/Calibration/"'))
            code, log = package('omitted-copy-' + missing)
            check(code != 0 and "packaged zip is missing '" + prefix + 'Calibration/' + missing in log,
                  'actual archive validator rejects omitted staged movie: ' + missing)
        script.write_text(original)
        shell = shutil.which('pwsh')
        if not shell and (candidate := Path.home() / '.dotnet/tools/pwsh').is_file():
            shell = str(candidate)
        if shell:
            shell_environment = dict(os.environ)
            shell_environment.setdefault('DOTNET_ROOT', str(Path.home() / '.dotnet'))
            shell_environment['PATH'] = shell_environment['DOTNET_ROOT'] + os.pathsep + shell_environment['PATH']
            code, log = execute([shell, '-NoProfile', '-NonInteractive', '-File',
                str(source / 'tests/GloomhavenVR.NativeVideoTests/MovieInstallerTests.ps1'),
                '-Installer', str(source / 'scripts/install.ps1'), '-Fixture', str(root)],
                evidence / 'powershell-installer.log', shell_environment)
            check(code == 0, log)
            print(log.strip())
        else:
            print('Native movie PowerShell installer: pwsh unavailable; deployment execution not checked on this host.')
        (evidence / 'results.json').write_text(json.dumps({'python_assertions': count,
            'scope': 'Actual bash release packager, actual PowerShell media deploy/archive blocks when available; opaque substitute intro/compiler/other binaries, reviewed calibration MP4 bytes.'}, indent=2) + '\n')
        print('Native movie packaging: ' + str(count) + ' assertions, 3 missing-source and 2 omitted-copy controls passed.')
        status = 0
    finally:
        receipt = cleanup_completed_run('native-video-packaging', status, completion, source, process_reaped=True)
        (evidence / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    main()
