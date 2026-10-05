"""Compare an actual Vulkan host with captured original Windows/D3D11 pixels.

The reference must be a passing original-native run with unchanged bundle
provenance. This bounded fixture does not certify bone animation, a complete
Campaign material census, Android Vulkan execution, or a headset picture.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

from manifest import ValidationError, sha256
from run import _private_output, validate_pixels


def original_oracle(root: Path):
    config = json.loads((root / 'reference-config.json').read_text())
    receipt = json.loads((root / 'windows-pixels.json').read_text())
    inputs = json.loads((root / 'native-inputs.json').read_text())
    validate_pixels({'renderCases': config['cases']}, config['sourceManifestSha256'], receipt, root)
    bundles = inputs.get('originalBundles')
    if not bundles or any(not Path(path).is_file() or sha256(Path(path)) != value for path, value in bundles.items()):
        raise ValidationError('Original native D3D bundle provenance is missing or changed.')
    if config.get('vulkanCandidate'):
        raise ValidationError('A recovered candidate cannot become the original D3D oracle.')
    hashes = {}
    size = config['width'] * config['height'] * 4
    for row in receipt['pictures']:
        path = root / row['referenceFile']
        if path.stat().st_size != size:
            raise ValidationError('Original D3D readback dimensions differ from its actual configuration.')
        hashes[row['referenceFile']] = sha256(path)
    return config, hashes, bundles


def run(executable: Path, candidate: Path, reference: Path, output: Path, icd: Path, device_index=0):
    executable, candidate, reference, icd = [path.resolve() for path in (executable, candidate, reference, icd)]
    original, hashes, bundles = original_oracle(reference)
    output = _private_output(output)
    if output == reference:
        raise ValidationError('Vulkan candidate evidence cannot overwrite its original D3D oracle.')
    configuration = dict(original, vulkanCandidate=True, originalReadbackRoot=str(reference),
                         outputRoot=str(output), candidateBundlePath=str(candidate))
    config = output / 'reference-config.json'
    config.write_text(json.dumps(configuration, indent=2) + '\n')
    command = [str(executable), '-batchmode', '-force-vulkan', '-force-device-index', str(device_index),
               '-screen-fullscreen', '0', '-logFile', str(output / 'vulkan-player.log'),
               '--quest-shader-config', str(config)]
    environment = dict(os.environ, VK_ICD_FILENAMES=str(icd))
    if not environment.get('DISPLAY'):
        launcher = shutil.which('xvfb-run')
        if launcher is None: raise ValidationError('The native Unity Vulkan renderer requires an X display.')
        command = [launcher, '-a', *command]
    before = {str(executable): sha256(executable), str(candidate): sha256(candidate)}
    actual = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=1800)
    (output / 'vulkan-console.log').write_text(actual.stdout + actual.stderr)
    if actual.returncode:
        raise ValidationError('Actual Vulkan pixel renderer failed: ' + str(output / 'vulkan-player.log'))
    receipt = json.loads((output / 'vulkan-pixels.json').read_text())
    validate_pixels({'renderCases': original['cases']}, original['sourceManifestSha256'], receipt, output, 'Vulkan')
    if any(sha256(reference / name) != value for name, value in hashes.items()) or any(sha256(Path(path)) != value for path, value in {**bundles, **before}.items()):
        raise ValidationError('Original or candidate input bytes changed during the Vulkan pixel run.')
    (output / 'native-inputs.json').write_text(json.dumps(dict(originalBundles=bundles, originalReadbacks=hashes,
        originalReceiptSha256=sha256(reference / 'windows-pixels.json'), originalConfigSha256=sha256(reference / 'reference-config.json'),
        executableSha256=before[str(executable)], candidateBundleSha256=before[str(candidate)], icdSha256=sha256(icd),
        harnessSha256=sha256(Path(__file__))), indent=2) + '\n')
    return receipt


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ('executable', 'candidate', 'reference', 'output', 'icd'):
        p.add_argument('--' + name, type=Path, required=True)
    p.add_argument('--device-index', type=int, default=0)
    a = p.parse_args()
    print('Actual Vulkan pixel pictures:', len(run(a.executable, a.candidate, a.reference, a.output, a.icd, a.device_index)['pictures']))


if __name__ == '__main__': main()
