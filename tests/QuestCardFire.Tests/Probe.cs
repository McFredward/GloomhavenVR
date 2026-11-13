using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DiagnosticProgram
{
    public static int Checks;
    public static string Metrics = "passive native Image/CanvasRenderer bindings; no flame pixel qualification repeated";
    private static readonly List<Object> Owned = new();
    private static int observations;
    private static T Keep<T>(T item) where T : Object { Owned.Add(item); return item; }
    private static void Check(bool value, string why) { Checks++; if (!value) throw new Exception(why); }
    private static string Snapshot() => string.Join("\n", QuestCardFireEvidence.Snapshot);
    private static void Observe(CardEffects fx, string transition)
    {
        string before = Snapshot(); QuestCardFireEvidence.Observe(fx, transition);
        if (before != Snapshot()) observations++;
    }
    private static CardEffects Create(Canvas canvas, Material? material, Material? drawn, Texture? expected, bool burn)
    {
        var obj = Keep(new GameObject("actual-native-card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CardEffects)));
        obj.transform.SetParent(canvas.transform, false);
        var image = obj.GetComponent<Image>(); if (material != null) image.material = material;
        image.color = new Color(.2f, .3f, .4f, .5f);
        image.canvasRenderer.SetColor(new Color(.5f, .4f, .3f, .8f));
        image.canvasRenderer.materialCount = drawn != null ? 1 : 0;
        if (drawn != null) image.canvasRenderer.SetMaterial(drawn, 0);
        var fx = obj.GetComponent<CardEffects>(); fx._uiFxOverlay = image; fx.overlayFrameBurn = expected;
        if (burn) fx.Effects.Add(CardEffects.FXTask.BurnCard);
        return fx;
    }
    public static void Cleanup() { foreach (Object item in Owned) if (item != null) Object.DestroyImmediate(item); Owned.Clear(); }
    public static IEnumerator Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var canvas = Keep(new GameObject("actual-native-fire-canvas", typeof(RectTransform), typeof(Canvas))).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var shader = Resources.Load<Shader>("Binding");
        Check(shader != null && shader.isSupported, "actual Unity material binding shader is supported");
        var expected = Keep(new Texture2D(4, 4)); expected.name = "owned-native-fire";
        var wrong = Keep(new Texture2D(4, 4)); wrong.name = "stale-fire-copy";
        var source = Keep(new Material(shader)); source.SetTexture("_ParticleTexture", expected); source.SetFloat("_FXAnim", .375f);
        source.SetFloat("_Glow", 1.5f); source.SetColor("_TintColor", new Color(.7f,.6f,.5f,.4f)); source.renderQueue = 4100;
        var drawn = Keep(new Material(source)); drawn.SetTexture("_ParticleTexture", wrong); drawn.SetFloat("_FXAnim", .125f);
        drawn.SetColor("_TintColor", new Color(.2f,.3f,.4f,.5f)); drawn.renderQueue = 4120;
        var fx = Create(canvas, source, drawn, expected, true);
        var image = fx._uiFxOverlay!; var renderer = image.canvasRenderer; renderer.cull = true;
        foreach (int gate in new[] { 0, 1 })
        {
            QuestStandalonePlatform.Enabled = gate != 0; VRLog.DebugEnabled = gate == 0;
            int reads = fx.NativeReads; Observe(fx, "disabled");
            Check(fx.NativeReads == reads && QuestCardFireEvidence.Snapshot.Length == 0, "Quest/Debug gates precede native reads and formatting");
        }
        QuestStandalonePlatform.Enabled = VRLog.DebugEnabled = true;
        var idleMaterial = Keep(new Material(shader)); idleMaterial.SetFloat("_FXAnim", 0);
        idleMaterial.SetColor("_TintColor", new Color(1,.3f,0,1));
        var idle = Create(canvas, idleMaterial, idleMaterial, expected, false);
        var pending = Create(canvas, null, null, null, false); // Actual default UI shader has no _FXAnim.
        for (int i = 0; i < 512; i++) { Observe(idle, "sample"); Observe(pending, "sample"); }
        Check(QuestCardFireEvidence.Snapshot.Length == 0 && observations == 0, "idle cards and missing fire metadata do not exhaust the observation lane");
        var ghostMaterial = Keep(new Material(shader)); ghostMaterial.SetFloat("_FXAnim", .75f);
        ghostMaterial.SetColor("_TintColor", new Color(.8f,.8f,1,.7f)); ghostMaterial.SetTexture("_ParticleTexture",wrong);
        var ghost = Create(canvas,ghostMaterial,ghostMaterial,expected,false);
        // Event observations deliberately bypass the per-card sample clock: a
        // flood of ordinary native discard transitions must leave the later burn
        // lane intact, even when this same UI Image animates its blue ghost.
        for (int i = 0; i < 512; i++) Observe(ghost,"ordinary-discard-"+i);
        Check(QuestCardFireEvidence.Snapshot.Length == 0 && observations == 0,
            "ordinary discard overlays do not consume the first-burn evidence budget");
        ghostMaterial.SetColor("_TintColor", new Color(1,.3f,0,0));
        for (int i = 0; i < 256; i++) Observe(ghost,"transparent-warm-overlay-"+i);
        Check(QuestCardFireEvidence.Snapshot.Length == 0 && observations == 0,
            "transparent warm metadata cannot consume the native burn evidence lane");
        Material priorBase = image.material; Material priorDraw = renderer.GetMaterial();
        Color priorGraphic = image.color, priorRendered = renderer.GetColor();
        int materialCount = Resources.FindObjectsOfTypeAll<Material>().Length;
        Observe(fx, "native-first-step");
        string row = Snapshot();
        Check(row.Contains("base[id="+source.GetInstanceID()) && row.Contains("draw[id="+drawn.GetInstanceID())
            && row.Contains("anim=0.375") && row.Contains("anim=0.125")
            && row.Contains("particle=owned-native-fire expected=True") && row.Contains("particle=stale-fire-copy expected=False"),
            "actual base and stale CanvasRenderer material/texture/animation bindings remain distinct");
        Check(row.Contains("queue=4100") && row.Contains("queue=4120") && row.Contains("glow=1.50")
            && row.Contains("cull=True") && row.Contains("canvas=WorldSpace")
            && row.Contains("graphic="+priorGraphic) && row.Contains("rendered="+priorRendered),
            "actual native tint, queue, graphic/rendered color and canvas/cull state are retained");
        Check(image.material == priorBase && renderer.GetMaterial() == priorDraw && image.color == priorGraphic
            && renderer.GetColor() == priorRendered && renderer.cull && renderer.materialCount == 1
            && image.gameObject.activeSelf && image.enabled && source.GetFloat("_FXAnim") == .375f && drawn.GetFloat("_FXAnim") == .125f
            && source.GetTexture("_ParticleTexture") == expected && drawn.GetTexture("_ParticleTexture") == wrong
            && Resources.FindObjectsOfTypeAll<Material>().Length == materialCount,
            "passive observation neither writes nor materializes native material/color/texture/active state");
        int readsBefore = fx.NativeReads; string throttled = Snapshot();
        for (int i = 0; i < 1024; i++) Observe(fx, "sample");
        Check(fx.NativeReads == readsBefore && Snapshot() == throttled, "same-frame samples are throttled before expensive native work");
        renderer.cull = false; renderer.SetMaterial(source,0); Observe(fx, "native-binding-change");
        Check(Snapshot().Contains("cull=False") && !Snapshot().Contains("particle=stale-fire-copy"), "actual renderer binding/cull changes are observed without a stale material cache");
        source.SetColor("_TintColor",new Color(.8f,.8f,1,.7f));
        Observe(fx,"burn-before-native-tint");
        Check(Snapshot().Contains("event=burn-before-native-tint") && Snapshot().Contains("burn=True"),
            "native burn flag admits the observation before its warm tint arrives");
        fx.Effects.Clear(); fx.Effects.Add(CardEffects.FXTask.LostMode); Observe(fx, "durable-lost-card");
        Check(Snapshot().Contains("burn=False lost=True"), "native durable lost effect remains observable independently of burn task");
        source.SetColor("_TintColor",new Color(1,.3f,0,1));
        fx.Effects.Clear(); Observe(fx, "native-animation-only");
        Check(Snapshot().Contains("burn=False lost=False") && Snapshot().Contains("anim=0.375"), "settled native warm fire animation is observable without native effect flags");
        string[] independent = QuestCardFireEvidence.Snapshot; independent[0] = "caller-overwrite";
        Check(!Snapshot().Contains("caller-overwrite"), "snapshots are independent arrays");
        Observe(fx, new string('X',2400));
        Check(QuestCardFireEvidence.Snapshot.Length == 1 && QuestCardFireEvidence.Snapshot[0].Length <= 900,
            "one native fire record is bounded to 900 characters");
        var cards = new List<CardEffects>();
        for (int i = 0; i < 9; i++)
        {
            var other = Create(canvas,source,source,expected,true); cards.Add(other); Observe(other,"new-burning-card-"+i);
            Check(QuestCardFireEvidence.Snapshot.Length <= 4, "native fire snapshots retain at most four cards");
        }
        Check(!Snapshot().Contains("fx="+fx.GetInstanceID()+" ") && Snapshot().Contains("fx="+cards[8].GetInstanceID()+" "),
            "bounded card rotation retains newest native fire observations");
        for (int i = 0; i < 256; i++) Observe(cards[8],"budget-event-"+i);
        Check(observations == 128, "native fire observation lane stops at 128 admitted observations");
        string terminal = Snapshot(); readsBefore = cards[8].NativeReads;
        for (int i = 0; i < 256; i++) Observe(cards[8],"after-budget-"+i);
        Check(Snapshot() == terminal && cards[8].NativeReads == readsBefore,
            "exhausted fire lane avoids native reads and preserves bounded completed evidence");
        foreach (Object item in Owned) if (item != null) Object.Destroy(item);
        yield break;
    }
}
