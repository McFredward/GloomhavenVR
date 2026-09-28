using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class InteractionProgram
{
    private static int count;
    private static void Check(bool condition, string reason) { count++; if (!condition) throw new Exception(reason); }
    public static int Run()
    {
        count = 0;
        OfferFeedback();
        NativeFrame();
        PhysicalCardAura();
        NativePhysicalPoke();
        NativeLaserOcclusion();
        FirstVisitCue();
        Approach();
        WalkAway();
        foreach (float scale in new[] { .05f, 1f, 2f, 198.12f })
        for (int scenario = 0; scenario < 22; scenario++) RunCase(scale, scenario);
        SwapOffering();
        return count;
    }

    private static void NativeFrame()
    {
        var root = new GameObject("CardHilight", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(UIEnhancementCardHighlighter));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        ((RectTransform)root.transform).sizeDelta = new Vector2(325.1f, 449.5f);
        var aura = new GameObject("Aura", typeof(RectTransform));
        aura.transform.SetParent(root.transform, false);
        ((RectTransform)aura.transform).sizeDelta = new Vector2(500f, 500f);
        var types = new GameObject("Types", typeof(RectTransform));
        types.transform.SetParent(aura.transform, false);
        // Production can use a zero-sized Buy/Sell group with its actual drawing
        // nested below it. The old test put Image on Buy and missed the headset oval.
        var buy = new GameObject("Buy", typeof(RectTransform));
        buy.transform.SetParent(types.transform, false);
        foreach (RectTransform stretch in new[] { (RectTransform)types.transform })
        { stretch.anchorMin = Vector2.zero; stretch.anchorMax = Vector2.one; stretch.sizeDelta = Vector2.zero; }
        var ink = new GameObject("Visible cyan ring", typeof(RectTransform), typeof(Image));
        ink.transform.SetParent(buy.transform, false);
        ((RectTransform)ink.transform).sizeDelta = new Vector2(500f, 500f);
        var auraRect = (RectTransform)aura.transform;
        auraRect.localScale = new Vector3(.36f, 1.42f, 1f);
        var auraImage = ink.GetComponent<Image>();
        auraImage.raycastTarget = true;
        var nativeFrame = new GameObject("GUI_LevelUp_Frame", typeof(RectTransform), typeof(Image));
        nativeFrame.transform.SetParent(root.transform, false);
        var rect = (RectTransform)nativeFrame.transform;
        rect.sizeDelta = new Vector2(100f, 100f);
        rect.localScale = new Vector3(.02f, 1f, 1f); // captured squeezed flat animation
        rect.GetComponent<Image>().raycastTarget = true;
        var area = new GameObject("Native enhancement area", typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(UIEnhancementButtonHighlight));
        area.transform.SetParent(root.transform, false);
        var areaImage = area.GetComponent<Image>();
        areaImage.raycastTarget = true;
        ((RectTransform)area.transform).sizeDelta = new Vector2(150f, 80f);
        var card = new GameObject("Native print", typeof(RectTransform), typeof(AbilityCardUI), typeof(Image));
        card.transform.SetParent(root.transform, false);
        var mask = card.AddComponent<TownServiceNativeEnhancementCardMask>();
        mask.Mask();
        Check(TownServiceNativeEnhancementCardMask.TryAuraCaptureBounds((RectTransform)root.transform,
                out Rect initialCapture) && initialCapture.width > 325f,
            "native aura reserves its full width beyond CardHilight's narrow host for panel capture");
        Check(rect.sizeDelta == Vector2.zero && Mathf.Abs(rect.localScale.x - 1f) < .001f
            && !rect.GetComponent<Image>().raycastTarget,
            "world-space full-card frame occupies the card rather than a squeezed vertical strip and cannot steal native clicks");
        Check(!auraImage.raycastTarget && areaImage.raycastTarget,
            "original aura cannot intercept a grip-held fingertip or laser press on the native enhancement area");
        var events = new GameObject("EventSystem", typeof(EventSystem));
        int nativeClicks = 0;
        area.GetComponent<Button>().onClick.AddListener(() => nativeClicks++);
        Canvas.ForceUpdateCanvases();
        var pointer = new PointerEventData(events.GetComponent<EventSystem>())
        { position = RectTransformUtility.WorldToScreenPoint(null, area.transform.position), button = PointerEventData.InputButton.Left };
        bool areaHit = areaImage.Raycast(pointer.position, null);
        Check(areaHit && areaImage.raycastTarget && !auraImage.raycastTarget,
            "grip-held fingertip projection reaches the original native ability button while aura pixels do not claim input");
        ExecuteEvents.Execute(area, pointer, ExecuteEvents.pointerClickHandler);
        Check(nativeClicks == 1, "physical area press follows the original native button callback exactly once");
        float firstPulse = Diameter((RectTransform)ink.transform);
        Check(SquareInWorld((RectTransform)ink.transform)
            && Mathf.Abs(firstPulse - Mathf.Sqrt(500f * 500f * .36f * 1.42f) * 1.08f) < 1f,
            "actual submitted enchantress aura ink is round and preserves the native pulse size");
        rect.localScale = new Vector3(.02f, 1f, 1f); // native animation rewrites X after the initial mask
        auraRect.localScale = new Vector3(.13f, 1.52f, 1f); // native effects can animate later than Ritual.Tick
        mask.SendMessage("LateUpdate");
        Check(Mathf.Abs(rect.localScale.x - 1f) < .001f,
            "native flat animation cannot resquash the physical frame before render");
        mask.SendMessage("OnBeforeCanvasRender");
        float secondPulse = Diameter((RectTransform)ink.transform);
        Check(SquareInWorld((RectTransform)ink.transform)
            && Mathf.Abs(secondPulse - Mathf.Sqrt(500f * 500f * .13f * 1.52f) * 1.08f) < 1f
            && secondPulse < firstPulse,
            "render boundary corrects late native aura animation while retaining its pulse amplitude");
        Check(TownServiceNativeEnhancementCardMask.TryAuraCaptureBounds((RectTransform)root.transform,
                out Rect settledCapture) && settledCapture.width >= initialCapture.width - .01f,
            "later smaller pulse cannot collapse the reserved capture frame across the cyan ring");
        ink.GetComponent<Image>().enabled = false;
        Check(TownServiceNativeEnhancementCardMask.TryAuraCaptureBounds((RectTransform)root.transform,
                out Rect culledCapture) && culledCapture.width >= settledCapture.width - .01f,
            "transient native graphic absence keeps the offered ring's capture width");
        ink.GetComponent<Image>().enabled = true;
        mask.SendMessage("OnBeforeCanvasRender");
        Check(Mathf.Abs(Diameter((RectTransform)ink.transform) - secondPulse) < .01f,
            "repeated canvas submissions do not inflate the native aura without a new animation write");
        // The headset video varies the card/head angle. The old correction measured
        // this rotated child but scaled Aura's different parent axes, so the same
        // native ring became a narrow oval as it turned. Exercise real Unity
        // RectTransform world corners at several child + card rotations.
        var shearingParent = new GameObject("Outer nonuniform parent", typeof(RectTransform));
        shearingParent.transform.SetParent(root.transform, false);
        shearingParent.transform.localScale = new Vector3(1.22f, .78f, 1f);
        var scaledParent = new GameObject("Rotated nonuniform parent", typeof(RectTransform));
        scaledParent.transform.SetParent(shearingParent.transform, false);
        scaledParent.transform.localRotation = Quaternion.Euler(0f, 0f, 29f);
        scaledParent.transform.localScale = new Vector3(1.4f, .65f, 1f);
        aura.transform.SetParent(scaledParent.transform, false);
        foreach (float angle in new[] { 0f, 24f, 47f, 81f, 135f })
        {
            auraRect.localScale = new Vector3(.31f, 1.65f, 1f);
            auraRect.localRotation = Quaternion.Euler(0f, 0f, angle * .33f);
            ink.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            root.transform.rotation = Quaternion.Euler(17f, angle * .47f, -12f);
            mask.SendMessage("OnBeforeCanvasRender");
            Check(CircleInWorld((RectTransform)ink.transform),
                "rotated native ink remains a world-space circle around the physical card");
            float rotatedDiameter = Diameter((RectTransform)ink.transform);
            mask.SendMessage("OnBeforeCanvasRender");
            Check(CircleInWorld((RectTransform)ink.transform)
                && Mathf.Abs(Diameter((RectTransform)ink.transform) - rotatedDiameter) < .001f,
                "repeated render callbacks do not change a sheared-parent ring diameter");
        }
        mask.Restore();
        Check(!TownServiceNativeEnhancementCardMask.TryAuraCaptureBounds((RectTransform)root.transform,
                out _), "capture reservation ends with the physical offer");
        Check(rect.sizeDelta == new Vector2(100f, 100f) && Mathf.Abs(rect.localScale.x - .02f) < .001f
            && rect.GetComponent<Image>().raycastTarget,
            "native frame transform and input return to their original flat state");
        Check(auraImage.raycastTarget && areaImage.raycastTarget
            && Mathf.Abs(auraRect.localScale.x - .36f) < .001f
            && Mathf.Abs(auraRect.localScale.y - 1.42f) < .001f,
            "original aura input and transform return to native ownership when the card leaves the palm");
        UnityEngine.Object.DestroyImmediate(events);
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static bool SquareInWorld(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float width = Vector3.Distance(corners[0], corners[3]);
        float height = Vector3.Distance(corners[0], corners[1]);
        return width > .001f && Mathf.Abs(width / height - 1f) < .01f;
    }

    private static bool CircleInWorld(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector3 right = corners[3] - corners[0], up = corners[1] - corners[0];
        return right.magnitude > .001f
            && Mathf.Abs(right.magnitude / up.magnitude - 1f) < .01f
            && Mathf.Abs(Vector3.Dot(right.normalized, up.normalized)) < .01f;
    }

    private static bool DiameterAtLeast(RectTransform ink, RectTransform card, float fraction)
    {
        var ring = new Vector3[4]; var face = new Vector3[4];
        ink.GetWorldCorners(ring); card.GetWorldCorners(face);
        return Vector3.Distance(ring[0], ring[1]) >= Vector3.Distance(face[0], face[1]) * fraction;
    }

    private static float Diameter(RectTransform ink)
    {
        var corners = new Vector3[4]; ink.GetWorldCorners(corners);
        return Vector3.Distance(corners[0], corners[1]);
    }

    private static void PhysicalCardAura()
    {
        var fixture = new GameObject("Physical aura fixture");
        var native = new GameObject("Native", typeof(UIWindow), typeof(UINewEnhancementWindow));
        native.transform.SetParent(fixture.transform, false);
        var shop = native.GetComponent<UINewEnhancementWindow>();
        var highlighter = new GameObject("CardHilight", typeof(RectTransform),
            typeof(UIEnhancementCardHighlighter));
        highlighter.transform.SetParent(native.transform, false);
        ((RectTransform)highlighter.transform).sizeDelta = new Vector2(325f, 450f);
        highlighter.transform.localScale = Vector3.one * .0004f;
        shop.cardHolder = highlighter.GetComponent<UIEnhancementCardHighlighter>();
        var aura = new GameObject("Aura", typeof(RectTransform)); aura.transform.SetParent(highlighter.transform, false);
        var types = new GameObject("Types", typeof(RectTransform)); types.transform.SetParent(aura.transform, false);
        var buy = new GameObject("Buy", typeof(RectTransform)); buy.transform.SetParent(types.transform, false);
        var ink = new GameObject("Actual effect ink", typeof(RectTransform), typeof(Image));
        ink.transform.SetParent(buy.transform, false);
        ((RectTransform)ink.transform).sizeDelta = new Vector2(300f, 700f);
        var printed = new GameObject("Original printed card", typeof(RectTransform), typeof(AbilityCardUI));
        printed.transform.SetParent(highlighter.transform, false);
        var station = new GameObject("Resident").transform; station.SetParent(fixture.transform, false);
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station, false);
        var fan = new GameObject("Fan").transform; fan.SetParent(fixture.transform, false);
        CardsDriver.FanRoot = fan;
        var card = new GameObject("Physical card", typeof(VRCard)).GetComponent<VRCard>();
        card.transform.SetParent(fan, false); card.Owner = shop.character; card.Model.ID = 573;
        CardsDriver.OffScenarioFanCards = new[] { card };
        var slot = new GameObject("Native slot", typeof(RectTransform), typeof(Button), typeof(UIEnhanceCardSlot))
            .GetComponent<UIEnhanceCardSlot>();
        slot.transform.SetParent(native.transform, false); slot.Selectable = slot.GetComponent<Button>();
        slot.AbilityCard = printed.GetComponent<AbilityCardUI>(); slot.AbilityCard.AbilityCard = card.Model;
        slot.Selected = () => { shop.selectedCard = slot.AbilityCard; shop.cardHolder.Card = slot.AbilityCard; };
        shop.CardsDisplay.slotsPool.Add(slot);
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => true, () => true))
        {
            handoff.Tick(); card.transform.position = handoff.Seat.position;
            Check(TownServiceEnhancementHandoff.TryOffer(card), "original card can be offered for physical aura test");
            var mask = printed.GetComponent<TownServiceNativeEnhancementCardMask>();
            Check(mask != null, "same-frame handoff hides only duplicate art and reacts to the accepted offer");
            mask.SendMessage("OnBeforeCanvasRender");
            var corners = new Vector3[4]; ((RectTransform)ink.transform).GetWorldCorners(corners);
            float diameter = Vector3.Distance(corners[0], corners[1]);
            float physical = CardsConfig.CardHeight * card.transform.lossyScale.x;
            Check(diameter >= physical * 1.03f && diameter <= physical * 1.13f
                && SquareInWorld((RectTransform)ink.transform),
                "rendered native aura follows the actual physical card, not the 325x450 highlighter root");
            mask.SendMessage("OnBeforeCanvasRender");
            Check(Mathf.Abs(Diameter((RectTransform)ink.transform) - diameter) < .0001f,
                "physical-card mapping does not grow on repeated canvas submissions");
            ((RectTransform)aura.transform).localScale = new Vector3(.7f, .7f, 1f);
            mask.SendMessage("OnBeforeCanvasRender");
            Check(SquareInWorld((RectTransform)ink.transform)
                && Mathf.Abs(Diameter((RectTransform)ink.transform) - diameter * .7f) < .002f,
                "later native aura pulse remains visible at the physical card's scale");
        }
        UnityEngine.Object.DestroyImmediate(fixture);
    }

    private static void NativePhysicalPoke()
    {
        var cameraGo = new GameObject("Head camera", typeof(Camera));
        var camera = cameraGo.GetComponent<Camera>();
        cameraGo.transform.position = new Vector3(0f, 0f, -2f);
        cameraGo.transform.rotation = Quaternion.identity;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 5f;
        var events = new GameObject("EventSystem", typeof(EventSystem));
        var root = new GameObject("Native palm card", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(UIEnhancementCardHighlighter));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        ((RectTransform)root.transform).sizeDelta = new Vector2(.4f, .5f);
        var aura = new GameObject("Aura", typeof(RectTransform), typeof(Image));
        aura.transform.SetParent(root.transform, false);
        ((RectTransform)aura.transform).sizeDelta = new Vector2(.25f, .25f);
        aura.GetComponent<Image>().raycastTarget = true;
        var area = new GameObject("Original ability area", typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(UIEnhancementButtonHighlight));
        area.transform.SetParent(root.transform, false);
        ((RectTransform)area.transform).sizeDelta = new Vector2(.18f, .1f);
        area.GetComponent<Image>().raycastTarget = true;
        var card = new GameObject("Native print", typeof(RectTransform), typeof(AbilityCardUI));
        card.transform.SetParent(root.transform, false);
        var mask = card.AddComponent<TownServiceNativeEnhancementCardMask>();
        mask.Mask();
        Check(root.GetComponent<GraphicRaycaster>().ignoreReversedGraphics,
            "native card uses its original raycaster winding rule while masked");
        var tip = new GameObject("Tracked index tip");
        var hand = new VRHand();
        hand.Rig.IndexTip = tip.transform;
        int nativeClicks = 0;
        area.GetComponent<Button>().onClick.AddListener(() => nativeClicks++);
        var poke = new PokeInteractor(hand);
        UguiPokeSurfaces.Surfaces.Add(canvas);
        var physical = new GameObject("Physical offered card", typeof(BoxCollider));
        physical.transform.position = new Vector3(0f, 0f, -.025f);
        physical.GetComponent<BoxCollider>().size = new Vector3(.3f, .4f, .006f);
        try
        {
            Canvas.ForceUpdateCanvases();
            Physics.SyncTransforms();
            Ray laser = new(camera.transform.position, Vector3.forward);
            var cardHit = physical.GetComponent<BoxCollider>().Raycast(laser, out RaycastHit cardPoint, 5f);
            Vector2 uiPoint = RectTransformUtility.WorldToScreenPoint(camera, area.transform.position);
            // NullGfxDevice under -nographics yields no GraphicRaycaster output;
            // Image.Raycast still exercises the original Unity Graphic geometry.
            Check(cardHit && cardPoint.distance < Vector3.Distance(camera.transform.position, root.transform.position)
                && area.GetComponent<Image>().raycastTarget
                && area.GetComponent<Image>().Raycast(uiPoint, camera),
                "original enhancement area remains laser-raycastable behind its physical card collider"
                    + " (cardHit=" + cardHit + " cardDist=" + cardPoint.distance + ")");
            tip.transform.position = new Vector3(0f, 0f, -.03f);
            poke.Tick();
            tip.transform.position = new Vector3(0f, 0f, -.003f);
            poke.Tick();
            tip.transform.position = new Vector3(0f, 0f, .014f);
            poke.Tick();
            Check(nativeClicks == 0, "physical fingertip without grip cannot select the native ability area");
            tip.transform.position = new Vector3(0f, 0f, -.03f);
            hand.GripPressed = true;
            poke.Tick();
            tip.transform.position = new Vector3(0f, 0f, -.003f);
            poke.Tick();
            tip.transform.position = new Vector3(0f, 0f, .014f);
            poke.Tick();
            Check(nativeClicks == 1,
                "production fingertip plane/depth/grip route clicks the original ability Button once through the masked aura");
            poke.Tick();
            Check(nativeClicks == 1, "one continuous grip press cannot duplicate the native area callback");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // Use the unmodified canvas and its normal GraphicRaycaster
                // winding rule. A synthetic reversed canvas is not evidence
                // about the game's converted card orientation.
                camera.Render();
                Canvas.ForceUpdateCanvases();
                var uiHits = new System.Collections.Generic.List<RaycastResult>();
                root.GetComponent<GraphicRaycaster>().Raycast(
                    new PointerEventData(events.GetComponent<EventSystem>()) { position = uiPoint }, uiHits);
                Check(uiHits.Count > 0 && uiHits[0].gameObject == area,
                    "rendered GraphicRaycaster chooses original ability area behind offered card"
                    + " (hits=" + uiHits.Count + " top=" + (uiHits.Count > 0 ? uiHits[0].gameObject.name : "none")
                    + " graphics=" + GraphicRegistry.GetGraphicsForCanvas(canvas).Count
                    + " depth=" + area.GetComponent<Image>().depth
                    + " reversed=" + root.GetComponent<GraphicRaycaster>().ignoreReversedGraphics
                    + " camera=" + canvas.worldCamera + " screen=" + uiPoint + ")");
            }
        }
        finally
        {
            UguiPokeSurfaces.Surfaces.Clear();
            poke.CancelAll();
            mask.Restore();
            Check(root.GetComponent<GraphicRaycaster>().ignoreReversedGraphics,
                "native card raycaster winding remains untouched after release");
            UnityEngine.Object.DestroyImmediate(tip);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(physical);
            UnityEngine.Object.DestroyImmediate(events);
            UnityEngine.Object.DestroyImmediate(cameraGo);
        }
    }

    private static void NativeLaserOcclusion()
    {
        const float panel = 2f, card = 1.90f, epsilon = .005f;
        float none = float.PositiveInfinity;
        Check(Mathf.Abs(NativeVisualDistanceFixture.ResolveVisualHitDistance(
                none, none, card, 5f) - card) < .0001f,
            "laser ends at offered physical card even when the game physics ray mask excludes it");
        Check(Mathf.Abs(NativeVisualDistanceFixture.ResolveVisualHitDistance(
                panel, none, card, 5f) - card) < .0001f,
            "native enhancement area can receive the pointer while its visible laser ends on the physical card");
        Check(Mathf.Abs(NativeVisualDistanceFixture.ResolveVisualHitDistance(
                panel + 1f, none, card, 5f) - card) < .0001f,
            "a fresh or one-frame-old background UI beam cannot extend past a nearer offered card");
        Check(Mathf.Abs(NativeVisualDistanceFixture.ResolveVisualHitDistance(
                panel, none, none, 5f) - panel) < .0001f,
            "ordinary unobstructed UI beam still ends on its original panel");
        Check(Mathf.Abs(NativeVisualDistanceFixture.ResolveVisualHitDistance(
                none, 1.5f, card, 5f) - 1.5f) < .0001f,
            "an unrelated nearer physics surface still stops the beam before the offered card");
        Check(Mathf.Abs(NativeVisualDistanceFixture.ResolveVisualHitDistance(
                none, none, none, 5f) - 5f) < .0001f,
            "open laser remains unchanged away from solid occluders");
        var offered = new GameObject("Exact offered card", typeof(VRCard), typeof(BoxCollider));
        offered.transform.position = new Vector3(0f, 0f, -.1f);
        offered.GetComponent<BoxCollider>().size = new Vector3(.4f, .5f, .02f);
        var foreign = new GameObject("Foreign collider behind card", typeof(BoxCollider));
        foreign.transform.position = new Vector3(0f, 0f, -.04f);
        foreign.GetComponent<BoxCollider>().size = new Vector3(.4f, .5f, .02f);
        Physics.SyncTransforms();
        Ray ray = new(new Vector3(0f, 0f, -2f), Vector3.forward);
        float blocked = NativePhysicsOcclusionFixture.OtherPhysicsOccludingDistance(
            offered.GetComponent<VRCard>(), ray.origin, ray.direction, 2f, Physics.DefaultRaycastLayers);
        Check(blocked > 1.9f && blocked < 2f,
            "physics rescan finds a foreign collider between offered card and native area");
        UnityEngine.Object.DestroyImmediate(foreign);
        Physics.SyncTransforms();
        Check(float.IsPositiveInfinity(NativePhysicsOcclusionFixture.OtherPhysicsOccludingDistance(
            offered.GetComponent<VRCard>(), ray.origin, ray.direction, 2f, Physics.DefaultRaycastLayers)),
            "physics rescan excludes only the offered card's own collider");
        var saturation = new GameObject("Offered-card collider saturation");
        saturation.transform.SetParent(offered.transform, false);
        for (int i = 0; i < 70; i++)
        {
            var child = new GameObject("Card child collider", typeof(BoxCollider));
            child.transform.SetParent(saturation.transform, false);
            child.transform.localPosition = new Vector3(0f, 0f, .06f);
            child.GetComponent<BoxCollider>().size = new Vector3(.4f, .5f, .002f);
        }
        Physics.SyncTransforms();
        Check(NativePhysicsOcclusionFixture.OtherPhysicsOccludingDistance(
            offered.GetComponent<VRCard>(), ray.origin, ray.direction, 2f, Physics.DefaultRaycastLayers) == 0f,
            "a full reusable physics hit buffer fails closed instead of missing a later blocker");
        UnityEngine.Object.DestroyImmediate(saturation);
        Physics.SyncTransforms();
        Check(float.IsPositiveInfinity(NativePhysicsOcclusionFixture.OtherPhysicsOccludingDistance(
            offered.GetComponent<VRCard>(), ray.origin, ray.direction, 2f, Physics.DefaultRaycastLayers)),
            "reused physics scratch does not keep stale hits from a previous crowded frame");
        UnityEngine.Object.DestroyImmediate(offered);
        Check(NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, card,
            none, none, none, none, none, epsilon),
            "only the physical offered card in front of its native area allows the laser through");
        Check(NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, card,
            none, none, none, none, none, epsilon),
            "excluding the exact offered card from the second physics scan permits its original area");
        Check(!NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, card,
            none, 1.95f, none, none, none, epsilon),
            "a board behind the offered card still occludes the native button");
        Check(!NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, card,
            1.95f, none, none, none, none, epsilon),
            "a different raised fan card behind the offered card still occludes the button");
        Check(!NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, card,
            none, none, none, 1.95f, none, epsilon),
            "a second physical town prop behind the offered card still occludes the button");
        Check(!NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, card,
            none, none, 1.95f, none, none, epsilon),
            "a resident blocker behind the offered card still occludes the button");
        Check(!NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, card,
            none, none, none, none, 1.95f, epsilon),
            "an unrelated physics hit behind the offered card still occludes the button");
        Check(!NativeAreaOcclusionFixture.OfferedAreaClear(panel, card, 1.80f,
            none, none, none, none, none, epsilon),
            "a nearer solid surface cannot masquerade as the offered card");
        Check(!NativeAreaOcclusionFixture.OfferedAreaClear(panel, 2.10f, 2.10f,
            none, none, none, none, none, epsilon),
            "the offered card must actually lie between laser and native button");
    }

    private static void FirstVisitCue()
    {
        var root = new GameObject("First visit fixture");
        var native = new GameObject("Native", typeof(UIWindow), typeof(UINewEnhancementWindow));
        native.transform.SetParent(root.transform, false);
        var shop = native.GetComponent<UINewEnhancementWindow>();
        var station = new GameObject("Resident").transform; station.SetParent(root.transform, false);
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station, false);
        var card = new GameObject("Owned map card", typeof(VRCard)).GetComponent<VRCard>();
        card.transform.SetParent(root.transform, false); card.Owner = shop.character; card.Model.ID = 412;
        CardsDriver.OffScenarioFanCards = new[] { card };
        var slot = new GameObject("Native slot", typeof(RectTransform), typeof(Button), typeof(UIEnhanceCardSlot))
            .GetComponent<UIEnhanceCardSlot>();
        slot.transform.SetParent(native.transform, false); slot.Selectable = slot.GetComponent<Button>();
        slot.AbilityCard = new GameObject("Native card", typeof(AbilityCardUI)).GetComponent<AbilityCardUI>();
        slot.AbilityCard.AbilityCard = card.Model;
        slot.Selected = () => shop.selectedCard = slot.AbilityCard;
        shop.CardsDisplay.slotsPool.Add(slot);
        var hand = new VRHand(); VRHands.Left = hand; VRHands.Right = null;
        hand.Grabber.Held = card; card.IsHeld = true;
        bool input = false;
        TownServicePresentation.SessionAge = .08f;
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => true, () => input))
        {
            handoff.Tick(); card.transform.position = handoff.Seat.position; handoff.Tick();
            CanvasGroup gate = handoff.Zone.GetComponent<CanvasGroup>();
            Check(gate.alpha > .3f && gate.alpha < .5f && hand.HoverTicks == 0 && hand.ClickPulses == 0,
                "first opening shows neutral palm locator while native input remains blocked");
            TownServicePresentation.SessionAge = 1.4f;
            handoff.Tick();
            Check(gate.alpha > .3f && gate.alpha < .5f,
                "slow relocation keeps a visible noninteractive palm locator past the old 350 ms cutoff");
            card.IsHeld = false; hand.Grabber.Held = null;
            Check(!TownServiceEnhancementHandoff.TryOffer(card) && handoff.Card == null,
                "first opening preview cannot commit native card selection");
            card.IsHeld = true; hand.Grabber.Held = card;
            input = true; handoff.Tick();
            Check(gate.alpha == 1f && hand.HoverTicks == 1 && hand.ClickPulses == 1,
                "ready first visit turns the same locator into a haptic snap target");
            slot.Selectable.interactable = false; handoff.Tick();
            Check(gate.alpha > .3f && gate.alpha < .5f && !TownServiceEnhancementHandoff.TryOffer(card),
                "native row refresh retains a visible locator without pretending the disabled slot accepts a card");
            slot.Selectable.interactable = true; handoff.Tick();
            Check(gate.alpha == 1f,
                "restored native row resumes the actionable overlay on the same visit");
            CardsDriver.OffScenarioFanCards = Array.Empty<VRCard>(); handoff.Tick();
            Check(gate.alpha == 1f,
                "held owned card remains offerable when the visible fan no longer lists its plucked card");
        }
        CardsDriver.OffScenarioFanCards = new[] { card };
        TownServicePresentation.SessionAge = 3f;
        using (var revisit = new TownServiceEnhancementHandoff(shop, station, () => true, () => true))
        { revisit.Tick(); Check(revisit.Zone.GetComponent<CanvasGroup>().alpha == 1f,
            "later visit is ready without inheriting a stale preview latch"); }
        VRHands.Left = null; card.IsHeld = false;
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void OfferFeedback()
    {
        var root = new GameObject("Offer cue", typeof(RectTransform), typeof(CanvasGroup));
        var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(root.transform, false);
        var cue = new TownServiceOfferFeedback(root.GetComponent<CanvasGroup>(), root.transform);
        var hand = new VRHand();
        cue.Tick(false, hand, 0f, true, .43f); cue.Paint(false);
        Check(hand.HoverTicks == 0 && hand.ClickPulses == 0 && root.GetComponent<CanvasGroup>().alpha == 0f,
            "blocked native offer has no visual or haptic preview");
        cue.Tick(true, hand, .7f, false, .43f); cue.Paint(true);
        Check(root.GetComponent<CanvasGroup>().alpha > .99f && hand.HoverTicks == 0,
            "valid empty handoff target is visible before card approach");
        cue.Tick(true, hand, .30f, false, .43f);
        cue.Tick(true, hand, .22f, false, .43f);
        Check(hand.HoverTicks == 1 && hand.ClickPulses == 0,
            "approach has one debounced hover pulse");
        cue.Tick(true, hand, .10f, true, .43f);
        cue.Tick(true, hand, .08f, true, .43f);
        Check(hand.HoverTicks == 1 && hand.ClickPulses == 1,
            "native accept volume has one debounced snap pulse");
        var secondHand = new VRHand();
        cue.Tick(true, secondHand, .09f, true, .43f);
        Check(secondHand.HoverTicks == 1 && secondHand.ClickPulses == 1,
            "a different controller receives its own approach and snap edge");
        cue.Tick(false, hand, .08f, true, .43f); cue.Paint(false);
        Check(root.GetComponent<CanvasGroup>().alpha == 0f && hand.ClickPulses == 1,
            "native disablement clears an apparently actionable target");
        var bowl = new TownServiceOfferFeedback();
        var purseHand = new VRHand();
        bowl.Tick(true, purseHand, .20f, false, .28f, snapPulse: false);
        bowl.Tick(true, purseHand, .05f, true, .28f, snapPulse: false);
        Check(purseHand.HoverTicks == 1 && purseHand.ClickPulses == 0,
            "priestess approach adds one hover pulse without duplicating the purse token's inside-bowl snap pulse");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void SwapOffering()
    {
        var root = new GameObject("Swap fixture");
        var native = new GameObject("Native", typeof(UIWindow), typeof(UINewEnhancementWindow));
        native.transform.SetParent(root.transform, false);
        var shop = native.GetComponent<UINewEnhancementWindow>();
        var station = new GameObject("Resident").transform; station.SetParent(root.transform, false);
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station, false);
        palm.localPosition = new Vector3(-.18f, 1.14f, .23f);
        var fan = new GameObject("Fan").transform; fan.SetParent(root.transform, false); CardsDriver.FanRoot = fan;
        VRCard Card(int id)
        {
            var card = new GameObject("Card " + id, typeof(VRCard)).GetComponent<VRCard>();
            card.transform.SetParent(fan, false); card.Owner = shop.character; card.Model.ID = id;
            new GameObject("Full", typeof(FullAbilityCard)).transform.SetParent(card.transform, false);
            return card;
        }
        UIEnhanceCardSlot Slot(VRCard card)
        {
            var slot = new GameObject("Slot " + card.Model.ID, typeof(RectTransform), typeof(Button), typeof(UIEnhanceCardSlot)).GetComponent<UIEnhanceCardSlot>();
            slot.transform.SetParent(native.transform, false); slot.Selectable = slot.GetComponent<Button>();
            slot.AbilityCard = new GameObject("Original " + card.Model.ID, typeof(AbilityCardUI)).GetComponent<AbilityCardUI>();
            slot.AbilityCard.transform.SetParent(slot.transform, false); slot.AbilityCard.AbilityCard = card.Model;
            slot.AbilityCard.fullAbilityCard = new GameObject("Full", typeof(FullAbilityCard)).GetComponent<FullAbilityCard>();
            slot.Selected = () => shop.selectedCard = slot.AbilityCard;
            shop.CardsDisplay.slotsPool.Add(slot); return slot;
        }
        VRCard first = Card(301), replacement = Card(302), rejected = Card(303);
        Slot(first); Slot(replacement); UIEnhanceCardSlot rejectedSlot = Slot(rejected);
        rejectedSlot.Selected = () => { shop.selectedCard = rejectedSlot.AbilityCard; throw new Exception("replacement rejected"); };
        CardsDriver.OffScenarioFanCards = new[] { first, replacement, rejected };
        CardsDriver.Returned = CardsDriver.Rebuilds = 0; CardsDriver.LastReturned = null;
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => true, () => true))
        {
            handoff.Tick(); first.transform.position = handoff.Seat.position;
            Check(TownServiceEnhancementHandoff.TryOffer(first), "first valid owned card occupies enchantress palm");
            rejected.transform.position = handoff.Seat.position;
            Check(!TownServiceEnhancementHandoff.TryOffer(rejected) && ReferenceEquals(handoff.Card, first)
                && CardsDriver.Returned == 0 && ReferenceEquals(shop.selectedCard!.AbilityCard, first.Model),
                "rejected replacement preserves prior enchantress card atomically");
            replacement.transform.position = handoff.Seat.position;
            Check(TownServiceEnhancementHandoff.TryOffer(replacement), "second valid owned card atomically swaps into enchantress palm");
            Check(ReferenceEquals(handoff.Card, replacement) && ReferenceEquals(shop.selectedCard!.AbilityCard, replacement.Model),
                "replacement owns both physical and native enchantment selection");
            Check(CardsDriver.Returned == 1 && ReferenceEquals(CardsDriver.LastReturned, first) && first.IsFlying,
                "displaced enchantment card takes canonical fan return flight");
            Check(TownServiceEnhancementHandoff.IsParked(first) && TownServiceEnhancementHandoff.IsParked(replacement),
                "returning old card and parked replacement remain uniquely published");
            Check(!TownServiceEnhancementHandoff.TryOffer(replacement), "same parked card cannot replace itself");
            CardsDriver.Complete();
            Check(!TownServiceEnhancementHandoff.IsParked(first) && TownServiceEnhancementHandoff.IsParked(replacement),
                "displaced card retires only after its canonical return completes");
        }
        CardsDriver.Complete();
        UnityEngine.Object.DestroyImmediate(root);
    }
    private static void RunCase(float scale, int scenario)
    {
        var root = new GameObject("Fixture"); root.transform.localScale = Vector3.one * scale;
        var native = new GameObject("Native", typeof(UIWindow), typeof(UINewEnhancementWindow)); native.transform.SetParent(root.transform, false);
        var shop = native.GetComponent<UINewEnhancementWindow>();
        var station = new GameObject("Resident").transform; station.SetParent(root.transform, false);
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station, false); palm.localPosition = new Vector3(-.18f, 1.14f, .23f);
        TownServicePopulation.Station = new TownServiceStation { Root = station };
        var fan = new GameObject("Fan").transform; fan.SetParent(root.transform, false); CardsDriver.FanRoot = fan;
        var card = new GameObject("ActualHandCard", typeof(VRCard)).GetComponent<VRCard>(); card.transform.SetParent(fan, false);
        card.transform.position = palm.position; card.Owner = shop.character;
        card.Model.ID = 123;
        var face = new GameObject("Full", typeof(FullAbilityCard)).GetComponent<FullAbilityCard>(); face.transform.SetParent(card.transform, false);
        new GameObject("Top").transform.SetParent(face.transform, false);
        var secondFaceTop = new GameObject("Top").transform; secondFaceTop.SetParent(face.transform, false);
        var slot = new GameObject("OriginalSlot", typeof(RectTransform), typeof(Button), typeof(UIEnhanceCardSlot)).GetComponent<UIEnhanceCardSlot>();
        slot.transform.SetParent(native.transform, false); slot.Selectable = slot.GetComponent<Button>();
        slot.AbilityCard = new GameObject("OriginalCard", typeof(AbilityCardUI)).GetComponent<AbilityCardUI>();
        slot.AbilityCard.transform.SetParent(slot.transform, false); slot.AbilityCard.AbilityCard = card.Model;
        if (scenario == 21) slot.AbilityCard.AbilityCard = new ScenarioRuleLibrary.CAbilityCard { ID = card.Model.ID };
        slot.AbilityCard.fullAbilityCard = new GameObject("Full", typeof(FullAbilityCard)).GetComponent<FullAbilityCard>();
        slot.AbilityCard.fullAbilityCard.transform.SetParent(slot.AbilityCard.transform, false);
        var originalTop = new GameObject("Top").transform; originalTop.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        var secondOriginalTop = new GameObject("Top").transform; secondOriginalTop.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        var nativePrint = new GameObject("Native print", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        nativePrint.transform.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        var nativeArea = new GameObject("Native enhancement area", typeof(RectTransform),
            typeof(Image), typeof(UIEnhancementButtonHighlight)).GetComponent<Image>();
        nativeArea.transform.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        shop.CardsDisplay.slotsPool.Add(slot);
        bool alive = true, input = true; int selected = 0;
        int priorOfferLines = TownServiceVoice.Inspections;
        slot.Selected = () =>
        {
            selected++; shop.selectedCard = slot.AbilityCard; shop.cardHolder.Card = slot.AbilityCard;
            if (scenario == 5) shop.character = new Owner { CharacterID = "foreign" };
            if (scenario == 6) slot.AbilityCard.AbilityCard = new ScenarioRuleLibrary.CAbilityCard();
            if (scenario == 7) throw new Exception("native callback failed");
        };
        CardsDriver.Returned = CardsDriver.Rebuilds = 0; CardsDriver.LastReturned = null;
        VRRigDriver.HeadCamera = null; VRHands.Left = VRHands.Right = null;
        VRHands.Primary = scenario == 20 ? new VRHand { WorldScale = 3f * scale } : null;
        MapRoomDriver.Active = true;
        CardsDriver.OffScenarioFanCards = new[] { card };
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => alive, () => input))
        {
            handoff.Tick();
            Check(handoff.Zone.GetComponent<CanvasGroup>().alpha == 1f && !card.IsHeld,
                scenario == 21 ? "same owned card ID survives a native enhancement-list model refresh"
                    : "empty ready palm advertises an owned offering without requiring a held card");
            if (scenario == 1) shop.character = new Owner { CharacterID = "foreign" };
            if (scenario == 2) card.transform.position = palm.TransformPoint(new Vector3(0f, 0f, 1f));
            if (scenario == 3) slot.Selectable.interactable = false;
            if (scenario == 4) card.Owned = false;
            if (scenario == 8) shop._isConfirmationBoxOpened = true;
            if (scenario == 9) card.IsHeld = true;
            if (scenario == 1 || scenario == 3 || scenario == 4 || scenario == 8)
            {
                handoff.Tick();
                float alpha = handoff.Zone.GetComponent<CanvasGroup>().alpha;
                Check(scenario == 3 ? alpha > .3f && alpha < .5f : alpha == 0f,
                    "disabled owned row shows only the waiting preview; foreign or confirmation cards show no drop cue");
            }
            bool offered = TownServiceEnhancementHandoff.TryOffer(card);
            string reason = scenario == 1 ? "foreign native character refuses offering"
                : scenario == 2 ? "distant release refuses offering"
                : scenario == 3 ? "disabled native slot refuses offering"
                : scenario == 5 ? "native callback owner race refuses offering" : "invalid offering is refused";
            Check(offered == (scenario == 0 || scenario >= 10), reason);
            if (offered)
            {
                if (scenario == 21) Check(!ReferenceEquals(card.Model, shop.selectedCard!.AbilityCard),
                    "same owned card ID survives a native enhancement-list model refresh");
                Check(selected == 1 && ReferenceEquals(handoff.Card, card), "one native selection parks the original card");
                var palmCanvas = new GameObject("Palm canvas", typeof(Canvas)).GetComponent<Canvas>();
                var wrongCanvas = new GameObject("Other canvas", typeof(Canvas)).GetComponent<Canvas>();
                var area = nativeArea.GetComponent<UIEnhancementButtonHighlight>();
                area.Ability = new object();
                TownServicePresentation.Ritual = new TownServiceRitual { Handoff = handoff };
                TownServicePresentation.Ritual.Surfaces.Add(new TownServiceSurface
                    { Id = 11, Panel = new ConvertedPanel { HostCanvas = palmCanvas } });
                Check(TownServiceEnhancementHandoff.TryNativeArea(palmCanvas, nativeArea.gameObject, out VRCard? selectedCard)
                    && ReferenceEquals(selectedCard, card),
                    "laser over a live original ability-area button selects that native area on the offered card");
                Check(!TownServiceEnhancementHandoff.TryNativeArea(wrongCanvas, nativeArea.gameObject, out _)
                    && !TownServiceEnhancementHandoff.TryNativeArea(palmCanvas, nativePrint.gameObject, out _),
                    "unrelated canvas and non-ability card print never steal the physical reclaim trigger");
                shop._isConfirmationBoxOpened = true;
                Check(!TownServiceEnhancementHandoff.TryNativeArea(palmCanvas, nativeArea.gameObject, out _),
                    "native confirmation closes the area-selection laser gate");
                shop._isConfirmationBoxOpened = false;
                TownServicePresentation.Ritual = null;
                UnityEngine.Object.DestroyImmediate(palmCanvas.gameObject);
                UnityEngine.Object.DestroyImmediate(wrongCanvas.gameObject);
                Check(!nativePrint.enabled && nativeArea.enabled
                    && TownServiceVoice.Inspections == priorOfferLines + 1,
                    "same-frame handoff hides only duplicate art and reacts to the accepted offer");
                Check(TownServiceEnhancementHandoff.IsParked(card), "parked card excluded from fan adoption");
                Check(handoff.Face == face.transform && handoff.CloneOf(originalTop) == face.transform.Find("Top"), "actual printed face retains native provenance");
                Check(handoff.CloneOf(secondOriginalTop) == secondFaceTop, "same-named printed nodes retain distinct native provenance");
                Check(card.FullCollider && card.Grabbable && TownServiceEnhancementHandoff.CanReclaim(card), "offering remains reclaimable");
                Check(!TownServiceEnhancementHandoff.TryOffer(card), "duplicate release cannot select twice");
                palm.localPosition += new Vector3(.1f, .04f, -.02f); handoff.Tick();
                Check(Mathf.Abs((card.transform.position.y - palm.position.y) / scale - (scenario == 20 ? .405f : .17f)) < .007f
                    && (new Vector2(card.transform.position.x - palm.position.x, card.transform.position.z - palm.position.z)).magnitude < .001f * scale,
                    "physical offering floats upright above actual palm at every scale");
                Check(Vector3.Dot(card.transform.up, Vector3.up) > .999f, "offered ability card is upright over the palm");
                if (scenario == 20)
                    Check(Mathf.Abs(card.transform.lossyScale.x / scale - 3f) < .001f,"offered mage card preserves tracked reading size across independent resident scale");
                if (scenario == 10)
                {
                    var hand = new VRHand();
                    input=false; shop._isConfirmationBoxOpened=true;
                    var confirm=new GameObject("NativeRuneConfirmation",typeof(UIWindow),typeof(UIEnhancementConfirmationBox));
                    confirm.transform.SetParent(root.transform,false);
                    Singleton<UIEnhancementConfirmationBox>.Instance=confirm.GetComponent<UIEnhancementConfirmationBox>();
                    GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode=GloomhavenVR.Core.Events.VRMode.ModalUI;
                    TownServicePalmConfirmation.Owned=false;
                    Check(!hand.Grabber.ForceGrab(card,true),"unowned rune prompt retains ordinary modal grab block");
                    TownServicePalmConfirmation.Owned=true;
                    Check(hand.Grabber.ForceGrab(card,true),"actual routed grab reclaims mage card through owned native confirmation");
                    TownServicePalmConfirmation.Owned=false;
                    GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode=GloomhavenVR.Core.Events.VRMode.TableIdle;
                    Check(handoff.Card == null && shop.selectedCard == null, "manual reclaim clears native options");
                    Check(CardsDriver.Returned == 0, "manual reclaim preserves held card");
                    card.transform.SetParent(fan, true); card.IsHeld = false;
                    Check(TownServiceEnhancementHandoff.ReturnReclaimed(card) && CardsDriver.Returned == 1, "reclaimed release returns to hand rather than palm");
                    Check(!TownServiceEnhancementHandoff.ReturnReclaimed(card), "reclaimed release is one shot");
                }
                else if (scenario == 11)
                {
                    input = false; handoff.Tick();
                    Check(handoff.Card == card, "opening input fade retains offering");
                }
                else if (scenario >= 18)
                {
                    handoff.Dispose();
                    var returning = TownServiceEnhancementHandoff.Returning;
                    Check(returning.Count == 1 && returning[0].Card == card
                        && returning[0].CardId == 123 && returning[0].Face == face.transform,
                        "return presentation survives ritual disposal with actual face and fixed identity");
                    UnityEngine.Object.DestroyImmediate(slot.AbilityCard.gameObject);
                    Check(returning[0].Face == face.transform && returning[0].CardId == 123,
                        "native pool recycling cannot change return face provenance");
                    Check(TownServiceEnhancementHandoff.IsParked(card), "return flight stays excluded from static fan");
                    if (scenario == 18)
                    {
                        CardsDriver.Complete();
                        Check(TownServiceEnhancementHandoff.Returning.Count == 0 && !TownServiceEnhancementHandoff.IsParked(card),
                            "only actual flight completion retires return presentation");
                    }
                    else
                    {
                        MapRoomDriver.Active = false;
                        Check(TownServiceEnhancementHandoff.Returning.Count == 0, "map teardown clears return presentation");
                        MapRoomDriver.Active = true;
                    }
                }
                else if (scenario >= 12)
                {
                    if (scenario == 12)
                    {
                        var camera = new GameObject("Head", typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                        camera.transform.position = palm.TransformPoint(new Vector3(0f, 0f, 3f)); VRRigDriver.HeadCamera = camera;
                    }
                    if (scenario == 13) card.CurrentCharacter = false;
                    if (scenario == 14) card.InLoadout = false;
                    if (scenario == 15) alive = false;
                    if (scenario == 16) shop.selectedCard = null;
                    if (scenario == 17) slot.AbilityCard.AbilityCard = new ScenarioRuleLibrary.CAbilityCard();
                    handoff.Tick();
                    Check(handoff.Card == null && CardsDriver.Returned == 1 && CardsDriver.LastReturned == card,
                        scenario == 12 ? "walking away returns original card" : "stale owner or native selection returns original card");
                }
            }
            else
            {
                Check(handoff.Card == null && !TownServiceEnhancementHandoff.IsParked(card), "rejection never steals card ownership");
                if (scenario < 5 || scenario == 8 || scenario == 9) Check(selected == 0, reason);
            }
        }
        if (scenario == 0)
        {
            slot.AbilityCard.gameObject.SetActive(false);
            Check(nativePrint.enabled && nativeArea.enabled,
                "native pooled card restores its artwork when the game disables it");
        }
        Check(card != null, "disposing station never destroys actual map card");
        UnityEngine.Object.DestroyImmediate(root);
        TownServicePopulation.Station = null; VRRigDriver.HeadCamera = null;
    }

    private static void WalkAway()
    {
        var root=new GameObject("Empty visit",typeof(UIWindow),typeof(UINewEnhancementWindow));
        var palm=new GameObject("ActivityOfferingPalm").transform;palm.SetParent(root.transform,false);
        TownServicePopulation.Station=new TownServiceStation{Root=root.transform};
        var head=new GameObject("Head",typeof(Camera)).GetComponent<Camera>();
        VRRigDriver.HeadCamera=head; MapRoomDriver.Active=true;
        WorldUIConfig.ImmersiveTownServices.Value=true; CardsDriver.OffScenarioFanCards=null;
        using(var handoff=new TownServiceEnhancementHandoff(root.GetComponent<UINewEnhancementWindow>(),root.transform,()=>true,()=>true))
        {
            handoff.Tick();head.transform.position=Vector3.forward*3f;
            int closed=ModalFallback.Closed;
            TownServiceEnhancementHandoff.TickApproach();
            Check(ModalFallback.Closed==closed+1 && !root.GetComponent<UIWindow>().IsOpen,
                "walking away closes empty native service through its existing exit path");
            TownServiceEnhancementHandoff.TickApproach();
            Check(ModalFallback.Closed==closed+1,"closed service is not repeatedly exited");
        }
        UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head.gameObject);
        TownServicePopulation.Station=null;VRRigDriver.HeadCamera=null;
    }

    private static void Approach()
    {
        var root = new GameObject("Approach");
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(root.transform, false);
        TownServicePopulation.Station = new TownServiceStation { Root = root.transform };
        var firstSlot = new NewPartyCharacterUI();
        var selectedSlot = new NewPartyCharacterUI();
        NewPartyDisplayUI.PartyDisplay = new NewPartyDisplayUI
        { FirstSlot = firstSlot, SelectedUISlot = selectedSlot };
        MapRoomDriver.SwitchForcesFirst = true;
        var card = new GameObject("OwnedCard", typeof(VRCard)).GetComponent<VRCard>(); card.transform.SetParent(root.transform, false);
        var hand = new VRHand(); hand.Grabber.Held = card; VRHands.Left = hand;
        CardsDriver.OffScenarioFanCards = new[] { card };
        var head = new GameObject("Head", typeof(Camera)).GetComponent<Camera>(); head.transform.SetParent(root.transform, false);
        VRRigDriver.HeadCamera = head;
        void Outside()
        {
            head.transform.position = palm.position + Vector3.forward * 3f;
            card.transform.position = palm.position + Vector3.forward * 3f;
            TownServiceEnhancementHandoff.TickApproach();
        }
        void Offer() { card.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach(); }
        Outside();
        MapRoomDriver.Visits = 0; GuildmasterDestinations.Mode = EGuildmasterMode.None;
        card.Owned = false; Offer();
        Check(MapRoomDriver.Visits == 0, "foreign held card never opens native service");
        card.Owned = true; StoryComposite.PointOfNoReturn = true; Outside(); Offer();
        Check(MapRoomDriver.Visits == 0, "story commitment prevents automatic visit");
        StoryComposite.PointOfNoReturn = false; MapRoomDriver.CanVisit = false; Outside(); Offer();
        Check(MapRoomDriver.Visits == 0, "native unavailable service never opens");
        MapRoomDriver.CanVisit = true; Outside();
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 0, "distant card never opens service");
        Offer();
        Check(MapRoomDriver.Visits == 1, "owned card approach opens through original native visit");
        Check(ReferenceEquals(NewPartyDisplayUI.PartyDisplay.SelectedUISlot, selectedSlot)
            && selectedSlot.Clicks == 1,
            "native destination change preserves the visitor's exact selected character slot");
        Check(MapRoomDriver.LastSuppressed, "automatic enchantress entry suppresses the flat button sound");
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 1, "repeated approach cannot toggle native service");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
        System.Threading.Thread.Sleep(125);
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 1, "explicit close remains closed while card stays near");
        VRHands.Left = null; Outside(); head.transform.position = palm.position;
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 2, "head proximity opens original service without a held card");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 2, "explicit close remains closed while head stays near");
        head.transform.position = palm.position + Vector3.forward * 1.6f; TownServiceEnhancementHandoff.TickApproach();
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 2, "head hysteresis avoids boundary reopen");
        Outside(); head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "leaving and returning re-arms proximity greeting");
        GuildmasterDestinations.Mode = EGuildmasterMode.Merchant;
        Outside(); head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4 && GuildmasterDestinations.Mode == EGuildmasterMode.Enchantress,
            "physical enchantress approach switches a stale merchant destination through the native rail");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4,
            "closing a visit switched from merchant does not reopen it while still near");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
        TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4, "explicitly closing the deferred enchantress visit stays closed while still near");
        Outside(); MapRoomDriver.CanVisit = false;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4, "native rail briefly unavailable cannot consume the visitor's approach");
        MapRoomDriver.CanVisit = true; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4, "pending native rail retries are rate-limited between frames");
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 5, "pending approach opens when original native rail becomes ready");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 5, "explicit close clears a previously deferred approach");
        Outside(); GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode = GloomhavenVR.Core.Events.VRMode.ModalUI;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 5, "modal confirmation prevents proximity opening");
        GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode = GloomhavenVR.Core.Events.VRMode.TableIdle;
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 6,
            "transient modal or unloaded cards defer the original visit instead of consuming its proximity edge");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 6, "explicit close of a deferred modal approach remains closed");
        VRHands.Left = hand; card.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 7, "deliberately offering a card overrides an earlier proximity close");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; Outside(); VRHands.Left = null;
        CardsDriver.OffScenarioFanCards = null; head.transform.position = palm.position;
        TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 7, "head arrival before owned map cards finish building stays pending");
        CardsDriver.OffScenarioFanCards = new[] { card };
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 8, "pending visit opens after the map cards load without stepping away");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; Outside();
        head.transform.position = palm.position + Vector3.up * 1.5f;
        TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 9, "visitor attention uses the floor-plane reach even when HMD is above the palm");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; WorldUIConfig.MapRoomHand!.Value = false;
        Outside(); head.transform.position = palm.position; Offer();
        Check(!TownServiceEnhancementHandoff.Enabled && MapRoomDriver.Visits == 9, "disabled map hand prevents automatic immersive opening");
        WorldUIConfig.MapRoomHand.Value = true;
        Outside(); GuildmasterDestinations.Mode = EGuildmasterMode.Temple;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 10 && GuildmasterDestinations.Mode == EGuildmasterMode.Enchantress,
            "approaching the enchantress also switches a completed temple visit");
        Outside(); GuildmasterDestinations.Mode = EGuildmasterMode.Merchant;
        var confirmation = new GameObject("Trade confirmation", typeof(UIWindow), typeof(UIItemConfirmationBox))
            .GetComponent<UIItemConfirmationBox>();
        Singleton<UIItemConfirmationBox>.Instance = confirmation;
        confirmation.IsActive = true;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 10 && GuildmasterDestinations.Mode == EGuildmasterMode.Merchant,
            "a live merchant purchase confirmation is never interrupted by resident approach");
        confirmation.GetComponent<UIWindow>().IsOpen = false;
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 11 && GuildmasterDestinations.Mode == EGuildmasterMode.Enchantress,
            "closed merchant confirmation cannot strand a physical visit behind stale IsActive");
        Singleton<UIItemConfirmationBox>.Instance = null;
        UnityEngine.Object.DestroyImmediate(confirmation.gameObject);
        Outside(); GuildmasterDestinations.Mode = EGuildmasterMode.Trainer;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 11 && GuildmasterDestinations.Mode == EGuildmasterMode.Trainer,
            "physical enchantress approach cannot interrupt a non-service destination");
        var templeRoot = new GameObject("Temple competitor");
        templeRoot.transform.position = root.transform.position + Vector3.right * 2.65f;
        TownServicePopulation.MageStation = new TownServiceStation { Root = root.transform };
        TownServicePopulation.TempleStation = new TownServiceStation { Root = templeRoot.transform };
        Outside(); GuildmasterDestinations.Mode = EGuildmasterMode.Temple;
        head.transform.position = root.transform.position + Vector3.right * 1.35f;
        TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 11 && GuildmasterDestinations.Mode == EGuildmasterMode.Temple,
            "overlapping approach cannot switch away from the physically nearer temple");
        head.transform.position = root.transform.position + Vector3.right * 1.1f;
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 12 && GuildmasterDestinations.Mode == EGuildmasterMode.Enchantress,
            "moving closer to the enchantress switches without crossing her approach boundary again");
        GuildmasterDestinations.Mode = EGuildmasterMode.Temple;
        System.Threading.Thread.Sleep(125);
        TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 12 && GuildmasterDestinations.Mode == EGuildmasterMode.Temple,
            "a deliberate temple destination press is not undone while the visitor stands still");
        Vector3 midpoint = root.transform.position + Vector3.right * 1.3f;
        Check(TownServiceEnhancementHandoff.PrefersEnchantress(midpoint, EGuildmasterMode.Enchantress)
            && !TownServiceEnhancementHandoff.PrefersEnchantress(midpoint, EGuildmasterMode.Temple),
            "overlapping resident approach keeps the current destination inside the stable tie band");
        Check(TownServiceEnhancementHandoff.PrefersEnchantress(midpoint, EGuildmasterMode.Merchant)
            && TownServiceEnhancementHandoff.PrefersEnchantress(midpoint, EGuildmasterMode.None),
            "an unrelated destination cannot bias a nearer enchantress toward the temple");
        Check(TownServiceEnhancementHandoff.PrefersEnchantress(midpoint - Vector3.right * .2f, EGuildmasterMode.Temple)
            && !TownServiceEnhancementHandoff.PrefersEnchantress(midpoint + Vector3.right * .2f, EGuildmasterMode.Enchantress),
            "moving decisively toward either stand assigns the native destination to the nearer resident");
        UnityEngine.Object.DestroyImmediate(templeRoot);
        TownServicePopulation.MageStation = TownServicePopulation.TempleStation = null;
        UnityEngine.Object.DestroyImmediate(root); VRHands.Left = null; VRRigDriver.HeadCamera = null; TownServicePopulation.Station = null;
        NewPartyDisplayUI.PartyDisplay = null; MapRoomDriver.SwitchForcesFirst = false;
    }
}
