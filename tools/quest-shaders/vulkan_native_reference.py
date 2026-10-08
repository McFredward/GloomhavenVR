"""Bounded original D3D versus recovered Vulkan shadow/instance pixel evidence.

Original bank bytes and readbacks are immutable inputs to the candidate run.
This host proof makes no Android, complete material, bone or headset claim.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess

from manifest import ValidationError, sha256
from run import _private_output

MODES = {
    'shadow': dict(argument='--quest-shadow-config', receipt='shadow-depth.json',
        configuration='shadow-config.json', channels=1,
        ids=('baseline', 'increased-shadow-bias', 'alpha-clip-negative')),
    'instance-nan': dict(argument='--quest-instance-nan-config', receipt='instance-nan.json',
        configuration='instance-config.json', channels=4,
        ids=('baseline', 'fractional', 'one', 'negative', 'positive-infinity', 'negative-infinity', 'nan')),
}


def validate_receipt(mode, receipt, root, width, backend):
    definition = MODES[mode]
    if receipt.get('unityVersion') != '2021.3.5f1' or receipt.get('backend') != backend or receipt.get('headsetPictureVerified') is not False:
        raise ValidationError('Bounded native receipt uses the wrong version/backend or claims a headset picture.')
    if receipt.get('originalD3DReadbacksCompared') is not (backend == 'Vulkan'):
        raise ValidationError('Bounded native receipt misstates the original readback comparison.')
    if mode == 'shadow' and receipt.get('actualOriginalNativeShadowPass') is not (backend == 'Direct3D11'):
        raise ValidationError('The recovered shadow candidate is not the original native oracle.')
    if mode == 'instance-nan' and receipt.get('actualDrawMeshInstanced') is not True:
        raise ValidationError('The native instance fixture did not execute an actual instanced draw.')
    if mode == 'instance-nan' and receipt.get('hardwareTier') != 1:
        raise ValidationError('The native instance fixture did not execute the formerly failing original tier2 bank.')
    rows = receipt.get('cases', [])
    if [row.get('id') for row in rows] != list(definition['ids']) or not all(row.get('passed') is True for row in rows):
        raise ValidationError('Bounded native case coverage is incomplete or failed.')
    hashes = {}
    for row in rows:
        name = row['id'] + '.f32'
        path = root / name
        if path.stat().st_size != width * width * definition['channels'] * 4:
            raise ValidationError('Bounded native float readback extent differs from its configuration.')
        hashes[name] = sha256(path)
    return hashes


def _player_inputs(executable, original, candidate):
    inputs = {str(path.resolve()): sha256(path) for path in (executable, original, candidate)}
    stem = executable.stem if executable.suffix.lower() == '.exe' else executable.name
    assembly = executable.parent / (stem + '_Data/Managed/Assembly-CSharp.dll')
    engine = executable.parent / ('UnityPlayer.dll' if executable.suffix.lower() == '.exe' else 'UnityPlayer.so')
    if not assembly.is_file() or not engine.is_file():
        raise ValidationError('Actual native fixture player implementation is missing.')
    inputs.update({str(path.resolve()): sha256(path) for path in (assembly, engine)})
    return inputs


def _launch(executable, definition, config, output, backend, icd=None, wine=None, wine_prefix=None):
    command = [str(executable), '-batchmode', '-screen-fullscreen', '0', '-logFile', str(output / 'player.log')]
    environment = dict(os.environ)
    if backend == 'Vulkan':
        command += ['-force-vulkan', '-force-device-index', '0']
        if icd is None or not icd.is_file(): raise ValidationError('Actual Vulkan fixture requires its ICD input.')
        environment['VK_ICD_FILENAMES'] = str(icd)
    else:
        command += ['-force-d3d11']
        if os.name != 'nt':
            if wine is None or not wine.is_file() or wine_prefix is None:
                raise ValidationError('Original Windows/D3D11 fixture requires a separate Wine prefix on this host.')
            environment.update(WINEPREFIX=str(wine_prefix.resolve()), WINEDEBUG='-all')
            command = [str(wine), *command]
    command += [definition['argument'], str(config)]
    if os.name != 'nt' and not environment.get('DISPLAY'):
        launcher = shutil.which('xvfb-run')
        if launcher is None: raise ValidationError('Native pixel fixtures require a graphics display.')
        command = [launcher, '-a', *command]
    result = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=1800)
    (output / 'console.log').write_text(result.stdout + result.stderr)
    if result.returncode: raise ValidationError('Native pixel fixture failed: ' + str(output / 'player.log'))
    receipt = json.loads((output / definition['receipt']).read_text())
    return receipt


def capture_original(mode, executable, original, auxiliary, output, wine=None, wine_prefix=None):
    definition = MODES[mode]
    executable, original, auxiliary = [p.resolve() for p in (executable, original, auxiliary)]
    before = _player_inputs(executable, original, auxiliary)
    output = _private_output(output)
    configuration = dict(originalBundle=str(original), candidateBundle=str(auxiliary), output=str(output),
                         originalReadbacks='', vulkan=False, width=128)
    if mode == 'shadow':
        configuration.update(shaderAddress='Assets/Content/Characters/Common/Shaders/Amp_CharShader.shader', **{'pass': 3})
    config = output / definition['configuration']; config.write_text(json.dumps(configuration, indent=2) + '\n')
    receipt = _launch(executable, definition, config, output, 'Direct3D11', wine=wine, wine_prefix=wine_prefix)
    hashes = validate_receipt(mode, receipt, output, configuration['width'], 'Direct3D11')
    if any(sha256(Path(path)) != value for path, value in before.items()):
        raise ValidationError('Original native shader/host inputs changed during capture.')
    provenance = dict(schema=1, mode=mode, backend='Direct3D11', inputs=before, originalReadbacks=hashes,
        configurationSha256=sha256(config), receiptSha256=sha256(output / definition['receipt']),
        harnessSha256=sha256(Path(__file__)))
    (output / 'native-inputs.json').write_text(json.dumps(provenance, indent=2) + '\n')
    return receipt


def original_oracle(mode, reference):
    definition = MODES[mode]
    proof = json.loads((reference / 'native-inputs.json').read_text())
    config_path, receipt_path = reference / definition['configuration'], reference / definition['receipt']
    config, receipt = json.loads(config_path.read_text()), json.loads(receipt_path.read_text())
    if proof.get('schema') != 1 or proof.get('mode') != mode or proof.get('backend') != 'Direct3D11' or config.get('vulkan') is not False:
        raise ValidationError('Recovered Vulkan evidence cannot become the original D3D oracle.')
    if proof.get('configurationSha256') != sha256(config_path) or proof.get('receiptSha256') != sha256(receipt_path):
        raise ValidationError('Original native receipt/configuration changed.')
    hashes = validate_receipt(mode, receipt, reference, config['width'], 'Direct3D11')
    if hashes != proof.get('originalReadbacks') or not proof.get('inputs') or any(sha256(Path(path)) != value for path, value in proof['inputs'].items()):
        raise ValidationError('Original native input bytes/readbacks changed.')
    return config, proof


def compare_vulkan(mode, executable, candidate, reference, output, icd):
    definition = MODES[mode]
    executable, candidate, reference, icd = [p.resolve() for p in (executable, candidate, reference, icd)]
    original, proof = original_oracle(mode, reference)
    output = _private_output(output)
    if output == reference: raise ValidationError('Recovered evidence must not overwrite the original oracle.')
    before = _player_inputs(executable, Path(original['originalBundle']), candidate)
    configuration = dict(original, candidateBundle=str(candidate), output=str(output), originalReadbacks=str(reference), vulkan=True)
    config = output / definition['configuration']; config.write_text(json.dumps(configuration, indent=2) + '\n')
    receipt = _launch(executable, definition, config, output, 'Vulkan', icd)
    hashes = validate_receipt(mode, receipt, output, configuration['width'], 'Vulkan')
    original_oracle(mode, reference)
    if any(sha256(Path(path)) != value for path, value in before.items()):
        raise ValidationError('Recovered native shader/host inputs changed during comparison.')
    provenance = dict(schema=1, mode=mode, backend='Vulkan', inputs=before, originalOracleSha256=sha256(reference / 'native-inputs.json'),
        originalReadbacks=proof['originalReadbacks'], candidateReadbacks=hashes, icdSha256=sha256(icd),
        configurationSha256=sha256(config), receiptSha256=sha256(output / definition['receipt']),
        harnessSha256=sha256(Path(__file__)))
    (output / 'native-inputs.json').write_text(json.dumps(provenance, indent=2) + '\n')
    return receipt


def negative_controls(mode, executable, candidate, reference, output, icd):
    """Plant one corrupt extent and one wrong pixel in private readback copies.

    The authoritative original oracle remains unchanged. The wrong-pixel case
    bypasses only the Python checksum preflight to exercise the real renderer's
    float comparison; it never emits a replacement original provenance record.
    """
    definition = MODES[mode]
    executable, candidate, reference, icd = [p.resolve() for p in (executable, candidate, reference, icd)]
    original, proof = original_oracle(mode, reference)
    output = _private_output(output)
    if output == reference: raise ValidationError('Negative fixtures cannot overwrite the original oracle.')
    before = _player_inputs(executable, Path(original['originalBundle']), candidate)
    results = []
    for defect in ('readback-extent', 'readback-pixel'):
        directory = output / defect; directory.mkdir(parents=True, exist_ok=True)
        planted = directory / 'planted-readbacks'; planted.mkdir(exist_ok=True)
        for name in proof['originalReadbacks']: shutil.copy2(reference / name, planted / name)
        baseline = planted / (definition['ids'][0] + '.f32')
        if defect == 'readback-extent': baseline.write_bytes(baseline.read_bytes()[:-4])
        else:
            data = bytearray(baseline.read_bytes())
            values = struct.unpack('<' + 'f' * (len(data) // 4), data)
            index = next((index for index, value in enumerate(values) if .00001 < value < 100), None)
            if index is None: raise ValidationError('The native baseline has no finite foreground pixel to test.')
            struct.pack_into('<f', data, index * 4, values[index] + .125)
            baseline.write_bytes(data)
        configuration = dict(original, candidateBundle=str(candidate), output=str(directory), originalReadbacks=str(planted), vulkan=True)
        config = directory / definition['configuration']; config.write_text(json.dumps(configuration, indent=2) + '\n')
        try: _launch(executable, definition, config, directory, 'Vulkan', icd)
        except ValidationError:
            log = (directory / 'player.log').read_text()
            expected = ('extent differs' if defect == 'readback-extent' else
                        ('Native shadow/depth witness failed' if mode == 'shadow' else 'Native instanced NaN witness failed'))
            if expected not in log: raise ValidationError('Negative pixel fixture failed for an unrelated reason.')
        else: raise ValidationError('Actual native renderer accepted the planted readback defect.')
        results.append(dict(defect=defect, rejected=True, actualNativeRendererExecuted=True, plantedReadbackSha256=sha256(baseline),
                            configurationSha256=sha256(config), playerLogSha256=sha256(directory / 'player.log')))
    original_oracle(mode, reference)
    if any(sha256(Path(path)) != value for path, value in before.items()):
        raise ValidationError('Native shader/host inputs changed during negative controls.')
    result = dict(schema=1, mode=mode, controls=results, originalOracleSha256=sha256(reference / 'native-inputs.json'),
                  headsetPictureVerified=False, harnessSha256=sha256(Path(__file__)))
    (output / 'negative-controls.json').write_text(json.dumps(result, indent=2) + '\n')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=MODES, required=True)
    commands = parser.add_subparsers(dest='command', required=True)
    native = commands.add_parser('original')
    for name in ('executable', 'original', 'auxiliary', 'output'): native.add_argument('--' + name, type=Path, required=True)
    native.add_argument('--wine', type=Path); native.add_argument('--wine-prefix', type=Path)
    candidate = commands.add_parser('vulkan')
    for name in ('executable', 'candidate', 'reference', 'output', 'icd'): candidate.add_argument('--' + name, type=Path, required=True)
    negative = commands.add_parser('negative')
    for name in ('executable', 'candidate', 'reference', 'output', 'icd'): negative.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'original':
        result = capture_original(args.mode, args.executable, args.original, args.auxiliary, args.output, args.wine, args.wine_prefix)
    elif args.command == 'vulkan': result = compare_vulkan(args.mode, args.executable, args.candidate, args.reference, args.output, args.icd)
    else: result = negative_controls(args.mode, args.executable, args.candidate, args.reference, args.output, args.icd)
    print('Actual bounded native cases:', len(result['cases'] if 'cases' in result else result['controls']))


if __name__ == '__main__': main()
