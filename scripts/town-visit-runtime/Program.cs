using System;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class InteractionProgram
{
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    public static int Run()
    {
        _assertions = 0;
        for (byte service = 1; service <= 3; service++)
        {
            var station = new GameObject("resident");
            var hand = VRHands.Primary = new VRHand();
            using (var target = new TownServiceVisitTarget(service, station.transform))
            {
                foreach (bool visible in new[] { false, true })
                {
                    target.Tick(visible); Physics.SyncTransforms();
                    Check(station.GetComponentsInChildren<Collider>().Length == 0 && VRInteractables.Pokes.Count == 0,
                        "approach residents have no invisible torso pick geometry");
                    Check(float.IsPositiveInfinity(TownServiceVisitTarget.OccludingDistance(hand.Origin, hand.Direction, 20)),
                        "empty space beside resident never clamps the laser");
                    target.OnPokeEnter(hand); target.OnPoke(hand); hand.TriggerDown = true; TownServiceVisitTarget.TickLaser();
                    Check(MapRoomDriver.Presses == 0 && hand.Hover == 0 && hand.Click == 0 && !hand.Ray.UiHitOverride.HasValue,
                        "approach residents never become invisible buttons");
                    hand.Ray.ComputeResidentOcclusion(hand.Origin, hand.Direction, 20, 3f);
                    Check(hand.Ray.SolidOccluderDistance == 3f && hand.Ray.SolidOccluderIsBoard,
                        "visible board geometry still blocks after proxy removal");
                }
            }
            Object.DestroyImmediate(station);
        }
        Check(!TownServiceVisitTarget.Replaces(EGuildmasterMode.None), "other destination retains native button");
        foreach (EGuildmasterMode mode in new[] { EGuildmasterMode.Merchant, EGuildmasterMode.Temple, EGuildmasterMode.Enchantress })
        {
            Check(TownServiceVisitTarget.Replaces(mode), "available resident replaces its obsolete button");
            WorldUIConfig.ImmersiveTownServices.Value = false;
            Check(!TownServiceVisitTarget.Replaces(mode), "disabled immersion keeps native destination");
            WorldUIConfig.ImmersiveTownServices.Value = true;
        }
        VisibleCanvas();
        OfferingPickup();
        return _assertions;
    }
    private static void OfferingPickup()
    {
        foreach(bool mage in new[]{true,false}) foreach(float scale in new[]{.05f,1f,198.12f})
        foreach(float reach in new[]{.1f,2f})
        {
            var hand=VRHands.Primary=new VRHand{WorldScale=scale,Origin=Vector3.back*reach*scale,Direction=Vector3.forward};
            var go=new GameObject("actual offered physical card");
            FixtureCard card=mage?go.AddComponent<GloomhavenVR.Cards.VRCard>():go.AddComponent<GloomhavenVR.Cards.ItemsPile.ItemChip>();
            var collider=go.AddComponent<BoxCollider>();collider.size=new Vector3(.18f,.24f,.009f)*scale;
            VRInteractables.Grabbables.Add(new VRInteractables.Entry{Target=card,Collider=collider});Physics.SyncTransforms();
            try
            {
                Check(TownServicePhysicalRay.TryPick(hand,out var target,out _,out _) && ReferenceEquals(target,card),
                    mage?"offered mage card is physically taken by the same trigger ray route":"offered merchant card is physically taken by the same trigger ray route");
                card.Owned=false;
                Check(!TownServicePhysicalRay.TryPick(hand,out _,out _,out _),"foreign offering never bypasses native ownership");
                card.Owned=true;hand.RayUgui.HasHit=true;hand.RayUgui.HitDistance=.01f*scale;hand.TriggerDown=true;
                TownServicePhysicalRay.Tick(hand);
                Check(hand.Grabber.Held==null,"nearer visible native UI keeps its own trigger");
                hand.RayUgui.HasHit=false;TownServicePhysicalRay.Tick(hand);
                Check(ReferenceEquals(hand.Grabber.Held,card)&&card.Grabbed,"pickup retains physical offered identity in the hand");
            }
            finally {VRInteractables.Grabbables.Clear();Object.DestroyImmediate(go);}
        }
    }

    private static void VisibleCanvas()
    {
        var root = new GameObject("transparent party layout", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var imageGo = new GameObject("visible character column", typeof(RectTransform), typeof(Image));
        imageGo.transform.SetParent(root.transform, false);
        var image = imageGo.GetComponent<Image>(); var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.sizeDelta = new Vector2(100,200);
        try
        {
            Canvas.ForceUpdateCanvases();
            Vector2 center = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            Check(VisibleUiSurface.Contains(canvas, center, null), "painted original widget is a laser surface");
            Check(!VisibleUiSurface.Contains(canvas, center + Vector2.right * 200, null), "transparent character frame does not clamp beam");
            image.raycastTarget = false;
            Check(VisibleUiSurface.Contains(canvas, center, null), "visible decorative paper still occludes background UI");
            image.color = Color.clear; Canvas.ForceUpdateCanvases();
            Check(!VisibleUiSurface.Contains(canvas, center, null), "transparent native hit image does not invent a surface");
            image.color = Color.white;
            var group = imageGo.AddComponent<CanvasGroup>(); group.alpha=0f; Canvas.ForceUpdateCanvases();
            Check(!VisibleUiSurface.Contains(canvas, center, null), "hidden CanvasGroup cannot retain an invisible laser surface");
            group.alpha=1f; group.interactable=false; Canvas.ForceUpdateCanvases();
            Check(VisibleUiSurface.Contains(canvas, center, null), "disabled but visible control still blocks UI behind it");
            image.enabled=false; Canvas.ForceUpdateCanvases();
            Check(!VisibleUiSurface.Contains(canvas, center, null), "disabled graphic is no laser surface");
            image.enabled=true;
            var childGo = new GameObject("nested original canvas", typeof(RectTransform),typeof(Canvas));
            childGo.transform.SetParent(root.transform,false); var nested=childGo.GetComponent<Canvas>();
            imageGo.transform.SetParent(childGo.transform,false); UguiPokeSurfaces.Children=new(){nested}; Canvas.ForceUpdateCanvases();
            center=RectTransformUtility.WorldToScreenPoint(null,rect.position);
            Check(VisibleUiSurface.Contains(canvas,center,null),"nested original canvas remains part of visible surface");
            nested.enabled=false; Canvas.ForceUpdateCanvases();
            Check(!VisibleUiSurface.Contains(canvas,center,null),"disabled nested canvas does not leave a ghost surface");
        }
        finally { UguiPokeSurfaces.Children=null; Object.DestroyImmediate(root); }
    }
}
