"""Prepare a private exact-version Unity compiler project from shader evidence."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil

from manifest import ValidationError, asset_path, sha256


def prepare(overlay, original_project, output):
    overlay, original_project, output = map(lambda p: Path(p).resolve(), (overlay, original_project, output))
    if output == overlay or overlay in output.parents or output in overlay.parents or output == original_project or original_project in output.parents:
        raise ValidationError('Compiler bootstrap requires a private disjoint project.')
    path = overlay / 'QuestRecovery/campaign-shaders.json'
    manifest = json.loads(path.read_text())
    if manifest.get('scope') != 'campaign-compiler':
        raise ValidationError('Compiler bootstrap requires its independent native bank coverage manifest.')
    output.mkdir(parents=True, exist_ok=True)
    for source in (overlay / 'Assets').rglob('*'):
        if source.is_file():
            relative = source.relative_to(overlay)
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    for material in manifest['materials']:
        relative = Path(asset_path(material['assetPath']))
        source = original_project / relative
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        shutil.copy2(source.with_name(source.name + '.meta'), target.with_name(target.name + '.meta'))
    gate = Path(__file__).resolve().parents[2] / 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignShaderValidation.cs'
    for helper in ("QuestVulkanShaderValidation.cs", "QuestSmolvDecoder.cs"):
        extra = gate.with_name(helper)
        destination = output / "Assets/Quest/Editor" / helper
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(extra, destination)
    destination = output / 'Assets/Quest/Editor/QuestCampaignShaderValidation.cs'
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(gate, destination)
    for directory in ('Packages', 'ProjectSettings', 'QuestRecovery'):
        (output / directory).mkdir(exist_ok=True)
    shutil.copy2(path, output / 'QuestRecovery/campaign-shaders.json')
    (output / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (output / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {
        'com.unity.modules.assetbundle': '1.0.0', 'com.unity.modules.jsonserialize': '1.0.0'}}) + '\n')
    (output / 'QuestRecovery/compiler-bootstrap.json').write_text(json.dumps({'schema': 1, 'shaderCount': len(manifest['shaders']),
        'materialCount': len(manifest['materials']), 'scope': 'compiler-only', 'manifestSha256': sha256(path),
        'gateSha256': sha256(gate), 'pixelParityVerified': False, 'headsetPictureVerified': False}, indent=2) + '\n')
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--overlay', type=Path, required=True)
    parser.add_argument('--original-project', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(prepare(args.overlay, args.original_project, args.output))


if __name__ == '__main__':
    main()
