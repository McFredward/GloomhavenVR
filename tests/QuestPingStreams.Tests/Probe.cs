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
    private static void Save(Color32[] pixels, string path)
    {
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
        texture.SetPixels32(pixels); texture.Apply(false); File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
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
        VRCameraPolicy.AllowedHead = camera;
        Color32[] before = Pixels(camera, target);
        QuestStandalonePlatform.Enabled = false;
        QuestPingStreams.Attach(ping);
        Check(ping.GetComponent<QuestPingStreams>() == null, "Desktop ping source receives no Quest component");
        QuestStandalonePlatform.Enabled = true;
        var managerOwner = new GameObject("exact-native-manager-boundary"); var manager = managerOwner.AddComponent<PingManager>(); manager.Assigned(ping);
        QuestPingStreams.OnPingShown(manager);
        Check(ping.GetComponent<QuestPingStreams>() != null, "Original assigned-ping owner admits its exact scoped consumer");
        Color32[] after = Pixels(camera, target);
        var draws = ping.GetComponentsInChildren<MeshRenderer>(true);
        Check(draws.Length == 2, "Only the two original particle consumers have owned draws");
        foreach (MeshRenderer draw in draws)
        {
            Mesh mesh = draw.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh.HasVertexAttribute(VertexAttribute.Color) && mesh.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.Float32, "Owned ping upload has explicit floating-point COLOR");
            Check(mesh.vertexCount > 0 && mesh.vertexCount < 4096, "The native particle geometry remains bounded");
        }
        for (int i = 0; i < 2; i++)
        {
            Check(Streams(renderers[i], original.ToArray()) && renderers[i].forceRenderingOff, "The native simulation retains all streams without drawing twice");
            Check(renderers[i].sharedMaterial.GetInstanceID() == materialIds[i] && renderers[i].sharedMaterial.mainTexture == textures[i], "Original ping material and texture identity remain unchanged");
            Check(renderers[i].bounds == bounds[i] && systems[i].particleCount == particleCounts[i] && (!renderers[i].sharedMaterial.HasProperty("_TintColor") || renderers[i].sharedMaterial.GetColor("_TintColor") == colors[i]), "Original ping simulation, geometry bounds and tint remain unchanged");
        }
        long error = 0; int visible = 0, max = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].r > 2 || before[i].g > 2 || before[i].b > 2) visible++;
            foreach (int delta in new[] { Math.Abs(before[i].r-after[i].r), Math.Abs(before[i].g-after[i].g), Math.Abs(before[i].b-after[i].b) }) { error += delta; max = Math.Max(max, delta); }
        }
        Check(visible > 50, "Actual original ping particles contribute visible native pixels");
        Directory.CreateDirectory("ProbeOutput");
        Save(before, "ProbeOutput/before.png"); Save(after, "ProbeOutput/after.png");
        Debug.Log("Pixel parity max=" + max + " error=" + error + " beforeVisible=" + visible);
        foreach (MeshRenderer draw in draws) Debug.Log("draw=" + draw.bounds + " source=" + draw.transform.parent.GetComponent<ParticleSystemRenderer>().bounds + " sourceScale=" + draw.transform.parent.lossyScale);
        Check(max <= 2 && error < before.Length * 3, "Owned ping upload preserves original rendered pixels");
        // The board rig rotates/translates/scales native particle owners.
        // BakeMesh(true) includes rotation+scale, and the owned draw must not
        // apply those a second time.
        ping.transform.SetPositionAndRotation(new Vector3(.2f,.1f,-.3f), Quaternion.Euler(0,31,0));
        ping.transform.localScale = Vector3.one * 1.25f;
        QuestStandalonePlatform.Enabled = false; Color32[] movedBefore = Pixels(camera, target);
        QuestStandalonePlatform.Enabled = true; Color32[] movedAfter = Pixels(camera, target);
        int movedMax = 0;
        for (int i = 0; i < movedBefore.Length; i++)
        {
            movedMax = Math.Max(movedMax, Math.Abs(movedBefore[i].r-movedAfter[i].r));
            movedMax = Math.Max(movedMax, Math.Abs(movedBefore[i].g-movedAfter[i].g));
            movedMax = Math.Max(movedMax, Math.Abs(movedBefore[i].b-movedAfter[i].b));
        }
        Check(movedMax <= 2, "Board translation, yaw and scale preserve native ping pixels");
        ping.SetActive(false);
        foreach (ParticleSystemRenderer renderer in renderers) Check(Streams(renderer, original.ToArray()) && !renderer.forceRenderingOff, "Pooling releases the exact original ping renderer");
        foreach (MeshRenderer draw in draws) Check(!draw.enabled, "Pooling disables every owned draw");
        ping.SetActive(true);
        foreach (ParticleSystem system in systems) system.Simulate(.5f, false, true, true);
        Pixels(camera, target);
        foreach (ParticleSystemRenderer renderer in renderers) Check(Streams(renderer, original.ToArray()) && renderer.forceRenderingOff, "Pooled native ping reapplies its scoped upload");
        MeshRenderer firstDraw = Array.Find(draws, draw => draw.transform.parent.GetComponent<ParticleSystemRenderer>() == renderers[0]);
        Material nativeMaterial = renderers[0].sharedMaterial;
        Material foreignMaterial = new Material(Shader.Find("Sprites/Default"));
        renderers[0].sharedMaterial = foreignMaterial; Pixels(camera, target);
        Check(!renderers[0].forceRenderingOff && !firstDraw.enabled, "A later material writer retains its native draw");
        renderers[0].sharedMaterial = nativeMaterial; Pixels(camera, target);
        Check(renderers[0].forceRenderingOff && firstDraw.enabled, "Restoring the exact original material resumes the owned draw");
        UnityEngine.Object.DestroyImmediate(foreignMaterial);
        renderers[0].SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position });
        QuestPingStreams.ResetAll();
        Check(Streams(renderers[0], ParticleSystemVertexStream.Position), "A later foreign vertex writer retains ownership during reset");
        Check(Streams(renderers[1], original.ToArray()), "Reset restores the untouched original ping stream source");
        UnityEngine.Object.DestroyImmediate(ping); UnityEngine.Object.DestroyImmediate(managerOwner);

        // Wrong material family and mesh/extended streams must remain inert.
        var foreign = new GameObject("foreign-particle"); var ps = foreign.AddComponent<ParticleSystem>();
        ParticleSystemRenderer other = foreign.GetComponent<ParticleSystemRenderer>();
        other.sharedMaterial = new Material(Shader.Find("Sprites/Default")); other.SetActiveVertexStreams(original);
        QuestPingStreams.Attach(foreign);
        Pixels(camera, target);
        Check(Streams(other, original.ToArray()) && foreign.GetComponentInChildren<MeshRenderer>() == null, "Foreign particle materials retain their authored streams");
        UnityEngine.Object.DestroyImmediate(other.sharedMaterial); UnityEngine.Object.DestroyImmediate(foreign);
        UnityEngine.Object.DestroyImmediate(owner); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        Directory.CreateDirectory("ProbeOutput"); File.WriteAllText("ProbeOutput/results.json", "{\"assertions\":" + checks + ",\"visiblePixels\":" + visible + ",\"pixelMaxDelta\":" + max + ",\"hardwareVerified\":false}\n");
        Debug.Log("PASS Quest ping streams: " + checks + " assertions, actual native pixels=" + visible);
    }
}
