#!/usr/bin/env python3
"""Compile only the authored embedded Android/Vulkan loading shader, never game art."""
import argparse
import ctypes
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

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def reap_owned(process):
    """Unity can leave its compiler/Xvfb children alive after its own exit."""
    reaped = []
    for sig in (signal.SIGTERM, signal.SIGKILL):
        if process is not None:
            try: os.killpg(process.pid, sig)
            except ProcessLookupError: pass
        for entry in Path('/proc').iterdir():
            if not entry.name.isdigit(): continue
            try:
                parent = int((entry / 'stat').read_text().rsplit(')', 1)[1].split()[1])
                if parent == os.getpid(): os.kill(int(entry.name), sig)
            except (FileNotFoundError, ProcessLookupError): pass
        deadline = time.monotonic() + 2
        while time.monotonic() < deadline:
            try: child, status = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError: break
            if child: reaped.append({'pid': child, 'status': status})
            else: time.sleep(.02)
    return reaped


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--unitypy-python', type=Path, default=Path.home() / 'unitypy-venv/bin/python')
    args = parser.parse_args()
    if ctypes.CDLL(None).prctl(36, 1, 0, 0, 0) != 0:
        raise RuntimeError('The isolated Unity producer requires child-process adoption for safe cleanup.')
    root = args.source_root.resolve()
    out = ROOT / '.planning/debug/quest-loading-shader-build'
    out.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    project = run / 'unity'
    for name in ('Assets/Editor', 'Assets/QuestLoading', 'Packages', 'ProjectSettings'):
        (project / name).mkdir(parents=True, exist_ok=True)
    source = root / 'src/GloomhavenVR/Assets/QuestLoading/QuestLoadingSpinner.shader'
    editor = ROOT / 'scripts/quest-loading-shader/Editor/QuestLoadingShaderBuilder.cs'
    verifier = ROOT / 'scripts/quest-loading-shader/verify_bundle.py'
    shader_meta = Path(str(source) + '.meta')
    pins = {str(path.relative_to(root)): sha(path) for path in (source, shader_meta, editor, verifier, Path(__file__).resolve())}
    shutil.copyfile(source, project / 'Assets/QuestLoading/QuestLoadingSpinner.shader')
    shutil.copyfile(shader_meta, project / 'Assets/QuestLoading/QuestLoadingSpinner.shader.meta')
    shutil.copyfile(editor, project / 'Assets/Editor/QuestLoadingShaderBuilder.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    status = 1
    process = None
    try:
        command = [str(args.unity), '-batchmode', '-nographics', '-buildTarget', 'Android', '-projectPath', str(project),
                   '-executeMethod', 'QuestLoadingShaderBuilder.Build', '-loadingBundleOutput', str(run / 'bundle'), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
        with (run / 'launch.log').open('w') as stream:
            process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            try: code = process.wait(timeout=240)
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL); process.wait()
        if code or not (run / 'bundle/build-result.txt').is_file(): raise RuntimeError('Tiny shader compilation failed; see ' + str(run))
        if any(sha(root / name) != digest for name, digest in pins.items()): raise RuntimeError('Compiler source changed during tiny build.')
        bank = run / 'bundle/ghvr-quest-loading.bundle'
        with (run / 'metadata.log').open('w') as stream:
            subprocess.run([str(args.unitypy_python), str(verifier), str(bank), str(run / 'metadata.json')],
                           stdout=stream, stderr=subprocess.STDOUT, check=True, timeout=60)
        actual = json.loads((run / 'metadata.json').read_text())
        destination = source.parent / 'ghvr-quest-loading.bundle'
        shutil.copyfile(bank, destination)
        provenance = {'schema': 1, 'unityVersion': '2021.3.5f1', 'target': 'Android', 'graphicsApi': 'Vulkan',
                      'shaderName': actual['shaderName'], 'assetPath': actual['assetPath'], 'bundleName': destination.name,
                      'bundleBytes': destination.stat().st_size, 'bundleSha256': sha(destination),
                      'resource': 'GloomhavenVR.QuestLoading.spinner.bundle', 'sourceSha256': sha(source), 'sourcePins': pins,
                      'typeTreesEnabled': True, 'chunkBasedCompression': True, 'colorSpace': 'Linear',
                      'stereoRenderingPath': 'MultiPass',
                      'limits': actual['limits']}
        (source.parent / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps(provenance, indent=2) + '\n')
        status = 0
        print('PASS embedded loading bank: ' + str(destination.stat().st_size) + ' bytes; ' + str(destination), flush=True)
    finally:
        reaped = reap_owned(process)
        sys.path.insert(0, str(ROOT / 'scripts'))
        from town_run_retention import cleanup_completed_run
        receipt = cleanup_completed_run('quest-loading-shader-build', status, completion, ROOT,
                                        process_reaped=process is None or process.poll() is not None)
        (run / 'cleanup.json').write_text(json.dumps({'retention': receipt, 'childrenReaped': reaped}, indent=2) + '\n')
        print('Evidence: ' + str(run), flush=True)
    return status


if __name__ == '__main__':
    raise SystemExit(main())
