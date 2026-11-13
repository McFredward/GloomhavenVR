#!/usr/bin/env python3
"""Validate diagnostic material recovery in the real original editor and a private project."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def hashes(root):
    return {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted(root.rglob('*')) if path.is_file()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, required=True)
    parser.add_argument('--probe-assets', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--reviewed-project', type=Path,
                        help='Optional local build609 snapshot; reads only its recovered materials.')
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    source = args.probe_assets.resolve(strict=True)
    if not (source / 'Resources/quest-original-model.prefab').is_file():
        parser.error('Owned original model slice is required; no synthetic substitute is accepted.')
    source_hashes = hashes(source)
    reviewed = args.reviewed_project.resolve(strict=True) if args.reviewed_project else None
    reviewed_materials = reviewed / 'Assets/Quest/Recovered/NativeDependencies/Material' if reviewed else None
    reviewed_hashes = hashes(reviewed_materials) if reviewed_materials else None
    destination_root = args.output_root.resolve()
    if destination_root.is_relative_to(source) or (reviewed and destination_root.is_relative_to(reviewed)):
        parser.error('Fixture output must be separate from read-only original assets and reviewed projects.')
    args.output_root.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix='fixture-', dir=args.output_root.resolve()))
    project = work / 'project'
    editor = project / 'Assets/Editor'
    editor.mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    shutil.copytree(source, project / 'Assets/Quest/Recovered')
    shutil.copy2(repository / 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestProbeMaterialConversion.cs', editor)
    shutil.copy2(Path(__file__).with_name('ProbeMaterialFixture.cs'), editor)
    if reviewed_materials:
        destination = project / 'Assets/Reviewed609'
        destination.mkdir()
        # New fixture material GUIDs avoid collisions; original texture pointers are retained.
        for path in reviewed_materials.glob('*.mat'):
            shutil.copy2(path, destination / path.name)
    else:
        (project / 'Assets/Reviewed609').mkdir()
    output = work / 'result.json'
    log = work / 'unity.log'
    environment = os.environ.copy()
    environment['GHVR_MATERIAL_FIXTURE_OUTPUT'] = str(output)
    command = [str(args.unity.resolve(strict=True)), '-batchmode', '-nographics', '-projectPath', str(project),
               '-executeMethod', 'ProbeMaterialFixture.Run', '-logFile', str(log)]
    try:
        completed = subprocess.run(command, env=environment, timeout=600)
    finally:
        if hashes(source) != source_hashes:
            raise RuntimeError('Read-only original native slice changed during fixture.')
        if reviewed_materials and hashes(reviewed_materials) != reviewed_hashes:
            raise RuntimeError('Read-only reviewed build materials changed during fixture.')
    if completed.returncode or not output.is_file():
        raise RuntimeError(f'Real Unity fixture failed, exit={completed.returncode}; inspect {log}')
    result = json.loads(output.read_text())
    if not result.get('passed') or result.get('unityVersion') != '2021.3.5f1':
        raise RuntimeError(f'Invalid fixture evidence: {output}')
    retry_output = work / 'retry-result.json'
    retry_log = work / 'retry-unity.log'
    environment['GHVR_MATERIAL_FIXTURE_RETRY_OUTPUT'] = str(retry_output)
    retry_command = [str(args.unity.resolve(strict=True)), '-batchmode', '-nographics', '-projectPath', str(project),
                     '-executeMethod', 'ProbeMaterialFixture.RetryExisting', '-logFile', str(retry_log)]
    try:
        retried = subprocess.run(retry_command, env=environment, timeout=600)
    finally:
        if hashes(source) != source_hashes or (reviewed_materials and hashes(reviewed_materials) != reviewed_hashes):
            raise RuntimeError('Read-only original fixture inputs changed during the fresh-process retry.')
    if retried.returncode or not retry_output.is_file() or not json.loads(retry_output.read_text()).get('passed'):
        raise RuntimeError(f'Real Unity fresh-process retry failed, exit={retried.returncode}; inspect {retry_log}')
    result['freshProcessRetry'] = json.loads(retry_output.read_text())
    result['retryLog'] = str(retry_log)
    result['sourceAssetsUnchanged'] = True
    result['reviewedMaterialsUnchanged'] = reviewed_materials is not None
    result['project'] = str(project)
    result['log'] = str(log)
    output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
