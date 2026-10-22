#!/usr/bin/env python3
"""Execute shared-window context, sampler, receive, handoff and movement-election production kernels."""
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


def execute(command, log):
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=120)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/shared-window-pose-runtime')
    parser.add_argument('--case', action='append', help='Partial development run; repeat to choose cases')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir)).resolve()
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    status, results = 1, []
    hashes = {}
    try:
        files = {
            'context': 'Net/SharedWindowPoseContext.cs', 'scales': 'Net/SharedWindowScaleContext.cs', 'authority': 'Net/SharedWindowSpawnAuthority.cs',
            'story': 'Net/Remote/RemoteStorySync.cs', 'story_initial': 'Net/Remote/RemoteStorySync.InitialPose.cs',
            'map': 'Net/Remote/RemoteMapStory.cs', 'map_initial': 'Net/Remote/RemoteMapStory.InitialPose.cs',
            'track': 'Net/SharedWindowPoseTrack.cs', 'frame': 'Net/Remote/SharedWindowFrame.cs',
            'ledger': 'Net/MapStoryOpeningLedger.cs', 'opening': 'Net/MapStoryLifecycleState.cs',
            'lifecycle': 'Net/Remote/MapStoryLifecycle.cs', 'version': 'Net/VersionGuard.cs',
            'avatar': 'Net/Avatar/AvatarState.cs', 'codec': 'Net/Avatar/AvatarSerializer.cs',
            'anchor': 'Net/Board/IBoardAnchor.cs', 'protocol': 'Net/NetProtocol.cs',
            'law': 'WorldUI/Modal/SharedWindowSizeLaw.cs', 'sender': 'Net/Avatar/NetAvatarDriver.cs',
            'presence': 'Net/PresenceState.cs', 'module': 'Net/NetModule.cs',
            'video': 'Net/Remote/RemoteMapStory.Video.cs', 'playback': 'Net/Remote/RemoteVideoPlayback.cs',
            'reward': 'Net/Remote/RemoteMapStory.Reward.cs', 'reward_window': 'Net/RewardWindowState.cs',
            'reward_handshake': 'Net/RewardPoseHandshake.cs', 'reward_handoff': 'Net/RewardPoseHandoff.cs',
        }
        original = {name: (root / 'src/GloomhavenVR' / path).read_text() for name, path in files.items()}
        hashes = {files[name]: hashlib.sha256(code.encode()).hexdigest() for name, code in original.items()}
        fixture = ROOT / 'scripts/shared-window-pose-runtime'
        fixture_sources = {path.name: path.read_text() for path in fixture.glob('*.cs')}
        for path in [Path(__file__), *fixture.glob('*.cs')]:
            hashes[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
        # The proof consumes the actual runtime kernels. These pins connect their inputs to the
        # integrated packet path; the independent full golden suite owns legacy19/21 byte vectors.
        assert 'SharedWindowSpawnAuthority.SampleContext(ref extras);' in original['sender']
        assert 'SharedWindowSpawnAuthority.Bind(' in original['module']
        assert 'SharedWindowPoseContext.Write(buffer, ref i, state.WindowPoseContext.Value)' in original['presence']
        assert 'SharedWindowPoseContext.TryRead(buffer, i, len, out SharedWindowPoseContext context)' in original['presence']
        assert 'SharedWindowScaleContext.Write(buffer, ref i, state.WindowScales.Value)' in original['presence']
        assert 'SharedWindowScaleContext.TryRead(buffer, i, len, out SharedWindowScaleContext context)' in original['presence']
        for source in ('story', 'map'):
            assert 'p.WindowPoseContext' in original[source]
        prefix = 'using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.UI; using GloomhavenVR.Core; using GloomhavenVR.WorldUI; using GloomhavenVR.WorldUI.MapRoom; namespace GloomhavenVR.Net { '
        story = '\n'.join(block(original['story'], signature) for signature in (
            'private readonly struct PeerStory', 'internal static uint HashDialog(', 'private static uint HashString(',
            'internal static void Sample(', 'private static void TrackFrame(', 'private static bool TryToAnchor(',
            'private static bool TryToWorld(', 'internal static void Observe(', 'private static void ResolvePose('))
        mapped = '\n'.join(block(original['map'], signature) for signature in (
            'private readonly struct PeerEntry', 'private sealed class Local', 'private static void WritePose(',
            'private static void TrackFrame(', 'private static bool SyncIdentity(', 'private static bool ToShared(',
            'private static bool ToWorld(', 'internal static void Observe(', 'private static void NoteStamp(',
            'private static void Forget(', 'private static void ResolvePose(', 'private static void PruneStale(Dictionary'))
        codec = '\n'.join(block(original['codec'], signature) for signature in (
            'internal static void WriteU32(', 'internal static uint ReadU32(',
            'internal static void WriteF32(', 'internal static float ReadF32('))
        protocol = '\n'.join(re.search(r'public const (?:byte|ushort|float) ' + name + r' = [^;]+;', original['protocol'])[0] for name in (
            'ModBuild', 'ExtIdSharedWindowPoseContext', 'ExtIdSharedWindowScaleContext', 'StorySizeMinCode', 'StorySizeMaxCode', 'StorySizeDefaultCode',
            'StoryOpenBit', 'StoryPoseBit', 'StoryFinishedBit', 'StoryPageNone', 'StoryPageMax',
            'SharedOpenBit', 'SharedPoseBit', 'SharedFinishedBit', 'SharedWindowKindMapStory',
            'SharedWindowKindQuestConfirm', 'SharedWindowKindEncounter', 'SharedFrameSeatAnchor', 'SharedFrameParchment',
            'RewardFrameScenario', 'SendRateHz', 'ExtIdRewardPoseHandshake'))
        for signature in ('public static byte EncodeStorySize(', 'public static byte EncodeStoryPage('):
            protocol += '\n' + block(original['protocol'], signature)
        protocol += '\n' + re.search(r'public static float DecodeStorySize\([\s\S]*?;', original['protocol'])[0]
        version = block(original['version'], 'internal static void CollectContinuationPeers(')
        version += '\n' + re.search(r'internal static int PeerBuild\([\s\S]*?;', original['version'])[0]
        lifecycle = block(original['lifecycle'], 'internal static void PoseOpening(')
        # Native decoder/content lifecycle is covered by VideoPlaybackTests. Execute its exact
        # pose-forwarding tail here with real peer storage, codecs and first-reveal adoption.
        video_observe = block(original['playback'], 'internal static void Observe(')
        video_tail = video_observe[video_observe.rindex('        if (extras.HasVideoWindow)'):-1]
        sources = {
            'Context.cs': original['context'], 'Scales.cs': original['scales'], 'Authority.cs': original['authority'],
            'Story.cs': prefix + 'internal static partial class RemoteStorySync {\n' + story + '\n}}',
            'Map.cs': prefix + 'internal static partial class RemoteMapStory {\n' + mapped + '\n}}',
            'StoryInitial.cs': original['story_initial'], 'MapInitial.cs': original['map_initial'],
            'Track.cs': original['track'], 'Frame.cs': original['frame'],
            'Ledger.cs': original['ledger'], 'Opening.cs': original['opening'],
            'Lifecycle.cs': prefix + 'internal static partial class MapStoryLifecycle {\n' + lifecycle + '\n}}',
            'Version.cs': prefix + 'internal static partial class VersionGuard {\n' + version + '\n}}',
            'RigPose.cs': prefix + block(original['avatar'], 'internal struct RigPose') + '\n}',
            'Codec.cs': prefix + 'internal static unsafe class AvatarSerializer {\n' + codec + '\n}}',
            'Protocol.cs': prefix + 'internal static class NetProtocol {\n' + protocol + '\n}}',
            'Anchor.cs': original['anchor'], 'Law.cs': original['law'],
            'VideoReceipt.cs': prefix + 'internal static class VideoPoseReceipt { internal static void Observe(int sender, in PresenceState extras) {\n' + video_tail + '\n}}}',
            'Video.cs': original['video'], 'Reward.cs': original['reward'],
            'RewardHandshake.cs': original['reward_handshake'], 'RewardHandoff.cs': original['reward_handoff'],
            'RewardWindow.cs': prefix + block(original['reward_window'], 'internal struct RewardWindowState') + '\n' + block(original['reward_window'], 'internal static class RewardPosePolicy') + '\ninternal static class RewardWindowCodec {\n' + block(original['reward_window'], 'internal static bool Valid(') + '\n' + re.search(r'private static bool Finite\([^;]+;', original['reward_window'])[0] + '\n}}',
            'RewardCensus.cs': prefix + 'internal static class NetAvatarDriver {\n' + block(original['sender'], 'internal static void CollectRewardPosePeers(') + '\n}}',
        }
        variants = [
            ('production', '', '', '', ''),
            ('observer-frame-leaks', 'Context.cs', 'Origin + yaw * (relative.Position * Scale)',
             'GloomhavenVR.WorldUI.PanelLayout.Origin + yaw * (relative.Position * GloomhavenVR.WorldUI.PanelLayout.WorldScale)',
             "record119 reconstructs the sender's canonical world pose independent of every observer's seat and zoom"),
            ('wrong-author', 'Authority.cs', '&& sender == AuthorId &&', '&& sender > 0 &&',
             'another sender cannot claim host initial placement'),
            ('unknown-local-author', 'Authority.cs', '(NetPlayerActors.LocalPlayerId() > 0 && AuthorId == NetPlayerActors.LocalPlayerId())',
             '(AuthorId == NetPlayerActors.LocalPlayerId())', 'unknown local player identity cannot reveal a provisional personal spawn'),
            ('wrong-initial-kind', 'Authority.cs', 'context.Value.IsInitial(kind)', 'true',
             'current host initial state is limited to its own window kind'),
            ('story-first-snap-echo', 'StoryInitial.cs', '_lastFramePos = position;', '_lastFramePos = Vector3.zero;',
             "first visible shared snap is adopted as a baseline and never echoed as the observer's manual move"),
            ('map-first-snap-echo', 'MapInitial.cs', 'local.FramePos = position;', 'local.FramePos = Vector3.zero;',
             'map first shared snap retains its exact subject baseline and is never republished as a manual move'),
            ('map-initial-subject-lost', 'MapInitial.cs', 'local.Grab = grab;', 'local.Grab = null;',
             'map initial adoption retains the exact native subject and skips identity-settle suppression'),
            ('story-host-overrides-manual', 'StoryInitial.cs', 'if (best != 0 && peer.Initial && !initial) continue;', '',
             'a current manual peer pose outranks a newer host initial pose for late joiners'),
            ('map-host-overrides-manual', 'MapInitial.cs', 'if (best != 0 && peer.Initial && !initial) continue;', '',
             'map late join follows the actual manual mover ahead of a newer host initial stamp'),
            ('map-canonical-frame-rejected', 'Map.cs', 'if (frame == NetProtocol.RewardFrameScenario)',
             'if (scenarioFrame && frame == NetProtocol.RewardFrameScenario)',
             'map initial handoff reconstructs the same sender pose in parchment and normalized fallback frames'),
            ('story-missing-context', 'Story.cs', 'if (!Canonical || !(referenceScale >= 0.0001f && referenceScale <= 1000000f)) Flags =', 'if (bool.Parse("false")) Flags =',
             'missing or foreign initial scenario provenance stays pending: missing'),
            ('map-missing-context', 'Map.cs', 'if (!context.HasValue || !context.Value.Valid || !(referenceScale >= 0.0001f && referenceScale <= 1000000f))', 'if (bool.Parse("false"))',
             'missing or foreign map initial provenance stays pending: MapStory missing'),
            ('story-live-host-overrides-manual', 'Story.cs', 'if (bestPeer != 0 && s.Initial && !bestInitial) continue;', '',
             'live scenario pose election also keeps the manual mover ahead of repeated host initialization'),
            ('map-live-host-overrides-manual', 'Map.cs', 'if (bestPeer != 0 && s.Initial && !bestInitial) continue;', '',
             'live map election preserves the manual pose in its actual decoded coordinate frame'),
            ('wrong-initial-kind-sampling', 'Authority.cs', 'SharedWindowPoseContext.KindBit(entry.Kind + 1)', 'SharedWindowPoseContext.KindBit(entry.Kind)',
             'actual presence sampler tags all six initial window kinds atomically with the sender frame'),
            ('reopened-float-reuses-old-pose', 'MapInitial.cs',
             'if (requireOpeningReceipt && unchecked(peer.ReceiptFrame - local.InitialReceiptFrame) < 0) continue;', '',
             'same-key reopened quest or encounter rejects cached prior-float pose'),
            ('story-initial-key-not-adopted', 'StoryInitial.cs', '_localKey = key;', '_localKey = 0;',
             'a genuine local grab publishes a manual pose after initial adoption'),
            ('captured-scale-sampling-lost', 'Authority.cs', 'references.Set((int)kind, grab.SharedReferenceWorldScale);', 'references.Set((int)kind, SharedWindowSize.ReferenceWorldScale);',
             "the actual presence sampler publishes each posed grab's captured reference rather than ambient geometry"),
            ('story-initial-scale-lost', 'StoryInitial.cs', 'grab.ApplySharedReferenceWorldScale(owner.ReferenceScale);', '',
             'scenario121 adoption changes the pending grab reference before its first physical snap or reveal'),
            ('map-initial-scale-lost', 'MapInitial.cs', 'grab.ApplySharedReferenceWorldScale(owner.ReferenceScale);', '',
             'map121 adoption reconstructs Seat0 without a local parchment and sets the pending grab reference before reveal'),
            ('video-initial-scale-lost', 'Video.cs', 'grab.ApplySharedReferenceWorldScale(owner.ReferenceScale);', '',
             'video121 adoption resolves a3D-map host for a2D-map observer before the first reveal'),
            ('story-live-scale-lost', 'Story.cs', 'grab.ApplySharedReferenceWorldScale(owner.ReferenceScale);', '',
             'live scenario placement adopts the winning packet reference before writing its physical pose'),
            ('map-live-scale-lost', 'Map.cs', 'grab.ApplySharedReferenceWorldScale(owner.ReferenceScale);', '',
             'live map placement adopts the winning packet reference before writing its physical pose'),
            ('story-missing-scale-accepted', 'Story.cs', 'if (!Canonical || !(referenceScale >= 0.0001f && referenceScale <= 1000000f)) Flags =', 'if (!Canonical) Flags =',
             'scenario missing or invalid121 remains pending: missing'),
            ('map-missing-scale-accepted', 'Map.cs', 'if (!context.HasValue || !context.Value.Valid || !(referenceScale >= 0.0001f && referenceScale <= 1000000f))', 'if (!context.HasValue || !context.Value.Valid)',
             'map missing or invalid121 remains pending: MapStory missing'),
            ('reward-initial-scale-lost', 'Reward.cs', 'referenceScale = owner.ReferenceScale;', 'referenceScale = 0f;',
             'actual reward121 receive and initial publication retain reference and WorldFrame2 pose before conversion exists'),
            ('video-scale-receipt-lost', 'VideoReceipt.cs', 'extras.WindowScales?.Reference((int)SharedWindowKind.Video) ?? 0f', '0f',
             'video121 adoption resolves a3D-map host for a2D-map observer before the first reveal'),
            ('map-writer-parchment-domain', 'Map.cs', 'frame = NetProtocol.SharedFrameSeatAnchor;\n        if (!PanelLayout.TryGetAnchor', 'if (MapRoomDriver.TryGetParchmentFrame(out Vector3 c, out float scale)) { frame = NetProtocol.SharedFrameParchment; localPos = (worldPos-c)/scale; localRot = worldRot; return true; }\n        frame = NetProtocol.SharedFrameSeatAnchor;\n        if (!PanelLayout.TryGetAnchor',
             'current map writer uses source Seat0 even while a parchment frame is available'),
            ('reward-writer-seat-domain', 'Map.cs', 'if (scenarioFrame)', 'if (scenarioFrame && bool.Parse("false"))',
             'the actual reward sampler keeps WorldFrame2 even while parchment geometry is available'),
        ]
        if args.case:
            unknown = set(args.case) - {item[0] for item in variants}
            if unknown:
                parser.error('Unknown case: ' + ', '.join(sorted(unknown)))
            variants = [item for item in variants if item[0] in args.case]
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        for name, filename, before, after, expected in variants:
            case = run / name
            generated = case / 'Sources'
            generated.mkdir(parents=True)
            for source_name, text in sources.items():
                if source_name == filename:
                    if text.count(before) != 1:
                        raise RuntimeError('Causal-control source binding drift: ' + name)
                    text = text.replace(before, after, 1)
                (generated / source_name).write_text(text)
            for source_name, text in fixture_sources.items():
                (generated / source_name).write_text(text)
            project = case / 'Pose.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><AllowUnsafeBlocks>true</AllowUnsafeBlocks><GenerateDocumentationFile>false</GenerateDocumentationFile><NoWarn>CS0649;CS0169</NoWarn></PropertyGroup></Project>\n')
            if execute([dotnet, 'build', str(project), '-c', 'Release', '--disable-build-servers', '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false'], case / 'build.log'):
                raise RuntimeError('Compilation is not a passing control: ' + str(case / 'build.log'))
            code = execute([dotnet, str(case / 'bin/Release/net8.0/Pose.dll')], case / 'runtime.log')
            output = (case / 'runtime.log').read_text()
            passed = code == 0 if not expected else code != 0 and expected in output
            results.append({'case': name, 'passed': passed, 'exit_code': code, 'expected': expected, 'output': output})
            print(('PASS ' if passed else 'FAIL ') + name + ': ' + output.strip(), flush=True)
            if not passed:
                raise RuntimeError('Production/control failure: ' + str(case))
        for name, text in original.items():
            if (root / 'src/GloomhavenVR' / files[name]).read_text() != text:
                raise RuntimeError('Production input changed during proof: ' + files[name])
        for name, text in fixture_sources.items():
            if (fixture / name).read_text() != text:
                raise RuntimeError('Fixture input changed during proof: ' + name)
        status = 0
        return 0
    finally:
        (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': status, 'cases': results}, indent=2) + '\n')
        # Sources may be borrowed from the integration checkout; generated outputs belong only to
        # this worker's run, never to the read-only source-root or another agent's active run.
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('shared-window-pose-runtime', status, completion, ROOT, process_reaped=True), indent=2) + '\n')
        print('Evidence: ' + str(run), flush=True)


if __name__ == '__main__':
    raise SystemExit(main())
