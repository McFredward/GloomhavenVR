using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DiagnosticProgram
{
    private static HarmonyLib.Harmony? lifetime;
    public static void Cleanup() { lifetime?.UnpatchSelf(); lifetime = null; }
    public static int Checks;
    public static string Metrics = "";
    private static readonly List<GameObject> Roots = new();
    private static readonly List<Object> Assets = new();
    private static void Check(bool value, string message)
    {
        Checks++; if (!value) throw new Exception(message);
    }
    private static GameObject Root(string name)
    {
        var go = new GameObject(name, typeof(RectTransform)); Roots.Add(go); return go;
    }
    private static FullAbilityCard Face(int i)
    {
        var root = Root("card-" + i);
        if (i < 64)
        {
            root.AddComponent<AbilityCardUI>();
            if (i >= 32) root.AddComponent<ObjectPool>();
        }
        FullAbilityCard face = root.AddComponent<FullAbilityCard>(); face.Adopted = i >= 64 && i < 96;
        var half = new GameObject("top", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
        half.transform.SetParent(root.transform, false);
        var action = half.AddComponent<FullAbilityCardAction>();
        action.canvasGroup = half.GetComponent<CanvasGroup>(); action.actionButton = half.GetComponent<Button>();
        action.actionButton.targetGraphic = half.GetComponent<Image>(); face.topActionButton = action;
        if (i == 31) root.SetActive(false); // inactive native previews remain in the comparison
        return face;
    }
    private static int CensusLines() => VRLog.Lines.Count(line => line.StartsWith("CARD HALF TONE CENSUS"));
    private static IEnumerator CompleteCensus()
    {
        int start = CardHalfTone.Completed;
        int frames = 0;
        while (CardHalfTone.Completed == start)
        {
            yield return null;
            int before = CardHalfTone.Sampled;
            CardHalfTone.Tick();
            int delta = CardHalfTone.Sampled - before;
            Check(delta <= 8, "census samples at most eight faces per actual frame");
            int samples = CardArtGuard.Samples;
            for (int i = 0; i < 100; i++) CardHalfTone.Tick();
            Check(CardArtGuard.Samples == samples, "many card offers cannot multiply one frame census work");
            Check(++frames < 350, "bounded census completes despite many card offers");
        }
        Check(CardDiagnosticProbe.HeapQueries == 1, "interactive diagnostics never query the native resource heap again");
    }
    private static RectTransform FaceRoot(string name)
    {
        RectTransform root = (RectTransform)Root(name).transform;
        root.sizeDelta = new Vector2(100, 150); return root;
    }
    private static T Graphic<T>(RectTransform parent, string name, Color color, Vector2? size = null, Vector2? position = null) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.sizeDelta = size ?? parent.sizeDelta; rect.anchoredPosition = position ?? Vector2.zero;
        T graphic = go.AddComponent<T>(); graphic.color = color; return graphic;
    }
    private static Sprite Artwork()
    {
        var texture = new Texture2D(4, 4); Assets.Add(texture);
        texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray()); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)); Assets.Add(sprite);
        sprite.name = "authoritative-art"; return sprite;
    }
    private static byte[] Footprint()
    {
        var mask = new byte[24 * 36];
        for (int y = 2; y < 34; y++) for (int x = 2; x < 22; x++) mask[y * 24 + x] = 255;
        return mask;
    }
    private static void Blackout()
    {
        byte[] mask = Footprint(); Sprite art = Artwork();
        RectTransform root = FaceRoot("zero-result inventory");
        for (int i = 0; i < 32; i++)
        {
            var image = Graphic<Image>(root, "art-" + i, Color.black); image.sprite = art;
            Graphic<Image>(root, "bright-" + i, Color.white);
        }
        CardFace.Maintain(root, mask);
        int inventory = VRLog.Lines.Count(line => line.Contains("FULL FACE INVENTORY"));
        Check(inventory == 1, "first Debug inventory remains useful");
        // Arrival forces actual maintenance; the old latch rebuilt a large unprintable inventory.
        // A real uGUI child array/corner buffer remains necessary, so this is a bounded allocation
        // claim, not a zero-allocation claim. The negative control reinstates the exact old latch.
        CardFace.Maintain(root, mask);
        long calibrate = GC.GetAllocatedBytesForCurrentThread();
        var allocation = new byte[16384];
        bool allocationCounter = GC.GetAllocatedBytesForCurrentThread() - calibrate >= allocation.Length;
        GC.KeepAlive(allocation);
        CardDiagnosticProbe.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 16; i++) CardFace.Maintain(root, mask);
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 16;
        Check(CardDiagnosticProbe.InventoryEntries == 0, "zero-result maintenance never rebuilds a hidden full inventory");
        Check(CardDiagnosticProbe.GeometryWalks == 0 && CardDiagnosticProbe.FootprintProbes == 0,
            "bright and art-bearing graphics skip unnecessary geometry and footprint probes");
        if (allocationCounter) Check(bytes < 2000, "zero-result maintenance does not allocate a hidden full inventory (" + bytes + " bytes/pass)");
        Check(VRLog.Lines.Count(line => line.Contains("FULL FACE INVENTORY")) == inventory, "zero-result Debug inventory is one shot");

        // The first maintenance happened earlier THIS frame. Native artwork arriving now must
        // still apply its silhouette correction before its first rendered frame.
        var backdrop = Graphic<Image>(root, "new dark backdrop", Color.black);
        CardFace.Maintain(root, mask);
        Check(backdrop.color.a == 0, "arrival corrects a newly loaded quad in the same frame");
        Check(VRLog.Lines.Count(line => line.Contains("FIRST MUTED QUADS")) == 1, "first positive pass after zero retains a bounded diagnostic");
        var strip = Graphic<Image>(root, "thin edge strip", Color.black, new Vector2(2, 150), new Vector2(-49, 0));
        CardFace.Maintain(root, mask);
        Check(strip.color.a == 0, "thin edge strips retain tier B correction");
        var bright = Graphic<Image>(root, "legitimate bright graphic", Color.white);
        var dimmer = Graphic<Image>(root, "native translucent dimmer", new Color(0, 0, 0, 0.5f));
        var artwork = Graphic<Image>(root, "native artwork", Color.black); artwork.sprite = art;
        var textured = Graphic<RawImage>(root, "native textured artwork", Color.black); textured.texture = art.texture;
        var rawBackdrop = Graphic<RawImage>(root, "raw dark backdrop", Color.black);
        CardFace.Maintain(root, mask);
        Check(bright.color == Color.white && dimmer.color.a == 0.5f, "native bright graphics and dimmers preserve their state");
        Check(artwork.color == Color.black && textured.color == Color.black, "authoritative sprite and texture artwork is never muted");
        Check(rawBackdrop.color.a == 0, "RawImage remains covered by the same correction");
        backdrop.sprite = art; CardFace.Maintain(root, mask);
        Check(backdrop.color == Color.black, "newly arriving sprite art is immediately restored");
        rawBackdrop.texture = art.texture; CardFace.Maintain(root, mask);
        Check(rawBackdrop.color == Color.black, "newly arriving texture art is immediately restored");
        strip.color = Color.white; CardFace.Maintain(root, mask);
        Check(strip.color == Color.white, "native recolour takes ownership without a stale restore");
        var remuted = Graphic<Image>(root, "native dark recolour", Color.black);
        CardFace.Maintain(root, mask); remuted.color = new Color(0.1f, 0.1f, 0.1f, 1);
        CardFace.Maintain(root, mask);
        Check(remuted.color.a == 0, "a native dark recolour retains the visible border correction");
        var foreign = Graphic<Image>(root, "native later write", Color.black);
        CardFace.Maintain(root, mask); foreign.color = Color.red;
        CardFace.Restore(root);
        Check(remuted.color == Color.black, "detach restores original colour for still-owned corrections");
        Check(foreign.color == Color.red, "detach never overwrites a later native colour write");
        VRLog.WantsDebug = false;
        RectTransform normal = FaceRoot("normal logging presentation");
        var normalBackdrop = Graphic<Image>(normal, "normal dark backdrop", Color.black);
        int logs = VRLog.Lines.Count;
        CardFace.Maintain(normal, mask);
        Check(normalBackdrop.color.a == 0 && VRLog.Lines.Count == logs, "normal logging retains immediate visual correction without diagnostics");
        CardFace.Restore(normal);
        Check(normalBackdrop.color == Color.black, "normal logging detach restoration remains exact");
        Metrics += "blackout steady inventory/geometry/probes=0/0/0; allocation=" + (allocationCounter ? bytes + " bytes/pass" : "unavailable (Mono calibration returned zero)") + "; ";
    }
    public static IEnumerator Run()
    {
        Checks = 0; CardHalfTone.Clear(); VRLog.Lines.Clear(); VRLog.WantsDebug = false;
        var faces = new List<FullAbilityCard>();
        faces.Add(Face(0));
        CardHalfTone.SeedCensusRegistry();
        Check(CardHalfTone.Population == 1, "startup discovery seeds pre-existing native scene cards");
        lifetime = new HarmonyLib.Harmony("ghvr.card-census." + typeof(DiagnosticProgram).Assembly.GetName().Name);
        lifetime.PatchAll(typeof(GloomhavenVR.Cards.Patches.FullAbilityCard_OnEnable_CensusLifetime));
        lifetime.PatchAll(typeof(GloomhavenVR.Cards.Patches.FullAbilityCard_Init_CensusLifetime));
        lifetime.PatchAll(typeof(GloomhavenVR.Cards.Patches.FullAbilityCard_OnDestroy_CensusLifetime));
        for (int i = 1; i < 227; i++) faces.Add(Face(i));
        Check(CardHalfTone.Population == 227, "original Unity OnEnable registers newly created active cards");
        var parked = Root("inactive future pool"); parked.SetActive(false);
        var initialized = parked.AddComponent<FullAbilityCard>(); initialized.Init();
        Check(CardHalfTone.Population == 228, "original Init registers a never-enabled inactive future pool card");
        parked.SetActive(true);
        Check(CardHalfTone.Population == 228, "native enable after Init never duplicates a pooled registry entry");
        Object.DestroyImmediate(parked);
        Check(CardHalfTone.Population == 227, "original OnDestroy releases inactive card registry references");
        for (int i = 0; i < 100; i++) CardHalfTone.Tick();
        Check(PerfMonitor.Calls.Count == 0 && CardArtGuard.Samples == 0 && VRLog.Lines.Count == 0,
            "normal logging never discovers or samples a census");
        VRLog.WantsDebug = true;
        CardHalfTone.Tick();
        Check(CardHalfTone.Busy && CardArtGuard.Samples == 0, "discovery is measured separately from per-face sampling");
        int samples = CardArtGuard.Samples;
        for (int i = 0; i < 100; i++) CardHalfTone.Tick();
        Check(CardArtGuard.Samples == samples, "many card offers cannot multiply one frame census work");
        IEnumerator scan = CompleteCensus(); while (scan.MoveNext()) yield return scan.Current;
        Check(CardArtGuard.Samples == 227 && CensusLines() == 1, "all 227 native and mod faces remain diagnosed");
        string first = VRLog.Lines.Last(line => line.StartsWith("CARD HALF TONE CENSUS"));
        Check(first.Contains("GAME-LIVE(the overlay's population) 32 face(s)") && first.Contains("GAME-POOL(parked, = what a clone is copied from) 32 face(s)")
              && first.Contains("ADOPTED(scenario fan/tray) 32 face(s)") && first.Contains("CLONE(mod-built fronts) 131 face(s)"),
            "inactive native, pool, adopted and cloned populations remain separately measured");
        Check(first.Contains("not simultaneous state"), "multi-frame sampling explicitly states its evidence limitation");
        yield return null; CardHalfTone.Due(); CardHalfTone.Tick();
        scan = CompleteCensus(); while (scan.MoveNext()) yield return scan.Current;
        Check(CensusLines() == 1, "unchanged census does not log again merely because its counter changed");
        faces[0].topActionButton!.canvasGroup!.alpha = 0.5f;
        yield return null; CardHalfTone.Due(); CardHalfTone.Tick();
        scan = CompleteCensus(); while (scan.MoveNext()) yield return scan.Current;
        Check(CensusLines() == 2, "changed native half state remains visible in Debug evidence");
        yield return null; CardHalfTone.HeartbeatDue(); CardHalfTone.Due(); CardHalfTone.Tick();
        scan = CompleteCensus(); while (scan.MoveNext()) yield return scan.Current;
        Check(CensusLines() == 3, "bounded heartbeat still dates unchanged measurements");

        yield return null; CardHalfTone.Due(); CardHalfTone.Tick();
        Object.DestroyImmediate(faces[226].gameObject);
        scan = CompleteCensus(); while (scan.MoveNext()) yield return scan.Current;
        string afterDestroy = VRLog.Lines.Last(line => line.StartsWith("CARD HALF TONE CENSUS"));
        Check(afterDestroy.Contains("compared 226 ability face(s)") && afterDestroy.Contains("1 skipped"), "destroyed faces do not break an in-flight diagnostic snapshot");
        yield return null; CardHalfTone.Due(); CardHalfTone.Tick();
        Check(CardHalfTone.Busy, "partial diagnostic starts");
        VRLog.WantsDebug = false; CardHalfTone.Tick();
        Check(!CardHalfTone.Busy, "switching off Debug releases a partial census without sampling");
        VRLog.WantsDebug = true;
        yield return null; CardHalfTone.Tick();
        Check(CardHalfTone.Busy && CardHalfTone.Sampled == 0, "switching Debug back on begins a fresh snapshot");
        yield return null; CardHalfTone.Tick();
        Check(CardHalfTone.DiagnosticRefs, "in-flight diagnostic retains only its current scene references");
        CardHalfTone.Reset();
        Check(!CardHalfTone.DiagnosticRefs && CardHalfTone.Sampled == 0, "production scene Reset releases every diagnostic scene reference");
        Check(CardHalfTone.Population == 226, "scene diagnostic Reset retains still-live pooled cards without a new heap discovery");
        Blackout();
        foreach (var pair in PerfMonitor.WorstMs) Metrics += pair.Key + " worst=" + pair.Value.ToString("F3") + "ms; ";
        foreach (GameObject go in Roots) if (go != null) Object.DestroyImmediate(go);
        foreach (Object asset in Assets) if (asset != null) Object.DestroyImmediate(asset);
        Roots.Clear(); Assets.Clear(); Cleanup();
    }
}
