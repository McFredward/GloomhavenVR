using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Core;
internal static partial class ScenarioSceneryBudget
{
    private static int _checks;
    private static GameObject Node(Transform parent,string name)
    {var node=new GameObject(name);node.transform.SetParent(parent);return node;}
    private static MeshRenderer Leaf(Transform parent,string name,bool foliage=true)
    {
        var node=Node(parent,name);node.AddComponent<MeshFilter>().sharedMesh=new Mesh{bounds=new Bounds{size=new Vector3(1,.2f,1)}};
        var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterials=new[]{new Material{shader=new Shader{name=foliage?"Amp_Basic_Foliage":"Amp_Basic_N_MRAO"}}};return renderer;
    }
    private static void Check(bool value,string message){_checks++;if(!value)throw new Exception(message);}
    private static Verdict Inspect(MeshRenderer renderer,ProceduralMapTile tile)=>Classify(renderer,tile,out _,out _);
    internal static int Run()
    {
        _checks=0; var scenario=new GameObject("Scenario");scenario.AddComponent<ProceduralScenario>();
        var tile=Node(scenario.transform,"ABCHL").AddComponent<ProceduralMapTile>();
        var generated=Node(tile.transform,"Generated Content");
        var roots=Node(generated.transform,"PCG_FR_Floor_Grass_Roots_06_PR");
        var grass=Leaf(roots.transform,"FR_Floor_Detail_Grass_01");
        Check(Inspect(grass,tile)==Verdict.Eligible,"hardware roots grass is not restricted to old Hex generator");
        var floor=Leaf(roots.transform,"FR_Floor_Grass_Half_01",false);roots.AddComponent<Collider>();
        Check(Inspect(grass,tile)==Verdict.Eligible,"parent collider with retained floor base permits decorative grass");
        Check(Inspect(floor,tile)==Verdict.Structural,"solid floor base is never scenery");
        floor.enabled=false;
        Check(Inspect(grass,tile)==Verdict.Effect,"disabled retained base does not represent an enabled parent collider");
        floor.enabled=true; floor.forceRenderingOff=true;
        Check(Inspect(grass,tile)==Verdict.Effect,"hidden retained base does not represent an enabled parent collider");floor.forceRenderingOff=false;
        var colliderLeaf=Leaf(generated.transform,"FR_Floor_LargeBush_03");var enabled=colliderLeaf.gameObject.AddComponent<Collider>();
        Check(Inspect(colliderLeaf,tile)==Verdict.Effect,"enabled leaf collider cannot become invisible ray blocker");enabled.enabled=false;
        Check(Inspect(colliderLeaf,tile)==Verdict.Eligible,"disabled leaf collider permits decoration");
        var pure=Node(generated.transform,"PCG_FR_Floor_Grass_Hex_Point_PR");pure.AddComponent<Collider>();
        Check(Inspect(Leaf(pure.transform,"FR_Floor_Scatter_Grass_Small_01"),tile)==Verdict.Effect,"pure colliding decoration remains represented");
        var wall=Node(tile.transform,"Wall 6");wall.AddComponent<ProceduralWall>();wall.AddComponent<Collider>();
        var mixed=Node(Node(wall.transform,"Generated Content").transform,"FR_Default_Bay_10");
        // Read-only PCG_Forest.asset.bundle provenance is pinned in NativeForestProvenance.json.
        // This is the hardware's real 11-member bay and shared box, not a single-tree proxy.
        var hardwareBay=Node(mixed.transform,"FR_Default_Bay_10");
        var bayCollision=hardwareBay.AddComponent<Collider>();
        var bayMembers=new System.Collections.Generic.List<MeshRenderer>();
        string[] bayNames={"FR_Tree_02 (3)","FR_Tree_05 (2)","FR_Stones_01 (1)","FR_Floor_LargeBush_07 (3)","FR_Floor_PlantsBushes_04 (2)","FR_Floor_LargeBush_04 (1)","FR_Floor_LargeBush_01 (1)","FR_Floor_LargeBush_04 (2)","FR_Floor_LargeBush_04 (3)","FR_Floor_LargeBush_07 (4)","FR_Floor_LargeBush_07 (5)"};
        foreach(string name in bayNames)
        {
            var member=Leaf(hardwareBay.transform,name,false);
            string original="CR_"+name.Substring(0,name.IndexOf(" (",StringComparison.Ordinal));
            member.GetComponent<MeshFilter>()!.sharedMesh!.name=original;
            bayMembers.Add(member);
            Check(Inspect(member,tile)==Verdict.Eligible,"captured native bay admits every tree bush scatter despite shared box");
        }
        Check(DecorativeCategories(hardwareBay)==6,"native bay whole-prefab proof requires vegetation and dressing controls");
        var bayRecords=new System.Collections.Generic.List<Record>();
        foreach(var member in bayMembers)bayRecords.Add(new Record{Renderer=member,BayColliders=BayCollidersFor(member)});
        SetHidden(bayRecords[0],true);
        Check(bayCollision.enabled,"partially visible native bay keeps represented shared collision");
        foreach(var bayRecord in bayRecords)SetHidden(bayRecord,true);
        Check(!bayCollision.enabled,"zero captured bay removes shared decorative collision after last visual member");
        SetHidden(bayRecords[1],false);
        Check(bayCollision.enabled,"restoring one bay tree restores original collision immediately");
        foreach(var bayRecord in bayRecords)SetHidden(bayRecord,false);
        bayCollision.enabled=false;foreach(var bayRecord in bayRecords)SetHidden(bayRecord,true);
        foreach(var bayRecord in bayRecords)SetHidden(bayRecord,false);
        Check(!bayCollision.enabled,"native bay restoration preserves foreign collider disable");bayCollision.enabled=true;
        bayCollision.isTrigger=true;
        Check(Inspect(bayMembers[0],tile)==Verdict.Effect,"native bay trigger cannot be treated as decorative box");bayCollision.isTrigger=false;
        hardwareBay.AddComponent<ActorBehaviour>();
        Check(Inspect(bayMembers[0],tile)==Verdict.Ancestry&&DecorativeCategories(hardwareBay)==0,"native gameplay identity vetoes whole bay deferred creation");
        var edge=Node(mixed.transform,"PCG_FR_Floor_Grass_Hex_EdgeTemp_PR");edge.AddComponent<Collider>();
        var edgeBase=Leaf(edge.transform,"FR_Floor_Grass_Seg_L",false);
        edgeBase.GetComponent<MeshFilter>()!.sharedMesh!.name="FR_Floor_Grass_Seg_L";
        var edgeGrass=Leaf(edge.transform,"FR_Floor_Scatter_Grass_Small_01");
        edgeGrass.GetComponent<MeshFilter>()!.sharedMesh!.name="FR_Floor_Detail_Small_01_Grass";
        Check(Inspect(edgeBase,tile)==Verdict.Structural&&Inspect(edgeGrass,tile)==Verdict.Eligible,"captured edge keeps original solid floor segment and removes real grass mesh");
        Check(DecorativeCategories(edge)==0,"mixed native edge floor cannot be deferred as decoration");
        var rootsWood=Leaf(mixed.transform,"FR_CW_UnderWall_01_Roots",false);
        rootsWood.GetComponent<MeshFilter>()!.sharedMesh!.name="FR_CW_UnderWall_01_Roots";
        Check(Inspect(rootsWood,tile)==Verdict.Eligible,"captured underwall roots are detachable vegetation beside retained rock wall");
        var tree=Node(mixed.transform,"FR_Tree_05 (2)");var bark=Leaf(tree.transform,"LOD0",false);
        Check(Inspect(bark,tile)==Verdict.Eligible,"LOD bark inherits decorative tree identity");
        Check(Inspect(Leaf(mixed.transform,"FR_Floor_LargeBush_07 (4)"),tile)==Verdict.Eligible,"hardware large bush names are admitted");
        Check(Inspect(Leaf(mixed.transform,"FR_Wall_Grassy_Verge_Thin_Ivy_Grass_01"),tile)==Verdict.Eligible,"explicit wall foliage dressing is admitted");
        foreach(string name in new[]{"FR_Pillar_Stone_01","FR_Wall_Grassy_Verge_Thin_01","FR_CW_UnderWall_01_Rock","EN_CR_Floor_BaseHex_Plain","CV_Wall_Generic_02"})
            Check(Inspect(Leaf(mixed.transform,name),tile)==Verdict.Structural,"structural geometry beats foliage shader: "+name);
        var composite=Node(mixed.transform,"FR_Wall_Grassy_Verge_Thin_Narrow_01");
        composite.AddComponent<Collider>();
        var core=Leaf(composite.transform,"LOD0",false);
        var canopy=Leaf(composite.transform,"LOD1");
        Check(Inspect(canopy,tile)==Verdict.Eligible,"solid wall LOD represents composite collider for detached foliage");
        composite.GetComponent<Collider>().enabled=false;
        Check(Inspect(canopy,tile)==Verdict.Eligible,"dedicated wall foliage LOD is optional despite structural ancestor");
        var lowerCanopy=Leaf(composite.transform,"LOD2");
        Classify(canopy,tile,out var canopyUnit,out _);Classify(lowerCanopy,tile,out var lowerUnit,out _);
        Check(ReferenceEquals(canopyUnit,lowerUnit)&&ReferenceEquals(canopyUnit,composite.transform),"foliage LOD levels share the original density unit");
        Check(Inspect(core,tile)==Verdict.Structural,"anonymous native solid wall core stays structural");
        canopy.sharedMaterials=new[]{canopy.sharedMaterials[0],core.sharedMaterials[0]};
        Check(Inspect(canopy,tile)==Verdict.Structural,"mixed-material wall LOD cannot erase stone with grass");
        canopy.sharedMaterials=new[]{new Material{shader=new Shader{name="Amp_Basic_Foliage"}}};
        var treePillar=Node(mixed.transform,"FR_Pillar_Tree_Trunk_01");
        Check(Inspect(Leaf(treePillar.transform,"LOD1"),tile)==Verdict.Eligible,"dedicated tree pillar canopy belongs to vegetation");
        Check(Inspect(Leaf(treePillar.transform,"LOD0",false),tile)==Verdict.Eligible,"solid tree pillar trunk is vegetation at zero");
        var actualTree=Node(mixed.transform,"FR_Pillar_Thin_Tree_Trunk_02");
        var treeBark=Leaf(actualTree.transform,"LOD0",false);
        var treeCanopy=Leaf(actualTree.transform,"LOD1");
        var treeCollision=actualTree.AddComponent<Collider>();
        Check(Inspect(treeBark,tile)==Verdict.Eligible&&Inspect(treeCanopy,tile)==Verdict.Eligible,"native tree pillar owns only its decorative collision");
        Classify(treeBark,tile,out var barkUnit,out _);Classify(treeCanopy,tile,out var branchUnit,out _);
        Check(ReferenceEquals(barkUnit,branchUnit)&&ReferenceEquals(barkUnit,actualTree.transform),"all tree bark and foliage LODs share one stable carrier");
        treeCollision.isTrigger=true;
        Check(Inspect(treeBark,tile)==Verdict.Effect,"native tree trigger collision cannot be suppressed");treeCollision.isTrigger=false;
        treeCollision.attachedRigidbody=new Rigidbody();
        Check(Inspect(treeBark,tile)==Verdict.Effect,"native tree rigid body collision cannot be suppressed");treeCollision.attachedRigidbody=null;
        var treeFloor=Leaf(actualTree.transform,"FR_Floor_Grass_Half_01",false);
        Check(!CanOwnTreeCollider(treeCollision,actualTree.transform),"tree composite with a solid floor cannot own shared collision");
        Check(Inspect(treeBark,tile)==Verdict.Eligible&&Inspect(treeFloor,tile)==Verdict.Structural,"solid floor keeps mixed tree collision represented");
        treeFloor.transform.SetParent(generated.transform);
        var nativePlant=Leaf(composite.transform,"FR_Wall_Grassy_Verge_Thin_Narrow_Plants_01");
        Check(Inspect(nativePlant,tile)==Verdict.Eligible,"hardware named wall plant leaf is optional beside masonry");
        nativePlant.sharedMaterials=new[]{canopy.sharedMaterials[0],core.sharedMaterials[0]};
        Check(Inspect(nativePlant,tile)==Verdict.Structural,"mixed material wall plant mesh cannot erase masonry");
        var firstTreeRecord=new Record{Renderer=treeBark,TreeColliders=new[]{treeCollision}};
        var secondTreeRecord=new Record{Renderer=treeCanopy,TreeColliders=new[]{treeCollision}};
        SetHidden(firstTreeRecord,true);SetHidden(secondTreeRecord,true);
        Check(!treeCollision.enabled&&firstTreeRecord.ColliderClaims&&secondTreeRecord.ColliderClaims,"zero tree masks own and suppress the shared decorative collider");
        SetHidden(firstTreeRecord,false);
        Check(!treeCollision.enabled,"one remaining hidden tree member retains collision ownership");
        SetHidden(secondTreeRecord,false);
        Check(treeCollision.enabled&&TreeColliderOwners.Count==0,"last member restores only owned decorative tree collision");
        treeCollision.enabled=false;SetHidden(firstTreeRecord,true);SetHidden(firstTreeRecord,false);
        Check(!treeCollision.enabled&&TreeColliderOwners.Count==0,"foreign disabled tree collision is never enabled by restoration");
        treeCollision.enabled=true;
        var treeGenerator=Node(mixed.transform,"PCG_FR_Pillar_Tree_Trunk_01_PR");
        var generatedTree=Node(treeGenerator.transform,"FR_Pillar_Tree_Trunk_01");
        var generatedTreeBark=Leaf(generatedTree.transform,"LOD0",false);
        var generatorCollider=treeGenerator.AddComponent<Collider>();
        Check(Inspect(generatedTreeBark,tile)==Verdict.Eligible,"native tree generator wrapper admits dedicated decorative collider ownership");
        generatorCollider.isTrigger=true;
        Check(Inspect(generatedTreeBark,tile)==Verdict.Effect,"original tree generator trigger remains native collision");
        var hardwareGenerator=Node(mixed.transform,"PCG_FR_Pillar_Tree_Trunk_01_PR");
        var hardwareTree=Node(hardwareGenerator.transform,"FR_Pillar_Tree_Trunk_01");
        var hardwareBark=Leaf(hardwareTree.transform,"FR_Pillar_Tree_Trunk_01",false);
        var hardwareVines=Leaf(hardwareTree.transform,"CR_RU_Vines (2)");
        var hardwareGrass=Leaf(hardwareTree.transform,"FR_Floor_Detail_Grass_01");
        var hardwareCollision=hardwareGenerator.AddComponent<Collider>();
        Check(Inspect(hardwareBark,tile)==Verdict.Eligible&&Inspect(hardwareVines,tile)==Verdict.Eligible&&Inspect(hardwareGrass,tile)==Verdict.Eligible,"hardware named native tree assembly admits vines and grass with its pure tree collider");
        Classify(hardwareBark,tile,out var nativeBarkCarrier,out var nativeBarkKind);
        Classify(hardwareVines,tile,out var nativeVineCarrier,out var nativeVineKind);
        Classify(hardwareGrass,tile,out var nativeGrassCarrier,out var nativeGrassKind);
        Check(ReferenceEquals(nativeBarkCarrier,hardwareGenerator.transform)&&ReferenceEquals(nativeVineCarrier,nativeBarkCarrier)&&ReferenceEquals(nativeGrassCarrier,nativeBarkCarrier),"hardware named vines and grass share the complete original tree carrier");
        Check(nativeBarkKind==Kind.Vegetation&&nativeVineKind==Kind.Vegetation&&nativeGrassKind==Kind.Vegetation,"attached tree grass uses vegetation independently of ground grass");
        Check(CanOwnTreeCollider(hardwareCollision,hardwareGenerator.transform),"original tree collision admits inherited native vine and grass members");
        var enclosingWall=Node(mixed.transform,"PCG_FR_Wall_Grassy_Verge_Thin_01_PR");
        var enclosingWallCollision=enclosingWall.AddComponent<Collider>();
        var enclosingStone=Leaf(enclosingWall.transform,"FR_Wall_Grassy_Verge_Thin_01",false);
        var enclosedGenerator=Node(enclosingWall.transform,"PCG_FR_Pillar_Tree_Trunk_01_PR");
        var enclosedTree=Node(enclosedGenerator.transform,"FR_Pillar_Tree_Trunk_01");
        var enclosedBark=Leaf(enclosedTree.transform,"FR_Pillar_Tree_Trunk_01",false);
        var enclosedVines=Leaf(enclosedTree.transform,"CR_RU_Vines (2)");
        var enclosedCollision=enclosedGenerator.AddComponent<Collider>();
        Check(Inspect(enclosedBark,tile)==Verdict.Eligible&&Inspect(enclosedVines,tile)==Verdict.Eligible,"completed native tree under mixed masonry wrapper remains optional");
        Check(ReferenceEquals(NativeTreeCarrier(enclosedBark),enclosedGenerator.transform)&&ReferenceEquals(NativeTreeCarrier(enclosedVines),enclosedGenerator.transform),"completed tree carrier stops at surrounding masonry wrapper");
        Check(Inspect(enclosingStone,tile)==Verdict.Structural&&CanOwnTreeCollider(enclosedCollision,enclosedGenerator.transform)&&!CanOwnTreeCollider(enclosingWallCollision,enclosedGenerator.transform),"native tree owns only inner collision while its surrounding masonry stays native");
        var inheritedFloor=Leaf(hardwareTree.transform,"Mesh",false);
        inheritedFloor.GetComponent<MeshFilter>()!.sharedMesh!.name="FR_Floor_Grass_Half_01";
        Check(Inspect(inheritedFloor,tile)==Verdict.Structural,"anonymous original floor mesh under tree keeps its solid identity");
        Check(!CanOwnTreeCollider(hardwareCollision,hardwareGenerator.transform),"anonymous solid floor prevents whole-tree collision ownership");
        var protectedLight=Leaf(hardwareTree.transform,"CR_RU_Vines (3)");protectedLight.gameObject.AddComponent<Light>();
        Check(Inspect(protectedLight,tile)==Verdict.Ancestry,"native light on an attached tree member remains protected");
        var grassFloor=Node(mixed.transform,"FR_Floor_Grass_Half_01");
        Check(Inspect(Leaf(grassFloor.transform,"LOD1"),tile)==Verdict.Eligible,"separate floor grass LOD is optional");
        Check(Inspect(Leaf(grassFloor.transform,"LOD0",false),tile)==Verdict.Structural,"anonymous solid grass floor LOD is retained");
        var fakeWall=Node(mixed.transform,"Player_Wall_Fake");
        Check(Inspect(Leaf(fakeWall.transform,"LOD1"),tile)==Verdict.Structural,"shader alone cannot admit unknown structural asset");
        var anonymousMesh=Leaf(mixed.transform,"Mesh");
        anonymousMesh.GetComponent<MeshFilter>()!.sharedMesh!.name="FR_Floor_Detail_Grass_08_PR";
        Check(Inspect(anonymousMesh,tile)==Verdict.Eligible,"original grass mesh family admits anonymous generated wrapper");
        anonymousMesh.GetComponent<MeshFilter>()!.sharedMesh!.name="FR_Floor_Grass_Half_01";
        anonymousMesh.sharedMaterials=core.sharedMaterials;
        Check(Inspect(anonymousMesh,tile)==Verdict.Structural,"solid original floor mesh stays protected beneath anonymous wrapper");
        var anonymousComposite=Node(mixed.transform,"PCG_Anonymous_Composite");
        anonymousComposite.AddComponent<Collider>();
        anonymousMesh.transform.SetParent(anonymousComposite.transform);
        Check(Inspect(Leaf(anonymousComposite.transform,"FR_Floor_Detail_Grass_08_PR"),tile)==Verdict.Eligible,"original solid floor mesh represents an anonymous composite collider");
        var scatter=Node(mixed.transform,"CV_Floor_Scatter_01 (2)");
        Check(Inspect(Leaf(scatter.transform,"LOD2",false),tile)==Verdict.Eligible,"cave scatter LOD hierarchy is admitted");
        foreach(string name in new[]{"CV_Crystal_Medium_02","CV_Floor_Stalagmites_06","FR_Stones_01 (1)","CR_RU_Vines (5)"})
            Check(Inspect(Leaf(mixed.transform,name,false),tile)==Verdict.Eligible,"native decoration family is admitted: "+name);
        var prop=Node(tile.transform,"ThreeHexObstacle");prop.AddComponent<ProceduralProp>();var propGrass=Leaf(Node(prop.transform,"Generated Content").transform,"FR_Floor_Scatter_Grass_Medium_03");
        Check(Inspect(propGrass,tile)==Verdict.Ancestry,"gameplay obstacle grass is protected by actual ancestry");
        var native=Node(generated.transform,"Native chest");native.AddComponent<UnityGameEditorObject>().PropObject=new object();
        Check(Inspect(Leaf(native.transform,"FR_Tree_02"),tile)==Verdict.Ancestry,"native gameplay identity without ProceduralProp is protected");
        var interaction=Node(generated.transform,"Interactable");interaction.AddComponent<CInteractable>();
        Check(Inspect(Leaf(interaction.transform,"FR_Tree_02"),tile)==Verdict.Ancestry,"non-tile interaction stays visible");
        var preview=Node(generated.transform,"Preview");
        Check(Inspect(Leaf(preview.transform,"FR_Floor_Detail_Grass_08_PR"),tile)==Verdict.Ancestry,"unrevealed room preview stays visible");
        var door=Node(generated.transform,"Door");door.AddComponent<ProceduralDoorway>();
        Check(Inspect(Leaf(door.transform,"FR_Floor_PlantsBushes_04"),tile)==Verdict.Ancestry,"door remains visible");
        var actor=Node(generated.transform,"Actor");actor.AddComponent<ActorBehaviour>();
        Check(Inspect(Leaf(actor.transform,"FR_Floor_Detail_Grass_06_PR"),tile)==Verdict.Ancestry,"actor remains visible");
        Check(Inspect(Leaf(mixed.transform,"Water"),tile)!=Verdict.Eligible,"water is outside detail budget");
        tile.gameObject.activeSelf=false;
        Check(Inspect(propGrass,tile)==Verdict.Ancestry && Inspect(bark,tile)==Verdict.Eligible,"inactive hierarchy executes native guard rather than inactive-skipping parent lookup");
        tile.gameObject.activeSelf=true;
        var ancestorCollider=roots.AddComponent<MeshCollider>();ancestorCollider.sharedMesh=grass.GetComponent<MeshFilter>()!.sharedMesh;
        Check(Inspect(grass,tile)==Verdict.Effect,"matching mesh collider remains represented even in mixed base unit");ancestorCollider.enabled=false;
        Check(IsScenarioTile(tile),"real parent scenario authorizes tile");
        var separate=new GameObject("Native root-sibling tile").AddComponent<ProceduralMapTile>();separate.gameObject.scene.Roots.Add(scenario);
        Check(IsScenarioTile(separate),"native scene-root scenario fallback authorizes tile");
        var unrelated=new GameObject("Map room").AddComponent<ProceduralMapTile>();
        Check(!IsScenarioTile(unrelated),"map outside real scenario is never budgeted");
        Check(!ShouldHide(99u,100)&&ShouldHide(99u,0)&&ShouldHide(99u,25)&&!ShouldHide(24u,25),"density boundaries preserve stable subset");
        var record=new Record{Renderer=grass};SetHidden(record,true);Check(grass.forceRenderingOff&&record.Owned,"budget owns changed force flag");
        int writes=grass.Writes;SetHidden(record,true);Check(grass.Writes==writes,"unchanged force flag does not repeat setters");
        SetHidden(record,false);Check(!grass.forceRenderingOff&&!record.Owned,"owned mask is reversible");
        grass.forceRenderingOff=true;record.Owned=false;writes=grass.Writes;SetHidden(record,true);SetHidden(record,false);
        Check(grass.forceRenderingOff&&grass.Writes==writes&&!record.Owned,"foreign renderer mask cannot be restored");
        return _checks;
    }
}
internal static class Program
{
    private static void Main()=>Console.WriteLine("Scenario full classifier graph: "+ScenarioSceneryBudget.Run()+" assertions passed");
}
