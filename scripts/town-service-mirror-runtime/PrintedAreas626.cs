using System;
using System.Collections;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    // The physical offered-face endpoint is an external handoff boundary here;
    // its actual ownership and selection path has the handoff runtime harness.
    // Mask fitting, original capture, wire, admission and playback are production.
    internal sealed class UIEnhancementCardHighlighter : MonoBehaviour
    { internal AbilityCardUI? Card; }
}

public static partial class MirrorProgram
{
    private static IEnumerator PrintedAreas626()
    {
        TownServiceMirror.Shutdown();
        Transform owner = Go("Owner printed area frame").transform;
        Transform observer = Go("Observer printed area frame").transform;
        observer.SetPositionAndRotation(new Vector3(6f, .13f, -.4f), Quaternion.Euler(0f, 83f, 0f));
        observer.localScale = Vector3.one * 1.4f;
        GameObject holder = Go("CardHilight", owner);
        RectTransform root = (RectTransform)holder.transform; root.sizeDelta = new Vector2(325f, 450f);
        Canvas canvas = holder.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = _camera;
        holder.AddComponent<CanvasGroup>();
        var highlighter = holder.AddComponent<GloomhavenVR.WorldUI.UIEnhancementCardHighlighter>();
        GameObject printed = Go("Native pooled card", root);
        var nativeCard = printed.AddComponent<GloomhavenVR.WorldUI.AbilityCardUI>();
        RectTransform nativeFace = (RectTransform)Go("FullAbilityCard", printed.transform).transform;
        nativeFace.sizeDelta = new Vector2(294f, 450f); nativeFace.pivot = new Vector2(.31f, .68f);
        nativeFace.anchoredPosition = new Vector2(19f, -11f); nativeFace.localScale = Vector3.one * .84f;
        nativeCard.fullAbilityCard = nativeFace; highlighter.Card = nativeCard;
        Image area = Image("Selectable native printed row", nativeFace, new Vector2(47f, -62f),
            new Vector2(171f, 82f), new Color(.1f, .8f, 1f, .75f));
        area.gameObject.AddComponent<GloomhavenVR.WorldUI.UIEnhancementButtonHighlight>();
        area.rectTransform.pivot = new Vector2(.23f, .77f);
        RectTransform physical = (RectTransform)Go("Actually adopted face", owner).transform;
        physical.sizeDelta = new Vector2(294f, 450f); physical.localScale = Vector3.one * .00049f;
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        TownServiceMirror.RegisterTemplate(3, 1, root, address: "enchant.holder|");
        var mask = printed.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask();
        TownServiceMirror.BeginSession(3, 626, owner, owner);
        TownServiceMirror.RegisterModule(10, 1, root, address: "enchant.holder|");
        TownServiceMirror.SetPriority(10, true);
        var sourceCorners = new Vector3[4]; var remoteCorners = new Vector3[4];
        foreach (float scale in new[] { .07f, .6f, 1f, 2f })
        foreach (float angle in new[] { 0f, 43f, 119f })
        {
            physical.SetPositionAndRotation(owner.TransformPoint(new Vector3(.17f, .81f, -.2f)),
                Quaternion.Euler(17f, angle, -9f));
            physical.localScale = new Vector3(.00049f * scale * .91f, .00049f * scale * 1.13f, .00049f * scale);
            root.SetPositionAndRotation(physical.position, physical.rotation);
            root.localScale = new Vector3(.00052f * .73f, .00052f * 1.16f, .00052f);
            mask.SendMessage("LateUpdate");
            yield return null;
            List<byte[]> packets = Capture();
            // Sender sampling is bounded; wait for the next genuine changed native
            // frame, never substitute a handwritten snapshot or a ten-second repair.
            for (float until = Time.unscaledTime + .3f; packets.Count == 0 && Time.unscaledTime < until;)
            { yield return null; packets = Capture(); }
            Receive(1, packets);
            for (float until = Time.unscaledTime + .3f;
                TownServiceMirror.InteractionOwner(3) != 1 && Time.unscaledTime < until;) yield return null;
            TownServiceMirror.TickRemote(_ => observer);
            TownServiceBinding? admitted = Remote(1);
            Check(admitted != null, "corrected native printed areas are admitted on the first owner capture without a repair deadline");
            for (float until = Time.unscaledTime + .16f; Time.unscaledTime < until;)
            { TownServiceMirror.TickRemote(_ => observer); yield return null; }
            TownServiceMirror.TickRemote(_ => observer);
            RectTransform remoteArea = (RectTransform)admitted!.Root.Find("Native pooled card/FullAbilityCard/Selectable native printed row");
            area.rectTransform.GetWorldCorners(sourceCorners); remoteArea.GetWorldCorners(remoteCorners);
            for (int corner = 0; corner < 4; corner++)
                Check(Vector3.Distance(remoteCorners[corner], observer.TransformPoint(owner.InverseTransformPoint(sourceCorners[corner]))) < .000015f,
                    "admitted original native area corners retain exact owner print fitting across pivots, rotations and asymmetric scales");
            Check(remoteArea.GetComponent<Image>().enabled && remoteArea.gameObject.activeInHierarchy,
                "offered native area ink stays present through actual capture, admission and native playback");
            if (scale == 1f && angle == 43f)
            {
                Color32[] ownerInk = Render(root, 8, "native-print-626-owner");
                Color32[] observerInk = Render(admitted.Root, 9, "native-print-626-observer");
                int visible = 0, worst = 0; long totalError = 0;
                for (int pixel = 0; pixel < ownerInk.Length; pixel++)
                {
                    if (!ownerInk[pixel].Equals(ownerInk[0])) visible++;
                    int error = Math.Abs(ownerInk[pixel].r - observerInk[pixel].r)
                        + Math.Abs(ownerInk[pixel].g - observerInk[pixel].g)
                        + Math.Abs(ownerInk[pixel].b - observerInk[pixel].b)
                        + Math.Abs(ownerInk[pixel].a - observerInk[pixel].a);
                    totalError += error; worst = Math.Max(worst, error);
                }
                Check(visible > 500, "corrected offered native area produces real rendered ink rather than an empty matching image");
                Check(totalError <= ownerInk.Length / 50 && worst <= 20,
                    "corrected owner and admitted observer render the same native area ink");
            }
        }
        mask.Restore(); GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.PhysicalCardFace = null;
        TownServiceMirror.Shutdown(); UnityEngine.Object.DestroyImmediate(owner.gameObject);
        UnityEngine.Object.DestroyImmediate(observer.gameObject);
    }
}
