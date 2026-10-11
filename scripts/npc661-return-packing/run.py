#!/usr/bin/env python3
"""Exact full-root return packing, bounded original-byte decode and causal controls."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc661-return-packing')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    receipts = {}
    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')

    def native(source, output, expected=''):
        result = subprocess.run(['python3', str(root / 'scripts/npc658-flight-runtime/run.py'),
            '--source-root', str(source), '--output-dir', str(output), '--case', 'capacity', '--no-negative-controls'],
            capture_output=True, text=True)
        (run / (output.name + '.log')).write_text(result.stdout + result.stderr)
        evidence = sorted(output.glob('run-*'))[-1]
        actual = (evidence / 'results.txt').read_text() if (evidence / 'results.txt').exists() else ''
        if expected:
            if result.returncode == 0 or expected not in actual:
                raise RuntimeError('Expected named engine control did not fail: ' + str(evidence))
        elif result.returncode:
            raise RuntimeError('Current native Capacity/Mixed failed: ' + str(evidence))
        receipts[output.name] = {'evidence': str(evidence), 'expectedEngineFailure': expected, 'result': actual}
        return evidence

    positive = native(root, run / 'current-native')
    manifest = json.loads((positive / 'manifest.json').read_text())
    dll = Path(manifest['cases'][0]['dll'])
    byte_project = run / 'byte-proof'
    byte_project.mkdir()
    for name in ('ByteProof.cs', 'ByteProof.csproj'):
        shutil.copyfile(HERE / name, byte_project / name)
    bound = byte_project / 'source'; bound.mkdir()
    source_hashes = {}
    for name in ('PresentationCompression.cs', 'NetPacket.cs'):
        path = root / 'src/GloomhavenVR/Net' / name
        shutil.copyfile(path, bound / name)
        source_hashes[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    protocol = root / 'src/GloomhavenVR/Net/NetProtocol.cs'
    raw_protocol = protocol.read_text()
    constants = []
    for name in ('Magic', 'Version', 'MsgRig', 'MsgExtras'):
        match = re.search(r'public const (uint|byte) ' + name + r' = ([^;]+);', raw_protocol)
        if match is None:
            raise RuntimeError('Protocol boundary source drift: ' + name)
        constants.append('internal const ' + match[1] + ' ' + name + ' = ' + match[2] + ';')
    (bound / 'NetProtocol.cs').write_text('namespace GloomhavenVR.Net; internal static class NetProtocol { '
        + ' '.join(constants) + ' }\n')
    source_hashes['NetProtocol.cs (full source; exact scalar ports)'] = hashlib.sha256(protocol.read_bytes()).hexdigest()
    packets = run / 'real-raw'; packets.mkdir()
    literal = json.loads((HERE / 'real-packets.json').read_text())
    for packet in literal['packets']:
        payload = base64.b64decode(packet['base64'], validate=True)
        if hashlib.sha256(payload).hexdigest() != packet['sha256']:
            raise RuntimeError('Actual raw literal hash mismatch')
        (packets / packet['file']).write_bytes(payload)
    shutil.copyfile(HERE / 'goldens.json', packets / 'goldens.json')

    # The old codec is source-exact, not a stub or a rewritten size policy.
    old_source = run / 'old-codec'; old_source.mkdir()
    for path in (positive / 'capacity/production').glob('*.cs'):
        shutil.copyfile(path, old_source / path.name)
    historical = subprocess.check_output(['git', 'show',
        '6e5c93b4d:src/GloomhavenVR/Net/TownServices/TownServiceMotionCodec.cs'], cwd=root, text=True)
    (old_source / 'TownServiceMotionCodec.cs').write_text(historical)
    old_project = run / 'old-codec.csproj'
    shutil.copyfile(root / 'scripts/town-service-mirror-runtime/Mirror.csproj', old_project)
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    build = subprocess.run([dotnet, 'build', str(old_project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
        '-p:CaseName=ReturnPacking661Old', '-p:FixtureDir=' + str(positive / 'fixture'),
        '-p:ProductionDir=' + str(old_source), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
        '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
        '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
    (run / 'old-codec-build.log').write_text(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError('Historical size control must compile: ' + str(run / 'old-codec-build.log'))
    old_dll = run / 'bin/Release/netstandard2.1/ReturnPacking661Old.dll'
    byte_result = subprocess.run([dotnet, 'run', '--project', str(byte_project / 'ByteProof.csproj'), '--',
        str(dll), str(packets), str(run / 'byte-readbacks'), str(old_dll)], capture_output=True, text=True)
    (run / 'current-byte.log').write_text(byte_result.stdout + byte_result.stderr)
    if byte_result.returncode:
        raise RuntimeError('Actual current-source byte proof failed: ' + str(run / 'current-byte.log'))

    old_result = subprocess.run([dotnet, 'run', '--no-build', '--project', str(byte_project / 'ByteProof.csproj'), '--',
        str(old_dll), str(packets), str(run / 'old-byte-readbacks')], capture_output=True, text=True)
    (run / 'old-codec-byte.log').write_text(old_result.stdout + old_result.stderr)
    marker = 'Real eight-part full native root recipe fits unchanged864-byte event'
    if old_result.returncode == 0 or marker not in old_result.stdout + old_result.stderr:
        raise RuntimeError('Historical unchanged864-byte control must fail at its named assertion')
    receipts['old-codec-size'] = {'revision': '6e5c93b4d', 'sha256': hashlib.sha256(historical.encode()).hexdigest(),
        'expectedFailure': marker}

    # Preserve every real field and all original native deadline/fairness assertions.
    # Revert only the known-fit-core floor to prove the exact rollback starvation.
    control = run / 'old-floor-source'
    shutil.copytree(root / 'src', control / 'src')
    for name in ('scripts', 'ressources', 'unity'):
        (control / name).symlink_to(root / name, target_is_directory=True)
    budget_path = control / 'src/GloomhavenVR/Net/TownServices/TownServiceMotionBudget.cs'
    budget = budget_path.read_text()
    needle = ('            int floor = selected.Count > guaranteedCore ? guaranteedCore : 0;\n'
        '            int keep = Math.Max(floor, selected.Count * 3 / 4);\n'
        '            while (keep > floor && keep < selected.Count')
    if budget.count(needle) != 1:
        raise RuntimeError('Exact old-floor control binding drift')
    budget = budget.replace(needle, '            int keep = Math.Max(0, selected.Count * 3 / 4);\n'
        '            while (keep > 0 && keep < selected.Count', 1)
    budget = budget.replace('        int guaranteedCore = selected.Count;\n', '', 1)
    budget_path.write_text(budget)
    native(control, run / 'old-floor-native',
        'all large-cohort children complete by .267s within actual remaining .35-.07=.28s native lifetime')
    receipts['scalarSourcePorts'] = source_hashes
    receipts['harness'] = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in HERE.iterdir() if path.is_file()}
    (run / 'receipts.json').write_text(json.dumps(receipts, indent=2) + '\n')
    print('Evidence: ' + str(run))
    print(byte_result.stdout.strip())
    print('PASS native Capacity/Mixed and named old-codec/old-core-floor controls')


if __name__ == '__main__':
    main()
