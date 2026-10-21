using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GloomhavenVR.Board.Patches;
using GloomhavenVR.Core;
using UnityEngine;

internal static class QuestNativeFxLayoutPose
{
    private static readonly List<ParticleSystemVertexStream> Compact = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private static readonly List<ParticleSystemVertexStream> Normal = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }

    internal static void Run()
    {
        var rows = new List<string>();
        foreach (bool shield in new[] { false, true })
        {
            if (shield && Resources.Load<GameObject>("NativeShield") == null) continue;
            foreach (int layout in new[] { 4, 3 })
                foreach (int pose in new[] { 0, 1, 2 }) rows.Add(Observe(shield, layout, pose));
        }
        // Repeated rejected exact native owners exercise the small per-kind
        // diagnostic cap. Every custom source remains native, without uploads.
        foreach (bool shield in new[] { false, true })
        {
            if (shield && Resources.Load<GameObject>("NativeShield") == null) continue;
            for (int repeat = 0; repeat < 4; repeat++) RejectCustom(shield);
        }
        File.WriteAllText("ProbeOutput/layout-pose.json", "{\"assertions\":" + checks + ",\"cases\":[" + string.Join(",", rows) + "],\"hardwareVerified\":false}\n");
        Debug.Log("PASS native FX layouts and scenario poses: " + checks + " assertions / " + rows.Count + " pixel cases");
    }

    private static string Observe(bool shield, int layout, int pose)
    {
        var root = new GameObject("scenario-source-frame");
        if (pose == 1) { root.transform.position = new Vector3(7, 2, -4); root.transform.rotation = Quaternion.Euler(0, 37, 0); root.transform.localScale = Vector3.one * 13; }
        var effect = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(shield ? "NativeShield" : "NativePing"), root.transform, false);
        var sources = effect.GetComponentsInChildren<ParticleSystemRenderer>(true);
        var streams = layout == 3 ? Compact : Normal;
        foreach (var source in sources)
            if (source.renderMode == (shield ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard)
                && source.sharedMaterial.shader.name != "KriptoFX/RFX4/Particle") source.SetActiveVertexStreams(streams);
        foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
        { system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); system.useAutoRandomSeed = false; system.randomSeed = 21; system.Simulate(shield ? .3f : .5f, false, true, true); }
        var cameraRoot = new GameObject("tracked-rig-frame");
        if (pose == 2) { cameraRoot.transform.rotation = Quaternion.Euler(0, 37, 0); cameraRoot.transform.localScale = Vector3.one * 13; }
        var cameraObject = new GameObject("native-fx-head"); cameraObject.transform.SetParent(cameraRoot.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        Vector3 view = shield ? new Vector3(0, 2, -5) : new Vector3(0, 5, -5);
        Vector3 targetPoint = shield ? new Vector3(0, 1, 0) : Vector3.zero;
        // Native Local particle scaling deliberately ignores an ancestor's
        // scale for the emitted shield geometry. Aim at its actual native
        // bounds, not a fictitious parent-scaled shield centre.
        Vector3 worldTarget = shield ? sources[0].bounds.center : root.transform.position;
        Quaternion viewRotation = pose == 2 ? cameraRoot.transform.rotation : root.transform.rotation;
        camera.transform.position = worldTarget + viewRotation * (view - targetPoint) * (pose == 1 ? 13 : 1);
        camera.transform.LookAt(worldTarget);
        camera.orthographic = true; camera.orthographicSize = 2; camera.aspect = 1; camera.nearClipPlane = .01f; camera.farClipPlane = 1000;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
        QuestStandalonePlatform.Enabled = false; var before = Pixels(camera, target);
        QuestStandalonePlatform.Enabled = true; VRCameraPolicy.AllowedHead = camera;
        if (shield) QuestPingStreams.AttachShield(effect); else QuestPingStreams.Attach(effect);
        Check(effect.GetComponentsInChildren<MeshRenderer>(true).Length == 2, "Qualified source layout receives exactly its two native effect uploads");
        var after = Pixels(camera, target); int visible = 0, max = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].r > 2 || before[i].g > 2 || before[i].b > 2) visible++;
            max = Math.Max(max, Math.Max(Math.Abs(before[i].r - after[i].r), Math.Max(Math.Abs(before[i].g - after[i].g), Math.Abs(before[i].b - after[i].b))));
        }
        Debug.Log("LAYOUT_POSE shield=" + shield + " streams=" + layout + " pose=" + pose + " visible=" + visible + " max=" + max);
        Check(visible > 30, "Original FX contributes measurable pixels under the actual scenario pose");
        Check(max <= 2, "Scenario root scale and yaw preserve exact original FX pixels");
        var actual = new List<ParticleSystemVertexStream>();
        foreach (var source in sources)
        {
            if (source.sharedMaterial.shader.name == "KriptoFX/RFX4/Particle") continue;
            source.GetActiveVertexStreams(actual);
            Check(actual.Count == streams.Count, "Native source retains its admitted stream layout");
            for (int i = 0; i < actual.Count; i++) Check(actual[i] == streams[i], "Native stream fields remain authored");
            Check(!source.forceRenderingOff && source.enabled, "Scenario pose keeps original native visibility ownership");
        }
        string prefix = "ProbeOutput/" + (shield ? "shield" : "ping") + "-layout" + layout + "-pose" + pose;
        Save(before, prefix + "-before.png"); Save(after, prefix + "-after.png");
        QuestPingStreams.ResetAll(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraRoot); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        return "{\"shield\":" + shield.ToString().ToLowerInvariant() + ",\"streams\":" + layout + ",\"pose\":" + pose + ",\"visiblePixels\":" + visible + ",\"maximumRgbDifference\":" + max + "}";
    }

    private static void RejectCustom(bool shield)
    {
        var effect = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(shield ? "NativeShield" : "NativePing"));
        var custom = new List<ParticleSystemVertexStream>(Normal) { ParticleSystemVertexStream.UV2 };
        foreach (var source in effect.GetComponentsInChildren<ParticleSystemRenderer>(true)) source.SetActiveVertexStreams(custom);
        if (shield) QuestPingStreams.AttachShield(effect); else QuestPingStreams.Attach(effect);
        Check(effect.GetComponentsInChildren<MeshRenderer>(true).Length == 0, "Custom native layouts retain complete original rendering");
        var actual = new List<ParticleSystemVertexStream>();
        foreach (var source in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
        { source.GetActiveVertexStreams(actual); Check(actual.Count == custom.Count, "Custom source stream writer is preserved"); }
        UnityEngine.Object.DestroyImmediate(effect);
    }

    private static Color32[] Pixels(Camera camera, RenderTexture target)
    {
        camera.Render(); var previous = RenderTexture.active; var image = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); image.Apply(false); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }
    private static void Save(Color32[] pixels, string path)
    { var image = new Texture2D(128, 128, TextureFormat.RGBA32, false, true); image.SetPixels32(pixels); image.Apply(false); File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); }
}
