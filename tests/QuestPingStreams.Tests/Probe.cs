using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Board.Patches;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class QuestPingStreamsProbe
{
    private static int checks;
    private static readonly List<ParticleSystemVertexStream> original = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Streams(ParticleSystemRenderer renderer, params ParticleSystemVertexStream[] wanted)
    {
        var actual = new List<ParticleSystemVertexStream>(); renderer.GetActiveVertexStreams(actual);
        if (actual.Count != wanted.Length) return false;
        for (int i = 0; i < actual.Count; i++) if (actual[i] != wanted[i]) return false;
        return true;
    }
    public static void Run()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += WaitForPlay;
        EditorApplication.EnterPlaymode();
    }
    private static void WaitForPlay()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.update -= WaitForPlay;
        try { Probe(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static Color32[] Pixels(Camera camera, RenderTexture target)
    {
        camera.Render(); RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(false); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }
    private static void Probe()
    {
        GameObject prefab = Resources.Load<GameObject>("NativePing");
        Check(prefab != null, "Exact original ping prefab imports");
        GameObject ping = UnityEngine.Object.Instantiate(prefab);
        var renderers = ping.GetComponentsInChildren<ParticleSystemRenderer>(true);
        var systems = ping.GetComponentsInChildren<ParticleSystem>(true);
        Check(renderers.Length == 2 && systems.Length == 2, "Both original ping particle consumers are retained");
        foreach (ParticleSystem system in systems) { system.useAutoRandomSeed = false; system.randomSeed = 21; system.Simulate(.5f, false, true, true); }
        var materialIds = new int[2]; var textures = new Texture[2]; var bounds = new Bounds[2];
        var particleCounts = new int[2]; var colors = new Color[2];
        for (int i = 0; i < 2; i++)
        {
            Check(Streams(renderers[i], original.ToArray()), "Native ping source has its authored stream layout");
            materialIds[i] = renderers[i].sharedMaterial.GetInstanceID(); textures[i] = renderers[i].sharedMaterial.mainTexture;
            bounds[i] = renderers[i].bounds; particleCounts[i] = systems[i].particleCount;
            colors[i] = renderers[i].sharedMaterial.HasProperty("_TintColor") ? renderers[i].sharedMaterial.GetColor("_TintColor") : Color.white;
        }
        var owner = new GameObject("ping-camera"); Camera camera = owner.AddComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.transform.position = new Vector3(0, 5, -5); camera.transform.LookAt(Vector3.zero);
        camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.orthographic = true; camera.orthographicSize = 2;
        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
        Color32[] before = Pixels(camera, target);
        QuestStandalonePlatform.Enabled = false;
        QuestPingStreams.Attach(ping);
        Check(ping.GetComponent<QuestPingStreams>() == null, "Desktop ping source receives no Quest component");
        QuestStandalonePlatform.Enabled = true;
        var managerOwner = new GameObject("exact-native-manager-boundary"); var manager = managerOwner.AddComponent<PingManager>(); manager.Assigned(ping);
        QuestPingStreams.OnPingShown(manager);
        Check(ping.GetComponent<QuestPingStreams>() != null, "Original assigned-ping owner admits its exact scoped consumer");
        for (int i = 0; i < 2; i++)
        {
            Check(Streams(renderers[i], ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV), "Ping upload uses its exact consumed native stream layout");
            Check(renderers[i].sharedMaterial.GetInstanceID() == materialIds[i] && renderers[i].sharedMaterial.mainTexture == textures[i], "Original ping material and texture identity remain unchanged");
            Check(renderers[i].bounds == bounds[i] && systems[i].particleCount == particleCounts[i] && (!renderers[i].sharedMaterial.HasProperty("_TintColor") || renderers[i].sharedMaterial.GetColor("_TintColor") == colors[i]), "Original ping simulation, geometry bounds and tint remain unchanged");
        }
        Color32[] after = Pixels(camera, target); long error = 0; int visible = 0, max = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].r > 2 || before[i].g > 2 || before[i].b > 2) visible++;
            foreach (int delta in new[] { Math.Abs(before[i].r-after[i].r), Math.Abs(before[i].g-after[i].g), Math.Abs(before[i].b-after[i].b) }) { error += delta; max = Math.Max(max, delta); }
        }
        Check(visible > 50, "Actual original ping particles contribute visible native pixels");
        Check(max <= 2 && error < before.Length * 3, "Repacked native ping preserves original rendered pixels");
        ping.SetActive(false);
        foreach (ParticleSystemRenderer renderer in renderers) Check(Streams(renderer, original.ToArray()), "Pooling restores exact original ping streams");
        ping.SetActive(true);
        foreach (ParticleSystemRenderer renderer in renderers) Check(Streams(renderer, ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV), "Pooled native ping reapplies its scoped upload");
        renderers[0].SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position });
        QuestPingStreams.ResetAll();
        Check(Streams(renderers[0], ParticleSystemVertexStream.Position), "A later foreign vertex writer retains ownership during reset");
        Check(Streams(renderers[1], original.ToArray()), "Reset restores the untouched original ping stream source");
        UnityEngine.Object.DestroyImmediate(ping); UnityEngine.Object.DestroyImmediate(managerOwner);
        UnityEngine.Object.DestroyImmediate(owner); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        // Wrong material family and mesh/extended streams must remain inert.
        var foreign = new GameObject("foreign-particle"); var ps = foreign.AddComponent<ParticleSystem>();
        ParticleSystemRenderer other = foreign.GetComponent<ParticleSystemRenderer>();
        other.sharedMaterial = new Material(Shader.Find("Sprites/Default")); other.SetActiveVertexStreams(original);
        QuestPingStreams.Attach(foreign);
        Check(Streams(other, original.ToArray()), "Foreign particle materials retain their authored streams");
        UnityEngine.Object.DestroyImmediate(other.sharedMaterial); UnityEngine.Object.DestroyImmediate(foreign);
        Directory.CreateDirectory("ProbeOutput"); File.WriteAllText("ProbeOutput/results.json", "{\"assertions\":" + checks + ",\"visiblePixels\":" + visible + ",\"pixelMaxDelta\":" + max + ",\"hardwareVerified\":false}\n");
        Debug.Log("PASS Quest ping streams: " + checks + " assertions, actual native pixels=" + visible);
    }
}
