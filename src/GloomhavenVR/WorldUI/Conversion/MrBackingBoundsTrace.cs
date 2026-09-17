using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Bounded evidence for the second-open map-window background defect. This observes the
/// actual MR measurement, including its clipped extremal contributors; it changes no geometry.</summary>
internal static class MrBackingBoundsTrace
{
    private sealed class State
    {
        internal int Reports;
        internal bool Seen, Valid;
        internal Rect Last;

        internal bool Accept(bool valid, Rect bounds)
        {
            if (Reports >= 8) return false;
            float tolerance = Mathf.Max(8f, Mathf.Max(bounds.width, bounds.height) * 0.005f);
            bool moved = Math.Abs(bounds.xMin - Last.xMin) > tolerance
                || Math.Abs(bounds.xMax - Last.xMax) > tolerance
                || Math.Abs(bounds.yMin - Last.yMin) > tolerance
                || Math.Abs(bounds.yMax - Last.yMax) > tolerance;
            if (Seen && Valid == valid && (!valid || !moved)) return false;
            Seen = true; Valid = valid; Last = bounds; Reports++;
            return true;
        }
    }

    private static readonly ConditionalWeakTable<ConvertedPanel, State> States = new();

    internal static void Observe(ConvertedPanel panel, PanelInkBounds.Ink ink, bool measured)
    {
        if (!VRLog.Wants(VRLogLevel.Info)) return;
        try
        {
            State state = States.GetValue(panel, _ => new State());
            bool valid = measured && ink.Valid;
            if (!state.Accept(valid, ink.Rect)) return;
            var text = new StringBuilder(900);
            text.Append("MR BOUNDS SOURCE: target='").Append(panel.Target != null ? panel.Target.name : "<gone>")
                .Append("' instance=").Append(panel.Target != null ? panel.Target.GetInstanceID() : 0)
                .Append(" frame=").Append(Time.frameCount).Append(" sample=").Append(state.Reports)
                .Append("/8 measured=").Append(valid).Append(" graphics=").Append(ink.Graphics)
                .Append(" host=");
            AppendRect(text, panel.HostRect != null ? panel.HostRect.rect : default);
            text.Append(" painted="); AppendRect(text, ink.Rect);
            AppendExtreme(text, "TOP", ink.MrTop, ink.MrTopRect, panel.HostRect);
            AppendExtreme(text, "BOTTOM", ink.MrBottom, ink.MrBottomRect, panel.HostRect);
            AppendExtreme(text, "LEFT", ink.MrLeft, ink.MrLeftRect, panel.HostRect);
            AppendExtreme(text, "RIGHT", ink.MrRight, ink.MrRightRect, panel.HostRect);
            VRLog.Note("WorldUI", text.ToString());
        }
        catch (Exception)
        {
            // A destroyed diagnostic getter or logging failure must never turn valid geometry
            // into the outer ink walk's fallback, nor affect native continuation or visibility.
        }
    }

    private static void AppendExtreme(StringBuilder text, string edge, Graphic? graphic, Rect clipped, Transform? host)
    {
        text.Append(" | ").Append(edge).Append('=');
        if (graphic == null) { text.Append("<none>"); return; }
        text.Append(graphic.GetType().Name).Append('#').Append(graphic.GetInstanceID()).Append(" path=");
        Transform? node = graphic.transform;
        for (int depth = 0; node != null && depth < 12; depth++, node = node.parent)
        {
            if (depth > 0) text.Append(" <- ");
            text.Append(node.name);
            if (ReferenceEquals(node, host)) break;
        }
        text.Append(" clipped="); AppendRect(text, clipped);
        text.Append(" local="); AppendRect(text, graphic.rectTransform.rect);
        CanvasRenderer? renderer = graphic.canvasRenderer;
        text.Append(" colorA="); Number(text, graphic.color.a);
        text.Append(" ownA="); Number(text, renderer != null ? renderer.GetAlpha() : -1);
        text.Append(" inheritedA="); Number(text, renderer != null ? renderer.GetInheritedAlpha() : -1);
        text.Append(" culled=").Append(renderer != null && renderer.cull)
            .Append(" enabled=").Append(graphic.enabled)
            .Append(" active=").Append(graphic.gameObject.activeInHierarchy)
            .Append(" canvasEnabled=").Append(graphic.canvas != null && graphic.canvas.isActiveAndEnabled);
        if (graphic is Image image)
            text.Append(" image=").Append(image.type).Append(" sprite='")
                .Append(image.overrideSprite != null ? image.overrideSprite.name : "<none>").Append('\'');
        Material? material = graphic.material;
        text.Append(" material='").Append(material != null ? material.name : "<none>").Append('\'');
        text.Append(" shader='").Append(material != null && material.shader != null ? material.shader.name : "<none>").Append('\'');
        if (material != null && material.HasProperty("_Color"))
        { text.Append(" materialColorA="); Number(text, material.GetColor("_Color").a); }
        text.Append(" ancestry=");
        int terms = 0;
        node = graphic.transform;
        for (int depth = 0; node != null && depth < 24; depth++, node = node.parent)
        {
            CanvasGroup? group = node.GetComponent<CanvasGroup>();
            if (group != null && group.enabled)
            {
                if (terms++ > 0) text.Append(';');
                text.Append(node.name).Append("/CanvasGroup alpha="); Number(text, group.alpha);
                text.Append(" ignoreParents=").Append(group.ignoreParentGroups);
            }
            RectMask2D? rectangle = node.GetComponent<RectMask2D>();
            if (rectangle != null && rectangle.enabled)
            {
                if (terms++ > 0) text.Append(';');
                text.Append(node.name).Append("/RectMask2D");
            }
            Mask? mask = node.GetComponent<Mask>();
            if (mask != null && mask.enabled)
            {
                if (terms++ > 0) text.Append(';');
                text.Append(node.name).Append("/Mask showGraphic=").Append(mask.showMaskGraphic);
            }
            if (ReferenceEquals(node, host) || terms >= 12) break;
        }
        if (terms == 0) text.Append("<none>");
    }

    private static void AppendRect(StringBuilder text, Rect rect)
    {
        text.Append('['); Number(text, rect.xMin); text.Append(','); Number(text, rect.yMin);
        text.Append(".."); Number(text, rect.xMax); text.Append(','); Number(text, rect.yMax); text.Append(']');
    }

    private static void Number(StringBuilder text, float value) => text.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
}
