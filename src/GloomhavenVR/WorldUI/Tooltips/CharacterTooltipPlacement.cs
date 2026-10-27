using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Build730: original skin/personal-quest hints must clear the live character.
/// They occupy a right-hand annotation lane beside the fixed window; their
/// native content, show/hide authority and controller lifetime remain unchanged.
/// Identity comes from the actual serialized controllers, never object names,
/// translated text or every tooltip in a character-shaped hierarchy.
/// </summary>
internal static class CharacterTooltipPlacement
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SkinTooltip = Field(typeof(UICharacterSkinSelector), "skinTooltip");
    private static readonly FieldInfo? PartyInformation = Field(typeof(UIAdventurePartyAssemblyCharacterDisplay), "information");
    private static readonly FieldInfo? DescriptionTooltip = Field(typeof(UICampaignAssemblyCharacterInformation), "descriptionTooltip");
    private static readonly FieldInfo? QuestConcealTooltip = Field(typeof(UICampaignAssemblyCharacterInformation), "personalQuestTooltip");
    private static readonly FieldInfo? CreatorPerks = Field(typeof(UICharacterCreatorClassRoster), "perksTooltip");
    private static readonly FieldInfo? CreatorInformation = Field(typeof(UICharacterCreatorClassRoster), "information");
    private static readonly FieldInfo? LocalTarget = Field(typeof(UILocalTooltip), "target");
    private static readonly FieldInfo? QuestDescription = Field(typeof(UIPersonalQuest), "questDescription");
    private static readonly Vector3[] HostCorners = new Vector3[4], TargetCorners = new Vector3[4], HintCorners = new Vector3[4];
    private const float GapPixels = 12f;
    private sealed class NativeLocal
    {
        internal UILocalTooltip Widget = null!;
        internal readonly NativeShared Placement = new();
    }
    private static readonly List<NativeLocal> NativeLocals = new(2);
    private static readonly object ConvertedAnnotationOwner = new();
    private static readonly Dictionary<RectTransform, Annotation> Annotations = new();

    // An outboard native annotation needs the same head-space UI carrier even
    // when character depth is disabled or its model is not ready. This source
    // only leases its original plate as a routing member: no copied content,
    // new depth, material, shader, mask or callback is introduced.
    private sealed class Annotation : IMenuDepthWindowAnnotationSource
    {
        internal object Owner = null!;
        public Graphic Graphic { get; }
        public bool IsReady => true;
        public RectTransform AnnotationRect { get; }
        internal Annotation(Graphic graphic, RectTransform rect) { Graphic = graphic; AnnotationRect = rect; }
        public void BeforeCapture(MenuDepthEye eye) { }
        public void AfterCapture() { }
    }

    private static void TrackAnnotation(RectTransform rect, object owner, RectTransform? anchor)
    {
        if (Annotations.TryGetValue(rect, out Annotation? current)) { current.Owner = owner; return; }
        Graphic? plate = rect.GetComponent<Graphic>();
        // The original local quest root has layout/window components but no
        // Graphic. Its exact UIPersonalQuest.questDescription reference names
        // the original visible TMP child; never search arbitrary descendants or
        // add an invisible root Image just to make routing appear to work.
        if (plate == null && anchor != null && anchor.GetComponent<UIPersonalQuest>() is UIPersonalQuest quest
            && QuestDescription?.GetValue(quest) is Component description
            && description.transform.IsChildOf(rect))
            plate = description.GetComponent<Graphic>();
        if (plate == null) return;
        var source = new Annotation(plate, rect) { Owner = owner };
        Annotations.Add(rect, source); MenuDepthSources.Register(source);
    }

    private static void ReleaseAnnotation(RectTransform rect, object owner)
    {
        if (!Annotations.TryGetValue(rect, out Annotation? source) || !ReferenceEquals(source.Owner, owner)) return;
        MenuDepthSources.Unregister(source); Annotations.Remove(rect);
    }

    internal static void TrackConvertedAnnotation(RectTransform rect, RectTransform? anchor)
        => TrackAnnotation(rect, ConvertedAnnotationOwner, anchor);
    internal static void ReleaseConvertedAnnotation(RectTransform rect) => ReleaseAnnotation(rect, ConvertedAnnotationOwner);

    private static FieldInfo? Field(System.Type type, string name)
    {
        FieldInfo? field = type.GetField(name, Fields);
        if (field == null)
            VRLog.Alert("WorldUI", "CHARACTER TOOLTIP FIELD: " + type.Name + "." + name
                + " is unavailable; retaining the ordinary native hint placement.");
        return field;
    }

    private static bool Describes(Component? declared, RectTransform anchor) => declared != null
        && (ReferenceEquals(declared.transform, anchor) || anchor.IsChildOf(declared.transform));

    internal static bool IsSharedHint(RectTransform? anchor)
    {
        if (anchor == null) return false;
        UIAdventurePartyAssemblyCharacterDisplay? party = anchor.GetComponentInParent<UIAdventurePartyAssemblyCharacterDisplay>();
        UICharacterCreatorWindow? creator = anchor.GetComponentInParent<UICharacterCreatorWindow>();
        if (party == null && creator == null) return false;
        UICharacterSkinSelector? skin = anchor.GetComponentInParent<UICharacterSkinSelector>();
        if (skin != null && Describes(SkinTooltip?.GetValue(skin) as Component, anchor)) return true;
        if (party != null && PartyInformation?.GetValue(party) is IList information)
        {
            for (int i = 0; i < information.Count; i++)
            {
                if (information[i] is not UICampaignAssemblyCharacterInformation item) continue;
                if (Describes(DescriptionTooltip?.GetValue(item) as Component, anchor)
                    || Describes(QuestConcealTooltip?.GetValue(item) as Component, anchor)) return true;
            }
        }
        UICharacterCreatorClassRoster? roster = creator != null
            ? anchor.GetComponentInParent<UICharacterCreatorClassRoster>() : null;
        return roster != null && (Describes(CreatorPerks?.GetValue(roster) as Component, anchor)
            || Describes(CreatorInformation?.GetValue(roster) as Component, anchor));
    }

    internal static bool TryLocalHint(Component widget, out RectTransform? anchor)
    {
        anchor = widget is UILocalTooltip ? LocalTarget?.GetValue(widget) as RectTransform : null;
        // Both native campaign variants anchor the original quest explanation to
        // this exact quest-state print. Other local/item/quest-result hints stay
        // on their existing path, including their native position and callbacks.
        return anchor != null && anchor.GetComponent<UIPersonalQuestState>() != null
            && anchor.GetComponent<UIPersonalQuest>() != null
            && anchor.GetComponentInParent<UICampaignAssemblyCharacterInformation>() != null
            && anchor.GetComponentInParent<UIAdventurePartyAssemblyCharacterDisplay>() != null;
    }

    internal static void SettleNativeLocal(Component widget)
    {
        if (widget is not UILocalTooltip local || !local.IsShown) return;
        for (int i = 0; i < NativeLocals.Count; i++)
            if (ReferenceEquals(NativeLocals[i].Widget, local)) { NativeLocals[i].Placement.TickLocal(local); return; }
        if (!TryLocalHint(local, out _)) return;
        var record = new NativeLocal { Widget = local }; NativeLocals.Add(record); record.Placement.TickLocal(local);
    }

    internal static void ReleaseNativeLocal(Component widget)
    {
        for (int i = NativeLocals.Count - 1; i >= 0; i--)
            if (ReferenceEquals(NativeLocals[i].Widget, widget))
            { NativeLocals[i].Placement.Restore(); NativeLocals.RemoveAt(i); }
    }

    internal static void TickNativeLocals(bool active)
    {
        for (int i = NativeLocals.Count - 1; i >= 0; i--)
        {
            NativeLocal record = NativeLocals[i];
            if (!active || record.Widget == null || !record.Widget.IsShown
                || ModalFallback.FindOwningWindow(record.Widget.transform) != null)
            { record.Placement.Restore(); NativeLocals.RemoveAt(i); }
            else record.Placement.TickLocal(record.Widget);
        }
    }

    internal static Vector3 RightCenter(RectTransform host, Quaternion rotation, RectTransform? anchor,
        float halfWidth, float halfHeight, float proud)
        => RightCenter(host, host.rect, rotation, anchor, halfWidth, halfHeight, proud);

    private static Vector3 RightCenter(RectTransform host, Rect frame, Quaternion rotation, RectTransform? anchor,
        float halfWidth, float halfHeight, float proud)
    {
        Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;
        HostCorners[0] = host.TransformPoint(new Vector3(frame.xMin, frame.yMin));
        HostCorners[1] = host.TransformPoint(new Vector3(frame.xMin, frame.yMax));
        HostCorners[2] = host.TransformPoint(new Vector3(frame.xMax, frame.yMax));
        HostCorners[3] = host.TransformPoint(new Vector3(frame.xMax, frame.yMin));
        Vector3 center = (HostCorners[0] + HostCorners[2]) * .5f;
        float halfHostWidth = Vector3.Distance(HostCorners[0], HostCorners[3]) * .5f;
        float halfHostHeight = Vector3.Distance(HostCorners[0], HostCorners[1]) * .5f;
        float y = halfHostHeight - halfHeight;
        if (anchor != null)
        {
            anchor.GetWorldCorners(TargetCorners);
            Vector3 top = (TargetCorners[1] + TargetCorners[2]) * .5f;
            y = Vector3.Dot(top - center, up) - halfHeight;
        }
        float limit = Mathf.Max(0f, halfHostHeight - halfHeight);
        y = Mathf.Clamp(y, -limit, limit);
        float gap = GapPixels * Mathf.Max(Mathf.Abs(host.lossyScale.x), 1e-6f);
        return center + right * (halfHostWidth + gap + halfWidth) + up * y
            - rotation * Vector3.forward * proud;
    }

    internal static void PlaceLocal(RectTransform hint, RectTransform host, RectTransform? anchor)
        => Place(hint, host, host.rect, anchor, false);

    private static void Place(RectTransform hint, RectTransform host, Rect frame, RectTransform? anchor, bool preserveZ)
    {
        Quaternion rotation = host.rotation;
        Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;
        hint.GetWorldCorners(HintCorners);
        Vector3 center = (HintCorners[0] + HintCorners[2]) * .5f;
        float halfWidth = Mathf.Abs(Vector3.Dot(HintCorners[3] - HintCorners[0], right)) * .5f;
        float halfHeight = Mathf.Abs(Vector3.Dot(HintCorners[1] - HintCorners[0], up)) * .5f;
        Vector3 wanted = RightCenter(host, frame, rotation, anchor, halfWidth, halfHeight, 0f);
        Vector3 delta = wanted - center;
        if (Mathf.Abs(Vector3.Dot(delta, right)) <= 1e-4f && Mathf.Abs(Vector3.Dot(delta, up)) <= 1e-4f) return;
        Vector3 local = hint.parent != null ? hint.parent.InverseTransformVector(delta) : delta;
        Vector3 position = hint.localPosition;
        hint.localPosition = new Vector3(position.x + local.x, position.y + local.y, preserveZ ? position.z : 0f);
    }

    /// <summary>
    /// The native FlatScreen still owns its original canvases, controller and
    /// tooltip. Only these exact annotations move beside its fixed full-UI
    /// frame. The head capture carries their ink beyond that original frame;
    /// neither the physical quad nor the source Canvas is resized or reparented.
    /// </summary>
    internal sealed class NativeShared
    {
        private RectTransform? _anchor, _hint;
        private Transform? _anchorParent, _hintParent;
        private Canvas? _source;
        private bool _recognized, _positionHeld;
        private Vector3 _nativePosition, _appliedPosition;

        internal void Tick(Canvas canvas, UITooltip tooltip)
        {
            RectTransform? anchor = tooltip.m_AnchorToTarget;
            if (anchor == null || !tooltip.IsActive() || tooltip.alpha <= 0f)
            { Restore(); return; }
            if (!ReferenceEquals(anchor, _anchor) || !ReferenceEquals(anchor.parent, _anchorParent))
            {
                Restore();
                ResolveAnchor(anchor, IsSharedHint(anchor));
            }
            if (tooltip.transform is RectTransform hint) Seat(canvas, anchor, hint);
        }

        internal void TickLocal(UILocalTooltip tooltip)
        {
            RectTransform? anchor = LocalTarget?.GetValue(tooltip) as RectTransform;
            if (anchor == null || !tooltip.IsShown) { Restore(); return; }
            if (!ReferenceEquals(anchor, _anchor) || !ReferenceEquals(anchor.parent, _anchorParent))
            {
                Restore();
                ResolveAnchor(anchor, TryLocalHint(tooltip, out _));
            }
            if (_source != null && tooltip.transform is RectTransform hint) Seat(_source, anchor, hint);
            else ReleasePosition();
        }

        private void ResolveAnchor(RectTransform anchor, bool recognized)
        {
            _anchor = anchor; _anchorParent = anchor.parent; _recognized = recognized;
            Canvas? source = anchor.GetComponentInParent<Canvas>();
            _source = source != null ? source.rootCanvas : null;
        }

        private void Seat(Canvas canvas, RectTransform anchor, RectTransform hint)
        {
            Canvas? root = _source;
            Camera? camera = root != null ? root.worldCamera : null;
            if (!_recognized || root == null || !root.isActiveAndEnabled
                || root.renderMode != RenderMode.ScreenSpaceCamera || camera == null || !camera.orthographic
                || !camera.isActiveAndEnabled || !FlatScreen.IsCaptured(camera)
                || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceCamera
                || canvas.worldCamera != camera || !anchor.IsChildOf(root.transform)
                || root.transform is not RectTransform frame)
            { ReleasePosition(); return; }

            Rect viewport = camera.rect;
            if (viewport.width <= 0f || viewport.height <= 0f) { ReleasePosition(); return; }
            // The quad represents the complete UI target, not merely this
            // camera's authored viewport or the character window's sub-rect.
            // Read the original camera basis here, outside the temporary head
            // capture lease. No source or screen geometry is written.
            float depth = camera.WorldToViewportPoint(frame.TransformPoint(frame.rect.center)).z;
            Vector3 lower = frame.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(
                -viewport.x / viewport.width, -viewport.y / viewport.height, depth)));
            Vector3 upper = frame.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(
                (1f - viewport.x) / viewport.width, (1f - viewport.y) / viewport.height, depth)));
            Rect fullFrame = Rect.MinMaxRect(Mathf.Min(lower.x, upper.x), Mathf.Min(lower.y, upper.y),
                Mathf.Max(lower.x, upper.x), Mathf.Max(lower.y, upper.y));
            if (!MenuDepthProjection.Finite(lower) || !MenuDepthProjection.Finite(upper))
            { ReleasePosition(); return; }

            if (!ReferenceEquals(_hint, hint) || !ReferenceEquals(_hintParent, hint.parent))
            { ReleasePosition(); _hint = hint; _hintParent = hint.parent; }
            Vector3 position = hint.localPosition;
            if (!_positionHeld || !SameXY(position, _appliedPosition)) _nativePosition = position;
            Place(hint, frame, fullFrame, anchor, true);
            _appliedPosition = hint.localPosition; _positionHeld = true;
            TrackAnnotation(hint, this, anchor);
        }

        private static bool SameXY(Vector3 left, Vector3 right) =>
            Mathf.Abs(left.x - right.x) <= 1e-4f && Mathf.Abs(left.y - right.y) <= 1e-4f;

        private void ReleasePosition()
        {
            if (!ReferenceEquals(_hint, null)) ReleaseAnnotation(_hint, this);
            // A native placement, pooled reparent or new owner wins immediately.
            // Only hand back the XY value that is still our own last write.
            if (_positionHeld && _hint != null && _hint.parent == _hintParent
                && SameXY(_hint.localPosition, _appliedPosition))
                _hint.localPosition = new Vector3(_nativePosition.x, _nativePosition.y, _hint.localPosition.z);
            _positionHeld = false; _hint = null; _hintParent = null;
        }

        internal void Restore()
        {
            ReleasePosition(); _anchor = null; _anchorParent = null; _source = null; _recognized = false;
        }
    }
}
