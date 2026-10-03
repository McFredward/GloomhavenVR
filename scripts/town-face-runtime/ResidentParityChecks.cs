using System;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

internal static class ResidentParityChecks
{
    internal static int Run(Transform root, Vector3 eye, Camera camera)
    {
        int count = 0;
        void Check(bool valid, string reason) { count++; if (!valid) throw new Exception(reason); }
        TownServiceFaceAttention.ResetBlessingFocus();
        NetAvatarDriver.Heads.Clear(); TownServiceMirror.RemoteSessions.Clear();
        TownServicePresentation.Active = false;
        camera.transform.position = eye + new Vector3(.2f, 0f, 1.4f);
        NetAvatarDriver.Heads[2] = eye + new Vector3(-.2f, 0f, 2f);
        TownServiceMirror.BrowsingOwner = 2;
        TownServiceMirror.CardOwner = 0;
        FaceClock.Now += 2f;
        var attention = new TownServiceFaceAttention();
        Vector3? selected = attention.Select(1, root, Quaternion.identity, eye);
        Check(selected.HasValue && selected.Value.x > eye.x,
            "browsing cannot monopolize resident gaze before a physical offer");
        TownServiceMirror.RemoteSessions[2] = new TownServiceSessionInfo
            { Active = true, Service = 1, Session = 9, LastSeenTime = FaceClock.Now };
        NetAvatarDriver.Heads[2] = eye + new Vector3(-.2f, 0f, 3f);
        FaceClock.Now += 2f;
        selected = new TownServiceFaceAttention().Select(1, root, Quaternion.identity, eye);
        Check(selected.HasValue && selected.Value.x > eye.x,
            "a distant native visit cannot suppress a nearby valid resident gaze");
        NetAvatarDriver.Heads[2] = eye + new Vector3(-.2f, 0f, 2f);
        TownServiceMirror.CardOwner = 2;
        selected = attention.Select(1, root, Quaternion.identity, eye);
        Check(selected.HasValue && selected.Value.x < eye.x,
            "a physically offered card owns the exclusive merchant gaze");
        selected = attention.Select(3, root, Quaternion.identity, eye);
        Check(selected.HasValue && selected.Value.x < eye.x,
            "a physically offered card owns the exclusive mage gaze");
        NetAvatarDriver.Heads.Remove(2);
        Check(!attention.Select(1, root, Quaternion.identity, eye).HasValue,
            "missing physical owner does not hand its attention to another visitor");
        TownServiceMirror.CardOwner = 0;
        selected = attention.Select(1, root, Quaternion.identity, eye);
        Check(selected.HasValue && selected.Value.x > eye.x,
            "removing the offered card immediately frees resident attention");
        NetAvatarDriver.Heads[2] = eye + new Vector3(-.2f, 0f, 2f);
        var priestess = new TownServiceFaceAttention();
        TownServiceFaceAttention.BlessVisitor(2, .2f);
        selected = priestess.Select(2, root, Quaternion.identity, eye);
        Check(selected.HasValue && selected.Value.x < eye.x,
            "priestess looks at the currently blessed visitor without taking a lease");
        FaceClock.Now += TownServiceActivityMotion.TempleBlessingVisualSeconds;
        selected = priestess.Select(2, root, Quaternion.identity, eye);
        Check(selected.HasValue && selected.Value.x > eye.x,
            "completed blessing releases its temporary gaze focus");
        TownServiceFaceAttention.ResetBlessingFocus();
        TownServiceMirror.BrowsingOwner = TownServiceMirror.CardOwner = 0;
        NetAvatarDriver.Heads.Clear(); TownServiceMirror.RemoteSessions.Clear();

        TownServiceSharedCue.Reset();
        TownServiceSharedCue.ObserveMerchantVisitor(2, 15, true);
        Check(TownServiceSharedCue.HasReadyMerchantVisitor,
            "numeric merchant readiness extends the shared palm without a guide canvas");
        TownServiceSharedCue.ObserveMerchantVisitor(2, 15, false);
        Check(!TownServiceSharedCue.HasReadyMerchantVisitor,
            "merchant readiness withdrawal releases the shared palm");
        TownServiceMirror.CardOwner = 2;
        Check(TownServiceSharedCue.HasReadyMerchantVisitor,
            "physically parked merchant card retains its globally offered hand");
        TownServiceMirror.CardOwner = 0;
        TownServiceSharedCue.SetLocal(true, .7f); TownServiceSharedCue.Tick();
        Check(TownServiceSharedCue.LocalReady && TownServiceSharedCue.LocalStrength == .7f
            && !TownServiceSharedCue.PublishedReady && TownServiceSharedCue.PublishedGuideOwner == 0,
            "personal local guide readiness does not elect a shared visual owner");
        var remote = new TownServiceSessionInfo
            { Active = true, Service = 3, Session = 12, LastSeenTime = FaceClock.Now };
        TownServiceMirror.RemoteSessions[2] = remote;
        TownServiceSharedCue.ObserveVisitor(2, 11, true, .8f);
        Check(!TownServiceSharedCue.HasReadyVisitor(3),
            "ready evidence from a previous native visit cannot extend the mage hand");
        TownServiceSharedCue.ObserveVisitor(2, 12, true, .8f);
        Check(TownServiceSharedCue.HasReadyVisitor(3),
            "fresh visitor readiness survives local-only remote guide suppression");
        remote.Service = 1;
        Check(!TownServiceSharedCue.HasReadyVisitor(3), "changed service retires old mage readiness");
        remote.Service = 3; FaceClock.Now += 3.01f;
        Check(!TownServiceSharedCue.HasReadyVisitor(3), "stale mage readiness cannot retain the shared palm");

        var guide = new GameObject("Local native drop guide", typeof(RectTransform), typeof(CanvasGroup));
        var border = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        border.transform.SetParent(guide.transform, false);
        var gate = guide.GetComponent<CanvasGroup>(); gate.alpha = 1f;
        TownServiceSharedCue.PaintRemote(gate, guide.transform);
        Check(gate.alpha == 0f && !gate.interactable && !gate.blocksRaycasts,
            "pre-drop guide exception leaves observer guide invisible");
        gate.alpha = .7f; TownServiceSharedCue.PaintLocal(gate, guide.transform);
        Check(gate.alpha == .7f, "shared readiness cannot alter an eligible player's original local guide");
        var hand = new VRHand();
        var feedback = new TownServiceOfferFeedback(gate, guide.transform);
        FaceClock.Delta = .1f;
        float strength = feedback.Tick(true, hand, .20f, false, .28f);
        feedback.Paint(true);
        Check(hand.HoverPulses == 1 && hand.SnapPulses == 0 && strength > 0f,
            "valid purse approach pulses once before its snap edge");
        Check(gate.alpha == 1f && guide.transform.localScale.x > .001f
            && border.GetComponent<Image>().color.g > .67f,
            "actual local guide ink and scale react to valid purse approach");
        feedback.Tick(true, hand, .20f, false, .28f);
        Check(hand.HoverPulses == 1, "steady purse approach does not repeat a haptic each frame");
        FaceClock.Now += .1f;
        feedback.Tick(true, hand, .02f, true, .28f, snapPulse: false);
        Check(hand.SnapPulses == 0,
            "temple guide leaves the inside-volume snap pulse to the original held token");
        feedback.Clear();
        feedback.Tick(false, hand, .02f, true, .28f);
        feedback.Paint(false);
        Check(gate.alpha == 0f && hand.HoverPulses == 1 && hand.SnapPulses == 0,
            "ineligible purse drop never paints or pulses an actionable destination");
        UnityEngine.Object.DestroyImmediate(guide);
        TownServiceSharedCue.Reset(); TownServiceMirror.RemoteSessions.Clear();
        FaceClock.Delta = 1f / 90f;
        return count;
    }
}
