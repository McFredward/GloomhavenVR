#!/usr/bin/env python3
"""Join unchanged original managed/Unity callbacks to real native process ownership."""
import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
spec = importlib.util.spec_from_file_location('quest_owned_reap', ROOT / 'scripts/quest-world-material-tests.py')
owned = importlib.util.module_from_spec(spec)
spec.loader.exec_module(owned)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--control', choices=('respawn',))
    parser.add_argument('--journal', type=Path, default=Path('/home/claw/quest3-local/full-procedural/campaign-host-v1/output/native-requests/calls.jsonl'))
    args = parser.parse_args()
    base = ROOT / '.planning/debug/quest-native-reentry'
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base))
    project = run / 'unity'
    runtime = project / 'Assets/Runtime'
    plugins = project / 'Assets/Plugins/x86_64'
    for path in (runtime, plugins):
        path.mkdir(parents=True, exist_ok=True)
    log = run / 'runner.log'
    log.write_text('Quest actual native reentry: ' + str(run) + '\n')
    print(log.read_text(), end='', flush=True)
    ctypes.CDLL(None).prctl(36, 1, 0, 0, 0)
    ndk = Path('/home/claw/unity-2021.3.5/Editor/Data/PlaybackEngines/AndroidPlayer/NDK/toolchains/llvm/prebuilt/linux-x86_64/bin')
    original = (ROOT / 'ressources').resolve() / 'GH_Data'
    sources = ROOT / 'tools/quest-procedural-runtime'
    snapshot = run / 'native-source'
    snapshot.mkdir()
    code, process = 1, None
    try:
        for name in ('worker.c', 'bridge.c', 'protocol.h'):
            shutil.copy2(sources / name, snapshot / name)
        if args.control == 'respawn':
            path = snapshot / 'bridge.c'
            source = path.read_text()
            before = '(channel < 0 && !launch())'
            assert source.count(before) == 1
            path.write_text(source.replace(before, '!launch()', 1))
        inputs = project / 'NativeInputs'
        payload, state = inputs / 'payload', inputs / 'state'
        payload.mkdir(parents=True)
        state.mkdir()
        def checked(command, name):
            with (run / name).open('w') as output:
                subprocess.run(command, check=True, stdout=output, stderr=subprocess.STDOUT, timeout=90)
        sys.path.insert(0, str(sources))
        import runtime as native_runtime
        definition = snapshot / 'kernel32.def'
        definition.write_text('LIBRARY kernel32.dll\nEXPORTS\n' + '\n'.join(native_runtime.KERNEL_IMPORTS) + '\n')
        checked([str(ndk / 'ld.lld'), '-m', 'i386pep', '--shared', '--out-implib=' + str(snapshot / 'kernel32.lib'), str(definition), '-o', str(snapshot / 'kernel32-import-placeholder.dll')], 'imports-build.log')
        checked([str(ndk / 'clang'), '--target=x86_64-pc-windows-msvc', '-ffreestanding', '-fno-builtin', '-fno-stack-protector', '-O2', '-Wall', '-Wextra', '-Werror', '-c', str(snapshot / 'worker.c'), '-o', str(snapshot / 'worker.obj')], 'worker-compile.log')
        checked([str(ndk / 'ld.lld'), '-flavor', 'link', '/entry:Main', '/subsystem:console', '/nodefaultlib', '/out:' + str(payload / 'ApparanceWorker.exe'), str(snapshot / 'worker.obj'), str(snapshot / 'kernel32.lib')], 'worker-link.log')
        worker_bytes = (payload / 'ApparanceWorker.exe').read_bytes()
        (snapshot / 'quest_worker_payload.h').write_text('static const unsigned char ghpr_worker_payload[] = {' + ','.join(str(value) for value in worker_bytes) + '};\n')
        shutil.copy2(payload / 'ApparanceWorker.exe', inputs / 'expected-worker.exe')
        (payload / 'ApparanceWorker.exe').unlink()  # prove embedded delivery, with no retained PE fallback
        checked(['/usr/bin/cc', '-std=c11', '-O2', '-fPIC', '-shared', '-fvisibility=hidden', '-Wall', '-Wextra', '-Werror', '-DGHPR_HOST_PROOF', '-DGHPR_EMBEDDED_WORKER', str(snapshot / 'bridge.c'), '-pthread', '-o', str(plugins / 'libApparanceEngine.so')], 'bridge-build.log')
        shutil.copy2(original / 'Plugins/x86_64/ApparanceEngine.dll', payload / 'ApparanceEngine.dll')
        for name in ('Apparance.Net.dll', 'Apparance.Unity.dll'):
            shutil.copy2(original / 'Managed' / name, plugins / name)
        for folder, name in [('harmonyx/2.7.0/lib/net45', '0Harmony.dll'), ('monomod.runtimedetour/21.12.13.1/lib/net452', 'MonoMod.RuntimeDetour.dll'), ('monomod.utils/21.12.13.1/lib/net452', 'MonoMod.Utils.dll')]:
            shutil.copy2(Path.home() / '.nuget/packages' / folder / name, plugins / name)
        for binary in (Path.home() / '.nuget/packages/mono.cecil/0.11.4/lib/net40').glob('Mono.Cecil*.dll'):
            shutil.copy2(binary, plugins / binary.name)
        fixture = ROOT / 'tests/QuestScenarioEvidence.Tests'
        shutil.copy2(fixture / 'NativeReentryProbe.cs', runtime / 'Probe.cs')
        decompiled = original.parent.parent / 'decompiled/GH.Runtime'
        for name in ('ProceduralBase.cs', 'IProceduralContentMonitor.cs'):
            shutil.copy2(decompiled / name, runtime / name)
        rows = []
        for line in args.journal.read_text().splitlines():
            row = json.loads(line)
            if row['operation'] not in ('create', 'build', 'asset-response'):
                continue
            rows.append(line)
            if row['operation'] == 'build':
                source = args.journal.parent / row['buffer']
                if source.parent != args.journal.parent or hashlib.sha256(source.read_bytes()).hexdigest() != row['sha256']:
                    raise AssertionError('Recorded effective parameter buffer differs')
                shutil.copy2(source, inputs / source.name)
        (inputs / 'calls.jsonl').write_text('\n'.join(rows) + '\n')
        streaming = project / 'Assets/StreamingAssets'
        streaming.mkdir()
        shutil.copytree(original / 'StreamingAssets/Procedures', streaming / 'Procedures')
        (project / 'Packages').mkdir()
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.jsonserialize":"1.0.0"}}\n')
        (project / 'ProjectSettings').mkdir()
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in [sources / 'bridge.c', sources / 'worker.c', sources / 'protocol.h', original / 'Managed/Apparance.Net.dll', original / 'Managed/Apparance.Unity.dll', original / 'Plugins/x86_64/ApparanceEngine.dll', args.journal, fixture / 'NativeReentryProbe.cs', decompiled / 'ProceduralBase.cs']}
        (run / 'inputs.json').write_text(json.dumps({'sha256': hashes, 'control': args.control, 'nativeParameters': 'exact recorded effective bytes', 'assetReplies': 'exact recorded bounds/variant data', 'assetPrefabs': 'small fixture placeholders; original artwork not certified'}, indent=2) + '\n')
        env = dict(os.environ, WINEDEBUG='-all', MSBUILDDISABLENODEREUSE='1')
        command = ['/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'QuestNativeReentryProbe.Run', '-logFile', str(run / 'unity.log')]
        process = subprocess.Popen(command, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
        code = process.wait(timeout=180)
        cached_worker = state / 'apparance-worker.exe'
        if not args.control and (cached_worker.read_bytes() != worker_bytes or stat.S_IMODE(cached_worker.stat().st_mode) != 0o600):
            raise AssertionError('Private worker bytes or0600 ownership mode differ')
        for name in ('Apparance.Net.dll', 'Apparance.Unity.dll'):
            if (plugins / name).read_bytes() != (original / 'Managed' / name).read_bytes():
                raise AssertionError('Unchanged original managed assembly differed after import: ' + name)
        report_file = project / 'ProbeOutput/native-reentry.json'
        if report_file.is_file():
            shutil.copy2(report_file, run / 'result.json')
        if args.control:
            expected = 'All previously created native handles finish actual Unity placement after original code reconnect'
            report = json.loads(report_file.read_text()) if report_file.is_file() else {}
            if code == 0 or ('InvalidOperationException: ' + expected) not in (run / 'unity.log').read_text() or report.get('busy') != 18 or report.get('queued') != 0:
                raise AssertionError('Original process-loss control did not reproduce submitted busy incomplete handles')
            (run / 'control.json').write_text(json.dumps({'control': args.control, 'rejected': True, 'expected': expected}, indent=2) + '\n')
            print('PASS original native respawn control: 18 busy/unqueued/incomplete handles', flush=True)
            code = 0
        else:
            if code:
                raise RuntimeError('Actual native reentry fixture failed: ' + str(run / 'unity.log'))
            report = json.loads(report_file.read_text())
            assert report['complete'] == 18 and report['busy'] == 0 and report['queued'] == 0 and report['sameWorker']
            print('PASS actual native reentry: ' + str(report), flush=True)
    except BaseException:
        code = 1
        raise
    finally:
        if process is not None and process.poll() is None:
            process.kill()
            process.wait()
        (run / 'process-cleanup.json').write_text(json.dumps(owned.reap_owned(process.pid if process else 1073741823), indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-native-reentry', code, log, ROOT, process_reaped=True), indent=2) + '\n')
        # Wine's generated prefix alone is regenerable; preserve task inputs,
        # source, original identities, native logs, results and process receipts.
        prefix = state / 'wine-prefix' if 'state' in locals() else None
        if prefix is not None and prefix.is_dir():
            shutil.rmtree(prefix)


if __name__ == '__main__':
    main()
