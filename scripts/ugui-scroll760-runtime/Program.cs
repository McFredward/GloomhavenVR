using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using Object = UnityEngine.Object;

public sealed class Scroll760Override : EventTrigger
{
    public int Calls;
    public override void OnScroll(PointerEventData data) { Calls++; }
}
public sealed class Scroll760Handler : MonoBehaviour, IScrollHandler
{
    public int Calls;
    public void OnScroll(PointerEventData data) { Calls++; }
}
public static class UguiScroll760Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static RectTransform Rect(string name, Transform? parent, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
    }
    private static Image Picture(string name, Transform parent, Vector2 size)
    { var rect = Rect(name, parent, size); return rect.gameObject.AddComponent<Image>(); }
    private static EventTrigger HoverAdapter(GameObject go, Action? entered = null)
    {
        var trigger = go.AddComponent<EventTrigger>();
        foreach (EventTriggerType type in new[] { EventTriggerType.PointerEnter, EventTriggerType.PointerExit, EventTriggerType.Select, EventTriggerType.Deselect })
        {
            var entry = new EventTrigger.Entry { eventID = type };
            if (type == EventTriggerType.PointerEnter && entered != null) entry.callback.AddListener(_ => entered());
            trigger.triggers.Add(entry);
        }
        return trigger;
    }
    private static void Bind(UguiPointer pointer, GraphicRaycaster raycaster, Camera camera, RectTransform target)
    {
        var data = new PointerEventData(EventSystem.current) { position = camera.WorldToScreenPoint(target.position) };
        var hits = new List<RaycastResult>(); raycaster.Raycast(data, hits);
        Check(hits.Count > 0 && hits[0].gameObject == target.gameObject, "actual original raycaster resolves the pointed card/list graphic");
        pointer.SetFixtureHover(hits[0].gameObject);
    }
    private static void Reset(ScrollRect scroll)
    { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; Canvas.ForceUpdateCanvases(); }
    private static bool Moved(ScrollRect scroll) => scroll.verticalNormalizedPosition < .999f;

    public static IEnumerator Run(Action<int> report)
    {
        checks = 0;
        new GameObject("EventSystem", typeof(EventSystem));
        var camera = new GameObject("Native UI event camera", typeof(Camera)).GetComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 200f;
        camera.transform.position = new Vector3(0, 0, -1000); camera.farClipPlane = 2000;
        var root = Rect("Starting-card overview", null, new Vector2(600, 400));
        var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        var raycaster = root.gameObject.AddComponent<GraphicRaycaster>();
        var view = Picture("Viewport", root, new Vector2(400, 200));
        var content = Rect("Card list", view.transform, new Vector2(400, 1000));
        content.anchorMin = content.anchorMax = new Vector2(.5f, 1f); content.pivot = new Vector2(.5f, 1f);
        var scroll = view.gameObject.AddComponent<ScrollRect>(); scroll.content = content;
        scroll.viewport = view.rectTransform; scroll.horizontal = false; scroll.vertical = true; scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
        var full = Rect("Full", content, new Vector2(180, 160));
        full.anchorMin = full.anchorMax = new Vector2(.5f, 1f); full.anchoredPosition = new Vector2(0, -90);
        int enters = 0; var nativeHover = HoverAdapter(full.gameObject, () => enters++);
        var top = Picture("Top button", full, new Vector2(160, 100)); HoverAdapter(top.gameObject);
        var label = Picture("Text Container", top.transform, new Vector2(100, 50));
        var gap = Picture("Gap", view.transform, new Vector2(50, 50)); gap.rectTransform.anchoredPosition = new Vector2(170, 0);
        var pointer = new UguiPointer(); var hand = new VRHand { Side = HandSide.Right, Thumbstick = new Vector2(0, -1) };
        var driver = new RayUguiDriver(hand, pointer);
        yield return null; Canvas.ForceUpdateCanvases(); Reset(scroll);
        Bind(pointer, raycaster, camera, gap.rectTransform); driver.TickFixtureScroll();
        Check(Moved(scroll), "list gap scrolls through original ScrollRect");
        Reset(scroll); Bind(pointer, raycaster, camera, label.rectTransform); driver.TickFixtureScroll();
        Check(Moved(scroll), "card descendants scroll past the native pointer-only EventTriggers");
        Check(pointer.FixtureScrollDelta == Vector2.zero, "wheel delta is cleared after dispatch");
        Check(UiScrollFocus.IsScrolling(hand), "real list retains pointing-hand stick ownership");
        Check(!UiScrollFocus.IsScrolling(new VRHand { Side = HandSide.Left }), "other hand does not inherit list ownership");
        ExecuteEvents.Execute(full.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        Check(enters == 1 && nativeHover.triggers.Count == 4, "original hover adapter and entries remain unchanged");
        int realTriggerCalls = 0; var real = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
        real.callback.AddListener(_ => realTriggerCalls++); nativeHover.triggers.Add(real);
        Reset(scroll); driver.TickFixtureScroll();
        Check(realTriggerCalls == 1 && !Moved(scroll), "explicit EventTrigger Scroll entry retains nearest-owner priority");
        nativeHover.triggers.Remove(real);
        var derived = top.gameObject.AddComponent<Scroll760Override>();
        Reset(scroll); driver.TickFixtureScroll();
        Check(derived.Calls == 1 && !Moved(scroll), "derived EventTrigger override retains its unlisted native Scroll behavior");
        derived.enabled = false;
        var handler = top.gameObject.AddComponent<Scroll760Handler>();
        Reset(scroll); driver.TickFixtureScroll();
        Check(handler.Calls == 1 && !Moved(scroll), "custom IScrollHandler beside empty EventTrigger retains nearest-owner priority");
        handler.enabled = false;
        Reset(scroll); driver.TickFixtureScroll();
        Check(Moved(scroll) && handler.Calls == 1 && derived.Calls == 1, "disabled intermediate handlers yield to the active native list");
        Reset(scroll); scroll.verticalNormalizedPosition = .5f; Canvas.ForceUpdateCanvases();
        float restingPosition = scroll.verticalNormalizedPosition;
        hand.Thumbstick = new Vector2(0, .29f); driver.TickFixtureScroll();
        Check(Mathf.Abs(scroll.verticalNormalizedPosition - restingPosition) < .00001f, "resting stick deadzone never scrolls the native list");
        Reset(scroll);
        hand.Thumbstick = new Vector2(0, -1); scroll.enabled = false; driver.TickFixtureScroll();
        Check(!Moved(scroll), "disabled list never receives wheel delivery"); scroll.enabled = true;
        pointer.SetFixtureHover(null); driver.TickFixtureScroll();
        Check(!Moved(scroll), "missing hover never scrolls a stale list");
        pointer.SetFixtureHover(label.gameObject);
        var innerView = Picture("Inner list", full, new Vector2(150, 150));
        var innerContent = Rect("Inner content", innerView.transform, new Vector2(150, 600));
        innerContent.anchorMin = innerContent.anchorMax = new Vector2(.5f, 1f); innerContent.pivot = new Vector2(.5f, 1f);
        var inner = innerView.gameObject.AddComponent<ScrollRect>(); inner.content = innerContent; inner.viewport = innerView.rectTransform;
        inner.horizontal = false; inner.inertia = false; inner.scrollSensitivity = 30f;
        var innerCard = Picture("Nested card", innerContent, new Vector2(100, 80));
        innerCard.rectTransform.anchorMin = innerCard.rectTransform.anchorMax = new Vector2(.5f, 1f);
        innerCard.rectTransform.anchoredPosition = new Vector2(0, -50); HoverAdapter(innerCard.gameObject);
        yield return null; Canvas.ForceUpdateCanvases(); Reset(inner); Reset(scroll);
        pointer.SetFixtureHover(innerCard.gameObject); driver.TickFixtureScroll();
        Check(Moved(inner) && !Moved(scroll), "nearest nested native ScrollRect alone receives the wheel");
        report(checks);
    }
}
