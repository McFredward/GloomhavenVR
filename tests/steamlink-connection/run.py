#!/usr/bin/env python3
"""One bounded Linux CI suite; no Windows/OpenVR/device execution is claimed."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
RESOURCE = ROOT / 'src/GloomhavenVR/Assets/SteamLink/connection-reader.exe'
API = ROOT / 'src/GloomhavenVR/Assets/SteamLink/openvr_api.dll'
PIN = '1c3a2c92e188d095cb80bc56b6452237b977571beac97da9159281491990284d'


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--sanitize', action='store_true', help='ASan/UBSan native metadata controls')
    parser.add_argument('--rebuild-native', action='store_true', help='Optional manual MinGW byte-identical resource rebuild')
    parser.add_argument('--typecheck', action='store_true', help='Local net472 Unity API typecheck, excluded from ordinary CI')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    output = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    cache = output / 'owned-cache'
    if cache.exists():
        raise RuntimeError('Output cache already exists; do not overwrite an active/foreign run')
    log = output / 'run.log'
    if log.exists():
        raise RuntimeError('Use a fresh output directory to retain previous failures')
    cache.mkdir()
    records = []
    status = 1
    dotnet = os.environ.get('DOTNET', shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet'))
    env = dict(os.environ, DOTNET_CLI_USE_MSBUILD_SERVER='0', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', PYTHONDONTWRITEBYTECODE='1')

    def run(name: str, command: list[str], timeout: int = 120) -> None:
        target = output / (name + '.log')
        with target.open('wb') as sink:
            child = subprocess.Popen(command, cwd=ROOT, env=env, stdout=sink, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = child.wait(timeout=timeout)
            except BaseException:
                if child.poll() is None:
                    os.killpg(child.pid, signal.SIGKILL)
                child.wait()
                raise
        records.append({'name': name, 'exit_code': code, 'log': target.name})
        if code:
            raise RuntimeError(f'{name} failed ({code}); see {target}')

    try:
        raw = RESOURCE.read_bytes()
        assert len(raw) == 42496 and hashlib.sha256(raw).hexdigest() == PIN
        native = cache / 'native-fixture'
        flags = ['-fsanitize=address,undefined', '-fno-omit-frame-pointer', '-g'] if args.sanitize else ['-O2']
        run('native-build', [os.environ.get('CC', 'cc'), '-std=c11', '-Wall', '-Wextra', '-Werror', *flags, str(HERE / 'NativeFixture.c'), '-o', str(native)])
        if args.sanitize:
            env.update(ASAN_OPTIONS='detect_leaks=1:abort_on_error=1', UBSAN_OPTIONS='halt_on_error=1:print_stacktrace=1')
        run('native-metadata', [str(native)], 15)
        # Separate intermediate/output roots prevent this tiny suite from changing Main or another project.
        managed = cache / 'managed'
        run('managed-build', [dotnet, 'build', str(HERE / 'ConnectionTests.csproj'), '-c', 'Release', '--disable-build-servers',
                              '-p:BaseIntermediateOutputPath=' + str(managed / 'obj') + '/', '-p:OutputPath=' + str(managed / 'bin') + '/'])
        run('managed-controls', [dotnet, str(managed / 'bin/ConnectionTests.dll'), str(RESOURCE), str(API)], 30)
        if args.rebuild_native:
            rebuilt = cache / 'connection-reader.exe'
            run('resource-rebuild', ['bash', str(ROOT / 'scripts/build-steamlink-connection-reader.sh'), str(rebuilt)])
            assert rebuilt.read_bytes() == raw, 'MinGW output differs from the immutable embedded resource'
        if args.typecheck:
            modules = Path(os.environ.get('UNITY_MODULES', str(Path.home() / '.nuget/packages/unityengine.modules/2021.3.5/lib/net35')))
            typed = cache / 'typecheck'
            run('unity-net472-typecheck', [dotnet, 'build', str(HERE / 'Typecheck.csproj'), '-c', 'Release', '--disable-build-servers',
                                          '-p:UnityModulesPath=' + str(modules), '-p:BaseIntermediateOutputPath=' + str(typed / 'obj') + '/',
                                          '-p:OutputPath=' + str(typed / 'bin') + '/'])
        status = 0
    except BaseException as exc:
        records.append({'failure': str(exc)})
        print(str(exc), file=sys.stderr)
    finally:
        result = {'schema': 'ghvr-steamlink-connection-tests-v1', 'exit_code': status,
                  'helper_bytes': RESOURCE.stat().st_size, 'helper_sha256': hashlib.sha256(RESOURCE.read_bytes()).hexdigest(),
                  'sdk_loader_bytes': API.stat().st_size, 'sdk_loader_sha256': hashlib.sha256(API.read_bytes()).hexdigest(),
                  'actual_windows_execution': False, 'hardware_identity_verified': False,
                  'receiver_verified': False, 'runs': records}
        (output / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
        log.write_text('Steam Link connection focused suite; all subprocesses reaped.\n')
        spec = importlib.util.spec_from_file_location('town_run_retention', ROOT / 'scripts/town_run_retention.py')
        retention = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = retention
        spec.loader.exec_module(retention)
        cleanup = retention.cleanup_completed_run('steamlink-connection', status, log, ROOT, process_reaped=True)
        # This suite owns exactly this fresh private cache, never shared package/game inputs.
        cleanup['exact_private_fallback'] = str(cache)
        shutil.rmtree(cache)
        (output / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    print('evidence: ' + str(output))
    return status


if __name__ == '__main__':
    raise SystemExit(main())
