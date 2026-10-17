#!/usr/bin/env python3
"""Optionally verify the produced plugin's path binding with real GH.Runtime under Unity Mono.

Does not start Unity, OpenVR, the helper, or a headset session. Input DLLs are read
only. A source fixture pass does not substitute for this produced-artifact proof.
"""
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dll', type=Path, required=True, help='Actual final built GloomhavenVR.dll, expected fixed')
    parser.add_argument('--managed', type=Path, required=True, help='Read-only game Managed directory containing GH.Runtime.dll')
    parser.add_argument('--mono', type=Path, required=True, help='Unity MonoBleedingEdge/bin/mono executable')
    parser.add_argument('--expected-build', type=int)
    parser.add_argument('--legacy-dll', type=Path, help='Optional retained broken689 artifact; expected refused in a separate process')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default=os.environ.get('DOTNET', shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    output = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    cache = output / 'owned-cache'
    cache.mkdir()
    log = output / 'run.log'
    log.write_text('evidence: ' + str(output) + '\n')
    records = []
    status = 1
    inputs: dict[str, Path] = {}

    def pin(path: Path) -> dict:
        return {'path': str(path), 'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}

    def run(name: str, command: list[str], timeout: int = 30) -> str:
        child = subprocess.Popen(command, cwd=cache, env=dict(os.environ, PYTHONDONTWRITEBYTECODE='1'), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, start_new_session=True)
        try:
            text, _ = child.communicate(timeout=timeout)
        except BaseException:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            text, _ = child.communicate()
            (output / (name + '.log')).write_text(text)
            raise
        (output / (name + '.log')).write_text(text)
        records.append({'name': name, 'exit_code': child.returncode})
        if child.returncode:
            raise RuntimeError(name + ' failed; see retained log')
        return text

    before = {}
    try:
        mono = args.mono.resolve(strict=True)
        references = mono.parent.parent / 'lib/mono/4.5'
        inputs = {'plugin': args.dll.resolve(strict=True), 'GH.Runtime': (args.managed / 'GH.Runtime.dll').resolve(strict=True),
                  'mono': mono, **{name: (references / name).resolve(strict=True) for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')}}
        if args.legacy_dll:
            inputs['legacy_plugin'] = args.legacy_dll.resolve(strict=True)
        before = {name: pin(path) for name, path in inputs.items()}
        source = output / 'source'
        source.mkdir()
        fixture = source / 'ProducedArtifactProbe.cs'
        shutil.copyfile(HERE / 'artifact/ProducedArtifactProbe.cs', fixture)
        sdks = run('sdk-list', [args.dotnet, '--list-sdks'])
        compilers = [Path(line.split('[', 1)[1].rstrip(']')) / line.split()[0] / 'Roslyn/bincore/csc.dll'
                     for line in sdks.splitlines() if '[' in line]
        compiler = next(path for path in reversed(compilers) if path.is_file())
        executable = cache / 'ProducedArtifactProbe.exe'
        run('compile', [args.dotnet, str(compiler), '-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-langversion:latest', '-optimize+',
                        '-out:' + str(executable), *['-reference:' + str(inputs[name]) for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')], str(fixture)])
        command = [str(mono), str(executable), str(inputs['plugin']), str(inputs['GH.Runtime'].parent), 'fixed']
        if args.expected_build is not None:
            command.append(str(args.expected_build))
        run('produced-plugin-fixed', command)
        if args.legacy_dll:
            run('retained-legacy-refusal', [str(mono), str(executable), str(inputs['legacy_plugin']), str(inputs['GH.Runtime'].parent), 'legacy'])
        assert before == {name: pin(path) for name, path in inputs.items()}, 'Read-only input changed during probe'
        status = 0
    except BaseException as error:
        records.append({'failure': str(error)})
        print(str(error), file=sys.stderr)
    finally:
        (output / 'result.json').write_text(json.dumps({'schema': 'ghvr-steamlink-produced-binding-v1', 'exit_code': status,
            'inputs': before, 'runs': records,
            'actual_produced_binary_invoked': any(record.get('name') == 'produced-plugin-fixed' for record in records),
            'actual_windows_execution': False, 'actual_proton_execution': False, 'actual_headset_query': False}, indent=2) + '\n')
        spec = importlib.util.spec_from_file_location('town_run_retention', ROOT / 'scripts/town_run_retention.py')
        retention = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = retention
        spec.loader.exec_module(retention)
        cleanup = retention.cleanup_completed_run('steamlink-produced-binding', status, log, ROOT, process_reaped=True)
        cleanup['exact_private_fallback'] = str(cache)
        shutil.rmtree(cache)
        (output / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    print('evidence: ' + str(output))
    return status


if __name__ == '__main__':
    raise SystemExit(main())
