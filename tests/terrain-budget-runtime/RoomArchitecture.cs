using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    // Admission here is an explicit immutable-bank boundary. The separately
    // source-bound catalog fixture verifies roles/streams against original game
    // bundles; these camera cases exercise current native ownership and recovery.
    private static void RoomArchitectureCoverage(Camera camera)
    {
        ProceduralWall.m_WallCache.Clear();
        var host = new GameObject("GloomhavenVR.RoomArchitectureTest");
        var scenario = new GameObject("Scenario"); scenario.AddComponent<ProceduralScenario>();
        var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.white);
        var floor = Surface(scenario, "AuditedFloorWithoutNameHint", new Vector3(0, 1, 0), material, false);
        var structure = Surface(scenario, "AuditedShelfWithoutWallName", new Vector3(2, 1, 0), material, false);
        var unrelated = Surface(scenario, "UnauditedFloor", new Vector3(-2, 1, 0), material, false);
        var wrongOwner = Surface(scenario, "VerifiedFloorInWallOwner", new Vector3(4, 1, 0), material);
        var outline = Surface(scenario, "VerifiedFloor_Outline", new Vector3(-.75f, 1, 0), material, false);
        Mesh floorOriginal = floor.GetComponent<MeshFilter>().sharedMesh;
        Mesh structureOriginal = structure.GetComponent<MeshFilter>().sharedMesh;
        var collision = floor.gameObject.AddComponent<MeshCollider>(); collision.sharedMesh = floorOriginal;
        var roles = new Dictionary<Mesh, int> { [floorOriginal] = 1, [structureOriginal] = 2,
            [wrongOwner.GetComponent<MeshFilter>().sharedMesh] = 1,
            [outline.GetComponent<MeshFilter>().sharedMesh] = 2 };
        bool room = true; int floorPercent = 0;
        ScenarioTerrainBudget.ConfigureArchitectureBank(mesh => roles.TryGetValue(mesh, out int role) ? role : 0);
        ScenarioTerrainBudget.ConfigureRoomArchitecture(() => room, () => floorPercent);
        ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(current => current);
        ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(current => current, () => new MaterialPass(), () => false, current => false);
        ScenarioTerrainBudget.ConfigureNativeCameraConsumers(current => current.commandBufferCount > 0);
        PerfConfig.SharedEnvironmentMaterialReadsOn = true; PerfConfig.TerrainCameraSourceLimit = 0;
        PerfConfig.TerrainSubstitutionOn = false; PerfConfig.CheapWallShadingOn = false;
        PerfConfig.TerrainDetailPercent = 0; PerfConfig.DistantTerrainDetailPercent = 0;
        camera.transform.position = new Vector3(0, 2, -3); camera.transform.LookAt(Vector3.up);
        camera.orthographicSize = 2;
        ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario); Tick(host, 4f);
        Check(Proxies(host).Count == 3 && !ScenarioTerrainBudget.OwnsRenderSubstitute(unrelated),
            "room master independently admits verified floors and tile architecture without name-only admission");
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(wrongOwner), "verified floor role requires current native map-tile ownership rather than a wall owner");
        Check(DuringRender(camera, () => floor.forceRenderingOff && !unrelated.forceRenderingOff),
            "room master renders verified floor while legacy terrain master is off");
        Mesh intermediate = Proxies(host).Find(r => r.transform.position == floor.transform.position)!.GetComponent<MeshFilter>().sharedMesh;
        Check(Triangles(intermediate) == Triangles(floorOriginal) && intermediate != Bank[floorOriginal][1],
            "floor geometry keeps exact topology through visible continuous intermediate morph after a hitch");
        Morph(host); Color[] coarsePixels = Pixels(camera);
        MeshRenderer floorProxy = Proxies(host).Find(r => r.transform.position == floor.transform.position)!;
        Mesh endpoint = floorProxy.GetComponent<MeshFilter>().sharedMesh;
        Check(Triangles(endpoint) < Triangles(floorOriginal) / 2 && endpoint.bounds == floorOriginal.bounds && Visible(coarsePixels) > 100,
            "verified room floor endpoint materially reduces triangles and retains original 3D bounds and nonempty camera pixels");
        var prepared = (System.Collections.IDictionary)Driver(host).GetType().GetField("_surfaces", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Driver(host))!;
        object floorSurface = prepared[floor.GetInstanceID()]!;
        Check(floorSurface.GetType().GetField("_morph", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(floorSurface) == null
            && floorSurface.GetType().GetField("_vertices", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(floorSurface) == null,
            "settled room floor releases its private morph mesh and transient full-vertex arrays");
        Check(ScenarioTerrainBudget.TryGetSettledRoomFloor(floor, out Mesh groupMesh) && groupMesh == endpoint,
            "settled floor grouping API exposes only the current selected readable endpoint");
        ScenarioTerrainBudget.ConfigureFloorGrouping(current => current == floor);
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(floor) && !DuringRender(camera, () => floor.forceRenderingOff),
            "prepared floor group owns geometry without a second terrain proxy mask");
        floorPercent = 100;
        Check(!ScenarioTerrainBudget.TryGetSettledRoomFloor(floor, out _) && ScenarioTerrainBudget.OwnsRenderSubstitute(floor),
            "changed floor setting invalidates a prepared group before the next Update and restores terrain ownership");
        floorPercent = 0; ScenarioTerrainBudget.ConfigureFloorGrouping(current => false);
        Check(floor.GetComponent<MeshFilter>().sharedMesh == floorOriginal && collision.sharedMesh == floorOriginal && floor.sharedMaterial == material,
            "room floor substitution leaves native geometry collisions and original material slots untouched");
        var block = new MaterialPropertyBlock(); block.SetFloat("_Cutoff", 1.2f); block.SetInteger("ToggleWallFade", 1);
        block.SetFloat("_EnableOcclusionMap", 1f); floor.SetPropertyBlock(block);
        Check(DuringRender(camera, () =>
        {
            var current = new MaterialPropertyBlock(); floorProxy.GetPropertyBlock(current);
            return floor.forceRenderingOff && current.GetFloat("_GHVRTerrainNeverFade") == 1f && current.GetFloat("_GHVRWorldNeverFade") == 1f;
        }), "verified coarse floor copies native blocks while retaining both never-fade channels");
        floor.SetPropertyBlock(null);
        Check(DuringRender(camera, () =>
        {
            var current = new MaterialPropertyBlock(); Proxies(host).Find(r => r.transform.position == outline.transform.position)!.GetPropertyBlock(current);
            return outline.forceRenderingOff && current.GetFloat("_GHVRTerrainNeverFade") == 1f && current.GetFloat("_GHVRWorldNeverFade") == 1f;
        }), "verified floor-outline structure retains original never-fade marker independently of grouping role");
        floorPercent = 100; Tick(host, 4f);
        Check(DuringRender(camera, () => floor.forceRenderingOff), "floor full-detail restoration retains continuous intermediate camera geometry");
        Morph(host);
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(floor) && !DuringRender(camera, () => floor.forceRenderingOff),
            "independent floor detail100 restores original native floor immediately after the morph endpoint");
        Check(ScenarioTerrainBudget.OwnsRenderSubstitute(structure), "floor quality dial leaves coarse structural room architecture independent");
        floorPercent = 0; Morph(host);
        floor.gameObject.AddComponent<CInteractable>();
        using (ScenarioTerrainBudget.BeginFloorReadPass())
            Check(!ScenarioTerrainBudget.TryGetSettledRoomFloor(floor, out _),
                "settled floor group lookup rejects a newly interactive native owner at its unchanged pose");
        Check(!DuringRender(camera, () => floor.forceRenderingOff), "new native floor interaction between camera passes revokes architecture admission");
        Object.DestroyImmediate(floor.GetComponent<CInteractable>());
        HeldPropsReset(floor.gameObject);
        using (ScenarioTerrainBudget.BeginFloorReadPass())
            Check(!ScenarioTerrainBudget.TryGetSettledRoomFloor(floor, out _), "settled floor group lookup rejects a currently held native floor");
        Check(!DuringRender(camera, () => floor.forceRenderingOff), "registered held room floor is native despite immutable architecture role");
        GloomhavenVR.Board.FigureGrab.HeldProps.SetRoots();
        bool leased = false;
        Camera.CameraCallback release = current =>
        {
            if (current != camera) return;
            leased = floor.forceRenderingOff; ScenarioTerrainBudget.BeforeNativeRendererWrite(floor);
        };
        Camera.onPreCull += release; Render(camera); Camera.onPreCull -= release;
        Check(leased && !floor.forceRenderingOff && !floorProxy.enabled,
            "late original floor write releases both native mask and private geometry before the actual camera draw");
        GameObject? clone = null;
        Camera.CameraCallback copy = current =>
        {
            if (current != camera) return;
            ScenarioTerrainBudget.BeforeNativeContentChange(); clone = Object.Instantiate(floor.gameObject);
        };
        Camera.onPreCull += copy; Render(camera); Camera.onPreCull -= copy;
        Check(clone != null && !clone.GetComponent<MeshRenderer>().forceRenderingOff && clone.GetComponent<MeshFilter>().sharedMesh == floorOriginal
            && clone.GetComponent<MeshCollider>().sharedMesh == floorOriginal && clone.transform.childCount == 0,
            "pooled floor clone after native content recovery retains original geometry collider material slots and visibility");
        Object.DestroyImmediate(clone);
        var nestedObject = new GameObject("NestedFloorCamera"); var nested = nestedObject.AddComponent<Camera>();
        nested.targetTexture = camera.targetTexture; nested.CopyFrom(camera);
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(current => { nested.Render(); return current; });
        Render(camera);
        Check(!floor.forceRenderingOff && !structure.forceRenderingOff && Proxies(host).TrueForAll(r => !r.enabled),
            "nested room-floor material preparation releases every resumed outer camera mask");
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(current => current); Object.DestroyImmediate(nestedObject);
        room = false;
        Check(!DuringRender(camera, () => floor.forceRenderingOff), "room master off camera releases interrupted floor leases before Update");
        Tick(host);
        FieldInfo surfaces = Driver(host).GetType().GetField("_surfaces", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Check(((System.Collections.IDictionary)surfaces.GetValue(Driver(host))!).Count == 0 && Proxies(host).TrueForAll(r => !r.enabled),
            "both room and legacy masters off dispose private floor and architecture preparation");
        room = true; Tick(host); Morph(host);
        Check(DuringRender(camera, () => floor.forceRenderingOff), "room master reopening reseeds verified floor architecture from original native scene");
        ScenarioTerrainBudget.Shutdown(); ScenarioTerrainBudget.ConfigureRoomArchitecture(() => false, () => 100);
        ScenarioTerrainBudget.ConfigureArchitectureBank(mesh => 0);
        Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(material);
        PerfConfig.TerrainSubstitutionOn = true;
    }
    private static void HeldPropsReset(GameObject root) => GloomhavenVR.Board.FigureGrab.HeldProps.SetRoots(root);
}
