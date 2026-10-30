#!/usr/bin/env python3
"""Actual production GMD1 reader against independent golden/real assets and compiling controls."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def replace_once(value: str, before: str, after: str) -> str:
    if value.count(before) != 1:
        raise ValueError('Mutation no longer binds exact production source: ' + before)
    return value.replace(before, after, 1)


def main() -> int:
    base = ROOT / '.planning/debug/menu-depth-data'
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base))
    original = Path(os.environ.get('MENU_DEPTH_SOURCE_ROOT', str(ROOT))) / 'src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthHeightfield.cs'
    code = original.read_text(); before = sha(original)
    fixture = ROOT / 'tests/GloomhavenVR.MenuDepthDataTests'
    assets = ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Story'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    environment = dict(os.environ, MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    results = []
    started = time.monotonic()
    exit_code = 1

    def execute(argv: list[str], log: Path) -> tuple[int, str]:
        with log.open('w') as stream:
            child = subprocess.Popen(argv, cwd=ROOT, env=environment, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                status = child.wait(timeout=180)
            except BaseException:
                os.killpg(child.pid, signal.SIGKILL); child.wait(); raise
        return status, log.read_text(errors='replace')

    try:
        mutations = [
            ('production', code, ''),
            ('crc-guard-lost', replace_once(code, '|| Crc(bytes, 32) != U32(bytes, 20)', ''), 'wrong payload checksum is refused'),
            ('flags-guard-lost', replace_once(code, '|| U32(bytes, 12) != 3', ''), 'lost inverse-depth flags are refused'),
            ('width-cap-lost', replace_once(code, 'width > 512', 'width > 1024'), 'field width above512 is refused'),
            ('source-cap-lost', replace_once(code, 'sourceWidth > 16384', 'sourceWidth > 32768'), 'native source width above16384 is refused'),
            ('origin-flipped', replace_once(code, '* (Height - 1);', '* 0f + (1f - v) * (Height - 1);'), 'bottom-left sample remains original bottom-left'),
            ('bilinear-lost', replace_once(code, 'return (bottom * (1f - ty) + top * ty) / 65535f;', 'return _samples[y0 * Width + x0] / 65535f;'), 'bilinear interior combines four original corners'),
            ('little-endian-lost', replace_once(code, 'bytes[at] | bytes[at + 1] << 8;', 'bytes[at + 1] | bytes[at] << 8;'), 'portable golden decode'),
        ]
        for name, source, expected in mutations:
            case = run / name
            production = case / 'production'; production.mkdir(parents=True)
            (production / 'MenuDepthHeightfield.cs').write_text(source)
            for filename in ('Program.cs', 'MenuDepthDataTests.csproj'):
                shutil.copyfile(fixture / filename, case / filename)
            project = case / 'MenuDepthDataTests.csproj'
            argv = [dotnet, 'build', str(project), '-c', 'Release', '-v', 'quiet', '--nologo',
                    '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false',
                    '-p:FixtureDir=' + str(case), '-p:ProductionDir=' + str(production)]
            status, _ = execute(argv, case / 'build.log')
            if status:
                raise RuntimeError('Compilation failure is not a causal-control pass: ' + name)
            dll = case / 'bin/Release/net8.0/MenuDepthDataTests.dll'
            retained = case / 'actual-executed-artifact'; retained.mkdir()
            for artifact in dll.parent.iterdir():
                if artifact.is_file():
                    shutil.copyfile(artifact, retained / artifact.name)
            status, output = execute([dotnet, str(dll), str(assets)], case / 'run.log')
            if expected:
                passed = status != 0 and expected in output
            else:
                passed = status == 0 and 'Menu depth C# data:' in output
            results.append({'case': name, 'passed': passed, 'exit_code': status, 'expected': expected,
                            'run_log_sha256': sha(case / 'run.log'), 'production_sha256': sha(production / 'MenuDepthHeightfield.cs'),
                            'actual_executed_dll_sha256': sha(retained / dll.name)})
            if not passed:
                raise RuntimeError('Actual production/control failed: ' + name + '\n' + output)
            print(output.strip() if not expected else 'PASS causal control ' + name + ': ' + expected, flush=True)
        if sha(original) != before:
            raise RuntimeError('Main production source changed during the copied-source run')
        exit_code = 0
    except Exception as error:
        print(str(error), file=sys.stderr)
    finally:
        fixture_pins = [{'path': str(path.relative_to(ROOT)), 'sha256': sha(path)}
                        for path in [*sorted(fixture.glob('*')), Path(__file__)]]
        report = {'passed': exit_code == 0, 'source_file': str(original), 'source_sha256_before': before,
                  'source_sha256_after': sha(original), 'fixture_inputs': fixture_pins, 'cases': results,
                  'elapsed_seconds': time.monotonic() - started,
                  'scope': 'Actual production C# header/sample reader, independent fixed golden, all515 real delivered fields and seven compiling causal controls. No Unity/JSON-runtime/mesh/XR claim.'}
        (run / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
        completion = run / 'completion.log'; completion.write_text('evidence: ' + str(run) + '\n')
        cleanup = cleanup_completed_run('menu-depth-data', exit_code, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
        print('evidence: ' + str(run), flush=True)
    return exit_code


if __name__ == '__main__':
    raise SystemExit(main())
