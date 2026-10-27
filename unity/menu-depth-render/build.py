#!/usr/bin/env python3
"""Build the actual dual-eye and depth-writing UI shaders with the game's Unity version."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers, cleanup_project_editors


def execute(argv, log):
    with log.open('w') as stream:
        process = subprocess.Popen(argv, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=420)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()


def main():
    output = ROOT / '.planning/debug/menu-depth-capture'
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    log = run / 'completion.log'
    log.write_text('evidence: ' + str(run) + '\n')
    print('Evidence: ' + str(run), flush=True)
    status = 1
    try:
        project = run / 'unity'
        for name in ('Assets/Editor', 'Assets/MenuDepth', 'Packages', 'ProjectSettings'):
            (project / name).mkdir(parents=True)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        # A versioned minimal project no longer receives Unity's template modules.
        # AssetBundle must be enabled or BuildAssetBundles silently omits native
        # class142 from the serialized file, producing an unloadable archive.
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}\n')
        source = ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering'
        files = [source / name for name in ('MenuDepthDisplay.shader', 'MenuDepthGraphic.shader', 'MenuDepthCharacter.shader', 'Unity-LICENSE.txt')]
        files.append(Path(__file__).parent / 'Editor/MenuDepthBuilder.cs')
        for path in files:
            shutil.copyfile(path, project / ('Assets/Editor' if path.suffix == '.cs' else 'Assets/MenuDepth') / path.name)
        result = run / 'bundle'
        argv = ['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
                '-projectPath', str(project), '-executeMethod', 'MenuDepthBuilder.Build',
                '-menuDepthOutput', str(result), '-logFile', str(run / 'unity.log')]
        (run / 'command.json').write_text(json.dumps(argv, indent=2) + '\n')
        status = execute(argv, run / 'launch.log')
        if status:
            raise RuntimeError('Menu-depth shader bundle build failed; evidence=' + str(run))
        bundle = result / 'menu-depth.bundle'
        shutil.copyfile(bundle, source / bundle.name)
        digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
        provenance = {'unity_version': '2021.3.5f1', 'target': 'StandaloneWindows64',
                      'graphics_apis': ['Direct3D11', 'OpenGLCore', 'Vulkan'], 'type_trees': True,
                      'runtime_load_checked': True,
                      'bundle_bytes': bundle.stat().st_size, 'bundle_sha256': digest(bundle),
                      'sources': [{'path': str(p.relative_to(ROOT)), 'sha256': digest(p)} for p in files]}
        (source / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps(provenance, indent=2) + '\n')
        print(json.dumps({'status': 'pass', 'bytes': bundle.stat().st_size, 'sha256': digest(bundle)}))
        return 0
    finally:
        editors = cleanup_project_editors(run / 'unity', Path('/home/claw/unity-2021.3.5/Editor/Unity'), launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']:
            raise RuntimeError('Private Unity editor survived reaping; its generated caches are retained.')
        compilers = cleanup_project_compilers(run / 'unity', editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Project compiler survived reaping; its generated caches are retained.')
        retained = run / 'assemblies/editor'; retained.mkdir(parents=True, exist_ok=True)
        hashes = {}
        for compiled in sorted((run / 'unity/Library/ScriptAssemblies').glob('*')):
            if compiled.is_file() and compiled.suffix in ('.dll', '.pdb', '.mdb'):
                shutil.copyfile(compiled, retained / compiled.name)
                hashes[compiled.name] = hashlib.sha256(compiled.read_bytes()).hexdigest()
        (run / 'editor-assemblies.json').write_text(json.dumps(hashes, indent=2) + '\n')
        cleanup = cleanup_completed_run('menu-depth-shader-build', status, log, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())
