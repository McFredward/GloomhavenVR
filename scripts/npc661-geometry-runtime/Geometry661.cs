using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private const string Geometry661Invariant = "offered body and printed front preserve exact original geometry on every render";
    private static int _geometry661Frames;
    private static void RegisterPhysical661(Transform body, Transform print)
    {
        // The old-code causal control has no registration API. Keep its actual
        // production path intact and let the same rendered invariant reject it.
        typeof(TownServiceMirror).GetMethod("RegisterOfferedPhysical", PrivateStatic)?.Invoke(null, new object[] { body, print });
    }

    private static void RenderGeometry661(float now)
    {
        // Deterministic subframes use the same opening restore seam that the
        // production TickRemote calls before its ordinary interpolation pass.
        typeof(TownServiceMirror).GetMethod("RestoreOfferedPhysicalMounts", PrivateStatic)?.Invoke(null, null);
        OfferedRender629(now);
    }

    private static IEnumerator Geometry661()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); _offeredSequence629 = 661000; _geometry661Frames = 0;
        Transform owner = Go("661 owner shared world").transform;
        Transform observer = Go("661 observer shared world").transform;
        owner.localScale = Vector3.one * .8f;
        owner.rotation = Quaternion.Euler(0f, 23f, 0f);
        observer.localScale = Vector3.one * 1.2f;
        observer.SetPositionAndRotation(new Vector3(7f, .2f, -.3f), Quaternion.Euler(0f, 79f, 0f));
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        Transform card = Go("661 real source card frame", owner).transform;
        card.localPosition = new Vector3(.2f, .4f, -.1f);
        VRCard ownerCard = card.gameObject.AddComponent<VRCard>();
        ownerCard.FixtureBacking(new Vector2(.14406f, .2205f));
        Transform body = card.Find("Visual/Backing");
        Check(body.GetComponent<MeshFilter>() != null && body.GetComponent<MeshRenderer>() != null,
            "geometry proof uses the actual procedural VRCard factory and complete CardMesh engine");
        RectTransform canvas = (RectTransform)Go("FaceCanvas", card).transform;
        canvas.localScale = Vector3.one * .00049f;
        canvas.localPosition = new Vector3(0f, 0f, -.0012f);
        canvas.sizeDelta = new Vector2(294f, 450f);
        canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform physical = (RectTransform)Go("FullAbilityCard", canvas).transform;
        physical.sizeDelta = canvas.sizeDelta;
        physical.gameObject.AddComponent<CanvasGroup>();
        Image frontInk = Image("Actual front artwork", physical, Vector2.zero, physical.sizeDelta, new Color(.8f, .04f, .03f));
        Image("Actual front identity mark", physical, new Vector2(30f, 85f), new Vector2(57f, 44f), Color.white);

        RectTransform nativeCanvas = (RectTransform)Go("Actual converted native canvas", owner).transform;
        nativeCanvas.localScale = new Vector3(.00073f, .00116f, .001f);
        nativeCanvas.localRotation = Quaternion.Euler(0f, 27f, 11f);
        nativeCanvas.sizeDelta = new Vector2(680f, 660f);
        nativeCanvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform holder = (RectTransform)Go("CardHilight", nativeCanvas).transform;
        holder.sizeDelta = new Vector2(325f, 450f); holder.localScale = Vector3.one * .52f;
        holder.gameObject.AddComponent<CanvasGroup>();
        var highlighter = holder.gameObject.AddComponent<UIEnhancementCardHighlighter>();
        var nativeCard = Go("Native pooled card", holder).AddComponent<AbilityCardUI>();
        RectTransform nativePrint = (RectTransform)Go("FullAbilityCard", nativeCard.transform).transform;
        nativePrint.sizeDelta = physical.sizeDelta; nativePrint.localScale = Vector3.one * .84f;
        nativePrint.pivot = new Vector2(.31f, .68f); nativePrint.anchoredPosition = new Vector2(19f, -11f);
        nativeCard.fullAbilityCard = nativePrint; highlighter.Card = nativeCard;
        Image row = Image("Original selected row", nativePrint, new Vector2(47f, -62f), new Vector2(171f, 82f), Color.cyan);
        row.gameObject.AddComponent<UIEnhancementButtonHighlight>();
        RectTransform aura = (RectTransform)Go("Aura", holder).transform; aura.sizeDelta = new Vector2(600f, 600f);
        Image ring = Image("Original aura ink", aura, Vector2.zero, new Vector2(520f, 520f), new Color(.1f, .7f, 1f, .35f));
        TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        var mask = nativeCard.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask(); mask.SendMessage("LateUpdate");
        RegisterPhysical661(body, physical);
        TownServiceMirror.RegisterMotionOffering(body, true); TownServiceMirror.RegisterMotionOffering(physical, true);
        TownServiceMirror.RegisterTemplate(3, 1, holder, address: "enchant.holder|");
        TownServiceMirror.RegisterTemplate(3, 2, physical, address: "face.661|");
        string bodyAddress = TownServiceAbilityBody.Key(ownerCard) + "|";
        TownServiceMirror.RegisterTemplate(3, 3, body, address: bodyAddress);
        TownServiceMirror.BeginSession(3, 661, owner, owner);
        TownServiceMirror.RegisterModule(10, 1, holder, address: "enchant.holder|");
        TownServiceMirror.RegisterModule(11, 2, physical, address: "face.661|");
        TownServiceMirror.RegisterModule(12, 3, body, address: bodyAddress);
        foreach (ushort module in new ushort[] { 10, 11, 12 }) TownServiceMirror.SetPriority(module, true);
        FastCapture initial = OfferedCapture629(); Receive(1, initial.Artwork); DeliverMotion(1, initial);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding frontCopy = Remote(1, 11)!, bodyCopy = Remote(1, 12)!, holderCopy = Remote(1, 10)!;
        Check(frontCopy != null && bodyCopy != null && holderCopy != null,
            "actual original body, printed front and overlay are independently admitted before movement");
        Check(ReferenceEquals(body.GetComponent<MeshFilter>().sharedMesh, bodyCopy.Root.GetComponent<MeshFilter>().sharedMesh),
            "observer retains actual original body vertices and back-facing triangle partitions");
        RectTransform remoteRow = (RectTransform)holderCopy.Root.Find("Native pooled card/FullAbilityCard/Original selected row");
        RectTransform remoteRing = (RectTransform)holderCopy.Root.Find("Aura/Original aura ink");
        float clock = Time.unscaledTime + 1f;
        var delayed = new List<TownServiceMotionPacket>();
        for (int sample = 0; sample < 32; sample++)
        {
            yield return null;
            clock += .08f;
            // The first24 samples retain the actual scalar room-scale contract
            // and nonuniform native converted canvas. Eight further body-only
            // samples challenge complete affine/reflected print ancestry; they
            // do not certify the unrelated native overlay world-TRS protocol.
            owner.localScale = sample < 24 ? Vector3.one * .8f : new Vector3(.8f, .69f, .92f);
            observer.localScale = sample < 24 ? Vector3.one * 1.2f
                : sample < 28 ? new Vector3(1.2f, .93f, 1.1f) : new Vector3(-1.2f, .93f, 1.1f);
            card.localPosition = new Vector3(.2f, .4f + .006f * Mathf.Sin(sample * .45f), -.1f);
            card.localRotation = Quaternion.Euler(4f * Mathf.Sin(sample * .3f), 55f + sample * 21f, 2f);
            card.localScale = Vector3.one * (sample % 3 == 0 ? .85f : sample % 3 == 1 ? 1f : 1.15f);
            row.color = sample % 2 == 0 ? Color.cyan : Color.green;
            frontInk.color = sample % 2 == 0 ? new Color(.8f, .04f, .03f) : new Color(.7f, .1f, .02f);
            mask.SendMessage("LateUpdate");
            FastCapture capture = OfferedCapture629();
            // Keep real source records and the production codec. Only actual
            // transport loss/order/jitter is controlled; no geometry is injected.
            foreach (TownServiceMotionPacket prior in delayed)
            {
                prior.Sequence = ++_offeredSequence629; prior.SampleTime = clock - .08f;
                ReceiveGeometry661(prior, clock);
            }
            delayed.Clear();
            foreach (byte[] bytes in capture.Motion)
            {
                Check(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet), "real offered geometry decodes from bounded numeric packet");
                var late = new TownServiceMotionPacket { SampleTime = clock };
                for (int entry = packet!.Entries.Count - 1; entry >= 0; entry--)
                {
                    TownServiceMotionEntry value = packet.Entries[entry];
                    if (value.Module == 12 && value.Kind == 1 && sample % 3 != 0)
                    { late.Entries.Add(value); packet.Entries.RemoveAt(entry); }
                    else if (value.Module == 12 && value.Kind == 9 && sample % 4 != 0)
                        packet.Entries.RemoveAt(entry); // exact relative geometry remains unchanged
                }
                if (late.Entries.Count != 0) delayed.Add(late);
                packet.Sequence = ++_offeredSequence629; packet.SampleTime = clock;
                ReceiveGeometry661(packet, clock);
            }
            for (int render = 0; render < 7; render++)
            {
                float now = clock + render * .011f;
                RenderGeometry661(now); Canvas.ForceUpdateCanvases(); _geometry661Frames++;
                float error = GeometryError661(body, physical, bodyCopy.Root, frontCopy.Root);
                if (error >= .00005f)
                {
                    File.AppendAllText(Path.Combine(_output, "geometry661-failure.txt"),
                        "sample=" + sample + " render=" + render + " maxVertexDistance=" + error.ToString("F8")
                        + " bodyRotation=" + bodyCopy.Root.rotation.eulerAngles + " frontRotation=" + frontCopy.Root.rotation.eulerAngles + "\n");
                    RenderPhysical661(body, physical, 8, false, "failure-owner");
                    RenderPhysical661(bodyCopy.Root, frontCopy.Root, 9, false, "failure-observer");
                }
                Check(error < .00005f, Geometry661Invariant);
                if (sample < 24)
                {
                    CheckOfferedCorners629(physical, row.rectTransform, frontCopy.Root, remoteRow,
                        "native enhancement overlay stays registered to coherent body and print during owner rotation");
                    CheckOfferedPlane629(physical, ring.rectTransform, frontCopy.Root, remoteRing,
                        "stable offered ring retains exact printed plane throughout body and front rotation");
                }
                foreach (bool back in new[] { false, true })
                {
                    string label = "frame-" + _geometry661Frames + (back ? "-back" : "-front");
                    Color32[] reference = RenderPhysical661(body, physical, 8, back, sample % 8 == 0 && render == 3 ? label + "-owner" : null);
                    Color32[] observed = RenderPhysical661(bodyCopy.Root, frontCopy.Root, 9, back, sample % 8 == 0 && render == 3 ? label + "-observer" : null);
                    int changed = 0, ink = 0;
                    for (int i = 0; i < reference.Length; i++)
                    {
                        if (reference[i].r > 40 || reference[i].g > 40 || reference[i].b > 40) ink++;
                        bool originalInk = reference[i].r > 40 || reference[i].g > 40 || reference[i].b > 40;
                        bool receivedInk = observed[i].r > 40 || observed[i].g > 40 || observed[i].b > 40;
                        if (originalInk != receivedInk) changed++;
                    }
                    Check(ink > 3000, "actual original card submits visible ink from its front and opposing native back");
                    // Front content colors have their own allowed interpolation.
                    // Compare silhouette/occlusion pixels, preserving full native materials.
                    if (changed >= 80)
                    {
                        File.AppendAllText(Path.Combine(_output, "geometry661-raster-failure.txt"), label + " changed=" + changed + " ink=" + ink + "\n");
                        RenderPhysical661(body, physical, 8, back, "raster-failure-owner");
                        RenderPhysical661(bodyCopy.Root, frontCopy.Root, 9, back, "raster-failure-observer");
                    }
                    Check(changed < 80, "coherent physical card retains native front/back silhouette on every camera render");
                }
            }
        }
        mask.Restore();
        bool withdrawn = false;
        for (int turn = 0; turn < 8 && !withdrawn; turn++)
        {
            FastCapture withdraw = OfferedCapture629(); OfferedReceive629(withdraw, clock + .2f + turn * .08f);
            RenderGeometry661(clock + .2f + turn * .08f);
            foreach (byte[] bytes in withdraw.Motion)
            {
                TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet);
                foreach (var entry in packet!.Entries) if (entry.Kind == 9 && entry.Module == 12 && !entry.Visible) withdrawn = true;
            }
            yield return null;
        }
        Check(withdrawn, "ending the exact native offer withdraws physical body affinity before reuse or return");
        File.WriteAllText(Path.Combine(_output, "geometry661-receipt.txt"), "renders=" + _geometry661Frames + "\nassertions=" + _assertions + "\n");
        TownServiceEnhancementHandoff.PhysicalCardFace = null; TownServiceMirror.Shutdown();
    }

    private static void ReceiveGeometry661(TownServiceMotionPacket packet, float receipt)
    {
        byte[] wire = TownServiceMotionCodec.TryWritePacked(packet)!;
        Check(wire != null && TownServiceMotionCodec.TryRead(wire, wire.Length, out var decoded), "jittered actual offered records retain exact wire geometry");
        TownServiceMotionCodec.TryRead(wire, wire.Length, out var received);
        Check(TownServiceMirror.ReceiveMotion(1, received!), "actual offered receiver accepts reordered source records");
        float authored = received!.SampleTime; received.SampleTime = receipt; OfferedReceiptClock629(received); received.SampleTime = authored;
    }

    private static float GeometryError661(Transform body, Transform print, Transform remoteBody, Transform remotePrint)
    {
        float error = 0f;
        foreach (Vector3 vertex in body.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Vector3 expected = remotePrint.TransformPoint(print.InverseTransformPoint(body.TransformPoint(vertex)));
            error = Mathf.Max(error, Vector3.Distance(expected, remoteBody.TransformPoint(vertex)));
        }
        return error;
    }

    private static Color32[] RenderPhysical661(Transform body, Transform print, int layer, bool back, string? name)
    {
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(body, layer); Layer(print, layer);
        foreach (Canvas canvas in print.GetComponentsInParent<Canvas>(true)) canvas.gameObject.layer = layer;
        _camera.cullingMask = 1 << layer;
        Vector3 center = print.TransformPoint(((RectTransform)print).rect.center);
        Quaternion view = back ? print.rotation * Quaternion.Euler(0f, 180f, 0f) : print.rotation;
        _camera.transform.SetPositionAndRotation(center - view * Vector3.forward * 2f, view);
        Vector3[] corners = new Vector3[4]; ((RectTransform)print).GetWorldCorners(corners);
        _camera.orthographicSize = ((RectTransform)print).rect.height * .65f;
        // Normalize only the camera to the actual complete print matrix. World
        // vertices are independently checked before this readback. This exposes
        // body/print misregistration even under nonuniform/reflected ancestors,
        // without mistaking differing shared-room scale for different source ink.
        Matrix4x4 localView = Matrix4x4.TRS(new Vector3(0f, 0f, back ? 10f : -10f),
            back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, Vector3.one);
        _camera.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * localView.inverse * print.worldToLocalMatrix;
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(256, 192, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var image = new Texture2D(256, 192, TextureFormat.RGBA32, false);
        try
        {
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 256, 192), 0, 0); image.Apply();
            if (name != null) File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = null; _camera.targetTexture = null;
            _camera.ResetWorldToCameraMatrix();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
