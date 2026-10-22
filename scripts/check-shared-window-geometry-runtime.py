#!/usr/bin/env python3
"""Run original modal pose/scale writers against Unity's actual world hierarchy."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def block(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def execute(command, log, timeout=240):
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/shared-window-geometry-runtime')
    parser.add_argument('--case', action='append', help='Partial development run; repeat to choose cases')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir)).resolve()
    status = 1
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    try:
        paths = {
            'grab': 'src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs',
            'law': 'src/GloomhavenVR/WorldUI/Modal/SharedWindowSizeLaw.cs',
            'size': 'src/GloomhavenVR/WorldUI/Modal/SharedWindowSize.cs',
            'convert': 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs',
            'protocol': 'src/GloomhavenVR/Net/NetProtocol.cs',
            'spawn': 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.9.Spawn.cs',
            'panel': 'src/GloomhavenVR/WorldUI/Conversion/ConvertedPanel.cs',
            'video': 'src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoWindow.cs',
            'bridge': 'src/GloomhavenVR/WorldUI/Modal/SharedWindowSpawnBridge.cs',
            'host': 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.SharedWindowSpawn.cs',
            'lifecycle': 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs',
            'reward': 'src/GloomhavenVR/WorldUI/Modal/RewardShowcasePlacement.cs',
            'core': 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs',
            'placement': 'src/GloomhavenVR/WorldUI/Conversion/PanelPlacement.cs',
            'facing': 'src/GloomhavenVR/WorldUI/HeadFacing.cs',
            'video_placement': 'src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoWindow.SharedPlacement.cs',
        }
        original = {name: (root / path).read_text() for name, path in paths.items()}
        hashes = {paths[name]: hashlib.sha256(code.encode()).hexdigest() for name, code in original.items()}
        fixture = ROOT / 'scripts/shared-window-geometry-runtime'
        for path in [Path(__file__), *fixture.glob('*.cs')]:
            hashes[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
        # Pin the real initial placement and identity producers, not just the consumers.
        assert 'panel.SharedDesignKind = SharedWindows.KindOf(window);' in original['size']
        assert '_panel.SharedDesignKind = kind;' in original['grab']
        assert 'SharedDesignKind = SharedWindowKind.Video,' in original['video']
        assert 'ComputeInitialSpawn(head, out Vector3 position, out Quaternion rotation);' in original['video']
        place = block(original['spawn'], 'private static bool PlaceAtHmd(')
        assert place.count('SharedWindowSize.RenderWorldScale(') == 2
        assert 'public SharedWindowKind SharedDesignKind;' in original['panel']
        compute = block(original['spawn'], 'private static bool ComputeHmdPose(')
        assert compute.index('SharedWindowSpawnBridge.Handles(self)') < compute.index('TrySharedWindowAnchor(')
        replay = block(original['spawn'], 'private static void TickPoseRePlaceOne(')
        assert 'SharedWindowSize.RenderWorldScale(panel, anchor.Scale,\n                grab?.SharedReferenceWorldScale ?? 0f) * extraScale' in replay
        assert 'PanelWorldHalfSize(panel, wp.Grab.SharedReferenceWorldScale * wp.ExtraScale)' in original['host']
        reveal = block(original['lifecycle'], 'private static void CompleteReveal(')
        assert reveal.index('SharedWindowSpawnBridge.TryReveal(panel)') < reveal.index('SetPanelRenderVisible(panel, visible: true')
        prefix = 'using System; using UnityEngine; using UnityEngine.UI; using GloomhavenVR.Core; namespace GloomhavenVR.WorldUI { '
        grab = '\n'.join(block(original['grab'], signature) for signature in (
            'internal void Build(', 'internal void Tick()', 'internal void LateSyncHost()',
            'internal void ApplySharedReferenceWorldScale(', 'internal void PlaceFrameAt(',
            'private void SyncHostToFrame(', 'private float ResolveGrabFactor()',
            'internal void SetExtraScale(', 'private void PinVisualToFrame(', 'private void WriteVisual()'))
        grab += '\n' + block(original['grab'], 'internal void SnapFrameTo(')
        grab += '\n' + re.search(r'internal float SharedReferenceWorldScale => [^;]+;', original['grab'])[0]
        size = '\n'.join(block(original['size'], signature) for signature in ('internal static float ReferenceWorldScale',))
        size += '\n' + block(original['size'], 'private static float NativeHexReferenceWorldScale()')
        size += '\n' + '\n'.join(re.search(r'private static (?:float|bool) ' + name + r' = [^;]+;', original['size'])[0] for name in ('NativeHexScale', 'NativeHexReadAttempted'))
        size += '\n' + re.search(r'internal static bool UsesSharedWorldFrame\([\s\S]*?;', original['size'])[0]
        size += '\n' + re.search(r'internal static float RenderWorldScale\([\s\S]*?;', original['size'])[0]
        codec = block(original['protocol'], 'public static byte EncodeStorySize(')
        codec += '\n' + re.search(r'public static float DecodeStorySize\([\s\S]*?;', original['protocol'])[0]
        codec += '\n' + '\n'.join(re.search(r'public const byte ' + name + r' = [^;]+;', original['protocol'])[0] for name in ('StorySizeMinCode', 'StorySizeMaxCode', 'StorySizeDefaultCode'))
        clamp = '\n'.join(block(original['spawn'], signature) for signature in (
            'private static string? SeatCentreAtEyeLevel(', 'private static bool TryGetBoardPlaneY(',
            'private static string? ClampSpawnPose(', 'private struct HeightDecision'))
        clamp += '\n' + '\n'.join(re.search(r'private const float ' + name + r' = [^;]+;', original['spawn'])[0]
            for name in ('MaxSpawnPitchDeg', 'SteepGazePullFactor', 'BoardTopClearanceMeters', 'MaxAboveEyeMeters'))
        clamp += '\n' + re.search(r'private const float WindowDistanceMeters = [^;]+;', original['core'])[0]
        sources = {
            'Grab.cs': prefix + 'internal sealed partial class GrabbableModal {\n' + grab + '\n}}',
            'Size.cs': prefix + 'internal static partial class SharedWindowSize {\n' + size + '\n}}',
            'Law.cs': original['law'],
            'Conversion.cs': prefix + 'internal static partial class CanvasConversion {\n' + block(original['convert'], 'internal static void PlaceHost(') + '\n}}',
            'Codec.cs': 'using UnityEngine; namespace GloomhavenVR.Net { internal static class NetProtocol {\n' + codec + '\n}}',
            'Bridge.cs': original['bridge'],
            'Host.cs': original['host'],
            'Reward.cs': original['reward'],
            'Clamp.cs': prefix + 'internal static partial class ModalFallback {\n' + clamp + '\n}}',
            'Placement.cs': prefix + 'internal static class PanelPlacement {\n'
                + '\n'.join(re.search(r'internal const float ' + name + r' = [^;]+;', original['placement'])[0]
                    for name in ('SpawnDistance', 'SpawnDrop')) + '\n'
                + '\n'.join(block(original['placement'], signature) for signature in (
                    'internal static void Spawn(', 'internal static Quaternion Facing(')) + '\n'
                + re.search(r'private static Vector3 Flatten\([^;]+;', original['placement'])[0] + '\n}}',
            'VideoSpawn.cs': prefix + 'internal static partial class NativeVideoWindow {\n'
                + block(original['video_placement'], 'private static void ComputeInitialSpawn(') + '\n}}',
            'Facing.cs': prefix + 'internal static class HeadFacing {\n'
                + re.search(r'private const float DegenerateSqr = [^;]+;', original['facing'])[0] + '\n'
                + block(original['facing'], 'internal static Vector3 Flatten(') + '\n}}',
            'HostMeasure.cs': prefix + 'internal static partial class ModalFallback {\n' + block(original['spawn'], 'private static Vector2 PanelWorldHalfSize(') + '\n}}',
        }
        variants = [
            ('production', '', '', '', ''),
            ('local-zoom-size', 'Grab.cs', 'float worldScale = SharedWindowSize.RenderWorldScale(_panel, _spawnWorldScale,\n                                                           _spawnSharedWorldScale);',
             'float worldScale = _spawnWorldScale;', "shared Build world extent ignores each client's zoom and canvas dial"),
            ('late-local-zoom', 'Grab.cs', 'SyncHostToFrame(host, metersPerPixel, renderScale);', 'SyncHostToFrame(host, metersPerPixel, _spawnWorldScale);',
             'shared Update and LateUpdate retain the same canonical geometry'),
            ('map-local-zoom', 'Size.cs', 'return Mathf.Max(scale, 0.01f);', 'return PanelLayout.WorldScale;',
             'every shared kind uses stable parchment geometry on its first Build'),
            ('shared-reference-not-frozen', 'Grab.cs', '_spawnSharedWorldScale = SharedWindowSize.ReferenceWorldScale;', '_spawnSharedWorldScale = 0f;',
             'a live shared window keeps its reference through later rig/frame transitions'),
            ('private-size-leaks', 'Size.cs', ': privateScale;', ': ReferenceWorldScale;',
             'private window initially uses its own captured zoom'),
            ('resize-not-quantized', 'Grab.cs', 'float shared = SharedWindowSizeLaw.SharedGrabFactor(raw);', 'float shared = raw;',
             'explicit shared resize stays quantized while independent zoom is ignored'),
            ('host-pose-lags-frame', 'Grab.cs', 'host.SetPositionAndRotation(_visualPos, _visualRot);', 'host.SetPositionAndRotation(Vector3.zero, _visualRot);',
             'physical shared drag writes the same actual host pose in Update and LateUpdate'),
            ('follower-private-head', 'Host.cs', 'if (!author)', 'if (bool.Parse("false"))',
             'follower never reads its own camera while awaiting the initial shared pose'),
            ('follower-reveal-without-author', 'Bridge.cs', 'if (!Handles(panel)) return true;', 'if (!Handles(panel) || !IsInitialAuthor) return true;',
             'follower stays hidden while the current opening has no initial author pose'),
            ('follower-no-replay-retirement', 'Host.cs', 'wp.PoseRePlaceDone = true;\n            panel.SharedSpawnPoseReady = true;\n            NoteSharedAnchorSpent', 'wp.PoseRePlaceDone = false;\n            panel.SharedSpawnPoseReady = true;\n            NoteSharedAnchorSpent',
             'adopting the first shared pose retires every provisional replay'),
            ('host-replay-live-head', 'Host.cs', 'if (replay.HasValue && replay.Value.Valid)', 'if (replay.HasValue && replay.Value.Valid && bool.Parse("false"))',
             'shared final-fit replay keeps original head and captured reference through zoom and map changes'),
            ('standing-author-never-ready', 'Host.cs', 'wp.Panel.SharedSpawnPoseReady = true;', 'wp.Panel.SharedSpawnPoseReady = false;',
             'already revealed host window becomes an initial pose publisher when a session starts'),
            ('native-size-from-menu-rig', 'Size.cs', 'return NativeHexReferenceWorldScale();', 'return Rig.VRRigDriver.BaseWorldScale;',
             'native Hex reference ignores menu and torn-down rig bases'),
            ('native-reference-cache-lost', 'Size.cs', 'if (NativeHexScale > 0f) return NativeHexScale;', 'if (NativeHexScale > 0f && bool.Parse("false")) return NativeHexScale;',
             'successful original Hex geometry survives later rig and native-field gaps'),
            ('native-reference-writes-original', 'Size.cs', 'float width = UnityGameEditorRuntime.s_TileSize.x;', 'float width = UnityGameEditorRuntime.s_TileSize.x; UnityGameEditorRuntime.s_TileSize.x = 999f;',
             'shared size never writes the native tile or LOS geometry'),
            ('native-collider-world-bounds', 'Size.cs', 'if (collider != null) size = collider.size;', 'if (collider != null) size = collider.bounds.size;',
             'pre-initialization reference reads the original collider size without prefab transform scale'),
            ('native-renderer-fallback-lost', 'Size.cs', 'if (renderer != null) size = renderer.bounds.size;', 'if (renderer != null && bool.Parse("false")) size = renderer.bounds.size;',
             'original renderer fallback matches native initialization when the Hex has no collider'),
            ('captured-reference-ambient-getter', 'Grab.cs', 'internal float SharedReferenceWorldScale => _spawnSharedWorldScale;', 'internal float SharedReferenceWorldScale => SharedWindowSize.ReferenceWorldScale;',
             'shared final-fit replay keeps original head and captured reference through zoom and map changes'),
            ('captured-reference-adoption-lost', 'Grab.cs', '_spawnSharedWorldScale = reference;', '_spawnSharedWorldScale = _spawnSharedWorldScale * 1f;',
             'a follower adopts the author reference independently of its local map domain'),
            ('captured-reference-delayed-render', 'Grab.cs', '_panel.HostGo.transform.localScale = Vector3.one * (unit * _visualScale);', '/* immediate reference-scale synchronization omitted */',
             'reference adoption updates the actual host before any first reveal or pose tick'),
            ('captured-reference-frame-factor', 'Grab.cs', '_spawnSharedWorldScale = reference;', '_spawnSharedWorldScale = reference; if (_frame != null) _frame.localScale = Vector3.one;',
             'reference adoption preserves the exact physical frame and manual factor'),
            ('host-distance-private-zoom', 'Host.cs', 'placementScale = SharedHostSpawnWorldScale(panel);',
             'placementScale = PanelLayout.WorldScale;',
             'shared opening distance uses its captured reference independent of host live and saved zoom'),
            ('host-distance-ambient-reference', 'Host.cs', 'return wp.Grab.SharedReferenceWorldScale;',
             'return SharedWindowSize.ReferenceWorldScale;',
             'shared final-fit replay keeps original head and captured reference through zoom and map changes'),
            ('host-distance-private-replay', 'Host.cs', 'Vector3 raw = headPosition + headForward * (WindowDistanceMeters * placementScale);',
             'Vector3 raw; if (replay.HasValue && replay.Value.Valid) { raw = replay.Value.RawPos; placementScale = replay.Value.Scale; } else raw = headPosition + headForward * (WindowDistanceMeters * placementScale);',
             'a replay from earlier private placement cannot import its old zoom distance or scale'),
            ('host-distance-pre-grab-zoom', 'Host.cs', 'return SharedWindowSize.ReferenceWorldScale;',
             'return PanelLayout.WorldScale;',
             'initial conversion before the grab exists uses the common geometry reference instead of private zoom'),
            ('host-distance-raw-pitch', 'Clamp.cs', 'reason = SeatCentreAtEyeLevel(headPos, headForward, ref pos, scale);',
             'pos.y = headPos.y;',
             'shared opening distance uses its captured reference independent of host live and saved zoom'),
            ('video-distance-private-zoom', 'VideoSpawn.cs', 'float placementScale = SharedPlacementRequired',
             'float placementScale = SharedPlacementRequired && bool.Parse("false")',
             'shared video opening uses fixed absolute distance across independent host zoom'),
            ('video-distance-ambient-reference', 'VideoSpawn.cs', '_grab?.SharedReferenceWorldScale ?? SharedWindowSize.ReferenceWorldScale',
             'SharedWindowSize.ReferenceWorldScale',
             'shared video uses its captured grab reference through later scene transitions'),
            ('reward-captured-reference-lost', 'Reward.cs', 'if (_referenceScale > 0f) grab.ApplySharedReferenceWorldScale(_referenceScale);', '/* cached reward reference omitted */',
             'a reward reference received before conversion reaches the later actual grab'),
        ]
        if args.case:
            unknown = set(args.case) - {item[0] for item in variants}
            if unknown:
                parser.error('Unknown case: ' + ', '.join(sorted(unknown)))
            variants = [item for item in variants if item[0] in args.case]
        unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
        managed = unity.parent / 'Data/Managed'
        game = root / 'ressources/GH_Data/Managed'
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        manifest = {'result': str(run / 'results.txt'), 'cases': []}
        for name, filename, before, after, expected in variants:
            case = run / name
            generated = case / 'Sources'
            generated.mkdir(parents=True)
            for source_name, text in sources.items():
                if filename == source_name:
                    if text.count(before) != 1:
                        raise RuntimeError('Causal-control binding drift: ' + name)
                    text = text.replace(before, after, 1)
                (generated / source_name).write_text(text)
            for path in fixture.glob('*.cs'):
                shutil.copyfile(path, generated / path.name)
            assembly = 'WindowGeometry_' + name.replace('-', '_')
            project = case / 'Geometry.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><AssemblyName>' + assembly + '</AssemblyName></PropertyGroup><ItemGroup><Reference Include="' + str(managed) + '/UnityEngine/*.dll"/><Reference Include="UnityEngine.UI" HintPath="' + str(game) + '/UnityEngine.UI.dll"/></ItemGroup></Project>\n')
            result = execute([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false'], case / 'build.log', 120)
            if result:
                raise RuntimeError('Compilation is not a passing causal control: ' + str(case / 'build.log'))
            manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        project = run / 'unity'
        for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'):
            (project / directory).mkdir(parents=True)
        for case in manifest['cases']:
            path = project / 'Assets/Plugins' / Path(case['dll']).name
            shutil.copyfile(case['dll'], path)
            case['dll'] = str(path)
        shutil.copyfile(ROOT / 'scripts/shared-ui-window-runtime/Editor/SharedUiRunner.cs', project / 'Assets/Editor/SharedUiRunner.cs')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0'}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        path = run / 'manifest.json'
        path.write_text(json.dumps(manifest, indent=2) + '\n')
        status = execute(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'SharedUiRunner.Start', '-interactionManifest', str(path), '-logFile', str(run / 'unity.log')], run / 'unity-launch.log')
        if (run / 'results.txt').exists():
            print((run / 'results.txt').read_text(), flush=True)
        for name, code in original.items():
            if hashlib.sha256((root / paths[name]).read_bytes()).hexdigest() != hashes[paths[name]]:
                raise RuntimeError('Source changed during the proof: ' + paths[name])
        return status
    finally:
        (run / 'source-hashes.json').write_text(json.dumps(locals().get('hashes', {}), indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('shared-window-geometry-runtime', status, completion, ROOT, process_reaped=True), indent=2) + '\n')
        print('Evidence: ' + str(run), flush=True)


if __name__ == '__main__':
    raise SystemExit(main())
