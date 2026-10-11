using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static IEnumerator NativeOverlays635()
    {
        TownServiceMirror.Shutdown(); _offeredSequence629 = 100000;
        Transform owner = Go("Native mage owner frame").transform;
        Transform observer = Go("Native mage observer frame").transform;
        observer.SetPositionAndRotation(new Vector3(5f, .24f, -.3f), Quaternion.Euler(0f, 71f, 0f));
        observer.localScale = Vector3.one * 1.4f;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        RectTransform canvas = (RectTransform)Go("Converted original highlighter canvas", owner).transform;
        canvas.sizeDelta = new Vector2(680f, 660f);
        // CanvasConversion.PlaceHost supplies a uniform metres-per-pixel basis.
        // Do not invent a sheared source mount and call it a hardware defect.
        canvas.localScale = Vector3.one * .001f;
        canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform holder = (RectTransform)NativeRow632(canvas, "");
        Check(holder.name == "CardHilight" && holder.GetComponentsInChildren<RectTransform>(true).Length == 11,
            "actual eleven-node serialized highlighter is used without a painted replacement");
        RectTransform aura = (RectTransform)holder.Find("Aura");
        RectTransform ring = (RectTransform)aura.Find("Highlight");
        Check(ring.GetComponent<Image>().sprite != null && ring.anchorMin == Vector2.zero && ring.anchorMax == Vector2.one,
            "native ring keeps its original sprite and stretched serialized child rect");
        // Native PlaceNewCard and UIEnchantressEffect.Play activate/fade these
        // serialized controls. Do not accidentally compare two hidden prefabs.
        holder.Find("CardHolder").GetComponent<CanvasGroup>().alpha = 1f;
        ring.GetComponent<CanvasGroup>().alpha = 1f;
        aura.Find("Types/Buy").gameObject.SetActive(true);
        holder.Find("GUI_LevelUp_Frame").gameObject.SetActive(false);
        RectTransform area = (RectTransform)holder.Find("Enhancement Ability Highlight Variant");
        Check(area != null && area.Find("Frame") != null && area.Find("Image") != null,
            "actual original pooled area prototype and children are imported");
        area.gameObject.AddComponent<UIEnhancementButtonHighlight>();
        CanvasGroup fill = area.Find("Image").GetComponent<CanvasGroup>();
        Check(fill != null, "actual original area fill carries its native CanvasGroup");
        // InstantiateNative creates a pooled native UI prototype in the flat
        // pool before the offered card is fitted into its small world canvas.
        // This retains the serialized local basis; it must not be pre-corrected
        // by the fixture before native SetParent(target) has happened.
        area.SetParent(owner, false);

        // The full card model and ability-row generation are external game-rule
        // boundaries. Keep their geometry explicit and drive the game's actual
        // pooled parenting contract, which retained the preceding world basis.
        var native = Go("Selected native ability card", holder.Find("CardHolder")).AddComponent<AbilityCardUI>();
        RectTransform sourcePrint = (RectTransform)Go("FullAbilityCard", native.transform).transform;
        sourcePrint.sizeDelta = new Vector2(325.1f, 449.5f);
        native.fullAbilityCard = sourcePrint;
        var highlighter = holder.gameObject.AddComponent<GloomhavenVR.WorldUI.UIEnhancementCardHighlighter>();
        highlighter.Card = native;
        RectTransform target = (RectTransform)Go("Actual ability target boundary", sourcePrint).transform;
        target.sizeDelta = new Vector2(270f, 108f); target.pivot = new Vector2(.17f, .83f);
        target.anchoredPosition = new Vector2(-42f, 83f);
        area.SetParent(target); // Native HighlightButtons: worldPositionStays=true.
        area.pivot = target.pivot; area.sizeDelta = target.rect.size; area.position = target.position;
        area.gameObject.SetActive(true);
        RectTransform physical = (RectTransform)Go("Actual adopted physical print boundary", owner).transform;
        physical.sizeDelta = sourcePrint.sizeDelta; physical.localScale = Vector3.one * .00049f;
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        physical.gameObject.AddComponent<CanvasGroup>();
        Image("Original card body boundary", physical, Vector2.zero, physical.sizeDelta, new Color(.24f, .21f, .17f, 1f));
        TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        var mask = native.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>();
        mask.Mask(); mask.SendMessage("LateUpdate");
        Func<Transform, bool> exclude = node => node == aura || node == area;
        TownServiceMirror.RegisterTemplate(3, 1, holder, exclude, "enchant.holder|");
        TownServiceMirror.RegisterTemplate(3, 2, aura, address: "enchant.holder|Aura#0");
        TownServiceMirror.RegisterTemplate(3, 3, area, address: "enchant.highlight|");
        TownServiceMirror.RegisterTemplate(3, 4, physical, address: "face.635|");
        TownServiceMirror.BeginSession(3, 635, owner, owner);
        TownServiceMirror.RegisterModule(10, 1, holder, exclude, "enchant.holder|");
        TownServiceMirror.RegisterModule(11, 2, aura, address: "enchant.holder|Aura#0");
        TownServiceMirror.RegisterModule(12, 3, area, address: "enchant.highlight|");
        TownServiceMirror.RegisterModule(13, 4, physical, address: "face.635|");
        foreach (ushort id in new ushort[] {10, 11, 12, 13}) TownServiceMirror.SetPriority(id, true);
        // Use one transport clock from the initial source sample onwards. A
        // future clock added only after admission falsely treats the first spin
        // sample as a one-second pause and stretches its interpolation period.
        float clock = Time.unscaledTime + 1f;
        FastCapture initial = OfferedCapture629(); Receive(1, initial.Artwork);
        CompleteOfferedArtwork632(initial.Artwork); OfferedReceive629(initial, clock);
        IEnumerator settle = FastSettle(observer, .16f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding admitted = Remote(1, 10)!, admittedAura = Remote(1, 11)!, admittedArea = Remote(1, 12)!, face = Remote(1, 13)!;
        File.WriteAllLines(Path.Combine(_output, "native-admission.log"), GloomhavenVR.Core.VRLog.Messages);
        Check(admitted != null && admittedAura != null && admittedArea != null && face != null,
            "serialized original partitions and physical print are admitted through production transport");
        RectTransform remoteRing = (RectTransform)admittedAura.Root.Find("Highlight");
        RectTransform remoteArea = (RectTransform)admittedArea.Root;
        OfferedRender629(clock); OfferedRender629(clock + .15f);
        string evidence = Path.Combine(_output, "native-overlays635-geometry.csv");
        File.WriteAllText(evidence, "step,subframe,sourcePhase,remotePhase,physicalYaw,pooledPrintYaw,sourceDiameter,sourceHeight,remoteAreaError\n");
        for (int step = 0; step < 14; step++)
        {
            yield return null; clock += .12f;
            float angle = step * 29f;
            // ObjectPool.SpawnCard restores position/scale while retaining the
            // preceding card rotation. The physical print is the plane, not the
            // highlighter's sibling Aura or the card holder's transform.
            float retainedYaw = step % 3 == 0 ? 90f : step % 3 == 1 ? 0f : -43f;
            native.transform.localRotation = Quaternion.Euler(0f, retainedYaw, 0f);
            physical.SetPositionAndRotation(new Vector3(.17f, .81f + Mathf.Sin(step * .21f) * .015f, -.2f),
                Quaternion.Euler(14f, angle, -7f));
            physical.localScale = Vector3.one * (.00049f * (step % 3 == 0 ? .6f : step % 3 == 1 ? 1f : 1.5f));
            // Original Rotate writes world-Z; a quiet source must reinterpret it
            // on the actual offered print before capture. Native Play/lifetime
            // is covered by the separate original-controller proof.
            aura.eulerAngles = new Vector3(0f, 0f, (step + 1) * 2.16f);
            fill.alpha = step % 3 == 0 ? 0f : step % 3 == 1 ? .1f : .35f;
            mask.SendMessage("LateUpdate");
            var targetCorners = new Vector3[4]; var sourceAreaCorners = new Vector3[4];
            target.GetWorldCorners(targetCorners); area.GetWorldCorners(sourceAreaCorners);
            for (int corner = 0; corner < 4; corner++)
                Check(Vector3.Distance(targetCorners[corner], sourceAreaCorners[corner]) < .000015f,
                    "real pooled native area loses its retained flat basis before offered-card capture");
            var sourceCorners = new Vector3[4]; var faceCorners = new Vector3[4];
            ring.GetWorldCorners(sourceCorners); physical.GetWorldCorners(faceCorners);
            float diameter = Vector3.Distance(sourceCorners[0], sourceCorners[1]);
            float width = Vector3.Distance(sourceCorners[0], sourceCorners[3]);
            float height = Vector3.Distance(faceCorners[0], faceCorners[1]);
            var fittedPrintCorners = new Vector3[4]; sourcePrint.GetWorldCorners(fittedPrintCorners);
            for (int corner = 0; corner < 4; corner++)
                Check(Vector3.Distance(fittedPrintCorners[corner],
                    faceCorners[corner] - physical.forward * (height * .0003f)) < .000005f,
                    "original pooled print fits the actual physical paper before source target and observer comparisons");
            File.AppendAllText(Path.Combine(_output, "source-circle.log"), "step=" + step + " phase=" + ring.localEulerAngles.z
                + " diameter=" + diameter + " width=" + width + " cardHeight=" + height
                + " aura=" + aura.localToWorldMatrix.ToString("F7") + " ring=" + ring.localToWorldMatrix.ToString("F7") + "\n");
            Check(Mathf.Abs(diameter - width) < diameter * .001f && diameter > height,
                "actual native ring is round and surrounds the entire physical card at every owner yaw");
            Vector3 sourceNormal = Vector3.Cross(sourceCorners[3] - sourceCorners[0], sourceCorners[1] - sourceCorners[0]).normalized;
            Check(Mathf.Abs(Vector3.Dot(sourceNormal, physical.forward)) > .99999f,
                "actual native ring uses the pooled physical print plane rather than its differently rotated sibling holder");
            FastCapture next = OfferedCapture629(); CompleteOfferedArtwork632(next.Artwork);
            foreach (byte[] bytes in next.Motion)
            {
                TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet);
                // Delay the independent holder header exactly as bounded town
                // partitions can arrive; retain every original print relation.
                packet!.Entries.RemoveAll(entry => entry.Module != 13 && entry.Kind == 1);
                packet.Sequence = ++_offeredSequence629; packet.SampleTime = clock;
                byte[] encoded = TownServiceMotionCodec.TryWritePacked(packet)!;
                Check(TownServiceMotionCodec.TryRead(encoded, encoded.Length, out packet),
                    "genuine serialized original numeric motion survives the production wire codec");
                Check(TownServiceMirror.ReceiveMotion(1, packet!), "observer accepts genuine native offered metadata");
                OfferedReceiptClock629(packet!);
            }
            OfferedRender629(clock);
            // The native ring may rotate its Aura mount, its drawing child, or
            // both when compensating the native basis. Measure the rendered ink
            // against its physical print rather than assuming a rotating child.
            float previous = (Quaternion.Inverse(face.Root.rotation) * remoteRing.rotation).eulerAngles.z;
            int movingFrames = 0;
            for (int subframe = 1; subframe <= 10; subframe++)
            {
                float now = clock + subframe * .012f;
                _camera.transform.SetPositionAndRotation(new Vector3(step * .2f, 3f, -4f), Quaternion.Euler(step * 9f, step * 31f, 0f));
                OfferedRender629(now);
                _offeredContext629 = "native635 step=" + step + " subframe=" + subframe;
                CheckOfferedCorners629(physical, area, face.Root, remoteArea,
                    "native original area stays on the same physical print throughout an observer turn");
                CheckOfferedPlane629(physical, ring, face.Root, remoteRing,
                    "native original spinning sprite remains coplanar with the owner physical card");
                Quaternion visiblePhase = Quaternion.Inverse(face.Root.rotation) * remoteRing.rotation;
                if (Mathf.Abs(Mathf.DeltaAngle(previous, visiblePhase.eulerAngles.z)) > .005f) movingFrames++;
                previous = visiblePhase.eulerAngles.z;
                File.AppendAllText(evidence, string.Join(",", step, subframe,
                    (Quaternion.Inverse(physical.rotation) * ring.rotation).eulerAngles.z,
                    visiblePhase.eulerAngles.z, angle, retainedYaw, diameter, height, AreaError635(physical, area, face.Root, remoteArea)) + "\n");
            }
            if (step > 0)
                Check(movingFrames >= 6, "actual original ring progresses on rendered subframes rather than snapping between samples");
            OfferedRender629(clock + .15f);
            CheckOfferedCorners629(physical, ring, face.Root, remoteRing,
                "settled observer exactly retains the original native ring sprite phase and full extent");
            Check(Mathf.Abs(remoteArea.Find("Image").GetComponent<CanvasGroup>().alpha - fill.alpha) < .0001f,
                "native original hover and selected fill opacity are shared without a viewer callback");
            foreach (Graphic graphic in admittedArea.Root.GetComponentsInChildren<Graphic>(true))
                Check(!graphic.raycastTarget, "observer original area presentation remains inert");
            if (step == 6 || step == 13)
            {
                Color32[] ownerPixels = RenderOffered629(canvas, sourcePrint, ring, area, 8, "native635-owner-" + step);
                Color32[] observerPixels = RenderOffered629(admitted.Root.parent, (RectTransform)admitted.Root.Find("CardHolder/Selected native ability card/FullAbilityCard"),
                    remoteRing, remoteArea, 9, "native635-observer-" + step);
                int ink = 0; long difference = 0;
                for (int pixel = 0; pixel < ownerPixels.Length; pixel++)
                {
                    if (!ownerPixels[pixel].Equals(ownerPixels[0])) ink++;
                    difference += Math.Abs(ownerPixels[pixel].r - observerPixels[pixel].r)
                        + Math.Abs(ownerPixels[pixel].g - observerPixels[pixel].g) + Math.Abs(ownerPixels[pixel].b - observerPixels[pixel].b);
                }
                Check(ink > 500 && difference <= ownerPixels.Length / 10,
                    "source and observer render the same serialized native ring and pooled area ink");
            }
        }
        mask.Restore(); TownServiceEnhancementHandoff.PhysicalCardFace = null;
        TownServiceMirror.Shutdown(); UnityEngine.Object.DestroyImmediate(owner.gameObject);
        UnityEngine.Object.DestroyImmediate(observer.gameObject);
    }

    private static float AreaError635(Transform sourcePrint, RectTransform area, Transform remotePrint, RectTransform remoteArea)
    {
        var a = new Vector3[4]; var b = new Vector3[4]; area.GetWorldCorners(a); remoteArea.GetWorldCorners(b);
        float error = 0f;
        for (int i = 0; i < 4; i++) error = Mathf.Max(error, Vector3.Distance(remotePrint.TransformPoint(sourcePrint.InverseTransformPoint(a[i])), b[i]));
        return error;
    }
}
