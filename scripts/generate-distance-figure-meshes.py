#!/usr/bin/env python3
"""Append far-distance and face-preserving NPC meshes without rebuilding existing banks."""
import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--skip-extract', action='store_true')
    parser.add_argument('--only', nargs='*')
    parser.add_argument('--npc-only', action='store_true')
    parser.add_argument('--no-package', action='store_true')
    args = parser.parse_args()
    if (args.only or args.npc_only) and not args.no_package:
        parser.error("Focused extraction is validation-only; pass --no-package to preserve existing part identities")
    root = args.source_root.resolve()
    out = (args.output_dir or root / '.planning/debug/frame612-distance-mesh').resolve()
    out.mkdir(parents=True, exist_ok=True)
    if not args.skip_extract:
        command = [str(Path.home() / 'unitypy-venv/bin/python'), str(root / 'tools/figure-mesh/export-native.py'),
                   '--source-root', str(root), '--output-dir', str(out / 'native')]
        if args.only: command.extend(['--only'] + args.only)
        subprocess.run(command, check=True)
    subprocess.run([str(Path.home() / 'unitypy-venv/bin/python'), str(root / 'tools/figure-mesh/export-town.py'),
                    '--bundle', str(root / 'prebuilt/ghvr-town.bundle'), '--output-dir', str(out / 'native')], check=True)
    boundary = out / 'Boundaries.cs'
    boundary.write_text('namespace GloomhavenVR.Core { internal static class VRLog { internal static bool WantsDebug => false; internal static void Debug(string a,string b) {} internal static void Note(string a,string b) {} } }\n')
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    subprocess.run([str(Path.home() / '.dotnet/dotnet'), 'build', str(root / 'tools/figure-mesh/Generator.csproj'),
                    '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                    '-p:GeneratorSource=' + str(root / 'tools/figure-mesh'),
                    '-p:RuntimeSource=' + str(root / 'src/GloomhavenVR/Core/Perf'),
                    '-p:GeneratedBoundaries=' + str(boundary), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
                    '-o', str(out / 'generator')], check=True)
    project = root / 'unity/GloomhavenVR.FigureMeshes'
    (project / 'Assets/Editor').mkdir(parents=True, exist_ok=True)
    shutil.copyfile(out / 'generator/Generator.dll', project / 'Assets/Editor/Generator.dll')
    command = [str(unity), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod',
                    'FigureDistanceMeshGenerator.Build', '-nativeMeshInput', str(out / 'native'),
                    '-nativeMeshOutput', str(out / 'bank'), '-townMeshBundle', str(root / 'prebuilt/ghvr-town.bundle'),
                    '-logFile', str(out / 'unity.log')]
    if args.npc_only: command.append('-onlyNpcMeshes')
    subprocess.run(command, check=True, timeout=7200)
    if args.no_package: return
    index_path = root / 'prebuilt/ghvr-figure-meshes-index.json'
    index = json.loads(index_path.read_text())
    additions = json.loads((out / 'bank/index.json').read_text())['entries']
    # One complete append generation owns its numbered distance parts. Never keep
    # obsolete references pointing into a rebuilt part with different membership.
    index['entries'] = [entry for entry in index['entries'] if not entry['bank'].startswith('ghvr-figure-meshes-distance-')] + additions
    index_path.write_text('{\"entries\":[' + ',\n'.join(json.dumps(entry, separators=(',', ':')) for entry in index['entries']) + ']}\n')
    required = {entry['bank'] for entry in additions}
    banks = sorted(bank for bank in (out / 'bank').glob('ghvr-figure-meshes-distance-*.bundle') if bank.name in required)
    referenced = {entry['bank'] for entry in index['entries']}
    for old in (root / 'prebuilt').glob('ghvr-figure-meshes-distance-*.bundle'):
        if old.name not in referenced: old.unlink()
    for bank in banks: shutil.copyfile(bank, root / 'prebuilt' / bank.name)
    report = {'format': 1, 'editor': '2021.3.5f1', 'original_town_bundle_sha256': hashlib.sha256((root / 'prebuilt/ghvr-town.bundle').read_bytes()).hexdigest(),
              'banks': {bank.name: hashlib.sha256(bank.read_bytes()).hexdigest() for bank in banks},
              'meshes': json.loads((out / 'bank/results.json').read_text())}
    (root / 'tools/figure-mesh/distance-manifest.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'derivatives': len(report['meshes']), 'parts': len(banks), 'bytes': sum(bank.stat().st_size for bank in banks)}))

if __name__ == '__main__': main()
