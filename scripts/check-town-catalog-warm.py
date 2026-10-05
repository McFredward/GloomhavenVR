#!/usr/bin/env python3
"""Run the narrow original-bank preparation, reference-clock and repair proof in Unity."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-catalog-warm')
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('mirror_source_binding', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    avatar = (root / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.TownServices.cs').read_text()
    bound['WarmQueue.cs'] = '''using System; using System.Collections.Generic; using GloomhavenVR.Net.TownServices;
namespace GloomhavenVR.Net; internal sealed partial class NetAvatarDriver {
private sealed class TownPacket { internal ulong Sequence; internal uint Session; internal byte Service; internal bool VisitorStock; internal byte[] Bytes = null!; }
private readonly Dictionary<int, Dictionary<uint, TownPacket>> _pendingTown = new();
private readonly Dictionary<int, List<TownPacket>> _pendingTownVoice = new();
internal bool QueueFixture(int peer, byte[] bytes) => QueueTownService(peer, bytes, bytes.Length);
internal void ApplyFixture() => ApplyTownServices();
internal int QueuedFixture(int peer) => _pendingTown.TryGetValue(peer, out var packets) ? packets.Count : 0;
''' + loader.expression(avatar, 'private readonly Dictionary<int, List<TownServiceMotionPacket>> _pendingTownMotion') + '\n' + loader.method(avatar, 'private bool QueueTownService(int sender, byte[] bytes, int length)') + '\n' + loader.method(avatar, 'private void ApplyTownServices()') + '\n}\n'
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(root / 'scripts/town-catalog-warm-runtime/CatalogWarm.cs', fixture / 'CatalogWarm.cs')
    program = fixture / 'Program.cs'
    anchor = '            DelayedCensusRace();'
    branch = '''            if (suite == "catalog-warm") {
                var warm = CatalogWarmClock(); while (warm.MoveNext()) yield return warm.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
'''
    text = program.read_text()
    if text.count(anchor) != 1: raise RuntimeError('Fixture entry binding drift')
    program.write_text(text.replace(anchor, branch + anchor, 1))
    variants = [('production', None, None, None, '')]
    if not args.no_negative_controls:
        variants += [
            ('native-toggle-patch', 'TownCatalogClock.cs', 'headers[i] = TownServiceDelta.Create(reauthorized, current);', 'headers[i] = TownServiceDelta.Retain(current); headers[i].Nodes = Array.Empty<TownServiceNode>(); headers[i].BaseSequence = previous.Sequence;', 'actual captured Canvas and mesh exposure changes produce genuine original property patches'),
            ('queue-completeness', 'WarmQueue.cs', '| (frame.CatalogBank?.Updates.Length == 0 ? 524288u : 0u)', '', 'main-thread coalescing retains rejected references and complete repair with the same source sequence'),
            ('peer-cache-clear', 'TownServiceMirror.CatalogBank.cs', 'CatalogOriginalBanks.Remove(peer);', '// cache clear disabled', 'departed public sender clears all dormant native revisions'),
        ]
    manifest = {'suite': 'catalog-warm', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if text.count(before) != 1: raise RuntimeError('Mutation binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Mirror.csproj'; shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'TownCatalogWarm_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        print('Compiled ' + name, flush=True)
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}, indent=2) + '\n')
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True)
    shutil.copytree(root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices', project / 'Assets/Resources/TownServices')
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2))
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=300)
    report = Path(manifest['result'])
    if report.exists(): print(report.read_text(), end='')
    if result.returncode or not report.exists(): raise SystemExit('FAIL warm catalogue proof; see ' + str(run / 'unity.log'))
    print('PASS warm catalogue source/runtime; evidence: ' + str(run))


if __name__ == '__main__': main()
