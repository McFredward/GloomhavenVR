using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WaterTerrainProgram
{
    private static int _assertions;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Driver => typeof(WaterTerrainVR).GetField("_driver", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static void Check(bool condition, string reason) { _assertions++; if (!condition) throw new Exception(reason); }
    private static void Tick(bool slow = false)
    {
        if (slow) Driver.GetType().GetField("_next", Private)!.SetValue(Driver, 0f);
        Driver.GetType().GetMethod("Tick", Private)!.Invoke(Driver, null);
    }
    private static int Counter(string name) => (int)Driver.GetType().GetField(name, Private)!.GetValue(Driver)!;
    private static ProceduralMapTile Tile(string name)
    {
        var tile = new GameObject(name).AddComponent<ProceduralMapTile>();
        SceneRegistry.MapTiles.Tiles.Add(tile); return tile;
    }
    private static Renderer Surface(Transform root, string name, params Material[] slots)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name;
        go.transform.SetParent(root, false); go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterials = slots; return renderer;
    }
    private static Material Water(string name, string shader = "VFX/Water_Shd_Trans")
    {
        var result = new Material(Shader.Find(shader)); result.name = name;
        result.SetColor("_Color_Tint", new Color(.18f, .38f, .24f, .7f)); return result;
    }
    private static bool Own(Renderer r, int slot = 0) => r.sharedMaterials[slot] != null
        && r.sharedMaterials[slot].shader.name == WaterOwnSurface.FilmShaderName;
    private static void Ready(Renderer r) => new MaterialLoaderData().Complete(r, r.sharedMaterials);
    public static int Run()
    {
        _assertions = 0;
        VRSession.IsRunning = true;
        NativePublicationIntegration.Configure();
        WaterTerrainVR.Install();
        Driver.GetType().GetField("UsePlacementHook", Private)!.SetValue(Driver, true);
        Check(Shader.Find(WaterOwnSurface.FilmShaderName) != null, "actual production WaterVR shader imports");
        var crypt = Tile("Crypt tile");
        var delayed = Surface(crypt.transform, "TERRAIN_Water_Plane", Array.Empty<Material>());
        Tick(slow: true);
        var native = Water("TERRAIN_GEN_WaterPlane_Crypt_Mat");
        var asynchronousLoader = new MaterialLoaderData();
        delayed.enabled = false;
        asynchronousLoader.Complete(delayed, new Material[] { null! });
        Tick(); Check(delayed.sharedMaterials.Length == 0 && !delayed.enabled, "partial native material completion stays empty and disabled");
        asynchronousLoader.Complete(delayed, new[] { native });
        Ready(delayed); Tick();
        Check(Own(delayed), "asynchronous publication after empty seed adopts in the same frame");
        Check(WaterTerrainVR.IsOwnedWaterSurface(delayed), "actual owned water remains recognizable after shader replacement");
        Check(ReferenceEquals(native.shader, Shader.Find("VFX/Water_Shd_Trans")), "shared authored material never mutates");
        Check(ReferenceEquals(delayed.sharedMaterial.shader, Shader.Find(WaterOwnSurface.FilmShaderName)), "first adoption uses actual production shader, no interim fallback");
        int made = Counter("_instancesMade"); Material first = delayed.sharedMaterial;
        for (int i = 0; i < 10; i++) { Ready(delayed); Tick(); }
        Check(Counter("_instancesMade") == made && ReferenceEquals(first, delayed.sharedMaterial), "repeated callbacks never clone unchanged water");
        int collections = SceneRegistry.MapTiles.Collections;
        for (int i = 0; i < 10; i++) Tick();
        Check(SceneRegistry.MapTiles.Collections == collections, "settled enforcement performs no tile rediscovery between slow ticks");

        var neutral = new Material(Shader.Find("Unlit/Color")); neutral.name = "Stone";
        var mixed = Surface(crypt.transform, "TERRAIN_Water_Plane Mixed", null!, neutral, native);
        Ready(mixed); Tick();
        Check(Own(mixed, 2), "water in a non-first material slot is replaced");
        Check(ReferenceEquals(mixed.sharedMaterials[1], neutral), "unrelated material slot remains the original shared material");
        Check(mixed.sharedMaterials[0] == null, "authored empty first slot stays empty");
        var low = Water("TERRAIN_GEN_WaterPlane_Crypt_LCM", "VFX/Water_Shd_Trans_Low");
        var slots = mixed.sharedMaterials; slots[2] = low; mixed.sharedMaterials = slots;
        Check(!WaterTerrainVR.IsOwnedWaterSurface(mixed), "current native overwrite is never stale owned-water provenance");
        Tick();
        Check(Own(mixed, 2) && !ReferenceEquals(slots[2], mixed.sharedMaterials[2]), "later-slot native quality replacement re-adopts without slot-zero guard");
        Check(ReferenceEquals(mixed.sharedMaterials[1], neutral), "quality re-adoption preserves unrelated slot");
        Check(SceneRegistry.MapTiles.Collections == collections, "native overwrite re-adopts directly without scene/tile rescan");
        mixed.sharedMaterials[2].shader = Shader.Find("VFX/Water_Shd_Trans_Low");
        Check(!WaterTerrainVR.IsOwnedWaterSurface(mixed), "private shader overwrite does not claim current VR shader ownership");
        Tick(); Check(Own(mixed, 2), "native in-place private shader overwrite is repaired in the same frame");

        var inactive = Surface(crypt.transform, "TERRAIN_Water_Plane Preview", native);
        inactive.gameObject.SetActive(false); Ready(inactive); Tick();
        Check(Own(inactive), "inactive preview water receives the material before first visible frame");
        inactive.gameObject.SetActive(true); Check(Own(inactive), "preview activation retains replacement");
        var lateRoom = Tile("Late opened room");
        var late = Surface(lateRoom.transform, "TERRAIN_Water_Plane", Array.Empty<Material>());
        WaterTerrainVR.NotifyContentPlaced(lateRoom); Tick();
        late.sharedMaterial = native; Ready(late); Tick();
        Check(Own(late), "late room placement then delayed native publication receives replacement");

        foreach (string materialName in new[] { "TERRAIN_GEN_WaterPlane_Mat", "TERRAIN_GEN_WaterPlane_Sewer_LCM" })
        {
            var film = Surface(crypt.transform, "TERRAIN_Water_Authored", Water(materialName));
            Ready(film); Tick(); Check(Own(film), "verified native terrain family: " + materialName);
        }
        var sluice = new GameObject("DLC_TH_SC08_SluiceGate_BAY(Clone)");
        sluice.transform.SetParent(crypt.transform, false);
        foreach (string materialName in new[] { "DLC_SC11_WaterPlane_MAT", "DLC_SC11_WaterPlane_LCM", "DLC_SC11_WaterTorus_MAT", "DLC_SC11_WaterTorus_LCM" })
        {
            var film = Surface(sluice.transform, "geo_watersurface_cube", Water(materialName));
            Ready(film); Tick(); Check(Own(film), "verified DLC terrain ancestry: " + materialName);
        }
        foreach (string shader in new[] { "VFX/Water_Shd", "VFX/Water_Shd_Low", "VFX/Water_Shd_Trans", "VFX/Water_Shd_Trans_Low" })
        {
            var film = Surface(crypt.transform, "TERRAIN_Water_Plane Quality", Water("TERRAIN_GEN_WaterPlane_Mat", shader));
            Ready(film); Tick(); Check(Own(film), "all native water quality shaders: " + shader);
        }
        var negativeRoot = new GameObject("Decorative fountain");
        var decorative = Surface(negativeRoot.transform, "FR_SW_Pond_Small", Water("ForestWater_Mat"));
        Ready(decorative); Tick(); Check(!Own(decorative), "decorative fountain outside native terrain is untouched");
        foreach (string decorativeName in new[] { "CaveWater_Mat", "CaveWater_LCM", "ForestWater_Mat", "ForestWater_LCM" })
        {
            var decorativeOnTile = Surface(crypt.transform, "FR_SW_Pond_Small", Water(decorativeName));
            decorativeOnTile.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            Ready(decorativeOnTile); Tick(); Check(Own(decorativeOnTile), "whole-game native pond water family replaced: " + decorativeName);
            Check(decorativeOnTile.reflectionProbeUsage == UnityEngine.Rendering.ReflectionProbeUsage.Off, "pond replacement does not add terrain reflection probe treatment");
        }
        var stone = Surface(crypt.transform, "TERRAIN_Crypt_Water_Stone", neutral);
        Ready(stone); Tick(); Check(ReferenceEquals(stone.sharedMaterial, neutral), "opaque water-named stone is not a film");
        var particles = new GameObject("TERRAIN_Water_Foam"); particles.transform.SetParent(crypt.transform);
        particles.AddComponent<ParticleSystem>(); var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = native; Ready(particleRenderer); Tick();
        Check(ReferenceEquals(particleRenderer.sharedMaterial, native), "water particles are not retuned as terrain films");

        // Destroyed queued members are pruned, and an event burst cannot discard a valid film.
        var destroyed = Surface(crypt.transform, "TERRAIN_Water_Plane Destroyed", native);
        Ready(destroyed); Object.DestroyImmediate(destroyed.gameObject); Tick();
        var burst = new List<Renderer>();
        for (int i = 0; i < 259; i++) { var film = Surface(crypt.transform, "TERRAIN_Water_Plane Burst " + i, native); burst.Add(film); Ready(film); }
        Tick(); foreach (Renderer film in burst) Check(Own(film), "bounded ready burst preserves every renderer");
        Check(Counter("_liveInstances") == Counter("_instancesMade") - Counter("_instancesDestroyed"), "private material live ledger is balanced");

        // All exact native renderer paths/materials from the shipped 380-source census.
        foreach (NativeWaterMaterial sample in NativeWaterCoverage.Materials)
        {
            var film = Surface(crypt.transform, "Native quality film", Array.Empty<Material>());
            Material authored = sample.Create(); new MaterialLoaderData().Complete(film, new[] { authored }); Tick();
            Check(Own(film), "native material identity and quality: " + sample.Name);
            Check(film.sharedMaterial.renderQueue == authored.renderQueue, "native render queue retained: " + sample.Name);
            Check(ReferenceEquals(film.sharedMaterial.GetTexture(WaterOwnSurface.NormalMapProperty), authored.GetTexture("_Normal_Map")), "native normal map retained: " + sample.Name);
        }
        foreach (NativeWaterSurface sample in NativeWaterCoverage.Surfaces)
        {
            Transform parent = crypt.transform;
            for (int i = 0; i < sample.Ancestry.Length - 1; i++)
            {
                var ancestor = new GameObject(sample.Ancestry[i]); ancestor.transform.SetParent(parent, false); parent = ancestor.transform;
            }
            var film = Surface(parent, sample.Ancestry[sample.Ancestry.Length - 1], Array.Empty<Material>());
            WaterTerrainVR.NotifyContentPlaced(crypt); Tick();
            Material authored = NativeWaterCoverage.Materials[sample.Material].Create();
            new MaterialLoaderData().Complete(film, new[] { authored }); Tick();
            Check(Own(film), "native full-game material-loader assignment: " + sample.Name);
            Check(WaterTerrainVR.IsOwnedWaterSurface(film), "native full-game current water ownership: " + sample.Name);
            Check(film.sharedMaterial.renderQueue == authored.renderQueue, "native surface queue retained: " + sample.Name);
        }
        Check(NativeWaterCoverage.Surfaces.Length == 45 && NativeWaterCoverage.Materials.Length == 14, "all 45 native film assignments and 14 materials execute");

        // A foreign native assignment after adoption owns its slot even on uninstall.
        var foreign = new Material(neutral); foreign.name = "Replacement native stone";
        var current = mixed.sharedMaterials; current[1] = foreign; mixed.sharedMaterials = current;
        WaterTerrainVR.Uninstall();
        Check(ReferenceEquals(mixed.sharedMaterials[1], foreign), "restoration retains later native unrelated material writes");
        Check(ReferenceEquals(mixed.sharedMaterials[2], low), "uninstall restores exact newest native water quality material");
        Check(ReferenceEquals(delayed.sharedMaterial, native), "uninstall restores exact original water material reference");
        Check(!WaterTerrainVR.IsOwnedWaterSurface(delayed), "uninstall retires exact owned-water provenance");
        Check(delayed.enabled && mixed.enabled, "water is never hidden");
        Ready(delayed); Check(ReferenceEquals(delayed.sharedMaterial, native), "late callback after uninstall is inert");

        WaterTerrainVR.Install(); Driver.GetType().GetField("UsePlacementHook", Private)!.SetValue(Driver, true);
        Ready(delayed); Tick(); Check(Own(delayed), "a fresh lifetime adopts normally");
        VRSession.IsRunning = false; Tick();
        Check(ReferenceEquals(delayed.sharedMaterial, native), "VR exit restores all owned material references");
        VRSession.IsRunning = true; Tick(slow: true); Check(Own(delayed), "VR re-entry seeds prior tiles after ownership reset");
        WaterTerrainVR.Uninstall();
        return _assertions;
    }
}
