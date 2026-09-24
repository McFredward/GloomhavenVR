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
        var sections = new ushort[] {10,11,13,14,15,16};
        var occupied = new List<Bounds>();
        foreach (ushort id in sections)
        {
            var placement = TownServiceRitualLayout.Folio(id);
            Bounds bounds = Bounds(placement);
            Check(bounds.min.x >= -.75f && bounds.max.x <= .75f,
                "complete native folio sections stay within the stand width");
            Check(bounds.min.y >= .985f && bounds.max.y <= 1.59f,
                "native folio stays above tabletop and below resident face");
            Check(bounds.min.z >= -.338f && bounds.max.z <= .338f,
                "native folio clears front edge and rear decoration");
            foreach(Bounds previous in occupied)
                Check(!previous.Intersects(bounds),"native folio content and original controls never overlap");
            occupied.Add(bounds);
            if(id==10) Check(Vector3.Dot(placement.Rotation*Vector3.up,Vector3.up)>.999f,
                "native enhancement options face the visitor upright");
            if(id==11) Check(placement.Size.y<=.43f && placement.Position.x<-.3f,
                "selected native card hotspots stay readable beside the options");
        }
        for (int count = 1; count <= TownServiceRitualLayout.MaxOfferings; count++)
        {
            var purseBounds = new List<Bounds>();
            for (int i = 0; i < count; i++)
            {
                var pose = TownServiceRitualLayout.Offering(i, count);
                Check(Quaternion.Angle(pose.Rotation, Quaternion.identity) < .001f,
                    "purse rests upright above the hand rather than lying like a card");
                var bounds = new Bounds(pose.Position, new Vector3(pose.Size.x, pose.Size.y, .1f));
                foreach (Bounds previous in purseBounds)
                    Check(!previous.Intersects(bounds), "all native blessing purses have separate reachable bodies");
                purseBounds.Add(bounds);
                Check(bounds.min.x > -.30f && bounds.max.x < .30f && bounds.min.y > -.01f,
                    "complete native purse choice stays above the palm within comfortable reach");
            }
        }
        bool rejected = false;
        try { TownServiceRitualLayout.Offering(0, TownServiceRitualLayout.MaxOfferings + 1); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "temple overflow restores complete native service instead of hiding entries");
        rejected = false;
        try { TownServiceRitualLayout.Folio(99); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "unknown native section cannot silently overlap another control");
        checks += BookInkProof.Run();
        checks += NativeBookProof.Run();
        Render();
        return checks;
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
        foreach (ushort id in new ushort[] {10,11,13,14,15,16})
        {
            var p=TownServiceRitualLayout.Folio(id);
            Color color=id==10?new Color(.28f,.44f,.61f):id==11?new Color(.49f,.31f,.64f):new Color(.75f,.58f,.2f);
            Box("original native section "+id,p.Position+TownServiceRitualLayout.Origin,
                new Vector3(p.Size.x,p.Size.y,.003f),p.Rotation,color);
        }
        var cameraObject = new GameObject("layout camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(.20f, 1.95f, -1.05f); camera.transform.LookAt(new Vector3(0f, .96f, 0f));
        camera.fieldOfView = 65f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .07f, .075f);
        var rt = new RenderTexture(1000, 750, 24); var image = new Texture2D(1000, 750, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1000, 750), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(path, "native-folio-layout.png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null; Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(rt); Object.DestroyImmediate(image); foreach (var material in materials) Object.DestroyImmediate(material);
        }
    }
}
