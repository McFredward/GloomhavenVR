using System;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;

internal static partial class Program
{
    private static void EqualNodes(CardAppearanceNode[] expected, CardAppearanceNode[] actual, string context)
    {
        Check(expected.Length == actual.Length, context + " node count");
        for (int i = 0; i < expected.Length; i++)
        {
            var a = expected[i]; var b = actual[i];
            Check(a.Role == b.Role && a.Flags == b.Flags && a.Binding == b.Binding && a.Mask == b.Mask,
                context + " role/flags/binding/mask " + i);
            for (int j = 0; j < a.Values.Length; j++) Check(a.Values[j] == b.Values[j], context + " value " + i + ":" + j);
        }
    }
    private static CardEffects Fixture(int groups)
    {
        var root = new GameObject("Full");
        var effects = root.Add<CardEffects>();
        effects.overlayFrameGhost = new Texture(); effects.overlayFrameBurn = new Texture();
        effects._lowMaterial = new Material();
        for (int i = 0; i < groups; i++) AddGroup(root.transform, i);
        int role = 0;
        foreach (FieldInfo field in typeof(CardEffects).GetFields().Where(f => f.FieldType == typeof(Graphic)))
        {
            var child = new GameObject("Graphic " + role); root.transform.Add(child.transform);
            Graphic graphic = role >= 7 && role <= 10 ? child.Add<TMPro.TextMeshProUGUI>() : child.Add<Graphic>();
            graphic.color = new Color(.1f * role); graphic.canvasRenderer.Color = new Color(.05f * role);
            Material material = graphic.material;
            foreach (int id in CardAppearanceBindings.FloatIds)
            { material.shader.Properties.Add(id); material.Floats[id] = .125f * role; }
            foreach (int id in new[] { CardAppearanceBindings.BurnTint, CardAppearanceBindings.FlameTint })
            { material.shader.Properties.Add(id); material.Colors[id] = new Color(.25f * role); }
            material.shader.Properties.Add(CardAppearanceBindings.Noise);
            material.Scales[CardAppearanceBindings.Noise] = new Vector2 { x = .2f, y = .7f };
            material.shader.Properties.Add(CardAppearanceBindings.Particle);
            material.Textures[CardAppearanceBindings.Particle] = effects.overlayFrameGhost;
            field.SetValue(effects, graphic); role++;
        }
        return effects;
    }
    private static void CaptureVectors()
    {
        CardEffects effects = Fixture(16);
        var actual = new CardAppearanceBindings(effects);
        var reference = new ReferenceCardAppearanceBindings(effects);
        void Compare(string context, bool detached = false)
        {
            EqualNodes(reference.Capture(detached), actual.Capture(detached), context);
            EqualNodes(reference.CaptureExtraGroups(detached), actual.CaptureExtraGroups(detached), context + " extra");
        }
        Compare("initial full shader output");
        var retained = actual.Capture(); var retainedExtra = actual.CaptureExtraGroups();
        var frozen = retained.Select(n => n.Copy()).ToArray();
        var frozenExtra = retainedExtra.Select(n => n.Copy()).ToArray();
        Check(ReferenceEquals(retained, actual.Capture()), "Unchanged capture reuses immutable array");
        Check(ReferenceEquals(retainedExtra, actual.CaptureExtraGroups()), "Unchanged supplemental groups reuse immutable array");
        effects._headerImage!.color = new Color(.93f);
        Compare("graphic color change");
        Check(!ReferenceEquals(retained[0], actual.Capture()[0]), "Changed graphic publishes a new node");
        Check(ReferenceEquals(retained[1], actual.Capture()[1]), "Unchanged graphic retains its immutable node during neighboring animation");
        effects._headerImage.canvasRenderer.Color = new Color(.72f);
        effects._headerImage.enabled = false;
        ((TMPro.TextMeshProUGUI)effects._header!).enableVertexGradient = true;
        effects._uiFxOverlay!.material.Textures[CardAppearanceBindings.Particle] = effects.overlayFrameBurn;
        effects._topButton!.material.Floats[CardAppearanceBindings.FloatIds[0]] = .64f;
        Compare("renderer flags gradient texture and shader animation");
        foreach (Graphic? graphic in actual.Graphics)
        {
            if (graphic == null) continue;
            foreach (int id in CardAppearanceBindings.FloatIds) graphic.material.Floats[id] += .37f;
            graphic.material.Colors[CardAppearanceBindings.BurnTint] = new Color(.29f);
            graphic.material.Colors[CardAppearanceBindings.FlameTint] = new Color(.31f);
            graphic.material.Scales[CardAppearanceBindings.Noise] = new Vector2 { x = 3f, y = 4f };
        }
        var animatedGroup = actual.Groups.Values.First();
        animatedGroup.alpha = .81f; animatedGroup.enabled = !animatedGroup.enabled;
        animatedGroup.ignoreParentGroups = !animatedGroup.ignoreParentGroups;
        animatedGroup.gameObject.activeSelf = !animatedGroup.gameObject.activeSelf;
        Compare("all shader channels and group flags animate at unchanged cadence");
        effects._topButton.material.shader = new Shader();
        Compare("same material changed shader clears absent property lanes");
        effects._bottomAction!.material = new Material { shader = effects._lowMaterial!.shader };
        Compare("material replacement and low material flag");
        effects._uiFxOverlay.material.shader = new Shader();
        Compare("flame texture property removed by shader swap");
        var rootGroup = effects.gameObject.Add<CanvasGroup>(); rootGroup.alpha = .25f;
        effects.gameObject.activeSelf = false;
        Compare("parked root"); Compare("detached root", true);
        effects.gameObject.activeSelf = true;
        var holder = new GameObject("Delayed artwork holder"); effects.transform.Add(holder.transform);
        effects.transform.Children.Remove(effects._uiFxOverlay.transform); holder.transform.Add(effects._uiFxOverlay.transform);
        holder.activeSelf = false;
        Compare("hidden intermediate holder");
        holder.activeSelf = true;
        Compare("first visible artwork");
        AddGroup(holder.transform, 80);
        Compare("late group insertion");
        holder.transform.name = "Renamed native holder";
        Compare("path rename");
        effects.transform.Children.Reverse();
        Compare("sibling reorder changes group identities");
        effects.transform.Children.Remove(holder.transform);
        Compare("detached removed subtree");
        EqualNodes(frozen, retained, "retained original snapshot is immutable");
        EqualNodes(frozenExtra, retainedExtra, "retained supplemental snapshot is immutable");
        // Cache lifetime ends with the bindings. A replacement hierarchy must sample its own
        // initial values even when it occupies the same visual seat after rest/reconnection.
        var replacement = new CardAppearanceBindings(Fixture(16));
        EqualNodes(frozen, replacement.Capture(), "fresh bindings reset capture history");
        var duplicate = effects.gameObject.Add<CanvasGroup>();
        bool rejected = false;
        try { actual.Capture(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Duplicate group binding remains rejected");
        effects.gameObject.Components.Remove(duplicate);
        Compare("recovery after invalid hierarchy");
        CaptureStateVectors(); AllocationVectors();
    }
    private static void CaptureStateVectors()
    {
        CardEffects effects = Fixture(16);
        var capture = new CardAppearanceCapture(effects);
        var candidate = capture.Candidate;
        candidate.ActorId = candidate.SourceActorId = 4;
        candidate.FaceCode = 32; candidate.ListCount = 10; candidate.PoolCount = 10;
        candidate.Nodes = capture.Bindings.Capture(); candidate.ExtraGroups = capture.Bindings.CaptureExtraGroups();
        var first = capture.Publish(); var firstCopy = first.Copy();
        Check(!ReferenceEquals(first, candidate), "Published state is never the mutable sampling candidate");
        Check(ReferenceEquals(first, capture.Publish()), "Unchanged state reuses validated immutable state");
        candidate.FaceCode = 33; candidate.PoolSeat = 1;
        var second = capture.Publish();
        Check(!ReferenceEquals(first, second), "Seat/provenance changes publish a state");
        Check(first.FaceCode == 32 && first.PoolSeat == 0, "Old seat/provenance remain immutable");
        candidate.SourceActorId = 5;
        Check(capture.Publish().SourceActorId == 5, "Donor provenance changes publish even when artwork matches");
        effects._headerImage!.color = new Color(.61f);
        candidate.Nodes = capture.Bindings.Capture(); candidate.ExtraGroups = capture.Bindings.CaptureExtraGroups();
        capture.Publish();
        Check(CardAppearanceState.Same(first, firstCopy), "Retained complete state survives later owner samples");
        candidate.ListCount = 0;
        bool rejected = false;
        try { capture.Publish(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Changed invalid address cannot reuse previously valid state");
        candidate.ListCount = 10;
        Check(capture.Publish().ListCount == 10, "Valid state recovers after invalid candidate");
        var excessive = new CardAppearanceCapture(Fixture(65));
        excessive.Candidate.ActorId = 4; excessive.Candidate.FaceCode = 32; excessive.Candidate.ListCount = 1;
        excessive.Candidate.Nodes = excessive.Bindings.Capture(); excessive.Candidate.ExtraGroups = excessive.Bindings.CaptureExtraGroups();
        rejected = false;
        try { excessive.Publish(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "All groups captured; oversized publication still rejected without truncation");
    }
    private static void AllocationVectors()
    {
        CardEffects effects = Fixture(16);
        var optimized = new CardAppearanceCapture(effects);
        optimized.Candidate.ActorId = 4; optimized.Candidate.FaceCode = 32; optimized.Candidate.ListCount = 1;
        var reference = new ReferenceCardAppearanceBindings(effects);
        void Sample()
        {
            optimized.Candidate.Nodes = optimized.Bindings.Capture();
            optimized.Candidate.ExtraGroups = optimized.Bindings.CaptureExtraGroups(); optimized.Publish();
        }
        for (int i = 0; i < 200; i++) Sample();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Sample();
        long optimizedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        for (int i = 0; i < 200; i++) { reference.Capture(); reference.CaptureExtraGroups(); }
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { reference.Capture(); reference.CaptureExtraGroups(); }
        long referenceBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(optimizedBytes == 0, "Warmed unchanged capture/publication allocates no managed objects in the stub harness");
        Check(referenceBytes > optimizedBytes, "Allocation instrument detects the original capture allocations");
        Console.WriteLine($"Warmed unchanged 12-graphic/16-group captures x1000: production {optimizedBytes} B; MB492 oracle {referenceBytes} B (stub runtime, not Unity/headset).");
    }
}
