#!/usr/bin/env python3
"""Portable full production Classify/ownership tests; Unity discovery proof is a separate local suite.

This compiles Classify and all of its real helper bodies, including actual transform-component
ancestry decisions. UnityGraph supplies an explicit semantic component graph for hosted CI. It
is not Unity and cannot establish render pixels or native engine lifetime. The independent
check-scenario-scenery-runtime.py runs the complete source/Driver in actual Unity 2021.3.5.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]

def block(source, signature):
    start=source.index(signature)
    brace=source.index('{',start)
    depth=1; index=brace+1
    while depth:
        if source[index]=='{': depth+=1
        elif source[index]=='}': depth-=1
        index+=1
    return source[start:index]

def expression(source,signature):
    start=source.index(signature);return source[start:source.index(';',start)+1]

def production(source):
    methods=[block(source,key) for key in (
        'private enum Verdict','private enum Kind','private sealed class Record',
        'private readonly struct ColliderFacts', 'private sealed class TreeColliderOwner',
        'private static void SetHidden(', 'private static void ClaimTreeColliders(', 'private static void ReleaseTreeColliders(', 'private static Verdict Classify(',
        'private static bool CanOwnBayCollider(', 'private static Collider[] BayCollidersFor(', 'private static void RefreshBayCollider(', 'internal static int DecorativeCategories(', 'private static bool HasUnrepresentedCollider(', 'private static bool CanOwnTreeCollider(', 'private static Transform? NativeTreeCarrier(', 'private static ColliderFacts ReadColliderFacts(',
        'private static bool UsesFoliage(', 'private static bool UsesOnlyFoliage(',
        'private static bool RepresentsSolidComposite(', 'private static Kind NamedKind(',
        'private static bool BlocksSmallDressingAnimation(', 'private static bool HasSmallDressingFigureAncestor(', 'private static bool HasUnknownSmallDressingCallback(', 'private static bool IsExactSmallSubtree(', 'private static string OriginalAssetName(',
        'private static bool IsHardStructuralName(', 'private static bool IsScenarioTile(',
    )]
    methods += [expression(source,key) for key in (
        'private static bool ShouldHide(', 'private static bool IsStructuralName(', 'private static bool IsGrassBase(',
        'private static bool IsNativeWallWoodLeaf(', 'private static bool IsNativeSceneryAsset(', 'private static bool IsNativeTreeAsset(', 'private static bool IsNativeWallPlantLeaf(', 'private static bool ColliderIsPresent(', 'private static string TreeAssetName(',
        'private static bool IsNativeCompositeDressing(', 'private static readonly HashSet<string> NativeCompositeDressing',
        'private static bool IsNativeGroundCore(', 'private static readonly HashSet<string> NativeGroundCores',
        'private static bool IsNativeSmallDressing(', 'private static bool IsLargeDecorativeUnit(',
        'private static readonly HashSet<string> NativeSmallDressing =',
        'private static readonly HashSet<string> NativeSmallDressingContainers',
        'private static readonly HashSet<string> NativeRetainedFurnitureCores',
    )]
    header='''using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.SceneManagement;
namespace GloomhavenVR.Core;
internal static partial class ScenarioSceneryBudget
{
private const string GrassShader="Amp_Basic_Foliage";
private static bool _colliderFactsActive=false;
private static readonly Dictionary<Transform,ColliderFacts> ColliderReadFacts=new();
private static readonly Dictionary<Collider,bool> TreeColliderReadFacts=new();
private static readonly Dictionary<Collider,TreeColliderOwner> TreeColliderOwners=new();
private static readonly Dictionary<Collider,bool> BayColliderOwners=new();
private static readonly Dictionary<Collider,bool> BayColliderReadFacts=new();
'''
    return header+'\n'.join(methods)+'\n}\n'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT);args=parser.parse_args()
    source=(args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioSceneryBudget.cs').read_text()
    architecture=(args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioArchitecturalDetailBudget.cs').read_text()
    fixture=ROOT/'tests/GloomhavenVR.ScenarioSceneryBudgetTests'
    metadata=json.loads((fixture/'NativeDetailProvenance.json').read_text())
    review=metadata['scenery_review']
    if (review['bundle_count'],review['renderer_count'],review['projector_count']) != (2144,47754,137):
        raise SystemExit('Original whole-game PCG renderer/projector census changed; review actual assets')
    if [n for n,_ in review['classification']] != metadata['scenery_names']:
        raise SystemExit('Every original PCG identity needs an explicit retained/candidate classification')
    catalog=set(re.findall(r'"([^"]+)"',expression(source,'private static readonly HashSet<string> NativeCompositeDressing')))
    originals={row['mesh'] for row in review['composite_dressing']}
    if catalog != originals or len(originals)!=64:
        raise SystemExit('Detached composite mesh admission must match independently reviewed original prefabs')
    mesh_values=','.join(json.dumps(n) for n in sorted(originals))
    ground={row['mesh'] for row in review['ground_cores']}
    if ground != set(re.findall(r'"([^"]+)"',expression(source,'private static readonly HashSet<string> NativeGroundCores'))) or len(ground)!=166:
        raise SystemExit('Shared ground collision representation must match original retained mesh records')
    ground_values=','.join(json.dumps(n) for n in sorted(ground))
    small={row['mesh'] for row in review['small_dressing']}
    if small != set(re.findall(r'"([^"]+)"',expression(source,'private static readonly HashSet<string> NativeSmallDressing ='))):
        raise SystemExit('Small dressing admission must match independently reread original mesh hierarchies')
    if any(not row.get('chain') or len(row.get('size',[]))!=3 for row in review['small_dressing']):
        raise SystemExit('Small dressing requires original component ancestry and bounds')
    for table, field in (('NativeSmallDressingContainers', 'small_dressing_containers'), ('NativeRetainedFurnitureCores', 'retained_furniture_cores')):
        records=review[field]
        original_set=set(records) if field=='small_dressing_containers' else {row['mesh'] for row in records}
        actual_set=set(re.findall(r'"([^"]+)"',expression(source,'private static readonly HashSet<string> '+table)))
        if original_set != actual_set:
            raise SystemExit('Native small container/retained core proof drift: '+table)
    small_values=','.join(json.dumps(n) for n in sorted(small))
    variants=[('production',source,'')]
    for name,old,new,expected in (
        ('captured-bay-rejected','if (CanOwnBayCollider(collider))\n                continue;','if (CanOwnBayCollider(collider) && false)\n                continue;','captured native bay admits every'),
        ('captured-bay-invisible-box','{ collider.enabled = false; BayColliderOwners[collider] = true; }','{ collider.enabled = true; BayColliderOwners[collider] = true; }','zero captured bay removes shared'),
        ('captured-floor-segment-lost','|| name.StartsWith("FR_Floor_Grass_Seg_", StringComparison.Ordinal)','|| false','captured edge keeps original solid floor segment'),
        ('old-generator-only','if (!reachedTile || !generated)','if (!reachedTile || !generated || unit == null || !unit.name.StartsWith("PCG_FR_Floor_Grass_Hex_", StringComparison.Ordinal))','hardware roots grass'),
        ('leaf-collider-invisible','if (blockingCollider)','if (blockingCollider && false)','disabled retained base'),
        ('native-prop-admitted','|| t.GetComponent<ProceduralProp>() != null','|| false','gameplay obstacle grass'),
        ('structural-foliage-hidden','(!dedicatedStructuralFoliage && IsHardStructuralName(assetName))','(!dedicatedStructuralFoliage && IsHardStructuralName(assetName) && false)','structural geometry beats foliage shader'),
        ('foreign-mask-restored','if (!record.Owned && !renderer.forceRenderingOff)','if (!record.Owned)','foreign renderer mask cannot'),
        ('root-sibling-missed','if (roots[i].GetComponent<ProceduralScenario>() != null)','if (roots[i].GetComponent<ProceduralScenario>() != null && false)','native scene-root scenario fallback'),
        ('structural-foliage-child-retained','if (foliage && !IsHardStructuralName(renderer.name))','if (foliage && !IsHardStructuralName(renderer.name) && false)','solid wall LOD represents'),
        ('anonymous-solid-lod-missed','if (RepresentsSolidComposite(member.transform, node))','if (IsStructuralName(member.name) || IsGrassBase(member.name))','solid wall LOD represents'),
        ('tree-pillar-retained','if (IsNativeTreeAsset(name))\n            return false;','if (IsNativeTreeAsset(name))\n            return name.IndexOf("_Pillar_", StringComparison.OrdinalIgnoreCase) >= 0;','solid tree pillar trunk is vegetation'),
        ('tree-collider-left-on','collider.enabled = false;\n                owner.Owned = true;','collider.enabled = true;\n                owner.Owned = true;','zero tree masks own and suppress'),
        ('mixed-tree-collider-owned','safe &= treeMember;','safe &= treeMember || true;','tree composite with a solid floor cannot'),
        ('native-wall-plant-retained','bool foliageDressing = IsNativeWallPlantLeaf(name)','bool foliageDressing = false','captured underwall roots'),
        ('native-tree-inheritance-missed', 'if (treeCarrier != null)', 'if (treeCarrier != null && (unit == null || IsNativeTreeAsset(unit.name)))', 'hardware named native tree assembly admits'),
        ('anonymous-tree-floor-admitted', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))))', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))) && false)', 'anonymous original floor mesh under tree keeps its solid identity'),
        ('completed-tree-wall-boundary-lost', 'return carrier; // outside an already complete tree, the wall/floor is its boundary', 'return null; // negative: surrounding wall incorrectly discards completed tree', 'completed native tree under mixed masonry wrapper remains optional'),
        ('small-inert-animator-rejected', 'return animator != null && (!smallDressing || animator.runtimeAnimatorController != null);',
            'return animator != null;', 'controller-less original shelf animator is inert'),
        ('active-controller-ignored', 'return animator != null && (!smallDressing || animator.runtimeAnimatorController != null);',
            'return animator != null && !smallDressing;', 'active native controller protects small shelf mesh'),
        ('small-global-figure-guard-lost', 'smallDressing ? !HasSmallDressingFigureAncestor(renderer)',
            'smallDressing ? true', 'actual actor above tile retains exact small mesh'),
        ('large-unit-child-hidden', '|| IsLargeDecorativeUnit(t.name)\n                || (smallDressing',
            '|| false\n                || (smallDressing', 'large corpse and cross preserve their separately named small pieces'),
        ('small-collision-left-on', '{ collider.enabled = false; BayColliderOwners[collider] = true; }',
            '{ collider.enabled = collider.name == "FR_Default_Bay_10" ? false : true; BayColliderOwners[collider] = true; }',
            'pure original page clutter owns its decorative box'),

    ):
        if source.count(old)!=1:raise SystemExit('Classifier mutation binding drift: '+name)
        variants.append((name,source.replace(old,new,1),expected))
    dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet')
    env=dict(os.environ,DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    with tempfile.TemporaryDirectory(prefix='ghvr-scenery-classify-')as temp:
        folder=Path(temp)
        (folder/'Classifier.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649</NoWarn></PropertyGroup></Project>')
        shutil.copyfile(fixture/'UnityGraph.cs',folder/'UnityGraph.cs');shutil.copyfile(fixture/'Checks.cs',folder/'Checks.cs')
        (folder/'NativeSceneryMetadata.cs').write_text('internal static class NativeSceneryMetadata { internal static readonly string[] CompositeMeshes = {'+mesh_values+'}; internal static readonly string[] GroundMeshes = {'+ground_values+'}; internal static readonly string[] SmallMeshes = {'+small_values+'}; }')
        shutil.copyfile(args.source_root/'src/GloomhavenVR/Core/FigureRendererGuard.cs',folder/'FigureGuard.cs')
        (folder/'Architecture.cs').write_text(architecture)
        for name,text,expected in variants:
            (folder/'Production.cs').write_text(production(text))
            run=subprocess.run([dotnet,'run','--project',str(folder/'Classifier.csproj'),'-c','Release','--no-launch-profile'],env=env,capture_output=True,text=True)
            output=run.stdout+run.stderr
            if not expected:
                if run.returncode:raise SystemExit(output)
                print(run.stdout,end='')
            elif run.returncode==0 or 'error CS' in output or expected not in output:
                raise SystemExit('Negative control failed to reach intended runtime defect: '+name+'\n'+output)
            else:print('Scenario classifier negative control: '+name+' failed as expected')
    print('Scenario scenery: full production Classify graph and '+str(len(variants)-1)+' runtime negative controls passed; actual Unity lifecycle tested separately')

if __name__=='__main__':main()
