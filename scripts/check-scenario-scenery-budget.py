#!/usr/bin/env python3
"""Portable full production Classify/ownership tests; Unity discovery proof is a separate local suite.

This compiles Classify and all of its real helper bodies, including actual transform-component
ancestry decisions. UnityGraph supplies an explicit semantic component graph for hosted CI. It
is not Unity and cannot establish render pixels or native engine lifetime. The independent
check-scenario-scenery-runtime.py runs the complete source/Driver in actual Unity 2021.3.5.
"""
import argparse
import os
from pathlib import Path
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
        'private static bool HasUnrepresentedCollider(', 'private static bool CanOwnTreeCollider(', 'private static Transform? NativeTreeCarrier(', 'private static ColliderFacts ReadColliderFacts(',
        'private static bool UsesFoliage(', 'private static bool UsesOnlyFoliage(',
        'private static bool RepresentsSolidComposite(', 'private static Kind NamedKind(',
        'private static bool IsHardStructuralName(', 'private static bool IsScenarioTile(',
    )]
    methods += [expression(source,key) for key in (
        'private static bool ShouldHide(', 'private static bool IsStructuralName(', 'private static bool IsGrassBase(',
        'private static bool IsNativeSceneryAsset(', 'private static bool IsNativeTreeAsset(', 'private static bool IsNativeWallPlantLeaf(', 'private static bool ColliderIsPresent(', 'private static string TreeAssetName(',
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
'''
    return header+'\n'.join(methods)+'\n}\n'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT);args=parser.parse_args()
    source=(args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioSceneryBudget.cs').read_text()
    fixture=ROOT/'tests/GloomhavenVR.ScenarioSceneryBudgetTests'
    variants=[('production',source,'')]
    for name,old,new,expected in (
        ('old-generator-only','if (!reachedTile || !generated)','if (!reachedTile || !generated || unit == null || !unit.name.StartsWith("PCG_FR_Floor_Grass_Hex_", StringComparison.Ordinal))','hardware roots grass'),
        ('leaf-collider-invisible','if (blockingCollider)','if (blockingCollider && false)','disabled retained base'),
        ('native-prop-admitted','|| t.GetComponent<ProceduralProp>() != null','|| false','gameplay obstacle grass'),
        ('structural-foliage-hidden','(!dedicatedStructuralFoliage && IsHardStructuralName(assetName))','(!dedicatedStructuralFoliage && IsHardStructuralName(assetName) && false)','structural geometry beats foliage shader'),
        ('foreign-mask-restored','if (!record.Owned && !renderer.forceRenderingOff)','if (!record.Owned)','foreign renderer mask cannot'),
        ('root-sibling-missed','if (roots[i].GetComponent<ProceduralScenario>() != null)','if (roots[i].GetComponent<ProceduralScenario>() != null && false)','native scene-root scenario fallback'),
        ('structural-foliage-child-retained','if (foliage && !IsHardStructuralName(renderer.name))','if (foliage && !IsHardStructuralName(renderer.name) && false)','solid wall LOD represents'),
        ('anonymous-solid-lod-missed','if (RepresentsSolidComposite(member.transform, node))','if (IsStructuralName(member.name) || IsGrassBase(member.name))','solid wall LOD represents'),
        ('tree-pillar-retained','if (IsNativeTreeAsset(name))\n            return false;','if (IsNativeTreeAsset(name))\n            return name.IndexOf("_Pillar_", StringComparison.OrdinalIgnoreCase) >= 0;','solid tree pillar trunk is vegetation'),
        ('tree-collider-left-on','collider.enabled = false;','collider.enabled = true;','zero tree masks own and suppress'),
        ('mixed-tree-collider-owned','safe &= treeMember;','safe &= treeMember || true;','tree composite with a solid floor cannot'),
        ('native-wall-plant-retained','bool foliageDressing = IsNativeWallPlantLeaf(name)','bool foliageDressing = false','hardware named wall plant leaf'),
        ('native-tree-inheritance-missed', 'if (treeCarrier != null)', 'if (treeCarrier != null && (unit == null || IsNativeTreeAsset(unit.name)))', 'hardware named native tree assembly admits'),
        ('anonymous-tree-floor-admitted', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))))', 'if (IsNativeSceneryAsset(mesh.name)\n            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))) && false)', 'anonymous original floor mesh under tree keeps its solid identity'),
        ('completed-tree-wall-boundary-lost', 'return carrier; // outside an already complete tree, the wall/floor is its boundary', 'return null; // negative: surrounding wall incorrectly discards completed tree', 'completed native tree under mixed masonry wrapper remains optional'),

    ):
        if source.count(old)!=1:raise SystemExit('Classifier mutation binding drift: '+name)
        variants.append((name,source.replace(old,new,1),expected))
    dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet')
    env=dict(os.environ,DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    with tempfile.TemporaryDirectory(prefix='ghvr-scenery-classify-')as temp:
        folder=Path(temp)
        (folder/'Classifier.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649</NoWarn></PropertyGroup></Project>')
        shutil.copyfile(fixture/'UnityGraph.cs',folder/'UnityGraph.cs');shutil.copyfile(fixture/'Checks.cs',folder/'Checks.cs')
        shutil.copyfile(args.source_root/'src/GloomhavenVR/Core/FigureRendererGuard.cs',folder/'FigureGuard.cs')
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
