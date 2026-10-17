#!/usr/bin/env python3
"""Build the small real-scene MR shader bank without rebuilding world assets."""
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


def digest(data):
    return hashlib.sha256(data).hexdigest()


def stage_sources(root, project):
    stage = project / 'Assets/SteamLink'
    stage.mkdir(parents=True)
    pins = {}
    for name in ('SteamLinkSceneAtlas.shader', 'SteamLinkSceneCoverage.shader'):
        path = root / 'unity/Assets/Shaders' / name
        data = path.read_bytes()
        pins[str(path.relative_to(root))] = digest(data)
        (stage / name).write_bytes(data)
    variants_path = root / 'tests/steamlink-transparent-materials/variants.json'
    if variants_path.is_file():
        manifest = json.loads(variants_path.read_text())
        pins[str(variants_path.relative_to(root))] = digest(variants_path.read_bytes())
        for entry in manifest['variants']:
            original = (root / entry['source']).resolve()
            original.relative_to(root.resolve())
            data = original.read_bytes()
            assert digest(data) == entry['source_sha256'], 'Original owned shader has changed: ' + entry['source']
            pins[entry['source']] = digest(data)
            text = data.decode('utf-8-sig')
            for replacement in entry['replacements']:
                assert text.count(replacement['before']) == replacement['count'], 'Variant binding drift: ' + entry['source']
                text = text.replace(replacement['before'], replacement['after'])
            assert ('Shader "' + entry['variant_shader'] + '"') in text, 'Distinct derivative identity required'
            relative = original.relative_to(root / 'unity/GloomhavenVR.Assets/Assets/Bundle')
            target = stage / 'Coverage' / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text)
            for include in original.parent.glob('*.cginc'):
                shutil.copyfile(include, target.parent / include.name)
                pins[str(include.relative_to(root))] = digest(include.read_bytes())
    return pins


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    log = run / 'completion.log'
    log.write_text('Owned real-scene shader build: ' + str(run) + '\n')
    project = run / 'owned-unity-project'
    (project / 'Assets/Editor').mkdir(parents=True)
    shutil.copyfile(Path(__file__).parent / 'Editor/SceneShaderBuilder.cs', project / 'Assets/Editor/SceneShaderBuilder.cs')
    for folder in ('Packages', 'ProjectSettings'):
        (project / folder).mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    process = None
    status = 1
    try:
        pins = stage_sources(root, project)
        (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
        command = ['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-nographics',
            '-projectPath', str(project), '-executeMethod', 'SceneShaderBuilder.Build',
            '-sceneBundleOutput', str(run / 'bundle'), '-logFile', str(run / 'unity.log')]
        with (run / 'launch.log').open('w') as stream:
            process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = process.wait(timeout=300)
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
        assert code == 0 and (run / 'bundle/build-result.txt').is_file(), 'Build failed; see ' + str(run)
        assert all(digest((root / path).read_bytes()) == pin for path, pin in pins.items()), 'Sources changed during build'
        bank = run / 'bundle/scene.bundle'
        # A successful BuildPipeline return alone does not establish a loadable
        # bank: disabling the builtin assetbundle module silently strips its
        # container while still serializing all requested Shader objects.
        with (run / 'metadata.log').open('w') as stream:
            subprocess.run([str(Path.home() / 'unitypy-venv/bin/python'),
                str(Path(__file__).parent / 'verify_bundle.py'), str(bank),
                str(root / 'tests/steamlink-transparent-materials/variants.json'), str(run / 'metadata.json')],
                stdout=stream, stderr=subprocess.STDOUT, check=True, timeout=60)
        asset = root / 'src/GloomhavenVR/Assets/SteamLink/scene.bundle'
        asset.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(bank, asset)
        receipt = {'unity': '2021.3.5f1', 'target': 'StandaloneWindows64',
            'graphics_apis': ['Direct3D11', 'OpenGLCore', 'Vulkan'], 'source_pins': pins,
            'bundle_sha256': digest(asset.read_bytes()), 'bundle_bytes': asset.stat().st_size,
            'resource': 'GloomhavenVR.SteamLink.scene.bundle',
            'limits': 'Compiled shader bank only; GPU and real XR routing require separate checks.'}
        (asset.parent / 'provenance.json').write_text(json.dumps(receipt, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps(receipt, indent=2) + '\n')
        status = 0
        print('PASS scene bank build: ' + str(asset), flush=True)
    finally:
        sys.path.insert(0, str(root / 'scripts'))
        from town_run_retention import cleanup_completed_run
        receipt = cleanup_completed_run('steamlink-scene-bank', status, log, root, process_reaped=process is None or process.poll() is not None)
        if project.is_dir() and not project.is_symlink(): shutil.rmtree(project)
        (run / 'cleanup.json').write_text(json.dumps({'retention_api': receipt,
            'owned_project_removed': str(project)}, indent=2) + '\n')
    return status


if __name__ == '__main__':
    raise SystemExit(main())
