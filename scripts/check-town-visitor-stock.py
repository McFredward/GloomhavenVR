#!/usr/bin/env python3
"""Replay three independent original town-service presentation lanes in Unity 2021.3.5."""
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-visitor-stock')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--only-mutation', help='Run production and the named affected controls (comma-separated)')
    args = parser.parse_args()
    if not args.unity.is_file():
        parser.error('Real Unity 2021.3.5 is required; this rendered proof cannot silently skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('town_mirror_source_binding', ROOT / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loader)
    bound, hashes = loader.sources(args.source_root)
    helper = args.source_root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceTemplateAssets.cs'
    bound[helper.name] = helper.read_text()
    avatar = args.source_root / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.TownServices.cs'
    # Apply the real instance queue beside the shared static rig-hand adapter.
    # Bind its current motion state field when present, rather than dropping the
    # production main-thread fast-lane pass from this native three-lane proof.
    motion_signature = 'private readonly Dictionary<int, List<TownServiceMotionPacket>> _pendingTownMotion'
    motion_field = loader.expression(avatar.read_text(), motion_signature) + '\n' if motion_signature in avatar.read_text() else ''
    bound['VisitorQueue.cs'] = 'using System;\nusing System.Collections.Generic;\nusing GloomhavenVR.Net.TownServices;\nnamespace GloomhavenVR.Net;\ninternal sealed partial class NetAvatarDriver {\n' + '    private sealed class TownPacket { internal ulong Sequence; internal uint Session; internal byte Service; internal bool VisitorStock; internal byte[] Bytes = null!; }\n    private readonly Dictionary<int, Dictionary<uint, TownPacket>> _pendingTown = new();\n    private readonly Dictionary<int, List<TownPacket>> _pendingTownVoice = new();\n    internal bool QueueFixture(int peer, byte[] packet) => QueueTownService(peer, packet, packet.Length);\n    internal void ApplyFixture() => ApplyTownServices();\n    internal int QueuedFixture(int peer) => _pendingTown.TryGetValue(peer, out var queued) ? queued.Count : 0;\n    internal int QueuedVoiceFixture(int peer) => _pendingTownVoice.TryGetValue(peer, out var queued) ? queued.Count : 0;\n' + motion_field + loader.method(avatar.read_text(), 'private bool QueueTownService(int sender, byte[] bytes, int length)') + '\n' + loader.method(avatar.read_text(), 'private void ApplyTownServices()') + '\n}\n'
    fixture = run / 'fixture'
    shutil.copytree(ROOT / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(ROOT / 'scripts/town-visitor-stock-runtime/Program.cs', fixture / 'VisitorStockLanes.cs')
    program = fixture / 'Program.cs'
    text = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    branch = '''            if (suite == "visitor-stock")
            {
                var fronts = VisitorStockLanes();
                while (fronts.MoveNext()) yield return fronts.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;
            }
'''
    if anchor not in text:
        raise SystemExit('Fixture entry binding drift')
    program.write_text(text.replace(anchor, branch + anchor, 1))
    # Older common harness revisions did not need template traversal. Bind the unchanged
    # production Append implementation in its inert NativeTemplates seam, not a guessed path.
    publisher = fixture / 'Publisher.cs'
    if 'NativeTemplatePaths.cs' not in bound:
        text = publisher.read_text()
        append = loader.method((args.source_root / 'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.cs').read_text(),
                               'internal static string Append(string path, Transform child)')
        anchor = '        internal static bool IsBoundary(Transform source) => false;'
        if anchor not in text:
            raise SystemExit('Native-template fixture boundary binding drift')
        publisher.write_text(text.replace(anchor, anchor + '\n' + append, 1))
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('stock-voice-queue-alias', 'VisitorQueue.cs',
             'packet.Sequence == frame.Sequence && packet.Session == frame.Session\n                && packet.Service == frame.Service && packet.VisitorStock == frame.VisitorStock',
             'packet.Sequence == frame.Sequence',
             'actual avatar retains coincident private and stock voice ordinals'),
            ('stock-voice-source-bypass', 'TownServiceMirror.Stock.cs',
             '&& LiveStockItems(key);', '&& true;',
             'a stock census alone does not authorize a pickup cue'),
            ('stock-voice-source-flush-omitted', 'TownServiceMirror.cs',
             '\n        if (IsStockPeerKey(peer)) FlushStockVoicePending(RealPeer(peer));',
             '\n        if (IsStockPeerKey(peer)) { }',
             'matching original lifted membership releases the deferred stock cue using the real sender'),
            ('stock-voice-clears-private', 'TownServiceMirror.cs',
             'ClearLocalModules(); ClearLaneVoiceOutgoing(); _nextManifest = 0;',
             'ClearLocalModules(); ClearVoiceOutgoing(); _nextManifest = 0;',
             "starting a stock hold preserves the other NPC's queued private cue"),
            ('secondary-purse-inscriptions', 'TownServiceMirror.cs',
             '|| address.StartsWith("temple.row|", StringComparison.Ordinal);', '|| false;',
             'secondary visitor retains the original held-purse inscriptions'),
            ('stock-restore-warm-page', 'TownServiceMirror.Stock.cs',
             'shown &= module.Host.GetComponent<CanvasGroup>().alpha > .01f;',
             'shown &= true;',
             'retired stock return preserves the selected rack page and hidden warm originals'),
            ('reconnect-ancestry-lost', 'TownServiceMirror.cs',
             'ResetInteractionLeases(); ClearStockPeerKeys();',
             'ResetInteractionLeases(); ClearStockPeerKeys(); PrivateLane.Parents.Clear(); StockLane.Parents.Clear();',
             'network reconnect retains ancestry for unchanged original local bindings'),
            ('visitor-queue-alias', 'VisitorQueue.cs',
             ' | (frame.VisitorStock ? 262144u : 0u)', '',
             'actual avatar queue retains all three original presentation lanes'),
            ('shared-original-parents', 'TownServiceMirror.cs',
             'private static Dictionary<Transform, ParentLink> SourceParents => _local.Parents;',
             'private static Dictionary<Transform, ParentLink> SourceParents => PrivateLane.Parents;',
             'original module ancestry is scoped to each presentation lane'),
            ('stock-capture-omitted', 'TownServiceMirror.cs',
             'using (new LaneScope(StockLane)) { CaptureLane(send); CaptureStockVoice(send); }', '// visitor stock omitted',
             'original module ancestry is scoped to each presentation lane'),
            ('stock-dedup-omitted', 'TownServiceMirror.cs',
             '        SuppressRemoteStockDuplicates();', '        // duplicate shelf branch left visible',
             'observer suppresses the whole duplicate public shelf sample while it is held'),
            ('virtual-peer-frame', 'TownServiceMirror.cs',
             'sharedFrame(RealPeer(entry.Key))', 'sharedFrame(entry.Key)',
             'all mirror lanes resolve the real sender frame rather than virtual namespace IDs'),
            ('stock-membership-omitted', 'TownServiceMirror.Stock.cs',
             'StockMountIds.Add(frame.Module);', '{}',
             'only the live typed stock mount vacates its exact public item slot'),
        ]
    if args.only_mutation:
        requested = set(args.only_mutation.split(','))
        variants = [variant for variant in variants if variant[0] == 'production' or variant[0] in requested]
        if len(variants) != len(requested) + 1: parser.error('Unknown mutation: ' + args.only_mutation)
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}, indent=2) + '\n')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'suite': 'visitor-stock', 'cases': []}
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / 'production'
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if text.count(before) != 1:
                    raise SystemExit('Production mutation binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Mirror.csproj'
        shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'TownVisitorStock_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                                 '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
                                 '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
                                 '-p:UnityUi=' + str(args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
                                 '-p:UnityTmp=' + str(args.source_root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2))
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                             '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(manifest_path),
                             '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    report = Path(manifest['result'])
    if report.is_file():
        print(report.read_text(), end='')
    if result.returncode or not report.is_file():
        raise SystemExit('FAIL Unity run; see ' + str(run / 'unity.log'))
    for case in manifest['cases']:
        counts = run / (case['name'] + '-evidence/assertions.txt')
        if counts.is_file():
            print(case['name'] + ': ' + counts.read_text().strip())
    # Keep rendered PNGs, source snapshots/hashes and logs, not large Unity/compiler caches.
    shutil.rmtree(project)
    for name, *_ in variants:
        for output in ('bin', 'obj'):
            shutil.rmtree(run / name / output, ignore_errors=True)
    print(f'PASS: {len(variants)} complete production/negative variants; evidence: {run}')


if __name__ == '__main__':
    main()
