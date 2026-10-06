#!/usr/bin/env python3
"""Run the complete production scenario-figure quality driver in Unity 2021.3.5."""
import argparse
import hashlib
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/scenario-figure-detail-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--case', action='append', help='Run only a named production/negative case; repeat for focused follow-up')
    args = parser.parse_args()
    if not args.unity.is_file(): parser.error('Real Unity 2021.3.5 is required; this proof cannot silently skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/scenario-figure-detail-runtime'
    base = args.source_root / 'src/GloomhavenVR/Core'
    figures = (base / 'Perf/ScenarioFigureDetailBudget.cs').read_text()
    sources = {'Figures.cs': figures.replace('Time.unscaledTime', 'FigureClock.Now')}
    sources['Effects.cs'] = (base / 'Perf/ScenarioFigureEffects.cs').read_text()
    sources['Distance.cs'] = (base / 'Perf/FigureDistanceLodPolicy.cs').read_text()
    sources['Skinning.cs'] = (base / 'Perf/FigureSkinningBudget.cs').read_text()
    sources['MeshBank.cs'] = (base / 'Perf/ScenarioFigureMeshBank.cs').read_text()
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(args.source_root.resolve()), 'sha256': {key: hashlib.sha256(text.encode()).hexdigest() for key, text in sources.items()}}, indent=2)+'\n')
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('report-unapplied-ready', 'Figures.cs', '&& _pending.Count == 0 && _measurementApplied', '&& _pending.Count == 0 && (_measurementApplied || !_measurementApplied)', 'cannot become measurement-ready before its late pass'),
            ('swap-native-cloth-topology', 'Figures.cs', '&& renderer.GetComponent<Cloth>() == null)', ')', 'large native Cloth topology is never admitted'),
            ('override-local-detail', 'Figures.cs', 'int wanted = restore ? 100 :', 'int wanted = restore || HeldFigures.Owns(Actor) ? 100 :', 'player detail controls only player'),
            ('revive-held-cloth', 'Figures.cs', 'bool cloth = restore || PerfConfig.FigureClothSimulationEnabled;', 'bool cloth = restore || NetHeldFigures.Owns(Actor) || PerfConfig.FigureClothSimulationEnabled;', 'remote hold keeps configured'),
            ('admit-empty-lod', 'Figures.cs', 'if (coarse > 0 && coarse < full) admitted.Add(i);', 'if (coarse < full) admitted.Add(i);', 'empty far-cull LOD'),
            ('overwrite-foreign-table', 'Figures.cs', 'if (!SameTable(current, Applied ?? Original))', 'if (!SameTable(current, Applied ?? Original) && PerfConfig.PlayerFigureDetailPercent < 0)', 'foreign LOD controller table'),
            ('miss-steady-ownership', 'Figures.cs', 'if (Group == null || !SameTable(Group.GetLODs(), Applied))', 'if (Group == null)', 'idle mask reader immediately relinquishes a foreign native LOD table'),
            ('foreign-idle-mask-owned', 'Figures.cs', '|| !_masked.Contains(renderer)) return false;\n            if (verified == null || verified.Add(this)) CheckOwnership();\n            return !Foreign && Applied != null && _masked.Contains(renderer) && renderer.forceRenderingOff;', '|| false) return false;\n            if (verified == null || verified.Add(this)) CheckOwnership();\n            return !Foreign && Applied != null && renderer.forceRenderingOff;', 'idle mask reader never treats a foreign mask'),
            ('idle-mask-reader-always-rejects', 'Figures.cs', 'if (lod.OwnsMask(renderer, _maskReadDepth > 0 ? _maskReadVerified : null)) return true;', 'if (false && lod.OwnsMask(renderer, _maskReadDepth > 0 ? _maskReadVerified : null)) return true;', 'idle mask reader recognizes only omitted'),
            ('idle-mask-scope-leaks', 'Figures.cs',
             'if (_maskReadDepth++ == 0) _maskReadVerified.Clear();\n            _finishMaskRead ??= EndLodMaskRead;\n            return new LodMaskReadScope(_finishMaskRead);\n        }\n        private void EndLodMaskRead()\n        {\n            if (--_maskReadDepth == 0) _maskReadVerified.Clear();',
             '_maskReadDepth++;\n            _finishMaskRead ??= EndLodMaskRead;\n            return new LodMaskReadScope(_finishMaskRead);\n        }\n        private void EndLodMaskRead()\n        {\n            _maskReadDepth--;',
             'idle mask reader immediately relinquishes a foreign native LOD table'),
            ('cancel-native-reset', 'Figures.cs', 'if (!forcingPosition && !Cloth.enabled) Cloth.enabled = true;', 'if (!Cloth.enabled) Cloth.enabled = true;', 'native cloth teleport reset'),
            ('admit-map-actors', 'Figures.cs', 'if (!IsScenarioActorRoot(root, scene))', 'if (!IsScenarioActorRoot(root, scene) && PerfConfig.PlayerFigureDetailPercent < 0)', 'map models and immersive NPCs'),
            ('reject-native-game-board', 'Figures.cs', 'if (board != null && root.transform.IsChildOf(board.transform)) return true;', 'if (board != null && root.transform.IsChildOf(board.transform)) return false;', 'original enabled native cloth solvers'),
            ('forget-disabled-cook-claim', 'Figures.cs', 'item.Owned |= FigureCloth.TakeDisabledSimulationOwnership(item.Cloth);', 'item.Owned |= false;', 'rescale cook original-enable claim'),
            ('skip-shutdown-restoration', 'Figures.cs', 'foreach (ActorRecord record in _actors)\n            {\n                try { record.Apply(true); }', 'foreach (ActorRecord record in _actors)\n            {\n                try { if (_faulted) record.Apply(true); }', 'VR off restores exact original'),
            ('ignore-fx-only-setting', 'Figures.cs', '|| PerfConfig.FigureEffectsDensityPercent < 100', '|| PerfConfig.FigureEffectsDensityPercent < 0', 'FX-only activation pauses identified'),
            ('skip-particle-mask', 'Effects.cs', 'Mask?.Apply(reduce);', 'Mask?.Apply(false);', 'ambient particles produce no rendered pixels'),
            ('keep-particle-solver-running', 'Effects.cs', 'System.Pause(false); Paused = true;', 'Paused = false;', 'FX-only activation pauses identified'),
            ('revive-originally-stopped-fx', 'Effects.cs', 'if (!reduce && !Paused) return;', 'if (!reduce && !Paused) { System.Play(false); return; }', 'originally stopped and paused effects remain'),
            ('keep-alpha-shell', 'Effects.cs', 'foreach (Mask item in _shells) item.Apply(index++ >= keep);', 'foreach (Mask item in _shells) item.Apply(false);', 'late native alpha material completion admits'),
            ('admit-pooled-ability-under-idle', 'Effects.cs', 'if (name.StartsWith("P_", StringComparison.Ordinal)', 'if (false', 'pooled ability under Idle remains'),
            ('admit-unverified-alpha-body', 'Effects.cs', 'else return false;', 'else return true;', 'only original native body is admitted'),
            ('lose-fx-restoration', 'Effects.cs', 'internal void Restore() => Apply(100);', 'internal void Restore() => Apply(0);', 'LivingSpirit mesh and native particle state restore at original quality'),
            ('ignore-late-native-material', 'Figures.cs', 'if (renderer != null && renderer.enabled) ScenarioFigureDetailBudget.MaterialReady(renderer);', 'if (renderer != null && renderer.enabled && PerfConfig.FigureEffectsDensityPercent < 0) ScenarioFigureDetailBudget.MaterialReady(renderer);', 'late native alpha material completion admits'),
            ('reject-native-instance-material', 'Effects.cs', 'else if (name.EndsWith("(Instance)", StringComparison.Ordinal))', 'else if (name.EndsWith("(NotNativeInstance)", StringComparison.Ordinal))', 'late native alpha material completion admits'),
            ('adopt-incomplete-native-material', 'Figures.cs', 'if (renderer != null && renderer.enabled) ScenarioFigureDetailBudget.MaterialReady(renderer);', 'if (renderer != null) ScenarioFigureDetailBudget.MaterialReady(renderer);', 'unfinished native material load cannot adopt'),
            ('omit-spirit-catalog', 'Effects.cs', '["MO_LivingSpirit_PR"] = new', '["MO_LivingSpirit_NotOriginal"] = new', 'complete original prefab family suppresses resident cosmetics: MO_LivingSpirit_PR'),
            ('retain-spirit-mesh', 'Effects.cs', 'if (!supplemental && !KnownAmbientMesh(renderer, root.transform)) return false;', 'if (!supplemental) return false;', 'LivingSpirit original mesh eye bands obey zero density'),
            ('pause-native-callbacks', 'Effects.cs', 'if (MayPause && System.isPlaying && !System.isPaused)', 'if (System.isPlaying && !System.isPaused)', 'native callbacks collision and card UI are excluded from ambient suppression'),
            ('admit-unknown-mesh-material', 'Effects.cs', '&& OriginalName(materials[0].name) == material)', '&& materials[0] != null)', 'LivingSpirit mesh name alone cannot suppress unknown material/body geometry'),
        ]
    if args.case:
        selected = set(args.case)
        unknown = selected - {case[0] for case in variants}
        if unknown: parser.error('Unknown case: '+', '.join(sorted(unknown)))
        variants = [case for case in variants if case[0] in selected]
    manifest = {'result': str(run/'results.txt'), 'cases': []}
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        for path, text in sources.items():
            if path == filename:
                if text.count(before) != 1: raise SystemExit('Production mutation binding drift: '+name)
                text = text.replace(before, after, 1)
            if name == 'skip-shutdown-restoration' and path == 'Figures.cs':
                text = text.replace('try { lod.Restore(); } catch', 'try { if (_faulted) lod.Restore(); } catch', 1)
            (production/path).write_text(text)
        project=build/'Figures.csproj'; shutil.copyfile(fixture/'Figures.csproj', project)
        assembly='ScenarioFigureDetail_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path=run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2))
    project=run/'unity'; (project/'Assets/Editor').mkdir(parents=True); (project/'Packages').mkdir(); (project/'ProjectSettings').mkdir()
    shutil.copyfile(ROOT/'scripts/scenario-scenery-runtime/Editor/InteractionRunner.cs',project/'Assets/Editor/InteractionRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.cloth":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    # Actual native particle/alpha render proof needs a graphics device. Use a private
    # virtual display and Mesa's software OpenGL; never quietly fall back to -nographics.
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','InteractionRunner.Start','-interactionManifest',str(manifest_path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):
        xvfb=shutil.which('xvfb-run')
        if not xvfb: parser.error('xvfb-run is required for the real render proof on a headless machine')
        command=[xvfb,'-a','-s','-screen 0 640x480x24']+command
    result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=300)
    report=Path(manifest['result'])
    if report.is_file(): print(report.read_text(),end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see '+str(run/'unity.log'))
    print('PASS: '+str(len(variants))+' complete production/negative variants; evidence: '+str(run))

if __name__=='__main__': main()
