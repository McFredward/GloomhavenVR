using System;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Main()
    {
        var cards = new Graphic[4];
        for (int board = 0; board < cards.Length; board++)
        {
            var graphic = cards[board] = new Graphic();
            var group = new CanvasGroup();
            var material = new Material();
            var texture = new Texture();
            var color = new Color(.2f + board, .3f, .4f, .5f);
            var rendered = new Color(.7f, .8f, .9f, .6f);
            var bounds = new Vector4(1 + board, 2, 3, 4);
            var scale = new Vector2(2, 3);
            void Paint(float progress)
            {
                NativePlaybackWrites.Group(group, progress, 7);
                NativePlaybackWrites.Graphic(graphic, color, rendered, 3);
                NativePlaybackWrites.Material(graphic, material);
                NativePlaybackWrites.Float(material, 1, progress);
                NativePlaybackWrites.Vector(material, 2, bounds);
                NativePlaybackWrites.Color(material, 3, color);
                NativePlaybackWrites.TextureScale(material, 4, scale);
                NativePlaybackWrites.Texture(material, 5, texture);
            }
            int Writes() => group.Writes + group.gameObject.Writes + graphic.Writes
                + graphic.gameObject.Writes + graphic.canvasRenderer.Writes + material.Writes;
            Paint(.25f);
            Check(material.GetFloat(1) == .25f && group.alpha == .25f, "First frame is applied");
            int writes = Writes();
            for (int frame = 0; frame < 100; frame++) Paint(.25f);
            Check(Writes() == writes, "Unchanged native output must perform no writes");
            Paint(.25000003f);
            Check(material.GetFloat(1) == .25000003f && group.alpha == .25000003f,
                "Small intermediate frame changes are exact, without an epsilon dead band");
            graphic.color = default;
            graphic.canvasRenderer.SetColor(default);
            graphic.enabled = false;
            graphic.gameObject.SetActive(false);
            group.alpha = 1;
            group.enabled = false;
            group.ignoreParentGroups = false;
            group.gameObject.SetActive(false);
            graphic.material = new Material();
            material.SetFloat(1, 9);
            material.SetColor(3, default);
            material.SetTextureScale(4, default);
            material.SetTexture(5, null);
            Paint(.25f);
            Check(graphic.color == color && graphic.canvasRenderer.GetColor() == rendered && graphic.enabled
                && graphic.gameObject.activeSelf && group.enabled && group.ignoreParentGroups && group.gameObject.activeSelf,
                "Repeated snapshots repair an intervening UI writer");
            Check(ReferenceEquals(graphic.material, material) && material.GetFloat(1) == .25f
                && material.GetColor(3) == color && material.GetTextureScale(4) == scale
                && ReferenceEquals(material.GetTexture(5), texture), "Repeated snapshots repair material and texture writers");
            bounds = new Vector4(9, 8, 7, 6);
            Paint(.25f);
            Check(material.GetVector(2) == bounds, "Moving world bounds update with a settled burn frame");
            material.SetVector(2, default); Paint(.25f);
            Check(material.GetVector(2) == bounds, "World bounds repair an external shader writer");
            color = new Color(color.r + .0000001f, color.g, color.b, color.a); Paint(.25f);
            Check(graphic.color == color && material.GetColor(3) == color, "Small color changes are never approximated");
            NativePlaybackWrites.Group(group, 0, 0);
            Check(!group.enabled && !group.gameObject.activeSelf && !group.ignoreParentGroups && group.alpha == 0,
                "Reset restores every original group flag and alpha");
        }
        Check(!ReferenceEquals(cards[0].material, cards[1].material)
            && !ReferenceEquals(cards[2].material, cards[3].material), "Four boards retain independent output materials");
        NodeRange();
        PropertySupport();
        MaterialOwnership();
        Console.WriteLine($"Native playback production helper harness: {_checks} assertions passed.");
    }
    private static void NodeRange()
    {
        foreach (int primary in new[] { 0, 12, 20 })
        foreach (int extra in new[] { 0, 1, 65 })
        {
            var first = new int[primary]; var second = new int[extra];
            for (int i = 0; i < first.Length; i++) first[i] = i;
            for (int i = 0; i < second.Length; i++) second[i] = primary + i;
            int count = 0;
            foreach (int item in new NativePlaybackRange<int>(first, second))
                Check(item == count++, "Supplemental native nodes retain exact full order");
            Check(count == primary + extra, "No supplemental groups are omitted");
        }
        int seen = 0;
        foreach (int item in new NativePlaybackRange<int>(new[] { 9 }, null)) seen += item;
        Check(seen == 9, "Legacy-only frame accepts absent supplemental array");
        var nodes = new[] { 1, 2, 3 }; var groups = new[] { 4, 5 };
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int iteration = 0; iteration < 1000; iteration++)
            foreach (int node in new NativePlaybackRange<int>(nodes, groups)) sum += node;
        long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(sum == 15000 && allocation == 0, "Native range performs no per-frame iterator allocation");
    }
    private static void PropertySupport()
    {
        var cache = new NativePlaybackProperties();
        var first = new Material();
        first.shader.Properties.UnionWith(new[] { CardAppearanceBindings.FloatIds[0],
            CardAppearanceBindings.BurnTint, Shader.PropertyToID("_PosAndBounds") });
        uint initial = cache.Support(first);
        Check(initial == (1u | (1u << 15) | NativePlaybackProperties.BoundsMask), "Original shader support mask");
        int probes = first.Probes;
        for (int i = 0; i < 100; i++) Check(cache.Support(first) == initial, "Stable shader retains support");
        Check(first.Probes == probes, "Settled shader makes no repeated HasProperty calls");
        var sameShader = new Material { shader = first.shader };
        Check(cache.Support(sameShader) == initial && sameShader.Probes == 0, "Material swap with same shader shares invariant support");
        first.shader = new Shader();
        first.shader.Properties.UnionWith(new[] { CardAppearanceBindings.Particle, CardAppearanceBindings.Noise,
            CardAppearanceBindings.FlameTint, CardAppearanceBindings.FloatIds[1] });
        Check(cache.Support(first) == (2u | (1u << 16) | (1u << 17) | NativePlaybackProperties.ParticleMask),
            "Shader replacement on the same material invalidates support immediately");
        Check(cache.Support(sameShader) == initial, "Returning to original variant restores exact support");
    }
    private static void MaterialOwnership()
    {
        var source = new Graphic();
        var targets = new Graphic[4];
        var owners = new NativePlaybackMaterialOwner[4];
        var materials = new Material[4];
        for (int board = 0; board < 4; board++)
        {
            targets[board] = new Graphic(); materials[board] = new Material();
            owners[board].Copy(source, targets[board]);
            Check(ReferenceEquals(source.material, targets[board].material), "Unclaimed graphic mirrors original material");
            owners[board].Own(materials[board]);
            NativePlaybackWrites.Material(targets[board], materials[board]);
            int before = targets[board].Writes;
            for (int frame = 0; frame < 100; frame++)
            {
                owners[board].Copy(source, targets[board]);
                NativePlaybackWrites.Material(targets[board], materials[board]);
            }
            Check(targets[board].Writes == before, "Claimed material avoids mirror/owner ping-pong");
        }
        var newSource = new Material(); source.material = newSource;
        var replacement = new Material();
        owners[0].Own(replacement); NativePlaybackWrites.Material(targets[0], replacement);
        owners[0].Release(source, targets[0], materials[0]);
        owners[0].Copy(source, targets[0]);
        Check(ReferenceEquals(targets[0].material, replacement), "Old teardown cannot revoke replacement ownership");
        owners[0].Release(source, targets[0], replacement);
        Check(ReferenceEquals(targets[0].material, newSource), "Non-effect frame immediately restores latest source material");
        for (int board = 1; board < 4; board++)
        {
            owners[board].Copy(source, targets[board]);
            Check(ReferenceEquals(targets[board].material, materials[board]), "Releasing one board leaves other boards unchanged");
            targets[board].material = new Material();
            NativePlaybackWrites.Material(targets[board], materials[board]);
            Check(ReferenceEquals(targets[board].material, materials[board]), "Owner frame restores an external material assignment");
            owners[board].Release(source, targets[board], materials[board]);
            Check(ReferenceEquals(targets[board].material, newSource), "Teardown restores current original material");
        }
    }
}
