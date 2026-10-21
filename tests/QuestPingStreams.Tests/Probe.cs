using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Board.Patches;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;

public static class QuestPingStreamsProbe
{
    private static int checks;
    private static readonly List<ParticleSystemVertexStream> original = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private static readonly List<ParticleSystemVertexStream> packed = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Streams(ParticleSystemRenderer renderer, List<ParticleSystemVertexStream> wanted)
    {
        var actual = new List<ParticleSystemVertexStream>(); renderer.GetActiveVertexStreams(actual);
        if (actual.Count != wanted.Count) return false;
        for (int i = 0; i < actual.Count; i++) if (actual[i] != wanted[i]) return false;
        return true;
    }
    public static void Run()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += WaitForPlay; EditorApplication.EnterPlaymode();
    }
    private static void WaitForPlay()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.update -= WaitForPlay;
        try
        {
            if (Environment.GetEnvironmentVariable("QUEST_PING_AUTOPLAY_ONLY") != "1") Probe();
            QuestPingAutoClock.Begin();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static Color32[] Pixels(Camera camera, RenderTexture target)
    {
        camera.Render(); RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(false); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }
    private static void Save(Color32[] pixels, string path)
    {
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
        texture.SetPixels32(pixels); texture.Apply(false); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
    }
    private static void Probe()
    {
        GameObject prefab = Resources.Load<GameObject>("NativePing"); Check(prefab != null, "Exact original ping prefab imports");
        GameObject ping = UnityEngine.Object.Instantiate(prefab);
        var renderers = ping.GetComponentsInChildren<ParticleSystemRenderer>(true); var systems = ping.GetComponentsInChildren<ParticleSystem>(true);
        Check(renderers.Length == 2 && systems.Length == 2, "Both original ping particle consumers are retained");
        foreach (ParticleSystem system in systems)
        {
            Check(system.main.cullingMode == ParticleSystemCullingMode.Pause, "Original ping authors visibility-based simulation pause");
            system.useAutoRandomSeed = false; system.randomSeed = 21; system.Simulate(.5f, false, true, true);
        }
        var materialIds = new int[2]; var textures = new Texture[2]; var bounds = new Bounds[2]; var particleCounts = new int[2]; var colors = new Color[2];
        for (int i = 0; i < 2; i++)
        {
            Check(Streams(renderers[i], original), "Native ping source has its authored stream layout");
            materialIds[i] = renderers[i].sharedMaterial.GetInstanceID(); textures[i] = renderers[i].sharedMaterial.mainTexture;
            bounds[i] = renderers[i].bounds; particleCounts[i] = systems[i].particleCount;
            colors[i] = renderers[i].sharedMaterial.HasProperty("_TintColor") ? renderers[i].sharedMaterial.GetColor("_TintColor") : Color.white;
        }
        var owner = new GameObject("ping-camera"); Camera camera = owner.AddComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.transform.position = new Vector3(0, 5, -5); camera.transform.LookAt(Vector3.zero);
        camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.orthographic = true; camera.orthographicSize = 2;
        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
        VRCameraPolicy.AllowedHead = camera; Color32[] before = Pixels(camera, target);
        QuestStandalonePlatform.Enabled = false; QuestPingStreams.Attach(ping);
        Check(ping.GetComponent<QuestPingStreams>() == null, "Desktop ping source receives no Quest component"); QuestStandalonePlatform.Enabled = true;
        var managerOwner = new GameObject("exact-native-manager-boundary"); var manager = managerOwner.AddComponent<PingManager>(); manager.Assigned(ping); QuestPingStreams.OnPingShown(manager);
        Check(ping.GetComponent<QuestPingStreams>() != null, "Original assigned-ping owner admits its exact scoped consumer");
        foreach (var renderer in renderers) Check(Streams(renderer, packed), "Original ping retains its exact consumed native streams");
        bool nativeVisible = false;
        Camera.CameraCallback inspect = active => {
            if (active != camera) return;
            foreach (ParticleSystemRenderer renderer in renderers) Check(!renderer.forceRenderingOff && renderer.enabled, "Native ping renderer remains enabled during actual head culling");
            nativeVisible = true;
        };
        Camera.onPreCull += inspect; Color32[] after = Pixels(camera, target); Camera.onPreCull -= inspect;
        Check(nativeVisible, "The actual head callback observes the native ping draw");
        Check(ping.GetComponentsInChildren<MeshRenderer>(true).Length == 0, "Ping animation uses no baked MeshRenderer replacement");
        for (int i = 0; i < 2; i++)
        {
            Check(renderers[i].sharedMaterial.GetInstanceID() == materialIds[i] && renderers[i].sharedMaterial.mainTexture == textures[i], "Original ping material and texture identity remain unchanged");
            Check(renderers[i].bounds == bounds[i] && systems[i].particleCount == particleCounts[i] && (!renderers[i].sharedMaterial.HasProperty("_TintColor") || renderers[i].sharedMaterial.GetColor("_TintColor") == colors[i]), "Original ping simulation, geometry bounds and tint remain unchanged");
        }
        long error = 0; int visible = 0, max = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].r > 2 || before[i].g > 2 || before[i].b > 2) visible++;
            foreach (int delta in new[] { Math.Abs(before[i].r-after[i].r), Math.Abs(before[i].g-after[i].g), Math.Abs(before[i].b-after[i].b) }) { error += delta; max = Math.Max(max, delta); }
        }
        Check(visible > 50, "Actual original ping particles contribute visible native pixels"); Directory.CreateDirectory("ProbeOutput");
        Save(before, "ProbeOutput/before.png"); Save(after, "ProbeOutput/after.png");
        Check(max <= 2 && error < before.Length * 3, "Native stream request preserves original rendered pixels");
        ping.SetActive(false); foreach (var renderer in renderers) Check(Streams(renderer, original), "Pooling restores exact original ping streams");
        ping.SetActive(true); foreach (var renderer in renderers) Check(Streams(renderer, packed), "Pooled native ping resumes its consumed stream request");
        Material nativeMaterial = renderers[0].sharedMaterial; Shader nativeShader = nativeMaterial.shader;
        ping.SetActive(false); nativeMaterial.shader = Shader.Find("Sprites/Default"); ping.SetActive(true);
        Check(Streams(renderers[0], original) && !renderers[0].forceRenderingOff, "Changed native contracts retain authored streams");
        ping.SetActive(false); nativeMaterial.shader = nativeShader; ping.SetActive(true);
        Check(Streams(renderers[0], packed), "Original shader can resume its qualified native stream request");
        renderers[0].forceRenderingOff = true; Pixels(camera, target);
        Check(renderers[0].forceRenderingOff, "Foreign visibility mask is not cleared by any ping callback"); renderers[0].forceRenderingOff = false;
        var foreignStreams = new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position }; renderers[0].SetActiveVertexStreams(foreignStreams);
        QuestPingStreams.ResetAll(); Check(Streams(renderers[0], foreignStreams), "A later foreign vertex writer retains ownership during reset");
        Check(Streams(renderers[1], original), "Reset restores the untouched original ping stream source");
        UnityEngine.Object.DestroyImmediate(ping); UnityEngine.Object.DestroyImmediate(managerOwner);
        var foreign = new GameObject("foreign-particle"); foreign.AddComponent<ParticleSystem>(); ParticleSystemRenderer other = foreign.GetComponent<ParticleSystemRenderer>();
        other.sharedMaterial = new Material(Shader.Find("Sprites/Default")); other.SetActiveVertexStreams(original); QuestPingStreams.Attach(foreign);
        Check(Streams(other, original), "Foreign particle materials retain their authored streams");
        UnityEngine.Object.DestroyImmediate(other.sharedMaterial); UnityEngine.Object.DestroyImmediate(foreign);
        UnityEngine.Object.DestroyImmediate(owner); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        File.WriteAllText("ProbeOutput/results.json", "{\"assertions\":" + checks + ",\"visiblePixels\":" + visible + ",\"pixelMaxDelta\":" + max + ",\"hardwareVerified\":false}\n");
        Debug.Log("PASS Quest ping streams: " + checks + " assertions, actual native pixels=" + visible);
    }
}
