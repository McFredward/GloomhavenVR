using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InteractionProgram
{
    public static RuntimeAnimatorController FixtureController = null!;
    private static int _count;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly MethodInfo Classifier = typeof(ScenarioSceneryBudget).GetMethod("Classify", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static Mesh _mesh = null!;
    private static readonly List<Mesh> ExtraMeshes = new();
    private static Material _foliage = null!, _solid = null!;
    private static void Check(bool value, string text) { _count++; if (!value) throw new Exception(text); }
    private static GameObject Node(Transform parent, string name)
    {
        var node = new GameObject(name); node.transform.SetParent(parent, false); return node;
    }
    private static MeshRenderer Leaf(Transform parent, string name, bool foliage = true)
    {
        var leaf = Node(parent, name); leaf.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var renderer = leaf.AddComponent<MeshRenderer>(); renderer.sharedMaterial = foliage ? _foliage : _solid; return renderer;
    }
    private static MeshRenderer NativeLeaf(Transform parent, string name, string original, bool foliage = true)
    {
        var renderer = Leaf(parent, name, foliage);
        var mesh = UnityEngine.Object.Instantiate(_mesh); mesh.name = original; ExtraMeshes.Add(mesh);
        renderer.GetComponent<MeshFilter>().sharedMesh = mesh; return renderer;
    }
    private static string Classify(MeshRenderer renderer, ProceduralMapTile tile)
    {
        object?[] args = { renderer, tile, null, null };
        return Classifier.Invoke(null, args)!.ToString()!;
    }
    private static string Kind(MeshRenderer renderer, ProceduralMapTile tile)
    {
        object?[] args = { renderer, tile, null, null }; Classifier.Invoke(null, args); return args[3]!.ToString()!;
    }
    private static Transform? Unit(MeshRenderer renderer, ProceduralMapTile tile)
    {
        object?[] args = { renderer, tile, null, null }; Classifier.Invoke(null, args); return args[2] as Transform;
    }
    private static Component Driver(GameObject root)
    {
        ScenarioSceneryBudget.Install(root);
        var driver = root.GetComponents<MonoBehaviour>().Single(); driver.enabled = false; return driver;
    }
    private static void Tick(Component driver, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            SceneryClock.Now += .05f;
            driver.GetType().GetMethod("Update", Private)!.Invoke(driver, null);
        }
    }
    private static int Count(Component driver, string field) => ((ICollection)driver.GetType().GetField(field, Private)!.GetValue(driver)!).Count;
    private static int Value(Component driver, string field) => (int)driver.GetType().GetField(field, Private)!.GetValue(driver)!;
    public static int Run()
    {
        _count = 0; VRLog.Messages.Clear(); PerfMonitor.Marks.Clear(); SceneRegistry.MapTiles.Tiles.Clear();
        ExtraMeshes.Clear();
        HeldProps.Held = null; NetHeldProps.Any = false; NetHeldProps.Visual = null; VRSession.IsRunning = true;
        PerfConfig.ScenarioSceneryDensityPercentValue = 100; PerfConfig.ScenarioDecorationDensityPercentValue = 100;
        PerfConfig.ScenarioVegetationDensityPercentValue = 100;
        SceneryClock.Now = 0; SceneController.Instance.IsLoading = true;
        var gameScene = SceneManager.GetActiveScene();
        var proceduralScene = SceneManager.CreateScene("Scenario scenery fixture " + Guid.NewGuid());
        Choreographer.s_Choreographer.m_ProcGenScene = proceduralScene;
        var scenario = new GameObject("Actual procedural root", typeof(ProceduralScenario));
        SceneManager.MoveGameObjectToScene(scenario, proceduralScene);
        var tile = Node(scenario.transform, "ABCHL : (61829fe2-5d85-49e3-bf10-5e5b5853b292)").AddComponent<ProceduralMapTile>();
        SceneRegistry.MapTiles.Tiles.Add(tile);
        var generated = Node(tile.transform, "Generated Content");
        var host = new GameObject("Scenery driver fixture");
        _mesh = new Mesh { name = "Local grass bounds", vertices = new[] { new Vector3(-.5f,-.1f,-.5f), new Vector3(.5f,.1f,.5f), new Vector3(.5f,0,-.5f) }, triangles = new[] { 0,1,2 } };
        _mesh.RecalculateBounds();
        _foliage = new Material(Shader.Find("Amp_Basic_Foliage"));
        _solid = new Material(Shader.Find("Standard"));
        Check(_foliage.shader != null && _foliage.shader.name == "Amp_Basic_Foliage", "fixture uses a real authored foliage shader identity");
        Component? driver = null;
        try
        {
            var roots = Node(generated.transform, "PCG_FR_Floor_Grass_Roots_06_PR");
            var grass = Leaf(roots.transform, "FR_Floor_Detail_Grass_01");
            Check(Classify(grass, tile) == "Eligible", "hardware grass outside old Hex generator is admitted by full classifier");
            Check(Kind(grass, tile) == "Grass", "grass category is independent of broad decoration");
            var floorBase = Leaf(roots.transform, "FR_Floor_Grass_Half_01", false);
            roots.AddComponent<BoxCollider>();
            Check(Classify(grass, tile) == "Eligible", "grass over retained floor base is not rejected by incidental parent collider");
            Check(Classify(floorBase, tile) == "Structural", "solid forest floor plate stays visible");
            floorBase.enabled=false;
            Check(Classify(grass,tile)=="Effect","disabled retained floor base cannot justify invisible parent collider");
            floorBase.enabled=true; floorBase.forceRenderingOff=true;
            Check(Classify(grass,tile)=="Effect","force-hidden retained floor base cannot justify invisible parent collider"); floorBase.forceRenderingOff=false;
            var full = Node(generated.transform, "Full");
            var grassDetail = Leaf(full.transform, "FR_Floor_Detail_Grass_08_PR");
            Check(Classify(grassDetail, tile) == "Eligible", "native Full wrapper retains floor grass admission");
            var wall = Node(tile.transform, "Wall 6"); wall.AddComponent<ProceduralWall>(); wall.AddComponent<BoxCollider>();
            var wallGenerated = Node(wall.transform, "Generated Content");
            var mixed = Node(wallGenerated.transform, "FR_Default_Bay_10");
            // Captured source metadata is pinned in NativeForestProvenance.json: PCG_Forest
            // SHA256 29b4994ac122f76fe4bbab53c9f9829e93b038637c9baf485da4cf0d06b56b17.
            var hardwareBay = Node(mixed.transform, "FR_Default_Bay_10");
            hardwareBay.transform.localPosition = new Vector3(40,0,0);
            var bayCollider = hardwareBay.AddComponent<BoxCollider>();
            var bayMembers = new List<MeshRenderer>();
            string[] bayNames = {"FR_Tree_02 (3)","FR_Tree_05 (2)","FR_Stones_01 (1)","FR_Floor_LargeBush_07 (3)","FR_Floor_PlantsBushes_04 (2)","FR_Floor_LargeBush_04 (1)","FR_Floor_LargeBush_01 (1)","FR_Floor_LargeBush_04 (2)","FR_Floor_LargeBush_04 (3)","FR_Floor_LargeBush_07 (4)","FR_Floor_LargeBush_07 (5)"};
            foreach (string name in bayNames)
            {
                var member = NativeLeaf(hardwareBay.transform,name,"CR_"+name.Substring(0,name.IndexOf(" (",StringComparison.Ordinal)),false);
                bayMembers.Add(member);
                Check(Classify(member,tile)=="Eligible","captured native bay admits every conifer bush and scatter under shared box");
            }
            Check(ScenarioSceneryBudget.DecorativeCategories(hardwareBay)==6,"captured native bay proof separates vegetation from dressing");
            bayCollider.isTrigger=true;
            Check(Classify(bayMembers[0],tile)=="Effect","captured bay trigger collision is never decorative");bayCollider.isTrigger=false;
            var bayNative = hardwareBay.AddComponent<UnityGameEditorObject>();bayNative.PropObject=new object();
            Check(Classify(bayMembers[0],tile)=="Ancestry"&&ScenarioSceneryBudget.DecorativeCategories(hardwareBay)==0,"captured bay with native prop identity cannot bypass gameplay creation");
            UnityEngine.Object.DestroyImmediate(bayNative);
            var edge = Node(mixed.transform,"PCG_FR_Floor_Grass_Hex_EdgeTemp_PR");
            var edgeCollider=edge.AddComponent<BoxCollider>();
            var edgeBase=NativeLeaf(edge.transform,"FR_Floor_Grass_Seg_L","FR_Floor_Grass_Seg_L",false);
            var edgeGrass=NativeLeaf(edge.transform,"FR_Floor_Scatter_Grass_Small_01","FR_Floor_Detail_Small_01_Grass");
            Check(Classify(edgeBase,tile)=="Structural"&&Classify(edgeGrass,tile)=="Eligible","captured native edge keeps solid floor while real grass mesh is optional");
            Check(ScenarioSceneryBudget.DecorativeCategories(edge)==0,"mixed captured edge cannot be deferred as a decorative prefab");
            var underRoots=NativeLeaf(mixed.transform,"FR_CW_UnderWall_01_Roots","FR_CW_UnderWall_01_Roots",false);
            var logWall=Node(mixed.transform,"PCG_FR_Wall_Grassy_Verge_Thin_02_PR");
            var logWallCollision=logWall.AddComponent<BoxCollider>();
            var logWallCore=NativeLeaf(logWall.transform,"FR_Wall_Grassy_Verge_Thin_02","FR_Wall_Grassy_Verge_Thin_02",false);
            var logWallStone=NativeLeaf(logWall.transform,"FR_Wall_Grassy_Verge_Thin_Stone_02","FR_Wall_Grassy_Verge_Thin_Stone_02",false);
            var wallLog=NativeLeaf(logWall.transform,"FR_Wall_Grassy_Verge_Thin_Log_02","FR_Wall_Grassy_Verge_Thin_Log_02",false);
            var solidLogWall=NativeLeaf(logWall.transform,"FR_Wall_Solid_Log_01","FR_Wall_Solid_Log_01",false);
            Check(Classify(solidLogWall,tile)=="Structural","unproven solid log wall retains original wood geometry beside captured detachable log");
            Check(Classify(logWallCore,tile)=="Structural"&&Classify(logWallStone,tile)=="Structural","captured log wall retains both original solid core and stone member");
            wallLog.gameObject.AddComponent<BoxCollider>();
            Check(Classify(wallLog,tile)!="Eligible","a wall log owning structural collision cannot become an invisible wall");
            UnityEngine.Object.DestroyImmediate(wallLog.GetComponent<BoxCollider>());
            Check(Classify(underRoots,tile)=="Eligible"&&Classify(wallLog,tile)=="Eligible","captured detachable wall roots and log are vegetation despite solid wood material");
            var tree = Node(mixed.transform, "FR_Tree_05 (2)");
            var treeLeaf = Leaf(tree.transform, "LOD0", false);
            Check(Classify(treeLeaf, tile) == "Eligible", "LOD bark inherits actual decorative tree identity");
            Check(Kind(treeLeaf, tile) == "Vegetation", "tree uses vegetation budget");
            var bush = Leaf(mixed.transform, "FR_Floor_LargeBush_07 (4)");
            var ivy = Leaf(mixed.transform, "FR_Wall_Grassy_Verge_Thin_Ivy_Grass_01");
            Check(Classify(bush, tile) == "Eligible" && Classify(ivy, tile) == "Eligible", "mixed wall branch preserves independent bush and foliage admission");
            var pillar = Leaf(mixed.transform, "FR_Pillar_Tree_Trunk_01", false);
            var foliagePillar = Leaf(mixed.transform, "FR_Pillar_Tree_Trunk_02");
            var coreGrassyWall = Leaf(mixed.transform, "FR_Wall_Grassy_Verge_Thin_01");
            var wallSupport = Leaf(mixed.transform, "FR_CW_UnderWall_01_Rock");
            Check(Classify(foliagePillar,tile)=="Eligible" && Classify(coreGrassyWall,tile)=="Structural" && Classify(wallSupport,tile)=="Structural", "hard structural mesh identity wins over foliage material");
            var wallStone = Leaf(mixed.transform, "CV_Wall_Generic_02", false);
            var floor = Leaf(mixed.transform, "EN_CR_Floor_BaseHex_Plain", false);
            var water = Leaf(mixed.transform, "Water", false);
            var ossuary = Node(generated.transform,"CR_OS_Floor_Basic_Half_02");
            var ossuaryCollision = ossuary.AddComponent<BoxCollider>();
            var ossuaryCore = NativeLeaf(ossuary.transform,"CR_OS_Floor_Basic_Half_02","CR_OS_Floor_Basic_Half_02",false);
            var ornament = NativeLeaf(ossuary.transform,"LOD0","CR_OS_Floor_Basic_Half_02_Skull",false);
            Check(Classify(ornament,tile)=="Eligible"&&Classify(ossuaryCore,tile)=="Structural",
                "original detached skull layer follows decoration while real floor core remains");
            var ornaments = new List<MeshRenderer>();
            foreach (string original in NativeSceneryMetadata.CompositeMeshes)
            {
                var member=NativeLeaf(ossuary.transform,"LOD1",original,false); ornaments.Add(member);
                Check(Classify(member,tile)=="Eligible","every reviewed original detached ornament is optional: "+original);
            }
            var unknownSkull=NativeLeaf(generated.transform,"CV_Wall_Unknown_Skull","CV_Wall_Unknown_Skull",false);
            Check(Classify(unknownSkull,tile)=="Structural","unlisted skull wall preserves real masonry");
            var bonepile=NativeLeaf(generated.transform,"fi_vil_combs_props_bonepile_03c2","Mesh",false);
            var paper=NativeLeaf(generated.transform,"ST_TownMilitia_Paper_01","ST_TownMilitia_Paper_01",false);
            var blood=Node(generated.transform,"DECAL_BloodSplat_Proj_PR").AddComponent<Projector>();
            var cloneBlood=Node(generated.transform,"DECAL_FR_BloodSplat_Proj_PR(Clone)").AddComponent<Projector>();
            var duplicateClone=Node(generated.transform,"DECAL_Dirt_Proj_PR (12)(Clone)").AddComponent<Projector>();
            var arbitrarySuffix=Node(generated.transform,"DECAL_BloodSplat_Proj_PR MagicCircle").AddComponent<Projector>();
            var namedSuffix=Node(generated.transform,"DECAL_Dirt_Proj_PR (MagicCircle)").AddComponent<Projector>();
            var foreignProjection=Node(generated.transform,"DECAL_Dirt_Proj_PR (1)").AddComponent<Projector>(); foreignProjection.enabled=false;
            var nativeRune=Node(generated.transform,"ST_Demon_Circle02_Decal").AddComponent<Projector>();
            var propProjectorRoot=Node(generated.transform,"Native blood prop"); propProjectorRoot.AddComponent<ProceduralProp>();
            var propProjection=Node(propProjectorRoot.transform,"DECAL_BloodSplat_Proj_PR").AddComponent<Projector>();
            Check(Classify(bonepile,tile)=="Eligible"&&Classify(paper,tile)=="Eligible","original generic bone-pile mesh and paper use the decoration budget");
            var genericFloors=new List<MeshRenderer>();
            for (int i=1;i<=4;i++)
            {
                string name="CR_OS_Floor_0"+i;
                var originalPrefab=Node(generated.transform,name+"_PR");originalPrefab.AddComponent<BoxCollider>();
                var originalGroup=Node(originalPrefab.transform,name+"_New");
                var originalCore=NativeLeaf(originalGroup.transform,name,name,false);
                var originalBones=NativeLeaf(originalGroup.transform,name+"_Bones",name+"_Bones",false);
                genericFloors.Add(originalCore);genericFloors.Add(originalBones);
                Check(Classify(originalCore,tile)!="Eligible"&&Classify(originalBones,tile)=="Eligible",
                    "original generic floor prefab collider is represented by real retained core: "+name);
            }
            Check(Classify(pillar, tile) == "Eligible", "original tree pillar bark is vegetation rather than masonry");
            Check(Classify(wallStone, tile) == "Structural" && Classify(floor, tile) == "Structural", "native masonry and playable floor retain rendering");
            var nativeComposite=Node(mixed.transform,"FR_Wall_Grassy_Verge_Thin_Narrow_01");
            var compositeCollider=nativeComposite.AddComponent<BoxCollider>();
            var solidLod=Leaf(nativeComposite.transform,"LOD0",false);
            var foliageLod=Leaf(nativeComposite.transform,"LOD1");
            Check(Classify(foliageLod,tile)=="Eligible","solid wall LOD represents composite collider beside detached foliage");
            compositeCollider.enabled=false;
            Check(Classify(foliageLod,tile)=="Eligible","dedicated wall foliage LOD remains optional despite structural parent");
            var lowerFoliageLod=Leaf(nativeComposite.transform,"LOD2");
            Check(Unit(foliageLod,tile)==nativeComposite.transform&&Unit(lowerFoliageLod,tile)==nativeComposite.transform,"native foliage LOD levels share a stable density unit");
            Check(Classify(solidLod,tile)=="Structural","original anonymous solid wall LOD remains visible");
            foliageLod.sharedMaterials=new[]{_foliage,_solid};
            Check(Classify(foliageLod,tile)=="Structural","mixed-material stone and grass wall LOD remains intact");
            foliageLod.sharedMaterials=new[]{_foliage};
            compositeCollider.enabled=true;
            var pillarComposite=Node(mixed.transform,"FR_Pillar_Tree_Trunk_01");
            var pillarCanopy=Leaf(pillarComposite.transform,"LOD1");
            var pillarCore=Leaf(pillarComposite.transform,"LOD0",false);
            Check(Classify(pillarCanopy,tile)=="Eligible"&&Classify(pillarCore,tile)=="Eligible","separate tree canopy and original bark share vegetation admission");
            var actualTree=Node(mixed.transform,"FR_Pillar_Thin_Tree_Trunk_02");
            actualTree.transform.localPosition=new Vector3(20,0,0);
            var treeBark=Leaf(actualTree.transform,"LOD0",false);
            var treeCanopy=Leaf(actualTree.transform,"LOD1");
            var treeCollision=actualTree.AddComponent<BoxCollider>();
            Check(Classify(treeBark,tile)=="Eligible"&&Classify(treeCanopy,tile)=="Eligible","native tree pillar owns only its decorative collision");
            Check(Unit(treeBark,tile)==actualTree.transform&&Unit(treeCanopy,tile)==actualTree.transform,"complete tree bark and foliage LODs share one stable carrier");
            Physics.SyncTransforms();
            var treeRay=new Ray(new Vector3(20,0,-3),Vector3.forward);
            Check(Physics.Raycast(treeRay,out var hit,5)&&hit.collider==treeCollision,"fixture proves the visible native tree originally blocks the actual physics ray");
            treeCollision.isTrigger=true;
            Check(Classify(treeBark,tile)=="Effect","native tree trigger collision cannot be suppressed");treeCollision.isTrigger=false;
            var dynamicBody=actualTree.AddComponent<Rigidbody>();dynamicBody.isKinematic=true;
            Check(Classify(treeBark,tile)=="Effect","native tree rigid body collision cannot be suppressed");
            UnityEngine.Object.DestroyImmediate(dynamicBody);
            var mixedTree=Node(mixed.transform,"FR_Pillar_Tree_Trunk_03");
            var mixedTreeCollision=mixedTree.AddComponent<BoxCollider>();
            var mixedTreeBark=Leaf(mixedTree.transform,"LOD0",false);
            var mixedTreeFloor=Leaf(mixedTree.transform,"FR_Floor_Grass_Half_01",false);
            Check(Classify(mixedTreeBark,tile)=="Eligible"&&Classify(mixedTreeFloor,tile)=="Structural","mixed native tree unit retains its solid floor and shared collision");
            var leafTree=Leaf(mixed.transform,"FR_Tree_06",false);
            var leafTreeCollision=leafTree.gameObject.AddComponent<BoxCollider>();
            Check(Classify(leafTree,tile)=="Eligible","a purely decorative original tree leaf collider is also reversible");
            var foreignTree=Node(mixed.transform,"FR_Tree_07");
            var foreignTreeLeaf=Leaf(foreignTree.transform,"LOD0",false);
            var foreignTreeCollision=foreignTree.AddComponent<BoxCollider>();foreignTreeCollision.enabled=false;
            var namedPlant=Leaf(nativeComposite.transform,"FR_Wall_Grassy_Verge_Thin_Narrow_Plants_01");
            Check(Classify(namedPlant,tile)=="Eligible"&&Kind(namedPlant,tile)=="Vegetation","hardware wall plant leaf is optional beside retained masonry");
            namedPlant.sharedMaterials=new[]{_foliage,_solid};
            Check(Classify(namedPlant,tile)=="Structural","mixed wall plant leaf cannot erase its masonry material");
            namedPlant.sharedMaterials=new[]{_foliage};
            var treeGenerator=Node(mixed.transform,"PCG_FR_Pillar_Tree_Trunk_01_PR");
            var generatedTree=Node(treeGenerator.transform,"FR_Pillar_Tree_Trunk_01");
            var generatedTreeBark=Leaf(generatedTree.transform,"LOD0",false);
            var generatorCollider=treeGenerator.AddComponent<BoxCollider>();
            Check(Classify(generatedTreeBark,tile)=="Eligible","native tree generator wrapper owns only its decorative collision");
            generatorCollider.isTrigger=true;
            Check(Classify(generatedTreeBark,tile)=="Effect","original tree generator trigger remains native collision");generatorCollider.isTrigger=false;
            var hardwareGenerator=Node(mixed.transform,"PCG_FR_Pillar_Tree_Trunk_01_PR");
            hardwareGenerator.transform.localPosition=new Vector3(30,0,0);
            var hardwareTree=Node(hardwareGenerator.transform,"FR_Pillar_Tree_Trunk_01");
            var hardwareMembers=new List<MeshRenderer>();
            hardwareMembers.Add(Leaf(hardwareTree.transform,"FR_Pillar_Tree_Trunk_01",false));
            hardwareMembers.Add(Leaf(hardwareTree.transform,"LOD1"));
            for(int i=0;i<13;i++)hardwareMembers.Add(Leaf(hardwareTree.transform,"CR_RU_Vines ("+i+")"));
            hardwareMembers.Add(Leaf(hardwareTree.transform,"FR_Floor_Detail_Grass_01"));
            hardwareMembers.Add(Leaf(hardwareTree.transform,"FR_Floor_Detail_Grass_05_PR"));
            foreach(var member in hardwareMembers)
            {
                var original=UnityEngine.Object.Instantiate(_mesh);ExtraMeshes.Add(original);
                original.name=member.name=="LOD1"?"FR_Tree_02":member.name.Split(' ')[0];
                member.GetComponent<MeshFilter>().sharedMesh=original;
            }
            var hardwareCollision=hardwareGenerator.AddComponent<BoxCollider>();
            Check(hardwareMembers.Count==17&&hardwareMembers.All(r=>Classify(r,tile)=="Eligible"),"hardware-equivalent 17-renderer native tree admits original vines and grass with its tree collider");
            Check(hardwareMembers.All(r=>Unit(r,tile)==hardwareGenerator.transform&&Kind(r,tile)=="Vegetation"),"hardware-equivalent named tree assembly uses one original vegetation carrier for every member");
            var enclosingWall=Node(mixed.transform,"PCG_FR_Wall_Grassy_Verge_Thin_01_PR");
            var enclosingWallCollision=enclosingWall.AddComponent<BoxCollider>();
            var enclosingStone=Leaf(enclosingWall.transform,"FR_Wall_Grassy_Verge_Thin_01",false);
            var enclosedGenerator=Node(enclosingWall.transform,"PCG_FR_Pillar_Tree_Trunk_01_PR");
            var enclosedTree=Node(enclosedGenerator.transform,"FR_Pillar_Tree_Trunk_01");
            var enclosedBark=Leaf(enclosedTree.transform,"FR_Pillar_Tree_Trunk_01",false);
            var enclosedVines=Leaf(enclosedTree.transform,"CR_RU_Vines (2)");
            var enclosedCollision=enclosedGenerator.AddComponent<BoxCollider>();
            Check(Classify(enclosedBark,tile)=="Eligible"&&Classify(enclosedVines,tile)=="Eligible","completed native tree under mixed masonry wrapper remains optional");
            Check(Unit(enclosedBark,tile)==enclosedGenerator.transform&&Unit(enclosedVines,tile)==enclosedGenerator.transform,"completed tree carrier stops at surrounding masonry wrapper");
            Check(Classify(enclosingStone,tile)=="Structural","surrounding mixed masonry core remains structural");
            var anonymousTreeFloor=Leaf(mixedTree.transform,"Mesh",false);
            var treeFloorMesh=UnityEngine.Object.Instantiate(_mesh);ExtraMeshes.Add(treeFloorMesh);treeFloorMesh.name="FR_Floor_Grass_Half_01";
            anonymousTreeFloor.GetComponent<MeshFilter>().sharedMesh=treeFloorMesh;
            Check(Classify(anonymousTreeFloor,tile)=="Structural","anonymous original floor mesh under tree keeps its solid identity");
            var lightTree=Node(mixed.transform,"FR_Tree_08");
            var lightVine=Leaf(lightTree.transform,"CR_RU_Vines (3)");lightVine.gameObject.AddComponent<Light>();
            Check(Classify(lightVine,tile)=="Ancestry","native tree light branch cannot inherit optional vegetation ownership");
            var nativeTreeProp=Node(lightTree.transform,"Native prop");nativeTreeProp.AddComponent<ProceduralProp>();
            var nativeTreePropLeaf=Leaf(nativeTreeProp.transform,"FR_Floor_Detail_Grass_01");
            Check(Classify(nativeTreePropLeaf,tile)=="Ancestry","native prop beneath a tree stays protected despite foliage identity");
            var unknownComposite=Node(mixed.transform,"Player_Wall_Fake");
            Check(Classify(Leaf(unknownComposite.transform,"LOD1"),tile)=="Structural","unknown structural asset cannot be admitted by foliage shader alone");
            var anonymousGrass=Leaf(mixed.transform,"Mesh");
            var originalGrass=UnityEngine.Object.Instantiate(_mesh); ExtraMeshes.Add(originalGrass);
            originalGrass.name="FR_Floor_Detail_Grass_08_PR";
            anonymousGrass.GetComponent<MeshFilter>().sharedMesh=originalGrass;
            Check(Classify(anonymousGrass,tile)=="Eligible","original grass mesh family admits anonymous generated wrapper");
            var anonymousFloor=Leaf(mixed.transform,"Mesh",false);
            var originalFloor=UnityEngine.Object.Instantiate(_mesh); ExtraMeshes.Add(originalFloor);
            originalFloor.name="FR_Floor_Grass_Half_01";
            anonymousFloor.GetComponent<MeshFilter>().sharedMesh=originalFloor;
            Check(Classify(anonymousFloor,tile)=="Structural","original solid floor mesh under anonymous wrapper stays visible");
            var anonymousComposite=Node(mixed.transform,"PCG_Anonymous_Composite");
            anonymousComposite.AddComponent<BoxCollider>();
            anonymousFloor.transform.SetParent(anonymousComposite.transform,false);
            var anonymousCompositeGrass=Leaf(anonymousComposite.transform,"FR_Floor_Detail_Grass_08_PR");
            Check(Classify(anonymousCompositeGrass,tile)=="Eligible","original solid floor mesh represents an anonymous composite collider");
            Check(Classify(water, tile) != "Eligible", "water is not decorative detail");
            var scatter = Node(wallGenerated.transform, "CV_Floor_Scatter_01 (2)");
            var caveLod = Leaf(scatter.transform, "LOD2", false);
            Check(Classify(caveLod, tile) == "Eligible" && Kind(caveLod, tile) == "Dressing", "native cave scatter LOD hierarchy is admitted");
            var obstacle = Node(tile.transform, "ThreeHexObstacle : (f8c8bc14)"); obstacle.AddComponent<ProceduralProp>();
            var obstacleGenerated = Node(obstacle.transform, "Generated Content");
            var propGrass = Leaf(obstacleGenerated.transform, "FR_Floor_Scatter_Grass_Medium_03");
            Check(Classify(propGrass, tile) == "Ancestry", "gameplay prop grass can never be scenery");
            var nativeObject = Node(generated.transform, "Native chest mesh holder"); nativeObject.AddComponent<UnityGameEditorObject>().PropObject = new object();
            var chestGrass = Leaf(nativeObject.transform, "FR_Floor_Scatter_Grass_Small_01");
            Check(Classify(chestGrass, tile) == "Ancestry", "native gameplay object identity protects props without ProceduralProp");
            var interactable = Node(generated.transform, "Native interaction"); interactable.AddComponent<CInteractable>();
            Check(Classify(Leaf(interactable.transform,"FR_Tree_02"),tile)=="Ancestry", "non-tile interactable remains visible");
            var door = Node(tile.transform, "Door"); door.AddComponent<ProceduralDoorway>();
            Check(Classify(Leaf(Node(door.transform,"Generated Content").transform,"FR_Floor_PlantsBushes_01"),tile)=="Ancestry", "door dressing never disappears");
            var actor = Node(generated.transform, "Actor"); actor.AddComponent<ActorBehaviour>();
            Check(Classify(Leaf(actor.transform,"FR_Floor_Detail_Grass_05_PR"),tile)=="Ancestry", "actor accessory never disappears");
            var preview = Node(generated.transform, "Preview");
            Check(Classify(Leaf(preview.transform,"FR_Floor_Grass_Half_01"),tile)=="Ancestry", "unrevealed room preview retains native geometry");
            var blocker = Leaf(full.transform,"FR_Floor_PlantsBushes_04"); var collider = blocker.gameObject.AddComponent<BoxCollider>();
            Check(Classify(blocker,tile)=="Effect", "enabled hidden-leaf collider cannot become an invisible ray blocker");
            collider.enabled=false;
            Check(Classify(blocker,tile)=="Eligible", "disabled collider does not block decorative admission");
            var pure = Node(full.transform,"PCG_FR_Floor_Grass_Hex_Point_PR"); pure.AddComponent<BoxCollider>();
            Check(Classify(Leaf(pure.transform,"FR_Floor_Scatter_Grass_Small_01"),tile)=="Effect", "pure decorative unit with enabled collider remains visible");
            var scale = scenario.transform.localScale; scenario.transform.localScale = new Vector3(198,198,198);
            scenario.transform.rotation=Quaternion.Euler(50,30,15);
            Check(Classify(grass,tile)=="Eligible" && Classify(treeLeaf,tile)=="Eligible", "classification uses mesh-local bounds independent of world scale and rotation");
            scenario.transform.localScale=scale; scenario.transform.rotation=Quaternion.identity;
            tile.gameObject.SetActive(false);
            Check(Classify(propGrass,tile)=="Ancestry" && Classify(treeLeaf,tile)=="Eligible", "inactive content still executes actual native ancestry veto");
            tile.gameObject.SetActive(true);
            var separateTile = new GameObject("Native root-sibling map tile",typeof(ProceduralMapTile));
            SceneManager.MoveGameObjectToScene(separateTile,proceduralScene);
            var detachedTile=separateTile.GetComponent<ProceduralMapTile>(); SceneRegistry.MapTiles.Tiles.Add(detachedTile);
            var separateGrass=Leaf(Node(separateTile.transform,"Generated Content").transform,"FR_Floor_Detail_Grass_06_PR");
            var foreign=Leaf(full.transform,"FR_Floor_LargeBush_03"); foreign.forceRenderingOff=true;
            // Actual original prefab layouts from the hash-verified whole-game audit: the
            // page scatter owns a shared box; shelf books own their box; jugs carry an inert
            // Animator; mixed bays keep their retained core/corpse and its shared collision.
            var pageRoot=Node(generated.transform,"TO_INT_Cathedral_Clutter_Pages_03_PR");
            var originalPages=NativeLeaf(pageRoot.transform,"TO_INT_Floor_Clutter_Pages_03","TO_INT_Floor_Clutter_Pages_03",false);
            var originalPageBox=pageRoot.AddComponent<BoxCollider>();
            pageRoot.AddComponent<MaterialLoader>();pageRoot.AddComponent<DetailsDisabler>();pageRoot.AddComponent<ImportantObjectsShadowsDisabler>();
            var shelfBookRoot=Node(generated.transform,"TO_INT_Shelf_Clutter_Default_01_PR");
            var shelfBooks=NativeLeaf(shelfBookRoot.transform,"CR_ST_Shelf_Books_01","CR_ST_Shelf_Books_01",false);
            var shelfBookBox=shelfBooks.gameObject.AddComponent<BoxCollider>();
            Check(Classify(originalPages,tile)=="Eligible"&&Classify(shelfBooks,tile)=="Eligible",
                "pure original pages and shelf books admit their native decorative colliders");
            var originalJugs=NativeLeaf(generated.transform,"CR_ST_Shelf_Alchemy_Jugs_01","CR_ST_Shelf_Alchemy_Jugs_01",false);
            var jugAnimator=originalJugs.gameObject.AddComponent<Animator>();
            Check(Classify(originalJugs,tile)=="Eligible","controller-less original shelf animation is inert");
            var controller=new AnimatorOverrideController(FixtureController);
            jugAnimator.runtimeAnimatorController=controller;
            Check(jugAnimator.runtimeAnimatorController != null,"fixture assigns a real native runtime animator controller");
            Check(Classify(originalJugs,tile)=="Ancestry","active native controller still protects original shelf content");
            jugAnimator.runtimeAnimatorController=null;UnityEngine.Object.DestroyImmediate(controller);
            var unknownSmall=NativeLeaf(generated.transform,"CR_OS_Skull_Low","CR_OS_Skull_Low",false);
            unknownSmall.gameObject.AddComponent<UnknownNativeCallback>();
            Check(Classify(unknownSmall,tile)=="Ancestry","unknown native callbacks retain exact small decoration");
            var smallOriginals=new List<MeshRenderer>();
            var smallOwnMeshes=new List<MeshRenderer>();
            var smallOwnBoxes=new List<BoxCollider>();
            var retainedShelfRoot=Node(generated.transform,"CR_ST_Shelf_Alchemy_01");
            var retainedShelf=NativeLeaf(retainedShelfRoot.transform,"CR_ST_Shelf_Alchemy_01","CR_ST_Shelf_Alchemy_01",false);
            var retainedShelfBox=retainedShelfRoot.AddComponent<BoxCollider>();
            foreach(string original in NativeSceneryMetadata.SmallMeshes)
            {
                var member=NativeLeaf(retainedShelfRoot.transform,"Mesh",original,false);
                smallOriginals.Add(member);
                Check(Classify(member,tile)=="Eligible","every whole-game exact small leaf is independent of native structural carrier: "+original);
                var own=NativeLeaf(generated.transform,original,original,false);
                smallOwnMeshes.Add(own);smallOwnBoxes.Add(own.gameObject.AddComponent<BoxCollider>());
                Check(Classify(own,tile)=="Eligible","every exact small standalone leaf can own its proven dedicated collider: "+original);
            }
            var outerActor=scenario.AddComponent<ActorBehaviour>();
            Check(Classify(originalJugs,tile)=="Ancestry","actual actor above native tile boundary retains exact small meshes");
            UnityEngine.Object.DestroyImmediate(outerActor);
            var outerInteractable=scenario.AddComponent<CInteractableActor>();
            Check(Classify(originalJugs,tile)=="Ancestry","actual interactable actor above native tile boundary retains exact small meshes");
            UnityEngine.Object.DestroyImmediate(outerInteractable);
            var outerAnimator=scenario.AddComponent<Animator>();var outerController=new AnimatorOverrideController(FixtureController);
            outerAnimator.runtimeAnimatorController=outerController;
            Check(Classify(originalJugs,tile)=="Ancestry","active rig above native tile boundary retains exact small meshes");
            UnityEngine.Object.DestroyImmediate(outerAnimator);UnityEngine.Object.DestroyImmediate(outerController);
            var largePieces=new List<MeshRenderer>();
            foreach(string large in new[]{"CR_Corpse_Sitting","CR_Cross_01","skeleton_Lying 1","CR_ST_Stone_Coffin_01"})
            {
                var largeRoot=Node(generated.transform,large);
                var member=NativeLeaf(largeRoot.transform,"CR_OS_Skull_Low","CR_OS_Skull_Low",false);
                largePieces.Add(member);
                Check(Classify(member,tile)=="Ancestry","large corpse cross and coffin preserve separately named pieces: "+large);
            }
            var preservedFurniture=new List<MeshRenderer>();
            foreach(string original in NativeSceneryMetadata.FurnitureCores)
            {
                var member=NativeLeaf(generated.transform,"Mesh",original,false);
                preservedFurniture.Add(member);
                Check(Classify(member,tile)!="Eligible","original large furniture cores stay visible: "+original);
            }
            var torture=Node(generated.transform,"Torture_Bay_Sm_01");var tortureBox=torture.AddComponent<BoxCollider>();
            var tortureBoard=Node(torture.transform,"CR_CT_TortureBoard_Wall (1)");
            var skeleton=Node(tortureBoard.transform,"skeleton_Lying 1 (1)");
            var skeletonBody=NativeLeaf(skeleton.transform,"chest03","chest03",false);
            var wallChain=NativeLeaf(tortureBoard.transform,"CR_TC_WallChains_03 (2)","CR_TC_WallChains_03",false);
            Check(Classify(wallChain,tile)=="Eligible"&&Classify(skeletonBody,tile)!="Eligible",
                "actual mixed torture bay removes small chain leaf but retains large skeleton and shared collision");
            driver=Driver(host); Tick(driver,10);
            Check(!grass.forceRenderingOff && !treeLeaf.forceRenderingOff, "both 100 settings retain original rendering");
            PerfConfig.ScenarioDecorationDensityPercentValue=0; Tick(driver,50);
            Check(!grass.forceRenderingOff&&!treeLeaf.forceRenderingOff&&caveLod.forceRenderingOff,"decoration budget is independent from grass and vegetation");
            Check(ornament.forceRenderingOff&&ornaments.All(r=>r.forceRenderingOff)&&bonepile.forceRenderingOff&&paper.forceRenderingOff,
                "decoration zero removes all reviewed composite ornaments and original loose clutter");
            Check(!ossuaryCore.forceRenderingOff&&!unknownSkull.forceRenderingOff&&ossuaryCollision.enabled,
                "zero decoration retains original floor masonry and native collision");
            Check(!blood.enabled&&!foreignProjection.enabled&&nativeRune.enabled&&propProjection.enabled,
                "decoration zero masks identified original paint projections while gameplay and magic remain");
            Check(!cloneBlood.enabled&&!duplicateClone.enabled&&arbitrarySuffix.enabled&&namedSuffix.enabled,
                "native projector clones and numeric duplicates qualify while arbitrary suffixes remain");
            Check(genericFloors.Where((r,i)=>i%2==0).All(r=>!r.forceRenderingOff)&&genericFloors.Where((r,i)=>i%2==1).All(r=>r.forceRenderingOff),
                "original generic floor hierarchy removes bones while floor cores remain visible");
            Check(originalPages.forceRenderingOff&&shelfBooks.forceRenderingOff&&!originalPageBox.enabled&&!shelfBookBox.enabled,
                "zero decoration removes original page book meshes and owned collision together");
            Check(originalJugs.forceRenderingOff&&smallOriginals.All(r=>r.forceRenderingOff)&&wallChain.forceRenderingOff,
                "zero decoration masks all exact small original meshes including inert animated shelf contents");
            Check(smallOwnMeshes.All(r=>r.forceRenderingOff)&&smallOwnBoxes.All(c=>!c.enabled),
                "zero decoration removes every exact standalone small mesh and its dedicated native collider");
            Check(!retainedShelf.forceRenderingOff&&retainedShelfBox.enabled&&largePieces.All(r=>!r.forceRenderingOff)
                && preservedFurniture.All(r=>!r.forceRenderingOff)&&!skeletonBody.forceRenderingOff&&tortureBox.enabled&&!unknownSmall.forceRenderingOff,
                "large units native shelf and mixed shared collision remain untouched at decoration zero");
            PerfConfig.ScenarioSceneryDensityPercentValue=0; PerfConfig.ScenarioVegetationDensityPercentValue=0; Tick(driver,50);
            Check(grass.forceRenderingOff && treeLeaf.forceRenderingOff && bush.forceRenderingOff && caveLod.forceRenderingOff, "decoration zero hides real generated grass tree and cave LOD meshes");
            Check(bayMembers.All(r=>r.forceRenderingOff)&&!bayCollider.enabled,"zero budgets remove all eleven captured bay meshes and shared decorative box");
            Physics.SyncTransforms();
            Check(!Physics.Raycast(new Ray(new Vector3(40,0,-3),Vector3.forward),out var bayHit,5),"captured zero bay leaves no invisible laser blocker");
            Check(edgeGrass.forceRenderingOff&&!edgeBase.forceRenderingOff&&edgeCollider.enabled,"zero grass removes captured edge leaf while gameplay floor and collision stay");
            Check(underRoots.forceRenderingOff&&wallLog.forceRenderingOff&&!logWallCore.forceRenderingOff&&!logWallStone.forceRenderingOff&&logWallCollision.enabled,"zero vegetation removes captured roots and detached log while both structural wall members and collision stay");

            Check(foliageLod.forceRenderingOff&&pillarCanopy.forceRenderingOff&&!solidLod.forceRenderingOff&&pillarCore.forceRenderingOff,"full production driver removes complete tree pillars while preserving masonry LODs");
            Check(treeBark.forceRenderingOff&&treeCanopy.forceRenderingOff&&!treeCollision.enabled,"zero vegetation removes complete native tree pillars and their decorative collision");
            Check(generatedTreeBark.forceRenderingOff&&!generatorCollider.enabled,"zero vegetation masks complete tree generator geometry and its dedicated collider");
            Check(hardwareMembers.All(r=>r.forceRenderingOff)&&!hardwareCollision.enabled,"zero vegetation masks the whole hardware-equivalent 17-renderer tree without named-child leftovers");
            Check(enclosedBark.forceRenderingOff&&enclosedVines.forceRenderingOff&&!enclosedCollision.enabled&&!enclosingStone.forceRenderingOff&&enclosingWallCollision.enabled,"zero vegetation removes only inner native tree collision while surrounding wall geometry and collision stay");

            var newlyNativeBody=actualTree.AddComponent<Rigidbody>();newlyNativeBody.isKinematic=true;
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(!treeBark.forceRenderingOff&&!treeCanopy.forceRenderingOff&&treeCollision.enabled,"native rigid body added after hiding restores the complete tree and native collision");
            UnityEngine.Object.DestroyImmediate(newlyNativeBody);
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(treeBark.forceRenderingOff&&treeCanopy.forceRenderingOff&&!treeCollision.enabled,"later decorative regeneration can re-admit a tree after native collision is removed");

            Physics.SyncTransforms();
            Check(!Physics.Raycast(treeRay,out hit,5),"zero vegetation leaves no invisible tree blocker in the actual physics ray");
            Check(mixedTreeBark.forceRenderingOff&&!mixedTreeFloor.forceRenderingOff&&mixedTreeCollision.enabled,"zero vegetation retains shared mixed-unit floor collision");
            Check(!anonymousTreeFloor.forceRenderingOff&&mixedTreeCollision.enabled&&!lightVine.forceRenderingOff&&!nativeTreePropLeaf.forceRenderingOff,"whole-tree masking retains anonymous solid floor native light and gameplay prop branches");
            Check(leafTree.forceRenderingOff&&!leafTreeCollision.enabled,"zero vegetation disables only owned standalone tree leaf collision");
            Check(foreignTreeLeaf.forceRenderingOff&&!foreignTreeCollision.enabled,"zero vegetation does not acquire a foreign collider disable");
            Check(namedPlant.forceRenderingOff&&!solidLod.forceRenderingOff,"hardware named wall plant layer disappears while its native masonry stays");
            Check(anonymousGrass.forceRenderingOff&&!anonymousFloor.forceRenderingOff,"full production driver uses original mesh identity without erasing playable floor");
            Check(compositeCollider.enabled,"retained composite collider remains enabled under density masking");
            Check(separateGrass.forceRenderingOff, "scene-root native scenario fallback is discovered despite active scene not ProcGen");
            Check(!floorBase.forceRenderingOff && pillar.forceRenderingOff && !wallStone.forceRenderingOff && !floor.forceRenderingOff && !propGrass.forceRenderingOff && !chestGrass.forceRenderingOff && !water.forceRenderingOff, "essential scenario floor wall obstacle actors and native props remain rendered");
            Check(roots.GetComponent<BoxCollider>().enabled && wall.GetComponent<BoxCollider>().enabled, "budget never changes floor or shared native wall collision");
            PerfConfig.ScenarioSceneryDensityPercentValue=100; Tick(driver,50);
            Check(!grass.forceRenderingOff&&treeLeaf.forceRenderingOff&&caveLod.forceRenderingOff,"grass slider changes visible grass even at decoration zero");
            Check(hardwareMembers.All(r=>r.forceRenderingOff)&&!hardwareCollision.enabled,"grass 100 vegetation zero cannot restore attached native tree foliage or grass");

            PerfConfig.ScenarioVegetationDensityPercentValue=100; Tick(driver,50);
            Check(!treeLeaf.forceRenderingOff&&caveLod.forceRenderingOff,"vegetation slider restores trees independently of loose decoration");
            Check(!bayMembers[0].forceRenderingOff&&!bayMembers[1].forceRenderingOff&&bayMembers[2].forceRenderingOff&&bayCollider.enabled,"restoring captured conifers preserves independent dressing zero and represented collision");

            Check(!treeBark.forceRenderingOff&&!treeCanopy.forceRenderingOff&&treeCollision.enabled&&leafTreeCollision.enabled,"vegetation 100 restores all owned tree bark foliage and decorative collision");
            Check(!foreignTreeLeaf.forceRenderingOff&&!foreignTreeCollision.enabled,"vegetation 100 retains foreign disabled tree collision");
            Check(hardwareMembers.All(r=>!r.forceRenderingOff)&&hardwareCollision.enabled,"vegetation 100 restores every named original tree member and its owned collider");

            PerfConfig.ScenarioVegetationDensityPercentValue=50;Tick(driver,50);
            Check(treeBark.forceRenderingOff==treeCanopy.forceRenderingOff&&treeCollision.enabled!=treeBark.forceRenderingOff,"partial density cannot select different native bark canopy LODs");
            Check(hardwareMembers.All(r=>r.forceRenderingOff==hardwareMembers[0].forceRenderingOff)&&hardwareCollision.enabled!=hardwareMembers[0].forceRenderingOff,"partial density keeps all 17 named native tree members and shared tree collision together");

            PerfConfig.ScenarioVegetationDensityPercentValue=100;Tick(driver,50);

            PerfConfig.ScenarioDecorationDensityPercentValue=100; Tick(driver,50);
            Check(!grass.forceRenderingOff && !treeLeaf.forceRenderingOff && foreign.forceRenderingOff, "restoration clears owned masks and preserves foreign force flag");
            Check(blood.enabled&&!foreignProjection.enabled,"restoring decoration preserves foreign disabled paint projection");
            Check(ornaments.All(r=>!r.forceRenderingOff)&&!paper.forceRenderingOff&&!bonepile.forceRenderingOff,
                "decoration 100 restores every owned original ornament and loose-clutter renderer");
            PerfConfig.ScenarioSceneryDensityPercentValue=0; Tick(driver,50);
            Check(grass.forceRenderingOff && !treeLeaf.forceRenderingOff, "existing grass key works as an independent grass-only budget");
            Check(!propGrass.forceRenderingOff && !floorBase.forceRenderingOff, "grass slider preserves gameplay props and floor bases");
            PerfConfig.ScenarioVegetationDensityPercentValue=0; PerfConfig.ScenarioDecorationDensityPercentValue=0; Tick(driver,50);
            // Invoke the actual production creation prefix: a zero-decorative template must
            // produce a stable native handle/group transform without a real prefab instance.
            var template=Node(host.transform,"FR_Tree_05");
            template.transform.localScale=new Vector3(.5f,.25f,.75f);
            NativeLeaf(template.transform,"FR_Tree_05","CR_FR_Tree_05",false);
            template.AddComponent<BoxCollider>();template.AddComponent<MaterialLoader>();
            var prefix=typeof(ApparanceEntity_DecorativePlacementPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!;
            var pos=new Vector3(4,5,6);var rot=Quaternion.Euler(0,37,0);var scaled=new Vector3(2,3,4);
            object?[] createArgs={template,pos,scaled,rot,full.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"unscoped creation cannot infer native leaf versus later instanced group children");
            var scopePrefix=typeof(ApparanceEntity_ObjectPlacementContextPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!;
            var scopeFinalizer=typeof(ApparanceEntity_ObjectPlacementContextPatch).GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static)!;
            var scopeTarget=typeof(ApparanceEntity_ObjectPlacementContextPatch).GetMethod("TargetMethod",BindingFlags.NonPublic|BindingFlags.Static)!;
            Check(((MethodBase)scopeTarget.Invoke(null,null)!).Name.EndsWith(".CreateObject",StringComparison.Ordinal),"creation scope resolves the actual explicit native CreateObject method");
            object?[] groupScope={1,null};scopePrefix.Invoke(null,groupScope);
            object?[] nestedLeaf={0,null};scopePrefix.Invoke(null,nestedLeaf);
            Check((bool)typeof(ScenarioDecorativePlacement).GetField("_leafPlacement",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!,"nested native leaf receives its own creation scope");
            scopeFinalizer.Invoke(null,new object?[]{null,nestedLeaf[1]});
            bool nativeGroupCreates=(bool)prefix.Invoke(null,createArgs)!;
            Check(nativeGroupCreates,"native child-count group keeps original prefab before instanced children bypass creation prefix and after nested leaf return");
            // The native child fastpath AddInstance never invokes the prefab prefix. Simulate
            // that source-proven branch with no second prefix call: the original group's box
            // must already exist, even if its only subsequent child is a transform group.
            var untouchedGroup=UnityEngine.Object.Instantiate(template,pos,rot,full.transform);
            untouchedGroup.transform.localScale=scaled;
            int nativeMeshInstanceCount=1;
            var transformGroup=Node(untouchedGroup.transform,"Native CreateGroup child");
            Check(nativeMeshInstanceCount==1&&transformGroup.transform.parent==untouchedGroup.transform&&untouchedGroup.GetComponent<BoxCollider>().enabled,"instanced-child and CreateGroup fastpaths retain original parent collision without later interception");
            var nativeFailure=new InvalidOperationException("native placement failure");
            Check(ReferenceEquals(scopeFinalizer.Invoke(null,new object?[]{nativeFailure,groupScope[1]}),nativeFailure),"native creation finalizer preserves the original exception while closing its scope");
            createArgs=new object?[]{template,pos,scaled,rot,full.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"native group exception restores the unscoped conservative creation guard");
            UnityEngine.Object.DestroyImmediate(untouchedGroup);
            object?[] leafScope={0,null};scopePrefix.Invoke(null,leafScope);
            int beforeLoads=MaterialLoader.Instances;
            bool nativeCreates=(bool)prefix.Invoke(null,createArgs)!;
            var receipt=(GameObject)createArgs[5]!;
            Check(!nativeCreates&&receipt.GetComponentsInChildren<MeshRenderer>(true).Length==0&&receipt.GetComponentsInChildren<Collider>(true).Length==0,"creation prefix defers actual renderer collider and material instances at zero");
            Check(MaterialLoader.Instances==beforeLoads&&receipt.GetComponents<MonoBehaviour>().Length==0,"zero creation never instantiates original native material callbacks");
            Check(receipt.transform.position==pos&&Quaternion.Angle(receipt.transform.rotation,rot)<.001f&&receipt.transform.localScale==scaled,"deferred native placement retains original world pose and scale");
            receipt.name="Native generated handle name";
            var emittedGroup=Node(receipt.transform,"Native emitted child group");
            Check(ScenarioDecorativePlacement.DeferredObjects==1&&ScenarioDecorativePlacement.DeferredRenderers==1,"deferred creation records actual avoided prefab and renderer counts");
            var bayTemplate=UnityEngine.Object.Instantiate(hardwareBay,host.transform,false);
            bayTemplate.name="FR_Default_Bay_10";bayTemplate.GetComponent<BoxCollider>().enabled=true;
            foreach(var member in bayTemplate.GetComponentsInChildren<MeshRenderer>(true)) member.forceRenderingOff=false;
            createArgs=new object?[]{bayTemplate,pos,scaled,rot,full.transform,null};
            Check(!(bool)prefix.Invoke(null,createArgs)!,"captured eleven-member native conifer bay defers whole decorative prefab creation at zero");
            var bayReceipt=(GameObject)createArgs[5]!;
            Check(bayReceipt.GetComponentsInChildren<MeshRenderer>(true).Length==0&&bayReceipt.GetComponentsInChildren<Collider>(true).Length==0&&ScenarioDecorativePlacement.DeferredObjects==2&&ScenarioDecorativePlacement.DeferredRenderers==12,"captured native bay records eleven actually avoided renderer instances and no shared invisible box");
            PerfConfig.ScenarioVegetationDensityPercentValue=100;Tick(driver,5);
            var restoredVisual=receipt.GetComponentsInChildren<MeshRenderer>(true).Single();
            Check(receipt.name=="Native generated handle name"&&emittedGroup.transform.parent==receipt.transform&&restoredVisual.transform.parent!.parent==receipt.transform,"restoration retains native handle name and subsequently emitted group identity");
            Check(restoredVisual.GetComponent<MeshFilter>().sharedMesh==template.GetComponentInChildren<MeshFilter>().sharedMesh&&restoredVisual.sharedMaterial==_solid&&!restoredVisual.forceRenderingOff,"restoration instantiates exact original mesh material and full tree hierarchy");
            Check(restoredVisual.transform.parent!.localScale==Vector3.one&&receipt.transform.localScale==scaled,"restoration reproduces original pose without applying template scale twice");
            Check(MaterialLoader.Instances==beforeLoads+1,"restoration runs original material component lifecycle on demand");
            Check(bayReceipt.GetComponentsInChildren<MeshRenderer>(true).Length==11&&bayReceipt.GetComponentInChildren<BoxCollider>().enabled,"captured native bay restores all eleven original visual members and shared collision when vegetation returns");
            PerfConfig.ScenarioVegetationDensityPercentValue=0;Tick(driver,10);
            Check(restoredVisual.forceRenderingOff,"restored recipe remains reversible when zero vegetation is selected again");
            Check(bayReceipt.GetComponentsInChildren<MeshRenderer>(true).All(r=>r.forceRenderingOff)&&!bayReceipt.GetComponentInChildren<BoxCollider>().enabled,"restored captured bay removes every optional member and owns its invisible collider again at zero");
            var hash=typeof(ScenarioSceneryBudget).GetMethod("StableHash",BindingFlags.NonPublic|BindingFlags.Static)!;
            var hashLeaf=restoredVisual.transform;
            var hashNative=Node(full.transform,receipt.name);
            hashNative.transform.SetSiblingIndex(receipt.transform.GetSiblingIndex());
            var hashNativeLeaf=Node(hashNative.transform,hashLeaf.name);
            uint nativeHash=(uint)hash.Invoke(null,new object[]{hashNativeLeaf.transform,tile})!;
            UnityEngine.Object.DestroyImmediate(hashNative);
            uint restoredHash=(uint)hash.Invoke(null,new object[]{hashLeaf,tile})!;
            Check(nativeHash==restoredHash,"restored original leaf retains ordinary native density hash without recipe wrapper");
            template.SetActive(false);
            createArgs=new object?[]{template,pos,scaled,rot,full.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"authored inactive template keeps native activation semantics");template.SetActive(true);
            var nativeParent=Node(full.transform,"Native cached prop parent");
            nativeParent.AddComponent<UnityGameEditorObject>().PropObject=new object();
            createArgs=new object?[]{template,pos,scaled,rot,nativeParent.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"native parent PropObject identity protects gameplay creation without ProceduralProp");
            var collidingParent=Node(full.transform,"Native colliding generated group");collidingParent.AddComponent<BoxCollider>();
            createArgs=new object?[]{template,pos,scaled,rot,collidingParent.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"creation retains decoration representing an unrepresented native ancestor collider");
            createArgs=new object?[]{template,pos,scaled,rot,tile.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"creation requires actual Generated Content provenance before deferring a template");
            var unknown=template.AddComponent<UnknownNativeCallback>();
            createArgs=new object?[]{template,pos,scaled,rot,full.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!&&createArgs[5]==null,"unknown prefab callbacks keep original native creation");UnityEngine.Object.DestroyImmediate(unknown);
            var foreignCallback=template.AddComponent<ForeignCallbacks.MaterialLoader>();
            createArgs=new object?[]{template,pos,scaled,rot,full.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"foreign component named MaterialLoader cannot impersonate the native visual callback");
            UnityEngine.Object.DestroyImmediate(foreignCallback);
            createArgs=new object?[]{template,pos,scaled,rot,host.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"map UI NPC and other non-scenario destinations retain native creation");
            createArgs=new object?[]{edge,pos,scaled,rot,full.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"creation prefix never denies mixed gameplay floor output");
            PerfConfig.ScenarioVegetationDensityPercentValue=100;
            createArgs=new object?[]{template,pos,scaled,rot,full.transform,null};
            Check((bool)prefix.Invoke(null,createArgs)!,"vegetation 100 always uses native original prefab creation");
            foreach(int density in new[]{1,50,100})
            {
                PerfConfig.ScenarioSceneryDensityPercentValue=density;
                PerfConfig.ScenarioVegetationDensityPercentValue=density;
                PerfConfig.ScenarioDecorationDensityPercentValue=density;
                createArgs=new object?[]{template,pos,scaled,rot,full.transform,null};
                int proofEntries=SceneryTemplateProofProbe.Entries;
                Check((bool)prefix.Invoke(null,createArgs)!&&createArgs[5]==null&&SceneryTemplateProofProbe.Entries==proofEntries,"all positive budgets retain native creation without template proof at "+density+" percent");
            }
            PerfConfig.ScenarioSceneryDensityPercentValue=0;
            PerfConfig.ScenarioDecorationDensityPercentValue=0;
            PerfConfig.ScenarioVegetationDensityPercentValue=0;
            scopeFinalizer.Invoke(null,new object?[]{null,leafScope[1]});
            var pageTemplate=UnityEngine.Object.Instantiate(pageRoot,host.transform,false);
            pageTemplate.name="TO_INT_Cathedral_Clutter_Pages_03_PR";
            pageTemplate.GetComponent<BoxCollider>().enabled=true;
            pageTemplate.GetComponentInChildren<MeshRenderer>(true).forceRenderingOff=false;
            object?[] pageScope={0,null};scopePrefix.Invoke(null,pageScope);
            object?[] pageArgs={pageTemplate,pos,scaled,rot,full.transform,null};
            int beforePageLoads=MaterialLoader.Instances;
            Check(!(bool)prefix.Invoke(null,pageArgs)!,"actual original small page prefab defers native visual creation at zero");
            var pageReceipt=(GameObject)pageArgs[5]!;
            Check(pageReceipt.GetComponentsInChildren<MeshRenderer>(true).Length==0
                &&pageReceipt.GetComponentsInChildren<Collider>(true).Length==0&&MaterialLoader.Instances==beforePageLoads,
                "small page creation avoids original mesh collider and visual callbacks together");
            scopeFinalizer.Invoke(null,new object?[]{null,pageScope[1]});
            // A delayed population larger than the ordinary per-frame traversal budget must
            // finish before the real loading UI closes, without a later +2-second catch-up.
            var loadingLeaves=new List<MeshRenderer>();
            for(int i=0;i<1400;i++)loadingLeaves.Add(Leaf(full.transform,"FR_Floor_Detail_Grass_01 (load "+i+")"));
            ScenarioSceneryBudget.BeforeLoadingComplete();
            Check(loadingLeaves.All(r=>r.forceRenderingOff),"actual loading-close preparation drains every pending visual before UI hide");
            Check(!((bool)driver.GetType().GetField("_settlePending",Private)!.GetValue(driver)!),"actual loading-close preparation cancels visible post-load settled retry");
            foreach(var loadLeaf in loadingLeaves)UnityEngine.Object.DestroyImmediate(loadLeaf.gameObject);
            var showLate=Leaf(full.transform,"FR_Floor_Detail_Grass_01 (reveal)");showLate.gameObject.SetActive(false);
            ScenarioSceneryBudget.BeforeContentShown(showLate.gameObject);
            Check(showLate.forceRenderingOff&&!showLate.gameObject.activeSelf,"room reveal prepares inactive decorative leaf before native activation");
            showLate.gameObject.SetActive(true);
            Check(showLate.forceRenderingOff,"room reveal has no visible first frame of optional grass");
            // Retain the independent unpatched fallback-edge proof after explicitly restarting
            // native loading, so a broken Harmony registration cannot silently omit discovery.
            SceneController.Instance.IsLoading=true;Tick(driver);
            var late=Leaf(full.transform,"FR_Floor_PlantsBushes_02");
            Check(!late.forceRenderingOff, "late fixture begins unmasked before native placement");
            SceneController.Instance.IsLoading=false; Tick(driver,10);
            Check(late.forceRenderingOff, "loading-complete edge discovers late Apparance content missed by scene entry");
            var settled=Leaf(full.transform,"FR_Floor_Scatter_Grass_Medium_05");
            SceneryClock.Now+=3; Tick(driver,100);
            Check(settled.forceRenderingOff,"settled loading-complete catchup discovers registration after first completion scan");
            // The explicit native placement notification covers subsequent regeneration/reveals.
            var regenerated=Leaf(full.transform,"FR_Floor_Detail_Grass_05_PR");
            ScenarioSceneryBudget.ContentPlaced(tile); Tick(driver,100);
            Check(regenerated.forceRenderingOff, "native placement/reveal discovers generated content after initial preparation");
            var revealTree=Node(full.transform,"FR_Pillar_Tree_Trunk_04");
            var revealBark=Leaf(revealTree.transform,"LOD0",false);
            var revealCollision=revealTree.AddComponent<BoxCollider>();
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(revealBark.forceRenderingOff&&!revealCollision.enabled,"revealed native tree restores the complete zero-vegetation rule after loading");
            HeldProps.Held=revealTree.transform;Tick(driver);
            Check(!revealBark.forceRenderingOff&&revealCollision.enabled,"held native tree immediately restores its owned visual collision");HeldProps.Held=null;
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(revealBark.forceRenderingOff&&!revealCollision.enabled,"native replacement re-admits the released tree without stale ownership");
            revealBark.transform.SetParent(host.transform,false);Tick(driver,30);
            Check(!revealBark.forceRenderingOff&&revealCollision.enabled,"reparenting tree geometry restores its now-unrepresented old collision");

            var destroyedTree=Node(full.transform,"FR_Tree_08");
            var destroyedTreeLeaf=Leaf(destroyedTree.transform,"LOD0",false);
            var destroyedTreeCollision=destroyedTree.AddComponent<BoxCollider>();
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(destroyedTreeLeaf.forceRenderingOff&&!destroyedTreeCollision.enabled,"destroyed-branch fixture initially owns its native decorative collider");
            UnityEngine.Object.DestroyImmediate(destroyedTreeLeaf);
            Tick(driver,100);
            Check(destroyedTreeCollision.enabled,"pruning a destroyed renderer releases its remaining native tree collider");
            var materialLate=Leaf(full.transform,"FR_Floor_Grass_Half_01",false);
            ScenarioSceneryBudget.ContentPlaced(tile); Tick(driver,100);
            Check(!materialLate.forceRenderingOff,"native floor-grass placeholder is initially preserved before material completion");
            materialLate.sharedMaterial=_foliage;
            ScenarioSceneryBudget.MaterialsReady(materialLate);
            Check(materialLate.forceRenderingOff,"late native material completion reclassifies before any rendered frame or global polling");
            // Captured EdgeTemp: grass finishes first while its independently loaded solid
            // floor remains disabled. Loading completion cannot yet represent the shared box.
            // A whole-tile retry would cross 9,000 unrelated nodes after the spinner closes.
            var pendingForest=Node(full.transform,"Pending material forest");
            for(int i=0;i<9000;i++)Node(pendingForest.transform,"Unrelated native node "+i);
            var delayedComposite=Node(full.transform,"PCG_FR_Floor_Grass_Hex_EdgeTemp_PR");
            var delayedCollision=delayedComposite.AddComponent<BoxCollider>();
            var delayedFloor=NativeLeaf(delayedComposite.transform,"FR_Floor_Grass_Seg_L","FR_Floor_Grass_Seg_L",false);
            delayedFloor.enabled=false;
            var delayedGrass=NativeLeaf(delayedComposite.transform,"FR_Floor_Scatter_Grass_Small_01","FR_Floor_Detail_Small_01_Grass");
            ScenarioSceneryBudget.MaterialsReady(delayedGrass);
            Check(!delayedGrass.forceRenderingOff,"already-ready grass retains shared collision while native solid floor is still disabled");
            ScenarioSceneryBudget.BeforeLoadingComplete();
            Check(!delayedGrass.forceRenderingOff,"loading completion preserves unrepresented box until original solid floor material finishes");
            delayedFloor.enabled=true;
            ScenarioSceneryBudget.MaterialsReady(delayedFloor);
            Check(delayedGrass.forceRenderingOff,"late solid floor masks already-ready shared grass synchronously before any subsequent Update or queued tile walk");
            Check(delayedFloor.enabled&&!delayedFloor.forceRenderingOff&&delayedCollision.enabled,"late floor sibling preparation retains original solid geometry and shared native collision");
            UnityEngine.Object.DestroyImmediate(pendingForest);
            int unique=Value(driver,"_meshRenderers");
            for(int i=0;i<100;i++)ScenarioSceneryBudget.ContentPlaced(tile);
            Check(Count(driver,"_pending")==1,"repeated native tile notifications deduplicate pending discovery");
            Tick(driver,100);
            Check(Value(driver,"_meshRenderers")==unique,"repeat discovery reports unique renderer population rather than traversal encounters");
            var bulk=new List<MeshRenderer>();
            for(int i=0;i<1200;i++)bulk.Add(Leaf(full.transform,"FR_Floor_Detail_Grass_01 ("+i+")"));
            ScenarioSceneryBudget.ContentShown(tile.gameObject); Tick(driver,250);
            Check(bulk.Count(r=>r.forceRenderingOff)==1200,"large synthetic native room masks 1200 eligible leaves rather than old 27-leaf scale");
            Check(Value(driver,"_meshRenderers")==unique+1200,"late large population counts every unique discovered renderer");
            Check(PerfMonitor.Marks.Any(m=>m.Contains("ScenarioDecorationDensityPercent=0")) && PerfMonitor.Marks.Any(m=>m.Contains("retune complete")),"settings and finished retune mark distinct performance windows");
            Check(ScenarioSceneryBudget.IsOwnedHidden(grass), "pure visual skip recognises only a live budget-owned decoration mask");
            HeldProps.Held=grass.transform; Tick(driver);
            Check(Value(driver,"_heldMeshChecks")==1, "holding one mesh examines one renderer instead of 1200 unrelated scenery records");
            Check(!grass.forceRenderingOff,"local held decoration restores immediately before bounded ancestry watch"); HeldProps.Held=null;
            Check(!ScenarioSceneryBudget.IsOwnedHidden(grass), "same-frame held rescue releases the budget visual ownership query");
            NetHeldProps.Visual=bulk[1199].gameObject; NetHeldProps.Any=true; Tick(driver);
            Check(!bulk[1199].forceRenderingOff && Value(driver,"_heldMeshChecks")==1,
                "remote held decoration restores immediately using only its visual root index");
            NetHeldProps.Any=false; NetHeldProps.Visual=null;
            Check(!ScenarioSceneryBudget.IsOwnedHidden(foreign), "foreign renderer masks never qualify for skipping original visual lanes");
            treeLeaf.transform.SetParent(host.transform,false); Tick(driver,30);
            Check(!treeLeaf.forceRenderingOff,"reparented decoration no longer retains scenery mask");
            createArgs=new object?[]{template,pos,scaled,rot,full.transform,null};
            leafScope=new object?[]{0,null};scopePrefix.Invoke(null,leafScope);
            Check(!(bool)prefix.Invoke(null,createArgs)!,"deferred-only recovery fixture actually skips original creation");
            scopeFinalizer.Invoke(null,new object?[]{null,leafScope[1]});
            var deferredOnly=(GameObject)createArgs[5]!;
            driver.GetType().GetMethod("RestoreAll",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(driver,null);
            PerfConfig.ScenarioSceneryDensityPercentValue=100;PerfConfig.ScenarioVegetationDensityPercentValue=100;PerfConfig.ScenarioDecorationDensityPercentValue=100;
            Tick(driver,3);
            Check(deferredOnly.GetComponentsInChildren<MeshRenderer>(true).Length==1,"all 100 restores deferred-only scenes even with no renderer records");
            VRSession.IsRunning=false; Tick(driver);
            Check(!originalPages.forceRenderingOff&&!shelfBooks.forceRenderingOff&&originalPageBox.enabled&&shelfBookBox.enabled
                && !originalJugs.forceRenderingOff&&smallOriginals.All(r=>!r.forceRenderingOff)&&!wallChain.forceRenderingOff,
                "full decoration restores every exact original small mesh and only owned native boxes");
            Check(smallOwnMeshes.All(r=>!r.forceRenderingOff)&&smallOwnBoxes.All(c=>c.enabled),
                "full detail restores every exact standalone small mesh and its dedicated native collider");
            Check(pageReceipt.GetComponentsInChildren<MeshRenderer>(true).Length==1
                &&pageReceipt.GetComponentInChildren<BoxCollider>().enabled,
                "full decoration restores the exact deferred small page prefab and its native collision");
            Check(bulk.All(r=>!r.forceRenderingOff)&&foreign.forceRenderingOff,"VR shutdown restores every owned live renderer only");
            Check(treeCollision.enabled&&leafTreeCollision.enabled&&mixedTreeCollision.enabled&&!foreignTreeCollision.enabled,"VR shutdown restores all owned tree colliders and preserves native floor and foreign masks");
            int remainingOwners=((IDictionary)typeof(ScenarioSceneryBudget).GetField("TreeColliderOwners",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!).Count;
            Check(remainingOwners==0,"scene exit releases all tree collision claims without cross-scene retention");

            var allBiome=new List<MeshRenderer>();
            foreach(string name in new[]{"CR_RU_Floor_Foliage_01_PR_Low","CR_TC_Floor_Moss",
                "FR_DFG_Geranium_01_PR","FR_DFG_Clutter_Large_LongGrass_01_PR"})
            {
                var member=NativeLeaf(generated.transform,name,name);
                Check(Classify(member,tile)=="Eligible","all-biome original vegetation admits "+name);
                allBiome.Add(member);
            }
            var allBiomeProps=new List<MeshRenderer>();
            foreach(string name in new[]{"ST_Cult_Clutter_01","SB_01_ElementalPower_Clutter_Pages_03_PR",
                "SE_Rot_Clutter_01_PR","CS_02_GuardCamp_Clutter_Floor_01_PR","CT_01_Ship_Floor_Clutter_01_PR",
                "TO_INT_Candlestick_01","DLC_SB_Arena_Banners_04"})
            {
                var member=NativeLeaf(generated.transform,name,name,false);
                Check(Classify(member,tile)=="Eligible","original biome and DLC dressing admits "+name);
                allBiome.Add(member);
                var native=NativeLeaf(generated.transform,name,name,false);native.gameObject.AddComponent<ProceduralProp>();
                Check(Classify(native,tile)=="Ancestry","all-biome native gameplay props remain protected: "+name);
                allBiomeProps.Add(native);
            }
            var mossWall=NativeLeaf(generated.transform,"CV_Wall_Mossy_01","CV_Wall_Mossy_01",false);
            Check(Classify(mossWall,tile)=="Structural","moss naming never admits a solid native wall core");
            VRSession.IsRunning=true;
            PerfConfig.ScenarioSceneryDensityPercentValue=0;PerfConfig.ScenarioVegetationDensityPercentValue=0;PerfConfig.ScenarioDecorationDensityPercentValue=0;
            Tick(driver,100);ScenarioSceneryBudget.ContentShown(tile.gameObject);
            Check(ScenarioSceneryBudget.IsPreparingPresentation,"new-room queued scenery exposes actual presentation preparation");
            ScenarioSceneryBudget.BeforeLoadingComplete();
            Check(allBiome.All(r=>r.forceRenderingOff),"complete driver masks original all-biome and DLC cosmetics at zero");
            Check(allBiomeProps.All(r=>!r.forceRenderingOff)&&!mossWall.forceRenderingOff,"complete all-biome driver preserves native gameplay and solid wall geometry");
            PerfConfig.ScenarioDecorationDensityPercentValue=35;Tick(driver,100);
            int hiddenSmall=smallOriginals.Count(r=>r.forceRenderingOff);
            Check(hiddenSmall>0&&hiddenSmall<smallOriginals.Count,"low positive decoration selects a deterministic proportion of real small meshes");
            var selectedSmall=smallOriginals.Select(r=>r.forceRenderingOff).ToArray();
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(selectedSmall.SequenceEqual(smallOriginals.Select(r=>r.forceRenderingOff)),"rediscovery preserves exact small-mesh density membership");
            PerfConfig.ScenarioDecorationDensityPercentValue=0;Tick(driver,100);
            var activeController=new AnimatorOverrideController(FixtureController);jugAnimator.runtimeAnimatorController=activeController;
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(!originalJugs.forceRenderingOff,"native controller activation restores a previously masked shelf mesh");
            jugAnimator.runtimeAnimatorController=null;UnityEngine.Object.DestroyImmediate(activeController);
            ScenarioSceneryBudget.ContentPlaced(tile);Tick(driver,100);
            Check(originalJugs.forceRenderingOff,"clearing native inert shelf controller permits bounded remasking");
            Check(!ScenarioSceneryBudget.IsPreparingPresentation,"drained loading work does not include ongoing ancestry monitoring or diagnostics");
            PerfConfig.ScenarioSceneryDensityPercentValue=100;PerfConfig.ScenarioVegetationDensityPercentValue=100;PerfConfig.ScenarioDecorationDensityPercentValue=100;
            Tick(driver,100);
            Check(allBiome.All(r=>!r.forceRenderingOff),"original all-biome and DLC cosmetics restore at full detail");

            ScenarioSceneryBudget.Shutdown(); driver=null;
            Check(!late.forceRenderingOff&&!separateGrass.forceRenderingOff,"explicit teardown is reversible and idempotent");
            ScenarioSceneryBudget.Shutdown();
        }
        finally
        {
            ScenarioSceneryBudget.Shutdown(); SceneRegistry.MapTiles.Tiles.Clear(); HeldProps.Held=null;
            UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(scenario);
            UnityEngine.Object.DestroyImmediate(_foliage); UnityEngine.Object.DestroyImmediate(_solid); UnityEngine.Object.DestroyImmediate(_mesh);
            foreach(var mesh in ExtraMeshes) UnityEngine.Object.DestroyImmediate(mesh);
            ExtraMeshes.Clear();
            if(gameScene.IsValid())SceneManager.SetActiveScene(gameScene);
            foreach(var root in proceduralScene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(proceduralScene);
        }
        return _count;
    }
}
