using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private const BindingFlags NativeFields635 = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    private static void NativeSet635(object value, string field, object data) => value.GetType().GetField(field, NativeFields635)!.SetValue(value, data);
    private static T NativeGet635<T>(object value, string field) => (T)value.GetType().GetField(field, NativeFields635)!.GetValue(value)!;
    private static void PumpNative635(UIEnchantressEffect effect, LoopAnimator loop)
    {
        NativeGet635<LTDescr>(effect, "rotationAnimation").setUseManualTime(true);
        foreach (LTDescr animation in NativeGet635<List<LTDescr>>(loop, "currentAnim")) animation.setUseManualTime(true);
        LeanTween.dtManual = 1f / 90f;
        typeof(LeanTween).GetField("frameRendered", NativeFields635)!.SetValue(null, -1);
        LeanTween.update();
    }
    private static IEnumerator NativeOverlay635()
    {
        LeanTween.reset();
        Transform owner = Go("Actual native offered-card workspace635").transform;
        var canvas = owner.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = _camera;
        var raycaster = owner.gameObject.AddComponent<GraphicRaycaster>();
        ((RectTransform)owner).sizeDelta = new Vector2(1000, 1000); owner.localScale = Vector3.one * .001f;
        Transform highlighter = NativeRow632(owner, "");
        ApplyNativeInput635(highlighter);
        Check(highlighter.name == "CardHilight" && highlighter.GetComponentsInChildren<RectTransform>(true).Length == 11,
            "actual serialized original enchantress hierarchy is loaded without replacement effect geometry");
        highlighter.gameObject.AddComponent<GloomhavenVR.WorldUI.UIEnhancementCardHighlighter>();
        RectTransform aura = (RectTransform)highlighter.Find("Aura");
        RectTransform types = (RectTransform)aura.Find("Types");
        RectTransform buy = (RectTransform)types.Find("Buy");
        RectTransform sell = (RectTransform)types.Find("Sell");
        aura.gameObject.SetActive(false);
        var loop = types.gameObject.AddComponent<LoopAnimator>();
        NativeSet635(loop, "effects", new List<AnimationSetting> { new AnimationSetting(types, TweenAction.CANVASGROUP_ALPHA, .6f, 1f, 2f, LeanTweenType.linear) });
        NativeSet635(loop, "autoStart", false); NativeSet635(loop, "ignoreTimeScale", false);
        var effect = aura.gameObject.AddComponent<UIEnchantressEffect>();
        NativeSet635(effect, "enchantressEffect", aura.gameObject); NativeSet635(effect, "rotationTime", 20f);
        NativeSet635(effect, "rotationSpeed", 1f); NativeSet635(effect, "idleAnimator", loop);
        NativeSet635(effect, "buyEffect", buy.gameObject); NativeSet635(effect, "sellEffect", sell.gameObject);
        aura.gameObject.SetActive(true); effect.ShowModeEffect(true);
        var printed = Go("Actual original pooled print"); printed.transform.SetParent(highlighter.Find("CardHolder"), false);
        var printRect = printed.GetComponent<RectTransform>(); printRect.sizeDelta = new Vector2(294, 450);
        // Native SpawnCard omits resetLocalRotation; retain an old pooled inspection pose.
        printRect.localRotation = Quaternion.Euler(9, 90, 6);
        highlighter.Find("CardHolder").GetComponent<CanvasGroup>().alpha = 1f; // Original PlaceNewCard(immediately=true) state.
        var source = printed.AddComponent<GloomhavenVR.WorldUI.AbilityCardUI>(); source.fullAbilityCard = printRect;
        var physical = Go("Actual adopted physical print frame"); physical.transform.SetParent(owner, false);
        var physicalRect = physical.GetComponent<RectTransform>(); physicalRect.sizeDelta = new Vector2(294, 450);
        physicalRect.localScale = Vector3.one * .81f;
        var paper = physical.AddComponent<Image>(); paper.color = new Color(.92f, .9f, .84f); paper.raycastTarget = false;
        TownServiceEnhancementHandoff.PhysicalCardFace = physicalRect;
        Transform originalArea = highlighter.Find("Enhancement Ability Highlight Variant");
        originalArea.gameObject.AddComponent<GloomhavenVR.WorldUI.UIEnhancementButtonHighlight>();
        originalArea.gameObject.AddComponent<Button>();
        int clicks = 0; originalArea.GetComponent<Button>().onClick.AddListener(() => clicks++);
        var areaRect = (RectTransform)originalArea;
        // The following exact native pool/Highlight transform writes are inserted
        // by the runner from the read-only shipped controller sources.
        NativePoolPlace635(originalArea, printRect);
        var mask = printed.AddComponent<TownServiceNativeEnhancementCardMask>();
        RectTransform nativeFrame = (RectTransform)highlighter.Find("GUI_LevelUp_Frame");
        Vector3 frameBefore = nativeFrame.anchoredPosition3D, auraBefore = aura.localPosition;
        mask.Mask(); effect.Play();
        var events = new GameObject("Native pointer635", typeof(EventSystem));
        var hitCorners = new Vector3[4]; var paperCorners = new Vector3[4];
        float initialTextureAngle = 0; float previousTextureAngle = 0; float accumulatedAngle = 0;
        for (int frame = 0; frame <= 1800; frame++)
        {
            // Make the offered paper yaw and tilt without moving the native clock.
            if (frame % 450 == 0)
            {
                physicalRect.localRotation = Quaternion.Euler(13, frame / 450 * 37, -9);
                physicalRect.localPosition = new Vector3(11, 0, -.01f);
                // The production surface places its converted host before the mask
                // fits the hidden native print. Preserve that real author order.
                highlighter.localRotation = physicalRect.localRotation;
                highlighter.localScale = new Vector3(1.2f, 1.2f, 1f);
            }
            PumpNative635(effect, loop);
            mask.SendMessage("OnBeforeCanvasRender"); Canvas.ForceUpdateCanvases();
            if (frame % 45 != 0) continue;
            Vector3 normal = Vector3.Cross(buy.TransformVector(Vector3.right).normalized, buy.TransformVector(Vector3.up).normalized).normalized;
            File.AppendAllText(Path.Combine(_output,"native-geometry.log"),frame+" dot="+Vector3.Dot(normal,physicalRect.forward)+" phase="+aura.localEulerAngles+" holder="+highlighter.eulerAngles+" physical="+physicalRect.eulerAngles+" normal="+normal+" expected="+physicalRect.forward+"\n");
            Check(Vector3.Dot(normal, physicalRect.forward) > .9999f,
                "actual native rotating ring remains coplanar with the physical offered print for the whole original clock");
            float width = buy.TransformVector(Vector3.right).magnitude * buy.rect.width;
            float height = buy.TransformVector(Vector3.up).magnitude * buy.rect.height;
            Check(Mathf.Abs(width / height - 1f) < .001f, "actual native serialized ring retains circular ink under card yaw and tilt");
            areaRect.GetWorldCorners(hitCorners); printRect.GetWorldCorners(paperCorners);
            for (int corner = 0; corner < 4; corner++)
                Check(Vector3.Distance(hitCorners[corner], paperCorners[corner]) < .000001f,
                    "native world-preserving pooled area matches its original target instead of retaining the flat basis");
            Vector3 edge = buy.TransformVector(Vector3.right).normalized;
            float angle = Mathf.Atan2(Vector3.Dot(edge, physicalRect.up), Vector3.Dot(edge, physicalRect.right)) * Mathf.Rad2Deg;
            if (frame == 0) initialTextureAngle = previousTextureAngle = angle;
            else
            {
                float step = Mathf.DeltaAngle(previousTextureAngle, angle);
                Check(step > 7f && step < 11f, "actual native textured ink advances continuously rather than freezing its principal axis");
                accumulatedAngle += step; previousTextureAngle = angle;
            }
            if (frame % 450 == 0)
            {
                _camera.transform.SetPositionAndRotation(physicalRect.position - physicalRect.forward * 2f, physicalRect.rotation);
                _camera.orthographicSize = .39f; _camera.aspect = 1f;
                string filename = "native-overlay-" + frame + ".png";
                RenderNativeOverlay635(owner, filename);
                CheckNativePointer635(raycaster, events.GetComponent<EventSystem>(), originalArea.gameObject);
                yield return null;
            }
        }
        Check(accumulatedAngle > 350 && accumulatedAngle < 365, "actual original twenty-second ring clock completes one continuous turn");
        Check(clicks == 5, "all rendered native pooled targets receive a real GraphicRaycaster hit and callback");
        effect.Stop(); mask.Restore(); TownServiceEnhancementHandoff.PhysicalCardFace = null;
        Check((nativeFrame.anchoredPosition3D - frameBefore).sqrMagnitude < .000001f,
            "ending the offer restores the original frame's complete anchored position including depth");
        Check((aura.localPosition - auraBefore).sqrMagnitude < .000001f,
            "ending the offer restores the original aura's complete native position");
        Object.DestroyImmediate(events); Object.DestroyImmediate(owner.gameObject);
    }
    private static void ApplyNativeInput635(Transform root)
    {
        Transform[] transforms = root.GetComponentsInChildren<RectTransform>(true).Select(value => (Transform)value).ToArray();
        using var stream = File.OpenRead(Path.Combine(Application.dataPath, "NativeFirstPicture632/native-input635.bin"));
        using var reader = new BinaryReader(stream);
        for (int count = reader.ReadUInt16(); count > 0; count--)
        {
            Transform node = transforms[reader.ReadUInt16()];
            bool hasRenderer = reader.ReadBoolean(), cull = reader.ReadBoolean();
            if (hasRenderer)
            {
                CanvasRenderer renderer = node.GetComponent<CanvasRenderer>();
                if (renderer == null) renderer = node.gameObject.AddComponent<CanvasRenderer>();
                renderer.cullTransparentMesh = cull;
            }
            Graphic[] graphics = node.GetComponents<Graphic>();
            for (int i = 0, length = reader.ReadByte(); i < length; i++) graphics[i].raycastTarget = reader.ReadBoolean();
        }
        Check(stream.Position == stream.Length, "actual serialized native transparent-mesh and ray-target grammar is consumed exactly");
    }
    private static void CheckNativePointer635(GraphicRaycaster raycaster, EventSystem events, GameObject nativeArea)
    {
        var pointer = new PointerEventData(events) { position = _camera.WorldToScreenPoint(nativeArea.transform.position), button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
        var hit = hits.FirstOrDefault(item => item.gameObject.GetComponentInParent<GloomhavenVR.WorldUI.UIEnhancementButtonHighlight>() != null);
        if (hit.gameObject == null)
        {
            File.AppendAllText(Path.Combine(_output, "native-pointer.log"), "point=" + pointer.position + " cameraRect=" + _camera.pixelRect + " allHits=" + hits.Count + "\n");
            foreach (Graphic graphic in nativeArea.GetComponentsInChildren<Graphic>(true))
                File.AppendAllText(Path.Combine(_output, "native-pointer.log"), graphic.name + " enabled=" + graphic.enabled + " ray=" + graphic.raycastTarget + " depth=" + graphic.depth + " active=" + graphic.gameObject.activeInHierarchy + " cull=" + graphic.canvasRenderer.cull + " cullTransparent=" + graphic.canvasRenderer.cullTransparentMesh + " pointInside=" + RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, pointer.position, _camera) + " raycast=" + graphic.Raycast(pointer.position, _camera) + "\n");
        }
        Check(hit.gameObject != null, "original rendered pooled enhancement area receives the real laser projection");
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }
    private static void RenderNativeOverlay635(Transform owner, string filename)
    {
        int previous = _camera.cullingMask; _camera.cullingMask = -1;
        var target = new RenderTexture(1024, 1024, 24); target.Create();
        var image = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
        _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(_output, filename), image.EncodeToPNG());
        RenderTexture.active = null; _camera.targetTexture = null; _camera.cullingMask = previous;
        Object.DestroyImmediate(image); Object.DestroyImmediate(target);
    }
}
