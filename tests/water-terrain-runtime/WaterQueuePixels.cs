using System;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class WaterQueuePixels
{
    public static int Run()
    {
        NativePublicationIntegration.Configure(); VRSession.IsRunning = true;
        WaterTerrainVR.Install();
        object driver = typeof(WaterTerrainVR).GetField("_driver", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var root = new GameObject("Native pond queue fixture"); root.transform.position = new Vector3(1000, 0, 0);
        var tile = root.AddComponent<ProceduralMapTile>(); SceneRegistry.MapTiles.Tiles.Add(tile);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad); ground.transform.SetParent(root.transform, false);
        ground.transform.localRotation = Quaternion.Euler(90, 0, 0); ground.transform.localScale = new Vector3(4, 4, 4);
        var groundMaterial = new Material(Shader.Find("Unlit/Color")); groundMaterial.color = Color.red;
        // Native opaque/cutout ground is allowed anywhere in the <=2500 phase;
        // it must remain behind a transparent film regardless of material sorting.
        groundMaterial.renderQueue = 2000;
        ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        var film = GameObject.CreatePrimitive(PrimitiveType.Quad); film.name = "FR_SW_Pond_Small"; film.transform.SetParent(root.transform, false);
        film.transform.localPosition = new Vector3(0, .15f, 0); film.transform.localRotation = Quaternion.Euler(90, 0, 0); film.transform.localScale = new Vector3(3, 3, 3);
        var renderer = film.GetComponent<Renderer>();
        var cameraObject = new GameObject("Water queue camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.transform.position = root.transform.position + Vector3.up * 5;
        camera.transform.rotation = Quaternion.Euler(90, 0, 0); camera.orthographic = true; camera.orthographicSize = 1;
        camera.nearClipPlane = .1f; camera.farClipPlane = 10; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.opaqueSortMode = OpaqueSortMode.FrontToBack;
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
        var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false, true); int assertions = 0;
        try
        {
            foreach (int groundQueue in new[] { 2000, 2450 })
            {
            groundMaterial.renderQueue = groundQueue;
            renderer.enabled = false; camera.Render();
            RenderTexture originalTarget = RenderTexture.active; RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply(); RenderTexture.active = originalTarget;
            Color groundPixel = readback.GetPixel(32, 32);
            if (groundPixel.r < .5f) throw new Exception("native opaque ground must actually render: " + groundPixel);
            renderer.enabled = true; assertions++;
            foreach (int authoredQueue in new[] { 2000, 2900, 3000 })
            {
                var native = new Material(Shader.Find("VFX/Water_Shd")); native.name = "CaveWater_Mat"; native.renderQueue = authoredQueue;
                native.SetColor("_Color_Tint", new Color(.1f, .9f, .15f, .8f));
                new MaterialLoaderData().Complete(renderer, new[] { native });
                driver.GetType().GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(driver, null);
                camera.Render(); RenderTexture prior = RenderTexture.active; RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply(); RenderTexture.active = prior;
                Color pixel = readback.GetPixel(32, 32);
                assertions++;
                if (pixel.g < .025f) throw new Exception("opaque queue film is visible above native ground: authored " + authoredQueue + " / owned " + renderer.sharedMaterial.renderQueue + " / pixel " + pixel);
                if (groundQueue == 2450)
                {
                    assertions++;
                    if (renderer.sharedMaterial.renderQueue != (authoredQueue <= 2500 ? 3000 : authoredQueue)) throw new Exception("only opaque film queues enter the transparent phase");
                }
                WaterTerrainVR.Uninstall();
                assertions++;
                if (!ReferenceEquals(renderer.sharedMaterial, native) || native.renderQueue != authoredQueue) throw new Exception("pixel fixture restores the exact native queue and material");
                Object.DestroyImmediate(native);
                WaterTerrainVR.Install(); driver = typeof(WaterTerrainVR).GetField("_driver", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            }
            }
        }
        finally
        {
            WaterTerrainVR.Uninstall(); camera.targetTexture = null; target.Release();
            Object.DestroyImmediate(target); Object.DestroyImmediate(readback); Object.DestroyImmediate(groundMaterial);
        }
        return assertions;
    }
}
