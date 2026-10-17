#!/usr/bin/env python3
"""Compile the real connection lifecycle/monitor against named Unity/XR/session ports."""
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


def block(source: str, signature: str) -> str:
    """Retain the selected complete declaration verbatim, including its body."""
    start = source.index(signature)
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source-root', type=Path, default=ROOT,
                        help='Production checkout whose actual lifecycle/monitor are compiled')
    args = parser.parse_args()
    source_root = args.source_root.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    output = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    cache = output / 'owned-cache'; cache.mkdir()
    sources = output / 'sources'; sources.mkdir()
    completion = output / 'completion.log'
    completion.write_text('Evidence: ' + str(output) + '\n')
    inputs = [source_root / 'src/GloomhavenVR/Core/SteamLink/SteamLinkConnectionLifecycle.cs',
              source_root / 'src/GloomhavenVR/Core/SteamLink/SteamLinkConnection.cs',
              HERE / 'Boundary.cs', HERE / 'Program.cs', HERE / 'Fixture.csproj', Path(__file__)]
    pins = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    (output / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    for path in inputs: shutil.copyfile(path, sources / path.name)
    lifecycle, connection = (path.read_text() for path in inputs[:2])
    # These declarations are exact production text. The replacement facade only
    # injects a reader into Monitor; it does not implement availability/epochs.
    declarations = [block(connection, signature) for signature in (
        'internal enum Reason', 'internal sealed class Snapshot', 'internal sealed class Monitor',
        'internal static Snapshot Classify(')]
    text_start = connection.index('private static bool Text(')
    text_end = connection.index(';', text_start) + 1
    declarations.append(connection[text_start:text_end])
    extracted = ('using System; using System.Linq; using System.Threading; using System.Threading.Tasks;\n'
                 + 'namespace GloomhavenVR.Core.SteamLink;\ninternal static partial class SteamLinkConnection\n{\n'
                 + '\n'.join(declarations) + '\n}\n')
    (sources / 'Connection.cs').write_text(extracted)
    dotnet = os.environ.get('DOTNET', shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet'))
    env = dict(os.environ, DOTNET_CLI_USE_MSBUILD_SERVER='0', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
               PYTHONDONTWRITEBYTECODE='1')
    results: list[dict] = []
    status = 1

    def run(name: str, command: list[str], expected: int = 0) -> str:
        target = output / (name + '.log')
        with target.open('wb') as log:
            child = subprocess.Popen(command, cwd=source_root, env=env, stdout=log,
                                     stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = child.wait(timeout=120)
            except BaseException:
                if child.poll() is None: os.killpg(child.pid, signal.SIGKILL)
                child.wait()
                raise
        results.append({'case': name, 'exit_code': code, 'expected': expected, 'log': target.name})
        if code != expected:
            raise RuntimeError(f'{name} returned {code}, expected {expected}; see {target}')
        return target.read_text()

    try:
        # Reintroduce the exact old camera coupling. The old class is not compiled
        # in isolation with missing Checking; both variants have the current API.
        # Only the old invalidation/Tick dependency is restored, so its failure
        # must be the semantic regression assertion, never a compiler error.
        tick = 'SteamLinkConnection.Tick(true, Time.unscaledTime);'
        assert lifecycle.count(tick) == 1, 'Production active-session Tick shape changed; review the causal mutant'
        field = 'private static bool _watching;'
        assert lifecycle.count(field) == 1
        old = lifecycle.replace(field, field + '\n    private static Camera? _head;')
        old = old.replace(tick, '''Camera? head = VRCameraPolicy.AllowedHead;
        if (_head != head)
        {
            _head = head;
            SteamLinkConnection.Invalidate();
        }
        SteamLinkConnection.Tick(head != null && head.isActiveAndEnabled, Time.unscaledTime);''')
        (sources / 'camera-dependent-mutant.cs').write_text(old)
        for name, selected in (('production', lifecycle), ('camera-dependent-mutant', old)):
            project = cache / name; project.mkdir()
            for fixed in ('Fixture.csproj', 'Boundary.cs', 'Program.cs'):
                shutil.copyfile(HERE / fixed, project / fixed)
            (project / 'Connection.cs').write_text(extracted)
            (project / 'Lifecycle.cs').write_text(selected)
            run(name + '-build', [dotnet, 'build', str(project / 'Fixture.csproj'), '-c', 'Release',
                '--disable-build-servers'])
            logged = run(name + '-controls', [dotnet, str(project / 'bin/Release/net8.0/Fixture.dll')],
                         expected=0 if name == 'production' else 7)
            if name == 'production':
                assert 'lifecycle/actual-monitor assertions' in logged, logged
                print(logged.strip())
            else:
                assert 'CAMERA_LOADING_REGRESSION: camera/loading must not revoke a proven Frame connection' in logged
                assert 'error CS' not in logged
        assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest() == pin for p, pin in pins.items()), \
            'Source changed during test; rerun only this bounded suite'
        status = 0
    except BaseException as error:
        results.append({'failure': str(error)})
        print(str(error), file=sys.stderr)
    finally:
        spec = importlib.util.spec_from_file_location('town_run_retention', ROOT / 'scripts/town_run_retention.py')
        retention = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = retention
        spec.loader.exec_module(retention)
        cleanup = retention.cleanup_completed_run('steamlink-loading-lifecycle', status, completion, ROOT,
                                                   process_reaped=True)
        # All children have stopped/reaped. This exact fresh cache belongs only
        # to this invocation; source copies, logs, pins and reports stay intact.
        shutil.rmtree(cache)
        (output / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
        (output / 'result.json').write_text(json.dumps({
            'schema': 'ghvr-steamlink-loading-lifecycle-v1', 'exit_code': status,
            'source_root': str(source_root), 'runs': results,
            'scope': 'Actual production lifecycle, Monitor, Snapshot and Classify; explicit Unity/XR/session/reader boundaries.',
            'actual_unity_rendering': False, 'actual_openvr_execution': False,
            'actual_headset_output_verified': False}, indent=2) + '\n')
    print('Evidence: ' + str(output))
    return status


if __name__ == '__main__':
    raise SystemExit(main())
