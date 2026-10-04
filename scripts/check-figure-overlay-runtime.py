#!/usr/bin/env python3
"""Render native SpittingDrake pose and cutout/depth overlays in real Unity play mode.

Native assets remain read-only; generated project and evidence are isolated. A shader source
GL proof complements the release bundle's separate Windows shader-build/immutability gate.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/figure-overlay-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--native-drake-bundle', type=Path, default=ROOT / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/npc_spittingdrake_assets_all.bundle')
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--case', action='append', help='Partial focused variant list')
    args = parser.parse_args()
    if not args.unity.is_file() or not args.native_drake_bundle.is_file():
        parser.error('Actual Unity and native SpittingDrake bundle required; no silent skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/figure-overlay-runtime'
    base = args.source_root / 'src/GloomhavenVR/Board/FigureGrab'
    sources = {name: (base / name).read_text() for name in ('FigureOverlay.cs', 'FigureHighlight.cs', 'FigureGlowGrade.cs', 'FigureGhosts.cs', 'FigureVisualMirror.cs', 'FigureInteractionPreparation.cs')}
    stat_source = (args.source_root / 'src/GloomhavenVR/WorldUI/Surfaces/StatPanelSurface.cs').read_text()
    stat_start = stat_source.index('    // Build617: cache only already-authored original imagery')
    stat_end = stat_source.index('    /// <summary>Viewer-relative dock side', stat_start)
    sources['StatPreparation.cs'] = ('using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;using SpriteMemoryManagement;'
        'namespace GloomhavenVR.WorldUI.Surfaces { internal sealed class StatPanelSurface {\n'
        + stat_source[stat_start:stat_end] + '\n}}')
    sources['StatPortraitPreparation.cs'] = (args.source_root / 'src/GloomhavenVR/WorldUI/Surfaces/StatPortraitPreparation.cs').read_text()
    wall = args.source_root / 'src/GloomhavenVR/Core/WallFade'
    generated = run / 'WallOwnershipReads.cs'
    subprocess.run([sys.executable, str(ROOT / 'tests/GloomhavenVR.WallReadFactsTests/extract-driver.py'),
                    str(wall / 'WallSegmentFade.cs'), str(generated),
                    str(wall / 'WallSegmentFade.Mounted.cs'), str(wall / 'WallSegmentFade.Prepare.cs'),
                    '--ownership-only'], check=True)
    sources.update({'WallOwnershipReads.cs': generated.read_text(),
                    'WallCommitGeometryReads.cs': (wall / 'WallCommitGeometryReads.cs').read_text(),
                    'WallSegmentFade.SelectionFacts.cs': (wall / 'WallSegmentFade.SelectionFacts.cs').read_text(),
                    'WallSegmentFadeCulprits.cs': (wall / 'WallSegmentFadeCulprits.cs').read_text(),
                    'ModVisualOwnership.cs': (args.source_root / 'src/GloomhavenVR/Core/ModVisualOwnership.cs').read_text()})
    shader = (args.source_root / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader').read_text()
    proof = {'root': str(args.source_root.resolve()), 'native_drake': str(args.native_drake_bundle.resolve()), 'sha256': {key: hashlib.sha256(value.encode()).hexdigest() for key, value in sources.items()}}
    for name in ('WallSegmentFade.cs', 'WallSegmentFade.CommitPhases.cs',
                 'WallSegmentFade.Prepare.cs', 'WallSegmentFade.Water.cs'):
        proof['sha256'][name] = hashlib.sha256((wall / name).read_bytes()).hexdigest()
    proof['sha256']['Overlay.shader'] = hashlib.sha256(shader.encode()).hexdigest()
    proof['sha256']['native_drake_bundle'] = hashlib.file_digest(args.native_drake_bundle.open('rb'), 'sha256').hexdigest()
    proof['sha256']['StatPanelSurface.cs'] = hashlib.sha256(stat_source.encode()).hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps(proof, indent=2) + '\n')
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('drop-native-alpha', 'FigureVisualMirror.cs', 'block.SetFloat(UseMask, 1f);', 'block.SetFloat(UseMask, 0f);', 'native alpha silhouette prevents rectangular'),
            ('drop-native-uv', 'FigureVisualMirror.cs', 'new Vector4(scale.x, scale.y, offset.x, offset.y)', 'new Vector4(1, 1, 0, 0)', 'native mask texture scale and offset'),
            ('freeze-ghost-phase', 'FigureVisualMirror.cs', 'pair.Copy.localRotation = pair.Source.localRotation;', '// frozen pose injected', 'evaluated animation phase remains exact'),
            ('revive-native-force-off', 'FigureVisualMirror.cs', 'pair.Copy.forceRenderingOff = pair.Source.forceRenderingOff;\n            }\n            if (!_home)', 'pair.Copy.forceRenderingOff = false;\n            }\n            if (!_home)', 'source cosmetic/LOD forceRenderingOff'),
            ('default-flying-pose', 'FigureVisualMirror.cs', 'pair.Copy.localRotation = pair.Source.localRotation;', 'pair.Copy.localRotation = Quaternion.identity;', 'sleeping ghost snapshot pixels'),
            ('native-controller-restart', 'FigureVisualMirror.cs', 'mirror.CaptureAnimatorPoses(source);', 'mirror.CaptureAnimatorPoses(source); root.AddComponent<Animator>();', 'ghost never owns native Animator'),
            ('highlight-force-off', 'FigureHighlight.cs', 'if (requireEnabled && (!r.enabled || r.forceRenderingOff))', 'if (requireEnabled && !r.enabled)', 'highlight admission respects source forceRenderingOff'),
            ('scroll-native-mask', 'Overlay.shader', 'o.maskUV = TRANSFORM_TEX(v.uv, _AlphaMaskTex);', 'o.maskUV = o.uv;', 'pulse scroll cannot displace native alpha UVs'),
            ('alpha-depth-no-clip', 'Overlay.shader', 'clip(mask - max(_AlphaMaskCutoff, 0.0001));', '// clip removed: depth rectangle', 'zero-alpha texels never stamp rectangular ghost depth'),
            ('skip-material-ready', 'FigureVisualMirror.cs', 'if (pair.Masks && ready && !pair.Ready)', 'if (false && pair.Masks && ready && !pair.Ready)', 'first material-ready edge refreshes ghost and depth cutout masks'),
            ('skip-inactive-tint', 'FigureOverlay.cs', 'if (!r.gameObject.activeInHierarchy) inactiveSkipped++;', 'if (!r.gameObject.activeInHierarchy) { inactiveSkipped++; continue; }', 'inactive native surface is tinted before it can activate'),
            ('revive-excluded-subtree', 'FigureVisualMirror.cs', 'pair.Copy == null || ModOwned(pair.Copy, transform)', 'pair.Copy == null', 'excluded mod subtree stays inactive in same-frame sync'),
            ('miss-mirror-wall-owner', 'WallSegmentFade.SelectionFacts.cs', 'renderer.GetComponentInParent<FigureVisualMirror>(true) != null', 'false', 'native-named ghost child keeps exact wall-census exemption'),
            ('broaden-native-wall-owner', 'WallSegmentFade.SelectionFacts.cs', 'renderer.GetComponentInParent<FigureVisualMirror>(true) != null', 'renderer.GetComponentInParent<ActorBehaviour>(true) != null', 'original native actor remains outside mod wall ownership'),
            ('exempt-wall-shader-mirror', 'WallOwnershipReads.cs', 'bool modExempt = f.Mod && !(f.Mesh != null && f.WallFadeShader);', 'bool modExempt = f.Mod;', 'real wall-shader mirror keeps conservative signature'),
            ('fold-dead-native-named-ghost', 'WallOwnershipReads.cs', 'bool holeExempt = SceneRowWasExemptWhenAlive(i);', 'bool holeExempt = false;', 'destroyed native-named ghost row retains exact exemption'),
            ('broaden-native-root-selection', 'WallSegmentFade.SelectionFacts.cs', 'renderer.GetComponent<HexSelect_Control>() != null', 'renderer.GetComponentInParent<HexSelect_Control>(true) != null', 'foreign emitter beneath native selector remains a conservative'),
            ('miss-native-root-selection', 'WallSegmentFade.SelectionFacts.cs', 'renderer.GetComponent<HexSelect_Control>() != null', 'bool.Parse("false")', 'exact native root selector emitter keeps its wall-census exemption'),
            ('ignore-actor-particles', 'WallSegmentFade.SelectionFacts.cs', 'return owner != null && owner.gameObject.activeInHierarchy;', 'return false;', 'native actor particles keep all wall signature halves unchanged'),
            ('admit-detached-particles', 'WallSegmentFade.SelectionFacts.cs', 'return owner != null && owner.gameObject.activeInHierarchy;', 'return (owner != null && owner.gameObject.activeInHierarchy) || !renderer.gameObject.activeSelf;', 'detached native particles restore conservative wall membership'),
            ('admit-actor-water', 'WallSegmentFade.SelectionFacts.cs', '!(renderer is ParticleSystemRenderer) || water', '!(renderer is ParticleSystemRenderer)', 'native actor water particles retain protection rects'),
            ('uncached-geometry', 'WallCommitGeometryReads.cs', 'if (Memo.TryGetValue(renderer, out Bounds bounds))', 'if (bool.Parse("false") && Memo.TryGetValue(renderer, out Bounds bounds))', 'five hundred repeated commit reads cross the native bounds boundary once'),
            ('room-resets-existing-ghosts', 'FigureInteractionPreparation.cs', 'internal static void BeginNewActors()\n    {', 'internal static void BeginNewActors()\n    {\n        Reset();', 'local and remote pickup reuse the same original visual cache'),
            ('portrait-renews-serial-wait', 'StatPreparation.cs', 'Cards.CardArtPin.PinReference(art.Reference);', '_portraitWaitUntil = Time.realtimeSinceStartup + 30f; Cards.CardArtPin.PinReference(art.Reference);', 'all missing original portraits share one absolute preparation deadline'),
            ('stale-prepared-pose', 'FigureInteractionPreparation.cs', 'entry.Mirror.RefreshPreparedPose(source);', '// stale loader pose injected', 'prepared acquire binds current sleeping pose'),
            ('reuse-changed-mesh', 'FigureInteractionPreparation.cs', '!entry.Mirror.MatchesPreparedSource(source)', 'false', 'changed original mesh invalidates prepared identity'),
            ('destroy-pooled-ghost', 'FigureInteractionPreparation.cs', 'Park(ghost); entry.InUse = false;', 'Park(ghost); Object.Destroy(ghost); entry.InUse = false;', 'room preparation preserves parked original ghosts and existing cache lifetime'),
            ('skip-stat-sprite-cache', 'StatPreparation.cs', 'Cards.CardFaceMipBake.ReplacementFor(art.Sprite);', '{ }', 'original stat sprite cache is ready before preview'),
            ('wrong-original-monster-model', 'StatPortraitPreparation.cs', 'string model = enemy.MonsterClass.DefaultModel;', 'string model = actor.Class.DefaultModel;', 'original monster and object portrait selectors'),
            ('skip-original-prop-override', 'StatPortraitPreparation.cs', 'model = obj.AttachedProp.PropHealthDetails.ActorSpriteName;', 'model = enemy.MonsterClass.DefaultModel;', 'original monster and object portrait selectors'),
            ('skip-original-portrait-queue', 'StatPreparation.cs', 'Cards.ScenarioCardPreparation.IncludeOriginalReference(portrait);', '{ }', 'original monster and object portrait selectors'),
            ('skip-late-original-portraits', 'StatPreparation.cs', 'if (!_portraitDiscoveryPending || UIInfoTools.Instance == null) return false;', 'if (!_portraitDiscoveryPending) return false;', 'late native UI resources keep one deferred portrait discovery job'),
            ('skip-inactive-material-release', 'FigureInteractionPreparation.cs', 'owner.Release();', '{ }', 'reset releases owned material of a prepared ghost that never activated'),
            ('skip-prepared-current-mask', 'FigureVisualMirror.cs', 'FigureOverlayMasks.Apply(pair.Source, pair.Copy, _materialScratch);', '// stale same-material mask injected', 'prepared acquire refreshes current blend weights and same-material native mask UVs'),
            ('reuse-changed-shader', 'FigureVisualMirror.cs', ' != slot.Shaders[i]', ' != (_materialScratch[i] != null ? _materialScratch[i].shader : null)', 'same material with changed shader invalidates prepared surface classification'),
            ('replace-immediate-held-ghost', 'FigureInteractionPreparation.cs', 'if (HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor)\n                || (Entries.TryGetValue(actor, out Entry active) && active.InUse)) return;', 'if (bool.Parse("false")) return;', 'pending preparation preserves an immediate live local pickup ghost'),
            ('cancel-destroys-active-ghost', 'FigureInteractionPreparation.cs', '_pending = Array.Empty<ActorBehaviour>(); _completed = 0; _begun = false;', 'Reset();', 'preparation timeout cancellation preserves an active hold and immediate input'),
            ('reuse-changed-lod', 'FigureVisualMirror.cs', 'if (table[i].screenRelativeTransitionHeight != lod.Table[i].screenRelativeTransitionHeight', 'if (bool.Parse("false")', 'changed native LOD transition invalidates prepared renderer table'),
        ]
    if args.case:
        missing=set(args.case)-{v[0] for v in variants}
        if missing: parser.error('Unknown cases: '+str(missing))
        variants=[v for v in variants if v[0] in args.case]
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    case_shaders = {}
    for name, filename, before, after, expected in variants:
        shader_code = shader
        if filename == 'Overlay.shader':
            if shader_code.count(before) != 1: raise SystemExit('Shader mutation binding drift: ' + name)
            shader_code = shader_code.replace(before, after, 1)
        build = run / name
        production = build / 'production'
        production.mkdir(parents=True)
        for path, code in sources.items():
            if path == filename:
                if code.count(before) != 1: raise SystemExit('Production mutation binding drift: ' + name)
                code = code.replace(before, after, 1)
            (production / path).write_text(code)
        project = build / 'Overlay.csproj'
        shutil.copyfile(fixture / 'Overlay.csproj', project)
        assembly = 'FigureOverlay_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(args.source_root / 'ressources/GH_Data/Managed')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a passing negative control')
        case_shaders[assembly] = shader_code
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2))
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    (project / 'Assets/Plugins').mkdir()
    for entry in manifest['cases']:
        destination = project / 'Assets/Plugins' / Path(entry['dll']).name
        shutil.copyfile(entry['dll'], destination)
        entry['dll'] = str(destination)
    manifest_path.write_text(json.dumps(manifest, indent=2))
    shutil.copyfile(fixture / 'Editor/OverlayRunner.cs', project / 'Assets/Editor/OverlayRunner.cs')
    for assembly, shader_code in case_shaders.items():
        (project / 'Assets' / ('Overlay_' + assembly + '.shader')).write_text(shader_code)
    for family in ('WallFade', 'Water_Shd'):
        (project / 'Assets' / ('Native' + family + '.shader')).write_text(
            'Shader "Fixture/' + family + '" { SubShader { Pass { } } }')
    (project / 'Assets/NativeCutout.shader').write_text('''Shader "Fixture/NativeCutout" { Properties { _Diffuse ("Diffuse", 2D)="white" {} _Cutoff("Cutoff",Float)=0.5 } SubShader { Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" } Pass { } } }''')
    dependencies = {'com.unity.modules.' + name: '1.0.0' for name in ('physics', 'cloth', 'animation', 'assetbundle', 'imageconversion', 'particlesystem')}
    dependencies['com.unity.ugui'] = '1.0.0'
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}) + '\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project), '-executeMethod', 'OverlayRunner.Start', '-interactionManifest', str(manifest_path), '-nativeDrakeBundle', str(args.native_drake_bundle.resolve()), '-evidenceRoot', str(run), '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=360)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see ' + str(run / 'unity.log'))
    print('PASS: ' + str(len(variants)) + ' production/negative variants; evidence: ' + str(run))


if __name__ == '__main__': main()
