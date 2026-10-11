#!/usr/bin/env python3
"""Prove production actor bar height against real native Drake skin, poses and rendered pixels."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/actor-bar-pose-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--native-drake-bundle', type=Path, default=ROOT / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/npc_spittingdrake_assets_all.bundle')
    parser.add_argument('--native-other-bundle', type=Path, default=ROOT / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/npc_cavebear_assets_all.bundle')
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    if not args.unity.is_file() or not args.native_drake_bundle.is_file() or not args.native_other_bundle.is_file():
        parser.error('Real Unity and original native Drake bundle required; no silent skip')
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve())) if args.output_dir.is_dir() else None
    if run is None:
        args.output_dir.mkdir(parents=True)
        run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    source = args.source_root / 'src/GloomhavenVR'
    bars = (source / 'WorldUI/ActorBars.cs').read_text()
    # The complete renderer/gameplay adoption depends on the game. Check the production hook,
    # then run its exact resample/preparation/track methods in real Unity with boundary inputs.
    adopt = method(bars, 'private static void Adopt(WorldspacePanelUIController controller)')
    required = ('ActorBarPose? pose = CapturePose(controller);', 'TryPoseOffset(pose, controller, out anchorOffset)',
                'Pose = pose,', 'AnchorSamplesLeft = pose != null ? 0 : AnchorSampleBudget',
                '* (0.5f + phase / DepthScanIntervalSeconds)')
    for token in required:
        if token not in adopt: raise SystemExit('Production adoption hook drift: ' + token)
    if 'ActorBarPose.Reset();' not in bars or bars.index('ResampleAnchor(adopted, controller, now);') > bars.index('Vector3 pos = track + Vector3.up * adopted.AnchorOffsetWU;'):
        raise SystemExit('Production steady pose or shutdown hook drift')
    if 'if (Adoptions.Count == 0) ActorBarPose.Reset();' not in bars:
        raise SystemExit('Last native bar release must drop cached native mesh references')
    methods = '\n'.join(method(bars, signature) for signature in (
        'private static bool TryGetTrackPoint(', 'private static void ResampleAnchor(',
        'private static ActorBarPose? CapturePose(', 'private static bool TryPoseOffset(',
        'private static float TrackY(', 'private static void FollowPoseAnchor('))
    budget = (source / 'Core/Perf/ScenarioFigureDetailBudget.cs').read_text()
    seam = method(budget, 'internal Mesh? OriginalMeshFor(Renderer renderer)')
    seam += '\n' + method(budget, 'internal ScenarioFigureMeshBank.Record? OriginalRecordFor(Renderer renderer)')
    sources = {
        'ActorBarPose.cs': (source / 'WorldUI/ActorBarPose.cs').read_text(),
        'NativeActorPoseAudit.cs': (source / 'Core/Perf/NativeActorPoseAudit.cs').read_text(),
        'FigureVisualMirror.cs': (source / 'Board/FigureGrab/FigureVisualMirror.cs').read_text(),
        'ScenarioFigureMeshBank.cs': (source / 'Core/Perf/ScenarioFigureMeshBank.cs').read_text(),
        'ActorBarsReads.cs': 'using UnityEngine; using GloomhavenVR.Board.FigureGrab; using GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI { internal static partial class ActorBars {\n' + methods + '\n}}',
        'OriginalMeshReads.cs': 'using UnityEngine; namespace GloomhavenVR.Core { internal static partial class ScenarioFigureDetailBudget {\ninternal static Mesh? OriginalMeshFor(Renderer renderer) => _driver?.OriginalMeshFor(renderer);\ninternal static ScenarioFigureMeshBank.Record? OriginalRecordFor(Renderer renderer) => _driver?.OriginalRecordFor(renderer);\ninternal sealed partial class Driver {\n' + seam + '\n}}}',
    }
    proof = {'root': str(args.source_root.resolve()), 'sha256': {name: hashlib.sha256(code.encode()).hexdigest() for name, code in sources.items()}}
    proof['sha256']['ActorBars.cs'] = hashlib.sha256(bars.encode()).hexdigest()
    proof['sha256']['ScenarioFigureDetailBudget.cs'] = hashlib.sha256(budget.encode()).hexdigest()
    proof['sha256']['native_other_bundle'] = hashlib.file_digest(args.native_other_bundle.open('rb'), 'sha256').hexdigest()
    proof['sha256']['native_drake_bundle'] = hashlib.file_digest(args.native_drake_bundle.open('rb'), 'sha256').hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps(proof, indent=2) + '\n')
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('no-avatar-cycle-binding', 'ActorBarPose.cs', 'Animator sampler = animated.gameObject.AddComponent<Animator>();\n            sampler.avatar = animator.avatar; sampler.enabled = false;', '', 'complete native loop peak known before first flap'),
            ('lifetime-loop-cache', 'ActorBarPose.cs', 'if (!_hasLoop || key != _loopState || scale != _loopScale || rotation != _loopRotation)', 'if (!_hasLoop || scale != _loopScale || rotation != _loopRotation)', 'native sleep state releases prior flight cycle peak immediately'),
            ('skip-unknown-bone-writer', 'ActorBarPose.cs', 'if (component is MonoBehaviour || component is UnityEngine.Animations.IConstraint)', 'if (bool.Parse("false"))', 'unknown procedural bone deformation always gets an immediate safe ceiling'),
            ('disable-loop-cadence', 'ActorBarPose.cs', 'bool sparse = interval > 0f && SparseEligible', 'bool sparse = bool.Parse("false") && SparseEligible', 'Frame loop cadence actually suppresses repeated original bone matrix walks'),
            ('omit-cycle-envelope', 'ActorBarPose.cs', 'if (haveBounds) relative =', 'if (false && haveBounds) relative =', 'complete native loop peak known before first flap'),
            ('head-offset-smoothing', 'ActorBarsReads.cs', 'adopted.AnchorOffsetWU = adopted.PoseAnchorY - trackY;', 'adopted.AnchorOffsetWU = ActorBarPose.Follow(adopted.AnchorOffsetWU, offset, Time.unscaledDeltaTime);', 'animated head cannot bob world-space cycle anchor'),
            ('ignore-live-pose', 'ActorBarsReads.cs', 'if (TryPoseOffset(adopted.Pose, controller, out float poseOffset))', 'if (TryPoseOffset(null, controller, out float poseOffset))', 'live native skin envelope remains available'),
            ('keep-lifetime-maximum', 'ActorBarPose.cs', ': Mathf.Lerp(current, needed, 1f - Mathf.Exp(-8f * Mathf.Max(0f, deltaSeconds)))', ': current', 'sleep transition releases lifetime high-water latch'),
            ('smooth-waking', 'ActorBarPose.cs', '? needed // abrupt native waking', '? Mathf.Lerp(current, needed, 0.01f) // abrupt native waking', 'health band lowest edge clears actual evaluated pose'),
            ('retain-flight-floor', 'ActorBarPose.cs', 'float needed = top - trackY + Mathf.Max(0.26f, 0.12f * height);', 'float needed = Mathf.Max(2.10f, top - trackY + 0.12f * height);', 'sleeping bar lowers below authored flight floor'),
            ('clamp-to-static-head', 'ActorBarPose.cs', 'baseY - trackY, upper', '0f, upper', 'flight-captured static head point never floors sleeping bar'),
            ('lose-bake-world-space', 'ActorBarPose.cs', 'Matrix4x4 world = Baker.transform.localToWorldMatrix;', 'Matrix4x4 world = skin.transform.localToWorldMatrix;', 'default BakeMesh preserves rotated scaled child ancestry'),
            ('head-bones-only', 'ActorBarPose.cs', 'Top(bone.Transform.localToWorldMatrix, bone.Local)', 'bone.Transform.position.y', 'default BakeMesh recovered original native bind vertices'),
            ('use-derivative-source', 'ActorBarPose.cs', 'Mesh? source = owner?.Original ?? skin.sharedMesh;', 'Mesh? source = owner?.Current ?? skin.sharedMesh;', 'detail0 vs100 native envelope uses exact original source'),
            ('admit-native-named-ghost', 'ActorBarPose.cs', 'renderer.GetComponentInParent<FigureVisualMirror>(true) != null', 'false', 'native-named visual ghost excluded by exact mirror owner'),
            ('admit-unsupported-particle', 'ActorBarPose.cs', 'renderer.GetComponentInParent<ParticleSystem>(true) != null', 'false', 'unsupported particle skin cannot poison valid native body'),
            ('admit-rigid-action-fx', 'ActorBarPose.cs', 'if (renderer is SkinnedMeshRenderer skin)', 'if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>()?.sharedMesh is Mesh fx) boxes[renderer.transform] = fx.bounds;\n            if (renderer is SkinnedMeshRenderer skin)', 'temporary large rigid native action effect never raises body envelope'),
            ('admit-foreign-skeleton', 'ActorBarPose.cs', 'if (!ownsHead) continue;', '// separate effect skeleton admitted', 'unsupported separate native FX skeleton cannot poison tracked head body'),
            ('latch-invalid-body', 'ActorBarsReads.cs', 'if (adopted.Pose != null)', 'if (false && adopted.Pose != null)', 'reparented native skeleton triggers bounded deferred preparation'),
            ('ignore-controller-retarget', 'ActorBarPose.cs', '&& root.transform == _root && head == _head;', '&& head != null;', 'pooled controller retarget invalidates old body while it stays alive'),
            ('ignore-native-source-swap', 'ActorBarPose.cs', 'if (current == Original) return true;', 'if (current != null) return true;', 'same-root native mesh replacement invalidates cached body source'),
            ('ignore-native-palette-swap', 'ActorBarPose.cs', 'if (current[i] != skin.Bones[i]) return false;', 'if (current[i] != skin.Bones[i] && bool.Parse("false")) return false;', 'same-root native bone palette replacement invalidates old cached bones'),
            ('consume-positive-player-offset', 'ActorBarPose.cs', 'needed + Mathf.Max(0f, userOffset) : 6f', 'needed : 6f', 'positive player offset survives legitimate large native body'),
            ('rearm-partial-controller', 'ActorBarsReads.cs', 'if (TryPoseOffset(candidate, controller, out poseOffset))', 'if (TryPoseOffset(adopted.Pose = candidate, controller, out poseOffset))', 'partial native tracking controller cannot repeatedly re-arm preparation'),
            ('unbounded-preparation', 'ActorBarsReads.cs', 'if (adopted.AnchorSamplesLeft <= 0 || now < adopted.NextAnchorSample)', 'if (bool.Parse("false"))', 'missing body retries bounded and never per-frame'),
            ('retain-profile-refs', 'ActorBarPose.cs', 'Profiles.Clear(); Refused.Clear(); Vertices.Clear();', 'Refused.Clear(); Vertices.Clear();', 'reset releases profile meshes'),
        ]
    if args.case:
        requested = set(args.case)
        unknown = requested - {entry[0] for entry in variants}
        if unknown: parser.error('Unknown case: ' + ', '.join(sorted(unknown)))
        variants = [entry for entry in variants if entry[0] in requested]
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    fixture = ROOT / 'scripts/actor-bar-pose-runtime'
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, filename, before, after, expected in variants:
        build = run / name; generated = build / 'production'; generated.mkdir(parents=True)
        for path, code in sources.items():
            if path == filename:
                if code.count(before) != 1: raise SystemExit('Mutation binding drift: ' + name)
                code = code.replace(before, after, 1)
            (generated / path).write_text(code)
        assembly = 'ActorBarPose_' + name.replace('-', '_')
        project = build / 'Pose.csproj'; shutil.copyfile(fixture / 'Pose.csproj', project)
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly,
            '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(generated), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a passing defect control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    project = run / 'unity'
    for folder in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'): (project / folder).mkdir(parents=True)
    for entry in manifest['cases']:
        destination = project / 'Assets/Plugins' / Path(entry['dll']).name; shutil.copyfile(entry['dll'], destination); entry['dll'] = str(destination)
    # Read-only bank links let the actual production mesh record resolve installed derivatives.
    for bank in (args.source_root / 'prebuilt').glob('ghvr-figure-meshes-*'):
        (project / 'Assets/Plugins' / bank.name).symlink_to(bank.resolve())
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2))
    shutil.copyfile(fixture / 'Editor/PoseRunner.cs', project / 'Assets/Editor/PoseRunner.cs')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.' + name: '1.0.0' for name in ('physics', 'cloth', 'animation', 'assetbundle', 'imageconversion', 'particlesystem')}}))
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = ['xvfb-run', '-a', str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'PoseRunner.Start',
               '-interactionManifest', str(manifest_path), '-nativeDrakeBundle', str(args.native_drake_bundle.resolve()), '-nativeOtherBundle', str(args.native_other_bundle.resolve()), '-evidenceRoot', str(run), '-logFile', str(run / 'unity.log')]
    # Twenty-seven independent native-skin variants render real animation frames.
    # With the eight-job integration workload, the 600s wall deadline expired after
    # production and 24 controls had already passed. Keep a finite deadline while
    # allowing software-GL/CPU contention; timeout is still a failed proof.
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=900)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity; see ' + str(run / 'unity.log'))
    print('PASS: ' + str(len(variants)) + ' production/defect variants; evidence: ' + str(run))


if __name__ == '__main__': main()
