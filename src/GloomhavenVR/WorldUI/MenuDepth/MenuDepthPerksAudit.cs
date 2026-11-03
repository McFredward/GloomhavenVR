using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Bounded provenance for the reported out-of-row perk modifier. Never changes native widgets.</summary>
internal static class MenuDepthPerksAudit
{
    private sealed class Visit
    {
        internal readonly UIPerksWindow Window;
        internal readonly int Started;
        internal int Samples;
        internal Visit(UIPerksWindow window) { Window = window; Started = Time.frameCount; }
    }
    private static readonly List<Visit> Pending = new(2);
    private static readonly List<UIPerkAttackModifier> Cards = new(32);
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly FieldInfo? Conditional = AccessTools.Field(typeof(UIPerksActiveModifiers), "conditionalPool");
    private static readonly FieldInfo? Additional = AccessTools.Field(typeof(UIPerksActiveModifiers), "additionalModifiersPool");
    private static readonly FieldInfo? Content = AccessTools.Field(typeof(UIAttackModifier<UIPerkAttackModifierCounter>), "content");
    private static int _openings;

    internal static void Observe(UIPerksWindow window, bool shown)
    {
        if (!VRLog.WantsDebug || window == null) return;
        for (int i = Pending.Count - 1; i >= 0; i--)
            if (Pending[i].Window == null || Pending[i].Window == window) Pending.RemoveAt(i);
        if (shown && _openings++ < 6) Pending.Add(new Visit(window));
    }

    internal static void Tick()
    {
        if (!VRLog.WantsDebug) { Pending.Clear(); return; }
        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            Visit visit = Pending[i];
            if (visit.Window == null || !visit.Window.gameObject.activeInHierarchy || !visit.Window.IsVisible)
            { Pending.RemoveAt(i); continue; }
            int elapsed = Time.frameCount - visit.Started;
            if (elapsed < (visit.Samples == 0 ? 2 : 30)) continue;
            Report(visit.Window, ++visit.Samples);
            if (visit.Samples == 2) Pending.RemoveAt(i);
        }
    }

    private static void Report(UIPerksWindow window, int sample)
    {
        // Show/Hide supply one exact native window. No scene census and no stable
        // per-frame graphic/transform walk; two small snapshots per opening only.
        Cards.Clear(); window.GetComponentsInChildren(true, Cards);
        UIPerksActiveModifiers summary = window.ModifierDisplay;
        var summaryRect = summary != null ? summary.transform as RectTransform : null;
        var windowRect = window.transform as RectTransform;
        var conditional = summary != null ? Conditional?.GetValue(summary) as List<UIPerkAttackModifier> : null;
        var additional = summary != null ? Additional?.GetValue(summary) as List<UIPerkAttackModifier> : null;
        Transform tooltip = UITooltip.GetTransform(); // Native getter only reads the existing singleton.
        int active = 0, outside = 0, notes = 0;
        for (int i = 0; i < Cards.Count; i++)
        {
            UIPerkAttackModifier card = Cards[i];
            if (card == null || !card.gameObject.activeInHierarchy) continue;
            ++active;
            var rect = card.transform as RectTransform;
            bool summaryOwner = summary != null && card.transform.IsChildOf(summary.transform);
            bool tooltipOwner = tooltip != null && card.transform.IsChildOf(tooltip);
            bool outsideSummary = summaryOwner && rect != null && summaryRect != null && !Overlaps(rect, summaryRect);
            bool outsidePoolFrame = MenuDepthPerksCardClip.For(card)?.Outside() == true;
            if (summaryOwner && !outsideSummary && !outsidePoolFrame) continue;
            ++outside;
            if (notes++ >= 12) continue;
            Image backing = card.GetComponent<Image>();
            GameObject? content = Content?.GetValue(card) as GameObject;
            Vector3 local = windowRect != null ? windowRect.InverseTransformPoint(card.transform.position) : card.transform.localPosition;
            string pool = conditional?.Contains(card) == true ? "conditional" : additional?.Contains(card) == true ? "additional" : "none";
            VRLog.Debug("WorldUI", $"PERK MODIFIER PROVENANCE: sample={sample}/2; path='{Path(card.transform)}'; pool={pool}; "
                + $"summaryOwner={summaryOwner}; outsideSummary={outsideSummary}; outsidePoolFrame={outsidePoolFrame}; tooltipOwner={tooltipOwner}; modifier='{card.ModifierId}'; "
                + $"windowLocal={local}; rect={(rect != null ? rect.rect.ToString() : "none")}; contentActive={content != null && content.activeInHierarchy}; "
                + $"inkActive={backing != null && backing.isActiveAndEnabled}; culled={backing != null && backing.canvasRenderer.cull}; "
                + $"alpha={(backing != null ? backing.canvasRenderer.GetInheritedAlpha() * backing.color.a : 0f):F3}; layoutEnabled={LayoutRebuilder.Enable}.");
        }
        VRLog.Debug("WorldUI", $"PERK MODIFIER PROVENANCE SUMMARY: sample={sample}/2; window='{window.name}'; typed={Cards.Count}; active={active}; "
            + $"outOfSummary={outside}; conditional={conditional?.Count ?? -1}; additional={additional?.Count ?? -1}; untouched=true.");
        Cards.Clear();
    }

    private static bool Overlaps(RectTransform card, RectTransform scope)
    {
        card.GetWorldCorners(Corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < Corners.Length; i++)
        { Vector3 point = scope.InverseTransformPoint(Corners[i]); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return scope.rect.Overlaps(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
    }

    private static string Path(Transform value)
    {
        string path = value.name;
        for (int i = 0; i < 8 && value.parent != null; i++) { value = value.parent; path = value.name + "/" + path; }
        return path;
    }

    internal static void Shutdown() { Pending.Clear(); Cards.Clear(); _openings = 0; }
}
