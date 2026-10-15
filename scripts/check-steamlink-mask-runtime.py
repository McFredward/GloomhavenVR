#!/usr/bin/env python3
"""Focused production lifecycle controls and real-Unity-reference type checking, not an XR proof."""
from pathlib import Path
import argparse
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def execute(command, log):
    with log.open('w') as stream:
        child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                 cwd=ROOT, start_new_session=True)
        try:
            return child.wait(timeout=120)
        except subprocess.TimeoutExpired:
            os.killpg(child.pid, signal.SIGKILL)
            child.wait()
            return 124
        except BaseException:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--compile-runtime', action='store_true', help='Also type-check runtime against actual local Unity references')
    mode.add_argument('--compile-only', action='store_true', help='Recheck only actual Unity-reference typing after a bounded repair')
    parser.add_argument('--output-root', type=Path, default=ROOT / '.planning/debug/steamlink-mask-runtime')
    parser.add_argument('--retention-checkout', type=Path, default=ROOT)
    args = parser.parse_args()
    args.output_root = args.output_root.absolute()
    args.output_root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_root))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    framework = Path.home() / '.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
    source = ROOT / 'tests/steamlink-mask-runtime'
    result = {'scope': 'focused production lifecycle controls; actual Unity-reference typing only when opted in',
              'xr_callbacks_tested': False, 'native_carriage_proved': False, 'stages': []}
    code = 1
    try:
        projects = [('portable', 'Controls')]
        if args.compile_runtime: projects.append(('compile', 'RuntimeCompile'))
        if args.compile_only: projects = [('compile', 'RuntimeCompile')]
        for folder, name in projects:
            project_dir = run / folder
            project_dir.mkdir()
            project = project_dir / (name + '.csproj')
            shutil.copy2(source / (name + '.csproj'), project)
            command = [dotnet, 'build', '--disable-build-servers', str(project), '--configfile', str(source / 'NuGet.Config'),
                       '-c', 'Release', '-p:SourceRoot=' + str(ROOT), '-p:UseSharedCompilation=false']
            if folder == 'compile':
                command.append('-p:FrameworkReferenceDir=' + str(framework))
            code = execute(command, run / (folder + '-build.log'))
            result['stages'].append({'name': folder + '-build', 'exit_code': code})
            if code:
                return code
            if folder == 'portable':
                dll = project_dir / 'bin/Release/net8.0/Controls.dll'
                code = execute([dotnet, str(dll), str(ROOT / 'src/GloomhavenVR/Assets/SteamLink')], run / 'controls.json')
                result['stages'].append({'name': 'production-lifecycle-controls', 'exit_code': code})
                if code:
                    return code
        code = 0
        return 0
    finally:
        result['exit_code'] = code
        (run / 'result.json').write_text(json.dumps(result, indent=2, sort_keys=True) + '\n')
        cleanup = cleanup_completed_run('steamlink-mask-runtime', code, completion,
                                        args.retention_checkout, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(cleanup, indent=2, sort_keys=True) + '\n')
        print('evidence: ' + str(run))
        print(json.dumps(result, sort_keys=True))


if __name__ == '__main__':
    raise SystemExit(main())
