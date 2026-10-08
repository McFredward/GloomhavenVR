#!/usr/bin/env python3
"""Run the complete production scenery classifier/discovery driver in Unity 2021.3.5."""
import argparse
import copy
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
    parser.add_argument('--bank-source-root', type=Path, help='Explicit frozen bank worker root; defaults to source-root')
    parser.add_argument('--catalog-source-root', type=Path, help='Explicit frozen catalog/assets root; defaults to source-root')
    parser.add_argument('--unitypy-python', type=Path, default=Path('/home/claw/unitypy-venv/bin/python'), help='Readonly native sibling exporter Python with UnityPy installed')
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/scenario-scenery-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--variant', action='append', help='Run production and only the named negative variant(s); partial coverage')
    parser.add_argument('--skip-production', action='store_true', help='Resume only --variant controls after production passed on the same source tree')
    args = parser.parse_args()
    if args.skip_production and not args.variant: parser.error('--skip-production requires --variant')
    if not args.unity.is_file(): parser.error('Real Unity 2021.3.5 is required; this proof cannot silently skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/scenario-scenery-runtime'
    base = args.source_root / 'src/GloomhavenVR/Core'
    bank_root = args.bank_source_root or args.source_root
    catalog_root = args.catalog_source_root or args.source_root
    input_files = [base/'Perf/ScenarioSceneryBudget.cs', base/'Perf/ScenarioArchitecturalDetailBudget.cs',
        base/'Perf/ScenarioDecorativePlacement.cs', base/'FigureRendererGuard.cs',
        bank_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshBank.cs',
        bank_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshStream.cs',
        catalog_root/'tools/environment-mesh/ornaments.json', catalog_root/'tools/environment-mesh/catalog.json.gz',
        catalog_root/'tools/environment-mesh/export-native.py', catalog_root/'tools/environment-mesh/roles.py',
        catalog_root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/index.json', Path(__file__).resolve(),
        *sorted(fixture.glob('*.cs')), fixture/'Scenery.csproj', fixture/'Editor/InteractionRunner.cs', fixture/'prepare-native-graphs.py']
    input_hashes = {str(path.resolve()):hashlib.sha256(path.read_bytes()).hexdigest() for path in input_files}
    (run/'input-source-hashes.json').write_text(json.dumps(input_hashes,indent=2)+'\n')
    scenery = (base / 'Perf/ScenarioSceneryBudget.cs').read_text()
    guard = (base / 'FigureRendererGuard.cs').read_text()
    sources = {'Placement.cs': (base / 'Perf/ScenarioDecorativePlacement.cs').read_text(), 'Scenery.cs': scenery.replace('Time.unscaledTime', 'SceneryClock.Now'), 'FigureGuard.cs': guard,
        'Architecture.cs': (base / 'Perf/ScenarioArchitecturalDetailBudget.cs').read_text()}
    sources['Bank.cs'] = (bank_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshBank.cs').read_text()
    sources['MeshStream.cs'] = (bank_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshStream.cs').read_text()
    # Only index delivery and the readonly game-root path are explicit boundaries.
    # The complete production bank reader still verifies current Unity metadata,
    # source SHA and native identity. No ornament/role verdict is replaced.
    index_lookup = 'TextAsset? asset = Asset("index.json");'
    assert sources['Bank.cs'].count(index_lookup) == 1, 'bank index delivery binding drift'
    sources['Bank.cs'] = sources['Bank.cs'].replace(index_lookup, 'TextAsset? asset = ArchitectureNativeFiles.Index();')
    native_root = 'Path.Combine(Application.streamingAssetsPath, "aa", "StandaloneWindows64", source.path)'
    assert sources['Bank.cs'].count(native_root) == 1, 'bank readonly source path binding drift'
    sources['Bank.cs'] = sources['Bank.cs'].replace(native_root, 'Path.Combine(ArchitectureNativeFiles.StreamingRoot, "aa", "StandaloneWindows64", source.path)')
    index_path = catalog_root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/index.json'
    ornaments_path = catalog_root/'tools/environment-mesh/ornaments.json'
    index = json.loads(index_path.read_text()); ornaments = json.loads(ornaments_path.read_text())
    actual = {entry['key']:entry for entry in index['entries']}
    assert {entry['key'] for entry in index['entries'] if entry.get('ornament')} == {entry['key'] for entry in ornaments['entries']}, 'bank positive ornament set must equal closed all-use catalog'
    rows=[]
    for ornament in ornaments['entries']:
        entry=actual[ornament['key']]
        assert entry.get('ornament') and entry['signature']==ornament['signature'] and entry['sources']==ornament['sources'], 'positive ornament catalog binding drift'
        exact=next(variant for variant in entry['variants'] if variant['tier']==100)
        mesh_file=index_path.parent/exact['file']
        assert hashlib.sha256(mesh_file.read_bytes()).hexdigest()==exact['sha256'], 'original ornament geometry hash drift'
        rows.append({'name':entry['signature']['name'],'mesh':str(mesh_file.resolve()),'readable':entry['signature']['readable'],
            'routes':[use['route'] for use in ornament['uses']]})
    core=next(entry for entry in index['entries'] if entry.get('role')=='structure' and not entry.get('ornament')
        and entry['signature']['name']=='CR_OS_Wall_01_Main')
    core_exact=next(variant for variant in core['variants'] if variant['tier']==100)
    core_mesh=index_path.parent/core_exact['file']
    assert hashlib.sha256(core_mesh.read_bytes()).hexdigest()==core_exact['sha256'], 'retained original core hash drift'
    graph_dir=run/'native-graphs'
    result=subprocess.run([str(args.unitypy_python), str(fixture/'prepare-native-graphs.py'),
        '--catalog-root',str(catalog_root),'--game-streaming-root',str((args.source_root/'ressources/GH_Data/StreamingAssets').resolve()),
        '--output',str(graph_dir)],capture_output=True,text=True)
    (run/'native-graph-export.log').write_text(result.stdout+result.stderr)
    if result.returncode: raise SystemExit(result.stdout+result.stderr)
    print(result.stdout,end='')
    floor=next(entry for entry in index['entries'] if entry.get('role')=='floor' and not entry.get('ornament')
        and entry['signature']['name']=='CR_ST_Floor_Basic_Half_01')
    floor_exact=next(variant for variant in floor['variants'] if variant['tier']==100)
    floor_mesh=index_path.parent/floor_exact['file']
    assert hashlib.sha256(floor_mesh.read_bytes()).hexdigest()==floor_exact['sha256'], 'retained original floor hash drift'
    fixture_index=copy.deepcopy(index)
    bad_provenance=copy.deepcopy(actual[ornaments['entries'][0]['key']])
    bad_provenance['key']='fixture-bad-provenance'; bad_provenance['signature']['name']='GloomhavenVR.Fixture.Ornament.BadSource'
    for source in bad_provenance['sources']: source['sha256']='0'*64
    fixture_index['entries'].append(bad_provenance)
    fixture_index_path=run/'index-with-negative-source-fixture.json'
    fixture_index_path.write_text(json.dumps(fixture_index))
    sources['ArchitectureNativeData.cs']='using System; using System.IO; using UnityEngine;\nnamespace GloomhavenVR.Core { internal static class ArchitectureNativeFiles {\n'+\
        'internal static readonly string StreamingRoot='+json.dumps(str((args.source_root/'ressources/GH_Data/StreamingAssets').resolve()))+';\n'+\
        'internal static TextAsset Index()=>new TextAsset(File.ReadAllText('+json.dumps(str(fixture_index_path.resolve()))+'));\n'+\
        'internal static readonly string Graphs='+json.dumps(str((graph_dir/'graphs.json').resolve()))+';\n'+\
        'internal static readonly string ProofOutput='+json.dumps(str((run/'architecture-coverage.json').resolve()))+';\n'+\
        'internal readonly struct Entry { internal readonly string Name,Mesh; internal readonly bool Readable; internal Entry(string n,string m,bool r){Name=n;Mesh=m;Readable=r;} }\n'+\
        'internal static readonly Entry[] Ornaments={'+','.join('new Entry('+json.dumps(row['name'])+','+json.dumps(row['mesh'])+','+str(row['readable']).lower()+')' for row in rows)+'};\n'+\
        'internal static readonly Entry Core=new Entry('+json.dumps(core['signature']['name'])+','+json.dumps(str(core_mesh.resolve()))+','+str(core['signature']['readable']).lower()+');\n'+\
        'internal static readonly Entry Floor=new Entry('+json.dumps(floor['signature']['name'])+','+json.dumps(str(floor_mesh.resolve()))+','+str(floor['signature']['readable']).lower()+');\n} }\n'
    metadata = json.loads((args.source_root/'tests/GloomhavenVR.ScenarioSceneryBudgetTests/NativeDetailProvenance.json').read_text())
    composite = sorted({row['mesh'] for row in metadata['scenery_review']['composite_dressing']})
    small = sorted({row['mesh'] for row in metadata['scenery_review']['small_dressing']})
    furniture = sorted({row['mesh'] for row in metadata['scenery_review']['retained_furniture_cores']})
    sources['NativeSceneryMetadata.cs'] = 'internal static class NativeSceneryMetadata { internal static readonly string[] CompositeMeshes = {' + ','.join(json.dumps(n) for n in composite) + '}; internal static readonly string[] SmallMeshes = {' + ','.join(json.dumps(n) for n in small) + '}; internal static readonly string[] FurnitureCores = {' + ','.join(json.dumps(n) for n in furniture) + '}; }'
    # Observe entry only; the complete production proof body still runs unchanged. This
    # distinguishes native fallthrough before proof from an expensive proof returning zero.
    proof_entry = 'internal static int DecorativeCategories(GameObject root)\n    {'
    if sources['Scenery.cs'].count(proof_entry) != 1: raise SystemExit('Template proof observer binding drift')
    sources['Scenery.cs'] = sources['Scenery.cs'].replace(proof_entry, proof_entry + '\n        SceneryTemplateProofProbe.Entries++;', 1)
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(args.source_root.resolve()), 'sha256': {key: hashlib.sha256(text.encode()).hexdigest() for key, text in sources.items()}, 'test_observers': ['deterministic unscaled clock', 'DecorativeCategories entry counter only']}, indent=2)+'\n')
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('captured-bay-rejected', 'Scenery.cs', 'if (CanOwnBayCollider(collider))\n                continue;', 'if (CanOwnBayCollider(collider) && false)\n                continue;', 'captured native bay admits every'),
            ('captured-bay-invisible-box', 'Scenery.cs', '{ collider.enabled = false; BayColliderOwners[collider] = true; }', '{ collider.enabled = true; BayColliderOwners[collider] = true; }', 'zero decoration removes original page book meshes and owned collision together'),
            ('captured-floor-segment-lost', 'Scenery.cs', '|| name.StartsWith("FR_Floor_Grass_Seg_", StringComparison.Ordinal)', '|| false', 'captured native edge keeps solid floor'),
            ('omit-creation-deferral', 'Placement.cs', 'if (categories == 0 || !Zero(categories)) return false;', 'if (categories == 0 || !Zero(categories) || true) return false;', 'creation prefix defers actual renderer collider'),
            ('all-positive-prefab-walk', 'Placement.cs', 'if (PerfConfig.ScenarioSceneryDensityPercentValue > 0\n            && PerfConfig.ScenarioVegetationDensityPercentValue > 0', 'if (PerfConfig.ScenarioSceneryDensityPercentValue <= 0\n            && PerfConfig.ScenarioVegetationDensityPercentValue > 0', 'all positive budgets retain native creation without template proof'),
            ('instantiate-zero-prefab', 'Placement.cs', 'var placement = new GameObject(template.name);', 'var placement = UnityEngine.Object.Instantiate(template);', 'creation prefix defers actual renderer collider'),
            ('lose-restore-pose', 'Placement.cs', 'visual.transform.localScale = Vector3.one;', 'visual.transform.localScale = recipe.Template.transform.localScale;', 'restoration reproduces original pose without'),
            ('omit-loading-completion', 'Scenery.cs', 'WalkNodes(loading: true, complete: true);', 'WalkNodes(loading: true, complete: false);', 'actual loading-close preparation drains'),
            ('skip-inactive-reveal', 'Scenery.cs', 'if (root != null) _driver?.PrepareSubtree(root);', 'if (root != null && root.activeInHierarchy) _driver?.PrepareSubtree(root);', 'room reveal prepares inactive'),
            ('deferred-only-restore-skipped', 'Scenery.cs', 'ScenarioDecorativePlacement.Refresh(complete: false);\n            if (!BudgetActive && _records.Count == 0 && _projectors.Count == 0)', 'if (!BudgetActive && _records.Count == 0) return;\n            ScenarioDecorativePlacement.Refresh(complete: false);\n            if (!BudgetActive && _records.Count == 0 && _projectors.Count == 0)', 'all 100 restores deferred-only'),
            ('native-parent-prop-deferred', 'Scenery.cs', '|| (native != null && native.PropObject != null)\n                || FigureRendererGuard.CarriesFigureComponent(t)', '|| false\n                || FigureRendererGuard.CarriesFigureComponent(t)', 'native parent PropObject identity protects'),
            ('unrepresented-parent-deferred', 'Scenery.cs', 'if (!facts.Represented)\n                for (int i = 0; i < facts.Colliders.Length; i++)', 'if (!facts.Represented && false)\n                for (int i = 0; i < facts.Colliders.Length; i++)', 'creation retains decoration representing an unrepresented'),
            # Bind the creation-provenance return, not the separate cosmetic-projector proof.
            ('generated-creation-provenance-lost', 'Scenery.cs', '        }\n        return generated;\n    }', '        }\n        return generated || true;\n    }', 'creation requires actual Generated Content provenance'),
            ('native-group-parent-deferred', 'Placement.cs', '_leafPlacement = childCount == 0;', '_leafPlacement = childCount <= 1;', 'native child-count group keeps original prefab'),
            ('unscoped-placement-deferred', 'Placement.cs', '!_insideObjectPlacement || !_leafPlacement || !VRSession.IsRunning', '!VRSession.IsRunning', 'unscoped creation cannot infer native leaf'),
            ('recipe-density-hash-changed', 'Scenery.cs', 'if (ScenarioDecorativePlacement.IsRestoredRoot(node)) continue;', 'if (ScenarioDecorativePlacement.IsRestoredRoot(node) && false) continue;', 'restored original leaf retains ordinary native density hash'),
            ('unknown-log-wall-admitted', 'Scenery.cs', '|| name.StartsWith("FR_Wall_Grassy_Verge_Thin_Log_", StringComparison.Ordinal)', '|| name.IndexOf("_Log", StringComparison.OrdinalIgnoreCase) >= 0', 'unproven solid log wall retains original wood geometry'),
            ('old-hex-generator-only', 'Scenery.cs', 'if (!reachedTile || !generated)', 'if (!reachedTile || !generated || unit == null || !unit.name.StartsWith("PCG_FR_Floor_Grass_Hex_", StringComparison.Ordinal))', 'hardware grass outside old Hex generator'),
            ('leaf-collider-hidden', 'Scenery.cs', 'if (blockingCollider)', 'if (blockingCollider && false)', 'disabled retained floor base cannot'),
            ('prop-grass-admitted', 'Scenery.cs', '|| t.GetComponent<ProceduralProp>() != null', '|| false', 'native prop beneath a tree stays protected'),
            ('miss-late-apparance', 'Scenery.cs', 'if (_wasLoading && !loading && BudgetActive)', 'if (_wasLoading && !loading && BudgetActive && false)', 'loading-complete edge discovers late'),
            ('foreign-force-restored', 'Scenery.cs', 'if (!record.Owned && !renderer.forceRenderingOff)', 'if (!record.Owned)', 'restoration clears owned masks'),
            ('active-procgen-only', 'Scenery.cs', '_inScenarioScene = VRSession.IsRunning;', '_inScenarioScene = VRSession.IsRunning && SceneManager.GetActiveScene().name == "ProcGen";', 'decoration budget is independent from grass'),
            ('root-sibling-missed', 'Scenery.cs', 'if (roots[i].GetComponent<ProceduralScenario>() != null)', 'if (roots[i].GetComponent<ProceduralScenario>() != null && false)', 'scene-root native scenario fallback'),
            ('late-material-missed', 'Scenery.cs', '_driver?.PrepareMaterialReady(mesh);\n            _driver?.QueueRenderer(mesh);', '{ /* negative: omit native material readiness */ }', 'late native material completion reclassifies'),
            ('late-floor-siblings-queued', 'Scenery.cs', 'PrepareSubtree(composite.gameObject);', 'QueueTile(TileAncestor(composite));', 'late solid floor masks already-ready shared grass synchronously'),
            ('structural-foliage-child-retained', 'Scenery.cs', 'if (foliage && !IsHardStructuralName(renderer.name))', 'if (foliage && !IsHardStructuralName(renderer.name) && false)', 'solid wall LOD represents'),
            ('anonymous-solid-lod-missed', 'Scenery.cs', 'if (RepresentsSolidComposite(member.transform, node))', 'if (IsStructuralName(member.name) || IsGrassBase(member.name))', 'original generic floor prefab collider is represented'),
            ('grass-still-capped', 'Scenery.cs', 'Kind.Grass => _density,', 'Kind.Grass => Math.Min(_density, _decorationDensity),', 'decoration budget is independent from grass'),
            ('tree-pillar-retained', 'Scenery.cs', 'if (IsNativeTreeAsset(name))\n            return false;', 'if (IsNativeTreeAsset(name))\n            return name.IndexOf("_Pillar_", StringComparison.OrdinalIgnoreCase) >= 0;', 'hard structural mesh identity'),
            ('tree-collider-left-on', 'Scenery.cs', 'collider.enabled = false;\n                owner.Owned = true;', 'collider.enabled = true;\n                owner.Owned = true;', 'zero vegetation removes complete native tree pillars'),
            ('mixed-tree-collider-owned', 'Scenery.cs', 'safe &= treeMember;', 'safe &= treeMember || true;', 'zero vegetation retains shared mixed-unit floor collision'),
            ('native-wall-plant-retained', 'Scenery.cs', 'bool foliageDressing = IsNativeWallPlantLeaf(name)', 'bool foliageDressing = false', 'captured detachable wall roots'),
            ('foreign-tree-collider-enabled', 'Scenery.cs', 'if (owner.Owned && collider != null && !collider.enabled)', 'if (collider != null && !collider.enabled)', 'vegetation 100 retains foreign disabled tree collision'),
            ('native-tree-inheritance-missed', 'Scenery.cs', 'if (treeCarrier != null)', 'if (treeCarrier != null && (unit == null || IsNativeTreeAsset(unit.name)))', 'hardware-equivalent 17-renderer native tree admits'),
            ('anonymous-tree-floor-admitted', 'Scenery.cs', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))))', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))) && false)', 'anonymous original floor mesh under tree keeps its solid identity'),
            ('completed-tree-wall-boundary-lost', 'Scenery.cs', 'return carrier; // outside an already complete tree, the wall/floor is its boundary', 'return null; // negative: surrounding wall incorrectly discards completed tree', 'completed native tree under mixed masonry wrapper remains optional'),

            ('held-full-scenery-scan', 'Scenery.cs', '_heldMeshChecks++;', '_heldMeshChecks += _records.Count;', 'holding one mesh examines one renderer'),
            ('remote-held-root-omitted', 'Scenery.cs', 'NetHeldProps.CopyVisualRoots(_heldRoots);', '/* negative: omit actual remote roots */', 'remote held decoration restores immediately'),

            ('detached-skull-retained', 'Scenery.cs', 'private static bool IsNativeCompositeDressing(string name) => NativeCompositeDressing.Contains(name);', 'private static bool IsNativeCompositeDressing(string name) => name.Length < 0 && NativeCompositeDressing.Contains(name);', 'original detached skull layer follows decoration'),
            ('unknown-skull-core-admitted', 'Scenery.cs', 'private static bool IsNativeCompositeDressing(string name) => NativeCompositeDressing.Contains(name);', 'private static bool IsNativeCompositeDressing(string name) => name.IndexOf("Skull", StringComparison.Ordinal) >= 0 || NativeCompositeDressing.Contains(name);', 'unlisted skull wall preserves real masonry'),
            ('cosmetic-projector-retained', 'Scenery.cs', 'projector.enabled = false;\n            record.Owned = true;', 'projector.enabled = true;\n            record.Owned = true;', 'decoration zero masks identified original paint projections'),
            ('foreign-projector-enabled', 'Scenery.cs', 'else if (!hide && record.Owned)', 'else if (!hide)', 'restoring decoration preserves foreign disabled paint projection'),
            ('generic-floor-collision-unrepresented', 'Scenery.cs', '|| IsNativeGroundCore(mesh.name)', '|| false', 'original generic floor prefab collider is represented'),
            ('clone-projector-rejected', 'Scenery.cs', 'if (original.EndsWith("(Clone)", StringComparison.Ordinal))', 'if (original.EndsWith("(Clone)", StringComparison.Ordinal) && false)', 'native projector clones and numeric duplicates qualify'),

            ('inert-shelf-animator-rejected', 'Scenery.cs', 'return animator != null && (!smallDressing || animator.runtimeAnimatorController != null);', 'return animator != null;', 'controller-less original shelf animation is inert'),
            ('active-shelf-controller-ignored', 'Scenery.cs', 'return animator != null && (!smallDressing || animator.runtimeAnimatorController != null);', 'return animator != null && !smallDressing;', 'active native controller still protects original shelf content'),
            ('small-global-figure-guard-lost', 'Scenery.cs', 'smallDressing ? !HasSmallDressingFigureAncestor(renderer)', 'smallDressing ? true', 'actual actor above native tile boundary retains exact small meshes'),
            ('large-unit-child-hidden', 'Scenery.cs', '|| IsLargeDecorativeUnit(t.name)\n                || (smallDressing', '|| false\n                || (smallDressing', 'large corpse cross and coffin preserve separately named pieces'),
            ('small-page-collision-retained', 'Scenery.cs', '&& !NativeSmallDressingContainers.Contains(OriginalAssetName(collider.name))', '&& true', 'pure original pages and shelf books admit their native decorative colliders'),
            ('unknown-small-callback-ignored', 'Scenery.cs', '|| (smallDressing && !generated && HasUnknownSmallDressingCallback(t))', '|| false', 'unknown native callbacks retain exact small decoration'),
            ('subtree-cache-retained', 'Scenery.cs', 'finally { _colliderFactsActive = false; ColliderReadFacts.Clear(); }', 'finally { _colliderFactsActive = false; ColliderReadFacts.Clear(); }', ''),
        ]
        variants += [
            ('architecture-unit-promotion-lost', 'Architecture.cs', '            unit = parent;', '            /* negative: no multipart promotion */', 'non-LOD multipart ornament promotes'),
            ('architecture-retained-core-lost', 'Architecture.cs', 'if (core == null) return false;', 'if (core == null) return true;', 'sole WallTop boundary without an actual retained body'),
            ('architecture-collider-core-lost', 'Architecture.cs', 'if (represented == null) return false;', 'if (represented == null) return true;', 'distant retained wall cannot represent a lone ornament collider'),
            ('architecture-whole-unit-proof-lost', 'Architecture.cs', 'if (!WholeOrnament(parent)) break;', 'if (!WholeOrnament(parent) && false) break;', 'required native body is never promoted'),
            ('architecture-light-boundary-lost', 'Architecture.cs', '|| node.GetComponent<Canvas>() != null || node.GetComponent<Light>() != null', '|| node.GetComponent<Canvas>() != null', 'native light ancestor protects ornamental'),
            ('architecture-native-prop-boundary-lost', 'Architecture.cs', '|| (original != null && original.PropObject != null)', '|| false', 'native PropObject protects positive ornament'),
            ('architecture-live-witness-lost', 'Scenery.cs', 'if (!core.IsCurrent()) { represented = false; break; }', 'if (!core.IsCurrent() && false) { represented = false; break; }', 'native core hidden after admission rescues'),
            ('architecture-retune-witness-lost', 'Scenery.cs', 'if (!core.IsCurrent()) { hide = false; record.Invalidated = true; break; }', 'if (!core.IsCurrent() && false) { hide = false; record.Invalidated = true; break; }', 'retuning retained records after core loss cannot create a new mask'),
            ('architecture-density-ignored', 'Scenery.cs', 'Kind.Architecture => _architectureDensity,', 'Kind.Architecture => 100,', 'architecture zero masks positive represented trim'),
            ('architecture-catalog-positive-flag-lost', 'Bank.cs', 'out Entry entry) && entry.ornament', 'out Entry entry)', 'actual SHA-backed native core is retained structure'),
            ('architecture-catalog-native-topology-lost', 'Bank.cs', '&& Matches(native, entry.signature) && SourceValid(entry);', '&& SourceValid(entry);', 'same-metadata native non-triangle topology remains unadmitted'),
            ('architecture-catalog-source-sha-lost', 'Bank.cs', '&& Matches(native, entry.signature) && SourceValid(entry);', '&& Matches(native, entry.signature);', 'ornament source SHA mismatch retains native visual'),
        ]
        variants = [v for v in variants if v[0] != 'subtree-cache-retained']
    if args.variant:
        known = {v[0] for v in variants}
        missing = set(args.variant) - known
        if missing: parser.error('Unknown negative variant(s): ' + ', '.join(sorted(missing)))
        variants = [v for v in variants if (v[0] == 'production' and not args.skip_production) or v[0] in args.variant]
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
    (run/'unity-exit-code.txt').write_text(str(result.returncode) + '\n')
    current_hashes={path:hashlib.sha256(Path(path).read_bytes()).hexdigest() for path in input_hashes}
    stable=current_hashes==input_hashes
    (run/'input-stability.json').write_text(json.dumps({'stable':stable,'current':current_hashes},indent=2)+'\n')
    if not stable: raise SystemExit('FAIL: source changed during the frozen Unity proof; rerun finalized source')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see '+str(run/'unity.log'))
    print('PASS: '+str(len(variants))+' complete production/negative variants; evidence: '+str(run))

if __name__=='__main__': main()
