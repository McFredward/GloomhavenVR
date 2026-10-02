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
        HeldProps.Held = null; NetHeldProps.Any = false; VRSession.IsRunning = true;
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
            Check(Classify(foliagePillar,tile)=="Structural" && Classify(coreGrassyWall,tile)=="Structural" && Classify(wallSupport,tile)=="Structural", "hard structural mesh identity wins over foliage material");
            var wallStone = Leaf(mixed.transform, "CV_Wall_Generic_02", false);
            var floor = Leaf(mixed.transform, "EN_CR_Floor_BaseHex_Plain", false);
            var water = Leaf(mixed.transform, "Water", false);
            Check(Classify(pillar, tile) == "Structural", "structural tree pillar is preserved");
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
            Check(Classify(pillarCanopy,tile)=="Eligible"&&Classify(pillarCore,tile)=="Structural","separate tree canopy is optional while original trunk remains");
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
            driver=Driver(host); Tick(driver,10);
            Check(!grass.forceRenderingOff && !treeLeaf.forceRenderingOff, "both 100 settings retain original rendering");
            PerfConfig.ScenarioDecorationDensityPercentValue=0; Tick(driver,50);
            Check(!grass.forceRenderingOff&&!treeLeaf.forceRenderingOff&&caveLod.forceRenderingOff,"decoration budget is independent from grass and vegetation");
            PerfConfig.ScenarioSceneryDensityPercentValue=0; PerfConfig.ScenarioVegetationDensityPercentValue=0; Tick(driver,50);
            Check(grass.forceRenderingOff && treeLeaf.forceRenderingOff && bush.forceRenderingOff && caveLod.forceRenderingOff, "decoration zero hides real generated grass tree and cave LOD meshes");
            Check(foliageLod.forceRenderingOff&&pillarCanopy.forceRenderingOff&&!solidLod.forceRenderingOff&&!pillarCore.forceRenderingOff,"full production driver removes wall and pillar foliage while preserving structural LODs");
            Check(anonymousGrass.forceRenderingOff&&!anonymousFloor.forceRenderingOff,"full production driver uses original mesh identity without erasing playable floor");
            Check(compositeCollider.enabled,"retained composite collider remains enabled under density masking");
            Check(separateGrass.forceRenderingOff, "scene-root native scenario fallback is discovered despite active scene not ProcGen");
            Check(!floorBase.forceRenderingOff && !pillar.forceRenderingOff && !wallStone.forceRenderingOff && !floor.forceRenderingOff && !propGrass.forceRenderingOff && !chestGrass.forceRenderingOff && !water.forceRenderingOff, "essential scenario floor wall obstacle actors and native props remain rendered");
            Check(roots.GetComponent<BoxCollider>().enabled && wall.GetComponent<BoxCollider>().enabled, "budget never changes collider enabled state");
            PerfConfig.ScenarioSceneryDensityPercentValue=100; Tick(driver,50);
            Check(!grass.forceRenderingOff&&treeLeaf.forceRenderingOff&&caveLod.forceRenderingOff,"grass slider changes visible grass even at decoration zero");
            PerfConfig.ScenarioVegetationDensityPercentValue=100; Tick(driver,50);
            Check(!treeLeaf.forceRenderingOff&&caveLod.forceRenderingOff,"vegetation slider restores trees independently of loose decoration");
            PerfConfig.ScenarioDecorationDensityPercentValue=100; Tick(driver,50);
            Check(!grass.forceRenderingOff && !treeLeaf.forceRenderingOff && foreign.forceRenderingOff, "restoration clears owned masks and preserves foreign force flag");
            PerfConfig.ScenarioSceneryDensityPercentValue=0; Tick(driver,50);
            Check(grass.forceRenderingOff && !treeLeaf.forceRenderingOff, "existing grass key works as an independent grass-only budget");
            Check(!propGrass.forceRenderingOff && !floorBase.forceRenderingOff, "grass slider preserves gameplay props and floor bases");
            PerfConfig.ScenarioVegetationDensityPercentValue=0; PerfConfig.ScenarioDecorationDensityPercentValue=0; Tick(driver,50);
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
            var materialLate=Leaf(full.transform,"FR_Floor_Grass_Half_01",false);
            ScenarioSceneryBudget.ContentPlaced(tile); Tick(driver,100);
            Check(!materialLate.forceRenderingOff,"native floor-grass placeholder is initially preserved before material completion");
            materialLate.sharedMaterial=_foliage;
            ScenarioSceneryBudget.MaterialsReady(materialLate); Tick(driver,20);
            Check(materialLate.forceRenderingOff,"late native material completion reclassifies exact leaf without placement or global polling");
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
            HeldProps.Held=grass.transform; Tick(driver);
            Check(!grass.forceRenderingOff,"local held decoration restores immediately before bounded ancestry watch"); HeldProps.Held=null;
            treeLeaf.transform.SetParent(host.transform,false); Tick(driver,30);
            Check(!treeLeaf.forceRenderingOff,"reparented decoration no longer retains scenery mask");
            VRSession.IsRunning=false; Tick(driver);
            Check(bulk.All(r=>!r.forceRenderingOff)&&foreign.forceRenderingOff,"VR shutdown restores every owned live renderer only");
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
