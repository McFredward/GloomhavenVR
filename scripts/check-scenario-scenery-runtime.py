#!/usr/bin/env python3
"""Run the complete production scenery classifier/discovery driver in Unity 2021.3.5."""
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/scenario-scenery-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    if not args.unity.is_file(): parser.error('Real Unity 2021.3.5 is required; this proof cannot silently skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/scenario-scenery-runtime'
    base = args.source_root / 'src/GloomhavenVR/Core'
    scenery = (base / 'Perf/ScenarioSceneryBudget.cs').read_text()
    guard = (base / 'FigureRendererGuard.cs').read_text()
    sources = {'Scenery.cs': scenery.replace('Time.unscaledTime', 'SceneryClock.Now'), 'FigureGuard.cs': guard}
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(args.source_root.resolve()), 'sha256': {key: hashlib.sha256(text.encode()).hexdigest() for key, text in sources.items()}}, indent=2)+'\n')
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('old-hex-generator-only', 'Scenery.cs', 'if (!reachedTile || !generated)', 'if (!reachedTile || !generated || unit == null || !unit.name.StartsWith("PCG_FR_Floor_Grass_Hex_", StringComparison.Ordinal))', 'hardware grass outside old Hex generator'),
            ('leaf-collider-hidden', 'Scenery.cs', 'if (blockingCollider)', 'if (blockingCollider && false)', 'disabled retained floor base cannot'),
            ('prop-grass-admitted', 'Scenery.cs', '|| t.GetComponent<ProceduralProp>() != null', '|| false', 'native prop beneath a tree stays protected'),
            ('miss-late-apparance', 'Scenery.cs', 'if (_wasLoading && !loading && BudgetActive)', 'if (_wasLoading && !loading && BudgetActive && false)', 'loading-complete edge discovers late'),
            ('foreign-force-restored', 'Scenery.cs', 'if (!record.Owned && !renderer.forceRenderingOff)', 'if (!record.Owned)', 'restoration clears owned masks'),
            ('active-procgen-only', 'Scenery.cs', '_inScenarioScene = VRSession.IsRunning;', '_inScenarioScene = VRSession.IsRunning && SceneManager.GetActiveScene().name == "ProcGen";', 'decoration budget is independent from grass'),
            ('root-sibling-missed', 'Scenery.cs', 'if (roots[i].GetComponent<ProceduralScenario>() != null)', 'if (roots[i].GetComponent<ProceduralScenario>() != null && false)', 'scene-root native scenario fallback'),
            ('late-material-missed', 'Scenery.cs', '_driver?.QueueRenderer(mesh);', '{ /* negative: omit native material readiness */ }', 'late native material completion reclassifies'),
            ('structural-foliage-child-retained', 'Scenery.cs', 'if (foliage && !IsHardStructuralName(renderer.name))', 'if (foliage && !IsHardStructuralName(renderer.name) && false)', 'solid wall LOD represents'),
            ('anonymous-solid-lod-missed', 'Scenery.cs', 'if (RepresentsSolidComposite(member.transform, node))', 'if (IsStructuralName(member.name) || IsGrassBase(member.name))', 'solid wall LOD represents'),
            ('grass-still-capped', 'Scenery.cs', 'Kind.Grass => _density,', 'Kind.Grass => Math.Min(_density, _decorationDensity),', 'decoration budget is independent from grass'),
            ('tree-pillar-retained', 'Scenery.cs', 'if (IsNativeTreeAsset(name))\n            return false;', 'if (IsNativeTreeAsset(name))\n            return name.IndexOf("_Pillar_", StringComparison.OrdinalIgnoreCase) >= 0;', 'hard structural mesh identity'),
            ('tree-collider-left-on', 'Scenery.cs', 'collider.enabled = false;', 'collider.enabled = true;', 'zero vegetation removes complete native tree pillars'),
            ('mixed-tree-collider-owned', 'Scenery.cs', 'safe &= treeMember;', 'safe &= treeMember || true;', 'zero vegetation retains shared mixed-unit floor collision'),
            ('native-wall-plant-retained', 'Scenery.cs', 'bool foliageDressing = IsNativeWallPlantLeaf(name)', 'bool foliageDressing = false', 'hardware wall plant leaf'),
            ('foreign-tree-collider-enabled', 'Scenery.cs', 'if (owner.Owned && collider != null && !collider.enabled)', 'if (collider != null && !collider.enabled)', 'vegetation 100 retains foreign disabled tree collision'),
            ('native-tree-inheritance-missed', 'Scenery.cs', 'if (treeCarrier != null)', 'if (treeCarrier != null && (unit == null || IsNativeTreeAsset(unit.name)))', 'hardware-equivalent 17-renderer native tree admits'),
            ('anonymous-tree-floor-admitted', 'Scenery.cs', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))))', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))) && false)', 'anonymous original floor mesh under tree keeps its solid identity'),
            ('completed-tree-wall-boundary-lost', 'Scenery.cs', 'return carrier; // outside an already complete tree, the wall/floor is its boundary', 'return null; // negative: surrounding wall incorrectly discards completed tree', 'completed native tree under mixed masonry wrapper remains optional'),

            ('subtree-cache-retained', 'Scenery.cs', 'finally { _colliderFactsActive = false; ColliderReadFacts.Clear(); }', 'finally { _colliderFactsActive = false; ColliderReadFacts.Clear(); }', ''),
        ]
        variants = [v for v in variants if v[0] != 'subtree-cache-retained']
    manifest = {'result': str(run/'results.txt'), 'cases': []}
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        for path, text in sources.items():
            if path == filename:
                if text.count(before) != 1: raise SystemExit('Production mutation binding drift: '+name)
                text = text.replace(before, after, 1)
            (production/path).write_text(text)
        project=build/'Scenery.csproj'; shutil.copyfile(fixture/'Scenery.csproj', project)
        assembly='ScenarioScenery_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path=run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2))
    project=run/'unity'; (project/'Assets/Editor').mkdir(parents=True); (project/'Packages').mkdir(); (project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/InteractionRunner.cs',project/'Assets/Editor/InteractionRunner.cs')
    (project/'Assets/Foliage.shader').write_text('Shader "Amp_Basic_Foliage" { SubShader { Pass { } } }\n')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result=subprocess.run([str(args.unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','InteractionRunner.Start','-interactionManifest',str(manifest_path),'-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    report=Path(manifest['result'])
    if report.is_file(): print(report.read_text(),end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see '+str(run/'unity.log'))
    print('PASS: '+str(len(variants))+' complete production/negative variants; evidence: '+str(run))

if __name__=='__main__': main()
