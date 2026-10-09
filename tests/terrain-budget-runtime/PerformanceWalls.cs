using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void HiddenWallWork(Camera camera)
    {
        ScenarioTerrainBudget.Shutdown(); ProceduralWall.m_WallCache.Clear();
        PerfConfig.TerrainSubstitutionOn = true; PerfConfig.CheapWallShadingOn = true;
        PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 0;
        PerfConfig.TerrainCameraSourceLimit = 0; PerfConfig.SharedEnvironmentMaterialReadsOn = true;
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(material => material);
        ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(material => material,
            () => new MaterialPass(), () => false, _ => false);
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
        ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
        var hidden = new HashSet<Renderer>();
        ScenarioTerrainBudget.ConfigurePerformanceWallVisibility(hidden.Contains);
        var host = new GameObject("GloomhavenVR.HiddenWallTerrainOwner");
        var scenario = new GameObject("Hidden wall scenario"); scenario.AddComponent<ProceduralScenario>();
        var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.red);
        var wall = Surface(scenario, "CV_Wall_Generic_01", new Vector3(0, 1, 0), material);
        var retained = Surface(scenario, "CV_Wall_Generic_02", new Vector3(1.4f, 1, 0), material);
        var floor = Surface(scenario, "CV_Floor_Basic", new Vector3(0, -1, 0), material, false);
        wall.gameObject.layer = retained.gameObject.layer = floor.gameObject.layer = 27;
        camera.cullingMask = 1 << 27; camera.orthographic = true; camera.orthographicSize = 2f;
        camera.transform.position = new Vector3(0, 1, -3); camera.transform.LookAt(new Vector3(0, 1, 0));
        GloomhavenVR.Rig.VRRigDriver.HeadCamera = camera;
        var collider = wall.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = wall.GetComponent<MeshFilter>().sharedMesh;
        Mesh collisionMesh = collider.sharedMesh;
        Material? changedMaterial = null;
        try
        {
            ScenarioTerrainBudget.Install(host); Morph(host);
            Check(DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(wall)),
                "hidden-wall terrain fixture starts with an actual prepared source and paired lease");
            Component owner = Driver(host);
            owner.GetType().GetMethod("HandlePreCull", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(owner, new object[] { camera });
            Check(ScenarioTerrainBudget.HasCurrentRenderLease(wall),
                "hidden-wall terrain fixture interrupts a genuine current camera lease");
            // Production acquisition releases consumers before assigning the
            // independent wall owner's persistent native visibility mask.
            ScenarioTerrainBudget.BeforeNativeRendererWrite(wall); hidden.Add(wall); wall.forceRenderingOff = true;
            Check(!ScenarioTerrainBudget.HasCurrentRenderLease(wall)
                && Proxies(host).Find(proxy => proxy.transform.position == wall.transform.position)!.enabled == false,
                "exact wall acquisition synchronously revokes its current terrain proxy before hiding");
            TerrainWorkObserver.Reset(wall); TerrainReadObserver.Reset();
            for (int frame = 0; frame < 64; frame++) Tick(host);
            Render(camera); Render(camera);
            Check(TerrainWorkObserver.Validations == 0 && TerrainWorkObserver.Details == 0
                && TerrainWorkObserver.GeometrySteps == 0,
                "exact performance-hidden terrain has zero per-source validation detail or geometry work");
            Check(TerrainWorkObserver.NativeReads == 0 && !ScenarioTerrainBudget.OwnsRenderSubstitute(wall),
                "exact performance-hidden terrain camera skips native visibility reads and substitute ownership");
            Check(DuringRender(camera, () => wall.forceRenderingOff && !ScenarioTerrainBudget.HasCurrentRenderLease(wall)
                && ScenarioTerrainBudget.HasCurrentRenderLease(retained)),
                "exact wall mask stays owned while unhidden terrain retains its actual camera substitute");
            Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(floor) && !floor.forceRenderingOff
                && collider.enabled && collider.sharedMesh == collisionMesh,
                "hidden-wall terrain skips leave native floors and gameplay collision untouched");

            // A foreign native mask is not the performance policy: Update still
            // maintains its private record, although PreCull cannot draw it.
            retained.forceRenderingOff = true; TerrainWorkObserver.Reset(retained); Tick(host);
            Check(TerrainWorkObserver.Validations > 0 && TerrainWorkObserver.Details > 0
                && TerrainWorkObserver.GeometrySteps > 0 && retained.forceRenderingOff,
                "foreign native renderer masks do not suspend unrelated terrain maintenance");
            hidden.Add(retained); TerrainReadObserver.Reset(); Tick(host);
            Check(TerrainReadObserver.HeadPositions == 0 && TerrainReadObserver.HeadScales == 0
                && TerrainReadObserver.HandPositions == 0,
                "all exact hidden terrain skips even shared head and tracked detail reads");

            var arriving = Surface(scenario, "CV_Pillar_Generic_01", new Vector3(-1.4f, 1, 0), material);
            arriving.gameObject.layer = 27; hidden.Add(arriving); arriving.forceRenderingOff = true;
            TerrainWorkObserver.Reset(arriving); ScenarioTerrainBudget.QueueRoot(arriving.gameObject); Tick(host);
            Check(TerrainWorkObserver.FilterReads == 0 && TerrainWorkObserver.Preparations == 0
                && !ScenarioTerrainBudget.OwnsRenderSubstitute(arriving),
                "exact already-hidden discovery performs no native filter reads or private preparation");

            // Final unmask fans out the exact source to MaterialReady. This callback,
            // rather than a recurring discovery poll, is the explicit external boundary.
            hidden.Remove(arriving); arriving.forceRenderingOff = false; TerrainFinalWallRelease.Invoke(arriving);
            TerrainWorkObserver.Reset(arriving); Morph(host);
            Debug.Log("Terrain exact-release observation: prepared=" + TerrainWorkObserver.Preparations
                + ", owned=" + ScenarioTerrainBudget.OwnsRenderSubstitute(arriving)
                + ", leased=" + DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(arriving)));
            Check(TerrainWorkObserver.Preparations == 1 && ScenarioTerrainBudget.OwnsRenderSubstitute(arriving)
                && DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(arriving)),
                "exact final wall release requeues previously hidden native discovery once");

            Mesh exact = Box("CV_Wall_Generic_01"); Mesh coarse = Coarse(exact);
            Mesh changedMesh = Object.Instantiate(exact); changedMesh.name = "CV_Wall_Generic_01"; changedMesh.UploadMeshData(true);
            Bank.Add(changedMesh, new[] { exact, coarse }); wall.GetComponent<MeshFilter>().sharedMesh = changedMesh;
            changedMaterial = new Material(material); changedMaterial.SetColor("_Tint", Color.blue); wall.sharedMaterial = changedMaterial;
            var changedParent = new GameObject("Current native generated wall parent");
            changedParent.transform.SetParent(wall.transform.parent, false); wall.transform.SetParent(changedParent.transform, true);
            hidden.Remove(wall); wall.forceRenderingOff = false; TerrainFinalWallRelease.Invoke(wall);
            TerrainWorkObserver.Reset(wall); Morph(host);
            Check(TerrainWorkObserver.Preparations == 1 && TerrainWorkObserver.Validations > 0
                && DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(wall)
                    && Proxies(host).Exists(proxy => proxy.enabled
                        && proxy.transform.position == wall.transform.position
                        && proxy.GetComponent<MeshFilter>().sharedMesh == coarse
                        && proxy.sharedMaterial.GetColor("_Tint") == Color.blue)),
                "Regular restores current native mesh material and reparented terrain after exact release");
            Color current = Pixels(camera)[48 * 96 + 48];
            Check(current.b > .04f && current.r < .02f && collider.sharedMesh == collisionMesh,
                "Regular actual terrain pixels use changed current blue source while original collider stays native");
            ActorBehaviour actor = changedParent.AddComponent<ActorBehaviour>();
            Check(!DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(wall)),
                "Regular rechecks current native actor ancestry after hidden terrain resumes");
            Object.DestroyImmediate(actor);
            Check(DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(wall)),
                "Regular native scope eligibility resumes on the next actual camera");
            hidden.Remove(retained); retained.forceRenderingOff = false; TerrainFinalWallRelease.Invoke(retained);
            Morph(host);
            Check(DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(retained)),
                "independently restored exact terrain source resumes its selected coarse detail");
        }
        finally
        {
            TerrainWorkObserver.Reset(null); ScenarioTerrainBudget.ConfigurePerformanceWallVisibility(_ => false);
            ScenarioTerrainBudget.Shutdown(); Object.DestroyImmediate(host); Object.DestroyImmediate(scenario);
            if (changedMaterial != null) Object.DestroyImmediate(changedMaterial);
            Object.DestroyImmediate(material); ProceduralWall.m_WallCache.Clear();
        }
    }
}
