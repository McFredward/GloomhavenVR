using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object = UnityEngine.Object;

public static class InteractionProgram
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static Bounds Bounds(TownServiceRitualLayout.Placement p)
    {
        Vector3 center = p.Position + TownServiceRitualLayout.Origin;
        var bounds = new Bounds(center, Vector3.zero);
        foreach (float x in new[] { -.5f, .5f }) foreach (float y in new[] { -.5f, .5f }) foreach (float z in new[] { -.0015f, .0015f })
            bounds.Encapsulate(center + p.Rotation * new Vector3(p.Size.x * x, p.Size.y * y, z));
        return bounds;
    }
    public static int Run()
    {
        checks = 0;
        foreach (int count in new[] { 1, 13, 21, 31, 32, 40, TownServiceRitualLayout.MaxCards }) Stock(count, true);
        foreach (int count in new[] { 1, 14, 28, 30, 40, TownServiceRitualLayout.MaxRunes }) Stock(count, false);
        foreach (bool sell in new[] { false, true })
        {
            Bounds bounds = Bounds(TownServiceRitualLayout.Mode(sell));
            Check(bounds.max.y < .925f && bounds.min.y > .82f && bounds.max.z < -.36f && bounds.min.z > -.37f,
                "mode medallions mount on the real front apron below stock");
        }
        for (int i = 0; i < TownServiceRitualLayout.MaxOfferings; i++)
        {
            var pose = TownServiceRitualLayout.Offering(i, TownServiceRitualLayout.MaxOfferings);
            Vector3 center = pose.Position + TownServiceRitualLayout.Origin;
            Check(Mathf.Abs(center.y - .958f) < .00001f && Quaternion.Angle(pose.Rotation * Quaternion.Euler(-90f, 0f, 0f), Quaternion.identity) < .001f,
                "native offering coin lies flat on the real worktop");
            Check(Mathf.Abs(center.x) + .0375f < .75f && Mathf.Abs(center.z) + .0375f < .338f, "all offering faces stay on altar");
            Check(center.x > .16f || center.z - .0375f > .03f, "offering does not cover the native devotion ledger");
            Check(Vector2.Distance(new Vector2(center.x, center.z), new Vector2(0f, .18f)) > .16f + .0375f, "offering does not overlap native bowl");
        }
        bool rejected = false;
        try { TownServiceRitualLayout.Card(0, TownServiceRitualLayout.MaxCards + 1); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "overflow restores the complete native service instead of hiding entries");
        rejected = false;
        try { TownServiceRitualLayout.Rune(0, TownServiceRitualLayout.MaxRunes + 1); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "rune overflow restores the complete native service instead of hiding entries");
        Render();
        return checks;
    }
    private static void Stock(int count, bool cards)
    {
        var poses = new List<TownServiceRitualLayout.Placement>();
        for (int i = 0; i < count; i++)
        {
            var p = cards ? TownServiceRitualLayout.Card(i, count) : TownServiceRitualLayout.Rune(i, count); poses.Add(p);
            Bounds b = Bounds(p);
            Check(b.min.x >= -.75f && b.max.x <= .75f && b.min.z >= -.338f && b.max.z <= .338f, "every complete projected face stays on the worktop");
            Check(Mathf.Abs(b.min.y - .959f) < .00001f, "every file lower edge is physically supported rather than floating");
            Check(b.max.x <= -.164f || b.min.x >= .169f, "stock clears selected card and original capacity book");
            Check(b.max.z <= .101f, "stock clears native rear lanterns and alchemy vessels");
            for (int j = 0; j < i; j++)
            {
                var previous = poses[j]; Vector3 d = p.Position - previous.Position;
                bool sideGap = Mathf.Abs(d.x) > (p.Size.x + previous.Size.x) * .5f + .004f;
                float separation = Mathf.Abs(Vector3.Dot(d, p.Rotation * Vector3.forward));
                Check(sideGap || separation >= .006f, "parallel stock faces and millimetre rims cannot intersect");
            }
        }
    }
    private static void Render()
    {
        string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-layoutEvidence"); if (at < 0) return;
        string path = args[at + 1]; var root = new GameObject("layout diagnostic"); var materials = new List<Material>();
        void Box(string name, Vector3 position, Vector3 size, Quaternion rotation, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root.transform, false);
            go.transform.localPosition = position; go.transform.localScale = size; go.transform.localRotation = rotation;
            var material = new Material(Shader.Find("Unlit/Color")) { color = color }; materials.Add(material); go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        Box("existing worktop", new Vector3(0f, .925f, 0f), new Vector3(1.5f, .06f, .676f), Quaternion.identity, new Color(.17f, .11f, .07f));
        Box("native capacity book reserved", new Vector3(0f, .972f, .22f), new Vector3(.32f, .03f, .32f), Quaternion.identity, new Color(.65f, .53f, .32f));
        Box("selected original card", new Vector3(0f, .995f, -.08f), new Vector3(.25f, .003f, .38f), Quaternion.identity, new Color(.37f, .35f, .30f));
        foreach (bool cards in new[] { true, false }) for (int i = 0; i < (cards ? 31 : 28); i++)
        {
            var p = cards ? TownServiceRitualLayout.Card(i, 31) : TownServiceRitualLayout.Rune(i, 28);
            Color c = cards ? new Color(.28f, .44f, .61f) : new Color(.49f, .31f, .64f);
            c *= i % 2 == 0 ? 1f : .75f; c.a = 1f;
            Box((cards ? "card " : "rune ") + i, p.Position + TownServiceRitualLayout.Origin,
                new Vector3(p.Size.x, p.Size.y, .003f), p.Rotation, c);
        }
        foreach (bool sell in new[] { false, true })
        {
            var p = TownServiceRitualLayout.Mode(sell);
            Box(sell ? "remove" : "buy", p.Position + TownServiceRitualLayout.Origin,
                new Vector3(p.Size.x, p.Size.y, .003f), p.Rotation, new Color(.75f, .58f, .2f));
        }
        var cameraObject = new GameObject("layout camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(.20f, 1.95f, -1.05f); camera.transform.LookAt(new Vector3(0f, .96f, 0f));
        camera.fieldOfView = 65f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .07f, .075f);
        var rt = new RenderTexture(1000, 750, 24); var image = new Texture2D(1000, 750, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1000, 750), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(path, "native-full-stock-layout.png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null; Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(rt); Object.DestroyImmediate(image); foreach (var material in materials) Object.DestroyImmediate(material);
        }
    }
}
