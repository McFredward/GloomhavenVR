using System;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object = UnityEngine.Object;

static class Program
{
    static int assertions;
    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    static void Near(float value, float expected, string message) => Check(MathF.Abs(value - expected) < 1e-6f, message);
    static Transform Plate(Mesh mesh, Material material) => new() { Filter = new() { sharedMesh = mesh }, Renderer = new() { sharedMaterial = material } };
    static void VerifyField(Mesh mesh, Rect shown, Rect host, float progress)
    {
        float aspect = host.width / MathF.Max(host.height, .01f);
        for (int i = 0; i < mesh.uv.Length; i++)
        {
            Vector2 uv = mesh.uv[i];
            Vector2 hostUv = new(Math.Clamp((shown.xMin + uv.x * shown.width - host.xMin) / host.width, 0, 1),
                Math.Clamp((shown.yMin + uv.y * shown.height - host.yMin) / host.height, 0, 1));
            float expected = WindowMaterialiseField.Presence(WindowMaterialiseField.Threshold(hostUv, aspect), progress);
            Near(mesh.colors[i].a, expected, "Backing vertices must match the original host field");
            Check(mesh.colors[i].r == 1 && mesh.colors[i].g == 1 && mesh.colors[i].b == 1, "Field color must preserve caller plate tint");
        }
    }
    static void Main()
    {
        var original = new Mesh(); var opaque = new Material(); var fade = new Material();
        Transform plate = Plate(original, opaque);
        Rect host = new(-320, -150, 640, 300), shown = new(-245, 23, 207, 114);
        var helper = new MrBackingMaterialise();
        int initial = Mesh.Created;
        helper.Apply(plate, shown, host, 0, fade);
        Check(Mesh.Created == initial && ReferenceEquals(plate.Filter!.sharedMesh, original), "Steady opaque plate must not allocate a field mesh");
        helper.Apply(plate, shown, host, .45f, fade);
        Mesh mesh = plate.Filter!.sharedMesh!;
        Check(!ReferenceEquals(mesh, original) && mesh.vertices.Length == 1089 && mesh.triangles.Length == 6144, "Animated plate must use one bounded subdivided grid");
        Check(mesh.Dynamic && ReferenceEquals(plate.Renderer!.sharedMaterial, fade), "Animated plate must use supplied fade material and dynamic mesh");
        VerifyField(mesh, shown, host, .45f);
        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            Vector3 p = mesh.vertices[i]; Vector2 uv = mesh.uv[i];
            Near(p.x, uv.x - .5f, "Plate mesh UV must follow fitted quad geometry");
            Near(p.y, uv.y - .5f, "Plate mesh UV must follow fitted quad geometry");
            Check(p.z == 0 && mesh.normals[i].z == -1, "Grid must remain on original quad plane");
        }
        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            var a = mesh.vertices[mesh.triangles[i]]; var b = mesh.vertices[mesh.triangles[i + 1]]; var c = mesh.vertices[mesh.triangles[i + 2]];
            Check((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) < 0, "All grid faces must match the original quad winding");
        }
        Color[] colors = mesh.colors;
        int writes = mesh.ColorWrites;
        helper.Apply(plate, shown, host, .45f, fade);
        Check(mesh.ColorWrites == writes && ReferenceEquals(mesh.colors, colors), "Same field inputs must not upload colors or allocate per eye");
        float[] previous = new float[colors.Length]; Array.Fill(previous, 1f);
        for (int step = 1; step <= 20; step++)
        {
            helper.Apply(plate, shown, host, step / 20f, fade);
            for (int i = 0; i < colors.Length; i++)
            {
                Check(colors[i].a <= previous[i], "Vanish field must be monotonic at each surface point");
                previous[i] = colors[i].a;
            }
        }
        foreach (Color c in mesh.colors) Check(c.a == 0, "Element end must leave no backing through debris tail");
        Check(plate.Renderer!.enabled, "Helper must never own renderer visibility");
        helper.Apply(plate, shown, host, 0, fade);
        Check(ReferenceEquals(plate.Filter.sharedMesh, original) && ReferenceEquals(plate.Renderer.sharedMaterial, opaque), "Progress zero must restore original opaque quad and material");
        Check(!original.Destroyed && !mesh.Destroyed, "Original quad is borrowed and field mesh is reusable");

        // Reopening and changing the fitted extent reuses topology but recomputes host thresholds.
        Rect shifted = new(-400, -180, 210, 180), otherHost = new(-500, -200, 900, 200);
        helper.Apply(plate, shifted, host, .4f, fade); VerifyField(mesh, shifted, host, .4f);
        helper.Apply(plate, shifted, otherHost, .4f, fade); VerifyField(mesh, shifted, otherHost, .4f);
        Check(ReferenceEquals(plate.Filter.sharedMesh, mesh) && Mesh.Created == initial + 1, "Fit changes and reopen must reuse the owned mesh");
        plate.Renderer.enabled = false;
        helper.Apply(plate, shown, host, .8f, fade); helper.Restore(plate);
        Check(!plate.Renderer.enabled, "Restore must not reveal a natively hidden plate");
        plate.Renderer.enabled = true;
        helper.Apply(plate, shown, host, .5f, fade);
        for (int i = 0; i < 20; i++) helper.Apply(plate, shown, host, .4f + .01f * i, fade);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) helper.Apply(plate, shown, host, .4f + .001f * i, fade);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "Animating cached field must not allocate managed objects per frame");

        // Same geometry/progress has identical results for separate peer/eye consumers.
        var peer = Plate(original, opaque); using var other = new MrBackingMaterialise();
        helper.Apply(plate, shown, host, .63f, fade); other.Apply(peer, shown, host, .63f, fade);
        for (int i = 0; i < colors.Length; i++) Near(mesh.colors[i].a, peer.Filter!.sharedMesh!.colors[i].a, "Identical owner geometry must produce identical eye and peer fields");
        other.Restore(peer);
        helper.Apply(peer, shown, host, .63f, fade);
        Check(ReferenceEquals(plate.Filter.sharedMesh, original) && ReferenceEquals(plate.Renderer.sharedMaterial, opaque), "Retargeting must restore the previous plate first");
        Check(ReferenceEquals(peer.Filter!.sharedMesh, mesh), "Retargeting must retain the owned mesh");
        helper.Restore(null);
        Check(ReferenceEquals(peer.Filter.sharedMesh, original), "Null-safe restore must release the bound live plate");
        helper.Apply(peer, shown, host, .2f, fade); helper.Apply(peer, new Rect(0, 0, 0, 2), host, .3f, fade);
        Check(ReferenceEquals(peer.Filter.sharedMesh, original), "Invalid fit must restore safe original presentation");
        helper.Apply(peer, shown, host, .2f, fade); helper.Apply(peer, shown, host, float.NaN, fade);
        Check(ReferenceEquals(peer.Filter.sharedMesh, original), "Invalid progress must not leave corrupted field colors");
        helper.Apply(peer, shown, host, .2f, fade);
        var foreign = new Mesh(); var foreignMaterial = new Material();
        peer.Filter.sharedMesh = foreign; peer.Renderer!.sharedMaterial = foreignMaterial;
        helper.Restore(peer);
        Check(ReferenceEquals(peer.Filter.sharedMesh, foreign) && ReferenceEquals(peer.Renderer.sharedMaterial, foreignMaterial), "Restore must not overwrite another writer's replacement resources");
        peer.Filter.sharedMesh = original; peer.Renderer.sharedMaterial = opaque;
        helper.Apply(peer, shown, host, .2f, fade); helper.Dispose(); helper.Dispose();
        Check(mesh.Destroyed && !original.Destroyed && !opaque.Destroyed && !fade.Destroyed, "Dispose must destroy only the owned mesh once");
        Check(ReferenceEquals(peer.Filter.sharedMesh, original), "Dispose must detach owned mesh before destroying it");
        int disposedCount = Mesh.Created; helper.Apply(peer, shown, host, .3f, fade);
        Check(Mesh.Created == disposedCount && ReferenceEquals(peer.Filter.sharedMesh, original), "Disposed helper must not recreate native resources");
        var lost = new MrBackingMaterialise(); var gone = Plate(original, opaque);
        lost.Apply(gone, shown, host, .4f, fade); Mesh lostMesh = gone.Filter!.sharedMesh!;
        Object.Destroy(gone.Filter); Object.Destroy(gone.Renderer); Object.Destroy(gone);
        lost.Restore(gone); lost.Dispose();
        Check(lostMesh.Destroyed && !original.Destroyed, "Destroyed plate cleanup must remain safe and release owned mesh");
        Console.WriteLine($"MR backing materialise: {assertions} runtime assertions passed.");
    }
}
